using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using Project.Gameplay.Combat;
using Project.Gameplay.Resources;
using Project.Gameplay.Buildings;

namespace Project.Gameplay.Units
{
    public class RTSSelectionController : MonoBehaviour
    {
        [Header("Refs")]
        public Camera cam;
        
        public bool IsDraggingSelection => _isDragging && Mouse.current != null && Mouse.current.leftButton.isPressed;

        [Header("Selection")]
        public LayerMask unitLayerMask;
        public bool addToSelectionWithShift = true;

        [Header("Double Click")]
        public float doubleClickTime = 0.3f;

        [Header("Hover sobre recursos (con aldeano seleccionado)")]
        [Tooltip("Tiempo sobre un recurso para mostrar borde de hover (facilita el clic).")]
        public float resourceHoverDelay = 0.25f;

        public LayerMask buildingLayerMask;
        [Tooltip("Capa de recursos (árboles, piedra, oro). Debe incluir la capa 'Resource' si el generador la usa.")]
        public LayerMask resourceLayerMask;

        private Project.Gameplay.Buildings.BuildingSelectable _selectedBuilding;
        private ResourceSelectable _selectedResource;
        
        public Project.UI.SelectionBoxUI selectionBoxUI;

        private Vector2 _dragStart;
        private bool _isDragging;

        private readonly List<UnitSelectable> _selected = new();
        private readonly Dictionary<int, HealthBarWorld> _healthBarCache = new();
        private int _selectedVillagerCount;

        // Para detectar doble clic
        private float _lastClickTime;
        private Vector2 _lastClickPos;

        // Hover sobre recursos
        private ResourceSelectable _hoveredResource;
        private float _hoveredResourceTime;
		
		public Project.Gameplay.Buildings.BuildingPlacer buildingPlacer;

        [Header("Barra de vida (opcional)")]
        [Tooltip("Prefab con HealthBar (Canvas + HealthBarWorld + Fill). Si la unidad no tiene barra (ej. aldeanos de la escena), se instancia este prefab al seleccionar.")]
        public GameObject healthBarFallbackPrefab;

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (selectionBoxUI == null)
                selectionBoxUI = FindFirstObjectByType<Project.UI.SelectionBoxUI>();
			if (buildingPlacer == null)
				buildingPlacer = FindFirstObjectByType<Project.Gameplay.Buildings.BuildingPlacer>();
	        }

