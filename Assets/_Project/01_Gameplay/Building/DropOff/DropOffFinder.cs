using UnityEngine;
using Project.Gameplay.Resources;

namespace Project.Gameplay.Buildings
{
    public static class DropOffFinder
    {
        public static DropOffPoint FindNearest(Vector3 from, ResourceKind kind)
        {
            var all = Object.FindObjectsByType<DropOffPoint>(FindObjectsSortMode.None);

            DropOffPoint best = null;
            float bestDist = float.MaxValue;

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
