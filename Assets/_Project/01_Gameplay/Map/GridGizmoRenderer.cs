using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Project.Gameplay.Map
{
    public enum GridRenderMode
    {
        [UnityEngine.Tooltip("Líneas con GL (estilo anterior del generador): siempre visibles en ambas direcciones, sin culling.")]
        GLLines,
        [UnityEngine.Tooltip("Mesh con quads: mejor rendimiento en mapas muy grandes.")]
        Mesh
    }

    /// <summary>
    /// Dibuja una cuadrícula en vista Scene (Gizmos) y en Game (GL o Mesh).
    /// Usa el runtime generado del mapa; no mantiene tamaños locales del grid.
    /// </summary>
    public class GridGizmoRenderer : MonoBehaviour
    {
        sealed class GridMeshChunk
        {
            public GameObject go;
            public MeshFilter mf;
            public MeshRenderer mr;
            public Mesh mesh;
            public Bounds worldBounds;
            public int cx, cz;
        }

        [Header("Referencia")]
        [Tooltip("Arrastra aquí el GameObject que tiene el RTS Map Generator (ahí está el MapGrid en Play). Así la grilla usa el mismo tamaño y terreno.")]
        public GameObject mapGridSource;
        [Tooltip("Terreno sobre el que se dibuja la grilla. Si no asignas, se busca en mapGridSource o en la escena.")]
        public Terrain terrain;

        [Header("Visibilidad")]
        [Tooltip("Mostrar grilla en vista Scene (Gizmos). Desactivado por defecto para no generar malla densa fuera de Play.")]
        public bool showInScene = false;
        [Tooltip("Mostrar grilla en Game view.")]
        public bool showInGameView = true;
        [Tooltip("GLLines = dibujo con GL (aspecto bueno, rendimiento malo). Mesh = quads; activa 'Estilo GL en Mesh' para el mismo aspecto con buen rendimiento.")]
        public GridRenderMode gameViewRenderMode = GridRenderMode.Mesh;
        [Tooltip("Qué tan visible: bajo = muy tenue, alto = más marcado. Afecta Scene y Game.")]
        [Range(0.02f, 0.5f)] public float lineAlpha = 0.12f;
        [Tooltip("Si está activo, la grilla solo se muestra mientras estás en Build Mode (BuildingPlacer.IsPlacing).")]
        public bool showOnlyInBuildMode = false;
        [Tooltip("Tecla para activar/desactivar la grilla manualmente (independiente del Build Mode).")]
        public Key toggleKey = Key.Z;
        [Tooltip("Si está activo, la grilla se muestra al iniciar el juego. Si no, queda oculta hasta pulsar la tecla de toggle o entrar en Build Mode.")]
        public bool startWithGridVisible = true;
        [Tooltip("Referencia opcional al BuildingPlacer para detectar Build Mode sin búsquedas por frame.")]
        public Project.Gameplay.Buildings.BuildingPlacer buildingPlacer;

        [Header("Altura sobre el terreno")]
        [Tooltip("Offset sobre la superficie (evita z-fight). Valores bajos (0.01–0.03) evitan que la grilla se vea flotando; sube solo si desaparece en zonas claras.")]
        public float heightOffset = 0.02f;
        [Tooltip("Sumar posición Y del Terrain al SampleHeight. Desactívalo si la grilla queda ~0.5 m por encima (SampleHeight ya en mundo).")]
        public bool addTerrainPositionY = true;
        [Tooltip("Siempre activo: la grilla sigue el relieve en GL y Mesh. Dejar activo para que se vea bien en todo el terreno.")]
        public bool segmentLinesFollowTerrain = true;
        [Tooltip("Recomendado: asigna MAT_Grid (usa shader GridAlwaysOnTop = ZTest Always, se ve en toda la superficie). Si no asignas, se usa un material por defecto que puede desaparecer en arena/zonas claras.")]
        public Material lineMaterialOverride;
        [Tooltip("Si está activo, la grilla no se dibuja sobre unidades, edificios ni recursos (respeta depth: solo se ve en el suelo).")]
        public bool occludeGridOverObjects = true;
        [Tooltip("Segmentos por arista de celda. Se combina con min/max y adaptación por tamaño de mapa.")]
        [Range(1, 5)] public int segmentsPerCellEdge = 2;
        [Tooltip("Legacy/debug: ya no fuerza grosor ni segmentación; GL y Mesh comparten ComputeGridVisualSettings.")]
        public bool meshUseGLStyle = true;

        [Header("Visual unificado (Game View — GL y Mesh)")]
        public bool normalizeGridVisuals = true;
        public bool scaleThicknessWithCamera = true;
        public float referenceCameraDistance = 60f;
        public float minDistanceScale = 0.75f;
        public float maxDistanceScale = 2.25f;
        public bool useCellRelativeThickness = true;
        public float minorThicknessRatio = 0.012f;
        public float majorThicknessRatio = 0.018f;
        public bool adaptiveSegmentsByMapSize = true;
        public int minSegmentsPerCellEdge = 2;
        public int maxSegmentsPerCellEdge = 4;
        [Tooltip("Mesh: si está activo, la segmentación mesh puede superar el tope runtime (minor≤2, major=1).")]
        public bool meshDebugAllowHighSegments = false;
        public bool autoLogActiveRenderModeOnce = true;

        [Header("Debug (diagnóstico grilla/terreno)")]
        [Tooltip("Si está activo, al reconstruir chunk o LOD se loguea conteo de vértices/tris (muy caro en tiempo real; solo diagnóstico).")]
        public bool debugGridMeshPerfStats = false;
        [Tooltip("Si está activo, al entrar en Play se hace un log del Terrain usado y de los bounds del grid vs terreno, y en Scene se dibujan los rectángulos.")]
        public bool debugGridAndTerrainBounds = false;
        [Tooltip("Si está activo, cuenta cuántas veces SampleY usó fallback (terrain null) y lo loguea cada 2 s.")]
        public bool debugLogSampleYFallback = false;
        [Tooltip("Fuerza que la grilla se dibuje encima de todo (ZTest Always). Solo para probar si el problema es depth; la grilla tapará también unidades.")]
        public bool forceGridOnTop = false;

        Material _lineMat;
        Material _lineMatOccludeCopy; // Copia del override con ZTest LEqual cuando occludeGridOverObjects
        Terrain _cachedTerrain;
        static readonly int s_GridLinesLimit = 2048;
        float _placerResolveTimer;
        bool _lastBuildMode;
        bool _toggledByKey;

        /// <summary>Solo benchmark: fuerza la grilla invisible en Game view sin tocar <see cref="showInGameView"/>.</summary>
        bool _benchmarkForceHideGameGrid;

        public void SetBenchmarkForceHideGameGrid(bool hide) => _benchmarkForceHideGameGrid = hide;

        [Header("Major/Minor lines (Game View)")]
        [Tooltip("Cada cuántas celdas dibujar una línea mayor (más marcada). Ej: 4 u 8.")]
        public int majorLineEveryN = 4;
        [Tooltip("Grosor (mundo) de líneas menores. Más grosor = líneas más continuas, menos efecto a rayas.")]
        public float minorLineThickness = 0.035f;
        [Tooltip("Grosor (mundo) de líneas mayores en Game view.")]
        public float majorLineThickness = 0.06f;
        [Tooltip("Multiplicador de alpha para líneas mayores (Game view).")]
        public float majorAlphaMultiplier = 2.0f;

        [Header("Mesh chunks (Game View)")]
        [Min(4)] public int chunkCellsX = 32;
        [Min(4)] public int chunkCellsY = 32;
        [Tooltip("Solo genera geometría minor en chunks visibles (+ margen). Major sigue en todos los chunks.")]
        public bool restrictMinorGridToVisibleArea = true;
        [Range(0f, 0.5f)] public float visibleAreaPaddingPercent = 0.08f;
        [Tooltip("Amplía el test de frustum al marcar chunks visibles (0 = ajustado al FOV).")]
        [Range(0f, 0.35f)] public float chunkFrustumPaddingPercent = 0.05f;
        [Tooltip("Si la lista de chunks minor visibles cambia, reconstruir minor (no cada frame).")]
        [Min(0.05f)] public float minorVisibilityRebuildCooldown = 0.35f;

        Material _minorMat;
        Material _majorMat;
        GameObject _meshRoot;
        Transform _minorChunksParent;
        Transform _majorChunksParent;
        readonly List<GridMeshChunk> _minorChunks = new List<GridMeshChunk>();
        readonly List<GridMeshChunk> _majorChunks = new List<GridMeshChunk>();
        float _minorDirtyTimer;
        float _lodDirtyTimer;
        int _lastChunkCellsX = -1;
        int _lastChunkCellsY = -1;
        bool _lastRestrictMinorVisible;
        float _lastVisiblePad = -1f;
        float _lastFrustumPad = -1f;
        int _numChunksX;
        int _numChunksZ;
        bool[] _chunkRendererVisible;
        bool[] _chunkMinorFill;
        bool[] _chunkBuiltRendererVisible;
        bool[] _chunkBuiltMinorFill;
        float _lastLodRefreshTime;
        const float MajorLodRefreshInterval = 0.12f;
        float _lastCell;
        Vector3 _lastOrigin;
        int _lastW, _lastH;
        int _lastMajorN;
        float _lastMinorTh, _lastMajorTh, _lastAlpha, _lastMajorAlphaMul;
        bool _lastSegmentFollow;
        float _lastHeightOffset;
        bool _lastAddTerrainPosY;
        int _lastSegmentsPerCellEdge;
        float _lastDistanceScale = -999f;
        bool _lastNormalizeGridVisuals;
        bool _lastScaleThicknessWithCamera;
        float _lastReferenceCameraDistance;
        float _lastMinDistanceScale;
        float _lastMaxDistanceScale;
        bool _lastUseCellRelativeThickness;
        float _lastMinorThicknessRatio;
        float _lastMajorThicknessRatio;
        bool _lastAdaptiveSegmentsByMapSize;
        int _lastMinSegmentsPerCellEdge;
        int _lastMaxSegmentsPerCellEdge;
        bool _loggedGridModeOnce;
        bool _loggedGridDataSourceOnce;
        string _lastResolvedGridDataSource = "";
        bool _debugBoundsLoggedOnce;
        int _sampleYFallbackCount;
        float _debugFallbackLogTimer;

        const string GridVisualLayerName = "GridVisual";
        static int s_gridVisualLayerCached = int.MinValue;

        Vector3 _visCamSampleXZ;
        float _visQuantizedScaleBucket = float.NaN;
        bool _hasVisCamVisibilitySample;
        Camera _visCamLastRef;
        bool _lastMeshDebugAllowHighSeg;

        readonly List<Vector3> _meshRebuildVerts = new List<Vector3>(4096);
        readonly List<int> _meshRebuildTris = new List<int>(16384);

        public static int GetGridVisualLayerOrDefault()
        {
            if (s_gridVisualLayerCached != int.MinValue) return s_gridVisualLayerCached;
            int idx = LayerMask.NameToLayer(GridVisualLayerName);
            s_gridVisualLayerCached = idx >= 0 ? idx : 0;
            if (idx < 0)
                Debug.LogWarning("[GridGizmoRenderer] Layer 'GridVisual' no encontrada; usando Default. Añádela en Project Settings > Tags and Layers.");
            return s_gridVisualLayerCached;
        }

        void ApplyGridVisualLayerRecursive(Transform root)
        {
            if (root == null) return;
            int layer = GetGridVisualLayerOrDefault();
            void Visit(Transform t)
            {
                if (t == null) return;
                t.gameObject.layer = layer;
                for (int i = 0; i < t.childCount; i++)
                    Visit(t.GetChild(i));
            }
            Visit(root);
        }

        static void ConfigureGridMeshRenderer(MeshRenderer mr)
        {
            if (mr == null) return;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            mr.allowOcclusionWhenDynamic = false;
        }

        bool ShouldRefreshChunkVisibility(Camera cam, float cellSize, Vector3 origin, int w, int h)
        {
            if (cam != _visCamLastRef)
            {
                _visCamLastRef = cam;
                return true;
            }
            if (!_hasVisCamVisibilitySample) return true;
            if (cam == null) return false;

            Vector3 p = cam.transform.position;
            float dx = p.x - _visCamSampleXZ.x;
            float dz = p.z - _visCamSampleXZ.z;
            if (dx * dx + dz * dz >= cellSize * cellSize) return true;

            float scale = 1f;
            if (normalizeGridVisuals && scaleThicknessWithCamera)
                scale = ComputeGridDistanceScale(cam, origin, w, h, cellSize);
            float q = QuantizeDistanceScale(scale);
            if (float.IsNaN(_visQuantizedScaleBucket) || Mathf.Abs(q - _visQuantizedScaleBucket) > 0.0001f)
                return true;
            return false;
        }

        void RecordVisibilityCameraSample(Camera cam, float cellSize, Vector3 origin, int w, int h)
        {
            if (cam == null)
            {
                _hasVisCamVisibilitySample = true;
                _visQuantizedScaleBucket = float.NaN;
                return;
            }
            _visCamSampleXZ = cam.transform.position;
            _hasVisCamVisibilitySample = true;
            float scale = 1f;
            if (normalizeGridVisuals && scaleThicknessWithCamera)
                scale = ComputeGridDistanceScale(cam, origin, w, h, cellSize);
            _visQuantizedScaleBucket = QuantizeDistanceScale(scale);
        }

        void GetMeshRuntimeSegmentBudget(GridVisualSettings visual, bool chunkFrustumVisible, out int segForMajor, out int segForMinorWhenFilled)
        {
            int baseSeg = Mathf.Max(1, visual.segments);
            if (!chunkFrustumVisible)
            {
                segForMajor = 1;
                segForMinorWhenFilled = 1;
                return;
            }
            if (meshDebugAllowHighSegments)
            {
                segForMajor = Mathf.Max(1, baseSeg);
                segForMinorWhenFilled = Mathf.Max(1, baseSeg);
                return;
            }
            segForMajor = 1;
            segForMinorWhenFilled = Mathf.Min(2, Mathf.Max(1, baseSeg));
        }

        void OnEnable()
        {
            CreateLineMaterial();
            EnsureMeshObjects();
        }

        void OnDisable()
        {
            _loggedGridDataSourceOnce = false;
            _debugBoundsLoggedOnce = false;
            if (_lineMatOccludeCopy != null)
            {
                if (Application.isPlaying) Destroy(_lineMatOccludeCopy);
                else DestroyImmediate(_lineMatOccludeCopy);
                _lineMatOccludeCopy = null;
            }
            if (_lineMat != null && _lineMat != lineMaterialOverride)
            {
                if (Application.isPlaying)
                    Destroy(_lineMat);
                else
                    DestroyImmediate(_lineMat);
                _lineMat = null;
            }
            CleanupMeshObjects();
        }

        void OnDestroy()
        {
            CleanupMeshObjects();
        }

        void CreateLineMaterial()
        {
            if (_lineMat != null) return;
            if (lineMaterialOverride != null) { _lineMat = lineMaterialOverride; ApplyCullOff(_lineMat); return; }
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                _lineMat = new Material(shader);
                ApplyCullOff(_lineMat);
            }
        }

        void ApplyCullOff(Material mat)
        {
            if (mat == null) return;
            // Mesh del grid ya no usa quads doble cara; el culling backface es viable si quieres ahorrar overdraw.
            // Mantener Off por compatibilidad con vistas bajas / normales inciertas del ribbon.
            if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", 0); // 0 = Off
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0); // No escribir depth
            // Ocultar grilla sobre unidades/edificios/recursos: ZTest LEqual (4) para que el depth de esos objetos la tape
            if (occludeGridOverObjects)
            {
                if (mat.HasProperty("_ZTest")) mat.SetInt("_ZTest", 4); // 4 = LEqual
                mat.renderQueue = 3000; // Transparent: se dibuja después de opaques (terreno, unidades, edificios)
                return;
            }
            // Si usa GridAlwaysOnTop (MAT_Grid), el shader ya tiene Queue Overlay y ZTest Always; no sobrescribir
            if (mat.shader != null && mat.shader.name.Contains("GridAlwaysOnTop"))
                return;
            mat.renderQueue = 3500; // Después de Transparent (3000): que arena/otras capas no tapen la grilla
            if (forceGridOnTop && mat.HasProperty("_ZTest")) mat.SetInt("_ZTest", 8); // 8 = Always
        }

        MapGrid GetResolvedMapGrid()
        {
            MapGrid best = FindBestMapGrid();
            if (best != null) return best;
            if (mapGridSource != null) { var g = mapGridSource.GetComponent<MapGrid>(); if (g != null) return g; }
            var grid = GetComponent<MapGrid>();
            if (grid != null) return grid;
            return FindFirstObjectByType<MapGrid>();
        }

        /// <summary>MapGrid listo en escena primero (no forzar mapGridSource/Terrain).</summary>
        MapGrid FindBestMapGrid()
        {
            var found = FindObjectsByType<MapGrid>(FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
            {
                var g = found[i];
                if (g != null && g.IsReady) return g;
            }
            if (mapGridSource != null)
            {
                var g = mapGridSource.GetComponent<MapGrid>();
                if (g != null && g.IsReady) return g;
            }
            var self = GetComponent<MapGrid>();
            if (self != null && self.IsReady) return self;
            return null;
        }

        RTSMapGenerator FindRTSMapGeneratorForLayout()
        {
            var gen = mapGridSource != null ? mapGridSource.GetComponent<RTSMapGenerator>() : null;
            if (gen == null) gen = GetComponent<RTSMapGenerator>();
            if (gen == null)
            {
                var grid = FindFirstObjectByType<MapGrid>();
                if (grid != null) gen = grid.GetComponent<RTSMapGenerator>();
            }
            if (gen == null) gen = FindFirstObjectByType<RTSMapGenerator>();
            return gen;
        }

        bool TryGetGridDataFromTerrain(out float cellSize, out Vector3 origin, out int w, out int h)
        {
            cellSize = Mathf.Max(0.01f, MatchRuntimeState.DefaultCellSize);
            origin = Vector3.zero;
            w = 0;
            h = 0;
            Terrain t = Terrain.activeTerrain;
            if (t == null || t.terrainData == null) return false;
            Vector3 size = t.terrainData.size;
            w = Mathf.Max(1, Mathf.RoundToInt(size.x / cellSize));
            h = Mathf.Max(1, Mathf.RoundToInt(size.z / cellSize));
            origin = t.transform.position;
            return true;
        }

        void LogGridDataSourceOnce(bool terrainFallback)
        {
            if (_loggedGridDataSourceOnce) return;
            _loggedGridDataSourceOnce = true;
            Debug.Log(terrainFallback ? "mapGridSource=Terrain" : "mapGridSource=MapGrid");
        }

        bool GetGridData(out float cellSize, out Vector3 origin, out int w, out int h)
        {
            MapGrid grid = FindBestMapGrid();
            if (grid != null && grid.IsReady)
            {
                cellSize = grid.cellSize;
                origin = grid.origin;
                w = grid.width;
                h = grid.height;
                _lastResolvedGridDataSource = "MapGrid";
                LogGridDataSourceOnce(terrainFallback: false);
                return true;
            }

            RTSMapGenerator gen = FindRTSMapGeneratorForLayout();
            if (gen != null)
            {
                RTSMapGenerator.GetAuthoritativeGridLayout(gen, out cellSize, out origin, out w, out h);
                _lastResolvedGridDataSource = "RTSMapGenerator";
                LogGridDataSourceOnce(terrainFallback: false);
                return true;
            }

            if (MatchRuntimeState.Current != null)
            {
                cellSize = Mathf.Max(0.01f, MatchRuntimeState.Current.map.cellSize);
                w = Mathf.Max(1, MatchRuntimeState.Current.map.width);
                h = Mathf.Max(1, MatchRuntimeState.Current.map.height);
                origin = MatchRuntimeState.Current.map.centerAtOrigin
                    ? new Vector3(-w * cellSize * 0.5f, transform.position.y, -h * cellSize * 0.5f)
                    : transform.position;
                _lastResolvedGridDataSource = "MatchConfig";
                LogGridDataSourceOnce(terrainFallback: false);
                return true;
            }

            if (TryGetGridDataFromTerrain(out cellSize, out origin, out w, out h))
            {
                _lastResolvedGridDataSource = "Terrain";
                LogGridDataSourceOnce(terrainFallback: true);
                return true;
            }

            cellSize = MatchRuntimeState.DefaultCellSize;
            origin = Vector3.zero;
            w = 0;
            h = 0;
            _lastResolvedGridDataSource = "";
            return false;
        }

        void OnValidate()
        {
            // Si está apuntando al Terrain (que también tiene RTSMapGenerator), siempre es válido,
            // pero ayuda a detectar cuando se olvidó de setear mapGridSource.
            if (mapGridSource == null)
            {
                var gen = GetComponent<RTSMapGenerator>();
                if (gen != null)
                    mapGridSource = gen.gameObject;
            }
        }

        Terrain GetTerrain()
        {
            if (terrain != null) { _cachedTerrain = terrain; return terrain; }
            if (_cachedTerrain != null && _cachedTerrain.terrainData != null) return _cachedTerrain;
            var gen = GetComponent<RTSMapGenerator>();
            if (gen != null && gen.terrain != null) { _cachedTerrain = gen.terrain; return _cachedTerrain; }
            if (mapGridSource != null) { gen = mapGridSource.GetComponent<RTSMapGenerator>(); if (gen != null && gen.terrain != null) { _cachedTerrain = gen.terrain; return _cachedTerrain; } }
            var grid = GetResolvedMapGrid();
            if (grid != null) { gen = grid.GetComponent<RTSMapGenerator>(); if (gen != null && gen.terrain != null) { _cachedTerrain = gen.terrain; return _cachedTerrain; } }
            _cachedTerrain = FindFirstObjectByType<Terrain>();
            return _cachedTerrain;
        }

        Camera GetPrimaryCameraForGrid()
        {
            if (Camera.main != null) return Camera.main;
            if (Camera.current != null) return Camera.current;
            return null;
        }

        float ComputeGridDistanceScale(Camera cam, Vector3 origin, int w, int h, float cellSize)
        {
            if (cam == null) return 1f;

            Vector3 center = origin + new Vector3(w * cellSize * 0.5f, 0f, h * cellSize * 0.5f);
            float dist = Vector3.Distance(cam.transform.position, center);
            float scale = dist / Mathf.Max(0.001f, referenceCameraDistance);
            return Mathf.Clamp(scale, minDistanceScale, maxDistanceScale);
        }

        static float QuantizeDistanceScale(float s) => Mathf.Round(s * 4f) / 4f;

        private struct GridVisualSettings
        {
            public float minorThickness;
            public float majorThickness;
            public int segments;
            public float distanceScale;
        }

        GridVisualSettings ComputeGridVisualSettings(float cellSize, Vector3 origin, int w, int h)
        {
            GridVisualSettings s = new GridVisualSettings();

            Camera cam = GetPrimaryCameraForGrid();
            float distanceScale = 1f;

            if (normalizeGridVisuals && scaleThicknessWithCamera)
                distanceScale = ComputeGridDistanceScale(cam, origin, w, h, cellSize);

            float minorTh;
            float majorTh;

            if (normalizeGridVisuals && useCellRelativeThickness)
            {
                minorTh = Mathf.Max(0.001f, cellSize * minorThicknessRatio * distanceScale);
                majorTh = Mathf.Max(0.001f, cellSize * majorThicknessRatio * distanceScale);
            }
            else
            {
                minorTh = Mathf.Max(0.001f, minorLineThickness * distanceScale);
                majorTh = Mathf.Max(0.001f, majorLineThickness * distanceScale);
            }

            int segBase = Mathf.Clamp(segmentsPerCellEdge, minSegmentsPerCellEdge, maxSegmentsPerCellEdge);
            int seg = segBase;

            if (normalizeGridVisuals && adaptiveSegmentsByMapSize)
            {
                int mapMax = Mathf.Max(w, h);
                int extra = 0;
                if (mapMax >= 192) extra = 1;
                if (mapMax >= 320) extra = 2;
                seg = Mathf.Clamp(segBase + extra, minSegmentsPerCellEdge, maxSegmentsPerCellEdge);
            }

            if (Application.isPlaying && !meshDebugAllowHighSegments)
                seg = Mathf.Min(seg, 2);

            s.minorThickness = minorTh;
            s.majorThickness = majorTh;
            s.segments = seg;
            s.distanceScale = distanceScale;
            return s;
        }

        void LogDebugBoundsOnce(float cellSize, Vector3 origin, int w, int h)
        {
            _debugBoundsLoggedOnce = true;
            var t = GetTerrain();
            float gridW = w * cellSize;
            float gridH = h * cellSize;
            string terrainInfo = t != null && t.terrainData != null
                ? $"Terrain '{t.name}' pos={t.transform.position}, size={t.terrainData.size}"
                : "Terrain = null o sin terrainData";
            Debug.Log($"[GridGizmoRenderer] DEBUG BOUNDS | Grid: origin={origin}, size=({gridW}, {gridH}) => XZ=[{origin.x:F1},{origin.x + gridW:F1}] x [{origin.z:F1},{origin.z + gridH:F1}] | {terrainInfo} | gridDataSource={_lastResolvedGridDataSource}");
        }

        void DrawDebugBoundsGizmos(float cellSize, Vector3 origin, int w, int h)
        {
            float gw = w * cellSize;
            float gh = h * cellSize;
            float y = origin.y;
            Vector3 p0 = new Vector3(origin.x, y, origin.z);
            Vector3 p1 = new Vector3(origin.x + gw, y, origin.z);
            Vector3 p2 = new Vector3(origin.x + gw, y, origin.z + gh);
            Vector3 p3 = new Vector3(origin.x, y, origin.z + gh);
            Gizmos.color = Color.red;
            Gizmos.DrawLine(p0, p1); Gizmos.DrawLine(p1, p2); Gizmos.DrawLine(p2, p3); Gizmos.DrawLine(p3, p0);

            var t = GetTerrain();
            if (t != null && t.terrainData != null)
            {
                var pos = t.transform.position;
                var size = t.terrainData.size;
                Vector3 t0 = pos;
                Vector3 t1 = pos + new Vector3(size.x, 0f, 0f);
                Vector3 t2 = pos + new Vector3(size.x, 0f, size.z);
                Vector3 t3 = pos + new Vector3(0f, 0f, size.z);
                Gizmos.color = Color.green;
                Gizmos.DrawLine(t0, t1); Gizmos.DrawLine(t1, t2); Gizmos.DrawLine(t2, t3); Gizmos.DrawLine(t3, t0);
            }
        }

        float SampleY(float worldX, float worldZ, float fallbackY)
        {
            var t = GetTerrain();
            if (t == null || t.terrainData == null)
            {
                if (debugLogSampleYFallback) _sampleYFallbackCount++;
                return fallbackY + heightOffset;
            }

            Vector3 worldPos = new Vector3(worldX, 0f, worldZ);
            float h = t.SampleHeight(worldPos);
            if (addTerrainPositionY) h += t.transform.position.y;
            return h + heightOffset;
        }

        /// <summary>Altura en el "medio" entre dos celdas: promedio de los centros de las celdas adyacentes al borde (wx,wz). Así la línea se ve continua.</summary>
        float SampleYLineMidpoint(float wx, float wz, float cellSize, Vector3 origin, int w, int h, float fallbackY, bool lineIsNS)
        {
            float half = cellSize * 0.5f;
            float a, b;
            if (lineIsNS)
            {
                float left = wx - half, right = wx + half;
                a = (left >= origin.x - 0.001f) ? SampleY(left, wz, fallbackY) : SampleY(wx, wz, fallbackY);
                b = (right <= origin.x + w * cellSize + 0.001f) ? SampleY(right, wz, fallbackY) : SampleY(wx, wz, fallbackY);
            }
            else
            {
                float down = wz - half, up = wz + half;
                a = (down >= origin.z - 0.001f) ? SampleY(wx, down, fallbackY) : SampleY(wx, wz, fallbackY);
                b = (up <= origin.z + h * cellSize + 0.001f) ? SampleY(wx, up, fallbackY) : SampleY(wx, wz, fallbackY);
            }
            return (a + b) * 0.5f;
        }

        bool IsBuildModeActive()
        {
            if (!showOnlyInBuildMode) return true;
            if (!Application.isPlaying) return true; // en editor, mostrar para debug

            if (buildingPlacer == null)
            {
                _placerResolveTimer -= Time.unscaledDeltaTime;
                if (_placerResolveTimer <= 0f)
                {
                    _placerResolveTimer = 1.0f;
                    buildingPlacer = FindFirstObjectByType<Project.Gameplay.Buildings.BuildingPlacer>();
                }
            }
            return buildingPlacer != null && buildingPlacer.IsPlacing;
        }

        /// <summary>Visibilidad en Game: tecla Z alterna <see cref="_toggledByKey"/>; con solo Build Mode, también durante colocación.</summary>
        bool ShouldShowGrid()
        {
            if (!Application.isPlaying) return true;
            if (!showOnlyInBuildMode)
                return _toggledByKey;
            return _toggledByKey || IsBuildModeActive();
        }

        void Start()
        {
            _toggledByKey = startWithGridVisible;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[toggleKey].wasPressedThisFrame)
                _toggledByKey = !_toggledByKey;

            if (debugLogSampleYFallback && _sampleYFallbackCount > 0)
            {
                _debugFallbackLogTimer += Time.unscaledDeltaTime;
                if (_debugFallbackLogTimer >= 2f)
                {
                    Debug.Log($"[GridGizmoRenderer] SampleY usó fallback {_sampleYFallbackCount} veces en los últimos ~2 s (Terrain null o sin terrainData).");
                    _sampleYFallbackCount = 0;
                    _debugFallbackLogTimer = 0f;
                }
            }
        }

        void LateUpdate()
        {
            if (_benchmarkForceHideGameGrid) return;
            if (!showInGameView || !ShouldShowGrid()) return;
            if (gameViewRenderMode != GridRenderMode.Mesh) return;
            if (_meshRoot == null || !_meshRoot.activeSelf) return;
            if (!GetGridData(out float cellSize, out Vector3 origin, out int w, out int h)) return;
            if (_numChunksX <= 0 || _minorChunks.Count != _majorChunks.Count) return;

            int ncx = _numChunksX;
            int n = ncx * _numChunksZ;
            if (_minorChunks.Count != n) return;

            Camera cam = GetPrimaryCameraForGrid();
            if (!ShouldRefreshChunkVisibility(cam, cellSize, origin, w, h))
                return;

            UpdateChunkFrustumFlags(cam, cellSize, origin, w, h, ncx, _numChunksZ);
            RecordVisibilityCameraSample(cam, cellSize, origin, w, h);

            for (int i = 0; i < n; i++)
            {
                if (_majorChunks[i].go != null) _majorChunks[i].go.SetActive(_chunkRendererVisible[i]);
                if (_minorChunks[i].go != null) _minorChunks[i].go.SetActive(_chunkMinorFill[i]);
            }

            bool lodMismatch = _chunkBuiltRendererVisible == null || !CompareBoolArrays(_chunkRendererVisible, _chunkBuiltRendererVisible, n);
            bool minorMismatch = restrictMinorGridToVisibleArea &&
                (_chunkBuiltMinorFill == null || !CompareBoolArrays(_chunkMinorFill, _chunkBuiltMinorFill, n));

            if (lodMismatch) _lodDirtyTimer += Time.unscaledDeltaTime;
            else _lodDirtyTimer = 0f;
            if (minorMismatch) _minorDirtyTimer += Time.unscaledDeltaTime;
            else if (!restrictMinorGridToVisibleArea) _minorDirtyTimer = 0f;

            bool doLod = lodMismatch && Time.unscaledTime - _lastLodRefreshTime >= MajorLodRefreshInterval;
            bool doMinor = minorMismatch && _minorDirtyTimer >= minorVisibilityRebuildCooldown;

            if (!doLod && !doMinor) return;

            if (doLod)
            {
                _lastLodRefreshTime = Time.unscaledTime;
                _lodDirtyTimer = 0f;
            }
            if (doMinor)
                _minorDirtyTimer = 0f;

            int majorN = Mathf.Max(1, majorLineEveryN);
            GridVisualSettings visual = ComputeGridVisualSettings(cellSize, origin, w, h);
            Vector3 meshOrigin = origin;

            bool hasBuilt = _chunkBuiltRendererVisible != null && _chunkBuiltRendererVisible.Length >= n;

            for (int i = 0; i < n; i++)
            {
                int cz = i / ncx;
                int cx = i % ncx;
                bool visDirty = !hasBuilt || _chunkRendererVisible[i] != _chunkBuiltRendererVisible[i];
                bool minorDirty = restrictMinorGridToVisibleArea && _chunkBuiltMinorFill != null && i < _chunkBuiltMinorFill.Length
                    && _chunkMinorFill[i] != _chunkBuiltMinorFill[i];
                bool rebuildMajor = doLod && visDirty;
                bool rebuildMinor = (doLod && visDirty) || (restrictMinorGridToVisibleArea && doMinor && minorDirty);
                if (!rebuildMajor && !rebuildMinor)
                    continue;
                RebuildChunkPair(i, cx, cz, cellSize, meshOrigin, w, h, majorN, visual,
                    _chunkRendererVisible[i], _chunkMinorFill[i],
                    rebuildMajor, rebuildMinor);
            }

            EnsureBuiltMaskArrays(n);
            CopyBoolSlice(_chunkRendererVisible, _chunkBuiltRendererVisible, n);
            if (restrictMinorGridToVisibleArea)
                CopyBoolSlice(_chunkMinorFill, _chunkBuiltMinorFill, n);
            else
            {
                for (int i = 0; i < n; i++) _chunkBuiltMinorFill[i] = true;
            }

            if (debugGridMeshPerfStats)
            {
                AggregateMeshStats(n, out long aggMinorV, out long aggMinorT, out long aggMajorV, out long aggMajorT,
                    out int visCount, out int minorActive, out int majorActive, out int castShadowsN);
                Debug.Log($"[GridGizmoRenderer] Grid perf | chunks={n} | visible={visCount} | minorVerts={aggMinorV} | minorTris={aggMinorT / 3} | majorVerts={aggMajorV} | majorTris={aggMajorT / 3} | minorActive={minorActive} | majorActive={majorActive} | castShadows={castShadowsN}");
            }
        }

        void OnDrawGizmos()
        {
            if (!showInScene) return;
            if (!ShouldShowGrid()) return;
            if (!GetGridData(out float cellSize, out Vector3 origin, out int w, out int h)) return;

            if (debugGridAndTerrainBounds)
                DrawDebugBoundsGizmos(cellSize, origin, w, h);

            int majorN = Mathf.Max(1, majorLineEveryN);
            for (int x = 0; x <= w; x++)
            {
                float a = (x % majorN == 0) ? Mathf.Clamp01(lineAlpha * majorAlphaMultiplier) : lineAlpha;
                Gizmos.color = new Color(1f, 1f, 1f, a);
                float wx = origin.x + x * cellSize;
                for (int z = 0; z < h; z++)
                {
                    float wz0 = origin.z + z * cellSize, wz1 = origin.z + (z + 1) * cellSize;
                    float y0 = SampleYLineMidpoint(wx, wz0, cellSize, origin, w, h, origin.y, true);
                    float y1 = SampleYLineMidpoint(wx, wz1, cellSize, origin, w, h, origin.y, true);
                    Gizmos.DrawLine(new Vector3(wx, y0, wz0), new Vector3(wx, y1, wz1));
                }
            }
            for (int z = 0; z <= h; z++)
            {
                float a = (z % majorN == 0) ? Mathf.Clamp01(lineAlpha * majorAlphaMultiplier) : lineAlpha;
                Gizmos.color = new Color(1f, 1f, 1f, a);
                float wz = origin.z + z * cellSize;
                for (int x = 0; x < w; x++)
                {
                    float wx0 = origin.x + x * cellSize, wx1 = origin.x + (x + 1) * cellSize;
                    float y0 = SampleYLineMidpoint(wx0, wz, cellSize, origin, w, h, origin.y, false);
                    float y1 = SampleYLineMidpoint(wx1, wz, cellSize, origin, w, h, origin.y, false);
                    Gizmos.DrawLine(new Vector3(wx0, y0, wz), new Vector3(wx1, y1, wz));
                }
            }
        }

        void OnRenderObject()
        {
            if (_benchmarkForceHideGameGrid) { SetMeshVisible(false); return; }
            if (!showInGameView) { SetMeshVisible(false); return; }
            if (!ShouldShowGrid()) { SetMeshVisible(false); return; }
            if (!GetGridData(out float cellSize, out Vector3 origin, out int w, out int h)) { SetMeshVisible(false); return; }

            if (debugGridMeshPerfStats && autoLogActiveRenderModeOnce && !_loggedGridModeOnce)
            {
                Debug.Log("[GridGizmoRenderer] Active render mode: " + gameViewRenderMode);
                _loggedGridModeOnce = true;
            }
            else if (autoLogActiveRenderModeOnce && !_loggedGridModeOnce)
                _loggedGridModeOnce = true;

            if (debugGridAndTerrainBounds && !_debugBoundsLoggedOnce)
                LogDebugBoundsOnce(cellSize, origin, w, h);

            Camera camGrid = GetPrimaryCameraForGrid();
            float currentDistanceScale = 1f;
            if (normalizeGridVisuals && scaleThicknessWithCamera)
                currentDistanceScale = ComputeGridDistanceScale(camGrid, origin, w, h, cellSize);
            float quantizedScale = QuantizeDistanceScale(currentDistanceScale);

            if (gameViewRenderMode == GridRenderMode.GLLines)
            {
                SetMeshVisible(false);
                DrawGridGL(cellSize, origin, w, h);
                return;
            }

            EnsureMeshObjects();
            int majorN = Mathf.Max(1, majorLineEveryN);
            bool settingsChanged =
                Mathf.Abs(_lastCell - cellSize) > 0.0001f ||
                _lastOrigin != origin ||
                _lastW != w || _lastH != h ||
                _lastMajorN != majorN ||
                Mathf.Abs(_lastMinorTh - minorLineThickness) > 0.0001f ||
                Mathf.Abs(_lastMajorTh - majorLineThickness) > 0.0001f ||
                Mathf.Abs(_lastAlpha - lineAlpha) > 0.0001f ||
                Mathf.Abs(_lastMajorAlphaMul - majorAlphaMultiplier) > 0.0001f ||
                _lastSegmentFollow != segmentLinesFollowTerrain ||
                Mathf.Abs(_lastHeightOffset - heightOffset) > 0.0001f ||
                _lastAddTerrainPosY != addTerrainPositionY ||
                _lastSegmentsPerCellEdge != segmentsPerCellEdge ||
                _lastNormalizeGridVisuals != normalizeGridVisuals ||
                _lastScaleThicknessWithCamera != scaleThicknessWithCamera ||
                Mathf.Abs(_lastReferenceCameraDistance - referenceCameraDistance) > 0.0001f ||
                Mathf.Abs(_lastMinDistanceScale - minDistanceScale) > 0.0001f ||
                Mathf.Abs(_lastMaxDistanceScale - maxDistanceScale) > 0.0001f ||
                _lastUseCellRelativeThickness != useCellRelativeThickness ||
                Mathf.Abs(_lastMinorThicknessRatio - minorThicknessRatio) > 0.0001f ||
                Mathf.Abs(_lastMajorThicknessRatio - majorThicknessRatio) > 0.0001f ||
                _lastAdaptiveSegmentsByMapSize != adaptiveSegmentsByMapSize ||
                _lastMinSegmentsPerCellEdge != minSegmentsPerCellEdge ||
                _lastMaxSegmentsPerCellEdge != maxSegmentsPerCellEdge ||
                Mathf.Abs(_lastDistanceScale - quantizedScale) > 0.0001f ||
                _lastChunkCellsX != chunkCellsX ||
                _lastChunkCellsY != chunkCellsY ||
                _lastRestrictMinorVisible != restrictMinorGridToVisibleArea ||
                Mathf.Abs(_lastVisiblePad - visibleAreaPaddingPercent) > 0.0001f ||
                Mathf.Abs(_lastFrustumPad - chunkFrustumPaddingPercent) > 0.0001f ||
                _lastMeshDebugAllowHighSeg != meshDebugAllowHighSegments;

            if (settingsChanged)
            {
                _lastCell = cellSize;
                _lastOrigin = origin;
                _lastW = w; _lastH = h;
                _lastMajorN = majorN;
                _lastMinorTh = minorLineThickness;
                _lastMajorTh = majorLineThickness;
                _lastAlpha = lineAlpha;
                _lastMajorAlphaMul = majorAlphaMultiplier;
                _lastSegmentFollow = segmentLinesFollowTerrain;
                _lastHeightOffset = heightOffset;
                _lastAddTerrainPosY = addTerrainPositionY;
                _lastSegmentsPerCellEdge = segmentsPerCellEdge;
                _lastNormalizeGridVisuals = normalizeGridVisuals;
                _lastScaleThicknessWithCamera = scaleThicknessWithCamera;
                _lastReferenceCameraDistance = referenceCameraDistance;
                _lastMinDistanceScale = minDistanceScale;
                _lastMaxDistanceScale = maxDistanceScale;
                _lastUseCellRelativeThickness = useCellRelativeThickness;
                _lastMinorThicknessRatio = minorThicknessRatio;
                _lastMajorThicknessRatio = majorThicknessRatio;
                _lastAdaptiveSegmentsByMapSize = adaptiveSegmentsByMapSize;
                _lastMinSegmentsPerCellEdge = minSegmentsPerCellEdge;
                _lastMaxSegmentsPerCellEdge = maxSegmentsPerCellEdge;
                _lastDistanceScale = quantizedScale;
                _lastChunkCellsX = chunkCellsX;
                _lastChunkCellsY = chunkCellsY;
                _lastRestrictMinorVisible = restrictMinorGridToVisibleArea;
                _lastVisiblePad = visibleAreaPaddingPercent;
                _lastFrustumPad = chunkFrustumPaddingPercent;
                _lastMeshDebugAllowHighSeg = meshDebugAllowHighSegments;

                RebuildGridMeshes(cellSize, origin, w, h, majorN);
            }

            SetMeshVisible(true);
        }

        /// <summary>
        /// Misma política que la malla: <see cref="ComputeGridVisualSettings"/>, misma segmentación y <see cref="SampleYLineMidpoint"/>.
        /// Grosor con triángulos (tiras); no depende de GL line width del driver.
        /// </summary>
        static void EmitGlQuadDoubleSided(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
        {
            GL.Vertex3(v0.x, v0.y, v0.z); GL.Vertex3(v2.x, v2.y, v2.z); GL.Vertex3(v1.x, v1.y, v1.z);
            GL.Vertex3(v0.x, v0.y, v0.z); GL.Vertex3(v3.x, v3.y, v3.z); GL.Vertex3(v2.x, v2.y, v2.z);
            GL.Vertex3(v0.x, v0.y, v0.z); GL.Vertex3(v1.x, v1.y, v1.z); GL.Vertex3(v2.x, v2.y, v2.z);
            GL.Vertex3(v0.x, v0.y, v0.z); GL.Vertex3(v2.x, v2.y, v2.z); GL.Vertex3(v3.x, v3.y, v3.z);
        }

        void DrawGridLineQuadsStripGL(float cellSize, Vector3 origin, int w, int h, int majorN, bool major, float halfT, int segmentsPerCellEdge)
        {
            int seg = Mathf.Max(1, segmentsPerCellEdge);
            for (int x = 0; x <= w; x++)
            {
                if (((x % majorN) == 0) != major) continue;
                float wx = origin.x + x * cellSize;
                for (int z = 0; z < h; z++)
                {
                    for (int s = 0; s < seg; s++)
                    {
                        float t0 = (float)s / seg;
                        float t1 = (float)(s + 1) / seg;
                        float wz0 = origin.z + (z + t0) * cellSize;
                        float wz1 = origin.z + (z + t1) * cellSize;
                        float y0 = SampleYLineMidpoint(wx, wz0, cellSize, origin, w, h, origin.y, true);
                        float y1 = SampleYLineMidpoint(wx, wz1, cellSize, origin, w, h, origin.y, true);
                        EmitGlQuadDoubleSided(
                            new Vector3(wx - halfT, y0, wz0), new Vector3(wx + halfT, y0, wz0),
                            new Vector3(wx + halfT, y1, wz1), new Vector3(wx - halfT, y1, wz1));
                    }
                }
            }
            for (int z = 0; z <= h; z++)
            {
                if (((z % majorN) == 0) != major) continue;
                float wz = origin.z + z * cellSize;
                for (int x = 0; x < w; x++)
                {
                    for (int s = 0; s < seg; s++)
                    {
                        float t0 = (float)s / seg;
                        float t1 = (float)(s + 1) / seg;
                        float wx0 = origin.x + (x + t0) * cellSize;
                        float wx1 = origin.x + (x + t1) * cellSize;
                        float y0 = SampleYLineMidpoint(wx0, wz, cellSize, origin, w, h, origin.y, false);
                        float y1 = SampleYLineMidpoint(wx1, wz, cellSize, origin, w, h, origin.y, false);
                        EmitGlQuadDoubleSided(
                            new Vector3(wx0, y0, wz - halfT), new Vector3(wx0, y0, wz + halfT),
                            new Vector3(wx1, y1, wz + halfT), new Vector3(wx1, y1, wz - halfT));
                    }
                }
            }
        }

        void DrawGridGL(float cellSize, Vector3 origin, int w, int h)
        {
            if (lineMaterialOverride != null) _lineMat = lineMaterialOverride;
            else if (_lineMat == null) CreateLineMaterial();
            if (_lineMat == null) return;

            Material matToUse = _lineMat;
            if (occludeGridOverObjects)
            {
                if (lineMaterialOverride != null)
                {
                    if (_lineMatOccludeCopy == null) _lineMatOccludeCopy = new Material(lineMaterialOverride);
                    ApplyCullOff(_lineMatOccludeCopy);
                    matToUse = _lineMatOccludeCopy;
                }
                else
                    ApplyCullOff(_lineMat);
            }

            if (!matToUse.SetPass(0)) return;

            GridVisualSettings visual = ComputeGridVisualSettings(cellSize, origin, w, h);
            int majorN = Mathf.Max(1, majorLineEveryN);
            int lineCount = (w + 1) + (h + 1);
            if (lineCount > s_GridLinesLimit) return;

            float halfMinor = visual.minorThickness * 0.5f;
            float halfMajor = visual.majorThickness * 0.5f;
            int seg = visual.segments;

            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);

            GL.Begin(GL.TRIANGLES);
            GL.Color(new Color(1f, 1f, 1f, lineAlpha));
            DrawGridLineQuadsStripGL(cellSize, origin, w, h, majorN, major: false, halfMinor, seg);
            GL.End();

            GL.Begin(GL.TRIANGLES);
            GL.Color(new Color(1f, 1f, 1f, Mathf.Clamp01(lineAlpha * Mathf.Max(1f, majorAlphaMultiplier))));
            DrawGridLineQuadsStripGL(cellSize, origin, w, h, majorN, major: true, halfMajor, seg);
            GL.End();

            GL.PopMatrix();
        }

        void EnsureMeshObjects()
        {
            if (_meshRoot != null) return;
            _meshRoot = new GameObject("[GridMesh]");
            _meshRoot.hideFlags = HideFlags.DontSave;
            _meshRoot.transform.SetParent(null);
            _meshRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _meshRoot.transform.localScale = Vector3.one;

            var minorRoot = new GameObject("MinorChunks");
            minorRoot.hideFlags = HideFlags.DontSave;
            minorRoot.transform.SetParent(_meshRoot.transform, false);
            minorRoot.transform.localPosition = Vector3.zero;
            _minorChunksParent = minorRoot.transform;

            var majorRoot = new GameObject("MajorChunks");
            majorRoot.hideFlags = HideFlags.DontSave;
            majorRoot.transform.SetParent(_meshRoot.transform, false);
            majorRoot.transform.localPosition = Vector3.zero;
            _majorChunksParent = majorRoot.transform;

            if (lineMaterialOverride != null) _lineMat = lineMaterialOverride;
            else if (_lineMat == null) CreateLineMaterial();

            if (_lineMat != null)
            {
                if (_minorMat == null) _minorMat = new Material(_lineMat);
                if (_majorMat == null) _majorMat = new Material(_lineMat);
                ApplyCullOff(_minorMat);
                ApplyCullOff(_majorMat);
                _minorMat.color = new Color(1f, 1f, 1f, lineAlpha);
                _majorMat.color = new Color(1f, 1f, 1f, Mathf.Clamp01(lineAlpha * Mathf.Max(1f, majorAlphaMultiplier)));
            }

            ApplyGridVisualLayerRecursive(_meshRoot.transform);
        }

        void CleanupMeshObjects()
        {
            _minorChunks.Clear();
            _majorChunks.Clear();
            if (_meshRoot != null)
            {
                if (Application.isPlaying) Destroy(_meshRoot);
                else DestroyImmediate(_meshRoot);
                _meshRoot = null;
            }
            _minorChunksParent = null;
            _majorChunksParent = null;
            if (_minorMat != null) { if (Application.isPlaying) Destroy(_minorMat); else DestroyImmediate(_minorMat); _minorMat = null; }
            if (_majorMat != null) { if (Application.isPlaying) Destroy(_majorMat); else DestroyImmediate(_majorMat); _majorMat = null; }
            _chunkRendererVisible = null;
            _chunkMinorFill = null;
            _chunkBuiltRendererVisible = null;
            _chunkBuiltMinorFill = null;
        }

        void SetMeshVisible(bool visible)
        {
            if (_meshRoot == null) return;
            if (_meshRoot.activeSelf != visible) _meshRoot.SetActive(visible);
        }

        static bool CompareBoolArrays(bool[] a, bool[] b, int n)
        {
            if (a == null || b == null || a.Length < n || b.Length < n) return false;
            for (int i = 0; i < n; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        static void CopyBoolSlice(bool[] src, bool[] dst, int n)
        {
            for (int i = 0; i < n; i++) dst[i] = src[i];
        }

        void EnsureBuiltMaskArrays(int n)
        {
            if (_chunkBuiltRendererVisible == null || _chunkBuiltRendererVisible.Length != n)
                _chunkBuiltRendererVisible = new bool[n];
            if (_chunkBuiltMinorFill == null || _chunkBuiltMinorFill.Length != n)
                _chunkBuiltMinorFill = new bool[n];
        }

        void EnsureVisibilityArrays(int n)
        {
            if (_chunkRendererVisible == null || _chunkRendererVisible.Length != n)
                _chunkRendererVisible = new bool[n];
            if (_chunkMinorFill == null || _chunkMinorFill.Length != n)
                _chunkMinorFill = new bool[n];
        }

        static bool BoundsVisibleInFrustum(Plane[] planes, Bounds worldBounds, float inflatePercent)
        {
            Bounds b = worldBounds;
            if (inflatePercent > 0f)
                b.extents *= (1f + inflatePercent);
            return GeometryUtility.TestPlanesAABB(planes, b);
        }

        Bounds ComputeChunkWorldBounds(int cx, int cz, float cellSize, Vector3 origin, int w, int h)
        {
            float x0 = origin.x + cx * chunkCellsX * cellSize;
            float x1 = origin.x + Mathf.Min((cx + 1) * chunkCellsX, w) * cellSize;
            float z0 = origin.z + cz * chunkCellsY * cellSize;
            float z1 = origin.z + Mathf.Min((cz + 1) * chunkCellsY, h) * cellSize;
            float y00 = SampleY(x0, z0, origin.y);
            float y01 = SampleY(x0, z1, origin.y);
            float y10 = SampleY(x1, z0, origin.y);
            float y11 = SampleY(x1, z1, origin.y);
            float ymin = Mathf.Min(Mathf.Min(y00, y01), Mathf.Min(y10, y11));
            float ymax = Mathf.Max(Mathf.Max(y00, y01), Mathf.Max(y10, y11));
            float ypad = Mathf.Max(25f, cellSize * 4f);
            var center = new Vector3((x0 + x1) * 0.5f, (ymin + ymax) * 0.5f, (z0 + z1) * 0.5f);
            var size = new Vector3(Mathf.Max(0.01f, x1 - x0), Mathf.Max(ypad, ymax - ymin + ypad), Mathf.Max(0.01f, z1 - z0));
            return new Bounds(center, size);
        }

        void UpdateChunkFrustumFlags(Camera cam, float cellSize, Vector3 origin, int w, int h, int ncx, int ncz)
        {
            int n = ncx * ncz;
            EnsureVisibilityArrays(n);
            if (cam == null)
            {
                for (int i = 0; i < n; i++)
                {
                    _chunkRendererVisible[i] = true;
                    _chunkMinorFill[i] = true;
                }
                return;
            }

            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
            for (int i = 0; i < n; i++)
            {
                int cz = i / ncx;
                int cx = i % ncx;
                Bounds b = ComputeChunkWorldBounds(cx, cz, cellSize, origin, w, h);
                if (i < _minorChunks.Count) _minorChunks[i].worldBounds = b;
                if (i < _majorChunks.Count) _majorChunks[i].worldBounds = b;

                _chunkRendererVisible[i] = BoundsVisibleInFrustum(planes, b, chunkFrustumPaddingPercent);
                _chunkMinorFill[i] = !restrictMinorGridToVisibleArea ||
                    BoundsVisibleInFrustum(planes, b, chunkFrustumPaddingPercent + visibleAreaPaddingPercent);
            }
        }

        GridMeshChunk CreateChunkEntry(Transform parent, string prefix, Material mat, int cx, int cz)
        {
            var go = new GameObject($"{prefix}_{cx}_{cz}");
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = GetGridVisualLayerOrDefault();
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            ConfigureGridMeshRenderer(mr);
            var mesh = new Mesh { name = $"{prefix}_{cx}_{cz}" };
            mesh.MarkDynamic();
            mesh.indexFormat = IndexFormat.UInt32;
            mf.sharedMesh = mesh;
            if (mat != null) mr.sharedMaterial = mat;
            return new GridMeshChunk { go = go, mf = mf, mr = mr, mesh = mesh, cx = cx, cz = cz };
        }

        void SyncChunkLists(int ncx, int ncz)
        {
            EnsureMeshObjects();
            int n = ncx * ncz;

            while (_minorChunks.Count > n)
            {
                int last = _minorChunks.Count - 1;
                var c = _minorChunks[last];
                if (c.go != null)
                {
                    if (Application.isPlaying) Destroy(c.go);
                    else DestroyImmediate(c.go);
                }
                _minorChunks.RemoveAt(last);
            }
            while (_majorChunks.Count > n)
            {
                int last = _majorChunks.Count - 1;
                var c = _majorChunks[last];
                if (c.go != null)
                {
                    if (Application.isPlaying) Destroy(c.go);
                    else DestroyImmediate(c.go);
                }
                _majorChunks.RemoveAt(last);
            }

            for (int i = _minorChunks.Count; i < n; i++)
            {
                int cz = i / ncx;
                int cx = i % ncx;
                _minorChunks.Add(CreateChunkEntry(_minorChunksParent, "Minor", _minorMat, cx, cz));
                _majorChunks.Add(CreateChunkEntry(_majorChunksParent, "Major", _majorMat, cx, cz));
            }

            if (_meshRoot != null)
                ApplyGridVisualLayerRecursive(_meshRoot.transform);
        }

        static void ClearChunkMesh(GridMeshChunk ch)
        {
            if (ch.mesh == null) return;
            ch.mesh.Clear(false);
            ch.mesh.indexFormat = IndexFormat.UInt32;
        }

        static void AddQuadSingleSided(List<Vector3> verts, List<int> tris, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
        {
            int b = verts.Count;
            verts.Add(v0);
            verts.Add(v1);
            verts.Add(v2);
            verts.Add(v3);
            tris.Add(b + 0);
            tris.Add(b + 2);
            tris.Add(b + 1);
        }

        void BuildLineQuadsMeshSegmentedChunk(List<Vector3> verts, List<int> tris, float cellSize, Vector3 gridOrigin, Vector3 meshOrigin,
            int w, int h, int majorN, bool major, float halfT, int segmentsPerCellEdge,
            int cx, int cz, int vxStart, int vxEndEx, int hzStart, int hzEndEx, int zCellStart, int zCellEndEx, int xCellStart, int xCellEndEx)
        {
            int seg = Mathf.Max(1, segmentsPerCellEdge);
            for (int vl = vxStart; vl < vxEndEx; vl++)
            {
                if (((vl % majorN) == 0) != major) continue;
                float wx = gridOrigin.x + vl * cellSize;
                for (int z = zCellStart; z < zCellEndEx; z++)
                {
                    for (int s = 0; s < seg; s++)
                    {
                        float t0 = (float)s / seg;
                        float t1 = (float)(s + 1) / seg;
                        float wz0 = gridOrigin.z + (z + t0) * cellSize;
                        float wz1 = gridOrigin.z + (z + t1) * cellSize;
                        float y0 = SampleYLineMidpoint(wx, wz0, cellSize, gridOrigin, w, h, gridOrigin.y, true);
                        float y1 = SampleYLineMidpoint(wx, wz1, cellSize, gridOrigin, w, h, gridOrigin.y, true);
                        AddQuadSingleSided(verts, tris,
                            new Vector3(wx - halfT, y0, wz0) - meshOrigin, new Vector3(wx + halfT, y0, wz0) - meshOrigin,
                            new Vector3(wx + halfT, y1, wz1) - meshOrigin, new Vector3(wx - halfT, y1, wz1) - meshOrigin);
                    }
                }
            }

            for (int hl = hzStart; hl < hzEndEx; hl++)
            {
                if (((hl % majorN) == 0) != major) continue;
                float wz = gridOrigin.z + hl * cellSize;
                for (int x = xCellStart; x < xCellEndEx; x++)
                {
                    for (int s = 0; s < seg; s++)
                    {
                        float t0 = (float)s / seg;
                        float t1 = (float)(s + 1) / seg;
                        float wx0 = gridOrigin.x + (x + t0) * cellSize;
                        float wx1 = gridOrigin.x + (x + t1) * cellSize;
                        float y0 = SampleYLineMidpoint(wx0, wz, cellSize, gridOrigin, w, h, gridOrigin.y, false);
                        float y1 = SampleYLineMidpoint(wx1, wz, cellSize, gridOrigin, w, h, gridOrigin.y, false);
                        AddQuadSingleSided(verts, tris,
                            new Vector3(wx0, y0, wz - halfT) - meshOrigin, new Vector3(wx0, y0, wz + halfT) - meshOrigin,
                            new Vector3(wx1, y1, wz + halfT) - meshOrigin, new Vector3(wx1, y1, wz - halfT) - meshOrigin);
                    }
                }
            }
        }

        void UploadChunkMesh(GridMeshChunk ch, List<Vector3> verts, List<int> tris)
        {
            ch.mesh.Clear(false);
            ch.mesh.indexFormat = IndexFormat.UInt32;
            if (verts.Count == 0 || tris.Count == 0)
            {
                ch.mesh.SetVertices(System.Array.Empty<Vector3>());
                ch.mesh.SetTriangles(System.Array.Empty<int>(), 0);
            }
            else
            {
                ch.mesh.SetVertices(verts);
                ch.mesh.SetTriangles(tris, 0);
            }
            ch.mesh.RecalculateBounds();
        }

        void RebuildChunkPair(int index, int cx, int cz, float cellSize, Vector3 meshOrigin, int w, int h, int majorN,
            GridVisualSettings visual, bool frustumVisible, bool minorFillOk,
            bool rebuildMajor, bool rebuildMinor)
        {
            Vector3 gridOrigin = meshOrigin;
            int vxStart = cx * chunkCellsX;
            int vxEndEx = Mathf.Min((cx + 1) * chunkCellsX, w + 1);
            int hzStart = cz * chunkCellsY;
            int hzEndEx = Mathf.Min((cz + 1) * chunkCellsY, h + 1);
            int zCellStart = cz * chunkCellsY;
            int zCellEndEx = Mathf.Min((cz + 1) * chunkCellsY, h);
            int xCellStart = cx * chunkCellsX;
            int xCellEndEx = Mathf.Min((cx + 1) * chunkCellsX, w);

            GetMeshRuntimeSegmentBudget(visual, frustumVisible, out int segMajorBudget, out int segMinorBudget);
            bool skipMinorMesh = restrictMinorGridToVisibleArea && !minorFillOk;

            if (rebuildMinor && index < _minorChunks.Count)
            {
                var minorCh = _minorChunks[index];
                if (skipMinorMesh)
                {
                    ClearChunkMesh(minorCh);
                }
                else
                {
                    _meshRebuildVerts.Clear();
                    _meshRebuildTris.Clear();
                    float halfMinor = visual.minorThickness * 0.5f;
                    BuildLineQuadsMeshSegmentedChunk(_meshRebuildVerts, _meshRebuildTris, cellSize, gridOrigin, meshOrigin, w, h, majorN, false, halfMinor, segMinorBudget,
                        cx, cz, vxStart, vxEndEx, hzStart, hzEndEx, zCellStart, zCellEndEx, xCellStart, xCellEndEx);
                    UploadChunkMesh(minorCh, _meshRebuildVerts, _meshRebuildTris);
                }
            }

            if (rebuildMajor && index < _majorChunks.Count)
            {
                var majorCh = _majorChunks[index];
                if (!frustumVisible)
                {
                    ClearChunkMesh(majorCh);
                }
                else
                {
                    _meshRebuildVerts.Clear();
                    _meshRebuildTris.Clear();
                    float halfMajor = visual.majorThickness * 0.5f;
                    BuildLineQuadsMeshSegmentedChunk(_meshRebuildVerts, _meshRebuildTris, cellSize, gridOrigin, meshOrigin, w, h, majorN, true, halfMajor, segMajorBudget,
                        cx, cz, vxStart, vxEndEx, hzStart, hzEndEx, zCellStart, zCellEndEx, xCellStart, xCellEndEx);
                    UploadChunkMesh(majorCh, _meshRebuildVerts, _meshRebuildTris);
                }
            }
        }

        void RebuildGridMeshes(float cellSize, Vector3 origin, int w, int h, int majorN)
        {
            EnsureMeshObjects();
            _meshRoot.transform.SetPositionAndRotation(origin, Quaternion.identity);

            int ncx = Mathf.Max(1, (w + chunkCellsX - 1) / chunkCellsX);
            int ncz = Mathf.Max(1, (h + chunkCellsY - 1) / chunkCellsY);
            _numChunksX = ncx;
            _numChunksZ = ncz;
            int n = ncx * ncz;

            SyncChunkLists(ncx, ncz);

            if (_minorMat != null) _minorMat.color = new Color(1f, 1f, 1f, lineAlpha);
            if (_majorMat != null) _majorMat.color = new Color(1f, 1f, 1f, Mathf.Clamp01(lineAlpha * Mathf.Max(1f, majorAlphaMultiplier)));

            GridVisualSettings visual = ComputeGridVisualSettings(cellSize, origin, w, h);
            Camera cam = GetPrimaryCameraForGrid();
            UpdateChunkFrustumFlags(cam, cellSize, origin, w, h, ncx, ncz);

            Vector3 meshOrigin = origin;

            for (int i = 0; i < n; i++)
            {
                int cz = i / ncx;
                int cx = i % ncx;
                RebuildChunkPair(i, cx, cz, cellSize, meshOrigin, w, h, majorN, visual,
                    _chunkRendererVisible[i], _chunkMinorFill[i],
                    rebuildMajor: true, rebuildMinor: true);
            }

            EnsureBuiltMaskArrays(n);
            CopyBoolSlice(_chunkRendererVisible, _chunkBuiltRendererVisible, n);
            if (restrictMinorGridToVisibleArea)
                CopyBoolSlice(_chunkMinorFill, _chunkBuiltMinorFill, n);
            else
            {
                for (int i = 0; i < n; i++) _chunkBuiltMinorFill[i] = true;
            }

            _lastLodRefreshTime = Time.unscaledTime;
            _lodDirtyTimer = 0f;
            _minorDirtyTimer = 0f;

            RecordVisibilityCameraSample(cam, cellSize, origin, w, h);

            if (debugGridMeshPerfStats)
            {
                AggregateMeshStats(n, out long totalMinorV, out long totalMinorT, out long totalMajorV, out long totalMajorT,
                    out int visCount, out int minorActive, out int majorActive, out int castShadowsN);
                Debug.Log($"[GridGizmoRenderer] Grid perf | chunks={n} | visible={visCount} | minorVerts={totalMinorV} | minorTris={totalMinorT / 3} | majorVerts={totalMajorV} | majorTris={totalMajorT / 3} | minorActive={minorActive} | majorActive={majorActive} | castShadows={castShadowsN}");
            }
        }

        void AggregateMeshStats(int n, out long totalMinorV, out long totalMinorT, out long totalMajorV, out long totalMajorT,
            out int visibleChunks, out int minorActive, out int majorActive, out int castShadowsEnabledCount)
        {
            totalMinorV = totalMajorV = 0;
            totalMinorT = totalMajorT = 0;
            visibleChunks = 0;
            minorActive = 0;
            majorActive = 0;
            castShadowsEnabledCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (_chunkRendererVisible != null && i < _chunkRendererVisible.Length && _chunkRendererVisible[i])
                    visibleChunks++;
            }
            for (int i = 0; i < n; i++)
            {
                if (i < _minorChunks.Count)
                {
                    var c = _minorChunks[i];
                    if (c.mesh != null)
                    {
                        totalMinorV += c.mesh.vertexCount;
                        totalMinorT += c.mesh.triangles.Length;
                    }
                    if (c.go != null && c.go.activeInHierarchy && c.mr != null && c.mr.enabled)
                        minorActive++;
                    if (c.mr != null && c.mr.shadowCastingMode != ShadowCastingMode.Off)
                        castShadowsEnabledCount++;
                }
                if (i < _majorChunks.Count)
                {
                    var c = _majorChunks[i];
                    if (c.mesh != null)
                    {
                        totalMajorV += c.mesh.vertexCount;
                        totalMajorT += c.mesh.triangles.Length;
                    }
                    if (c.go != null && c.go.activeInHierarchy && c.mr != null && c.mr.enabled)
                        majorActive++;
                    if (c.mr != null && c.mr.shadowCastingMode != ShadowCastingMode.Off)
                        castShadowsEnabledCount++;
                }
            }
        }
    }
}
