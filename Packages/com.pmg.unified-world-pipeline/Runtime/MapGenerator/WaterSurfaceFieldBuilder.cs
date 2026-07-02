using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Datos visuales estilo Crest, pero baked desde la hidrologia procedural del RTS:
    /// profundidad/orilla y flujo downstream por celda.
    /// </summary>
    public static class WaterSurfaceFieldBuilder
    {
        public static void Build(GridSystem grid, MapGenConfig config)
        {
            if (grid == null)
                return;

            int w = grid.Width;
            int h = grid.Height;
            grid.WaterShoreDistanceCells = BuildInteriorDistanceToShore(grid);
            grid.WaterDepth01 = new float[w, h];
            grid.WaterFlowXZ = new Vector2[w, h];

            BuildDepth(grid, config);
            BuildRiverFlow(grid, config);
            LogSummary(grid, config);
        }

        public static float SampleDepth01(GridSystem grid, Vector3 world)
        {
            if (grid?.WaterDepth01 == null)
                return 0f;
            Vector2Int c = grid.WorldToCell(world);
            if (!grid.InBoundsCell(c))
                return 0f;
            return grid.WaterDepth01[c.x, c.y];
        }

        public static Vector2 SampleFlowXZ(GridSystem grid, Vector3 world)
        {
            if (grid?.WaterFlowXZ == null)
                return Vector2.zero;
            Vector2Int c = grid.WorldToCell(world);
            if (!grid.InBoundsCell(c))
                return Vector2.zero;
            return grid.WaterFlowXZ[c.x, c.y];
        }

        static int[,] BuildInteriorDistanceToShore(GridSystem grid)
        {
            int w = grid.Width;
            int h = grid.Height;
            const int inf = 1_000_000;
            var dist = new int[w, h];
            var q = new Queue<Vector2Int>(w * h / 4);

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    bool water = IsWaterCell(grid.GetCell(x, z).type);
                    bool shore = x == 0 || z == 0 || x == w - 1 || z == h - 1;
                    if (water)
                    {
                        foreach (var n in grid.Neighbors8(x, z))
                        {
                            if (!IsWaterCell(grid.GetCell(n.x, n.y).type))
                            {
                                shore = true;
                                break;
                            }
                        }
                    }

                    if (water && shore)
                    {
                        dist[x, z] = 0;
                        q.Enqueue(new Vector2Int(x, z));
                    }
                    else
                    {
                        dist[x, z] = inf;
                    }
                }
            }

            while (q.Count > 0)
            {
                Vector2Int p = q.Dequeue();
                int nd = dist[p.x, p.y] + 1;
                foreach (var n in grid.Neighbors8(p.x, p.y))
                {
                    if (!IsWaterCell(grid.GetCell(n.x, n.y).type))
                        continue;
                    if (nd >= dist[n.x, n.y])
                        continue;
                    dist[n.x, n.y] = nd;
                    q.Enqueue(n);
                }
            }

            return dist;
        }

        static void BuildDepth(GridSystem grid, MapGenConfig config)
        {
            float lakeNorm = Mathf.Max(1f, config != null && config.lakeShoreVisualWidth > 0.01f ? config.lakeShoreVisualWidth : 7f);
            float riverNorm = Mathf.Max(1f, config != null && config.riverShoreVisualWidth > 0.01f ? config.riverShoreVisualWidth : 1.55f);
            float waterHeight01 = config != null ? config.waterHeight01 : 0.24f;
            float lakeDepthScale = Mathf.Max(0.01f, config != null ? config.lakeBedDepthBelowWater01 : 0.082f);
            float riverDepthScale = Mathf.Max(0.005f, config != null ? Mathf.Max(config.riverBedDepthBelowWater01, config.tributaryBedDepthBelowWater01) : 0.052f);

            for (int x = 0; x < grid.Width; x++)
            {
                for (int z = 0; z < grid.Height; z++)
                {
                    ref CellData c = ref grid.GetCell(x, z);
                    if (!IsWaterCell(c.type))
                    {
                        grid.WaterDepth01[x, z] = 0f;
                        continue;
                    }

                    float norm = c.type == CellType.River ? riverNorm : lakeNorm;
                    float shore01 = Mathf.Clamp01(grid.WaterShoreDistanceCells[x, z] / norm);
                    float bedDepth01 = Mathf.Clamp01((waterHeight01 - c.height01) / (c.type == CellType.River ? riverDepthScale : lakeDepthScale));
                    float depth01 = Mathf.Max(shore01, bedDepth01);

                    if (c.riverFord || c.waterTraverse == WaterTraverseMode.FordShallow)
                        depth01 = Mathf.Min(depth01, 0.24f);

                    grid.WaterDepth01[x, z] = Mathf.Clamp01(depth01);
                }
            }
        }

        static void BuildRiverFlow(GridSystem grid, MapGenConfig config)
        {
            var lines = grid.RiverVisualSurfacesBuilt && grid.RiverVisualSurfaces != null
                ? null
                : grid.RiverCenterlinesCellSpace;

            if (grid.RiverVisualSurfacesBuilt && grid.RiverVisualSurfaces != null)
            {
                for (int i = 0; i < grid.RiverVisualSurfaces.Count; i++)
                {
                    var data = grid.RiverVisualSurfaces[i];
                    if (data == null || data.FinalCenterlineCells == null)
                        continue;
                    StampFlowLine(grid, config, data.FinalCenterlineCells, i);
                }
            }
            else if (lines != null)
            {
                for (int i = 0; i < lines.Count; i++)
                    StampFlowLine(grid, config, lines[i], i);
            }

            SmoothFlowInsideWater(grid);
        }

        static void StampFlowLine(GridSystem grid, MapGenConfig config, List<Vector2> line, int riverIndex)
        {
            if (line == null || line.Count < 2)
                return;

            float widthRatio = 1f;
            if (grid.RiverWidthRatioToMain != null && riverIndex >= 0 && riverIndex < grid.RiverWidthRatioToMain.Count)
                widthRatio = Mathf.Clamp(grid.RiverWidthRatioToMain[riverIndex], 0.15f, 1.25f);

            float radius = config != null
                ? Mathf.Max(1.25f, (riverIndex == 0 ? config.riverVisualRibbonFullWidthCellsMain : config.riverVisualRibbonFullWidthCellsTributary) * 0.75f)
                : 2f;
            float speed = Mathf.Lerp(0.28f, 1f, widthRatio);

            for (int i = 0; i < line.Count - 1; i++)
            {
                Vector2 a = line[i];
                Vector2 b = line[i + 1];
                Vector2 d = b - a;
                float len = d.magnitude;
                if (len < 1e-4f)
                    continue;

                Vector2 dir = d / len;
                int steps = Mathf.Max(1, Mathf.CeilToInt(len * 2f));
                for (int s = 0; s <= steps; s++)
                {
                    Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                    int cx = Mathf.RoundToInt(p.x);
                    int cz = Mathf.RoundToInt(p.y);
                    int r = Mathf.CeilToInt(radius);
                    for (int dz = -r; dz <= r; dz++)
                    {
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int x = cx + dx;
                            int z = cz + dz;
                            if (!grid.InBoundsCell(x, z) || grid.GetCell(x, z).type != CellType.River)
                                continue;

                            float dist = Vector2.Distance(new Vector2(x + 0.5f, z + 0.5f), p);
                            if (dist > radius)
                                continue;

                            float weight = Mathf.Clamp01(1f - dist / Mathf.Max(0.001f, radius));
                            Vector2 current = grid.WaterFlowXZ[x, z];
                            Vector2 target = dir * speed;
                            grid.WaterFlowXZ[x, z] = Vector2.Lerp(current, target, Mathf.Max(weight, 0.35f));
                        }
                    }
                }
            }
        }

        static void SmoothFlowInsideWater(GridSystem grid)
        {
            var src = grid.WaterFlowXZ;
            var dst = new Vector2[grid.Width, grid.Height];
            for (int x = 0; x < grid.Width; x++)
            {
                for (int z = 0; z < grid.Height; z++)
                {
                    if (!IsWaterCell(grid.GetCell(x, z).type))
                        continue;

                    Vector2 sum = src[x, z] * 2f;
                    float weight = 2f;
                    foreach (var n in grid.Neighbors8(x, z))
                    {
                        if (!IsWaterCell(grid.GetCell(n.x, n.y).type))
                            continue;
                        sum += src[n.x, n.y];
                        weight += 1f;
                    }

                    dst[x, z] = sum / Mathf.Max(1f, weight);
                }
            }

            grid.WaterFlowXZ = dst;
        }

        static void LogSummary(GridSystem grid, MapGenConfig config)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;

            int water = 0;
            int river = 0;
            int flow = 0;
            float depthMax = 0f;
            float flowMax = 0f;
            for (int x = 0; x < grid.Width; x++)
            {
                for (int z = 0; z < grid.Height; z++)
                {
                    CellType t = grid.GetCell(x, z).type;
                    if (t == CellType.Water) water++;
                    else if (t == CellType.River) river++;

                    float d = grid.WaterDepth01 != null ? grid.WaterDepth01[x, z] : 0f;
                    if (d > depthMax) depthMax = d;

                    float f = grid.WaterFlowXZ != null ? grid.WaterFlowXZ[x, z].magnitude : 0f;
                    if (f > 0.01f) flow++;
                    if (f > flowMax) flowMax = f;
                }
            }

            Debug.Log(
                $"[WaterSurfaceFields] waterCells={water} riverCells={river} flowCells={flow} " +
                $"maxDepth01={depthMax:F2} maxFlow={flowMax:F2}");
        }

        static bool IsWaterCell(CellType type) => type == CellType.Water || type == CellType.River;
    }
}
