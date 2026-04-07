using System;
using System.Collections.Generic;
using Project.Gameplay.AI;
using Project.Gameplay.Buildings;
using Project.Gameplay.Map.Generation;
using Project.Gameplay.Map.Generation.Alpha;
using Project.Gameplay.Map.Generator;
using Project.Gameplay.Units;
using UnityEngine;

namespace Project.Gameplay.Map
{
    [CreateAssetMenu(menuName = "Project/Match/Match Config", fileName = "MatchConfig")]
    public sealed class MatchConfig : ScriptableObject
    {
        [Header("Alpha — alto nivel (recomendado para nuevos mapas)")]
        [Tooltip("Si está activo, los bloques Layout/Terreno/Hidrología/etc. pisan los campos legacy (map/geography/water/resources…) al compilar. Desactivar mantiene assets antiguos tal cual.")]
        public bool useHighLevelAlphaConfig = false;
        public LayoutConfig layout = new();
        public TerrainShapeConfig terrainShape = new();
        public HydrologyConfig hydrology = new();
        public RegionClassificationConfig regionClassification = new();
        public ResourceDistributionConfig resourceDistribution = new();
        public PlayerSpawnConfig playerSpawn = new();
        public VisualBindingConfig visualBinding = new();

        [Header("Mapa (legacy / detalle)")]
        public MapSettings map = new();

        [Header("Geografia (legacy / detalle)")]
        public GeographySettings geography = new();

        [Header("Agua")]
        public WaterSettings water = new();

        [Header("Perfil técnico (generador definitivo, opcional)")]
        [Tooltip("Tuning avanzado (MapGen). Si es null, se usa plantilla en escena (deprecated) o baseline de fábrica.")]
        public MapGenerationProfile mapGenerationProfile;

        [Header("Recursos")]
        public ResourceSettings resources = new();

        [Header("Clima")]
        public ClimateSettings climate = new();

        [Header("Jugadores")]
        public PlayerSettings players = new();

        [Header("Inicio")]
        public StartingLoadoutSettings startingLoadout = new();

        [Header("Graficos")]
        public GraphicsProfileSettings graphics = new();

        [Header("Minimapa")]
        public MinimapSettings minimap = new();

        public MatchConfig CreateRuntimeCopy()
        {
            MatchConfig copy = CreateInstance<MatchConfig>();
            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.useHighLevelAlphaConfig = useHighLevelAlphaConfig;
            copy.layout = layout.Clone();
            copy.terrainShape = terrainShape.Clone();
            copy.hydrology = hydrology.Clone();
            copy.regionClassification = regionClassification.Clone();
            copy.resourceDistribution = resourceDistribution.Clone();
            copy.playerSpawn = playerSpawn.Clone();
            copy.visualBinding = visualBinding.Clone();
            copy.map = map.Clone();
            copy.geography = geography.Clone();
            copy.water = water.Clone();
            copy.resources = resources.Clone();
            copy.climate = climate.Clone();
            copy.players = players.Clone();
            copy.startingLoadout = startingLoadout.Clone();
            copy.graphics = graphics.Clone();
            copy.minimap = minimap.Clone();
            copy.mapGenerationProfile = mapGenerationProfile;
            return copy;
        }

        [Serializable]
        public sealed class MapSettings
        {
            public MapPresetType preset = MapPresetType.Continental;
            [Min(16)] public int width = 256;
            [Min(16)] public int height = 256;
            [Min(0.01f)] public float cellSize = 2.5f;
            public bool centerAtOrigin = true;
            public bool randomSeedOnPlay = true;
            public int seed = 12345;
            public string mapType = "Continental";

            public MapSettings Clone() => (MapSettings)MemberwiseClone();
        }

        [Serializable]
        public sealed class GeographySettings
        {
            [Range(0f, 1f)] public float terrainFlatness = 0.6f;
            public float heightMultiplier = 8f;
            [Range(0.0001f, 0.05f)] public float noiseScale = 0.02f;
            [Range(1, 6)] public int noiseOctaves = 3;
            [Range(0f, 1f)] public float noisePersistence = 0.5f;
            [Range(1f, 4f)] public float noiseLacunarity = 2f;
            public float maxSlope = 15f;
            public bool alignTerrainToGrid = true;

            public GeographySettings Clone() => (GeographySettings)MemberwiseClone();
        }

        [Serializable]
        public sealed class WaterSettings
        {
            [Tooltip("Autoridad canónica para el generador definitivo: altura del agua en el heightmap normalizado [0–1].")]
            [Range(0f, 1f)] public float baseHeightNormalized = 0.4f;

