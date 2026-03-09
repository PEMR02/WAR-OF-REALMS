using UnityEngine;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Dibuja en Scene View el volumen del footprint del edificio.
    /// El transform.localScale debe ser exactamente (size.x * cellSize, altura, size.y * cellSize).
    /// Referencia visual para validar encaje con la grilla sin depender del mesh.
    /// </summary>
    public class BuildingFootprintGizmo : MonoBehaviour
    {
        [Tooltip("Color del wire en Scene View (semi-transparente para no tapar el mesh).")]
        public Color gizmoColor = new Color(0f, 1f, 0.5f, 0.25f);

        void OnDrawGizmos()
        {
            Gizmos.color = gizmoColor;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
    }
}
