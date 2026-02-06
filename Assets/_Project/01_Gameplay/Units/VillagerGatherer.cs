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
        private ResourceNode _targetNode;
        private DropOffPoint _deposit;

        private int _carried;
        private ResourceKind _carriedKind;
        private float _gatherTimer;
        private float _retryTimer;

        private State _state = State.Idle;

        // RTS: ignorar Y en distancias de interacci車n (evita ※se ve cerca pero nunca interact迆a§)
        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            if (_agent.stoppingDistance < 0.25f) _agent.stoppingDistance = 0.6f;

            // Fail-safe: si se desconfigura el prefab, igual se amarra al PlayerResources en escena
            if (owner == null)
                owner = FindFirstObjectByType<PlayerResources>();
        }

        public void Gather(ResourceNode node)
        {
            _targetNode = node;
            _deposit = null;

            _gatherTimer = 0f;
            _retryTimer = 0f;

            if (_targetNode != null)
            {
                _carriedKind = _targetNode.kind;

                if (debugLogs)
                    Debug.Log($"[{gameObject.name}] Asignado a recolectar: {_carriedKind} (raw value: {(int)_carriedKind})");

                SetDestinationSmart(_targetNode.transform.position);
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

            // Si el nodo desapareci車 pero llevo carga, deposito igual
            if (_targetNode == null)
            {
                if (_carried > 0)
                {
                    EnsureDepositMode();
                    TickDeposit();
                }
                return;
            }

            // Priorizar dep車sito si estoy lleno
            if (_carried >= carryCapacity)
            {
                EnsureDepositMode();
                TickDeposit();
                return;
            }

            // Si el nodo se agot車 y llevo algo, deposito
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
                    Debug.Log($"[{gameObject.name}] Cambiando a modo dep車sito. Llevando: {_carried} {_carriedKind}");

                _agent.ResetPath();
                _state = State.GoingToDrop;
                _deposit = null; // forzar b迆squeda fresh
            }
        }

        void TickGather()
        {
            // Ir al nodo si hace falta (distancia en XZ)
            float dist = FlatDistance(transform.position, _targetNode.transform.position);

            if (dist > interactRange)
            {
                if (!_agent.pathPending && (!_agent.hasPath || _agent.remainingDistance <= _agent.stoppingDistance + 0.15f))
                    SetDestinationSmart(_targetNode.transform.position);

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

            // Buscar/rebuscar dep車sito v芍lido cada cierto tiempo
            if (_deposit == null || !_deposit.Accepts(_carriedKind))
            {
                if (_retryTimer <= 0f)
                {
                    _retryTimer = retryDepositEvery;

                    if (debugLogs)
                        Debug.Log($"[{gameObject.name}] Buscando dep車sito para: {_carriedKind} (raw: {(int)_carriedKind})");

                    _deposit = DropOffFinder.FindNearest(transform.position, _carriedKind);

                    if (_deposit == null)
                    {
                        if (debugLogs)
                            Debug.LogWarning($"[{gameObject.name}] ? NO se encontr車 dep車sito para {_carriedKind}. Quedando idle con recursos.");
                    }
                    else
                    {
                        if (debugLogs)
                            Debug.Log($"[{gameObject.name}] ? Dep車sito encontrado: {_deposit.gameObject.name}");
                    }

                    if (_deposit == null)
                    {
                        _agent.ResetPath();
                        _state = State.Idle;
                        return;
                    }

                    SetDestinationSmart(_deposit.DropPosition);
                    _state = State.GoingToDrop;
                }
                return;
            }

            // Ya hay dep車sito -> ir
            Vector3 dropPos = _deposit.DropPosition;

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
                    SetDestinationSmart(_targetNode.transform.position);
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
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas))
                _agent.SetDestination(hit.position);
            else
                _agent.SetDestination(desired);
        }
		
		public void AbortGatherAndBankCarried()
		{
			// Deposita instantaneo lo que lleva para evitar perder recursos y evitar “ping-pong”
			if (owner != null && _carried > 0)
				owner.Add(_carriedKind, _carried);

			_carried = 0;
			_targetNode = null;
			_deposit = null;

			_gatherTimer = 0f;
			_retryTimer = 0f;

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

			if (_agent != null && _agent.isOnNavMesh)
				_agent.ResetPath();
			_state = State.Idle; // si existe en tu script
		}

		
    }
}