            [Tooltip("LEGACY: pasabilidad / flujos viejos. No gobierna waterHeight01 del generador definitivo (ver baseHeightNormalized).")]
            public float waterHeight = -999f;
            [Range(-1f, 1f)] public float waterHeightRelative = -1f;
            public int riverCount = 3;
            public int lakeCount = 2;
            public int maxLakeCells = 800;
            [Tooltip("Radio base del cauce en celdas (más ancho = menos “línea” de 1 celda).")]
            [Range(0, 6)] public int riverWidthRadiusCells = 2;
            [Tooltip("Variación ± del ancho a lo largo del río (0 = uniforme).")]
            [Range(0, 3)] public int riverWidthNoiseAmplitudeCells = 1;
            [Tooltip("Profundidad en celdas de río absorbidas en la boca del lago (confluencia orgánica).")]
            [Range(0, 8)] public int lakeRiverMouthBlendCells = 3;
            public int sandShoreCells = 3;
            public float surfaceOffset = 0.05f;
            public int chunkSize = 32;
            public float alpha = 0.88f;
            public bool showWater = true;
            public WaterMeshMode meshMode = WaterMeshMode.FullPlaneIntersect;
            public int waterLayer = -1;
            public Material material;

            public WaterSettings Clone() => (WaterSettings)MemberwiseClone();
        }

        [Serializable]
        public sealed class ResourceVisualSettings
        {
            public GameObject treePrefab;
            public GameObject[] treePrefabVariants;
            public GameObject berryPrefab;
            public GameObject[] berryPrefabVariants;
            public GameObject animalPrefab;
            public GameObject[] animalPrefabVariants;
            public GameObject goldPrefab;
            public GameObject[] goldPrefabVariants;
            public GameObject stonePrefab;
            public GameObject[] stonePrefabVariants;
            public Material stoneMaterialOverride;
            public Material[] treeMaterialOverrides;
            public Vector3 treePlacementRotation;
            [Tooltip("Layer de Unity para nodos recolectables (nombre).")]
            public string resourceLayerName = "Resource";
            public bool randomRotationPerResource = true;
            [Range(0f, 1f)] public float cellPlacementRandomOffset = 0.8f;
            public bool forceResourceShadowCasting;

            public ResourceVisualSettings Clone() => (ResourceVisualSettings)MemberwiseClone();
        }

        [Serializable]
        public sealed class ResourcePlacementSettings
        {
            [Tooltip("-1 = usar fracción por defecto del colocador (~0.75).")]
            public float globalTreesClusterFraction = -1f;
            public bool preferGlobalTreesOnGrassAlphamap;
            [Range(0.4f, 1f)] public float globalStoneGoldClusterFraction = 0.82f;
            public Vector2Int globalMineralClusterSize = new(2, 6);
            public float globalMineralClusterRadiusCells = 3.2f;

            public ResourcePlacementSettings Clone() => (ResourcePlacementSettings)MemberwiseClone();
        }

        [Serializable]
        public sealed class ResourceSettings
        {
            public Vector2 ringNear = new(6f, 12f);
            public Vector2 ringMid = new(12f, 20f);
            public Vector2 ringFar = new(30f, 50f);
            public Vector2Int nearTrees = new(8, 12);
            public Vector2Int midTrees = new(12, 20);
            public Vector2Int berries = new(6, 8);
            public Vector2Int animals = new(2, 4);
            public Vector2Int goldSafe = new(6, 8);
            public Vector2Int stoneSafe = new(4, 6);
            public Vector2Int goldFar = new(8, 12);
            public Vector2Int globalTrees = new(80, 120);
            public Vector2Int globalStone = new(8, 14);
            public Vector2Int globalGold = new(10, 16);
            public Vector2Int globalAnimals = new(8, 20);
            public float globalExcludeRadius = 55f;
            public bool forestClustering = true;
            [Range(0f, 1f)] public float clusterDensity = 0.6f;
            public int clusterMinSize = 15;
            public int clusterMaxSize = 40;
            public int minWoodTrees = 15;
            public int minGoldNodes = 6;
            public int minStoneNodes = 4;
            public int minFoodValue = 8;
            public int maxResourceRetries = 5;

            [Header("Visuales / prefabs (fuente de verdad)")]
            public ResourceVisualSettings visuals = new();

            [Header("Colocación global avanzada")]
            public ResourcePlacementSettings placement = new();

            public ResourceSettings Clone()
            {
                var c = (ResourceSettings)MemberwiseClone();
                c.visuals = visuals.Clone();
                c.placement = placement.Clone();
                return c;
            }
        }

