using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

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

        public LayerMask buildingLayerMask;
        
        private Project.Gameplay.Buildings.BuildingSelectable _selectedBuilding;
        
        public Project.UI.SelectionBoxUI selectionBoxUI;

        private Vector2 _dragStart;
        private bool _isDragging;

        private readonly List<UnitSelectable> _selected = new();

        // Para detectar doble clic
        private float _lastClickTime;
        private Vector2 _lastClickPos;
		
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
            var allUnits = FindObjectsOfType<UnitSelectable>();
            
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

            var all = FindObjectsOfType<UnitSelectable>();
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
        }

        void ClearSelection()
        {
            if (_selectedBuilding != null)
            {
                _selectedBuilding.SetSelected(false);
                _selectedBuilding = null;
            }

            for (int i = 0; i < _selected.Count; i++)
                if (_selected[i] != null) _selected[i].SetSelected(false);

            _selected.Clear();
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
		
		public int CountSelectedWithComponent<T>() where T : Component
		{
			int c = 0;
			for (int i = 0; i < _selected.Count; i++)
				if (_selected[i] != null && _selected[i].GetComponent<T>() != null) c++;
			return c;
		}
		
		public bool HasSelectedVillagers()
		{
			for (int i = 0; i < _selected.Count; i++)
			{
				var u = _selected[i];
				if (u == null) continue;

				// Si tiene VillagerGatherer o Builder, lo consideramos "aldeano"
				if (u.GetComponent<VillagerGatherer>() != null) return true;
				if (u.GetComponent<Builder>() != null) return true;
			}
			return false;
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

    }
}