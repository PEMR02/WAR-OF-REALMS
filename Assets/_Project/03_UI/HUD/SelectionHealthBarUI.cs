using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Project.Gameplay.Units;
using Project.Gameplay.Buildings;
using Project.Gameplay.Resources;
using Project.Gameplay.Combat;

namespace Project.UI
{
    /// <summary>
    /// Un solo panel (SelectionInfo_General) para toda la selección: nombre, barra (vida o recurso restante) y texto "actual / max".
    /// Unidades y edificios: nombre + barra de vida + "HP: actual / max". Recursos: tipo + barra de cantidad + "cantidad / máximo".
    /// </summary>
    public class SelectionHealthBarUI : MonoBehaviour
    {
        [Header("Refs")]
        public RTSSelectionController selection;

        [Header("Panel único")]
        public GameObject rootPanel;
        [Tooltip("Opcional: imagen de fondo del panel (marco). Si se asigna, se pondrá en frameColor (ej. negro).")]
        public Image frameImage;
        [Tooltip("Color del marco/fondo del panel (ej. negro). Solo se usa si frameImage está asignado.")]
        public Color frameColor = Color.black;
        public Image fillImage;
        public Image backgroundImage;
        [Tooltip("Nombre (unidad/edificio) o tipo de recurso (Madera, Piedra...).")]
        public TextMeshProUGUI titleTextTMP;
        public Text titleTextLegacy;
        [Tooltip("Ej. 80 / 100 (vida) o 240 / 300 (recurso).")]
        public TextMeshProUGUI hpTextTMP;
        public Text hpTextLegacy;

        [Header("Colores")]
        public Color colorFullHealth = new Color(0.2f, 0.85f, 0.2f);
        public Color colorNoHealth = new Color(0.9f, 0.15f, 0.15f);
        [Tooltip("Si true, la barra de recursos usa los mismos colores que la de vida (verde/rojo). Si false, usa colores por tipo (madera, oro, etc.).")]
        public bool useLifeColorsForResources = true;

        [Header("Performance")]
        [Tooltip("Intervalo entre actualizaciones de la barra (más alto = menos CPU).")]
        public float pollInterval = 0.12f;

        private static readonly string[] ResourceKindNames = { "Madera", "Piedra", "Oro", "Comida" };

        private float _pollTimer;
        void Awake()
        {
            if (selection == null)
                selection = FindFirstObjectByType<RTSSelectionController>();

            if (fillImage != null && fillImage.type != Image.Type.Filled)
            {
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Horizontal;
                fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            }

            if (titleTextTMP == null && titleTextLegacy == null)
                titleTextTMP = GetComponentInChildren<TextMeshProUGUI>(true);
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
            RefreshFromSelection();
        }

        void Start()
        {
            if (rootPanel != null) rootPanel.SetActive(false);
        }

        void Update()
        {
            // Polling periódico: actualiza HP/cantidad y recupera selección si se perdió el evento (ej. UI habilitada tarde)
            _pollTimer -= Time.deltaTime;
            if (_pollTimer <= 0f)
            {
                _pollTimer = pollInterval;
                RefreshFromSelection();
            }
        }

        void RefreshFromSelection()
        {
            if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();
            if (selection == null)
            {
                if (rootPanel != null && rootPanel.activeSelf) rootPanel.SetActive(false);
                return;
            }

            // 1) Recurso seleccionado
            ResourceNode resource = selection.GetSelectedResourceNode();
            if (resource != null && !resource.IsDepleted)
            {
                ShowPanel();
                float ratio = resource.GetBarRatio01();
                if (fillImage != null)
                {
                    EnsureFillType(fillImage);
                    fillImage.fillAmount = ratio;
                    fillImage.color = useLifeColorsForResources ? colorFullHealth : resource.GetBarFullColor();
                }
                if (backgroundImage != null)
                    backgroundImage.color = useLifeColorsForResources ? colorNoHealth : resource.GetBarEmptyColor();
                SetText(titleTextTMP, titleTextLegacy, ResourceKindNames[(int)resource.kind]);
                SetText(hpTextTMP, hpTextLegacy, $"{resource.amount} / {resource.MaxAmount}");
                return;
            }

            // 2) Unidad o edificio (vida): barra que baja (verde se acorta, fondo rojo visible)
            Health health = GetSelectedHealth();
            if (health == null)
            {
                if (rootPanel != null && rootPanel.activeSelf) rootPanel.SetActive(false);
                return;
            }

            ShowPanel();
            float healthRatio = Mathf.Clamp01(health.CurrentHP / (float)Mathf.Max(1, health.MaxHP));
            if (fillImage != null)
            {
                EnsureFillType(fillImage);
                fillImage.fillAmount = healthRatio;
                fillImage.color = colorFullHealth;
            }
            if (backgroundImage != null)
                backgroundImage.color = colorNoHealth;
            SetText(titleTextTMP, titleTextLegacy, GetUnitOrBuildingName());
            SetText(hpTextTMP, hpTextLegacy, $"{health.CurrentHP} / {health.MaxHP}");
        }

        void ShowPanel()
        {
            if (rootPanel != null && !rootPanel.activeSelf)
                rootPanel.SetActive(true);
            if (frameImage != null)
                frameImage.color = frameColor;
        }

        /// <summary>Si la barra no está en Filled, fillAmount no hace nada y se ve siempre llena. Forzamos Filled cada vez.</summary>
        static void EnsureFillType(Image img)
        {
            if (img == null) return;
            if (img.type != Image.Type.Filled)
            {
                img.type = Image.Type.Filled;
                img.fillMethod = Image.FillMethod.Horizontal;
                img.fillOrigin = (int)Image.OriginHorizontal.Left;
            }
        }

        static void SetText(TextMeshProUGUI tmp, Text leg, string value)
        {
            if (tmp != null) tmp.text = value;
            if (leg != null) leg.text = value;
        }

        string GetUnitOrBuildingName()
        {
            if (selection == null) return "";

            BuildingSelectable building = selection.GetSelectedBuilding();
            if (building != null)
            {
                var inst = building.GetComponent<BuildingInstance>();
                if (inst != null && inst.buildingSO != null)
                    return inst.buildingSO.id;
                return building.gameObject.name;
            }

            var units = selection.GetSelected();
            if (units != null && units.Count > 0 && units[0] != null)
            {
                string raw = units[0].gameObject.name;
                int idx = raw.IndexOf('(');
                return idx > 0 ? raw.Substring(0, idx).Trim() : raw.Replace("(Clone)", "").Trim();
            }

            return "";
        }

        Health GetSelectedHealth()
        {
            if (selection == null) return null;

            BuildingSelectable building = selection.GetSelectedBuilding();
            if (building != null)
            {
                var health = building.GetComponent<Health>();
                if (health == null) health = building.GetComponentInChildren<Health>(true);
                return health; // mostrar barra aunque esté muerto (0%)
            }

            var units = selection.GetSelected();
            if (units != null && units.Count > 0)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    if (units[i] == null) continue;
                    var health = units[i].GetComponent<Health>();
                    if (health == null) health = units[i].GetComponentInParent<Health>();
                    if (health == null) health = units[i].GetComponentInChildren<Health>(true);
                    if (health != null) return health; // mostrar barra aunque esté muerto (0%)
                }
            }

            return null;
        }
    }
}
