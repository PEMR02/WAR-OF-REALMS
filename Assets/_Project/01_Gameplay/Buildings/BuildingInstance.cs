using UnityEngine;
using Project.Gameplay.Map;
using Project.Gameplay.Combat;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Representa una instancia de edificio en el mundo.
    /// Mantiene referencia al BuildingSO que lo generó.
    /// Gestiona la ocupación de celdas en el MapGrid durante su ciclo de vida.
    /// </summary>
    public class BuildingInstance : MonoBehaviour
    {
        [Header("Building Data")]
        public BuildingSO buildingSO;  // El SO que define este edificio
        
        [Header("Runtime")]
        public float constructionProgress = 1f;  // 0-1 (1 = completado)
        public bool isComplete = true;

        bool _cellsOccupied;  // 🟢 Control de ocupación de celdas
        
        void Start()
        {
            // Comprobado en Start() para que el generador de mapa pueda asignar buildingSO
            // después de AddComponent<BuildingInstance>() en el mismo frame (p. ej. Town Centers).
            if (buildingSO == null)
            {
                Debug.LogWarning($"BuildingInstance en {gameObject.name} no tiene BuildingSO asignado.");
                return;
            }

            // Vida: inicializar desde BuildingSO (añade Health si no existe)
            var health = GetComponent<Health>();
            if (health == null) health = gameObject.AddComponent<Health>();
            health.InitFromMax(buildingSO.maxHP);

            // 🟢 Si viene de un BuildSite, este método es llamado explícitamente desde BuildSite.Complete()
            // Si se crea directamente (ej. generador de mapa), ocupar celdas automáticamente
            if (!_cellsOccupied)
            {
                OccupyCellsOnStart();
            }
        }

        /// <summary>
        /// Ocupa las celdas del footprint del edificio en el MapGrid.
        /// Llamado desde BuildSite.Complete() o automáticamente en Start() si no viene de un BuildSite.
        /// </summary>
        public void OccupyCellsOnStart()
        {
            if (_cellsOccupied) return;  // Ya ocupadas
            if (buildingSO == null) return;
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return;

            Vector2Int center = MapGrid.Instance.WorldToCell(transform.position);
            Vector2Int size = new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt(buildingSO.size.x)),
                Mathf.Max(1, Mathf.RoundToInt(buildingSO.size.y))
            );
            Vector2Int min = new Vector2Int(center.x - size.x / 2, center.y - size.y / 2);

            MapGrid.Instance.SetOccupiedRect(min, size, true);
            _cellsOccupied = true;

            Debug.Log($"🏢 BuildingInstance: Ocupado {size.x}×{size.y} en {center} ({buildingSO.id})");
        }

        void OnDestroy()
        {
            // 🟢 Liberar celdas al destruir el edificio
            if (_cellsOccupied)
            {
                FreeCells();
            }
        }

        /// <summary>Libera las celdas ocupadas por este edificio.</summary>
        void FreeCells()
        {
            if (buildingSO == null) return;
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return;

            Vector2Int center = MapGrid.Instance.WorldToCell(transform.position);
            Vector2Int size = new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt(buildingSO.size.x)),
                Mathf.Max(1, Mathf.RoundToInt(buildingSO.size.y))
            );
            Vector2Int min = new Vector2Int(center.x - size.x / 2, center.y - size.y / 2);

            MapGrid.Instance.SetOccupiedRect(min, size, false);
            _cellsOccupied = false;

            Debug.Log($"🔓 BuildingInstance: Liberado {size.x}×{size.y} en {center} ({buildingSO.id})");
        }
    }
}
