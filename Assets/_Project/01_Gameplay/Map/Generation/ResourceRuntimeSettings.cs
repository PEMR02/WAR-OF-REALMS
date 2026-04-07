using System;
using UnityEngine;

namespace Project.Gameplay.Map.Generation
{
    /// <summary>
    /// Snapshot compilado de recursos: conteos, anillos, prefabs y opciones de colocación.
    /// <see cref="MapResourcePlacer"/> debe leer esto inyectado, no depender de campos duplicados en escena.
    /// </summary>
    [Serializable]
    public sealed class ResourceRuntimeSettings
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
        public float clusterDensity = 0.6f;
        public int clusterMinSize = 15;
        public int clusterMaxSize = 40;
        public int minWoodTrees = 15;
        public int minGoldNodes = 6;
        public int minStoneNodes = 4;
        public int minFoodValue = 8;
        public int maxResourceRetries = 5;

        public float globalTreesClusterFraction = -1f;
        public bool preferGlobalTreesOnGrassAlphamap;
        public float globalStoneGoldClusterFraction = 0.82f;
        public Vector2Int globalMineralClusterSize = new(2, 6);
        public float globalMineralClusterRadiusCells = 3.2f;

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
        public string resourceLayerName = "Resource";
        public bool randomRotationPerResource = true;
        public float cellPlacementRandomOffset = 0.8f;
        public bool forceResourceShadowCasting;

        /// <summary>DEPRECATED: rellena desde campos legacy del MonoBehaviour de escena (solo compatibilidad).</summary>
        public static ResourceRuntimeSettings FromLegacySceneGenerator(RTSMapGenerator g)
        {
            if (g == null) return new ResourceRuntimeSettings();
            return new ResourceRuntimeSettings
            {
                ringNear = g.ringNear,
                ringMid = g.ringMid,
                ringFar = g.ringFar,
                nearTrees = g.nearTrees,
                midTrees = g.midTrees,
                berries = g.berries,
                animals = g.animals,
                goldSafe = g.goldSafe,
                stoneSafe = g.stoneSafe,
                goldFar = g.goldFar,
                globalTrees = g.globalTrees,
                globalStone = g.globalStone,
                globalGold = g.globalGold,
                globalAnimals = g.globalAnimals,
                globalExcludeRadius = g.globalExcludeRadius,
                forestClustering = g.forestClustering,
                clusterDensity = g.clusterDensity,
                clusterMinSize = g.clusterMinSize,
                clusterMaxSize = g.clusterMaxSize,
                minWoodTrees = g.minWoodTrees,
                minGoldNodes = g.minGoldNodes,
                minStoneNodes = g.minStoneNodes,
                minFoodValue = g.minFoodValue,
                maxResourceRetries = g.maxResourceRetries,
                globalTreesClusterFraction = g.globalTreesClusterFraction,
                preferGlobalTreesOnGrassAlphamap = g.preferGlobalTreesOnGrassAlphamap,
                globalStoneGoldClusterFraction = g.globalStoneGoldClusterFraction,
                globalMineralClusterSize = g.globalMineralClusterSize,
                globalMineralClusterRadiusCells = g.globalMineralClusterRadiusCells,
                treePrefab = g.treePrefab,
                treePrefabVariants = g.treePrefabVariants,
                berryPrefab = g.berryPrefab,
                berryPrefabVariants = g.berryPrefabVariants,
                animalPrefab = g.animalPrefab,
                animalPrefabVariants = g.animalPrefabVariants,
                goldPrefab = g.goldPrefab,
                goldPrefabVariants = g.goldPrefabVariants,
                stonePrefab = g.stonePrefab,
                stonePrefabVariants = g.stonePrefabVariants,
                stoneMaterialOverride = g.stoneMaterialOverride,
                treeMaterialOverrides = g.treeMaterialOverrides,
                treePlacementRotation = g.treePlacementRotation,
                resourceLayerName = string.IsNullOrEmpty(g.resourceLayerName) ? "Resource" : g.resourceLayerName,
                randomRotationPerResource = g.randomRotationPerResource,
                cellPlacementRandomOffset = g.cellPlacementRandomOffset,
                forceResourceShadowCasting = g.forceResourceShadowCasting,
            };
        }
    }
}
