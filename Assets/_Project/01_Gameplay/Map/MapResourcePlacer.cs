using System;
using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Resources;
using Project.Gameplay.Map.Generator;

namespace Project.Gameplay.Map
{
    /// <summary>
    /// Coloca recursos (árboles, piedra, oro, comida) en el mapa.
    /// Separado de RTSMapGenerator para mantener terreno y recursos en módulos distintos.
    /// </summary>
    public static class MapResourcePlacer
    {
        /// <summary>Coloca prefabs de recursos según CellData.resourceType del Generador Definitivo. Usar cuando useDefinitiveGenerator = true.</summary>
        public static void PlaceFromDefinitiveGrid(GridSystem definitiveGrid, RTSMapGenerator gen)
        {
            if (definitiveGrid == null || gen == null) return;
            var mapGrid = gen.GetGrid();
            if (mapGrid == null || !mapGrid.IsReady) return;

            int wood = 0, stone = 0, gold = 0, food = 0;
            for (int x = 0; x < definitiveGrid.Width; x++)
            {
                for (int z = 0; z < definitiveGrid.Height; z++)
                {
                    ref var cell = ref definitiveGrid.GetCell(x, z);
                    if (cell.resourceType == ResourceType.None) continue;
                    if (!mapGrid.IsCellFree(new Vector2Int(x, z))) continue;

                    Vector3 world = definitiveGrid.CellToWorldCenter(x, z);
                    world.y = gen.terrain != null ? gen.SampleHeight(world) : world.y;
                    GameObject prefab = GetPrefabForResourceType(cell.resourceType, gen);
                    if (prefab == null) continue;
                    ResourceKind kind = ToResourceKind(cell.resourceType);
                    Quaternion rot = GetPlacementRotation(kind, prefab, gen);
                    if (kind != ResourceKind.Food && gen.randomRotationPerResource && gen.GetRng() != null)
                        rot = rot * Quaternion.Euler(0f, (float)(gen.GetRng().NextDouble() * 360.0), 0f);
                    GameObject go = UnityEngine.Object.Instantiate(prefab, world, rot);
                    if (!go.activeSelf) go.SetActive(true);
                    SnapResourceBottomToTerrain(go, gen);
                    EnsureResourceCollectable(go, kind, gen);
                    go.name = $"{cell.resourceType}_{x}_{z}";
                    mapGrid.SetOccupied(new Vector2Int(x, z), true);
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
        public static void PlaceGlobalOnly(IList<Vector3> spawns, RTSMapGenerator gen)
        {
            if (gen == null) return;
            var grid = gen.GetGrid();
            if (grid == null || !grid.IsReady) return;
            if (spawns == null) spawns = Array.Empty<Vector3>();
            PlaceGlobalResources(spawns, gen);
        }

        static GameObject GetPrefabForResourceType(ResourceType type, RTSMapGenerator gen)
        {
            switch (type)
            {
                case ResourceType.Wood: return GetPrefabForPlacement(gen.treePrefab, gen.treePrefabVariants, gen);
                case ResourceType.Stone: return GetPrefabForPlacement(gen.stonePrefab, gen.stonePrefabVariants, gen);
                case ResourceType.Gold: return GetPrefabForPlacement(gen.goldPrefab, gen.goldPrefabVariants, gen);
                case ResourceType.Food: return GetPrefabForFood(gen);
                default: return null;
            }
        }

        /// <summary>Para Food: si hay berry y animal, alternar al azar; si no, usar el que exista.</summary>
        static GameObject GetPrefabForFood(RTSMapGenerator gen)
        {
            bool hasBerry = gen.berryPrefab != null || (gen.berryPrefabVariants != null && gen.berryPrefabVariants.Length > 0);
            bool hasAnimal = gen.animalPrefab != null || (gen.animalPrefabVariants != null && gen.animalPrefabVariants.Length > 0);
            var rng = gen.GetRng();
            if (hasBerry && hasAnimal)
            {
                if (rng != null && rng.Next(2) == 0)
                    return GetPrefabForPlacement(gen.animalPrefab, gen.animalPrefabVariants, gen);
                return GetPrefabForPlacement(gen.berryPrefab, gen.berryPrefabVariants, gen);
            }
            if (hasAnimal) return GetPrefabForPlacement(gen.animalPrefab, gen.animalPrefabVariants, gen);
            return GetPrefabForPlacement(gen.berryPrefab, gen.berryPrefabVariants, gen);
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

        public static void Place(IList<Vector3> spawns, RTSMapGenerator gen)
        {
            if (gen == null) return;
            var grid = gen.GetGrid();
            if (grid == null || !grid.IsReady) return;

            PlaceResourcesPerPlayer(spawns, gen);
            PlaceGlobalResources(spawns, gen);
        }

        static void PlaceResourcesPerPlayer(IList<Vector3> spawns, RTSMapGenerator gen)
        {
            var grid = gen.GetGrid();
            var rng = gen.GetRng();
            if (grid == null || rng == null) return;

            Debug.Log($"PlaceResourcesPerPlayer: {spawns.Count} jugadores");

            if (gen.treePrefab == null)
                Debug.LogWarning("RTS Map Generator: treePrefab no asignado.");
            if (gen.berryPrefab == null) Debug.LogWarning("RTS Map Generator: berryPrefab no asignado.");
            if (gen.goldPrefab == null) Debug.LogWarning("RTS Map Generator: goldPrefab no asignado.");
            if (!HasAnyStonePrefab(gen)) Debug.LogWarning("RTS Map Generator: stonePrefab (o stonePrefabVariants) no asignado.");
            if (gen.animalPrefab == null) Debug.LogWarning("RTS Map Generator: animalPrefab no asignado.");

            for (int i = 0; i < spawns.Count; i++)
            {
                int attempts = 0;
                while (attempts < gen.maxResourceRetries)
                {
                    attempts++;
                    var stats = new PlayerResourcesStats();
                    var spawned = new List<GameObject>();
                    var occupiedCells = new List<Vector2Int>();
                    bool ok = PlaceResourcesForSpawn(spawns[i], stats, spawned, occupiedCells, gen);
                    bool fair = stats.WoodTrees >= gen.minWoodTrees &&
                        stats.GoldNodes >= gen.minGoldNodes &&
                        stats.StoneNodes >= gen.minStoneNodes &&
                        stats.FoodValue >= gen.minFoodValue;

                    if (ok)
                    {
                        if (fair)
                            Debug.Log($"Player {i + 1} recursos: Wood={stats.WoodTrees}, Gold={stats.GoldNodes}, Stone={stats.StoneNodes}, Food={stats.FoodValue}, Total={spawned.Count}");
                        else
                            Debug.LogWarning($"Player {i + 1}: recursos colocados pero por debajo del mínimo deseado (Wood={stats.WoodTrees}/{gen.minWoodTrees}).");
                        break;
                    }
                    for (int s = 0; s < spawned.Count; s++)
                        if (spawned[s] != null) UnityEngine.Object.Destroy(spawned[s]);
                    for (int c = 0; c < occupiedCells.Count; c++)
                        grid.SetOccupied(occupiedCells[c], false);
                    if (attempts == gen.maxResourceRetries)
                        Debug.LogWarning($"Player {i + 1}: No se pudieron colocar recursos después de {gen.maxResourceRetries} intentos.");
                }
            }
        }

        static void PlaceGlobalResources(IList<Vector3> spawns, RTSMapGenerator gen)
        {
            var grid = gen.GetGrid();
            var rng = gen.GetRng();
            if (grid == null || rng == null) return;
            if (!HasAnyTreePrefab(gen) && !HasAnyGoldPrefab(gen) && !HasAnyStonePrefab(gen) && !HasAnyAnimalPrefab(gen)) return;

            int treesTarget = rng.Next(gen.globalTrees.x, gen.globalTrees.y + 1);
            int stoneTarget = rng.Next(gen.globalStone.x, gen.globalStone.y + 1);
            int goldTarget = rng.Next(gen.globalGold.x, gen.globalGold.y + 1);
            int animalsTarget = rng.Next(gen.globalAnimals.x, gen.globalAnimals.y + 1);
            int treesPlaced = 0, stonePlaced = 0, goldPlaced = 0, animalsPlaced = 0;

            // Árboles: con clustering se agrupan en bosques densos + resto dispersos
            if (HasAnyTreePrefab(gen) && treesTarget > 0)
            {
                if (gen.forestClustering && gen.clusterMaxSize > 0 && gen.clusterMinSize < gen.clusterMaxSize)
                {
                    treesPlaced = PlaceGlobalTreesWithClustering(spawns, treesTarget, gen);
                }
                else
                {
                    treesPlaced = PlaceGlobalTreesScattered(spawns, treesTarget, gen);
                }
            }

            // Piedra, oro y animales: dispersos
            int maxAttempts = (stoneTarget + goldTarget + animalsTarget) * 25;
            int attempts = 0;
            while ((stonePlaced < stoneTarget || goldPlaced < goldTarget || animalsPlaced < animalsTarget) && attempts < maxAttempts)
            {
                attempts++;
                Vector2Int cell = new Vector2Int(rng.Next(0, grid.width), rng.Next(0, grid.height));
                if (!grid.IsCellFree(cell)) continue;
                Vector3 world = grid.CellToWorld(cell);
                if (IsWithinExcludeRadius(world, spawns, gen.globalExcludeRadius)) continue;

                if (stonePlaced < stoneTarget && GetRandomStonePrefab(gen) != null && TryPlaceSingleGlobal(GetRandomStonePrefab(gen), cell, ResourceKind.Stone, gen))
                { stonePlaced++; continue; }
                if (goldPlaced < goldTarget && GetRandomGoldPrefab(gen) != null && TryPlaceSingleGlobal(GetRandomGoldPrefab(gen), cell, ResourceKind.Gold, gen))
                { goldPlaced++; continue; }
                if (animalsPlaced < animalsTarget && GetRandomAnimalPrefab(gen) != null && TryPlaceSingleGlobal(GetRandomAnimalPrefab(gen), cell, ResourceKind.Food, gen))
                { animalsPlaced++; continue; }
            }
            Debug.Log($"Recursos en el mapa: árboles={treesPlaced}, piedra={stonePlaced}, oro={goldPlaced}, animales={animalsPlaced}");
        }

        /// <summary>Coloca árboles globales en bosques (clusters) densos y el resto dispersos.</summary>
        static int PlaceGlobalTreesWithClustering(IList<Vector3> spawns, int treesTarget, RTSMapGenerator gen)
        {
            var grid = gen.GetGrid();
            var rng = gen.GetRng();
            int placed = 0;
            int inClusters = (int)(treesTarget * 0.75f); // 75% en bosques, resto dispersos

            // Radio del cluster en celdas: alta densidad = radio pequeño (árboles muy juntos), baja = radio mayor
            float clusterRadiusCells = Mathf.Lerp(6f, 2.5f, gen.clusterDensity);
            int minSize = Mathf.Clamp(gen.clusterMinSize, 2, 200);
            int maxSize = Mathf.Clamp(gen.clusterMaxSize, minSize, 200);

            int clusterBudget = inClusters;
            int maxClusters = 20;
            for (int c = 0; c < maxClusters && clusterBudget > 0; c++)
            {
                int clusterSize = rng.Next(minSize, maxSize + 1);
                clusterSize = Mathf.Min(clusterSize, clusterBudget);
                if (clusterSize < 2) break;

                if (!TryFindClusterCenter(spawns, gen, out Vector2Int centerCell))
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
                    Vector3 w = grid.CellToWorld(new Vector2Int(cell.x, cell.y));
                    if (IsWithinExcludeRadius(w, spawns, gen.globalExcludeRadius)) continue;

                    GameObject prefab = GetRandomTreePrefab(gen);
                    if (prefab != null && TryPlaceSingleGlobal(prefab, cell, ResourceKind.Wood, gen))
                    {
                        placed++;
                        clusterBudget--;
                    }
                }
            }

            // Resto dispersos (hasta completar treesTarget)
            int remaining = treesTarget - placed;
            if (remaining > 0)
                placed += PlaceGlobalTreesScattered(spawns, remaining, gen);
            return placed;
        }

        static bool TryFindClusterCenter(IList<Vector3> spawns, RTSMapGenerator gen, out Vector2Int centerCell)
        {
            var grid = gen.GetGrid();
            var rng = gen.GetRng();
            for (int i = 0; i < 60; i++)
            {
                Vector2Int cell = new Vector2Int(rng.Next(0, grid.width), rng.Next(0, grid.height));
                Vector3 world = grid.CellToWorld(cell);
                if (!grid.IsCellFree(cell)) continue;
                if (IsWithinExcludeRadius(world, spawns, gen.globalExcludeRadius)) continue;
                centerCell = cell;
                return true;
            }
            centerCell = Vector2Int.zero;
            return false;
        }

        /// <summary>Coloca árboles globales uno a uno en celdas aleatorias (sin clustering).</summary>
        static int PlaceGlobalTreesScattered(IList<Vector3> spawns, int treesTarget, RTSMapGenerator gen)
        {
            var grid = gen.GetGrid();
            var rng = gen.GetRng();
            int treesPlaced = 0;
            int maxAttempts = treesTarget * 25;
            int attempts = 0;
            while (treesPlaced < treesTarget && attempts < maxAttempts)
            {
                attempts++;
                Vector2Int cell = new Vector2Int(rng.Next(0, grid.width), rng.Next(0, grid.height));
                if (!grid.IsCellFree(cell)) continue;
                Vector3 world = grid.CellToWorld(cell);
                if (IsWithinExcludeRadius(world, spawns, gen.globalExcludeRadius)) continue;

                if (GetRandomTreePrefab(gen) != null && TryPlaceSingleGlobal(GetRandomTreePrefab(gen), cell, ResourceKind.Wood, gen))
                    treesPlaced++;
            }
            return treesPlaced;
        }

        static bool PlaceResourcesForSpawn(Vector3 spawn, PlayerResourcesStats stats, List<GameObject> spawned, List<Vector2Int> occupied, RTSMapGenerator gen)
        {
            PlaceCluster(spawn, gen.ringNear, gen.berries, gen.berryPrefab, stats, spawned, occupied, ResourceKind.Food, gen, gen.berryPrefabVariants);
            PlaceScatter(spawn, gen.ringNear, gen.animals, gen.animalPrefab, stats, spawned, occupied, ResourceKind.Food, gen, gen.animalPrefabVariants);
            PlaceCluster(spawn, gen.ringNear, gen.nearTrees, gen.treePrefab, stats, spawned, occupied, ResourceKind.Wood, gen, gen.treePrefabVariants);
            PlaceCluster(spawn, gen.ringMid, gen.midTrees, gen.treePrefab, stats, spawned, occupied, ResourceKind.Wood, gen, gen.treePrefabVariants);
            PlaceCluster(spawn, gen.ringMid, gen.goldSafe, gen.goldPrefab, stats, spawned, occupied, ResourceKind.Gold, gen, gen.goldPrefabVariants);
            PlaceCluster(spawn, gen.ringMid, gen.stoneSafe, gen.stonePrefab, stats, spawned, occupied, ResourceKind.Stone, gen, gen.stonePrefabVariants);
            PlaceCluster(spawn, gen.ringFar, gen.goldFar, gen.goldPrefab, stats, spawned, occupied, ResourceKind.Gold, gen, gen.goldPrefabVariants);
            return true;
        }

        static void PlaceCluster(Vector3 spawn, Vector2 ring, Vector2Int countRange, GameObject prefab,
            PlayerResourcesStats stats, List<GameObject> spawned, List<Vector2Int> occupied, ResourceKind kind,
            RTSMapGenerator generator, GameObject[] prefabVariants = null)
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
                TryPlaceSingle(pos, pPrefab, stats, spawned, occupied, kind, generator);
            }
        }

        static void PlaceScatter(Vector3 spawn, Vector2 ring, Vector2Int countRange, GameObject prefab,
            PlayerResourcesStats stats, List<GameObject> spawned, List<Vector2Int> occupied, ResourceKind kind,
            RTSMapGenerator generator, GameObject[] prefabVariants = null)
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
                TryPlaceSingle(p, pPrefab, stats, spawned, occupied, kind, generator);
            }
        }

