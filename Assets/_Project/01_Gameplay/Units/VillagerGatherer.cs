using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Resources;
using Project.Gameplay.Players;
using Project.Gameplay.Buildings;
using Project.Gameplay.Faction;
using Project.Gameplay.Units.Movement;

namespace Project.Gameplay.Units
{
    /// <summary>Ejecuta después de <see cref="UnitMover"/> (orden 0) para que órdenes de gather/deposit no las pise el avance de waypoints A*.</summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(NavMeshAgent))]
    public class VillagerGatherer : MonoBehaviour
    {
        enum State { Idle, GoingToNode, Gathering, GoingToDrop, Depositing }

        [Header("Owner")]
        public PlayerResources owner;

        [Header("Carry")]
        public int carryCapacity = 20;

        [Header("Gather")]
        public float gatherInterval = 0.6f;
        public int gatherPerTick = 2;
        public float interactRange = 1.6f;

        [Header("Nav")]
        public float navSampleRadius = 2.5f;

        [Header("Deposit Retry")]
        public float retryDepositEvery = 0.8f;

        [Header("Anti-atasco")]
        [Tooltip("Si no avanza hacia el recurso o el depósito durante este tiempo (seg), se fuerza repath o nueva búsqueda de depósito.")]
        public float stuckRecoverSeconds = 2.4f;
        [Tooltip("Velocidad mínima (m/s) en XZ para considerar que sí avanza hacia el objetivo.")]
        public float stuckMinMoveSpeed = 0.06f;

        [Header("Debug")]
        public bool debugLogs = false;

        private NavMeshAgent _agent;
        private UnitMover _mover;
        private Builder _builder;
        private ResourceNode _targetNode;
        private Collider _targetCollider;
        private DropOffPoint _deposit;

        private int _carried;
        private ResourceKind _carriedKind;
        private float _gatherTimer;
        private float _retryTimer;
        private float _stuckTimer;
        private Vector3 _stuckSamplePos;

        private State _state = State.Idle;

        /// <summary>
        /// Tras <see cref="PauseGatherKeepCarried"/> el aldeano puede seguir cargando recursos; sin esto, el siguiente
        /// <c>Update</c> (DefaultExecutionOrder 50, después de <see cref="RTSOrderController"/>) vuelve a forzar depósito
        /// y anula el movimiento que el jugador acaba de dar.
        /// </summary>
        bool _playerOverrodeGatherAutomation;
        /// <summary>No reactivar depósito automático hasta esta hora (evita mismo frame / fallo NavMesh tras orden de movimiento).</summary>
        float _manualOrderDepositResumeAfterTime = -1f;

        // RTS: ignorar Y en distancias de interaccin (evita "se ve cerca pero nunca interacta")
        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>
        /// Desfase estable en XZ por instancia: evita que dos aldeanos pidan el mismo vrtice del recurso / NavMesh y queden pegados por avoidance.
        /// </summary>
        Vector3 StableApproachSpread(float radiusMeters)
        {
            unchecked
            {
                uint u = (uint)gameObject.GetInstanceID() * 1597334677u;
                float ang = (u & 0xFFFFu) / 65536f * Mathf.PI * 2f;
                return new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radiusMeters;
            }
        }

        Vector3 GetDesiredNodeApproachWorld()
        {
            Vector3 basePt = _targetCollider != null
                ? _targetCollider.ClosestPoint(transform.position)
                : _targetNode.transform.position;
            return basePt + StableApproachSpread(0.42f);
        }

        int DropSpreadId => gameObject.GetInstanceID();

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _mover = GetComponent<UnitMover>();
            _builder = GetComponent<Builder>();
            if (_agent.stoppingDistance < 0.25f) _agent.stoppingDistance = 0.6f;

            // Fail-safe: si se desconfigura el prefab, igual se amarra al PlayerResources en escena
            if (owner == null)
                owner = PlayerResources.FindPrimaryHumanSkirmish();
        }

