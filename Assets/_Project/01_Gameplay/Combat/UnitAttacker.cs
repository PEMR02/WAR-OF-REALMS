using UnityEngine;
using Project.Gameplay.Units;
using Project.Gameplay.AI;

namespace Project.Gameplay.Combat
{
    /// <summary>
    /// Ataque cuerpo a cuerpo o a rango: inflige daño a un objetivo cada attackIntervalSec si está en rango.
    /// Usado por unidades del jugador (orden de atacar) y por enemigos/mobs (IA asigna target).
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class UnitAttacker : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Objetivo actual (asignado por orden o por IA).")]
        public Transform attackTarget;

        [Header("Persecución (jugador / órdenes)")]
        [Tooltip("Si hay UnitMover y el objetivo está lejos, acercarse. Desactivado automáticamente si hay EnemyAI (evita doble control).")]
        public bool chaseTargetWhenOutOfRange = true;
        [Tooltip("Nombre del parámetro Trigger en el Animator al impactar (vacío = no animar).")]
        public string attackAnimatorTrigger = "Attack";

        [Header("Debug")]
        public bool debugLogs = false;

        Health _health;
        UnitStatsRuntime _stats;
        UnitMover _mover;
        Animator _animator;
        bool _skipChaseBecauseEnemyAI;
        float _nextChaseRefresh;
        Vector3 _lastChaseTargetPos;
        float _nextAttackTime;
        IHealth _targetHealth;
        Transform _targetTransform;

        const float ChaseInterval = 0.25f;
        const float ChaseRetargetDist = 0.65f;

        void Awake()
        {
            _health = GetComponent<Health>();
            _stats = GetComponent<UnitStatsRuntime>();
            _mover = GetComponent<UnitMover>();
            if (_mover == null) _mover = GetComponentInParent<UnitMover>();
            _animator = GetComponent<Animator>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            _skipChaseBecauseEnemyAI = GetComponent<EnemyAI>() != null;
        }

        void Update()
        {
            if (attackTarget == null)
            {
                _targetHealth = null;
                _targetTransform = null;
                return;
            }

            if (_targetTransform != attackTarget)
                CacheTarget(attackTarget);

            if (_targetHealth != null && !_targetHealth.IsAlive)
            {
                ClearTarget();
                return;
            }

            float range = GetAttackRange();
            float rangeSq = range * range;
            float distSq = (attackTarget.position - transform.position).sqrMagnitude;

            if (distSq > rangeSq)
            {
                if (chaseTargetWhenOutOfRange && !_skipChaseBecauseEnemyAI && _mover != null)
                {
                    Vector3 tp = attackTarget.position;
                    if (Time.time >= _nextChaseRefresh || (tp - _lastChaseTargetPos).sqrMagnitude >= ChaseRetargetDist * ChaseRetargetDist)
                    {
                        _nextChaseRefresh = Time.time + ChaseInterval;
                        _lastChaseTargetPos = tp;
                        _mover.MoveTo(tp);
                    }
                }
                return;
            }

            if (chaseTargetWhenOutOfRange && !_skipChaseBecauseEnemyAI && _mover != null)
                _mover.Stop();

            if (Time.time < _nextAttackTime)
                return;

            int damage = GetAttackDamage();
            if (damage <= 0) return;

            if (_targetHealth != null)
            {
                _targetHealth.TakeDamage(damage, gameObject);
                FireAttackAnimation();
                if (debugLogs) Debug.Log($"{name} golpeó {attackTarget.name} por {damage}");
            }

            _nextAttackTime = Time.time + GetAttackInterval();
        }

        void FireAttackAnimation()
        {
            if (_animator == null || !gameObject.activeInHierarchy || string.IsNullOrEmpty(attackAnimatorTrigger))
                return;
            _animator.SetTrigger(attackAnimatorTrigger);
        }

        public void SetTarget(Transform target)
        {
            attackTarget = target;
            _nextChaseRefresh = 0f;
            CacheTarget(target);
        }

        public void ClearTarget()
        {
            attackTarget = null;
            _targetHealth = null;
            _targetTransform = null;
        }

        public bool HasValidTarget => attackTarget != null && _targetHealth != null && _targetHealth.IsAlive;
        public float GetAttackRange() => _stats != null ? _stats.GetEffectiveAttackRange() : 1.5f;
        public float GetAttackInterval() => _stats != null ? _stats.GetEffectiveAttackIntervalSec() : 1.3f;
        public int GetAttackDamage() => _stats != null ? _stats.GetEffectiveAttack() : 10;

        void CacheTarget(Transform target)
        {
            _targetTransform = target;
            _targetHealth = target != null ? target.GetComponentInParent<IHealth>() : null;
        }
    }
}
