using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline
{
    /// <summary>
    /// Contrato UWP: anchos de río, hidrología y lagos desde PMGUnifiedWorldPipelineConfig.
    /// Una sola fuente de verdad visual; sin ratios dendríticos ni caps de lobby.
    /// </summary>
    public static class UwpHydrologyContract
    {
        public struct ResolvedHydrology
        {
            public float mainFullWidthCells;
            public float tributaryFullWidthCells;
            public float tributaryRatioToMain;
            public int mainRiverCount;
            public int tributaryCount;
            public int riverCount;
            public int lakeCount;
            public int maxLakeCells;
            public float cellSizeWorld;
            public int gridCells;
        }

        public static ResolvedHydrology Resolve(PMGUnifiedWorldPipelineConfig pipeline)
        {
            float cellSize = pipeline != null && pipeline.uwpCellSizeWorld > 0.01f
                ? pipeline.uwpCellSizeWorld
                : 2.5f;
            int grid = pipeline != null && pipeline.uwpGridCells > 0
                ? pipeline.uwpGridCells
                : 358;

            float mainW = pipeline != null && pipeline.uwpMainRiverFullWidthCells > 0.01f
                ? pipeline.uwpMainRiverFullWidthCells
                : 3.75f;
            float tribW = pipeline != null && pipeline.uwpTributaryRiverFullWidthCells > 0.01f
                ? pipeline.uwpTributaryRiverFullWidthCells
                : 1.55f;
            tribW = Mathf.Min(tribW, mainW * 0.95f);

            int mainRivers = pipeline != null ? Mathf.Clamp(pipeline.uwpMainRiverCount, 1, 1) : 1;
            int tributaries = pipeline != null ? Mathf.Clamp(pipeline.uwpTributaryCount, 0, 6) : 3;
            int lakes = pipeline != null ? Mathf.Clamp(pipeline.uwpLakeCount, 0, 12) : 2;
            int maxLake = pipeline != null ? Mathf.Clamp(pipeline.uwpMaxLakeCells, 50, 12000) : 1400;

            return new ResolvedHydrology
            {
                mainFullWidthCells = mainW,
                tributaryFullWidthCells = tribW,
                tributaryRatioToMain = mainW > 0.01f ? tribW / mainW : 0.42f,
                mainRiverCount = mainRivers,
                tributaryCount = tributaries,
                riverCount = Mathf.Clamp(mainRivers + tributaries, 1, 8),
                lakeCount = lakes,
                maxLakeCells = maxLake,
                cellSizeWorld = cellSize,
                gridCells = grid,
            };
        }

        public static void ApplyVisualWidths(MapGenConfig cfg, PMGUnifiedWorldPipelineConfig pipeline)
        {
            if (cfg == null) return;

            ResolvedHydrology h = Resolve(pipeline);
            cfg.uwpOwnedVisualPolicy = true;
            cfg.riverVisualRibbonFullWidthCellsMain = h.mainFullWidthCells;
            cfg.riverVisualRibbonFullWidthCellsTributary = h.tributaryFullWidthCells;

            cfg.riverSurfaceVisualNormalWidthMul = 1f;
            cfg.riverSurfaceVisualMaxWidthMul = 1.12f;
            cfg.riverSurfaceTributaryVisualWidthMul = 1f;
            cfg.riverSurfaceTributaryMinWidthMul = 1f;
            cfg.riverSurfaceTributaryMaxWidthMul = 1.1f;

            cfg.riverSecondaryWidthRatioToMain = h.tributaryRatioToMain;
            cfg.riverMediumWidthRatioToMain = h.tributaryRatioToMain * 0.85f;
            cfg.riverHeadwaterWidthRatioToMain = h.tributaryRatioToMain * 0.65f;

            float carveDepth = pipeline != null && pipeline.uwpMainRiverCarveDepthWorld > 0.001f
                ? pipeline.uwpMainRiverCarveDepthWorld
                : 0.11f;

            cfg.riverWidthRadiusCells = Mathf.Clamp(Mathf.RoundToInt(h.mainFullWidthCells * 0.25f), 1, 6);
            cfg.riverVisualHalfWidthCells = Mathf.Max(cfg.riverVisualHalfWidthCells, h.mainFullWidthCells * 0.22f);
            cfg.riverVisualBankInset = 0.06f;
            cfg.riverVisualTerrainCarveEnabled = true;
            cfg.riverTerrainCarveDepthWorld = carveDepth;
            ApplyMainVsTributaryTerrainProfile(cfg, h, carveDepth);

            cfg.riverSurfaceTributaryWidthFixEnabled = true;
            cfg.riverSurfaceMeshExtraYOffsetWorld = Mathf.Max(cfg.riverSurfaceMeshExtraYOffsetWorld, 0.15f);
            cfg.riverRibbonAntiZFightYOffsetWorld = Mathf.Max(cfg.riverRibbonAntiZFightYOffsetWorld, 0.045f);

            if (pipeline != null && pipeline.uwpMainRiverSurfaceWorldY > 0.01f)
            {
                float terrainY = cfg.terrainHeightWorld > 0f ? cfg.terrainHeightWorld : 38f;
                float baseWaterY = cfg.waterHeight01 * terrainY + Mathf.Max(cfg.waterSurfaceOffset, 0.02f);
                float widthT = Mathf.InverseLerp(2f, 12f, h.mainFullWidthCells);
                float targetY = Mathf.Lerp(pipeline.uwpMainRiverSurfaceWorldY * 0.12f, pipeline.uwpMainRiverSurfaceWorldY, widthT);
                float requiredLift = Mathf.Max(0f, targetY - baseWaterY + carveDepth * widthT * 0.6f);
                cfg.riverRibbonVerticalLiftWorld = Mathf.Max(cfg.riverRibbonVerticalLiftWorld, requiredLift);
                cfg.riverSurfaceMeshExtraYOffsetWorld = Mathf.Max(cfg.riverSurfaceMeshExtraYOffsetWorld, requiredLift * 0.22f);
            }
            else if (pipeline != null)
            {
                cfg.riverRibbonVerticalLiftWorld = 0f;
            }
        }

        /// <summary>
        /// Ajuste de anchos/carve tras <see cref="ApplyVisualWidths"/> (p. ej. override desde RTS Play).
        /// </summary>
        public static void RetuneVisualWidths(
            MapGenConfig cfg,
            float mainFullWidthCells,
            float tributaryFullWidthCells,
            float mainCarveDepthWorld = 0.11f)
        {
            if (cfg == null)
                return;

            float mainW = Mathf.Max(0.5f, mainFullWidthCells);
            float tribW = Mathf.Clamp(tributaryFullWidthCells, 0.5f, mainW * 0.95f);
            var h = new ResolvedHydrology
            {
                mainFullWidthCells = mainW,
                tributaryFullWidthCells = tribW,
                tributaryRatioToMain = mainW > 0.01f ? tribW / mainW : 0.42f,
            };

            cfg.riverVisualRibbonFullWidthCellsMain = mainW;
            cfg.riverVisualRibbonFullWidthCellsTributary = tribW;
            cfg.riverSecondaryWidthRatioToMain = h.tributaryRatioToMain;
            cfg.riverMediumWidthRatioToMain = h.tributaryRatioToMain * 0.85f;
            cfg.riverHeadwaterWidthRatioToMain = h.tributaryRatioToMain * 0.65f;
            cfg.riverWidthRadiusCells = Mathf.Clamp(Mathf.RoundToInt(mainW * 0.25f), 1, 6);
            cfg.riverVisualHalfWidthCells = Mathf.Max(cfg.riverVisualHalfWidthCells, mainW * 0.22f);
            ApplyMainVsTributaryTerrainProfile(cfg, h, mainCarveDepthWorld);
        }

        /// <summary>
        /// Fiabilidad tributarios/bocas para RTS Play sin cambiar grid ni riverCount.
        /// </summary>
        public static void ApplyRtsPlayPlacementTuning(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            float cellScale = cfg.cellSizeWorld > 0.01f ? cfg.cellSizeWorld / 2.5f : 1f;
            cfg.allowFallbackCrossing = true;
            cfg.riverAvoidCrossingOtherRivers = false;
            cfg.riverPlacementMaxAttemptsPerRiver = Mathf.Clamp(cfg.riverPlacementMaxAttemptsPerRiver, 4, 8);
            cfg.riverCorridorRejectEarlyAbort = Mathf.Min(cfg.riverCorridorRejectEarlyAbort, 6);
            cfg.maxTotalRiverBuildAttempts = Mathf.Clamp(cfg.maxTotalRiverBuildAttempts, 24, 48);
            cfg.riverTributaryProceduralMaxSourceDistCells = Mathf.Clamp(
                Mathf.Max(cfg.riverTributaryProceduralMaxSourceDistCells, Mathf.RoundToInt(56f * cellScale)),
                24,
                72);
        }

        /// <summary>
        /// Troncal: valle al ancho del mesh (no foso extra) y lecho más profundo.
        /// Tributarios: carve más suave y radio menor.
        /// </summary>
        public static void ApplyMainVsTributaryTerrainProfile(MapGenConfig cfg, ResolvedHydrology h, float mainCarveDepthWorld)
        {
            if (cfg == null) return;

            int mainFalloff = Mathf.Clamp(Mathf.RoundToInt(h.mainFullWidthCells * 0.42f), 4, 8);
            cfg.riverTerrainCarveFalloffCells = mainFalloff;
            cfg.riverVisualTerrainCarveExtraCells = 1;
            cfg.riverVisualTerrainBankFalloffCells = Mathf.Clamp(mainFalloff / 2, 3, 5);
            cfg.riverVisualTerrainBankSoftness = 0.72f;
            cfg.riverVisualTerrainCenterDepthMul = 1.32f;
            cfg.riverTributaryTerrainCarveRadiusMul = Mathf.Clamp(h.tributaryRatioToMain * 0.92f, 0.78f, 0.92f);

            cfg.riverBedDepthBelowWater01 = Mathf.Clamp(Mathf.Max(cfg.riverBedDepthBelowWater01, 0.022f), 0.020f, 0.030f);
            cfg.tributaryBedDepthBelowWater01 = Mathf.Clamp(Mathf.Min(cfg.tributaryBedDepthBelowWater01, 0.013f), 0.010f, 0.016f);
            cfg.riverTerrainCarveDepthWorld = Mathf.Max(mainCarveDepthWorld, 0.04f);
        }

        public static void ApplyHydrologyCounts(MapGenConfig cfg, PMGUnifiedWorldPipelineConfig pipeline)
        {
            if (cfg == null) return;

            ResolvedHydrology h = Resolve(pipeline);
            cfg.cellSizeWorld = h.cellSizeWorld;
            cfg.gridW = h.gridCells;
            cfg.gridH = h.gridCells;

            bool centerAtOrigin = pipeline == null || pipeline.centerMapAtOrigin;
            cfg.origin = centerAtOrigin
                ? new Vector3(-h.gridCells * h.cellSizeWorld * 0.5f, 0f, -h.gridCells * h.cellSizeWorld * 0.5f)
                : Vector3.zero;

            int hmRes = Mathf.Clamp(Mathf.ClosestPowerOfTwo(h.gridCells) + 1, 33, 2049);
            if ((hmRes & 1) == 0) hmRes++;
            cfg.heightmapResolution = hmRes;

            cfg.riverCount = h.riverCount;
            cfg.lakeCount = h.lakeCount;
            cfg.maxLakeCells = h.maxLakeCells;
            cfg.ignoreLobbyHydrologyCaps = true;

            if (pipeline != null && pipeline.uwpCityCount > 0)
                cfg.cityCount = Mathf.Clamp(pipeline.uwpCityCount, 1, 8);

            cfg.allowFallbackCrossing = true;
            cfg.riverAvoidCrossingOtherRivers = false;
            cfg.riverPlacementMaxAttemptsPerRiver = Mathf.Max(cfg.riverPlacementMaxAttemptsPerRiver, 80);
            cfg.riverCorridorRejectEarlyAbort = Mathf.Min(cfg.riverCorridorRejectEarlyAbort, 8);
            cfg.maxTotalRiverBuildAttempts = Mathf.Max(cfg.maxTotalRiverBuildAttempts, 720);
            cfg.riverTributaryProceduralMaxSourceDistCells =
                Mathf.Max(cfg.riverTributaryProceduralMaxSourceDistCells, Mathf.RoundToInt(72f * (h.cellSizeWorld / 2.5f)));
        }
    }
}
