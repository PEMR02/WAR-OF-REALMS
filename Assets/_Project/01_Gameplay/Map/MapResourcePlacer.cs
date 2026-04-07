using System;
using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Resources;
using Project.Gameplay.Map.Generator;
using Project.Gameplay.Map.Generation;

namespace Project.Gameplay.Map
{
    /// <summary>
    /// Coloca recursos (árboles, piedra, oro, comida) en el mapa.
    /// Separado de RTSMapGenerator para mantener terreno y recursos en módulos distintos.
    /// </summary>
    public static class MapResourcePlacer
    {
        /// <summary>Coloca prefabs de recursos según CellData.resourceType del Generador Definitivo. Usar cuando useDefinitiveGenerator = true.</summary>
        /// <param name="townCenterWorldPositionsExclude">Opcional: no colocar recursos en XZ dentro de <paramref name="minDistanceFromTownCenters"/> de estos puntos (evita árboles dentro del centro urbano).</param>
        public static void PlaceFromDefinitiveGrid(GridSystem definitiveGrid, RTSMapGenerator gen, ResourceRuntimeSettings resources, IList<Vector3> townCenterWorldPositionsExclude = null, float minDistanceFromTownCenters = 0f)
        {
            if (definitiveGrid == null || gen == null) return;
            ResourceRuntimeSettings res = resources ?? ResourceRuntimeSettings.FromLegacySceneGenerator(gen);
            if (resources == null)
                Debug.LogWarning("[MapGen] PlaceFromDefinitiveGrid: resources null — fallback legacy desde RTSMapGenerator (deprecated).");
            var mapGrid = gen.GetGrid();
            if (mapGrid == null || !mapGrid.IsReady) return;

            int wood = 0, stone = 0, gold = 0, food = 0;
            for (int x = 0; x < definitiveGrid.Width; x++)
            {
                for (int z = 0; z < definitiveGrid.Height; z++)
                {
                    ref var cell = ref definitiveGrid.GetCell(x, z);
                    if (cell.resourceType == ResourceType.None) continue;
                    if (cell.resourceType == ResourceType.Wood &&
                        (cell.type == CellType.Water || cell.type == CellType.River))
                        continue;
                    var placeCell = new Vector2Int(x, z);
                    if (!mapGrid.IsCellFree(placeCell)) continue;
                    if (cell.resourceType == ResourceType.Wood && mapGrid.IsWater(placeCell)) continue;

                    Vector3 world = definitiveGrid.CellToWorldCenter(x, z);
                    world.y = gen.terrain != null ? gen.SampleHeight(world) : world.y;
                    if (minDistanceFromTownCenters > 0.01f && IsWithinMinDistanceXZ(world, townCenterWorldPositionsExclude, minDistanceFromTownCenters))
                        continue;
                    GameObject prefab = GetPrefabForResourceType(cell.resourceType, gen, res);
                    if (prefab == null) continue;
                    ResourceKind kind = ToResourceKind(cell.resourceType);
                    Quaternion rot = GetPlacementRotation(kind, prefab, gen, res);
                    if (kind != ResourceKind.Food && res.randomRotationPerResource && gen.GetRng() != null)
                        rot = rot * Quaternion.Euler(0f, (float)(gen.GetRng().NextDouble() * 360.0), 0f);
                    GameObject go = UnityEngine.Object.Instantiate(prefab, world, rot);
                    NavMeshSpawnSafety.DisableNavMeshAgentsOnHierarchy(go);
                    if (!go.activeSelf) go.SetActive(true);
                    SnapResourceBottomToTerrain(go, gen);
                    EnsureResourceCollectable(go, kind, gen, res);
                    go.name = $"{cell.resourceType}_{x}_{z}";
                    mapGrid.SetOccupied(placeCell, true);
                    switch (cell.resourceType) { case ResourceType.Wood: wood++; break; case ResourceType.Stone: stone++; break; case ResourceType.Gold: gold++; break; case ResourceType.Food: food++; break; }
                }
            }
            if (gen != null && gen.debugLogs)
                Debug.Log($"PlaceFromDefinitiveGrid: Wood={wood}, Stone={stone}, Gold={gold}, Food={food}");
        }

        /// <summary>
        /// Coloca SOLO recursos globales (fuera de los Town Centers) usando los rangos globalTrees/globalStone/globalGold del RTSMapGenerator.
        /// Útil cuando el mapa es definitivo pero quieres mantener dispersión global "legacy" (sin duplicar recursos de spawn).
        /// </summary>
        public static void PlaceGlobalOnly(IList<Vector3> spawns, RTSMapGenerator gen, ResourceRuntimeSettings resources)
        {
            if (gen == null) return;
            ResourceRuntimeSettings res = resources ?? ResourceRuntimeSettings.FromLegacySceneGenerator(gen);
            if (resources == null)
                Debug.LogWarning("[MapGen] PlaceGlobalOnly: resources null — fallback legacy (deprecated).");
            var grid = gen.GetGrid();
            if (grid == null || !grid.IsReady) return;
            if (spawns == null) spawns = Array.Empty<Vector3>();
            PlaceGlobalResources(spawns, gen, res);
        }

        static bool IsWithinMinDistanceXZ(Vector3 world, IList<Vector3> centers, float minDist)
        {
            if (centers == null || centers.Count == 0 || minDist <= 0f) return false;
            float d2 = minDist * minDist;
            for (int i = 0; i < centers.Count; i++)
            {
                float dx = world.x - centers[i].x;
                float dz = world.z - centers[i].z;
                if (dx * dx + dz * dz < d2) return true;
            }
            return false;
        }

