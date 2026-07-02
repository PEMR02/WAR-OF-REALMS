using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Map.Generation.Alpha;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Orquestador del Generador Definitivo. Ejecuta fases en orden; validación y reintentos.</summary>
    public class MapGenerator : MonoBehaviour
    {
        /// <summary>Se invoca al terminar Generate() con éxito. Suscríbete para sincronizar MapGrid, colocar TCs, bake NavMesh, etc.</summary>
        public static event System.Action<GridSystem, List<CityNode>, List<Road>, MapGenConfig> OnGenerationComplete;

        [Tooltip("Configuración del generador. Crear desde Assets → Create → Map Generator → MapGenConfig.")]
        public MapGenConfig config;
        [Tooltip("Terrain donde se exportará heightmap y splat. Puede ser null para solo datos lógicos.")]
        public Terrain terrain;
        [Header("Terrain texturas (override)")]
        [Tooltip("Si se asignan aquí, se usan en lugar de los del MapGenConfig (útil cuando el RTS pasa sus grass/dirt/rock).")]
        public TerrainLayer terrainGrassLayerOverride;
        public TerrainLayer terrainDirtLayerOverride;
        public TerrainLayer terrainRockLayerOverride;
        [Tooltip("Tamaño de tiling en mundo (X,Z). >0 para reducir repetición de la textura. RTSMapGenerator lo asigna desde grassTileSize.")]
        public Vector2 terrainGrassTileSize;
        public Vector2 terrainDirtTileSize;
        public Vector2 terrainRockTileSize;
        public TerrainLayer terrainSandLayerOverride;
        public Vector2 terrainSandTileSize;
        [Range(1, 6)] public int terrainSandShoreCells = 3;

        [Header("Debug")]
        [Tooltip("Logs detallados del pipeline (override local). También se puede activar desde MapGenConfig.debugLogs.")]
        public bool debugLogs = false;

        private GridSystem _grid;
        private IRng _rng;
        private List<CityNode> _cities = new List<CityNode>();
        private List<Road> _roads = new List<Road>();
        private float _phaseStartTime;
        private bool _dbg;

        /// <summary>Grid generado (válido tras Generate() exitoso).</summary>
        public GridSystem Grid => _grid;
        /// <summary>Ciudades colocadas (válido tras Generate() exitoso).</summary>
        public List<CityNode> Cities => _cities;
        /// <summary>Caminos entre ciudades (válido tras Generate() exitoso).</summary>
        public List<Road> Roads => _roads;

        /// <summary>Ejecuta el pipeline completo. Retorna true si la validación pasó.</summary>
        /// <param name="skipSurfaceExport">Si true, no exporta terreno ni crea mallas de agua (vista previa / lobby).</param>
        /// <param name="skipRoadConnectivityValidation">Si true, no exige que el MST de caminos una todas las ciudades (solo lobby / preview 2D).</param>
        public bool Generate(MapGenConfig cfg = null, Terrain t = null, bool skipSurfaceExport = false, bool skipRoadConnectivityValidation = false)
        {
            MapGenConfig c = cfg != null ? cfg : config;
            Terrain tr = t != null ? t : terrain;
            if (c == null)
            {
                Debug.LogError("MapGenerator: MapGenConfig es null. Asigna uno o pásalo por parámetro.");
                return false;
            }

            WaterVisualPipelinePolicy.ApplyToRuntimeConfig(c);
            _dbg = debugLogs || c.debugLogs;

            if (_dbg || c.debugHydrologyNetwork || c.debugRiverHydrologyPerf)
                Debug.Log($"[WaterHeightRuntime] waterHeight01={c.waterHeight01:F4} waterVisualPipeline={WaterVisualPipelinePolicy.RuntimeName(c)}");

            for (int retry = 0; retry < c.maxRetries; retry++)
            {
                int seed = c.seed + retry;
                _rng = new XorShiftRng(seed);
                _grid = new GridSystem(c.gridW, c.gridH, c.cellSizeWorld, c.origin);
                _cities.Clear();
                _roads.Clear();

                LogPhaseStart("Fase0 Init");
                RunPhase0_Init(c);
                LogPhaseEnd("Fase0 Init");

                LogPhaseStart("Fase1 GridBase");
                RunPhase1_GridBase(c);
                LogPhaseEnd("Fase1 GridBase");
                MapHeightPhaseDiagnostics.TryLogHeightPhase(_grid, c, "after_grid_base");

                LogPhaseStart("Fase2 Regiones");
                RegionGenerator.GenerateRegions(_grid, c, _rng);
                LogPhaseEnd("Fase2 Regiones");
                MapHeightPhaseDiagnostics.TryLogHeightPhase(_grid, c, "after_regions");

                // Pipeline alturas/hidrología: primero height01 de tierra (ruido + macro) para que el muestreo downhill
                // del agua vea relieve real; luego tipado acuático, campo de distancia a agua, superficie cauce/lago,
                // refinamiento (picos/spawn) y por último ciudades/caminos + carve (sin mover SimpleRiverPathGenerator).
                LogPhaseStart("Fase3 BaseHeightGeneration");
                HeightGenerator.GenerateBaseTerrainHeights(_grid, c, _rng);
                LogPhaseEnd("Fase3 BaseHeightGeneration");
                MapHeightPhaseDiagnostics.TryLogHeightPhase(_grid, c, "after_base_noise_heights");
                MapTerrainReliefDiagnostics.TryLogTerrainRelief(_grid, c, "after_base_noise_heights");

                LogPhaseStart("Fase3b MacroRelief (alpha)");
                MacroTerrainSculptor.Apply(_grid, c, _rng, c.alphaTerrainFeatureRecord);
                LogPhaseEnd("Fase3b MacroRelief (alpha)");
                MapHeightPhaseDiagnostics.TryLogHeightPhase(_grid, c, "after_macro_pre_water");
                MapTerrainReliefDiagnostics.TryLogTerrainRelief(_grid, c, "after_macro_pre_water");

                LogPhaseStart("Fase4 Agua / hidrología");
                WaterGenerator.GenerateWater(_grid, c, _rng);
                LogPhaseEnd("Fase4 Agua / hidrología");
                MapHeightPhaseDiagnostics.TryLogHeightPhase(_grid, c, "after_water_hydro");

                LogPhaseStart("Fase4b WaterDistance");
                WaterDistanceField.Build(_grid);
                LogPhaseEnd("Fase4b WaterDistance");
                MapHeightPhaseDiagnostics.TryLogHeightPhase(_grid, c, "after_water_distance");

                LogPhaseStart("Fase4c HydrologySurfaceHeights");
                HeightGenerator.ApplyHydrologySurfaceHeights(_grid, c);
                LogPhaseEnd("Fase4c HydrologySurfaceHeights");
                MapHeightPhaseDiagnostics.TryLogHeightPhase(_grid, c, "after_hydro_surface_heights");

                LogPhaseStart("Fase5 Refinamiento terreno (post-hidrología)");
                HeightGenerator.GenerateFinalTerrainPass(_grid, c);
                LogPhaseEnd("Fase5 Refinamiento terreno (post-hidrología)");
                MapHeightPhaseDiagnostics.TryLogHeightPhase(_grid, c, "after_macro_smooth_normalize");
                MapTerrainReliefDiagnostics.TryLogTerrainRelief(_grid, c, "after_macro_smooth_normalize");

                LogPhaseStart("Fase5b WaterSurfaceFields");
                WaterSurfaceFieldBuilder.Build(_grid, c);
                LogPhaseEnd("Fase5b WaterSurfaceFields");

                LogPhaseStart("Fase6 Ciudades");
                _cities = CityGenerator.GenerateCities(_grid, c, _rng);
                LogPhaseEnd("Fase6 Ciudades", $"# ciudades={_cities.Count}");

                LogPhaseStart("Fase7 Caminos");
                _roads = RoadNetworkGenerator.BuildRoads(_grid, _cities, c);
                LogPhaseEnd("Fase7 Caminos", $"# caminos={_roads.Count}");

                LogPhaseStart("Fase8 Carve");
                TerrainCarver.ApplyCityFlatten(_grid, _cities, c);
                TerrainCarver.ApplyRoadFlatten(_grid, _roads, c);
                HeightGenerator.RecalculateLandSlopes(_grid, c);
                LogPhaseEnd("Fase8 Carve");
                MapHeightPhaseDiagnostics.TryLogHeightPhase(_grid, c, "after_city_road_flatten");

                LogPhaseStart("Fase8c RegionClassification");
                if (c.alphaRegionRules != null)
                {
                    _grid.SemanticRegions = RegionClassifier.Classify(_grid, c.alphaRegionRules);
                    if (_dbg && _grid.SemanticRegions != null)
                    {
                        var m = _grid.SemanticRegions;
                        Debug.Log(
                            $"[MapGen] Regiones: Plain={m.CountType(TerrainRegionType.Plain)} Hill={m.CountType(TerrainRegionType.Hill)} " +
                            $"Mtn={m.CountType(TerrainRegionType.Mountain)} RiverBank={m.CountType(TerrainRegionType.RiverBank)} " +
                            $"SpawnFriendly={m.CountType(TerrainRegionType.SpawnFriendly)} ForestCand={m.CountType(TerrainRegionType.ForestCandidate)}");
                    }
                }
                else
                    _grid.SemanticRegions = null;
                LogPhaseEnd("Fase8c RegionClassification");

                LogPhaseStart("Fase8 Recursos");
                ResourceGenerator.PlaceResources(_grid, _cities, c, _rng);
                LogPhaseEnd("Fase8 Recursos");

                if (!skipSurfaceExport)
                {
                    var spawnCells = new List<Vector2Int>();
                    int nSpawns = Mathf.Max(0, _cities != null ? _cities.Count : 0);
                    nSpawns = Mathf.Min(nSpawns, Mathf.Max(1, c.cityCount));
                    for (int i = 0; i < nSpawns; i++)
                        spawnCells.Add(_cities[i].Center);

                    if (UwpFrozenSurfacePipeline.ShouldUse(c))
                    {
                        LogPhaseStart("Fase9 UwpFrozenSurface");
                        var layers = new UwpFrozenSurfacePipeline.TerrainExportLayers
                        {
                            grass = terrainGrassLayerOverride,
                            dirt = terrainDirtLayerOverride,
                            rock = terrainRockLayerOverride,
                            sand = terrainSandLayerOverride,
                            grassTile = terrainGrassTileSize,
                            dirtTile = terrainDirtTileSize,
                            rockTile = terrainRockTileSize,
                            sandTile = terrainSandTileSize,
                            sandShoreCells = terrainSandShoreCells
                        };
                        UwpFrozenSurfacePipeline.Apply(
                            _grid, c, tr, c.riverWaterMaterial, spawnCells, _cities, _roads, layers);
                        if (tr != null)
                            StartCoroutine(RefreshTerrainNextFrame(tr));
                        LogPhaseEnd("Fase9 UwpFrozenSurface");
                    }
                    else
                    {
                        if (tr != null)
                        {
                            LogPhaseStart("Fase9 TerrainExport");
                            TerrainExporter.ApplyToTerrain(tr, _grid, c,
                                terrainGrassLayerOverride, terrainDirtLayerOverride, terrainRockLayerOverride,
                                terrainGrassTileSize, terrainDirtTileSize, terrainRockTileSize,
                                terrainSandLayerOverride, terrainSandTileSize, terrainSandShoreCells);
                            TerrainSplatDebugDisplay.Refresh(tr, c);
                            LogPhaseEnd("Fase9 TerrainExport");
                            StartCoroutine(RefreshTerrainNextFrame(tr));
                        }

                        LogPhaseStart("Fase9 WaterMesh");
                        WaterMeshBuilder.BuildWaterMeshes(_grid, c, c.riverWaterMaterial, spawnCells, _cities, _roads);
                        LogPhaseEnd("Fase9 WaterMesh");
                    }
                }

                LogPhaseStart("Fase10 GameplayExport");
                RunPhase10_GameplayExport(c);
                LogPhaseEnd("Fase10 GameplayExport");

                string valReason;
                bool okValidation = skipRoadConnectivityValidation
                    ? MapValidator.Validate(_grid, _cities, c, out valReason)
                    : MapValidator.Validate(_grid, _cities, _roads, c, out valReason);
                if (okValidation)
                {
                    if (_dbg) Debug.Log($"MapGenerator: Validación OK (seed={seed}, retry={retry}).");
                    if (!skipSurfaceExport)
                        OnGenerationComplete?.Invoke(_grid, _cities, _roads, c);
                    return true;
                }
                Debug.LogWarning($"MapGenerator: Validación fallida (retry={retry}): {valReason}");
            }

            Debug.LogError("MapGenerator: Todas las reintentos fallaron.");
            return false;
        }

        private void LogPhaseStart(string phase)
        {
            _phaseStartTime = Time.realtimeSinceStartup;
            if (_dbg) Debug.Log($"[{phase}] Inicio (t={_phaseStartTime:F2}s)");
        }

        private void LogPhaseEnd(string phase, string extra = null)
        {
            float elapsed = Time.realtimeSinceStartup - _phaseStartTime;
            if (_dbg) Debug.Log($"[{phase}] Fin en {elapsed:F3}s" + (string.IsNullOrEmpty(extra) ? "" : $" | {extra}"));
        }

        private void RunPhase0_Init(MapGenConfig c)
        {
            if (_dbg) Debug.Log($"Fase0: seed={_rng.Seed}, grid={c.gridW}x{c.gridH}, cellSize={c.cellSizeWorld}");
        }

        private void RunPhase1_GridBase(MapGenConfig c)
        {
            _grid.DistanceToWaterCells = null;
            _grid.WaterShoreDistanceCells = null;
            _grid.WaterDepth01 = null;
            _grid.WaterFlowXZ = null;
            _grid.SemanticRegions = null;
            for (int x = 0; x < _grid.Width; x++)
                for (int z = 0; z < _grid.Height; z++)
                {
                    ref var cell = ref _grid.GetCell(x, z);
                    cell = CellData.Default();
                }
            if (_dbg) Debug.Log($"Fase1: Grid {_grid.Width}x{_grid.Height} inicializado.");
        }

        private void RunPhase10_GameplayExport(MapGenConfig c)
        {
            // Hook: NavMesh bake, entrega de datos a BuildSystem/Pathfinding.
            // No asumimos APIs existentes; tu código puede suscribirse a un evento estático o leer _grid después.
            if (_dbg) Debug.Log("Fase10: Gameplay export listo (hook para NavMesh/BuildSystem).");
        }

        /// <summary>Forzar que el Terrain actualice el alphamap en el siguiente frame (URP a veces no lo muestra hasta entonces).</summary>
        private IEnumerator RefreshTerrainNextFrame(Terrain tr)
        {
            yield return null;
            if (tr == null) yield break;
            tr.terrainData = tr.terrainData;
            tr.enabled = false;
            tr.enabled = true;
        }

        /// <summary>Para debug: ejecutar una fase concreta (requiere grid/rng/cities ya creados). No usado en Generate() normal.</summary>
        [ContextMenu("Debug: Generate (usar config y terrain asignados)")]
        private void DebugGenerate()
        {
            Generate(config, terrain);
        }

        /// <summary>Altura mundo para gizmos: si hay Terrain, encima de la superficie muestreada (evita dibujar bajo el mesh).</summary>
        float GizmoYAtWorldXZ(float wx, float wz, float aboveSurface = 0.35f)
        {
            if (terrain != null && terrain.terrainData != null)
            {
                Vector3 tp = terrain.transform.position;
                float h = terrain.SampleHeight(new Vector3(wx, tp.y + 2000f, wz)) + tp.y;
                return h + aboveSurface;
            }
            return _grid.Origin.y + 0.15f + aboveSurface;
        }

        void OnDrawGizmos()
        {
            MapGenConfig c = config;
            if (c == null || _grid == null)
                return;
            float cs = _grid.CellSizeWorld;
            Vector3 o = _grid.Origin;
            // Una muestra al centro del mapa para overlays densos (máscara): evita miles de SampleHeight por frame.
            float cxm = o.x + _grid.Width * cs * 0.5f;
            float czm = o.z + _grid.Height * cs * 0.5f;
            float y = GizmoYAtWorldXZ(cxm, czm, 0.05f);

            if (c.debugDrawWaterMaskGizmos)
            {
                var mask = WaterGenerator.DebugLastRiverFusionMask01;
                var core = WaterGenerator.DebugLastRiverFusionCoreMask;
                var shore = WaterGenerator.DebugLastRiverFusionShoreMask;
                if (mask != null && core != null && shore != null)
                {
                    int w = _grid.Width;
                    int h = _grid.Height;
                    float s = Mathf.Max(0.06f, cs * 0.22f);
                    for (int z = 0; z < h; z++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            float v = Mathf.Clamp01(mask[x, z]);
                            if (v < 0.02f && !core[x, z] && !shore[x, z])
                                continue;

                            Vector3 p = new Vector3(o.x + (x + 0.5f) * cs, y + 0.02f, o.z + (z + 0.5f) * cs);
                            Color col = new Color(0.1f, 0.55f, 0.95f, Mathf.Lerp(0.08f, 0.38f, v));
                            if (core[x, z]) col = new Color(0.9f, 0.15f, 0.15f, 0.42f);
                            else if (shore[x, z]) col = new Color(0.18f, 0.95f, 0.35f, 0.44f);
                            Gizmos.color = col;
                            Gizmos.DrawCube(p, new Vector3(s, 0.02f, s));
                        }
                    }
                }
            }

            if (c.debugDrawWaterFusionMask && WaterGenerator.DebugLastRiverFusionBlurField != null)
            {
                var blur = WaterGenerator.DebugLastRiverFusionBlurField;
                int wF = _grid.Width;
                int hF = _grid.Height;
                float s = Mathf.Max(0.06f, cs * 0.2f);
                for (int z = 0; z < hF; z++)
                {
                    for (int x = 0; x < wF; x++)
                    {
                        float v = Mathf.Clamp01(blur[x, z]);
                        if (v < 0.015f)
                            continue;
                        Vector3 p = new Vector3(o.x + (x + 0.5f) * cs, y + 0.03f, o.z + (z + 0.5f) * cs);
                        Gizmos.color = new Color(0.12f, 0.42f, 0.92f, Mathf.Lerp(0.08f, 0.42f, v));
                        Gizmos.DrawCube(p, new Vector3(s, 0.015f, s));
                    }
                }
            }

            if (c.debugDrawWaterShoreDepthGizmos && WaterMeshBuilder.DebugLastWaterInteriorDistanceGrid != null)
            {
                var distG = WaterMeshBuilder.DebugLastWaterInteriorDistanceGrid;
                int wM = _grid.Width;
                int hM = _grid.Height;
                int md = Mathf.Max(1, WaterMeshBuilder.DebugLastWaterInteriorDistanceMax);
                float s = Mathf.Max(0.05f, cs * 0.18f);
                for (int z = 0; z < hM; z++)
                {
                    for (int x = 0; x < wM; x++)
                    {
                        int d = distG[x, z];
                        if (d < 0) continue;
                        var ct = _grid.GetCell(x, z).type;
                        if (ct != CellType.Water && ct != CellType.River) continue;
                        float t = Mathf.Clamp01(d / (float)md);
                        Vector3 p = new Vector3(o.x + (x + 0.5f) * cs, y + 0.04f, o.z + (z + 0.5f) * cs);
                        Gizmos.color = Color.Lerp(new Color(0.2f, 0.85f, 1f, 0.32f), new Color(0.04f, 0.1f, 0.35f, 0.4f), t);
                        Gizmos.DrawCube(p, new Vector3(s, 0.012f, s));
                    }
                }
            }

            if (c.riverCrossingDebugVisuals)
            {
                var fpC = WaterMeshBuilder.DebugRiverFordFootprintCentersWorld;
                var fpS = WaterMeshBuilder.DebugRiverFordFootprintSizesWorld;
                if (fpC != null && fpS != null && fpC.Count == fpS.Count)
                {
                    for (int i = 0; i < fpC.Count; i++)
                    {
                        float gy = GizmoYAtWorldXZ(fpC[i].x, fpC[i].z, 0.18f);
                        Vector3 p = new Vector3(fpC[i].x, gy, fpC[i].z);
                        Gizmos.color = new Color(1f, 0.92f, 0.12f, 0.95f);
                        Gizmos.DrawWireCube(p, fpS[i]);
                    }
                }

                var failed = WaterMeshBuilder.DebugRiverFordFailedCenterPositionsWorld;
                if (failed != null && failed.Count > 0)
                {
                    float fr = Mathf.Max(0.06f, cs * 0.22f);
                    for (int i = 0; i < failed.Count; i++)
                    {
                        float gy = GizmoYAtWorldXZ(failed[i].x, failed[i].z, 0.22f);
                        Vector3 p = new Vector3(failed[i].x, gy, failed[i].z);
                        Gizmos.color = new Color(1f, 0.28f, 0.22f, 0.92f);
                        Gizmos.DrawWireSphere(p, fr);
                    }
                }
            }

            if (c.debugDrawWaterCrossingGizmos)
            {
                var cx = WaterMeshBuilder.DebugWaterCrossingPositionsWorld;
                if (cx != null && cx.Count > 0)
                {
                    Gizmos.color = new Color(1f, 0.55f, 0.05f, 0.9f);
                    float cr = Mathf.Max(0.1f, cs * 0.4f);
                    for (int i = 0; i < cx.Count; i++)
                    {
                        float sy = GizmoYAtWorldXZ(cx[i].x, cx[i].z, 0.15f);
                        Gizmos.DrawWireSphere(new Vector3(cx[i].x, sy, cx[i].z), cr);
                    }
                }

                // Vados funcionales: verde = celdas River transitables (altura por celda para terreno ondulado).
                float fs = Mathf.Max(0.12f, cs * 0.28f);
                float fh = Mathf.Max(0.04f, cs * 0.08f);
                Gizmos.color = new Color(0.18f, 0.95f, 0.28f, 0.85f);
                for (int z = 0; z < _grid.Height; z++)
                {
                    for (int x = 0; x < _grid.Width; x++)
                    {
                        ref var cd = ref _grid.GetCell(x, z);
                        if (cd.type != CellType.River || !cd.riverFord)
                            continue;
                        float wx = o.x + (x + 0.5f) * cs;
                        float wz = o.z + (z + 0.5f) * cs;
                        float gy = GizmoYAtWorldXZ(wx, wz, 0.12f);
                        Vector3 p = new Vector3(wx, gy, wz);
                        Gizmos.DrawCube(p, new Vector3(fs, fh, fs));

                        // Marcador alto para localizar vados a distancia en Scene view.
                        float towerHeight = 100f;
                        float towerRadius = Mathf.Max(0.25f, cs * 0.18f);
                        Vector3 tp = new Vector3(wx, gy + towerHeight * 0.5f, wz);
                        Gizmos.DrawCube(tp, new Vector3(towerRadius, towerHeight, towerRadius));
                        Gizmos.DrawWireSphere(new Vector3(wx, gy + towerHeight, wz), Mathf.Max(0.6f, cs * 0.3f));
                    }
                }
            }

            if (!c.debugDrawRiverPathInScene)
            {
                if (!c.debugDrawRiverRibbonGizmos && !c.riverSurfaceDebugDrawCenterline)
                    return;
            }

            void DrawPolyCell(List<Vector2> poly, Color col)
            {
                if (poly == null || poly.Count < 2)
                    return;
                Gizmos.color = col;
                for (int i = 0; i < poly.Count - 1; i++)
                {
                    Vector3 a = new Vector3(o.x + poly[i].x * cs, y, o.z + poly[i].y * cs);
                    Vector3 b = new Vector3(o.x + poly[i + 1].x * cs, y, o.z + poly[i + 1].y * cs);
                    Gizmos.DrawLine(a, b);
                }
            }

            if (c.debugRiverDrawMacro && _grid.RiverPathDebugMacro != null)
            {
                foreach (var poly in _grid.RiverPathDebugMacro)
                    DrawPolyCell(poly, new Color(1f, 0.4f, 0.9f, 0.9f));
            }

            if (c.debugRiverDrawSmoothedCenterline && _grid.RiverPathDebugSmoothed != null)
            {
                foreach (var poly in _grid.RiverPathDebugSmoothed)
                    DrawPolyCell(poly, new Color(0.2f, 0.95f, 0.35f, 0.9f));
            }

            if (c.debugDrawRiverRibbonGizmos || c.riverSurfaceDebugDrawCenterline)
            {
                float pSize = Mathf.Max(0.02f, c.debugRiverRibbonPointSize);
                var pts = c.riverSurfaceDebugDrawCenterline
                    ? RiverSurfaceMeshBuilder.DebugRiverSurfaceCenterlineNodesWorld
                    : WaterMeshBuilder.DebugRibbonPathPointsWorld;
                if (pts != null && pts.Count > 0)
                {
                    Gizmos.color = new Color(1f, 0.95f, 0.05f, 0.95f);
                    float gy = y + 0.08f;
                    for (int i = 0; i < pts.Count; i++)
                        Gizmos.DrawSphere(new Vector3(pts[i].x, gy, pts[i].z), pSize * 2.2f);
                }

                if (c.debugDrawRiverRibbonGizmos)
                {
                    var okA = WaterMeshBuilder.DebugRibbonAcceptedSegmentsAWorld;
                    var okB = WaterMeshBuilder.DebugRibbonAcceptedSegmentsBWorld;
                    Gizmos.color = new Color(0.15f, 1f, 0.25f, 0.92f);
                    int okCount = Mathf.Min(okA.Count, okB.Count);
                    for (int i = 0; i < okCount; i++)
                        Gizmos.DrawLine(new Vector3(okA[i].x, y + 0.08f, okA[i].z), new Vector3(okB[i].x, y + 0.08f, okB[i].z));

                    var badA = WaterMeshBuilder.DebugRibbonDiscardedSegmentsAWorld;
                    var badB = WaterMeshBuilder.DebugRibbonDiscardedSegmentsBWorld;
                    Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.95f);
                    int badCount = Mathf.Min(badA.Count, badB.Count);
                    for (int i = 0; i < badCount; i++)
                        Gizmos.DrawLine(new Vector3(badA[i].x, y + 0.1f, badA[i].z), new Vector3(badB[i].x, y + 0.1f, badB[i].z));
                }
            }
        }
    }
}
