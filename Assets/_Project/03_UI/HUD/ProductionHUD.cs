using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.Gameplay.Buildings;
using Project.Gameplay.Units;
using System.Collections.Generic;

namespace Project.UI
{
    /// <summary>
    /// HUD para entrenar unidades desde edificios productores (Cuartel, Arquería, etc.)
    /// Estilo Age of Empires II
    /// </summary>
    public class ProductionHUD : MonoBehaviour
    {
        [Header("Refs")]
        public Project.Gameplay.Units.RTSSelectionController selection;
        public ProductionCatalog catalog;

        [Header("Panels")]
        public GameObject panelUnits;       // Panel con botones de unidades (1-9)
        public GameObject panelQueue;       // Panel con cola de producción

        [Header("Unit Buttons (1-9)")]
        public Button btnUnit1;
        public Button btnUnit2;
        public Button btnUnit3;
        public Button btnUnit4;
        public Button btnUnit5;
        public Button btnUnit6;
        public Button btnUnit7;
        public Button btnUnit8;
        public Button btnUnit9;

        [Header("Queue Display")]
        public Transform queueContainer;    // Container para los items de la cola
        public GameObject queueItemPrefab;  // Prefab para cada item en cola

        [Header("Text Labels")]
        public TextMeshProUGUI titleText;           // Nombre del edificio
        public TextMeshProUGUI progressText;        // "Entrenando: Milicia (45%)"

        private Button[] _unitButtons;
        private TextMeshProUGUI[] _unitLabels;
        private ProductionBuilding _currentBuilding;
        private bool _lastHasProductionBuilding;

        void Awake()
        {
            if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();
            if (catalog == null)
            {
                // Intentar cargar desde Resources
                catalog = Resources.Load<ProductionCatalog>("ProductionCatalog");
            }

            // Organizar arrays
            _unitButtons = new Button[] { btnUnit1, btnUnit2, btnUnit3, btnUnit4, btnUnit5, btnUnit6, btnUnit7, btnUnit8, btnUnit9 };
            _unitLabels = new TextMeshProUGUI[_unitButtons.Length];

            // Obtener labels
            for (int i = 0; i < _unitButtons.Length; i++)
            {
                if (_unitButtons[i] != null)
                    _unitLabels[i] = _unitButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            }

            // Hook click listeners
            for (int i = 0; i < _unitButtons.Length; i++)
            {
                int slot = i + 1; // 1-9
                if (_unitButtons[i] != null)
                    _unitButtons[i].onClick.AddListener(() => OnUnitButtonClick(slot));
            }

            HideAllPanels();
        }

        void Start()
        {
            HideAllPanels();
        }

        void OnEnable()
        {
            // Suscribirse a eventos de edificios cuando cambien
            if (selection != null)
            {
                // Nota: RTSSelectionController no tiene evento de selección cambiada aún
                // Por ahora usaremos Update() para detectar cambios
            }
        }

        void Update()
        {
            // Detectar si hay un edificio productor seleccionado
            ProductionBuilding building = GetSelectedProductionBuilding();
            bool hasProductionBuilding = building != null;

            // Si cambió la selección
            if (hasProductionBuilding != _lastHasProductionBuilding || building != _currentBuilding)
            {
                _lastHasProductionBuilding = hasProductionBuilding;
                _currentBuilding = building;

                if (!hasProductionBuilding)
                {
                    HideAllPanels();
                    return;
                }
                else
                {
                    // Mostrar panel de producción
                    ShowProductionPanel();
                }
            }

            // Si no hay edificio, ocultar todo
            if (!hasProductionBuilding)
            {
                HideAllPanels();
                return;
            }

            // Refrescar UI continuamente si hay edificio seleccionado
            RefreshUI();
        }

        ProductionBuilding GetSelectedProductionBuilding()
        {
            if (selection == null)
                return null;

            var selectedUnits = selection.GetSelected();
            if (selectedUnits == null || selectedUnits.Count == 0)
                return null;

            // Revisar si alguna selección tiene ProductionBuilding
            foreach (var selectable in selectedUnits)
            {
                if (selectable == null) continue;
                var building = selectable.GetComponent<ProductionBuilding>();
                if (building != null)
                    return building;
            }

            return null;
        }

        void ShowProductionPanel()
        {
            if (panelUnits != null)
                panelUnits.SetActive(true);

            if (panelQueue != null)
                panelQueue.SetActive(true);

            RefreshUnitButtons();
            RefreshQueueDisplay();
        }

        void HideAllPanels()
        {
            if (panelUnits != null)
                panelUnits.SetActive(false);

            if (panelQueue != null)
                panelQueue.SetActive(false);

            UpdateTitle("");
            UpdateProgress("");
        }

        void RefreshUI()
        {
            if (_currentBuilding == null)
            {
                HideAllPanels();
                return;
            }

            RefreshUnitButtons();
            RefreshQueueDisplay();
            RefreshProgress();
        }

