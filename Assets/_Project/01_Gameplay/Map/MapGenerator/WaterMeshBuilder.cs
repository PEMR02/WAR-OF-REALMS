using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 9: construye mesh de agua por chunks; quads por celda Water/River. Un GameObject raíz "Water".</summary>
    public static class WaterMeshBuilder
    {
        private static Transform _waterRoot;

        static int _riverVisualHalfSamples;
        static double _riverVisualHalfSum;
        static float _riverVisualHalfMin;
        static float _riverVisualHalfMax;

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
            // Marching squares: solo lagos si el río va por ribbon; luego mallas de río continuas.
            bool marchingSquaresOk = false;
            if (config.waterRoundedEdges)
                marchingSquaresOk = BuildRoundedWaterMarchingSquares(_waterRoot, grid, config, sharedWaterMat, y, cellSize, waterLayer, config.riverVisualUseContinuousMesh);

            bool riverRibbonsOk = false;
            if (config.riverVisualUseContinuousMesh)
            {
                float riverY = y + Mathf.Max(0f, config.riverRibbonVerticalLiftWorld);
                riverRibbonsOk = BuildRiverRibbonMeshes(_waterRoot, grid, config, sharedWaterMat, riverY, cellSize, waterLayer);
            }

            if (marchingSquaresOk)
                return _waterRoot.gameObject;

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
                            bool countRiver = !config.riverVisualUseContinuousMesh;
                            bool isWater = smoothMask != null
                                ? smoothMask[gx, gz]
                                : (cell.type == CellType.Water || (countRiver && cell.type == CellType.River));
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
            {
                if (riverRibbonsOk)
                    return _waterRoot.gameObject;
                Debug.LogWarning($"Fase9 WaterMesh: 0 chunks (había {waterCellCount} celdas agua). Revisa Fase3 o sube riverCount/lakeCount.");
            }
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
            bool countRiver = config == null || !config.riverVisualUseContinuousMesh;

            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    var t = grid.GetCell(x, z).type;
                    a[x, z] = countRiver ? (t == CellType.Water || t == CellType.River) : (t == CellType.Water);
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

            // El filtro de mayoría borra trazos de 1 celda; reforzar río solo si el MS/chunk lo usa como agua.
            if (countRiver)
            {
                for (int z = 0; z < h; z++)
                    for (int x = 0; x < w; x++)
                        if (grid.GetCell(x, z).type == CellType.River)
                            a[x, z] = true;
            }

            return a;
        }

        /// <summary>Río visual: strip desde centerline en espacio celda convertida a mundo en este paso (mismo Origin/cellSize que el terreno).</summary>
        private static bool BuildRiverRibbonMeshes(Transform parent, GridSystem grid, MapGenConfig config, Material mat, float waterY, float cellSize, int waterLayer)
        {
            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return false;
            if (config == null)
                return false;

            LogRiverRibbonGeometryPassBanner(config, grid);

            if (config.debugRiverVisualStats)
            {
                _riverVisualHalfSamples = 0;
                _riverVisualHalfSum = 0.0;
                _riverVisualHalfMin = float.MaxValue;
                _riverVisualHalfMax = float.MinValue;
            }

            float halfW = config.riverVisualMeshHalfWidth - Mathf.Max(0f, config.riverVisualBankInset);
            halfW = Mathf.Max(0.08f, halfW);

            float sampleW = Mathf.Max(0.06f, config.riverVisualSampleSpacing);
            float csSafe = Mathf.Max(0.0001f, cellSize);
            float minSegCells = Mathf.Clamp(sampleW / csSafe, 0.04f, 2f);
            float maxJumpCells = Mathf.Clamp(Mathf.Max(minSegCells * 2.15f, 1.45f), 1.35f, 2.95f);
            float dedupeEpsCells = 0.055f;
            float dedupeEpsWorld = Mathf.Max(0.035f, cellSize * 0.065f);
            // Un solo remuestreo fino bastaba con ~9k vértices/río; aflojamos paso en celda para menos puntos previos a Catmull.
            float stepCells = Mathf.Clamp(sampleW / csSafe * 0.62f, 0.10f, 0.52f);
            float maxSegmentCells = Mathf.Clamp(minSegCells * 0.82f, 0.16f, 0.92f);

            bool any = false;
            for (int riverIndex = 0; riverIndex < grid.RiverCenterlinesCellSpace.Count; riverIndex++)
            {
                var cellPath = grid.RiverCenterlinesCellSpace[riverIndex];
                if (cellPath == null || cellPath.Count < 2)
                    continue;

                int sourcePoints = cellPath.Count;
                var ribbonCell = new List<Vector2>(cellPath);
                int lapPasses = config != null ? Mathf.Clamp(config.riverRibbonCellSpaceLaplacianPasses, 0, 10) : 2;
                float lapAlpha = config != null ? Mathf.Clamp01(config.riverRibbonCellSpaceLaplacianAlpha) : 0.22f;
                SmoothRibbonPolylineCellSpace(ribbonCell, grid.Width, grid.Height, lapPasses, lapAlpha);
                var cellLakePieces = SplitCellPolylineExcludingLakeCells(grid, ribbonCell);
                if (cellLakePieces.Count == 0)
                    continue;

                float maxJumpWorldStrict = csSafe * maxJumpCells * 1.02f;
                float resampleStepWorld = Mathf.Clamp(sampleW * 0.52f, csSafe * 0.14f, csSafe * 0.62f);
                float catmullStepWorld = Mathf.Clamp(sampleW * 0.48f, csSafe * 0.12f, csSafe * 0.52f);

                int segmentCount = 0;
                int totalSegPoints = 0;
                int sub = 0;

                foreach (var ribbonSegment in cellLakePieces)
                {
                    if (ribbonSegment == null || ribbonSegment.Count < 2)
                        continue;

                    var cellDeduped = DedupeNearlyDuplicateConsecutivePointsCell(ribbonSegment, dedupeEpsCells, riverIndex, config);
                    var cellSubdivided = SubdivideLongSegmentsCell2D(cellDeduped, maxSegmentCells);
                    var cellGapRuns = SplitPolylineAtExcessiveGapsCell(cellSubdivided, maxJumpCells, riverIndex, config);

                    foreach (var cellRun in cellGapRuns)
                    {
                        if (cellRun == null || cellRun.Count < 2)
                            continue;
                        if (cellRun.Count == 2 && Vector2.Distance(cellRun[0], cellRun[1]) > maxJumpCells * 1.01f)
                        {
                            LogRiverRibbonDegenerateSegment(config, riverIndex, sub, cellRun.Count, "cell_two_points_excessive_span");
                            sub++;
                            continue;
                        }

                        var cellResampled = ResamplePolylineUniformCell(cellRun, stepCells);
                        if (cellResampled.Count < 2)
                        {
                            LogRiverRibbonDegenerateSegment(config, riverIndex, sub, cellResampled.Count, "cell_after_resample_lt2");
                            sub++;
                            continue;
                        }

                        var worldPath = BuildWorldPathFromCellCenterline(grid, waterY, cellResampled);
                        var deduped = DedupeNearlyDuplicateConsecutivePoints(worldPath, dedupeEpsWorld, riverIndex, config);
                        var gapRuns = SplitPolylineAtExcessiveGaps(deduped, maxJumpWorldStrict, riverIndex, config);
                        foreach (var run in gapRuns)
                        {
                            if (run == null || run.Count < 2)
                                continue;
                            if (run.Count == 2 && HorizontalDistanceXZ(run[0], run[1]) > maxJumpWorldStrict * 1.01f)
                            {
                                LogRiverRibbonDegenerateSegment(config, riverIndex, sub, run.Count, "world_two_points_excessive_span");
                                sub++;
                                continue;
                            }

                            var preCtrl = ResamplePolylineUniformXZ(run, resampleStepWorld * 1.12f, waterY);
                            if (preCtrl.Count < 2)
                            {
                                LogRiverRibbonDegenerateSegment(config, riverIndex, sub, preCtrl.Count, "after_world_presample_lt2");
                                sub++;
                                continue;
                            }

                            var resampled = ResamplePolylineCatmullRomUniformXZ(preCtrl, catmullStepWorld, waterY);
                            if (resampled.Count < 2)
                            {
                                LogRiverRibbonDegenerateSegment(config, riverIndex, sub, resampled.Count, "after_catmull_lt2");
                                sub++;
                                continue;
                            }

                            var ribbonAfterLake = SplitRiverPolylineExcludingLakeCells(grid, cellSize, resampled, waterY);
                            foreach (var ribbonPath in ribbonAfterLake)
                            {
                                if (ribbonPath == null || ribbonPath.Count < 2)
                                    continue;

                                float len = PolylineLengthXZ(ribbonPath);
                                float maxD = MaxConsecutiveHorizontalSegment(ribbonPath);
                                ComputeBoundsXZ(ribbonPath, out Vector3 bMin, out Vector3 bMax);
                                LogRiverRibbonSegmentDetail(config, riverIndex, sub, ribbonPath.Count, len, maxD, bMin, bMax);

                                ApplyRibbonCenterlineLateralJitter(ribbonPath, waterY, config, riverIndex, sub);

                                if (TryBuildRiverRibbonStripMesh(parent, ribbonPath, halfW, waterY, mat, waterLayer, cellSize, riverIndex, sub, config, catmullStepWorld, $"Water_River_{riverIndex}_{sub}"))
                                {
                                    any = true;
                                    segmentCount++;
                                    totalSegPoints += ribbonPath.Count;
                                }

                                sub++;
                            }
                        }
                    }
                }

                LogRiverRibbonRiverAggregate(config, riverIndex, segmentCount, totalSegPoints, sourcePoints);
            }

            if (config.debugRiverVisualStats && _riverVisualHalfSamples > 0)
            {
                float avg = (float)(_riverVisualHalfSum / _riverVisualHalfSamples);
                float span = avg > 1e-5f ? (_riverVisualHalfMax - _riverVisualHalfMin) / avg : 0f;
                Debug.Log($"[RiverVisual] Ribbon semiancho (m): medio={avg:F3} min={_riverVisualHalfMin:F3} max={_riverVisualHalfMax:F3} muestras={_riverVisualHalfSamples} variacionRel={span:F3}");
            }

            return any;
        }

        /// <summary>Jitter lateral suave en la centerline antes del strip (bordes menos “vectoriales”).</summary>
        static void ApplyRibbonCenterlineLateralJitter(List<Vector3> path, float waterY, MapGenConfig config, int riverIndex, int segmentHint)
        {
            if (path == null || path.Count < 2 || config == null) return;
            float jit = config.riverRibbonLateralJitterWorld;
            if (jit < 1e-5f) return;
            float nsc = Mathf.Max(0.06f, config.riverRibbonJitterNoiseScale);
            float seed = config.seed * 0.00017f + riverIndex * 1.713f + segmentHint * 0.331f;
            float acc = 0f;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 tangent;
                if (i == 0) tangent = path[1] - path[0];
                else if (i == path.Count - 1) tangent = path[i] - path[i - 1];
                else tangent = path[i + 1] - path[i - 1];
                tangent.y = 0f;
                if (tangent.sqrMagnitude < 1e-10f) continue;
                tangent.Normalize();
                Vector3 r = Vector3.Cross(Vector3.up, tangent);
                r.y = 0f;
                if (r.sqrMagnitude < 1e-10f) continue;
                r.Normalize();
                if (i > 0) acc += HorizontalDistanceXZ(path[i - 1], path[i]);
                float pn = Mathf.PerlinNoise(seed + acc * nsc * 0.29f, seed * 0.51f + path[i].x * nsc * 0.17f + path[i].z * nsc * 0.13f);
                float off = (pn - 0.5f) * 2f * jit;
                path[i] = new Vector3(path[i].x + r.x * off, waterY, path[i].z + r.z * off);
            }
        }

        private static void LogRiverRibbonGeometryPassBanner(MapGenConfig config, GridSystem grid)
        {
            if (config == null || !config.debugRiverRibbonGeometry)
                return;
            int rivers = grid != null && grid.RiverCenterlinesCellSpace != null ? grid.RiverCenterlinesCellSpace.Count : 0;
            int seed = config.seed;
            string scene = SceneManager.GetActiveScene().name;
            Debug.Log($"[RiverRibbonDebug] seed={seed} scene={scene} rivers={rivers}");
        }

        /// <summary>Solo visual del ribbon: suaviza esquinas (Laplaciano, extremos fijos) sin tocar las celdas River del grid.</summary>
        private static void SmoothRibbonPolylineCellSpace(List<Vector2> poly, int gridW, int gridH, int iterations, float alpha)
        {
            if (poly == null || poly.Count < 3 || iterations <= 0)
                return;
            float minX = 0.5f;
            float maxX = Mathf.Max(minX, gridW - 0.5f);
            float minY = 0.5f;
            float maxY = Mathf.Max(minY, gridH - 0.5f);
            alpha = Mathf.Clamp01(alpha);
            for (int it = 0; it < iterations; it++)
            {
                var copy = new List<Vector2>(poly);
                for (int i = 1; i < poly.Count - 1; i++)
                {
                    Vector2 neighborAvg = (copy[i - 1] + copy[i + 1]) * 0.5f;
                    Vector2 p = Vector2.Lerp(copy[i], neighborAvg, alpha);
                    p.x = Mathf.Clamp(p.x, minX, maxX);
                    p.y = Mathf.Clamp(p.y, minY, maxY);
                    poly[i] = p;
                }
            }
        }

        private static void LogRiverRibbonRiverAggregate(MapGenConfig config, int riverIndex, int segments, int totalPointsInSegments, int sourcePoints)
        {
            if (config == null || !config.debugRiverRibbonGeometry)
                return;
            Debug.Log($"[RiverRibbonDebug] river={riverIndex} sourcePoints={sourcePoints} segments={segments} totalPoints={totalPointsInSegments}");
        }

        private static void LogRiverRibbonSegmentDetail(MapGenConfig config, int riverIndex, int segmentIndex, int pointCount, float length, float maxSegment, Vector3 boundsMin, Vector3 boundsMax)
        {
            if (config == null || !config.debugRiverRibbonGeometry)
                return;
            Debug.Log($"[RiverRibbonDebug] segment={riverIndex}_{segmentIndex} points={pointCount} length={length:F3} maxSegment={maxSegment:F3} boundsMin=({boundsMin.x:F2},{boundsMin.z:F2}) boundsMax=({boundsMax.x:F2},{boundsMax.z:F2})");
        }

        private static void LogRiverRibbonDegenerateSegment(MapGenConfig config, int riverIndex, int segmentIndex, int pointCount, string reason)
        {
            if (config == null || !config.debugRiverRibbonGeometry)
                return;
            Debug.LogWarning($"[RiverRibbonDebug] WARNING degenerate segment | river={riverIndex} segment={segmentIndex} points={pointCount} reason={reason}");
        }

        private static void ComputeBoundsXZ(List<Vector3> path, out Vector3 min, out Vector3 max)
        {
            min = path[0];
            max = path[0];
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 p = path[i];
                min = new Vector3(Mathf.Min(min.x, p.x), p.y, Mathf.Min(min.z, p.z));
                max = new Vector3(Mathf.Max(max.x, p.x), p.y, Mathf.Max(max.z, p.z));
            }
        }

        private static float PolylineLengthXZ(List<Vector3> path)
        {
            if (path == null || path.Count < 2)
                return 0f;
            float s = 0f;
            for (int i = 1; i < path.Count; i++)
                s += HorizontalDistanceXZ(path[i - 1], path[i]);
            return s;
        }

        private static List<Vector3> DedupeNearlyDuplicateConsecutivePoints(List<Vector3> path, float eps, int riverIndex, MapGenConfig config)
        {
            var o = new List<Vector3>(path.Count);
            for (int i = 0; i < path.Count; i++)
            {
                if (o.Count == 0)
                {
                    o.Add(path[i]);
                    continue;
                }

                if (HorizontalDistanceXZ(o[o.Count - 1], path[i]) < eps)
                {
                    if (config != null && config.debugRiverRibbonGeometry)
                        Debug.Log($"[RiverRibbonDebug] dedupe skip near-duplicate | river={riverIndex} idx={i}");
                    continue;
                }

                o.Add(path[i]);
            }

            return o;
        }

        /// <summary>Muestreo uniforme por longitud de arco en XZ (antes del ribbon).</summary>
        private static List<Vector3> ResamplePolylineUniformXZ(List<Vector3> path, float stepWorld, float waterY)
        {
            if (path == null || path.Count == 0)
                return new List<Vector3>();
            if (path.Count == 1)
                return new List<Vector3> { new Vector3(path[0].x, waterY, path[0].z) };

            stepWorld = Mathf.Max(0.05f, stepWorld);
            float totalLen = PolylineLengthXZ(path);
            if (totalLen < 1e-5f)
                return new List<Vector3> { new Vector3(path[0].x, waterY, path[0].z) };

            var o = new List<Vector3>(Mathf.Max(8, Mathf.CeilToInt(totalLen / stepWorld) + 2));
            Vector3 first = new Vector3(path[0].x, waterY, path[0].z);
            o.Add(first);

            float targetDist = stepWorld;
            float acc = 0f;

            for (int seg = 0; seg < path.Count - 1; seg++)
            {
                Vector3 a = new Vector3(path[seg].x, waterY, path[seg].z);
                Vector3 b = new Vector3(path[seg + 1].x, waterY, path[seg + 1].z);
                float sl = HorizontalDistanceXZ(a, b);
                if (sl < 1e-7f)
                    continue;

                while (targetDist <= acc + sl + 1e-5f)
                {
                    float u = (targetDist - acc) / sl;
                    u = Mathf.Clamp01(u);
                    Vector3 p = Vector3.Lerp(a, b, u);
                    p.y = waterY;
                    o.Add(p);
                    targetDist += stepWorld;
                }

                acc += sl;
            }

            Vector3 last = new Vector3(path[path.Count - 1].x, waterY, path[path.Count - 1].z);
            if (HorizontalDistanceXZ(o[o.Count - 1], last) > 1e-4f)
                o.Add(last);
            else
                o[o.Count - 1] = last;

            return o;
        }

        private static Vector2 CatmullRom2D(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        /// <summary>Curva Catmull–Rom en XZ pasando por los puntos de control; remuestreo fino para cauce orgánico.</summary>
        private static List<Vector3> ResamplePolylineCatmullRomUniformXZ(List<Vector3> ctrl, float stepWorld, float waterY)
        {
            if (ctrl == null || ctrl.Count == 0)
                return new List<Vector3>();
            if (ctrl.Count == 1)
                return new List<Vector3> { new Vector3(ctrl[0].x, waterY, ctrl[0].z) };
            if (ctrl.Count == 2)
                return ResamplePolylineUniformXZ(ctrl, stepWorld, waterY);

            stepWorld = Mathf.Max(0.028f, stepWorld);
            var ext = new List<Vector2>(ctrl.Count + 2);
            Vector2 To2(Vector3 v) => new Vector2(v.x, v.z);
            ext.Add(To2(ctrl[0]) * 2f - To2(ctrl[1]));
            for (int i = 0; i < ctrl.Count; i++)
                ext.Add(To2(ctrl[i]));
            ext.Add(To2(ctrl[ctrl.Count - 1]) * 2f - To2(ctrl[ctrl.Count - 2]));

            var o = new List<Vector3>(Mathf.Max(16, Mathf.CeilToInt(PolylineLengthXZ(ctrl) / stepWorld) + 4));
            int nSeg = ctrl.Count - 1;
            for (int seg = 0; seg < nSeg; seg++)
            {
                Vector2 p0 = ext[seg];
                Vector2 p1 = ext[seg + 1];
                Vector2 p2 = ext[seg + 2];
                Vector2 p3 = ext[seg + 3];
                float chord = Vector2.Distance(p1, p2);
                int steps = Mathf.Max(2, Mathf.CeilToInt(chord / stepWorld));
                for (int s = 0; s < steps; s++)
                {
                    if (seg > 0 && s == 0)
                        continue;
                    float t = s / (float)steps;
                    Vector2 q = CatmullRom2D(p0, p1, p2, p3, t);
                    o.Add(new Vector3(q.x, waterY, q.y));
                }
            }

            Vector3 last = new Vector3(ctrl[ctrl.Count - 1].x, waterY, ctrl[ctrl.Count - 1].z);
            if (o.Count == 0 || HorizontalDistanceXZ(o[o.Count - 1], last) > 1e-4f)
                o.Add(last);
            else
                o[o.Count - 1] = last;

            return o;
        }

        private static List<Vector3> BuildWorldPathFromCellCenterline(GridSystem grid, float waterY, List<Vector2> cellPath)
        {
            float cs = Mathf.Max(0.0001f, grid.CellSizeWorld);
            var w = new List<Vector3>(cellPath.Count);
            foreach (var c in cellPath)
                w.Add(new Vector3(grid.Origin.x + c.x * cs, waterY, grid.Origin.z + c.y * cs));
            return w;
        }

        private static float PolylineLength2D(List<Vector2> path)
        {
            if (path == null || path.Count < 2)
                return 0f;
            float s = 0f;
            for (int i = 1; i < path.Count; i++)
                s += Vector2.Distance(path[i - 1], path[i]);
            return s;
        }

        private static List<Vector2> DedupeNearlyDuplicateConsecutivePointsCell(List<Vector2> path, float epsCells, int riverIndex, MapGenConfig config)
        {
            var o = new List<Vector2>(path != null ? path.Count : 0);
            if (path == null)
                return o;
            for (int i = 0; i < path.Count; i++)
            {
                if (o.Count == 0)
                {
                    o.Add(path[i]);
                    continue;
                }

                if (Vector2.Distance(o[o.Count - 1], path[i]) < epsCells)
                {
                    if (config != null && config.debugRiverRibbonGeometry)
                        Debug.Log($"[RiverRibbonDebug] dedupe skip near-duplicate (cell) | river={riverIndex} idx={i}");
                    continue;
                }

                o.Add(path[i]);
            }

            return o;
        }

        private static List<List<Vector2>> SplitPolylineAtExcessiveGapsCell(List<Vector2> path, float maxJumpCells, int riverIndex, MapGenConfig config)
        {
            var outRuns = new List<List<Vector2>>();
            if (path == null || path.Count < 2)
                return outRuns;

            var cur = new List<Vector2>();
            for (int i = 0; i < path.Count; i++)
            {
                if (cur.Count == 0)
                {
                    cur.Add(path[i]);
                    continue;
                }

                float d = Vector2.Distance(cur[cur.Count - 1], path[i]);
                if (d > maxJumpCells)
                {
                    int segLabel = outRuns.Count;
                    if (config != null && config.debugRiverRibbonGeometry)
                        Debug.LogWarning($"[RiverRibbonDebug] WARNING abnormal jump | river={riverIndex} segment={segLabel} idx={i} dist={d:F3} maxAllowed={maxJumpCells:F3} (cell-space)");

                    if (cur.Count >= 2)
                        outRuns.Add(new List<Vector2>(cur));
                    cur.Clear();
                    cur.Add(path[i]);
                    continue;
                }

                cur.Add(path[i]);
            }

            if (cur.Count >= 2)
                outRuns.Add(cur);

            if (outRuns.Count == 0 && path.Count >= 2)
                outRuns.Add(new List<Vector2>(path));

            return outRuns;
        }

        private static List<Vector2> SubdivideLongSegmentsCell2D(List<Vector2> path, float maxSegCells)
        {
            if (path == null || path.Count < 2)
                return path != null ? new List<Vector2>(path) : new List<Vector2>();
            maxSegCells = Mathf.Max(0.12f, maxSegCells);
            var o = new List<Vector2>(path.Count * 2);
            o.Add(path[0]);
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 a = o[o.Count - 1];
                Vector2 b = path[i];
                float d = Vector2.Distance(a, b);
                if (d <= maxSegCells + 1e-5f)
                {
                    o.Add(b);
                    continue;
                }

                int steps = Mathf.CeilToInt(d / maxSegCells);
                for (int s = 1; s < steps; s++)
                {
                    float t = s / (float)steps;
                    o.Add(Vector2.Lerp(a, b, t));
                }

                o.Add(b);
            }

            return o;
        }

        private static List<Vector2> ResamplePolylineUniformCell(List<Vector2> path, float stepCells)
        {
            if (path == null || path.Count == 0)
                return new List<Vector2>();
            if (path.Count == 1)
                return new List<Vector2> { path[0] };

            stepCells = Mathf.Max(0.04f, stepCells);
            float totalLen = PolylineLength2D(path);
            if (totalLen < 1e-5f)
                return new List<Vector2> { path[0] };

            var o = new List<Vector2>(Mathf.Max(8, Mathf.CeilToInt(totalLen / stepCells) + 2));
            o.Add(path[0]);

            float targetDist = stepCells;
            float acc = 0f;

            for (int seg = 0; seg < path.Count - 1; seg++)
            {
                Vector2 a = path[seg];
                Vector2 b = path[seg + 1];
                float sl = Vector2.Distance(a, b);
                if (sl < 1e-7f)
                    continue;

                while (targetDist <= acc + sl + 1e-5f)
                {
                    float u = (targetDist - acc) / sl;
                    u = Mathf.Clamp01(u);
                    o.Add(Vector2.Lerp(a, b, u));
                    targetDist += stepCells;
                }

                acc += sl;
            }

            Vector2 last = path[path.Count - 1];
            if (Vector2.Distance(o[o.Count - 1], last) > 1e-4f)
                o.Add(last);
            else
                o[o.Count - 1] = last;

            return o;
        }

        private static float HorizontalDistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float MaxConsecutiveHorizontalSegment(List<Vector3> path)
        {
            if (path == null || path.Count < 2)
                return 0f;
            float m = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                float d = HorizontalDistanceXZ(path[i - 1], path[i]);
                if (d > m)
                    m = d;
            }

            return m;
        }

        /// <summary>Parte la polilínea donde el salto consecutivo supera el umbral (antes de resample).</summary>
        private static List<List<Vector3>> SplitPolylineAtExcessiveGaps(List<Vector3> path, float maxSegWorld, int riverIndex, MapGenConfig config)
        {
            var outRuns = new List<List<Vector3>>();
            if (path == null || path.Count < 2)
                return outRuns;

            var cur = new List<Vector3>();
            for (int i = 0; i < path.Count; i++)
            {
                if (cur.Count == 0)
                {
                    cur.Add(path[i]);
                    continue;
                }

                float d = HorizontalDistanceXZ(cur[cur.Count - 1], path[i]);
                if (d > maxSegWorld)
                {
                    int segLabel = outRuns.Count;
                    if (config != null && config.debugRiverRibbonGeometry)
                        Debug.LogWarning($"[RiverRibbonDebug] WARNING abnormal jump | river={riverIndex} segment={segLabel} idx={i} dist={d:F3} maxAllowed={maxSegWorld:F3}");

                    if (cur.Count >= 2)
                        outRuns.Add(new List<Vector3>(cur));
                    cur.Clear();
                    cur.Add(path[i]);
                    continue;
                }

                cur.Add(path[i]);
            }

            if (cur.Count >= 2)
                outRuns.Add(cur);

            if (outRuns.Count == 0 && path.Count >= 2)
                outRuns.Add(new List<Vector3>(path));

            return outRuns;
        }

        /// <summary>Parte la centerline en espacio celda: tramos fuera de celdas Water (tras suavizado Laplaciano).</summary>
        private static List<List<Vector2>> SplitCellPolylineExcludingLakeCells(GridSystem grid, List<Vector2> path)
        {
            var runs = new List<List<Vector2>>();
            if (path == null || path.Count == 0)
                return runs;
            var cur = new List<Vector2>();
            for (int i = 0; i < path.Count; i++)
            {
                Vector2 pi = path[i];
                if (cur.Count == 0)
                {
                    if (!CellSampleIsLakeGrid(grid, pi))
                        cur.Add(pi);
                    continue;
                }

                Vector2 prev = cur[cur.Count - 1];
                Vector2 mid = (prev + pi) * 0.5f;
                if (CellSampleIsLakeGrid(grid, mid) || CellSampleIsLakeGrid(grid, pi))
                {
                    if (cur.Count >= 2)
                        runs.Add(new List<Vector2>(cur));
                    cur.Clear();
                    if (!CellSampleIsLakeGrid(grid, pi))
                        cur.Add(pi);
                    continue;
                }

                cur.Add(pi);
            }

            if (cur.Count >= 2)
                runs.Add(cur);
            return runs;
        }

        private static bool CellSampleIsLakeGrid(GridSystem grid, Vector2 cellSample)
        {
            int gx = Mathf.FloorToInt(cellSample.x);
            int gz = Mathf.FloorToInt(cellSample.y);
            if (!grid.InBoundsCell(gx, gz))
                return false;
            return grid.GetCell(gx, gz).type == CellType.Water;
        }

        /// <summary>Parte la polilínea en tramos cuyo punto/medio no cae en celda Water.</summary>
        private static List<List<Vector3>> SplitRiverPolylineExcludingLakeCells(GridSystem grid, float cellSize, List<Vector3> path, float waterY)
        {
            var runs = new List<List<Vector3>>();
            var cur = new List<Vector3>();

            for (int i = 0; i < path.Count; i++)
            {
                var pi = new Vector3(path[i].x, waterY, path[i].z);

                if (cur.Count == 0)
                {
                    if (!WorldXZIsLakeCell(grid, cellSize, pi))
                        cur.Add(pi);
                    continue;
                }

                Vector3 prev = cur[cur.Count - 1];
                Vector3 mid = (prev + pi) * 0.5f;
                if (WorldXZIsLakeCell(grid, cellSize, mid) || WorldXZIsLakeCell(grid, cellSize, pi))
                {
                    if (cur.Count >= 2)
                        runs.Add(new List<Vector3>(cur));
                    cur.Clear();
                    if (!WorldXZIsLakeCell(grid, cellSize, pi))
                        cur.Add(pi);
                    continue;
                }

                cur.Add(pi);
            }

            if (cur.Count >= 2)
                runs.Add(cur);
            return runs;
        }

        private static bool WorldXZIsLakeCell(GridSystem grid, float cellSize, Vector3 w)
        {
            int gx = Mathf.FloorToInt((w.x - grid.Origin.x) / cellSize);
            int gz = Mathf.FloorToInt((w.z - grid.Origin.z) / cellSize);
            if (!grid.InBoundsCell(gx, gz))
                return false;
            return grid.GetCell(gx, gz).type == CellType.Water;
        }

        /// <summary>Strip por segmento: tangente por arista, lateral estable (evita flip 180°) y semiancho opcionalmente modulado con ruido suave.</summary>
        private static bool TryBuildRiverRibbonStripMesh(Transform parent, List<Vector3> pts, float halfWidthWorld, float waterY, Material mat, int waterLayer, float cellSize, int riverIndex, int segmentIndex, MapGenConfig config, float ribbonResampleStepWorld, string objectName)
        {
            if (pts == null || pts.Count < 2)
                return false;

            float sampleW = config != null ? Mathf.Max(0.06f, config.riverVisualSampleSpacing) : 0.4f;
            float stepRef = ribbonResampleStepWorld > 1e-5f
                ? ribbonResampleStepWorld
                : Mathf.Clamp(sampleW * 0.38f, cellSize * 0.055f, cellSize * 0.34f);
            float maxEdge = Mathf.Clamp(stepRef * 1.38f, cellSize * 0.68f, cellSize * 2.25f);
            if (config != null && config.riverMaxSegmentLengthWorld > 1e-4f)
                maxEdge = Mathf.Min(maxEdge, config.riverMaxSegmentLengthWorld);

            var work = new List<Vector3>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
                work.Add(new Vector3(pts[i].x, waterY, pts[i].z));

            float varAmt = config != null ? Mathf.Clamp01(config.riverRibbonWidthVariation) : 0f;
            float freq = config != null ? Mathf.Max(0.001f, config.riverRibbonWidthNoiseFreq) : 0.1f;
            float phase = riverIndex * 19.17f + segmentIndex * 7.41f;
            float perlinBlend = config != null ? Mathf.Clamp01(config.riverRibbonPerlinWidthBlend) : 0f;
            float perlinFreq = config != null ? Mathf.Max(0.001f, config.riverRibbonPerlinWidthFreq) : 0.09f;
            float perlinOff = (config != null ? config.seed * 0.00011f : 0f) + riverIndex * 3.907f + segmentIndex * 2.173f;
            var halfPerVertex = new float[work.Count];
            float accLen = 0f;
            for (int i = 0; i < work.Count; i++)
            {
                if (i > 0)
                    accLen += HorizontalDistanceXZ(work[i - 1], work[i]);
                float n =
                    Mathf.Sin(accLen * freq * (Mathf.PI * 2f) + phase) * 0.40f +
                    Mathf.Sin(accLen * freq * 0.47f * (Mathf.PI * 2f) + phase * 1.3f) * 0.34f +
                    Mathf.Sin(accLen * freq * 0.13f * (Mathf.PI * 2f) + phase * 0.71f) * 0.28f;
                float halfBase = halfWidthWorld * (1f + varAmt * n * 1.22f);
                float perlin01 = Mathf.PerlinNoise(perlinOff + accLen * perlinFreq, perlinOff * 0.37f + segmentIndex * 0.19f);
                float widthMul = Mathf.Lerp(1f, 0.8f + perlin01 * 0.4f, perlinBlend);
                halfPerVertex[i] = halfBase * widthMul;
            }

            if (varAmt > 1e-4f && work.Count > 2)
            {
                for (int pass = 0; pass < 3; pass++)
                {
                    var copy = (float[])halfPerVertex.Clone();
                    for (int i = 1; i < work.Count - 1; i++)
                        halfPerVertex[i] = (copy[i - 1] + 2f * copy[i] + copy[i + 1]) * 0.25f;
                }
            }

            float minMul = config != null ? Mathf.Clamp(config.riverRibbonHalfWidthMinMul, 0.45f, 1f) : 0.66f;
            float maxMul = config != null ? Mathf.Clamp(config.riverRibbonHalfWidthMaxMul, minMul + 0.02f, 1.75f) : 1.42f;
            for (int i = 0; i < work.Count; i++)
                halfPerVertex[i] = Mathf.Clamp(halfPerVertex[i], halfWidthWorld * minMul, halfWidthWorld * maxMul);

            if (config != null && config.debugRiverVisualStats)
            {
                for (int i = 0; i < work.Count; i++)
                {
                    float h = halfPerVertex[i];
                    _riverVisualHalfSamples++;
                    _riverVisualHalfSum += h;
                    if (h < _riverVisualHalfMin) _riverVisualHalfMin = h;
                    if (h > _riverVisualHalfMax) _riverVisualHalfMax = h;
                }
            }

            Vector3 TangentAt(int idx)
            {
                int n = work.Count;
                if (n < 2)
                    return Vector3.forward;
                if (idx <= 0)
                {
                    Vector3 d = work[1] - work[0];
                    d.y = 0f;
                    return d.sqrMagnitude > 1e-10f ? d.normalized : Vector3.forward;
                }
                if (idx >= n - 1)
                {
                    Vector3 d = work[n - 1] - work[n - 2];
                    d.y = 0f;
                    return d.sqrMagnitude > 1e-10f ? d.normalized : Vector3.forward;
                }
                Vector3 dm = work[idx + 1] - work[idx - 1];
                dm.y = 0f;
                return dm.sqrMagnitude > 1e-10f ? dm.normalized : Vector3.forward;
            }

            var verts = new List<Vector3>((work.Count - 1) * 4);
            var uvs = new List<Vector2>((work.Count - 1) * 4);
            var tris = new List<int>((work.Count - 1) * 6);

            float uScale = Mathf.Max(halfWidthWorld * 2f, Mathf.Max(0.01f, cellSize) * 0.5f);
            float uAcc = 0f;
            Vector3 rPrev = Vector3.zero;
            bool haveR = false;

            for (int i = 0; i < work.Count - 1; i++)
            {
                Vector3 a = work[i];
                Vector3 b = work[i + 1];
                Vector3 d = b - a;
                d.y = 0f;
                float sl = d.magnitude;
                if (sl < 1e-5f)
                    continue;
                if (sl > maxEdge)
                {
                    if (config != null && config.debugRiverRibbonGeometry)
                        Debug.LogWarning($"[RiverRibbonDebug] WARNING abnormal jump | river={riverIndex} segment={segmentIndex} idx={i + 1} dist={sl:F3} maxAllowed={maxEdge:F3} (pre-triangulation)");
                    return false;
                }

                Vector3 ta = TangentAt(i);
                Vector3 tb = TangentAt(i + 1);
                Vector3 tMid = ta + tb;
                tMid.y = 0f;
                if (tMid.sqrMagnitude < 1e-12f)
                    tMid = d.sqrMagnitude > 1e-12f ? d.normalized : ta;
                else
                    tMid.Normalize();

                Vector3 r = Vector3.Cross(Vector3.up, tMid);
                r.y = 0f;
                if (r.sqrMagnitude < 1e-12f)
                {
                    if (config != null && config.debugRiverRibbonGeometry)
                        Debug.LogWarning($"[RiverRibbonDebug] WARNING invalid tangent | river={riverIndex} segment={segmentIndex} idx={i} (cross)");
                    continue;
                }

                r.Normalize();
                if (haveR && Vector3.Dot(rPrev, r) < 0f)
                    r = -r;
                rPrev = r;
                haveR = true;

                float hwA = halfPerVertex[i];
                float hwB = halfPerVertex[i + 1];

                int b0 = verts.Count;
                verts.Add(a - r * hwA);
                uvs.Add(new Vector2(uAcc / uScale, 0f));
                verts.Add(a + r * hwA);
                uvs.Add(new Vector2(uAcc / uScale, 1f));
                verts.Add(b + r * hwB);
                uvs.Add(new Vector2((uAcc + sl) / uScale, 1f));
                verts.Add(b - r * hwB);
                uvs.Add(new Vector2((uAcc + sl) / uScale, 0f));

                tris.Add(b0);
                tris.Add(b0 + 3);
                tris.Add(b0 + 1);
                tris.Add(b0 + 1);
                tris.Add(b0 + 3);
                tris.Add(b0 + 2);

                uAcc += sl;
            }

            if (verts.Count == 0 || tris.Count == 0)
                return false;

            var mesh = new Mesh();
            mesh.name = objectName;
            if (verts.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            var colors = new List<Color>(verts.Count);
            for (int i = 0; i < verts.Count; i++)
                colors.Add(Color.white);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.layer = waterLayer;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (mat != null)
            {
                mr.sharedMaterial = mat;
                mr.enabled = true;
            }

            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.renderingLayerMask = 1u;
            return true;
        }

        private static bool BuildRoundedWaterMarchingSquares(Transform parent, GridSystem grid, MapGenConfig config, Material mat, float y, float cellSize, int waterLayer, bool marchingSquaresLakesOnly)
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
                    bool include = marchingSquaresLakesOnly ? (t == CellType.Water) : (t == CellType.Water || t == CellType.River);
                    if (!include) continue;
                    if (gx < minX) minX = gx;
                    if (gz < minZ) minZ = gz;
                    if (gx > maxX) maxX = gx;
                    if (gz > maxZ) maxZ = gz;
                }
            }
            if (maxX < 0 || maxZ < 0)
            {
                if (config.debugLogs && !marchingSquaresLakesOnly)
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
                        coarseMask[x, z] = marchingSquaresLakesOnly ? (t == CellType.Water) : (t == CellType.Water || t == CellType.River);
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

                if (!marchingSquaresLakesOnly)
                {
                    for (int z = 0; z < rectH; z++)
                        for (int x = 0; x < rectW; x++)
                        {
                            int gx = rectMinX + x;
                            int gz = rectMinZ + z;
                            if (grid.GetCell(gx, gz).type == CellType.River)
                                coarseMask[x, z] = true;
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

            // Campo escalar: lagos ~planos; ríos = perfil por submuestra (euclídeo) + refuerzo post-blur para no caer bajo iso.
            float riverSoftStart = Mathf.Clamp(config.riverMsCellSoftStart01, 0f, 0.82f);
            var field = new float[sw, sh];
            for (int z = 0; z < sh; z++)
            {
                int iz = sampleZ0 + z;
                for (int x = 0; x < sw; x++)
                {
                    int ix = sampleX0 + x;
                    int gx = Mathf.Clamp(ix / effectiveSubdiv, 0, w - 1);
                    int gz = Mathf.Clamp(iz / effectiveSubdiv, 0, h - 1);
                    var t = grid.GetCell(gx, gz).type;

                    if (t == CellType.River)
                    {
                        if (marchingSquaresLakesOnly)
                            field[x, z] = 0f;
                        else
                            field[x, z] = RiverLatticeSoftValue(ix, iz, gx, gz, effectiveSubdiv, riverSoftStart);
                        continue;
                    }

                    bool inCoarse = true;
                    if (coarseMask != null)
                    {
                        int mx = Mathf.Clamp(gx - rectMinX, 0, rectW - 1);
                        int mz = Mathf.Clamp(gz - rectMinZ, 0, rectH - 1);
                        inCoarse = coarseMask[mx, mz];
                    }

                    if (t == CellType.Water)
                        field[x, z] = inCoarse ? 1f : 0f;
                    else
                        field[x, z] = 0f;
                }
            }

            // Ruido suave en lagos (solo MS lagos): desplaza el iso en la orilla tras el blur.
            if (marchingSquaresLakesOnly && config != null && config.lakeShoreMsNoiseAmplitude > 1e-5f)
            {
                float amp = Mathf.Clamp(config.lakeShoreMsNoiseAmplitude, 0f, 0.28f);
                float sc = Mathf.Max(0.015f, config.lakeShoreMsNoiseScale);
                float ox = (config.seed % 997) * 0.0371f;
                float oz = (config.seed / 997) * 0.0413f;
                for (int zi = 0; zi < sh; zi++)
                {
                    float worldZf = grid.Origin.z + (sampleZ0 + zi) * step;
                    for (int xi = 0; xi < sw; xi++)
                    {
                        int ix = sampleX0 + xi;
                        int iz = sampleZ0 + zi;
                        int gx = Mathf.Clamp(ix / effectiveSubdiv, 0, w - 1);
                        int gz = Mathf.Clamp(iz / effectiveSubdiv, 0, h - 1);
                        if (grid.GetCell(gx, gz).type != CellType.Water) continue;
                        float worldXf = grid.Origin.x + (sampleX0 + xi) * step;
                        float n = (Mathf.PerlinNoise(ox + worldXf * sc, oz + worldZf * sc) - 0.5f) * 2f * amp;
                        field[xi, zi] = Mathf.Clamp01(field[xi, zi] + n);
                    }
                }
            }

            // Campo continuo MS en celdas (solo si el río no va por ribbon aparte).
            if (!marchingSquaresLakesOnly
                && config.riverVisualUseContinuousField
                && grid.RiverCenterlinesCellSpace != null
                && grid.RiverCenterlinesCellSpace.Count > 0)
            {
                float halfW = Mathf.Max(0.08f, config.riverVisualHalfWidthCells);
                float soft = Mathf.Max(0.02f, config.riverVisualSoftnessCells);
                float strength = Mathf.Clamp01(config.riverVisualFieldStrength);
                float cullMargin = halfW + soft + 0.35f;
                for (int z = 0; z < sh; z++)
                {
                    int iz = sampleZ0 + z;
                    float cellZ = iz / (float)effectiveSubdiv;
                    for (int x = 0; x < sw; x++)
                    {
                        int ix = sampleX0 + x;
                        float cellX = ix / (float)effectiveSubdiv;
                        int gx = Mathf.Clamp(ix / effectiveSubdiv, 0, w - 1);
                        int gz = Mathf.Clamp(iz / effectiveSubdiv, 0, h - 1);
                        if (grid.GetCell(gx, gz).type == CellType.Water)
                            continue;

                        float d2 = MinDistSqPointToPolylinesCellSpace(cellX, cellZ, grid.RiverCenterlinesCellSpace, cullMargin);
                        if (d2 >= 1e20f)
                            continue;
                        float d = Mathf.Sqrt(d2);
                        float cont = RiverContinuousFieldFromDistance(d, halfW, soft) * strength;
                        if (cont > 1e-5f)
                            field[x, z] = Mathf.Max(field[x, z], cont);
                    }
                }
            }

            // Blur para redondear la máscara (suaviza esquinas).
            if (blurIters > 0)
                BoxBlur(field, sw, sh, blurRadius, blurIters);

            float landRiverClamp = Mathf.Clamp(iso - 0.028f, 0f, 0.92f);
            for (int z = 0; z < sh; z++)
            {
                int gz = Mathf.Clamp((sampleZ0 + z) / effectiveSubdiv, 0, h - 1);
                for (int x = 0; x < sw; x++)
                {
                    int gx = Mathf.Clamp((sampleX0 + x) / effectiveSubdiv, 0, w - 1);
                    var t = grid.GetCell(gx, gz).type;
                    // No forzar suelo en río: el perfil por submuestra + blur ya redondea; el mínimo plano devolvía bordes cuadrados.
                    if (t == CellType.Land && CellTouchesRiverCardinal(grid, gx, gz))
                        field[x, z] = Mathf.Min(field[x, z], landRiverClamp);
                }
            }

            float minAboveIso = Mathf.Max(0f, config.riverMsMinAboveIsoAfterBlur);
            if (!marchingSquaresLakesOnly && minAboveIso > 0.0005f)
            {
                float riverFloor = Mathf.Clamp(iso + minAboveIso, iso + 0.02f, 0.995f);
                for (int z = 0; z < sh; z++)
                {
                    int gz = Mathf.Clamp((sampleZ0 + z) / effectiveSubdiv, 0, h - 1);
                    for (int x = 0; x < sw; x++)
                    {
                        int gx = Mathf.Clamp((sampleX0 + x) / effectiveSubdiv, 0, w - 1);
                        if (grid.GetCell(gx, gz).type != CellType.River) continue;
                        field[x, z] = Mathf.Max(field[x, z], riverFloor);
                    }
                }
            }

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

        /// <summary>
        /// Valor 0–1 en la malla MS para celdas de río: alto al centro del tile, suave hacia los bordes (chamfer anti-escalera).
        /// </summary>
        private static float RiverLatticeSoftValue(int ix, int iz, int gx, int gz, int subdiv, float softStart01)
        {
            if (subdiv <= 1)
                return 1f;
            if (softStart01 <= 0.001f)
                return 1f;
            int rx = ix - gx * subdiv;
            int rz = iz - gz * subdiv;
            float fx = rx / (float)subdiv;
            float fz = rz / (float)subdiv;
            float ndx = (fx - 0.5f) * 2f;
            float ndz = (fz - 0.5f) * 2f;
            float r = Mathf.Clamp01(Mathf.Sqrt(ndx * ndx + ndz * ndz) / 1.41421356f);
            float t0 = Mathf.Clamp(softStart01, 0.22f, 0.82f);
            const float t1 = 0.992f;
            return 1f - Mathf.SmoothStep(t0, t1, r);
        }

        /// <summary>Campo 0–1 desde distancia euclídea al eje del río (en celdas).</summary>
        private static float RiverContinuousFieldFromDistance(float distCells, float halfWidthCells, float softnessCells)
        {
            float t = distCells - halfWidthCells;
            if (t <= 0f)
                return 1f;
            if (t >= softnessCells)
                return 0f;
            return 1f - Mathf.SmoothStep(0f, softnessCells, t);
        }

        private static float MinDistSqPointToPolylinesCellSpace(float px, float pz, List<List<Vector2>> polylines, float segmentCullMarginCells)
        {
            float best = float.MaxValue;
            foreach (var poly in polylines)
            {
                if (poly == null || poly.Count < 2)
                    continue;
                for (int i = 0; i < poly.Count - 1; i++)
                {
                    Vector2 a = poly[i];
                    Vector2 b = poly[i + 1];
                    float m = segmentCullMarginCells;
                    float minX = Mathf.Min(a.x, b.x) - m;
                    float maxX = Mathf.Max(a.x, b.x) + m;
                    float minY = Mathf.Min(a.y, b.y) - m;
                    float maxY = Mathf.Max(a.y, b.y) + m;
                    if (px < minX || px > maxX || pz < minY || pz > maxY)
                        continue;
                    float d2 = DistSqPointSegmentXY(px, pz, a.x, a.y, b.x, b.y);
                    if (d2 < best)
                        best = d2;
                }
            }

            return best;
        }

        private static float DistSqPointSegmentXY(float px, float pz, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax;
            float dy = by - ay;
            float len2 = dx * dx + dy * dy;
            if (len2 < 1e-12f)
            {
                float qx = px - ax;
                float qz = pz - ay;
                return qx * qx + qz * qz;
            }

            float t = Mathf.Clamp01(((px - ax) * dx + (pz - ay) * dy) / len2);
            float qx2 = px - (ax + t * dx);
            float qz2 = pz - (ay + t * dy);
            return qx2 * qx2 + qz2 * qz2;
        }

        private static bool CellTouchesRiverCardinal(GridSystem grid, int gx, int gz)
        {
            foreach (var n in grid.Neighbors4(gx, gz))
            {
                if (grid.GetCell(n.x, n.y).type == CellType.River)
                    return true;
            }
            return false;
        }

        /// <summary>Material para el agua. Aplica transparencia si config.waterAlpha &lt; 1 para ver la arena bajo el agua.</summary>
        private static Material GetOrCreateWaterMaterial(Material assigned, MapGenConfig config)
        {
            Material mat;
            if (assigned != null)
                mat = new Material(assigned);
            else
            {
                Shader river = Shader.Find("Project/RTS River Water");
                if (river != null)
                    mat = new Material(river);
                else
                {
                    Material fb = GetFallbackMaterial();
                    mat = fb != null ? new Material(fb) : null;
                }
            }

            if (mat == null)
            {
                Debug.LogError("WaterMeshBuilder: No se pudo crear material de agua.");
                return null;
            }

            if (mat.renderQueue < 0) mat.renderQueue = 2001;

            bool isRtsRiver = mat.shader != null && mat.shader.name.Contains("RTS River Water");
            if (assigned == null && !isRtsRiver)
            {
                Color azulAgua = new Color(0.25f, 0.48f, 0.75f, 1f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", azulAgua);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", azulAgua);
            }

            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_ZWriteControl")) mat.SetFloat("_ZWriteControl", 0f);
            if (mat.HasProperty("_ZWriteMode")) mat.SetFloat("_ZWriteMode", 0f);

            float alpha = (config != null && config.waterAlpha > 0f) ? Mathf.Clamp01(config.waterAlpha) : 1f;
            if (isRtsRiver)
                ApplyRiverWaterShaderProperties(mat, config);
            else if (alpha < 0.99f)
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

        static void ApplyRiverWaterShaderProperties(Material mat, MapGenConfig config)
        {
            if (mat == null || config == null) return;
            if (!mat.HasProperty("_ShallowColor")) return;
            mat.SetColor("_ShallowColor", config.riverWaterShallowColor);
            mat.SetColor("_DeepColor", config.riverWaterDeepColor);
            mat.SetVector("_FlowSpeed", new Vector4(config.riverUVFlowSpeed.x, config.riverUVFlowSpeed.y, 0f, 0f));
            mat.SetFloat("_BankSoft", Mathf.Clamp(config.riverBankBlendStrength, 0.05f, 0.55f));
            mat.SetFloat("_Alpha", Mathf.Clamp01(config.waterAlpha));
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
