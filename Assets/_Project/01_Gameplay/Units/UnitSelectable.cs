using UnityEngine;
using Project.Gameplay;

namespace Project.Gameplay.Units
{
    public class UnitSelectable : MonoBehaviour
    {
        [Header("Visual - Selección")]
        [Tooltip("Renderers a iluminar al seleccionar. Vacío = todos los hijos.")]
        public Renderer[] renderers;
        [Tooltip("Brillo adicional al seleccionar (blanco).")]
        [Range(0f, 0.5f)] public float highlightIntensity = 0.2f;
        [Tooltip("Tinte de color al seleccionar (ej. cyan para que se note el outline).")]
        public Color selectionTint = new Color(0.15f, 0.35f, 0.5f, 0f);

        private Color[] _baseColors;
        private SelectableOutline _outline;

        void OnEnable()
        {
            UnitSelectableRegistry.Register(this);
        }

        void OnDisable()
        {
            UnitSelectableRegistry.Unregister(this);
        }

        void Awake()
        {
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>();

            _baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].material != null)
                    _baseColors[i] = renderers[i].material.color;
            }

            _outline = GetComponent<SelectableOutline>();
            if (_outline == null) _outline = gameObject.AddComponent<SelectableOutline>();
        }

        public void SetSelected(bool selected)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || r.material == null) continue;

                var baseCol = _baseColors[i];
                if (selected)
                {
                    Color c = baseCol + new Color(highlightIntensity, highlightIntensity, highlightIntensity, 0f) + selectionTint;
                    r.material.color = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), baseCol.a);
                }
                else
                    r.material.color = baseCol;
            }

            if (_outline != null) _outline.SetSelectionOutline(selected);
        }
    }
}
