using UnityEngine;
using Project.Gameplay.Map;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Genera en runtime una base 3D baja bajo el edificio, escalada al footprint.
    /// La idea es ocultar pequeñas irregularidades del terreno, al estilo plataformas de Anno.
    /// </summary>
    public class BuildingBasePlatform : MonoBehaviour
    {
        [Tooltip("Grosor vertical de la plataforma (metros).")]
        public float thickness = 0.35f;

        [Tooltip("Margen extra alrededor del footprint (metros).")]
        public float margin = 0.3f;

        [Tooltip("Material de la plataforma (piedra/tierra). Si es null se intenta localizar MAT_RTS_GroundDecal por nombre.")]
        public Material platformMaterial;

        Transform _root;

        void Start()
        {
            TrySpawnPlatform();
        }

        void TrySpawnPlatform()
        {
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return;

            var instance = GetComponent<BuildingInstance>();
            if (instance == null || instance.buildingSO == null) return;

            var terrain = Terrain.activeTerrain ?? FindFirstObjectByType<Terrain>();
            if (terrain == null) return;

            if (platformMaterial == null)
            {
                platformMaterial = FindMaterialByName("MAT_RTS_GroundDecal");
            }
            if (platformMaterial == null) return;

            float cellSize = MapGrid.Instance.cellSize;
            float w = Mathf.Max(1, instance.buildingSO.size.x) * cellSize + margin * 2f;
            float d = Mathf.Max(1, instance.buildingSO.size.y) * cellSize + margin * 2f;

            float baseY = MapGrid.GetAreaAverageHeight(terrain, transform.position, instance.buildingSO.size);

            _root = new GameObject("BasePlatform").transform;
            _root.SetParent(transform, false);

            float halfThick = thickness * 0.5f;
            float localY = (baseY - transform.position.y) - halfThick;
            _root.localPosition = new Vector3(0f, localY, 0f);
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;

            var mf = _root.gameObject.AddComponent<MeshFilter>();
            var mr = _root.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = platformMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            mf.sharedMesh = CreateBoxMesh(w, thickness, d);
        }

        static Material FindMaterialByName(string name)
        {
            var mats = UnityEngine.Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && mats[i].name == name)
                    return mats[i];
            }
            return null;
        }

        static Mesh CreateBoxMesh(float width, float height, float depth)
        {
            var m = new Mesh { name = "BuildingBasePlatform" };

            float hw = width * 0.5f;
            float hh = height * 0.5f;
            float hd = depth * 0.5f;

            Vector3[] v =
            {
                // Top
                new Vector3(-hw,  hh, -hd),
                new Vector3( hw,  hh, -hd),
                new Vector3( hw,  hh,  hd),
                new Vector3(-hw,  hh,  hd),
                // Bottom
                new Vector3(-hw, -hh, -hd),
                new Vector3( hw, -hh, -hd),
                new Vector3( hw, -hh,  hd),
                new Vector3(-hw, -hh,  hd)
            };

            int[] tris =
            {
                // Top
                0, 2, 1, 0, 3, 2,
                // Bottom
                4, 5, 6, 4, 6, 7,
                // Front
                4, 0, 1, 4, 1, 5,
                // Back
                7, 6, 2, 7, 2, 3,
                // Left
                4, 7, 3, 4, 3, 0,
                // Right
                5, 1, 2, 5, 2, 6
            };

            Vector2[] uv =
            {
                new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1),
                new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1)
            };

            m.vertices = v;
            m.triangles = tris;
            m.uv = uv;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}