        void Update()
        {
            if (buildingPlacer != null && buildingPlacer.IsPlacing)
				return; // mientras colocas edificios, no seleccionas unidades
			
			var mouse = Mouse.current;
            if (mouse == null || cam == null) return;

            // LMB pressed -> start drag
            if (mouse.leftButton.wasPressedThisFrame)
            {
                // CRUCIAL: Ignorar clicks sobre UI (botones, paneles, etc)
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                {
                    return; // Hay UI bajo el cursor, no procesar selección
                }

                _dragStart = mouse.position.ReadValue();
                _isDragging = true;

                // Mostrar rectángulo desde el inicio
                selectionBoxUI?.Show(_dragStart, _dragStart);
            }

            // Mientras mantienes presionado -> actualizar rectángulo
            if (_isDragging && mouse.leftButton.isPressed)
            {
                selectionBoxUI?.Show(_dragStart, mouse.position.ReadValue());
            }

            // Hover sobre recursos cuando hay aldeano seleccionado
            UpdateResourceHover();

            // LMB released -> select click or box
            if (_isDragging && mouse.leftButton.wasReleasedThisFrame)
            {
                Vector2 dragEnd = mouse.position.ReadValue();
                _isDragging = false;

                // Ocultar rectángulo al soltar
                selectionBoxUI?.Hide();

                // CRUCIAL: Si el cursor está sobre UI al soltar, cancelar selección
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                {
                    return; // Hay UI bajo el cursor, no procesar selección
                }

                bool isBox = Vector2.Distance(_dragStart, dragEnd) > 8f;

                bool additive = addToSelectionWithShift &&
                                (Keyboard.current != null) &&
                                (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

                if (!additive) ClearSelection();

                if (isBox)
                {
                    BoxSelect(_dragStart, dragEnd);
                }
                else
                {
                    // Detectar doble clic
                    float timeSinceLastClick = Time.time - _lastClickTime;
                    bool isDoubleClick = timeSinceLastClick <= doubleClickTime &&
                                        Vector2.Distance(_lastClickPos, dragEnd) < 10f;

                    if (isDoubleClick)
                    {
                        // Si tiene shift, no limpia la selección previa
                        DoubleClickSelect(dragEnd, additive);
                        _lastClickTime = 0f; // resetear para evitar triple clic
                    }
                    else
                    {
                        ClickSelect(dragEnd);
                        _lastClickTime = Time.time;
                        _lastClickPos = dragEnd;
                    }
                }
            }
        }

        void ClickSelect(Vector2 screenPos)
        {
            Ray ray = cam.ScreenPointToRay(screenPos);

            // 1) Prioridad: Units
            if (Physics.Raycast(ray, out RaycastHit hitU, 5000f, unitLayerMask))
            {
                var u = hitU.collider.GetComponentInParent<UnitSelectable>();
                if (u != null) AddSelection(u);
                return;
            }

            // 2) Luego: Buildings
            if (Physics.Raycast(ray, out RaycastHit hitB, 5000f, buildingLayerMask))
            {
                var b = hitB.collider.GetComponentInParent<Project.Gameplay.Buildings.BuildingSelectable>();
                if (b != null)
                {
                    ClearSelection();
                    b.SetSelected(true);
                    _selectedBuilding = b;
                    SetWorldHealthBarVisible(b.gameObject, true);
                    return;
                }
            }

            // 3) Recursos (árboles, piedra, oro, etc.)
            if (resourceLayerMask != 0 && Physics.Raycast(ray, out RaycastHit hitR, 5000f, resourceLayerMask))
            {
                var res = hitR.collider.GetComponentInParent<ResourceSelectable>();
                if (res != null && res.GetResourceNode() != null)
                {
                    ClearSelection();
                    res.SetSelected(true);
                    _selectedResource = res;
                    SetWorldHealthBarVisible(res.gameObject, true);
                }
            }
        }

        void DoubleClickSelect(Vector2 screenPos, bool additive = false)
        {
            Ray ray = cam.ScreenPointToRay(screenPos);

            // Verificar qué unidad clickeaste
            if (!Physics.Raycast(ray, out RaycastHit hit, 5000f, unitLayerMask))
                return;

            var clickedUnit = hit.collider.GetComponentInParent<UnitSelectable>();
            if (clickedUnit == null) return;

            // Obtener el nombre base del prefab (sin el número de clon)
            // "Aldeano (2)" -> "Aldeano"
            // "Unit_01 (3)" -> "Unit_01"
            string clickedName = clickedUnit.gameObject.name;
            string baseName = clickedName.Split('(')[0].Trim();

            // Solo limpiar selección si NO es aditivo (sin shift)
            if (!additive)
                ClearSelection();

            // Buscar todas las unidades del mismo tipo que estén visibles
            var allUnits = FindObjectsByType<UnitSelectable>(FindObjectsSortMode.None);
            
            for (int i = 0; i < allUnits.Length; i++)
            {
                var u = allUnits[i];
                
                // Obtener el nombre base de esta unidad
                string uName = u.gameObject.name;
                string uBaseName = uName.Split('(')[0].Trim();
                
                // Verificar que sea del mismo tipo (mismo nombre base)
                if (uBaseName != baseName) continue;

                // Verificar que esté visible en cámara
                Vector3 screenPoint = cam.WorldToScreenPoint(u.transform.position);
                
                // Está detrás de la cámara
                if (screenPoint.z < 0f) continue;

                // Está fuera de los límites de la pantalla
                if (screenPoint.x < 0 || screenPoint.x > Screen.width ||
                    screenPoint.y < 0 || screenPoint.y > Screen.height)
                    continue;

                // Seleccionar esta unidad
                AddSelection(u);
            }
        }

        void BoxSelect(Vector2 start, Vector2 end)
        {
            Rect r = MakeRect(start, end);

            var all = FindObjectsByType<UnitSelectable>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var u = all[i];
                Vector3 sp = cam.WorldToScreenPoint(u.transform.position);
                if (sp.z < 0f) continue;

                if (r.Contains(sp))
                    AddSelection(u);
            }
        }

        void AddSelection(UnitSelectable u)
        {
            if (_selected.Contains(u)) return;
            _selected.Add(u);
            u.SetSelected(true);
            SetWorldHealthBarVisible(u.gameObject, true);

            // Cache: aldeano = VillagerGatherer o Builder.
            if (u.GetComponent<VillagerGatherer>() != null) _selectedVillagerCount++;
            else if (u.GetComponent<Builder>() != null) _selectedVillagerCount++;
        }

        void ClearSelection()
        {
            ClearResourceHover();
            if (_selectedBuilding != null)
            {
                SetWorldHealthBarVisible(_selectedBuilding.gameObject, false);
                _selectedBuilding.SetSelected(false);
                _selectedBuilding = null;
            }

            if (_selectedResource != null)
            {
                SetWorldHealthBarVisible(_selectedResource.gameObject, false);
                _selectedResource.SetSelected(false);
                _selectedResource = null;
            }

            for (int i = 0; i < _selected.Count; i++)
            {
                if (_selected[i] != null)
                {
                    SetWorldHealthBarVisible(_selected[i].gameObject, false);
                    _selected[i].SetSelected(false);
                }
            }

            _selected.Clear();
            _selectedVillagerCount = 0;
        }