        static GameObject GetPrefabForResourceType(ResourceType type, RTSMapGenerator gen, ResourceRuntimeSettings s)
        {
            switch (type)
            {
                case ResourceType.Wood: return GetPrefabForPlacement(s.treePrefab, s.treePrefabVariants, gen);
                case ResourceType.Stone: return GetPrefabForPlacement(s.stonePrefab, s.stonePrefabVariants, gen);
                case ResourceType.Gold: return GetPrefabForPlacement(s.goldPrefab, s.goldPrefabVariants, gen);
                case ResourceType.Food: return GetPrefabForFood(gen, s);
                default: return null;
            }
        }

        /// <summary>Para Food: si hay bayas y animales, sesgo ~65% a bayas (incl. variantes como Tree_06); si no, el que exista.</summary>
        static GameObject GetPrefabForFood(RTSMapGenerator gen, ResourceRuntimeSettings s)
        {
            bool hasBerry = s.berryPrefab != null || (s.berryPrefabVariants != null && s.berryPrefabVariants.Length > 0);
            bool hasAnimal = s.animalPrefab != null || (s.animalPrefabVariants != null && s.animalPrefabVariants.Length > 0);
            var rng = gen.GetRng();
            if (hasBerry && hasAnimal)
            {
                if (rng == null || rng.Next(100) < 65)
                    return GetPrefabForPlacement(s.berryPrefab, s.berryPrefabVariants, gen);
                return GetPrefabForPlacement(s.animalPrefab, s.animalPrefabVariants, gen);
            }
            if (hasAnimal) return GetPrefabForPlacement(s.animalPrefab, s.animalPrefabVariants, gen);
            return GetPrefabForPlacement(s.berryPrefab, s.berryPrefabVariants, gen);
        }

        static ResourceKind ToResourceKind(ResourceType type)
        {
            switch (type)
            {
                case ResourceType.Wood: return ResourceKind.Wood;
                case ResourceType.Stone: return ResourceKind.Stone;
                case ResourceType.Gold: return ResourceKind.Gold;
                case ResourceType.Food: return ResourceKind.Food;
                default: return ResourceKind.Wood;
            }
        }

        public static void Place(IList<Vector3> spawns, RTSMapGenerator gen, ResourceRuntimeSettings resources = null)
        {
            if (gen == null) return;
            ResourceRuntimeSettings res = resources ?? ResourceRuntimeSettings.FromLegacySceneGenerator(gen);
            var grid = gen.GetGrid();
            if (grid == null || !grid.IsReady) return;

            PlaceResourcesPerPlayer(spawns, gen, res);
            PlaceGlobalResources(spawns, gen, res);
        }

        static void PlaceResourcesPerPlayer(IList<Vector3> spawns, RTSMapGenerator gen, ResourceRuntimeSettings s)
        {
            var grid = gen.GetGrid();
            var rng = gen.GetRng();
            if (grid == null || rng == null) return;

            Debug.Log($"PlaceResourcesPerPlayer: {spawns.Count} jugadores");

            if (s.treePrefab == null)
                Debug.LogWarning("RTS Map Generator: treePrefab no asignado.");
            if (s.berryPrefab == null) Debug.LogWarning("RTS Map Generator: berryPrefab no asignado.");
            if (s.goldPrefab == null) Debug.LogWarning("RTS Map Generator: goldPrefab no asignado.");
            if (!HasAnyStonePrefab(s)) Debug.LogWarning("RTS Map Generator: stonePrefab (o stonePrefabVariants) no asignado.");
            if (s.animalPrefab == null) Debug.LogWarning("RTS Map Generator: animalPrefab no asignado.");

            for (int i = 0; i < spawns.Count; i++)
            {
                int attempts = 0;
                while (attempts < s.maxResourceRetries)
                {
                    attempts++;
                    var stats = new PlayerResourcesStats();
                    var spawned = new List<GameObject>();
                    var occupiedCells = new List<Vector2Int>();
                    bool ok = PlaceResourcesForSpawn(spawns[i], stats, spawned, occupiedCells, gen, s);
                    bool fair = stats.WoodTrees >= s.minWoodTrees &&
                        stats.GoldNodes >= s.minGoldNodes &&
                        stats.StoneNodes >= s.minStoneNodes &&
                        stats.FoodValue >= s.minFoodValue;

                    if (ok)
                    {
                        if (fair)
                            Debug.Log($"Player {i + 1} recursos: Wood={stats.WoodTrees}, Gold={stats.GoldNodes}, Stone={stats.StoneNodes}, Food={stats.FoodValue}, Total={spawned.Count}");
                        else
                            Debug.LogWarning($"Player {i + 1}: recursos colocados pero por debajo del mínimo deseado (Wood={stats.WoodTrees}/{s.minWoodTrees}).");
                        break;
                    }
                    for (int si = 0; si < spawned.Count; si++)
                        if (spawned[si] != null) UnityEngine.Object.Destroy(spawned[si]);
                    for (int c = 0; c < occupiedCells.Count; c++)
                        grid.SetOccupied(occupiedCells[c], false);
                    if (attempts == s.maxResourceRetries)
                        Debug.LogWarning($"Player {i + 1}: No se pudieron colocar recursos después de {s.maxResourceRetries} intentos.");
                }
            }
        }

