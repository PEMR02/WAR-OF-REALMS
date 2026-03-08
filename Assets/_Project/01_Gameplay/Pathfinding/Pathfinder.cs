using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Map;
using Project.Core.DataStructures;

namespace Project.Gameplay.Pathfinding
{
    /// <summary>A* pathfinding sobre MapGrid. Evita agua, edificios y celdas bloqueadas.</summary>
    public class Pathfinder
    {
        private const int MaxIterations = 10000;
        private const float DiagonalCost = 1.414f;

        private static readonly Vector2Int[] Neighbors8 =
        {
            new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
        };

        private readonly PriorityQueue<Node> _openSet = new PriorityQueue<Node>();
        private readonly HashSet<Vector2Int> _closedSet = new HashSet<Vector2Int>();
        private readonly Dictionary<Vector2Int, Node> _nodes = new Dictionary<Vector2Int, Node>();

        private struct Node
        {
            public Vector2Int cell;
            public float gCost;
            public float hCost;
            public float fCost => gCost + hCost;
            public Vector2Int parent;
        }

        /// <param name="canSwim">Si true, puede atravesar agua. Si false, agua es intransitable.</param>
        public PathResult FindPath(Vector3 worldStart, Vector3 worldGoal, bool canSwim = false)
        {
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady)
                return PathResult.Failed("Grid no está listo");

            Vector2Int start = MapGrid.Instance.WorldToCell(worldStart);
            Vector2Int goal = MapGrid.Instance.WorldToCell(worldGoal);

            if (!MapGrid.Instance.IsInBounds(start))
                return PathResult.Failed("Start fuera de bounds");

            if (!MapGrid.Instance.IsInBounds(goal))
                return PathResult.Failed("Goal fuera de bounds");

            // Si el destino está en agua y NO puede nadar → ir a la orilla más cercana
            if (!canSwim && MapGrid.Instance.IsWater(goal))
            {
                Vector2Int shore = FindNearestLandCell(goal, 15);
                if (shore == goal)
                    return PathResult.Failed("Destino en agua sin orilla cerca");
                goal = shore;
            }

            // Si el destino está ocupado (edificio) → buscar celda libre más cercana al borde desde la dirección de start
            if (!MapGrid.Instance.IsCellFree(goal))
            {
                Vector2Int nearest = FindNearestWalkableCell(goal, 6, canSwim, start);
                if (nearest == goal)
                    return PathResult.Failed("Goal ocupado y sin celda libre cerca");
                goal = nearest;
            }

            return FindPathInternal(start, goal, canSwim);
        }

