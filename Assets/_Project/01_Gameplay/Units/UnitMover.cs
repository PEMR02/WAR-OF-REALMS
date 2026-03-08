using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Map;
using Project.Gameplay.Pathfinding;

namespace Project.Gameplay.Units
{
    /// <summary>
    /// Mueve unidades usando A* sobre MapGrid para decidir la ruta,
    /// y NavMeshAgent para ejecutar el movimiento (maneja altura automáticamente).
    /// A* decide POR DÓNDE ir (evita edificios/agua); NavMesh decide CÓMO moverse.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class UnitMover : MonoBehaviour
    {
        private NavMeshAgent _agent;
        private Pathfinder _pathfinder;

        [Header("Pathfinding")]
        [Tooltip("Usar A* sobre el grid para planificar ruta. El NavMeshAgent sigue los waypoints.")]
        public bool useGridPathfinding = true;
        [Tooltip("Si true, esta unidad puede nadar (agua costo normal). Si false, evita agua.")]
        public bool canSwim = false;

        [Header("NavMesh")]
        public float snapToNavMeshRadius = 50f;

        [Header("Debug")]
        public bool debugLogs = false;

        // Ruta A*: lista de waypoints mundo que el agente sigue uno a uno
        private List<Vector3> _waypoints;
        private int _waypointIndex;
        private bool _followingAStarPath;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _pathfinder = new Pathfinder();
        }

        void Start()
        {
            // Intentar colocar en NavMesh al inicio
            Invoke(nameof(TrySnapToNavMesh), 0.5f);
        }

        // ─────────────────────────────────────────────────────────────
        //  API PÚBLICA
        // ─────────────────────────────────────────────────────────────

        /// <summary>Mueve la unidad al destino.</summary>
        public void MoveTo(Vector3 worldPos)
        {
            if (_agent == null || !_agent.enabled) return;

            // Asegurar que está en NavMesh
            if (!_agent.isOnNavMesh)
            {
                if (!TrySnapToNavMesh()) return;
            }

            _followingAStarPath = false;
            _waypoints = null;
            _waypointIndex = 0;

            // Intentar planificar con A*
            if (useGridPathfinding && MapGrid.Instance != null && MapGrid.Instance.IsReady)
            {
                PathResult result = _pathfinder.FindPath(transform.position, worldPos, canSwim);
                if (result.success && result.cells != null && result.cells.Count > 0)
                {
                    _waypoints = CellsToWorldPoints(result.cells);
                    if (_waypoints.Count > 0)
                    {
                        _waypointIndex = 0;
                        _followingAStarPath = true;
                        SendAgentTo(_waypoints[0]);
                        if (debugLogs)
                            Debug.Log($"{name}: Ruta A* con {_waypoints.Count} waypoints.");
                        return;
                    }
                }
                // A* decidió que no hay ruta (ej. destino en agua) → no hacer nada
                if (debugLogs)
                    Debug.Log($"{name}: A* sin ruta ({result.error}). No se mueve.");
                return;
            }

            // Solo llega aquí si grid no está listo → fallback NavMesh directo
            SendAgentTo(worldPos);
        }

        public void Stop()
        {
            _followingAStarPath = false;
            _waypoints = null;
            _waypointIndex = 0;
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.ResetPath();
                _agent.isStopped = true;
            }
        }

        /// <summary>True si está siguiendo una ruta A*.</summary>
        public bool IsFollowingPath => _followingAStarPath;

        // ─────────────────────────────────────────────────────────────
        //  UPDATE: avanzar waypoints A* cuando el agente llega a uno
        // ─────────────────────────────────────────────────────────────

        void Update()
        {
            if (!_followingAStarPath || _waypoints == null)
                return;
            if (_agent == null || !_agent.isOnNavMesh)
                return;

            // Si el agente llegó al waypoint actual, avanzar al siguiente
            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.3f)
            {
                _waypointIndex++;
                if (_waypointIndex < _waypoints.Count)
                {
                    SendAgentTo(_waypoints[_waypointIndex]);
                }
                else
                {
                    // Ruta completada
                    _followingAStarPath = false;
                    _waypoints = null;
                    _waypointIndex = 0;
                    if (debugLogs)
                        Debug.Log($"{name}: Ruta A* completada.");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  INTERNOS
        // ─────────────────────────────────────────────────────────────

        void SendAgentTo(Vector3 worldPos)
        {
            if (_agent == null || !_agent.isOnNavMesh) return;

            // Buscar punto válido en NavMesh cerca del destino
            if (NavMesh.SamplePosition(worldPos, out NavMeshHit hit, snapToNavMeshRadius, NavMesh.AllAreas))
            {
                _agent.isStopped = false;
                _agent.SetDestination(hit.position);
            }
            else
            {
                _agent.isStopped = false;
                _agent.SetDestination(worldPos);
            }
        }

        bool TrySnapToNavMesh()
        {
            if (_agent == null || !_agent.enabled) return false;
            if (_agent.isOnNavMesh) return true;

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, snapToNavMeshRadius, NavMesh.AllAreas))
            {
                _agent.Warp(hit.position);
                return true;
            }
            return false;
        }

        List<Vector3> CellsToWorldPoints(List<Vector2Int> cells)
        {
            var list = new List<Vector3>(cells.Count);
            foreach (Vector2Int cell in cells)
            {
                Vector3 w = MapGrid.Instance.CellToWorld(cell);
                // Y no importa: el NavMeshAgent la corrige automáticamente
                list.Add(w);
            }
            return list;
        }
    }
}
