using System;
using UnityEngine;

namespace Project.Gameplay.Map.Generation.Alpha
{
    /// <summary>Alpha: abundancia declarativa (sin geometría manual).</summary>
    public enum AbundanceTier
    {
        None = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        VeryHigh = 4
    }

    public enum MapSpawnPattern
    {
        Corners = 0,
        Edges = 1,
        Balanced = 2,
        Random = 3
    }

    /// <summary>Layout de tablero y escala (alto nivel).</summary>
    [Serializable]
    public sealed class LayoutConfig
    {
        [Min(16)] public int mapWidth = 256;
        [Min(16)] public int mapHeight = 256;
        [Min(0.5f)] public float gridCellSize = 2.5f;
        [Range(1, 8)] public int playerCount = 2;
        public bool randomSeedOnPlay = true;
        public int seed = 12345;
        public bool centerMapAtOrigin = true;
        [Tooltip("Margen interior (celdas) donde no se colocan features críticos en el borde absoluto.")]
        [Min(0)] public int playableMarginCells = 8;
        [Tooltip("Radio base de aplanado cerca de spawns (unidades mundo aprox.; también guía flatten en Match.players).")]
        [Min(4f)] public float baseFlattenRadiusWorld = 15f;
        public MapSpawnPattern spawnPattern = MapSpawnPattern.Balanced;

        public LayoutConfig Clone() => new LayoutConfig
        {
            mapWidth = mapWidth,
            mapHeight = mapHeight,
            gridCellSize = gridCellSize,
            playerCount = playerCount,
            randomSeedOnPlay = randomSeedOnPlay,
            seed = seed,
            centerMapAtOrigin = centerMapAtOrigin,
            playableMarginCells = playableMarginCells,
            baseFlattenRadiusWorld = baseFlattenRadiusWorld,
            spawnPattern = spawnPattern
        };
    }

    /// <summary>Relieve macro automático (sin esculpir a mano).</summary>
    [Serializable]
    public sealed class TerrainShapeConfig
    {
        public bool mountainsEnabled = true;
        [Range(0, 12)] public int mountainMassCount = 2;
        [Tooltip("Altura relativa 0–1 añadida en el pico (el export mundo usa terrainHeightWorld).")]
        public Vector2 mountainHeight01Range = new(0.12f, 0.30f);
        public Vector2Int mountainRadiusCellsRange = new(10, 28);
        public bool hillsEnabled = true;
        [Tooltip("0 = casi liso, 1 = mucho relieve de ruido base.")]
        [Range(0f, 1f)] public float hillDensity = 0.45f;
        [Range(0, 8)] public int basinCount = 1;
        [Range(0.02f, 0.12f)] public float basinDepth01 = 0.05f;
        [Range(0f, 1f)] public float valleyStrength = 0.35f;
        [Range(0f, 1f)] public float plateauChance = 0.15f;
        [Range(0f, 1f)] public float terrainRoughness = 0.5f;
        public bool flattenSpawnZones = true;
        public bool mountainExclusionNearSpawn = true;

        public TerrainShapeConfig Clone() => new TerrainShapeConfig
        {
            mountainsEnabled = mountainsEnabled,
            mountainMassCount = mountainMassCount,
            mountainHeight01Range = mountainHeight01Range,
            mountainRadiusCellsRange = mountainRadiusCellsRange,
            hillsEnabled = hillsEnabled,
            hillDensity = hillDensity,
            basinCount = basinCount,
            basinDepth01 = basinDepth01,
            valleyStrength = valleyStrength,
            plateauChance = plateauChance,
            terrainRoughness = terrainRoughness,
            flattenSpawnZones = flattenSpawnZones,
            mountainExclusionNearSpawn = mountainExclusionNearSpawn
        };
    }

