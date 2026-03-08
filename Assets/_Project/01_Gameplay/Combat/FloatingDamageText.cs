using UnityEngine;
using TMPro;

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

        void Awake()
        {
            _cg = GetComponent<CanvasGroup>();
        }

        public static void Spawn(Vector3 worldPos, int amount, bool isHeal = false)
        {
            if (Camera.main == null) return;

            var go = new GameObject("FloatingDamage");
            go.transform.position = worldPos + Vector3.up * 1.5f;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(2f, 0.8f);
            rt.localScale = Vector3.one * 0.02f;

            go.AddComponent<CanvasGroup>();

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
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
            transform.position += Vector3.up * (floatSpeed * Time.deltaTime);

            if (_timer >= fadeStart && _cg != null)
                _cg.alpha = 1f - Mathf.Clamp01((_timer - fadeStart) / (lifetime - fadeStart));

            if (_timer >= lifetime)
                Destroy(gameObject);
        }
    }
}
