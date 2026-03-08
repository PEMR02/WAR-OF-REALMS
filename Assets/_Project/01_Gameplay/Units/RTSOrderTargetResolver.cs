using UnityEngine;
using Project.Gameplay.Buildings;
using Project.Gameplay.Resources;
using Project.Gameplay.Combat;

namespace Project.Gameplay.Units
{
    /// <summary>
    /// Resuelve qué tipo de objetivo se ha clickeado con el rayo (BuildSite, recurso, edificio, suelo).
    /// Separa la resolución del objetivo del dispatch de órdenes.
    /// </summary>
    public static class RTSOrderTargetResolver
    {
        public enum TargetType
        {
            None,
            BuildSite,
            Resource,
            Building,
            Ground
        }

        public struct ResolveResult
        {
            public TargetType type;
            public RaycastHit hit;
            public BuildSite buildSite;
            public ResourceNode resourceNode;
            public DropOffPoint dropOffPoint;
            public Health buildingHealth;
            public Vector3 buildingPosition;
        }

        /// <summary>
        /// Orden de raycast: BuildSite → Resource → Building → Ground.
        /// </summary>
        public static ResolveResult Resolve(Ray ray, LayerMask buildSiteMask, LayerMask resourceMask, LayerMask buildingMask, LayerMask groundMask)
        {
            var result = new ResolveResult { type = TargetType.None };

            if (Physics.Raycast(ray, out RaycastHit hitS, 5000f, buildSiteMask))
            {
                var site = hitS.collider.GetComponentInParent<BuildSite>();
                if (site != null)
                {
                    result.type = TargetType.BuildSite;
                    result.hit = hitS;
                    result.buildSite = site;
                    return result;
                }
            }

            if (Physics.Raycast(ray, out RaycastHit hitR, 5000f, resourceMask))
            {
                var node = hitR.collider.GetComponentInParent<ResourceNode>();
                if (node != null)
                {
                    result.type = TargetType.Resource;
                    result.hit = hitR;
                    result.resourceNode = node;
                    return result;
                }
            }

            if (buildingMask != 0 && Physics.Raycast(ray, out RaycastHit hitB, 5000f, buildingMask))
            {
                result.dropOffPoint = hitB.collider.GetComponentInParent<DropOffPoint>();
                result.buildingHealth = hitB.collider.GetComponentInParent<Health>();
                var buildingRoot = hitB.collider.GetComponentInParent<UnitMover>() != null ? null : hitB.collider.transform.root;
                result.buildingPosition = buildingRoot != null ? buildingRoot.position : hitB.point;
                result.type = TargetType.Building;
                result.hit = hitB;
                return result;
            }

            if (Physics.Raycast(ray, out RaycastHit hitG, 5000f, groundMask))
            {
                result.type = TargetType.Ground;
                result.hit = hitG;
                return result;
            }

            return result;
        }
    }
}
