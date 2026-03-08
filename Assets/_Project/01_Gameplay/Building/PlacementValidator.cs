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
                if (!MapGrid.Instance.IsWorldAreaFree(pos, size, true))
                    return false;
                // Estilo Anno: no construir sobre agua
                Vector2Int center = MapGrid.Instance.WorldToCell(pos);
                int w = Mathf.Max(1, Mathf.RoundToInt(size.x));
                int h = Mathf.Max(1, Mathf.RoundToInt(size.y));
                for (int dx = 0; dx < w; dx++)
                    for (int dy = 0; dy < h; dy++)
                    {
                        var c = new Vector2Int(center.x - w / 2 + dx, center.y - h / 2 + dy);
                        if (MapGrid.Instance.IsInBounds(c) && MapGrid.Instance.IsWater(c))
                            return false;
                    }
                return true;
            }

            return true;
        }
    }
}
