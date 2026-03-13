using UnityEngine;
using Project.Gameplay.Map;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Spawns a ground decal quad under the building with a dirt texture and soft alpha fade at edges.
    /// Add to building prefab or BuildSite prefab. Uses buildingSO.size and MapGrid for footprint.
    /// </summary>
    public class BuildingGroundDecal : MonoBehaviour
    {
        [Header("Decal")]
        [Tooltip("Material with dirt texture; use shader 'Project/RTS Ground Decal' for edge fade.")]
        public Material dirtMaterial;
        [Tooltip("Optional: create runtime material from this texture if dirtMaterial is null.")]
        public Texture2D dirtTexture;
        [Tooltip("Height offset above terrain to avoid z-fight.")]
        public float heightOffset = 0.02f;
        [Tooltip("Extra margin around footprint (world units).")]
        public float margin = 0.2f;

        Transform _decalRoot;
        BuildingSO _buildingSO;

        void Start()
        {
            var instance = GetComponent<BuildingInstance>();
            var site = GetComponent<BuildSite>();
            if (instance != null && instance.buildingSO != null)
                _buildingSO = instance.buildingSO;
            else if (site != null && site.buildingSO != null)
                _buildingSO = site.buildingSO;
            if (_buildingSO == null) return;
            SpawnDecal();
        }

        void SpawnDecal()
        {
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady)
            {
                Invoke(nameof(SpawnDecal), 0.5f);
                return;
            }

            float cellSize = MapGrid.Instance.cellSize;
            int w = Mathf.Max(1, Mathf.RoundToInt(_buildingSO.size.x));
            int h = Mathf.Max(1, Mathf.RoundToInt(_buildingSO.size.y));
            float width = w * cellSize + margin * 2f;
            float depth = h * cellSize + margin * 2f;

            Terrain terrain = Terrain.activeTerrain != null ? Terrain.activeTerrain : FindFirstObjectByType<Terrain>();
            Vector3 center = transform.position;
            float y = center.y;
            if (terrain != null)
            {
                float avgY = 0f;
                int samples = 0;
                for (int ix = -1; ix <= 1; ix++)
                    for (int iz = -1; iz <= 1; iz++)
                    {
                        Vector3 p = center + new Vector3(ix * cellSize * 0.5f, 0f, iz * cellSize * 0.5f);
                        avgY += terrain.SampleHeight(p) + terrain.transform.position.y;
                        samples++;
                    }
                y = (avgY / samples) + heightOffset;
            }
            else
                y += heightOffset;

            _decalRoot = new GameObject("GroundDecal").transform;
            _decalRoot.SetParent(transform, false);
            _decalRoot.localPosition = new Vector3(0f, y - transform.position.y, 0f);
            _decalRoot.localRotation = Quaternion.identity;
            _decalRoot.localScale = Vector3.one;

            MeshFilter mf = _decalRoot.gameObject.AddComponent<MeshFilter>();
            MeshRenderer mr = _decalRoot.gameObject.AddComponent<MeshRenderer>();

            Mesh mesh = new Mesh { name = "DecalQuad" };
            float hw = width * 0.5f;
            float hd = depth * 0.5f;
            mesh.vertices = new Vector3[]
            {
                new Vector3(-hw, 0, -hd),
                new Vector3( hw, 0, -hd),
                new Vector3( hw, 0,  hd),
                new Vector3(-hw, 0,  hd)
            };
            mesh.uv = new Vector2[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;

            Material mat = dirtMaterial;
            if (mat == null && dirtTexture != null)
            {
                var shader = Shader.Find("Project/RTS Ground Decal");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                {
                    mat = new Material(shader);
                    mat.mainTexture = dirtTexture;
                    mat.color = new Color(0.45f, 0.35f, 0.25f, 0.9f);
                }
            }
            if (mat != null)
            {
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        void OnDestroy()
        {
            if (_decalRoot != null && _decalRoot.gameObject != null)
            {
                if (Application.isPlaying)
                    Destroy(_decalRoot.gameObject);
                else
                    DestroyImmediate(_decalRoot.gameObject);
            }
        }
    }
}
