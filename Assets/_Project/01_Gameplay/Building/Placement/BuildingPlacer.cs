using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Project.Gameplay.Faction;
using Project.Gameplay.Players;
using Project.Gameplay.Map;
using Project.Gameplay.Units;
using Project.UI;

namespace Project.Gameplay.Buildings
{
    public class BuildingPlacer : MonoBehaviour
    {
        [Header("Grid")]
        public bool snapToGrid = true;

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
        [Tooltip("Capas de unidades a desplazar fuera del área de construcción (ej. Unit). 0 = no desplazar. Aplica a edificios y muros.")]
        public LayerMask unitDisplacementMask = 0;

        [Header("Terrain (footprint) — estilo Anno: edificios apoyados, sin flotar")]
        [Tooltip("Diferencia máxima de altura (m) entre puntos del footprint. 1.5–2 = más plano tipo Anno.")]
        public float maxHeightDelta = 1.8f;
        [Tooltip("Pendiente máxima en grados (0 = no validar). 12–18° típico para zonas construibles.")]
        [Range(0f, 45f)] public float maxSlopeDegrees = 14f;

        [Header("Owner")]
        public PlayerResources owner;

        [Header("Build")]
        public BuildingSO selectedBuilding;

        [Header("Build Site")]
        [SerializeField] GameObject buildSitePrefab; // prefab con BuildSite + visual fundación

        [Header("Debug")]
        public bool debugLogs = false;

        private GameObject _ghost;
        private GhostPreview _ghostPreview;
        private float _ghostPivotToBottom = 0f;
        private bool _isPlacing;
        private float _terrainResolveTimer;

        [Header("Path (muros orgánicos)")]
        [Tooltip("Tecla para confirmar el path del muro (añadir puntos con clic, confirmar con esta tecla).")]
        public UnityEngine.InputSystem.Key pathConfirmKey = UnityEngine.InputSystem.Key.Enter;
        [Tooltip("Capa(s) que bloquean añadir punto: si el clic impacta aquí (ej. Unidad, Edificio), no se añade punto y la unidad puede recibir órdenes. 0 = no bloquear.")]
        public LayerMask pathBlockingLayers = 0;
        [Tooltip("Grosor de la línea de preview del path (más visible).")]
        [Range(0.05f, 0.5f)] public float pathPreviewLineWidth = 0.25f;
        [Tooltip("Permitir confirmar path con doble clic (además de Enter).")]
        public bool pathConfirmWithDoubleClick = true;
        [Tooltip("Distancia mínima XZ entre puntos consecutivos del path (además de cellSize*0.5). Evita duplicados por doble clic / snap.")]
        [SerializeField] float minWallPathPointSpacingFloor = 0.75f;
        [Tooltip("Longitud mínima XZ de cada tramo entre puntos al confirmar el muro (además de cellSize*0.35).")]
        [SerializeField] float minWallSegmentLengthFloor = 1f;
        private readonly List<Vector3> _pathPoints = new List<Vector3>(32);
        private readonly List<int> _pathGatePointIndices = new List<int>(8);
        private bool _isPlacingPath;
        private LineRenderer _pathPreviewLine;
        private List<GameObject> _pathGhostSegments = new List<GameObject>(64);
        private float _pathLastClickTime = -1f;
        private Vector2 _pathLastClickPos;

        public BuildSite LastPlacedSite { get; private set; }
        public bool IsPlacing => _isPlacing;
        public bool IsPlacingPath => _isPlacingPath;
        public int PathPointCount => _pathPoints.Count;

        [Header("Selection Gate")]
        public Project.Gameplay.Units.RTSSelectionController selection;

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (owner == null) owner = PlayerResources.FindPrimaryHumanSkirmish();
            if (selection == null) selection = FindFirstObjectByType<Project.Gameplay.Units.RTSSelectionController>();
            if (terrain == null) terrain = FindFirstObjectByType<Terrain>();

        }

        float GetCellSize()
        {
            return MapGrid.GetCellSizeOrDefault();
        }

        float GetMinWallPathPointSpacing()
        {
            float cs = GetCellSize();
            return Mathf.Max(minWallPathPointSpacingFloor, cs * 0.5f);
        }

        float GetMinWallSegmentLength()
        {
            float cs = GetCellSize();
            return Mathf.Max(minWallSegmentLengthFloor, cs * 0.35f);
        }

