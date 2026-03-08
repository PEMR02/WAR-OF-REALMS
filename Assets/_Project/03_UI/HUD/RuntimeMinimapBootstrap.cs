using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Project.Gameplay;
using Project.Gameplay.Units;
using Project.Gameplay.Buildings;

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

        [Header("Minimap Fit")]
        [SerializeField] bool autoFitWorld = false;
        [SerializeField] bool useFixedOrthographicSize = true;
        [SerializeField, Min(10f)] float fixedOrthographicSize = 160f;
        [SerializeField, Min(1.0f)] float mapPaddingFactor = 1.2f;
        [SerializeField, Min(0.2f)] float boundsRefreshEvery = 1.0f;

        [Header("View Rect")]
        [SerializeField, Min(1f)] float viewRectThickness = 3f;
        [SerializeField] Color viewRectColor = new Color(0.3f, 0.9f, 1f, 0.95f);
        [SerializeField, Range(0.4f, 1.2f)] float viewRectScale = 1f;

        [Header("UI Size")]
        [Tooltip("Tamaño mínimo del minimapa en píxeles de referencia (1080p). Aumentar para agrandar.")]
        [SerializeField, Range(100f, 600f)] float uiScaleMultiplier = 260f;
        [Tooltip("Limitar el tamaño máximo del minimapa.")]
        [SerializeField, Min(100f)] float uiMaxSize = 600f;

        [Header("Iconos (unidades/edificios)")]
        [SerializeField] bool showUnitIcons = true;
        [SerializeField] bool showBuildingIcons = true;
        [SerializeField] Color unitIconColor = new Color(0.2f, 0.9f, 0.3f, 0.95f);
        [SerializeField] Color buildingIconColor = new Color(0.3f, 0.6f, 1f, 0.95f);
        [SerializeField, Range(4f, 16f)] float iconSizePx = 8f;
        [SerializeField, Min(0.05f)] float iconRefreshInterval = 0.1f;

        [Header("Pings")]
        [SerializeField] bool enablePings = true;
        [SerializeField] float pingDuration = 4f;
        [SerializeField] UnityEngine.InputSystem.Key pingModifierKey = UnityEngine.InputSystem.Key.LeftAlt;
        [SerializeField] Color pingColor = new Color(1f, 0.85f, 0.2f, 0.9f);
        [SerializeField, Range(4f, 20f)] float pingSizePx = 10f;

        Camera _minimapCamera;
        Camera _mainCamera;
        RenderTexture _renderTexture;
        Bounds _worldBounds;
        bool _hasWorldBounds;
        bool _warnedMissingViewport;
        float _nextBoundsRefreshTime;

        RectTransform _viewRectTop;
        RectTransform _viewRectBottom;
        RectTransform _viewRectLeft;
        RectTransform _viewRectRight;

        LayoutElement _minimapContainerLayout;
        RectTransform _minimapRawRect;
        RTSCameraController _rtsCameraController;
        bool _minimapDragging;

        RectTransform _iconOverlay;
        readonly List<Image> _iconPool = new List<Image>();
        readonly List<Transform> _iconTargets = new List<Transform>();
        readonly List<bool> _iconIsUnit = new List<bool>();
        float _iconRefreshTimer;

        struct MinimapPing { public Vector3 worldPos; public float endTime; }
        readonly List<MinimapPing> _pings = new List<MinimapPing>();
        readonly List<Image> _pingImages = new List<Image>();

        /// <summary>True cuando el cursor está sobre el área del minimapa. Consultado por RTSSelectionController.</summary>
        public static bool IsPointerOverMinimap { get; private set; }

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
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SetupMinimapForActiveScene();
        }

        void LateUpdate()
        {
            if (_minimapCamera == null)
                return;

            if (autoFitWorld && Time.unscaledTime >= _nextBoundsRefreshTime)
            {
                _nextBoundsRefreshTime = Time.unscaledTime + boundsRefreshEvery;
                ConfigureCameraFromWorldBounds();
            }

            if (_mainCamera == null)
                _mainCamera = ResolveMainCamera();

            UpdateCameraViewRect();

            if (Application.isPlaying)
            {
                HandleMinimapClick();
                UpdateMinimapIcons();
                UpdateMinimapPings();
            }
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

            // Convertir UV a posición mundo via rayo de la cámara del minimapa
            Ray ray = _minimapCamera.ViewportPointToRay(new Vector3(u, v, 0f));
            float groundY = GetGroundPlaneY();
            var plane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));

            if (!plane.Raycast(ray, out float dist)) return;
            Vector3 worldPos = ray.GetPoint(dist);

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

        void UpdateMinimapIcons()
        {
            if (!showUnitIcons && !showBuildingIcons) return;
            if (_iconOverlay == null || _minimapCamera == null) return;

            _iconRefreshTimer -= Time.unscaledDeltaTime;
            if (_iconRefreshTimer > 0f) return;
            _iconRefreshTimer = iconRefreshInterval;

            _iconTargets.Clear();
            _iconIsUnit.Clear();

            if (showUnitIcons)
            {
                var units = FindObjectsByType<UnitSelectable>(FindObjectsSortMode.None);
                for (int i = 0; i < units.Length; i++)
                {
                    if (units[i] != null && units[i].gameObject.activeInHierarchy)
                    {
                        _iconTargets.Add(units[i].transform);
                        _iconIsUnit.Add(true);
                    }
                }
            }

            if (showBuildingIcons)
            {
                var buildings = FindObjectsByType<BuildingInstance>(FindObjectsSortMode.None);
                for (int i = 0; i < buildings.Length; i++)
                {
                    if (buildings[i] != null && buildings[i].gameObject.activeInHierarchy)
                    {
                        _iconTargets.Add(buildings[i].transform);
                        _iconIsUnit.Add(false);
                    }
                }
            }

            int need = _iconTargets.Count;
            while (_iconPool.Count < need)
                AddIconToPool();
            while (_iconPool.Count > need)
                RemoveIconFromPool();

            for (int i = 0; i < need; i++)
            {
                var target = _iconTargets[i];
                bool isUnit = _iconIsUnit[i];
                var img = _iconPool[i];

                if (target == null) { img.gameObject.SetActive(false); continue; }

                Vector3 vp = _minimapCamera.WorldToViewportPoint(target.position);
                if (vp.z < 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f)
                {
                    img.gameObject.SetActive(false);
                    continue;
                }

                img.gameObject.SetActive(true);
                img.color = isUnit ? unitIconColor : buildingIconColor;

                var rt = img.rectTransform;
                rt.anchorMin = new Vector2(vp.x, vp.y);
                rt.anchorMax = new Vector2(vp.x, vp.y);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(iconSizePx, iconSizePx);
            }
        }

        void AddIconToPool()
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

            _iconPool.Add(img);
        }

        void UpdateMinimapPings()
        {
            if (!enablePings || _iconOverlay == null || _minimapCamera == null) return;

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

        void RemoveIconFromPool()
        {
            int last = _iconPool.Count - 1;
            if (last < 0) return;
            if (_iconPool[last].gameObject != null)
                Destroy(_iconPool[last].gameObject);
            _iconPool.RemoveAt(last);
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
            EnsureRenderTexture(512);
            EnsureCamera();
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
            raw.texture = _renderTexture;
            _minimapRawRect = raw.rectTransform;

            CacheMinimapLayout(viewport.transform);
            ApplyMinimapSize();
            EnsureViewRectOverlay(raw.transform);
            EnsureMinimapIconOverlay(raw.transform);
            UpdateCameraViewRect();
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
            var hudRoot = GameObject.Find("HUD_Main_V2");
            if (hudRoot != null)
            {
                var path = hudRoot.transform.Find("HUD_Bottom/RightPanel/MinimapContainer/MinimapViewport");
                if (path != null) return path.gameObject;
            }

            // Fallback por nombre por si cambia la jerarquía.
            return GameObject.Find("MinimapViewport");
        }

        void EnsureRenderTexture(int size)
        {
            bool needsCreate = _renderTexture == null || _renderTexture.width != size || _renderTexture.height != size;
            if (!needsCreate)
                return;

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }

            _renderTexture = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
            {
                name = "Minimap_RT",
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear
            };
            _renderTexture.Create();
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
            _minimapCamera.cullingMask = ~(1 << 5); // Excluye layer UI
            _minimapCamera.allowHDR = false;
            _minimapCamera.allowMSAA = false;
            _minimapCamera.targetTexture = _renderTexture;
            _minimapCamera.depth = -100f;
        }

        void ConfigureCameraFromWorldBounds()
        {
            if (_minimapCamera == null)
                return;

            _worldBounds = CalculateWorldBounds();
            _hasWorldBounds = true;
            Vector3 center = _worldBounds.center;

            float halfSpan = Mathf.Max(_worldBounds.extents.x, _worldBounds.extents.z);
            halfSpan = Mathf.Max(halfSpan, 40f);

            float camHeight = Mathf.Max(_worldBounds.max.y + 80f, 120f);
            _minimapCamera.transform.position = new Vector3(center.x, camHeight, center.z);
            _minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _minimapCamera.orthographicSize = useFixedOrthographicSize
                ? fixedOrthographicSize
                : (halfSpan * mapPaddingFactor);
            _minimapCamera.nearClipPlane = 0.3f;
            _minimapCamera.farClipPlane = Mathf.Max(800f, camHeight + _worldBounds.size.y + 200f);
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
            if (_mainCamera == null) return;
            if (_viewRectTop == null || _viewRectBottom == null || _viewRectLeft == null || _viewRectRight == null) return;

            float groundY = GetGroundPlaneY();
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));

            if (!TryViewportToGround(_mainCamera, new Vector3(0f, 0f, 0f), groundPlane, out var p0)) return;
            if (!TryViewportToGround(_mainCamera, new Vector3(0f, 1f, 0f), groundPlane, out var p1)) return;
            if (!TryViewportToGround(_mainCamera, new Vector3(1f, 1f, 0f), groundPlane, out var p2)) return;
            if (!TryViewportToGround(_mainCamera, new Vector3(1f, 0f, 0f), groundPlane, out var p3)) return;

            Vector2 a = WorldToMinimapUVFromCamera(p0);
            Vector2 b = WorldToMinimapUVFromCamera(p1);
            Vector2 c = WorldToMinimapUVFromCamera(p2);
            Vector2 d = WorldToMinimapUVFromCamera(p3);

            float uMin = Mathf.Clamp01(Mathf.Min(a.x, b.x, c.x, d.x));
            float uMax = Mathf.Clamp01(Mathf.Max(a.x, b.x, c.x, d.x));
            float vMin = Mathf.Clamp01(Mathf.Min(a.y, b.y, c.y, d.y));
            float vMax = Mathf.Clamp01(Mathf.Max(a.y, b.y, c.y, d.y));

            if (uMax - uMin < 0.002f || vMax - vMin < 0.002f)
                return;

            // Ajuste perceptual estilo AoE: la franja superior del frustum suele "sobreestimar" lo que el jugador interpreta como foco.
            float cx = (uMin + uMax) * 0.5f;
            float cy = (vMin + vMax) * 0.5f;
            float halfW = (uMax - uMin) * 0.5f * viewRectScale;
            float halfH = (vMax - vMin) * 0.5f * viewRectScale;
            uMin = Mathf.Clamp01(cx - halfW);
            uMax = Mathf.Clamp01(cx + halfW);
            vMin = Mathf.Clamp01(cy - halfH);
            vMax = Mathf.Clamp01(cy + halfH);

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

            // Fallback estable: centro de bounds, evita usar minY que exagera el rect.
            return _worldBounds.center.y;
        }

        static bool TryViewportToGround(Camera cam, Vector3 viewportPoint, Plane plane, out Vector3 hitPoint)
        {
            Ray ray = cam.ViewportPointToRay(viewportPoint);

            if (plane.Raycast(ray, out float enter))
            {
                hitPoint = ray.GetPoint(enter);
                return true;
            }

            // Fallback para casos raros de cámara/terreno.
            if (Physics.Raycast(ray, out RaycastHit hit, 5000f, ~0, QueryTriggerInteraction.Ignore))
            {
                hitPoint = hit.point;
                return true;
            }

            hitPoint = default;
            return false;
        }

        Vector2 WorldToMinimapUVFromCamera(Vector3 world)
        {
            if (_minimapCamera == null) return Vector2.zero;
            Vector3 vp = _minimapCamera.WorldToViewportPoint(world);
            return new Vector2(vp.x, vp.y);
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
}
