using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 9: exporta grid a Terrain (heightmap + alphamaps por altura).</summary>
    public static class TerrainExporter
    {
        /// <summary>Parámetros: config + override de layers. tileSize > 0 reduce repetición. sand = orillas lagos/ríos.</summary>
        public static void ApplyToTerrain(Terrain terrain, GridSystem grid, MapGenConfig config,
            TerrainLayer grassOverride = null, TerrainLayer dirtOverride = null, TerrainLayer rockOverride = null,
            Vector2 grassTileSize = default, Vector2 dirtTileSize = default, Vector2 rockTileSize = default,
            TerrainLayer sandOverride = null, Vector2 sandTileSize = default, int sandShoreCells = 3)
        {
            if (terrain == null || terrain.terrainData == null || grid == null || config == null) return;

            var data = terrain.terrainData;
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
            data.SetHeights(0, 0, heights);

            TerrainLayer g = ApplyTileSize(grassOverride != null ? grassOverride : config.grassLayer, grassTileSize);
            TerrainLayer d = ApplyTileSize(dirtOverride != null ? dirtOverride : config.dirtLayer, dirtTileSize);
            TerrainLayer r = ApplyTileSize(rockOverride != null ? rockOverride : config.rockLayer, rockTileSize);
            TerrainLayer s = ApplyTileSize(sandOverride != null ? sandOverride : config.sandLayer, sandTileSize);
            int shoreCells = sandShoreCells > 0 ? sandShoreCells : config.sandShoreCells;
            if (config.paintTerrainByHeight && (g != null || d != null || r != null))
            {
                PaintTerrainByHeight(data, heights, res, config, grid, g, d, r, s, shoreCells);
                EnsureTerrainMaterialSupportsLayers(terrain);
            }
            else if (config.paintTerrainByHeight && g == null && d == null && r == null)
                Debug.LogWarning("TerrainExporter: Paint Terrain By Height activado pero no hay Grass/Dirt/Rock layers. Asigna Texture_Grass, Texture_Dirt, Texture_Rock en el RTS o en MapGenConfig.");

            if (config.debugLogs)
                Debug.Log($"Fase9 TerrainExport: heightmap {res}x{res}, size={data.size}, texturas={(g != null || d != null || r != null ? "aplicadas" : "no")}.");

            // Volumen visual: paredes laterales + base (Terrain Skirt)
            // Pasamos el mismo array heights (valores 0-1) para muestrear bordes
            // directamente, sin depender de terrain.SampleHeight() que puede
            // tener un frame de retraso tras SetHeights().
            if (config.showTerrainSkirt)
                TerrainSkirtBuilder.BuildSkirt(terrain, config, heights);
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
            var outH = new float[w, h];
            float waterH = config.waterHeight01;

            // Copia base.
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    outH[x, z] = grid.GetCell(x, z).height01;

            int radius = Mathf.Max(0, config.shoreSmoothRadiusCells);
            float strength = Mathf.Clamp01(config.shoreSmoothStrength);
            if (radius <= 0 || strength <= 0.0001f) return outH;

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
                        outH[x, z] = waterH; // agua plana
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

            return outH;
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

        static void PaintTerrainByHeight(TerrainData data, float[,] heights, int res, MapGenConfig config,
            GridSystem grid, TerrainLayer grassLayer, TerrainLayer dirtLayer, TerrainLayer rockLayer,
            TerrainLayer sandLayer, int sandShoreCells)
        {
            var layers = new List<TerrainLayer>();
            if (grassLayer != null) layers.Add(grassLayer);
            if (dirtLayer != null) layers.Add(dirtLayer);
            if (rockLayer != null) layers.Add(rockLayer);
            bool useSand = sandLayer != null && grid != null && sandShoreCells > 0;
            if (useSand) layers.Add(sandLayer);
            if (layers.Count == 0) return;

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
            int gw = grid != null ? grid.Width : 0;
            int gh = grid != null ? grid.Height : 0;

            for (int y = 0; y < ah; y++)
            {
                for (int x = 0; x < aw; x++)
                {
                    float hx = aw > 1 ? (float)x / (aw - 1) * (res - 1) : 0f;
                    float hy = ah > 1 ? (float)y / (ah - 1) * (res - 1) : 0f;
                    int ix = Mathf.Clamp((int)hx, 0, res - 1);
                    int iy = Mathf.Clamp((int)hy, 0, res - 1);
                    float hRaw = heights[iy, ix];
                    float h = Mathf.Clamp01((hRaw - minH) / rangeH);

                    float g, d, r;
                    if (numLayers == 1)
                    {
                        g = 1f; d = 0f; r = 0f;
                    }
                    else if (numLayers == 2 && !useSand)
                    {
                        g = 1f - h; d = h; r = 0f;
                    }
                    else
                    {
                        PaintThreeLayers(h, gMax, dMax, blend, out g, out d, out r);
                    }

                    float sandW = 0f;
                    if (useSand && shoreDist != null && gw > 0 && gh > 0)
                    {
                        // Interpolación bilinear de la distancia a la orilla
                        float gxf = (aw > 1) ? (float)x / (aw - 1) * (gw - 1) : 0f;
                        float gzf = (ah > 1) ? (float)y / (ah - 1) * (gh - 1) : 0f;
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
                        float distF = Mathf.Lerp(Mathf.Lerp(d00, d10, tx), Mathf.Lerp(d01, d11, tx), tz);
                        // Bajo el agua (dist 0): terreno = 100% arena para que al ver a través del agua se vea arena (doble textura)
                        if (distF <= 0.5f)
                            sandW = 1f;
                        else if (distF <= sandShoreCells + 0.5f)
                            sandW = 1f - Mathf.Clamp01((distF - 0.5f) / sandShoreCells);
                    }

                    if (sandW > 0.001f)
                    {
                        float mul = 1f - sandW;
                        g *= mul; d *= mul; r *= mul;
                        float sum = g + d + r + sandW;
                        if (sum > 0.0001f) { g /= sum; d /= sum; r /= sum; sandW /= sum; }
                    }

                    map[y, x, 0] = g;
                    if (numLayers >= 2) map[y, x, 1] = d;
                    if (numLayers >= 3) map[y, x, 2] = r;
                    if (numLayers >= 4) map[y, x, 3] = sandW;
                }
            }
            data.SetAlphamaps(0, 0, map);
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
