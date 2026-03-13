using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Project.Gameplay.Map;
using Project.Gameplay.Combat;
using Project.Gameplay.Players;

namespace Project.Gameplay.Buildings
{
    public class BuildSite : MonoBehaviour
    {
        [Header("Config")]
        public BuildingSO buildingSO;
        public GameObject finalPrefab;
        public float buildTime = 10f;     // segundos con 1 aldeano
        public float refundOnCancel = 0.75f;

        [Header("Owner (asignado al colocar)")]
        [Tooltip("Jugador que colocó el edificio; se usa para reembolso al cancelar.")]
        public PlayerResources owner;

        [Header("Runtime")]
        [Range(0f, 1f)] public float progress01;
        [Tooltip("Altura Y de la base del edificio (placementY del footprint). Se usa al completar para que el edificio no quede volando.")]
        public float targetBaseY = float.MinValue;

        [Header("Debug")]
        public bool debugLogs = false;

        readonly HashSet<Project.Gameplay.Units.Builder> _builders = new();
        readonly List<Project.Gameplay.Units.Builder> _buildersSnapshot = new List<Project.Gameplay.Units.Builder>(16);
        bool _completed;
        bool _cellsOccupied;
        Image _progressFill;
        const float ProgressBarHeight = 2.5f;

        public bool IsCompleted => progress01 >= 1f;

        public void Register(Project.Gameplay.Units.Builder b) => _builders.Add(b);
        public void Unregister(Project.Gameplay.Units.Builder b) => _builders.Remove(b);

		void Start()
		{
			// Asegurar que el solar sea seleccionable (click para ver panel "Cancelar construcción")
			if (GetComponent<BuildingSelectable>() == null)
				gameObject.AddComponent<BuildingSelectable>();

			StartCoroutine(ValidateAfterOneFrame());
			_cellsOccupied = true;
			EnsureBuildProgressBar();
		}

		void LateUpdate()
		{
			if (_progressFill != null)
				_progressFill.fillAmount = progress01;
		}

		void EnsureBuildProgressBar()
		{
			if (_progressFill != null) return;

			var root = new GameObject("BuildProgressBar");
			root.transform.SetParent(transform);
			root.transform.localPosition = new Vector3(0f, ProgressBarHeight, 0f);
			root.transform.localRotation = Quaternion.identity;
			root.transform.localScale = Vector3.one;

			var canvas = root.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.WorldSpace;
			canvas.worldCamera = Camera.main;
			var rt = root.GetComponent<RectTransform>();
			if (rt == null) rt = root.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(2f, 0.2f);
			rt.localScale = Vector3.one * 0.01f;

			var bgGo = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			bgGo.transform.SetParent(root.transform, false);
			var bgRt = bgGo.GetComponent<RectTransform>();
			bgRt.anchorMin = Vector2.zero;
			bgRt.anchorMax = Vector2.one;
			bgRt.offsetMin = Vector2.zero;
			bgRt.offsetMax = Vector2.zero;
			bgGo.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.2f, 0.9f);
			bgGo.GetComponent<Image>().raycastTarget = false;

			var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			fillGo.transform.SetParent(root.transform, false);
			var fillRt = fillGo.GetComponent<RectTransform>();
			fillRt.anchorMin = Vector2.zero;
			fillRt.anchorMax = Vector2.one;
			fillRt.offsetMin = Vector2.zero;
			fillRt.offsetMax = Vector2.zero;
			_progressFill = fillGo.GetComponent<Image>();
			_progressFill.color = new Color(0.2f, 0.75f, 0.25f, 0.95f);
			_progressFill.raycastTarget = false;
			_progressFill.type = Image.Type.Filled;
			_progressFill.fillMethod = Image.FillMethod.Horizontal;
			_progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
			_progressFill.fillAmount = progress01;
		}

		IEnumerator ValidateAfterOneFrame()
		{
			yield return null;

			if (buildingSO == null || finalPrefab == null)
			{
				Debug.LogWarning($"BuildSite inválido destruido: {name} (no configurado)");
				Destroy(gameObject);
			}
		}

		void OnDestroy()
		{
			// 🟢 Si se destruye sin completar (cancelación), liberar celdas
			if (!_completed && _cellsOccupied)
			{
				FreeCells();
			}
		}

