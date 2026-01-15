using UnityEngine;

namespace Project.Gameplay.Buildings
{
    public class BuildingSelectable : MonoBehaviour
    {
        public Renderer[] renderers;
        public float highlightIntensity = 0.2f;
        private Color[] _base;

        void Awake()
        {
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>();

            _base = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null && renderers[i].material != null)
                    _base[i] = renderers[i].material.color;
        }

        public void SetSelected(bool selected)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || r.material == null) continue;

                var baseCol = _base[i];
                r.material.color = selected
                    ? baseCol + new Color(highlightIntensity, highlightIntensity, highlightIntensity, 0f)
                    : baseCol;
            }
        }
    }
}
