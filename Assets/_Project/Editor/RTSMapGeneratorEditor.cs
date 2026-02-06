using UnityEngine;
using UnityEditor;
using Project.Gameplay.Map;

namespace ProjectEditor.Map
{
    [CustomEditor(typeof(RTSMapGenerator))]
    public class RTSMapGeneratorEditor : Editor
    {
        static readonly (string guid, string prop)[] kResourcePrefabs = new[]
        {
            ("72ec9e209cefc2044a4a23649d7901eb", "treePrefab"),   // PF_Wood
            ("3c3299b03f1777140a0c711753318564", "berryPrefab"),  // PF_Food
            ("1a71c038b4729ee4d997beb5bacaf461", "goldPrefab"),   // PF_Gold
            ("48435e0ae3fee3940a6a544ed57e9123", "stonePrefab"),  // PF_Stone
            ("3124c9b701b5b34438e34ee372c0f552", "animalPrefab"), // PF_Animal
        };

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            RTSMapGenerator generator = (RTSMapGenerator)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Acciones del Generador", EditorStyles.boldLabel);

            if (GUILayout.Button("Asignar prefabs de recursos por defecto", GUILayout.Height(28)))
            {
                AssignDefaultResourcePrefabs(generator);
            }

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Generar Mapa", GUILayout.Height(30)))
            {
                if (Application.isPlaying)
                {
                    generator.Generate();
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", 
                        "El generador solo funciona en Play Mode.\nPresiona Play primero.", 
                        "OK");
                }
            }

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Debug: Estado del Generador", GUILayout.Height(25)))
            {
                generator.SendMessage("DebugGeneratorState");
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox(
                "RECURSOS: Si no ves árboles/recursos, pulsa \"Asignar prefabs de recursos por defecto\" y asegúrate de que Min Wood Trees sea alcanzable (p. ej. 15).\n\n" +
                "1. Terrain asignado\n" +
                "2. Prefabs de recursos asignados (o usar el botón arriba)\n" +
                "3. Play → el mapa se genera automáticamente", 
                MessageType.Info);
        }

        static void AssignDefaultResourcePrefabs(RTSMapGenerator generator)
        {
            SerializedObject so = new SerializedObject(generator);
            foreach (var (guid, propName) in kResourcePrefabs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    so.FindProperty(propName).objectReferenceValue = prefab;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(generator);
        }
    }
}
