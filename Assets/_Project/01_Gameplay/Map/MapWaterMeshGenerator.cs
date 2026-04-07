using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Project.Gameplay.Map
{
    /// <summary>
    /// Genera la malla visual del agua a partir del MapGrid (celdas marcadas como agua).
    /// Extraído de RTSMapGenerator para reducir tamaño y responsabilidades.
    /// </summary>
    /// <remarks>
    /// El generador definitivo usa Project.Gameplay.Map.Generator.WaterMeshBuilder con GridSystem;
    /// esta clase queda para modos chunk / plano del RTS cuando no entra ese pipeline.
    /// </remarks>
    public static class MapWaterMeshGenerator
    {
        public struct WaterMeshConfig
        {
            public MapGrid grid;
            public float waterHeight;
            public float waterSurfaceOffset;
            public Material waterMaterial;
            public bool showWater;
            public WaterMeshMode waterMeshMode;
            public int waterChunkSize;
            public int waterLayerOverride;
        }

        static Material s_defaultWaterMat;
        static Material s_fallbackQuadWaterMat;
        static Material s_pinkFallback;
        static Material s_waterMaterialInstance;
        static Material s_waterMaterialInstanceSource;

        /// <summary>Genera el árbol de GameObjects con la malla de agua. Devuelve el Transform raíz o null si no hay agua o no se muestra.</summary>
        /// <param name="config">Configuración (grid, altura, material, modo, etc.).</param>
        /// <param name="coroutineRunner">MonoBehaviour que ejecutará la corrutina para asegurar visibilidad en cámaras (ej. RTSMapGenerator).</param>
        /// <param name="log">Opcional: callback para mensajes de log.</param>
        public static Transform Generate(WaterMeshConfig config, MonoBehaviour coroutineRunner, Action<string> log = null)
        {
            if (log == null) log = _ => { };

            if (!config.showWater || config.grid == null || !config.grid.IsReady)
                return null;

            if (config.waterHeight <= -998f)
            {
                log("Agua: sin malla (Water Height está en -999 = desactivado). Para ver lagos/ríos: pon Water Height en world units, ej. 3–4 si Height Multiplier = 8.");
                return null;
            }

            if (config.waterMeshMode == WaterMeshMode.FullPlaneIntersect)
                return GenerateFullPlaneIntersect(config, coroutineRunner, log);

            return GenerateChunks(config, coroutineRunner, log);
        }

        static Transform GenerateFullPlaneIntersect(WaterMeshConfig config, MonoBehaviour coroutineRunner, Action<string> log)
        {
            MapGrid grid = config.grid;
            float cellSize = grid.cellSize;
            float y = config.waterHeight + config.waterSurfaceOffset;
            int gridW = grid.width;
            int gridH = grid.height;
            Vector3 origin = grid.origin;

            int vertsW = gridW + 1;
            int vertsH = gridH + 1;
            var verts = new List<Vector3>(vertsW * vertsH);
            for (int gz = 0; gz < vertsH; gz++)
            {
                for (int gx = 0; gx < vertsW; gx++)
                {
                    float wx = origin.x + gx * cellSize;
                    float wz = origin.z + gz * cellSize;
                    verts.Add(new Vector3(wx, y, wz));
                }
            }

            int VertexIndex(int vx, int vz) => vz * vertsW + vx;

            var tris = new List<int>();
            for (int gz = 0; gz < gridH; gz++)
            {
                for (int gx = 0; gx < gridW; gx++)
                {
                    if (!grid.IsWater(new Vector2Int(gx, gz))) continue;

                    int v00 = VertexIndex(gx, gz);
                    int v10 = VertexIndex(gx + 1, gz);
                    int v11 = VertexIndex(gx + 1, gz + 1);
                    int v01 = VertexIndex(gx, gz + 1);
                    tris.Add(v00); tris.Add(v10); tris.Add(v11);
                    tris.Add(v00); tris.Add(v11); tris.Add(v01);
                }
            }

            if (tris.Count == 0)
            {
                Debug.LogWarning("Agua (FullPlaneIntersect): 0 celdas bajo el nivel. Sube Water Height o Water Height Relative para que haya agua.");
                return null;
            }

            int waterLayer = config.waterLayerOverride >= 0 ? Mathf.Clamp(config.waterLayerOverride, 0, 31) : 0;
            Transform waterRoot = CreateWaterRoot(waterLayer);

            var mesh = new Mesh();
            mesh.name = "Water_FullPlaneIntersect";
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            if (verts.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            CreateWaterChild(waterRoot, "Water_FullPlaneIntersect", mesh, config.waterMaterial, waterLayer);

            if (config.waterMaterial == null)
                Debug.LogWarning("MapWaterMeshGenerator: waterMaterial no asignado; se usa material por defecto. Crea MAT_Water y asígnalo en Agua (visual).");
            log($"Water (FullPlaneIntersect): 1 mesh, {verts.Count} vértices, {tris.Count / 3} triángulos, capa '{LayerMask.LayerToName(waterLayer)}'.");

            if (coroutineRunner != null)
                coroutineRunner.StartCoroutine(EnsureWaterVisibleNextFrame(waterLayer, log));

            return waterRoot;
        }

        static Transform GenerateChunks(WaterMeshConfig config, MonoBehaviour coroutineRunner, Action<string> log)
        {
            MapGrid grid = config.grid;
            float cellSize = grid.cellSize;
            float half = cellSize * 0.5f;
            float y = config.waterHeight + config.waterSurfaceOffset;
            int chunkSize = Mathf.Max(1, config.waterChunkSize);
            int gridW = grid.width;
            int gridH = grid.height;

            int waterLayer = config.waterLayerOverride >= 0 ? Mathf.Clamp(config.waterLayerOverride, 0, 31) : 0;
            Transform waterRoot = CreateWaterRoot(waterLayer);

            int chunkCount = 0;
            for (int cz = 0; cz < gridH; cz += chunkSize)
            {
                for (int cx = 0; cx < gridW; cx += chunkSize)
                {
                    var verts = new List<Vector3>();
                    var tris = new List<int>();

                    int cxe = Mathf.Min(cx + chunkSize, gridW);
                    int cze = Mathf.Min(cz + chunkSize, gridH);

                    for (int gz = cz; gz < cze; gz++)
                    {
                        for (int gx = cx; gx < cxe; gx++)
                        {
                            var c = new Vector2Int(gx, gz);
                            if (!grid.IsWater(c)) continue;

                            Vector3 center = grid.CellToWorld(c);
                            center.y = y;

                            Vector3 v0 = center + new Vector3(-half, 0f, -half);
                            Vector3 v1 = center + new Vector3(half, 0f, -half);
                            Vector3 v2 = center + new Vector3(half, 0f, half);
                            Vector3 v3 = center + new Vector3(-half, 0f, half);

                            v0 = waterRoot.InverseTransformPoint(v0);
                            v1 = waterRoot.InverseTransformPoint(v1);
                            v2 = waterRoot.InverseTransformPoint(v2);
                            v3 = waterRoot.InverseTransformPoint(v3);

                            int baseIdx = verts.Count;
                            verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
                            tris.Add(baseIdx); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
                            tris.Add(baseIdx); tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
                        }
                    }

                    if (verts.Count == 0) continue;

                    var mesh = new Mesh();
                    mesh.name = $"WaterChunk_{cx}_{cz}";
                    mesh.SetVertices(verts);
                    mesh.SetTriangles(tris, 0);
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    var b = mesh.bounds;
                    mesh.bounds = new Bounds(b.center, b.size + Vector3.one * (cellSize * 2f));

                    if (chunkCount == 0)
                        log($"Water chunk (primer mesh): {verts.Count} vértices, {tris.Count / 3} triángulos, bounds size = {mesh.bounds.size}.");

                    CreateWaterChild(waterRoot, $"WaterChunk_{cx}_{cz}", mesh, config.waterMaterial, waterLayer);
                    chunkCount++;
                }
            }

            if (config.waterMaterial == null && chunkCount > 0)
                Debug.LogWarning("MapWaterMeshGenerator: waterMaterial no asignado; se usa material por defecto. Crea MAT_Water (URP Lit/Unlit) y asígnalo en Agua (visual).");

            if (chunkCount == 0)
            {
                Debug.LogWarning("Agua: 0 celdas bajo el nivel. Sube Water Height (ej. 30–50% de Height Multiplier: si es 8, prueba 3–4) para que las depresiones del terreno sean agua.");
                if (waterRoot != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(waterRoot.gameObject);
                    else UnityEngine.Object.DestroyImmediate(waterRoot.gameObject);
                }
                return null;
            }

            log($"Water mesh: {chunkCount} chunks, capa = '{LayerMask.LayerToName(waterLayer)}' ({waterLayer}).");
            if (coroutineRunner != null)
                coroutineRunner.StartCoroutine(EnsureWaterVisibleNextFrame(waterLayer, log));

            return waterRoot;
        }

        static Transform CreateWaterRoot(int waterLayer)
        {
            var root = new GameObject("Water").transform;
            root.SetParent(null);
            root.position = Vector3.zero;
            root.rotation = Quaternion.identity;
            root.localScale = Vector3.one;
            root.gameObject.SetActive(true);
            root.gameObject.layer = waterLayer;
            return root;
        }

        static void CreateWaterChild(Transform parent, string name, Mesh mesh, Material assigned, int waterLayer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = waterLayer;
            go.SetActive(true);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            Material mat = GetMaterialForWaterMesh(assigned);
            if (mat == null) mat = GetDefaultWaterMaterial();
            if (mat == null) mat = GetFallbackWaterMaterialFromQuad();
            mr.sharedMaterial = mat != null ? mat : GetOrCreatePinkFallback();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = true;
            mr.renderingLayerMask = unchecked((uint)-1);
        }

        static IEnumerator EnsureWaterVisibleNextFrame(int waterLayer, Action<string> log)
        {
            yield return null;
            int waterBit = 1 << waterLayer;
            var cams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var cam in cams)
            {
                if (cam == null || !cam.enabled) continue;
                if ((cam.cullingMask & waterBit) == 0)
                {
                    cam.cullingMask |= waterBit;
                    log($"Water: Cámara '{cam.name}' ahora incluye la capa del agua en Culling Mask (Game view).");
                }
            }
        }

        static Material GetMaterialForWaterMesh(Material assigned)
        {
            if (assigned != null)
            {
                if (s_waterMaterialInstance == null || s_waterMaterialInstanceSource != assigned)
                {
                    s_waterMaterialInstanceSource = assigned;
                    s_waterMaterialInstance = new Material(assigned);
                    if (s_waterMaterialInstance.renderQueue < 2500)
                        s_waterMaterialInstance.renderQueue = 2001;
                }
                return s_waterMaterialInstance;
            }
            var mat = GetDefaultWaterMaterial();
            if (mat == null) mat = GetFallbackWaterMaterialFromQuad();
            return mat;
        }

        static Material GetDefaultWaterMaterial()
        {
            if (s_defaultWaterMat != null) return s_defaultWaterMat;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                s_defaultWaterMat = new Material(shader);
                var waterColor = new Color(0.2f, 0.45f, 0.85f, 1f);
                if (s_defaultWaterMat.HasProperty("_BaseColor"))
                    s_defaultWaterMat.SetColor("_BaseColor", waterColor);
                else if (s_defaultWaterMat.HasProperty("_Color"))
                    s_defaultWaterMat.SetColor("_Color", waterColor);
                s_defaultWaterMat.renderQueue = 2001;
            }
            return s_defaultWaterMat;
        }

        static Material GetFallbackWaterMaterialFromQuad()
        {
            if (s_fallbackQuadWaterMat != null) return s_fallbackQuadWaterMat;
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            if (quad == null) return null;
            quad.SetActive(false);
            var mr = quad.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null) { UnityEngine.Object.Destroy(quad); return null; }
            s_fallbackQuadWaterMat = new Material(mr.sharedMaterial);
            UnityEngine.Object.Destroy(quad);
            if (s_fallbackQuadWaterMat.HasProperty("_BaseColor")) s_fallbackQuadWaterMat.SetColor("_BaseColor", new Color(0.2f, 0.45f, 0.85f, 1f));
            else if (s_fallbackQuadWaterMat.HasProperty("_Color")) s_fallbackQuadWaterMat.SetColor("_Color", new Color(0.2f, 0.45f, 0.85f, 1f));
            s_fallbackQuadWaterMat.renderQueue = 2001;
            return s_fallbackQuadWaterMat;
        }

        static Material GetOrCreatePinkFallback()
        {
            if (s_pinkFallback != null) return s_pinkFallback;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                s_pinkFallback = new Material(shader);
                if (s_pinkFallback.HasProperty("_Color")) s_pinkFallback.SetColor("_Color", new Color(0.2f, 0.45f, 0.85f, 1f));
                if (s_pinkFallback.HasProperty("_BaseColor")) s_pinkFallback.SetColor("_BaseColor", new Color(0.2f, 0.45f, 0.85f, 1f));
                s_pinkFallback.renderQueue = 2001;
            }
            if (s_pinkFallback == null) s_pinkFallback = GetFallbackWaterMaterialFromQuad();
            return s_pinkFallback;
        }
    }
}
