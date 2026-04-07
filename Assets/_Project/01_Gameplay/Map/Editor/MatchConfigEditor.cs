#if UNITY_EDITOR
using Project.Gameplay.Map;
using UnityEditor;
using UnityEngine;

namespace Project.Gameplay.Map.Editor
{
    [CustomEditor(typeof(MatchConfig))]
    public sealed class MatchConfigEditor : UnityEditor.Editor
    {
        static readonly string[] AlphaInspectorOrder =
        {
            "layout",
            "terrainShape",
            "hydrology",
            "regionClassification",
            "resourceDistribution",
            "playerSpawn",
            "visualBinding",
            "mapGenerationProfile",
            "climate",
            "startingLoadout",
            "graphics",
            "minimap"
        };

        static readonly string[] LegacyHiddenWhenAlpha =
        {
            "map", "geography", "water", "resources", "players"
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var alphaProp = serializedObject.FindProperty("useHighLevelAlphaConfig");
            EditorGUILayout.PropertyField(alphaProp);

            if (alphaProp.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Modo ALPHA: edita solo los bloques de alto nivel. " +
                    "map / geography / water / resources / players están ocultos: se regeneran al compilar (espejo). " +
                    "Clima y minimapa siguen aquí porque definen capas visuales del terreno.",
                    MessageType.Info);

                foreach (var name in AlphaInspectorOrder)
                {
                    var p = serializedObject.FindProperty(name);
                    if (p != null)
                        EditorGUILayout.PropertyField(p, true);
                }

                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Legacy (solo espejo — no editar)", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(true))
                {
                    foreach (var name in LegacyHiddenWhenAlpha)
                    {
                        var p = serializedObject.FindProperty(name);
                        if (p != null)
                            EditorGUILayout.PropertyField(p, true);
                    }
                }
            }
            else
            {
                DrawDefaultInspector();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
