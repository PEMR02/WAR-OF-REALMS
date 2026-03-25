using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Configuración del Generador Definitivo de Mapas. Fuente única de parámetros (sin valores mágicos).</summary>
    [CreateAssetMenu(fileName = "MapGenConfig", menuName = "Map Generator/MapGenConfig", order = 0)]
    public class MapGenConfig : ScriptableObject
    {
        [Header("Grid (solo plantilla en disco)")]
        [Tooltip("En Play, RTSMapGenerator pisa estos valores con width/height + GridConfig + centerAtOrigin. Edita el grid jugable en el RTS, no aquí.")]
        public int gridW = 256;
        [Tooltip("En Play, lo define RTSMapGenerator.height. Este campo en el asset es solo referencia / escenas sin RTS.")]
        public int gridH = 256;
        [Tooltip("En Play, el tamaño de celda es GridConfig.gridSize en RTSMapGenerator, no este número.")]
        public float cellSizeWorld = 2.5f;
        [Tooltip("En Play, se recalcula desde centerAtOrigin y el transform del RTS. Origen (0,0,0) en el asset suele quedar desfasado del terreno centrado.")]
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
        [Tooltip("Porcentaje 0–1 del mapa para grass (zonas bajas). Si > 0 se usa con dirt/rock para derivar umbrales.")]
        public float grassPercent01 = 0.6f;
        public float dirtPercent01 = 0.2f;
        public float rockPercent01 = 0.2f;
        [Tooltip("Umbrales derivados o legacy: grass hasta este valor, dirt hasta dirtMaxHeight01.")]
        public float grassMaxHeight01 = 0.6f;
        public float dirtMaxHeight01 = 0.8f;
        [Range(0f, 0.25f)] public float textureBlendWidth = 0.08f;
        [Header("Arena en orillas")]
        public TerrainLayer sandLayer;
        [Range(1, 6)] public int sandShoreCells = 3;

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
        [Tooltip("Transparencia del agua (0 = opaco, 0.85 = se ve la arena bajo el agua). Requiere material con soporte alpha.")]
        [Range(0.5f, 1f)] public float waterAlpha = 0.88f;
        [Tooltip("Capa de Unity para el GameObject del agua (0 = Default). -1 = usar 0. Debe estar en la Culling Mask de la cámara.")]
        public int waterLayer = -1;

        [Header("Agua - bordes redondeados (Marching Squares)")]
        [Tooltip("✅ ACTIVAR para bordes orgánicos (elimina esquinas cuadradas). Desactivar solo si quieres agua tipo Minecraft.")]
        public bool waterRoundedEdges = true;
        [Tooltip("Subdivisión por celda (2-4 recomendado). Mayor = bordes más suaves pero más vértices. 3-4 es ideal para la mayoría de casos.")]
        [Range(1, 8)] public int waterEdgeSubdiv = 4;
        [Tooltip("Iteraciones de blur (3-4 recomendado para lagos naturales). Más iteraciones = bordes más redondeados.")]
        [Range(0, 8)] public int waterEdgeBlurIterations = 3;
        [Tooltip("Radio del blur. 2 es óptimo para suavizar sin perder definición.")]
        [Range(1, 4)] public int waterEdgeBlurRadius = 2;
        [Tooltip("Nivel de iso. 0.5 es perfecto (no cambiar a menos que quieras lagos más grandes/pequeños).")]
        [Range(0.05f, 0.95f)] public float waterIsoLevel = 0.5f;

        [Header("Agua - post-proceso de máscara (rápido)")]
        [Tooltip("✅ ACTIVAR para eliminar píxeles solitarios y esquinas afiladas ANTES del Marching Squares. Mejora mucho el resultado.")]
        public bool waterMaskPostProcess = true;
        [Tooltip("Iteraciones del suavizado de máscara (2-3 recomendado). Reduce esquinas aisladas.")]
        [Range(0, 8)] public int waterMaskSmoothIterations = 2;
        [Tooltip("Umbral de vecinos. 5 = mayoría (recomendado). Bajar a 4 hace lagos más grandes, subir a 6 los hace más pequeños.")]
        [Range(0, 9)] public int waterMaskSmoothThreshold = 5;

        [Header("Agua MS - límites de seguridad")]
        [Tooltip("Máximo de samples (esquinas) para Marching Squares (sw*sh). Si se supera, se hace fallback a agua por chunks (más barato).")]
        public int waterMsMaxCornerSamples = 250000;

        [Header("Terrain Skirt (volumen visual)")]
        [Tooltip("Activa las paredes laterales y base que dan volumen al mapa (efecto bloque de tierra).")]
        public bool showTerrainSkirt = true;
        [Tooltip("Profundidad en metros de las paredes laterales bajo el terreno.")]
        public float skirtDepth = 30f;
        [Tooltip("Número de muestras de altura por cada borde del terreno. Más muestras = bordes más suaves.")]
        [Range(32, 512)] public int skirtEdgeSamples = 128;
        [Tooltip("Material URP Lit para paredes y base del skirt (p. ej. MAT_TerrainSkirt_SoilLayers con soil_layers). Si es null, se intenta cargar desde Resources; si falla, shader procedural Custom/TerrainSkirt (bandas de color). Las UV del mesh asumen atlas 4 columnas en soil_layers (Sur/Este/Norte/Oeste).")]
        public Material skirtMaterial;
    }
}
