using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Map;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Opcional: sincroniza el GridSystem del Generador Definitivo con MapGrid (tu grid de gameplay). Suscríbete a OnGenerationComplete.</summary>
    public class MapGeneratorBridge : MonoBehaviour
    {
        [Tooltip("Si está asignado, al terminar Generate() se sincroniza este MapGrid con el GridSystem. Si no, se usa MapGrid.Instance.")]
        public MapGrid mapGridOverride;

        void OnEnable()
        {
            MapGenerator.OnGenerationComplete += OnMapGenerated;
        }

        void OnDisable()
        {
            MapGenerator.OnGenerationComplete -= OnMapGenerated;
        }

        void OnMapGenerated(GridSystem grid, List<CityNode> cities, List<Road> roads, MapGenConfig config)
        {
            if (grid == null) return;

            MapGrid target = mapGridOverride != null ? mapGridOverride : MapGrid.Instance;
            if (target == null)
            {
                Debug.Log("MapGeneratorBridge: MapGrid no encontrado. Asigna mapGridOverride o asegura que MapGrid.Instance exista para sincronizar.");
                return;
            }

            SyncGridToMapGrid(grid, target);
            Debug.Log($"MapGeneratorBridge: MapGrid sincronizado. Ciudades={cities?.Count ?? 0}, Caminos={roads?.Count ?? 0}. Coloca TCs/recursos desde Cities y Grid.");
        }

        /// <summary>Copia water y blocked del GridSystem al MapGrid. No toca occupied (eso lo hace tu colocación de edificios).</summary>
        public static void SyncGridToMapGrid(GridSystem grid, MapGrid mapGrid)
        {
            if (grid == null || mapGrid == null) return;

            mapGrid.Initialize(grid.Width, grid.Height, grid.CellSizeWorld, grid.Origin);

            for (int x = 0; x < grid.Width; x++)
            {
                for (int z = 0; z < grid.Height; z++)
                {
                    var c = new Vector2Int(x, z);
                    ref var cell = ref grid.GetCell(c);
                    bool isWater = cell.type == CellType.Water || cell.type == CellType.River;
                    bool blocked = !cell.walkable || isWater || cell.type == CellType.Mountain;
                    mapGrid.SetWater(c, isWater);
                    mapGrid.SetBlocked(c, blocked);
                }
            }
        }
    }
}
