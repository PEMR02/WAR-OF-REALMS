using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Buildings;
using Project.Gameplay.Map;

namespace Project.Gameplay.Units
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class Builder : MonoBehaviour
    {
        public float buildPower = 1f;
        [Tooltip("Rango de construcción en celdas: el aldeano debe estar a esta distancia del borde de la huella (ej. 1 = una celda).")]
        [Min(0.25f)]
        public float buildRangeCells = 1f;
        [Tooltip("Fallback en metros cuando MapGrid no está disponible (solo en editor o inicio).")]
        public float buildRangeWorldFallback = 5f;

        NavMeshAgent _agent;
        UnitMover _mover;
        VillagerGatherer _gatherer;
        BuildSite _target;
        readonly List<BuildSite> _buildQueue = new List<BuildSite>();
        [Header("Debug (temporal)")]
        [SerializeField] bool constructionOrderDebug = false;
        float _defaultStoppingDistance;
        float _lastDistToBuildPoint = float.MaxValue;
        float _lastStuckCheckTime;
        const float StuckCheckInterval = 1f;
        const float StuckDistEpsilon = 0.15f;
        /// <summary>Muro compuesto + puerta: aunque IsGateTransitionActive bloquee repaths agresivos, reintentar a intervalos para no quedar congelado.</summary>
        const float CompoundGateStuckRepathInterval = 5f;
        /// <summary>Solo rama sin UnitMover: buscar NavMesh cerca del aldeano antes de Warp (orden típico ~puertas en el proyecto).</summary>
        const float MoveToReanchorSampleRadius = 4f;
        float _compoundGateStuckLastRecover;

       void Awake()
		{
			_agent = GetComponent<NavMeshAgent>();
			_mover = GetComponent<UnitMover>();
            _gatherer = GetComponent<VillagerGatherer>();
            _defaultStoppingDistance = _agent != null ? _agent.stoppingDistance : 0.5f;
		}

        /// <summary>Asigna un solar para construir. Si se pasa otro mientras ya tiene uno, se encola (cola de construcción).</summary>
        /// <param name="debugOrderReason">Solo si <see cref="constructionOrderDebug"/>: motivo en logs al asignar o limpiar.</param>
        public void SetBuildTarget(BuildSite site, string debugOrderReason = null)
        {
			if (_gatherer != null) _gatherer.PauseGatherKeepCarried();

            if (site == null)
            {
                ClearCurrentTarget(debugOrderReason ?? "SetBuildTarget(null)");
                _buildQueue.Clear();
                if (_agent != null) _agent.stoppingDistance = _defaultStoppingDistance;
                return;
            }

            // Mismo solar otra vez (p. ej. clic tras asignación del placer): antes se hacía return y no se
            // recalculaba approach ni MoveTo → algunos aldeanos no marchaban al tramo/slot actual.
            if (_target == site)
            {
                if (_agent != null) _agent.stoppingDistance = 0.2f;
                Vector3 dest = GetBuildSiteApproachPoint(_target, transform.position, buildRangeCells, this);
                _lastDistToBuildPoint = float.MaxValue;
                _lastStuckCheckTime = Time.time;
                MoveTo(dest);
                return;
            }

            if (!_buildQueue.Contains(site)) _buildQueue.Add(site);

            if (_target == null)
                StartNextInQueue(debugOrderReason ?? "SetBuildTarget");
            else if (constructionOrderDebug)
                Debug.Log($"[BuilderOrder] {name}: encolado '{site.name}' (activo '{_target.name}') motivo={debugOrderReason ?? "SetBuildTarget"}", this);
        }

        void ClearCurrentTarget(string debugLostReason = null)
        {
            if (_target != null)
            {
                if (constructionOrderDebug)
                    Debug.Log($"[BuilderOrder] {name}: perdió BuildSite '{_target.name}' motivo={debugLostReason ?? "ClearCurrentTarget"}", this);
                _target.Unregister(this);
                _target = null;
            }
            if (_agent != null) _agent.stoppingDistance = _defaultStoppingDistance;
        }

        void StartNextInQueue(string queueAdvanceReason = null)
        {
            ClearCurrentTarget(queueAdvanceReason);
            if (_buildQueue.Count == 0) return;

            _target = _buildQueue[0];
            _buildQueue.RemoveAt(0);
            if (_target == null) { StartNextInQueue(queueAdvanceReason); return; }

            _target.Register(this);
            if (_agent != null) _agent.stoppingDistance = 0.2f;
            Vector3 dest = GetBuildSiteApproachPoint(_target, transform.position, buildRangeCells, this);
            if (constructionOrderDebug)
                Debug.Log($"[BuilderOrder] {name}: BuildSite activo '{_target.name}' motivo={queueAdvanceReason ?? "StartNextInQueue"}", this);
            MoveTo(dest);
        }

        public void ClearBuildTargetIfThis(BuildSite site)
        {
            _buildQueue.RemoveAll(s => s == site);
            if (_target != site) return;
            StartNextInQueue("ClearBuildTargetIfThis");
        }

        /// <summary>True si tiene un edificio asignado para construir (actual o en cola).</summary>
        public bool HasBuildTarget => _target != null || _buildQueue.Count > 0;

        /// <summary>Intervalo entre re-approach forzados cuando el aldeano está fuera de rango y no avanza.</summary>
        const float OutOfRangeRepathInterval = 0.6f;
        float _lastOutOfRangeRepathTime;

        void Update()
        {
            if (_target == null) return;
            if (_target.IsCompleted)
            {
                StartNextInQueue("BuildSite.IsCompleted");
                return;
            }

            // Forzar stoppingDistance bajo mientras construye; UnitMover.ApplyMinStoppingDistance lo sube a 0.4.
            if (_agent != null && _agent.stoppingDistance > 0.25f)
                _agent.stoppingDistance = 0.2f;

            Vector3 a = transform.position; a.y = 0f;
            Vector3 buildPoint = _target.IsCompoundPathBuilding
                ? _target.GetClosestPointOnActiveCompoundSegment(transform.position, this)
                : GetBuildSiteClosestPoint(_target, transform.position, this);
            Vector3 b = buildPoint; b.y = 0f;
            float dist = Vector3.Distance(a, b);
            float cellSize = MapGrid.Instance != null && MapGrid.Instance.IsReady ? MapGrid.Instance.cellSize : 2.5f;
            float maxRange = _target.IsCompoundPathBuilding
                ? (cellSize * Mathf.Max(2.5f, 1.5f + buildRangeCells * 0.5f))
                : ((MapGrid.Instance != null && MapGrid.Instance.IsReady) ? (buildRangeCells * cellSize) : buildRangeWorldFallback);

            if (dist > maxRange)
            {
                if (_target.IsCompoundPathBuilding && _mover != null && _mover.IsGateTransitionActive)
                {
                    if (Time.time - _compoundGateStuckLastRecover >= CompoundGateStuckRepathInterval)
                    {
                        _compoundGateStuckLastRecover = Time.time;
                        MoveTo(GetBuildSiteApproachPoint(_target, transform.position, buildRangeCells, this));
                    }
                    return;
                }

                // Cada OutOfRangeRepathInterval, si no avanza o no tiene path, re-approach.
                bool shouldRepath = false;
                if (Time.time - _lastStuckCheckTime >= StuckCheckInterval)
                {
                    bool notGettingCloser = dist >= _lastDistToBuildPoint - StuckDistEpsilon;
                    if (notGettingCloser) shouldRepath = true;
                    _lastDistToBuildPoint = dist;
                    _lastStuckCheckTime = Time.time;
                }
                if (!shouldRepath && _agent != null && !_agent.pathPending && (!_agent.hasPath || _agent.remainingDistance <= _agent.stoppingDistance + 0.15f))
                    shouldRepath = true;

                if (shouldRepath && Time.time - _lastOutOfRangeRepathTime >= OutOfRangeRepathInterval)
                {
                    _lastOutOfRangeRepathTime = Time.time;
                    if (_agent != null && _agent.isOnNavMesh)
                        _agent.ResetPath();
                    MoveTo(GetBuildSiteApproachPoint(_target, transform.position, buildRangeCells, this));
                }
                return;
            }

            // --- En rango: construir ---
            _lastDistToBuildPoint = float.MaxValue;
            _compoundGateStuckLastRecover = Time.time;
            if (_agent != null && _agent.isOnNavMesh && _agent.hasPath)
                _agent.ResetPath();

            _target.AddWorkSeconds(buildPower * Time.deltaTime, this);

            // Tras aportar trabajo, el tramo donde estaba pudo completarse → caminar al siguiente.
            if (_target != null && !_target.IsCompleted && _target.IsCompoundPathBuilding)
            {
                Vector3 newBuildPt = _target.GetClosestPointOnActiveCompoundSegment(transform.position, this);
                Vector3 nb = newBuildPt; nb.y = 0f;
                if (Vector3.Distance(a, nb) > maxRange)
                    MoveTo(GetBuildSiteApproachPoint(_target, transform.position, buildRangeCells, this));
            }
        }

        /// <summary>
        /// Calcula el centro del rectángulo de celdas ocupadas en espacio mundo,
        /// usando la misma lógica que OccupyCellsOnStart (WorldToCell con Floor + división entera).
        /// Esto garantiza que la huella usada para medir distancias coincide exactamente con las celdas marcadas ocupadas.
        /// </summary>
        /// <summary>Altura válida para destinos NavMesh cerca de un solar (evita Y del pivot desalineado con el mesh).</summary>
        static float SampleWalkableYAt(Vector3 worldXZ, float fallbackY)
        {
            float probeY = fallbackY + 2f;
            var p = new Vector3(worldXZ.x, probeY, worldXZ.z);
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 14f, NavMesh.AllAreas))
                return hit.position.y;
            var terrain = Terrain.activeTerrain != null ? Terrain.activeTerrain : Object.FindFirstObjectByType<Terrain>();
            if (terrain != null)
                return terrain.SampleHeight(worldXZ) + terrain.transform.position.y;
            return fallbackY;
        }

        static Vector3 GetGridAlignedCenter(BuildSite site)
        {
            if (site.buildingSO == null || MapGrid.Instance == null || !MapGrid.Instance.IsReady)
                return site.transform.position;

            float cs = MapGrid.Instance.cellSize;
            Vector2Int size = new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt(site.buildingSO.size.x)),
                Mathf.Max(1, Mathf.RoundToInt(site.buildingSO.size.y))
            );
            Vector2Int cellCenter = MapGrid.Instance.WorldToCell(site.transform.position);
            Vector2Int min = new Vector2Int(cellCenter.x - size.x / 2, cellCenter.y - size.y / 2);

            // Centro del rectángulo de celdas en espacio mundo (igual que OccupyCellsOnStart)
            float worldCenterX = MapGrid.Instance.origin.x + (min.x + size.x * 0.5f) * cs;
            float worldCenterZ = MapGrid.Instance.origin.z + (min.y + size.y * 0.5f) * cs;
            return new Vector3(worldCenterX, site.transform.position.y, worldCenterZ);
        }

        /// <summary>Punto más cercano del BuildSite al aldeano (sobre el borde de la huella). Se usa para medir si está en rango para construir.</summary>
        static Vector3 GetBuildSiteClosestPoint(BuildSite site, Vector3 fromPosition, Builder builder = null)
        {
            if (site == null) return Vector3.zero;
            if (site.IsCompoundPathBuilding)
                return site.GetWorkPositionForBuilder(builder);

            // Prioridad 1: huella del edificio usando centro alineado a celdas (mismo cálculo que OccupyCellsOnStart)
            if (site.buildingSO != null && MapGrid.Instance != null && MapGrid.Instance.IsReady)
            {
                float cs = MapGrid.Instance.cellSize;
                Vector2Int size = new Vector2Int(
                    Mathf.Max(1, Mathf.RoundToInt(site.buildingSO.size.x)),
                    Mathf.Max(1, Mathf.RoundToInt(site.buildingSO.size.y))
                );
                Vector3 gridCenter = GetGridAlignedCenter(site);
                float hx = size.x * cs * 0.5f;
                float hz = size.y * cs * 0.5f;
                float px = Mathf.Clamp(fromPosition.x, gridCenter.x - hx, gridCenter.x + hx);
                float pz = Mathf.Clamp(fromPosition.z, gridCenter.z - hz, gridCenter.z + hz);
                float y = SampleWalkableYAt(new Vector3(px, 0f, pz), site.transform.position.y);
                return new Vector3(px, y, pz);
            }

            var col = site.GetComponentInChildren<Collider>();
            if (col != null)
            {
                Vector3 closest = col.ClosestPoint(fromPosition);
                closest.y = site.transform.position.y;
                return closest;
            }

            return site.transform.position;
        }

        /// <summary>Punto donde la unidad debe ir: justo fuera del borde de la huella, en dirección al aldeano. Así el destino está en celda libre y A* no los deja lejos.</summary>
        static Vector3 GetBuildSiteApproachPoint(BuildSite site, Vector3 fromPosition, float rangeCells, Builder builder = null)
        {
            if (site == null) return fromPosition;
            // Muro: waypoint del tramo asignado (varios aldeanos pueden trabajar en paralelo en distintos tramos).
            if (site.IsCompoundPathBuilding)
                return site.GetApproachPointForBuilder(builder);
            // Usar centro alineado a celdas para dirección y cálculo de borde (igual que OccupyCellsOnStart)
            Vector3 center = GetGridAlignedCenter(site);
            Vector3 onEdge = GetBuildSiteClosestPoint(site, fromPosition);
            float cs = MapGrid.Instance != null && MapGrid.Instance.IsReady ? MapGrid.Instance.cellSize : 2.5f;
            // Offset de exactamente media celda: coloca el destino en el centro de la celda adyacente al edificio,
            // garantizando que A* recibe una celda libre y el waypoint final coincide con el destino calculado.
            float offset = cs * 0.5f;
            Vector3 dir = fromPosition - center;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
                dir = (onEdge - center).normalized;
            else
                dir.Normalize();
            Vector3 approach = onEdge + dir * offset;
            approach.y = SampleWalkableYAt(approach, site.transform.position.y);
            return approach;
        }

        void MoveTo(Vector3 pos)
        {
			if (_mover != null)
			{
				// Evitar que UnitMover suba stoppingDistance por encima de lo que Builder necesita para acercarse al muro.
				if (_target != null && _agent != null)
					_agent.stoppingDistance = 0.2f;
				_mover.MoveTo(pos);
				if (_target != null && _agent != null)
					_agent.stoppingDistance = 0.2f;
				return;
			}

			if (_agent == null || !_agent.enabled)
				return;

			if (!_agent.isOnNavMesh)
			{
				if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, MoveToReanchorSampleRadius, NavMesh.AllAreas))
					_agent.Warp(hit.position);
			}

			if (!_agent.isOnNavMesh)
				return;

			_agent.SetDestination(pos);
        }
    }
}
