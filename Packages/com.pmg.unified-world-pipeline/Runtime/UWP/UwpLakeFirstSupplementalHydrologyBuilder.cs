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
            // Reservar al menos N slots para Headwater según desire tipado; inland no se come el cupo.
            int headwaterDesire = ResolveHeadwaterDesire(config, minDim);
            int headwaterReserve = headwaterDesire > 0 && missingTribSlots > 0
                ? Mathf.Min(headwaterDesire, missingTribSlots)
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
            int maxAttempts = Mathf.Clamp(target * 36, 24, 96);

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
                    int spillCap = UwpLakeFirstHydrologyBuilder.ResolveLakeSpillCap(
                        config, Mathf.Max(0, config.riverCount - 1));
                    int spillNow = CountAcceptedLakeSpillTributaries(graph, grid);
                    if (spillNow < spillCap &&
                        UwpLakeFirstHydrologyBuilder.TryPromoteSpillForUnownedLake(
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

                        continue;
                    }

                    // Cupo spill lleno o promote falló: no quemar el intento;
                    // intentar colocar inland (o fail por PassesInlandFeederValidation).
                    if (spillNow >= spillCap)
                    {
                        graph.SupplementalReport.RejectLines.Add(
                            $"attempt={attempt} reason=near_unowned_lake_spill_cap_fallback_inland lake={nearLakeIdx} spill={spillNow}/{spillCap}");
                    }
                    else
                    {
                        graph.SupplementalReport.RejectLines.Add(
                            $"attempt={attempt} reason=near_unowned_lake_spill_failed_fallback_inland lake={nearLakeIdx}");
                    }
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
                    // Sin fords de placement: vados fantasma en seco / sin ribbon continuo.
                    null,
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

        /// <summary>
        /// Inland/Headwater: revoca riverFord en cauce suplemental (placement/thin-zone).
        /// Llamar DESPUÉS de ApplyFunctionalRiverFordsFromThinZones.
        /// No toca vados del main cerca de la confluencia.
        /// </summary>
        public static void ClearFordsAlongSupplementalRivers(GridSystem grid)
        {
            if (grid?.RiverCenterlinesCellSpace == null)
                return;

            int w = grid.Width;
            int h = grid.Height;
            const int clearR = 1;
            var mainLine = grid.RiverCenterlinesCellSpace.Count > 0
                ? grid.RiverCenterlinesCellSpace[0]
                : null;

            for (int ri = 1; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
            {
                if (!UwpTributaryOriginUtility.IsSupplemental(grid, ri))
                    continue;
                var line = grid.RiverCenterlinesCellSpace[ri];
                if (line == null || line.Count < 2)
                    continue;
                for (int i = 0; i < line.Count; i++)
                {
                    int cx = Mathf.Clamp(Mathf.FloorToInt(line[i].x), 0, w - 1);
                    int cz = Mathf.Clamp(Mathf.FloorToInt(line[i].y), 0, h - 1);
                    for (int dz = -clearR; dz <= clearR; dz++)
                    for (int dx = -clearR; dx <= clearR; dx++)
                    {
                        int x = cx + dx;
                        int z = cz + dz;
                        if (!grid.InBoundsCell(x, z))
                            continue;
                        // Conservar fords del troncal en la junta.
                        if (mainLine != null && MinDistToCenterlineCells(mainLine, x, z) <= 2.25f)
                            continue;
                        ref var cell = ref grid.GetCell(x, z);
                        if (cell.type == CellType.River && cell.riverFord)
                            cell.riverFord = false;
                    }
                }
            }
        }

        static float MinDistToCenterlineCells(List<Vector2> line, int x, int z)
        {
            if (line == null || line.Count == 0)
                return float.MaxValue;
            Vector2 p = new Vector2(x + 0.5f, z + 0.5f);
            float best = float.MaxValue;
            for (int i = 0; i < line.Count; i++)
            {
                float d = (line[i] - p).sqrMagnitude;
                if (d < best)
                    best = d;
            }

            return Mathf.Sqrt(best);
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

            // Preferir solo InlandFeeder en el primer pase: rotar a spill agota candidatos cerca del lago.
            var inlandOnly = new List<int>(2);
            for (int i = 0; i < receivers.Count; i++)
            {
                if (UwpTributaryOriginUtility.IsInlandFeeder(grid, receivers[i]))
                    inlandOnly.Add(receivers[i]);
            }

            int minJoinSpacing = Mathf.Clamp(config.inlandFeederMinConfluenceSpacingCells, 10, 28);
            int accepted = 0;
            int rejected = 0;
            int attempt = 0;
            int maxAttempts = Mathf.Clamp(target * 28, 20, 96);
            var primaryReceivers = inlandOnly.Count > 0 ? inlandOnly : receivers;

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
                primaryReceivers,
                minJoinSpacing,
                target,
                maxAttempts,
                relaxedAngle: false,
                ref accepted,
                ref rejected,
                ref attempt);

            if (accepted < target)
            {
                int fallbackAttempts = Mathf.Clamp(target * 28, 16, 64);
                // Fallback relaxed: inland primero otra vez; si falla, todos los receptores.
                var fallbackReceivers = inlandOnly.Count > 0 ? inlandOnly : receivers;
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
                    fallbackReceivers,
                    Mathf.Max(8, (minJoinSpacing * 2) / 3),
                    target,
                    fallbackAttempts,
                    relaxedAngle: true,
                    ref accepted,
                    ref rejected,
                    ref attempt);
            }

            if (accepted < target && inlandOnly.Count > 0 && receivers.Count > inlandOnly.Count)
            {
                // Último recurso: spill receptors con ángulo relajado.
                int spillAttempts = Mathf.Clamp(target * 16, 12, 40);
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
                    Mathf.Max(8, (minJoinSpacing * 2) / 3),
                    target,
                    spillAttempts,
                    relaxedAngle: true,
                    ref accepted,
                    ref rejected,
                    ref attempt);
            }

            // Inland corto → BuildConfluenceCandidatesForReceiver suele devolver 0 (no_candidates).
            if (accepted < target)
            {
                for (int i = 0; i < inlandOnly.Count && accepted < target; i++)
                {
                    if (TryEmergencyHeadwaterOntoReceiver(
                            grid,
                            config,
                            rng,
                            inlandOnly[i],
                            claimedJoins,
                            riverOccupiedCells,
                            ref riverOccAabbValid,
                            ref riverOccMinX,
                            ref riverOccMaxX,
                            ref riverOccMinZ,
                            ref riverOccMaxZ,
                            graph,
                            ref waterCells,
                            out string emergencyFail))
                    {
                        accepted++;
                    }
                    else
                    {
                        rejected++;
                        graph.SupplementalReport.RejectLines.Add(
                            $"headwater attempt=emergency relaxed=1 reason={emergencyFail ?? "emergency_failed"}");
                    }
                }
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
                    null,
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

        /// <summary>
        /// Fallback cuando InlandFeeder es corto y BuildConfluenceCandidatesForReceiver → no_candidates.
        /// Muestrea el cuerpo del receptor y traza un feeder corto en tierra hacia él.
        /// </summary>
        static bool TryEmergencyHeadwaterOntoReceiver(
            GridSystem grid,
            MapGenConfig config,
            IRng rng,
            int receiverRi,
            HashSet<long> claimedJoins,
            HashSet<long> riverOccupiedCells,
            ref bool riverOccAabbValid,
            ref int riverOccMinX,
            ref int riverOccMaxX,
            ref int riverOccMinZ,
            ref int riverOccMaxZ,
            UwpWaterGraph graph,
            ref int waterCells,
            out string fail)
        {
            fail = null;
            if (grid?.RiverCenterlinesCellSpace == null || config == null || rng == null)
            {
                fail = "emergency_null";
                return false;
            }

            if (receiverRi <= 0 || receiverRi >= grid.RiverCenterlinesCellSpace.Count)
            {
                fail = "emergency_bad_receiver";
                return false;
            }

            int slotIndex = grid.RiverCenterlinesCellSpace.Count;
            if (slotIndex >= config.riverCount)
            {
                fail = "emergency_no_slot";
                return false;
            }

            var recv = grid.RiverCenterlinesCellSpace[receiverRi];
            if (recv == null || recv.Count < 6)
            {
                fail = "emergency_recv_short";
                return false;
            }

            int w = grid.Width;
            int h = grid.Height;
            // Cuerpo medio: ~45–55% a lo largo (T centrica, no punta).
            int minFromEnds = Mathf.Max(4, Mathf.RoundToInt(recv.Count * 0.28f));
            int joinIdx = Mathf.Clamp(
                Mathf.RoundToInt(recv.Count * 0.48f),
                minFromEnds,
                recv.Count - 1 - minFromEnds);
            for (int tryOff = 0; tryOff < 10; tryOff++)
            {
                int idx = Mathf.Clamp(
                    joinIdx + ((tryOff % 2 == 0) ? tryOff / 2 : -(tryOff / 2 + 1)),
                    minFromEnds,
                    recv.Count - 1 - minFromEnds);
                Vector2 jp = recv[idx];
                var joinCell = new Vector2Int(
                    Mathf.Clamp(Mathf.FloorToInt(jp.x), 0, w - 1),
                    Mathf.Clamp(Mathf.FloorToInt(jp.y), 0, h - 1));

                if (!JoinIsOnReceiverMidBody(grid, receiverRi, joinCell, relaxedAngle: true))
                    continue;
                bool joinBusy = false;
                foreach (long pk in claimedJoins)
                {
                    int jx = (int)(pk >> 32);
                    int jz = (int)(uint)pk;
                    if (Chebyshev(joinCell, new Vector2Int(jx, jz)) < 6)
                    {
                        joinBusy = true;
                        break;
                    }
                }

                if (joinBusy)
                    continue;
                if (CrossesLakeBody(new List<Vector2Int> { joinCell }, grid))
                    continue;
                if (MinDistJoinToLakeMouthOrBody(grid, joinCell) < 8)
                    continue;

                Vector2 down = RiverConfluenceUtility.ReceiverDownstreamAt(recv, idx);
                if (down.sqrMagnitude < 1e-6f)
                    down = Vector2.right;
                down.Normalize();
                Vector2 side = new Vector2(-down.y, down.x);
                if (rng.NextFloat() < 0.5f)
                    side = -side;

                int reach = Mathf.Clamp(12 + rng.NextInt(0, 10), 12, 28);
                Vector2Int source = new Vector2Int(
                    Mathf.Clamp(joinCell.x + Mathf.RoundToInt(side.x * reach), 1, w - 2),
                    Mathf.Clamp(joinCell.y + Mathf.RoundToInt(side.y * reach), 1, h - 2));

                if (!TryBuildEmergencyHeadwaterPath(grid, source, joinCell, out List<Vector2Int> path) ||
                    path == null || path.Count < 6)
                    continue;

                if (CrossesLakeBody(path, grid))
                    continue;

                if (HeadwaterPathTouchesMainRiver(grid, path, joinCell))
                    continue;

                if (JoinTooCloseToReceiverMainMouth(grid, receiverRi, joinCell, 12))
                    continue;

                var centerline = new List<Vector2>(path.Count);
                for (int p = 0; p < path.Count; p++)
                    centerline.Add(new Vector2(path[p].x + 0.5f, path[p].y + 0.5f));
                UwpTributaryOriginUtility.PinEndpointConfluence(centerline, joinCell);

                var fordCells = new List<Vector2Int>(0);
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
                    fail = "emergency_raster_failed";
                    continue;
                }

                waterCells += added;
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
                    "headwater_feeder_emergency_inland",
                    receiverRi);

                if (config.debugLogs || config.debugHydrologyNetwork || config.uwpOwnedVisualPolicy)
                {
                    Debug.Log(
                        $"[LakeFirstSupplemental] kind=HeadwaterFeeder riverIndex={slotIndex} receiver={receiverRi} " +
                        $"relaxed=1 source=({path[0].x},{path[0].y}) join=({joinCell.x},{joinCell.y}) " +
                        $"accepted=1 mode=emergency_inland seed={config.seed}");
                }

                fail = null;
                return true;
            }

            fail = fail ?? "emergency_no_join";
            return false;
        }

        static bool TryBuildEmergencyHeadwaterPath(
            GridSystem grid,
            Vector2Int source,
            Vector2Int join,
            out List<Vector2Int> path)
        {
            path = null;
            if (grid == null)
                return false;

            path = new List<Vector2Int>(48);
            int x = source.x;
            int y = source.y;
            path.Add(new Vector2Int(x, y));
            int guard = 0;
            while ((x != join.x || y != join.y) && guard++ < 128)
            {
                int dx = join.x == x ? 0 : (join.x > x ? 1 : -1);
                int dy = join.y == y ? 0 : (join.y > y ? 1 : -1);
                if (Mathf.Abs(join.x - x) >= Mathf.Abs(join.y - y))
                    x += dx;
                else
                    y += dy;
                x = Mathf.Clamp(x, 0, grid.Width - 1);
                y = Mathf.Clamp(y, 0, grid.Height - 1);
                var next = new Vector2Int(x, y);
                if (path[path.Count - 1] != next)
                    path.Add(next);
                if (grid.LakeBodyCellsPacked != null &&
                    grid.LakeBodyCellsPacked.Contains(PackCell(x, y)))
                    return false;
                // No atravesar el Main en emergencia Manhattan.
                if (grid.RiverCenterlinesCellSpace != null &&
                    grid.RiverCenterlinesCellSpace.Count > 0 &&
                    (x != join.x || y != join.y) &&
                    CellTouchesMainRiverCenterline(grid, new Vector2Int(x, y), maxChebyshev: 0))
                    return false;
            }

            if (path.Count > 0 && path[path.Count - 1] != join)
                path.Add(join);

            return path.Count >= 6;
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
            if (grid == null || path == null || path.Count < (relaxedAngle ? 6 : 8))
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

            if (HeadwaterPathTouchesMainRiver(grid, path, joinCell))
            {
                reject = "crosses_main_river";
                return false;
            }

            int minLakeSep = ResolveHeadwaterMinSeparationFromLakeCells(config, Mathf.Min(grid.Width, grid.Height));
            // Sin InlandFeeder, el receptor suele ser LakeSpill: sep estricta ≈ nunca hay sitio.
            // Relajar distancia a lago; spill más, inland aún más (join lejos de boca lago).
            bool receiverIsSpill = UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, receiverRiverIndex);
            bool receiverIsInland = UwpTributaryOriginUtility.IsInlandFeeder(grid, receiverRiverIndex);
            if (receiverIsSpill)
                minLakeSep = Mathf.Max(10, (minLakeSep * 2) / 3);
            else if (receiverIsInland)
                minLakeSep = Mathf.Max(8, (minLakeSep * 1) / 2);
            if (relaxedAngle)
                minLakeSep = Mathf.Max(6, (minLakeSep * 2) / 3);

            if (MinDistJoinToLakeMouthOrBody(grid, joinCell) < minLakeSep)
            {
                reject = "join_near_lake_mouth";
                return false;
            }

            int sourceLakeSep = Mathf.Max(8, minLakeSep * 2 / 3);
            if (relaxedAngle)
                sourceLakeSep = Mathf.Max(6, sourceLakeSep * 2 / 3);
            if (MinChebyshevToAnyLakeBodyCell(grid, path[0], minLakeSep) < sourceLakeSep)
            {
                reject = "source_near_lake";
                return false;
            }

            // Solo relevante cuando el receptor es un spill (cabeza hacia el lago).
            if (receiverIsSpill &&
                JoinTooCloseToLakeEndOfReceiver(grid, receiverRiverIndex, joinCell, minLakeSep))
            {
                reject = "join_near_lake_spill_head";
                return false;
            }

            // Inland: T en el cuerpo (≈30–70%). Unir en la punta → V / “nariz” (imgs).
            if (receiverIsInland &&
                !JoinIsOnReceiverMidBody(grid, receiverRiverIndex, joinCell, relaxedAngle))
            {
                reject = "join_not_on_receiver_midbody";
                return false;
            }

            // Evitar slide de headwater hacia la boca inland↔main (overlay del troncal).
            if (receiverIsInland)
            {
                int mouthSep = relaxedAngle ? 10 : 12;
                if (JoinTooCloseToReceiverMainMouth(grid, receiverRiverIndex, joinCell, mouthSep))
                {
                    reject = "join_near_receiver_main_mouth";
                    return false;
                }
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

        /// <summary>
        /// Headwater→Inland: join solo en el tramo central del receptor (evita punta/V).
        /// </summary>
        static bool JoinIsOnReceiverMidBody(
            GridSystem grid,
            int receiverRiverIndex,
            Vector2Int joinCell,
            bool relaxedAngle)
        {
            if (grid?.RiverCenterlinesCellSpace == null ||
                receiverRiverIndex <= 0 ||
                receiverRiverIndex >= grid.RiverCenterlinesCellSpace.Count)
                return false;

            var line = grid.RiverCenterlinesCellSpace[receiverRiverIndex];
            if (line == null || line.Count < 8)
                return false;

            int idx = ClosestCenterlineIndex(line, joinCell);
            float along01 = idx / (float)(line.Count - 1);
            // Estricto: 32–68%. Relajado: 26–74% (sigue lejos de ambos extremos).
            float lo = relaxedAngle ? 0.26f : 0.32f;
            float hi = relaxedAngle ? 0.74f : 0.68f;
            if (along01 < lo || along01 > hi)
                return false;

            // El tip debe pertenecer al receptor, no al Main adyacente.
            Vector2 joinF = new Vector2(joinCell.x + 0.5f, joinCell.y + 0.5f);
            float distSq = DistanceSqToPolylineCellSpaceStatic(joinF, line);
            float maxDist = relaxedAngle ? 2.35f : 1.85f;
            if (distSq > maxDist * maxDist)
                return false;

            // Margen Chebyshev a ambos extremos del inland.
            Vector2Int tip0 = new Vector2Int(
                Mathf.FloorToInt(line[0].x), Mathf.FloorToInt(line[0].y));
            Vector2Int tip1 = new Vector2Int(
                Mathf.FloorToInt(line[line.Count - 1].x), Mathf.FloorToInt(line[line.Count - 1].y));
            int minTipSep = relaxedAngle ? 8 : 10;
            if (Chebyshev(joinCell, tip0) < minTipSep || Chebyshev(joinCell, tip1) < minTipSep)
                return false;

            return true;
        }

        /// <summary>
        /// Rechaza joins de headwater cerca de la boca del receptor con el main
        /// (fuerza unión a mitad de cuerpo → T limpia, sin slide al troncal).
        /// </summary>
        static bool JoinTooCloseToReceiverMainMouth(
            GridSystem grid,
            int receiverRiverIndex,
            Vector2Int joinCell,
            int minSepCells)
        {
            if (grid == null || receiverRiverIndex <= 0 || minSepCells <= 0)
                return false;

            Vector2Int mouth = default;
            bool haveMouth = false;
            if (grid.LakeFirstWaterGraph?.Tributaries != null)
            {
                for (int i = 0; i < grid.LakeFirstWaterGraph.Tributaries.Count; i++)
                {
                    var trib = grid.LakeFirstWaterGraph.Tributaries[i];
                    if (!trib.Accepted || trib.RiverIndex != receiverRiverIndex)
                        continue;
                    mouth = trib.MainRiverConfluenceCell;
                    haveMouth = true;
                    break;
                }
            }

            if (!haveMouth &&
                grid.RiverCenterlinesCellSpace != null &&
                receiverRiverIndex < grid.RiverCenterlinesCellSpace.Count)
            {
                var line = grid.RiverCenterlinesCellSpace[receiverRiverIndex];
                if (line != null && line.Count >= 2)
                {
                    // Extremo más cercano al main (índice 0).
                    var mainLine = grid.RiverCenterlinesCellSpace[0];
                    if (mainLine != null && mainLine.Count >= 2)
                    {
                        Vector2 a = line[0];
                        Vector2 b = line[line.Count - 1];
                        float da = DistanceSqToPolylineCellSpaceStatic(a, mainLine);
                        float db = DistanceSqToPolylineCellSpaceStatic(b, mainLine);
                        Vector2 mout = da <= db ? a : b;
                        mouth = new Vector2Int(Mathf.FloorToInt(mout.x), Mathf.FloorToInt(mout.y));
                        haveMouth = true;
                    }
                }
            }

            if (!haveMouth)
                return false;

            if (Chebyshev(joinCell, mouth) < minSepCells)
                return true;

            // También rechazar tramo final 25% hacia la boca del main.
            if (grid.RiverCenterlinesCellSpace != null &&
                receiverRiverIndex < grid.RiverCenterlinesCellSpace.Count)
            {
                var line = grid.RiverCenterlinesCellSpace[receiverRiverIndex];
                if (line != null && line.Count >= 6)
                {
                    int idx = ClosestCenterlineIndex(line, joinCell);
                    float along01 = idx / (float)(line.Count - 1);
                    Vector2 end0 = line[0];
                    Vector2 end1 = line[line.Count - 1];
                    float d0 = (end0 - new Vector2(mouth.x + 0.5f, mouth.y + 0.5f)).sqrMagnitude;
                    float d1 = (end1 - new Vector2(mouth.x + 0.5f, mouth.y + 0.5f)).sqrMagnitude;
                    bool mouthAtEnd = d1 <= d0;
                    if (mouthAtEnd && along01 > 0.75f)
                        return true;
                    if (!mouthAtEnd && along01 < 0.25f)
                        return true;
                }
            }

            return false;
        }

        static float DistanceSqToPolylineCellSpaceStatic(Vector2 p, List<Vector2> line)
        {
            if (line == null || line.Count == 0)
                return float.MaxValue;
            if (line.Count == 1)
                return (p - line[0]).sqrMagnitude;
            float best = float.MaxValue;
            for (int i = 0; i < line.Count - 1; i++)
            {
                Vector2 a = line[i];
                Vector2 b = line[i + 1];
                Vector2 ab = b - a;
                float lenSq = ab.sqrMagnitude;
                Vector2 q = lenSq < 1e-8f ? a : a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
                float d = (p - q).sqrMagnitude;
                if (d < best)
                    best = d;
            }

            return best;
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

            // Cabeza→inland: preferir T (~55–125°), evita Y aguda / colita en orilla.
            if (angleOk && UwpTributaryOriginUtility.IsInlandFeeder(grid, receiverRiverIndex))
            {
                float lo = relaxedAngle ? 48f : 55f;
                float hi = relaxedAngle ? 132f : 125f;
                if (joinAngleDeg < lo || joinAngleDeg > hi)
                {
                    reject = "join_angle_not_t_to_inland";
                    return false;
                }
            }

            if (!angleOk && !relaxedAngle)
            {
                // Primera pasada: permitir ventana loose si el preferido falla (sin esperar fallback).
                angleOk = RiverDendriticUtility.IsJoinAngleLooseAcceptable(
                    config, joinAngleDeg, isParallel, isTJunction);
                if (angleOk && UwpTributaryOriginUtility.IsInlandFeeder(grid, receiverRiverIndex))
                {
                    if (joinAngleDeg < 48f || joinAngleDeg > 132f)
                        angleOk = false;
                }
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
            // OrigenKinds es la fuente de verdad post-raster (promote puede no
            // sincronizar graph.Tributaries al instante).
            int fromKinds = UwpLakeFirstHydrologyBuilder.CountLakeSpillRivers(grid);
            if (fromKinds > 0)
                return fromKinds;

            int count = 0;
            if (graph?.Tributaries == null)
                return 0;
            for (int i = 0; i < graph.Tributaries.Count; i++)
            {
                var trib = graph.Tributaries[i];
                if (trib.Accepted && trib.LakeComponentIndex >= 0)
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
            // Reglas InlandFeeder: recorrido útil inland→main (no “colita” corta).
            const int MinInlandPathCells = 40;
            const int MinInlandSpanChebyshev = 32;
            if (grid == null || path == null || path.Count < MinInlandPathCells)
            {
                reject = "path_too_short";
                return false;
            }

            if (RiverRouteGenerator.LastTributaryConfluencePlanValid)
                joinCell = RiverRouteGenerator.LastTributaryConfluencePlan.ConfluenceCell;
            else
                joinCell = path[path.Count - 1];

            int span = Chebyshev(path[0], joinCell);
            if (span < MinInlandSpanChebyshev)
            {
                reject = "path_span_short";
                return false;
            }

            // Path con demasiadas celdas para el span = zigzag tipo cola.
            float maxCellsForSpan = span * 1.75f + 10f;
            if (path.Count > maxCellsForSpan)
            {
                reject = "path_too_windy";
                return false;
            }

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

        /// <summary>
        /// Headwater no debe pisar/cruzar el Main antes del join (T solo al receptor inland/spill).
        /// Última celda (join) se excluye.
        /// </summary>
        static bool HeadwaterPathTouchesMainRiver(
            GridSystem grid,
            List<Vector2Int> path,
            Vector2Int joinCell)
        {
            if (grid?.RiverCenterlinesCellSpace == null ||
                grid.RiverCenterlinesCellSpace.Count == 0 ||
                path == null ||
                path.Count < 3)
                return false;

            int limit = Mathf.Max(0, path.Count - 1);
            for (int i = 0; i < limit; i++)
            {
                var c = path[i];
                if (c.x == joinCell.x && c.y == joinCell.y)
                    continue;
                // Radio 1: pisa corredor inmediato del troncal (overlay visual).
                if (CellTouchesMainRiverCenterline(grid, c, maxChebyshev: 1))
                    return true;
            }

            return false;
        }

        static bool CellTouchesMainRiverCenterline(GridSystem grid, Vector2Int cell, int maxChebyshev)
        {
            var main = grid.RiverCenterlinesCellSpace[0];
            if (main == null || main.Count < 2)
                return false;

            for (int i = 0; i < main.Count; i++)
            {
                int mx = Mathf.FloorToInt(main[i].x);
                int mz = Mathf.FloorToInt(main[i].y);
                if (Chebyshev(cell, new Vector2Int(mx, mz)) <= maxChebyshev)
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
                var tally = new System.Collections.Generic.Dictionary<string, int>(16);
                int shown = 0;
                for (int i = r.RejectLines.Count - 1; i >= 0; i--)
                {
                    string line = r.RejectLines[i];
                    if (string.IsNullOrEmpty(line) || line.IndexOf("headwater", System.StringComparison.Ordinal) < 0)
                        continue;
                    string reason = line;
                    int ri = line.LastIndexOf("reason=", System.StringComparison.Ordinal);
                    if (ri >= 0)
                        reason = line.Substring(ri + 7);
                    if (!tally.TryGetValue(reason, out int c))
                        c = 0;
                    tally[reason] = c + 1;
                    if (shown < 8)
                    {
                        Debug.LogWarning($"[LakeFirstSupplemental] reject[{shown}] {line}");
                        shown++;
                    }
                }

                if (tally.Count > 0)
                {
                    var parts = new System.Text.StringBuilder(128);
                    foreach (var kv in tally)
                    {
                        if (parts.Length > 0)
                            parts.Append(' ');
                        parts.Append(kv.Key).Append('=').Append(kv.Value);
                    }

                    Debug.LogWarning(
                        $"[LakeFirstSupplemental] seed={config.seed} headwaterRejectTally {parts}");
                }
            }
        }
    }
}
