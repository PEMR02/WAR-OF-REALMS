using Project.Gameplay.Map.Generation;
using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace Project.Gameplay.Map.CleanWaterPipeline
{
    /// <summary>
    /// Punto de entrada único del pipeline de agua limpio para RTS Play.
    ///
    /// Fases (no duplicar lógica; solo orquestar el camino vivo del package UWP):
    /// 1. Compilación — <see cref="CleanWaterPipelineCompile"/>
    /// 2. Agua lógica Fase4 — <see cref="WaterGenerator"/> vía <see cref="MapGenerator.Generate"/>
    /// 3. Visual congelado Fase9 — <see cref="CleanWaterFrozenVisualBridge"/> → <see cref="UwpFrozenSurfacePipeline"/>
    ///
    /// Clases vivas del package (no usar copias en 09_Herramientaspruebas):
    /// WaterGenerator, RiverRouteGenerator, RiverSurfaceMeshBuilder, WaterMeshBuilder, TerrainExporter.
    /// </summary>
    public static class CleanWaterPipelineOrchestrator
    {
        public static RuntimeMapGenerationSettings CompileForPlay(
            MatchConfig match,
            MapGenConfig sceneLegacyDefinitiveTemplate,
            MapGenerationRuntimeContext runtimeContext = null,
            bool logSummary = true)
        {
            var runtime = CleanWaterPipelineCompile.Build(
                match,
                sceneLegacyDefinitiveTemplate,
                runtimeContext,
                logSummary);

            if (runtime?.CompiledMapGen != null)
                CleanWaterPipelineAudit.LogPostCompile(runtime.CompiledMapGen);

            return runtime;
        }

        /// <summary>Llamar tras generación exitosa para auditoría rápida en consola.</summary>
        public static void AuditAfterGenerate(GridSystem grid, MapGenConfig cfg) =>
            CleanWaterPipelineAudit.LogPostGenerate(grid, cfg);
    }
}
