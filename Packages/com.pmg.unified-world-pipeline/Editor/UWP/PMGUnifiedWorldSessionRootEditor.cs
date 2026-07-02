using UnityEditor;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline.Editor
{
    [CustomEditor(typeof(PMGUnifiedWorldSessionRoot))]
    public class PMGUnifiedWorldSessionRootEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var root = (PMGUnifiedWorldSessionRoot)target;

            EditorGUILayout.LabelField("PMG Unified World Session", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Regenerar mundo", GUILayout.Height(28)))
            {
                if (root.pipelineConfig != null)
                    PMGUnifiedWorldPipelineRunner.ApplyFullToScene(root.pipelineConfig, root.lastSeed, root);
            }

            if (GUILayout.Button("Abrir pipeline"))
                PMGUnifiedWorldPipelineWindow.Open();
            EditorGUILayout.EndHorizontal();

            if (root.lastReport.aspects != null && root.lastReport.aspects.Length > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(
                    $"Nota: {root.lastReport.totalGrade0To10:F1}/10 ({root.lastReport.totalGradeLetter}) seed={root.lastSeed}",
                    EditorStyles.boldLabel);

                for (int i = 0; i < root.lastReport.aspects.Length; i++)
                {
                    PMGUnifiedWorldAspectScore a = root.lastReport.aspects[i];
                    EditorGUILayout.LabelField($"{a.aspect}: {a.score0To10:F1} ({a.gradeLetter}) — {a.details}");
                }
            }

            EditorGUILayout.Space();
            root.manualVisualWaterScore = EditorGUILayout.Slider("Nota visual manual (agua)", root.manualVisualWaterScore, 0f, 10f);
            root.sessionNotes = EditorGUILayout.TextArea(root.sessionNotes, GUILayout.MinHeight(48f));

            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
