using UnityEngine;
using UnityEngine.AI;
using System.Collections;

namespace Project.Gameplay.Resources
{
    /// <summary>
    /// Comportamiento de animal de pasto: alterna entre estar quieto, "comer" (idle) y caminar a un punto aleatorio.
    /// Si una unidad se acerca, se aleja poco a poco (huida suave).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class AnimalPastureBehaviour : MonoBehaviour
    {
        public enum State { Idle, Grazing, Walking, Fleeing }

        [Header("Recorrido aleatorio")]
        [Tooltip("Radio máximo para elegir un punto de paseo (desde la posición actual).")]
        public float wanderRadius = 8f;
        [Tooltip("Tiempo mínimo en Idle (segundos).")]
        public float idleTimeMin = 2f;
        [Tooltip("Tiempo máximo en Idle (segundos).")]
        public float idleTimeMax = 6f;
        [Tooltip("Tiempo mínimo \"comiendo\" / quieto (segundos).")]
        public float grazingTimeMin = 3f;
        [Tooltip("Tiempo máximo \"comiendo\" (segundos).")]
        public float grazingTimeMax = 8f;
        [Tooltip("Velocidad al caminar (m/s).")]
        public float walkSpeed = 0.8f;
        [Tooltip("Velocidad al huir (m/s).")]
        public float fleeSpeed = 1.2f;

        [Header("Huir de unidades")]
        [Tooltip("Capa(s) de las unidades que hacen huir al animal.")]
        public LayerMask unitLayerMask = -1;
        [Tooltip("Distancia a la que empieza a huir.")]
        public float fleeStartDistance = 4f;
        [Tooltip("Distancia a la que deja de huir.")]
        public float fleeStopDistance = 8f;
        [Tooltip("Radio de OverlapSphere para buscar unidades (no demasiado grande).")]
        public float detectionRadius = 12f;

        [Header("Animator (opcional)")]
        [Tooltip("Si está en otro hijo, se busca en hijos. Si no, se usa el del mismo objeto.")]
        public Animator animator;
        public string speedParameter = "Speed";
        [Tooltip("Nombre del estado Idle en el Animator (para reiniciar si el clip no hace loop).")]
        public string idleStateName = "Idle";
        [Tooltip("Suavizado del Speed del Animator para que no se corte la animación al caminar (segundos).")]
        [Min(0.01f)]
        public float animSpeedSmoothTime = 0.2f;

        NavMeshAgent _agent;
        Vector3 _homePosition;
        State _state = State.Idle;
        float _stateTimer;
        Transform _nearestUnit;
        float _idleAnimTimer;
        float _smoothedAnimSpeed;
        readonly Collider[] _unitDetectionBuffer = new Collider[24];

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            _agent.speed = walkSpeed;
            _agent.angularSpeed = 120f;
            _agent.acceleration = 2f;
            _agent.stoppingDistance = 0.2f;
            _agent.autoBraking = true;
        }

        void Start()
        {
            _homePosition = transform.position;
            if (_agent.isOnNavMesh)
                _agent.ResetPath();
            StartCoroutine(BehaviourLoop());
        }

        void Update()
        {
            UpdateFleeFromUnits();

            if (_state == State.Fleeing && _nearestUnit != null && _agent != null && _agent.isOnNavMesh && _agent.enabled)
            {
                Vector3 away = (transform.position - _nearestUnit.position).normalized;
                Vector3 fleeTarget = transform.position + away * fleeStopDistance;
                fleeTarget.y = transform.position.y;
                if (NavMesh.SamplePosition(fleeTarget, out var hit, fleeStopDistance, NavMesh.AllAreas))
                {
                    _agent.speed = fleeSpeed;
                    _agent.SetDestination(hit.position);
                }
            }

            if ((_state == State.Idle || _state == State.Grazing) && animator != null && animator.isInitialized)
            {
                _idleAnimTimer -= Time.deltaTime;
                if (_idleAnimTimer <= 0f)
                {
                    _idleAnimTimer = 1.5f;
                    animator.Play(idleStateName, 0, Random.Range(0f, 0.9f));
                }
            }
            else
                _idleAnimTimer = 1.5f;
        }

