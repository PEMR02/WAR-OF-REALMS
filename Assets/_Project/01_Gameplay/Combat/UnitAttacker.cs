using UnityEngine;
using Project.Gameplay.Units;

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

        [Header("Debug")]
        public bool debugLogs = false;

        Health _health;
        UnitStatsRuntime _stats;
        float _nextAttackTime;
        IHealth _targetHealth;
        Transform _targetTransform;

        void Awake()
        {
            _health = GetComponent<Health>();
            _stats = GetComponent<UnitStatsRuntime>();
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
            if ((attackTarget.position - transform.position).sqrMagnitude > rangeSq)
                return;

            if (Time.time < _nextAttackTime)
                return;

            int damage = GetAttackDamage();
            if (damage <= 0) return;

            if (_targetHealth != null)
            {
                _targetHealth.TakeDamage(damage, gameObject);
                if (debugLogs) Debug.Log($"{name} golpeó {attackTarget.name} por {damage}");
            }

            _nextAttackTime = Time.time + GetAttackInterval();
        }

        public void SetTarget(Transform target)
        {
            attackTarget = target;
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
            _targetHealth = target != null ? target.GetComponent<IHealth>() : null;
        }
    }
}