        static float SqrDistXZPath(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>Quita puntos consecutivos duplicados o demasiado cercanos y reasigna índices de puerta.</summary>
        bool TrySanitizeWallPathForConfirm(out List<Vector3> cleaned, out List<int> cleanedGateIndices)
        {
            cleaned = new List<Vector3>(_pathPoints.Count);
            cleanedGateIndices = new List<int>(8);
            float minPt = GetMinWallPathPointSpacing();
            float minSeg = GetMinWallSegmentLength();

            if (_pathPoints.Count == 0)
                return false;

            var srcToCleaned = new int[_pathPoints.Count];
            for (int i = 0; i < srcToCleaned.Length; i++)
                srcToCleaned[i] = -1;

            cleaned.Add(_pathPoints[0]);
            srcToCleaned[0] = 0;

            for (int i = 1; i < _pathPoints.Count; i++)
            {
                Vector3 last = cleaned[cleaned.Count - 1];
                float sqr = SqrDistXZPath(_pathPoints[i], last);
                if (sqr < minPt * minPt)
                {
                    srcToCleaned[i] = cleaned.Count - 1;
                    Debug.Log($"[WallBuild] ignored duplicate/too-close path point pos={_pathPoints[i]} last={last} distXZ={Mathf.Sqrt(sqr):F3} min={minPt:F3}");
                    continue;
                }
                cleaned.Add(_pathPoints[i]);
                srcToCleaned[i] = cleaned.Count - 1;
            }

            for (int g = 0; g < _pathGatePointIndices.Count; g++)
            {
                int oldIdx = _pathGatePointIndices[g];
                if (oldIdx < 0 || oldIdx >= srcToCleaned.Length) continue;
                int ni = srcToCleaned[oldIdx];
                if (ni < 0) continue;
                if (!cleanedGateIndices.Contains(ni))
                    cleanedGateIndices.Add(ni);
            }

            if (cleaned.Count < 2)
            {
                Debug.LogWarning($"[WallBuild] path confirm rejected: after dedupe fewer than 2 points (had {_pathPoints.Count}).");
                return false;
            }

            for (int i = 0; i < cleaned.Count - 1; i++)
            {
                float leg = Mathf.Sqrt(SqrDistXZPath(cleaned[i], cleaned[i + 1]));
                if (leg < minSeg)
                {
                    Debug.LogWarning($"[WallBuild] path confirm rejected: segment {i}→{i + 1} too short legXZ={leg:F3} min={minSeg:F3} a={cleaned[i]} b={cleaned[i + 1]}");
                    return false;
                }
            }

            return true;
        }

        void Update()
        {
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

            // En modo path (muro) no bloquear todo por UI, para que se dibuje la línea y el fantasma
            if (!_isPlacingPath && UiInputRaycast.IsPointerOverGameObject())
                return;

            // ESC lo gestiona RTSSelectionController (llama a BuildModeController.Cancel) para no desincronizar estado.

            // Modo path (muros/cercas orgánicos): varios clics + confirmar
            if (_isPlacingPath && selectedBuilding != null)
            {
                UpdatePathPlacement(mouse, kb, cam);
                return;
            }

            if (!_isPlacing || selectedBuilding == null) return;

            // Rotar con Q/E (Q = horario en Y, E = antihorario; intercambiado respecto a la cámara RTS)
            if (kb != null && kb.qKey.wasPressedThisFrame) _currentYaw += rotationStep;
            if (kb != null && kb.eKey.wasPressedThisFrame) _currentYaw -= rotationStep;

            // Raycast al suelo
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 5000f, groundMask))
                return;

            Vector3 p = hit.point;
            float cellSize = GetCellSize();
            int bw = Mathf.Max(1, Mathf.RoundToInt(selectedBuilding.size.x));
            int bh = Mathf.Max(1, Mathf.RoundToInt(selectedBuilding.size.y));
            if (snapToGrid)
            {
                // Puerta: usar MapGrid para centrar en celda (misma grilla que el muro).
                if (selectedBuilding != null && selectedBuilding.id == GateBuildingId && MapGrid.Instance != null && MapGrid.Instance.IsReady)
                {
                    Vector2Int cell = MapGrid.Instance.WorldToCell(p);
                    p = MapGrid.Instance.CellToWorld(cell);
                }
                else
                {
                    Vector3 origin = (MapGrid.Instance != null && MapGrid.Instance.IsReady) ? MapGrid.Instance.origin : Vector3.zero;
                    if (bw == 1 && bh == 1)
                        p = GridSnapUtil.SnapToGridIntersection(p, origin, cellSize);
                    else
                        p = GridSnapUtil.SnapToBuildingGrid(p, origin, cellSize, bw, bh);
                }
            }
            // Altura: footprint completo (FootprintTerrainSampler + TerrainPlacementValidator); BuildingAnchorSolver = única fuente de Y para ghost y site
            bool validTerrain = true;
            float ghostPivotY = p.y + _ghostPivotToBottom; // fallback sin terreno
            if (terrain != null)
            {
                var sample = FootprintTerrainSampler.Sample(terrain, p, new Vector2(bw, bh), _currentYaw);
                validTerrain = TerrainPlacementValidator.IsValid(sample, maxHeightDelta, maxSlopeDegrees, new Vector2(bw, bh));
                BuildingAnchorSolver.Solve(sample, _ghostPivotToBottom, out float placementY, out float visualOffsetY);
                p.y = placementY;
                ghostPivotY = placementY + visualOffsetY;
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

            bool valid = validPlace && validTerrain && canPay && hasSitePrefab && hasVillagers;

            // Actualizar ghost: pivot Y = ghostPivotY (ya calculado con BuildingAnchorSolver cuando hay terreno)
            if (_ghost != null)
            {
                _ghost.transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
                Vector3 ghostPos = p;
                ghostPos.y = ghostPivotY;
                _ghost.transform.position = ghostPos;
            }

            _ghostPreview?.SetValid(valid);



            // Colocar con clic izquierdo
			if (mouse.leftButton.wasPressedThisFrame)
			{
				// Puerta: si edificio seleccionado es puerta y clic en muro, reemplazar ese tramo por el prefab de la puerta.
				if (selectedBuilding != null && selectedBuilding.id == GateBuildingId && selectedBuilding.prefab != null && CanAfford(selectedBuilding))
				{
					if (TryReplaceWallSegmentWithGate(ray))
					{
						foreach (var cost in selectedBuilding.costs)
							owner.Subtract(cost.kind, cost.amount);
						if (debugLogs) Debug.Log("Puerta colocada reemplazando segmento del muro.");
						Cancel();
						return;
					}
				}
				if (valid)
				{
					if (TryPlaceBuildSite(p, out var site))
					{
						LastPlacedSite = site;
						if (debugLogs) Debug.Log($"🏗️ Fundación creada: {selectedBuilding.id} en {p}");

						BeginAt(p, Quaternion.Euler(0f, _currentYaw, 0f));
						AutoAssignBuilders(site);
					}
					else
					{
						Debug.LogWarning("No se pudo crear BuildSite (revisa Console).");
					}
				}
			}

            // Cancelar con clic derecho
            if (mouse.rightButton.wasPressedThisFrame)
                Cancel();
        }

