using UnityEngine;

namespace Project.Gameplay.Combat
{
    /// <summary>
    /// Unifica creación del hijo BarAnchor y enlace a <see cref="Health"/> / <see cref="WorldBarSettings"/> en runtime.
    /// No modifica <see cref="HealthBarWorld"/>; la barra moderna sigue en <see cref="HealthBarManager"/>.
    /// </summary>
    public static class WorldBarRuntimeUtility
    {
        /// <param name="entity">Raíz de la entidad (edificio o unidad).</param>
        /// <param name="defaultOffsetY">Altura local Y del BarAnchor cuando se crea (si ya existe, no se mueve el transform).</param>
        public static void EnsureWorldBarAnchor(GameObject entity, float defaultOffsetY)
        {
            if (entity == null) return;

            Transform root = entity.transform;
            Transform anchor = root.Find("BarAnchor");
            if (anchor == null)
            {
                var go = new GameObject("BarAnchor");
                anchor = go.transform;
                anchor.SetParent(root, false);
                anchor.localPosition = new Vector3(0f, defaultOffsetY, 0f);
            }

            Health health = entity.GetComponent<Health>();
            if (health == null) health = entity.GetComponentInParent<Health>();
            if (health == null) health = entity.GetComponentInChildren<Health>(true);
            if (health != null)
                health.SetBarAnchor(anchor);

            WorldBarSettings settings = entity.GetComponent<WorldBarSettings>();
            if (settings == null) settings = entity.GetComponentInChildren<WorldBarSettings>(true);
            if (settings != null)
            {
                settings.barAnchor = anchor;
                settings.autoAnchorName = "BarAnchor";
                settings.useLocalOffsetOverride = true;
                settings.localOffset = Vector3.zero;
            }
        }
    }
}
