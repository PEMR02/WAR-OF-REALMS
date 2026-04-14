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
        public float legacyRiverWidthScale = 1.5f;

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
                    config.macroMountainHeight01Min = Mathf.Max(config.macroMountainHeight01Min, 0.12f);
                    config.macroMountainHeight01Max = Mathf.Max(
                        config.macroMountainHeight01Max,
                        Mathf.Max(config.macroMountainHeight01Min + 0.04f, 0.28f));
                }
            }

            if (applyLegacyRiverWidthScale)
            {
                float k = Mathf.Max(0.25f, legacyRiverWidthScale);
                config.riverWidthRadiusCells = Mathf.Clamp(Mathf.RoundToInt(config.riverWidthRadiusCells * k), 0, 6);
                config.riverVisualHalfWidthCells = Mathf.Clamp(config.riverVisualHalfWidthCells * k, 0.12f, 2f);
                config.riverVisualMeshHalfWidth = Mathf.Clamp(config.riverVisualMeshHalfWidth * k, 0.2f, 32f);
                legacyRiverWidthScaleAppliedToCompiledConfig = true;
            }
        }
    }
}
