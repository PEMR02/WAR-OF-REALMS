using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Project.UI
{
    /// <summary>
    /// Tooltip flotante singleton. Se crea automáticamente si no existe en escena.
    /// Úsalo con TooltipTrigger en los botones.
    /// </summary>
    public class TooltipUI : MonoBehaviour
    {
        public static TooltipUI Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureTooltip()
        {
            if (Instance != null) return;
            var go = new GameObject("__TooltipUI");
            go.AddComponent<TooltipUI>();
        }

        [Header("Visual")]
        [Tooltip("Panel raíz del tooltip. Si no se asigna, se crea automáticamente.")]
        public RectTransform panel;
        public TextMeshProUGUI tooltipText;

        [Header("Offset")]
        public Vector2 offset = new Vector2(12f, -12f);

        [Header("Style")]
        public Color backgroundColor = new Color(0.08f, 0.10f, 0.14f, 0.92f);
        public Color textColor = new Color(0.88f, 0.92f, 1f, 1f);
        [Range(8f, 18f)] public float fontSize = 12f;

        private Canvas _canvas;
        private RectTransform _canvasRect;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsurePanel();
            Hide();
        }

        void EnsurePanel()
        {
            // Buscar o crear un Canvas de overlay dedicado al tooltip
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                // Intentar encontrar el Canvas principal en escena
                _canvas = FindFirstObjectByType<Canvas>();
            }
            if (_canvas == null)
            {
                var canvasGo = new GameObject("TooltipCanvas");
                _canvas = canvasGo.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 999;
                canvasGo.AddComponent<CanvasScaler>();
                canvasGo.AddComponent<GraphicRaycaster>();
            }
            _canvasRect = _canvas.GetComponent<RectTransform>();

            if (panel == null)
            {
                var panelGo = new GameObject("TooltipPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                panelGo.transform.SetParent(_canvas.transform, false);

                panel = panelGo.GetComponent<RectTransform>();
                panel.pivot = new Vector2(0f, 1f);
                panel.anchorMin = Vector2.zero;
                panel.anchorMax = Vector2.zero;
                panel.sizeDelta = new Vector2(200f, 60f);

                var bg = panelGo.GetComponent<Image>();
                bg.color = backgroundColor;
                bg.raycastTarget = false;

                // Añadir outline para leer mejor
                var outline = panelGo.AddComponent<Outline>();
                outline.effectColor = new Color(0.3f, 0.6f, 1f, 0.5f);
                outline.effectDistance = new Vector2(1f, -1f);

                // Texto
                var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                textGo.transform.SetParent(panelGo.transform, false);

                var rt = textGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(6f, 4f);
                rt.offsetMax = new Vector2(-6f, -4f);

                tooltipText = textGo.GetComponent<TextMeshProUGUI>();
                tooltipText.fontSize = fontSize;
                tooltipText.color = textColor;
                tooltipText.raycastTarget = false;
                tooltipText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            }
        }

        void LateUpdate()
        {
            if (panel == null || !panel.gameObject.activeSelf) return;
            FollowMouse();
        }

        void FollowMouse()
        {
            if (_canvasRect == null) return;

            Vector2 mousePos = UnityEngine.InputSystem.Mouse.current != null
                ? UnityEngine.InputSystem.Mouse.current.position.ReadValue()
                : (Vector2)Input.mousePosition;

            // Calcular posición en canvas space
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, mousePos, _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera,
                out Vector2 localPoint);

            panel.anchoredPosition = localPoint + offset;

            // Evitar que salga de pantalla por la derecha/abajo
            Vector2 size = panel.sizeDelta;
            Vector2 canvasSize = _canvasRect.sizeDelta;
            float x = Mathf.Clamp(panel.anchoredPosition.x, 0f, canvasSize.x - size.x);
            float y = Mathf.Clamp(panel.anchoredPosition.y, -canvasSize.y + size.y, 0f);
            panel.anchoredPosition = new Vector2(x, y);
        }

        public static void Show(string content)
        {
            if (Instance == null) return;
            if (Instance.tooltipText != null)
                Instance.tooltipText.text = content;

            // Ajustar tamaño al contenido
            if (Instance.panel != null && Instance.tooltipText != null)
            {
                Instance.tooltipText.ForceMeshUpdate();
                Vector2 textSize = Instance.tooltipText.GetRenderedValues(false);
                Instance.panel.sizeDelta = new Vector2(
                    Mathf.Max(120f, textSize.x + 16f),
                    Mathf.Max(30f, textSize.y + 12f));
            }

            if (Instance.panel != null)
                Instance.panel.gameObject.SetActive(true);
        }

        public static void Hide()
        {
            if (Instance != null && Instance.panel != null)
                Instance.panel.gameObject.SetActive(false);
        }
    }
}
