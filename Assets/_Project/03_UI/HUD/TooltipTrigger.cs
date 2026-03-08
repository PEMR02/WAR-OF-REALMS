using UnityEngine;
using UnityEngine.EventSystems;

namespace Project.UI
{
    /// <summary>
    /// Agrega este componente a cualquier botón para mostrar un tooltip al hacer hover.
    /// </summary>
    public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [TextArea(2, 6)]
        public string content;

        public void OnPointerEnter(PointerEventData _)
        {
            if (!string.IsNullOrWhiteSpace(content))
                TooltipUI.Show(content);
        }

        public void OnPointerExit(PointerEventData _)
        {
            TooltipUI.Hide();
        }

        public void OnPointerClick(PointerEventData _)
        {
            TooltipUI.Hide();
        }
    }
}
