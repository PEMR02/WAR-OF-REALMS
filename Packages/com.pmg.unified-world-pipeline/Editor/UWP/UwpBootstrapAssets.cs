using Project.Gameplay.Map;
using Project.Gameplay.Map.Generator;
using UnityEditor;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline.Editor
{
    /// <summary>Crea carpetas de salida y baselines UWP dentro del paquete UPM.</summary>
    public static class UwpBootstrapAssets
    {
        public static void EnsureFolderTree()
        {
            EnsureFolder("Assets", "PMGUnifiedWorldPipelineOutput");
            EnsureFolder(UwpAssetPaths.OutputRoot, "Reports");
            EnsureFolder(UwpAssetPaths.OutputRoot, "Sessions");
            if (!AssetDatabase.IsValidFolder(UwpAssetPaths.DefaultConfigRoot))
                EnsureFolder(UwpAssetPaths.ContentRoot, "DefaultConfig");
        }

        public static MapGenConfig EnsureMapGenBaseline()
        {
            EnsureFolderTree();
            var existing = AssetDatabase.LoadAssetAtPath<MapGenConfig>(UwpAssetPaths.MapGenBaseline);
            if (existing != null)
                return existing;

            var baseline = ScriptableObject.CreateInstance<MapGenConfig>();
            baseline.name = "UwpMapGenBaseline";
            ApplyUwpMapGenBaselineDefaults(baseline);
            AssetDatabase.CreateAsset(baseline, UwpAssetPaths.MapGenBaseline);
            return baseline;
        }

        public static MatchConfig EnsureMatchBaseline()
        {
            EnsureFolderTree();
            var existing = AssetDatabase.LoadAssetAtPath<MatchConfig>(UwpAssetPaths.MatchBaseline);
            if (existing != null)
                return existing;

            var baseline = ScriptableObject.CreateInstance<MatchConfig>();
            baseline.name = "UwpMatchBaseline";
            ApplyUwpMatchBaselineDefaults(baseline);
            AssetDatabase.CreateAsset(baseline, UwpAssetPaths.MatchBaseline);
            return baseline;
        }

        public static void WirePipelineConfig(PMGUnifiedWorldPipelineConfig config)
        {
            if (config == null) return;
            config.uwpMapGenBaseline = EnsureMapGenBaseline();
            config.uwpMatchBaseline = EnsureMatchBaseline();
            config.definitiveTemplate = config.uwpMapGenBaseline;
            config.matchConfig = config.uwpMatchBaseline;
            config.uwpIndependentMode = true;
            config.useDefinitiveTemplateBaseline = true;
            EditorUtility.SetDirty(config);
        }

        static void ApplyUwpMapGenBaselineDefaults(MapGenConfig cfg)
        {
            if (cfg == null) return;
            cfg.gridW = 358;
            cfg.gridH = 358;
            cfg.cellSizeWorld = 3f;
            cfg.seed = 424242;
            cfg.waterHeight01 = 0.24f;
            cfg.cityCount = 2;
            cfg.riverCount = 4;
            cfg.lakeCount = 2;
            cfg.maxLakeCells = 2000;
            cfg.macroTerrainEnabled = false;
            cfg.macroMountainMassCount = 0;
            cfg.macroBasinCount = 0;
            cfg.terrainHeightWorld = 38f;
            cfg.paintTerrainByHeight = true;
            cfg.debugLogs = false;
            cfg.debugHydrologyNetwork = false;
            cfg.debugRiverHydrologyPerf = false;
        }

        static void ApplyUwpMatchBaselineDefaults(MatchConfig match)
        {
            if (match == null) return;
            match.layout.mapWidth = 358;
            match.layout.mapHeight = 358;
            match.layout.gridCellSize = 3f;
            match.layout.centerMapAtOrigin = true;
            match.layout.seed = 424242;
            match.layout.playerCount = 2;
        }

        static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
