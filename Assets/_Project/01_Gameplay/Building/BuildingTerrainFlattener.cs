using UnityEngine;
using Project.Gameplay.Map;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Aplana el terreno bajo el footprint de un edificio usando TerrainData.SetHeights,
    /// estilo Anno: zona relativamente plana bajo la base, con pequeña transición suave.
    /// </summary>
    public static class BuildingTerrainFlattener
    {
        /// <param name="centerWorld">Centro del edificio en mundo.</param>
        /// <param name="sizeInCells">Tamaño del footprint en celdas (BuildingSO.size).</param>
        /// <param name="targetBaseY">Altura objetivo para la base (placementY calculado previamente).</param>
        public static void FlattenUnderBuilding(Vector3 centerWorld, Vector2 sizeInCells, float targetBaseY)
        {
            var terrain = Terrain.activeTerrain ?? Object.FindFirstObjectByType<Terrain>();
            if (terrain == null) return;
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return;

            TerrainData data = terrain.terrainData;
            if (data == null) return;

            Vector3 terrainPos = terrain.transform.position;
            Vector3 terrainSize = data.size;

            // Área aplanada en metros: footprint del edificio con un pequeño margen.
            float cellSize = MapGrid.Instance.cellSize;
            float wMeters = Mathf.Max(1, sizeInCells.x) * cellSize;
            float dMeters = Mathf.Max(1, sizeInCells.y) * cellSize;
            const float extraMargin = 0.5f;
            wMeters += extraMargin * 2f;
            dMeters += extraMargin * 2f;

            float halfW = wMeters * 0.5f;
            float halfD = dMeters * 0.5f;

            float minWorldX = centerWorld.x - halfW;
            float maxWorldX = centerWorld.x + halfW;
            float minWorldZ = centerWorld.z - halfD;
            float maxWorldZ = centerWorld.z + halfD;

            // Convertir a coordenadas de heightmap (índices).
            int hmRes = data.heightmapResolution;

            int minHX = Mathf.Clamp(Mathf.RoundToInt((minWorldX - terrainPos.x) / terrainSize.x * (hmRes - 1)), 0, hmRes - 1);
            int maxHX = Mathf.Clamp(Mathf.RoundToInt((maxWorldX - terrainPos.x) / terrainSize.x * (hmRes - 1)), 0, hmRes - 1);
            int minHZ = Mathf.Clamp(Mathf.RoundToInt((minWorldZ - terrainPos.z) / terrainSize.z * (hmRes - 1)), 0, hmRes - 1);
            int maxHZ = Mathf.Clamp(Mathf.RoundToInt((maxWorldZ - terrainPos.z) / terrainSize.z * (hmRes - 1)), 0, hmRes - 1);

            if (minHX >= maxHX || minHZ >= maxHZ) return;

            int width = maxHX - minHX + 1;
            int depth = maxHZ - minHZ + 1;

            float targetHeight01 = Mathf.InverseLerp(terrainPos.y, terrainPos.y + terrainSize.y, targetBaseY);

            float[,] heights = data.GetHeights(minHX, minHZ, width, depth);

            // Radio interno completamente plano y borde con transición suave para no crear un "muro" brusco.
            float innerRadius = 0.35f;
            float outerRadius = 0.5f;

            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Coordenadas locales normalizadas en [-0.5, 0.5]
                    float nx = (x / (float)(width - 1)) - 0.5f;
                    float nz = (z / (float)(depth - 1)) - 0.5f;
                    float dist = Mathf.Sqrt(nx * nx + nz * nz);

                    float t;
                    if (dist <= innerRadius)
                        t = 1f;
                    else if (dist >= outerRadius)
                        t = 0f;
                    else
                        t = 1f - Mathf.InverseLerp(innerRadius, outerRadius, dist);

                    if (t <= 0f) continue;

                    float current = heights[z, x];
                    float blended = Mathf.Lerp(current, targetHeight01, t);
                    heights[z, x] = blended;
                }
            }

            data.SetHeights(minHX, minHZ, heights);
        }
    }
}

