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
            public bool hasGroundHit;
            public Vector3 groundPosition;
        }

        /// <summary>
        /// Orden de raycast: BuildSite → Resource → Building → Ground.
        /// </summary>
        public static ResolveResult Resolve(Ray ray, LayerMask buildSiteMask, LayerMask resourceMask, LayerMask buildingMask, LayerMask groundMask)
        {
            var result = new ResolveResult { type = TargetType.None };
            RaycastHit[] hits = Physics.RaycastAll(ray, 5000f, buildSiteMask | resourceMask | buildingMask | groundMask);
            if (hits == null || hits.Length == 0)
                return result;

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            RaycastHit bestBuildSite = default;
            bool hasBuildSite = false;
            RaycastHit bestResource = default;
            bool hasResource = false;
            ResourceNode resolvedResourceNode = null;
            RaycastHit bestBuilding = default;
            bool hasBuilding = false;
            RaycastHit bestGround = default;
            bool hasGround = false;

            if (ResourcePickResolver.TryResolveTopmostResourceUnderCursor(ray, resourceMask, out ResourceSelectable _, out ResourceNode resourceNode, out RaycastHit resourceHit))
            {
                bestResource = resourceHit;
                hasResource = true;
                resolvedResourceNode = resourceNode;
            }

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null || hit.collider.isTrigger) continue;

                int hitBit = 1 << hit.collider.gameObject.layer;
                if (!hasBuildSite && (buildSiteMask.value & hitBit) != 0)
                {
                    var site = hit.collider.GetComponentInParent<BuildSite>();
                    if (site != null)
                    {
                        bestBuildSite = hit;
                        hasBuildSite = true;
                    }
                }

                if (!hasBuilding && buildingMask != 0 && (buildingMask.value & hitBit) != 0)
                {
                    if (hit.collider.GetComponentInParent<UnitMover>() == null)
                    {
                        bestBuilding = hit;
                        hasBuilding = true;
                    }
                }

                if (!hasGround && (groundMask.value & hitBit) != 0)
                {
                    bestGround = hit;
                    hasGround = true;
                }
            }

            if (hasGround)
            {
                result.hasGroundHit = true;
                result.groundPosition = bestGround.point;
            }

            if (hasBuildSite)
            {
                result.type = TargetType.BuildSite;
                result.hit = bestBuildSite;
                result.buildSite = bestBuildSite.collider.GetComponentInParent<BuildSite>();
                return result;
            }

            if (hasResource)
            {
                result.type = TargetType.Resource;
                result.hit = bestResource;
                result.resourceNode = resolvedResourceNode;
                return result;
            }

            if (hasBuilding)
            {
                // Colliders de obra (p. ej. muro compuesto) a veces solo están en buildingMask: sin esto el dispatch
                // trata el clic como "edificio" y limpia SetBuildTarget + solo MoveCommand → HasBuildTarget false.
                BuildSite siteUnderConstruction = bestBuilding.collider.GetComponentInParent<BuildSite>();
                if (siteUnderConstruction != null && !siteUnderConstruction.IsCompleted)
                {
                    result.type = TargetType.BuildSite;
                    result.hit = bestBuilding;
                    result.buildSite = siteUnderConstruction;
                    return result;
                }

                result.dropOffPoint = bestBuilding.collider.GetComponentInParent<DropOffPoint>();
                result.buildingHealth = bestBuilding.collider.GetComponentInParent<Health>();
                var buildingRoot = bestBuilding.collider.transform.root;
                result.buildingPosition = buildingRoot != null ? buildingRoot.position : bestBuilding.point;

                bool isActionableBuilding =
                    result.dropOffPoint != null ||
                    (result.buildingHealth != null && result.buildingHealth.IsAlive && result.buildingHealth.CurrentHP < result.buildingHealth.MaxHP);

                if (isActionableBuilding || !hasGround)
                {
                    result.type = TargetType.Building;
                    result.hit = bestBuilding;
                    return result;
                }
            }

            if (hasGround)
            {
                result.type = TargetType.Ground;
                result.hit = bestGround;
            }

            return result;
        }
    }
}
