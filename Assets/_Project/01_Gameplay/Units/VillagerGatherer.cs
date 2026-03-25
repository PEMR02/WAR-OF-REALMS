using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Resources;
using Project.Gameplay.Players;
using Project.Gameplay.Buildings;

namespace Project.Gameplay.Units
{
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

        [Header("Debug")]
        public bool debugLogs = false;

        private NavMeshAgent _agent;
        private UnitMover _mover;
        private ResourceNode _targetNode;
        private Collider _targetCollider;
        private DropOffPoint _deposit;

        private int _carried;
        private ResourceKind _carriedKind;
        private float _gatherTimer;
        private float _retryTimer;

        private State _state = State.Idle;

        // RTS: ignorar Y en distancias de interacciùn (evita "se ve cerca pero nunca interactùa")
        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>
        /// Desfase estable en XZ por instancia: evita que dos aldeanos pidan el mismo vùrtice del recurso / NavMesh y queden pegados por avoidance.
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
            if (_agent.stoppingDistance < 0.25f) _agent.stoppingDistance = 0.6f;

            // Fail-safe: si se desconfigura el prefab, igual se amarra al PlayerResources en escena
            if (owner == null)
                owner = FindFirstObjectByType<PlayerResources>();
        }

        public void Gather(ResourceNode node)
        {
            _targetNode = node;
            _targetCollider = _targetNode != null ? _targetNode.GetComponentInChildren<Collider>(true) : null;
            _deposit = null;

            _gatherTimer = 0f;
            _retryTimer = 0f;

            if (_targetNode != null)
            {
                _carriedKind = _targetNode.kind;

                if (debugLogs)
                    Debug.Log($"[{gameObject.name}] Asignado a recolectar: {_carriedKind} (raw value: {(int)_carriedKind})");

                // Dirigir al punto MùS cercano del collider a la pos actual (no al centro del bounds).
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

            // Si el nodo desapareciùù pero llevo carga, deposito igual
            if (_targetNode == null)
            {
                if (_carried > 0)
                {
                    EnsureDepositMode();
                    TickDeposit();
                }
                return;
            }

            // Priorizar depùùsito si estoy lleno
            if (_carried >= carryCapacity)
            {
                EnsureDepositMode();
                TickDeposit();
                return;
            }

            // Si el nodo se agotùù y llevo algo, deposito
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
                    Debug.Log($"[{gameObject.name}] Cambiando a modo depùùsito. Llevando: {_carried} {_carriedKind}");

                _agent.ResetPath();
                _state = State.GoingToDrop;
                _deposit = null; // forzar bùùsqueda fresh
            }
        }

        void TickGather()
        {
            // Ir al nodo si hace falta (distancia en XZ)
            Vector3 nodePoint = _targetCollider != null
                ? _targetCollider.ClosestPoint(transform.position)
                : _targetNode.transform.position;
            float dist = FlatDistance(transform.position, nodePoint);

            // Robustez: si el NavMesh ya lo dejù a distancia de parada, igual consideramos "en rango".
            bool arrivedByNav = _agent != null
                                 && !_agent.pathPending
                                 && _agent.remainingDistance <= _agent.stoppingDistance + 0.4f;

            if (dist > interactRange && !arrivedByNav)
            {
                if (!_agent.pathPending && (!_agent.hasPath || _agent.remainingDistance <= _agent.stoppingDistance + 0.15f))
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
                if (taken <= 0) return;

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

            // Buscar/rebuscar depùùsito vùùlido cada cierto tiempo
            if (_deposit == null || !_deposit.Accepts(_carriedKind))
            {
                if (_retryTimer <= 0f)
                {
                    _retryTimer = retryDepositEvery;

                    if (debugLogs)
                        Debug.Log($"[{gameObject.name}] Buscando depùùsito para: {_carriedKind} (raw: {(int)_carriedKind})");

                    _deposit = DropOffFinder.FindNearest(transform.position, _carriedKind);

                    if (_deposit == null)
                    {
                        if (debugLogs)
                            Debug.LogWarning($"[{gameObject.name}] ? NO se encontrùù depùùsito para {_carriedKind}. Quedando idle con recursos.");
                    }
                    else
                    {
                        if (debugLogs)
                            Debug.Log($"[{gameObject.name}] ? Depùùsito encontrado: {_deposit.gameObject.name}");
                    }

                    if (_deposit == null)
                    {
                        _agent.ResetPath();
                        _state = State.Idle;
                        return;
                    }

                    SetDestinationSmart(_deposit.GetDropPositionFrom(transform.position));
                    _state = State.GoingToDrop;
                }
                return;
            }

            // Ya hay depùùsito -> ir
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
            }
        }

        void SetDestinationSmart(Vector3 desired)
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;

            Vector3 target = desired;
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas))
                target = hit.position;

            // Centralizar el movimiento en UnitMover para que recolecciùn/depùsito tambiùn respeten puertas.
            if (_mover != null)
                _mover.MoveTo(target);
            else
                _agent.SetDestination(target);
        }
		
		public void AbortGatherAndBankCarried()
		{
			// Deposita instantaneo lo que lleva para evitar perder recursos y evitar ùùping-pongùù
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

		// Importante: detener UnitMover para cancelar rutas A* que podrùan seguir ejecutùndose
		// aunque el gatherer pase a Idle. (De lo contrario el aldeano "va al recurso"
		// pero nunca entra en estado Gathering.)
		if (_mover != null)
			_mover.Stop();
		if (_agent != null && _agent.isOnNavMesh)
			_agent.ResetPath();
		_state = State.Idle;
	}

	/// <summary>
	/// Orden manual de depositar en un punto concreto (ej. click derecho sobre TownCenter).
	/// Si llevo recursos, voy a depositar en ese punto especùqfico.
	/// Si no llevo nada, me quedo idle (el llamador deberùqa moverme con UnitMover en ese caso).
	/// </summary>
	public bool GoDepositAt(DropOffPoint point)
	{
		if (point == null) return false;
		if (_carried <= 0) return false;
		if (!point.Accepts(_carriedKind)) return false;

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

	/// <summary>True si no estù recolectando ni yendo a depùsito.</summary>
	public bool IsIdle => _state == State.Idle;

		
    }
}
