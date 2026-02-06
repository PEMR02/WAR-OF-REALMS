using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 3: genera agua como sistema. Ríos = random walk desde borde; lagos = flood fill desde semilla.</summary>
    public static class WaterGenerator
    {
        private static readonly Vector2Int[] EdgeDirections = { new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(-1, 0) };

        /// <summary>Parámetros: riverCount, lakeCount, maxLakeCells. Marca CellType Water/River. Determinista por rng.</summary>
        public static void GenerateWater(GridSystem grid, MapGenConfig config, IRng rng)
        {
            if (grid == null || config == null || rng == null) return;

            int w = grid.Width;
            int h = grid.Height;
            int waterCells = 0;

            // Ríos: random walk desde un borde hacia el interior (longitud fija por seed)
            int riverCount = Mathf.Min(config.riverCount, 8);
            int riverLength = Mathf.Max(20, Mathf.Min(w, h) / 2);
            for (int i = 0; i < riverCount; i++)
            {
                Vector2Int start = PickRiverStart(w, h, rng);
                var path = RandomWalkRiver(grid, start, riverLength, rng);
                foreach (var c in path)
                {
                    if (!grid.InBoundsCell(c.x, c.y)) continue;
                    ref var cell = ref grid.GetCell(c);
                    if (cell.type == CellType.Land)
                    {
                        cell.type = CellType.River;
                        cell.walkable = false;
                        cell.buildable = false;
                        waterCells++;
                    }
                }
            }

            // Lagos: flood fill (BFS) desde semilla en Land, hasta maxLakeCells por lago
            int lakeCount = Mathf.Min(config.lakeCount, 6);
            int maxLake = Mathf.Min(config.maxLakeCells, 300);
            for (int i = 0; i < lakeCount; i++)
            {
                int attempts = 20;
                while (attempts-- > 0)
                {
                    int cx = rng.NextInt(w / 6, (5 * w) / 6);
                    int cz = rng.NextInt(h / 6, (5 * h) / 6);
                    ref var seedCell = ref grid.GetCell(cx, cz);
                    if (seedCell.type != CellType.Land) continue;

                    int added = FloodFillLake(grid, new Vector2Int(cx, cz), maxLake);
                    waterCells += added;
                    break;
                }
            }

            int total = w * h;
            float pct = total > 0 ? (waterCells * 100f / total) : 0f;
            if (config.debugLogs)
                Debug.Log($"Fase3 Agua: {waterCells} celdas ({pct:F1}%), ríos={riverCount} (random walk), lagos={lakeCount} (flood fill).");
        }

        /// <summary>Elige una celda en el borde del mapa (arriba/abajo/izq/der).</summary>
        private static Vector2Int PickRiverStart(int w, int h, IRng rng)
        {
            int side = rng.NextInt(0, 4);
            switch (side)
            {
                case 0: return new Vector2Int(rng.NextInt(0, w), 0);           // abajo
                case 1: return new Vector2Int(rng.NextInt(0, w), h - 1);      // arriba
                case 2: return new Vector2Int(0, rng.NextInt(0, h));           // izq
                default: return new Vector2Int(w - 1, rng.NextInt(0, h));      // der
            }
        }

        /// <summary>Random walk desde start: N pasos, con ligera tendencia hacia el centro para que cruce el mapa.</summary>
        private static List<Vector2Int> RandomWalkRiver(GridSystem grid, Vector2Int start, int length, IRng rng)
        {
            var path = new List<Vector2Int> { start };
            int w = grid.Width;
            int h = grid.Height;
            int centerX = w / 2;
            int centerZ = h / 2;
            Vector2Int current = start;

            for (int step = 0; step < length; step++)
            {
                int dx = Mathf.Clamp(centerX - current.x, -1, 1);
                int dz = Mathf.Clamp(centerZ - current.y, -1, 1);
                // 60% hacia centro, 40% aleatorio
                int moveX, moveZ;
                if (rng.NextFloat() < 0.6f && (dx != 0 || dz != 0))
                {
                    moveX = dx != 0 ? dx : (rng.NextInt(0, 2) == 0 ? -1 : 1);
                    moveZ = dz != 0 ? dz : (rng.NextInt(0, 2) == 0 ? -1 : 1);
                    if (dx != 0 && dz != 0 && rng.NextFloat() < 0.5f) moveZ = 0;
                    else if (dx != 0 && dz != 0) moveX = 0;
                }
                else
                {
                    moveX = rng.NextInt(0, 3) - 1;
                    moveZ = rng.NextInt(0, 3) - 1;
                    if (moveX == 0 && moveZ == 0) moveX = 1;
                }
                Vector2Int next = new Vector2Int(current.x + moveX, current.y + moveZ);
                if (grid.InBoundsCell(next.x, next.y))
                {
                    current = next;
                    path.Add(current);
                }
            }
            return path;
        }

        /// <summary>Flood fill desde seed; solo Land; máximo maxCells. Retorna celdas marcadas como Water.</summary>
        private static int FloodFillLake(GridSystem grid, Vector2Int seed, int maxCells)
        {
            var queue = new Queue<Vector2Int>();
            var visited = new HashSet<Vector2Int>();
            queue.Enqueue(seed);
            visited.Add(seed);
            int count = 0;

            while (queue.Count > 0 && count < maxCells)
            {
                var c = queue.Dequeue();
                ref var cell = ref grid.GetCell(c);
                if (cell.type != CellType.Land) continue;
                cell.type = CellType.Water;
                cell.walkable = false;
                cell.buildable = false;
                count++;

                foreach (var dir in EdgeDirections)
                {
                    var n = new Vector2Int(c.x + dir.x, c.y + dir.y);
                    if (!grid.InBoundsCell(n.x, n.y) || visited.Contains(n)) continue;
                    ref var ncell = ref grid.GetCell(n);
                    if (ncell.type != CellType.Land) continue;
                    visited.Add(n);
                    queue.Enqueue(n);
                }
            }
            return count;
        }
    }
}
