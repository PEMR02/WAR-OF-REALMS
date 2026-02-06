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
            return !_blocked[Index(c)] && !_occupied[Index(c)];
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
