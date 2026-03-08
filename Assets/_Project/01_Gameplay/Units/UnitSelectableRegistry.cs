using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Units
{
    /// <summary>
    /// Registro estático de UnitSelectable activos. Evita FindObjectsByType en box select y doble clic.
    /// </summary>
    public static class UnitSelectableRegistry
    {
        private static readonly List<UnitSelectable> _all = new List<UnitSelectable>(256);

        public static void Register(UnitSelectable u)
        {
            if (u != null && !_all.Contains(u))
                _all.Add(u);
        }

        public static void Unregister(UnitSelectable u)
        {
            if (u != null)
                _all.Remove(u);
        }

        /// <summary>Devuelve todos los registrados (no alloc si se itera sin modificar la lista).</summary>
        public static IReadOnlyList<UnitSelectable> All => _all;
    }
}
