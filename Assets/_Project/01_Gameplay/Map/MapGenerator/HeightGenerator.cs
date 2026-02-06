using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 4: genera height01 coherente con regiones; agua plana a waterHeight01; calcula slopeDeg.</summary>
    public static class HeightGenerator
    {
        /// <summary>Parámetros: regionId/biomeId (ya en grid), config.waterHeight01. Escribe height01 y slopeDeg.</summary>
        public static void GenerateHeights(GridSystem grid, MapGenConfig config, IRng rng)
        {
            if (grid == null || config == null || rng == null) return;

            int w = grid.Width;
            int h = grid.Height;
            float waterH = config.waterHeight01;
            float scale = config.regionNoiseScale;
            int seedOff = rng.NextInt(0, 50000);

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    ref var cell = ref grid.GetCell(x, z);
                    if (cell.type == CellType.Water || cell.type == CellType.River)
                    {
                        cell.height01 = waterH;
                        cell.slopeDeg = 0f;
                        continue;
                    }
                    float nx = (x + seedOff) * scale;
                    float nz = (z + seedOff * 2) * scale;
                    float noise = Mathf.PerlinNoise(nx, nz);
                    cell.height01 = Mathf.Clamp01(0.3f + 0.5f * noise + 0.1f * (cell.regionId % 5) / 5f);
                    cell.slopeDeg = 0f; // se recalcula abajo
                }
            }

            // Slope aproximado desde vecinos 4
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    ref var cell = ref grid.GetCell(x, z);
                    if (cell.type == CellType.Water || cell.type == CellType.River) continue;
                    float hc = cell.height01;
                    float maxDiff = 0f;
                    foreach (var n in grid.Neighbors4(x, z))
                    {
                        float hn = grid.GetCell(n.x, n.y).height01;
                        maxDiff = Mathf.Max(maxDiff, Mathf.Abs(hn - hc));
                    }
                    cell.slopeDeg = Mathf.Clamp(maxDiff * 90f, 0f, 90f);
                }
            }

            if (config.debugLogs)
                Debug.Log($"Fase4 Heights: listo. Agua plana a {waterH:F2}. Slope calculado.");
        }
    }
}
