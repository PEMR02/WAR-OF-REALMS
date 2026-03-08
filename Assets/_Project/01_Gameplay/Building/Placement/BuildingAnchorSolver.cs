using UnityEngine;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Calcula la altura final de colocación del edificio (Y) y el offset del pivot respecto a la base.
    /// Usa la altura promedio del footprint para apoyar el edificio de forma estable.
    /// </summary>
    public static class BuildingAnchorSolver
    {
        /// <summary>
        /// Resuelve la posición Y final del edificio y el offset a aplicar al pivot para que la base visual quede sobre el terreno.
        /// </summary>
        /// <param name="sample">Resultado de FootprintTerrainSampler.Sample.</param>
        /// <param name="pivotToBottomOffset">Distancia desde el pivot del prefab hasta la base visual (Y). Positivo = pivot por encima de la base.</param>
        /// <param name="placementY">Altura Y de colocación (avgHeight del footprint).</param>
        /// <param name="visualOffsetY">Offset a sumar al pivot para que la base coincida con el suelo (suele ser +pivotToBottomOffset para que la base quede en placementY).</param>
        public static void Solve(
            in FootprintTerrainSampler.SampleResult sample,
            float pivotToBottomOffset,
            out float placementY,
            out float visualOffsetY)
        {
            placementY = sample.valid ? sample.avgHeight : 0f;
            // Queremos que la BASE del edificio esté en placementY. El pivot está pivotToBottomOffset por encima de la base.
            // Por tanto: position.y = placementY + pivotToBottomOffset  =>  visualOffsetY = pivotToBottomOffset
            visualOffsetY = pivotToBottomOffset;
        }
    }
}
