using System;

namespace Project.Gameplay.Combat
{
    /// <summary>
    /// Interfaz para cualquier entidad que tenga vida (unidades, edificios, etc.).
    /// Base para sistema de ataque y daños.
    /// </summary>
    public interface IHealth
    {
        int CurrentHP { get; }
        int MaxHP { get; }
        bool IsAlive { get; }
        void TakeDamage(int amount, object source = null);
        event Action OnDeath;
    }
}
