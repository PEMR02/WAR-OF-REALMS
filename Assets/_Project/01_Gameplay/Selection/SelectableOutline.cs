using UnityEngine;

namespace Project.Gameplay
{
    /// <summary>
    /// Muestra un borde/outline alrededor del objeto: selección (más marcado) o hover (más suave).
    /// Crea en runtime copias del mesh escaladas con material Cull Front.
    /// </summary>
    public class SelectableOutline : MonoBehaviour
    {
        public enum OutlineState { Off, Hover, Selected }

        [Header("Apariencia")]
        [Tooltip("Color del borde al seleccionar (más visible).")]
        public Color selectionColor = new Color(0.15f, 0.85f, 0.35f, 0.98f);
        [Tooltip("Color del borde al hacer hover (más suave).")]
        public Color hoverColor = new Color(0.4f, 0.75f, 0.4f, 0.8f);
        [Tooltip("Grosor del borde (escala del mesh; 1.1–1.15 muy visible en animales).")]
        [Range(1.02f, 1.25f)] public float outlineScale = 1.12f;

        private GameObject[] _outlineObjects;
        private Material _outlineMaterial;
        private OutlineState _state;

        void Awake()
        {
            TryCreateOutlineRenderers();
        }

        void TryCreateOutlineRenderers()
        {
            if (_outlineObjects != null && _outlineObjects.Length > 0) return;
            CreateOutlineRenderers();
        }

        void CreateOutlineRenderers()
        {
            var meshFilters = GetComponentsInChildren<MeshFilter>(true);
            var skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);

            int total = (meshFilters != null ? meshFilters.Length : 0) + (skinned != null ? skinned.Length : 0);
            if (total == 0) return;

            var shader = Shader.Find("Unlit/OutlineCullFront");
            if (shader == null) return;

            _outlineMaterial = new Material(shader);
            _outlineObjects = new GameObject[total];
            int idx = 0;

            if (meshFilters != null)
            {
                for (int i = 0; i < meshFilters.Length; i++)
                {
                    var mf = meshFilters[i];
                    if (mf == null || mf.sharedMesh == null) continue;
                    _outlineObjects[idx++] = CreateOutlineObject(mf.sharedMesh, mf.transform);
                }
            }

            if (skinned != null)
            {
                for (int i = 0; i < skinned.Length; i++)
                {
                    var smr = skinned[i];
                    if (smr == null || smr.sharedMesh == null) continue;
                    _outlineObjects[idx++] = CreateOutlineObject(smr.sharedMesh, smr.transform);
                }
            }

            if (idx < total)
            {
                var trimmed = new GameObject[idx];
                for (int i = 0; i < idx; i++) trimmed[i] = _outlineObjects[i];
                _outlineObjects = trimmed;
            }
        }

        GameObject CreateOutlineObject(Mesh mesh, Transform parentForOutline)
        {
            var go = new GameObject("Outline");
            go.transform.SetParent(parentForOutline != null ? parentForOutline : transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * outlineScale;

            var outlineMf = go.AddComponent<MeshFilter>();
            outlineMf.sharedMesh = mesh;

            var outlineMr = go.AddComponent<MeshRenderer>();
            outlineMr.sharedMaterial = _outlineMaterial;
            outlineMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outlineMr.receiveShadows = false;
            if (_outlineMaterial != null && _outlineMaterial.renderQueue < 2500)
                _outlineMaterial.renderQueue = 2500;

            go.SetActive(false);
            return go;
        }

        /// <summary>Activa borde de selección (más marcado).</summary>
        public void SetSelectionOutline(bool on)
        {
            if (on) SetState(OutlineState.Selected);
            else if (_state == OutlineState.Selected) SetState(OutlineState.Off);
        }

        /// <summary>Activa borde de hover (más suave). Solo se muestra si no hay selección.</summary>
        public void SetHoverOutline(bool on)
        {
            if (on && _state != OutlineState.Selected) SetState(OutlineState.Hover);
            else if (on == false && _state == OutlineState.Hover) SetState(OutlineState.Off);
        }

        void SetState(OutlineState state)
        {
            _state = state;
            if (state != OutlineState.Off) TryCreateOutlineRenderers();
            if (_outlineObjects == null || _outlineObjects.Length == 0) return;

            bool active = state != OutlineState.Off;
            for (int i = 0; i < _outlineObjects.Length; i++)
            {
                if (_outlineObjects[i] != null)
                    _outlineObjects[i].SetActive(active);
            }

            if (_outlineMaterial != null)
            {
                _outlineMaterial.color = state == OutlineState.Selected ? selectionColor : hoverColor;
            }
        }
    }
}
