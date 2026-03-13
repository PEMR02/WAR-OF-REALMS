using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Units
{
    /// <summary>
    /// Corrige unidades que se ven "volando": el pivote/collider está en el suelo pero el modelo está elevado.
    /// Crea un contenedor "VisualPivot" bajo el root, mueve todos los hijos ahí y aplica un offset Y negativo
    /// para que los pies coincidan con el terreno. No modifica el root (NavMeshAgent sigue correcto).
    /// </summary>
    public class UnitVisualGroundOffset : MonoBehaviour
    {
        [Tooltip("Unidades a bajar la parte visual (pies en el suelo). Típico 0.4 si el modelo flota.")]
        [Range(0f, 1f)]
        public float visualOffsetDown = 0.4f;

        void Start()
        {
            if (visualOffsetDown <= 0f || transform.childCount == 0) return;

            var pivot = new GameObject("VisualPivot");
            pivot.transform.SetParent(transform, false);
            pivot.transform.localPosition = new Vector3(0f, -visualOffsetDown, 0f);
            pivot.transform.localRotation = Quaternion.identity;
            pivot.transform.localScale = Vector3.one;

            int n = transform.childCount - 1;
            var toReparent = new List<Transform>(n);
            for (int i = 0; i < transform.childCount; i++)
            {
                var t = transform.GetChild(i);
                if (t != pivot.transform) toReparent.Add(t);
            }
            foreach (var t in toReparent)
                t.SetParent(pivot.transform, true);
        }
    }
}
