using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Presets de mapas estilo Age of Empires 2.
    /// Cada preset define parámetros para generar un mapa con características específicas.
    /// </summary>
    public enum MapPresetType
    {
        Custom,          // Sin preset, usa valores del inspector
        Continental,     // Mapa estándar con bosques y lagos
        Archipelago,     // Muchas islas pequeñas
        GoldRush,        // Mucho oro en el centro
        Forest,          // Bosque denso
        Desert,          // Pocos árboles, terreno plano
        Rivers,          // Ríos principales
        TeamIslands,     // Islas por equipo
        Arabia           // Clásico AoE2: poco agua, bosques medianos
    }

    [System.Serializable]
    public class MapPreset
    {
        public string name;
        public string description;
        
        [Header("Agua")]
        public int riverCount;
        public int lakeCount;
        public int maxLakeCells;
        
        [Header("Árboles")]
        public Vector2Int globalTrees;
        public bool forestClustering;  // Si true, agrupa árboles en bosques
        [Range(0f, 1f)] public float clusterDensity;  // 0 = disperso, 1 = muy denso
        public int clusterMinSize;
        public int clusterMaxSize;
        
        [Header("Terreno")]
        public float terrainFlatness;
        public float heightMultiplier;
        
        [Header("Recursos")]
        public float resourceMultiplier;  // 1.0 = normal, 1.5 = más recursos
    }

    public static class MapPresets
    {
        public static MapPreset GetPreset(MapPresetType type)
        {
            switch (type)
            {
                case MapPresetType.Continental:
                    return new MapPreset
                    {
                        name = "Continental",
                        description = "Mapa estándar con balance de recursos",
                        riverCount = 2,
                        lakeCount = 3,
                        maxLakeCells = 800,
                        globalTrees = new Vector2Int(500, 750),
                        forestClustering = true,
                        clusterDensity = 0.6f,
                        clusterMinSize = 15,
                        clusterMaxSize = 40,
                        terrainFlatness = 0.6f,
                        heightMultiplier = 8f,
                        resourceMultiplier = 1.0f
                    };

                case MapPresetType.Archipelago:
                    return new MapPreset
                    {
                        name = "Archipelago",
                        description = "Muchas islas pequeñas, mucho agua",
                        riverCount = 0,
                        lakeCount = 10,
                        maxLakeCells = 1800,
                        globalTrees = new Vector2Int(250, 400),
                        forestClustering = true,
                        clusterDensity = 0.75f,
                        clusterMinSize = 10,
                        clusterMaxSize = 28,
                        terrainFlatness = 0.75f,
                        heightMultiplier = 5f,
                        resourceMultiplier = 0.8f
                    };

                case MapPresetType.GoldRush:
                    return new MapPreset
                    {
                        name = "Gold Rush",
                        description = "Mucho oro en el centro del mapa",
                        riverCount = 1,
                        lakeCount = 2,
                        maxLakeCells = 600,
                        globalTrees = new Vector2Int(350, 550),
                        forestClustering = true,
                        clusterDensity = 0.5f,
                        clusterMinSize = 8,
                        clusterMaxSize = 20,
                        terrainFlatness = 0.7f,
                        heightMultiplier = 5f,
                        resourceMultiplier = 1.0f
                    };

                case MapPresetType.Forest:
                    return new MapPreset
                    {
                        name = "Forest",
                        description = "Bosque muy denso, difícil de navegar",
                        riverCount = 2,
                        lakeCount = 2,
                        maxLakeCells = 400,
                        globalTrees = new Vector2Int(2200, 3200),
                        forestClustering = true,
                        clusterDensity = 0.92f,
                        clusterMinSize = 35,
                        clusterMaxSize = 85,
                        terrainFlatness = 0.45f,
                        heightMultiplier = 12f,
                        resourceMultiplier = 1.0f
                    };

                case MapPresetType.Desert:
                    return new MapPreset
                    {
                        name = "Desert",
                        description = "Pocos árboles, terreno plano, casi sin agua",
                        riverCount = 0,
                        lakeCount = 1,
                        maxLakeCells = 200,
                        globalTrees = new Vector2Int(40, 90),
                        forestClustering = false,
                        clusterDensity = 0.3f,
                        clusterMinSize = 5,
                        clusterMaxSize = 10,
                        terrainFlatness = 0.9f,
                        heightMultiplier = 3f,
                        resourceMultiplier = 0.7f
                    };

                case MapPresetType.Rivers:
                    return new MapPreset
                    {
                        name = "Rivers",
                        description = "Ríos principales dividen el mapa",
                        riverCount = 5,
                        lakeCount = 1,
                        maxLakeCells = 400,
                        globalTrees = new Vector2Int(450, 700),
                        forestClustering = true,
                        clusterDensity = 0.6f,
                        clusterMinSize = 12,
                        clusterMaxSize = 30,
                        terrainFlatness = 0.65f,
                        heightMultiplier = 7f,
                        resourceMultiplier = 1.0f
                    };

                case MapPresetType.TeamIslands:
                    return new MapPreset
                    {
                        name = "Team Islands",
                        description = "Islas grandes para equipos",
                        riverCount = 0,
                        lakeCount = 6,
                        maxLakeCells = 1500,
                        globalTrees = new Vector2Int(400, 600),
                        forestClustering = true,
                        clusterDensity = 0.65f,
                        clusterMinSize = 15,
                        clusterMaxSize = 35,
                        terrainFlatness = 0.7f,
                        heightMultiplier = 6f,
                        resourceMultiplier = 1.0f
                    };

                case MapPresetType.Arabia:
                    return new MapPreset
                    {
                        name = "Arabia",
                        description = "Clásico AoE2: poco agua, bosques medianos",
                        riverCount = 0,
                        lakeCount = 1,
                        maxLakeCells = 400,
                        globalTrees = new Vector2Int(400, 650),
                        forestClustering = true,
                        clusterDensity = 0.6f,
                        clusterMinSize = 12,
                        clusterMaxSize = 30,
                        terrainFlatness = 0.7f,
                        heightMultiplier = 6f,
                        resourceMultiplier = 1.0f
                    };

                default:
                    return null;  // Custom: usar valores del inspector
            }
        }
    }
}
