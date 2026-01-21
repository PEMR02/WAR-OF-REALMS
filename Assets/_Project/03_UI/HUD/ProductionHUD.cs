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
        public Slider progressBar;                  // Barra de progreso visual

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

            // DEBUG temporal
            if (hasProductionBuilding && building != _currentBuilding)
                Debug.Log($"[ProductionHUD] Edificio productor detectado: {building.gameObject.name}");

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

            // Verificar si hay un edificio seleccionado
            var selectedBuilding = selection.GetSelectedBuilding();
            if (selectedBuilding != null)
            {
                var productionBuilding = selectedBuilding.GetComponent<ProductionBuilding>();
                if (productionBuilding != null)
                    return productionBuilding;
            }

            // Fallback: revisar unidades seleccionadas (por si alguna unidad tiene ProductionBuilding)
            var selectedUnits = selection.GetSelected();
            if (selectedUnits != null && selectedUnits.Count > 0)
            {
                foreach (var selectable in selectedUnits)
                {
                    if (selectable == null) continue;
                    var building = selectable.GetComponent<ProductionBuilding>();
                    if (building != null)
                        return building;
                }
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
                        {
                            string costString = GetCostString(unit);
                            _unitLabels[arrayIndex].text = $"{slot}. {unit.displayName}\n{costString}";
                        }
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
                    // Crear item simple con botón de cancelar
                    GameObject itemObj = new GameObject($"QueueItem_{i}");
                    itemObj.transform.SetParent(queueContainer);
                    itemObj.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 25;
                    
                    var tmp = itemObj.AddComponent<TextMeshProUGUI>();
                    float progress = (i == 0) ? _currentBuilding.queue.CurrentProgress * 100f : 0f;
                    string progressStr = (i == 0) ? $" ({Mathf.RoundToInt(progress)}%)" : "";
                    tmp.text = $"{i + 1}. {unit.displayName}{progressStr}";
                    tmp.fontSize = 12;
                    tmp.color = (i == 0) ? Color.yellow : Color.white;
                    
                    // Agregar evento de click derecho
                    var eventTrigger = itemObj.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                    var pointerClick = new UnityEngine.EventSystems.EventTrigger.Entry
                    {
                        eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick
                    };
                    int index = i;
                    pointerClick.callback.AddListener((data) =>
                    {
                        var pointerData = (UnityEngine.EventSystems.PointerEventData)data;
                        if (pointerData.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right)
                        {
                            OnCancelUnit(index);
                        }
                    });
                    eventTrigger.triggers.Add(pointerClick);
                }
            }
        }

        void RefreshProgress()
        {
            if (_currentBuilding == null || !_currentBuilding.queue.IsProducing)
            {
                UpdateProgress("");
                UpdateProgressBar(0f);
                return;
            }

            UnitSO currentUnit = _currentBuilding.queue.CurrentUnit;
            float progress = _currentBuilding.queue.CurrentProgress;
            int progressPercent = Mathf.RoundToInt(progress * 100f);

            UpdateProgress($"Entrenando: {currentUnit.displayName} ({progressPercent}%)");
            UpdateProgressBar(progress);
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

        string GetCostString(UnitSO unit)
        {
            if (unit == null || unit.costs == null || unit.costs.Length == 0)
                return "Gratis";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < unit.costs.Length; i++)
            {
                var cost = unit.costs[i];
                string resourceName = GetResourceShortName(cost.kind);
                sb.Append($"{resourceName}:{cost.amount}");
                
                if (i < unit.costs.Length - 1)
                    sb.Append(" | ");
            }
            return sb.ToString();
        }

        string GetResourceShortName(Project.Gameplay.Resources.ResourceKind kind)
        {
            return kind switch
            {
                Project.Gameplay.Resources.ResourceKind.Wood => "🪵",
                Project.Gameplay.Resources.ResourceKind.Stone => "🪨",
                Project.Gameplay.Resources.ResourceKind.Gold => "🪙",
                Project.Gameplay.Resources.ResourceKind.Food => "🍖",
                _ => kind.ToString()
            };
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

        void UpdateProgressBar(float value)
        {
            if (progressBar != null)
            {
                progressBar.value = value;
                progressBar.gameObject.SetActive(value > 0f); // Ocultar si no hay progreso
            }
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
