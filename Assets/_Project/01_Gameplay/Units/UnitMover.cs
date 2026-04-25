using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Map;
using Project.Gameplay.Pathfinding;
using Project.Gameplay.Buildings;
using Project.Gameplay.Units.Movement;

namespace Project.Gameplay.Units
{
    /// <summary>
    /// Mueve unidades usando A* sobre MapGrid para decidir la ruta,
    /// y NavMeshAgent para ejecutar el movimiento (maneja altura automáticamente).
    /// A* decide POR DÓNDE ir (evita edificios/agua); NavMesh decide CÓMO moverse.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class UnitMover : MonoBehaviour, IUnitMovementComponent
    {
        private NavMeshAgent _agent;
        private IUnitMovementPlanner _planner;
        private IUnitLocomotionController _locomotion;

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
        [SerializeField] bool drawMovementGizmos = true;

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
        float _staleMovingTimer;
        const float StaleMovingRecoverDelay = 0.5f;
        float _skipStaleWatchdogUntilTime;
        bool _playerOrderTimingPending;
        float _playerOrderReceivedTime;
        float _playerOrderSetDestinationTime;
        float _wallMoverDbgLastUnscaled;
        float _wallMoverIdleDbgLastUnscaled;

        /// <summary>Reutilizado para comprobar si el path del NavMesh cruza agua lógica (el mesh suele existir bajo lagos).</summary>
        NavMeshPath _navWaterCheckPath;

        public event System.Action<IUnitMovementComponent> MovementStarted;
        public event System.Action<IUnitMovementComponent> MovementStopped;
        public event System.Action<IUnitMovementComponent, Vector3> DestinationChanged;
        public event System.Action<IUnitMovementComponent, string> PathFailed;

        public Vector3 CurrentDestination { get; private set; }
        public UnitMovementState MovementState { get; private set; } = UnitMovementState.Idle;
        public string LastPathFailureReason { get; private set; }
        public Transform Transform => transform;

        Builder _cachedBuilder;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _cachedBuilder = GetComponent<Builder>();
            _planner = new GridUnitMovementPlanner(useGridPathfinding, canSwim, pathSmoothEpsilon);
            _locomotion = new NavMeshUnitLocomotionController(_agent, snapToNavMeshRadius);
            _navWaterCheckPath = new NavMeshPath();
            if (_agent != null)
            {
                _agent.autoBraking = false;
                if (_agent.acceleration < 16f) _agent.acceleration = 16f;
                if (_agent.angularSpeed < 420f) _agent.angularSpeed = 420f;
                if (_agent.stoppingDistance > 0.35f) _agent.stoppingDistance = 0.35f;
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
            if (_agent == null) return;
            // Si un Builder tiene target activo, necesita stoppingDistance bajo (0.2) para acercarse al muro.
            // No pisar ese valor con el mínimo genérico de movimiento.
            if (_cachedBuilder != null && _cachedBuilder.HasBuildTarget) return;
            if (_agent.stoppingDistance < minStoppingDistance)
                _agent.stoppingDistance = minStoppingDistance;
        }

        // ─────────────────────────────────────────────────────────────
        //  API PÚBLICA
        // ─────────────────────────────────────────────────────────────

        /// <summary>Mantiene la API legacy; redirige a movimiento no prioritario (rally, IA, etc.).</summary>
        public void MoveTo(Vector3 worldPos) => RequestMove(worldPos);

        /// <inheritdoc />
        public bool RequestPlayerMove(Vector3 worldPos) => RequestMoveInternal(worldPos, playerOrder: true);

        /// <summary>Movimiento automático: durante cruce de puerta solo actualiza el destino final (no corta el flujo).</summary>
        public bool RequestMove(Vector3 worldPos) => RequestMoveInternal(worldPos, playerOrder: false);

        const float WallMoverDbgInterval = 0.35f;

        bool WallBuildNavDebugActive()
        {
            return _cachedBuilder != null && _cachedBuilder.ShouldWallBuildRuntimeLog()
                   && _cachedBuilder.CurrentBuildSite != null && _cachedBuilder.CurrentBuildSite.IsCompoundPathBuilding;
        }

        void WallMoverDbgLogMoveRequest(Vector3 requestedDest, string pathKind, bool playerOrder)
        {
            if (playerOrder || !WallBuildNavDebugActive()) return;
            float t = Time.unscaledTime;
            if (t - _wallMoverDbgLastUnscaled < WallMoverDbgInterval) return;
            _wallMoverDbgLastUnscaled = t;
            bool hasPath = _agent != null && _agent.hasPath;
            bool pending = _agent != null && _agent.pathPending;
            float rem = _agent != null ? _agent.remainingDistance : -1f;
            int wpC = _waypoints != null ? _waypoints.Count : 0;
            Debug.Log($"[WallBuildDbg] UnitMover RequestMove pathKind={pathKind} unit={name} dest={requestedDest} followingA*={_followingAStarPath} wpCount={wpC} wpIndex={_waypointIndex} gateLeg={_isHeadingToGateIntermediate} hasPath={hasPath} pathPending={pending} remDist={rem:F3} velSqr={(_agent != null ? _agent.velocity.sqrMagnitude : 0f):F4} moveState={MovementState}", this);
        }

        /// <summary>Entrada interna: la capa de órdenes solicita movimiento, no toca el agente directamente.</summary>
        bool RequestMoveInternal(Vector3 worldPos, bool playerOrder)
        {
            if (_agent == null || !_agent.enabled) return false;
            UnitMovementState oldState = MovementState;
            Vector3 oldDest = CurrentDestination;
            if (playerOrder)
                _playerOrderReceivedTime = Time.realtimeSinceStartup;

            if (playerOrder)
            {
                ImmediateInterruptForPlayerOrder(worldPos, oldDest);
            }

            void LogPlayerTransitionIfNeeded()
            {
                if (!playerOrder || !debugLogs) return;
                bool acceptedSameFrame = _agent != null && (_agent.hasPath || _agent.pathPending);
                bool pathPending = _agent != null && _agent.pathPending;
                Debug.Log($"[UnitMover] Player transition oldState={oldState} newDest={worldPos} resetPath=true acceptedSameFrame={acceptedSameFrame} pathPending={pathPending}", this);
            }

            ApplyMinStoppingDistance();
            if (_locomotion == null || !_locomotion.TryEnsureOnNavMesh())
            {
                EmitPathFailed("No se pudo proyectar la unidad a NavMesh.");
                return false;
            }

            // Cruce de puerta: órdenes automáticas encolan el destino real; el jugador interrumpe y replanifica ya.
            if (_isHeadingToGateIntermediate && !playerOrder)
            {
                _postGateDestination = worldPos;
                _hasPostGateDestination = true;
                CurrentDestination = worldPos;
                DestinationChanged?.Invoke(this, worldPos);
                LogPlayerTransitionIfNeeded();
                WallMoverDbgLogMoveRequest(worldPos, "gate_queue_update_dest", playerOrder);
                return true;
            }

            _planner = new GridUnitMovementPlanner(useGridPathfinding, canSwim, pathSmoothEpsilon);
            SetMovementState(UnitMovementState.PendingPath);
            CurrentDestination = worldPos;
            DestinationChanged?.Invoke(this, worldPos);
            LastPathFailureReason = null;
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

            if (playerOrder)
            {
                _followingAStarPath = false;
                _waypoints = null;
                _waypointIndex = 0;
                _hasAStarGoal = false;
                SetMovementState(UnitMovementState.Moving);
                SetAgentDestinationDirect(worldPos);
                MovementStarted?.Invoke(this);
                _playerOrderSetDestinationTime = Time.realtimeSinceStartup;
                _playerOrderTimingPending = true;
                if (debugLogs)
                    Debug.Log($"[UnitMover] player order timing received={_playerOrderReceivedTime:F4} setDestination={_playerOrderSetDestinationTime:F4} unit={name}", this);
                LogPlayerTransitionIfNeeded();
                return true;
            }

            GateController ignoredGate = ShouldIgnoreGate(_ignoreGateTemporarily) ? _ignoreGateTemporarily : null;
            if (_planner != null && _planner.TryCreateGateTraversal(transform.position, worldPos, ignoredGate, out GateTraversalPlan gatePlan) && gatePlan.isValid)
            {
                _postGateDestination = gatePlan.finalDestination;
                _hasPostGateDestination = true;
                _gateIntermediateDestination = gatePlan.sameSidePoint;
                _gateOppositeSidePoint = gatePlan.oppositeSidePoint;
                _gateSecondLeg = false;
                _isHeadingToGateIntermediate = true;
                _activeGate = gatePlan.gate;
                SetMovementState(UnitMovementState.Moving);
                gatePlan.gate.ForcePassThrough(_agent);
                if (_locomotion.TrySamplePosition(gatePlan.sameSidePoint, 4f, out Vector3 approach))
                    _locomotion.SetDestination(approach);
                else
                    _locomotion.SetDestination(gatePlan.sameSidePoint);
                if (debugLogs)
                    Debug.Log($"{name}: Destino al otro lado del muro → forzando paso por puerta '{gatePlan.gate.name}' (primero approach).", this);
                MovementStarted?.Invoke(this);
                LogPlayerTransitionIfNeeded();
                WallMoverDbgLogMoveRequest(worldPos, "gate_traversal", playerOrder);
                return true;
            }

            string failureReason = null;
            if (_planner != null && _planner.TryPlanPath(transform.position, worldPos, out UnitMovementPlan plan, out failureReason))
            {
                if (!plan.isDirectFallback && plan.waypoints != null && plan.waypoints.Count > 0)
                {
                    _astarGoalWorld = worldPos;
                    _hasAStarGoal = true;
                    _waypoints = plan.waypoints;
                    _waypointIndex = 0;
                    SkipReachedWaypoints();
                    _followingAStarPath = true;
                    ResetStuckTracking();
                    SetMovementState(UnitMovementState.Moving);
                    if (_waypoints == null || _waypoints.Count == 0 || _waypointIndex >= _waypoints.Count)
                    {
                        _followingAStarPath = false;
                        _hasAStarGoal = false;
                        SetAgentDestinationDirect(worldPos);
                    }
                    else
                        SendAgentTo(_waypoints[_waypointIndex]);
                    if (debugLogs)
                        Debug.Log($"{name}: Ruta A* con {_waypoints.Count} waypoints.");
                    MovementStarted?.Invoke(this);
                    LogPlayerTransitionIfNeeded();
                    WallMoverDbgLogMoveRequest(worldPos, _followingAStarPath ? "astar_waypoints" : "astar_degenerate_direct", playerOrder);
                    return true;
                }

                _hasAStarGoal = false;
                SetMovementState(UnitMovementState.Moving);
                SendAgentTo(plan.finalDestination);
                MovementStarted?.Invoke(this);
                LogPlayerTransitionIfNeeded();
                WallMoverDbgLogMoveRequest(worldPos, "astar_planner_final", playerOrder);
                return true;
            }

            if (!string.IsNullOrEmpty(failureReason))
            {
                EmitPathFailed(failureReason);
                if (debugLogs)
                    Debug.Log($"{name}: A* sin ruta ({failureReason}). Fallback NavMesh directo.", this);
            }

            bool builderCompoundAutoMove = !playerOrder && _cachedBuilder != null && _cachedBuilder.HasBuildTarget
                && _cachedBuilder.CurrentBuildSite != null && _cachedBuilder.CurrentBuildSite.IsCompoundPathBuilding;
            if (builderCompoundAutoMove)
            {
                const float builderDestNavValidateRadius = 6f;
                if (!NavMesh.SamplePosition(worldPos, out _, builderDestNavValidateRadius, NavMesh.AllAreas))
                {
                    Debug.LogWarning($"[UnitMover] rejected non-navigable builder destination dest={worldPos} unit={name}", this);
                    Stop();
                    return false;
                }
            }

            SetMovementState(UnitMovementState.Moving);
            SendAgentTo(worldPos);
            MovementStarted?.Invoke(this);
            LogPlayerTransitionIfNeeded();
            WallMoverDbgLogMoveRequest(worldPos, "navmesh_direct_fallback", playerOrder);
            return true;
        }

        void ImmediateInterruptForPlayerOrder(Vector3 newDest, Vector3 oldDest)
        {
            _followingAStarPath = false;
            _waypoints = null;
            _waypointIndex = 0;
            _hasAStarGoal = false;
            _hasPostGateDestination = false;
            _isHeadingToGateIntermediate = false;
            _gateSecondLeg = false;
            _activeGate = null;
            _ignoreGateTemporarily = null;
            _ignoreGateUntilTime = 0f;
            _repathCooldownTimer = 0f;
            _staleMovingTimer = 0f;
            _skipStaleWatchdogUntilTime = Time.time + 0.75f;
            ResetStuckTracking();
            CurrentDestination = oldDest;
            SetMovementState(UnitMovementState.Repathing);

            if (_agent != null && _agent.enabled)
            {
                if (_agent.isOnNavMesh)
                    _agent.ResetPath();
                _agent.isStopped = false;
            }

            if (debugLogs)
                Debug.Log($"[UnitMover] Player order immediate interrupt: unit={name}, oldDest={oldDest}, newDest={newDest}", this);
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
            CurrentDestination = transform.position;
            SetMovementState(UnitMovementState.Idle);
            _locomotion?.Stop();
            MovementStopped?.Invoke(this);
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
            if (TryRecoverStaleMovingState())
                return;

            if (_agent == null || !_agent.isOnNavMesh)
                return;

            if (MapGrid.Instance != null && MapGrid.Instance.IsReady && !canSwim
                && MapGrid.Instance.IsWater(MapGrid.Instance.WorldToCell(transform.position))
                && (_followingAStarPath || _hasAStarGoal))
            {
                TryRecoverFromWaterCell();
            }

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
                        if (NavMesh.SamplePosition(_gateOppositeSidePoint, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                            _locomotion.SetDestination(hit.position);
                        else
                            _locomotion.SetDestination(_gateOppositeSidePoint);
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

            if (_playerOrderTimingPending && _agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                if (_agent.velocity.sqrMagnitude > 0.01f)
                {
                    float firstVelocityTime = Time.realtimeSinceStartup;
                    float delay = firstVelocityTime - _playerOrderSetDestinationTime;
                    if (debugLogs)
                        Debug.Log($"[UnitMover] player order timing received={_playerOrderReceivedTime:F4} setDestination={_playerOrderSetDestinationTime:F4} firstVelocity={firstVelocityTime:F4} delay={delay:F3}s unit={name}", this);
                    _playerOrderTimingPending = false;
                }
            }

            if (!_followingAStarPath || _waypoints == null)
            {
                if (!_isHeadingToGateIntermediate
                    && !_agent.pathPending
                    && (_agent.remainingDistance <= _agent.stoppingDistance + 0.15f || !_agent.hasPath)
                    && _agent.velocity.sqrMagnitude < 0.01f
                    && MovementState != UnitMovementState.Idle)
                {
                    if (WallBuildNavDebugActive())
                    {
                        float tu = Time.unscaledTime;
                        if (tu - _wallMoverIdleDbgLastUnscaled >= WallMoverDbgInterval)
                        {
                            _wallMoverIdleDbgLastUnscaled = tu;
                            float dGoal = FlatDistanceXZ(transform.position, CurrentDestination);
                            Debug.Log($"[WallBuildDbg] UnitMover→Idle (no A* path) unit={name} distXZ_to_CurrentDestination={dGoal:F3} hasPath={_agent.hasPath} rem={_agent.remainingDistance:F3} stop={_agent.stoppingDistance:F3} prevState={MovementState}", this);
                        }
                    }
                    SetMovementState(UnitMovementState.Idle);
                    MovementStopped?.Invoke(this);
                }
                return;
            }

            _repathCooldownTimer -= Time.deltaTime;

            if (enableStuckRepath && _hasAStarGoal && !_isHeadingToGateIntermediate)
                UpdateStuckRepath();

            // Si el agente llegó al waypoint actual, avanzar al siguiente.
            // Importante: sin path válido, remainingDistance suele ser 0 y NO implica "llegué al waypoint"
            // (p. ej. ResetPath, fallo de path o carrera con otro sistema). Exigir hasPath o cercanía real en XZ.
            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.3f)
            {
                List<Vector3> currentWaypoints = _waypoints;
                bool canAdvance = _agent.hasPath;
                int waypointCount = currentWaypoints != null ? currentWaypoints.Count : 0;
                if (!canAdvance && _waypointIndex >= 0 && _waypointIndex < waypointCount)
                {
                    Vector3 w = currentWaypoints[_waypointIndex];
                    float dx = transform.position.x - w.x;
                    float dz = transform.position.z - w.z;
                    float distXZ = Mathf.Sqrt(dx * dx + dz * dz);
                    canAdvance = distXZ <= _agent.stoppingDistance + 0.95f;
                }

                if (canAdvance)
                {
                    _waypointIndex++;
                    ResetStuckTracking();
                    currentWaypoints = _waypoints;
                    waypointCount = currentWaypoints != null ? currentWaypoints.Count : 0;
                    if (currentWaypoints != null && _waypointIndex >= 0 && _waypointIndex < waypointCount)
                    {
                        SendAgentTo(currentWaypoints[_waypointIndex]);
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
        }

        bool TryRecoverStaleMovingState()
        {
            if (Time.time < _skipStaleWatchdogUntilTime)
                return false;

            bool claimsMoving = MovementState == UnitMovementState.Moving || IsFollowingPath;
            if (!claimsMoving)
            {
                _staleMovingTimer = 0f;
                return false;
            }

            bool agentInvalid = _agent == null || !_agent.enabled || !_agent.isOnNavMesh;
            bool noUsablePath = !agentInvalid
                                && !_agent.pathPending
                                && !_agent.hasPath
                                && _agent.velocity.sqrMagnitude < 0.01f;

            if (!agentInvalid && !noUsablePath)
            {
                _staleMovingTimer = 0f;
                return false;
            }

            _staleMovingTimer += Time.deltaTime;
            if (_staleMovingTimer < StaleMovingRecoverDelay)
                return false;

            _staleMovingTimer = 0f;
            float stopping = _agent != null ? Mathf.Max(0.4f, _agent.stoppingDistance + 0.2f) : 0.6f;
            bool destinationIsFar = FlatDistanceXZ(transform.position, CurrentDestination) > stopping;
            bool recovered = false;

            if (destinationIsFar && _agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(CurrentDestination, out NavMeshHit hit, EffectiveSnapRadiusForDestination(), NavMesh.AllAreas))
                {
                    _locomotion.SetDestination(hit.position);
                    recovered = _agent.pathPending || _agent.hasPath;
                }
            }

            if (debugLogs)
            {
                string unit = gameObject != null ? gameObject.name : "<null>";
                bool hasAgent = _agent != null;
                bool hasPath = hasAgent && _agent.hasPath;
                bool onNavMesh = hasAgent && _agent.enabled && _agent.isOnNavMesh;
                Debug.Log($"[UnitMover] Recover stale moving state: unit={unit}, currentDest={CurrentDestination}, agentHasPath={hasPath}, isOnNavMesh={onNavMesh}, recovered={recovered}", this);
            }

            if (recovered)
            {
                SetMovementState(UnitMovementState.Moving);
                return true;
            }

            Stop();
            SetMovementState(UnitMovementState.Idle);
            return true;
        }

        static float FlatDistanceXZ(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
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
                SetMovementState(stuck ? UnitMovementState.Stuck : UnitMovementState.Repathing);
                _repathCooldownTimer = repathCooldownSeconds;
                if (TryRepathToStoredGoal() && debugLogs)
                    Debug.Log($"{name}: Repath A* (atasco o path parcial).", this);
            }
        }

        bool TryRepathToStoredGoal()
        {
            if (!_hasAStarGoal || MapGrid.Instance == null || !MapGrid.Instance.IsReady)
                return false;
            SetMovementState(UnitMovementState.Repathing);
            string failureReason = null;
            if (_planner == null || !_planner.TryPlanPath(transform.position, _astarGoalWorld, out UnitMovementPlan plan, out failureReason))
            {
                if (debugLogs)
                    Debug.Log($"{name}: Repath A* falló ({failureReason}) → NavMesh al objetivo guardado.", this);
                if (!string.IsNullOrEmpty(failureReason))
                    EmitPathFailed(failureReason);
                _followingAStarPath = false;
                _waypoints = null;
                _waypointIndex = 0;
                _hasAStarGoal = false;
                SendAgentTo(_astarGoalWorld);
                SetMovementState(UnitMovementState.Moving);
                return true;
            }
            if (plan.isDirectFallback || plan.waypoints == null || plan.waypoints.Count == 0)
            {
                _followingAStarPath = false;
                _waypoints = null;
                _waypointIndex = 0;
                _hasAStarGoal = false;
                SendAgentTo(_astarGoalWorld);
                SetMovementState(UnitMovementState.Moving);
                return true;
            }
            _waypoints = plan.waypoints;
            _waypointIndex = 0;
            SkipReachedWaypoints();
            ResetStuckTracking();
            if (_waypoints == null || _waypoints.Count == 0 || _waypointIndex >= _waypoints.Count)
            {
                _followingAStarPath = false;
                _hasAStarGoal = false;
                SendAgentTo(_astarGoalWorld);
            }
            else
                SendAgentTo(_waypoints[_waypointIndex]);
            SetMovementState(UnitMovementState.Moving);
            return true;
        }

        void SkipReachedWaypoints()
        {
            if (_waypoints == null || _agent == null)
                return;

            while (_waypointIndex < _waypoints.Count)
            {
                Vector3 w = _waypoints[_waypointIndex];
                float dx = transform.position.x - w.x;
                float dz = transform.position.z - w.z;
                float distXZ = Mathf.Sqrt(dx * dx + dz * dz);
                if (distXZ > _agent.stoppingDistance + 0.95f)
                    break;
                _waypointIndex++;
            }

            if (_waypointIndex >= _waypoints.Count)
            {
                _waypoints = null;
                _waypointIndex = 0;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  INTERNOS
        // ─────────────────────────────────────────────────────────────

        void SendAgentTo(Vector3 worldPos)
        {
            if (_agent == null || !_agent.isOnNavMesh) return;

            float snapR = EffectiveSnapRadiusForDestination();
            Vector3 navDest = worldPos;
            if (NavMesh.SamplePosition(worldPos, out NavMeshHit hit, snapR, NavMesh.AllAreas))
                navDest = hit.position;

            RejectDestinationOnWaterCell(ref navDest, snapR, worldPos);

            // Integración puertas: solo forzar paso si unidad y destino están en LADOS OPUESTOS de la puerta (evita bucle al recalcular tras cruzar).
            if (_isHeadingToGateIntermediate && _hasPostGateDestination && (navDest - _gateIntermediateDestination).sqrMagnitude < 4f)
            {
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
                            if (NavMesh.SamplePosition(sameSidePoint.position, out NavMeshHit approachHit, 4f, NavMesh.AllAreas))
                                _locomotion.SetDestination(approachHit.position);
                            else
                                _locomotion.SetDestination(sameSidePoint.position);
                            return;
                        }
                    }
                }
            }

            Vector3 pathFrom = _agent.nextPosition;
            TryClampNavDestinationToAvoidGridWater(pathFrom, ref navDest);

            // Si el waypoint A* se proyecta al mismo punto actual del agente, la unidad queda en estado "Moving"
            // pero el NavMeshAgent nunca arranca (hasPath=false, remainingDistance=0). Saltar ese waypoint evita
            // el bucle visto en play donde CurrentDestination cambia pero el aldeano no se mueve.
            Vector3 flatFrom = pathFrom; flatFrom.y = 0f;
            Vector3 flatNavDest = navDest; flatNavDest.y = 0f;
            Vector3 flatRequested = worldPos; flatRequested.y = 0f;
            float sampledProgress = (flatNavDest - flatFrom).sqrMagnitude;
            float requestedDistance = (flatRequested - flatFrom).sqrMagnitude;
            if (sampledProgress <= 0.04f && requestedDistance > 1.0f)
            {
                List<Vector3> currentWaypoints = _waypoints;
                if (_followingAStarPath && currentWaypoints != null && _waypointIndex + 1 < currentWaypoints.Count)
                {
                    _waypointIndex++;
                    SendAgentTo(currentWaypoints[_waypointIndex]);
                    return;
                }

                if (_hasAStarGoal)
                {
                    SetAgentDestinationDirect(_astarGoalWorld);
                    return;
                }
            }

            _locomotion.SetDestination(navDest);
        }

        void SetAgentDestinationDirect(Vector3 worldPos)
        {
            if (_agent == null || !_agent.isOnNavMesh) return;

            float snapR = EffectiveSnapRadiusForDestination();
            Vector3 navDest = worldPos;
            if (NavMesh.SamplePosition(worldPos, out NavMeshHit hit, snapR, NavMesh.AllAreas))
                navDest = hit.position;
            RejectDestinationOnWaterCell(ref navDest, snapR, worldPos);

            Vector3 pathFrom = _agent.nextPosition;
            TryClampNavDestinationToAvoidGridWater(pathFrom, ref navDest);

            _locomotion.SetDestination(navDest);
        }

        /// <summary>Con A* activo, limita el snap para no “saltar” a la otra orilla del lago en el NavMesh.</summary>
        float EffectiveSnapRadiusForDestination()
        {
            if (!useGridPathfinding || MapGrid.Instance == null || !MapGrid.Instance.IsReady)
                return snapToNavMeshRadius;
            float cs = MapGrid.GetCellSizeOrDefault();
            return _followingAStarPath || _hasAStarGoal
                ? Mathf.Min(snapToNavMeshRadius, Mathf.Max(0.65f, cs * 0.85f))
                : snapToNavMeshRadius;
        }

        void RejectDestinationOnWaterCell(ref Vector3 navDest, float snapR, Vector3 originalWorld)
        {
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady || canSwim)
                return;
            var c = MapGrid.Instance.WorldToCell(navDest);
            if (!MapGrid.Instance.IsWater(c))
                return;
            for (float f = 0.35f; f <= 1.01f; f += 0.22f)
            {
                float r = Mathf.Max(0.25f, snapR * f);
                if (NavMesh.SamplePosition(originalWorld, out NavMeshHit h2, r, NavMesh.AllAreas))
                {
                    var c2 = MapGrid.Instance.WorldToCell(h2.position);
                    if (!MapGrid.Instance.IsWater(c2))
                    {
                        navDest = h2.position;
                        return;
                    }
                }
            }
        }

        /// <summary>El NavMesh suele ser continuo bajo el agua: recorta el destino al último vértice del path que no cruce celdas de agua según MapGrid.</summary>
        void TryClampNavDestinationToAvoidGridWater(Vector3 fromWorld, ref Vector3 navDestWorld)
        {
            Vector3 desired = navDestWorld;
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady || _isHeadingToGateIntermediate)
                return;

            if (_navWaterCheckPath == null)
                _navWaterCheckPath = new NavMeshPath();

            _navWaterCheckPath.ClearCorners();
            if (!NavMesh.CalculatePath(fromWorld, navDestWorld, NavMesh.AllAreas, _navWaterCheckPath))
                return;
            if (_navWaterCheckPath.status == NavMeshPathStatus.PathInvalid)
                return;

            Vector3[] corners = _navWaterCheckPath.corners;
            if (corners == null || corners.Length < 2)
                return;

            float minProgressSq = Mathf.Max(0.04f, MapGrid.GetCellSizeOrDefault() * 0.2f);
            minProgressSq *= minProgressSq;

            for (int i = 0; i < corners.Length - 1; i++)
            {
                if (!NavMeshSegmentViolatesWaterOnGrid(corners[i], corners[i + 1]))
                    continue;

                Vector3 safe = corners[i];
                float tight = Mathf.Max(0.35f, MapGrid.GetCellSizeOrDefault() * 0.45f);
                if (NavMesh.SamplePosition(safe, out NavMeshHit sh, tight, NavMesh.AllAreas))
                    navDestWorld = sh.position;
                else
                    navDestWorld = safe;

                if ((navDestWorld - fromWorld).sqrMagnitude < minProgressSq)
                    navDestWorld = desired;
                return;
            }
        }

