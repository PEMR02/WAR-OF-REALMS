using System.Collections.Generic;
using System.Text;
using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace Project.Gameplay.Map.Generation
{
    /// <summary>
    /// MapQualityContract v1 — worldMeters: hidrología colocada + coherencia orilla/carve en metros world.
    /// </summary>
    public static class RtsMapQualityEvaluator
    {
        public const string ContractVersion = "v1-worldMeters";

        public const float BankLipHardFailM = 0.12f;
        public const float BankStepHardFailM = 0.10f;
        public const float BankLipGoodM = 0.05f;
        public const float BankLipAcceptableM = 0.08f;
        public const float CrossDepthMinGoodM = 0.06f;
        public const float CrossDepthMinAcceptableM = 0.04f;

        public struct RtsMapQualityReport
        {
            public int seed;
            public string contractVersion;
            public bool hardPass;
            public bool hydrologyHardPass;
            public bool carveHardPass;
            public float hydrologyScore;
            public float carveScore;
            public float totalScore;
            public int targetRivers;
            public int placedRivers;
            public int tributaryCount;
            public int confluenceCount;
            public float bankLipP50M;
            public float bankLipP90M;
            public float bankStepP95M;
            public float crossDepthMedianM;
            public float crossDepthP10M;
            public float maskLogicalIou;
            public int crossSectionsSampled;
            public int crossSectionsValidPct;
            public float waterVisualWorldM;
            public float carveDepthCfgM;
            public float mainWidthCfgCells;
            public string notes;

            public override string ToString()
            {
                return $"seed={seed} total={totalScore:F1} hydro={hydrologyScore:F1} carve={carveScore:F1} " +
                       $"rivers={placedRivers}/{targetRivers} tribs={tributaryCount} " +
                       $"lipP90={bankLipP90M * 100f:F1}cm stepP95={bankStepP95M * 100f:F1}cm " +
                       $"crossMed={crossDepthMedianM * 100f:F1}cm hard={(hardPass ? 1 : 0)}";
            }
        }

        public struct BatchSummary
        {
            public RtsMapQualityReport[] all;
            public RtsMapQualityReport[] top;
            public int evaluatedCount;
            public int hardPassCount;
        }

        public static BatchSummary EvaluateBatch(IReadOnlyList<RtsMapQualityReport> reports, int topN = 10)
        {
            var all = new List<RtsMapQualityReport>(reports);
            all.Sort((a, b) => b.totalScore.CompareTo(a.totalScore));
            int take = Mathf.Clamp(topN, 1, all.Count);
            var top = new RtsMapQualityReport[take];
            for (int i = 0; i < take; i++)
                top[i] = all[i];

            int pass = 0;
            for (int i = 0; i < all.Count; i++)
                if (all[i].hardPass)
                    pass++;

            return new BatchSummary
            {
                all = all.ToArray(),
                top = top,
                evaluatedCount = all.Count,
                hardPassCount = pass
            };
        }

        /// <summary>Prepara máscara visual si hace falta y evalúa el grid.</summary>
        public static RtsMapQualityReport Evaluate(GridSystem grid, MapGenConfig config, int seed = 0)
        {
            var report = new RtsMapQualityReport
            {
                seed = seed != 0 ? seed : (config != null ? config.seed : 0),
                contractVersion = ContractVersion
            };

            if (grid == null || config == null)
            {
                report.notes = "grid o config null";
                return report;
            }

            report.targetRivers = Mathf.Max(0, config.riverCount);
            report.placedRivers = grid.RiverCenterlinesCellSpace?.Count ?? 0;
            report.tributaryCount = Mathf.Max(0, report.placedRivers - 1);
            report.confluenceCount = grid.RiverConfluences?.Count ?? 0;
            report.carveDepthCfgM = config.riverTerrainCarveDepthWorld;
            report.mainWidthCfgCells = config.riverVisualRibbonFullWidthCellsMain;

            float terrainWorld = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            float waterVisual01 = TerrainExporter.ComputeWaterVisualHeight01(config);
            report.waterVisualWorldM = waterVisual01 * terrainWorld;

            if (config.riverVisualUseRiverSurfaceMeshStrip &&
                grid.RiverCenterlinesCellSpace != null &&
                grid.RiverCenterlinesCellSpace.Count > 0)
            {
                RiverSurfaceMeshBuilder.BuildRiverVisualSurfaceMask(grid, config, config.cellSizeWorld);
            }

            var notes = new StringBuilder();
            report.hydrologyScore = ScoreHydrology(report, notes, out report.hydrologyHardPass);
            report.carveScore = 0f;

            if (grid.RiverVisualSurfaceMask == null ||
                grid.RiverVisualSurfaceMask.GetLength(0) != grid.Width ||
                grid.RiverVisualSurfaceMask.GetLength(1) != grid.Height)
            {
                notes.Append("sin máscara visual; ");
                report.carveHardPass = false;
            }
            else
            {
                float[,] postH = TerrainExporter.BuildEvaluationCellHeights(grid, config);
                float[,] preH = TerrainExporter.BuildLogicalEvaluationCellHeights(grid);
                report.maskLogicalIou = ComputeMaskLogicalIou(grid);
                MeasureCarveCoherence(
                    grid,
                    config,
                    postH,
                    preH,
                    report.waterVisualWorldM,
                    terrainWorld,
                    notes,
                    out report.bankLipP50M,
                    out report.bankLipP90M,
                    out report.bankStepP95M,
                    out report.crossDepthMedianM,
                    out report.crossDepthP10M,
                    out report.crossSectionsSampled,
                    out report.crossSectionsValidPct,
                    out report.carveScore,
                    out report.carveHardPass);
            }

            report.hardPass = report.hydrologyHardPass && report.carveHardPass;
            report.totalScore = Mathf.Clamp(
                report.hydrologyScore / 25f * 35f + report.carveScore / 30f * 65f,
                0f,
                100f);
            report.notes = notes.ToString().Trim();
            return report;
        }

        static float ScoreHydrology(RtsMapQualityReport r, StringBuilder notes, out bool hardPass)
        {
            hardPass = true;
            float score = 0f;

            if (r.targetRivers <= 0)
            {
                notes.Append("sin ríos objetivo; ");
                hardPass = r.placedRivers == 0;
                return 0f;
            }

            if (r.placedRivers < r.targetRivers)
            {
                hardPass = false;
                notes.Append($"ríos={r.placedRivers}/{r.targetRivers}; ");
                score += r.placedRivers / (float)r.targetRivers * 10f;
            }
            else
            {
                score += 10f;
                notes.Append($"ríos={r.placedRivers}; ");
            }

            int expectedTribs = Mathf.Max(0, r.targetRivers - 1);
            if (expectedTribs > 0)
            {
                if (r.tributaryCount < expectedTribs)
                {
                    hardPass = false;
                    notes.Append($"tribs={r.tributaryCount}/{expectedTribs}; ");
                    score += r.tributaryCount / (float)expectedTribs * 8f;
                }
                else
                {
                    score += 8f;
                    notes.Append($"tribs={r.tributaryCount}; ");
                }
            }
            else
                score += 8f;

            if (r.tributaryCount > 0)
            {
                float confRatio = r.confluenceCount / (float)r.tributaryCount;
                if (confRatio < 0.85f)
                {
                    hardPass = false;
                    notes.Append($"confluencias={r.confluenceCount}; ");
                    score += confRatio * 7f;
                }
                else
                {
                    score += 7f;
                }
            }
            else if (expectedTribs > 0)
            {
                hardPass = false;
                notes.Append("sin confluencias; ");
            }
            else
                score += 7f;

            return Mathf.Clamp(score, 0f, 25f);
        }

        static float ComputeMaskLogicalIou(GridSystem grid)
        {
            bool[,] mask = grid.RiverVisualSurfaceMask;
            if (mask == null)
                return 0f;

            int w = grid.Width;
            int h = grid.Height;
            int inter = 0;
            int union = 0;
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    bool logical = grid.GetCell(x, z).type == CellType.River;
                    bool visual = mask[x, z];
                    if (logical || visual)
                        union++;
                    if (logical && visual)
                        inter++;
                }
            }

            return union > 0 ? inter / (float)union : 0f;
        }

        static void MeasureCarveCoherence(
            GridSystem grid,
            MapGenConfig config,
            float[,] postH,
            float[,] preH,
            float waterVisualWorldM,
            float terrainWorldM,
            StringBuilder notes,
            out float lipP50,
            out float lipP90,
            out float stepP95,
            out float crossMedian,
            out float crossP10,
            out int sectionsSampled,
            out int sectionsValidPct,
            out float carveScore,
            out bool carveHardPass)
        {
            lipP50 = lipP90 = stepP95 = crossMedian = crossP10 = 0f;
            sectionsSampled = sectionsValidPct = 0;
            carveScore = 0f;
            carveHardPass = false;

            bool[,] mask = grid.RiverVisualSurfaceMask;
            int w = grid.Width;
            int h = grid.Height;
            var lips = new List<float>(512);
            var steps = new List<float>(256);
            var crossDepths = new List<float>(64);

            CollectBankLipsAndSteps(grid, mask, postH, terrainWorldM, waterVisualWorldM, lips, steps);

            if (lips.Count > 0)
            {
                lips.Sort();
                lipP50 = PercentileSorted(lips, 0.50f);
                lipP90 = PercentileSorted(lips, 0.90f);
            }

            if (steps.Count > 0)
            {
                steps.Sort();
                stepP95 = PercentileSorted(steps, 0.95f);
            }

            SampleCrossSections(grid, mask, postH, terrainWorldM, crossDepths, out sectionsSampled, out sectionsValidPct);

            if (crossDepths.Count > 0)
            {
                crossDepths.Sort();
                crossMedian = PercentileSorted(crossDepths, 0.50f);
                crossP10 = PercentileSorted(crossDepths, 0.10f);
            }

            carveHardPass = lips.Count >= 8 &&
                            lipP90 <= BankLipHardFailM &&
                            stepP95 <= BankStepHardFailM &&
                            sectionsSampled > 0 &&
                            sectionsValidPct >= 70;

            if (lipP90 > BankLipHardFailM)
                notes.Append($"lipP90={lipP90 * 100f:F0}cm; ");
            if (stepP95 > BankStepHardFailM)
                notes.Append($"stepP95={stepP95 * 100f:F0}cm; ");
            if (crossMedian < CrossDepthMinAcceptableM)
                notes.Append($"valle_plano={crossMedian * 100f:F0}cm; ");
            if (ComputeMaskLogicalIou(grid) < 0.45f)
                notes.Append("mask≠logical; ");

            float lipTerm = Mathf.Clamp01(1f - Mathf.Max(0f, lipP90 - BankLipGoodM) / (BankLipHardFailM - BankLipGoodM));
            float stepTerm = Mathf.Clamp01(1f - Mathf.Max(0f, stepP95 - 0.04f) / (BankStepHardFailM - 0.04f));
            float crossTerm = crossMedian >= CrossDepthMinGoodM
                ? 1f
                : crossMedian >= CrossDepthMinAcceptableM
                    ? Mathf.InverseLerp(CrossDepthMinAcceptableM, CrossDepthMinGoodM, crossMedian)
                    : crossMedian / CrossDepthMinAcceptableM * 0.45f;
            float iou = ComputeMaskLogicalIou(grid);
            float iouTerm = Mathf.Clamp01((iou - 0.35f) / 0.45f);

            carveScore = Mathf.Clamp(
                lipTerm * 12f + stepTerm * 8f + crossTerm * 6f + iouTerm * 4f,
                0f,
                30f);

            if (carveHardPass && notes.Length == 0)
                notes.Append("orilla OK; ");
        }

        static void CollectBankLipsAndSteps(
            GridSystem grid,
            bool[,] mask,
            float[,] postH,
            float terrainWorldM,
            float waterVisualWorldM,
            List<float> lips,
            List<float> steps)
        {
            int w = grid.Width;
            int h = grid.Height;
            var isBank = new bool[w, h];

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (mask[x, z])
                        continue;
                    if (grid.GetCell(x, z).type != CellType.Land)
                        continue;
                    if (!TouchesMask(x, z, mask, w, h))
                        continue;

                    isBank[x, z] = true;
                    float lip = postH[x, z] * terrainWorldM - waterVisualWorldM;
                    lips.Add(Mathf.Max(0f, lip));
                }
            }

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (!isBank[x, z])
                        continue;
                    float hHere = postH[x, z] * terrainWorldM;
                    TryStep(x - 1, z);
                    TryStep(x + 1, z);
                    TryStep(x, z - 1);
                    TryStep(x, z + 1);

                    void TryStep(int nx, int nz)
                    {
                        if ((uint)nx >= (uint)w || (uint)nz >= (uint)h || !isBank[nx, nz])
                            return;
                        steps.Add(Mathf.Abs(hHere - postH[nx, nz] * terrainWorldM));
                    }
                }
            }
        }

        static bool TouchesMask(int x, int z, bool[,] mask, int w, int h)
        {
            if (x > 0 && mask[x - 1, z]) return true;
            if (x + 1 < w && mask[x + 1, z]) return true;
            if (z > 0 && mask[x, z - 1]) return true;
            if (z + 1 < h && mask[x, z + 1]) return true;
            return false;
        }

        static void SampleCrossSections(
            GridSystem grid,
            bool[,] mask,
            float[,] postH,
            float terrainWorldM,
            List<float> crossDepths,
            out int sampled,
            out int validPct)
        {
            sampled = 0;
            validPct = 0;
            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return;

            List<Vector2> main = grid.RiverCenterlinesCellSpace[0];
            if (main == null || main.Count < 4)
                return;

            int step = Mathf.Max(4, main.Count / 12);
            int valid = 0;
            for (int i = step; i < main.Count - step; i += step)
            {
                sampled++;
                if (TryCrossDepthAt(main, i, grid, mask, postH, terrainWorldM, out float depth) && depth > 0.005f)
                {
                    crossDepths.Add(depth);
                    if (depth >= CrossDepthMinAcceptableM)
                        valid++;
                }
            }

            validPct = sampled > 0 ? Mathf.RoundToInt(valid * 100f / sampled) : 0;
        }

        static bool TryCrossDepthAt(
            List<Vector2> path,
            int index,
            GridSystem grid,
            bool[,] mask,
            float[,] postH,
            float terrainWorldM,
            out float depthM)
        {
            depthM = 0f;
            int w = grid.Width;
            int h = grid.Height;
            Vector2 prev = path[Mathf.Max(0, index - 1)];
            Vector2 next = path[Mathf.Min(path.Count - 1, index + 1)];
            Vector2 tan = next - prev;
            if (tan.sqrMagnitude < 1e-6f)
                return false;

            tan.Normalize();
            Vector2 perp = new Vector2(-tan.y, tan.x);
            int cx = Mathf.Clamp(Mathf.RoundToInt(path[index].x), 0, w - 1);
            int cz = Mathf.Clamp(Mathf.RoundToInt(path[index].y), 0, h - 1);

            float bankMax = float.MinValue;
            float centerMin = float.MaxValue;
            const int radius = 8;
            for (int d = -radius; d <= radius; d++)
            {
                int sx = Mathf.Clamp(cx + Mathf.RoundToInt(perp.x * d), 0, w - 1);
                int sz = Mathf.Clamp(cz + Mathf.RoundToInt(perp.y * d), 0, h - 1);
                float hw = postH[sx, sz] * terrainWorldM;
                if (mask[sx, sz])
                    centerMin = Mathf.Min(centerMin, hw);
                else if (TouchesMask(sx, sz, mask, w, h))
                    bankMax = Mathf.Max(bankMax, hw);
            }

            if (bankMax <= float.MinValue + 1f || centerMin >= float.MaxValue - 1f)
                return false;

            depthM = bankMax - centerMin;
            return true;
        }

        static float PercentileSorted(List<float> sorted, float p)
        {
            if (sorted == null || sorted.Count == 0)
                return 0f;
            if (sorted.Count == 1)
                return sorted[0];
            float t = Mathf.Clamp01(p) * (sorted.Count - 1);
            int i0 = Mathf.FloorToInt(t);
            int i1 = Mathf.Min(i0 + 1, sorted.Count - 1);
            float f = t - i0;
            return Mathf.Lerp(sorted[i0], sorted[i1], f);
        }

        public static List<int> BuildVariedSeedList(int count = 100, int baseSeed = 12345, int batchOffset = 0)
        {
            var seeds = new List<int>(count);
            var seen = new HashSet<int>();
            int[] multipliers = { 1, 3, 7, 11, 17, 23, 29, 37, 43, 53 };

            for (int i = 0; i < count; i++)
            {
                int m = multipliers[(i + batchOffset) % multipliers.Length];
                int s = unchecked(baseSeed + batchOffset * 999983 + i * 7919 * m + (i * i * 131));
                if (i % 7 == 0)
                    s ^= 0x5A3E91;
                if (i % 11 == 0)
                    s += 424242;
                while (!seen.Add(s))
                    s++;
                seeds.Add(s);
            }

            return seeds;
        }
    }
}
