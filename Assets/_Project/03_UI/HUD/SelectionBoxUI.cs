using UnityEngine;

namespace Project.UI
{
    public class SelectionBoxUI : MonoBehaviour
    {
        public RectTransform canvasRect;   // RectTransform del Canvas
        public RectTransform box;          // RectTransform de la Image SelectionBox

        void Awake()
        {
            if (canvasRect == null)
                canvasRect = GetComponentInParent<Canvas>().GetComponent<RectTransform>();
        }

        public void Show(Vector2 startScreen, Vector2 endScreen)
        {
            if (box == null || canvasRect == null) return;

            if (!box.gameObject.activeSelf)
                box.gameObject.SetActive(true);

            // Convertir Screen -> Local (Canvas space)
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, startScreen, null, out var a);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, endScreen, null, out var b);

            Vector2 min = Vector2.Min(a, b);
            Vector2 max = Vector2.Max(a, b);

            box.anchoredPosition = min;
            box.sizeDelta = max - min;
        }

        public void Hide()
        {
            if (box != null && box.gameObject.activeSelf)
                box.gameObject.SetActive(false);
        }
    }
}
