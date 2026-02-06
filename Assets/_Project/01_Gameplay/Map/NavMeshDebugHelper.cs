using UnityEngine;
using Unity.AI.Navigation;

namespace Project.Gameplay.Map
{
    public class NavMeshDebugHelper : MonoBehaviour
    {
        [Header("Referencias")]
        public NavMeshSurface navMeshSurface;
        public Terrain terrain;

        [Header("Debug Options")]
        public bool showDebugInfo = true;
        public Color navMeshColor = new Color(0, 1, 0, 0.3f);

        void Start()
        {
            if (navMeshSurface == null)
                navMeshSurface = FindFirstObjectByType<NavMeshSurface>();

            if (terrain == null)
                terrain = FindFirstObjectByType<Terrain>();

            if (showDebugInfo)
                InvokeRepeating(nameof(LogNavMeshStatus), 2f, 5f);
        }

        void LogNavMeshStatus()
        {
            if (navMeshSurface == null) return;

            Debug.Log("=== NavMesh Status ===");
            Debug.Log($"NavMesh Surface asignado: {navMeshSurface != null}");
            Debug.Log($"NavMesh Data: {navMeshSurface.navMeshData != null}");
            
            if (navMeshSurface.navMeshData != null)
            {
                var bounds = navMeshSurface.navMeshData.sourceBounds;
                Debug.Log($"NavMesh Bounds: center={bounds.center}, size={bounds.size}");
            }

            if (terrain != null)
            {
                Debug.Log($"Terrain Collider: {terrain.GetComponent<TerrainCollider>() != null}");
            }
        }

        [ContextMenu("Rebuild NavMesh Now")]
        void RebuildNavMesh()
        {
            if (navMeshSurface == null)
            {
                Debug.LogError("No hay NavMeshSurface asignado");
                return;
            }

            Debug.Log("Reconstruyendo NavMesh...");
            navMeshSurface.BuildNavMesh();
            Debug.Log("NavMesh reconstruido");
        }

        [ContextMenu("Clear NavMesh")]
        void ClearNavMesh()
        {
            if (navMeshSurface == null)
            {
                Debug.LogError("No hay NavMeshSurface asignado");
                return;
            }

            navMeshSurface.RemoveData();
            Debug.Log("NavMesh limpiado");
        }

        void OnDrawGizmosSelected()
        {
            if (navMeshSurface == null || navMeshSurface.navMeshData == null)
                return;

            Gizmos.color = navMeshColor;
            var bounds = navMeshSurface.navMeshData.sourceBounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
