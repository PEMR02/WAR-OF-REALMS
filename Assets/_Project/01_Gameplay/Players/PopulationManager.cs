using UnityEngine;
using System;

namespace Project.Gameplay.Players
{
    /// <summary>
    /// Gestiona el límite de población del jugador (como en Age of Empires II)
    /// </summary>
    public class PopulationManager : MonoBehaviour
    {
        [Header("Population")]
        [SerializeField] private int _currentPopulation = 0;
        [SerializeField] private int _maxPopulation = 200; // Límite máximo absoluto
        [SerializeField] private int _currentHousingCapacity = 5; // Capacidad inicial (Town Center)

        public int CurrentPopulation => _currentPopulation;
        public int MaxPopulation => Mathf.Min(_currentHousingCapacity, _maxPopulation);
        public int AvailablePopulation => MaxPopulation - _currentPopulation;
        public bool HasPopulationSpace => _currentPopulation < MaxPopulation;

        // Eventos
        public event Action<int, int> OnPopulationChanged; // (current, max)

        void Awake()
        {
            // Inicializar población
            _currentPopulation = 0;
        }

        /// <summary>
        /// Verifica si hay espacio para una unidad que consume N población
        /// </summary>
        public bool CanAddPopulation(int amount)
        {
            return _currentPopulation + amount <= MaxPopulation;
        }

        /// <summary>
        /// Agrega población (cuando se crea una unidad)
        /// </summary>
        public bool TryAddPopulation(int amount = 1)
        {
            if (!CanAddPopulation(amount))
            {
                Debug.LogWarning($"PopulationManager: No hay espacio de población. Actual: {_currentPopulation}/{MaxPopulation}");
                return false;
            }

            _currentPopulation += amount;
            OnPopulationChanged?.Invoke(_currentPopulation, MaxPopulation);
            return true;
        }

        /// <summary>
        /// Remueve población (cuando muere una unidad)
        /// </summary>
        public void RemovePopulation(int amount = 1)
        {
            _currentPopulation = Mathf.Max(0, _currentPopulation - amount);
            OnPopulationChanged?.Invoke(_currentPopulation, MaxPopulation);
        }

        /// <summary>
        /// Aumenta la capacidad de vivienda (cuando se construye una casa)
        /// </summary>
        public void AddHousingCapacity(int amount)
        {
            _currentHousingCapacity += amount;
            OnPopulationChanged?.Invoke(_currentPopulation, MaxPopulation);
            Debug.Log($"PopulationManager: Capacidad aumentada. Nuevo máximo: {MaxPopulation}");
        }

        /// <summary>
        /// Reduce la capacidad de vivienda (cuando se destruye una casa)
        /// </summary>
        public void RemoveHousingCapacity(int amount)
        {
            _currentHousingCapacity = Mathf.Max(0, _currentHousingCapacity - amount);
            OnPopulationChanged?.Invoke(_currentPopulation, MaxPopulation);
            
            // Si la población actual excede el máximo, avisar
            if (_currentPopulation > MaxPopulation)
            {
                Debug.LogWarning($"PopulationManager: Población actual ({_currentPopulation}) excede máximo ({MaxPopulation})");
            }
        }

        /// <summary>
        /// Resetea la población (para reiniciar partida)
        /// </summary>
        public void Reset()
        {
            _currentPopulation = 0;
            _currentHousingCapacity = 5; // Town Center inicial
            OnPopulationChanged?.Invoke(_currentPopulation, MaxPopulation);
        }
    }
}