        static void PlaceGlobalResources(IList<Vector3> spawns, RTSMapGenerator gen, ResourceRuntimeSettings s)
        {
            var grid = gen.GetGrid();
            var rng = gen.GetRng();
            if (grid == null || rng == null) return;
            if (!HasAnyTreePrefab(s) && !HasAnyGoldPrefab(s) && !HasAnyStonePrefab(s) && !HasAnyAnimalPrefab(s)) return;

            int treesTarget = rng.Next(s.globalTrees.x, s.globalTrees.y + 1);
            int stoneTarget = rng.Next(s.globalStone.x, s.globalStone.y + 1);
            int goldTarget = rng.Next(s.globalGold.x, s.globalGold.y + 1);
            int animalsTarget = rng.Next(s.globalAnimals.x, s.globalAnimals.y + 1);
            int treesPlaced = 0, stonePlaced = 0, goldPlaced = 0, animalsPlaced = 0;

            // Árboles: con clustering se agrupan en bosques densos + resto dispersos
            if (HasAnyTreePrefab(s) && treesTarget > 0)
            {
                if (s.forestClustering && s.clusterMaxSize > 0 && s.clusterMinSize < s.clusterMaxSize)
                {
                    treesPlaced = PlaceGlobalTreesWithClustering(spawns, treesTarget, gen, s);
                }
                else
                {
                    treesPlaced = PlaceGlobalTreesScattered(spawns, treesTarget, gen, s);
                }
            }

            // Piedra y oro: manchas (clusters); animales: dispersos
            if (HasAnyStonePrefab(s) && stoneTarget > 0)
                stonePlaced = PlaceGlobalMineralClustered(spawns, stoneTarget, gen, s, ResourceKind.Stone);
            if (HasAnyGoldPrefab(s) && goldTarget > 0)
                goldPlaced = PlaceGlobalMineralClustered(spawns, goldTarget, gen, s, ResourceKind.Gold);

            int maxAttempts = animalsTarget * 35;
            int attempts = 0;
            while (animalsPlaced < animalsTarget && attempts < maxAttempts)
            {
                attempts++;
                Vector2Int cell = new Vector2Int(rng.Next(0, grid.width), rng.Next(0, grid.height));
                if (!grid.IsCellFree(cell)) continue;
                Vector3 world = grid.CellToWorld(cell);
                if (IsWithinExcludeRadius(world, spawns, s.globalExcludeRadius)) continue;
                if (GetRandomAnimalPrefab(s, gen) != null && TryPlaceSingleGlobal(GetRandomAnimalPrefab(s, gen), cell, ResourceKind.Food, gen, s))
                    animalsPlaced++;
            }
            Debug.Log($"Recursos en el mapa: árboles={treesPlaced}, piedra={stonePlaced}, oro={goldPlaced}, animales={animalsPlaced}");
        }

        /// <summary>Piedra u oro global en vetas/filones compactos + remate disperso si hace falta.</summary>
        static int PlaceGlobalMineralClustered(IList<Vector3> spawns, int target, RTSMapGenerator gen, ResourceRuntimeSettings s, ResourceKind kind)
        {
            if (target <= 0 || gen == null) return 0;
            if (kind != ResourceKind.Stone && kind != ResourceKind.Gold) return 0;
            bool stone = kind == ResourceKind.Stone;
            if (stone && !HasAnyStonePrefab(s)) return 0;
            if (!stone && !HasAnyGoldPrefab(s)) return 0;

            var grid = gen.GetGrid();
            var rng = gen.GetRng();

            float frac = s.globalStoneGoldClusterFraction;
            if (frac < 0.4f || frac > 1f) frac = 0.82f;
            int clusterBudget = Mathf.RoundToInt(target * frac);
            int placed = 0;

            float radius = Mathf.Clamp(s.globalMineralClusterRadiusCells, 1.2f, 10f);
            int minK = Mathf.Max(2, s.globalMineralClusterSize.x);
            int maxK = Mathf.Max(minK, s.globalMineralClusterSize.y);
            int maxDeposits = Mathf.Clamp(Mathf.Max(6, target / 2), 4, 64);

            for (int d = 0; d < maxDeposits && clusterBudget > 0 && placed < target; d++)
            {
                int clusterSize = rng.Next(minK, maxK + 1);
                clusterSize = Mathf.Min(clusterSize, clusterBudget, target - placed);
                if (clusterSize < 1) break;

                if (!TryFindClusterCenterPass(spawns, gen, s, filterGrass: false, maxTries: 160, out Vector2Int centerCell))
                    break;

                int localPlaced = 0;
                int innerAttempts = 0;
                int innerMax = Mathf.Max(clusterSize * 24, 48);
                while (localPlaced < clusterSize && innerAttempts < innerMax && placed < target)
                {
                    innerAttempts++;
                    float angle = (float)(rng.NextDouble() * 2.0 * Math.PI);
                    float dist = (float)(rng.NextDouble() * radius);
                    int dx = Mathf.RoundToInt(Mathf.Cos(angle) * dist);
                    int dz = Mathf.RoundToInt(Mathf.Sin(angle) * dist);
                    Vector2Int cell = new Vector2Int(centerCell.x + dx, centerCell.y + dz);
                    if (cell.x < 0 || cell.x >= grid.width || cell.y < 0 || cell.y >= grid.height) continue;
                    if (!grid.IsCellFree(cell)) continue;
                    Vector3 w = grid.CellToWorld(cell);
                    if (IsWithinExcludeRadius(w, spawns, s.globalExcludeRadius)) continue;

                    GameObject prefab = stone ? GetRandomStonePrefab(s, gen) : GetRandomGoldPrefab(s, gen);
                    if (prefab != null && TryPlaceSingleGlobal(prefab, cell, kind, gen, s))
                    {
                        placed++;
                        localPlaced++;
                        clusterBudget--;
                    }
                }
            }

            int scatterNeed = target - placed;
            if (scatterNeed > 0)
                placed += PlaceGlobalMineralScattered(spawns, scatterNeed, gen, s, kind);
            return placed;
        }

