using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Faction;
using Project.Gameplay.Players;
using Project.Gameplay.Units;

namespace Project.Gameplay.AI
{
    public sealed class AIScoutManager
    {
        float _nextScoutTime;
        VillagerGatherer _scout;

        public void Tick(
            Vector3 townCenter,
            PlayerResources owner,
            FactionId faction,
            AIDifficultyProfile profile,
            float gameTime)
        {
            if (profile == null || owner == null) return;
            if (Time.time < _nextScoutTime) return;
            _nextScoutTime = Time.time + Mathf.Max(0.8f, 2.2f / Mathf.Max(0.2f, profile.scoutingFrequency));

            if (_scout == null || !_scout.gameObject.activeInHierarchy || _scout.owner != owner)
                _scout = PickScout(owner, faction);

            if (_scout == null) return;
            var builder = _scout.GetComponent<Builder>();
            if (builder != null && builder.HasBuildTarget) return;
            if (!_scout.IsIdle) return;

            float radius = 35f + gameTime * 0.08f * profile.scoutingFrequency;
            radius = Mathf.Min(radius, 140f);
            float ang = Random.Range(0f, Mathf.PI * 2f);
            Vector3 target = townCenter + new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius);
            if (NavMesh.SamplePosition(target, out var hit, 25f, NavMesh.AllAreas))
                target = hit.position;
            var mover = _scout.GetComponent<UnitMover>();
            if (mover != null)
                mover.MoveTo(target);
        }

        static VillagerGatherer PickScout(PlayerResources owner, FactionId faction)
        {
            var all = Object.FindObjectsByType<VillagerGatherer>(FindObjectsSortMode.None);
            VillagerGatherer best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                var g = all[i];
                if (g == null || g.owner != owner) continue;
                var fm = g.GetComponentInParent<FactionMember>();
                if (fm == null || fm.faction != faction) continue;
                var b = g.GetComponent<Builder>();
                if (b != null && b.HasBuildTarget) continue;
                float d = g.IsIdle ? 0f : 10f;
                if (d < bestD)
                {
                    bestD = d;
                    best = g;
                }
            }
            return best;
        }
    }
}
