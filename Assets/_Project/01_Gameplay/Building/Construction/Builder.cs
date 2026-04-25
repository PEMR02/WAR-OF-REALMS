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
        [Header("Diagnóstico runtime muro (temporal)")]
        [Tooltip("Logs [WallBuildDbg] para muro compuesto: este aldeano o si está seleccionado.")]
        [SerializeField] bool debugWallBuildRuntime = false;
        [Tooltip("Mínimo tiempo entre líneas [WallBuildDbg] por aldeano.")]
        [SerializeField] [Min(0.05f)] float wallBuildDebugLogInterval = 0.35f;
        float _wallBuildDebugLastLogUnscaled;
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
                if (!TryGetBuildSiteApproachWorldStatic(_target, transform.position, buildRangeCells, this, out Vector3 dest))
                {
                    WallBuildDbgLog($"SetBuildTarget same-site: no navigable compound approach, skip MoveTo builder={name} site={_target.name}", ignoreThrottle: true);
                    return;
                }
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
            if (!TryGetBuildSiteApproachWorldStatic(_target, transform.position, buildRangeCells, this, out Vector3 dest))
            {
                if (constructionOrderDebug)
                    Debug.Log($"[BuilderOrder] {name}: BuildSite activo '{_target.name}' sin approach NavMesh (esperar) motivo={queueAdvanceReason ?? "StartNextInQueue"}", this);
                return;
            }
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

        /// <summary>True si debe imprimir trazas <c>[WallBuildDbg]</c> (bool en prefab o unidad seleccionada).</summary>
        public bool ShouldWallBuildRuntimeLog() =>
            debugWallBuildRuntime || WallBuildRuntimeDebug.IsBuilderInCurrentSelection(this);

        void WallBuildDbgLog(string details, bool ignoreThrottle = false)
        {
            if (_target == null || !_target.IsCompoundPathBuilding || !ShouldWallBuildRuntimeLog())
                return;
            float t = Time.unscaledTime;
            if (!ignoreThrottle && t - _wallBuildDebugLastLogUnscaled < wallBuildDebugLogInterval)
                return;
            if (!ignoreThrottle)
                _wallBuildDebugLastLogUnscaled = t;
            Debug.Log($"[WallBuildDbg] {details}", this);
        }

        /// <summary>Intervalo entre re-approach forzados cuando el aldeano está fuera de rango y no avanza.</summary>
        const float OutOfRangeRepathInterval = 0.6f;
        /// <summary>Backoff temporal cuando un re-approach se queda sin path inmediatamente (evita ping-pong).</summary>
        const float OutOfRangeNoPathBackoff = 1.8f;
        float _lastOutOfRangeRepathTime;
        float _outOfRangeRepathBlockedUntil;

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

            Vector3 approachPoint = Vector3.zero;
            if (_target.IsCompoundPathBuilding)
                TryGetBuildSiteApproachWorldStatic(_target, transform.position, buildRangeCells, this, out approachPoint);
            bool compound = _target.IsCompoundPathBuilding;
            string wallBranch = "";

            if (dist > maxRange)
            {
                if (_target.IsCompoundPathBuilding && _mover != null && _mover.IsGateTransitionActive)
                {
                    if (Time.time - _compoundGateStuckLastRecover >= CompoundGateStuckRepathInterval)
                    {
                        _compoundGateStuckLastRecover = Time.time;
                        wallBranch = "MoveTo approach (gate stuck interval)";
                        if (TryGetBuildSiteApproachWorldStatic(_target, transform.position, buildRangeCells, this, out Vector3 dGate))
                            MoveTo(dGate);
                        else
                            wallBranch = "skip MoveTo (no navigable approach, gate stuck)";
                    }
                    else
                        wallBranch = "wait (gate transition active)";
                    WallBuildDbgLog($"builder={name} target={_target.name} compoundPath={compound} soCompound={(_target.buildingSO != null && _target.buildingSO.isCompound)} pos={transform.position} buildRangeCells={buildRangeCells} dist={dist:F3} maxRange={maxRange:F3} workPt={buildPoint} approachPt={approachPoint} inRange=False branch={wallBranch} moverGate={(_mover != null && _mover.IsGateTransitionActive)}");
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

                if (shouldRepath
                    && Time.time >= _outOfRangeRepathBlockedUntil
                    && Time.time - _lastOutOfRangeRepathTime >= OutOfRangeRepathInterval)
                {
                    _lastOutOfRangeRepathTime = Time.time;
                    if (_agent != null && _agent.isOnNavMesh)
                        _agent.ResetPath();
                    wallBranch = "MoveTo approach (out of range repath)";
                    if (TryGetBuildSiteApproachWorldStatic(_target, transform.position, buildRangeCells, this, out Vector3 dRep))
                    {
                        MoveTo(dRep);
                        if (_agent != null && _agent.isOnNavMesh && !_agent.pathPending && !_agent.hasPath)
                        {
                            _outOfRangeRepathBlockedUntil = Time.time + OutOfRangeNoPathBackoff;
                            wallBranch = "MoveTo approach (out of range repath, immediate no-path backoff)";
                            WallBuildDbgLog($"Repath immediate no-path: builder={name} blockFor={OutOfRangeNoPathBackoff:F2}s dest={dRep}", ignoreThrottle: true);
                        }
                    }
                    else
                        wallBranch = "skip MoveTo (no navigable approach, out of range)";
                }
                else
                    wallBranch = Time.time < _outOfRangeRepathBlockedUntil
                        ? "wait (out of range, repath backoff)"
                        : "wait (out of range, no repath yet)";
                WallBuildDbgLog($"builder={name} target={_target.name} compoundPath={compound} soCompound={(_target.buildingSO != null && _target.buildingSO.isCompound)} pos={transform.position} buildRangeCells={buildRangeCells} dist={dist:F3} maxRange={maxRange:F3} workPt={buildPoint} approachPt={approachPoint} inRange=False branch={wallBranch} shouldRepath={shouldRepath}");
                return;
            }

            // --- En rango: construir ---
            _lastDistToBuildPoint = float.MaxValue;
            _compoundGateStuckLastRecover = Time.time;
            if (_agent != null && _agent.isOnNavMesh && _agent.hasPath)
                _agent.ResetPath();

            wallBranch = "AddWorkSeconds";
            WallBuildDbgLog($"builder={name} target={_target.name} compoundPath={compound} soCompound={(_target.buildingSO != null && _target.buildingSO.isCompound)} pos={transform.position} buildRangeCells={buildRangeCells} dist={dist:F3} maxRange={maxRange:F3} workPt={buildPoint} approachPt={approachPoint} inRange=True branch={wallBranch}");

            BuildSite targetBeforeWork = _target;
            targetBeforeWork.AddWorkSeconds(buildPower * Time.deltaTime, this);
            bool targetDestroyedOrReplaced = this == null || targetBeforeWork == null || _target == null || _target != targetBeforeWork;
            if (targetDestroyedOrReplaced || (_target != null && _target.IsCompleted))
            {
                if (constructionOrderDebug)
                    Debug.Log($"[BuilderOrder] update aborted after completion targetDestroyed={(_target == null)} builder={name}", this);
                if (compound)
                    WallBuildDbgLog($"post AddWorkSeconds: targetValid={(_target != null && _target == targetBeforeWork && !_target.IsCompleted)} destroyedOrReplaced={targetDestroyedOrReplaced} builder={name}", ignoreThrottle: true);
                return;
            }

            // Tras aportar trabajo, el tramo donde estaba pudo completarse → caminar al siguiente.
            if (_target != null && !_target.IsCompleted && _target.IsCompoundPathBuilding)
            {
                Vector3 newBuildPt = _target.GetClosestPointOnActiveCompoundSegment(transform.position, this);
                Vector3 nb = newBuildPt; nb.y = 0f;
                if (Vector3.Distance(a, nb) > maxRange)
                {
                    WallBuildDbgLog($"branch=MoveTo approach (post work, next segment out of range) distToNewWork={Vector3.Distance(a, nb):F3} maxRange={maxRange:F3} builder={name}");
                    if (TryGetBuildSiteApproachWorldStatic(_target, transform.position, buildRangeCells, this, out Vector3 dPost))
                        MoveTo(dPost);
                }
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

        /// <summary>
        /// Destino de approach para movimiento. Muro compuesto: solo si hay proyección NavMesh válida.
        /// </summary>
        static bool TryGetBuildSiteApproachWorldStatic(BuildSite site, Vector3 fromPosition, float rangeCells, Builder builder, out Vector3 approachWorld)
        {
            approachWorld = fromPosition;
            if (site == null) return false;
            if (site.IsCompoundPathBuilding)
                return site.TryGetNavigableApproachForBuilder(builder, fromPosition, out approachWorld);
            approachWorld = GetBuildSiteApproachPointNonCompound(site, fromPosition, rangeCells);
            return true;
        }

        static Vector3 GetBuildSiteApproachPointNonCompound(BuildSite site, Vector3 fromPosition, float rangeCells)
        {
            Vector3 center = GetGridAlignedCenter(site);
            Vector3 onEdge = GetBuildSiteClosestPoint(site, fromPosition);
            float cs = MapGrid.Instance != null && MapGrid.Instance.IsReady ? MapGrid.Instance.cellSize : 2.5f;
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

        /// <summary>Punto donde la unidad debe ir: justo fuera del borde de la huella, en dirección al aldeano. Así el destino está en celda libre y A* no los deja lejos.</summary>
        static Vector3 GetBuildSiteApproachPoint(BuildSite site, Vector3 fromPosition, float rangeCells, Builder builder = null)
        {
            if (site == null) return fromPosition;
            if (TryGetBuildSiteApproachWorldStatic(site, fromPosition, rangeCells, builder, out Vector3 w))
                return w;
            return site.transform.position;
        }

        void MoveTo(Vector3 pos)
        {
            WallBuildDbgLog($"MoveTo pos={pos} builder={name} target={(_target != null ? _target.name : "<null>")} (compound approach / reposition)");
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

        public BuildSite CurrentBuildSite => _target;
    }
}
