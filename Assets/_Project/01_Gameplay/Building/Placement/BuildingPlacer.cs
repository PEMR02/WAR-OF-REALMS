using UnityEngine;
using UnityEngine.InputSystem;
using Project.Gameplay.Players;
using UnityEngine.EventSystems;

namespace Project.Gameplay.Buildings
{
    public class BuildingPlacer : MonoBehaviour
    {
        [Header("Grid")]
        public bool snapToGrid = true;
        public GridConfig gridConfig;
        public float gridSize = 1f;

        [Header("Rotation")]
        public float rotationStep = 90f;
        private float _currentYaw = 0f;

        [Header("Refs")]
        public Camera cam;
        public LayerMask groundMask;

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
        private bool _isPlacing;

        public BuildSite LastPlacedSite { get; private set; }
        public bool IsPlacing => _isPlacing;

        [Header("Selection Gate")]
        public Project.Gameplay.Units.RTSSelectionController selection;

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (owner == null) owner = FindFirstObjectByType<PlayerResources>();
            if (selection == null) selection = FindFirstObjectByType<Project.Gameplay.Units.RTSSelectionController>();

            if (gridConfig != null)
                gridSize = gridConfig.gridSize;
        }

        void Update()
        {
            if (gridConfig != null)
                gridSize = gridConfig.gridSize;

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
            p.y = 0f;

            if (snapToGrid) p = Snap(p, gridSize);

            // Gate: debe haber aldeanos seleccionados
            bool hasVillagers = selection != null &&
                                selection.CountSelectedWithComponent<Project.Gameplay.Units.VillagerGatherer>() > 0;

            bool hasSitePrefab = buildSitePrefab != null;

            // Validar colocación
            bool validPlace = PlacementValidator.IsValidPlacement(p, selectedBuilding.size, blockingMask);
            bool canPay = CanAfford(selectedBuilding);

            bool valid = validPlace && canPay && hasSitePrefab && hasVillagers;

            // Actualizar ghost
            if (_ghost != null)
            {
                _ghost.transform.position = p;
                _ghost.transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
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
            _isPlacing = false;
            _currentYaw = 0f;
        }

        bool TryPlaceBuildSite(Vector3 position, out BuildSite site)
		{
			site = null;

			if (selectedBuilding == null || owner == null) return false;

			if (buildSitePrefab == null)
			{
				Debug.LogError("BuildingPlacer: buildSitePrefab no está asignado.");
				return false;
			}

			if (!CanAfford(selectedBuilding))
				return false;

			Quaternion rot = Quaternion.Euler(0f, _currentYaw, 0f);
			GameObject go = Instantiate(buildSitePrefab, position, rot);
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

			return true;
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
		
		void AutoAssignBuilders(BuildSite site)
		{
			if (selection == null) return;

			var selected = selection.GetSelected();
			for (int i = 0; i < selected.Count; i++)
			{
				var builder = selected[i].GetComponent<Project.Gameplay.Units.Builder>();
				if (builder != null)
				{
					// Si tienes UnitMover, perfecto (tu mover expone MoveTo)
					// Builder actualmente usa NavMeshAgent directo, igual sirve.
					builder.SetBuildTarget(site);
				}
			}
		}

    }
}
