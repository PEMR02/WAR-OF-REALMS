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
        public GameObject titleHeader;      // Header con nombre del edificio (hijo directo de HUD_Production)

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

        [Header("Build Site (solar en construcción)")]
        [Tooltip("Botón para cancelar la construcción cuando está seleccionado un solar. Opcional.")]
        public Button cancelConstructionButton;

        [Header("Raycast Blockers")]
        public bool autoDisableNamedRaycastBlockers = true;
        public string[] namedRaycastBlockers = new[] { "Text_Title" };
        public Graphic[] extraRaycastBlockers;

        [Header("Text Labels")]
        public TextMeshProUGUI titleText;           // Nombre del edificio
        public TextMeshProUGUI progressText;        // "Entrenando: Milicia (45%)"
        public Slider progressBar;                  // Barra de progreso visual

        private Button[] _unitButtons;
        private TextMeshProUGUI[] _unitLabels;
        private ProductionBuilding _currentBuilding;
        private BuildSite _currentBuildSite;
        private bool _lastHasProductionBuilding;
        private bool _lastHasBuildSite;
        private TextMeshProUGUI _firstQueueItemText;
        private readonly System.Collections.Generic.Dictionary<string, Graphic> _cachedNamedBlockers = new System.Collections.Generic.Dictionary<string, Graphic>();
        [Header("Performance")]
        [Tooltip("Frecuencia de chequeo de selección (segundos). Evita hacer polling pesado cada frame.")]
        public float pollSelectionEvery = 0.10f;
        private float _pollTimer;

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
            if (selection != null)
                selection.OnSelectionChanged += OnSelectionChangedHandler;
        }

        void OnDisable()
        {
            if (selection != null)
                selection.OnSelectionChanged -= OnSelectionChangedHandler;
        }

        void OnSelectionChangedHandler()
        {
            // Fuerza re-evaluación inmediata en el próximo Update (resetea el timer)
            _pollTimer = 0f;
        }

        void Update()
        {
            _pollTimer -= Time.deltaTime;

            bool pollNow = _pollTimer <= 0f;
            if (pollNow) _pollTimer = Mathf.Max(0.02f, pollSelectionEvery);

            ProductionBuilding building = pollNow ? GetSelectedProductionBuilding() : _currentBuilding;
            BuildSite buildSite = pollNow ? GetSelectedBuildSite() : _currentBuildSite;

            bool hasProductionBuilding = building != null;
            bool hasBuildSite = buildSite != null;
            bool hasAny = hasProductionBuilding || hasBuildSite;

            // Detección de cambio de selección
            bool buildingChanged = (building != _currentBuilding) || (buildSite != _currentBuildSite);
            bool modeChanged = (hasProductionBuilding != _lastHasProductionBuilding) || (hasBuildSite != _lastHasBuildSite);

            if (modeChanged || buildingChanged)
            {
                _lastHasProductionBuilding = hasProductionBuilding;
                _lastHasBuildSite = hasBuildSite;

                if (_currentBuilding != null)
                {
                    _currentBuilding.OnUnitQueued -= OnUnitQueued;
                    _currentBuilding.OnUnitCompleted -= OnUnitCompleted;
                    _currentBuilding.OnQueueChanged -= OnQueueChanged;
                }

                _currentBuilding = building;
                _currentBuildSite = buildSite;

                if (!hasAny)
                {
                    HideAllPanels();
                    return;
                }

                if (_currentBuilding != null)
                {
                    _currentBuilding.OnUnitQueued += OnUnitQueued;
                    _currentBuilding.OnUnitCompleted += OnUnitCompleted;
                    _currentBuilding.OnQueueChanged += OnQueueChanged;
                }

                if (hasBuildSite && !hasProductionBuilding)
                    ShowBuildSitePanel();
                else
                    ShowProductionPanel();
            }

            if (!hasAny)
            {
                HideAllPanels();
                return;
            }

            if (hasProductionBuilding && _currentBuilding != null)
                RefreshProgress();
            else if (hasBuildSite && _currentBuildSite != null)
                RefreshBuildSitePanel();
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

        BuildSite GetSelectedBuildSite()
        {
            if (selection == null) return null;
            var selectedBuilding = selection.GetSelectedBuilding();
            if (selectedBuilding == null) return null;
            return selectedBuilding.GetComponent<BuildSite>();
        }

        void ShowProductionPanel()
        {
            if (titleHeader != null) titleHeader.SetActive(true);
            if (panelUnits != null) panelUnits.SetActive(true);
            if (panelQueue != null) panelQueue.SetActive(true);
            if (cancelConstructionButton != null) cancelConstructionButton.gameObject.SetActive(false);

            RefreshUnitButtons();
            RefreshQueueDisplay();
        }

        void ShowBuildSitePanel()
        {
            if (titleHeader != null) titleHeader.SetActive(true);
            if (panelUnits != null) panelUnits.SetActive(false);
            if (panelQueue != null)
            {
                panelQueue.SetActive(true);
                if (queueContainer != null)
                {
                    foreach (Transform child in queueContainer)
                    {
                        if (child != null) Destroy(child.gameObject);
                    }
                }
            }
            if (cancelConstructionButton != null)
            {
                cancelConstructionButton.gameObject.SetActive(true);
                cancelConstructionButton.onClick.RemoveAllListeners();
                cancelConstructionButton.onClick.AddListener(OnCancelConstructionClick);
            }
            RefreshBuildSitePanel();
        }

        void RefreshBuildSitePanel()
        {
            if (_currentBuildSite == null) return;
            string name = _currentBuildSite.buildingSO != null ? _currentBuildSite.buildingSO.id : "Edificio";
            UpdateTitle($"En construcción: {name}");
            UpdateProgress($"Progreso: {Mathf.RoundToInt(_currentBuildSite.progress01 * 100)}%");
        }

        void OnCancelConstructionClick()
        {
            if (_currentBuildSite == null) return;
            _currentBuildSite.CancelConstruction();
            _currentBuildSite = null;
            _lastHasBuildSite = false;
            if (selection != null) selection.ClearSelection();
            HideAllPanels();
        }

        void HideAllPanels()
        {
            if (titleHeader != null) titleHeader.SetActive(false);
            if (panelUnits != null) panelUnits.SetActive(false);
            if (panelQueue != null) panelQueue.SetActive(false);
            if (cancelConstructionButton != null) cancelConstructionButton.gameObject.SetActive(false);

            _firstQueueItemText = null;
            _currentBuildSite = null;
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
                    _unitButtons[arrayIndex].interactable = hasUnit && CanAfford(unit) && HasPopulationSpace(unit);

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

                    // Tooltip
                    var trigger = _unitButtons[arrayIndex].GetComponent<TooltipTrigger>();
                    if (trigger == null)
                        trigger = _unitButtons[arrayIndex].gameObject.AddComponent<TooltipTrigger>();
                    trigger.content = hasUnit ? BuildTooltip(unit) : "";
                }
            }
        }

        void RefreshQueueDisplay()
        {
            if (queueContainer == null || _currentBuilding == null)
                return;

            // Evitar que textos/imagenes del Panel_Queue bloqueen raycasts de la cola
            DisablePanelQueueRaycastBlockers();
            DisableExternalRaycastBlockers();

            // Limpiar items anteriores
            foreach (Transform child in queueContainer)
            {
                if (child != null)
                    Destroy(child.gameObject);
            }

            // Mostrar unidades en cola
            List<UnitSO> queuedUnits = _currentBuilding.queue.GetAllUnits();
            
            // Configurar el padre (Panel_Queue) para que no limite el tamaño
            if (queueContainer.parent != null)
            {
                var parentVlg = queueContainer.parent.GetComponent<VerticalLayoutGroup>();
                if (parentVlg != null)
                {
                    parentVlg.childControlWidth = true;
                    parentVlg.childControlHeight = false; // ← IMPORTANTE: NO controlar altura
                    parentVlg.childForceExpandWidth = true;
                    parentVlg.childForceExpandHeight = false;
                }
            }
            
            // Asegurar que el contenedor tenga layout adecuado
            var vlg = queueContainer.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.childControlWidth = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
                vlg.spacing = 3;
                vlg.padding = new RectOffset(4, 4, 4, 4);
            }
            
            // ⚠️ ContentSizeFitter no funciona porque el padre controla el tamaño
            // Solución: Calcular y asignar el tamaño manualmente
            
            // Remover ContentSizeFitter si existe (no funciona)
            var csf = queueContainer.GetComponent<ContentSizeFitter>();
            if (csf != null)
                Destroy(csf);
            
            _firstQueueItemText = null;

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

                    // Indicador "en construcción": primer ítem con fondo destacado
                    bool isCurrent = (i == 0);
                    var itemBg = itemObj.GetComponent<UnityEngine.UI.Image>();
                    if (itemBg != null)
                        itemBg.color = isCurrent ? new Color(0.25f, 0.35f, 0.2f, 0.95f) : new Color(0.15f, 0.18f, 0.25f, 0.9f);

                    // ✅ Click izquierdo o derecho para cancelar (prefab)
                    int cancelIndex = i;
                    
                    // Forzar raycasts aunque haya CanvasGroup padres bloqueando
                    var itemGroup = itemObj.GetComponent<CanvasGroup>();
                    if (itemGroup == null)
                        itemGroup = itemObj.AddComponent<CanvasGroup>();
                    itemGroup.interactable = true;
                    itemGroup.blocksRaycasts = true;
                    itemGroup.ignoreParentGroups = true;

                    // Desactivar raycast en TODOS los gráficos hijos (evita que "se coma" el click)
                    var graphics = itemObj.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
                    for (int g = 0; g < graphics.Length; g++)
                        graphics[g].raycastTarget = false;

                    // Usar el root como target de raycast
                    var imgForClick = itemObj.GetComponent<UnityEngine.UI.Image>();
                    if (imgForClick != null)
                    {
                        imgForClick.raycastTarget = true;
                    }
                    else
                    {
                        // Crear un Image invisible para recibir raycasts
                        var img = itemObj.AddComponent<UnityEngine.UI.Image>();
                        img.color = new Color(0f, 0f, 0f, 0f);
                        img.raycastTarget = true;
                    }

                    // Limpiar listener del botón si existe (evita doble trigger)
                    var btn = itemObj.GetComponentInChildren<Button>();
                    if (btn != null)
                        btn.onClick.RemoveAllListeners();

                    // Handler SIEMPRE en el root (itemObj): es quien recibe el raycast y el click
                    var clickHandler = itemObj.GetComponent<QueueItemClickHandler>();
                    if (clickHandler == null)
                        clickHandler = itemObj.AddComponent<QueueItemClickHandler>();
                    clickHandler.Setup(cancelIndex, this);
                }
                else
                {
                    GameObject itemObj = new GameObject($"QueueItem_{i}");
                    itemObj.AddComponent<RectTransform>();
                    itemObj.transform.SetParent(queueContainer, false);

                    var layoutElem = itemObj.AddComponent<UnityEngine.UI.LayoutElement>();
                    layoutElem.minHeight = 22;
                    layoutElem.preferredHeight = 22;
                    layoutElem.flexibleHeight = 0;

                    var img = itemObj.AddComponent<UnityEngine.UI.Image>();
                    img.color = new Color(0.15f, 0.18f, 0.25f, 0.9f);
                    img.raycastTarget = true;

                    GameObject textObj = new GameObject("Text");
                    var textRect = textObj.AddComponent<RectTransform>();
                    textObj.transform.SetParent(itemObj.transform, false);
                    textRect.anchorMin = Vector2.zero;
                    textRect.anchorMax = Vector2.one;
                    textRect.sizeDelta = Vector2.zero;
                    textRect.offsetMin = new Vector2(6, 0);
                    textRect.offsetMax = new Vector2(-6, 0);

                    var tmp = textObj.AddComponent<TextMeshProUGUI>();
                    float progress = (i == 0) ? _currentBuilding.queue.CurrentProgress * 100f : 0f;
                    string progressStr = (i == 0) ? $" ({Mathf.RoundToInt(progress)}%)" : "";
                    tmp.text = $"{i + 1}. {unit.displayName}{progressStr}";
                    tmp.fontSize = 13;
                    tmp.color = (i == 0) ? new Color(1f, 0.85f, 0.2f) : Color.white;
                    tmp.alignment = TextAlignmentOptions.Left;
                    tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
                    tmp.raycastTarget = false;

                    // Guardar ref al primer ítem para actualizar % en vivo
                    if (i == 0) _firstQueueItemText = tmp;

                    int index = i;
                    var clickHandler = itemObj.AddComponent<QueueItemClickHandler>();
                    clickHandler.Setup(index, this);
                }
            }
            
            // ✅ FORZAR TAMAÑO MANUAL del contenedor
            if (queueContainer.childCount > 0)
            {
                var containerRect = queueContainer.GetComponent<RectTransform>();
                
                // Calcular altura necesaria: (items * altura) + (espacios * spacing) + padding
                float itemHeight = 24f;
                float spacing = vlg != null ? vlg.spacing : 3f;
                float padding = vlg != null ? (vlg.padding.top + vlg.padding.bottom) : 8f;
                float totalHeight = (queueContainer.childCount * itemHeight) + 
                                  ((queueContainer.childCount - 1) * spacing) + 
                                  padding;
                
                // Asignar el tamaño directamente
                containerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalHeight);
                
                // Forzar recálculo
                Canvas.ForceUpdateCanvases();
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
            }
        }

        void DisablePanelQueueRaycastBlockers()
        {
            if (panelQueue == null || queueContainer == null) return;

            var graphics = panelQueue.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                var g = graphics[i];
                if (g == null) continue;

                // Solo permitir raycasts en elementos de la cola
                if (g.transform.IsChildOf(queueContainer))
                    continue;

                g.raycastTarget = false;
            }
        }

        void DisableExternalRaycastBlockers()
        {
            if (extraRaycastBlockers != null)
            {
                for (int i = 0; i < extraRaycastBlockers.Length; i++)
                {
                    var g = extraRaycastBlockers[i];
                    if (g != null) g.raycastTarget = false;
                }
            }

            if (!autoDisableNamedRaycastBlockers || namedRaycastBlockers == null) return;

            for (int i = 0; i < namedRaycastBlockers.Length; i++)
            {
                string name = namedRaycastBlockers[i];
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (!_cachedNamedBlockers.TryGetValue(name, out Graphic g))
                {
                    var go = GameObject.Find(name);
                    if (go != null) g = go.GetComponent<Graphic>();
                    if (g != null) _cachedNamedBlockers[name] = g;
                }
                if (g != null) g.raycastTarget = false;
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
            int queueCount = _currentBuilding.queue.Count;

            string queueInfo = queueCount > 1 ? $" | Cola: {queueCount}" : "";
            UpdateProgress($"Entrenando: {currentUnit.displayName} ({progressPercent}%){queueInfo}");
            UpdateProgressBar(progress);

            // Actualizar texto del primer ítem de cola en tiempo real
            if (_firstQueueItemText != null)
                _firstQueueItemText.text = $"1. {currentUnit.displayName} ({progressPercent}%)";
        }

        void OnUnitButtonClick(int slot)
        {
            if (_currentBuilding == null || catalog == null) return;

            string buildingId = GetBuildingId(_currentBuilding);
            UnitSO unit = catalog.Get(buildingId, slot);

            if (unit != null)
            {
                bool success = _currentBuilding.TryQueueUnit(unit);
                
                // Mostrar mensaje si no se pudo agregar
                if (!success)
                {
                    // Verificar qué falló
                    if (_currentBuilding.populationManager != null && 
                        !_currentBuilding.populationManager.CanReservePopulation(unit.populationCost))
                    {
                        UpdateProgress($"[!] Sin población (necesitas {unit.populationCost})");
                    }
                    else if (_currentBuilding.owner != null)
                    {
                        UpdateProgress($"[!] Sin recursos para {unit.displayName}");
                    }
                }
            }
        }

        void OnCancelUnit(int index)
        {
            if (_currentBuilding != null)
            {
                _currentBuilding.CancelUnit(index);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // EVENTOS del ProductionBuilding
        // ═══════════════════════════════════════════════════════════

        void OnUnitQueued(UnitSO unit)
        {
            RefreshQueueDisplay();
            RefreshProgress();
        }

        void OnUnitCompleted(UnitSO unit)
        {
            RefreshQueueDisplay();
            RefreshProgress();
        }

        void OnQueueChanged()
        {
            // Delay muy pequeño para que el click derecho se procese completamente
            StartCoroutine(RefreshQueueDelayed());
        }

        System.Collections.IEnumerator RefreshQueueDelayed()
        {
            yield return null; // Esperar 1 frame
            RefreshQueueDisplay();
            RefreshProgress();
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

        bool HasPopulationSpace(UnitSO unit)
        {
            if (_currentBuilding == null || _currentBuilding.populationManager == null)
                return true;
            return _currentBuilding.populationManager.CanReservePopulation(unit.populationCost);
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
                Project.Gameplay.Resources.ResourceKind.Wood => "W",
                Project.Gameplay.Resources.ResourceKind.Stone => "S",
                Project.Gameplay.Resources.ResourceKind.Gold => "G",
                Project.Gameplay.Resources.ResourceKind.Food => "F",
                _ => kind.ToString()
            };
        }

        string BuildTooltip(UnitSO unit)
        {
            if (unit == null) return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(unit.displayName);

            if (unit.costs != null && unit.costs.Length > 0)
            {
                sb.Append("Costo: ");
                for (int i = 0; i < unit.costs.Length; i++)
                {
                    var c = unit.costs[i];
                    string rName = c.kind switch
                    {
                        Project.Gameplay.Resources.ResourceKind.Wood  => "Madera",
                        Project.Gameplay.Resources.ResourceKind.Stone => "Piedra",
                        Project.Gameplay.Resources.ResourceKind.Gold  => "Oro",
                        Project.Gameplay.Resources.ResourceKind.Food  => "Comida",
                        _ => c.kind.ToString()
                    };
                    sb.Append($"{rName} {c.amount}");
                    if (i < unit.costs.Length - 1) sb.Append(" | ");
                }
                sb.AppendLine();
            }

            if (unit.populationCost > 0)
                sb.AppendLine($"Poblacion: {unit.populationCost}");

            if (unit.trainingTimeSeconds > 0f)
                sb.Append($"Tiempo: {unit.trainingTimeSeconds:F0}s");

            return sb.ToString().TrimEnd();
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

        /// <summary>
        /// Método público para cancelar unidad desde QueueItemClickHandler
        /// </summary>
        public void CancelUnitByIndex(int index)
        {
            OnCancelUnit(index);
        }
    }

    /// <summary>
    /// Componente helper para detectar click derecho en items de la cola
    /// </summary>
    public class QueueItemClickHandler : MonoBehaviour, 
        UnityEngine.EventSystems.IPointerClickHandler,
        UnityEngine.EventSystems.IPointerEnterHandler,
        UnityEngine.EventSystems.IPointerExitHandler
    {
        private int _index;
        private ProductionHUD _hud;
        private UnityEngine.UI.Image _image;
        private bool _isProcessing = false;

        public void Setup(int index, ProductionHUD hud)
        {
            _index = index;
            _hud = hud;
            _image = GetComponent<UnityEngine.UI.Image>();
        }

        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
        {
            if (eventData.button != UnityEngine.EventSystems.PointerEventData.InputButton.Right &&
                eventData.button != UnityEngine.EventSystems.PointerEventData.InputButton.Left)
                return;

            if (_isProcessing) return; // Evitar múltiples clicks

            _isProcessing = true;

            // Feedback visual inmediato: color rojo
            if (_image != null)
            {
                _image.color = new Color(0.8f, 0.2f, 0.2f, 0.8f);
                _image.raycastTarget = false; // Desactivar raycasts inmediatamente
            }

            // Cancelar la unidad
            _hud.CancelUnitByIndex(_index);
        }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
        {
            if (_image != null)
                _image.color = new Color(0.3f, 0.3f, 0.3f, 0.8f); // Más claro al hacer hover
        }

        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
        {
            if (_image != null)
                _image.color = new Color(0.2f, 0.2f, 0.2f, 0.8f); // Color normal
        }
    }
}
