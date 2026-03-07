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

        public RectTransform RectTransform { get; private set; }

        private Health _target;
        private float _lastNormalized = -1f;
        private const float NormalizedTolerance = 0.0001f;

        private void Awake()
        {
            RectTransform = GetComponent<RectTransform>();
            if (fillImage != null && fillImage.type != Image.Type.Filled)
            {
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Horizontal;
                fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            }
        }

        public void Bind(Health health)
        {
            _target = health;
            _lastNormalized = -1f;
            ApplyColorsFromSource();
            Refresh();
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

            float value = _target != null ? _target.Normalized : 0f;
            value = Mathf.Clamp01(value);
            if (Mathf.Abs(value - _lastNormalized) <= NormalizedTolerance)
                return;
            _lastNormalized = value;
            fillImage.fillAmount = value;
        }

        public Health GetTarget() => _target;

        private void OnDestroy()
        {
            if (_target != null && HealthBarManager.Instance != null)
                HealthBarManager.Instance.Unregister(_target);
        }
    }
}
