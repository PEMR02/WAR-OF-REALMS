using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline
{
    public static class UwpGridLayoutUtility
    {
        public static void ApplyPipelineLayout(PMGUnifiedWorldPipelineConfig pipeline, MapGenConfig cfg)
        {
            if (pipeline == null || cfg == null)
                return;

            float cell = pipeline.uwpCellSizeWorld > 0.01f ? pipeline.uwpCellSizeWorld : 3f;
            int grid = pipeline.uwpGridCells > 0 ? pipeline.uwpGridCells : 358;
            cfg.cellSizeWorld = cell;
            cfg.gridW = grid;
            cfg.gridH = grid;
            cfg.origin = pipeline.centerMapAtOrigin
                ? new Vector3(-grid * cell * 0.5f, 0f, -grid * cell * 0.5f)
                : Vector3.zero;
        }
    }
}
