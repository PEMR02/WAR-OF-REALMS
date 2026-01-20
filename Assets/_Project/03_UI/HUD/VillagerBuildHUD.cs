using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.Gameplay.Units;
using Project.Gameplay.Buildings;

namespace Project.UI
{
    public class VillagerBuildHUD : MonoBehaviour
    {
        [Header("Refs")]
        public BuildModeController build;
        public RTSSelectionController selection;

        [Header("Panels")]
        public GameObject rootPanelCategories;  // Panel con 4 botones de categoría
        public GameObject rootPanelSlots;        // Panel con 9 botones de slots

        [Header("Category Buttons (1-4)")]
        public Button btnCategoryEcon;      // Slot 1
        public Button btnCategoryMilitary;  // Slot 2
        public Button btnCategoryDefenses;  // Slot 3
        public Button btnCategorySpecial;   // Slot 4

        [Header("Slot Buttons (1-9)")]
        public Button btnSlot1;
        public Button btnSlot2;
        public Button btnSlot3;
        public Button btnSlot4;
        public Button btnSlot5;
        public Button btnSlot6;
        public Button btnSlot7;
        public Button btnSlot8;
        public Button btnSlot9;

        [Header("Text Labels")]
        public TextMeshProUGUI titleText;  // Muestra estado/categoría actual

        private Button[] _categoryButtons;
        private Button[] _slotButtons;
        private TextMeshProUGUI[] _categoryLabels;
        private TextMeshProUGUI[] _slotLabels;
        bool _lastHasVillagers;
        private CanvasGroup _categoriesCanvasGroup;
        private CanvasGroup _slotsCanvasGroup;

        void Awake()
        {
            if (build == null) build = FindFirstObjectByType<BuildModeController>();
            if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();

            Debug.Log($"VillagerBuildHUD Awake: build={build != null}, selection={selection != null}");

            // Organizar arrays para facilitar acceso
            _categoryButtons = new Button[] { btnCategoryEcon, btnCategoryMilitary, btnCategoryDefenses, btnCategorySpecial };
            _slotButtons = new Button[] { btnSlot1, btnSlot2, btnSlot3, btnSlot4, btnSlot5, btnSlot6, btnSlot7, btnSlot8, btnSlot9 };

            // Obtener labels de botones
            _categoryLabels = new TextMeshProUGUI[_categoryButtons.Length];
            for (int i = 0; i < _categoryButtons.Length; i++)
            {
                if (_categoryButtons[i] != null)
                    _categoryLabels[i] = _categoryButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            }

            _slotLabels = new TextMeshProUGUI[_slotButtons.Length];
            for (int i = 0; i < _slotButtons.Length; i++)
            {
                if (_slotButtons[i] != null)
                    _slotLabels[i] = _slotButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            }

            // CRUCIAL: Agregar CanvasGroup para bloquear raycasts y evitar que clicks atraviesen al mundo
            if (rootPanelCategories != null)
            {
                _categoriesCanvasGroup = rootPanelCategories.GetComponent<CanvasGroup>();
                if (_categoriesCanvasGroup == null)
                    _categoriesCanvasGroup = rootPanelCategories.AddComponent<CanvasGroup>();
                _categoriesCanvasGroup.blocksRaycasts = true;
                _categoriesCanvasGroup.interactable = true;
                Debug.Log("VillagerBuildHUD: CanvasGroup agregado a Panel_Categories");
            }

            if (rootPanelSlots != null)
            {
                _slotsCanvasGroup = rootPanelSlots.GetComponent<CanvasGroup>();
                if (_slotsCanvasGroup == null)
                    _slotsCanvasGroup = rootPanelSlots.AddComponent<CanvasGroup>();
                _slotsCanvasGroup.blocksRaycasts = true;
                _slotsCanvasGroup.interactable = true;
                Debug.Log("VillagerBuildHUD: CanvasGroup agregado a Panel_Slots");
            }

            // Hook category buttons
            if (btnCategoryEcon != null)
                btnCategoryEcon.onClick.AddListener(() => OnCategoryClick(BuildCategory.Econ));
            if (btnCategoryMilitary != null)
                btnCategoryMilitary.onClick.AddListener(() => OnCategoryClick(BuildCategory.Military));
            if (btnCategoryDefenses != null)
                btnCategoryDefenses.onClick.AddListener(() => OnCategoryClick(BuildCategory.Defenses));
            if (btnCategorySpecial != null)
                btnCategorySpecial.onClick.AddListener(() => OnCategoryClick(BuildCategory.Special));

            // Hook slot buttons
            for (int i = 0; i < _slotButtons.Length; i++)
            {
                int slotIndex = i + 1; // slots son 1-9, array es 0-8
                if (_slotButtons[i] != null)
                    _slotButtons[i].onClick.AddListener(() => OnSlotClick(slotIndex));
            }

            // Inicializar: ocultar todo al inicio
            HideAllPanels();
        }

