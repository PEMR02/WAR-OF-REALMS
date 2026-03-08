using UnityEngine;
using UnityEngine.Rendering;

namespace Project.Gameplay.Resources
{
    /// <summary>
    /// Objetos que pueden atenuarse cuando tapan la cámara (ej. árboles).
    /// Alpha = transparencia (por defecto, para ver unidades/edificios). Scale = reducir tamaño.
    /// </summary>
    public class FadeableByCamera : MonoBehaviour
    {
        public enum FadeMode { Alpha, Scale }

        [Tooltip("Escala mínima cuando está atenuado (solo en modo Scale).")]
        [Range(0.1f, 0.5f)] public float minScaleWhenFaded = 0.3f;
        [Tooltip("Velocidad de transición hacia faded/unfaded.")]
        public float fadeSpeed = 8f;
        [Tooltip("Alpha = transparencia (ver a través). Scale = reducir tamaño.")]
        public FadeMode mode = FadeMode.Alpha;
        [Tooltip("Alpha mínima cuando está atenuado (0.2–0.35 para ver unidades pero que el árbol se distinga).")]
        [Range(0.08f, 0.5f)] public float minAlphaWhenFaded = 0.28f;

        [Header("Borde cuando está atenuado")]
        [Tooltip("Si true, al atenuar se muestra un borde suave para que se aprecie que hay un objeto.")]
        public bool showOutlineWhenFaded = true;
        [Tooltip("Color del borde (verde bosque; alfa = suavidad).")]
        public Color outlineColorWhenFaded = new Color(0.12f, 0.45f, 0.15f, 0.78f);
        [Tooltip("Grosor del borde al atenuar (más alto = más visible).")]
        [Range(1.02f, 1.15f)] public float outlineScale = 1.07f;

        private Vector3 _originalScale;
        private float _currentFade = 1f;
        private float _targetFade = 1f;
        private Renderer[] _renderers;
        private Color[] _originalColors;
        private Material[] _instancedMaterials;
        private UnityEngine.Rendering.ShadowCastingMode[] _originalShadowMode;
        private bool _initialized;
        private bool _transparentModeActive;
        private GameObject[] _outlineObjects;
        private Material _outlineMaterial;