        public void Gather(ResourceNode node)
        {
            _playerOverrodeGatherAutomation = false;
            _manualOrderDepositResumeAfterTime = -1f;
            _targetNode = node;
            _targetCollider = _targetNode != null ? _targetNode.GetComponentInChildren<Collider>(true) : null;
            _deposit = null;

            _gatherTimer = 0f;
            _retryTimer = 0f;
            ResetStuckTracking();

            if (_targetNode != null)
            {
                _carriedKind = _targetNode.kind;

                if (debugLogs)
                    Debug.Log($"[{gameObject.name}] Asignado a recolectar: {_carriedKind} (raw value: {(int)_carriedKind})");

                // Dirigir al punto MS cercano del collider a la pos actual (no al centro del bounds).
                // Esto reduce casos donde el NavMesh se detiene en el borde y nunca entra al rango exacto.
                SetDestinationSmart(GetDesiredNodeApproachWorld());
                _state = State.GoingToNode;
            }
            else
            {
                _state = State.Idle;
            }
        }

        void Update()
        {
            if (owner == null) return;

            // Si el nodo desapareci pero llevo carga, deposito igual
            if (_targetNode == null)
            {
                if (_carried > 0)
                {
                    if (_builder != null && _builder.HasBuildTarget)
                        return;

                    if (_playerOverrodeGatherAutomation)
                    {
                        if (Time.time < _manualOrderDepositResumeAfterTime)
                            return;
                        // MovementState puede marcar Idle un frame antes que el NavMeshAgent cierre el path;
                        // si aquí llamamos EnsureDepositMode() pisamos el destino del jugador y parece que "no se mueven".
                        bool navStillMoving = _agent != null && _agent.enabled && _agent.isOnNavMesh
                            && (_agent.pathPending
                                || (_agent.hasPath && _agent.remainingDistance > _agent.stoppingDistance + 0.08f));
                        bool moverIdle = (_mover == null || _mover.MovementState == UnitMovementState.Idle) && !navStillMoving;
                        bool navSettled = _agent == null || (!_agent.hasPath && !_agent.pathPending);
                        bool constructing = _builder != null && _builder.HasBuildTarget;
                        if (!constructing && moverIdle && navSettled)
                        {
                            _playerOverrodeGatherAutomation = false;
                            // No EnsureDepositMode aquí: el siguiente Update (rama sin override) lo hace y evita carrera con UnitMover.
                        }
                        return;
                    }

                    if (TryRecoverCarryingIdleState())
                        return;

                    EnsureDepositMode();
                    TickDeposit();
                }
                return;
            }

            // Nodo agotado sin carga: evitar Gathering con Take() siempre 0 (bloquea órdenes).
            if (_targetNode.IsDepleted && _carried == 0)
            {
                ClearGatherTargetIdle();
                return;
            }

            // Priorizar depsito si estoy lleno
            if (_carried >= carryCapacity)
            {
                EnsureDepositMode();
                TickDeposit();
                return;
            }

            // Si el nodo se agot y llevo algo, deposito
            if (_targetNode.IsDepleted && _carried > 0)
            {
                EnsureDepositMode();
                TickDeposit();
                return;
            }

            TickGather();
        }

        void EnsureDepositMode()
        {
            if (_state != State.GoingToDrop && _state != State.Depositing)
            {
                if (debugLogs)
                    Debug.Log($"[{gameObject.name}] Cambiando a modo depsito. Llevando: {_carried} {_carriedKind}");

                // Imprescindible: UnitMover puede seguir avanzando waypoints A* al recurso; ResetPath solo no limpia ese estado
                // y el aldeano queda “pegado” al árbol/piedra sin ir a depositar (jugador e IA).
                if (_mover != null)
                    _mover.Stop();
                else if (_agent != null && _agent.isOnNavMesh)
                    _agent.ResetPath();

                _state = State.GoingToDrop;
                _deposit = null; // forzar bsqueda fresh
                ResetStuckTracking();
            }
        }