        static bool TryPlaceSingle(Vector3 world, GameObject prefab, PlayerResourcesStats stats,
            List<GameObject> spawned, List<Vector2Int> occupied, ResourceKind kind, RTSMapGenerator generator)
        {
            var grid = generator.GetGrid();
            Vector3 snapped = generator.SnapToGrid(world);
            Vector2Int cell = grid.WorldToCell(snapped);
            if (!grid.IsCellFree(cell)) return false;
            Vector3 w = grid.CellToWorld(cell);
            ApplyRandomOffsetInCell(ref w, generator);
            w.y = generator.terrain != null ? generator.SampleHeight(w) : 0f;
            Quaternion rot = GetPlacementRotation(kind, prefab, generator);
            rot = ApplyRandomRotation(rot, generator, kind);
            GameObject go = UnityEngine.Object.Instantiate(prefab, w, rot);
            if (!go.activeSelf) go.SetActive(true);
            SnapResourceBottomToTerrain(go, generator);
            EnsureResourceCollectable(go, kind, generator);
            go.name = $"{kind}_{spawned.Count}";
            spawned.Add(go);
            occupied.Add(cell);
            grid.SetOccupied(cell, true);
            stats.Add(kind, 1);
            return true;
        }

        static bool TryPlaceSingleGlobal(GameObject prefab, Vector2Int cell, ResourceKind kind, RTSMapGenerator generator)
        {
            var grid = generator.GetGrid();
            if (!grid.IsCellFree(cell)) return false;
            Vector3 w = grid.CellToWorld(cell);
            ApplyRandomOffsetInCell(ref w, generator);
            w.y = generator.terrain != null ? generator.SampleHeight(w) : 0f;
            Quaternion rot = GetPlacementRotation(kind, prefab, generator);
            rot = ApplyRandomRotation(rot, generator, kind);
            GameObject go = UnityEngine.Object.Instantiate(prefab, w, rot);
            if (!go.activeSelf) go.SetActive(true);
            SnapResourceBottomToTerrain(go, generator);
            EnsureResourceCollectable(go, kind, generator);
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
                if (grid.IsCellFree(cell))
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

        static GameObject GetRandomTreePrefab(RTSMapGenerator g) => GetRandomPrefab(g.treePrefab, g.treePrefabVariants, g);
        static GameObject GetRandomStonePrefab(RTSMapGenerator g) => GetRandomPrefab(g.stonePrefab, g.stonePrefabVariants, g);
        static GameObject GetRandomGoldPrefab(RTSMapGenerator g) => GetRandomPrefab(g.goldPrefab, g.goldPrefabVariants, g);
        static GameObject GetRandomAnimalPrefab(RTSMapGenerator g) => GetRandomPrefab(g.animalPrefab, g.animalPrefabVariants, g);

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

        static bool HasAnyTreePrefab(RTSMapGenerator g) => g.treePrefab != null || HasAnyIn(g.treePrefabVariants);
        static bool HasAnyStonePrefab(RTSMapGenerator g) => g.stonePrefab != null || HasAnyIn(g.stonePrefabVariants);
        static bool HasAnyGoldPrefab(RTSMapGenerator g) => g.goldPrefab != null || HasAnyIn(g.goldPrefabVariants);
        static bool HasAnyAnimalPrefab(RTSMapGenerator g) => g.animalPrefab != null || HasAnyIn(g.animalPrefabVariants);
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

        static void EnsureResourceCollectable(GameObject go, ResourceKind kind, RTSMapGenerator generator)
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
            if (go.GetComponent<Collider>() == null)
            {
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = 0.5f;
                cap.height = 2f;
                cap.center = Vector3.zero;
            }
            if (go.GetComponent<ResourceSelectable>() == null)
                go.AddComponent<ResourceSelectable>();

            EnsureResourceCastsShadows(go);

            string layerName = !string.IsNullOrEmpty(generator.resourceLayerName) ? generator.resourceLayerName : "Resource";
            int resourceLayer = LayerMask.NameToLayer(layerName);
            if (resourceLayer < 0) resourceLayer = 11;
            SetLayerRecursively(go, resourceLayer);
            if (kind == ResourceKind.Stone && generator.stoneMaterialOverride != null)
                ApplyMaterialToRenderers(go, generator.stoneMaterialOverride);
            if (kind == ResourceKind.Wood && generator.treeMaterialOverrides != null && generator.treeMaterialOverrides.Length > 0)
                ApplyTreeMaterialOverrides(go, generator);
            if (kind == ResourceKind.Wood && go.GetComponent<FadeableByCamera>() == null)
                go.AddComponent<FadeableByCamera>();
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
        static Quaternion GetPlacementRotation(ResourceKind kind, GameObject prefab, RTSMapGenerator generator)
        {
            if (kind == ResourceKind.Wood)
                return Quaternion.Euler(generator.treePlacementRotation);
            if (prefab != null)
                return prefab.transform.rotation;
            return Quaternion.identity;
        }

        /// <summary>Añade rotación Y aleatoria solo para árboles/piedra/oro; los animales (Food) mantienen la orientación del prefab para no tumbarlos.</summary>
        static Quaternion ApplyRandomRotation(Quaternion baseRot, RTSMapGenerator generator, ResourceKind kind)
        {
            if (kind == ResourceKind.Food) return baseRot;
            if (generator == null || !generator.randomRotationPerResource) return baseRot;
            var rng = generator.GetRng();
            if (rng == null) return baseRot;
            float randomY = (float)(rng.NextDouble() * 360.0);
            return baseRot * Quaternion.Euler(0f, randomY, 0f);
        }

        static void ApplyRandomOffsetInCell(ref Vector3 w, RTSMapGenerator generator)
        {
            if (generator == null || generator.cellPlacementRandomOffset <= 0.0001f) return;
            var rng = generator.GetRng();
            var grid = generator.GetGrid();
            if (rng == null || grid == null) return;
            float half = grid.cellSize * 0.5f * generator.cellPlacementRandomOffset;
            w.x += (float)(rng.NextDouble() * 2.0 - 1.0) * half;
            w.z += (float)(rng.NextDouble() * 2.0 - 1.0) * half;
        }

        static void ApplyTreeMaterialOverrides(GameObject go, RTSMapGenerator generator)
        {
            if (go == null || generator.treeMaterialOverrides == null || generator.treeMaterialOverrides.Length == 0) return;
            var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                int matIndex = GetTreeMaterialIndexForRenderer(renderers[i].gameObject, i, generator.treeMaterialOverrides.Length);
                Material mat = generator.treeMaterialOverrides[matIndex];
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
