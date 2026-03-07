using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Project.Gameplay.Buildings;
using Project.Gameplay.Combat;
using Unity.AI.Navigation;
using Project.Gameplay.Map.Generator;

namespace Project.Gameplay.Map
{
    public enum WaterMeshMode
    {
        [Tooltip("Varios meshes por zona (chunks). Mejor culling en mapas grandes.")]
        Chunks,
        [Tooltip("Un rectángulo del tamaño del mapa a waterHeight; geometría solo donde terreno < waterHeight (intersección con el mapa). Un solo mesh/draw call.")]
        FullPlaneIntersect
    }

    [DefaultExecutionOrder(-200)] // Ejecutar antes que las unidades para desactivar NavMeshAgent y evitar "Failed to create agent"
    public class RTSMapGenerator : MonoBehaviour
    {
        [Header("Map Preset (Estilo AoE2)")]
        [Tooltip("Preset del mapa. 'Custom' usa los valores del Inspector. Otros presets sobrescriben configuración automáticamente.")]
        public MapPresetType mapPreset = MapPresetType.Continental;
        
        [Header("Grid")]
        [Tooltip("⚠️ IMPORTANTE: Asigna 'GridConfig.asset' aquí. Es la fuente única de verdad para el tamaño de celda.")]
        public GridConfig gridConfig;
        public int width = 256;
        public int height = 256;
        public bool centerAtOrigin = true;
        [Tooltip("Offset aleatorio dentro de cada celda (0=centro, 1=máx. hasta borde). Así los recursos no quedan tan cuadrados.")]
        [Range(0f, 1f)] public float cellPlacementRandomOffset = 0.8f;

        [Header("Seed")]
        public int seed = 12345;
        public bool randomSeedOnPlay = true;

        [Header("Terrain (opcional)")]
        public Terrain terrain;
        [Tooltip("Cota de agua en world units. Celdas con altura del terreno < esta cota = agua. -999 = sin agua. Tras generar, en consola se muestra el rango real del terreno para que ajustes con criterio.")]
        public float waterHeight = -999f;
        [Tooltip("Si está en [0,1], ignora waterHeight y usa este valor como fracción del rango terreno: 0.25 = agua hasta 25% del rango (depresiones). -1 = usar waterHeight en world units.")]
        [Range(-1f, 1f)] public float waterHeightRelative = -1f;
        public float maxSlope = 15f;
        [Tooltip("Altura máxima del relieve. Valores bajos = terreno más plano (estilo AoE2).")]
        public float heightMultiplier = 8f;
        [Range(0.0001f, 0.05f)] public float noiseScale = 0.02f;
        [Range(1, 6)] public int noiseOctaves = 3;
        [Range(0f, 1f)] public float noisePersistence = 0.5f;
        [Range(1f, 4f)] public float noiseLacunarity = 2f;
        [Range(0f, 1f), Tooltip("1 = casi plano, 0 = relieve completo. Recomendado ~0.6 para estilo AoE2.")]
        public float terrainFlatness = 0.6f;
        public bool alignTerrainToGrid = true;

        [Header("Terrain Textures (opcional, estilo AoE2)")]
        [Tooltip("Activa para pintar el terreno por altura. Si lo activas sin asignar layers puede afectar el NavMesh/movimiento.")]
        public bool paintTerrainByHeight = false;
        [Tooltip("Asigna Terrain Layers para pintar por altura (solo si paintTerrainByHeight está activado).")]
        public TerrainLayer grassLayer;
        public TerrainLayer dirtLayer;
        public TerrainLayer rockLayer;
        [Tooltip("Tamaño de repetición de la textura Grass en unidades mundo (X,Z). Valores mayores = menos repetición. 0 = usar el del Terrain Layer. Si la hierba se ve muy repetitiva, prueba 50–80 en X y Z.")]
        public Vector2 grassTileSize = new Vector2(0f, 0f);
        [Tooltip("Igual que Grass; 0 = usar el del layer.")]
        public Vector2 dirtTileSize = new Vector2(0f, 0f);
        [Tooltip("Igual que Grass; 0 = usar el del layer.")]
        public Vector2 rockTileSize = new Vector2(0f, 0f);
        [Tooltip("Para arena en orillas; 0 = usar el del layer.")]
        public Vector2 sandTileSize = new Vector2(0f, 0f);
        [Header("Porcentaje del mapa por textura (grass/dirt/rock) — debe sumar 100")]
        [Tooltip("% del terreno que será hierba (zonas bajas). Ej: 60 = 60% grass.")]
        [Range(0, 100)] public int grassPercent = 60;
        [Tooltip("% del terreno que será tierra/dirt (zonas medias). Ej: 20 = 20% dirt.")]
        [Range(0, 100)] public int dirtPercent = 20;
        [Tooltip("% del terreno que será roca (zonas altas). Ej: 20 = 20% rock.")]
        [Range(0, 100)] public int rockPercent = 20;
        [Tooltip("Ancho de transición entre capas (0.02–0.15 típico).")]
        [Range(0.02f, 0.25f)] public float textureBlendWidth = 0.08f;

        [Header("Arena (orillas de lagos y ríos)")]
        [Tooltip("Terrain Layer para arena/sand en las orillas. Opcional.")]
        public TerrainLayer sandLayer;
        [Tooltip("Ancho de la franja de arena desde la orilla (en celdas del grid).")]
        [Range(1, 6)] public int sandShoreCells = 3;

        [Header("NavMesh (opcional)")]
        public NavMeshSurface navMeshSurface;
        public bool rebuildNavMeshOnGenerate = true;

        [Header("Agua (visual)")]
        [Tooltip("Offset en Y sobre waterHeight para que la malla no z-fightee con el terreno. En world units.")]
        public float waterSurfaceOffset = 0.05f;
        [Tooltip("Mostrar malla de agua. Se regenera al generar el mapa.")]
        public bool showWater = true;
        [Tooltip("Modo de malla: Chunks = varios meshes por zona (mejor culling). FullPlaneIntersect = un rectángulo del tamaño del mapa a waterHeight, con geometría solo donde el terreno está por debajo (intersección).")]
        public WaterMeshMode waterMeshMode = WaterMeshMode.FullPlaneIntersect;
        [Tooltip("Material de la malla de agua (ej. MAT_Water, URP Lit o Unlit). Si no asignas, se usa un material por defecto.")]
        public Material waterMaterial;
        [Tooltip("Transparencia del agua (0.5–1). Valores < 1 permiten ver la arena bajo el agua. Usado por el generador definitivo.")]
        [Range(0.5f, 1f)] public float waterAlpha = 0.88f;
        [Tooltip("Tamaño de chunk en celdas (solo si waterMeshMode = Chunks). Menor = más meshes, mejor culling.")]
        public int waterChunkSize = 32;
        [Tooltip("Si >= 0, el agua usa esta capa. Por defecto (-1) se usa capa 0 (Default) para que se vea en Game view.")]
        public int waterLayerOverride = -1;

        [Header("Players")]
        [Range(2, 4)] public int playerCount = 2;
        public float spawnEdgePadding = 20f;
        public float minPlayerDistance2p = 120f;
        public float minPlayerDistance4p = 100f;
        public float spawnFlatRadius = 8f;
        [Tooltip("Pendiente máxima permitida en spawns. Debe coincidir con Max Slope del NavMesh (p. ej. 60).")]
        public float maxSlopeAtSpawn = 60f;
        public float waterExclusionRadius = 12f;
        public bool flattenSpawnAreas = true;
        public float flattenRadius = 15f;

        [Header("Town Center")]
        public BuildingSO townCenterSO;
        public GameObject townCenterPrefabOverride;
        public float tcClearRadius = 6f;
        [Tooltip("Ajuste vertical extra del Town Center tras alinearlo al terreno (por si el pivot/modelo necesita corrección fina).")]
        public float townCenterSpawnYOffset = 0f;

        [Header("Resources Prefabs")]
        public GameObject treePrefab;
        public GameObject berryPrefab;
        public GameObject animalPrefab;
        public GameObject goldPrefab;
        public GameObject stonePrefab;
        [Header("Recursos: Layer y apariencia")]
        [Tooltip("Nombre del layer que usa RTSOrderController.resourceMask para detectar recursos al clic. Por defecto \"Resource\".")]
        public string resourceLayerName = "Resource";
        [Tooltip("Opcional: material que se aplica a todas las piedras al colocarlas. Si las variantes (rocas del pack) se ven blancas/sin textura, asigna aquí tu material de piedra (ej. MAT_Stone) para que todas usen el mismo aspecto.")]
        public Material stoneMaterialOverride;
        [Tooltip("Opcional: [0]=tronco, [1]=follaje. Se asigna por nombre del objeto (Trunk/Leaves, Tronco/Hojas, etc.). Para que las hojas no se vean blancas, el material de follaje debe usar un shader con Alpha Clip o Cutout (ej. Nature/Leaves o URP con Alpha Clip).")]
        public Material[] treeMaterialOverrides;
        [Tooltip("Rotación al colocar árboles (Euler, grados). Si el modelo sale tumbado o con el eje mal, ajusta aquí (ej. X=90 para levantarlo).")]
        public Vector3 treePlacementRotation = Vector3.zero;
        [Tooltip("Añade rotación Y aleatoria (0–360°) a cada recurso colocado para que el mapa se vea más dinámico.")]
        public bool randomRotationPerResource = true;

        [Header("Variantes (opcional, 5+ modelos por tipo recomendado)")]
        [Tooltip("Varias formas de árbol/madera. Cada variante debe tener ResourceNode+Collider o se añaden al colocar.")]
        public GameObject[] treePrefabVariants;
        [Tooltip("Varias formas de baya/comida. Cada variante debe tener ResourceNode+Collider o se añaden al colocar.")]
        public GameObject[] berryPrefabVariants;
        [Tooltip("Varias formas de animal/comida. Cada variante debe tener ResourceNode+Collider o se añaden al colocar.")]
        public GameObject[] animalPrefabVariants;
        [Tooltip("Varias formas de oro. Cada variante debe tener ResourceNode+Collider o se añaden al colocar.")]
        public GameObject[] goldPrefabVariants;
        [Tooltip("Varias formas de piedra/roca. Cada variante debe tener ResourceNode+Collider o se añaden al colocar.")]
        public GameObject[] stonePrefabVariants;

        [Header("Resource Rings")]
        public Vector2 ringNear = new Vector2(6f, 12f);
        public Vector2 ringMid = new Vector2(12f, 20f);
        public Vector2 ringFar = new Vector2(30f, 50f);

        [Header("Resource Counts")]
        public Vector2Int nearTrees = new Vector2Int(8, 12);
        public Vector2Int midTrees = new Vector2Int(12, 20);
        public Vector2Int berries = new Vector2Int(6, 8);
        public Vector2Int animals = new Vector2Int(2, 4);
        public Vector2Int goldSafe = new Vector2Int(6, 8);
        public Vector2Int stoneSafe = new Vector2Int(4, 6);
        public Vector2Int goldFar = new Vector2Int(8, 12);

