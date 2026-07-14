using UnityEngine;
using Project.Gameplay.Map.Generation.Alpha;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Alpha: relieve macro (picos orgánicos, mesetas RTS, cuencas) sobre height01.
    /// Formas elípticas + warp Perlin (no discos perfectos). Mesetas: techo casi plano buildable.
    /// </summary>
    public static class MacroTerrainSculptor
    {
        public static void Apply(GridSystem grid, MapGenConfig config, IRng rng, TerrainFeatureRuntime record)
        {
            if (grid == null || config == null || rng == null || !config.macroTerrainEnabled) return;

            record?.mountains.Clear();
            if (record != null)
            {
                record.macroMountainMassRequested = config.macroMountainMassCount;
                record.macroBasinRequested = config.macroBasinCount;
            }
            int margin = Mathf.Clamp(config.macroMountainSpawnAvoidanceMarginCells, 4, grid.Width / 2);
            int w = grid.Width;
            int h = grid.Height;

            for (int m = 0; m < config.macroMountainMassCount; m++)
            {
                for (int attempt = 0; attempt < 48; attempt++)
                {
                    int cx = rng.NextInt(margin, w - margin);
                    int cz = rng.NextInt(margin, h - margin);
                    ref var cell = ref grid.GetCell(cx, cz);
                    if (cell.type != CellType.Land) continue;
                    int rMin = Mathf.Min(config.macroMountainRadiusCellsMin, config.macroMountainRadiusCellsMax);
                    int rMax = Mathf.Max(config.macroMountainRadiusCellsMin, config.macroMountainRadiusCellsMax);
                    int rad = rng.NextInt(rMin, rMax + 1);
                    float add = config.macroMountainHeight01Min +
                                (config.macroMountainHeight01Max - config.macroMountainHeight01Min) * rng.NextFloat();
                    // Picos suaves: high-ground RTS = mesetas (ApplyPlateaus). Evita cordillera vs headwater.
                    add *= 1.35f;
                    var shape = SampleOrganicShape(rng);
                    ApplyOrganicMountain(grid, cx, cz, rad, add, shape, onlyLand: true);
                    // Segundo lóbulo irregular (cordillera corta / espolón).
                    if (rng.NextFloat() < 0.72f)
                    {
                        float ang = rng.NextFloat() * Mathf.PI * 2f;
                        float sep = rad * (0.35f + 0.45f * rng.NextFloat());
                        int sx = Mathf.Clamp(cx + Mathf.RoundToInt(Mathf.Cos(ang) * sep), 0, w - 1);
                        int sz = Mathf.Clamp(cz + Mathf.RoundToInt(Mathf.Sin(ang) * sep), 0, h - 1);
                        if (grid.GetCell(sx, sz).type == CellType.Land)
                        {
                            int rad2 = Mathf.Max(3, Mathf.RoundToInt(rad * (0.45f + 0.35f * rng.NextFloat())));
                            var shape2 = SampleOrganicShape(rng);
                            ApplyOrganicMountain(grid, sx, sz, rad2, add * 0.62f, shape2, onlyLand: true);
                        }
                    }

                    ref var after = ref grid.GetCell(cx, cz);
                    record?.mountains.Add(new MountainFeature
                    {
                        peakCell = new Vector2Int(cx, cz),
                        peakHeight01 = after.height01,
                        radiusCells = rad
                    });
                    break;
                }
            }

            MarkMountainCores(grid, record);
            ApplyPlateaus(grid, config, rng, record, margin);

            for (int b = 0; b < config.macroBasinCount; b++)
            {
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    int cx = rng.NextInt(margin, w - margin);
                    int cz = rng.NextInt(margin, h - margin);
                    ref var cell = ref grid.GetCell(cx, cz);
                    if (cell.type != CellType.Land) continue;
                    int rad = rng.NextInt(8, 22);
                    float sub = Mathf.Clamp(config.macroBasinDepth01, 0.01f, 0.2f);
                    var shape = SampleOrganicShape(rng);
                    ApplyOrganicMountain(grid, cx, cz, rad, -sub, shape, onlyLand: true);
                    break;
                }
            }
        }

        struct OrganicShape
        {
            public float aspect;
            public float angleRad;
            public float noiseOx;
            public float noiseOz;
            public float noiseScale;
            public float warpAmp;
            public float ridgeAmp;
        }

        static OrganicShape SampleOrganicShape(IRng rng)
        {
            return new OrganicShape
            {
                aspect = 0.48f + rng.NextFloat() * 0.85f,
                angleRad = rng.NextFloat() * Mathf.PI,
                noiseOx = rng.NextFloat() * 512f,
                noiseOz = rng.NextFloat() * 512f,
                noiseScale = 0.045f + rng.NextFloat() * 0.055f,
                warpAmp = 0.28f + rng.NextFloat() * 0.32f,
                ridgeAmp = 0.12f + rng.NextFloat() * 0.22f
            };
        }

        /// <summary>Distancia normalizada elíptica + warp Perlin (borde irregular).</summary>
        static float OrganicNormDist(int x, int z, int cx, int cz, int radius, in OrganicShape s)
        {
            float dx = x - cx;
            float dz = z - cz;
            float c = Mathf.Cos(s.angleRad);
            float sn = Mathf.Sin(s.angleRad);
            float lx = dx * c + dz * sn;
            float lz = -dx * sn + dz * c;
            float invR = 1f / Mathf.Max(1f, radius);
            float u = lx * invR;
            float v = lz * invR / Mathf.Max(0.35f, s.aspect);
            float eDist = Mathf.Sqrt(u * u + v * v);

            float n1 = Mathf.PerlinNoise(
                (x + s.noiseOx) * s.noiseScale,
                (z + s.noiseOz) * s.noiseScale);
            float n2 = Mathf.PerlinNoise(
                (x + s.noiseOx) * s.noiseScale * 2.1f + 19.7f,
                (z + s.noiseOz) * s.noiseScale * 2.1f + 7.3f);
            float warp = (n1 - 0.5f) * 2f * s.warpAmp + (n2 - 0.5f) * s.warpAmp * 0.55f;
            return eDist * (1f + warp);
        }

        static void ApplyOrganicMountain(
            GridSystem grid,
            int cx,
            int cz,
            int radius,
            float delta01,
            in OrganicShape shape,
            bool onlyLand)
        {
            int w = grid.Width;
            int h = grid.Height;
            // Bounding box holgado por elongación + warp.
            int pad = Mathf.CeilToInt(radius * Mathf.Max(1.15f, shape.aspect + 0.35f) * (1f + shape.warpAmp));
            for (int x = Mathf.Max(0, cx - pad); x < Mathf.Min(w, cx + pad + 1); x++)
            {
                for (int z = Mathf.Max(0, cz - pad); z < Mathf.Min(h, cz + pad + 1); z++)
                {
                    ref var cell = ref grid.GetCell(x, z);
                    if (onlyLand && cell.type != CellType.Land) continue;

                    float nd = OrganicNormDist(x, z, cx, cz, radius, shape);
                    if (nd >= 1.02f)
                        continue;

                    float t = 1f - Mathf.Clamp01(nd);
                    // Pico más marcado en el núcleo; falda más larga.
                    float falloff = Mathf.Pow(t, 0.55f);
                    float ridge = 1f + shape.ridgeAmp * (Mathf.PerlinNoise(
                        (x + shape.noiseOx) * shape.noiseScale * 3.4f,
                        (z + shape.noiseOz) * shape.noiseScale * 3.4f) - 0.35f);
                    cell.height01 = Mathf.Clamp01(cell.height01 + delta01 * falloff * ridge);
                }
            }
        }

        static void ApplyPlateaus(
            GridSystem grid,
            MapGenConfig config,
            IRng rng,
            TerrainFeatureRuntime record,
            int margin)
        {
            int plateauCount = Mathf.Clamp(config.macroPlateauCount, 0, 4);
            if (plateauCount <= 0)
                return;

            int w = grid.Width;
            int h = grid.Height;
            int rMin = Mathf.Min(config.macroPlateauRadiusCellsMin, config.macroPlateauRadiusCellsMax);
            int rMax = Mathf.Max(config.macroPlateauRadiusCellsMin, config.macroPlateauRadiusCellsMax);
            int rim = Mathf.Clamp(config.macroPlateauRimCells, 3, 16);
            float hMin = Mathf.Min(config.macroPlateauHeight01Min, config.macroPlateauHeight01Max);
            float hMax = Mathf.Max(config.macroPlateauHeight01Min, config.macroPlateauHeight01Max);

            var placed = new Vector2Int[plateauCount];
            int placedN = 0;
            int accepted = 0;

            for (int p = 0; p < plateauCount; p++)
            {
                bool ok = false;
                for (int attempt = 0; attempt < 56; attempt++)
                {
                    int cx = rng.NextInt(margin, w - margin);
                    int cz = rng.NextInt(margin, h - margin);
                    ref var cell = ref grid.GetCell(cx, cz);
                    if (cell.type != CellType.Land)
                        continue;

                    int flatRad = rng.NextInt(rMin, rMax + 1);
                    int totalRad = flatRad + rim;
                    if (!IsClearOfFeatures(cx, cz, totalRad, record, placed, placedN, gap: 6))
                        continue;

                    float raise = hMin + (hMax - hMin) * rng.NextFloat();
                    float topH = Mathf.Clamp01(cell.height01 + raise);
                    var shape = SampleOrganicShape(rng);
                    // Mesetas algo más redondeadas que picos, pero no círculos perfectos.
                    shape.aspect = Mathf.Clamp(shape.aspect, 0.62f, 1.15f);
                    shape.warpAmp *= 0.85f;
                    // 1–2 rampas de acceso (talud suave); resto de borde más empinado (high-ground RTS).
                    float rampYaw = rng.NextFloat() * Mathf.PI * 2f;
                    bool dualRamp = rng.NextFloat() < 0.55f;
                    ApplyOrganicPlateau(grid, cx, cz, flatRad, rim, topH, shape, rampYaw, dualRamp);
                    placed[placedN++] = new Vector2Int(cx, cz);
                    accepted++;
                    ok = true;
                    break;
                }

                if (!ok && config.debugLogs)
                {
                    Debug.LogWarning(
                        $"[MacroTerrainSculptor] plateau {p + 1}/{plateauCount} skipped (no clear site)");
                }
            }

            if (config.debugLogs)
            {
                Debug.Log(
                    $"[MacroTerrainSculptor] plateaus accepted={accepted}/{plateauCount} " +
                    $"mountains={record?.mountains?.Count ?? 0}");
            }
        }

        static bool IsClearOfFeatures(
            int cx,
            int cz,
            int totalRad,
            TerrainFeatureRuntime record,
            Vector2Int[] placedPlateaus,
            int placedN,
            int gap)
        {
            if (record?.mountains != null)
            {
                for (int i = 0; i < record.mountains.Count; i++)
                {
                    var m = record.mountains[i];
                    int need = totalRad + Mathf.Max(4, m.radiusCells) + gap;
                    int dx = Mathf.Abs(cx - m.peakCell.x);
                    int dz = Mathf.Abs(cz - m.peakCell.y);
                    if (Mathf.Max(dx, dz) < need)
                        return false;
                }
            }

            for (int i = 0; i < placedN; i++)
            {
                int dx = Mathf.Abs(cx - placedPlateaus[i].x);
                int dz = Mathf.Abs(cz - placedPlateaus[i].y);
                if (Mathf.Max(dx, dz) < totalRad * 2 + gap)
                    return false;
            }

            return true;
        }

        static void ApplyOrganicPlateau(
            GridSystem grid,
            int cx,
            int cz,
            int flatRadius,
            int rimCells,
            float topHeight01,
            in OrganicShape shape,
            float rampYawRad,
            bool dualRamp)
        {
            int w = grid.Width;
            int h = grid.Height;
            int outer = flatRadius + rimCells;
            int pad = Mathf.CeilToInt(outer * Mathf.Max(1.15f, shape.aspect + 0.35f) * (1f + shape.warpAmp) * 1.35f);
            float rampDx = Mathf.Cos(rampYawRad);
            float rampDz = Mathf.Sin(rampYawRad);

            for (int x = Mathf.Max(0, cx - pad); x < Mathf.Min(w, cx + pad + 1); x++)
            {
                for (int z = Mathf.Max(0, cz - pad); z < Mathf.Min(h, cz + pad + 1); z++)
                {
                    ref var cell = ref grid.GetCell(x, z);
                    if (cell.type != CellType.Land && cell.type != CellType.Mountain)
                        continue;

                    float nd = OrganicNormDist(x, z, cx, cz, flatRadius, shape);
                    float rimNorm = rimCells / Mathf.Max(1f, flatRadius);

                    // Lado rampa (1 o 2): talud largo; escarpe: talud corto.
                    float wx = x - cx;
                    float wz = z - cz;
                    float len = Mathf.Sqrt(wx * wx + wz * wz);
                    float towardRamp = 0f;
                    if (len > 1e-3f)
                    {
                        float ux = wx / len;
                        float uz = wz / len;
                        float d0 = ux * rampDx + uz * rampDz;
                        if (dualRamp)
                            towardRamp = Mathf.Max(d0, -d0); // dos entradas opuestas
                        else
                            towardRamp = d0;
                    }

                    // Escarpe: rim corto; rampa: rim largo (acceso jugable).
                    float rimScale = Mathf.Lerp(0.32f, 2.05f, Mathf.SmoothStep(-0.2f, 0.55f, towardRamp));
                    float flatEnd = Mathf.Lerp(0.90f, 0.95f, Mathf.SmoothStep(0.1f, 0.7f, towardRamp));
                    float outerNd = 1f + rimNorm * rimScale;
                    if (nd > outerNd + 0.02f)
                        continue;

                    float target;
                    if (nd <= flatEnd)
                    {
                        float micro = (Mathf.PerlinNoise(
                            (x + shape.noiseOx) * 0.11f,
                            (z + shape.noiseOz) * 0.11f) - 0.5f) * 0.025f;
                        target = topHeight01 + micro;
                    }
                    else
                    {
                        float t = Mathf.Clamp01((nd - flatEnd) / Mathf.Max(0.05f, outerNd - flatEnd));
                        // Escarpe: caída más rápida; rampa: SmoothStep largo.
                        float edge = towardRamp > 0.2f
                            ? 1f - Mathf.SmoothStep(0f, 1f, t)
                            : 1f - (t * t);
                        if (towardRamp < -0.05f)
                            edge = Mathf.Pow(edge, 1.85f);
                        target = Mathf.Lerp(cell.height01, topHeight01, edge);
                    }

                    if (target > cell.height01)
                        cell.height01 = Mathf.Clamp01(target);
                }
            }
        }

        static void MarkMountainCores(GridSystem grid, TerrainFeatureRuntime record)
        {
            if (grid == null || record?.mountains == null || record.mountains.Count == 0)
                return;

            int w = grid.Width;
            int h = grid.Height;
            for (int i = 0; i < record.mountains.Count; i++)
            {
                var m = record.mountains[i];
                int coreR = Mathf.Max(1, Mathf.RoundToInt(m.radiusCells * 0.38f));
                int cx = m.peakCell.x;
                int cz = m.peakCell.y;
                for (int x = Mathf.Max(0, cx - coreR); x < Mathf.Min(w, cx + coreR + 1); x++)
                {
                    for (int z = Mathf.Max(0, cz - coreR); z < Mathf.Min(h, cz + coreR + 1); z++)
                    {
                        int dx = x - cx;
                        int dz = z - cz;
                        if (dx * dx + dz * dz > coreR * coreR)
                            continue;
                        ref var cell = ref grid.GetCell(x, z);
                        if (cell.type == CellType.Land)
                            cell.type = CellType.Mountain;
                    }
                }
            }
        }
    }
}
