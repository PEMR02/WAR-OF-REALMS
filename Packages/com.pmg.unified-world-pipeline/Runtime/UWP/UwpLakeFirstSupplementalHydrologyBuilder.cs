using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Post lake-first: tributarios supplementales (inland→main, headwater→trib).
    /// </summary>
    public static class UwpLakeFirstSupplementalHydrologyBuilder
    {
        static long PackCell(int x, int z) => ((long)x << 32) | (uint)z;

        public static void BuildAndApply(
            GridSystem grid,
            MapGenConfig config,
            IRng rng,
            ref int waterCells,
            HashSet<long> riverOccupiedCells,
            ref bool riverOccAabbValid,
            ref int riverOccMinX,
            ref int riverOccMaxX,
            ref int riverOccMinZ,
            ref int riverOccMaxZ)
        {
            if (grid == null || config == null || rng == null || !config.uwpLakeFirstSupplementalEnabled)
                return;
            if (!config.uwpLakeFirstHydrologyPipeline || grid.RiverCenterlinesCellSpace == null ||
                grid.RiverCenterlinesCellSpace.Count == 0)
                return;

            var graph = grid.LakeFirstWaterGraph;
            if (graph == null)
                graph = new UwpWaterGraph { Seed = config.seed };

            int minDim = Mathf.Min(grid.Width, grid.Height);
            int tribBudget = Mathf.Max(0, config.riverCount - 1);
            int lakeSpillCount = CountAcceptedLakeSpillTributaries(graph, grid);
            int missingTribSlots = Mathf.Max(0, tribBudget - lakeSpillCount);
            // Reservar al menos 1 slot para HeadwaterFeeder si el mapa lo pide; inland no se come el cupo.
            int headwaterDesire = ResolveHeadwaterDesire(config, minDim);
            int headwaterReserve = headwaterDesire > 0 && missingTribSlots > 0
                ? Mathf.Min(1, missingTribSlots)
                : 0;
            int inlandSlots = Mathf.Max(0, missingTribSlots - headwaterReserve);
            int target = Mathf.Min(
                ResolveInlandFeederTarget(config, minDim, grid.RiverCenterlinesCellSpace.Count),
                inlandSlots);
            graph.SupplementalReport.InlandFeederTarget = target;

            var claimedJoins = CollectClaimedConfluenceCells(grid);
            int accepted = 0;
            int rejected = 0;

            if (target > 0)
            {
            int minSepLake = ResolveLakeSpillSeparationCells(config, minDim);
            int minJoinSpacing = Mathf.Clamp(config.inlandFeederMinConfluenceSpacingCells, 8, 32);
            int attempt = 0;
            int maxAttempts = Mathf.Clamp(target * 20, 12, 64);

            while (accepted < target && attempt < maxAttempts)
            {
                attempt++;
                int slotIndex = grid.RiverCenterlinesCellSpace.Count;
                // Dejar hueco para headwater reservado.
                if (slotIndex >= config.riverCount - headwaterReserve)
                    break;

                if (!RiverRouteGenerator.TryPlaceUwpFillPassTributaryRoute(
                        grid,
                        config,
                        rng,
                        slotIndex + 1,
                        attempt + 60000,
                        avoidCrossingCorridor: false,
                        occupiedRiverCells: null,
                        out List<Vector2Int> path,
                        out List<Vector2> centerline,
                        out List<Vector2Int> fordCells,
                        out string fail) ||
                    path == null || path.Count < 2 || centerline == null || centerline.Count < 2)
                {
                    rejected++;
                    if (!string.IsNullOrEmpty(fail))
                        graph.SupplementalReport.RejectLines.Add($"attempt={attempt} reason={fail}");
                    continue;
                }

                int nearLakeCells = ResolveInlandNearLakePromoteCells(config, minDim);
                if (UwpLakeFirstHydrologyBuilder.TryFindOrRegisterNearbyUnownedLake(
                        grid, config, path, nearLakeCells, out int nearLakeIdx))
                {
                    if (UwpLakeFirstHydrologyBuilder.TryPromoteSpillForUnownedLake(
                            grid,
                            config,
                            rng,
                            nearLakeIdx,
                            claimedJoins,
                            ref waterCells,
                            riverOccupiedCells,
                            ref riverOccAabbValid,
                            ref riverOccMinX,
                            ref riverOccMaxX,
                            ref riverOccMinZ,
                            ref riverOccMaxZ))
                    {
                        graph.SupplementalReport.RejectLines.Add(
                            $"attempt={attempt} reason=inland_near_lake_promoted_spill lake={nearLakeIdx}");
                        if (config.debugLogs || config.debugHydrologyNetwork || config.uwpOwnedVisualPolicy)
                        {
                            Debug.Log(
                                $"[LakeFirstSupplemental] inland→LakeSpill promote lake={nearLakeIdx} " +
                                $"seed={config.seed} (inland path discarded)");
                        }
                    }
                    else
                    {
                        rejected++;
                        graph.SupplementalReport.RejectLines.Add(
                            $"attempt={attempt} reason=near_unowned_lake_spill_failed lake={nearLakeIdx}");
                    }

                    continue;
                }

                if (!PassesInlandFeederValidation(
                        grid,
                        path,
                        claimedJoins,
                        minSepLake,
                        minJoinSpacing,
                        nearLakeCells,
                        out Vector2Int joinCell,
                        out string reject))
                {
                    rejected++;
                    graph.SupplementalReport.RejectLines.Add($"attempt={attempt} reason={reject}");
                    continue;
                }

                UwpTributaryOriginUtility.PinEndpointConfluence(centerline, joinCell);

                if (!RiverSurfaceMeshBuilder.TryPrepareLakeFirstInlandFeederVisualCenterline(
                        grid,
                        config,
                        centerline,
                        joinCell,
                        slotIndex,
                        out string visualReject))
                {
                    rejected++;
                    graph.SupplementalReport.RejectLines.Add(
                        $"attempt={attempt} reason={visualReject ?? "inland_visual_approach"}");
                    continue;
                }

                int added = WaterGenerator.ApplySupplementalValidatedTributary(
                    grid,
                    config,
                    rng,
                    slotIndex,
                    UwpTributaryOriginKind.InlandFeeder,
                    path,
                    centerline,
                    fordCells,
                    riverOccupiedCells,
                    ref riverOccAabbValid,
                    ref riverOccMinX,
                    ref riverOccMaxX,
                    ref riverOccMinZ,
                    ref riverOccMaxZ);

                if (added <= 0)
                {
                    rejected++;
                    graph.SupplementalReport.RejectLines.Add($"attempt={attempt} reason=raster_failed");
                    continue;
                }

                waterCells += added;
                accepted++;
                claimedJoins.Add(PackCell(joinCell.x, joinCell.y));
                graph.FinalCenterlineByRiverIndex[slotIndex] = new List<Vector2>(centerline);
                graph.Tributaries.Add(new UwpTributaryGraphEdge
                {
                    RiverIndex = slotIndex,
                    LakeComponentIndex = -1,
                    MainRiverConfluenceCell = joinCell,
                    CenterlineCells = new List<Vector2>(centerline),
                    PathCells = new List<Vector2Int>(path),
                    Accepted = true,
                    ConnectivityValid = true,
                });

                RiverConfluenceUtility.TryRegisterFromPlacement(
                    grid,
                    config,
                    slotIndex,
                    path,
                    centerline,
                    joinCell,
                    "inland_feeder_supplemental");

                if (config.debugLogs || config.debugHydrologyNetwork)
                {
                    Debug.Log(
                        $"[LakeFirstSupplemental] kind=InlandFeeder riverIndex={slotIndex} " +
                        $"source=({path[0].x},{path[0].y}) join=({joinCell.x},{joinCell.y}) accepted=1 seed={config.seed}");
                }
            }

            }

            graph.SupplementalReport.InlandFeedersAccepted = accepted;
            graph.SupplementalReport.InlandFeedersRejected = rejected;

            BuildHeadwaterFeeders(
                grid,
                config,
                rng,
                ref waterCells,
                claimedJoins,
                riverOccupiedCells,
                ref riverOccAabbValid,
                ref riverOccMinX,
                ref riverOccMaxX,
                ref riverOccMinZ,
                ref riverOccMaxZ,
                graph);

            grid.LakeFirstWaterGraph = graph;
            LogSummary(config, graph);
        }

        static void BuildHeadwaterFeeders(
            GridSystem grid,
            MapGenConfig config,
            IRng rng,
            ref int waterCells,
            HashSet<long> claimedJoins,
            HashSet<long> riverOccupiedCells,
            ref bool riverOccAabbValid,
            ref int riverOccMinX,
            ref int riverOccMaxX,
            ref int riverOccMinZ,
            ref int riverOccMaxZ,
            UwpWaterGraph graph)
        {
            int minDim = Mathf.Min(grid.Width, grid.Height);
            int target = ResolveHeadwaterFeederTarget(config, minDim, grid.RiverCenterlinesCellSpace.Count);
            graph.SupplementalReport.HeadwaterFeederTarget = target;
            if (target <= 0)
                return;

            var receivers = CollectHeadwaterReceiverRiverIndices(grid);
            if (receivers.Count == 0)
            {
                graph.SupplementalReport.RejectLines.Add("headwater reason=no_receiver_tributaries");
                return;
            }

            int minJoinSpacing = Mathf.Clamp(config.inlandFeederMinConfluenceSpacingCells, 10, 28);
            int accepted = 0;
            int rejected = 0;
            int attempt = 0;
            int maxAttempts = Mathf.Clamp(target * 28, 20, 96);

            RunHeadwaterPlacementPass(
                grid,
                config,
                rng,
                ref waterCells,
                claimedJoins,
                riverOccupiedCells,
                ref riverOccAabbValid,
                ref riverOccMinX,
                ref riverOccMaxX,
                ref riverOccMinZ,
                ref riverOccMaxZ,
                graph,
                receivers,
                minJoinSpacing,
                target,
                maxAttempts,
                relaxedAngle: false,
                ref accepted,
                ref rejected,
                ref attempt);

            if (accepted < target)
            {
                int fallbackAttempts = Mathf.Clamp(target * 20, 12, 48);
                RunHeadwaterPlacementPass(
                    grid,
                    config,
                    rng,
                    ref waterCells,
                    claimedJoins,
                    riverOccupiedCells,
                    ref riverOccAabbValid,
                    ref riverOccMinX,
                    ref riverOccMaxX,
                    ref riverOccMinZ,
                    ref riverOccMaxZ,
                    graph,
                    receivers,
                    minJoinSpacing,
                    target,
                    fallbackAttempts,
                    relaxedAngle: true,
                    ref accepted,
                    ref rejected,
                    ref attempt);
            }

            graph.SupplementalReport.HeadwaterFeedersAccepted = accepted;
            graph.SupplementalReport.HeadwaterFeedersRejected = rejected;
        }

        static void RunHeadwaterPlacementPass(
            GridSystem grid,
            MapGenConfig config,
            IRng rng,
            ref int waterCells,
            HashSet<long> claimedJoins,
            HashSet<long> riverOccupiedCells,
            ref bool riverOccAabbValid,
            ref int riverOccMinX,
            ref int riverOccMaxX,
            ref int riverOccMinZ,
            ref int riverOccMaxZ,
            UwpWaterGraph graph,
            List<int> receivers,
            int minJoinSpacing,
            int target,
            int maxAttempts,
            bool relaxedAngle,
            ref int accepted,
            ref int rejected,
            ref int attempt)
        {
            int passStart = attempt;
            while (accepted < target && (attempt - passStart) < maxAttempts)
            {
                attempt++;
                int slotIndex = grid.RiverCenterlinesCellSpace.Count;
                if (slotIndex >= config.riverCount)
                    break;

                int receiverRi = receivers[attempt % receivers.Count];
                if (!RiverRouteGenerator.TryPlaceUwpHeadwaterFeederRoute(
                        grid,
                        config,
                        rng,
                        slotIndex + 1,
                        receiverRi,
                        attempt + (relaxedAngle ? 90000 : 70000),
                        riverOccupiedCells,
                        out List<Vector2Int> path,
                        out List<Vector2> centerline,
                        out List<Vector2Int> fordCells,
                        out string fail) ||
                    path == null || path.Count < 2 || centerline == null || centerline.Count < 2)
                {
                    rejected++;
                    if (!string.IsNullOrEmpty(fail))
                    {
                        graph.SupplementalReport.RejectLines.Add(
                            $"headwater attempt={attempt} relaxed={(relaxedAngle ? 1 : 0)} reason={fail}");
                    }
                    continue;
                }

                if (!PassesHeadwaterFeederValidation(
                        grid,
                        config,
                        path,
                        centerline,
                        receiverRi,
                        claimedJoins,
                        minJoinSpacing,
                        relaxedAngle,
                        out Vector2Int joinCell,
                        out string reject))
                {
                    rejected++;
                    graph.SupplementalReport.RejectLines.Add(
                        $"headwater attempt={attempt} relaxed={(relaxedAngle ? 1 : 0)} reason={reject}");
                    continue;
                }

                UwpTributaryOriginUtility.PinEndpointConfluence(centerline, joinCell);

                int added = WaterGenerator.ApplySupplementalValidatedTributary(
                    grid,
                    config,
                    rng,
                    slotIndex,
                    UwpTributaryOriginKind.HeadwaterFeeder,
                    path,
                    centerline,
                    fordCells,
                    riverOccupiedCells,
                    ref riverOccAabbValid,
                    ref riverOccMinX,
                    ref riverOccMaxX,
                    ref riverOccMinZ,
                    ref riverOccMaxZ,
                    receiverRi);

                if (added <= 0)
                {
                    rejected++;
                    graph.SupplementalReport.RejectLines.Add(
                        $"headwater attempt={attempt} relaxed={(relaxedAngle ? 1 : 0)} reason=raster_failed");
                    continue;
                }

                waterCells += added;
                accepted++;
                claimedJoins.Add(PackCell(joinCell.x, joinCell.y));
                graph.FinalCenterlineByRiverIndex[slotIndex] = new List<Vector2>(centerline);
                graph.Tributaries.Add(new UwpTributaryGraphEdge
                {
                    RiverIndex = slotIndex,
                    LakeComponentIndex = -1,
                    MainRiverConfluenceCell = joinCell,
                    CenterlineCells = new List<Vector2>(centerline),
                    PathCells = new List<Vector2Int>(path),
                    Accepted = true,
                    ConnectivityValid = true,
                });

                RiverConfluenceUtility.TryRegisterFromPlacement(
                    grid,
                    config,
                    slotIndex,
                    path,
                    centerline,
                    joinCell,
                    "headwater_feeder_supplemental",
                    receiverRi);

                if (config.debugLogs || config.debugHydrologyNetwork || config.uwpOwnedVisualPolicy)
                {
                    Debug.Log(
                        $"[LakeFirstSupplemental] kind=HeadwaterFeeder riverIndex={slotIndex} receiver={receiverRi} " +
                        $"relaxed={(relaxedAngle ? 1 : 0)} source=({path[0].x},{path[0].y}) " +
                        $"join=({joinCell.x},{joinCell.y}) accepted=1 seed={config.seed}");
                }
            }
        }

        static int ResolveHeadwaterDesire(MapGenConfig config, int minDim)
        {
            if (config == null)
                return 0;
            if (config.headwaterFeederTargetCount >= 0)
                return Mathf.Max(0, config.headwaterFeederTargetCount);

            int auto = Mathf.Clamp(Mathf.FloorToInt(minDim / 160f), 0, 3);
            if (!config.ignoreLobbyHydrologyCaps && minDim <= 256)
                auto = Mathf.Min(auto, 1);
            return auto;
        }

        static int ResolveHeadwaterFeederTarget(MapGenConfig config, int minDim, int currentRiverCount)
        {
            int globalCap = Mathf.Min(8, config.riverCount);
            int remaining = globalCap - currentRiverCount;
            if (remaining <= 0)
                return 0;

            int desire = ResolveHeadwaterDesire(config, minDim);
            // Garantizar al menos 1 si hay cupo y hay deseo (ensayo / play).
            if (desire <= 0 && remaining > 0 && minDim >= 160)
                desire = 1;
            return Mathf.Min(Mathf.Max(0, desire), remaining);
        }

        static List<int> CollectHeadwaterReceiverRiverIndices(GridSystem grid)
        {
            var receivers = new List<int>(4);
            if (grid?.RiverCenterlinesCellSpace == null)
                return receivers;

            // Preferir InlandFeeder como receptor: evita uniones junto a boca lago→spill.
            for (int ri = 1; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
            {
                if (UwpTributaryOriginUtility.IsInlandFeeder(grid, ri))
                    receivers.Add(ri);
            }

            for (int ri = 1; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
            {
                if (UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, ri) &&
                    !receivers.Contains(ri))
                    receivers.Add(ri);
            }

            return receivers;
        }

        static bool PassesHeadwaterFeederValidation(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2Int> path,
            List<Vector2> centerline,
            int receiverRiverIndex,
            HashSet<long> claimedJoins,
            int minJoinSpacing,
            bool relaxedAngle,
            out Vector2Int joinCell,
            out string reject)
        {
            joinCell = default;
            reject = null;
            if (grid == null || path == null || path.Count < 8)
            {
                reject = "path_too_short";
                return false;
            }

            if (RiverRouteGenerator.LastTributaryConfluencePlanValid)
                joinCell = RiverRouteGenerator.LastTributaryConfluencePlan.ConfluenceCell;
            else
                joinCell = path[path.Count - 1];

            foreach (long pk in claimedJoins)
            {
                int jx = (int)(pk >> 32);
                int jz = (int)(uint)pk;
                if (Chebyshev(joinCell, new Vector2Int(jx, jz)) < minJoinSpacing)
                {
                    reject = "join_spacing";
                    return false;
                }
            }

            if (CrossesLakeBody(path, grid))
            {
                reject = "crosses_lake_body";
                return false;
            }

            int minLakeSep = ResolveHeadwaterMinSeparationFromLakeCells(config, Mathf.Min(grid.Width, grid.Height));
            if (MinDistJoinToLakeMouthOrBody(grid, joinCell) < minLakeSep)
            {
                reject = "join_near_lake_mouth";
                return false;
            }

            if (MinChebyshevToAnyLakeBodyCell(grid, path[0], minLakeSep) < Mathf.Max(10, minLakeSep * 2 / 3))
            {
                reject = "source_near_lake";
                return false;
            }

            if (JoinTooCloseToLakeEndOfReceiver(grid, receiverRiverIndex, joinCell, minLakeSep))
            {
                reject = "join_near_lake_spill_head";
                return false;
            }

            if (receiverRiverIndex <= 0 || grid.RiverCenterlinesCellSpace == null ||
                receiverRiverIndex >= grid.RiverCenterlinesCellSpace.Count)
            {
                reject = "invalid_receiver";
                return false;
            }

            if (!PassesHeadwaterJoinAngle(
                    grid, config, centerline, receiverRiverIndex, joinCell, relaxedAngle, out reject))
                return false;

            return true;
        }

        static int ResolveHeadwaterMinSeparationFromLakeCells(MapGenConfig config, int minDim)
        {
            int baseSep = config != null
                ? Mathf.Max(18, config.inlandFeederMinSeparationFromLakeTribCells)
                : 24;
            if (minDim >= 320)
                return Mathf.Clamp(baseSep + 4, 20, 40);
            return Mathf.Clamp(baseSep, 18, 36);
        }

        static int MinDistJoinToLakeMouthOrBody(GridSystem grid, Vector2Int joinCell)
        {
            int best = int.MaxValue;
            if (grid?.LakeFirstWaterGraph?.Lakes != null)
            {
                for (int i = 0; i < grid.LakeFirstWaterGraph.Lakes.Count; i++)
                {
                    var lake = grid.LakeFirstWaterGraph.Lakes[i];
                    if (lake == null)
                        continue;
                    if (lake.OutletValid)
                        best = Mathf.Min(best, Chebyshev(joinCell, lake.OutletCell));
                    if (lake.BodyCellsPacked != null && lake.BodyCellsPacked.Count > 0)
                        best = Mathf.Min(best, MinChebyshevToPackedBody(joinCell, lake.BodyCellsPacked, 48));
                }
            }

            if (grid?.LakeFirstWaterGraph?.Tributaries != null)
            {
                for (int i = 0; i < grid.LakeFirstWaterGraph.Tributaries.Count; i++)
                {
                    var trib = grid.LakeFirstWaterGraph.Tributaries[i];
                    if (!trib.Accepted || trib.LakeComponentIndex < 0)
                        continue;
                    best = Mathf.Min(best, Chebyshev(joinCell, trib.LakeOutletCell));
                }
            }

            if (grid?.LakeMouthCellsPacked != null)
            {
                foreach (long pk in grid.LakeMouthCellsPacked)
                {
                    int x = (int)(pk >> 32);
                    int z = (int)(uint)pk;
                    best = Mathf.Min(best, Chebyshev(joinCell, new Vector2Int(x, z)));
                }
            }

            return best;
        }

        static int MinChebyshevToAnyLakeBodyCell(GridSystem grid, Vector2Int cell, int maxScan)
        {
            if (grid?.LakeBodyCellsPacked != null && grid.LakeBodyCellsPacked.Count > 0)
                return MinChebyshevToPackedBody(cell, grid.LakeBodyCellsPacked, maxScan);

            int best = maxScan + 1;
            var lakes = grid?.LakeFirstWaterGraph?.Lakes;
            if (lakes == null)
                return best;
            for (int i = 0; i < lakes.Count; i++)
            {
                var body = lakes[i]?.BodyCellsPacked;
                if (body == null || body.Count == 0)
                    continue;
                best = Mathf.Min(best, MinChebyshevToPackedBody(cell, body, maxScan));
                if (best == 0)
                    return 0;
            }

            return best;
        }

        static bool JoinTooCloseToLakeEndOfReceiver(
            GridSystem grid,
            int receiverRiverIndex,
            Vector2Int joinCell,
            int minSepCells)
        {
            if (grid == null || receiverRiverIndex <= 0)
                return false;

            if (!UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, receiverRiverIndex))
                return false;

            if (grid.LakeFirstWaterGraph?.Tributaries != null)
            {
                for (int i = 0; i < grid.LakeFirstWaterGraph.Tributaries.Count; i++)
                {
                    var trib = grid.LakeFirstWaterGraph.Tributaries[i];
                    if (!trib.Accepted || trib.RiverIndex != receiverRiverIndex)
                        continue;
                    if (Chebyshev(joinCell, trib.LakeOutletCell) < minSepCells)
                        return true;
                }
            }

            if (grid.RiverCenterlinesCellSpace == null ||
                receiverRiverIndex >= grid.RiverCenterlinesCellSpace.Count)
                return false;

            var line = grid.RiverCenterlinesCellSpace[receiverRiverIndex];
            if (line == null || line.Count < 4)
                return false;

            int idx = ClosestCenterlineIndex(line, joinCell);
            // LakeSpill: inicio ≈ outlet lago → main. Rechazar uniones en el tramo cercano al lago.
            float along01 = idx / (float)(line.Count - 1);
            return along01 < 0.30f;
        }

        static bool PassesHeadwaterJoinAngle(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> centerline,
            int receiverRiverIndex,
            Vector2Int joinCell,
            bool relaxedAngle,
            out string reject)
        {
            reject = null;
            if (config == null || centerline == null || centerline.Count < 2)
            {
                reject = "no_centerline";
                return false;
            }

            var recvLine = grid.RiverCenterlinesCellSpace[receiverRiverIndex];
            if (recvLine == null || recvLine.Count < 2)
            {
                reject = "invalid_receiver_line";
                return false;
            }

            int recvIdx = ClosestCenterlineIndex(recvLine, joinCell);
            Vector2 recvDown = RiverConfluenceUtility.ReceiverDownstreamAt(recvLine, recvIdx);
            Vector2 tribIn = RiverDendriticUtility.TributaryIncomingAt(centerline, centerline.Count - 1);
            float joinAngleDeg = RiverDendriticUtility.ComputeDirectedJoinAngleDeg(recvDown, tribIn);
            bool isParallel = joinAngleDeg < 20f || joinAngleDeg > 160f;
            bool isTJunction = joinAngleDeg >= 88f && joinAngleDeg <= 92f;

            bool angleOk = relaxedAngle
                ? RiverDendriticUtility.IsJoinAngleLooseAcceptable(config, joinAngleDeg, isParallel, isTJunction)
                : RiverDendriticUtility.IsJoinAngleAcceptable(config, joinAngleDeg, out isParallel, out isTJunction);

            if (!angleOk && !relaxedAngle)
            {
                // Primera pasada: permitir ventana loose si el preferido falla (sin esperar fallback).
                angleOk = RiverDendriticUtility.IsJoinAngleLooseAcceptable(
                    config, joinAngleDeg, isParallel, isTJunction);
            }

            if (!angleOk)
            {
                reject = isParallel ? "join_angle_parallel" : (isTJunction ? "join_angle_90" : "join_angle_out_of_range");
                return false;
            }

            Vector2 recvNorm = recvDown.sqrMagnitude > 1e-6f ? recvDown.normalized : Vector2.right;
            if (Vector2.Dot(recvNorm, tribIn) < 0f)
            {
                reject = "join_against_receiver_flow";
                return false;
            }

            return true;
        }

        static int ClosestCenterlineIndex(IReadOnlyList<Vector2> line, Vector2Int cell)
        {
            float px = cell.x + 0.5f;
            float pz = cell.y + 0.5f;
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < line.Count; i++)
            {
                float dx = line[i].x - px;
                float dz = line[i].y - pz;
                float d = dx * dx + dz * dz;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            return best;
        }

        static int CountAcceptedLakeSpillTributaries(UwpWaterGraph graph, GridSystem grid)
        {
            int count = 0;
            if (graph?.Tributaries != null)
            {
                for (int i = 0; i < graph.Tributaries.Count; i++)
                {
                    var trib = graph.Tributaries[i];
                    if (trib.Accepted && trib.LakeComponentIndex >= 0)
                        count++;
                }
            }

            if (count > 0 || grid?.RiverOriginKinds == null)
                return count;

            for (int ri = 1; ri < grid.RiverOriginKinds.Count; ri++)
            {
                if (grid.RiverOriginKinds[ri] == UwpTributaryOriginKind.LakeSpill)
                    count++;
            }

            return count;
        }

        static int ResolveInlandFeederTarget(MapGenConfig config, int minDim, int currentRiverCount)
        {
            int globalCap = Mathf.Min(8, config.riverCount);
            int remaining = globalCap - currentRiverCount;
            if (remaining <= 0)
                return 0;

            int auto = Mathf.Clamp(Mathf.FloorToInt(minDim / 128f) - 1, 0, 4);
            if (!config.ignoreLobbyHydrologyCaps && minDim <= 256)
                auto = Mathf.Min(auto, 2);

            int target = config.inlandFeederTargetCount >= 0
                ? config.inlandFeederTargetCount
                : auto;
            return Mathf.Min(Mathf.Max(0, target), remaining);
        }

        static int ResolveLakeSpillSeparationCells(MapGenConfig config, int minDim)
        {
            int auto = Mathf.Max(20, Mathf.RoundToInt(minDim / 12f));
            return Mathf.Clamp(
                config.inlandFeederMinSeparationFromLakeTribCells > 0
                    ? config.inlandFeederMinSeparationFromLakeTribCells
                    : auto,
                12,
                48);
        }

        static HashSet<long> CollectClaimedConfluenceCells(GridSystem grid)
        {
            var claimed = new HashSet<long>();
            if (grid?.RiverConfluences != null)
            {
                for (int i = 0; i < grid.RiverConfluences.Count; i++)
                {
                    var n = grid.RiverConfluences[i];
                    if (n.Valid)
                        claimed.Add(PackCell(n.Cell.x, n.Cell.y));
                }
            }

            if (grid?.LakeFirstWaterGraph?.Tributaries != null)
            {
                for (int i = 0; i < grid.LakeFirstWaterGraph.Tributaries.Count; i++)
                {
                    var trib = grid.LakeFirstWaterGraph.Tributaries[i];
                    if (trib.Accepted)
                        claimed.Add(PackCell(trib.MainRiverConfluenceCell.x, trib.MainRiverConfluenceCell.y));
                }
            }

            return claimed;
        }

        static bool PassesInlandFeederValidation(
            GridSystem grid,
            List<Vector2Int> path,
            HashSet<long> claimedJoins,
            int minSepLake,
            int minJoinSpacing,
            int nearLakeBodyCells,
            out Vector2Int joinCell,
            out string reject)
        {
            joinCell = default;
            reject = null;
            if (grid == null || path == null || path.Count < 14)
            {
                reject = "path_too_short";
                return false;
            }

            if (RiverRouteGenerator.LastTributaryConfluencePlanValid)
                joinCell = RiverRouteGenerator.LastTributaryConfluencePlan.ConfluenceCell;
            else
                joinCell = path[path.Count - 1];

            foreach (long pk in claimedJoins)
            {
                int jx = (int)(pk >> 32);
                int jz = (int)(uint)pk;
                if (Chebyshev(joinCell, new Vector2Int(jx, jz)) < minJoinSpacing)
                {
                    reject = "join_spacing";
                    return false;
                }
            }

            if (MinDistPathToAnyLakeBody(grid, path, nearLakeBodyCells) <= nearLakeBodyCells)
            {
                reject = "path_near_lake_body";
                return false;
            }

            if (MinDistToLakeSpillNetwork(grid, path[0]) < minSepLake)
            {
                reject = "source_near_lake_spill";
                return false;
            }

            int bodyEnd = Mathf.Max(2, Mathf.RoundToInt(path.Count * 0.72f));
            for (int i = 0; i < bodyEnd; i++)
            {
                if (MinDistToLakeSpillNetwork(grid, path[i]) < minSepLake * 0.55f)
                {
                    reject = "path_near_lake_spill";
                    return false;
                }
            }

            if (CrossesLakeBody(path, grid))
            {
                reject = "crosses_lake_body";
                return false;
            }

            return true;
        }

        static int ResolveInlandNearLakePromoteCells(MapGenConfig config, int minDim)
        {
            int baseCells = config != null
                ? Mathf.Clamp(config.lakeMinChebyshevDistanceFromMainRiverCells / 2, 10, 18)
                : 12;
            if (minDim >= 320)
                return Mathf.Min(baseCells + 2, 20);
            return baseCells;
        }

        static int MinDistPathToAnyLakeBody(GridSystem grid, List<Vector2Int> path, int maxScan)
        {
            if (path == null || path.Count == 0)
                return maxScan + 1;

            // Prefer graph lakes; fallback a packed bodies reales (aunque lakesSelected=0).
            var lakes = grid?.LakeFirstWaterGraph?.Lakes;
            int best = maxScan + 1;
            int step = Mathf.Max(1, path.Count / 16);

            if (lakes != null && lakes.Count > 0)
            {
                for (int li = 0; li < lakes.Count; li++)
                {
                    var lake = lakes[li];
                    if (lake?.BodyCellsPacked == null || lake.BodyCellsPacked.Count == 0)
                        continue;
                    for (int i = 0; i < path.Count; i += step)
                    {
                        int d = MinChebyshevToPackedBody(path[i], lake.BodyCellsPacked, maxScan);
                        if (d < best)
                            best = d;
                        if (best == 0)
                            return 0;
                    }
                }
            }

            if (best <= maxScan)
                return best;

            if (grid?.LakeBodyCellsPacked != null && grid.LakeBodyCellsPacked.Count > 0)
            {
                for (int i = 0; i < path.Count; i += step)
                {
                    int d = MinChebyshevToPackedBody(path[i], grid.LakeBodyCellsPacked, maxScan);
                    if (d < best)
                        best = d;
                    if (best == 0)
                        return 0;
                }
            }

            return best;
        }

        static int MinChebyshevToPackedBody(Vector2Int cell, HashSet<long> body, int maxScan)
        {
            if (body == null || body.Count == 0)
                return maxScan + 1;
            if (body.Contains(PackCell(cell.x, cell.y)))
                return 0;

            int best = maxScan + 1;
            for (int dz = -maxScan; dz <= maxScan; dz++)
            {
                for (int dx = -maxScan; dx <= maxScan; dx++)
                {
                    int d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
                    if (d == 0 || d >= best)
                        continue;
                    if (body.Contains(PackCell(cell.x + dx, cell.y + dz)))
                        best = d;
                }
            }

            return best;
        }

        static bool CrossesLakeBody(List<Vector2Int> path, GridSystem grid)
        {
            if (grid?.LakeBodyCellsPacked == null || grid.LakeBodyCellsPacked.Count == 0 || path == null)
                return false;
            int limit = Mathf.Max(0, path.Count - 2);
            for (int i = 0; i < limit; i++)
            {
                if (grid.LakeBodyCellsPacked.Contains(PackCell(path[i].x, path[i].y)))
                    return true;
            }

            return false;
        }

        static int MinDistToLakeSpillNetwork(GridSystem grid, Vector2Int cell)
        {
            if (grid?.RiverCenterlinesCellSpace == null)
                return int.MaxValue;

            int best = int.MaxValue;
            for (int ri = 1; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
            {
                if (!UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, ri))
                    continue;

                var line = grid.RiverCenterlinesCellSpace[ri];
                if (line == null || line.Count < 2)
                    continue;

                for (int pi = 0; pi < line.Count; pi += 4)
                {
                    int lx = Mathf.RoundToInt(line[pi].x);
                    int lz = Mathf.RoundToInt(line[pi].y);
                    best = Mathf.Min(best, Chebyshev(cell, new Vector2Int(lx, lz)));
                }
            }

            if (grid.LakeFirstWaterGraph?.Tributaries != null)
            {
                for (int ti = 0; ti < grid.LakeFirstWaterGraph.Tributaries.Count; ti++)
                {
                    var trib = grid.LakeFirstWaterGraph.Tributaries[ti];
                    if (!trib.Accepted)
                        continue;
                    best = Mathf.Min(best, Chebyshev(cell, trib.LakeOutletCell));
                }
            }

            return best;
        }

        static int Chebyshev(Vector2Int a, Vector2Int b) =>
            Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        static void LogSummary(MapGenConfig config, UwpWaterGraph graph)
        {
            if (config == null || graph == null)
                return;
            if (!config.debugLogs && !config.debugHydrologyNetwork && !config.uwpOwnedVisualPolicy)
                return;

            var r = graph.SupplementalReport;
            Debug.Log(
                $"[LakeFirstSupplemental] seed={config.seed} inlandTarget={r.InlandFeederTarget} " +
                $"inlandAccepted={r.InlandFeedersAccepted} inlandRejected={r.InlandFeedersRejected} " +
                $"headwaterTarget={r.HeadwaterFeederTarget} headwaterAccepted={r.HeadwaterFeedersAccepted} " +
                $"headwaterRejected={r.HeadwaterFeedersRejected}");
            if (r.HeadwaterFeederTarget > 0 && r.HeadwaterFeedersAccepted == 0)
            {
                Debug.LogWarning(
                    $"[LakeFirstSupplemental] seed={config.seed} headwater none accepted " +
                    $"(target={r.HeadwaterFeederTarget} rejected={r.HeadwaterFeedersRejected})");
                int shown = 0;
                for (int i = r.RejectLines.Count - 1; i >= 0 && shown < 8; i--)
                {
                    string line = r.RejectLines[i];
                    if (string.IsNullOrEmpty(line) || line.IndexOf("headwater", System.StringComparison.Ordinal) < 0)
                        continue;
                    Debug.LogWarning($"[LakeFirstSupplemental] reject[{shown}] {line}");
                    shown++;
                }
            }
        }
    }
}
