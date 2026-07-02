using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace Project.Gameplay.Map.CleanWaterPipeline
{
    /// <summary>
    /// Tuning de tributarios para Play RTS (Fase4). Idempotente: seguro llamar tras
    /// <see cref="Generation.RtsHydrologyProfile.Apply"/>.
    /// </summary>
    public static class CleanWaterHydrologyTuning
    {
        public static void Apply(MapGenConfig cfg)
        {
            if (cfg == null)
                return;

            cfg.riverRelaxedMissingTributaryFillPass = true;
            cfg.riverTributaryRouteBudgetMs = Mathf.Clamp(cfg.riverTributaryRouteBudgetMs, 120, 320);
            cfg.maxTotalRiverBuildAttempts = Mathf.Clamp(cfg.maxTotalRiverBuildAttempts, 16, 48);
            cfg.riverTributaryRouteMaxAttempts = Mathf.Clamp(cfg.riverTributaryRouteMaxAttempts, 4, 8);
            cfg.riverTributaryCandidatesPerSlot = Mathf.Clamp(cfg.riverTributaryCandidatesPerSlot, 4, 24);
            cfg.riverPlacementMaxAttemptsPerRiver = Mathf.Clamp(cfg.riverPlacementMaxAttemptsPerRiver, 4, 8);
            cfg.riverTributaryProceduralCandidatesPerSlot = Mathf.Clamp(cfg.riverTributaryProceduralCandidatesPerSlot, 4, 16);
            cfg.riverTributaryProceduralMaxSourceDistCells = Mathf.Clamp(cfg.riverTributaryProceduralMaxSourceDistCells, 24, 72);
            cfg.riverTributaryRecoveryEnabled = false;
            cfg.maxRetries = 1;

            cfg.lakeRiverConnectorMaxPerMap = Mathf.Max(cfg.lakeRiverConnectorMaxPerMap, 2);
            cfg.lakeRiverConnectorMaxDistanceCells = Mathf.Clamp(cfg.lakeRiverConnectorMaxDistanceCells, 32, 80);

            cfg.debugLogs = false;
            cfg.debugRiverHydrologyPerf = false;
            cfg.debugHydrologyNetwork = false;
            cfg.debugWaterGeneratePerfDiagnostics = false;
            cfg.debugRiverVisualStats = false;
        }
    }
}