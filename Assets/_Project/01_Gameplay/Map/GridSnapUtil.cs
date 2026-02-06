using UnityEngine;

namespace Project.Gameplay.Map
{
    /// <summary>
    /// Utilidades de snap determinista para RTS.
    /// - Grid base: líneas/intersecciones cada cellSize desde origin (esquinas de celdas).
    /// - Building grid: snap al CENTRO del footprint (width x height en celdas), sin drift acumulativo.
    /// </summary>
    public static class GridSnapUtil
    {
        /// <summary>
        /// Snap a intersección del grid (líneas) usando origin.
        /// Mantiene compatibilidad con el comportamiento clásico (1x1).
        /// </summary>
        public static Vector3 SnapToGridIntersection(Vector3 worldPos, Vector3 origin, float cellSize)
        {
            if (cellSize <= 0.0001f) return worldPos;
            float gx = (worldPos.x - origin.x) / cellSize;
            float gz = (worldPos.z - origin.z) / cellSize;
            float sx = origin.x + Mathf.Round(gx) * cellSize;
            float sz = origin.z + Mathf.Round(gz) * cellSize;
            return new Vector3(sx, worldPos.y, sz);
        }

        /// <summary>
        /// Firma requerida (sin origin). Asume origin = (0,0,0).
        /// Úsala solo si no tienes acceso al origin real del MapGrid.
        /// </summary>
        public static Vector3 SnapToBuildingGrid(Vector3 worldPos, float cellSize, int width, int height)
            => SnapToBuildingGrid(worldPos, Vector3.zero, cellSize, width, height);

        /// <summary>
        /// Snap al CENTRO del footprint del edificio (width x height en celdas) usando origin.
        /// Centro = esquina inferior-izquierda + (width*cellSize, height*cellSize)/2.
        /// Soporta tamaños pares/impares sin offsets acumulativos.
        /// </summary>
        public static Vector3 SnapToBuildingGrid(Vector3 worldPos, Vector3 origin, float cellSize, int width, int height)
        {
            if (cellSize <= 0.0001f) return worldPos;
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);

            float u = (worldPos.x - origin.x) / cellSize;
            float v = (worldPos.z - origin.z) / cellSize;

            float halfW = width * 0.5f;
            float halfH = height * 0.5f;

            // Elegir la celda "min" (esquina inferior-izq del footprint) de forma estable.
            int minCellX = Mathf.RoundToInt(u - halfW);
            int minCellZ = Mathf.RoundToInt(v - halfH);

            // Centro del footprint en coordenadas del grid (en unidades de celda).
            float centerU = minCellX + halfW;
            float centerV = minCellZ + halfH;

            float sx = origin.x + centerU * cellSize;
            float sz = origin.z + centerV * cellSize;
            return new Vector3(sx, worldPos.y, sz);
        }
    }
}

