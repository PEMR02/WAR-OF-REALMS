using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Campo de distancia a agua (río/lago) en celdas — Chebyshev, multi-fuente BFS.</summary>
    public static class WaterDistanceField
    {
        public const int UnreachableDistance = 1_000_000;
        const int Inf = UnreachableDistance;

        public static void Build(GridSystem grid)
        {
            if (grid == null) return;
            int w = grid.Width;
            int h = grid.Height;
            var dist = new int[w, h];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    dist[x, z] = Inf;

            var q = new Queue<Vector2Int>();
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    ref var c = ref grid.GetCell(x, z);
                    if (c.type == CellType.Water || c.type == CellType.River)
                    {
                        dist[x, z] = 0;
                        q.Enqueue(new Vector2Int(x, z));
                    }
                }
            }

            while (q.Count > 0)
            {
                var p = q.Dequeue();
                int d = dist[p.x, p.y];
                foreach (var n in grid.Neighbors8(p.x, p.y))
                {
                    int nd = d + 1;
                    if (nd < dist[n.x, n.y])
                    {
                        dist[n.x, n.y] = nd;
                        q.Enqueue(n);
                    }
                }
            }

            grid.DistanceToWaterCells = dist;
        }

        public static int Get(GridSystem grid, int x, int z)
        {
            if (grid?.DistanceToWaterCells == null) return Inf;
            if ((uint)x >= (uint)grid.Width || (uint)z >= (uint)grid.Height) return Inf;
            return grid.DistanceToWaterCells[x, z];
        }
    }
}
