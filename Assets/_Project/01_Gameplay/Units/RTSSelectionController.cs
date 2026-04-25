using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Project.Gameplay.Combat;
using Project.Gameplay.Resources;
using Project.Gameplay.Buildings;
using Project.Gameplay.Map;
using Project.UI;
using Project.Gameplay.Faction;
using Project.Core;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
        [Tooltip("Incluir la capa Units (y Default). Se rellenará en runtime si hace falta; ver ProjectLayers.Units.")]
        public LayerMask unitLayerMask;
        [Tooltip("Mantener pulsado Shift al hacer clic/doble clic para sumar unidades a la selección (mismo tipo en doble clic).")]
        public bool addToSelectionWithShift = true;
        [Tooltip("Mantener pulsado Ctrl al hacer clic/doble clic para sumar otros tipos de unidades a la selección (mezclar aldeanos, soldados, etc.).")]
        public bool addToSelectionWithCtrl = true;

        [Header("Double Click")]
        public float doubleClickTime = 0.3f;

        [Header("Hover sobre recursos")]
        [Tooltip("Si está activo, solo se muestra hover al pasar el mouse si tienes al menos un aldeano tuyo seleccionado.")]
        public bool resourceHoverRequiresSelectedVillager = false;
        [Tooltip("Segundos sobre el recurso antes de mostrar hover (0 = inmediato).")]
        public float resourceHoverDelay = 0f;
        public bool debugResourcePicking = false;

        public LayerMask buildingLayerMask;
        [Tooltip("Capa de recursos (árboles, piedra, oro). Si está en Nothing, se rellena desde RTSMapGenerator.resourceLayerName al iniciar.")]
        public LayerMask resourceLayerMask;

        private Project.Gameplay.Buildings.BuildingSelectable _selectedBuilding;
        private ResourceSelectable _selectedResource;
        
        public Project.UI.SelectionBoxUI selectionBoxUI;

        private Vector2 _dragStart;
        private bool _isDragging;

        private readonly List<UnitSelectable> _selected = new();

        // Para detectar doble clic
        private float _lastClickTime;
        private Vector2 _lastClickPos;

        // Hover sobre recursos
        private ResourceSelectable _hoveredResource;
        private float _hoveredResourceTime;
        private int _resourceHoverFrameSkip;
        private int _buildingHoverFrameSkip;
        private int _unitHoverFrameSkip;
        // Hover sobre edificios
        private BuildingSelectable _hoveredBuilding;
        // Hover sobre unidades
        private UnitSelectable _hoveredUnit;
		
		public Project.Gameplay.Buildings.BuildingPlacer buildingPlacer;

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (selectionBoxUI == null)
                selectionBoxUI = FindFirstObjectByType<Project.UI.SelectionBoxUI>();
            if (buildingPlacer == null)
                buildingPlacer = FindFirstObjectByType<Project.Gameplay.Buildings.BuildingPlacer>();
            EnsureUnitLayerMask();
            EnsureResourceLayerMaskFromMap();
            EnsureBuildingLayerMaskFromMap();
            if (resourceLayerMask.value != 0)
                resourceLayerMask |= 1 << 0;
            if (buildingLayerMask.value != 0)
                buildingLayerMask |= 1 << 0;
        }

        void Start()
        {
            EnsureUnitLayerMask();
            EnsureResourceLayerMaskFromMap();
            EnsureBuildingLayerMaskFromMap();
        }

        /// <summary>Todas las unidades usan <see cref="ProjectLayers.Units"/>. Asegura el bit en el mask aunque la escena tenga valores viejos.</summary>
        void EnsureUnitLayerMask()
        {
            int u = LayerMask.NameToLayer(ProjectLayers.Units);
            if (u < 0) return;
            if (unitLayerMask.value == 0)
                unitLayerMask = (1 << u) | 1;
            else
            {
                unitLayerMask |= 1 << u;
                unitLayerMask |= 1;
            }
        }

        void EnsureResourceLayerMaskFromMap()
        {
            string layerName = "Resource";
            var gen = FindFirstObjectByType<RTSMapGenerator>();
            if (gen != null && !string.IsNullOrEmpty(gen.resourceLayerName))
                layerName = gen.resourceLayerName;
            int layer = MapResourcePlacer.ResolveResourceLayerIndex(layerName);
            if (layer < 0) return;
            int bit = 1 << layer;
            // La escena puede tener serializado solo BaseTerrain (11); los recursos colocados van a "Resource".
            if ((resourceLayerMask.value & bit) == 0)
                resourceLayerMask |= bit;
        }

        /// <summary>Si el mask quedó en Nothing en el prefab, usar capa Building para raycasts de hover/clic.</summary>
        void EnsureBuildingLayerMaskFromMap()
        {
            if (buildingLayerMask != 0) return;
            int layer = LayerMask.NameToLayer("Building");
            if (layer < 0) layer = 0;
            buildingLayerMask = 1 << layer;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (!ShouldReadPointerInput()) return;

            // ESC: primero retroceder en el menú de construcción (categoría / colocación); si no hay menú activo, deseleccionar.
            // Debe ir antes del return por IsPlacing para que Esc funcione al colocar y dentro de submenús aunque el puntero no esté sobre la UI.
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                var build = FindFirstObjectByType<BuildModeController>();
                if (build != null && build.state != BuildState.Idle)
                    build.Cancel();
                else
                    ClearSelection();
                return;
            }

            // Mientras colocas (edificio o muro por path), no procesar clic izquierdo como selección o se deseleccionan los aldeanos y se sale del modo construcción.
            if (buildingPlacer != null && buildingPlacer.IsPlacing)
				return;
			
            if (mouse == null || cam == null) return;

            // LMB pressed -> start drag
            if (mouse.leftButton.wasPressedThisFrame)
            {
                // CRUCIAL: Ignorar clicks sobre UI (botones, paneles, etc) o sobre el minimapa
                if (UiInputRaycast.IsPointerOverGameObject()
                    || RuntimeMinimapBootstrap.IsPointerOverMinimap)
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

            UpdateResourceHover();
            UpdateBuildingHover();
            UpdateUnitHover();

            // LMB released -> select click or box
            if (_isDragging && mouse.leftButton.wasReleasedThisFrame)
            {
                Vector2 dragEnd = mouse.position.ReadValue();
                _isDragging = false;

                // Ocultar rectángulo al soltar
                selectionBoxUI?.Hide();

                // CRUCIAL: Si el cursor está sobre UI al soltar, cancelar selección
                if (UiInputRaycast.IsPointerOverGameObject())
                {
                    return; // Hay UI bajo el cursor, no procesar selección
                }

                var kbSel = Keyboard.current;
                bool additive = kbSel != null && (
                    (addToSelectionWithShift && (kbSel.leftShiftKey.isPressed || kbSel.rightShiftKey.isPressed)) ||
                    (addToSelectionWithCtrl && (kbSel.leftCtrlKey.isPressed || kbSel.rightCtrlKey.isPressed)));

                // Doble clic: si el segundo clic cae en la ventana temporal y cerca del primero, no tratarlo
                // como selección por caja aunque el ratón se haya movido ~12–20 px al soltar (evita fallos del doble clic).
                float timeSinceLastClick = Time.time - _lastClickTime;
                bool inDoubleClickWindow = _lastClickTime > 0f && timeSinceLastClick <= doubleClickTime &&
                    Vector2.Distance(_lastClickPos, dragEnd) < 24f;
                float dragDist = Vector2.Distance(_dragStart, dragEnd);
                bool isBox = dragDist > 12f && !inDoubleClickWindow;
                bool isDoubleClick = !isBox && inDoubleClickWindow;

                // En doble clic no limpiar aquí; DoubleClickSelect usa ClearSelectionForDoubleClick (no oculta barras de unidades)
                if (!additive && !isDoubleClick) ClearSelection();

                if (isBox)
                {
                    BoxSelect(_dragStart, dragEnd);
                }
                else
                {
                    if (isDoubleClick)
                    {
                        // Si tiene shift, no limpia la selección previa
                        DoubleClickSelect(dragEnd, additive);
                        _lastClickTime = 0f; // resetear para evitar triple clic
                    }
                    else
                    {
                        ClickSelect(dragEnd, additive);
                        _lastClickTime = Time.time;
                        _lastClickPos = dragEnd;
                    }
                }
            }
        }

        static bool ShouldReadPointerInput()
        {
#if UNITY_EDITOR
            // Igual que en RTSOrderController: no mezclar clics de Scene/Inspector con coordenadas de GameView.
            var focused = EditorWindow.focusedWindow;
            var hovered = EditorWindow.mouseOverWindow;
            bool gameFocused = focused != null && focused.GetType().Name == "GameView";
            bool gameHovered = hovered != null && hovered.GetType().Name == "GameView";
            return gameFocused || gameHovered;
#else
            return true;
#endif
        }

        void ClickSelect(Vector2 screenPos, bool additive = false)
        {
            Ray ray = cam.ScreenPointToRay(screenPos);

            // 1) Prioridad: Units (todos los hits: un solo Raycast con el primero sin UnitSelectable bloqueaba árboles/recursos detrás)
            if (unitLayerMask.value != 0)
            {
                var uhits = Physics.RaycastAll(ray, 5000f, unitLayerMask, QueryTriggerInteraction.Collide);
                System.Array.Sort(uhits, (a, b) => a.distance.CompareTo(b.distance));
                for (int ui = 0; ui < uhits.Length; ui++)
                {
                    var u = uhits[ui].collider.GetComponentInParent<UnitSelectable>();
                    if (u != null)
                    {
                        AddSelection(u);
                        return;
                    }
                }
            }

            // Con Ctrl/Shift (additive) no reemplazar selección por edificio ni recurso
            if (additive) return;

            // 2) Luego: Buildings (ignorar triggers: muro compuesto usa un Box trigger grande en el AABB del path;
            // si no, clic en suelo dentro del patio seleccionaba el muro).
            if (Physics.Raycast(ray, out RaycastHit hitB, 5000f, buildingLayerMask, QueryTriggerInteraction.Ignore))
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

            // 3) Recursos (RaycastAll: mismo criterio que hover; un solo Raycast fallaba con colliders finos/solapados)
            if (TryResolveTopmostResourceUnderCursor(ray, out ResourceSelectable resource, out ResourceNode _, out RaycastHit _))
            {
                ClearSelection();
                resource.SetSelected(true);
                _selectedResource = resource;
                SetHealthBarVisibleForEntity(resource.gameObject, true);
                OnSelectionChanged?.Invoke();
            }
        }

        bool TryResolveTopmostResourceUnderCursor(Ray ray, out ResourceSelectable resource, out ResourceNode node, out RaycastHit hit)
        {
            if (resourceLayerMask == 0)
                EnsureResourceLayerMaskFromMap();

            return ResourcePickResolver.TryResolveTopmostResourceUnderCursor(
                ray,
                resourceLayerMask,
                out resource,
                out node,
                out hit,
                debugResourcePicking);
        }

        void DoubleClickSelect(Vector2 screenPos, bool additive = false)
        {
            Ray ray = cam.ScreenPointToRay(screenPos);

            UnitSelectable clickedUnit = null;
            if (unitLayerMask.value != 0)
            {
                var uhits = Physics.RaycastAll(ray, 5000f, unitLayerMask, QueryTriggerInteraction.Collide);
                System.Array.Sort(uhits, (a, b) => a.distance.CompareTo(b.distance));
                for (int ui = 0; ui < uhits.Length; ui++)
                {
                    clickedUnit = uhits[ui].collider.GetComponentInParent<UnitSelectable>();
                    if (clickedUnit != null) break;
                }
            }
            if (clickedUnit == null) return;

            // Solo limpiar selección si NO es aditivo (sin shift).
            // Importante: en doble clic no ocultamos las barras de las unidades antes de reañadirlas,
            // para evitar que la unidad sobre la que se hizo doble clic pierda su barra (Hide + Register
            // en el mismo frame puede dejar ese ente sin barra visible).
            if (!additive)
                ClearSelectionForDoubleClick();

            var allUnits = UnitSelectableRegistry.All;
            for (int i = 0; i < allUnits.Count; i++)
            {
                var u = allUnits[i];
                if (u == null) continue;

                if (!SameUnitTypeForDoubleClick(clickedUnit, u)) continue;

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

            // Asegurar que todas las barras de vida se muestren (doble clic = mismo comportamiento que box select)
            for (int i = 0; i < _selected.Count; i++)
            {
                if (_selected[i] != null)
                    SetHealthBarVisibleForEntity(_selected[i].gameObject, true);
            }
        }

        void BoxSelect(Vector2 start, Vector2 end)
        {
            Rect r = MakeRect(start, end);

            var all = UnitSelectableRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null) continue;
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

            OnSelectionChanged?.Invoke();
        }

        void ClearBuildingAndResource()
        {
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
        }

        /// <summary>Limpia la selección actual (unidades, edificio, recurso). Llamable desde UI (ej. al cancelar construcción).</summary>
        public void ClearSelection()
        {
            ClearResourceHover();
            ClearBuildingHover();
            ClearUnitHover();
            bool hadSelection = _selected.Count > 0 || _selectedBuilding != null || _selectedResource != null;

            ClearBuildingAndResource();

            for (int i = 0; i < _selected.Count; i++)
            {
                if (_selected[i] != null)
                {
                    SetHealthBarVisibleForEntity(_selected[i].gameObject, false);
                    _selected[i].SetSelected(false);
                }
            }
            _selected.Clear();
            if (hadSelection) OnSelectionChanged?.Invoke();
        }

        /// <summary>Limpia edificio/recurso y la lista de unidades sin ocultar barras de vida. Usado en doble clic para evitar que la unidad del primer clic pierda la barra.</summary>
        void ClearSelectionForDoubleClick()
        {
            ClearResourceHover();
            ClearBuildingHover();
            ClearUnitHover();
            bool hadSelection = _selected.Count > 0 || _selectedBuilding != null || _selectedResource != null;

            ClearBuildingAndResource();

            for (int i = 0; i < _selected.Count; i++)
            {
                if (_selected[i] != null)
                    _selected[i].SetSelected(false);
            }
            _selected.Clear();
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
			return CountSelectedPlayerVillagers() > 0;
		}

        /// <summary>Aldeanos del jugador seleccionados (excluye enemigos; sirve para construcción y órdenes).</summary>
        public int CountSelectedPlayerVillagers()
        {
            int c = 0;
            for (int i = 0; i < _selected.Count; i++)
            {
                var u = _selected[i];
                if (u == null || !u.IsVillager) continue;
                if (FactionMember.IsPlayerCommandable(u.gameObject)) c++;
            }
            return c;
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
            if (resourceLayerMask == 0)
                EnsureResourceLayerMaskFromMap();
            if (resourceLayerMask == 0 || cam == null)
            {
                ClearResourceHover();
                return;
            }

            if (resourceHoverRequiresSelectedVillager && !HasSelectedVillagers())
            {
                ClearResourceHover();
                return;
            }

            if (UiInputRaycast.IsPointerOverGameObject())
            {
                ClearResourceHover();
                return;
            }

            // Throttle: raycast cada 2 frames para reducir coste
            _resourceHoverFrameSkip++;
            if ((_resourceHoverFrameSkip & 1) != 0)
                return;

            var mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            Ray ray = cam.ScreenPointToRay(mousePos);
            ResourceSelectable res = null;
            if (TryResolveTopmostResourceUnderCursor(ray, out ResourceSelectable resolved, out ResourceNode _, out RaycastHit _))
                res = resolved;
            if (res == null)
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
                if (resourceHoverDelay <= 0f)
                    res.SetHovered(true);
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

        void UpdateBuildingHover()
        {
            if (buildingLayerMask == 0 || cam == null)
            {
                ClearBuildingHover();
                return;
            }
            if (UiInputRaycast.IsPointerOverGameObject())
            {
                ClearBuildingHover();
                return;
            }
            _buildingHoverFrameSkip++;
            if ((_buildingHoverFrameSkip & 1) != 0) return;

            var mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            Ray ray = cam.ScreenPointToRay(mousePos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 5000f, buildingLayerMask, QueryTriggerInteraction.Ignore))
            {
                ClearBuildingHover();
                return;
            }
            var building = hit.collider.GetComponentInParent<BuildingSelectable>();
            if (building == null)
            {
                ClearBuildingHover();
                return;
            }
            if (building == _hoveredBuilding)
                building.SetHovered(true);
            else
            {
                ClearBuildingHover();
                _hoveredBuilding = building;
                building.SetHovered(true);
            }
        }

        void ClearBuildingHover()
        {
            if (_hoveredBuilding != null)
            {
                _hoveredBuilding.SetHovered(false);
                _hoveredBuilding = null;
            }
        }

        void UpdateUnitHover()
        {
            if (unitLayerMask == 0 || cam == null)
            {
                ClearUnitHover();
                return;
            }
            if (UiInputRaycast.IsPointerOverGameObject())
            {
                ClearUnitHover();
                return;
            }
            _unitHoverFrameSkip++;
            if ((_unitHoverFrameSkip & 1) != 0) return;

            var mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            Ray ray = cam.ScreenPointToRay(mousePos);
            var uhits = Physics.RaycastAll(ray, 5000f, unitLayerMask, QueryTriggerInteraction.Collide);
            System.Array.Sort(uhits, (a, b) => a.distance.CompareTo(b.distance));
            UnitSelectable unit = null;
            for (int ui = 0; ui < uhits.Length; ui++)
            {
                unit = uhits[ui].collider.GetComponentInParent<UnitSelectable>();
                if (unit != null) break;
            }
            if (unit == null)
            {
                ClearUnitHover();
                return;
            }
            if (unit == _hoveredUnit)
                unit.SetHovered(true);
            else
            {
                ClearUnitHover();
                _hoveredUnit = unit;
                unit.SetHovered(true);
            }
        }

        void ClearUnitHover()
        {
            if (_hoveredUnit != null)
            {
                _hoveredUnit.SetHovered(false);
                _hoveredUnit = null;
            }
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

        /// <summary>
        /// Mismo tipo para doble clic: preferir <see cref="UnitStatsRuntime.ResolvedUnitDefinition"/> (UnitSO)
        /// para que coincida aunque los nombres de instancia difieran; si no hay SO en ambos, comparar nombre base.
        /// </summary>
        static bool SameUnitTypeForDoubleClick(UnitSelectable a, UnitSelectable b)
        {
            if (a == null || b == null) return false;
            var sa = a.GetComponent<UnitStatsRuntime>();
            var sb = b.GetComponent<UnitStatsRuntime>();
            var defA = sa != null ? sa.ResolvedUnitDefinition : null;
            var defB = sb != null ? sb.ResolvedUnitDefinition : null;
            if (defA != null && defB != null)
                return defA == defB;
            return GetBaseInstanceName(a.gameObject.name) == GetBaseInstanceName(b.gameObject.name);
        }

        static string GetBaseInstanceName(string goName)
        {
            if (string.IsNullOrEmpty(goName)) return string.Empty;
            string s = goName.Trim();
            const string cloneSuffix = "(Clone)";
            if (s.EndsWith(cloneSuffix, System.StringComparison.Ordinal))
                s = s.Substring(0, s.Length - cloneSuffix.Length).TrimEnd();
            int paren = s.IndexOf('(');
            if (paren >= 0)
                s = s.Substring(0, paren).TrimEnd();
            return s;
        }
    }
}