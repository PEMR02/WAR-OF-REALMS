using UnityEngine;
using Project.Gameplay.Buildings;

namespace Project.Gameplay.Map
{
    /// <summary>
    /// Grid visible only while placing buildings; hidden during normal gameplay (Manor Lords / AoE IV style).
    /// Drives GridGizmoRenderer or any custom grid renderer via showOnlyInBuildMode.
    /// </summary>
    public class GridVisibility : MonoBehaviour
    {
        [Header("Grid Renderer")]
        [Tooltip("Si no asignas, se busca GridGizmoRenderer en la escena.")]
        public GridGizmoRenderer gridGizmoRenderer;
        [Tooltip("Referencia opcional al BuildingPlacer para detectar modo construcción.")]
        public BuildingPlacer buildingPlacer;

        void Awake()
        {
            if (gridGizmoRenderer == null)
                gridGizmoRenderer = FindFirstObjectByType<GridGizmoRenderer>();
            if (buildingPlacer == null)
                buildingPlacer = FindFirstObjectByType<BuildingPlacer>();
            if (gridGizmoRenderer != null)
            {
                gridGizmoRenderer.showOnlyInBuildMode = true;
                if (buildingPlacer != null)
                    gridGizmoRenderer.buildingPlacer = buildingPlacer;
            }
        }
    }
}
