using UnityEngine;
using Project.Gameplay.Players;

namespace Project.Gameplay.AI
{
    /// <summary>FSM ligero: decide el macro-estado según economía, amenazas y tiempo.</summary>
    public sealed class AIStrategyManager
    {
        public AIStrategicState State { get; private set; } = AIStrategicState.Opening;

        public void Tick(AIKnowledge k, PlayerResources res, AIDifficultyProfile profile, AIMilitaryManager military)
        {
            if (k == null || res == null) return;

            if (k.VisibleHostileUnits.Count > 0)
            {
                State = AIStrategicState.Defending;
                return;
            }

            int total = res.wood + res.gold + res.stone + res.food;
            bool lowEco = total < 180;
            bool canBoom = res.food > 120 && res.wood > 80;

            if (k.GameTimeSeconds < 90f && lowEco)
            {
                State = AIStrategicState.Opening;
                return;
            }

            if (k.EstimatedSelfMilitaryStrength < 8f && canBoom)
            {
                State = AIStrategicState.Boom;
                return;
            }

            if (military != null && military.ShouldAttackNow())
            {
                State = profile != null && profile.aggression > 0.9f ? AIStrategicState.Attacking : AIStrategicState.Harassing;
                return;
            }

            if (k.GameTimeSeconds > 240f && canBoom)
                State = AIStrategicState.Expanding;
            else if (lowEco)
                State = AIStrategicState.Recovering;
            else
                State = AIStrategicState.Boom;
        }
    }
}
