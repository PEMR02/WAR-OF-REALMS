using System;
using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Units;
using Project.Gameplay.Players;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Edificio que puede producir unidades (Cuartel, Arquería, Establo, etc.)
    /// </summary>
    public class ProductionBuilding : MonoBehaviour
    {
        [Header("Refs")]
        public PlayerResources owner;
        public Transform spawnPoint;  // Punto donde aparecen las unidades

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

            // Auto-crear SpawnPoint si no existe
            if (spawnPoint == null)
            {
                GameObject spawnObj = new GameObject("SpawnPoint");
                spawnObj.transform.SetParent(transform);
                spawnObj.transform.localPosition = new Vector3(0, 0, 5); // Delante del edificio
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
            
            // Verificar recursos
            if (owner != null && !CanAfford(unit))
                return false;

            // Cobrar recursos
            if (owner != null)
                PayCost(unit);

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

            queue.RemoveAt(index);
            OnQueueChanged?.Invoke();
        }

        void SpawnUnit(UnitSO unit)
        {
            if (unit == null || unit.prefab == null) return;

            Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
            GameObject unitObj = Instantiate(unit.prefab, pos, Quaternion.identity);
            
            OnUnitCompleted?.Invoke(unit);
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

            _currentProgress += deltaTime / current.trainingTimeSeconds;
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
