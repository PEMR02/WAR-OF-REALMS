using System;
using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Units;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Catálogo que define qué unidades puede producir cada edificio
    /// Similar a BuildCatalog pero para unidades
    /// </summary>
    [CreateAssetMenu(menuName = "RTS/Production Catalog")]
    public class ProductionCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string buildingId;  // "barracks", "archery_range", "stable"
            
            [Range(1, 9)]
            public int slot; // 1..9 (hotkey)
            
            public UnitSO unit;
        }

        public List<Entry> entries = new();

        /// <summary>
        /// Obtiene la unidad en el slot específico para un edificio
        /// </summary>
        public UnitSO Get(string buildingId, int slot)
        {
            foreach (var entry in entries)
            {
                if (entry.buildingId == buildingId && entry.slot == slot)
                    return entry.unit;
            }
            return null;
        }

        /// <summary>
        /// Obtiene todas las unidades que puede producir un edificio
        /// </summary>
        public List<UnitSO> GetAllUnits(string buildingId)
        {
            List<UnitSO> units = new();
            foreach (var entry in entries)
            {
                if (entry.buildingId == buildingId && entry.unit != null)
                    units.Add(entry.unit);
            }
            return units;
        }

        /// <summary>
        /// Obtiene el slot de una unidad en un edificio específico
        /// </summary>
        public int GetSlot(string buildingId, UnitSO unit)
        {
            foreach (var entry in entries)
            {
                if (entry.buildingId == buildingId && entry.unit == unit)
                    return entry.slot;
            }
            return -1;
        }
    }
}
