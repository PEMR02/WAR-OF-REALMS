using System;
using System.Text;

namespace PMG.UnifiedWorldPipeline
{
    [Serializable]
    public struct PMGUnifiedWorldQualityReport
    {
        public int seed;
        public bool generationSucceeded;
        public string failureReason;
        public float totalWeightedScore;
        public float totalGrade0To10;
        public string totalGradeLetter;
        public PMGUnifiedWorldAspectScore[] aspects;
        public PMGUnifiedWorldMetrics metrics;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"seed={seed} nota={totalGrade0To10:F1} ({totalGradeLetter})");
            if (aspects != null)
            {
                for (int i = 0; i < aspects.Length; i++)
                {
                    PMGUnifiedWorldAspectScore a = aspects[i];
                    if (a.aspect == PMGUnifiedWorldQualityAspect.Overall) continue;
                    sb.Append($" | {a.aspect}={a.score0To10:F1}{a.gradeLetter}");
                }
            }

            return sb.ToString();
        }
    }

    [Serializable]
    public struct PMGUnifiedWorldMetrics
    {
        public int gridW;
        public int gridH;
        public int lakeComponentCount;
        public int minLakeCells;
        public int maxLakeCells;
        public float lakeCoverage01;
        public float lakeSpread01;
        public int riverCenterlineCount;
        public int tributaryCount;
        public float mainRiverSpan01;
        public float maxRiverStraightness;
        public float playableLand01;
        public float heightRange01;
        public float waterCoverage01;
        public int cityCount;
        public int roadCount;
        public int resourceCells;
        public float estimatedWalkable01;
        public bool mainRiverStartAtBorder;
        public bool mainRiverEndAtBorder;
        public float riverBorderEndpointWidthMul;
        public float riverBorderGhostCells;
        public bool terrainLayersBound;
    }

    [Serializable]
    public struct PMGUnifiedWorldBatchSummary
    {
        public PMGUnifiedWorldQualityReport[] all;
        public PMGUnifiedWorldQualityReport[] top;
        public int evaluatedCount;
    }
}