        [Header("Recursos en el resto del mapa")]
        [Tooltip("Árboles a repartir por todo el mapa (fuera del radio de los spawns).")]
        public Vector2Int globalTrees = new Vector2Int(80, 120);
        [Tooltip("Piedra a repartir por el mapa (fuera de spawns).")]
        public Vector2Int globalStone = new Vector2Int(8, 14);
        [Tooltip("Oro a repartir por el mapa (fuera de spawns).")]
        public Vector2Int globalGold = new Vector2Int(10, 16);
        [Tooltip("Animales (comida) a repartir por el mapa (fuera de spawns). Requiere Animal Prefab o variantes asignados.")]
        public Vector2Int globalAnimals = new Vector2Int(8, 20);
        [Tooltip("Radio alrededor de cada spawn dentro del cual NO se colocan recursos globales (para no solapar con los del Town Center).")]
        public float globalExcludeRadius = 55f;

        [Header("Clustering de árboles (bosques)")]
        [Tooltip("Si está activo, los árboles globales se agrupan en bosques densos + algunos sueltos. Requiere preset o valores manuales debajo.")]
        public bool forestClustering = true;
        [Range(0f, 1f), Tooltip("Densidad dentro de cada bosque: 0 = disperso, 1 = muy denso.")]
        public float clusterDensity = 0.6f;
        [Tooltip("Mínimo de árboles por bosque.")]
        public int clusterMinSize = 15;
        [Tooltip("Máximo de árboles por bosque.")]
        public int clusterMaxSize = 40;

        [Header("Agua (preset / generador definitivo)")]
        [Tooltip("Número de ríos (usado por el generador cuando no hay MapGenConfig asignado o se crea desde preset).")]
        public int riverCount = 3;
        [Tooltip("Número de lagos.")]
        public int lakeCount = 2;
        [Tooltip("Máximo de celdas por lago (flood fill).")]
        public int maxLakeCells = 800;

        [Header("Fairness")]
        [Tooltip("Mínimo deseado de árboles por jugador (solo aviso; los recursos ya colocados no se borran). Con valores por defecto se colocan ~20–32. Pon 15–20 para evitar el aviso.")]
        public int minWoodTrees = 15;
        public int minGoldNodes = 6;
        public int minStoneNodes = 4;
        public int minFoodValue = 8;
        public int maxResourceRetries = 5;

        [Header("Debug")]
        [Tooltip("Imprime logs informativos del generador (recomendado OFF en builds). Warnings/Errors siempre se muestran.")]
        public bool debugLogs = false;
        public bool drawGizmos = true;
        public Color spawnColor = Color.cyan;
        public Color ringNearColor = new Color(0f, 1f, 0f, 0.4f);
        public Color ringMidColor = new Color(1f, 1f, 0f, 0.4f);
        public Color ringFarColor = new Color(1f, 0.5f, 0f, 0.4f);

        [Header("Generador Definitivo (ÚNICO)")]
        [Tooltip("Config del Generador Definitivo. Si no asignas, se crea uno en runtime desde los campos de este componente (grid, seed, playerCount, etc.).")]
        public MapGenConfig definitiveMapGenConfig;

        [Header("Grilla visual")]
        [Tooltip("Mostrar cuadrícula en vista Scene (sobre el terreno).")]
        public bool showGridInScene = true;
        [Tooltip("Mostrar cuadrícula en Game view.")]
        public bool showGridInGameView = true;
        [Range(0.02f, 0.5f), Tooltip("Transparencia de las líneas (bajo = muy tenue).")]
        public float gridLineAlpha = 0.06f;
        [Tooltip("Altura sobre el terreno. Si la grilla se ve cortada por cerros, sube a 0.15–0.35 o usa un material con ZTest Always (Unlit/GridAlwaysOnTop).")]
        [Range(0.01f, 0.5f)] public float gridHeightOffset = 0.2f;
        [Tooltip("Material con ZTest Always (ej. Unlit/GridAlwaysOnTop) para que la grilla no se oculte tras el terreno. Opcional: Unlit/Color en URP.")]
        public Material gridLineMaterialOverride;
        [Tooltip("Segmentar líneas: cada línea sigue el relieve (más vértices, grilla pegada al terreno). Si desactivado, líneas rectas (pueden quedar tapadas).")]
        public bool gridSegmentFollowTerrain = false;

        MapGrid _grid;
        Material _gridLineMat;
        static readonly int s_GridLinesLimit = 4096;
        System.Random _rng;
        readonly List<Vector3> _spawns = new();
        readonly List<Vector3> _townCenterPositions = new();
        
        struct TownCenterReservation
        {
            public Vector2 centerWorldXZ;
            public Vector2Int min;
            public Vector2Int size;
        }
        
        // Reservas temporales alrededor de TCs para evitar colocar recursos.
        // Importante: NO deben quedarse como "occupied" permanentemente porque el A* evita occupied y aleja a las unidades.
        readonly List<TownCenterReservation> _townCenterReservations = new();
        Transform _waterRoot;

        public MapGrid GetGrid() => _grid;
        public System.Random GetRng() => _rng;
        public float SampleHeight(Vector3 world)
        {
            if (terrain == null) return world.y;
            return terrain.SampleHeight(world) + terrain.transform.position.y;
        }
        public Vector3 SnapToGrid(Vector3 world)
        {
            float size = _grid != null ? _grid.cellSize : 1f;
            world.x = Mathf.Round(world.x / size) * size;
            world.z = Mathf.Round(world.z / size) * size;
            return world;
        }
        void Log(string msg)
        {
            if (debugLogs) Debug.Log(msg);
        }
        bool HasAnyTreePrefab() { return treePrefab != null || HasAnyIn(treePrefabVariants); }
        bool HasAnyStonePrefab() { return stonePrefab != null || HasAnyIn(stonePrefabVariants); }
        bool HasAnyGoldPrefab() { return goldPrefab != null || HasAnyIn(goldPrefabVariants); }
        bool HasAnyBerryPrefab() { return berryPrefab != null || HasAnyIn(berryPrefabVariants); }
        bool HasAnyAnimalPrefab() { return animalPrefab != null || HasAnyIn(animalPrefabVariants); }
        static bool HasAnyIn(GameObject[] arr) { if (arr == null) return false; foreach (var p in arr) if (p != null) return true; return false; }

        void OnEnable()
        {
            CreateGridLineMaterial();
        }

        void OnDisable()
        {
            if (_gridLineMat != null && _gridLineMat != gridLineMaterialOverride)
            {
                if (Application.isPlaying) Destroy(_gridLineMat);
                else DestroyImmediate(_gridLineMat);
                _gridLineMat = null;
            }
        }

        void CreateGridLineMaterial()
        {
            if (_gridLineMat != null) return;
            if (gridLineMaterialOverride != null) { _gridLineMat = gridLineMaterialOverride; return; }
            var shader = Shader.Find("Unlit/GridAlwaysOnTop");
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                _gridLineMat = new Material(shader);
                if (shader.name == "Unlit/GridAlwaysOnTop" && _gridLineMat.HasProperty("_Color"))
                    _gridLineMat.SetColor("_Color", new Color(1f, 1f, 1f, gridLineAlpha));
            }
        }

        float GridSampleY(float worldX, float worldZ, float fallbackY)
        {
            if (terrain == null || terrain.terrainData == null) return fallbackY + gridHeightOffset;
            Vector3 worldPos = new Vector3(worldX, 0f, worldZ);
            float y = terrain.SampleHeight(worldPos) + terrain.transform.position.y;
            return y + gridHeightOffset;
        }

        void Awake()
        {
            // Desactivar todos los NavMeshAgent al inicio para evitar "Failed to create agent because it is not close enough to the NavMesh"
            // (el NavMesh se construye después; se re-activan en FixUnitsAfterNavMesh).
            var agents = FindObjectsByType<UnityEngine.AI.NavMeshAgent>(FindObjectsSortMode.None);
            foreach (var a in agents)
            {
                if (a != null && a.enabled)
                {
                    a.enabled = false;
                }
            }
            if (agents.Length > 0)
                Log($"RTSMapGenerator: {agents.Length} NavMeshAgent(s) desactivados hasta que el NavMesh esté listo.");
        }

        void Start()
        {
            if (navMeshSurface == null)
                navMeshSurface = FindFirstObjectByType<NavMeshSurface>();
            Generate();
        }

        public void Generate()
        {
            Log("=== Iniciando generación de mapa (Definitivo) ===");
            if (navMeshSurface == null)
                navMeshSurface = FindFirstObjectByType<NavMeshSurface>();

            if (_grid == null)
                _grid = GetComponent<MapGrid>();
            if (_grid == null)
                _grid = gameObject.AddComponent<MapGrid>();

            // 🟢 Aplicar preset si no es Custom
            ApplyMapPreset();

            // Pipeline legacy retirado: desde ahora el proyecto usa SOLO el Generador Definitivo.
            RunDefinitiveGenerate();
        }
        
        static Vector2Int ScaleVector2Int(Vector2Int v, float multiplier)
        {
            if (multiplier <= 0f) multiplier = 1f;
            return new Vector2Int(Mathf.RoundToInt(v.x * multiplier), Mathf.RoundToInt(v.y * multiplier));
        }

        /// <summary>Devuelve el layer o una copia con tileSize aplicado para reducir el efecto de repetición (textura no tileable).</summary>
        static TerrainLayer GetTerrainLayerWithTiling(TerrainLayer layer, Vector2 tileSize)
        {
            if (layer == null) return null;
            if (tileSize.x <= 0f && tileSize.y <= 0f) return layer;
            float sx = tileSize.x > 0f ? tileSize.x : layer.tileSize.x;
            float sy = tileSize.y > 0f ? tileSize.y : layer.tileSize.y;
            TerrainLayer clone = UnityEngine.Object.Instantiate(layer);
            clone.tileSize = new Vector2(sx, sy);
            return clone;
        }

        /// <summary>Aplica configuración del preset seleccionado (si no es Custom).</summary>
        void ApplyMapPreset()
        {
            if (mapPreset == MapPresetType.Custom) return;
            
            MapPreset preset = MapPresets.GetPreset(mapPreset);
            if (preset == null) return;
            
            // Aplicar configuración del preset
            terrainFlatness = preset.terrainFlatness;
            heightMultiplier = preset.heightMultiplier;
            globalTrees = ScaleVector2Int(preset.globalTrees, preset.resourceMultiplier);
            forestClustering = preset.forestClustering;
            clusterDensity = preset.clusterDensity;
            clusterMinSize = preset.clusterMinSize;
            clusterMaxSize = preset.clusterMaxSize;
            riverCount = preset.riverCount;
            lakeCount = preset.lakeCount;
            maxLakeCells = preset.maxLakeCells;

            Debug.Log($"🗺️ Preset aplicado: {preset.name} - {preset.description}");
        }

