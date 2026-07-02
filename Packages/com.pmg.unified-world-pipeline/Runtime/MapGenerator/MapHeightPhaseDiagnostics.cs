using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Logs [HeightPhase]: etiquetas de fase (ej.: after_base_noise_heights, after_water_hydro, after_macro_smooth_normalize).</summary>
    public static class MapHeightPhaseDiagnostics
    {
        public static void TryLogHeightPhase(GridSystem grid, MapGenConfig config, string phase)
        {
            if (grid == null || config == null)
                return;
            if (!config.debugLogs && !config.debugHydrologyNetwork && !config.debugRiverHydrologyPerf)
                return;

            int w = grid.Width;
            int h = grid.Height;
            int nLand = 0, nRiver = 0, nWater = 0, nMountain = 0;
            float minL = 2f, maxL = -1f;
            double sumL = 0.0;
            double slopeSum = 0.0;
            int slopeN = 0;

            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    ref var c = ref grid.GetCell(x, z);
                    switch (c.type)
                    {
                        case CellType.Land:
                            nLand++;
                            float hl = c.height01;
                            if (hl < minL) minL = hl;
                            if (hl > maxL) maxL = hl;
                            sumL += hl;
                            float bestD = 0f;
                            foreach (var n in grid.Neighbors4(new Vector2Int(x, z)))
                            {
                                if (!grid.InBoundsCell(n.x, n.y))
                                    continue;
                                ref var nc = ref grid.GetCell(n.x, n.y);
                                if (nc.type != CellType.Land)
                                    continue;
                                bestD = Mathf.Max(bestD, Mathf.Abs(nc.height01 - hl));
                            }

                            slopeSum += bestD;
                            slopeN++;
                            break;
                        case CellType.River:
                            nRiver++;
                            break;
                        case CellType.Water:
                            nWater++;
                            break;
                        case CellType.Mountain:
                            nMountain++;
                            break;
                    }
                }
            }

            float avgL = nLand > 0 ? (float)(sumL / nLand) : 0f;
            float avgSlope = slopeN > 0 ? (float)(slopeSum / slopeN) : 0f;
            string mm = nLand > 0 ? $"{minL:F4}/{maxL:F4}" : "na/na";
            Debug.Log(
                $"[HeightPhase] phase={phase} land={nLand} river={nRiver} water={nWater} mtn={nMountain} " +
                $"landMinMax={mm} landAvg={avgL:F4} avgNeighborSlope01={avgSlope:F5}");
        }
    }
}