    /// <summary>Hidrología declarativa: el generador traza ríos/lagos solo.</summary>
    [Serializable]
    public sealed class HydrologyConfig
    {
        public bool riversEnabled = true;
        [Range(0, 8)] public int riverCount = 3;
        public Vector2Int riverWidthCellsRange = new(2, 5);
        [Tooltip("Profundidad relativa del cauce (se mapea a parámetros técnicos del MapGen).")]
        [Range(0.01f, 0.12f)] public float riverDepthHint01 = 0.04f;
        public bool lakesEnabled = true;
        [Range(0, 12)] public int lakeCount = 2;
        public Vector2Int lakeSizeCellsRange = new(120, 800);
        [Range(0.25f, 0.75f)] public float waterBaseHeightNormalized = 0.4f;
        [Range(-0.2f, 0.2f)] public float waterCoverageBias = 0f;
        [Range(0.5f, 3f)] public float shorelineBlend = 1.2f;
        public bool avoidSpawnFlooding = true;
        public bool connectRiversToLakesIfPossible = true;
        [Min(1f)] public float wetCorridorWidthCells = 8f;

        public HydrologyConfig Clone() => new HydrologyConfig
        {
            riversEnabled = riversEnabled,
            riverCount = riverCount,
            riverWidthCellsRange = riverWidthCellsRange,
            riverDepthHint01 = riverDepthHint01,
            lakesEnabled = lakesEnabled,
            lakeCount = lakeCount,
            lakeSizeCellsRange = lakeSizeCellsRange,
            waterBaseHeightNormalized = waterBaseHeightNormalized,
            waterCoverageBias = waterCoverageBias,
            shorelineBlend = shorelineBlend,
            avoidSpawnFlooding = avoidSpawnFlooding,
            connectRiversToLakesIfPossible = connectRiversToLakesIfPossible,
            wetCorridorWidthCells = wetCorridorWidthCells
        };
    }

    /// <summary>Umbrales para clasificación semántica post-terreno (alpha).</summary>
    [Serializable]
    public sealed class RegionClassificationConfig
    {
        [Range(0.5f, 0.95f)] public float mountainHeightThreshold01 = 0.78f;
        [Range(15f, 75f)] public float mountainSlopeThresholdDeg = 28f;
        [Range(0.45f, 0.8f)] public float hillHeightThreshold01 = 0.58f;
        [Range(1, 24)] public int nearWaterDistanceCells = 4;
        [Range(1, 16)] public int shorelineDistanceCells = 2;
        [Range(20f, 70f)] public float rockyZoneSlopeThresholdDeg = 22f;
        [Range(0f, 1f)] public float fertileZoneHumidityBonus = 0.25f;
        [Range(1, 12)] public int floodplainNearRiverDistanceCells = 5;

        public RegionClassificationConfig Clone() => new RegionClassificationConfig
        {
            mountainHeightThreshold01 = mountainHeightThreshold01,
            mountainSlopeThresholdDeg = mountainSlopeThresholdDeg,
            hillHeightThreshold01 = hillHeightThreshold01,
            nearWaterDistanceCells = nearWaterDistanceCells,
            shorelineDistanceCells = shorelineDistanceCells,
            rockyZoneSlopeThresholdDeg = rockyZoneSlopeThresholdDeg,
            fertileZoneHumidityBonus = fertileZoneHumidityBonus,
            floodplainNearRiverDistanceCells = floodplainNearRiverDistanceCells
        };
    }

    /// <summary>Economía / densidad de recursos en lenguaje de diseño.</summary>
    [Serializable]
    public sealed class ResourceDistributionConfig
    {
        public AbundanceTier forestDensity = AbundanceTier.Medium;
        public AbundanceTier goldAbundance = AbundanceTier.Medium;
        public AbundanceTier stoneAbundance = AbundanceTier.Medium;
        public AbundanceTier berryAbundance = AbundanceTier.Medium;
        public bool animalsEnabled = true;
        public AbundanceTier animalDensity = AbundanceTier.Medium;
        [Range(0f, 1f)] public float playerResourceFairness = 0.85f;
        public bool perPlayerStartResourcesEnabled = true;
        public bool globalResourceClustersEnabled = true;
        [Range(0f, 1f)] public float clusterDensity = 0.55f;
        [Range(1, 8)] public int minResourceDistanceCells = 2;
        public bool avoidOvercrowding = true;
        [Range(0f, 2f)] public float forestNearWaterBonus = 1.2f;
        [Range(0f, 2f)] public float rockNearMountainBonus = 1.35f;
        [Range(0f, 2f)] public float goldNearMountainBonus = 1.25f;
        [Range(0f, 2f)] public float animalNearWaterBonus = 1.15f;
        [Tooltip("Si true, Fase8 usa sesgo por terreno/distancia al agua (alpha).")]
        public bool useTerrainDrivenPlacement = true;