        /// <summary>Busca la celda de TIERRA más cercana (no agua, no bloqueada, no ocupada).</summary>
        private static Vector2Int FindNearestLandCell(Vector2Int center, int maxRadius)
        {
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return center;
            for (int r = 1; r <= maxRadius; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        var c = new Vector2Int(center.x + dx, center.y + dy);
                        if (!MapGrid.Instance.IsInBounds(c)) continue;
                        if (MapGrid.Instance.IsWater(c)) continue;
                        if (!MapGrid.Instance.IsCellFree(c)) continue;
                        return c;
                    }
                }
            }
            return center;
        }

        /// <summary>
        /// Busca la celda transitable más cercana al goal ocupado, priorizando el lado
        /// más próximo a 'fromHint' (la celda de inicio). Así las unidades van al lado
        /// del edificio desde el que se aproximan en lugar de a cualquier celda libre aleatoria.
        /// </summary>
        private static Vector2Int FindNearestWalkableCell(Vector2Int center, int maxRadius, bool canSwim, Vector2Int? fromHint = null)
        {
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return center;

            // Dirección preferida: desde el edificio hacia la unidad (para que la celda encontrada quede del lado correcto)
            bool hasDir = fromHint.HasValue;
            Vector2 prefDir = hasDir
                ? new Vector2(fromHint.Value.x - center.x, fromHint.Value.y - center.y).normalized
                : Vector2.zero;

            Vector2Int best = center;
            float bestScore = float.MaxValue;

            for (int r = 1; r <= maxRadius; r++)
            {
                // Si ya tenemos candidato y el radio actual no puede mejorar el score, parar
                if (best != center && r > bestScore + 1f) break;

                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        var c = new Vector2Int(center.x + dx, center.y + dy);
                        if (!MapGrid.Instance.IsInBounds(c)) continue;
                        if (!MapGrid.Instance.IsCellFree(c)) continue;
                        if (!canSwim && MapGrid.Instance.IsWater(c)) continue;

                        float score = r;
                        if (hasDir && (dx != 0 || dy != 0))
                        {
                            // Restar hasta 0.9 al score si la celda está en la dirección preferida
                            Vector2 d = new Vector2(dx, dy).normalized;
                            float dot = Vector2.Dot(d, prefDir); // -1..1
                            score -= dot * 0.9f;
                        }

                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = c;
                        }
                    }
                }
            }

            return best;
        }

        private PathResult FindPathInternal(Vector2Int start, Vector2Int goal, bool canSwim)
        {
            if (start == goal)
                return PathResult.Success(new List<Vector2Int>());

            _openSet.Clear();
            _closedSet.Clear();
            _nodes.Clear();

            var startNode = new Node
            {
                cell = start,
                gCost = 0f,
                hCost = Heuristic(start, goal),
                parent = start
            };

            _openSet.Enqueue(startNode, startNode.fCost);
            _nodes[start] = startNode;

            int iterations = 0;
            while (_openSet.Count > 0 && iterations < MaxIterations)
            {
                iterations++;
                Node current = _openSet.Dequeue();

                if (current.cell == goal)
                    return ReconstructPath(start, goal);

                _closedSet.Add(current.cell);

                foreach (Vector2Int dir in Neighbors8)
                {
                    Vector2Int neighbor = new Vector2Int(current.cell.x + dir.x, current.cell.y + dir.y);

                    if (_closedSet.Contains(neighbor))
                        continue;
                    if (!MapGrid.Instance.IsCellFree(neighbor))
                        continue;
                    // Si no puede nadar, agua es intransitable
                    if (!canSwim && MapGrid.Instance.IsWater(neighbor))
                        continue;

                    float moveCost = IsDiagonal(current.cell, neighbor) ? DiagonalCost : 1f;
                    float terrainCost = MapGrid.Instance.GetTerrainCost(neighbor);
                    if (terrainCost <= 0 || terrainCost >= 1000f)
                        continue;
                    float tentativeG = current.gCost + moveCost * terrainCost;

                    if (!_nodes.TryGetValue(neighbor, out Node neighborNode) || tentativeG < neighborNode.gCost)
                    {
                        neighborNode = new Node
                        {
                            cell = neighbor,
                            gCost = tentativeG,
                            hCost = Heuristic(neighbor, goal),
                            parent = current.cell
                        };
                        _nodes[neighbor] = neighborNode;
                        _openSet.Enqueue(neighborNode, neighborNode.fCost);
                    }
                }
            }

            return PathResult.Failed($"No path found after {iterations} iterations");
        }

        private PathResult ReconstructPath(Vector2Int start, Vector2Int goal)
        {
            var path = new List<Vector2Int>();
            Vector2Int current = goal;

            while (current != start)
            {
                path.Add(current);
                if (!_nodes.TryGetValue(current, out Node n))
                    return PathResult.Failed("Path reconstruction failed");
                current = n.parent;
            }

            path.Reverse();
            return PathResult.Success(path);
        }

        private static float Heuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dz = Mathf.Abs(a.y - b.y);
            return Mathf.Min(dx, dz) * DiagonalCost + Mathf.Abs(dx - dz);
        }

        private static bool IsDiagonal(Vector2Int from, Vector2Int to)
        {
            return Mathf.Abs(from.x - to.x) == 1 && Mathf.Abs(from.y - to.y) == 1;
        }

    }

    public struct PathResult
    {
        public bool success;
        public List<Vector2Int> cells;
        public string error;

        public static PathResult Success(List<Vector2Int> cells)
            => new PathResult { success = true, cells = cells };

        public static PathResult Failed(string err)
            => new PathResult { success = false, error = err };
    }
}
