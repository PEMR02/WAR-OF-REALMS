using UnityEngine;
using Project.Gameplay.Buildings;
using Project.Gameplay.Faction;
using Project.Gameplay.Units;

namespace Project.Gameplay.Combat
{
    /// <summary>
    /// Ataque automático básico para edificios con capacidad ofensiva.
    /// </summary>
    public sealed class BuildingAttacker : MonoBehaviour
    {
        [Header("Runtime Config")]
        public BuildingSO source;
        public bool debugLogs = false;

        float _nextAttackAt;
        float _nextScanAt;
        Transform _target;
        IHealth _targetHealth;
        FactionMember _selfFaction;

        const float ScanInterval = 0.25f;
        static readonly System.Collections.Generic.List<Health> s_healthCache = new System.Collections.Generic.List<Health>(256);
        static float s_nextHealthCacheRefresh;
        const float HealthCacheRefreshInterval = 0.5f;

        public bool HasValidAttackData =>
            source != null &&
            source.canAttack &&
            source.attackDamage > 0 &&
            source.attackRange > 0f &&
            source.attackCooldown > 0f;

        void Awake()
        {
            _selfFaction = GetComponentInParent<FactionMember>();
        }

        void Update()
        {
            if (!HasValidAttackData)
                return;

            if (Time.time >= _nextScanAt)
            {
                _nextScanAt = Time.time + ScanInterval;
                RefreshTarget();
            }

            if (_target == null || _targetHealth == null || !_targetHealth.IsAlive)
                return;

            float range = Mathf.Max(0.1f, source.attackRange);
            if ((_target.position - transform.position).sqrMagnitude > range * range)
                return;

            if (Time.time < _nextAttackAt)
                return;

            _targetHealth.TakeDamage(source.attackDamage, gameObject);
            float cooldown = source.attackCooldown > 0f ? source.attackCooldown : 1f;
            _nextAttackAt = Time.time + cooldown;
        }

        void RefreshTarget()
        {
            if (Time.time >= s_nextHealthCacheRefresh || s_healthCache.Count == 0)
            {
                s_nextHealthCacheRefresh = Time.time + HealthCacheRefreshInterval;
                s_healthCache.Clear();
                var allNow = Object.FindObjectsByType<Health>(FindObjectsSortMode.None);
                for (int i = 0; i < allNow.Length; i++)
                    if (allNow[i] != null)
                        s_healthCache.Add(allNow[i]);
            }
            Transform best = null;
            IHealth bestHealth = null;
            float bestSqr = float.MaxValue;
            float range = Mathf.Max(0.1f, source.attackRange);
            float rangeSqr = range * range;
            Vector3 from = transform.position;
            for (int i = 0; i < s_healthCache.Count; i++)
            {
                var h = s_healthCache[i];
                if (h == null || !h.IsAlive) continue;
                var t = h.transform;
                if (t == transform) continue;
                var targetFaction = t.GetComponentInParent<FactionMember>();
                if (_selfFaction != null && targetFaction != null && !FactionMember.IsHostile(_selfFaction.faction, targetFaction.faction))
                    continue;
                if (!source.attackTargetsGroundUnits && t.GetComponentInParent<UnitMover>() == null)
                    continue;
                float sqr = (t.position - from).sqrMagnitude;
                if (sqr > rangeSqr || sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = t;
                bestHealth = h;
            }

            _target = best;
            _targetHealth = bestHealth;
            if (debugLogs && _target == null)
                Debug.Log($"[Combat] {name} sin objetivo válido en rango.");
        }

        void OnDrawGizmosSelected()
        {
            if (source == null || source.attackRange <= 0f) return;
            Gizmos.color = new Color(1f, 0.7f, 0.1f, 0.85f);
            Gizmos.DrawWireSphere(transform.position, source.attackRange);
        }
    }
}