        public ResourceDistributionConfig Clone() => new ResourceDistributionConfig
        {
            forestDensity = forestDensity,
            goldAbundance = goldAbundance,
            stoneAbundance = stoneAbundance,
            berryAbundance = berryAbundance,
            animalsEnabled = animalsEnabled,
            animalDensity = animalDensity,
            playerResourceFairness = playerResourceFairness,
            perPlayerStartResourcesEnabled = perPlayerStartResourcesEnabled,
            globalResourceClustersEnabled = globalResourceClustersEnabled,
            clusterDensity = clusterDensity,
            minResourceDistanceCells = minResourceDistanceCells,
            avoidOvercrowding = avoidOvercrowding,
            forestNearWaterBonus = forestNearWaterBonus,
            rockNearMountainBonus = rockNearMountainBonus,
            goldNearMountainBonus = goldNearMountainBonus,
            animalNearWaterBonus = animalNearWaterBonus,
            useTerrainDrivenPlacement = useTerrainDrivenPlacement
        };
    }

    /// <summary>Reglas de spawn / equidad (alto nivel). Convive con <see cref="MatchConfig.PlayerSettings"/>.</summary>
    [Serializable]
    public sealed class PlayerSpawnConfig
    {
        public MapSpawnPattern spawnPattern = MapSpawnPattern.Balanced;
        [Min(8f)] public float minSpawnDistanceWorld = 100f;
        public bool avoidMountains = true;
        public bool preferPlains = true;
        public bool avoidDeepWater = true;
        public bool ensureAccessToNearbyResources = true;
        public bool ensureFairStartZones = true;
        [Tooltip("Reservado (equipos / futuro).")]
        public int teamLayoutMode = 0;

        public PlayerSpawnConfig Clone() => new PlayerSpawnConfig
        {
            spawnPattern = spawnPattern,
            minSpawnDistanceWorld = minSpawnDistanceWorld,
            avoidMountains = avoidMountains,
            preferPlains = preferPlains,
            avoidDeepWater = avoidDeepWater,
            ensureAccessToNearbyResources = ensureAccessToNearbyResources,
            ensureFairStartZones = ensureFairStartZones,
            teamLayoutMode = teamLayoutMode
        };
    }

    /// <summary>Enlaces a perfiles visuales (datos abstractos → representación). Alpha: IDs; futuro: ScriptableObjects.</summary>
    [Serializable]
    public sealed class VisualBindingConfig
    {
        public string riverConstructionProfile = "Default";
        public string lakeConstructionProfile = "Default";
        public string mountainDecorationProfile = "Default";
        public string terrainDecorationProfile = "Default";
        public string waterMaterialProfile = "Default";
        public string shorelineProfile = "Default";
        public string forestVisualProfile = "Default";
        public string rockVisualProfile = "Default";
        public string animalSpawnProfile = "Default";

        [Header("Prefabs (alpha: fuente de visuales si están asignados)")]
        [Tooltip("Si no es null, sustituye resources.visuals al compilar en modo alpha.")]
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

        public VisualBindingConfig Clone() => new VisualBindingConfig
        {
            riverConstructionProfile = riverConstructionProfile,
            lakeConstructionProfile = lakeConstructionProfile,
            mountainDecorationProfile = mountainDecorationProfile,
            terrainDecorationProfile = terrainDecorationProfile,
            waterMaterialProfile = waterMaterialProfile,
            shorelineProfile = shorelineProfile,
            forestVisualProfile = forestVisualProfile,
            rockVisualProfile = rockVisualProfile,
            animalSpawnProfile = animalSpawnProfile,
            treePrefab = treePrefab,
            treePrefabVariants = treePrefabVariants,
            berryPrefab = berryPrefab,
            berryPrefabVariants = berryPrefabVariants,
            animalPrefab = animalPrefab,
            animalPrefabVariants = animalPrefabVariants,
            goldPrefab = goldPrefab,
            goldPrefabVariants = goldPrefabVariants,
            stonePrefab = stonePrefab,
            stonePrefabVariants = stonePrefabVariants,
            stoneMaterialOverride = stoneMaterialOverride,
            treeMaterialOverrides = treeMaterialOverrides
        };
    }
}
