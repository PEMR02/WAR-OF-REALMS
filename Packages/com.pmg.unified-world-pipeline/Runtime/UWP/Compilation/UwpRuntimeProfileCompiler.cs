using Project.Gameplay.Map;
using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline
{
    /// <summary>
    /// Compila PMGUnifiedWorldPipelineConfig → MapGenConfig runtime autosuficiente.
    /// No requiere MatchConfig ni RTSMapGenerator; la plantilla MapGen es opcional.
    /// </summary>
    public static class UwpRuntimeProfileCompiler
    {
        public const string CompilerVersion = "2025-06-25-trib-single-confluence-v2";

        /// <summary>Crea MapGenConfig runtime: plantilla opcional + perfil UWP completo.</summary>
        public static MapGenConfig CreateRuntimeConfig(PMGUnifiedWorldPipelineConfig pipeline, int seed)
        {
            MapGenConfig cfg = CreateBaseline(pipeline);
            cfg.seed = seed;
            ApplyFullProfile(pipeline, cfg);
            return cfg;
        }

        /// <summary>Reaplica perfil UWP sobre config existente (p. ej. antes de agua/terrain export).</summary>
        public static void ApplyFullProfile(PMGUnifiedWorldPipelineConfig pipeline, MapGenConfig cfg)
        {
            if (cfg == null) return;

            pipeline?.PullVisualBindingsFromPipelineOnly();
            UwpHydrologyContract.ApplyHydrologyCounts(cfg, pipeline);
            UwpHydrologyContract.ApplyVisualWidths(cfg, pipeline);
            UwpTerrainProfileModule.Apply(cfg, pipeline);
            UwpRiverProfileModule.Apply(cfg, pipeline);
            UwpVisualBindingsModule.ApplyMaterials(cfg, pipeline);

            if (pipeline != null && pipeline.applyRiverVisualExportFix)
                UwpRiverProfileModule.ApplyExportFix(cfg, pipeline);

            if (pipeline != null && pipeline.applyRiverBankTerrainFix)
                UwpRiverProfileModule.ApplyBankTerrainFix(cfg);

            UwpHydrologyContract.ResolvedHydrology h = UwpHydrologyContract.Resolve(pipeline);
            float lakeWorldM = Mathf.Sqrt(h.maxLakeCells) * h.cellSizeWorld;
            Debug.LogWarning(
                $"[UWP] Profile compiled | v={CompilerVersion} grid={h.gridCells}@{h.cellSizeWorld:F1}m " +
                $"main={h.mainFullWidthCells:F1}c trib={h.tributaryFullWidthCells:F1}c " +
                $"rivers={h.riverCount} lakes={h.lakeCount} maxLake={h.maxLakeCells} (~{lakeWorldM:F0}m) " +
                $"uwpOwnedVisual=1 ignoreLobbyCaps=1");
        }

        static MapGenConfig CreateBaseline(PMGUnifiedWorldPipelineConfig pipeline)
        {
            MapGenConfig template = null;
            if (pipeline != null)
            {
                template = pipeline.uwpIndependentMode
                    ? pipeline.uwpMapGenBaseline
                    : pipeline.ResolveMapGenBaseline();
            }

            if (pipeline != null &&
                pipeline.useDefinitiveTemplateBaseline &&
                template != null)
            {
                return Object.Instantiate(template);
            }

            var created = ScriptableObject.CreateInstance<MapGenConfig>();
            UwpBootstrapRuntimeDefaults.ApplyMapGen(created);
            return created;
        }
    }
}
