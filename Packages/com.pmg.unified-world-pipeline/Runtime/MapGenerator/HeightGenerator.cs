using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Alturas lógicas: paso base (ruido + relieve jugable en tierra) antes de hidrología;
    /// superficie de cauce/lago tras tipar celdas; refinamiento post-cuenca (supresión de picos + slopes).
    /// </summary>
    public static class HeightGenerator
    {
        // Micro-relieve e hidrología base (pre-agua): amplitudes conservadoras; ver validación [TerrainRelief].
        const float HydroMicroReliefFreqMul = 5f;
        const float HydroMicroReliefAmp = 0.07f;
        const float HillMaskEdgeFloor = 0.72f;

        const int FallbackBasinCountMin = 2;
        const int FallbackBasinCountMaxExclusive = 5;
        const int FallbackBasinRadiusMin = 9;
        const int FallbackBasinRadiusMaxExclusive = 21;
        const float FallbackBasinDepthMin = 0.018f;
        const float FallbackBasinDepthMax = 0.038f;

        /// <summary>
        /// Pre-hidrología: solo celdas no acuáticas. Ruido fractal, valles/mesetas respecto a waterHeight01 de config
        /// (sin celdas Water/River aún; <see cref="GridSystem.DistanceToWaterCells"/> suele ser null ⇒ factor llanura neutro).
        /// No aplica aplanado de ciudades/caminos ni carve (Fase posterior en <see cref="TerrainCarver"/>).
        /// </summary>
        public static void GenerateBaseTerrainHeights(GridSystem grid, MapGenConfig config, IRng rng)
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
                    if (cell.type == CellType.Water || cell.type == CellType.River)
                        continue;

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
                    hillMask *= Mathf.Lerp(HillMaskEdgeFloor, 1f, edge01);

                    float slopeBand = Mathf.Lerp(0.12f, 0.28f, hillMask);
                    float summitBand = Mathf.Lerp(0.04f, 0.18f, ridged * waterDist01);
                    float baseH = plainBase + slopeBand + summitBand;

                    if (config.macroTerrainEnabled)
                    {
                        float macroHillMul = Mathf.Lerp(0.9f, 1.12f, config.macroHillDensity);
                        float roughness = Mathf.Lerp(0.92f, 1.12f, config.macroRoughnessWeight * detailNoise);
                        baseH = waterH + (baseH - waterH) * macroHillMul * roughness;
                    }

                    float hydroDetail =
                        Mathf.PerlinNoise(nx * HydroMicroReliefFreqMul + 19.71f, nz * HydroMicroReliefFreqMul + 13.27f) - 0.5f;
                    baseH += hydroDetail * HydroMicroReliefAmp;

                    float contX01 = w > 1 ? x / (float)(w - 1) : 0f;
                    float contZ01 = h > 1 ? z / (float)(h - 1) : 0f;
                    float continentalSlope = ((1f - contX01) + (1f - contZ01)) * 0.06f;
                    baseH += continentalSlope;

                    // Cerca de agua y bordes de spawn potenciales, capamos el relieve agresivo
                    // para dejar mejores mesetas jugables antes del macro sculpting.
                    float spawnFriendlyCap = Mathf.Lerp(0.48f, 1f, edge01 * waterDist01);
                    float capHeight = Mathf.Lerp(waterH + 0.08f, 0.78f, spawnFriendlyCap);
                    cell.height01 = Mathf.Clamp01(Mathf.Min(baseH, capHeight));
                    cell.slopeDeg = 0f;
                }
            }

            if (!config.macroTerrainEnabled || config.macroBasinCount <= 0)
                ApplyFallbackHydrologyBasins(grid, config, rng);

            RecalculateLandSlopes(grid, config);

            if (config.debugLogs)
                Debug.Log($"Base terrain heights: listo (pre-hidrología). Referencia agua config={waterH:F2}. Slopes Land.");
        }

        /// <summary>
        /// Tras colocar ríos/lagos: ajusta height01 solo en <see cref="CellType.Water"/> y <see cref="CellType.River"/>.
        /// No modifica alturas de tierra (Land/Mountain).
        /// </summary>
        public static void ApplyHydrologySurfaceHeights(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null) return;

            int w = grid.Width;
            int h = grid.Height;
            float waterH = config.waterHeight01;

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    ref var cell = ref grid.GetCell(x, z);
                    if (cell.type == CellType.Water)
                    {
                        float shoreDist = grid.WaterShoreDistanceCells != null
                            ? Mathf.Max(0f, grid.WaterShoreDistanceCells[x, z])
                            : 1f;
                        float ramp = Mathf.Max(1f, config.lakeBedDepthRampCells);
                        float deep01 = Mathf.Clamp01(shoreDist / ramp);
                        deep01 = deep01 * deep01 * (3f - 2f * deep01);
                        float minDepth = Mathf.Clamp(config.lakeBedMinDepthBelowWater01, 0f, 0.05f);
                        float maxDepth = Mathf.Clamp(config.lakeBedDepthBelowWater01, minDepth, 0.16f);
                        float depth = Mathf.Lerp(minDepth, maxDepth, deep01);
                        cell.height01 = Mathf.Clamp01(waterH - depth);
                        cell.slopeDeg = 0f;
                    }
                    else if (cell.type == CellType.River)
                    {
                        float depth = ResolveRiverBedDepthBelowWater01(grid, config, x, z, cell.riverFord);
                        cell.height01 = Mathf.Clamp01(waterH - depth);
                        cell.slopeDeg = 0f;
                    }
                }
            }
        }

        static float ResolveRiverBedDepthBelowWater01(GridSystem grid, MapGenConfig config, int x, int z, bool ford)
        {
            if (ford)
                return Mathf.Clamp(config.riverFordDepthBelowWater01, 0.002f, 0.12f);

            float mainDepth = Mathf.Clamp(config.riverBedDepthBelowWater01, 0.004f, 0.18f);
            float tributaryDepth = Mathf.Clamp(config.tributaryBedDepthBelowWater01, 0.004f, 0.18f);
            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count < 2)
                return mainDepth;

            int nearestRiver = FindNearestRiverCenterlineIndex(grid, x + 0.5f, z + 0.5f);
            return nearestRiver > 0 ? tributaryDepth : mainDepth;
        }

        static int FindNearestRiverCenterlineIndex(GridSystem grid, float cx, float cz)
        {
            int bestIndex = 0;
            float bestSq = float.PositiveInfinity;
            Vector2 p = new Vector2(cx, cz);

            for (int ri = 0; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
            {
                var line = grid.RiverCenterlinesCellSpace[ri];
                if (line == null || line.Count == 0)
                    continue;

                if (line.Count == 1)
                {
                    float d = (p - line[0]).sqrMagnitude;
                    if (d < bestSq)
                    {
                        bestSq = d;
                        bestIndex = ri;
                    }
                    continue;
                }

                for (int i = 0; i < line.Count - 1; i++)
                {
                    float d = DistanceSqPointToSegment(p, line[i], line[i + 1]);
                    if (d < bestSq)
                    {
                        bestSq = d;
                        bestIndex = ri;
                    }
                }
            }

            return bestIndex;
        }

        static float DistanceSqPointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = Mathf.Max(1e-5f, ab.sqrMagnitude);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
            Vector2 q = a + ab * t;
            return (p - q).sqrMagnitude;
        }

        /// <summary>
        /// Post-hidrología (con distancia a agua válida): recorta picos en márgenes y recalcula pendientes en tierra.
        /// Carve ciudad/camino permanece en <see cref="TerrainCarver"/> tras colocar ciudades y red vial.
        /// </summary>
        public static void GenerateFinalTerrainPass(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null) return;
            ApplySpawnFriendlyPeakSuppression(grid, config);
            RecalculateLandSlopes(grid, config);
            if (config.debugLogs)
                Debug.Log("Final terrain pass (post-hidrología): supresión de picos + slopes Land.");
        }

        /// <summary>Recalcula pendiente en tierra (no agua/río). Tras cambios de height01 (macro, refine, carve).</summary>
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

        /// <summary>
        /// Cuencas radiales suaves cuando el alpha macro no coloca cuencas (macro off o macroBasinCount=0).
        /// No crea agua; solo tendencia de altura para downhill antes de <see cref="WaterGenerator.GenerateWater"/>.
        /// </summary>
        static void ApplyFallbackHydrologyBasins(GridSystem grid, MapGenConfig config, IRng rng)
        {
            int w = grid.Width;
            int h = grid.Height;
            int margin = Mathf.Clamp(config.macroMountainSpawnAvoidanceMarginCells, 4, Mathf.Max(4, Mathf.Min(w, h) / 2));
            int basinCount = rng.NextInt(FallbackBasinCountMin, FallbackBasinCountMaxExclusive);

            for (int b = 0; b < basinCount; b++)
            {
                for (int attempt = 0; attempt < 36; attempt++)
                {
                    int cx = rng.NextInt(margin, w - margin);
                    int cz = rng.NextInt(margin, h - margin);
                    ref var cell = ref grid.GetCell(cx, cz);
                    if (cell.type != CellType.Land)
                        continue;

                    int rad = rng.NextInt(FallbackBasinRadiusMin, FallbackBasinRadiusMaxExclusive);
                    float depth = Mathf.Lerp(FallbackBasinDepthMin, FallbackBasinDepthMax, rng.NextFloat());
                    ApplyRadialLandHeight01Delta(grid, cx, cz, rad, -depth, onlyLand: true);
                    break;
                }
            }
        }

        static void ApplyRadialLandHeight01Delta(GridSystem grid, int cx, int cz, int radius, float delta01, bool onlyLand)
        {
            int w = grid.Width;
            int h = grid.Height;
            for (int x = Mathf.Max(0, cx - radius); x < Mathf.Min(w, cx + radius + 1); x++)
            {
                for (int z = Mathf.Max(0, cz - radius); z < Mathf.Min(h, cz + radius + 1); z++)
                {
                    ref var cell = ref grid.GetCell(x, z);
                    if (onlyLand && cell.type != CellType.Land)
                        continue;
                    int dx = x - cx;
                    int dz = z - cz;
                    float t = 1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz) / Mathf.Max(1f, radius));
                    float falloff = Mathf.Pow(t, 0.82f);
                    cell.height01 = Mathf.Clamp01(cell.height01 + delta01 * falloff);
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