        static int PlaceGlobalMineralScattered(IList<Vector3> spawns, int need, RTSMapGenerator gen, ResourceRuntimeSettings s, ResourceKind kind)
        {
            if (need <= 0) return 0;
            var grid = gen.GetGrid();
            var rng = gen.GetRng();
            int placed = 0;
            int attempts = 0;
            int maxAttempts = Mathf.Max(need * 40, 80);
            bool stone = kind == ResourceKind.Stone;
            while (placed < need && attempts < maxAttempts)
            {
                attempts++;
                Vector2Int cell = new Vector2Int(rng.Next(0, grid.width), rng.Next(0, grid.height));
                if (!grid.IsCellFree(cell)) continue;
                Vector3 world = grid.CellToWorld(cell);
                if (IsWithinExcludeRadius(world, spawns, s.globalExcludeRadius)) continue;
                GameObject prefab = stone ? GetRandomStonePrefab(s, gen) : GetRandomGoldPrefab(s, gen);
                if (prefab != null && TryPlaceSingleGlobal(prefab, cell, kind, gen, s))
                    placed++;
            }
            return placed;
        }

        /// <summary>Coloca árboles globales en bosques (clusters) densos y el resto dispersos.</summary>
        static int PlaceGlobalTreesWithClustering(IList<Vector3> spawns, int treesTarget, RTSMapGenerator gen, ResourceRuntimeSettings s)
        {
            var grid = gen.GetGrid();
            var rng = gen.GetRng();
            int placed = 0;
            float clusterFrac = s.globalTreesClusterFraction >= 0f && s.globalTreesClusterFraction <= 1f
                ? s.globalTreesClusterFraction
                : 0.75f;
            int inClusters = Mathf.RoundToInt(treesTarget * clusterFrac);

            // Radio del cluster en celdas: alta densidad = radio pequeño (árboles muy juntos), baja = radio mayor
            float clusterRadiusCells = Mathf.Lerp(6f, 2.5f, s.clusterDensity);
            int minSize = Mathf.Clamp(s.clusterMinSize, 2, 200);
            int maxSize = Mathf.Clamp(s.clusterMaxSize, minSize, 200);

            int clusterBudget = inClusters;
            int maxClusters = Mathf.Clamp(Mathf.Max(20, treesTarget / 30), 16, 56);
            for (int c = 0; c < maxClusters && clusterBudget > 0; c++)
            {
                int clusterSize = rng.Next(minSize, maxSize + 1);
                clusterSize = Mathf.Min(clusterSize, clusterBudget);
                if (clusterSize < 2) break;

                if (!TryFindClusterCenter(spawns, gen, s, out Vector2Int centerCell))
                    break;

                for (int t = 0; t < clusterSize; t++)
                {
                    float angle = (float)(rng.NextDouble() * 360.0 * Mathf.Deg2Rad);
                    float dist = (float)(rng.NextDouble() * clusterRadiusCells);
                    int dx = Mathf.RoundToInt(Mathf.Cos(angle) * dist);
                    int dz = Mathf.RoundToInt(Mathf.Sin(angle) * dist);
                    Vector2Int cell = new Vector2Int(centerCell.x + dx, centerCell.y + dz);
                    if (cell.x < 0 || cell.x >= grid.width || cell.y < 0 || cell.y >= grid.height) continue;
                    if (!grid.IsCellFree(cell)) continue;
                    if (grid.IsWater(cell)) continue;
                    Vector3 w = grid.CellToWorld(new Vector2Int(cell.x, cell.y));
                    if (IsWithinExcludeRadius(w, spawns, s.globalExcludeRadius)) continue;
                    if (!IsWorldOkForGlobalTree(gen, s, w)) continue;

                    GameObject prefab = GetRandomTreePrefab(s, gen);
                    if (prefab != null && TryPlaceSingleGlobal(prefab, cell, ResourceKind.Wood, gen, s))
                    {
                        placed++;
                        clusterBudget--;
                    }
                }
            }

            // Resto dispersos (hasta completar treesTarget)
            int remaining = treesTarget - placed;
            if (remaining > 0)
                placed += PlaceGlobalTreesScattered(spawns, remaining, gen, s);
            return placed;
        }

        static bool TryFindClusterCenter(IList<Vector3> spawns, RTSMapGenerator gen, ResourceRuntimeSettings s, out Vector2Int centerCell)
        {
            if (TryFindClusterCenterPass(spawns, gen, s, filterGrass: s.preferGlobalTreesOnGrassAlphamap,
                    maxTries: s.preferGlobalTreesOnGrassAlphamap ? 220 : 60, out centerCell))
                return true;
            if (s.preferGlobalTreesOnGrassAlphamap &&
                TryFindClusterCenterPass(spawns, gen, s, filterGrass: false, maxTries: 100, out centerCell))
                return true;
            centerCell = Vector2Int.zero;
            return false;
        }

        static bool TryFindClusterCenterPass(IList<Vector3> spawns, RTSMapGenerator gen, ResourceRuntimeSettings s, bool filterGrass, int maxTries, out Vector2Int centerCell)
        {
            var grid = gen.GetGrid();
            var rng = gen.GetRng();
            for (int i = 0; i < maxTries; i++)
            {
                Vector2Int cell = new Vector2Int(rng.Next(0, grid.width), rng.Next(0, grid.height));
                Vector3 world = grid.CellToWorld(cell);
                if (!grid.IsCellFree(cell)) continue;
                if (grid.IsWater(cell)) continue;
                if (IsWithinExcludeRadius(world, spawns, s.globalExcludeRadius)) continue;
                if (filterGrass && !IsWorldOkForGlobalTree(gen, s, world)) continue;
                centerCell = cell;
                return true;
            }
            centerCell = Vector2Int.zero;
            return false;
        }

