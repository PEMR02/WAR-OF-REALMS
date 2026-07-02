namespace Project.Gameplay.Map.Generator
{
    /// <summary>RNG determinista para generación de mapas. Mismo seed = mismo mapa.</summary>
    public interface IRng
    {
        /// <summary>Entero en [min, max) (max exclusivo).</summary>
        int NextInt(int minInclusive, int maxExclusive);
        /// <summary>Float en [0, 1).</summary>
        float NextFloat();
        /// <summary>Seed usado (para debug/logs).</summary>
        int Seed { get; }
    }
}
