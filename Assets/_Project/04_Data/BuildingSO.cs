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

		[Header("Combat")]
		[Tooltip("Vida máxima del edificio. Usado por el componente Health.")]
		public int maxHP = 300;
		
		[Header("Population")]
		[Tooltip("Cuántos slots de población proporciona este edificio (ej: Casa = 5)")]
		public int populationProvided = 0;

        [Header("Production")]
        [Tooltip("Dirección de salida de unidades respecto al forward del edificio. 1 = forward, -1 = backward.")]
        [Range(-1f, 1f)] public float unitSpawnForwardSign = -1f;

    }
}
