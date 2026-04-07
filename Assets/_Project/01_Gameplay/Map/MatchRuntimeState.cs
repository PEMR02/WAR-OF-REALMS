using UnityEngine;

namespace Project.Gameplay.Map
{
    public static class MatchRuntimeState
    {
        public static MatchConfig Current { get; private set; }
        public static Bounds GeneratedWorldBounds { get; private set; }
        public static bool HasGeneratedWorldBounds { get; private set; }

        public static float DefaultCellSize => Current != null ? Current.map.cellSize : 3f;

        public static void SetCurrent(MatchConfig config)
        {
            Current = config;
        }

        public static void SetGeneratedWorldBounds(Bounds bounds)
        {
            GeneratedWorldBounds = bounds;
            HasGeneratedWorldBounds = true;
        }

        public static void ClearGeneratedWorldBounds()
        {
            GeneratedWorldBounds = default;
            HasGeneratedWorldBounds = false;
        }

        public static bool TryGetGeneratedWorldBounds(out Bounds bounds)
        {
            bounds = GeneratedWorldBounds;
            return HasGeneratedWorldBounds;
        }
    }
}
