using UnityEngine;
using System;
using Project.Gameplay.Faction;

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
        [SerializeField] private int _reservedPopulation = 0; // Reservada por colas de producción
        [Tooltip("Si true, al iniciar cuenta los aldeanos ya en escena (generados por mapa o colocados a mano) para que Pop coincida con Ociosos.")]
        [SerializeField] private bool registerExistingVillagersOnStart = true;
        [Tooltip("Si true, no ejecuta RegisterExistingVillagers en Start (p. ej. PopulationManager en Town Center de la IA).")]
        [SerializeField] public bool skipAutoRegisterPopulation;

        public int CurrentPopulation => _currentPopulation;
        public int MaxPopulation => Mathf.Min(_currentHousingCapacity, _maxPopulation);
        public int ReservedPopulation => _reservedPopulation;
        public int AvailablePopulation => MaxPopulation - (_currentPopulation + _reservedPopulation);
        public bool HasPopulationSpace => AvailablePopulation > 0;

        // Eventos
        public event Action<int, int> OnPopulationChanged; // (current, max)

        void Awake()
        {
            _currentPopulation = 0;
        }

        /// <summary>Población global del jugador humano (no el <see cref="PopulationManager"/> del TC de la IA).</summary>
        public static PopulationManager FindPrimaryHumanSkirmish()
        {
            var all = FindObjectsByType<PopulationManager>(FindObjectsSortMode.None);
            PopulationManager fallback = null;
            for (int i = 0; i < all.Length; i++)
            {
                var pm = all[i];
                if (pm == null) continue;
                if (pm.gameObject.name.StartsWith("TownCenter_Player", StringComparison.Ordinal))
                    continue;
                if (pm.gameObject.name == "GameManagers")
                    return pm;
                fallback ??= pm;
            }
            return fallback;
        }

        /// <summary>
        /// Población asociada al mismo jugador que <paramref name="resources"/> (p. ej. en el Town Center de la IA);
        /// si no hay componente local, cae al <see cref="PopulationManager"/> del jugador humano en escaramuza.
        /// </summary>
        public static PopulationManager ResolveForOwner(PlayerResources resources)
        {
            if (resources != null)
            {
                var local = resources.GetComponent<PopulationManager>();
                if (local != null) return local;
            }
            return FindPrimaryHumanSkirmish();
        }

        void Start()
        {
            if (!skipAutoRegisterPopulation && registerExistingVillagersOnStart)
                RegisterExistingVillagers();
        }

        /// <summary>Skirmish IA: capacidad del TC + población ya generada (aldeanos iniciales).</summary>
        public void SetInitialStateForAiTownCenter(int housingCapacity, int currentPopulationUnits)
        {
            _currentHousingCapacity = Mathf.Max(0, housingCapacity);
            _currentPopulation = Mathf.Max(0, currentPopulationUnits);
            _reservedPopulation = 0;
            OnPopulationChanged?.Invoke(_currentPopulation, MaxPopulation);
        }

        /// <summary>Cuenta aldeanos ya presentes en la escena (map gen o colocados a mano) para que Pop coincida con Ociosos.</summary>
        public void RegisterExistingVillagers()
        {
            var gatherers = UnityEngine.Object.FindObjectsByType<Project.Gameplay.Units.VillagerGatherer>(UnityEngine.FindObjectsSortMode.None);
            int added = 0;
            for (int i = 0; i < gatherers.Length; i++)
            {
                if (gatherers[i] == null) continue;
                var fm = gatherers[i].GetComponentInParent<FactionMember>();
                if (fm != null && !fm.IsPlayer)
                    continue;
                if (TryAddPopulation(1)) added++;
            }
            if (added > 0)
                Debug.Log($"PopulationManager: registrados {added} aldeanos existentes en escena. Pop: {_currentPopulation}/{MaxPopulation}");
        }

        /// <summary>
        /// Verifica si hay espacio para una unidad que consume N población
        /// </summary>
        public bool CanAddPopulation(int amount)
        {
            return _currentPopulation + _reservedPopulation + amount <= MaxPopulation;
        }

        /// <summary>
        /// Verifica si hay espacio para reservar N población (para colas)
        /// </summary>
        public bool CanReservePopulation(int amount)
        {
            return _currentPopulation + _reservedPopulation + amount <= MaxPopulation;
        }

        /// <summary>
        /// Reserva población para una cola de producción
        /// </summary>
        public bool TryReservePopulation(int amount)
        {
            if (amount <= 0) return true;
            if (!CanReservePopulation(amount))
            {
                Debug.LogWarning($"PopulationManager: No hay espacio para reservar población. Actual: {_currentPopulation}+{_reservedPopulation}/{MaxPopulation}");
                return false;
            }

            _reservedPopulation += amount;
            OnPopulationChanged?.Invoke(_currentPopulation, MaxPopulation);
            return true;
        }

        /// <summary>
        /// Libera población reservada (al cancelar en cola)
        /// </summary>
        public void ReleaseReservedPopulation(int amount)
        {
            if (amount <= 0) return;
            _reservedPopulation = Mathf.Max(0, _reservedPopulation - amount);
            OnPopulationChanged?.Invoke(_currentPopulation, MaxPopulation);
        }

        /// <summary>
        /// Convierte población reservada en población actual (al spawnear)
        /// </summary>
        public bool CommitReservedPopulation(int amount)
        {
            if (amount <= 0) return true;

            if (_reservedPopulation < amount)
                return false;

            _reservedPopulation -= amount;
            _currentPopulation += amount;
            OnPopulationChanged?.Invoke(_currentPopulation, MaxPopulation);
            return true;
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
            _reservedPopulation = 0;
            OnPopulationChanged?.Invoke(_currentPopulation, MaxPopulation);
        }
    }
}
