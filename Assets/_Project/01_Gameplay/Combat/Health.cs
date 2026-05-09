using UnityEngine;
using Project.Gameplay.Units;

namespace Project.Gameplay.Combat
{
    /// <summary>Tipo de daño para aplicar armadura o resistencia mágica.</summary>
    public enum DamageType
    {
        Physical,
        Magic
    }
    /// <summary>
    /// Componente de vida reutilizable para unidades y edificios.
    /// Inicializar con InitFromMax() al spawnear o desde BuildingSO/UnitSO.
    /// Expone barAnchor y GetBarWorldPosition() para el HealthBarManager (barras en pantalla).
    /// </summary>
    public class Health : MonoBehaviour, IHealth, IWorldBarSource
    {
        static bool s_loggedFloatingTextFailure;
        [Header("Stats")]
        [Tooltip("Vida máxima. Si se usa InitFromMax() al spawnear, este valor se sobrescribe.")]
        public int maxHP = 100;

        [Header("Runtime")]
        [SerializeField] private int _currentHP;

        [Header("Barra (HealthBarManager)")]
        [Tooltip("Punto de anclaje en mundo para la barra de vida flotante. Si null, se usa transform.position + fallbackOffset.")]
        [SerializeField] private Transform barAnchor;
        [Tooltip("Offset usado cuando barAnchor es null (ej. encima de la cabeza).")]
        [SerializeField] private Vector3 fallbackOffset = new Vector3(0f, 2f, 0f);

        [Header("Debug (solo para probar barra al X%)")]
        [Tooltip("Si activas esto, al iniciar la vida quedará al Start Percent (ej. 50%). Útil para ver los dos colores de la barra.")]
        [SerializeField] private bool startWithPercentForTesting;
        [Range(0, 100)]
        [SerializeField] private int startPercent = 50;

        public int CurrentHP => _currentHP;
        public int MaxHP => maxHP;
        public bool IsAlive => _currentHP > 0;

        /// <summary>Ratio 0-1 para la barra (lleno = 1).</summary>
        public float Normalized => maxHP > 0 ? Mathf.Clamp01(_currentHP / (float)maxHP) : 0f;

        public Transform BarAnchor => barAnchor;

        public event System.Action OnDeath;
        /// <summary>Se invoca al recibir daño (amount, source). Útil para mobs que se vuelven hostiles al ser atacados.</summary>
        public event System.Action<int, object> OnDamageReceived;
        /// <summary>Se invoca cuando cambia la vida (current, max).</summary>
        public event System.Action<int, int> OnHealthChanged;
        public Transform LastAttackerTransform { get; private set; }
        public float LastDamageTime { get; private set; } = -999f;

        /// <summary>Posición en mundo donde debe dibujarse la barra (HealthBarManager la convierte a pantalla).</summary>
        public Vector3 GetBarWorldPosition()
        {
            if (barAnchor != null)
                return barAnchor.position;
            return transform.position + fallbackOffset;
        }

        /// <summary>Asigna el anchor en runtime (ej. desde BuildSite o MapGenerator al crear BarAnchor).</summary>
        public void SetBarAnchor(Transform anchor)
        {
            barAnchor = anchor;
        }

        void Awake()
        {
            EnsureHPInitialized();
            NotifyHealthChanged();
        }

        void Start()
        {
            if (startWithPercentForTesting && maxHP > 0)
                _currentHP = Mathf.Clamp(Mathf.RoundToInt(maxHP * startPercent / 100f), 1, maxHP);
            else
                EnsureHPInitialized();

            NotifyHealthChanged();
        }

        void OnDestroy()
        {
            HealthBarManager.Instance?.Unregister(this);
        }

        void EnsureHPInitialized()
        {
            if (_currentHP <= 0 && maxHP > 0)
                _currentHP = maxHP;
        }

        /// <summary>
        /// Inicializa vida desde un valor máximo (ej. desde UnitSO/BuildingSO al spawnear).
        /// </summary>
        public void InitFromMax(int newMaxHP)
        {
            maxHP = Mathf.Max(1, newMaxHP);
            _currentHP = maxHP;
            NotifyHealthChanged();
        }

        /// <summary>Inflige daño. Si hay UnitStatsRuntime, aplica reducción por armadura (Physical) o resistencia mágica (Magic).</summary>
        public void TakeDamage(int amount, DamageType type = DamageType.Physical, object source = null)
        {
            if (amount <= 0 || !IsAlive) return;

            int reduction = 0;
            var stats = GetComponent<UnitStatsRuntime>();
            if (stats != null)
                reduction = type == DamageType.Physical ? stats.GetEffectiveArmor() : stats.GetEffectiveMagicResist();
            int final = Mathf.Max(1, amount - reduction);

            TrySpawnFloatingText(final, isHeal: false);
            _currentHP = Mathf.Max(0, _currentHP - final);
            if (source is GameObject sourceGo)
                LastAttackerTransform = sourceGo.transform;
            else if (source is Component sourceComponent)
                LastAttackerTransform = sourceComponent.transform;
            LastDamageTime = Time.time;
            OnDamageReceived?.Invoke(final, source);
            NotifyHealthChanged();

            if (_currentHP <= 0)
            {
                OnDeath?.Invoke();
                Destroy(gameObject);
            }
        }

        /// <summary>Compatible con código que llama TakeDamage(amount, source).</summary>
        public void TakeDamage(int amount, object source) => TakeDamage(amount, DamageType.Physical, source);

        /// <summary>
        /// Restaura vida (curación, reparación).
        /// </summary>
        public void Heal(int amount)
        {
            if (amount <= 0 || !IsAlive) return;
            if (amount >= 5) TrySpawnFloatingText(amount, isHeal: true);
            int prev = _currentHP;
            _currentHP = Mathf.Min(maxHP, _currentHP + amount);
            if (_currentHP != prev)
                NotifyHealthChanged();
        }

        void NotifyHealthChanged()
        {
            OnHealthChanged?.Invoke(_currentHP, Mathf.Max(1, maxHP));
        }

        void TrySpawnFloatingText(int amount, bool isHeal)
        {
            try
            {
                FloatingDamageText.Spawn(transform.position, amount, isHeal);
            }
            catch (System.Exception ex)
            {
                if (!s_loggedFloatingTextFailure)
                {
                    s_loggedFloatingTextFailure = true;
                    Debug.LogWarning($"[Combat] FloatingDamageText deshabilitado por error: {ex.GetType().Name}");
                }
            }
        }

        // IWorldBarSource (deprecated: usado por HealthBarWorld legacy)
        public float GetBarRatio01() => Normalized;
        public Color GetBarFullColor() => new Color(0.2f, 1f, 0.2f);
        public Color GetBarEmptyColor() => new Color(0.9f, 0.1f, 0.1f);
        public bool IsBarVisible() => IsAlive;
    }
}
