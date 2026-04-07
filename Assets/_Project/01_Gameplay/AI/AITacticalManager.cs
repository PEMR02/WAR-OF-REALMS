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
        public void TickDefense(AIKnowledge k, FactionId myFaction, AIDifficultyProfile profile)
        {
            if (k == null || k.VisibleHostileUnits.Count == 0 || profile == null) return;

            Transform focus = k.VisibleHostileUnits[0];
            var friendlies = Object.FindObjectsByType<UnitAttacker>(FindObjectsSortMode.None);
            for (int i = 0; i < friendlies.Length; i++)
            {
                var atk = friendlies[i];
                if (atk == null) continue;
                var fm = atk.GetComponentInParent<FactionMember>();
                if (fm == null || fm.faction != myFaction) continue;
                if (atk.GetComponent<VillagerGatherer>() != null) continue;

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
    }
}
