using UnityEngine;

namespace Project.Core
{
    /// <summary>
    /// Nombres de capas del proyecto. Las unidades viven en <see cref="Units"/>; no mezclar con recursos ni terreno.
    /// </summary>
    public static class ProjectLayers
    {
        public const string Units = "Units";
        public const string Building = "Building";
        public const string Resource = "Resource";

        public static int GetLayerIndex(string layerName)
        {
            return string.IsNullOrEmpty(layerName) ? -1 : LayerMask.NameToLayer(layerName);
        }

        public static LayerMask GetMaskForLayerName(string layerName)
        {
            int i = GetLayerIndex(layerName);
            return i < 0 ? default : (LayerMask)(1 << i);
        }
    }
}
