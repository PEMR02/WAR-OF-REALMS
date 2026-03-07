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
        /// <summary>Se dispara cada vez que cambia la selección (unidades, edificio o recurso).</summary>
        public event System.Action OnSelectionChanged;

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
        private int _selectedVillagerCount;

        // Para detectar doble clic
        private float _lastClickTime;
        private Vector2 _lastClickPos;

        // Hover sobre recursos
        private ResourceSelectable _hoveredResource;
        private float _hoveredResourceTime;
		
		public Project.Gameplay.Buildings.BuildingPlacer buildingPlacer;

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

            // ESC: deseleccionar todo (cuando no se está colocando un edificio)
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                ClearSelection();
                return;
            }

            // LMB pressed -> start drag
            if (mouse.leftButton.wasPressedThisFrame)
            {
                // CRUCIAL: Ignorar clicks sobre UI (botones, paneles, etc) o sobre el minimapa
                if ((EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    || Project.UI.RuntimeMinimapBootstrap.IsPointerOverMinimap)
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
                if (Project.UI.RuntimeMinimapBootstrap.IsPointerOverMinimap)
                {
                    // Si el drag empezó fuera del minimapa pero el cursor llegó a él, cancelar
                    _isDragging = false;
                    selectionBoxUI?.Hide();
                }
                else
                {
                    selectionBoxUI?.Show(_dragStart, mouse.position.ReadValue());
                }
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

                bool isBox = Vector2.Distance(_dragStart, dragEnd) > 12f;

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
                    // Detectar doble clic (tolerancia 24px para que cuente como doble clic aunque el dedo se mueva un poco)
                    float timeSinceLastClick = Time.time - _lastClickTime;
                    bool isDoubleClick = timeSinceLastClick <= doubleClickTime &&
                                        Vector2.Distance(_lastClickPos, dragEnd) < 24f;

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
                    SetHealthBarVisibleForEntity(b.gameObject, true);
                    OnSelectionChanged?.Invoke();
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
                    SetHealthBarVisibleForEntity(res.gameObject, true);
                    OnSelectionChanged?.Invoke();
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

                // Verificar que esté visible en cámara (margen para incluir unidades en el borde)
                Vector3 screenPoint = cam.WorldToScreenPoint(u.transform.position);
                const float screenMargin = 20f;
                
                // Está detrás de la cámara
                if (screenPoint.z < 0f) continue;

                // Está fuera de los límites de la pantalla (con margen)
                if (screenPoint.x < -screenMargin || screenPoint.x > Screen.width + screenMargin ||
                    screenPoint.y < -screenMargin || screenPoint.y > Screen.height + screenMargin)
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
            SetHealthBarVisibleForEntity(u.gameObject, true);

            // Cache: aldeano = VillagerGatherer o Builder.
            if (u.GetComponent<VillagerGatherer>() != null) _selectedVillagerCount++;
            else if (u.GetComponent<Builder>() != null) _selectedVillagerCount++;

            OnSelectionChanged?.Invoke();
        }

        /// <summary>Limpia la selección actual (unidades, edificio, recurso). Llamable desde UI (ej. al cancelar construcción).</summary>
        public void ClearSelection()
        {
            ClearResourceHover();
            bool hadSelection = _selected.Count > 0 || _selectedBuilding != null || _selectedResource != null;

            if (_selectedBuilding != null)
            {
                SetHealthBarVisibleForEntity(_selectedBuilding.gameObject, false);
                _selectedBuilding.SetSelected(false);
                _selectedBuilding = null;
            }

            if (_selectedResource != null)
            {
                SetHealthBarVisibleForEntity(_selectedResource.gameObject, false);
                _selectedResource.SetSelected(false);
                _selectedResource = null;
            }

            for (int i = 0; i < _selected.Count; i++)
            {
                if (_selected[i] != null)
                {
                    SetHealthBarVisibleForEntity(_selected[i].gameObject, false);
                    _selected[i].SetSelected(false);
                }
            }

            _selected.Clear();
            _selectedVillagerCount = 0;
            if (hadSelection) OnSelectionChanged?.Invoke();
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

        /// <summary>Muestra u oculta la barra flotante para la entidad. Usa la API del HealthBarManager; solo entidades con Health muestran barra.</summary>
        void SetHealthBarVisibleForEntity(GameObject entity, bool visible)
        {
            if (entity == null) return;
            if (visible)
                HealthBarManager.ShowBarForEntity(entity);
            else
                HealthBarManager.HideBarForEntity(entity);
        }
    }
}