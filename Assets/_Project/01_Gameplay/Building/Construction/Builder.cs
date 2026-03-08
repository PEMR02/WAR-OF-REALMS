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
        public float buildRangeWorldFallback = 2.5f;

        NavMeshAgent _agent;
        UnitMover _mover;
        VillagerGatherer _gatherer;
        BuildSite _target;
        readonly List<BuildSite> _buildQueue = new List<BuildSite>();
        float _defaultStoppingDistance;

       void Awake()
		{
			_agent = GetComponent<NavMeshAgent>();
			_mover = GetComponent<UnitMover>();
            _gatherer = GetComponent<VillagerGatherer>();
            _defaultStoppingDistance = _agent != null ? _agent.stoppingDistance : 0.5f;
		}

        /// <summary>Asigna un solar para construir. Si se pasa otro mientras ya tiene uno, se encola (cola de construcción).</summary>
        public void SetBuildTarget(BuildSite site)
        {
			if (_gatherer != null) _gatherer.PauseGatherKeepCarried();

            if (site == null)
            {
                ClearCurrentTarget();
                _buildQueue.Clear();
                if (_agent != null) _agent.stoppingDistance = _defaultStoppingDistance;
                return;
            }

            if (_target == site) return;
            if (!_buildQueue.Contains(site)) _buildQueue.Add(site);

            if (_target == null)
                StartNextInQueue();
        }

        void ClearCurrentTarget()
        {
            if (_target != null)
            {
                _target.Unregister(this);
                _target = null;
            }
            if (_agent != null) _agent.stoppingDistance = _defaultStoppingDistance;
        }

        void StartNextInQueue()
        {
            ClearCurrentTarget();
            if (_buildQueue.Count == 0) return;

            _target = _buildQueue[0];
            _buildQueue.RemoveAt(0);
            if (_target == null) { StartNextInQueue(); return; }

            _target.Register(this);
            if (_agent != null) _agent.stoppingDistance = 0.2f;
            Vector3 dest = GetBuildSiteApproachPoint(_target, transform.position, buildRangeCells);
            MoveTo(dest);
        }

        public void ClearBuildTargetIfThis(BuildSite site)
        {
            _buildQueue.RemoveAll(s => s == site);
            if (_target != site) return;
            StartNextInQueue();
        }

        /// <summary>True si tiene un edificio asignado para construir (actual o en cola).</summary>
        public bool HasBuildTarget => _target != null || _buildQueue.Count > 0;

        void Update()
        {
            if (_target == null) return;
            if (_target.IsCompleted)
            {
                StartNextInQueue();
                return;
            }

            Vector3 a = transform.position; a.y = 0f;
            Vector3 buildPoint = GetBuildSiteClosestPoint(_target, transform.position);
            Vector3 b = buildPoint; b.y = 0f;
            float dist = Vector3.Distance(a, b);
            float maxRange = (MapGrid.Instance != null && MapGrid.Instance.IsReady)
                ? (buildRangeCells * MapGrid.Instance.cellSize)
                : buildRangeWorldFallback;

            if (dist > maxRange)
            {
                if (!_agent.pathPending && (!_agent.hasPath || _agent.remainingDistance <= _agent.stoppingDistance + 0.15f))
                {
                    Vector3 approachPoint = GetBuildSiteApproachPoint(_target, transform.position, buildRangeCells);
                    MoveTo(approachPoint);
                }
                return;
            }

            if (_agent != null && _agent.isOnNavMesh && _agent.hasPath)
                _agent.ResetPath();
            _target.AddWorkSeconds(buildPower * Time.deltaTime);
        }

        /// <summary>
        /// Calcula el centro del rectángulo de celdas ocupadas en espacio mundo,
        /// usando la misma lógica que OccupyCellsOnStart (WorldToCell con Floor + división entera).
        /// Esto garantiza que la huella usada para medir distancias coincide exactamente con las celdas marcadas ocupadas.
        /// </summary>
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
        static Vector3 GetBuildSiteClosestPoint(BuildSite site, Vector3 fromPosition)
        {
            if (site == null) return Vector3.zero;

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
                return new Vector3(px, site.transform.position.y, pz);
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
        static Vector3 GetBuildSiteApproachPoint(BuildSite site, Vector3 fromPosition, float rangeCells)
        {
            if (site == null) return fromPosition;
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
            approach.y = site.transform.position.y;
            return approach;
        }

        void MoveTo(Vector3 pos)
        {
			if (_mover != null) _mover.MoveTo(pos);
			else _agent.SetDestination(pos);
        }
    }
}
