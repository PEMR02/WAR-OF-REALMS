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
        public const float DefaultMainFullWidthCells = 5.35f;
        public const float DefaultMainCarveDepthWorld = 0.14f;
        public const float DefaultMainMeshOnlyWidthMul = 1.72f;
        public const float DefaultTributaryMeshOnlyWidthMul = 1.5f;
        public const float DefaultTributaryConfluenceMeshBoostMul = 1.10f;
        public const float DefaultMainSurfaceRootYOffsetWorld = 0f;

        public static void Apply(MapGenConfig cfg, float mainFullWidthCellsOverride = 0f)
        {
            if (cfg == null)
                return;

            UwpHydrologyContract.ApplyRtsPlayPlacementTuning(cfg);
            UwpHydrologyContract.ApplyVisualWidths(cfg, null);

            float mainW = mainFullWidthCellsOverride > 0.01f
                ? mainFullWidthCellsOverride
                : DefaultMainFullWidthCells;
            float tribW = Mathf.Min(cfg.riverVisualRibbonFullWidthCellsTributary, mainW * 0.95f);
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
                Mathf.Max(cfg.maxTotalRiverBuildAttempts, 24),
                24,
                48);
            cfg.riverTributaryRouteBudgetMs = Mathf.Clamp(
                Mathf.Max(cfg.riverTributaryRouteBudgetMs, 280),
                120,
                320);
            cfg.riverTributaryRouteMaxAttempts = Mathf.Clamp(cfg.riverTributaryRouteMaxAttempts, 4, 8);
            cfg.riverTributaryCandidatesPerSlot = Mathf.Clamp(
                Mathf.Max(cfg.riverTributaryCandidatesPerSlot, 16),
                4,
                24);
            cfg.riverPlacementMaxAttemptsPerRiver = Mathf.Clamp(cfg.riverPlacementMaxAttemptsPerRiver, 4, 8);
            cfg.riverTributaryProceduralCandidatesPerSlot = Mathf.Clamp(
                Mathf.Max(cfg.riverTributaryProceduralCandidatesPerSlot, 24),
                8,
                32);
            cfg.riverTributaryProceduralMaxSourceDistCells =
                Mathf.Max(cfg.riverTributaryProceduralMaxSourceDistCells, 72);

            ApplyRtsPlayPerformanceCaps(cfg);

            Debug.LogWarning(
                "[HydrologyCompileAudit] RTS perfil UWP: " +
                $"riverCount={cfg.riverCount} avoidCross={cfg.riverAvoidCrossingOtherRivers} " +
                $"maxAttempts={cfg.maxTotalRiverBuildAttempts} fillPass={cfg.riverRelaxedMissingTributaryFillPass} " +
                $"main={cfg.riverVisualRibbonFullWidthCellsMain:F2}c carve={cfg.riverTerrainCarveDepthWorld:F3}m " +
                $"meshMul={cfg.riverSurfaceMainMeshOnlyWidthMul:F2} tribMeshMul={cfg.riverSurfaceTributaryMeshOnlyWidthMul:F2} " +
                $"rootY={cfg.riverSurfaceMainRootYOffsetWorld:F2}m " +
                $"lift={cfg.riverRibbonVerticalLiftWorld:F2}m meshY={cfg.riverSurfaceMeshExtraYOffsetWorld:F2}m " +
                $"uwpOwned={cfg.uwpOwnedVisualPolicy} ignoreLobbyCaps={cfg.ignoreLobbyHydrologyCaps}");

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
            cfg.riverSurfaceTributaryVisualWidthMul = 1.08f;
            cfg.riverSurfaceTributaryConfluenceApproachCells = Mathf.Max(cfg.riverSurfaceTributaryConfluenceApproachCells, 14);
            cfg.riverConfluenceTributaryEndWidthMul = Mathf.Max(cfg.riverConfluenceTributaryEndWidthMul, 1f);
            cfg.riverSurfaceWidthNoiseAmpMain = Mathf.Min(cfg.riverSurfaceWidthNoiseAmpMain, 0.028f);
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
            cfg.riverSurfaceMainArcWidthVarEnabled = false;
            cfg.riverSurfaceWidthNoiseAmpMain = Mathf.Min(cfg.riverSurfaceWidthNoiseAmpMain, 0.035f);
            cfg.riverSurfaceWidthNoiseAmpTributary = Mathf.Min(cfg.riverSurfaceWidthNoiseAmpTributary, 0.045f);
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
            cfg.riverVisualTerrainBankFalloffCells = 0;
            cfg.riverVisualTerrainBankSoftness = 1f;
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
            // Tributarios visualmente más anchos (mesh); el carve sigue el ancho del mesh (ver TerrainExporter).
            cfg.riverVisualRibbonFullWidthCellsTributary = Mathf.Clamp(
                cfg.riverVisualRibbonFullWidthCellsTributary * 1.4f,
                0.5f,
                Mathf.Min(3f, mainFullWidthCells * 0.9f));
            cfg.riverWidthRadiusCells = Mathf.Clamp(Mathf.RoundToInt(mainFullWidthCells * 0.25f), 1, 6);
            cfg.riverVisualRasterMaskExtraCellMargin = 0f;
            cfg.shoreSmoothRadiusCells = 2;
            cfg.shoreSmoothStrength = 0.30f;
            cfg.sandShoreCells = 1;
            cfg.riverConfluenceTerrainMaxHeightAboveWater01 = 0f;
            cfg.lakeRiverConnectorMaxPerMap = 1;
            cfg.uwpCarveEuclideanBellProfileEnabled = true;
            cfg.uwpCarveTransverseFlatRatio = 0.38f;
            cfg.uwpCarveTransverseBankPower = 1.8f;
            cfg.uwpCarveHalfWidthMulMain = 0.92f;
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
            cfg.riverTributaryRecoveryEnabled = false;
            cfg.riverLogPlacementFailureSummary = false;
        }

        /// <summary>Caps finales Play RTS: anulan boosts del runner UWP Editor (720/880 intentos).</summary>
        static void ApplyRtsPlayPerformanceCaps(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            cfg.maxTotalRiverBuildAttempts = Mathf.Clamp(cfg.maxTotalRiverBuildAttempts, 16, 48);
            cfg.riverPlacementMaxAttemptsPerRiver = Mathf.Clamp(cfg.riverPlacementMaxAttemptsPerRiver, 4, 8);
            cfg.riverTributaryRecoveryEnabled = false;
            cfg.riverTributaryRecoveryAttempts = Mathf.Min(cfg.riverTributaryRecoveryAttempts, 4);
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
            cfg.riverSurfaceSplineMaxAngleStepDeg = 22f;
            cfg.riverSurfaceSplineMaxDeviationCells = 2.85f;
            cfg.riverSurfaceSplineSampleSpacingCells = 0.44f;
            cfg.riverSurfaceSplineTension = 0.50f;
            cfg.riverSurfaceVisualSpacingCells = 0.68f;

            cfg.riverMainForceOrganicReshape = true;
            cfg.riverMainOrganicReshapeBudgetMs = 18;
            cfg.riverConfluenceTributaryEndWidthMul = 1f;
            cfg.riverSurfaceTributaryConfluenceApproachCells =
                Mathf.Max(cfg.riverSurfaceTributaryConfluenceApproachCells, 14);
        }
    }
}
