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
            var all = Object.FindObjectsByType<DropOffPoint>(FindObjectsSortMode.None);

            DropOffPoint bestFriendly = null;
            float bestFriendlyDist = float.MaxValue;
            DropOffPoint bestAny = null;
            float bestAnyDist = float.MaxValue;

            int ghostLayer = LayerMask.NameToLayer("Ghost");

            for (int i = 0; i < all.Length; i++)
            {
                var d = all[i];
                if (d == null) continue;

                // Filtrar antes de evaluar
                if (!d.isActiveAndEnabled) continue;
                if (ghostLayer != -1 && d.gameObject.layer == ghostLayer) continue;

                if (!d.Accepts(kind)) continue;

                Vector3 p = d.DropPosition;
                float dist = (p - from).sqrMagnitude;

                if (dist < bestAnyDist)
                {
                    bestAnyDist = dist;
                    bestAny = d;
                }

                if (factionHint == null) continue;
                var ownerFm = d.GetComponentInParent<FactionMember>();
                if (ownerFm != null && ownerFm.faction == factionHint.faction && dist < bestFriendlyDist)
                {
                    bestFriendlyDist = dist;
                    bestFriendly = d;
                }
            }

            return bestFriendly != null ? bestFriendly : bestAny;
        }
    }
}