        void Start()
        {
            // Asegurar que los paneles empiecen ocultos
            HideAllPanels();
            
            // Forzar un refresh inicial
            RefreshUI();
        }

        void OnEnable()
        {
            if (build != null)
            {
                build.OnStateChanged += OnBuildStateChanged;
                build.OnCategoryChanged += OnCategoryChanged;
                build.OnBuildingChanged += OnBuildingChanged;
                Debug.Log("VillagerBuildHUD: Eventos suscritos correctamente");
            }
            else
            {
                Debug.LogError("VillagerBuildHUD: build es NULL en OnEnable, eventos NO suscritos!");
            }
        }

        void OnDisable()
        {
            if (build != null)
            {
                build.OnStateChanged -= OnBuildStateChanged;
                build.OnCategoryChanged -= OnCategoryChanged;
                build.OnBuildingChanged -= OnBuildingChanged;
            }
        }

        void Update()
        {
            bool hasVillagers = selection != null && selection.HasSelectedVillagers();

            // Si cambió el estado de selección, actúa
            if (hasVillagers != _lastHasVillagers)
            {
                Debug.Log($"VillagerBuildHUD: Update - Cambió selección: hasVillagers={hasVillagers}, estado={build?.state}");
                _lastHasVillagers = hasVillagers;

                if (!hasVillagers)
                {
                    Debug.LogWarning("VillagerBuildHUD: Aldeanos DESELECCIONADOS - Cancelando build mode");
                    // Si pierdes aldeanos, cerrar HUD (y salir del modo build gradualmente)
                    HideAllPanels();
                    if (build != null && build.state != BuildState.Idle)
                        build.Cancel(); // se irá cerrando en 1-3 frames
                    return;
                }
                else
                {
                    // Si recién seleccionaste aldeanos, entrar al root como AoE2
                    if (build != null && build.state == BuildState.Idle)
                        build.EnterBuildRoot();
                }
            }

            // Si no hay aldeanos, ocultar todo
            if (!hasVillagers)
            {
                HideAllPanels();
                return;
            }

            // Con aldeanos, refrescar normal
            RefreshUI();
            
            // Asegurar que el panel correcto esté visible según el estado
            if (build != null)
            {
                if (build.state == BuildState.BuildRoot)
                {
                    // En BuildRoot, asegurar que categories esté visible
                    if (rootPanelCategories != null && !rootPanelCategories.activeSelf)
                        rootPanelCategories.SetActive(true);
                    if (rootPanelSlots != null && rootPanelSlots.activeSelf)
                        rootPanelSlots.SetActive(false);
                }
                else if (build.state == BuildState.Category || build.state == BuildState.Placing)
                {
                    // En Category/Placing, asegurar que slots esté visible
                    if (rootPanelSlots != null)
                    {
                        if (!rootPanelSlots.activeSelf)
                        {
                            Debug.LogWarning($"VillagerBuildHUD: Update detectó que Panel_Slots está desactivado. Estado: {build.state}, Reactivando...");
                            rootPanelSlots.SetActive(true);
                            rootPanelSlots.transform.SetAsLastSibling();
                        }
                        
                        // Asegurar que todos los botones estén activos
                        for (int i = 0; i < _slotButtons.Length; i++)
                        {
                            if (_slotButtons[i] != null && !_slotButtons[i].gameObject.activeSelf)
                            {
                                _slotButtons[i].gameObject.SetActive(true);
                            }
                        }
                        
                        // Verificar que el Canvas esté activo
                        Canvas canvas = rootPanelSlots.GetComponentInParent<Canvas>();
                        if (canvas != null && !canvas.gameObject.activeInHierarchy)
                        {
                            Debug.LogWarning("VillagerBuildHUD: Canvas está desactivado!");
                            canvas.gameObject.SetActive(true);
                        }
                    }
                    // Asegurar que Panel_Categories esté completamente desactivado
                    if (rootPanelCategories != null && rootPanelCategories.activeSelf)
                    {
                        rootPanelCategories.SetActive(false);
                    }
                }
            }
        }

