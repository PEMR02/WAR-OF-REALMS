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

            // Alinear jugabilidad con lo visual: río lógico más estrecho para reducir "espacio muerto" al construir/caminar.
            config.riverWidthRadiusCells = Mathf.Clamp(Mathf.RoundToInt(config.riverWidthRadiusCells * 0.34f), 0, 1);
            config.riverWidthNoiseAmplitudeCells = Mathf.Clamp(Mathf.RoundToInt(config.riverWidthNoiseAmplitudeCells * 0.35f), 0, 1);

            // Río: mantener ancho visual, pero acercar navegación/edificación al borde.
            config.riverTerrainCarveDepthWorld = Mathf.Clamp(config.riverTerrainCarveDepthWorld * 0.26f, 0.04f, 0.8f);
            config.riverTerrainCarveFalloffCells = Mathf.Clamp(Mathf.RoundToInt(config.riverTerrainCarveFalloffCells * 0.30f), 1, 10);
            // Evita ríos "flotando" sobre el terreno: lift visual casi neutro.
            config.riverRibbonVerticalLiftWorld = Mathf.Clamp(config.riverRibbonVerticalLiftWorld + 0.02f, 0f, 2.5f);
            config.riverVisualBankInset = Mathf.Clamp(config.riverVisualBankInset + 0.24f, 0f, 3f);
            config.riverRibbonLateralJitterWorld = Mathf.Clamp(config.riverRibbonLateralJitterWorld * 0.55f, 0f, 1.4f);

            // (1) Orillas más caminables: cauce un poco menos hundido y transición más ancha (más polígonos NavMesh cerca del agua).
            config.riverTerrainCarveDepthWorld = Mathf.Clamp(config.riverTerrainCarveDepthWorld * 0.72f, 0.04f, 0.75f);
            config.riverTerrainCarveFalloffCells = Mathf.Clamp(Mathf.RoundToInt(config.riverTerrainCarveFalloffCells * 0.84f), 1, 12);
            config.riverFordDepthBelowWater01 = Mathf.Clamp(config.riverFordDepthBelowWater01 * 0.88f, 0.002f, 0.12f);

            // (2) Más vados y corredor mínimo amplio; terreno en vado menos tallado → mejor pegado al cruce.
            config.riverFordEveryCells = Mathf.Clamp(Mathf.RoundToInt(config.riverFordEveryCells * 0.52f), 8, 120);
            // Evita franjas laterales de vado demasiado anchas (parches azules diagonales).
            config.riverFordCorridorRadiusCells = 0;
            config.riverTerrainCarveFordMul = Mathf.Clamp(config.riverTerrainCarveFordMul * 1.18f, 0.08f, 1f);

            // Rendimiento agua: conservar forma orgánica pero con menor densidad de triangulación.
            config.waterEdgeSubdiv = Mathf.Clamp(config.waterEdgeSubdiv, 1, 2);
            config.waterMaskSmoothIterations = Mathf.Clamp(config.waterMaskSmoothIterations, 0, 1);
            config.waterMsMaxCornerSamples = Mathf.Clamp(config.waterMsMaxCornerSamples, 60000, 140000);
        }
    }
}
