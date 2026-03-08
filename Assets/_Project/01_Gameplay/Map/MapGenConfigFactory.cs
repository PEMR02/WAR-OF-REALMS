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
        public static MapGenConfig CreateFrom(RTSMapGenerator gen)
        {
            if (gen == null) return null;

            if (gen.gridConfig == null)
                Debug.LogWarning("⚠️ RTSMapGenerator: gridConfig NO está asignado. Asigna 'GridConfig.asset' en el Inspector. Usando fallback: cellSize=2.5");

            MapGenConfig c = ScriptableObject.CreateInstance<MapGenConfig>();
            float cellSize = gen.gridConfig != null ? gen.gridConfig.gridSize : 2.5f;
            c.gridW = gen.width;
            c.gridH = gen.height;
            c.cellSizeWorld = cellSize;
            c.origin = gen.centerAtOrigin ? new Vector3(-gen.width * cellSize * 0.5f, 0f, -gen.height * cellSize * 0.5f) : gen.transform.position;
            c.seed = gen.randomSeedOnPlay ? Random.Range(1, int.MaxValue) : gen.seed;
            c.maxRetries = 5;
            c.regionCount = 8;
            c.regionNoiseScale = gen.noiseScale;
            c.waterHeight01 = 0.4f;
            c.riverCount = Mathf.Clamp(gen.riverCount, 0, 8);
            c.lakeCount = Mathf.Clamp(gen.lakeCount, 0, 6);
            c.maxLakeCells = Mathf.Max(100, gen.maxLakeCells);
            c.cityCount = Mathf.Max(2, gen.playerCount);
            c.minCityDistanceCells = 40;
            c.cityRadiusCells = 8;
            c.maxCitySlopeDeg = gen.maxSlopeAtSpawn;
            c.cityWaterBufferCells = 2;
            c.roadWidthCells = 2;
            c.roadFlattenStrength = 0.8f;
            c.ringNear = new Vector2Int((int)gen.ringNear.x, (int)gen.ringNear.y);
            c.ringMid = new Vector2Int((int)gen.ringMid.x, (int)gen.ringMid.y);
            c.ringFar = new Vector2Int((int)gen.ringFar.x, (int)gen.ringFar.y);
            c.minWoodPerCity = gen.minWoodTrees;
            c.minStonePerCity = gen.minStoneNodes;
            c.minGoldPerCity = gen.minGoldNodes;
            c.minFoodPerCity = gen.minFoodValue;
            c.maxResourceRetries = gen.maxResourceRetries;
            c.terrainHeightWorld = gen.heightMultiplier;
            c.heightmapResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(gen.width, gen.height)) + 1, 33, 4097);
            c.paintTerrainByHeight = gen.paintTerrainByHeight;
            c.grassLayer = gen.grassLayer;
            c.dirtLayer = gen.dirtLayer;
            c.rockLayer = gen.rockLayer;
            int totalPct = gen.grassPercent + gen.dirtPercent + gen.rockPercent;
            if (totalPct < 1) totalPct = 100;
            c.grassPercent01 = Mathf.Clamp01((float)gen.grassPercent / totalPct);
            c.dirtPercent01 = Mathf.Clamp01((float)gen.dirtPercent / totalPct);
            c.rockPercent01 = Mathf.Clamp01((float)gen.rockPercent / totalPct);
            c.grassMaxHeight01 = c.grassPercent01;
            c.dirtMaxHeight01 = c.grassPercent01 + c.dirtPercent01;
            c.textureBlendWidth = gen.textureBlendWidth;
            c.sandLayer = gen.sandLayer;
            c.sandShoreCells = gen.sandShoreCells;
            c.waterChunkSize = gen.waterChunkSize;
            c.waterSurfaceOffset = gen.waterSurfaceOffset;
            c.waterMaterial = gen.waterMaterial;
            c.waterAlpha = gen.waterAlpha;
            c.waterLayer = gen.waterLayerOverride >= 0 ? gen.waterLayerOverride : -1;
            c.waterRoundedEdges = true;
            c.waterEdgeSubdiv = 4;
            c.waterEdgeBlurIterations = 3;
            c.waterEdgeBlurRadius = 2;
            c.waterIsoLevel = 0.5f;
            c.waterMaskPostProcess = true;
            c.waterMaskSmoothIterations = 2;
            c.waterMaskSmoothThreshold = 5;
            c.waterMsMaxCornerSamples = 250000;

            return c;
        }
    }
}
