using Project.Gameplay.Map.Generation;
using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace Project.Gameplay.Map.CleanWaterPipeline
{
    /// <summary>
    /// Compilación Match → MapGenConfig con tuning de agua limpia aplicado al final.
    /// </summary>
    public static class CleanWaterPipelineCompile
    {
        public static RuntimeMapGenerationSettings Build(
            MatchConfig match,
            MapGenConfig sceneLegacyDefinitiveTemplate,
            MapGenerationRuntimeContext runtimeContext = null,
            bool logSummary = true)
        {
            var runtime = MatchConfigCompiler.Build(
                match,
                sceneLegacyDefinitiveTemplate,
                runtimeContext,
                logSummary);

            if (runtime?.CompiledMapGen != null)
                CleanWaterHydrologyTuning.Apply(runtime.CompiledMapGen);

            return runtime;
        }
    }
}