        /// <summary>Coloca árboles globales uno a uno en celdas aleatorias (sin clustering).</summary>
        static int PlaceGlobalTreesScattered(IList<Vector3> spawns, int treesTarget, RTSMapGenerator gen, ResourceRuntimeSettings s)
        {
            int maxAttempts = treesTarget * (s.preferGlobalTreesOnGrassAlphamap ? 40 : 25);
            int placed = PlaceGlobalTreesScatteredInner(spawns, treesTarget, gen, s, maxAttempts, filterGrass: s.preferGlobalTreesOnGrassAlphamap);
            if (s.preferGlobalTreesOnGrassAlphamap && placed < treesTarget)
            {
                int left = treesTarget - placed;
                placed += PlaceGlobalTreesScatteredInner(spawns, left, gen, s, left * 35, filterGrass: false);
            }
            return placed;
        }

        static int PlaceGlobalTreesScatteredInner(IList<Vector3> spawns, int treesTarget, RTSMapGenerator gen, ResourceRuntimeSettings s, int maxAttempts, bool filterGrass)
        {
            var grid = gen.GetGrid();
            var rng = gen.GetRng();
            int treesPlaced = 0;
            int attempts = 0;
            while (treesPlaced < treesTarget && attempts < maxAttempts)
            {
                attempts++;
                Vector2Int cell = new Vector2Int(rng.Next(0, grid.width), rng.Next(0, grid.height));
                if (!grid.IsCellFree(cell)) continue;
                if (grid.IsWater(cell)) continue;
                Vector3 world = grid.CellToWorld(cell);
                if (IsWithinExcludeRadius(world, spawns, s.globalExcludeRadius)) continue;
                if (filterGrass && !IsWorldOkForGlobalTree(gen, s, world)) continue;

                if (GetRandomTreePrefab(s, gen) != null && TryPlaceSingleGlobal(GetRandomTreePrefab(s, gen), cell, ResourceKind.Wood, gen, s))
                    treesPlaced++;
            }
            return treesPlaced;
        }

        /// <summary>Capa [0] del Terrain = grass en RTSMapGenerator (orden grass, dirt, rock…).</summary>
        static bool IsWorldOkForGlobalTree(RTSMapGenerator gen, ResourceRuntimeSettings s, Vector3 worldXZ)
        {
            if (gen == null)
                return true;
            var mg = gen.GetGrid();
            if (mg != null && mg.IsReady)
            {
                Vector2Int c = mg.WorldToCell(gen.SnapToGrid(worldXZ));
                if (mg.IsWater(c)) return false;
            }
            if (!s.preferGlobalTreesOnGrassAlphamap)
                return true;
            Terrain t = gen.terrain;
            if (t == null || t.terrainData == null)
                return true;
            TerrainData td = t.terrainData;
            if (td.alphamapWidth < 1 || td.alphamapHeight < 1 || td.alphamapLayers < 1)
                return true;

            Vector3 local = worldXZ - t.transform.position;
            float nx = local.x / td.size.x;
            float nz = local.z / td.size.z;
            if (nx < 0f || nx > 1f || nz < 0f || nz > 1f)
                return false;
            int ax = Mathf.Clamp((int)(nx * td.alphamapWidth), 0, td.alphamapWidth - 1);
            int az = Mathf.Clamp((int)(nz * td.alphamapHeight), 0, td.alphamapHeight - 1);
            float[,,] mix = td.GetAlphamaps(ax, az, 1, 1);
            float grassW = mix[0, 0, 0];
            for (int i = 1; i < td.alphamapLayers; i++)
            {
                if (mix[0, 0, i] > grassW + 0.02f)
                    return false;
            }
            return grassW >= 0.22f;
        }

        static bool PlaceResourcesForSpawn(Vector3 spawn, PlayerResourcesStats stats, List<GameObject> spawned, List<Vector2Int> occupied, RTSMapGenerator gen, ResourceRuntimeSettings s)
        {
            PlaceCluster(spawn, s.ringNear, s.berries, s.berryPrefab, stats, spawned, occupied, ResourceKind.Food, gen, s.berryPrefabVariants, s);
            PlaceScatter(spawn, s.ringNear, s.animals, s.animalPrefab, stats, spawned, occupied, ResourceKind.Food, gen, s.animalPrefabVariants, s);
            PlaceCluster(spawn, s.ringNear, s.nearTrees, s.treePrefab, stats, spawned, occupied, ResourceKind.Wood, gen, s.treePrefabVariants, s);
            PlaceCluster(spawn, s.ringMid, s.midTrees, s.treePrefab, stats, spawned, occupied, ResourceKind.Wood, gen, s.treePrefabVariants, s);
            PlaceCluster(spawn, s.ringMid, s.goldSafe, s.goldPrefab, stats, spawned, occupied, ResourceKind.Gold, gen, s.goldPrefabVariants, s);
            PlaceCluster(spawn, s.ringMid, s.stoneSafe, s.stonePrefab, stats, spawned, occupied, ResourceKind.Stone, gen, s.stonePrefabVariants, s);
            PlaceCluster(spawn, s.ringFar, s.goldFar, s.goldPrefab, stats, spawned, occupied, ResourceKind.Gold, gen, s.goldPrefabVariants, s);
            return true;
        }

