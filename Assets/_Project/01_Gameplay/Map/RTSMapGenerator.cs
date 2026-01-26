using System;
using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Buildings;
using Project.Gameplay.Resources;

namespace Project.Gameplay.Map
{
    public class RTSMapGenerator : MonoBehaviour
    {
        [Header("Grid")]
        public GridConfig gridConfig;
        public int width = 256;
        public int height = 256;
        public bool centerAtOrigin = true;

        [Header("Seed")]
        public int seed = 12345;
        public bool randomSeedOnPlay = true;

        [Header("Terrain (opcional)")]
        public Terrain terrain;
        public float waterHeight = -999f; // si no usas agua, deja negativo
        public float maxSlope = 15f;

        [Header("Players")]
        [Range(2, 4)] public int playerCount = 2;
        public float spawnEdgePadding = 20f;
        public float minPlayerDistance2p = 120f;
        public float minPlayerDistance4p = 100f;
        public float spawnFlatRadius = 8f;
        public float maxSlopeAtSpawn = 5f;
        public float waterExclusionRadius = 12f;

        [Header("Town Center")]
        public BuildingSO townCenterSO;
        public GameObject townCenterPrefabOverride;
        public float tcClearRadius = 6f;

        [Header("Resources Prefabs")]
        public GameObject treePrefab;
        public GameObject berryPrefab;
        public GameObject animalPrefab;
        public GameObject goldPrefab;
        public GameObject stonePrefab;

        [Header("Resource Rings")]
        public Vector2 ringNear = new Vector2(6f, 12f);
        public Vector2 ringMid = new Vector2(12f, 20f);
        public Vector2 ringFar = new Vector2(30f, 50f);

        [Header("Resource Counts")]
        public Vector2Int nearTrees = new Vector2Int(8, 12);
        public Vector2Int midTrees = new Vector2Int(12, 20);
        public Vector2Int berries = new Vector2Int(6, 8);
        public Vector2Int animals = new Vector2Int(2, 4);
        public Vector2Int goldSafe = new Vector2Int(6, 8);
        public Vector2Int stoneSafe = new Vector2Int(4, 6);
        public Vector2Int goldFar = new Vector2Int(8, 12);

        [Header("Fairness")]
        public int minWoodTrees = 40;
        public int minGoldNodes = 6;
        public int minStoneNodes = 4;
        public int minFoodValue = 8;
        public int maxResourceRetries = 5;

        [Header("Debug")]
        public bool drawGizmos = true;
        public Color spawnColor = Color.cyan;
        public Color ringNearColor = new Color(0f, 1f, 0f, 0.4f);
        public Color ringMidColor = new Color(1f, 1f, 0f, 0.4f);
        public Color ringFarColor = new Color(1f, 0.5f, 0f, 0.4f);

        MapGrid _grid;
        System.Random _rng;
        readonly List<Vector3> _spawns = new();

        void Start()
        {
            Generate();
        }

        public void Generate()
        {
            int actualSeed = randomSeedOnPlay ? UnityEngine.Random.Range(1, int.MaxValue) : seed;
            _rng = new System.Random(actualSeed);

            float cellSize = gridConfig != null ? gridConfig.gridSize : 1f;
            Vector3 origin = centerAtOrigin
                ? new Vector3(-width * cellSize * 0.5f, 0f, -height * cellSize * 0.5f)
                : transform.position;

            if (_grid == null)
                _grid = GetComponent<MapGrid>();
            if (_grid == null)
                _grid = gameObject.AddComponent<MapGrid>();

            _grid.Initialize(width, height, cellSize, origin);

            BakePassability();
            _spawns.Clear();

            GenerateSpawns();
            PlaceTownCenters();
            PlaceResourcesPerPlayer();
        }

