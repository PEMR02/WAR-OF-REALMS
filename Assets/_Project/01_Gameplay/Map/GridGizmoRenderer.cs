using UnityEngine;
using UnityEngine.InputSystem;

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
    /// Si asignas MapGrid, usa su tamaño; si no, busca MapGrid en la escena o usa gridSize/halfSize.
    /// </summary>
    public class GridGizmoRenderer : MonoBehaviour
    {
        [Header("Referencia")]
        [Tooltip("Arrastra aquí el GameObject que tiene el RTS Map Generator (ahí está el MapGrid en Play). Así la grilla usa el mismo tamaño y terreno.")]
        public GameObject mapGridSource;
        [Tooltip("Terreno sobre el que se dibuja la grilla. Si no asignas, se busca en mapGridSource o en la escena.")]
        public Terrain terrain;

        [Header("Fallback (sin MapGrid)")]
        public float gridSize = 1f;
        public int halfSize = 50;

        [Header("Visibilidad")]
        [Tooltip("Mostrar grilla en vista Scene (Gizmos).")]
        public bool showInScene = true;
        [Tooltip("Mostrar grilla en Game view.")]
        public bool showInGameView = true;
        [Tooltip("GLLines = dibujo con GL (aspecto bueno, rendimiento malo). Mesh = quads; activa 'Estilo GL en Mesh' para el mismo aspecto con buen rendimiento.")]
        public GridRenderMode gameViewRenderMode = GridRenderMode.Mesh;
        [Tooltip("Qué tan visible: bajo = muy tenue, alto = más marcado. Afecta Scene y Game.")]
        [Range(0.02f, 0.5f)] public float lineAlpha = 0.12f;
        [Tooltip("Si está activo, la grilla solo se muestra mientras estás en Build Mode (BuildingPlacer.IsPlacing).")]
        public bool showOnlyInBuildMode = true;
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
        [Tooltip("Segmentos por arista de celda en modo Mesh. Más segmentos = la línea sigue mejor el relieve. Con 'Estilo GL en Mesh' se fuerza 1.")]
        [Range(1, 5)] public int segmentsPerCellEdge = 3;
        [Tooltip("Si está activo, el Mesh usa el mismo aspecto que GL (líneas finas, 1 segmento por arista) con buen rendimiento. Recomendado.")]
        public bool meshUseGLStyle = true;

        [Header("Debug (diagnóstico grilla/terreno)")]
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

        [Header("Major/Minor lines (Game View)")]
        [Tooltip("Cada cuántas celdas dibujar una línea mayor (más marcada). Ej: 4 u 8.")]
        public int majorLineEveryN = 4;
        [Tooltip("Grosor (mundo) de líneas menores. Más grosor = líneas más continuas, menos efecto a rayas.")]
        public float minorLineThickness = 0.035f;
        [Tooltip("Grosor (mundo) de líneas mayores en Game view.")]
        public float majorLineThickness = 0.06f;
        [Tooltip("Multiplicador de alpha para líneas mayores (Game view).")]
        public float majorAlphaMultiplier = 2.0f;

        Mesh _minorMesh;
        Mesh _majorMesh;
        Material _minorMat;
        Material _majorMat;
        GameObject _meshRoot;
        MeshFilter _minorMf;
        MeshFilter _majorMf;
        MeshRenderer _minorMr;
        MeshRenderer _majorMr;
        float _lastCell;
        Vector3 _lastOrigin;
        int _lastW, _lastH;
        int _lastMajorN;
        float _lastMinorTh, _lastMajorTh, _lastAlpha, _lastMajorAlphaMul;
        bool _lastSegmentFollow;
        float _lastHeightOffset;
        bool _lastAddTerrainPosY;
        int _lastSegmentsPerCellEdge;
        bool _lastMeshUseGLStyle;
        bool _debugBoundsLoggedOnce;
        int _sampleYFallbackCount;
        float _debugFallbackLogTimer;

        void OnEnable()
        {
            CreateLineMaterial();
            EnsureMeshObjects();
        }

        void OnDisable()
        {
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
            if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", 0); // 0 = Off, visible desde ambos lados
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
            if (mapGridSource != null) { var g = mapGridSource.GetComponent<MapGrid>(); if (g != null) return g; }
            var grid = GetComponent<MapGrid>();
            if (grid != null) return grid;
            return FindFirstObjectByType<MapGrid>();
        }

        bool GetGridData(out float cellSize, out Vector3 origin, out int w, out int h)
        {
            var grid = GetResolvedMapGrid();
            if (grid != null && grid.IsReady)
            {
                cellSize = grid.cellSize;
                origin = grid.origin;
                w = grid.width;
                h = grid.height;
                return true;
            }
            if (gridSize > 0.0001f)
            {
                cellSize = gridSize;
                origin = transform.position - new Vector3(halfSize * gridSize, 0f, halfSize * gridSize);
                w = halfSize * 2;
                h = halfSize * 2;
                return true;
            }
            cellSize = 1f; origin = Vector3.zero; w = 0; h = 0;
            return false;
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

        void LogDebugBoundsOnce(float cellSize, Vector3 origin, int w, int h)
        {
            _debugBoundsLoggedOnce = true;
            var t = GetTerrain();
            float gridW = w * cellSize;
            float gridH = h * cellSize;
            string terrainInfo = t != null && t.terrainData != null
                ? $"Terrain '{t.name}' pos={t.transform.position}, size={t.terrainData.size}"
                : "Terrain = null o sin terrainData";
            Debug.Log($"[GridGizmoRenderer] DEBUG BOUNDS | Grid: origin={origin}, size=({gridW}, {gridH}) => XZ=[{origin.x:F1},{origin.x + gridW:F1}] x [{origin.z:F1},{origin.z + gridH:F1}] | {terrainInfo} | mapGridSource={((object)mapGridSource?.name ?? "null")}");
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

        /// <summary>True si la grilla debe mostrarse: tecla Z activa, o (según opciones) Build Mode.</summary>
        bool ShouldShowGrid()
        {
            if (_toggledByKey) return true;
            if (!showOnlyInBuildMode) return true;
            if (!Application.isPlaying) return true;
            return IsBuildModeActive();
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
            if (!showInGameView) { SetMeshVisible(false); return; }
            if (!ShouldShowGrid()) { SetMeshVisible(false); return; }
            if (!GetGridData(out float cellSize, out Vector3 origin, out int w, out int h)) { SetMeshVisible(false); return; }

            if (debugGridAndTerrainBounds && !_debugBoundsLoggedOnce)
                LogDebugBoundsOnce(cellSize, origin, w, h);

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
                _lastMeshUseGLStyle != meshUseGLStyle;

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
                _lastMeshUseGLStyle = meshUseGLStyle;

                RebuildGridMeshes(cellSize, origin, w, h, majorN);
            }

            SetMeshVisible(true);
        }

        /// <summary>Dibuja la grilla con GL.LINES (estilo anterior: líneas puras, sin culling, siempre visibles en ambas direcciones).</summary>
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

            int majorN = Mathf.Max(1, majorLineEveryN);
            int lineCount = (w + 1) + (h + 1);
            if (lineCount > s_GridLinesLimit) return;

            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.LINES);
            GL.Color(new Color(1f, 1f, 1f, lineAlpha));

            // Altura "a la mitad" entre celdas (promedio de centros adyacentes) para que la línea se vea continua.
            for (int x = 0; x <= w; x++)
            {
                float wx = origin.x + x * cellSize;
                for (int z = 0; z < h; z++)
                {
                    float wz0 = origin.z + z * cellSize;
                    float wz1 = origin.z + (z + 1) * cellSize;
                    float y0 = SampleYLineMidpoint(wx, wz0, cellSize, origin, w, h, origin.y, true);
                    float y1 = SampleYLineMidpoint(wx, wz1, cellSize, origin, w, h, origin.y, true);
                    GL.Vertex3(wx, y0, wz0);
                    GL.Vertex3(wx, y1, wz1);
                }
            }
            for (int z = 0; z <= h; z++)
            {
                float wz = origin.z + z * cellSize;
                for (int x = 0; x < w; x++)
                {
                    float wx0 = origin.x + x * cellSize;
                    float wx1 = origin.x + (x + 1) * cellSize;
                    float y0 = SampleYLineMidpoint(wx0, wz, cellSize, origin, w, h, origin.y, false);
                    float y1 = SampleYLineMidpoint(wx1, wz, cellSize, origin, w, h, origin.y, false);
                    GL.Vertex3(wx0, y0, wz);
                    GL.Vertex3(wx1, y1, wz);
                }
            }

            GL.End();
            GL.PopMatrix();
        }

        void EnsureMeshObjects()
        {
            if (_meshRoot != null) return;
            _meshRoot = new GameObject("[GridMesh]");
            _meshRoot.hideFlags = HideFlags.DontSave;
            // Grilla en espacio mundo: parent null para que los vértices (en coords mundo) coincidan con el terreno
            _meshRoot.transform.SetParent(null);
            _meshRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _meshRoot.transform.localScale = Vector3.one;

            var minorGo = new GameObject("Minor");
            minorGo.hideFlags = HideFlags.DontSave;
            minorGo.transform.SetParent(_meshRoot.transform, false);
            _minorMf = minorGo.AddComponent<MeshFilter>();
            _minorMr = minorGo.AddComponent<MeshRenderer>();

            var majorGo = new GameObject("Major");
            majorGo.hideFlags = HideFlags.DontSave;
            majorGo.transform.SetParent(_meshRoot.transform, false);
            _majorMf = majorGo.AddComponent<MeshFilter>();
            _majorMr = majorGo.AddComponent<MeshRenderer>();

            if (_minorMesh == null) { _minorMesh = new Mesh { name = "GridMinor" }; _minorMesh.MarkDynamic(); }
            if (_majorMesh == null) { _majorMesh = new Mesh { name = "GridMajor" }; _majorMesh.MarkDynamic(); }
            _minorMf.sharedMesh = _minorMesh;
            _majorMf.sharedMesh = _majorMesh;

            // Material: preferir override; si no, crear uno Unlit sencillo.
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
                _minorMr.sharedMaterial = _minorMat;
                _majorMr.sharedMaterial = _majorMat;
            }
        }

        void CleanupMeshObjects()
        {
            if (_meshRoot != null)
            {
                if (Application.isPlaying) Destroy(_meshRoot);
                else DestroyImmediate(_meshRoot);
                _meshRoot = null;
            }
            if (_minorMat != null) { if (Application.isPlaying) Destroy(_minorMat); else DestroyImmediate(_minorMat); _minorMat = null; }
            if (_majorMat != null) { if (Application.isPlaying) Destroy(_majorMat); else DestroyImmediate(_majorMat); _majorMat = null; }
            // Meshes se dejan; Unity las gestiona con el objeto.
        }

        void SetMeshVisible(bool visible)
        {
            if (_meshRoot == null) return;
            if (_meshRoot.activeSelf != visible) _meshRoot.SetActive(visible);
        }

        void RebuildGridMeshes(float cellSize, Vector3 origin, int w, int h, int majorN)
        {
            // Limitar para evitar mallas enormes
            int lineCount = (w + 1) + (h + 1);
            if (lineCount > s_GridLinesLimit)
            {
                _minorMesh.Clear(false);
                _majorMesh.Clear(false);
                return;
            }

            if (_minorMat != null) _minorMat.color = new Color(1f, 1f, 1f, lineAlpha);
            if (_majorMat != null) _majorMat.color = new Color(1f, 1f, 1f, Mathf.Clamp01(lineAlpha * Mathf.Max(1f, majorAlphaMultiplier)));

            int seg = meshUseGLStyle ? 1 : Mathf.Clamp(segmentsPerCellEdge, 1, 5);
            float minorTh = meshUseGLStyle ? 0.018f : Mathf.Max(0.001f, minorLineThickness);
            float majorTh = meshUseGLStyle ? 0.026f : Mathf.Max(0.001f, majorLineThickness);
            BuildLineQuadsMesh(_minorMesh, cellSize, origin, w, h, majorN, major: false, thickness: minorTh, seg);
            BuildLineQuadsMesh(_majorMesh, cellSize, origin, w, h, majorN, major: true, thickness: majorTh, seg);
        }

        void BuildLineQuadsMesh(Mesh mesh, float cellSize, Vector3 origin, int w, int h, int majorN, bool major, float thickness, int segmentsPerCellEdge)
        {
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            float halfT = thickness * 0.5f;
            // Siempre segmentado (como GL): sigue el relieve en toda la superficie; el modo flat no se usa.
            BuildLineQuadsMeshSegmented(verts, tris, cellSize, origin, w, h, majorN, major, halfT, segmentsPerCellEdge);

            mesh.Clear(false);
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Añade un quad visible por ambos lados (evita que una dirección desaparezca por culling).</summary>
        static void AddQuadDoubleSided(System.Collections.Generic.List<Vector3> verts, System.Collections.Generic.List<int> tris, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
        {
            int b = verts.Count;
            verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
            tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b + 0); tris.Add(b + 3); tris.Add(b + 2);
            tris.Add(b + 0); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 3);
        }

        void BuildLineQuadsMeshFlat(System.Collections.Generic.List<Vector3> verts, System.Collections.Generic.List<int> tris, float cellSize, Vector3 origin, int w, int h, int majorN, bool major, float halfT)
        {
            for (int x = 0; x <= w; x++)
            {
                if (((x % majorN) == 0) != major) continue;
                float wx = origin.x + x * cellSize;
                float z0 = origin.z, z1 = origin.z + h * cellSize;
                float y0 = SampleY(wx, z0, origin.y), y1 = SampleY(wx, z1, origin.y);
                AddQuadDoubleSided(verts, tris,
                    new Vector3(wx - halfT, y0, z0), new Vector3(wx + halfT, y0, z0),
                    new Vector3(wx + halfT, y1, z1), new Vector3(wx - halfT, y1, z1));
            }
            for (int z = 0; z <= h; z++)
            {
                if (((z % majorN) == 0) != major) continue;
                float wz = origin.z + z * cellSize;
                float x0 = origin.x, x1 = origin.x + w * cellSize;
                float y0 = SampleY(x0, wz, origin.y), y1 = SampleY(x1, wz, origin.y);
                AddQuadDoubleSided(verts, tris,
                    new Vector3(x0, y0, wz - halfT), new Vector3(x0, y0, wz + halfT),
                    new Vector3(x1, y1, wz + halfT), new Vector3(x1, y1, wz - halfT));
            }
        }

        /// <summary>Líneas segmentadas: subdivisión por arista (segmentsPerCellEdge) para seguir mejor el relieve y evitar que la grilla desaparezca en pendientes.</summary>
        void BuildLineQuadsMeshSegmented(System.Collections.Generic.List<Vector3> verts, System.Collections.Generic.List<int> tris, float cellSize, Vector3 origin, int w, int h, int majorN, bool major, float halfT, int segmentsPerCellEdge)
        {
            int seg = Mathf.Max(1, segmentsPerCellEdge);
            // N-S: varias subdivisiones por arista de celda
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
                        AddQuadDoubleSided(verts, tris,
                            new Vector3(wx - halfT, y0, wz0), new Vector3(wx + halfT, y0, wz0),
                            new Vector3(wx + halfT, y1, wz1), new Vector3(wx - halfT, y1, wz1));
                    }
                }
            }
            // E-O: igual, subdivisión por arista
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
                        AddQuadDoubleSided(verts, tris,
                            new Vector3(wx0, y0, wz - halfT), new Vector3(wx0, y0, wz + halfT),
                            new Vector3(wx1, y1, wz + halfT), new Vector3(wx1, y1, wz - halfT));
                    }
                }
            }
        }
    }
}
