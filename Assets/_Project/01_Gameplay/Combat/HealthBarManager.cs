using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Combat
{
    /// <summary>
    /// Singleton persistente: un solo Canvas en Screen Space, barras instanciadas dinámicamente
    /// y posicionadas con WorldToScreenPoint. Solo una instancia activa; duplicados en otras escenas se destruyen.
    /// Ciclo de vida: Awake valida Canvas, establece Instance y DontDestroyOnLoad; OnDestroy limpia Instance y barras (no se crea Canvas en runtime).
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class HealthBarManager : MonoBehaviour
    {
        public static HealthBarManager Instance { get; private set; }

        [SerializeField] private Camera worldCamera;
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform canvasRect;
        [SerializeField] private HealthBarUI healthBarPrefab;
        [Tooltip("Barras más lejos que esta distancia (metros) no se actualizan (solo se ocultan). 0 = sin límite. Valores bajos (~120) ocultan barras con cámara RTS alejada.")]
        [SerializeField] private float maxBarDistance = 0f;
        [Tooltip("Margen extra en coordenadas de viewport (0–1) al decidir si la barra está en pantalla. La barra se dibuja por encima de la unidad; sin margen suele quedar fuera del rango Y y nunca verse.")]
        [SerializeField] private float viewportVisibilityMargin = 0.35f;
        [Tooltip("Si false, la barra se oculta cuando la vida esta al maximo.")]
        [SerializeField] private bool showWhenFull = false;
        [Tooltip("Logs [WorldHealthBar] al mostrar/ocultar barra moderna o desactivar legacy (sin spam; solo al cambiar visibilidad / una vez por Register).")]
        [SerializeField] private bool debugLogs;

        /// <summary>Una barra por entidad (GameObject), para que doble clic muestre barra en cada unidad aunque compartan Health (ej. mismo prefab con dos UnitSelectable).</summary>
        private readonly Dictionary<GameObject, (Health health, HealthBarUI bar)> _barsByEntity = new();
        private readonly Dictionary<GameObject, bool> _debugLastBarVisible = new();
        private readonly List<GameObject> _toRemove = new List<GameObject>(32);
        private bool _createdCanvasRuntime;
        private static bool _warnedMissingPrefab;
        private static bool _warnedMissingCamera;
        private static bool _warnedNoInstanceWhenShowingBar;
        private static bool _warnedCanvasDelayed;
        private static bool _warnedAutoBootstrapFailed;
        private bool _bootstrapFailed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    "[HealthBarManager] Ya existe una instancia activa. Destruyendo duplicado. Deja solo un HealthBarManager en el proyecto (ej. en escena bootstrap o HUD).",
                    this);
                Destroy(gameObject);
                return;
            }

            TryResolveCanvasAndRect();
            if (canvas != null)
                TryCommitSingleton();
            else if (!_warnedCanvasDelayed)
            {
                _warnedCanvasDelayed = true;
                Debug.LogWarning(
                    "[HealthBarManager] No se encontró Canvas (Screen Space) en Awake. Se reintentará en Start (p. ej. si el HUD se activa un frame después).",
                    this);
            }
        }

        private void Start()
        {
            if (Instance != null && Instance != this)
                return;

            TryResolveCanvasAndRect();
            if (canvas == null)
            {
                if (!_bootstrapFailed)
                {
                    _bootstrapFailed = true;
                    Debug.LogError(
                        "[HealthBarManager] Sin Canvas Screen Space / Camera tras Awake+Start. Asigna Canvas y RectTransform en el Inspector o asegura un Canvas HUD activo en la escena. Este objeto se destruye.",
                        this);
                    enabled = false;
                    Destroy(gameObject);
                }
                return;
            }

            TryCommitSingleton();
        }

        void TryResolveCanvasAndRect()
        {
            if (canvas == null)
            {
                var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                Canvas pick = null;
                for (int i = 0; i < canvases.Length; i++)
                {
                    var c = canvases[i];
                    if (c == null || c.renderMode == RenderMode.WorldSpace) continue;
                    if (pick == null) pick = c;
                    string n = c.gameObject.name;
                    if (n.IndexOf("HUD", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("UI", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        pick = c;
                        break;
                    }
                }
                canvas = pick;
            }

            if (canvas != null && canvasRect == null)
                canvasRect = canvas.GetComponent<RectTransform>();

            if (worldCamera == null)
                worldCamera = Camera.main;
            if (worldCamera == null && !_warnedMissingCamera)
            {
                _warnedMissingCamera = true;
                Debug.LogWarning("[HealthBarManager] Cámara no asignada y Camera.main es null. Asigna World Camera en el Inspector o asegura una cámara con tag MainCamera.", this);
            }

            if (healthBarPrefab == null && !_warnedMissingPrefab)
            {
                _warnedMissingPrefab = true;
                Debug.LogWarning("[HealthBarManager] Health Bar Prefab no asignado. Asigna PF_HealthBarUI en el Inspector para que se muestren las barras.", this);
            }
        }

        void TryCommitSingleton()
        {
            if (canvas == null || canvasRect == null)
                return;
            if (Instance != null && Instance != this)
                return;

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>Si Instance quedó null (orden de escena), fuerza bootstrap desde cualquier HealthBarManager en memoria.</summary>
        static void TryWakeAnyInstance()
        {
            if (Instance != null)
                return;
            var all = FindObjectsByType<HealthBarManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                all[i].TryResolveCanvasAndRect();
                all[i].TryCommitSingleton();
                if (Instance != null)
                    return;
            }
            TryAutoBootstrapInstance();
        }

        static void TryAutoBootstrapInstance()
        {
            if (Instance != null)
                return;

            var go = new GameObject("HealthBarManager_Auto");
            var mgr = go.AddComponent<HealthBarManager>();

            // Resolver Canvas HUD (screen-space) para ubicar barras en pantalla.
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                var c = canvases[i];
                if (c == null || c.renderMode == RenderMode.WorldSpace) continue;
                mgr.canvas = c;
                mgr.canvasRect = c.GetComponent<RectTransform>();
                break;
            }

            // Resolver prefab de barra (si no existe en escena, buscar asset cargado en memoria).
            if (mgr.healthBarPrefab == null)
            {
                var sceneBars = FindObjectsByType<HealthBarUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < sceneBars.Length; i++)
                {
                    if (sceneBars[i] == null) continue;
                    mgr.healthBarPrefab = sceneBars[i];
                    break;
                }
            }
            if (mgr.healthBarPrefab == null)
            {
                // Fallback compatible entre versiones: buscar en objetos cargados en memoria.
                var allBars = FindObjectsByType<HealthBarUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < allBars.Length; i++)
                {
                    var b = allBars[i];
                    if (b == null) continue;
                    mgr.healthBarPrefab = b;
                    break;
                }
            }

            mgr.TryResolveCanvasAndRect();
            mgr.TryCommitSingleton();

            if (Instance == null && !_warnedAutoBootstrapFailed)
            {
                _warnedAutoBootstrapFailed = true;
                Debug.LogWarning("[HealthBarManager] No se pudo auto-bootstrappear instancia/prefab para barras de mundo.");
            }
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;

            foreach (var kv in _barsByEntity)
            {
                if (kv.Value.bar != null && kv.Value.bar.gameObject != null)
                    Destroy(kv.Value.bar.gameObject);
            }
            _barsByEntity.Clear();

            if (_createdCanvasRuntime && canvas != null && canvas.gameObject != null)
                Destroy(canvas.gameObject);

            Instance = null;
        }

        /// <summary>Registra una barra para esta entidad. Una barra por GameObject; si dos entidades comparten Health, cada una tiene su barra.</summary>
        /// <remarks>
        /// Una vez registrada, <see cref="HealthBarUI"/> está suscrito a <see cref="Health.OnHealthChanged"/>:
        /// daño o curación actualiza el fill y la visibilidad vía <see cref="NotifyBarVisibilityRefresh"/> sin alterar la política global <c>showWhenFull</c>.
        /// </remarks>
        public void Register(GameObject entity)
        {
            if (Instance != this || entity == null)
                return;
            if (_barsByEntity.ContainsKey(entity))
                return;
            Health health = ResolveHealth(entity);
            if (health == null)
                return;
            if (healthBarPrefab == null || canvas == null || canvasRect == null)
                return;

            DisableLegacyWorldBars(entity, health);

            HealthBarUI bar = Instantiate(healthBarPrefab, canvas.transform);
            bar.Bind(health);
            bar.gameObject.SetActive(true);
            bar.transform.SetAsLastSibling();
            bar.Refresh();
            _barsByEntity[entity] = (health, bar);
            UpdateSingleBar(entity, health, bar);
        }

        /// <summary>Apaga barras world-space legacy bajo la entidad, el Health y sus raíces (evita duplicado visual con PF_HealthBarUI).</summary>
        void DisableLegacyWorldBars(GameObject entity, Health health)
        {
            var seen = new HashSet<int>();
            var roots = new List<Transform>(6);
            if (entity != null)
            {
                roots.Add(entity.transform);
                roots.Add(entity.transform.root);
            }
            if (health != null)
            {
                roots.Add(health.transform);
                roots.Add(health.transform.root);
            }

            bool anyDisabled = false;
            for (int r = 0; r < roots.Count; r++)
            {
                Transform rt = roots[r];
                if (rt == null) continue;
                var legacyBars = rt.GetComponentsInChildren<HealthBarWorld>(true);
                for (int i = 0; i < legacyBars.Length; i++)
                {
                    var lb = legacyBars[i];
                    if (lb == null) continue;
                    int id = lb.GetInstanceID();
                    if (seen.Contains(id)) continue;
                    seen.Add(id);

                    lb.enabled = false;
                    Transform tr = lb.transform;
                    Transform deactivateRoot = tr;
                    for (Transform p = tr; p != null; p = p.parent)
                    {
                        if (string.Equals(p.name, "HealthBar", System.StringComparison.OrdinalIgnoreCase))
                        {
                            deactivateRoot = p;
                            break;
                        }
                    }
                    if (deactivateRoot.gameObject.activeSelf)
                    {
                        deactivateRoot.gameObject.SetActive(false);
                        anyDisabled = true;
                    }
                }
            }

            if (anyDisabled && debugLogs && entity != null)
                Debug.Log($"[WorldHealthBar] Legacy disabled on {entity.name}", entity);
        }

        public void Unregister(GameObject entity)
        {
            if (Instance != this || entity == null)
                return;
            if (_barsByEntity.TryGetValue(entity, out var entry))
            {
                if (entry.bar != null && entry.bar.gameObject != null)
                    Destroy(entry.bar.gameObject);
                _barsByEntity.Remove(entity);
            }
            _debugLastBarVisible.Remove(entity);
        }

        /// <summary>Quita todas las barras asociadas a este Health (p. ej. al destruir la unidad).</summary>
        public void Unregister(Health health)
        {
            if (Instance != this || health == null)
                return;
            _toRemove.Clear();
            foreach (var kv in _barsByEntity)
            {
                if (kv.Value.health == health)
                    _toRemove.Add(kv.Key);
            }
            for (int i = 0; i < _toRemove.Count; i++)
            {
                GameObject key = _toRemove[i];
                if (_barsByEntity.TryGetValue(key, out var entry))
                {
                    if (entry.bar != null && entry.bar.gameObject != null)
                        Destroy(entry.bar.gameObject);
                    _barsByEntity.Remove(key);
                    _debugLastBarVisible.Remove(key);
                }
            }
        }

        /// <summary>Llama desde HealthBarUI cuando cambia la vida para reaplicar visibilidad sin esperar solo a LateUpdate.</summary>
        public static void NotifyBarVisibilityRefresh(HealthBarUI bar)
        {
            if (Instance == null || bar == null) return;
            Instance.ApplyVisibilityForBar(bar);
        }

        void ApplyVisibilityForBar(HealthBarUI bar)
        {
            foreach (var kv in _barsByEntity)
            {
                if (kv.Value.bar != bar) continue;
                UpdateSingleBar(kv.Key, kv.Value.health, bar);
                return;
            }
        }

        public bool IsRegistered(Health health) => Instance == this && health != null && IsRegisteredByHealth(health);
        bool IsRegisteredByHealth(Health health)
        {
            foreach (var kv in _barsByEntity)
                if (kv.Value.health == health) return true;
            return false;
        }

        /// <summary>Resuelve Health en la entidad: mismo GameObject, hijos o padre. Así la barra se muestra aunque UnitSelectable esté en un hijo y Health en el root.</summary>
        static Health ResolveHealth(GameObject entity)
        {
            if (entity == null) return null;
            var h = entity.GetComponent<Health>();
            if (h != null) return h;
            h = entity.GetComponentInParent<Health>();
            if (h != null) return h;
            h = entity.GetComponentInChildren<Health>(true);
            return h;
        }

        /// <summary>API reutilizable: muestra la barra para una entidad que tenga Health (unidad, edificio). Si no tiene Health, no hace nada. Usar desde selección, hover, combate, etc.</summary>
        public static void ShowBarForEntity(GameObject entity)
        {
            if (entity == null) return;
            TryWakeAnyInstance();
            if (Instance == null)
            {
                if (!_warnedNoInstanceWhenShowingBar)
                {
                    _warnedNoInstanceWhenShowingBar = true;
                    Debug.LogWarning(
                        "[HealthBarManager] No hay instancia en la escena: las barras de vida no se mostrarán. " +
                        "Añade un GameObject con el componente HealthBarManager (ej. en el HUD o en la escena de juego) y asigna el prefab PF_HealthBarUI en el Inspector.",
                        entity);
                }
                return;
            }
            Instance.Register(entity);
        }

        /// <summary>API reutilizable: oculta la barra para la entidad.</summary>
        public static void HideBarForEntity(GameObject entity)
        {
            if (entity == null || Instance == null) return;
            Instance.Unregister(entity);
        }

        private void LateUpdate()
        {
            if (Instance != this)
                return;

            if (worldCamera == null)
                worldCamera = Camera.main;
            if (worldCamera == null || canvas == null || canvasRect == null)
                return;

            _toRemove.Clear();

            foreach (var kv in _barsByEntity)
            {
                GameObject entity = kv.Key;
                Health health = kv.Value.health;
                HealthBarUI bar = kv.Value.bar;

                if (entity == null || health == null || bar == null)
                {
                    _toRemove.Add(kv.Key);
                    continue;
                }

                UpdateSingleBar(entity, health, bar);
            }

            for (int i = 0; i < _toRemove.Count; i++)
            {
                GameObject key = _toRemove[i];
                if (_barsByEntity.TryGetValue(key, out var entry))
                {
                    if (entry.bar != null && entry.bar.gameObject != null)
                        Destroy(entry.bar.gameObject);
                    _barsByEntity.Remove(key);
                    _debugLastBarVisible.Remove(key);
                }
            }
        }

        void UpdateSingleBar(GameObject entity, Health health, HealthBarUI bar)
        {
            if (worldCamera == null)
                worldCamera = Camera.main;
            if (worldCamera == null || canvas == null || canvasRect == null)
                return;

            Vector3 worldPos = GetBarWorldPositionForEntity(entity, health);
            float maxDistSq = maxBarDistance * maxBarDistance;
            string hideReason = null;
            if (maxDistSq > 0f && (worldPos - worldCamera.transform.position).sqrMagnitude > maxDistSq)
            {
                hideReason = "distance";
                bar.gameObject.SetActive(false);
                LogVisibilityDebug(entity, health, false, hideReason);
                return;
            }

            Vector3 visibilitySample = entity.transform.position + Vector3.up * 0.75f;
            Vector3 viewport = worldCamera.WorldToViewportPoint(visibilitySample);
            float m = Mathf.Max(0f, viewportVisibilityMargin);
            bool inViewport = viewport.z > 0f &&
                viewport.x >= -m && viewport.x <= 1f + m &&
                viewport.y >= -m && viewport.y <= 1f + m;

            bool showByHealth = health.IsAlive && (showWhenFull || health.CurrentHP < health.MaxHP);
            bool visible = inViewport && showByHealth;

            if (!health.IsAlive)
                hideReason = "dead";
            else if (!showByHealth)
                hideReason = "full";
            else if (!inViewport)
                hideReason = "offscreen";

            bar.gameObject.SetActive(visible);
            LogVisibilityDebug(entity, health, visible, hideReason);

            if (!visible)
                return;

            Vector3 screenPoint = worldCamera.WorldToScreenPoint(worldPos);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPoint,
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : worldCamera,
                out Vector2 localPoint
            );

            bar.RectTransform.localPosition = localPoint;
            bar.Refresh();
        }

        void LogVisibilityDebug(GameObject entity, Health health, bool visible, string hideReason)
        {
            if (entity == null || health == null)
                return;

            bool unchanged = _debugLastBarVisible.TryGetValue(entity, out bool prevVis) && prevVis == visible;
            _debugLastBarVisible[entity] = visible;
            if (unchanged || !debugLogs)
                return;

            float fill = health.MaxHP > 0 ? health.CurrentHP / (float)health.MaxHP : 0f;
            if (visible)
                Debug.Log($"[WorldHealthBar] Show modern {entity.name} {health.CurrentHP}/{health.MaxHP} fill={fill:F3} active=True", entity);
            else
                Debug.Log($"[WorldHealthBar] Hide modern {entity.name} reason={hideReason ?? "unknown"}", entity);
        }

        /// <summary>Posición mundial de la barra: sobre la entidad. Si Health está en la entidad usa BarAnchor; si está en el padre usa posición de la entidad.</summary>
        static Vector3 GetBarWorldPositionForEntity(GameObject entity, Health health)
        {
            if (health == null || entity == null) return Vector3.zero;
            if (health.transform == entity.transform || entity.transform.IsChildOf(health.transform))
                return health.GetBarWorldPosition();
            return entity.transform.position + Vector3.up * 2f;
        }
    }
}
