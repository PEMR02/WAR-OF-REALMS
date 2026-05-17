using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Malla de superficie de río tipo cinta (quad strip) desde grid.RiverCenterlinesCellSpace.
    /// Prep de centerline + ribbon con joins miter limitados; sin geometría desde máscara visual.
    /// </summary>
    public static class RiverSurfaceMeshBuilder
    {
        public static int LastMeshCount { get; private set; }
        public static int LastVertexSum { get; private set; }
        public static int LastTriSum { get; private set; }
        public static int DetachedRiverSurfaceSkips { get; private set; }
        public static int ShortRiverSurfaceSkips { get; private set; }
        public static int RiverSurfaceFragmentCullCount { get; private set; }

        const float DedupeCellEps = 1e-4f;
        const float MinSegmentCellEps = 1e-5f;
        const float CollinearDotThreshold = 0.998f;
        const float JoinAngleSmoothDeg = 22f;
        const float JoinAngleHardDeg = 52f;
        const float MiterLimitMul = 1.25f;
        const float AlignmentMaxDistCells = 2.25f;
        const float ChaikinMaxRiverDistCells = 0.45f;
        const float VisualCenterlineMaxInputMul = 1.75f;
        const float MaxSmoothDeviationCellsDefault = 0.5f;
        const int CrossSectionVertexCount = 5;
        const float MaxWidthStepFracOfBase = 0.2f;

        struct RiverCenterlinePrepStats
        {
            public int RawPts;
            public int DedupPts;
            public int SimplifiedPts;
            public int ResampledPts;
            public int SmoothedPts;
            public int CornerDensePts;
            public float MaxDeviationCells;
        }

        struct RiverJoinStats
        {
            public int Total;
            public int Smooth;
            public int Medium;
            public int Hard;
            public int MiterRejected;
            public float MaxMiterRatio;
        }

        struct RiverSplineBuildStats
        {
            public int RawPts;
            public int SimplifiedPts;
            public int SplinePts;
            public int AnchorsHard;
            public int FordAnchors;
            public int HardBendAnchorCount;
            public int Attempts;
            public float MaxDeviationCells;
            public float MaxActualDeviationCells;
            public float AvgDeviationCells;
            public float MaxAngleStepDeg;
            public bool EndpointStartAtBorder;
            public bool EndpointEndAtBorder;
            public bool BorderExtensionApplied;
            public bool SelfIntersectionDetected;
            public bool Accepted;
            public bool FallbackUsed;
            public string FallbackReason;
        }

        static Material s_cachedRiverSurfaceMaterial;
        static Shader s_cachedRiverSurfaceShader;

        public static void ResetStats()
        {
            LastMeshCount = 0;
            LastVertexSum = 0;
            LastTriSum = 0;
            DetachedRiverSurfaceSkips = 0;
            ShortRiverSurfaceSkips = 0;
            RiverSurfaceFragmentCullCount = 0;
        }

        public static Material GetRiverSurfaceMaterial(MapGenConfig config, Material waterFallback)
        {
            bool forceFlat = config != null &&
                (config.riverSurfaceDebugForceUnlitFlat || config.riverSurfaceDebugFlatMaterial || config.riverSurfaceDebugShowWire);
            if (config != null && forceFlat)
            {
                Shader sd = Shader.Find("Sprites/Default");
                if (sd == null)
                    sd = Shader.Find("UI/Default");
                if (sd == null)
                    sd = Shader.Find("Universal Render Pipeline/Unlit");
                var m = new Material(sd);
                if (m.HasProperty("_Color"))
                    m.SetColor("_Color", new Color(0.2f, 0.45f, 1f, 0.65f));
                else if (m.HasProperty("_BaseColor"))
                    m.SetColor("_BaseColor", new Color(0.2f, 0.45f, 1f, 0.65f));
                m.renderQueue = 3000;
                if (config.debugLogs || config.debugHydrologyNetwork)
                {
                    Debug.Log(
                        $"[RiverSurfaceMaterialDebug] flat=1 material={(m.shader != null ? m.shader.name : "null")} " +
                        $"forceUnlitFlat={(config.riverSurfaceDebugForceUnlitFlat ? 1 : 0)}");
                }

                return m;
            }

            Shader sh = Shader.Find("Project/River Water Simple");
            if (sh != null)
            {
                int createdNew = 0;
                if (s_cachedRiverSurfaceMaterial == null || s_cachedRiverSurfaceShader != sh)
                {
                    s_cachedRiverSurfaceMaterial = new Material(sh);
                    s_cachedRiverSurfaceShader = sh;
                    createdNew = 1;
                }

                if (s_cachedRiverSurfaceMaterial.HasProperty("_BaseColor"))
                    s_cachedRiverSurfaceMaterial.SetColor("_BaseColor", new Color(0.25f, 0.55f, 0.88f, 0.82f));
                if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
                {
                    Debug.Log(
                        $"[RiverSurfaceMaterial] riverId=-1 material={s_cachedRiverSurfaceMaterial.name} " +
                        $"shader={sh.name} createdNewMaterial={createdNew}");
                }

                return s_cachedRiverSurfaceMaterial;
            }

            if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
            {
                Debug.Log(
                    $"[RiverSurfaceMaterial] riverId=-1 material={(waterFallback != null ? waterFallback.name : "null")} " +
                    $"shader={(waterFallback != null && waterFallback.shader != null ? waterFallback.shader.name : "null")} createdNewMaterial=0");
            }

            return waterFallback;
        }

        static void LogRiverGeometryHistoryAudit(MapGenConfig config)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                "[RiverGeometryHistoryAudit] oldCommit=99277d4/56ade5 oldMethod=TryBuildRiverRibbonStripMesh+TangentAt+halfPerVertex " +
                "currentMethod=BuildOrganicVisualRiverCenterline+CrossSectionMesh " +
                "takeFromOld=tangent_avg_prev_next,ribbon_spacing,resample,1-edge-smooth,width_sine_perlin_clamped " +
                "discardFromOld=visual_mask_geometry,2vert_strip_bevel_caps,miter_fans,meander,border_extension");
        }

        static bool IsPointNearFord(GridSystem grid, Vector2 cellPt, int fordDistCells)
        {
            if (grid == null)
                return false;
            int cx = Mathf.Clamp(Mathf.FloorToInt(cellPt.x), 0, grid.Width - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt(cellPt.y), 0, grid.Height - 1);
            return WaterMeshBuilder.GridCellNearFordRiverChebyshev(grid, cx, cy, fordDistCells);
        }

        static List<Vector2> RemoveCollinearPointsCellFordAware(List<Vector2> pts, GridSystem grid, MapGenConfig config)
        {
            if (pts == null || pts.Count < 3)
                return pts;
            int fordD = Mathf.Max(1, config != null ? config.riverVisualFordKeepDistanceCells : 5);
            var r = new List<Vector2>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                if (IsPointNearFord(grid, pts[i], fordD) || i <= 1 || i >= pts.Count - 2)
                {
                    r.Add(pts[i]);
                    continue;
                }

                Vector2 a = r[r.Count - 1];
                Vector2 b = pts[i];
                Vector2 c = pts[i + 1];
                Vector2 d0 = b - a;
                Vector2 d1 = c - b;
                if (d0.sqrMagnitude < 1e-12f || d1.sqrMagnitude < 1e-12f)
                {
                    r.Add(b);
                    continue;
                }

                float dot = Mathf.Clamp(Vector2.Dot(d0.normalized, d1.normalized), -1f, 1f);
                if (dot > CollinearDotThreshold)
                    continue;
                r.Add(b);
            }

            r.Add(pts[pts.Count - 1]);
            return r;
        }

        static List<Vector2> ChaikinOpenCellPreserveEnds(List<Vector2> pts, int passes)
        {
            var cur = new List<Vector2>(pts);
            for (int p = 0; p < passes && cur.Count >= 3; p++)
            {
                Vector2 first = cur[0];
                Vector2 last = cur[cur.Count - 1];
                var next = new List<Vector2>(cur.Count * 2) { first };
                for (int i = 0; i < cur.Count - 1; i++)
                {
                    Vector2 a = cur[i];
                    Vector2 b = cur[i + 1];
                    next.Add(0.75f * a + 0.25f * b);
                    next.Add(0.25f * a + 0.75f * b);
                }

                next.Add(last);
                cur = next;
            }

            return cur;
        }

        static void ApplyMinimalBorderExtensionCell(List<Vector2> pts, int w, int h, float maxCells)
        {
            if (pts == null || pts.Count < 2 || maxCells < 1e-4f)
                return;
            void ExtendEnd(int idx, int innerIdx)
            {
                if (!IsTrueMapEdgeCellSpace(pts[idx], w, h))
                    return;
                Vector2 dir = pts[idx] - pts[innerIdx];
                if (dir.sqrMagnitude < 1e-10f)
                    return;
                dir.Normalize();
                pts[idx] = pts[idx] + dir * maxCells;
            }

            ExtendEnd(0, 1);
            ExtendEnd(pts.Count - 1, pts.Count - 2);
        }

        static float InteriorTurnAngleDeg(List<Vector2> pts, int i)
        {
            if (pts == null || pts.Count < 3 || i <= 0 || i >= pts.Count - 1)
                return 0f;
            Vector2 a = pts[i] - pts[i - 1];
            Vector2 b = pts[i + 1] - pts[i];
            if (a.sqrMagnitude < 1e-12f || b.sqrMagnitude < 1e-12f)
                return 0f;
            return Vector2.Angle(a, b);
        }

        static List<Vector2> ResampleAdaptiveCell(List<Vector2> pts, float baseSpacing, float tightSpacing)
        {
            if (pts == null || pts.Count < 2)
                return pts;
            baseSpacing = Mathf.Clamp(baseSpacing, 0.5f, 1.25f);
            tightSpacing = Mathf.Clamp(tightSpacing, 0.3f, 0.65f);
            var result = new List<Vector2>(pts.Count * 3) { pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float ang = i > 0 ? InteriorTurnAngleDeg(pts, i) : InteriorTurnAngleDeg(pts, i + 1);
                float step = ang >= JoinAngleHardDeg ? tightSpacing : (ang >= JoinAngleSmoothDeg ? Mathf.Lerp(baseSpacing, tightSpacing, 0.5f) : baseSpacing);
                float segLen = Vector2.Distance(pts[i], pts[i + 1]);
                int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / Mathf.Max(0.2f, step)));
                for (int s = 1; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    Vector2 p = Vector2.Lerp(pts[i], pts[i + 1], t);
                    if ((p - result[result.Count - 1]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                        result.Add(p);
                }
            }

            return result.Count >= 2 ? result : pts;
        }

        static List<Vector2> DensifyHardCornersCell(List<Vector2> pts, float tightSpacing)
        {
            if (pts == null || pts.Count < 3)
                return pts;
            var result = new List<Vector2>(pts.Count * 2) { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                float ang = InteriorTurnAngleDeg(pts, i);
                if (ang >= JoinAngleHardDeg)
                {
                    float dBack = Vector2.Distance(pts[i - 1], pts[i]);
                    float dFwd = Vector2.Distance(pts[i], pts[i + 1]);
                    int stepsBack = Mathf.Max(0, Mathf.CeilToInt(dBack / tightSpacing) - 1);
                    int stepsFwd = Mathf.Max(0, Mathf.CeilToInt(dFwd / tightSpacing) - 1);
                    for (int s = stepsBack; s >= 1; s--)
                    {
                        Vector2 p = Vector2.Lerp(pts[i], pts[i - 1], s / (float)(stepsBack + 1));
                        if ((p - result[result.Count - 1]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                            result.Add(p);
                    }

                    if ((pts[i] - result[result.Count - 1]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                        result.Add(pts[i]);
                    for (int s = 1; s <= stepsFwd; s++)
                    {
                        Vector2 p = Vector2.Lerp(pts[i], pts[i + 1], s / (float)(stepsFwd + 1));
                        if ((p - result[result.Count - 1]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                            result.Add(p);
                    }
                }
                else
                {
                    if ((pts[i] - result[result.Count - 1]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                        result.Add(pts[i]);
                }
            }

            if ((pts[pts.Count - 1] - result[result.Count - 1]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                result.Add(pts[pts.Count - 1]);
            return result.Count >= 2 ? result : pts;
        }

        static Vector2 ClampPointToPlayableRect(Vector2 p, int width, int height)
        {
            float minX = 0.5f;
            float maxX = (width - 1) + 0.5f;
            float minZ = 0.5f;
            float maxZ = (height - 1) + 0.5f;
            return new Vector2(
                Mathf.Clamp(p.x, minX, maxX),
                Mathf.Clamp(p.y, minZ, maxZ));
        }

        static void ClampPolylinePlayableCellSpace(List<Vector2> pts, int width, int height)
        {
            if (pts == null)
                return;
            for (int i = 0; i < pts.Count; i++)
                pts[i] = ClampPointToPlayableRect(pts[i], width, height);
        }

        static int CountPointsOutsidePlayableRect(List<Vector2> pts, int width, int height)
        {
            if (pts == null)
                return 0;
            float minX = 0.5f;
            float maxX = (width - 1) + 0.5f;
            float minZ = 0.5f;
            float maxZ = (height - 1) + 0.5f;
            int outside = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 p = pts[i];
                if (p.x < minX - 1e-4f || p.x > maxX + 1e-4f || p.y < minZ - 1e-4f || p.y > maxZ + 1e-4f)
                    outside++;
            }

            return outside;
        }

        static bool MeasureVisualCenterlineRiverAlignment(
            GridSystem grid,
            List<Vector2> visualCells,
            out float maxDistToRiverCell,
            out int pointsFarFromRiver)
        {
            maxDistToRiverCell = 0f;
            pointsFarFromRiver = 0;
            if (grid == null || visualCells == null || visualCells.Count == 0)
                return false;
            int w = grid.Width;
            int h = grid.Height;
            for (int i = 0; i < visualCells.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(visualCells[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(visualCells[i].y), 0, h - 1);
                float d = DistanceToNearestRiverCellChebyshev(grid, cx, cy);
                maxDistToRiverCell = Mathf.Max(maxDistToRiverCell, d);
                if (d > AlignmentMaxDistCells)
                    pointsFarFromRiver++;
            }

            return maxDistToRiverCell <= AlignmentMaxDistCells && pointsFarFromRiver == 0;
        }

        static void LogRiverSurfaceAlignmentFix(
            MapGenConfig config,
            int riverId,
            int inputPts,
            int visualPts,
            float maxDist,
            int pointsFar,
            bool fallbackUsed,
            string fallbackReason)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverSurfaceAlignmentFix] riverId={riverId} input={inputPts} visual={visualPts} " +
                $"maxDistToRiverCell={maxDist:F2} pointsFarFromRiver={pointsFar} fallbackUsed={(fallbackUsed ? 1 : 0)} " +
                $"fallbackReason={(string.IsNullOrEmpty(fallbackReason) ? "none" : fallbackReason)}");
        }

        static void LogRiverSurfaceBorderPolicy(
            MapGenConfig config,
            int riverId,
            bool startAtBorder,
            bool endAtBorder,
            int vertsOutsideAfterClamp)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverSurfaceBorderPolicy] riverId={riverId} borderExtensionDisabled=1 " +
                $"startAtBorder={(startAtBorder ? 1 : 0)} endAtBorder={(endAtBorder ? 1 : 0)} " +
                $"vertsOutsideAfterClamp={vertsOutsideAfterClamp}");
        }

        static List<Vector2> TryChaikinNearRiverCells(GridSystem grid, List<Vector2> pts)
        {
            if (pts == null || pts.Count < 3 || grid == null)
                return pts;
            var smoothed = ChaikinOpenCellPreserveEnds(pts, 1);
            if (PolylineSelfIntersectsXZCell(smoothed))
                return pts;
            int w = grid.Width;
            int h = grid.Height;
            for (int i = 0; i < smoothed.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(smoothed[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(smoothed[i].y), 0, h - 1);
                if (DistanceToNearestRiverCellChebyshev(grid, cx, cy) > ChaikinMaxRiverDistCells)
                    return pts;
            }

            return smoothed;
        }

        static bool ClosestPointOnPolyline2D(Vector2 p, List<Vector2> poly, out Vector2 closest, out float dist)
        {
            closest = p;
            dist = 99f;
            if (poly == null || poly.Count < 2)
                return false;
            for (int i = 0; i < poly.Count - 1; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[i + 1];
                Vector2 ab = b - a;
                float abLen2 = ab.sqrMagnitude;
                Vector2 q = abLen2 < 1e-10f ? a : a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLen2);
                float d = Vector2.Distance(p, q);
                if (d < dist)
                {
                    dist = d;
                    closest = q;
                }
            }

            return dist < 99f;
        }

        static Vector2 SoftProjectTowardPolyline(Vector2 p, List<Vector2> logical, float maxDev)
        {
            if (logical == null || logical.Count < 2 || maxDev <= 1e-5f)
                return p;
            if (!ClosestPointOnPolyline2D(p, logical, out Vector2 closest, out float dist))
                return p;
            if (dist <= maxDev)
                return p;
            float excess = dist - maxDev;
            float pull = 1f - Mathf.Exp(-excess / Mathf.Max(0.2f, maxDev * 0.5f));
            return Vector2.Lerp(p, closest, Mathf.Clamp01(pull));
        }

        static float MaxConsecutiveAngleDeg(List<Vector2> pts)
        {
            if (pts == null || pts.Count < 3)
                return 0f;
            float maxA = 0f;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i] - pts[i - 1];
                Vector2 b = pts[i + 1] - pts[i];
                if (a.sqrMagnitude < 1e-12f || b.sqrMagnitude < 1e-12f)
                    continue;
                maxA = Mathf.Max(maxA, Vector2.Angle(a, b));
            }

            return maxA;
        }

        static List<Vector2> RefineSamplesByMaxAngle(List<Vector2> samples, float maxAngleDeg, int maxIterations = 6)
        {
            if (samples == null || samples.Count < 3 || maxAngleDeg < 1f)
                return samples;
            var pts = new List<Vector2>(samples);
            for (int iter = 0; iter < maxIterations; iter++)
            {
                bool inserted = false;
                for (int i = pts.Count - 2; i >= 1; i--)
                {
                    Vector2 a = pts[i] - pts[i - 1];
                    Vector2 b = pts[i + 1] - pts[i];
                    if (a.sqrMagnitude < 1e-12f || b.sqrMagnitude < 1e-12f)
                        continue;
                    if (Vector2.Angle(a, b) <= maxAngleDeg)
                        continue;
                    Vector2 mid = (pts[i - 1] + pts[i + 1]) * 0.5f;
                    if ((mid - pts[i]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                    {
                        pts.Insert(i + 1, mid);
                        inserted = true;
                    }
                }

                if (!inserted)
                    break;
            }

            return pts;
        }

        static float MeasurePolylineDeviation(List<Vector2> visual, List<Vector2> logical, out float avgDev, out int pointsOverLimit, float limit)
        {
            avgDev = 0f;
            pointsOverLimit = 0;
            if (visual == null || logical == null || visual.Count == 0)
                return 0f;
            float maxD = 0f;
            for (int i = 0; i < visual.Count; i++)
            {
                float d = DistancePointToPolyline2D(visual[i], logical);
                maxD = Mathf.Max(maxD, d);
                avgDev += d;
                if (d > limit)
                    pointsOverLimit++;
            }

            avgDev /= visual.Count;
            return maxD;
        }

        static float CentripetalKnotInterval(Vector2 a, Vector2 b, float alpha)
        {
            float d = Vector2.Distance(a, b);
            return Mathf.Pow(Mathf.Max(d, 1e-4f), alpha);
        }

        static Vector2 CentripetalCatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t, float alpha)
        {
            float t0 = 0f;
            float t1 = t0 + CentripetalKnotInterval(p0, p1, alpha);
            float t2 = t1 + CentripetalKnotInterval(p1, p2, alpha);
            float t3 = t2 + CentripetalKnotInterval(p2, p3, alpha);
            float u = Mathf.Lerp(t1, t2, Mathf.Clamp01(t));
            Vector2 A1 = Vector2.Lerp(p0, p1, t1 > 1e-6f ? (u - t0) / (t1 - t0) : 0f);
            Vector2 A2 = Vector2.Lerp(p1, p2, t2 - t1 > 1e-6f ? (u - t1) / (t2 - t1) : 0f);
            Vector2 A3 = Vector2.Lerp(p2, p3, t3 - t2 > 1e-6f ? (u - t2) / (t3 - t2) : 0f);
            Vector2 B1 = Vector2.Lerp(A1, A2, t2 - t0 > 1e-6f ? (u - t0) / (t2 - t0) : 0f);
            Vector2 B2 = Vector2.Lerp(A2, A3, t3 - t1 > 1e-6f ? (u - t1) / (t3 - t1) : 0f);
            return Vector2.Lerp(B1, B2, t2 - t1 > 1e-6f ? (u - t1) / (t2 - t1) : 0f);
        }

        static void CollectSplineAnchorIndices(
            List<Vector2> control,
            GridSystem grid,
            MapGenConfig config,
            out HashSet<int> hardAnchors,
            out HashSet<int> softAnchors,
            out int fordAnchorCount)
        {
            hardAnchors = new HashSet<int>();
            softAnchors = new HashSet<int>();
            fordAnchorCount = 0;
            if (control == null || control.Count < 2)
                return;
            hardAnchors.Add(0);
            hardAnchors.Add(control.Count - 1);
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            float sharpDeg = config.riverSurfaceSharpBendAngleDeg > 1f ? config.riverSurfaceSharpBendAngleDeg : 70f;
            for (int i = 0; i < control.Count; i++)
            {
                if (IsPointNearFord(grid, control[i], fordD))
                {
                    hardAnchors.Add(i);
                    fordAnchorCount++;
                }
                else if (i > 0 && i < control.Count - 1)
                {
                    float ang = InteriorTurnAngleDeg(control, i);
                    if (ang >= sharpDeg)
                        softAnchors.Add(i);
                }
            }
        }

        static List<Vector2> SampleCentripetalSpline(
            List<Vector2> control,
            float spacing,
            float alpha,
            int maxSamples)
        {
            var samples = new List<Vector2>();
            if (control == null || control.Count < 2)
                return samples;
            if (control.Count < 4)
                return ResampleUniformSpacingCell(control, spacing, maxSamples);

            spacing = Mathf.Max(0.12f, spacing);
            maxSamples = Mathf.Clamp(maxSamples, 2, 16384);
            samples.Add(control[0]);
            for (int i = 0; i < control.Count - 1; i++)
            {
                Vector2 p0 = control[Mathf.Max(0, i - 1)];
                Vector2 p1 = control[i];
                Vector2 p2 = control[i + 1];
                Vector2 p3 = control[Mathf.Min(control.Count - 1, i + 2)];
                float segLen = Vector2.Distance(p1, p2);
                int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / spacing));
                for (int s = 1; s <= steps; s++)
                {
                    if (samples.Count >= maxSamples)
                        break;
                    float t = s / (float)steps;
                    Vector2 p = CentripetalCatmullRom(p0, p1, p2, p3, t, alpha);
                    if ((p - samples[samples.Count - 1]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                        samples.Add(p);
                }

                if (samples.Count >= maxSamples)
                    break;
            }

            if ((samples[samples.Count - 1] - control[control.Count - 1]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                samples.Add(control[control.Count - 1]);
            return samples.Count >= 2 ? samples : control;
        }

        static void EnforceSplineConstraints(
            List<Vector2> samples,
            List<Vector2> logical,
            GridSystem grid,
            MapGenConfig config,
            HashSet<int> hardAnchors,
            List<Vector2> control)
        {
            if (samples == null || logical == null || samples.Count == 0 || config == null)
                return;
            float maxDev = Mathf.Clamp(config.riverSurfaceSplineMaxDeviationCells, 0.1f, 2f);
            float fordRadius = Mathf.Clamp(config.riverSurfaceSplineFordLockRadiusCells, 0f, 4f);
            float endpointLock = Mathf.Clamp(config.riverSurfaceSplineEndpointLockCells, 0f, 2f);
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);

            for (int i = 0; i < samples.Count; i++)
            {
                Vector2 p = samples[i];
                float localMaxDev = maxDev;
                if (IsPointNearFord(grid, p, fordD))
                    localMaxDev *= 0.35f;
                else if (i == 0 || i == samples.Count - 1)
                    localMaxDev = Mathf.Min(localMaxDev, endpointLock > 1e-4f ? endpointLock : maxDev * 0.25f);

                p = SoftProjectTowardPolyline(p, logical, localMaxDev);

                if (i == 0)
                    p = Vector2.Lerp(p, logical[0], endpointLock > 1e-4f ? 0.85f : 1f);
                else if (i == samples.Count - 1)
                    p = Vector2.Lerp(p, logical[logical.Count - 1], endpointLock > 1e-4f ? 0.85f : 1f);
                else if (fordRadius > 1e-4f && control != null)
                {
                    for (int a = 0; a < control.Count; a++)
                    {
                        if (!hardAnchors.Contains(a) || !IsPointNearFord(grid, control[a], fordD))
                            continue;
                        float d = Vector2.Distance(p, control[a]);
                        if (d < fordRadius)
                        {
                            float w = 1f - d / fordRadius;
                            p = Vector2.Lerp(p, control[a], w * 0.65f);
                        }
                    }
                }

                samples[i] = p;
            }
        }

        static void LogRiverSurfaceSpline(MapGenConfig config, int riverId, RiverSplineBuildStats s)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverSurfaceSpline] riverId={riverId} attempts={s.Attempts} accepted={(s.Accepted ? 1 : 0)} " +
                $"fallbackUsed={(s.FallbackUsed ? 1 : 0)} fallbackReason={(string.IsNullOrEmpty(s.FallbackReason) ? "none" : s.FallbackReason)} " +
                $"maxDeviationCells={s.MaxDeviationCells:F3} maxActualDeviationCells={s.MaxActualDeviationCells:F3} " +
                $"selfIntersectionDetected={(s.SelfIntersectionDetected ? 1 : 0)} anchorCount={s.SimplifiedPts} " +
                $"fordAnchorCount={s.FordAnchors} hardBendAnchorCount={s.HardBendAnchorCount} sampleCount={s.SplinePts}");
        }

        static List<Vector2> BuildOrganicVisualRiverCenterline(
            GridSystem grid,
            List<Vector2> rawPath,
            MapGenConfig config,
            int riverIndex,
            out int fordCellsNear,
            out RiverCenterlinePrepStats prepStats,
            out RiverSplineBuildStats splineStats)
        {
            return BuildSplineVisualCenterlineFromLogical(
                grid,
                rawPath,
                config,
                riverIndex,
                out fordCellsNear,
                out prepStats,
                out splineStats);
        }

        static List<Vector2> BuildSplineVisualCenterlineFromLogical(
            GridSystem grid,
            List<Vector2> rawPath,
            MapGenConfig config,
            int riverIndex,
            out int fordCellsNear,
            out RiverCenterlinePrepStats prepStats,
            out RiverSplineBuildStats splineStats)
        {
            fordCellsNear = 0;
            prepStats = default;
            splineStats = default;
            if (rawPath == null || rawPath.Count < 2)
                return null;

            prepStats.RawPts = rawPath.Count;
            splineStats.RawPts = rawPath.Count;
            var logical = new List<Vector2>(rawPath);
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            int maxPts = Mathf.Max(2, Mathf.CeilToInt(rawPath.Count * VisualCenterlineMaxInputMul));

            var control = new List<Vector2>(rawPath);
            control = DedupeConsecutiveCell(control, DedupeCellEps);
            control = RemoveNearNullSegmentsCell(control, MinSegmentCellEps);
            control = RemoveCollinearPointsCellFordAware(control, grid, config);
            if (control == null || control.Count < 2)
                return null;

            CollectSplineAnchorIndices(control, grid, config, out HashSet<int> hardAnchors, out HashSet<int> softAnchors, out int fordAnchors);
            splineStats.AnchorsHard = hardAnchors.Count;
            splineStats.FordAnchors = fordAnchors;
            splineStats.SimplifiedPts = control.Count;
            int hardBends = 0;
            for (int hi = 1; hi + 1 < control.Count; hi++)
            {
                if (InteriorTurnAngleDeg(control, hi) >= JoinAngleHardDeg)
                    hardBends++;
            }

            splineStats.HardBendAnchorCount = hardBends;

            float baseSpacing = config.riverSurfaceSplineSampleSpacingCells > 0.01f
                ? config.riverSurfaceSplineSampleSpacingCells
                : 0.4f;
            baseSpacing = Mathf.Clamp(baseSpacing, 0.35f, 0.45f);
            float alpha = 0.5f;
            float tension = Mathf.Clamp01(config.riverSurfaceSplineTension);
            float maxAngle = Mathf.Clamp(config.riverSurfaceSplineMaxAngleStepDeg, 12f, 45f);
            float baseMaxDev = Mathf.Clamp(config.riverSurfaceSplineMaxDeviationCells, 0.1f, 1.35f);
            float[] devTries = { baseMaxDev, 0.9f, 0.55f };
            float[] spacingMul = { 1f, 1.12f, 1.28f };

            List<Vector2> samples = null;
            float maxDevLimitUsed = baseMaxDev;
            for (int att = 0; att < devTries.Length; att++)
            {
                splineStats.Attempts = att + 1;
                maxDevLimitUsed = devTries[att];
                float spacing = baseSpacing * spacingMul[att] * Mathf.Lerp(1.1f, 0.9f, tension);
                var attemptSamples = SampleCentripetalSpline(control, spacing, alpha, maxPts);
                attemptSamples = RefineSamplesByMaxAngle(attemptSamples, maxAngle);
                EnforceSplineConstraints(attemptSamples, logical, grid, config, hardAnchors, control);
                splineStats.MaxAngleStepDeg = MaxConsecutiveAngleDeg(attemptSamples);

                bool invalid = false;
                for (int i = 0; i < attemptSamples.Count; i++)
                {
                    if (float.IsNaN(attemptSamples[i].x) || float.IsNaN(attemptSamples[i].y))
                        invalid = true;
                }

                if (invalid)
                {
                    splineStats.SelfIntersectionDetected = false;
                    splineStats.FallbackReason = "nan";
                    continue;
                }

                if (PolylineSelfIntersectsXZCell(attemptSamples))
                {
                    splineStats.SelfIntersectionDetected = true;
                    continue;
                }

                float maxDev = MeasurePolylineDeviation(attemptSamples, logical, out float avgDev, out int overLimit, maxDevLimitUsed);
                if (maxDev > maxDevLimitUsed + 0.01f || splineStats.MaxAngleStepDeg > maxAngle + 2f)
                    continue;

                samples = attemptSamples;
                splineStats.SplinePts = samples.Count;
                splineStats.MaxDeviationCells = maxDevLimitUsed;
                splineStats.MaxActualDeviationCells = maxDev;
                splineStats.AvgDeviationCells = avgDev;
                splineStats.Accepted = true;
                splineStats.FallbackUsed = false;
                splineStats.SelfIntersectionDetected = false;
                splineStats.FallbackReason = null;
                break;
            }

            if (samples == null)
            {
                splineStats.Accepted = false;
                splineStats.FallbackUsed = true;
                if (string.IsNullOrEmpty(splineStats.FallbackReason))
                    splineStats.FallbackReason = splineStats.SelfIntersectionDetected ? "self_intersection" : "deviation_or_angle";
                LogRiverSurfaceSpline(config, riverIndex, splineStats);
                return null;
            }

            prepStats.DedupPts = control.Count;
            prepStats.SimplifiedPts = control.Count;
            prepStats.ResampledPts = samples.Count;
            prepStats.SmoothedPts = prepStats.CornerDensePts = samples.Count;
            prepStats.MaxDeviationCells = splineStats.MaxActualDeviationCells;

            int outsideBefore = CountPointsOutsidePlayableRect(samples, grid.Width, grid.Height);
            ClampPolylinePlayableCellSpace(samples, grid.Width, grid.Height);
            int outsideAfter = CountPointsOutsidePlayableRect(samples, grid.Width, grid.Height);

            splineStats.EndpointStartAtBorder = IsTrueMapEdgeCellSpace(samples[0], grid.Width, grid.Height);
            splineStats.EndpointEndAtBorder = IsTrueMapEdgeCellSpace(samples[samples.Count - 1], grid.Width, grid.Height);
            splineStats.BorderExtensionApplied = !config.riverSurfaceDisableBorderExtension &&
                config.riverSurfaceBorderExtendMaxCells > 1e-4f;

            LogRiverSurfaceSpline(config, riverIndex, splineStats);
            LogRiverSurfaceBorderPolicy(config, riverIndex, splineStats.EndpointStartAtBorder, splineStats.EndpointEndAtBorder, outsideAfter);

            for (int i = 0; i < samples.Count; i++)
            {
                if (IsPointNearFord(grid, samples[i], fordD))
                    fordCellsNear++;
            }

            if (outsideBefore > 0 && (config.debugLogs || config.debugHydrologyNetwork))
            {
                Debug.LogWarning(
                    $"[RiverSurfaceAlignment] riverId={riverIndex} pointsOutsideMap={outsideBefore} clampedTo={outsideAfter}");
            }

            return samples;
        }

        static List<Vector2> BuildVisualCenterlineSimple(
            GridSystem grid,
            List<Vector2> rawPath,
            MapGenConfig config,
            int maxPts,
            bool allowChaikin)
        {
            if (rawPath == null || rawPath.Count < 2)
                return null;
            var pts = new List<Vector2>(rawPath);
            pts = DedupeConsecutiveCell(pts, DedupeCellEps);
            pts = RemoveNearNullSegmentsCell(pts, MinSegmentCellEps);
            pts = RemoveCollinearPointsCellFordAware(pts, grid, config);
            if (pts == null || pts.Count < 2)
                return null;

            float spacing = config.riverSurfaceVisualSpacingCells > 0.01f
                ? config.riverSurfaceVisualSpacingCells
                : 1f;
            spacing = Mathf.Clamp(spacing, 0.75f, 1f);
            maxPts = Mathf.Clamp(maxPts, 2, 8192);
            pts = ResampleUniformSpacingCell(pts, spacing, maxPts);
            if (allowChaikin)
                pts = TryChaikinNearRiverCells(grid, pts);
            if (pts.Count > maxPts)
                pts = ResampleUniformSpacingCell(pts, spacing, maxPts);
            return pts != null && pts.Count >= 2 ? pts : null;
        }

        static void LogRiverSurfaceCenterlinePrep(MapGenConfig config, int riverId, RiverCenterlinePrepStats s)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverSurfaceCenterlinePrep] riverId={riverId} rawPts={s.RawPts} dedupPts={s.DedupPts} simplifiedPts={s.SimplifiedPts} " +
                $"resampledPts={s.ResampledPts} smoothedPts={s.SmoothedPts} cornerDensePts={s.CornerDensePts} " +
                $"maxDeviationCells={s.MaxDeviationCells:F3}");
        }

        static List<Vector2> BuildVisualCenterlineFromLogical(
            GridSystem grid,
            List<Vector2> rawPath,
            MapGenConfig config,
            int riverIndex,
            out int fordCellsNear,
            out RiverCenterlinePrepStats prepStats)
        {
            fordCellsNear = 0;
            prepStats = default;
            if (rawPath == null || rawPath.Count < 2)
                return null;

            List<Vector2> pts;
            bool fallbackUsed = false;
            string fallbackReason = null;
            var logicalRef = new List<Vector2>(rawPath);
            float devLimit = config.riverSurfaceUseSplineVisualCenterline
                ? Mathf.Clamp(config.riverSurfaceSplineMaxDeviationCells, 0.1f, 2f)
                : AlignmentMaxDistCells;

            if (config.riverSurfaceUseSplineVisualCenterline)
            {
                pts = BuildOrganicVisualRiverCenterline(
                    grid,
                    rawPath,
                    config,
                    riverIndex,
                    out fordCellsNear,
                    out prepStats,
                    out RiverSplineBuildStats splineStats);
                if (pts == null || pts.Count < 2)
                {
                    fallbackUsed = true;
                    splineStats.FallbackUsed = true;
                    fallbackReason = string.IsNullOrEmpty(splineStats.FallbackReason)
                        ? "spline_rejected"
                        : splineStats.FallbackReason;
                    int maxPts = Mathf.Max(2, Mathf.CeilToInt(rawPath.Count * VisualCenterlineMaxInputMul));
                    pts = BuildVisualCenterlineSimple(grid, rawPath, config, maxPts, allowChaikin: false);
                    fordCellsNear = 0;
                    prepStats = default;
                    prepStats.RawPts = rawPath.Count;
                }
            }
            else
            {
                int maxPts = Mathf.Max(2, Mathf.CeilToInt(rawPath.Count * VisualCenterlineMaxInputMul));
                pts = BuildVisualCenterlineSimple(grid, rawPath, config, maxPts, allowChaikin: true);
                prepStats.RawPts = rawPath.Count;
                prepStats.DedupPts = prepStats.SimplifiedPts = prepStats.ResampledPts = pts != null ? pts.Count : 0;
                if (pts != null)
                {
                    float maxDev = MeasurePolylineDeviation(pts, logicalRef, out _, out int over, devLimit);
                    if (over > 0 || maxDev > devLimit)
                    {
                        fallbackUsed = true;
                        fallbackReason = "alignment_failed";
                        pts = BuildVisualCenterlineSimple(grid, rawPath, config, maxPts, allowChaikin: false);
                    }
                }
            }

            if (pts == null || pts.Count < 2)
                return null;

            float maxDist = MeasurePolylineDeviation(pts, logicalRef, out float avgDist, out int pointsOver, devLimit);
            int pointsOutside = CountPointsOutsidePlayableRect(pts, grid.Width, grid.Height);
            int fordSamples = 0;
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            for (int i = 0; i < pts.Count; i++)
            {
                if (IsPointNearFord(grid, pts[i], fordD))
                    fordSamples++;
            }

            ClampPolylinePlayableCellSpace(pts, grid.Width, grid.Height);
            pointsOutside = CountPointsOutsidePlayableRect(pts, grid.Width, grid.Height);

            bool accepted = pointsOver == 0 && pointsOutside == 0 && maxDist <= devLimit;
            LogRiverSurfaceAlignmentFix(
                config,
                riverIndex,
                rawPath.Count,
                pts.Count,
                maxDist,
                pointsOver,
                fallbackUsed,
                fallbackReason);
            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[RiverSurfaceAlignment] riverId={riverIndex} samples={pts.Count} maxDeviationCells={maxDist:F3} " +
                    $"avgDeviationCells={avgDist:F3} pointsOverLimit={pointsOver} pointsOutsideMap={pointsOutside} " +
                    $"fordSamples={fordSamples} accepted={(accepted ? 1 : 0)}");
            }

            prepStats.SmoothedPts = prepStats.CornerDensePts = pts.Count;
            prepStats.MaxDeviationCells = maxDist;
            LogRiverSurfaceCenterlinePrep(config, riverIndex, prepStats);

            if (fordCellsNear == 0)
            {
                for (int i = 0; i < pts.Count; i++)
                {
                    if (IsPointNearFord(grid, pts[i], fordD))
                        fordCellsNear++;
                }
            }

            if (config.riverSurfaceUseSplineVisualCenterline && !fallbackUsed && (pointsOver > 0 || maxDist > devLimit + 0.01f))
            {
                fallbackUsed = true;
                fallbackReason = "spline_alignment";
                LogRiverSurfaceSpline(config, riverIndex, new RiverSplineBuildStats
                {
                    RawPts = rawPath.Count,
                    FallbackUsed = true,
                    FallbackReason = fallbackReason,
                    Accepted = false
                });
                int maxPtsFb = Mathf.Max(2, Mathf.CeilToInt(rawPath.Count * VisualCenterlineMaxInputMul));
                pts = BuildVisualCenterlineSimple(grid, rawPath, config, maxPtsFb, allowChaikin: false);
                if (pts != null)
                {
                    ClampPolylinePlayableCellSpace(pts, grid.Width, grid.Height);
                    maxDist = MeasurePolylineDeviation(pts, logicalRef, out avgDist, out pointsOver, devLimit);
                    pointsOutside = CountPointsOutsidePlayableRect(pts, grid.Width, grid.Height);
                }
            }

            return pts;
        }

        static void LogRiverSurfaceSource(GridSystem grid, MapGenConfig config, int riverId, List<Vector2> raw)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            int unique = 0;
            var hs = new HashSet<long>();
            int fordNear = 0;
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            if (raw != null)
            {
                for (int i = 0; i < raw.Count; i++)
                {
                    int cx = Mathf.FloorToInt(raw[i].x);
                    int cy = Mathf.FloorToInt(raw[i].y);
                    long k = PackCellKey(cx, cy);
                    if (hs.Add(k))
                        unique++;
                    if (IsPointNearFord(grid, raw[i], fordD))
                        fordNear++;
                }
            }

            Debug.Log(
                $"[RiverSurfaceSource] riverId={riverId} source=RiverCenterlinesCellSpace inputPoints={(raw != null ? raw.Count : 0)} " +
                $"uniqueCells={unique} fordCellsNear={fordNear} usesVisualMaskAsGeometry=0");
        }

        static void LogRiverSurfaceAlignment(GridSystem grid, MapGenConfig config, int riverId, List<Vector2> visualCells)
        {
            if (config == null || grid == null || visualCells == null ||
                (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            int w = grid.Width;
            int h = grid.Height;
            float maxD = 0f;
            float sumD = 0f;
            int far = 0;
            int fordZones = 0;
            int fordAligned = 0;
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            for (int i = 0; i < visualCells.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(visualCells[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(visualCells[i].y), 0, h - 1);
                float d = DistanceToNearestRiverCellChebyshev(grid, cx, cy);
                maxD = Mathf.Max(maxD, d);
                sumD += d;
                if (d > AlignmentMaxDistCells)
                    far++;
                if (IsPointNearFord(grid, visualCells[i], fordD + 1))
                {
                    fordZones++;
                    if (d <= 1.05f)
                        fordAligned++;
                }
            }

            float avg = visualCells.Count > 0 ? sumD / visualCells.Count : 0f;
            Debug.Log(
                $"[RiverSurfaceAlignment] riverId={riverId} visualPoints={visualCells.Count} maxDistToRiverCell={maxD:F2} " +
                $"avgDistToRiverCell={avg:F2} pointsFarFromRiver={far} fordZonesChecked={fordZones} fordZonesAligned={fordAligned}");
        }

        static float DistanceToNearestRiverCellChebyshev(GridSystem grid, int cx, int cy)
        {
            if (grid == null)
                return 99f;
            if (grid.GetCell(cx, cy).type == CellType.River)
                return 0f;
            int w = grid.Width;
            int h = grid.Height;
            float best = 99f;
            for (int r = 1; r <= 4; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = cx + dx;
                        int nz = cy + dz;
                        if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                            continue;
                        if (grid.GetCell(nx, nz).type != CellType.River)
                            continue;
                        float d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
                        if (d < best)
                            best = d;
                    }
                }

                if (best < 99f)
                    break;
            }

            return best;
        }

        static void ResolveRiverSurfaceWidthBands(
            MapGenConfig config,
            float baseHalfW,
            out float minHalfW,
            out float normalHalfW,
            out float maxHalfW)
        {
            minHalfW = Mathf.Max(0.02f, baseHalfW);
            float normalMul = config != null
                ? Mathf.Clamp(config.riverSurfaceVisualNormalWidthMul, 1.25f, 3f)
                : 2f;
            float maxMul = config != null
                ? Mathf.Clamp(config.riverSurfaceVisualMaxWidthMul, normalMul, 4f)
                : 3f;
            normalHalfW = minHalfW * normalMul;
            maxHalfW = minHalfW * maxMul;
        }

        static List<float> BuildOrganicHalfWidths(
            List<Vector2> cellPath,
            float baseHalfW,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            out float hwMin,
            out float hwMax,
            out float maxDeltaStep,
            out int fordDampApplied)
        {
            fordDampApplied = 0;
            maxDeltaStep = 0f;
            ResolveRiverSurfaceWidthBands(config, baseHalfW, out float minHalfW, out float normalHalfW, out float maxHalfW);
            hwMin = minHalfW;
            hwMax = maxHalfW;
            int n = cellPath != null ? cellPath.Count : 0;
            var hw = new List<float>(n);
            if (n < 1)
                return hw;

            float totalLen = PolylineLengthCellSpace(cellPath);
            float acc = 0f;
            float phase = riverIndex * 17.31f + config.seed * 0.013f;
            float organicFrac = Mathf.Clamp(config.riverSurfaceWidthOrganicVarFrac, 0f, 0.2f);
            float sineAmp = organicFrac;
            float noiseAmp = organicFrac * 0.5f;
            float noiseScale = Mathf.Max(0.002f, config.riverSurfaceWidthNoiseScale);
            float fordMinW = minHalfW * Mathf.Clamp(config.riverSurfaceFordMinWidthMul, 1f, 1.25f);
            float fordMaxW = minHalfW * Mathf.Clamp(config.riverSurfaceFordMaxWidthMul, config.riverSurfaceFordMinWidthMul, 1.35f);
            float fade = 0.1f;
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            float swing = Mathf.Max(0.01f, maxHalfW - minHalfW);
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                    acc += Vector2.Distance(cellPath[i], cellPath[i - 1]);
                float t01 = totalLen > 1e-4f ? acc / totalLen : 0f;
                float endFade = MeanderEdgeFade(t01, fade);
                float ang = InteriorTurnAngleDeg(cellPath, i);
                float bendFade = ang >= JoinAngleHardDeg ? 0.55f : (ang >= JoinAngleSmoothDeg ? 0.8f : 1f);
                float sine = Mathf.Sin(acc * 0.11f + phase) * sineAmp;
                float n01 = Mathf.PerlinNoise(acc * noiseScale + phase, riverIndex * 0.17f);
                float noise = (n01 * 2f - 1f) * noiseAmp;
                float wv = normalHalfW + (sine + noise) * swing * 0.5f * endFade * bendFade;
                wv = Mathf.Clamp(wv, minHalfW, maxHalfW);
                if (IsPointNearFord(grid, cellPath[i], fordD))
                {
                    wv = Mathf.Clamp(wv, fordMinW, fordMaxW);
                    fordDampApplied++;
                }

                hw.Add(Mathf.Max(0.02f, wv));
            }

            if (n >= 5)
            {
                var smoothed = new List<float>(hw);
                for (int i = 2; i < n - 2; i++)
                {
                    smoothed[i] = (hw[i - 2] + hw[i - 1] + hw[i] * 2f + hw[i + 1] + hw[i + 2]) * 0.16666667f;
                }

                for (int i = 1; i < n - 1; i++)
                    smoothed[i] = (smoothed[i - 1] + smoothed[i] * 2f + smoothed[i + 1]) * 0.25f;
                hw = smoothed;
            }
            else if (n >= 3)
            {
                var smoothed = new List<float>(hw);
                for (int i = 1; i < n - 1; i++)
                    smoothed[i] = (hw[i - 1] + hw[i] * 2f + hw[i + 1]) * 0.25f;
                hw = smoothed;
            }

            float maxStepAllowed = swing * 0.14f;
            for (int i = 1; i < n; i++)
            {
                float step = hw[i] - hw[i - 1];
                if (Mathf.Abs(step) > maxStepAllowed)
                    hw[i] = hw[i - 1] + Mathf.Sign(step) * maxStepAllowed;
            }

            float avg = 0f;
            for (int i = 0; i < n; i++)
            {
                hwMin = Mathf.Min(hwMin, hw[i]);
                hwMax = Mathf.Max(hwMax, hw[i]);
                avg += hw[i];
                if (i > 0)
                    maxDeltaStep = Mathf.Max(maxDeltaStep, Mathf.Abs(hw[i] - hw[i - 1]));
            }

            if (n > 0)
                avg /= n;
            LogRiverSurfaceWidthScale(
                config,
                riverIndex,
                baseHalfW,
                minHalfW,
                normalHalfW,
                maxHalfW,
                hwMin,
                hwMax,
                avg,
                maxDeltaStep,
                fordDampApplied);

            return hw;
        }

        static void LogRiverSurfaceWidthScale(
            MapGenConfig config,
            int riverId,
            float oldBaseHalfWidth,
            float minHalfWidth,
            float normalHalfWidth,
            float maxHalfWidth,
            float finalMinHalfWidth,
            float finalMaxHalfWidth,
            float avgHalfWidth,
            float maxWidthStep,
            int fordDampApplied)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            bool widthOk = finalMinHalfWidth >= oldBaseHalfWidth - 0.001f &&
                finalMaxHalfWidth <= maxHalfWidth + 0.001f &&
                normalHalfWidth >= oldBaseHalfWidth * 1.99f;
            Debug.Log(
                $"[RiverSurfaceWidthScale] riverId={riverId} oldBaseHalfWidth={oldBaseHalfWidth:F3} finalMinHalfWidth={finalMinHalfWidth:F3} " +
                $"normalHalfWidth={normalHalfWidth:F3} finalMaxHalfWidth={finalMaxHalfWidth:F3} avgHalfWidth={avgHalfWidth:F3} " +
                $"minObservedHalfWidth={finalMinHalfWidth:F3} maxObservedHalfWidth={finalMaxHalfWidth:F3} maxWidthStep={maxWidthStep:F4} " +
                $"fordDampApplied={fordDampApplied} widthPolicy=current_is_min_normal_2x_max_3x accepted={(widthOk ? 1 : 0)}");
        }

        static void LogRiverSurfaceWidth(
            MapGenConfig config,
            int riverId,
            float baseHalfW,
            float hwMin,
            float hwMax,
            float maxDeltaStep,
            int fordDampApplied)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            ResolveRiverSurfaceWidthBands(config, baseHalfW, out float minH, out float normalH, out float maxH);
            float avg = (hwMin + hwMax) * 0.5f;
            LogRiverSurfaceWidthScale(config, riverId, baseHalfW, minH, normalH, maxH, hwMin, hwMax, avg, maxDeltaStep, fordDampApplied);
        }

        static void ApplyRiverBankNoise(
            List<Vector3> center,
            List<Vector3> left,
            List<Vector3> right,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSizeWorld,
            out float maxEdgeDelta)
        {
            maxEdgeDelta = 0f;
            if (config == null || center == null || left == null || right == null || cellPath == null)
                return;
            if (center.Count != left.Count || center.Count != right.Count || center.Count != cellPath.Count)
                return;

            float ampCells = Mathf.Clamp(config.riverSurfaceBankNoiseAmpCells, 0f, 0.35f);
            if (ampCells < 1e-5f)
                return;

            float ampWorld = ampCells * Mathf.Max(0.01f, cellSizeWorld);
            float lenCells = Mathf.Max(4f, config.riverSurfaceBankNoiseLengthCells);
            float phase = riverIndex * 9.17f + config.seed * 0.021f;
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            int n = center.Count;
            float acc = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                    acc += Vector2.Distance(cellPath[i], cellPath[i - 1]);
                float t01 = n > 1 ? i / (float)(n - 1) : 0f;
                float edgeFade = MeanderEdgeFade(t01, 0.12f);
                if (i == 0 || i == n - 1 || IsPointNearFord(grid, cellPath[i], fordD))
                    edgeFade *= 0.15f;

                Vector3 tan = TangentNormalize(center, i);
                Vector3 nrm = PerpendicularXZ(tan);
                float nL = (Mathf.PerlinNoise(acc / lenCells + phase, 0.11f) * 2f - 1f) * ampWorld * edgeFade;
                float nR = (Mathf.PerlinNoise(acc / lenCells + phase + 17.3f, 0.23f) * 2f - 1f) * ampWorld * edgeFade;
                left[i] += nrm * nL;
                right[i] -= nrm * nR;
                maxEdgeDelta = Mathf.Max(maxEdgeDelta, Mathf.Abs(nL));
                maxEdgeDelta = Mathf.Max(maxEdgeDelta, Mathf.Abs(nR));
            }

            if (n >= 3)
            {
                for (int pass = 0; pass < 1; pass++)
                {
                    var lTmp = new Vector3[n];
                    var rTmp = new Vector3[n];
                    lTmp[0] = left[0];
                    rTmp[0] = right[0];
                    lTmp[n - 1] = left[n - 1];
                    rTmp[n - 1] = right[n - 1];
                    for (int i = 1; i < n - 1; i++)
                    {
                        lTmp[i] = (left[i - 1] + left[i] * 2f + left[i + 1]) * 0.25f;
                        rTmp[i] = (right[i - 1] + right[i] * 2f + right[i + 1]) * 0.25f;
                    }

                    left.Clear();
                    right.Clear();
                    for (int i = 0; i < n; i++)
                    {
                        left.Add(lTmp[i]);
                        right.Add(rTmp[i]);
                    }
                }
            }
        }

        static void LogRiverSurfaceBanks(
            MapGenConfig config,
            int riverId,
            float leftNoiseAmp,
            float rightNoiseAmp,
            float maxBankStep)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverSurfaceBanks] riverId={riverId} leftNoiseAmp={leftNoiseAmp:F3} rightNoiseAmp={rightNoiseAmp:F3} " +
                $"bankSmoothPasses=1 maxBankStep={maxBankStep:F4}");
        }

        static void LogRiverSurfaceMeshBuild(
            MapGenConfig config,
            int riverId,
            int sections,
            int verts,
            int tris,
            bool visibleDebugWire)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverSurfaceMeshBuild] riverId={riverId} sections={sections} verts={verts} tris={tris} " +
                $"crossSectionVerts={CrossSectionVertexCount} usesFans=0 usesBevelCaps=0 usesWaterChunks=0 " +
                $"visibleDebugWire={(visibleDebugWire ? 1 : 0)}");
        }

        static float ComputeEndpointTaperMul(
            int index,
            int count,
            MapGenConfig config,
            bool startAtBorder,
            bool endAtBorder,
            bool skipEndBlend)
        {
            if (config == null || count < 2)
                return 1f;
            int taperCells = Mathf.Clamp(config.riverSurfaceInteriorEndpointTaperCells, 3, 12);
            float minMul = Mathf.Clamp(config.riverSurfaceInteriorEndpointMinWidthMul, 1f, 1.25f);
            float mul = 1f;
            if (!startAtBorder && index < taperCells)
            {
                float t = index / (float)taperCells;
                mul = Mathf.Min(mul, Mathf.SmoothStep(minMul, 1f, t));
            }

            if (!endAtBorder && !skipEndBlend && index >= count - taperCells)
            {
                float t = (count - 1 - index) / (float)taperCells;
                mul = Mathf.Min(mul, Mathf.SmoothStep(minMul, 1f, t));
            }

            return mul;
        }

        static void AddCrossSectionQuad(List<int> tris, int a0, int a1, int b0, int b1)
        {
            AddTriStripWinding(tris, a0, b0, a1);
            AddTriStripWinding(tris, a1, b0, b1);
        }

        static void BuildCrossSectionRiverMesh(
            List<Vector3> center,
            List<float> halfWidthWorld,
            List<Vector2> cellSpaceLine,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSizeWorld,
            float uvScale,
            bool startAtBorder,
            bool endAtBorder,
            bool skipEndBlend,
            out List<Vector3> verts,
            out List<Vector2> uvs,
            out List<Vector3> normals,
            out List<Vector4> tangents,
            out List<int> tris,
            out float maxSegBuilt,
            out float maxBankStep)
        {
            verts = new List<Vector3>();
            uvs = new List<Vector2>();
            normals = new List<Vector3>();
            tangents = new List<Vector4>();
            tris = new List<int>();
            maxSegBuilt = 0f;
            maxBankStep = 0f;
            int n = center.Count;
            if (n < 2 || halfWidthWorld.Count != n)
                return;

            float innerFrac = config != null
                ? Mathf.Clamp(config.riverSurfaceInnerBankWidthFrac, 0.4f, 0.75f)
                : 0.58f;
            float bankAmpWorld = config != null
                ? Mathf.Clamp(config.riverSurfaceBankNoiseAmpCells, 0f, 0.35f) * Mathf.Max(0.01f, cellSizeWorld)
                : 0f;
            float bankLen = config != null ? Mathf.Max(4f, config.riverSurfaceBankNoiseLengthCells) : 22f;
            float phase = riverIndex * 9.17f + (config != null ? config.seed * 0.021f : 0f);
            int fordD = config != null ? Mathf.Max(1, config.riverVisualFordKeepDistanceCells) : 5;
            float[] accV = BuildAccumulatedV(center);
            float maxLeftNoise = 0f;
            float maxRightNoise = 0f;

            for (int i = 0; i < n; i++)
            {
                Vector3 c = center[i];
                Vector3 tan = TangentNormalize(center, i);
                Vector3 nrm = PerpendicularXZ(tan);
                float hw = Mathf.Max(0.02f, halfWidthWorld[i]) * ComputeEndpointTaperMul(
                    i, n, config, startAtBorder, endAtBorder, skipEndBlend);

                float leftMul = 1f;
                float rightMul = 1f;
                if (i > 0 && i < n - 1)
                {
                    Vector3 tin = center[i] - center[i - 1];
                    Vector3 tout = center[i + 1] - center[i];
                    tin.y = tout.y = 0f;
                    float cross = tin.x * tout.z - tin.z * tout.x;
                    if (Mathf.Abs(cross) > 1e-6f)
                    {
                        if (cross > 0f)
                        {
                            leftMul = 1.06f;
                            rightMul = 0.94f;
                        }
                        else
                        {
                            leftMul = 0.94f;
                            rightMul = 1.06f;
                        }
                    }
                }

                float edgeFade = MeanderEdgeFade(n > 1 ? i / (float)(n - 1) : 0f, 0.12f);
                if (i == 0 || i == n - 1 || (cellSpaceLine != null && IsPointNearFord(grid, cellSpaceLine[i], fordD)))
                    edgeFade *= 0.12f;

                float acc = i > 0 ? accV[i] : 0f;
                float nL = (Mathf.PerlinNoise(acc / bankLen + phase, 0.11f) * 2f - 1f) * bankAmpWorld * edgeFade;
                float nR = (Mathf.PerlinNoise(acc / bankLen + phase + 17.3f, 0.23f) * 2f - 1f) * bankAmpWorld * edgeFade;
                maxLeftNoise = Mathf.Max(maxLeftNoise, Mathf.Abs(nL));
                maxRightNoise = Mathf.Max(maxRightNoise, Mathf.Abs(nR));
                maxBankStep = Mathf.Max(maxBankStep, Mathf.Max(Mathf.Abs(nL), Mathf.Abs(nR)));

                float hwL = hw * leftMul;
                float hwR = hw * rightMul;
                Vector3 lb = c - nrm * hwL + nrm * nL;
                Vector3 li = c - nrm * (hwL * innerFrac) + nrm * (nL * 0.55f);
                Vector3 ri = c + nrm * (hwR * innerFrac) - nrm * (nR * 0.55f);
                Vector3 rb = c + nrm * hwR - nrm * nR;

                verts.Add(lb);
                verts.Add(li);
                verts.Add(c);
                verts.Add(ri);
                verts.Add(rb);
                float v = acc * uvScale;
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(0.25f, v));
                uvs.Add(new Vector2(0.5f, v));
                uvs.Add(new Vector2(0.75f, v));
                uvs.Add(new Vector2(1f, v));
                for (int k = 0; k < CrossSectionVertexCount; k++)
                {
                    normals.Add(Vector3.up);
                    tangents.Add(new Vector4(1f, 0f, 0f, 1f));
                }

                if (i < n - 1)
                {
                    Vector3 d = center[i + 1] - center[i];
                    d.y = 0f;
                    maxSegBuilt = Mathf.Max(maxSegBuilt, d.magnitude);
                }
            }

            for (int i = 0; i < n - 1; i++)
            {
                int rowA = i * CrossSectionVertexCount;
                int rowB = (i + 1) * CrossSectionVertexCount;
                for (int q = 0; q < CrossSectionVertexCount - 1; q++)
                {
                    AddCrossSectionQuad(tris, rowA + q, rowA + q + 1, rowB + q, rowB + q + 1);
                }
            }

            if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
            {
                LogRiverSurfaceBanks(
                    config,
                    riverIndex,
                    maxLeftNoise,
                    maxRightNoise,
                    maxBankStep);
            }
        }

        static Vector2 DirXZTo2(Vector3 d)
        {
            d.y = 0f;
            if (d.sqrMagnitude < 1e-12f)
                return Vector2.right;
            d.Normalize();
            return new Vector2(d.x, d.z);
        }

        static Vector2 NormalLeft2(Vector2 dirNorm)
        {
            return new Vector2(-dirNorm.y, dirNorm.x);
        }

        static void BuildRibbonSidesMiterLimited(
            List<Vector3> center,
            List<float> halfWidth,
            out List<Vector3> left,
            out List<Vector3> right,
            out RiverJoinStats joinStats)
        {
            joinStats = default;
            int n = center != null ? center.Count : 0;
            left = new List<Vector3>(n);
            right = new List<Vector3>(n);
            if (n < 2 || halfWidth == null || halfWidth.Count != n)
                return;

            joinStats.Total = n;
            for (int i = 0; i < n; i++)
            {
                float hw = Mathf.Max(0.02f, halfWidth[i]);
                float y = center[i].y;
                Vector2 c = new Vector2(center[i].x, center[i].z);
                Vector2 miter;
                float scale = hw;

                if (i == 0)
                {
                    Vector2 dir = DirXZTo2(center[1] - center[0]);
                    miter = NormalLeft2(dir);
                    joinStats.Smooth++;
                }
                else if (i == n - 1)
                {
                    Vector2 dir = DirXZTo2(center[n - 1] - center[n - 2]);
                    miter = NormalLeft2(dir);
                    joinStats.Smooth++;
                }
                else
                {
                    Vector2 dirIn = DirXZTo2(center[i] - center[i - 1]);
                    Vector2 dirOut = DirXZTo2(center[i + 1] - center[i]);
                    float ang = Vector2.Angle(dirIn, dirOut);
                    Vector2 nIn = NormalLeft2(dirIn);
                    Vector2 nOut = NormalLeft2(dirOut);
                    miter = nIn + nOut;
                    float miterLen = miter.magnitude;
                    if (miterLen < 1e-6f)
                    {
                        miter = nIn;
                        scale = hw;
                        joinStats.Smooth++;
                    }
                    else
                    {
                        miter /= miterLen;
                        float dot = Vector2.Dot(miter, nIn);
                        scale = hw / Mathf.Max(0.15f, Mathf.Abs(dot));
                        float ratio = scale / Mathf.Max(1e-4f, hw);
                        joinStats.MaxMiterRatio = Mathf.Max(joinStats.MaxMiterRatio, ratio);
                        if (ang < JoinAngleSmoothDeg)
                            joinStats.Smooth++;
                        else if (ang < JoinAngleHardDeg)
                            joinStats.Medium++;
                        else
                            joinStats.Hard++;

                        if (scale > hw * MiterLimitMul)
                        {
                            joinStats.MiterRejected++;
                            scale = hw;
                            miter = nIn + nOut;
                            if (miter.sqrMagnitude < 1e-8f)
                                miter = nIn;
                            else
                                miter.Normalize();
                        }
                    }
                }

                left.Add(new Vector3(c.x - miter.x * scale, y, c.y - miter.y * scale));
                right.Add(new Vector3(c.x + miter.x * scale, y, c.y + miter.y * scale));
            }
        }

        static void LogRiverSurfaceJoinStats(MapGenConfig config, int riverId, RiverJoinStats s)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverSurfaceJoinStats] riverId={riverId} total={s.Total} smooth={s.Smooth} medium={s.Medium} hard={s.Hard} " +
                $"miterRejected={s.MiterRejected} maxMiterRatio={s.MaxMiterRatio:F3}");
        }

        static void DrawRiverSurfaceDebugLines(
            MapGenConfig config,
            List<Vector3> center,
            List<Vector3> left,
            List<Vector3> right)
        {
            if (config == null || center == null || left == null || right == null)
                return;
            bool drawCl = config.riverSurfaceDebugDrawCenterline;
            bool drawEd = config.riverSurfaceDebugDrawEdges;
            bool drawJn = config.riverSurfaceDebugDrawJoinNormals;
            if (!drawCl && !drawEd && !drawJn)
                return;

            const float dur = 45f;
            int n = Mathf.Min(center.Count, Mathf.Min(left.Count, right.Count));
            if (drawCl)
            {
                for (int i = 0; i < n - 1; i++)
                    Debug.DrawLine(center[i] + Vector3.up * 0.05f, center[i + 1] + Vector3.up * 0.05f, Color.cyan, dur);
            }

            if (drawEd)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    Debug.DrawLine(left[i] + Vector3.up * 0.06f, left[i + 1] + Vector3.up * 0.06f, Color.green, dur);
                    Debug.DrawLine(right[i] + Vector3.up * 0.06f, right[i + 1] + Vector3.up * 0.06f, Color.yellow, dur);
                }
            }

            if (drawJn)
            {
                for (int i = 1; i < n - 1; i++)
                {
                    Vector3 mid = (left[i] + right[i]) * 0.5f;
                    Vector3 nrm = mid - center[i];
                    nrm.y = 0f;
                    if (nrm.sqrMagnitude > 1e-8f)
                        Debug.DrawLine(center[i] + Vector3.up * 0.08f, center[i] + nrm.normalized * 0.6f + Vector3.up * 0.08f, Color.magenta, dur);
                }
            }
        }

        static int MinChebyshevDistToMapEdge(Vector2 p, int w, int h)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, w - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, h - 1);
            return Mathf.Min(Mathf.Min(cx, w - 1 - cx), Mathf.Min(cy, h - 1 - cy));
        }

        static void LogRiverSurfaceEndpoint(
            MapGenConfig config,
            int riverId,
            List<Vector2> cellLine,
            int w,
            int h,
            bool startAtBorder,
            bool endAtBorder,
            bool startCap,
            bool endCap,
            float baseHalfW,
            List<float> halfWidths)
        {
            if (config == null || cellLine == null || cellLine.Count < 1 ||
                (!config.debugLogs && !config.debugHydrologyNetwork))
                return;

            int startDist = MinChebyshevDistToMapEdge(cellLine[0], w, h);
            int endDist = MinChebyshevDistToMapEdge(cellLine[cellLine.Count - 1], w, h);
            float startWMul = 1f;
            float endWMul = 1f;
            if (halfWidths != null && halfWidths.Count > 0 && baseHalfW > 1e-5f)
            {
                startWMul = halfWidths[0] / baseHalfW;
                endWMul = halfWidths[halfWidths.Count - 1] / baseHalfW;
            }

            string startMode = startAtBorder ? "BorderFlatCut" : (startCap ? "InteriorTaper" : "ConfluenceBlend");
            string endMode = endAtBorder ? "BorderFlatCut" : (endCap ? "InteriorTaper" : "ConfluenceBlend");
            int warnInterior = config.lakeCount <= 0 && (!startAtBorder || !endAtBorder) ? 1 : 0;
            float minMul = Mathf.Clamp(config.riverSurfaceInteriorEndpointMinWidthMul, 1f, 1.25f);
            Debug.Log(
                $"[RiverSurfaceEndpoint] riverId={riverId} startMode={startMode} endMode={endMode} " +
                $"startAtBorder={(startAtBorder ? 1 : 0)} endAtBorder={(endAtBorder ? 1 : 0)} startDistBorder={startDist} endDistBorder={endDist} " +
                $"startWidthMul={startWMul:F3} endWidthMul={endWMul:F3} flatCut={(startAtBorder || endAtBorder ? 1 : 0)} " +
                $"taperApplied={((!startAtBorder && startCap) || (!endAtBorder && endCap) ? 1 : 0)} " +
                $"warningInteriorEndpointNoLake={warnInterior} endpointMinWidthMul={minMul:F2} " +
                "createdRoundCap=0 createdLargeBevel=0");
        }

        public static bool BuildRiverSurfaces(
            Transform parent,
            GridSystem grid,
            MapGenConfig config,
            Material waterMaterial,
            float riverSurfaceWorldY,
            float cellSize,
            int waterLayer)
        {
            ResetStats();
            if (parent == null || grid == null || config == null)
                return false;
            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return false;

            LogRiverGeometryHistoryAudit(config);

            Material mat = GetRiverSurfaceMaterial(config, waterMaterial);
            if (mat == null)
                return false;

            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[RiverSurfaceDebug] flatMaterial={(config.riverSurfaceDebugFlatMaterial ? 1 : 0)} wire={(config.riverSurfaceDebugShowWire ? 1 : 0)} " +
                    $"shaderName={(mat.shader != null ? mat.shader.name : "null")} materialFallback={(mat == waterMaterial ? 1 : 0)} yOffset={riverSurfaceWorldY:F3}");
            }

            Vector3 origin = grid.Origin;
            float inset = Mathf.Max(0f, config.riverVisualBankInset);
            int w = grid.Width;
            int h = grid.Height;
            bool any = false;
            bool logRm = config.debugLogs || config.debugHydrologyNetwork;

            for (int riverIndex = 0; riverIndex < grid.RiverCenterlinesCellSpace.Count; riverIndex++)
            {
                var rawPath = grid.RiverCenterlinesCellSpace[riverIndex];
                if (rawPath == null || rawPath.Count < 2)
                    continue;

                LogRiverSurfaceSource(grid, config, riverIndex, rawPath);

                var cellProcessed = BuildVisualCenterlineFromLogical(
                    grid,
                    rawPath,
                    config,
                    riverIndex,
                    out int fordNearBuild,
                    out RiverCenterlinePrepStats prepStats);
                if (cellProcessed == null || cellProcessed.Count < 2)
                    continue;

                if (riverIndex > 0 && TryCullTributarySurfacePiece(grid, cellProcessed, riverIndex, config, logRm))
                    continue;

                LogRiverSurfaceAlignment(grid, config, riverIndex, cellProcessed);

                float fullCellsW = riverIndex == 0
                    ? config.riverVisualRibbonFullWidthCellsMain
                    : (config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                        ? config.riverVisualRibbonFullWidthCellsTributary
                        : config.riverVisualRibbonFullWidthCellsMain);
                float baseHalfW = fullCellsW > 0.01f
                    ? Mathf.Max(0.08f, fullCellsW * 0.5f * cellSize - inset)
                    : Mathf.Max(0.08f, config.riverVisualMeshHalfWidth - inset);

                var worldCenters = CellPolylineToWorldXZ(cellProcessed, origin, cellSize, riverSurfaceWorldY);
                if (worldCenters.Count < 2)
                    continue;

                List<float> halfWidths = BuildOrganicHalfWidths(
                    cellProcessed,
                    baseHalfW,
                    grid,
                    config,
                    riverIndex,
                    out float hwMin,
                    out float hwMax,
                    out float maxDeltaStep,
                    out int fordDamp);
                if (halfWidths.Count != worldCenters.Count)
                    continue;

                bool startAtBorder = IsTrueMapEdgeCellSpace(cellProcessed[0], w, h);
                bool endAtBorder = IsTrueMapEdgeCellSpace(cellProcessed[cellProcessed.Count - 1], w, h);
                if (logRm)
                {
                    var src = new Vector2Int(Mathf.RoundToInt(cellProcessed[0].x), Mathf.RoundToInt(cellProcessed[0].y));
                    var dst = new Vector2Int(
                        Mathf.RoundToInt(cellProcessed[cellProcessed.Count - 1].x),
                        Mathf.RoundToInt(cellProcessed[cellProcessed.Count - 1].y));
                    RiverRouteGenerator.LogRiverBorderPolicy(
                        config,
                        startAtBorder ? RiverAnchorKind.BorderExit : RiverAnchorKind.HighlandSpring,
                        endAtBorder ? RiverAnchorKind.BorderExit : RiverAnchorKind.LakeSink,
                        src,
                        dst,
                        w,
                        h,
                        Mathf.Clamp(config.riverMainBorderExitInsetCells, 0, 48),
                        Mathf.Max(0, config.riverMainMaxBorderPathExtensionCells),
                        config.riverMainBorderExitInsetCells == 0 && config.riverMainMaxBorderPathExtensionCells == 0 ? 1 : 0,
                        meshReachesBorder: (startAtBorder || endAtBorder) ? 1 : 0,
                        terrainCarveReachesBorder: -1);
                }
                bool endCap = !endAtBorder && !(riverIndex > 0 && config.riverSurfaceSkipTributaryConfluenceCap);
                LogRiverSurfaceEndpoint(
                    config,
                    riverIndex,
                    cellProcessed,
                    w,
                    h,
                    startAtBorder,
                    endAtBorder,
                    startCap: !startAtBorder,
                    endCap: endCap,
                    baseHalfW,
                    halfWidths);

                if (logRm)
                {
                    bool debugWire = config.riverSurfaceDebugShowWire ||
                        config.riverSurfaceDebugDrawCenterline ||
                        config.riverSurfaceDebugDrawEdges;
                    Debug.Log(
                        $"[RiverSurfaceMaterial] materialName={(mat != null ? mat.name : "null")} " +
                        $"shaderName={(mat != null && mat.shader != null ? mat.shader.name : "null")} " +
                        $"isInstance={(mat != null && mat.name.Contains("Instance") ? 1 : 0)} debugWire={(debugWire ? 1 : 0)}");
                }

                string goName = riverIndex == 0
                    ? "Water_RiverSurface_Main"
                    : $"Water_RiverSurface_Tributary_{riverIndex}";
                if (TryBuildStripMeshWithCaps(
                        parent,
                        worldCenters,
                        halfWidths,
                        mat,
                        waterLayer,
                        goName,
                        config.riverSurfaceMeshUvScale,
                        cellSize,
                        config,
                        riverIndex,
                        w,
                        h,
                        cellProcessed,
                        grid,
                        origin,
                        centerlinePreClipped: true,
                        out int verts,
                        out int tris,
                        out float maxSegBuilt))
                {
                    any = true;
                    LastMeshCount++;
                    if (logRm)
                    {
                        Debug.Log(
                            $"[RiverSurfaceMesh] riverIndex={riverIndex} centerlineInput={rawPath.Count} centerlineUsed={cellProcessed.Count} " +
                            $"verts={verts} tris={tris} maxSegmentLength={maxSegBuilt:F3} fordNear={fordNearBuild} source=RiverCenterlinesCellSpace");
                    }
                }
            }

            return any;
        }

        /// <summary>Centerline en espacio celda: dedupe, segmentos nulos, colineales, quiebres, Chaikin opcional, remuestreo.</summary>
        static List<Vector2> ProcessCenterlineCellSpace(
            List<Vector2> cellPath,
            MapGenConfig config,
            out int afterColinear,
            out int afterSmooth,
            out int afterResample)
        {
            afterColinear = afterSmooth = afterResample = 0;
            var pts = new List<Vector2>(cellPath);
            pts = DedupeConsecutiveCell(pts, DedupeCellEps);
            pts = RemoveNearNullSegmentsCell(pts, MinSegmentCellEps);
            pts = RemoveCollinearPointsCell(pts, CollinearDotThreshold);
            afterColinear = pts != null ? pts.Count : 0;
            if (pts == null || pts.Count < 2)
                return null;

            pts = InsertSharpBendMidpointsCell(pts, config.riverSurfaceSharpBendAngleDeg);

            int chaikinPasses = Mathf.Clamp(config.riverSurfaceChaikinPasses, 0, 2);
            if (chaikinPasses > 0)
            {
                var chaikin = ChaikinOpenCell(pts, chaikinPasses);
                if (!PolylineSelfIntersectsXZCell(chaikin))
                    pts = chaikin;
            }

            afterSmooth = pts.Count;

            int pathCells = cellPath.Count;
            int maxPts = Mathf.Max(8, Mathf.CeilToInt(pathCells * Mathf.Max(1.02f, config.riverSurfaceMaxVisualPointRatio)));
            float spacing = Mathf.Max(0.08f, config.riverSurfaceSampleSpacingCells);
            pts = ResampleUniformSpacingCell(pts, spacing, maxPts);
            afterResample = pts != null ? pts.Count : 0;

            return pts;
        }

        static List<float> BuildHalfWidthsDeterministic(
            List<Vector3> worldCenters,
            float baseHalfW,
            float amp,
            float noiseScale,
            int riverIndex)
        {
            int n = worldCenters.Count;
            var hw = new List<float>(n);
            float acc = 0f;
            float yHash = riverIndex * 12.9898f;
            for (int i = 0; i < n; i++)
            {
                float n01 = Mathf.PerlinNoise(acc * noiseScale + yHash, yHash * 0.071f);
                float mul = 1f;
                if (amp > 1e-6f)
                    mul = Mathf.Clamp(1f + amp * (n01 * 2f - 1f), 1f - amp, 1f + amp);
                hw.Add(Mathf.Max(0.02f, baseHalfW * mul));
                if (i < n - 1)
                {
                    Vector3 d = worldCenters[i + 1] - worldCenters[i];
                    d.y = 0f;
                    acc += d.magnitude;
                }
            }

            return hw;
        }

        static long PackCellKey(int x, int y) => ((long)x << 20) ^ (y & 0xfffff);

        static HashSet<long> BuildJoinProximityCellKeys(
            List<List<Vector2>> lines,
            int skipIndex,
            int w,
            int h)
        {
            var hs = new HashSet<long>();
            if (lines == null)
                return hs;
            for (int li = 0; li < lines.Count; li++)
            {
                if (li == skipIndex)
                    continue;
                var ln = lines[li];
                if (ln == null)
                    continue;
                for (int k = 0; k < ln.Count; k++)
                {
                    int cx = Mathf.Clamp(Mathf.FloorToInt(ln[k].x), 0, w - 1);
                    int cy = Mathf.Clamp(Mathf.FloorToInt(ln[k].y), 0, h - 1);
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx;
                            int ny = cy + dz;
                            if ((uint)nx < (uint)w && (uint)ny < (uint)h)
                                hs.Add(PackCellKey(nx, ny));
                        }
                    }
                }
            }

            return hs;
        }

        static float MapBorderWidthFade01(float cellX, float cellY, int w, int h, float bandCells)
        {
            float west = 0.5f;
            float east = (w - 1) + 0.5f;
            float south = 0.5f;
            float north = (h - 1) + 0.5f;
            float mx = Mathf.Min(cellX - west, east - cellX);
            float my = Mathf.Min(cellY - south, north - cellY);
            float d = Mathf.Max(0f, Mathf.Min(mx, my));
            return Mathf.Clamp01(d / Mathf.Max(0.25f, bandCells));
        }

        static float BendWidthDampeningAtIndex(List<Vector3> pts, int i, float thrDeg)
        {
            if (pts == null || i < 1 || i >= pts.Count - 1)
                return 1f;
            Vector3 a = pts[i] - pts[i - 1];
            Vector3 b = pts[i + 1] - pts[i];
            a.y = 0f;
            b.y = 0f;
            if (a.sqrMagnitude < 1e-14f || b.sqrMagnitude < 1e-14f)
                return 1f;
            float ang = Vector3.Angle(a, b);
            if (ang >= thrDeg + 14f)
                return 0.26f;
            if (ang >= thrDeg)
                return 0.48f;
            if (ang >= thrDeg - 14f)
                return 0.68f;
            return 1f;
        }

        static List<float> BuildMainRiverHalfWidthsWithArcVariation(
            GridSystem grid,
            List<Vector3> worldCenters,
            List<Vector2> cellSpace,
            float baseHalfW,
            float perlinAmp,
            float noiseScale,
            HashSet<long> joinCells,
            MapGenConfig config,
            out float minW,
            out float maxW)
        {
            minW = float.MaxValue;
            maxW = 0f;
            int n = worldCenters != null ? worldCenters.Count : 0;
            var hw = new List<float>(n);
            if (n < 1 || grid == null || config == null || cellSpace == null || cellSpace.Count != n)
            {
                var fb = BuildHalfWidthsDeterministic(worldCenters, baseHalfW, perlinAmp, noiseScale, 0);
                minW = maxW = baseHalfW;
                if (fb != null && fb.Count > 0)
                {
                    minW = maxW = fb[0];
                    for (int z = 1; z < fb.Count; z++)
                    {
                        minW = Mathf.Min(minW, fb[z]);
                        maxW = Mathf.Max(maxW, fb[z]);
                    }
                }

                return fb;
            }

            int w = grid.Width;
            int h = grid.Height;
            float acc = 0f;
            float yHash = 12.9898f;
            float maxFrac = Mathf.Clamp(config.riverSurfaceMainArcWidthVarMaxFrac, 0f, 0.12f);
            float invLen = Mathf.Max(0.002f, config.riverSurfaceMainArcWidthVarInvLengthWorld);
            bool arcOn = config.riverSurfaceMainArcWidthVarEnabled && maxFrac > 1e-6f;
            float bendThr = Mathf.Clamp(config.riverSurfaceSharpBendAngleDeg - 6f, 35f, 95f);
            const float mapBorderBandCells = 4f;
            for (int i = 0; i < n; i++)
            {
                float n01 = Mathf.PerlinNoise(acc * noiseScale + yHash, yHash * 0.071f);
                float mulP = 1f;
                float perlinUse = arcOn ? perlinAmp * 0.55f : perlinAmp;
                if (perlinUse > 1e-6f)
                    mulP = Mathf.Clamp(1f + perlinUse * (n01 * 2f - 1f), 1f - perlinUse, 1f + perlinUse);

                float arcMul = 1f;
                if (arcOn)
                {
                    float phase = acc * invLen * Mathf.PI * 2f * 0.88f;
                    arcMul = 1f + maxFrac * Mathf.Sin(phase);
                    float t01 = n > 1 ? i / (float)(n - 1) : 0f;
                    float endFade = MeanderEdgeFade(t01, 0.12f);
                    arcMul = 1f + (arcMul - 1f) * endFade;
                    float bendD = BendWidthDampeningAtIndex(worldCenters, i, bendThr);
                    arcMul = Mathf.Lerp(1f, arcMul, bendD);

                    int cx = Mathf.Clamp(Mathf.FloorToInt(cellSpace[i].x), 0, w - 1);
                    int cy = Mathf.Clamp(Mathf.FloorToInt(cellSpace[i].y), 0, h - 1);
                    float fordBlend = 1f;
                    ref var c0 = ref grid.GetCell(cx, cy);
                    if (c0.riverFord)
                        fordBlend = 0.22f;
                    else
                    {
                        foreach (var nb in grid.Neighbors4(cx, cy))
                        {
                            if (grid.GetCell(nb.x, nb.y).riverFord)
                            {
                                fordBlend = 0.32f;
                                break;
                            }
                        }
                    }

                    arcMul = Mathf.Lerp(1f, arcMul, fordBlend);
                    if (joinCells != null && joinCells.Contains(PackCellKey(cx, cy)))
                        arcMul = Mathf.Lerp(1f, arcMul, 0.28f);
                    arcMul = Mathf.Clamp(arcMul, 1f - maxFrac, 1f + maxFrac);
                }

                int cxw = Mathf.Clamp(Mathf.FloorToInt(cellSpace[i].x), 0, w - 1);
                int cyw = Mathf.Clamp(Mathf.FloorToInt(cellSpace[i].y), 0, h - 1);
                int fordDistW = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
                if (WaterMeshBuilder.GridCellNearFordRiverChebyshev(grid, cxw, cyw, fordDistW))
                {
                    mulP = Mathf.Lerp(1f, mulP, 0.4f);
                    arcMul = Mathf.Lerp(1f, arcMul, 0.4f);
                }

                float borderFade = MapBorderWidthFade01(cellSpace[i].x, cellSpace[i].y, w, h, mapBorderBandCells);
                float mulPVis = Mathf.Lerp(1f, mulP, borderFade);
                float arcMulVis = Mathf.Lerp(1f, arcMul, borderFade);
                ResolveRiverSurfaceWidthBands(config, baseHalfW, out float minHwL, out float normalHwL, out float maxHwL);
                float wv = Mathf.Max(0.02f, normalHwL * mulPVis * arcMulVis);
                wv = Mathf.Clamp(wv, minHwL, maxHwL);
                hw.Add(wv);
                minW = Mathf.Min(minW, wv);
                maxW = Mathf.Max(maxW, wv);
                if (i < n - 1)
                {
                    Vector3 d = worldCenters[i + 1] - worldCenters[i];
                    d.y = 0f;
                    acc += d.magnitude;
                }
            }

            return hw;
        }

        static void MeasureSharpBends(List<Vector3> pts, float thresholdDeg, out int sharpCount, out float maxAngleDeg)
        {
            sharpCount = 0;
            maxAngleDeg = 0f;
            if (pts == null || pts.Count < 3)
                return;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i] - pts[i - 1];
                Vector3 b = pts[i + 1] - pts[i];
                a.y = 0f;
                b.y = 0f;
                if (a.sqrMagnitude < 1e-12f || b.sqrMagnitude < 1e-12f)
                    continue;
                float ang = Vector3.Angle(a, b);
                maxAngleDeg = Mathf.Max(maxAngleDeg, ang);
                if (ang > thresholdDeg + 1e-3f)
                    sharpCount++;
            }
        }

        static List<Vector2> InsertSharpBendMidpointsCell(List<Vector2> pts, float thresholdDeg)
        {
            if (pts == null || pts.Count < 3)
                return pts;
            var dst = new List<Vector2>(pts.Count + 8) { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i] - pts[i - 1];
                Vector2 b = pts[i + 1] - pts[i];
                if (a.sqrMagnitude < 1e-14f || b.sqrMagnitude < 1e-14f)
                {
                    dst.Add(pts[i]);
                    continue;
                }

                float ang = Vector2.Angle(a, b);
                dst.Add(pts[i]);
                if (ang > thresholdDeg + 0.01f)
                {
                    Vector2 cut = Vector2.Lerp(pts[i], (pts[i - 1] + pts[i + 1]) * 0.5f, 0.42f);
                    dst.Add(cut);
                }
            }

            dst.Add(pts[pts.Count - 1]);
            return dst;
        }

        static List<Vector2> ChaikinOpenCell(List<Vector2> p, int passes)
        {
            var cur = new List<Vector2>(p);
            for (int pass = 0; pass < passes; pass++)
            {
                if (cur.Count < 2)
                    break;
                var nxt = new List<Vector2>(cur.Count * 2 + 2);
                nxt.Add(cur[0]);
                for (int i = 0; i < cur.Count - 1; i++)
                {
                    nxt.Add(Vector2.Lerp(cur[i], cur[i + 1], 0.25f));
                    nxt.Add(Vector2.Lerp(cur[i], cur[i + 1], 0.75f));
                }

                nxt.Add(cur[cur.Count - 1]);
                cur = nxt;
            }

            return cur;
        }

        static bool PolylineSelfIntersectsXZCell(List<Vector2> poly)
        {
            if (poly == null || poly.Count < 4)
                return false;
            int n = poly.Count;
            for (int i = 0; i < n - 1; i++)
            {
                for (int j = i + 2; j < n - 1; j++)
                {
                    if (SegmentsIntersect2D(poly[i], poly[i + 1], poly[j], poly[j + 1]))
                        return true;
                }
            }

            return false;
        }

        static bool SegmentsIntersect2D(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            float d = (p2.x - p1.x) * (p4.y - p3.y) - (p2.y - p1.y) * (p4.x - p3.x);
            if (Mathf.Abs(d) < 1e-10f)
                return false;
            float t = ((p3.x - p1.x) * (p4.y - p3.y) - (p3.y - p1.y) * (p4.x - p3.x)) / d;
            float u = ((p3.x - p1.x) * (p2.y - p1.y) - (p3.y - p1.y) * (p2.x - p1.x)) / d;
            return t > 1e-5f && t < 1f - 1e-5f && u > 1e-5f && u < 1f - 1e-5f;
        }

        static List<Vector2> ResampleUniformSpacingCell(List<Vector2> src, float spacingCells, int maxPts)
        {
            if (src == null || src.Count < 2)
                return src;
            maxPts = Mathf.Clamp(maxPts, 2, 8192);
            spacingCells = Mathf.Max(0.05f, spacingCells);

            var cum = new float[src.Count];
            cum[0] = 0f;
            for (int i = 1; i < src.Count; i++)
            {
                float d = Vector2.Distance(src[i], src[i - 1]);
                cum[i] = cum[i - 1] + d;
            }

            float L = cum[src.Count - 1];
            if (L < 1e-6f)
                return src;

            int target = Mathf.Max(2, Mathf.CeilToInt(L / spacingCells) + 1);
            if (target > maxPts)
            {
                spacingCells = L / (maxPts - 1);
                target = maxPts;
            }

            target = Mathf.Min(target, maxPts);
            var dst = new List<Vector2>(target);
            for (int i = 0; i < target; i++)
            {
                float t = (i / (float)Mathf.Max(1, target - 1)) * L;
                int j = 0;
                while (j < cum.Length - 1 && cum[j + 1] < t)
                    j++;
                float seg = Mathf.Max(1e-8f, cum[j + 1] - cum[j]);
                float u = (t - cum[j]) / seg;
                dst.Add(Vector2.Lerp(src[j], src[j + 1], Mathf.Clamp01(u)));
            }

            return dst;
        }

        static float MaxSegmentLengthCell(List<Vector2> pts)
        {
            if (pts == null || pts.Count < 2)
                return 0f;
            float m = 0f;
            for (int i = 1; i < pts.Count; i++)
                m = Mathf.Max(m, Vector2.Distance(pts[i], pts[i - 1]));
            return m;
        }

        static List<Vector2> DedupeConsecutiveCell(List<Vector2> pts, float eps)
        {
            if (pts == null || pts.Count < 2)
                return pts;
            float e2 = eps * eps;
            var r = new List<Vector2>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                if ((pts[i] - r[r.Count - 1]).sqrMagnitude > e2)
                    r.Add(pts[i]);
            }

            return r;
        }

        static List<Vector2> RemoveNearNullSegmentsCell(List<Vector2> pts, float eps)
        {
            if (pts == null || pts.Count < 2)
                return pts;
            float e2 = eps * eps;
            var r = new List<Vector2>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                if ((pts[i] - r[r.Count - 1]).sqrMagnitude >= e2)
                    r.Add(pts[i]);
            }

            return r;
        }

        static List<Vector2> RemoveCollinearPointsCell(List<Vector2> pts, float dotThresh)
        {
            if (pts == null || pts.Count < 3)
                return pts;
            var r = new List<Vector2>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector2 a = r[r.Count - 1];
                Vector2 b = pts[i];
                Vector2 c = pts[i + 1];
                Vector2 d0 = b - a;
                Vector2 d1 = c - b;
                float m0 = d0.sqrMagnitude;
                float m1 = d1.sqrMagnitude;
                if (m0 < 1e-12f || m1 < 1e-12f)
                {
                    r.Add(b);
                    continue;
                }

                float dot = Mathf.Clamp(Vector2.Dot(d0.normalized, d1.normalized), -1f, 1f);
                if (dot > dotThresh)
                    continue;
                r.Add(b);
            }

            r.Add(pts[pts.Count - 1]);
            return r;
        }

        static List<Vector3> CellPolylineToWorldXZ(List<Vector2> cellPath, Vector3 origin, float cellSize, float yWorld)
        {
            var r = new List<Vector3>(cellPath.Count);
            for (int i = 0; i < cellPath.Count; i++)
            {
                var c = cellPath[i];
                float wx = origin.x + c.x * cellSize;
                float wz = origin.z + c.y * cellSize;
                r.Add(new Vector3(wx, yWorld, wz));
            }

            return r;
        }

        static int CountProximityWarnings(List<Vector3> c, float threshold)
        {
            if (c == null || c.Count < 4)
                return 0;
            float th2 = threshold * threshold;
            int w = 0;
            for (int i = 0; i < c.Count; i++)
            {
                int jMax = Mathf.Min(c.Count - 2, i + 48);
                for (int j = i + 2; j <= jMax; j++)
                {
                    float d2 = PointSegmentDistSqXZ(c[i], c[j], c[j + 1]);
                    if (d2 < th2)
                        w++;
                }
            }

            return w;
        }

        static float PointSegmentDistSqXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 ab = new Vector2(b.x - a.x, b.z - a.z);
            float den = Vector2.Dot(ab, ab);
            if (den < 1e-8f)
            {
                float dx = p.x - a.x;
                float dz = p.z - a.z;
                return dx * dx + dz * dz;
            }

            Vector2 ap = new Vector2(p.x - a.x, p.z - a.z);
            float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / den);
            float qx = a.x + ab.x * t;
            float qz = a.z + ab.y * t;
            float dx2 = p.x - qx;
            float dz2 = p.z - qz;
            return dx2 * dx2 + dz2 * dz2;
        }

        static float MaxSegmentLengthXZ(List<Vector3> pts)
        {
            if (pts == null || pts.Count < 2)
                return 0f;
            float m = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector3 d = pts[i] - pts[i - 1];
                d.y = 0f;
                m = Mathf.Max(m, d.magnitude);
            }

            return m;
        }

        static bool IsTrueMapEdgeCellSpace(Vector2 p, int w, int h, float eps = 0.055f)
        {
            float west = 0.5f;
            float east = (w - 1) + 0.5f;
            float south = 0.5f;
            float north = (h - 1) + 0.5f;
            return p.x <= west + eps || p.x >= east - eps || p.y <= south + eps || p.y >= north - eps;
        }

        static void GetPlayableWorldBoundsXZ(Vector3 origin, int w, int h, float cellSize, float edgeInsetWorld, out float xMin, out float xMax, out float zMin, out float zMax)
        {
            float inset = Mathf.Max(0f, edgeInsetWorld);
            xMin = origin.x + inset;
            xMax = origin.x + w * cellSize - inset;
            zMin = origin.z + inset;
            zMax = origin.z + h * cellSize - inset;
            if (xMax < xMin)
                xMax = xMin = origin.x + w * cellSize * 0.5f;
            if (zMax < zMin)
                zMax = zMin = origin.z + h * cellSize * 0.5f;
        }

        static bool IsInsidePlayableBoundsXZ(Vector3 p, float xMin, float xMax, float zMin, float zMax)
        {
            return p.x >= xMin - 1e-4f && p.x <= xMax + 1e-4f && p.z >= zMin - 1e-4f && p.z <= zMax + 1e-4f;
        }

        static Vector3 ClampXZToPlayableBounds(Vector3 p, float xMin, float xMax, float zMin, float zMax)
        {
            p.x = Mathf.Clamp(p.x, xMin, xMax);
            p.z = Mathf.Clamp(p.z, zMin, zMax);
            return p;
        }

        /// <summary>Proyecta un extremo fuera del mapa sobre el borde jugable a lo largo del segmento interior→extremo.</summary>
        static void ProjectStripEndpointToPlayableEdge(List<Vector3> center, int endIdx, int innerIdx, float xMin, float xMax, float zMin, float zMax)
        {
            if (center == null || endIdx < 0 || endIdx >= center.Count || innerIdx < 0 || innerIdx >= center.Count)
                return;
            Vector3 a = center[innerIdx];
            Vector3 b = center[endIdx];
            a.y = b.y = 0f;
            if (IsInsidePlayableBoundsXZ(b, xMin, xMax, zMin, zMax))
                return;
            for (int step = 0; step < 24; step++)
            {
                float t = 1f - Mathf.Pow(0.5f, step + 1);
                Vector3 p = Vector3.Lerp(a, b, t);
                if (IsInsidePlayableBoundsXZ(p, xMin, xMax, zMin, zMax))
                {
                    center[endIdx] = p;
                    return;
                }
            }

            center[endIdx] = ClampXZToPlayableBounds(b, xMin, xMax, zMin, zMax);
        }

        static void RebuildStripCrossSectionAt(List<Vector3> center, List<float> halfWidths, List<Vector3> left, List<Vector3> right, int i)
        {
            if (center == null || halfWidths == null || left == null || right == null)
                return;
            if (i < 0 || i >= center.Count || halfWidths.Count != center.Count)
                return;
            if (i >= left.Count || i >= right.Count)
                return;
            Vector3 tan = TangentNormalize(center, i);
            Vector3 nrm = PerpendicularXZ(tan);
            float hw = Mathf.Max(0.02f, halfWidths[i]);
            left[i] = center[i] - nrm * hw;
            right[i] = center[i] + nrm * hw;
        }

        static int ClipRiverSurfaceStripToPlayableBounds(
            Vector3 origin,
            int gridW,
            int gridH,
            float cellSize,
            List<Vector3> center,
            List<float> halfWidths,
            List<Vector3> left,
            List<Vector3> right)
        {
            if (center == null || left == null || right == null || center.Count < 2)
                return 0;
            float inset = Mathf.Max(cellSize * 0.02f, 0.01f);
            GetPlayableWorldBoundsXZ(origin, gridW, gridH, cellSize, inset, out float xMin, out float xMax, out float zMin, out float zMax);

            int n = center.Count;
            if (n >= 2)
            {
                ProjectStripEndpointToPlayableEdge(center, 0, 1, xMin, xMax, zMin, zMax);
                ProjectStripEndpointToPlayableEdge(center, n - 1, n - 2, xMin, xMax, zMin, zMax);
            }

            int clipped = 0;
            void ClipList(List<Vector3> pts)
            {
                if (pts == null)
                    return;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (!IsInsidePlayableBoundsXZ(pts[i], xMin, xMax, zMin, zMax))
                        clipped++;
                    pts[i] = ClampXZToPlayableBounds(pts[i], xMin, xMax, zMin, zMax);
                }
            }

            ClipList(center);
            ClipList(left);
            ClipList(right);
            if (halfWidths != null && halfWidths.Count == center.Count)
            {
                RebuildStripCrossSectionAt(center, halfWidths, left, right, 0);
                RebuildStripCrossSectionAt(center, halfWidths, left, right, n - 1);
            }

            return clipped;
        }

        static int FinalClampVertexListToPlayableBounds(
            List<Vector3> verts,
            Vector3 origin,
            int gridW,
            int gridH,
            float cellSize,
            out int visibleOutside,
            out float minX,
            out float maxX,
            out float minZ,
            out float maxZ)
        {
            visibleOutside = 0;
            minX = maxX = minZ = maxZ = 0f;
            if (verts == null || verts.Count == 0)
                return 0;

            float inset = Mathf.Max(cellSize * 0.02f, 0.01f);
            GetPlayableWorldBoundsXZ(origin, gridW, gridH, cellSize, inset, out float xMin, out float xMax, out float zMin, out float zMax);
            minX = maxX = verts[0].x;
            minZ = maxZ = verts[0].z;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 p = verts[i];
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z);
                maxZ = Mathf.Max(maxZ, p.z);
                if (!IsInsidePlayableBoundsXZ(p, xMin, xMax, zMin, zMax))
                    visibleOutside++;
                verts[i] = ClampXZToPlayableBounds(p, xMin, xMax, zMin, zMax);
            }

            return visibleOutside;
        }

        static bool TryCullRiverSurfaceFragmentAfterBuild(
            GameObject go,
            Mesh mesh,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            string objectName,
            List<Vector2> cellSpaceLine,
            float cellSizeWorld,
            bool logDiag,
            out bool culled)
        {
            culled = false;
            if (go == null || mesh == null || grid == null || config == null)
                return false;

            Bounds bounds = mesh.bounds;
            float cs = Mathf.Max(0.01f, cellSizeWorld);
            int nearCells = Mathf.Max(1, config.riverVisualFinalCleanupNearRiverCells);
            WaterMeshBuilder.ComputeWaterVisualBoundsMaskStats(grid, bounds, nearCells, out int intersectsMask, out int nearMaskCells);
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            int nearFord = WaterMeshBuilder.ComputeNearFordFromWorldBounds(grid, bounds, fordD);
            float maxDim = Mathf.Max(bounds.size.x, bounds.size.z);
            int areaApprox = Mathf.Max(1, Mathf.RoundToInt(bounds.size.x * bounds.size.z / (cs * cs)));
            float lineLenCells = PolylineLengthCellSpace(cellSpaceLine);

            bool shouldCull = false;
            string reason = "";
            if (riverIndex > 0)
            {
                if (intersectsMask == 0 && maxDim < cs * 2.75f && areaApprox < Mathf.Max(4, config.riverVisualMinSurfacePieceAreaCells))
                {
                    shouldCull = true;
                    reason = "tributary_off_mask_small";
                }
                else if (intersectsMask == 0 && nearMaskCells == 0 && maxDim < cs * 4f && lineLenCells < config.riverVisualMinSurfacePieceLengthCells * 0.5f)
                {
                    shouldCull = true;
                    reason = "tributary_detached_fragment";
                }
            }
            else if (intersectsMask == 0 && nearMaskCells == 0 && nearFord == 0)
            {
                if (maxDim < cs * 1.85f && areaApprox <= 6)
                {
                    shouldCull = true;
                    reason = "tiny_main_off_mask";
                }
                else if (lineLenCells >= 12f && maxDim < cs * 3.5f && areaApprox <= 14)
                {
                    shouldCull = true;
                    reason = "main_subfragment_off_mask";
                }
            }

            if (!shouldCull)
                return false;

            if (logDiag)
            {
                WaterMeshBuilder.LogWaterVisualObject(
                    config,
                    objectName,
                    "RiverSurface",
                    riverIndex,
                    mesh.vertexCount,
                    mesh.triangles.Length / 3,
                    bounds,
                    intersectsMask,
                    nearMaskCells,
                    nearFord,
                    riverIndex == 0 ? 1 : 0,
                    riverIndex > 0 ? 1 : 0,
                    1,
                    reason);
                Debug.Log(
                    $"[RiverSurfaceFragmentCull] riverIndex={riverIndex} name={objectName} maxDim={maxDim:F3} areaCells={areaApprox} " +
                    $"lineLenCells={lineLenCells:F1} intersectsMask={intersectsMask} nearMaskCells={nearMaskCells} " +
                    $"nearFord={nearFord} culled=1 reason={reason}");
            }

            if (Application.isPlaying)
            {
                Object.Destroy(go);
                Object.Destroy(mesh);
            }
            else
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(mesh);
            }

            RiverSurfaceFragmentCullCount++;
            culled = true;
            return true;
        }

        static void CopyVector3List(IReadOnlyList<Vector3> src, List<Vector3> dst)
        {
            if (src == null || dst == null)
                return;
            if (dst.Count != src.Count)
            {
                dst.Clear();
                for (int i = 0; i < src.Count; i++)
                    dst.Add(src[i]);
            }
            else
            {
                for (int i = 0; i < src.Count; i++)
                    dst[i] = src[i];
            }
        }

        static bool ShouldSkipDetachedTributaryRiverSurface(
            GridSystem grid,
            List<Vector2> tributaryCellSpace,
            int tributaryIndex,
            MapGenConfig config)
        {
            if (grid == null || tributaryCellSpace == null || tributaryIndex <= 0 || config == null)
                return false;
            int minPatch = Mathf.Max(2, config.riverVisualMinDetachedPatchCells);
            int corridor = Mathf.Clamp(config.riverVisualMainRiverCorridorCells, 1, 8);
            var mainLine = grid.RiverCenterlinesCellSpace[0];
            if (mainLine == null || mainLine.Count < 2)
                return false;

            var corridorCells = new HashSet<Vector2Int>();
            int w = grid.Width;
            int h = grid.Height;
            for (int i = 0; i < mainLine.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.RoundToInt(mainLine[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.RoundToInt(mainLine[i].y), 0, h - 1);
                for (int dy = -corridor; dy <= corridor; dy++)
                {
                    for (int dx = -corridor; dx <= corridor; dx++)
                    {
                        int nx = cx + dx;
                        int ny = cy + dy;
                        if ((uint)nx < (uint)w && (uint)ny < (uint)h)
                            corridorCells.Add(new Vector2Int(nx, ny));
                    }
                }
            }

            var trib = new HashSet<Vector2Int>();
            bool hasFord = false;
            for (int i = 0; i < tributaryCellSpace.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(tributaryCellSpace[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(tributaryCellSpace[i].y), 0, h - 1);
                trib.Add(new Vector2Int(cx, cy));
                if (grid.GetCell(cx, cy).riverFord)
                    hasFord = true;
            }

            if (hasFord)
                return false;
            foreach (Vector2Int c in trib)
            {
                if (corridorCells.Contains(c))
                    return false;
            }

            return trib.Count < minPatch;
        }

        static float PolylineLengthCellSpace(List<Vector2> pts)
        {
            if (pts == null || pts.Count < 2)
                return 0f;
            float L = 0f;
            for (int i = 1; i < pts.Count; i++)
                L += Vector2.Distance(pts[i], pts[i - 1]);
            return L;
        }

        static bool TryCullTributarySurfacePiece(
            GridSystem grid,
            List<Vector2> tributaryCellSpace,
            int tributaryIndex,
            MapGenConfig config,
            bool logRm)
        {
            if (grid == null || tributaryCellSpace == null || tributaryIndex <= 0 || config == null)
                return false;

            bool detachedSkip = ShouldSkipDetachedTributaryRiverSurface(grid, tributaryCellSpace, tributaryIndex, config);
            bool shortSkip = !detachedSkip &&
                ShouldSkipShortTributaryRiverSurfaceVisual(grid, tributaryCellSpace, tributaryIndex, config);
            if (!detachedSkip && !shortSkip)
                return false;

            if (detachedSkip)
                DetachedRiverSurfaceSkips++;
            else
                ShortRiverSurfaceSkips++;

            if (logRm)
            {
                int w = grid.Width;
                int h = grid.Height;
                var trib = new HashSet<Vector2Int>();
                for (int i = 0; i < tributaryCellSpace.Count; i++)
                {
                    int cx = Mathf.Clamp(Mathf.FloorToInt(tributaryCellSpace[i].x), 0, w - 1);
                    int cy = Mathf.Clamp(Mathf.FloorToInt(tributaryCellSpace[i].y), 0, h - 1);
                    trib.Add(new Vector2Int(cx, cy));
                }

                int fordKeep = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
                bool nearFord = false;
                foreach (Vector2Int c in trib)
                {
                    if (WaterMeshBuilder.GridCellNearFordRiverChebyshev(grid, c.x, c.y, fordKeep))
                    {
                        nearFord = true;
                        break;
                    }
                }

                var joinKeys = BuildJoinProximityCellKeys(grid.RiverCenterlinesCellSpace, tributaryIndex, w, h);
                bool nearConfluence = false;
                foreach (Vector2Int c in trib)
                {
                    if (joinKeys.Contains(PackCellKey(c.x, c.y)))
                    {
                        nearConfluence = true;
                        break;
                    }
                }

                float lenCells = PolylineLengthCellSpace(tributaryCellSpace);
                string reason = detachedSkip ? "detached_patch" : "short_piece";
                Debug.Log(
                    $"[RiverSurfacePieceCull] riverIndex={tributaryIndex} lengthCells={lenCells:F1} areaCells={trib.Count} " +
                    $"nearFord={(nearFord ? 1 : 0)} nearConfluence={(nearConfluence ? 1 : 0)} culled=1 reason={reason}");
            }

            return true;
        }

        static bool ShouldSkipShortTributaryRiverSurfaceVisual(
            GridSystem grid,
            List<Vector2> tributaryCellSpace,
            int tributaryIndex,
            MapGenConfig config)
        {
            if (grid == null || tributaryCellSpace == null || tributaryIndex <= 0 || config == null)
                return false;
            int minLen = Mathf.Max(2, config.riverVisualMinSurfacePieceLengthCells);
            int minArea = Mathf.Max(2, config.riverVisualMinSurfacePieceAreaCells);
            int corridor = Mathf.Max(
                Mathf.Clamp(config.riverVisualMainRiverCorridorCells, 1, 8),
                Mathf.Clamp(config.riverVisualMainCorridorKeepDistanceCells, 1, 16));
            int fordKeep = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            int w = grid.Width;
            int h = grid.Height;
            var mainLine = grid.RiverCenterlinesCellSpace[0];
            if (mainLine == null || mainLine.Count < 2)
                return false;

            var corridorCells = new HashSet<Vector2Int>();
            for (int i = 0; i < mainLine.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.RoundToInt(mainLine[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.RoundToInt(mainLine[i].y), 0, h - 1);
                for (int dy = -corridor; dy <= corridor; dy++)
                {
                    for (int dx = -corridor; dx <= corridor; dx++)
                    {
                        int nx = cx + dx;
                        int ny = cy + dy;
                        if ((uint)nx < (uint)w && (uint)ny < (uint)h)
                            corridorCells.Add(new Vector2Int(nx, ny));
                    }
                }
            }

            var trib = new HashSet<Vector2Int>();
            for (int i = 0; i < tributaryCellSpace.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(tributaryCellSpace[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(tributaryCellSpace[i].y), 0, h - 1);
                trib.Add(new Vector2Int(cx, cy));
            }

            foreach (Vector2Int c in trib)
            {
                if (WaterMeshBuilder.GridCellNearFordRiverChebyshev(grid, c.x, c.y, fordKeep))
                    return false;
            }

            var joinKeys = BuildJoinProximityCellKeys(grid.RiverCenterlinesCellSpace, tributaryIndex, w, h);
            foreach (Vector2Int c in trib)
            {
                if (joinKeys.Contains(PackCellKey(c.x, c.y)))
                    return false;
            }

            foreach (Vector2Int c in trib)
            {
                if (corridorCells.Contains(c))
                    return false;
            }

            float polyLen = PolylineLengthCellSpace(tributaryCellSpace);
            return polyLen < minLen || trib.Count < minArea;
        }

        static void ResolveVisualMeanderAndOverlapWarnings(
            List<Vector3> worldCenters,
            List<Vector2> cellProcessed,
            List<Vector3> backupAfterBorder,
            List<Vector3> backupCellsOnly,
            Vector3 origin,
            float cellSize,
            int w,
            int h,
            MapGenConfig config,
            int riverIndex,
            float baseHalfW,
            bool logDetail,
            out int overlapWarningsAfter,
            out int overlapWarningsBefore)
        {
            float th = Mathf.Max(0.02f, baseHalfW * 0.9f);
            overlapWarningsBefore = backupAfterBorder != null ? CountProximityWarnings(backupAfterBorder, th) : 0;
            int retryReduced = 0;
            int disabledDueToOverlap = 0;
            int revertedNoBorder = 0;
            bool meanderAccepted = false;
            float maxOffsetCells = 0f;
            string reject = "na";

            if (config == null ||
                !config.riverSurfaceVisualMeanderEnabled ||
                worldCenters == null ||
                backupAfterBorder == null ||
                backupCellsOnly == null ||
                worldCenters.Count < 4)
            {
                if (worldCenters != null && backupAfterBorder != null)
                    CopyVector3List(backupAfterBorder, worldCenters);
                meanderAccepted = false;
                reject = "meander_disabled_or_short";
                overlapWarningsAfter = worldCenters != null ? CountProximityWarnings(worldCenters, th) : 0;
                if (logDetail)
                {
                    Debug.Log(
                        $"[RiverVisualMeander] riverId={riverIndex} enabled={(config != null && config.riverSurfaceVisualMeanderEnabled ? 1 : 0)} " +
                        $"amplitudeCells={(config != null ? config.riverSurfaceVisualMeanderAmplitudeCells : 0f):F3} frequencyCells={(config != null ? config.riverSurfaceVisualMeanderFrequencyCells : 0f):F2} " +
                        $"points={(worldCenters != null ? worldCenters.Count : 0)} maxOffsetCells={maxOffsetCells:F3} accepted={(meanderAccepted ? 1 : 0)} " +
                        $"retryReducedAmplitude=0 disabledDueToOverlap=0 overlapWarningsBefore={overlapWarningsBefore} overlapWarningsAfter={overlapWarningsAfter} rejectReason={reject}");
                }

                return;
            }

            void ApplyMeander(float ampOverride, bool silent, out bool acc, out float maxOff, out string rej)
            {
                CopyVector3List(backupAfterBorder, worldCenters);
                ApplyVisualMeanderToCenters(
                    worldCenters,
                    cellProcessed,
                    origin,
                    cellSize,
                    w,
                    h,
                    config,
                    riverIndex,
                    logDetail: false,
                    ampOverride,
                    silent,
                    out maxOff,
                    out acc,
                    out rej);
            }

            ApplyMeander(-1f, true, out meanderAccepted, out maxOffsetCells, out reject);
            overlapWarningsAfter = CountProximityWarnings(worldCenters, th);
            if (overlapWarningsAfter > 0)
            {
                retryReduced = 1;
                float halfAmp = Mathf.Clamp(config.riverSurfaceVisualMeanderAmplitudeCells * 0.5f, 0f, 0.6f);
                ApplyMeander(halfAmp, true, out meanderAccepted, out maxOffsetCells, out reject);
                overlapWarningsAfter = CountProximityWarnings(worldCenters, th);
            }

            if (overlapWarningsAfter > 0)
            {
                disabledDueToOverlap = 1;
                meanderAccepted = false;
                CopyVector3List(backupAfterBorder, worldCenters);
                overlapWarningsAfter = CountProximityWarnings(worldCenters, th);
            }

            if (overlapWarningsAfter > 0)
            {
                revertedNoBorder = 1;
                meanderAccepted = false;
                CopyVector3List(backupCellsOnly, worldCenters);
                overlapWarningsAfter = CountProximityWarnings(worldCenters, th);
            }

            if (logDetail)
            {
                Debug.Log(
                    $"[RiverVisualMeander] riverId={riverIndex} enabled=1 amplitudeCells={config.riverSurfaceVisualMeanderAmplitudeCells:F3} " +
                    $"frequencyCells={config.riverSurfaceVisualMeanderFrequencyCells:F2} points={worldCenters.Count} maxOffsetCells={maxOffsetCells:F3} " +
                    $"accepted={(meanderAccepted ? 1 : 0)} retryReducedAmplitude={retryReduced} disabledDueToOverlap={disabledDueToOverlap} " +
                    $"revertedNoBorderExtension={revertedNoBorder} overlapWarningsBefore={overlapWarningsBefore} overlapWarningsAfter={overlapWarningsAfter} " +
                    $"rejectReason={reject}");
            }
        }

        static void ApplyBorderVisualExtensionToCenters(
            List<Vector3> centersWorld,
            IReadOnlyList<Vector2> cellSpace,
            Vector3 origin,
            float cellSize,
            int w,
            int h,
            MapGenConfig config,
            float fullRiverWidthWorld)
        {
            if (centersWorld == null || cellSpace == null || centersWorld.Count != cellSpace.Count || config == null)
                return;
            float extLegacy = Mathf.Clamp(config.riverSurfaceExtendBorderExitVisualCells, 0f, 1.5f) * cellSize;
            float mul = config.riverSurfaceExtendBeyondMapWidthMul;
            float fullW = Mathf.Max(cellSize * 0.25f, fullRiverWidthWorld);
            float extByWidth = mul > 1e-5f ? Mathf.Clamp(mul * fullW, 1.5f * fullW, 3f * fullW) : 0f;
            float extUse = Mathf.Max(extLegacy, extByWidth);
            if (extUse < 1e-5f)
                return;
            int n = centersWorld.Count;
            if (n < 2)
                return;
            if (IsTrueMapEdgeCellSpace(cellSpace[n - 1], w, h))
            {
                Vector3 t = TangentNormalize(centersWorld, n - 1);
                if (t.sqrMagnitude > 1e-12f)
                    centersWorld[n - 1] += t * extUse;
            }

            if (IsTrueMapEdgeCellSpace(cellSpace[0], w, h))
            {
                Vector3 t0 = TangentNormalize(centersWorld, 0);
                if (t0.sqrMagnitude > 1e-12f)
                    centersWorld[0] -= t0 * extUse;
            }
        }

        static List<Vector2> WorldCentersToCellSpacePolyline(List<Vector3> centersWorld, Vector3 origin, float cellSize, int w, int h, bool clampToMapInterior)
        {
            float inv = 1f / Mathf.Max(1e-5f, cellSize);
            var r = new List<Vector2>(centersWorld.Count);
            for (int i = 0; i < centersWorld.Count; i++)
            {
                float x = (centersWorld[i].x - origin.x) * inv;
                float y = (centersWorld[i].z - origin.z) * inv;
                if (clampToMapInterior)
                {
                    x = Mathf.Clamp(x, 0.2f, w - 0.2f);
                    y = Mathf.Clamp(y, 0.2f, h - 0.2f);
                }

                r.Add(new Vector2(x, y));
            }

            return r;
        }

        static float MeanderEdgeFade(float t01, float fade)
        {
            fade = Mathf.Clamp01(fade);
            if (fade < 1e-5f)
                return 1f;
            if (t01 <= fade)
                return Mathf.SmoothStep(0f, 1f, t01 / Mathf.Max(1e-5f, fade));
            if (t01 >= 1f - fade)
                return Mathf.SmoothStep(1f, 0f, (t01 - (1f - fade)) / Mathf.Max(1e-5f, fade));
            return 1f;
        }

        static void ApplyVisualMeanderToCenters(
            List<Vector3> centersWorld,
            IReadOnlyList<Vector2> cellSpace,
            Vector3 origin,
            float cellSize,
            int w,
            int h,
            MapGenConfig config,
            int riverIndex,
            bool logDetail,
            float amplitudeCellsOverride,
            bool silent,
            out float maxOffsetCells,
            out bool accepted,
            out string rejectReason)
        {
            maxOffsetCells = 0f;
            accepted = false;
            rejectReason = "disabled";
            if (config == null || !config.riverSurfaceVisualMeanderEnabled || centersWorld == null || centersWorld.Count < 4)
            {
                rejectReason = config == null || !config.riverSurfaceVisualMeanderEnabled ? "disabled" : "too_few_points";
                return;
            }

            float ampC = amplitudeCellsOverride >= 0f
                ? Mathf.Clamp(amplitudeCellsOverride, 0f, 0.6f)
                : Mathf.Clamp(config.riverSurfaceVisualMeanderAmplitudeCells, 0f, 0.6f);
            float freqC = Mathf.Max(2f, config.riverSurfaceVisualMeanderFrequencyCells);
            float fade01 = Mathf.Clamp(config.riverSurfaceVisualMeanderEndFade01, 0.02f, 0.35f);

            var trial = new List<Vector3>(centersWorld);
            float acc = 0f;
            maxOffsetCells = 0f;
            for (int i = 0; i < trial.Count; i++)
            {
                if (i > 0)
                {
                    Vector3 d = trial[i] - trial[i - 1];
                    d.y = 0f;
                    acc += d.magnitude;
                }

                if (i == 0 || i == trial.Count - 1)
                    continue;
                if (cellSpace != null && i < cellSpace.Count && IsTrueMapEdgeCellSpace(cellSpace[i], w, h))
                    continue;

                float t01 = trial.Count > 1 ? i / (float)(trial.Count - 1) : 0f;
                float fade = MeanderEdgeFade(t01, fade01);
                if (fade < 1e-4f)
                    continue;

                Vector3 tan = TangentNormalize(trial, i);
                Vector3 nrm = PerpendicularXZ(tan);
                float phase = acc / Mathf.Max(0.01f, cellSize) / freqC * (Mathf.PI * 2f) + riverIndex * 1.713f;
                float offWorld = Mathf.Sin(phase) * ampC * cellSize * fade;
                trial[i] += nrm * offWorld;
                maxOffsetCells = Mathf.Max(maxOffsetCells, Mathf.Abs(offWorld / Mathf.Max(1e-5f, cellSize)));
            }

            var cellPoly = WorldCentersToCellSpacePolyline(trial, origin, cellSize, w, h, true);
            if (PolylineSelfIntersectsXZCell(cellPoly))
            {
                rejectReason = "self_intersect";
                if (logDetail && !silent)
                {
                    UnityEngine.Debug.Log(
                        $"[RiverVisualMeander] riverId={riverIndex} enabled=1 amplitudeCells={ampC:F3} frequencyCells={freqC:F2} " +
                        $"points={trial.Count} maxOffsetCells={maxOffsetCells:F3} accepted=0 rejectReason={rejectReason}");
                }

                return;
            }

            for (int i = 0; i < centersWorld.Count; i++)
                centersWorld[i] = trial[i];
            accepted = true;
            rejectReason = "ok";
            if (logDetail && !silent)
            {
                UnityEngine.Debug.Log(
                    $"[RiverVisualMeander] riverId={riverIndex} enabled=1 amplitudeCells={ampC:F3} frequencyCells={freqC:F2} " +
                    $"points={trial.Count} maxOffsetCells={maxOffsetCells:F3} accepted=1 rejectReason=none");
            }
        }

        static float ComputeMaxInteriorBendAngleDeg(List<Vector3> c)
        {
            int n = c != null ? c.Count : 0;
            if (n < 3)
                return 0f;
            float m = 0f;
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 a = c[i] - c[i - 1];
                Vector3 b = c[i + 1] - c[i];
                a.y = 0f;
                b.y = 0f;
                if (a.sqrMagnitude < 1e-14f || b.sqrMagnitude < 1e-14f)
                    continue;
                m = Mathf.Max(m, Vector3.Angle(a, b));
            }

            return m;
        }

        static float BendCapRelaxMulFromEnd(List<Vector3> c, bool atStart, float relaxAngleDeg, int scan)
        {
            int n = c != null ? c.Count : 0;
            if (n < 3)
                return 1f;
            float minMul = 1f;
            int used = Mathf.Min(Mathf.Max(1, scan), n - 2);
            for (int k = 0; k < used; k++)
            {
                int i = atStart ? 1 + k : n - 2 - k;
                if (i < 1 || i >= n - 1)
                    break;
                Vector3 a = c[i] - c[i - 1];
                Vector3 b = c[i + 1] - c[i];
                a.y = 0f;
                b.y = 0f;
                if (a.sqrMagnitude < 1e-14f || b.sqrMagnitude < 1e-14f)
                    continue;
                float ang = Vector3.Angle(a, b);
                if (ang >= relaxAngleDeg + 16f)
                    minMul = Mathf.Min(minMul, 0.34f);
                else if (ang >= relaxAngleDeg)
                    minMul = Mathf.Min(minMul, 0.54f);
                else if (ang >= relaxAngleDeg - 14f)
                    minMul = Mathf.Min(minMul, 0.76f);
            }

            return minMul;
        }

        struct RiverBorderEndpointSymmetry
        {
            public bool AtBorder;
            public bool GhostInserted;
            public Vector2 OutwardDir;
            public float VisualHalfWidth;
            public bool UsesTaper;
            public bool UsesCap;
        }

        static void ResolveRiverBorderEndpointDirs(
            List<Vector2> cellSpace,
            int endpointIndex,
            int interiorIndex,
            out Vector2 outwardDir,
            out Vector2 inwardDir)
        {
            Vector2 endpoint = cellSpace[endpointIndex];
            Vector2 interior = cellSpace[interiorIndex];
            outwardDir = endpoint - interior;
            if (outwardDir.sqrMagnitude < 1e-10f)
                outwardDir = endpointIndex == 0 ? Vector2.left : Vector2.right;
            else
                outwardDir.Normalize();
            inwardDir = -outwardDir;
        }

        static void ApplySymmetricBorderEndpointMeshTreatment(
            List<Vector3> center,
            List<float> halfWidths,
            List<Vector2> cellSpace,
            Vector3 mapOrigin,
            int gridW,
            int gridH,
            float cellSizeWorld,
            MapGenConfig config,
            float baseHalfWidthWorld,
            out RiverBorderEndpointSymmetry startSym,
            out RiverBorderEndpointSymmetry endSym)
        {
            startSym = default;
            endSym = default;
            if (center == null || halfWidths == null || cellSpace == null || config == null)
                return;
            if (center.Count != halfWidths.Count || center.Count != cellSpace.Count || center.Count < 2)
                return;

            float ghostCells = Mathf.Clamp(config.riverSurfaceBorderGhostCells, 0f, 6f);
            float borderWidthMul = Mathf.Clamp(config.riverSurfaceBorderEndpointWidthMul, 1.5f, 3f);
            float minBorderHalf = Mathf.Max(0.02f, baseHalfWidthWorld * borderWidthMul);

            void TreatEndpoint(bool isStart, ref RiverBorderEndpointSymmetry sym)
            {
                int n = center.Count;
                if (n < 2)
                    return;
                int ep = isStart ? 0 : n - 1;
                int inner = isStart ? 1 : n - 2;
                sym.AtBorder = IsTrueMapEdgeCellSpace(cellSpace[ep], gridW, gridH);
                sym.UsesTaper = false;
                sym.UsesCap = false;
                if (!sym.AtBorder)
                    return;

                ResolveRiverBorderEndpointDirs(cellSpace, ep, inner, out Vector2 outward, out _);
                sym.OutwardDir = outward;
                sym.VisualHalfWidth = Mathf.Max(halfWidths[ep], minBorderHalf);
                halfWidths[ep] = sym.VisualHalfWidth;

                if (ghostCells > 1e-4f)
                {
                    Vector2 ghostCell = cellSpace[ep] + outward * ghostCells;
                    Vector3 ghostWorld = new Vector3(
                        mapOrigin.x + ghostCell.x * cellSizeWorld,
                        center[ep].y,
                        mapOrigin.z + ghostCell.y * cellSizeWorld);
                    if (isStart)
                    {
                        center.Insert(0, ghostWorld);
                        halfWidths.Insert(0, sym.VisualHalfWidth);
                        cellSpace.Insert(0, ghostCell);
                    }
                    else
                    {
                        center.Add(ghostWorld);
                        halfWidths.Add(sym.VisualHalfWidth);
                        cellSpace.Add(ghostCell);
                    }

                    sym.GhostInserted = true;
                }
            }

            TreatEndpoint(true, ref startSym);
            TreatEndpoint(false, ref endSym);
        }

        static void LogRiverEndpointSymmetry(
            MapGenConfig config,
            int riverId,
            RiverBorderEndpointSymmetry startSym,
            RiverBorderEndpointSymmetry endSym)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverEndpointSymmetry] riverId={riverId} startAtBorder={(startSym.AtBorder ? 1 : 0)} endAtBorder={(endSym.AtBorder ? 1 : 0)} " +
                $"startOutwardDir=({startSym.OutwardDir.x:F2},{startSym.OutwardDir.y:F2}) endOutwardDir=({endSym.OutwardDir.x:F2},{endSym.OutwardDir.y:F2}) " +
                $"startGhostPoints={(startSym.GhostInserted ? 1 : 0)} endGhostPoints={(endSym.GhostInserted ? 1 : 0)} " +
                $"startUsesTaper={(startSym.UsesTaper ? 1 : 0)} endUsesTaper={(endSym.UsesTaper ? 1 : 0)} " +
                $"startUsesCap={(startSym.UsesCap ? 1 : 0)} endUsesCap={(endSym.UsesCap ? 1 : 0)} " +
                $"startVisualHalfWidth={startSym.VisualHalfWidth:F3} endVisualHalfWidth={endSym.VisualHalfWidth:F3} symmetricPolicy=1");
        }

        static void LogRiverSurfaceEndFix(
            MapGenConfig config,
            int riverId,
            RiverBorderEndpointSymmetry endSym,
            float baseHalfWidthWorld)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            bool stable = endSym.GhostInserted && endSym.AtBorder;
            float widthMul = baseHalfWidthWorld > 1e-5f ? endSym.VisualHalfWidth / baseHalfWidthWorld : 0f;
            Debug.Log(
                $"[RiverSurfaceEndFix] riverId={riverId} endAtBorder={(endSym.AtBorder ? 1 : 0)} endGhostPointUsed={(endSym.GhostInserted ? 1 : 0)} " +
                $"endTangentStable={(stable ? 1 : 0)} endWidthMul={widthMul:F3} " +
                "endCapDisabled=1 endTaperDisabled=1 endFlatCut=1 endContinuesBeyondMapVisually=1");
        }

        static bool TryBuildStripMeshWithCaps(
            Transform parent,
            List<Vector3> center,
            List<float> halfWidthWorld,
            Material mat,
            int waterLayer,
            string objectName,
            float uvScale,
            float cellSizeWorld,
            MapGenConfig config,
            int riverIndex,
            int gridWCells,
            int gridHCells,
            List<Vector2> cellSpaceLine,
            GridSystem grid,
            Vector3 mapOrigin,
            bool centerlinePreClipped,
            out int outVerts,
            out int outTris,
            out float maxSegBuilt)
        {
            outVerts = 0;
            outTris = 0;
            maxSegBuilt = 0f;
            int n = center.Count;
            if (n < 2 || halfWidthWorld == null || halfWidthWorld.Count != n)
                return false;

            var meshCenter = new List<Vector3>(center);
            var meshHalfW = new List<float>(halfWidthWorld);
            var meshCell = cellSpaceLine != null ? new List<Vector2>(cellSpaceLine) : null;
            float borderMul = Mathf.Clamp(config != null ? config.riverSurfaceBorderEndpointWidthMul : 2f, 1.5f, 3f);
            float baseHalfForBorder = config != null && config.riverVisualRibbonFullWidthCellsMain > 0.01f
                ? config.riverVisualRibbonFullWidthCellsMain * 0.5f * cellSizeWorld
                : (halfWidthWorld.Count > 0 ? halfWidthWorld[0] / borderMul : 0.5f);
            ApplySymmetricBorderEndpointMeshTreatment(
                meshCenter,
                meshHalfW,
                meshCell,
                mapOrigin,
                gridWCells,
                gridHCells,
                cellSizeWorld,
                config,
                baseHalfForBorder,
                out RiverBorderEndpointSymmetry startSym,
                out RiverBorderEndpointSymmetry endSym);
            if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
            {
                LogRiverEndpointSymmetry(config, riverIndex, startSym, endSym);
                if (endSym.AtBorder)
                    LogRiverSurfaceEndFix(config, riverIndex, endSym, baseHalfForBorder);
            }

            n = meshCenter.Count;
            center = meshCenter;
            halfWidthWorld = meshHalfW;
            cellSpaceLine = meshCell;

            bool cellHintOkPre = cellSpaceLine != null && cellSpaceLine.Count == n;
            bool startAtBorderPre = cellHintOkPre && IsTrueMapEdgeCellSpace(cellSpaceLine[0], gridWCells, gridHCells);
            bool endAtBorderPre = cellHintOkPre && IsTrueMapEdgeCellSpace(cellSpaceLine[n - 1], gridWCells, gridHCells);
            float extensionWorldPre = 0f;
            if (startAtBorderPre || endAtBorderPre)
            {
                GetPlayableWorldBoundsXZ(mapOrigin, gridWCells, gridHCells, cellSizeWorld, cellSizeWorld * 0.02f,
                    out float xMin, out float xMax, out float zMin, out float zMax);
                if (startAtBorderPre && !IsInsidePlayableBoundsXZ(center[0], xMin, xMax, zMin, zMax))
                    extensionWorldPre = Mathf.Max(extensionWorldPre, Vector3.Distance(center[0], ClampXZToPlayableBounds(center[0], xMin, xMax, zMin, zMax)));
                if (endAtBorderPre && !IsInsidePlayableBoundsXZ(center[n - 1], xMin, xMax, zMin, zMax))
                    extensionWorldPre = Mathf.Max(extensionWorldPre, Vector3.Distance(center[n - 1], ClampXZToPlayableBounds(center[n - 1], xMin, xMax, zMin, zMax)));
            }

            bool logCap = config != null && (config.debugLogs || config.debugHydrologyNetwork);
            bool cellHintOk = cellSpaceLine != null && cellSpaceLine.Count == n;
            bool startAtBorder = cellHintOk && IsTrueMapEdgeCellSpace(cellSpaceLine[0], gridWCells, gridHCells);
            bool endAtBorder = cellHintOk && IsTrueMapEdgeCellSpace(cellSpaceLine[n - 1], gridWCells, gridHCells);
            bool skipEndBlend = riverIndex > 0 &&
                config != null &&
                config.riverSurfaceSkipTributaryConfluenceCap;

            BuildCrossSectionRiverMesh(
                center,
                halfWidthWorld,
                cellSpaceLine,
                grid,
                config,
                riverIndex,
                cellSizeWorld,
                uvScale,
                startAtBorder,
                endAtBorder,
                skipEndBlend,
                out List<Vector3> verts,
                out List<Vector2> uvs,
                out List<Vector3> normals,
                out List<Vector4> tangents,
                out List<int> tris,
                out maxSegBuilt,
                out _);

            bool visibleDebugWire = config != null &&
                (config.riverSurfaceDebugShowWire ||
                 config.riverSurfaceDebugDrawCenterline ||
                 config.riverSurfaceDebugDrawEdges ||
                 config.riverSurfaceDebugDrawJoinNormals);

            if (logCap)
            {
                LogRiverSurfaceMeshBuild(config, riverIndex, n, verts.Count, tris.Count / 3, visibleDebugWire);
            }

            if (verts.Count < CrossSectionVertexCount * 2 || tris.Count < 6)
                return false;

            if (riverIndex > 0)
            {
                int trisBeforeCull = tris.Count / 3;
                int culledTris = CullTrianglesOutsideVisualMask(verts, tris, grid, config, riverIndex, mapOrigin, cellSizeWorld);
                int trisAfterCull = tris.Count / 3;
                if (logCap && culledTris > 0)
                {
                    Debug.Log(
                        $"[RiverSurfaceTriangleCull] riverIndex={riverIndex} trisBefore={trisBeforeCull} trisAfter={trisAfterCull} " +
                        $"culledTris={culledTris} reason=outside_visual_mask");
                }
            }

            if (tris.Count < 6)
                return false;

            int visibleOutside = 0;
            float bMinX = 0f, bMaxX = 0f, bMinZ = 0f, bMaxZ = 0f;
            int capVertsOutsidePlayable = FinalClampVertexListToPlayableBounds(
                verts,
                mapOrigin,
                gridWCells,
                gridHCells,
                cellSizeWorld,
                out visibleOutside,
                out bMinX,
                out bMaxX,
                out bMinZ,
                out bMaxZ);

            if (logCap && (startAtBorderPre || endAtBorderPre || visibleOutside > 0))
            {
                Debug.Log(
                    $"[RiverSurfaceBorderClip] riverIndex={riverIndex} clippedVerts=0 " +
                    $"startAtBorder={(startAtBorderPre ? 1 : 0)} endAtBorder={(endAtBorderPre ? 1 : 0)} " +
                    $"extensionWorld={extensionWorldPre:F3} visibleOutsideBounds={visibleOutside} " +
                    $"minX={bMinX:F2} maxX={bMaxX:F2} minZ={bMinZ:F2} maxZ={bMaxZ:F2} note=legacy_post_clamp");
            }

            var mesh = new Mesh { name = objectName };
            if (verts.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            var colors = new List<Color>(verts.Count);
            for (int i = 0; i < verts.Count; i++)
                colors.Add(Color.white);
            mesh.SetColors(colors);
            mesh.RecalculateBounds();

            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.layer = waterLayer;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.enabled = true;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.renderingLayerMask = 1u;

            bool logDiag = config != null && (config.debugLogs || config.debugHydrologyNetwork);
            if (riverIndex > 0 &&
                TryCullRiverSurfaceFragmentAfterBuild(
                    go,
                    mesh,
                    grid,
                    config,
                    riverIndex,
                    objectName,
                    cellHintOk ? cellSpaceLine : null,
                    cellSizeWorld,
                    logDiag,
                    out bool fragmentCulled))
            {
                outVerts = 0;
                outTris = 0;
                return false;
            }

            if (logDiag)
            {
                var bounds = mesh.bounds;
                int nearCells = Mathf.Max(1, config.riverVisualFinalCleanupNearRiverCells);
                WaterMeshBuilder.ComputeWaterVisualBoundsMaskStats(
                    grid,
                    bounds,
                    nearCells,
                    out int intersectsMask,
                    out int nearMaskCells);
                int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
                int nf = WaterMeshBuilder.ComputeNearFordFromWorldBounds(grid, bounds, fordD);
                WaterMeshBuilder.LogWaterVisualObject(
                    config,
                    objectName,
                    "RiverSurface",
                    riverIndex,
                    verts.Count,
                    tris.Count / 3,
                    bounds,
                    intersectsMask,
                    nearMaskCells,
                    nf,
                    riverIndex == 0 ? 1 : 0,
                    riverIndex > 0 ? 1 : 0,
                    0,
                    centerlinePreClipped ? "cache_preclip" : "");
            }

            LastVertexSum += verts.Count;
            LastTriSum += tris.Count / 3;
            outVerts = verts.Count;
            outTris = tris.Count / 3;
            return true;
        }

        /// <summary>Mismo orden que el strip principal: (a,c,b) y (b,c,d) para normal hacia arriba.</summary>
        static void AddTriStripWinding(List<int> tris, int a, int b, int c)
        {
            tris.Add(a);
            tris.Add(b);
            tris.Add(c);
        }

        static float[] BuildAccumulatedV(List<Vector3> center)
        {
            int n = center.Count;
            var acc = new float[n];
            acc[0] = 0f;
            for (int i = 1; i < n; i++)
            {
                Vector3 d = center[i] - center[i - 1];
                d.y = 0f;
                acc[i] = acc[i - 1] + d.magnitude;
            }

            return acc;
        }

        static Vector3 QuadraticBezierXZ(Vector3 a, Vector3 m, Vector3 b, float t)
        {
            float o = 1f - t;
            Vector3 p = o * o * a + 2f * o * t * m + t * t * b;
            p.y = a.y;
            return p;
        }

        static void SmoothLRPass(List<Vector3> left, List<Vector3> right, float strength)
        {
            int n = left.Count;
            if (n < 3)
                return;
            var nl = new List<Vector3>(left);
            var nr = new List<Vector3>(right);
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 al = (left[i - 1] + left[i] + left[i + 1]) / 3f;
                Vector3 ar = (right[i - 1] + right[i] + right[i + 1]) / 3f;
                Vector3 ll = Vector3.Lerp(left[i], al, strength);
                ll.y = left[i].y;
                nl[i] = ll;
                Vector3 rr = Vector3.Lerp(right[i], ar, strength);
                rr.y = right[i].y;
                nr[i] = rr;
            }

            for (int i = 1; i < n - 1; i++)
            {
                left[i] = nl[i];
                right[i] = nr[i];
            }
        }

        static Vector3 TangentNormalize(List<Vector3> path, int i)
        {
            int n = path.Count;
            if (n < 2)
                return Vector3.forward;
            if (i <= 0)
            {
                Vector3 d = path[1] - path[0];
                d.y = 0f;
                return d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.forward;
            }

            if (i >= n - 1)
            {
                Vector3 d = path[n - 1] - path[n - 2];
                d.y = 0f;
                return d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.forward;
            }

            Vector3 t = path[i + 1] - path[i - 1];
            t.y = 0f;
            return t.sqrMagnitude > 1e-12f ? t.normalized : Vector3.forward;
        }

        /// <summary>
        /// Asegura máscara + centerlines cacheadas (misma preparación que el mesh).
        /// </summary>
        public static void BuildRiverVisualSurfaceMask(GridSystem grid, MapGenConfig config, float cellSize)
        {
            if (grid == null || config == null)
                return;
            EnsureRiverVisualSurfaceCache(grid, config);
            if (config.debugLogs || config.debugHydrologyNetwork)
                LogRiverVisualCacheUse("BuildRiverVisualSurfaceMask", grid, -1);
        }

        static int AnchorCellKey(Vector2 p) =>
            (Mathf.Clamp(Mathf.RoundToInt(p.x), -32768, 32767) << 16) ^
            (Mathf.Clamp(Mathf.RoundToInt(p.y), -32768, 32767) & 0xffff);

        static void AddAnchorKey(HashSet<int> keys, Vector2 p, List<Vector2Int> list)
        {
            int k = AnchorCellKey(p);
            if (keys.Add(k))
                list.Add(new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y)));
        }

        static HashSet<int> BuildLockedAnchorKeys(
            GridSystem grid,
            List<Vector2> rawFunctional,
            int riverIndex,
            MapGenConfig config,
            out List<Vector2Int> lockedList,
            out int fordAnchorCount,
            out float fordMaxDistance)
        {
            lockedList = new List<Vector2Int>();
            fordAnchorCount = 0;
            fordMaxDistance = 0f;
            var keys = new HashSet<int>();
            if (rawFunctional == null || rawFunctional.Count < 2)
                return keys;

            AddAnchorKey(keys, rawFunctional[0], lockedList);
            AddAnchorKey(keys, rawFunctional[rawFunctional.Count - 1], lockedList);
            for (int i = 0; i < rawFunctional.Count; i++)
                AddAnchorKey(keys, rawFunctional[i], lockedList);

            for (int i = 1; i < rawFunctional.Count - 1; i++)
            {
                Vector2 a = rawFunctional[i - 1];
                Vector2 b = rawFunctional[i];
                Vector2 c = rawFunctional[i + 1];
                Vector2 d0 = b - a;
                Vector2 d1 = c - b;
                if (d0.sqrMagnitude < 1e-10f || d1.sqrMagnitude < 1e-10f)
                    continue;
                float dot = Mathf.Clamp(Vector2.Dot(d0.normalized, d1.normalized), -1f, 1f);
                if (Vector2.Angle(d0, d1) >= Mathf.Max(35f, config.riverSurfaceSharpBendAngleDeg - 8f))
                    AddAnchorKey(keys, b, lockedList);
            }

            if (riverIndex > 0 && grid.RiverCenterlinesCellSpace != null)
            {
                var joinKeys = BuildJoinProximityCellKeys(grid.RiverCenterlinesCellSpace, riverIndex, grid.Width, grid.Height);
                for (int i = 0; i < rawFunctional.Count; i++)
                {
                    int cx = Mathf.Clamp(Mathf.FloorToInt(rawFunctional[i].x), 0, grid.Width - 1);
                    int cy = Mathf.Clamp(Mathf.FloorToInt(rawFunctional[i].y), 0, grid.Height - 1);
                    if (joinKeys.Contains(PackCellKey(cx, cy)))
                        AddAnchorKey(keys, rawFunctional[i], lockedList);
                }
            }

            int fordSearch = Mathf.Max(2, config.riverVisualFordKeepDistanceCells + 2);
            for (int i = 0; i < rawFunctional.Count - 1; i++)
            {
                Vector2 a = rawFunctional[i];
                Vector2 b = rawFunctional[i + 1];
                int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x)) - fordSearch, 0, grid.Width - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x)) + fordSearch, 0, grid.Width - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y)) - fordSearch, 0, grid.Height - 1);
                int z1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y)) + fordSearch, 0, grid.Height - 1);
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        if (!grid.GetCell(x, z).riverFord)
                            continue;
                        Vector2 cellCenter = new Vector2(x + 0.5f, z + 0.5f);
                        float dSeg = DistancePointToOpenSegment2D(cellCenter, a, b);
                        fordMaxDistance = Mathf.Max(fordMaxDistance, dSeg);
                        if (dSeg <= fordSearch + 0.5f)
                        {
                            Vector2 onPath = ClosestPointOnOpenSegment2D(cellCenter, a, b);
                            AddAnchorKey(keys, onPath, lockedList);
                            fordAnchorCount++;
                        }
                    }
                }
            }

            return keys;
        }

        static Vector2 ClosestPointOnOpenSegment2D(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float den = Vector2.Dot(ab, ab);
            if (den < 1e-10f)
                return a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / den);
            return a + ab * t;
        }

        static float DistancePointToPolyline2D(Vector2 p, List<Vector2> poly)
        {
            if (poly == null || poly.Count < 2)
                return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < poly.Count - 1; i++)
                best = Mathf.Min(best, DistancePointToOpenSegment2D(p, poly[i], poly[i + 1]));
            return best;
        }

        static List<Vector2> ProcessCenterlineCellSpaceAnchored(
            List<Vector2> cellPath,
            HashSet<int> lockedKeys,
            MapGenConfig config,
            out int afterColinear,
            out int afterSmooth,
            out int afterResample)
        {
            afterColinear = afterSmooth = afterResample = 0;
            var pts = new List<Vector2>(cellPath);
            pts = DedupeConsecutiveCell(pts, DedupeCellEps);
            pts = RemoveNearNullSegmentsCell(pts, MinSegmentCellEps);
            pts = RemoveCollinearPointsCellAnchored(pts, CollinearDotThreshold, lockedKeys);
            afterColinear = pts != null ? pts.Count : 0;
            if (pts == null || pts.Count < 2)
                return null;

            pts = InsertSharpBendMidpointsCell(pts, config.riverSurfaceSharpBendAngleDeg);

            int chaikinPasses = Mathf.Clamp(config.riverSurfaceChaikinPasses, 0, 2);
            if (chaikinPasses > 0)
            {
                var chaikin = ChaikinOpenCellAnchored(pts, chaikinPasses, lockedKeys);
                if (!PolylineSelfIntersectsXZCell(chaikin))
                    pts = chaikin;
            }

            afterSmooth = pts.Count;
            int pathCells = cellPath.Count;
            int maxPts = Mathf.Max(8, Mathf.CeilToInt(pathCells * Mathf.Max(1.02f, config.riverSurfaceMaxVisualPointRatio)));
            float spacing = Mathf.Max(0.08f, config.riverSurfaceSampleSpacingCells);
            pts = ResampleUniformSpacingCellAnchored(pts, spacing, maxPts, lockedKeys);
            afterResample = pts != null ? pts.Count : 0;
            return pts;
        }

        static List<Vector2> RemoveCollinearPointsCellAnchored(List<Vector2> pts, float dotThresh, HashSet<int> lockedKeys)
        {
            if (pts == null || pts.Count < 3)
                return pts;
            var r = new List<Vector2>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                if (lockedKeys.Contains(AnchorCellKey(pts[i])))
                {
                    r.Add(pts[i]);
                    continue;
                }

                Vector2 a = r[r.Count - 1];
                Vector2 b = pts[i];
                Vector2 c = pts[i + 1];
                Vector2 d0 = b - a;
                Vector2 d1 = c - b;
                if (d0.sqrMagnitude < 1e-12f || d1.sqrMagnitude < 1e-12f)
                {
                    r.Add(b);
                    continue;
                }

                float dot = Mathf.Clamp(Vector2.Dot(d0.normalized, d1.normalized), -1f, 1f);
                if (dot > dotThresh)
                    continue;
                r.Add(b);
            }

            r.Add(pts[pts.Count - 1]);
            return r;
        }

        static List<Vector2> ChaikinOpenCellAnchored(List<Vector2> pts, int passes, HashSet<int> lockedKeys)
        {
            var cur = new List<Vector2>(pts);
            for (int p = 0; p < passes && cur.Count >= 3; p++)
            {
                var next = new List<Vector2> { cur[0] };
                for (int i = 0; i < cur.Count - 1; i++)
                {
                    Vector2 a = cur[i];
                    Vector2 b = cur[i + 1];
                    if (lockedKeys.Contains(AnchorCellKey(a)) || lockedKeys.Contains(AnchorCellKey(b)))
                    {
                        if (i > 0 || next[next.Count - 1] != a)
                            next.Add(a);
                        next.Add(b);
                        continue;
                    }

                    Vector2 q = 0.75f * a + 0.25f * b;
                    Vector2 r = 0.25f * a + 0.75f * b;
                    next.Add(q);
                    next.Add(r);
                }

                if (next[next.Count - 1] != cur[cur.Count - 1])
                    next.Add(cur[cur.Count - 1]);
                cur = next;
            }

            return cur;
        }

        static List<Vector2> ResampleUniformSpacingCellAnchored(
            List<Vector2> pts,
            float spacing,
            int maxPts,
            HashSet<int> lockedKeys)
        {
            if (pts == null || pts.Count < 2)
                return pts;
            float total = PolylineLengthCellSpace(pts);
            if (total < spacing * 0.5f)
                return new List<Vector2>(pts);
            int target = Mathf.Clamp(Mathf.FloorToInt(total / spacing) + 1, 2, maxPts);
            var result = new List<Vector2>(target + lockedKeys.Count);
            var forced = new List<Vector2>();
            for (int i = 0; i < pts.Count; i++)
            {
                if (lockedKeys.Contains(AnchorCellKey(pts[i])))
                    forced.Add(pts[i]);
            }

            float step = total / (target - 1);
            float acc = 0f;
            int seg = 0;
            result.Add(pts[0]);
            for (int k = 1; k < target - 1; k++)
            {
                float want = k * step;
                while (seg < pts.Count - 1)
                {
                    float segLen = Vector2.Distance(pts[seg], pts[seg + 1]);
                    if (acc + segLen >= want)
                    {
                        float t = segLen > 1e-8f ? (want - acc) / segLen : 0f;
                        result.Add(Vector2.Lerp(pts[seg], pts[seg + 1], t));
                        break;
                    }

                    acc += segLen;
                    seg++;
                }
            }

            result.Add(pts[pts.Count - 1]);
            for (int i = 0; i < forced.Count; i++)
            {
                Vector2 f = forced[i];
                bool exists = false;
                for (int j = 0; j < result.Count; j++)
                {
                    if ((result[j] - f).sqrMagnitude < DedupeCellEps * DedupeCellEps)
                    {
                        exists = true;
                        result[j] = f;
                        break;
                    }
                }

                if (!exists)
                    InsertPointSortedAlongPolyline(result, f);
            }

            return result;
        }

        static void InsertPointSortedAlongPolyline(List<Vector2> poly, Vector2 p)
        {
            if (poly == null || poly.Count < 2)
            {
                poly?.Add(p);
                return;
            }

            float bestT = 0f;
            int bestSeg = 0;
            float bestD = float.MaxValue;
            float acc = 0f;
            for (int i = 0; i < poly.Count - 1; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[i + 1];
                Vector2 ab = b - a;
                float len = ab.magnitude;
                float t = len > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / (len * len)) : 0f;
                Vector2 q = a + ab * t;
                float d = (p - q).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    bestSeg = i;
                    bestT = acc + t * len;
                }

                acc += len;
            }

            for (int i = bestSeg + 1; i < poly.Count; i++)
            {
                float segStart = 0f;
                for (int s = 0; s < i; s++)
                    segStart += Vector2.Distance(poly[s], poly[s + 1]);
                if (bestT <= segStart + 1e-5f)
                {
                    poly.Insert(i, p);
                    return;
                }
            }

            poly.Insert(bestSeg + 1, p);
        }

        static void EnforcePathFitToFunctional(
            List<Vector2> processed,
            List<Vector2> functional,
            HashSet<int> lockedKeys,
            float maxDevCells,
            out float maxDev,
            out float avgDev,
            out int reverted)
        {
            maxDev = 0f;
            avgDev = 0f;
            reverted = 0;
            if (processed == null || functional == null || processed.Count == 0)
                return;
            float sum = 0f;
            for (int i = 0; i < processed.Count; i++)
            {
                if (lockedKeys.Contains(AnchorCellKey(processed[i])))
                {
                    float dLocked = DistancePointToPolyline2D(processed[i], functional);
                    maxDev = Mathf.Max(maxDev, dLocked);
                    sum += dLocked;
                    continue;
                }

                float d = DistancePointToPolyline2D(processed[i], functional);
                maxDev = Mathf.Max(maxDev, d);
                sum += d;
                if (d > maxDevCells)
                {
                    processed[i] = ClosestPointOnPolyline2D(processed[i], functional);
                    reverted++;
                    d = DistancePointToPolyline2D(processed[i], functional);
                    maxDev = Mathf.Max(maxDev, d);
                }
            }

            avgDev = sum / Mathf.Max(1, processed.Count);
        }

        static Vector2 ClosestPointOnPolyline2D(Vector2 p, List<Vector2> poly)
        {
            if (poly == null || poly.Count == 0)
                return p;
            if (poly.Count == 1)
                return poly[0];
            float bestD = float.MaxValue;
            Vector2 best = poly[0];
            for (int i = 0; i < poly.Count - 1; i++)
            {
                Vector2 q = ClosestPointOnOpenSegment2D(p, poly[i], poly[i + 1]);
                float d = (p - q).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = q;
                }
            }

            return best;
        }

        static bool ClipSegmentToPlayableRect(
            Vector2 a,
            Vector2 b,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            out Vector2 ca,
            out Vector2 cb)
        {
            ca = a;
            cb = b;
            float t0 = 0f;
            float t1 = 1f;
            Vector2 d = b - a;
            bool Clip(float p, float q)
            {
                if (Mathf.Abs(p) < 1e-8f)
                    return q >= 0f;
                float r = q / p;
                if (p < 0f)
                {
                    if (r > t1)
                        return false;
                    if (r > t0)
                        t0 = r;
                }
                else
                {
                    if (r < t0)
                        return false;
                    if (r < t1)
                        t1 = r;
                }

                return true;
            }

            if (!Clip(-d.x, a.x - minX))
                return false;
            if (!Clip(d.x, maxX - a.x))
                return false;
            if (!Clip(-d.y, a.y - minZ))
                return false;
            if (!Clip(d.y, maxZ - a.y))
                return false;

            ca = a + d * t0;
            cb = a + d * t1;
            return t1 > t0 + 1e-6f;
        }

        static List<Vector2> PreClipCenterlineCellSpace(List<Vector2> cellPath, int w, int h, out bool startClipped, out bool endClipped)
        {
            startClipped = endClipped = false;
            if (cellPath == null || cellPath.Count < 2)
                return cellPath;
            float minX = 0.5f;
            float maxX = (w - 1) + 0.5f;
            float minZ = 0.5f;
            float maxZ = (h - 1) + 0.5f;

            bool Inside(Vector2 p) => p.x >= minX - 1e-4f && p.x <= maxX + 1e-4f && p.y >= minZ - 1e-4f && p.y <= maxZ + 1e-4f;
            startClipped = !Inside(cellPath[0]);
            endClipped = !Inside(cellPath[cellPath.Count - 1]);

            var clipped = new List<Vector2>(cellPath.Count + 4);
            for (int i = 0; i < cellPath.Count - 1; i++)
            {
                Vector2 a = cellPath[i];
                Vector2 b = cellPath[i + 1];
                if (!ClipSegmentToPlayableRect(a, b, minX, maxX, minZ, maxZ, out Vector2 ca, out Vector2 cb))
                    continue;
                if (clipped.Count == 0 || (clipped[clipped.Count - 1] - ca).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                    clipped.Add(ca);
                if ((ca - cb).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                    clipped.Add(cb);
            }

            if (clipped.Count < 2)
            {
                clipped.Clear();
                for (int i = 0; i < cellPath.Count; i++)
                {
                    Vector2 c = cellPath[i];
                    c.x = Mathf.Clamp(c.x, minX, maxX);
                    c.y = Mathf.Clamp(c.y, minZ, maxZ);
                    clipped.Add(c);
                }
            }

            return clipped.Count >= 2 ? clipped : cellPath;
        }

        static void ApplyFordWidthDampening(GridSystem grid, List<Vector2> cellPath, List<float> halfWidths, MapGenConfig config)
        {
            if (grid == null || cellPath == null || halfWidths == null || cellPath.Count != halfWidths.Count)
                return;
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            for (int i = 0; i < cellPath.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(cellPath[i].x), 0, grid.Width - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(cellPath[i].y), 0, grid.Height - 1);
                if (WaterMeshBuilder.GridCellNearFordRiverChebyshev(grid, cx, cy, fordD))
                    halfWidths[i] = Mathf.Max(0.02f, halfWidths[i] * 0.94f);
            }
        }

        static int CullTrianglesOutsideVisualMask(
            List<Vector3> verts,
            List<int> tris,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            Vector3 origin,
            float cellSize)
        {
            if (verts == null || tris == null || tris.Count < 6 || grid?.RiverVisualSurfaceMask == null)
                return 0;
            bool[,] mask = grid.RiverVisualSurfaceMask;
            int gw = grid.Width;
            int gh = grid.Height;
            int margin = config != null ? Mathf.Clamp(config.riverVisualTriangleCullMaskMarginCells, 0, 3) : 1;
            int fordD = Mathf.Max(1, config != null ? config.riverVisualFordKeepDistanceCells : 5);
            float invCs = 1f / Mathf.Max(1e-5f, cellSize);
            int before = tris.Count / 3;
            var kept = new List<int>(tris.Count);

            bool NearMask(int cx, int cz)
            {
                for (int dz = -margin; dz <= margin; dz++)
                {
                    for (int dx = -margin; dx <= margin; dx++)
                    {
                        int nx = cx + dx;
                        int nz = cz + dz;
                        if ((uint)nx < (uint)gw && (uint)nz < (uint)gh && mask[nx, nz])
                            return true;
                    }
                }

                return false;
            }

            for (int t = 0; t < tris.Count; t += 3)
            {
                int i0 = tris[t];
                int i1 = tris[t + 1];
                int i2 = tris[t + 2];
                if ((uint)i0 >= (uint)verts.Count || (uint)i1 >= (uint)verts.Count || (uint)i2 >= (uint)verts.Count)
                    continue;
                Vector3 c = (verts[i0] + verts[i1] + verts[i2]) / 3f;
                int cx = Mathf.Clamp(Mathf.FloorToInt((c.x - origin.x) * invCs), 0, gw - 1);
                int cz = Mathf.Clamp(Mathf.FloorToInt((c.z - origin.z) * invCs), 0, gh - 1);
                if (mask[cx, cz] || NearMask(cx, cz))
                {
                    kept.Add(i0);
                    kept.Add(i1);
                    kept.Add(i2);
                    continue;
                }

                if (WaterMeshBuilder.GridCellNearFordRiverChebyshev(grid, cx, cz, fordD))
                {
                    kept.Add(i0);
                    kept.Add(i1);
                    kept.Add(i2);
                }
            }

            int culled = before - kept.Count / 3;
            tris.Clear();
            tris.AddRange(kept);
            return culled;
        }

        public static void LogRiverVisualCacheUse(string consumer, GridSystem grid, int riverIndex)
        {
            if (grid == null || !grid.RiverVisualSurfacesBuilt)
                return;
            if (grid.RiverVisualSurfaces == null || grid.RiverVisualSurfaces.Count == 0)
                return;
            Debug.Log(
                $"[RiverVisualCacheUse] consumer={consumer} riverIndex={riverIndex} usedCachedMask=1 usedCachedCenterline=1 " +
                $"surfaces={grid.RiverVisualSurfaces.Count}");
        }

        /// <summary>
        /// Fase 1 (auditoría): antes del cache, mesh y máscara repetían ProcessCenterline + meander + clip distinto;
        /// el clamp post-malla (verts_clamped) deformaba triángulos. Este método unifica la verdad visual.
        /// </summary>
        public static bool EnsureRiverVisualSurfaceCache(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null || !config.riverVisualUseRiverSurfaceMeshStrip)
                return false;
            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return false;

            if (config.riverVisualSurfaceCacheEnabled &&
                grid.RiverVisualSurfacesBuilt &&
                grid.RiverVisualSurfaces != null &&
                grid.RiverVisualSurfaceMask != null &&
                grid.RiverVisualSurfaceMask.GetLength(0) == grid.Width &&
                grid.RiverVisualSurfaceMask.GetLength(1) == grid.Height &&
                grid.RiverVisualSurfaces.Count == grid.RiverCenterlinesCellSpace.Count)
            {
                return true;
            }

            grid.ClearRiverVisualSurfaceCache();
            float cellSize = grid.CellSizeWorld;
            int w = grid.Width;
            int h = grid.Height;
            Vector3 origin = grid.Origin;
            float inset = Mathf.Max(0f, config.riverVisualBankInset);
            float marginCells = Mathf.Max(0f, config.riverVisualRasterMaskExtraCellMargin);
            float maxDev = Mathf.Max(0.2f, config.riverVisualMaxPathDeviationCells);
            bool logDetail = config.debugLogs || config.debugHydrologyNetwork;
            var combinedMask = new bool[w, h];
            var surfaces = new List<RiverVisualSurfaceData>(grid.RiverCenterlinesCellSpace.Count);

            for (int riverIndex = 0; riverIndex < grid.RiverCenterlinesCellSpace.Count; riverIndex++)
            {
                var rawPath = grid.RiverCenterlinesCellSpace[riverIndex];
                var surface = new RiverVisualSurfaceData { RiverIndex = riverIndex, BuiltFromFunctionalPath = true };
                if (rawPath == null || rawPath.Count < 2)
                {
                    surface.Skipped = true;
                    surface.SkipReason = "raw_too_short";
                    surfaces.Add(surface);
                    continue;
                }

                surface.RawFunctionalCenterlineCells = new List<Vector2>(rawPath);
                var lockedKeys = BuildLockedAnchorKeys(grid, rawPath, riverIndex, config, out var lockedAnchors, out int fordAnchors, out float fordMaxDist);
                surface.LockedAnchorCells = lockedAnchors;
                surface.FordAnchorCount = fordAnchors;
                surface.FordMaxDistanceCells = fordMaxDist;

                if (logDetail)
                {
                    Debug.Log(
                        $"[RiverVisualFordAnchor] riverIndex={riverIndex} fordAnchors={fordAnchors} " +
                        $"maxDistanceToFordCell={fordMaxDist:F3} accepted={(fordMaxDist <= 0.35f ? 1 : 0)}");
                }

                var cellProcessed = ProcessCenterlineCellSpaceAnchored(rawPath, lockedKeys, config, out _, out _, out _);
                if (cellProcessed == null || cellProcessed.Count < 2)
                {
                    surface.Skipped = true;
                    surface.SkipReason = "process_failed";
                    surfaces.Add(surface);
                    continue;
                }

                if (riverIndex > 0 && TryCullTributarySurfacePiece(grid, cellProcessed, riverIndex, config, logDetail))
                {
                    surface.Skipped = true;
                    surface.SkipReason = "tributary_cull";
                    surfaces.Add(surface);
                    continue;
                }

                EnforcePathFitToFunctional(
                    cellProcessed,
                    rawPath,
                    lockedKeys,
                    maxDev,
                    out float maxD,
                    out float avgD,
                    out int reverted);
                surface.PathFitMaxDeviationCells = maxD;
                surface.PathFitAvgDeviationCells = avgD;
                surface.PathFitRevertedPoints = reverted;

                if (logDetail)
                {
                    Debug.Log(
                        $"[RiverVisualPathFit] riverIndex={riverIndex} maxDeviationCells={maxD:F3} avgDeviationCells={avgD:F3} " +
                        $"lockedAnchors={lockedAnchors.Count} revertedSegments={reverted} accepted={(maxD <= maxDev + 1e-4f ? 1 : 0)}");
                }

                surface.PreClipInputPoints = cellProcessed.Count;
                cellProcessed = PreClipCenterlineCellSpace(cellProcessed, w, h, out bool clipStart, out bool clipEnd);
                surface.PreClipOutputPoints = cellProcessed != null ? cellProcessed.Count : 0;
                surface.PreClipStart = clipStart;
                surface.PreClipEnd = clipEnd;

                if (logDetail)
                {
                    Debug.Log(
                        $"[RiverSurfacePreClip] riverIndex={riverIndex} inputPoints={surface.PreClipInputPoints} " +
                        $"outputPoints={surface.PreClipOutputPoints} startClipped={(clipStart ? 1 : 0)} endClipped={(clipEnd ? 1 : 0)} " +
                        $"visibleOutsideBounds=0");
                }

                if (cellProcessed == null || cellProcessed.Count < 2)
                {
                    surface.Skipped = true;
                    surface.SkipReason = "preclip_empty";
                    surfaces.Add(surface);
                    continue;
                }

                surface.FinalCenterlineCells = cellProcessed;

                float fullCellsW = riverIndex == 0
                    ? config.riverVisualRibbonFullWidthCellsMain
                    : (config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                        ? config.riverVisualRibbonFullWidthCellsTributary
                        : config.riverVisualRibbonFullWidthCellsMain);
                float baseHalfW = fullCellsW > 0.01f
                    ? Mathf.Max(0.08f, fullCellsW * 0.5f * cellSize - inset)
                    : Mathf.Max(0.08f, config.riverVisualMeshHalfWidth - inset);

                var worldForWidth = CellPolylineToWorldXZ(cellProcessed, origin, cellSize, 0f);
                float amp = riverIndex == 0
                    ? Mathf.Max(0f, config.riverSurfaceWidthNoiseAmpMain)
                    : Mathf.Max(0f, config.riverSurfaceWidthNoiseAmpTributary);
                float noiseScale = Mathf.Max(0.0001f, config.riverSurfaceWidthNoiseScale);
                List<float> halfWidths;
                if (riverIndex == 0)
                {
                    var joinCells = BuildJoinProximityCellKeys(grid.RiverCenterlinesCellSpace, 0, w, h);
                    halfWidths = BuildMainRiverHalfWidthsWithArcVariation(
                        grid,
                        worldForWidth,
                        cellProcessed,
                        baseHalfW,
                        amp,
                        noiseScale,
                        joinCells,
                        config,
                        out _,
                        out _);
                }
                else
                {
                    halfWidths = BuildHalfWidthsDeterministic(worldForWidth, baseHalfW, amp, noiseScale, riverIndex);
                }

                ApplyFordWidthDampening(grid, cellProcessed, halfWidths, config);
                surface.HalfWidthsWorld = halfWidths;

                int maskCells = RasterStripCellSpaceToMask(combinedMask, w, h, cellProcessed, halfWidths, cellSize, marginCells);
                if (logDetail)
                {
                    Debug.Log(
                        $"[RiverVisualSurfaceCache] riverIndex={riverIndex} rawPoints={surface.RawFunctionalCenterlineCells.Count} " +
                        $"finalPoints={cellProcessed.Count} maskCells={maskCells} lockedAnchors={lockedAnchors.Count} builtOnce=1 " +
                        $"source=functional_centerline");
                }

                surfaces.Add(surface);
            }

            int maskBefore = CountMaskTrue(combinedMask, w, h);
            MorphologicalClose1(combinedMask, w, h);
            int maskAfter = CountMaskTrue(combinedMask, w, h);
            if (logDetail)
            {
                Debug.Log(
                    $"[RiverVisualContinuity] holesFilled={Mathf.Max(0, maskAfter - maskBefore)} strayRemoved=0 " +
                    $"maskCellsBefore={maskBefore} maskCellsAfter={maskAfter}");
            }

            grid.RiverVisualSurfaces = surfaces;
            grid.RiverVisualSurfaceMask = combinedMask;
            grid.RiverVisualSurfacesBuilt = true;
            return true;
        }

        static int RasterStripCellSpaceToMask(
            bool[,] mask,
            int w,
            int h,
            List<Vector2> cellPath,
            List<float> halfWidthsWorld,
            float cellSize,
            float marginCells)
        {
            int added = 0;
            int n = cellPath != null ? cellPath.Count : 0;
            if (n < 2 || halfWidthsWorld == null || halfWidthsWorld.Count != n)
                return 0;
            float invCs = 1f / Mathf.Max(1e-5f, cellSize);
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 a = cellPath[i];
                Vector2 b = cellPath[i + 1];
                float hwCells =
                    0.5f * (halfWidthsWorld[i] + halfWidthsWorld[i + 1]) * invCs + marginCells;
                float pad = hwCells + 2f;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x) - pad), 0, w - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x) + pad), 0, w - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y) - pad), 0, h - 1);
                int z1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y) + pad), 0, h - 1);
                for (int cz = z0; cz <= z1; cz++)
                {
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        Vector2 p = new Vector2(cx + 0.5f, cz + 0.5f);
                        float d = DistancePointToOpenSegment2D(p, a, b);
                        if (d <= hwCells)
                        {
                            if (!mask[cx, cz])
                            {
                                mask[cx, cz] = true;
                                added++;
                            }
                        }
                    }
                }
            }

            return added;
        }

        static float DistancePointToOpenSegment2D(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = Vector2.Dot(ab, ab);
            if (denom < 1e-10f)
                return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
            Vector2 proj = a + ab * t;
            return Vector2.Distance(p, proj);
        }

        static int CountMaskTrue(bool[,] mask, int w, int h)
        {
            int c = 0;
            if (mask == null)
                return 0;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    if (mask[x, z])
                        c++;
            return c;
        }

        static void MorphologicalClose1(bool[,] mask, int w, int h)
        {
            if (mask == null)
                return;
            var dil = new bool[w, h];
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool v = mask[x, z];
                    if (!v)
                    {
                        for (int dz = -1; dz <= 1 && !v; dz++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, nz = z + dz;
                                if ((uint)nx < (uint)w && (uint)nz < (uint)h && mask[nx, nz])
                                {
                                    v = true;
                                    break;
                                }
                            }
                        }
                    }

                    dil[x, z] = v;
                }
            }

            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool v = dil[x, z];
                    if (v)
                    {
                        for (int dz = -1; dz <= 1 && v; dz++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, nz = z + dz;
                                if ((uint)nx < (uint)w && (uint)nz < (uint)h && !dil[nx, nz])
                                {
                                    v = false;
                                    break;
                                }
                            }
                        }
                    }

                    mask[x, z] = v;
                }
            }
        }

        static Vector3 PerpendicularXZ(Vector3 tangent)
        {
            Vector3 right = Vector3.Cross(Vector3.up, tangent);
            right.y = 0f;
            if (right.sqrMagnitude < 1e-12f)
                right = Vector3.right;
            else
                right.Normalize();
            return right;
        }
    }
}
