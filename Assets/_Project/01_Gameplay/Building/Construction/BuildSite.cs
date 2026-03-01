using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using Project.Gameplay.Map;
using Project.Gameplay.Combat;

namespace Project.Gameplay.Buildings
{
    public class BuildSite : MonoBehaviour
    {
        [Header("Config")]
        public BuildingSO buildingSO;
        public GameObject finalPrefab;
        public float buildTime = 10f;     // segundos con 1 aldeano
        public float refundOnCancel = 0.75f;

        [Header("Runtime")]
        [Range(0f, 1f)] public float progress01;

        readonly HashSet<Project.Gameplay.Units.Builder> _builders = new();
        bool _completed;
        bool _cellsOccupied;  // 🟢 Para evitar liberar dos veces

        public bool IsCompleted => progress01 >= 1f;

        public void Register(Project.Gameplay.Units.Builder b) => _builders.Add(b);
        public void Unregister(Project.Gameplay.Units.Builder b) => _builders.Remove(b);

		void Start()
		{
			// Espera 1 frame para dar tiempo a BuildingPlacer de configurar
			StartCoroutine(ValidateAfterOneFrame());
			_cellsOccupied = true;  // 🟢 Asumimos que BuildingPlacer ya ocupó las celdas
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
                AlignBuiltToTerrain(built);
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
                        Debug.Log($"🏠 {buildingSO.id} construido → +{buildingSO.populationProvided} población máxima");
                    }
                }
            }
            else
                Debug.LogWarning($"BuildSite: finalPrefab null | name={name} | id={GetInstanceID()} | so={(buildingSO?buildingSO.id:"null")}");
				

            // ✅ copiar antes de iterar (evita modificar la colección durante foreach)
            var copy = new List<Project.Gameplay.Units.Builder>(_builders);
            _builders.Clear();

            for (int i = 0; i < copy.Count; i++)
            {
                var b = copy[i];
                if (b != null) b.ClearBuildTargetIfThis(this);
            }

            _cellsOccupied = false;  // 🟢 Ya no somos responsables de las celdas
            Destroy(gameObject);
        }

        static void SetLayerRecursive(Transform root, int layer)
        {
            if (root == null) return;
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursive(root.GetChild(i), layer);
        }

        static void AlignBuiltToTerrain(GameObject go)
        {
            if (go == null) return;
            var terrain = Object.FindFirstObjectByType<Terrain>();
            if (terrain == null) return;

            Vector3 pivotPos = go.transform.position;
            float terrainY = terrain.SampleHeight(pivotPos) + terrain.transform.position.y;
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

            float delta = terrainY - (foundBounds ? bottomY : pivotPos.y);
            if (Mathf.Abs(delta) < 0.0001f) return;
            Vector3 p = go.transform.position;
            p.y += delta;
            go.transform.position = p;
        }

        static void ConfigureBuiltBuildingRuntime(GameObject built, BuildingSO soOverride)
        {
            if (built == null) return;

            var settings = built.GetComponent<WorldBarSettings>();
            if (settings != null)
            {
                Transform anchor = built.transform.Find("BarAnchor");
                if (anchor == null)
                {
                    var go = new GameObject("BarAnchor");
                    anchor = go.transform;
                    anchor.SetParent(built.transform, false);
                    anchor.localPosition = new Vector3(0f, 2f, 0f);
                }

                settings.barAnchor = anchor;
                settings.autoAnchorName = "BarAnchor";
                settings.useLocalOffsetOverride = true;
                settings.localOffset = Vector3.zero;
            }

            var bar = built.GetComponentInChildren<HealthBarWorld>(true);
            if (bar != null)
            {
                bar.useLocalOffset = false;
                bar.localOffset = Vector3.zero;
                bar.barScaleMultiplier = 1f;
                bar.keepConstantWorldSize = true;
                bar.billboardMode = HealthBarWorld.BillboardMode.None;
                bar.defaultAnchorName = "BarAnchor";
                bar.autoUseRendererTopWhenNoAnchor = true;
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

			Debug.Log($"🔓 Grid: Liberado {size.x}×{size.y} por cancelación de BuildSite");
		}
    }
}
