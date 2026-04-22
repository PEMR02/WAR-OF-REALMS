using UnityEngine;

namespace Project.Gameplay
{
    /// <summary>
    /// Apariencia del outline (borde 3D) para un tipo de entidad. Usado por edificios y recursos.
    /// </summary>
    [System.Serializable]
    public class OutlineAppearance
    {
        [Tooltip("Color del borde al seleccionar.")]
        public Color selectionColor = new Color(0.15f, 0.85f, 0.35f, 0.98f);
        [Tooltip("Color del borde al hacer hover.")]
        public Color hoverColor = new Color(0.4f, 0.75f, 0.4f, 0.8f);
        [Tooltip("Grosor del borde (escala del mesh).")]
        [Range(1.02f, 1.25f)] public float outlineScale = 1.12f;
    }

    /// <summary>
    /// Apariencia de selección para unidades: anillo en el suelo + outline 3D (opcional; el outline 3D en skinned suele verse mal).
    /// </summary>
    [System.Serializable]
    public class UnitSelectionAppearance
    {
        [Header("Anillo en el suelo (dona)")]
        [Tooltip("Color del anillo bajo la unidad.")]
        public Color ringColor = new Color(0.2f, 0.95f, 0.4f, 0.9f);
        [Tooltip("Brillo/emisión del anillo (5 = muy visible; permite HDR).")]
        [Range(1f, 12f)] public float ringBrightness = 5f;
        [Tooltip("Radio exterior del anillo (unidades mundo).")]
        public float ringRadius = 0.7f;
        [Tooltip("Grosor del anillo (0.5 = más grueso, 0.85 = más fino).")]
        [Range(0.2f, 0.95f)] public float ringInnerPercent = 0.65f;
        [Tooltip("Altura sobre el terreno (tipo sombra).")]
        public float ringHeightOffset = 0.02f;

        [Header("Outline 3D (alrededor del mesh; en unidades skinned puede verse mal)")]
        [Tooltip("Color del borde al seleccionar (si el outline 3D está activo en la unidad).")]
        public Color selectionColor = new Color(0.15f, 0.85f, 0.35f, 0.98f);
        [Tooltip("Color del borde al hacer hover.")]
        public Color hoverColor = new Color(0.4f, 0.75f, 0.4f, 0.8f);
        [Tooltip("Grosor del borde (escala del mesh). Valores por debajo de 1 = más fino, útil para unidades pequeñas.")]
        [Range(0.5f, 1.25f)] public float outlineScale = 1.12f;
    }

    /// <summary>
    /// Configuración por tipo: unidades (anillo en el suelo), edificios (outline 3D), recursos (outline 3D).
    /// Al tener una unidad seleccionada, el anillo usa el bloque Unidades (ringColor, ringBrightness, etc.).
    /// Asigna este asset en RTS Map Generator → Selection Outline Config (o SelectionOutlineConfigBootstrap).
    /// El tipo se detecta por componente (UnitSelectable, BuildingSelectable, ResourceSelectable), no por layer.
    /// </summary>
    [CreateAssetMenu(menuName = "Project/Selection/Outline Config", fileName = "SelectionOutlineConfig")]
    public class SelectionOutlineConfig : ScriptableObject
    {
        [Header("Unidades (anillo + outline 3D configurable)")]
        [Tooltip("Anillo en el suelo (dona) y opcionalmente outline 3D. Por defecto el outline 3D está desactivado en unidades (skinned).")]
        public UnitSelectionAppearance units = new UnitSelectionAppearance();

        [Header("Unidades enemigas (IA / COM)")]
        [Tooltip("Mismo esquema que Unidades pero para facción hostil al jugador (hover/selección/outline y anillo si aplica).")]
        public UnitSelectionAppearance enemyUnits = new UnitSelectionAppearance
        {
            ringColor = new Color(0.95f, 0.2f, 0.18f, 1f),
            ringBrightness = 5f,
            ringRadius = 0.6f,
            ringInnerPercent = 0.65f,
            ringHeightOffset = 0.08f,
            selectionColor = new Color(0.62f, 0.14f, 0.12f, 0.98f),
            hoverColor = new Color(1f, 0.52f, 0.48f, 0.85f),
            outlineScale = 1.04f
        };

        [Header("Edificios (outline 3D alrededor del mesh)")]
        public OutlineAppearance buildings = new OutlineAppearance();

        [Header("Recursos (outline 3D alrededor del mesh)")]
        public OutlineAppearance resources = new OutlineAppearance();
        
        [Header("Animales recurso móviles (PF_Cow/PF_Cow2/PF_Deer con NavMesh)")]
        [Tooltip("Override específico para animales de recurso en movimiento. Se usa en lugar de Unidades para evitar depender del outlineScale de unidades.")]
        public OutlineAppearance movingFoodResources = new OutlineAppearance
        {
            selectionColor = new Color(0.15f, 0.85f, 0.35f, 0.98f),
            hoverColor = new Color(0.4f, 0.75f, 0.4f, 0.8f),
            outlineScale = 1.06f
        };

        static SelectionOutlineConfig _global;

        /// <summary>Config global; se asigna desde RTSMapGenerator o SelectionOutlineConfigBootstrap.</summary>
        public static SelectionOutlineConfig Global => _global;

        /// <summary>Asigna el config global (llamado por RTSMapGenerator o Bootstrap).</summary>
        public static void SetGlobal(SelectionOutlineConfig config)
        {
            _global = config;
        }
    }
}
