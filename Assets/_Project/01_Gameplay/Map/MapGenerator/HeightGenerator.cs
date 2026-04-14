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
            float baseScale = Mathf.Max(0.0008f, config.regionNoiseScale);
            int seedOff = rng.NextInt(0, 50000);
            int edgeSuppressionMargin = Mathf.Clamp(config.macroMountainSpawnAvoidanceMarginCells, 4, Mathf.Max(4, Mathf.Min(w, h) / 2));
            int floodplainCells = Mathf.Max(config.cityWaterBufferCells + 2, 5);

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    ref var cell = ref grid.GetCell(x, z);
                    if (cell.type == CellType.Water)
                    {
                        cell.height01 = waterH;
                        cell.slopeDeg = 0f;
                        continue;
                    }
                    if (cell.type == CellType.River)
                    {
                        float depth = cell.riverFord
                            ? Mathf.Clamp(config.riverFordDepthBelowWater01, 0.002f, 0.12f)
                            : Mathf.Clamp(config.riverBedDepthBelowWater01, 0.004f, 0.18f);
                        cell.height01 = Mathf.Clamp01(waterH - depth);
                        cell.slopeDeg = 0f;
                        continue;
                    }

                    float nx = (x + seedOff) * baseScale;
                    float nz = (z + seedOff * 2) * baseScale;
                    float macroNoise = FractalNoise(nx * 0.52f, nz * 0.52f, 3, 0.55f, 2.05f);
                    float detailNoise = FractalNoise(nx * 1.45f + 11.3f, nz * 1.45f + 7.7f, 2, 0.45f, 2.2f);
                    float ridged = RidgedNoise(nx * 0.88f + 23.1f, nz * 0.88f + 17.4f);
                    float waterDist01 = EvaluateWaterDistanceFactor(grid, x, z, floodplainCells);
                    float edge01 = EvaluateEdgeFactor(x, z, w, h, edgeSuppressionMargin);
                    float regionBias = Mathf.Clamp01((cell.regionId % 7) / 6f);

                    // Base RTS: llanuras amplias cerca de agua/spawn edge, laderas suaves y cumbres
                    // solo donde macro+ridged tienen permiso suficiente.
                    float plainBase = Mathf.Lerp(waterH + 0.035f, 0.34f, macroNoise * 0.55f + detailNoise * 0.45f);
                    float hillMask = Mathf.Clamp01(
                        macroNoise * 0.75f +
                        ridged * Mathf.Lerp(0.18f, 0.42f, config.macroHillDensity) +
                        regionBias * 0.18f);
                    hillMask *= Mathf.Lerp(0.42f, 1f, waterDist01);
                    hillMask *= Mathf.Lerp(0.46f, 1f, edge01);

                    float slopeBand = Mathf.Lerp(0.12f, 0.28f, hillMask);
                    float summitBand = Mathf.Lerp(0.04f, 0.18f, ridged * waterDist01);
                    float baseH = plainBase + slopeBand + summitBand;

                    if (config.macroTerrainEnabled)
                    {
                        float macroHillMul = Mathf.Lerp(0.9f, 1.12f, config.macroHillDensity);
                        float roughness = Mathf.Lerp(0.92f, 1.12f, config.macroRoughnessWeight * detailNoise);
                        baseH = waterH + (baseH - waterH) * macroHillMul * roughness;
                    }

                    // Cerca de agua y bordes de spawn potenciales, capamos el relieve agresivo
                    // para dejar mejores mesetas jugables antes del macro sculpting.
                    float spawnFriendlyCap = Mathf.Lerp(0.48f, 1f, edge01 * waterDist01);
                    float capHeight = Mathf.Lerp(waterH + 0.08f, 0.78f, spawnFriendlyCap);
                    cell.height01 = Mathf.Clamp01(Mathf.Min(baseH, capHeight));
                    cell.slopeDeg = 0f;
                }
            }

            RecalculateLandSlopes(grid, config);

            if (config.debugLogs)
                Debug.Log($"Fase4 Heights: listo. Agua plana a {waterH:F2}. Slope calculado.");
        }

        /// <summary>Recalcula pendiente en tierra (no agua/río). Tras <see cref="MacroTerrainSculptor"/>.</summary>
        public static void RecalculateLandSlopes(GridSystem grid, MapGenConfig config)
        {
            if (grid == null) return;
            int w = grid.Width;
            int h = grid.Height;
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
        }

        /// <summary>
        /// Tras el macro relief, recorta picos cerca de márgenes jugables y agua para proteger
        /// zonas candidatas de spawn/ciudad sin eliminar el relieve central del mapa.
        /// </summary>
        public static void ApplySpawnFriendlyPeakSuppression(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null)
                return;

            int w = grid.Width;
            int h = grid.Height;
            int margin = Mathf.Clamp(config.macroMountainSpawnAvoidanceMarginCells, 4, Mathf.Max(4, Mathf.Min(w, h) / 2));
            int floodplainCells = Mathf.Max(config.cityWaterBufferCells + 2, 5);
            float waterH = config.waterHeight01;

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    ref var cell = ref grid.GetCell(x, z);
                    if (cell.type != CellType.Land)
                        continue;

                    float edge01 = EvaluateEdgeFactor(x, z, w, h, margin);
                    float water01 = EvaluateWaterDistanceFactor(grid, x, z, floodplainCells);
                    float allow01 = edge01 * water01;
                    float localCap = Mathf.Lerp(waterH + 0.09f, 0.84f, allow01);
                    if (cell.height01 <= localCap)
                        continue;

                    cell.height01 = Mathf.Lerp(localCap, cell.height01, allow01 * allow01);
                }
            }
        }

        static float EvaluateWaterDistanceFactor(GridSystem grid, int x, int z, int floodplainCells)
        {
            if (grid?.DistanceToWaterCells == null)
                return 1f;

            int dist = grid.DistanceToWaterCells[x, z];
            if (dist >= WaterDistanceField.UnreachableDistance)
                return 1f;

            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((dist - 1f) / Mathf.Max(1f, floodplainCells)));
        }

        static float EvaluateEdgeFactor(int x, int z, int w, int h, int margin)
        {
            int edgeDist = Mathf.Min(Mathf.Min(x, z), Mathf.Min(w - 1 - x, h - 1 - z));
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((edgeDist - margin * 0.25f) / Mathf.Max(1f, margin * 0.75f)));
        }

        static float FractalNoise(float x, float z, int octaves, float persistence, float lacunarity)
        {
            float sum = 0f;
            float amp = 1f;
            float freq = 1f;
            float norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x * freq, z * freq) * amp;
                norm += amp;
                amp *= persistence;
                freq *= lacunarity;
            }

            return norm > 1e-5f ? sum / norm : 0f;
        }

        static float RidgedNoise(float x, float z)
        {
            float n = Mathf.PerlinNoise(x, z);
            return 1f - Mathf.Abs(n * 2f - 1f);
        }
    }
}
