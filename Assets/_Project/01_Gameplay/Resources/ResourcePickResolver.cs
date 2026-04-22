using UnityEngine;

namespace Project.Gameplay.Resources
{
    public static class ResourcePickResolver
    {
        public static bool TryResolveTopmostResourceUnderCursor(
            Ray ray,
            LayerMask resourceLayerMask,
            out ResourceSelectable resource,
            out ResourceNode node,
            out RaycastHit hit,
            bool debugLogs = false)
        {
            resource = null;
            node = null;
            hit = default;

            if (resourceLayerMask.value == 0)
                return false;

            RaycastHit[] hits = Physics.RaycastAll(ray, 5000f, resourceLayerMask, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0)
                return false;

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            bool sawAnyHit = false;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider col = hits[i].collider;
                if (col == null)
                    continue;
                sawAnyHit = true;
                if (col.isTrigger)
                    continue;

                if (!TryResolveResourceHit(col, out resource, out node))
                    continue;

                hit = hits[i];
                return true;
            }

            if (debugLogs && sawAnyHit)
                Debug.Log("[ResourcePick] Hay hits en resourceLayerMask pero ninguno resolvió un ResourceNode válido.");

            resource = null;
            node = null;
            hit = default;
            return false;
        }

        public static bool TryResolveResourceHit(Collider collider, out ResourceSelectable resource, out ResourceNode node)
        {
            resource = null;
            node = null;
            if (collider == null)
                return false;

            node = collider.GetComponentInParent<ResourceNode>();
            if (node == null)
                return false;

            ResourcePickProxy proxy = node.GetComponentInChildren<ResourcePickProxy>(true);
            if (proxy != null)
            {
                if (!proxy.OwnsCollider(collider))
                {
                    // Regla oficial: cuando existe proxy, éste define el pick autoritativo.
                    // Si el raycast golpea otro collider del mismo recurso (compatibilidad), reencauzamos
                    // al selectable/node del proxy en lugar de descartar el hit.
                    if (!proxy.TryResolve(out resource, out node))
                        return false;
                    return resource != null && node != null;
                }

                if (!proxy.TryResolve(out resource, out node))
                    return false;

                return resource != null && node != null;
            }

            resource = collider.GetComponentInParent<ResourceSelectable>();
            if (resource == null)
                return false;

            node = resource.GetResourceNode();
            return node != null;
        }
    }
}