        static void PlaceCluster(Vector3 spawn, Vector2 ring, Vector2Int countRange, GameObject prefab,
            PlayerResourcesStats stats, List<GameObject> spawned, List<Vector2Int> occupied, ResourceKind kind,
            RTSMapGenerator generator, GameObject[] prefabVariants, ResourceRuntimeSettings res)
        {
            GameObject p = GetPrefabForPlacement(prefab, prefabVariants, generator);
            if (p == null) { Debug.LogWarning($"PlaceCluster: prefab null para {kind}"); return; }
            var rng = generator.GetRng();
            var grid = generator.GetGrid();
            int count = rng.Next(countRange.x, countRange.y + 1);
            if (!TryFindPointInRing(spawn, ring, generator, out Vector3 clusterCenter))
            { Debug.LogWarning($"PlaceCluster: No se encontró punto en anillo para {kind}"); return; }
            for (int i = 0; i < count; i++)
            {
                GameObject pPrefab = GetPrefabForPlacement(prefab, prefabVariants, generator);
                if (pPrefab == null) continue;
                float rx = (float)(rng.NextDouble() * 2.0 - 1.0) * 2.5f;
                float rz = (float)(rng.NextDouble() * 2.0 - 1.0) * 2.5f;
                Vector3 pos = clusterCenter + new Vector3(rx, 0f, rz);
                TryPlaceSingle(pos, pPrefab, stats, spawned, occupied, kind, generator, res);
            }
        }

        static void PlaceScatter(Vector3 spawn, Vector2 ring, Vector2Int countRange, GameObject prefab,
            PlayerResourcesStats stats, List<GameObject> spawned, List<Vector2Int> occupied, ResourceKind kind,
            RTSMapGenerator generator, GameObject[] prefabVariants, ResourceRuntimeSettings res)
        {
            if (GetPrefabForPlacement(prefab, prefabVariants, generator) == null)
            { Debug.LogWarning($"PlaceScatter: prefab null para {kind}"); return; }
            var rng = generator.GetRng();
            int count = rng.Next(countRange.x, countRange.y + 1);
            for (int i = 0; i < count; i++)
            {
                if (!TryFindPointInRing(spawn, ring, generator, out Vector3 p)) continue;
                GameObject pPrefab = GetPrefabForPlacement(prefab, prefabVariants, generator);
                if (pPrefab == null) continue;
                TryPlaceSingle(p, pPrefab, stats, spawned, occupied, kind, generator, res);
            }
        }

        static bool TryPlaceSingle(Vector3 world, GameObject prefab, PlayerResourcesStats stats,
            List<GameObject> spawned, List<Vector2Int> occupied, ResourceKind kind, RTSMapGenerator generator, ResourceRuntimeSettings res)
        {
            var grid = generator.GetGrid();
            Vector3 snapped = generator.SnapToGrid(world);
            Vector2Int cell = grid.WorldToCell(snapped);
            if (!grid.IsCellFree(cell)) return false;
            if (kind == ResourceKind.Wood && grid.IsWater(cell)) return false;
            Vector3 w = grid.CellToWorld(cell);
            ApplyRandomOffsetInCell(ref w, generator, res);
            w.y = generator.terrain != null ? generator.SampleHeight(w) : 0f;
            Quaternion rot = GetPlacementRotation(kind, prefab, generator, res);
            rot = ApplyRandomRotation(rot, generator, kind, res);
            GameObject go = UnityEngine.Object.Instantiate(prefab, w, rot);
            NavMeshSpawnSafety.DisableNavMeshAgentsOnHierarchy(go);
            if (!go.activeSelf) go.SetActive(true);
            SnapResourceBottomToTerrain(go, generator);
            EnsureResourceCollectable(go, kind, generator, res);
            go.name = $"{kind}_{spawned.Count}";
            spawned.Add(go);
            occupied.Add(cell);
            grid.SetOccupied(cell, true);
            stats.Add(kind, 1);
            return true;
        }

        static bool TryPlaceSingleGlobal(GameObject prefab, Vector2Int cell, ResourceKind kind, RTSMapGenerator generator, ResourceRuntimeSettings res)
        {
            var grid = generator.GetGrid();
            if (!grid.IsCellFree(cell)) return false;
            if (kind == ResourceKind.Wood && grid.IsWater(cell)) return false;
            Vector3 w = grid.CellToWorld(cell);
            ApplyRandomOffsetInCell(ref w, generator, res);
            w.y = generator.terrain != null ? generator.SampleHeight(w) : 0f;
            Quaternion rot = GetPlacementRotation(kind, prefab, generator, res);
            rot = ApplyRandomRotation(rot, generator, kind, res);
            GameObject go = UnityEngine.Object.Instantiate(prefab, w, rot);
            NavMeshSpawnSafety.DisableNavMeshAgentsOnHierarchy(go);
            if (!go.activeSelf) go.SetActive(true);
            SnapResourceBottomToTerrain(go, generator);
            EnsureResourceCollectable(go, kind, generator, res);
            go.name = $"Global_{kind}_{cell.x}_{cell.y}";
            grid.SetOccupied(cell, true);
            return true;
        }

