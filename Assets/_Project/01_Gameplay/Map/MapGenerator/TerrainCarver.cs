using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 7: aplana height01 en áreas de ciudad y en caminos (suavizado).</summary>
    public static class TerrainCarver
    {
        /// <summary>Parámetros: cityRadiusCells. Reduce height01 variación dentro del radio de cada ciudad.</summary>
        public static void ApplyCityFlatten(GridSystem grid, List<CityNode> cities, MapGenConfig config)
        {
            if (grid == null || cities == null || config == null) return;

            foreach (var city in cities)
            {
                int cx = city.Center.x;
                int cz = city.Center.y;
                int r = city.RadiusCells;
                float centerH = grid.GetCell(cx, cz).height01;
                for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        int gx = cx + dx;
                        int gz = cz + dz;
                        if (!grid.InBoundsCell(gx, gz)) continue;
                        ref var cell = ref grid.GetCell(gx, gz);
                        if (cell.type == CellType.Water || cell.type == CellType.River) continue;
                        float t = 1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz) / (r + 1f));
                        cell.height01 = Mathf.Lerp(cell.height01, centerH, t * config.roadFlattenStrength);
                    }
            }
            if (config.debugLogs)
                Debug.Log("Fase7 Carve: ciudades aplanadas.");
        }

        /// <summary>Parámetros: roadWidthCells, roadFlattenStrength. Suaviza height01 a lo largo de cada camino.</summary>
        public static void ApplyRoadFlatten(GridSystem grid, List<Road> roads, MapGenConfig config)
        {
            if (grid == null || roads == null || config == null) return;

            int halfW = config.roadWidthCells / 2;
            foreach (var road in roads)
            {
                foreach (var c in road.PathCells)
                {
                    float h = grid.GetCell(c.x, c.y).height01;
                    for (int dx = -halfW; dx <= halfW; dx++)
                        for (int dz = -halfW; dz <= halfW; dz++)
                        {
                            int gx = c.x + dx;
                            int gz = c.y + dz;
                            if (!grid.InBoundsCell(gx, gz)) continue;
                            ref var cell = ref grid.GetCell(gx, gz);
                            if (cell.type == CellType.Water || cell.type == CellType.River) continue;
                            cell.height01 = Mathf.Lerp(cell.height01, h, config.roadFlattenStrength);
                        }
                }
            }
            if (config.debugLogs)
                Debug.Log("Fase7 Carve: caminos suavizados.");
        }
    }
}