        void RefreshUnitButtons()
        {
            if (_currentBuilding == null || catalog == null)
            {
                // Ocultar todos los botones
                for (int i = 0; i < _unitButtons.Length; i++)
                {
                    if (_unitButtons[i] != null)
                    {
                        _unitButtons[i].gameObject.SetActive(true);
                        _unitButtons[i].interactable = false;
                        if (_unitLabels[i] != null)
                            _unitLabels[i].text = $"{i + 1}.";
                    }
                }
                return;
            }

            // Obtener el buildingId desde el edificio
            string buildingId = GetBuildingId(_currentBuilding);
            UpdateTitle(buildingId);

            // Refrescar cada botón según el catálogo
            for (int slot = 1; slot <= 9; slot++)
            {
                int arrayIndex = slot - 1;
                UnitSO unit = catalog.Get(buildingId, slot);

                if (_unitButtons[arrayIndex] != null)
                {
                    bool hasUnit = unit != null;
                    _unitButtons[arrayIndex].gameObject.SetActive(true);
                    _unitButtons[arrayIndex].interactable = hasUnit && CanAfford(unit);

                    if (_unitLabels[arrayIndex] != null)
                    {
                        if (hasUnit)
                            _unitLabels[arrayIndex].text = $"{slot}. {unit.displayName}";
                        else
                            _unitLabels[arrayIndex].text = $"{slot}.";
                    }
                }
            }
        }

        void RefreshQueueDisplay()
        {
            if (queueContainer == null || _currentBuilding == null)
                return;

            // Limpiar items anteriores
            foreach (Transform child in queueContainer)
                Destroy(child.gameObject);

            // Mostrar unidades en cola
            List<UnitSO> queuedUnits = _currentBuilding.queue.GetAllUnits();
            for (int i = 0; i < queuedUnits.Count; i++)
            {
                UnitSO unit = queuedUnits[i];
                if (unit == null) continue;

                // Si hay prefab para item de cola, usarlo
                if (queueItemPrefab != null)
                {
                    GameObject itemObj = Instantiate(queueItemPrefab, queueContainer);
                    var tmp = itemObj.GetComponentInChildren<TextMeshProUGUI>();
                    if (tmp != null)
                        tmp.text = unit.displayName;

                    // Agregar botón de cancelar
                    var btn = itemObj.GetComponentInChildren<Button>();
                    if (btn != null)
                    {
                        int index = i;
                        btn.onClick.AddListener(() => OnCancelUnit(index));
                    }
                }
                else
                {
                    // Crear item simple de texto
                    GameObject itemObj = new GameObject($"QueueItem_{i}");
                    itemObj.transform.SetParent(queueContainer);
                    var tmp = itemObj.AddComponent<TextMeshProUGUI>();
                    tmp.text = $"{i + 1}. {unit.displayName}";
                    tmp.fontSize = 14;
                }
            }
        }

        void RefreshProgress()
        {
            if (_currentBuilding == null || !_currentBuilding.queue.IsProducing)
            {
                UpdateProgress("");
                return;
            }

            UnitSO currentUnit = _currentBuilding.queue.CurrentUnit;
            float progress = _currentBuilding.queue.CurrentProgress;
            int progressPercent = Mathf.RoundToInt(progress * 100f);

            UpdateProgress($"Entrenando: {currentUnit.displayName} ({progressPercent}%)");
        }

        void OnUnitButtonClick(int slot)
        {
            if (_currentBuilding == null || catalog == null) return;

            string buildingId = GetBuildingId(_currentBuilding);
            UnitSO unit = catalog.Get(buildingId, slot);

            if (unit != null)
                _currentBuilding.TryQueueUnit(unit);
        }

        void OnCancelUnit(int index)
        {
            if (_currentBuilding == null) return;
            _currentBuilding.CancelUnit(index);
        }

        bool CanAfford(UnitSO unit)
        {
            if (_currentBuilding == null || _currentBuilding.owner == null)
                return true;

            if (unit.costs == null || unit.costs.Length == 0)
                return true;

            foreach (var cost in unit.costs)
            {
                if (_currentBuilding.owner.Get(cost.kind) < cost.amount)
                    return false;
            }

            return true;
        }

        string GetBuildingId(ProductionBuilding building)
        {
            // Intentar obtener el ID desde BuildingInstance si existe
            var buildingInstance = building.GetComponent<BuildingInstance>();
            if (buildingInstance != null && buildingInstance.buildingSO != null)
                return buildingInstance.buildingSO.id;

            // Fallback: usar nombre del GameObject
            return building.gameObject.name.Replace("(Clone)", "").Trim();
        }

        void UpdateTitle(string text)
        {
            if (titleText != null)
                titleText.text = text;
        }

        void UpdateProgress(string text)
        {
            if (progressText != null)
                progressText.text = text;
        }

        /// <summary>
        /// Método público para entrenar unidad desde hotkey
        /// </summary>
        public void TrainUnit(int slot)
        {
            OnUnitButtonClick(slot);
        }
    }
}