        static bool TryFindPointInRing(Vector3 center, Vector2 ring, RTSMapGenerator generator, out Vector3 result)
        {
            var rng = generator.GetRng();
            var grid = generator.GetGrid();
            for (int i = 0; i < 40; i++)
            {
                float t = (float)rng.NextDouble();
                float dist = Mathf.Lerp(ring.x, ring.y, t);
                float angle = (float)rng.NextDouble() * 360f;
                Vector3 p = center + new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad)) * dist;
                p = generator.SnapToGrid(p);
                Vector2Int cell = grid.WorldToCell(p);
                if (grid.IsCellFree(cell) && !grid.IsWater(cell))
                {
                    result = p;
                    return true;
                }
            }
            result = center;
            return false;
        }

        static bool IsWithinExcludeRadius(Vector3 world, IList<Vector3> spawns, float radius)
        {
            for (int i = 0; i < spawns.Count; i++)
            {
                float dx = world.x - spawns[i].x;
                float dz = world.z - spawns[i].z;
                if (dx * dx + dz * dz < radius * radius) return true;
            }
            return false;
        }

        static GameObject GetPrefabForPlacement(GameObject basePrefab, GameObject[] variants, RTSMapGenerator generator)
        {
            var rng = generator.GetRng();
            if (variants != null && variants.Length > 0)
            {
                for (int t = 0; t < 20; t++)
                {
                    var pick = variants[rng.Next(variants.Length)];
                    if (pick != null) return pick;
                }
            }
            return basePrefab;
        }

        static GameObject GetRandomTreePrefab(ResourceRuntimeSettings s, RTSMapGenerator g) => GetRandomPrefab(s.treePrefab, s.treePrefabVariants, g);
        static GameObject GetRandomStonePrefab(ResourceRuntimeSettings s, RTSMapGenerator g) => GetRandomPrefab(s.stonePrefab, s.stonePrefabVariants, g);
        static GameObject GetRandomGoldPrefab(ResourceRuntimeSettings s, RTSMapGenerator g) => GetRandomPrefab(s.goldPrefab, s.goldPrefabVariants, g);
        static GameObject GetRandomAnimalPrefab(ResourceRuntimeSettings s, RTSMapGenerator g) => GetRandomPrefab(s.animalPrefab, s.animalPrefabVariants, g);

        static GameObject GetRandomPrefab(GameObject basePrefab, GameObject[] variants, RTSMapGenerator generator)
        {
            var rng = generator.GetRng();
            if (variants != null && variants.Length > 0)
            {
                for (int t = 0; t < 20; t++)
                {
                    var pick = variants[rng.Next(variants.Length)];
                    if (pick != null) return pick;
                }
            }
            return basePrefab;
        }

        static bool HasAnyTreePrefab(ResourceRuntimeSettings s) => s.treePrefab != null || HasAnyIn(s.treePrefabVariants);
        static bool HasAnyStonePrefab(ResourceRuntimeSettings s) => s.stonePrefab != null || HasAnyIn(s.stonePrefabVariants);
        static bool HasAnyGoldPrefab(ResourceRuntimeSettings s) => s.goldPrefab != null || HasAnyIn(s.goldPrefabVariants);
        static bool HasAnyAnimalPrefab(ResourceRuntimeSettings s) => s.animalPrefab != null || HasAnyIn(s.animalPrefabVariants);
        static bool HasAnyIn(GameObject[] arr)
        {
            if (arr == null) return false;
            foreach (var p in arr) if (p != null) return true;
            return false;
        }

        /// <summary>Evita rocas/piedras flotantes: coloca la base del mesh sobre el terreno (estilo Anno).</summary>
        static void SnapResourceBottomToTerrain(GameObject go, RTSMapGenerator generator)
        {
            if (go == null || generator == null || generator.terrain == null) return;
            float bottomY = float.MaxValue;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null || !renderers[i].enabled) continue;
                bottomY = Mathf.Min(bottomY, renderers[i].bounds.min.y);
            }
            if (bottomY == float.MaxValue) return;
            Vector3 center = go.transform.position;
            float terrainY = generator.SampleHeight(center);
            float delta = terrainY - bottomY;
            if (Mathf.Abs(delta) < 0.001f) return;
            go.transform.position = go.transform.position + Vector3.up * delta;
        }

        static void EnsureResourceCollectable(GameObject go, ResourceKind kind, RTSMapGenerator generator, ResourceRuntimeSettings res)
        {
            if (go == null) return;
            var node = go.GetComponent<ResourceNode>();
            if (node == null)
            {
                node = go.AddComponent<ResourceNode>();
                node.kind = kind;
                node.amount = 300;
                node.snapToNavMeshOnAwake = true;
                node.snapRadius = 3f;
            }
            else
            {
                node.kind = kind;
                if (node.amount <= 0) node.amount = 300;
            }
            if (go.GetComponentInChildren<Collider>(true) == null)
            {
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = 0.5f;
                cap.height = 2f;
                cap.center = Vector3.zero;
            }
            if (go.GetComponent<ResourceSelectable>() == null)
                go.AddComponent<ResourceSelectable>();

            if (generator != null && res.forceResourceShadowCasting)
                EnsureResourceCastsShadows(go);

            string layerName = !string.IsNullOrEmpty(res.resourceLayerName) ? res.resourceLayerName : "Resource";
            int resourceLayer = LayerMask.NameToLayer(layerName);
            if (resourceLayer < 0) resourceLayer = 11;
            SetLayerRecursively(go, resourceLayer);
            if (kind == ResourceKind.Stone && res.stoneMaterialOverride != null)
                ApplyMaterialToRenderers(go, res.stoneMaterialOverride);
            if (kind == ResourceKind.Wood && res.treeMaterialOverrides != null && res.treeMaterialOverrides.Length > 0)
                ApplyTreeMaterialOverrides(go, generator, res);
            if (kind == ResourceKind.Wood && go.GetComponent<FadeableByCamera>() == null)
                go.AddComponent<FadeableByCamera>();

            EnsureRobustResourcePickCollider(go);
        }

        /// <summary>
        /// Árboles con mesh en hijos y pivot en el suelo suelen dejar un Capsule en el raíz que no envuelve el follaje:
        /// raycast/hover fallan. Ajusta o añade un <see cref="BoxCollider"/> en el raíz según bounds de renderers.
        /// </summary>
        static void EnsureRobustResourcePickCollider(GameObject go)
        {
            if (go == null) return;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            Bounds wb = default;
            bool have = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled) continue;
                if (!have) { wb = r.bounds; have = true; }
                else wb.Encapsulate(r.bounds);
            }
            if (!have) return;

            float pickQuality = 0f;
            float bestVolume = 0f;
            var allCols = go.GetComponentsInChildren<Collider>(true);
            if (allCols != null)
            {
                for (int i = 0; i < allCols.Length; i++)
                {
                    var c = allCols[i];
                    if (c == null || !c.enabled || c.isTrigger) continue;
                    var s = c.bounds.size;
                    float md = Mathf.Max(s.x, s.y, s.z);
                    pickQuality = Mathf.Max(pickQuality, md);
                    bestVolume = Mathf.Max(bestVolume, s.x * s.y * s.z);
                }
            }
            // Umbral más bajo: colliders altos y delgados (follaje) tenían maxDim alto pero mal centrados para raycast desde arriba.
            const float minGoodPick = 0.45f;
            const float minVolume = 0.06f;
            if (pickQuality >= minGoodPick && bestVolume >= minVolume) return;

            var rootCols = go.GetComponents<Collider>();
            for (int i = 0; i < rootCols.Length; i++)
            {
                if (rootCols[i] != null)
                    UnityEngine.Object.Destroy(rootCols[i]);
            }

            var box = go.AddComponent<BoxCollider>();
            Vector3 lossy = go.transform.lossyScale;
            float sx = Mathf.Max(0.001f, Mathf.Abs(lossy.x));
            float sy = Mathf.Max(0.001f, Mathf.Abs(lossy.y));
            float sz = Mathf.Max(0.001f, Mathf.Abs(lossy.z));
            box.center = go.transform.InverseTransformPoint(wb.center);
            box.size = new Vector3(
                Mathf.Max(0.55f, wb.size.x / sx),
                Mathf.Max(0.85f, wb.size.y / sy),
                Mathf.Max(0.55f, wb.size.z / sz));
        }

        /// <summary>Asegura que los recursos (árboles, animales, piedra, oro) proyecten sombras aunque el prefab tenga Off.</summary>
        static void EnsureResourceCastsShadows(GameObject go)
        {
            if (go == null) return;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr != null)
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr != null)
                    smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        /// <summary>Para árboles usa treePlacementRotation del generador; para el resto respeta la rotación del prefab (ej. ciervo con X=270 para que quede derecho).</summary>
        static Quaternion GetPlacementRotation(ResourceKind kind, GameObject prefab, RTSMapGenerator generator, ResourceRuntimeSettings res)
        {
            if (kind == ResourceKind.Wood)
                return Quaternion.Euler(res.treePlacementRotation);
            if (prefab != null)
                return prefab.transform.rotation;
            return Quaternion.identity;
        }

        /// <summary>Añade rotación Y aleatoria solo para árboles/piedra/oro; los animales (Food) mantienen la orientación del prefab para no tumbarlos.</summary>
        static Quaternion ApplyRandomRotation(Quaternion baseRot, RTSMapGenerator generator, ResourceKind kind, ResourceRuntimeSettings res)
        {
            if (kind == ResourceKind.Food) return baseRot;
            if (generator == null || !res.randomRotationPerResource) return baseRot;
            var rng = generator.GetRng();
            if (rng == null) return baseRot;
            float randomY = (float)(rng.NextDouble() * 360.0);
            return baseRot * Quaternion.Euler(0f, randomY, 0f);
        }

        static void ApplyRandomOffsetInCell(ref Vector3 w, RTSMapGenerator generator, ResourceRuntimeSettings res)
        {
            if (generator == null || res.cellPlacementRandomOffset <= 0.0001f) return;
            var rng = generator.GetRng();
            var grid = generator.GetGrid();
            if (rng == null || grid == null) return;
            float half = grid.cellSize * 0.5f * res.cellPlacementRandomOffset;
            w.x += (float)(rng.NextDouble() * 2.0 - 1.0) * half;
            w.z += (float)(rng.NextDouble() * 2.0 - 1.0) * half;
        }

        static void ApplyTreeMaterialOverrides(GameObject go, RTSMapGenerator generator, ResourceRuntimeSettings res)
        {
            if (go == null || res.treeMaterialOverrides == null || res.treeMaterialOverrides.Length == 0) return;
            var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                int matIndex = GetTreeMaterialIndexForRenderer(renderers[i].gameObject, i, res.treeMaterialOverrides.Length);
                Material mat = res.treeMaterialOverrides[matIndex];
                if (mat != null) renderers[i].sharedMaterial = mat;
            }
        }

        /// <summary>0 = tronco, 1 = follaje. Por nombre del objeto o por orden si no coincide.</summary>
        static int GetTreeMaterialIndexForRenderer(GameObject rendererOwner, int rendererOrder, int materialCount)
        {
            string name = rendererOwner.name.ToLowerInvariant();
            // Follaje/hojas: [1]
            if (name.Contains("leaf") || name.Contains("leaves") || name.Contains("foliage") ||
                name.Contains("hoja") || name.Contains("copa") || name.Contains("canopy") || name.Contains("fronda"))
                return materialCount > 1 ? 1 : 0;
            // Tronco: [0]
            if (name.Contains("trunk") || name.Contains("tronco") || name.Contains("bark") || name.Contains("stem") || name.Contains("tallo"))
                return 0;
            return Mathf.Min(rendererOrder, materialCount - 1);
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                var child = go.transform.GetChild(i);
                if (child != null) SetLayerRecursively(child.gameObject, layer);
            }
        }

        static void ApplyMaterialToRenderers(GameObject go, Material mat)
        {
            if (go == null || mat == null) return;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                if (r != null) r.sharedMaterial = mat;
        }

        sealed class PlayerResourcesStats
        {
            public int WoodTrees, GoldNodes, StoneNodes, FoodValue;
            public void Add(ResourceKind kind, int amount)
            {
                switch (kind)
                {
                    case ResourceKind.Wood: WoodTrees += amount; break;
                    case ResourceKind.Gold: GoldNodes += amount; break;
                    case ResourceKind.Stone: StoneNodes += amount; break;
                    case ResourceKind.Food: FoodValue += amount; break;
                }
            }
        }
    }
}
