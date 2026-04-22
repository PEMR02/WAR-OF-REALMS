using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay;
using Project.Gameplay.Buildings;
using Project.Gameplay.Combat;
using Unity.AI.Navigation;
using Project.Gameplay.Map.Generator;
using Project.Gameplay.Map.Generation;
using Project.Gameplay.Map.Generation.Alpha;
using Project.Gameplay.AI;
using Project.Gameplay.Faction;
using Project.Gameplay.Players;
using Project.Gameplay.Units;

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
        MatchConfig _matchUsedForLastGenerate;

        [Header("Map Preset (Estilo AoE2)")]
        [Tooltip("Preset del mapa. 'Custom' usa los valores del Inspector. Otros presets sobrescriben configuración automáticamente.")]
        public MapPresetType mapPreset = MapPresetType.Continental;

        [Header("Match Config")]
        [Tooltip("Configuración central de partida. Si está asignada, mapa/grid/minimapa/spawns/recursos salen de aquí.")]
        public MatchConfig matchConfig;

        [Header("Lobby / vista previa (opcional)")]
        [Tooltip("Si hay un MapLobbyController en escena que registre diferido, Start no llama Generate() hasta que el lobby confirme.")]
        MapLobbyController _deferredMapLobby;
        bool _pendingLobbyRuntimeTuning;
        
        [Tooltip("Ancho del mapa en celdas.")]
        public int width = 256;
        [Tooltip("Alto del mapa en celdas.")]
        public int height = 256;
        [Tooltip("Si está activo, el mapa se centra en el mundo: origen del grid en (-W·cell/2, Y del RTS, -H·cell/2). Evita que la grilla quede solo hacia +X/+Z.")]
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
        [Tooltip("Radio NavMesh.SamplePosition al recolocar unidades tras el bake (más alto = más tolerancia en mapas grandes).")]
        [Min(1f)] public float navMeshPostBakeSampleRadius = 48f;
        [Tooltip("Reintentos extra con radio ×1.5 por intento si el snap inicial falla.")]
        [Range(0, 5)] public int navMeshPostBakeSampleRetries = 2;

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

        [Header("Selección (outline unidades/edificios/recursos)")]
        [Tooltip("Config global del borde de selección. Asigna un asset creado con Create > Project > Selection > Outline Config para controlar color y grosor desde aquí (aplica a todos).")]
        public SelectionOutlineConfig selectionOutlineConfig;

        [Header("Lobby (UI pre-partida)")]
        [Tooltip("Tamaño de celda en unidades mundo empujado al Match en runtime (>0). Tiene prioridad sobre el default legacy.")]
        [Min(0f)] public float mapCellSizeWorld = 3f;
        [Tooltip("Masas montañosas macro (0–4 en lobby). Se aplica al MapGen tras compilar el match.")]
        [Range(0, 12)] public int lobbyMacroMountainMasses = 2;

        [Header("Inicio de partida")]
        [Tooltip("Prefab del aldeano inicial (3 por jugador). Si está vacío, se usa startingVillagerUnitSO.prefab.")]
        public GameObject startingVillagerPrefab;
        [Tooltip("Opcional: para resolver el prefab del aldeano si startingVillagerPrefab no está asignado.")]
        public UnitSO startingVillagerUnitSO;
        [Tooltip("Patrón de spawn en lobby: 0 esquinas, 1 radial/equilibrado, 2 opuestos/bordes.")]
        [Range(0, 2)] public int lobbySpawnPatternUi = 1;
        [Tooltip("Lobby: plaza 1–4 = humano si true, IA si false. Se copia a MatchConfig.players.slots al generar.")]
        public bool[] lobbyPlayerSlotIsHuman = new bool[4] { true, true, true, true };
        [Tooltip("Lobby: dificultad IA por plaza (0 Fácil, 1 Normal, 2 Difícil). Solo slots IA.")]
        [Range(0, 2)] public int[] lobbyAiDifficulty = new int[4] { 1, 1, 1, 1 };

        [Header("IA (skirmish)")]
        [Tooltip("Opcional: catálogo de producción (si vacío, se intenta tomar del ProductionHUD en escena).")]
        public ProductionCatalog aiProductionCatalog;
        [Tooltip("Opcional: unidades/edificios para la IA. Si faltan, la IA solo economía + aldeanos.")]
        public UnitSO aiVillagerUnitSO;
        public BuildingSO aiHouseSO;
        public BuildingSO aiBarracksSO;

        [Header("Players")]
        [Range(1, 4)] public int playerCount = 2;
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
        [Tooltip("-1 = 75% en clusters. Si no (0–1), fracción de globalTrees que coloca el colocador en manchas.")]
        public float globalTreesClusterFraction = -1f;
        [Tooltip("Si true, árboles globales solo donde el alphamap del terreno favorece la capa hierba [0].")]
        public bool preferGlobalTreesOnGrassAlphamap;

        [Header("Clusters piedra / oro globales")]
        [Tooltip("Fracción de nodos globalStone/globalGold colocados en manchas; el resto se reparte disperso si falta sitio.")]
        [Range(0.4f, 1f)] public float globalStoneGoldClusterFraction = 0.82f;
        [Tooltip("Tamaño de cada mancha (min–max nodos).")]
        public Vector2Int globalMineralClusterSize = new Vector2Int(2, 6);
        [Tooltip("Radio aproximado en celdas del grid; menor = vetas más compactas.")]
        public float globalMineralClusterRadiusCells = 3.2f;

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
        [Tooltip("Si false (recomendado en mapas grandes), no fuerza ShadowCasting On en cada recurso instanciado.")]
        public bool forceResourceShadowCasting = false;
        public bool drawGizmos = true;
        public Color spawnColor = Color.cyan;
        public Color ringNearColor = new Color(0f, 1f, 0f, 0.4f);
        public Color ringMidColor = new Color(1f, 1f, 0f, 0.4f);
        public Color ringFarColor = new Color(1f, 0.5f, 0f, 0.4f);

        [Header("Generador Definitivo (ÚNICO)")]
        [Tooltip("DEPRECATED: preferir MatchConfig.mapGenerationProfile. Plantilla técnica en escena si no hay perfil en Match.")]
        public MapGenConfig definitiveMapGenConfig;

        [Header("Generación — arquitectura")]
        [Tooltip("OFF por defecto: presets Forest/Continental no pisan el Inspector; la fuente de verdad es MatchConfig.")]
        public bool useLegacyMapPresets = false;

        [Tooltip(
            "Si hay MatchConfig asignado y esto está activo, riverCount / lakeCount / maxLakeCells de ESTE componente " +
            "pisan la copia runtime (útil para probar en escena). Desactiva para que mande solo el asset MatchConfig / hydrology alpha.")]
        public bool preferSceneHydrologyOverrides = true;

        MapGrid _grid;
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
        MatchConfig _runtimeMatchConfig;
        RuntimeMapGenerationSettings _lastCompiledSettings;

        /// <summary>Último resultado de <see cref="MatchConfigCompiler.Build"/> en esta sesión de generación.</summary>
        public RuntimeMapGenerationSettings LastCompiledMapSettings => _lastCompiledSettings;

        /// <summary>Registrado por <see cref="MapLobbyController"/> en Awake (orden de ejecución más bajo que este componente).</summary>
        public void RegisterDeferredMapLobby(MapLobbyController lobby) => _deferredMapLobby = lobby;

        /// <summary>Llama antes de <see cref="Generate"/> tras el lobby para aplicar seed/ríos/lagos del panel a la copia runtime del match.</summary>
        public void PrepareGenerateFromLobby() => _pendingLobbyRuntimeTuning = true;

        public MapGrid GetGrid() => _grid;
        public System.Random GetRng() => _rng;
        MatchConfig ResolveMatchConfig()
        {
            if (_runtimeMatchConfig != null)
                return _runtimeMatchConfig;

            _runtimeMatchConfig = matchConfig != null
                ? matchConfig.CreateRuntimeCopy()
                : CreateLegacyMatchConfig();
            if (_runtimeMatchConfig.useHighLevelAlphaConfig)
                HighLevelMatchSynthesizer.SynthesizeIntoLegacySlots(_runtimeMatchConfig);
            ApplySceneHydrologyToMatch(_runtimeMatchConfig);
            if (matchConfig != null && preferSceneHydrologyOverrides)
            {
                _runtimeMatchConfig.map.seed = seed;
                _runtimeMatchConfig.map.randomSeedOnPlay = randomSeedOnPlay;
            }
            if (_pendingLobbyRuntimeTuning)
            {
                PushSceneToMatchForGeneration(_runtimeMatchConfig);
                _pendingLobbyRuntimeTuning = false;
            }
            MatchRuntimeState.SetCurrent(_runtimeMatchConfig);
            return _runtimeMatchConfig;
        }

        /// <summary>
        /// Copia hidrología del Inspector del RTS a la copia runtime del match cuando <see cref="preferSceneHydrologyOverrides"/> y hay asset.
        /// </summary>
        internal void ApplySceneHydrologyToMatch(MatchConfig runtimeMatch)
        {
            if (runtimeMatch == null || matchConfig == null || !preferSceneHydrologyOverrides) return;
            runtimeMatch.water.riverCount = Mathf.Clamp(riverCount, 0, 8);
            runtimeMatch.water.lakeCount = Mathf.Clamp(lakeCount, 0, 12);
            runtimeMatch.water.maxLakeCells = Mathf.Max(50, maxLakeCells);
            if (runtimeMatch.useHighLevelAlphaConfig)
            {
                runtimeMatch.hydrology.riverCount = runtimeMatch.water.riverCount;
                runtimeMatch.hydrology.lakeCount = runtimeMatch.water.lakeCount;
                runtimeMatch.hydrology.riversEnabled = runtimeMatch.water.riverCount > 0;
                runtimeMatch.hydrology.lakesEnabled = runtimeMatch.water.lakeCount > 0;
            }
        }

        /// <summary>
        /// Copia el contador de masas montañosas del lobby/inspector al match antes de compilar,
        /// para que ApplyHighLevelToMapGen no deje macro en 0 respecto al asset MatchConfig.
        /// </summary>
        internal void ApplySceneLobbyMacroToMatch(MatchConfig runtimeMatch)
        {
            if (runtimeMatch == null || !runtimeMatch.useHighLevelAlphaConfig) return;
            int m = Mathf.Clamp(lobbyMacroMountainMasses, 0, 12);
            runtimeMatch.terrainShape.mountainsEnabled = m > 0;
            runtimeMatch.terrainShape.mountainMassCount = m;
        }

        MatchConfig CreateLegacyMatchConfig()
        {
            MatchConfig legacy = ScriptableObject.CreateInstance<MatchConfig>();
            legacy.hideFlags = HideFlags.HideAndDontSave;
            CopySceneGeneratorFieldsIntoMatchConfig(legacy);
            // Sin asset MatchConfig: el lobby y el generador alpha deben activarse para que layout/jugadores/hidrología sincronicen bien.
            legacy.useHighLevelAlphaConfig = true;
            legacy.layout.mapWidth = Mathf.Max(16, width);
            legacy.layout.mapHeight = Mathf.Max(16, height);
            legacy.layout.gridCellSize = mapCellSizeWorld > 0.01f ? mapCellSizeWorld : 2.5f;
            legacy.layout.playerCount = Mathf.Clamp(playerCount, 1, 8);
            legacy.layout.seed = seed;
            legacy.layout.randomSeedOnPlay = randomSeedOnPlay;
            legacy.layout.centerMapAtOrigin = centerAtOrigin;
            SyncLobbyPlayerSlotsIntoMatch(legacy);
            return legacy;
        }

        /// <summary>
        /// Copia el estado actual del RTS (inspector + lobby) sobre un <see cref="MatchConfig"/> runtime antes de compilar.
        /// </summary>
        internal void CopySceneGeneratorFieldsIntoMatchConfig(MatchConfig target)
        {
            if (target == null) return;

            target.map.preset = mapPreset;
            target.map.width = width;
            target.map.height = height;
            target.map.cellSize = mapCellSizeWorld > 0.01f ? mapCellSizeWorld : MatchRuntimeState.DefaultCellSize;
            target.map.centerAtOrigin = centerAtOrigin;
            target.map.randomSeedOnPlay = randomSeedOnPlay;
            target.map.seed = seed;
            target.geography.terrainFlatness = terrainFlatness;
            target.geography.heightMultiplier = heightMultiplier;
            target.geography.noiseScale = noiseScale;
            target.geography.noiseOctaves = noiseOctaves;
            target.geography.noisePersistence = noisePersistence;
            target.geography.noiseLacunarity = noiseLacunarity;
            target.geography.maxSlope = maxSlope;
            target.geography.alignTerrainToGrid = alignTerrainToGrid;
            target.water.waterHeight = waterHeight;
            target.water.waterHeightRelative = waterHeightRelative;
            target.water.riverCount = riverCount;
            target.water.lakeCount = lakeCount;
            target.water.maxLakeCells = maxLakeCells;
            target.water.sandShoreCells = sandShoreCells;
            target.water.surfaceOffset = waterSurfaceOffset;
            target.water.chunkSize = waterChunkSize;
            target.water.alpha = waterAlpha;
            target.water.showWater = showWater;
            target.water.meshMode = waterMeshMode;
            target.water.waterLayer = waterLayerOverride;
            target.water.material = waterMaterial;
            target.resources.ringNear = ringNear;
            target.resources.ringMid = ringMid;
            target.resources.ringFar = ringFar;
            target.resources.nearTrees = nearTrees;
            target.resources.midTrees = midTrees;
            target.resources.berries = berries;
            target.resources.animals = animals;
            target.resources.goldSafe = goldSafe;
            target.resources.stoneSafe = stoneSafe;
            target.resources.goldFar = goldFar;
            target.resources.globalTrees = globalTrees;
            target.resources.globalStone = globalStone;
            target.resources.globalGold = globalGold;
            target.resources.globalAnimals = globalAnimals;
            target.resources.globalExcludeRadius = globalExcludeRadius;
            target.resources.forestClustering = forestClustering;
            target.resources.clusterDensity = clusterDensity;
            target.resources.clusterMinSize = clusterMinSize;
            target.resources.clusterMaxSize = clusterMaxSize;
            target.resources.minWoodTrees = minWoodTrees;
            target.resources.minGoldNodes = minGoldNodes;
            target.resources.minStoneNodes = minStoneNodes;
            target.resources.minFoodValue = minFoodValue;
            target.resources.maxResourceRetries = maxResourceRetries;
            target.resources.visuals.treePrefab = treePrefab;
            target.resources.visuals.treePrefabVariants = treePrefabVariants;
            target.resources.visuals.berryPrefab = berryPrefab;
            target.resources.visuals.berryPrefabVariants = berryPrefabVariants;
            target.resources.visuals.animalPrefab = animalPrefab;
            target.resources.visuals.animalPrefabVariants = animalPrefabVariants;
            target.resources.visuals.goldPrefab = goldPrefab;
            target.resources.visuals.goldPrefabVariants = goldPrefabVariants;
            target.resources.visuals.stonePrefab = stonePrefab;
            target.resources.visuals.stonePrefabVariants = stonePrefabVariants;
            target.resources.visuals.stoneMaterialOverride = stoneMaterialOverride;
            target.resources.visuals.treeMaterialOverrides = treeMaterialOverrides;
            target.resources.visuals.treePlacementRotation = treePlacementRotation;
            target.resources.visuals.resourceLayerName = resourceLayerName;
            target.resources.visuals.randomRotationPerResource = randomRotationPerResource;
            target.resources.visuals.cellPlacementRandomOffset = cellPlacementRandomOffset;
            target.resources.visuals.forceResourceShadowCasting = forceResourceShadowCasting;
            target.resources.placement.globalTreesClusterFraction = globalTreesClusterFraction;
            target.resources.placement.preferGlobalTreesOnGrassAlphamap = preferGlobalTreesOnGrassAlphamap;
            target.resources.placement.globalStoneGoldClusterFraction = globalStoneGoldClusterFraction;
            target.resources.placement.globalMineralClusterSize = globalMineralClusterSize;
            target.resources.placement.globalMineralClusterRadiusCells = globalMineralClusterRadiusCells;
            target.water.baseHeightNormalized = waterHeightRelative >= 0f && waterHeightRelative <= 1f
                ? waterHeightRelative
                : 0.4f;
            target.climate.paintTerrainByHeight = paintTerrainByHeight;
            target.climate.grassLayer = grassLayer;
            target.climate.dirtLayer = dirtLayer;
            target.climate.rockLayer = rockLayer;
            target.climate.sandLayer = sandLayer;
            target.climate.grassTileSize = grassTileSize;
            target.climate.dirtTileSize = dirtTileSize;
            target.climate.rockTileSize = rockTileSize;
            target.climate.sandTileSize = sandTileSize;
            target.climate.grassPercent = grassPercent;
            target.climate.dirtPercent = dirtPercent;
            target.climate.rockPercent = rockPercent;
            target.climate.textureBlendWidth = textureBlendWidth;
            target.players.playerCount = playerCount;
            target.players.spawnEdgePadding = spawnEdgePadding;
            target.players.minPlayerDistance2p = minPlayerDistance2p;
            target.players.minPlayerDistance4p = minPlayerDistance4p;
            target.players.spawnFlatRadius = spawnFlatRadius;
            target.players.maxSlopeAtSpawn = maxSlopeAtSpawn;
            target.players.waterExclusionRadius = waterExclusionRadius;
            target.players.flattenSpawnAreas = flattenSpawnAreas;
            target.players.flattenRadius = flattenRadius;
            target.startingLoadout.townCenter = townCenterSO;
            target.startingLoadout.townCenterPrefabOverride = townCenterPrefabOverride;
            target.startingLoadout.townCenterClearRadius = tcClearRadius;
            target.startingLoadout.townCenterSpawnYOffset = townCenterSpawnYOffset;
            target.graphics.showWater = showWater;
            target.graphics.debugLogs = debugLogs;
        }

        internal void EnsureLobbyPlayerSlotsArray()
        {
            if (lobbyPlayerSlotIsHuman == null || lobbyPlayerSlotIsHuman.Length != 4)
                lobbyPlayerSlotIsHuman = new bool[4] { true, true, true, true };
            if (lobbyAiDifficulty == null || lobbyAiDifficulty.Length != 4)
                lobbyAiDifficulty = new int[4] { 1, 1, 1, 1 };
        }

        /// <summary>Rellena <see cref="MatchConfig.players.slots"/> desde el lobby (humano/IA) sin tocar el resto del asset.</summary>
        internal void SyncLobbyPlayerSlotsIntoMatch(MatchConfig m)
        {
            if (m == null) return;
            EnsureLobbyPlayerSlotsArray();
            int n = Mathf.Clamp(playerCount, 1, 4);
            int humans = 0;
            for (int i = 0; i < n; i++)
                if (lobbyPlayerSlotIsHuman[i]) humans++;
            if (humans == 0)
            {
                lobbyPlayerSlotIsHuman[0] = true;
                humans = 1;
            }

            m.players.playerCount = n;
            if (m.players.slots == null)
                m.players.slots = new List<MatchConfig.PlayerSlotSettings>();
            m.players.slots.Clear();
            for (int i = 0; i < n; i++)
            {
                var slot = new MatchConfig.PlayerSlotSettings
                {
                    id = $"Jugador {i + 1}",
                    kind = lobbyPlayerSlotIsHuman[i] ? MatchConfig.PlayerSlotKind.Human : MatchConfig.PlayerSlotKind.AI,
                    factionId = "Default"
                };
                if (slot.kind == MatchConfig.PlayerSlotKind.AI)
                {
                    int d = Mathf.Clamp(lobbyAiDifficulty[i], 0, 2);
                    slot.aiDifficulty = (AIDifficulty)d;
                }
                m.players.slots.Add(slot);
            }
        }

        static MapSpawnPattern LobbySpawnUiToPattern(int ui) =>
            ui <= 0 ? MapSpawnPattern.Corners : ui == 1 ? MapSpawnPattern.Balanced : MapSpawnPattern.Edges;

        /// <summary>
        /// Alinea la copia runtime del match con los campos actuales del RTS (lobby + inspector) antes de preview o “Empezar partida”.
        /// </summary>
        internal void PushSceneToMatchForGeneration(MatchConfig m)
        {
            if (m == null) return;

            if (m.useHighLevelAlphaConfig)
            {
                m.layout.mapWidth = width;
                m.layout.mapHeight = height;
                if (mapCellSizeWorld > 0.01f) m.layout.gridCellSize = mapCellSizeWorld;
                m.layout.playerCount = Mathf.Clamp(playerCount, 1, 8);
                m.layout.seed = seed;
                m.layout.randomSeedOnPlay = false;
                m.layout.centerMapAtOrigin = centerAtOrigin;
                m.layout.spawnPattern = LobbySpawnUiToPattern(lobbySpawnPatternUi);

                m.hydrology.riversEnabled = riverCount > 0;
                m.hydrology.riverCount = riverCount;
                m.hydrology.lakesEnabled = lakeCount > 0;
                m.hydrology.lakeCount = lakeCount;
                if (waterHeightRelative >= 0f && waterHeightRelative <= 1f)
                    m.hydrology.waterBaseHeightNormalized = waterHeightRelative;

                m.terrainShape.mountainsEnabled = lobbyMacroMountainMasses > 0;
                m.terrainShape.mountainMassCount = lobbyMacroMountainMasses;

                HighLevelMatchSynthesizer.SynthesizeIntoLegacySlots(m);

                m.resources.globalTrees = globalTrees;
                m.resources.globalStone = globalStone;
                m.resources.globalGold = globalGold;
                m.resources.globalAnimals = globalAnimals;
                m.resources.berries = berries;
                m.resources.nearTrees = nearTrees;
                m.resources.midTrees = midTrees;
            }
            else
                CopySceneGeneratorFieldsIntoMatchConfig(m);

            m.map.seed = seed;
            m.map.randomSeedOnPlay = false;
            SyncLobbyPlayerSlotsIntoMatch(m);
        }

        MapGenerationRuntimeContext BuildRuntimeGenerationContext()
        {
            return new MapGenerationRuntimeContext
            {
                applySceneHydrologyOverrides = matchConfig != null && preferSceneHydrologyOverrides,
                sceneRiverCount = riverCount,
                sceneLakeCount = lakeCount,
                sceneMaxLakeCells = maxLakeCells,
                applyLobbyMacroRelief = true,
                lobbyMacroMountainMassCount = lobbyMacroMountainMasses,
                applyLegacyRiverWidthScale = true,
                legacyRiverWidthScale = 1.5f
            };
        }

        void ApplyMatchConfigToLegacyFields(MatchConfig cfg)
        {
            if (cfg == null) return;

            mapPreset = cfg.map.preset;
            width = cfg.map.width;
            height = cfg.map.height;
            centerAtOrigin = cfg.map.centerAtOrigin;
            randomSeedOnPlay = cfg.map.randomSeedOnPlay;
            seed = cfg.map.seed;
            terrainFlatness = cfg.geography.terrainFlatness;
            heightMultiplier = cfg.geography.heightMultiplier;
            noiseScale = cfg.geography.noiseScale;
            noiseOctaves = cfg.geography.noiseOctaves;
            noisePersistence = cfg.geography.noisePersistence;
            noiseLacunarity = cfg.geography.noiseLacunarity;
            maxSlope = cfg.geography.maxSlope;
            alignTerrainToGrid = cfg.geography.alignTerrainToGrid;
            waterHeight = cfg.water.waterHeight;
            // Espejo de inspector: el generador definitivo usa solo baseHeightNormalized (Match).
            waterHeightRelative = cfg.water.baseHeightNormalized;
            riverCount = cfg.water.riverCount;
            lakeCount = cfg.water.lakeCount;
            maxLakeCells = cfg.water.maxLakeCells;
            sandShoreCells = cfg.water.sandShoreCells;
            waterSurfaceOffset = cfg.water.surfaceOffset;
            waterChunkSize = cfg.water.chunkSize;
            waterAlpha = cfg.water.alpha;
            showWater = cfg.water.showWater;
            waterMeshMode = cfg.water.meshMode;
            waterLayerOverride = cfg.water.waterLayer;
            waterMaterial = cfg.water.material;
            ringNear = cfg.resources.ringNear;
            ringMid = cfg.resources.ringMid;
            ringFar = cfg.resources.ringFar;
            nearTrees = cfg.resources.nearTrees;
            midTrees = cfg.resources.midTrees;
            berries = cfg.resources.berries;
            animals = cfg.resources.animals;
            goldSafe = cfg.resources.goldSafe;
            stoneSafe = cfg.resources.stoneSafe;
            goldFar = cfg.resources.goldFar;
            globalTrees = cfg.resources.globalTrees;
            globalStone = cfg.resources.globalStone;
            globalGold = cfg.resources.globalGold;
            globalAnimals = cfg.resources.globalAnimals;
            globalExcludeRadius = cfg.resources.globalExcludeRadius;
            forestClustering = cfg.resources.forestClustering;
            clusterDensity = cfg.resources.clusterDensity;
            clusterMinSize = cfg.resources.clusterMinSize;
            clusterMaxSize = cfg.resources.clusterMaxSize;
            minWoodTrees = cfg.resources.minWoodTrees;
            minGoldNodes = cfg.resources.minGoldNodes;
            minStoneNodes = cfg.resources.minStoneNodes;
            minFoodValue = cfg.resources.minFoodValue;
            maxResourceRetries = cfg.resources.maxResourceRetries;
            treePrefab = cfg.resources.visuals.treePrefab;
            treePrefabVariants = cfg.resources.visuals.treePrefabVariants;
            berryPrefab = cfg.resources.visuals.berryPrefab;
            berryPrefabVariants = cfg.resources.visuals.berryPrefabVariants;
            animalPrefab = cfg.resources.visuals.animalPrefab;
            animalPrefabVariants = cfg.resources.visuals.animalPrefabVariants;
            goldPrefab = cfg.resources.visuals.goldPrefab;
            goldPrefabVariants = cfg.resources.visuals.goldPrefabVariants;
            stonePrefab = cfg.resources.visuals.stonePrefab;
            stonePrefabVariants = cfg.resources.visuals.stonePrefabVariants;
            stoneMaterialOverride = cfg.resources.visuals.stoneMaterialOverride;
            treeMaterialOverrides = cfg.resources.visuals.treeMaterialOverrides;
            treePlacementRotation = cfg.resources.visuals.treePlacementRotation;
            resourceLayerName = cfg.resources.visuals.resourceLayerName;
            randomRotationPerResource = cfg.resources.visuals.randomRotationPerResource;
            cellPlacementRandomOffset = cfg.resources.visuals.cellPlacementRandomOffset;
            forceResourceShadowCasting = cfg.resources.visuals.forceResourceShadowCasting;
            globalTreesClusterFraction = cfg.resources.placement.globalTreesClusterFraction;
            preferGlobalTreesOnGrassAlphamap = cfg.resources.placement.preferGlobalTreesOnGrassAlphamap;
            globalStoneGoldClusterFraction = cfg.resources.placement.globalStoneGoldClusterFraction;
            globalMineralClusterSize = cfg.resources.placement.globalMineralClusterSize;
            globalMineralClusterRadiusCells = cfg.resources.placement.globalMineralClusterRadiusCells;
            paintTerrainByHeight = cfg.climate.paintTerrainByHeight;
            grassLayer = cfg.climate.grassLayer;
            dirtLayer = cfg.climate.dirtLayer;
            rockLayer = cfg.climate.rockLayer;
            sandLayer = cfg.climate.sandLayer;
            grassTileSize = cfg.climate.grassTileSize;
            dirtTileSize = cfg.climate.dirtTileSize;
            rockTileSize = cfg.climate.rockTileSize;
            sandTileSize = cfg.climate.sandTileSize;
            grassPercent = cfg.climate.grassPercent;
            dirtPercent = cfg.climate.dirtPercent;
            rockPercent = cfg.climate.rockPercent;
            textureBlendWidth = cfg.climate.textureBlendWidth;
            playerCount = Mathf.Clamp(cfg.players.playerCount, 1, 4);
            EnsureLobbyPlayerSlotsArray();
            if (cfg.players.slots != null && cfg.players.slots.Count > 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    lobbyPlayerSlotIsHuman[i] = i < cfg.players.slots.Count
                        && cfg.players.slots[i].kind == MatchConfig.PlayerSlotKind.Human;
                }
            }
            spawnEdgePadding = cfg.players.spawnEdgePadding;
            minPlayerDistance2p = cfg.players.minPlayerDistance2p;
            minPlayerDistance4p = cfg.players.minPlayerDistance4p;
            spawnFlatRadius = cfg.players.spawnFlatRadius;
            maxSlopeAtSpawn = cfg.players.maxSlopeAtSpawn;
            waterExclusionRadius = cfg.players.waterExclusionRadius;
            flattenSpawnAreas = cfg.players.flattenSpawnAreas;
            flattenRadius = cfg.players.flattenRadius;
            townCenterSO = cfg.startingLoadout.townCenter;
            townCenterPrefabOverride = cfg.startingLoadout.townCenterPrefabOverride;
            tcClearRadius = cfg.startingLoadout.townCenterClearRadius;
            townCenterSpawnYOffset = cfg.startingLoadout.townCenterSpawnYOffset;
            debugLogs = cfg.graphics.debugLogs;
        }

        public float SampleHeight(Vector3 world)
        {
            if (terrain == null || terrain.terrainData == null) return world.y;
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

        /// <summary>
        /// Única fuente de verdad para tamaño de celda, dimensiones en celdas y origen del mapa.
        /// Viene del <see cref="MatchConfig"/> activo y, en compatibilidad legacy, de los campos width/height/centerAtOrigin.
        /// Los campos de grid en <see cref="MapGenConfig"/> del proyecto solo sirven como plantilla (agua, ciudades, etc.);
        /// en runtime se pisan con estos valores para que no haya dos definiciones distintas.
        /// </summary>
        public static void GetAuthoritativeGridLayout(RTSMapGenerator gen, out float cellSizeWorld, out Vector3 origin, out int gridW, out int gridH)
        {
            cellSizeWorld = MatchRuntimeState.DefaultCellSize;
            origin = Vector3.zero;
            gridW = 1;
            gridH = 1;
            if (gen == null) return;

            MatchConfig cfg = gen.ResolveMatchConfig();
            cellSizeWorld = cfg != null ? Mathf.Max(0.01f, cfg.map.cellSize) : MatchRuntimeState.DefaultCellSize;
            if (cellSizeWorld <= 0.0001f)
                cellSizeWorld = MatchRuntimeState.DefaultCellSize;
            gridW = cfg != null ? Mathf.Max(1, cfg.map.width) : Mathf.Max(1, gen.width);
            gridH = cfg != null ? Mathf.Max(1, cfg.map.height) : Mathf.Max(1, gen.height);
            bool centered = cfg != null ? cfg.map.centerAtOrigin : gen.centerAtOrigin;
            if (centered)
                origin = new Vector3(-gridW * cellSizeWorld * 0.5f, gen.transform.position.y, -gridH * cellSizeWorld * 0.5f);
            else
                origin = gen.transform.position;
        }

        /// <summary>Copia el layout autoritativo al <see cref="MapGenConfig"/> usado por <see cref="MapGenerator"/> (instancia en runtime, no el asset en disco).</summary>
        public static void ApplyAuthoritativeGridLayout(RTSMapGenerator gen, MapGenConfig config)
        {
            if (gen == null || config == null) return;
            GetAuthoritativeGridLayout(gen, out float cs, out Vector3 o, out int w, out int h);
            config.cellSizeWorld = cs;
            config.gridW = w;
            config.gridH = h;
            config.origin = o;
        }

        void Awake()
        {
            if (selectionOutlineConfig != null)
                SelectionOutlineConfig.SetGlobal(selectionOutlineConfig);

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
            if (_deferredMapLobby != null && _deferredMapLobby.blockInitialGeneration)
            {
                _deferredMapLobby.Open(this);
                return;
            }
            Generate();
        }

        public void Generate()
        {
            Log("=== Iniciando generación de mapa (Definitivo) ===");
            _runtimeMatchConfig = null;
            MatchRuntimeState.ClearGeneratedWorldBounds();
            MatchConfig activeMatch = ResolveMatchConfig();
            ApplyMatchConfigToLegacyFields(activeMatch);
            if (navMeshSurface == null)
                navMeshSurface = FindFirstObjectByType<NavMeshSurface>();

            if (_grid == null)
                _grid = GetComponent<MapGrid>();
            if (_grid == null)
                _grid = gameObject.AddComponent<MapGrid>();

            if (useLegacyMapPresets)
            {
                ApplyMapPreset();
                if (matchConfig == null)
                    _runtimeMatchConfig = null;
            }
            else if (mapPreset != MapPresetType.Custom)
            {
                Debug.LogWarning(
                    "[MapGen] mapPreset != Custom está ignorado (useLegacyMapPresets=false). " +
                    "Edita MatchConfig (o activa useLegacyMapPresets solo para depuración legacy).");
            }

            // Pipeline legacy retirado: desde ahora el proyecto usa SOLO el Generador Definitivo.
            RunDefinitiveGenerate();
        }

        /// <summary>
        /// Ejecuta el pipeline lógico hasta recursos (sin terreno, mallas de agua ni NavMesh) y devuelve una textura 2D.
        /// Usa <see cref="seed"/>, ríos/lagos del Inspector y <see cref="preferSceneHydrologyOverrides"/> como en la partida.
        /// </summary>
        public bool TryBuildMapPreview(out Texture2D preview, out string failMessage, int textureMaxSize = 400, MapPreviewOverlayMode previewOverlay = MapPreviewOverlayMode.Terrain)
        {
            preview = null;
            failMessage = null;
            _runtimeMatchConfig = null;
            MatchConfig activeMatch = ResolveMatchConfig();
            if (activeMatch == null)
            {
                failMessage = "No hay MatchConfig resuelto.";
                return false;
            }
            PushSceneToMatchForGeneration(activeMatch);

            RuntimeMapGenerationSettings runtime = MatchConfigCompiler.Build(
                activeMatch,
                definitiveMapGenConfig,
                BuildRuntimeGenerationContext(),
                logSummary: false);
            MapGenConfig config = runtime.CompiledMapGen;
            if (config == null)
            {
                failMessage = "Compilación: MapGenConfig null.";
                _lastCompiledSettings = runtime;
                return false;
            }
            _lastCompiledSettings = runtime;
            if (MatchConfigCompiler.ApplyLegacyResourceFallbackFromScene(runtime.Resources, this))
                runtime.MarkLegacyResourceFallbackFromScene();

            ApplyAuthoritativeGridLayout(this, config);

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

            bool ok = generator.Generate(config, null, skipSurfaceExport: true, skipRoadConnectivityValidation: true);
            if (!ok)
            {
                failMessage = "Validación del generador falló; revisa la consola.";
                Destroy(config);
                generator.config = definitiveMapGenConfig;
                return false;
            }

            preview = MapPreviewTextureBuilder.Build(generator.Grid, generator.Cities, textureMaxSize, generator.Grid.SemanticRegions, previewOverlay);
            Destroy(config);
            generator.config = definitiveMapGenConfig;
            if (preview == null)
                failMessage = "No se pudo rasterizar el grid.";
            return preview != null;
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

            globalTreesClusterFraction = preset.globalTreesClusterFraction;
            preferGlobalTreesOnGrassAlphamap = preset.preferTreesOnGrassAlphamap;
            if (preset.overrideTerrainGrassPercent)
            {
                int g = Mathf.Clamp(preset.terrainGrassPercent, 10, 85);
                grassPercent = g;
                int remaining = 100 - g;
                int sumDR = dirtPercent + rockPercent;
                if (sumDR < 1)
                {
                    dirtPercent = remaining / 2;
                    rockPercent = remaining - dirtPercent;
                }
                else
                {
                    dirtPercent = Mathf.RoundToInt(remaining * (dirtPercent / (float)sumDR));
                    rockPercent = remaining - dirtPercent;
                    if (rockPercent < 0) { rockPercent = 0; dirtPercent = remaining; }
                    if (dirtPercent < 0) { dirtPercent = 0; rockPercent = remaining; }
                }
            }

            if (mapPreset == MapPresetType.Forest)
            {
                globalStone = new Vector2Int(150, 220);
                globalGold = new Vector2Int(38, 58);
                globalAnimals = new Vector2Int(6, 14);
            }

            Debug.Log($"🗺️ Preset aplicado: {preset.name} - {preset.description}");
        }

        void RunDefinitiveGenerate()
        {
            MatchConfig activeMatch = ResolveMatchConfig();
            _matchUsedForLastGenerate = activeMatch;
            RuntimeMapGenerationSettings runtime = MatchConfigCompiler.Build(
                activeMatch,
                definitiveMapGenConfig,
                BuildRuntimeGenerationContext(),
                logSummary: true);
            MapGenConfig config = runtime.CompiledMapGen;
            if (config == null)
            {
                Debug.LogError("RTSMapGenerator Definitive: MatchConfigCompiler no produjo MapGenConfig.");
                _lastCompiledSettings = runtime;
                return;
            }

            _lastCompiledSettings = runtime;

            if (MatchConfigCompiler.ApplyLegacyResourceFallbackFromScene(runtime.Resources, this))
                runtime.MarkLegacyResourceFallbackFromScene();

            MatchConfigCompiler.LogResourcePlacementSummary(runtime);

            ApplyAuthoritativeGridLayout(this, config);

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
                Destroy(config);
                generator.config = definitiveMapGenConfig;
                return;
            }

            if (runtime.TerrainFeatures != null)
                TerrainFeatureSummaryBuilder.AppendFromGrid(generator.Grid, config, runtime.TerrainFeatures);
            runtime.SemanticRegions = generator.Grid.SemanticRegions;
            MapGenerationPipelineLogger.LogPostGenerate(runtime, generator.Grid, config);
            if (runtime.UsedHighLevelAlphaConfig)
                MapVisualBinder.LogBindingPlan(activeMatch.visualBinding);

            _grid.InitializeFromGridSystem(generator.Grid);
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
            DestroyPrePlacedVillagersInScene();
            PlaceTownCenters(activeMatch);
            SpawnStartingVillagersAroundTownCenters(activeMatch);
            var popMgr = PopulationManager.FindPrimaryHumanSkirmish();
            if (popMgr != null)
                popMgr.RegisterExistingVillagers();
            float cellSz = mapCellSizeWorld > 0.01f ? mapCellSizeWorld : MapGrid.GetCellSizeOrDefault();
            float tcFootprintRadius = 0f;
            if (townCenterSO != null)
                tcFootprintRadius = Mathf.Max(townCenterSO.size.x, townCenterSO.size.y) * cellSz * 0.5f;
            float minDistFromTc = Mathf.Max(tcClearRadius, tcFootprintRadius) + cellSz * 2f;
            MapResourcePlacer.PlaceFromDefinitiveGrid(generator.Grid, this, runtime.Resources, _townCenterPositions, minDistFromTc);
            MapResourcePlacer.PlaceGlobalOnly(_spawns, this, runtime.Resources);
            ReleaseTownCenterReservations();

            // Notificar cámara RTS (si existe) para que actualice bounds al tamaño del mapa generado.
            var camCtrl = FindFirstObjectByType<Project.Gameplay.RTSCameraController>();
            if (camCtrl != null) camCtrl.RefreshBoundsFromMap();

            if (!rebuildNavMeshOnGenerate)
                AIPlayerBootstrap.SpawnForMatch(_matchUsedForLastGenerate, this);

            StartCoroutine(RebuildNavMeshCoroutine());
            Log("=== Generación Definitiva completada ===");

            Destroy(config);
            generator.config = definitiveMapGenConfig;
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

            var config = new MapWaterMeshGenerator.WaterMeshConfig
            {
                grid = _grid,
                waterHeight = waterHeight,
                waterSurfaceOffset = waterSurfaceOffset,
                waterMaterial = waterMaterial,
                showWater = showWater,
                waterMeshMode = waterMeshMode,
                waterChunkSize = waterChunkSize,
                waterLayerOverride = waterLayerOverride
            };

            _waterRoot = MapWaterMeshGenerator.Generate(config, this, Log);
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

        static bool IsLobbySlotHuman(MatchConfig m, int playerIndex)
        {
            if (m?.players?.slots == null || playerIndex < 0 || playerIndex >= m.players.slots.Count)
                return true;
            return m.players.slots[playerIndex].kind != MatchConfig.PlayerSlotKind.AI;
        }

        static void ApplySkirmishFactionToRoot(GameObject root, bool humanPlayerSlot)
        {
            if (root == null) return;
            var fm = root.GetComponent<FactionMember>();
            if (fm == null) fm = root.AddComponent<FactionMember>();
            fm.faction = humanPlayerSlot ? FactionId.Player : FactionId.Enemy;
        }

        /// <summary>Humano: el PlayerResources global de escena (HUD). IA: uno por Town Center enemigo.</summary>
        static PlayerResources ResolveGathererOwnerResources(int townCenterIndexZero, bool humanSlot)
        {
            if (humanSlot)
            {
                var global = PlayerResources.FindPrimaryHumanSkirmish();
                if (global != null) return global;
            }

            var tcGo = GameObject.Find($"TownCenter_Player{townCenterIndexZero + 1}");
            if (tcGo == null)
                return PlayerResources.FindPrimaryHumanSkirmish();
            var onTc = tcGo.GetComponent<PlayerResources>();
            if (onTc != null) return onTc;
            return tcGo.AddComponent<PlayerResources>();
        }

        void DestroyPrePlacedVillagersInScene()
        {
            var gatherers = FindObjectsByType<VillagerGatherer>(FindObjectsSortMode.None);
            for (int i = 0; i < gatherers.Length; i++)
            {
                if (gatherers[i] == null) continue;
                Destroy(gatherers[i].gameObject);
            }
            if (gatherers.Length > 0)
                Log($"DestroyPrePlacedVillagersInScene: eliminados {gatherers.Length} aldeanos previos (solo TC + 3 nuevos por jugador).");
        }

        void PlaceTownCenters(MatchConfig match)
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
                    NavMeshSpawnSafety.DisableNavMeshAgentsOnHierarchy(tc);
                    AlignTownCenterToTerrain(tc);
                    EnsureWorldBarAnchor(tc);
                    SetLayerRecursive(tc.transform, tc.layer);
                    if (tc.GetComponent<BuildingSelectable>() == null)
                        tc.AddComponent<BuildingSelectable>();
                    tc.name = $"TownCenter_Player{i + 1}";
                    world = tc.transform.position;
                    ApplySkirmishFactionToRoot(tc, IsLobbySlotHuman(match, i));

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

                        bool humanSlot = IsLobbySlotHuman(match, i);
                        if (humanSlot)
                        {
                            var globalPr = PlayerResources.FindPrimaryHumanSkirmish();
                            if (globalPr != null)
                                prod.owner = globalPr;
                            prod.populationManager = PopulationManager.FindPrimaryHumanSkirmish();
                            if (townCenterSO != null && townCenterSO.populationProvided > 0)
                            {
                                var pm = PopulationManager.FindPrimaryHumanSkirmish();
                                if (pm != null)
                                    pm.AddHousingCapacity(townCenterSO.populationProvided);
                            }
                        }
                        else
                        {
                            var prAi = tc.GetComponent<PlayerResources>();
                            if (prAi == null) prAi = tc.AddComponent<PlayerResources>();
                            prod.owner = prAi;
                            var pmAi = tc.GetComponent<PopulationManager>();
                            if (pmAi == null) pmAi = tc.AddComponent<PopulationManager>();
                            pmAi.skipAutoRegisterPopulation = true;
                            prod.populationManager = pmAi;
                            if (townCenterSO != null)
                                pmAi.SetInitialStateForAiTownCenter(townCenterSO.populationProvided, 0);
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
                if (r == null || BuildingTerrainAlignment.ShouldExcludeRendererForBaseAlignment(r)) continue;
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
                    if (c == null || BuildingTerrainAlignment.ShouldExcludeColliderForBaseAlignment(c)) continue;
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

        GameObject ResolveStartingVillagerPrefab()
        {
            if (startingVillagerPrefab != null) return startingVillagerPrefab;
            if (startingVillagerUnitSO != null && startingVillagerUnitSO.prefab != null)
                return startingVillagerUnitSO.prefab;
            return null;
        }

        /// <summary>3 aldeanos por Town Center (humano = bando Player; IA = Enemy para enfrentamiento). Población HUD solo humano.</summary>
        void SpawnStartingVillagersAroundTownCenters(MatchConfig match)
        {
            GameObject villPrefab = ResolveStartingVillagerPrefab();
            if (villPrefab == null)
            {
                Log("SpawnStartingVillagers: asigna startingVillagerPrefab o startingVillagerUnitSO (p. ej. Villager_UnitSO).");
                return;
            }

            if (_townCenterPositions.Count == 0)
                return;

            const int villagersPerPlayer = 3;
            int spawned = 0;
            for (int tcIndex = 0; tcIndex < _townCenterPositions.Count; tcIndex++)
            {
                bool humanSlot = IsLobbySlotHuman(match, tcIndex);
                Vector3 tcPos = _townCenterPositions[tcIndex];
                float radius = GetTownCenterPlacementRadius(tcIndex, tcPos);
                for (int v = 0; v < villagersPerPlayer; v++)
                {
                    float angle = (v / (float)villagersPerPlayer) * 360f * Mathf.Deg2Rad;
                    Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    Vector3 targetPos = tcPos + offset;
                    targetPos.y = SampleHeight(targetPos);
                    GameObject u = Instantiate(villPrefab, targetPos, Quaternion.identity);
                    NavMeshSpawnSafety.DisableNavMeshAgentsOnHierarchy(u);
                    u.name = $"Villager_Player{tcIndex + 1}_{v + 1}";
                    ApplySkirmishFactionToRoot(u, humanSlot);
                    var gatherer = u.GetComponent<VillagerGatherer>();
                    if (gatherer != null)
                        gatherer.owner = ResolveGathererOwnerResources(tcIndex, humanSlot);
                    if (!u.activeSelf) u.SetActive(true);
                    spawned++;
                }

                if (!humanSlot)
                {
                    var tcGo = GameObject.Find($"TownCenter_Player{tcIndex + 1}");
                    var pmAi = tcGo != null ? tcGo.GetComponent<PopulationManager>() : null;
                    if (pmAi != null)
                    {
                        for (int z = 0; z < villagersPerPlayer; z++)
                            pmAi.TryAddPopulation(1);
                    }
                }
            }

            Log($"SpawnStartingVillagers: {spawned} aldeanos ({villagersPerPlayer} por TC).");
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
                float radius = GetTownCenterPlacementRadius(tcIndex, tcPos);

                // Mover unidades a este Town Center
                for (int u = 0; u < unitsPerTC && unitIndex < allAgents.Length; u++, unitIndex++)
                {
                    var agent = allAgents[unitIndex];

                    // Offset en círculo fuera del edificio (radio desde bounds del TC + margen)
                    float angle = (u / (float)unitsPerTC) * 360f * Mathf.Deg2Rad;
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

        /// <summary>Radio mínimo para colocar unidades alrededor del TC (fuera del modelo). Usa bounds del edificio + margen.</summary>
        float GetTownCenterPlacementRadius(int tcIndex, Vector3 tcPos)
        {
            const float margin = 2f;
            const float minRadius = 4f;
            const float fallbackRadius = 8f;

            var tcGo = GameObject.Find($"TownCenter_Player{tcIndex + 1}");
            if (tcGo == null) return fallbackRadius;

            if (TryGetBuildingWorldBounds(tcGo, out Bounds b))
            {
                float halfSize = Mathf.Max(b.extents.x, b.extents.z);
                return Mathf.Max(minRadius, halfSize + margin);
            }
            return fallbackRadius;
        }

        static bool TryGetBuildingWorldBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            var all = root.GetComponentsInChildren<Renderer>(true);
            bool has = false;
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null) continue;
                string n = r.gameObject.name;
                if (n.Equals("GroundDecal", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("BasePlatform", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("SpawnPoint", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("DropAnchor", StringComparison.OrdinalIgnoreCase) ||
                    n.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Platform", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    r.GetComponent<UnityEngine.Canvas>() != null)
                    continue;
                if (!has) { bounds = r.bounds; has = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return has;
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

        /// <summary>
        /// Capas que participan en el bake del NavMesh. Se excluye <see cref="resourceLayerName"/>
        /// para que árboles y demás recursos no aporten mallas caminables (copas, rocas altas, etc.).
        /// </summary>
        LayerMask GetNavMeshCollectLayerMask()
        {
            int mask = ~0;
            string layerName = string.IsNullOrEmpty(resourceLayerName) ? "Resource" : resourceLayerName;
            int resourceLayer = MapResourcePlacer.ResolveResourceLayerIndex(layerName);
            if (resourceLayer >= 0)
                mask &= ~(1 << resourceLayer);
            return mask;
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
            // Excluir la capa de recursos (árboles, piedra, oro, etc.): con RenderMeshes sus mallas
            // (p. ej. copas) se horneaban como suelo transitable.
            navMeshSurface.layerMask = GetNavMeshCollectLayerMask();

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
            float baseR = Mathf.Max(1f, navMeshPostBakeSampleRadius);
            int extraRetries = Mathf.Clamp(navMeshPostBakeSampleRetries, 0, 5);
            
            Log($"Intentando recolocar {agents.Length} unidades (snap radius={baseR:F1}, retries={extraRetries})...");
            
            foreach (var agent in agents)
            {
                // No tocar unidades que están siguiendo ruta A* (su agente está desactivado a propósito)
                var mover = agent.GetComponent<Project.Gameplay.Units.UnitMover>();
                if (mover != null && mover.IsFollowingPath)
                {
                    Log($"Unidad {agent.name} está en ruta A*, no tocar.");
                    continue;
                }

                bool TrySnapToNavMesh(out UnityEngine.AI.NavMeshHit hitOut)
                {
                    Vector3 p = agent.transform.position;
                    float r = baseR;
                    for (int t = 0; t <= extraRetries; t++)
                    {
                        if (UnityEngine.AI.NavMesh.SamplePosition(p, out hitOut, r, UnityEngine.AI.NavMesh.AllAreas))
                            return true;
                        r *= 1.5f;
                    }
                    hitOut = default;
                    return false;
                }

                // Si el agente está deshabilitado, primero recolocar en NavMesh y LUEGO activar
                // (activar antes de recolocar provoca "Failed to create agent because it is not close enough to the NavMesh")
                if (!agent.enabled)
                {
                    if (TrySnapToNavMesh(out var hitDisabled))
                    {
                        agent.transform.position = hitDisabled.position;
                        agent.enabled = true;
                        fixedCount++;
                        Log($"✅ Unidad {agent.name} recolocada y activada en NavMesh");
                    }
                    else
                    {
                        failedCount++;
                        // Mantener desactivado: activar aquí reproduce el error de Unity en masa.
                    }
                    continue;
                }

                if (agent.isOnNavMesh)
                {
                    Log($"Unidad {agent.name} ya está en NavMesh");
                    continue;
                }
                    
                // Agente activo pero no en NavMesh: Warp a posición válida
                if (TrySnapToNavMesh(out var hit))
                {
                    agent.Warp(hit.position);
                    fixedCount++;
                    Log($"✅ Unidad {agent.name} recolocada a {hit.position}");
                }
                else
                {
                    agent.enabled = false;
                    failedCount++;
                }
            }
            
            if (failedCount > 0)
                Debug.LogWarning($"NavMesh post-bake: {fixedCount} unidades OK, {failedCount} sin posición válida (agentes dejados desactivados; revisar radio o terreno).");
            else if (fixedCount > 0)
                Log($"✅ Unidades recolocadas: {fixedCount} exitosas.");

            if (_matchUsedForLastGenerate != null)
                AIPlayerBootstrap.SpawnForMatch(_matchUsedForLastGenerate, this);
        }

        [ContextMenu("Debug: Estado del Generador")]
        void DebugGeneratorState()
        {
            Log("=== ESTADO DEL GENERADOR ===");
            Log($"Terrain asignado: {terrain != null}");
            Log($"Match Config: {matchConfig != null}");
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
