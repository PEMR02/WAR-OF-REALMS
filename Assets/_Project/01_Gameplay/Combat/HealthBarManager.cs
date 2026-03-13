using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Combat
{
    /// <summary>
    /// Singleton persistente: un solo Canvas en Screen Space, barras instanciadas dinámicamente
    /// y posicionadas con WorldToScreenPoint. Solo una instancia activa; duplicados en otras escenas se destruyen.
    /// Ciclo de vida: Awake establece Instance y DontDestroyOnLoad; OnDestroy limpia Instance y el Canvas creado en runtime.
    /// </summary>
    public class HealthBarManager : MonoBehaviour
    {
        public static HealthBarManager Instance { get; private set; }

        [SerializeField] private Camera worldCamera;
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform canvasRect;
        [SerializeField] private HealthBarUI healthBarPrefab;
        [Tooltip("Barras más lejos que esta distancia no se actualizan (solo se ocultan). Reduce coste cuando hay muchas unidades.")]
        [SerializeField] private float maxBarDistance = 120f;

        /// <summary>Una barra por entidad (GameObject), para que doble clic muestre barra en cada unidad aunque compartan Health (ej. mismo prefab con dos UnitSelectable).</summary>
        private readonly Dictionary<GameObject, (Health health, HealthBarUI bar)> _barsByEntity = new();
        private readonly List<GameObject> _toRemove = new List<GameObject>(32);
        private bool _createdCanvasRuntime;
        private static bool _warnedMissingCanvas;
        private static bool _warnedMissingPrefab;
        private static bool _warnedMissingCamera;
        private static bool _warnedNoInstanceWhenShowingBar;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    "[HealthBarManager] Ya existe una instancia activa. Destruyendo duplicado. Deja solo un HealthBarManager en el proyecto (ej. escena bootstrap o HUD).",
                    this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (worldCamera == null)
                worldCamera = Camera.main;
            if (worldCamera == null && !_warnedMissingCamera)
            {
                _warnedMissingCamera = true;
                Debug.LogWarning("[HealthBarManager] Cámara no asignada y Camera.main es null. Asigna World Camera en el Inspector o asegura una cámara con tag MainCamera.", this);
            }

            if (canvas != null && canvasRect == null)
                canvasRect = canvas.GetComponent<RectTransform>();

            if (canvas == null)
            {
                if (!_warnedMissingCanvas)
                {
                    _warnedMissingCanvas = true;
                    Debug.LogWarning(
                        "[HealthBarManager] Canvas no asignado. Creando Canvas en runtime como fallback solo para pruebas. Para producción asigna un Canvas en el Inspector.",
                        this);
                }
                var canvasGo = new GameObject("HealthBarsCanvas", typeof(RectTransform));
                canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;
                canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
                canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                canvasRect = canvasGo.GetComponent<RectTransform>();
                _createdCanvasRuntime = true;
            }

            if (healthBarPrefab == null && !_warnedMissingPrefab)
            {
                _warnedMissingPrefab = true;
                Debug.LogWarning("[HealthBarManager] Health Bar Prefab no asignado. Asigna PF_HealthBarUI en el Inspector para que se muestren las barras.", this);
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

                Vector3 screenPoint = worldCamera.WorldToScreenPoint(worldPos);

                bool visible = screenPoint.z > 0f;
                bar.gameObject.SetActive(visible);

                if (!visible)
                    continue;

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
