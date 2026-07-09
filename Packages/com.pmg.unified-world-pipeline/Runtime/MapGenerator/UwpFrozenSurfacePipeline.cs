using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// UWP: freeze cache visual → mesh agua → carve terreno (una pasada) → auditoría final.
    /// Compartido entre MapGenerator (Play/RTS) y runner UWP del Editor.
    /// </summary>
    public static class UwpFrozenSurfacePipeline
    {
        public struct TerrainExportLayers
        {
            public TerrainLayer grass;
            public TerrainLayer dirt;
            public TerrainLayer rock;
            public TerrainLayer sand;
            public Vector2 grassTile;
            public Vector2 dirtTile;
            public Vector2 rockTile;
            public Vector2 sandTile;
            public int sandShoreCells;
        }

        public static bool ShouldUse(MapGenConfig config)
        {
            return config != null &&
                   config.uwpOwnedVisualPolicy &&
                   config.riverVisualUseRiverSurfaceMeshStrip;
        }

        /// <summary>Congela cache, construye agua, exporta terreno y valida mesh/carve.</summary>
        public static GameObject Apply(
            GridSystem grid,
            MapGenConfig config,
            Terrain terrain,
            Material waterMaterial,
            List<Vector2Int> spawnCells,
            List<CityNode> cities,
            List<Road> roads,
            TerrainExportLayers layers)
        {
            if (grid == null || config == null)
                return null;

            WaterVisualPipelinePolicy.ApplyToRuntimeConfig(config);

            if (!grid.RiverVisualSurfaceCacheFrozen &&
                !RiverSurfaceMeshBuilder.FreezeUwpFinalWaterVisualSurfaceCache(grid, config))
            {
                Debug.LogError("[UwpFrozenSurfacePipeline] FreezeUwpFinalWaterVisualSurfaceCache falló.");
            }

            TerrainExporter.CleanUwpSkippedTributaryFunctionalData(grid, config);

            GameObject waterRoot = WaterMeshBuilder.BuildWaterMeshes(
                grid, config, waterMaterial, spawnCells, cities, roads);

            if (terrain != null)
            {
                TerrainExporter.ApplyToTerrain(
                    terrain, grid, config,
                    layers.grass, layers.dirt, layers.rock,
                    layers.grassTile, layers.dirtTile, layers.rockTile,
                    layers.sand, layers.sandTile, layers.sandShoreCells);
                TerrainSplatDebugDisplay.Refresh(terrain, config);
            }

            RiverSurfaceMeshBuilder.ValidateAndLogUwpWaterSurfaceFinal(grid, config, waterRoot);

            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[UWP] Play pipeline frozen={(grid.RiverVisualSurfaceCacheFrozen ? 1 : 0)} " +
                    $"surfaces={grid.RiverVisualSurfaces?.Count ?? 0} seed={config.seed}");
            }
            else
            {
                Debug.LogWarning(
                    $"[UWP] Play pipeline frozen={(grid.RiverVisualSurfaceCacheFrozen ? 1 : 0)} " +
                    $"surfaces={grid.RiverVisualSurfaces?.Count ?? 0} seed={config.seed}");
            }

            return waterRoot;
        }
    }
}