        bool TryRecoverCarryingIdleState()
        {
            bool noTargetNode = _targetNode == null;
            bool noBuildTarget = _builder == null || !_builder.HasBuildTarget;
            bool noRecentManualOrder = !_playerOverrodeGatherAutomation && Time.time >= _manualOrderDepositResumeAfterTime;
            if (!IsCarrying || !IsIdle || !noTargetNode || !noBuildTarget || !noRecentManualOrder)
                return false;

            bool staleMover = _mover != null
                              && _mover.MovementState == UnitMovementState.Moving
                              && (_agent == null
                                  || !_agent.enabled
                                  || !_agent.isOnNavMesh
                                  || (!_agent.pathPending && !_agent.hasPath && _agent.velocity.sqrMagnitude < 0.01f));
            if (staleMover)
                _mover.Stop();

            var drop = DropOffFinder.FindNearest(transform.position, _carriedKind, GetComponent<FactionMember>());
            if (drop != null)
            {
                if (GoDepositAt(drop))
                    return true;
            }
            else
            {
                if (_mover != null && _mover.MovementState == UnitMovementState.Moving)
                    _mover.Stop();
                else if (_agent != null && _agent.isOnNavMesh)
                    _agent.ResetPath();
                _state = State.Idle;
                return true;
            }

            return false;
        }

        void ResetStuckTracking()
        {
            _stuckTimer = 0f;
            _stuckSamplePos = transform.position;
        }

        void ClearGatherTargetIdle()
        {
            _targetNode = null;
            _targetCollider = null;
            _gatherTimer = 0f;
            if (_mover != null)
                _mover.Stop();
            else if (_agent != null && _agent.isOnNavMesh)
                _agent.ResetPath();
            _state = State.Idle;
        }

        /// <summary>True si parece atascado: casi sin moverse en XZ durante el intervalo de muestreo.</summary>
        bool IsLikelyStuckNav()
        {
            if (_agent == null || !_agent.enabled) return false;
            Vector3 p = transform.position;
            p.y = 0f;
            Vector3 s = _stuckSamplePos;
            s.y = 0f;
            float moved = Vector3.Distance(p, s);
            Vector3 v = _agent.velocity;
            v.y = 0f;
            bool barelyMoved = moved < 0.04f && v.sqrMagnitude < stuckMinMoveSpeed * stuckMinMoveSpeed;
            if (!barelyMoved)
            {
                _stuckSamplePos = transform.position;
                _stuckTimer = 0f;
                return false;
            }
            _stuckTimer += Time.deltaTime;
            return _stuckTimer >= stuckRecoverSeconds;
        }

        void ClearStuckAndRepathTowardResource()
        {
            ResetStuckTracking();
            if (_mover != null)
                _mover.Stop();
            else if (_agent != null && _agent.isOnNavMesh)
                _agent.ResetPath();
            SetDestinationSmart(GetDesiredNodeApproachWorld());
            if (debugLogs)
                Debug.Log($"[{gameObject.name}] Anti-atasco: repath hacia recurso.", this);
        }

        void ClearStuckAndRetryDeposit()
        {
            ResetStuckTracking();
            _deposit = null;
            _retryTimer = 0f;
            if (_mover != null)
                _mover.Stop();
            else if (_agent != null && _agent.isOnNavMesh)
                _agent.ResetPath();
            if (debugLogs)
                Debug.Log($"[{gameObject.name}] Anti-atasco: rebuscar depósito.", this);
        }