        void OnBuildStateChanged(BuildState newState)
        {
            Debug.Log($"VillagerBuildHUD: OnBuildStateChanged llamado - Nuevo estado: {newState}");
            RefreshUI();
        }

        void OnCategoryChanged(BuildCategory? category)
        {
            RefreshUI();
        }

        void OnBuildingChanged(BuildingSO building)
        {
            RefreshUI();
        }

        void OnCategoryClick(BuildCategory cat)
        {
            Debug.Log($"VillagerBuildHUD: OnCategoryClick - Categoría: {cat}, build={build != null}, Estado actual: {build?.state}");
            
            if (build != null)
            {
                build.EnterCategory(cat);
                Debug.Log($"VillagerBuildHUD: Después de EnterCategory - Nuevo estado: {build.state}");
                
                // Forzar refresh inmediato después del clic
                StartCoroutine(ForceRefreshAfterClick());
            }
            else
            {
                Debug.LogError("VillagerBuildHUD: build es NULL en OnCategoryClick!");
            }
        }
        
        System.Collections.IEnumerator ForceRefreshAfterClick()
        {
            yield return null; // Esperar 1 frame
            if (build != null && build.state == BuildState.Category)
            {
                Debug.Log($"VillagerBuildHUD: ForceRefreshAfterClick - Estado: {build.state}, Panel_Slots activo: {rootPanelSlots?.activeSelf}");
                RefreshUI();
            }
        }

        void OnSlotClick(int slotIndex)
        {
            if (build != null)
                build.PickSlot(slotIndex);
        }

        void RefreshUI()
        {
            // Si no hay build controller, ocultar todo
            if (build == null)
            {
                HideAllPanels();
                return;
            }

            // Mostrar paneles según estado - SOLO UNO A LA VEZ (similar a BuildPanelVisibility)
            switch (build.state)
            {
                case BuildState.Idle:
                    ShowCategories(false);
                    ShowSlots(false);
                    UpdateTitle("");
                    break;

                case BuildState.BuildRoot:
                    ShowCategories(true);
                    ShowSlots(false);
                    UpdateTitle("Construcción");
                    RefreshCategoryButtons();
                    break;

                case BuildState.Category:
                    // Asegurar que el panel de slots esté visible PRIMERO
                    ShowCategories(false);
                    ShowSlots(true);
                    
                    // Forzar que el panel y sus botones estén activos
                    if (rootPanelSlots != null)
                    {
                        rootPanelSlots.SetActive(true);
                        
                        // Asegurar que todos los botones estén activos
                        for (int i = 0; i < _slotButtons.Length; i++)
                        {
                            if (_slotButtons[i] != null)
                            {
                                _slotButtons[i].gameObject.SetActive(true);
                                // Asegurar que el botón sea visible
                                CanvasGroup cg = _slotButtons[i].GetComponent<CanvasGroup>();
                                if (cg != null)
                                {
                                    cg.alpha = 1f;
                                    cg.interactable = true;
                                    cg.blocksRaycasts = true;
                                }
                            }
                        }
                        
                        // Verificar tamaño del panel (debe ser suficiente para 9 botones en grid 3x3)
                        RectTransform rt = rootPanelSlots.GetComponent<RectTransform>();
                        if (rt != null && rt.sizeDelta.y < 280)
                        {
                            Debug.LogWarning($"VillagerBuildHUD: Panel_Slots height ({rt.sizeDelta.y}) es muy pequeño para 9 botones. Recomendado: mínimo 280");
                        }
                    }
                    
                    // Solo refrescar botones si ya hay categoría asignada
                    // (evita el problema de timing cuando se llama desde OnStateChanged antes de SetCategory)
                    if (build.currentCategory != null)
                    {
                        UpdateTitle(GetCategoryName(build.currentCategory));
                        RefreshSlotButtons();
                    }
                    else
                    {
                        // Si aún no hay categoría, solo mostrar el panel vacío
                        UpdateTitle("Selecciona edificio");
                        // Mostrar todos los botones pero deshabilitados
                        for (int i = 0; i < _slotButtons.Length; i++)
                        {
                            if (_slotButtons[i] != null)
                            {
                                _slotButtons[i].gameObject.SetActive(true);
                                _slotButtons[i].interactable = false;
                                if (_slotLabels[i] != null)
                                    _slotLabels[i].text = $"{i + 1}.";
                            }
                        }
                    }
                    break;

                case BuildState.Placing:
                    ShowCategories(false);
                    ShowSlots(true);
                    UpdateTitle($"Colocando: {build.currentBuilding?.id ?? "?"}");
                    RefreshSlotButtons();
                    break;
            }
        }

