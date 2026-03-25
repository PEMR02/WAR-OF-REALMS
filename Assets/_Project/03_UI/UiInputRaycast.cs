using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Project.UI
{
    /// <summary>
    /// Con <see cref="UnityEngine.InputSystem.UI.InputSystemUIInputModule"/>, <c>EventSystem.IsPointerOverGameObject()</c>
    /// sin argumento suele devolver false y el RTS trata el clic como si fuera en el mundo (p. ej. deselección).
    /// Aquí se hace <see cref="EventSystem.RaycastAll"/> con la posición del ratón/táctil, independiente del id de puntero
    /// y del orden de ejecución respecto al módulo UI.
    /// </summary>
    public static class UiInputRaycast
    {
        static readonly List<RaycastResult> RaycastResults = new List<RaycastResult>(24);

        public static bool IsPointerOverGameObject()
        {
            var es = EventSystem.current;
            if (es == null) return false;

            Vector2 pos;
            var mouse = Mouse.current;
            if (mouse != null)
                pos = mouse.position.ReadValue();
            else if (Touchscreen.current != null)
                pos = Touchscreen.current.primaryTouch.position.ReadValue();
            else
                return false;

            RaycastResults.Clear();
            es.RaycastAll(new PointerEventData(es) { position = pos }, RaycastResults);
            return RaycastResults.Count > 0;
        }
    }
}
