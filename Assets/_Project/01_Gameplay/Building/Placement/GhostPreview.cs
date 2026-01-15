using UnityEngine;

namespace Project.Gameplay.Buildings
{
    public class GhostPreview : MonoBehaviour
    {
        [Range(0f,1f)] public float alpha = 0.35f;

        Renderer[] _renderers;
        MaterialPropertyBlock _mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Color = Shader.PropertyToID("_Color");

        public void Initialize()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _mpb = new MaterialPropertyBlock();
        }

        public void SetValid(bool valid)
        {
            if (_renderers == null || _renderers.Length == 0) return;

            // verde si válido, rojo si no
            var c = valid ? new Color(0f, 1f, 0f, alpha) : new Color(1f, 0f, 0f, alpha);

            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;

                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColor, c); // URP/Lit
                _mpb.SetColor(Color, c);     // Built-in fallback
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
