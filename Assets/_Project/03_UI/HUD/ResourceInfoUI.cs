using UnityEngine;
using Project.Gameplay.Units;
using Project.Gameplay.Resources;

namespace Project.UI
{
    /// <summary>
    /// Muestra información del recurso seleccionado: tipo y cantidad restante (ej. "Madera: 240 / 300").
    /// Asigna el panel raíz y el texto (TextMeshProUGUI o UI.Text); se muestra solo cuando hay un recurso seleccionado.
    /// </summary>
    public class ResourceInfoUI : MonoBehaviour
    {
        [Header("Refs")]
        public RTSSelectionController selection;

        [Header("UI")]
        [Tooltip("Panel raíz que se muestra solo cuando hay un recurso seleccionado.")]
        public GameObject rootPanel;
        [Tooltip("Texto que muestra 'Tipo: cantidad / máximo'. Si usas TextMeshPro, asígnalo aquí.")]
        public TMPro.TextMeshProUGUI textTMP;
        [Tooltip("Alternativa a TextMeshPro: UnityEngine.UI.Text.")]
        public UnityEngine.UI.Text textLegacy;

        [Header("Performance")]
        public float pollInterval = 0.05f;

        private static readonly string[] KindNames = { "Madera", "Piedra", "Oro", "Comida" };

        private float _pollTimer;
        private bool _hasResourceSelected;

        void Awake()
        {
            if (selection == null)
                selection = FindFirstObjectByType<RTSSelectionController>();
            if (textTMP == null && textLegacy == null)
            {
                textTMP = GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (textTMP == null)
                    textLegacy = GetComponentInChildren<UnityEngine.UI.Text>(true);
            }
        }

        void OnEnable()
        {
            if (selection != null)
                selection.OnSelectionChanged += OnSelectionChanged;
        }

        void OnDisable()
        {
            if (selection != null)
                selection.OnSelectionChanged -= OnSelectionChanged;
        }

        void OnSelectionChanged()
        {
            _hasResourceSelected = selection != null && selection.GetSelectedResourceNode() != null;
            Refresh();
        }

        void Start()
        {
            if (rootPanel != null) rootPanel.SetActive(false);
        }

        void Update()
        {
            // Solo hacer polling mientras haya un recurso seleccionado (actualiza cantidad en tiempo real)
            if (!_hasResourceSelected) return;

            _pollTimer -= Time.deltaTime;
            if (_pollTimer <= 0f)
            {
                _pollTimer = pollInterval;
                Refresh();
            }
        }

        void Refresh()
        {
            ResourceNode node = selection != null ? selection.GetSelectedResourceNode() : null;

            if (node == null || node.IsDepleted)
            {
                _hasResourceSelected = false;
                if (rootPanel != null && rootPanel.activeSelf)
                    rootPanel.SetActive(false);
                return;
            }

            if (rootPanel != null && !rootPanel.activeSelf)
                rootPanel.SetActive(true);

            string kindName = KindNames[(int)node.kind];
            string line = $"{kindName}: {node.amount} / {node.MaxAmount}";

            if (textTMP != null)
                textTMP.text = line;
            if (textLegacy != null)
                textLegacy.text = line;
        }
    }
}
