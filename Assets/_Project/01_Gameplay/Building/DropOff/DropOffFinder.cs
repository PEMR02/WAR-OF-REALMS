using UnityEngine;
using Project.Gameplay.Resources;
using Project.Gameplay.Faction;
using Project.Gameplay.Players;

namespace Project.Gameplay.Buildings
{
    public static class DropOffFinder
    {
        /// <param name="ownerHint">Owner real esperado para depósito (prioridad económica).</param>
        /// <param name="factionHint">Compatibilidad legacy; solo se usa como fallback si no hay owner runtime.</param>
        public static DropOffPoint FindNearest(Vector3 from, ResourceKind kind, PlayerResources ownerHint = null, FactionMember factionHint = null)
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

                PlayerResources dropOwner = ResolveDropOffOwner(d);
                if (ownerHint != null)
                {
                    if (dropOwner == null || dropOwner != ownerHint)
                        continue;
                }
                else if (factionHint != null)
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

        static PlayerResources ResolveDropOffOwner(DropOffPoint dropOff)
        {
            if (dropOff == null) return null;

            var ownership = dropOff.GetComponentInParent<BuildingOwnership>();
            if (ownership != null && ownership.owner != null)
                return ownership.owner;

            var prod = dropOff.GetComponentInParent<ProductionBuilding>();
            if (prod != null && prod.owner != null)
                return prod.owner;

            return dropOff.GetComponentInParent<PlayerResources>();
        }
    }
}
