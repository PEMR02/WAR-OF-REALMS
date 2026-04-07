using UnityEngine;
using Project.Gameplay;
using Project.Gameplay.Faction;

namespace Project.Gameplay.Units
{
    public class UnitSelectable : MonoBehaviour
    {
        [Header("Visual - Selección")]
        [Tooltip("Renderers a iluminar al seleccionar. Vacío = todos los hijos.")]
        public Renderer[] renderers;
        [Tooltip("Brillo adicional al seleccionar (blanco).")]
        [Range(0f, 0.5f)] public float highlightIntensity = 0.2f;
        [Tooltip("Tinte de color al seleccionar (ej. cyan para que se note el outline).")]
        public Color selectionTint = new Color(0.15f, 0.35f, 0.5f, 0f);
        [Tooltip("Tinte al seleccionar unidades enemigas (rojo oscuro).")]
        public Color enemySelectionTint = new Color(0.38f, 0.06f, 0.05f, 0f);

        private Color[] _baseColors;
        private SelectableOutline _outline;
        private UnitSelectionRing _selectionRing;

        bool _cachedIsVillagerValid;
        bool _cachedIsVillager;

        /// <summary>True si la unidad tiene VillagerGatherer o Builder (aldeano). Cacheado para evitar GetComponent repetidos en selección.</summary>
        public bool IsVillager
        {
            get
            {
                if (!_cachedIsVillagerValid)
                {
                    _cachedIsVillagerValid = true;
                    _cachedIsVillager = GetComponent<VillagerGatherer>() != null || GetComponent<Builder>() != null;
                }
                return _cachedIsVillager;
            }
        }

        void OnEnable()
        {
            UnitSelectableRegistry.Register(this);
        }

        void OnDisable()
        {
            UnitSelectableRegistry.Unregister(this);
        }

        void Awake()
        {
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>();

            _baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].material != null)
                    _baseColors[i] = renderers[i].material.color;
            }

            _outline = GetComponent<SelectableOutline>();
            if (_outline == null) _outline = gameObject.AddComponent<SelectableOutline>();
            _selectionRing = GetComponent<UnitSelectionRing>();
            if (_selectionRing == null) _selectionRing = gameObject.AddComponent<UnitSelectionRing>();
        }

        public void SetSelected(bool selected)
        {
            bool hostile = FactionMember.IsHostileToPlayer(gameObject);
            Color tint = hostile ? enemySelectionTint : selectionTint;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || r.material == null) continue;

                var baseCol = _baseColors[i];
                if (selected)
                {
                    Color c = baseCol + new Color(highlightIntensity, highlightIntensity, highlightIntensity, 0f) + tint;
                    r.material.color = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), baseCol.a);
                }
                else
                    r.material.color = baseCol;
            }

            if (_selectionRing == null) _selectionRing = GetComponent<UnitSelectionRing>();
            if (_selectionRing == null) _selectionRing = gameObject.AddComponent<UnitSelectionRing>();
            _selectionRing.SetSelected(selected);

            if (_outline != null) _outline.SetSelectionOutline(selected);
        }

        /// <summary>Hover del cursor sobre la unidad (outline más suave).</summary>
        public void SetHovered(bool hovered)
        {
            if (_outline != null) _outline.SetHoverOutline(hovered);
        }
    }
}
