using UnityEngine;

namespace Project.Gameplay.Map
{
    /// <summary>
    /// Dibuja una cuadrícula muy tenue en vista Scene (Gizmos) y en Game (GL).
    /// Si asignas MapGrid, usa su tamaño; si no, busca MapGrid en la escena o usa gridSize/halfSize.
    /// Se ajusta al terreno generado cada partida usando el MapGrid actual.
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
        [Tooltip("Qué tan visible: bajo = muy tenue, alto = más marcado. Afecta Scene y Game.")]
        [Range(0.02f, 0.5f)] public float lineAlpha = 0.06f;
        [Tooltip("Si está activo, la grilla solo se muestra mientras estás en Build Mode (BuildingPlacer.IsPlacing).")]
        public bool showOnlyInBuildMode = true;
        [Tooltip("Referencia opcional al BuildingPlacer para detectar Build Mode sin búsquedas por frame.")]
        public Project.Gameplay.Buildings.BuildingPlacer buildingPlacer;

        [Header("Altura sobre el terreno")]
        [Tooltip("Offset sobre la superficie del terreno (evita z-fight). La grilla sigue la topografía si hay Terrain asignado.")]
        public float heightOffset = 0.02f;
        [Tooltip("Opcional: en URP puede fallar el material por defecto; asigna un material Unlit/Color aquí.")]
        public Material lineMaterialOverride;

        Material _lineMat;
        Terrain _cachedTerrain;
        static readonly int s_GridLinesLimit = 2048;
        float _placerResolveTimer;
        bool _lastBuildMode;

        [Header("Major/Minor lines (Game View)")]
        [Tooltip("Cada cuántas celdas dibujar una línea mayor (más marcada). Ej: 4 u 8.")]
        public int majorLineEveryN = 4;
        [Tooltip("Grosor (mundo) de líneas menores en Game view.")]
        public float minorLineThickness = 0.015f;
        [Tooltip("Grosor (mundo) de líneas mayores en Game view.")]
        public float majorLineThickness = 0.04f;
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

        void OnEnable()
        {
            CreateLineMaterial();
            EnsureMeshObjects();
        }

        void OnDisable()
        {
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
            if (lineMaterialOverride != null) { _lineMat = lineMaterialOverride; return; }
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader != null)
                _lineMat = new Material(shader);
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

        float SampleY(float worldX, float worldZ, float fallbackY)
        {
            var t = GetTerrain();
            if (t == null || t.terrainData == null) return fallbackY + heightOffset;

            Vector3 worldPos = new Vector3(worldX, 0f, worldZ);
            float terrainY = t.SampleHeight(worldPos) + t.transform.position.y;
            return terrainY + heightOffset;
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

        void OnDrawGizmos()
        {
            if (!showInScene) return;
            if (!IsBuildModeActive()) return;
            if (!GetGridData(out float cellSize, out Vector3 origin, out int w, out int h)) return;

            int majorN = Mathf.Max(1, majorLineEveryN);
            for (int x = 0; x <= w; x++)
            {
                float wx = origin.x + x * cellSize;
                float y0 = SampleY(wx, origin.z, origin.y);
                float y1 = SampleY(wx, origin.z + h * cellSize, origin.y);
                float a = (x % majorN == 0) ? Mathf.Clamp01(lineAlpha * majorAlphaMultiplier) : lineAlpha;
                Gizmos.color = new Color(1f, 1f, 1f, a);
                Gizmos.DrawLine(new Vector3(wx, y0, origin.z), new Vector3(wx, y1, origin.z + h * cellSize));
            }
            for (int z = 0; z <= h; z++)
            {
                float wz = origin.z + z * cellSize;
                float y0 = SampleY(origin.x, wz, origin.y);
                float y1 = SampleY(origin.x + w * cellSize, wz, origin.y);
                float a = (z % majorN == 0) ? Mathf.Clamp01(lineAlpha * majorAlphaMultiplier) : lineAlpha;
                Gizmos.color = new Color(1f, 1f, 1f, a);
                Gizmos.DrawLine(new Vector3(origin.x, y0, wz), new Vector3(origin.x + w * cellSize, y1, wz));
            }
        }

        void OnRenderObject()
        {
            if (!showInGameView) { SetMeshVisible(false); return; }
            if (!IsBuildModeActive()) { SetMeshVisible(false); return; }
            EnsureMeshObjects();
            if (!GetGridData(out float cellSize, out Vector3 origin, out int w, out int h)) { SetMeshVisible(false); return; }

            int majorN = Mathf.Max(1, majorLineEveryN);
            bool settingsChanged =
                Mathf.Abs(_lastCell - cellSize) > 0.0001f ||
                _lastOrigin != origin ||
                _lastW != w || _lastH != h ||
                _lastMajorN != majorN ||
                Mathf.Abs(_lastMinorTh - minorLineThickness) > 0.0001f ||
                Mathf.Abs(_lastMajorTh - majorLineThickness) > 0.0001f ||
                Mathf.Abs(_lastAlpha - lineAlpha) > 0.0001f ||
                Mathf.Abs(_lastMajorAlphaMul - majorAlphaMultiplier) > 0.0001f;

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

                RebuildGridMeshes(cellSize, origin, w, h, majorN);
            }

            SetMeshVisible(true);
        }

