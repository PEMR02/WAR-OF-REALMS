using UnityEngine;
using Project.Gameplay.Map.Generator;

namespace Project.Gameplay.Map.Generation.Alpha
{
    /// <summary>
    /// Clasificación semántica por celda (altura, pendiente, distancia al agua). Usada por recursos y spawns alpha.
    /// </summary>
    public static class RegionClassifier
    {
        public static SemanticRegionMap Classify(GridSystem grid, RegionClassificationConfig rules)
        {
            if (grid == null || rules == null) return null;
            if (grid.DistanceToWaterCells == null)
                WaterDistanceField.Build(grid);

            var map = new SemanticRegionMap(grid.Width, grid.Height);
            int w = grid.Width;
            int h = grid.Height;
            int nearW = Mathf.Max(1, rules.nearWaterDistanceCells);
            int shore = Mathf.Max(1, rules.shorelineDistanceCells);
            int flood = Mathf.Max(1, rules.floodplainNearRiverDistanceCells);
            int inf = WaterDistanceField.UnreachableDistance;

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    ref var cell = ref grid.GetCell(x, z);
                    int dw = WaterDistanceField.Get(grid, x, z);

                    if (cell.type == CellType.Water || cell.type == CellType.River)
                    {
                        map.Set(x, z, TerrainRegionType.WetZone);
                        continue;
                    }

                    float height = cell.height01;
                    float slope = cell.slopeDeg;
                    TerrainRegionType t = TerrainRegionType.Plain;

                    if (height >= rules.mountainHeightThreshold01 && slope >= rules.mountainSlopeThresholdDeg)
                        t = TerrainRegionType.Mountain;
                    else if (slope >= rules.rockyZoneSlopeThresholdDeg && height >= rules.hillHeightThreshold01 * 0.95f)
                        t = TerrainRegionType.RockyZone;
                    else if (height >= rules.hillHeightThreshold01)
                        t = TerrainRegionType.Hill;

                    if (dw < inf && dw <= nearW && t != TerrainRegionType.Mountain && t != TerrainRegionType.RockyZone)
                        t = dw <= shore ? TerrainRegionType.LakeShore : TerrainRegionType.RiverBank;

                    if ((t == TerrainRegionType.Plain || t == TerrainRegionType.Hill || t == TerrainRegionType.RiverBank)
                        && dw < inf && dw > nearW && dw <= flood + nearW
                        && rules.fertileZoneHumidityBonus > 0.01f)
                        t = TerrainRegionType.ForestCandidate;

                    if ((t == TerrainRegionType.Plain || t == TerrainRegionType.Hill)
                        && dw < inf && dw >= nearW + 1
                        && slope <= 22f
                        && height < rules.hillHeightThreshold01 * 1.02f)
                        t = TerrainRegionType.SpawnFriendly;

                    map.Set(x, z, t);
                }
            }

            return map;
        }
    }
}
