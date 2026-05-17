using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>MVP: río como ruta planificada (A* tipo carretera), sin descenso greedy ni Catmull/Laplacian.</summary>
    public static partial class RiverRouteGenerator
    {
        const int TributaryPathMinCellsDefault = 16;
        const int MaxAStarExpansionsGlobal = 260000;
        const int MainAStarExpansionCap = 30000;
        const int MainPairBudgetMs = 120;
        const int TributaryAStarExpansionCap = 25000;
        const int TributaryTotalBudgetMs = 50;
        const int TributaryMaxAttempts = 2;
        const int RandomAnchorPairsMax = 8;
        const int DeterministicModes = 4;

        /// <summary>Tope de puntos en centerline visual respecto a pathCells (no afecta grid).</summary>
        const float MainVisualCenterlineMaxPointsFactor = 1.2f;

        const float MainVisualRdpEpsilonCells = 0.34f;

        static long Pack(int x, int y) => ((long)x << 32) | (uint)y;

        /// <summary>Milisegundos monótonos (sin Environment.TickCount64 para API antiguas).</summary>
        static long MonotonicMs() =>
            (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);

        static void Unpack(long k, out int x, out int y)
        {
            x = (int)(k >> 32);
            y = (int)(k & 0xFFFFFFFF);
        }

        static int MainPathMinCells(int w, int h, MapGenConfig config = null) =>
            ResolveMainMinPathCells(config, w, h);

        static int MainPathMaxCells(int w, int h, MapGenConfig config = null) =>
            ResolveMainMaxPathCells(config, w, h);

        static int TributaryPathMinCells(int w, int h) =>
            Mathf.Clamp(TributaryPathMinCellsDefault, 12, 20);

        static int TributaryPathMaxCells(int w, int h) =>
            Mathf.Max(TributaryPathMinCells(w, h) + 1, Mathf.RoundToInt(Mathf.Min(w, h) * 0.75f));

        /// <summary>Longitud mínima aceptable del tributario en celdas (mapas ~256 → ~35–45).</summary>
        static int TributaryVisualMinCells(int w, int h)
        {
            int m = Mathf.Min(w, h);
            if (m >= 200)
                return Mathf.Clamp(Mathf.RoundToInt(m * 0.14f), 35, 45);
            return Mathf.Clamp(Mathf.RoundToInt(m * 0.18f), 22, 34);
        }

        static int BorderMargin(int w, int h) =>
            Mathf.Max(8, Mathf.RoundToInt(Mathf.Min(w, h) * 0.08f));

        static RiverAnchorKind? s_lastMainRouteStartKind;
        static RiverAnchorKind? s_lastMainRouteGoalKind;
        static RiverMainPattern? s_lastMainRoutePattern;

        static void ClearLastMainRouteAnchorMeta()
        {
            s_lastMainRouteStartKind = null;
            s_lastMainRouteGoalKind = null;
            s_lastMainRoutePattern = null;
        }

        static void NoteLastMainRouteAnchorsForBorderStretch(RiverMainPattern pat, RiverAnchorKind sk, RiverAnchorKind gk)
        {
            s_lastMainRoutePattern = pat;
            s_lastMainRouteStartKind = sk;
            s_lastMainRouteGoalKind = gk;
        }

        static bool IsTrueMapEdgeCell(Vector2Int c, int w, int h) =>
            c.x == 0 || c.x == w - 1 || c.y == 0 || c.y == h - 1;

        static bool CellTypeAllowsRiverExtensionLandOnly(CellType t) =>
            t == CellType.Land;

        /// <summary>
        /// Extiende el path por celdas Land hasta un borde real del mapa (solo meta BorderExit / inicio BorderToBorder).
        /// </summary>
        static bool TryExtendPathTowardTrueMapEdge(
            GridSystem grid,
            List<Vector2Int> path,
            int w,
            int h,
            bool extendFromStart,
            out int addedCells,
            out string reason)
        {
            addedCells = 0;
            reason = "na";
            if (path == null || path.Count < 2)
            {
                reason = "path_short";
                return false;
            }

            Vector2Int anchor = extendFromStart ? path[0] : path[path.Count - 1];
            if (IsTrueMapEdgeCell(anchor, w, h))
            {
                reason = "already_on_true_edge";
                return false;
            }

            var forbidden = new HashSet<long>();
            if (extendFromStart)
            {
                for (int i = 1; i < path.Count; i++)
                    forbidden.Add(Pack(path[i].x, path[i].y));
            }
            else
            {
                for (int i = 0; i < path.Count - 1; i++)
                    forbidden.Add(Pack(path[i].x, path[i].y));
            }

            var q = new Queue<Vector2Int>();
            var visited = new HashSet<long>();
            var parent = new Dictionary<long, long>();
            long startK = Pack(anchor.x, anchor.y);
            q.Enqueue(anchor);
            visited.Add(startK);
            parent[startK] = startK;

            Vector2Int foundEdge = default;
            bool got = false;
            int guard = 0;
            while (q.Count > 0 && guard++ < 5000)
            {
                var c = q.Dequeue();
                if (IsTrueMapEdgeCell(c, w, h) && grid.GetCell(c.x, c.y).type == CellType.Land)
                {
                    foundEdge = c;
                    got = true;
                    break;
                }

                foreach (var nb in grid.Neighbors4(c))
                {
                    if (!grid.InBoundsCell(nb.x, nb.y))
                        continue;
                    long nk = Pack(nb.x, nb.y);
                    if (visited.Contains(nk))
                        continue;
                    ref var nc = ref grid.GetCell(nb.x, nb.y);
                    if (!CellTypeAllowsRiverExtensionLandOnly(nc.type))
                        continue;
                    if (forbidden.Contains(nk))
                        continue;

                    visited.Add(nk);
                    parent[nk] = Pack(c.x, c.y);
                    q.Enqueue(nb);
                }
            }

            if (!got)
            {
                reason = "bfs_no_land_edge";
                return false;
            }

            var chain = new List<Vector2Int>();
            long ck = Pack(foundEdge.x, foundEdge.y);
            while (ck != startK)
            {
                Unpack(ck, out int ux, out int uy);
                chain.Add(new Vector2Int(ux, uy));
                if (!parent.TryGetValue(ck, out long pk) || pk == ck)
                {
                    reason = "parent_chain_broken";
                    return false;
                }

                ck = pk;
            }

            if (extendFromStart)
            {
                for (int i = chain.Count - 1; i >= 0; i--)
                    path.Insert(0, chain[i]);
                addedCells = chain.Count;
            }
            else
            {
                for (int i = chain.Count - 1; i >= 0; i--)
                    path.Add(chain[i]);
                addedCells = chain.Count;
            }

            reason = "extended_to_true_edge";
            return true;
        }

        static int MinChebyshevToMapEdge(Vector2Int c, int w, int h) =>
            Mathf.Min(Mathf.Min(c.x, w - 1 - c.x), Mathf.Min(c.y, h - 1 - c.y));

        static void TryExtendMainPathForBorderExit(
            GridSystem grid,
            List<Vector2Int> path,
            int w,
            int h,
            int riverSlot,
            MapGenConfig config,
            bool logDetail)
        {
            if (path == null || path.Count < 2)
                return;
            if (!s_lastMainRouteGoalKind.HasValue || s_lastMainRouteGoalKind.Value != RiverAnchorKind.BorderExit)
                return;

            var pat = s_lastMainRoutePattern ?? RiverMainPattern.BorderToBorder;
            var sk = s_lastMainRouteStartKind ?? RiverAnchorKind.BorderExit;
            Vector2Int startBefore = path[0];
            Vector2Int goalBefore = path[path.Count - 1];
            int maxExtend = config != null ? Mathf.Max(0, config.riverMainMaxBorderPathExtensionCells) : 0;
            int borderInset = config != null ? Mathf.Clamp(config.riverMainBorderExitInsetCells, 0, 48) : 0;
            int added = 0;
            string rEnd = "skip";
            string rStart = "skip";

            if (maxExtend > 0)
            {
                if (MinChebyshevToMapEdge(goalBefore, w, h) > 0)
                {
                    if (TryExtendPathTowardTrueMapEdge(grid, path, w, h, extendFromStart: false, out int addEnd, out rEnd))
                        added += addEnd;
                }
                else
                    rEnd = "goal_already_on_edge";

                int addSt = 0;
                if (pat == RiverMainPattern.BorderToBorder && sk == RiverAnchorKind.BorderExit &&
                    MinChebyshevToMapEdge(startBefore, w, h) > 0)
                {
                    if (TryExtendPathTowardTrueMapEdge(grid, path, w, h, extendFromStart: true, out addSt, out rStart))
                        added += addSt;
                }
                else if (pat == RiverMainPattern.BorderToBorder)
                    rStart = "start_already_on_edge";
            }
            else
            {
                rEnd = "extension_disabled";
                rStart = "extension_disabled";
            }

            Vector2Int goalAfter = path[path.Count - 1];
            Vector2Int startAfter = path[0];
            bool extended = added > 0;
            if (logDetail)
            {
                string reason = extended ? "extended" : $"no_change_end={rEnd}_start={rStart}";
                UnityEngine.Debug.Log(
                    $"[RiverBorderExit] slot={riverSlot} pattern={pat} goalBefore={goalBefore} goalAfter={goalAfter} " +
                    $"extended={(extended ? 1 : 0)} addedCells={added} reason={reason}");
            }

            if (config != null && (logDetail || config.debugLogs || config.debugHydrologyNetwork))
            {
                LogRiverBorderPolicy(
                    config,
                    sk,
                    s_lastMainRouteGoalKind.Value,
                    startAfter,
                    goalAfter,
                    w,
                    h,
                    borderInset,
                    maxExtend,
                    removedArtificialMargin: borderInset == 0 && maxExtend == 0 ? 1 : 0);
            }
        }

        internal static void LogRiverBorderPolicy(
            MapGenConfig config,
            RiverAnchorKind startKind,
            RiverAnchorKind goalKind,
            Vector2Int source,
            Vector2Int goal,
            int w,
            int h,
            int borderInsetCells,
            int maxBorderExtensionCells,
            int removedArtificialMargin,
            int meshReachesBorder = -1,
            int terrainCarveReachesBorder = -1)
        {
            if (config == null)
                return;
            int dsb = MinChebyshevToMapEdge(source, w, h);
            int dgb = MinChebyshevToMapEdge(goal, w, h);
            bool allowBorderStart = config.riverMainAllowBorderStart;
            bool allowBorderEnd = goalKind == RiverAnchorKind.BorderExit || goalKind == RiverAnchorKind.LakeSink;
            UnityEngine.Debug.Log(
                $"[RiverBorderPolicy] allowBorderStart={(allowBorderStart ? 1 : 0)} allowBorderEnd={(allowBorderEnd ? 1 : 0)} " +
                $"sourceAtBorder={(dsb == 0 ? 1 : 0)} goalAtBorder={(dgb == 0 ? 1 : 0)} sourceDistBorder={dsb} goalDistBorder={dgb} " +
                $"removedArtificialMargin={removedArtificialMargin} borderInsetCells={borderInsetCells} maxBorderExtensionCells={maxBorderExtensionCells} " +
                $"meshReachesBorder={meshReachesBorder} terrainCarveReachesBorder={terrainCarveReachesBorder}");
        }

        internal static bool TryValidateMainRouteForPolicy(
            MapGenConfig config,
            int w,
            int h,
            Vector2Int start,
            Vector2Int goal,
            List<Vector2Int> path,
            out string rejectReason)
        {
            rejectReason = null;
            if (config == null || path == null || path.Count < 2)
                return true;

            float mapDiag = Mathf.Sqrt(w * (float)w + h * (float)h);
            float pathDiagRatio = path.Count / Mathf.Max(1f, mapDiag);
            if (pathDiagRatio < Mathf.Clamp(config.riverMainMinPathToMapDiagRatio, 0.25f, 0.75f))
            {
                rejectReason = "path_too_short_vs_diag";
                return false;
            }

            if (config.lakeCount <= 0 && config.riverMainRetryIfSourceTooFarFromBorder)
            {
                int dsb = MinChebyshevToMapEdge(start, w, h);
                int maxSrc = Mathf.Clamp(config.riverMainMaxSourceDistanceFromBorderWhenNoLakeCells, 0, 48);
                if (dsb > maxSrc)
                {
                    rejectReason = "source_too_far_from_border_no_lake";
                    return false;
                }
            }

            return true;
        }

        internal static void LogRiverRouteLengthAudit(
            MapGenConfig config,
            int riverId,
            RiverMainPattern pattern,
            List<Vector2Int> path,
            Vector2Int start,
            Vector2Int goal,
            int w,
            int h,
            int retryCount,
            bool accepted,
            string rejectedReason)
        {
            if (config == null || !config.riverMainEndpointAuditEnabled)
                return;
            if (!config.debugLogs && !config.debugHydrologyNetwork && !accepted)
                return;

            int pathCells = path != null ? path.Count : 0;
            float mapDiag = Mathf.Sqrt(w * (float)w + h * (float)h);
            float ratio = pathCells / Mathf.Max(1f, mapDiag);
            int dsb = MinChebyshevToMapEdge(start, w, h);
            int dgb = MinChebyshevToMapEdge(goal, w, h);
            bool shortWarn = ratio < Mathf.Clamp(config.riverMainMinPathToMapDiagRatio, 0.25f, 0.75f);
            UnityEngine.Debug.Log(
                $"[RiverRouteLengthAudit] riverId={riverId} lakeCount={config.lakeCount} pattern={pattern} pathCells={pathCells} " +
                $"mapDiagCells={mapDiag:F1} pathDiagRatio={ratio:F3} sourceAtBorder={(dsb == 0 ? 1 : 0)} goalAtBorder={(dgb == 0 ? 1 : 0)} " +
                $"sourceDistBorder={dsb} goalDistBorder={dgb} rejectedReason={(string.IsNullOrEmpty(rejectedReason) ? "none" : rejectedReason)} " +
                $"retryCount={retryCount} accepted={(accepted ? 1 : 0)} shortRouteWarning={(shortWarn ? 1 : 0)}");
        }

        internal static void LogRiverEndpointPolicyRoute(
            MapGenConfig config,
            int riverId,
            RiverAnchorKind sourceKind,
            RiverAnchorKind goalKind,
            Vector2Int start,
            Vector2Int goal,
            int w,
            int h,
            string endpointPolicyResult)
        {
            if (config == null || !config.riverMainEndpointAuditEnabled)
                return;
            if (!config.debugLogs && !config.debugHydrologyNetwork)
                return;

            int dsb = MinChebyshevToMapEdge(start, w, h);
            int dgb = MinChebyshevToMapEdge(goal, w, h);
            UnityEngine.Debug.Log(
                $"[RiverEndpointPolicy] riverId={riverId} sourceKind={sourceKind} goalKind={goalKind} " +
                $"sourceAtBorder={(dsb == 0 ? 1 : 0)} goalAtBorder={(dgb == 0 ? 1 : 0)} sourceDistBorder={dsb} goalDistBorder={dgb} " +
                $"preferBorderToBorderWhenNoLake={(config.riverMainPreferBorderToBorderWhenNoLake ? 1 : 0)} " +
                $"maxSourceDistanceFromBorderWhenNoLakeCells={config.riverMainMaxSourceDistanceFromBorderWhenNoLakeCells} " +
                $"endpointPolicyResult={endpointPolicyResult}");
        }

        /// <summary>Mismo contrato de alto nivel que el flujo Fase 4 espera tras el generador simple.</summary>
        public static bool TryGenerateRouteRiver(
            GridSystem grid,
            MapGenConfig config,
            IRng rng,
            int riverSlot,
            int riverAttempt,
            bool mergeToExistingRiver,
            bool avoidCrossingCorridor,
            HashSet<long> occupiedRiverCells,
            out List<Vector2Int> pathCells,
            out List<Vector2> centerlineCellSpace,
            out List<Vector2Int> fordCells,
            out List<Vector2> debugMacro,
            out List<Vector2> debugSmoothed,
            out string rejectReason)
        {
            pathCells = null;
            centerlineCellSpace = null;
            fordCells = null;
            debugMacro = null;
            debugSmoothed = null;
            rejectReason = null;

            var sw = Stopwatch.StartNew();
            bool logRoute = config != null &&
                (config.debugHydrologyNetwork || config.debugLogs || config.debugRiverHydrologyPerf);
            bool dbgCost = config != null && config.debugHydrologyNetwork;

            if (grid == null || config == null || rng == null)
            {
                rejectReason = "null_args";
                if (logRoute)
                    LogRiverRoute(riverSlot, riverAttempt, mergeToExistingRiver, false, 0, 0, default, default, false, rejectReason, sw.Elapsed.TotalMilliseconds);
                return false;
            }

            int w = grid.Width;
            int h = grid.Height;
            int expandedNodes = 0;
            float finalCost = 0f;
            float sumNearRiverPen = 0f;
            float sumHeightBias = 0f;
            int pathLenForLog = 0;
            bool touchesRiver = false;
            Vector2Int logStart = default;
            Vector2Int logGoal = default;

            ClearLastMainRouteAnchorMeta();

            bool ok;
            if (mergeToExistingRiver)
            {
                ok = TryBuildTributaryRoute(
                    grid,
                    w,
                    h,
                    rng,
                    riverSlot,
                    riverAttempt,
                    avoidCrossingCorridor,
                    occupiedRiverCells,
                    logRoute,
                    out expandedNodes,
                    out finalCost,
                    out sumNearRiverPen,
                    out sumHeightBias,
                    out pathCells,
                    out logStart,
                    out touchesRiver,
                    out rejectReason);
                logGoal = pathCells != null && pathCells.Count > 0 ? pathCells[pathCells.Count - 1] : default;
            }
            else
            {
                bool isMainRiver = !mergeToExistingRiver;
                ok = TryBuildMainBorderRoute(
                    grid,
                    w,
                    h,
                    config,
                    rng,
                    riverSlot,
                    riverAttempt,
                    avoidCrossingCorridor,
                    occupiedRiverCells,
                    logRoute,
                    isMainRiver,
                    out expandedNodes,
                    out finalCost,
                    out sumNearRiverPen,
                    out sumHeightBias,
                    out pathCells,
                    out logStart,
                    out logGoal,
                    out rejectReason);
            }

            sw.Stop();
            pathLenForLog = pathCells != null ? pathCells.Count : 0;

            if (!ok || pathCells == null || pathCells.Count < 2)
            {
                rejectReason = string.IsNullOrEmpty(rejectReason) ? "route_fail" : rejectReason;
                if (logRoute)
                    LogRiverRoute(
                        riverSlot,
                        riverAttempt,
                        mergeToExistingRiver,
                        false,
                        pathLenForLog,
                        pathLenForLog,
                        logStart,
                        logGoal,
                        touchesRiver,
                        rejectReason,
                        sw.Elapsed.TotalMilliseconds);
                if (dbgCost)
                    LogRiverRouteCost(expandedNodes, finalCost, sumNearRiverPen, sumHeightBias, pathLenForLog);
                return false;
            }

            if (!mergeToExistingRiver)
            {
                TryExtendMainPathForBorderExit(
                    grid,
                    pathCells,
                    w,
                    h,
                    riverSlot,
                    config,
                    logRoute || config.debugHydrologyNetwork || config.debugLogs);
                if (pathCells != null && pathCells.Count > 0)
                    logGoal = pathCells[pathCells.Count - 1];
            }

            AccumulatePathEdgeCosts(
                grid,
                w,
                h,
                pathCells,
                riverSlot,
                riverAttempt,
                occupiedRiverCells,
                !mergeToExistingRiver,
                out finalCost,
                out sumNearRiverPen,
                out sumHeightBias);

            int mainMin = MainPathMinCells(w, h, config);
            int mainMax = MainPathMaxCells(w, h, config);
            int triMin = TributaryPathMinCells(w, h);
            int triMax = TributaryPathMaxCells(w, h);
            int triMinValidation = mergeToExistingRiver ? Mathf.Max(triMin, TributaryVisualMinCells(w, h)) : triMin;

            if (!HydrologyValidation.ValidatePlannedRiverCellPath(
                    grid,
                    pathCells,
                    mergeToExistingRiver,
                    w,
                    h,
                    mainMin,
                    mainMax,
                    triMinValidation,
                    triMax,
                    out string vPlan))
            {
                rejectReason = vPlan;
                if (logRoute)
                    LogRiverRoute(
                        riverSlot,
                        riverAttempt,
                        mergeToExistingRiver,
                        false,
                        pathCells.Count,
                        pathCells.Count,
                        logStart,
                        logGoal,
                        touchesRiver,
                        rejectReason,
                        sw.Elapsed.TotalMilliseconds);
                if (dbgCost)
                    LogRiverRouteCost(expandedNodes, finalCost, sumNearRiverPen, sumHeightBias, pathCells.Count);
                return false;
            }

            if (!mergeToExistingRiver)
            {
                centerlineCellSpace = BuildMainVisualCenterline(pathCells, w, h);
                SimpleRiverPathGenerator.OnePassLightSmooth(centerlineCellSpace, w, h, 0.22f);
                float maxSegLen = MaxConsecutiveSegmentLength(centerlineCellSpace);
                if (logRoute)
                {
                    float ratio = pathCells.Count > 0
                        ? centerlineCellSpace.Count / (float)pathCells.Count
                        : 0f;
                    LogRiverVisualCenterline(
                        riverSlot,
                        pathCells.Count,
                        centerlineCellSpace.Count,
                        ratio,
                        maxSegLen,
                        true);
                }
            }
            else
            {
                centerlineCellSpace = new List<Vector2>(pathCells.Count);
                for (int i = 0; i < pathCells.Count; i++)
                {
                    var c = pathCells[i];
                    centerlineCellSpace.Add(new Vector2(c.x + 0.5f, c.y + 0.5f));
                }

                SimpleRiverPathGenerator.OnePassLightSmooth(centerlineCellSpace, w, h, 0.32f);
            }

            debugMacro = new List<Vector2>(centerlineCellSpace);
            debugSmoothed = new List<Vector2>(centerlineCellSpace);
            fordCells = SimpleRiverPathGenerator.BuildFordAlongPath(pathCells, config, rng, w, h);

            bool hydroDetail = config.debugHydrologyNetwork || config.debugLogs;
            if (!mergeToExistingRiver && grid != null && pathCells != null && pathCells.Count > 0)
                PrioritizePlannedLakeSinksNearTerminus(grid, pathCells[pathCells.Count - 1]);

            if (mergeToExistingRiver && (logRoute || hydroDetail) && grid != null && pathCells != null && pathCells.Count > 0)
            {
                var endMv = pathCells[pathCells.Count - 1];
                bool conn = grid.InBoundsCell(endMv.x, endMv.y) && grid.GetCell(endMv.x, endMv.y).type == CellType.River;
                LogRiverSecondaryValidation(riverSlot, "tributary", conn, pathCells.Count, conn ? "accepted" : "unexpected");
            }

            if (logRoute)
                LogRiverRoute(
                    riverSlot,
                    riverAttempt,
                    mergeToExistingRiver,
                    true,
                    pathCells.Count,
                    pathCells.Count,
                    logStart,
                    logGoal,
                    mergeToExistingRiver && touchesRiver,
                    null,
                    sw.Elapsed.TotalMilliseconds);
            if (dbgCost)
                LogRiverRouteCost(expandedNodes, finalCost, sumNearRiverPen, sumHeightBias, pathCells.Count);

            return true;
        }

        static void AccumulatePathEdgeCosts(
            GridSystem grid,
            int w,
            int h,
            List<Vector2Int> path,
            int riverSlot,
            int attemptSalt,
            HashSet<long> occupiedRiverCells,
            bool strongStraightPenalty,
            out float totalG,
            out float sumNearRiverPen,
            out float sumHeightBias)
        {
            totalG = 0f;
            sumNearRiverPen = 0f;
            sumHeightBias = 0f;
            if (path == null || path.Count < 2)
                return;

            for (int i = 1; i < path.Count; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                int pdx = i >= 2 ? a.x - path[i - 2].x : 0;
                int pdy = i >= 2 ? a.y - path[i - 2].y : 0;
                float step = EdgeCost(
                    grid,
                    w,
                    h,
                    a.x,
                    a.y,
                    b.x,
                    b.y,
                    pdx,
                    pdy,
                    riverSlot,
                    attemptSalt,
                    occupiedRiverCells,
                    strongStraightPenalty,
                    out float penR,
                    out float penH);
                totalG += step;
                sumNearRiverPen += penR;
                sumHeightBias += penH;
            }
        }

        static void LogRiverRoute(
            int slot,
            int attempt,
            bool merge,
            bool accepted,
            int pathCellsCount,
            int length,
            Vector2Int start,
            Vector2Int goal,
            bool touchesRiver,
            string rejectReason,
            double ms)
        {
            string rr = rejectReason ?? "none";
            UnityEngine.Debug.Log(
                $"[RiverRoute] slot={slot} attempt={attempt} merge={(merge ? 1 : 0)} accepted={(accepted ? 1 : 0)} pathCells={pathCellsCount} length={length} " +
                $"start={start} goal={goal} touchesRiver={(touchesRiver ? 1 : 0)} rejectReason={rr} ms={ms:F2}");
        }

        static void LogRiverRouteCost(int expanded, float finalCost, float nearRiverPenalty, float heightBias, int len)
        {
            float n = len > 0 ? nearRiverPenalty / len : 0f;
            float hb = len > 0 ? heightBias / len : 0f;
            UnityEngine.Debug.Log(
                $"[RiverRouteCost] expandedNodes={expanded} finalCost={finalCost:F2} nearRiverPenalty={n:F3} heightBias={hb:F3}");
        }

        static void LogRiverRoutePair(
            int slot,
            int attempt,
            int pairIndex,
            Vector2Int start,
            Vector2Int goal,
            int manhattan,
            bool pathFound,
            int rawPathCells,
            string rejectReason,
            RiverMainPattern pattern = RiverMainPattern.BorderToBorder,
            RiverAnchorKind startKind = RiverAnchorKind.BorderExit,
            RiverAnchorKind goalKind = RiverAnchorKind.BorderExit)
        {
            UnityEngine.Debug.Log(
                $"[RiverRoutePair] slot={slot} attempt={attempt} pairIndex={pairIndex} pattern={pattern} startKind={startKind} goalKind={goalKind} " +
                $"start={start} goal={goal} manhattan={manhattan} pathFound={(pathFound ? 1 : 0)} rawPathCells={rawPathCells} rejectReason={rejectReason}");
        }

        static void LogRiverRouteShape(
            int slot,
            int isMainRiver,
            RiverMainPattern pattern,
            int pathCells,
            float straightnessRatio,
            int straightRunMaxCells,
            int bendCount,
            int waypointsUsed,
            bool shapeAccepted,
            int acceptedStraightFallback,
            string note)
        {
            string n = string.IsNullOrEmpty(note) ? "na" : note;
            UnityEngine.Debug.Log(
                $"[RiverRouteShape] slot={slot} isMainRiver={isMainRiver} pattern={pattern} pathCells={pathCells} " +
                $"straightnessRatio={straightnessRatio:F3} straightRunMaxCells={straightRunMaxCells} bendCount={bendCount} " +
                $"waypointsUsed={waypointsUsed} shapeAccepted={(shapeAccepted ? 1 : 0)} acceptedStraightFallback={acceptedStraightFallback} note={n}");
        }

        static void LogRiverRouteOrganicCheck(
            int slot,
            int isMainRiver,
            int merge,
            RiverMainPattern pattern,
            int pathCells,
            float straightnessRatio,
            float thresholdStraightness,
            int straightRunMaxCells,
            int thresholdStraightRun,
            int bendCount,
            int needsOrganicReshape)
        {
            UnityEngine.Debug.Log(
                $"[RiverRouteOrganicCheck] slot={slot} isMainRiver={isMainRiver} merge={merge} pattern={pattern} pathCells={pathCells} " +
                $"straightnessRatio={straightnessRatio:F3} thresholdStraightness={thresholdStraightness:F3} " +
                $"straightRunMaxCells={straightRunMaxCells} thresholdStraightRun={thresholdStraightRun} bendCount={bendCount} " +
                $"needsOrganicReshape={needsOrganicReshape}");
        }

        static void LogRiverRouteOrganicReshape(
            int slot,
            int isMainRiver,
            int attempt,
            int waypoints,
            int accepted,
            float oldStraightness,
            float newStraightness,
            int oldStraightRun,
            int newStraightRun,
            double ms,
            string reason)
        {
            string r = string.IsNullOrEmpty(reason) ? "na" : reason;
            UnityEngine.Debug.Log(
                $"[RiverRouteOrganicReshape] slot={slot} isMainRiver={isMainRiver} attempt={attempt} waypoints={waypoints} accepted={accepted} " +
                $"oldStraightness={oldStraightness:F3} newStraightness={newStraightness:F3} oldStraightRun={oldStraightRun} " +
                $"newStraightRun={newStraightRun} ms={ms:F1} reason={r}");
        }

        static bool MainRiverNeedsOrganicReshape(
            bool isMainRiver,
            MapGenConfig cfg,
            int pathCells,
            float straightnessRatio,
            int straightRunMaxCells,
            int bendCount)
        {
            if (!isMainRiver || cfg == null || !cfg.riverMainForceOrganicReshape || pathCells <= 2)
                return false;
            float thSr = cfg.riverMainMaxAcceptedStraightnessRatio > 0.001f
                ? cfg.riverMainMaxAcceptedStraightnessRatio
                : 0.64f;
            int thRun = cfg.riverMainMaxStraightRunCells > 0 ? cfg.riverMainMaxStraightRunCells : 18;
            int segs = pathCells - 1;
            bool bendSparse = segs > 30 && bendCount * 22 < segs;
            return straightnessRatio > thSr || straightRunMaxCells > thRun || bendSparse;
        }

        static bool MainRiverShapeAcceptedForLog(
            bool isMainRiver,
            MapGenConfig cfg,
            int pathCells,
            float straightnessRatio,
            int straightRunMaxCells,
            int bendCount,
            bool acceptedStraightFallback)
        {
            if (acceptedStraightFallback)
                return false;
            if (pathCells <= 2)
                return true;
            if (!isMainRiver || cfg == null || !cfg.riverMainForceOrganicReshape)
            {
                return !(pathCells > 2 &&
                    (straightRunMaxCells > Mathf.Min(64, Mathf.FloorToInt(pathCells * 0.28f)) ||
                     straightnessRatio > 0.88f));
            }

            return !MainRiverNeedsOrganicReshape(isMainRiver, cfg, pathCells, straightnessRatio, straightRunMaxCells, bendCount);
        }

        static bool MainRiverReshapeImprovedPath(
            MapGenConfig cfg,
            bool needsOrganic,
            float oldSr,
            int oldRun,
            float newSr,
            int newRun)
        {
            if (newSr < oldSr - 0.035f || newRun < oldRun - 2)
                return true;
            if (!needsOrganic || cfg == null)
                return false;
            float maxSr = cfg.riverMainMaxAcceptedStraightnessRatio > 0.001f
                ? cfg.riverMainMaxAcceptedStraightnessRatio
                : 0.64f;
            int maxRunT = cfg.riverMainMaxStraightRunCells > 0 ? cfg.riverMainMaxStraightRunCells : 18;
            if (newSr <= maxSr + 0.02f && (newSr < oldSr - 0.006f || newRun < oldRun))
                return true;
            if (newRun <= maxRunT && newRun < oldRun)
                return true;
            return false;
        }

        static void LogRiverSecondaryValidation(
            int slot,
            string mode,
            bool connected,
            int pathCells,
            string reason)
        {
            UnityEngine.Debug.Log(
                $"[RiverSecondaryValidation] slot={slot} mode={mode} connected={(connected ? 1 : 0)} pathCells={pathCells} reason={reason}");
        }

        static void LogRiverRouteMeander(
            int slot,
            Vector2Int start,
            int wpCount,
            Vector2Int wp1,
            Vector2Int wp2,
            Vector2Int goal,
            int directFallback,
            int lateralDelta,
            int pathCells)
        {
            string w1 = wpCount >= 1 ? wp1.ToString() : "na";
            string w2 = wpCount >= 2 ? wp2.ToString() : "na";
            UnityEngine.Debug.Log(
                $"[RiverRouteMeander] slot={slot} start={start} wpCount={wpCount} wp1={w1} wp2={w2} goal={goal} " +
                $"directFallback={directFallback} lateralDelta={lateralDelta} pathCells={pathCells}");
        }

        static void LogRiverRouteWaypointFallback(int slot, int pairIndex, string reason)
        {
            UnityEngine.Debug.Log($"[RiverRouteWaypointFallback] slot={slot} pairIndex={pairIndex} reason={reason}");
        }

        static void LogRiverRouteFatal(string reason)
        {
            UnityEngine.Debug.LogError($"[RiverRouteFatal] reason={reason}");
        }

        static void LogRiverRouteMeanderRetry(int slot, string reason, int variant, int oldPathCells, int newPathCells)
        {
            UnityEngine.Debug.Log(
                $"[RiverRouteMeanderRetry] slot={slot} reason={reason} variant={variant} oldPathCells={oldPathCells} newPathCells={newPathCells}");
        }

        static void LogRiverRouteBudgetAbort(
            int slot,
            int merge,
            int attempt,
            int expandedNodes,
            double ms,
            string reason)
        {
            UnityEngine.Debug.Log(
                $"[RiverRouteBudgetAbort] slot={slot} merge={merge} attempt={attempt} expandedNodes={expandedNodes} ms={ms:F1} reason={reason}");
        }

        static void LogRiverVisualCenterline(
            int slot,
            int pathCells,
            int visualPoints,
            float reductionRatio,
            float maxSegmentLength,
            bool accepted)
        {
            UnityEngine.Debug.Log(
                $"[RiverVisualCenterline] slot={slot} pathCells={pathCells} visualPoints={visualPoints} " +
                $"reductionRatio={reductionRatio:F3} maxSegmentLength={maxSegmentLength:F3} accepted={(accepted ? 1 : 0)}");
        }

        static void LogRiverRouteTributaryReject(int slot, int pathCells, string reason)
        {
            UnityEngine.Debug.Log($"[RiverRouteTributaryReject] slot={slot} pathCells={pathCells} reason={reason}");
        }

        static bool PathHasDuplicateCell(List<Vector2Int> path)
        {
            if (path == null || path.Count < 2)
                return false;
            var seen = new HashSet<long>();
            for (int i = 0; i < path.Count; i++)
            {
                var c = path[i];
                if (!seen.Add(Pack(c.x, c.y)))
                    return true;
            }

            return false;
        }

        static int MainPathHardMaxAccept(int w, int h, int manhattan)
        {
            int dyn = MainPathMaxCells(w, h, null);
            int capM = Mathf.CeilToInt(manhattan * 1.55f);
            return Mathf.Min(dyn, Mathf.Min(capM, 430));
        }

        static bool MainPathBacktrackingTooHigh(Vector2Int start, Vector2Int goal, List<Vector2Int> path)
        {
            if (path == null || path.Count < 3)
                return false;
            float gx = goal.x - start.x;
            float gy = goal.y - start.y;
            float d2 = gx * gx + gy * gy;
            if (d2 < 1e-4f)
                return false;
            float furthest = 0f;
            float penalty = 0f;
            for (int i = 0; i < path.Count; i++)
            {
                float vx = path[i].x - start.x;
                float vy = path[i].y - start.y;
                float t = (vx * gx + vy * gy) / d2;
                if (t + 1e-4f < furthest)
                    penalty += furthest - t;
                furthest = Mathf.Max(furthest, t);
            }

            return penalty > 0.18f;
        }

        /// <summary>Validación rápida post-A* main (antes de HydrologyValidation).</summary>
        static bool ValidateMainPathQuick(
            int w,
            int h,
            Vector2Int start,
            Vector2Int goal,
            List<Vector2Int> path,
            int minMain,
            int maxMain,
            bool hadWaypoints,
            out string badReason)
        {
            badReason = null;
            if (path == null || path.Count < 2)
            {
                badReason = "path_empty";
                return false;
            }

            if (PathHasDuplicateCell(path))
            {
                badReason = "self_intersection";
                return false;
            }

            int manhattan = Mathf.Abs(goal.x - start.x) + Mathf.Abs(goal.y - start.y);
            if (hadWaypoints && path.Count > manhattan * 1.45f + 1)
            {
                badReason = "meander_too_long_ratio";
                return false;
            }

            int hardMax = MainPathHardMaxAccept(w, h, manhattan);
            if (path.Count > hardMax)
            {
                badReason = "main_too_meandering";
                return false;
            }

            if (path.Count < minMain || path.Count > maxMain)
            {
                badReason = "length_band";
                return false;
            }

            if (MainPathBacktrackingTooHigh(start, goal, path))
            {
                badReason = "route_backtracking_too_high";
                return false;
            }

            return true;
        }

        static bool SatisfiesLateralSeparation(int w, int h, Vector2Int start, Vector2Int goal)
        {
            int dx = Mathf.Abs(goal.x - start.x);
            int dy = Mathf.Abs(goal.y - start.y);
            bool mostlyVertical = dy >= dx;
            if (mostlyVertical)
                return dx >= Mathf.RoundToInt(w * 0.25f);
            return dy >= Mathf.RoundToInt(h * 0.25f);
        }

        static int LateralDeltaForLog(int w, int h, Vector2Int start, Vector2Int goal)
        {
            int dx = Mathf.Abs(goal.x - start.x);
            int dy = Mathf.Abs(goal.y - start.y);
            return dy >= dx ? dx : dy;
        }

        static bool CellPairIsLand(GridSystem grid, Vector2Int a, Vector2Int b) =>
            grid.InBoundsCell(a.x, a.y) &&
            grid.InBoundsCell(b.x, b.y) &&
            grid.GetCell(a.x, a.y).type == CellType.Land &&
            grid.GetCell(b.x, b.y).type == CellType.Land;

        static bool TryGetDeterministicBorderPair(
            GridSystem grid,
            int w,
            int h,
            int margin,
            int mode,
            out Vector2Int start,
            out Vector2Int goal)
        {
            start = default;
            goal = default;
            int xLo = margin;
            int xHi = w - 1 - margin;
            int zLo = margin;
            int zHi = h - 1 - margin;
            if (xHi <= xLo || zHi <= zLo)
                return false;

            int needX = Mathf.Max(1, Mathf.RoundToInt(w * 0.25f));
            int needZ = Mathf.Max(1, Mathf.RoundToInt(h * 0.25f));
            int zMid = (zLo + zHi) / 2;
            int xMid = (xLo + xHi) / 2;
            int spanZ = zHi - zLo;
            int spanX = xHi - xLo;
            int dz = Mathf.Clamp(needZ / 2, 1, Mathf.Max(1, spanZ / 2));
            int dx = Mathf.Clamp(needX / 2, 1, Mathf.Max(1, spanX / 2));

            switch (mode)
            {
                case 0:
                {
                    int zA = Mathf.Clamp(zMid + dz, zLo, zHi);
                    int zB = Mathf.Clamp(zMid - dz, zLo, zHi);
                    if (Mathf.Abs(zA - zB) < needZ && spanZ >= needZ)
                    {
                        zA = Mathf.Min(zHi, zLo + needZ);
                        zB = zLo;
                    }

                    var s = new Vector2Int(xLo, zA);
                    var g = new Vector2Int(xHi, zB);
                    if (CellPairIsLand(grid, s, g) && SatisfiesLateralSeparation(w, h, s, g))
                    {
                        start = s;
                        goal = g;
                        return true;
                    }

                    for (int k = 0; k < 4; k++)
                    {
                        int t = zLo + (spanZ * (k + 1)) / 5;
                        zA = Mathf.Clamp(t + dz, zLo, zHi);
                        zB = Mathf.Clamp(t - dz, zLo, zHi);
                        s = new Vector2Int(xLo, zA);
                        g = new Vector2Int(xHi, zB);
                        if (CellPairIsLand(grid, s, g) && SatisfiesLateralSeparation(w, h, s, g))
                        {
                            start = s;
                            goal = g;
                            return true;
                        }
                    }

                    break;
                }
                case 1:
                {
                    int zA = Mathf.Clamp(zMid + dz, zLo, zHi);
                    int zB = Mathf.Clamp(zMid - dz, zLo, zHi);
                    if (Mathf.Abs(zA - zB) < needZ && spanZ >= needZ)
                    {
                        zA = Mathf.Min(zHi, zLo + needZ);
                        zB = zLo;
                    }

                    var s = new Vector2Int(xHi, zA);
                    var g = new Vector2Int(xLo, zB);
                    if (CellPairIsLand(grid, s, g) && SatisfiesLateralSeparation(w, h, s, g))
                    {
                        start = s;
                        goal = g;
                        return true;
                    }

                    for (int k = 0; k < 4; k++)
                    {
                        int t = zLo + (spanZ * (k + 1)) / 5;
                        zA = Mathf.Clamp(t + dz, zLo, zHi);
                        zB = Mathf.Clamp(t - dz, zLo, zHi);
                        s = new Vector2Int(xHi, zA);
                        g = new Vector2Int(xLo, zB);
                        if (CellPairIsLand(grid, s, g) && SatisfiesLateralSeparation(w, h, s, g))
                        {
                            start = s;
                            goal = g;
                            return true;
                        }
                    }

                    break;
                }
                case 2:
                {
                    int xA = Mathf.Clamp(xMid + dx, xLo, xHi);
                    int xB = Mathf.Clamp(xMid - dx, xLo, xHi);
                    if (Mathf.Abs(xA - xB) < needX && spanX >= needX)
                    {
                        xA = Mathf.Min(xHi, xLo + needX);
                        xB = xLo;
                    }

                    var s = new Vector2Int(xA, zLo);
                    var g = new Vector2Int(xB, zHi);
                    if (CellPairIsLand(grid, s, g) && SatisfiesLateralSeparation(w, h, s, g))
                    {
                        start = s;
                        goal = g;
                        return true;
                    }

                    for (int k = 0; k < 4; k++)
                    {
                        int t = xLo + (spanX * (k + 1)) / 5;
                        xA = Mathf.Clamp(t + dx, xLo, xHi);
                        xB = Mathf.Clamp(t - dx, xLo, xHi);
                        s = new Vector2Int(xA, zLo);
                        g = new Vector2Int(xB, zHi);
                        if (CellPairIsLand(grid, s, g) && SatisfiesLateralSeparation(w, h, s, g))
                        {
                            start = s;
                            goal = g;
                            return true;
                        }
                    }

                    break;
                }
                case 3:
                {
                    int xA = Mathf.Clamp(xMid + dx, xLo, xHi);
                    int xB = Mathf.Clamp(xMid - dx, xLo, xHi);
                    if (Mathf.Abs(xA - xB) < needX && spanX >= needX)
                    {
                        xA = Mathf.Min(xHi, xLo + needX);
                        xB = xLo;
                    }

                    var s = new Vector2Int(xA, zHi);
                    var g = new Vector2Int(xB, zLo);
                    if (CellPairIsLand(grid, s, g) && SatisfiesLateralSeparation(w, h, s, g))
                    {
                        start = s;
                        goal = g;
                        return true;
                    }

                    for (int k = 0; k < 4; k++)
                    {
                        int t = xLo + (spanX * (k + 1)) / 5;
                        xA = Mathf.Clamp(t + dx, xLo, xHi);
                        xB = Mathf.Clamp(t - dx, xLo, xHi);
                        s = new Vector2Int(xA, zHi);
                        g = new Vector2Int(xB, zLo);
                        if (CellPairIsLand(grid, s, g) && SatisfiesLateralSeparation(w, h, s, g))
                        {
                            start = s;
                            goal = g;
                            return true;
                        }
                    }

                    break;
                }
            }

            return false;
        }

        static void ComputeRiverPathShapeMetrics(
            List<Vector2Int> path,
            out int straightRunMaxCells,
            out float straightnessRatio,
            out int bendCount)
        {
            straightRunMaxCells = 0;
            straightnessRatio = 0f;
            bendCount = 0;
            if (path == null || path.Count < 2)
                return;

            int n = path.Count;
            float pathLen = n - 1;
            var a = path[0];
            var b = path[n - 1];
            float direct = Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
            straightnessRatio = pathLen > 1e-5f ? direct / pathLen : 1f;

            int run = 1;
            int maxRun = 1;
            int bends = 0;
            int ldx = 0, ldy = 0;
            for (int i = 1; i < n; i++)
            {
                int dx = path[i].x - path[i - 1].x;
                int dy = path[i].y - path[i - 1].y;
                if (i > 1 && (dx != ldx || dy != ldy))
                    bends++;
                if (i > 1 && dx == ldx && dy == ldy)
                    run++;
                else
                    run = 1;
                maxRun = Mathf.Max(maxRun, run);
                ldx = dx;
                ldy = dy;
            }

            straightRunMaxCells = maxRun;
            bendCount = bends;
        }

        static bool TryPickBasinBiasWaypoint(
            GridSystem grid,
            int w,
            int h,
            IReadOnlyList<Vector2Int> pool,
            Vector2Int start,
            Vector2Int goal,
            IRng rng,
            int salt,
            float tMin,
            float tMax,
            out Vector2Int wp)
        {
            wp = default;
            if (pool == null || pool.Count == 0 || rng == null)
                return false;

            Vector2 g = new Vector2(goal.x - start.x, goal.y - start.y);
            float gLen = g.magnitude;
            if (gLen < 2f)
                return false;
            Vector2 gN = g / gLen;
            int inner = Mathf.Max(4, Mathf.Min(w, h) / 32);
            float best = -1f;
            Vector2Int bestC = default;
            int tries = Mathf.Min(64, pool.Count * 4);
            for (int k = 0; k < tries; k++)
            {
                var c = pool[rng.NextInt(0, pool.Count)];
                if (!IsStrictInterior(c.x, c.y, w, h, inner))
                    continue;
                if (grid.GetCell(c.x, c.y).type != CellType.Land)
                    continue;

                Vector2 v = new Vector2(c.x - start.x, c.y - start.y);
                float t = Vector2.Dot(v, gN) / gLen;
                if (t < tMin || t > tMax)
                    continue;
                Vector2 ortho = v - gN * (t * gLen);
                float score = ortho.sqrMagnitude;
                if (score > best)
                {
                    best = score;
                    bestC = c;
                }
            }

            if (best < 6f)
                return false;

            wp = bestC;
            if (!SnapInteriorLandWaypoint(grid, w, h, inner, ref wp))
                return false;
            if (wp == start || wp == goal)
                return false;
            return true;
        }

        static bool TryBasinBiasedWaypointReshapePath(
            GridSystem grid,
            int w,
            int h,
            Vector2Int start,
            Vector2Int goal,
            IReadOnlyList<Vector2Int> biasPool,
            IRng rng,
            int riverSlot,
            int riverAttempt,
            int pairIndex,
            int minMain,
            int maxMain,
            bool avoidCrossingCorridor,
            HashSet<long> occupiedRiverCells,
            long pairDeadline,
            int maxWaypoints,
            bool isMainRiver,
            MapGenConfig routeShapeConfig,
            out List<Vector2Int> path,
            out int expandedNodes,
            out float finalCost,
            out float sumNearRiverPen,
            out float sumHeightBias)
        {
            path = null;
            expandedNodes = 0;
            finalCost = 0f;
            sumNearRiverPen = 0f;
            sumHeightBias = 0f;
            if (biasPool == null || biasPool.Count == 0 || rng == null)
                return false;
            if (MonotonicMs() >= pairDeadline)
                return false;

            maxWaypoints = Mathf.Clamp(maxWaypoints, 1, 2);
            Vector2Int[] mids;
            if (maxWaypoints >= 2 &&
                TryPickBasinBiasWaypoint(grid, w, h, biasPool, start, goal, rng, riverAttempt * 17 + pairIndex, 0.18f, 0.42f, out Vector2Int w1) &&
                TryPickBasinBiasWaypoint(grid, w, h, biasPool, start, goal, rng, riverAttempt * 19 + pairIndex + 3, 0.55f, 0.82f, out Vector2Int w2) &&
                w1 != w2)
            {
                mids = new[] { w1, w2 };
            }
            else if (TryPickBasinBiasWaypoint(grid, w, h, biasPool, start, goal, rng, riverAttempt * 13 + pairIndex * 5, 0.22f, 0.78f, out Vector2Int wOnly))
            {
                mids = new[] { wOnly };
            }
            else
                return false;

            if (!TryRunWaypointChain(
                    grid,
                    w,
                    h,
                    start,
                    mids,
                    goal,
                    pairIndex + 900,
                    minMain,
                    maxMain,
                    riverSlot,
                    avoidCrossingCorridor,
                    occupiedRiverCells,
                    pairDeadline,
                    isMainRiver,
                    routeShapeConfig,
                    out path,
                    out expandedNodes,
                    out finalCost,
                    out sumNearRiverPen,
                    out sumHeightBias,
                    out string segFail))
                return false;

            return path != null && path.Count >= minMain;
        }

        /// <summary>Intento de un par borde↔borde (no captura out del caller).</summary>
        static bool TryMainBorderPairEval(
            GridSystem grid,
            int w,
            int h,
            Vector2Int start,
            Vector2Int goal,
            int pairIndex,
            int minMain,
            int maxMain,
            int riverSlot,
            int riverAttempt,
            bool logRoute,
            bool avoidCrossingCorridor,
            HashSet<long> occupiedRiverCells,
            IRng rng,
            IReadOnlyList<Vector2Int> shapeBiasPool,
            out int expandedNodes,
            out float finalCost,
            out float sumNearRiverPen,
            out float sumHeightBias,
            out List<Vector2Int> chosenPath,
            out Vector2Int chosenStart,
            out Vector2Int chosenGoal,
            out string attemptReject,
            out int rawPathLen,
            out bool astarReturnedPath,
            bool isMainRiver,
            RiverMainPattern anchorPattern = RiverMainPattern.BorderToBorder,
            RiverAnchorKind startAnchorKind = RiverAnchorKind.BorderExit,
            RiverAnchorKind goalAnchorKind = RiverAnchorKind.BorderExit,
            MapGenConfig routeConfig = null)
        {
            expandedNodes = 0;
            finalCost = 0f;
            sumNearRiverPen = 0f;
            sumHeightBias = 0f;
            chosenPath = null;
            chosenStart = default;
            chosenGoal = default;
            attemptReject = null;
            rawPathLen = 0;
            astarReturnedPath = false;

            int lateralLog = LateralDeltaForLog(w, h, start, goal);
            bool meanderTried = false;
            string waypointFailReason = null;
            long pairDeadline = MonotonicMs() + MainPairBudgetMs;

            if (TryBuildWaypointSegmentedMainPath(
                    grid,
                    w,
                    h,
                    start,
                    goal,
                    pairIndex,
                    minMain,
                    maxMain,
                    riverSlot,
                    riverAttempt,
                    avoidCrossingCorridor,
                    occupiedRiverCells,
                    pairDeadline,
                    logRoute,
                    isMainRiver,
                    routeConfig,
                    out List<Vector2Int> meanderPath,
                    out int meanderExp,
                    out float meanderFc,
                    out float meanderSnp,
                    out float meanderShb,
                    out int wpCount,
                    out Vector2Int wp1,
                    out Vector2Int wp2,
                    out string meanderFail))
            {
                chosenPath = meanderPath;
                chosenStart = start;
                chosenGoal = goal;
                expandedNodes = meanderExp;
                finalCost = meanderFc;
                sumNearRiverPen = meanderSnp;
                sumHeightBias = meanderShb;
                rawPathLen = meanderPath.Count;
                astarReturnedPath = true;
                attemptReject = null;

                if (logRoute)
                    LogRiverRouteMeander(
                        riverSlot,
                        start,
                        wpCount,
                        wp1,
                        wp2,
                        goal,
                        0,
                        lateralLog,
                        meanderPath.Count);

                if (logRoute || (routeConfig != null && (routeConfig.debugLogs || routeConfig.debugHydrologyNetwork)))
                {
                    ComputeRiverPathShapeMetrics(meanderPath, out int srM, out float sratio, out int bdc);
                    float thSr = routeConfig != null && routeConfig.riverMainMaxAcceptedStraightnessRatio > 0.001f
                        ? routeConfig.riverMainMaxAcceptedStraightnessRatio
                        : 0.64f;
                    int thRun = routeConfig != null && routeConfig.riverMainMaxStraightRunCells > 0
                        ? routeConfig.riverMainMaxStraightRunCells
                        : 18;
                    bool needOr = MainRiverNeedsOrganicReshape(isMainRiver, routeConfig, meanderPath.Count, sratio, srM, bdc);
                    LogRiverRouteOrganicCheck(
                        riverSlot,
                        isMainRiver ? 1 : 0,
                        0,
                        anchorPattern,
                        meanderPath.Count,
                        sratio,
                        thSr,
                        srM,
                        thRun,
                        bdc,
                        needOr ? 1 : 0);
                    bool shapeOk = MainRiverShapeAcceptedForLog(isMainRiver, routeConfig, meanderPath.Count, sratio, srM, bdc, false);
                    int straightFb = needOr && !shapeOk ? 1 : 0;
                    LogRiverRouteShape(
                        riverSlot,
                        isMainRiver ? 1 : 0,
                        anchorPattern,
                        meanderPath.Count,
                        sratio,
                        srM,
                        bdc,
                        wpCount,
                        shapeOk,
                        straightFb,
                        "meander");
                }

                int manhattan = Mathf.Abs(start.x - goal.x) + Mathf.Abs(start.y - goal.y);
                if (logRoute)
                    LogRiverRoutePair(
                        riverSlot,
                        riverAttempt,
                        pairIndex,
                        start,
                        goal,
                        manhattan,
                        true,
                        meanderPath.Count,
                        "none",
                        anchorPattern,
                        startAnchorKind,
                        goalAnchorKind);
                return true;
            }

            meanderTried = true;
            waypointFailReason = meanderFail ?? "meander_failed";

            int manhattan2 = Mathf.Abs(start.x - goal.x) + Mathf.Abs(start.y - goal.y);
            long remMs = pairDeadline - MonotonicMs();
            long directDeadline = remMs <= 0
                ? MonotonicMs() + 8L
                : MonotonicMs() + Math.Max(8L, remMs);

            bool astarOk = RunAStarSingleGoal(
                grid,
                w,
                h,
                start,
                goal,
                mergeMode: false,
                occupiedRiverCells,
                avoidCrossingCorridor,
                goalRiverCells: null,
                riverSlot,
                pairIndex,
                MainAStarExpansionCap,
                strongStraightPenalty: true,
                directDeadline,
                routeConfig,
                isMainRiver && routeConfig != null,
                out expandedNodes,
                out finalCost,
                out sumNearRiverPen,
                out sumHeightBias,
                out List<Vector2Int> tryPath,
                out string rs);

            bool found = astarOk && tryPath != null;
            astarReturnedPath = found;
            rawPathLen = found ? tryPath.Count : 0;
            string logReject = found ? "none" : (rs ?? "no_path");

            if (logRoute)
            {
                LogRiverRoutePair(
                    riverSlot,
                    riverAttempt,
                    pairIndex,
                    start,
                    goal,
                    manhattan2,
                    found,
                    rawPathLen,
                    logReject,
                    anchorPattern,
                    startAnchorKind,
                    goalAnchorKind);
                if (meanderTried && found)
                    LogRiverRouteWaypointFallback(riverSlot, pairIndex, waypointFailReason);
            }

            if (!found)
            {
                attemptReject = logReject;
                return false;
            }

            if (!ValidateMainPathQuick(
                    w,
                    h,
                    start,
                    goal,
                    tryPath,
                    minMain,
                    maxMain,
                    hadWaypoints: false,
                    out string quickBad))
            {
                attemptReject = quickBad;
                if (logRoute)
                    LogRiverRoutePair(
                        riverSlot,
                        riverAttempt,
                        pairIndex,
                        start,
                        goal,
                        manhattan2,
                        true,
                        tryPath.Count,
                        attemptReject,
                        anchorPattern,
                        startAnchorKind,
                        goalAnchorKind);
                return false;
            }

            int waypointsShapeUsed = 0;
            ComputeRiverPathShapeMetrics(tryPath, out int straightRunMax, out float straightnessRatio, out int bendCount);
            int pathCellsCount = tryPath.Count;

            bool needsOrganic =
                MainRiverNeedsOrganicReshape(isMainRiver, routeConfig, pathCellsCount, straightnessRatio, straightRunMax, bendCount);

            bool diagOrganic =
                logRoute || (routeConfig != null && (routeConfig.debugLogs || routeConfig.debugHydrologyNetwork));
            if (diagOrganic && isMainRiver)
            {
                float thSr = routeConfig != null && routeConfig.riverMainMaxAcceptedStraightnessRatio > 0.001f
                    ? routeConfig.riverMainMaxAcceptedStraightnessRatio
                    : 0.64f;
                int thRun = routeConfig != null && routeConfig.riverMainMaxStraightRunCells > 0
                    ? routeConfig.riverMainMaxStraightRunCells
                    : 18;
                LogRiverRouteOrganicCheck(
                    riverSlot,
                    isMainRiver ? 1 : 0,
                    0,
                    anchorPattern,
                    pathCellsCount,
                    straightnessRatio,
                    thSr,
                    straightRunMax,
                    thRun,
                    bendCount,
                    needsOrganic ? 1 : 0);
            }

            bool legacyTooStraight =
                pathCellsCount > 2 &&
                (straightRunMax > Mathf.Min(64, Mathf.FloorToInt(pathCellsCount * 0.28f)) || straightnessRatio > 0.88f);

            var reshapeAttempts = new List<int>(2);
            if (needsOrganic && routeConfig != null && routeConfig.riverMainForceOrganicReshape)
            {
                int largeTh = Mathf.Clamp(routeConfig.riverMainOrganicLargeMapMinCells, 96, 512);
                if (Mathf.Max(w, h) >= largeTh)
                {
                    reshapeAttempts.Add(2);
                    reshapeAttempts.Add(1);
                }
                else
                    reshapeAttempts.Add(1);
            }
            else if (legacyTooStraight && pathCellsCount > 60)
            {
                reshapeAttempts.Add(pathCellsCount > 160 ? 2 : 1);
            }

            bool wantsBasinReshape =
                reshapeAttempts.Count > 0 &&
                shapeBiasPool != null &&
                shapeBiasPool.Count > 0 &&
                rng != null;

            bool canReshapeBudget =
                MonotonicMs() < pairDeadline &&
                (
                    (needsOrganic && routeConfig != null && routeConfig.riverMainForceOrganicReshape) ||
                    wantsBasinReshape);

            string riverShapeNote = "direct";

            if (canReshapeBudget)
            {
                long budgetMs = routeConfig != null
                    ? (long)Mathf.Clamp(routeConfig.riverMainOrganicReshapeBudgetMs, 10f, 200f)
                    : 80L;
                long reshapeDeadline = MonotonicMs() + Math.Min(budgetMs, Math.Max(0L, pairDeadline - MonotonicMs()));
                var swReshape = System.Diagnostics.Stopwatch.StartNew();
                float sr0 = straightnessRatio;
                int run0 = straightRunMax;
                int reshapeAttemptCounter = 0;

                if (needsOrganic && routeConfig != null && routeConfig.riverMainForceOrganicReshape)
                {
                    int largeTh = Mathf.Clamp(routeConfig.riverMainOrganicLargeMapMinCells, 96, 512);
                    bool mapLarge = Mathf.Max(w, h) >= largeTh;

                    if (mapLarge && MonotonicMs() < reshapeDeadline)
                    {
                        reshapeAttemptCounter++;
                        if (TryDeterministicOrganicSWaypointReshapePath(
                                grid,
                                w,
                                h,
                                start,
                                goal,
                                pairIndex,
                                minMain,
                                maxMain,
                                riverSlot,
                                riverAttempt,
                                avoidCrossingCorridor,
                                occupiedRiverCells,
                                reshapeDeadline,
                                2,
                                routeConfig,
                                isMainRiver,
                                out List<Vector2Int> sPath2,
                                out int sE2,
                                out float sF2,
                                out float sN2,
                                out float sH2))
                        {
                            if (ValidateMainPathQuick(
                                    w,
                                    h,
                                    start,
                                    goal,
                                    sPath2,
                                    minMain,
                                    maxMain,
                                    hadWaypoints: true,
                                    out string _))
                            {
                                ComputeRiverPathShapeMetrics(sPath2, out int sr2, out float sratio2, out int bends2);
                                if (MainRiverReshapeImprovedPath(
                                        routeConfig,
                                        needsOrganic,
                                        straightnessRatio,
                                        straightRunMax,
                                        sratio2,
                                        sr2))
                                {
                                    tryPath = sPath2;
                                    expandedNodes = sE2;
                                    finalCost = sF2;
                                    sumNearRiverPen = sN2;
                                    sumHeightBias = sH2;
                                    straightRunMax = sr2;
                                    straightnessRatio = sratio2;
                                    bendCount = bends2;
                                    pathCellsCount = tryPath.Count;
                                    waypointsShapeUsed = 2;
                                    riverShapeNote = "s_wp2";
                                    if (diagOrganic)
                                    {
                                        LogRiverRouteOrganicReshape(
                                            riverSlot,
                                            isMainRiver ? 1 : 0,
                                            reshapeAttemptCounter,
                                            2,
                                            1,
                                            sr0,
                                            straightnessRatio,
                                            run0,
                                            straightRunMax,
                                            swReshape.Elapsed.TotalMilliseconds,
                                            "s_accepted");
                                    }
                                }
                                else if (diagOrganic)
                                {
                                    LogRiverRouteOrganicReshape(
                                        riverSlot,
                                        isMainRiver ? 1 : 0,
                                        reshapeAttemptCounter,
                                        2,
                                        0,
                                        sr0,
                                        sratio2,
                                        run0,
                                        sr2,
                                        swReshape.Elapsed.TotalMilliseconds,
                                        "s_no_gain");
                                }
                            }
                            else if (diagOrganic)
                            {
                                LogRiverRouteOrganicReshape(
                                    riverSlot,
                                    isMainRiver ? 1 : 0,
                                    reshapeAttemptCounter,
                                    2,
                                    0,
                                    sr0,
                                    straightnessRatio,
                                    run0,
                                    straightRunMax,
                                    swReshape.Elapsed.TotalMilliseconds,
                                    "s_validate_fail");
                            }
                        }
                        else if (diagOrganic)
                        {
                            LogRiverRouteOrganicReshape(
                                riverSlot,
                                isMainRiver ? 1 : 0,
                                reshapeAttemptCounter,
                                2,
                                0,
                                sr0,
                                straightnessRatio,
                                run0,
                                straightRunMax,
                                swReshape.Elapsed.TotalMilliseconds,
                                "s_path_failed");
                        }
                    }

                    if (MainRiverNeedsOrganicReshape(isMainRiver, routeConfig, pathCellsCount, straightnessRatio, straightRunMax, bendCount) &&
                        MonotonicMs() < reshapeDeadline)
                    {
                        reshapeAttemptCounter++;
                        if (TryDeterministicOrganicSWaypointReshapePath(
                                grid,
                                w,
                                h,
                                start,
                                goal,
                                pairIndex,
                                minMain,
                                maxMain,
                                riverSlot,
                                riverAttempt,
                                avoidCrossingCorridor,
                                occupiedRiverCells,
                                reshapeDeadline,
                                1,
                                routeConfig,
                                isMainRiver,
                                out List<Vector2Int> sPath1,
                                out int sE1,
                                out float sF1,
                                out float sN1,
                                out float sH1))
                        {
                            if (ValidateMainPathQuick(
                                    w,
                                    h,
                                    start,
                                    goal,
                                    sPath1,
                                    minMain,
                                    maxMain,
                                    hadWaypoints: true,
                                    out string _))
                            {
                                ComputeRiverPathShapeMetrics(sPath1, out int sr2, out float sratio2, out int bends2);
                                if (MainRiverReshapeImprovedPath(
                                        routeConfig,
                                        needsOrganic,
                                        straightnessRatio,
                                        straightRunMax,
                                        sratio2,
                                        sr2))
                                {
                                    tryPath = sPath1;
                                    expandedNodes = sE1;
                                    finalCost = sF1;
                                    sumNearRiverPen = sN1;
                                    sumHeightBias = sH1;
                                    straightRunMax = sr2;
                                    straightnessRatio = sratio2;
                                    bendCount = bends2;
                                    pathCellsCount = tryPath.Count;
                                    waypointsShapeUsed = 1;
                                    riverShapeNote = "s_wp1";
                                    if (diagOrganic)
                                    {
                                        LogRiverRouteOrganicReshape(
                                            riverSlot,
                                            isMainRiver ? 1 : 0,
                                            reshapeAttemptCounter,
                                            1,
                                            1,
                                            sr0,
                                            straightnessRatio,
                                            run0,
                                            straightRunMax,
                                            swReshape.Elapsed.TotalMilliseconds,
                                            "s_accepted");
                                    }
                                }
                                else if (diagOrganic)
                                {
                                    LogRiverRouteOrganicReshape(
                                        riverSlot,
                                        isMainRiver ? 1 : 0,
                                        reshapeAttemptCounter,
                                        1,
                                        0,
                                        sr0,
                                        sratio2,
                                        run0,
                                        sr2,
                                        swReshape.Elapsed.TotalMilliseconds,
                                        "s_no_gain");
                                }
                            }
                            else if (diagOrganic)
                            {
                                LogRiverRouteOrganicReshape(
                                    riverSlot,
                                    isMainRiver ? 1 : 0,
                                    reshapeAttemptCounter,
                                    1,
                                    0,
                                    sr0,
                                    straightnessRatio,
                                    run0,
                                    straightRunMax,
                                    swReshape.Elapsed.TotalMilliseconds,
                                    "s_validate_fail");
                            }
                        }
                        else if (diagOrganic)
                        {
                            LogRiverRouteOrganicReshape(
                                riverSlot,
                                isMainRiver ? 1 : 0,
                                reshapeAttemptCounter,
                                1,
                                0,
                                sr0,
                                straightnessRatio,
                                run0,
                                straightRunMax,
                                swReshape.Elapsed.TotalMilliseconds,
                                "s_path_failed");
                        }
                    }
                }

                bool skipBasinForOrganic =
                    needsOrganic &&
                    !MainRiverNeedsOrganicReshape(isMainRiver, routeConfig, pathCellsCount, straightnessRatio, straightRunMax, bendCount);

                if (wantsBasinReshape && MonotonicMs() < reshapeDeadline && !skipBasinForOrganic)
                {
                    for (int ri = 0; ri < reshapeAttempts.Count; ri++)
                    {
                        if (MonotonicMs() >= reshapeDeadline)
                            break;
                        int maxWp = reshapeAttempts[ri];
                        if (ri > 0 && maxWp == reshapeAttempts[ri - 1])
                            continue;

                        reshapeAttemptCounter++;
                        if (!TryBasinBiasedWaypointReshapePath(
                                grid,
                                w,
                                h,
                                start,
                                goal,
                                shapeBiasPool,
                                rng,
                                riverSlot,
                                riverAttempt,
                                pairIndex,
                                minMain,
                                maxMain,
                                avoidCrossingCorridor,
                                occupiedRiverCells,
                                reshapeDeadline,
                                maxWp,
                                isMainRiver,
                                routeConfig,
                                out List<Vector2Int> basinPath,
                                out int bExp,
                                out float bFc,
                                out float bSnp,
                                out float bShb))
                        {
                            if (diagOrganic)
                            {
                                LogRiverRouteOrganicReshape(
                                    riverSlot,
                                    isMainRiver ? 1 : 0,
                                    reshapeAttemptCounter,
                                    maxWp,
                                    0,
                                    sr0,
                                    straightnessRatio,
                                    run0,
                                    straightRunMax,
                                    swReshape.Elapsed.TotalMilliseconds,
                                    "basin_path_failed");
                            }

                            continue;
                        }

                        if (!ValidateMainPathQuick(
                                w,
                                h,
                                start,
                                goal,
                                basinPath,
                                minMain,
                                maxMain,
                                hadWaypoints: true,
                                out string _))
                        {
                            if (diagOrganic)
                            {
                                LogRiverRouteOrganicReshape(
                                    riverSlot,
                                    isMainRiver ? 1 : 0,
                                    reshapeAttemptCounter,
                                    maxWp,
                                    0,
                                    sr0,
                                    straightnessRatio,
                                    run0,
                                    straightRunMax,
                                    swReshape.Elapsed.TotalMilliseconds,
                                    "validate_fail");
                            }

                            continue;
                        }

                        ComputeRiverPathShapeMetrics(basinPath, out int sr2, out float sratio2, out int bends2);
                        bool better = MainRiverReshapeImprovedPath(
                            routeConfig,
                            needsOrganic,
                            straightnessRatio,
                            straightRunMax,
                            sratio2,
                            sr2);
                        if (better)
                        {
                            tryPath = basinPath;
                            expandedNodes = bExp;
                            finalCost = bFc;
                            sumNearRiverPen = bSnp;
                            sumHeightBias = bShb;
                            straightRunMax = sr2;
                            straightnessRatio = sratio2;
                            bendCount = bends2;
                            pathCellsCount = tryPath.Count;
                            waypointsShapeUsed = maxWp;
                            riverShapeNote = "basin_wp";
                            if (diagOrganic)
                            {
                                LogRiverRouteOrganicReshape(
                                    riverSlot,
                                    isMainRiver ? 1 : 0,
                                    reshapeAttemptCounter,
                                    maxWp,
                                    1,
                                    sr0,
                                    straightnessRatio,
                                    run0,
                                    straightRunMax,
                                    swReshape.Elapsed.TotalMilliseconds,
                                    "accepted");
                            }

                            if (needsOrganic &&
                                MainRiverShapeAcceptedForLog(
                                    isMainRiver,
                                    routeConfig,
                                    pathCellsCount,
                                    straightnessRatio,
                                    straightRunMax,
                                    bendCount,
                                    false))
                                break;
                        }
                        else if (diagOrganic)
                        {
                            LogRiverRouteOrganicReshape(
                                riverSlot,
                                isMainRiver ? 1 : 0,
                                reshapeAttemptCounter,
                                maxWp,
                                0,
                                sr0,
                                sratio2,
                                run0,
                                sr2,
                                swReshape.Elapsed.TotalMilliseconds,
                                "no_gain");
                        }
                    }
                }
            }

            if (logRoute || (routeConfig != null && (routeConfig.debugLogs || routeConfig.debugHydrologyNetwork)))
            {
                bool shapeOk = MainRiverShapeAcceptedForLog(
                    isMainRiver,
                    routeConfig,
                    pathCellsCount,
                    straightnessRatio,
                    straightRunMax,
                    bendCount,
                    false);
                int straightFb = needsOrganic && !shapeOk ? 1 : 0;
                LogRiverRouteShape(
                    riverSlot,
                    isMainRiver ? 1 : 0,
                    anchorPattern,
                    pathCellsCount,
                    straightnessRatio,
                    straightRunMax,
                    bendCount,
                    waypointsShapeUsed,
                    shapeOk,
                    straightFb,
                    riverShapeNote);
            }

            chosenPath = tryPath;
            chosenStart = start;
            chosenGoal = goal;
            attemptReject = null;

            if (logRoute)
                LogRiverRouteMeander(
                    riverSlot,
                    start,
                    waypointsShapeUsed,
                    default,
                    default,
                    goal,
                    waypointsShapeUsed > 0 ? 0 : 1,
                    lateralLog,
                    tryPath.Count);

            return true;
        }

        static float PerpDistanceSqCell(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = Vector2.Dot(ab, ab);
            if (denom < 1e-8f)
                return (p - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
            Vector2 proj = a + t * ab;
            return (p - proj).sqrMagnitude;
        }

        static void RdpCollectIndices(List<Vector2> pts, int i0, int i1, float epsSq, HashSet<int> keep)
        {
            if (i1 <= i0 + 1)
                return;
            float dmax = 0f;
            int imax = i0;
            Vector2 a = pts[i0];
            Vector2 b = pts[i1];
            for (int i = i0 + 1; i < i1; i++)
            {
                float d = PerpDistanceSqCell(pts[i], a, b);
                if (d > dmax)
                {
                    dmax = d;
                    imax = i;
                }
            }

            if (dmax > epsSq)
            {
                RdpCollectIndices(pts, i0, imax, epsSq, keep);
                keep.Add(imax);
                RdpCollectIndices(pts, imax, i1, epsSq, keep);
            }
        }

        static List<int> BuildRdpKeepIndices(List<Vector2> pts, float epsilon)
        {
            int n = pts.Count;
            if (n <= 2)
            {
                var r = new List<int>(n);
                for (int i = 0; i < n; i++)
                    r.Add(i);
                return r;
            }

            var keep = new HashSet<int> { 0, n - 1 };
            RdpCollectIndices(pts, 0, n - 1, epsilon * epsilon, keep);
            var sorted = new List<int>(keep);
            sorted.Sort();
            return sorted;
        }

        static List<Vector2> RemoveCollinearAxisAlignedCellCenters(List<Vector2> pts)
        {
            if (pts == null || pts.Count < 3)
                return pts;
            var r = new List<Vector2>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector2 p0 = r[r.Count - 1];
                Vector2 p1 = pts[i];
                Vector2 p2 = pts[i + 1];
                bool col =
                    (Mathf.Abs(p0.x - p1.x) < 1e-4f && Mathf.Abs(p1.x - p2.x) < 1e-4f) ||
                    (Mathf.Abs(p0.y - p1.y) < 1e-4f && Mathf.Abs(p1.y - p2.y) < 1e-4f);
                if (!col)
                    r.Add(p1);
            }

            r.Add(pts[pts.Count - 1]);
            return r;
        }

        static Vector2 CatmullRomSegment(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f *
                (2f * p1 +
                    (-p0 + p2) * t +
                    (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                    (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        static List<Vector2> ResampleUniformAlongArcLength(List<Vector2> src, int targetCount, int w, int h)
        {
            if (src == null || src.Count < 2 || targetCount < 2)
                return src;
            if (src.Count <= targetCount)
                return src;
            var cum = new float[src.Count];
            cum[0] = 0f;
            for (int i = 1; i < src.Count; i++)
                cum[i] = cum[i - 1] + Vector2.Distance(src[i - 1], src[i]);
            float L = cum[src.Count - 1];
            if (L < 1e-5f)
                return src;
            float minX = 0.5f;
            float maxX = w - 0.5f;
            float minY = 0.5f;
            float maxY = h - 0.5f;
            var dst = new List<Vector2>(targetCount);
            for (int i = 0; i < targetCount; i++)
            {
                float tDist = (i / (float)Mathf.Max(1, targetCount - 1)) * L;
                int j = 0;
                while (j < cum.Length - 1 && cum[j + 1] < tDist)
                    j++;
                float segLen = Mathf.Max(1e-6f, cum[j + 1] - cum[j]);
                float u = (tDist - cum[j]) / segLen;
                Vector2 p = Vector2.Lerp(src[j], src[j + 1], Mathf.Clamp01(u));
                p.x = Mathf.Clamp(p.x, minX, maxX);
                p.y = Mathf.Clamp(p.y, minY, maxY);
                dst.Add(p);
            }

            return dst;
        }

        static List<Vector2> BuildCatmullRomOpenCenterline(List<Vector2> keys, int maxPoints, int w, int h)
        {
            int n = keys.Count;
            if (n < 2)
                return new List<Vector2>(keys);
            float minX = 0.5f;
            float maxX = w - 0.5f;
            float minY = 0.5f;
            float maxY = h - 0.5f;
            Vector2 pre = keys[0] + (keys[0] - keys[1]);
            Vector2 post = keys[n - 1] + (keys[n - 1] - keys[n - 2]);
            float totalLen = 0f;
            for (int i = 0; i < n - 1; i++)
                totalLen += Vector2.Distance(keys[i], keys[i + 1]);
            int cap = Mathf.Max(8, maxPoints);
            float spacing = Mathf.Max(0.14f, totalLen / Mathf.Max(2, cap - 1));

            var poly = new List<Vector2>(Mathf.Min(cap + 16, n * 4));
            void AddClamped(Vector2 p)
            {
                p.x = Mathf.Clamp(p.x, minX, maxX);
                p.y = Mathf.Clamp(p.y, minY, maxY);
                if (poly.Count == 0 || (poly[poly.Count - 1] - p).sqrMagnitude > 1e-8f)
                    poly.Add(p);
            }

            AddClamped(keys[0]);
            for (int seg = 0; seg < n - 1; seg++)
            {
                Vector2 p0 = seg == 0 ? pre : keys[seg - 1];
                Vector2 p1 = keys[seg];
                Vector2 p2 = keys[seg + 1];
                Vector2 p3 = seg + 2 < n ? keys[seg + 2] : post;
                float segLen = Vector2.Distance(p1, p2);
                int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / spacing));
                for (int s = 1; s <= steps; s++)
                {
                    if (poly.Count >= cap + 64)
                        break;
                    float t = s / (float)steps;
                    Vector2 q = CatmullRomSegment(p0, p1, p2, p3, Mathf.Clamp01(t));
                    AddClamped(q);
                }
            }

            AddClamped(keys[n - 1]);
            if (poly.Count > cap)
                poly = ResampleUniformAlongArcLength(poly, cap, w, h);
            return poly;
        }

        static float MaxConsecutiveSegmentLength(List<Vector2> poly)
        {
            if (poly == null || poly.Count < 2)
                return 0f;
            float m = 0f;
            for (int i = 1; i < poly.Count; i++)
                m = Mathf.Max(m, Vector2.Distance(poly[i - 1], poly[i]));
            return m;
        }

        /// <summary>Centerline visual del main; pathCells intactos para grid y vados.</summary>
        static List<Vector2> BuildMainVisualCenterline(List<Vector2Int> pathCells, int w, int h)
        {
            int pc = pathCells != null ? pathCells.Count : 0;
            int maxPts = Mathf.Max(8, Mathf.RoundToInt(pc * MainVisualCenterlineMaxPointsFactor));
            var dense = new List<Vector2>(pc);
            for (int i = 0; i < pc; i++)
            {
                var c = pathCells[i];
                dense.Add(new Vector2(c.x + 0.5f, c.y + 0.5f));
            }

            if (dense.Count < 2)
                return dense;

            var col = RemoveCollinearAxisAlignedCellCenters(dense);
            if (col.Count < 2)
                col = dense;
            var keepIdx = BuildRdpKeepIndices(col, MainVisualRdpEpsilonCells);
            var keys = new List<Vector2>(keepIdx.Count);
            for (int k = 0; k < keepIdx.Count; k++)
                keys.Add(col[keepIdx[k]]);
            if (keys.Count < 2)
            {
                keys.Clear();
                keys.Add(col[0]);
                keys.Add(col[col.Count - 1]);
            }

            return BuildCatmullRomOpenCenterline(keys, maxPts, w, h);
        }

        static List<Vector2Int> ConcatRiverSegments(List<Vector2Int> a, List<Vector2Int> b)
        {
            if (a == null || a.Count == 0)
                return b;
            if (b == null || b.Count == 0)
                return a;
            var r = new List<Vector2Int>(a.Count + b.Count);
            r.AddRange(a);
            for (int i = 1; i < b.Count; i++)
                r.Add(b[i]);
            return r;
        }

        static bool IsStrictInterior(int x, int y, int w, int h, int inner)
        {
            return x >= inner && y >= inner && x <= w - 1 - inner && y <= h - 1 - inner;
        }

        static bool SnapInteriorLandWaypoint(
            GridSystem grid,
            int w,
            int h,
            int inner,
            ref Vector2Int wp)
        {
            if (IsStrictInterior(wp.x, wp.y, w, h, inner) &&
                grid.GetCell(wp.x, wp.y).type == CellType.Land)
                return true;

            for (int r = 0; r <= 28; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r)
                            continue;
                        int nx = wp.x + dx;
                        int ny = wp.y + dy;
                        if (!IsStrictInterior(nx, ny, w, h, inner))
                            continue;
                        ref var c = ref grid.GetCell(nx, ny);
                        if (c.type == CellType.Land)
                        {
                            wp = new Vector2Int(nx, ny);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        static float MeanderFrac01(int riverSlot, int pairIndex, int riverAttempt, int salt)
        {
            uint u = unchecked((uint)(riverSlot * 2166136261 + pairIndex * 16777619 + riverAttempt * 50331653 + salt * 911382323));
            u ^= u >> 16;
            u *= 2246822519u;
            u ^= u >> 13;
            return (u & 0xFFFF) / 65535f;
        }

        static bool TryDeterministicOrganicSWaypointReshapePath(
            GridSystem grid,
            int w,
            int h,
            Vector2Int start,
            Vector2Int goal,
            int pairIndex,
            int minMain,
            int maxMain,
            int riverSlot,
            int riverAttempt,
            bool avoidCrossingCorridor,
            HashSet<long> occupiedRiverCells,
            long pairDeadline,
            int waypointCount,
            MapGenConfig routeShapeConfig,
            bool isMainRiver,
            out List<Vector2Int> path,
            out int expandedNodes,
            out float finalCost,
            out float sumNearRiverPen,
            out float sumHeightBias)
        {
            path = null;
            expandedNodes = 0;
            finalCost = 0f;
            sumNearRiverPen = 0f;
            sumHeightBias = 0f;
            if (MonotonicMs() >= pairDeadline)
                return false;

            waypointCount = Mathf.Clamp(waypointCount, 1, 2);
            int margin = BorderMargin(w, h);
            int inner = Mathf.Max(margin + 2, 4);

            Vector2 sa = new Vector2(start.x, start.y);
            Vector2 go = new Vector2(goal.x, goal.y);
            Vector2 delta = go - sa;
            float len = delta.magnitude;
            if (len < 10f)
                return false;

            Vector2 dir = delta / len;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            int mapSize = Mathf.Max(w, h);
            int offBase = Mathf.Clamp(Mathf.RoundToInt(mapSize * 0.11f), 18, 35);
            uint hMix = unchecked(
                (uint)(riverSlot * 2166136261 + pairIndex * 16777619 + riverAttempt * 50331653 + start.x * 911382323 + goal.y * 97266353));
            int sign = (hMix & 1u) != 0 ? 1 : -1;

            Vector2Int c1;
            Vector2Int c2 = default;
            if (waypointCount >= 2)
            {
                float t1 = 0.30f + MeanderFrac01(riverSlot, pairIndex, riverAttempt, 481) * 0.10f;
                float t2 = 0.60f + MeanderFrac01(riverSlot, pairIndex, riverAttempt, 482) * 0.10f;
                float fA = 0.88f + MeanderFrac01(riverSlot, pairIndex, riverAttempt, 483) * 0.24f;
                float fB = 0.88f + MeanderFrac01(riverSlot, pairIndex, riverAttempt, 484) * 0.24f;
                Vector2 p1 = Vector2.Lerp(sa, go, t1) + perp * (sign * offBase * fA);
                Vector2 p2 = Vector2.Lerp(sa, go, t2) + perp * (-sign * offBase * fB);
                c1 = new Vector2Int(Mathf.RoundToInt(p1.x), Mathf.RoundToInt(p1.y));
                c2 = new Vector2Int(Mathf.RoundToInt(p2.x), Mathf.RoundToInt(p2.y));
            }
            else
            {
                float tMid = 0.45f + MeanderFrac01(riverSlot, pairIndex, riverAttempt, 491) * 0.12f;
                float fA = 0.92f + MeanderFrac01(riverSlot, pairIndex, riverAttempt, 492) * 0.16f;
                Vector2 p1 = Vector2.Lerp(sa, go, tMid) + perp * (sign * offBase * fA);
                c1 = new Vector2Int(Mathf.RoundToInt(p1.x), Mathf.RoundToInt(p1.y));
            }

            if (!SnapInteriorLandWaypoint(grid, w, h, inner, ref c1) ||
                c1 == start || c1 == goal ||
                (occupiedRiverCells != null && occupiedRiverCells.Contains(Pack(c1.x, c1.y))))
                return false;

            if (waypointCount >= 2)
            {
                if (!SnapInteriorLandWaypoint(grid, w, h, inner, ref c2) ||
                    c2 == start || c2 == goal || c1 == c2 ||
                    (occupiedRiverCells != null && occupiedRiverCells.Contains(Pack(c2.x, c2.y))))
                    return false;
            }

            Vector2Int[] mids = waypointCount >= 2 ? new[] { c1, c2 } : new[] { c1 };

            return TryRunWaypointChain(
                grid,
                w,
                h,
                start,
                mids,
                goal,
                pairIndex + 7000,
                minMain,
                maxMain,
                riverSlot,
                avoidCrossingCorridor,
                occupiedRiverCells,
                pairDeadline,
                isMainRiver,
                routeShapeConfig,
                out path,
                out expandedNodes,
                out finalCost,
                out sumNearRiverPen,
                out sumHeightBias,
                out string _);
        }

        static bool TryBuildWaypointSegmentedMainPath(
            GridSystem grid,
            int w,
            int h,
            Vector2Int start,
            Vector2Int goal,
            int pairIndex,
            int minMain,
            int maxMain,
            int riverSlot,
            int riverAttempt,
            bool avoidCrossingCorridor,
            HashSet<long> occupiedRiverCells,
            long pairDeadlineEnvTick64,
            bool logRoute,
            bool isMainRiver,
            MapGenConfig routeConfig,
            out List<Vector2Int> path,
            out int expandedNodes,
            out float finalCost,
            out float sumNearRiverPen,
            out float sumHeightBias,
            out int wpCount,
            out Vector2Int wp1,
            out Vector2Int wp2,
            out string failReason)
        {
            path = null;
            expandedNodes = 0;
            finalCost = 0f;
            sumNearRiverPen = 0f;
            sumHeightBias = 0f;
            wpCount = 0;
            wp1 = default;
            wp2 = default;
            failReason = null;

            int margin = BorderMargin(w, h);
            int inner = Mathf.Max(margin + 2, 4);
            int adx = Mathf.Abs(goal.x - start.x);
            int ady = Mathf.Abs(goal.y - start.y);
            bool mostlyVertical = ady >= adx;
            int mapSize = Mathf.Max(w, h);
            int maxOffsetCells = Mathf.Min(Mathf.RoundToInt(mapSize * 0.18f), 42);
            int largeMin = routeConfig != null ? Mathf.Clamp(routeConfig.riverMainOrganicLargeMapMinCells, 96, 512) : 192;
            bool mapLarge = mapSize >= largeMin;
            int minLeg = Mathf.Min(adx, ady);
            int maxLeg = Mathf.Max(adx, ady);
            bool nearlyStraightMain =
                maxLeg > 0 && minLeg <= Mathf.Max(2, Mathf.RoundToInt(maxLeg * 0.06f));
            bool allowTwoWaypoints =
                mapLarge &&
                (nearlyStraightMain ||
                    (isMainRiver && routeConfig != null && routeConfig.riverMainForceOrganicReshape));

            int prevBadLen = 0;
            string prevBadReason = null;

            for (int variant = 0; variant < 3; variant++)
            {
                if (MonotonicMs() >= pairDeadlineEnvTick64)
                {
                    failReason = "astar_time_budget";
                    return false;
                }

                float offsetScale = variant == 0 ? 1f : variant == 1 ? 0.5f : 0.28f;
                bool useTwoWp = allowTwoWaypoints && variant == 0;

                int lo = Mathf.Clamp(20, 6, maxOffsetCells);
                int hi = Mathf.Clamp(35, lo, maxOffsetCells);
                float fU = MeanderFrac01(riverSlot, pairIndex, riverAttempt, variant * 31 + 5);
                int baseOffset = Mathf.RoundToInt((lo + fU * (hi - lo)) * offsetScale);
                baseOffset = Mathf.Clamp(baseOffset, 6, maxOffsetCells);

                int sign1 = (variant + pairIndex) % 2 == 0 ? 1 : -1;
                int sign2 = -sign1;

                Vector2Int[] mids;
                if (useTwoWp)
                {
                    float t1 = 0.33f;
                    float t2 = 0.66f;
                    float fx1 = Mathf.Lerp(start.x, goal.x, t1);
                    float fy1 = Mathf.Lerp(start.y, goal.y, t1);
                    float fx2 = Mathf.Lerp(start.x, goal.x, t2);
                    float fy2 = Mathf.Lerp(start.y, goal.y, t2);
                    int off2 = Mathf.Clamp(
                        Mathf.RoundToInt(baseOffset * (0.85f + MeanderFrac01(riverSlot, pairIndex, riverAttempt, 91) * 0.25f)),
                        6,
                        maxOffsetCells);
                    Vector2Int c1;
                    Vector2Int c2;
                    if (mostlyVertical)
                    {
                        c1 = new Vector2Int(Mathf.RoundToInt(fx1 + sign1 * baseOffset), Mathf.RoundToInt(fy1));
                        c2 = new Vector2Int(Mathf.RoundToInt(fx2 + sign2 * off2), Mathf.RoundToInt(fy2));
                    }
                    else
                    {
                        c1 = new Vector2Int(Mathf.RoundToInt(fx1), Mathf.RoundToInt(fy1 + sign1 * baseOffset));
                        c2 = new Vector2Int(Mathf.RoundToInt(fx2), Mathf.RoundToInt(fy2 + sign2 * off2));
                    }

                    if (!SnapInteriorLandWaypoint(grid, w, h, inner, ref c1) ||
                        !SnapInteriorLandWaypoint(grid, w, h, inner, ref c2) ||
                        c1 == start || c1 == goal || c2 == start || c2 == goal || c1 == c2)
                    {
                        failReason = "waypoint_snap_fail";
                        continue;
                    }

                    mids = new[] { c1, c2 };
                    wp1 = c1;
                    wp2 = c2;
                }
                else
                {
                    float tMid = 0.40f + MeanderFrac01(riverSlot, pairIndex, riverAttempt, variant * 7 + 3) * 0.20f;
                    float fx = Mathf.Lerp(start.x, goal.x, tMid);
                    float fy = Mathf.Lerp(start.y, goal.y, tMid);
                    Vector2Int c = mostlyVertical
                        ? new Vector2Int(Mathf.RoundToInt(fx + sign1 * baseOffset), Mathf.RoundToInt(fy))
                        : new Vector2Int(Mathf.RoundToInt(fx), Mathf.RoundToInt(fy + sign1 * baseOffset));
                    if (!SnapInteriorLandWaypoint(grid, w, h, inner, ref c) || c == start || c == goal)
                    {
                        failReason = "waypoint_snap_fail";
                        continue;
                    }

                    mids = new[] { c };
                    wp1 = c;
                    wp2 = default;
                }

                if (!TryRunWaypointChain(
                        grid,
                        w,
                        h,
                        start,
                        mids,
                        goal,
                        pairIndex,
                        minMain,
                        maxMain,
                        riverSlot,
                        avoidCrossingCorridor,
                        occupiedRiverCells,
                        pairDeadlineEnvTick64,
                        isMainRiver,
                        routeConfig,
                        out List<Vector2Int> merged,
                        out int expSum,
                        out float fcSum,
                        out float snpSum,
                        out float shbSum,
                        out string segFail))
                {
                    failReason = segFail;
                    continue;
                }

                bool hadWp = mids.Length > 0;
                if (!ValidateMainPathQuick(
                        w,
                        h,
                        start,
                        goal,
                        merged,
                        minMain,
                        maxMain,
                        hadWaypoints: hadWp,
                        out string quickBad))
                {
                    if (logRoute)
                        LogRiverRouteMeanderRetry(riverSlot, quickBad, variant, merged.Count, 0);

                    prevBadLen = merged.Count;
                    prevBadReason = quickBad;
                    failReason = quickBad;
                    continue;
                }

                if (logRoute && prevBadLen > 0 && !string.IsNullOrEmpty(prevBadReason))
                    LogRiverRouteMeanderRetry(riverSlot, prevBadReason, variant, prevBadLen, merged.Count);

                path = merged;
                expandedNodes = expSum;
                finalCost = fcSum;
                sumNearRiverPen = snpSum;
                sumHeightBias = shbSum;
                wpCount = mids.Length;
                return true;
            }

            failReason ??= "waypoint_segments_failed";
            return false;
        }

        static bool TryRunWaypointChain(
            GridSystem grid,
            int w,
            int h,
            Vector2Int start,
            Vector2Int[] mids,
            Vector2Int goal,
            int pairIndex,
            int minMain,
            int maxMain,
            int riverSlot,
            bool avoidCrossingCorridor,
            HashSet<long> occupiedRiverCells,
            long pairDeadlineEnvTick64,
            bool isMainRiver,
            MapGenConfig routeShapeConfig,
            out List<Vector2Int> path,
            out int expandedNodes,
            out float finalCost,
            out float sumNearRiverPen,
            out float sumHeightBias,
            out string failReason)
        {
            path = null;
            expandedNodes = 0;
            finalCost = 0f;
            sumNearRiverPen = 0f;
            sumHeightBias = 0f;
            failReason = null;

            if (mids == null || mids.Length == 0 || mids.Length > 2)
            {
                failReason = "bad_waypoints";
                return false;
            }

            var chain = new List<Vector2Int>(mids.Length + 2) { start };
            chain.AddRange(mids);
            chain.Add(goal);

            int expSum = 0;
            float fcSum = 0f;
            float snpSum = 0f;
            float shbSum = 0f;
            List<Vector2Int> acc = null;

            for (int s = 0; s < chain.Count - 1; s++)
            {
                long now = MonotonicMs();
                long rem = pairDeadlineEnvTick64 - now;
                if (rem <= 0)
                {
                    failReason = "astar_time_budget";
                    return false;
                }

                int segsLeft = chain.Count - 1 - s;
                long segEnd = now + Math.Max(8L, rem / Mathf.Max(1, segsLeft));

                bool ok = RunAStarSingleGoal(
                    grid,
                    w,
                    h,
                    chain[s],
                    chain[s + 1],
                    mergeMode: false,
                    occupiedRiverCells,
                    avoidCrossingCorridor,
                    null,
                    riverSlot,
                    pairIndex * 10 + s + 1,
                    MainAStarExpansionCap,
                    strongStraightPenalty: true,
                    segEnd,
                    routeShapeConfig,
                    isMainRiver && routeShapeConfig != null,
                    out int es,
                    out float fs,
                    out float sns,
                    out float shs,
                    out List<Vector2Int> ps,
                    out string rs);

                if (!ok || ps == null)
                {
                    failReason = $"seg_{s}:" + (rs ?? "no_path");
                    return false;
                }

                expSum += es;
                fcSum += fs;
                snpSum += sns;
                shbSum += shs;
                acc = acc == null ? ps : ConcatRiverSegments(acc, ps);
            }

            if (acc == null || acc.Count < minMain || acc.Count > maxMain)
            {
                failReason = acc == null ? "merge_null" : "merged_length_band";
                return false;
            }

            path = acc;
            expandedNodes = expSum;
            finalCost = fcSum;
            sumNearRiverPen = snpSum;
            sumHeightBias = shbSum;
            return true;
        }

        static bool TryBuildMainBorderRoute(
            GridSystem grid,
            int w,
            int h,
            MapGenConfig config,
            IRng rng,
            int riverSlot,
            int riverAttempt,
            bool avoidCrossingCorridor,
            HashSet<long> occupiedRiverCells,
            bool logRoute,
            bool isMainRiver,
            out int expandedNodes,
            out float finalCost,
            out float sumNearRiverPen,
            out float sumHeightBias,
            out List<Vector2Int> path,
            out Vector2Int logStart,
            out Vector2Int logGoal,
            out string rejectReason)
        {
            return TryBuildMainRiverAnchoredOrLegacy(
                grid,
                w,
                h,
                config,
                rng,
                riverSlot,
                riverAttempt,
                avoidCrossingCorridor,
                occupiedRiverCells,
                logRoute,
                isMainRiver,
                out expandedNodes,
                out finalCost,
                out sumNearRiverPen,
                out sumHeightBias,
                out path,
                out logStart,
                out logGoal,
                out rejectReason);
        }

        static int OppositeEdge(int e)
        {
            switch (e)
            {
                case 0:
                    return 1;
                case 1:
                    return 0;
                case 2:
                    return 3;
                default:
                    return 2;
            }
        }

        /// <summary>Borde: 0=N (y=0), 1=S (y=h-1), 2=W (x=0), 3=E (x=w-1).</summary>
        static bool TryPickBorderLandAnchor(
            GridSystem grid,
            int w,
            int h,
            IRng rng,
            int edge,
            int cornerExcluded,
            int edgeInset,
            bool preferMidHighHeight,
            out Vector2Int cell)
        {
            cell = default;
            int xLo = cornerExcluded;
            int xHi = w - 1 - cornerExcluded;
            int zLo = cornerExcluded;
            int zHi = h - 1 - cornerExcluded;
            if (xHi <= xLo) { xLo = 1; xHi = w - 2; }
            if (zHi <= zLo) { zLo = 1; zHi = h - 2; }

            for (int t = 0; t < 28; t++)
            {
                int x = 0, y = 0;
                switch (edge)
                {
                    case 0:
                        x = rng.NextInt(xLo, xHi + 1);
                        y = edgeInset;
                        break;
                    case 1:
                        x = rng.NextInt(xLo, xHi + 1);
                        y = h - 1 - edgeInset;
                        break;
                    case 2:
                        x = edgeInset;
                        y = rng.NextInt(zLo, zHi + 1);
                        break;
                    default:
                        x = w - 1 - edgeInset;
                        y = rng.NextInt(zLo, zHi + 1);
                        break;
                }

                if (!grid.InBoundsCell(x, y))
                    continue;
                ref var c = ref grid.GetCell(x, y);
                if (c.type != CellType.Land)
                    continue;
                if (preferMidHighHeight && (c.height01 < 0.22f || c.height01 > 0.95f))
                {
                    if (rng.NextFloat() > 0.2f)
                        continue;
                }

                cell = new Vector2Int(x, y);
                return true;
            }

            return false;
        }

        static bool TryBuildTributaryRoute(
            GridSystem grid,
            int w,
            int h,
            IRng rng,
            int riverSlot,
            int riverAttempt,
            bool avoidCrossingCorridor,
            HashSet<long> occupiedRiverCells,
            bool logRoute,
            out int expandedNodes,
            out float finalCost,
            out float sumNearRiverPen,
            out float sumHeightBias,
            out List<Vector2Int> path,
            out Vector2Int logStart,
            out bool touchesRiver,
            out string rejectReason)
        {
            expandedNodes = 0;
            finalCost = 0f;
            sumNearRiverPen = 0f;
            sumHeightBias = 0f;
            path = null;
            logStart = default;
            touchesRiver = false;
            rejectReason = null;

            var sw = Stopwatch.StartNew();
            var goalRiverCells = new HashSet<long>();
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (grid.GetCell(x, z).type != CellType.River)
                        continue;
                    goalRiverCells.Add(Pack(x, z));
                }
            }

            if (goalRiverCells.Count == 0)
            {
                rejectReason = "tributary_no_existing_river";
                return false;
            }

            int expTotal = 0;
            string lastRs = null;

            for (int attempt = 0; attempt < TributaryMaxAttempts; attempt++)
            {
                if (sw.ElapsedMilliseconds > TributaryTotalBudgetMs)
                {
                    rejectReason = "tributary_budget_timeout";
                    if (logRoute)
                        LogRiverRouteBudgetAbort(
                            riverSlot,
                            merge: 1,
                            attempt,
                            expTotal,
                            sw.Elapsed.TotalMilliseconds,
                            "tributary_total_ms");
                    path = null;
                    return false;
                }

                if (!TryPickTributaryStart(grid, w, h, rng, riverAttempt * 17 + attempt, out Vector2Int start))
                    continue;

                if (avoidCrossingCorridor && occupiedRiverCells != null &&
                    occupiedRiverCells.Count > 0 &&
                    occupiedRiverCells.Contains(Pack(start.x, start.y)))
                    continue;

                long msLeft = TributaryTotalBudgetMs - sw.ElapsedMilliseconds;
                if (msLeft <= 0)
                {
                    rejectReason = "tributary_budget_timeout";
                    if (logRoute)
                        LogRiverRouteBudgetAbort(
                            riverSlot,
                            merge: 1,
                            attempt,
                            expTotal,
                            sw.Elapsed.TotalMilliseconds,
                            "tributary_total_ms");
                    path = null;
                    return false;
                }

                long segDeadline = MonotonicMs() + Math.Max(6L, msLeft);

                if (!RunAStarSingleGoal(
                        grid,
                        w,
                        h,
                        start,
                        goal: default,
                        mergeMode: true,
                        occupiedRiverCells,
                        avoidCrossingCorridor,
                        goalRiverCells,
                        riverSlot,
                        attempt,
                        TributaryAStarExpansionCap,
                        strongStraightPenalty: false,
                        segDeadline,
                        null,
                        false,
                        out int expThis,
                        out finalCost,
                        out sumNearRiverPen,
                        out sumHeightBias,
                        out path,
                        out lastRs))
                {
                    expTotal += expThis;
                    if (lastRs == "astar_time_budget" || lastRs == "astar_node_cap")
                    {
                        rejectReason = "tributary_budget_timeout";
                        if (logRoute)
                            LogRiverRouteBudgetAbort(
                                riverSlot,
                                merge: 1,
                                attempt,
                                expTotal,
                                sw.Elapsed.TotalMilliseconds,
                                lastRs ?? "astar_budget");
                        path = null;
                        return false;
                    }

                    continue;
                }

                expandedNodes = expThis;
                expTotal += expThis;

                if (sw.ElapsedMilliseconds > TributaryTotalBudgetMs)
                {
                    rejectReason = "tributary_budget_timeout";
                    if (logRoute)
                        LogRiverRouteBudgetAbort(
                            riverSlot,
                            merge: 1,
                            attempt,
                            expTotal,
                            sw.Elapsed.TotalMilliseconds,
                            "tributary_total_ms_post_astar");
                    path = null;
                    return false;
                }

                int tMin = TributaryPathMinCells(w, h);
                int tMax = TributaryPathMaxCells(w, h);
                int triVisMin = TributaryVisualMinCells(w, h);
                if (path == null || path.Count < tMin || path.Count > tMax)
                {
                    path = null;
                    continue;
                }

                if (path.Count < triVisMin)
                {
                    if (logRoute)
                        LogRiverRouteTributaryReject(riverSlot, path.Count, "tributary_too_short_visual");
                    path = null;
                    rejectReason = "tributary_too_short_visual";
                    continue;
                }

                var end = path[path.Count - 1];
                if (grid.GetCell(end.x, end.y).type != CellType.River)
                {
                    path = null;
                    continue;
                }

                if (!PassesTributaryCleanliness(grid, w, h, path, occupiedRiverCells))
                {
                    path = null;
                    rejectReason = "tributary_not_clean";
                    continue;
                }

                logStart = start;
                touchesRiver = true;
                expandedNodes = expTotal;
                rejectReason = null;
                return true;
            }

            rejectReason = "tributary_exhausted";
            path = null;
            return false;
        }

        /// <summary>Evita carreras largas paralelas al cauce ocupado y “blob” local.</summary>
        static bool PassesTributaryCleanliness(
            GridSystem grid,
            int w,
            int h,
            List<Vector2Int> path,
            HashSet<long> occupiedRiverCells)
        {
            if (path == null || path.Count < 3)
                return true;

            int parallelRun = 0;
            int maxParallel = 0;
            for (int i = 0; i < path.Count - 1; i++)
            {
                var c = path[i];
                if (grid.GetCell(c.x, c.y).type == CellType.River)
                    break;

                bool alongside = false;
                if (occupiedRiverCells != null)
                {
                    foreach (var n in grid.Neighbors4(c))
                    {
                        long pk = Pack(n.x, n.y);
                        if (occupiedRiverCells.Contains(pk))
                        {
                            alongside = true;
                            break;
                        }
                    }
                }

                if (alongside)
                {
                    parallelRun++;
                    if (parallelRun > maxParallel)
                        maxParallel = parallelRun;
                }
                else
                    parallelRun = 0;
            }

            if (maxParallel > 10)
                return false;

            return true;
        }

        static bool TryPickTributaryStart(GridSystem grid, int w, int h, IRng rng, int attempt, out Vector2Int start)
        {
            start = default;
            bool preferBorder = (attempt & 1) == 0;
            for (int k = 0; k < 12; k++)
            {
                int x, y;
                if (preferBorder && k < 8)
                {
                    int edge = rng.NextInt(0, 4);
                    if (!TryPickBorderLandAnchor(grid, w, h, rng, edge, Mathf.Max(4, Mathf.Min(w, h) / 20), Mathf.Max(6, Mathf.Min(w, h) / 24), true, out Vector2Int b))
                        continue;
                    x = b.x;
                    y = b.y;
                }
                else
                {
                    x = rng.NextInt(2, w - 2);
                    y = rng.NextInt(2, h - 2);
                }

                ref var c = ref grid.GetCell(x, y);
                if (c.type != CellType.Land)
                    continue;
                if (c.height01 < 0.28f && rng.NextFloat() > 0.15f)
                    continue;

                bool nearRv = false;
                const int r = 16;
                for (int dz = -r; dz <= r && !nearRv; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx * dx + dz * dz > r * r)
                            continue;
                        int nx = x + dx;
                        int nz = y + dz;
                        if (!grid.InBoundsCell(nx, nz))
                            continue;
                        if (grid.GetCell(nx, nz).type == CellType.River)
                        {
                            nearRv = true;
                            break;
                        }
                    }
                }

                if (!nearRv)
                    continue;

                start = new Vector2Int(x, y);
                return true;
            }

            return false;
        }

        static bool RunAStarSingleGoal(
            GridSystem grid,
            int w,
            int h,
            Vector2Int start,
            Vector2Int goal,
            bool mergeMode,
            HashSet<long> occupiedRiverCells,
            bool avoidCrossingCorridor,
            HashSet<long> goalRiverCells,
            int riverSlot,
            int attemptSalt,
            int maxAStarExpansions,
            bool strongStraightPenalty,
            long deadlineEnvTick64,
            MapGenConfig riverStraightCostConfig,
            bool applyMainRiverStraightRunAccumCost,
            out int expandedNodes,
            out float finalG,
            out float sumNearRiverPen,
            out float sumHeightBias,
            out List<Vector2Int> outPath,
            out string rejectReason)
        {
            expandedNodes = 0;
            finalG = 0f;
            sumNearRiverPen = 0f;
            sumHeightBias = 0f;
            outPath = null;
            rejectReason = null;

            bool useZeroHeuristic = mergeMode && goalRiverCells != null && goalRiverCells.Count > 900;

            long startKey = Pack(start.x, start.y);
            var gScore = new Dictionary<long, float>(1024);
            var cameFrom = new Dictionary<long, long>(1024);
            gScore[startKey] = 0f;

            Dictionary<long, int> straightRunByNode = null;
            if (applyMainRiverStraightRunAccumCost && riverStraightCostConfig != null && !mergeMode)
            {
                straightRunByNode = new Dictionary<long, int>(1024);
                straightRunByNode[startKey] = 0;
            }

            float startH = mergeMode
                ? HeuristicMultiGoal(grid, w, h, start.x, start.y, goalRiverCells, useZeroHeuristic)
                : HeuristicSingle(grid, start.x, start.y, goal.x, goal.y);

            var open = new List<(float f, long k)>(256);
            open.Add((startH, startKey));

            long goalKeyFound = -1;

            int cap = Mathf.Clamp(maxAStarExpansions, 1000, MaxAStarExpansionsGlobal);

            while (open.Count > 0 && expandedNodes < cap)
            {
                if (deadlineEnvTick64 != 0 && MonotonicMs() >= deadlineEnvTick64)
                {
                    rejectReason = "astar_time_budget";
                    return false;
                }

                int bi = 0;
                float bf = open[0].f;
                for (int i = 1; i < open.Count; i++)
                {
                    if (open[i].f < bf)
                    {
                        bf = open[i].f;
                        bi = i;
                    }
                }

                var cur = open[bi];
                open.RemoveAt(bi);
                long curKey = cur.k;
                Unpack(curKey, out int cx, out int cy);

                if (!gScore.TryGetValue(curKey, out float gCurOpen))
                    continue;

                float hOpen = mergeMode
                    ? HeuristicMultiGoal(grid, w, h, cx, cy, goalRiverCells, useZeroHeuristic)
                    : HeuristicSingle(grid, cx, cy, goal.x, goal.y);
                if (cur.f > gCurOpen + hOpen + 0.12f)
                    continue;

                if (mergeMode)
                {
                    if (goalRiverCells != null && goalRiverCells.Contains(curKey))
                    {
                        goalKeyFound = curKey;
                        break;
                    }
                }
                else
                {
                    if (cx == goal.x && cy == goal.y)
                    {
                        goalKeyFound = curKey;
                        break;
                    }
                }

                expandedNodes++;
                float gCur = gCurOpen;

                bool haveParent = cameFrom.TryGetValue(curKey, out long parentKey) && parentKey != curKey;
                int pdx = 0, pdy = 0;
                if (haveParent)
                {
                    Unpack(parentKey, out int px, out int py);
                    pdx = cx - px;
                    pdy = cy - py;
                }

                foreach (var nb in grid.Neighbors4(cx, cy))
                {
                    int nx = nb.x;
                    int ny = nb.y;
                    long nk = Pack(nx, ny);
                    ref var nc = ref grid.GetCell(nx, ny);

                    if (!mergeMode)
                    {
                        if (nc.type != CellType.Land)
                            continue;
                    }
                    else
                    {
                        bool onGoalRiver = goalRiverCells != null && goalRiverCells.Contains(nk);
                        if (nc.type == CellType.River && !onGoalRiver)
                            continue;
                        if (!(nc.type == CellType.Land || nc.type == CellType.River))
                            continue;
                    }

                    if (nc.type == CellType.Mountain || nc.type == CellType.Water)
                        continue;

                    if (avoidCrossingCorridor && occupiedRiverCells != null &&
                        occupiedRiverCells.Count > 0 &&
                        occupiedRiverCells.Contains(nk))
                        continue;

                    float step = EdgeCost(
                        grid,
                        w,
                        h,
                        cx,
                        cy,
                        nx,
                        ny,
                        haveParent ? pdx : 0,
                        haveParent ? pdy : 0,
                        riverSlot,
                        attemptSalt,
                        occupiedRiverCells,
                        strongStraightPenalty,
                        out float penRiver,
                        out float penH);

                    if (straightRunByNode != null)
                    {
                        int ndx = nx - cx;
                        int ndy = ny - cy;
                        int childRun = 1;
                        if (haveParent && ndx == pdx && ndy == pdy &&
                            straightRunByNode.TryGetValue(curKey, out int prun))
                            childRun = prun + 1;

                        int startN = Mathf.Max(3, riverStraightCostConfig.riverMainStraightRunCostStartCells);
                        float mul = Mathf.Max(0f, riverStraightCostConfig.riverMainStraightRunCostMul);
                        if (childRun > startN)
                        {
                            int toGoal = Mathf.Abs(nx - goal.x) + Mathf.Abs(ny - goal.y);
                            float exitGate = toGoal <= 10 ? 0.28f : 1f;
                            step += (childRun - startN) * mul * exitGate;
                        }

                        float tentG2 = gCur + step;
                        if (!gScore.TryGetValue(nk, out float oldG) || tentG2 < oldG - 1e-6f)
                        {
                            gScore[nk] = tentG2;
                            cameFrom[nk] = curKey;
                            straightRunByNode[nk] = childRun;
                            float hCost = mergeMode
                                ? HeuristicMultiGoal(grid, w, h, nx, ny, goalRiverCells, useZeroHeuristic)
                                : HeuristicSingle(grid, nx, ny, goal.x, goal.y);
                            open.Add((tentG2 + hCost, nk));
                        }
                    }
                    else
                    {
                        float tentG = gCur + step;
                        if (!gScore.TryGetValue(nk, out float oldG) || tentG < oldG - 1e-6f)
                        {
                            gScore[nk] = tentG;
                            cameFrom[nk] = curKey;
                            float hCost = mergeMode
                                ? HeuristicMultiGoal(grid, w, h, nx, ny, goalRiverCells, useZeroHeuristic)
                                : HeuristicSingle(grid, nx, ny, goal.x, goal.y);
                            open.Add((tentG + hCost, nk));
                        }
                    }
                }
            }

            if (goalKeyFound < 0)
            {
                if (deadlineEnvTick64 != 0 && MonotonicMs() >= deadlineEnvTick64)
                    rejectReason = "astar_time_budget";
                else if (expandedNodes >= cap)
                    rejectReason = "astar_node_cap";
                else
                    rejectReason = mergeMode ? "astar_no_merge_target" : "no_path";
                return false;
            }

            finalG = gScore.TryGetValue(goalKeyFound, out float fg) ? fg : 0f;

            outPath = Reconstruct(cameFrom, goalKeyFound, startKey);
            if (outPath == null || outPath.Count < 2)
            {
                rejectReason = "reconstruct_fail";
                return false;
            }

            return true;
        }

        static List<Vector2Int> Reconstruct(Dictionary<long, long> cameFrom, long goalKey, long startKey)
        {
            var path = new List<Vector2Int>();
            long k = goalKey;
            int guard = 0;
            while (guard++ < 65536)
            {
                Unpack(k, out int x, out int y);
                path.Add(new Vector2Int(x, y));
                if (k == startKey)
                    break;
                if (!cameFrom.TryGetValue(k, out long pk))
                    return null;
                if (pk == k)
                    return null;
                k = pk;
            }

            path.Reverse();
            return path;
        }

        static float HeuristicSingle(GridSystem grid, int x, int y, int gx, int gy)
        {
            return Mathf.Abs(x - gx) + Mathf.Abs(y - gy);
        }

        static float HeuristicMultiGoal(
            GridSystem grid,
            int w,
            int h,
            int x,
            int y,
            HashSet<long> goalRiverCells,
            bool useZeroHeuristic)
        {
            if (useZeroHeuristic)
                return 0f;
            if (goalRiverCells == null || goalRiverCells.Count == 0)
                return 0f;
            int best = int.MaxValue;
            int scanned = 0;
            foreach (var pk in goalRiverCells)
            {
                if (++scanned > 900)
                    break;
                Unpack(pk, out int rx, out int rz);
                int d = Mathf.Abs(x - rx) + Mathf.Abs(y - rz);
                if (d < best)
                    best = d;
                if (best <= 1)
                    return best;
            }

            return best == int.MaxValue ? 0f : best;
        }

        static float EdgeCost(
            GridSystem grid,
            int w,
            int h,
            int x0,
            int y0,
            int x1,
            int y1,
            int pdx,
            int pdy,
            int riverSlot,
            int salt,
            HashSet<long> occupiedRiverCells,
            bool strongStraightPenalty,
            out float nearRiverPenalty,
            out float heightBiasComponent)
        {
            nearRiverPenalty = 0f;
            heightBiasComponent = 0f;

            if (x0 == x1 && y0 == y1)
            {
                int dPk = MinChebyshevDistToPacked(occupiedRiverCells, x1, y1, 7);
                if (dPk <= 2)
                    nearRiverPenalty = (3 - dPk) * 0.55f;
                float h1 = grid.GetCell(x1, y1).height01;
                heightBiasComponent = h1 * 0.22f;
                return 0f;
            }

            float baseCost = 1f;
            uint saltmix = unchecked((uint)(riverSlot * 1315423911 + salt * 374761393));
            float n01 = Hash01(x1, y1, saltmix);
            baseCost += (n01 - 0.5f) * 0.38f;

            float h01 = grid.GetCell(x1, y1).height01;
            heightBiasComponent = h01 * 0.22f;
            baseCost += h01 * 0.18f;

            int edgeDist = Mathf.Min(Mathf.Min(x1, w - 1 - x1), Mathf.Min(y1, h - 1 - y1));
            if (edgeDist <= 5)
                baseCost += (6 - edgeDist) * 0.16f;

            int dPack = MinChebyshevDistToPacked(occupiedRiverCells, x1, y1, 8);
            if (dPack <= 3)
            {
                float pen = (4 - dPack) * 0.85f;
                baseCost += pen;
                nearRiverPenalty = pen;
            }

            int cdx = x1 - x0;
            int cdy = y1 - y0;
            if (pdx != 0 || pdy != 0)
            {
                if (cdx == pdx && cdy == pdy)
                {
                    float straightPen = strongStraightPenalty ? 0.48f : 0.30f;
                    baseCost += straightPen;
                }
            }

            return baseCost;
        }

        static float Hash01(int x, int y, uint salt)
        {
            uint h = salt ^ (uint)(x * 73856093) ^ (uint)(y * 19349663);
            h *= 2246822519u;
            h ^= h >> 13;
            h *= 3266489917u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }

        static int MinChebyshevDistToPacked(HashSet<long> occ, int x, int y, int maxD)
        {
            if (occ == null || occ.Count == 0)
                return maxD + 1;
            for (int d = 0; d <= maxD; d++)
            {
                for (int dx = -d; dx <= d; dx++)
                {
                    for (int dy = -d; dy <= d; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != d)
                            continue;
                        long pk = Pack(x + dx, y + dy);
                        if (occ.Contains(pk))
                            return d;
                    }
                }
            }

            return maxD + 1;
        }
    }
}
