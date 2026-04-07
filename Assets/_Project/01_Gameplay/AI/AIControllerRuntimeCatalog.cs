using Project.Gameplay.Buildings;
using Project.Gameplay.Units;

namespace Project.Gameplay.AI
{
    /// <summary>Referencias resueltas al iniciar la IA (evita Resources.Load en cada tick).</summary>
    public static class AIControllerRuntimeCatalog
    {
        public static UnitSO Villager;
        public static BuildingSO House;
        public static BuildingSO Barracks;
        public static ProductionCatalog ProductionCatalog;
    }
}