        static Rect MakeRect(Vector2 a, Vector2 b)
        {
            float xMin = Mathf.Min(a.x, b.x);
            float yMin = Mathf.Min(a.y, b.y);
            float xMax = Mathf.Max(a.x, b.x);
            float yMax = Mathf.Max(a.y, b.y);

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
        
        public System.Collections.Generic.IReadOnlyList<UnitSelectable> GetSelected() => _selected;
		
		/// <summary>
		/// Obtiene el edificio actualmente seleccionado (si hay uno)
		/// </summary>
		public Project.Gameplay.Buildings.BuildingSelectable GetSelectedBuilding() => _selectedBuilding;

        /// <summary>Obtiene el recurso actualmente seleccionado (árbol, piedra, oro, etc.).</summary>
        public ResourceSelectable GetSelectedResource() => _selectedResource;

        /// <summary>Obtiene el nodo del recurso seleccionado (cantidad, tipo). Null si no hay recurso seleccionado.</summary>
        public ResourceNode GetSelectedResourceNode() => _selectedResource != null ? _selectedResource.GetResourceNode() : null;
		
		public int CountSelectedWithComponent<T>() where T : Component
		{
			int c = 0;
			for (int i = 0; i < _selected.Count; i++)
				if (_selected[i] != null && _selected[i].GetComponent<T>() != null) c++;
			return c;
		}
		
		public bool HasSelectedVillagers()
		{
			return _selectedVillagerCount > 0;
		}
		
		public void SelectOnly(UnitSelectable u)
		{
			if (u == null) return;
			ClearSelection();
			AddSelection(u);
		}

        public void AddToSelection(UnitSelectable u)
        {
            if (u == null) return;
            AddSelection(u);
        }

        void UpdateResourceHover()
        {
            if (!HasSelectedVillagers() || resourceLayerMask == 0 || cam == null)
            {
                ClearResourceHover();
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                ClearResourceHover();
                return;
            }

            var mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            Ray ray = cam.ScreenPointToRay(mousePos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 5000f, resourceLayerMask))
            {
                ClearResourceHover();
                return;
            }

            var res = hit.collider.GetComponentInParent<ResourceSelectable>();
            if (res == null || res.GetResourceNode() == null)
            {
                ClearResourceHover();
                return;
            }

            if (res == _hoveredResource)
            {
                _hoveredResourceTime += Time.deltaTime;
                if (_hoveredResourceTime >= resourceHoverDelay)
                    res.SetHovered(true);
            }
            else
            {
                ClearResourceHover();
                _hoveredResource = res;
                _hoveredResourceTime = 0f;
            }
        }

        void ClearResourceHover()
        {
            if (_hoveredResource != null)
            {
                _hoveredResource.SetHovered(false);
                _hoveredResource = null;
            }
            _hoveredResourceTime = 0f;
        }

        void SetWorldHealthBarVisible(GameObject entity, bool visible)
        {
            if (entity == null) return;
            int id = entity.GetInstanceID();
            _healthBarCache.TryGetValue(id, out var bar);
            if (bar == null)
            {
                bar = entity.GetComponentInChildren<HealthBarWorld>(true);
                if (bar != null) _healthBarCache[id] = bar;
            }
            var source = FindOrCreateWorldBarSource(entity);
            if (bar != null && !IsUsableBar(bar))
            {
                bar = null;
                _healthBarCache.Remove(id);
            }
            // Si la entidad tiene cualquier fuente de barra (vida, recurso) pero no tiene barra, instanciar fallback
            if (bar == null && visible && healthBarFallbackPrefab != null && source != null)
            {
                GameObject clone = Instantiate(healthBarFallbackPrefab, entity.transform);
                clone.name = "HealthBar";
                bar = clone.GetComponentInChildren<HealthBarWorld>(true);
                if (bar != null) _healthBarCache[id] = bar;
            }
            if (bar == null) return;
            if (visible) bar.Show();
            else bar.Hide();
        }

        static bool IsUsableBar(HealthBarWorld bar)
        {
            if (bar == null) return false;
            if (bar.GetComponent<Canvas>() == null) return false;
            return true;
        }

        IWorldBarSource FindOrCreateWorldBarSource(GameObject entity)
        {
            if (entity == null) return null;

            var source = entity.GetComponent<IWorldBarSource>();
            if (source != null) return source;
            source = entity.GetComponentInParent<IWorldBarSource>();
            if (source != null) return source;
            source = entity.GetComponentInChildren<IWorldBarSource>(true);
            if (source != null) return source;

            // Fallback para edificios seleccionables sin Health (algunos prefabs viejos).
            var building = entity.GetComponentInParent<BuildingSelectable>();
            if (building != null)
            {
                var health = building.GetComponent<Health>();
                if (health == null)
                {
                    int hp = 200;
                    var bi = building.GetComponent<BuildingInstance>();
                    if (bi != null && bi.buildingSO != null)
                        hp = Mathf.Max(1, bi.buildingSO.maxHP);

                    health = building.gameObject.AddComponent<Health>();
                    health.InitFromMax(hp);
                }
                return health;
            }

            return null;
        }
    }
}