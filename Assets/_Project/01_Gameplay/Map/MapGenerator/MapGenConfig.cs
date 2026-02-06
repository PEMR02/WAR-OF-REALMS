using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Configuración del Generador Definitivo de Mapas. Fuente única de parámetros (sin valores mágicos).</summary>
    [CreateAssetMenu(fileName = "MapGenConfig", menuName = "Map Generator/MapGenConfig", order = 0)]
    public class MapGenConfig : ScriptableObject
    {
        [Header("Grid")]
        public int gridW = 256;
        public int gridH = 256;
        public float cellSizeWorld = 1f;
        public Vector3 origin = Vector3.zero;

        [Header("Seed y reintentos")]
        public int seed = 12345;
        [Tooltip("Reintentos máximos si la validación falla (fairness, ciudades conectadas, etc.).")]
        public int maxRetries = 5;

        [Header("Debug / Logs")]
        [Tooltip("Si está activo, el Generador Definitivo imprime logs detallados por fase. Recomendado OFF para optimizar y limpiar consola.")]
        public bool debugLogs = false;

        [Header("Regiones / biomas")]
        [Tooltip("Cantidad aproximada de macro-regiones para regionId/biomeId.")]
        public int regionCount = 8;
        public float regionNoiseScale = 0.02f;

        [Header("Agua (ríos y lagos en grid)")]
        [Range(0f, 1f)] public float waterHeight01 = 0.25f;
        public int riverCount = 3;
        public int lakeCount = 2;
        [Tooltip("Máximo de celdas por lago (flood fill).")]
        public int maxLakeCells = 200;

        [Header("Ciudades (CityNodes)")]
        public int cityCount = 4;
        [Tooltip("Distancia mínima entre centros de ciudad en celdas.")]
        public int minCityDistanceCells = 40;
        public int cityRadiusCells = 8;
        [Tooltip("Pendiente máxima en grados para colocar una ciudad.")]
        public float maxCitySlopeDeg = 15f;
        [Tooltip("Celdas de separación mínima entre el borde del área de la ciudad y agua/río (evita ciudades pegadas al agua).")]
        public int cityWaterBufferCells = 2;

        [Header("Caminos")]
        [Tooltip("Ancho del camino en celdas (para carve y roadLevel).")]
        public int roadWidthCells = 2;
        [Range(0f, 1f)] public float roadFlattenStrength = 0.8f;

        [Header("Recursos (rings y fairness)")]
        public Vector2Int ringNear = new Vector2Int(6, 12);
        public Vector2Int ringMid = new Vector2Int(12, 24);
        public Vector2Int ringFar = new Vector2Int(24, 50);
        public int minWoodPerCity = 10;
        public int minStonePerCity = 4;
        public int minGoldPerCity = 4;
        public int minFoodPerCity = 6;
        public int maxResourceRetries = 5;

        [Header("Terrain export (alturas y texturas)")]
        [Tooltip("Altura del terreno en unidades mundo (eje Y). Debe coincidir con Height Multiplier del RTS si se usa desde ahí.")]
        public float terrainHeightWorld = 50f;
        [Tooltip("Resolución del heightmap del Terrain (potencia de 2 + 1).")]
        public int heightmapResolution = 513;
        [Tooltip("Pintar terreno por altura (grass/dirt/rock). Asigna Terrain Layers en el RTS o aquí.")]
        public bool paintTerrainByHeight = true;
        [Tooltip("Capas de terreno para pintar por altura (grass = bajo, dirt = medio, rock = alto).")]
        public TerrainLayer grassLayer;
        public TerrainLayer dirtLayer;
        public TerrainLayer rockLayer;
        [Tooltip("Umbrales 0–1 para pintar capas por altura (grass/dirt/rock).")]
        public float grassMaxHeight01 = 0.4f;
        public float dirtMaxHeight01 = 0.65f;
        [Range(0f, 0.25f)] public float textureBlendWidth = 0.05f;

        [Header("Shoreline smoothing (visual)")]
        [Tooltip("Radio en celdas para suavizar el terreno cerca del agua (solo visual al exportar a Terrain).")]
        public int shoreSmoothRadiusCells = 4;
        [Tooltip("Cuánto empuja el terreno hacia la altura del agua en la orilla (0 = nada, 1 = máximo).")]
        [Range(0f, 1f)] public float shoreSmoothStrength = 1f;

        [Header("Agua visual (mesh)")]
        public int waterChunkSize = 32;
        public float waterSurfaceOffset = 0.05f;
        [Tooltip("Material opcional para la malla de agua. Si no se asigna, se usa un fallback azulado.")]
        public Material waterMaterial;
        [Tooltip("Capa de Unity para el GameObject del agua (0 = Default). -1 = usar 0. Debe estar en la Culling Mask de la cámara.")]
        public int waterLayer = -1;

        [Header("Agua - bordes redondeados (Marching Squares)")]
        [Tooltip("Si está activo, genera el agua con Marching Squares (bordes más orgánicos y redondeados).")]
        public bool waterRoundedEdges = true;
        [Tooltip("Subdivisión por celda para el campo (2 = 2x resolución; 4 = más redondeado pero más caro).")]
        [Range(1, 8)] public int waterEdgeSubdiv = 3;
        [Tooltip("Iteraciones de blur del campo (más = bordes más redondeados).")]
        [Range(0, 8)] public int waterEdgeBlurIterations = 2;
        [Tooltip("Radio del blur (en samples del campo).")]
        [Range(1, 4)] public int waterEdgeBlurRadius = 1;
        [Tooltip("Nivel de iso (0..1). 0.5 suele ser el correcto.")]
        [Range(0.05f, 0.95f)] public float waterIsoLevel = 0.5f;

        [Header("Agua - post-proceso de máscara (rápido)")]
        [Tooltip("Suaviza la máscara binaria de agua antes de generar la malla (majority filter). Reduce 'dientes de sierra' sin shaders.")]
        public bool waterMaskPostProcess = true;
        [Tooltip("Iteraciones del suavizado de máscara (0 = off).")]
        [Range(0, 8)] public int waterMaskSmoothIterations = 1;
        [Tooltip("Umbral (0..9) de vecinos+centro para que una celda sea agua tras el suavizado. 5 = mayoría.")]
        [Range(0, 9)] public int waterMaskSmoothThreshold = 5;

        [Header("Agua MS - límites de seguridad")]
        [Tooltip("Máximo de samples (esquinas) para Marching Squares (sw*sh). Si se supera, se hace fallback a agua por chunks (más barato).")]
        public int waterMsMaxCornerSamples = 250000;
    }
}
