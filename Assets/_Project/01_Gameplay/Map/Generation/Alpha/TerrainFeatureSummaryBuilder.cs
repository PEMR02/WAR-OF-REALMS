using UnityEngine;
using Project.Gameplay.Map.Generator;

namespace Project.Gameplay.Map.Generation.Alpha
{
    /// <summary>Completa <see cref="TerrainFeatureRuntime"/> con datos derivados del grid (post-generación).</summary>
    public static class TerrainFeatureSummaryBuilder
    {
        public static void AppendFromGrid(GridSystem grid, MapGenConfig cfg, TerrainFeatureRuntime r)
        {
            if (r == null) return;
            r.rivers.Clear();
            var lines = grid?.RiverCenterlinesCellSpace;
            if (lines != null)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (line == null || line.Count < 2) continue;
                    var start = Vector2Int.RoundToInt(line[0]);
                    var end = Vector2Int.RoundToInt(line[line.Count - 1]);
                    r.rivers.Add(new RiverFeatureSummary
                    {
                        axisIndex = i,
                        sampleCount = line.Count,
                        startCell = start,
                        endCell = end
                    });
                }
            }

            r.lakes.Clear();
            int lakeCells = 0;
            Vector2Int seed = Vector2Int.zero;
            if (grid != null)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    for (int z = 0; z < grid.Height; z++)
                    {
                        ref var c = ref grid.GetCell(x, z);
                        if (c.type == CellType.Water)
                        {
                            lakeCells++;
                            if (lakeCells == 1) seed = new Vector2Int(x, z);
                        }
                    }
                }
            }

            r.lakes.Add(new LakeFeatureSummary
            {
                approxCellCount = lakeCells,
                seedCell = seed
            });
        }
    }
}
