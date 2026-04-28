using UnityEngine;
using UnityEngine.UI;

namespace Project.Gameplay.Combat
{
    /// <summary>
    /// Barra de vida UI (Screen Space) asociada a un Health.
    /// Estructura como las barras antiguas: Border (marco), Background (vida vacía/daño), Fill (vida actual).
    /// </summary>
    public class HealthBarUI : MonoBehaviour
    {
        static Sprite s_runtimeWhiteSprite;

        [Header("Referencias")]
        [SerializeField] private Image fillImage;
        [Tooltip("Fondo (parte vacía / daño). Si no se asigna, solo se dibuja el fill.")]
        [SerializeField] private Image backgroundImage;
        [Tooltip("Marco opcional. Si no se asigna, no se dibuja borde.")]
        [SerializeField] private Image borderImage;

        [Header("Colores por defecto (si Health no implementa IWorldBarSource)")]
        [SerializeField] private Color colorFullHealth = new Color(0.2f, 1f, 0.2f);
        [SerializeField] private Color colorNoHealth = new Color(0.9f, 0.1f, 0.1f);
        [SerializeField] private Color colorBorder = Color.black;
        [Header("Debug")]
        [SerializeField] private bool debugLogs;

        public RectTransform RectTransform { get; private set; }

        private Health _target;

        private void Awake()
        {
            RectTransform = GetComponent<RectTransform>();
            if (fillImage == null)
                fillImage = GetComponentInChildren<Image>(true);
            EnsureUISpriteForBarImages();
            if (fillImage != null && fillImage.type != Image.Type.Filled)
            {
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Horizontal;
                fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            }
        }

        /// <summary>
        /// Sin sprite, <see cref="Image.Type.Filled"/> no recorta el relleno y la barra se ve siempre llena (p. ej. PF_HealthBarUI con sprite vacío).
        /// El panel de selección suele tener sprites en escena; aquí igualamos el comportamiento en runtime.
        /// </summary>
        static void EnsureWhiteUISprite(Image img)
        {
            if (img == null || img.sprite != null) return;
            if (s_runtimeWhiteSprite == null)
            {
                var tex = Texture2D.whiteTexture;
                s_runtimeWhiteSprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect);
            }
            img.sprite = s_runtimeWhiteSprite;
        }

        void EnsureUISpriteForBarImages()
        {
            EnsureWhiteUISprite(fillImage);
            EnsureWhiteUISprite(backgroundImage);
            EnsureWhiteUISprite(borderImage);
        }

        public void Bind(Health health)
        {
            UnsubscribeTarget();
            _target = health;
            if (_target != null)
            {
                _target.OnHealthChanged += OnTargetHealthChanged;
                if (debugLogs)
                    Debug.Log($"[WorldHealthBar] Health asignado: {_target.name} (root: {_target.transform.root.name})", this);
            }
            else if (debugLogs)
            {
                Debug.LogWarning("[WorldHealthBar] No se encontro Health para Bind.", this);
            }
            ApplyColorsFromSource();
            Refresh();
        }

        void OnTargetHealthChanged(int current, int max)
        {
            if (debugLogs)
                Debug.Log($"[WorldHealthBar] Cambio de vida recibido: {current}/{Mathf.Max(1, max)}", this);
            Refresh();
            HealthBarManager.NotifyBarVisibilityRefresh(this);
        }

        void UnsubscribeTarget()
        {
            if (_target != null)
                _target.OnHealthChanged -= OnTargetHealthChanged;
            _target = null;
        }

        void ApplyColorsFromSource()
        {
            var source = _target as IWorldBarSource;
            if (fillImage != null)
                fillImage.color = source != null ? source.GetBarFullColor() : colorFullHealth;
            if (backgroundImage != null)
                backgroundImage.color = source != null ? source.GetBarEmptyColor() : colorNoHealth;
            if (borderImage != null)
                borderImage.color = colorBorder;
        }

        public void Refresh()
        {
            if (fillImage == null) return;
            EnsureUISpriteForBarImages();
            if (fillImage.type != Image.Type.Filled)
            {
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Horizontal;
                fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            }

            float value = _target != null
                ? (_target.CurrentHP / (float)Mathf.Max(1, _target.MaxHP))
                : 0f;
            value = Mathf.Clamp01(value);
            fillImage.fillAmount = value;
        }

        public Health GetTarget() => _target;

        private void OnDestroy()
        {
            var h = _target;
            UnsubscribeTarget();
            if (h != null && HealthBarManager.Instance != null)
                HealthBarManager.Instance.Unregister(h);
        }
    }
}
