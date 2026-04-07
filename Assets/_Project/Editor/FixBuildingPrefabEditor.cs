using UnityEngine;
using UnityEditor;
using Project.Gameplay.Buildings;
using Project.Gameplay.Map;

namespace ProjectEditor.Buildings
{
    /// <summary>
    /// Utilidad para reconfigurar un prefab de edificio después de cambiar el modelo:
    /// Layer Building, BuildingInstance con BuildingSO, BoxCollider si falta.
    /// Menú: Tools → Project → Reconfigurar prefab de edificio
    /// </summary>
    public static class FixBuildingPrefabEditor
    {
        const string kBuildingLayerName = "Building";

        [MenuItem("Tools/Project/Reconfigurar prefab de edificio")]
        public static void ShowWindow()
        {
            FixBuildingPrefabWindow.ShowWindow();
        }

        [MenuItem("Tools/Project/Reconfigurar prefab de edificio (selección actual)")]
        public static void FixSelectedPrefab()
        {
            if (Selection.activeObject == null)
            {
                EditorUtility.DisplayDialog("Reconfigurar prefab", "Selecciona un prefab en la ventana Project.", "OK");
                return;
            }

            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
            {
                EditorUtility.DisplayDialog("Reconfigurar prefab", "La selección no es un prefab (.prefab). Selecciona PF_House u otro prefab de edificio.", "OK");
                return;
            }

            BuildingSO so = AssetDatabase.LoadAssetAtPath<BuildingSO>(
                "Assets/_Project/04_Data/ScriptableObjecs/Buildings/House_SO.asset");
            if (so == null)
            {
                var soGuids = AssetDatabase.FindAssets("t:BuildingSO House_SO");
                if (soGuids.Length > 0)
                    so = AssetDatabase.LoadAssetAtPath<BuildingSO>(AssetDatabase.GUIDToAssetPath(soGuids[0]));
            }

            if (so == null)
            {
                EditorUtility.DisplayDialog("Reconfigurar prefab", "No se encontró House_SO. Asigna el BuildingSO manualmente en el prefab (BuildingInstance).", "OK");
            }

            bool ok = ApplyFixToPrefab(path, so);
            if (ok)
                Debug.Log($"Prefab reconfigurado: {path}");
            else
                EditorUtility.DisplayDialog("Reconfigurar prefab", "No se pudo reconfigurar. Revisa que el path sea correcto.", "OK");
        }

        public static bool ApplyFixToPrefab(string prefabPath, BuildingSO buildingSO)
        {
            if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab")) return false;

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null) return false;

            try
            {
                int buildingLayer = LayerMask.NameToLayer(kBuildingLayerName);
                if (buildingLayer < 0)
                {
                    Debug.LogWarning($"Layer \"{kBuildingLayerName}\" no existe. Créalo en Edit → Project Settings → Tags and Layers.");
                }
                else
                {
                    SetLayerRecursively(prefabRoot.transform, buildingLayer);
                }

                var bi = prefabRoot.GetComponent<BuildingInstance>();
                if (bi == null) bi = prefabRoot.AddComponent<BuildingInstance>();
                if (buildingSO != null) bi.buildingSO = buildingSO;

                float cellSize = GetEditorCellSize();
                var box = prefabRoot.GetComponent<BoxCollider>();
                if (box == null) box = prefabRoot.GetComponentInChildren<BoxCollider>();
                if (box == null)
                {
                    box = prefabRoot.AddComponent<BoxCollider>();
                    box.isTrigger = false;
                }
                // Tamaño del collider = huella del edificio (buildingSO.size × cellSize) para evitar boxes más grandes que el modelo y rutas raras
                if (bi.buildingSO != null)
                {
                    float w = bi.buildingSO.size.x * cellSize;
                    float d = bi.buildingSO.size.y * cellSize;
                    box.size = new Vector3(w, 2f, d);
                    box.center = new Vector3(0f, 1f, 0f);
                }
                else
                {
                    box.size = new Vector3(3f, 2f, 3f);
                    box.center = new Vector3(0f, 1f, 0f);
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        static void SetLayerRecursively(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursively(t.GetChild(i), layer);
        }

        /// <summary>CellSize en editor: MatchConfig.map.cellSize si existe.</summary>
        static float GetEditorCellSize()
        {
            var guids = AssetDatabase.FindAssets("t:MatchConfig");
            if (guids.Length > 0)
            {
                var config = AssetDatabase.LoadAssetAtPath<MatchConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (config != null) return Mathf.Max(0.01f, config.map.cellSize);
            }
            return MatchRuntimeState.DefaultCellSize;
        }
    }

    public class FixBuildingPrefabWindow : EditorWindow
    {
        GameObject prefabAsset;
        BuildingSO buildingSO;
        string prefabPath;

        public static void ShowWindow()
        {
            var w = GetWindow<FixBuildingPrefabWindow>(true, "Reconfigurar prefab edificio", true);
            w.minSize = new Vector2(320, 120);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Asigna el prefab de edificio (ej. PF_House) y el BuildingSO (ej. House_SO). " +
                "Se aplicará: Layer Building, BuildingInstance con el SO, BoxCollider con tamaño = huella (BuildingSO.size × cellSize) para que no sea más grande que el modelo y el pathfinding no genere rodeos.",
                MessageType.Info);

            EditorGUILayout.Space(4);
            prefabAsset = (GameObject)EditorGUILayout.ObjectField("Prefab", prefabAsset, typeof(GameObject), false);
            buildingSO = (BuildingSO)EditorGUILayout.ObjectField("Building SO", buildingSO, typeof(BuildingSO), false);

            if (prefabAsset == null && Selection.activeObject is GameObject go && AssetDatabase.GetAssetPath(go).EndsWith(".prefab"))
                prefabAsset = go;

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Reconfigurar prefab", GUILayout.Height(28)))
            {
                if (prefabAsset == null)
                {
                    EditorUtility.DisplayDialog("Error", "Asigna el prefab.", "OK");
                    return;
                }
                prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
                if (string.IsNullOrEmpty(prefabPath))
                {
                    EditorUtility.DisplayDialog("Error", "No se pudo obtener la ruta del prefab.", "OK");
                    return;
                }
                bool ok = FixBuildingPrefabEditor.ApplyFixToPrefab(prefabPath, buildingSO);
                if (ok) EditorUtility.DisplayDialog("Listo", "Prefab reconfigurado correctamente.", "OK");
                else EditorUtility.DisplayDialog("Error", "No se pudo guardar el prefab.", "OK");
            }
        }
    }
}
