using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Ancla lógica para extremos del río principal (solo hidrología / A*).</summary>
    public enum RiverAnchorKind
    {
        BorderExit = 0,
        HighlandSpring = 1,
        MountainSpring = 2,
        InteriorBasin = 3,
        LakeSink = 4,
        ExistingRiverJoin = 5
    }

    /// <summary>Patrón de extremos del río principal (no altera coste A*, solo start/goal).</summary>
    public enum RiverMainPattern
    {
        BorderToBorder = 0,
        HighlandToBorder = 1,
        MountainToBorder = 2,
        HighlandToLake = 3,
        LakeToBorder = 4,
        InteriorToBorder = 5,
        BorderToLake = 6,
        BorderToInteriorBasin = 7
    }

    public readonly struct RiverAnchorCandidate
    {
        public readonly Vector2Int cell;
        public readonly RiverAnchorKind kind;
        public readonly float score;
        public readonly float height01;
        public readonly float distanceToBorder;
        public readonly float distanceToWater;
        public readonly float distanceToExistingRiver;

        public RiverAnchorCandidate(
            Vector2Int cell,
            RiverAnchorKind kind,
            float score,
            float height01,
            float distanceToBorder,
            float distanceToWater,
            float distanceToExistingRiver)
        {
            this.cell = cell;
            this.kind = kind;
            this.score = score;
            this.height01 = height01;
            this.distanceToBorder = distanceToBorder;
            this.distanceToWater = distanceToWater;
            this.distanceToExistingRiver = distanceToExistingRiver;
        }
    }

    public static partial class RiverRouteGenerator
    {
        /// <summary>Candidatos de hundimiento de lago antes del flood-fill (solo referencia para ríos).</summary>
        public static void PreparePlannedLakeSinkCandidates(GridSystem grid, MapGenConfig config, IRng rng)
        {
            if (grid == null)
                return;
            grid.PlannedLakeSinkCandidates = new List<Vector2Int>();
            if (config == null || rng == null || config.lakeCount <= 0)
                return;

            int w = grid.Width;
            int h = grid.Height;
            int minSep = Mathf.Max(26, Mathf.Min(w, h) / 6);
            int inner = Mathf.Max(6, Mathf.Min(w, h) / 20);

            var locals = new List<(Vector2Int c, float h01)>();
            for (int z = inner; z < h - inner; z++)
            {
                for (int x = inner; x < w - inner; x++)
                {
                    ref var cd = ref grid.GetCell(x, z);
                    if (cd.type != CellType.Land)
                        continue;
                    float v = cd.height01;
                    bool localMin = true;
                    foreach (var nb in grid.Neighbors4(x, z))
                    {
                        ref var nd = ref grid.GetCell(nb.x, nb.y);
                        if (nd.type != CellType.Land)
                        {
                            localMin = false;
                            break;
                        }

                        if (nd.height01 < v - 1e-5f)
                        {
                            localMin = false;
                            break;
                        }
                    }

                    if (!localMin)
                        continue;
                    locals.Add((new Vector2Int(x, z), v));
                }
            }

            locals.Sort((a, b) => a.h01.CompareTo(b.h01));
            var picked = new List<Vector2Int>();
            int maxPick = Mathf.Clamp(config.lakeCount, 1, 8);
            for (int i = 0; i < locals.Count && picked.Count < maxPick; i++)
            {
                var c = locals[i].c;
                bool ok = true;
                for (int j = 0; j < picked.Count; j++)
                {
                    int md = Mathf.Abs(picked[j].x - c.x) + Mathf.Abs(picked[j].y - c.y);
                    if (md < minSep)
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                    picked.Add(c);
            }

            if (picked.Count == 0)
            {
                int salt = rng.NextInt(0, 1 << 20);
                for (int t = 0; t < 48 && picked.Count < maxPick; t++)
                {
                    int x = inner + (salt + t * 9973) % (w - 2 * inner);
                    int z = inner + (salt + t * 7919) % (h - 2 * inner);
                    if (grid.GetCell(x, z).type != CellType.Land)
                        continue;
                    picked.Add(new Vector2Int(x, z));
                }
            }

            grid.PlannedLakeSinkCandidates = picked;
        }

        public static int ResolveMainMinPathCells(MapGenConfig config, int w, int h)
        {
            if (config != null && config.riverMainMinPathCells > 0)
                return Mathf.Clamp(config.riverMainMinPathCells, 2, w * h - 1);
            return Mathf.Max(2, Mathf.RoundToInt(Mathf.Min(w, h) * 0.45f));
        }

        public static int ResolveMainMaxPathCells(MapGenConfig config, int w, int h)
        {
            int lo = ResolveMainMinPathCells(config, w, h);
            if (config != null && config.riverMainMaxPathCells > 0)
                return Mathf.Max(lo + 1, Mathf.Clamp(config.riverMainMaxPathCells, lo + 1, w * h));
            return Mathf.Max(lo + 1, Mathf.RoundToInt((w + h) * 1.25f));
        }

        static int MinChebyshevToBorder(Vector2Int c, int w, int h) =>
            Mathf.Min(Mathf.Min(c.x, w - 1 - c.x), Mathf.Min(c.y, h - 1 - c.y));

        static void LogRiverMainPattern(
            int slot,
            int attempt,
            RiverMainPattern pat,
            RiverAnchorKind sk,
            RiverAnchorKind gk,
            Vector2Int s,
            Vector2Int g,
            GridSystem grid,
            int w,
            int h)
        {
            float sh = grid.GetCell(s.x, s.y).height01;
            float gh = grid.GetCell(g.x, g.y).height01;
            int dsb = MinChebyshevToBorder(s, w, h);
            int dgb = MinChebyshevToBorder(g, w, h);
            int startAtBorder = dsb == 0 ? 1 : 0;
            int goalAtBorder = gk == RiverAnchorKind.BorderExit && dgb == 0 ? 1 : 0;
            int sourceWasAllowedBorder =
                pat == RiverMainPattern.BorderToBorder ||
                pat == RiverMainPattern.BorderToLake ||
                pat == RiverMainPattern.BorderToInteriorBasin
                    ? 1
                    : 0;
            UnityEngine.Debug.Log(
                $"[RiverMainPattern] slot={slot} attempt={attempt} pattern={pat} startKind={sk} goalKind={gk} " +
                $"startAtBorder={startAtBorder} goalAtBorder={goalAtBorder} sourceWasAllowedBorder={sourceWasAllowedBorder} " +
                $"start={s} goal={g} startHeight={sh:F3} goalHeight={gh:F3} sourceDistBorder={dsb} goalDistBorder={dgb}");
        }

        static bool SatisfiesPatternSeparation(int w, int h, Vector2Int start, Vector2Int goal, RiverMainPattern pat)
        {
            if (pat == RiverMainPattern.BorderToBorder)
                return SatisfiesLateralSeparation(w, h, start, goal);
            int man = Mathf.Abs(start.x - goal.x) + Mathf.Abs(start.y - goal.y);
            float frac = pat == RiverMainPattern.BorderToLake || pat == RiverMainPattern.BorderToInteriorBasin
                ? 0.18f
                : 0.22f;
            return man >= Mathf.RoundToInt(Mathf.Min(w, h) * frac);
        }

        static void BuildMainAnchorPools(
            GridSystem grid,
            int w,
            int h,
            MapGenConfig config,
            IRng rng,
            int borderInsetCells,
            int cornerExcludedCells,
            bool logAnchorDetail,
            out List<Vector2Int> borderExits,
            out List<Vector2Int> highlandSprings,
            out List<Vector2Int> mountainSprings,
            out List<Vector2Int> interiorBasins,
            out bool anyMountainCell)
        {
            borderExits = new List<Vector2Int>(w * 2 + h * 2);
            highlandSprings = new List<Vector2Int>(512);
            mountainSprings = new List<Vector2Int>(256);
            interiorBasins = new List<Vector2Int>(512);
            anyMountainCell = false;

            List<Vector2Int> borderList = borderExits;

            int cx = Mathf.Clamp(cornerExcludedCells, 1, Mathf.Min(w, h) / 2 - 1);
            int dMin = Mathf.Max(1, config.riverMainMinSourceDistanceFromBorderCells);
            int dMax = Mathf.Max(dMin + 1, config.riverMainMaxSourceDistanceFromBorderCells);

            double sumH = 0;
            int cntL = 0;
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (grid.GetCell(x, z).type == CellType.Land)
                    {
                        sumH += grid.GetCell(x, z).height01;
                        cntL++;
                    }
                }
            }

            float avgH = cntL > 0 ? (float)(sumH / cntL) : 0.5f;

            var mountainCells = new List<Vector2Int>(512);
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (grid.GetCell(x, z).type == CellType.Mountain)
                    {
                        anyMountainCell = true;
                        mountainCells.Add(new Vector2Int(x, z));
                    }
                }
            }

            var nearMountain = new bool[w, h];
            for (int i = 0; i < mountainCells.Count; i++)
            {
                var mc = mountainCells[i];
                for (int dz = -2; dz <= 2; dz++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int nx = mc.x + dx;
                        int nz = mc.y + dz;
                        if (!grid.InBoundsCell(nx, nz))
                            continue;
                        nearMountain[nx, nz] = true;
                    }
                }
            }

            void TryAddBorderLand(int x, int z)
            {
                if (x < cx || x > w - 1 - cx || z < cx || z > h - 1 - cx)
                    return;
                if (grid.GetCell(x, z).type != CellType.Land)
                    return;
                borderList.Add(new Vector2Int(x, z));
            }

            for (int x = cx; x < w - cx; x++)
            {
                TryAddBorderLand(x, 0);
                TryAddBorderLand(x, h - 1);
            }

            for (int z = cx; z < h - cx; z++)
            {
                TryAddBorderLand(0, z);
                TryAddBorderLand(w - 1, z);
            }

            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    ref var c = ref grid.GetCell(x, z);
                    if (c.type != CellType.Land)
                        continue;
                    int db = MinChebyshevToBorder(new Vector2Int(x, z), w, h);
                    if (db >= dMin && db <= dMax)
                    {
                        if (c.height01 >= avgH + 0.035f && c.height01 < 0.96f)
                            highlandSprings.Add(new Vector2Int(x, z));
                        if (c.height01 <= avgH - 0.035f)
                            interiorBasins.Add(new Vector2Int(x, z));
                    }

                    if (nearMountain[x, z] && db >= 4)
                        mountainSprings.Add(new Vector2Int(x, z));
                }
            }

            int cellTypeMountainCount = mountainCells.Count;
            int heightBasedMountainCandidates = 0;
            int slopeBasedCandidates = 0;
            if (mountainSprings.Count < 28)
            {
                float landMaxH = 0f;
                for (int zz = 0; zz < h; zz++)
                {
                    for (int xx = 0; xx < w; xx++)
                    {
                        ref var lc = ref grid.GetCell(xx, zz);
                        if (lc.type != CellType.Land)
                            continue;
                        landMaxH = Mathf.Max(landMaxH, lc.height01);
                    }
                }

                float thrHigh = Mathf.Max(0.82f, avgH + Mathf.Max(0.1f, (landMaxH - avgH) * 0.52f));
                var seen = new HashSet<long>();
                for (int si = 0; si < mountainSprings.Count; si++)
                    seen.Add(Pack(mountainSprings[si].x, mountainSprings[si].y));

                for (int z = 0; z < h; z++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        ref var c = ref grid.GetCell(x, z);
                        if (c.type != CellType.Land)
                            continue;
                        long pk = Pack(x, z);
                        if (seen.Contains(pk))
                            continue;
                        int db = MinChebyshevToBorder(new Vector2Int(x, z), w, h);
                        if (db < dMin)
                            continue;

                        float maxNb = c.height01;
                        foreach (var nb in grid.Neighbors4(x, z))
                        {
                            ref var nc = ref grid.GetCell(nb.x, nb.y);
                            if (nc.type == CellType.Land)
                                maxNb = Mathf.Max(maxNb, nc.height01);
                        }

                        float slope = maxNb - c.height01;
                        bool highSpring = c.height01 >= thrHigh && c.height01 < 0.97f;
                        bool slopeSpring = c.height01 >= avgH + 0.08f && slope > 0.055f && c.height01 < 0.96f;
                        if (!highSpring && !slopeSpring)
                            continue;

                        mountainSprings.Add(new Vector2Int(x, z));
                        seen.Add(pk);
                        if (highSpring)
                            heightBasedMountainCandidates++;
                        if (slopeSpring)
                            slopeBasedCandidates++;
                        if (mountainSprings.Count > 420)
                            break;
                    }

                    if (mountainSprings.Count > 420)
                        break;
                }
            }

            bool usedFallbackHeightBased = cellTypeMountainCount == 0 &&
                (heightBasedMountainCandidates > 0 || slopeBasedCandidates > 0);

            if (logAnchorDetail || (config != null && (config.debugHydrologyNetwork || config.debugLogs)))
            {
                UnityEngine.Debug.Log(
                    $"[MountainAnchorSource] cellTypeMountainCount={cellTypeMountainCount} " +
                    $"heightBasedMountainCandidates={heightBasedMountainCandidates} slopeBasedCandidates={slopeBasedCandidates} " +
                    $"usedFallbackHeightBased={(usedFallbackHeightBased ? 1 : 0)} totalMountainPool={mountainSprings.Count}");
            }

            if (borderList.Count == 0 && rng != null)
            {
                for (int t = 0; t < 80; t++)
                {
                    int edge = t % 4;
                    if (TryPickBorderLandAnchor(grid, w, h, rng, edge, cx, borderInsetCells, false, out Vector2Int bc))
                        borderList.Add(bc);
                }
            }
        }

        static bool TryPickEndpointsForPattern(
            RiverMainPattern pattern,
            GridSystem grid,
            int w,
            int h,
            IRng rng,
            MapGenConfig config,
            List<Vector2Int> borderExits,
            List<Vector2Int> highlandSprings,
            List<Vector2Int> mountainSprings,
            List<Vector2Int> interiorBasins,
            IReadOnlyList<Vector2Int> lakeSinks,
            out Vector2Int start,
            out Vector2Int goal,
            out RiverAnchorKind startKind,
            out RiverAnchorKind goalKind)
        {
            start = default;
            goal = default;
            startKind = RiverAnchorKind.BorderExit;
            goalKind = RiverAnchorKind.BorderExit;

            bool PickDistinct(List<Vector2Int> poolA, List<Vector2Int> poolB, out Vector2Int a, out Vector2Int b)
            {
                a = default;
                b = default;
                if (poolA == null || poolB == null || poolA.Count == 0 || poolB.Count == 0)
                    return false;
                for (int t = 0; t < 24; t++)
                {
                    a = poolA[rng.NextInt(0, poolA.Count)];
                    b = poolB[rng.NextInt(0, poolB.Count)];
                    if (a != b)
                        return true;
                }

                return false;
            }

            bool Land(Vector2Int c) =>
                grid.InBoundsCell(c.x, c.y) && grid.GetCell(c.x, c.y).type == CellType.Land;

            switch (pattern)
            {
                case RiverMainPattern.BorderToBorder:
                    if (borderExits == null || borderExits.Count < 2)
                        return false;
                    if (TryPickCentralBorderToBorderFromPool(
                            grid,
                            w,
                            h,
                            rng,
                            borderExits,
                            out start,
                            out goal))
                    {
                        startKind = RiverAnchorKind.BorderExit;
                        goalKind = RiverAnchorKind.BorderExit;
                        return Land(start) && Land(goal);
                    }

                    for (int t = 0; t < 28; t++)
                    {
                        start = borderExits[rng.NextInt(0, borderExits.Count)];
                        goal = borderExits[rng.NextInt(0, borderExits.Count)];
                        if (start == goal)
                            continue;
                        if (!SatisfiesLateralSeparation(w, h, start, goal))
                            continue;
                        startKind = RiverAnchorKind.BorderExit;
                        goalKind = RiverAnchorKind.BorderExit;
                        return Land(start) && Land(goal);
                    }

                    return false;
                case RiverMainPattern.HighlandToBorder:
                    if (!PickDistinct(highlandSprings, borderExits, out start, out goal))
                        return false;
                    startKind = RiverAnchorKind.HighlandSpring;
                    goalKind = RiverAnchorKind.BorderExit;
                    return Land(start) && Land(goal);
                case RiverMainPattern.MountainToBorder:
                    if (mountainSprings.Count == 0)
                        return false;
                    if (!PickDistinct(mountainSprings, borderExits, out start, out goal))
                        return false;
                    startKind = RiverAnchorKind.MountainSpring;
                    goalKind = RiverAnchorKind.BorderExit;
                    return Land(start) && Land(goal);
                case RiverMainPattern.InteriorToBorder:
                    if (!PickDistinct(interiorBasins, borderExits, out start, out goal))
                        return false;
                    startKind = RiverAnchorKind.InteriorBasin;
                    goalKind = RiverAnchorKind.BorderExit;
                    return Land(start) && Land(goal);
                case RiverMainPattern.HighlandToLake:
                    if (lakeSinks == null || lakeSinks.Count == 0 || highlandSprings.Count == 0)
                        return false;
                    start = highlandSprings[rng.NextInt(0, highlandSprings.Count)];
                    goal = lakeSinks[rng.NextInt(0, lakeSinks.Count)];
                    startKind = RiverAnchorKind.HighlandSpring;
                    goalKind = RiverAnchorKind.LakeSink;
                    if (!Land(goal))
                    {
                        int[] ox = { 1, -1, 0, 0, 1, 1, -1, -1 };
                        int[] oy = { 0, 0, 1, -1, 1, -1, 1, -1 };
                        for (int d = 0; d < 8; d++)
                        {
                            var g2 = new Vector2Int(goal.x + ox[d], goal.y + oy[d]);
                            if (Land(g2))
                            {
                                goal = g2;
                                break;
                            }
                        }
                    }

                    return Land(start) && Land(goal);
                case RiverMainPattern.LakeToBorder:
                    if (lakeSinks == null || lakeSinks.Count == 0 || borderExits.Count == 0)
                        return false;
                    goal = borderExits[rng.NextInt(0, borderExits.Count)];
                    var seed = lakeSinks[rng.NextInt(0, lakeSinks.Count)];
                    start = seed;
                    bool foundStart = Land(start);
                    if (!foundStart)
                    {
                        for (int r = 1; r <= 3 && !foundStart; r++)
                        {
                            for (int dz = -r; dz <= r && !foundStart; dz++)
                            {
                                for (int dx = -r; dx <= r && !foundStart; dx++)
                                {
                                    var c = new Vector2Int(seed.x + dx, seed.y + dz);
                                    if (Land(c))
                                    {
                                        start = c;
                                        foundStart = true;
                                    }
                                }
                            }
                        }
                    }

                    startKind = RiverAnchorKind.LakeSink;
                    goalKind = RiverAnchorKind.BorderExit;
                    return Land(start) && Land(goal);
                case RiverMainPattern.BorderToLake:
                    if (lakeSinks == null || lakeSinks.Count == 0 || borderExits == null || borderExits.Count == 0)
                        return false;
                    start = borderExits[rng.NextInt(0, borderExits.Count)];
                    goal = lakeSinks[rng.NextInt(0, lakeSinks.Count)];
                    startKind = RiverAnchorKind.BorderExit;
                    goalKind = RiverAnchorKind.LakeSink;
                    if (!Land(goal))
                    {
                        int[] ox = { 1, -1, 0, 0, 1, 1, -1, -1 };
                        int[] oy = { 0, 0, 1, -1, 1, -1, 1, -1 };
                        for (int d = 0; d < 8; d++)
                        {
                            var g2 = new Vector2Int(goal.x + ox[d], goal.y + oy[d]);
                            if (Land(g2))
                            {
                                goal = g2;
                                break;
                            }
                        }
                    }

                    return Land(start) && Land(goal);
                case RiverMainPattern.BorderToInteriorBasin:
                    if (interiorBasins == null || interiorBasins.Count == 0 || borderExits == null || borderExits.Count == 0)
                        return false;
                    if (!PickDistinct(borderExits, interiorBasins, out start, out goal))
                        return false;
                    startKind = RiverAnchorKind.BorderExit;
                    goalKind = RiverAnchorKind.InteriorBasin;
                    return Land(start) && Land(goal);
                default:
                    return false;
            }
        }

        static int BorderEdgeOfCell(Vector2Int c, int w, int h)
        {
            if (c.y <= 0) return 0;
            if (c.y >= h - 1) return 1;
            if (c.x <= 0) return 2;
            if (c.x >= w - 1) return 3;

            int dN = c.y;
            int dS = h - 1 - c.y;
            int dW = c.x;
            int dE = w - 1 - c.x;
            int best = Mathf.Min(Mathf.Min(dN, dS), Mathf.Min(dW, dE));
            if (best == dN) return 0;
            if (best == dS) return 1;
            if (best == dW) return 2;
            return 3;
        }

        static float DistanceSqPointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 1e-6f)
                return (p - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            Vector2 q = a + ab * t;
            return (p - q).sqrMagnitude;
        }

        static float MainCentralBorderPairScore(int w, int h, Vector2Int start, Vector2Int goal, IRng rng)
        {
            Vector2 center = new Vector2((w - 1) * 0.5f, (h - 1) * 0.5f);
            Vector2 a = new Vector2(start.x, start.y);
            Vector2 b = new Vector2(goal.x, goal.y);
            float minDim = Mathf.Max(1f, Mathf.Min(w, h));
            float diag = Mathf.Max(1f, Mathf.Sqrt(w * (float)w + h * (float)h));
            float centerDist = Mathf.Sqrt(DistanceSqPointToSegment(center, a, b)) / minDim;
            float direct = Vector2.Distance(a, b) / diag;

            int eA = BorderEdgeOfCell(start, w, h);
            int eB = BorderEdgeOfCell(goal, w, h);
            float edgePenalty = eA == eB ? 0.85f : 0f;
            if (OppositeEdge(eA) != eB)
                edgePenalty += 0.18f;

            float endCenterOffset;
            if (eA <= 1 && eB <= 1)
                endCenterOffset = (Mathf.Abs(start.x - center.x) + Mathf.Abs(goal.x - center.x)) / Mathf.Max(1f, w);
            else if (eA >= 2 && eB >= 2)
                endCenterOffset = (Mathf.Abs(start.y - center.y) + Mathf.Abs(goal.y - center.y)) / Mathf.Max(1f, h);
            else
                endCenterOffset =
                    (Mathf.Abs(start.x - center.x) / Mathf.Max(1f, w) +
                     Mathf.Abs(start.y - center.y) / Mathf.Max(1f, h) +
                     Mathf.Abs(goal.x - center.x) / Mathf.Max(1f, w) +
                     Mathf.Abs(goal.y - center.y) / Mathf.Max(1f, h)) * 0.5f;

            float jitter = rng != null ? rng.NextFloat() * 0.035f : 0f;
            return centerDist * 2.2f + endCenterOffset * 0.45f + edgePenalty - direct * 0.18f + jitter;
        }

        static bool TryPickCentralBorderToBorderFromPool(
            GridSystem grid,
            int w,
            int h,
            IRng rng,
            List<Vector2Int> borderExits,
            out Vector2Int start,
            out Vector2Int goal)
        {
            start = default;
            goal = default;
            if (grid == null || rng == null || borderExits == null || borderExits.Count < 2)
                return false;

            float bestScore = float.MaxValue;
            bool found = false;
            int samples = Mathf.Clamp(borderExits.Count * 3, 48, 160);
            for (int i = 0; i < samples; i++)
            {
                Vector2Int a = borderExits[rng.NextInt(0, borderExits.Count)];
                Vector2Int b = borderExits[rng.NextInt(0, borderExits.Count)];
                if (a == b)
                    continue;
                if (!CellPairIsLand(grid, a, b))
                    continue;
                if (!SatisfiesLateralSeparation(w, h, a, b))
                    continue;

                float score = MainCentralBorderPairScore(w, h, a, b, rng);
                if (score >= bestScore)
                    continue;

                bestScore = score;
                start = a;
                goal = b;
                found = true;
            }

            return found;
        }

        static bool TryPickCentralBorderToBorderFromEdges(
            GridSystem grid,
            int w,
            int h,
            IRng rng,
            int cornerExcluded,
            int edgeInset,
            out Vector2Int start,
            out Vector2Int goal)
        {
            start = default;
            goal = default;
            if (grid == null || rng == null)
                return false;

            float bestScore = float.MaxValue;
            bool found = false;
            for (int i = 0; i < 64; i++)
            {
                int edgeA = rng.NextInt(0, 4);
                int edgeB = i < 42 ? OppositeEdge(edgeA) : rng.NextInt(0, 4);
                if (edgeA == edgeB)
                    continue;
                if (!TryPickBorderLandAnchor(grid, w, h, rng, edgeA, cornerExcluded, edgeInset, false, out Vector2Int a))
                    continue;
                if (!TryPickBorderLandAnchor(grid, w, h, rng, edgeB, cornerExcluded, edgeInset, false, out Vector2Int b))
                    continue;
                if (!CellPairIsLand(grid, a, b) || !SatisfiesLateralSeparation(w, h, a, b))
                    continue;

                float score = MainCentralBorderPairScore(w, h, a, b, rng);
                if (score >= bestScore)
                    continue;

                bestScore = score;
                start = a;
                goal = b;
                found = true;
            }

            return found;
        }

        static RiverMainPattern[] BuildWeightedPatternOrder(
            IRng rng,
            MapGenConfig config,
            bool hasMountainSpring,
            bool hasLakeSink,
            bool hasHighland,
            bool hasBasin,
            bool hasBorderExitPool)
        {
            float wb = config.riverMainAllowBorderToBorder && hasBorderExitPool
                ? Mathf.Max(0f, config.riverMainBorderToBorderWeight)
                : 0f;
            float wi = Mathf.Max(0f, config.riverMainInteriorSourceWeight);
            float wl = Mathf.Max(0f, config.riverMainLakeSinkWeight);
            float wbs = config.riverMainAllowBorderStart && hasBorderExitPool
                ? Mathf.Max(0f, config.riverMainBorderStartWeight)
                : 0f;

            if (config.lakeCount <= 0 && config.riverMainPreferBorderToBorderWhenNoLake)
            {
                wb = Mathf.Max(wb, 2.5f);
                wi *= 0.35f;
                wbs *= 0.45f;
            }

            var scored = new List<(RiverMainPattern p, float key)>();

            void Offer(RiverMainPattern p, float weight)
            {
                if (weight < 1e-5f)
                    return;
                scored.Add((p, weight * rng.NextFloat()));
            }

            if (hasMountainSpring)
            {
                Offer(RiverMainPattern.MountainToBorder, wi * 0.55f);
                Offer(RiverMainPattern.HighlandToBorder, wi * 0.35f);
            }
            else if (hasHighland)
            {
                Offer(RiverMainPattern.HighlandToBorder, wi * 0.85f);
            }

            if (hasLakeSink && hasHighland)
                Offer(RiverMainPattern.HighlandToLake, wl * 0.55f);
            if (hasLakeSink)
                Offer(RiverMainPattern.LakeToBorder, wl * 0.45f);

            if (hasBasin)
                Offer(RiverMainPattern.InteriorToBorder, wi * 0.25f);

            if (wbs > 1e-5f)
            {
                if (hasLakeSink)
                    Offer(RiverMainPattern.BorderToLake, wbs * 0.5f);
                if (hasBasin)
                    Offer(RiverMainPattern.BorderToInteriorBasin, wbs * 0.4f);
            }

            if (wb > 1e-5f)
                Offer(RiverMainPattern.BorderToBorder, wb);

            if (scored.Count == 0)
            {
                if (config.riverMainAllowBorderToBorder && hasBorderExitPool)
                    return new[] { RiverMainPattern.BorderToBorder };
                return new[] { RiverMainPattern.HighlandToBorder };
            }

            scored.Sort((a, b) => b.key.CompareTo(a.key));

            if (config.riverMainAllowBorderToBorder && hasBorderExitPool)
            {
                int b2b = scored.FindIndex(s => s.p == RiverMainPattern.BorderToBorder);
                if (b2b > 0)
                {
                    var first = scored[b2b];
                    scored.RemoveAt(b2b);
                    scored.Insert(0, first);
                }
            }
            else if (config.lakeCount <= 0 && config.riverMainPreferBorderToBorderWhenNoLake)
            {
                int b2b = scored.FindIndex(s => s.p == RiverMainPattern.BorderToBorder);
                if (b2b > 0)
                {
                    var first = scored[b2b];
                    scored.RemoveAt(b2b);
                    scored.Insert(0, first);
                }
            }

            var arr = new RiverMainPattern[scored.Count];
            for (int i = 0; i < scored.Count; i++)
                arr[i] = scored[i].p;
            return arr;
        }

        static bool TryBuildMainRiverTypedAnchorsFirst(
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
            int minMain,
            int maxMain,
            int borderInsetCells,
            int cornerExcludedCells,
            out int expandedNodes,
            out float finalCost,
            out float sumNearRiverPen,
            out float sumHeightBias,
            out List<Vector2Int> path,
            out Vector2Int logStart,
            out Vector2Int logGoal,
            out string rejectReason)
        {
            expandedNodes = 0;
            finalCost = 0f;
            sumNearRiverPen = 0f;
            sumHeightBias = 0f;
            path = null;
            logStart = default;
            logGoal = default;
            rejectReason = null;

            BuildMainAnchorPools(
                grid,
                w,
                h,
                config,
                rng,
                borderInsetCells,
                cornerExcludedCells,
                logRoute || config.debugHydrologyNetwork || config.debugLogs,
                out List<Vector2Int> borderExits,
                out List<Vector2Int> highlandSprings,
                out List<Vector2Int> mountainSprings,
                out List<Vector2Int> interiorBasins,
                out bool anyMountain);

            var lakeList = grid.PlannedLakeSinkCandidates;
            int lakeN = lakeList != null ? lakeList.Count : 0;

            if (logRoute || config.debugHydrologyNetwork || config.debugLogs)
            {
                UnityEngine.Debug.Log(
                    $"[RiverAnchorCandidates] highland={highlandSprings.Count} mountain={mountainSprings.Count} " +
                    $"border={borderExits.Count} lakeSink={lakeN} basin={interiorBasins.Count} anyMountainCell={(anyMountain ? 1 : 0)}");
            }

            bool hasHigh = highlandSprings.Count > 0;
            bool hasMt = mountainSprings.Count > 0;
            bool hasBas = interiorBasins.Count > 0;
            bool hasLake = lakeN > 0;

            RiverMainPattern[] order = BuildWeightedPatternOrder(
                rng,
                config,
                hasMt,
                hasLake,
                hasHigh,
                hasBas,
                borderExits.Count > 0);

            int pairIndex = 0;
            int policyRetryCount = 0;
            foreach (RiverMainPattern pat in order)
            {
                int tries = pat == RiverMainPattern.BorderToBorder
                    ? 28
                    : (pat == RiverMainPattern.HighlandToLake || pat == RiverMainPattern.BorderToLake ? 22 : 16);
                for (int k = 0; k < tries; k++)
                {
                    if (!TryPickEndpointsForPattern(
                            pat,
                            grid,
                            w,
                            h,
                            rng,
                            config,
                            borderExits,
                            highlandSprings,
                            mountainSprings,
                            interiorBasins,
                            lakeList,
                            out Vector2Int st,
                            out Vector2Int gl,
                            out RiverAnchorKind sk,
                            out RiverAnchorKind gk))
                        continue;
                    if (!SatisfiesPatternSeparation(w, h, st, gl, pat))
                        continue;
                    if (!CellPairIsLand(grid, st, gl))
                        continue;

                    if (TryMainBorderPairEval(
                            grid,
                            w,
                            h,
                            st,
                            gl,
                            pairIndex++,
                            minMain,
                            maxMain,
                            riverSlot,
                            riverAttempt,
                            logRoute,
                            avoidCrossingCorridor,
                            occupiedRiverCells,
                            rng,
                            interiorBasins,
                            out expandedNodes,
                            out finalCost,
                            out sumNearRiverPen,
                            out sumHeightBias,
                            out path,
                            out logStart,
                            out logGoal,
                            out string pairReject,
                            out int rawLen,
                            out bool hadPath,
                            isMainRiver,
                            pat,
                            sk,
                            gk,
                            config))
                    {
                        if (!TryValidateMainRouteForPolicy(config, w, h, logStart, logGoal, path, out string policyReject))
                        {
                            policyRetryCount++;
                            rejectReason = policyReject;
                            if (config.riverMainEndpointAuditEnabled &&
                                (logRoute || config.debugHydrologyNetwork || config.debugLogs))
                            {
                                LogRiverRouteLengthAudit(
                                    config,
                                    riverSlot,
                                    pat,
                                    path,
                                    logStart,
                                    logGoal,
                                    w,
                                    h,
                                    policyRetryCount,
                                    false,
                                    policyReject);
                            }

                            continue;
                        }

                        rejectReason = null;
                        if (grid != null)
                        {
                            grid.HydrologyMainRiverTerminusCell = logGoal;
                            grid.HydrologyMainRiverPattern = pat;
                        }

                        NoteLastMainRouteAnchorsForBorderStretch(pat, sk, gk);

                        if (logRoute || config.debugHydrologyNetwork || config.debugLogs)
                            LogRiverMainPattern(riverSlot, riverAttempt, pat, sk, gk, logStart, logGoal, grid, w, h);

                        if (config.riverMainEndpointAuditEnabled)
                        {
                            string epResult = pat == RiverMainPattern.BorderToBorder
                                ? "accepted_border_to_border"
                                : (config.lakeCount <= 0 ? "accepted_fallback_no_lake" : "accepted");
                            LogRiverRouteLengthAudit(
                                config,
                                riverSlot,
                                pat,
                                path,
                                logStart,
                                logGoal,
                                w,
                                h,
                                policyRetryCount,
                                true,
                                "none");
                            LogRiverEndpointPolicyRoute(
                                config,
                                riverSlot,
                                sk,
                                gk,
                                logStart,
                                logGoal,
                                w,
                                h,
                                epResult);
                        }

                        return true;
                    }
                }
            }

            return false;
        }

        static bool TryBuildMainBorderRouteLegacyBody(
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
            int minMain,
            int maxMain,
            int margin,
            int cornerExcluded,
            int edgeInset,
            out int expandedNodes,
            out float finalCost,
            out float sumNearRiverPen,
            out float sumHeightBias,
            out List<Vector2Int> path,
            out Vector2Int logStart,
            out Vector2Int logGoal,
            out string rejectReason)
        {
            expandedNodes = 0;
            finalCost = 0f;
            sumNearRiverPen = 0f;
            sumHeightBias = 0f;
            path = null;
            logStart = default;
            logGoal = default;
            rejectReason = null;

            Vector2Int lastStart = new Vector2Int(-1, -1);
            Vector2Int lastGoal = new Vector2Int(-1, -1);
            string lastReject = "none";
            int lastRawLen = 0;
            bool lastPathFound = false;

            List<Vector2Int> chosenPath = null;
            Vector2Int chosenStart = default;
            Vector2Int chosenGoal = default;

            for (int pair = 0; pair < RandomAnchorPairsMax; pair++)
            {
                Vector2Int start;
                Vector2Int goal;
                if (!TryPickCentralBorderToBorderFromEdges(grid, w, h, rng, cornerExcluded, edgeInset, out start, out goal))
                {
                    int edgeA = rng.NextInt(0, 4);
                    int edgeB = OppositeEdge(edgeA);
                    if (!TryPickBorderLandAnchor(grid, w, h, rng, edgeA, cornerExcluded, edgeInset, false, out start))
                        continue;
                    if (!TryPickBorderLandAnchor(grid, w, h, rng, edgeB, cornerExcluded, edgeInset, false, out goal))
                        continue;
                }

                if (!SatisfiesLateralSeparation(w, h, start, goal))
                    continue;

                lastStart = start;
                lastGoal = goal;
                if (TryMainBorderPairEval(
                        grid,
                        w,
                        h,
                        start,
                        goal,
                        pair,
                        minMain,
                        maxMain,
                        riverSlot,
                        riverAttempt,
                        logRoute,
                        avoidCrossingCorridor,
                        occupiedRiverCells,
                        rng,
                        null,
                        out expandedNodes,
                        out finalCost,
                        out sumNearRiverPen,
                        out sumHeightBias,
                        out chosenPath,
                        out chosenStart,
                        out chosenGoal,
                        out string pairReject,
                        out int rawLen,
                        out bool hadPath,
                        isMainRiver,
                        RiverMainPattern.BorderToBorder,
                        RiverAnchorKind.BorderExit,
                        RiverAnchorKind.BorderExit,
                        config))
                {
                    path = chosenPath;
                    logStart = chosenStart;
                    logGoal = chosenGoal;
                    rejectReason = null;
                    lastPathFound = true;
                    lastRawLen = rawLen;
                    lastReject = "none";
                    if (grid != null)
                    {
                        grid.HydrologyMainRiverTerminusCell = logGoal;
                        grid.HydrologyMainRiverPattern = RiverMainPattern.BorderToBorder;
                    }

                    NoteLastMainRouteAnchorsForBorderStretch(
                        RiverMainPattern.BorderToBorder,
                        RiverAnchorKind.BorderExit,
                        RiverAnchorKind.BorderExit);

                    if (logRoute || (config != null && (config.debugHydrologyNetwork || config.debugLogs)))
                        LogRiverMainPattern(
                            riverSlot,
                            riverAttempt,
                            RiverMainPattern.BorderToBorder,
                            RiverAnchorKind.BorderExit,
                            RiverAnchorKind.BorderExit,
                            logStart,
                            logGoal,
                            grid,
                            w,
                            h);
                    return true;
                }

                lastPathFound = hadPath;
                lastRawLen = rawLen;
                lastReject = pairReject ?? "no_path";
                rejectReason = lastReject;
            }

            for (int d = 0; d < DeterministicModes; d++)
            {
                if (!TryGetDeterministicBorderPair(grid, w, h, margin, d, out Vector2Int ds, out Vector2Int dg))
                    continue;

                lastStart = ds;
                lastGoal = dg;
                if (TryMainBorderPairEval(
                        grid,
                        w,
                        h,
                        ds,
                        dg,
                        RandomAnchorPairsMax + d,
                        minMain,
                        maxMain,
                        riverSlot,
                        riverAttempt,
                        logRoute,
                        avoidCrossingCorridor,
                        occupiedRiverCells,
                        rng,
                        null,
                        out expandedNodes,
                        out finalCost,
                        out sumNearRiverPen,
                        out sumHeightBias,
                        out chosenPath,
                        out chosenStart,
                        out chosenGoal,
                        out string pairReject,
                        out int rawLen,
                        out bool hadPath,
                        isMainRiver,
                        RiverMainPattern.BorderToBorder,
                        RiverAnchorKind.BorderExit,
                        RiverAnchorKind.BorderExit,
                        config))
                {
                    path = chosenPath;
                    logStart = chosenStart;
                    logGoal = chosenGoal;
                    rejectReason = null;
                    lastPathFound = true;
                    lastRawLen = rawLen;
                    lastReject = "none";
                    if (grid != null)
                    {
                        grid.HydrologyMainRiverTerminusCell = logGoal;
                        grid.HydrologyMainRiverPattern = RiverMainPattern.BorderToBorder;
                    }

                    NoteLastMainRouteAnchorsForBorderStretch(
                        RiverMainPattern.BorderToBorder,
                        RiverAnchorKind.BorderExit,
                        RiverAnchorKind.BorderExit);

                    if (logRoute || (config != null && (config.debugHydrologyNetwork || config.debugLogs)))
                        LogRiverMainPattern(
                            riverSlot,
                            riverAttempt,
                            RiverMainPattern.BorderToBorder,
                            RiverAnchorKind.BorderExit,
                            RiverAnchorKind.BorderExit,
                            logStart,
                            logGoal,
                            grid,
                            w,
                            h);
                    return true;
                }

                lastPathFound = hadPath;
                lastRawLen = rawLen;
                lastReject = pairReject ?? "no_path";
                rejectReason = lastReject;
            }

            path = null;
            logStart = lastStart.x >= 0 ? lastStart : default;
            logGoal = lastGoal.x >= 0 ? lastGoal : default;
            rejectReason = lastPathFound ? lastReject : "no_path";
            if (string.IsNullOrEmpty(rejectReason) || rejectReason == "none")
                rejectReason = "no_main_route_on_land_map";

            if (logRoute)
                LogRiverRouteFatal("no_main_route_on_land_map");

            return false;
        }

        static bool TryBuildMainRiverAnchoredOrLegacy(
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
            int minMain = ResolveMainMinPathCells(config, w, h);
            int maxMain = ResolveMainMaxPathCells(config, w, h);
            int margin = BorderMargin(w, h);
            int cornerExcluded = margin;
            int edgeInset = margin;
            int borderInset = config != null
                ? Mathf.Clamp(config.riverMainBorderExitInsetCells, 0, Mathf.Min(w, h) / 3)
                : 0;

            if (config != null &&
                TryBuildMainRiverTypedAnchorsFirst(
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
                    minMain,
                    maxMain,
                    borderInset,
                    Mathf.Max(cornerExcluded, borderInset),
                    out expandedNodes,
                    out finalCost,
                    out sumNearRiverPen,
                    out sumHeightBias,
                    out path,
                    out logStart,
                    out logGoal,
                    out rejectReason))
                return true;

            return TryBuildMainBorderRouteLegacyBody(
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
                minMain,
                maxMain,
                margin,
                cornerExcluded,
                edgeInset,
                out expandedNodes,
                out finalCost,
                out sumNearRiverPen,
                out sumHeightBias,
                out path,
                out logStart,
                out logGoal,
                out rejectReason);
        }

        public static void PrioritizePlannedLakeSinksNearTerminus(GridSystem grid, Vector2Int goalLand)
        {
            var pl = grid?.PlannedLakeSinkCandidates;
            if (pl == null || pl.Count < 2)
                return;
            int best = 0;
            int bestD = int.MaxValue;
            for (int i = 0; i < pl.Count; i++)
            {
                int d = Mathf.Abs(pl[i].x - goalLand.x) + Mathf.Abs(pl[i].y - goalLand.y);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            if (best != 0)
            {
                Vector2Int tmp = pl[0];
                pl[0] = pl[best];
                pl[best] = tmp;
            }
        }

        public static void LogLakeSinkValidation(
            MapGenConfig config,
            int lakeSlot,
            Vector2Int? plannedSink,
            bool lakeCreated,
            Vector2Int lakeSeed,
            Vector2Int? terminusCell,
            int distTerminusToSeedChebyshev,
            bool riverMouthConnected,
            string fallbackApplied)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            string ps = plannedSink.HasValue ? plannedSink.Value.ToString() : "null";
            string ts = terminusCell.HasValue ? terminusCell.Value.ToString() : "null";
            UnityEngine.Debug.Log(
                $"[LakeSinkValidation] slot={lakeSlot} plannedSink={ps} lakeCreated={(lakeCreated ? 1 : 0)} lakeSeed={lakeSeed} " +
                $"distSinkToLake={distTerminusToSeedChebyshev} riverMouthConnected={(riverMouthConnected ? 1 : 0)} " +
                $"fallbackApplied={fallbackApplied} terminus={ts}");
        }
    }
}
