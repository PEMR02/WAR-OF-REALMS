using UnityEngine;
using Project.Gameplay.Faction;

namespace Project.Gameplay.AI
{
    public sealed class AIMilitaryManager
    {
        readonly AIKnowledge _knowledge;
        readonly AIDifficultyProfile _profile;
        readonly FactionId _faction;

        public AIMilitaryManager(AIKnowledge knowledge, AIDifficultyProfile profile, FactionId faction)
        {
            _knowledge = knowledge;
            _profile = profile;
            _faction = faction;
        }

        public bool ShouldAttackNow()
        {
            if (_knowledge.KnownEnemyTownCenter == null) return false;
            float self = Mathf.Max(0.5f, _knowledge.EstimatedSelfMilitaryStrength);
            float enemy = Mathf.Max(0.1f, _knowledge.EstimatedEnemyMilitaryStrength);
            float ratio = self / enemy;
            float need = _profile != null ? _profile.minAttackAdvantage : 1.15f;
            if (ratio < need) return false;
            float dist = Vector3.Distance(_knowledge.MyTownCenterPosition, _knowledge.KnownEnemyTownCenter.position);
            if (dist > 420f && _profile.aggression < 1f) return false;
            return true;
        }
    }
}
