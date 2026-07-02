using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    public enum RiverDendriticRole
    {
        MainCollector = 0,
        SecondaryTributary = 1,
        MediumStream = 2,
        HeadwaterStream = 3
    }

    /// <summary>Red dendrítica: roles, anchos coherentes, auditorías y shaping de confluencias (solo generación).</summary>
    public static class RiverDendriticUtility
    {
        static long Pack(int x, int z) => ((long)x << 32) | (uint)z;

        public static void EnsureRiverMetadata(GridSystem grid)
        {
            if (grid == null)
                return;
            if (grid.RiverDendriticRoles == null)
                grid.RiverDendriticRoles = new List<RiverDendriticRole>(8);
            if (grid.RiverReceiverIds == null)
                grid.RiverReceiverIds = new List<int>(8);
            if (grid.RiverWidthRatioToMain == null)
                grid.RiverWidthRatioToMain = new List<float>(8);
        }

        public static void ClearRiverMetadata(GridSystem grid)
        {
            if (grid == null)
                return;
            grid.RiverDendriticRoles?.Clear();
            grid.RiverReceiverIds?.Clear();
            grid.RiverWidthRatioToMain?.Clear();
        }

        public static RiverDendriticRole RoleForPlacement(int riverIndex, int receiverId, int pathLengthCells, int mainPathLengthCells)
        {
            if (riverIndex <= 0)
                return RiverDendriticRole.MainCollector;
            if (receiverId > 0)
                return RiverDendriticRole.MediumStream;
            if (mainPathLengthCells > 0 && pathLengthCells < Mathf.RoundToInt(mainPathLengthCells * 0.35f))
                return RiverDendriticRole.HeadwaterStream;
            return RiverDendriticRole.SecondaryTributary;
        }

        public static float WidthRatioToMain(MapGenConfig config, RiverDendriticRole role)
        {
            if (config == null || role == RiverDendriticRole.MainCollector)
                return 1f;
            switch (role)
            {
                case RiverDendriticRole.SecondaryTributary:
                    return Mathf.Clamp(config.riverSecondaryWidthRatioToMain, 0.45f, 0.85f);
                case RiverDendriticRole.MediumStream:
                    return Mathf.Clamp(config.riverMediumWidthRatioToMain, 0.25f, 0.60f);
                case RiverDendriticRole.HeadwaterStream:
                    return Mathf.Clamp(config.riverHeadwaterWidthRatioToMain, 0.12f, 0.35f);
                default:
                    return 0.65f;
            }
        }

        public static float VariableWidthRatioToMain(
            MapGenConfig config,
            RiverDendriticRole role,
            int riverIndex,
            int pathLengthCells,
            int mainPathLengthCells)
        {
            float baseRatio = WidthRatioToMain(config, role);
            if (role == RiverDendriticRole.MainCollector || riverIndex <= 0)
                return baseRatio;

            int bucket = Mathf.Abs(riverIndex * 37 + pathLengthCells * 11 + mainPathLengthCells * 3) % 4;
            float mul;
            switch (bucket)
            {
                case 0: mul = 1.00f; break;
                case 1: mul = 1.35f; break;
                case 2: mul = 1.65f; break;
                default: mul = 2.00f; break;
            }

            float cap;
            float floor;
            switch (role)
            {
                case RiverDendriticRole.SecondaryTributary:
                    cap = 0.82f;
                    floor = 0.48f;
                    break;
                case RiverDendriticRole.MediumStream:
                    cap = 0.66f;
                    floor = 0.30f;
                    break;
                case RiverDendriticRole.HeadwaterStream:
                    cap = 0.48f;
                    floor = 0.16f;
                    break;
                default:
                    cap = 0.82f;
                    floor = 0.22f;
                    break;
            }

            return Mathf.Clamp(baseRatio * mul, floor, cap);
        }

        public static int LogicalPaintRadiusCells(MapGenConfig config, RiverDendriticRole role)
        {
            if (config == null)
                return 1;
            int mainBase = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp(config.riverWidthRadiusCells, 0, 6) * 0.70f),
                0,
                6);
            if (role == RiverDendriticRole.MainCollector)
                return Mathf.Max(1, mainBase);
            float ratio = WidthRatioToMain(config, role);
            return Mathf.Clamp(Mathf.Max(1, Mathf.RoundToInt(mainBase * ratio)), 1, 6);
        }

        public static int LogicalPaintRadiusCells(MapGenConfig config, RiverDendriticRole role, float widthRatioToMain)
        {
            if (config == null)
                return 1;
            int mainBase = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp(config.riverWidthRadiusCells, 0, 6) * 0.70f),
                0,
                6);
            if (role == RiverDendriticRole.MainCollector)
                return Mathf.Max(1, mainBase);
            float ratio = Mathf.Clamp(widthRatioToMain, 0.12f, 0.85f);
            return Mathf.Clamp(Mathf.Max(1, Mathf.RoundToInt(mainBase * ratio)), 1, 6);
        }

        public static int CorridorMaxRadiusCells(MapGenConfig config, RiverDendriticRole role)
        {
            int paint = LogicalPaintRadiusCells(config, role);
            int amp = config != null
                ? Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(config.riverWidthNoiseAmplitudeCells, 0, 3) * 0.70f), 0, 3)
                : 0;
            if (role == RiverDendriticRole.MainCollector)
                return Mathf.Clamp(paint + amp, paint, 6);
            return Mathf.Clamp(paint + Mathf.Max(0, amp / 2), paint, 6);
        }

        public static float MainReferenceHalfWidthWorld(MapGenConfig config, float cellSizeWorld)
        {
            if (config == null)
                return 1f;
            float full = config.riverVisualRibbonFullWidthCellsMain > 0.01f
                ? config.riverVisualRibbonFullWidthCellsMain
                : 2.75f;
            float baseHalf = full * 0.5f * Mathf.Max(0.01f, cellSizeWorld);
            float normalMul = Mathf.Clamp(config.riverSurfaceVisualNormalWidthMul, 1.25f, 3f);
            return baseHalf * normalMul;
        }

        public static bool TryTrimTributaryAtConfluence(
            GridSystem grid,
            List<Vector2Int> path,
            List<Vector2> centerline,
            int minCellsAfterTrim,
            out int joinPathIndex,
            out Vector2Int joinCell)
        {
            joinPathIndex = -1;
            joinCell = default;
            if (path == null || path.Count < 4)
                return false;

            int firstRiver = -1;
            if (grid != null)
            {
                for (int i = 0; i < path.Count; i++)
                {
                    var c = path[i];
                    if (grid.InBoundsCell(c.x, c.y) && grid.GetCell(c.x, c.y).type == CellType.River)
                    {
                        firstRiver = i;
                        break;
                    }
                }
            }
            else
                firstRiver = path.Count - 1;

            if (firstRiver < 0)
                return false;

            if (grid != null)
            {
                for (int i = 0; i < firstRiver; i++)
                {
                    var c = path[i];
                    if (grid.InBoundsCell(c.x, c.y) && grid.GetCell(c.x, c.y).type == CellType.River)
                        return false;
                }
            }

            if (firstRiver + 1 < minCellsAfterTrim)
                return false;

            joinPathIndex = firstRiver;
            joinCell = path[firstRiver];
            int removeStart = firstRiver + 1;
            if (removeStart < path.Count)
                path.RemoveRange(removeStart, path.Count - removeStart);

            RebuildCenterlineFromPath(path, centerline);
            return path.Count >= minCellsAfterTrim;
        }

        public static bool TryTrimTributaryToConfluenceCell(
            GridSystem grid,
            List<Vector2Int> path,
            List<Vector2> centerline,
            Vector2Int confluenceCell,
            int minCellsAfterTrim,
            out int firstReceiverTouchIndex,
            out int removedPostJoinCells,
            out string rejectReason)
        {
            firstReceiverTouchIndex = -1;
            removedPostJoinCells = 0;
            rejectReason = null;

            if (path == null || path.Count < 4)
            {
                rejectReason = "path_short";
                return false;
            }

            int joinIdx = -1;
            for (int i = 0; i < path.Count; i++)
            {
                if (path[i].x == confluenceCell.x && path[i].y == confluenceCell.y)
                {
                    joinIdx = i;
                    break;
                }
            }

            if (joinIdx < 0)
            {
                rejectReason = "path_not_at_confluence";
                return false;
            }

            for (int i = 0; i < joinIdx; i++)
            {
                var c = path[i];
                if (grid != null && grid.InBoundsCell(c.x, c.y) && grid.GetCell(c.x, c.y).type == CellType.River)
                {
                    rejectReason = "crosses_receiver_before_end";
                    return false;
                }
            }

            removedPostJoinCells = path.Count - joinIdx - 1;
            if (removedPostJoinCells > 0)
                path.RemoveRange(joinIdx + 1, removedPostJoinCells);

            if (path.Count < minCellsAfterTrim)
            {
                rejectReason = "too_short_after_trim";
                return false;
            }

            firstReceiverTouchIndex = joinIdx;
            RebuildCenterlineFromPath(path, centerline);
            return true;
        }

        static void RebuildCenterlineFromPath(List<Vector2Int> path, List<Vector2> centerline)
        {
            if (centerline == null || path == null)
                return;
            centerline.Clear();
            for (int i = 0; i < path.Count; i++)
            {
                var c = path[i];
                centerline.Add(new Vector2(c.x + 0.5f, c.y + 0.5f));
            }
        }

        public static Vector2 TributaryIncomingAt(IReadOnlyList<Vector2> tribLine, int tribIdx)
        {
            if (tribLine == null || tribLine.Count < 2)
                return Vector2.right;
            int i0 = Mathf.Clamp(tribIdx - 1, 0, tribLine.Count - 1);
            int i1 = Mathf.Clamp(tribIdx, 0, tribLine.Count - 1);
            Vector2 d = tribLine[i1] - tribLine[i0];
            if (d.sqrMagnitude < 1e-6f && tribIdx > 0)
                d = tribLine[tribIdx] - tribLine[tribIdx - 1];
            return d.sqrMagnitude < 1e-6f ? Vector2.right : d.normalized;
        }

        public static float ComputeDirectedJoinAngleDeg(Vector2 receiverDownstream, Vector2 tributaryIncoming)
        {
            if (receiverDownstream.sqrMagnitude < 1e-6f || tributaryIncoming.sqrMagnitude < 1e-6f)
                return 45f;
            receiverDownstream.Normalize();
            tributaryIncoming.Normalize();
            return Vector2.Angle(receiverDownstream, tributaryIncoming);
        }

        public static bool IsJoinAngleAcceptable(
            MapGenConfig config,
            float joinAngleDeg,
            out bool isParallel,
            out bool isTJunction)
        {
            isParallel = joinAngleDeg < 20f || joinAngleDeg > 160f;
            isTJunction = joinAngleDeg >= 88f && joinAngleDeg <= 92f;
            if (config == null)
                return !isParallel && !isTJunction;
            float minAng = Mathf.Clamp(config.riverTributaryPreferredJoinAngleMinDeg, 20f, 80f);
            float maxAng = Mathf.Clamp(config.riverTributaryPreferredJoinAngleMaxDeg, minAng + 5f, 89f);
            return !isParallel && !isTJunction && joinAngleDeg >= minAng && joinAngleDeg <= maxAng;
        }

        public static bool IsJoinAngleLooseAcceptable(
            MapGenConfig config,
            float joinAngleDeg,
            bool isParallel,
            bool isTJunction)
        {
            if (isParallel)
                return false;
            if (isTJunction)
                return false;
            return joinAngleDeg >= 25f && joinAngleDeg <= 85f;
        }

        public static bool ValidateFinalConfluenceAngle(
            MapGenConfig config,
            IReadOnlyList<Vector2> tributaryCenterline,
            int joinIndex,
            Vector2 receiverDownstream,
            out float joinAngleDeg,
            out string rejectReason)
        {
            joinAngleDeg = 0f;
            rejectReason = null;

            if (tributaryCenterline == null || tributaryCenterline.Count < 2)
            {
                rejectReason = "path_length";
                return false;
            }

            if (receiverDownstream.sqrMagnitude < 1e-6f)
            {
                rejectReason = "receiver_dir_missing";
                return false;
            }

            int join = Mathf.Clamp(joinIndex, 1, tributaryCenterline.Count - 1);
            Vector2 tribIn = TributaryIncomingAt(tributaryCenterline, join);
            joinAngleDeg = ComputeDirectedJoinAngleDeg(receiverDownstream, tribIn);

            bool ok = IsJoinAngleAcceptable(config, joinAngleDeg, out bool isParallel, out bool isTJunction);
            if (ok)
                return true;

            if (config != null &&
                config.riverConfluenceAcceptLooseAngle &&
                IsJoinAngleLooseAcceptable(config, joinAngleDeg, isParallel, isTJunction))
            {
                return true;
            }

            rejectReason = isParallel ? "join_angle_parallel" : (isTJunction ? "join_angle_90" : "join_angle_out_of_range");
            return false;
        }

        public static void LogRiverConfluenceGeometryAudit(
            MapGenConfig config,
            int riverId,
            int receiverId,
            float joinAngleDeg,
            Vector2 receiverDownstreamDir,
            Vector2 tributaryIncomingDir,
            bool isParallel,
            bool isTJunction,
            bool accepted,
            string rejectReason)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork && !config.riverDendriticAuditLogs))
                return;
            Debug.Log(
                $"[RiverConfluenceGeometryAudit] riverId={riverId} receiverId={receiverId} joinAngleDeg={joinAngleDeg:F1} " +
                $"receiverDownstreamDir=({receiverDownstreamDir.x:F3},{receiverDownstreamDir.y:F3}) " +
                $"tributaryIncomingDir=({tributaryIncomingDir.x:F3},{tributaryIncomingDir.y:F3}) " +
                $"isParallel={(isParallel ? 1 : 0)} isTJunction={(isTJunction ? 1 : 0)} accepted={(accepted ? 1 : 0)} " +
                $"rejectReason={rejectReason ?? "none"}");
        }

        public static void LogRiverWidthRuntimeAudit(
            MapGenConfig config,
            string stage,
            int riverSlot,
            RiverDendriticRole role,
            float cellSizeWorld,
            bool legacyScaleApplied,
            bool runtimeOverrideApplied)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;

            int mainLogical = LogicalPaintRadiusCells(config, RiverDendriticRole.MainCollector);
            int tribLogical = LogicalPaintRadiusCells(config, role);
            float mainVisual = MainReferenceHalfWidthWorld(config, cellSizeWorld);
            float ratio = WidthRatioToMain(config, role);
            float tribVisual = mainVisual * ratio;
            float tribToMain = mainVisual > 0.01f ? tribVisual / mainVisual : 0f;
            bool ok = riverSlot <= 0 || (tribToMain >= 0.15f && tribToMain <= 0.85f);
            Debug.Log(
                $"[RiverWidthRuntimeAudit] stage={stage} mainLogicalRadius={mainLogical} tributaryLogicalRadius={tribLogical} " +
                $"mainVisualHalfWidth={mainVisual:F3} tributaryVisualHalfWidth={tribVisual:F3} tributaryToMainRatio={tribToMain:F3} " +
                $"legacyScaleApplied={(legacyScaleApplied ? 1 : 0)} dendriticScaleApplied={(config.riverDendriticNetworkEnabled ? 1 : 0)} " +
                $"runtimeOverrideApplied={(runtimeOverrideApplied ? 1 : 0)} ok={(ok ? 1 : 0)}");
        }

        public static bool ValidateTributaryPathGeometry(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2Int> path,
            List<Vector2> centerline,
            int receiverRiverIndex,
            HashSet<long> receiverOccupiedCells,
            out float joinAngleDeg,
            out bool crossesBeforeEnd,
            out bool runsParallel,
            out int parallelRunCells,
            out string rejectReason)
        {
            return ValidateTributaryPathGeometry(
                grid,
                config,
                path,
                centerline,
                receiverRiverIndex,
                receiverOccupiedCells,
                null,
                null,
                out joinAngleDeg,
                out crossesBeforeEnd,
                out runsParallel,
                out parallelRunCells,
                out rejectReason);
        }

        public static bool ValidateTributaryPathGeometry(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2Int> path,
            List<Vector2> centerline,
            int receiverRiverIndex,
            HashSet<long> receiverOccupiedCells,
            Vector2Int? requiredConfluenceCell,
            Vector2? receiverDownstream,
            out float joinAngleDeg,
            out bool crossesBeforeEnd,
            out bool runsParallel,
            out int parallelRunCells,
            out string rejectReason)
        {
            joinAngleDeg = 0f;
            crossesBeforeEnd = false;
            runsParallel = false;
            parallelRunCells = 0;
            rejectReason = null;

            if (grid == null || config == null || path == null || path.Count < 4)
            {
                rejectReason = "path_short";
                return false;
            }

            if (!config.riverDendriticNetworkEnabled)
                return true;

            int tailCells = Mathf.Clamp(config.riverTributaryJoinTailCells, 6, 20);
            int maxParallel = Mathf.Clamp(config.riverTributaryMaxParallelRunCells, 4, 12);

            int firstRiver = -1;
            for (int i = 0; i < path.Count; i++)
            {
                var c = path[i];
                if (grid.InBoundsCell(c.x, c.y) && grid.GetCell(c.x, c.y).type == CellType.River)
                {
                    firstRiver = i;
                    break;
                }
            }

            if (firstRiver < 0)
            {
                rejectReason = "no_river_join";
                return false;
            }

            for (int i = 0; i < firstRiver; i++)
            {
                var c = path[i];
                if (grid.InBoundsCell(c.x, c.y) && grid.GetCell(c.x, c.y).type == CellType.River)
                {
                    crossesBeforeEnd = true;
                    rejectReason = "crosses_receiver_before_end";
                    return false;
                }
            }

            if (requiredConfluenceCell.HasValue)
            {
                var end = path[path.Count - 1];
                if (end.x != requiredConfluenceCell.Value.x || end.y != requiredConfluenceCell.Value.y)
                {
                    rejectReason = "end_not_at_confluence";
                    return false;
                }

                if (firstRiver != path.Count - 1)
                {
                    crossesBeforeEnd = true;
                    rejectReason = "receiver_touch_before_final";
                    return false;
                }
            }
            else if (firstRiver < path.Count - tailCells)
            {
                crossesBeforeEnd = true;
                rejectReason = "join_not_in_final_segment";
                return false;
            }

            int joinIdx = requiredConfluenceCell.HasValue ? path.Count - 1 : firstRiver;
            int bodyEnd = Mathf.Max(0, joinIdx - tailCells);
            int approachExtra = config != null
                ? Mathf.Clamp(config.riverTributaryApproachParallelExtraCells, 0, 8)
                : 4;

            if (receiverOccupiedCells != null && receiverOccupiedCells.Count > 0)
            {
                int bodyParallelSegments = CountParallelSegmentsAlongReceiver(
                    grid, path, receiverOccupiedCells, 0, bodyEnd);
                if (bodyParallelSegments > 1)
                {
                    runsParallel = true;
                    parallelRunCells = bodyParallelSegments;
                    rejectReason = "multiple_receiver_proximity";
                    return false;
                }
            }

            int bodyMax = ComputeMaxParallelRunAlongReceiver(grid, path, receiverOccupiedCells, 0, bodyEnd);
            int tailMax = bodyEnd < joinIdx
                ? ComputeMaxParallelRunAlongReceiver(grid, path, receiverOccupiedCells, bodyEnd, joinIdx)
                : 0;

            parallelRunCells = Mathf.Max(bodyMax, tailMax);
            if (bodyMax > maxParallel)
            {
                runsParallel = true;
                rejectReason = "parallel_to_receiver";
                return false;
            }

            if (tailMax > maxParallel + approachExtra)
            {
                runsParallel = true;
                rejectReason = "parallel_to_receiver";
                return false;
            }

            if (bodyEnd < joinIdx &&
                receiverOccupiedCells != null &&
                receiverOccupiedCells.Count > 0)
            {
                int tailTotalParallel = ComputeTotalParallelCellsAlongReceiver(
                    grid, path, receiverOccupiedCells, bodyEnd, joinIdx);
                if (tailTotalParallel > maxParallel + approachExtra)
                {
                    runsParallel = true;
                    parallelRunCells = tailTotalParallel;
                    rejectReason = "parallel_to_receiver";
                    return false;
                }
            }

            if (centerline != null && centerline.Count >= 2)
            {
                int tribCl = Mathf.Clamp(firstRiver, 1, centerline.Count - 1);
                Vector2 recvDown = receiverDownstream ?? Vector2.right;
                if (!receiverDownstream.HasValue &&
                    receiverRiverIndex >= 0 &&
                    grid.RiverCenterlinesCellSpace != null &&
                    receiverRiverIndex < grid.RiverCenterlinesCellSpace.Count)
                {
                    var recv = grid.RiverCenterlinesCellSpace[receiverRiverIndex];
                    if (recv != null && recv.Count >= 2)
                    {
                        int mainCl = ClosestIndex(recv, centerline[tribCl]);
                        recvDown = RiverConfluenceUtility.ReceiverDownstreamAt(recv, mainCl).normalized;
                    }
                }

                Vector2 tribIn = TributaryIncomingAt(centerline, tribCl);
                joinAngleDeg = ComputeDirectedJoinAngleDeg(recvDown, tribIn);
                bool angleOk = IsJoinAngleAcceptable(config, joinAngleDeg, out bool isParallel, out bool isTJunction);
                if (!angleOk)
                {
                    if (config.riverConfluenceAcceptLooseAngle &&
                        IsJoinAngleLooseAcceptable(config, joinAngleDeg, isParallel, isTJunction))
                        return true;

                    rejectReason = isParallel ? "join_angle_parallel" : (isTJunction ? "join_angle_90" : "join_angle_out_of_range");
                    return false;
                }
            }

            return true;
        }

        /// <summary>True si el path intersecta otro tributario ya colocado (índices 1..riverSlot-1).</summary>
        public static bool CrossesOtherTributaryCenterline(
            GridSystem grid,
            int riverSlot,
            List<Vector2Int> path)
        {
            if (grid?.RiverCenterlinesCellSpace == null || path == null || path.Count < 2 || riverSlot <= 1)
                return false;

            int otherEnd = Mathf.Min(riverSlot, grid.RiverCenterlinesCellSpace.Count);
            for (int oi = 1; oi < otherEnd; oi++)
            {
                var other = grid.RiverCenterlinesCellSpace[oi];
                if (other == null || other.Count < 2)
                    continue;

                for (int i = 0; i < path.Count - 1; i++)
                {
                    Vector2 a = new Vector2(path[i].x + 0.5f, path[i].y + 0.5f);
                    Vector2 b = new Vector2(path[i + 1].x + 0.5f, path[i + 1].y + 0.5f);
                    for (int j = 0; j < other.Count - 1; j++)
                    {
                        if (SegmentsIntersectOpen2D(a, b, other[j], other[j + 1]))
                            return true;
                    }
                }
            }

            return false;
        }

        static bool SegmentsIntersectOpen2D(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            float d1 = (p3.x - p1.x) * (p2.y - p1.y) - (p3.y - p1.y) * (p2.x - p1.x);
            float d2 = (p4.x - p1.x) * (p2.y - p1.y) - (p4.y - p1.y) * (p2.x - p1.x);
            float d3 = (p1.x - p3.x) * (p4.y - p3.y) - (p1.y - p3.y) * (p4.x - p3.x);
            float d4 = (p2.x - p3.x) * (p4.y - p3.y) - (p2.y - p3.y) * (p4.x - p3.x);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
                   ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        public static int ComputeMaxParallelRunAlongReceiver(
            GridSystem grid,
            List<Vector2Int> path,
            HashSet<long> receiverOccupiedCells,
            int startIndex,
            int endIndexExclusive)
        {
            if (grid == null || path == null || receiverOccupiedCells == null || receiverOccupiedCells.Count == 0)
                return 0;

            int parallelRun = 0;
            int maxRun = 0;
            int i0 = Mathf.Clamp(startIndex, 0, path.Count);
            int i1 = Mathf.Clamp(endIndexExclusive, i0, path.Count);
            for (int i = i0; i < i1; i++)
            {
                var c = path[i];
                bool alongside = false;
                foreach (var n in grid.Neighbors4(c))
                {
                    if (receiverOccupiedCells.Contains(Pack(n.x, n.y)))
                    {
                        alongside = true;
                        break;
                    }
                }

                if (alongside)
                {
                    parallelRun++;
                    if (parallelRun > maxRun)
                        maxRun = parallelRun;
                }
                else
                    parallelRun = 0;
            }

            return maxRun;
        }

        public static int ComputeTotalParallelCellsAlongReceiver(
            GridSystem grid,
            List<Vector2Int> path,
            HashSet<long> receiverOccupiedCells,
            int startIndex,
            int endIndexExclusive)
        {
            if (grid == null || path == null || receiverOccupiedCells == null || receiverOccupiedCells.Count == 0)
                return 0;

            int total = 0;
            int i0 = Mathf.Clamp(startIndex, 0, path.Count);
            int i1 = Mathf.Clamp(endIndexExclusive, i0, path.Count);
            for (int i = i0; i < i1; i++)
            {
                var c = path[i];
                foreach (var n in grid.Neighbors4(c))
                {
                    if (receiverOccupiedCells.Contains(Pack(n.x, n.y)))
                    {
                        total++;
                        break;
                    }
                }
            }

            return total;
        }

        public static int CountParallelSegmentsAlongReceiver(
            GridSystem grid,
            List<Vector2Int> path,
            HashSet<long> receiverOccupiedCells,
            int startIndex,
            int endIndexExclusive)
        {
            if (grid == null || path == null || receiverOccupiedCells == null || receiverOccupiedCells.Count == 0)
                return 0;

            int segments = 0;
            bool inSegment = false;
            int i0 = Mathf.Clamp(startIndex, 0, path.Count);
            int i1 = Mathf.Clamp(endIndexExclusive, i0, path.Count);
            for (int i = i0; i < i1; i++)
            {
                var c = path[i];
                bool alongside = false;
                foreach (var n in grid.Neighbors4(c))
                {
                    if (receiverOccupiedCells.Contains(Pack(n.x, n.y)))
                    {
                        alongside = true;
                        break;
                    }
                }

                if (alongside && !inSegment)
                {
                    segments++;
                    inSegment = true;
                }
                else if (!alongside)
                    inSegment = false;
            }

            return segments;
        }

        public static void ApplyDownstreamApproachBlend(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> centerline,
            int receiverRiverIndex,
            int joinIndex)
        {
            if (grid == null || config == null || centerline == null || centerline.Count < 4)
                return;
            Vector2 downstream = Vector2.right;
            if (receiverRiverIndex >= 0 &&
                grid.RiverCenterlinesCellSpace != null &&
                receiverRiverIndex < grid.RiverCenterlinesCellSpace.Count)
            {
                var recv = grid.RiverCenterlinesCellSpace[receiverRiverIndex];
                if (recv != null && recv.Count >= 2)
                {
                    int join = Mathf.Clamp(joinIndex, 0, centerline.Count - 1);
                    downstream = RiverConfluenceUtility.ReceiverDownstreamAt(recv, ClosestIndex(recv, centerline[join]));
                }
            }

            ApplyDownstreamApproachBlend(config, centerline, joinIndex, downstream);
        }

        public static void ApplyDownstreamApproachBlend(
            MapGenConfig config,
            List<Vector2> centerline,
            int joinIndex,
            Vector2 receiverDownstream)
        {
            if (config == null || centerline == null || centerline.Count < 4)
                return;
            if (!config.riverDendriticNetworkEnabled)
                return;

            int blend = Mathf.Clamp(config.riverTributaryDownstreamBlendCells, 8, 20);
            int join = Mathf.Clamp(joinIndex, 1, centerline.Count - 1);
            int start = Mathf.Max(1, join - blend + 1);
            Vector2 fixedJoin = centerline[join];

            if (receiverDownstream.sqrMagnitude < 1e-6f)
                return;

            for (int i = start; i < join; i++)
            {
                float t = (join <= start) ? 1f : (i - start) / (float)(join - start);
                float fadeOutNearJoin = 1f - Mathf.SmoothStep(0f, 1f, t);
                float smooth = 0.16f * fadeOutNearJoin;
                if (smooth <= 1e-4f)
                    continue;

                Vector2 prev = centerline[Mathf.Max(0, i - 1)];
                Vector2 next = centerline[Mathf.Min(join, i + 1)];
                centerline[i] = Vector2.Lerp(centerline[i], (prev + next) * 0.5f, smooth);
            }

            centerline[join] = fixedJoin;
        }

        public static void LogRiverNetworkTopologyAudit(
            GridSystem grid,
            MapGenConfig config,
            int riverCountRequested,
            int waterChunksCreated,
            int msIncludesRiver,
            int pipelineGuardOk)
        {
            if (grid == null || config == null)
                return;
            if (!config.debugLogs && !config.debugHydrologyNetwork && !config.riverDendriticAuditLogs)
                return;

            int centerlineCount = grid.RiverCenterlinesCellSpace != null ? grid.RiverCenterlinesCellSpace.Count : 0;
            int confCount = grid.RiverConfluences != null ? grid.RiverConfluences.Count : 0;
            int mainCount = 0;
            int tribCount = 0;
            int borderToBorder = 0;
            int interiorToMain = 0;
            int interiorToTrib = 0;
            int crossing = 0;
            int parallel = 0;

            int w = grid.Width;
            int h = grid.Height;
            if (grid.RiverCenterlinesCellSpace != null)
            {
                for (int ri = 0; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
                {
                    var line = grid.RiverCenterlinesCellSpace[ri];
                    if (line == null || line.Count < 2)
                        continue;
                    if (ri == 0)
                        mainCount++;
                    else
                        tribCount++;

                    bool startBorder = IsMapEdgeCell(line[0], w, h);
                    bool endBorder = IsMapEdgeCell(line[line.Count - 1], w, h);
                    if (ri == 0 && startBorder && endBorder)
                        borderToBorder++;

                    int recv = grid.RiverReceiverIds != null && ri < grid.RiverReceiverIds.Count
                        ? grid.RiverReceiverIds[ri]
                        : (ri > 0 ? 0 : -1);
                    if (ri > 0)
                    {
                        if (recv <= 0)
                            interiorToMain++;
                        else
                            interiorToTrib++;
                    }
                }
            }

            Debug.Log(
                $"[RiverNetworkTopologyAudit] riverCountRequested={riverCountRequested} riverCenterlineCount={centerlineCount} " +
                $"mainRiverCount={mainCount} tributaryCount={tribCount} confluenceCount={confCount} " +
                $"tributariesCrossingReceiver={crossing} tributariesRunningParallelToReceiver={parallel} " +
                $"borderToBorderCount={borderToBorder} interiorToMainCount={interiorToMain} interiorToTributaryCount={interiorToTrib} " +
                $"waterChunksCreated={waterChunksCreated} msIncludesRiver={msIncludesRiver} pipelineGuardOk={pipelineGuardOk}");
        }

        public static void LogRiverConfluenceGeometryAudits(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null)
                return;
            if (!config.debugLogs && !config.debugHydrologyNetwork && !config.riverDendriticAuditLogs)
                return;
            if (grid.RiverConfluences == null)
                return;

            var mainOcc = BuildOccupiedFromRiverIndex(grid, 0, config);
            for (int i = 0; i < grid.RiverConfluences.Count; i++)
            {
                var n = grid.RiverConfluences[i];
                int tribId = n.TributaryRiverIndex;
                if (tribId <= 0 ||
                    grid.RiverCenterlinesCellSpace == null ||
                    tribId >= grid.RiverCenterlinesCellSpace.Count)
                    continue;

                var line = grid.RiverCenterlinesCellSpace[tribId];
                var path = CenterlineToPath(line);
                if (path == null)
                    continue;

                var recvLine = grid.RiverCenterlinesCellSpace[n.MainRiverIndex];
                Vector2 recvDown = recvLine != null
                    ? RiverConfluenceUtility.ReceiverDownstreamAt(recvLine, n.MainCenterlineIndex).normalized
                    : Vector2.right;
                int joinIdx = path.Count - 1;
                for (int pi = 0; pi < path.Count; pi++)
                {
                    if (path[pi].x == n.Cell.x && path[pi].y == n.Cell.y)
                    {
                        joinIdx = pi;
                        break;
                    }
                }

                Vector2 tribIn = TributaryIncomingAt(line, Mathf.Clamp(joinIdx, 1, line.Count - 1));
                bool ok = ValidateTributaryPathGeometry(
                    grid,
                    config,
                    path,
                    line,
                    n.MainRiverIndex,
                    mainOcc,
                    n.Cell,
                    recvDown,
                    out float ang,
                    out bool cross,
                    out bool par,
                    out int parCells,
                    out string reason);

                LogRiverConfluenceGeometryAudit(
                    config,
                    tribId,
                    n.MainRiverIndex,
                    ang,
                    recvDown,
                    tribIn,
                    par || ang < 20f || ang > 160f,
                    ang >= 88f && ang <= 92f,
                    ok && n.Valid,
                    string.IsNullOrEmpty(reason) ? "none" : reason);
            }
        }

        public static void LogMcpTributaryTopologyAudit(
            GridSystem grid,
            MapGenConfig config,
            int tributaryMeshCount,
            int hasChunks,
            int hasMs,
            int pipelineGuardOk)
        {
            if (config == null || grid == null)
                return;
            if (!config.debugLogs && !config.debugHydrologyNetwork && !config.riverDendriticAuditLogs)
                return;

            int mainCount = 0;
            int tribCount = 0;
            int parallelCount = 0;
            int crossCount = 0;
            int endTouch = 0;
            int overshoot = 0;
            float ratioMin = float.MaxValue;
            float ratioMax = 0f;
            float ratioSum = 0f;
            int ratioN = 0;
            float angMin = float.MaxValue;
            float angMax = 0f;
            float angSum = 0f;
            int angN = 0;

            var mainOcc = BuildOccupiedFromRiverIndex(grid, 0, config);
            if (grid.RiverCenterlinesCellSpace != null)
            {
                for (int ri = 0; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
                {
                    if (ri == 0)
                        mainCount++;
                    else
                        tribCount++;

                    if (ri <= 0)
                        continue;

                    var line = grid.RiverCenterlinesCellSpace[ri];
                    var path = CenterlineToPath(line);
                    if (path == null)
                        continue;

                    bool touches = path.Count > 0 &&
                                     grid.InBoundsCell(path[path.Count - 1].x, path[path.Count - 1].y) &&
                                     grid.GetCell(path[path.Count - 1].x, path[path.Count - 1].y).type == CellType.River;
                    if (touches)
                        endTouch++;

                    int firstRv = -1;
                    for (int pi = 0; pi < path.Count; pi++)
                    {
                        if (grid.GetCell(path[pi].x, path[pi].y).type == CellType.River)
                        {
                            firstRv = pi;
                            break;
                        }
                    }

                    if (firstRv >= 0 && firstRv < path.Count - 1)
                        overshoot++;
                    if (firstRv > 0)
                        crossCount++;

                    if (!ValidateTributaryPathGeometry(
                            grid,
                            config,
                            path,
                            line,
                            0,
                            mainOcc,
                            null,
                            null,
                            out _,
                            out _,
                            out bool par,
                            out _,
                            out _))
                        parallelCount++;
                    else if (par)
                        parallelCount++;

                    float ratio = grid.RiverWidthRatioToMain != null && ri < grid.RiverWidthRatioToMain.Count
                        ? grid.RiverWidthRatioToMain[ri]
                        : WidthRatioToMain(config, RiverDendriticRole.SecondaryTributary);
                    ratioMin = Mathf.Min(ratioMin, ratio);
                    ratioMax = Mathf.Max(ratioMax, ratio);
                    ratioSum += ratio;
                    ratioN++;

                    if (grid.RiverConfluences != null)
                    {
                        for (int ci = 0; ci < grid.RiverConfluences.Count; ci++)
                        {
                            var cn = grid.RiverConfluences[ci];
                            if (cn.TributaryRiverIndex != ri)
                                continue;
                            angMin = Mathf.Min(angMin, cn.AngleDeg);
                            angMax = Mathf.Max(angMax, cn.AngleDeg);
                            angSum += cn.AngleDeg;
                            angN++;
                        }
                    }
                }
            }

            int confCount = grid.RiverConfluences != null ? grid.RiverConfluences.Count : 0;
            float ratioAvg = ratioN > 0 ? ratioSum / ratioN : 0f;
            float angAvg = angN > 0 ? angSum / angN : 0f;
            if (ratioMin == float.MaxValue)
                ratioMin = 0f;
            if (angMin == float.MaxValue)
                angMin = 0f;

            bool ok = mainCount == 1 && tribCount >= Mathf.Max(0, config.riverCount - 1) &&
                      hasChunks == 0 && pipelineGuardOk == 1;
            Debug.Log(
                $"[MCPTributaryTopologyAudit] mainCount={mainCount} tributaryCount={tribCount} confluenceCount={confCount} " +
                $"hasChunks={hasChunks} hasMS={hasMs} pipelineGuardOk={pipelineGuardOk} tributaryMeshes={tributaryMeshCount} " +
                $"tributaryParallelToMainCount={parallelCount} tributaryCrossesMainBeforeEndCount={crossCount} " +
                $"tributaryEndTouchesMain={endTouch} tributaryEndOvershootsMain={overshoot} " +
                $"tributaryWidthRatioMinAvgMax={ratioMin:F3}/{ratioAvg:F3}/{ratioMax:F3} " +
                $"confluenceAngleMinAvgMax={angMin:F1}/{angAvg:F1}/{angMax:F1} ok={(ok ? 1 : 0)}");
        }

        public static void LogRiverOrderWidthAudits(GridSystem grid, MapGenConfig config, float cellSize)
        {
            if (grid == null || config == null)
                return;
            if (!config.debugLogs && !config.debugHydrologyNetwork && !config.riverDendriticAuditLogs)
                return;
            if (grid.RiverCenterlinesCellSpace == null)
                return;

            float mainHalf = MainReferenceHalfWidthWorld(config, cellSize);
            for (int ri = 0; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
            {
                RiverDendriticRole role = grid.RiverDendriticRoles != null && ri < grid.RiverDendriticRoles.Count
                    ? grid.RiverDendriticRoles[ri]
                    : (ri == 0 ? RiverDendriticRole.MainCollector : RiverDendriticRole.SecondaryTributary);
                float ratio = grid.RiverWidthRatioToMain != null && ri < grid.RiverWidthRatioToMain.Count
                    ? grid.RiverWidthRatioToMain[ri]
                    : WidthRatioToMain(config, role);
                int paintR = LogicalPaintRadiusCells(config, role);
                int carveR = paintR;
                float meshHalf = ri == 0
                    ? RiverSurfaceMeshBuilder.LastMainRiverAvgHalfWidthWorld
                    : RiverSurfaceMeshBuilder.GetTributaryAvgHalfWidthWorld(ri);
                float logicalHalf = paintR * cellSize;
                float visToLog = logicalHalf > 0.01f ? meshHalf / logicalHalf : 0f;
                string roleStr = role switch
                {
                    RiverDendriticRole.MainCollector => "main",
                    RiverDendriticRole.SecondaryTributary => "secondary",
                    RiverDendriticRole.MediumStream => "medium",
                    _ => "headwater"
                };
                Debug.Log(
                    $"[RiverOrderWidthAudit] riverId={ri} role={roleStr} order={(int)role} targetWidthRatioToMain={ratio:F3} " +
                    $"logicalPaintRadius={paintR} terrainCarveRadius={carveR} meshAvgHalfWidth={meshHalf:F3} " +
                    $"visualToLogicalRatio={visToLog:F3} fordWidthSource=logical_paint_radius");
            }
        }

        public static void LogRiverOrphanWaterAudit(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null || !config.riverOrphanWaterAuditEnabled)
                return;
            if (!config.debugLogs && !config.debugHydrologyNetwork)
                return;

            int w = grid.Width;
            int h = grid.Height;
            var visited = new bool[w, h];
            int totalComponents = 0;
            int lakeComponents = 0;
            int riverConnected = 0;
            int orphanComponents = 0;
            int orphanCells = 0;
            int largestOrphan = 0;
            int wouldRemove = 0;
            int wouldConnect = 0;

            var centerlinePacked = new HashSet<long>();
            if (grid.RiverCenterlinesCellSpace != null)
            {
                for (int ri = 0; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
                {
                    var line = grid.RiverCenterlinesCellSpace[ri];
                    if (line == null)
                        continue;
                    for (int pi = 0; pi < line.Count; pi++)
                    {
                        int cx = Mathf.FloorToInt(line[pi].x);
                        int cz = Mathf.FloorToInt(line[pi].y);
                        centerlinePacked.Add(Pack(cx, cz));
                    }
                }
            }

            bool[,] mask = grid.RiverVisualSurfaceMask;

            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (visited[x, z])
                        continue;
                    ref var c0 = ref grid.GetCell(x, z);
                    if (c0.type != CellType.Water && c0.type != CellType.River)
                        continue;

                    totalComponents++;
                    int size = 0;
                    bool touchesLake = false;
                    bool touchesRiverNet = false;
                    bool touchesFord = false;
                    bool touchesMask = false;
                    var qx = new Queue<int>();
                    var qz = new Queue<int>();
                    qx.Enqueue(x);
                    qz.Enqueue(z);
                    visited[x, z] = true;

                    while (qx.Count > 0)
                    {
                        int cx = qx.Dequeue();
                        int cz = qz.Dequeue();
                        size++;
                        ref var c = ref grid.GetCell(cx, cz);
                        if (c.type == CellType.River)
                            touchesRiverNet = true;
                        if (grid.LakeBodyCellsPacked != null && grid.LakeBodyCellsPacked.Contains(Pack(cx, cz)))
                            touchesLake = true;
                        if (c.riverFord)
                            touchesFord = true;
                        if (mask != null && mask[cx, cz])
                            touchesMask = true;
                        if (centerlinePacked.Contains(Pack(cx, cz)))
                            touchesRiverNet = true;

                        foreach (var n in grid.Neighbors4(new Vector2Int(cx, cz)))
                        {
                            int nx = n.x;
                            int nz = n.y;
                            if (visited[nx, nz])
                                continue;
                            ref var nc = ref grid.GetCell(nx, nz);
                            if (nc.type != CellType.Water && nc.type != CellType.River)
                                continue;
                            visited[nx, nz] = true;
                            qx.Enqueue(nx);
                            qz.Enqueue(nz);
                        }
                    }

                    if (touchesLake && size >= Mathf.Max(8, config.lakeVisualRealLakeMinCells))
                        lakeComponents++;
                    else if (touchesRiverNet || touchesMask)
                        riverConnected++;
                    else if (!touchesFord)
                    {
                        orphanComponents++;
                        orphanCells += size;
                        if (size > largestOrphan)
                            largestOrphan = size;
                        if (size <= config.riverVisualStrayPoolMaxCells)
                        {
                            wouldRemove += size;
                            if (config.riverOrphanWaterCleanupEnabled)
                                wouldConnect += size;
                        }
                    }
                }
            }

            Debug.Log(
                $"[RiverOrphanWaterAudit] componentsFound={totalComponents} lakeComponents={lakeComponents} " +
                $"riverConnectedComponents={riverConnected} orphanComponents={orphanComponents} orphanCells={orphanCells} " +
                $"largestOrphanCells={largestOrphan} wouldRemoveCells={wouldRemove} wouldConnectCells={wouldConnect}");
        }

        public static HashSet<long> BuildOccupiedFromRiverIndex(GridSystem grid, int riverIndex, MapGenConfig config)
        {
            var occ = new HashSet<long>();
            if (grid == null || grid.RiverCenterlinesCellSpace == null || riverIndex >= grid.RiverCenterlinesCellSpace.Count)
                return occ;
            var path = CenterlineToPath(grid.RiverCenterlinesCellSpace[riverIndex]);
            if (path == null)
                return occ;
            RiverDendriticRole role = grid.RiverDendriticRoles != null && riverIndex < grid.RiverDendriticRoles.Count
                ? grid.RiverDendriticRoles[riverIndex]
                : (riverIndex == 0 ? RiverDendriticRole.MainCollector : RiverDendriticRole.SecondaryTributary);
            int r = CorridorMaxRadiusCells(config, role);
            for (int i = 0; i < path.Count; i++)
            {
                var c = path[i];
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = c.x + dx;
                        int nz = c.y + dz;
                        if ((uint)nx < (uint)grid.Width && (uint)nz < (uint)grid.Height)
                            occ.Add(Pack(nx, nz));
                    }
                }
            }

            return occ;
        }

        static List<Vector2Int> CenterlineToPath(List<Vector2> line)
        {
            if (line == null || line.Count < 2)
                return null;
            var path = new List<Vector2Int>(line.Count);
            for (int i = 0; i < line.Count; i++)
                path.Add(new Vector2Int(Mathf.RoundToInt(line[i].x), Mathf.RoundToInt(line[i].y)));
            return path;
        }

        static bool IsMapEdgeCell(Vector2 p, int w, int h)
        {
            int x = Mathf.RoundToInt(p.x);
            int z = Mathf.RoundToInt(p.y);
            return x <= 1 || z <= 1 || x >= w - 2 || z >= h - 2;
        }

        static int ClosestIndex(IReadOnlyList<Vector2> line, Vector2 p)
        {
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
