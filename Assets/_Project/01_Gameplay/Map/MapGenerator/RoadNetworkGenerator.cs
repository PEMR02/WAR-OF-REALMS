using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 6: conecta CityNodes con red de caminos. Grafo completo → MST (Prim) + A* en grid.</summary>
    public static class RoadNetworkGenerator
    {
        /// <summary>Parámetros: config.roadWidthCells. Retorna lista de Road con PathCells. Marca roadLevel en celdas.</summary>
        public static List<Road> BuildRoads(GridSystem grid, List<CityNode> cities, MapGenConfig config)
        {
            var roads = new List<Road>();
            if (grid == null || cities == null || cities.Count < 2 || config == null) return roads;

            // Grafo: todas las aristas (i, j) con coste = distancia en celdas (o Manhattan)
            var edges = new List<(int from, int to, float cost)>();
            for (int i = 0; i < cities.Count; i++)
                for (int j = i + 1; j < cities.Count; j++)
                {
                    float cost = Manhattan(cities[i].Center, cities[j].Center);
                    edges.Add((i, j, cost));
                }

            // MST con Prim: empezar con ciudad 0, ir añadiendo la arista de menor coste que conecte un nodo del MST con uno fuera
            var inMst = new bool[cities.Count];
            inMst[0] = true;
            var mstEdges = new List<(int from, int to)>();

            for (int k = 0; k < cities.Count - 1; k++)
            {
                float bestCost = float.MaxValue;
                int bestFrom = -1, bestTo = -1;
                foreach (var e in edges)
                {
                    bool fromIn = inMst[e.from];
                    bool toIn = inMst[e.to];
                    if (fromIn == toIn) continue; // ambas dentro o ambas fuera
                    if (e.cost >= bestCost) continue;
                    bestCost = e.cost;
                    bestFrom = e.from;
                    bestTo = e.to;
                }
                if (bestFrom < 0) break;
                mstEdges.Add((bestFrom, bestTo));
                inMst[bestFrom] = true;
                inMst[bestTo] = true;
            }

            // Para cada arista del MST, path A* entre centros y marcar roadLevel
            foreach (var (fromId, toId) in mstEdges)
            {
                var from = cities[fromId];
                var to = cities[toId];
                var path = FindPathAStar(grid, from.Center, to.Center);
                if (path == null || path.Count == 0) continue;
                var road = new Road { FromCityId = from.Id, ToCityId = to.Id, PathCells = path };
                roads.Add(road);
                foreach (var c in path)
                {
                    ref var cell = ref grid.GetCell(c);
                    cell.roadLevel = 1;
                }
            }

            int totalCells = 0;
            foreach (var r in roads) totalCells += r.PathCells.Count;
            if (config.debugLogs)
                Debug.Log($"Fase6 Caminos: MST con {mstEdges.Count} aristas, {roads.Count} caminos, {totalCells} celdas.");
            return roads;
        }

        private static float Manhattan(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }

        /// <summary>A* en grid: evita Water/River/Mountain. Coste 1 por paso; heurística Manhattan.</summary>
        private static List<Vector2Int> FindPathAStar(GridSystem grid, Vector2Int start, Vector2Int goal)
        {
            var open = new List<Vector2Int> { start };
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, float>();
            var fScore = new Dictionary<Vector2Int, float>();
            gScore[start] = 0f;
            fScore[start] = Manhattan(start, goal);

            while (open.Count > 0)
            {
                // Nodo con menor fScore
                int bestIdx = 0;
                float bestF = fScore.ContainsKey(open[0]) ? fScore[open[0]] : float.MaxValue;
                for (int i = 1; i < open.Count; i++)
                {
                    float f = fScore.ContainsKey(open[i]) ? fScore[open[i]] : float.MaxValue;
                    if (f < bestF) { bestF = f; bestIdx = i; }
                }
                var current = open[bestIdx];
                open.RemoveAt(bestIdx);

                if (current == goal)
                    return ReconstructPath(cameFrom, start, goal);

                foreach (var n in grid.Neighbors4(current.x, current.y))
                {
                    ref var cell = ref grid.GetCell(n);
                    if (cell.type == CellType.Water || cell.type == CellType.River || cell.type == CellType.Mountain)
                        continue;

                    float tentativeG = (gScore.ContainsKey(current) ? gScore[current] : float.MaxValue) + 1f;
                    float nG = gScore.ContainsKey(n) ? gScore[n] : float.MaxValue;
                    if (tentativeG >= nG) continue;

                    cameFrom[n] = current;
                    gScore[n] = tentativeG;
                    fScore[n] = tentativeG + Manhattan(n, goal);
                    if (!open.Contains(n)) open.Add(n);
                }
            }
            return null;
        }

        private static List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int start, Vector2Int goal)
        {
            var path = new List<Vector2Int>();
            var current = goal;
            while (true)
            {
                path.Add(current);
                if (current == start) break;
                if (!cameFrom.TryGetValue(current, out current)) break;
            }
            path.Reverse();
            return path;
        }
    }
}
