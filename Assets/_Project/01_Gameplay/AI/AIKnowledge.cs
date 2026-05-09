using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Combat;
using Project.Gameplay.Buildings;
using Project.Gameplay.Faction;
using Project.Gameplay.Resources;
using Project.Gameplay.Units;

namespace Project.Gameplay.AI
{
    /// <summary>Memoria limitada de la IA (sin omnisciencia): lo que ve o recordó recientemente.</summary>
    public sealed class AIKnowledge
    {
        public Vector3 MyTownCenterPosition { get; set; }
        public Transform KnownEnemyTownCenter { get; set; }
        public float GameTimeSeconds { get; set; }

        public readonly List<ResourceNode> WoodNodes = new();
        public readonly List<ResourceNode> GoldNodes = new();
        public readonly List<ResourceNode> StoneNodes = new();
        public readonly List<ResourceNode> FoodNodes = new();

        public readonly List<Transform> VisibleHostileUnits = new();
        public readonly Dictionary<Transform, float> LastKnownEnemyPositionTime = new();
        public Transform RecentThreat { get; private set; }
        public float RecentThreatTime { get; private set; } = -999f;
        public float EstimatedEnemyMilitaryStrength { get; set; }
        public float EstimatedSelfMilitaryStrength { get; set; }

        public void ClearPerceptions()
        {
            VisibleHostileUnits.Clear();
        }

        public void RegisterThreat(Transform attacker, float now)
        {
            if (attacker == null) return;
            RecentThreat = attacker;
            RecentThreatTime = now;
        }

        public bool TryGetRecentThreat(float now, float memorySeconds, out Transform attacker)
        {
            attacker = null;
            if (RecentThreat == null) return false;
            if (now - RecentThreatTime > memorySeconds) return false;
            attacker = RecentThreat;
            return true;
        }

        public void RefreshResourceLists(Vector3 from, float maxDistance, FactionId myFaction)
        {
            WoodNodes.Clear();
            GoldNodes.Clear();
            StoneNodes.Clear();
            FoodNodes.Clear();
            float d2 = maxDistance * maxDistance;
            var nodes = Object.FindObjectsByType<ResourceNode>(FindObjectsSortMode.None);
            for (int i = 0; i < nodes.Length; i++)
            {
                var n = nodes[i];
                if (n == null || n.IsDepleted) continue;
                if ((n.transform.position - from).sqrMagnitude > d2) continue;
                switch (n.kind)
                {
                    case ResourceKind.Wood: WoodNodes.Add(n); break;
                    case ResourceKind.Gold: GoldNodes.Add(n); break;
                    case ResourceKind.Stone: StoneNodes.Add(n); break;
                    case ResourceKind.Food: FoodNodes.Add(n); break;
                }
            }
        }

        public void RefreshHostiles(FactionId myFaction, Vector3 from, float sightRadius, float memorySeconds)
        {
            ClearPerceptions();
            float sight2 = sightRadius * sightRadius;
            var members = Object.FindObjectsByType<FactionMember>(FindObjectsSortMode.None);
            float now = Time.time;
            for (int i = 0; i < members.Length; i++)
            {
                var fm = members[i];
                if (fm == null) continue;
                if (!FactionMember.IsHostile(myFaction, fm.faction)) continue;
                var t = fm.transform;
                float dist2 = (t.position - from).sqrMagnitude;
                if (dist2 <= sight2)
                {
                    if (KnownEnemyTownCenter == null && fm.GetComponentInParent<TownCenter>() != null)
                        KnownEnemyTownCenter = t;
                    var atk = fm.GetComponent<UnitAttacker>();
                    if (atk == null || atk.GetAttackDamage() <= 0) continue;
                    VisibleHostileUnits.Add(t);
                    LastKnownEnemyPositionTime[t] = now;
                }
            }

            var stale = new List<Transform>();
            foreach (var kv in LastKnownEnemyPositionTime)
            {
                if (kv.Key == null) { stale.Add(kv.Key); continue; }
                if (now - kv.Value > memorySeconds) stale.Add(kv.Key);
            }
            for (int s = 0; s < stale.Count; s++)
                LastKnownEnemyPositionTime.Remove(stale[s]);

            EstimatedEnemyMilitaryStrength = ScoreMilitary(VisibleHostileUnits);
        }

        public void EstimateSelfMilitary(IEnumerable<GameObject> units)
        {
            var list = new List<Transform>();
            foreach (var go in units)
            {
                if (go == null) continue;
                var atk = go.GetComponent<UnitAttacker>();
                if (atk == null || atk.GetAttackDamage() <= 0) continue;
                list.Add(go.transform);
            }
            EstimatedSelfMilitaryStrength = ScoreMilitary(list);
        }

        static float ScoreMilitary(List<Transform> units)
        {
            float s = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                var t = units[i];
                if (t == null) continue;
                var h = t.GetComponent<IHealth>();
                int hp = h != null && h.IsAlive ? h.CurrentHP : 0;
                var stats = t.GetComponent<UnitStatsRuntime>();
                int atk = stats != null ? stats.GetEffectiveAttack() : 5;
                s += hp * 0.02f + atk;
            }
            return s;
        }

        public ResourceNode PickNearestNeed(ResourceKind kind, Vector3 from, AIDifficultyProfile profile)
        {
            List<ResourceNode> list = kind switch
            {
                ResourceKind.Wood => WoodNodes,
                ResourceKind.Gold => GoldNodes,
                ResourceKind.Stone => StoneNodes,
                ResourceKind.Food => FoodNodes,
                _ => null
            };
            if (list == null || list.Count == 0) return null;
            ResourceNode best = null;
            float bestD = float.MaxValue;
            float jitter = profile != null ? (1.1f - profile.economicEfficiency) * 25f : 0f;
            for (int i = 0; i < list.Count; i++)
            {
                var n = list[i];
                if (n == null || n.IsDepleted) continue;
                float d = (n.transform.position - from).sqrMagnitude + Random.Range(0f, jitter);
                if (d < bestD)
                {
                    bestD = d;
                    best = n;
                }
            }
            return best;
        }
    }
}
