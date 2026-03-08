using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Genera las 4 paredes laterales + base plana que dan volumen al Unity Terrain.
    /// Todos los vértices se crean en ESPACIO LOCAL del Terrain (sin dependencia de su
    /// posición en el mundo), lo que evita cualquier desfase al regenerar el mapa.
    ///
    /// Convención de ejes locales del Terrain:
    ///   X : 0 … terrainData.size.x  (= gridW * cellSize)
    ///   Y : 0 … terrainData.size.y  (altura máxima del terreno)
    ///   Z : 0 … terrainData.size.z  (= gridH * cellSize)
    ///
    /// El skirt añade Y negativo: de 0 hasta -skirtDepth.
    /// </summary>
    public static class TerrainSkirtBuilder
    {
        const string SkirtRootName = "TerrainSkirt";

        // ─────────────────────────────────────────────────────────────
        // API pública
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Destruye el skirt previo y crea uno nuevo.
        /// <paramref name="hmHeights"/> es el mismo float[res,res] que se pasó a
        /// TerrainData.SetHeights (valores en [0..1]). Se usa para muestrear la
        /// altura del borde sin depender de terrain.SampleHeight().
        /// </summary>
        public static GameObject BuildSkirt(Terrain terrain, MapGenConfig config,
                                            float[,] hmHeights = null)
        {
            if (terrain == null || config == null || !config.showTerrainSkirt) return null;

            DestroyExisting(terrain.transform);

            TerrainData td  = terrain.terrainData;
            float tw        = td.size.x;   // ancho  local (X)
            float th        = td.size.y;   // altura local (Y)
            float tl        = td.size.z;   // largo  local (Z)
            float depth     = Mathf.Max(1f, config.skirtDepth);
            int   samples   = Mathf.Clamp(config.skirtEdgeSamples, 8, 512);
            int   hmRes     = hmHeights != null ? hmHeights.GetLength(0) : 0;

            // Función que devuelve la altura LOCAL Y en [0..th] dado (u,v) ∈ [0..1]
            // u → eje X del terreno, v → eje Z del terreno
            float HeightAt(float u, float v)
            {
                if (hmHeights != null && hmRes > 1)
                {
                    // heights[row, col] donde row=v, col=u en Unity
                    int col = Mathf.Clamp(Mathf.RoundToInt(u * (hmRes - 1)), 0, hmRes - 1);
                    int row = Mathf.Clamp(Mathf.RoundToInt(v * (hmRes - 1)), 0, hmRes - 1);
                    return hmHeights[row, col] * th;
                }
                // Fallback: SampleHeight devuelve altura local [0..th]
                float wx = terrain.transform.position.x + u * tw;
                float wz = terrain.transform.position.z + v * tl;
                return terrain.SampleHeight(new Vector3(wx, 0f, wz));
            }

            Material mat = ResolveMaterial(config);

            var root = new GameObject(SkirtRootName);
            root.transform.SetParent(terrain.transform, false);
            // worldPositionStays=false → posición local (0,0,0) respecto al Terrain ✓

            BuildSide(root, "Side_South", tw, tl, th, depth, samples, mat, Side.South, HeightAt);
            BuildSide(root, "Side_North", tw, tl, th, depth, samples, mat, Side.North, HeightAt);
            BuildSide(root, "Side_West",  tw, tl, th, depth, samples, mat, Side.West,  HeightAt);
            BuildSide(root, "Side_East",  tw, tl, th, depth, samples, mat, Side.East,  HeightAt);
            BuildBottom(root, tw, tl, depth, mat);

            return root;
        }

        // ─────────────────────────────────────────────────────────────
        // Construcción de caras
        // ─────────────────────────────────────────────────────────────

        enum Side { South, North, West, East }

        /// <param name="th">Altura máxima local del Terrain (terrainData.size.y).</param>
        /// <param name="heightAt">Función (u,v)→localY, u y v en [0..1].</param>
        static void BuildSide(GameObject parent, string name,
                               float tw, float tl, float th, float depth, int samples,
                               Material mat, Side side,
                               System.Func<float, float, float> heightAt)
        {
            var verts   = new List<Vector3>(samples * 2);
            var uvs     = new List<Vector2>(samples * 2);
            var normals = new List<Vector3>(samples * 2);
            var tris    = new List<int>((samples - 1) * 6);

            // Normal local apuntando hacia afuera de cada cara
            Vector3 faceNormal = side switch
            {
                Side.South => new Vector3( 0, 0, -1),
                Side.North => new Vector3( 0, 0,  1),
                Side.West  => new Vector3(-1, 0,  0),
                _          => new Vector3( 1, 0,  0),
            };

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / (samples - 1);  // 0..1 a lo largo del borde

                // Posición LOCAL en el plano XZ del Terrain (0..tw, 0..tl)
                float lx, lz;
                float sampleU, sampleV;  // coordenadas de muestra del heightmap

                switch (side)
                {
                    case Side.South:
                        lx = t * tw;  lz = 0f;
                        sampleU = t;  sampleV = 0f;
                        break;
                    case Side.North:
                        lx = t * tw;  lz = tl;
                        sampleU = t;  sampleV = 1f;
                        break;
                    case Side.West:
                        lx = 0f;      lz = t * tl;
                        sampleU = 0f; sampleV = t;
                        break;
                    default: // East
                        lx = tw;      lz = t * tl;
                        sampleU = 1f; sampleV = t;
                        break;
                }

                float surfaceLocalY = heightAt(sampleU, sampleV);   // [0..th]
                float bottomLocalY  = -depth;                        // < 0

                // UV-V: usamos coordenada de altura ABSOLUTA para que las bandas
                // queden a alturas consistentes en el mundo (efecto corte geológico).
                // totalRange = rango completo de Y posible: desde el fondo (-depth)
                // hasta la altura máxima del terreno (th).
                // V=0 → fondo, V=1 → punto al nivel máximo del terreno.
                float totalRange = depth + th;
                float vSurface = (surfaceLocalY + depth) / totalRange;  // [0..1]
                float vBottom  = 0f;                                    // siempre 0

                // Vértice superior → sigue el relieve del Terrain
                verts.Add(new Vector3(lx, surfaceLocalY, lz));
                uvs.Add(new Vector2(t, vSurface));
                normals.Add(faceNormal);

                // Vértice inferior → fondo plano
                verts.Add(new Vector3(lx, bottomLocalY, lz));
                uvs.Add(new Vector2(t, vBottom));
                normals.Add(faceNormal);
            }

            // Quads como dos triángulos por columna adyacente
            for (int i = 0; i < samples - 1; i++)
            {
                int tl2 = i * 2;
                int bl  = i * 2 + 1;
                int tr  = i * 2 + 2;
                int br  = i * 2 + 3;

                // Winding CCW visto desde el exterior de cada cara:
                // South(-Z) y East(+X) comparten grupo A; North(+Z) y West(-X) grupo B
                if (side == Side.South || side == Side.East)
                {
                    tris.Add(tl2); tris.Add(tr); tris.Add(bl);
                    tris.Add(tr);  tris.Add(br); tris.Add(bl);
                }
                else
                {
                    tris.Add(tl2); tris.Add(bl); tris.Add(tr);
                    tris.Add(tr);  tris.Add(bl); tris.Add(br);
                }
            }

            CreateMeshObject(parent, name, verts, uvs, normals, tris, mat);
        }

        static void BuildBottom(GameObject parent, float tw, float tl,
                                float depth, Material mat)
        {
            float y = -depth;  // local Y del fondo

            var verts = new List<Vector3>
            {
                new Vector3(0f, y, 0f),
                new Vector3(tw, y, 0f),
                new Vector3(0f, y, tl),
                new Vector3(tw, y, tl),
            };
            var uvs = new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, 1), new Vector2(1, 1),
            };
            var normals = new List<Vector3>
            {
                Vector3.down, Vector3.down, Vector3.down, Vector3.down,
            };
            // Winding: cara inferior apunta hacia abajo (vista desde abajo = CCW)
            var tris = new List<int> { 0, 1, 2,  1, 3, 2 };

            CreateMeshObject(parent, "Bottom", verts, uvs, normals, tris, mat);
        }

        // ─────────────────────────────────────────────────────────────
        // Utilidades
        // ─────────────────────────────────────────────────────────────

        static void CreateMeshObject(GameObject parent, string name,
                                     List<Vector3> verts, List<Vector2> uvs,
                                     List<Vector3> normals, List<int> tris,
                                     Material mat)
        {
            var go   = new GameObject(name);
            go.transform.SetParent(parent.transform, false);

            var mesh = new Mesh { name = name };
            mesh.indexFormat = verts.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial              = mat;
            mr.shadowCastingMode           = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows              = true;
        }

        static Material ResolveMaterial(MapGenConfig config)
        {
            if (config.skirtMaterial != null) return config.skirtMaterial;

            var shader = Shader.Find("Custom/TerrainSkirt")
                      ?? Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Diffuse");

            var mat = new Material(shader) { name = "MAT_TerrainSkirt_Generated" };

            if (mat.HasProperty("_ColorSand"))     mat.SetColor("_ColorSand",     new Color(0.76f, 0.60f, 0.42f));
            if (mat.HasProperty("_ColorSubsoil"))  mat.SetColor("_ColorSubsoil",  new Color(0.55f, 0.27f, 0.07f));
            if (mat.HasProperty("_ColorClay"))     mat.SetColor("_ColorClay",     new Color(0.42f, 0.22f, 0.06f));
            if (mat.HasProperty("_ColorOrganic"))  mat.SetColor("_ColorOrganic",  new Color(0.28f, 0.14f, 0.04f));
            if (mat.HasProperty("_ColorDark"))     mat.SetColor("_ColorDark",     new Color(0.18f, 0.10f, 0.03f));
            if (mat.HasProperty("_BaseColor"))     mat.SetColor("_BaseColor",     new Color(0.38f, 0.20f, 0.06f));

            return mat;
        }

        static void DestroyExisting(Transform terrainTransform)
        {
            var old = terrainTransform.Find(SkirtRootName);
            if (old == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Object.DestroyImmediate(old.gameObject);
            else
#endif
                Object.Destroy(old.gameObject);
        }
    }
}
