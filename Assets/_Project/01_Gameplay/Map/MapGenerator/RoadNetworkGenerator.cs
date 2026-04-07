using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 6: conecta CityNodes con red de caminos. Grafo completo → MST (Prim) + A* en grid.</summary>
    public static class RoadNetworkGenerator
    {
        private enum PathMode
        {
            /// <summary>Solo tierra; bloquea agua, río y montaña.</summary>
            StrictLandOnly,
            /// <summary>Permite cruzar ríos con coste alto (vado / puente lógico).</summary>
            AllowRiverFord,
            /// <summary>Permite también lagos/agua con coste muy alto (último recurso para validación).</summary>
            AllowWaterBridge
        }

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
                var path = FindPathAStar(grid, from.Center, to.Center, PathMode.StrictLandOnly);
                if (path == null || path.Count == 0) continue;
                var road = new Road { FromCityId = from.Id, ToCityId = to.Id, PathCells = path };
                roads.Add(road);
                foreach (var c in path)
                {
                    ref var cell = ref grid.GetCell(c);
                    cell.roadLevel = 1;
                }
            }

            // Si el MST falló en alguna arista (A* imposible por agua), pueden quedar islas: reparar con rutas más permisivas.
            EnsureAllCitiesConnected(grid, cities, roads, config);

            int totalCells = 0;
            foreach (var r in roads) totalCells += r.PathCells.Count;
            if (config.debugLogs)
                Debug.Log($"Fase6 Caminos: MST con {mstEdges.Count} aristas, {roads.Count} caminos, {totalCells} celdas.");
            return roads;
        }

        /// <summary>Añade caminos extra entre componentes desconectadas (vado/puente sobre río o agua si hace falta).</summary>
        private static void EnsureAllCitiesConnected(GridSystem grid, List<CityNode> cities, List<Road> roads, MapGenConfig config)
        {
            const int maxExtraRoads = 64;
            int added = 0;
            while (added < maxExtraRoads)
            {
                var components = GetCityComponents(cities, roads);
                if (components.Count <= 1) return;

                bool linked = false;
                var compA = components[0];
                for (int ci = 1; ci < components.Count && !linked; ci++)
                {
                    var compB = components[ci];
                    foreach (int idA in compA)
                    {
                        CityNode cityA = null;
                        foreach (var c in cities) { if (c.Id == idA) { cityA = c; break; } }
                        if (cityA == null) continue;

                        foreach (int idB in compB)
                        {
                            CityNode cityB = null;
                            foreach (var c in cities) { if (c.Id == idB) { cityB = c; break; } }
                            if (cityB == null) continue;

                            List<Vector2Int> path = FindPathAStar(grid, cityA.Center, cityB.Center, PathMode.AllowRiverFord);
                            if (path == null || path.Count == 0)
                                path = FindPathAStar(grid, cityA.Center, cityB.Center, PathMode.AllowWaterBridge);
                            if (path == null || path.Count == 0) continue;

                            roads.Add(new Road { FromCityId = cityA.Id, ToCityId = cityB.Id, PathCells = path });
                            foreach (var cell in path)
                            {
                                ref var cd = ref grid.GetCell(cell);
                                cd.roadLevel = 1;
                            }
                            linked = true;
                            added++;
                            if (config.debugLogs)
                                Debug.Log($"Fase6 Caminos: reparación conectó ciudad {cityA.Id}↔{cityB.Id} (vado/puente).");
                            break;
                        }
                        if (linked) break;
                    }
                }

                if (!linked) return;
            }
        }

        private static List<List<int>> GetCityComponents(List<CityNode> cities, List<Road> roads)
        {
            var adj = new Dictionary<int, List<int>>();
            foreach (var c in cities) adj[c.Id] = new List<int>();
            foreach (var r in roads ?? new List<Road>())
            {
                if (adj.ContainsKey(r.FromCityId) && adj.ContainsKey(r.ToCityId))
                {
                    adj[r.FromCityId].Add(r.ToCityId);
                    adj[r.ToCityId].Add(r.FromCityId);
                }
            }

            var visited = new HashSet<int>();
            var result = new List<List<int>>();
            foreach (var city in cities)
            {
                if (visited.Contains(city.Id)) continue;
                var comp = new List<int>();
                var q = new Queue<int>();
                q.Enqueue(city.Id);
                visited.Add(city.Id);
                while (q.Count > 0)
                {
                    int id = q.Dequeue();
                    comp.Add(id);
                    foreach (int n in adj[id])
                    {
                        if (!visited.Contains(n)) { visited.Add(n); q.Enqueue(n); }
                    }
                }
                result.Add(comp);
            }
            return result;
        }

        private static float Manhattan(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }

        /// <summary>A* en grid. Montaña siempre bloqueada. Modos laxos permiten río/agua con coste alto (heurística Manhattan×1 admisible).</summary>
        private static List<Vector2Int> FindPathAStar(GridSystem grid, Vector2Int start, Vector2Int goal, PathMode mode)
        {
            const float costLand = 1f;
            const float costRiverFord = 10f;
            const float costWaterBridge = 28f;

            float MoveCost(CellType t)
            {
                switch (t)
                {
                    case CellType.Mountain:
                        return -1f;
                    case CellType.Land:
                        return costLand;
                    case CellType.River:
                        if (mode == PathMode.StrictLandOnly) return -1f;
                        return costRiverFord;
                    case CellType.Water:
                        if (mode != PathMode.AllowWaterBridge) return -1f;
                        return costWaterBridge;
                    default:
                        return costLand;
                }
            }

            var open = new List<Vector2Int> { start };
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, float>();
            var fScore = new Dictionary<Vector2Int, float>();
            gScore[start] = 0f;
            fScore[start] = Manhattan(start, goal);

            while (open.Count > 0)
            {
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
                    float stepCost = MoveCost(cell.type);
                    if (stepCost < 0f) continue;

                    float tentativeG = (gScore.ContainsKey(current) ? gScore[current] : float.MaxValue) + stepCost;
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
