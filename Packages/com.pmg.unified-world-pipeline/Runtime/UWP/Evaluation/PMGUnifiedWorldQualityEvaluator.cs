using System.Collections.Generic;
using System.Text;
using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline
{
    /// <summary>
    /// Evalúa un GridSystem del Generador Definitivo y produce notas por aspecto (0–10).
    /// Inspirado en WaterAuthoringMapCandidateScorer / flujo index.html.
    /// </summary>
    public static class PMGUnifiedWorldQualityEvaluator
    {
        public static PMGUnifiedWorldQualityReport Evaluate(
            GridSystem grid,
            MapGenConfig config,
            IReadOnlyList<CityNode> cities,
            IReadOnlyList<Road> roads,
            PMGUnifiedWorldPipelineConfig pipelineConfig,
            int seed,
            bool generationSucceeded,
            string failureReason = null)
        {
            var report = new PMGUnifiedWorldQualityReport
            {
                seed = seed,
                generationSucceeded = generationSucceeded,
                failureReason = failureReason ?? string.Empty,
                metrics = generationSucceeded && grid != null
                    ? CollectMetrics(grid, config, cities, roads)
                    : default
            };

            if (!generationSucceeded || grid == null || config == null)
            {
                report.aspects = BuildFailedAspects(pipelineConfig);
                report.totalGrade0To10 = 0f;
                report.totalGradeLetter = "F";
                return report;
            }

            var aspects = new List<PMGUnifiedWorldAspectScore>(9);
            aspects.Add(ScoreRivers(report.metrics, config, pipelineConfig));
            aspects.Add(ScoreRiverEndpoints(report.metrics, config, pipelineConfig));
            aspects.Add(ScoreLakes(report.metrics, config, pipelineConfig));
            aspects.Add(ScoreTerrain(report.metrics, pipelineConfig));
            aspects.Add(ScoreCoastline(report.metrics, pipelineConfig));
            aspects.Add(ScoreNavMesh(report.metrics, pipelineConfig));
            aspects.Add(ScoreCities(report.metrics, config, cities, roads, pipelineConfig));
            aspects.Add(ScoreResources(report.metrics, config, pipelineConfig));
            aspects.Add(ScoreVisualPlaceholder(pipelineConfig));

            float weightSum = 0f;
            float weighted = 0f;
            for (int i = 0; i < aspects.Count; i++)
            {
                weightSum += aspects[i].weight;
                weighted += aspects[i].WeightedPoints;
            }

            report.aspects = aspects.ToArray();
            report.totalWeightedScore = weighted;
            report.totalGrade0To10 = weightSum > 0.001f ? weighted / weightSum : 0f;
            report.totalGradeLetter = PMGUnifiedWorldGradeUtil.ToLetter(report.totalGrade0To10);
            return report;
        }

        static PMGUnifiedWorldMetrics CollectMetrics(
            GridSystem grid,
            MapGenConfig config,
            IReadOnlyList<CityNode> cities,
            IReadOnlyList<Road> roads)
        {
            int w = grid.Width;
            int h = grid.Height;
            int total = w * h;
            float minH = 1f;
            float maxH = 0f;
            int playable = 0;
            int waterCells = 0;
            int lakeCells = 0;
            int resourceCells = 0;
            int blocked = 0;

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    ref CellData cell = ref grid.GetCell(x, z);
                    minH = Mathf.Min(minH, cell.height01);
                    maxH = Mathf.Max(maxH, cell.height01);

                    if (cell.type == CellType.Water || cell.type == CellType.River)
                    {
                        waterCells++;
                        blocked++;
                    }

                    if (cell.type == CellType.Water)
                        lakeCells++;

                    if (cell.type == CellType.Mountain)
                        blocked++;

                    if (cell.slopeDeg > GetMaxPlayableSlopeDeg(config) + 2f)
                        blocked++;

                    if (cell.type == CellType.Land && cell.slopeDeg <= GetMaxPlayableSlopeDeg(config))
                        playable++;

                    if (cell.resourceType != ResourceType.None)
                        resourceCells++;
                }
            }

            CountLakeComponents(grid, out int lakeComponents, out int minLake, out int maxLake, out List<Vector2> centroids);
            float spread = ComputeCentroidSpread01(centroids, w, h);
            float mainSpan = ComputeMainRiverSpan01(grid);
            float straightness = EstimateMaxStraightness(grid);
            DetectMainRiverBorderEndpoints(grid, out bool startBorder, out bool endBorder);

            int tribs = 0;
            if (grid.RiverCenterlinesCellSpace != null)
                tribs = Mathf.Max(0, grid.RiverCenterlinesCellSpace.Count - 1);

            return new PMGUnifiedWorldMetrics
            {
                gridW = w,
                gridH = h,
                lakeComponentCount = lakeComponents,
                minLakeCells = minLake,
                maxLakeCells = maxLake,
                lakeCoverage01 = lakeCells / (float)total,
                lakeSpread01 = spread,
                riverCenterlineCount = grid.RiverCenterlinesCellSpace?.Count ?? 0,
                tributaryCount = tribs,
                mainRiverSpan01 = mainSpan,
                maxRiverStraightness = straightness,
                playableLand01 = playable / (float)total,
                heightRange01 = maxH - minH,
                waterCoverage01 = waterCells / (float)total,
                cityCount = cities?.Count ?? 0,
                roadCount = roads?.Count ?? 0,
                resourceCells = resourceCells,
                estimatedWalkable01 = 1f - blocked / (float)total,
                mainRiverStartAtBorder = startBorder,
                mainRiverEndAtBorder = endBorder,
                riverBorderEndpointWidthMul = config != null ? config.riverSurfaceBorderEndpointWidthMul : 2f,
                riverBorderGhostCells = config != null ? config.riverSurfaceBorderGhostCells : 0f,
                terrainLayersBound = config != null && (config.grassLayer != null || config.dirtLayer != null || config.rockLayer != null)
            };
        }

        static void DetectMainRiverBorderEndpoints(GridSystem grid, out bool startAtBorder, out bool endAtBorder)
        {
            startAtBorder = false;
            endAtBorder = false;
            if (grid?.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return;

            List<Vector2> main = grid.RiverCenterlinesCellSpace[0];
            if (main == null || main.Count < 1) return;

            int w = grid.Width;
            int h = grid.Height;
            startAtBorder = IsBorderCell(main[0], w, h);
            endAtBorder = IsBorderCell(main[main.Count - 1], w, h);
        }

        static bool IsBorderCell(Vector2 p, int w, int h)
        {
            int cx = Mathf.Clamp(Mathf.RoundToInt(p.x), 0, w - 1);
            int cz = Mathf.Clamp(Mathf.RoundToInt(p.y), 0, h - 1);
            return cx == 0 || cz == 0 || cx == w - 1 || cz == h - 1;
        }

        static PMGUnifiedWorldAspectScore ScoreRiverEndpoints(
            PMGUnifiedWorldMetrics m,
            MapGenConfig config,
            PMGUnifiedWorldPipelineConfig p)
        {
            float w = p != null ? p.weightRiverEndpoints : 0.9f;
            var notes = new StringBuilder();
            float score = 10f;

            if (m.riverBorderEndpointWidthMul > 1.05f)
            {
                score -= Mathf.Clamp((m.riverBorderEndpointWidthMul - 1f) * 6f, 0f, 6f);
                notes.Append($"borderMul={m.riverBorderEndpointWidthMul:F2}; ");
            }

            if (m.riverBorderGhostCells > 0.05f)
            {
                score -= Mathf.Clamp(m.riverBorderGhostCells * 2f, 0f, 4f);
                notes.Append($"ghost={m.riverBorderGhostCells:F1}; ");
            }

            if ((m.mainRiverStartAtBorder || m.mainRiverEndAtBorder) && m.riverBorderEndpointWidthMul <= 1.01f)
                notes.Append("borde ancho uniforme; ");

            if (notes.Length == 0)
                notes.Append("extremos OK; ");

            score = Mathf.Clamp(score, 0f, 10f);
            return Aspect(PMGUnifiedWorldQualityAspect.RiverEndpoints, score, w, notes.ToString());
        }

        static PMGUnifiedWorldAspectScore ScoreRivers(
            PMGUnifiedWorldMetrics m,
            MapGenConfig config,
            PMGUnifiedWorldPipelineConfig p)
        {
            float w = p != null ? p.weightRivers : 1.4f;
            var notes = new StringBuilder();
            float score = 0f;

            if (m.riverCenterlineCount == 0)
            {
                notes.Append("sin ríos; ");
                return Aspect(PMGUnifiedWorldQualityAspect.Rivers, 0f, w, notes.ToString());
            }

            float spanScore = Mathf.Clamp01(m.mainRiverSpan01 / (p != null ? p.idealMainRiverSpan01 : 0.62f)) * 4f;
            score += spanScore;
            notes.Append($"main={m.mainRiverSpan01:P0}; ");

            float tribScore = Mathf.Clamp(m.tributaryCount, 0f, 8f) * 0.35f;
            score += tribScore;
            notes.Append($"tribs={m.tributaryCount}; ");

            float straightPenalty = m.maxRiverStraightness > 0.72f ? (m.maxRiverStraightness - 0.72f) * 8f : 0f;
            score += Mathf.Clamp(3.5f - straightPenalty, 0f, 3.5f);
            if (straightPenalty > 0.5f)
                notes.Append("tramos rectos; ");

            score = Mathf.Clamp(score, 0f, 10f);
            return Aspect(PMGUnifiedWorldQualityAspect.Rivers, score, w, notes.ToString());
        }

        static PMGUnifiedWorldAspectScore ScoreLakes(
            PMGUnifiedWorldMetrics m,
            MapGenConfig config,
            PMGUnifiedWorldPipelineConfig p)
        {
            float w = p != null ? p.weightLakes : 1.2f;
            var notes = new StringBuilder();
            float score = 0f;

            int idealMin = p != null ? p.idealLakeCountMin : 3;
            int idealMax = p != null ? p.idealLakeCountMax : 5;
            int minCells = p != null ? p.minLakeCellsIdeal : 80;
            int lakeCountForScore = m.lakeComponentCount;
            if (p != null && p.uwpIndependentMode && config != null && config.lakeCount > 0)
                lakeCountForScore = config.lakeCount;

            if (lakeCountForScore == 0 && m.lakeComponentCount == 0)
            {
                notes.Append("sin lagos; ");
                return Aspect(PMGUnifiedWorldQualityAspect.Lakes, 0f, w, notes.ToString());
            }

            float countMid = (idealMin + idealMax) * 0.5f;
            score += Mathf.Clamp01(1f - Mathf.Abs(lakeCountForScore - countMid) / countMid) * 3.5f;
            notes.Append($"lagos={lakeCountForScore}");
            if (m.lakeComponentCount != lakeCountForScore)
                notes.Append($"(cuerpos={m.lakeComponentCount})");
            notes.Append("; ");

            if (m.minLakeCells >= minCells)
                score += 2.5f;
            else
            {
                score += m.minLakeCells / (float)minCells * 1.5f;
                notes.Append("lagos pequeños; ");
            }

            float covIdeal = p != null ? p.idealLakeCoverage01 : 0.045f;
            score += Mathf.Clamp01(1f - Mathf.Abs(m.lakeCoverage01 - covIdeal) / 0.06f) * 2f;

            float spreadIdeal = p != null ? p.idealLakeSpread01 : 0.42f;
            score += Mathf.Clamp01(m.lakeSpread01 / spreadIdeal) * 2f;
            if (m.lakeSpread01 < spreadIdeal * 0.65f)
                notes.Append("amontonados; ");

            score = Mathf.Clamp(score, 0f, 10f);
            return Aspect(PMGUnifiedWorldQualityAspect.Lakes, score, w, notes.ToString());
        }

        static PMGUnifiedWorldAspectScore ScoreTerrain(PMGUnifiedWorldMetrics m, PMGUnifiedWorldPipelineConfig p)
        {
            float w = p != null ? p.weightTerrain : 1f;
            float score = 0f;
            var notes = new StringBuilder();

            score += Mathf.Clamp01(m.playableLand01 / 0.62f) * 5f;
            notes.Append($"jugable={m.playableLand01:P0}; ");

            float rangeIdeal = 0.22f;
            score += Mathf.Clamp01(1f - Mathf.Abs(m.heightRange01 - rangeIdeal) / 0.18f) * 5f;
            notes.Append($"relieve={m.heightRange01:F2}; ");

            score = Mathf.Clamp(score, 0f, 10f);
            return Aspect(PMGUnifiedWorldQualityAspect.TerrainRelief, score, w, notes.ToString());
        }

        static PMGUnifiedWorldAspectScore ScoreCoastline(PMGUnifiedWorldMetrics m, PMGUnifiedWorldPipelineConfig p)
        {
            float w = p != null ? p.weightCoastline : 0.8f;
            float idealWater = 0.12f;
            float score = Mathf.Clamp01(1f - Mathf.Abs(m.waterCoverage01 - idealWater) / 0.14f) * 10f;
            string notes = $"agua={m.waterCoverage01:P1}";
            return Aspect(PMGUnifiedWorldQualityAspect.Coastline, score, w, notes);
        }

        static PMGUnifiedWorldAspectScore ScoreNavMesh(PMGUnifiedWorldMetrics m, PMGUnifiedWorldPipelineConfig p)
        {
            float w = p != null ? p.weightNavMesh : 1.2f;
            float score = Mathf.Clamp01(m.estimatedWalkable01 / 0.78f) * 10f;
            if (m.waterCoverage01 > 0.18f)
                score *= 0.75f;
            string notes = $"walkable≈{m.estimatedWalkable01:P0}";
            return Aspect(PMGUnifiedWorldQualityAspect.NavMeshWalkable, score, w, notes);
        }

        static PMGUnifiedWorldAspectScore ScoreCities(
            PMGUnifiedWorldMetrics m,
            MapGenConfig config,
            IReadOnlyList<CityNode> cities,
            IReadOnlyList<Road> roads,
            PMGUnifiedWorldPipelineConfig p)
        {
            float w = p != null ? p.weightCities : 1f;
            int expected = config != null ? Mathf.Max(1, config.cityCount) : 2;
            float score = 0f;
            var notes = new StringBuilder();

            if (m.cityCount == 0)
            {
                notes.Append("sin ciudades; ");
                return Aspect(PMGUnifiedWorldQualityAspect.CityFairness, 0f, w, notes.ToString());
            }

            score += Mathf.Clamp01(m.cityCount / (float)expected) * 5f;
            notes.Append($"ciudades={m.cityCount}; ");

            if (roads != null && roads.Count > 0 && m.cityCount > 1)
                score += 5f;
            else if (m.cityCount == 1)
                score += 4f;
            else
                notes.Append("caminos débiles; ");

            score = Mathf.Clamp(score, 0f, 10f);
            return Aspect(PMGUnifiedWorldQualityAspect.CityFairness, score, w, notes.ToString());
        }

        static PMGUnifiedWorldAspectScore ScoreResources(
            PMGUnifiedWorldMetrics m,
            MapGenConfig config,
            PMGUnifiedWorldPipelineConfig p)
        {
            float w = p != null ? p.weightResources : 0.7f;
            float ideal = m.gridW * m.gridH * 0.008f;
            float score = ideal > 1f
                ? Mathf.Clamp01(m.resourceCells / ideal) * 10f
                : 5f;
            string notes = $"recursos={m.resourceCells}";
            return Aspect(PMGUnifiedWorldQualityAspect.ResourcePlacement, score, w, notes);
        }

        static PMGUnifiedWorldAspectScore ScoreVisualPlaceholder(PMGUnifiedWorldPipelineConfig p)
        {
            float w = p != null ? p.weightVisualWater : 0.7f;
            return Aspect(
                PMGUnifiedWorldQualityAspect.VisualWater,
                5f,
                w,
                "Pendiente revisión manual en escena (Apply Full).");
        }

        static PMGUnifiedWorldAspectScore Aspect(
            PMGUnifiedWorldQualityAspect aspect,
            float score,
            float weight,
            string details)
        {
            score = Mathf.Clamp(score, 0f, 10f);
            return new PMGUnifiedWorldAspectScore
            {
                aspect = aspect,
                score0To10 = score,
                weight = weight,
                gradeLetter = PMGUnifiedWorldGradeUtil.ToLetter(score),
                summary = $"{aspect} {score:F1} ({PMGUnifiedWorldGradeUtil.ToLetter(score)})",
                details = details
            };
        }

        static float GetMaxPlayableSlopeDeg(MapGenConfig config)
        {
            if (config == null) return 22f;
            return Mathf.Max(18f, config.maxCitySlopeDeg + 6f);
        }

        static PMGUnifiedWorldAspectScore[] BuildFailedAspects(PMGUnifiedWorldPipelineConfig p)
        {
            return new[]
            {
                Aspect(PMGUnifiedWorldQualityAspect.Rivers, 0f, p?.weightRivers ?? 1f, "generación fallida"),
                Aspect(PMGUnifiedWorldQualityAspect.RiverEndpoints, 0f, p?.weightRiverEndpoints ?? 0.9f, "generación fallida"),
                Aspect(PMGUnifiedWorldQualityAspect.Lakes, 0f, p?.weightLakes ?? 1f, "generación fallida"),
                Aspect(PMGUnifiedWorldQualityAspect.TerrainRelief, 0f, p?.weightTerrain ?? 1f, "generación fallida"),
                Aspect(PMGUnifiedWorldQualityAspect.Coastline, 0f, p?.weightCoastline ?? 1f, "generación fallida"),
                Aspect(PMGUnifiedWorldQualityAspect.NavMeshWalkable, 0f, p?.weightNavMesh ?? 1f, "generación fallida"),
                Aspect(PMGUnifiedWorldQualityAspect.CityFairness, 0f, p?.weightCities ?? 1f, "generación fallida"),
                Aspect(PMGUnifiedWorldQualityAspect.ResourcePlacement, 0f, p?.weightResources ?? 1f, "generación fallida"),
                Aspect(PMGUnifiedWorldQualityAspect.VisualWater, 0f, p?.weightVisualWater ?? 1f, "generación fallida")
            };
        }

        static void CountLakeComponents(
            GridSystem grid,
            out int components,
            out int minCells,
            out int maxCells,
            out List<Vector2> centroids)
        {
            int w = grid.Width;
            int h = grid.Height;
            var seen = new bool[w, h];
            components = 0;
            minCells = int.MaxValue;
            maxCells = 0;
            centroids = new List<Vector2>();

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (seen[x, z] || grid.GetCell(x, z).type != CellType.Water)
                        continue;

                    int size = FloodLake(grid, seen, x, z, out Vector2 centroid);
                    if (size < 4) continue;
                    components++;
                    minCells = Mathf.Min(minCells, size);
                    maxCells = Mathf.Max(maxCells, size);
                    centroids.Add(centroid);
                }
            }

            if (components == 0)
                minCells = 0;
        }

        static int FloodLake(GridSystem grid, bool[,] seen, int sx, int sz, out Vector2 centroid)
        {
            int w = grid.Width;
            int h = grid.Height;
            var stack = new Stack<Vector2Int>();
            stack.Push(new Vector2Int(sx, sz));
            seen[sx, sz] = true;
            int count = 0;
            float cx = 0f;
            float cz = 0f;

            while (stack.Count > 0)
            {
                Vector2Int p = stack.Pop();
                count++;
                cx += p.x;
                cz += p.y;

                TryPush(p.x + 1, p.y);
                TryPush(p.x - 1, p.y);
                TryPush(p.x, p.y + 1);
                TryPush(p.x, p.y - 1);
            }

            centroid = count > 0 ? new Vector2(cx / count, cz / count) : Vector2.zero;
            return count;

            void TryPush(int x, int z)
            {
                if (x < 0 || z < 0 || x >= w || z >= h || seen[x, z]) return;
                if (grid.GetCell(x, z).type != CellType.Water) return;
                seen[x, z] = true;
                stack.Push(new Vector2Int(x, z));
            }
        }

        static float ComputeCentroidSpread01(IReadOnlyList<Vector2> centroids, int w, int h)
        {
            if (centroids == null || centroids.Count < 2) return centroids != null && centroids.Count == 1 ? 0.35f : 0f;
            float maxD = 0f;
            for (int i = 0; i < centroids.Count; i++)
            {
                for (int j = i + 1; j < centroids.Count; j++)
                {
                    float d = Vector2.Distance(centroids[i], centroids[j]);
                    if (d > maxD) maxD = d;
                }
            }

            float diag = Mathf.Sqrt(w * w + h * h);
            return diag > 0.001f ? maxD / diag : 0f;
        }

        static float ComputeMainRiverSpan01(GridSystem grid)
        {
            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return 0f;

            List<Vector2> main = grid.RiverCenterlinesCellSpace[0];
            if (main == null || main.Count < 2) return 0f;

            float maxD = 0f;
            for (int i = 0; i < main.Count; i++)
            {
                for (int j = i + 1; j < main.Count; j++)
                    maxD = Mathf.Max(maxD, Vector2.Distance(main[i], main[j]));
            }

            float diag = Mathf.Sqrt(grid.Width * grid.Width + grid.Height * grid.Height);
            return diag > 0.001f ? maxD / diag : 0f;
        }

        static float EstimateMaxStraightness(GridSystem grid)
        {
            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return 0f;

            float worst = 0f;
            for (int r = 0; r < grid.RiverCenterlinesCellSpace.Count; r++)
            {
                List<Vector2> path = grid.RiverCenterlinesCellSpace[r];
                if (path == null || path.Count < 3) continue;

                float pathLen = 0f;
                for (int i = 1; i < path.Count; i++)
                    pathLen += Vector2.Distance(path[i - 1], path[i]);

                float chord = Vector2.Distance(path[0], path[path.Count - 1]);
                if (pathLen < 0.001f) continue;
                worst = Mathf.Max(worst, chord / pathLen);
            }

            return worst;
        }

        public static List<int> BuildVariedSeedList(int count, int baseSeed = 2026)
        {
            var list = new List<int>(count);
            var rng = new System.Random(baseSeed);
            var used = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                int s;
                do
                {
                    s = rng.Next(100000, 9999999);
                } while (!used.Add(s));

                list.Add(s);
            }

            return list;
        }
    }
}