        public void Begin()
        {
            if (selectedBuilding == null)
            {
                Debug.LogWarning("BuildingPlacer: No hay edificio seleccionado.");
                return;
            }

            _isPlacing = true;
            _currentYaw = 0f;
            _pathPoints.Clear();

            // Muro/cerca por path: no requiere prefab principal, solo compoundSegmentPrefab
            if (selectedBuilding.isCompound && selectedBuilding.compoundPathMode)
            {
                _isPlacingPath = true;
                DestroyPathPreview();
                EnsurePathPreview();
                Debug.Log($"BuildingPlacer: Modo path activado para '{selectedBuilding.id}'. Clic en el terreno = añadir punto, {pathConfirmKey} o doble clic = confirmar, derecho = quitar punto, ESC = cancelar.");
                return;
            }

            if (selectedBuilding.prefab == null)
            {
                Debug.LogWarning($"BuildingPlacer: El edificio '{selectedBuilding.id}' no tiene prefab asignado.");
                _isPlacing = false;
                return;
            }

            _isPlacingPath = false;
            if (_ghost != null) Destroy(_ghost);

            try
            {
                if (!(selectedBuilding.prefab is GameObject go) || go == null)
                {
                    Debug.LogWarning($"BuildingPlacer: El prefab de '{selectedBuilding.id}' no es un GameObject válido. Asigna el prefab en el BuildingSO desde el Inspector.");
                    return;
                }
                _ghost = Instantiate(go);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"BuildingPlacer: No se pudo instanciar el edificio '{selectedBuilding.id}'. Revisa que el prefab en el BuildingSO sea correcto.\n{e}");
                return;
            }
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
            _isPlacingPath = false;
            _pathPoints.Clear();
            _pathGatePointIndices.Clear();
            DestroyPathPreview();
            _currentYaw = 0f;
        }

        /// <summary>ID del BuildingSO de la puerta (colocar puerta = reemplazar segmento de muro; usar Muro_Puerta_SO).</summary>
        const string GateBuildingId = "Muro_Puerta";
        /// <summary>Segmentos del muro que ocupa la puerta (la puerta reemplaza este número de bloques para que coincida el espacio).</summary>
        const int GateReplacesSegmentCount = 3;

        /// <summary>Si el rayo impacta un muro compuesto, reemplaza varios segmentos por una sola puerta. Requiere selectedBuilding = Muro_Puerta_SO.</summary>
        bool TryReplaceWallSegmentWithGate(Ray ray)
        {
            RaycastHit[] hits = Physics.RaycastAll(ray, 5000f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var site = hit.collider.GetComponentInParent<BuildSite>();
                if (site != null && site.TryReplaceCompoundPathSegmentsWithGate(hit, selectedBuilding, selectedBuilding.prefab, selectedBuilding.gateReplacementRotationOffset))
                    return true;

                var bi = hit.collider.GetComponentInParent<BuildingInstance>();
                if (bi == null || bi.buildingSO == null || !bi.buildingSO.isCompound) continue;
                Transform root = bi.transform;
                Transform segment = hit.collider.transform;
                while (segment.parent != null && segment.parent != root) segment = segment.parent;
                if (segment.parent != root) continue;

                int midIndex = segment.GetSiblingIndex();
                int n = Mathf.Min(GateReplacesSegmentCount, root.childCount);
                int start = Mathf.Max(0, midIndex - (n / 2));
                int end = start + n - 1;
                if (end >= root.childCount) { end = root.childCount - 1; start = Mathf.Max(0, end - n + 1); }

                Vector3 posSum = Vector3.zero;
                Transform middleSegment = null;
                for (int i = start; i <= end; i++)
                {
                    Transform child = root.GetChild(i);
                    posSum += child.position;
                    if (i == midIndex) middleSegment = child;
                }
                int count = end - start + 1;
                if (middleSegment == null) middleSegment = root.GetChild(start + count / 2);
                Vector3 centerPos = count > 0 ? posSum / count : segment.position;
                Quaternion rot = middleSegment.rotation * Quaternion.Euler(selectedBuilding.gateReplacementRotationOffset);
                int layer = root.gameObject.layer;

                for (int i = end; i >= start; i--)
                    Destroy(root.GetChild(i).gameObject);

                GameObject gate = Instantiate(selectedBuilding.prefab, centerPos, rot, root);
                gate.transform.localScale = Vector3.one;
                gate.layer = layer;
                SetLayerRecursive(gate.transform, layer);
                if (gate.GetComponentInChildren<GateController>(true) == null)
                    gate.AddComponent<GateController>();
                var rootObs = root.GetComponent<UnityEngine.AI.NavMeshObstacle>();
                if (rootObs != null) UnityEngine.Object.Destroy(rootObs);
                var rootBox = root.GetComponent<Collider>();
                if (rootBox != null) rootBox.isTrigger = true;
                return true;
            }
            return false;
        }

        bool TryPlaceBuildSite(Vector3 position, out BuildSite site)
        {
            if (selectedBuilding == null || owner == null)
            {
                site = null;
                return false;
            }
            Quaternion rot = Quaternion.Euler(0f, _currentYaw, 0f);
            return TryPlaceBuildSiteForOwner(selectedBuilding, position, rot, owner, out site);
        }

        /// <summary>Coloca un solar pagando con los recursos del jugador indicado (p. ej. IA). Reutiliza prefab y máscaras de este placer.</summary>
        public bool TryPlaceBuildSiteForOwner(BuildingSO building, Vector3 position, Quaternion rotation, PlayerResources payFrom, out BuildSite site)
        {
            site = null;
            if (building == null || payFrom == null) return false;

            if (position.sqrMagnitude < 9f)
            {
                Debug.LogWarning("BuildingPlacer: TryPlaceBuildSiteForOwner rechazado (posición cerca de 0,0,0).");
                return false;
            }

            if (buildSitePrefab == null)
            {
                Debug.LogError("BuildingPlacer: buildSitePrefab no está asignado.");
                return false;
            }

            if (!CanAffordFor(building, payFrom))
                return false;

            GameObject go = Instantiate(buildSitePrefab, position, rotation);
            AlignBuildSiteToTerrain(go, position.y);
            go.name = $"[SITE] {building.id}";

            site = go.GetComponent<BuildSite>();
            if (site == null)
            {
                Debug.LogError("BuildingPlacer: buildSitePrefab NO tiene componente BuildSite.");
                Destroy(go);
                return false;
            }

            foreach (var cost in building.costs)
                payFrom.Subtract(cost.kind, cost.amount);

            site.buildingSO = building;
            site.finalPrefab = building.prefab;
            site.buildTime = GetBuildTime(building);
            site.owner = payFrom;
            site.targetBaseY = position.y;

            if (debugLogs) Debug.Log($"SITE CONFIG -> name={go.name} id={go.GetInstanceID()} so={(site.buildingSO ? site.buildingSO.id : "null")} final={(site.finalPrefab != null)} buildTime={site.buildTime}");

            OccupyCells(position, building.size, true);
            DisplaceUnitsInFootprint(position, building.size);

            return true;
        }

        public bool CanAffordFor(BuildingSO building, PlayerResources payFrom)
        {
            if (building == null || payFrom == null) return false;
            if (building.costs == null || building.costs.Length == 0) return true;
            foreach (var cost in building.costs)
            {
                if (!payFrom.Has(cost.kind, cost.amount))
                    return false;
            }
            return true;
        }

        /// <summary>Asigna aldeanos del bando que usan <paramref name="payFrom"/> como dueño de recursos.</summary>
        public void AssignBuildersToSiteForOwner(BuildSite site, PlayerResources payFrom, FactionId faction, int maxBuilders)
        {
            if (site == null || payFrom == null || maxBuilders <= 0) return;
            var all = Object.FindObjectsByType<Builder>(FindObjectsSortMode.None);
            var scored = new List<(Builder b, float d)>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null) continue;
                var g = b.GetComponent<VillagerGatherer>();
                if (g == null || g.owner != payFrom) continue;
                var fm = b.GetComponentInParent<FactionMember>();
                if (fm == null || fm.faction != faction) continue;
                float d = (b.transform.position - site.transform.position).sqrMagnitude;
                scored.Add((b, d));
            }
            scored.Sort((a, c) => a.d.CompareTo(c.d));
            int n = Mathf.Min(maxBuilders, scored.Count);
            for (int i = 0; i < n; i++)
                scored[i].b.SetBuildTarget(site, "BuildingPlacer.AssignBuildersToSiteForOwner");
        }

        /// <param name="targetBaseY">Si tiene valor, la base visual del site se coloca en esta Y (footprint). Si no, se usa sample del terreno en el pivot.</param>
        void AlignBuildSiteToTerrain(GameObject siteGo, float? targetBaseY = null)
        {
            if (siteGo == null) return;

            float targetY = targetBaseY ?? (terrain != null ? terrain.SampleHeight(siteGo.transform.position) + terrain.transform.position.y : siteGo.transform.position.y);
            float bottomY = float.MaxValue;
            bool foundBounds = false;

            var renderers = siteGo.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled) continue;
                if (BuildingTerrainAlignment.ShouldExcludeRendererForBaseAlignment(r)) continue;
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
                    if (BuildingTerrainAlignment.ShouldExcludeColliderForBaseAlignment(c)) continue;
                    bottomY = Mathf.Min(bottomY, c.bounds.min.y);
                    foundBounds = true;
                }
            }

            if (!foundBounds) return;

            float delta = targetY - bottomY;
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

			if (debugLogs) Debug.Log($"🔲 Grid: {(occupy ? "Ocupado" : "Liberado")} {size.x}×{size.y} en {center}");
		}

		/// <summary>Desplaza unidades que estén dentro del footprint de construcción (todas las edificaciones).</summary>
		void DisplaceUnitsInFootprint(Vector3 worldPos, Vector2 footprintSize)
		{
			if (unitDisplacementMask == 0 || MapGrid.Instance == null || !MapGrid.Instance.IsReady) return;
			Vector2Int center = MapGrid.Instance.WorldToCell(worldPos);
			Vector2Int size = new Vector2Int(
				Mathf.Max(1, Mathf.RoundToInt(footprintSize.x)),
				Mathf.Max(1, Mathf.RoundToInt(footprintSize.y))
			);
			Vector2Int min = new Vector2Int(center.x - size.x / 2, center.y - size.y / 2);
			DisplaceUnitsInRect(min, size);
		}

		/// <summary>Desplaza unidades dentro del rect de celdas (path de muro o footprint) fuera del área.</summary>
		void DisplaceUnitsInRect(Vector2Int min, Vector2Int size)
		{
			if (unitDisplacementMask == 0 || MapGrid.Instance == null || !MapGrid.Instance.IsReady) return;
			float cs = MapGrid.Instance.cellSize;
			Vector3 centerWorld = MapGrid.Instance.CellToWorld(new Vector2Int(min.x + size.x / 2, min.y + size.y / 2));
			if (terrain != null)
				centerWorld.y = terrain.SampleHeight(centerWorld) + terrain.transform.position.y;
			Vector3 halfExtents = new Vector3(size.x * cs * 0.5f, 5f, size.y * cs * 0.5f);
			Collider[] hits = Physics.OverlapBox(centerWorld, halfExtents, Quaternion.identity, unitDisplacementMask, QueryTriggerInteraction.Ignore);
			var moved = new HashSet<Project.Gameplay.Units.UnitMover>();
			float margin = cs * 2f;
			float distOut = Mathf.Max(size.x, size.y) * cs * 0.5f + margin;
			for (int i = 0; i < hits.Length; i++)
			{
				var mover = hits[i].GetComponentInParent<Project.Gameplay.Units.UnitMover>();
				if (mover == null || moved.Contains(mover)) continue;
				// No desplazar aldeanos con Builder: evita que se empuje a quien va a construir y se trabe
				if (mover.GetComponentInParent<Project.Gameplay.Units.Builder>() != null) continue;
				moved.Add(mover);
				Vector3 unitPos = mover.transform.position;
				Vector3 dir = (unitPos - centerWorld);
				dir.y = 0f;
				if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
				else dir.Normalize();
				Vector3 exitPoint = centerWorld + dir * distOut;
				exitPoint.y = unitPos.y;
				mover.MoveTo(exitPoint);
			}
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

        static Quaternion SafeLookRotation(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return Quaternion.identity;
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
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
                if (BuildingTerrainAlignment.ShouldExcludeRendererForBaseAlignment(r)) continue;
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
                    if (BuildingTerrainAlignment.ShouldExcludeColliderForBaseAlignment(c)) continue;
                    bottomY = Mathf.Min(bottomY, c.bounds.min.y);
                    found = true;
                }
            }

            if (!found) return 0f;
            return go.transform.position.y - bottomY;
        }
		
        void EnsurePathPreview()
        {
            if (_pathPreviewLine != null) return;
            var go = new GameObject("[PathPreview]");
            go.transform.SetParent(transform);
            _pathPreviewLine = go.AddComponent<LineRenderer>();
            _pathPreviewLine.positionCount = 0;
            _pathPreviewLine.useWorldSpace = true;
            _pathPreviewLine.startWidth = pathPreviewLineWidth;
            _pathPreviewLine.endWidth = pathPreviewLineWidth * 0.6f;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _pathPreviewLine.material = shader != null ? new Material(shader) : null;
            _pathPreviewLine.startColor = new Color(0.3f, 1f, 0.4f, 0.95f);
            _pathPreviewLine.endColor = new Color(0.2f, 0.85f, 0.3f, 0.9f);
            _pathPreviewLine.numCapVertices = 4;
            _pathPreviewLine.numCornerVertices = 4;
        }

        void DestroyPathPreview()
        {
            if (_pathPreviewLine != null)
            {
                if (_pathPreviewLine.gameObject != null)
                    Destroy(_pathPreviewLine.gameObject);
                _pathPreviewLine = null;
            }
            ClearPathGhostSegments();
        }

        void ClearPathGhostSegments()
        {
            for (int i = 0; i < _pathGhostSegments.Count; i++)
            {
                if (_pathGhostSegments[i] != null)
                    Destroy(_pathGhostSegments[i]);
            }
            _pathGhostSegments.Clear();
        }

        void BuildPathGhostPreview(IReadOnlyList<Vector3> points, BuildingSO so)
        {
            ClearPathGhostSegments();
            if (so == null || so.compoundSegmentPrefab == null || points == null || points.Count == 0) return;

            float segLength = Mathf.Max(0.5f, so.compoundSegmentLength);
            Transform container = transform;
            int ghostLayer = LayerMask.NameToLayer("Ghost");
            if (ghostLayer < 0) ghostLayer = 0;

            bool hasGatePrefab = so.compoundGatePrefab != null;

            // Un solo punto = cursor antes del primer clic: mostrar un segmento siguiendo el puntero (como el resto de edificios).
            if (points.Count == 1)
            {
                Vector3 pos = points[0];
                if (terrain != null)
                    pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;
                Quaternion rot = Quaternion.Euler(so.compoundSegmentRotationOffset);
                GameObject seg = Instantiate(so.compoundSegmentPrefab, pos, rot, container);
                seg.name = "PathGhost_Seg";
                MakeGhostSafe(seg);
                if (ghostLayer >= 0) SetLayerRecursive(seg.transform, ghostLayer);
                var gp = seg.GetComponent<GhostPreview>();
                if (gp == null) gp = seg.AddComponent<GhostPreview>();
                gp.Initialize();
                gp.SetValid(IsValidWallPreviewAt(pos, so));
                _pathGhostSegments.Add(seg);
                return;
            }

            for (int p = 0; p < points.Count - 1; p++)
            {
                Vector3 start = points[p];
                Vector3 end = points[p + 1];
                Vector3 toNext = end - start;
                toNext.y = 0f;
                float distance = toNext.magnitude;
                if (distance < 0.01f) continue;
                Vector3 direction = toNext / distance;

                int numSegs = Mathf.Max(1, Mathf.CeilToInt(distance / segLength));
                float step = distance / numSegs;

                for (int i = 0; i < numSegs; i++)
                {
                    float t = (i * step + step * 0.5f) / Mathf.Max(distance, 0.001f);
                    Vector3 worldPos = Vector3.Lerp(start, end, t);
                    if (terrain != null)
                        worldPos.y = terrain.SampleHeight(worldPos) + terrain.transform.position.y;

                    Quaternion rot = SafeLookRotation(direction) * Quaternion.Euler(so.compoundSegmentRotationOffset);
                    GameObject seg = Instantiate(so.compoundSegmentPrefab, worldPos, rot, container);
                    seg.name = "PathGhost_Seg";
                    MakeGhostSafe(seg);
                    if (ghostLayer >= 0) SetLayerRecursive(seg.transform, ghostLayer);
                    var gpSeg = seg.GetComponent<GhostPreview>();
                    if (gpSeg == null) gpSeg = seg.AddComponent<GhostPreview>();
                    gpSeg.Initialize();
                    gpSeg.SetValid(IsValidWallPreviewAt(worldPos, so));
                    _pathGhostSegments.Add(seg);
                }
            }

            if (so.compoundCornerPrefab != null && points.Count >= 2)
            {
                float minAngleRad = so.compoundCornerMinAngleDeg * Mathf.Deg2Rad;
                for (int j = 0; j < points.Count; j++)
                {
                    bool placeCorner = false;
                    Vector3 forwardDir = Vector3.forward;

                    if (j == 0 || j == points.Count - 1)
                    {
                        if (!so.compoundPlaceCornerAtEndpoints) continue;
                        placeCorner = true;
                        if (j == 0 && points.Count > 1)
                            forwardDir = (points[1] - points[0]).normalized;
                        else if (j == points.Count - 1 && points.Count > 1)
                            forwardDir = (points[j] - points[j - 1]).normalized;
                        forwardDir.y = 0f;
                    }
                    else
                    {
                        Vector3 dirIn = (points[j] - points[j - 1]).normalized;
                        Vector3 dirOut = (points[j + 1] - points[j]).normalized;
                        dirIn.y = 0f;
                        dirOut.y = 0f;
                        float dot = Vector3.Dot(dirIn, dirOut);
                        float angleRad = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
                        if (angleRad >= minAngleRad)
                        {
                            placeCorner = true;
                            Vector3 bisector = (dirIn + dirOut).normalized;
                            if (bisector.sqrMagnitude < 0.01f)
                                forwardDir = dirOut;
                            else
                                forwardDir = bisector;
                        }
                    }

                    if (!placeCorner) continue;

                    Vector3 pos = points[j];
                    if (terrain != null)
                        pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;
                    Quaternion cornerRot = SafeLookRotation(forwardDir) * Quaternion.Euler(so.compoundSegmentRotationOffset);
                    GameObject corner = Instantiate(so.compoundCornerPrefab, pos, cornerRot, container);
                    corner.name = "PathGhost_Corner";
                    MakeGhostSafe(corner);
                    if (ghostLayer >= 0) SetLayerRecursive(corner.transform, ghostLayer);
                    var gpCorner = corner.GetComponent<GhostPreview>();
                    if (gpCorner == null) gpCorner = corner.AddComponent<GhostPreview>();
                    gpCorner.Initialize();
                    gpCorner.SetValid(IsValidWallPreviewAt(pos, so));
                    _pathGhostSegments.Add(corner);
                }
            }

            // Puertas en path: instanciar el prefab de puerta en los puntos marcados (Shift+clic).
            if (hasGatePrefab && _pathGatePointIndices.Count > 0 && points.Count >= 2)
            {
                for (int i = 0; i < _pathGatePointIndices.Count; i++)
                {
                    int gatePointIndex = _pathGatePointIndices[i];
                    if (gatePointIndex < 0 || gatePointIndex >= points.Count) continue;

                    Vector3 pos = points[gatePointIndex];
                    if (MapGrid.Instance != null && MapGrid.Instance.IsReady)
                    {
                        Vector2Int cell = MapGrid.Instance.WorldToCell(pos);
                        pos = MapGrid.Instance.CellToWorld(cell);
                    }
                    if (terrain != null)
                        pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;

                    Vector3 forwardDir = Vector3.forward;
                    if (gatePointIndex < points.Count - 1)
                        forwardDir = (points[gatePointIndex + 1] - points[gatePointIndex]).normalized;
                    else if (gatePointIndex > 0)
                        forwardDir = (points[gatePointIndex] - points[gatePointIndex - 1]).normalized;
                    forwardDir.y = 0f;
                    if (forwardDir.sqrMagnitude < 0.001f) forwardDir = Vector3.forward;

                    Quaternion gateRot = SafeLookRotation(forwardDir) * Quaternion.Euler(so.compoundSegmentRotationOffset);
                    GameObject gate = Instantiate(so.compoundGatePrefab, pos, gateRot, container);
                    gate.name = "PathGhost_Gate";
                    MakeGhostSafe(gate);
                    if (ghostLayer >= 0) SetLayerRecursive(gate.transform, ghostLayer);
                    var gpGate = gate.GetComponent<GhostPreview>();
                    if (gpGate == null) gpGate = gate.AddComponent<GhostPreview>();
                    gpGate.Initialize();
                    gpGate.SetValid(IsValidWallPreviewAt(pos, so));
                    _pathGhostSegments.Add(gate);
                }
            }
        }

        bool IsValidWallPreviewAt(Vector3 pos, BuildingSO so)
        {
            if (so == null) return false;

            // 1) Validación de colisión/ocupación (misma que edificios, pero por "pieza" del path).
            bool validPlace = PlacementValidator.IsValidPlacement(pos, so.size, blockingMask);
            if (!validPlace) return false;

            // 2) Validación de terreno por footprint (pendiente / delta de altura).
            if (terrain == null) return true;
            int bw = Mathf.Max(1, Mathf.RoundToInt(so.size.x));
            int bh = Mathf.Max(1, Mathf.RoundToInt(so.size.y));
            var sample = FootprintTerrainSampler.Sample(terrain, pos, new Vector2(bw, bh), _currentYaw);
            return TerrainPlacementValidator.IsValid(sample, maxHeightDelta, maxSlopeDegrees, new Vector2(bw, bh));
        }

        /// <summary>
        /// Colliders de muros/cercas ya construidos (compound path) no deben consumir el clic de "añadir punto":
        /// el ray largo los atraviesa antes que el suelo y el fantasma ya valida el terreno/ocupación.
        /// </summary>
        static bool IsCompoundPathWallColliderForPathBlock(Collider c)
        {
            if (c == null) return false;
            var bi = c.GetComponentInParent<BuildingInstance>();
            return bi != null && bi.buildingSO != null && bi.buildingSO.compoundPathMode;
        }

        static bool PathBlockingRayHasRealBlocker(Ray ray, float maxDistance, LayerMask pathBlockingLayers)
        {
            var hits = Physics.RaycastAll(ray, maxDistance, pathBlockingLayers, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return false;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                if (!IsCompoundPathWallColliderForPathBlock(hits[i].collider))
                    return true;
            }
            return false;
        }

        void UpdatePathPlacement(Mouse mouse, Keyboard kb, Camera camera)
        {
            if (camera == null || mouse == null) return;

            Vector2 mousePos2 = mouse.position.ReadValue();
            Ray ray = camera.ScreenPointToRay(mousePos2);
            // Si groundMask es 0 (sin asignar), usar Everything para que el clic impacte algo (terreno, etc.)
            LayerMask rayMask = groundMask != 0 ? groundMask : (LayerMask)(-1);
            bool hitGround = Physics.Raycast(ray, out RaycastHit hit, 5000f, rayMask);
            Vector3 p = hitGround ? hit.point : ray.GetPoint(100f);

            float cellSize = GetCellSize();
            if (snapToGrid && cellSize > 0.001f)
            {
                bool canSnapCellCenter = MapGrid.Instance != null && MapGrid.Instance.IsReady;
                bool isWallPath = _isPlacingPath && selectedBuilding != null && selectedBuilding.id == "Muro";
                bool isGatePreviewHeld = kb != null
                    && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)
                    && selectedBuilding != null
                    && selectedBuilding.compoundGatePrefab != null;

                // En path del muro: el muro debe quedar "entre líneas" (centro de celda), igual que edificios.
                // La puerta (Shift) también va al centro de celda.
                if (_isPlacingPath && canSnapCellCenter && (isWallPath || isGatePreviewHeld))
                {
                    Vector2Int cell = MapGrid.Instance.WorldToCell(p);
                    p = MapGrid.Instance.CellToWorld(cell);
                }
                else
                {
                    Vector3 origin = (MapGrid.Instance != null && MapGrid.Instance.IsReady) ? MapGrid.Instance.origin : Vector3.zero;
                    p.x = Mathf.Round((p.x - origin.x) / cellSize) * cellSize + origin.x;
                    p.z = Mathf.Round((p.z - origin.z) / cellSize) * cellSize + origin.z;
                }
            }
            if (terrain != null)
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y;

            // Clic derecho = quitar último punto (no cancelar todo)
            if (mouse.rightButton.wasPressedThisFrame)
            {
                if (UiInputRaycast.IsPointerOverGameObject()) return;
                if (_pathPoints.Count > 0)
                {
                    int lastIdx = _pathPoints.Count - 1;
                    _pathGatePointIndices.Remove(lastIdx);
                    _pathPoints.RemoveAt(lastIdx);
                    if (debugLogs) Debug.Log($"Path: punto eliminado. Quedan {_pathPoints.Count}.");
                }
                else
                    Cancel();
                UpdatePathPreviewAndGhost(p);
                return;
            }

            void TryConfirmPath()
            {
                if (_pathPoints.Count < 2) return;
                if (!TrySanitizeWallPathForConfirm(out List<Vector3> pathForSite, out List<int> gatesForSite))
                    return;
                if (!CanAfford(selectedBuilding)) { if (debugLogs) Debug.LogWarning("BuildingPlacer: No hay recursos para el muro."); return; }
                if (buildSitePrefab == null) { Debug.LogError("BuildingPlacer: buildSitePrefab no asignado."); return; }

                BuildSite.ComputePathOccupiedRect(pathForSite, out Vector2Int pathMin, out Vector2Int pathSize);
                Quaternion rot = Quaternion.identity;
                GameObject go = Instantiate(buildSitePrefab, pathForSite[0], rot);
                go.name = $"[SITE] {selectedBuilding.id}";
                var site = go.GetComponent<BuildSite>();
                if (site == null) { Destroy(go); return; }

                foreach (var cost in selectedBuilding.costs)
                    owner.Subtract(cost.kind, cost.amount);

                site.buildingSO = selectedBuilding;
                site.finalPrefab = selectedBuilding.prefab;
                site.buildTime = GetBuildTime(selectedBuilding);
                site.owner = owner;
                site.targetBaseY = pathForSite[0].y;
                site.SetPathPoints(pathForSite);
                site.SetPathPointGates(gatesForSite);
                site.SetPathOccupiedRect(pathMin, pathSize);

                // No ocupar celdas aquí: el aldeano debe poder entrar a construir. Se ocupan al completar (BuildingInstance.OccupyCellsOnStart).
                // if (MapGrid.Instance != null && MapGrid.Instance.IsReady)
                //     MapGrid.Instance.SetOccupiedRect(pathMin, pathSize, true);

                DisplaceUnitsInRect(pathMin, pathSize);

                LastPlacedSite = site;
                if (debugLogs) Debug.Log($"🏗️ Muro path creado: {selectedBuilding.id} con {pathForSite.Count} puntos.");
                Debug.Log($"[WallBuild] site created name={site.name} points={pathForSite.Count}");
                AutoAssignBuilders(site);
                Cancel();
            }

            // Confirmar path (Enter o doble clic)
            if (kb != null && kb[pathConfirmKey].wasPressedThisFrame)
            {
                if (_pathPoints.Count >= 2)
                    TryConfirmPath();
                else if (debugLogs)
                    Debug.Log("BuildingPlacer: Añade al menos 2 puntos antes de confirmar (Enter).");
                else
                    Cancel();
                return;
            }

            // Clic izquierdo = añadir punto
            if (mouse.leftButton.wasPressedThisFrame)
            {
                // Solo no añadir punto si el clic impacta una unidad/edificio (pathBlockingLayers), para poder dar órdenes
                // Si Path Blocking Layers = Everything, no bloquear (si no, el clic en el suelo bloquearía siempre)
                // Acotar al suelo: evita hits "delante" del punto de colocación; ignorar muros compound ya construidos alinea con el preview verde.
                bool blockAdd = false;
                if (pathBlockingLayers != 0 && pathBlockingLayers != (LayerMask)(-1))
                {
                    float blockRayLen = hitGround ? hit.distance + 0.12f : 5000f;
                    blockAdd = PathBlockingRayHasRealBlocker(ray, blockRayLen, pathBlockingLayers);
                }
                if (blockAdd)
                {
                    UpdatePathPreviewAndGhost(hitGround ? p : (Vector3?)null);
                    return;
                }

                float timeSinceLastClick = Time.time - _pathLastClickTime;
                bool isDoubleClick = pathConfirmWithDoubleClick && _pathPoints.Count >= 1 && timeSinceLastClick <= 0.35f && Vector2.Distance(_pathLastClickPos, mousePos2) < 24f;
                bool asGate = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);

                // Doble clic: confirmar sin añadir punto nuevo (evita duplicados por segundo clic).
                if (isDoubleClick)
                {
                    if (_pathPoints.Count >= 2)
                    {
                        TryConfirmPath();
                        return;
                    }
                    UpdatePathPreviewAndGhost(hitGround ? p : (Vector3?)null);
                    return;
                }

                if (_pathPoints.Count > 0)
                {
                    float minPt = GetMinWallPathPointSpacing();
                    float sqr = SqrDistXZPath(p, _pathPoints[_pathPoints.Count - 1]);
                    if (sqr < minPt * minPt)
                    {
                        Debug.Log($"[WallBuild] ignored duplicate/too-close path point pos={p} last={_pathPoints[_pathPoints.Count - 1]} distXZ={Mathf.Sqrt(sqr):F3} min={minPt:F3}");
                        UpdatePathPreviewAndGhost(hitGround ? p : (Vector3?)null);
                        return;
                    }
                }

                _pathPoints.Add(p);
                if (asGate && selectedBuilding.compoundGatePrefab != null)
                    _pathGatePointIndices.Add(_pathPoints.Count - 1);
                _pathLastClickTime = Time.time;
                _pathLastClickPos = mousePos2;
                Debug.Log($"Muro: punto {_pathPoints.Count} añadido en {p}{(asGate ? " (puerta)" : "")}. Sigue clicando o pulsa Enter/doble clic para confirmar.");
            }

            UpdatePathPreviewAndGhost(hitGround ? p : (Vector3?)null);
        }

        void UpdatePathPreviewAndGhost(Vector3? currentMousePos)
        {
            UpdatePathPreviewLine(currentMousePos);
            var pointsForGhost = new List<Vector3>(_pathPoints);
            if (currentMousePos.HasValue)
                pointsForGhost.Add(currentMousePos.Value);
            BuildPathGhostPreview(pointsForGhost, selectedBuilding);
        }

        void UpdatePathPreviewLine(Vector3? currentMousePos)
        {
            if (_pathPreviewLine == null) return;
            int count = _pathPoints.Count + (currentMousePos.HasValue ? 1 : 0);
            _pathPreviewLine.positionCount = count;
            for (int i = 0; i < _pathPoints.Count; i++)
                _pathPreviewLine.SetPosition(i, _pathPoints[i]);
            if (currentMousePos.HasValue)
                _pathPreviewLine.SetPosition(_pathPoints.Count, currentMousePos.Value);
        }

		void AutoAssignBuilders(BuildSite site)
		{
			int assigned = 0;
			if (selection != null)
			{
				var selected = selection.GetSelected();
				for (int i = 0; i < selected.Count; i++)
				{
					var sel = selected[i];
					if (sel == null || !FactionMember.IsPlayerCommandable(sel.gameObject)) continue;
					var builder = sel.GetComponent<Project.Gameplay.Units.Builder>();
					if (builder != null)
					{
						builder.SetBuildTarget(site, "BuildingPlacer.AutoAssignBuilders selección");
						assigned++;
                        if (site != null && site.IsCompoundPathBuilding)
                            Debug.Log($"[WallBuild] builder assigned builder={builder.name} site={site.name} source=selection");
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
					var b = allBuilders[i];
					if (b == null || !FactionMember.IsPlayerCommandable(b.gameObject)) continue;
					float d = (b.transform.position - sitePos).sqrMagnitude;
					if (d < nearestDist)
					{
						nearestDist = d;
						nearest = b;
					}
				}
				if (nearest != null)
				{
					nearest.SetBuildTarget(site, "BuildingPlacer.AutoAssignBuilders más cercano");
                    if (site != null && site.IsCompoundPathBuilding)
                        Debug.Log($"[WallBuild] builder assigned builder={nearest.name} site={site.name} source=nearest");
					if (debugLogs) Debug.Log($"BuildingPlacer: sin aldeanos seleccionados, asignado el más cercano a {site.name}");
				}
			}
		}

    }
}
