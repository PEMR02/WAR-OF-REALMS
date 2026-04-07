using UnityEngine;
using Project.Gameplay.Map.Generator;

namespace Project.Gameplay.Map
{
    /// <summary>
    /// Crea MapGenConfig desde los campos del RTSMapGenerator (extracción del monolito).
    /// RTSMapGenerator usa esto cuando no tiene definitiveMapGenConfig asignado.
    /// </summary>
    public static class MapGenConfigFactory
    {
        public static MapGenConfig CreateFrom(MatchConfig match)
        {
            if (match == null) return null;

            MapGenConfig c = ScriptableObject.CreateInstance<MapGenConfig>();
            c.gridW = Mathf.Max(1, match.map.width);
            c.gridH = Mathf.Max(1, match.map.height);
            c.cellSizeWorld = Mathf.Max(0.01f, match.map.cellSize);
            c.seed = match.map.randomSeedOnPlay ? Random.Range(1, int.MaxValue) : match.map.seed;
            c.maxRetries = 5;
            c.regionCount = 8;
            c.regionNoiseScale = match.geography.noiseScale;
            // Agua: MatchConfigCompiler.ApplyMatchToMapGen pisa con baseHeightNormalized; aquí valor inicial coherente.
            c.waterHeight01 = Mathf.Clamp01(match.water.baseHeightNormalized);
            c.riverCount = Mathf.Clamp(match.water.riverCount, 0, 8);
            c.lakeCount = Mathf.Clamp(match.water.lakeCount, 0, 12);
            c.maxLakeCells = Mathf.Max(100, match.water.maxLakeCells);
            c.cityCount = Mathf.Max(2, match.players.playerCount);
            c.minCityDistanceCells = 40;
            c.cityRadiusCells = 8;
            c.maxCitySlopeDeg = match.players.maxSlopeAtSpawn;
            c.cityWaterBufferCells = 2;
            c.roadWidthCells = 2;
            c.roadFlattenStrength = 0.8f;
            c.ringNear = new Vector2Int((int)match.resources.ringNear.x, (int)match.resources.ringNear.y);
            c.ringMid = new Vector2Int((int)match.resources.ringMid.x, (int)match.resources.ringMid.y);
            c.ringFar = new Vector2Int((int)match.resources.ringFar.x, (int)match.resources.ringFar.y);
            c.minWoodPerCity = match.resources.minWoodTrees;
            c.minStonePerCity = match.resources.minStoneNodes;
            c.minGoldPerCity = match.resources.minGoldNodes;
            c.minFoodPerCity = match.resources.minFoodValue;
            c.maxResourceRetries = match.resources.maxResourceRetries;
            c.terrainHeightWorld = match.geography.heightMultiplier;
            c.heightmapResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(match.map.width, match.map.height)) + 1, 33, 4097);
            c.paintTerrainByHeight = match.climate.paintTerrainByHeight;
            c.grassLayer = match.climate.grassLayer;
            c.dirtLayer = match.climate.dirtLayer;
            c.rockLayer = match.climate.rockLayer;
            int totalPct = match.climate.grassPercent + match.climate.dirtPercent + match.climate.rockPercent;
            if (totalPct < 1) totalPct = 100;
            c.grassPercent01 = Mathf.Clamp01((float)match.climate.grassPercent / totalPct);
            c.dirtPercent01 = Mathf.Clamp01((float)match.climate.dirtPercent / totalPct);
            c.rockPercent01 = Mathf.Clamp01((float)match.climate.rockPercent / totalPct);
            c.grassMaxHeight01 = c.grassPercent01;
            c.dirtMaxHeight01 = c.grassPercent01 + c.dirtPercent01;
            c.textureBlendWidth = match.climate.textureBlendWidth;
            c.terrainBlendSharpness = match.climate.terrainBlendSharpness;
            c.terrainMacroNoiseScale = match.climate.terrainMacroNoiseScale;
            c.terrainMacroNoiseStrength = match.climate.terrainMacroNoiseStrength;
            c.grassDryLayer = match.climate.grassDryLayer;
            c.grassDryBlendStrength = match.climate.grassDryBlendStrength;
            c.grassDryNoiseScale = match.climate.grassDryNoiseScale;
            c.wetDirtLayer = match.climate.wetDirtLayer;
            c.terrainMoistureRadius = match.climate.terrainMoistureRadius;
            c.terrainMoistureStrength = match.climate.terrainMoistureStrength;
            c.terrainMoistureNoiseScale = match.climate.terrainMoistureNoiseScale;
            c.terrainMoistureNoiseStrength = match.climate.terrainMoistureNoiseStrength;
            c.sandEdgeNoiseScale = match.climate.sandEdgeNoiseScale;
            c.sandEdgeNoiseStrength = match.climate.sandEdgeNoiseStrength;
            c.debugTerrainMoisture = match.climate.debugTerrainMoisture;
            c.debugTerrainMacro = match.climate.debugTerrainMacro;
            c.debugTerrainGrassDry = match.climate.debugTerrainGrassDry;
            c.sandLayer = match.climate.sandLayer;
            c.sandShoreCells = match.water.sandShoreCells;
            c.waterChunkSize = match.water.chunkSize;
            c.waterSurfaceOffset = match.water.surfaceOffset;
            c.waterMaterial = match.water.material;
            c.waterAlpha = match.water.alpha;
            c.waterLayer = match.water.waterLayer >= 0 ? match.water.waterLayer : -1;
            c.waterRoundedEdges = true;
            c.waterEdgeSubdiv = 4;
            c.waterEdgeBlurIterations = 3;
            c.waterEdgeBlurRadius = 2;
            c.waterIsoLevel = 0.5f;
            c.waterMaskPostProcess = true;
            c.waterMaskSmoothIterations = 2;
            c.waterMaskSmoothThreshold = 5;
            c.waterMsMaxCornerSamples = 250000;

