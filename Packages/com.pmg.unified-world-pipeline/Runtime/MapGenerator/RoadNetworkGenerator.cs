using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Qué rutas lógicas generar para planificar vados estratégicos (Fase9).</summary>
    [System.Flags]
    public enum StrategicFordPathBuildParts
    {
        None = 0,
        /// <summary>MST entre ciudades con A* que permite cruzar río (prioridad en mapas 2+ ciudades).</summary>
        MstLax = 1 << 0,
        /// <summary>Celdas de caminos Fase6 (solo tierra; suele no tener River).</summary>
        Phase6Roads = 1 << 1,
        /// <summary>Ciudad → punto representativo del mayor componente solo-Land.</summary>
        SyntheticToMainLand = 1 << 2,
        /// <summary>Ciudad → anclas del mapa (cantidad vía <see cref="MapGenConfig.riverCrossingStrategicAnchorCount"/>).</summary>
        SyntheticAnchors = 1 << 3,

        MstAndRoadsOnly = MstLax | Phase6Roads,
        SyntheticOnly = SyntheticToMainLand | SyntheticAnchors,
        All = MstLax | Phase6Roads | SyntheticToMainLand | SyntheticAnchors
    }

    /// <summary>Métricas de <see cref="RoadNetworkGenerator.BuildStrategicFordPlanningPaths"/>.</summary>
    public struct StrategicPathBuildStats
    {
        public double ElapsedMs;
        public int PathsMstLax;
        public int PathsPhase6Roads;
        public int PathsSyntheticMainLand;
        public int PathsSyntheticAnchors;
        public int TotalPaths;
    }

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

            var mstEdges = BuildPrimMstEdges(cities);

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

        /// <summary>
        /// Rutas lógicas invisibles (no se pintan): A* con río permitido (coste alto), lagos bloqueados.
        /// Usar <paramref name="parts"/> para fasear: primero <see cref="StrategicFordPathBuildParts.MstAndRoadsOnly"/>,
        /// luego sintéticas solo si aún faltan vados (ahorra A*).
        /// </summary>
        public static List<List<Vector2Int>> BuildStrategicFordPlanningPaths(
            GridSystem grid,
            List<CityNode> cities,
            List<Road> roads,
            MapGenConfig config,
            StrategicFordPathBuildParts parts,
            out StrategicPathBuildStats stats)
        {
            stats = default;
            var paths = new List<List<Vector2Int>>();
            if (grid == null || config == null)
                return paths;

            var sw = System.Diagnostics.Stopwatch.StartNew();

            if ((parts & StrategicFordPathBuildParts.MstLax) != 0
                && cities != null
                && cities.Count >= 2)
            {
                var mst = BuildPrimMstEdges(cities);
                foreach (var (fromId, toId) in mst)
                {
                    var path = FindPathAStar(grid, cities[fromId].Center, cities[toId].Center, PathMode.AllowRiverFord);
                    if (path != null && path.Count >= 2)
                    {
                        paths.Add(path);
                        stats.PathsMstLax++;
                    }
                }
            }

            if ((parts & StrategicFordPathBuildParts.Phase6Roads) != 0 && roads != null && roads.Count > 0)
            {
                foreach (var r in roads)
                {
                    if (r.PathCells == null || r.PathCells.Count < 2)
                        continue;
                    paths.Add(new List<Vector2Int>(r.PathCells));
                    stats.PathsPhase6Roads++;
                }
            }

            bool wantMain = (parts & StrategicFordPathBuildParts.SyntheticToMainLand) != 0;
            bool wantAnchors = (parts & StrategicFordPathBuildParts.SyntheticAnchors) != 0;
            int anchorBudget = config != null ? Mathf.Clamp(config.riverCrossingStrategicAnchorCount, 0, 4) : 0;

            if (cities != null && cities.Count > 0 && (wantMain || (wantAnchors && anchorBudget > 0)))
            {
                BuildLandOnlyComponents(grid, out var landComp, out int landCompCount, out var landSizes);
                int mainLand = -1;
                int bestSz = 0;
                for (int i = 0; i < landCompCount; i++)
                {
                    if (landSizes[i] > bestSz)
                    {
                        bestSz = landSizes[i];
                        mainLand = i;
                    }
                }

                var anchors = GetStrategicAnchorCells(grid, wantAnchors ? anchorBudget : 0);
                foreach (var city in cities)
                {
                    Vector2Int start = FindNearestLandWalkableCell(grid, city.Center, 24);
                    if (wantMain && mainLand >= 0)
                    {
                        Vector2Int goalMain = FindClosestCellInLandComponent(landComp, mainLand, start, grid.Width, grid.Height);
                        if (goalMain.x != int.MinValue)
                        {
                            var p = FindPathAStar(grid, start, goalMain, PathMode.AllowRiverFord);
                            if (p != null && p.Count >= 2)
                            {
                                paths.Add(p);
                                stats.PathsSyntheticMainLand++;
                            }
                        }
                    }

                    if (wantAnchors && anchorBudget > 0)
                    {
                        for (int a = 0; a < anchors.Count; a++)
                        {
                            Vector2Int goalA = FindNearestLandWalkableCell(grid, anchors[a], 32);
                            var p = FindPathAStar(grid, start, goalA, PathMode.AllowRiverFord);
                            if (p != null && p.Count >= 2)
                            {
                                paths.Add(p);
                                stats.PathsSyntheticAnchors++;
                            }
                        }
                    }
                }
            }

            sw.Stop();
            stats.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            stats.TotalPaths = paths.Count;
            return paths;
        }

        /// <summary>Compat: una sola fase con todas las partes.</summary>
        public static List<List<Vector2Int>> BuildStrategicFordPlanningPaths(
            GridSystem grid,
            List<CityNode> cities,
            List<Road> roads,
            MapGenConfig config)
        {
            return BuildStrategicFordPlanningPaths(grid, cities, roads, config, StrategicFordPathBuildParts.All, out _);
        }

        private static List<(int from, int to)> BuildPrimMstEdges(IReadOnlyList<CityNode> cities)
        {
            var mstEdges = new List<(int from, int to)>();
            if (cities == null || cities.Count < 2)
                return mstEdges;

            var edges = new List<(int from, int to, float cost)>();
            for (int i = 0; i < cities.Count; i++)
                for (int j = i + 1; j < cities.Count; j++)
                    edges.Add((i, j, Manhattan(cities[i].Center, cities[j].Center)));

            var inMst = new bool[cities.Count];
            inMst[0] = true;
            for (int k = 0; k < cities.Count - 1; k++)
            {
                float bestCost = float.MaxValue;
                int bestFrom = -1, bestTo = -1;
                foreach (var e in edges)
                {
                    bool fromIn = inMst[e.from];
                    bool toIn = inMst[e.to];
                    if (fromIn == toIn)
                        continue;
                    if (e.cost >= bestCost)
                        continue;
                    bestCost = e.cost;
                    bestFrom = e.from;
                    bestTo = e.to;
                }

                if (bestFrom < 0)
                    break;
                mstEdges.Add((bestFrom, bestTo));
                inMst[bestFrom] = true;
                inMst[bestTo] = true;
            }

            return mstEdges;
        }

        private static bool IsStrictLandWalkable(GridSystem grid, Vector2Int c)
        {
            if (!grid.InBoundsCell(c))
                return false;
            ref var cd = ref grid.GetCell(c);
            return cd.type == CellType.Land && cd.walkable;
        }

        private static void BuildLandOnlyComponents(GridSystem grid, out int[,] comp, out int compCount, out int[] sizes)
        {
            int w = grid.Width;
            int h = grid.Height;
            comp = new int[w, h];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    comp[x, z] = -1;

            compCount = 0;
            var q = new Queue<Vector2Int>();
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    var c = new Vector2Int(x, z);
                    if (comp[x, z] >= 0 || !IsStrictLandWalkable(grid, c))
                        continue;
                    int id = compCount++;
                    comp[x, z] = id;
                    q.Clear();
                    q.Enqueue(c);
                    while (q.Count > 0)
                    {
                        var p = q.Dequeue();
                        foreach (var n in grid.Neighbors4(p))
                        {
                            if (comp[n.x, n.y] >= 0 || !IsStrictLandWalkable(grid, n))
                                continue;
                            comp[n.x, n.y] = id;
                            q.Enqueue(n);
                        }
                    }
                }
            }

            sizes = new int[compCount];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    int id = comp[x, z];
                    if (id >= 0 && id < compCount)
                        sizes[id]++;
                }
        }

        private static Vector2Int FindNearestLandWalkableCell(GridSystem grid, Vector2Int start, int maxR)
        {
            if (IsStrictLandWalkable(grid, start))
                return start;
            for (int r = 1; r <= maxR; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r)
                        continue;
                    var c = new Vector2Int(start.x + dx, start.y + dz);
                    if (IsStrictLandWalkable(grid, c))
                        return c;
                }
            }

            return start;
        }

        private static Vector2Int FindClosestCellInLandComponent(int[,] comp, int targetId, Vector2Int from, int w, int h)
        {
            int best = int.MaxValue;
            var bestC = new Vector2Int(int.MinValue, int.MinValue);
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (comp[x, z] != targetId)
                        continue;
                    int d = Mathf.Abs(x - from.x) + Mathf.Abs(z - from.y);
                    if (d < best)
                    {
                        best = d;
                        bestC = new Vector2Int(x, z);
                    }
                }
            }

            return bestC;
        }

        private static List<Vector2Int> GetStrategicAnchorCells(GridSystem grid, int maxAnchors)
        {
            var list = new List<Vector2Int>(4);
            if (maxAnchors <= 0)
                return list;

            int w = grid.Width;
            int h = grid.Height;
            if (w < 2 || h < 2)
                return list;
            list.Add(new Vector2Int(Mathf.Max(0, w / 4), Mathf.Max(0, h / 4)));
            list.Add(new Vector2Int(Mathf.Clamp((3 * w) / 4, 0, w - 1), Mathf.Clamp((3 * h) / 4, 0, h - 1)));
            list.Add(new Vector2Int(Mathf.Max(0, w / 4), Mathf.Clamp((3 * h) / 4, 0, h - 1)));
            list.Add(new Vector2Int(Mathf.Clamp((3 * w) / 4, 0, w - 1), Mathf.Max(0, h / 4)));
            int take = Mathf.Min(maxAnchors, list.Count);
            if (take < list.Count)
                list.RemoveRange(take, list.Count - take);
            return list;
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
