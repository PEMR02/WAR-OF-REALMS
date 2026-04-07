using UnityEngine;
using UnityEditor;
using Project.Gameplay.Buildings;
using Project.Gameplay.Map;

namespace ProjectEditor.Buildings
{
    /// <summary>
    /// Estándar RTS: añade o actualiza el child "Footprint" en prefabs de edificio.
    /// Footprint = objeto auxiliar con escala exacta (size × cellSize) para validar encaje sin depender del mesh.
    /// Menú: Tools → Project → Añadir/actualizar Footprint (edificio)
    /// </summary>
    public static class AddBuildingFootprintEditor
    {
        const string kFootprintName = "Footprint";
        const float kFootprintHeight = 0.1f;

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

        [MenuItem("Tools/Project/Añadir o actualizar Footprint (edificio seleccionado)")]
        public static void AddOrUpdateFootprintSelected()
        {
            if (Selection.activeObject == null)
            {
                EditorUtility.DisplayDialog("Footprint", "Selecciona un prefab de edificio en la ventana Project.", "OK");
                return;
            }

            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
            {
                EditorUtility.DisplayDialog("Footprint", "La selección no es un prefab (.prefab).", "OK");
                return;
            }

            bool ok = AddOrUpdateFootprintInPrefab(path);
            if (ok)
                Debug.Log($"[Footprint] Prefab actualizado: {path}");
            else
                EditorUtility.DisplayDialog("Footprint", "No se pudo actualizar. ¿El prefab tiene BuildingInstance con BuildingSO asignado?", "OK");
        }

        [MenuItem("Tools/Project/Añadir Footprint a TownCenter, House, Barracks")]
        public static void AddFootprintToMainThree()
        {
            string[] paths = new[]
            {
                "Assets/_Project/08_Prefabs/Buildings/PF_TownCenter.prefab",
                "Assets/_Project/08_Prefabs/Buildings/PF_House.prefab",
                "Assets/_Project/08_Prefabs/Buildings/PF_Barracks.prefab"
            };

            int done = 0;
            foreach (string p in paths)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(p) == null) continue;
                if (AddOrUpdateFootprintInPrefab(p)) done++;
            }

            Debug.Log($"[Footprint] Actualizados {done}/{paths.Length} prefabs.");
            if (done > 0)
                AssetDatabase.SaveAssets();
        }

        /// <summary>Añade o actualiza el child Footprint según BuildingSO.size × cellSize. Añade BuildingFootprintGizmo si falta.</summary>
        public static bool AddOrUpdateFootprintInPrefab(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab")) return false;

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) return false;

            try
            {
                var bi = root.GetComponent<BuildingInstance>();
                if (bi == null || bi.buildingSO == null)
                {
                    Debug.LogWarning($"[Footprint] Prefab sin BuildingInstance o BuildingSO: {prefabPath}");
                    return false;
                }

                float cellSize = GetEditorCellSize();
                float w = bi.buildingSO.size.x * cellSize;
                float d = bi.buildingSO.size.y * cellSize;

                Transform footprintTr = root.transform.Find(kFootprintName);
                GameObject footprintGo;
                if (footprintTr != null)
                {
                    footprintGo = footprintTr.gameObject;
                }
                else
                {
                    footprintGo = new GameObject(kFootprintName);
                    footprintGo.transform.SetParent(root.transform, false);
                }

                footprintGo.transform.localPosition = Vector3.zero;
                footprintGo.transform.localRotation = Quaternion.identity;
                footprintGo.transform.localScale = new Vector3(w, kFootprintHeight, d);

                var gizmo = footprintGo.GetComponent<BuildingFootprintGizmo>();
                if (gizmo == null)
                    footprintGo.AddComponent<BuildingFootprintGizmo>();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
