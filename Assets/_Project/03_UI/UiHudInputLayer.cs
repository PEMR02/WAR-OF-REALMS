using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// Asegura que un tramo del HUD reciba clics por encima de otros Canvas overlay
    /// (p. ej. <see cref="Project.Gameplay.Combat.HealthBarManager"/> usa sortingOrder 100).
    /// </summary>
    public static class UiHudInputLayer
    {
        public const int DefaultSortAboveHealthBars = 200;

        public static void EnsureNestedInputCanvas(Transform root, int sortOrder = DefaultSortAboveHealthBars)
        {
            if (root == null) return;
            var go = root.gameObject;
            var c = go.GetComponent<Canvas>();
            if (c == null)
                c = go.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder = sortOrder;
            if (go.GetComponent<GraphicRaycaster>() == null)
                go.AddComponent<GraphicRaycaster>();
        }

        public static void FixCanvasGroupsRecursive(Transform t)
        {
            if (t == null) return;
            var cg = t.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
            for (int i = 0; i < t.childCount; i++)
                FixCanvasGroupsRecursive(t.GetChild(i));
        }
    }
}
