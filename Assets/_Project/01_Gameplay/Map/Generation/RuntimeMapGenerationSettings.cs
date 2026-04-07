using Project.Gameplay.Map.Generation.Alpha;
using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace Project.Gameplay.Map.Generation
{
    /// <summary>
    /// Resultado único de <see cref="MatchConfigCompiler.Build"/> antes de generar terreno/agua/recursos.
    /// Contiene el <see cref="MapGenConfig"/> que consumirá <see cref="MapGenerator"/> y snapshots legibles para logs/diagnóstico.
    /// </summary>
    public sealed class RuntimeMapGenerationSettings
    {
        public MatchConfig SourceMatch { get; internal set; }
        public string SourceMatchName { get; internal set; }
        public MapGenerationProfile TechnicalProfile { get; internal set; }
        public string TechnicalProfileName { get; internal set; }
        public bool UsedSceneLegacyDefinitiveTemplate { get; internal set; }
        public bool UsedLegacyResourceFallbackFromScene { get; internal set; }
        public bool UsedHighLevelAlphaConfig { get; internal set; }

        internal void MarkLegacyResourceFallbackFromScene() => UsedLegacyResourceFallbackFromScene = true;

        public int ResolvedSeed { get; internal set; }
        public float ResolvedWaterHeight01 { get; internal set; }

        public ResourceRuntimeSettings Resources { get; internal set; } = new();

        /// <summary>Registro de relieve macro (alpha); null si no aplica.</summary>
        public TerrainFeatureRuntime TerrainFeatures { get; internal set; }

        /// <summary>Relleno tras <see cref="MapGenerator.Generate"/> si hubo clasificación.</summary>
        public SemanticRegionMap SemanticRegions { get; internal set; }

        /// <summary>Instancia runtime (HideAndDontSave) lista para <see cref="MapGenerator.Generate"/>.</summary>
        public MapGenConfig CompiledMapGen { get; internal set; }
    }
}