        void RunDefinitiveGenerate()
        {
            MapGenConfig config = definitiveMapGenConfig != null ? definitiveMapGenConfig : CreateRuntimeMapGenConfig();
            if (config == null) { Debug.LogError("RTSMapGenerator Definitive: no hay MapGenConfig."); return; }

            var generator = GetComponent<MapGenerator>();
            if (generator == null) generator = gameObject.AddComponent<MapGenerator>();
            generator.config = config;
            generator.terrain = terrain;
            generator.terrainGrassLayerOverride = grassLayer;
            generator.terrainDirtLayerOverride = dirtLayer;
            generator.terrainRockLayerOverride = rockLayer;
            generator.terrainGrassTileSize = grassTileSize;
            generator.terrainDirtTileSize = dirtTileSize;
            generator.terrainRockTileSize = rockTileSize;
            generator.terrainSandLayerOverride = sandLayer;
            generator.terrainSandTileSize = sandTileSize;
            generator.terrainSandShoreCells = sandShoreCells;

            if (!generator.Generate(config, terrain))
            {
                Debug.LogError("RTSMapGenerator Definitive: el Generador Definitivo falló (validación o reintentos).");
                return;
            }

            MapGeneratorBridge.SyncGridToMapGrid(generator.Grid, _grid);
            _spawns.Clear();
            int spawnCount = Mathf.Min(playerCount, generator.Cities != null ? generator.Cities.Count : 0);
            for (int i = 0; i < spawnCount; i++)
            {
                var city = generator.Cities[i];
                Vector3 w = generator.Grid.CellToWorldCenter(city.Center);
                w.y = SampleHeight(w);
                _spawns.Add(w);
            }
            if (_spawns.Count == 0 && generator.Cities != null && generator.Cities.Count > 0)
            {
                for (int i = 0; i < generator.Cities.Count; i++)
                {
                    var city = generator.Cities[i];
                    Vector3 w = generator.Grid.CellToWorldCenter(city.Center);
                    w.y = SampleHeight(w);
                    _spawns.Add(w);
                }
            }
            Log($"Definitive: {_spawns.Count} spawns desde ciudades.");

            _rng = new System.Random(config.seed);
            PlaceTownCenters();
            MoveExistingUnitsToTownCenters();
            // Definitivo: colocar recursos desde el grid definitivo (evita duplicar lógicas legacy).
            MapResourcePlacer.PlaceFromDefinitiveGrid(generator.Grid, this);
            // Además: dispersión global (árboles/piedra/oro) fuera de TCs, usando parámetros del RTSMapGenerator.
            MapResourcePlacer.PlaceGlobalOnly(_spawns, this);
            ReleaseTownCenterReservations();

            // Notificar cámara RTS (si existe) para que actualice bounds al tamaño del mapa generado.
            var camCtrl = FindFirstObjectByType<Project.Gameplay.RTSCameraController>();
            if (camCtrl != null) camCtrl.RefreshBoundsFromMap();

            StartCoroutine(RebuildNavMeshCoroutine());
            Log("=== Generación Definitiva completada ===");
        }

