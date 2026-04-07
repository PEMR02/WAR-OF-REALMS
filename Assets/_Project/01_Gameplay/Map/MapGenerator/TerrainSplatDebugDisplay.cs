using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Planos flotantes encima del <see cref="Terrain"/> con texturas generadas desde
    /// <see cref="TerrainExporter.DebugLastMoisture01"/> / Macro / GrassDry (solo editor de depuración).
    /// </summary>
    public static class TerrainSplatDebugDisplay
    {
        const string RootName = "_TerrainSplatDebug";

        public static void Refresh(Terrain terrain, MapGenConfig config)
        {
            if (terrain == null) return;
            Transform existing = terrain.transform.Find(RootName);
            if (existing != null)
                Object.Destroy(existing.gameObject);

            bool any = config != null && (config.debugTerrainMoisture || config.debugTerrainMacro || config.debugTerrainGrassDry);
            if (!any) return;

            var root = new GameObject(RootName).transform;
            root.SetParent(terrain.transform, false);
            Vector3 size = terrain.terrainData.size;
            float yLift = size.y + 1.5f;
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Unlit/Texture")
                        ?? Shader.Find("Sprites/Default");

            int planeIndex = 0;
            void AddPlane(string name, float[,] data)
            {
                if (data == null) return;
                int h = data.GetLength(0);
                int w = data.GetLength(1);
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                for (int yy = 0; yy < h; yy++)
                {
                    for (int xx = 0; xx < w; xx++)
                    {
                        float v = Mathf.Clamp01(data[yy, xx]);
                        tex.SetPixel(xx, yy, new Color(v, v, v, 1f));
                    }
                }
                tex.Apply(false);

                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = name;
                Object.Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(root, false);
                q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                float qz = size.z * (0.12f + planeIndex * 0.30f);
                float qx = size.x * 0.12f;
                q.transform.localPosition = new Vector3(qx, yLift, qz);
                float span = Mathf.Min(size.x, size.z) * 0.26f;
                q.transform.localScale = new Vector3(span, 1f, span);
                var mr = q.GetComponent<MeshRenderer>();
                var mat = new Material(sh);
                mat.mainTexture = tex;
                mr.sharedMaterial = mat;
                planeIndex++;
            }

            if (config.debugTerrainMoisture)
                AddPlane("DebugMoisture", TerrainExporter.DebugLastMoisture01);
            if (config.debugTerrainMacro)
                AddPlane("DebugMacroNoise", TerrainExporter.DebugLastMacro01);
            if (config.debugTerrainGrassDry)
                AddPlane("DebugGrassDryMix", TerrainExporter.DebugLastGrassDryMix01);

            if (planeIndex == 0)
            {
                Object.Destroy(root.gameObject);
                return;
            }
        }
    }
}
