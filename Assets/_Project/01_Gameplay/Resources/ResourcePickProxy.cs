using UnityEngine;

namespace Project.Gameplay.Resources
{
    /// <summary>
    /// Punto autoritativo de pick para raycasts sobre recursos. Desacopla el click/hover
    /// de la jerarquía accidental del prefab.
    /// </summary>
    public class ResourcePickProxy : MonoBehaviour
    {
        [SerializeField] ResourceSelectable selectable;
        [SerializeField] ResourceNode node;
        [SerializeField] Collider pickCollider;
        [SerializeField] bool debugDrawBounds;

        public Collider PickCollider => pickCollider;

        public void Bind(ResourceSelectable selectableRef, ResourceNode nodeRef, Collider colliderRef)
        {
            selectable = selectableRef;
            node = nodeRef;
            pickCollider = colliderRef;
        }

        public bool TryResolve(out ResourceSelectable resolvedSelectable, out ResourceNode resolvedNode)
        {
            resolvedSelectable = selectable != null ? selectable : GetComponentInParent<ResourceSelectable>();
            resolvedNode = node != null ? node : GetComponentInParent<ResourceNode>();
            return resolvedSelectable != null && resolvedNode != null;
        }

        public bool OwnsCollider(Collider hitCollider)
        {
            if (hitCollider == null)
                return false;
            if (pickCollider == null)
                return hitCollider.transform.IsChildOf(transform);
            return hitCollider == pickCollider || hitCollider.transform.IsChildOf(pickCollider.transform);
        }

        void OnDrawGizmosSelected()
        {
            if (!debugDrawBounds || pickCollider == null)
                return;

            Gizmos.color = new Color(0.1f, 1f, 0.85f, 0.9f);
            Bounds b = pickCollider.bounds;
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
