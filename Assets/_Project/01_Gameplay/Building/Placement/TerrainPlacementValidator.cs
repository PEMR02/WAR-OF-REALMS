using UnityEngine;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Valida si un footprint es construible según pendiente y diferencia de altura del terreno.
    /// </summary>
    public static class TerrainPlacementValidator
    {
        /// <summary>
        /// Valida usando el resultado del FootprintTerrainSampler.
        /// </summary>
        /// <param name="sample">Resultado de FootprintTerrainSampler.Sample.</param>
        /// <param name="maxHeightDelta">Diferencia máxima permitida entre min y max altura en el footprint (metros).</param>
        /// <param name="maxSlopeDegrees">Pendiente máxima permitida (grados). Opcional; si &lt;= 0 no se valida pendiente.</param>
        /// <param name="footprintSizeInCells">Tamaño del footprint (ej. 3x3) para estimar pendiente desde heightDelta.</param>
        public static bool IsValid(
            in FootprintTerrainSampler.SampleResult sample,
            float maxHeightDelta = 2f,
            float maxSlopeDegrees = 0f,
            Vector2 footprintSizeInCells = default)
        {
            if (!sample.valid) return false;
            if (sample.heightDelta > maxHeightDelta) return false;

            if (maxSlopeDegrees > 0f && footprintSizeInCells.x >= 1f && footprintSizeInCells.y >= 1f)
            {
                float cellSize = Project.Gameplay.Map.MapGrid.GetCellSizeOrDefault();
                float diagonal = Mathf.Sqrt(footprintSizeInCells.x * footprintSizeInCells.x + footprintSizeInCells.y * footprintSizeInCells.y) * cellSize;
                if (diagonal > 0.001f)
                {
                    float slopeRad = Mathf.Atan2(sample.heightDelta, diagonal);
                    float slopeDeg = slopeRad * Mathf.Rad2Deg;
                    if (slopeDeg > maxSlopeDegrees) return false;
                }
            }

            return true;
        }
    }
}
