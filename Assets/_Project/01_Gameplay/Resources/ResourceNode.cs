using UnityEngine;
using UnityEngine.AI;

namespace Project.Gameplay.Resources
{
    public enum ResourceKind { Wood, Stone, Gold, Food }

    public class ResourceNode : MonoBehaviour
    {
        public ResourceKind kind = ResourceKind.Wood;
        public int amount = 300;

        [Header("Placement")]
        [Tooltip("Auto-snap this node to the nearest NavMesh position on Awake (prevents Y-offset issues)")]
        public bool snapToNavMeshOnAwake = true;
        public float snapRadius = 3f;

        void Awake()
        {
            if (!snapToNavMeshOnAwake) return;

            if (NavMesh.SamplePosition(transform.position, out var hit, snapRadius, NavMesh.AllAreas))
            {
                transform.position = hit.position;
            }
        }

        public bool IsDepleted => amount <= 0;

        public int Take(int request)
        {
            if (amount <= 0) return 0;

            int taken = Mathf.Min(request, amount);
            amount -= taken;

            if (amount <= 0)
                Destroy(gameObject);

            return taken;
        }
    }
}
