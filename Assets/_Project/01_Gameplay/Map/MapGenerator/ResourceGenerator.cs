using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 8: coloca recursos por rings (cerca/medio/lejos) por ciudad. Marca resourceType; valida buildable/walkable.</summary>
    public static class ResourceGenerator
    {
        /// <summary>Parámetros: ringNear, ringMid, ringFar; minWoodPerCity, minStonePerCity, etc.; maxResourceRetries.</summary>
        public static void PlaceResources(GridSystem grid, List<CityNode> cities, MapGenConfig config, IRng rng)
        {
            if (grid == null || config == null || rng == null) return;

            int wood = 0, stone = 0, gold = 0, food = 0;
            foreach (var city in cities)
            {
                PlaceInRing(grid, city.Center, config.ringNear, config.minWoodPerCity, ResourceType.Wood, rng, ref wood);
                PlaceInRing(grid, city.Center, config.ringMid, config.minStonePerCity, ResourceType.Stone, rng, ref stone);
                PlaceInRing(grid, city.Center, config.ringMid, config.minGoldPerCity, ResourceType.Gold, rng, ref gold);
                PlaceInRing(grid, city.Center, config.ringNear, config.minFoodPerCity, ResourceType.Food, rng, ref food);
            }

            if (config.debugLogs)
                Debug.Log($"Fase8 Recursos: Wood={wood}, Stone={stone}, Gold={gold}, Food={food}.");
        }

        private static void PlaceInRing(GridSystem grid, Vector2Int center, Vector2Int ring, int count, ResourceType type, IRng rng, ref int placed)
        {
            int minR = Mathf.Min(ring.x, ring.y);
            int maxR = Mathf.Max(ring.x, ring.y);
            for (int i = 0; i < count + 5; i++)
            {
                int r = rng.NextInt(minR, maxR + 1);
                int angle = rng.NextInt(0, 360);
                float rad = angle * Mathf.Deg2Rad;
                int cx = center.x + Mathf.RoundToInt(r * Mathf.Cos(rad));
                int cz = center.y + Mathf.RoundToInt(r * Mathf.Sin(rad));
                if (!grid.InBoundsCell(cx, cz)) continue;
                ref var cell = ref grid.GetCell(cx, cz);
                if (cell.type != CellType.Land || cell.resourceType != ResourceType.None || !cell.walkable) continue;
                cell.resourceType = type;
                placed++;
                if (placed >= count) break;
            }
        }
    }
}
