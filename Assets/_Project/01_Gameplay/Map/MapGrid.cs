using UnityEngine;

namespace Project.Gameplay.Map
{
    public class MapGrid : MonoBehaviour
    {
        public static MapGrid Instance { get; private set; }

        [Header("Runtime")]
        public int width;
        public int height;
        public float cellSize = 1f;
        public Vector3 origin;

        bool[] _blocked;
        bool[] _occupied;
        bool[] _water;
        float[] _terrainCosts;
        /// <summary>Celdas bajo una puerta abierta: transitables para A* aunque el muro marque ocupación.</summary>
        bool[] _openGatePassable;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("MapGrid: ya existe una instancia. Usando la primera.");
                return;
            }
            Instance = this;
        }

        public void Initialize(int w, int h, float size, Vector3 originWorld)
        {
            width = Mathf.Max(1, w);
            height = Mathf.Max(1, h);
            cellSize = Mathf.Max(0.01f, size);
            origin = originWorld;

            _blocked = new bool[width * height];
            _occupied = new bool[width * height];
            _water = new bool[width * height];
            _terrainCosts = new float[width * height];
            for (int i = 0; i < _terrainCosts.Length; i++)
                _terrainCosts[i] = 1f;
            _openGatePassable = new bool[width * height];
        }

        /// <summary>Costo de movimiento en la celda (1 = normal). Agua/blocked no se usan como transitables.</summary>
        public float GetTerrainCost(Vector2Int c)
        {
            if (!IsInBounds(c)) return float.MaxValue;
            return _terrainCosts[Index(c)];
        }

        /// <summary>Asigna costo de terreno (ej. 0.5 camino, 1.5 bosque).</summary>
        public void SetTerrainCost(Vector2Int c, float cost)
        {
            if (!IsInBounds(c)) return;
            _terrainCosts[Index(c)] = Mathf.Max(0.01f, cost);
        }

        public bool IsReady => _blocked != null && _occupied != null;

        public bool IsWater(Vector2Int c)
        {
            if (!IsInBounds(c)) return false;
            return _water[Index(c)];
        }

        public void SetWater(Vector2Int c, bool value)
        {
            if (!IsInBounds(c)) return;
            _water[Index(c)] = value;
        }

        public bool IsInBounds(Vector2Int c)
        {
            return c.x >= 0 && c.x < width && c.y >= 0 && c.y < height;
        }

        public Vector2Int WorldToCell(Vector3 world)
        {
            int x = Mathf.FloorToInt((world.x - origin.x) / cellSize);
            int y = Mathf.FloorToInt((world.z - origin.z) / cellSize);
            return new Vector2Int(x, y);
        }

        public Vector3 CellToWorld(Vector2Int cell)
        {
            return origin + new Vector3((cell.x + 0.5f) * cellSize, 0f, (cell.y + 0.5f) * cellSize);
        }

        /// <summary>Altura del terreno en el centro de la celda (requiere Terrain en escena).</summary>
        public static float GetCellHeight(Terrain terrain, Vector2Int cell)
        {
            if (terrain == null || Instance == null || !Instance.IsReady) return 0f;
            Vector3 w = Instance.CellToWorld(cell);
            return terrain.SampleHeight(new Vector3(w.x, 0f, w.z)) + terrain.transform.position.y;
        }

        /// <summary>Altura promedio del terreno en un área rectangular (centro + 4 esquinas).</summary>
        public static float GetAreaAverageHeight(Terrain terrain, Vector3 centerWorld, Vector2 sizeInCells)
        {
            if (terrain == null || Instance == null || !Instance.IsReady) return centerWorld.y;
            int w = Mathf.Max(1, Mathf.RoundToInt(sizeInCells.x));
            int h = Mathf.Max(1, Mathf.RoundToInt(sizeInCells.y));
            Vector2Int c = Instance.WorldToCell(centerWorld);
            float sum = 0f;
            int n = 0;
            for (int dx = 0; dx < w; dx++)
                for (int dy = 0; dy < h; dy++)
                {
                    var cell = new Vector2Int(c.x - w / 2 + dx, c.y - h / 2 + dy);
                    if (Instance.IsInBounds(cell)) { sum += GetCellHeight(terrain, cell); n++; }
                }
            return n > 0 ? sum / n : centerWorld.y;
        }

        /// <summary>Min y max altura del terreno en el área.</summary>
        public static void GetAreaMinMaxHeight(Terrain terrain, Vector3 centerWorld, Vector2 sizeInCells, out float min, out float max)
        {
            min = max = centerWorld.y;
            if (terrain == null || Instance == null || !Instance.IsReady) return;
            int w = Mathf.Max(1, Mathf.RoundToInt(sizeInCells.x));
            int h = Mathf.Max(1, Mathf.RoundToInt(sizeInCells.y));
            Vector2Int c = Instance.WorldToCell(centerWorld);
            min = float.MaxValue;
            max = float.MinValue;
            for (int dx = 0; dx < w; dx++)
                for (int dy = 0; dy < h; dy++)
                {
                    var cell = new Vector2Int(c.x - w / 2 + dx, c.y - h / 2 + dy);
                    if (!Instance.IsInBounds(cell)) continue;
                    float y = GetCellHeight(terrain, cell);
                    if (y < min) min = y;
                    if (y > max) max = y;
                }
            if (min == float.MaxValue) min = max = centerWorld.y;
        }

        public bool IsCellBlocked(Vector2Int c)
        {
            if (!IsInBounds(c)) return true;
            return _blocked[Index(c)];
        }

        public bool IsCellOccupied(Vector2Int c)
        {
            if (!IsInBounds(c)) return true;
            return _occupied[Index(c)];
        }

        public bool IsCellFree(Vector2Int c)
        {
            if (!IsInBounds(c)) return false;
            if (_blocked[Index(c)]) return false;
            if (_openGatePassable != null && _openGatePassable[Index(c)])
                return true;
            return !_occupied[Index(c)];
        }

        /// <summary>Marca celdas como paso por puerta abierta (A* puede atravesar muro en esas celdas).</summary>
        public void SetOpenGatePassable(Vector2Int c, bool value)
        {
            if (!IsReady || _openGatePassable == null || !IsInBounds(c)) return;
            _openGatePassable[Index(c)] = value;
        }

        /// <summary>True si esta celda es pasillo de puerta abierta (para coste en pathfinding).</summary>
        public bool IsOpenGatePassableCell(Vector2Int c)
        {
            if (!IsReady || _openGatePassable == null || !IsInBounds(c)) return false;
            return _openGatePassable[Index(c)];
        }

        public void SetBlocked(Vector2Int c, bool value)
        {
            if (!IsInBounds(c)) return;
            _blocked[Index(c)] = value;
        }

        public void SetOccupied(Vector2Int c, bool value)
        {
            if (!IsInBounds(c)) return;
            _occupied[Index(c)] = value;
        }

        public bool IsAreaFreeRect(Vector2Int min, Vector2Int size, bool requirePassable = true)
        {
            for (int x = 0; x < size.x; x++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    var c = new Vector2Int(min.x + x, min.y + y);
                    if (!IsInBounds(c)) return false;
                    int idx = Index(c);
                    if (_occupied[idx]) return false;
                    if (requirePassable && _blocked[idx]) return false;
                }
            }
            return true;
        }

        /// <param name="sizeInCells">Tamaño del área en celdas (ej. 3x3).</param>
        public bool IsWorldAreaFree(Vector3 centerWorld, Vector2 sizeInCells, bool requirePassable = true)
        {
            Vector2Int center = WorldToCell(centerWorld);
            Vector2Int size = new Vector2Int(Mathf.RoundToInt(sizeInCells.x), Mathf.RoundToInt(sizeInCells.y));
            Vector2Int min = new Vector2Int(center.x - size.x / 2, center.y - size.y / 2);
            return IsAreaFreeRect(min, size, requirePassable);
        }

        public void SetOccupiedRect(Vector2Int min, Vector2Int size, bool value)
        {
            for (int x = 0; x < size.x; x++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    var c = new Vector2Int(min.x + x, min.y + y);
                    if (!IsInBounds(c)) continue;
                    _occupied[Index(c)] = value;
                }
            }
        }

        public void SetOccupiedCircle(Vector2 centerWorld, float radius, bool value)
        {
            int minX = Mathf.FloorToInt((centerWorld.x - radius - origin.x) / cellSize);
            int maxX = Mathf.FloorToInt((centerWorld.x + radius - origin.x) / cellSize);
            int minY = Mathf.FloorToInt((centerWorld.y - radius - origin.z) / cellSize);
            int maxY = Mathf.FloorToInt((centerWorld.y + radius - origin.z) / cellSize);

            float r2 = radius * radius;
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (!IsInBounds(c)) continue;
                    Vector3 w = CellToWorld(c);
                    Vector2 w2 = new Vector2(w.x, w.z);
                    if ((w2 - centerWorld).sqrMagnitude <= r2)
                        _occupied[Index(c)] = value;
                }
            }
        }

        int Index(Vector2Int c) => c.y * width + c.x;
    }
}