		/// <summary>Cancela la construcción: reembolso parcial, libera aldeanos y celdas, destruye el solar.</summary>
		public void CancelConstruction()
		{
			if (_completed) return;

			_completed = true;
			_cellsOccupied = false;

			// Reembolso parcial al jugador
			if (owner != null && buildingSO != null && buildingSO.costs != null)
			{
				foreach (var cost in buildingSO.costs)
				{
					int refund = Mathf.RoundToInt(cost.amount * refundOnCancel);
					if (refund > 0)
						owner.Add(cost.kind, refund);
				}
			}

			FreeCells();

			// Quitar este solar como objetivo de los aldeanos que estaban construyendo (snapshot reutilizable, sin alloc)
			_buildersSnapshot.Clear();
			_buildersSnapshot.AddRange(_builders);
			_builders.Clear();
			foreach (var b in _buildersSnapshot)
			{
				if (b != null) b.ClearBuildTargetIfThis(this);
			}

			Destroy(gameObject);
		}

        public void AddWorkSeconds(float workSeconds)
        {
            if (_completed) return;
            if (buildTime <= 0.01f) buildTime = 0.01f;

            progress01 = Mathf.Clamp01(progress01 + (workSeconds / buildTime));
            if (progress01 < 1f) return;

            Complete();
        }

        void Complete()
        {
            _completed = true;

            // 🟢 NO liberar celdas aquí, el BuildingInstance las toma
            // Las celdas quedan ocupadas, solo cambia el dueño (BuildSite → BuildingInstance)

            if (finalPrefab != null)
            {
                GameObject built = Instantiate(finalPrefab, transform.position, transform.rotation);
                AlignBuiltToTerrain(built, targetBaseY > -10000f ? targetBaseY : (float?)null);
                // Mantener capa de selección/colisión consistente en todo el edificio final.
                built.layer = gameObject.layer;
                SetLayerRecursive(built.transform, built.layer);
                // 🟢 Pasar info del footprint al edificio final
                var instance = built.GetComponent<BuildingInstance>();
                if (instance != null)
                {
                    instance.buildingSO = buildingSO;
                    instance.OccupyCellsOnStart();  // El edificio toma control de las celdas
                }

                // TownCenter construido por aldeanos: asegurar producción igual que TC inicial del mapa.
                if (built.GetComponent<TownCenter>() != null && built.GetComponent<ProductionBuilding>() == null)
                    built.AddComponent<ProductionBuilding>();

                ConfigureBuiltBuildingRuntime(built, buildingSO);

                var buildingCtrl = built.GetComponent<BuildingController>();
                if (buildingCtrl != null) buildingCtrl.RefreshObstacleAndCollider();

                // ✅ Aumentar límite de población si el edificio lo proporciona
                if (buildingSO != null && buildingSO.populationProvided > 0)
                {
                    var popManager = Object.FindFirstObjectByType<Project.Gameplay.Players.PopulationManager>();
                    if (popManager != null)
                    {
                        popManager.AddHousingCapacity(buildingSO.populationProvided);
                        if (debugLogs) Debug.Log($"🏠 {buildingSO.id} construido → +{buildingSO.populationProvided} población máxima");
                    }
                }
            }
            else
                Debug.LogWarning($"BuildSite: finalPrefab null | name={name} | id={GetInstanceID()} | so={(buildingSO?buildingSO.id:"null")}");
				

            // Snapshot reutilizable (evita alloc y modificar la colección durante la iteración)
            _buildersSnapshot.Clear();
            _buildersSnapshot.AddRange(_builders);
            _builders.Clear();

            for (int i = 0; i < _buildersSnapshot.Count; i++)
            {
                var b = _buildersSnapshot[i];
                if (b != null) b.ClearBuildTargetIfThis(this);
            }

            _cellsOccupied = false;  // 🟢 Ya no somos responsables de las celdas
            if (buildingSO != null)
                Project.UI.GameplayNotifications.Show($"Edificio completado: {buildingSO.id}");
            Destroy(gameObject);
        }

        static void SetLayerRecursive(Transform root, int layer)
        {
            if (root == null) return;
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursive(root.GetChild(i), layer);
        }

