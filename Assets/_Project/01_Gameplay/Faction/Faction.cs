using UnityEngine;

namespace Project.Gameplay.Faction
{
    /// <summary>
    /// Identificador de bando para unidades, edificios y mobs.
    /// Usado por IA enemiga, combate y aggro de mobs.
    /// </summary>
    public enum FactionId
    {
        Neutral = 0,
        Player = 1,
        Enemy = 2,
    }

    /// <summary>
    /// Asigna un bando a la entidad. Añadir a unidades, edificios y mobs.
    /// </summary>
    public class FactionMember : MonoBehaviour
    {
        [Tooltip("Bando de esta entidad. Player = jugador, Enemy = IA hostil, Neutral = no ataca ni es atacado por defecto.")]
        public FactionId faction = FactionId.Neutral;

        public static bool IsHostile(FactionId a, FactionId b)
        {
            if (a == FactionId.Neutral || b == FactionId.Neutral) return false;
            return a != b;
        }

        public bool IsHostileTo(FactionMember other)
        {
            return other != null && IsHostile(faction, other.faction);
        }

        public bool IsPlayer => faction == FactionId.Player;
        public bool IsEnemy => faction == FactionId.Enemy;
        public bool IsNeutral => faction == FactionId.Neutral;
    }
}
