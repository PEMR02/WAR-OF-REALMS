using UnityEngine;
using Project.Gameplay.Map;

namespace Project.Gameplay.Buildings
{
    public static class PlacementValidator
    {
        // Valida sobre Ground y sin colisiones con Unit/Building/Obstacle.
        // size = tamaño en CELDAS de la grilla (ej. 3x3). Se usa cellSize del MapGrid para convertir a mundo en el OverlapBox.
        public static bool IsValidPlacement(
            Vector3 pos,
            Vector2 size,
            LayerMask blockingMask,
            float yOffset = 0.5f)
        {
            float cellSize = (MapGrid.Instance != null && MapGrid.Instance.IsReady) ? MapGrid.Instance.cellSize : 1f;
            float wx = size.x * cellSize;
            float wz = size.y * cellSize;
            Vector3 halfExtents = new Vector3(wx * 0.5f, yOffset, wz * 0.5f);
            Collider[] hits = Physics.OverlapBox(pos, halfExtents, Quaternion.identity, blockingMask);

            if (hits != null && hits.Length > 0) return false;

            if (MapGrid.Instance != null && MapGrid.Instance.IsReady)
            {
                return MapGrid.Instance.IsWorldAreaFree(pos, size, true);
            }

            return true;
        }
    }
}
