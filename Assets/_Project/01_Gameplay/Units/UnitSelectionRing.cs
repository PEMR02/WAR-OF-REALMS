using UnityEngine;
using Project.Gameplay;
using Project.Gameplay.Map;
using Project.Gameplay.Faction;

namespace Project.Gameplay.Units
{
    /// <summary>
    /// Anillo (dona) en el suelo bajo la unidad, tipo sombra, visible solo cuando está seleccionada.
    /// Complementa la selección en unidades (que no usan outline 3D por ser skinned).
    /// </summary>
    [RequireComponent(typeof(Transform))]
    public class UnitSelectionRing : MonoBehaviour
    {
        [Header("Anillo (override si no hay SelectionOutlineConfig; si hay config se usa config → Unidades)")]
        [Tooltip("Radio exterior del anillo (unidades mundo).")]
        public float radius = 0.7f;
        [Tooltip("Radio interior (grosor del anillo).")]
        [Range(0.2f, 0.95f)] public float innerRadiusPercent = 0.65f;
        [Tooltip("Altura sobre el terreno (tipo sombra).")]
        public float heightOffset = 0.02f;
        [Tooltip("Color del anillo.")]
        public Color ringColor = new Color(0.15f, 0.85f, 0.35f, 0.6f);
        [Tooltip("Material opcional (unlit con alpha). Si null, se crea uno con el shader de decal en suelo.")]
        public Material ringMaterial;

        Transform _ringRoot;
        MeshRenderer _mr;
        Terrain _terrain;
        bool _visible;

        void Start()
        {
            _terrain = Terrain.activeTerrain != null ? Terrain.activeTerrain : FindFirstObjectByType<Terrain>();
            CreateRing();
        }

        void CreateRing()
        {
            if (SelectionOutlineConfig.Global != null)
            {
                bool hostile = FactionMember.IsHostileToPlayer(gameObject);
                var u = hostile ? SelectionOutlineConfig.Global.enemyUnits : SelectionOutlineConfig.Global.units;
                radius = u.ringRadius;
                innerRadiusPercent = u.ringInnerPercent;
                heightOffset = u.ringHeightOffset;
                float br = Mathf.Max(1f, u.ringBrightness);
                // HDR: sin clamp en RGB para que sea emisivo y se vea como el outline de edificios
                ringColor = new Color(
                    u.ringColor.r * br,
                    u.ringColor.g * br,
                    u.ringColor.b * br,
                    Mathf.Min(1f, u.ringColor.a));
            }

            _ringRoot = new GameObject("UnitSelectionRing").transform;
            _ringRoot.SetParent(transform, false);
            _ringRoot.localPosition = Vector3.zero;
            _ringRoot.localRotation = Quaternion.identity;
            _ringRoot.localScale = Vector3.one;

            var mf = _ringRoot.gameObject.AddComponent<MeshFilter>();
            _mr = _ringRoot.gameObject.AddComponent<MeshRenderer>();

            // Anillo (dona): plano en XZ, sin rotar — queda bajo la unidad tipo sombra
            float rOut = radius;
            float rIn = radius * Mathf.Clamp01(innerRadiusPercent);
            int segments = 32;
            var verts = new Vector3[segments * 2 + 2];
            var uvs = new Vector2[segments * 2 + 2];
            for (int i = 0; i <= segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                float cx = Mathf.Cos(a);
                float sz = Mathf.Sin(a);
                verts[i * 2 + 0] = new Vector3(cx * rIn, 0f, sz * rIn);
                verts[i * 2 + 1] = new Vector3(cx * rOut, 0f, sz * rOut);
                float u = (float)i / segments;
                uvs[i * 2 + 0] = new Vector2(u, 0f);
                uvs[i * 2 + 1] = new Vector2(u, 1f);
            }
            var tris = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                int o = i * 2;
                int n = ((i + 1) % (segments + 1)) * 2;
                // Winding CCW visto desde arriba (Y+) para que se vea el anillo en el suelo
                tris[i * 6 + 0] = o + 0;
                tris[i * 6 + 1] = n + 0;
                tris[i * 6 + 2] = o + 1;
                tris[i * 6 + 3] = o + 1;
                tris[i * 6 + 4] = n + 0;
                tris[i * 6 + 5] = n + 1;
            }
            var mesh = new Mesh { name = "UnitSelectionRing" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mf.sharedMesh = mesh;

            if (ringMaterial != null)
                _mr.sharedMaterial = ringMaterial;
            else
            {
                var shader = Shader.Find("Project/RTS Ground Decal");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    mat.color = ringColor;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", ringColor);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", ringColor);
                    _mr.sharedMaterial = mat;
                }
                else
                    _mr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = ringColor };
            }
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = false;
            _ringRoot.gameObject.SetActive(_visible);
        }

        void LateUpdate()
        {
            if (_ringRoot == null || !_visible) return;
            Vector3 pos = transform.position;
            float y = pos.y;
            if (_terrain != null)
                y = _terrain.SampleHeight(pos) + _terrain.transform.position.y + heightOffset;
            else
                y += heightOffset;
            _ringRoot.position = new Vector3(pos.x, y, pos.z);
            // Sin rotación: el mesh está en XZ (y=0), queda plano en el suelo tipo sombra
            _ringRoot.rotation = Quaternion.identity;
        }

        /// <summary>Muestra u oculta el círculo de selección (llamado por UnitSelectable).</summary>
        public void SetSelected(bool selected)
        {
            _visible = selected;
            if (selected && _ringRoot == null && _terrain == null)
                _terrain = Terrain.activeTerrain != null ? Terrain.activeTerrain : FindFirstObjectByType<Terrain>();
            if (selected && _ringRoot == null)
                CreateRing();
            if (_ringRoot != null)
                _ringRoot.gameObject.SetActive(selected);
        }

        void OnDestroy()
        {
            if (_ringRoot != null && _ringRoot.gameObject != null)
            {
                if (Application.isPlaying)
                    Destroy(_ringRoot.gameObject);
                else
                    DestroyImmediate(_ringRoot.gameObject);
            }
        }
    }
}
