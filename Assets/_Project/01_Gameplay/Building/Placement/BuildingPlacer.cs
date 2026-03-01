using UnityEngine;
using UnityEngine.InputSystem;
using Project.Gameplay.Players;
using Project.Gameplay.Map;
using UnityEngine.EventSystems;

namespace Project.Gameplay.Buildings
{
    public class BuildingPlacer : MonoBehaviour
    {
        [Header("Grid")]
        public bool snapToGrid = true;
        [Tooltip("Referencia al ScriptableObject con configuración del grid. Se sincroniza automáticamente con MapGrid.")]
        public GridConfig gridConfig;
        [Tooltip("Tamaño de celda en metros (auto-sync con MapGrid si existe). 2.5 es típico para RTS.")]
        public float gridSize = 2.5f;

        [Header("Rotation")]
        public float rotationStep = 90f;
        private float _currentYaw = 0f;

        [Header("Refs")]
        public Camera cam;
        public LayerMask groundMask;
        [Tooltip("Opcional: si el edificio queda en Y=0 o en 0,0, se usa para ponerlo sobre el terreno.")]
        public Terrain terrain;

        [Header("Blocking")]
        public LayerMask blockingMask;

        [Header("Owner")]
        public PlayerResources owner;

        [Header("Build")]
        public BuildingSO selectedBuilding;

        [Header("Build Site")]
        [SerializeField] GameObject buildSitePrefab; // prefab con BuildSite + visual fundación

        private GameObject _ghost;
        private GhostPreview _ghostPreview;
        private float _ghostPivotToBottom = 0f;
        private bool _isPlacing;
        private float _terrainResolveTimer;

        public BuildSite LastPlacedSite { get; private set; }
        public bool IsPlacing => _isPlacing;

        [Header("Selection Gate")]
        public Project.Gameplay.Units.RTSSelectionController selection;

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (owner == null) owner = FindFirstObjectByType<PlayerResources>();
            if (selection == null) selection = FindFirstObjectByType<Project.Gameplay.Units.RTSSelectionController>();
            if (terrain == null) terrain = FindFirstObjectByType<Terrain>();

            RefreshGridSize();
        }

        /// <summary>Usa MapGrid.cellSize si existe (Play), si no gridConfig.gridSize, si no gridSize. Una sola fuente de verdad para el snap.</summary>
        void RefreshGridSize()
        {
            if (MapGrid.Instance != null && MapGrid.Instance.IsReady)
                gridSize = MapGrid.Instance.cellSize;
            else if (gridConfig != null)
                gridSize = gridConfig.gridSize;
        }

        void Update()
        {
            // Grid size: actualizar solo si MapGrid está listo y cambió (evita trabajo por frame).
            if (MapGrid.Instance != null && MapGrid.Instance.IsReady)
            {
                float cs = MapGrid.Instance.cellSize;
                if (Mathf.Abs(gridSize - cs) > 0.001f) gridSize = cs;
            }

            // Terrain puede crearse en runtime; resolverlo con throttle (no cada frame).
            if (terrain == null)
            {
                _terrainResolveTimer -= Time.unscaledDeltaTime;
                if (_terrainResolveTimer <= 0f)
                {
                    _terrainResolveTimer = 0.75f;
                    terrain = FindFirstObjectByType<Terrain>();
                }
            }

            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null || cam == null) return;
			
			if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
				return;


