using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline
{
    /// <summary>Módulo UWP: materiales y capas terrain solo desde PMGUnifiedWorldPipelineConfig.</summary>
    public static class UwpVisualBindingsModule
    {
        public static void ApplyMaterials(MapGenConfig cfg, PMGUnifiedWorldPipelineConfig pipeline)
        {
            if (cfg == null || pipeline == null) return;

            if (pipeline.grassLayer != null) cfg.grassLayer = pipeline.grassLayer;
            if (pipeline.dirtLayer != null) cfg.dirtLayer = pipeline.dirtLayer;
            if (pipeline.rockLayer != null) cfg.rockLayer = pipeline.rockLayer;
            if (pipeline.sandLayer != null) cfg.sandLayer = pipeline.sandLayer;
            if (pipeline.riverWaterMaterial != null) cfg.riverWaterMaterial = pipeline.riverWaterMaterial;
            if (pipeline.lakeWaterMaterial != null) cfg.lakeWaterMaterial = pipeline.lakeWaterMaterial;
            if (pipeline.seaWaterMaterial != null) cfg.seaWaterMaterial = pipeline.seaWaterMaterial;
            if (pipeline.tributaryWaterMaterial != null) cfg.tributaryWaterMaterial = pipeline.tributaryWaterMaterial;
        }

        public static void ApplyToGenerator(
            PMGUnifiedWorldPipelineConfig pipeline,
            MapGenConfig cfg,
            MapGenerator generator)
        {
            if (pipeline == null || generator == null) return;

            pipeline.PullVisualBindingsFromPipelineOnly();

            generator.terrainGrassLayerOverride = pipeline.grassLayer;
            generator.terrainDirtLayerOverride = pipeline.dirtLayer;
            generator.terrainRockLayerOverride = pipeline.rockLayer;
            generator.terrainSandLayerOverride = pipeline.sandLayer;
            generator.terrainGrassTileSize = pipeline.grassTileSize;
            generator.terrainDirtTileSize = pipeline.dirtTileSize;
            generator.terrainRockTileSize = pipeline.rockTileSize;
            generator.terrainSandTileSize = pipeline.sandTileSize;
            generator.terrainSandShoreCells = pipeline.sandShoreCells > 0 ? pipeline.sandShoreCells : 3;

            ApplyMaterials(cfg, pipeline);
            if (cfg != null)
                cfg.paintTerrainByHeight = true;
        }

        public static bool HasMinimumBindings(PMGUnifiedWorldPipelineConfig pipeline)
        {
            if (pipeline == null) return false;
            return pipeline.grassLayer != null || pipeline.HasAnyTerrainLayerBinding();
        }
    }
}
