using PMG.UnifiedWorldPipeline;
using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace Project.Gameplay.Map.Generation
{
    /// <summary>
    /// Perfil hidrología UWP para la ruta RTS (Play). Reversible vía
    /// <see cref="MapGenerationRuntimeContext.applyUwpHydrologyProfile"/>.
    /// </summary>
    public static class RtsHydrologyProfile
    {
        /// <summary>Ancho visual/carve troncal (+15% sobre 6.25 → cuenca más legible).</summary>
        public const float DefaultMainFullWidthCells = 7.1875f;
        public const float DefaultMainCarveDepthWorld = 0.14f;
        /// <summary>Mesh troncal ≈ carve (evita orilla blanca plana ancha tipo bandeja).</summary>
        public const float DefaultMainMeshOnlyWidthMul = 1.32f;
        /// <summary>Tributario/headwater más estrechos (0.8× respecto a 1.48).</summary>
        public const float DefaultTributaryMeshOnlyWidthMul = 1.184f;
        public const float DefaultTributaryConfluenceMeshBoostMul = 1.10f;
        public const float DefaultMainSurfaceRootYOffsetWorld = 0f;
        /// <summary>Trib ≈ 0.35× main (antes 0.44; −20% visual/carve).</summary>
        public const float DefaultTributaryToMainWidthRatio = 0.352f;
        /// <summary>Boca trib→main más ancha que el cuerpo (unión mesh/carve).</summary>
        public const float DefaultTributaryConfluenceEndWidthRatio = 0.82f;
        /// <summary>Lagos 1.5× el maxLakeCells compilado (área lógica).</summary>
        public const float DefaultLakeAreaScale = 1.5f;
        public const int DefaultPlayMinRiverBuildAttempts = 80;
        public const int DefaultPlayMaxRiverBuildAttempts = 180;

        public static void Apply(MapGenConfig cfg, float mainFullWidthCellsOverride = 0f)
        {
            if (cfg == null)
                return;

            UwpHydrologyContract.ApplyRtsPlayPlacementTuning(cfg);
            UwpHydrologyContract.ApplyVisualWidths(cfg, null);

            float mainW = mainFullWidthCellsOverride > 0.01f
                ? mainFullWidthCellsOverride
                : DefaultMainFullWidthCells;
            float tribW = Mathf.Clamp(
                mainW * DefaultTributaryToMainWidthRatio,
                1.0f,
                mainW * 0.50f);
            UwpHydrologyContract.RetuneVisualWidths(cfg, mainW, tribW, DefaultMainCarveDepthWorld);

            UwpRiverProfileModule.Apply(cfg, null);
            ApplyRtsTributaryVisibilityRules(cfg);
            ApplyRtsCarveUniformityRules(cfg, mainW);
            ApplyRtsMainRiverWaterPresentation(cfg);
            ApplyRtsTributaryReliabilityRules(cfg);
            ApplyRtsCenterlinePathRules(cfg);
            UwpRiverProfileModule.ApplyExportFix(cfg, null);
            ApplyRtsBorderEndpointRules(cfg);

            cfg.ignoreLobbyHydrologyCaps = true;
            UwpHydrologyContract.ApplyRtsPlayPlacementTuning(cfg);
            cfg.riverRelaxedMissingTributaryFillPass = true;
            cfg.maxTotalRiverBuildAttempts = Mathf.Clamp(
                Mathf.Max(cfg.maxTotalRiverBuildAttempts, DefaultPlayMinRiverBuildAttempts),
                DefaultPlayMinRiverBuildAttempts,
                DefaultPlayMaxRiverBuildAttempts);
            cfg.riverTributaryRouteBudgetMs = Mathf.Clamp(
                Mathf.Max(cfg.riverTributaryRouteBudgetMs, 320),
                200,
                420);
            cfg.riverTributaryRouteMaxAttempts = Mathf.Clamp(cfg.riverTributaryRouteMaxAttempts, 8, 16);
            cfg.riverTributaryCandidatesPerSlot = Mathf.Clamp(
                Mathf.Max(cfg.riverTributaryCandidatesPerSlot, 24),
                16,
                36);
            cfg.riverPlacementMaxAttemptsPerRiver = Mathf.Clamp(cfg.riverPlacementMaxAttemptsPerRiver, 6, 12);
            cfg.riverTributaryProceduralCandidatesPerSlot = Mathf.Clamp(
                Mathf.Max(cfg.riverTributaryProceduralCandidatesPerSlot, 48),
                32,
                64);
            cfg.riverTributaryProceduralMaxSourceDistCells =
                Mathf.Max(cfg.riverTributaryProceduralMaxSourceDistCells, 96);

            ApplyRtsPlayPerformanceCaps(cfg);
            UwpRiverProfileModule.ApplyBankTerrainFix(cfg);
            ApplyRtsPostBankTerrainGuards(cfg);

            // Lagos más grandes (área); no toca profundidades bed/carve.
            cfg.maxLakeCells = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Max(80, cfg.maxLakeCells) * DefaultLakeAreaScale),
                150,
                5000);

            Debug.LogWarning(
                "[HydrologyCompileAudit] RTS perfil UWP: " +
                $"riverCount={cfg.riverCount} avoidCross={cfg.riverAvoidCrossingOtherRivers} " +
                $"maxAttempts={cfg.maxTotalRiverBuildAttempts} fillPass={cfg.riverRelaxedMissingTributaryFillPass} " +
                $"main={cfg.riverVisualRibbonFullWidthCellsMain:F2}c carve={cfg.riverTerrainCarveDepthWorld:F3}m " +
                $"trib={cfg.riverVisualRibbonFullWidthCellsTributary:F2}c maxLake={cfg.maxLakeCells} " +
                $"meshMul={cfg.riverSurfaceMainMeshOnlyWidthMul:F2} tribMeshMul={cfg.riverSurfaceTributaryMeshOnlyWidthMul:F2} " +
                $"flatRatio={cfg.uwpCarveTransverseFlatRatio:F2} meanderAmp={cfg.riverSurfaceVisualMeanderAmplitudeCells:F2} " +
                $"lakeBed={cfg.lakeBedDepthBelowWater01:F3} bandCells={cfg.unifiedWaterTerrainBandCells:F2} " +
                $"rootY={cfg.riverSurfaceMainRootYOffsetWorld:F2}m " +
                $"uwpOwned={cfg.uwpOwnedVisualPolicy} bankFall={cfg.riverVisualTerrainBankFalloffCells} " +
                $"confApproach={cfg.riverSurfaceTributaryConfluenceApproachCells} " +
                $"confEndMul={cfg.riverConfluenceTributaryEndWidthMul:F2}");

            if (cfg.debugLogs || cfg.debugHydrologyNetwork)
            {
                Debug.Log(
                    "[MapGen] Perfil hidrología UWP (RTS Play) aplicado: " +
                    $"main={cfg.riverVisualRibbonFullWidthCellsMain:F2}c " +
                    $"trib={cfg.riverVisualRibbonFullWidthCellsTributary:F2}c " +
                    $"carve={cfg.riverTerrainCarveDepthWorld:F3}m falloff={cfg.riverTerrainCarveFalloffCells}c " +
                    $"carveMode={(cfg.uwpOwnedVisualPolicy ? "visualMaskOnly" : "legacyStacked")} " +
                    $"uwpOwned={cfg.uwpOwnedVisualPolicy} maxAttempts={cfg.maxTotalRiverBuildAttempts} " +
                    $"spline={(cfg.riverSurfaceUseSplineVisualCenterline ? 1 : 0)} chaikin={cfg.riverSurfaceChaikinPasses}");
            }
        }

        /// <summary>Tributarios colocados en Fase4 no deben perderse en malla MS / cleanup.</summary>
        static void ApplyRtsTributaryVisibilityRules(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            cfg.riverVisualMinSurfacePieceLengthCells = 3;
            cfg.riverVisualMinSurfacePieceAreaCells = 2;
            cfg.riverVisualMinDetachedPatchCells = 2;
            cfg.riverSurfaceTributaryVisualWidthMul = 0.74f;
            cfg.riverSurfaceTributaryConfluenceApproachCells = Mathf.Clamp(
                cfg.riverSurfaceTributaryConfluenceApproachCells, 10, 14);
            cfg.riverConfluenceTributaryEndWidthMul = DefaultTributaryConfluenceEndWidthRatio;
            cfg.riverConfluenceHideLastSegmentUnderMain = true;
            cfg.riverSurfaceSkipTributaryConfluenceCap = true;
            cfg.riverSurfaceAllowStraightTrustedTributaries = true;
            cfg.riverSurfaceWidthNoiseAmpMain = Mathf.Clamp(cfg.riverSurfaceWidthNoiseAmpMain, 0.045f, 0.08f);
            cfg.lakeMSRemoveNearRiverDistanceCells = 0;
            cfg.riverVisualFinalCleanupMaxPatchCells = Mathf.Max(cfg.riverVisualFinalCleanupMaxPatchCells, 96);
            cfg.lakeRiverMouthBlendCells = Mathf.Max(cfg.lakeRiverMouthBlendCells, 7);
        }

        /// <summary>Mesh troncal más ancho que carve; Y de superficie unificado con lagos MS (sin offset local).</summary>
        static void ApplyRtsMainRiverWaterPresentation(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            cfg.riverSurfaceMainMeshOnlyWidthMul = DefaultMainMeshOnlyWidthMul;
            cfg.riverSurfaceTributaryMeshOnlyWidthMul = DefaultTributaryMeshOnlyWidthMul;
            cfg.riverSurfaceMainRootYOffsetWorld = DefaultMainSurfaceRootYOffsetWorld;
            cfg.riverRibbonVerticalLiftWorld = 0f;
            cfg.riverSurfaceMeshExtraYOffsetWorld = 0.02f;
            cfg.riverRibbonAntiZFightYOffsetWorld = 0.02f;
            cfg.riverSurfaceMainArcWidthVarEnabled = true;
            cfg.riverSurfaceWidthNoiseAmpMain = Mathf.Clamp(cfg.riverSurfaceWidthNoiseAmpMain, 0.05f, 0.09f);
            cfg.riverSurfaceWidthNoiseAmpTributary = Mathf.Min(cfg.riverSurfaceWidthNoiseAmpTributary, 0.045f);
            cfg.riverSurfaceVisualMeanderEnabled = true;
            // Knob base; la amplitud real la escala RiverSurfaceMeshBuilder con min(W,H).
            cfg.riverSurfaceVisualMeanderAmplitudeCells = Mathf.Max(cfg.riverSurfaceVisualMeanderAmplitudeCells, 0.48f);
            cfg.riverSurfaceVisualMeanderFrequencyCells = Mathf.Clamp(cfg.riverSurfaceVisualMeanderFrequencyCells, 10f, 20f);
        }

        /// <summary>Entrada/salida en borde del mapa: extensión fantasma y sin corte plano abrupto.</summary>
        static void ApplyRtsBorderEndpointRules(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            cfg.riverSurfaceFlatMapBorderCut = false;
            cfg.riverSurfaceBorderGhostCells = 2.75f;
            cfg.riverSurfaceBorderEndpointWidthMul = 1.55f;
            cfg.riverSurfaceSkipCapAtMapBorder = true;
            cfg.sandShoreExtraDistanceNoise = 0f;
            cfg.sandEdgeNoiseStrength = 0f;
            cfg.sandShoreFalloffPower = 1.65f;
            cfg.sandShoreAlphamapSmoothCap = 2;
            cfg.terrainAlphamapSmoothPasses = Mathf.Max(cfg.terrainAlphamapSmoothPasses, 2);
        }

        /// <summary>Carve más estrecho que mesh y profundidad plana (agua Y uniforme).</summary>
        static void ApplyRtsCarveUniformityRules(MapGenConfig cfg, float mainFullWidthCells)
        {
            if (cfg == null)
                return;

            cfg.riverTerrainCarveFalloffCells = 0;
            cfg.riverVisualTerrainCarveExtraCells = 0;
            // Bancos suaves (cuenca): falloff fuera de máscara; lecho sigue flat (carveFalloff=0).
            cfg.riverVisualTerrainBankFalloffCells = 5;
            cfg.riverVisualTerrainBankSoftness = 0.72f;
            cfg.riverVisualTerrainCenterDepthMul = 1f;
            cfg.riverTerrainCarveCenterCurve = 1f;
            cfg.riverTerrainCarveFordMul = Mathf.Clamp(cfg.riverTerrainCarveFordMul, 0.88f, 0.95f);
            cfg.riverBedDepthBelowWater01 = Mathf.Clamp(cfg.riverBedDepthBelowWater01, 0.022f, 0.028f);
            // Tributario/boca de lago más profundo: mejor cruce con terreno y unión tributario-lago honda.
            cfg.tributaryBedDepthBelowWater01 = Mathf.Clamp(
                Mathf.Max(cfg.tributaryBedDepthBelowWater01, 0.066f),
                0.036f,
                0.09f);
            // Lagos más profundos: StylizedWater2 usa profundidad en espacio-mundo (_WorldSpaceDepth)
            // para el color profundo y para atenuar las cáusticas (la "cuadrícula" del fondo). Con el
            // cap previo (0.022) el lago era demasiado somero y las cáusticas se veían en todo el fondo.
            // Maximizamos la profundidad dentro del rango que permite terrainHeightWorld.
            cfg.lakeBedDepthBelowWater01 = Mathf.Clamp(
                Mathf.Max(cfg.lakeBedDepthBelowWater01, 0.14f),
                0.05f,
                0.16f);
            cfg.lakeBedMinDepthBelowWater01 = Mathf.Clamp(
                Mathf.Max(cfg.lakeBedMinDepthBelowWater01, 0.045f),
                0f,
                0.05f);
            // Tributarios más estrechos (mesh+carve); profundidades bed sin cambio.
            cfg.riverVisualRibbonFullWidthCellsTributary = Mathf.Clamp(
                mainFullWidthCells * DefaultTributaryToMainWidthRatio,
                1.0f,
                mainFullWidthCells * 0.50f);
            cfg.riverWidthRadiusCells = Mathf.Clamp(Mathf.RoundToInt(mainFullWidthCells * 0.25f), 1, 6);
            cfg.riverVisualRasterMaskExtraCellMargin = 0f;
            cfg.shoreSmoothRadiusCells = Mathf.Max(cfg.shoreSmoothRadiusCells, 4);
            cfg.shoreSmoothStrength = Mathf.Max(cfg.shoreSmoothStrength, 0.38f);
            cfg.sandShoreCells = Mathf.Max(cfg.sandShoreCells, 6);
            cfg.riverConfluenceTerrainMaxHeightAboveWater01 = 0f;
            cfg.lakeRiverConnectorMaxPerMap = 1;
            cfg.uwpCarveEuclideanBellProfileEnabled = true;
            // flatRatio/bankPower: legacy / no Lake First flatFloor. Headwater Play usa bandeja plana.
            cfg.uwpCarveTransverseFlatRatio = 0.28f;
            cfg.uwpCarveTransverseBankPower = 1.45f;
            cfg.uwpCarveHalfWidthMulMain = 0.98f;
            cfg.uwpCarveHalfWidthMulTributary = 1f;
            cfg.uwpCarveLongitudinalRadiusNoiseAmpMain = 0.03f;
            cfg.uwpCarveLongitudinalRadiusNoiseAmpTributary = 0.025f;
            cfg.uwpCarveLongitudinalRadiusNoiseScale = 0.04f;
        }

        static void ApplyRtsTributaryReliabilityRules(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            cfg.riverTributaryRecoveryRelaxGeometry = false;
            cfg.riverTributaryRecoveryEnabled = true;
            cfg.riverTributaryRecoveryAttempts = Mathf.Max(cfg.riverTributaryRecoveryAttempts, 12);
            cfg.riverTributaryRecoveryMaxMs = Mathf.Max(cfg.riverTributaryRecoveryMaxMs, 120);
            cfg.riverLogPlacementFailureSummary = false;
        }

        static void ApplyRtsPostBankTerrainGuards(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            // Carve legacy off; bancos suaves + sand shore para cuenca main (no cajón).
            cfg.riverTerrainCarveFalloffCells = 0;
            cfg.riverVisualTerrainCarveExtraCells = 0;
            cfg.riverVisualTerrainBankFalloffCells = Mathf.Clamp(
                Mathf.Max(cfg.riverVisualTerrainBankFalloffCells, 5), 4, 6);
            cfg.riverVisualTerrainBankSoftness = Mathf.Clamp(
                Mathf.Max(cfg.riverVisualTerrainBankSoftness, 0.72f), 0.55f, 0.85f);
            cfg.riverVisualTerrainCenterDepthMul = 1f;
            cfg.sandShoreCells = Mathf.Clamp(Mathf.Max(cfg.sandShoreCells, 6), 5, 7);
            cfg.shoreSmoothRadiusCells = Mathf.Clamp(cfg.shoreSmoothRadiusCells, 2, 6);
            cfg.shoreSmoothStrength = Mathf.Clamp(cfg.shoreSmoothStrength, 0.28f, 0.42f);
            // Evitar dual-grass con máscara grayscale (SampleScene trae blend 0.38 sin layer).
            cfg.grassDryBlendStrength = 0f;
            cfg.grassDryLayer = null;
            cfg.riverConfluenceTributaryEndWidthMul = DefaultTributaryConfluenceEndWidthRatio;
            cfg.riverSurfaceTributaryConfluenceApproachCells =
                Mathf.Clamp(cfg.riverSurfaceTributaryConfluenceApproachCells, 10, 14);

            // ApplyBankTerrainFix → ApplyShallowerWaterBedCaps pisa camas profundas del perfil RTS.
            cfg.lakeBedDepthBelowWater01 = Mathf.Clamp(
                Mathf.Max(cfg.lakeBedDepthBelowWater01, 0.14f), 0.05f, 0.16f);
            cfg.lakeBedMinDepthBelowWater01 = Mathf.Clamp(
                Mathf.Max(cfg.lakeBedMinDepthBelowWater01, 0.045f), 0f, 0.05f);
            cfg.tributaryBedDepthBelowWater01 = Mathf.Clamp(
                Mathf.Max(cfg.tributaryBedDepthBelowWater01, 0.066f), 0.036f, 0.09f);
            cfg.riverBedDepthBelowWater01 = Mathf.Clamp(cfg.riverBedDepthBelowWater01, 0.022f, 0.028f);
            // Lip/banda ancha = "orilla blanca plana" bajo el agua (no es solo meshMul).
            cfg.unifiedWaterTerrainBandCells = Mathf.Min(cfg.unifiedWaterTerrainBandCells, 0.75f);
            cfg.unifiedWaterTerrainBankLipWorld = Mathf.Min(cfg.unifiedWaterTerrainBankLipWorld, 0.014f);
            cfg.unifiedWaterShoreTerrainOffsetWorld = Mathf.Min(cfg.unifiedWaterShoreTerrainOffsetWorld, 0.012f);
            cfg.uwpCarveTransverseFlatRatio = 0.28f;
            cfg.uwpCarveTransverseBankPower = 1.45f;
            cfg.riverSurfaceMainMeshOnlyWidthMul = DefaultMainMeshOnlyWidthMul;
            cfg.riverBankBlendStrength = Mathf.Min(cfg.riverBankBlendStrength, 0.08f);
        }

        /// <summary>Caps finales Play RTS: más aire que el editor UWP pero sin coste de 880 intentos.</summary>
        static void ApplyRtsPlayPerformanceCaps(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            cfg.maxTotalRiverBuildAttempts = Mathf.Clamp(
                cfg.maxTotalRiverBuildAttempts,
                DefaultPlayMinRiverBuildAttempts,
                DefaultPlayMaxRiverBuildAttempts);
            cfg.riverPlacementMaxAttemptsPerRiver = Mathf.Clamp(cfg.riverPlacementMaxAttemptsPerRiver, 6, 12);
            cfg.riverTributaryRecoveryAttempts = Mathf.Clamp(cfg.riverTributaryRecoveryAttempts, 8, 20);
            cfg.maxRetries = Mathf.Clamp(cfg.maxRetries, 1, 1);
            cfg.debugLogs = false;
            cfg.debugHydrologyNetwork = false;
            cfg.debugRiverHydrologyPerf = false;
            cfg.debugRiverVisualStats = false;
            cfg.debugWaterGeneratePerfDiagnostics = false;
        }

        /// <summary>
        /// Recorrido visual estable: menos chaikin/reshape agresivo (evita malla fragmentada y desalineada del carve).
        /// </summary>
        static void ApplyRtsCenterlinePathRules(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            cfg.riverSurfaceUseSplineVisualCenterline = true;
            cfg.riverSurfaceChaikinPasses = 2;
            cfg.riverSurfaceSplineMaxAngleStepDeg = 18f;
            cfg.riverSurfaceSplineMaxDeviationCells = 3.4f;
            cfg.riverSurfaceSplineSampleSpacingCells = 0.40f;
            cfg.riverSurfaceSplineTension = 0.42f;
            cfg.riverSurfaceVisualSpacingCells = 0.62f;

            cfg.riverMainForceOrganicReshape = true;
            cfg.riverMainOrganicReshapeBudgetMs = 22;
            cfg.riverConfluenceTributaryEndWidthMul = DefaultTributaryConfluenceEndWidthRatio;
            cfg.riverSurfaceTributaryConfluenceApproachCells =
                Mathf.Clamp(cfg.riverSurfaceTributaryConfluenceApproachCells, 10, 14);
        }
    }
}
