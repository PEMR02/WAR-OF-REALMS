using UnityEngine;

namespace Project.Gameplay.Units
{
    public class UnitSelectable : MonoBehaviour
    {
        [Header("Visual")]
        public Renderer[] renderers;
        public float highlightIntensity = 0.25f;

        private Color[] _baseColors;

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
        }

        public void SetSelected(bool selected)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || r.material == null) continue;

                var baseCol = _baseColors[i];
                r.material.color = selected
                    ? baseCol + new Color(highlightIntensity, highlightIntensity, highlightIntensity, 0f)
                    : baseCol;
            }
        }
    }
}
