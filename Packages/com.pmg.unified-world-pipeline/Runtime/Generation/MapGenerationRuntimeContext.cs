using Project.Gameplay.Map;
using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace Project.Gameplay.Map.Generation
{
    /// <summary>
    /// Snapshot explícito de overrides runtime permitidos durante la compilación.
    /// Evita que RTSMapGenerator siga mutando el MapGenConfig ya compilado.
    /// </summary>
    public sealed class MapGenerationRuntimeContext
    {
        public bool applySceneHydrologyOverrides;
        public int sceneRiverCount;
        public int sceneLakeCount;
        public int sceneMaxLakeCells;

        public bool applyLobbyMacroRelief;
        public int lobbyMacroMountainMassCount;

        public bool applyLegacyRiverWidthScale = true;
        public float legacyRiverWidthScale = 1.32f;

        [Tooltip("Perfil UWP en Play: ancho/carve armónicos + tributarios. Desactivar para volver al runtime legacy.")]
        public bool applyUwpHydrologyProfile;

        [Tooltip("Ancho visual troncal en celdas (0 = default UWP 3.75). Solo si applyUwpHydrologyProfile.")]
        public float uwpMainRiverFullWidthCells;

        public bool uwpHydrologyProfileWasAppliedToCompiledConfig;
        public bool sceneHydrologyWasAppliedToMatch;
        public bool lobbyMacroWasAppliedToMatch;
        public bool legacyRiverWidthScaleAppliedToCompiledConfig;

        public static MapGenerationRuntimeContext CreateDefault() => new();

        public void ApplyToMatch(MatchConfig match)
        {
            if (match == null)
                return;

            if (applySceneHydrologyOverrides)
            {
                match.water.riverCount = Mathf.Clamp(sceneRiverCount, 0, 8);
                match.water.lakeCount = Mathf.Clamp(sceneLakeCount, 0, 12);
                match.water.maxLakeCells = Mathf.Max(50, sceneMaxLakeCells);
                sceneHydrologyWasAppliedToMatch = true;

                if (match.useHighLevelAlphaConfig)
                {
                    match.hydrology.riverCount = match.water.riverCount;
                    match.hydrology.lakeCount = match.water.lakeCount;
                    match.hydrology.riversEnabled = match.water.riverCount > 0;
                    match.hydrology.lakesEnabled = match.water.lakeCount > 0;
                }
            }

            if (applyLobbyMacroRelief && match.useHighLevelAlphaConfig)
            {
                int m = Mathf.Clamp(lobbyMacroMountainMassCount, 0, 12);
                match.terrainShape.mountainsEnabled = m > 0;
                match.terrainShape.mountainMassCount = m;
                lobbyMacroWasAppliedToMatch = true;
            }
        }

        public void ApplyToCompiledMapGen(MapGenConfig config)
        {
            if (config == null)
                return;

            if (applyLobbyMacroRelief)
            {
                int m = Mathf.Clamp(lobbyMacroMountainMassCount, 0, 12);
                // Solo sobrescribir si el lobby pide montañas > 0. Si el contador es 0,
                // se respeta el Match compilado.
                if (m > 0)
                {
                    config.macroTerrainEnabled = true;
                    // Picos suaves (no cordillera); el high-ground RTS son las mesetas.
                    config.macroMountainMassCount = Mathf.Clamp(Mathf.Min(m, 2), 1, 2);
                    config.macroMountainHeight01Min = Mathf.Clamp(
                        Mathf.Max(config.macroMountainHeight01Min, 0.14f), 0.10f, 0.22f);
                    config.macroMountainHeight01Max = Mathf.Clamp(
                        Mathf.Max(config.macroMountainHeight01Max, 0.22f), 0.18f, 0.32f);
                    config.macroMountainRadiusCellsMin = Mathf.Clamp(config.macroMountainRadiusCellsMin, 4, 12);
                    config.macroMountainRadiusCellsMax = Mathf.Clamp(
                        config.macroMountainRadiusCellsMax,
                        config.macroMountainRadiusCellsMin + 3,
                        18);

                    // Mesetas: sobresalen del height01 ya generado (suma local; no regenera noise).
                    config.macroPlateauCount = Mathf.Max(config.macroPlateauCount, m >= 4 ? 3 : 2);
                    config.macroPlateauHeight01Min = Mathf.Max(config.macroPlateauHeight01Min, 0.40f);
                    config.macroPlateauHeight01Max = Mathf.Max(
                        config.macroPlateauHeight01Max,
                        Mathf.Max(config.macroPlateauHeight01Min + 0.10f, 0.58f));
                    config.macroPlateauRadiusCellsMin = Mathf.Clamp(config.macroPlateauRadiusCellsMin, 12, 22);
                    config.macroPlateauRadiusCellsMax = Mathf.Clamp(
                        config.macroPlateauRadiusCellsMax,
                        config.macroPlateauRadiusCellsMin + 4,
                        36);
                    // Rim corto = escarpe usable; rampas siguen en MacroTerrainSculptor.
                    config.macroPlateauRimCells = Mathf.Clamp(config.macroPlateauRimCells, 4, 7);

                    Debug.Log(
                        $"[LobbyMacroRelief] enabled masses={config.macroMountainMassCount} " +
                        $"plateaus={config.macroPlateauCount} " +
                        $"platH=[{config.macroPlateauHeight01Min:F2},{config.macroPlateauHeight01Max:F2}] " +
                        $"mtnH=[{config.macroMountainHeight01Min:F2},{config.macroMountainHeight01Max:F2}] " +
                        $"terrainY={config.terrainHeightWorld:F1}");
                }
            }

            if (applyUwpHydrologyProfile)
            {
                RtsHydrologyProfile.Apply(config, uwpMainRiverFullWidthCells);
                uwpHydrologyProfileWasAppliedToCompiledConfig = true;
            }

            bool skipLegacyRiverCompression = config.uwpOwnedVisualPolicy || applyUwpHydrologyProfile;

            if (applyLegacyRiverWidthScale && !skipLegacyRiverCompression)
            {
                // Tope de seguridad para que overrides antiguos no ensanchen de más el río jugable.
                float k = Mathf.Min(Mathf.Max(0.25f, legacyRiverWidthScale), 1.32f);
                config.riverWidthRadiusCells = Mathf.Clamp(Mathf.RoundToInt(config.riverWidthRadiusCells * k), 0, 6);
                config.riverVisualHalfWidthCells = Mathf.Clamp(config.riverVisualHalfWidthCells * k, 0.12f, 2f);
                config.riverVisualMeshHalfWidth = Mathf.Clamp(config.riverVisualMeshHalfWidth * k, 0.2f, 32f);
                legacyRiverWidthScaleAppliedToCompiledConfig = true;
            }

            if (!skipLegacyRiverCompression)
            {
                // Alinear jugabilidad con lo visual: río más estrecho que el asset, pero legible en cámara RTS.
                int rwCells = Mathf.RoundToInt(config.riverWidthRadiusCells * 0.42f);
                config.riverWidthRadiusCells = Mathf.Clamp(Mathf.Max(1, rwCells), 1, 3);
                int raCells = Mathf.RoundToInt(config.riverWidthNoiseAmplitudeCells * 0.45f);
                config.riverWidthNoiseAmplitudeCells = Mathf.Clamp(Mathf.Max(1, raCells), 1, 2);

                config.riverTerrainCarveDepthWorld = Mathf.Clamp(config.riverTerrainCarveDepthWorld * 0.34f, 0.04f, 0.8f);
                config.riverTerrainCarveFalloffCells = Mathf.Clamp(Mathf.RoundToInt(config.riverTerrainCarveFalloffCells * 0.38f), 1, 10);
                config.riverRibbonVerticalLiftWorld = Mathf.Clamp(config.riverRibbonVerticalLiftWorld + 0.02f, 0f, 2.5f);
                config.riverVisualBankInset = Mathf.Clamp(config.riverVisualBankInset + 0.24f, 0f, 3f);
                config.riverRibbonLateralJitterWorld = Mathf.Clamp(config.riverRibbonLateralJitterWorld * 0.55f, 0f, 1.4f);

                config.riverTerrainCarveDepthWorld = Mathf.Clamp(config.riverTerrainCarveDepthWorld * 0.78f, 0.04f, 0.75f);
                config.riverTerrainCarveFalloffCells = Mathf.Clamp(Mathf.RoundToInt(config.riverTerrainCarveFalloffCells * 0.90f), 1, 12);
                config.riverFordDepthBelowWater01 = Mathf.Clamp(config.riverFordDepthBelowWater01 * 0.88f, 0.002f, 0.12f);

                config.riverFordEveryCells = Mathf.Clamp(Mathf.RoundToInt(config.riverFordEveryCells * 0.52f), 8, 120);
                config.riverFordCorridorRadiusCells = 0;
                config.riverTerrainCarveFordMul = Mathf.Clamp(config.riverTerrainCarveFordMul * 1.18f, 0.08f, 1f);
            }

            // Rendimiento vs calidad visual del agua (marching squares).
            config.waterEdgeSubdiv = Mathf.Clamp(config.waterEdgeSubdiv, 1, 3);
            config.waterMsMaxCornerSamples = Mathf.Clamp(config.waterMsMaxCornerSamples, 80000, 2_500_000);
            // El runtime no debe desactivar MS por plantillas antiguas: sin esto el agua vuelve a quads por celda.
            config.waterRoundedEdges = true;

            // ── Preservar canales de río legibles (estilo RTS/AoE2) ──────────────────────────────
            // Los 4 stages siguientes se apilan y convierten ríos en masas/lagos/blobs.
            // Se limitan agresivamente en runtime para mantener canales estrechos y direccionales.

            // 1. CA pre-MS: expande bordes +2 celdas antes de que el MS haga su propio blur.
            //    Con threshold=5 y 2 pasadas, ríos de 2 celdas se vuelven masas de 6+.
            //    → 0 pasadas: el MS usa el grid raw y aplica su propio suavizado.
            config.waterMaskSmoothIterations = 0;

            // 2. Box blur del campo escalar MS: radius 2 × 3 pasadas fusiona ríos próximos.
            //    → Máx 1 pasada, radio 1: suaviza solo esquinas 90°, preserva forma.
            config.waterEdgeBlurIterations = Mathf.Min(config.waterEdgeBlurIterations, 1);
            config.waterEdgeBlurRadius = 1;

            // 3. Smoothness extra (blur adicional post-campo): 0.7 añade ~1 pasada más.
            //    → 0.1: casi nulo; solo suaviza esquinas muy duras.
            config.waterEdgeSmoothness = Mathf.Min(config.waterEdgeSmoothness, 0.1f);

            // 4. Shore visual width controla el gradiente de profundidad UV.
            //    Con 7 celdas, un río de 2 celdas parece un lago de 14.
            //    → 2.5 celdas: el gradiente queda dentro del cauce.
            config.shoreVisualWidth = Mathf.Min(config.shoreVisualWidth, 2.5f);

            // 5. River fusion blur: 3 pasadas mezcla tributarios en una sola masa.
            //    → 1 pasada: cada río mantiene su silueta independiente.
            config.riverFusionBlurPasses = Mathf.Min(config.riverFusionBlurPasses, 1);

            // SplitLakeMs: el MS es solo lago (ríos van por ribbon). Restaurar calidad de contorno
            // que el clamp anti-blob de río había aplastado (lagos en escalera + foam dentada).
            if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
            {
                config.waterEdgeSubdiv = Mathf.Max(config.waterEdgeSubdiv, 5);
                config.waterEdgeBlurIterations = Mathf.Max(config.waterEdgeBlurIterations, 4);
                config.waterEdgeBlurRadius = Mathf.Max(config.waterEdgeBlurRadius, 2);
                config.waterEdgeSmoothness = Mathf.Max(config.waterEdgeSmoothness, 1.0f);
                config.waterMaskSmoothIterations = Mathf.Max(config.waterMaskSmoothIterations, 2);
                if (config.lakeShoreMsNoiseAmplitude < 0.05f)
                    config.lakeShoreMsNoiseAmplitude = 0.06f;
            }
        }
    }
}