        void TickGather()
        {
            // Ir al nodo si hace falta (distancia en XZ)
            Vector3 nodePoint = _targetCollider != null
                ? _targetCollider.ClosestPoint(transform.position)
                : _targetNode.transform.position;
            float dist = FlatDistance(transform.position, nodePoint);

            // Robustez: si el NavMesh ya lo dej a distancia de parada, igual consideramos "en rango".
            bool arrivedByNav = _agent != null
                                 && !_agent.pathPending
                                 && _agent.remainingDistance <= _agent.stoppingDistance + 0.4f;

            if (dist > interactRange && !arrivedByNav)
            {
                if (IsLikelyStuckNav())
                    ClearStuckAndRepathTowardResource();
                else if (!_agent.pathPending && (!_agent.hasPath || _agent.remainingDistance <= _agent.stoppingDistance + 0.15f))
                {
                    SetDestinationSmart(GetDesiredNodeApproachWorld());
                }

                _state = State.GoingToNode;
                return;
            }

            // Dentro de rango -> recolectar
            _state = State.Gathering;
            _gatherTimer += Time.deltaTime;

            if (_gatherTimer >= gatherInterval)
            {
                _gatherTimer = 0f;

                int taken = _targetNode.Take(gatherPerTick);
                if (_targetNode == null)
                {
                    _targetCollider = null;
                    if (_carried > 0)
                        EnsureDepositMode();
                    else
                        ClearGatherTargetIdle();
                    return;
                }
                if (taken <= 0)
                {
                    if (_targetNode.IsDepleted)
                        ClearGatherTargetIdle();
                    return;
                }

                _carried += taken;

                if (_carried >= carryCapacity)
                {
                    EnsureDepositMode();
                }
            }
        }

        void TickDeposit()
        {
            _retryTimer -= Time.deltaTime;

            // Buscar/rebuscar depsito vlido cada cierto tiempo
            if (_deposit == null || !_deposit.Accepts(_carriedKind))
            {
                if (_retryTimer <= 0f)
                {
                    _retryTimer = retryDepositEvery;

                    if (debugLogs)
                        Debug.Log($"[{gameObject.name}] Buscando depsito para: {_carriedKind} (raw: {(int)_carriedKind})");

                    _deposit = DropOffFinder.FindNearest(transform.position, _carriedKind, GetComponent<FactionMember>());

                    if (_deposit == null)
                    {
                        if (debugLogs)
                            Debug.LogWarning($"[{gameObject.name}] ? NO se encontr depsito para {_carriedKind}. Quedando idle con recursos.");
                    }
                    else
                    {
                        if (debugLogs)
                            Debug.Log($"[{gameObject.name}] ? Depsito encontrado: {_deposit.gameObject.name}");
                    }

                    if (_deposit == null)
                    {
                        if (_mover != null)
                            _mover.Stop();
                        else if (_agent != null && _agent.isOnNavMesh)
                            _agent.ResetPath();
                        _state = State.Idle;
                        return;
                    }

                    SetDestinationSmart(_deposit.GetDropPositionFrom(transform.position));
                    _state = State.GoingToDrop;
                }
                return;
            }

            // Ya hay depsito -> ir
            Vector3 dropPos = _deposit.GetDropPositionFrom(transform.position, DropSpreadId);

            if (!_agent.pathPending && (!_agent.hasPath || _agent.remainingDistance <= _agent.stoppingDistance + 0.15f))
            {
                SetDestinationSmart(dropPos);
                _state = State.GoingToDrop;
            }

            // Llegada robusta
            bool arrivedByNav = !_agent.pathPending && _agent.hasPath &&
                                _agent.remainingDistance <= _agent.stoppingDistance + 0.25f;

            bool arrivedByDist = FlatDistance(transform.position, dropPos) <= interactRange + 0.8f;

            if (!arrivedByNav && !arrivedByDist && FlatDistance(transform.position, dropPos) > interactRange + 0.35f && IsLikelyStuckNav())
                ClearStuckAndRetryDeposit();

            if (arrivedByNav || arrivedByDist)
            {
                _state = State.Depositing;

                if (debugLogs)
                    Debug.Log($"[{gameObject.name}] ? DEPOSITANDO {_carried} {_carriedKind} en {_deposit.gameObject.name}");

                owner.Add(_carriedKind, _carried);
                _carried = 0;

                // volver al nodo si sigue activo
                if (_targetNode != null && !_targetNode.IsDepleted)
                {
                    SetDestinationSmart(GetDesiredNodeApproachWorld());
                    _state = State.GoingToNode;
                }
                else
                {
                    _agent.ResetPath();
                    _state = State.Idle;
                }

                ResetStuckTracking();
            }
        }

