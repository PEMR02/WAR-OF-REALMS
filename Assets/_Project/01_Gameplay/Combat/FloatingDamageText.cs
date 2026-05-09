using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Project.Gameplay.Combat
{
    /// <summary>
    /// Muestra un número flotante (daño o curación) en mundo y luego se destruye.
    /// Se instancia desde Health al recibir daño o al curar.
    /// </summary>
    public class FloatingDamageText : MonoBehaviour
    {
        public TextMeshProUGUI label;
        public float floatSpeed = 2f;
        public float lifetime = 1.2f;
        public float fadeStart = 0.6f;

        private float _timer;
        private CanvasGroup _cg;
        RectTransform _rect;

        static Canvas s_canvas;
        static RectTransform s_canvasRect;
        static Camera s_cachedCamera;
        static bool s_loggedMissingUi;

        void Awake()
        {
            _cg = GetComponent<CanvasGroup>();
            _rect = GetComponent<RectTransform>();
        }

        public static void Spawn(Vector3 worldPos, int amount, bool isHeal = false)
        {
            if (!TryEnsureUiRoot(out var canvas, out var canvasRect, out var cam))
                return;

            var screen = cam.WorldToScreenPoint(worldPos + Vector3.up * 1.5f);
            if (screen.z <= 0f) return;

            var go = new GameObject("FloatingDamage");
            go.transform.SetParent(canvas.transform, false);
            var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local))
                return;
            rt.anchoredPosition = local;
            rt.sizeDelta = new Vector2(120f, 40f);

            var cg = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
            cg.alpha = 1f;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.GetComponent<RectTransform>() ?? textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = isHeal ? $"+{amount}" : $"-{amount}";
            tmp.fontSize = 36;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.color = isHeal ? new Color(0.2f, 1f, 0.3f) : new Color(1f, 0.3f, 0.2f);

            var fd = go.AddComponent<FloatingDamageText>();
            fd.label = tmp;
        }

        void Update()
        {
            _timer += Time.deltaTime;
            if (_rect != null)
                _rect.anchoredPosition += Vector2.up * (floatSpeed * 45f * Time.deltaTime);

            if (_timer >= fadeStart && _cg != null)
                _cg.alpha = 1f - Mathf.Clamp01((_timer - fadeStart) / (lifetime - fadeStart));

            if (_timer >= lifetime)
                Destroy(gameObject);
        }

        static bool TryEnsureUiRoot(out Canvas canvas, out RectTransform canvasRect, out Camera cam)
        {
            canvas = s_canvas;
            canvasRect = s_canvasRect;
            cam = s_cachedCamera != null ? s_cachedCamera : Camera.main;
            if (cam == null)
            {
                LogMissingUiOnce("[Combat] FloatingDamageText omitido: no hay cámara principal.");
                return false;
            }
            s_cachedCamera = cam;

            if (canvas == null || canvasRect == null)
            {
                var root = new GameObject("FloatingDamageCanvas");
                canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasRect = root.GetComponent<RectTransform>();
                root.AddComponent<CanvasScaler>();
                root.AddComponent<GraphicRaycaster>();
                s_canvas = canvas;
                s_canvasRect = canvasRect;
            }

            return canvas != null && canvasRect != null;
        }

        static void LogMissingUiOnce(string msg)
        {
            if (s_loggedMissingUi) return;
            s_loggedMissingUi = true;
            Debug.LogWarning(msg);
        }
    }
}
