using UnityEngine;
using Project.Gameplay;

namespace Project.Gameplay.Buildings
{
    public class BuildingSelectable : MonoBehaviour
    {
        [Header("Visual - Selección")]
        public Renderer[] renderers;
        [Range(0f, 0.5f)] public float highlightIntensity = 0.2f;
        [Tooltip("Tinte al seleccionar (ej. cyan para feedback claro).")]
        public Color selectionTint = new Color(0.12f, 0.3f, 0.45f, 0f);
        private Color[] _base;
        private SelectableOutline _outline;

        void Awake()
        {
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>();

            _base = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null && renderers[i].material != null)
                    _base[i] = renderers[i].material.color;

            _outline = GetComponent<SelectableOutline>();
            if (_outline == null) _outline = gameObject.AddComponent<SelectableOutline>();
        }

        public void SetSelected(bool selected)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || r.material == null) continue;

                var baseCol = _base[i];
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