        [Serializable]
        public sealed class ClimateSettings
        {
            public string climateId = "Temperate";
            public TerrainLayer grassLayer;
            public TerrainLayer dirtLayer;
            public TerrainLayer rockLayer;
            public TerrainLayer sandLayer;
            public bool paintTerrainByHeight = false;
            public Vector2 grassTileSize = Vector2.zero;
            public Vector2 dirtTileSize = Vector2.zero;
            public Vector2 rockTileSize = Vector2.zero;
            public Vector2 sandTileSize = Vector2.zero;
            [Range(0, 100)] public int grassPercent = 60;
            [Range(0, 100)] public int dirtPercent = 20;
            [Range(0, 100)] public int rockPercent = 20;
            [Range(0.02f, 0.25f)] public float textureBlendWidth = 0.08f;
            [Range(0f, 1f)] public float terrainBlendSharpness = 0.2f;
            public float terrainMacroNoiseScale = 0.012f;
            [Range(0f, 0.45f)] public float terrainMacroNoiseStrength = 0.08f;
            public TerrainLayer grassDryLayer;
            [Range(0f, 1f)] public float grassDryBlendStrength = 0.55f;
            public float grassDryNoiseScale = 0.009f;
            public TerrainLayer wetDirtLayer;
            [Range(0.5f, 48f)] public float terrainMoistureRadius = 10f;
            [Range(0f, 1f)] public float terrainMoistureStrength = 0.65f;
            public float terrainMoistureNoiseScale = 0.14f;
            [Range(0f, 1f)] public float terrainMoistureNoiseStrength = 0.35f;
            public float sandEdgeNoiseScale = 0.22f;
            [Range(0f, 2.5f)] public float sandEdgeNoiseStrength = 0.85f;
            public bool debugTerrainMoisture = false;
            public bool debugTerrainMacro = false;
            public bool debugTerrainGrassDry = false;

            public ClimateSettings Clone() => (ClimateSettings)MemberwiseClone();
        }

        [Serializable]
        public sealed class PlayerSettings
        {
            [Range(1, 8)] public int playerCount = 2;
            public List<PlayerSlotSettings> slots = new();
            public float spawnEdgePadding = 20f;
            public float minPlayerDistance2p = 120f;
            public float minPlayerDistance4p = 100f;
            public float spawnFlatRadius = 8f;
            public float maxSlopeAtSpawn = 60f;
            public float waterExclusionRadius = 12f;
            public bool flattenSpawnAreas = true;
            public float flattenRadius = 15f;

            public PlayerSettings Clone()
            {
                PlayerSettings copy = (PlayerSettings)MemberwiseClone();
                copy.slots = new List<PlayerSlotSettings>(slots.Count);
                for (int i = 0; i < slots.Count; i++)
                    copy.slots.Add(slots[i].Clone());
                return copy;
            }
        }

        [Serializable]
        public sealed class PlayerSlotSettings
        {
            public string id = "Player";
            public PlayerSlotKind kind = PlayerSlotKind.Human;
            public string factionId = "Default";
            [Tooltip("Solo aplica si kind = AI. Una misma IA parametrizada por perfil (Easy/Normal/Hard).")]
            public AIDifficulty aiDifficulty = AIDifficulty.Normal;

            public PlayerSlotSettings Clone() => (PlayerSlotSettings)MemberwiseClone();
        }

        public enum PlayerSlotKind
        {
            Human,
            AI,
            Closed
        }

        [Serializable]
        public sealed class StartingLoadoutSettings
        {
            public BuildingSO townCenter;
            public GameObject townCenterPrefabOverride;
            public float townCenterClearRadius = 6f;
            public float townCenterSpawnYOffset = 0f;
            public List<StartingUnitEntry> units = new();
            public List<StartingBuildingEntry> buildings = new();

            public StartingLoadoutSettings Clone()
            {
                StartingLoadoutSettings copy = (StartingLoadoutSettings)MemberwiseClone();
                copy.units = new List<StartingUnitEntry>(units.Count);
                for (int i = 0; i < units.Count; i++)
                    copy.units.Add(units[i].Clone());
                copy.buildings = new List<StartingBuildingEntry>(buildings.Count);
                for (int i = 0; i < buildings.Count; i++)
                    copy.buildings.Add(buildings[i].Clone());
                return copy;
            }
        }

        [Serializable]
        public sealed class StartingUnitEntry
        {
            public UnitSO unit;
            [Min(1)] public int count = 1;

            public StartingUnitEntry Clone() => (StartingUnitEntry)MemberwiseClone();
        }

        [Serializable]
        public sealed class StartingBuildingEntry
        {
            public BuildingSO building;
            [Min(1)] public int count = 1;

            public StartingBuildingEntry Clone() => (StartingBuildingEntry)MemberwiseClone();
        }

        [Serializable]
        public sealed class GraphicsProfileSettings
        {
            public string profileId = "Default";
            public bool showGridInBuildMode = true;
            public bool showWater = true;
            public bool debugLogs = false;

            public GraphicsProfileSettings Clone() => (GraphicsProfileSettings)MemberwiseClone();
        }

        [Serializable]
        public sealed class MinimapSettings
        {
            public bool autoFitWorld = true;
            public bool useFixedOrthographicSize = false;
            [Min(10f)] public float fixedOrthographicSize = 160f;
            [Min(1f)] public float mapPaddingFactor = 1.05f;
            [Min(0.2f)] public float boundsRefreshEvery = 1f;
            public bool cropUvHorizontal = false;
            [Range(0f, 0.45f)] public float uvHorizontalInset = 0f;

            public MinimapSettings Clone() => (MinimapSettings)MemberwiseClone();
        }
    }
}
