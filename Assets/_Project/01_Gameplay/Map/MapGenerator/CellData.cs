using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    public enum CellType
    {
        Land = 0,
        Water = 1,
        River = 2,
        Mountain = 3
    }

    public enum ResourceType
    {
        None = 0,
        Wood = 1,
        Stone = 2,
        Gold = 3,
        Food = 4
    }

    /// <summary>Datos lógicos por celda del grid. Fuente única de verdad para altura, tipo, región, ciudad, camino, recurso.</summary>
    [System.Serializable]
    public struct CellData
    {
        [Range(0f, 1f)] public float height01;
        [Range(0f, 90f)] public float slopeDeg;
        public CellType type;
        public bool walkable;
        public bool buildable;
        public bool occupied;
        public int regionId;
        public int biomeId;
        public int cityId;
        public ResourceType resourceType;
        /// <summary>0 = ninguno, 1 = trail, 2 = main road, etc.</summary>
        public byte roadLevel;

        public static CellData Default()
        {
            return new CellData
            {
                height01 = 0.5f,
                slopeDeg = 0f,
                type = CellType.Land,
                walkable = true,
                buildable = true,
                occupied = false,
                regionId = 0,
                biomeId = 0,
                cityId = -1,
                resourceType = ResourceType.None,
                roadLevel = 0
            };
        }
    }
}
