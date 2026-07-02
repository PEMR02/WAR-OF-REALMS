using Project.Gameplay.Map;
using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline
{
    /// <summary>
    /// Configuración de sesión del pipeline unificado. Perfil autosuficiente: no requiere MatchConfig ni RTS en escena.
    /// </summary>
    [CreateAssetMenu(
        fileName = "PMGUnifiedWorldPipelineConfig",
        menuName = "PMG/Unified World Pipeline/Pipeline Config",
        order = 0)]
    public class PMGUnifiedWorldPipelineConfig : ScriptableObject
    {
        [Header("Modo independiente")]
        [Tooltip("Perfil UWP manda (hidrología, mapa, anchos). MatchConfig/RTS solo referencia opcional.")]
        public bool uwpIndependentMode = true;
        [Tooltip("Clonar MapGenConfig.asset como baseline de generación (opcional).")]
        public bool useDefinitiveTemplateBaseline = true;

        [Header("Baseline UWP (motor / MapGen propio)")]
        [Tooltip("Plantilla MapGen del paquete UWP (Assets/Baseline).")]
        public MapGenConfig uwpMapGenBaseline;
        [Tooltip("MatchConfig del paquete UWP (solo referencia layout/seed legacy).")]
        public MatchConfig uwpMatchBaseline;
        [Tooltip("Obsoleto: alias de uwpMapGenBaseline.")]
        public MapGenConfig definitiveTemplate;
        [Tooltip("Obsoleto: alias de uwpMatchBaseline.")]
        public MatchConfig matchConfig;

        [Header("Gameplay UWP")]
        [Range(1, 8)] public int uwpCityCount = 2;

        [Header("Referencia opcional escena")]
        [Tooltip("Componente opcional con capas/materiales (IUwpSceneVisualBindings).")]
        public MonoBehaviour sceneVisualBindingsHost;

        [Header("Layout mapa UWP")]
        [Range(128, 512)] public int uwpGridCells = 358;
        [Range(1.5f, 4f)] public float uwpCellSizeWorld = 3f;
        public bool centerMapAtOrigin = true;

        [Header("Terrain layers (explícito > RTS > MapGen template)")]
        public TerrainLayer grassLayer;
        public TerrainLayer dirtLayer;
        public TerrainLayer rockLayer;
        public TerrainLayer sandLayer;
        public Vector2 grassTileSize = new Vector2(6f, 6f);
        public Vector2 dirtTileSize = new Vector2(6f, 6f);
        public Vector2 rockTileSize = new Vector2(6f, 6f);
        public Vector2 sandTileSize = new Vector2(6f, 6f);
        [Range(1, 6)] public int sandShoreCells = 3;
        public Material terrainMaterialTemplate;

        [Header("Agua / materiales")]
        public Material riverWaterMaterial;
        public Material lakeWaterMaterial;
        public Material seaWaterMaterial;
        public Material tributaryWaterMaterial;

        [Header("Tuning visual río (export)")]
        [Tooltip("Aplica corrección de ensanche en bordes y carve más suave al MapGenConfig runtime.")]
        public bool applyRiverVisualExportFix = true;
        [Tooltip("MouthFusion (pipeline 5). Desactivado por defecto: el agua del juego suele verse mejor con WebFusion (4).")]
        public bool useMouthFusionWaterPipeline = false;
        [Tooltip("Suaviza orillas de río en TerrainExporter (solo heightmap; no toca meshes de agua).")]
        public bool applyRiverBankTerrainFix = true;
        [Tooltip("Ancho total del río principal en celdas (mesh + carve). Tributario ~1.55.")]
        [Range(2f, 12f)] public float uwpMainRiverFullWidthCells = 3.75f;
        [Tooltip("Ancho total tributarios en celdas.")]
        [Range(1f, 6f)] public float uwpTributaryRiverFullWidthCells = 1.55f;
        [Tooltip("Profundidad carve heightmap río principal (metros).")]
        [Range(0.04f, 0.2f)] public float uwpMainRiverCarveDepthWorld = 0.11f;
        [Tooltip("Altura world Y local de Water_RiverSurface_Main. 0 = nivel base del mesh (sin lift).")]
        [Range(0f, 4f)] public float uwpMainRiverSurfaceWorldY = 0f;

        [Header("Hidrología UWP (runtime)")]
        [Tooltip("Troncal del mapa (slot 0 del motor; hoy solo 1).")]
        [Range(1, 1)] public int uwpMainRiverCount = 1;
        [Tooltip("Tributarios que desembocan en el troncal.")]
        [Range(0, 6)] public int uwpTributaryCount = 3;
        [Tooltip("Lagos generados por flood fill.")]
        [Range(0, 12)] public int uwpLakeCount = 2;
        [Tooltip("Tamaño máximo de cada lago en celdas de grid.")]
        [Range(50, 12000)] public int uwpMaxLakeCells = 1400;

        [Header("Import Web Terrain JSON")]
        [Tooltip("JSON del index.html (schema pmg-web-terrain-json).")]
        public TextAsset webTerrainJson;
        public bool jsonUseVisualHeight = true;
        public bool jsonFlipZ = false;
        [Tooltip("Ignora máscaras/plano de agua del JSON y usa WaterGenerator + WaterMeshBuilder del juego.")]
        public bool jsonUseGameWaterSystem = true;

        [Header("Checklist")]
        public PMGUnifiedWorldChecklistAsset checklist;

        [Header("Generación")]
        public int seedOverride;
        public bool useSeedOverride;
        [Range(1, 200)] public int batchSeedCount = 50;
        [Range(1, 30)] public int batchTopN = 12;
        public bool skipRoadConnectivityInPreview = true;
        [Tooltip("Legacy: hidrología desde RTS en escena (desactivado si uwpIndependentMode).")]
        public bool applySceneHydrologyOverrides = false;

        [Header("Pesos evaluación (suma sugerida ≈ 10)")]
        [Range(0f, 3f)] public float weightRivers = 1.4f;
        [Range(0f, 3f)] public float weightRiverEndpoints = 0.9f;
        [Range(0f, 3f)] public float weightLakes = 1.2f;
        [Range(0f, 3f)] public float weightTerrain = 1f;
        [Range(0f, 3f)] public float weightCoastline = 0.8f;
        [Range(0f, 3f)] public float weightNavMesh = 1.2f;
        [Range(0f, 3f)] public float weightCities = 1f;
        [Range(0f, 3f)] public float weightResources = 0.7f;
        [Range(0f, 3f)] public float weightVisualWater = 0.7f;

        [Header("Umbrales RTS (referencia index.html / web)")]
        [Range(0.4f, 0.95f)] public float idealMainRiverSpan01 = 0.62f;
        [Range(2, 12)] public int idealLakeCountMin = 3;
        [Range(2, 12)] public int idealLakeCountMax = 5;
        [Range(40, 400)] public int minLakeCellsIdeal = 80;
        [Range(0.02f, 0.12f)] public float idealLakeCoverage01 = 0.045f;
        [Range(0.25f, 0.9f)] public float idealLakeSpread01 = 0.42f;

        public int ResolveSeed()
        {
            if (useSeedOverride) return seedOverride;
            if (uwpMatchBaseline != null) return uwpMatchBaseline.layout.seed;
            if (matchConfig != null) return matchConfig.layout.seed;
            if (uwpMapGenBaseline != null) return uwpMapGenBaseline.seed;
            if (definitiveTemplate != null) return definitiveTemplate.seed;
            return 424242;
        }

        public MapGenConfig ResolveMapGenBaseline()
        {
            if (uwpMapGenBaseline != null)
                return uwpMapGenBaseline;
            if (uwpIndependentMode)
                return null;
            return definitiveTemplate;
        }

        public MatchConfig ResolveMatchBaseline()
        {
            if (uwpMatchBaseline != null)
                return uwpMatchBaseline;
            if (uwpIndependentMode)
                return null;
            return matchConfig;
        }

        public void EnsureDefaultsFromProject()
        {
#if UNITY_EDITOR
            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (!string.IsNullOrEmpty(assetPath) &&
                assetPath != UwpAssetPaths.PipelineConfig &&
                uwpIndependentMode)
            {
                UnityEngine.Debug.LogWarning(
                    $"[UWP] Config fuera del paquete ({assetPath}). Usa {UwpAssetPaths.PipelineConfig} " +
                    "para modo independiente exportable.");
            }

            SyncBaselineAliases();

            if (checklist == null)
            {
                checklist = UnityEditor.AssetDatabase.LoadAssetAtPath<PMGUnifiedWorldChecklistAsset>(
                    UwpAssetPaths.Checklist);
            }

            if (ResolveMapGenBaseline() == null)
            {
                uwpMapGenBaseline = UnityEditor.AssetDatabase.LoadAssetAtPath<MapGenConfig>(
                    UwpAssetPaths.MapGenBaseline);
                SyncBaselineAliases();
            }

            if (ResolveMatchBaseline() == null)
            {
                uwpMatchBaseline = UnityEditor.AssetDatabase.LoadAssetAtPath<MatchConfig>(
                    UwpAssetPaths.MatchBaseline);
                SyncBaselineAliases();
            }

            PullVisualBindingsFromPipelineOnly();
#endif
        }

        void SyncBaselineAliases()
        {
            if (uwpMapGenBaseline != null) definitiveTemplate = uwpMapGenBaseline;
            else if (definitiveTemplate != null) uwpMapGenBaseline = definitiveTemplate;

            if (uwpMatchBaseline != null) matchConfig = uwpMatchBaseline;
            else if (matchConfig != null) uwpMatchBaseline = matchConfig;
        }

        /// <summary>Materiales/capas: solo este SO + rutas del proyecto; sin RTS ni MapGen template.</summary>
        public void PullVisualBindingsFromPipelineOnly()
        {
            if (grassLayer == null)
                grassLayer = EnsureLayerHasDiffuse(null, UwpAssetPaths.DefaultGrassLayer);
            if (dirtLayer == null)
                dirtLayer = EnsureLayerHasDiffuse(null, UwpAssetPaths.DefaultDirtLayer);
            if (rockLayer == null)
                rockLayer = EnsureLayerHasDiffuse(null, UwpAssetPaths.DefaultRockLayer);
            if (sandLayer == null)
                sandLayer = EnsureLayerHasDiffuse(null, UwpAssetPaths.DefaultSandLayer);

            if (riverWaterMaterial == null)
                riverWaterMaterial = LoadMaterialAtPath(UwpAssetPaths.DefaultRiverMaterial);
            if (lakeWaterMaterial == null)
                lakeWaterMaterial = LoadMaterialAtPath(UwpAssetPaths.DefaultLakeMaterial);
            if (tributaryWaterMaterial == null)
                tributaryWaterMaterial = riverWaterMaterial;

            if (!uwpIndependentMode && definitiveTemplate != null)
                PullVisualBindingsFromTemplateIfEmpty();
        }

#if UNITY_EDITOR
        static Material LoadMaterialAtPath(string assetPath)
        {
            return string.IsNullOrEmpty(assetPath)
                ? null
                : UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        }
#endif

        public void PullVisualBindingsFromTemplateIfEmpty()
        {
            MapGenConfig t = definitiveTemplate;
            if (t == null) return;

            if (grassLayer == null) grassLayer = t.grassLayer;
            if (dirtLayer == null) dirtLayer = t.dirtLayer;
            if (rockLayer == null) rockLayer = t.rockLayer;
            if (sandLayer == null) sandLayer = t.sandLayer;
            if (riverWaterMaterial == null) riverWaterMaterial = t.riverWaterMaterial;
            if (lakeWaterMaterial == null) lakeWaterMaterial = t.lakeWaterMaterial;
            if (seaWaterMaterial == null) seaWaterMaterial = t.seaWaterMaterial;
            if (tributaryWaterMaterial == null) tributaryWaterMaterial = t.tributaryWaterMaterial;
            if (terrainMaterialTemplate == null) terrainMaterialTemplate = t.terrainMaterialTemplateOverride;
#if UNITY_EDITOR
            ValidateTerrainLayerRefs();
#endif
        }

        void ValidateTerrainLayerRefs()
        {
#if UNITY_EDITOR
            grassLayer = EnsureLayerHasDiffuse(grassLayer, UwpAssetPaths.DefaultGrassLayer);
            dirtLayer = EnsureLayerHasDiffuse(dirtLayer, UwpAssetPaths.DefaultDirtLayer);
            rockLayer = EnsureLayerHasDiffuse(rockLayer, UwpAssetPaths.DefaultRockLayer);
            sandLayer = EnsureLayerHasDiffuse(sandLayer, UwpAssetPaths.DefaultSandLayer);
#endif
        }

        public static TerrainLayer EnsureLayerHasDiffuse(TerrainLayer layer, string fallbackPath)
        {
            if (layer != null && layer.diffuseTexture != null)
                return layer;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>(fallbackPath);
#else
            return layer;
#endif
        }

        public bool HasAnyTerrainLayerBinding()
        {
            return grassLayer != null || dirtLayer != null || rockLayer != null || sandLayer != null;
        }
    }
}
