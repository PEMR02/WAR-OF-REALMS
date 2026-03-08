using UnityEngine;
using UnityEditor;
using System.Linq;

namespace Project.Editor
{
    /// <summary>
    /// Herramienta para centrar el pivot de todos los prefabs de edificios.
    /// Resuelve el problema de edificios desplazados del grid.
    /// </summary>
    public class FixBuildingPivots : EditorWindow
    {
        [MenuItem("Tools/RTS/Fix Building Pivots (Center All)")]
        static void FixAllBuildingPivots()
        {
            if (!EditorUtility.DisplayDialog(
                "Fix Building Pivots",
                "Esto centrará el pivot de TODOS los prefabs de edificios.\n\n" +
                "Se buscarán prefabs en:\n" +
                "- Assets/_Project/Buildings/\n" +
                "- Assets/_Project/04_Data/ScriptableObjecs/Buildings/\n\n" +
                "¿Continuar?",
                "Sí, arreglar",
                "Cancelar"))
            {
                return;
            }

            int fixedCount = 0;
            int skipped = 0;

            // Buscar prefabs en carpetas de edificios
            string[] searchFolders = new[] 
            { 
                "Assets/_Project/Buildings",
                "Assets/_Project/04_Data",
                "Assets/Prefabs"  // Por si están aquí
            };

            string[] guids = AssetDatabase.FindAssets("t:Prefab", searchFolders);

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                if (prefab == null) continue;

                // Filtro: solo edificios (que tengan BuildingInstance o BuildingSO)
                bool isBuilding = prefab.GetComponent<Project.Gameplay.Buildings.BuildingInstance>() != null ||
                                  path.Contains("Building") || 
                                  path.Contains("House") || 
                                  path.Contains("TownCenter") ||
                                  path.Contains("Barracks");

                if (!isBuilding)
                {
                    skipped++;
                    continue;
                }

                if (CenterPivot(prefab, path))
                    fixedCount++;
                else
                    skipped++;
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog(
                "Completado",
                $"✅ Pivots arreglados: {fixedCount}\n" +
                $"⏭️ Omitidos: {skipped}\n\n" +
                "Ahora los edificios deberían estar centrados en el grid.",
                "OK");
        }

        static bool CenterPivot(GameObject prefab, string prefabPath)
        {
            try
            {
                // Cargar contenido del prefab
                GameObject instance = PrefabUtility.LoadPrefabContents(prefabPath);

                // Calcular bounds de todos los renderers
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    PrefabUtility.UnloadPrefabContents(instance);
                    return false;
                }

                Bounds bounds = renderers[0].bounds;
                foreach (var r in renderers)
                    bounds.Encapsulate(r.bounds);

                Vector3 worldCenter = bounds.center;
                
                // Offset para centrar (solo X y Z, Y se mantiene en el suelo)
                Vector3 offset = instance.transform.position - worldCenter;
                offset.y = 0; // Mantener altura original

                // Aplicar offset a todos los hijos (mueve el modelo, no el pivot)
                foreach (Transform child in instance.transform)
                {
                    child.position += offset;
                }

                // Guardar
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                PrefabUtility.UnloadPrefabContents(instance);

                Debug.Log($"✅ Pivot centrado: {prefab.name} (offset: {-offset})");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"⚠️ No se pudo arreglar {prefab.name}: {e.Message}");
                return false;
            }
        }

        [MenuItem("Tools/RTS/Fix Single Building Pivot (Selected)")]
        static void FixSelectedPivot()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("Error", "Selecciona un prefab en Project", "OK");
                return;
            }

            string path = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog("Error", "El objeto seleccionado no es un prefab", "OK");
                return;
            }

            if (CenterPivot(selected, path))
            {
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Completado", $"✅ Pivot centrado: {selected.name}", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "No se pudo centrar el pivot", "OK");
            }
        }
    }
}
