using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using Project.Gameplay.Combat;
using Project.UI;

namespace Project.Gameplay.Units
{
    /// <summary>
    /// Estadísticas de unidad en partida: valores base (desde UnitSO) + modificadores (mejoras, debuffs, auras).
    /// Health, UnitMover y combate leen de aquí cuando existe; si no, usan valores del prefab/SO directo.
    /// </summary>
    public class UnitStatsRuntime : MonoBehaviour
    {
        [Header("Base (asignado al spawn desde UnitSO)")]
        [Tooltip("Opcional en prefabs colocados en escena: asigna el SO para nombre en HUD si no pasó por InitFromUnitSO.")]
        [SerializeField] UnitSO definitionInEditor;
        UnitSO _definitionFromSpawn;

        [SerializeField] int _baseMaxHP = 100;
        [SerializeField] int _baseAttack = 10;
        [SerializeField] float _baseAttackRange = 1.5f;
        [SerializeField] float _baseAttackIntervalSec = 1.3f;
        [SerializeField] float _baseMoveSpeed = 3.8f;
        [SerializeField] int _baseArmor = 0;
        [SerializeField] int _baseMagicResist = 0;
        [SerializeField] int _baseBonusDamageVsCavalry = 0;

        // Modificadores: add se suma al base, mul es % (0 = sin cambio, 0.2 = +20%). Efectivo = (base + add) * (1 + mul).
        float _addMaxHP;
        float _mulMaxHP;
        float _addAttack;
        float _mulAttack;
        float _addAttackRange;
        float _mulAttackRange;
        float _addAttackInterval;
        float _mulAttackInterval;
        float _addMoveSpeed;
        float _mulMoveSpeed;
        float _addArmor;
        float _mulArmor;
        float _addMagicResist;
        float _mulMagicResist;
        float _addBonusVsCavalry;
        float _mulBonusVsCavalry;

        /// <summary>SO usado para stats y nombre en HUD (spawn o inspector).</summary>
        public UnitSO ResolvedUnitDefinition => _definitionFromSpawn != null ? _definitionFromSpawn : definitionInEditor;

        /// <summary>Inicializa valores base desde el UnitSO (llamar al spawnear).</summary>
        public void InitFromUnitSO(UnitSO so)
        {
            if (so == null) return;
            _definitionFromSpawn = so;
            _baseMaxHP = so.maxHP;
            _baseAttack = so.attack;
            _baseAttackRange = so.attackRange;
            _baseAttackIntervalSec = so.attackIntervalSec;
            _baseMoveSpeed = so.moveSpeed;
            _baseArmor = so.armor;
            _baseMagicResist = so.magicResist;
            _baseBonusDamageVsCavalry = so.bonusDamageVsCavalry;

            var health = GetComponent<Health>();
            if (health != null)
                health.InitFromMax(GetEffectiveMaxHP());

            var agent = GetComponent<NavMeshAgent>();
            if (agent != null)
                agent.speed = GetEffectiveMoveSpeed();
        }

        public int GetEffectiveMaxHP() => Mathf.Max(1, Mathf.RoundToInt((_baseMaxHP + _addMaxHP) * (1f + _mulMaxHP)));
        public int GetEffectiveAttack() => Mathf.Max(0, Mathf.RoundToInt((_baseAttack + _addAttack) * (1f + _mulAttack)));
        public float GetEffectiveAttackRange() => Mathf.Max(0.5f, (_baseAttackRange + _addAttackRange) * (1f + _mulAttackRange));
        public float GetEffectiveAttackIntervalSec() => Mathf.Max(0.3f, (_baseAttackIntervalSec + _addAttackInterval) * (1f + _mulAttackInterval));
        public float GetEffectiveMoveSpeed() => Mathf.Max(0.5f, (_baseMoveSpeed + _addMoveSpeed) * (1f + _mulMoveSpeed));
        public int GetEffectiveArmor() => Mathf.Max(0, Mathf.RoundToInt((_baseArmor + _addArmor) * (1f + _mulArmor)));
        public int GetEffectiveMagicResist() => Mathf.Max(0, Mathf.RoundToInt((_baseMagicResist + _addMagicResist) * (1f + _mulMagicResist)));
        public int GetEffectiveBonusDamageVsCavalry() => Mathf.Max(0, Mathf.RoundToInt((_baseBonusDamageVsCavalry + _addBonusVsCavalry) * (1f + _mulBonusVsCavalry)));

        // API para mejoras/debuffs (durante la partida)
        public void AddModifierMaxHP(float add, float mulPercent = 0f) { _addMaxHP += add; _mulMaxHP += mulPercent; }
        public void AddModifierAttack(float add, float mulPercent = 0f) { _addAttack += add; _mulAttack += mulPercent; }
        public void AddModifierMoveSpeed(float add, float mulPercent = 0f) { _addMoveSpeed += add; _mulMoveSpeed += mulPercent; ApplyMoveSpeedToAgent(); }
        public void AddModifierArmor(float add, float mulPercent = 0f) { _addArmor += add; _mulArmor += mulPercent; }
        public void AddModifierMagicResist(float add, float mulPercent = 0f) { _addMagicResist += add; _mulMagicResist += mulPercent; }

        void ApplyMoveSpeedToAgent()
        {
            var agent = GetComponent<NavMeshAgent>();
            if (agent != null) agent.speed = GetEffectiveMoveSpeed();
        }

        /// <summary>Nombre para HUD: displayName del SO o id/objeto humanizado.</summary>
        public string GetHudDisplayName()
        {
            var so = ResolvedUnitDefinition;
            if (so != null)
            {
                if (!string.IsNullOrWhiteSpace(so.displayName)) return so.displayName.Trim();
                if (!string.IsNullOrWhiteSpace(so.id)) return SelectionDisplayName.HumanizeId(so.id);
            }
            return SelectionDisplayName.HumanizeId(gameObject.name);
        }
    }
}
