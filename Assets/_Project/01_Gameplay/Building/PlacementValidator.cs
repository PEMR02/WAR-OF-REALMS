using UnityEngine;
using Project.Gameplay.Map;

namespace Project.Gameplay.Buildings
{
    public static class PlacementValidator
    {
        static readonly Collider[] OverlapBuffer = new Collider[64];

        // Valida sobre Ground y sin colisiones con Unit/Building/Obstacle.
        // size = tamaño en CELDAS de la grilla (ej. 3x3). Se usa cellSize del MapGrid para convertir a mundo en el OverlapBox.
        /// <param name="overlapInset">Margen interno (metros) para el OverlapBox; evita que dos edificios adyacentes (que solo se tocan en el borde) se rechacen. 0.05–0.1 típico.</param>
        public static bool IsValidPlacement(
            Vector3 pos,
            Vector2 size,
            LayerMask blockingMask,
            float yOffset = 0.5f,
            float overlapInset = 0.08f)
        {
            float cellSize = (MapGrid.Instance != null && MapGrid.Instance.IsReady) ? MapGrid.Instance.cellSize : 1f;
            float wx = size.x * cellSize;
            float wz = size.y * cellSize;
            float hx = Mathf.Max(0.01f, wx * 0.5f - overlapInset);
            float hz = Mathf.Max(0.01f, wz * 0.5f - overlapInset);
            Vector3 halfExtents = new Vector3(hx, yOffset, hz);
            // Ignorar triggers: el muro compuesto usa un BoxCollider trigger grande (AABB del path) para selección;
            // sin esto, "Queries Hit Triggers" en Physics hace que todo el interior quede inválido para construir.
            int hitCount = Physics.OverlapBoxNonAlloc(pos, halfExtents, OverlapBuffer, Quaternion.identity, blockingMask, QueryTriggerInteraction.Ignore);
            if (hitCount >= OverlapBuffer.Length) return false;
            if (hitCount > 0) return false;

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
