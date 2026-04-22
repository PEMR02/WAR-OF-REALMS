using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Project.Gameplay;
using Project.Gameplay.Map;
using Project.Gameplay.Units;
using Project.Gameplay.Buildings;
using Project.Gameplay.Resources;

namespace Project.UI
{
    /// <summary>
    /// Conecta un minimapa funcional al contenedor de HUD sin depender de referencias manuales.
    /// - Busca MinimapViewport
    /// - Crea/ajusta RenderTexture
    /// - Crea/ajusta cámara ortográfica superior
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-1000)]
    public sealed class RuntimeMinimapBootstrap : MonoBehaviour
    {
        const string BootstrapObjectName = "__RuntimeMinimapBootstrap";
        const string MinimapCameraName = "MinimapCamera_RT";
        const int UILayer = 5;

        [Header("Minimap Fit")]
        [SerializeField] bool autoFitWorld = true;
        [SerializeField] bool useFixedOrthographicSize = false;
        [SerializeField, Min(10f)] float fixedOrthographicSize = 160f;
        [SerializeField, Min(1.0f)] float mapPaddingFactor = 1.05f;
        [SerializeField, Min(0.2f)] float boundsRefreshEvery = 1.0f;

        [Header("Minimap rendimiento (mapas grandes)")]
        [Tooltip("Si true y hay textura asignada, no se renderiza la cámara del minimapa (base estática).")]
        public bool useStaticMinimapBackground = false;
        [Tooltip("Textura del mapa completo; solo se usa si useStaticMinimapBackground.")]
        public Texture2D staticMinimapBackground;
        [Tooltip("Render manual a intervalos en Play; reduce coste GPU.")]
        public bool throttledMinimapUpdate = true;
        [Tooltip("Segundos entre renders del minimapa (Play + throttled).")]
        public float minimapUpdateInterval = 0.20f;
        [Tooltip("Resolución fija de la RT (cuadrada); reemplaza RT más grandes.")]
        public int minimapTextureSize = 256;
        public bool minimapDisableShadows = true;
        public bool minimapDisableHdr = true;
        public bool minimapDisableMsaa = true;

        [Header("View Rect")]
        [SerializeField, Min(1f)] float viewRectThickness = 3f;
        [SerializeField] Color viewRectColor = new Color(0.3f, 0.9f, 1f, 0.95f);
        [SerializeField, Range(0.4f, 1.2f)] float viewRectScale = 1f;

        [Header("UI Size")]
        [Tooltip("Tamaño mínimo del minimapa en píxeles de referencia (1080p). Aumentar para agrandar.")]
        [SerializeField, Range(100f, 600f)] float uiScaleMultiplier = 260f;
        [Tooltip("Limitar el tamaño máximo del minimapa.")]
        [SerializeField, Min(100f)] float uiMaxSize = 600f;

        [Header("UV (relleno visual)")]
        [Tooltip("Recorta horizontalmente la textura del minimapa para reducir bandas negras si la RT tiene letterboxing.")]
        [SerializeField] bool cropUvHorizontal = true;
        [SerializeField, Range(0f, 0.45f)] float uvHorizontalInset = 0.1f;

        [Header("Iconos (unidades/edificios/recursos)")]
        [SerializeField] bool showUnitIcons = true;
        [SerializeField] bool showBuildingIcons = true;
        [SerializeField] bool showSpecialResourceIcons = true;
        [Tooltip("Objetos con MinimapWorldIconTarget (objetivos u otros marcados a mano).")]
        [SerializeField] bool showImportantMarkerIcons = true;
        [SerializeField] Color unitIconColor = new Color(0.2f, 0.9f, 0.3f, 0.95f);
        [SerializeField] Color buildingIconColor = new Color(0.3f, 0.6f, 1f, 0.95f);
        [SerializeField] Color specialResourceIconColor = new Color(0.95f, 0.75f, 0.2f, 0.95f);
        [SerializeField] Color importantMarkerIconColor = new Color(1f, 0.4f, 0.85f, 0.95f);
        [SerializeField, Min(1)] int minimapResourceMinStone = 80;
        [SerializeField, Min(1)] int minimapResourceMinFood = 120;
        [SerializeField, Range(4f, 16f)] float iconSizePx = 8f;
        [SerializeField, Min(0.05f)] float iconRefreshInterval = 0.1f;
        [SerializeField] bool minimapIconDebugLog = false;

        [Header("Pings")]
        [SerializeField] bool enablePings = true;
        [SerializeField] float pingDuration = 4f;
        [SerializeField] UnityEngine.InputSystem.Key pingModifierKey = UnityEngine.InputSystem.Key.LeftAlt;
        [SerializeField] Color pingColor = new Color(1f, 0.85f, 0.2f, 0.9f);
        [SerializeField, Range(4f, 20f)] float pingSizePx = 10f;

        Camera _minimapCamera;
        Camera _mainCamera;
        RenderTexture _renderTexture;
        int _renderTextureAllocW;
        int _renderTextureAllocH;
        Bounds _worldBounds;
        bool _hasWorldBounds;
        Vector3 _mapPlaneOrigin;
        float _mapPlaneWidth = 1f;
        float _mapPlaneHeight = 1f;
        bool _warnedMissingViewport;
        float _nextBoundsRefreshTime;
        float _nextMinimapRenderTime;
        Vector3 _lastMainCamSamplePos = new Vector3(float.NaN, 0f, 0f);
        Quaternion _lastMainCamSampleRot = Quaternion.identity;
        float _lastMainCamOrthoOrFov = float.NaN;
        bool _minimapDiagLogged;

        float _minimapWorldQueryCacheTime = -999f;
        BuildingInstance[] _cachedBuildings = System.Array.Empty<BuildingInstance>();
        ResourceNode[] _cachedResourceNodes = System.Array.Empty<ResourceNode>();
        MinimapWorldIconTarget[] _cachedMarkers = System.Array.Empty<MinimapWorldIconTarget>();
        const float MinimapWorldQueryInterval = 0.35f;

        readonly List<MinimapIconEntry> _iconEntryScratch = new List<MinimapIconEntry>(128);
        readonly HashSet<int> _iconSeenScratch = new HashSet<int>();
        readonly HashSet<int> _iconWantScratch = new HashSet<int>();
        readonly List<int> _iconReleaseScratch = new List<int>(32);

        RectTransform _viewRectTop;
        RectTransform _viewRectBottom;
        RectTransform _viewRectLeft;
        RectTransform _viewRectRight;

        LayoutElement _minimapContainerLayout;
        RectTransform _minimapRawRect;
        RawImage _minimapRawImage;
        RTSCameraController _rtsCameraController;
        bool _minimapDragging;

        /// <summary>Solo benchmark: suprime render manual/RT de la cámara del minimapa (overlay puede seguir).</summary>
        bool _benchmarkSuppressMinimapRender;

        RectTransform _iconOverlay;
        readonly List<Image> _minimapIconPool = new List<Image>(64);
        readonly Dictionary<int, int> _minimapTargetIdToPoolIndex = new Dictionary<int, int>(128);
        readonly Stack<int> _minimapFreeIconIndices = new Stack<int>(64);
        float _iconRefreshTimer;

        struct MinimapIconEntry
        {
            public Transform Target;
            public Color Color;
        }

        struct MinimapPing { public Vector3 worldPos; public float endTime; }
        readonly List<MinimapPing> _pings = new List<MinimapPing>();
        readonly List<Image> _pingImages = new List<Image>();

        /// <summary>True cuando el cursor está sobre el área del minimapa. Consultado por RTSSelectionController.</summary>
        public static bool IsPointerOverMinimap { get; private set; }

        public void SetBenchmarkMinimapRenderSuppressed(bool suppressed)
        {
            _benchmarkSuppressMinimapRender = suppressed;
            if (suppressed && _minimapCamera != null)
                _minimapCamera.enabled = false;
        }

        public void SetBenchmarkIconOverlaySuppressed(bool suppressed)
        {
            if (_iconOverlay != null)
                _iconOverlay.gameObject.SetActive(!suppressed);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureBootstrap()
        {
            var existing = GameObject.Find(BootstrapObjectName);
            if (existing != null && existing.GetComponent<RuntimeMinimapBootstrap>() != null)
                return;

            var go = new GameObject(BootstrapObjectName);
            DontDestroyOnLoad(go);
            go.AddComponent<RuntimeMinimapBootstrap>();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void EnsureBootstrapInEditor()
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying) return;

                var existing = GameObject.Find(BootstrapObjectName);
                if (existing != null && existing.GetComponent<RuntimeMinimapBootstrap>() != null)
                    return;

                var go = new GameObject(BootstrapObjectName);
                go.hideFlags = HideFlags.DontSave;
                go.AddComponent<RuntimeMinimapBootstrap>();
            };
        }