        bool NavMeshSegmentViolatesWaterOnGrid(Vector3 a, Vector3 b)
        {
            if (canSwim)
                return WorldXZSegmentCrossesImpassableWater(a, b);
            return WorldXZSegmentCrossesWater(a, b);
        }

        bool TryRecoverFromWaterCell()
        {
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady || !_agent.isOnNavMesh)
                return false;
            Vector2Int c = MapGrid.Instance.WorldToCell(transform.position);
            if (!MapGrid.Instance.IsWater(c))
                return false;

            for (int r = 1; r <= 14; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r)
                            continue;
                        var nc = new Vector2Int(c.x + dx, c.y + dy);
                        if (!MapGrid.Instance.IsInBounds(nc))
                            continue;
                        if (MapGrid.Instance.IsWater(nc))
                            continue;
                        if (!MapGrid.Instance.IsCellFree(nc))
                            continue;
                        Vector3 w = MapGrid.Instance.CellToWorld(nc);
                        if (NavMesh.SamplePosition(w, out NavMeshHit hit, Mathf.Max(0.5f, MapGrid.GetCellSizeOrDefault() * 0.75f), NavMesh.AllAreas))
                        {
                            _locomotion.Warp(hit.position);
                            _repathCooldownTimer = 0f;
                            ResetStuckTracking();
                            if (_followingAStarPath && _hasAStarGoal)
                                TryRepathToStoredGoal();
                            if (debugLogs)
                                Debug.Log($"{name}: Recuperación desde celda de agua → Warp a tierra cercana.", this);
                            return true;
                        }
                    }
                }
            }

            return false;
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

            if (_locomotion.TrySamplePosition(transform.position, snapToNavMeshRadius, out Vector3 sampled))
            {
                _locomotion.Warp(sampled);
                return true;
            }
            return false;
        }

        void SetMovementState(UnitMovementState newState)
        {
            MovementState = newState;
        }

        void EmitPathFailed(string reason)
        {
            LastPathFailureReason = reason;
            PathFailed?.Invoke(this, reason);
        }

        void OnDrawGizmosSelected()
        {
            if (!drawMovementGizmos)
                return;

            if (CurrentDestination != Vector3.zero)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(CurrentDestination + Vector3.up * 0.2f, 0.45f);
            }

            if (_waypoints != null && _waypoints.Count > 0)
            {
                Gizmos.color = Color.green;
                Vector3 prev = transform.position + Vector3.up * 0.1f;
                for (int i = _waypointIndex; i < _waypoints.Count; i++)
                {
                    Vector3 p = _waypoints[i] + Vector3.up * 0.1f;
                    Gizmos.DrawLine(prev, p);
                    Gizmos.DrawWireSphere(p, i == _waypointIndex ? 0.28f : 0.18f);
                    prev = p;
                }
            }
        }

        List<Vector3> CellsToWorldPoints(List<Vector2Int> cells)
        {
            if (cells == null || cells.Count == 0)
                return new List<Vector3>();
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady)
                return new List<Vector3>();

            List<Vector2Int> simplifiedCells = SimplifyCellPath(cells);
            var smoothed = PathSmoother.SmoothPath(simplifiedCells, MapGrid.Instance, pathSmoothEpsilon);
            // Douglas–Peucker en mundo puede dejar solo 2 puntos y el NavMesh corta en línea recta por lagos/ríos.
            if (!canSwim && smoothed != null && smoothed.Count >= 2 && SmoothedWorldPathCrossesWater(smoothed))
                smoothed = null;
            if (canSwim && smoothed != null && smoothed.Count >= 2 && SmoothedWorldPathCrossesImpassableWater(smoothed))
                smoothed = null;
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
                if (MapGrid.Instance.IsImpassableWater(c))
                    return false;
                if (!canSwim && MapGrid.Instance.IsWater(c))
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

        /// <summary>
        /// True si algún segmento XZ atraviesa celda con agua lógica (lago / río no vado). Evita atajos del NavMesh sobre el terreno bajo el agua.
        /// </summary>
        bool SmoothedWorldPathCrossesWater(List<Vector3> worldPts)
        {
            if (worldPts == null || MapGrid.Instance == null || !MapGrid.Instance.IsReady)
                return false;
            for (int i = 0; i < worldPts.Count - 1; i++)
            {
                if (WorldXZSegmentCrossesWater(worldPts[i], worldPts[i + 1]))
                    return true;
            }
            return false;
        }

        static bool WorldXZSegmentCrossesWater(Vector3 a, Vector3 b)
        {
            var g = MapGrid.Instance;
            if (g == null || !g.IsReady)
                return false;
            Vector2Int c0 = g.WorldToCell(a);
            Vector2Int c1 = g.WorldToCell(b);
            foreach (var c in BresenhamLineCells(c0, c1))
            {
                if (!g.IsInBounds(c))
                    continue;
                if (g.IsWater(c))
                    return true;
            }
            return false;
        }

        bool SmoothedWorldPathCrossesImpassableWater(List<Vector3> worldPts)
        {
            if (worldPts == null || MapGrid.Instance == null || !MapGrid.Instance.IsReady)
                return false;
            for (int i = 0; i < worldPts.Count - 1; i++)
            {
                if (WorldXZSegmentCrossesImpassableWater(worldPts[i], worldPts[i + 1]))
                    return true;
            }
            return false;
        }

        static bool WorldXZSegmentCrossesImpassableWater(Vector3 a, Vector3 b)
        {
            var g = MapGrid.Instance;
            if (g == null || !g.IsReady)
                return false;
            Vector2Int c0 = g.WorldToCell(a);
            Vector2Int c1 = g.WorldToCell(b);
            foreach (var c in BresenhamLineCells(c0, c1))
            {
                if (!g.IsInBounds(c))
                    continue;
                if (g.IsImpassableWater(c))
                    return true;
            }
            return false;
        }

        static IEnumerable<Vector2Int> BresenhamLineCells(Vector2Int from, Vector2Int to)
        {
            int x0 = from.x, y0 = from.y;
            int x1 = to.x, y1 = to.y;
            int dx = Mathf.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                yield return new Vector2Int(x0, y0);
                if (x0 == x1 && y0 == y1)
                    yield break;
                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }
                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }
    }
}
