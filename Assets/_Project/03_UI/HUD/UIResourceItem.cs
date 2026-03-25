using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// Fila de recurso: icono (Image) + valor (TMP) y etiqueta opcional (TMP).
    /// Sin sprites en texto ni etiquetas &lt;sprite&gt;.
    /// </summary>
    public class UIResourceItem : MonoBehaviour
    {
        [Header("Icono")]
        [Tooltip("Referencia al componente Image del hijo (p. ej. 'Icon'). Arrastra el GameObject Icon o el componente Image de la jerarquía; no es un archivo del Project.")]
        [SerializeField] Image icon;
        [Tooltip("Sprite por defecto de esta fila si UIResourceBar no asigna uno en sus campos Wood/Stone/... Icon.")]
        [SerializeField] Sprite iconSprite;

        [Header("Texto")]
        [SerializeField] TMP_Text valueText;
        [SerializeField] TMP_Text labelText;

        void Reset()
        {
            if (icon == null)
            {
                var tr = transform.Find("Icon");
                if (tr != null)
                    icon = tr.GetComponent<Image>();
            }
            if (valueText == null)
            {
                var tr = transform.Find("Value");
                if (tr != null)
                    valueText = tr.GetComponent<TMP_Text>();
            }
            if (labelText == null)
            {
                var tr = transform.Find("Label");
                if (tr != null)
                    labelText = tr.GetComponent<TMP_Text>();
            }
        }

        /// <param name="spriteFromBar">Si no es null, tiene prioridad sobre iconSprite de esta fila.</param>
        public void SetData(Sprite spriteFromBar, string label, int value)
        {
            if (icon != null)
            {
                Sprite use = spriteFromBar != null ? spriteFromBar : iconSprite;
                if (use != null)
                    icon.sprite = use;
                icon.enabled = icon.sprite != null;
            }

            if (labelText != null)
            {
                bool show = !string.IsNullOrEmpty(label);
                labelText.gameObject.SetActive(show);
                if (show)
                    labelText.text = label;
            }

            SetValue(value);
        }

        public void SetValue(int value)
        {
            if (valueText != null)
                valueText.text = value.ToString();
        }

        public void SetValueFormatted(int current, int max)
        {
            if (valueText != null)
                valueText.text = $"{current}/{max}";
        }

        public void SetValueTextColor(Color color)
        {
            if (valueText != null)
                valueText.color = color;
        }
    }
}
