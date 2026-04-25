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

        /// <summary>Una barra por entidad (GameObject), para que doble clic muestre barra en cada unidad aunque compartan Health (ej. mismo prefab con dos UnitSelectable).</summary>
        private readonly Dictionary<GameObject, (Health health, HealthBarUI bar)> _barsByEntity = new();
        private readonly List<GameObject> _toRemove = new List<GameObject>(32);
        private bool _createdCanvasRuntime;
        private static bool _warnedMissingPrefab;
        private static bool _warnedMissingCamera;
        private static bool _warnedNoInstanceWhenShowingBar;
        private static bool _warnedCanvasDelayed;
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

            HealthBarUI bar = Instantiate(healthBarPrefab, canvas.transform);
            bar.Bind(health);
            bar.gameObject.SetActive(true);
            bar.transform.SetAsLastSibling();
            bar.Refresh();
            _barsByEntity[entity] = (health, bar);
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
                }
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
            h = entity.GetComponentInChildren<Health>(true);
            if (h != null) return h;
            h = entity.GetComponentInParent<Health>();
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

                Vector3 worldPos = GetBarWorldPositionForEntity(entity, health);
                float maxDistSq = maxBarDistance * maxBarDistance;
                if (maxDistSq > 0f && (worldPos - worldCamera.transform.position).sqrMagnitude > maxDistSq)
                {
                    bar.gameObject.SetActive(false);
                    continue;
                }

                Vector3 visibilitySample = entity.transform.position + Vector3.up * 0.75f;
                Vector3 viewport = worldCamera.WorldToViewportPoint(visibilitySample);
                float m = Mathf.Max(0f, viewportVisibilityMargin);
                bool visible = viewport.z > 0f &&
                    viewport.x >= -m && viewport.x <= 1f + m &&
                    viewport.y >= -m && viewport.y <= 1f + m;
                bar.gameObject.SetActive(visible);

                if (!visible)
                    continue;

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

            for (int i = 0; i < _toRemove.Count; i++)
            {
                GameObject key = _toRemove[i];
                if (_barsByEntity.TryGetValue(key, out var entry))
                {
                    if (entry.bar != null && entry.bar.gameObject != null)
                        Destroy(entry.bar.gameObject);
                    _barsByEntity.Remove(key);
                }
            }
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
