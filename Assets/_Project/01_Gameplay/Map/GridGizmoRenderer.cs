using UnityEngine;

namespace Project.Gameplay.Map
{
    public class GridGizmoRenderer : MonoBehaviour
    {
        public float gridSize = 1f;
        public int halfSize = 50; // 50 -> 100x100 celdas
        public bool show = true;

        void OnDrawGizmos()
        {
            if (!show || gridSize <= 0.0001f) return;

            Gizmos.color = new Color(1f, 1f, 1f, 0.08f);

            for (int x = -halfSize; x <= halfSize; x++)
            {
                Vector3 a = transform.position + new Vector3(x * gridSize, 0f, -halfSize * gridSize);
                Vector3 b = transform.position + new Vector3(x * gridSize, 0f,  halfSize * gridSize);
                Gizmos.DrawLine(a, b);
            }

            for (int z = -halfSize; z <= halfSize; z++)
            {
                Vector3 a = transform.position + new Vector3(-halfSize * gridSize, 0f, z * gridSize);
                Vector3 b = transform.position + new Vector3( halfSize * gridSize, 0f, z * gridSize);
                Gizmos.DrawLine(a, b);
            }
        }
    }
}
