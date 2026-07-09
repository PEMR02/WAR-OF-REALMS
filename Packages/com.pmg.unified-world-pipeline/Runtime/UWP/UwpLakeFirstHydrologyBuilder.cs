using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Pipeline experimental lake-first: main → lagos lejos del main → tributario outlet→confluencia
    /// con validación de conectividad antes de rasterizar. Sin post-fixes visuales.
    /// </summary>
    public static class UwpLakeFirstHydrologyBuilder
    {
        const int MaxTributarySlots = 7;

        static readonly int[] NeighborDx = { -1, 1, 0, 0, -1, 1, -1, 1 };
        static readonly int[] NeighborDz = { 0, 0, -1, 1, -1, -1, 1, 1 };

        public static UwpWaterGraph BuildAndApply(
            GridSystem grid,
            MapGenConfig config,
            IRng rng,
            int targetRiverCount,
            ref int waterCells,
            HashSet<long> riverOccupiedCells,
            ref bool riverOccAabbValid,
            ref int riverOccMinX,
            ref int riverOccMaxX,
            ref int riverOccMinZ,
            ref int riverOccMaxZ)
        {
            var graph = new UwpWaterGraph { Seed = config != null ? config.seed : 0 };
            if (grid == null || config == null || rng == null)
                return graph;

            grid.LakeFirstWaterGraph = graph;
            grid.ClearRiverVisualSurfaceCache();
            RiverConfluenceUtility.Clear(grid);

            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
            {
                LogSeed(config, "abort=no_main_river");
                return graph;
            }

            TrimExtraCenterlines(grid);
            var mainCl = grid.RiverCenterlinesCellSpace[0];
            if (mainCl == null || mainCl.Count < 2)
            {
                LogSeed(config, "abort=main_centerline_invalid");
                return graph;
            }

            graph.MainCenterlineCells = new List<Vector2>(mainCl);
            graph.FinalCenterlineByRiverIndex[0] = new List<Vector2>(mainCl);

            var lakeComponents = SelectIntentionalLakeComponents(grid, config, out int rawLakeComponents);
            graph.Report.LakeCandidates = rawLakeComponents;

            int minLakeDist = config.lakeValidateSeparationFromMainRiver
                ? config.lakeMinChebyshevDistanceFromMainRiverCells
                : 8;
            int minTribCells = Mathf.Max(14, config.riverVisualMinSurfacePieceLengthCells * 2);
            int minFromMainEnds = Mathf.Max(4, config.riverConfluenceMinDistanceFromMainEndpointsCells);
            int maxTribSlots = Mathf.Clamp(targetRiverCount - 1, 0, MaxTributarySlots);
            int nextRiverIndex = 1;

            grid.LakeBodyComponents = lakeComponents;
            grid.LakeComponentTributaryOwnerRiverIndex = new List<int>(lakeComponents.Count);
            for (int i = 0; i < lakeComponents.Count; i++)
                grid.LakeComponentTributaryOwnerRiverIndex.Add(-1);

            var claimedConfluenceCells = new HashSet<long>();
            var tributaryPlacementOrder = new List<(int compIdx, int distMain)>();

            for (int compIdx = 0; compIdx < lakeComponents.Count; compIdx++)
            {
                var comp = lakeComponents[compIdx];
                var lakeNode = new UwpLakeGraphNode
                {
                    ComponentIndex = compIdx,
                    CellCount = comp.Count,
                    BodyCellsPacked = comp
                };

                if (comp.Count == 0)
                {
                    lakeNode.RejectReason = "empty_component";
                    graph.Lakes.Add(lakeNode);
                    graph.Report.LakesRejected++;
                    graph.Report.LakeRejectLines.Add($"comp={compIdx} reason=empty_component");
                    continue;
                }

                ComputeComponentSeed(comp, out lakeNode.SeedCellX, out lakeNode.SeedCellZ);
                lakeNode.DistanceToMainCells = MinChebyshevComponentToMain(comp, mainCl, grid.Width, grid.Height);
                graph.Lakes.Add(lakeNode);

                if (config.lakeValidateSeparationFromMainRiver &&
                    lakeNode.DistanceToMainCells < minLakeDist)
                {
                    lakeNode.Accepted = true;
                    lakeNode.RejectReason = "too_close_to_main_standalone";
                    graph.Report.LakesAccepted++;
                    graph.Report.LakeRejectLines.Add(
                        $"comp={compIdx} reason=too_close_to_main_standalone dist={lakeNode.DistanceToMainCells} min={minLakeDist} trib=skipped");
                    continue;
                }

                lakeNode.Accepted = true;
                graph.Report.LakesAccepted++;

                if (!TryBuildLakeOutlet(comp, mainCl, grid, out Vector2Int outlet, out string outletFail))
                {
                    graph.Report.TributaryRejectLines.Add($"comp={compIdx} reason=outlet_{outletFail}");
                    continue;
                }

                lakeNode.OutletCell = outlet;
                lakeNode.OutletValid = true;
                tributaryPlacementOrder.Add((compIdx, lakeNode.DistanceToMainCells));
            }

            tributaryPlacementOrder.Sort((a, b) => b.distMain.CompareTo(a.distMain));
            for (int tribPass = 0; tribPass < 2 && nextRiverIndex <= maxTribSlots; tribPass++)
            {
                if (tribPass == 1)
                    tributaryPlacementOrder.Sort((a, b) => a.distMain.CompareTo(b.distMain));

                for (int ti = 0; ti < tributaryPlacementOrder.Count; ti++)
                {
                    if (nextRiverIndex > maxTribSlots)
                        break;

                    int compIdx = tributaryPlacementOrder[ti].compIdx;
                    var comp = lakeComponents[compIdx];
                    var lakeNode = graph.Lakes[compIdx];
                    if (lakeNode.OwnerTributaryRiverIndex >= 0)
                        continue;

                    TryPlaceTributaryForLake(
                        grid,
                        config,
                        rng,
                        comp,
                        compIdx,
                        mainCl,
                        lakeNode.OutletCell,
                        minFromMainEnds,
                        minTribCells,
                        claimedConfluenceCells,
                        ref waterCells,
                        riverOccupiedCells,
                        ref riverOccAabbValid,
                        ref riverOccMinX,
                        ref riverOccMaxX,
                        ref riverOccMinZ,
                        ref riverOccMaxZ,
                        ref nextRiverIndex,
                        maxTribSlots,
                        lakeNode,
                        graph,
                        confluenceWindowCells: tribPass == 0 ? 4 : 6);
                }
            }

            graph.Report.FinalRiverCount = grid.RiverCenterlinesCellSpace?.Count ?? 0;
            graph.Report.FinalConnectivityOk = AuditFinalConnectivity(graph, grid);
            EmitSeedLogs(config, graph);
            return graph;
        }

        static bool TryPlaceTributaryForLake(
            GridSystem grid,
            MapGenConfig config,
            IRng rng,
            HashSet<long> comp,
            int compIdx,
            List<Vector2> mainCl,
            Vector2Int outlet,
            int minFromMainEnds,
            int minTribCells,
            HashSet<long> claimedConfluenceCells,
            ref int waterCells,
            HashSet<long> riverOccupiedCells,
            ref bool riverOccAabbValid,
            ref int riverOccMinX,
            ref int riverOccMaxX,
            ref int riverOccMinZ,
            ref int riverOccMaxZ,
            ref int nextRiverIndex,
            int maxTribSlots,
            UwpLakeGraphNode lakeNode,
            UwpWaterGraph graph,
            int confluenceWindowCells = 4)
        {
            if (nextRiverIndex > maxTribSlots)
                return false;

            var confluenceCandidates = CollectConfluenceCandidates(
                mainCl, outlet, grid, minFromMainEnds, claimedConfluenceCells, rng, compIdx);
            if (confluenceCandidates.Count == 0)
            {
                graph.Report.TributaryRejectLines.Add($"comp={compIdx} reason=confluence_no_valid_confluence");
                graph.Report.TributariesRejected++;
                return false;
            }

            foreach (var cand in confluenceCandidates)
            {
                if (!CollectReachableConfluenceGoals(
                        grid, mainCl, cand.mainClIdx, confluenceWindowCells, comp, out List<Vector2Int> goals) ||
                    goals == null || goals.Count == 0)
                {
                    continue;
                }

                var goalPacked = new HashSet<long>(goals.Count);
                for (int gi = 0; gi < goals.Count; gi++)
                {
                    var g = goals[gi];
                    goalPacked.Add(PackCell(g.x, g.y));
                }

                if (!TryBuildTributaryPath(
                        grid,
                        outlet,
                        goalPacked,
                        comp,
                        minTribCells,
                        out List<Vector2Int> pathCells,
                        out Vector2Int confluence,
                        out string pathFail))
                {
                    continue;
                }

                var tribEdge = new UwpTributaryGraphEdge
                {
                    LakeComponentIndex = compIdx,
                    LakeOutletCell = outlet,
                    MainRiverConfluenceCell = confluence,
                    MainCenterlineIndex = cand.mainClIdx,
                    DistanceLakeToMainCells = Chebyshev(outlet, confluence)
                };

                tribEdge.PathCells = pathCells;
                tribEdge.CenterlineCells = PathToCenterline(pathCells);
                ApplyLakeFirstValidatedTributaryModifiers(
                    tribEdge.CenterlineCells,
                    comp,
                    outlet,
                    nextRiverIndex,
                    grid.Width,
                    grid.Height);
                PinLakeFirstTributaryEndpointConfluence(tribEdge.CenterlineCells, confluence);
                var organicPathCells = BuildRasterPathFromCenterline(tribEdge.CenterlineCells, grid.Width, grid.Height);
                if (organicPathCells != null && organicPathCells.Count >= 2)
                {
                    if (organicPathCells[organicPathCells.Count - 1].x != confluence.x ||
                        organicPathCells[organicPathCells.Count - 1].y != confluence.y)
                    {
                        organicPathCells.Add(confluence);
                    }

                    tribEdge.PathCells = organicPathCells;
                }
                tribEdge.DebugCarvePathCells = new List<Vector2>(tribEdge.CenterlineCells);

                if (!ValidateConnectivity(grid, comp, outlet, tribEdge.PathCells, confluence, mainCl))
                {
                    tribEdge.RejectReason = "connectivity_failed";
                    tribEdge.ConnectivityValid = false;
                    tribEdge.Accepted = false;
                    graph.Tributaries.Add(tribEdge);
                    graph.Report.TributariesRejected++;
                    graph.Report.TributaryRejectLines.Add($"comp={compIdx} reason=connectivity_failed");
                    continue;
                }

                tribEdge.ConnectivityValid = true;
                int riverSlot = nextRiverIndex;
                int addedCells = WaterGenerator.ApplyLakeFirstValidatedTributary(
                    grid,
                    config,
                    rng,
                    riverSlot,
                    tribEdge.PathCells,
                    tribEdge.CenterlineCells,
                    riverOccupiedCells,
                    ref riverOccAabbValid,
                    ref riverOccMinX,
                    ref riverOccMaxX,
                    ref riverOccMinZ,
                    ref riverOccMaxZ);

                if (addedCells <= 0)
                {
                    tribEdge.RejectReason = "raster_failed";
                    tribEdge.Accepted = false;
                    graph.Tributaries.Add(tribEdge);
                    graph.Report.TributariesRejected++;
                    graph.Report.TributaryRejectLines.Add($"comp={compIdx} reason=raster_failed");
                    continue;
                }

                waterCells += addedCells;
                tribEdge.RiverIndex = riverSlot;
                tribEdge.Accepted = true;
                graph.Tributaries.Add(tribEdge);
                graph.Report.TributariesAccepted++;

                graph.FinalCenterlineByRiverIndex[riverSlot] = new List<Vector2>(tribEdge.CenterlineCells);
                grid.LakeComponentTributaryOwnerRiverIndex[compIdx] = riverSlot;
                lakeNode.OwnerTributaryRiverIndex = riverSlot;
                claimedConfluenceCells.Add(PackCell(confluence.x, confluence.y));

                RiverConfluenceUtility.TryRegisterFromPlacement(
                    grid,
                    config,
                    riverSlot,
                    tribEdge.PathCells,
                    tribEdge.CenterlineCells,
                    confluence,
                    "lake_first_pipeline");

                nextRiverIndex++;
                return true;
            }

            graph.Report.TributariesRejected++;
            graph.Report.TributaryRejectLines.Add($"comp={compIdx} reason=path_astar_no_path");
            return false;
        }

        static void TrimExtraCenterlines(GridSystem grid)
        {
            if (grid.RiverCenterlinesCellSpace == null)
                return;
            while (grid.RiverCenterlinesCellSpace.Count > 1)
            {
                int last = grid.RiverCenterlinesCellSpace.Count - 1;
                grid.RiverCenterlinesCellSpace.RemoveAt(last);
                if (grid.RiverCenterlinesWorld != null && grid.RiverCenterlinesWorld.Count > last)
                    grid.RiverCenterlinesWorld.RemoveAt(last);
            }
        }

        static List<HashSet<long>> SelectIntentionalLakeComponents(
            GridSystem grid,
            MapGenConfig config,
            out int rawComponentCount)
        {
            rawComponentCount = 0;
            var all = BuildLakeBodyComponents(grid);
            rawComponentCount = all.Count;
            if (all.Count == 0 || config == null)
                return all;

            int maxLakes = Mathf.Clamp(config.lakeCount, 0, 12);
            if (maxLakes <= 0)
                return new List<HashSet<long>>();

            int minCells = Mathf.Max(24, config.maxLakeCells / 16);
            var ranked = new List<HashSet<long>>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Count >= minCells)
                    ranked.Add(all[i]);
            }

            ranked.Sort((a, b) => b.Count.CompareTo(a.Count));
            var selected = new List<HashSet<long>>();
            for (int i = 0; i < ranked.Count && selected.Count < maxLakes; i++)
                selected.Add(ranked[i]);
            return selected;
        }

        static List<HashSet<long>> BuildLakeBodyComponents(GridSystem grid)
        {
            var components = new List<HashSet<long>>();
            if (grid?.LakeBodyCellsPacked == null || grid.LakeBodyCellsPacked.Count == 0)
                return components;

            var visited = new HashSet<long>();
            foreach (long pk in grid.LakeBodyCellsPacked)
            {
                if (!visited.Add(pk))
                    continue;
                var comp = new HashSet<long> { pk };
                var q = new Queue<long>();
                q.Enqueue(pk);
                while (q.Count > 0)
                {
                    long cur = q.Dequeue();
                    int x = (int)(cur >> 32);
                    int z = (int)(uint)cur;
                    for (int ni = 0; ni < 4; ni++)
                    {
                        int nx = x + NeighborDx[ni];
                        int nz = z + NeighborDz[ni];
                        if (!grid.InBoundsCell(nx, nz))
                            continue;
                        long nk = PackCell(nx, nz);
                        if (!grid.LakeBodyCellsPacked.Contains(nk) || !visited.Add(nk))
                            continue;
                        comp.Add(nk);
                        q.Enqueue(nk);
                    }
                }

                if (comp.Count > 0)
                    components.Add(comp);
            }

            return components;
        }

        static void ComputeComponentSeed(HashSet<long> comp, out int sx, out int sz)
        {
            sx = 0;
            sz = 0;
            if (comp == null || comp.Count == 0)
                return;
            foreach (long pk in comp)
            {
                sx = (int)(pk >> 32);
                sz = (int)(uint)pk;
                return;
            }
        }

        static void RemoveLakeComponentFromGrid(GridSystem grid, HashSet<long> comp, ref int waterCells)
        {
            if (grid == null || comp == null)
                return;
            foreach (long pk in comp)
            {
                int x = (int)(pk >> 32);
                int z = (int)(uint)pk;
                if (!grid.InBoundsCell(x, z))
                    continue;
                ref var cell = ref grid.GetCell(x, z);
                if (cell.type == CellType.Water)
                {
                    cell.type = CellType.Land;
                    cell.walkable = true;
                    cell.buildable = true;
                    cell.waterTraverse = WaterTraverseMode.NotWater;
                    waterCells = Mathf.Max(0, waterCells - 1);
                }

                grid.LakeBodyCellsPacked?.Remove(pk);
            }
        }

        static bool TryBuildLakeOutlet(
            HashSet<long> comp,
            List<Vector2> mainCl,
            GridSystem grid,
            out Vector2Int outlet,
            out string fail)
        {
            outlet = default;
            fail = null;
            if (comp == null || comp.Count == 0 || mainCl == null || grid == null)
            {
                fail = "invalid_args";
                return false;
            }

            float bestScore = float.MaxValue;
            bool found = false;
            Vector2 mainCentroid = ComputePolylineCentroid(mainCl);
            Vector2 lakeCentroid = ComputeComponentCentroid(comp);

            foreach (long pk in comp)
            {
                int lx = (int)(pk >> 32);
                int lz = (int)(uint)pk;
                for (int ni = 0; ni < 4; ni++)
                {
                    int nx = lx + NeighborDx[ni];
                    int nz = lz + NeighborDz[ni];
                    if (!grid.InBoundsCell(nx, nz))
                        continue;
                    ref var ncell = ref grid.GetCell(nx, nz);
                    if (ncell.type != CellType.Land)
                        continue;
                    if (IsOnMainRiverCell(nx, nz, mainCl))
                        continue;

                    float distMain = MinChebyshevPointToMain(new Vector2(nx + 0.5f, nz + 0.5f), mainCl, grid.Width, grid.Height);
                    Vector2 shore = new Vector2(nx + 0.5f, nz + 0.5f);
                    float awayFromMain = Vector2.Distance(shore, mainCentroid);
                    Vector2 fromLake = shore - new Vector2(lx + 0.5f, lz + 0.5f);
                    if (fromLake.sqrMagnitude > 1e-6f)
                        fromLake.Normalize();
                    Vector2 toMain = mainCentroid - shore;
                    if (toMain.sqrMagnitude > 1e-6f)
                        toMain.Normalize();
                    float faceMain = fromLake.sqrMagnitude > 1e-6f && toMain.sqrMagnitude > 1e-6f
                        ? Vector2.Dot(fromLake, toMain)
                        : 0f;
                    Vector2 exitFromCentroid = shore - lakeCentroid;
                    if (exitFromCentroid.sqrMagnitude > 1e-6f)
                        exitFromCentroid.Normalize();
                    Vector2 lakeToMain = mainCentroid - lakeCentroid;
                    if (lakeToMain.sqrMagnitude > 1e-6f)
                        lakeToMain.Normalize();
                    float exitAlign = exitFromCentroid.sqrMagnitude > 1e-6f && lakeToMain.sqrMagnitude > 1e-6f
                        ? Vector2.Dot(exitFromCentroid, lakeToMain)
                        : 0f;
                    float score = distMain * 0.28f - awayFromMain * 0.55f - faceMain * 2.6f - exitAlign * 4.2f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        outlet = new Vector2Int(nx, nz);
                        found = true;
                    }
                }
            }

            if (!found)
                fail = "no_shore_land";
            return found;
        }

        static List<(Vector2Int cell, int mainClIdx)> CollectConfluenceCandidates(
            List<Vector2> mainCl,
            Vector2Int outlet,
            GridSystem grid,
            int minFromEnds,
            HashSet<long> claimed,
            IRng rng,
            int compIdx)
        {
            var result = new List<(Vector2Int cell, int mainClIdx, int dist)>();
            if (mainCl == null || mainCl.Count < minFromEnds * 2 + 2 || grid == null)
                return new List<(Vector2Int, int)>();

            int w = grid.Width;
            int h = grid.Height;
            int minDist = Mathf.Max(8, minFromEnds);
            int maxDist = Mathf.Max(minDist + 12, Mathf.Min(w, h) * 2 / 3);
            int start = minFromEnds;
            int end = mainCl.Count - minFromEnds - 1;

            for (int idx = start; idx <= end; idx++)
            {
                Vector2 p = mainCl[idx];
                int cx = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, w - 1);
                int cz = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, h - 1);
                long pk = PackCell(cx, cz);
                if (claimed.Contains(pk))
                    continue;

                int dist = Chebyshev(outlet, new Vector2Int(cx, cz));
                if (dist < minDist || dist > maxDist)
                    continue;
                if (!IsOnMainRiverCell(cx, cz, mainCl))
                    continue;

                result.Add((new Vector2Int(cx, cz), idx, dist));
            }

            result.Sort((a, b) =>
            {
                int cmp = a.dist.CompareTo(b.dist);
                if (cmp != 0)
                    return cmp;
                return (compIdx + (rng != null ? rng.NextInt(0, 3) : 0) + a.mainClIdx).CompareTo(b.mainClIdx);
            });

            var slim = new List<(Vector2Int cell, int mainClIdx)>(result.Count);
            for (int i = 0; i < result.Count; i++)
                slim.Add((result[i].cell, result[i].mainClIdx));
            return slim;
        }

        static bool CollectReachableConfluenceGoals(
            GridSystem grid,
            List<Vector2> mainCl,
            int centerIdx,
            int windowCells,
            HashSet<long> lakeComp,
            out List<Vector2Int> goals)
        {
            goals = new List<Vector2Int>();
            if (grid == null || mainCl == null || mainCl.Count == 0)
                return false;

            int lo = Mathf.Max(0, centerIdx - windowCells);
            int hi = Mathf.Min(mainCl.Count - 1, centerIdx + windowCells);
            var seen = new HashSet<long>();
            for (int idx = lo; idx <= hi; idx++)
            {
                Vector2 p = mainCl[idx];
                int cx = Mathf.FloorToInt(p.x);
                int cz = Mathf.FloorToInt(p.y);
                TryAddConfluenceGoal(grid, cx, cz, lakeComp, seen, goals);
                TryAddConfluenceGoal(grid, cx + 1, cz, lakeComp, seen, goals);
                TryAddConfluenceGoal(grid, cx - 1, cz, lakeComp, seen, goals);
                TryAddConfluenceGoal(grid, cx, cz + 1, lakeComp, seen, goals);
                TryAddConfluenceGoal(grid, cx, cz - 1, lakeComp, seen, goals);
            }

            return goals.Count > 0;
        }

        static void TryAddConfluenceGoal(
            GridSystem grid,
            int x,
            int z,
            HashSet<long> lakeComp,
            HashSet<long> seen,
            List<Vector2Int> goals)
        {
            if (!grid.InBoundsCell(x, z))
                return;
            long pk = PackCell(x, z);
            if (!seen.Add(pk))
                return;
            if (lakeComp.Contains(pk))
                return;
            ref var cell = ref grid.GetCell(x, z);
            if (cell.type != CellType.River)
                return;
            if (!HasLandApproachToCell(grid, x, z, lakeComp))
                return;
            goals.Add(new Vector2Int(x, z));
        }

        static bool HasLandApproachToCell(GridSystem grid, int x, int z, HashSet<long> lakeComp)
        {
            for (int ni = 0; ni < 4; ni++)
            {
                int nx = x + NeighborDx[ni];
                int nz = z + NeighborDz[ni];
                if (!grid.InBoundsCell(nx, nz))
                    continue;
                long nk = PackCell(nx, nz);
                if (lakeComp.Contains(nk))
                    continue;
                if (grid.GetCell(nx, nz).type == CellType.Land)
                    return true;
            }

            return false;
        }

        static bool TryBuildTributaryPath(
            GridSystem grid,
            Vector2Int start,
            HashSet<long> goalCells,
            HashSet<long> lakeComp,
            int minCells,
            out List<Vector2Int> path,
            out Vector2Int goalUsed,
            out string fail)
        {
            path = null;
            goalUsed = default;
            fail = null;
            if (grid == null || goalCells == null || goalCells.Count == 0)
            {
                fail = "no_goals";
                return false;
            }

            var closed = new HashSet<long>();
            var open = new List<(long pk, int g, int f)>();
            var cameFrom = new Dictionary<long, long>();
            var gScore = new Dictionary<long, int>();
            Vector2Int primaryGoal = start;
            int bestHeuristic = int.MaxValue;
            foreach (long gpk in goalCells)
            {
                int gx = (int)(gpk >> 32);
                int gz = (int)(uint)gpk;
                var g = new Vector2Int(gx, gz);
                int goalH = Heuristic(start, g);
                if (goalH < bestHeuristic)
                {
                    bestHeuristic = goalH;
                    primaryGoal = g;
                }
            }

            long startPk = PackCell(start.x, start.y);
            open.Add((startPk, 0, Heuristic(start, primaryGoal)));
            gScore[startPk] = 0;

            while (open.Count > 0)
            {
                open.Sort((a, b) => a.f.CompareTo(b.f));
                var current = open[0];
                open.RemoveAt(0);
                if (!closed.Add(current.pk))
                    continue;

                int cx = (int)(current.pk >> 32);
                int cz = (int)(uint)current.pk;
                if (goalCells.Contains(current.pk))
                {
                    path = ReconstructPath(cameFrom, current.pk);
                    goalUsed = new Vector2Int(cx, cz);
                    if (path == null || path.Count < minCells)
                    {
                        fail = "path_too_short";
                        return false;
                    }

                    return true;
                }

                for (int ni = 0; ni < 8; ni++)
                {
                    int nx = cx + NeighborDx[ni];
                    int nz = cz + NeighborDz[ni];
                    if (!grid.InBoundsCell(nx, nz))
                        continue;
                    long nk = PackCell(nx, nz);
                    if (closed.Contains(nk))
                        continue;
                    if (lakeComp.Contains(nk))
                        continue;

                    bool isGoal = goalCells.Contains(nk);
                    ref var cell = ref grid.GetCell(nx, nz);
                    if (!isGoal)
                    {
                        if (cell.type != CellType.Land)
                            continue;
                    }
                    else if (cell.type != CellType.River && cell.type != CellType.Land)
                    {
                        continue;
                    }

                    int stepCost = ni >= 4 ? 14 : 10;
                    int tentative = current.g + stepCost;
                    if (gScore.TryGetValue(nk, out int prev) && tentative >= prev)
                        continue;
                    gScore[nk] = tentative;
                    cameFrom[nk] = current.pk;
                    int f = tentative + Heuristic(new Vector2Int(nx, nz), primaryGoal);
                    open.Add((nk, tentative, f));
                }
            }

            fail = "astar_no_path";
            return false;
        }

        static List<Vector2Int> ReconstructPath(Dictionary<long, long> cameFrom, long current)
        {
            var path = new List<Vector2Int>();
            while (true)
            {
                int x = (int)(current >> 32);
                int z = (int)(uint)current;
                path.Add(new Vector2Int(x, z));
                if (!cameFrom.TryGetValue(current, out long prev))
                    break;
                current = prev;
            }

            path.Reverse();
            return path;
        }

        static List<Vector2> PathToCenterline(List<Vector2Int> path)
        {
            var cl = new List<Vector2>(path.Count);
            for (int i = 0; i < path.Count; i++)
            {
                var c = path[i];
                cl.Add(new Vector2(c.x + 0.5f, c.y + 0.5f));
            }

            return cl;
        }

        /// <summary>Modificadores orgánicos aplicados tras validar conectividad y antes de raster/carve.</summary>
        static void ApplyLakeFirstValidatedTributaryModifiers(
            List<Vector2> centerline,
            HashSet<long> comp,
            Vector2Int outlet,
            int riverSlot,
            int w,
            int h)
        {
            if (centerline == null || centerline.Count < 2)
                return;

            ApplyLakeFirstCenterlineOrganicWiggle(centerline, riverSlot, w, h);
            ApplyLakeFirstCenterlineOrganicWiggle(centerline, riverSlot + 31, w, h);
            ApplyLakeFirstCenterlineOrganicWiggle(centerline, riverSlot + 67, w, h);
            ApplyLakeFirstCenterlineChaikin(centerline, passes: 2);
            RefineLakeFirstTributaryMouthCenterline(comp, outlet, centerline, riverSlot);
            TrimLakeFirstCenterlineLakeInterior(comp, centerline, maxMouthPoints: 5);
            ResampleLakeFirstCenterlineUniform(centerline, spacingCells: 0.58f, maxPoints: 220);
        }

        static Vector2 ComputeComponentCentroid(HashSet<long> comp)
        {
            if (comp == null || comp.Count == 0)
                return Vector2.zero;

            Vector2 sum = Vector2.zero;
            int n = 0;
            foreach (long pk in comp)
            {
                int x = (int)(pk >> 32);
                int z = (int)(uint)pk;
                sum += new Vector2(x + 0.5f, z + 0.5f);
                n++;
            }

            return n > 0 ? sum / n : Vector2.zero;
        }

        static void TrimLakeFirstCenterlineLakeInterior(HashSet<long> comp, List<Vector2> centerline, int maxMouthPoints)
        {
            if (comp == null || centerline == null || centerline.Count < 3 || maxMouthPoints < 1)
                return;

            int keptInside = 0;
            for (int i = 0; i < centerline.Count;)
            {
                int cx = Mathf.FloorToInt(centerline[i].x);
                int cz = Mathf.FloorToInt(centerline[i].y);
                if (!comp.Contains(PackCell(cx, cz)))
                    break;

                keptInside++;
                if (keptInside > maxMouthPoints)
                    centerline.RemoveAt(i);
                else
                    i++;
            }
        }

        static void PinLakeFirstTributaryEndpointConfluence(List<Vector2> centerline, Vector2Int confluence)
        {
            if (centerline == null || centerline.Count < 2)
                return;
            centerline[centerline.Count - 1] = new Vector2(confluence.x + 0.5f, confluence.y + 0.5f);
        }

        static List<Vector2Int> BuildRasterPathFromCenterline(List<Vector2> centerline, int w, int h)
        {
            var path = new List<Vector2Int>();
            if (centerline == null || centerline.Count < 2 || w < 2 || h < 2)
                return path;

            void AddCell(int cx, int cz)
            {
                if ((uint)cx >= (uint)w || (uint)cz >= (uint)h)
                    return;
                var cell = new Vector2Int(cx, cz);
                if (path.Count == 0 || path[path.Count - 1].x != cell.x || path[path.Count - 1].y != cell.y)
                    path.Add(cell);
            }

            for (int i = 0; i < centerline.Count - 1; i++)
            {
                int x0 = Mathf.Clamp(Mathf.RoundToInt(centerline[i].x - 0.5f), 0, w - 1);
                int z0 = Mathf.Clamp(Mathf.RoundToInt(centerline[i].y - 0.5f), 0, h - 1);
                int x1 = Mathf.Clamp(Mathf.RoundToInt(centerline[i + 1].x - 0.5f), 0, w - 1);
                int z1 = Mathf.Clamp(Mathf.RoundToInt(centerline[i + 1].y - 0.5f), 0, h - 1);
                int dx = Mathf.Abs(x1 - x0);
                int dz = Mathf.Abs(z1 - z0);
                int sx = x0 < x1 ? 1 : -1;
                int sz = z0 < z1 ? 1 : -1;
                int err = dx - dz;
                int cx = x0;
                int cz = z0;
                while (true)
                {
                    AddCell(cx, cz);
                    if (cx == x1 && cz == z1)
                        break;
                    int e2 = err * 2;
                    if (e2 > -dz)
                    {
                        err -= dz;
                        cx += sx;
                    }

                    if (e2 < dx)
                    {
                        err += dx;
                        cz += sz;
                    }
                }
            }

            return path.Count >= 2 ? path : null;
        }

        static void ResampleLakeFirstCenterlineUniform(List<Vector2> centerline, float spacingCells, int maxPoints)
        {
            if (centerline == null || centerline.Count < 2)
                return;

            spacingCells = Mathf.Clamp(spacingCells, 0.45f, 1.1f);
            float total = 0f;
            for (int i = 1; i < centerline.Count; i++)
                total += Vector2.Distance(centerline[i], centerline[i - 1]);
            if (total < spacingCells * 0.5f)
                return;

            int samples = Mathf.Clamp(Mathf.CeilToInt(total / spacingCells) + 1, 2, maxPoints);
            var resampled = new List<Vector2>(samples);
            float step = total / (samples - 1);
            float acc = 0f;
            int seg = 0;
            for (int s = 0; s < samples; s++)
            {
                float target = s * step;
                while (seg < centerline.Count - 2)
                {
                    float segLen = Vector2.Distance(centerline[seg], centerline[seg + 1]);
                    if (acc + segLen >= target)
                        break;
                    acc += segLen;
                    seg++;
                }

                float localLen = Vector2.Distance(centerline[seg], centerline[seg + 1]);
                float t = localLen > 1e-6f ? (target - acc) / localLen : 0f;
                resampled.Add(Vector2.Lerp(centerline[seg], centerline[seg + 1], Mathf.Clamp01(t)));
            }

            centerline.Clear();
            centerline.AddRange(resampled);
        }

        static void ApplyLakeFirstCenterlineChaikin(List<Vector2> centerline, int passes)
        {
            if (centerline == null || centerline.Count < 4 || passes <= 0)
                return;

            var work = new List<Vector2>(centerline);
            for (int pass = 0; pass < passes; pass++)
            {
                var next = new List<Vector2>(work.Count * 2) { work[0] };
                for (int i = 0; i < work.Count - 1; i++)
                {
                    Vector2 p0 = work[i];
                    Vector2 p1 = work[i + 1];
                    next.Add(Vector2.Lerp(p0, p1, 0.25f));
                    next.Add(Vector2.Lerp(p0, p1, 0.75f));
                }

                next.Add(work[work.Count - 1]);
                work = next;
            }

            centerline.Clear();
            centerline.AddRange(work);
        }

        /// <summary>Rompe líneas rectas del A* con meandro ligero antes de la boca lago.</summary>
        static void ApplyLakeFirstCenterlineOrganicWiggle(
            List<Vector2> centerline,
            int riverSlot,
            int w,
            int h)
        {
            if (centerline == null || centerline.Count < 4 || w < 2 || h < 2)
                return;

            float amp = 0.72f;
            float freq = 3.8f;
            float acc = 0f;
            var trial = new List<Vector2>(centerline);
            for (int i = 1; i < trial.Count - 1; i++)
            {
                acc += Vector2.Distance(trial[i], trial[i - 1]);
                if (IsMapEdgeCellSpace(trial[i], w, h))
                    continue;

                float t01 = trial.Count > 1 ? i / (float)(trial.Count - 1) : 0f;
                float fade = Mathf.Clamp01(1f - Mathf.Abs(t01 - 0.5f) * 1.05f);
                if (fade < 0.08f)
                    continue;

                Vector2 tan = trial[i + 1] - trial[i - 1];
                if (tan.sqrMagnitude < 1e-8f)
                    tan = trial[i] - trial[i - 1];
                if (tan.sqrMagnitude < 1e-8f)
                    continue;
                tan.Normalize();
                Vector2 nrm = new Vector2(-tan.y, tan.x);
                float phase = acc / freq * (Mathf.PI * 2f) + riverSlot * 2.17f;
                float noise01 = Mathf.PerlinNoise(acc / (freq * 0.8f) + riverSlot * 11.3f, riverSlot * 0.41f + 2.7f);
                float wave = Mathf.Clamp(Mathf.Sin(phase) * 0.68f + (noise01 * 2f - 1f) * 0.42f, -1f, 1f);
                trial[i] += nrm * (wave * amp * fade);
            }

            for (int i = 0; i < centerline.Count; i++)
                centerline[i] = trial[i];
        }

        /// <summary>Extiende el centerline hacia el interior del lago para una boca más orgánica.</summary>
        static void RefineLakeFirstTributaryMouthCenterline(
            HashSet<long> comp,
            Vector2Int outlet,
            List<Vector2> centerline,
            int riverSlot)
        {
            if (comp == null || centerline == null || centerline.Count < 2)
                return;

            Vector2Int lakeCell = default;
            bool foundLake = false;
            Vector2 outletPt = new Vector2(outlet.x + 0.5f, outlet.y + 0.5f);
            Vector2 flowDir = centerline.Count > 1
                ? centerline[1] - centerline[0]
                : Vector2.right;
            if (flowDir.sqrMagnitude < 1e-6f && centerline.Count > 2)
                flowDir = centerline[2] - centerline[0];
            if (flowDir.sqrMagnitude < 1e-6f)
                return;
            flowDir.Normalize();

            float bestAlign = -2f;
            for (int ni = 0; ni < 4; ni++)
            {
                int nx = outlet.x + NeighborDx[ni];
                int nz = outlet.y + NeighborDz[ni];
                if (!comp.Contains(PackCell(nx, nz)))
                    continue;

                Vector2 lakeCenter = new Vector2(nx + 0.5f, nz + 0.5f);
                Vector2 outFromLake = (outletPt - lakeCenter).normalized;
                float align = Vector2.Dot(outFromLake, flowDir);
                if (align > bestAlign)
                {
                    bestAlign = align;
                    lakeCell = new Vector2Int(nx, nz);
                    foundLake = true;
                }
            }

            if (!foundLake)
                return;

            Vector2 lakeShore = new Vector2(lakeCell.x + 0.5f, lakeCell.y + 0.5f);
            Vector2 lakeCentroid = ComputeComponentCentroid(comp);
            Vector2 intoLake = lakeCentroid - lakeShore;
            if (intoLake.sqrMagnitude < 1e-6f)
                intoLake = -flowDir;
            intoLake.Normalize();
            Vector2 interior = FindLakeInteriorMouthAnchor(comp, lakeCell, intoLake);
            Vector2 towardCentroid = Vector2.Lerp(lakeShore, lakeCentroid, 0.38f);
            Vector2 midIngress = Vector2.Lerp(lakeShore, towardCentroid, 0.55f);
            Vector2 softInterior = Vector2.Lerp(lakeShore, interior, 0.34f);

            centerline.Insert(0, lakeShore);
            centerline.Insert(0, softInterior);
            centerline.Insert(0, midIngress);
            centerline.Insert(0, towardCentroid);
        }

        static Vector2 FindLakeInteriorMouthAnchor(HashSet<long> comp, Vector2Int fromLakeCell, Vector2 intoLake)
        {
            Vector2 origin = new Vector2(fromLakeCell.x + 0.5f, fromLakeCell.y + 0.5f);
            if (intoLake.sqrMagnitude < 1e-6f)
                intoLake = Vector2.left;
            intoLake.Normalize();
            Vector2 perp = new Vector2(-intoLake.y, intoLake.x);

            float best = -1f;
            Vector2 bestPt = origin + intoLake * 1.35f;
            foreach (long pk in comp)
            {
                int x = (int)(pk >> 32);
                int z = (int)(uint)pk;
                Vector2 p = new Vector2(x + 0.5f, z + 0.5f);
                Vector2 delta = p - origin;
                float along = Vector2.Dot(delta, intoLake);
                if (along < 0.55f || along > 5.5f)
                    continue;
                float lateral = Mathf.Abs(Vector2.Dot(delta, perp));
                float score = along - lateral * 0.38f;
                if (score > best)
                {
                    best = score;
                    bestPt = p;
                }
            }

            return bestPt;
        }

        static bool IsMapEdgeCellSpace(Vector2 p, int w, int h)
        {
            if (w < 2 || h < 2)
                return false;
            float west = 0.5f;
            float east = (w - 1) + 0.5f;
            float south = 0.5f;
            float north = (h - 1) + 0.5f;
            return p.x <= west + 0.01f || p.x >= east - 0.01f || p.y <= south + 0.01f || p.y >= north - 0.01f;
        }

        static bool ValidateConnectivity(
            GridSystem grid,
            HashSet<long> lakeComp,
            Vector2Int outlet,
            List<Vector2Int> path,
            Vector2Int confluence,
            List<Vector2> mainCl)
        {
            if (path == null || path.Count < 2 || lakeComp == null || mainCl == null)
                return false;

            bool outletTouchesLake = false;
            for (int ni = 0; ni < 4; ni++)
            {
                int nx = outlet.x + NeighborDx[ni];
                int nz = outlet.y + NeighborDz[ni];
                if (lakeComp.Contains(PackCell(nx, nz)))
                {
                    outletTouchesLake = true;
                    break;
                }
            }

            if (!outletTouchesLake)
                return false;

            for (int i = 1; i < path.Count; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                if (Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)) > 1)
                    return false;
            }

            var last = path[path.Count - 1];
            if (last.x != confluence.x || last.y != confluence.y)
                return false;

            return IsOnMainRiverCell(confluence.x, confluence.y, mainCl);
        }

        static bool AuditFinalConnectivity(UwpWaterGraph graph, GridSystem grid)
        {
            if (graph == null || grid?.RiverCenterlinesCellSpace == null)
                return false;
            foreach (var trib in graph.Tributaries)
            {
                if (!trib.Accepted)
                    continue;
                if (!trib.ConnectivityValid || trib.CenterlineCells == null || trib.CenterlineCells.Count < 2)
                    return false;
                if (trib.RiverIndex <= 0 || trib.RiverIndex >= grid.RiverCenterlinesCellSpace.Count)
                    return false;
            }

            return true;
        }

        static bool IsOnMainRiverCell(int cx, int cz, List<Vector2> mainCl)
        {
            if (mainCl == null)
                return false;
            const float tol = 1.05f;
            for (int i = 0; i < mainCl.Count; i++)
            {
                float d = Mathf.Max(Mathf.Abs(mainCl[i].x - (cx + 0.5f)), Mathf.Abs(mainCl[i].y - (cz + 0.5f)));
                if (d <= tol)
                    return true;
            }

            return false;
        }

        static Vector2 ComputePolylineCentroid(List<Vector2> poly)
        {
            if (poly == null || poly.Count == 0)
                return Vector2.zero;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < poly.Count; i++)
                sum += poly[i];
            return sum / poly.Count;
        }

        static int MinChebyshevComponentToMain(HashSet<long> comp, List<Vector2> mainCl, int gw, int gh)
        {
            int best = 99999;
            foreach (long pk in comp)
            {
                int x = (int)(pk >> 32);
                int z = (int)(uint)pk;
                int d = MinChebyshevPointToMain(new Vector2(x + 0.5f, z + 0.5f), mainCl, gw, gh);
                if (d < best)
                    best = d;
            }

            return best;
        }

        static int MinChebyshevPointToMain(Vector2 p, List<Vector2> mainCl, int gw, int gh)
        {
            if (mainCl == null || mainCl.Count == 0)
                return 99999;
            int best = 99999;
            for (int i = 0; i < mainCl.Count; i++)
            {
                int d = Mathf.Max(
                    Mathf.Abs(Mathf.RoundToInt(p.x) - Mathf.FloorToInt(mainCl[i].x)),
                    Mathf.Abs(Mathf.RoundToInt(p.y) - Mathf.FloorToInt(mainCl[i].y)));
                if (d < best)
                    best = d;
            }

            for (int i = 0; i < mainCl.Count - 1; i++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(mainCl[i], mainCl[i + 1]) * 2f));
                for (int s = 0; s <= steps; s++)
                {
                    Vector2 q = Vector2.Lerp(mainCl[i], mainCl[i + 1], s / (float)steps);
                    int d = Mathf.Max(
                        Mathf.Abs(Mathf.RoundToInt(p.x) - Mathf.FloorToInt(q.x)),
                        Mathf.Abs(Mathf.RoundToInt(p.y) - Mathf.FloorToInt(q.y)));
                    if (d < best)
                        best = d;
                }
            }

            return best;
        }

        static int Heuristic(Vector2Int a, Vector2Int b) =>
            Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)) * 10;

        static int Chebyshev(Vector2Int a, Vector2Int b) =>
            Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        static long PackCell(int x, int z) => ((long)x << 32) | (uint)z;

        static void LogSeed(MapGenConfig config, string msg)
        {
            if (config == null)
                return;
            Debug.Log($"[UWP_LAKE_FIRST] seed={config.seed} {msg}");
        }

        static void EmitSeedLogs(MapGenConfig config, UwpWaterGraph graph)
        {
            if (config == null || graph == null)
                return;

            bool log = config.debugLogs || config.debugHydrologyNetwork || config.uwpLakeFirstHydrologyPipeline;
            if (!log)
                return;

            var r = graph.Report;
            Debug.Log(
                $"[UWP_LAKE_FIRST] seed={config.seed} " +
                $"lakeComponentsRaw={r.LakeCandidates} lakesSelected={graph.Lakes.Count} " +
                $"lakesAccepted={r.LakesAccepted} lakesRejected={r.LakesRejected} " +
                $"tribAccepted={r.TributariesAccepted} tribRejected={r.TributariesRejected} " +
                $"finalRivers={r.FinalRiverCount} connectivity={(r.FinalConnectivityOk ? 1 : 0)}");

            for (int i = 0; i < r.LakeRejectLines.Count; i++)
                Debug.Log($"[UWP_LAKE_FIRST] seed={config.seed} lakeReject {r.LakeRejectLines[i]}");

            for (int i = 0; i < graph.Lakes.Count; i++)
            {
                var lake = graph.Lakes[i];
                if (lake.Accepted)
                {
                    Debug.Log(
                        $"[UWP_LAKE_FIRST] seed={config.seed} lakeAccepted comp={lake.ComponentIndex} " +
                        $"cells={lake.CellCount} distMain={lake.DistanceToMainCells} outlet=({lake.OutletCell.x},{lake.OutletCell.y}) " +
                        $"ownerTrib={lake.OwnerTributaryRiverIndex}");
                }
            }

            for (int i = 0; i < graph.Tributaries.Count; i++)
            {
                var trib = graph.Tributaries[i];
                Debug.Log(
                    $"[UWP_LAKE_FIRST] seed={config.seed} tributary comp={trib.LakeComponentIndex} " +
                    $"accepted={(trib.Accepted ? 1 : 0)} riverIndex={trib.RiverIndex} " +
                    $"outlet=({trib.LakeOutletCell.x},{trib.LakeOutletCell.y}) " +
                    $"confluence=({trib.MainRiverConfluenceCell.x},{trib.MainRiverConfluenceCell.y}) " +
                    $"distLakeMain={trib.DistanceLakeToMainCells} connectivity={(trib.ConnectivityValid ? 1 : 0)} " +
                    $"reason={(string.IsNullOrEmpty(trib.RejectReason) ? "ok" : trib.RejectReason)}");
            }

            for (int i = 0; i < r.TributaryRejectLines.Count; i++)
                Debug.Log($"[UWP_LAKE_FIRST] seed={config.seed} tribReject {r.TributaryRejectLines[i]}");
        }
    }
}