              // Cancelar con ESC
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                Cancel();
                return;
            }

            if (!_isPlacing || selectedBuilding == null) return;

            // Rotar con Q/E
            if (kb != null && kb.qKey.wasPressedThisFrame) _currentYaw -= rotationStep;
            if (kb != null && kb.eKey.wasPressedThisFrame) _currentYaw += rotationStep;

            // Raycast al suelo
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 5000f, groundMask))
                return;

            Vector3 p = hit.point;
            if (snapToGrid)
            {
                Vector3 origin = (MapGrid.Instance != null && MapGrid.Instance.IsReady) ? MapGrid.Instance.origin : Vector3.zero;
                int bw = 1, bh = 1;
                if (selectedBuilding != null)
                {
                    // BuildingSO.size puede ser Vector2 (float) o Vector2Int (int). RoundToInt funciona en ambos casos.
                    bw = Mathf.Max(1, Mathf.RoundToInt(selectedBuilding.size.x));
                    bh = Mathf.Max(1, Mathf.RoundToInt(selectedBuilding.size.y));
                }

                // Compatibilidad: 1x1 mantiene el snap clásico a intersección.
                if (bw == 1 && bh == 1)
                    p = GridSnapUtil.SnapToGridIntersection(p, origin, gridSize);
                else
                    p = GridSnapUtil.SnapToBuildingGrid(p, origin, gridSize, bw, bh);
            }
            // Siempre usar altura del Terrain si existe (evita edificios en Y=0 cuando el raycast golpea otro collider)
            if (terrain != null)
            {
                p.y = terrain.SampleHeight(new Vector3(p.x, 0f, p.z)) + terrain.transform.position.y;
            }
            else if (p.y < 0.1f)
            {
                // Sin Terrain y Y casi 0: probable fallo de raycast; no colocar
                if (mouse.leftButton.wasPressedThisFrame)
                    Debug.LogWarning("BuildingPlacer: Terrain no encontrado y altura ~0. ¿Terrain en escena y groundMask correcto?");
                return;
            }

            // Rechazar colocación en 0,0 o muy cerca (groundMask debe incluir la capa del Terrain)
            if (p.sqrMagnitude < 9f)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                    Debug.LogWarning("BuildingPlacer: posición cerca de (0,0,0). ¿Ground Mask incluye la capa del Terrain?");
                return;
            }

            // Debe haber al menos un aldeano seleccionado para poder construir
            bool hasVillagers = selection != null && selection.HasSelectedVillagers();
            bool hasSitePrefab = buildSitePrefab != null;

            bool validPlace = PlacementValidator.IsValidPlacement(p, selectedBuilding.size, blockingMask);
            bool canPay = CanAfford(selectedBuilding);

            bool valid = validPlace && canPay && hasSitePrefab && hasVillagers;

            // Actualizar ghost
            if (_ghost != null)
            {
                _ghost.transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
                Vector3 ghostPos = p;
                // Elevar/ajustar el ghost según su base visual para que no se vea enterrado.
                ghostPos.y += _ghostPivotToBottom;
                _ghost.transform.position = ghostPos;
            }

            _ghostPreview?.SetValid(valid);



            // Colocar con clic izquierdo
			if (mouse.leftButton.wasPressedThisFrame && valid)
			{
				if (TryPlaceBuildSite(p, out var site))
				{
					LastPlacedSite = site;
					Debug.Log($"🏗️ Fundación creada: {selectedBuilding.id} en {p}");

					BeginAt(p, Quaternion.Euler(0f, _currentYaw, 0f));
					AutoAssignBuilders(site);
				}
				else
				{
					Debug.LogWarning("No se pudo crear BuildSite (revisa Console).");
				}
			}

            // Cancelar con clic derecho
            if (mouse.rightButton.wasPressedThisFrame)
                Cancel();
        }

        public void Begin()
        {
            if (selectedBuilding == null || selectedBuilding.prefab == null)
            {
                Debug.LogWarning("BuildingPlacer: No hay edificio seleccionado o el prefab es null.");
                return;
            }

            _isPlacing = true;
            _currentYaw = 0f;

            if (_ghost != null) Destroy(_ghost);

            _ghost = Instantiate(selectedBuilding.prefab);
            _ghost.name = $"[GHOST] {selectedBuilding.id}";

            MakeGhostSafe(_ghost);
            _ghostPivotToBottom = ComputePivotToBottomOffset(_ghost);

            _ghostPreview = _ghost.AddComponent<GhostPreview>();
            _ghostPreview.Initialize();
        }

        void BeginAt(Vector3 pos, Quaternion rot)
        {
            if (selectedBuilding == null || selectedBuilding.prefab == null) return;

            _isPlacing = true;

            if (_ghost != null) Destroy(_ghost);

            _ghost = Instantiate(selectedBuilding.prefab, pos, rot);
            _ghost.name = $"[GHOST] {selectedBuilding.id}";

            MakeGhostSafe(_ghost);
            _ghostPivotToBottom = ComputePivotToBottomOffset(_ghost);

            _ghostPreview = _ghost.AddComponent<GhostPreview>();
            _ghostPreview.Initialize();
        }

        public void Cancel()
        {
            if (_ghost != null)
            {
                Destroy(_ghost);
                _ghost = null;
            }

            _ghostPreview = null;
            _ghostPivotToBottom = 0f;
            _isPlacing = false;
            _currentYaw = 0f;
        }

        bool TryPlaceBuildSite(Vector3 position, out BuildSite site)
		{
			site = null;

			if (selectedBuilding == null || owner == null) return false;

			// Evitar colocar en origen por si algo pasó la validación de Update
			if (position.sqrMagnitude < 9f)
			{
				Debug.LogWarning("BuildingPlacer: TryPlaceBuildSite rechazado (posición cerca de 0,0,0).");
				return false;
			}

			if (buildSitePrefab == null)
			{
				Debug.LogError("BuildingPlacer: buildSitePrefab no está asignado.");
				return false;
			}

			if (!CanAfford(selectedBuilding))
				return false;

			Quaternion rot = Quaternion.Euler(0f, _currentYaw, 0f);
			GameObject go = Instantiate(buildSitePrefab, position, rot);
            AlignBuildSiteToTerrain(go);
			go.name = $"[SITE] {selectedBuilding.id}";

			site = go.GetComponent<BuildSite>();
			if (site == null)
			{
				Debug.LogError("BuildingPlacer: buildSitePrefab NO tiene componente BuildSite.");
				Destroy(go);
				return false;
			}

			// ✅ SOLO AHORA descuento (cuando ya sé que el site existe)
			foreach (var cost in selectedBuilding.costs)
				owner.Subtract(cost.kind, cost.amount);

			// Configurar site
			site.buildingSO = selectedBuilding;
			site.finalPrefab = selectedBuilding.prefab;
			site.buildTime = GetBuildTime(selectedBuilding);

			Debug.Log($"SITE CONFIG -> name={go.name} id={go.GetInstanceID()} so={(site.buildingSO ? site.buildingSO.id : "null")} final={(site.finalPrefab != null)} buildTime={site.buildTime}");

			// 🟢 OCUPAR CELDAS EN EL GRID (crítico para RTS)
			OccupyCells(position, selectedBuilding.size, true);

			return true;
		}

        void AlignBuildSiteToTerrain(GameObject siteGo)
        {
            if (siteGo == null || terrain == null) return;

            Vector3 pivotPos = siteGo.transform.position;
            float terrainY = terrain.SampleHeight(pivotPos) + terrain.transform.position.y;
            float bottomY = float.MaxValue;
            bool foundBounds = false;

            var renderers = siteGo.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled) continue;
                if (r.gameObject.GetComponent<Canvas>() != null) continue;
                bottomY = Mathf.Min(bottomY, r.bounds.min.y);
                foundBounds = true;
            }

            if (!foundBounds)
            {
                var colliders = siteGo.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    var c = colliders[i];
                    if (c == null || !c.enabled) continue;
                    bottomY = Mathf.Min(bottomY, c.bounds.min.y);
                    foundBounds = true;
                }
            }

            if (!foundBounds) return;

            float delta = terrainY - bottomY;
            if (Mathf.Abs(delta) < 0.0001f) return;
            Vector3 p = siteGo.transform.position;
            p.y += delta;
            siteGo.transform.position = p;
        }

		/// <summary>Marca/desmarca celdas ocupadas en MapGrid según footprint del edificio.</summary>
		void OccupyCells(Vector3 worldPos, Vector2 footprintSize, bool occupy)
		{
			if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return;

			Vector2Int center = MapGrid.Instance.WorldToCell(worldPos);
			Vector2Int size = new Vector2Int(
				Mathf.Max(1, Mathf.RoundToInt(footprintSize.x)),
				Mathf.Max(1, Mathf.RoundToInt(footprintSize.y))
			);
			Vector2Int min = new Vector2Int(center.x - size.x / 2, center.y - size.y / 2);

			MapGrid.Instance.SetOccupiedRect(min, size, occupy);

			Debug.Log($"🔲 Grid: {(occupy ? "Ocupado" : "Liberado")} {size.x}×{size.y} en {center}");
		}


        float GetBuildTime(BuildingSO so)
        {
            // Ideal: agrega buildTimeSeconds en BuildingSO. Mientras, fijo.
            try
            {
                var f = so.GetType().GetField("buildTimeSeconds");
                if (f != null)
                {
                    object v = f.GetValue(so);
                    if (v is float ft && ft > 0.01f) return ft;
                }
            }
            catch { }
            return 10f;
        }

        public bool CanAfford(BuildingSO building)
        {
            if (building == null || owner == null) return false;
            if (building.costs == null || building.costs.Length == 0) return true;

            foreach (var cost in building.costs)
            {
                if (!owner.Has(cost.kind, cost.amount))
                    return false;
            }
            return true;
        }

        // Snap legacy (sin origin). Se mantiene por compatibilidad, pero el flujo principal usa GridSnapUtil.
        static Vector3 Snap(Vector3 p, float grid)
        {
            if (grid <= 0.0001f) return p;
            p.x = Mathf.Round(p.x / grid) * grid;
            p.z = Mathf.Round(p.z / grid) * grid;
            return p;
        }

        static void MakeGhostSafe(GameObject root)
        {
            if (root == null) return;

            int ghostLayer = LayerMask.NameToLayer("Ghost");
            if (ghostLayer != -1)
                SetLayerRecursive(root.transform, ghostLayer);

            foreach (var c in root.GetComponentsInChildren<Collider>(true))
                c.enabled = false;

            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                mb.enabled = false;

            foreach (var o in root.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true))
            {
                o.carving = false;
                o.enabled = false;
            }

            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }
        }

        static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i), layer);
        }

        static float ComputePivotToBottomOffset(GameObject go)
        {
            if (go == null) return 0f;
            float bottomY = float.MaxValue;
            bool found = false;

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled) continue;
                if (r.gameObject.GetComponent<Canvas>() != null) continue;
                bottomY = Mathf.Min(bottomY, r.bounds.min.y);
                found = true;
            }

            if (!found)
            {
                var colliders = go.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    var c = colliders[i];
                    if (c == null || !c.enabled) continue;
                    bottomY = Mathf.Min(bottomY, c.bounds.min.y);
                    found = true;
                }
            }

            if (!found) return 0f;
            return go.transform.position.y - bottomY;
        }
		
		void AutoAssignBuilders(BuildSite site)
		{
			int assigned = 0;
			if (selection != null)
			{
				var selected = selection.GetSelected();
				for (int i = 0; i < selected.Count; i++)
				{
					var builder = selected[i].GetComponent<Project.Gameplay.Units.Builder>();
					if (builder != null)
					{
						builder.SetBuildTarget(site);
						assigned++;
					}
				}
			}
			// Si no había aldeanos seleccionados, asignar el más cercano al sitio
			if (assigned == 0 && site != null)
			{
				var allBuilders = Object.FindObjectsByType<Project.Gameplay.Units.Builder>(FindObjectsSortMode.None);
				Vector3 sitePos = site.transform.position;
				Project.Gameplay.Units.Builder nearest = null;
				float nearestDist = float.MaxValue;
				for (int i = 0; i < allBuilders.Length; i++)
				{
					float d = (allBuilders[i].transform.position - sitePos).sqrMagnitude;
					if (d < nearestDist)
					{
						nearestDist = d;
						nearest = allBuilders[i];
					}
				}
				if (nearest != null)
				{
					nearest.SetBuildTarget(site);
					Debug.Log($"BuildingPlacer: sin aldeanos seleccionados, asignado el más cercano a {site.name}");
				}
			}
		}

    }
}
