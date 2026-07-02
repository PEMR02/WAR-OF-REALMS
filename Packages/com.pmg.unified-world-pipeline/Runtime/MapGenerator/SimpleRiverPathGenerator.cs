using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Río RTS: descenso por height01, sin Catmull/Laplacian/enforce ni V2.</summary>
    public static class SimpleRiverPathGenerator
    {
        public const int MaxRiverPoints = 192;

        const int PathRecentTabuSteps = 8;
        // Epsilon reducido: meseta estricta → el río prefiere descender en lugar de vagar lateral.
        const float PlateauUpEpsilon = 0.0012f;
        const int BacktrackPopMin = 4;
        const int BacktrackPopMaxExclusive = 9;
        const int MaxBacktrackEvents = 64;
        const int MainLoopSafetyCap = 800;

        static long Pack(Vector2Int c) => ((long)c.x << 32) | (uint)c.y;

        static bool s_heightSummaryLogged;

        /// <summary>Una vez por generación si hay flags de debug: rango height01 y pendiente media en Land.</summary>
        public static void TryLogHeightmapSummaryOnce(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null || s_heightSummaryLogged)
                return;
            if (!config.debugHydrologyNetwork && !config.debugRiverHydrologyPerf && !config.debugLogs)
                return;

            s_heightSummaryLogged = true;
            int gw = grid.Width;
            int gh = grid.Height;
            float minH = 1f, maxH = 0f;
            int land = 0;
            double slopeSum = 0.0;
            int slopeSamples = 0;
            for (int z = 0; z < gh; z++)
            {
                for (int x = 0; x < gw; x++)
                {
                    ref var c = ref grid.GetCell(x, z);
                    if (c.type != CellType.Land)
                        continue;
                    float h = c.height01;
                    land++;
                    if (h < minH) minH = h;
                    if (h > maxH) maxH = h;
                    float bestD = 0f;
                    foreach (var n in grid.Neighbors4(new Vector2Int(x, z)))
                    {
                        if (!grid.InBoundsCell(n.x, n.y))
                            continue;
                        ref var nc = ref grid.GetCell(n.x, n.y);
                        if (nc.type != CellType.Land)
                            continue;
                        float d = Mathf.Abs(nc.height01 - h);
                        if (d > bestD)
                            bestD = d;
                    }

                    slopeSum += bestD;
                    slopeSamples++;
                }
            }

            float avgSlope = slopeSamples > 0 ? (float)(slopeSum / slopeSamples) : 0f;
            Debug.Log(
                $"[RiverHeightField] landCells={land} minHeight01={minH:F4} maxHeight01={maxH:F4} avgNeighborSlope01={avgSlope:F5}");
        }

        public static void ResetHeightSummaryLog() => s_heightSummaryLogged = false;

        public static bool TryGenerateDownhillRiver(
            GridSystem grid,
            int w,
            int h,
            Vector2Int start,
            bool mergeIntoExistingRiver,
            bool avoidCrossingCorridor,
            HashSet<long> occupiedCorridorPacked,
            MapGenConfig config,
            IRng rng,
            int riverSlot,
            int riverAttempt,
            out List<Vector2> centerlineCellSpace,
            out List<Vector2Int> rasterPath,
            out List<Vector2Int> fordCells,
            out List<Vector2> debugMacro,
            out List<Vector2> debugSmoothed,
            out string failReason)
        {
            centerlineCellSpace = null;
            rasterPath = null;
            fordCells = null;
            debugMacro = null;
            debugSmoothed = null;
            failReason = null;

            bool dbg = config != null && (config.debugHydrologyNetwork || config.debugRiverHydrologyPerf || config.debugLogs);

            if (grid == null || config == null || rng == null)
            {
                failReason = "null_args";
                if (dbg)
                    LogRiverDownhillFail(failReason, start, 0, 0f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, riverSlot, riverAttempt, mergeIntoExistingRiver);
                return false;
            }

            if (!grid.InBoundsCell(start.x, start.y))
            {
                failReason = "bad_start_bounds";
                if (dbg)
                    LogRiverDownhillFail(failReason, start, 0, 0f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, riverSlot, riverAttempt, mergeIntoExistingRiver);
                return false;
            }

            ref var sc0 = ref grid.GetCell(start.x, start.y);
            if (sc0.type != CellType.Land)
            {
                failReason = "bad_start_type";
                if (dbg)
                    LogRiverDownhillFail(
                        failReason,
                        start,
                        0,
                        sc0.height01,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        riverSlot,
                        riverAttempt,
                        mergeIntoExistingRiver);
                return false;
            }

            if (avoidCrossingCorridor &&
                occupiedCorridorPacked != null &&
                occupiedCorridorPacked.Count > 0 &&
                occupiedCorridorPacked.Contains(Pack(start)))
            {
                failReason = "start_in_corridor";
                if (dbg)
                    LogRiverDownhillFail(
                        failReason,
                        start,
                        0,
                        sc0.height01,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        riverSlot,
                        riverAttempt,
                        mergeIntoExistingRiver);
                return false;
            }

            if (dbg)
            {
                int dRiver = MinChebyshevDistToRiver4(grid, w, h, start.x, start.y);
                Debug.Log(
                    $"[RiverStart] slot={riverSlot} attempt={riverAttempt} merge={(mergeIntoExistingRiver ? 1 : 0)} " +
                    $"cell={start} height01={sc0.height01:F4} distRiver4={dRiver} avoidCorridor={(avoidCrossingCorridor ? 1 : 0)}");
            }

            var path = new List<Vector2Int>(96);
            Vector2Int cur = start;
            path.Add(cur);
            Vector2Int? prev = null;

            int forwarded = 0;
            int backtrackEvents = 0;
            int plateauMoves = 0;
            int ancientRevisits = 0;
            int safety = 0;

            while (forwarded < MaxRiverPoints && safety++ < MainLoopSafetyCap)
            {
                if (mergeIntoExistingRiver && forwarded > 0 && grid.GetCell(cur.x, cur.y).type == CellType.River)
                    break;

                ref var curCell = ref grid.GetCell(cur.x, cur.y);
                float curH = curCell.height01;

                if (!TryPickNextStep(
                        grid,
                        w,
                        h,
                        cur,
                        curH,
                        prev,
                        forwarded,
                        mergeIntoExistingRiver,
                        avoidCrossingCorridor,
                        occupiedCorridorPacked,
                        path,
                        PathRecentTabuSteps,
                        PlateauUpEpsilon,
                        rng,
                        out Vector2Int nxt,
                        out int nNb,
                        out int rejOob,
                        out int rejTabu,
                        out int rejWater,
                        out int rejMountain,
                        out int rejOccupied,
                        out int rejRiverMain,
                        out int rejPrev,
                        out int rejUphillTier,
                        out int validLand))
                {
                    int pop = rng.NextInt(BacktrackPopMin, BacktrackPopMaxExclusive);
                    pop = Mathf.Min(pop, path.Count - 1);
                    if (pop < 1 || path.Count <= 2 || backtrackEvents >= MaxBacktrackEvents)
                    {
                        if (dbg)
                        {
                            LogRiverDownhillFail(
                                "no_valid_neighbor",
                                cur,
                                path.Count,
                                curH,
                                nNb,
                                validLand,
                                rejOob,
                                rejTabu,
                                rejWater,
                                rejMountain,
                                rejOccupied,
                                rejRiverMain,
                                rejPrev,
                                rejUphillTier,
                                riverSlot,
                                riverAttempt,
                                mergeIntoExistingRiver);
                        }

                        break;
                    }

                    for (int p = 0; p < pop; p++)
                        path.RemoveAt(path.Count - 1);
                    cur = path[path.Count - 1];
                    prev = path.Count >= 2 ? path[path.Count - 2] : (Vector2Int?)null;
                    backtrackEvents++;
                    continue;
                }

                if (AncientRevisit(path, nxt, PathRecentTabuSteps))
                    ancientRevisits++;

                ref var nCell = ref grid.GetCell(nxt.x, nxt.y);
                float nH = nCell.height01 + (rng.NextFloat() - 0.5f) * 0.0012f;
                if (!(nH < curH - 1e-5f))
                    plateauMoves++;

                prev = cur;
                cur = nxt;
                path.Add(cur);
                forwarded++;

                if (mergeIntoExistingRiver && grid.GetCell(cur.x, cur.y).type == CellType.River)
                    break;
            }

            bool terminatedOk = path.Count >= 2 &&
                                (!mergeIntoExistingRiver || grid.GetCell(path[path.Count - 1].x, path[path.Count - 1].y).type == CellType.River);

            if (dbg)
            {
                string term = terminatedOk ? "ok" : "stuck_or_short";
                Debug.Log(
                    $"[RiverFlow] slot={riverSlot} attempt={riverAttempt} merge={(mergeIntoExistingRiver ? 1 : 0)} " +
                    $"length={path.Count} backtracks={backtrackEvents} plateauMoves={plateauMoves} revisits={ancientRevisits} terminated={term}");
            }

            if (mergeIntoExistingRiver && (path.Count < 2 || grid.GetCell(path[path.Count - 1].x, path[path.Count - 1].y).type != CellType.River))
            {
                failReason = "tributary_no_merge";
                ref var endC = ref grid.GetCell(path[path.Count - 1].x, path[path.Count - 1].y);
                if (dbg)
                    LogRiverDownhillFail(
                        failReason,
                        path[path.Count - 1],
                        path.Count,
                        endC.height01,
                        4,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        riverSlot,
                        riverAttempt,
                        mergeIntoExistingRiver);
                return false;
            }

            if (path.Count < 2)
            {
                failReason = "path_short";
                if (dbg)
                    LogRiverDownhillFail(
                        failReason,
                        cur,
                        path.Count,
                        grid.InBoundsCell(cur.x, cur.y) ? grid.GetCell(cur.x, cur.y).height01 : 0f,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        riverSlot,
                        riverAttempt,
                        mergeIntoExistingRiver);
                return false;
            }

            if (!HydrologyValidation.ValidateRiverCellPath(path, w, h, out string vReason))
            {
                failReason = vReason;
                if (dbg)
                    LogRiverDownhillFail(
                        failReason,
                        cur,
                        path.Count,
                        grid.InBoundsCell(cur.x, cur.y) ? grid.GetCell(cur.x, cur.y).height01 : 0f,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        riverSlot,
                        riverAttempt,
                        mergeIntoExistingRiver);
                return false;
            }

            var center = new List<Vector2>(path.Count);
            for (int i = 0; i < path.Count; i++)
            {
                var c = path[i];
                center.Add(new Vector2(c.x + 0.5f, c.y + 0.5f));
            }

            OnePassLightSmooth(center, w, h, 0.32f);
            rasterPath = path;
            centerlineCellSpace = center;
            debugMacro = new List<Vector2>(center);
            debugSmoothed = new List<Vector2>(center);
            fordCells = BuildFordAlongPath(path, config, rng, w, h);

            float len = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                len += Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
            }

            if (config.debugHydrologyNetwork || config.debugRiverHydrologyPerf || config.debugLogs)
            {
                float aw = Mathf.Clamp(config.riverWidthRadiusCells, 0f, 6f);
                UnityEngine.Debug.Log(
                    $"[RiverSimple] slot={riverSlot} attempt={riverAttempt} length={len:F1} pointCount={path.Count} " +
                    $"merged={(mergeIntoExistingRiver ? 1 : 0)} terminated=ok avgWidth={aw:F2}");
                UnityEngine.Debug.Log(
                    $"[RiverWidthTrace] stage=simple_avg_width slot={riverSlot} attempt={riverAttempt} " +
                    $"merge={(mergeIntoExistingRiver ? 1 : 0)} avgWidth={aw:F2} riverWidthRadiusCells={config.riverWidthRadiusCells}");
            }

            return true;
        }

        static int MinChebyshevDistToRiver4(GridSystem grid, int w, int h, int sx, int sy)
        {
            int best = int.MaxValue;
            const int cap = 48;
            for (int dz = -cap; dz <= cap; dz++)
            {
                for (int dx = -cap; dx <= cap; dx++)
                {
                    int x = sx + dx;
                    int y = sy + dz;
                    if (!grid.InBoundsCell(x, y))
                        continue;
                    if (grid.GetCell(x, y).type != CellType.River)
                        continue;
                    int d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
                    if (d < best)
                        best = d;
                }
            }

            return best == int.MaxValue ? -1 : best;
        }

        static bool CellTouchesRiver4(GridSystem grid, Vector2Int c)
        {
            foreach (var n in grid.Neighbors4(c))
            {
                if (!grid.InBoundsCell(n.x, n.y))
                    continue;
                if (grid.GetCell(n.x, n.y).type == CellType.River)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Elige siguiente celda: 8-vecinos, tabú corto (no cooldown absoluto), meseta con epsilon,
        /// puntuación (caída + interior + inercia), merge tributario a River sin tabú.
        /// </summary>
        static bool TryPickNextStep(
            GridSystem grid,
            int w,
            int h,
            Vector2Int cur,
            float curH,
            Vector2Int? prev,
            int forwarded,
            bool mergeIntoExistingRiver,
            bool avoidCrossingCorridor,
            HashSet<long> occupiedCorridorPacked,
            List<Vector2Int> path,
            int cooldownLen,
            float plateauEpsilon,
            IRng rng,
            out Vector2Int nxt,
            out int nNb,
            out int rejOob,
            out int rejTabu,
            out int rejWater,
            out int rejMountain,
            out int rejOccupied,
            out int rejRiverMain,
            out int rejPrev,
            out int rejUphillTier,
            out int validLand)
        {
            nxt = default;
            nNb = 0;
            rejOob = rejTabu = rejWater = rejMountain = rejOccupied = rejRiverMain = rejPrev = rejUphillTier = 0;
            validLand = 0;

            if (mergeIntoExistingRiver)
            {
                var mergeHits = new List<Vector2Int>(4);
                foreach (var n in grid.Neighbors8(cur))
                {
                    if (!grid.InBoundsCell(n.x, n.y))
                        continue;
                    ref var rc = ref grid.GetCell(n.x, n.y);
                    if (rc.type != CellType.River)
                        continue;
                    if (avoidCrossingCorridor &&
                        occupiedCorridorPacked != null &&
                        occupiedCorridorPacked.Count > 0 &&
                        occupiedCorridorPacked.Contains(Pack(n)))
                        continue;
                    mergeHits.Add(n);
                }

                if (mergeHits.Count > 0)
                {
                    nxt = PickBestScored(grid, w, h, cur, curH, prev, mergeHits, rng);
                    nNb = 8;
                    return true;
                }
            }

            var tierDown = new List<Vector2Int>(8);
            var tierPlateau = new List<Vector2Int>(8);
            var tierAny = new List<Vector2Int>(8);
            float bestAnyH = float.MaxValue;

            foreach (var n in grid.Neighbors8(cur))
            {
                nNb++;
                if (!grid.InBoundsCell(n.x, n.y))
                {
                    rejOob++;
                    continue;
                }

                if (RecentTabu(n, path, cooldownLen))
                {
                    rejTabu++;
                    continue;
                }

                ref var cell = ref grid.GetCell(n.x, n.y);
                if (cell.type == CellType.Water)
                {
                    rejWater++;
                    continue;
                }

                if (cell.type == CellType.Mountain)
                {
                    rejMountain++;
                    continue;
                }

                if (avoidCrossingCorridor &&
                    occupiedCorridorPacked != null &&
                    occupiedCorridorPacked.Count > 0 &&
                    occupiedCorridorPacked.Contains(Pack(n)) &&
                    cell.type != CellType.River)
                {
                    rejOccupied++;
                    continue;
                }

                if (!mergeIntoExistingRiver && cell.type == CellType.River)
                {
                    rejRiverMain++;
                    continue;
                }

                if (cell.type != CellType.Land)
                    continue;

                validLand++;
                float h01 = cell.height01 + (rng.NextFloat() - 0.5f) * 0.0012f;
                if (h01 < curH - 1e-5f)
                    tierDown.Add(n);
                else if (h01 <= curH + plateauEpsilon)
                    tierPlateau.Add(n);

                if (h01 < bestAnyH - 1e-5f)
                {
                    bestAnyH = h01;
                    tierAny.Clear();
                    tierAny.Add(n);
                }
                else if (Mathf.Abs(h01 - bestAnyH) < 1e-4f)
                {
                    tierAny.Add(n);
                }
            }

            List<Vector2Int> pool = tierDown.Count > 0 ? tierDown : tierPlateau.Count > 0 ? tierPlateau : tierAny;
            if (pool.Count == 0)
                return false;

            if (mergeIntoExistingRiver && pool.Count > 1)
            {
                int withRiver = 0;
                for (int i = 0; i < pool.Count; i++)
                {
                    if (CellTouchesRiver4(grid, pool[i]))
                        withRiver++;
                }

                if (withRiver > 0 && withRiver < pool.Count)
                    pool.RemoveAll(x => !CellTouchesRiver4(grid, x));
            }

            if (pool.Count == 0)
            {
                rejUphillTier = 1;
                return false;
            }

            nxt = PickBestScored(grid, w, h, cur, curH, prev, pool, rng);
            return true;
        }

        static bool RecentTabu(Vector2Int n, List<Vector2Int> path, int cooldownLen)
        {
            if (path == null || cooldownLen <= 0)
                return false;
            for (int back = 1; back <= cooldownLen; back++)
            {
                int idx = path.Count - 1 - back;
                if (idx < 0)
                    break;
                if (path[idx] == n)
                    return true;
            }

            return false;
        }

        static bool AncientRevisit(List<Vector2Int> path, Vector2Int nxt, int cooldownLen)
        {
            int lim = path.Count - (cooldownLen + 1);
            for (int i = 0; i < lim; i++)
            {
                if (path[i] == nxt)
                    return true;
            }

            return false;
        }

        static float ScoreNeighbor(
            int w,
            int h,
            Vector2Int cur,
            Vector2Int cand,
            Vector2Int? prev,
            float curH,
            float candH01)
        {
            // Bias descendente más fuerte: prioriza caída sobre movimiento lateral en plateau.
            float drop = Mathf.Max(0f, curH - candH01) * 4.0f;
            float distBorder = Mathf.Min(Mathf.Min(cand.x, cand.y), Mathf.Min(w - 1 - cand.x, h - 1 - cand.y));
            float interior01 = Mathf.Clamp01(distBorder / Mathf.Max(4f, Mathf.Min(w, h) * 0.28f));
            float inward = interior01 * 0.04f;
            float inertia = 0f;
            if (prev.HasValue)
            {
                Vector2 d0 = new Vector2(cur.x - prev.Value.x, cur.y - prev.Value.y);
                if (d0.sqrMagnitude > 1e-6f)
                {
                    d0.Normalize();
                    Vector2 d1 = new Vector2(cand.x - cur.x, cand.y - cur.y);
                    if (d1.sqrMagnitude > 1e-6f)
                    {
                        d1.Normalize();
                        // Inercia elevada: el río mantiene dirección en vez de serpentear en meseta.
                        inertia = Vector2.Dot(d0, d1) * 0.10f;
                    }
                }
            }

            return drop + inward + inertia;
        }

        static Vector2Int PickBestScored(
            GridSystem grid,
            int w,
            int h,
            Vector2Int cur,
            float curH,
            Vector2Int? prev,
            List<Vector2Int> pool,
            IRng rng)
        {
            float best = float.NegativeInfinity;
            var tops = new List<Vector2Int>(pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                var n = pool[i];
                float h01 = grid.GetCell(n.x, n.y).height01;
                float s = ScoreNeighbor(w, h, cur, n, prev, curH, h01) + (rng.NextFloat() - 0.5f) * 0.0008f;
                if (s > best + 1e-5f)
                {
                    best = s;
                    tops.Clear();
                    tops.Add(n);
                }
                else if (Mathf.Abs(s - best) < 1e-5f)
                {
                    tops.Add(n);
                }
            }

            return tops[rng.NextInt(0, tops.Count)];
        }

        static void LogRiverDownhillFail(
            string reason,
            Vector2Int cell,
            int steps,
            float currentHeight01,
            int neighborCount4,
            int validNeighborCount,
            int rOob,
            int rTabu,
            int rWat,
            int rMtn,
            int rOcc,
            int rRvMain,
            int rPrev,
            int rTierPrev,
            int riverSlot,
            int riverAttempt,
            bool merge)
        {
            Debug.Log(
                $"[RiverDownhillFail] reason={reason} merge={(merge ? 1 : 0)} slot={riverSlot} attempt={riverAttempt} " +
                $"cell={cell} steps={steps} currentHeight01={currentHeight01:F4} n8={neighborCount4} validLand={validNeighborCount} " +
                $"rj=oob:{rOob},tabu:{rTabu},wat:{rWat},mtn:{rMtn},occ:{rOcc},rvMain:{rRvMain},prev:{rPrev},tierPrev:{rTierPrev}");
        }

        public static void OnePassLightSmooth(List<Vector2> poly, int gw, int gh, float a)
        {
            if (poly == null || poly.Count < 3)
                return;
            var tmp = new List<Vector2>(poly);
            float minX = 0.5f, maxX = gw - 0.5f, minY = 0.5f, maxY = gh - 0.5f;
            for (int i = 1; i < poly.Count - 1; i++)
            {
                Vector2 avg = (tmp[i - 1] + tmp[i + 1]) * 0.5f;
                Vector2 p = Vector2.Lerp(tmp[i], avg, Mathf.Clamp01(a));
                p.x = Mathf.Clamp(p.x, minX, maxX);
                p.y = Mathf.Clamp(p.y, minY, maxY);
                poly[i] = p;
            }
        }

        public static List<Vector2Int> BuildFordAlongPath(List<Vector2Int> path, MapGenConfig config, IRng rng, int w, int h)
        {
            var ford = new List<Vector2Int>(8);
            int fordEvery = Mathf.Max(0, config.riverFordEveryCells);
            if (fordEvery <= 1 || path == null || path.Count < 8)
                return ford;

            int fordPhase = rng.NextInt(0, fordEvery);
            const int desiredFordsPerRiver = 3;
            var used = new HashSet<long>();
            int startIdx = Mathf.Clamp(path.Count / 10, 2, Mathf.Max(2, path.Count - 4));
            int endIdx = Mathf.Clamp(path.Count - 1 - startIdx, startIdx + 1, path.Count - 2);
            for (int j = 1; j <= desiredFordsPerRiver; j++)
            {
                float t = j / (desiredFordsPerRiver + 1f);
                int idx = Mathf.RoundToInt(Mathf.Lerp(startIdx, endIdx, t));
                int jitter = Mathf.Clamp(fordPhase % 3, 0, 2);
                idx = Mathf.Clamp(idx + ((j & 1) == 0 ? -jitter : jitter), startIdx, endIdx);
                var c = path[idx];
                if (c.x < 0 || c.x >= w || c.y < 0 || c.y >= h)
                    continue;
                long k = Pack(c);
                if (used.Add(k))
                    ford.Add(c);
            }

            return ford;
        }
    }
}
