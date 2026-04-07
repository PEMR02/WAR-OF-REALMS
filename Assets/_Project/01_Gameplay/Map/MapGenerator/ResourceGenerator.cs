using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Map.Generation.Alpha;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 8: coloca recursos por rings; alpha opcional con sesgo por <see cref="SemanticRegionMap"/>.</summary>
    public static class ResourceGenerator
    {
        public static void PlaceResources(GridSystem grid, List<CityNode> cities, MapGenConfig config, IRng rng)
        {
            if (grid == null || config == null || rng == null) return;

            int wood = 0, stone = 0, gold = 0, food = 0;
            foreach (var city in cities)
            {
                PlaceInRing(grid, city.Center, config.ringNear, config.minWoodPerCity, ResourceType.Wood, rng, ref wood, config);
                PlaceInRing(grid, city.Center, config.ringMid, config.minStonePerCity, ResourceType.Stone, rng, ref stone, config);
                PlaceInRing(grid, city.Center, config.ringMid, config.minGoldPerCity, ResourceType.Gold, rng, ref gold, config);
                PlaceInRing(grid, city.Center, config.ringNear, config.minFoodPerCity, ResourceType.Food, rng, ref food, config);
            }

            if (config.debugLogs)
                Debug.Log($"Fase8 Recursos: Wood={wood}, Stone={stone}, Gold={gold}, Food={food}. (sesgo terreno={(config.alphaUseTerrainResourceBias ? "sí" : "no")})");
        }

        static void PlaceInRing(GridSystem grid, Vector2Int center, Vector2Int ring, int count, ResourceType type, IRng rng, ref int placed, MapGenConfig config)
        {
            int minR = Mathf.Min(ring.x, ring.y);
            int maxR = Mathf.Max(ring.x, ring.y);
            if (count <= 0) return;

            int target = placed + count;
            while (placed < target)
            {
                if (!config.alphaUseTerrainResourceBias || grid.SemanticRegions == null)
                {
                    bool ok = false;
                    for (int i = 0; i < 48 && !ok; i++)
                    {
                        if (TryRandomCellInRing(grid, center, minR, maxR, rng, out int cx, out int cz))
                            ok = TryOccupyOne(grid, cx, cz, type, ref placed);
                    }
                    if (!ok) break;
                    continue;
                }

                float bestScore = float.NegativeInfinity;
                int bestX = -1, bestZ = -1;
                int trials = Mathf.Max(48, (target - placed) * 28);
                for (int i = 0; i < trials; i++)
                {
                    if (!TryRandomCellInRing(grid, center, minR, maxR, rng, out int cx, out int cz)) continue;
                    ref var cell = ref grid.GetCell(cx, cz);
                    if (cell.type != CellType.Land || cell.resourceType != ResourceType.None || !cell.walkable) continue;
                    float sc = ScoreCell(grid, cx, cz, type, config);
                    if (sc > bestScore)
                    {
                        bestScore = sc;
                        bestX = cx;
                        bestZ = cz;
                    }
                }

                if (bestX >= 0 && TryOccupyOne(grid, bestX, bestZ, type, ref placed))
                    continue;

                bool fb = false;
                for (int i = 0; i < 32 && !fb; i++)
                {
                    if (TryRandomCellInRing(grid, center, minR, maxR, rng, out int cx, out int cz))
                        fb = TryOccupyOne(grid, cx, cz, type, ref placed);
                }
                if (!fb) break;
            }
        }

        static bool TryRandomCellInRing(GridSystem grid, Vector2Int center, int minR, int maxR, IRng rng, out int cx, out int cz)
        {
            cx = cz = 0;
            int r = rng.NextInt(minR, maxR + 1);
            int angle = rng.NextInt(0, 360);
            float rad = angle * Mathf.Deg2Rad;
            cx = center.x + Mathf.RoundToInt(r * Mathf.Cos(rad));
            cz = center.y + Mathf.RoundToInt(r * Mathf.Sin(rad));
            if (!grid.InBoundsCell(cx, cz)) return false;
            ref var cell = ref grid.GetCell(cx, cz);
            return cell.type == CellType.Land && cell.resourceType == ResourceType.None && cell.walkable;
        }

        static bool TryOccupyOne(GridSystem grid, int cx, int cz, ResourceType type, ref int placed)
        {
            ref var cell = ref grid.GetCell(cx, cz);
            if (cell.type != CellType.Land || cell.resourceType != ResourceType.None || !cell.walkable) return false;
            cell.resourceType = type;
            placed++;
            return true;
        }

        static float ScoreCell(GridSystem grid, int cx, int cz, ResourceType type, MapGenConfig config)
        {
            ref var cell = ref grid.GetCell(cx, cz);
            int dw = WaterDistanceField.Get(grid, cx, cz);
            float invW = dw >= WaterDistanceField.UnreachableDistance - 100 ? 0f : 1f / (1f + dw);
            var sem = grid.SemanticRegions;
            TerrainRegionType r = sem != null ? sem.Get(cx, cz) : TerrainRegionType.Unknown;
            float score = 0f;

            switch (type)
            {
                case ResourceType.Wood:
                    score += config.alphaWoodNearWaterWeight * invW * 3f;
                    score -= cell.height01 * 0.8f;
                    if (r == TerrainRegionType.RiverBank || r == TerrainRegionType.LakeShore || r == TerrainRegionType.WetZone || r == TerrainRegionType.ForestCandidate)
                        score += 2.5f;
                    if (r == TerrainRegionType.Mountain || r == TerrainRegionType.RockyZone)
                        score -= 2f;
                    break;
                case ResourceType.Stone:
                    score += cell.height01 * config.alphaStoneMountainWeight * 2f;
                    score += (cell.slopeDeg / 90f) * config.alphaStoneMountainWeight;
                    if (r == TerrainRegionType.Mountain || r == TerrainRegionType.RockyZone)
                        score += 3f;
                    if (r == TerrainRegionType.RiverBank || r == TerrainRegionType.WetZone)
                        score -= 1f;
                    break;
                case ResourceType.Gold:
                    score += cell.height01 * config.alphaGoldMountainWeight * 2.2f;
                    if (r == TerrainRegionType.Mountain || r == TerrainRegionType.Hill)
                        score += 2f;
                    if (r == TerrainRegionType.Plain || r == TerrainRegionType.SpawnFriendly)
                        score += 0.4f;
                    break;
                case ResourceType.Food:
                    score += config.alphaFoodNearWaterWeight * invW * 3f;
                    if (r == TerrainRegionType.RiverBank || r == TerrainRegionType.LakeShore || r == TerrainRegionType.WetZone)
                        score += 2f;
                    break;
            }

            return score;
        }
    }
}
