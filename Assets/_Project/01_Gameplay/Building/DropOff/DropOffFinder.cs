using UnityEngine;
using Project.Gameplay.Resources;
using Project.Gameplay.Faction;

namespace Project.Gameplay.Buildings
{
    public static class DropOffFinder
    {
        /// <param name="factionHint">Si se indica, se prefiere un depósito del mismo bando (IA no camina al TC enemigo).</param>
        public static DropOffPoint FindNearest(Vector3 from, ResourceKind kind, FactionMember factionHint = null)
        {
            var all = Object.FindObjectsByType<DropOffPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            DropOffPoint best = null;
            float bestDist = float.MaxValue;
            int ghostLayer = LayerMask.NameToLayer("Ghost");

            for (int i = 0; i < all.Length; i++)
            {
                var d = all[i];
                if (d == null) continue;

                if (!d.isActiveAndEnabled) continue;
                if (ghostLayer != -1 && d.gameObject.layer == ghostLayer) continue;
                if (!d.Accepts(kind)) continue;

                if (factionHint != null)
                {
                    var ownerFm = d.GetComponentInParent<FactionMember>();
                    if (ownerFm != null && factionHint.IsHostileTo(ownerFm))
                        continue;
                }

                Vector3 p = d.DropPosition;
                float dist = (p - from).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = d;
                }
            }

            return best;
        }
    }
}
