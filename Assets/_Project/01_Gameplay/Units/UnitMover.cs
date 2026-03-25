using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Map;
using Project.Gameplay.Pathfinding;
using Project.Gameplay.Buildings;

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
        [Tooltip("Distancia mínima al destino para considerar que la unidad llegó. Evita que se amontonen y vibren en el mismo punto.")]
        public float minStoppingDistance = 0.4f;

        [Header("Ruta suavizada")]
        [Tooltip("Epsilon Douglas-Peucker en metros (más alto = menos waypoints, trayectoria más recta).")]
        [SerializeField] float pathSmoothEpsilon = 0.55f;

        [Header("Repath si se atasca")]
        [SerializeField] bool enableStuckRepath = true;
        [SerializeField] float stuckRepathSeconds = 2.25f;
        [SerializeField] float stuckMinMoveDistance = 0.22f;
        [SerializeField] float repathCooldownSeconds = 1.35f;

        [Header("Debug")]
        public bool debugLogs = false;

        // Ruta A*: lista de waypoints mundo que el agente sigue uno a uno
        private List<Vector3> _waypoints;
        private int _waypointIndex;
        private bool _followingAStarPath;

        // Puertas RTS: destino intermedio para forzar el paso (primero mismo lado, luego al otro)
        Vector3 _postGateDestination;
        bool _hasPostGateDestination;
        Vector3 _gateIntermediateDestination;
        Vector3 _gateOppositeSidePoint; // segundo tramo: punto al otro lado de la puerta
        bool _isHeadingToGateIntermediate;
        bool _gateSecondLeg; // true = yendo al otro lado; false = yendo al approach (mismo lado)
        GateController _activeGate;
        GateController _ignoreGateTemporarily;
        float _ignoreGateUntilTime;

        const float GateIgnoreDuration = 1.5f;

        Vector3 _astarGoalWorld;
        bool _hasAStarGoal;
        float _repathCooldownTimer;
        float _stuckTime;
        Vector3 _stuckLastPos;
        bool _stuckTrackingInitialized;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _pathfinder = new Pathfinder();
            if (_agent != null)
            {
                _agent.autoBraking = false;
                _agent.avoidancePriority = 20 + Mathf.Abs(GetInstanceID()) % 60;
            }
        }

        void Start()
        {
            ApplyMinStoppingDistance();
            // Intentar colocar en NavMesh al inicio
            Invoke(nameof(TrySnapToNavMesh), 0.5f);
        }

        void ApplyMinStoppingDistance()
        {
            if (_agent != null && _agent.stoppingDistance < minStoppingDistance)
                _agent.stoppingDistance = minStoppingDistance;
        }

        // ─────────────────────────────────────────────────────────────
        //  API PÚBLICA
        // ─────────────────────────────────────────────────────────────

        /// <summary>Mueve la unidad al destino.</summary>
        public void MoveTo(Vector3 worldPos)
        {
            if (_agent == null || !_agent.enabled) return;

            ApplyMinStoppingDistance();
            if (!_agent.isOnNavMesh)
            {
                if (!TrySnapToNavMesh()) return;
            }

            // Mientras la unidad ya está comprometida con una puerta, no permitimos que nuevas órdenes
            // reseteen el subflujo de cruce. Solo actualizamos el destino final a alcanzar al salir.
            if (_isHeadingToGateIntermediate)
            {
                _postGateDestination = worldPos;
                _hasPostGateDestination = true;
                return;
            }

            _followingAStarPath = false;
            _waypoints = null;
            _waypointIndex = 0;
            _hasAStarGoal = false;
            _repathCooldownTimer = 0f;
            ResetStuckTracking();
            _isHeadingToGateIntermediate = false;
            _gateSecondLeg = false;
            _hasPostGateDestination = false;
            _activeGate = null;

            // CRÍTICO: Si la línea unidad → destino cruza una puerta Y unidad y destino están en LADOS OPUESTOS,
            // forzar paso por la puerta. Si ya están en el mismo lado, no forzar (evita bucle).
            var gateOnPath = GateController.FindGateOnSegment(transform.position, worldPos, 6f);
            if (ShouldIgnoreGate(gateOnPath))
                gateOnPath = null;
            if (gateOnPath != null && gateOnPath.entryPoint != null && gateOnPath.exitPoint != null && gateOnPath.gateCenter != null)
            {
                Vector3 center = gateOnPath.gateCenter.position;
                Vector3 fwd = gateOnPath.gateCenter.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f)
                {
                    fwd.Normalize();
                    Vector3 toUnit = transform.position - center;
                    toUnit.y = 0f;
                    Vector3 toDest = worldPos - center;
                    toDest.y = 0f;
                    float sideUnit = toUnit.sqrMagnitude > 0.0001f ? Vector3.Dot(toUnit.normalized, fwd) : 0f;
                    float sideDest = toDest.sqrMagnitude > 0.0001f ? Vector3.Dot(toDest.normalized, fwd) : 0f;
                    bool oppositeSides = (sideUnit > 0.1f && sideDest < -0.1f) || (sideUnit < -0.1f && sideDest > 0.1f);
                    if (oppositeSides)
                    {
                        // Primero ir al punto del MISMO lado (approach), luego al otro lado; así el camino corto no rodea el muro.
                        float entrySide = Vector3.Dot((gateOnPath.entryPoint.position - center), fwd);
                        Transform sameSidePoint = (entrySide * sideUnit > 0f) ? gateOnPath.entryPoint : gateOnPath.exitPoint;
                        Transform oppositeSidePoint = (entrySide * sideUnit > 0f) ? gateOnPath.exitPoint : gateOnPath.entryPoint;
                        if ((sameSidePoint.position - transform.position).sqrMagnitude > 0.9f)
                        {
                            _postGateDestination = worldPos;
                            _hasPostGateDestination = true;
                            _gateIntermediateDestination = sameSidePoint.position;
                            _gateOppositeSidePoint = oppositeSidePoint.position;
                            _gateSecondLeg = false;
                            _isHeadingToGateIntermediate = true;
                            _activeGate = gateOnPath;
                            gateOnPath.ForcePassThrough(_agent);
                            _agent.isStopped = false;
                            if (NavMesh.SamplePosition(sameSidePoint.position, out NavMeshHit approachHit, 4f, NavMesh.AllAreas))
                                _agent.SetDestination(approachHit.position);
                            else
                                _agent.SetDestination(sameSidePoint.position);
                            if (debugLogs)
                                Debug.Log($"{name}: Destino al otro lado del muro → forzando paso por puerta '{gateOnPath.name}' (primero approach).", this);
                            return;
                        }
                    }
                }
            }

            // Intentar planificar con A*
            if (useGridPathfinding && MapGrid.Instance != null && MapGrid.Instance.IsReady)
            {
                PathResult result = _pathfinder.FindPath(transform.position, worldPos, canSwim);
                if (!result.success || result.cells == null)
                {
                    if (debugLogs)
                        Debug.Log($"{name}: A* sin ruta ({result.error}). No se mueve.");
                    return;
                }

                // Misma celda inicio/fin en grid: Pathfinder devuelve lista vacía. Antes no se movía → atasco "en la casilla".
                if (result.cells.Count == 0)
                {
                    _followingAStarPath = false;
                    _waypoints = null;
                    _waypointIndex = 0;
                    _hasAStarGoal = false;
                    SendAgentTo(worldPos);
                    if (debugLogs)
                        Debug.Log($"{name}: A* misma celda → NavMesh directo al objetivo.");
                    return;
                }

                _astarGoalWorld = worldPos;
                _hasAStarGoal = true;
                _waypoints = CellsToWorldPoints(result.cells);
                if (_waypoints.Count > 0)
                {
                    _waypointIndex = 0;
                    _followingAStarPath = true;
                    ResetStuckTracking();
                    SendAgentTo(_waypoints[0]);
                    if (debugLogs)
                        Debug.Log($"{name}: Ruta A* con {_waypoints.Count} waypoints.");
                    return;
                }
                // Celdas > 0 pero waypoints vacíos (raro): fallback
                _hasAStarGoal = false;
                SendAgentTo(worldPos);
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
            _hasAStarGoal = false;
            _repathCooldownTimer = 0f;
            ResetStuckTracking();
            _isHeadingToGateIntermediate = false;
            _hasPostGateDestination = false;
            _activeGate = null;
            _ignoreGateTemporarily = null;
            _ignoreGateUntilTime = 0f;
            _gateSecondLeg = false;
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.ResetPath();
                _agent.isStopped = true;
            }
        }

        /// <summary>True si está siguiendo una ruta A*.</summary>
        public bool IsFollowingPath => _followingAStarPath;
        /// <summary>True si la unidad está ejecutando el paso intermedio de una puerta.</summary>
        public bool IsGateTransitionActive => _isHeadingToGateIntermediate;

        // ─────────────────────────────────────────────────────────────
        //  UPDATE: avanzar waypoints A* cuando el agente llega a uno
        // ─────────────────────────────────────────────────────────────

        void Update()
        {
            if (_agent == null || !_agent.isOnNavMesh)
                return;

            // Puerta: al alcanzar el punto intermedio (Entry/Exit) retomamos el destino real.
            // Usar distancia real al destino además de remainingDistance: cuando hay varias unidades apelotonadas
            // o el path es inválido, remainingDistance puede no bajar y se quedan encerrados.
            if (_isHeadingToGateIntermediate)
            {
                Vector3 p = transform.position;
                Vector3 d = _agent.destination;
                p.y = 0f;
                d.y = 0f;
                float distToDestXZ = (d - p).magnitude;
                bool arrivedByRemaining = !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.5f;
                bool arrivedByDistance = distToDestXZ < 2f;
                if (arrivedByRemaining || arrivedByDistance)
                {
                    if (!_gateSecondLeg)
                    {
                        // Llegamos al approach (mismo lado); ahora cruzar al otro lado.
                        _gateSecondLeg = true;
                        _gateIntermediateDestination = _gateOppositeSidePoint;
                        _agent.isStopped = false;
                        if (NavMesh.SamplePosition(_gateOppositeSidePoint, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                            _agent.SetDestination(hit.position);
                        else
                            _agent.SetDestination(_gateOppositeSidePoint);
                        return;
                    }
                    _isHeadingToGateIntermediate = false;
                    _gateSecondLeg = false;
                    _ignoreGateTemporarily = _activeGate;
                    _ignoreGateUntilTime = Time.time + GateIgnoreDuration;
                    _activeGate = null;
                    if (_hasPostGateDestination)
                    {
                        var dest = _postGateDestination;
                        _hasPostGateDestination = false;
                        SetAgentDestinationDirect(dest);
                        return;
                    }
                }
            }

            if (!_followingAStarPath || _waypoints == null)
                return;

            _repathCooldownTimer -= Time.deltaTime;

            if (enableStuckRepath && _hasAStarGoal && !_isHeadingToGateIntermediate)
                UpdateStuckRepath();

            // Si el agente llegó al waypoint actual, avanzar al siguiente
            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.3f)
            {
                _waypointIndex++;
                ResetStuckTracking();
                if (_waypointIndex < _waypoints.Count)
                {
                    SendAgentTo(_waypoints[_waypointIndex]);
                }
                else
                {
                    // Ruta completada en celdas: el último waypoint es centro de celda; el objetivo real
                    // (depósito, recurso junto a NavMeshObstacle) suele estar en otro punto walkable.
                    // Sin este tramo final el agente se queda en el borde de la celda / fuera de alcance del interact.
                    Vector3 finalGoal = _astarGoalWorld;
                    _followingAStarPath = false;
                    _waypoints = null;
                    _waypointIndex = 0;
                    _hasAStarGoal = false;
                    SetAgentDestinationDirect(finalGoal);
                    if (debugLogs)
                        Debug.Log($"{name}: Ruta A* completada → tramo final NavMesh al objetivo.", this);
                }
            }
        }

        void ResetStuckTracking()
        {
            _stuckTime = 0f;
            _stuckTrackingInitialized = false;
        }

        void UpdateStuckRepath()
        {
            if (!_stuckTrackingInitialized)
            {
                _stuckLastPos = transform.position;
                _stuckTrackingInitialized = true;
                _stuckTime = 0f;
            }

            float minSqr = stuckMinMoveDistance * stuckMinMoveDistance;
            if ((transform.position - _stuckLastPos).sqrMagnitude >= minSqr)
            {
                _stuckLastPos = transform.position;
                _stuckTime = 0f;
            }
            else
                _stuckTime += Time.deltaTime;

            bool partial = _agent.hasPath && _agent.pathStatus == NavMeshPathStatus.PathPartial;
            bool barelyMoving = _agent.velocity.sqrMagnitude < 0.015f;
            bool stuck = _stuckTime >= stuckRepathSeconds && barelyMoving && !_agent.pathPending;
            if ((stuck || partial) && _repathCooldownTimer <= 0f)
            {
                _repathCooldownTimer = repathCooldownSeconds;
                if (TryRepathToStoredGoal() && debugLogs)
                    Debug.Log($"{name}: Repath A* (atasco o path parcial).", this);
            }
        }

        bool TryRepathToStoredGoal()
        {
            if (!_hasAStarGoal || MapGrid.Instance == null || !MapGrid.Instance.IsReady)
                return false;
            PathResult result = _pathfinder.FindPath(transform.position, _astarGoalWorld, canSwim);
            if (!result.success || result.cells == null || result.cells.Count == 0)
                return false;
            var newWaypoints = CellsToWorldPoints(result.cells);
            if (newWaypoints.Count == 0)
                return false;
            _waypoints = newWaypoints;
            _waypointIndex = 0;
            ResetStuckTracking();
            SendAgentTo(_waypoints[0]);
            return true;
        }

        // ─────────────────────────────────────────────────────────────
        //  INTERNOS
        // ─────────────────────────────────────────────────────────────

        void SendAgentTo(Vector3 worldPos)
        {
            if (_agent == null || !_agent.isOnNavMesh) return;

            // Buscar punto válido en NavMesh cerca del destino
            Vector3 navDest = worldPos;
            if (NavMesh.SamplePosition(worldPos, out NavMeshHit hit, snapToNavMeshRadius, NavMesh.AllAreas))
            {
                navDest = hit.position;
            }

            // Integración puertas: solo forzar paso si unidad y destino están en LADOS OPUESTOS de la puerta (evita bucle al recalcular tras cruzar).
            if (_isHeadingToGateIntermediate && _hasPostGateDestination && (navDest - _gateIntermediateDestination).sqrMagnitude < 4f)
            {
                _agent.isStopped = false;
                return;
            }
            var gate = GateController.FindGateOnSegment(transform.position, navDest, 4.5f);
            if (ShouldIgnoreGate(gate))
                gate = null;
            if (gate != null && gate.entryPoint != null && gate.exitPoint != null && gate.gateCenter != null)
            {
                Vector3 center = gate.gateCenter.position;
                Vector3 forward = gate.gateCenter.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f)
                {
                    forward.Normalize();
                    Vector3 toAgent = transform.position - center;
                    toAgent.y = 0f;
                    Vector3 toDest = navDest - center;
                    toDest.y = 0f;
                    float sideUnit = toAgent.sqrMagnitude > 0.0001f ? Vector3.Dot(toAgent.normalized, forward) : 0f;
                    float sideDest = toDest.sqrMagnitude > 0.0001f ? Vector3.Dot(toDest.normalized, forward) : 0f;
                    bool oppositeSides = (sideUnit > 0.1f && sideDest < -0.1f) || (sideUnit < -0.1f && sideDest > 0.1f);
                    if (oppositeSides)
                    {
                        float entrySide = Vector3.Dot((gate.entryPoint.position - center), forward);
                        Transform sameSidePoint = (entrySide * sideUnit > 0f) ? gate.entryPoint : gate.exitPoint;
                        Transform oppositeSidePoint = (entrySide * sideUnit > 0f) ? gate.exitPoint : gate.entryPoint;
                        if ((sameSidePoint.position - transform.position).sqrMagnitude > 0.9f)
                        {
                            _postGateDestination = navDest;
                            _hasPostGateDestination = true;
                            _gateIntermediateDestination = sameSidePoint.position;
                            _gateOppositeSidePoint = oppositeSidePoint.position;
                            _gateSecondLeg = false;
                            _isHeadingToGateIntermediate = true;
                            _activeGate = gate;
                            gate.ForcePassThrough(_agent);
                            _agent.isStopped = false;
                            if (NavMesh.SamplePosition(sameSidePoint.position, out NavMeshHit approachHit, 4f, NavMesh.AllAreas))
                                _agent.SetDestination(approachHit.position);
                            else
                                _agent.SetDestination(sameSidePoint.position);
                            return;
                        }
                    }
                }
            }

            _agent.isStopped = false;
            _agent.SetDestination(navDest);
        }

        void SetAgentDestinationDirect(Vector3 worldPos)
        {
            if (_agent == null || !_agent.isOnNavMesh) return;

            Vector3 navDest = worldPos;
            if (NavMesh.SamplePosition(worldPos, out NavMeshHit hit, snapToNavMeshRadius, NavMesh.AllAreas))
                navDest = hit.position;

            _agent.isStopped = false;
            _agent.SetDestination(navDest);
        }

        bool ShouldIgnoreGate(GateController gate)
        {
            if (gate == null || _ignoreGateTemporarily == null)
                return false;

            if (gate != _ignoreGateTemporarily)
                return false;

            if (Time.time <= _ignoreGateUntilTime)
                return true;

            _ignoreGateTemporarily = null;
            return false;
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
            if (cells == null || cells.Count == 0)
                return new List<Vector3>();
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady)
                return new List<Vector3>();

            List<Vector2Int> simplifiedCells = SimplifyCellPath(cells);
            var smoothed = PathSmoother.SmoothPath(simplifiedCells, MapGrid.Instance, pathSmoothEpsilon);
            if (smoothed != null && smoothed.Count >= 2)
                return smoothed;
            if (smoothed != null && smoothed.Count == 1)
                return smoothed;

            var list = new List<Vector3>(simplifiedCells.Count);
            foreach (Vector2Int cell in simplifiedCells)
            {
                Vector3 w = MapGrid.Instance.CellToWorld(cell);
                list.Add(w);
            }
            return list;
        }

        List<Vector2Int> SimplifyCellPath(List<Vector2Int> cells)
        {
            if (cells == null || cells.Count <= 2 || MapGrid.Instance == null || !MapGrid.Instance.IsReady)
                return cells;

            var result = new List<Vector2Int>(cells.Count);
            int anchorIndex = 0;
            result.Add(cells[anchorIndex]);

            while (anchorIndex < cells.Count - 1)
            {
                int furthestReachable = anchorIndex + 1;
                for (int i = anchorIndex + 2; i < cells.Count; i++)
                {
                    if (!HasGridLineOfSight(cells[anchorIndex], cells[i]))
                        break;
                    furthestReachable = i;
                }

                result.Add(cells[furthestReachable]);
                anchorIndex = furthestReachable;
            }

            return result;
        }

        bool HasGridLineOfSight(Vector2Int from, Vector2Int to)
        {
            int x0 = from.x;
            int y0 = from.y;
            int x1 = to.x;
            int y1 = to.y;

            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                Vector2Int c = new Vector2Int(x0, y0);
                if (!MapGrid.Instance.IsInBounds(c))
                    return false;
                // Línea de visión en grid: celdas de puerta abierta cuentan como libres.
                if (!MapGrid.Instance.IsCellFree(c) && !MapGrid.Instance.IsOpenGatePassableCell(c))
                    return false;

                if (x0 == x1 && y0 == y1)
                    return true;

                int e2 = err * 2;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }
    }
}
