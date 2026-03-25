using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Combat;
using Project.Gameplay.Faction;

namespace Project.Gameplay.AI
{
    /// <summary>
    /// Comportamiento de mob: Neutral por defecto. Opcionalmente agresivo al acercarse el jugador (aggro radius)
    /// o al recibir daño (aggro on damage). Cuando se activa, pasa a Enemy y usa EnemyAI para perseguir/atacar.
    /// Requiere FactionMember, NavMeshAgent, Health. Opcional: UnitAttacker + EnemyAI (se activan al volverse hostil).
    /// </summary>
    [RequireComponent(typeof(FactionMember))]
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    public class MobBehaviour : MonoBehaviour
    {
        [Header("Aggro")]
        [Tooltip("Si true, al recibir daño de una unidad del jugador se vuelve hostil y ataca al atacante.")]
        public bool aggroOnDamage = true;
        [Tooltip("Si > 0, al entrar una unidad del jugador en este radio el mob se vuelve hostil (aggro radius).")]
        public float aggroRadius = 0f;
        [Tooltip("Capa de unidades del jugador para detección de aggro radius.")]
        public LayerMask playerUnitLayers = -1;
        [Tooltip("Comprobar aggro radius cada N segundos.")]
        public float aggroCheckInterval = 0.4f;

        [Header("Debug")]
        public bool debugLogs = false;

        FactionMember _faction;
        Health _health;
        EnemyAI _enemyAI;
        UnitAttacker _attacker;
        float _nextAggroCheck;
        bool _isHostile;

        void Awake()
        {
            _faction = GetComponent<FactionMember>();
            _health = GetComponent<Health>();
            _enemyAI = GetComponent<EnemyAI>();
            _attacker = GetComponent<UnitAttacker>();
            if (_enemyAI != null) _enemyAI.enabled = false;
        }

        void OnEnable()
        {
            if (_health != null)
                _health.OnDeath += OnDeath;
        }

        void OnDisable()
        {
            if (_health != null)
                _health.OnDeath -= OnDeath;
        }

        void Start()
        {
            if (_faction != null && _faction.IsNeutral)
                SubscribeToDamage();
        }

        void SubscribeToDamage()
        {
            if (_health == null) return;
            _health.OnDamageReceived += OnDamageReceived;
        }

        void UnsubscribeFromDamage()
        {
            if (_health != null)
                _health.OnDamageReceived -= OnDamageReceived;
        }

        void OnDamageReceived(int amount, object source)
        {
            if (!aggroOnDamage || _isHostile) return;
            var go = source as GameObject;
            if (go == null) return;
            var other = go.GetComponent<FactionMember>();
            if (other == null || !other.IsPlayer) return;
            BecomeHostile(go.transform);
        }

        void OnDeath()
        {
            UnsubscribeFromDamage();
        }

        void Update()
        {
            if (_isHostile) return;
            if (aggroRadius <= 0f) return;
            if (Time.time < _nextAggroCheck) return;
            _nextAggroCheck = Time.time + aggroCheckInterval;

            var player = FindNearestPlayerInRadius(aggroRadius);
            if (player != null)
                BecomeHostile(player);
        }

        Transform FindNearestPlayerInRadius(float radius)
        {
            var hits = Physics.OverlapSphere(transform.position, radius, playerUnitLayers);
            Transform nearest = null;
            float nearestSq = radius * radius;
            for (int i = 0; i < hits.Length; i++)
            {
                var fm = hits[i].GetComponent<FactionMember>();
                if (fm == null || !fm.IsPlayer) continue;
                var ih = hits[i].GetComponent<IHealth>();
                if (ih != null && !ih.IsAlive) continue;
                float sq = (hits[i].transform.position - transform.position).sqrMagnitude;
                if (sq < nearestSq) { nearestSq = sq; nearest = hits[i].transform; }
            }
            return nearest;
        }

        void BecomeHostile(Transform firstTarget)
        {
            if (_isHostile) return;
            _isHostile = true;
            UnsubscribeFromDamage();
            if (_faction != null) _faction.faction = FactionId.Enemy;
            if (_enemyAI != null)
            {
                _enemyAI.enabled = true;
                _enemyAI.SetTarget(firstTarget);
            }
            if (_attacker != null) _attacker.SetTarget(firstTarget);
            if (debugLogs) Debug.Log($"{name} se volvió hostil, objetivo: {firstTarget?.name}");
        }
    }
}
