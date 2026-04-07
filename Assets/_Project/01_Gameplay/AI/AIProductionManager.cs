using UnityEngine;
using Project.Gameplay.Buildings;
using Project.Gameplay.Players;
using Project.Gameplay.Units;

namespace Project.Gameplay.AI
{
    public sealed class AIProductionManager
    {
        readonly ProductionCatalog _catalog;

        public AIProductionManager(ProductionCatalog catalog)
        {
            _catalog = catalog;
        }

        public void TickTownCenter(ProductionBuilding tc, PlayerResources res, PopulationManager pop, AIStrategicState state, AIDifficultyProfile profile)
        {
            if (tc == null || res == null || pop == null || profile == null) return;
            var villager = AIControllerRuntimeCatalog.Villager;
            if (villager == null) return;
            if (tc.queue.Count >= 3) return;
            if (!pop.CanReservePopulation(villager.populationCost)) return;
            if (!CanAfford(res, villager)) return;
            float want = state == AIStrategicState.Opening || state == AIStrategicState.Boom ? 1f : 0.35f;
            if (Random.value > want * profile.economicEfficiency) return;
            tc.TryQueueUnit(villager);
        }

        public void TickBarracks(ProductionBuilding barracks, PlayerResources res, PopulationManager pop, AIDifficultyProfile profile)
        {
            if (barracks == null || res == null || pop == null || profile == null) return;
            if (_catalog == null) return;
            if (barracks.queue.Count >= 4) return;
            if (!pop.HasPopulationSpace) return;
            if (Random.value > 0.55f * profile.economicEfficiency) return;

            float infantryWeight = 0.45f;
            var militia = _catalog.Get("Barracks", 1);
            var archer = _catalog.Get("Barracks", 2);
            UnitSO pick = militia;
            if (archer != null && militia != null && Random.value > infantryWeight)
                pick = archer;
            if (pick == null) pick = militia ?? archer;
            if (pick == null) return;
            if (!pop.CanReservePopulation(pick.populationCost)) return;
            if (!CanAfford(res, pick)) return;
            barracks.TryQueueUnit(pick);
        }

        static bool CanAfford(PlayerResources res, UnitSO u)
        {
            if (u.costs == null) return true;
            for (int i = 0; i < u.costs.Length; i++)
            {
                var c = u.costs[i];
                if (res.Get(c.kind) < c.amount) return false;
            }
            return true;
        }
    }
}