#endif

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SetupMinimapForActiveScene();
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            IsPointerOverMinimap = false;
        }

        void OnDestroy()
        {
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }

            _renderTextureAllocW = 0;
            _renderTextureAllocH = 0;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _minimapDiagLogged = false;
            _minimapWorldQueryCacheTime = -999f;
            ClearMinimapIconBindings();
            SetupMinimapForActiveScene();
        }

        void ClearMinimapIconBindings()
        {
            _minimapTargetIdToPoolIndex.Clear();
            _minimapFreeIconIndices.Clear();
            for (int i = 0; i < _minimapIconPool.Count; i++)
            {
                if (_minimapIconPool[i] != null)
                    _minimapIconPool[i].gameObject.SetActive(false);
            }
        }

        void LateUpdate()
        {
            if (_minimapCamera == null)
                return;

            if (_minimapRawImage != null)
                SyncRenderTextureToMinimapRect(_minimapRawImage);

            if (autoFitWorld && Time.unscaledTime >= _nextBoundsRefreshTime)
            {
                _nextBoundsRefreshTime = Time.unscaledTime + boundsRefreshEvery;
                ConfigureCameraFromWorldBounds();
            }

            if (_mainCamera == null)
                _mainCamera = ResolveMainCamera();

            UpdateCameraViewRect();

            if (!Application.isPlaying)
            {
                if (!useStaticMinimapBackground && _minimapCamera != null)
                    _minimapCamera.enabled = true;
                return;
            }

            TickMinimapRenderPipeline();

            HandleMinimapClick();
            UpdateMinimapIcons();
            UpdateMinimapPings();
        }

        void TickMinimapRenderPipeline()
        {
            if (_minimapCamera == null) return;
            if (_benchmarkSuppressMinimapRender)
            {
                _minimapCamera.enabled = false;
                return;
            }
            if (useStaticMinimapBackground && staticMinimapBackground != null)
            {
                _minimapCamera.enabled = false;
                return;
            }

            bool manual = throttledMinimapUpdate;
            _minimapCamera.enabled = !manual;

            if (!manual)
                return;

            bool due = Time.unscaledTime >= _nextMinimapRenderTime;
            if (!due && !MainCameraRelevantChange())
                return;

            _nextMinimapRenderTime = Time.unscaledTime + Mathf.Max(0.05f, minimapUpdateInterval);
            _minimapCamera.Render();
            SnapshotMainCameraForMinimapThrottle();
        }

        void HandleMinimapClick()
        {
            if (_minimapCamera == null || _minimapRawRect == null)
            {
                IsPointerOverMinimap = false;
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                IsPointerOverMinimap = false;
                return;
            }

            Vector2 mousePos = mouse.position.ReadValue();

            // Cámara del Canvas (Screen Space Overlay usa null)
            Camera canvasCamera = null;
            var canvas = _minimapRawRect.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                canvasCamera = canvas.worldCamera;

            bool overRect = false;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _minimapRawRect, mousePos, canvasCamera, out Vector2 localPoint))
            {
                overRect = _minimapRawRect.rect.Contains(localPoint);
            }

            IsPointerOverMinimap = overRect;

            bool pressed = mouse.leftButton.wasPressedThisFrame;
            bool held    = mouse.leftButton.isPressed;

            if (!pressed && !held)
            {
                _minimapDragging = false;
                return;
            }

            if (!overRect)
            {
                _minimapDragging = false;
                return;
            }

            // Solo actuar si es el primer frame de click, o si ya estábamos arrastrando
            if (!pressed && !_minimapDragging) return;
            _minimapDragging = held;

            // UV normalizado [0,1]
            Rect rect = _minimapRawRect.rect;
            float u = (localPoint.x - rect.xMin) / rect.width;
            float v = (localPoint.y - rect.yMin) / rect.height;

            Vector3 worldPos;
            if (useStaticMinimapBackground && staticMinimapBackground != null && _hasWorldBounds)
                worldPos = WorldPositionFromMinimapUv(u, v);
            else
            {
                Ray ray = _minimapCamera.ViewportPointToRay(new Vector3(u, v, 0f));
                float groundY = GetGroundPlaneY();
                var plane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
                if (!plane.Raycast(ray, out float dist)) return;
                worldPos = ray.GetPoint(dist);
            }

            // Ping: Alt + click deja marcador en el minimapa
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (enablePings && kb != null && kb[pingModifierKey].isPressed)
            {
                _pings.Add(new MinimapPing { worldPos = worldPos, endTime = Time.time + pingDuration });
                return;
            }

            // Mover la cámara RTS
            if (_rtsCameraController == null)
                _rtsCameraController = FindFirstObjectByType<RTSCameraController>();
            if (_rtsCameraController != null)
                _rtsCameraController.MoveToWorldPosition(worldPos);
        }

        void EnsureMinimapIconOverlay(Transform rawParent)
        {
            if (rawParent == null) return;

            var t = rawParent.Find("MinimapIconOverlay");
            if (t == null)
            {
                var go = new GameObject("MinimapIconOverlay", typeof(RectTransform));
                go.transform.SetParent(rawParent, false);
                t = go.transform;

                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);

                var canvas = go.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = 1;
                var gr = go.AddComponent<GraphicRaycaster>();
                gr.enabled = false;
            }

            _iconOverlay = t.GetComponent<RectTransform>();
        }

        void RefreshMinimapWorldQueriesIfStale()
        {
            if (Time.unscaledTime - _minimapWorldQueryCacheTime < MinimapWorldQueryInterval)
                return;
            _minimapWorldQueryCacheTime = Time.unscaledTime;
            if (showBuildingIcons)
                _cachedBuildings = FindObjectsByType<BuildingInstance>(FindObjectsSortMode.None);
            if (showSpecialResourceIcons)
                _cachedResourceNodes = FindObjectsByType<ResourceNode>(FindObjectsSortMode.None);
            if (showImportantMarkerIcons)
                _cachedMarkers = FindObjectsByType<MinimapWorldIconTarget>(FindObjectsSortMode.None);
        }

        void UpdateMinimapIcons()
        {
            if (!showUnitIcons && !showBuildingIcons && !showSpecialResourceIcons && !showImportantMarkerIcons)
                return;
            if (_iconOverlay == null || !_iconOverlay.gameObject.activeSelf || _minimapCamera == null) return;

            _iconRefreshTimer -= Time.unscaledDeltaTime;
            if (_iconRefreshTimer > 0f) return;
            _iconRefreshTimer = iconRefreshInterval;

            RefreshMinimapWorldQueriesIfStale();

            _iconEntryScratch.Clear();
            _iconSeenScratch.Clear();
            var entries = _iconEntryScratch;
            var seenTargetIds = _iconSeenScratch;
            int skippedDecorative = 0;
            int duplicatesPrevented = 0;
            int iconsCreatedThisPass = 0;

            void TryAddEntry(Transform t, Color c)
            {
                if (t == null) return;
                int id = t.GetInstanceID();
                if (!seenTargetIds.Add(id))
                {
                    duplicatesPrevented++;
                    return;
                }
                entries.Add(new MinimapIconEntry { Target = t, Color = c });
            }

            if (showUnitIcons)
            {
                var allUnits = UnitSelectableRegistry.All;
                for (int i = 0; i < allUnits.Count; i++)
                {
                    var u = allUnits[i];
                    if (u == null || !u.gameObject.activeInHierarchy)
                        continue;
                    if (u.GetComponent<ResourceNode>() != null)
                    {
                        skippedDecorative++;
                        continue;
                    }
                    var agent = u.GetComponentInParent<NavMeshAgent>();
                    if (agent == null)
                    {
                        skippedDecorative++;
                        continue;
                    }
                    TryAddEntry(u.transform, unitIconColor);
                }
            }

            if (showBuildingIcons)
            {
                var buildings = _cachedBuildings;
                for (int i = 0; i < buildings.Length; i++)
                {
                    var b = buildings[i];
                    if (b == null || !b.gameObject.activeInHierarchy)
                        continue;
                    if (b.buildingSO == null)
                    {
                        skippedDecorative++;
                        continue;
                    }
                    if (b.perSegmentHealth)
                    {
                        skippedDecorative++;
                        continue;
                    }
                    TryAddEntry(b.transform, buildingIconColor);
                }
            }

            if (showSpecialResourceIcons)
            {
                var nodes = _cachedResourceNodes;
                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    if (node == null || !node.gameObject.activeInHierarchy || node.IsDepleted)
                        continue;
                    switch (node.kind)
                    {
                        case ResourceKind.Wood:
                            skippedDecorative++;
                            continue;
                        case ResourceKind.Stone:
                            if (node.amount < minimapResourceMinStone)
                            {
                                skippedDecorative++;
                                continue;
                            }
                            break;
                        case ResourceKind.Food:
                            if (node.amount < minimapResourceMinFood)
                            {
                                skippedDecorative++;
                                continue;
                            }
                            break;
                        case ResourceKind.Gold:
                            break;
                    }
                    TryAddEntry(node.transform, specialResourceIconColor);
                }
            }

            if (showImportantMarkerIcons)
            {
                var markers = _cachedMarkers;
                for (int i = 0; i < markers.Length; i++)
                {
                    if (markers[i] == null || !markers[i].gameObject.activeInHierarchy)
                        continue;
                    TryAddEntry(markers[i].transform, importantMarkerIconColor);
                }
            }

            _iconWantScratch.Clear();
            var wantIds = _iconWantScratch;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Target != null)
                    wantIds.Add(entries[i].Target.GetInstanceID());
            }

            _iconReleaseScratch.Clear();
            var toRelease = _iconReleaseScratch;
            foreach (var kv in _minimapTargetIdToPoolIndex)
            {
                if (!wantIds.Contains(kv.Key))
                    toRelease.Add(kv.Key);
            }
            for (int i = 0; i < toRelease.Count; i++)
            {
                int id = toRelease[i];
                if (!_minimapTargetIdToPoolIndex.TryGetValue(id, out int poolIdx)) continue;
                _minimapTargetIdToPoolIndex.Remove(id);
                if (poolIdx >= 0 && poolIdx < _minimapIconPool.Count)
                {
                    var img = _minimapIconPool[poolIdx];
                    if (img != null) img.gameObject.SetActive(false);
                }
                _minimapFreeIconIndices.Push(poolIdx);
            }

            int activeShown = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                Transform target = entries[i].Target;
                Color col = entries[i].Color;
                if (target == null) continue;
                int id = target.GetInstanceID();

                if (!_minimapTargetIdToPoolIndex.TryGetValue(id, out int idx))
                {
                    if (_minimapFreeIconIndices.Count > 0)
                        idx = _minimapFreeIconIndices.Pop();
                    else
                    {
                        CreateMinimapPooledIcon();
                        idx = _minimapIconPool.Count - 1;
                        iconsCreatedThisPass++;
                    }
                    _minimapTargetIdToPoolIndex[id] = idx;
                }

                var image = _minimapIconPool[idx];
                if (image == null) continue;

                Vector3 vp = _minimapCamera.WorldToViewportPoint(target.position);
                if (vp.z < 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f)
                {
                    image.gameObject.SetActive(false);
                    continue;
                }

                image.gameObject.SetActive(true);
                image.color = col;
                var rt = image.rectTransform;
                rt.anchorMin = new Vector2(vp.x, vp.y);
                rt.anchorMax = new Vector2(vp.x, vp.y);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(iconSizePx, iconSizePx);
                activeShown++;
            }

            if (minimapIconDebugLog)
            {
                Debug.Log($"[MinimapIcons] active={activeShown} pooled={_minimapIconPool.Count} created={iconsCreatedThisPass} skippedDecorative={skippedDecorative} duplicatesPrevented={duplicatesPrevented}");
            }
        }

        void CreateMinimapPooledIcon()
        {
            var go = new GameObject("MinimapIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_iconOverlay, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(iconSizePx, iconSizePx);

            var img = go.GetComponent<Image>();
            img.color = unitIconColor;
            img.raycastTarget = false;
            img.sprite = null;
            img.type = Image.Type.Simple;

            _minimapIconPool.Add(img);
            go.SetActive(false);
        }

        void UpdateMinimapPings()
        {
            if (!enablePings || _iconOverlay == null || !_iconOverlay.gameObject.activeSelf || _minimapCamera == null) return;

            for (int i = _pings.Count - 1; i >= 0; i--)
            {
                if (_pings[i].endTime <= Time.time)
                    _pings.RemoveAt(i);
            }

            while (_pingImages.Count < _pings.Count)
            {
                var go = new GameObject("Ping", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(_iconOverlay, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(pingSizePx, pingSizePx);
                var img = go.GetComponent<Image>();
                img.color = pingColor;
                img.raycastTarget = false;
                img.sprite = null;
                _pingImages.Add(img);
            }

            for (int i = 0; i < _pings.Count; i++)
            {
                Vector3 vp = _minimapCamera.WorldToViewportPoint(_pings[i].worldPos);
                var img = _pingImages[i];
                img.gameObject.SetActive(vp.z >= 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f);
                if (img.gameObject.activeSelf)
                {
                    img.rectTransform.anchorMin = new Vector2(vp.x, vp.y);
                    img.rectTransform.anchorMax = new Vector2(vp.x, vp.y);
                    img.rectTransform.anchoredPosition = Vector2.zero;
                }
            }
            for (int i = _pings.Count; i < _pingImages.Count; i++)
                _pingImages[i].gameObject.SetActive(false);
        }

        void SetupMinimapForActiveScene()
        {
            var viewport = FindMinimapViewport();
            if (viewport == null)
            {
                if (!_warnedMissingViewport)
                {
                    _warnedMissingViewport = true;
                    Debug.LogWarning("RuntimeMinimapBootstrap: no se encontro MinimapViewport.");
                }
                return;
            }

            _warnedMissingViewport = false;
            EnsureCamera();
            ApplyMatchMinimapSettings();
            ConfigureCameraFromWorldBounds();
            _mainCamera = ResolveMainCamera();

            var raw = GetOrCreateRawTarget(viewport);
            if (raw == null)
            {
                Debug.LogWarning("RuntimeMinimapBootstrap: no se pudo crear objetivo RawImage para el minimapa.");
                return;
            }

            raw.raycastTarget = true;  // necesario para que EventSystem bloquee el drag de selección RTS
            raw.color = Color.white;
            _minimapRawRect = raw.rectTransform;
            _minimapRawImage = raw;

            CacheMinimapLayout(viewport.transform);
            ApplyMinimapSize();

            Canvas.ForceUpdateCanvases();
            if (_minimapContainerLayout != null)
            {
                var containerRt = _minimapContainerLayout.transform as RectTransform;
                if (containerRt != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(containerRt);
            }
            if (_minimapRawRect != null)
            {
                var vpRt = viewport.transform as RectTransform;
                if (vpRt != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(vpRt);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_minimapRawRect);
            }
            SyncRenderTextureToMinimapRect(raw);
            EnsureViewRectOverlay(raw.transform);
            EnsureMinimapIconOverlay(raw.transform);
            UpdateCameraViewRect();
            TryMinimapInitialRender();
            LogMinimapDiagnostics();
        }

        void ApplyMatchMinimapSettings()
        {
            if (MatchRuntimeState.Current == null)
                return;

            MatchConfig.MinimapSettings cfg = MatchRuntimeState.Current.minimap;
            autoFitWorld = cfg.autoFitWorld;
            useFixedOrthographicSize = cfg.useFixedOrthographicSize;
            fixedOrthographicSize = cfg.fixedOrthographicSize;
            mapPaddingFactor = cfg.mapPaddingFactor;
            boundsRefreshEvery = cfg.boundsRefreshEvery;
            cropUvHorizontal = cfg.cropUvHorizontal;
            uvHorizontalInset = cfg.uvHorizontalInset;
        }

        void CacheMinimapLayout(Transform viewportTransform)
        {
            if (viewportTransform == null) return;
            var container = viewportTransform.parent;
            _minimapContainerLayout = container != null
                ? container.GetComponent<LayoutElement>()
                : null;
        }

        void ApplyMinimapSize()
        {
            if (_minimapContainerLayout == null) return;
            float size = Mathf.Clamp(uiScaleMultiplier, 100f, uiMaxSize);
            _minimapContainerLayout.minHeight = size;
            _minimapContainerLayout.preferredWidth = -1;
            _minimapContainerLayout.preferredHeight = -1;
            _minimapContainerLayout.flexibleHeight = 1f;
            _minimapContainerLayout.flexibleWidth = 1f;
        }

        static RawImage GetOrCreateRawTarget(GameObject viewport)
        {
            if (viewport == null) return null;

            // Caso ideal: el viewport ya tiene RawImage.
            var raw = viewport.GetComponent<RawImage>();
            if (raw != null) return raw;

            // Si ya hay Image (placeholder/frame), no intentamos agregar RawImage al mismo GO.
            // En su lugar creamos un hijo que sí renderiza la RT.
            var existingImage = viewport.GetComponent<Image>();
            if (existingImage != null)
            {
                Transform child = viewport.transform.Find("MinimapRaw");
                GameObject rawGo;

                if (child == null)
                {
                    rawGo = new GameObject("MinimapRaw", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                    rawGo.transform.SetParent(viewport.transform, false);

                    var rt = rawGo.GetComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    rt.pivot = new Vector2(0.5f, 0.5f);
                }
                else
                {
                    rawGo = child.gameObject;
                    raw = rawGo.GetComponent<RawImage>();
                    if (raw == null) raw = rawGo.AddComponent<RawImage>();
                    return raw;
                }

                return rawGo.GetComponent<RawImage>();
            }

            // Si no hay ningún Graphic, sí podemos agregar RawImage directamente.
            return viewport.AddComponent<RawImage>();
        }

        GameObject FindMinimapViewport()
        {
            var hudRoot = GameObject.Find("HUD_Main") ?? GameObject.Find("HUD_Main_V2");
            if (hudRoot != null)
            {
                var path = hudRoot.transform.Find("HUD_Bottom/RightPanel/MinimapContainer/MinimapViewport");
                if (path != null) return path.gameObject;
            }

            return GameObject.Find("MinimapViewport");
        }

        /// <summary>
        /// RT fija barata (minimapTextureSize²) salvo modo estático con textura asignada.
        /// </summary>
        void SyncRenderTextureToMinimapRect(RawImage raw)
        {
            if (raw == null) return;

            if (useStaticMinimapBackground && staticMinimapBackground != null)
            {
                raw.texture = staticMinimapBackground;
                if (_minimapCamera != null)
                    _minimapCamera.targetTexture = null;
                ApplyUvCrop(raw);
                return;
            }

            int side = Mathf.Clamp(minimapTextureSize, 64, 512);
            side = Mathf.Max(64, (side / 2) * 2);
            int w = side;
            int h = side;

            if (_renderTexture != null && _renderTextureAllocW == w && _renderTextureAllocH == h)
            {
                if (raw.texture != _renderTexture) raw.texture = _renderTexture;
                if (_minimapCamera != null && _minimapCamera.targetTexture != _renderTexture)
                    _minimapCamera.targetTexture = _renderTexture;
                ApplyUvCrop(raw);
                return;
            }

            EnsureRenderTexture(w, h);
            _renderTextureAllocW = w;
            _renderTextureAllocH = h;

            raw.texture = _renderTexture;
            ApplyUvCrop(raw);
            if (_minimapCamera != null)
            {
                _minimapCamera.targetTexture = _renderTexture;
                _minimapCamera.aspect = w > 0 && h > 0 ? (float)w / h : 1f;
            }
        }

        void ApplyUvCrop(RawImage raw)
        {
            if (raw == null) return;
            if (!cropUvHorizontal)
            {
                raw.uvRect = new Rect(0f, 0f, 1f, 1f);
                return;
            }
            float i = Mathf.Clamp(uvHorizontalInset, 0f, 0.45f);
            raw.uvRect = new Rect(i, 0f, Mathf.Max(0.1f, 1f - 2f * i), 1f);
        }

        void EnsureRenderTexture(int width, int height)
        {
            bool needsCreate = _renderTexture == null
                || _renderTexture.width != width
                || _renderTexture.height != height;
            if (!needsCreate)
                return;

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }

            _renderTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
            {
                name = "Minimap_RT",
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear
            };
            _renderTexture.Create();
        }

        static int ComputeMinimapCullingMaskExcludingUiAndGridVisual()
        {
            int mask = ~0;
            mask &= ~(1 << UILayer);
            int gridVis = LayerMask.NameToLayer("GridVisual");
            if (gridVis >= 0)
                mask &= ~(1 << gridVis);
            // Siluetas de selección/hover (SelectableOutline / FadeOutline) se colocan en Ignore Raycast para no pintar el RT del minimapa.
            int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycast >= 0)
                mask &= ~(1 << ignoreRaycast);
            // Agua (AQUAS): si el minimapa renderiza water, OnWillRenderObject llama Camera.Render()
            // de reflejo dentro del mismo frame URP → "UniversalCameraData has already been created".
            int water = LayerMask.NameToLayer("Water");
            if (water >= 0)
                mask &= ~(1 << water);
            return mask;
        }

        void EnsureCamera()
        {
            var existing = GameObject.Find(MinimapCameraName);
            if (existing != null)
                _minimapCamera = existing.GetComponent<Camera>();

            if (_minimapCamera == null)
            {
                var go = new GameObject(MinimapCameraName);
                _minimapCamera = go.AddComponent<Camera>();

                var listener = go.GetComponent<AudioListener>();
                if (listener != null)
                    listener.enabled = false;
            }

            _minimapCamera.enabled = true;
            _minimapCamera.orthographic = true;
            _minimapCamera.clearFlags = CameraClearFlags.SolidColor;
            _minimapCamera.backgroundColor = new Color(0.05f, 0.07f, 0.10f, 1f);
            _minimapCamera.cullingMask = ComputeMinimapCullingMaskExcludingUiAndGridVisual();
            ApplyMinimapCameraQualityFlags();
            _minimapCamera.targetTexture = _renderTexture;
            if (_renderTexture != null)
                _minimapCamera.aspect = (float)_renderTexture.width / Mathf.Max(1, _renderTexture.height);
            _minimapCamera.depth = -100f;
        }

        void ApplyMinimapCameraQualityFlags()
        {
            if (_minimapCamera == null) return;
            if (minimapDisableHdr) _minimapCamera.allowHDR = false;
            if (minimapDisableMsaa) _minimapCamera.allowMSAA = false;
            if (!minimapDisableShadows) return;
            var urp = _minimapCamera.GetUniversalAdditionalCameraData();
            if (urp != null)
                urp.renderShadows = false;
        }

        bool MainCameraRelevantChange()
        {
            if (_mainCamera == null) return true;
            if (float.IsNaN(_lastMainCamSamplePos.x)) return true;
            const float posEps = 0.15f;
            if (Vector3.SqrMagnitude(_mainCamera.transform.position - _lastMainCamSamplePos) > posEps * posEps)
                return true;
            if (Quaternion.Angle(_mainCamera.transform.rotation, _lastMainCamSampleRot) > 0.35f)
                return true;
            float cur = _mainCamera.orthographic ? _mainCamera.orthographicSize : _mainCamera.fieldOfView;
            if (float.IsNaN(_lastMainCamOrthoOrFov) || Mathf.Abs(cur - _lastMainCamOrthoOrFov) > 0.05f)
                return true;
            return false;
        }

        void SnapshotMainCameraForMinimapThrottle()
        {
            if (_mainCamera == null) return;
            _lastMainCamSamplePos = _mainCamera.transform.position;
            _lastMainCamSampleRot = _mainCamera.transform.rotation;
            _lastMainCamOrthoOrFov = _mainCamera.orthographic ? _mainCamera.orthographicSize : _mainCamera.fieldOfView;
        }

        void TryMinimapInitialRender()
        {
            if (!Application.isPlaying) return;
            if (_minimapCamera == null) return;
            if (useStaticMinimapBackground && staticMinimapBackground != null) return;
            SnapshotMainCameraForMinimapThrottle();
            if (throttledMinimapUpdate)
            {
                _minimapCamera.enabled = false;
                _minimapCamera.Render();
                _nextMinimapRenderTime = Time.unscaledTime + Mathf.Max(0.05f, minimapUpdateInterval);
            }
            else
            {
                _minimapCamera.enabled = true;
            }
        }

        void LogMinimapDiagnostics()
        {
            if (_minimapDiagLogged) return;
            _minimapDiagLogged = true;
            int gridL = LayerMask.NameToLayer("GridVisual");
            int mask = ComputeMinimapCullingMaskExcludingUiAndGridVisual();
            bool gridEx = gridL >= 0 && (mask & (1 << gridL)) == 0;
            bool uiEx = (mask & (1 << UILayer)) == 0;
            string mode = useStaticMinimapBackground && staticMinimapBackground != null
                ? "staticBackground"
                : (throttledMinimapUpdate ? "throttled" : "continuous");
            string rtInfo = useStaticMinimapBackground && staticMinimapBackground != null
                ? $"{staticMinimapBackground.width}x{staticMinimapBackground.height}"
                : $"{Mathf.Clamp(minimapTextureSize, 64, 512)}x{Mathf.Clamp(minimapTextureSize, 64, 512)}";
            float ortho = _minimapCamera != null ? _minimapCamera.orthographicSize : 0f;
            Debug.Log($"[Minimap] mode={mode} | rt={rtInfo} | interval={minimapUpdateInterval:F2} | gridExcluded={(gridEx ? "yes" : "no")} | uiExcluded={(uiEx ? "yes" : "no")} | orthoSize={ortho:F1} | staticBackground={useStaticMinimapBackground}");
        }

        void ConfigureCameraFromWorldBounds()
        {
            if (_minimapCamera == null)
                return;

            bool fromPlane = TryGetMapPlaneExtents(out Vector3 planeOrigin, out float worldW, out float worldH);
            if (fromPlane)
            {
                _worldBounds = BuildBoundsFromMapPlane(planeOrigin, worldW, worldH);
                _mapPlaneOrigin = planeOrigin;
                _mapPlaneWidth = Mathf.Max(0.001f, worldW);
                _mapPlaneHeight = Mathf.Max(0.001f, worldH);
            }
            else
            {
                _worldBounds = CalculateWorldBounds();
                Bounds b = _worldBounds;
                _mapPlaneOrigin = new Vector3(b.center.x - b.extents.x, 0f, b.center.z - b.extents.z);
                _mapPlaneWidth = Mathf.Max(0.001f, b.size.x);
                _mapPlaneHeight = Mathf.Max(0.001f, b.size.z);
            }

            _hasWorldBounds = true;

            Vector3 centerXZ = _mapPlaneOrigin + new Vector3(_mapPlaneWidth * 0.5f, 0f, _mapPlaneHeight * 0.5f);
            float minY = _mapPlaneOrigin.y;
            float maxY = _mapPlaneOrigin.y + 80f;
            var activeTerrain = Terrain.activeTerrain;
            if (activeTerrain != null && activeTerrain.terrainData != null)
            {
                minY = activeTerrain.transform.position.y;
                maxY = activeTerrain.transform.position.y + activeTerrain.terrainData.size.y;
            }

            float camHeight = Mathf.Max(maxY + 150f, minY + 250f);
            _minimapCamera.transform.position = new Vector3(centerXZ.x, camHeight, centerXZ.z);
            _minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            float aspect = Mathf.Max(GetTargetCameraAspect(), 0.01f);
            // Encuadra todo el plano mapa W×H: con RT cuadrada (aspect≈1) equivale a ortho ≈ Max(W,H)*0.5 * padding.
            float orthoFromMap = Mathf.Max(_mapPlaneHeight * 0.5f, _mapPlaneWidth / (2f * aspect)) * mapPaddingFactor;

            _minimapCamera.orthographic = true;
            _minimapCamera.orthographicSize = useFixedOrthographicSize && !autoFitWorld
                ? fixedOrthographicSize
                : orthoFromMap;
            _minimapCamera.nearClipPlane = 0.3f;
            float mapDiag = Mathf.Sqrt(_mapPlaneWidth * _mapPlaneWidth + _mapPlaneHeight * _mapPlaneHeight);
            float verticalSpan = Mathf.Max(1f, camHeight - minY + (maxY - minY) + 200f);
            _minimapCamera.farClipPlane = Mathf.Max(verticalSpan, mapDiag * 2f, 800f);

            _minimapCamera.cullingMask = ComputeMinimapCullingMaskExcludingUiAndGridVisual();
            ApplyMinimapCameraQualityFlags();
        }

        static bool TryGetMapPlaneExtents(out Vector3 origin, out float worldW, out float worldH)
        {
            var grids = FindObjectsByType<MapGrid>(FindObjectsSortMode.None);
            for (int i = 0; i < grids.Length; i++)
            {
                MapGrid g = grids[i];
                if (g != null && g.IsReady)
                {
                    origin = g.origin;
                    worldW = g.width * g.cellSize;
                    worldH = g.height * g.cellSize;
                    return true;
                }
            }

            var gen = FindFirstObjectByType<RTSMapGenerator>();
            if (gen != null)
            {
                RTSMapGenerator.GetAuthoritativeGridLayout(gen, out float cs, out origin, out int gw, out int gh);
                worldW = gw * cs;
                worldH = gh * cs;
                if (worldW > 0.001f && worldH > 0.001f)
                    return true;
            }

            var terrain = Terrain.activeTerrain;
            if (terrain != null && terrain.terrainData != null)
            {
                Vector3 sz = terrain.terrainData.size;
                origin = terrain.transform.position;
                worldW = sz.x;
                worldH = sz.z;
                return true;
            }

            origin = Vector3.zero;
            worldW = 0f;
            worldH = 0f;
            return false;
        }

        static Bounds BuildBoundsFromMapPlane(Vector3 origin, float worldW, float worldH)
        {
            float minY = origin.y;
            float maxY = origin.y + 80f;
            var t = Terrain.activeTerrain;
            if (t != null && t.terrainData != null)
            {
                minY = t.transform.position.y;
                maxY = t.transform.position.y + t.terrainData.size.y;
            }

            Vector3 center = origin + new Vector3(worldW * 0.5f, (minY + maxY) * 0.5f, worldH * 0.5f);
            Vector3 size = new Vector3(worldW, Mathf.Max(40f, maxY - minY), worldH);
            return new Bounds(center, size);
        }

        float GetTargetCameraAspect()
        {
            if (_renderTexture != null && _renderTexture.height > 0)
                return (float)_renderTexture.width / _renderTexture.height;

            if (_minimapRawRect != null)
            {
                Rect rect = _minimapRawRect.rect;
                if (rect.height > 0.01f)
                    return Mathf.Abs(rect.width) / Mathf.Abs(rect.height);
            }

            return 1f;
        }

        Camera ResolveMainCamera()
        {
            var rtsCam = FindFirstObjectByType<RTSCameraController>();
            if (rtsCam != null && rtsCam.cam != null)
                return rtsCam.cam;

            if (Camera.main != null)
                return Camera.main;

            return FindFirstObjectByType<Camera>();
        }

        void EnsureViewRectOverlay(Transform rawParent)
        {
            if (rawParent == null) return;

            var root = rawParent.Find("CameraViewRect");
            if (root == null)
            {
                var go = new GameObject("CameraViewRect", typeof(RectTransform));
                go.transform.SetParent(rawParent, false);
                root = go.transform;

                var r = go.GetComponent<RectTransform>();
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = Vector2.zero;
                r.offsetMax = Vector2.zero;
                r.pivot = new Vector2(0.5f, 0.5f);
            }

            _viewRectTop = EnsureLine(root, "Top");
            _viewRectBottom = EnsureLine(root, "Bottom");
            _viewRectLeft = EnsureLine(root, "Left");
            _viewRectRight = EnsureLine(root, "Right");
        }

        static RectTransform EnsureLine(Transform parent, string name)
        {
            var t = parent.Find(name);
            GameObject go;
            if (t == null)
            {
                go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(parent, false);
            }
            else
            {
                go = t.gameObject;
            }

            var img = go.GetComponent<Image>();
            if (img == null) img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(0.3f, 0.9f, 1f, 0.95f);

            return go.GetComponent<RectTransform>();
        }

        void UpdateCameraViewRect()
        {
            if (!_hasWorldBounds) return;
            _mainCamera = ResolveMainCamera();
            if (_mainCamera == null) return;
            if (_mapPlaneWidth < 0.001f || _mapPlaneHeight < 0.001f) return;
            if (_viewRectTop == null || _viewRectBottom == null || _viewRectLeft == null || _viewRectRight == null) return;

            float groundY = GetGroundPlaneY();
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
            // 4 esquinas del viewport = intersección del frustum visible con el plano del suelo (mapa completo).
            float maxRay = Mathf.Max(_mapPlaneWidth, _mapPlaneHeight) * 8f + 4000f;

            if (!TryViewportToGround(_mainCamera, new Vector3(0f, 0f, 0f), groundPlane, maxRay, out var p0)) return;
            if (!TryViewportToGround(_mainCamera, new Vector3(0f, 1f, 0f), groundPlane, maxRay, out var p1)) return;
            if (!TryViewportToGround(_mainCamera, new Vector3(1f, 1f, 0f), groundPlane, maxRay, out var p2)) return;
            if (!TryViewportToGround(_mainCamera, new Vector3(1f, 0f, 0f), groundPlane, maxRay, out var p3)) return;

            Vector2 a = WorldToMapNormalizedUv(p0);
            Vector2 b = WorldToMapNormalizedUv(p1);
            Vector2 c = WorldToMapNormalizedUv(p2);
            Vector2 d = WorldToMapNormalizedUv(p3);

            float uMin = Mathf.Clamp01(Mathf.Min(a.x, b.x, c.x, d.x));
            float uMax = Mathf.Clamp01(Mathf.Max(a.x, b.x, c.x, d.x));
            float vMin = Mathf.Clamp01(Mathf.Min(a.y, b.y, c.y, d.y));
            float vMax = Mathf.Clamp01(Mathf.Max(a.y, b.y, c.y, d.y));

            if (uMax - uMin < 0.002f || vMax - vMin < 0.002f)
                return;

            if (Mathf.Abs(viewRectScale - 1f) > 0.02f)
            {
                float cx = (uMin + uMax) * 0.5f;
                float cy = (vMin + vMax) * 0.5f;
                float halfW = (uMax - uMin) * 0.5f * viewRectScale;
                float halfH = (vMax - vMin) * 0.5f * viewRectScale;
                uMin = Mathf.Clamp01(cx - halfW);
                uMax = Mathf.Clamp01(cx + halfW);
                vMin = Mathf.Clamp01(cy - halfH);
                vMax = Mathf.Clamp01(cy + halfH);
            }

            ApplyLineStyle(_viewRectTop);
            ApplyLineStyle(_viewRectBottom);
            ApplyLineStyle(_viewRectLeft);
            ApplyLineStyle(_viewRectRight);

            PlaceHorizontalLine(_viewRectTop, uMin, uMax, vMax, viewRectThickness, true);
            PlaceHorizontalLine(_viewRectBottom, uMin, uMax, vMin, viewRectThickness, false);
            PlaceVerticalLine(_viewRectLeft, uMin, vMin, vMax, viewRectThickness, false);
            PlaceVerticalLine(_viewRectRight, uMax, vMin, vMax, viewRectThickness, true);
        }

        void ApplyLineStyle(RectTransform rt)
        {
            if (rt == null) return;
            var img = rt.GetComponent<Image>();
            if (img == null) return;
            img.color = viewRectColor;
        }

        float GetGroundPlaneY()
        {
            var terrain = Terrain.activeTerrain;
            if (terrain != null)
                return terrain.transform.position.y + 0.05f;

            return _mapPlaneOrigin.y + 0.05f;
        }

        static bool TryViewportToGround(Camera cam, Vector3 viewportPoint, Plane plane, float maxRayDistance, out Vector3 hitPoint)
        {
            Ray ray = cam.ViewportPointToRay(viewportPoint);

            if (plane.Raycast(ray, out float enter) && enter > 0.001f)
            {
                hitPoint = ray.GetPoint(enter);
                return true;
            }

            if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                hitPoint = hit.point;
                return true;
            }

            hitPoint = default;
            return false;
        }

        Vector3 WorldPositionFromMinimapUv(float u, float v)
        {
            float x = _mapPlaneOrigin.x + Mathf.Clamp01(u) * _mapPlaneWidth;
            float z = _mapPlaneOrigin.z + Mathf.Clamp01(v) * _mapPlaneHeight;
            return new Vector3(x, GetGroundPlaneY(), z);
        }

        Vector2 WorldToMapNormalizedUv(Vector3 world)
        {
            float u = (world.x - _mapPlaneOrigin.x) / _mapPlaneWidth;
            float v = (world.z - _mapPlaneOrigin.z) / _mapPlaneHeight;
            return new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
        }

        static void PlaceHorizontalLine(RectTransform rt, float uMin, float uMax, float v, float thickness, bool topPivot)
        {
            rt.anchorMin = new Vector2(uMin, v);
            rt.anchorMax = new Vector2(uMax, v);
            rt.pivot = new Vector2(0.5f, topPivot ? 1f : 0f);
            rt.sizeDelta = new Vector2(0f, thickness);
            rt.anchoredPosition = Vector2.zero;
        }

        static void PlaceVerticalLine(RectTransform rt, float u, float vMin, float vMax, float thickness, bool rightPivot)
        {
            rt.anchorMin = new Vector2(u, vMin);
            rt.anchorMax = new Vector2(u, vMax);
            rt.pivot = new Vector2(rightPivot ? 1f : 0f, 0.5f);
            rt.sizeDelta = new Vector2(thickness, 0f);
            rt.anchoredPosition = Vector2.zero;
        }

        static Bounds CalculateWorldBounds()
        {
            if (TryGetMapPlaneExtents(out Vector3 o, out float w, out float h))
                return BuildBoundsFromMapPlane(o, w, h);

            if (MatchRuntimeState.TryGetGeneratedWorldBounds(out Bounds generatedBounds))
                return generatedBounds;

            var terrain = Terrain.activeTerrain;
            if (terrain != null && terrain.terrainData != null)
            {
                Vector3 size = terrain.terrainData.size;
                Vector3 center = terrain.transform.position + new Vector3(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f);
                return new Bounds(center, size);
            }

            Renderer[] renderers;
#if UNITY_2022_2_OR_NEWER
            renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
#else
            renderers = FindObjectsOfType<Renderer>();
#endif
            bool hasBounds = false;
            Bounds bounds = default;

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                if (!r.enabled) continue;
                if (r.gameObject.layer == 5) continue; // UI

                if (!hasBounds)
                {
                    bounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            if (!hasBounds)
                bounds = new Bounds(Vector3.zero, new Vector3(200f, 50f, 200f));

            return bounds;
        }
    }

    /// <summary>
    /// Opcional: añadir a un objeto en escena para que <see cref="RuntimeMinimapBootstrap"/> muestre su icono (objetivos, POI).
    /// </summary>
    public sealed class MinimapWorldIconTarget : MonoBehaviour { }
}
