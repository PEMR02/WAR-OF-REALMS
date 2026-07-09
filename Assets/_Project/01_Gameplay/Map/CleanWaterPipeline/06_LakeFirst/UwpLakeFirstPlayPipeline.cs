using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace Project.Gameplay.Map.CleanWaterPipeline
{
    /// <summary>
    /// Play/lobby: activa el pipeline experimental lake-first en Fase4/Fase9 UWP
    /// (main → lagos lejos del troncal → tributario outlet→confluencia validado).
    /// </summary>
    public static class UwpLakeFirstPlayPipeline
    {
        public static bool IsEnabled(RuntimeRiverWaterPipelineMode mode) =>
            mode == RuntimeRiverWaterPipelineMode.LakeFirstHydrology;

        public static void ApplyBeforeGenerate(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            cfg.uwpOwnedVisualPolicy = true;
            cfg.uwpLakeFirstHydrologyPipeline = true;
            cfg.riverRelaxedMissingTributaryFillPass = false;
            cfg.riverTributaryRecoveryEnabled = false;
            cfg.lakeRiverConnectorMaxPerMap = 0;

            CleanWaterHydrologyTuning.Apply(cfg);

            cfg.riverSurfaceVisualMeanderEnabled = true;
            cfg.riverSurfaceVisualMeanderAmplitudeCells = Mathf.Max(cfg.riverSurfaceVisualMeanderAmplitudeCells, 0.36f);
            cfg.riverSurfaceVisualMeanderFrequencyCells = Mathf.Min(cfg.riverSurfaceVisualMeanderFrequencyCells, 8.5f);
            cfg.riverSurfaceVisualMeanderEndFade01 = Mathf.Min(cfg.riverSurfaceVisualMeanderEndFade01, 0.07f);
            cfg.tributaryBedDepthBelowWater01 = Mathf.Max(cfg.tributaryBedDepthBelowWater01, 0.072f);

            cfg.riverVisualRibbonFullWidthCellsTributary = Mathf.Max(cfg.riverVisualRibbonFullWidthCellsTributary, 1.92f);
            cfg.riverSurfaceTributaryVisualWidthMul = Mathf.Max(cfg.riverSurfaceTributaryVisualWidthMul, 1.42f);
            cfg.riverSurfaceTributaryMeshOnlyWidthMul = Mathf.Max(cfg.riverSurfaceTributaryMeshOnlyWidthMul, 1.18f);
            cfg.lakeShoreVisualWidth = Mathf.Max(cfg.lakeShoreVisualWidth, 9f);
            cfg.lakeMSShoreExpandCells = Mathf.Max(cfg.lakeMSShoreExpandCells, 1);
            cfg.lakeMSPerimeterExpandWorld = Mathf.Max(cfg.lakeMSPerimeterExpandWorld, 5.2f);
            cfg.lakeRiverMouthBlendCells = Mathf.Max(cfg.lakeRiverMouthBlendCells, 7);

            cfg.debugHydrologyNetwork = false;
            cfg.debugRiverHydrologyPerf = false;
            cfg.debugWaterGeneratePerfDiagnostics = false;

            Debug.Log(
                $"[UwpLakeFirstPlayPipeline] enabled seed={cfg.seed} " +
                $"rivers={cfg.riverCount} lakes={cfg.lakeCount} uwpOwned=1 lakeFirst=1");
        }

        public static void ClearFromConfig(MapGenConfig cfg)
        {
            if (cfg == null)
                return;
            cfg.uwpLakeFirstHydrologyPipeline = false;
        }
    }
}
