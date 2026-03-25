using UnityEngine;
using UnityEngine.AI;
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
                return GetDropPositionFrom(transform.position + Vector3.up * 0.5f, 0);
            }
        }

        /// <summary>
        /// Devuelve un punto de entrega cercano al edificio, optimizado para la posición del aldeano.
        /// Evita que todos los aldeanos intenten el mismo punto exacto y se atasquen.
        /// </summary>
        /// <param name="fromWorld">Posición aproximada del aldeano (dirección de llegada).</param>
        /// <param name="agentSpreadId">ID estable por unidad (p. ej. <c>gameObject.GetInstanceID()</c>). Si es 0, no se aplica desfase lateral extra.</param>
        public Vector3 GetDropPositionFrom(Vector3 fromWorld, int agentSpreadId = 0)
        {
            Vector3 candidate;

            if (dropAnchor != null)
            {
                // Si hay anchor explícito, repartir con un pequeño offset radial según dirección de llegada.
                Vector3 dir = fromWorld - dropAnchor.position;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
                else dir.Normalize();
                candidate = dropAnchor.position + dir * 0.9f;
            }
            else
            {
                var col = GetComponent<Collider>();
                if (col != null)
                {
                    Vector3 p = col.ClosestPoint(fromWorld);
                    // Empujar un poco hacia afuera del collider para evitar solape exacto.
                    Vector3 outward = p - transform.position;
                    outward.y = 0f;
                    if (outward.sqrMagnitude > 0.0001f)
                        p += outward.normalized * 0.35f;
                    candidate = p;
                }
                else
                    candidate = transform.position;
            }

            // Varios aldeanos en el mismo sitio comparten la misma dirección → mismo candidato → avoidance peleándose.
            if (agentSpreadId != 0)
            {
                unchecked
                {
                    uint u = (uint)agentSpreadId * 2654435769u;
                    float ang = (u & 0xFFFFu) / 65536f * Mathf.PI * 2f;
                    candidate += new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.32f;
                }
            }

            // Huellas grandes (ej. Mining Camp 2x2): el candidato suele caer DENTRO del hueco del NavMeshObstacle.
            // El agente se queda en el borde walkable y nunca cumple distancia al punto "ideal" ? atasco con carga.
            const float sampleRadius = 6f;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                return hit.position;

            return candidate;
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