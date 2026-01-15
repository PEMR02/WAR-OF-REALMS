using UnityEngine;
using Project.Gameplay.Resources;

namespace Project.Gameplay.Buildings
{
    [System.Flags]
    public enum DropOffMask
    {
        None  = 0,
        Wood  = 1 << 0,  // 1
        Stone = 1 << 1,  // 2
        Gold  = 1 << 2,  // 4
        Food  = 1 << 3   // 8
    }

    public class DropOffPoint : MonoBehaviour
    {
        public DropOffMask accepts = DropOffMask.Wood | DropOffMask.Stone | DropOffMask.Gold | DropOffMask.Food;

        [Header("Optional anchor where villagers should deliver")]
        public Transform dropAnchor;

        public Vector3 DropPosition
        {
            get
            {
                if (dropAnchor != null) return dropAnchor.position;

                var col = GetComponent<Collider>();
                if (col != null)
                {
                    var from = transform.position + Vector3.up * 0.5f;
                    return col.ClosestPoint(from);
                }

                return transform.position;
            }
        }

        public bool Accepts(ResourceKind kind)
        {
            DropOffMask k = kind switch
            {
                ResourceKind.Wood => DropOffMask.Wood,
                ResourceKind.Stone => DropOffMask.Stone,
                ResourceKind.Gold => DropOffMask.Gold,
                ResourceKind.Food => DropOffMask.Food,
                _ => DropOffMask.None
            };

            return (accepts & k) != 0;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            int raw = (int)accepts;
            if (raw < 0 || raw > 15)
            {
                accepts = DropOffMask.Wood | DropOffMask.Stone | DropOffMask.Gold | DropOffMask.Food;
                Debug.LogWarning($"[{gameObject.name}] DropOffMask inválido. Reseteado.");
            }
        }
#endif
    }
}