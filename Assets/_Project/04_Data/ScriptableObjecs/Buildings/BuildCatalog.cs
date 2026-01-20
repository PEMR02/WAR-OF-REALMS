using System;
using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Buildings;

public enum BuildCategory { Econ, Military, Defenses, Special }

[CreateAssetMenu(menuName = "RTS/Build Catalog")]
public class BuildCatalog : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public BuildCategory category;

        [Range(1, 9)]
        public int slot; // 1..9

        public BuildingSO building;
    }

    public List<Entry> entries = new();

    public BuildingSO Get(BuildCategory cat, int slot)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.category == cat && e.slot == slot)
                return e.building;
        }
        return null;
    }
}