            c.showTerrainSkirt = true;
            c.skirtDepth = 30f;
            c.skirtEdgeSamples = 128;
            c.skirtMaterial = UnityEngine.Resources.Load<Material>(TerrainSkirtBuilder.SkirtSoilMaterialResourceName);

            // Río visual / ribbon: CreateFrom no clona un MapGenConfig.asset; alinear con defaults del SO.
            c.riverVisualUseContinuousMesh = true;
            c.riverVisualUseContinuousField = true;
            c.riverVisualMeshHalfWidth = Mathf.Max(1.2f, match.map.cellSize * 0.52f);
            c.riverVisualSampleSpacing = Mathf.Clamp(match.map.cellSize * 0.16f, 0.25f, 0.55f);
            c.riverVisualBankInset = 0f;
            c.debugRiverRibbonGeometry = false;
            c.riverWidthRadiusCells = Mathf.Clamp(match.water.riverWidthRadiusCells, 0, 6);
            c.riverWidthNoiseAmplitudeCells = Mathf.Clamp(match.water.riverWidthNoiseAmplitudeCells, 0, 3);
            c.lakeRiverMouthBlendCells = Mathf.Clamp(match.water.lakeRiverMouthBlendCells, 0, 8);
            c.riverRibbonWidthVariation = 0.34f;
            c.riverRibbonWidthNoiseFreq = 0.065f;
            c.riverRibbonHalfWidthMinMul = 0.64f;
            c.riverRibbonHalfWidthMaxMul = 1.45f;
            c.riverRibbonPerlinWidthBlend = 1f;
            c.riverRibbonPerlinWidthFreq = Mathf.Clamp(0.09f / Mathf.Max(0.5f, match.map.cellSize), 0.02f, 0.2f);
            c.riverRibbonLateralJitterWorld = Mathf.Clamp(match.map.cellSize * 0.14f, 0.2f, 0.55f);
            c.riverRibbonJitterNoiseScale = 0.62f;
            c.riverTerrainCarveDepthWorld = Mathf.Clamp(match.map.cellSize * 0.38f, 0.35f, 1.35f);
            c.riverTerrainCarveFalloffCells = 8;
            c.riverTerrainCarveCenterCurve = 1.35f;
            c.riverTerrainCarveFordMul = 0.32f;
            c.sandShoreFalloffPower = 2.15f;
            c.sandShoreExtraDistanceNoise = 0.55f;
            c.sandSoilContrastNearShore = 1.38f;
            c.sandShoreAlphamapSmoothCap = 1;
            c.debugRiverVisualStats = false;
            c.riverRibbonVerticalLiftWorld = Mathf.Clamp(match.map.cellSize * 0.126f, 0.28f, 0.65f);
            c.riverBedDepthBelowWater01 = 0.022f;
            c.lakeOrganicIrregularity = 0.74f;
            c.lakeExtraSeedSpreadCells = 4;
            c.lakeShoreMsNoiseAmplitude = 0.095f;
            c.lakeShoreMsNoiseScale = Mathf.Clamp(match.map.cellSize * 0.042f, 0.07f, 0.16f);
            c.waterEdgeBlurIterations = 4;

            return c;
        }
    }
}