        MapGenConfig CreateRuntimeMapGenConfig()
        {
            MapGenConfig c = ScriptableObject.CreateInstance<MapGenConfig>();
            
            // 🟢 FUENTE ÚNICA DE VERDAD: GridConfig.gridSize
            if (gridConfig == null)
            {
                Debug.LogWarning("⚠️ RTSMapGenerator: gridConfig NO está asignado. Asigna 'GridConfig.asset' en el Inspector. Usando fallback: cellSize=2.5");
            }
            float cellSize = gridConfig != null ? gridConfig.gridSize : 2.5f;
            
            c.gridW = width;
            c.gridH = height;
            c.cellSizeWorld = cellSize;
            c.origin = centerAtOrigin ? new Vector3(-width * cellSize * 0.5f, 0f, -height * cellSize * 0.5f) : transform.position;
            c.seed = randomSeedOnPlay ? UnityEngine.Random.Range(1, int.MaxValue) : seed;
            c.maxRetries = 5;
            c.regionCount = 8;
            c.regionNoiseScale = noiseScale;
            c.waterHeight01 = 0.4f;
            c.riverCount = Mathf.Clamp(riverCount, 0, 8);
            c.lakeCount = Mathf.Clamp(lakeCount, 0, 6);
            c.maxLakeCells = Mathf.Max(100, maxLakeCells);
            c.cityCount = Mathf.Max(2, playerCount);
            c.minCityDistanceCells = 40;
            c.cityRadiusCells = 8;
            c.maxCitySlopeDeg = maxSlopeAtSpawn;
            c.cityWaterBufferCells = 2;
            c.roadWidthCells = 2;
            c.roadFlattenStrength = 0.8f;
            c.ringNear = new Vector2Int((int)ringNear.x, (int)ringNear.y);
            c.ringMid = new Vector2Int((int)ringMid.x, (int)ringMid.y);
            c.ringFar = new Vector2Int((int)ringFar.x, (int)ringFar.y);
            c.minWoodPerCity = minWoodTrees;
            c.minStonePerCity = minStoneNodes;
            c.minGoldPerCity = minGoldNodes;
            c.minFoodPerCity = minFoodValue;
            c.maxResourceRetries = maxResourceRetries;
            c.terrainHeightWorld = heightMultiplier;
            c.heightmapResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(width, height)) + 1, 33, 4097);
            c.paintTerrainByHeight = paintTerrainByHeight;
            c.grassLayer = grassLayer;
            c.dirtLayer = dirtLayer;
            c.rockLayer = rockLayer;
            int totalPct = grassPercent + dirtPercent + rockPercent;
            if (totalPct < 1) totalPct = 100;
            c.grassPercent01 = Mathf.Clamp01((float)grassPercent / totalPct);
            c.dirtPercent01 = Mathf.Clamp01((float)dirtPercent / totalPct);
            c.rockPercent01 = Mathf.Clamp01((float)rockPercent / totalPct);
            c.grassMaxHeight01 = c.grassPercent01;
            c.dirtMaxHeight01 = c.grassPercent01 + c.dirtPercent01;
            c.textureBlendWidth = textureBlendWidth;
            c.sandLayer = sandLayer;
            c.sandShoreCells = sandShoreCells;
            c.waterChunkSize = waterChunkSize;
            c.waterSurfaceOffset = waterSurfaceOffset;
            c.waterMaterial = waterMaterial;
            c.waterAlpha = waterAlpha;
            c.waterLayer = waterLayerOverride >= 0 ? waterLayerOverride : -1;
            
            // 🟢 Parámetros de Marching Squares (agua orgánica)
            c.waterRoundedEdges = true;
            c.waterEdgeSubdiv = 4;
            c.waterEdgeBlurIterations = 3;
            c.waterEdgeBlurRadius = 2;
            c.waterIsoLevel = 0.5f;
            c.waterMaskPostProcess = true;
            c.waterMaskSmoothIterations = 2;
            c.waterMaskSmoothThreshold = 5;
            c.waterMsMaxCornerSamples = 250000;
            
            return c;
        }

        /// <summary>Rango real del terreno en world Y (muestreando celdas). Para recomendar Water Height sin adivinar.</summary>
        void GetTerrainHeightRange(out float minWorld, out float maxWorld)
        {
            minWorld = 0f;
            maxWorld = heightMultiplier;
            if (_grid == null || !_grid.IsReady || terrain == null) return;
            float minH = float.MaxValue;
            float maxH = float.MinValue;
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    Vector3 w = _grid.CellToWorld(new Vector2Int(x, z));
                    float h = SampleHeight(w);
                    minH = Mathf.Min(minH, h);
                    maxH = Mathf.Max(maxH, h);
                }
            }
            if (minH <= maxH) { minWorld = minH; maxWorld = maxH; }
        }

        void BakePassability()
        {
            if (_grid == null || !_grid.IsReady)
            {
                Debug.LogWarning("BakePassability: Grid no está listo");
                return;
            }

            GetTerrainHeightRange(out float minHeightWorld, out float maxHeightWorld);
            if (waterHeightRelative >= 0f && waterHeightRelative <= 1f)
            {
                waterHeight = Mathf.Lerp(minHeightWorld, maxHeightWorld, waterHeightRelative);
                Log($"Water Height (relativo {waterHeightRelative:P0}): {waterHeight:F2} world units (rango terreno {minHeightWorld:F2}–{maxHeightWorld:F2}).");
            }
            else if (waterHeight > -998f)
                Log($"Rango terreno (world Y): min={minHeightWorld:F2}, max={maxHeightWorld:F2}. Sugerencia ~25% agua: Water Height = {Mathf.Lerp(minHeightWorld, maxHeightWorld, 0.25f):F2} (o usa Water Height Relative = 0.25).");

            int blockedCount = 0;
            int waterCount = 0;
            int totalCells = width * height;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Vector2Int c = new Vector2Int(x, y);
                    Vector3 w = _grid.CellToWorld(c);
                    float h = terrain != null ? SampleHeight(w) : 0f;
                    float slope = terrain != null ? SampleSlope(w) : 0f;

                    bool isWater = waterHeight > -998f && h < waterHeight;
                    if (isWater) waterCount++;
                    _grid.SetWater(c, isWater);
                    bool blocked = slope > maxSlope;
                    _grid.SetBlocked(c, blocked);
                    if (isWater)
                        _grid.SetTerrainCost(c, 5f);

                    if (blocked) blockedCount++;
                }
            }

            if (waterHeight > -998f)
                Log($"BakePassability: {waterCount} celdas de agua (altura < {waterHeight:F1}), {blockedCount}/{totalCells} bloqueadas ({(blockedCount * 100f / totalCells):F1}%)");
            else
                Log($"BakePassability: {blockedCount}/{totalCells} celdas bloqueadas ({(blockedCount * 100f / totalCells):F1}%). Agua desactivada (Water Height negativo).");
        }

        void GenerateWaterMesh()
        {
            if (_waterRoot != null)
            {
                if (Application.isPlaying) Destroy(_waterRoot.gameObject);
                else DestroyImmediate(_waterRoot.gameObject);
                _waterRoot = null;
            }

            if (!showWater || _grid == null || !_grid.IsReady)
                return;

            if (waterHeight <= -998f)
            {
                Log("Agua: sin malla (Water Height está en -999 = desactivado). Para ver lagos/ríos: pon Water Height en world units, ej. 3–4 si Height Multiplier = 8.");
                return;
            }

            if (waterMeshMode == WaterMeshMode.FullPlaneIntersect)
            {
                GenerateWaterMeshFullPlaneIntersect();
                return;
            }

            GenerateWaterMeshChunks();
        }

        /// <summary>Un rectángulo del tamaño del mapa a waterHeight; geometría solo donde terreno &lt; waterHeight (intersección con el mapa). Un solo mesh.</summary>
        void GenerateWaterMeshFullPlaneIntersect()
        {
            float cellSize = _grid.cellSize;
            float y = waterHeight + waterSurfaceOffset;
            int gridW = _grid.width;
            int gridH = _grid.height;
            Vector3 origin = _grid.origin;

            // Mallado del rectángulo: (gridW+1) x (gridH+1) vértices alineados al grid
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

            // Índice del vértice en (gx, gz) de la grilla de vértices
            int VertexIndex(int vx, int vz) => vz * vertsW + vx;

            // Triángulos solo donde hay agua (intersección con el mapa)
            var tris = new List<int>();
            for (int gz = 0; gz < gridH; gz++)
            {
                for (int gx = 0; gx < gridW; gx++)
                {
                    if (!_grid.IsWater(new Vector2Int(gx, gz))) continue;

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
                return;
            }

            _waterRoot = new GameObject("Water").transform;
            _waterRoot.SetParent(null);
            _waterRoot.position = Vector3.zero;
            _waterRoot.rotation = Quaternion.identity;
            _waterRoot.localScale = Vector3.one;
            _waterRoot.gameObject.SetActive(true);
            int waterLayer = waterLayerOverride >= 0 ? Mathf.Clamp(waterLayerOverride, 0, 31) : 0;
            _waterRoot.gameObject.layer = waterLayer;

            var mesh = new Mesh();
            mesh.name = "Water_FullPlaneIntersect";
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            if (verts.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Water_FullPlaneIntersect");
            go.transform.SetParent(_waterRoot, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = waterLayer;
            go.SetActive(true);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            Material mat = GetMaterialForWaterMesh(waterMaterial);
            if (mat == null) mat = GetDefaultWaterMaterial();
            if (mat == null) mat = GetFallbackWaterMaterialFromQuad();
            mr.sharedMaterial = mat != null ? mat : GetOrCreatePinkFallback();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = true;
            mr.renderingLayerMask = unchecked((uint)-1);

            if (waterMaterial == null)
                Debug.LogWarning("RTSMapGenerator: waterMaterial no asignado; se usa material por defecto. Crea MAT_Water y asígnalo en Agua (visual).");
            Log($"Water (FullPlaneIntersect): 1 mesh, {verts.Count} vértices, {tris.Count / 3} triángulos, capa '{LayerMask.LayerToName(waterLayer)}'.");
            StartCoroutine(EnsureWaterVisibleNextFrame(waterLayer));
        }

        void GenerateWaterMeshChunks()
        {
            float cellSize = _grid.cellSize;
            float half = cellSize * 0.5f;
            float y = waterHeight + waterSurfaceOffset;
            int chunkSize = Mathf.Max(1, waterChunkSize);
            int gridW = _grid.width;
            int gridH = _grid.height;

            _waterRoot = new GameObject("Water").transform;
            _waterRoot.SetParent(null);
            _waterRoot.position = Vector3.zero;
            _waterRoot.rotation = Quaternion.identity;
            _waterRoot.localScale = Vector3.one;
            _waterRoot.gameObject.SetActive(true);
            int waterLayer = waterLayerOverride >= 0 ? Mathf.Clamp(waterLayerOverride, 0, 31) : 0;
            _waterRoot.gameObject.layer = waterLayer;

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
                            if (!_grid.IsWater(c)) continue;

                            Vector3 center = _grid.CellToWorld(c);
                            center.y = y;

                            Vector3 v0 = center + new Vector3(-half, 0f, -half);
                            Vector3 v1 = center + new Vector3(half, 0f, -half);
                            Vector3 v2 = center + new Vector3(half, 0f, half);
                            Vector3 v3 = center + new Vector3(-half, 0f, half);

                            v0 = _waterRoot.InverseTransformPoint(v0);
                            v1 = _waterRoot.InverseTransformPoint(v1);
                            v2 = _waterRoot.InverseTransformPoint(v2);
                            v3 = _waterRoot.InverseTransformPoint(v3);

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

                    var go = new GameObject($"WaterChunk_{cx}_{cz}");
                    go.transform.SetParent(_waterRoot, false);
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                    go.layer = waterLayer;
                    go.SetActive(true);

                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = mesh;

                    if (chunkCount == 0)
                        Log($"Water chunk (primer mesh): {verts.Count} vértices, {tris.Count / 3} triángulos, bounds size = {mesh.bounds.size}.");

                    var mr = go.AddComponent<MeshRenderer>();
                    Material mat = GetMaterialForWaterMesh(waterMaterial);
                    if (mat == null) mat = GetDefaultWaterMaterial();
                    if (mat == null) mat = GetFallbackWaterMaterialFromQuad();
                    mr.sharedMaterial = mat != null ? mat : GetOrCreatePinkFallback();
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.enabled = true;
                    mr.renderingLayerMask = unchecked((uint)-1);

                    chunkCount++;
                }
            }

            if (waterMaterial == null && chunkCount > 0)
                Debug.LogWarning("RTSMapGenerator: waterMaterial no asignado; se usa material por defecto. Crea MAT_Water (URP Lit/Unlit) y asígnalo en Agua (visual).");

            if (chunkCount == 0)
                Debug.LogWarning("Agua: 0 celdas bajo el nivel. Sube Water Height (ej. 30–50% de Height Multiplier: si es 8, prueba 3–4) para que las depresiones del terreno sean agua.");
            else
            {
                string layerName = LayerMask.LayerToName(waterLayer);
                Log($"Water mesh: {chunkCount} chunks, capa = '{layerName}' ({waterLayer}).");
                StartCoroutine(EnsureWaterVisibleNextFrame(waterLayer));
            }
        }

        /// <summary>Aplica el Culling Mask a todas las cámaras al frame siguiente (por si Main Camera no está activa en Start).</summary>
        IEnumerator EnsureWaterVisibleNextFrame(int waterLayer)
        {
            yield return null; // Un frame para que la cámara del Game view esté activa
            int waterBit = 1 << waterLayer;
            var cams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var cam in cams)
            {
                if (cam == null || !cam.enabled) continue;
                if ((cam.cullingMask & waterBit) == 0)
                {
                    cam.cullingMask |= waterBit;
                    Log($"Water: Cámara '{cam.name}' ahora incluye la capa del agua en Culling Mask (Game view).");
                }
            }
        }

        static Material s_defaultWaterMat;
        static Material s_fallbackQuadWaterMat;
        static Material s_pinkFallback; // Nunca null una vez creado; para que el agua siempre tenga material
        static Material s_waterMaterialInstance; // Instancia de waterMaterial con queue 2001 para Game view
        static Material s_waterMaterialInstanceSource; // material del que se creó la instancia
        static Material GetMaterialForWaterMesh(Material assigned)
        {
            if (assigned != null)
            {
                // Instancia para no modificar el asset; respetar la cola del material (Transparent en URP suele ser 3000+)
                if (s_waterMaterialInstance == null || s_waterMaterialInstanceSource != assigned)
                {
                    s_waterMaterialInstanceSource = assigned;
                    s_waterMaterialInstance = new Material(assigned);
                    // Solo forzar 2001 si el material es opaco; si es Transparent (3000+) dejar su cola para Game view
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
            // URP primero: en proyectos URP los Built-in (Internal-Colored) NO se dibujan en Game view
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

        /// <summary>Fallback cuando Shader.Find falla en Play/Game view: material del Quad del pipeline actual.</summary>
        static Material GetFallbackWaterMaterialFromQuad()
        {
            if (s_fallbackQuadWaterMat != null) return s_fallbackQuadWaterMat;
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            if (quad == null) return null;
            quad.SetActive(false); // No mostrar el quad ni un frame
            var mr = quad.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null) { UnityEngine.Object.Destroy(quad); return null; }
            s_fallbackQuadWaterMat = new Material(mr.sharedMaterial);
            UnityEngine.Object.Destroy(quad);
            if (s_fallbackQuadWaterMat.HasProperty("_BaseColor")) s_fallbackQuadWaterMat.SetColor("_BaseColor", new Color(0.2f, 0.45f, 0.85f, 1f));
            else if (s_fallbackQuadWaterMat.HasProperty("_Color")) s_fallbackQuadWaterMat.SetColor("_Color", new Color(0.2f, 0.45f, 0.85f, 1f));
            s_fallbackQuadWaterMat.renderQueue = 2001;
            // Nota: método static → no loggea para evitar CS0120 y spam. Si necesitas debug, activa debugLogs y loggea desde contexto de instancia.
            return s_fallbackQuadWaterMat;
        }

        /// <summary>Material que nunca es null; URP primero para que se vea en Game view.</summary>
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

        void GenerateHeightmap()
        {
            if (terrain == null)
            {
                Debug.LogWarning("GenerateHeightmap: No hay Terrain asignado");
                return;
            }
            
            if (_grid == null || !_grid.IsReady)
            {
                Debug.LogWarning("GenerateHeightmap: Grid no está listo");
                return;
            }

            float cellSize = _grid.cellSize;
            int maxSize = Mathf.Max(width, height);
            int resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(maxSize) + 1, 33, 4097);

            var data = terrain.terrainData;
            if (data == null)
            {
                Log("Creando nuevo TerrainData");
                data = new TerrainData();
                terrain.terrainData = data;
            }

            data.heightmapResolution = resolution;
            data.size = new Vector3(width * cellSize, heightMultiplier, height * cellSize);

            // Asignar TerrainLayers ANTES del heightmap para que Unity inicialice alphamap correctamente (estilo ChatGPT/AoE2)
            if (paintTerrainByHeight && (grassLayer != null || dirtLayer != null || rockLayer != null))
            {
                if (grassLayer != null && rockLayer != null && dirtLayer == null)
                    Debug.LogWarning("RTSMapGenerator: Tienes Grass y Rock pero no Dirt. Asigna también 'Dirt Layer' en Terrain Textures para ver las 3 zonas (hierba / tierra / roca).");
                var layersList = new List<TerrainLayer>();
                if (grassLayer != null) layersList.Add(GetTerrainLayerWithTiling(grassLayer, grassTileSize));
                if (dirtLayer != null) layersList.Add(GetTerrainLayerWithTiling(dirtLayer, dirtTileSize));
                if (rockLayer != null) layersList.Add(GetTerrainLayerWithTiling(rockLayer, rockTileSize));
                data.terrainLayers = layersList.ToArray();
                Log($"RTSMapGenerator: TerrainLayers asignados al TerrainData: {data.terrainLayers.Length} (grass/dirt/rock). GrassLayer={((grassLayer != null) ? grassLayer.name : "null")}");
            }
            
            Log($"Generando heightmap: resolution={resolution}, size={data.size}, noiseScale={noiseScale}, octaves={noiseOctaves}");

            float[,] heights = new float[resolution, resolution];
            Vector2 seedOffset = new Vector2(_rng.Next(-100000, 100000), _rng.Next(-100000, 100000));
            
            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float nx = (float)x / (resolution - 1);
                    float ny = (float)y / (resolution - 1);
                    float worldX = nx * width;
                    float worldY = ny * height;

                    float h = FractalNoise(worldX, worldY, seedOffset);
                    // Aplanar: 1 = casi plano, 0 = relieve completo
                    float flat = Mathf.Clamp01(terrainFlatness);
                    h = 0.5f + (1f - flat) * (h - 0.5f);
                    heights[y, x] = Mathf.Clamp01(h);
                    
                    minHeight = Mathf.Min(minHeight, heights[y, x]);
                    maxHeight = Mathf.Max(maxHeight, heights[y, x]);
                }
            }

            data.SetHeights(0, 0, heights);
            Log($"Heightmap generado: min={minHeight:F3}, max={maxHeight:F3}, flatness={terrainFlatness:F2}");

            if (paintTerrainByHeight && (grassLayer != null || dirtLayer != null || rockLayer != null))
            {
                PaintTerrainByHeight(data, heights, resolution);
                if (terrain != null)
                {
                    terrain.terrainData = data;
                    var tc = terrain.GetComponent<TerrainCollider>();
                    if (tc != null) tc.terrainData = data;
                    EnsureTerrainMaterialSupportsLayers();
                    // Forzar actualización del material para que muestre el alphamap (splat)
                    TerrainExtensions.UpdateGIMaterials(terrain);
                    StartCoroutine(RefreshTerrainVisualNextFrame(data));
                }
            }
        }

        IEnumerator RefreshTerrainVisualNextFrame(TerrainData data)
        {
            yield return null;
            if (terrain == null || data == null) yield break;
            terrain.terrainData = data;
            var tc = terrain.GetComponent<TerrainCollider>();
            if (tc != null) tc.terrainData = data;
            TerrainExtensions.UpdateGIMaterials(terrain);
            // Forzar que el renderer vuelva a leer TerrainData/alphamap (a veces necesario en URP en runtime)
            terrain.enabled = false;
            terrain.enabled = true;
        }

        void EnsureTerrainMaterialSupportsLayers()
        {
            if (terrain == null) return;
            // El Terrain debe usar un material que soporte Terrain Layers (splat) para que se vean las texturas.
            // Solo consideramos válido un shader que sea explícitamente Terrain/Lit de URP o equivalente.
            var mat = terrain.materialTemplate;
            bool needsTerrainShader = mat == null || mat.shader == null;
            if (!needsTerrainShader)
            {
                string shaderName = mat.shader.name;
                bool isUrpTerrainLit = shaderName.Contains("Terrain/Lit");
                bool isTerrainStandard = shaderName.Contains("Terrain/Standard") || shaderName.Contains("Nature/Terrain");
                needsTerrainShader = !isUrpTerrainLit && !isTerrainStandard;
            }
            if (needsTerrainShader)
            {
                Shader terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
                if (terrainShader == null)
                    terrainShader = Shader.Find("Terrain/Lit");
                if (terrainShader == null)
                    terrainShader = Shader.Find("Nature/Terrain/Standard");
                if (terrainShader == null)
                    terrainShader = Shader.Find("Terrain/Standard");
                if (terrainShader != null)
                {
                    terrain.materialTemplate = new Material(terrainShader);
                    Log($"RTSMapGenerator: Se asignó material Terrain ({terrainShader.name}) para mostrar las capas de textura.");
                }
                else
                    Debug.LogWarning("RTSMapGenerator: No se encontró shader de Terrain. En el Terrain, asigna un material con shader Universal Render Pipeline → Terrain → Lit.");
            }
        }

        void PaintTerrainByHeight(TerrainData data, float[,] heights, int res)
        {
            var layers = new List<TerrainLayer>();
            if (grassLayer != null) layers.Add(GetTerrainLayerWithTiling(grassLayer, grassTileSize));
            if (dirtLayer != null) layers.Add(GetTerrainLayerWithTiling(dirtLayer, dirtTileSize));
            if (rockLayer != null) layers.Add(GetTerrainLayerWithTiling(rockLayer, rockTileSize));
            if (layers.Count == 0) return;

            data.terrainLayers = layers.ToArray();

            // Asegurar resolución del alphamap (algunos TerrainData tienen 0 por defecto)
            int desiredAlphamap = Mathf.Clamp(Mathf.Max(256, (res - 1) / 2), 16, 1024);
            if (data.alphamapWidth <= 0 || data.alphamapHeight <= 0)
            {
                try { data.alphamapResolution = desiredAlphamap; } catch { /* en algunas versiones es read-only */ }
            }
            int alphamapWidth = data.alphamapWidth;
            int alphamapHeight = data.alphamapHeight;
            if (alphamapWidth <= 0 || alphamapHeight <= 0)
            {
                Debug.LogWarning($"RTSMapGenerator: Alphamap inválido ({alphamapWidth}x{alphamapHeight}). Las texturas no se aplicarán. Revisa el Terrain Data del Terrain.");
                return;
            }

            int numLayers = layers.Count;
            float[,,] map = new float[alphamapHeight, alphamapWidth, numLayers];
            float blend = Mathf.Clamp(textureBlendWidth, 0.02f, 0.2f);

            int totalPct = grassPercent + dirtPercent + rockPercent;
            if (totalPct < 1) totalPct = 100;
            float grassMax = Mathf.Clamp01((float)grassPercent / totalPct);
            float dirtMax = Mathf.Clamp01((float)(grassPercent + dirtPercent) / totalPct);

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

            for (int y = 0; y < alphamapHeight; y++)
            {
                for (int x = 0; x < alphamapWidth; x++)
                {
                    float hx = (alphamapWidth > 1) ? (float)x / (alphamapWidth - 1) * (res - 1) : 0f;
                    float hy = (alphamapHeight > 1) ? (float)y / (alphamapHeight - 1) * (res - 1) : 0f;
                    int ix = Mathf.Clamp((int)hx, 0, res - 1);
                    int iy = Mathf.Clamp((int)hy, 0, res - 1);
                    float hRaw = heights[iy, ix];
                    float h = Mathf.Clamp01((hRaw - minH) / rangeH);

                    if (numLayers == 1)
                    {
                        map[y, x, 0] = 1f;
                    }
                    else if (numLayers == 2)
                    {
                        map[y, x, 0] = 1f - h;
                        map[y, x, 1] = h;
                    }
                    else
                    {
                        PaintThreeLayers(h, grassMax, dirtMax, blend,
                            out float g, out float d, out float r);
                        map[y, x, 0] = g;
                        map[y, x, 1] = d;
                        map[y, x, 2] = r;
                    }
                }
            }
            data.SetAlphamaps(0, 0, map);
            Log($"Terrain pintado con {numLayers} capa(s), alphamap {alphamapWidth}x{alphamapHeight} (grass {grassPercent}% / dirt {dirtPercent}% / rock {rockPercent}%).");
        }

        /// <summary>
        /// Pinta 3 capas (grass/dirt/rock) por altura con transición configurable.
        /// g + d + r = 1.
        /// </summary>
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

        float FractalNoise(float x, float y, Vector2 offset)
        {
            float amplitude = 1f;
            float frequency = 1f;
            float value = 0f;
            float max = 0f;

            for (int i = 0; i < noiseOctaves; i++)
            {
                float sx = (x + offset.x) * noiseScale * frequency;
                float sy = (y + offset.y) * noiseScale * frequency;
                float n = Mathf.PerlinNoise(sx, sy);
                value += n * amplitude;
                max += amplitude;
                amplitude *= noisePersistence;
                frequency *= noiseLacunarity;
            }

            if (max <= 0.0001f) return 0f;
            return value / max;
        }

        void GenerateSpawns()
        {
            float radius = Mathf.Min(width, height) * 0.5f - spawnEdgePadding;
            float minDistance = playerCount == 2 ? minPlayerDistance2p : minPlayerDistance4p;

            float baseAngle = (float)_rng.NextDouble() * 360f;
            float step = playerCount == 2 ? 180f : 90f;

            Log($"GenerateSpawns: radius={radius}, minDistance={minDistance}, baseAngle={baseAngle:F1}");

            for (int i = 0; i < playerCount; i++)
            {
                bool placed = false;
                
                // Intentar primero con el ángulo ideal
                float idealAngle = baseAngle + (step * i);
                
                // Intentar con diferentes radios y jitters
                for (int attempt = 0; attempt < 50 && !placed; attempt++)
                {
                    float jitter = (float)(_rng.NextDouble() * 20.0 - 10.0); // -10 a 10
                    float angle = idealAngle + jitter;
                    
                    // Variar el radio también para más flexibilidad
                    float radiusVariation = (float)(_rng.NextDouble() * 20.0 - 10.0);
                    float currentRadius = Mathf.Max(radius + radiusVariation, 50f);
                    
                    Vector3 candidate = PolarToWorld(currentRadius, angle);

                    if (IsSpawnValid(candidate, minDistance))
                    {
                        _spawns.Add(candidate);
                        Log($"✓ Spawn {i} colocado en: {candidate} (angle={angle:F1}°, radius={currentRadius:F1}, attempt={attempt + 1})");
                        placed = true;
                        break;
                    }
                }
                
                if (!placed)
                {
                    Debug.LogWarning($"✗ Spawn {i}: No se pudo encontrar ubicación ideal. Usando fallback...");
                    
                    // FALLBACK: Colocar el spawn de todas formas usando solo la distancia mínima
                    for (int fallbackAttempt = 0; fallbackAttempt < 20 && !placed; fallbackAttempt++)
                    {
                        float angle = idealAngle + (float)(_rng.NextDouble() * 40.0 - 20.0);
                        float currentRadius = radius * 0.8f; // Más cerca del centro
                        Vector3 candidate = PolarToWorld(currentRadius, angle);
                        
                        // Solo verificar distancia mínima, ignorar pendiente
                        bool validDistance = true;
                        for (int j = 0; j < _spawns.Count; j++)
                        {
                            if (Vector3.Distance(_spawns[j], candidate) < minDistance * 0.8f) // 80% de la distancia
                            {
                                validDistance = false;
                                break;
                            }
                        }
                        
                        if (validDistance)
                        {
                            _spawns.Add(candidate);
                            Debug.LogWarning($"⚠ Spawn {i} colocado con fallback en: {candidate} (puede no ser óptimo)");
                            placed = true;
                            break;
                        }
                    }
                    
                    if (!placed)
                    {
                        Debug.LogError($"✗✗✗ Spawn {i}: FALLO CRÍTICO - No se pudo colocar ni con fallback. El mapa NO funcionará correctamente.");
                    }
                }
            }
        }

        bool IsSpawnValid(Vector3 pos, float minDistance)
        {
            // Verificar distancia mínima con otros spawns
            for (int i = 0; i < _spawns.Count; i++)
            {
                if (Vector3.Distance(_spawns[i], pos) < minDistance)
                {
                    Log($"Spawn rechazado: muy cerca de otro spawn (distancia={Vector3.Distance(_spawns[i], pos):F1})");
                    return false;
                }
            }

            // Verificar que el área sea plana (solo si hay terreno)
            // Usar el mayor entre maxSlopeAtSpawn y maxSlope para coincidir con NavMesh (p. ej. 60°)
            float spawnSlopeLimit = Mathf.Max(maxSlopeAtSpawn, maxSlope);
            if (terrain != null && !IsAreaFlat(pos, spawnFlatRadius, spawnSlopeLimit))
            {
                Log($"Spawn rechazado en {pos}: área no plana");
                return false;
            }

            // Verificar altura del agua
            if (waterHeight > -998f && pos.y < waterHeight + 0.1f)
            {
                Log($"Spawn rechazado: bajo el agua (y={pos.y}, waterHeight={waterHeight})");
                return false;
            }

            return true;
        }

        void PlaceTownCenters()
        {
            _townCenterPositions.Clear();
            _townCenterReservations.Clear();
            
            if (_grid == null || !_grid.IsReady)
            {
                Debug.LogWarning("PlaceTownCenters: Grid no está listo");
                return;
            }
            
            if (townCenterSO == null && townCenterPrefabOverride == null)
            {
                Debug.LogWarning("PlaceTownCenters: No hay Town Center SO ni prefab asignado");
                return;
            }

            // Consistencia visual/escala:
            // - El TC construido por aldeano usa BuildingSO.prefab.
            // - El TC inicial del mapa debe usar el mismo prefab para evitar tamaños distintos.
            // townCenterPrefabOverride queda como fallback cuando no hay SO/prefab en SO.
            GameObject prefab = (townCenterSO != null && townCenterSO.prefab != null)
                ? townCenterSO.prefab
                : townCenterPrefabOverride;
            if (prefab == null)
            {
                Debug.LogError("PlaceTownCenters: El prefab del Town Center es null");
                return;
            }

            if (townCenterSO != null && townCenterSO.prefab != null && townCenterPrefabOverride != null && townCenterPrefabOverride != townCenterSO.prefab)
                Debug.LogWarning("PlaceTownCenters: townCenterPrefabOverride es distinto a townCenterSO.prefab. Se usará townCenterSO.prefab para mantener consistencia con el TC construido por aldeanos.");
            
            Vector2 size = townCenterSO != null ? townCenterSO.size : new Vector2(4, 4);
            Vector2Int footprint = new Vector2Int(Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y));
            
            Log($"PlaceTownCenters: footprint={footprint}, spawns={_spawns.Count}");

            int placedCount = 0;
            for (int i = 0; i < _spawns.Count; i++)
            {
                Vector3 center = SnapToGrid(_spawns[i]);
                Vector2Int cell = _grid.WorldToCell(center);
                Vector2Int min = new Vector2Int(cell.x - footprint.x / 2, cell.y - footprint.y / 2);

                // Si aplanamos los spawns, no requerir que el área sea pasable
                bool requirePassable = !flattenSpawnAreas;
                bool areaFree = _grid.IsAreaFreeRect(min, footprint, requirePassable);
                
                if (!areaFree && flattenSpawnAreas)
                {
                    // Forzar que el área esté libre si está aplanada
                    Log($"Town Center {i + 1}: Limpiando área bloqueada en spawn aplanado");
                    for (int x = 0; x < footprint.x; x++)
                    {
                        for (int y = 0; y < footprint.y; y++)
                        {
                            var c = new Vector2Int(min.x + x, min.y + y);
                            if (_grid.IsInBounds(c))
                                _grid.SetBlocked(c, false);
                        }
                    }
                    areaFree = _grid.IsAreaFreeRect(min, footprint, false);
                }

                if (areaFree)
                {
                    Vector3 world = _grid.CellToWorld(cell);
                    world.y = terrain != null ? SampleHeight(world) : 0f;
                    GameObject tc = Instantiate(prefab, world, Quaternion.identity);
                    AlignTownCenterToTerrain(tc);
                    EnsureWorldBarAnchor(tc);
                    SetLayerRecursive(tc.transform, tc.layer);
                    if (tc.GetComponent<BuildingSelectable>() == null)
                        tc.AddComponent<BuildingSelectable>();
                    tc.name = $"TownCenter_Player{i + 1}";
                    world = tc.transform.position;

                    // Si el prefab es Static, el bake de NavMesh incluye su collider con margen → las unidades no se acercan.
                    // Forzar no-static para que solo el NavMeshObstacle tallen en runtime (igual que edificios construidos después).
                    tc.isStatic = false;

                    // 🟢 NO ocupar celdas manualmente, BuildingInstance lo hará
                    // _grid.SetOccupiedRect(min, footprint, true);  // REMOVIDO

                    // Asegurar que el Town Center pueda crear aldeanos (ProductionBuilding + BuildingInstance)
                    if (townCenterSO != null)
                    {
                        var bi = tc.GetComponent<BuildingInstance>();
                        if (bi == null) bi = tc.AddComponent<BuildingInstance>();
                        bi.buildingSO = townCenterSO;
                        // 🟢 Ocupar celdas inmediatamente (no esperar a Start)
                        bi.OccupyCellsOnStart();

                        // Aplicar tamaño de obstáculo/collider ya (MapGrid está listo). Así las unidades pueden acercarse.
                        var buildingCtrl = tc.GetComponent<BuildingController>();
                        if (buildingCtrl != null) buildingCtrl.RefreshObstacleAndCollider();

                        var prod = tc.GetComponent<ProductionBuilding>();
                        if (prod == null)
                        {
                            prod = tc.AddComponent<ProductionBuilding>();
                            if (prod.spawnPoint == null)
                            {
                                var spawnObj = new GameObject("SpawnPoint");
                                spawnObj.transform.SetParent(tc.transform);
                                float safeScaleZ = Mathf.Max(0.001f, Mathf.Abs(tc.transform.lossyScale.z));
                                float sign = townCenterSO != null && townCenterSO.unitSpawnForwardSign >= 0f ? 1f : -1f;
                                spawnObj.transform.localPosition = new Vector3(0f, 0f, (3f * sign) / safeScaleZ);
                                prod.spawnPoint = spawnObj.transform;
                            }
                        }
                    }

                    // Limpiar alrededor del TC
                    _townCenterReservations.Add(new TownCenterReservation
                    {
                        centerWorldXZ = new Vector2(world.x, world.z),
                        min = min,
                        size = footprint
                    });
                    _grid.SetOccupiedCircle(new Vector2(world.x, world.z), tcClearRadius, true);
                    _townCenterPositions.Add(world);
                    placedCount++;
                    Log($"Town Center {i + 1} colocado en: {world}");
                }
                else
                {
                    Debug.LogWarning($"Town Center {i + 1}: No se pudo colocar, área no libre en cell={cell}, min={min}");
                }
            }
            
            Log($"Town Centers colocados: {placedCount}/{_spawns.Count}");
        }

        /// <summary>
        /// Alinea el Town Center al terreno usando la base visual/collider (no solo el pivote).
        /// Evita que modelos con pivot centrado queden enterrados o flotando.
        /// </summary>
        void AlignTownCenterToTerrain(GameObject townCenter)
        {
            if (townCenter == null) return;
            if (terrain == null)
            {
                if (Mathf.Abs(townCenterSpawnYOffset) > 0.0001f)
                {
                    var p = townCenter.transform.position;
                    p.y += townCenterSpawnYOffset;
                    townCenter.transform.position = p;
                }
                return;
            }

            Vector3 pivotPos = townCenter.transform.position;
            float terrainY = SampleHeight(pivotPos);
            float bottomY = float.MaxValue;
            bool foundBounds = false;

            var renderers = townCenter.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                string n = r.gameObject.name;
                if (n.Equals("DropAnchor", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("SpawnPoint", StringComparison.OrdinalIgnoreCase) ||
                    r.gameObject.GetComponent<Canvas>() != null)
                    continue;
                bottomY = Mathf.Min(bottomY, r.bounds.min.y);
                foundBounds = true;
            }

            // Fallback si el prefab no tiene renderers visibles.
            if (!foundBounds)
            {
                var colliders = townCenter.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider c = colliders[i];
                    if (c == null) continue;
                    bottomY = Mathf.Min(bottomY, c.bounds.min.y);
                    foundBounds = true;
                }
            }

            float delta = terrainY - (foundBounds ? bottomY : pivotPos.y);
            Vector3 pos = townCenter.transform.position;
            pos.y += delta + townCenterSpawnYOffset;
            townCenter.transform.position = pos;
        }

        void EnsureWorldBarAnchor(GameObject go)
        {
            if (go == null) return;

            Transform anchor = go.transform.Find("BarAnchor");
            if (anchor == null)
            {
                var created = new GameObject("BarAnchor");
                anchor = created.transform;
                anchor.SetParent(go.transform, false);
                anchor.localPosition = new Vector3(0f, 2f, 0f);
            }

            var health = go.GetComponent<Health>();
            if (health == null) health = go.GetComponentInChildren<Health>(true);
            if (health != null)
                health.SetBarAnchor(anchor);

            var settings = go.GetComponent<WorldBarSettings>();
            if (settings != null)
            {
                settings.barAnchor = anchor;
                settings.autoAnchorName = "BarAnchor";
                settings.useLocalOffsetOverride = true;
                settings.localOffset = Vector3.zero;
            }
        }

        static void SetLayerRecursive(Transform t, int layer)
        {
            if (t == null) return;
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i), layer);
        }

        /// <summary>
        /// Libera las reservas circulares usadas solo para evitar colocar recursos cerca de TCs.
        /// Mantiene la huella real del edificio como occupied.
        /// </summary>
        void ReleaseTownCenterReservations()
        {
            if (_grid == null || !_grid.IsReady) return;
            if (_townCenterReservations.Count == 0) return;

            for (int i = 0; i < _townCenterReservations.Count; i++)
            {
                var r = _townCenterReservations[i];
                _grid.SetOccupiedCircle(r.centerWorldXZ, tcClearRadius, false);
                _grid.SetOccupiedRect(r.min, r.size, true);
            }
        }

        void MoveExistingUnitsToTownCenters()
        {
            if (_townCenterPositions.Count == 0)
            {
                Debug.LogWarning("No hay Town Centers generados para mover unidades");
                return;
            }

            // Buscar todas las unidades con NavMeshAgent
            var allAgents = FindObjectsByType<UnityEngine.AI.NavMeshAgent>(FindObjectsSortMode.None);
            
            if (allAgents == null || allAgents.Length == 0)
            {
                Log("No hay unidades para mover");
                return;
            }

            Log($"Moviendo {allAgents.Length} unidades a {_townCenterPositions.Count} Town Centers...");

            // Distribuir unidades entre los Town Centers
            int unitsPerTC = Mathf.CeilToInt((float)allAgents.Length / _townCenterPositions.Count);
            int unitIndex = 0;

            for (int tcIndex = 0; tcIndex < _townCenterPositions.Count && unitIndex < allAgents.Length; tcIndex++)
            {
                Vector3 tcPos = _townCenterPositions[tcIndex];
                
                // Mover unidades a este Town Center
                for (int u = 0; u < unitsPerTC && unitIndex < allAgents.Length; u++, unitIndex++)
                {
                    var agent = allAgents[unitIndex];
                    
                    // Calcular offset en círculo alrededor del TC
                    float angle = (u / (float)unitsPerTC) * 360f * Mathf.Deg2Rad;
                    float radius = 8f;
                    Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                    Vector3 targetPos = tcPos + offset;
                    targetPos.y = SampleHeight(targetPos);

                    // Desactivar NavMeshAgent para mover la unidad; NO re-activar aquí:
                    // el NavMesh aún no está construido. Se re-activará en FixUnitsAfterNavMesh.
                    agent.enabled = false;
                    agent.transform.position = targetPos;

                    Log($"Unidad {agent.name} movida a Town Center {tcIndex + 1}: {targetPos}");
                }
            }

            Log($"✅ {unitIndex} unidades movidas a Town Centers");
        }

        Vector3 PolarToWorld(float radius, float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;
            p = SnapToGrid(p);
            
            // Samplear altura solo si el terreno existe y está listo
            if (terrain != null && terrain.terrainData != null)
            {
                p.y = SampleHeight(p);
            }
            else
            {
                p.y = 0f;
            }
            
            return p;
        }

        bool IsAreaFlat(Vector3 center, float radius, float maxSlopeAllowed)
        {
            if (terrain == null) return true;

            const int samples = 8;
            int failedSamples = 0;
            float maxSlopeFound = 0f;
            
            for (int i = 0; i < samples; i++)
            {
                float angle = (360f / samples) * i;
                Vector3 p = center + new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
                float slope = SampleSlope(p);
                maxSlopeFound = Mathf.Max(maxSlopeFound, slope);
                
                if (slope > maxSlopeAllowed)
                    failedSamples++;
            }
            
            // Permitir hasta 2 muestras fallidas de 8 (más tolerante)
            bool isFlat = failedSamples <= 2;
            
            if (!isFlat)
            {
                Log($"IsAreaFlat falló: {failedSamples}/8 muestras exceden pendiente. Max slope={maxSlopeFound:F1}°, permitido={maxSlopeAllowed:F1}°");
            }
            
            return isFlat;
        }

        float SampleSlope(Vector3 world)
        {
            if (terrain == null || terrain.terrainData == null)
                return 0f;
                
            var data = terrain.terrainData;
            
            // Verificar que el terreno tenga un tamaño válido
            if (data.size.x <= 0.01f || data.size.z <= 0.01f)
                return 0f;
                
            Vector3 local = world - terrain.transform.position;
            float nx = Mathf.Clamp01(local.x / data.size.x);
            float nz = Mathf.Clamp01(local.z / data.size.z);
            
            Vector3 normal = data.GetInterpolatedNormal(nx, nz);
            return Vector3.Angle(normal, Vector3.up);
        }

        void FlattenSpawnAreas()
        {
            if (terrain == null || terrain.terrainData == null)
                return;

            var data = terrain.terrainData;
            int resolution = data.heightmapResolution;
            float[,] heights = data.GetHeights(0, 0, resolution, resolution);
            
            Log($"Aplanando {_spawns.Count} áreas de spawn (radio={flattenRadius})");

            for (int i = 0; i < _spawns.Count; i++)
            {
                Vector3 spawnWorld = _spawns[i];
                Vector3 local = spawnWorld - terrain.transform.position;
                
                // Convertir a coordenadas normalizadas del terreno
                float nx = Mathf.Clamp01(local.x / data.size.x);
                float nz = Mathf.Clamp01(local.z / data.size.z);
                
                // Convertir a coordenadas del heightmap
                int centerX = Mathf.RoundToInt(nx * (resolution - 1));
                int centerZ = Mathf.RoundToInt(nz * (resolution - 1));
                
                // Obtener altura del centro
                float centerHeight = heights[centerZ, centerX];
                
                // Aplanar en un radio
                float radiusInHeightmapUnits = flattenRadius / data.size.x * resolution;
                int radiusInt = Mathf.CeilToInt(radiusInHeightmapUnits);
                
                for (int z = -radiusInt; z <= radiusInt; z++)
                {
                    for (int x = -radiusInt; x <= radiusInt; x++)
                    {
                        int hx = centerX + x;
                        int hz = centerZ + z;
                        
                        if (hx < 0 || hx >= resolution || hz < 0 || hz >= resolution)
                            continue;
                        
                        float dist = Mathf.Sqrt(x * x + z * z);
                        if (dist > radiusInt)
                            continue;
                        
                        // Interpolación suave hacia el centro
                        float t = 1f - Mathf.Clamp01(dist / radiusInt);
                        heights[hz, hx] = Mathf.Lerp(heights[hz, hx], centerHeight, t * t);
                    }
                }
                
                Log($"Spawn {i} aplanado en heightmap coords ({centerX}, {centerZ})");
            }
            
            data.SetHeights(0, 0, heights);
            Log("Áreas de spawn aplanadas");
        }

        IEnumerator RebuildNavMeshCoroutine()
        {
            if (!rebuildNavMeshOnGenerate)
            {
                Log("NavMesh: Rebuild desactivado en configuración");
                yield break;
            }

            if (navMeshSurface == null)
            {
                Debug.LogWarning("NavMesh: No hay NavMeshSurface asignado");
                yield break;
            }

            // Dejar que el terreno y colliders se actualicen tras SetHeights/Flatten
            yield return new WaitForEndOfFrame();
            yield return null;

            // Con Physics Colliders el Terrain NO se recoge (Terrain=0). Usar Render Meshes para que
            // el Terrain (TerrainData) se incluya como fuente y genere geometría.
            navMeshSurface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            navMeshSurface.layerMask = -1; // Todas las capas

            // Asegurar referencia al Terrain (si no está asignada, buscar en la escena)
            Terrain terrainToUse = terrain != null ? terrain : FindFirstObjectByType<Terrain>();
            if (terrainToUse == null)
                Debug.LogWarning("NavMesh: No hay Terrain en la escena. El NavMesh puede quedar vacío (0 triángulos).");

            // Construir con agentSlope forzado a 60°. Pasar el Terrain para añadirlo como source si no se recogió.
            BuildNavMeshWithSlope(this, navMeshSurface, 60f, terrainToUse);

            if (navMeshSurface.navMeshData != null)
            {
                var bounds = navMeshSurface.navMeshData.sourceBounds;
            Log($"NavMesh: Construcción completada. Bounds: center={bounds.center}, size={bounds.size}");
            }
            else
            {
                Debug.LogWarning("NavMesh: Construcción completada pero navMeshData es NULL");
            }

            // Re-aplicar obstáculos y forzar re-tallado en la NavMesh recién construida (TCs del mapa quedan con carving correcto).
            foreach (var ctrl in FindObjectsByType<BuildingController>(FindObjectsSortMode.None))
            {
                if (ctrl == null) continue;
                ctrl.RefreshObstacleAndCollider();
                var obs = ctrl.GetComponent<NavMeshObstacle>();
                if (obs != null)
                {
                    obs.enabled = false;
                    obs.enabled = true;
                }
            }

            StartCoroutine(FixUnitsAfterNavMesh());
        }

        /// <summary>
        /// Construye el NavMesh usando los mismos sources que NavMeshSurface pero con agentSlope forzado.
        /// Si terrain no es null y no hay fuente Terrain, se añade manualmente (el paquete a veces no la recoge).
        /// </summary>
        static void BuildNavMeshWithSlope(RTSMapGenerator owner, NavMeshSurface surface, float minAgentSlopeDegrees, Terrain terrain = null)
        {
            var settings = surface.GetBuildSettings();
            float originalSlope = settings.agentSlope;
            if (settings.agentSlope < minAgentSlopeDegrees)
            {
                settings.agentSlope = minAgentSlopeDegrees;
                if (owner != null) owner.Log($"NavMesh: Forzando agentSlope a {minAgentSlopeDegrees}° (agente tenía {originalSlope}°)");
            }
            // Evitar descarte de regiones pequeñas; puede dejar 0 triángulos si es muy alto
            settings.minRegionArea = 0f;

            var collectMethod = typeof(NavMeshSurface).GetMethod("CollectSources", BindingFlags.NonPublic | BindingFlags.Instance);
            if (collectMethod == null)
            {
                Debug.LogWarning("NavMesh: No se pudo obtener CollectSources, usando BuildNavMesh estándar.");
                surface.BuildNavMesh();
                return;
            }

            var sources = (List<NavMeshBuildSource>)collectMethod.Invoke(surface, null);
            if (sources == null || sources.Count == 0)
            {
                Debug.LogWarning("NavMesh: CollectSources devolvió 0 fuentes. ¿El Terrain está en una capa incluida?");
                surface.BuildNavMesh();
                return;
            }

            // Diagnóstico: contar por tipo (Terrain debe estar para que haya geometría)
            int terrainCount = 0, meshCount = 0, otherCount = 0;
            foreach (var src in sources)
            {
                if (src.shape == NavMeshBuildSourceShape.Terrain) terrainCount++;
                else if (src.shape == NavMeshBuildSourceShape.Mesh) meshCount++;
                else otherCount++;
            }
            if (owner != null) owner.Log($"NavMesh: sources: {sources.Count} (Terrain={terrainCount}, Mesh={meshCount}, otros={otherCount})");

            // Si el Terrain no se recogió (Terrain=0) pero tenemos referencia, añadirlo manualmente
            if (terrainCount == 0 && terrain != null && terrain.terrainData != null)
            {
                var terrainSource = new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Terrain,
                    sourceObject = terrain.terrainData,
                    transform = terrain.transform.localToWorldMatrix,
                    area = surface.defaultArea
                };
                sources.Add(terrainSource);
                terrainCount = 1;
                if (owner != null) owner.Log("NavMesh: Terrain añadido manualmente como source (no estaba en CollectSources).");
            }

            Bounds surfaceBounds;
            if (surface.collectObjects == CollectObjects.Volume)
            {
                surfaceBounds = new Bounds(surface.center, surface.size);
            }
            else
            {
                var boundsMethod = typeof(NavMeshSurface).GetMethod("CalculateWorldBounds", BindingFlags.NonPublic | BindingFlags.Instance);
                if (boundsMethod != null)
                    surfaceBounds = (Bounds)boundsMethod.Invoke(surface, new object[] { sources });
                else
                {
                    surfaceBounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
                    Debug.LogWarning("NavMesh: No se pudo calcular bounds, usando genérico.");
                }
            }

            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, surfaceBounds, surface.transform.position, surface.transform.rotation);
            if (data != null)
            {
                data.name = surface.gameObject.name;
                surface.RemoveData();
                surface.navMeshData = data;
                if (surface.isActiveAndEnabled)
                    surface.AddData();
                if (owner != null) owner.Log($"NavMesh: Construyendo con agentSlope={settings.agentSlope}°, minRegionArea=0, sources={sources.Count}");
            }
            else
            {
                Debug.LogWarning("NavMesh: BuildNavMeshData devolvió null. Probando con Use Geometry = Render Meshes (Terrain).");
                surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
                surface.BuildNavMesh();
            }
        }
        
        IEnumerator FixUnitsAfterNavMesh()
        {
            // Esperar más tiempo para que el NavMesh se asiente completamente
            yield return new WaitForSeconds(0.5f);
            
            // Verificar que el NavMesh existe
            var triangulation = UnityEngine.AI.NavMesh.CalculateTriangulation();
            Log($"NavMesh: {triangulation.vertices.Length} vértices, {triangulation.indices.Length / 3} triángulos");
            
            if (triangulation.vertices.Length == 0)
            {
                Debug.LogError("❌ NavMesh NO tiene geometría. Revisa la configuración del NavMeshSurface en el Inspector.");
                Debug.LogError("Asegúrate de que Max Slope (en NavMeshSurface) sea mayor a 45°");
                // No activar agentes cuando no hay NavMesh para evitar "Failed to create agent"
                yield break;
            }
            
            // Buscar todas las unidades con NavMeshAgent (incluidas las que dejamos deshabilitadas al mover)
            var agents = FindObjectsByType<UnityEngine.AI.NavMeshAgent>(FindObjectsSortMode.None);
            int fixedCount = 0;
            int failedCount = 0;
            
            Log($"Intentando recolocar {agents.Length} unidades...");
            
            foreach (var agent in agents)
            {
                // No tocar unidades que están siguiendo ruta A* (su agente está desactivado a propósito)
                var mover = agent.GetComponent<Project.Gameplay.Units.UnitMover>();
                if (mover != null && mover.IsFollowingPath)
                {
                    Log($"Unidad {agent.name} está en ruta A*, no tocar.");
                    continue;
                }

                // Si el agente está deshabilitado, primero recolocar en NavMesh y LUEGO activar
                // (activar antes de recolocar provoca "Failed to create agent because it is not close enough to the NavMesh")
                if (!agent.enabled)
                {
                    if (UnityEngine.AI.NavMesh.SamplePosition(agent.transform.position, out var hitDisabled, 100f, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        agent.transform.position = hitDisabled.position;
                        agent.enabled = true;
                        fixedCount++;
                        Log($"✅ Unidad {agent.name} recolocada y activada en NavMesh");
                    }
                    else
                    {
                        agent.enabled = true; // activar igual para no dejarla bloqueada
                        failedCount++;
                        Debug.LogWarning($"❌ Unidad {agent.name} no se encontró NavMesh cerca, activada en posición actual.");
                    }
                    continue;
                }

                if (agent.isOnNavMesh)
                {
                    Log($"Unidad {agent.name} ya está en NavMesh");
                    continue;
                }
                    
                // Agente activo pero no en NavMesh: Warp a posición válida
                if (UnityEngine.AI.NavMesh.SamplePosition(agent.transform.position, out var hit, 100f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                    fixedCount++;
                    Log($"✅ Unidad {agent.name} recolocada a {hit.position}");
                }
                else
                {
                    failedCount++;
                    Debug.LogWarning($"❌ Unidad {agent.name} NO pudo ser recolocada. Posición: {agent.transform.position}");
                }
            }
            
            if (fixedCount > 0 || failedCount > 0)
            {
                if (failedCount > 0)
                    Debug.LogError($"⚠️ Unidades recolocadas: {fixedCount} exitosas, {failedCount} FALLIDAS");
                else
            Log($"✅ Unidades recolocadas: {fixedCount} exitosas, {failedCount} fallidas");
            }
        }

        [ContextMenu("Debug: Estado del Generador")]
        void DebugGeneratorState()
        {
            Log("=== ESTADO DEL GENERADOR ===");
            Log($"Terrain asignado: {terrain != null}");
            Log($"Grid Config: {gridConfig != null}");
            Log($"Grid inicializado: {_grid != null && _grid.IsReady}");
            Log($"Spawns generados: {_spawns?.Count ?? 0}");
            Log($"NavMeshSurface: {navMeshSurface != null}");
            
            if (terrain != null)
            {
                Log($"Terrain pos: {terrain.transform.position}");
                Log($"Terrain data: {terrain.terrainData != null}");
                if (terrain.terrainData != null)
                    Log($"Terrain size: {terrain.terrainData.size}");
            }
            
            Log("=== PREFABS ===");
            Log($"Town Center SO: {townCenterSO != null}");
            Log($"Town Center Prefab Override: {townCenterPrefabOverride != null}");
            Log($"Tree: {HasAnyTreePrefab()}");
            Log($"Berry: {HasAnyBerryPrefab()}");
            Log($"Animal: {HasAnyAnimalPrefab()}");
            Log($"Gold: {HasAnyGoldPrefab()}");
            Log($"Stone: {HasAnyStonePrefab()}");
        }

        void OnDrawGizmos()
        {
            if (!showGridInScene || _grid == null || !_grid.IsReady) return;
            float cs = _grid.cellSize;
            Vector3 o = _grid.origin;
            int w = _grid.width;
            int h = _grid.height;
            Gizmos.color = new Color(1f, 1f, 1f, gridLineAlpha);
            if (gridSegmentFollowTerrain)
                DrawGridGizmosSegmented(cs, o, w, h);
            else
            {
                for (int x = 0; x <= w; x++)
                {
                    float wx = o.x + x * cs;
                    float y0 = GridSampleY(wx, o.z, o.y);
                    float y1 = GridSampleY(wx, o.z + h * cs, o.y);
                    Gizmos.DrawLine(new Vector3(wx, y0, o.z), new Vector3(wx, y1, o.z + h * cs));
                }
                for (int z = 0; z <= h; z++)
                {
                    float wz = o.z + z * cs;
                    float y0 = GridSampleY(o.x, wz, o.y);
                    float y1 = GridSampleY(o.x + w * cs, wz, o.y);
                    Gizmos.DrawLine(new Vector3(o.x, y0, wz), new Vector3(o.x + w * cs, y1, wz));
                }
            }
        }

        void DrawGridGizmosSegmented(float cs, Vector3 o, int w, int h)
        {
            for (int x = 0; x <= w; x++)
            {
                float wx = o.x + x * cs;
                for (int z = 0; z < h; z++)
                {
                    float wz0 = o.z + z * cs;
                    float wz1 = o.z + (z + 1) * cs;
                    float y0 = GridSampleY(wx, wz0, o.y);
                    float y1 = GridSampleY(wx, wz1, o.y);
                    Gizmos.DrawLine(new Vector3(wx, y0, wz0), new Vector3(wx, y1, wz1));
                }
            }
            for (int z = 0; z <= h; z++)
            {
                float wz = o.z + z * cs;
                for (int x = 0; x < w; x++)
                {
                    float wx0 = o.x + x * cs;
                    float wx1 = o.x + (x + 1) * cs;
                    float y0 = GridSampleY(wx0, wz, o.y);
                    float y1 = GridSampleY(wx1, wz, o.y);
                    Gizmos.DrawLine(new Vector3(wx0, y0, wz), new Vector3(wx1, y1, wz));
                }
            }
        }

        void OnRenderObject()
        {
            if (!showGridInGameView || _grid == null || !_grid.IsReady) return;
            if (gridLineMaterialOverride != null) _gridLineMat = gridLineMaterialOverride;
            else if (_gridLineMat == null) CreateGridLineMaterial();
            if (_gridLineMat == null) return;
            int w = _grid.width;
            int h = _grid.height;
            float cs = _grid.cellSize;
            Vector3 o = _grid.origin;
            if (_gridLineMat.HasProperty("_Color"))
                _gridLineMat.SetColor("_Color", new Color(1f, 1f, 1f, gridLineAlpha));
            if (!_gridLineMat.SetPass(0)) return;
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.LINES);
            GL.Color(new Color(1f, 1f, 1f, gridLineAlpha));
            if (gridSegmentFollowTerrain)
                DrawGridGLSegmented(cs, o, w, h);
            else
            {
                if ((w + 1) + (h + 1) > s_GridLinesLimit) { GL.End(); GL.PopMatrix(); return; }
                for (int x = 0; x <= w; x++)
                {
                    float wx = o.x + x * cs;
                    float y0 = GridSampleY(wx, o.z, o.y);
                    float y1 = GridSampleY(wx, o.z + h * cs, o.y);
                    GL.Vertex3(wx, y0, o.z);
                    GL.Vertex3(wx, y1, o.z + h * cs);
                }
                for (int z = 0; z <= h; z++)
                {
                    float wz = o.z + z * cs;
                    float y0 = GridSampleY(o.x, wz, o.y);
                    float y1 = GridSampleY(o.x + w * cs, wz, o.y);
                    GL.Vertex3(o.x, y0, wz);
                    GL.Vertex3(o.x + w * cs, y1, wz);
                }
            }
            GL.End();
            GL.PopMatrix();
        }

        void DrawGridGLSegmented(float cs, Vector3 o, int w, int h)
        {
            for (int x = 0; x <= w; x++)
            {
                float wx = o.x + x * cs;
                for (int z = 0; z < h; z++)
                {
                    float wz0 = o.z + z * cs;
                    float wz1 = o.z + (z + 1) * cs;
                    float y0 = GridSampleY(wx, wz0, o.y);
                    float y1 = GridSampleY(wx, wz1, o.y);
                    GL.Vertex3(wx, y0, wz0);
                    GL.Vertex3(wx, y1, wz1);
                }
            }
            for (int z = 0; z <= h; z++)
            {
                float wz = o.z + z * cs;
                for (int x = 0; x < w; x++)
                {
                    float wx0 = o.x + x * cs;
                    float wx1 = o.x + (x + 1) * cs;
                    float y0 = GridSampleY(wx0, wz, o.y);
                    float y1 = GridSampleY(wx1, wz, o.y);
                    GL.Vertex3(wx0, y0, wz);
                    GL.Vertex3(wx1, y1, wz);
                }
            }
        }

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            if (_spawns == null || _spawns.Count == 0) return;

            for (int i = 0; i < _spawns.Count; i++)
            {
                Gizmos.color = spawnColor;
                Gizmos.DrawSphere(_spawns[i], 1.2f);

                Gizmos.color = ringNearColor;
                Gizmos.DrawWireSphere(_spawns[i], ringNear.x);
                Gizmos.DrawWireSphere(_spawns[i], ringNear.y);

                Gizmos.color = ringMidColor;
                Gizmos.DrawWireSphere(_spawns[i], ringMid.x);
                Gizmos.DrawWireSphere(_spawns[i], ringMid.y);

                Gizmos.color = ringFarColor;
                Gizmos.DrawWireSphere(_spawns[i], ringFar.x);
                Gizmos.DrawWireSphere(_spawns[i], ringFar.y);
            }
        }
    }
}
