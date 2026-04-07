using UnityEngine;
using Project.Gameplay.Map.Generation.Alpha;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Alpha: relieve macro automático (picos y cuencas) sobre el heightmap lógico del grid.
    /// No expone geometría manual; solo parámetros agregados desde <see cref="MapGenConfig"/>.
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
                    add *= 2f;
                    ApplyRadialDelta(grid, cx, cz, rad, add, onlyLand: true);
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
                    ApplyRadialDelta(grid, cx, cz, rad, -sub, onlyLand: true);
                    break;
                }
            }
        }

        static void ApplyRadialDelta(GridSystem grid, int cx, int cz, int radius, float delta01, bool onlyLand)
        {
            int w = grid.Width;
            int h = grid.Height;
            for (int x = Mathf.Max(0, cx - radius); x < Mathf.Min(w, cx + radius + 1); x++)
            {
                for (int z = Mathf.Max(0, cz - radius); z < Mathf.Min(h, cz + radius + 1); z++)
                {
                    ref var cell = ref grid.GetCell(x, z);
                    if (onlyLand && cell.type != CellType.Land) continue;
                    int dx = x - cx;
                    int dz = z - cz;
                    float t = 1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz) / Mathf.Max(1f, radius));
                    float falloff = t * t;
                    cell.height01 = Mathf.Clamp01(cell.height01 + delta01 * falloff);
                }
            }
        }
    }
}
