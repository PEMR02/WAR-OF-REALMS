using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Faction;
using Project.Gameplay.Players;
using Project.Gameplay.Resources;
using Project.Gameplay.Units;

namespace Project.Gameplay.AI
{
    public sealed class AIEconomyManager
    {
        public void Tick(
            AIKnowledge knowledge,
            PlayerResources res,
            IReadOnlyList<VillagerGatherer> villagers,
            AIDifficultyProfile profile,
            Vector3 townCenter,
            FactionId faction)
        {
            if (knowledge == null || res == null || villagers == null || profile == null) return;

            float sight = 220f * Mathf.Lerp(0.75f, 1.15f, profile.scoutingFrequency);
            knowledge.RefreshResourceLists(townCenter, sight, faction);

            for (int i = 0; i < villagers.Count; i++)
            {
                var v = villagers[i];
                if (v == null) continue;
                var builder = v.GetComponent<Builder>();
                if (builder != null && builder.HasBuildTarget) continue;
                if (!v.IsIdle) continue;

                ResourceKind kind = PickKindByStock(res, profile);
                var node = knowledge.PickNearestNeed(kind, v.transform.position, profile);
                if (node != null)
                    v.Gather(node);
            }
        }

        static ResourceKind PickKindByStock(PlayerResources res, AIDifficultyProfile p)
        {
            float f = res.food / Mathf.Max(1f, 120f * p.economicEfficiency);
            float w = res.wood / Mathf.Max(1f, 100f * p.economicEfficiency);
            float g = res.gold / Mathf.Max(1f, 80f * p.economicEfficiency);
            float s = res.stone / Mathf.Max(1f, 60f * p.economicEfficiency);
            float min = Mathf.Min(Mathf.Min(f, w), Mathf.Min(g, s));
            if (min == f) return ResourceKind.Food;
            if (min == w) return ResourceKind.Wood;
            if (min == g) return ResourceKind.Gold;
            return ResourceKind.Stone;
        }
    }
}
