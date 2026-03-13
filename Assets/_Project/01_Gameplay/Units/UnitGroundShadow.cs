using UnityEngine;
using Project.Gameplay.Map;

namespace Project.Gameplay.Units
{
    /// <summary>
    /// Spawns a small circular shadow under the unit. Scale based on unit size, stays aligned with terrain.
    /// Prevents units from looking like they float.
    /// </summary>
    [RequireComponent(typeof(Transform))]
    public class UnitGroundShadow : MonoBehaviour
    {
        [Header("Shadow")]
        [Tooltip("Radius of the shadow circle (world units).")]
        public float radius = 0.5f;
        [Tooltip("Height offset above terrain to avoid z-fight.")]
        public float heightOffset = 0.02f;
        [Tooltip("Material for the shadow (unlit, alpha). If null, a default dark quad is used.")]
        public Material shadowMaterial;

        Transform _shadowRoot;
        MeshFilter _mf;
        MeshRenderer _mr;
        Terrain _terrain;

        void Start()
        {
            _terrain = Terrain.activeTerrain != null ? Terrain.activeTerrain : FindFirstObjectByType<Terrain>();
            CreateShadow();
        }

        void CreateShadow()
        {
            _shadowRoot = new GameObject("UnitGroundShadow").transform;
            _shadowRoot.SetParent(transform, false);
            _shadowRoot.localPosition = Vector3.zero;
            _shadowRoot.localRotation = Quaternion.identity;
            _shadowRoot.localScale = Vector3.one;

            _mf = _shadowRoot.gameObject.AddComponent<MeshFilter>();
            _mr = _shadowRoot.gameObject.AddComponent<MeshRenderer>();

            int segments = 16;
            var verts = new Vector3[segments + 1];
            var uvs = new Vector2[segments + 1];
            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
                uvs[i + 1] = new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a));
            }
            var tris = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                tris[i * 3 + 0] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segments + 1;
            }
            var mesh = new Mesh { name = "UnitShadow" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            _mf.sharedMesh = mesh;

            if (shadowMaterial != null)
                _mr.sharedMaterial = shadowMaterial;
            else
            {
                var shader = Shader.Find("Project/RTS Ground Decal");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    mat.color = new Color(0, 0, 0, 0.4f);
                    _mr.sharedMaterial = mat;
                }
            }
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = false;
        }

        void LateUpdate()
        {
            if (_shadowRoot == null) return;
            Vector3 pos = transform.position;
            float y = pos.y;
            if (_terrain != null)
                y = _terrain.SampleHeight(pos) + _terrain.transform.position.y + heightOffset;
            else
                y += heightOffset;
            _shadowRoot.position = new Vector3(pos.x, y, pos.z);
            _shadowRoot.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        void OnDestroy()
        {
            if (_shadowRoot != null && _shadowRoot.gameObject != null)
            {
                if (Application.isPlaying)
                    Destroy(_shadowRoot.gameObject);
                else
                    DestroyImmediate(_shadowRoot.gameObject);
            }
        }
    }
}
