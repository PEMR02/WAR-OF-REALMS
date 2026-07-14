using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

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

            if (config.riverVisualUseRiverSurfaceMeshStrip &&
                grid.RiverCenterlinesCellSpace != null &&
                grid.RiverCenterlinesCellSpace.Count > 0)
            {
                if (!grid.RiverVisualSurfaceCacheFrozen &&
                    (!grid.RiverVisualSurfacesBuilt ||
                    grid.RiverVisualSurfaceMask == null ||
                    grid.RiverVisualSurfaceMask.GetLength(0) != grid.Width ||
                    grid.RiverVisualSurfaceMask.GetLength(1) != grid.Height))
                {
                    RiverSurfaceMeshBuilder.EnsureRiverVisualSurfaceCache(grid, config);
                    if (config.debugLogs || config.debugHydrologyNetwork)
                        RiverSurfaceMeshBuilder.LogRiverVisualCacheUse("TerrainExporter", grid, -1);
                }
            }

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

            ApplyRiverEndReachTerrainCarveToHeightmap(heights, res, grid, config);
            ApplySlopeCliffAntiStairSmoothing(heights, res);
            ApplyUnifiedWaterlineHeightmapBand(heights, res, grid, config);
            ApplyUwpRiverVisualMaskHeightmapFloorClamp(heights, res, grid, config);
            ApplySlopeCliffAntiStairSmoothing(heights, res);

            data.SetHeights(0, 0, heights);

            TerrainLayer g = ApplyTileSize(
                grassOverride != null ? grassOverride : config.grassLayer,
                grassTileSize.x > 0.01f || grassTileSize.y > 0.01f ? grassTileSize : new Vector2(28f, 28f));
            TerrainLayer d = ApplyTileSize(
                dirtOverride != null ? dirtOverride : config.dirtLayer,
                dirtTileSize.x > 0.01f || dirtTileSize.y > 0.01f ? dirtTileSize : new Vector2(24f, 24f));
            TerrainLayer r = ApplyTileSize(
                rockOverride != null ? rockOverride : config.rockLayer,
                rockTileSize.x > 0.01f || rockTileSize.y > 0.01f ? rockTileSize : new Vector2(22f, 22f));
            if (config.grassDryLayer == null && config.grassDryBlendStrength > 1e-5f)
            {
                // Solo albedo de hierba seca real. Grass_02.png es máscara grayscale → NO usar como diffuse.
                config.grassDryLayer = CreateRuntimeTerrainLayerFromProjectTexture(
                    "Assets/_Project/05_Art/Materials/Terreno/Grass_01.jpg",
                    "TL_Runtime_GrassDry");
                if (config.grassDryLayer == null)
                    config.grassDryBlendStrength = 0f;
            }
            TerrainLayer s = ApplyTileSize(
                sandOverride != null ? sandOverride : (config.sandLayer != null ? config.sandLayer : CreateRuntimeTerrainLayerFromProjectTexture("Packages/com.pmg.unified-world-pipeline/Content/TerrainTextures/Sand.png", "TL_Runtime_Sand")),
                sandTileSize.x > 0.01f || sandTileSize.y > 0.01f ? sandTileSize : new Vector2(18f, 18f));
            TerrainLayer fordBed = null;
            if (s != null && config.riverFordBedLayer != null)
                fordBed = ApplyTileSize(config.riverFordBedLayer, sandTileSize);
            int shoreCells = sandShoreCells > 0 ? sandShoreCells : config.sandShoreCells;
            // Orilla menos encajonada: mínimo 5 celdas de franja sand visual.
            shoreCells = Mathf.Clamp(Mathf.Max(shoreCells, 5), 1, 8);

            if (config.terrainMaterialTemplateOverride != null)
                terrain.materialTemplate = config.terrainMaterialTemplateOverride;

            // Pintar siempre que haya layers (el flag a veces queda false tras perfiles y deja todo dirt).
            bool canPaint = g != null || d != null || r != null;
            if (canPaint)
            {
                if (!config.paintTerrainByHeight)
                    Debug.LogWarning("[TerrainExporter] paintTerrainByHeight=false pero hay layers: se pinta igual.");
                PaintTerrainByHeight(data, heights, res, config, grid, g, d, r, s, shoreCells, fordBed,
                    grassTileSize, dirtTileSize);
                EnsureTerrainMaterialSupportsLayers(terrain);
            }
            else
            {
                ClearSplatDebugBuffers();
                if (config.paintTerrainByHeight)
                    Debug.LogWarning("TerrainExporter: Paint Terrain By Height activado pero no hay Grass/Dirt/Rock layers. Asigna Texture_Grass, Texture_Dirt, Texture_Rock en el RTS o en MapGenConfig.");
                EnsureTerrainMaterialSupportsLayers(terrain);
            }

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

        static void ApplySlopeCliffAntiStairSmoothing(float[,] heights, int res)
        {
            if (heights == null || res < 5)
                return;
            var work = new float[res, res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    work[y, x] = heights[y, x];

            for (int y = 2; y < res - 2; y++)
            {
                for (int x = 2; x < res - 2; x++)
                {
                    float dx = Mathf.Abs(work[y, x + 1] - work[y, x - 1]);
                    float dz = Mathf.Abs(work[y + 1, x] - work[y - 1, x]);
                    float diagA = Mathf.Abs(work[y + 1, x + 1] - work[y - 1, x - 1]);
                    float diagB = Mathf.Abs(work[y + 1, x - 1] - work[y - 1, x + 1]);
                    float slope = Mathf.Max(Mathf.Max(dx, dz), Mathf.Max(diagA, diagB));
                    if (slope < 0.018f)
                        continue;

                    float cardinal = (work[y, x - 1] + work[y, x + 1] + work[y - 1, x] + work[y + 1, x]) * 0.25f;
                    float diagonal = (work[y - 1, x - 1] + work[y - 1, x + 1] + work[y + 1, x - 1] + work[y + 1, x + 1]) * 0.25f;
                    float blended = Mathf.Lerp(cardinal, diagonal, 0.45f);
                    float strength = Mathf.Clamp01((slope - 0.018f) / 0.05f) * 0.38f;
                    heights[y, x] = Mathf.Lerp(work[y, x], blended, strength);
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

        static void ApplyUnifiedWaterlineHeightmapBand(float[,] heights, int res, GridSystem grid, MapGenConfig config)
        {
            if (heights == null || grid == null || config == null || res < 2 ||
                !WaterVisualPipelinePolicy.IsUnifiedSingleSurface(config))
                return;

            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            float visualOffsetWorld = UnifiedWaterField.VisualSurfaceOffsetWorld(config, terrainY);
            float visualWaterH = Mathf.Clamp01(config.waterHeight01 + visualOffsetWorld / Mathf.Max(1e-4f, terrainY));
            // Respetar perfil RTS (band≤0.75, lip≤0.014). Floor 1.85/0.07 recreaba orilla blanca plana.
            float bankLipWorld = Mathf.Max(config.unifiedWaterTerrainBankLipWorld, config.unifiedWaterShoreTerrainOffsetWorld);
            float landLipH = Mathf.Clamp01(visualWaterH + Mathf.Max(0.015f, bankLipWorld) / Mathf.Max(1e-4f, terrainY));
            float edgeSubmergeH = Mathf.Max(0.025f, config.unifiedWaterTerrainEdgeSubmergeWorld) / Mathf.Max(1e-4f, terrainY);
            float deepSubmergeH = Mathf.Max(edgeSubmergeH, Mathf.Max(config.lakeBedDepthBelowWater01, config.tributaryBedDepthBelowWater01));
            float bandCells = Mathf.Max(0.25f, config.unifiedWaterTerrainBandCells);
            float sampleCellRadius = Mathf.Max(0.35f, bandCells * 0.5f);

            int adjusted = 0;
            for (int y = 0; y < res; y++)
            {
                float gzF = (y / (float)(res - 1)) * (grid.Height - 1);
                for (int x = 0; x < res; x++)
                {
                    float gxF = (x / (float)(res - 1)) * (grid.Width - 1);
                    float waterMix = UnifiedWaterField.SampleWater01(grid, config, gxF, gzF);
                    float nearWater = waterMix;
                    nearWater = Mathf.Max(nearWater, UnifiedWaterField.SampleWater01(grid, config, gxF + sampleCellRadius, gzF));
                    nearWater = Mathf.Max(nearWater, UnifiedWaterField.SampleWater01(grid, config, gxF - sampleCellRadius, gzF));
                    nearWater = Mathf.Max(nearWater, UnifiedWaterField.SampleWater01(grid, config, gxF, gzF + sampleCellRadius));
                    nearWater = Mathf.Max(nearWater, UnifiedWaterField.SampleWater01(grid, config, gxF, gzF - sampleCellRadius));
                    if (nearWater <= 0.02f)
                        continue;

                    float inside01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(UnifiedWaterField.Iso - 0.08f, UnifiedWaterField.Iso + 0.12f, waterMix));
                    float shore01 = 1f - Mathf.Clamp01(Mathf.Abs(waterMix - UnifiedWaterField.Iso) / 0.5f);
                    float nearShore01 = Mathf.Max(shore01, Mathf.Clamp01((nearWater - 0.02f) / Mathf.Max(0.01f, UnifiedWaterField.Iso - 0.02f)) * (1f - inside01));

                    if (inside01 > 0.001f)
                    {
                        float submergeH = Mathf.Lerp(edgeSubmergeH, deepSubmergeH, Mathf.Clamp01(inside01 * 0.85f));
                        float target = Mathf.Clamp01(visualWaterH - submergeH);
                        float strength = Mathf.Clamp01(0.45f + inside01 * 0.55f);
                        heights[y, x] = Mathf.Min(heights[y, x], target);
                        heights[y, x] = Mathf.Lerp(heights[y, x], target, strength);
                    }
                    else if (nearShore01 > 0.001f)
                    {
                        int cx = Mathf.Clamp(Mathf.RoundToInt(gxF), 0, grid.Width - 1);
                        int cz = Mathf.Clamp(Mathf.RoundToInt(gzF), 0, grid.Height - 1);
                        // Solo preservar el LECHO (celda en máscara). El anillo adyacente DEBE subir a lip
                        // o el mesh unificado queda separado en Y y desaparece la orilla blanca del material.
                        if (grid.RiverVisualSurfaceMask != null &&
                            grid.InBoundsCell(cx, cz) &&
                            grid.RiverVisualSurfaceMask[cx, cz])
                            continue;
                        if (IsLandCellAdjacentToLakeWater(grid, cx, cz) &&
                            grid.GetCell(cx, cz).type == CellType.Water)
                            continue;

                        // No re-levantar lecho ya excavado fuera de máscara (carve euclídeo main/trib).
                        // Sin esto el lip recrea la orilla jagged de la máscara bool.
                        float carvedChannelCeil = visualWaterH - Mathf.Max(edgeSubmergeH * 0.35f, 0.008f);
                        if (heights[y, x] < carvedChannelCeil)
                            continue;

                        float lipPull = Mathf.Clamp01(nearShore01 * 0.92f);
                        float target = Mathf.Lerp(heights[y, x], landLipH, lipPull);
                        heights[y, x] = Mathf.Max(heights[y, x], target);
                    }
                    adjusted++;
                }
            }

            if (adjusted > 0 && config.debugLogs)
            {
                Debug.Log(
                    $"[UnifiedWaterlineTerrainBand] adjusted={adjusted} visualWaterH={visualWaterH:F4} " +
                    $"landLipH={landLipH:F4} edgeSubmergeH={edgeSubmergeH:F4} deepSubmergeH={deepSubmergeH:F4}");
            }
        }

        static bool ShouldPreserveUwpRiverChannelCarve(GridSystem grid, int cx, int cz)
        {
            if (grid == null || !grid.InBoundsCell(cx, cz))
                return false;
            if (IsLandCellAdjacentToLakeWater(grid, cx, cz))
                return true;
            if (grid.RiverVisualSurfaceMask == null)
                return false;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx;
                    int nz = cz + dz;
                    if (!grid.InBoundsCell(nx, nz))
                        continue;
                    if (grid.RiverVisualSurfaceMask[nx, nz])
                        return true;
                }
            }

            return false;
        }

        static float SampleRiverVisualMaskBilinear(GridSystem grid, float gxF, float gzF)
        {
            if (grid?.RiverVisualSurfaceMask == null)
                return 0f;
            int gx0 = Mathf.Clamp(Mathf.FloorToInt(gxF), 0, grid.Width - 1);
            int gz0 = Mathf.Clamp(Mathf.FloorToInt(gzF), 0, grid.Height - 1);
            int gx1 = Mathf.Clamp(gx0 + 1, 0, grid.Width - 1);
            int gz1 = Mathf.Clamp(gz0 + 1, 0, grid.Height - 1);
            float tx = Mathf.Clamp01(gxF - gx0);
            float tz = Mathf.Clamp01(gzF - gz0);
            float m00 = grid.RiverVisualSurfaceMask[gx0, gz0] ? 1f : 0f;
            float m10 = grid.RiverVisualSurfaceMask[gx1, gz0] ? 1f : 0f;
            float m01 = grid.RiverVisualSurfaceMask[gx0, gz1] ? 1f : 0f;
            float m11 = grid.RiverVisualSurfaceMask[gx1, gz1] ? 1f : 0f;
            return Mathf.Lerp(Mathf.Lerp(m00, m10, tx), Mathf.Lerp(m01, m11, tx), tz);
        }

        static float SampleInfluenceFieldBilinear(float[,] field, int w, int h, float gxF, float gzF)
        {
            if (field == null)
                return 0f;
            int gx0 = Mathf.Clamp(Mathf.FloorToInt(gxF), 0, w - 1);
            int gz0 = Mathf.Clamp(Mathf.FloorToInt(gzF), 0, h - 1);
            int gx1 = Mathf.Clamp(gx0 + 1, 0, w - 1);
            int gz1 = Mathf.Clamp(gz0 + 1, 0, h - 1);
            float tx = Mathf.Clamp01(gxF - gx0);
            float tz = Mathf.Clamp01(gzF - gz0);
            float a = Mathf.Lerp(field[gx0, gz0], field[gx1, gz0], tx);
            float b = Mathf.Lerp(field[gx0, gz1], field[gx1, gz1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        /// <summary>
        /// Campo 0..1 de intensidad del canal: 1 en la máscara/boca de lago, decae con smoothstep
        /// hacia afuera en <paramref name="bankFalloffCells"/> celdas. Un jitter Perlin perturba la
        /// distancia efectiva para romper el contorno recto (erosión visual de orilla).
        /// </summary>
        static float[,] BuildUwpChannelCarveInfluenceField(
            GridSystem grid,
            MapGenConfig config,
            bool[,] mask,
            int bankFalloffCells,
            float noiseAmpCells)
        {
            int w = grid.Width;
            int h = grid.Height;
            var infl = new float[w, h];
            var dist = new int[w, h];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    dist[x, z] = -1;

            var qx = new Queue<int>();
            var qz = new Queue<int>();
            bool maskOnlySeeds = UsesUwpFrozenCarveContract(grid, config);
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    bool seed = mask != null && mask[x, z];
                    if (!seed && !maskOnlySeeds)
                        seed = IsLandCellAdjacentToLakeWater(grid, x, z);
                    if (!seed)
                        continue;

                    dist[x, z] = 0;
                    qx.Enqueue(x);
                    qz.Enqueue(z);
                }
            }

            int ownedProjectionSeeds = 0;
            // Frozen carve: solo máscara visual; no sembrar proyección lago fuera de ella (evita carve fantasma).
            bool skipOwnedLakeProjectionSeeds = UsesUwpFrozenCarveContract(grid, config);
            if (!skipOwnedLakeProjectionSeeds && config.uwpOwnedVisualPolicy && grid.RiverVisualSurfaces != null)
            {
                float tribFull = config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                    ? config.riverVisualRibbonFullWidthCellsTributary
                    : config.riverVisualRibbonFullWidthCellsMain * 0.65f;
                int projectionRadius = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(tribFull * 0.40f, 2f)), 2, 5);
                int projectionSpan = Mathf.Clamp(config.lakeRiverMouthBlendCells, 3, 7);
                float maxDist = Mathf.Min(
                    Mathf.Max(8f, config.lakeRiverConnectorMaxDistanceCells, config.lakeRiverMouthBlendCells + 12f),
                    96f);

                void SeedCell(int sx, int sz)
                {
                    if ((uint)sx >= (uint)w || (uint)sz >= (uint)h)
                        return;
                    if (dist[sx, sz] == 0)
                        return;
                    dist[sx, sz] = 0;
                    qx.Enqueue(sx);
                    qz.Enqueue(sz);
                    ownedProjectionSeeds++;
                }

                void SeedDisk(Vector2 center, int radius)
                {
                    int cx = Mathf.Clamp(Mathf.FloorToInt(center.x), 0, w - 1);
                    int cz = Mathf.Clamp(Mathf.FloorToInt(center.y), 0, h - 1);
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            if (dx * dx + dz * dz > radius * radius)
                                continue;
                            SeedCell(cx + dx, cz + dz);
                        }
                    }
                }

                void SeedOwnedProjectionEndpoint(List<Vector2> line, int endpointIndex)
                {
                    Vector2 end = line[endpointIndex];
                    int ex = Mathf.Clamp(Mathf.FloorToInt(end.x), 0, w - 1);
                    int ez = Mathf.Clamp(Mathf.FloorToInt(end.y), 0, h - 1);
                    bool endpointAtLake =
                        grid.GetCell(ex, ez).type == CellType.Water ||
                        TerrainCellNearLakeBody(grid, ex, ez, 4) ||
                        IsLandCellAdjacentToLakeWater(grid, ex, ez) ||
                        TryFindNearestLakeShoreForCarve(grid, end, maxDist, out _);
                    if (!endpointAtLake)
                        return;

                    int dir = endpointIndex == 0 ? 1 : -1;
                    int limit = Mathf.Min(projectionSpan, line.Count - 1);
                    for (int k = 0; k <= limit; k++)
                    {
                        int idx = endpointIndex + dir * k;
                        if (idx < 0 || idx >= line.Count)
                            break;
                        float fade = 1f - k / Mathf.Max(1f, limit);
                        int r = Mathf.Max(bankFalloffCells + 1, Mathf.RoundToInt(Mathf.Lerp(bankFalloffCells + 1, projectionRadius, fade)));
                        SeedDisk(line[idx], r);
                    }

                    if (TryFindNearestLakeShoreForCarve(grid, end, maxDist, out Vector2 shore))
                    {
                        float bridgeDist = Vector2.Distance(end, shore);
                        int steps = Mathf.Clamp(Mathf.CeilToInt(bridgeDist / 0.55f), 2, 10);
                        for (int s = 1; s <= steps; s++)
                            SeedDisk(Vector2.Lerp(end, shore, s / (float)steps), projectionRadius);
                    }
                }

                for (int ri = 1; ri < grid.RiverVisualSurfaces.Count; ri++)
                {
                    if (!IsLakeOwnedTributaryIndex(grid, ri))
                        continue;
                    var surface = grid.RiverVisualSurfaces[ri];
                    if (surface.Skipped || surface.FinalCenterlineCells == null || surface.FinalCenterlineCells.Count < 2)
                        continue;
                    SeedOwnedProjectionEndpoint(surface.FinalCenterlineCells, 0);
                    SeedOwnedProjectionEndpoint(surface.FinalCenterlineCells, surface.FinalCenterlineCells.Count - 1);
                }

                if (ownedProjectionSeeds > 0)
                {
                    Debug.Log(
                        $"[OwnedTributaryLakeProjectionClampSeed] seeds={ownedProjectionSeeds} " +
                        $"radius={projectionRadius} span={projectionSpan} seed={config.seed}");
                }
            }

            int maxD = Mathf.Max(1, bankFalloffCells);
            while (qx.Count > 0)
            {
                int x = qx.Dequeue();
                int z = qz.Dequeue();
                int d = dist[x, z];
                if (d >= maxD)
                    continue;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                            continue;
                        int nx = x + dx;
                        int nz = z + dz;
                        if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                            continue;
                        if (dist[nx, nz] != -1)
                            continue;
                        dist[nx, nz] = d + 1;
                        qx.Enqueue(nx);
                        qz.Enqueue(nz);
                    }
                }
            }

            float noiseScale = 0.20f;
            float ox = (config.seed % 733) * 0.017f;
            float oz = ((config.seed / 733) % 733) * 0.019f;
            float span = maxD + 0.5f;
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    int d = dist[x, z];
                    if (d < 0)
                    {
                        infl[x, z] = 0f;
                        continue;
                    }
                    if (d == 0)
                    {
                        infl[x, z] = 1f;
                        continue;
                    }

                    float n = Mathf.PerlinNoise(ox + x * noiseScale, oz + z * noiseScale);
                    float dEff = d - (n - 0.5f) * 2f * noiseAmpCells;
                    float t = Mathf.Clamp01(1f - dEff / span);
                    infl[x, z] = t * t * (3f - 2f * t);
                }
            }

            return infl;
        }

        /// <summary>
        /// Tras la banda de waterline: rebaja el canal ribbon y bocas lago-tributario con una
        /// pendiente de orilla suave (falloff + jitter) en vez de una pared vertical.
        /// </summary>
        static void ApplyUwpRiverVisualMaskHeightmapFloorClamp(
            float[,] heights,
            int res,
            GridSystem grid,
            MapGenConfig config)
        {
            if (heights == null || grid == null || config == null || res < 2 ||
                !config.riverVisualTerrainCarveEnabled || grid.RiverVisualSurfaceMask == null)
                return;

            // Contrato frozen: el carve uniforme ya sigue la máscara; el BFS de orilla
            // deprimía terreno fuera del ribbon (orilla blanca sin agua / carve fantasma).
            if (UsesUwpFrozenCarveContract(grid, config))
                return;

            float depthW = config.riverTerrainCarveDepthWorld;
            if (depthW < 1e-4f)
                return;

            float depth01 = Mathf.Clamp(depthW, 0f, 3f) / Mathf.Max(1e-4f, config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f);
            float floorMain = ResolveUwpRiverCarveFloor01(grid, config, depth01, 0);
            float floorTrib = ResolveUwpRiverCarveFloor01(grid, config, depth01, 1);
            float floorH = Mathf.Min(floorMain, floorTrib);

            int bankFalloff = Mathf.Clamp(Mathf.Max(3, config.riverVisualTerrainBankFalloffCells), 2, 6);
            float noiseAmp = Mathf.Clamp(bankFalloff * 0.42f, 0.6f, 2.2f);
            float[,] influence = BuildUwpChannelCarveInfluenceField(
                grid, config, grid.RiverVisualSurfaceMask, bankFalloff, noiseAmp);

            int w = grid.Width;
            int h = grid.Height;
            int clamped = 0;

            for (int y = 0; y < res; y++)
            {
                float gzF = (y / (float)(res - 1)) * (h - 1);
                for (int x = 0; x < res; x++)
                {
                    float gxF = (x / (float)(res - 1)) * (w - 1);
                    float infl = SampleInfluenceFieldBilinear(influence, w, h, gxF, gzF);
                    if (infl <= 0.001f)
                        continue;

                    float natural = heights[y, x];
                    float target = Mathf.Lerp(natural, floorH, infl);
                    if (target < natural - 1e-6f)
                    {
                        heights[y, x] = target;
                        clamped++;
                    }
                }
            }

            if (clamped > 0 && config.uwpOwnedVisualPolicy)
            {
                Debug.Log(
                    $"[UwpRiverHeightmapFloorClamp] clamped={clamped} floorH={floorH:F4} " +
                    $"bankFalloff={bankFalloff} noiseAmp={noiseAmp:F2} seed={config.seed}");
            }
        }

        static float SampleWaterMaskBilinear(GridSystem grid, float gxF, float gzF)
        {
            int gx0 = Mathf.Clamp(Mathf.FloorToInt(gxF), 0, grid.Width - 1);
            int gz0 = Mathf.Clamp(Mathf.FloorToInt(gzF), 0, grid.Height - 1);
            int gx1 = Mathf.Clamp(gx0 + 1, 0, grid.Width - 1);
            int gz1 = Mathf.Clamp(gz0 + 1, 0, grid.Height - 1);
            float tx = Mathf.Clamp01(gxF - gx0);
            float tz = Mathf.Clamp01(gzF - gz0);

            float w00 = IsWaterOrRiver(grid.GetCell(gx0, gz0).type) ? 1f : 0f;
            float w10 = IsWaterOrRiver(grid.GetCell(gx1, gz0).type) ? 1f : 0f;
            float w01 = IsWaterOrRiver(grid.GetCell(gx0, gz1).type) ? 1f : 0f;
            float w11 = IsWaterOrRiver(grid.GetCell(gx1, gz1).type) ? 1f : 0f;
            return Mathf.Lerp(Mathf.Lerp(w00, w10, tx), Mathf.Lerp(w01, w11, tx), tz);
        }

        static bool SampleTouchesWater(GridSystem grid, float gxF, float gzF, int radius)
        {
            int cx = Mathf.Clamp(Mathf.RoundToInt(gxF), 0, grid.Width - 1);
            int cz = Mathf.Clamp(Mathf.RoundToInt(gzF), 0, grid.Height - 1);
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx;
                    int z = cz + dz;
                    if ((uint)x >= (uint)grid.Width || (uint)z >= (uint)grid.Height)
                        continue;
                    if (IsWaterOrRiver(grid.GetCell(x, z).type))
                        return true;
                }
            }
            return false;
        }

        static bool IsWaterOrRiver(CellType t)
        {
            return t == CellType.Water || t == CellType.River;
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

        static TerrainLayer CreateRuntimeTerrainLayerFromProjectTexture(string assetPath, string layerName)
        {
#if UNITY_EDITOR
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex == null)
                return null;
            var layer = new TerrainLayer
            {
                name = layerName,
                diffuseTexture = tex,
                tileSize = new Vector2(12f, 12f)
            };
            return layer;
#else
            return null;
#endif
        }

        static bool UsesRiverVisualSurfaceMaskForTerrainPaint(GridSystem grid, MapGenConfig config)
        {
            if (config == null || grid == null || !config.riverVisualUseRiverSurfaceMeshStrip)
                return false;
            bool[,] mask = grid.RiverVisualSurfaceMask;
            return mask != null &&
                   mask.GetLength(0) == grid.Width &&
                   mask.GetLength(1) == grid.Height;
        }

        static int CountTerrainMaskTrue(bool[,] mask, int w, int h)
        {
            if (mask == null)
                return 0;
            int count = 0;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    if (mask[x, z])
                        count++;
            return count;
        }

        struct UwpGhostRiverHeightRestoreStats
        {
            public int components;
            public int ghostCells;
            public int restoredCells;
            public int unresolvedCells;
        }

        static bool CanRunUwpSkippedTributaryFunctionalCleanup(GridSystem grid, MapGenConfig config)
        {
            return config != null &&
                   config.uwpOwnedVisualPolicy &&
                   grid != null &&
                   grid.RiverVisualSurfaceCacheFrozen &&
                   UsesRiverVisualSurfaceMaskForTerrainPaint(grid, config);
        }

        static bool CanRestoreUwpGhostRiverHeights(GridSystem grid, MapGenConfig config) =>
            CanRunUwpSkippedTributaryFunctionalCleanup(grid, config);

        static int FindNearestRiverCenterlineIndexLocal(GridSystem grid, float cx, float cz)
        {
            if (grid?.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return 0;

            int bestIndex = 0;
            float bestSq = float.PositiveInfinity;
            var p = new Vector2(cx, cz);

            for (int ri = 0; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
            {
                var line = grid.RiverCenterlinesCellSpace[ri];
                if (line == null || line.Count == 0)
                    continue;

                if (line.Count == 1)
                {
                    float d = (p - line[0]).sqrMagnitude;
                    if (d < bestSq)
                    {
                        bestSq = d;
                        bestIndex = ri;
                    }
                    continue;
                }

                for (int i = 0; i < line.Count - 1; i++)
                {
                    float d = DistanceSqPointToSegmentLocal(p, line[i], line[i + 1]);
                    if (d < bestSq)
                    {
                        bestSq = d;
                        bestIndex = ri;
                    }
                }
            }

            return bestIndex;
        }

        static float DistanceSqPointToSegmentLocal(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = Mathf.Max(1e-5f, ab.sqrMagnitude);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
            Vector2 q = a + ab * t;
            return (p - q).sqrMagnitude;
        }

        static int CountValidConfluencesForTributary(GridSystem grid, int riverIndex)
        {
            if (grid?.RiverConfluences == null)
                return 0;
            int count = 0;
            for (int i = 0; i < grid.RiverConfluences.Count; i++)
            {
                RiverConfluenceNode node = grid.RiverConfluences[i];
                if (node.Valid && node.TributaryRiverIndex == riverIndex)
                    count++;
            }
            return count;
        }

        static bool[,] EnsureUwpSkippedTributaryFunctionalMask(GridSystem grid)
        {
            int w = grid.Width;
            int h = grid.Height;
            bool[,] mask = grid.UwpSkippedTributaryFunctionalMask;
            if (mask == null || mask.GetLength(0) != w || mask.GetLength(1) != h)
            {
                mask = new bool[w, h];
                grid.UwpSkippedTributaryFunctionalMask = mask;
            }

            return mask;
        }

        static bool UsesUwpSkippedTributaryFunctionalMask(GridSystem grid, MapGenConfig config)
        {
            if (!CanRunUwpSkippedTributaryFunctionalCleanup(grid, config))
                return false;
            bool[,] mask = grid.UwpSkippedTributaryFunctionalMask;
            return mask != null &&
                   mask.GetLength(0) == grid.Width &&
                   mask.GetLength(1) == grid.Height;
        }

        static float TryAverageValidLandNeighborHeightExcludingSkippedTributary(
            float[,] heights,
            GridSystem grid,
            bool[,] skippedTribMask,
            int w,
            int h,
            int x,
            int z)
        {
            float sum = 0f;
            int count = 0;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    int nx = x + dx;
                    int nz = z + dz;
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                        continue;
                    if (skippedTribMask != null && skippedTribMask[nx, nz])
                        continue;
                    if (grid.GetCell(nx, nz).type != CellType.Land)
                        continue;
                    sum += heights[nx, nz];
                    count++;
                }
            }

            return count > 0 ? sum / count : float.NaN;
        }

        static int RestoreTargetRiverHeightsFromLandNeighbors(
            float[,] heights,
            GridSystem grid,
            bool[,] isTarget,
            int w,
            int h,
            bool[,] skippedTribMask = null)
        {
            var resolved = new bool[w, h];
            var queue = new Queue<Vector2Int>();
            int restored = 0;

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (!isTarget[x, z])
                        continue;

                    float landAvg = skippedTribMask != null
                        ? TryAverageValidLandNeighborHeightExcludingSkippedTributary(
                            heights, grid, skippedTribMask, w, h, x, z)
                        : TryAverageLandNeighborHeight(heights, grid, w, h, x, z);
                    if (float.IsNaN(landAvg))
                        continue;

                    heights[x, z] = landAvg;
                    resolved[x, z] = true;
                    queue.Enqueue(new Vector2Int(x, z));
                }
            }

            while (queue.Count > 0)
            {
                Vector2Int c = queue.Dequeue();
                float seedH = heights[c.x, c.y];
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                            continue;
                        int nx = c.x + dx;
                        int nz = c.y + dz;
                        if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                            continue;
                        if (!isTarget[nx, nz] || resolved[nx, nz])
                            continue;

                        heights[nx, nz] = seedH;
                        resolved[nx, nz] = true;
                        queue.Enqueue(new Vector2Int(nx, nz));
                    }
                }
            }

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (isTarget[x, z] && resolved[x, z])
                        restored++;
                }
            }

            return restored;
        }

        static void LogUwpSkippedTributaryHeightFinal(float[,] outH, GridSystem grid, MapGenConfig config)
        {
            if (!UsesUwpSkippedTributaryFunctionalMask(grid, config) || outH == null)
                return;

            int w = grid.Width;
            int h = grid.Height;
            bool[,] skippedTribMask = grid.UwpSkippedTributaryFunctionalMask;
            int cells = 0;
            float minH = float.PositiveInfinity;
            float maxH = float.NegativeInfinity;
            double sumH = 0d;
            int stillLowerThanLand = 0;

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (!skippedTribMask[x, z])
                        continue;

                    float cellH = outH[x, z];
                    cells++;
                    minH = Mathf.Min(minH, cellH);
                    maxH = Mathf.Max(maxH, cellH);
                    sumH += cellH;

                    float landRef = TryAverageValidLandNeighborHeightExcludingSkippedTributary(
                        outH, grid, skippedTribMask, w, h, x, z);
                    if (!float.IsNaN(landRef) && cellH + 1e-5f < landRef)
                        stillLowerThanLand++;
                }
            }

            if (cells <= 0)
                return;

            float avgH = (float)(sumH / cells);
            Debug.Log(
                $"[UWP_SKIPPED_TRIBUTARY_HEIGHT_FINAL] cells={cells} min={minH:F4} max={maxH:F4} " +
                $"avg={avgH:F4} stillLowerThanLand={stillLowerThanLand}");
        }

        /// <summary>
        /// UWP post-freeze: limpia datos funcionales de tributarios skipped (ford, River→Land, height01).
        /// </summary>
        public static void CleanUwpSkippedTributaryFunctionalData(GridSystem grid, MapGenConfig config)
        {
            if (!CanRunUwpSkippedTributaryFunctionalCleanup(grid, config))
                return;

            if (grid.RiverVisualSurfaces == null || grid.RiverVisualSurfaces.Count == 0)
                return;

            int w = grid.Width;
            int h = grid.Height;
            bool[,] rivMask = grid.RiverVisualSurfaceMask;
            bool[,] skippedTribMask = EnsureUwpSkippedTributaryFunctionalMask(grid);
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                    skippedTribMask[x, z] = false;
            }

            var work = new float[w, h];
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                    work[x, z] = grid.GetCell(x, z).height01;
            }

            for (int si = 0; si < grid.RiverVisualSurfaces.Count; si++)
            {
                RiverVisualSurfaceData surface = grid.RiverVisualSurfaces[si];
                if (surface == null || !surface.Skipped)
                    continue;

                int riverIndex = surface.RiverIndex >= 0 ? surface.RiverIndex : si;
                if (grid.RiverCenterlinesCellSpace == null ||
                    riverIndex < 0 ||
                    riverIndex >= grid.RiverCenterlinesCellSpace.Count)
                    continue;

                var isTarget = new bool[w, h];
                int cells = 0;
                int fordCleared = 0;

                for (int x = 0; x < w; x++)
                {
                    for (int z = 0; z < h; z++)
                    {
                        ref CellData cell = ref grid.GetCell(x, z);
                        if (cell.type != CellType.River || rivMask[x, z])
                            continue;
                        if (FindNearestRiverCenterlineIndexLocal(grid, x + 0.5f, z + 0.5f) != riverIndex)
                            continue;

                        isTarget[x, z] = true;
                        skippedTribMask[x, z] = true;
                        cells++;
                        if (cell.riverFord)
                        {
                            cell.riverFord = false;
                            fordCleared++;
                        }
                    }
                }

                if (cells <= 0)
                {
                    int confluencesOnly = CountValidConfluencesForTributary(grid, riverIndex);
                    if (confluencesOnly > 0)
                    {
                        Debug.Log(
                            $"[UWP_CLEAN_SKIPPED_TRIBUTARY_FUNCTIONAL] riverIndex={riverIndex} cells=0 " +
                            $"fordCleared=0 heightRestored=0 typeRestored=0 confluencesIgnored={confluencesOnly}");
                    }
                    continue;
                }

                int heightRestored = RestoreTargetRiverHeightsFromLandNeighbors(
                    work, grid, isTarget, w, h, skippedTribMask);
                int typeRestored = 0;
                for (int x = 0; x < w; x++)
                {
                    for (int z = 0; z < h; z++)
                    {
                        if (!isTarget[x, z])
                            continue;

                        ref CellData cell = ref grid.GetCell(x, z);
                        cell.height01 = work[x, z];
                        cell.type = CellType.Land;
                        cell.riverFord = false;
                        cell.walkable = true;
                        cell.buildable = true;
                        cell.waterTraverse = WaterTraverseMode.NotWater;
                        typeRestored++;
                    }
                }

                int confluencesIgnored = CountValidConfluencesForTributary(grid, riverIndex);
                Debug.Log(
                    $"[UWP_CLEAN_SKIPPED_TRIBUTARY_FUNCTIONAL] riverIndex={riverIndex} cells={cells} " +
                    $"fordCleared={fordCleared} heightRestored={heightRestored} typeRestored={typeRestored} " +
                    $"confluencesIgnored={confluencesIgnored}");
            }
        }

        /// <summary>
        /// UWP post-freeze: restaura height01 en corredores River lógicos fuera de RiverVisualSurfaceMask
        /// (gameplay/NavMesh). Llamar tras FreezeUwpFinalWaterVisualSurfaceCache.
        /// </summary>
        public static void RestoreUwpGhostLogicalRiverHeightsInGrid(GridSystem grid, MapGenConfig config)
        {
            if (!CanRestoreUwpGhostRiverHeights(grid, config))
                return;

            int w = grid.Width;
            int h = grid.Height;
            bool[,] rivMask = grid.RiverVisualSurfaceMask;
            var work = new float[w, h];
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                    work[x, z] = grid.GetCell(x, z).height01;
            }

            UwpGhostRiverHeightRestoreStats stats =
                RestoreGhostLogicalRiverHeightsOutsideVisualMask(work, grid, rivMask, writeLog: true);

            if (stats.restoredCells <= 0)
                return;

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (grid.GetCell(x, z).type != CellType.River || rivMask[x, z])
                        continue;
                    grid.GetCell(x, z).height01 = work[x, z];
                }
            }
        }

        static float TryAverageLandNeighborHeight(float[,] heights, GridSystem grid, int w, int h, int x, int z)
        {
            float sum = 0f;
            int count = 0;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    int nx = x + dx;
                    int nz = z + dz;
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                        continue;
                    if (grid.GetCell(nx, nz).type != CellType.Land)
                        continue;
                    sum += heights[nx, nz];
                    count++;
                }
            }

            return count > 0 ? sum / count : float.NaN;
        }

        static int CountGhostRiverComponents(bool[,] isGhost, int w, int h)
        {
            var visited = new bool[w, h];
            int components = 0;
            var stack = new Stack<Vector2Int>();

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (!isGhost[x, z] || visited[x, z])
                        continue;

                    components++;
                    visited[x, z] = true;
                    stack.Push(new Vector2Int(x, z));
                    while (stack.Count > 0)
                    {
                        Vector2Int c = stack.Pop();
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dz == 0)
                                    continue;
                                int nx = c.x + dx;
                                int nz = c.y + dz;
                                if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                                    continue;
                                if (!isGhost[nx, nz] || visited[nx, nz])
                                    continue;
                                visited[nx, nz] = true;
                                stack.Push(new Vector2Int(nx, nz));
                            }
                        }
                    }
                }
            }

            return components;
        }

        /// <summary>
        /// UWP ribbon: restaura altura en celdas River lógicas fuera de la máscara visual (evita canales grises sin agua).
        /// Semillas desde Land vecina; interior del componente por BFS 8-conectado.
        /// </summary>
        static UwpGhostRiverHeightRestoreStats RestoreGhostLogicalRiverHeightsOutsideVisualMask(
            float[,] outH,
            GridSystem grid,
            bool[,] rivMask,
            bool writeLog = false)
        {
            var stats = new UwpGhostRiverHeightRestoreStats();
            if (outH == null || grid == null || rivMask == null)
                return stats;

            int w = grid.Width;
            int h = grid.Height;
            if (rivMask.GetLength(0) != w || rivMask.GetLength(1) != h)
                return stats;
            if (outH.GetLength(0) != w || outH.GetLength(1) != h)
                return stats;

            var isGhost = new bool[w, h];
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (grid.GetCell(x, z).type == CellType.River && !rivMask[x, z])
                    {
                        isGhost[x, z] = true;
                        stats.ghostCells++;
                    }
                }
            }

            if (stats.ghostCells <= 0)
                return stats;

            stats.components = CountGhostRiverComponents(isGhost, w, h);

            var resolved = new bool[w, h];
            var queue = new Queue<Vector2Int>();

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (!isGhost[x, z])
                        continue;

                    float landAvg = TryAverageLandNeighborHeight(outH, grid, w, h, x, z);
                    if (float.IsNaN(landAvg))
                        continue;

                    outH[x, z] = landAvg;
                    resolved[x, z] = true;
                    queue.Enqueue(new Vector2Int(x, z));
                }
            }

            while (queue.Count > 0)
            {
                Vector2Int c = queue.Dequeue();
                float seedH = outH[c.x, c.y];
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                            continue;
                        int nx = c.x + dx;
                        int nz = c.y + dz;
                        if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                            continue;
                        if (!isGhost[nx, nz] || resolved[nx, nz])
                            continue;

                        outH[nx, nz] = seedH;
                        resolved[nx, nz] = true;
                        queue.Enqueue(new Vector2Int(nx, nz));
                    }
                }
            }

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (!isGhost[x, z])
                        continue;
                    if (resolved[x, z])
                        stats.restoredCells++;
                    else
                        stats.unresolvedCells++;
                }
            }

            if (writeLog)
            {
                Debug.Log(
                    $"[UWP_GHOST_RIVER_HEIGHT_RESTORE] components={stats.components} " +
                    $"ghostCells={stats.ghostCells} restoredCells={stats.restoredCells} " +
                    $"unresolvedCells={stats.unresolvedCells} source=RiverVisualSurfaceMask");
            }

            return stats;
        }

        static void LogRiverTerrainPaintAudit(GridSystem grid, MapGenConfig config)
        {
            if (config == null || grid == null)
                return;

            int w = grid.Width;
            int h = grid.Height;
            int logicalRiverCells = 0;
            int skippedLogicalRiverCells = 0;
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (grid.GetCell(x, z).type != CellType.River)
                        continue;
                    logicalRiverCells++;
                    if (UsesRiverVisualSurfaceMaskForTerrainPaint(grid, config) &&
                        !grid.RiverVisualSurfaceMask[x, z])
                        skippedLogicalRiverCells++;
                }
            }

            if (UsesRiverVisualSurfaceMaskForTerrainPaint(grid, config))
            {
                bool[,] mask = grid.RiverVisualSurfaceMask;
                int maskCells = CountTerrainMaskTrue(mask, w, h);
                bool uniformUwpRiverCarve = IsUniformUwpRiverCarveChannel(config);
                int uwpCarveInset = uniformUwpRiverCarve ? ResolveUwpRiverCarveInsetCells(config) : 0;
                int paintedBedCells = 0;
                for (int x = 0; x < w; x++)
                {
                    for (int z = 0; z < h; z++)
                    {
                        if (mask == null || !mask[x, z])
                            continue;
                        if (uniformUwpRiverCarve &&
                            !IsRiverVisualMaskCoreCell(mask, w, h, x, z, uwpCarveInset))
                            continue;
                        paintedBedCells++;
                    }
                }

                Debug.Log(
                    $"[RiverTerrainPaintUwpMask] riverSurfaceMeshActive=1 source=RiverVisualSurfaceMask " +
                    $"maskCells={maskCells} logicalRiverCells={logicalRiverCells} paintedBedCells={paintedBedCells} " +
                    $"skippedLogicalRiverCells={skippedLogicalRiverCells}");
                return;
            }

            bool strip = config.riverVisualUseRiverSurfaceMeshStrip;
            Debug.Log(
                $"[RiverTerrainPaint] riverSurfaceMeshActive={(strip ? 1 : 0)} riverPaintedAsWater={(strip ? 0 : 1)} " +
                $"riverPaintedAsBed=1 riverCells={logicalRiverCells}");
        }

        static float[,] BuildShoreSmoothedCellHeights(GridSystem grid, MapGenConfig config)
        {
            int w = grid.Width;
            int h = grid.Height;
            var outH = BuildLogicalCellHeightSnapshot(grid);
            if (UsesRiverVisualSurfaceMaskForTerrainPaint(grid, config))
                RestoreGhostLogicalRiverHeightsOutsideVisualMask(outH, grid, grid.RiverVisualSurfaceMask);
            float waterH = config.waterHeight01;

            if (config != null &&
                config.riverVisualUseRiverSurfaceMeshStrip &&
                config.uwpOwnedVisualPolicy &&
                grid.RiverCenterlinesCellSpace != null &&
                grid.RiverCenterlinesCellSpace.Count > 0 &&
                !grid.RiverVisualSurfaceCacheFrozen)
            {
                RiverSurfaceMeshBuilder.EnsureRiverVisualSurfaceCache(grid, config);
            }

            int radius = Mathf.Max(0, config.shoreSmoothRadiusCells);
            float strength = Mathf.Clamp01(config.shoreSmoothStrength);
            if (radius <= 0 || strength <= 0.0001f)
            {
                if (config.riverVisualUseRiverSurfaceMeshStrip && config.riverEndReachTerrainFixEnabled)
                    ApplyRiverEndReachTerrainCarve(outH, grid, config);
                if (config.riverVisualUseRiverSurfaceMeshStrip && config.riverConfluenceEnabled && !config.uwpOwnedVisualPolicy)
                    ApplyRiverConfluenceTerrainCarve(outH, grid, config);
                LogRiverTerrainPaintAudit(grid, config);
                LogUwpSkippedTributaryHeightFinal(outH, grid, config);
                return outH;
            }

            ApplyVisualShorelineSmoothing(outH, grid, waterH, radius, strength, config);
            bool stripRiverSurface = config != null && config.riverVisualUseRiverSurfaceMeshStrip;
            if (stripRiverSurface && config.riverVisualTerrainCarveEnabled)
            {
                if (!config.uwpOwnedVisualPolicy)
                {
                    ApplyLogicalRiverCorridorTerrainCarve(outH, grid, config);
                    if (config.riverSurfaceTributaryWidthFixEnabled)
                        ApplyTributaryVisualTerrainCarve(outH, grid, config);
                    ApplyRiverVisualTerrainChannelCarve(outH, grid, config);
                    ApplyRiverBorderOutletTerrainCarve(outH, grid, config);
                }
                else
                {
                    // UWP: misma máscara que la malla ribbon (centerline + ancho en EnsureRiverVisualSurfaceCache).
                    ApplyRiverVisualTerrainChannelCarve(outH, grid, config);
                }
            }
            else if (config.riverVisualTerrainCarveEnabled)
                ApplyRiverTerrainChannelCarve(outH, grid, config);

            if (stripRiverSurface && config.riverEndReachTerrainFixEnabled)
                ApplyRiverEndReachTerrainCarve(outH, grid, config);

            if (stripRiverSurface && config.riverConfluenceEnabled && !config.uwpOwnedVisualPolicy)
                ApplyRiverConfluenceTerrainCarve(outH, grid, config);

            LogRiverTerrainPaintAudit(grid, config);
            LogUwpSkippedTributaryHeightFinal(outH, grid, config);

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
        static void ApplyVisualShorelineSmoothing(float[,] outH, GridSystem grid, float waterH, int radius, float strength, MapGenConfig config)
        {
            int w = grid.Width;
            int h = grid.Height;
            // Multi-source BFS para distancia (en celdas) al agua más cercana.
            float terrainY = config != null && config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            float shoreTargetH = waterH;
            bool stripRiverSurface = config != null && config.riverVisualUseRiverSurfaceMeshStrip;
            bool useMaskPaint = UsesRiverVisualSurfaceMaskForTerrainPaint(grid, config);
            if (WaterVisualPipelinePolicy.IsUnifiedSingleSurface(config))
            {
                float visualSurfaceOffsetWorld =
                    Mathf.Max(config.waterSurfaceOffset, 0.02f) +
                    config.unifiedWaterSurfaceExtraYOffsetWorld +
                    WaterMeshBuilder.ComputeUnifiedWaterDepthDrivenLiftWorld(config, terrainY);
                float visualWaterH = waterH + visualSurfaceOffsetWorld / Mathf.Max(1e-4f, terrainY);
                shoreTargetH = Mathf.Clamp01(visualWaterH + config.unifiedWaterShoreTerrainOffsetWorld / Mathf.Max(1e-4f, terrainY));
            }
            else if (stripRiverSurface)
            {
                float riverSurfaceWorld =
                    Mathf.Max(config.waterSurfaceOffset, 0.02f) +
                    Mathf.Max(0f, config.riverRibbonVerticalLiftWorld) +
                    Mathf.Max(0f, config.riverRibbonAntiZFightYOffsetWorld) +
                    Mathf.Max(0f, config.riverSurfaceMeshExtraYOffsetWorld);
                shoreTargetH = Mathf.Clamp01(waterH + riverSurfaceWorld / Mathf.Max(1e-4f, terrainY));
            }

            var dist = new int[w, h];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    dist[x, z] = -1;

            var qx = new Queue<int>();
            var qz = new Queue<int>();

            bool[,] rivMask = useMaskPaint ? grid.RiverVisualSurfaceMask : null;
            bool hasRivMask = rivMask != null &&
                              rivMask.GetLength(0) == w &&
                              rivMask.GetLength(1) == h;

            bool uniformUwpRiverCarve = IsUniformUwpRiverCarveChannel(config);
            int uwpCarveInset = uniformUwpRiverCarve ? ResolveUwpRiverCarveInsetCells(config) : 0;
            bool useSkippedTribExclusion = UsesUwpSkippedTributaryFunctionalMask(grid, config);
            bool[,] skippedTribMask = useSkippedTribExclusion ? grid.UwpSkippedTributaryFunctionalMask : null;
            int skippedShoreSmoothCells = 0;
            if (useSkippedTribExclusion)
            {
                for (int sx = 0; sx < w; sx++)
                {
                    for (int sz = 0; sz < h; sz++)
                    {
                        if (skippedTribMask[sx, sz])
                            skippedShoreSmoothCells++;
                    }
                }
            }

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    var t = grid.GetCell(x, z).type;
                    bool seedWater = t == CellType.Water;
                    bool seedRiver = t == CellType.River && !useMaskPaint;
                    bool seedVisualRiver = hasRivMask && rivMask[x, z];
                    if (uniformUwpRiverCarve && seedVisualRiver)
                        seedVisualRiver = IsRiverVisualMaskCoreCell(rivMask, w, h, x, z, uwpCarveInset);
                    if (seedWater || seedRiver || seedVisualRiver)
                    {
                        dist[x, z] = 0;
                        qx.Enqueue(x);
                        qz.Enqueue(z);
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
                    if (useMaskPaint && hasRivMask &&
                        grid.GetCell(nx, nz).type == CellType.River && !rivMask[nx, nz])
                        return;
                    if (skippedTribMask != null && skippedTribMask[nx, nz])
                        return;
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
                    if (skippedTribMask != null && skippedTribMask[x, z])
                        continue;

                    var t = grid.GetCell(x, z).type;
                    if (t == CellType.Water) continue;
                    if (useMaskPaint)
                    {
                        if (hasRivMask && rivMask[x, z]) continue;
                    }
                    else if (t == CellType.River)
                    {
                        continue;
                    }
                    int d = dist[x, z];
                    if (d <= 0 || d > radius) continue;

                    // d=1 -> casi al nivel del agua, d=radius -> casi sin efecto.
                    float k = 1f - (float)d / (radius + 1f);
                    k *= strength;
                    float target = Mathf.Lerp(outH[x, z], shoreTargetH, k);
                    outH[x, z] = shoreTargetH >= waterH ? Mathf.Max(waterH, target) : target;
                }
            }

            if (skippedShoreSmoothCells > 0)
            {
                Debug.Log(
                    $"[UWP_SKIP_SHORE_SMOOTH_ON_SKIPPED_TRIBUTARY] cells={skippedShoreSmoothCells}");
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

        /// <summary>Talla cauce desde celdas River lógicas + radio riverWidthRadiusCells (no usa RiverVisualSurfaceMask).</summary>
        static void ApplyLogicalRiverCorridorTerrainCarve(float[,] outH, GridSystem grid, MapGenConfig config)
        {
            if (config == null || grid == null || !config.riverVisualTerrainCarveEnabled)
                return;

            float depthW = config.riverTerrainCarveDepthWorld;
            if (depthW < 1e-4f)
                return;

            int radius = Mathf.Clamp(config.riverWidthRadiusCells, 0, 6);
            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            float depth01 = Mathf.Clamp(depthW, 0f, 3f) / terrainY;
            int falloff = Mathf.Clamp(config.riverTerrainCarveFalloffCells, 1, 32);
            float curve = Mathf.Clamp(config.riverTerrainCarveCenterCurve, 0.35f, 3.5f);
            float fordMul = Mathf.Clamp(config.riverTerrainCarveFordMul, 0.08f, 1f);
            int extra = Mathf.Clamp(config.riverVisualTerrainCarveExtraCells, 0, 4);
            int bankFall = Mathf.Clamp(config.riverVisualTerrainBankFalloffCells, 0, 8);
            float centerMul = Mathf.Clamp(config.riverVisualTerrainCenterDepthMul, 1f, 1.5f);
            float bankSoft = Mathf.Clamp(config.riverVisualTerrainBankSoftness, 0.35f, 1f);
            int maxD = falloff + extra + bankFall + radius;

            int w = grid.Width;
            int h = grid.Height;
            var corridor = new bool[w, h];
            int riverCells = 0;
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (grid.GetCell(x, z).type != CellType.River)
                        continue;
                    riverCells++;
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > radius)
                                continue;
                            int nx = x + dx;
                            int nz = z + dz;
                            if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                                continue;
                            corridor[nx, nz] = true;
                        }
                    }
                }
            }

            var dBank = new int[w, h];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    dBank[x, z] = int.MaxValue;

            var qx = new Queue<int>();
            var qz = new Queue<int>();
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (grid.GetCell(x, z).type != CellType.River)
                        continue;
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
                if (d >= maxD)
                    continue;
                void TryNb(int nx, int nz)
                {
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                        return;
                    ref var c = ref grid.GetCell(nx, nz);
                    if (c.type == CellType.Water && !corridor[nx, nz])
                        return;
                    if (c.type != CellType.Land && c.type != CellType.River && !(c.type == CellType.Water && corridor[nx, nz]))
                        return;
                    if (d + 1 >= dBank[nx, nz])
                        return;
                    dBank[nx, nz] = d + 1;
                    qx.Enqueue(nx);
                    qz.Enqueue(nz);
                }

                TryNb(x - 1, z);
                TryNb(x + 1, z);
                TryNb(x, z - 1);
                TryNb(x, z + 1);
            }

            int carved = 0;
            int bankCells = 0;
            int corridorCoreCells = 0;
            double sumCarve = 0.0;
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (dBank[x, z] > maxD)
                        continue;
                    ref var c = ref grid.GetCell(x, z);
                    if (c.type == CellType.Water && !corridor[x, z])
                        continue;
                    bool isRiverCore = c.type == CellType.River;
                    float u = Mathf.Clamp01(dBank[x, z] / (float)Mathf.Max(1, maxD));
                    float profile = Mathf.Pow(1f - u, curve);
                    if (isRiverCore)
                    {
                        profile = Mathf.Pow(profile, 1f / centerMul);
                        corridorCoreCells++;
                    }
                    else
                        profile *= bankSoft;
                    float carve = depth01 * profile;
                    float maxVisualLower = depth01 * (corridor[x, z] ? 0.45f : 0.18f);
                    carve = Mathf.Min(carve, maxVisualLower);
                    if (c.riverFord)
                        carve *= fordMul;
                    if (carve < 1e-8f)
                        continue;
                    outH[x, z] = Mathf.Clamp01(outH[x, z] - carve);
                    carved++;
                    sumCarve += carve;
                    if (!isRiverCore)
                        bankCells++;
                }
            }

            if (carved > 0 && (config.debugLogs || config.debugHydrologyNetwork || config.debugRiverVisualStats))
            {
                double avg = sumCarve / carved;
                Debug.Log(
                    $"[RiverTerrainCarveSource] source=LogicalRiverCells usedSurfaceMask=0 riverCells={riverCells} " +
                    $"corridorRadiusCells={radius} carvedCells={carved} bankCells={bankCells} corridorCoreCells={corridorCoreCells} " +
                    $"carve01_medio={(float)avg:F4}");
            }
        }

        static int MinChebyshevDistToMapBorder(int x, int z, int w, int h) =>
            Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(z, h - 1 - z));

        static bool IsMapBorderCell(int x, int z, int w, int h) =>
            MinChebyshevDistToMapBorder(x, z, w, h) == 0;

        static void AuditRiverOutletTerrain(
            MapGenConfig config,
            int riverId,
            string endpointLabel,
            bool atBorder,
            Vector2Int endpointCell,
            Vector2 outletDir,
            float[,] outH,
            GridSystem grid,
            int radiusCells,
            int lengthCells,
            float waterH,
            float maxAboveWater,
            out int sampledCells,
            out float maxAbove,
            out float avgAbove,
            out int carvedNear,
            out int bankNear,
            out int needsOutletFix)
        {
            sampledCells = carvedNear = bankNear = 0;
            maxAbove = avgAbove = 0f;
            needsOutletFix = 0;
            if (!atBorder || outH == null || grid == null)
                return;

            int w = grid.Width;
            int h = grid.Height;
            float sumAbove = 0f;
            int steps = Mathf.Max(1, lengthCells);
            var centers = new List<Vector2Int>(steps);
            var ep = new Vector2(endpointCell.x, endpointCell.y);
            for (int s = 0; s < steps; s++)
            {
                Vector2 p = ep - outletDir * s;
                centers.Add(new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(p.x), 0, w - 1), Mathf.Clamp(Mathf.RoundToInt(p.y), 0, h - 1)));
            }

            float ceiling = waterH + maxAboveWater;
            for (int ci = 0; ci < centers.Count; ci++)
            {
                var c0 = centers[ci];
                for (int dz = -radiusCells; dz <= radiusCells; dz++)
                {
                    for (int dx = -radiusCells; dx <= radiusCells; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > radiusCells)
                            continue;
                        int x = c0.x + dx;
                        int z = c0.y + dz;
                        if ((uint)x >= (uint)w || (uint)z >= (uint)h)
                            continue;
                        sampledCells++;
                        float above = outH[x, z] - waterH;
                        if (above > maxAbove)
                            maxAbove = above;
                        sumAbove += above;
                        ref var cell = ref grid.GetCell(x, z);
                        if (cell.type == CellType.River)
                            carvedNear++;
                        else if (cell.type == CellType.Land)
                            bankNear++;
                        if (outH[x, z] > ceiling + 1e-5f)
                            needsOutletFix = 1;
                    }
                }
            }

            if (sampledCells > 0)
                avgAbove = sumAbove / sampledCells;
        }

        static void LogRiverOutletTerrainAudit(
            MapGenConfig config,
            int riverId,
            string endpoint,
            bool atBorder,
            Vector2Int endpointCell,
            Vector2 outletDir,
            int sampledCells,
            float maxTerrainAboveWater01,
            float avgTerrainAboveWater01,
            int carvedCellsNearOutlet,
            int bankCellsNearOutlet,
            bool reachesBorder,
            int needsOutletFix)
        {
            if (config == null || !config.riverOutletTerrainFixDebugLogs)
                return;
            if (!config.debugLogs && !config.debugHydrologyNetwork && needsOutletFix == 0)
                return;
            Debug.Log(
                $"[RiverOutletTerrainAudit] riverId={riverId} endpoint={endpoint} atBorder={(atBorder ? 1 : 0)} " +
                $"endpointCell=({endpointCell.x},{endpointCell.y}) outletDir=({outletDir.x:F2},{outletDir.y:F2}) sampledCells={sampledCells} " +
                $"maxTerrainAboveWater01={maxTerrainAboveWater01:F4} avgTerrainAboveWater01={avgTerrainAboveWater01:F4} " +
                $"carvedCellsNearOutlet={carvedCellsNearOutlet} bankCellsNearOutlet={bankCellsNearOutlet} " +
                $"reachesBorder={(reachesBorder ? 1 : 0)} needsOutletFix={needsOutletFix}");
        }

        static void LogRiverOutletTerrainFix(
            MapGenConfig config,
            int riverId,
            string endpoint,
            bool applied,
            Vector2Int endpointCell,
            bool atBorder,
            int outletLengthCells,
            int radiusCells,
            int cellsLowered,
            float maxBeforeAboveWater01,
            float maxAfterAboveWater01,
            bool reachesBorder)
        {
            if (config == null || !config.riverOutletTerrainFixDebugLogs)
                return;
            if (!config.debugLogs && !config.debugHydrologyNetwork && !applied)
                return;
            Debug.Log(
                $"[RiverOutletTerrainFix] riverId={riverId} endpoint={endpoint} applied={(applied ? 1 : 0)} " +
                $"endpointCell=({endpointCell.x},{endpointCell.y}) atBorder={(atBorder ? 1 : 0)} outletLengthCells={outletLengthCells} " +
                $"radiusCells={radiusCells} cellsLowered={cellsLowered} maxBeforeAboveWater01={maxBeforeAboveWater01:F4} " +
                $"maxAfterAboveWater01={maxAfterAboveWater01:F4} reachesBorder={(reachesBorder ? 1 : 0)}");
        }

        static void LogRiverBorderCarveConsistency(
            MapGenConfig config,
            int riverId,
            bool startMeshAtBorder,
            bool endMeshAtBorder,
            bool startTerrainOk,
            bool endTerrainOk)
        {
            if (config == null || !config.riverOutletTerrainFixDebugLogs)
                return;
            if (!config.debugLogs && !config.debugHydrologyNetwork)
                return;
            int ok = (!startMeshAtBorder || startTerrainOk) && (!endMeshAtBorder || endTerrainOk) ? 1 : 0;
            Debug.Log(
                $"[RiverBorderCarveConsistency] riverId={riverId} startMeshAtBorder={(startMeshAtBorder ? 1 : 0)} " +
                $"endMeshAtBorder={(endMeshAtBorder ? 1 : 0)} startOk={(startTerrainOk ? 1 : 0)} endOk={(endTerrainOk ? 1 : 0)} ok={ok}");
        }

        static void LowerOutletTerrainAtCell(
            float[,] outH,
            GridSystem grid,
            int x,
            int z,
            int cheb,
            int radiusCells,
            int bankFall,
            float waterH,
            float ceiling,
            float bedExtra,
            ref int cellsLowered,
            ref float maxAfter)
        {
            if ((uint)x >= (uint)grid.Width || (uint)z >= (uint)grid.Height)
                return;
            ref var cell = ref grid.GetCell(x, z);
            if (cell.riverFord)
                return;

            float distNorm = cheb / (float)Mathf.Max(1, radiusCells);
            float centerW = 1f - Mathf.SmoothStep(0f, 1f, distNorm);
            float bankW = Mathf.SmoothStep(0f, 1f, (float)cheb / Mathf.Max(1, radiusCells + bankFall));
            float target = ceiling - centerW * bedExtra;
            target = Mathf.Lerp(target, ceiling, bankW * 0.65f);

            float before = outH[x, z];
            if (before > target + 1e-6f)
                cellsLowered++;
            outH[x, z] = Mathf.Min(before, target);
            maxAfter = Mathf.Max(maxAfter, outH[x, z] - waterH);
        }

        static void StampOutletCorridorDisk(
            float[,] outH,
            GridSystem grid,
            Vector2 centerCell,
            int radiusCells,
            int bankFall,
            float waterH,
            float ceiling,
            float bedExtra,
            ref int cellsLowered,
            ref float maxAfter)
        {
            int w = grid.Width;
            int h = grid.Height;
            int sx = Mathf.Clamp(Mathf.RoundToInt(centerCell.x), 0, w - 1);
            int sz = Mathf.Clamp(Mathf.RoundToInt(centerCell.y), 0, h - 1);
            for (int dz = -radiusCells; dz <= radiusCells; dz++)
            {
                for (int dx = -radiusCells; dx <= radiusCells; dx++)
                {
                    int cheb = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
                    if (cheb > radiusCells)
                        continue;
                    LowerOutletTerrainAtCell(
                        outH,
                        grid,
                        sx + dx,
                        sz + dz,
                        cheb,
                        radiusCells,
                        bankFall,
                        waterH,
                        ceiling,
                        bedExtra,
                        ref cellsLowered,
                        ref maxAfter);
                }
            }
        }

        /// <summary>Rebaja terreno bajo entradas/salidas en borde real (post-tallado lógico, solo heightmap visual).</summary>
        static void ApplyRiverBorderOutletTerrainCarve(float[,] outH, GridSystem grid, MapGenConfig config)
        {
            if (config == null || grid == null || outH == null || !config.riverOutletTerrainFixEnabled)
                return;
            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return;

            int w = grid.Width;
            int h = grid.Height;
            float waterH = config.waterHeight01;
            float maxAbove = Mathf.Clamp(config.riverOutletTerrainFixMaxHeightAboveWater01, 0f, 0.02f);
            float ceiling = waterH + maxAbove;
            float bedExtra = Mathf.Clamp(config.riverBedDepthBelowWater01, 0f, 0.14f);
            int lengthCells = Mathf.Clamp(config.riverOutletTerrainFixLengthCells, 4, 20);
            int bankFall = Mathf.Clamp(config.riverOutletTerrainFixBankFalloffCells, 1, 8);
            float radiusMul = Mathf.Clamp(config.riverOutletTerrainFixRadiusMul, 0.8f, 2f);
            float halfWidthCells = config.riverVisualRibbonFullWidthCellsMain > 0.01f
                ? config.riverVisualRibbonFullWidthCellsMain * 0.5f
                : Mathf.Max(0.5f, config.riverVisualMeshHalfWidth / Mathf.Max(0.01f, grid.CellSizeWorld));
            int radiusCells = Mathf.Max(1, Mathf.CeilToInt(halfWidthCells * radiusMul));

            for (int riverId = 0; riverId < grid.RiverCenterlinesCellSpace.Count; riverId++)
            {
                var line = grid.RiverCenterlinesCellSpace[riverId];
                if (line == null || line.Count < 2)
                    continue;

                bool startMeshAtBorder = false;
                bool endMeshAtBorder = false;
                bool startTerrainOk = false;
                bool endTerrainOk = false;

                void ProcessEndpoint(bool isStart)
                {
                    int i0 = isStart ? 0 : line.Count - 1;
                    int i1 = isStart ? 1 : line.Count - 2;
                    Vector2 p0 = line[i0];
                    Vector2 p1 = line[i1];
                    int cx = Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, w - 1);
                    int cz = Mathf.Clamp(Mathf.RoundToInt(p0.y), 0, h - 1);
                    bool atBorder = IsMapBorderCell(cx, cz, w, h);
                    if (config.riverOutletTerrainFixOnlyAtMapBorder && !atBorder)
                        return;

                    Vector2 outward = p0 - p1;
                    if (outward.sqrMagnitude < 1e-6f)
                        outward = isStart ? Vector2.left : Vector2.right;
                    else
                        outward.Normalize();
                    Vector2 inward = -outward;

                    string ep = isStart ? "start" : "end";
                    if (isStart)
                        startMeshAtBorder = atBorder;
                    else
                        endMeshAtBorder = atBorder;

                    AuditRiverOutletTerrain(
                        config,
                        riverId,
                        ep,
                        atBorder,
                        new Vector2Int(cx, cz),
                        outward,
                        outH,
                        grid,
                        radiusCells,
                        lengthCells,
                        waterH,
                        maxAbove,
                        out int sampled,
                        out float maxBefore,
                        out float avgBefore,
                        out int carvedNear,
                        out int bankNear,
                        out int needsFix);

                    LogRiverOutletTerrainAudit(
                        config,
                        riverId,
                        ep,
                        atBorder,
                        new Vector2Int(cx, cz),
                        outward,
                        sampled,
                        maxBefore,
                        avgBefore,
                        carvedNear,
                        bankNear,
                        atBorder,
                        needsFix);

                    if (!atBorder)
                        return;

                    int cellsLowered = 0;
                    float maxAfter = 0f;
                    for (int step = 0; step < lengthCells; step++)
                    {
                        Vector2 hub = p0 - inward * step;
                        StampOutletCorridorDisk(
                            outH,
                            grid,
                            hub,
                            radiusCells,
                            bankFall,
                            waterH,
                            ceiling,
                            bedExtra,
                            ref cellsLowered,
                            ref maxAfter);
                    }

                    Vector2 borderTan = new Vector2(-outward.y, outward.x);
                    int tangentSpan = Mathf.Max(radiusCells, lengthCells / 2);
                    for (int t = -tangentSpan; t <= tangentSpan; t++)
                    {
                        Vector2 along = new Vector2(cx, cz) + borderTan * t;
                        StampOutletCorridorDisk(
                            outH,
                            grid,
                            along,
                            radiusCells,
                            bankFall,
                            waterH,
                            ceiling,
                            bedExtra,
                            ref cellsLowered,
                            ref maxAfter);
                    }

                    bool terrainOk = maxAfter <= maxAbove + 1e-4f;
                    if (isStart)
                        startTerrainOk = terrainOk;
                    else
                        endTerrainOk = terrainOk;

                    LogRiverOutletTerrainFix(
                        config,
                        riverId,
                        ep,
                        cellsLowered > 0 || needsFix == 1,
                        new Vector2Int(cx, cz),
                        atBorder,
                        lengthCells,
                        radiusCells,
                        cellsLowered,
                        maxBefore,
                        maxAfter,
                        atBorder);
                }

                ProcessEndpoint(isStart: true);
                ProcessEndpoint(isStart: false);

                LogRiverBorderCarveConsistency(
                    config,
                    riverId,
                    startMeshAtBorder,
                    endMeshAtBorder,
                    startTerrainOk,
                    endTerrainOk);
            }
        }

        struct RiverEndReachTerrainParams
        {
            public int reachLengthCells;
            public int radiusCells;
            public int bankFall;
            public float waterH;
            public float ceiling;
            public float bedExtra;
            public float visualHalfWidthCells;
            public int logicalRadiusCells;
            public float meshWidthMul;
            public float endpointWidthMul;
            public bool usesVisualWidth;
        }

        static RiverEndReachTerrainParams BuildRiverEndReachTerrainParams(MapGenConfig config, GridSystem grid)
        {
            float cellSize = Mathf.Max(0.01f, grid.CellSizeWorld);
            float normalMul = Mathf.Clamp(config.riverSurfaceVisualNormalWidthMul, 1.25f, 3f);
            float endpointMul = Mathf.Clamp(config.riverSurfaceBorderEndpointWidthMul, 1.5f, 3f);
            float visualHalfWidthCells;
            if (config.riverEndReachTerrainFixUseVisualWidth)
            {
                float oldBaseHalfWorld = config.riverVisualRibbonFullWidthCellsMain > 0.01f
                    ? config.riverVisualRibbonFullWidthCellsMain * 0.5f * cellSize
                    : config.riverVisualMeshHalfWidth;
                visualHalfWidthCells = (oldBaseHalfWorld / cellSize) * normalMul;
            }
            else
            {
                visualHalfWidthCells = config.riverVisualRibbonFullWidthCellsMain > 0.01f
                    ? config.riverVisualRibbonFullWidthCellsMain * 0.5f
                    : config.riverVisualMeshHalfWidth / cellSize;
            }

            int logicalRadius = Mathf.Max(1, config.riverWidthRadiusCells);
            float radiusMul = Mathf.Clamp(config.riverEndReachTerrainFixRadiusMul, 0.8f, 2f);
            int radiusCells = Mathf.Max(
                logicalRadius,
                Mathf.CeilToInt(visualHalfWidthCells * radiusMul));

            int outletLen = Mathf.Clamp(config.riverOutletTerrainFixLengthCells, 4, 20);
            int reachLen = Mathf.Max(12, outletLen, config.riverEndReachTerrainFixLengthCells);
            reachLen = Mathf.Clamp(reachLen, 12, 48);

            float maxAbove = Mathf.Clamp(
                config.riverEndReachTerrainFixMaxHeightAboveWater01 > 1e-6f
                    ? config.riverEndReachTerrainFixMaxHeightAboveWater01
                    : config.riverOutletTerrainFixMaxHeightAboveWater01,
                0f,
                0.02f);

            return new RiverEndReachTerrainParams
            {
                reachLengthCells = reachLen,
                radiusCells = radiusCells,
                bankFall = Mathf.Clamp(config.riverOutletTerrainFixBankFalloffCells, 1, 8),
                waterH = config.waterHeight01,
                ceiling = config.waterHeight01 + maxAbove,
                bedExtra = Mathf.Clamp(config.riverBedDepthBelowWater01, 0f, 0.14f),
                visualHalfWidthCells = visualHalfWidthCells,
                logicalRadiusCells = logicalRadius,
                meshWidthMul = normalMul,
                endpointWidthMul = endpointMul,
                usesVisualWidth = config.riverEndReachTerrainFixUseVisualWidth
            };
        }

        static void BuildReachPolylineSamples(
            IReadOnlyList<Vector2> line,
            int reachLengthCells,
            bool fromStart,
            List<Vector2> samples)
        {
            samples.Clear();
            if (line == null || line.Count < 2)
                return;

            int count = Mathf.Min(reachLengthCells, line.Count);
            int iBegin = fromStart ? 0 : line.Count - count;
            int iEnd = fromStart ? count - 1 : line.Count - 1;
            samples.Add(line[iBegin]);
            for (int i = iBegin; i < iEnd; i++)
            {
                Vector2 a = line[i];
                Vector2 b = line[i + 1];
                float segLen = Vector2.Distance(a, b);
                int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / 0.35f));
                for (int s = 1; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    samples.Add(Vector2.Lerp(a, b, t));
                }
            }
        }

        static float MinDistanceToPolylineCells(Vector2 p, IReadOnlyList<Vector2> polyline)
        {
            if (polyline == null || polyline.Count == 0)
                return float.MaxValue;
            if (polyline.Count == 1)
                return Vector2.Distance(p, polyline[0]);

            float best = float.MaxValue;
            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Vector2 a = polyline[i];
                Vector2 b = polyline[i + 1];
                Vector2 ab = b - a;
                float abLenSq = ab.sqrMagnitude;
                float t = abLenSq < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLenSq);
                Vector2 closest = a + ab * t;
                best = Mathf.Min(best, Vector2.Distance(p, closest));
            }

            return best;
        }

        static void AuditRiverEndReachTerrain(
            float[,] outH,
            GridSystem grid,
            IReadOnlyList<Vector2> line,
            IReadOnlyList<Vector2> reachSamples,
            RiverEndReachTerrainParams p,
            Vector2Int anchorCell,
            bool fromStart,
            out int sampleCount,
            out float maxAboveWater,
            out float avgAboveWater,
            out int cellsAboveWater,
            out Vector2Int worstCell,
            out int worstDistFromEndCells)
        {
            sampleCount = reachSamples != null ? reachSamples.Count : 0;
            maxAboveWater = 0f;
            avgAboveWater = 0f;
            cellsAboveWater = 0;
            worstCell = Vector2Int.zero;
            worstDistFromEndCells = 0;
            if (outH == null || grid == null || reachSamples == null || reachSamples.Count == 0)
                return;

            int w = grid.Width;
            int h = grid.Height;
            int r = p.radiusCells + p.bankFall;
            float sum = 0f;
            int n = 0;

            int minX = w - 1;
            int maxX = 0;
            int minZ = h - 1;
            int maxZ = 0;
            for (int i = 0; i < reachSamples.Count; i++)
            {
                Vector2 c = reachSamples[i];
                minX = Mathf.Min(minX, Mathf.FloorToInt(c.x) - r);
                maxX = Mathf.Max(maxX, Mathf.CeilToInt(c.x) + r);
                minZ = Mathf.Min(minZ, Mathf.FloorToInt(c.y) - r);
                maxZ = Mathf.Max(maxZ, Mathf.CeilToInt(c.y) + r);
            }

            minX = Mathf.Clamp(minX, 0, w - 1);
            maxX = Mathf.Clamp(maxX, 0, w - 1);
            minZ = Mathf.Clamp(minZ, 0, h - 1);
            maxZ = Mathf.Clamp(maxZ, 0, h - 1);

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    float distAxis = MinDistanceToPolylineCells(new Vector2(x + 0.5f, z + 0.5f), reachSamples);
                    if (distAxis > p.radiusCells + p.bankFall)
                        continue;

                    float above = outH[x, z] - p.waterH;
                    if (above <= p.ceiling - p.waterH + 1e-5f)
                        continue;

                    cellsAboveWater++;
                    sum += above;
                    n++;

                    int distAnchor = Mathf.Max(Mathf.Abs(x - anchorCell.x), Mathf.Abs(z - anchorCell.y));
                    if (above > maxAboveWater + 1e-6f)
                    {
                        maxAboveWater = above;
                        worstCell = new Vector2Int(x, z);
                        worstDistFromEndCells = distAnchor;
                    }
                    else
                    {
                        maxAboveWater = Mathf.Max(maxAboveWater, above);
                    }
                }
            }

            if (n > 0)
                avgAboveWater = sum / n;
        }

        static void StampRiverEndReachCorridor(
            float[,] outH,
            GridSystem grid,
            IReadOnlyList<Vector2> reachSamples,
            RiverEndReachTerrainParams p,
            ref int cellsLowered,
            ref int skippedFordCells,
            ref float maxAfter)
        {
            if (reachSamples == null)
                return;

            for (int si = 0; si < reachSamples.Count; si++)
            {
                float tAlong = reachSamples.Count <= 1
                    ? 1f
                    : si / (float)(reachSamples.Count - 1);
                int localRadius = Mathf.CeilToInt(
                    p.radiusCells * Mathf.Lerp(1f, Mathf.Clamp(p.endpointWidthMul, 1f, 3f), tAlong));

                Vector2 hub = reachSamples[si];
                int sx = Mathf.Clamp(Mathf.RoundToInt(hub.x), 0, grid.Width - 1);
                int sz = Mathf.Clamp(Mathf.RoundToInt(hub.y), 0, grid.Height - 1);
                for (int dz = -localRadius; dz <= localRadius; dz++)
                {
                    for (int dx = -localRadius; dx <= localRadius; dx++)
                    {
                        int x = sx + dx;
                        int z = sz + dz;
                        if ((uint)x >= (uint)grid.Width || (uint)z >= (uint)grid.Height)
                            continue;
                        int cheb = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
                        if (cheb > localRadius)
                            continue;

                        ref var cell = ref grid.GetCell(x, z);
                        if (cell.riverFord)
                        {
                            skippedFordCells++;
                            continue;
                        }

                        float distNorm = cheb / (float)Mathf.Max(1, localRadius);
                        float centerW = 1f - Mathf.SmoothStep(0f, 1f, distNorm);
                        float bankW = Mathf.SmoothStep(0f, 1f, (float)cheb / Mathf.Max(1, localRadius + p.bankFall));
                        float target = p.ceiling - centerW * p.bedExtra;
                        target = Mathf.Lerp(target, p.ceiling, bankW * 0.65f);

                        float before = outH[x, z];
                        if (before > target + 1e-6f)
                            cellsLowered++;
                        outH[x, z] = Mathf.Min(before, target);
                        maxAfter = Mathf.Max(maxAfter, outH[x, z] - p.waterH);
                    }
                }
            }
        }

        static void ApplyRiverEndReachTerrainCarve(float[,] outH, GridSystem grid, MapGenConfig config)
        {
            if (config == null || grid == null || outH == null || !config.riverEndReachTerrainFixEnabled)
                return;
            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return;

            int w = grid.Width;
            int h = grid.Height;
            var reachParams = BuildRiverEndReachTerrainParams(config, grid);
            var reachSamples = new List<Vector2>(reachParams.reachLengthCells * 4);
            var startSamples = new List<Vector2>(reachParams.reachLengthCells * 4);

            for (int riverId = 0; riverId < grid.RiverCenterlinesCellSpace.Count; riverId++)
            {
                var line = grid.RiverCenterlinesCellSpace[riverId];
                if (line == null || line.Count < 2)
                    continue;

                Vector2 endPt = line[line.Count - 1];
                int endCx = Mathf.Clamp(Mathf.RoundToInt(endPt.x), 0, w - 1);
                int endCz = Mathf.Clamp(Mathf.RoundToInt(endPt.y), 0, h - 1);
                bool endAtBorder = IsMapBorderCell(endCx, endCz, w, h);
                if (!endAtBorder)
                    continue;

                BuildReachPolylineSamples(line, reachParams.reachLengthCells, fromStart: false, reachSamples);

                AuditRiverEndReachTerrain(
                    outH,
                    grid,
                    line,
                    reachSamples,
                    reachParams,
                    new Vector2Int(endCx, endCz),
                    fromStart: false,
                    out int sampleCount,
                    out float maxBefore,
                    out float avgBefore,
                    out int cellsAboveBefore,
                    out Vector2Int worstCell,
                    out int worstDistFromEnd);

                if (config.riverEndReachTerrainFixDebugLogs || config.debugLogs || config.debugHydrologyNetwork)
                {
                    Debug.Log(
                        $"[RiverEndReachTerrainAudit] riverId={riverId} endAtBorder=1 endCell=({endCx},{endCz}) " +
                        $"waterHeight01={reachParams.waterH:F4} sampleCount={sampleCount} reachLengthCells={reachParams.reachLengthCells} " +
                        $"visualHalfWidthCells={reachParams.visualHalfWidthCells:F3} logicalRadiusCells={reachParams.logicalRadiusCells} " +
                        $"maxTerrainAboveWaterBefore={maxBefore:F4} avgTerrainAboveWaterBefore={avgBefore:F4} worstCell=({worstCell.x},{worstCell.y}) " +
                        $"worstDistFromEndCells={worstDistFromEnd} terrainCellsAboveWaterBefore={cellsAboveBefore} " +
                        $"meshWidthMul={reachParams.meshWidthMul:F3} usesVisualWidth={(reachParams.usesVisualWidth ? 1 : 0)}");
                }

                int cellsLowered = 0;
                int skippedFord = 0;
                float maxAfter = 0f;
                int startIndex = Mathf.Max(0, line.Count - reachParams.reachLengthCells);
                int endIndex = line.Count - 1;

                StampRiverEndReachCorridor(
                    outH,
                    grid,
                    reachSamples,
                    reachParams,
                    ref cellsLowered,
                    ref skippedFord,
                    ref maxAfter);

                bool applied = cellsLowered > 0 || cellsAboveBefore > 0;
                float maxAboveAllowed = reachParams.ceiling - reachParams.waterH;
                bool ok = maxAfter <= maxAboveAllowed + 1e-4f;

                if (config.riverEndReachTerrainFixDebugLogs || config.debugLogs || config.debugHydrologyNetwork)
                {
                    Debug.Log(
                        $"[RiverEndReachTerrainFix] riverId={riverId} applied={(applied ? 1 : 0)} cellsLowered={cellsLowered} " +
                        $"reachLengthCells={reachParams.reachLengthCells} radiusCells={reachParams.radiusCells} " +
                        $"maxBeforeAboveWater01={maxBefore:F4} maxAfterAboveWater01={maxAfter:F4} worstDistFromEndCells={worstDistFromEnd} " +
                        $"startIndex={startIndex} endIndex={endIndex} skippedFordCells={skippedFord} ok={(ok ? 1 : 0)}");
                }

                Vector2 startPt = line[0];
                int startCx = Mathf.Clamp(Mathf.RoundToInt(startPt.x), 0, w - 1);
                int startCz = Mathf.Clamp(Mathf.RoundToInt(startPt.y), 0, h - 1);

                BuildReachPolylineSamples(line, reachParams.reachLengthCells, fromStart: true, startSamples);
                AuditRiverEndReachTerrain(
                    outH,
                    grid,
                    line,
                    startSamples,
                    reachParams,
                    new Vector2Int(startCx, startCz),
                    fromStart: true,
                    out _,
                    out float startMaxAbove,
                    out _,
                    out _,
                    out _,
                    out _);

                int startCellsLowered = 0;
                int startSkippedFord = 0;
                float startMaxAfter = 0f;
                if (IsMapBorderCell(startCx, startCz, w, h))
                {
                    StampRiverEndReachCorridor(
                        outH,
                        grid,
                        startSamples,
                        reachParams,
                        ref startCellsLowered,
                        ref startSkippedFord,
                        ref startMaxAfter);
                    startMaxAbove = startMaxAfter;
                }

                if (config.riverEndReachTerrainFixDebugLogs || config.debugLogs || config.debugHydrologyNetwork)
                {
                    bool startReachOk = startMaxAbove <= maxAboveAllowed + 1e-4f;
                    Debug.Log(
                        $"[RiverStartEndTerrainCompare] startMaxAboveWater={startMaxAbove:F4} endMaxAboveWater={maxAfter:F4} " +
                        $"startCellsLowered={startCellsLowered} endCellsLowered={cellsLowered} " +
                        $"startSkippedFordCells={startSkippedFord} startReachOk={(startReachOk ? 1 : 0)} endReachOk={(ok ? 1 : 0)}");
                }
            }
        }

        static void ApplyTributaryVisualTerrainCarve(float[,] outH, GridSystem grid, MapGenConfig config)
        {
            if (config == null || grid == null || outH == null || !config.riverVisualTerrainCarveEnabled)
                return;
            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count < 2)
                return;

            float waterH = config.waterHeight01;
            float maxAbove = Mathf.Clamp(config.riverConfluenceTerrainMaxHeightAboveWater01, 0f, 0.02f);
            float ceiling = waterH + maxAbove;
            float bedExtra = config.uwpOwnedVisualPolicy
                ? Mathf.Clamp(config.tributaryBedDepthBelowWater01, 0f, 0.14f)
                : Mathf.Clamp(config.riverBedDepthBelowWater01, 0f, 0.14f);
            int bankFall = Mathf.Clamp(config.riverVisualTerrainBankFalloffCells, 1, 8);
            float cellSize = Mathf.Max(0.01f, grid.CellSizeWorld);
            float carveRadiusMul = Mathf.Clamp(config.riverTributaryTerrainCarveRadiusMul, 1f, 1.25f);
            if (config.uwpOwnedVisualPolicy)
                carveRadiusMul = Mathf.Min(carveRadiusMul, 0.95f);

            for (int riverId = 1; riverId < grid.RiverCenterlinesCellSpace.Count; riverId++)
            {
                var line = grid.RiverCenterlinesCellSpace[riverId];
                if (line == null || line.Count < 2)
                    continue;

                var carveLine = RiverSurfaceMeshBuilder.BuildSnappedCellCenterPolyline(line);
                if (carveLine == null || carveLine.Count < 2)
                    carveLine = line;

                float ratio = grid.RiverWidthRatioToMain != null && riverId < grid.RiverWidthRatioToMain.Count
                    ? grid.RiverWidthRatioToMain[riverId]
                    : RiverDendriticUtility.WidthRatioToMain(
                        config,
                        RiverDendriticUtility.RoleForPlacement(riverId, 0, 48, 0));
                float avgHalfW = RiverSurfaceMeshBuilder.GetTributaryAvgHalfWidthWorld(riverId);
                if (avgHalfW < 0.02f)
                    avgHalfW = RiverDendriticUtility.MainReferenceHalfWidthWorld(config, cellSize) * ratio;

                int radiusCells = Mathf.Max(
                    2,
                    Mathf.CeilToInt((avgHalfW / cellSize) * carveRadiusMul));
                int cellsLowered = 0;
                int skippedFord = 0;
                float maxAfter = 0f;
                int step = Mathf.Max(1, carveLine.Count / 96);
                for (int pi = 0; pi < carveLine.Count; pi += step)
                {
                    int cx = Mathf.Clamp(Mathf.RoundToInt(carveLine[pi].x), 0, grid.Width - 1);
                    int cz = Mathf.Clamp(Mathf.RoundToInt(carveLine[pi].y), 0, grid.Height - 1);
                    ref var c = ref grid.GetCell(cx, cz);
                    if (c.riverFord)
                    {
                        skippedFord++;
                        continue;
                    }

                    Vector2 hub = new Vector2(carveLine[pi].x + 0.5f, carveLine[pi].y + 0.5f);
                    StampOutletCorridorDisk(
                        outH,
                        grid,
                        hub,
                        radiusCells,
                        bankFall,
                        waterH,
                        ceiling,
                        bedExtra,
                        ref cellsLowered,
                        ref maxAfter);
                }

                if (config.riverSurfaceTributaryWidthDebugLogs || config.debugLogs || config.debugHydrologyNetwork)
                {
                    Debug.Log(
                        $"[RiverTributaryTerrainCarve] riverId={riverId} radiusCells={radiusCells} cellsLowered={cellsLowered} " +
                        $"skippedFordCells={skippedFord} ok=1");
                }
            }
        }

        static bool IsUniformUwpRiverCarveChannel(MapGenConfig config)
        {
            return config != null &&
                config.uwpOwnedVisualPolicy &&
                config.riverTerrainCarveFalloffCells <= 0 &&
                config.riverVisualTerrainCarveExtraCells <= 0 &&
                config.riverVisualTerrainBankFalloffCells <= 0;
        }

        /// <summary>
        /// Tras carve frozen: asegura CarveApplied en superficies con centerline válida
        /// (evita falsos positivos en ValidateAndLogUwpWaterSurfaceFinal).
        /// </summary>
        static void EnsureUwpFrozenCarveFlagsMarked(GridSystem grid)
        {
            if (grid?.RiverVisualSurfaces == null)
                return;
            for (int ri = 0; ri < grid.RiverVisualSurfaces.Count; ri++)
            {
                var surface = grid.RiverVisualSurfaces[ri];
                if (surface == null || surface.Skipped || surface.CarveApplied)
                    continue;
                var line = surface.FinalCenterlineCells;
                if (line == null || line.Count < 2)
                    continue;
                if (ri > 0 && RiverSurfaceMeshBuilder.IsUwpDegenerateTributary(line, grid.CellSizeWorld))
                    continue;
                RiverSurfaceMeshBuilder.MarkUwpRiverCarveApplied(grid, ri, line);
            }
        }

        static float ComputeUniformUwpRiverCarveFloor01(MapGenConfig config, float depth01, int riverIndex = 0)
        {
            float waterH = config != null ? config.waterHeight01 : 0.24f;
            bool tributary = riverIndex > 0;
            float bedExtra = config != null
                ? Mathf.Clamp(
                    tributary ? config.tributaryBedDepthBelowWater01 : config.riverBedDepthBelowWater01,
                    0.010f,
                    0.08f)
                : (tributary ? 0.034f : 0.022f);
            float depthScale = tributary ? 1.28f : 1f;
            float bedWeight = tributary ? 0.88f : 0.4f;
            float extraBelow = tributary ? 0.028f : 0f;
            if (tributary)
            {
                float tribGap = Mathf.Max(
                    0.055f,
                    depth01 * depthScale * 0.96f + bedExtra * bedWeight + extraBelow);
                return Mathf.Clamp01(waterH - tribGap);
            }

            return Mathf.Clamp01(waterH - depth01 * 0.96f * depthScale - bedExtra * bedWeight - extraBelow);
        }

        /// <summary>Lecho carve alineado con Y unificado del mesh (superficie − profundidad fija en mundo).</summary>
        static float ComputeUwpUnifiedChannelBedFloor01(
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float depth01)
        {
            if (grid == null || config == null)
                return ComputeUniformUwpRiverCarveFloor01(config, depth01, riverIndex);

            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            float baseWaterY = grid.Origin.y + config.waterHeight01 * terrainY +
                Mathf.Max(config.waterSurfaceOffset, 0.02f);
            float surfaceY = WaterVisualPipelinePolicy.UsesUwpUnifiedWaterSurfaceLevel(config)
                ? WaterVisualPipelinePolicy.ResolveUwpUnifiedChannelSurfaceWorldY(config, baseWaterY)
                : baseWaterY + Mathf.Max(0.02f, config.riverRibbonAntiZFightYOffsetWorld) +
                  Mathf.Max(0f, config.riverSurfaceMeshExtraYOffsetWorld);

            bool tributary = riverIndex > 0;
            float antiZ = Mathf.Max(0.02f, config.riverRibbonAntiZFightYOffsetWorld);
            // Main: lecho bajo Snap bankBand (0.22) y FoamWidth (~0.39).
            // Un poco más hondo que el mínimo histórico (0.50) para valle en el eje.
            float bedDepthWorld = tributary
                ? Mathf.Max(0.052f, config.riverTerrainCarveDepthWorld * 0.58f + antiZ * 0.5f)
                : Mathf.Max(0.52f, config.riverTerrainCarveDepthWorld * 1.22f + antiZ * 0.5f);
            // Headwater: lecho CONTÍNUO bajo mesh (flatFloor). Más bajo que trib genérico
            // para no “comerse” el ribbon (charcos / foam-only).
            if (tributary &&
                UwpTributaryOriginUtility.GetOrigin(grid, riverIndex) == UwpTributaryOriginKind.HeadwaterFeeder)
            {
                bedDepthWorld = Mathf.Max(
                    bedDepthWorld,
                    Mathf.Max(0.40f, config.riverTerrainCarveDepthWorld * 1.15f + antiZ * 0.6f));
            }
            // Inland→main: alinear profundidad de lecho con el troncal (evita escalón / “blanco”
            // en la junta; antes trib genérico 0.58× quedaba mucho más alto que el main).
            if (tributary &&
                UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex))
            {
                bedDepthWorld = Mathf.Max(
                    bedDepthWorld,
                    Mathf.Max(0.42f, config.riverTerrainCarveDepthWorld * 1.05f + antiZ * 0.55f));
            }
            float bedWorldY = surfaceY - bedDepthWorld;
            return Mathf.Clamp01((bedWorldY - grid.Origin.y) / Mathf.Max(1e-4f, terrainY));
        }

        static float ResolveUwpRiverCarveFloor01(
            GridSystem grid,
            MapGenConfig config,
            float depth01,
            int riverIndex)
        {
            if (UsesUwpFrozenCarveContract(grid, config) &&
                WaterVisualPipelinePolicy.UsesUwpUnifiedWaterSurfaceLevel(config))
                return ComputeUwpUnifiedChannelBedFloor01(grid, config, riverIndex, depth01);
            return ComputeUniformUwpRiverCarveFloor01(config, depth01, riverIndex);
        }

        static bool UsesUwpFrozenCarveContract(GridSystem grid, MapGenConfig config) =>
            config != null && config.uwpOwnedVisualPolicy && grid != null && grid.RiverVisualSurfaceCacheFrozen;

        static void TryApplyUniformUwpFloorAtCell(
            float[,] outH,
            GridSystem grid,
            bool[,] mask,
            int cx,
            int cz,
            float floorH,
            float fordFloorDelta,
            bool allowWaterCarve = false)
        {
            if (grid == null || outH == null || !grid.InBoundsCell(cx, cz))
                return;
            ref var cell = ref grid.GetCell(cx, cz);
            if (cell.type == CellType.Water && !allowWaterCarve && (mask == null || !mask[cx, cz]))
                return;
            float target = cell.riverFord ? floorH - fordFloorDelta : floorH;
            outH[cx, cz] = Mathf.Min(outH[cx, cz], target);
        }

        static void ApplyUwpConfluenceCenterlineFloorCarve(
            float[,] outH,
            GridSystem grid,
            MapGenConfig config,
            bool[,] mask,
            float floorH,
            float fordFloorDelta)
        {
            ApplyUwpRiverCenterlinesFloorCarve(outH, grid, config, mask, floorH, fordFloorDelta);
        }

        static bool IsUwpMainRiverMaskCarveCell(GridSystem grid, MapGenConfig config, int x, int z)
        {
            if (grid?.RiverVisualSurfaces == null || grid.RiverVisualSurfaces.Count == 0)
                return true;

            var main = grid.RiverVisualSurfaces[0];
            if (main.Skipped || main.FinalCenterlineCells == null || main.FinalCenterlineCells.Count < 1)
                return true;

            float mainFull = config != null ? config.riverVisualRibbonFullWidthCellsMain : 6f;
            int reach = Mathf.Max(2, Mathf.CeilToInt(mainFull * 0.55f));
            float best = float.MaxValue;
            for (int i = 0; i < main.FinalCenterlineCells.Count; i++)
            {
                Vector2 p = main.FinalCenterlineCells[i];
                float d = Mathf.Max(Mathf.Abs(p.x - (x + 0.5f)), Mathf.Abs(p.y - (z + 0.5f)));
                if (d < best)
                    best = d;
                if (best <= reach)
                    return true;
            }

            return best <= reach;
        }

        static void ApplyRiverConfluenceTerrainCarve(float[,] outH, GridSystem grid, MapGenConfig config)
        {
            if (config == null || grid == null || outH == null || !config.riverConfluenceEnabled)
                return;
            if (IsUniformUwpRiverCarveChannel(config))
                return;
            if (grid.RiverConfluences == null || grid.RiverConfluences.Count == 0)
                return;

            float waterH = config.waterHeight01;
            float maxAbove = Mathf.Clamp(config.riverConfluenceTerrainMaxHeightAboveWater01, 0f, 0.02f);
            float ceiling = waterH + maxAbove;
            float bedExtra = Mathf.Clamp(config.riverBedDepthBelowWater01, 0f, 0.14f);
            int bankFall = Mathf.Clamp(config.riverOutletTerrainFixBankFalloffCells, 1, 8);

            for (int n = 0; n < grid.RiverConfluences.Count; n++)
            {
                var node = grid.RiverConfluences[n];
                if (!node.Valid)
                    continue;

                int radiusCells = Mathf.Max(2, node.MergeRadiusCells + 2);
                if (config.riverEndReachTerrainFixUseVisualWidth)
                {
                    var p = BuildRiverEndReachTerrainParams(config, grid);
                    radiusCells = Mathf.Max(radiusCells, p.radiusCells);
                }

                int cellsLowered = 0;
                float maxAfter = 0f;
                Vector2 hub = new Vector2(node.Cell.x + 0.5f, node.Cell.y + 0.5f);
                StampOutletCorridorDisk(
                    outH,
                    grid,
                    hub,
                    radiusCells,
                    bankFall,
                    waterH,
                    ceiling,
                    bedExtra,
                    ref cellsLowered,
                    ref maxAfter);

                if (config.riverConfluenceDebugLogs || config.debugLogs || config.debugHydrologyNetwork)
                {
                    bool ok = maxAfter <= maxAbove + 1e-4f;
                    Debug.Log(
                        $"[RiverConfluenceTerrain] riverId={node.TributaryRiverIndex} receiverId={node.MainRiverIndex} " +
                        $"cellsLowered={cellsLowered} maxAfterAboveWater01={maxAfter:F4} ok={(ok ? 1 : 0)}");
                }
            }
        }

        static void ApplyRiverEndReachTerrainCarveToHeightmap(
            float[,] heights,
            int hmRes,
            GridSystem grid,
            MapGenConfig config)
        {
            if (config == null || grid == null || heights == null || hmRes < 2 || !config.riverEndReachTerrainFixEnabled)
                return;
            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return;

            int gw = grid.Width;
            int gh = grid.Height;
            var reachParams = BuildRiverEndReachTerrainParams(config, grid);
            var reachSamples = new List<Vector2>(reachParams.reachLengthCells * 4);

            for (int riverId = 0; riverId < grid.RiverCenterlinesCellSpace.Count; riverId++)
            {
                var line = grid.RiverCenterlinesCellSpace[riverId];
                if (line == null || line.Count < 2)
                    continue;

                Vector2 endPt = line[line.Count - 1];
                Vector2 startPt = line[0];
                int endCx = Mathf.Clamp(Mathf.RoundToInt(endPt.x), 0, gw - 1);
                int endCz = Mathf.Clamp(Mathf.RoundToInt(endPt.y), 0, gh - 1);
                int startCx = Mathf.Clamp(Mathf.RoundToInt(startPt.x), 0, gw - 1);
                int startCz = Mathf.Clamp(Mathf.RoundToInt(startPt.y), 0, gh - 1);
                bool endAtBorder = IsMapBorderCell(endCx, endCz, gw, gh);
                bool startAtBorder = IsMapBorderCell(startCx, startCz, gw, gh);
                if (!endAtBorder && !startAtBorder)
                    continue;

                BuildReachPolylineSamples(line, reachParams.reachLengthCells, fromStart: false, reachSamples);
                if (!endAtBorder)
                    reachSamples.Clear();
                int r = reachParams.radiusCells + reachParams.bankFall;
                int minX = gw - 1;
                int maxX = 0;
                int minZ = gh - 1;
                int maxZ = 0;

                void IncludeSamples(List<Vector2> samples)
                {
                    for (int i = 0; i < samples.Count; i++)
                    {
                        Vector2 c = samples[i];
                        minX = Mathf.Min(minX, Mathf.FloorToInt(c.x) - r);
                        maxX = Mathf.Max(maxX, Mathf.CeilToInt(c.x) + r);
                        minZ = Mathf.Min(minZ, Mathf.FloorToInt(c.y) - r);
                        maxZ = Mathf.Max(maxZ, Mathf.CeilToInt(c.y) + r);
                    }
                }

                if (endAtBorder)
                    IncludeSamples(reachSamples);
                if (startAtBorder)
                {
                    var startReachSamples = new List<Vector2>(reachParams.reachLengthCells * 4);
                    BuildReachPolylineSamples(line, reachParams.reachLengthCells, fromStart: true, startReachSamples);
                    IncludeSamples(startReachSamples);
                    reachSamples.AddRange(startReachSamples);
                }

                minX = Mathf.Clamp(minX, 0, gw - 1);
                maxX = Mathf.Clamp(maxX, 0, gw - 1);
                minZ = Mathf.Clamp(minZ, 0, gh - 1);
                maxZ = Mathf.Clamp(maxZ, 0, gh - 1);

                int cellsLowered = 0;
                float maxAfter = 0f;
                for (int x = minX; x <= maxX; x++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        if (grid.GetCell(x, z).riverFord)
                            continue;

                        float distAxis = MinDistanceToPolylineCells(new Vector2(x + 0.5f, z + 0.5f), reachSamples);
                        if (distAxis > reachParams.radiusCells + reachParams.bankFall)
                            continue;

                        int cheb = Mathf.RoundToInt(distAxis);
                        float distNorm = cheb / (float)Mathf.Max(1, reachParams.radiusCells);
                        float centerW = 1f - Mathf.SmoothStep(0f, 1f, distNorm);
                        float bankW = Mathf.SmoothStep(
                            0f,
                            1f,
                            distAxis / Mathf.Max(1, reachParams.radiusCells + reachParams.bankFall));
                        float target = reachParams.ceiling - centerW * reachParams.bedExtra;
                        target = Mathf.Lerp(target, reachParams.ceiling, bankW * 0.65f);

                        float u = gw <= 1 ? 0f : (float)x / (gw - 1);
                        float v = gh <= 1 ? 0f : (float)z / (gh - 1);
                        int hx0 = Mathf.Clamp(Mathf.FloorToInt(u * (hmRes - 1)), 0, hmRes - 1);
                        int hy0 = Mathf.Clamp(Mathf.FloorToInt(v * (hmRes - 1)), 0, hmRes - 1);
                        int hx1 = Mathf.Min(hx0 + 1, hmRes - 1);
                        int hy1 = Mathf.Min(hy0 + 1, hmRes - 1);

                        void LowerHm(int hy, int hx)
                        {
                            float before = heights[hy, hx];
                            if (before > target + 1e-6f)
                                cellsLowered++;
                            heights[hy, hx] = Mathf.Min(before, target);
                            maxAfter = Mathf.Max(maxAfter, heights[hy, hx] - reachParams.waterH);
                        }

                        LowerHm(hy0, hx0);
                        if (hx1 != hx0)
                            LowerHm(hy0, hx1);
                        if (hy1 != hy0)
                            LowerHm(hy1, hx0);
                        if (hx1 != hx0 && hy1 != hy0)
                            LowerHm(hy1, hx1);
                    }
                }

                if (config.riverEndReachTerrainFixDebugLogs || config.debugLogs || config.debugHydrologyNetwork)
                {
                    float maxAboveAllowed = reachParams.ceiling - reachParams.waterH;
                    bool ok = maxAfter <= maxAboveAllowed + 1e-4f;
                    Debug.Log(
                        $"[RiverEndReachTerrainFix] riverId={riverId} phase=heightmap_post_smooth applied=1 cellsLowered={cellsLowered} " +
                        $"reachLengthCells={reachParams.reachLengthCells} radiusCells={reachParams.radiusCells} " +
                        $"maxAfterAboveWater01={maxAfter:F4} ok={(ok ? 1 : 0)}");
                }
            }
        }

        static bool IsRiverVisualMaskCoreCell(bool[,] mask, int w, int h, int x, int z, int insetCells)
        {
            if (mask == null || !mask[x, z])
                return false;
            if (insetCells <= 0)
                return true;
            for (int dz = -insetCells; dz <= insetCells; dz++)
            {
                for (int dx = -insetCells; dx <= insetCells; dx++)
                {
                    int nx = x + dx;
                    int nz = z + dz;
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)h || !mask[nx, nz])
                        return false;
                }
            }

            return true;
        }

        static int ResolveUwpRiverCarveInsetCells(MapGenConfig config)
        {
            if (config == null)
                return 1;
            float mainW = Mathf.Max(0.5f, config.riverVisualRibbonFullWidthCellsMain);
            return Mathf.Clamp(Mathf.RoundToInt(mainW * 0.10f), 1, 2);
        }

        const float UwpCenterlineExtraCarveDepthWorld = 0.2f;

        static void StampUniformUwpCenterlineFloor(
            float[,] outH,
            GridSystem grid,
            MapGenConfig config,
            bool[,] mask,
            List<Vector2> line,
            float floorH,
            float fordFloorDelta)
        {
            if (line == null || line.Count < 1 || grid == null || outH == null)
                return;
            int w = grid.Width;
            int h = grid.Height;
            float terrainY = config != null && config.terrainHeightWorld > 0f
                ? config.terrainHeightWorld
                : 50f;
            float centerExtraDepth01 = UwpCenterlineExtraCarveDepthWorld / Mathf.Max(1e-4f, terrainY);
            for (int i = 0; i < line.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.RoundToInt(line[i].x), 0, w - 1);
                int cz = Mathf.Clamp(Mathf.RoundToInt(line[i].y), 0, h - 1);
                ref var cell = ref grid.GetCell(cx, cz);
                if (cell.type == CellType.Water && (mask == null || !mask[cx, cz]))
                    continue;
                float target = cell.riverFord ? floorH - fordFloorDelta : floorH - centerExtraDepth01;
                outH[cx, cz] = target;
            }
        }

        const float UwpTributaryCarveHalfWidthMul = 0.90f;

        static float ResolveUwpCarveHalfWidthMul(MapGenConfig config, int riverIndex)
        {
            if (config == null)
                return UwpTributaryCarveHalfWidthMul;
            return riverIndex == 0
                ? Mathf.Clamp(config.uwpCarveHalfWidthMulMain, 0.65f, 1.15f)
                : Mathf.Clamp(config.uwpCarveHalfWidthMulTributary, 0.65f, 1.15f);
        }

        static float SampleUwpCarveLongitudinalRadiusMul(MapGenConfig config, int riverIndex, int srcIdx)
        {
            if (config == null)
                return 1f;
            float amp = riverIndex == 0
                ? config.uwpCarveLongitudinalRadiusNoiseAmpMain
                : config.uwpCarveLongitudinalRadiusNoiseAmpTributary;
            amp = Mathf.Clamp(amp, 0f, 0.08f);
            if (amp <= 1e-5f)
                return 1f;
            float scale = Mathf.Clamp(config.uwpCarveLongitudinalRadiusNoiseScale, 0.01f, 0.12f);
            float ox = (config.seed % 997) * 0.0413f + riverIndex * 0.173f;
            float oz = (config.seed / 997) * 0.0371f + srcIdx * scale;
            float n = Mathf.PerlinNoise(ox, oz);
            return 1f + (n * 2f - 1f) * amp;
        }

        static float ComputeUwpCarveBellProfile01(float normalizedDist, float flatRatio, float bankPower)
        {
            float t = Mathf.Clamp01(normalizedDist);
            // Honrar perfil RTS (flatRatio≈0.16): núcleo estrecho + pendiente a orillas.
            flatRatio = Mathf.Clamp(flatRatio, 0.12f, 0.55f);
            bankPower = Mathf.Clamp(bankPower, 1.2f, 2.8f);
            if (t <= flatRatio)
                return 1f;
            float u = Mathf.InverseLerp(flatRatio, 1f, t);
            u = u * u * (3f - 2f * u);
            return Mathf.Pow(1f - u, bankPower);
        }

        const float LakeFirstTributaryCarveWidthMul = 0.9f;

        static bool TryGetUwpTributaryCarveHalfWidthWorld(
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            int pointIndex,
            out float halfWidthWorld)
        {
            halfWidthWorld = 0f;
            if (grid?.RiverVisualSurfaces == null ||
                riverIndex < 0 || riverIndex >= grid.RiverVisualSurfaces.Count)
                return false;

            var surface = grid.RiverVisualSurfaces[riverIndex];
            if (surface.Skipped)
                return false;

            bool frozenTrib = UsesUwpFrozenCarveContract(grid, config) && riverIndex > 0;
            List<float> widths = null;
            if (frozenTrib &&
                surface.MaskHalfWidthsWorld != null && surface.MaskHalfWidthsWorld.Count > 0)
                widths = surface.MaskHalfWidthsWorld;
            else if (frozenTrib &&
                surface.HalfWidthsWorld != null && surface.HalfWidthsWorld.Count > 0)
                widths = surface.HalfWidthsWorld;
            else if (surface.MaskHalfWidthsWorld != null && surface.MaskHalfWidthsWorld.Count > 0)
                widths = surface.MaskHalfWidthsWorld;
            else
                widths = surface.HalfWidthsWorld;
            if (widths == null || widths.Count == 0)
                return false;

            int idx = Mathf.Clamp(pointIndex, 0, widths.Count - 1);
            halfWidthWorld = widths[idx];
            return halfWidthWorld > 1e-4f;
        }

        static int HalfWidthWorldToUwpCarveRadiusCells(float halfWidthWorld, float cellSizeWorld, float halfWidthMul)
        {
            float cs = Mathf.Max(0.01f, cellSizeWorld);
            float mul = Mathf.Clamp(halfWidthMul, 0.65f, 1.15f);
            return Mathf.Max(1, Mathf.CeilToInt(halfWidthWorld * mul / cs));
        }

        /// <summary>
        /// Headwater: radio en celdas solo para bounding box del stamp.
        /// El límite real es maxDistWorld ≈ half-width del mesh (carve ≤ mesh).
        /// </summary>
        static int HalfWidthWorldToUwpHeadwaterCarveRadiusCells(float halfWidthWorld, float cellSizeWorld)
        {
            float cs = Mathf.Max(0.01f, cellSizeWorld);
            return Mathf.Max(1, Mathf.CeilToInt(halfWidthWorld / cs + 0.35f));
        }

        static bool TryGetUwpTributaryMeshHalfWidthWorld(
            GridSystem grid,
            int riverIndex,
            int pointIndex,
            out float halfWidthWorld)
        {
            halfWidthWorld = 0f;
            if (grid?.RiverVisualSurfaces == null ||
                riverIndex < 0 || riverIndex >= grid.RiverVisualSurfaces.Count)
                return false;
            var surface = grid.RiverVisualSurfaces[riverIndex];
            if (surface == null || surface.Skipped || surface.HalfWidthsWorld == null || surface.HalfWidthsWorld.Count == 0)
                return false;
            int idx = Mathf.Clamp(pointIndex, 0, surface.HalfWidthsWorld.Count - 1);
            halfWidthWorld = surface.HalfWidthsWorld[idx];
            return halfWidthWorld > 1e-4f;
        }

        static List<Vector2> DensifyUwpTributaryCarvePolyline(List<Vector2> line, float spacingCells = 0.38f)
        {
            if (line == null || line.Count < 2)
                return line;

            spacingCells = Mathf.Clamp(spacingCells, 0.22f, 0.65f);
            var densified = new List<Vector2>(line.Count * 4) { line[0] };
            for (int i = 0; i < line.Count - 1; i++)
            {
                Vector2 a = line[i];
                Vector2 b = line[i + 1];
                float segLen = Vector2.Distance(a, b);
                int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / spacingCells));
                for (int s = 1; s <= steps; s++)
                    densified.Add(Vector2.Lerp(a, b, s / (float)steps));
            }

            return densified.Count >= 2 ? densified : line;
        }

        static int MapCarvePointToSourceIndex(List<Vector2> source, Vector2 p)
        {
            if (source == null || source.Count <= 1)
                return 0;

            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < source.Count; i++)
            {
                float d = (source[i] - p).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            return best;
        }

        static void StampUwpTributaryCarveAlongPolyline(
            float[,] outH,
            GridSystem grid,
            bool[,] mask,
            List<Vector2> sourceLine,
            List<Vector2> carveLine,
            int riverIndex,
            bool useMeshHalfWidths,
            float fallbackHalfW,
            float cellSize,
            int bodyRadius,
            int endpointRadius,
            float floorH,
            float fordFloorDelta,
            float fordMul,
            MapGenConfig config)
        {
            if (outH == null || grid == null || carveLine == null || carveLine.Count < 2)
                return;

            const float spacingCellsDefault = 0.36f;
            int w = grid.Width;
            int h = grid.Height;
            bool frozenCarve = UsesUwpFrozenCarveContract(grid, config);
            bool frozenTribCarve = frozenCarve && riverIndex > 0;
            bool frozenMainCarve = frozenCarve && riverIndex == 0;
            // Contrato Lake First (mismo que Headwater): floor plano + stamps euclídeos bajo mesh.
            bool lakeFirstChannelContract = frozenCarve &&
                config != null &&
                config.uwpLakeFirstHydrologyPipeline;
            // Main frozen: stamps más densos (misma idea que densify 0.28 del carve line).
            float spacingCells = (frozenMainCarve || lakeFirstChannelContract) ? 0.28f : spacingCellsDefault;
            float halfWidthMul = frozenTribCarve
                ? 1f
                : (frozenCarve ? 1f : ResolveUwpCarveHalfWidthMul(config, riverIndex));
            bool requireMask = frozenCarve;

            for (int i = 0; i < carveLine.Count - 1; i++)
            {
                Vector2 a = carveLine[i];
                Vector2 b = carveLine[i + 1];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / spacingCells));
                for (int s = 0; s <= steps; s++)
                {
                    Vector2 p = Vector2.Lerp(a, b, s / (float)Mathf.Max(1, steps));
                    int srcIdx = MapCarvePointToSourceIndex(sourceLine, p);
                    float along = sourceLine.Count <= 1 ? 0.5f : srcIdx / (float)(sourceLine.Count - 1);
                    bool midBody = along > 0.14f && along < 0.86f;
                    bool mouthZone = along >= 0.78f || along <= 0.22f;
                    bool lakeFirstMidCarveBoost = frozenTribCarve &&
                        config.uwpLakeFirstHydrologyPipeline &&
                        UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex) &&
                        along >= 0.50f && along <= 0.76f;

                    float halfW = fallbackHalfW;
                    if (useMeshHalfWidths &&
                        TryGetUwpTributaryCarveHalfWidthWorld(grid, config, riverIndex, srcIdx, out float meshHalfW))
                        halfW = meshHalfW;

                    bool headwaterFeeder = frozenTribCarve && config.uwpLakeFirstHydrologyPipeline &&
                        UwpTributaryOriginUtility.GetOrigin(grid, riverIndex) == UwpTributaryOriginKind.HeadwaterFeeder;

                    float euclidMaxDistWorld = -1f;
                    int radius;
                    if (lakeFirstChannelContract && (useMeshHalfWidths || frozenMainCarve || headwaterFeeder))
                    {
                        // Carve = half de máscara (contrato mesh>carve).
                        // No Max con mesh: anulaba el margen blanco en orillas (foam = mesh∩terreno).
                        // Cabe bajo mesh para no dejar bandeja blanca sin agua (sobre-carve).
                        float visualHalf = halfW;
                        if (!frozenMainCarve &&
                            useMeshHalfWidths &&
                            TryGetUwpTributaryMeshHalfWidthWorld(grid, riverIndex, srcIdx, out float meshHalf))
                        {
                            // Headwater: stamp más cerca del mesh (aún ≤0.98) para cubrir ribbon estrecho.
                            float foamMul = headwaterFeeder ? 0.98f : (1f / Mathf.Max(1.01f, 1.3f));
                            float foamCeil = meshHalf * foamMul;
                            float floorRatio = headwaterFeeder ? 0.95f : 0.98f;
                            visualHalf = Mathf.Min(
                                Mathf.Max(visualHalf, foamCeil * (headwaterFeeder ? 1f : 0.98f)),
                                meshHalf * floorRatio);
                        }

                        euclidMaxDistWorld = visualHalf + cellSize * (headwaterFeeder ? 0.12f : 0.08f);
                        radius = Mathf.Max(1, Mathf.CeilToInt(euclidMaxDistWorld / Mathf.Max(0.01f, cellSize)));
                    }
                    else
                    {
                        radius = useMeshHalfWidths
                            ? HalfWidthWorldToUwpCarveRadiusCells(halfW, cellSize, halfWidthMul)
                            : (mouthZone ? endpointRadius : bodyRadius);
                    }
                    if (!frozenTribCarve && !frozenMainCarve)
                    {
                        if (midBody)
                            radius = Mathf.Max(radius, bodyRadius + 1);
                        if (mouthZone)
                            radius = Mathf.Max(radius, endpointRadius);
                    }

                    if (!frozenTribCarve && !frozenMainCarve)
                    {
                        radius = Mathf.Max(1, Mathf.RoundToInt(radius * SampleUwpCarveLongitudinalRadiusMul(config, riverIndex, srcIdx)));
                    }
                    else
                    {
                        radius = Mathf.Max(1, radius);
                    }

                    int px = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, w - 1);
                    int pz = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, h - 1);
                    bool allowWaterCarve = (!frozenCarve && (mouthZone || TerrainCellNearLakeBody(grid, px, pz, 5))) ||
                        (frozenCarve && config.uwpLakeFirstHydrologyPipeline && riverIndex > 0 &&
                         (mouthZone || TerrainCellNearLakeBody(grid, px, pz, 5)));
                    bool lakeMouthLandCarve = frozenTribCarve && config.uwpLakeFirstHydrologyPipeline &&
                        UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex) &&
                        along <= 0.28f && IsLandCellAdjacentToLakeWater(grid, px, pz);
                    // Headwater / lake-spill→main: bypass mask; límite = maxDistWorld (euclídeo).
                    bool headwaterJoinLandCarve = headwaterFeeder && along >= 0.82f;
                    bool lakeSpillMainJoinCarve = frozenTribCarve &&
                        config.uwpLakeFirstHydrologyPipeline &&
                        UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex) &&
                        along >= 0.76f;
                    float joinMaxDistWorld = (headwaterFeeder || lakeSpillMainJoinCarve) ? euclidMaxDistWorld : -1f;
                    if ((headwaterJoinLandCarve || lakeSpillMainJoinCarve) && joinMaxDistWorld > 0f)
                    {
                        // Cuña Y trib↔main: acompañar mesh con margen foam (no stamp = mesh).
                        float meshHalfJoin = joinMaxDistWorld - cellSize * 0.08f;
                        if (TryGetUwpTributaryMeshHalfWidthWorld(grid, riverIndex, srcIdx, out float mh))
                            meshHalfJoin = mh;
                        float recvHalfWorld = Mathf.Max(
                            0.01f,
                            config.riverVisualRibbonFullWidthCellsMain * 0.5f * cellSize);
                        float foamCeil = meshHalfJoin * (1f / Mathf.Max(1.01f, 1.3f));
                        float joinReach = Mathf.Max(
                            foamCeil + cellSize * 0.10f,
                            Mathf.Max(
                                foamCeil * (lakeSpillMainJoinCarve ? 1.06f : 1.03f),
                                Mathf.Max(recvHalfWorld * 0.28f, cellSize * 1.05f)));
                        float joinCap = foamCeil * (lakeSpillMainJoinCarve ? 1.10f : 1.06f) + cellSize * 0.12f;
                        joinMaxDistWorld = Mathf.Min(joinReach, joinCap);
                        // Nunca superar el overhang de foam del mesh.
                        joinMaxDistWorld = Mathf.Min(joinMaxDistWorld, meshHalfJoin * 0.98f);
                        euclidMaxDistWorld = joinMaxDistWorld;
                        radius = Mathf.Max(1, Mathf.CeilToInt(joinMaxDistWorld / Mathf.Max(0.01f, cellSize)));
                    }

                    bool effectiveRequireMask = requireMask && !lakeMouthLandCarve && !lakeFirstChannelContract;
                    bool forceFullDepth = frozenTribCarve || frozenMainCarve || midBody;
                    bool stampForceFullDepth = forceFullDepth || lakeFirstMidCarveBoost || headwaterFeeder ||
                        frozenMainCarve || lakeSpillMainJoinCarve;
                    float stampFloorH = floorH;
                    if (lakeFirstMidCarveBoost)
                    {
                        float terrainY = Mathf.Max(1f, config.terrainHeightWorld);
                        stampFloorH = Mathf.Max(0f, floorH - 0.5f / terrainY);
                    }

                    // Lake First: bandeja plana bajo el mesh (main / spill / inland / headwater).
                    // NO usar V/U bajo el ribbon del headwater: el lecho parcial deja terreno
                    // alto dentro del mesh → charcos / foam blanco / “el carve se lo come”.
                    // El look de valle en orillas exteriores viene de bankFalloff, no de flatFloor=false.
                    bool flatFloor = lakeFirstChannelContract || frozenTribCarve || frozenMainCarve;
                    float stampFordDelta = headwaterFeeder ? 0f : fordFloorDelta;
                    float stampFordMul = headwaterFeeder ? 1f : fordMul;
                    float stampMaxDistWorld = headwaterFeeder
                        ? joinMaxDistWorld
                        : (lakeSpillMainJoinCarve
                            ? joinMaxDistWorld
                            : (lakeFirstChannelContract ? euclidMaxDistWorld : -1f));

                    StampUniformUwpFloorDisk(
                        outH,
                        grid,
                        mask,
                        new Vector2(p.x, p.y),
                        radius,
                        stampFloorH,
                        stampFordDelta,
                        stampFordMul,
                        config,
                        requireMask: effectiveRequireMask,
                        allowWaterCarve: allowWaterCarve,
                        forceFullDepth: stampForceFullDepth,
                        uniformFlatChannelFloor: flatFloor,
                        maxDistWorld: stampMaxDistWorld);
                    TryApplyUniformUwpFloorAtCell(
                        outH, grid, null, px, pz, stampFloorH, stampFordDelta, allowWaterCarve);
                }
            }
        }

        static void ApplyUwpTributaryLogicalPathCarve(
            float[,] outH,
            GridSystem grid,
            MapGenConfig config,
            bool[,] mask,
            float depth01,
            float fordFloorDelta,
            float fordMul)
        {
            if (grid?.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count < 2 || config == null)
                return;

            float tribFull = config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                ? config.riverVisualRibbonFullWidthCellsTributary
                : config.riverVisualRibbonFullWidthCellsMain * 0.65f;
            float mainFull = Mathf.Max(tribFull, config.riverVisualRibbonFullWidthCellsMain);
            float cellSize = Mathf.Max(0.01f, grid.CellSizeWorld);
            int bodyRadius = Mathf.Max(1, Mathf.CeilToInt(tribFull * 0.24f));
            int endpointRadius = Mathf.Max(bodyRadius + 1, Mathf.CeilToInt(mainFull * 0.22f));

            for (int ri = 0; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
            {
                if (grid.RiverVisualSurfaces != null &&
                    ri < grid.RiverVisualSurfaces.Count &&
                    grid.RiverVisualSurfaces[ri].Skipped)
                    continue;

                List<Vector2> sourceLine = ResolveUwpTributaryCarveCenterline(grid, config, ri);
                if (sourceLine == null || sourceLine.Count < 2)
                    continue;

                bool frozenCarve = UsesUwpFrozenCarveContract(grid, config);
                // Main: densificar como headwater (0.28) para orillas diagonales menos escalonadas.
                float densifyStep = ri == 0 ? 0.28f : 0.38f;
                if (frozenCarve &&
                    config.uwpLakeFirstHydrologyPipeline &&
                    UwpTributaryOriginUtility.GetOrigin(grid, ri) == UwpTributaryOriginKind.HeadwaterFeeder)
                    densifyStep = 0.28f;
                List<Vector2> carveLine = DensifyUwpTributaryCarvePolyline(sourceLine, densifyStep);

                float ribbonFull = ri == 0 ? mainFull : tribFull;
                float fallbackHalfW = ribbonFull * 0.5f * cellSize;
                int bodyRad = ri == 0
                    ? Mathf.Max(1, Mathf.CeilToInt(mainFull * 0.24f))
                    : bodyRadius;
                int endRad = ri == 0
                    ? Mathf.Max(bodyRad + 1, Mathf.CeilToInt(mainFull * 0.22f))
                    : endpointRadius;

                bool useMeshHalfWidths = grid.RiverVisualSurfaces != null &&
                    ri < grid.RiverVisualSurfaces.Count &&
                    !grid.RiverVisualSurfaces[ri].Skipped &&
                    ((frozenCarve &&
                      grid.RiverVisualSurfaces[ri].MaskHalfWidthsWorld != null &&
                      grid.RiverVisualSurfaces[ri].MaskHalfWidthsWorld.Count > 0) ||
                     (grid.RiverVisualSurfaces[ri].HalfWidthsWorld != null &&
                      grid.RiverVisualSurfaces[ri].HalfWidthsWorld.Count > 0));

                float floorH = ResolveUwpRiverCarveFloor01(grid, config, depth01, ri);

                if (!frozenCarve)
                {
                    StampUniformUwpCenterlineFloor(outH, grid, config, mask, carveLine, floorH, fordFloorDelta);
                }

                StampUwpTributaryCarveAlongPolyline(
                    outH,
                    grid,
                    mask,
                    sourceLine,
                    carveLine,
                    ri,
                    useMeshHalfWidths,
                    fallbackHalfW,
                    cellSize,
                    bodyRad,
                    endRad,
                    floorH,
                    fordFloorDelta,
                    fordMul,
                    config);

                if (frozenCarve &&
                    config.uwpLakeFirstHydrologyPipeline &&
                    UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, ri))
                {
                    StampLakeSpillMainJoinWedgeCarve(
                        outH, grid, config, sourceLine, carveLine, ri, floorH, fordFloorDelta, fordMul, cellSize);
                }

                if (!frozenCarve)
                {
                    ApplyUwpTributaryEndpointCarveFlare(
                        outH, grid, config, carveLine, sourceLine, floorH, fordFloorDelta, fordMul, endRad, ri, cellSize);
                }

                RiverSurfaceMeshBuilder.MarkUwpRiverCarveApplied(grid, ri, sourceLine);
            }
        }

        /// <summary>
        /// Cuña spill→main: disco en la boca + offsets laterales cubren la esquina aguda
        /// que los stamps del centerline dejan sin tallar (lado izquierdo típico).
        /// </summary>
        static void StampLakeSpillMainJoinWedgeCarve(
            float[,] outH,
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> sourceLine,
            List<Vector2> carveLine,
            int riverIndex,
            float floorH,
            float fordFloorDelta,
            float fordMul,
            float cellSize)
        {
            if (outH == null || grid == null || config == null || carveLine == null || carveLine.Count < 3 || riverIndex <= 0)
                return;

            // Lake-spill: el extremo hacia el main suele ser el final de la polyline densificada.
            int joinEp = carveLine.Count - 1;
            if (grid.RiverCenterlinesCellSpace != null &&
                grid.RiverCenterlinesCellSpace.Count > 0 &&
                grid.RiverCenterlinesCellSpace[0] != null &&
                grid.RiverCenterlinesCellSpace[0].Count >= 2)
            {
                var mainLine = grid.RiverCenterlinesCellSpace[0];
                float dStart = DistanceSqPointToPolylineCellSpace(carveLine[0], mainLine);
                float dEnd = DistanceSqPointToPolylineCellSpace(carveLine[carveLine.Count - 1], mainLine);
                joinEp = dStart <= dEnd ? 0 : carveLine.Count - 1;
            }

            joinEp = Mathf.Clamp(joinEp, 0, carveLine.Count - 1);
            bool joinAtStart = joinEp == 0;
            Vector2 mouth = carveLine[joinEp];
            int prev = joinAtStart
                ? Mathf.Min(1, carveLine.Count - 1)
                : Mathf.Max(0, carveLine.Count - 2);
            Vector2 approach = mouth - carveLine[prev];
            if (approach.sqrMagnitude < 1e-8f)
                return;
            approach.Normalize();
            Vector2 perp = new Vector2(-approach.y, approach.x);

            float meshHalf = cellSize * 1.2f;
            int srcIdx = MapCarvePointToSourceIndex(sourceLine ?? carveLine, mouth);
            if (TryGetUwpTributaryMeshHalfWidthWorld(grid, riverIndex, srcIdx, out float mh))
                meshHalf = Mathf.Max(meshHalf, mh);
            else if (TryGetUwpTributaryCarveHalfWidthWorld(grid, config, riverIndex, srcIdx, out float ch))
                meshHalf = Mathf.Max(meshHalf, ch);
            float mainHalf = Mathf.Max(0.01f, config.riverVisualRibbonFullWidthCellsMain * 0.5f * cellSize);

            float hubWorld = Mathf.Max(meshHalf * 1.12f, mainHalf * 0.42f) + cellSize * 0.18f;
            int hubRadius = Mathf.Max(2, Mathf.CeilToInt(hubWorld / Mathf.Max(0.01f, cellSize)));
            StampUniformUwpFloorDisk(
                outH, grid, null, mouth, hubRadius, floorH, fordFloorDelta, fordMul, config,
                requireMask: false, allowWaterCarve: true, forceFullDepth: true,
                uniformFlatChannelFloor: true, maxDistWorld: hubWorld);

            float sideOff = meshHalf * 0.55f + cellSize * 0.18f;
            float sideWorld = Mathf.Max(meshHalf * 1.05f, mainHalf * 0.38f) + cellSize * 0.16f;
            int sideRadius = Mathf.Max(2, Mathf.CeilToInt(sideWorld / Mathf.Max(0.01f, cellSize)));
            Vector2 behind = mouth - approach * (meshHalf * 0.22f + cellSize * 0.10f);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector2 side = behind + perp * (sideOff * s);
                StampUniformUwpFloorDisk(
                    outH, grid, null, side, sideRadius, floorH, fordFloorDelta, fordMul, config,
                    requireMask: false, allowWaterCarve: true, forceFullDepth: true,
                    uniformFlatChannelFloor: true, maxDistWorld: sideWorld);
            }

            // Un paso corto hacia el main: cubre esquina Y sin bandeja fuera del mesh.
            Vector2 intoMain = mouth + approach * (mainHalf * 0.18f + cellSize * 0.10f);
            float intoWorld = Mathf.Max(meshHalf * 1.05f, mainHalf * 0.32f);
            int intoRadius = Mathf.Max(2, Mathf.CeilToInt(intoWorld / Mathf.Max(0.01f, cellSize)));
            StampUniformUwpFloorDisk(
                outH, grid, null, intoMain, intoRadius, floorH, fordFloorDelta, fordMul, config,
                requireMask: false, allowWaterCarve: true, forceFullDepth: true,
                uniformFlatChannelFloor: true, maxDistWorld: intoWorld);

            Vector2 ahead = mouth + approach * (mainHalf * 0.12f);
            float wingWorld = Mathf.Max(meshHalf * 0.92f, mainHalf * 0.28f);
            int wingRadius = Mathf.Max(2, Mathf.CeilToInt(wingWorld / Mathf.Max(0.01f, cellSize)));
            for (int s = -1; s <= 1; s += 2)
            {
                Vector2 wing = ahead + perp * (sideOff * 0.70f * s);
                StampUniformUwpFloorDisk(
                    outH, grid, null, wing, wingRadius, floorH, fordFloorDelta, fordMul, config,
                    requireMask: false, allowWaterCarve: true, forceFullDepth: true,
                    uniformFlatChannelFloor: true, maxDistWorld: wingWorld);
            }
        }

        static float DistanceSqPointToPolylineCellSpace(Vector2 p, List<Vector2> line)
        {
            if (line == null || line.Count == 0)
                return float.MaxValue;
            if (line.Count == 1)
                return (p - line[0]).sqrMagnitude;
            float best = float.MaxValue;
            for (int i = 0; i < line.Count - 1; i++)
            {
                Vector2 q = ClosestPointOnOpenSegment2D(p, line[i], line[i + 1]);
                float d = (p - q).sqrMagnitude;
                if (d < best)
                    best = d;
            }
            return best;
        }

        static Vector2 ClosestPointOnOpenSegment2D(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 1e-12f)
                return a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return a + ab * t;
        }

        /// <summary>Carve de respaldo: terreno entre extremo del tributario y orilla del lago (sin depender de máscara).</summary>
        static void ApplyUwpTributaryLakeMouthFinalCarveReach(
            float[,] outH,
            GridSystem grid,
            MapGenConfig config,
            bool[,] mask,
            float depth01,
            float fordFloorDelta,
            float fordMul)
        {
            // Frozen: reach completo desactivado salvo bordes tierra-lago en lake-first.
            if (UsesUwpFrozenCarveContract(grid, config) && !config.uwpLakeFirstHydrologyPipeline)
                return;

            if (grid?.RiverVisualSurfaces == null || config == null)
                return;

            float maxDist = Mathf.Max(8f, config.lakeRiverConnectorMaxDistanceCells);
            if (config.uwpOwnedVisualPolicy)
                maxDist = Mathf.Min(Mathf.Max(maxDist, config.lakeRiverMouthBlendCells + 12f), 96f);
            float tribFull = config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                ? config.riverVisualRibbonFullWidthCellsTributary
                : config.riverVisualRibbonFullWidthCellsMain * 0.65f;
            float carveRadiusMul = config.uwpLakeFirstHydrologyPipeline ? LakeFirstTributaryCarveWidthMul : 1f;
            int radius = Mathf.Max(2, Mathf.CeilToInt(tribFull * 0.24f * carveRadiusMul));
            int w = grid.Width;
            int h = grid.Height;
            int reachCount = 0;

            for (int ri = 1; ri < grid.RiverVisualSurfaces.Count; ri++)
            {
                if (!UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, ri))
                    continue;

                var surface = grid.RiverVisualSurfaces[ri];
                if (surface.Skipped || surface.FinalCenterlineCells == null || surface.FinalCenterlineCells.Count < 2)
                    continue;

                var line = surface.FinalCenterlineCells;
                float floorH = ResolveUwpRiverCarveFloor01(grid, config, depth01, ri);
                bool frozen = UsesUwpFrozenCarveContract(grid, config);
                bool lakeFirst = config.uwpLakeFirstHydrologyPipeline;
                reachCount += StampTributaryLakeMouthLandEdgeAlongCenterline(
                    outH, grid, config, mask, line, radius, floorH, fordFloorDelta, fordMul, w, h);
                if (!UsesUwpFrozenCarveContract(grid, config))
                {
                    reachCount += StampOwnedTributaryLakeProjectionCarve(
                        outH, grid, config, mask, line, ri, maxDist, radius, floorH, fordFloorDelta, fordMul, w, h);
                }

                if (!frozen || !lakeFirst)
                {
                    bool needsReach =
                        EndpointNeedsLakeMouthCarveReach(grid, line, 0, maxDist, w, h) ||
                        EndpointNeedsLakeMouthCarveReach(grid, line, line.Count - 1, maxDist, w, h);
                    if (!needsReach)
                        continue;

                    reachCount += StampLakeMouthCarveReachForEndpoint(
                        outH, grid, config, mask, line, 0, maxDist, radius, floorH, fordFloorDelta, fordMul, w, h);
                    reachCount += StampLakeMouthCarveReachForEndpoint(
                        outH, grid, config, mask, line, line.Count - 1,
                        maxDist, radius, floorH, fordFloorDelta, fordMul, w, h);
                }
                else
                {
                    int lakeMouthIdx = ResolveTributaryLakeMouthEndpointIndex(grid, ri, line);
                    if (lakeMouthIdx >= 0 &&
                        EndpointNeedsLakeMouthCarveReach(grid, line, lakeMouthIdx, maxDist, w, h))
                    {
                        reachCount += StampLakeMouthCarveReachForEndpoint(
                            outH, grid, config, mask, line, lakeMouthIdx,
                            maxDist, radius, floorH, fordFloorDelta, fordMul, w, h);
                    }
                }
            }

            if (reachCount > 0 && (config.debugLogs || config.debugHydrologyNetwork || config.uwpOwnedVisualPolicy))
            {
                Debug.Log(
                    $"[TributaryLakeMouthCarveReach] stamped={reachCount} maxDist={maxDist:F1} seed={config.seed}");
            }
        }

        static bool IsLandCellAdjacentToLakeWater(GridSystem grid, int cx, int cz)
        {
            if (grid == null || !grid.InBoundsCell(cx, cz))
                return false;
            var cellType = grid.GetCell(cx, cz).type;
            if (cellType != CellType.Land && cellType != CellType.River)
                return false;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    int nx = cx + dx;
                    int nz = cz + dz;
                    if (!grid.InBoundsCell(nx, nz))
                        continue;
                    if (grid.GetCell(nx, nz).type == CellType.Water)
                        return true;
                }
            }

            return false;
        }

        static bool EndpointNeedsLakeMouthCarveReach(
            GridSystem grid,
            List<Vector2> line,
            int endpointIndex,
            float maxDistCells,
            int w,
            int h)
        {
            if (line == null || endpointIndex < 0 || endpointIndex >= line.Count)
                return false;
            Vector2 end = line[endpointIndex];
            int ex = Mathf.Clamp(Mathf.FloorToInt(end.x), 0, w - 1);
            int ez = Mathf.Clamp(Mathf.FloorToInt(end.y), 0, h - 1);
            if (grid.GetCell(ex, ez).type == CellType.Water)
                return false;
            if (IsLandCellAdjacentToLakeWater(grid, ex, ez))
                return true;
            return TryFindNearestLakeShoreForCarve(grid, end, maxDistCells, out Vector2 shore) &&
                   Vector2.Distance(end, shore) > 0.2f;
        }

        static int StampTributaryLakeMouthLandEdgeAlongCenterline(
            float[,] outH,
            GridSystem grid,
            MapGenConfig config,
            bool[,] mask,
            List<Vector2> line,
            int radiusCells,
            float floorH,
            float fordFloorDelta,
            float fordMul,
            int w,
            int h)
        {
            if (line == null || line.Count < 2)
                return 0;

            int count = 0;
            bool frozenLakeFirst = config != null && config.uwpLakeFirstHydrologyPipeline &&
                                   UsesUwpFrozenCarveContract(grid, config);
            for (int i = 0; i < line.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(line[i].x), 0, w - 1);
                int cz = Mathf.Clamp(Mathf.FloorToInt(line[i].y), 0, h - 1);
                if (!IsLandCellAdjacentToLakeWater(grid, cx, cz))
                    continue;

                StampUniformUwpFloorDisk(
                    outH,
                    grid,
                    mask,
                    line[i],
                    radiusCells,
                    floorH,
                    fordFloorDelta,
                    fordMul,
                    config,
                    requireMask: !frozenLakeFirst,
                    allowWaterCarve: false,
                    forceFullDepth: true,
                    uniformFlatChannelFloor: true);
                TryApplyUniformUwpFloorAtCell(outH, grid, null, cx, cz, floorH, fordFloorDelta, false);
                count++;
            }

            return count;
        }

        static int StampOwnedTributaryLakeProjectionCarve(
            float[,] outH,
            GridSystem grid,
            MapGenConfig config,
            bool[,] mask,
            List<Vector2> line,
            int riverIndex,
            float maxDistCells,
            int radiusCells,
            float floorH,
            float fordFloorDelta,
            float fordMul,
            int w,
            int h)
        {
            if (grid == null || config == null || line == null || line.Count < 2)
                return 0;
            if (!config.uwpOwnedVisualPolicy || !IsLakeOwnedTributaryIndex(grid, riverIndex))
                return 0;

            int count = 0;
            int projectionRadius = Mathf.Max(radiusCells, Mathf.CeilToInt(radiusCells * 1.05f));
            int span = Mathf.Clamp(config.lakeRiverMouthBlendCells, 3, 7);

            int StampProjectionEndpoint(int endpointIndex)
            {
                Vector2 end = line[endpointIndex];
                int ex = Mathf.Clamp(Mathf.FloorToInt(end.x), 0, w - 1);
                int ez = Mathf.Clamp(Mathf.FloorToInt(end.y), 0, h - 1);
                bool endpointAtLake =
                    grid.GetCell(ex, ez).type == CellType.Water ||
                    TerrainCellNearLakeBody(grid, ex, ez, 3) ||
                    IsLandCellAdjacentToLakeWater(grid, ex, ez) ||
                    TryFindNearestLakeShoreForCarve(grid, end, maxDistCells, out _);
                if (!endpointAtLake)
                    return 0;

                int dir = endpointIndex == 0 ? 1 : -1;
                int stamped = 0;
                int limit = Mathf.Min(span, line.Count - 1);
                for (int k = 0; k <= limit; k++)
                {
                    int idx = endpointIndex + dir * k;
                    if (idx < 0 || idx >= line.Count)
                        break;

                    float fade = 1f - k / Mathf.Max(1f, limit);
                    int r = Mathf.Max(radiusCells, Mathf.RoundToInt(Mathf.Lerp(radiusCells, projectionRadius, fade)));
                    Vector2 p = line[idx];
                    StampUniformUwpFloorDisk(
                        outH,
                        grid,
                        mask,
                        p,
                        r,
                        floorH,
                        fordFloorDelta,
                        fordMul,
                        config,
                        requireMask: false,
                        allowWaterCarve: true,
                        forceFullDepth: true,
                        uniformFlatChannelFloor: true);

                    int px = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, w - 1);
                    int pz = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, h - 1);
                    TryApplyUniformUwpFloorAtCell(outH, grid, null, px, pz, floorH, fordFloorDelta, true);
                    stamped++;
                }

                if (TryFindNearestLakeShoreForCarve(grid, end, maxDistCells, out Vector2 shore))
                {
                    float bridgeDist = Vector2.Distance(end, shore);
                    if (bridgeDist > 0.05f)
                    {
                        int steps = Mathf.Clamp(Mathf.CeilToInt(bridgeDist / 0.45f), 2, 10);
                        for (int s = 1; s <= steps; s++)
                        {
                            Vector2 p = Vector2.Lerp(end, shore, s / (float)steps);
                            StampUniformUwpFloorDisk(
                                outH,
                                grid,
                                mask,
                                p,
                                projectionRadius,
                                floorH,
                                fordFloorDelta,
                                fordMul,
                                config,
                                requireMask: false,
                                allowWaterCarve: true,
                                forceFullDepth: true,
                                uniformFlatChannelFloor: true);

                            int px = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, w - 1);
                            int pz = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, h - 1);
                            TryApplyUniformUwpFloorAtCell(outH, grid, null, px, pz, floorH, fordFloorDelta, true);
                            stamped++;
                        }
                    }
                }

                return stamped;
            }

            count += StampProjectionEndpoint(0);
            count += StampProjectionEndpoint(line.Count - 1);

            if (count > 0 && (config.debugLogs || config.debugHydrologyNetwork || config.uwpOwnedVisualPolicy))
            {
                Debug.Log(
                    $"[OwnedTributaryLakeProjectionCarve] riverIndex={riverIndex} stamped={count} " +
                    $"radius={projectionRadius} span={span}");
            }

            return count;
        }

        static bool IsLakeOwnedTributaryIndex(GridSystem grid, int riverIndex)
        {
            if (grid?.LakeComponentTributaryOwnerRiverIndex == null || riverIndex <= 0)
                return false;
            for (int i = 0; i < grid.LakeComponentTributaryOwnerRiverIndex.Count; i++)
            {
                if (grid.LakeComponentTributaryOwnerRiverIndex[i] == riverIndex)
                    return true;
            }

            return false;
        }

        static int ResolveTributaryLakeMouthEndpointIndex(GridSystem grid, int riverIndex, List<Vector2> line)
        {
            if (line == null || line.Count < 2)
                return -1;
            if (IsLakeOwnedTributaryIndex(grid, riverIndex))
                return 0;

            int last = line.Count - 1;
            if (TerrainCellNearLakeBody(grid, Mathf.FloorToInt(line[last].x), Mathf.FloorToInt(line[last].y), 4))
                return last;
            return 0;
        }

        static int StampLakeMouthCarveReachForEndpoint(
            float[,] outH,
            GridSystem grid,
            MapGenConfig config,
            bool[,] mask,
            List<Vector2> line,
            int endpointIndex,
            float maxDistCells,
            int radiusCells,
            float floorH,
            float fordFloorDelta,
            float fordMul,
            int w,
            int h)
        {
            if (line == null || endpointIndex < 0 || endpointIndex >= line.Count)
                return 0;

            Vector2 end = line[endpointIndex];
            int ex = Mathf.Clamp(Mathf.FloorToInt(end.x), 0, w - 1);
            int ez = Mathf.Clamp(Mathf.FloorToInt(end.y), 0, h - 1);
            ref var endCell = ref grid.GetCell(ex, ez);
            if (endCell.type == CellType.Water)
                return 0;

            bool landAtLakeEdge = IsLandCellAdjacentToLakeWater(grid, ex, ez);
            if (!TryFindNearestLakeShoreForCarve(grid, end, maxDistCells, out Vector2 shore))
            {
                if (!landAtLakeEdge)
                    return 0;

                StampUniformUwpFloorDisk(
                    outH,
                    grid,
                    mask,
                    end,
                    radiusCells,
                    floorH,
                    fordFloorDelta,
                    fordMul,
                    config,
                    requireMask: false,
                    allowWaterCarve: false,
                    forceFullDepth: true,
                    uniformFlatChannelFloor: true);
                return 1;
            }

            float bridgeDist = Vector2.Distance(end, shore);
            if (bridgeDist < 0.2f)
            {
                if (!landAtLakeEdge)
                    return 0;

                StampUniformUwpFloorDisk(
                    outH,
                    grid,
                    mask,
                    end,
                    radiusCells,
                    floorH,
                    fordFloorDelta,
                    fordMul,
                    config,
                    requireMask: false,
                    allowWaterCarve: false,
                    forceFullDepth: true,
                    uniformFlatChannelFloor: true);
                return 1;
            }

            int steps = Mathf.Clamp(Mathf.CeilToInt(bridgeDist / 0.36f), 2, 24);
            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                Vector2 p = Vector2.Lerp(end, shore, t);
                StampUniformUwpFloorDisk(
                    outH,
                    grid,
                    mask,
                    p,
                    radiusCells,
                    floorH,
                    fordFloorDelta,
                    fordMul,
                    config,
                    requireMask: false,
                    allowWaterCarve: false,
                    forceFullDepth: true,
                    uniformFlatChannelFloor: true);
            }

            return steps;
        }

        static bool TryFindNearestLakeShoreForCarve(
            GridSystem grid,
            Vector2 from,
            float maxDistCells,
            out Vector2 shore)
        {
            shore = default;
            if (grid?.LakeBodyCellsPacked == null || grid.LakeBodyCellsPacked.Count == 0)
                return false;

            const float minSepCells = 0.35f;
            float minSepSq = minSepCells * minSepCells;
            float maxSq = maxDistCells * maxDistCells;
            float bestSq = float.MaxValue;

            if (grid.LakeMouthCellsPacked != null)
            {
                foreach (long pk in grid.LakeMouthCellsPacked)
                {
                    int x = (int)(pk >> 32);
                    int z = (int)(uint)pk;
                    var center = new Vector2(x + 0.5f, z + 0.5f);
                    float sq = (center - from).sqrMagnitude;
                    if (sq < minSepSq || sq >= bestSq)
                        continue;
                    bestSq = sq;
                    shore = center;
                }
            }

            foreach (long pk in grid.LakeBodyCellsPacked)
            {
                int x = (int)(pk >> 32);
                int z = (int)(uint)pk;
                if (grid.GetCell(x, z).type != CellType.Water)
                    continue;
                var center = new Vector2(x + 0.5f, z + 0.5f);
                float sq = (center - from).sqrMagnitude;
                if (sq < minSepSq || sq >= bestSq)
                    continue;
                bestSq = sq;
                shore = center;
            }

            return bestSq <= maxSq;
        }

        public static List<Vector2> ResolveUwpTributaryCarveCenterline(GridSystem grid, MapGenConfig config, int riverIndex)
        {
            if (grid?.RiverVisualSurfaces != null &&
                riverIndex >= 0 && riverIndex < grid.RiverVisualSurfaces.Count)
            {
                var surface = grid.RiverVisualSurfaces[riverIndex];
                if (surface.Skipped)
                    return null;
                if (surface.FinalCenterlineCells != null && surface.FinalCenterlineCells.Count >= 2)
                    return surface.FinalCenterlineCells;
                return null;
            }

            if (config != null && config.uwpOwnedVisualPolicy)
                return null;

            if (grid?.RiverCenterlinesCellSpace == null ||
                riverIndex < 0 || riverIndex >= grid.RiverCenterlinesCellSpace.Count)
                return null;

            var raw = grid.RiverCenterlinesCellSpace[riverIndex];
            if (raw == null || raw.Count < 2)
                return null;
            var snapped = RiverSurfaceMeshBuilder.BuildSnappedCellCenterPolyline(raw);
            return snapped != null && snapped.Count >= 2 ? snapped : raw;
        }

        static void ApplyUwpTributaryEndpointCarveFlare(
            float[,] outH,
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> line,
            List<Vector2> sourceLine,
            float floorH,
            float fordFloorDelta,
            float fordMul,
            int endpointRadius,
            int riverIndex,
            float cellSizeWorld)
        {
            if (grid == null || outH == null || line == null || line.Count < 2 || config == null)
                return;

            if (sourceLine == null || sourceLine.Count < 2)
                sourceLine = line;

            float mainFull = config.riverVisualRibbonFullWidthCellsMain;
            int confluenceRadius = Mathf.Max(endpointRadius + 1, Mathf.CeilToInt(mainFull * 0.28f));
            int lakeRadius = Mathf.Max(endpointRadius, Mathf.CeilToInt(mainFull * 0.20f));
            int w = grid.Width;
            int h = grid.Height;

            void FlareEnd(bool atStart)
            {
                int endIdx = atStart ? 0 : line.Count - 1;
                int cx = Mathf.Clamp(Mathf.RoundToInt(line[endIdx].x), 0, w - 1);
                int cz = Mathf.Clamp(Mathf.RoundToInt(line[endIdx].y), 0, h - 1);
                ref var endCell = ref grid.GetCell(cx, cz);
                bool nearMain = endCell.type == CellType.River;
                if (!nearMain && !atStart)
                {
                    for (int k = 1; k <= 3 && endIdx - k >= 0; k++)
                    {
                        int px = Mathf.Clamp(Mathf.RoundToInt(line[endIdx - k].x), 0, w - 1);
                        int pz = Mathf.Clamp(Mathf.RoundToInt(line[endIdx - k].y), 0, h - 1);
                        if (grid.GetCell(px, pz).type == CellType.River)
                        {
                            nearMain = true;
                            break;
                        }
                    }
                }

                bool nearLake = endCell.type == CellType.Water || TerrainCellNearLakeBody(grid, cx, cz, 4);
                if (!nearLake)
                {
                    int probe = atStart ? 1 : endIdx - 1;
                    if (probe >= 0 && probe < line.Count)
                    {
                        int px = Mathf.Clamp(Mathf.RoundToInt(line[probe].x), 0, w - 1);
                        int pz = Mathf.Clamp(Mathf.RoundToInt(line[probe].y), 0, h - 1);
                        nearLake = grid.GetCell(px, pz).type == CellType.Water ||
                                   TerrainCellNearLakeBody(grid, px, pz, 5);
                    }
                }

                if (!nearMain && !nearLake)
                    return;

                int radius = nearMain ? confluenceRadius : lakeRadius;
                int blend = Mathf.Clamp(config.riverSurfaceTributaryConfluenceApproachCells, 6, 18);
                float tribFull = config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                    ? config.riverVisualRibbonFullWidthCellsTributary
                    : config.riverVisualRibbonFullWidthCellsMain * 0.52f;
                int nearMainCapRadius = Mathf.Max(endpointRadius, Mathf.CeilToInt(tribFull * 0.30f));
                int i0 = atStart ? 0 : Mathf.Max(0, line.Count - blend);
                int i1 = atStart ? Mathf.Min(line.Count - 1, blend - 1) : line.Count - 1;
                for (int i = i0; i <= i1; i++)
                {
                    float t = i1 <= i0 ? 1f : (i - i0) / (float)(i1 - i0);
                    int r = Mathf.Max(bodyRadiusFromRadius(radius, t), endpointRadius);
                    int srcIdx = MapCarvePointToSourceIndex(sourceLine, line[i]);
                    if (TryGetUwpTributaryCarveHalfWidthWorld(grid, config, riverIndex, srcIdx, out float meshHalfW))
                        r = Mathf.Max(r, HalfWidthWorldToUwpCarveRadiusCells(
                            meshHalfW, cellSizeWorld,
                            UsesUwpFrozenCarveContract(grid, config) ? 1f : ResolveUwpCarveHalfWidthMul(config, riverIndex)));
                    if (nearMain)
                        r = Mathf.Min(r, nearMainCapRadius);

                    int px = Mathf.Clamp(Mathf.RoundToInt(line[i].x), 0, w - 1);
                    int pz = Mathf.Clamp(Mathf.RoundToInt(line[i].y), 0, h - 1);
                    bool allowWaterCarve = nearLake || TerrainCellNearLakeBody(grid, px, pz, 5);
                    StampUniformUwpFloorDisk(
                        outH, grid, null, new Vector2(px + 0.5f, pz + 0.5f),
                        r, floorH, fordFloorDelta, fordMul, config, requireMask: false, allowWaterCarve: allowWaterCarve);
                    TryApplyUniformUwpFloorAtCell(
                        outH, grid, null, px, pz, floorH, fordFloorDelta, allowWaterCarve);
                }
            }

            FlareEnd(true);
            FlareEnd(false);
        }

        static int bodyRadiusFromRadius(int endpointRadius, float t) =>
            Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(endpointRadius * 0.55f, endpointRadius, Mathf.SmoothStep(0f, 1f, t))));

        static bool TerrainCellNearLakeBody(GridSystem grid, int x, int z, int radius)
        {
            if (grid?.LakeBodyCellsPacked == null || grid.LakeBodyCellsPacked.Count == 0)
                return false;
            long pk = ((long)x << 32) | (uint)z;
            if (grid.LakeBodyCellsPacked.Contains(pk))
                return true;
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > radius)
                        continue;
                    long nk = ((long)(x + dx) << 32) | (uint)(z + dz);
                    if (grid.LakeBodyCellsPacked.Contains(nk))
                        return true;
                }
            }

            return false;
        }

        static void ApplyUwpRiverCenterlinesFloorCarve(
            float[,] outH,
            GridSystem grid,
            MapGenConfig config,
            bool[,] mask,
            float floorH,
            float fordFloorDelta)
        {
            if (grid == null || outH == null)
                return;

            if (grid.RiverVisualSurfaces != null && grid.RiverVisualSurfaces.Count > 0)
            {
                for (int ri = 0; ri < grid.RiverVisualSurfaces.Count; ri++)
                {
                    var surface = grid.RiverVisualSurfaces[ri];
                    if (surface.Skipped || surface.FinalCenterlineCells == null || surface.FinalCenterlineCells.Count < 1)
                        continue;
                    StampUniformUwpCenterlineFloor(
                        outH, grid, config, mask, surface.FinalCenterlineCells, floorH, fordFloorDelta);
                }
            }
            else if (grid.RiverCenterlinesCellSpace != null &&
                     (config == null || !config.uwpOwnedVisualPolicy))
            {
                for (int ri = 0; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
                {
                    var line = grid.RiverCenterlinesCellSpace[ri];
                    if (line == null || line.Count < 1)
                        continue;
                    StampUniformUwpCenterlineFloor(outH, grid, config, mask, line, floorH, fordFloorDelta);
                }
            }
        }

        static int[,] BuildRiverMaskInwardDistanceGrid(bool[,] mask, int w, int h, int inset)
        {
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
                    if (!IsRiverVisualMaskCoreCell(mask, w, h, x, z, inset))
                        continue;
                    dist[x, z] = 0;
                    qx.Enqueue(x);
                    qz.Enqueue(z);
                }
            }

            while (qx.Count > 0)
            {
                int x = qx.Dequeue();
                int z = qz.Dequeue();
                int d = dist[x, z];
                void Try(int nx, int nz)
                {
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                        return;
                    if (!mask[nx, nz] || dist[nx, nz] != -1)
                        return;
                    dist[nx, nz] = d + 1;
                    qx.Enqueue(nx);
                    qz.Enqueue(nz);
                }

                Try(x - 1, z);
                Try(x + 1, z);
                Try(x, z - 1);
                Try(x, z + 1);
            }

            return dist;
        }

        static void ApplyUniformUwpOrganicBankSoften(
            float[,] outH,
            GridSystem grid,
            bool[,] mask,
            MapGenConfig config,
            float floorH,
            float fordFloorDelta,
            float fordMul,
            int inset)
        {
            if (grid == null || outH == null || mask == null || config == null)
                return;
            int w = grid.Width;
            int h = grid.Height;
            int bankCells = 2;
            int[,] inward = BuildRiverMaskInwardDistanceGrid(mask, w, h, inset);
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (!mask[x, z])
                        continue;
                    int d = inward[x, z];
                    if (d < 0 || d > bankCells)
                        continue;
                    ref var cell = ref grid.GetCell(x, z);
                    if (cell.type == CellType.Water && !mask[x, z])
                        continue;
                    float coreTarget = cell.riverFord ? floorH - fordFloorDelta * fordMul : floorH;
                    if (d <= 0)
                    {
                        outH[x, z] = coreTarget;
                        continue;
                    }

                    float t = Mathf.Clamp01(d / (float)bankCells);
                    float eased = t * t * (3f - 2f * t);
                    float natural = cell.height01;
                    float target = Mathf.Lerp(coreTarget, natural, eased);
                    outH[x, z] = Mathf.Min(outH[x, z], target);
                }
            }
        }

        static void StampUniformUwpFloorDisk(
            float[,] outH,
            GridSystem grid,
            bool[,] mask,
            Vector2 centerCell,
            int radiusCells,
            float floorH,
            float fordFloorDelta,
            float fordMul,
            MapGenConfig config,
            bool requireMask = true,
            bool allowWaterCarve = false,
            bool forceFullDepth = false,
            bool uniformFlatChannelFloor = false,
            float maxDistWorld = -1f)
        {
            if (grid == null || outH == null || radiusCells < 1)
                return;
            int w = grid.Width;
            int h = grid.Height;
            float cx = centerCell.x;
            float cz = centerCell.y;
            int sx = Mathf.Clamp(Mathf.FloorToInt(cx), 0, w - 1);
            int sz = Mathf.Clamp(Mathf.FloorToInt(cz), 0, h - 1);
            bool useBell = config != null && config.uwpCarveEuclideanBellProfileEnabled;
            float flatRatio = uniformFlatChannelFloor
                ? 1f
                : (config != null ? config.uwpCarveTransverseFlatRatio : 0.38f);
            float bankPower = config != null ? config.uwpCarveTransverseBankPower : 1.8f;
            float maxDist = Mathf.Max(1f, radiusCells);
            bool frozenCarve = UsesUwpFrozenCarveContract(grid, config);
            float cellSize = Mathf.Max(0.01f, grid.CellSizeWorld);
            bool limitWorld = maxDistWorld > 1e-5f;

            for (int dz = -radiusCells; dz <= radiusCells; dz++)
            {
                for (int dx = -radiusCells; dx <= radiusCells; dx++)
                {
                    int nx = sx + dx;
                    int nz = sz + dz;
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                        continue;

                    float dist = useBell
                        ? Mathf.Sqrt((nx + 0.5f - cx) * (nx + 0.5f - cx) + (nz + 0.5f - cz) * (nz + 0.5f - cz))
                        : Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
                    if (limitWorld)
                    {
                        // Distancia euclídea en mundo: carve no supera half-width del mesh.
                        float distWorld = Mathf.Sqrt(
                            (nx + 0.5f - cx) * (nx + 0.5f - cx) +
                            (nz + 0.5f - cz) * (nz + 0.5f - cz)) * cellSize;
                        if (distWorld > maxDistWorld + 1e-4f)
                            continue;
                    }
                    else if (useBell)
                    {
                        if (dist > maxDist + 0.001f)
                            continue;
                    }
                    else if (dist > radiusCells)
                    {
                        continue;
                    }

                    if (requireMask && mask != null && !mask[nx, nz])
                        continue;
                    ref var cell = ref grid.GetCell(nx, nz);
                    if (cell.type == CellType.Water && !allowWaterCarve && (mask == null || !mask[nx, nz]))
                        continue;

                    float coreTarget = cell.riverFord ? floorH - fordFloorDelta * fordMul : floorH;
                    float natural = cell.height01;
                    if (allowWaterCarve && cell.type == CellType.Water)
                        natural = coreTarget;
                    if (frozenCarve && requireMask)
                        natural = Mathf.Min(natural, coreTarget);

                    float target;
                    if (useBell)
                    {
                        // Con maxDistWorld (main frozen): normalizar a half-width visual, no a radiusCells.
                        float distWorldBell = Mathf.Sqrt(
                            (nx + 0.5f - cx) * (nx + 0.5f - cx) +
                            (nz + 0.5f - cz) * (nz + 0.5f - cz)) * cellSize;
                        float normT = limitWorld
                            ? distWorldBell / Mathf.Max(1e-4f, maxDistWorld)
                            : dist / maxDist;
                        float profile = ComputeUwpCarveBellProfile01(normT, flatRatio, bankPower);
                        if ((forceFullDepth || uniformFlatChannelFloor) && normT <= flatRatio)
                            profile = 1f;
                        if (frozenCarve && (forceFullDepth || uniformFlatChannelFloor) && normT <= flatRatio)
                            target = coreTarget;
                        else if (uniformFlatChannelFloor)
                            target = coreTarget;
                        else
                            target = Mathf.Lerp(natural, coreTarget, profile);
                    }
                    else
                    {
                        float edge = 1f - Mathf.Clamp01((dist - 0.25f) / maxDist);
                        float edgeEased = edge * edge * (3f - 2f * edge);
                        if (forceFullDepth || (allowWaterCarve && cell.type == CellType.Water))
                            natural = coreTarget;
                        target = forceFullDepth
                            ? coreTarget
                            : Mathf.Lerp(coreTarget, natural, 1f - edgeEased);
                    }

                    outH[nx, nz] = Mathf.Min(outH[nx, nz], target);
                }
            }
        }

        static void ApplyUwpMapBorderRiverFloorReach(
            float[,] outH,
            GridSystem grid,
            MapGenConfig config,
            bool[,] mask,
            float floorH,
            float fordFloorDelta,
            float fordMul)
        {
            if (grid?.RiverVisualSurfaces == null || config == null || outH == null)
                return;
            int w = grid.Width;
            int h = grid.Height;
            int reachCells = 10;
            for (int ri = 0; ri < grid.RiverVisualSurfaces.Count; ri++)
            {
                var surface = grid.RiverVisualSurfaces[ri];
                if (surface.Skipped || surface.FinalCenterlineCells == null || surface.FinalCenterlineCells.Count < 2)
                    continue;

                float fullCells = ri == 0
                    ? config.riverVisualRibbonFullWidthCellsMain
                    : (config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                        ? config.riverVisualRibbonFullWidthCellsTributary
                        : config.riverVisualRibbonFullWidthCellsMain);
                int radiusCells = Mathf.Max(1, Mathf.CeilToInt(fullCells * 0.22f));
                var line = surface.FinalCenterlineCells;

                void ProcessEnd(bool isStart)
                {
                    int ep = isStart ? 0 : line.Count - 1;
                    int cx = Mathf.Clamp(Mathf.RoundToInt(line[ep].x), 0, w - 1);
                    int cz = Mathf.Clamp(Mathf.RoundToInt(line[ep].y), 0, h - 1);
                    if (!IsMapBorderCell(cx, cz, w, h))
                        return;

                    for (int k = 0; k < reachCells; k++)
                    {
                        int i = isStart ? k : line.Count - 1 - k;
                        if ((uint)i >= (uint)line.Count)
                            break;
                        float inland = (k + 1) / (float)(reachCells + 1);
                        float widthFade = inland * inland * (3f - 2f * inland);
                        int r = Mathf.Max(1, Mathf.RoundToInt(radiusCells * Mathf.Lerp(0.45f, 1f, widthFade)));
                        int px = Mathf.Clamp(Mathf.RoundToInt(line[i].x), 0, w - 1);
                        int pz = Mathf.Clamp(Mathf.RoundToInt(line[i].y), 0, h - 1);
                        StampUniformUwpFloorDisk(
                            outH,
                            grid,
                            mask,
                            new Vector2(px + 0.5f, pz + 0.5f),
                            r,
                            floorH,
                            fordFloorDelta,
                            fordMul,
                            config);
                    }
                }

                ProcessEnd(true);
                ProcessEnd(false);
            }
        }

        static void SoftenShoreDistanceGrid(int[,] dist, int w, int h, int maxDist, int passes)
        {
            if (dist == null || passes <= 0)
                return;
            int cap = maxDist + 3;
            for (int pass = 0; pass < passes; pass++)
            {
                var next = new int[w, h];
                for (int x = 0; x < w; x++)
                {
                    for (int z = 0; z < h; z++)
                    {
                        int d0 = dist[x, z];
                        if (d0 > cap)
                        {
                            next[x, z] = d0;
                            continue;
                        }

                        int sum = d0;
                        int count = 1;
                        if (x > 0) { sum += dist[x - 1, z]; count++; }
                        if (x + 1 < w) { sum += dist[x + 1, z]; count++; }
                        if (z > 0) { sum += dist[x, z - 1]; count++; }
                        if (z + 1 < h) { sum += dist[x, z + 1]; count++; }
                        next[x, z] = Mathf.RoundToInt(sum / (float)count);
                    }
                }

                for (int x = 0; x < w; x++)
                    for (int z = 0; z < h; z++)
                        dist[x, z] = next[x, z];
            }
        }

        static void ApplyRiverVisualTerrainChannelCarve(float[,] outH, GridSystem grid, MapGenConfig config)
        {
            if (config == null || grid == null || grid.RiverVisualSurfaceMask == null || !config.riverVisualTerrainCarveEnabled)
                return;
            bool[,] m = grid.RiverVisualSurfaceMask;
            float depthW = config.riverTerrainCarveDepthWorld;
            if (depthW < 1e-4f)
                return;
            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            float depth01 = Mathf.Clamp(depthW, 0f, 3f) / terrainY;
            float fordMul = Mathf.Clamp(config.riverTerrainCarveFordMul, 0.08f, 1f);
            int w = grid.Width;
            int h = grid.Height;
            int carved = 0;
            bool uniformUwpChannel = IsUniformUwpRiverCarveChannel(config);
            bool frozenCarve = UsesUwpFrozenCarveContract(grid, config);
            // RTS: bankFall>0 → rama no-uniforme. Frozen Lake First: solo stamps (contrato Headwater).
            // Evita BFS de máscara bool que dibuja orilla en sierra antes del floor plano.
            if (frozenCarve && !uniformUwpChannel)
            {
                float fordFloorDelta = depth01 * 0.14f;
                ApplyUwpTributaryLogicalPathCarve(outH, grid, config, m, depth01, fordFloorDelta, fordMul);
                ApplyUwpTributaryLakeMouthFinalCarveReach(
                    outH, grid, config, m, depth01, fordFloorDelta, fordMul);
                EnsureUwpFrozenCarveFlagsMarked(grid);
                if (config.debugLogs || config.debugHydrologyNetwork || config.debugRiverVisualStats)
                {
                    Debug.Log(
                        $"[RiverVisualTerrainSync] usedSurfaceMask=1 mode=lakeFirstChannelContract " +
                        $"flatFloor=1 skippedMaskBfs=1 meshOverCarve=1 seed={config.seed}");
                }
                return;
            }

            if (uniformUwpChannel)
            {
                int inset = ResolveUwpRiverCarveInsetCells(config);
                float fordFloorDelta = depth01 * 0.14f;
                carved = 0;

                float floorH = ComputeUniformUwpRiverCarveFloor01(config, depth01);
                for (int x = 0; x < w; x++)
                {
                    for (int z = 0; z < h; z++)
                    {
                        if (!IsRiverVisualMaskCoreCell(m, w, h, x, z, inset))
                            continue;
                        if (!IsUwpMainRiverMaskCarveCell(grid, config, x, z))
                            continue;
                        ref var c = ref grid.GetCell(x, z);
                        if (c.type == CellType.Water && !m[x, z])
                            continue;
                        float target = c.riverFord ? floorH - fordFloorDelta * fordMul : floorH;
                        if (outH[x, z] <= target + 1e-8f)
                            continue;
                        outH[x, z] = target;
                        carved++;
                    }
                }

                if (!frozenCarve)
                {
                    ApplyUwpConfluenceCenterlineFloorCarve(outH, grid, config, m, floorH, fordFloorDelta);
                    ApplyUniformUwpOrganicBankSoften(outH, grid, m, config, floorH, fordFloorDelta, fordMul, inset);
                    ApplyUwpMapBorderRiverFloorReach(outH, grid, config, m, floorH, fordFloorDelta, fordMul);
                }

                ApplyUwpTributaryLogicalPathCarve(outH, grid, config, m, depth01, fordFloorDelta, fordMul);

                ApplyUwpTributaryLakeMouthFinalCarveReach(
                    outH, grid, config, m, depth01, fordFloorDelta, fordMul);

                if (config.debugDrawTributaryCarveAudit)
                    UwpTributaryCarveDebugAudit.AuditAfterCarve(outH, grid, config, m);

                if (frozenCarve)
                    EnsureUwpFrozenCarveFlagsMarked(grid);

                if ((carved > 0 || frozenCarve) && (config.debugLogs || config.debugHydrologyNetwork || config.debugRiverVisualStats))
                {
                    Debug.Log(
                        $"[RiverVisualTerrainSync] carvedCells={carved} usedSurfaceMask=1 " +
                        $"mode=uniformUwpFloor frozenCarve={(frozenCarve ? 1 : 0)} insetCells={inset} seed={config.seed}");
                }

                return;
            }

            int falloff = Mathf.Clamp(config.riverTerrainCarveFalloffCells, 1, 32);
            float curve = Mathf.Clamp(config.riverTerrainCarveCenterCurve, 0.35f, 3.5f);
            int extra = Mathf.Clamp(config.riverVisualTerrainCarveExtraCells, 0, 4);
            int bankFall = Mathf.Clamp(config.riverVisualTerrainBankFalloffCells, 0, 8);
            float centerMul = Mathf.Clamp(config.riverVisualTerrainCenterDepthMul, 1f, 1.5f);
            float bankSoft = Mathf.Clamp(config.riverVisualTerrainBankSoftness, 0.35f, 1f);
            int maxD = falloff + extra + bankFall;

            var dBank = new int[w, h];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    dBank[x, z] = int.MaxValue;

            var qx = new Queue<int>();
            var qz = new Queue<int>();
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (!m[x, z])
                        continue;
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
                if (d >= maxD)
                    continue;
                void TryNb(int nx, int nz)
                {
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                        return;
                    ref var c = ref grid.GetCell(nx, nz);
                    if (c.type == CellType.Water && !m[nx, nz])
                        return;
                    if (c.type != CellType.Land && c.type != CellType.River && !(c.type == CellType.Water && m[nx, nz]))
                        return;
                    if (d + 1 >= dBank[nx, nz])
                        return;
                    dBank[nx, nz] = d + 1;
                    qx.Enqueue(nx);
                    qz.Enqueue(nz);
                }

                TryNb(x - 1, z);
                TryNb(x + 1, z);
                TryNb(x, z - 1);
                TryNb(x, z + 1);
            }

            carved = 0;
            int bankCells = 0;
            int maskCells = 0;
            double sumCarve = 0.0;
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (dBank[x, z] > maxD)
                        continue;
                    ref var c = ref grid.GetCell(x, z);
                    if (c.type == CellType.Water && !m[x, z])
                        continue;
                    float u = Mathf.Clamp01(dBank[x, z] / (float)Mathf.Max(1, maxD));
                    float profile = Mathf.Pow(1f - u, curve);
                    if (m[x, z])
                        profile = Mathf.Pow(profile, 1f / centerMul);
                    else
                        profile *= bankSoft;
                    float carve = depth01 * profile;
                    float maxVisualLower;
                    if (config.uwpOwnedVisualPolicy)
                        maxVisualLower = depth01 * (m[x, z] ? 0.95f : 0.72f);
                    else
                    {
                        float bankCapMul = 0.18f;
                        maxVisualLower = depth01 * (m[x, z] ? 0.45f : bankCapMul);
                    }
                    carve = Mathf.Min(carve, maxVisualLower);
                    if (c.riverFord)
                        carve *= fordMul;
                    if (carve < 1e-8f)
                        continue;
                    outH[x, z] = Mathf.Clamp01(outH[x, z] - carve);
                    carved++;
                    sumCarve += carve;
                    if (m[x, z])
                        maskCells++;
                    else
                        bankCells++;
                }
            }

            if (carved > 0 && (config.debugLogs || config.debugHydrologyNetwork || config.debugRiverVisualStats))
            {
                double avg = sumCarve / carved;
                Debug.Log(
                    $"[RiverVisualTerrainSync] riverMaskCells={maskCells} carvedCells={carved} bankCells={bankCells} " +
                    $"centerDepthMul={centerMul:F2} bankSoftness={bankSoft:F2} usedSurfaceMask=1 carve01_medio={(float)avg:F4}");
            }

            if (UsesUwpFrozenCarveContract(grid, config))
            {
                float fordFloorDelta = depth01 * 0.14f;
                ApplyUwpTributaryLogicalPathCarve(outH, grid, config, m, depth01, fordFloorDelta, fordMul);
                ApplyUwpTributaryLakeMouthFinalCarveReach(outH, grid, config, m, depth01, fordFloorDelta, fordMul);
                EnsureUwpFrozenCarveFlagsMarked(grid);
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
                mat = t.materialTemplate;
            }
            if (mat == null)
                return;
            // No mutar el asset compartido: instancia runtime y quita height-blend (aplasta grass sin MaskMap).
            if (!mat.name.EndsWith("(RuntimeSplat)", System.StringComparison.Ordinal))
            {
                mat = new Material(mat) { name = mat.name + " (RuntimeSplat)" };
                t.materialTemplate = mat;
            }
            if (mat.IsKeywordEnabled("_TERRAIN_BLEND_HEIGHT"))
                mat.DisableKeyword("_TERRAIN_BLEND_HEIGHT");
            t.basemapDistance = Mathf.Max(t.basemapDistance, 2000f);
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
            float cliffPush = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.035f, 0.11f, slopeMag)) * 0.85f;
            float push = Mathf.Max(Mathf.Clamp01(slopeMag * sts * 30f), cliffPush);
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

            float maxH = float.MinValue;
            for (int iy = 0; iy < res; iy++)
                for (int ix = 0; ix < res; ix++)
                {
                    float v = heights[iy, ix];
                    if (v > maxH) maxH = v;
                }
            if (maxH < 0.01f) maxH = 1f;

            int[,] shoreDist = useSand ? BuildShoreDistanceGrid(grid, sandShoreCells + 1, config) : null;
            int moistureMaxDist = 1;
            if (useWet)
                moistureMaxDist = Mathf.Clamp(Mathf.CeilToInt(config.terrainMoistureRadius) + 4, 1, 512);
            int[,] moistureDist = useWet ? BuildShoreDistanceGrid(grid, moistureMaxDist, config) : null;

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
                    // Anclar soil a waterHeight01 lógico (no lip visual): llanuras → grass, no dirt.
                    float waterH = Mathf.Clamp01(config != null ? config.waterHeight01 : 0.24f);
                    float landCeil = Mathf.Max(waterH + 0.08f, maxH);
                    float landRange = Mathf.Max(0.04f, landCeil - waterH);
                    float hLand = Mathf.Clamp01((hRaw - waterH) / landRange);

                    float htStr = Mathf.Clamp01(config.terrainHeightTintStrength);
                    if (htStr > 1e-5f)
                        hLand = Mathf.Clamp01(hLand + htStr * (hLand - 0.5f) * 0.5f);

                    float macroNoise = 0.5f;
                    if (macroStr > 1e-5f)
                    {
                        macroNoise = Mathf.PerlinNoise(x * macroSc * 0.11f + config.seed * 0.013f, y * macroSc * 0.11f + config.seed * 0.019f);
                        hLand = Mathf.Clamp01(hLand + (macroNoise - 0.5f) * 2f * macroStr * 0.65f);
                    }
                    if (bufMacro != null)
                        bufMacro[y, x] = macroNoise;

                    float ns = Mathf.Clamp(config.terrainNoiseStrength, 0f, 0.35f);
                    if (ns > 1e-5f)
                    {
                        float sc = Mathf.Max(0.02f, config.terrainNoiseScale);
                        float n = Mathf.PerlinNoise(x * sc * 0.17f + config.seed * 0.01f, y * sc * 0.17f + config.seed * 0.017f);
                        hLand = Mathf.Clamp01(hLand + (n - 0.5f) * 2f * ns);
                    }

                    // Ampliar banda grass: casi toda la llanura sobre el agua.
                    float gMaxLand = Mathf.Clamp(Mathf.Max(gMax, 0.82f), 0.70f, 0.92f);
                    float dMaxLand = Mathf.Clamp(Mathf.Max(dMax, gMaxLand + 0.10f), gMaxLand + 0.05f, 0.97f);

                    float h = hLand;

                    float g, d, r;
                    if (classicGrassDirtPair)
                    {
                        g = 1f - h; d = h; r = 0f;
                    }
                    else
                    {
                        PaintThreeLayers(h, gMaxLand, dMaxLand, blendEff, out g, out d, out r);
                    }

                    // Llanuras → grass; techos/mesetas (hLand alto) → dirt/rock legible.
                    if (hasGrass && hLand < 0.55f)
                    {
                        float grassBias = Mathf.Lerp(0.95f, 0.78f, Mathf.SmoothStep(0f, 0.55f, hLand));
                        g = Mathf.Max(g, grassBias);
                        float rest = Mathf.Max(0f, 1f - g);
                        float dr = d + r;
                        if (dr > 1e-5f)
                        {
                            d = rest * (d / dr);
                            r = rest * (r / dr);
                        }
                        else
                        {
                            d = 0f;
                            r = 0f;
                        }
                    }

                    // Forzar grass solo en Land bajo (no aplastar dirt en mesetas).
                    if (hasGrass && grid != null && gw > 0 && gh > 0)
                    {
                        int gx = Mathf.Clamp(Mathf.RoundToInt((aw > 1) ? (float)x / (aw - 1) * (gw - 1) : 0f), 0, gw - 1);
                        int gz = Mathf.Clamp(Mathf.RoundToInt((ah > 1) ? (float)y / (ah - 1) * (gh - 1) : 0f), 0, gh - 1);
                        var ct = grid.GetCell(gx, gz).type;
                        bool inRiverMask = grid.RiverVisualSurfaceMask != null &&
                                           grid.RiverVisualSurfaceMask[gx, gz];
                        if ((ct == CellType.Land || ct == CellType.Mountain) && !inRiverMask && hLand < 0.50f)
                        {
                            g = Mathf.Max(g, 0.88f);
                            float rest = Mathf.Max(0f, 1f - g);
                            float dr = d + r;
                            if (dr > 1e-5f) { d = rest * (d / dr); r = rest * (r / dr); }
                            else { d = 0f; r = 0f; }
                        }
                    }

                    AbsorbVirtualWeightsIntoRealSoil(ref g, ref d, ref r, hasGrass, hasDirt, hasRock);

                    if (hasRock)
                    {
                        // Cap suave: slope no debe convertir llanuras en dirt/rock (síntoma ocre del screenshot).
                        float sts = Mathf.Clamp01(config.terrainSlopeTintStrength) * 0.55f;
                        ApplySlopeToSoil(heights, res, hx, hy, sts, ref g, ref d, ref r);
                    }

                    // Tras slope: hierba solo en llanura; mesetas conservan dirt/rock del PaintThreeLayers.
                    if (hasGrass && hLand < 0.50f)
                    {
                        float postGrass = Mathf.Lerp(0.92f, 0.78f, Mathf.SmoothStep(0f, 0.50f, hLand));
                        if (g < postGrass)
                        {
                            g = postGrass;
                            float rest = Mathf.Max(0f, 1f - g);
                            float dr = d + r;
                            if (dr > 1e-5f) { d = rest * (d / dr); r = rest * (r / dr); }
                            else { d = 0f; r = 0f; }
                        }
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

            if (hasGrass && iGrass >= 0 && ah > 0 && aw > 0)
            {
                double sumG = 0d;
                int n = 0;
                int step = Mathf.Max(1, Mathf.Min(aw, ah) / 64);
                for (int y = 0; y < ah; y += step)
                    for (int x = 0; x < aw; x += step)
                    {
                        sumG += map[y, x, iGrass];
                        n++;
                    }
                float avgG = n > 0 ? (float)(sumG / n) : 0f;
                Debug.Log(
                    $"[TerrainExporter] splat grassAvg={avgG:F3} layers={numLayers} " +
                    $"dry={(useGrassDry ? 1 : 0)} sand={(useSand ? 1 : 0)} shoreCells={sandShoreCells}");
            }
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

        static int[,] BuildShoreDistanceGrid(GridSystem grid, int maxDist, MapGenConfig config)
        {
            int w = grid.Width;
            int h = grid.Height;
            var dist = new int[w, h];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    dist[x, z] = -1;

            var qx = new Queue<int>();
            var qz = new Queue<int>();
            bool useMaskPaint = UsesRiverVisualSurfaceMaskForTerrainPaint(grid, config);
            bool uniformUwpRiverCarve = IsUniformUwpRiverCarveChannel(config);
            int uwpCarveInset = uniformUwpRiverCarve ? ResolveUwpRiverCarveInsetCells(config) : 0;
            bool[,] rivMask = useMaskPaint ? grid.RiverVisualSurfaceMask : null;
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    var t = grid.GetCell(x, z).type;
                    bool seedRiverLogical = t == CellType.River && !useMaskPaint;
                    bool seedVisualRiver = rivMask != null && rivMask[x, z];
                    if (uniformUwpRiverCarve && seedVisualRiver)
                        seedVisualRiver = IsRiverVisualMaskCoreCell(rivMask, w, h, x, z, uwpCarveInset);
                    bool seed = t == CellType.Water || seedRiverLogical || seedVisualRiver;
                    if (seed)
                    {
                        dist[x, z] = 0;
                        qx.Enqueue(x);
                        qz.Enqueue(z);
                    }
                }
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
                    if (useMaskPaint && rivMask != null &&
                        grid.GetCell(nx, nz).type == CellType.River && !rivMask[nx, nz])
                        return;
                    dist[nx, nz] = d + 1;
                    qx.Enqueue(nx);
                    qz.Enqueue(nz);
                }
                Try(x - 1, z); Try(x + 1, z); Try(x, z - 1); Try(x, z + 1);
            }
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    if (dist[x, z] == -1) dist[x, z] = maxDist + 1;
            // Softener solo corría en mode=uniform; RTS fuerza bankFall=5 → sin softener
            // la arena del lecho sigue el borde bool jagged del main (orilla blanca cuadriculada).
            if (uniformUwpRiverCarve || UsesUwpFrozenCarveContract(grid, config))
                SoftenShoreDistanceGrid(dist, w, h, maxDist, uniformUwpRiverCarve ? 2 : 4);
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

        /// <summary>Nivel visual del agua en height01 (misma lógica que suavizado de orilla).</summary>
        public static float ComputeWaterVisualHeight01(MapGenConfig config)
        {
            if (config == null)
                return 0.24f;

            float waterH = config.waterHeight01;
            if (!WaterVisualPipelinePolicy.IsUnifiedSingleSurface(config))
                return waterH;

            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            float visualSurfaceOffsetWorld =
                Mathf.Max(config.waterSurfaceOffset, 0.02f) +
                config.unifiedWaterSurfaceExtraYOffsetWorld +
                WaterMeshBuilder.ComputeUnifiedWaterDepthDrivenLiftWorld(config, terrainY);
            float visualWaterH = waterH + visualSurfaceOffsetWorld / Mathf.Max(1e-4f, terrainY);
            return Mathf.Clamp01(visualWaterH + config.unifiedWaterShoreTerrainOffsetWorld / Mathf.Max(1e-4f, terrainY));
        }

        /// <summary>Heightmap de celdas post-orilla/carve (sin muestrear Terrain). Para evaluación de calidad.</summary>
        public static float[,] BuildEvaluationCellHeights(GridSystem grid, MapGenConfig config) =>
            BuildShoreSmoothedCellHeights(grid, config);

        /// <summary>Snapshot height01 lógico antes del pase visual de terreno.</summary>
        public static float[,] BuildLogicalEvaluationCellHeights(GridSystem grid) =>
            BuildLogicalCellHeightSnapshot(grid);

        public static float ResolveUwpRiverCarveFloor01ForDebug(
            GridSystem grid, MapGenConfig config, float depth01, int riverIndex) =>
            ResolveUwpRiverCarveFloor01(grid, config, depth01, riverIndex);

        public static bool IsLandCellAdjacentToLakeWaterForDebug(GridSystem grid, int cx, int cz) =>
            IsLandCellAdjacentToLakeWater(grid, cx, cz);
    }
}
