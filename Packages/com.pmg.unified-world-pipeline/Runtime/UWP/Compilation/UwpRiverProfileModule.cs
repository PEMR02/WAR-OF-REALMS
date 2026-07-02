using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline
{
    /// <summary>Módulo UWP: ríos, tributarios, centerlines y export visual.</summary>
    public static class UwpRiverProfileModule
    {
        public static void Apply(MapGenConfig cfg, PMGUnifiedWorldPipelineConfig pipeline)
        {
            if (cfg == null) return;
            ApplySoftCarves(cfg);
            ApplyReliabilityFix(cfg);
            ApplyCenterlineQuality(cfg);
            ApplyTributaryRoutingQuality(cfg);
            ApplyTributaryPlacementReliability(cfg);
        }

        public static void ApplyExportFix(MapGenConfig cfg, PMGUnifiedWorldPipelineConfig pipeline)
        {
            if (cfg == null) return;

            cfg.riverSurfaceBorderEndpointWidthMul = 1f;
            cfg.riverSurfaceBorderGhostCells = 0f;
            cfg.riverSurfaceSkipCapAtMapBorder = true;
            cfg.riverEndReachTerrainFixLengthCells = Mathf.Min(cfg.riverEndReachTerrainFixLengthCells, 12);
            cfg.riverEndReachTerrainFixRadiusMul = Mathf.Min(cfg.riverEndReachTerrainFixRadiusMul, 1.05f);
            cfg.riverOutletTerrainFixLengthCells = Mathf.Min(cfg.riverOutletTerrainFixLengthCells, 8);
            cfg.riverOutletTerrainFixRadiusMul = Mathf.Min(cfg.riverOutletTerrainFixRadiusMul, 1.02f);
            cfg.riverEndReachTerrainFixEnabled = false;
            cfg.riverOutletTerrainFixEnabled = false;

            if (pipeline != null && pipeline.useMouthFusionWaterPipeline)
            {
                cfg.waterVisualPipeline = WaterVisualPipelineMode.SplitLakeMsRiverMouthFusion;
                cfg.riverConfluenceTributaryEndWidthMul = Mathf.Min(cfg.riverConfluenceTributaryEndWidthMul, 0.42f);
                cfg.riverConfluenceVisualBlendLengthCells = Mathf.Max(cfg.riverConfluenceVisualBlendLengthCells, 12);
                cfg.riverConfluenceHideLastSegmentUnderMain = true;
                cfg.riverSurfaceSkipTributaryConfluenceCap = true;
            }
            else
            {
                cfg.waterVisualPipeline = WaterVisualPipelineMode.SplitLakeMsRiverWebFusion;
            }
        }

        public static void ApplyBankTerrainFix(MapGenConfig cfg)
        {
            if (cfg == null) return;
            ApplySoftCarves(cfg);
            cfg.shoreSmoothRadiusCells = 18;
            cfg.shoreSmoothStrength = 0.48f;
            cfg.sandShoreCells = Mathf.Clamp(cfg.sandShoreCells, 3, 4);
            cfg.unifiedWaterTerrainBankLipWorld = Mathf.Max(cfg.unifiedWaterTerrainBankLipWorld, 0.028f);
            cfg.unifiedWaterTerrainBandCells = Mathf.Max(cfg.unifiedWaterTerrainBandCells, 1.85f);
            cfg.unifiedWaterShoreTerrainOffsetWorld = Mathf.Max(cfg.unifiedWaterShoreTerrainOffsetWorld, 0.022f);
            cfg.unifiedWaterTerrainEdgeSubmergeWorld = Mathf.Max(cfg.unifiedWaterTerrainEdgeSubmergeWorld, 0.052f);
            UwpTerrainProfileModule.ApplyShallowerWaterBedCaps(cfg);
        }

        public static void ApplySoftCarves(MapGenConfig cfg)
        {
            if (cfg == null) return;
            cfg.riverVisualTerrainCarveEnabled = true;
            if (cfg.riverTerrainCarveDepthWorld <= 0.001f)
                cfg.riverTerrainCarveDepthWorld = 0.08f;
            if (!cfg.uwpOwnedVisualPolicy)
            {
                cfg.riverTerrainCarveFalloffCells = Mathf.Clamp(Mathf.Max(cfg.riverTerrainCarveFalloffCells, 10), 8, 14);
                cfg.riverVisualTerrainCarveExtraCells = Mathf.Max(cfg.riverVisualTerrainCarveExtraCells, 2);
            }
            cfg.riverVisualTerrainBankFalloffCells = Mathf.Max(cfg.riverVisualTerrainBankFalloffCells, 5);
            cfg.riverVisualTerrainBankSoftness = Mathf.Max(cfg.riverVisualTerrainBankSoftness, 0.72f);
            cfg.riverVisualTerrainCenterDepthMul = Mathf.Max(cfg.riverVisualTerrainCenterDepthMul, 1.18f);
            cfg.riverEndReachTerrainFixEnabled = false;
            cfg.riverOutletTerrainFixEnabled = false;
        }

        public static void ApplyReliabilityFix(MapGenConfig cfg)
        {
            if (cfg == null) return;
            float cellScale = cfg.cellSizeWorld > 0.01f ? cfg.cellSizeWorld / 2.5f : 1f;
            int corridorCells = Mathf.Clamp(Mathf.RoundToInt(6f * cellScale), 6, 12);

            cfg.riverVisualMinSurfacePieceLengthCells = Mathf.Min(cfg.riverVisualMinSurfacePieceLengthCells, 5);
            cfg.riverVisualMinSurfacePieceAreaCells = Mathf.Min(cfg.riverVisualMinSurfacePieceAreaCells, 3);
            cfg.riverVisualMinDetachedPatchCells = Mathf.Min(cfg.riverVisualMinDetachedPatchCells, 2);
            cfg.riverVisualMainRiverCorridorCells = Mathf.Max(cfg.riverVisualMainRiverCorridorCells, corridorCells);
            cfg.riverVisualMainCorridorKeepDistanceCells =
                Mathf.Max(cfg.riverVisualMainCorridorKeepDistanceCells, corridorCells);
            cfg.riverConfluenceMergeRadiusCells = Mathf.Max(cfg.riverConfluenceMergeRadiusCells, corridorCells);
            cfg.riverConfluenceTributaryEndWidthMul = Mathf.Clamp(cfg.riverConfluenceTributaryEndWidthMul, 0.48f, 0.58f);
            cfg.riverSurfaceTributaryConfluenceApproachCells = Mathf.Clamp(
                cfg.riverSurfaceTributaryConfluenceApproachCells, 8, 14);
            cfg.maxTotalRiverBuildAttempts = Mathf.Max(cfg.maxTotalRiverBuildAttempts, 720);
            cfg.riverDendriticNetworkEnabled = true;
            cfg.lakeRiverMouthBlendCells = Mathf.Max(cfg.lakeRiverMouthBlendCells, 7);
            cfg.riverConfluenceVisualBlendLengthCells = Mathf.Max(cfg.riverConfluenceVisualBlendLengthCells, 11);
            cfg.riverLakeEmissaryLakeFadeCells = Mathf.Max(cfg.riverLakeEmissaryLakeFadeCells, 16f);
            cfg.riverLakeEmissaryRiverFadeCells = Mathf.Max(cfg.riverLakeEmissaryRiverFadeCells, 10f);
        }

        public static void ApplyCenterlineQuality(MapGenConfig cfg)
        {
            if (cfg == null) return;
            cfg.riverSurfaceUseSplineVisualCenterline = true;
            cfg.riverSurfaceChaikinPasses = 1;
            cfg.riverSurfaceSharpBendAngleDeg = 95f;
            cfg.riverSurfaceSplineMaxAngleStepDeg = 20f;
            cfg.riverSurfaceSplineMaxDeviationCells = 1.55f;
            cfg.riverSurfaceSplineTension = 0.38f;
            cfg.riverSurfaceSplineSampleSpacingCells = 0.48f;
            cfg.riverSurfaceVisualSpacingCells = 0.82f;
            cfg.riverSurfaceSampleSpacingCells = 0.85f;
            cfg.riverSurfaceMaxVisualPointRatio = 1.28f;
        }

        static void ApplyTributaryRoutingQuality(MapGenConfig cfg)
        {
            if (cfg == null) return;

            float cellScale = cfg.cellSizeWorld > 0.01f ? cfg.cellSizeWorld / 2.5f : 1f;
            // Troncal ~150–220 celdas: caben 3 bocas con separación moderada (anti-paralelo sigue activo).
            int minSpacing = Mathf.Clamp(Mathf.RoundToInt(20f * cellScale), 16, 28);
            int minEndpointDist = Mathf.Clamp(Mathf.RoundToInt(18f * cellScale), 14, 28);

            cfg.riverTributaryMaxParallelRunCells = Mathf.Min(cfg.riverTributaryMaxParallelRunCells, 4);
            cfg.riverTributaryApproachParallelExtraCells = Mathf.Min(
                Mathf.Max(cfg.riverTributaryApproachParallelExtraCells, 2), 3);

            // Tributarios largos desde colinas (procedural).
            cfg.riverTributaryProceduralMinCells = Mathf.Clamp(
                Mathf.Max(cfg.riverTributaryProceduralMinCells, Mathf.RoundToInt(16f * cellScale)), 14, 22);
            cfg.riverTributaryRecoveryMinLengthCells = Mathf.Clamp(
                Mathf.Max(cfg.riverTributaryRecoveryMinLengthCells, Mathf.RoundToInt(18f * cellScale)), 16, 26);

            // Emisarios lago→río: mínimo corto (motor clamp ≥14 en shortStream).
            cfg.riverTributaryShortStreamMinCells = Mathf.Clamp(
                Mathf.Min(cfg.riverTributaryShortStreamMinCells, 14), 10, 16);
            cfg.riverTributaryShortStreamVisualMinCells = Mathf.Clamp(
                Mathf.Min(cfg.riverTributaryShortStreamVisualMinCells, 16), 12, 18);

            cfg.riverConfluenceAcceptLooseAngle = true;
            cfg.riverConfluenceMinJoinAngleDeg = Mathf.Max(cfg.riverConfluenceMinJoinAngleDeg, 18f);
            cfg.riverConfluenceMaxJoinAngleDeg = Mathf.Min(cfg.riverConfluenceMaxJoinAngleDeg, 82f);
            cfg.riverTributaryPreferredJoinAngleMinDeg = 38f;
            cfg.riverTributaryPreferredJoinAngleMaxDeg = 72f;
            cfg.riverConfluenceMinSpacingCells = minSpacing;
            cfg.riverConfluenceMinDistanceFromMainEndpointsCells = minEndpointDist;
            cfg.riverTributaryJoinTailCells = Mathf.Clamp(cfg.riverTributaryJoinTailCells, 8, 14);
            cfg.riverTributarySourcesPerConfluence = Mathf.Clamp(cfg.riverTributarySourcesPerConfluence, 6, 10);
        }

        static void ApplyTributaryPlacementReliability(MapGenConfig cfg)
        {
            if (cfg == null) return;
            cfg.riverRelaxedMissingTributaryFillPass = true;
            cfg.riverConfluenceEnabled = true;
            cfg.riverTributaryRecoveryEnabled = true;
            cfg.riverTributaryRecoveryRelaxGeometry = false;
            cfg.riverTributaryRecoveryAttempts = Mathf.Max(cfg.riverTributaryRecoveryAttempts, 36);
            cfg.riverTributaryRouteMaxAttempts = Mathf.Max(cfg.riverTributaryRouteMaxAttempts, 16);
            cfg.riverTributaryRouteBudgetMs = Mathf.Max(cfg.riverTributaryRouteBudgetMs, 400);
            cfg.riverTributaryRecoveryMaxMs = Mathf.Max(cfg.riverTributaryRecoveryMaxMs, 240);
            cfg.riverTributaryProceduralCandidatesPerSlot = Mathf.Max(cfg.riverTributaryProceduralCandidatesPerSlot, 64);
            cfg.riverTributaryCandidatesPerSlot = Mathf.Max(cfg.riverTributaryCandidatesPerSlot, 36);
            cfg.riverTributaryProceduralMaxSourceDistCells =
                Mathf.Max(cfg.riverTributaryProceduralMaxSourceDistCells, 112);
            cfg.maxTotalRiverBuildAttempts = Mathf.Max(cfg.maxTotalRiverBuildAttempts, 880);
            cfg.riverEarlyRejectConsecutiveToBreakStrictPass =
                Mathf.Max(cfg.riverEarlyRejectConsecutiveToBreakStrictPass, 28);
        }
    }
}
