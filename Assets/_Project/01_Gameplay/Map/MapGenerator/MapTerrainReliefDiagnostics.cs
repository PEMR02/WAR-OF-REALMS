using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Diagnostico temporal [TerrainRelief]: relieve local en Land (sin tocar ríos ni mallas).
    /// Activa con los mismos flags que MapHeightPhaseDiagnostics.
    /// </summary>
    public static class MapTerrainReliefDiagnostics
    {
        const float PlateauNeighborDeltaEps = 0.006f;

        /// <summary>
        /// localSlope*: max |dh| vs vecinos Land en 4-vecindad; heightStdDev sobre Land;
        /// basinCandidates: minimos locales estrictos (4 vecinos Land, todos mas altos);
        /// plateauRatio: fraccion de celdas Land (con al menos 2 vecinos Land) con max|dh| &lt; eps.
        /// </summary>
        public static void TryLogTerrainRelief(GridSystem grid, MapGenConfig config, string phase)
        {
            if (grid == null || config == null)
                return;
            if (!config.debugLogs && !config.debugHydrologyNetwork && !config.debugRiverHydrologyPerf)
                return;

            int w = grid.Width;
            int h = grid.Height;
            double sumH = 0.0;
            double sumH2 = 0.0;
            int nLand = 0;
            double sumSlope = 0.0;
            int slopeN = 0;
            float slopeMax = 0f;
            int basinCandidates = 0;
            int plateauCells = 0;
            int plateauDenom = 0;

            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    ref var c = ref grid.GetCell(x, z);
                    if (c.type != CellType.Land)
                        continue;

                    nLand++;
                    float hl = c.height01;
                    sumH += hl;
                    sumH2 += hl * hl;

                    float bestD = 0f;
                    int landNeigh = 0;
                    bool strictMin = true;
                    foreach (var n in grid.Neighbors4(new Vector2Int(x, z)))
                    {
                        if (!grid.InBoundsCell(n.x, n.y))
                            continue;
                        ref var nc = ref grid.GetCell(n.x, n.y);
                        if (nc.type != CellType.Land)
                            continue;
                        landNeigh++;
                        float dh = Mathf.Abs(nc.height01 - hl);
                        if (dh > bestD)
                            bestD = dh;
                        if (nc.height01 <= hl)
                            strictMin = false;
                    }

                    if (landNeigh > 0)
                    {
                        sumSlope += bestD;
                        slopeN++;
                        if (bestD > slopeMax)
                            slopeMax = bestD;
                    }

                    if (landNeigh >= 2)
                    {
                        plateauDenom++;
                        if (bestD < PlateauNeighborDeltaEps)
                            plateauCells++;
                    }

                    if (landNeigh == 4 && strictMin)
                        basinCandidates++;
                }
            }

            float avgSlope = slopeN > 0 ? (float)(sumSlope / slopeN) : 0f;
            float mean = nLand > 0 ? (float)(sumH / nLand) : 0f;
            float variance = nLand > 0 ? (float)(sumH2 / nLand) - mean * mean : 0f;
            float stdDev = variance > 1e-12f ? Mathf.Sqrt(Mathf.Max(0f, variance)) : 0f;
            float plateauRatio = plateauDenom > 0 ? plateauCells / (float)plateauDenom : 0f;

            Debug.Log(
                $"[TerrainRelief] phase={phase} localSlopeAvg={avgSlope:F5} localSlopeMax={slopeMax:F5} " +
                $"heightStdDev={stdDev:F5} basinCandidates={basinCandidates} plateauRatio={plateauRatio:F3} " +
                $"plateauEps={PlateauNeighborDeltaEps:F4} landCells={nLand}");
        }
    }
}
