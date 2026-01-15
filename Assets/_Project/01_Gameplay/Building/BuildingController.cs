using UnityEngine;
using UnityEngine.AI;

namespace Project.Gameplay.Buildings
{
    [RequireComponent(typeof(Collider))]
    public class BuildingController : MonoBehaviour
    {
        [Tooltip("Activa si quieres que el edificio carve el NavMesh en runtime.")]
        public bool carveNavMesh = true;

        void Awake()
        {
            if (carveNavMesh)
            {
                var obs = GetComponent<NavMeshObstacle>();
                if (obs == null) obs = gameObject.AddComponent<NavMeshObstacle>();
                obs.carving = true;
                obs.carveOnlyStationary = true;
            }
        }
    }
}
