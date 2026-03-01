using UnityEngine;

namespace Project.Gameplay.Combat
{
    /// <summary>
    /// Ajustes opcionales por entidad para la barra en mundo.
    /// Permite variar tamaño/offset por prefab sin duplicar lógica.
    /// </summary>
    public class WorldBarSettings : MonoBehaviour
    {
        [Header("Scale")]
        [Tooltip("Multiplicador de escala de barra para esta entidad.")]
        [Range(0.25f, 3f)] public float barScaleMultiplier = 1f;

        [Header("Offset")]
        [Tooltip("Si true, HealthBarWorld usa este offset local para posicionar la barra.")]
        public bool useLocalOffsetOverride = true;
        [Tooltip("Offset local de la barra respecto al pivote de la entidad.")]
        public Vector3 localOffset = new Vector3(0f, 2f, 0f);

        [Header("Anchor (opcional)")]
        [Tooltip("Anchor explícito para la barra (recomendado: child 'BarAnchor'). Si está vacío se intenta encontrar uno automáticamente.")]
        public Transform barAnchor;
        [Tooltip("Nombre del anchor a buscar automáticamente dentro de la entidad.")]
        public string autoAnchorName = "BarAnchor";

        [Header("Auto altura por renderer")]
        [Tooltip("Si no hay anchor, calcular altura usando el top de los renderers del modelo.")]
        public bool autoUseRendererTopWhenNoAnchor = true;
        [Tooltip("Padding extra sobre el top del renderer cuando no hay anchor.")]
        [Range(0f, 5f)] public float rendererTopPadding = 0.3f;
    }
}
