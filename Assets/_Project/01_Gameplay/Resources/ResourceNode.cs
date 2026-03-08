using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Combat;

namespace Project.Gameplay.Resources
{
    public enum ResourceKind { Wood, Stone, Gold, Food }

    /// <summary>
    /// Nodo de recurso (árbol, piedra, oro, etc.). Implementa IWorldBarSource para mostrar barra de "cantidad restante".
    /// </summary>
    public class ResourceNode : MonoBehaviour, IWorldBarSource
    {
        public ResourceKind kind = ResourceKind.Wood;
        [Tooltip("Cantidad actual. Al llegar a 0 se destruye el nodo.")]
        public int amount = 300;
        [Tooltip("Cantidad máxima (para la barra y la UI). Si es 0, se usa el valor inicial de amount al empezar.")]
        public int maxAmount = 0;

        [Header("Placement")]
        [Tooltip("Auto-snap this node to the nearest NavMesh position on Awake (prevents Y-offset issues)")]
        public bool snapToNavMeshOnAwake = true;
        public float snapRadius = 3f;

        int _initialMax;

        void Awake()
        {
            if (maxAmount <= 0)
                _initialMax = amount;
            else
                _initialMax = maxAmount;

            if (!snapToNavMeshOnAwake) return;

            if (NavMesh.SamplePosition(transform.position, out var hit, snapRadius, NavMesh.AllAreas))
            {
                transform.position = hit.position;
            }
        }

        public int MaxAmount => maxAmount > 0 ? maxAmount : _initialMax;
        public bool IsDepleted => amount <= 0;

        // IWorldBarSource: barra de recurso restante (color por tipo: madera=marron/verde, oro=amarillo, etc.)
        public float GetBarRatio01() => MaxAmount > 0 ? Mathf.Clamp01(amount / (float)MaxAmount) : 0f;
        public Color GetBarFullColor() => GetColorForKind(kind);
        public Color GetBarEmptyColor() => new Color(0.2f, 0.2f, 0.2f, 0.9f);
        public bool IsBarVisible() => !IsDepleted;

        static Color GetColorForKind(ResourceKind k)
        {
            return k switch
            {
                ResourceKind.Wood => new Color(0.5f, 0.35f, 0.15f),
                ResourceKind.Stone => new Color(0.5f, 0.5f, 0.55f),
                ResourceKind.Gold => new Color(0.9f, 0.75f, 0.2f),
                ResourceKind.Food => new Color(0.4f, 0.7f, 0.25f),
                _ => new Color(0.5f, 0.5f, 0.5f)
            };
        }

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
