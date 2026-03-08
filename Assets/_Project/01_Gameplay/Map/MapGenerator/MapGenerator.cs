using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
        public bool Generate(MapGenConfig cfg = null, Terrain t = null)
        {
            MapGenConfig c = cfg != null ? cfg : config;
            Terrain tr = t != null ? t : terrain;
            if (c == null)
            {
                Debug.LogError("MapGenerator: MapGenConfig es null. Asigna uno o pásalo por parámetro.");
                return false;
            }

            _dbg = debugLogs || c.debugLogs;

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

                LogPhaseStart("Fase2 Regiones");
                RegionGenerator.GenerateRegions(_grid, c, _rng);
                LogPhaseEnd("Fase2 Regiones");

                LogPhaseStart("Fase3 Agua");
                WaterGenerator.GenerateWater(_grid, c, _rng);
                LogPhaseEnd("Fase3 Agua");

                LogPhaseStart("Fase4 Heights");
                HeightGenerator.GenerateHeights(_grid, c, _rng);
                LogPhaseEnd("Fase4 Heights");

                LogPhaseStart("Fase5 Ciudades");
                _cities = CityGenerator.GenerateCities(_grid, c, _rng);
                LogPhaseEnd("Fase5 Ciudades", $"# ciudades={_cities.Count}");

                LogPhaseStart("Fase6 Caminos");
                _roads = RoadNetworkGenerator.BuildRoads(_grid, _cities, c);
                LogPhaseEnd("Fase6 Caminos", $"# caminos={_roads.Count}");

                LogPhaseStart("Fase7 Carve");
                TerrainCarver.ApplyCityFlatten(_grid, _cities, c);
                TerrainCarver.ApplyRoadFlatten(_grid, _roads, c);
                LogPhaseEnd("Fase7 Carve");

                LogPhaseStart("Fase8 Recursos");
                ResourceGenerator.PlaceResources(_grid, _cities, c, _rng);
                LogPhaseEnd("Fase8 Recursos");

                if (tr != null)
                {
                    LogPhaseStart("Fase9 TerrainExport");
                    TerrainExporter.ApplyToTerrain(tr, _grid, c,
                        terrainGrassLayerOverride, terrainDirtLayerOverride, terrainRockLayerOverride,
                        terrainGrassTileSize, terrainDirtTileSize, terrainRockTileSize,
                        terrainSandLayerOverride, terrainSandTileSize, terrainSandShoreCells);
                    LogPhaseEnd("Fase9 TerrainExport");
                    StartCoroutine(RefreshTerrainNextFrame(tr));
                }

                LogPhaseStart("Fase9 WaterMesh");
                WaterMeshBuilder.BuildWaterMeshes(_grid, c, c.waterMaterial);
                LogPhaseEnd("Fase9 WaterMesh");

                LogPhaseStart("Fase10 GameplayExport");
                RunPhase10_GameplayExport(c);
                LogPhaseEnd("Fase10 GameplayExport");

                if (MapValidator.Validate(_grid, _cities, _roads, c, out string reason))
                {
                    if (_dbg) Debug.Log($"MapGenerator: Validación OK (seed={seed}, retry={retry}).");
                    OnGenerationComplete?.Invoke(_grid, _cities, _roads, c);
                    return true;
                }
                Debug.LogWarning($"MapGenerator: Validación fallida (retry={retry}): {reason}");
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
    }
}
