using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Grid lógico: celdas (width x height) y nodos (width+1 x height+1). World &lt;-&gt; Cell &lt;-&gt; Node.</summary>
    public class GridSystem
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public float CellSizeWorld { get; private set; }
        public Vector3 Origin { get; private set; }

        private CellData[,] _cells;

        public GridSystem(int width, int height, float cellSizeWorld, Vector3 origin = default)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            CellSizeWorld = Mathf.Max(0.01f, cellSizeWorld);
            Origin = origin;
            _cells = new CellData[Width, Height];
            for (int x = 0; x < Width; x++)
                for (int z = 0; z < Height; z++)
                    _cells[x, z] = CellData.Default();
        }

        public ref CellData GetCell(int cx, int cz)
        {
            cx = Mathf.Clamp(cx, 0, Width - 1);
            cz = Mathf.Clamp(cz, 0, Height - 1);
            return ref _cells[cx, cz];
        }

        public ref CellData GetCell(Vector2Int c)
        {
            return ref GetCell(c.x, c.y);
        }

        public bool InBoundsCell(int cx, int cz)
        {
            return cx >= 0 && cx < Width && cz >= 0 && cz < Height;
        }

        public bool InBoundsCell(Vector2Int c)
        {
            return InBoundsCell(c.x, c.y);
        }

        /// <summary>Convierte posición mundo a celda (floor).</summary>
        public Vector2Int WorldToCell(Vector3 world)
        {
            int cx = Mathf.FloorToInt((world.x - Origin.x) / CellSizeWorld);
            int cz = Mathf.FloorToInt((world.z - Origin.z) / CellSizeWorld);
            return new Vector2Int(cx, cz);
        }

        /// <summary>Centro en mundo de la celda (cx, cz).</summary>
        public Vector3 CellToWorldCenter(int cx, int cz)
        {
            float x = Origin.x + (cx + 0.5f) * CellSizeWorld;
            float z = Origin.z + (cz + 0.5f) * CellSizeWorld;
            return new Vector3(x, Origin.y, z);
        }

        public Vector3 CellToWorldCenter(Vector2Int c)
        {
            return CellToWorldCenter(c.x, c.y);
        }

        /// <summary>Nodo (esquina) más cercano en grid de nodos (width+1) x (height+1).</summary>
        public Vector2Int WorldToNode(Vector3 world)
        {
            int nx = Mathf.RoundToInt((world.x - Origin.x) / CellSizeWorld);
            int nz = Mathf.RoundToInt((world.z - Origin.z) / CellSizeWorld);
            nx = Mathf.Clamp(nx, 0, Width);
            nz = Mathf.Clamp(nz, 0, Height);
            return new Vector2Int(nx, nz);
        }

        public Vector3 NodeToWorld(int nx, int nz)
        {
            float x = Origin.x + nx * CellSizeWorld;
            float z = Origin.z + nz * CellSizeWorld;
            return new Vector3(x, Origin.y, z);
        }

        /// <summary>Vecinos 4 (N-S-E-O) en celdas. Solo celdas dentro de bounds.</summary>
        public IEnumerable<Vector2Int> Neighbors4(int cx, int cz)
        {
            if (InBoundsCell(cx - 1, cz)) yield return new Vector2Int(cx - 1, cz);
            if (InBoundsCell(cx + 1, cz)) yield return new Vector2Int(cx + 1, cz);
            if (InBoundsCell(cx, cz - 1)) yield return new Vector2Int(cx, cz - 1);
            if (InBoundsCell(cx, cz + 1)) yield return new Vector2Int(cx, cz + 1);
        }

        public IEnumerable<Vector2Int> Neighbors4(Vector2Int c)
        {
            return Neighbors4(c.x, c.y);
        }

        /// <summary>Vecinos 8 (incluye diagonales). Solo celdas dentro de bounds.</summary>
        public IEnumerable<Vector2Int> Neighbors8(int cx, int cz)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if ((dx != 0 || dz != 0) && InBoundsCell(cx + dx, cz + dz))
                        yield return new Vector2Int(cx + dx, cz + dz);
        }

        public IEnumerable<Vector2Int> Neighbors8(Vector2Int c)
        {
            return Neighbors8(c.x, c.y);
        }
    }

    /// <summary>Nodo urbano: centro en celda y radio para área buildable.</summary>
    [Serializable]
    public class CityNode
    {
        public int Id;
        public Vector2Int Center;
        public int RadiusCells;
    }

    /// <summary>Camino entre dos ciudades: path en celdas.</summary>
    [Serializable]
    public class Road
    {
        public int FromCityId;
        public int ToCityId;
        public List<Vector2Int> PathCells = new List<Vector2Int>();
    }
}