        void Awake()
        {
            _originalScale = transform.localScale;
            _renderers = GetComponentsInChildren<Renderer>(true);
            if (_renderers != null && _renderers.Length > 0)
            {
                _originalColors = new Color[_renderers.Length];
                _instancedMaterials = new Material[_renderers.Length];
                _originalShadowMode = new UnityEngine.Rendering.ShadowCastingMode[_renderers.Length];
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] != null)
                    {
                        _originalShadowMode[i] = _renderers[i].shadowCastingMode;
                        if (_renderers[i].sharedMaterial != null)
                        {
                            _instancedMaterials[i] = _renderers[i].material;
                            if (_instancedMaterials[i].HasProperty("_BaseColor"))
                                _originalColors[i] = _instancedMaterials[i].GetColor("_BaseColor");
                            else if (_instancedMaterials[i].HasProperty("_Color"))
                                _originalColors[i] = _instancedMaterials[i].GetColor("_Color");
                            else
                                _originalColors[i] = Color.white;
                        }
                    }
                }
            }

            if (showOutlineWhenFaded && mode == FadeMode.Alpha)
                CreateOutlineRenderers();

            _initialized = true;
        }

        void CreateOutlineRenderers()
        {
            var meshFilters = GetComponentsInChildren<MeshFilter>(true);
            if (meshFilters == null || meshFilters.Length == 0) return;

            var shader = Shader.Find("Unlit/OutlineCullFront");
            if (shader == null) return;

            _outlineMaterial = new Material(shader) { color = outlineColorWhenFaded };
            _outlineObjects = new GameObject[meshFilters.Length];

            for (int i = 0; i < meshFilters.Length; i++)
            {
                var mf = meshFilters[i];
                if (mf == null || mf.sharedMesh == null) continue;

                var go = new GameObject("FadeOutline_" + i);
                go.transform.SetParent(mf.transform, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one * outlineScale;

                var outlineMf = go.AddComponent<MeshFilter>();
                outlineMf.sharedMesh = mf.sharedMesh;

                var outlineMr = go.AddComponent<MeshRenderer>();
                outlineMr.sharedMaterial = _outlineMaterial;
                outlineMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outlineMr.receiveShadows = false;

                go.SetActive(false);
                _outlineObjects[i] = go;
            }
        }

        /// <summary>Objetivo: 1 = visible normal, 0 = atenuado. El componente sigue interpolando cada frame hasta alcanzarlo (así al alejarse la cámara recuperan la opacidad).</summary>
        public void SetFadeTarget(float target)
        {
            _targetFade = Mathf.Clamp01(target);
        }

        void LateUpdate()
        {
            if (!_initialized || _renderers == null) return;

            if (mode == FadeMode.Scale)
            {
                float scaleMul = Mathf.Lerp(minScaleWhenFaded, 1f, _targetFade);
                float currentMul = _originalScale.x > 0.001f ? transform.localScale.x / _originalScale.x : 1f;
                float newMul = Mathf.MoveTowards(currentMul, scaleMul, fadeSpeed * Time.deltaTime);
                transform.localScale = _originalScale * newMul;
                return;
            }

            _currentFade = Mathf.MoveTowards(_currentFade, _targetFade, fadeSpeed * Time.deltaTime);
            float a = Mathf.Lerp(minAlphaWhenFaded, 1f, _currentFade);
            bool wantTransparent = a < 0.99f;
            if (wantTransparent != _transparentModeActive)
            {
                _transparentModeActive = wantTransparent;
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] != null)
                        _renderers[i].shadowCastingMode = wantTransparent ? UnityEngine.Rendering.ShadowCastingMode.Off : _originalShadowMode[i];
                    if (_instancedMaterials[i] != null)
                        SetMaterialTransparent(_instancedMaterials[i], wantTransparent);
                }

                if (_outlineObjects != null)
                {
                    for (int i = 0; i < _outlineObjects.Length; i++)
                        if (_outlineObjects[i] != null)
                            _outlineObjects[i].SetActive(wantTransparent);
                }
                if (wantTransparent && _outlineMaterial != null)
                    _outlineMaterial.color = outlineColorWhenFaded;
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null || _instancedMaterials[i] == null) continue;
                Material mat = _instancedMaterials[i];
                Color c = _originalColors[i];
                c.a = a;
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", c);
                else if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", c);
            }
        }

        static void SetMaterialTransparent(Material mat, bool transparent)
        {
            if (transparent)
            {
                if (mat.HasProperty("_Surface"))
                    mat.SetFloat("_Surface", 1f);
                if (mat.HasProperty("_Blend"))
                    mat.SetFloat("_Blend", 0f);
                if (mat.HasProperty("_AlphaClip"))
                    mat.SetFloat("_AlphaClip", 0f);
                if (mat.HasProperty("_ZWrite"))
                    mat.SetFloat("_ZWrite", 0f);
                if (mat.HasProperty("_SrcBlend"))
                    mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend"))
                    mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.renderQueue = 3000;
                if (mat.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") == false)
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (mat.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") == false)
                    mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            else
            {
                if (mat.HasProperty("_Surface"))
                    mat.SetFloat("_Surface", 0f);
                if (mat.HasProperty("_ZWrite"))
                    mat.SetFloat("_ZWrite", 1f);
                if (mat.HasProperty("_SrcBlend"))
                    mat.SetInt("_SrcBlend", (int)BlendMode.One);
                if (mat.HasProperty("_DstBlend"))
                    mat.SetInt("_DstBlend", (int)BlendMode.Zero);
                mat.renderQueue = 2000;
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
        }
    }
}
