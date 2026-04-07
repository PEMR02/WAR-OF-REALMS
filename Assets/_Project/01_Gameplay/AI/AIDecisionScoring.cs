using UnityEngine;
using Project.Gameplay.Buildings;
using Project.Gameplay.Players;
using Project.Gameplay.Units;

namespace Project.Gameplay.AI
{
    /// <summary>Puntuaciones para elegir la siguiente acción (no solo if-else encadenados).</summary>
    public static class AIDecisionScoring
    {
        public static float ProduceVillagerScore(PlayerResources res, PopulationManager pop, AIStrategicState state, AIDifficultyProfile p)
        {
            if (res == null || pop == null) return 0f;
            if (!pop.HasPopulationSpace) return 0f;
            var villager = AIControllerRuntimeCatalog.Villager;
            if (villager == null) return 0f;
            if (!CanAfford(res, villager)) return 0f;
            float housingPressure = 1f - (pop.AvailablePopulation / Mathf.Max(1f, pop.MaxPopulation));
            float want = 55f + housingPressure * 35f;
            if (state == AIStrategicState.Opening) want += 25f;
            return want * p.economicEfficiency;
        }

        public static float BuildHouseScore(PlayerResources res, PopulationManager pop, bool houseSitePending, bool alreadyHasCompletedHouse, AIDifficultyProfile p)
        {
            if (houseSitePending) return 0f;
            if (pop == null || res == null) return 0f;
            var house = AIControllerRuntimeCatalog.House;
            if (house == null || !CanAffordBuilding(res, house)) return 0f;

            int max = Mathf.Max(1, pop.MaxPopulation);
            float headroom = pop.AvailablePopulation / (float)max;
            // Baja prioridad si ya hay cupo; no devolver ~4f (bloqueaba RunConstructionScoring con umbral 4.5f).
            if (pop.AvailablePopulation > 4 && headroom > 0.34f)
                return 0f;

            float score = (alreadyHasCompletedHouse ? 70f : 92f) * p.economicEfficiency;
            if (pop.AvailablePopulation <= 1) score += 22f * p.economicEfficiency;
            return score;
        }

        public static float BuildBarracksScore(PlayerResources res, bool hasBarracks, bool barracksQueued, bool wantsArmy, AIDifficultyProfile p)
        {
            if (hasBarracks || barracksQueued || !wantsArmy) return 0f;
            var b = AIControllerRuntimeCatalog.Barracks;
            if (b == null || res == null || !CanAffordBuilding(res, b)) return 0f;
            return 78f * p.economicEfficiency;
        }

        public static float TrainUnitScore(PlayerResources res, PopulationManager pop, ProductionBuilding barracks, UnitSO unit, float armyRatioDeficit, AIDifficultyProfile p)
        {
            if (barracks == null || unit == null || res == null || pop == null) return 0f;
            if (barracks.queue.Count >= 4) return 0f;
            if (!pop.CanReservePopulation(unit.populationCost)) return 0f;
            if (!CanAfford(res, unit)) return 0f;
            return (40f + armyRatioDeficit * 80f) * p.economicEfficiency;
        }

        public static float AttackScore(AIMilitaryManager military, AIStrategicState state, AIDifficultyProfile p)
        {
            if (military == null || !military.ShouldAttackNow()) return 0f;
            float s = 60f * p.aggression;
            if (state == AIStrategicState.Attacking) s += 25f;
            if (state == AIStrategicState.Harassing) s += 15f;
            return s;
        }

        public static float DefendScore(AIKnowledge k, AIStrategicState state, AIDifficultyProfile p)
        {
            if (k == null || k.VisibleHostileUnits.Count == 0) return 0f;
            float s = 85f * Mathf.Clamp(p.tacticalSkill, 0.3f, 1.5f);
            if (state == AIStrategicState.Defending) s += 20f;
            return s;
        }

        public static float ExpandScore(AIStrategicState state, float bankedWood, AIDifficultyProfile p)
        {
            if (state != AIStrategicState.Boom && state != AIStrategicState.Expanding) return 0f;
            if (bankedWood < 200f) return 0f;
            return 25f * p.economicEfficiency;
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

        static bool CanAffordBuilding(PlayerResources res, BuildingSO b)
        {
            if (b.costs == null) return true;
            for (int i = 0; i < b.costs.Length; i++)
            {
                var c = b.costs[i];
                if (res.Get(c.kind) < c.amount) return false;
            }
            return true;
        }
    }
}