        void EnsureMeshObjects()
        {
            if (_meshRoot != null) return;
            _meshRoot = new GameObject("[GridMesh]");
            _meshRoot.hideFlags = HideFlags.DontSave;
            _meshRoot.transform.SetParent(transform, false);

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

            BuildLineQuadsMesh(_minorMesh, cellSize, origin, w, h, majorN, major: false, thickness: Mathf.Max(0.001f, minorLineThickness));
            BuildLineQuadsMesh(_majorMesh, cellSize, origin, w, h, majorN, major: true, thickness: Mathf.Max(0.001f, majorLineThickness));
        }

        void BuildLineQuadsMesh(Mesh mesh, float cellSize, Vector3 origin, int w, int h, int majorN, bool major, float thickness)
        {
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();

            float halfT = thickness * 0.5f;

            // Verticales (x fijo)
            for (int x = 0; x <= w; x++)
            {
                bool isMajor = (x % majorN) == 0;
                if (isMajor != major) continue;

                float wx = origin.x + x * cellSize;
                float z0 = origin.z;
                float z1 = origin.z + h * cellSize;
                float y0 = SampleY(wx, z0, origin.y);
                float y1 = SampleY(wx, z1, origin.y);

                int baseIdx = verts.Count;
                verts.Add(new Vector3(wx - halfT, y0, z0));
                verts.Add(new Vector3(wx + halfT, y0, z0));
                verts.Add(new Vector3(wx + halfT, y1, z1));
                verts.Add(new Vector3(wx - halfT, y1, z1));
                tris.Add(baseIdx + 0); tris.Add(baseIdx + 2); tris.Add(baseIdx + 1);
                tris.Add(baseIdx + 0); tris.Add(baseIdx + 3); tris.Add(baseIdx + 2);
            }

            // Horizontales (z fijo)
            for (int z = 0; z <= h; z++)
            {
                bool isMajor = (z % majorN) == 0;
                if (isMajor != major) continue;

                float wz = origin.z + z * cellSize;
                float x0 = origin.x;
                float x1 = origin.x + w * cellSize;
                float y0 = SampleY(x0, wz, origin.y);
                float y1 = SampleY(x1, wz, origin.y);

                int baseIdx = verts.Count;
                verts.Add(new Vector3(x0, y0, wz - halfT));
                verts.Add(new Vector3(x0, y0, wz + halfT));
                verts.Add(new Vector3(x1, y1, wz + halfT));
                verts.Add(new Vector3(x1, y1, wz - halfT));
                tris.Add(baseIdx + 0); tris.Add(baseIdx + 2); tris.Add(baseIdx + 1);
                tris.Add(baseIdx + 0); tris.Add(baseIdx + 3); tris.Add(baseIdx + 2);
            }

            mesh.Clear(false);
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