        void LateUpdate()
        {
            SyncAnimatorSpeed();
        }

        void SyncAnimatorSpeed()
        {
            if (animator == null || !animator.isInitialized) return;

            // Solo por estado: en Walking/Fleeing la animación Walk todo el rato. hasPath/remainingDistance
            // fallan en medio del camino (recalculos) y la vaca quedaba estática.
            float targetSpeed = 0f;
            if (_state == State.Walking)
                targetSpeed = walkSpeed;
            else if (_state == State.Fleeing)
                targetSpeed = fleeSpeed;
            else if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
                targetSpeed = _agent.velocity.magnitude;

            float smooth = Mathf.Clamp01(Time.deltaTime / Mathf.Max(0.01f, animSpeedSmoothTime));
            _smoothedAnimSpeed = Mathf.Lerp(_smoothedAnimSpeed, targetSpeed, smooth);
            animator.SetFloat(speedParameter, _smoothedAnimSpeed);
        }

        void UpdateFleeFromUnits()
        {
            if (unitLayerMask == 0) return;

            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, detectionRadius, _unitDetectionBuffer, unitLayerMask);
            _nearestUnit = null;
            float nearestDistSq = (detectionRadius + 1f) * (detectionRadius + 1f);

            for (int i = 0; i < hitCount; i++)
            {
                var c = _unitDetectionBuffer[i];
                if (c == null) continue;
                if (c.isTrigger) continue;
                float dSq = (c.transform.position - transform.position).sqrMagnitude;
                if (dSq < nearestDistSq)
                {
                    nearestDistSq = dSq;
                    _nearestUnit = c.transform;
                }
            }

            float fleeStartDistanceSq = fleeStartDistance * fleeStartDistance;
            float fleeStopDistanceSq = fleeStopDistance * fleeStopDistance;

            if (_nearestUnit != null && nearestDistSq < fleeStartDistanceSq)
                _state = State.Fleeing;
            else if (_state == State.Fleeing && (_nearestUnit == null || nearestDistSq > fleeStopDistanceSq))
                _state = State.Idle;
        }

        IEnumerator BehaviourLoop()
        {
            while (true)
            {
                if (_state == State.Fleeing)
                {
                    yield return null;
                    continue;
                }

                if (!_agent.isOnNavMesh)
                {
                    yield return null;
                    continue;
                }

                switch (_state)
                {
                    case State.Idle:
                        _agent.ResetPath();
                        _stateTimer = Random.Range(idleTimeMin, idleTimeMax);
                        while (_stateTimer > 0 && _state != State.Fleeing)
                        {
                            _stateTimer -= Time.deltaTime;
                            yield return null;
                        }
                        _state = State.Grazing;
                        break;

                    case State.Grazing:
                        _agent.ResetPath();
                        _stateTimer = Random.Range(grazingTimeMin, grazingTimeMax);
                        while (_stateTimer > 0 && _state != State.Fleeing)
                        {
                            _stateTimer -= Time.deltaTime;
                            yield return null;
                        }
                        _state = State.Walking;
                        break;

                    case State.Walking:
                        if (_agent.isOnNavMesh)
                        {
                            Vector3 randomPoint = _homePosition + Random.insideUnitSphere * wanderRadius;
                            randomPoint.y = _homePosition.y;
                            if (NavMesh.SamplePosition(randomPoint, out var hit, wanderRadius, NavMesh.AllAreas))
                            {
                                _agent.SetDestination(hit.position);
                                _agent.speed = walkSpeed;
                                while (_agent.pathPending || (_agent.remainingDistance > _agent.stoppingDistance + 0.2f && _state != State.Fleeing))
                                    yield return null;
                            }
                        }
                        _state = State.Idle;
                        break;
                }

                yield return null;
            }
        }
    }
}
