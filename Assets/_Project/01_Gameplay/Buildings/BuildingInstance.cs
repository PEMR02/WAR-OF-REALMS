using System.Collections.Generic;
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

        [Header("Path footprint (muros)")]
        [Tooltip("Si se asignan, se usa este rect en lugar de center+size para ocupar celdas (ej. muro con path).")]
        public Vector2Int? overrideOccupiedMin;
        public Vector2Int? overrideOccupiedSize;
        [Tooltip("Si se asignan, estas celdas tienen prioridad sobre el rect override para la ocupacion del grid.")]
        public List<Vector2Int> overrideOccupiedCells;
        [Tooltip("Muro compuesto por tramos: la vida va en cada segmento hijo, no en el root.")]
        public bool perSegmentHealth;

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

            if (perSegmentHealth)
            {
                if (!_cellsOccupied)
                    OccupyCellsOnStart();
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
            if (ShouldSkipGridOccupation()) return;

            if (overrideOccupiedCells != null && overrideOccupiedCells.Count > 0)
            {
                SetOccupiedCells(overrideOccupiedCells, true);
                _cellsOccupied = true;
                return;
            }

            // Muro por path con min/size del AABB del recorrido: ocupar rect llenaría el interior del polígono cerrado.
            // El footprint real son solo overrideOccupiedCells (perímetro); si faltan, no usar este rect.
            if (buildingSO != null && buildingSO.isCompound && buildingSO.compoundPathMode
                && overrideOccupiedMin.HasValue && overrideOccupiedSize.HasValue)
            {
                _cellsOccupied = true;
                return;
            }

            Vector2Int min;
            Vector2Int size;
            if (overrideOccupiedMin.HasValue && overrideOccupiedSize.HasValue && overrideOccupiedSize.Value.x > 0 && overrideOccupiedSize.Value.y > 0)
            {
                min = overrideOccupiedMin.Value;
                size = overrideOccupiedSize.Value;
            }
            else
            {
                Vector2Int center = MapGrid.Instance.WorldToCell(transform.position);
                size = new Vector2Int(
                    Mathf.Max(1, Mathf.RoundToInt(buildingSO.size.x)),
                    Mathf.Max(1, Mathf.RoundToInt(buildingSO.size.y))
                );
                min = new Vector2Int(center.x - size.x / 2, center.y - size.y / 2);
            }

            MapGrid.Instance.SetOccupiedRect(min, size, true);
            _cellsOccupied = true;
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
            if (ShouldSkipGridOccupation()) return;

            if (overrideOccupiedCells != null && overrideOccupiedCells.Count > 0)
            {
                SetOccupiedCells(overrideOccupiedCells, false);
                _cellsOccupied = false;
                return;
            }

            if (buildingSO != null && buildingSO.isCompound && buildingSO.compoundPathMode
                && overrideOccupiedMin.HasValue && overrideOccupiedSize.HasValue)
            {
                _cellsOccupied = false;
                return;
            }

            Vector2Int min;
            Vector2Int size;
            if (overrideOccupiedMin.HasValue && overrideOccupiedSize.HasValue && overrideOccupiedSize.Value.x > 0 && overrideOccupiedSize.Value.y > 0)
            {
                min = overrideOccupiedMin.Value;
                size = overrideOccupiedSize.Value;
            }
            else
            {
                Vector2Int center = MapGrid.Instance.WorldToCell(transform.position);
                size = new Vector2Int(
                    Mathf.Max(1, Mathf.RoundToInt(buildingSO.size.x)),
                    Mathf.Max(1, Mathf.RoundToInt(buildingSO.size.y))
                );
                min = new Vector2Int(center.x - size.x / 2, center.y - size.y / 2);
            }

            MapGrid.Instance.SetOccupiedRect(min, size, false);
            _cellsOccupied = false;
        }

        static void SetOccupiedCells(List<Vector2Int> cells, bool value)
        {
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady || cells == null) return;

            for (int i = 0; i < cells.Count; i++)
                MapGrid.Instance.SetOccupied(cells[i], value);
        }

        bool ShouldSkipGridOccupation()
        {
            // La puerta debe ser transitable para A*: el bloqueo real lo gestiona GateController con NavMeshObstacle.
            return buildingSO != null && buildingSO.id == "Muro_Puerta";
        }
    }
}
