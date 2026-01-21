using UnityEngine;

namespace Project.Gameplay.Units
{
    [CreateAssetMenu(menuName = "Project/Unit")]
    public class UnitSO : ScriptableObject
    {
        [Header("Identity")]
        public string id;                 // "militia", "archer", "villager"
        public string displayName;        // "Milicia", "Arquero"
        
        [Header("Prefab")]
        public GameObject prefab;        // Prefab de la unidad

        [Header("Costs")]
        public Cost[] costs;
        
        [System.Serializable]
        public class Cost
        {
            public Project.Gameplay.Resources.ResourceKind kind;
            public int amount;
        }
        
        [Header("Production")]
        public float trainingTimeSeconds = 15f;  // Tiempo de entrenamiento
        public int populationCost = 1;          // Cuánta población consume
        
        [Header("Stats (futuro)")]
        public int maxHP = 100;
        public int attack = 10;
        public float moveSpeed = 3.5f;
    }
}
