using UnityEngine;

namespace Project.Gameplay.Combat
{
    /// <summary>
    /// Cualquier cosa que pueda mostrar una barra en el mundo: vida (unidades/edificios) o cantidad restante (recursos).
    /// Una sola barra visual (WorldBarView/HealthBarWorld) lee esta interfaz.
    /// </summary>
    public interface IWorldBarSource
    {
        /// <summary>Ratio 0-1 (lleno = 1, vacío = 0).</summary>
        float GetBarRatio01();
        /// <summary>Color cuando la barra está llena (ej. verde vida, amarillo madera).</summary>
        Color GetBarFullColor();
        /// <summary>Color cuando la barra está vacía (ej. rojo daño, oscuro recurso).</summary>
        Color GetBarEmptyColor();
        /// <summary>Si false, la barra no debe mostrarse (muerto, agotado, etc.).</summary>
        bool IsBarVisible();
    }
}
