using UnityEngine;

namespace Project.Gameplay.Units
{
    /// <summary>Rol de la unidad para balance y contadores (no afecta lógica por ahora).</summary>
    public enum UnitRole
    {
        Economy,
        Scout,
        Infantry,
        AntiCavalry,
        HeavyInfantry,
        Ranged,
        Cavalry,
        Siege,
        Hero
    }

    [CreateAssetMenu(menuName = "Project/Unit")]
    public class UnitSO : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public string displayName;
        [Tooltip("Rol para contadores y futuras auras/mejoras.")]
        public UnitRole role = UnitRole.Infantry;

        [Header("Prefab")]
        public GameObject prefab;

        [Header("Costs")]
        public Cost[] costs;

        [System.Serializable]
        public class Cost
        {
            public Project.Gameplay.Resources.ResourceKind kind;
            public int amount;
        }

        [Header("Production")]
        public float trainingTimeSeconds = 15f;
        public int populationCost = 1;

        [Header("Stats base (se modifican en partida por mejoras/debuffs)")]
        [Tooltip("Vida máxima base.")]
        public int maxHP = 100;
        [Tooltip("Daño por golpe (físico).")]
        public int attack = 10;
        [Tooltip("Metros. 1–2 = melé, 7–8 = rango.")]
        public float attackRange = 1.5f;
        [Tooltip("Segundos entre ataques.")]
        public float attackIntervalSec = 1.3f;
        [Tooltip("Velocidad de movimiento (Unity/NavMesh). 2.5–3.5 lento, 3.8–4.5 medio, 5–6.5 rápido.")]
        public float moveSpeed = 3.8f;
        [Tooltip("Reducción de daño físico (0 = ninguna).")]
        [Range(0, 20)]
        public int armor = 0;
        [Tooltip("Reducción de daño mágico (0 = ninguno).")]
        [Range(0, 20)]
        public int magicResist = 0;

        [Header("Bonus opcional")]
        [Tooltip("Bonus de daño vs caballería (ej. Lancero). Se suma a attack cuando el objetivo es cavalry.")]
        public int bonusDamageVsCavalry;
    }
}