        void ShowCategories(bool show)
        {
            if (rootPanelCategories != null)
            {
                // Forzar activación incluso si está desactivado en el Inspector
                // (similar a BuildPanelVisibility que siempre activa/desactiva)
                rootPanelCategories.SetActive(show);
            }
        }

        void ShowSlots(bool show)
        {
            if (rootPanelSlots != null)
            {
                // Forzar activación siempre (similar a BuildPanelVisibility)
                rootPanelSlots.SetActive(show);
                
                // Asegurar que el padre esté activo si queremos mostrar el panel
                if (show)
                {
                    Transform parent = rootPanelSlots.transform.parent;
                    if (parent != null && !parent.gameObject.activeInHierarchy)
                    {
                        parent.gameObject.SetActive(true);
                    }
                    
                    // Asegurar que todos los botones hijos estén activos
                    for (int i = 0; i < _slotButtons.Length; i++)
                    {
                        if (_slotButtons[i] != null && !_slotButtons[i].gameObject.activeSelf)
                        {
                            _slotButtons[i].gameObject.SetActive(true);
                        }
                    }
                    
                    // Asegurar que Panel_Categories esté completamente desactivado
                    if (rootPanelCategories != null && rootPanelCategories.activeSelf)
                    {
                        rootPanelCategories.SetActive(false);
                    }
                    
                    // Asegurar que Panel_Slots tenga un índice de renderizado mayor (se renderice encima)
                    rootPanelSlots.transform.SetAsLastSibling();
                }
            }
        }

        void HideAllPanels()
        {
            ShowCategories(false);
            ShowSlots(false);
            UpdateTitle("");
        }

        void RefreshCategoryButtons()
        {
            // Etiquetas fijas para categorías
            string[] categoryNames = { "Económico", "Militar", "Defensas", "Especial" };

            for (int i = 0; i < _categoryButtons.Length; i++)
            {
                bool hasCategory = i < categoryNames.Length;
                if (_categoryButtons[i] != null)
                {
                    _categoryButtons[i].gameObject.SetActive(hasCategory);
                    _categoryButtons[i].interactable = hasCategory;
                }

                if (hasCategory && _categoryLabels[i] != null)
                    _categoryLabels[i].text = $"{i + 1}. {categoryNames[i]}";
            }
        }

        void RefreshSlotButtons()
        {
            if (build == null || build.catalog == null || build.currentCategory == null)
            {
                // Si no hay categoría, mostrar todos los botones pero deshabilitados
                for (int i = 0; i < _slotButtons.Length; i++)
                {
                    if (_slotButtons[i] != null)
                    {
                        _slotButtons[i].gameObject.SetActive(true);
                        _slotButtons[i].interactable = false;
                        if (_slotLabels[i] != null)
                            _slotLabels[i].text = $"{i + 1}.";
                    }
                }
                return;
            }

            BuildCategory cat = build.currentCategory.Value;

            // Refrescar cada slot según BuildCatalog
            for (int slot = 1; slot <= 9; slot++)
            {
                int arrayIndex = slot - 1;
                var building = build.catalog.Get(cat, slot);

                if (_slotButtons[arrayIndex] != null)
                {
                    bool hasBuilding = building != null;
                    // Mostrar el botón siempre, pero deshabilitarlo si no hay edificio
                    _slotButtons[arrayIndex].gameObject.SetActive(true);
                    _slotButtons[arrayIndex].interactable = hasBuilding;

                    // Actualizar label
                    if (_slotLabels[arrayIndex] != null)
                    {
                        if (hasBuilding)
                            _slotLabels[arrayIndex].text = $"{slot}. {building.id}";
                        else
                            _slotLabels[arrayIndex].text = $"{slot}.";
                    }
                }
            }
        }

        void UpdateTitle(string text)
        {
            if (titleText != null)
                titleText.text = text;
        }

        string GetCategoryName(BuildCategory? cat)
        {
            if (cat == null) return "Categoría";
            return cat.Value switch
            {
                BuildCategory.Econ => "Económico",
                BuildCategory.Military => "Militar",
                BuildCategory.Defenses => "Defensas",
                BuildCategory.Special => "Especial",
                _ => "Categoría"
            };
        }
    }
}
