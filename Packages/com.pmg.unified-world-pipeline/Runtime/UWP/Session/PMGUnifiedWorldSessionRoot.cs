using System;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline
{
    /// <summary>
    /// Raíz en escena del pipeline unificado: guarda config, última nota y checklist.
    /// </summary>
    [DisallowMultipleComponent]
    public class PMGUnifiedWorldSessionRoot : MonoBehaviour
    {
        public PMGUnifiedWorldPipelineConfig pipelineConfig;
        public int lastSeed;
        public PMGUnifiedWorldQualityReport lastReport;
        [Range(0f, 10f)] public float manualVisualWaterScore = 5f;
        [TextArea(2, 4)] public string sessionNotes;

        public void StoreReport(PMGUnifiedWorldQualityReport report)
        {
            lastReport = report;
            lastSeed = report.seed;
        }
    }
}
