using System.Collections.Generic;
using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace Project.Gameplay.Map.CleanWaterPipeline
{
    /// <summary>
    /// Puente al pipeline visual UWP congelado (Fase9): freeze → mesh → carve → auditoría.
    /// Delega en <see cref="UwpFrozenSurfacePipeline"/> sin duplicar lógica.
    /// </summary>
    public static class CleanWaterFrozenVisualBridge
    {
        public static bool ShouldUse(MapGenConfig config) =>
            UwpFrozenSurfacePipeline.ShouldUse(config);

        public static GameObject Apply(
            GridSystem grid,
            MapGenConfig config,
            Terrain terrain,
            Material waterMaterial,
            List<Vector2Int> spawnCells,
            List<CityNode> cities,
            List<Road> roads,
            UwpFrozenSurfacePipeline.TerrainExportLayers layers) =>
            UwpFrozenSurfacePipeline.Apply(
                grid, config, terrain, waterMaterial, spawnCells, cities, roads, layers);
    }
}
