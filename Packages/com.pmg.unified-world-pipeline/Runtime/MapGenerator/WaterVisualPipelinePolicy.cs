using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Centraliza la eleccion del pipeline visual de agua para que los flags legacy no se contradigan.
    /// La hidrologia/gameplay se mantiene intacta; esto solo decide como se dibuja la superficie.
    /// </summary>
    public static class WaterVisualPipelinePolicy
    {
        public static void ApplyToRuntimeConfig(MapGenConfig config)
        {
            if (config == null)
                return;

            switch (config.waterVisualPipeline)
            {
                case WaterVisualPipelineMode.CurrentSplitLakeMsRiverSurface:
                    ApplyCurrentSplitPipeline(config);
                    break;

                case WaterVisualPipelineMode.SplitLakeMsRiverWebFusion:
                    ApplySplitLakeMsRiverWebFusionPipeline(config);
                    break;

                case WaterVisualPipelineMode.SplitLakeMsRiverMouthFusion:
                    ApplySplitLakeMsRiverMouthFusionPipeline(config);
                    break;

                case WaterVisualPipelineMode.UnifiedSingleSurfaceExperimental:
                    ApplyUnifiedSingleSurfacePipeline(config);
                    break;

                case WaterVisualPipelineMode.LegacyMarchingSquaresAllWater:
                    config.riverVisualUseContinuousMesh = false;
                    config.riverVisualUseRiverSurfaceMeshStrip = false;
                    config.riverVisualRenderRiverAsMarchingSquaresCells = true;
                    config.debugRenderRiverRibbonMesh = false;
                    config.waterRoundedEdges = true;
                    break;

                case WaterVisualPipelineMode.LegacyChunkGridFallback:
                    config.riverVisualUseContinuousMesh = false;
                    config.riverVisualUseRiverSurfaceMeshStrip = false;
                    config.riverVisualRenderRiverAsMarchingSquaresCells = true;
                    config.debugRenderRiverRibbonMesh = false;
                    config.waterRoundedEdges = false;
                    break;
            }
        }

        public static bool IsCurrentSplit(MapGenConfig config)
        {
            if (config == null)
                return false;
            var mode = config.waterVisualPipeline;
            return mode == WaterVisualPipelineMode.CurrentSplitLakeMsRiverSurface ||
                mode == WaterVisualPipelineMode.SplitLakeMsRiverWebFusion ||
                mode == WaterVisualPipelineMode.SplitLakeMsRiverMouthFusion;
        }

        public static bool IsSplitLakeMsRiverWebFusion(MapGenConfig config)
        {
            if (config == null)
                return false;
            var mode = config.waterVisualPipeline;
            return mode == WaterVisualPipelineMode.SplitLakeMsRiverWebFusion ||
                mode == WaterVisualPipelineMode.SplitLakeMsRiverMouthFusion;
        }

        public static bool IsSplitLakeMsRiverMouthFusion(MapGenConfig config)
        {
            return config != null &&
                config.waterVisualPipeline == WaterVisualPipelineMode.SplitLakeMsRiverMouthFusion;
        }

        public static bool IsUnifiedSingleSurface(MapGenConfig config)
        {
            return config != null &&
                config.waterVisualPipeline == WaterVisualPipelineMode.UnifiedSingleSurfaceExperimental;
        }

        public static string RuntimeName(MapGenConfig config)
        {
            if (config == null)
                return "None";
            return config.waterVisualPipeline.ToString();
        }

        /// <summary>WebFusion UWP/RTS: un solo nivel Y para main, tributarios y lagos MS.</summary>
        public static bool UsesUwpUnifiedWaterSurfaceLevel(MapGenConfig config)
        {
            return config != null && IsSplitLakeMsRiverWebFusion(config);
        }

        /// <summary>baseWaterY = origin.y + waterHeight01*terrainY + waterSurfaceOffset.</summary>
        public static float ResolveUwpUnifiedChannelSurfaceWorldY(MapGenConfig config, float baseWaterY)
        {
            if (config == null)
                return baseWaterY;
            float antiZ = Mathf.Max(0.02f, config.riverRibbonAntiZFightYOffsetWorld);
            float extra = Mathf.Max(0f, config.riverSurfaceMeshExtraYOffsetWorld);
            return baseWaterY + antiZ + extra;
        }

        static void ApplyCurrentSplitPipeline(MapGenConfig config)
        {
            config.riverVisualUseContinuousMesh = true;
            config.riverVisualUseRiverSurfaceMeshStrip = true;
            config.riverVisualRenderRiverAsMarchingSquaresCells = false;
            config.debugRenderRiverRibbonMesh = false;
            config.waterRoundedEdges = true;
        }

        /// <summary>Duplica Current Split y aplica tuning de fusion río→lago alineado con Pruebas.</summary>
        static void ApplySplitLakeMsRiverWebFusionPipeline(MapGenConfig config)
        {
            ApplyCurrentSplitPipeline(config);

            float cellWorld = Mathf.Max(0.01f, config.cellSizeWorld);
            config.lakeMSPerimeterExpandWorld = Mathf.Max(config.lakeMSPerimeterExpandWorld, cellWorld * 0.55f);
            config.lakeRiverMouthBlendCells = Mathf.Max(config.lakeRiverMouthBlendCells, 5);
            config.riverLakeEmissaryEndpointFadeEnabled = true;
            config.riverLakeEmissaryLakeFadeCells = Mathf.Max(config.riverLakeEmissaryLakeFadeCells, 14f);
            config.riverLakeEmissaryRiverFadeCells = Mathf.Max(config.riverLakeEmissaryRiverFadeCells, 8f);
            config.riverLakeEmissaryLakeEndpointMinAlpha = 0.08f;
            config.riverLakeEmissaryRiverEndpointMinAlpha = 0.22f;
            // Respeta materiales DirectAsset del inspector (mismo look que Current Split).
            if (config.riverWaterMaterial == null &&
                config.riverWaterMaterialMode != WaterMaterialRuntimeMode.WORCustomShader)
                config.riverWaterMaterialMode = WaterMaterialRuntimeMode.WORCustomShader;
            if (config.tributaryWaterMaterial == null &&
                config.tributaryWaterMaterialMode != WaterMaterialRuntimeMode.WORCustomShader)
                config.tributaryWaterMaterialMode = WaterMaterialRuntimeMode.WORCustomShader;
        }

        /// <summary>WebFusion + recorte geométrico en orilla lago y fade/taper de boca alineados.</summary>
        static void ApplySplitLakeMsRiverMouthFusionPipeline(MapGenConfig config)
        {
            ApplySplitLakeMsRiverWebFusionPipeline(config);
            config.riverLakeEmissaryLakeEndpointMinAlpha = 0.04f;
            config.riverLakeEmissaryRiverEndpointMinAlpha = 0.18f;
        }

        static void ApplyUnifiedSingleSurfacePipeline(MapGenConfig config)
        {
            config.riverVisualUseContinuousMesh = false;
            config.riverVisualUseRiverSurfaceMeshStrip = false;
            config.riverVisualRenderRiverAsMarchingSquaresCells = true;
            config.debugRenderRiverRibbonMesh = false;
            config.waterRoundedEdges = true;
            config.waterMaskPostProcess = true;
            config.waterMaskSmoothIterations = Mathf.Max(config.waterMaskSmoothIterations, 3);
            config.waterEdgeSubdiv = Mathf.Max(config.waterEdgeSubdiv, 6);
            config.riverVisualUseContinuousField = true;
            config.riverVisualHalfWidthCells = Mathf.Max(config.riverVisualHalfWidthCells, config.unifiedRiverFieldMinHalfWidthCells);
            config.riverVisualSoftnessCells = Mathf.Max(config.riverVisualSoftnessCells, config.unifiedRiverFieldExtraSoftnessCells);
            config.riverMsMinAboveIsoAfterBlur = Mathf.Max(config.riverMsMinAboveIsoAfterBlur, 0.13f);
            config.waterEdgeBlurIterations = Mathf.Max(config.waterEdgeBlurIterations, 5);
            config.waterEdgeSmoothness = Mathf.Max(config.waterEdgeSmoothness, 1.2f);
            config.waterEdgeNoiseAmplitude = Mathf.Min(config.waterEdgeNoiseAmplitude, 0.045f);
        }
    }
}
