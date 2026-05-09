using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Combat;
using Project.Gameplay.Faction;
using Project.Gameplay.Units;

namespace Project.Gameplay.AI
{
    /// <summary>Micro simple: retirada con poca vida; focus fire básico.</summary>
    public sealed class AITacticalManager
    {
        const float VillagerDefenseRadius = 9f;
        const float VillagerChaseLimit = 11f;
        const float BaseDefenseRadius = 34f;
        const float ThreatMemorySeconds = 15f;

        public void TickDefense(AIKnowledge k, FactionId myFaction, AIDifficultyProfile profile)
        {
            var friendlies = Object.FindObjectsByType<UnitAttacker>(FindObjectsSortMode.None);
            if (k == null || profile == null || friendlies == null || friendlies.Length == 0) return;

            Transform focus = k.VisibleHostileUnits.Count > 0 ? k.VisibleHostileUnits[0] : null;
            if (focus == null && k.TryGetRecentThreat(Time.time, ThreatMemorySeconds, out Transform recentThreat))
                focus = recentThreat;
            if (focus == null)
                focus = FindRecentAggressor(friendlies, myFaction, k.MyTownCenterPosition);
            if (focus == null)
                return;
            for (int i = 0; i < friendlies.Length; i++)
            {
                var atk = friendlies[i];
                if (atk == null) continue;
                var fm = atk.GetComponentInParent<FactionMember>();
                if (fm == null || fm.faction != myFaction) continue;
                if (atk.GetAttackDamage() <= 0) continue;
                bool isVillager = atk.GetComponent<VillagerGatherer>() != null;

                var health = atk.GetComponent<Health>();
                if (health != null && health.IsAlive && health.MaxHP > 0)
                {
                    float ratio = (float)health.CurrentHP / health.MaxHP;
                    if (ratio < Mathf.Lerp(0.45f, 0.22f, profile.tacticalSkill))
                    {
                        Vector3 away = atk.transform.position - k.MyTownCenterPosition;
                        away.y = 0f;
                        if (away.sqrMagnitude < 4f) away = (atk.transform.position - focus.position).normalized * 8f;
                        else away = away.normalized * 8f;
                        var mover = atk.GetComponent<UnitMover>();
                        if (mover != null)
                            mover.MoveTo(atk.transform.position + away);
                        atk.ClearTarget();
                        continue;
                    }
                }

                if (isVillager)
                {
                    float toBaseSqr = (atk.transform.position - k.MyTownCenterPosition).sqrMagnitude;
                    float toEnemySqr = (atk.transform.position - focus.position).sqrMagnitude;
                    if (toBaseSqr > VillagerDefenseRadius * VillagerDefenseRadius || toEnemySqr > VillagerChaseLimit * VillagerChaseLimit)
                    {
                        atk.ClearTarget();
                        continue;
                    }
                }
                else
                {
                    float toBaseSqr = (atk.transform.position - k.MyTownCenterPosition).sqrMagnitude;
                    if (toBaseSqr > BaseDefenseRadius * BaseDefenseRadius)
                        continue;
                }

                atk.SetTarget(focus);
            }
        }

        public void TickAttackMove(IReadOnlyList<UnitAttacker> army, Transform enemyTcOrFocus, AIDifficultyProfile profile)
        {
            if (enemyTcOrFocus == null || profile == null) return;
            for (int i = 0; i < army.Count; i++)
            {
                var atk = army[i];
                if (atk == null) continue;
                atk.SetTarget(enemyTcOrFocus);
                if (Random.value < 0.1f * profile.tacticalSkill)
                {
                    var mover = atk.GetComponent<UnitMover>();
                    if (mover != null)
                        mover.MoveTo(enemyTcOrFocus.position + Random.insideUnitSphere * 5f);
                }
            }
        }

        static Transform FindRecentAggressor(UnitAttacker[] friendlies, FactionId myFaction, Vector3 basePos)
        {
            float now = Time.time;
            Transform best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < friendlies.Length; i++)
            {
                var atk = friendlies[i];
                if (atk == null) continue;
                var fm = atk.GetComponentInParent<FactionMember>();
                if (fm == null || fm.faction != myFaction) continue;
                var h = atk.GetComponent<Health>();
                if (h == null || !h.IsAlive) continue;
                if (now - h.LastDamageTime > ThreatMemorySeconds) continue;
                Transform aggressor = h.LastAttackerTransform;
                if (aggressor == null) continue;
                var aggressorHealth = aggressor.GetComponentInParent<IHealth>();
                if (aggressorHealth == null || !aggressorHealth.IsAlive) continue;
                var aggressorFaction = aggressor.GetComponentInParent<FactionMember>();
                if (aggressorFaction != null && !FactionMember.IsHostile(myFaction, aggressorFaction.faction)) continue;
                float sqr = (atk.transform.position - aggressor.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = aggressor;
            }

            var allHealth = Object.FindObjectsByType<Health>(FindObjectsSortMode.None);
            for (int i = 0; i < allHealth.Length; i++)
            {
                var h = allHealth[i];
                if (h == null || !h.IsAlive) continue;
                var fm = h.GetComponentInParent<FactionMember>();
                if (fm == null || fm.faction != myFaction) continue;
                if (now - h.LastDamageTime > ThreatMemorySeconds) continue;
                Transform aggressor = h.LastAttackerTransform;
                if (aggressor == null) continue;
                var aggressorHealth = aggressor.GetComponentInParent<IHealth>();
                if (aggressorHealth == null || !aggressorHealth.IsAlive) continue;
                var aggressorFaction = aggressor.GetComponentInParent<FactionMember>();
                if (aggressorFaction != null && !FactionMember.IsHostile(myFaction, aggressorFaction.faction)) continue;
                float sqr = (basePos - aggressor.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = aggressor;
            }

            return best;
        }
    }
}