        /// <param name="targetBaseY">Si tiene valor, la base del edificio se coloca en esta Y (misma que el placement). Si no, se usa SampleHeight en el pivot (fallback).</param>
        static void AlignBuiltToTerrain(GameObject go, float? targetBaseY = null)
        {
            if (go == null) return;

            float referenceY;
            if (targetBaseY.HasValue)
            {
                referenceY = targetBaseY.Value;
            }
            else
            {
                var terrain = Object.FindFirstObjectByType<Terrain>();
                if (terrain == null) return;
                Vector3 pivotPos = go.transform.position;
                referenceY = terrain.SampleHeight(pivotPos) + terrain.transform.position.y;
            }

            float bottomY = float.MaxValue;
            bool foundBounds = false;

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                string n = r.gameObject.name;
                if (n == "DropAnchor" || n == "SpawnPoint" || n == "BarAnchor" || r.gameObject.GetComponent<Canvas>() != null)
                    continue;
                bottomY = Mathf.Min(bottomY, r.bounds.min.y);
                foundBounds = true;
            }

            if (!foundBounds)
            {
                var colliders = go.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    var c = colliders[i];
                    if (c == null) continue;
                    bottomY = Mathf.Min(bottomY, c.bounds.min.y);
                    foundBounds = true;
                }
            }

            float pivotY = go.transform.position.y;
            float delta = referenceY - (foundBounds ? bottomY : pivotY);
            if (Mathf.Abs(delta) < 0.0001f) return;
            Vector3 p = go.transform.position;
            p.y += delta;
            go.transform.position = p;
        }

        static void ConfigureBuiltBuildingRuntime(GameObject built, BuildingSO soOverride)
        {
            if (built == null) return;

            Transform anchor = built.transform.Find("BarAnchor");
            if (anchor == null)
            {
                var go = new GameObject("BarAnchor");
                anchor = go.transform;
                anchor.SetParent(built.transform, false);
                anchor.localPosition = new Vector3(0f, 2.5f, 0f);
            }

            var health = built.GetComponent<Health>();
            if (health == null) health = built.GetComponentInChildren<Health>(true);
            if (health != null)
                health.SetBarAnchor(anchor);

            var settings = built.GetComponent<WorldBarSettings>();
            if (settings != null)
            {
                settings.barAnchor = anchor;
                settings.autoAnchorName = "BarAnchor";
                settings.useLocalOffsetOverride = true;
                settings.localOffset = Vector3.zero;
            }

            var prod = built.GetComponent<ProductionBuilding>();
            if (prod != null)
                EnsureSafeSpawnPoint(prod, built.transform, soOverride);
        }

        static void EnsureSafeSpawnPoint(ProductionBuilding prod, Transform root, BuildingSO soOverride)
        {
            if (prod == null || root == null) return;
            float sign = soOverride != null && soOverride.unitSpawnForwardSign >= 0f ? 1f : -1f;
            if (prod.spawnPoint == null)
            {
                var spawnObj = new GameObject("SpawnPoint");
                spawnObj.transform.SetParent(root);
                float safeScaleZ = Mathf.Max(0.001f, Mathf.Abs(root.lossyScale.z));
                spawnObj.transform.localPosition = new Vector3(0f, 0f, (3f * sign) / safeScaleZ);
                prod.spawnPoint = spawnObj.transform;
                return;
            }

            // Si el spawnpoint está absurdamente lejos por herencia de escala/prefab, normalizar.
            if (prod.spawnPoint.parent == root)
            {
                float worldDist = Vector3.Distance(root.position, prod.spawnPoint.position);
                if (worldDist > 8f)
                {
                    float safeScaleZ = Mathf.Max(0.001f, Mathf.Abs(root.lossyScale.z));
                    prod.spawnPoint.localPosition = new Vector3(0f, 0f, (3f * sign) / safeScaleZ);
                }
            }
        }

		/// <summary>Libera las celdas ocupadas por este BuildSite (si se cancela).</summary>
		void FreeCells()
		{
			if (buildingSO == null) return;
			if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return;

			Vector2Int center = MapGrid.Instance.WorldToCell(transform.position);
			Vector2Int size = new Vector2Int(
				Mathf.Max(1, Mathf.RoundToInt(buildingSO.size.x)),
				Mathf.Max(1, Mathf.RoundToInt(buildingSO.size.y))
			);
			Vector2Int min = new Vector2Int(center.x - size.x / 2, center.y - size.y / 2);

			MapGrid.Instance.SetOccupiedRect(min, size, false);

			if (debugLogs) Debug.Log($"🔓 Grid: Liberado {size.x}×{size.y} por cancelación de BuildSite");
		}
    }
}
