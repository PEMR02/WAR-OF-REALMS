using UnityEngine;
using Project.Gameplay.Buildings;
using Project.Gameplay.Faction;
using Project.Gameplay.Map;
using Project.Gameplay.Players;
using Project.Gameplay.Units;
using Project.UI;

namespace Project.Gameplay.AI
{
    /// <summary>Crea un <see cref="AIController"/> por cada slot IA tras generar el mapa / NavMesh.</summary>
    public static class AIPlayerBootstrap
    {
        static bool s_loggedMissingAiVillager;
        public static void SpawnForMatch(MatchConfig match, RTSMapGenerator generator)
        {
            if (match == null || generator == null) return;

            var existing = Object.FindObjectsByType<AIController>(FindObjectsSortMode.None);
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null)
                    Object.Destroy(existing[i].gameObject);
            }

            ProductionCatalog catalog = generator.aiProductionCatalog;
            if (catalog == null)
            {
                var hud = Object.FindFirstObjectByType<ProductionHUD>();
                if (hud != null) catalog = hud.catalog;
            }

            UnitSO villager = generator.aiVillagerUnitSO;
            if (villager == null && catalog != null)
                villager = catalog.Get("town_center", 1);
            if (villager == null)
                villager = FindVillagerUnitSoFallback();

            BuildingSO house = generator.aiHouseSO;
            if (house == null)
                house = FindBuildingSoById("House");
            BuildingSO barracks = generator.aiBarracksSO;
            if (barracks == null)
                barracks = FindBuildingSoById("Barracks");

            AIControllerRuntimeCatalog.Villager = villager;
            AIControllerRuntimeCatalog.House = house;
            AIControllerRuntimeCatalog.Barracks = barracks;
            AIControllerRuntimeCatalog.ProductionCatalog = catalog;

            if (villager == null)
            {
                if (!s_loggedMissingAiVillager)
                {
                    s_loggedMissingAiVillager = true;
                    Debug.LogError("[AI] Falta UnitSO de aldeano para IA (aiVillagerUnitSO/catalog/fallback).");
                }
            }
            if (house == null)
                Debug.LogError("[AI] Falta BuildingSO de casa para IA (aiHouseSO).");
            if (barracks == null)
                Debug.LogWarning("[AI] Falta BuildingSO de barracks para IA (aiBarracksSO).");

            var placer = Object.FindFirstObjectByType<BuildingPlacer>();
            var terrain = generator.terrain != null ? generator.terrain : Object.FindFirstObjectByType<Terrain>();
            LayerMask blocking = placer != null ? placer.blockingMask : default;

            int n = Mathf.Clamp(match.players.playerCount, 1, match.players.slots != null ? match.players.slots.Count : 1);
            for (int slot = 0; slot < n; slot++)
            {
                if (slot >= match.players.slots.Count) break;
                if (match.players.slots[slot].kind != MatchConfig.PlayerSlotKind.AI)
                    continue;

                string tcName = $"TownCenter_Player{slot + 1}";
                var tcGo = GameObject.Find(tcName);
                if (tcGo == null)
                {
                    Debug.LogWarning($"AIPlayerBootstrap: no se encontró {tcName}.");
                    continue;
                }

                var res = tcGo.GetComponent<PlayerResources>();
                if (res == null)
                    res = tcGo.AddComponent<PlayerResources>();
                var pop = tcGo.GetComponent<PopulationManager>();
                if (pop == null)
                {
                    pop = tcGo.AddComponent<PopulationManager>();
                    pop.skipAutoRegisterPopulation = true;
                }

                var tcProd = tcGo.GetComponent<ProductionBuilding>();
                if (tcProd == null)
                {
                    Debug.LogWarning($"AIPlayerBootstrap: {tcName} sin ProductionBuilding.");
                    continue;
                }

                var fm = tcGo.GetComponent<FactionMember>();
                var faction = fm != null ? fm.faction : FactionId.Enemy;

                var go = new GameObject($"AIController_Player{slot + 1}");
                var ctrl = go.AddComponent<AIController>();
                ctrl.playerIndexOneBased = slot + 1;
                ctrl.myFaction = faction;
                ctrl.resources = res;
                ctrl.population = pop;
                ctrl.townCenterProduction = tcProd;
                ctrl.townCenterTransform = tcGo.transform;

                ctrl.Initialize(
                    match.players.slots[slot].aiDifficulty,
                    res,
                    pop,
                    tcProd,
                    tcGo.transform,
                    placer,
                    terrain,
                    blocking,
                    catalog);
            }
        }

        static BuildingSO FindBuildingSoById(string id) => AIControllerRuntimeCatalog.FindBuildingSoById(id);

        static UnitSO FindVillagerUnitSoFallback()
        {
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<UnitSO>();
            for (int i = 0; i < all.Length; i++)
            {
                var so = all[i];
                if (so == null) continue;
                if (so.role == UnitRole.Economy) return so;
                string id = so.id ?? string.Empty;
                string dn = so.displayName ?? string.Empty;
                if (id.Equals("Villager", System.StringComparison.OrdinalIgnoreCase)
                    || id.Equals("Aldeano", System.StringComparison.OrdinalIgnoreCase)
                    || id.Equals("Worker", System.StringComparison.OrdinalIgnoreCase)
                    || dn.Equals("Villager", System.StringComparison.OrdinalIgnoreCase)
                    || dn.Equals("Aldeano", System.StringComparison.OrdinalIgnoreCase)
                    || dn.Equals("Worker", System.StringComparison.OrdinalIgnoreCase))
                    return so;
            }
            return null;
        }
    }
}
