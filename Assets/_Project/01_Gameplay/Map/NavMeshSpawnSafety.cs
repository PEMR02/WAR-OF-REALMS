using UnityEngine;
using UnityEngine.AI;

namespace Project.Gameplay.Map
{
    /// <summary>
    /// Evita que instancias con <see cref="NavMeshAgent"/> habilitado disparen
    /// "Failed to create agent because it is not close enough to the NavMesh" antes del bake.
    /// </summary>
    public static class NavMeshSpawnSafety
    {
        /// <summary>Desactiva todos los NavMeshAgent en la jerarquía (incluye raíz).</summary>
        public static void DisableNavMeshAgentsOnHierarchy(GameObject root)
        {
            if (root == null) return;
            var agents = root.GetComponentsInChildren<NavMeshAgent>(true);
            for (int i = 0; i < agents.Length; i++)
            {
                if (agents[i] != null)
                    agents[i].enabled = false;
            }
        }
    }
}
