using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Centerline de río RTS: nodos macro, meandros amplios, suavizado y remuestreo uniforme en espacio celda.
    /// El raster de celdas sigue la misma curva para alinear gameplay y ribbon.
    /// </summary>
    public static class RiverPathBuilder
    {
        private static long PackCell(Vector2Int c) => ((long)c.x << 32) | (uint)c.y;

        public static bool TryBuildSmoothCenterlineAndRaster(
            int w,
            int h,
            Vector2Int start,
            Vector2Int end,
            MapGenConfig config,
            IRng rng,
            out List<Vector2> centerlineCellSpace,
            out List<Vector2Int> rasterCells,
            out List<Vector2Int> fordCells,
            out List<Vector2> debugMacro,
            out List<Vector2> debugAfterSmooth)
        {
            centerlineCellSpace = null;
            rasterCells = null;
            fordCells = null;
            debugMacro = null;
            debugAfterSmooth = null;

            Vector2 s = new Vector2(start.x + 0.5f, start.y + 0.5f);
            Vector2 e = new Vector2(end.x + 0.5f, end.y + 0.5f);
            Vector2 chord = e - s;
            float chordLen = chord.magnitude;
            if (chordLen < 0.5f)
                return false;

            var macro = BuildMacroControlPolyline(w, h, s, e, config, rng);
            if (macro == null || macro.Count < 2)
                return false;

            debugMacro = new List<Vector2>(macro);

            float spacingRough = Mathf.Max(0.06f, Mathf.Clamp(config.riverCenterlineSampleSpacingCells, 0.06f, 0.55f) * 0.45f);
            var dense = CatmullRomSampleOpenPolyline(macro, spacingRough);
            if (dense == null || dense.Count < 2)
                return false;

            ApplyLaplacianSmoothClosedBounds(dense, w, h, config.riverSmoothingPasses, config.riverSmoothingStrength);

            if (config.riverStraightnessPenalty > 1e-5f)
                ApplyStraightnessPenalty(dense, w, h, config.riverStraightnessPenalty, Mathf.Min(w, h));

            RelaxSharpTurns(dense, w, h, config.riverMaxTurnAngleDegrees, config.riverMinCurveRadiusCells);

            float spacingFinal = Mathf.Clamp(config.riverCenterlineSampleSpacingCells, 0.08f, 0.55f);
            centerlineCellSpace = ResamplePolylineUniform2D(dense, spacingFinal);
            if (centerlineCellSpace == null || centerlineCellSpace.Count < 2)
                return false;

            DedupeConsecutive2D(centerlineCellSpace, 0.02f);

            debugAfterSmooth = new List<Vector2>(centerlineCellSpace);

            rasterCells = RasterCenterlineToCells(w, h, start, centerlineCellSpace, config, rng, out fordCells);
            return rasterCells != null && rasterCells.Count >= 2;
        }

        private static List<Vector2> BuildMacroControlPolyline(int w, int h, Vector2 s, Vector2 e, MapGenConfig config, IRng rng)
        {
            Vector2 d = e - s;
            float dist = d.magnitude;
            if (dist < 1e-4f)
                return null;

            Vector2 dirN = d / dist;
            Vector2 perp = new Vector2(-dirN.y, dirN.x);
            float mapR = Mathf.Min(w, h);
            int nCtrl = Mathf.Clamp(config.riverMacroNodeCount, 3, 18);
            float phase = rng.NextFloat() * Mathf.PI * 2f;
            float phaseSlow = rng.NextFloat() * Mathf.PI * 2f;
            float fBend = Mathf.Max(0.2f, config.riverMacroBendFrequency);
            float fSlow = Mathf.Clamp(config.riverMacroSlowBendFrequency, 0.12f, 1.8f);
            float wSlow = Mathf.Clamp(config.riverMacroSlowBendWeight, 0f, 1.2f);
            float str = Mathf.Clamp01(config.riverMacroBendStrength) * mapR;
            float nStr = Mathf.Clamp(config.riverLateralNoiseStrength, 0f, 0.5f) * mapR;
            float nSc = Mathf.Max(0.3f, config.riverLateralNoiseScale);

            float a1 = rng.NextFloat() * Mathf.PI * 2f;
            float a2 = rng.NextFloat() * Mathf.PI * 2f;
            float a3 = rng.NextFloat() * Mathf.PI * 2f;
            float phasePulse = rng.NextFloat() * Mathf.PI * 2f;
            float secFreq = Mathf.Clamp(config.riverCurvatureSectionFrequency, 0.08f, 3.5f);
            float secContrast = Mathf.Clamp01(config.riverCurvatureSectionContrast);

            var ctrl = new List<Vector2>(nCtrl);
            float margin = 0.55f;
            for (int i = 0; i < nCtrl; i++)
            {
                float t = nCtrl <= 1 ? 0f : i / (float)(nCtrl - 1);
                float te = Mathf.SmoothStep(0f, 1f, t);
                Vector2 baseP = Vector2.Lerp(s, e, te);
                float bulge = Mathf.Sin(t * Mathf.PI);
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f * secFreq + phasePulse);
                float sectionGain = Mathf.Lerp(1f - secContrast * 0.85f, 1f, pulse);
                sectionGain = Mathf.Max(0.12f, sectionGain);

                float bendMain = Mathf.Sin(t * Mathf.PI * 2f * fBend + phase) * str * bulge * sectionGain;
                float bendSlow = 0f;
                if (wSlow > 1e-4f)
                    bendSlow = Mathf.Sin(t * Mathf.PI * 2f * fSlow + phaseSlow) * str * wSlow * bulge * sectionGain;
                float nt = t * nSc * Mathf.PI * 2f;
                float noise = Mathf.Sin(nt + a1) * 0.45f + Mathf.Sin(nt * 1.73f + a2) * 0.35f + Mathf.Sin(nt * 0.61f + a3) * 0.2f;
                float noiseGain = Mathf.Lerp(0.45f, 1f, sectionGain);
                float bend = bendMain + bendSlow + noise * nStr * (0.35f + 0.65f * bulge) * noiseGain;

                Vector2 p = baseP + perp * bend;
                p.x = Mathf.Clamp(p.x, margin, w - margin);
                p.y = Mathf.Clamp(p.y, margin, h - margin);
                ctrl.Add(p);
            }

            return ctrl;
        }

        private static Vector2 CatmullRom2D(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        private static List<Vector2> CatmullRomSampleOpenPolyline(List<Vector2> ctrl, float spacingCells)
        {
            if (ctrl == null || ctrl.Count < 2)
                return null;
            spacingCells = Mathf.Max(0.05f, spacingCells);

            var ext = new List<Vector2>(ctrl.Count + 2);
            ext.Add(ctrl[0] * 2f - ctrl[1]);
            ext.AddRange(ctrl);
            ext.Add(ctrl[ctrl.Count - 1] * 2f - ctrl[ctrl.Count - 2]);

            var o = new List<Vector2>(64);
            int nSeg = ctrl.Count - 1;
            for (int seg = 0; seg < nSeg; seg++)
            {
                Vector2 p0 = ext[seg];
                Vector2 p1 = ext[seg + 1];
                Vector2 p2 = ext[seg + 2];
                Vector2 p3 = ext[seg + 3];
                float chord = Vector2.Distance(p1, p2);
                int steps = Mathf.Max(2, Mathf.CeilToInt(chord / spacingCells));
                for (int s = 0; s < steps; s++)
                {
                    if (seg > 0 && s == 0)
                        continue;
                    float t = s / (float)steps;
                    o.Add(CatmullRom2D(p0, p1, p2, p3, t));
                }
            }

            o.Add(ctrl[ctrl.Count - 1]);
            return o;
        }

        private static void ApplyLaplacianSmoothClosedBounds(List<Vector2> poly, int w, int h, int passes, float alpha)
        {
            if (poly == null || poly.Count < 3 || passes <= 0)
                return;
            alpha = Mathf.Clamp01(alpha);
            float minX = 0.5f;
            float maxX = Mathf.Max(minX, w - 0.5f);
            float minY = 0.5f;
            float maxY = Mathf.Max(minY, h - 0.5f);
            for (int it = 0; it < passes; it++)
            {
                var copy = new List<Vector2>(poly);
                for (int i = 1; i < poly.Count - 1; i++)
                {
                    Vector2 avg = (copy[i - 1] + copy[i + 1]) * 0.5f;
                    Vector2 p = Vector2.Lerp(copy[i], avg, alpha);
                    p.x = Mathf.Clamp(p.x, minX, maxX);
                    p.y = Mathf.Clamp(p.y, minY, maxY);
                    poly[i] = p;
                }
            }
        }

        private static void ApplyStraightnessPenalty(List<Vector2> poly, int w, int h, float penalty01, float mapMin)
        {
            if (poly == null || poly.Count < 5)
                return;
            float mag = penalty01 * mapMin;
            if (mag < 1e-5f)
                return;
            float minX = 0.5f;
            float maxX = Mathf.Max(minX, w - 0.5f);
            float minY = 0.5f;
            float maxY = Mathf.Max(minY, h - 0.5f);

            for (int i = 2; i < poly.Count - 2; i++)
            {
                Vector2 a = (poly[i] - poly[i - 1]).normalized;
                Vector2 b = (poly[i + 1] - poly[i]).normalized;
                if (Vector2.Dot(a, b) > 0.992f)
                {
                    Vector2 perp = new Vector2(-a.y, a.x);
                    float sign = (i & 1) == 0 ? 1f : -1f;
                    Vector2 p = poly[i] + perp * (mag * sign * 0.35f);
                    p.x = Mathf.Clamp(p.x, minX, maxX);
                    p.y = Mathf.Clamp(p.y, minY, maxY);
                    poly[i] = p;
                }
            }
        }

        private static void RelaxSharpTurns(List<Vector2> poly, int w, int h, float maxTurnDeg, float minRadiusCells)
        {
            if (poly == null || poly.Count < 3)
                return;
            float maxDot = Mathf.Cos(Mathf.Clamp(maxTurnDeg, 20f, 179f) * Mathf.Deg2Rad);
            float minR = Mathf.Max(0f, minRadiusCells);
            float minX = 0.5f;
            float maxX = Mathf.Max(minX, w - 0.5f);
            float minY = 0.5f;
            float maxY = Mathf.Max(minY, h - 0.5f);

            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 1; i < poly.Count - 1; i++)
                {
                    Vector2 p0 = poly[i - 1];
                    Vector2 p1 = poly[i];
                    Vector2 p2 = poly[i + 1];
                    Vector2 v1 = (p1 - p0);
                    Vector2 v2 = (p2 - p1);
                    float l1 = v1.magnitude;
                    float l2 = v2.magnitude;
                    if (l1 < 1e-5f || l2 < 1e-5f)
                        continue;
                    v1 /= l1;
                    v2 /= l2;
                    float dot = Vector2.Dot(v1, v2);
                    if (dot < maxDot)
                    {
                        Vector2 mid = (p0 + p2) * 0.5f;
                        Vector2 q = Vector2.Lerp(p1, mid, 0.55f);
                        q.x = Mathf.Clamp(q.x, minX, maxX);
                        q.y = Mathf.Clamp(q.y, minY, maxY);
                        poly[i] = q;
                    }

                    if (minR > 0.1f)
                    {
                        Vector2 e1 = p1 - p0;
                        Vector2 e2 = p2 - p1;
                        float parallelogram = Mathf.Abs(e1.x * e2.y - e1.y * e2.x);
                        if (parallelogram > 1e-6f)
                        {
                            float l12 = e1.magnitude;
                            float l23 = e2.magnitude;
                            float l13 = Vector2.Distance(p0, p2);
                            float triArea = 0.5f * parallelogram;
                            float R = (l12 * l23 * l13) / (4f * Mathf.Max(triArea, 1e-6f));
                            if (R < minR && R > 1e-4f)
                            {
                                Vector2 mid = (p0 + p2) * 0.5f;
                                Vector2 q = Vector2.Lerp(poly[i], mid, 0.4f);
                                q.x = Mathf.Clamp(q.x, minX, maxX);
                                q.y = Mathf.Clamp(q.y, minY, maxY);
                                poly[i] = q;
                            }
                        }
                    }
                }
            }
        }

        private static List<Vector2> ResamplePolylineUniform2D(List<Vector2> path, float stepCells)
        {
            if (path == null || path.Count == 0)
                return new List<Vector2>();
            if (path.Count == 1)
                return new List<Vector2> { path[0] };

            stepCells = Mathf.Max(0.05f, stepCells);
            float total = 0f;
            for (int i = 1; i < path.Count; i++)
                total += Vector2.Distance(path[i - 1], path[i]);
            if (total < 1e-5f)
                return new List<Vector2> { path[0] };

            var o = new List<Vector2>(Mathf.CeilToInt(total / stepCells) + 2);
            o.Add(path[0]);
            float target = stepCells;
            float acc = 0f;

            for (int seg = 0; seg < path.Count - 1; seg++)
            {
                Vector2 a = path[seg];
                Vector2 b = path[seg + 1];
                float sl = Vector2.Distance(a, b);
                if (sl < 1e-7f)
                    continue;

                while (target <= acc + sl + 1e-5f)
                {
                    float u = (target - acc) / sl;
                    u = Mathf.Clamp01(u);
                    o.Add(Vector2.Lerp(a, b, u));
                    target += stepCells;
                }

                acc += sl;
            }

            Vector2 last = path[path.Count - 1];
            if (Vector2.Distance(o[o.Count - 1], last) > 1e-4f)
                o.Add(last);
            else
                o[o.Count - 1] = last;

            return o;
        }

        private static void DedupeConsecutive2D(List<Vector2> poly, float eps)
        {
            if (poly == null || poly.Count < 2)
                return;
            var o = new List<Vector2>(poly.Count) { poly[0] };
            for (int i = 1; i < poly.Count; i++)
            {
                if (Vector2.Distance(o[o.Count - 1], poly[i]) >= eps)
                    o.Add(poly[i]);
            }

            poly.Clear();
            poly.AddRange(o);
        }

        private static List<Vector2Int> RasterCenterlineToCells(int w, int h, Vector2Int start, List<Vector2> center, MapGenConfig config, IRng rng, out List<Vector2Int> fordCells)
        {
            static bool IsInside(int gw, int gh, int x, int y) => x >= 0 && x < gw && y >= 0 && y < gh;

            var fordList = new List<Vector2Int>();
            var fordPacked = new HashSet<long>();

            float len = 0f;
            for (int i = 1; i < center.Count; i++)
                len += Vector2.Distance(center[i - 1], center[i]);
            float spc = Mathf.Clamp(config.riverCurveSamplesPerCellDist, 1f, 6.5f);
            int n = Mathf.Max(16, Mathf.CeilToInt(len * spc));

            var path = new List<Vector2Int>();
            var seen = new HashSet<long>();
            int fordEvery = Mathf.Max(0, config.riverFordEveryCells);
            if (fordEvery == 1)
                fordEvery = 0;
            int fordPhase = fordEvery > 0 ? rng.NextInt(0, fordEvery) : 0;
            int stepAlongRiver = 0;

            void ConsiderCell(Vector2Int c)
            {
                if (!IsInside(w, h, c.x, c.y))
                    return;
                stepAlongRiver++;
                bool isFord = fordEvery > 0 && stepAlongRiver > 1 && (stepAlongRiver + fordPhase) % fordEvery == 0;
                long k = PackCell(c);
                if (!seen.Add(k))
                    return;
                path.Add(c);
                if (isFord && fordPacked.Add(k))
                    fordList.Add(c);
            }

            ConsiderCell(start);
            Vector2Int prev = start;
            for (int i = 1; i <= n; i++)
            {
                float u = i / (float)n;
                Vector2 p = PointAtNormalizedArcLength(center, u);
                int qx = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, w - 1);
                int qy = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, h - 1);
                var q = new Vector2Int(qx, qy);
                foreach (var stepCell in BresenhamLine(prev, q))
                    ConsiderCell(stepCell);
                prev = q;
            }

            fordCells = fordList;
            return path;
        }

        private static Vector2 PointAtNormalizedArcLength(List<Vector2> poly, float u)
        {
            u = Mathf.Clamp01(u);
            if (poly == null || poly.Count == 0)
                return default;
            if (poly.Count == 1)
                return poly[0];
            float total = 0f;
            for (int i = 1; i < poly.Count; i++)
                total += Vector2.Distance(poly[i - 1], poly[i]);
            if (total < 1e-6f)
                return poly[0];
            float target = u * total;
            float acc = 0f;
            for (int i = 0; i < poly.Count - 1; i++)
            {
                float sl = Vector2.Distance(poly[i], poly[i + 1]);
                if (acc + sl >= target - 1e-5f)
                {
                    float t = sl < 1e-6f ? 0f : (target - acc) / sl;
                    return Vector2.Lerp(poly[i], poly[i + 1], Mathf.Clamp01(t));
                }
                acc += sl;
            }
            return poly[poly.Count - 1];
        }

        private static IEnumerable<Vector2Int> BresenhamLine(Vector2Int from, Vector2Int to)
        {
            int x0 = from.x, y0 = from.y;
            int x1 = to.x, y1 = to.y;
            int dx = Mathf.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                yield return new Vector2Int(x0, y0);
                if (x0 == x1 && y0 == y1) yield break;
                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }
                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }
    }
}
