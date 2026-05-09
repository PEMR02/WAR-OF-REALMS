using UnityEngine;
using Project.Gameplay.Units;
using Project.Gameplay.AI;
using Project.Gameplay.Faction;
using Project.Gameplay.Buildings;

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
        [Header("Villager Defense")]
        public float villagerDefenseRange = 8f;
        public float villagerMaxChaseDistance = 10f;
        [Header("Military Chase")]
        public float militaryMaxChaseDistance = 32f;

        Health _health;
        UnitStatsRuntime _stats;
        UnitMover _mover;
        VillagerGatherer _gatherer;
        Animator _animator;
        bool _skipChaseBecauseEnemyAI;
        float _nextChaseRefresh;
        Vector3 _lastChaseTargetPos;
        float _nextAttackTime;
        IHealth _targetHealth;
        Transform _targetTransform;
        bool _playerOrderedChase;

        const float ChaseInterval = 0.12f;
        const float ChaseRetargetDist = 0.08f;
        const float FlatDistanceYThreshold = 0.2f;

        void Awake()
        {
            _health = GetComponent<Health>();
            _stats = GetComponent<UnitStatsRuntime>();
            _mover = GetComponent<UnitMover>();
            _gatherer = GetComponent<VillagerGatherer>();
            if (_mover == null) _mover = GetComponentInParent<UnitMover>();
            _animator = GetComponent<Animator>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            _skipChaseBecauseEnemyAI = GetComponent<EnemyAI>() != null && !FactionMember.IsPlayerCommandable(gameObject);
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
            float distSq = GetSqrDistanceToAttackTarget();

            if (distSq > rangeSq)
            {
                float maxChase = _gatherer != null ? villagerMaxChaseDistance : militaryMaxChaseDistance;
                if (_playerOrderedChase)
                    maxChase = Mathf.Max(maxChase, 120f);
                if (maxChase > 0f && distSq > maxChase * maxChase)
                {
                    ClearTarget();
                    return;
                }
                if (!_skipChaseBecauseEnemyAI && _mover != null && chaseTargetWhenOutOfRange)
                {
                    Vector3 chaseDest = GetChaseDestinationWorld();
                    if (Time.time >= _nextChaseRefresh || (chaseDest - _lastChaseTargetPos).sqrMagnitude >= ChaseRetargetDist * ChaseRetargetDist)
                    {
                        _nextChaseRefresh = Time.time + ChaseInterval;
                        _lastChaseTargetPos = chaseDest;
                        if (_playerOrderedChase)
                            _mover.RequestPlayerMove(chaseDest);
                        else
                            _mover.MoveTo(chaseDest);
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
                if (debugLogs)
                {
                    bool building = attackTarget != null && attackTarget.GetComponentInParent<BuildingOwnership>() != null;
                    if (building)
                        Debug.Log($"[Combat] Damage applied to building {attackTarget.root.name} amount={damage}");
                    else
                        Debug.Log($"{name} golpeó {attackTarget.name} por {damage}");
                }
            }

            _nextAttackTime = Time.time + GetAttackInterval();
        }

        void FireAttackAnimation()
        {
            if (_animator == null || !gameObject.activeInHierarchy || string.IsNullOrEmpty(attackAnimatorTrigger))
                return;
            if (!HasAnimatorParameter(_animator, attackAnimatorTrigger, AnimatorControllerParameterType.Trigger))
            {
                if (debugLogs)
                    Debug.Log($"[Combat] Animator sin trigger '{attackAnimatorTrigger}' en {name}. Se aplica daño sin animación.");
                return;
            }
            _animator.SetTrigger(attackAnimatorTrigger);
        }

        static bool HasAnimatorParameter(Animator animator, string parameterName, AnimatorControllerParameterType expectedType)
        {
            if (animator == null || string.IsNullOrEmpty(parameterName))
                return false;
            var parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == parameterName && parameters[i].type == expectedType)
                    return true;
            }
            return false;
        }

        void OnDrawGizmosSelected()
        {
            float r = GetAttackRange();
            if (r <= 0f) return;
            Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.85f);
            Gizmos.DrawWireSphere(transform.position, r);
        }

        public void SetTarget(Transform target)
        {
            attackTarget = target;
            _playerOrderedChase = false;
            _nextChaseRefresh = 0f;
            CacheTarget(target);
        }

        public void EnableChaseForCurrentOrder()
        {
            chaseTargetWhenOutOfRange = true;
            _playerOrderedChase = true;
            _nextChaseRefresh = 0f;
        }

        public void ClearTarget()
        {
            attackTarget = null;
            _playerOrderedChase = false;
            _targetHealth = null;
            _targetTransform = null;
        }

        public bool HasValidTarget => attackTarget != null && _targetHealth != null && _targetHealth.IsAlive;
        public float GetAttackRange()
        {
            float range = _stats != null ? _stats.GetEffectiveAttackRange() : 1.5f;
            if (range > 0f) return range;
            return GetComponent<VillagerGatherer>() != null ? 1.2f : 1.5f;
        }

        public float GetAttackInterval()
        {
            float interval = _stats != null ? _stats.GetEffectiveAttackIntervalSec() : 1.3f;
            if (interval <= 0f) return 1f;
            if (_gatherer != null && interval < 0.9f) return 0.9f;
            return interval;
        }

        public int GetAttackDamage()
        {
            int dmg = _stats != null ? _stats.GetEffectiveAttack() : 10;
            if (dmg > 0) return dmg;
            if (GetComponent<VillagerGatherer>() != null)
                return 2;
            return 0;
        }

        void CacheTarget(Transform target)
        {
            _targetTransform = target;
            if (target == null)
            {
                _targetHealth = null;
                return;
            }
            Health h = target.GetComponent<Health>();
            if (h == null) h = target.GetComponentInParent<Health>();
            if (h == null) h = target.GetComponentInChildren<Health>(true);
            _targetHealth = h;
        }

        /// <summary>
        /// Distancia al borde del objetivo (colliders). Evita que melee nunca alcance el pivote de edificios grandes.
        /// </summary>
        float GetSqrDistanceToAttackTarget()
        {
            if (attackTarget == null) return float.PositiveInfinity;
            Vector3 from = transform.position;
            if (TryGetClosestPointOnTargetColliders(from, out _, out float bestSqr))
                return bestSqr;
            Vector3 dflat = attackTarget.position - from;
            dflat.y = 0f;
            return dflat.sqrMagnitude;
        }

        /// <summary>
        /// Destino de persecución: borde del collider más cercano al atacante, no el pivote (crítico vs edificios grandes).
        /// </summary>
        Vector3 GetChaseDestinationWorld()
        {
            if (attackTarget == null) return transform.position;
            Vector3 from = transform.position;
            if (TryGetClosestPointOnTargetColliders(from, out Vector3 closest, out _))
                return closest;
            return attackTarget.position;
        }

        bool TryGetClosestPointOnTargetColliders(Vector3 from, out Vector3 closestWorld, out float bestSqrDist)
        {
            closestWorld = attackTarget != null ? attackTarget.position : from;
            bestSqrDist = float.PositiveInfinity;
            var healthMb = _targetHealth as MonoBehaviour;
            if (healthMb != null)
            {
                var cols = healthMb.GetComponentsInChildren<Collider>(true);
                if (cols != null && cols.Length > 0)
                {
                    for (int i = 0; i < cols.Length; i++)
                    {
                        var c = cols[i];
                        if (c == null || !c.enabled) continue;
                        Vector3 closest = c.ClosestPoint(from);
                        float d = SqrFlatDistance(from, closest);
                        if (d < bestSqrDist)
                        {
                            bestSqrDist = d;
                            closestWorld = closest;
                        }
                    }
                    if (bestSqrDist < float.PositiveInfinity)
                        return true;
                }
            }
            return false;
        }

        static float SqrFlatDistance(in Vector3 a, in Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            float dy = a.y - b.y;
            if (Mathf.Abs(dy) <= FlatDistanceYThreshold)
                dy = 0f;
            return dx * dx + dz * dz + dy * dy;
        }
    }
}