        void SetDestinationSmart(Vector3 desired)
        {
            if (_agent == null || !_agent.enabled) return;
            if (!_agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit snapSelf, navSampleRadius * 2f, NavMesh.AllAreas))
                {
                    _agent.Warp(snapSelf.position);
                    if (debugLogs)
                        Debug.Log($"[{gameObject.name}] SetDestinationSmart: proyectado a NavMesh antes de orden.", this);
                }
                else
                    return;
            }

            Vector3 target = desired;
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas))
                target = hit.position;

            // Centralizar el movimiento en UnitMover para que recoleccin/depsito tambin respeten puertas.
            if (_mover != null)
                _mover.MoveTo(target);
            else
                _agent.SetDestination(target);
        }
		
		public void AbortGatherAndBankCarried()
		{
			_playerOverrodeGatherAutomation = false;
			_manualOrderDepositResumeAfterTime = -1f;
			// Deposita instantaneo lo que lleva para evitar perder recursos y evitar ping-pong
			if (owner != null && _carried > 0)
				owner.Add(_carriedKind, _carried);

			_carried = 0;
			_targetNode = null;
			_deposit = null;

			_gatherTimer = 0f;
			_retryTimer = 0f;

			if (_mover != null)
				_mover.Stop();
			if (_agent != null && _agent.isOnNavMesh)
				_agent.ResetPath();
			_state = State.Idle;
		}
	public void PauseGatherKeepCarried()
	{
		// Pausa el trabajo actual, pero mantiene lo que llevo cargado
		_targetNode = null;
		_deposit = null;

		_gatherTimer = 0f;
		_retryTimer = 0f;

		// Importante: detener UnitMover para cancelar rutas A* que podran seguir ejecutndose
		// aunque el gatherer pase a Idle. (De lo contrario el aldeano "va al recurso"
		// pero nunca entra en estado Gathering.)
		if (_mover != null)
			_mover.Stop();
		if (_agent != null && _agent.isOnNavMesh)
			_agent.ResetPath();
		_state = State.Idle;
		if (_carried > 0)
		{
			_playerOverrodeGatherAutomation = true;
			_manualOrderDepositResumeAfterTime = Time.time + 0.4f;
		}
		else
		{
			_playerOverrodeGatherAutomation = false;
			_manualOrderDepositResumeAfterTime = -1f;
		}
	}

	/// <summary>
	/// Orden manual de depositar en un punto concreto (ej. click derecho sobre TownCenter).
	/// Si llevo recursos, voy a depositar en ese punto especqfico.
	/// Si no llevo nada, me quedo idle (el llamador deberqa moverme con UnitMover en ese caso).
	/// </summary>
	public bool GoDepositAt(DropOffPoint point)
	{
		if (point == null) return false;
		if (_carried <= 0) return false;
		if (!point.Accepts(_carriedKind)) return false;

		_playerOverrodeGatherAutomation = false;
		_manualOrderDepositResumeAfterTime = -1f;
		_deposit = point;
		_retryTimer = 0f;
		_state = State.GoingToDrop;
		SetDestinationSmart(_deposit.GetDropPositionFrom(transform.position, DropSpreadId));

		if (debugLogs)
			Debug.Log($"[{gameObject.name}] Orden manual: depositar {_carried} {_carriedKind} en {point.gameObject.name}");

		return true;
	}

	/// <summary>True si el aldeano lleva recursos cargados.</summary>
	public bool IsCarrying => _carried > 0;

	/// <summary>True si no est recolectando ni yendo a depsito.</summary>
	public bool IsIdle => _state == State.Idle;

		
    }
}
