using Project.Gameplay.Map;
using UnityEditor;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline.Editor
{
    public static class PMGUnifiedWorldMenu
    {
        const string Root = UwpAssetPaths.PackageRoot;

        [MenuItem("PMG/Unified World Pipeline/Create Default Assets", false, 0)]
        public static void CreateDefaultAssets()
        {
            UwpBootstrapAssets.EnsureFolderTree();

            string checklistPath = UwpAssetPaths.Checklist;
            var checklist = AssetDatabase.LoadAssetAtPath<PMGUnifiedWorldChecklistAsset>(checklistPath);
            if (checklist == null)
            {
                checklist = ScriptableObject.CreateInstance<PMGUnifiedWorldChecklistAsset>();
                checklist.EnsureDefaultStructure();
                AssetDatabase.CreateAsset(checklist, checklistPath);
            }
            else
            {
                checklist.EnsureDefaultStructure();
                EditorUtility.SetDirty(checklist);
            }

            string configPath = UwpAssetPaths.PipelineConfig;
            var config = AssetDatabase.LoadAssetAtPath<PMGUnifiedWorldPipelineConfig>(configPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<PMGUnifiedWorldPipelineConfig>();
                AssetDatabase.CreateAsset(config, configPath);
            }

            config.checklist = checklist;
            UwpBootstrapAssets.WirePipelineConfig(config);
            config.EnsureDefaultsFromProject();
            config.PullVisualBindingsFromPipelineOnly();
            EditorUtility.SetDirty(config);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
            PMGUnifiedWorldPipelineWindow.Open();

            Debug.Log("[UWP] Assets creados. Abre Tools → PMG → Unified World Pipeline.");
        }

        [MenuItem("PMG/Unified World Pipeline/Open Window", false, 1)]
        public static void OpenWindow()
        {
            PMGUnifiedWorldPipelineWindow.Open();
        }

        [MenuItem("PMG/Unified World Pipeline/Evaluate Current Seed", false, 20)]
        public static void EvaluateMenu()
        {
            var config = AssetDatabase.LoadAssetAtPath<PMGUnifiedWorldPipelineConfig>(UwpAssetPaths.PipelineConfig);
            if (config == null)
            {
                CreateDefaultAssets();
                config = AssetDatabase.LoadAssetAtPath<PMGUnifiedWorldPipelineConfig>(UwpAssetPaths.PipelineConfig);
            }

            PMGUnifiedWorldGenerationResult gen = PMGUnifiedWorldPipelineRunner.GenerateLogic(config, null);
            PMGUnifiedWorldReportWriter.WriteSingle(gen.report, config);
            PMGUnifiedWorldPipelineRunner.CleanupTemp(ref gen);
            Debug.Log($"[UWP] {gen.report}");
        }

        [MenuItem("PMG/Unified World Pipeline/Probe Tributary Seeds", false, 21)]
        public static void ProbeTributarySeedsMenu()
        {
            var config = AssetDatabase.LoadAssetAtPath<PMGUnifiedWorldPipelineConfig>(UwpAssetPaths.PipelineConfig);
            if (config == null)
            {
                CreateDefaultAssets();
                config = AssetDatabase.LoadAssetAtPath<PMGUnifiedWorldPipelineConfig>(UwpAssetPaths.PipelineConfig);
            }

            PMGUnifiedWorldPipelineRunner.RunTributaryProbeBatch(config);
        }
    }
}