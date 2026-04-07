using UnityEngine;
using Project.Gameplay;

namespace Project.Gameplay.Resources
{
    /// <summary>
    /// Marca un recurso como seleccionable (clic). El generador de mapa lo añade junto con ResourceNode.
    /// Opcional: highlight visual al seleccionar; borde al hacer hover (con aldeano seleccionado).
    /// </summary>
    public class ResourceSelectable : MonoBehaviour
    {
        [Header("Visual - Selección")]
        [Tooltip("Renderers a iluminar al seleccionar. Vacío = todos los hijos.")]
        public Renderer[] renderers;
        [Tooltip("Si true, además del outline se altera el color del material al seleccionar. Útil para animales; para árboles suele verse como un 'blob' verde.")]
        public bool useMaterialHighlight = false;
        [Tooltip("Brillo extra al seleccionar (solo si useMaterialHighlight = true). 0.3–0.6 para que se note bien en animales.")]
        [Range(0f, 0.8f)] public float highlightIntensity = 0.0f;
        [Tooltip("Tint extra al seleccionar (solo si useMaterialHighlight = true).")]
        public Color selectionTint = new Color(0f, 0f, 0f, 0f);
        [Tooltip("Si el material lo soporta, añade un poco de emisión para que 'brille' más (solo si useMaterialHighlight = true).")]
        [Range(0f, 0.4f)] public float selectionEmission = 0.0f;

        private Color[] _baseColors;
        private SelectableOutline _outline;

        void Awake()
        {
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>(true);

            if (renderers == null || renderers.Length == 0)
            {
                var skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (skinned != null && skinned.Length > 0)
                    renderers = skinned;
            }

            if (renderers == null) renderers = new Renderer[0];
            _baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].material != null)
                    _baseColors[i] = GetMaterialColor(renderers[i].material);
            }

            _outline = GetComponent<SelectableOutline>();
            if (_outline == null) _outline = gameObject.AddComponent<SelectableOutline>();
        }

        static Color GetMaterialColor(Material mat)
        {
            if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
            if (mat.HasProperty("_Color")) return mat.GetColor("_Color");
            return Color.white;
        }

        static void SetMaterialColor(Material mat, Color c)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }

        public void SetSelected(bool selected)
        {
            if (useMaterialHighlight)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null) continue;
                    var mat = r.material;
                    if (mat == null) continue;

                    var baseCol = i < _baseColors.Length ? _baseColors[i] : GetMaterialColor(mat);
                    if (selected)
                    {
                        Color c = baseCol + new Color(highlightIntensity, highlightIntensity, highlightIntensity, 0f) + selectionTint;
                        c = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), baseCol.a);
                        SetMaterialColor(mat, c);
                        if (selectionEmission > 0.001f && mat.HasProperty("_EmissionColor"))
                        {
                            mat.SetColor("_EmissionColor", c * selectionEmission);
                            if (!mat.IsKeywordEnabled("_EMISSION")) mat.EnableKeyword("_EMISSION");
                        }
                    }
                    else
                    {
                        SetMaterialColor(mat, baseCol);
                        if (mat.HasProperty("_EmissionColor"))
                        {
                            mat.SetColor("_EmissionColor", Color.black);
                            if (mat.IsKeywordEnabled("_EMISSION")) mat.DisableKeyword("_EMISSION");
                        }
                    }
                }
            }

            if (_outline != null) _outline.SetSelectionOutline(selected);
        }

        /// <summary>Borde suave al pasar el mouse (con aldeano seleccionado).</summary>
        public void SetHovered(bool hovered)
        {
            if (_outline != null) _outline.SetHoverOutline(hovered);
        }

        /// <summary>Obtiene el nodo de recurso en este objeto (cantidad, tipo, etc.).</summary>
        public ResourceNode GetResourceNode() => GetComponentInParent<ResourceNode>();
    }
}
