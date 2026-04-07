using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 5: coloca CityNodes con validación; alpha opcional con puntuación de llanura y distancia al agua.</summary>
    public static class CityGenerator
    {
        public static List<CityNode> GenerateCities(GridSystem grid, MapGenConfig config, IRng rng)
        {
            var cities = new List<CityNode>();
            if (grid == null || config == null || rng == null) return cities;

            int w = grid.Width;
            int h = grid.Height;
            int count = Mathf.Clamp(config.cityCount, 1, 16);
            int minDist = config.minCityDistanceCells;
            int radius = config.cityRadiusCells;
            float maxSlope = config.maxCitySlopeDeg;
            int waterBuffer = Mathf.Max(0, config.cityWaterBufferCells);
            bool useScore = config.alphaPreferPlainsForCities || config.alphaMinChebyshevFromWaterForSpawn > 0;

            for (int i = 0; i < count; i++)
            {
                int bestX = -1, bestZ = -1;
                float bestScore = float.NegativeInfinity;
                int attempts = useScore ? 120 : 50;

                while (attempts-- > 0)
                {
                    int margin = radius + waterBuffer + 2;
                    int cx = rng.NextInt(margin, w - margin);
                    int cz = rng.NextInt(margin, h - margin);
                    if (!IsValidCityCenter(grid, cx, cz, radius, waterBuffer, maxSlope, config)) continue;
                    if (!FarEnoughFromCities(cities, cx, cz, minDist)) continue;

                    float sc = useScore ? ScoreCitySite(grid, cx, cz, radius, config) : 0f;
                    if (!useScore)
                    {
                        bestX = cx;
                        bestZ = cz;
                        break;
                    }
                    if (sc > bestScore)
                    {
                        bestScore = sc;
                        bestX = cx;
                        bestZ = cz;
                    }
                }

                if (bestX < 0) continue;

                var node = new CityNode { Id = i, Center = new Vector2Int(bestX, bestZ), RadiusCells = radius };
                cities.Add(node);
                MarkCityArea(grid, node);
            }

            if (config.debugLogs)
                Debug.Log($"Fase5 Ciudades: {cities.Count} colocadas.");
            return cities;
        }

        static float ScoreCitySite(GridSystem grid, int cx, int cz, int radius, MapGenConfig config)
        {
            float sumH = 0f;
            float maxSl = 0f;
            int n = 0;
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    int gx = cx + dx;
                    int gz = cz + dz;
                    if (!grid.InBoundsCell(gx, gz)) continue;
                    ref var c = ref grid.GetCell(gx, gz);
                    sumH += c.height01;
                    maxSl = Mathf.Max(maxSl, c.slopeDeg);
                    n++;
                }
            }

            float meanH = n > 0 ? sumH / n : 0.5f;
            float score = 0f;
            if (config.alphaPreferPlainsForCities)
            {
                score += (1f - meanH) * 4f;
                score -= (maxSl / 90f) * 2f;
                if (meanH > config.alphaCityCenterMaxMeanHeight01)
                    score -= 5f;
            }

            int dw = WaterDistanceField.Get(grid, cx, cz);
            if (config.alphaMinChebyshevFromWaterForSpawn > 0 && dw < WaterDistanceField.UnreachableDistance - 100)
            {
                if (dw < config.alphaMinChebyshevFromWaterForSpawn)
                    score -= 8f;
                else
                    score += Mathf.Min(3f, (dw - config.alphaMinChebyshevFromWaterForSpawn) * 0.15f);
            }

            return score;
        }

        static bool IsValidCityCenter(GridSystem grid, int cx, int cz, int radius, int waterBuffer, float maxSlope, MapGenConfig config)
        {
            int checkRadius = radius + waterBuffer;
            float sumH = 0f;
            int n = 0;
            for (int dx = -checkRadius; dx <= checkRadius; dx++)
            {
                for (int dz = -checkRadius; dz <= checkRadius; dz++)
                {
                    int gx = cx + dx;
                    int gz = cz + dz;
                    if (!grid.InBoundsCell(gx, gz)) return false;
                    ref var cell = ref grid.GetCell(gx, gz);
                    if (cell.type == CellType.Water || cell.type == CellType.River || cell.type == CellType.Mountain)
                        return false;
                    if (Mathf.Abs(dx) <= radius && Mathf.Abs(dz) <= radius && cell.slopeDeg > maxSlope)
                        return false;
                    if (Mathf.Abs(dx) <= radius && Mathf.Abs(dz) <= radius)
                    {
                        sumH += cell.height01;
                        n++;
                    }
                }
            }

            if (config.alphaMinChebyshevFromWaterForSpawn > 0 && grid.DistanceToWaterCells != null)
            {
                if (WaterDistanceField.Get(grid, cx, cz) < config.alphaMinChebyshevFromWaterForSpawn)
                    return false;
            }

            if (config.alphaPreferPlainsForCities && n > 0)
            {
                if (sumH / n > config.alphaCityCenterMaxMeanHeight01)
                    return false;
            }

            return true;
        }

        static bool FarEnoughFromCities(List<CityNode> cities, int cx, int cz, int minDist)
        {
            foreach (var c in cities)
            {
                int dx = cx - c.Center.x;
                int dz = cz - c.Center.y;
                if (dx * dx + dz * dz < minDist * minDist) return false;
            }
            return true;
        }

        static void MarkCityArea(GridSystem grid, CityNode node)
        {
            int cx = node.Center.x;
            int cz = node.Center.y;
            int r = node.RadiusCells;
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    int gx = cx + dx;
                    int gz = cz + dz;
                    if (grid.InBoundsCell(gx, gz))
                    {
                        ref var cell = ref grid.GetCell(gx, gz);
                        cell.cityId = node.Id;
                        cell.buildable = true;
                    }
                }
        }
    }
}
