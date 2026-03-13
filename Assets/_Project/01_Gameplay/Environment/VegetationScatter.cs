using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Map;

namespace Project.Gameplay.Environment
{
    /// <summary>
    /// Procedural vegetation: spawns grass, rocks and bushes with random scale/rotation.
    /// Avoids cells occupied by buildings. Run after map generation (e.g. on Start with short delay).
    /// </summary>
    public class VegetationScatter : MonoBehaviour
    {
        [Header("Bounds")]
        [Tooltip("Source of map size. If null, uses MapGrid or Terrain.")]
        public RTSMapGenerator mapGenerator;
        [Tooltip("Shrink bounds by this margin (world units) to avoid edges.")]
        public float edgeMargin = 2f;

        [Header("Prefabs (optional)")]
        public GameObject grassPrefab;
        public GameObject rockPrefab;
        public GameObject bushPrefab;
        [Tooltip("Optional flowers for variation.")]
        public GameObject flowerPrefab;

        [Header("Density")]
        [Tooltip("Approx. spacing between spawn points (world units). Smaller = denser.")]
        public float spacing = 1.5f;
        [Tooltip("Chance 0-1: grass / rock / bush / flower ratio. Weights are normalized.")]
        [Range(0, 1)] public float grassWeight = 0.5f;
        [Range(0, 1)] public float rockWeight = 0.2f;
        [Range(0, 1)] public float bushWeight = 0.25f;
        [Range(0, 1)] public float flowerWeight = 0.05f;

        [Header("Random")]
        public Vector2 scaleMin = new Vector2(0.7f, 0.8f);
        public Vector2 scaleMax = new Vector2(1.3f, 1.2f);
        [Tooltip("Random Y rotation (degrees).")]
        public float rotationRange = 360f;
        [Tooltip("Seed for reproducible placement. 0 = random.")]
        public int seed;

        [Header("Buildings")]
        [Tooltip("Extra cells to skip around occupied cells.")]
        public int buildingPadding = 1;

        Transform _root;
        List<GameObject> _spawned = new List<GameObject>();

        void Start()
        {
            if (seed == 0) seed = (int)(Time.realtimeSinceStartup * 1000f) ^ transform.GetInstanceID();
            Invoke(nameof(Scatter), 0.5f);
        }

        void Scatter()
        {
            var grid = MapGrid.Instance;
            if (grid == null || !grid.IsReady)
            {
                Invoke(nameof(Scatter), 0.5f);
                return;
            }

            Terrain terrain = Terrain.activeTerrain != null ? Terrain.activeTerrain : FindFirstObjectByType<Terrain>();
            Vector3 origin = grid.origin;
            float cs = grid.cellSize;
            int w = grid.width;
            int h = grid.height;
            float worldW = w * cs;
            float worldH = h * cs;
            float margin = Mathf.Max(0f, edgeMargin);

            if (_root != null)
            {
                foreach (var go in _spawned)
                    if (go != null) Destroy(go);
                _spawned.Clear();
                if (Application.isPlaying) Destroy(_root.gameObject);
                else DestroyImmediate(_root.gameObject);
            }

            _root = new GameObject("VegetationScatter").transform;
            _root.SetParent(transform);
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;

            float x0 = origin.x + margin;
            float z0 = origin.z + margin;
            float x1 = origin.x + worldW - margin;
            float z1 = origin.z + worldH - margin;
            Random.InitState(seed);

            float totalWeight = grassWeight + rockWeight + bushWeight + flowerWeight;
            if (totalWeight < 0.001f) totalWeight = 1f;
            float g = grassWeight / totalWeight;
            float r = rockWeight / totalWeight;
            float b = bushWeight / totalWeight;
            float f = flowerWeight / totalWeight;

            int count = 0;
            for (float x = x0; x < x1; x += spacing)
            {
                for (float z = z0; z < z1; z += spacing)
                {
                    if (!IsFreeWithPadding(grid, x, z)) continue;

                    float y = 0f;
                    if (terrain != null)
                        y = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
                    Vector3 pos = new Vector3(x, y, z);

                    float t = Random.value;
                    GameObject prefab = null;
                    if (t < g && grassPrefab != null) prefab = grassPrefab;
                    else if (t < g + r && rockPrefab != null) prefab = rockPrefab;
                    else if (t < g + r + b && bushPrefab != null) prefab = bushPrefab;
                    else if (flowerPrefab != null) prefab = flowerPrefab;
                    if (prefab == null) prefab = grassPrefab ?? rockPrefab ?? bushPrefab ?? flowerPrefab;
                    if (prefab == null) continue;

                    GameObject instance = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, rotationRange), 0f), _root);
                    float sx = Random.Range(scaleMin.x, scaleMax.x);
                    float sy = Random.Range(scaleMin.y, scaleMax.y);
                    instance.transform.localScale = new Vector3(sx, sy, sx);
                    instance.isStatic = false;
                    _spawned.Add(instance);
                    count++;
                }
            }

            if (count > 0)
                Debug.Log($"VegetationScatter: {count} instances.");
        }

        bool IsFreeWithPadding(MapGrid grid, float worldX, float worldZ)
        {
            Vector2Int c = grid.WorldToCell(new Vector3(worldX, 0f, worldZ));
            for (int dx = -buildingPadding; dx <= buildingPadding; dx++)
                for (int dy = -buildingPadding; dy <= buildingPadding; dy++)
                {
                    if (grid.IsCellOccupied(new Vector2Int(c.x + dx, c.y + dy)))
                        return false;
                }
            return true;
        }

        void OnDestroy()
        {
            if (_root != null && _root.gameObject != null)
            {
                if (Application.isPlaying)
                    Destroy(_root.gameObject);
                else
                    DestroyImmediate(_root.gameObject);
            }
        }
    }
}
