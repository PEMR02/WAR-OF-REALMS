using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Units;
using Project.Gameplay.Combat;
using Project.Gameplay.Faction;

namespace Project.Gameplay.AI
{
    /// <summary>
    /// IA simple para unidades enemigas: busca el objetivo del jugador más cercano,
    /// se acerca y ataca cuando está en rango. Requiere FactionMember (Enemy), UnitMover, UnitAttacker y Health.
    /// </summary>
    [RequireComponent(typeof(FactionMember))]
    [RequireComponent(typeof(UnitMover))]
    [RequireComponent(typeof(UnitAttacker))]
    [RequireComponent(typeof(Health))]
    public class EnemyAI : MonoBehaviour
    {
        [Header("Detección")]
        [Tooltip("Radio para buscar objetivos del jugador (unidades o edificios).")]
        public float detectionRadius = 25f;
        [Tooltip("Capa de unidades/edificios del jugador. Incluir capa Default o la que usen.")]
        public LayerMask targetLayers = -1;
        [Tooltip("Actualizar objetivo cada N segundos (throttle).")]
        public float retargetInterval = 0.5f;

        [Header("Comportamiento")]
        [Tooltip("Si true, deja de moverse cuando está en rango de ataque (solo ataca).")]
        public bool stopWhenInRange = true;

        [Header("Debug")]
        public bool debugLogs = false;

        FactionMember _faction;
        UnitMover _mover;
        UnitAttacker _attacker;
        float _nextRetargetTime;
        float _nextMoveRefreshTime;
        Transform _currentTarget;
        IHealth _currentTargetHealth;
        Vector3 _lastMoveTargetPosition;

        readonly Collider[] _targetBuffer = new Collider[48];

        const float MoveRefreshInterval = 0.25f;
        const float MoveRefreshDistance = 0.75f;

        void Awake()
        {
            _faction = GetComponent<FactionMember>();
            _mover = GetComponent<UnitMover>();
            _attacker = GetComponent<UnitAttacker>();
            if (_faction != null && _faction.faction != FactionId.Enemy)
                _faction.faction = FactionId.Enemy;
        }

        void Update()
        {
            if (_attacker == null || _mover == null) return;

            // Si tenemos objetivo y sigue vivo, mantener
            if (_currentTarget != null)
            {
                EnsureTargetCache();
                if (_currentTargetHealth == null || !_currentTargetHealth.IsAlive)
                {
                    _currentTarget = null;
                    _currentTargetHealth = null;
                    _attacker.ClearTarget();
                }
            }

            if (_currentTarget == null && Time.time >= _nextRetargetTime)
            {
                _nextRetargetTime = Time.time + retargetInterval;
                FindAndSetTarget();
            }

            if (_currentTarget == null) return;

            float range = _attacker.GetAttackRange();
            float rangeSq = range * range;
            float distSq = (_currentTarget.position - transform.position).sqrMagnitude;

            if (distSq <= rangeSq)
            {
                _attacker.SetTarget(_currentTarget);
                if (stopWhenInRange)
                    _mover.Stop();
            }
            else
            {
                _attacker.ClearTarget();
                Vector3 targetPos = _currentTarget.position;
                if (Time.time >= _nextMoveRefreshTime || (targetPos - _lastMoveTargetPosition).sqrMagnitude >= MoveRefreshDistance * MoveRefreshDistance)
                {
                    _nextMoveRefreshTime = Time.time + MoveRefreshInterval;
                    _lastMoveTargetPosition = targetPos;
                    _mover.MoveTo(targetPos);
                }
            }
        }

        void FindAndSetTarget()
        {
            _currentTarget = FindNearestPlayerTarget();
            EnsureTargetCache();
            if (_currentTarget != null && debugLogs)
                Debug.Log($"{name} nuevo objetivo: {_currentTarget.name}");
        }

        Transform FindNearestPlayerTarget()
        {
            Transform nearest = null;
            float nearestSq = detectionRadius * detectionRadius;

            int count = Physics.OverlapSphereNonAlloc(transform.position, detectionRadius, _targetBuffer, targetLayers);
            for (int i = 0; i < count; i++)
            {
                var hit = _targetBuffer[i];
                if (hit == null) continue;

                var go = hit.gameObject;
                var faction = go.GetComponent<FactionMember>();
                if (faction == null || !faction.IsPlayer) continue;
                var health = go.GetComponent<IHealth>();
                if (health != null && !health.IsAlive) continue;

                float sq = (go.transform.position - transform.position).sqrMagnitude;
                if (sq < nearestSq)
                {
                    nearestSq = sq;
                    nearest = go.transform;
                }
            }

            return nearest;
        }

        public void SetTarget(Transform target)
        {
            _currentTarget = target;
            EnsureTargetCache();
            _nextMoveRefreshTime = 0f;
        }

        void EnsureTargetCache()
        {
            if (_currentTarget == null)
            {
                _currentTargetHealth = null;
                return;
            }

            if (_currentTargetHealth != null && _attacker != null && _attacker.attackTarget == _currentTarget)
                return;

            _currentTargetHealth = _currentTarget.GetComponent<IHealth>();
        }
    }
}
