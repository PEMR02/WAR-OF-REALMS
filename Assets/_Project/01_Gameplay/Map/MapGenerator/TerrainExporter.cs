using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 9: exporta grid a Terrain (heightmap + alphamaps por altura).</summary>
    public static class TerrainExporter
    {
        /// <summary>Relleno en <see cref="PaintTerrainByHeight"/> si <see cref="MapGenConfig.debugTerrainMoisture"/>.</summary>
        public static float[,] DebugLastMoisture01 { get; private set; }
        /// <summary>Relleno si <see cref="MapGenConfig.debugTerrainMacro"/>.</summary>
        public static float[,] DebugLastMacro01 { get; private set; }
        /// <summary>Relleno si <see cref="MapGenConfig.debugTerrainGrassDry"/> (1 = pasto seco).</summary>
        public static float[,] DebugLastGrassDryMix01 { get; private set; }

        static void ClearSplatDebugBuffers()
        {
            DebugLastMoisture01 = null;
            DebugLastMacro01 = null;
            DebugLastGrassDryMix01 = null;
        }

        /// <summary>Parámetros: config + override de layers. tileSize > 0 reduce repetición. sand = orillas lagos/ríos.</summary>
        public static void ApplyToTerrain(Terrain terrain, GridSystem grid, MapGenConfig config,
            TerrainLayer grassOverride = null, TerrainLayer dirtOverride = null, TerrainLayer rockOverride = null,
            Vector2 grassTileSize = default, Vector2 dirtTileSize = default, Vector2 rockTileSize = default,
            TerrainLayer sandOverride = null, Vector2 sandTileSize = default, int sandShoreCells = 3)
        {
            if (terrain == null || grid == null || config == null) return;

            // Un Terrain en escena puede no tener TerrainData asignado (asset borrado o prefab sin datos).
            // Antes se hacía return aquí y el pipeline seguía llamando SampleHeight() → error en runtime.
            var data = terrain.terrainData;
            if (data == null)
            {
                data = new TerrainData();
                terrain.terrainData = data;
                var tc = terrain.GetComponent<TerrainCollider>();
                if (tc != null)
                    tc.terrainData = data;
                Debug.LogWarning(
                    "TerrainExporter: el Terrain no tenía TerrainData; se creó uno en runtime. " +
                    "Asigna un TerrainData en el Inspector o conserva el asset en el repo para evitar esto.");
            }
            int res = Mathf.Clamp(config.heightmapResolution, 33, 4097);
            float w = grid.Width * grid.CellSizeWorld;
            float h = grid.Height * grid.CellSizeWorld;
            data.heightmapResolution = res;
            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            data.size = new Vector3(w, terrainY, h);
            terrain.transform.position = config.origin;
            int desiredAlphamap = Mathf.Clamp(Mathf.Max(256, (res - 1) / 2), 16, 1024);
            try { data.alphamapResolution = desiredAlphamap; } catch { }

            float[,] heights = new float[res, res];
            // Suavizado de orilla (visual): acerca el terreno a waterHeight01 en un radio de celdas.
            // Esto elimina "escalones" duros al juntarse con el agua (sin tocar el grid lógico).
            var smoothedCellHeights = BuildShoreSmoothedCellHeights(grid, config);
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float u = (float)x / (res - 1);
                    float v = (float)y / (res - 1);
                    // Bilinear sampling entre celdas para evitar bloques (y mejorar la transición con el agua).
                    float gxF = u * (grid.Width - 1);
                    float gzF = v * (grid.Height - 1);
                    int gx0 = Mathf.Clamp(Mathf.FloorToInt(gxF), 0, grid.Width - 1);
                    int gz0 = Mathf.Clamp(Mathf.FloorToInt(gzF), 0, grid.Height - 1);
                    int gx1 = Mathf.Clamp(gx0 + 1, 0, grid.Width - 1);
                    int gz1 = Mathf.Clamp(gz0 + 1, 0, grid.Height - 1);
                    float tx = Mathf.Clamp01(gxF - gx0);
                    float tz = Mathf.Clamp01(gzF - gz0);

                    float h00 = smoothedCellHeights[gx0, gz0];
                    float h10 = smoothedCellHeights[gx1, gz0];
                    float h01 = smoothedCellHeights[gx0, gz1];
                    float h11 = smoothedCellHeights[gx1, gz1];

                    float hx0 = Mathf.Lerp(h00, h10, tx);
                    float hx1 = Mathf.Lerp(h01, h11, tx);
                    heights[y, x] = Mathf.Lerp(hx0, hx1, tz);
                }
            }

            int smoothPasses = Mathf.Max(0, config.terrainNormalSmoothingPasses);
            float smoothStr = Mathf.Clamp01(config.terrainNormalSmoothingStrength);
            if (smoothPasses > 0 && smoothStr > 1e-5f)
                ApplyHeightmapNeighborSmoothing(heights, res, smoothPasses, smoothStr);

            data.SetHeights(0, 0, heights);

            TerrainLayer g = ApplyTileSize(grassOverride != null ? grassOverride : config.grassLayer, grassTileSize);
            TerrainLayer d = ApplyTileSize(dirtOverride != null ? dirtOverride : config.dirtLayer, dirtTileSize);
            TerrainLayer r = ApplyTileSize(rockOverride != null ? rockOverride : config.rockLayer, rockTileSize);
            TerrainLayer s = ApplyTileSize(sandOverride != null ? sandOverride : config.sandLayer, sandTileSize);
            TerrainLayer fordBed = null;
            if (s != null && config.riverFordBedLayer != null)
                fordBed = ApplyTileSize(config.riverFordBedLayer, sandTileSize);
            int shoreCells = sandShoreCells > 0 ? sandShoreCells : config.sandShoreCells;
            if (config.paintTerrainByHeight && (g != null || d != null || r != null))
            {
                PaintTerrainByHeight(data, heights, res, config, grid, g, d, r, s, shoreCells, fordBed,
                    grassTileSize, dirtTileSize);
                EnsureTerrainMaterialSupportsLayers(terrain);
            }
            else
            {
                ClearSplatDebugBuffers();
                if (config.paintTerrainByHeight && g == null && d == null && r == null)
                    Debug.LogWarning("TerrainExporter: Paint Terrain By Height activado pero no hay Grass/Dirt/Rock layers. Asigna Texture_Grass, Texture_Dirt, Texture_Rock en el RTS o en MapGenConfig.");
            }

            if (config.terrainMaterialTemplateOverride != null)
                terrain.materialTemplate = config.terrainMaterialTemplateOverride;

            if (config.debugLogs)
                Debug.Log($"Fase9 TerrainExport: heightmap {res}x{res}, size={data.size}, texturas={(g != null || d != null || r != null ? "aplicadas" : "no")}.");

            // Volumen visual: paredes laterales + base (Terrain Skirt)
            // Pasamos el mismo array heights (valores 0-1) para muestrear bordes
            // directamente, sin depender de terrain.SampleHeight() que puede
            // tener un frame de retraso tras SetHeights().
            if (config.showTerrainSkirt)
                TerrainSkirtBuilder.BuildSkirt(terrain, config, heights);
        }

        /// <summary>Suaviza heightmap 0–1 hacia el promedio de vecinos (solo visual).</summary>
        static void ApplyHeightmapNeighborSmoothing(float[,] heights, int res, int passes, float strength)
        {
            if (res < 3 || passes <= 0) return;
            var work = new float[res, res];
            for (int p = 0; p < passes; p++)
            {
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                        work[y, x] = heights[y, x];

                for (int y = 1; y < res - 1; y++)
                {
                    for (int x = 1; x < res - 1; x++)
                    {
                        float avg = (work[y - 1, x] + work[y + 1, x] + work[y, x - 1] + work[y, x + 1]) * 0.25f;
                        heights[y, x] = Mathf.Clamp01(Mathf.Lerp(work[y, x], avg, strength));
                    }
                }
            }
        }

        static float SampleHeightBilinear(float[,] heights, int res, float fx, float fz)
        {
            fx = Mathf.Clamp(fx, 0f, res - 1);
            fz = Mathf.Clamp(fz, 0f, res - 1);
            int x0 = Mathf.FloorToInt(fx);
            int z0 = Mathf.FloorToInt(fz);
            int x1 = Mathf.Min(x0 + 1, res - 1);
            int z1 = Mathf.Min(z0 + 1, res - 1);
            float tx = fx - x0;
            float tz = fz - z0;
            float h00 = heights[z0, x0];
            float h10 = heights[z0, x1];
            float h01 = heights[z1, x0];
            float h11 = heights[z1, x1];
            float a = Mathf.Lerp(h00, h10, tx);
            float b = Mathf.Lerp(h01, h11, tx);
            return Mathf.Lerp(a, b, tz);
        }

        static void SmoothAlphamapsBox(TerrainData data, int passes)
        {
            if (passes <= 0 || data == null) return;
            int aw = data.alphamapWidth;
            int ah = data.alphamapHeight;
            int layers = data.alphamapLayers;
            if (aw < 2 || ah < 2 || layers < 1) return;

            float[,,] map = data.GetAlphamaps(0, 0, aw, ah);
            var tmp = new float[ah, aw, layers];

            for (int p = 0; p < passes; p++)
            {
                for (int y = 0; y < ah; y++)
                {
                    for (int x = 0; x < aw; x++)
                    {
                        for (int l = 0; l < layers; l++)
                        {
                            float sum = 0f;
                            int count = 0;
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                int yy = Mathf.Clamp(y + dy, 0, ah - 1);
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int xx = Mathf.Clamp(x + dx, 0, aw - 1);
                                    sum += map[yy, xx, l];
                                    count++;
                                }
                            }
                            tmp[y, x, l] = sum / count;
                        }
                    }
                }

                for (int y = 0; y < ah; y++)
                {
                    for (int x = 0; x < aw; x++)
                    {
                        float s = 0f;
                        for (int l = 0; l < layers; l++)
                            s += tmp[y, x, l];
                        if (s > 1e-6f)
                        {
                            for (int l = 0; l < layers; l++)
                                map[y, x, l] = tmp[y, x, l] / s;
                        }
                        else
                        {
                            map[y, x, 0] = 1f;
                            for (int l = 1; l < layers; l++)
                                map[y, x, l] = 0f;
                        }
                    }
                }
            }

            data.SetAlphamaps(0, 0, map);
        }

        static TerrainLayer ApplyTileSize(TerrainLayer layer, Vector2 tileSize)
        {
            if (layer == null) return null;
            if (tileSize.x <= 0f && tileSize.y <= 0f) return layer;
            TerrainLayer clone = UnityEngine.Object.Instantiate(layer);
            clone.tileSize = new Vector2(tileSize.x > 0f ? tileSize.x : layer.tileSize.x, tileSize.y > 0f ? tileSize.y : layer.tileSize.y);
            return clone;
        }

        static float[,] BuildShoreSmoothedCellHeights(GridSystem grid, MapGenConfig config)
        {
            int w = grid.Width;
            int h = grid.Height;
            var outH = BuildLogicalCellHeightSnapshot(grid);
            float waterH = config.waterHeight01;

            int radius = Mathf.Max(0, config.shoreSmoothRadiusCells);
            float strength = Mathf.Clamp01(config.shoreSmoothStrength);
            if (radius <= 0 || strength <= 0.0001f) return outH;

            ApplyVisualShorelineSmoothing(outH, grid, waterH, radius, strength);
            ApplyRiverTerrainChannelCarve(outH, grid, config);
            return outH;
        }

        static float[,] BuildLogicalCellHeightSnapshot(GridSystem grid)
        {
            int w = grid.Width;
            int h = grid.Height;
            var snapshot = new float[w, h];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    snapshot[x, z] = grid.GetCell(x, z).height01;
            return snapshot;
        }

        /// <summary>
        /// Deformación puramente visual sobre el snapshot lógico antes de muestrear el Terrain.
        /// No altera el grid ni la jugabilidad; solo suaviza el encuentro tierra/agua.
        /// </summary>
        static void ApplyVisualShorelineSmoothing(float[,] outH, GridSystem grid, float waterH, int radius, float strength)
        {
            int w = grid.Width;
            int h = grid.Height;
            // Multi-source BFS para distancia (en celdas) al agua más cercana.
            var dist = new int[w, h];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    dist[x, z] = -1;

            var qx = new Queue<int>();
            var qz = new Queue<int>();

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    var t = grid.GetCell(x, z).type;
                    if (t == CellType.Water || t == CellType.River)
                    {
                        dist[x, z] = 0;
                        qx.Enqueue(x);
                        qz.Enqueue(z);
                        // Lago = nivel superficial; río = lecho ya bajado en Fase4 (HeightGenerator).
                        outH[x, z] = grid.GetCell(x, z).height01;
                    }
                }
            }

            // BFS limitado al radio.
            while (qx.Count > 0)
            {
                int x = qx.Dequeue();
                int z = qz.Dequeue();
                int d = dist[x, z];
                if (d >= radius) continue;

                void Try(int nx, int nz)
                {
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)h) return;
                    if (dist[nx, nz] != -1) return;
                    dist[nx, nz] = d + 1;
                    qx.Enqueue(nx);
                    qz.Enqueue(nz);
                }

                Try(x - 1, z);
                Try(x + 1, z);
                Try(x, z - 1);
                Try(x, z + 1);
            }

            // Aplicar suavizado a tierra en función de la distancia al agua.
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    var t = grid.GetCell(x, z).type;
                    if (t == CellType.Water || t == CellType.River) continue;
                    int d = dist[x, z];
                    if (d <= 0 || d > radius) continue;

                    // d=1 -> casi al nivel del agua, d=radius -> casi sin efecto.
                    float k = 1f - (float)d / (radius + 1f);
                    k *= strength;
                    outH[x, z] = Mathf.Max(waterH, Mathf.Lerp(outH[x, z], waterH, k));
                }
            }
        }

        /// <summary>Tallada visual del cauce: más hondo hacia el centro del río, falloff desde el borde con tierra.</summary>
        static void ApplyRiverTerrainChannelCarve(float[,] outH, GridSystem grid, MapGenConfig config)
        {
            if (config == null || grid == null) return;
            float depthW = config.riverTerrainCarveDepthWorld;
            if (depthW < 1e-4f) return;

            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            float depth01 = Mathf.Clamp(depthW, 0f, 3f) / terrainY;
            int falloff = Mathf.Clamp(config.riverTerrainCarveFalloffCells, 1, 32);
            float curve = Mathf.Clamp(config.riverTerrainCarveCenterCurve, 0.35f, 3.5f);
            float fordMul = Mathf.Clamp(config.riverTerrainCarveFordMul, 0.08f, 1f);

            int w = grid.Width;
            int h = grid.Height;
            var dBank = new int[w, h];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    dBank[x, z] = -1;

            var qx = new Queue<int>();
            var qz = new Queue<int>();

            bool RiverTouchesLand8(int x, int z)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = x + dx, nz = z + dz;
                        if ((uint)nx >= (uint)w || (uint)nz >= (uint)h) continue;
                        if (grid.GetCell(nx, nz).type == CellType.Land)
                            return true;
                    }
                }
                return false;
            }

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (grid.GetCell(x, z).type != CellType.River) continue;
                    if (!RiverTouchesLand8(x, z)) continue;
                    dBank[x, z] = 0;
                    qx.Enqueue(x);
                    qz.Enqueue(z);
                }
            }

            while (qx.Count > 0)
            {
                int x = qx.Dequeue();
                int z = qz.Dequeue();
                int d = dBank[x, z];
                void TryRiverNeighbor(int nx, int nz)
                {
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)h) return;
                    if (grid.GetCell(nx, nz).type != CellType.River) return;
                    if (dBank[nx, nz] != -1) return;
                    dBank[nx, nz] = d + 1;
                    qx.Enqueue(nx);
                    qz.Enqueue(nz);
                }
                TryRiverNeighbor(x - 1, z);
                TryRiverNeighbor(x + 1, z);
                TryRiverNeighbor(x, z - 1);
                TryRiverNeighbor(x, z + 1);
            }

            int carved = 0;
            double sumCarve = 0.0;
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (grid.GetCell(x, z).type != CellType.River) continue;
                    int db = dBank[x, z];
                    if (db < 0) continue;
                    float u = Mathf.Clamp01(db / (float)falloff);
                    float profile = Mathf.Pow(u, curve);
                    float carve = depth01 * profile;
                    if (grid.GetCell(x, z).riverFord)
                        carve *= fordMul;
                    if (carve < 1e-8f) continue;
                    outH[x, z] = Mathf.Clamp01(outH[x, z] - carve);
                    carved++;
                    sumCarve += carve;
                }
            }

            if (carved > 0 && config.debugRiverVisualStats)
            {
                double avg = sumCarve / carved;
                Debug.Log($"[RiverVisual] Tallada cauce: profundidadCfg={depthW:F2}u falloff={falloff} curva={curve:F2} fordMul={fordMul:F2} | celdas={carved} carve01_medio={(float)avg:F4}");
            }
        }

        static void EnsureTerrainMaterialSupportsLayers(Terrain t)
        {
            if (t == null) return;
            var mat = t.materialTemplate;
            bool needs = mat == null || mat.shader == null;
            if (!needs)
            {
                string name = mat.shader.name;
                needs = !name.Contains("Terrain/Lit") && !name.Contains("Terrain/Standard") && !name.Contains("Nature/Terrain");
            }
            if (needs)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Terrain/Lit") ?? Shader.Find("Terrain/Lit") ?? Shader.Find("Nature/Terrain/Standard") ?? Shader.Find("Terrain/Standard");
                if (s != null) t.materialTemplate = new Material(s);
            }
        }

        static float SampleShoreDistanceBilinear(int[,] shoreDist, int gw, int gh, float gxf, float gzf)
        {
            int gx0 = Mathf.Clamp((int)gxf, 0, gw - 1);
            int gz0 = Mathf.Clamp((int)gzf, 0, gh - 1);
            int gx1 = Mathf.Clamp(gx0 + 1, 0, gw - 1);
            int gz1 = Mathf.Clamp(gz0 + 1, 0, gh - 1);
            float tx = Mathf.Clamp01(gxf - gx0);
            float tz = Mathf.Clamp01(gzf - gz0);
            float d00 = shoreDist[gx0, gz0];
            float d10 = shoreDist[gx1, gz0];
            float d01 = shoreDist[gx0, gz1];
            float d11 = shoreDist[gx1, gz1];
            return Mathf.Lerp(Mathf.Lerp(d00, d10, tx), Mathf.Lerp(d01, d11, tx), tz);
        }

        /// <summary>Máscara 0–1: 1 junto al agua, 0 lejos. Incluye ruido sobre la distancia.</summary>
        static float EvaluateMoistureMask01(float distCells, float radius, float noise01, float noiseStrength,
            int x, int y, int seed, float noiseScale)
        {
            if (radius < 0.25f) return 0f;
            float n = (noise01 - 0.5f) * 2f;
            float warp = n * noiseStrength * Mathf.Max(0.5f, radius * 0.35f);
            float dEff = Mathf.Max(0f, distCells + warp);
            float radial = 1f - Mathf.Clamp01(dEff / radius);
            float fine = Mathf.PerlinNoise(x * noiseScale * 0.19f + seed * 0.031f, y * noiseScale * 0.19f + seed * 0.027f);
            float breakup = Mathf.Lerp(1f, Mathf.Lerp(0.65f, 1f, fine), noiseStrength);
            return Mathf.Clamp01(radial * breakup);
        }

        static void AbsorbVirtualWeightsIntoRealSoil(ref float g, ref float d, ref float r, bool hasGrass, bool hasDirt, bool hasRock)
        {
            if (!hasRock) { d += r; r = 0f; }
            if (!hasDirt) { g += d; d = 0f; }
            if (!hasGrass) { d += g; g = 0f; }
            float s = g + d + r;
            if (s > 1e-5f) { g /= s; d /= s; r /= s; }
        }

        static void ApplySlopeToSoil(float[,] heights, int res, float hx, float hy, float sts,
            ref float g, ref float d, ref float r)
        {
            if (sts < 1e-5f) return;
            float dhdx = (SampleHeightBilinear(heights, res, hx + 1f, hy) - SampleHeightBilinear(heights, res, hx - 1f, hy)) * 0.5f;
            float dhdz = (SampleHeightBilinear(heights, res, hx, hy + 1f) - SampleHeightBilinear(heights, res, hx, hy - 1f)) * 0.5f;
            float slopeMag = Mathf.Sqrt(dhdx * dhdx + dhdz * dhdz);
            float push = Mathf.Clamp01(slopeMag * sts * 22f);
            r = Mathf.Min(1f, r + push);
            float scale = 1f - push * 0.55f;
            g *= scale;
            d *= scale;
            float sumSlope = g + d + r;
            if (sumSlope > 1e-5f)
            {
                g /= sumSlope;
                d /= sumSlope;
                r /= sumSlope;
            }
        }

        static void ApplyBlendSharpnessContrast(ref float g, ref float d, ref float r, float sharp)
        {
            if (sharp < 1e-5f) return;
            float e = Mathf.Lerp(1f, 1.85f, Mathf.Clamp01(sharp));
            g = Mathf.Pow(Mathf.Max(1e-6f, g), e);
            d = Mathf.Pow(Mathf.Max(1e-6f, d), e);
            r = Mathf.Pow(Mathf.Max(1e-6f, r), e);
            float s = g + d + r;
            if (s > 1e-5f)
            {
                g /= s;
                d /= s;
                r /= s;
            }
        }

        static void PaintTerrainByHeight(TerrainData data, float[,] heights, int res, MapGenConfig config,
            GridSystem grid, TerrainLayer grassLayer, TerrainLayer dirtLayer, TerrainLayer rockLayer,
            TerrainLayer sandLayer, int sandShoreCells, TerrainLayer riverFordBedLayer,
            Vector2 grassTileSize, Vector2 dirtTileSize)
        {
            ClearSplatDebugBuffers();

            bool hasGrass = grassLayer != null;
            bool hasDirt = dirtLayer != null;
            bool hasRock = rockLayer != null;
            bool useGrassDry = hasGrass && config.grassDryLayer != null && config.grassDryBlendStrength > 1e-5f;
            bool useWet = config.wetDirtLayer != null && grid != null && config.terrainMoistureStrength > 1e-5f;
            bool useSand = sandLayer != null && grid != null && sandShoreCells > 0;
            bool useFordBed = useSand && riverFordBedLayer != null;

            var layers = new List<TerrainLayer>();
            int iGrass = -1, iGrassDry = -1, iDirt = -1, iRock = -1, iWet = -1, iSand = -1, iFord = -1;

            if (hasGrass) { iGrass = layers.Count; layers.Add(grassLayer); }
            if (useGrassDry)
            {
                iGrassDry = layers.Count;
                layers.Add(ApplyTileSize(config.grassDryLayer, grassTileSize));
            }
            if (hasDirt) { iDirt = layers.Count; layers.Add(dirtLayer); }
            if (hasRock) { iRock = layers.Count; layers.Add(rockLayer); }
            if (useWet)
            {
                iWet = layers.Count;
                layers.Add(ApplyTileSize(config.wetDirtLayer, dirtTileSize));
            }
            if (useSand) { iSand = layers.Count; layers.Add(sandLayer); }
            if (useFordBed) { iFord = layers.Count; layers.Add(riverFordBedLayer); }

            if (layers.Count == 0) return;

            bool classicGrassDirtPair = hasGrass && hasDirt && !hasRock && !useGrassDry && !useWet && !useSand;

            data.terrainLayers = layers.ToArray();
            int aw = data.alphamapWidth;
            int ah = data.alphamapHeight;
            if (aw <= 0 || ah <= 0)
            {
                Debug.LogWarning("TerrainExporter: alphamap inválido (width=" + aw + ", height=" + ah + "). Asigna Terrain Layers y asegura que el Terrain Data tenga alphamapResolution.");
                return;
            }

            int numLayers = layers.Count;
            float[,,] map = new float[ah, aw, numLayers];

            float totalPct = config.grassPercent01 + config.dirtPercent01 + config.rockPercent01;
            float gMax, dMax;
            if (totalPct > 0.001f)
            {
                float gp = config.grassPercent01 / totalPct;
                float dp = config.dirtPercent01 / totalPct;
                gMax = gp;
                dMax = gp + dp;
            }
            else
            {
                gMax = config.grassMaxHeight01;
                dMax = config.dirtMaxHeight01;
            }

            float blend = Mathf.Clamp(config.textureBlendWidth, 0.02f, 0.2f);
            float sharp = Mathf.Clamp01(config.terrainBlendSharpness);
            float blendEff = blend * Mathf.Lerp(1f, 0.28f, sharp);

            float minH = float.MaxValue, maxH = float.MinValue;
            for (int iy = 0; iy < res; iy++)
                for (int ix = 0; ix < res; ix++)
                {
                    float v = heights[iy, ix];
                    if (v < minH) minH = v;
                    if (v > maxH) maxH = v;
                }
            float rangeH = maxH - minH;
            if (rangeH < 0.001f) rangeH = 1f;

            int[,] shoreDist = useSand ? BuildShoreDistanceGrid(grid, sandShoreCells + 1) : null;
            int moistureMaxDist = 1;
            if (useWet)
                moistureMaxDist = Mathf.Clamp(Mathf.CeilToInt(config.terrainMoistureRadius) + 4, 1, 512);
            int[,] moistureDist = useWet ? BuildShoreDistanceGrid(grid, moistureMaxDist) : null;

            int gw = grid != null ? grid.Width : 0;
            int gh = grid != null ? grid.Height : 0;

            float sandFalloffPow = Mathf.Clamp(config.sandShoreFalloffPower, 1f, 4f);
            float sandDistNoise = Mathf.Max(0f, config.sandShoreExtraDistanceNoise);
            float sandSoilContrast = Mathf.Clamp(config.sandSoilContrastNearShore, 1f, 2.6f);

            bool dbgMoisture = config.debugTerrainMoisture;
            bool dbgMacro = config.debugTerrainMacro;
            bool dbgGrassDry = config.debugTerrainGrassDry;
            float[,] bufMoisture = dbgMoisture ? new float[ah, aw] : null;
            float[,] bufMacro = dbgMacro ? new float[ah, aw] : null;
            float[,] bufGrassDry = dbgGrassDry ? new float[ah, aw] : null;

            float macroStr = Mathf.Max(0f, config.terrainMacroNoiseStrength);
            float macroSc = Mathf.Max(0.001f, config.terrainMacroNoiseScale);

            for (int y = 0; y < ah; y++)
            {
                for (int x = 0; x < aw; x++)
                {
                    float hx = aw > 1 ? (float)x / (aw - 1) * (res - 1) : 0f;
                    float hy = ah > 1 ? (float)y / (ah - 1) * (res - 1) : 0f;

                    if (numLayers == 1)
                    {
                        if (bufMacro != null)
                        {
                            float mn = Mathf.PerlinNoise(x * macroSc * 0.11f + config.seed * 0.013f, y * macroSc * 0.11f + config.seed * 0.019f);
                            bufMacro[y, x] = mn;
                        }
                        if (bufMoisture != null) bufMoisture[y, x] = 0f;
                        if (bufGrassDry != null) bufGrassDry[y, x] = 0f;
                        for (int li = 0; li < numLayers; li++)
                            map[y, x, li] = li == 0 ? 1f : 0f;
                        continue;
                    }

                    float hRaw = SampleHeightBilinear(heights, res, hx, hy);
                    float hNorm = Mathf.Clamp01((hRaw - minH) / rangeH);

                    float htStr = Mathf.Clamp01(config.terrainHeightTintStrength);
                    if (htStr > 1e-5f)
                        hNorm = Mathf.Clamp01(hNorm + htStr * (hNorm - 0.5f) * 0.5f);

                    float macroNoise = 0.5f;
                    if (macroStr > 1e-5f)
                    {
                        macroNoise = Mathf.PerlinNoise(x * macroSc * 0.11f + config.seed * 0.013f, y * macroSc * 0.11f + config.seed * 0.019f);
                        hNorm = Mathf.Clamp01(hNorm + (macroNoise - 0.5f) * 2f * macroStr);
                    }
                    if (bufMacro != null)
                        bufMacro[y, x] = macroNoise;

                    float ns = Mathf.Clamp(config.terrainNoiseStrength, 0f, 0.35f);
                    if (ns > 1e-5f)
                    {
                        float sc = Mathf.Max(0.02f, config.terrainNoiseScale);
                        float n = Mathf.PerlinNoise(x * sc * 0.17f + config.seed * 0.01f, y * sc * 0.17f + config.seed * 0.017f);
                        hNorm = Mathf.Clamp01(hNorm + (n - 0.5f) * 2f * ns);
                    }

                    float h = hNorm;

                    float g, d, r;
                    if (classicGrassDirtPair)
                    {
                        g = 1f - h; d = h; r = 0f;
                    }
                    else
                    {
                        PaintThreeLayers(h, gMax, dMax, blendEff, out g, out d, out r);
                    }

                    AbsorbVirtualWeightsIntoRealSoil(ref g, ref d, ref r, hasGrass, hasDirt, hasRock);

                    if (hasRock)
                    {
                        float sts = Mathf.Clamp01(config.terrainSlopeTintStrength);
                        ApplySlopeToSoil(heights, res, hx, hy, sts, ref g, ref d, ref r);
                    }

                    ApplyBlendSharpnessContrast(ref g, ref d, ref r, sharp);

                    Color tb = config.terrainBaseColor;
                    if (Mathf.Abs(tb.r - 1f) + Mathf.Abs(tb.g - 1f) + Mathf.Abs(tb.b - 1f) > 0.02f)
                    {
                        g *= Mathf.Max(0.05f, tb.g);
                        d *= Mathf.Max(0.05f, tb.r);
                        r *= Mathf.Max(0.05f, tb.b);
                        float sumT = g + d + r;
                        if (sumT > 1e-5f)
                        {
                            g /= sumT;
                            d /= sumT;
                            r /= sumT;
                        }
                    }

                    float gGreen = g;
                    float gDryAmt = 0f;
                    float dryMix01 = 0f;
                    if (useGrassDry)
                    {
                        float drySc = Mathf.Max(0.002f, config.grassDryNoiseScale);
                        float dryNoise = Mathf.PerlinNoise(x * drySc * 0.14f + config.seed * 0.023f, y * drySc * 0.14f + config.seed * 0.029f);
                        dryMix01 = Mathf.Clamp01(dryNoise * Mathf.Clamp01(config.grassDryBlendStrength));
                        gDryAmt = g * dryMix01;
                        gGreen = g * (1f - dryMix01);
                    }
                    if (bufGrassDry != null)
                        bufGrassDry[y, x] = dryMix01;

                    float wetW = 0f;
                    float moistureMask01 = 0f;
                    if (useWet && moistureDist != null && gw > 0 && gh > 0)
                    {
                        float gxf = (aw > 1) ? (float)x / (aw - 1) * (gw - 1) : 0f;
                        float gzf = (ah > 1) ? (float)y / (ah - 1) * (gh - 1) : 0f;
                        float distWater = SampleShoreDistanceBilinear(moistureDist, gw, gh, gxf, gzf);
                        float moistNoise = Mathf.PerlinNoise(x * Mathf.Max(0.02f, config.terrainMoistureNoiseScale) * 0.16f + config.seed * 0.037f,
                            y * Mathf.Max(0.02f, config.terrainMoistureNoiseScale) * 0.16f + config.seed * 0.041f);
                        moistureMask01 = EvaluateMoistureMask01(distWater, config.terrainMoistureRadius, moistNoise,
                            Mathf.Clamp01(config.terrainMoistureNoiseStrength), x, y, config.seed, config.terrainMoistureNoiseScale);
                        float soil = gGreen + gDryAmt + d;
                        float mStr = Mathf.Clamp01(config.terrainMoistureStrength);
                        float take = moistureMask01 * mStr * soil;
                        if (soil > 1e-5f && take > 1e-6f)
                        {
                            float k = (soil - take) / soil;
                            gGreen *= k;
                            gDryAmt *= k;
                            d *= k;
                            wetW = take;
                        }
                    }
                    if (bufMoisture != null)
                        bufMoisture[y, x] = moistureMask01 * Mathf.Clamp01(config.terrainMoistureStrength);

                    float sandW = 0f;
                    float fordW = 0f;
                    if (useSand && shoreDist != null && gw > 0 && gh > 0)
                    {
                        float gxf = (aw > 1) ? (float)x / (aw - 1) * (gw - 1) : 0f;
                        float gzf = (ah > 1) ? (float)y / (ah - 1) * (gh - 1) : 0f;
                        float distF = SampleShoreDistanceBilinear(shoreDist, gw, gh, gxf, gzf);
                        float edgeStr = Mathf.Max(0f, config.sandEdgeNoiseStrength);
                        if (edgeStr > 1e-5f)
                        {
                            float esc = Mathf.Max(0.02f, config.sandEdgeNoiseScale);
                            float en = Mathf.PerlinNoise(x * esc * 0.21f + config.seed * 0.043f, y * esc * 0.21f + config.seed * 0.047f);
                            distF += (en - 0.5f) * 2f * edgeStr * (sandShoreCells + 0.75f);
                            distF = Mathf.Max(0f, distF);
                        }

                        if (sandDistNoise > 1e-5f)
                        {
                            float sn = Mathf.PerlinNoise(x * 0.13f + config.seed * 0.051f, y * 0.13f + config.seed * 0.053f);
                            distF += (sn - 0.5f) * 2f * sandDistNoise;
                            distF = Mathf.Max(0f, distF);
                        }

                        if (distF <= 0.5f)
                        {
                            if (iFord >= 0)
                            {
                                float fordMix = SampleRiverFordMix01(grid, gxf, gzf);
                                fordW = Mathf.Clamp01(fordMix);
                                sandW = 1f - fordW;
                            }
                            else
                                sandW = 1f;
                        }
                        else if (distF <= sandShoreCells + 0.5f)
                        {
                            float t = Mathf.Clamp01((distF - 0.5f) / sandShoreCells);
                            sandW = Mathf.Pow(1f - t, sandFalloffPow);
                        }
                    }

                    if (sandSoilContrast > 1.001f && sandW > 0.04f && sandW < 0.96f && hasGrass && hasDirt)
                    {
                        float band = Mathf.Sin(sandW * Mathf.PI);
                        ApplyGrassDirtContrastOnly(ref gGreen, ref d, (sandSoilContrast - 1f) * band);
                    }

                    float shoreOpaque = sandW + fordW;
                    if (shoreOpaque > 0.001f)
                    {
                        float mul = 1f - Mathf.Min(1f, shoreOpaque);
                        gGreen *= mul; gDryAmt *= mul; d *= mul; r *= mul; wetW *= mul;
                    }

                    float sum = gGreen + gDryAmt + d + r + wetW + sandW + fordW;
                    if (sum < 1e-5f)
                    {
                        if (iGrass >= 0) map[y, x, iGrass] = 1f;
                        else if (iDirt >= 0) map[y, x, iDirt] = 1f;
                        else if (numLayers > 0) map[y, x, 0] = 1f;
                        continue;
                    }
                    gGreen /= sum; gDryAmt /= sum; d /= sum; r /= sum; wetW /= sum; sandW /= sum; fordW /= sum;

                    for (int li = 0; li < numLayers; li++)
                        map[y, x, li] = 0f;
                    if (iGrass >= 0) map[y, x, iGrass] = gGreen;
                    if (iGrassDry >= 0) map[y, x, iGrassDry] = gDryAmt;
                    if (iDirt >= 0) map[y, x, iDirt] = d;
                    if (iRock >= 0) map[y, x, iRock] = r;
                    if (iWet >= 0) map[y, x, iWet] = wetW;
                    if (iSand >= 0) map[y, x, iSand] = sandW;
                    if (iFord >= 0) map[y, x, iFord] = fordW;
                }
            }

            if (bufMoisture != null) DebugLastMoisture01 = bufMoisture;
            if (bufMacro != null) DebugLastMacro01 = bufMacro;
            if (bufGrassDry != null) DebugLastGrassDryMix01 = bufGrassDry;

            data.SetAlphamaps(0, 0, map);
            int amPasses = Mathf.Max(0, config.terrainAlphamapSmoothPasses);
            if (config.sandShoreAlphamapSmoothCap >= 0)
                amPasses = Mathf.Min(amPasses, config.sandShoreAlphamapSmoothCap);
            if (amPasses > 0)
                SmoothAlphamapsBox(data, amPasses);
        }

        /// <summary>Contraste solo hierba/tierra en franja de orilla (sin tocar roca).</summary>
        static void ApplyGrassDirtContrastOnly(ref float g, ref float d, float strength)
        {
            if (strength < 1e-5f) return;
            float e = Mathf.Lerp(1f, 1.85f, Mathf.Clamp01(strength));
            g = Mathf.Pow(Mathf.Max(1e-6f, g), e);
            d = Mathf.Pow(Mathf.Max(1e-6f, d), e);
            float s = g + d;
            if (s > 1e-5f)
            {
                g /= s;
                d /= s;
            }
        }

        /// <summary>Mezcla 0–1 de textura de vado (riverFord) en las 4 esquinas de celda más cercana al sample.</summary>
        static float SampleRiverFordMix01(GridSystem grid, float gxf, float gzf)
        {
            int gw = grid.Width;
            int gh = grid.Height;
            int gx0 = Mathf.Clamp((int)gxf, 0, gw - 1);
            int gz0 = Mathf.Clamp((int)gzf, 0, gh - 1);
            int gx1 = Mathf.Clamp(gx0 + 1, 0, gw - 1);
            int gz1 = Mathf.Clamp(gz0 + 1, 0, gh - 1);
            float tx = Mathf.Clamp01(gxf - gx0);
            float tz = Mathf.Clamp01(gzf - gz0);
            float B(int gx, int gz) => grid.GetCell(gx, gz).type == CellType.River && grid.GetCell(gx, gz).riverFord ? 1f : 0f;
            float b00 = B(gx0, gz0);
            float b10 = B(gx1, gz0);
            float b01 = B(gx0, gz1);
            float b11 = B(gx1, gz1);
            return Mathf.Lerp(Mathf.Lerp(b00, b10, tx), Mathf.Lerp(b01, b11, tx), tz);
        }

        static int[,] BuildShoreDistanceGrid(GridSystem grid, int maxDist)
        {
            int w = grid.Width;
            int h = grid.Height;
            var dist = new int[w, h];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    dist[x, z] = -1;

            var qx = new Queue<int>();
            var qz = new Queue<int>();
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    if (grid.GetCell(x, z).type == CellType.Water || grid.GetCell(x, z).type == CellType.River)
                    {
                        dist[x, z] = 0;
                        qx.Enqueue(x);
                        qz.Enqueue(z);
                    }

            while (qx.Count > 0)
            {
                int x = qx.Dequeue();
                int z = qz.Dequeue();
                int d = dist[x, z];
                if (d >= maxDist) continue;
                void Try(int nx, int nz)
                {
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)h) return;
                    if (dist[nx, nz] != -1) return;
                    dist[nx, nz] = d + 1;
                    qx.Enqueue(nx);
                    qz.Enqueue(nz);
                }
                Try(x - 1, z); Try(x + 1, z); Try(x, z - 1); Try(x, z + 1);
            }
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    if (dist[x, z] == -1) dist[x, z] = maxDist + 1;
            return dist;
        }

        static void PaintThreeLayers(float h, float grassMax, float dirtMax, float blend,
            out float g, out float d, out float r)
        {
            if (blend <= 0.001f)
            {
                if (h < grassMax) { g = 1f; d = 0f; r = 0f; return; }
                if (h < dirtMax) { g = 0f; d = 1f; r = 0f; return; }
                g = 0f; d = 0f; r = 1f;
                return;
            }
            float gToD = Mathf.Clamp01((h - (grassMax - blend)) / (blend * 2f));
            float dToR = Mathf.Clamp01((h - (dirtMax - blend)) / (blend * 2f));
            g = 1f - gToD;
            d = gToD * (1f - dToR);
            r = gToD * dToR;
            float sum = g + d + r;
            if (sum > 0.0001f) { g /= sum; d /= sum; r /= sum; }
        }
    }
}
