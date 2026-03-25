using UnityEngine;
using UnityEditor;
using UnityEngine.AI;
using Project.Gameplay.Buildings;

namespace ProjectEditor.Buildings
{
    /// <summary>
    /// Añade NavMeshObstacle (y asegura GateOpener) en el prefab Muro_Puerta para que pathfinding y puerta funcionen sin depender solo del runtime.
    /// Menú: Tools → Project → Configurar prefab Muro_Puerta
    /// </summary>
    public static class SetupGatePrefabEditor
    {
        const string GatePrefabPath = "Assets/_Project/08_Prefabs/Buildings/Muro_01/Muro_Puerta.prefab";

        [MenuItem("Tools/Project/Configurar prefab Muro_Puerta (GateController + carving + trigger)")]
        public static void SetupMuroPuertaPrefab()
        {
            string path = GatePrefabPath;
            if (!System.IO.File.Exists(path))
            {
                var guids = AssetDatabase.FindAssets("Muro_Puerta t:Prefab");
                if (guids.Length > 0)
                    path = AssetDatabase.GUIDToAssetPath(guids[0]);
            }
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
            {
                EditorUtility.DisplayDialog("Muro_Puerta", "No se encontró el prefab Muro_Puerta.", "OK");
                return;
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
            if (prefabRoot == null)
            {
                EditorUtility.DisplayDialog("Muro_Puerta", "No se pudo cargar el prefab.", "OK");
                return;
            }

            try
            {
                bool changed = false;

                // GateController (nuevo sistema). Mantener compatibilidad con GateOpener si existe, pero no lo forzamos.
                var gateCtrl = prefabRoot.GetComponent<GateController>();
                if (gateCtrl == null)
                {
                    gateCtrl = prefabRoot.AddComponent<GateController>();
                    changed = true;
                }

                var obs = prefabRoot.GetComponent<NavMeshObstacle>();
                if (obs == null)
                {
                    obs = prefabRoot.AddComponent<NavMeshObstacle>();
                    changed = true;
                }
                if (obs != null)
                {
                    obs.shape = NavMeshObstacleShape.Box;
                    obs.size = new Vector3(4f, 3f, 2f);
                    obs.center = Vector3.zero;
                    obs.carving = true;
                    obs.carveOnlyStationary = false;
                    obs.enabled = true;
                }

                // Trigger + Entry/Exit (hijos)
                var trigger = prefabRoot.transform.Find("GateTrigger");
                if (trigger == null)
                {
                    var go = new GameObject("GateTrigger");
                    trigger = go.transform;
                    trigger.SetParent(prefabRoot.transform, false);
                    var box = go.AddComponent<BoxCollider>();
                    box.isTrigger = true;
                    box.center = new Vector3(0f, 1.25f, 0f);
                    box.size = new Vector3(5f, 2.5f, 3f);
                    changed = true;
                }
                else
                {
                    var box = trigger.GetComponent<BoxCollider>();
                    if (box == null)
                    {
                        box = trigger.gameObject.AddComponent<BoxCollider>();
                        changed = true;
                    }
                    if (box != null)
                    {
                        box.isTrigger = true;
                    }
                }

                var entry = prefabRoot.transform.Find("EntryPoint");
                if (entry == null)
                {
                    entry = new GameObject("EntryPoint").transform;
                    entry.SetParent(prefabRoot.transform, false);
                    entry.localPosition = new Vector3(0f, 0f, -2.0f);
                    changed = true;
                }
                var exit = prefabRoot.transform.Find("ExitPoint");
                if (exit == null)
                {
                    exit = new GameObject("ExitPoint").transform;
                    exit.SetParent(prefabRoot.transform, false);
                    exit.localPosition = new Vector3(0f, 0f, 2.0f);
                    changed = true;
                }

                if (gateCtrl != null)
                {
                    gateCtrl.obstacle = obs;
                    gateCtrl.gateCenter = prefabRoot.transform;
                    gateCtrl.entryPoint = entry;
                    gateCtrl.exitPoint = exit;
                    changed = true;
                }

                if (changed || obs != null)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                    AssetDatabase.Refresh();
                    Debug.Log($"Muro_Puerta configurado: {path} (GateController, NavMeshObstacle carving=true, Trigger + Entry/Exit).");
                    EditorUtility.DisplayDialog("Muro_Puerta", "Prefab configurado: GateController, NavMeshObstacle carving=true, GateTrigger (IsTrigger), EntryPoint y ExitPoint.", "OK");
                }
                else
                    EditorUtility.DisplayDialog("Muro_Puerta", "El prefab ya tenía los componentes.", "OK");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
    }
}
