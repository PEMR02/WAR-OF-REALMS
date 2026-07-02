using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Campo continuo exclusivo del pipeline UnifiedSingleSurfaceExperimental.
    /// El mesh de agua y el terreno muestrean esta misma fuente para no generar costas distintas.
    /// </summary>
    public static class UnifiedWaterField
    {
        public const float Iso = 0.5f;

        public static bool IsEnabled(MapGenConfig config)
        {
            return WaterVisualPipelinePolicy.IsUnifiedSingleSurface(config);
        }

        public static float VisualSurfaceOffsetWorld(MapGenConfig config, float terrainY)
        {
            if (config == null) return 0f;
            return Mathf.Max(config.waterSurfaceOffset, 0.02f) +
                   config.unifiedWaterSurfaceExtraYOffsetWorld +
                   WaterMeshBuilder.ComputeUnifiedWaterDepthDrivenLiftWorld(config, terrainY);
        }

        public static float SampleWater01(GridSystem grid, MapGenConfig config, float cellX, float cellZ)
        {
            if (grid == null || config == null)
                return 0f;

            float lake = SampleLakeCellsBilinear(grid, cellX, cellZ);
            float river = SampleRiverCenterlineField(grid, config, cellX, cellZ);
            float cell = SampleCellFallback(grid, cellX, cellZ);
            return Mathf.Clamp01(Mathf.Max(Mathf.Max(lake, river), cell));
        }

        public static void FillField(
            float[,] field,
            int sw,
            int sh,
            int sampleX0,
            int sampleZ0,
            int subdiv,
            GridSystem grid,
            MapGenConfig config)
        {
            if (field == null || grid == null || config == null || subdiv <= 0)
                return;

            for (int z = 0; z < sh; z++)
            {
                float cellZ = (sampleZ0 + z) / (float)subdiv;
                for (int x = 0; x < sw; x++)
                {
                    float cellX = (sampleX0 + x) / (float)subdiv;
                    field[x, z] = SampleWater01(grid, config, cellX, cellZ);
                }
            }
        }

        static float SampleLakeCellsBilinear(GridSystem grid, float cellX, float cellZ)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(cellX), 0, grid.Width - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(cellZ), 0, grid.Height - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, grid.Width - 1);
            int z1 = Mathf.Clamp(z0 + 1, 0, grid.Height - 1);
            float tx = Mathf.Clamp01(cellX - x0);
            float tz = Mathf.Clamp01(cellZ - z0);

            float w00 = grid.GetCell(x0, z0).type == CellType.Water ? 1f : 0f;
            float w10 = grid.GetCell(x1, z0).type == CellType.Water ? 1f : 0f;
            float w01 = grid.GetCell(x0, z1).type == CellType.Water ? 1f : 0f;
            float w11 = grid.GetCell(x1, z1).type == CellType.Water ? 1f : 0f;
            return Mathf.Lerp(Mathf.Lerp(w00, w10, tx), Mathf.Lerp(w01, w11, tx), tz);
        }

        static float SampleCellFallback(GridSystem grid, float cellX, float cellZ)
        {
            int gx = Mathf.Clamp(Mathf.RoundToInt(cellX), 0, grid.Width - 1);
            int gz = Mathf.Clamp(Mathf.RoundToInt(cellZ), 0, grid.Height - 1);
            CellType t = grid.GetCell(gx, gz).type;
            if (t == CellType.Water)
                return 1f;
            if (t == CellType.River)
                return 0.72f;
            return 0f;
        }

        static float SampleRiverCenterlineField(GridSystem grid, MapGenConfig config, float cellX, float cellZ)
        {
            var lines = grid.RiverCenterlinesCellSpace;
            if (lines == null || lines.Count == 0)
                return 0f;

            int gx = Mathf.Clamp(Mathf.RoundToInt(cellX), 0, grid.Width - 1);
            int gz = Mathf.Clamp(Mathf.RoundToInt(cellZ), 0, grid.Height - 1);
            float widthMul = ResolveRiverWidthMultiplier(grid, gx, gz);
            float halfWidth = Mathf.Max(
                Mathf.Max(0.08f, config.riverVisualHalfWidthCells),
                Mathf.Max(0.3f, config.unifiedRiverFieldMinHalfWidthCells)) * widthMul;
            float softness = Mathf.Max(0.08f, config.riverVisualSoftnessCells + config.unifiedRiverFieldExtraSoftnessCells);
            float cull = halfWidth + softness + 2.25f;
            float d2 = MinDistSqPointToPolylines(cellX, cellZ, lines, cull);
            if (d2 >= 1e20f)
                return 0f;

            float d = Mathf.Sqrt(d2);
            if (d <= halfWidth)
                return 1f;
            if (d >= halfWidth + softness)
                return 0f;

            float t = Mathf.InverseLerp(halfWidth + softness, halfWidth, d);
            return Mathf.SmoothStep(0f, 1f, t);
        }

        static float ResolveRiverWidthMultiplier(GridSystem grid, int gx, int gz)
        {
            float mul = 1f;
            if (grid.WaterDepth01 != null &&
                gx >= 0 && gz >= 0 &&
                gx < grid.WaterDepth01.GetLength(0) &&
                gz < grid.WaterDepth01.GetLength(1))
            {
                mul += Mathf.Clamp01(grid.WaterDepth01[gx, gz]) * 0.45f;
            }

            if (grid.GetCell(gx, gz).type == CellType.Water)
                mul = Mathf.Max(mul, 1.35f);

            return Mathf.Clamp(mul, 1f, 2.2f);
        }

        static float MinDistSqPointToPolylines(float px, float pz, List<List<Vector2>> polylines, float cullMarginCells)
        {
            float best = float.PositiveInfinity;
            float cullSq = cullMarginCells * cullMarginCells;
            for (int i = 0; i < polylines.Count; i++)
            {
                var line = polylines[i];
                if (line == null || line.Count == 0)
                    continue;

                if (line.Count == 1)
                {
                    float d2 = (line[0] - new Vector2(px, pz)).sqrMagnitude;
                    if (d2 < best)
                        best = d2;
                    continue;
                }

                for (int p = 0; p < line.Count - 1; p++)
                {
                    Vector2 a = line[p];
                    Vector2 b = line[p + 1];
                    float minX = Mathf.Min(a.x, b.x) - cullMarginCells;
                    float maxX = Mathf.Max(a.x, b.x) + cullMarginCells;
                    float minZ = Mathf.Min(a.y, b.y) - cullMarginCells;
                    float maxZ = Mathf.Max(a.y, b.y) + cullMarginCells;
                    if (px < minX || px > maxX || pz < minZ || pz > maxZ)
                        continue;

                    float d2 = DistanceSqPointToSegment(px, pz, a, b);
                    if (d2 < best)
                    {
                        best = d2;
                        if (best <= cullSq * 0.0001f)
                            return best;
                    }
                }
            }
            return best;
        }

        static float DistanceSqPointToSegment(float px, float pz, Vector2 a, Vector2 b)
        {
            float vx = b.x - a.x;
            float vz = b.y - a.y;
            float wx = px - a.x;
            float wz = pz - a.y;
            float lenSq = vx * vx + vz * vz;
            if (lenSq <= 1e-6f)
            {
                float dx = px - a.x;
                float dz = pz - a.y;
                return dx * dx + dz * dz;
            }

            float t = Mathf.Clamp01((wx * vx + wz * vz) / lenSq);
            float cx = a.x + vx * t;
            float cz = a.y + vz * t;
            float ddx = px - cx;
            float ddz = pz - cz;
            return ddx * ddx + ddz * ddz;
        }
    }
}
