using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 9: construye mesh de agua por chunks; quads por celda Water/River. Un GameObject raíz "Water".</summary>
    public static class WaterMeshBuilder
    {
        private static Transform _waterRoot;

        /// <summary>Parámetros: config.waterChunkSize, config.waterHeight01, config.waterSurfaceOffset. material puede ser null (fallback).</summary>
        public static GameObject BuildWaterMeshes(GridSystem grid, MapGenConfig config, Material waterMaterial)
        {
            if (grid == null || config == null) return null;

            if (_waterRoot != null)
            {
                if (Application.isPlaying) Object.Destroy(_waterRoot.gameObject);
                else Object.DestroyImmediate(_waterRoot.gameObject);
                _waterRoot = null;
            }

            int chunkSize = Mathf.Max(1, config.waterChunkSize);
            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            // Offset Y mínimo >= 0.2f para evitar z-fighting con el terreno.
            // (Si config.waterSurfaceOffset es mayor, se respeta.)
            float y = grid.Origin.y + config.waterHeight01 * terrainY + Mathf.Max(config.waterSurfaceOffset, 0.2f);
            float cellSize = grid.CellSizeWorld;
            int w = grid.Width;
            int h = grid.Height;
            int waterCellCount = 0;
            for (int gx = 0; gx < w; gx++)
                for (int gz = 0; gz < h; gz++)
                {
                    var t = grid.GetCell(gx, gz).type;
                    if (t == CellType.Water || t == CellType.River) waterCellCount++;
                }

            // Siempre capa 0 (Default) para que la cámara muestre el agua sin tocar Culling Mask.
            const int waterLayer = 0;
            Material sharedWaterMat = GetOrCreateWaterMaterial(waterMaterial, config);

            _waterRoot = new GameObject("Water").transform;
            _waterRoot.SetParent(null);
            _waterRoot.position = Vector3.zero;
            _waterRoot.rotation = Quaternion.identity;
            _waterRoot.localScale = Vector3.one;
            _waterRoot.gameObject.layer = waterLayer;
            _waterRoot.gameObject.SetActive(true);
            // Bordes redondeados (Marching Squares) — recomendado para siluetas orgánicas.
            if (config.waterRoundedEdges)
            {
                if (BuildRoundedWaterMarchingSquares(_waterRoot, grid, config, sharedWaterMat, y, cellSize, waterLayer))
                    return _waterRoot.gameObject;
            }

            // Post-proceso de máscara (rápido): suaviza Water/River -> bool mask para reducir dientes sin MS.
            bool[,] smoothMask = null;
            if (config.waterMaskPostProcess && config.waterMaskSmoothIterations > 0)
                smoothMask = BuildSmoothedWaterMask(grid, config);

            int chunkCount = 0;
            int totalVerts = 0;
            int totalTris = 0;

            for (int cz = 0; cz < h; cz += chunkSize)
            {
                for (int cx = 0; cx < w; cx += chunkSize)
                {
                    int cxe = Mathf.Min(cx + chunkSize, w);
                    int cze = Mathf.Min(cz + chunkSize, h);
                    int chunkW = cxe - cx;
                    int chunkH = cze - cz;

                    // Vertex lattice determinista: (chunkW+1) x (chunkH+1).
                    // Importante: NO hay subdivisiones ni "merge por floats".
                    // Los chunks adyacentes comparten posiciones en borde, pero NO comparten caras => sin overlap.
                    int vertsW = chunkW + 1;
                    int vertsH = chunkH + 1;
                    int vertCount = vertsW * vertsH;
                    var verts = new List<Vector3>(vertCount);
                    var uvs = new List<Vector2>(vertCount);
                    var tris = new List<int>();

                    for (int lz = 0; lz < vertsH; lz++)
                    {
                        for (int lx = 0; lx < vertsW; lx++)
                        {
                            float wx = grid.Origin.x + (cx + lx) * cellSize;
                            float wz = grid.Origin.z + (cz + lz) * cellSize;
                            verts.Add(new Vector3(wx, y, wz));

                            // UVs simples y deterministas (0..1 global en el mapa).
                            float u = w > 1 ? (float)(cx + lx) / (w - 1) : 0f;
                            float v = h > 1 ? (float)(cz + lz) / (h - 1) : 0f;
                            uvs.Add(new Vector2(u, v));
                        }
                    }

                    int Index(int lx, int lz) => lz * vertsW + lx;

                    for (int gz = cz; gz < cze; gz++)
                    {
                        for (int gx = cx; gx < cxe; gx++)
                        {
                            ref var cell = ref grid.GetCell(gx, gz);
                            bool isWater = (smoothMask != null) ? smoothMask[gx, gz] : (cell.type == CellType.Water || cell.type == CellType.River);
                            if (!isWater) continue;

                            int lx = gx - cx;
                            int lz = gz - cz;

                            int i0 = Index(lx, lz);
                            int i1 = Index(lx + 1, lz);
                            int i2 = Index(lx + 1, lz + 1);
                            int i3 = Index(lx, lz + 1);

                            // Winding para que la normal apunte hacia +Y (cara superior).
                            tris.Add(i0); tris.Add(i2); tris.Add(i1);
                            tris.Add(i0); tris.Add(i3); tris.Add(i2);
                        }
                    }

                    if (tris.Count == 0) continue;

                    var mesh = new Mesh();
                    mesh.name = $"WaterChunk_{cx}_{cz}";
                    // Vertices ya están en world-space y el root está en identidad => local == world.
                    mesh.SetVertices(verts);
                    mesh.SetTriangles(tris, 0);
                    mesh.SetUVs(0, uvs);
                    // Vertex colors uniformes (blanco) para que Unlit no multiplique por valores basura.
                    var colors = new List<Color>(verts.Count);
                    for (int i = 0; i < verts.Count; i++) colors.Add(Color.white);
                    mesh.SetColors(colors);
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();

                    var go = new GameObject($"WaterChunk_{cx}_{cz}");
                    go.transform.SetParent(_waterRoot, false);
                    go.layer = waterLayer;
                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    if (sharedWaterMat != null)
                    {
                        mr.sharedMaterial = sharedWaterMat;
                        mr.enabled = true;
                    }
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.renderingLayerMask = 1u;
                    chunkCount++;
                    totalVerts += verts.Count;
                    totalTris += tris.Count / 3;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (config.debugLogs)
                        Debug.Log($"Fase9 WaterChunk [{cx},{cz}] size={chunkW}x{chunkH}: verts={verts.Count}, tris={(tris.Count / 3)} (1 quad/celda agua, sin subdiv ni merge).");
#endif
                }
            }

            if (chunkCount == 0)
                Debug.LogWarning($"Fase9 WaterMesh: 0 chunks (había {waterCellCount} celdas agua). Revisa Fase3 o sube riverCount/lakeCount.");
            else
            {
                string matInfo = sharedWaterMat != null ? sharedWaterMat.name : "null";
                int expectedTris = waterCellCount * 2;
                if (config.debugLogs)
                    Debug.Log($"Fase9 WaterMesh: {waterCellCount} celdas agua, {chunkCount} chunks, Y={y:F2}, material={matInfo}, totalVerts={totalVerts}, totalTris={totalTris}. Esperado ~{expectedTris} tris (2 por celda agua).");
            }
            return _waterRoot != null ? _waterRoot.gameObject : null;
        }

        private static bool[,] BuildSmoothedWaterMask(GridSystem grid, MapGenConfig config)
        {
            int w = grid.Width;
            int h = grid.Height;
            var a = new bool[w, h];
            var b = new bool[w, h];

            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    var t = grid.GetCell(x, z).type;
                    a[x, z] = (t == CellType.Water || t == CellType.River);
                }

            int iters = Mathf.Clamp(config.waterMaskSmoothIterations, 0, 8);
            int thr = Mathf.Clamp(config.waterMaskSmoothThreshold, 0, 9);
            if (iters <= 0) return a;

            for (int it = 0; it < iters; it++)
            {
                for (int z = 0; z < h; z++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int count = a[x, z] ? 1 : 0;
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int zz = z + dz;
                            if ((uint)zz >= (uint)h) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx;
                                if ((uint)xx >= (uint)w) continue;
                                if (dx == 0 && dz == 0) continue;
                                if (a[xx, zz]) count++;
                            }
                        }
                        b[x, z] = count >= thr;
                    }
                }
                // swap
                var tmp = a; a = b; b = tmp;
            }

            return a;
        }

        private static bool BuildRoundedWaterMarchingSquares(Transform parent, GridSystem grid, MapGenConfig config, Material mat, float y, float cellSize, int waterLayer)
        {
            int w = grid.Width;
            int h = grid.Height;
            int subdiv = Mathf.Clamp(config.waterEdgeSubdiv, 1, 8);
            int blurIters = Mathf.Max(0, config.waterEdgeBlurIterations);
            int blurRadius = Mathf.Clamp(config.waterEdgeBlurRadius, 1, 4);
            float iso = Mathf.Clamp(config.waterIsoLevel, 0.05f, 0.95f);

            int effectiveSubdiv = subdiv;
            // Optimización: generar MS solo en el bounding box del agua (con padding por blur),
            // en vez de sobre TODO el mapa (ahorra cientos de miles de vértices si el agua es poca).
            int minX = w, minZ = h, maxX = -1, maxZ = -1;
            for (int gz = 0; gz < h; gz++)
            {
                for (int gx = 0; gx < w; gx++)
                {
                    var t = grid.GetCell(gx, gz).type;
                    if (t != CellType.Water && t != CellType.River) continue;
                    if (gx < minX) minX = gx;
                    if (gz < minZ) minZ = gz;
                    if (gx > maxX) maxX = gx;
                    if (gz > maxZ) maxZ = gz;
                }
            }
            if (maxX < 0 || maxZ < 0)
            {
                // Sin agua -> no crear mesh.
                if (config.debugLogs)
                    Debug.LogWarning("Fase9 WaterMesh (MS): no hay celdas de agua.");
                return false;
            }

            int padCells = Mathf.Max(2, blurRadius * Mathf.Max(1, blurIters) + 2);
            int rectMinX = Mathf.Clamp(minX - padCells, 0, w - 1);
            int rectMinZ = Mathf.Clamp(minZ - padCells, 0, h - 1);
            int rectMaxX = Mathf.Clamp(maxX + padCells, 0, w - 1);
            int rectMaxZ = Mathf.Clamp(maxZ + padCells, 0, h - 1);
            int rectW = rectMaxX - rectMinX + 1;
            int rectH = rectMaxZ - rectMinZ + 1;

            // Máscara suavizada por celdas (rápido). Reduce esquinas aisladas antes del upsample+blur.
            bool[,] coarseMask = null;
            if (config.waterMaskPostProcess && config.waterMaskSmoothIterations > 0)
            {
                coarseMask = new bool[rectW, rectH];
                for (int z = 0; z < rectH; z++)
                    for (int x = 0; x < rectW; x++)
                    {
                        int gx = rectMinX + x;
                        int gz = rectMinZ + z;
                        var t = grid.GetCell(gx, gz).type;
                        coarseMask[x, z] = (t == CellType.Water || t == CellType.River);
                    }

                int iters = Mathf.Clamp(config.waterMaskSmoothIterations, 0, 8);
                int thr = Mathf.Clamp(config.waterMaskSmoothThreshold, 0, 9);
                if (iters > 0)
                {
                    var tmp = new bool[rectW, rectH];
                    for (int it = 0; it < iters; it++)
                    {
                        for (int z = 0; z < rectH; z++)
                        {
                            for (int x = 0; x < rectW; x++)
                            {
                                int count = coarseMask[x, z] ? 1 : 0;
                                for (int dz = -1; dz <= 1; dz++)
                                {
                                    int zz = z + dz;
                                    if ((uint)zz >= (uint)rectH) continue;
                                    for (int dx = -1; dx <= 1; dx++)
                                    {
                                        int xx = x + dx;
                                        if ((uint)xx >= (uint)rectW) continue;
                                        if (dx == 0 && dz == 0) continue;
                                        if (coarseMask[xx, zz]) count++;
                                    }
                                }
                                tmp[x, z] = count >= thr;
                            }
                        }
                        var swap = coarseMask; coarseMask = tmp; tmp = swap;
                    }
                }
            }

            // Límite de seguridad (evita mallas gigantes / GC).
            // En vez de caer directamente a chunks, intentamos degradar calidad bajando subdiv (3→2→1).
            int maxSamples = config.waterMsMaxCornerSamples;
            int sw, sh, sampleX0, sampleZ0;
            while (true)
            {
                sampleX0 = rectMinX * effectiveSubdiv;
                sampleZ0 = rectMinZ * effectiveSubdiv;
                sw = rectW * effectiveSubdiv + 1;
                sh = rectH * effectiveSubdiv + 1;
                int samples = sw * sh;

                if (maxSamples <= 0 || samples <= maxSamples)
                    break;

                if (effectiveSubdiv > 1)
                {
                    effectiveSubdiv--;
                    continue;
                }

                Debug.LogWarning($"Fase9 WaterMesh (MS): demasiado grande para MS (samples={samples} > max={maxSamples}). Fallback a agua por chunks. Sugerencia: sube waterMsMaxCornerSamples o baja waterEdgeSubdiv.");
                return false;
            }

            float step = cellSize / effectiveSubdiv;

            // Campo escalar: 1 agua, 0 tierra (upsampled).
            var field = new float[sw, sh];
            for (int z = 0; z < sh; z++)
            {
                int gz = Mathf.Clamp((sampleZ0 + z) / effectiveSubdiv, 0, h - 1);
                for (int x = 0; x < sw; x++)
                {
                    int gx = Mathf.Clamp((sampleX0 + x) / effectiveSubdiv, 0, w - 1);
                    bool isWater;
                    if (coarseMask != null)
                    {
                        int mx = Mathf.Clamp(gx - rectMinX, 0, rectW - 1);
                        int mz = Mathf.Clamp(gz - rectMinZ, 0, rectH - 1);
                        isWater = coarseMask[mx, mz];
                    }
                    else
                    {
                        var t = grid.GetCell(gx, gz).type;
                        isWater = (t == CellType.Water || t == CellType.River);
                    }
                    field[x, z] = isWater ? 1f : 0f;
                }
            }

            // Blur para redondear la máscara (suaviza esquinas).
            if (blurIters > 0)
                BoxBlur(field, sw, sh, blurRadius, blurIters);

            // Precrear vértices de esquina (samples) y caches de edges para evitar explosión de vértices.
            var verts = new List<Vector3>(sw * sh);
            var uvs = new List<Vector2>(sw * sh);
            var colors = new List<Color>(sw * sh);
            var tris = new List<int>(Mathf.Max(1024, sw * sh / 2));

            int[,] cornerIndex = new int[sw, sh];
            for (int z = 0; z < sh; z++)
            {
                float wz = grid.Origin.z + (sampleZ0 + z) * step;
                float v = (h * cellSize) > 0.0001f ? (wz - grid.Origin.z) / (h * cellSize) : 0f;
                for (int x = 0; x < sw; x++)
                {
                    float wx = grid.Origin.x + (sampleX0 + x) * step;
                    float u = (w * cellSize) > 0.0001f ? (wx - grid.Origin.x) / (w * cellSize) : 0f;
                    cornerIndex[x, z] = verts.Count;
                    verts.Add(new Vector3(wx, y, wz));
                    uvs.Add(new Vector2(u, v));
                    colors.Add(Color.white);
                }
            }

            int[,] hEdge = new int[sw - 1, sh];   // entre (x,z) y (x+1,z)
            int[,] vEdge = new int[sw, sh - 1];   // entre (x,z) y (x,z+1)
            for (int z = 0; z < sh; z++)
                for (int x = 0; x < sw - 1; x++)
                    hEdge[x, z] = -1;
            for (int z = 0; z < sh - 1; z++)
                for (int x = 0; x < sw; x++)
                    vEdge[x, z] = -1;

            int GetHEdge(int x, int z, float v0, float v1)
            {
                int idx = hEdge[x, z];
                if (idx != -1) return idx;
                float t = Mathf.Abs(v1 - v0) < 1e-6f ? 0.5f : (iso - v0) / (v1 - v0);
                t = Mathf.Clamp01(t);
                Vector3 p0 = verts[cornerIndex[x, z]];
                Vector3 p1 = verts[cornerIndex[x + 1, z]];
                Vector3 p = Vector3.Lerp(p0, p1, t);
                idx = verts.Count;
                verts.Add(p);
                float uu = sw > 1 ? p.x - grid.Origin.x : 0f;
                float vv = sh > 1 ? p.z - grid.Origin.z : 0f;
                // UVs en world-normalized para que la textura se vea continua
                uvs.Add(new Vector2(uu / (w * cellSize), vv / (h * cellSize)));
                colors.Add(Color.white);
                hEdge[x, z] = idx;
                return idx;
            }

            int GetVEdge(int x, int z, float v0, float v1)
            {
                int idx = vEdge[x, z];
                if (idx != -1) return idx;
                float t = Mathf.Abs(v1 - v0) < 1e-6f ? 0.5f : (iso - v0) / (v1 - v0);
                t = Mathf.Clamp01(t);
                Vector3 p0 = verts[cornerIndex[x, z]];
                Vector3 p1 = verts[cornerIndex[x, z + 1]];
                Vector3 p = Vector3.Lerp(p0, p1, t);
                idx = verts.Count;
                verts.Add(p);
                float uu = sw > 1 ? p.x - grid.Origin.x : 0f;
                float vv = sh > 1 ? p.z - grid.Origin.z : 0f;
                uvs.Add(new Vector2(uu / (w * cellSize), vv / (h * cellSize)));
                colors.Add(Color.white);
                vEdge[x, z] = idx;
                return idx;
            }

            void AddTri(int a, int b, int c)
            {
                // Invertimos winding para normal +Y (consistente con el agua anterior).
                tris.Add(a); tris.Add(c); tris.Add(b);
            }

            // Marching Squares (relleno) por celda de sample.
            var poly = new List<int>(8);
            for (int z = 0; z < sh - 1; z++)
            {
                for (int x = 0; x < sw - 1; x++)
                {
                    float a = field[x, z];
                    float b = field[x + 1, z];
                    float c = field[x + 1, z + 1];
                    float d = field[x, z + 1];
                    bool i0 = a >= iso;
                    bool i1 = b >= iso;
                    bool i2 = c >= iso;
                    bool i3 = d >= iso;

                    int mask = (i0 ? 1 : 0) | (i1 ? 2 : 0) | (i2 ? 4 : 0) | (i3 ? 8 : 0);
                    if (mask == 0) continue;

                    int p0 = cornerIndex[x, z];
                    int p1 = cornerIndex[x + 1, z];
                    int p2 = cornerIndex[x + 1, z + 1];
                    int p3 = cornerIndex[x, z + 1];

                    bool e0 = i0 != i1; // p0-p1
                    bool e1 = i1 != i2; // p1-p2
                    bool e2 = i2 != i3; // p2-p3
                    bool e3 = i3 != i0; // p3-p0

                    int vE0 = e0 ? GetHEdge(x, z, a, b) : -1;
                    int vE1 = e1 ? GetVEdge(x + 1, z, b, c) : -1;
                    int vE2 = e2 ? GetHEdge(x, z + 1, d, c) : -1; // p3-p2 (top)
                    int vE3 = e3 ? GetVEdge(x, z, a, d) : -1;

                    // Casos ambiguos (diagonales): 5 y 10.
                    if (mask == 5 || mask == 10)
                    {
                        float center = (a + b + c + d) * 0.25f;
                        bool centerInside = center >= iso;
                        if (!centerInside)
                        {
                            if (mask == 5)
                            {
                                // Triángulos separados: (p0-e0-e3) y (p2-e2-e1)
                                AddTri(p0, vE0, vE3);
                                AddTri(p2, vE2, vE1);
                            }
                            else
                            {
                                // mask==10: (p1-e1-e0) y (p3-e3-e2)
                                AddTri(p1, vE1, vE0);
                                AddTri(p3, vE3, vE2);
                            }
                            continue;
                        }
                        // Si el centro está dentro, usamos polígono conectado (sin self-intersection).
                    }

                    poly.Clear();
                    if (i0) poly.Add(p0);
                    if (e0) poly.Add(vE0);
                    if (i1) poly.Add(p1);
                    if (e1) poly.Add(vE1);
                    if (i2) poly.Add(p2);
                    if (e2) poly.Add(vE2);
                    if (i3) poly.Add(p3);
                    if (e3) poly.Add(vE3);

                    if (poly.Count < 3) continue;
                    int a0 = poly[0];
                    for (int i = 1; i < poly.Count - 1; i++)
                        AddTri(a0, poly[i], poly[i + 1]);
                }
            }

            if (tris.Count == 0)
            {
                Debug.LogWarning("Fase9 WaterMesh (MS): 0 tris (sin agua o iso demasiado alto).");
                return false;
            }

            var mesh = new Mesh();
            mesh.name = "Water_MarchingSquares";
            if (verts.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Water_MarchingSquares");
            go.transform.SetParent(parent, false);
            go.layer = waterLayer;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.renderingLayerMask = 1u;

            if (config.debugLogs)
                Debug.Log($"Fase9 WaterMesh (MS): rect={rectW}x{rectH} celdas (pad={padCells}), subdiv={effectiveSubdiv} (cfg={subdiv}), blurIters={blurIters}, iso={iso:F2}, verts={verts.Count}, tris={tris.Count / 3}.");
            return true;
        }

        private static void BoxBlur(float[,] field, int w, int h, int radius, int iterations)
        {
            var tmp = new float[w, h];
            int r = Mathf.Max(1, radius);
            for (int it = 0; it < iterations; it++)
            {
                for (int z = 0; z < h; z++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float sum = 0f;
                        int count = 0;
                        for (int dz = -r; dz <= r; dz++)
                        {
                            int zz = z + dz;
                            if ((uint)zz >= (uint)h) continue;
                            for (int dx = -r; dx <= r; dx++)
                            {
                                int xx = x + dx;
                                if ((uint)xx >= (uint)w) continue;
                                sum += field[xx, zz];
                                count++;
                            }
                        }
                        tmp[x, z] = count > 0 ? sum / count : field[x, z];
                    }
                }
                // swap
                for (int z = 0; z < h; z++)
                    for (int x = 0; x < w; x++)
                        field[x, z] = tmp[x, z];
            }
        }

        /// <summary>Material para el agua. Aplica transparencia si config.waterAlpha &lt; 1 para ver la arena bajo el agua.</summary>
        private static Material GetOrCreateWaterMaterial(Material assigned, MapGenConfig config)
        {
            Material mat = assigned != null ? new Material(assigned) : GetFallbackMaterial();

            if (mat == null)
            {
                Debug.LogError("WaterMeshBuilder: No se pudo crear material de agua.");
                return null;
            }

            if (mat.renderQueue < 0) mat.renderQueue = 2001;

            if (assigned == null)
            {
                Color azulAgua = new Color(0.25f, 0.48f, 0.75f, 1f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", azulAgua);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", azulAgua);
            }

            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_ZWriteControl")) mat.SetFloat("_ZWriteControl", 0f);
            if (mat.HasProperty("_ZWriteMode")) mat.SetFloat("_ZWriteMode", 0f);

            float alpha = (config != null && config.waterAlpha > 0f) ? Mathf.Clamp01(config.waterAlpha) : 1f;
            if (alpha < 0.99f)
            {
                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = alpha;
                    mat.SetColor("_BaseColor", c);
                }
                else if (mat.HasProperty("_Color"))
                {
                    Color c = mat.GetColor("_Color");
                    c.a = alpha;
                    mat.SetColor("_Color", c);
                }
                mat.renderQueue = 3000;
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
                if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);
            return mat;
        }

        private static Material ApplyWaterRenderQueue(Material source, int queue)
        {
            if (source == null) return null;
            var inst = new Material(source);
            inst.renderQueue = queue;
            return inst;
        }

        private static Material _fallback;
        private static Material GetFallbackMaterial()
        {
            if (_fallback != null) return _fallback;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Legacy Shaders/Vertex Lit");
            if (shader != null)
            {
                _fallback = new Material(shader);
                if (_fallback.HasProperty("_BaseColor")) _fallback.SetColor("_BaseColor", new Color(0.25f, 0.48f, 0.75f, 1f));
                else if (_fallback.HasProperty("_Color")) _fallback.SetColor("_Color", new Color(0.25f, 0.48f, 0.75f, 1f));
                _fallback.renderQueue = 2001;
                if (_fallback.HasProperty("_Cull")) _fallback.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                if (_fallback.HasProperty("_Surface")) _fallback.SetFloat("_Surface", 0f);
                if (_fallback.HasProperty("_ZWrite")) _fallback.SetFloat("_ZWrite", 0f);
            }
            return _fallback;
        }
    }
}
