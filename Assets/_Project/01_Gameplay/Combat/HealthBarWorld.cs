using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Project.Gameplay.Combat
{
    /// <summary>
    /// Barra en mundo (vida, recurso restante, etc.). Lee datos de cualquier IWorldBarSource en el padre.
    /// Solo visible cuando la entidad está seleccionada (RTSSelectionController).
    /// Sirve para unidades, edificios y recursos con la misma prefab.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class HealthBarWorld : MonoBehaviour
    {
        [Header("Bar")]
        [Tooltip("Image de relleno. Type = Filled, Fill Method = Horizontal, Fill Origin = Left.")]
        public Image fillImage;
        [Tooltip("Fondo: parte vacía (daño o recurso agotado). Debe estar detrás de Fill.")]
        public Image backgroundImage;
        [Tooltip("Marco negro: imagen más atrás (primer hijo). Se pinta con Color Border (negro). Asigna una Image con sprite Square para marco recto.")]
        public Image borderImage;
        [Tooltip("Altura sobre el pivote del padre (solo si useLocalOffset = true).")]
        public Vector3 localOffset = new Vector3(0f, 2f, 0f);
        [Tooltip("Si true, usa localOffset respecto al padre.")]
        public bool useLocalOffset = true;
        [Tooltip("Escala visual de la barra. Menor = más discreta.")]
        [Range(0.25f, 2f)] public float barScaleMultiplier = 0.5f;
        [Tooltip("Si true, compensa la escala del padre para mantener tamaño visual consistente (unidades vs edificios escalados).")]
        public bool keepConstantWorldSize = false;
        [Tooltip("Margen entre el marco negro y la barra (verde/rojo). A mayor valor, barra más pequeña dentro del marco.")]
        [Range(0f, 15f)] public float barPadding = 2f;
        [Tooltip("Grosor del marco negro si usas borderImage (unidades UI).")]
        [Range(0f, 8f)] public float borderWidth = 2f;
        [Tooltip("Redondea la posición para que la barra se vea más nítida (evita subpíxeles).")]
        public bool snapPositionForSharpness = true;
        [Tooltip("Nombre del anchor a buscar automáticamente si no hay anchor configurado en WorldBarSettings.")]
        public string defaultAnchorName = "BarAnchor";
        [Tooltip("Si no hay anchor, calcula altura usando el top de renderers del modelo.")]
        public bool autoUseRendererTopWhenNoAnchor = true;
        [Range(0f, 5f)] public float rendererTopPadding = 0.3f;

        [Header("Colores por defecto (solo si la fuente no devuelve colores; normalmente la fuente los define)")]
        [Tooltip("Color de relleno por defecto (vida llena).")]
        public Color colorFullHealth = new Color(0.2f, 1f, 0.2f);
        [Tooltip("Color de fondo por defecto (vida vacía / daño).")]
        public Color colorNoHealth = new Color(0.9f, 0.1f, 0.1f);
        [Tooltip("Color del borde si usas borderImage.")]
        public Color colorBorder = Color.black;

        public enum BillboardMode
        {
            None,
            YAxisOnly,
            Full
        }

        [Header("Opcional")]
        [Tooltip("None = barra fija sobre la entidad (no gira). YAxisOnly = solo gira en horizontal. Full = siempre mira a la cámara.")]
        public BillboardMode billboardMode = BillboardMode.None;

        [Header("Debug")]
        [Tooltip("Activar para ver en Consola si Show() encuentra fuente y activa la barra.")]
        public bool debugLogs = false;

        private IWorldBarSource _source;
        private WorldBarSettings _settings;
        private Canvas _canvas;
        private Vector3 _initialScale;
        private bool _forcedVisible = false;
        private Transform _sourceTransform;
        private Transform _cachedAnchor;
        private Renderer[] _cachedRenderers;
        private bool _hasRendererBounds;
        private Bounds _rendererBounds;

        IWorldBarSource ResolveSource()
        {
            if (_source != null) return _source;

            _source = GetComponentInParent<IWorldBarSource>();
            if (_source != null) return _source;

            // Fallback robusto si la búsqueda por interfaz no responde en algún prefab/runtime.
            var health = GetComponentInParent<Health>();
            if (health != null) { _source = health; return _source; }

            var resource = GetComponentInParent<Project.Gameplay.Resources.ResourceNode>();
            if (resource != null) { _source = resource; return _source; }

            return null;
        }

        void ResolveSourceTransform()
        {
            if (_source is Component c)
                _sourceTransform = c.transform;
            else if (_sourceTransform == null)
                _sourceTransform = transform.parent;
        }

        Transform FindAnchorByName(Transform root, string anchorName)
        {
            if (root == null || string.IsNullOrWhiteSpace(anchorName))
                return null;

            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (string.Equals(all[i].name, anchorName, StringComparison.OrdinalIgnoreCase))
                    return all[i];
            }
            return null;
        }

        void RefreshRendererBounds()
        {
            if (_sourceTransform == null)
            {
                _cachedRenderers = null;
                _hasRendererBounds = false;
                return;
            }

            _cachedRenderers = _sourceTransform.GetComponentsInChildren<Renderer>(true);
            _hasRendererBounds = false;
            for (int i = 0; i < _cachedRenderers.Length; i++)
            {
                var r = _cachedRenderers[i];
                if (r == null || !r.enabled) continue;
                if (!_hasRendererBounds)
                {
                    _rendererBounds = r.bounds;
                    _hasRendererBounds = true;
                }
                else
                {
                    _rendererBounds.Encapsulate(r.bounds);
                }
            }
        }

        void ResolveAnchorAndBounds()
        {
            _cachedAnchor = null;
            ResolveSourceTransform();
            if (_sourceTransform == null) return;

            if (_settings != null && IsValidAnchorForSource(_settings.barAnchor))
            {
                _cachedAnchor = _settings.barAnchor;
            }
            else
            {
                string anchorName = _settings != null && !string.IsNullOrWhiteSpace(_settings.autoAnchorName)
                    ? _settings.autoAnchorName
                    : defaultAnchorName;
                _cachedAnchor = FindAnchorByName(_sourceTransform, anchorName);
            }

            RefreshRendererBounds();
        }

        bool IsValidAnchorForSource(Transform anchor)
        {
            if (anchor == null || _sourceTransform == null) return false;

            // Debe pertenecer a la misma jerarquía de la entidad en escena.
            if (anchor == _sourceTransform) return true;
            if (anchor.IsChildOf(_sourceTransform)) return true;

            // Evita refs cruzadas al asset/prefab stage o a otra entidad runtime.
            if (anchor.root != _sourceTransform.root) return false;
            return anchor.IsChildOf(_sourceTransform);
        }

        Vector3 ComputeWorldOffset(Vector3 localLikeOffset)
        {
            if (_sourceTransform == null) return localLikeOffset;
            // Offset en unidades de mundo, sin amplificar por escala del edificio.
            return _sourceTransform.TransformDirection(localLikeOffset);
        }

        Vector3 ResolveDesiredWorldPosition(Vector3 effectiveOffset)
        {
            ResolveSourceTransform();
            if (_sourceTransform == null) return transform.position;

            if (_cachedAnchor != null)
                return _cachedAnchor.position + ComputeWorldOffset(effectiveOffset);

            bool useRendererTop = _settings != null ? _settings.autoUseRendererTopWhenNoAnchor : autoUseRendererTopWhenNoAnchor;
            float topPadding = _settings != null ? _settings.rendererTopPadding : rendererTopPadding;
            if (useRendererTop && _hasRendererBounds)
                return new Vector3(_rendererBounds.center.x, _rendererBounds.max.y + topPadding, _rendererBounds.center.z) + ComputeWorldOffset(effectiveOffset);

            return _sourceTransform.position + ComputeWorldOffset(effectiveOffset);
        }

        void Awake()
        {
            _source = ResolveSource();
            _settings = GetComponentInParent<WorldBarSettings>();
            ResolveSourceTransform();
            ResolveAnchorAndBounds();
            _canvas = GetComponent<Canvas>();
            _initialScale = transform.localScale;
            // Prefabs legacy pueden traer Canvas en escala 1 (enorme en mundo para edificios).
            // Normalizamos a una base segura para tamaño consistente.
            float maxInitialAxis = Mathf.Max(Mathf.Abs(_initialScale.x), Mathf.Abs(_initialScale.y), Mathf.Abs(_initialScale.z));
            if (maxInitialAxis > 0.05f)
                _initialScale = Vector3.one * 0.01f;

            if (fillImage == null)
                fillImage = GetComponentInChildren<Image>();

            if (fillImage != null && fillImage.type != Image.Type.Filled)
            {
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Horizontal;
                fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            }

            // Alinear Fill y Background al mismo rect (stretch) para que no se vean corridos
            AlignBarRects();

            if (_canvas != null)
            {
                _canvas.renderMode = RenderMode.WorldSpace;
                _canvas.worldCamera = Camera.main;
            }

            DisableUiRaycastTargets();
            // _forcedVisible permanece en su valor actual (false por defecto).
            // OnEnable() se encarga de ocultar si _forcedVisible es false.
        }

        void LateUpdate()
        {
            if (!_forcedVisible)
            {
                Hide();
                return;
            }

            _source = ResolveSource();
            ResolveSourceTransform();

            if (_source == null)
            {
                Hide();
                return;
            }

            if (_settings == null)
                _settings = GetComponentInParent<WorldBarSettings>();

            float scaleMultiplier = _settings != null ? _settings.barScaleMultiplier : barScaleMultiplier;
            // Si hay settings y useLocalOffsetOverride=true → usar offset de settings.
            // Si useLocalOffsetOverride=false → caer al offset/flag del propio HealthBarWorld.
            bool useOffset = (_settings != null && _settings.useLocalOffsetOverride) ? true : useLocalOffset;
            Vector3 offset = (_settings != null && _settings.useLocalOffsetOverride) ? _settings.localOffset : localOffset;

            Vector3 effectiveOffset = useOffset ? offset : Vector3.zero;
            Vector3 desiredWorldPos = ResolveDesiredWorldPosition(effectiveOffset);
            if (snapPositionForSharpness)
            {
                desiredWorldPos.x = Mathf.Round(desiredWorldPos.x * 100f) / 100f;
                desiredWorldPos.y = Mathf.Round(desiredWorldPos.y * 100f) / 100f;
                desiredWorldPos.z = Mathf.Round(desiredWorldPos.z * 100f) / 100f;
            }
            transform.position = desiredWorldPos;

            Vector3 baseScale = _initialScale.sqrMagnitude > 0.001f ? _initialScale : Vector3.one * 0.01f;
            Vector3 localScale = Vector3.Scale(baseScale, new Vector3(scaleMultiplier, scaleMultiplier, scaleMultiplier));
            if (keepConstantWorldSize && transform.parent != null)
            {
                Vector3 parentLossy = transform.parent.lossyScale;
                localScale = new Vector3(
                    localScale.x / Mathf.Max(0.001f, Mathf.Abs(parentLossy.x)),
                    localScale.y / Mathf.Max(0.001f, Mathf.Abs(parentLossy.y)),
                    localScale.z / Mathf.Max(0.001f, Mathf.Abs(parentLossy.z))
                );
            }
            transform.localScale = localScale;

            // Rotación: fija (horizontal en mundo) o según cámara
            if (billboardMode == BillboardMode.None)
            {
                // Barra siempre horizontal en mundo (plano XZ), no gira al mover la cámara
                transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            else if (Camera.main != null)
            {
                if (billboardMode == BillboardMode.YAxisOnly)
                {
                    Vector3 toCamera = Camera.main.transform.position - transform.position;
                    toCamera.y = 0f;
                    if (toCamera.sqrMagnitude > 0.0001f)
                        transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
                }
                else
                    transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);
            }

            float ratio = _source.GetBarRatio01();
            Color full = _source.GetBarFullColor();
            Color empty = _source.GetBarEmptyColor();
            if (fillImage != null)
            {
                if (fillImage.type != Image.Type.Filled)
                {
                    fillImage.type = Image.Type.Filled;
                    fillImage.fillMethod = Image.FillMethod.Horizontal;
                    fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
                }
                fillImage.fillAmount = ratio;
                fillImage.color = full;
            }
            if (backgroundImage != null)
                backgroundImage.color = empty;
            if (borderImage != null)
                borderImage.color = colorBorder;
        }

        public void Show()
        {
            _forcedVisible = true;

            if (_settings == null)
                _settings = GetComponentInParent<WorldBarSettings>();
            _source = ResolveSource();
            ResolveAnchorAndBounds();

            // Posicionar ANTES de activar para evitar el flash de un frame en posición incorrecta
            bool useOffset = (_settings != null && _settings.useLocalOffsetOverride) ? true : useLocalOffset;
            Vector3 offset = (_settings != null && _settings.useLocalOffsetOverride) ? _settings.localOffset : localOffset;
            Vector3 effectiveOffset = useOffset ? offset : Vector3.zero;
            transform.position = ResolveDesiredWorldPosition(effectiveOffset);

            // Activar parent si estuviera inactivo (ej. prefabs con HealthBar padre desactivado)
            if (transform.parent != null && !transform.parent.gameObject.activeSelf)
                transform.parent.gameObject.SetActive(true);

            gameObject.SetActive(true);

            if (debugLogs)
                Debug.Log($"[HealthBarWorld] Show() en {gameObject.name} | Source={(_source != null ? "ok" : "NULL")}");
        }

        public void Hide()
        {
            _forcedVisible = false;
            gameObject.SetActive(false);
        }

        void OnEnable()
        {
            if (_canvas == null) _canvas = GetComponent<Canvas>();
            if (_canvas != null)
            {
                _canvas.renderMode = RenderMode.WorldSpace;
                _canvas.worldCamera = Camera.main;
            }
            if (fillImage == null) fillImage = GetComponentInChildren<Image>();
            _source = ResolveSource();
            ResolveAnchorAndBounds();
            if (_settings == null) _settings = GetComponentInParent<WorldBarSettings>();
            DisableUiRaycastTargets();
            if (backgroundImage != null)
                backgroundImage.color = colorNoHealth;
            if (borderImage != null)
                borderImage.color = colorBorder;
            AlignBarRects();

            // Si se activó desde fuera sin pasar por Show(), ocultamos de inmediato
            if (!_forcedVisible)
                gameObject.SetActive(false);
        }

        /// <summary>Marco (negro) > Fondo (rojo) y Relleno (verde) misma medida. barPadding = margen entre marco y barra.</summary>
        void AlignBarRects()
        {
            var parent = transform as RectTransform;
            if (parent == null) return;

            float p = Mathf.Max(0f, barPadding);
            float b = Mathf.Max(0f, borderWidth);
            float inset = b + p; // marco + margen; la barra (verde y rojo) va dentro con la misma medida

            if (borderImage != null)
            {
                var rt = borderImage.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            if (backgroundImage != null)
            {
                var rt = backgroundImage.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(inset, inset);
                rt.offsetMax = new Vector2(-inset, -inset);
            }
            if (fillImage != null)
            {
                var rt = fillImage.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(inset, inset);
                rt.offsetMax = new Vector2(-inset, -inset);
            }
        }

        void DisableUiRaycastTargets()
        {
            // Evita que EventSystem considere la barra como UI bajo el cursor.
            var raycaster = GetComponent<GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = false;

            if (fillImage != null) fillImage.raycastTarget = false;
            if (backgroundImage != null) backgroundImage.raycastTarget = false;
            if (borderImage != null) borderImage.raycastTarget = false;
        }
    }
}
