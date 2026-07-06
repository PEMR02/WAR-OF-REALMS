using System.Collections.Generic;
using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace Project.Gameplay.Map.CleanWaterPipeline
{
    public enum RuntimeRiverWaterPipelineMode
    {
        AutoCleanSplineWhenUwp = 0,
        LegacyCurrent = 1,
        CleanSplineExperimental = 2,
        HydroGraphV2 = 3
    }

    /// <summary>
    /// Play-only switch for testing the single-source river flow: centerline cache -> mesh -> carve.
    /// </summary>
    public static class CleanRiverSplinePlayPipeline
    {
        const float MainMeshWidthMul = 1.78f;
        const float TributaryMeshWidthMul = 1.68f;
        const float TributaryVisualWidthMul = 1.14f;
        const float MinMainCarveDepthWorld = 0.18f;
        const float MinRiverBedDepth01 = 0.030f;
        const float MinTributaryBedDepth01 = 0.072f;
        const float CarveFlatCenterRatio = 0.30f;
        const float CarveBankPower = 2.35f;
        const int MaxCleanRiverAttempts = 180;

        public static bool IsEnabled(RuntimeRiverWaterPipelineMode mode, MapGenConfig cfg = null) =>
            mode == RuntimeRiverWaterPipelineMode.CleanSplineExperimental ||
            (mode == RuntimeRiverWaterPipelineMode.AutoCleanSplineWhenUwp &&
             cfg != null &&
             cfg.uwpOwnedVisualPolicy);

        public static void ApplyBeforeGenerate(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            CleanWaterHydrologyTuning.Apply(cfg);
            ApplySplineContractTuning(cfg);
            cfg.uwpOwnedVisualPolicy = true;
            cfg.debugHydrologyNetwork = false;
            cfg.debugRiverHydrologyPerf = false;
            cfg.debugWaterGeneratePerfDiagnostics = false;

            Debug.Log(
                $"[CleanRiverSplinePlayPipeline] enabled seed={cfg.seed} " +
                $"rivers={cfg.riverCount} lakes={cfg.lakeCount} uwpOwned=1 " +
                $"mainMeshMul={cfg.riverSurfaceMainMeshOnlyWidthMul:F2} " +
                $"tribMeshMul={cfg.riverSurfaceTributaryMeshOnlyWidthMul:F2} " +
                $"carve={cfg.riverTerrainCarveDepthWorld:F3}m " +
                $"riverBed={cfg.riverBedDepthBelowWater01:F3} " +
                $"tribBed={cfg.tributaryBedDepthBelowWater01:F3}");
        }

        static void ApplySplineContractTuning(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            cfg.riverSurfaceMainMeshOnlyWidthMul = Mathf.Max(cfg.riverSurfaceMainMeshOnlyWidthMul, MainMeshWidthMul);
            cfg.riverSurfaceTributaryMeshOnlyWidthMul = Mathf.Max(cfg.riverSurfaceTributaryMeshOnlyWidthMul, TributaryMeshWidthMul);
            cfg.riverSurfaceTributaryVisualWidthMul = Mathf.Max(cfg.riverSurfaceTributaryVisualWidthMul, TributaryVisualWidthMul);
            cfg.riverSurfaceTributaryMinWidthMul = Mathf.Max(cfg.riverSurfaceTributaryMinWidthMul, TributaryVisualWidthMul);
            cfg.riverSurfaceTributaryMaxWidthMul = Mathf.Max(cfg.riverSurfaceTributaryMaxWidthMul, TributaryVisualWidthMul + 0.18f);

            cfg.riverTerrainCarveDepthWorld = Mathf.Max(cfg.riverTerrainCarveDepthWorld, MinMainCarveDepthWorld);
            cfg.riverBedDepthBelowWater01 = Mathf.Max(cfg.riverBedDepthBelowWater01, MinRiverBedDepth01);
            cfg.tributaryBedDepthBelowWater01 = Mathf.Max(cfg.tributaryBedDepthBelowWater01, MinTributaryBedDepth01);

            cfg.uwpCarveEuclideanBellProfileEnabled = true;
            cfg.uwpCarveTransverseFlatRatio = Mathf.Min(cfg.uwpCarveTransverseFlatRatio, CarveFlatCenterRatio);
            cfg.uwpCarveTransverseBankPower = Mathf.Max(cfg.uwpCarveTransverseBankPower, CarveBankPower);
            cfg.uwpCarveHalfWidthMulMain = Mathf.Clamp(cfg.uwpCarveHalfWidthMulMain, 0.86f, 0.96f);
            cfg.uwpCarveHalfWidthMulTributary = Mathf.Clamp(cfg.uwpCarveHalfWidthMulTributary, 0.92f, 1.02f);

            cfg.riverSurfaceWidthNoiseAmpMain = Mathf.Min(cfg.riverSurfaceWidthNoiseAmpMain, 0.020f);
            cfg.riverSurfaceWidthNoiseAmpTributary = Mathf.Min(cfg.riverSurfaceWidthNoiseAmpTributary, 0.024f);
            cfg.riverSurfaceWidthOrganicVarFrac = Mathf.Min(cfg.riverSurfaceWidthOrganicVarFrac, 0.035f);
            cfg.riverVisualFinalCleanupMaxPatchCells = Mathf.Max(cfg.riverVisualFinalCleanupMaxPatchCells, 128);
            cfg.riverSurfaceTributaryConfluenceApproachCells = Mathf.Max(cfg.riverSurfaceTributaryConfluenceApproachCells, 16);
            cfg.lakeRiverMouthBlendCells = Mathf.Max(cfg.lakeRiverMouthBlendCells, 8);

            if (cfg.uwpOwnedVisualPolicy)
            {
                cfg.riverRelaxedMissingTributaryFillPass = true;
                cfg.maxTotalRiverBuildAttempts = Mathf.Max(cfg.maxTotalRiverBuildAttempts, 80);
                return;
            }

            cfg.riverRelaxedMissingTributaryFillPass = false;
            cfg.riverTributaryRecoveryEnabled = false;
            cfg.riverTributaryRecoveryRelaxGeometry = false;
            cfg.riverSurfaceAllowStraightTrustedTributaries = true;
            cfg.riverSurfaceChaikinPasses = 0;
            cfg.riverSurfaceVisualMeanderEnabled = false;
            cfg.riverSurfaceVisualMeanderAmplitudeCells = 0f;
            cfg.riverSurfaceMaxSmoothDeviationCells = Mathf.Min(cfg.riverSurfaceMaxSmoothDeviationCells, 0.35f);
            cfg.riverSurfaceSplineMaxDeviationCells = Mathf.Min(cfg.riverSurfaceSplineMaxDeviationCells, 1.6f);
            cfg.riverSurfaceSplineSampleSpacingCells = Mathf.Max(cfg.riverSurfaceSplineSampleSpacingCells, 0.55f);
            cfg.maxTotalRiverBuildAttempts = Mathf.Clamp(cfg.maxTotalRiverBuildAttempts, 24, MaxCleanRiverAttempts);
            cfg.riverTributaryRouteMaxAttempts = Mathf.Clamp(cfg.riverTributaryRouteMaxAttempts, 4, 6);
            cfg.riverTributaryCandidatesPerSlot = Mathf.Clamp(cfg.riverTributaryCandidatesPerSlot, 16, 20);
            cfg.riverTributaryProceduralCandidatesPerSlot = Mathf.Clamp(cfg.riverTributaryProceduralCandidatesPerSlot, 20, 28);
        }

        public static void AuditAfterGenerate(GridSystem grid, MapGenConfig cfg)
        {
            if (grid == null || cfg == null)
            {
                Debug.LogWarning("[CleanRiverSplinePlayPipeline] audit skipped: grid/config null.");
                return;
            }

            int centerlines = grid.RiverCenterlinesCellSpace?.Count ?? 0;
            int surfaces = grid.RiverVisualSurfaces?.Count ?? 0;
            int built = 0;
            int skipped = 0;

            if (grid.RiverVisualSurfaces != null)
            {
                for (int i = 0; i < grid.RiverVisualSurfaces.Count; i++)
                {
                    if (grid.RiverVisualSurfaces[i].Skipped)
                        skipped++;
                    else
                        built++;
                }
            }

            AuditSurfaceCoverage(grid, cfg);

            string status = centerlines > 0
                && surfaces > 0
                && built > 0
                && grid.RiverVisualSurfaceCacheFrozen
                    ? "OK"
                    : "CHECK";

            Debug.Log(
                $"[CleanRiverSplinePlayPipeline] audit status={status} " +
                $"centerlines={centerlines} surfaces={surfaces} built={built} skipped={skipped} " +
                $"frozen={(grid.RiverVisualSurfaceCacheFrozen ? 1 : 0)} seed={cfg.seed}");
        }

        static void AuditSurfaceCoverage(GridSystem grid, MapGenConfig cfg)
        {
            if (grid?.RiverVisualSurfaces == null || cfg == null)
                return;

            for (int ri = 0; ri < grid.RiverVisualSurfaces.Count; ri++)
            {
                var surface = grid.RiverVisualSurfaces[ri];
                if (surface == null || surface.Skipped)
                {
                    Debug.LogWarning(
                        $"[CleanRiverSplineCoverage] river={ri} skipped=1 reason={surface?.SkipReason ?? "null"}");
                    continue;
                }

                var line = surface.FinalCenterlineCells;
                var widths = ResolveHalfWidths(surface);
                if (line == null || line.Count < 2 || widths == null || widths.Count == 0)
                {
                    Debug.LogWarning(
                        $"[CleanRiverSplineCoverage] river={ri} status=CHECK reason=missing_line_or_widths " +
                        $"points={line?.Count ?? 0} widths={widths?.Count ?? 0}");
                    continue;
                }

                int samples = 0;
                int narrow = 0;
                float minHalf = float.MaxValue;
                float maxHalf = 0f;
                float avgHalf = 0f;
                float minLogicalClearance = float.MaxValue;
                float maxLogicalClearance = float.MinValue;
                float expectedBedClearance = ExpectedBedClearanceWorld(cfg, ri);
                float terrainY = Mathf.Max(1e-4f, cfg.terrainHeightWorld);

                for (int i = 0; i < line.Count; i++)
                {
                    float halfW = widths[Mathf.Min(i, widths.Count - 1)];
                    minHalf = Mathf.Min(minHalf, halfW);
                    maxHalf = Mathf.Max(maxHalf, halfW);
                    avgHalf += halfW;

                    if (ri > 0 && halfW < grid.CellSizeWorld * 0.72f)
                        narrow++;

                    int x = Mathf.Clamp(Mathf.RoundToInt(line[i].x), 0, grid.Width - 1);
                    int z = Mathf.Clamp(Mathf.RoundToInt(line[i].y), 0, grid.Height - 1);
                    ref var cell = ref grid.GetCell(x, z);
                    float logicalClearance = (Mathf.Clamp01(cfg.waterHeight01) - cell.height01) * terrainY;
                    minLogicalClearance = Mathf.Min(minLogicalClearance, logicalClearance);
                    maxLogicalClearance = Mathf.Max(maxLogicalClearance, logicalClearance);
                    samples++;
                }

                if (samples > 0)
                    avgHalf /= samples;

                string status = surface.MeshBuilt && surface.CarveApplied && narrow == 0
                    ? "OK"
                    : "CHECK";

                Debug.Log(
                    $"[CleanRiverSplineCoverage] river={ri} status={status} type={(ri == 0 ? "main" : "tributary")} " +
                    $"points={line.Count} mesh={(surface.MeshBuilt ? 1 : 0)} carve={(surface.CarveApplied ? 1 : 0)} " +
                    $"halfW(avg/min/max)={avgHalf:F2}/{minHalf:F2}/{maxHalf:F2}m narrowSamples={narrow} " +
                    $"logicalWaterMinusTerrain(min/max)={minLogicalClearance:F2}/{maxLogicalClearance:F2}m " +
                    $"expectedBedBelowWater={expectedBedClearance:F2}m " +
                    $"lenMesh={surface.LengthMesh:F2} lenCarve={surface.LengthCarve:F2}");
            }
        }

        static float ExpectedBedClearanceWorld(MapGenConfig cfg, int riverIndex)
        {
            if (cfg == null)
                return 0f;
            float terrainY = Mathf.Max(1e-4f, cfg.terrainHeightWorld);
            float depth01 = riverIndex == 0
                ? Mathf.Max(cfg.riverBedDepthBelowWater01, 0f)
                : Mathf.Max(cfg.tributaryBedDepthBelowWater01, cfg.riverBedDepthBelowWater01);
            return depth01 * terrainY + Mathf.Max(0f, cfg.riverTerrainCarveDepthWorld);
        }

        static List<float> ResolveHalfWidths(RiverVisualSurfaceData surface)
        {
            if (surface == null)
                return null;
            if (surface.HalfWidthsWorld != null && surface.HalfWidthsWorld.Count > 0)
                return surface.HalfWidthsWorld;
            if (surface.MaskHalfWidthsWorld != null && surface.MaskHalfWidthsWorld.Count > 0)
                return surface.MaskHalfWidthsWorld;
            return null;
        }
    }
}
