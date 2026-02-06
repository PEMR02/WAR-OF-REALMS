using UnityEngine;

namespace Project.Gameplay.Buildings
{
    [CreateAssetMenu(menuName = "Project/Building")]
    public class BuildingSO : ScriptableObject
    {
        public string id;
        public GameObject prefab;

        [Header("Footprint")]
        [Tooltip("Tamaño en celdas de la grilla (ej. 2x2 = 2 celdas de ancho x 2 de fondo). El tamaño en metros = size × cellSize del MapGrid/GridConfig.")]
        public Vector2 size = new Vector2(4, 4);

        [Header("Placement")]
        public bool requiresFlatGround = false;

        [Header("Costs")]
        public Cost[] costs;

        [System.Serializable]
        public class Cost
        {
            public Project.Gameplay.Resources.ResourceKind kind;
            public int amount;
        }
		
		public float buildTimeSeconds = 10f;
		
		[Header("Population")]
		[Tooltip("Cuántos slots de población proporciona este edificio (ej: Casa = 5)")]
		public int populationProvided = 0;

    }
}
