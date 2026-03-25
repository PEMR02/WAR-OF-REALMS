using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Units;
using Project.Gameplay.Players;
using Project.Gameplay.Combat;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Edificio que puede producir unidades (Cuartel, Arquería, Establo, etc.)
    /// </summary>
    public class ProductionBuilding : MonoBehaviour
    {
        [Header("Refs")]
        public PlayerResources owner;
        public PopulationManager populationManager;
        public Transform spawnPoint;  // Punto donde aparecen las unidades

        [Header("Rally")]
        [Tooltip("Si true, las unidades entrenadas aparecen en rallyPointWorld en lugar de junto al edificio.")]
        public bool useRallyPoint;
        [Tooltip("Posición en mundo donde aparecerán las unidades (click derecho con el edificio seleccionado).")]
        public Vector3 rallyPointWorld;

        [Header("Spawn")]
        [Tooltip("Separación en metros desde el borde del edificio al punto de aparición.")]
        public float spawnClearanceWorld = 1.25f;
        [Tooltip("Si true, calcula el spawn cerca del borde frontal del edificio (robusto ante escalas grandes).")]
        public bool useBoundsBasedSpawn = true;
        [Tooltip("Radio de búsqueda para ajustar el spawn al NavMesh.")]
        public float navMeshSampleRadius = 4f;
        [Tooltip("Radio extra para buscar NavMesh si el punto ideal falla (evita unidades atrapadas).")]
        public float navMeshFallbackRadius = 12f;
        [Tooltip("Fallback global si el BuildingSO no define dirección. 1 = forward, -1 = backward.")]
        [Range(-1f, 1f)] public float defaultSpawnForwardSign = -1f;

        [Header("Production")]
        public ProductionQueue queue = new();
        
        // Eventos
        public event Action<UnitSO> OnUnitQueued;
        public event Action<UnitSO> OnUnitCompleted;
        public event Action OnQueueChanged;

        void Awake()
        {
            // Auto-asignar owner si no está asignado
            if (owner == null)
                owner = FindFirstObjectByType<PlayerResources>();

            // Auto-asignar populationManager si no está asignado
            if (populationManager == null)
                populationManager = FindFirstObjectByType<PopulationManager>();

            // Auto-crear SpawnPoint si no existe
            if (spawnPoint == null)
            {
                GameObject spawnObj = new GameObject("SpawnPoint");
                spawnObj.transform.SetParent(transform);
                // Distancia en mundo consistente aunque el edificio tenga escala grande.
                float safeScaleZ = Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.z));
                float sign = GetSpawnDirectionSign();
                spawnObj.transform.localPosition = new Vector3(0f, 0f, (3f * sign) / safeScaleZ);
                spawnPoint = spawnObj.transform;
            }
        }

        void Update()
        {
            if (queue.IsProducing)
            {
                queue.Tick(Time.deltaTime);
                
                // Si completó la unidad actual
                if (queue.CurrentProgress >= 1f)
                {
                    SpawnUnit(queue.CurrentUnit);
                    queue.CompleteCurrentUnit();
                    OnQueueChanged?.Invoke();
                }
            }
        }

        /// <summary>
        /// Agrega una unidad a la cola de producción
        /// </summary>
        public bool TryQueueUnit(UnitSO unit)
        {
            if (unit == null) return false;
            
            // Verificar límite de población
            if (populationManager != null && !populationManager.CanReservePopulation(unit.populationCost))
            {
                Debug.LogWarning($"ProductionBuilding: No hay espacio de población para {unit.displayName} (requiere {unit.populationCost})");
                return false;
            }
            
            // Verificar recursos
            if (owner != null && !CanAfford(unit))
            {
                Debug.LogWarning($"ProductionBuilding: No hay recursos suficientes para {unit.displayName}");
                return false;
            }

            // Cobrar recursos
            if (owner != null)
                PayCost(unit);

            // Reservar población (si aplica)
            if (populationManager != null && !populationManager.TryReservePopulation(unit.populationCost))
            {
                // Revertir pago si no se pudo reservar
                if (owner != null)
                    RefundCost(unit, 1f);
                return false;
            }

            // Agregar a cola
            queue.Enqueue(unit);
            OnUnitQueued?.Invoke(unit);
            OnQueueChanged?.Invoke();
            
            return true;
        }

        /// <summary>
        /// Cancela una unidad en la cola y devuelve recursos
        /// </summary>
        public void CancelUnit(int index)
        {
            if (index < 0 || index >= queue.Count) return;
            
            UnitSO unit = queue.GetAt(index);
            if (unit == null) return;

            // Devolver recursos (50% como en AoE2)
            if (owner != null)
                RefundCost(unit, 0.5f);

            // Liberar población reservada
            if (populationManager != null)
                populationManager.ReleaseReservedPopulation(unit.populationCost);

            queue.RemoveAt(index);
            OnQueueChanged?.Invoke();
        }

        void SpawnUnit(UnitSO unit)
        {
            if (unit == null || unit.prefab == null)
            {
                Debug.LogWarning($"ProductionBuilding: No se puede spawnear {unit?.displayName ?? "null"} - prefab faltante");
                return;
            }

            // Agregar población
            if (populationManager != null)
            {
                if (!populationManager.CommitReservedPopulation(unit.populationCost))
                {
                    if (!populationManager.TryAddPopulation(unit.populationCost))
                    {
                        Debug.LogWarning($"ProductionBuilding: No se pudo agregar población al spawnear {unit.displayName}");
                        // Continuar de todos modos, la unidad ya fue entrenada
                    }
                }
            }

            // Siempre spawnear junto al edificio; el rally point es solo la orden de movimiento
            Vector3 pos = ResolveSpawnPosition();
            GameObject unitObj = Instantiate(unit.prefab, pos, Quaternion.identity);

            // Stats en partida (base + modificadores): vida, velocidad, armadura, etc.
            var stats = unitObj.GetComponent<UnitStatsRuntime>();
            if (stats == null) stats = unitObj.AddComponent<UnitStatsRuntime>();
            stats.InitFromUnitSO(unit);

            // Si hay rally point, dar orden de ir hasta ahí (a pie, no teletransporte)
            if (useRallyPoint)
            {
                var mover = unitObj.GetComponent<UnitMover>();
                if (mover != null)
                    mover.MoveTo(TryProjectToNavMesh(rallyPointWorld));
            }
            
            OnUnitCompleted?.Invoke(unit);
            Project.UI.GameplayNotifications.Show($"Unidad creada: {unit.displayName}");
        }

        Vector3 ResolveSpawnPosition()
        {
            Vector3 fallback = spawnPoint != null ? spawnPoint.position : transform.position;
            if (!useBoundsBasedSpawn)
                return TryProjectToNavMesh(fallback, allowInsideBuilding: false);

            Vector3 center = transform.position;
            if (TryGetBuildingWorldBounds(out Bounds bounds))
                center = new Vector3(bounds.center.x, transform.position.y, bounds.center.z);

            // Dirección de salida: desde el centro hacia el SpawnPoint (solo XZ; no usar Y del SpawnPoint para no spawnear en balcones).
            Vector3 sideDir = Vector3.zero;
            if (spawnPoint != null)
            {
                sideDir = spawnPoint.position - center;
                sideDir.y = 0f;
            }
            if (sideDir.sqrMagnitude < 0.0001f)
            {
                sideDir = transform.forward * GetSpawnDirectionSign();
                sideDir.y = 0f;
            }
            if (sideDir.sqrMagnitude < 0.0001f)
                sideDir = Vector3.forward;
            sideDir.Normalize();

            float edgeDistance = 1.5f;
            if (TryGetBuildingWorldBounds(out Bounds b))
                edgeDistance = Mathf.Max(b.extents.x, b.extents.z) + Mathf.Max(0.25f, spawnClearanceWorld);

            Vector3 candidate = center + sideDir * edgeDistance;
            // No usar spawnPoint.position.y: evita que la unidad aparezca en balcones/altura. Usar altura del suelo (NavMesh/terreno).
            candidate.y = center.y;

            return TryProjectToNavMesh(candidate, allowInsideBuilding: false);
        }

        Vector3 TryProjectToNavMesh(Vector3 candidate, bool allowInsideBuilding = false)
        {
            float radius = Mathf.Max(0.5f, navMeshSampleRadius);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, radius, NavMesh.AllAreas))
            {
                Vector3 pos = hit.position;
                if (!allowInsideBuilding && IsInsideBuildingBounds(pos))
                    return TryFindSpawnOutsideBuilding(candidate);
                return pos;
            }
            // Fallback: buscar más lejos (evita unidades atrapadas si el punto ideal no tiene NavMesh).
            float fallback = Mathf.Max(radius, navMeshFallbackRadius);
            if (NavMesh.SamplePosition(candidate, out hit, fallback, NavMesh.AllAreas))
            {
                Vector3 pos = hit.position;
                if (!allowInsideBuilding && IsInsideBuildingBounds(pos))
                    return TryFindSpawnOutsideBuilding(candidate);
                return pos;
            }
            return candidate;
        }

        bool IsInsideBuildingBounds(Vector3 worldPos)
        {
            if (!TryGetBuildingWorldBounds(out Bounds b)) return false;
            b.Expand(0.5f);
            return b.Contains(worldPos);
        }

        Vector3 TryFindSpawnOutsideBuilding(Vector3 preferredDirection)
        {
            Vector3 center = transform.position;
            float minDist = 3f + spawnClearanceWorld;
            if (TryGetBuildingWorldBounds(out Bounds b))
            {
                center = new Vector3(b.center.x, transform.position.y, b.center.z);
                minDist = Mathf.Max(b.extents.x, b.extents.z) + spawnClearanceWorld + 1f;
            }

            Vector3 sideDir = preferredDirection - center;
            sideDir.y = 0f;
            if (sideDir.sqrMagnitude < 0.0001f) sideDir = transform.forward;
            sideDir.Normalize();
            for (int step = 0; step < 8; step++)
            {
                Vector3 offset = Quaternion.Euler(0f, step * 45f, 0f) * sideDir;
                Vector3 candidate = center + offset * (minDist + step * 1.5f);
                candidate.y = center.y;
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshFallbackRadius, NavMesh.AllAreas) && !IsInsideBuildingBounds(hit.position))
                    return hit.position;
            }
            return preferredDirection;
        }

        bool TryGetBuildingWorldBounds(out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            var colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                var c = colliders[i];
                if (c == null) continue;
                string n = c.gameObject.name;
                if (n.Equals("SpawnPoint", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("DropAnchor", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!hasBounds) { bounds = c.bounds; hasBounds = true; }
                else bounds.Encapsulate(c.bounds);
            }

            if (hasBounds) return true;

            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled) continue;
                if (r.GetComponent<Canvas>() != null) continue;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }

            return hasBounds;
        }

        float GetSpawnDirectionSign()
        {
            float sign = defaultSpawnForwardSign;
            var bi = GetComponent<BuildingInstance>();
            if (bi != null && bi.buildingSO != null)
                sign = bi.buildingSO.unitSpawnForwardSign;

            // Normalizar: valores >= 0 salen por forward, valores < 0 por backward.
            return sign >= 0f ? 1f : -1f;
        }

        bool CanAfford(UnitSO unit)
        {
            if (unit.costs == null || unit.costs.Length == 0) return true;

            foreach (var cost in unit.costs)
            {
                if (owner.Get(cost.kind) < cost.amount)
                    return false;
            }
            return true;
        }

        void PayCost(UnitSO unit)
        {
            if (unit.costs == null) return;
            foreach (var cost in unit.costs)
                owner.Subtract(cost.kind, cost.amount);
        }

        void RefundCost(UnitSO unit, float percentage)
        {
            if (unit.costs == null) return;
            foreach (var cost in unit.costs)
            {
                int refund = Mathf.RoundToInt(cost.amount * percentage);
                owner.Add(cost.kind, refund);
            }
        }
    }

    /// <summary>
    /// Cola de producción de unidades
    /// </summary>
    [System.Serializable]
    public class ProductionQueue
    {
        [SerializeField] private List<UnitSO> _queue = new();
        [SerializeField] private float _currentProgress; // 0..1

        public int Count => _queue.Count;
        public bool IsProducing => _queue.Count > 0;
        public UnitSO CurrentUnit => _queue.Count > 0 ? _queue[0] : null;
        public float CurrentProgress => _currentProgress;

        public void Enqueue(UnitSO unit)
        {
            _queue.Add(unit);
        }

        public void Tick(float deltaTime)
        {
            if (_queue.Count == 0) return;

            UnitSO current = _queue[0];
            if (current == null) return;

            float duration = Mathf.Max(0.01f, current.trainingTimeSeconds);
            _currentProgress += deltaTime / duration;
        }

        public void CompleteCurrentUnit()
        {
            if (_queue.Count > 0)
                _queue.RemoveAt(0);
            _currentProgress = 0f;
        }

        public UnitSO GetAt(int index)
        {
            if (index < 0 || index >= _queue.Count) return null;
            return _queue[index];
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _queue.Count) return;
            _queue.RemoveAt(index);
            
            // Si cancelamos la primera unidad, resetear progreso
            if (index == 0)
                _currentProgress = 0f;
        }

        public List<UnitSO> GetAllUnits() => new List<UnitSO>(_queue);
    }
}
