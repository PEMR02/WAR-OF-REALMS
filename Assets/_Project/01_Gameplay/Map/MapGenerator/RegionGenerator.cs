using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 2: genera macro-regiones (regionId, biomeId) por celda. Ruido determinista.</summary>
    public static class RegionGenerator
    {
        /// <summary>Parámetros: config.regionCount, config.regionNoiseScale. Escribe regionId y biomeId en cada celda.</summary>
        public static void GenerateRegions(GridSystem grid, MapGenConfig config, IRng rng)
        {
            if (grid == null || config == null || rng == null) return;

            int w = grid.Width;
            int h = grid.Height;
            float scale = config.regionNoiseScale;
            int regionCount = Mathf.Max(1, config.regionCount);
            int seedOffset = rng.NextInt(0, 100000);

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    float nx = (x + seedOffset) * scale;
                    float nz = (z + seedOffset * 2) * scale;
                    float noise = Mathf.PerlinNoise(nx, nz);
                    int regionId = Mathf.Clamp(Mathf.FloorToInt(noise * regionCount), 0, regionCount - 1);
                    int biomeId = regionId % 3; // stub: 3 biomas por región

                    ref var cell = ref grid.GetCell(x, z);
                    cell.regionId = regionId;
                    cell.biomeId = biomeId;
                }
            }

            if (config.debugLogs)
                Debug.Log($"Fase2 Regiones: {regionCount} regiones, biomas por región. Listo.");
        }
    }
}
