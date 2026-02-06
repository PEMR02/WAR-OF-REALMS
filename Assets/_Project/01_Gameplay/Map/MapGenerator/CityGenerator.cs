using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 5: selecciona CityNodes (centros válidos: planos, buildable, lejos de agua). Marca cityId y buildable.</summary>
    public static class CityGenerator
    {
        /// <summary>Parámetros: cityCount, minCityDistanceCells, cityRadiusCells, maxCitySlopeDeg. Retorna lista de CityNode.</summary>
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

            for (int i = 0; i < count; i++)
            {
                int attempts = 50;
                while (attempts-- > 0)
                {
                    int margin = radius + waterBuffer + 2;
                    int cx = rng.NextInt(margin, w - margin);
                    int cz = rng.NextInt(margin, h - margin);
                    if (!IsValidCityCenter(grid, cx, cz, radius, waterBuffer, maxSlope)) continue;
                    if (!FarEnoughFromCities(cities, cx, cz, minDist)) continue;

                    var node = new CityNode { Id = i, Center = new Vector2Int(cx, cz), RadiusCells = radius };
                    cities.Add(node);
                    MarkCityArea(grid, node);
                    break;
                }
            }

            if (config.debugLogs)
                Debug.Log($"Fase5 Ciudades: {cities.Count} colocadas.");
            return cities;
        }

        private static bool IsValidCityCenter(GridSystem grid, int cx, int cz, int radius, int waterBuffer, float maxSlope)
        {
            int checkRadius = radius + waterBuffer;
            for (int dx = -checkRadius; dx <= checkRadius; dx++)
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
                }
            return true;
        }

        private static bool FarEnoughFromCities(List<CityNode> cities, int cx, int cz, int minDist)
        {
            foreach (var c in cities)
            {
                int dx = cx - c.Center.x;
                int dz = cz - c.Center.y;
                if (dx * dx + dz * dz < minDist * minDist) return false;
            }
            return true;
        }

        private static void MarkCityArea(GridSystem grid, CityNode node)
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
