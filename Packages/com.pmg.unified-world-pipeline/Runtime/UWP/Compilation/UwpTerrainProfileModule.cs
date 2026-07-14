using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline
{
    /// <summary>Módulo UWP: relieve, splat, orillas y skirt del terrain.</summary>
    public static class UwpTerrainProfileModule
    {
        public static void Apply(MapGenConfig cfg, PMGUnifiedWorldPipelineConfig pipeline)
        {
            if (cfg == null) return;

            float templateY = pipeline?.ResolveMapGenBaseline() is { terrainHeightWorld: > 1f } t
                ? t.terrainHeightWorld
                : 50f;
            cfg.terrainHeightWorld = Mathf.Clamp(templateY * 0.76f, 32f, 40f);
            cfg.terrainMaterialTemplateOverride = null;
            cfg.paintTerrainByHeight = true;

            ApplySplatLayerCap(cfg);
            EnsureSplatPercentDefaults(cfg);
            EnsureSkirtSettings(pipeline, cfg);

            cfg.macroTerrainEnabled = false;
            cfg.macroMountainMassCount = 0;
            cfg.macroBasinCount = 0;

            if (pipeline?.ResolveMapGenBaseline() is MapGenConfig baseline)
            {
                cfg.macroHillDensity = baseline.macroHillDensity;
                cfg.macroRoughnessWeight = baseline.macroRoughnessWeight;
                cfg.terrainMacroNoiseScale = baseline.terrainMacroNoiseScale;
                cfg.terrainMacroNoiseStrength = Mathf.Max(baseline.terrainMacroNoiseStrength, 0.15f);
            }
            else
            {
                cfg.macroHillDensity = 0.36f;
                cfg.macroRoughnessWeight = 0.42f;
                cfg.terrainMacroNoiseStrength = 0.15f;
            }

            cfg.regionNoiseScale = Mathf.Max(cfg.regionNoiseScale, 0.022f);
            cfg.shoreSmoothRadiusCells = Mathf.Max(cfg.shoreSmoothRadiusCells, 22);
            cfg.shoreSmoothStrength = Mathf.Max(cfg.shoreSmoothStrength, 0.52f);
            cfg.sandShoreCells = Mathf.Max(cfg.sandShoreCells, 5);
            cfg.terrainNormalSmoothingPasses = 3;
            cfg.terrainNormalSmoothingStrength = 0.36f;

            ApplyShallowerWaterBedCaps(cfg);
        }

        public static void ApplyJsonPresentation(MapGenConfig cfg, PMGUnifiedWorldPipelineConfig pipeline, float seaLevel01)
        {
            if (cfg == null) return;
            Apply(cfg, pipeline);
            cfg.waterHeight01 = Mathf.Clamp01(seaLevel01);
            UwpRiverProfileModule.ApplySoftCarves(cfg);
            UwpRiverProfileModule.ApplyReliabilityFix(cfg);
            UwpRiverProfileModule.ApplyCenterlineQuality(cfg);
        }

        internal static void ApplyShallowerWaterBedCaps(MapGenConfig cfg)
        {
            if (cfg == null) return;
            cfg.lakeBedDepthBelowWater01 = Mathf.Min(cfg.lakeBedDepthBelowWater01, 0.022f);
            cfg.lakeBedMinDepthBelowWater01 = Mathf.Min(cfg.lakeBedMinDepthBelowWater01, 0.008f);
            cfg.lakeBedDepthRampCells = Mathf.Max(cfg.lakeBedDepthRampCells, 10);
            // Troncal más profundo que tributario (no invertir).
            cfg.riverBedDepthBelowWater01 = Mathf.Clamp(cfg.riverBedDepthBelowWater01, 0.018f, 0.030f);
            cfg.tributaryBedDepthBelowWater01 = Mathf.Clamp(cfg.tributaryBedDepthBelowWater01, 0.008f, 0.015f);
        }

        static void ApplySplatLayerCap(MapGenConfig cfg)
        {
            if (cfg == null) return;
            // Dual-grass solo si hay albedo dry real. No forzar blend: Grass_02.png es máscara, no diffuse.
            cfg.grassDryBlendStrength = Mathf.Clamp(cfg.grassDryBlendStrength, 0f, 0.45f);
            cfg.terrainMoistureStrength = 0f;
            cfg.riverFordBedLayer = null;
            cfg.terrainAlphamapSmoothPasses = Mathf.Min(cfg.terrainAlphamapSmoothPasses, 1);
            cfg.terrainBlendSharpness = Mathf.Max(cfg.terrainBlendSharpness, 0.45f);
            cfg.textureBlendWidth = Mathf.Min(cfg.textureBlendWidth, 0.05f);
        }

        static void EnsureSplatPercentDefaults(MapGenConfig cfg)
        {
            if (cfg == null) return;
            float sum = cfg.grassPercent01 + cfg.dirtPercent01 + cfg.rockPercent01;
            if (sum > 0.01f) return;
            cfg.grassPercent01 = 0.6f;
            cfg.dirtPercent01 = 0.2f;
            cfg.rockPercent01 = 0.2f;
        }

        static void EnsureSkirtSettings(PMGUnifiedWorldPipelineConfig pipeline, MapGenConfig cfg)
        {
            if (cfg == null) return;
            cfg.showTerrainSkirt = true;
            MapGenConfig template = pipeline?.ResolveMapGenBaseline();
            if (template != null)
            {
                if (template.skirtDepth > 0f) cfg.skirtDepth = template.skirtDepth;
                if (template.skirtEdgeSamples > 0) cfg.skirtEdgeSamples = template.skirtEdgeSamples;
                if (template.skirtMaterial != null) cfg.skirtMaterial = template.skirtMaterial;
            }

            if (cfg.skirtMaterial == null)
                cfg.skirtMaterial = Resources.Load<Material>(TerrainSkirtBuilder.SkirtSoilMaterialResourceName);
        }
    }
}
