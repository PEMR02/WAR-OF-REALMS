using UnityEngine;
using Project.UI;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Datos base de un edificio. Los valores aquí son la base; durante la partida
    /// pueden modificarse por mejoras, debuffs o auras (futuro: BuildingStatsRuntime
    /// análogo a UnitStatsRuntime, con GetEffectiveMaxHP(), etc.).
    /// </summary>
    [CreateAssetMenu(menuName = "Project/Building")]
    public class BuildingSO : ScriptableObject
    {
        public string id;
        [Tooltip("Nombre en HUD. Vacío = se muestra el id legible (sin guiones bajos).")]
        public string displayName;
        public GameObject prefab;

        public string GetDisplayName() =>
            string.IsNullOrWhiteSpace(displayName) ? SelectionDisplayName.HumanizeId(id) : displayName.Trim();

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

        [Header("Compound (cercos / segmentos)")]
        [Tooltip("Si true, al completar la construcción se generan N segmentos (compoundSegmentPrefab) en lugar de un solo prefab.")]
        public bool isCompound = false;
        [Tooltip("Prefab de cada segmento (ej. tramo de valla). Se instancia compoundSegmentCount veces o a lo largo del path.")]
        public GameObject compoundSegmentPrefab;
        [Tooltip("Número de segmentos a colocar (solo si compoundPathMode = false).")]
        [Min(1)] public int compoundSegmentCount = 1;
        [Tooltip("Separación en metros entre segmentos en línea recta (solo si compoundPathMode = false).")]
        public float compoundSpacing = 2f;

        [Header("Compound Path (muros/cercas orgánicos — estilo Fence Layout)")]
        [Tooltip("Si true, el jugador coloca varios puntos y los segmentos siguen ese recorrido (path). Requiere pathPoints en el BuildSite.")]
        public bool compoundPathMode = false;
        [Tooltip("Longitud en metros de cada tramo a lo largo del path (segmentos colocados cada X metros).")]
        public float compoundSegmentLength = 2f;
        [Tooltip("Ajustar cada segmento al terreno con raycast (recomendado para terreno irregular).")]
        public bool compoundPathRaycastTerrain = true;
        [Tooltip("Capa para el raycast de terreno (Default, Terrain, etc.).")]
        public LayerMask compoundPathGroundMask = -1;
        [Tooltip("Rotación extra en grados (Euler X, Y, Z) aplicada a cada segmento. Ej: (-90, 0, 0) si el prefab del tramo está 'tumbado' y hay que levantarlo.")]
        public Vector3 compoundSegmentRotationOffset = new Vector3(-90f, 0f, 0f);
        [Header("Compound Corner (torre en giros)")]
        [Tooltip("Prefab opcional para colocar en cada cambio de dirección del path (ej. torre que une muros).")]
        public GameObject compoundCornerPrefab;
        [Tooltip("Ángulo mínimo en grados entre tramos para colocar una torre (ej. 15 = solo en giros claros).")]
        [Range(5f, 90f)] public float compoundCornerMinAngleDeg = 15f;
        [Tooltip("Si true, también coloca una torre en el primer y último punto del path.")]
        public bool compoundPlaceCornerAtEndpoints = false;
        [Header("Compound Gate (puerta en el muro)")]
        [Tooltip("Prefab opcional de puerta. Si se marca un punto del path como puerta (ej. Shift+clic), en ese tramo se instancia este prefab en lugar del segmento, con GateOpener para pathfinding.")]
        public GameObject compoundGatePrefab;
        [Tooltip("(Muro) Rotación extra (Euler) al colocar compoundGatePrefab en el path. Ej: (0, 90, 0) para alinear la puerta con el tramo.")]
        public Vector3 compoundGateRotationOffset = Vector3.zero;
        [Tooltip("(Puerta) Al reemplazar un segmento de muro por esta puerta, rotación extra (Euler) para alinear con el muro. Ej: (0, 90, 0).")]
        public Vector3 gateReplacementRotationOffset = Vector3.zero;
    }
}
