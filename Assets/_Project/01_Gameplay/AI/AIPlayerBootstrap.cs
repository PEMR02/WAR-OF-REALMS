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

            AIControllerRuntimeCatalog.Villager = villager;
            AIControllerRuntimeCatalog.House = generator.aiHouseSO;
            AIControllerRuntimeCatalog.Barracks = generator.aiBarracksSO;
            AIControllerRuntimeCatalog.ProductionCatalog = catalog;

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
    }
}
