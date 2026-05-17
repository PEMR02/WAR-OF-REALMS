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
                    config.macroMountainMassCount = m;
                    // Montaña más visible: más masa en heightmap01 y radios algo más compactos (picos más legibles).
                    config.macroMountainHeight01Min = Mathf.Max(config.macroMountainHeight01Min, 0.30f);
                    config.macroMountainHeight01Max = Mathf.Max(
                        config.macroMountainHeight01Max,
                        Mathf.Max(config.macroMountainHeight01Min + 0.08f, 0.56f));
                    config.macroMountainRadiusCellsMin = Mathf.Clamp(config.macroMountainRadiusCellsMin, 3, 12);
                    config.macroMountainRadiusCellsMax = Mathf.Clamp(config.macroMountainRadiusCellsMax, config.macroMountainRadiusCellsMin + 1, 18);
                }
            }

            if (applyLegacyRiverWidthScale)
            {
                // Tope de seguridad para que overrides antiguos no ensanchen de más el río jugable.
                float k = Mathf.Min(Mathf.Max(0.25f, legacyRiverWidthScale), 1.32f);
                config.riverWidthRadiusCells = Mathf.Clamp(Mathf.RoundToInt(config.riverWidthRadiusCells * k), 0, 6);
                config.riverVisualHalfWidthCells = Mathf.Clamp(config.riverVisualHalfWidthCells * k, 0.12f, 2f);
                config.riverVisualMeshHalfWidth = Mathf.Clamp(config.riverVisualMeshHalfWidth * k, 0.2f, 32f);
                legacyRiverWidthScaleAppliedToCompiledConfig = true;
            }

            // Alinear jugabilidad con lo visual: río más estrecho que el asset, pero legible en cámara RTS.
            // (0.34 + tope 2 dejaba cauces muy finos; además si redondea a 0 no hay expansión lateral del raster River.)
            int rwCells = Mathf.RoundToInt(config.riverWidthRadiusCells * 0.42f);
            config.riverWidthRadiusCells = Mathf.Clamp(Mathf.Max(1, rwCells), 1, 3);
            int raCells = Mathf.RoundToInt(config.riverWidthNoiseAmplitudeCells * 0.45f);
            config.riverWidthNoiseAmplitudeCells = Mathf.Clamp(Mathf.Max(1, raCells), 1, 2);

            // Río: tallada en heightmap un poco más visible (antes ~0.26×0.72 ≈ 19 % del valor compilado → “plano”).
            config.riverTerrainCarveDepthWorld = Mathf.Clamp(config.riverTerrainCarveDepthWorld * 0.34f, 0.04f, 0.8f);
            config.riverTerrainCarveFalloffCells = Mathf.Clamp(Mathf.RoundToInt(config.riverTerrainCarveFalloffCells * 0.38f), 1, 10);
            // Evita ríos "flotando" sobre el terreno: lift visual casi neutro.
            config.riverRibbonVerticalLiftWorld = Mathf.Clamp(config.riverRibbonVerticalLiftWorld + 0.02f, 0f, 2.5f);
            config.riverVisualBankInset = Mathf.Clamp(config.riverVisualBankInset + 0.24f, 0f, 3f);
            config.riverRibbonLateralJitterWorld = Mathf.Clamp(config.riverRibbonLateralJitterWorld * 0.55f, 0f, 1.4f);

            // (1) Orillas más caminables: cauce un poco menos hundido y transición más ancha (más polígonos NavMesh cerca del agua).
            config.riverTerrainCarveDepthWorld = Mathf.Clamp(config.riverTerrainCarveDepthWorld * 0.78f, 0.04f, 0.75f);
            config.riverTerrainCarveFalloffCells = Mathf.Clamp(Mathf.RoundToInt(config.riverTerrainCarveFalloffCells * 0.90f), 1, 12);
            config.riverFordDepthBelowWater01 = Mathf.Clamp(config.riverFordDepthBelowWater01 * 0.88f, 0.002f, 0.12f);

            // (2) Más vados y corredor mínimo amplio; terreno en vado menos tallado → mejor pegado al cruce.
            config.riverFordEveryCells = Mathf.Clamp(Mathf.RoundToInt(config.riverFordEveryCells * 0.52f), 8, 120);
            // Evita franjas laterales de vado demasiado anchas (parches azules diagonales).
            config.riverFordCorridorRadiusCells = 0;
            config.riverTerrainCarveFordMul = Mathf.Clamp(config.riverTerrainCarveFordMul * 1.18f, 0.08f, 1f);

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
        }
    }
}
