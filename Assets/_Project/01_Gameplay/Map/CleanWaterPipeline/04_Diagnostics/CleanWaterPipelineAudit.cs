using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace Project.Gameplay.Map.CleanWaterPipeline
{
    /// <summary>Logs compactos para validar el pipeline limpio en Play.</summary>
    public static class CleanWaterPipelineAudit
    {
        public static void LogPostCompile(MapGenConfig cfg)
        {
            if (cfg == null)
                return;
            Debug.LogWarning(
                $"[CleanWaterPipeline] compile uwpOwned={cfg.uwpOwnedVisualPolicy} " +
                $"rivers={cfg.riverCount} lakes={cfg.lakeCount} fillPass={cfg.riverRelaxedMissingTributaryFillPass} " +
                $"maxAttempts={cfg.maxTotalRiverBuildAttempts} tribBudgetMs={cfg.riverTributaryRouteBudgetMs} " +
                $"debugNet={cfg.debugHydrologyNetwork} recovery={cfg.riverTributaryRecoveryEnabled} " +
                $"pipeline={cfg.waterVisualPipeline} seed={cfg.seed}");
        }

        public static void LogPostGenerate(GridSystem grid, MapGenConfig cfg)
        {
            if (grid == null || cfg == null)
                return;

            int riverCells = 0;
            int lakeCells = 0;
            for (int x = 0; x < grid.Width; x++)
            {
                for (int z = 0; z < grid.Height; z++)
                {
                    var t = grid.GetCell(x, z).type;
                    if (t == CellType.River) riverCells++;
                    else if (t == CellType.Water) lakeCells++;
                }
            }

            int surfaces = grid.RiverVisualSurfaces?.Count ?? 0;
            int centerlines = grid.RiverCenterlinesCellSpace?.Count ?? 0;
            int meshBuilt = 0;
            int meshSkipped = 0;
            if (grid.RiverVisualSurfaces != null)
            {
                for (int i = 0; i < grid.RiverVisualSurfaces.Count; i++)
                {
                    if (grid.RiverVisualSurfaces[i].Skipped)
                        meshSkipped++;
                    else
                        meshBuilt++;
                }
            }

            Debug.Log(
                $"[CleanWaterPipeline] generate riverCells={riverCells} lakeCells={lakeCells} " +
                $"centerlines={centerlines} visualSurfaces={surfaces} meshBuilt={meshBuilt} meshSkipped={meshSkipped} " +
                $"frozen={(grid.RiverVisualSurfaceCacheFrozen ? 1 : 0)} seed={cfg.seed}");
        }
    }
}
