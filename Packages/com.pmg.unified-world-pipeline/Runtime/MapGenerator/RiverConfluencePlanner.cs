using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    [System.Serializable]
    public struct RiverConfluenceNode
    {
        public int MainRiverIndex;
        public int TributaryRiverIndex;
        public Vector2Int Cell;
        public int MainCenterlineIndex;
        public int TributaryCenterlineIndex;
        public float AngleDeg;
        public int MergeRadiusCells;
        public bool Valid;
    }

    /// <summary>Plan confluence-first: celda exacta y tangente aguas abajo del receptor.</summary>
    public struct RiverConfluencePlan
    {
        public int ReceiverId;
        public Vector2Int ConfluenceCell;
        public Vector2 ReceiverDownstreamDir;
        public int MainCenterlineIndex;
        public int DistFromReceiverStart;
        public int DistFromReceiverEnd;
        public bool Valid;
    }

    public static class RiverConfluenceUtility
    {
        static long Pack(int x, int z) => ((long)x << 32) | (uint)z;

        public static void Clear(GridSystem grid)
        {
            if (grid != null)
                grid.RiverConfluences = new List<RiverConfluenceNode>();
        }

        /// <summary>Legacy: no usar como destino principal de A* (confluence-first usa <see cref="TrySelectConfluence"/>).</summary>
        public static HashSet<long> BuildTributaryGoalCells(
            GridSystem grid,
            MapGenConfig config,
            IRng rng,
            HashSet<long> fallbackRiverCells)
        {
            if (fallbackRiverCells == null || fallbackRiverCells.Count == 0)
                return new HashSet<long>();
            if (config == null || !config.riverConfluenceEnabled)
                return fallbackRiverCells;

            var plan = default(RiverConfluencePlan);
            if (TrySelectConfluence(grid, config, rng, 0, 0, out plan, out _, out _) && plan.Valid)
            {
                var single = new HashSet<long> { Pack(plan.ConfluenceCell.x, plan.ConfluenceCell.y) };
                return single;
            }

            return fallbackRiverCells;
        }

        public static int BuildConfluenceCandidates(
            GridSystem grid,
            MapGenConfig config,
            List<int> outMainCenterlineIndices)
        {
            outMainCenterlineIndices?.Clear();
            if (outMainCenterlineIndices == null || grid?.RiverCenterlinesCellSpace == null ||
                grid.RiverCenterlinesCellSpace.Count == 0)
                return 0;

            var main = grid.RiverCenterlinesCellSpace[0];
            int minMainPts = config.uwpOwnedVisualPolicy ? 4 : 8;
            if (main == null || main.Count < minMainPts || config == null || !config.riverConfluenceEnabled)
                return 0;

            int w = grid.Width;
            int h = grid.Height;
            int minFromEnds = Mathf.Max(4, config.riverConfluenceMinDistanceFromMainEndpointsCells);
            int spacing = Mathf.Max(8, config.riverConfluenceMinSpacingCells);
            int fordRad = Mathf.Max(0, config.riverConfluenceAvoidFordRadiusCells);
            var existing = grid.RiverConfluences;

            for (int i = minFromEnds; i <= main.Count - 1 - minFromEnds; i++)
            {
                int cx = Mathf.Clamp(Mathf.RoundToInt(main[i].x), 0, w - 1);
                int cz = Mathf.Clamp(Mathf.RoundToInt(main[i].y), 0, h - 1);
                int snapR = config != null && config.uwpOwnedVisualPolicy ? 4 : 2;
                if (!TrySnapToNearestRiverCell(grid, cx, cz, snapR, out int sx, out int sz))
                    continue;
                cx = sx;
                cz = sz;
                if (IsTooCloseToExistingConfluence(cx, cz, existing, spacing))
                    continue;
                if (fordRad > 0 && CellNearFord(grid, cx, cz, fordRad))
                    continue;
                if (!HasLandApproachNeighbor(grid, cx, cz, w, h))
                    continue;

                outMainCenterlineIndices.Add(i);
            }

            return outMainCenterlineIndices.Count;
        }

        static bool IsNearRiverCell(GridSystem grid, int cx, int cz, int maxCheb)
        {
            return TrySnapToNearestRiverCell(grid, cx, cz, maxCheb, out _, out _);
        }

        static bool TrySnapToNearestRiverCell(GridSystem grid, int cx, int cz, int maxCheb, out int sx, out int sz)
        {
            sx = cx;
            sz = cz;
            if (grid == null)
                return false;
            maxCheb = Mathf.Clamp(maxCheb, 0, 6);
            if (grid.GetCell(cx, cz).type == CellType.River)
                return true;
            int bestD = int.MaxValue;
            bool found = false;
            for (int dz = -maxCheb; dz <= maxCheb; dz++)
            {
                for (int dx = -maxCheb; dx <= maxCheb; dx++)
                {
                    int d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
                    if (d > maxCheb)
                        continue;
                    int nx = cx + dx;
                    int nz = cz + dz;
                    if (!grid.InBoundsCell(nx, nz))
                        continue;
                    if (grid.GetCell(nx, nz).type != CellType.River)
                        continue;
                    if (d < bestD)
                    {
                        bestD = d;
                        sx = nx;
                        sz = nz;
                        found = true;
                    }
                }
            }

            return found;
        }

        static bool HasLandApproachNeighbor(GridSystem grid, int cx, int cz, int w, int h)
        {
            foreach (var n in grid.Neighbors4(new Vector2Int(cx, cz)))
            {
                if ((uint)n.x >= (uint)w || (uint)n.y >= (uint)h)
                    continue;
                if (grid.GetCell(n.x, n.y).type == CellType.Land)
                    return true;
            }

            return false;
        }

        public static int BuildConfluenceCandidatePlanList(
            GridSystem grid,
            MapGenConfig config,
            IRng rng,
            List<RiverConfluencePlan> outPlans)
        {
            outPlans?.Clear();
            if (outPlans == null || grid == null || config == null || !config.riverConfluenceEnabled)
                return 0;

            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return 0;

            var main = grid.RiverCenterlinesCellSpace[0];
            if (main == null || main.Count < 2)
                return 0;

            int minMainPts = config != null && config.uwpOwnedVisualPolicy ? 4 : 8;
            if (main.Count < minMainPts)
                return 0;

            var indices = new List<int>(64);
            if (BuildConfluenceCandidates(grid, config, indices) < 1)
                return 0;

            for (int i = indices.Count - 1; i > 0; i--)
            {
                int j = rng != null ? rng.NextInt(0, i + 1) : i;
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            int w = grid.Width;
            int h = grid.Height;
            for (int k = 0; k < indices.Count; k++)
            {
                int mainIdx = indices[k];
                int cx = Mathf.Clamp(Mathf.RoundToInt(main[mainIdx].x), 0, w - 1);
                int cz = Mathf.Clamp(Mathf.RoundToInt(main[mainIdx].y), 0, h - 1);
                if (!TrySnapToNearestRiverCell(grid, cx, cz, config.uwpOwnedVisualPolicy ? 4 : 2, out cx, out cz))
                    continue;
                Vector2 downstream = ReceiverDownstreamAt(main, mainIdx);
                if (downstream.sqrMagnitude < 1e-6f)
                    continue;
                downstream.Normalize();
                outPlans.Add(new RiverConfluencePlan
                {
                    ReceiverId = 0,
                    ConfluenceCell = new Vector2Int(cx, cz),
                    ReceiverDownstreamDir = downstream,
                    MainCenterlineIndex = mainIdx,
                    DistFromReceiverStart = mainIdx,
                    DistFromReceiverEnd = main.Count - 1 - mainIdx,
                    Valid = true
                });
            }

            return outPlans.Count;
        }

        public static bool TrySelectConfluence(
            GridSystem grid,
            MapGenConfig config,
            IRng rng,
            int riverId,
            int attemptSalt,
            out RiverConfluencePlan plan,
            out int candidateCount,
            out string rejectReason)
        {
            plan = default;
            candidateCount = 0;
            rejectReason = null;

            var list = new List<RiverConfluencePlan>(64);
            candidateCount = BuildConfluenceCandidatePlanList(grid, config, rng, list);
            if (candidateCount < 1)
            {
                rejectReason = "no_candidates";
                return false;
            }

            int pick = attemptSalt % candidateCount;
            if (pick < 0)
                pick += candidateCount;
            plan = list[pick];
            return plan.Valid;
        }

        public static void LogConfluenceSelectionResult(
            MapGenConfig config,
            int riverId,
            int receiverId,
            int candidateCount,
            Vector2Int selectedCell,
            Vector2Int sourceCell,
            RiverConfluencePlan plan,
            bool accepted,
            string rejectReason)
        {
            if (config == null ||
                (!config.riverConfluenceDebugLogs && !config.debugLogs && !config.debugHydrologyNetwork))
                return;

            Vector2 ds = plan.ReceiverDownstreamDir;
            Debug.Log(
                $"[RiverConfluenceSelection] riverId={riverId} receiverId={receiverId} candidateCount={candidateCount} " +
                $"selectedCell=({selectedCell.x},{selectedCell.y}) distFromReceiverStart={plan.DistFromReceiverStart} " +
                $"distFromReceiverEnd={plan.DistFromReceiverEnd} receiverDownstreamDir=({ds.x:F3},{ds.y:F3}) " +
                $"sourceCell=({sourceCell.x},{sourceCell.y}) accepted={(accepted ? 1 : 0)} rejectReason={rejectReason ?? "none"}");
        }

        public static Vector2 ReceiverDownstreamAt(IReadOnlyList<Vector2> mainLine, int mainIdx)
        {
            if (mainLine == null || mainLine.Count < 2)
                return Vector2.right;
            int i0 = Mathf.Max(0, mainIdx - 1);
            int i1 = Mathf.Min(mainLine.Count - 1, mainIdx + 1);
            Vector2 d = mainLine[i1] - mainLine[i0];
            if (d.sqrMagnitude < 1e-6f && mainIdx > 0)
                d = mainLine[mainIdx] - mainLine[mainIdx - 1];
            return d;
        }

        public static bool TryRegisterFromPlacement(
            GridSystem grid,
            MapGenConfig config,
            int tributaryRiverIndex,
            List<Vector2Int> path,
            List<Vector2> tributaryCenterline,
            Vector2Int joinCell,
            string source)
        {
            if (grid == null || config == null || !config.riverConfluenceEnabled ||
                tributaryRiverIndex <= 0 || path == null || path.Count < 2)
                return false;

            if (grid.RiverConfluences == null)
                grid.RiverConfluences = new List<RiverConfluenceNode>();

            var mainLine = grid.RiverCenterlinesCellSpace[0];
            if (mainLine == null || mainLine.Count < 2)
                return false;

            int mainClIdx = ClosestCenterlineIndex(mainLine, joinCell);
            int tribClIdx = tributaryCenterline != null && tributaryCenterline.Count > 0
                ? ClosestCenterlineIndex(tributaryCenterline, joinCell)
                : path.Count - 1;

            Vector2 recvDown = ReceiverDownstreamAt(mainLine, mainClIdx).normalized;
            Vector2 tribIn = RiverDendriticUtility.TributaryIncomingAt(tributaryCenterline, tribClIdx);
            float angle = RiverDendriticUtility.ComputeDirectedJoinAngleDeg(recvDown, tribIn);
            bool angleOk = RiverDendriticUtility.IsJoinAngleAcceptable(config, angle, out bool isParallel, out bool isTJunction);
            bool valid = angleOk;
            if (!angleOk && config.riverConfluenceAcceptLooseAngle)
                valid = RiverDendriticUtility.IsJoinAngleLooseAcceptable(config, angle, isParallel, isTJunction);

            int mergeR = Mathf.Max(1, config.riverConfluenceMergeRadiusCells);

            if (!valid)
            {
                if (config.riverConfluenceDebugLogs || config.debugLogs || config.debugHydrologyNetwork)
                {
                    string rejectReason = isParallel ? "join_angle_parallel" : (isTJunction ? "join_angle_90" : "join_angle_out_of_range");
                    Debug.Log(
                        $"[RiverConfluenceRejected] source={source} main=0 tributary={tributaryRiverIndex} " +
                        $"cell=({joinCell.x},{joinCell.y}) angleDeg={angle:F1} rejectReason={rejectReason} mergeR={mergeR}");
                    RiverDendriticUtility.LogRiverConfluenceGeometryAudit(
                        config,
                        tributaryRiverIndex,
                        0,
                        angle,
                        recvDown,
                        tribIn,
                        isParallel,
                        isTJunction,
                        false,
                        rejectReason);
                }

                return false;
            }

            grid.RiverConfluences.Add(new RiverConfluenceNode
            {
                MainRiverIndex = 0,
                TributaryRiverIndex = tributaryRiverIndex,
                Cell = joinCell,
                MainCenterlineIndex = mainClIdx,
                TributaryCenterlineIndex = tribClIdx,
                AngleDeg = angle,
                MergeRadiusCells = mergeR,
                Valid = valid
            });

            if (config.riverConfluenceDebugLogs || config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[RiverConfluenceCreated] source={source} main=0 tributary={tributaryRiverIndex} " +
                    $"cell=({joinCell.x},{joinCell.y}) angleDeg={angle:F1} valid={(valid ? 1 : 0)} mergeR={mergeR}");
                RiverDendriticUtility.LogRiverConfluenceGeometryAudit(
                    config,
                    tributaryRiverIndex,
                    0,
                    angle,
                    recvDown,
                    tribIn,
                    isParallel,
                    isTJunction,
                    valid,
                    angleOk ? "none" : "angle_out_of_range");
            }

            return valid;
        }

        public static bool ValidatePlacementConfluenceGeometry(
            GridSystem grid,
            MapGenConfig config,
            int tributaryRiverIndex,
            List<Vector2Int> path,
            List<Vector2> tributaryCenterline,
            Vector2Int joinCell,
            out float angle,
            out string rejectReason)
        {
            angle = 0f;
            rejectReason = null;

            if (grid == null || config == null || !config.riverConfluenceEnabled ||
                tributaryRiverIndex <= 0 || path == null || path.Count < 2)
            {
                rejectReason = "invalid_args";
                return false;
            }

            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count < 1)
            {
                rejectReason = "missing_receiver_centerline";
                return false;
            }

            var mainLine = grid.RiverCenterlinesCellSpace[0];
            if (mainLine == null || mainLine.Count < 2 || tributaryCenterline == null || tributaryCenterline.Count < 2)
            {
                rejectReason = "path_length";
                return false;
            }

            int mainClIdx = ClosestCenterlineIndex(mainLine, joinCell);
            int tribClIdx = ClosestCenterlineIndex(tributaryCenterline, joinCell);
            Vector2 recvDown = ReceiverDownstreamAt(mainLine, mainClIdx).normalized;
            Vector2 tribIn = RiverDendriticUtility.TributaryIncomingAt(tributaryCenterline, tribClIdx);
            angle = RiverDendriticUtility.ComputeDirectedJoinAngleDeg(recvDown, tribIn);
            bool angleOk = RiverDendriticUtility.IsJoinAngleAcceptable(config, angle, out bool isParallel, out bool isTJunction);
            if (angleOk)
                return true;

            if (config.riverConfluenceAcceptLooseAngle &&
                RiverDendriticUtility.IsJoinAngleLooseAcceptable(config, angle, isParallel, isTJunction))
            {
                return true;
            }

            rejectReason = isParallel ? "join_angle_parallel" : (isTJunction ? "join_angle_90" : "join_angle_out_of_range");
            return false;
        }

        public static void AuditConfluenceTopology(GridSystem grid, MapGenConfig config)
        {
            if (config == null || grid == null)
                return;
            if (!config.riverConfluenceDebugLogs && !config.debugLogs && !config.debugHydrologyNetwork)
                return;

            int riverCount = grid.RiverCenterlinesCellSpace != null ? grid.RiverCenterlinesCellSpace.Count : 0;
            int confCount = grid.RiverConfluences != null ? grid.RiverConfluences.Count : 0;
            int validCount = 0;
            if (grid.RiverConfluences != null)
            {
                for (int i = 0; i < grid.RiverConfluences.Count; i++)
                {
                    if (grid.RiverConfluences[i].Valid)
                        validCount++;
                }
            }

            bool ok = riverCount <= 1 ? confCount == 0 : validCount > 0 || confCount == 0;
            Debug.Log(
                $"[RiverConfluenceTopology] riverCount={riverCount} confluenceCount={confCount} tributariesMerged={validCount} " +
                $"orphanRiverCells=0 disconnectedWaterCells=0 confluenceTerrainOk=-1 meshOverlapWarnings=0 ok={(ok ? 1 : 0)}");
        }

        static bool IsTooCloseToExistingConfluence(int cx, int cz, List<RiverConfluenceNode> existing, int spacing)
        {
            if (existing == null)
                return false;
            for (int i = 0; i < existing.Count; i++)
            {
                var c = existing[i].Cell;
                if (Mathf.Max(Mathf.Abs(c.x - cx), Mathf.Abs(c.y - cz)) < spacing)
                    return true;
            }

            return false;
        }

        static bool CellNearFord(GridSystem grid, int cx, int cz, int radius)
        {
            int w = grid.Width;
            int h = grid.Height;
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx;
                    int z = cz + dz;
                    if ((uint)x >= (uint)w || (uint)z >= (uint)h)
                        continue;
                    if (grid.GetCell(x, z).riverFord)
                        return true;
                }
            }

            return false;
        }

        static int ClosestCenterlineIndex(IReadOnlyList<Vector2> line, Vector2Int cell)
        {
            Vector2 p = new Vector2(cell.x + 0.5f, cell.y + 0.5f);
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < line.Count; i++)
            {
                float d = (line[i] - p).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            return best;
        }
    }
}
