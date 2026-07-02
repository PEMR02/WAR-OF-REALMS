using System;

namespace PMG.UnifiedWorldPipeline
{
    /// <summary>Aspectos evaluables del mundo (nota 0–10 cada uno).</summary>
    public enum PMGUnifiedWorldQualityAspect
    {
        Rivers = 0,
        Lakes = 1,
        TerrainRelief = 2,
        Coastline = 3,
        NavMeshWalkable = 4,
        CityFairness = 5,
        ResourcePlacement = 6,
        VisualWater = 7,
        RiverEndpoints = 8,
        Overall = 99
    }

    [Serializable]
    public struct PMGUnifiedWorldAspectScore
    {
        public PMGUnifiedWorldQualityAspect aspect;
        public float score0To10;
        public float weight;
        public string gradeLetter;
        public string summary;
        public string details;

        public float WeightedPoints => score0To10 * weight;
    }

    public static class PMGUnifiedWorldGradeUtil
    {
        public static string ToLetter(float score0To10)
        {
            if (score0To10 >= 9f) return "A";
            if (score0To10 >= 7.5f) return "B";
            if (score0To10 >= 6f) return "C";
            if (score0To10 >= 4f) return "D";
            return "F";
        }
    }
}