        void BakePassability()
        {
            if (terrain == null || _grid == null || !_grid.IsReady) return;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Vector2Int c = new Vector2Int(x, y);
                    Vector3 w = _grid.CellToWorld(c);
                    float h = SampleHeight(w);
                    float slope = SampleSlope(w);

                    bool blocked = (waterHeight > -998f && h < waterHeight) || slope > maxSlope;
                    _grid.SetBlocked(c, blocked);
                }
            }
        }

        void GenerateSpawns()
        {
            float radius = Mathf.Min(width, height) * 0.5f - spawnEdgePadding;
            float minDistance = playerCount == 2 ? minPlayerDistance2p : minPlayerDistance4p;

            float baseAngle = (float)_rng.NextDouble() * 360f;
            float step = playerCount == 2 ? 180f : 90f;

            for (int i = 0; i < playerCount; i++)
            {
                bool placed = false;
                for (int attempt = 0; attempt < 30 && !placed; attempt++)
                {
                    float jitter = UnityEngine.Random.Range(-8f, 8f);
                    float angle = baseAngle + (step * i) + jitter;
                    Vector3 candidate = PolarToWorld(radius, angle);

                    if (!IsSpawnValid(candidate, minDistance))
                        continue;

                    _spawns.Add(candidate);
                    placed = true;
                }
            }
        }

        bool IsSpawnValid(Vector3 pos, float minDistance)
        {
            for (int i = 0; i < _spawns.Count; i++)
            {
                if (Vector3.Distance(_spawns[i], pos) < minDistance)
                    return false;
            }

            if (!IsAreaFlat(pos, spawnFlatRadius, maxSlopeAtSpawn))
                return false;

            if (waterHeight > -998f && pos.y < waterHeight + 0.1f)
                return false;

            return true;
        }

        void PlaceTownCenters()
        {
            if (_grid == null || !_grid.IsReady) return;
            if (townCenterSO == null && townCenterPrefabOverride == null) return;

            GameObject prefab = townCenterPrefabOverride != null ? townCenterPrefabOverride : townCenterSO.prefab;
            Vector2 size = townCenterSO != null ? townCenterSO.size : new Vector2(4, 4);
            Vector2Int footprint = new Vector2Int(Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y));

            for (int i = 0; i < _spawns.Count; i++)
            {
                Vector3 center = SnapToGrid(_spawns[i]);
                Vector2Int cell = _grid.WorldToCell(center);
                Vector2Int min = new Vector2Int(cell.x - footprint.x / 2, cell.y - footprint.y / 2);

                if (_grid.IsAreaFreeRect(min, footprint))
                {
                    Vector3 world = _grid.CellToWorld(cell);
                    world.y = SampleHeight(world);
                    Instantiate(prefab, world, Quaternion.identity);
                    _grid.SetOccupiedRect(min, footprint, true);

                    // Limpiar alrededor del TC
                    _grid.SetOccupiedCircle(new Vector2(world.x, world.z), tcClearRadius, true);
                }
            }
        }

        void PlaceResourcesPerPlayer()
        {
            for (int i = 0; i < _spawns.Count; i++)
            {
                int attempts = 0;
                while (attempts < maxResourceRetries)
                {
                    attempts++;
                    PlayerResourcesStats stats = new PlayerResourcesStats();
                    List<GameObject> spawned = new();
                    List<Vector2Int> occupiedCells = new();

                    bool ok = PlaceResourcesForSpawn(_spawns[i], stats, spawned, occupiedCells);
                    bool fair = stats.WoodTrees >= minWoodTrees &&
                                stats.GoldNodes >= minGoldNodes &&
                                stats.StoneNodes >= minStoneNodes &&
                                stats.FoodValue >= minFoodValue;

                    if (ok && fair) break;

                    // rollback
                    for (int s = 0; s < spawned.Count; s++)
                        if (spawned[s] != null) Destroy(spawned[s]);
                    for (int c = 0; c < occupiedCells.Count; c++)
                        _grid.SetOccupied(occupiedCells[c], false);
                }
            }
        }

        bool PlaceResourcesForSpawn(Vector3 spawn, PlayerResourcesStats stats, List<GameObject> spawned, List<Vector2Int> occupied)
        {
            // Anillo cercano
            PlaceCluster(spawn, ringNear, berries, berryPrefab, stats, spawned, occupied, ResourceKind.Food);
            PlaceScatter(spawn, ringNear, animals, animalPrefab, stats, spawned, occupied, ResourceKind.Food);
            PlaceCluster(spawn, ringNear, nearTrees, treePrefab, stats, spawned, occupied, ResourceKind.Wood);

            // Anillo medio
            PlaceCluster(spawn, ringMid, midTrees, treePrefab, stats, spawned, occupied, ResourceKind.Wood);
            PlaceCluster(spawn, ringMid, goldSafe, goldPrefab, stats, spawned, occupied, ResourceKind.Gold);
            PlaceCluster(spawn, ringMid, stoneSafe, stonePrefab, stats, spawned, occupied, ResourceKind.Stone);

            // Anillo lejano
            PlaceCluster(spawn, ringFar, goldFar, goldPrefab, stats, spawned, occupied, ResourceKind.Gold);

            return true;
        }

        void PlaceCluster(Vector3 spawn, Vector2 ring, Vector2Int countRange, GameObject prefab,
            PlayerResourcesStats stats, List<GameObject> spawned, List<Vector2Int> occupied, ResourceKind kind)
        {
            if (prefab == null) return;

            int count = UnityEngine.Random.Range(countRange.x, countRange.y + 1);
            Vector3 clusterCenter;
            if (!TryFindPointInRing(spawn, ring, out clusterCenter)) return;

            for (int i = 0; i < count; i++)
            {
                Vector3 p = clusterCenter + UnityEngine.Random.insideUnitSphere * 2.5f;
                p.y = 0f;
                TryPlaceSingle(p, prefab, stats, spawned, occupied, kind);
            }
        }

        void PlaceScatter(Vector3 spawn, Vector2 ring, Vector2Int countRange, GameObject prefab,
            PlayerResourcesStats stats, List<GameObject> spawned, List<Vector2Int> occupied, ResourceKind kind)
        {
            if (prefab == null) return;

            int count = UnityEngine.Random.Range(countRange.x, countRange.y + 1);
            for (int i = 0; i < count; i++)
            {
                Vector3 p;
                if (!TryFindPointInRing(spawn, ring, out p)) continue;
                TryPlaceSingle(p, prefab, stats, spawned, occupied, kind);
            }
        }

        bool TryPlaceSingle(Vector3 world, GameObject prefab, PlayerResourcesStats stats,
            List<GameObject> spawned, List<Vector2Int> occupied, ResourceKind kind)
        {
            Vector3 snapped = SnapToGrid(world);
            Vector2Int cell = _grid.WorldToCell(snapped);
            if (!_grid.IsCellFree(cell)) return false;

            Vector3 w = _grid.CellToWorld(cell);
            w.y = SampleHeight(w);

                    GameObject go = Instantiate(prefab, w, Quaternion.identity);
                    if (!go.activeSelf)
                        go.SetActive(true);
            spawned.Add(go);
            occupied.Add(cell);
            _grid.SetOccupied(cell, true);

            stats.Add(kind, 1);
            return true;
        }

        bool TryFindPointInRing(Vector3 center, Vector2 ring, out Vector3 result)
        {
            for (int i = 0; i < 40; i++)
            {
                float t = (float)_rng.NextDouble();
                float dist = Mathf.Lerp(ring.x, ring.y, t);
                float angle = (float)_rng.NextDouble() * 360f;
                Vector3 p = center + new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad)) * dist;
                p = SnapToGrid(p);
                Vector2Int cell = _grid.WorldToCell(p);
                if (_grid.IsCellFree(cell))
                {
                    result = p;
                    return true;
                }
            }
            result = center;
            return false;
        }

        Vector3 PolarToWorld(float radius, float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;
            p = SnapToGrid(p);
            p.y = SampleHeight(p);
            return p;
        }

        Vector3 SnapToGrid(Vector3 world)
        {
            float size = _grid != null ? _grid.cellSize : 1f;
            world.x = Mathf.Round(world.x / size) * size;
            world.z = Mathf.Round(world.z / size) * size;
            return world;
        }

        bool IsAreaFlat(Vector3 center, float radius, float maxSlopeAllowed)
        {
            if (terrain == null) return true;

            const int samples = 8;
            for (int i = 0; i < samples; i++)
            {
                float angle = (360f / samples) * i;
                Vector3 p = center + new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
                float slope = SampleSlope(p);
                if (slope > maxSlopeAllowed) return false;
            }
            return true;
        }

        float SampleHeight(Vector3 world)
        {
            if (terrain == null) return world.y;
            return terrain.SampleHeight(world) + terrain.transform.position.y;
        }

        float SampleSlope(Vector3 world)
        {
            if (terrain == null) return 0f;
            var data = terrain.terrainData;
            Vector3 local = world - terrain.transform.position;
            float nx = Mathf.Clamp01(local.x / data.size.x);
            float nz = Mathf.Clamp01(local.z / data.size.z);
            Vector3 normal = data.GetInterpolatedNormal(nx, nz);
            return Vector3.Angle(normal, Vector3.up);
        }

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            if (_spawns == null || _spawns.Count == 0) return;

            for (int i = 0; i < _spawns.Count; i++)
            {
                Gizmos.color = spawnColor;
                Gizmos.DrawSphere(_spawns[i], 1.2f);

                Gizmos.color = ringNearColor;
                Gizmos.DrawWireSphere(_spawns[i], ringNear.x);
                Gizmos.DrawWireSphere(_spawns[i], ringNear.y);

                Gizmos.color = ringMidColor;
                Gizmos.DrawWireSphere(_spawns[i], ringMid.x);
                Gizmos.DrawWireSphere(_spawns[i], ringMid.y);

                Gizmos.color = ringFarColor;
                Gizmos.DrawWireSphere(_spawns[i], ringFar.x);
                Gizmos.DrawWireSphere(_spawns[i], ringFar.y);
            }
        }

        class PlayerResourcesStats
        {
            public int WoodTrees;
            public int GoldNodes;
            public int StoneNodes;
            public int FoodValue;

            public void Add(ResourceKind kind, int amount)
            {
                switch (kind)
                {
                    case ResourceKind.Wood:
                        WoodTrees += amount;
                        break;
                    case ResourceKind.Gold:
                        GoldNodes += amount;
                        break;
                    case ResourceKind.Stone:
                        StoneNodes += amount;
                        break;
                    case ResourceKind.Food:
                        FoodValue += amount;
                        break;
                }
            }
        }
    }
}
