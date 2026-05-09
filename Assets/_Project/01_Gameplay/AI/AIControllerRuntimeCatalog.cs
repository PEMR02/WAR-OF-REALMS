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

        public static BuildingSO FindBuildingSoById(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<BuildingSO>();
            for (int i = 0; i < all.Length; i++)
            {
                var so = all[i];
                if (so == null || string.IsNullOrWhiteSpace(so.id)) continue;
                if (string.Equals(so.id, id, System.StringComparison.OrdinalIgnoreCase))
                    return so;
            }
            return null;
        }
    }
}
