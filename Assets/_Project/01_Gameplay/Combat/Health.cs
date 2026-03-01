using UnityEngine;

namespace Project.Gameplay.Combat
{
    /// <summary>
    /// Componente de vida reutilizable para unidades y edificios.
    /// Inicializar con InitFromMax() al spawnear o desde BuildingSO/UnitSO.
    /// Implementa IWorldBarSource para que la barra mundial única muestre vida.
    /// </summary>
    public class Health : MonoBehaviour, IHealth, IWorldBarSource
    {
        [Header("Stats")]
        [Tooltip("Vida máxima. Si se usa InitFromMax() al spawnear, este valor se sobrescribe.")]
        public int maxHP = 100;

        [Header("Runtime")]
        [SerializeField] private int _currentHP;

        [Header("Debug (solo para probar barra al X%)")]
        [Tooltip("Si activas esto, al iniciar la vida quedará al Start Percent (ej. 50%). Útil para ver los dos colores de la barra.")]
        [SerializeField] private bool startWithPercentForTesting;
        [Range(0, 100)]
        [SerializeField] private int startPercent = 50;

        public int CurrentHP => _currentHP;
        public int MaxHP => maxHP;
        public bool IsAlive => _currentHP > 0;

        public event System.Action OnDeath;

        void Start()
        {
            if (startWithPercentForTesting && maxHP > 0)
                _currentHP = Mathf.Clamp(Mathf.RoundToInt(maxHP * startPercent / 100f), 1, maxHP);
            else if (_currentHP <= 0 && maxHP > 0)
                _currentHP = maxHP;
        }

        /// <summary>
        /// Inicializa vida desde un valor máximo (ej. desde UnitSO/BuildingSO al spawnear).
        /// </summary>
        public void InitFromMax(int newMaxHP)
        {
            maxHP = Mathf.Max(1, newMaxHP);
            _currentHP = maxHP;
        }

        public void TakeDamage(int amount, object source = null)
        {
            if (amount <= 0 || !IsAlive) return;

            _currentHP = Mathf.Max(0, _currentHP - amount);

            if (_currentHP <= 0)
            {
                OnDeath?.Invoke();
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Restaura vida (curación, reparación).
        /// </summary>
        public void Heal(int amount)
        {
            if (amount <= 0 || !IsAlive) return;
            _currentHP = Mathf.Min(maxHP, _currentHP + amount);
        }

        // IWorldBarSource: misma barra para vida
        public float GetBarRatio01() => maxHP > 0 ? Mathf.Clamp01(_currentHP / (float)maxHP) : 0f;
        public Color GetBarFullColor() => new Color(0.2f, 1f, 0.2f);
        public Color GetBarEmptyColor() => new Color(0.9f, 0.1f, 0.1f);
        public bool IsBarVisible() => IsAlive;
    }
}
