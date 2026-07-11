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
            cfg.uwpLakeFirstSupplementalEnabled = true;
            cfg.inlandFeederTargetCount = -1;
            // Cupo mínimo: main + LakeSpill + Inland + HeadwaterFeeder.
            cfg.riverCount = Mathf.Max(cfg.riverCount, 4);
            if (cfg.headwaterFeederTargetCount < 0)
                cfg.headwaterFeederTargetCount = 1;
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
            cfg.lakeShoreVisualWidth = Mathf.Max(cfg.lakeShoreVisualWidth, 11f);
            cfg.lakeMSShoreExpandCells = Mathf.Max(cfg.lakeMSShoreExpandCells, 2);
            cfg.lakeMSPerimeterExpandWorld = Mathf.Max(cfg.lakeMSPerimeterExpandWorld, 6.8f);
            cfg.lakeRiverMouthBlendCells = Mathf.Max(cfg.lakeRiverMouthBlendCells, 7);

            ApplyLakeDepthTuning(cfg);

            cfg.debugHydrologyNetwork = false;
            cfg.debugRiverHydrologyPerf = false;
            cfg.debugWaterGeneratePerfDiagnostics = false;

            ApplyMapSizeHydrologyScaling(cfg);
            ApplyLakeFirstMainRiverSpanPolicy(cfg);

            Debug.Log(
                $"[UwpLakeFirstPlayPipeline] enabled seed={cfg.seed} " +
                $"rivers={cfg.riverCount} lakes={cfg.lakeCount} grid={cfg.gridW} uwpOwned=1 lakeFirst=1 supplemental=1 " +
                $"mainW={cfg.riverVisualRibbonFullWidthCellsMain:F2}c maxLakeCells={cfg.maxLakeCells} " +
                $"lakeDepth={cfg.lakeBedDepthBelowWater01:F3} b2bWeight={cfg.riverMainBorderToBorderWeight:F2} borderExt={cfg.riverMainMaxBorderPathExtensionCells}");
        }

        /// <summary>
        /// Lake First: el troncal debe cruzar el mapa (borde opuesto). Lagos se conectan por spill, no como meta del main.
        /// </summary>
        static void ApplyLakeFirstMainRiverSpanPolicy(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            int minDim = Mathf.Clamp(Mathf.Min(
                cfg.gridW > 0 ? cfg.gridW : 256,
                cfg.gridH > 0 ? cfg.gridH : 256), 64, 512);

            cfg.riverMainAllowBorderToBorder = true;
            cfg.riverMainBorderToBorderWeight = 2.8f;
            cfg.riverMainLakeSinkWeight = 0f;
            cfg.riverMainBorderStartWeight = 0f;
            cfg.riverMainInteriorSourceWeight = 0f;
            cfg.riverMainBorderExitInsetCells = 0;

            float minDiagRatio = 0.36f;
            cfg.riverMainMinPathToMapDiagRatio = Mathf.Min(cfg.riverMainMinPathToMapDiagRatio, minDiagRatio);

            if (minDim >= 320)
                cfg.riverPlacementMaxAttemptsPerRiver = Mathf.Max(cfg.riverPlacementMaxAttemptsPerRiver, 16);

            int borderExtend = Mathf.Clamp(Mathf.RoundToInt(minDim * 0.05f), 10, 28);
            cfg.riverMainMaxBorderPathExtensionCells = Mathf.Max(cfg.riverMainMaxBorderPathExtensionCells, borderExtend);
            cfg.riverSurfaceBorderGhostCells = Mathf.Max(cfg.riverSurfaceBorderGhostCells, 3.75f);
            cfg.riverSurfaceFlatMapBorderCut = false;
            cfg.riverSurfaceSkipCapAtMapBorder = true;
        }

        /// <summary>
        /// Restaura profundidad de lagos en Play (UwpTerrainProfileModule la capa a ~0.022).
        /// </summary>
        static void ApplyLakeDepthTuning(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            int minDim = Mathf.Clamp(Mathf.Min(
                cfg.gridW > 0 ? cfg.gridW : 256,
                cfg.gridH > 0 ? cfg.gridH : 256), 64, 512);
            float sizeT = Mathf.InverseLerp(192f, 384f, minDim);

            cfg.lakeBedDepthBelowWater01 = Mathf.Clamp(
                Mathf.Lerp(0.152f, 0.16f, sizeT),
                0.13f,
                0.16f);
            cfg.lakeBedMinDepthBelowWater01 = Mathf.Clamp(
                Mathf.Lerp(0.048f, 0.05f, sizeT),
                0.038f,
                0.05f);
            cfg.lakeBedDepthRampCells = Mathf.Clamp(
                Mathf.Lerp(10f, 8f, sizeT),
                7f,
                14f);
        }

        /// <summary>
        /// Escala ancho troncal, tamaño de lagos y sesgo BorderToBorder según gridW/H (lobby 192–384).
        /// </summary>
        static void ApplyMapSizeHydrologyScaling(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            int minDim = Mathf.Clamp(Mathf.Min(
                cfg.gridW > 0 ? cfg.gridW : 256,
                cfg.gridH > 0 ? cfg.gridH : 256), 64, 512);

            float sizeT = Mathf.InverseLerp(192f, 384f, minDim);
            float widthMul = Mathf.Lerp(1f, 1.28f, sizeT);
            float mainW = cfg.riverVisualRibbonFullWidthCellsMain * widthMul;
            float tribW = cfg.riverVisualRibbonFullWidthCellsTributary * widthMul;
            cfg.riverVisualRibbonFullWidthCellsMain = Mathf.Clamp(mainW, 4f, 8.5f);
            cfg.riverVisualRibbonFullWidthCellsTributary = Mathf.Clamp(tribW, 1.4f, 4.2f);
            cfg.riverSurfaceMainMeshOnlyWidthMul = Mathf.Max(cfg.riverSurfaceMainMeshOnlyWidthMul, 1.82f + sizeT * 0.12f);

            float areaMul = (minDim / 256f) * (minDim / 256f);
            // +50% sobre el cap base: flood-fill más largo → lagos más grandes en XZ.
            cfg.maxLakeCells = Mathf.Clamp(Mathf.RoundToInt(340f * areaMul * 1.5f), 360, 960);
            cfg.lakeMSShoreExpandCells = Mathf.Max(cfg.lakeMSShoreExpandCells, 2);
            cfg.lakeMSPerimeterExpandWorld = Mathf.Max(cfg.lakeMSPerimeterExpandWorld, 7.2f);
            cfg.lakeShoreVisualWidth = Mathf.Max(cfg.lakeShoreVisualWidth, 12f);

            if (minDim >= 320)
            {
                cfg.riverSurfaceBorderGhostCells = Mathf.Max(cfg.riverSurfaceBorderGhostCells, 4f);
            }
        }

        public static void ClearFromConfig(MapGenConfig cfg)
        {
            if (cfg == null)
                return;
            cfg.uwpLakeFirstHydrologyPipeline = false;
        }
    }
}
