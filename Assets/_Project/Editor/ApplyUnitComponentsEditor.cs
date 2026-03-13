using UnityEngine;
using UnityEditor;
using Project.Gameplay.Units;
using Project.Gameplay.Combat;

namespace ProjectEditor.Units
{
    /// <summary>
    /// Herramienta de editor: aplica los componentes base de una unidad plantilla (ej. Milicia / PF_Swordman)
    /// a los prefabs seleccionados. Útil para convertir modelos en unidades jugables sin configurar todo a mano.
    /// Menú: Tools → Project → Aplicar componentes de unidad (prefabs seleccionados)
    /// </summary>
    public class ApplyUnitComponentsEditor : EditorWindow
    {
        GameObject _templatePrefab;
        Vector2 _scroll;
        bool _copyAnimatorController = true;
        bool _copyNavMeshAgentSettings = true;
        bool _copyCollider = true;
        bool _copyHealthAndBar = true;
        bool _copyMovementAndAnim = true;
        bool _copySelectable = true;
        bool _copyUnitStats = true;
        string _status = "";

        static readonly System.Type[] kUnitComponentTypes = new System.Type[]
        {
            typeof(Animator),
            typeof(CapsuleCollider),
            typeof(UnityEngine.AI.NavMeshAgent),
            typeof(UnitSelectable),
            typeof(UnitMover),
            typeof(UnitAnimatorDriver),
            typeof(Health),
            typeof(WorldBarSettings),
            typeof(UnitStatsRuntime)
        };

        [MenuItem("Tools/Project/Aplicar componentes de unidad (prefabs seleccionados)")]
        public static void OpenWindow()
        {
            var w = GetWindow<ApplyUnitComponentsEditor>(false, "Componentes de unidad", true);
            w.minSize = new Vector2(360, 320);
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Asigna un prefab plantilla (ej. PF_Swordman / Milicia) y aplica sus componentes base a los prefabs que tengas seleccionados en la ventana Project.",
                MessageType.Info);
            EditorGUILayout.Space(4);

            _templatePrefab = (GameObject)EditorGUILayout.ObjectField("Plantilla (prefab unidad base)", _templatePrefab, typeof(GameObject), false);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Qué copiar", EditorStyles.boldLabel);
            _copyAnimatorController = EditorGUILayout.Toggle("Animator + Controller", _copyAnimatorController);
            _copyNavMeshAgentSettings = EditorGUILayout.Toggle("NavMeshAgent", _copyNavMeshAgentSettings);
            _copyCollider = EditorGUILayout.Toggle("CapsuleCollider", _copyCollider);
            _copyMovementAndAnim = EditorGUILayout.Toggle("UnitMover + UnitAnimatorDriver", _copyMovementAndAnim);
            _copySelectable = EditorGUILayout.Toggle("UnitSelectable", _copySelectable);
            _copyHealthAndBar = EditorGUILayout.Toggle("Health + WorldBarSettings", _copyHealthAndBar);
            _copyUnitStats = EditorGUILayout.Toggle("UnitStatsRuntime", _copyUnitStats);

            EditorGUILayout.Space(12);

            GUI.enabled = _templatePrefab != null && IsTemplateValid();
            if (GUILayout.Button("Aplicar a prefabs seleccionados", GUILayout.Height(32)))
            {
                ApplyToSelection();
            }
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(_status, MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        bool IsTemplateValid()
        {
            if (_templatePrefab == null) return false;
            string path = AssetDatabase.GetAssetPath(_templatePrefab);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab")) return false;
            return true;
        }

        void ApplyToSelection()
        {
            _status = "";

            if (_templatePrefab == null)
            {
                _status = "Asigna un prefab plantilla.";
                return;
            }

            string templatePath = AssetDatabase.GetAssetPath(_templatePrefab);
            if (string.IsNullOrEmpty(templatePath) || !templatePath.EndsWith(".prefab"))
            {
                _status = "La plantilla debe ser un prefab (.prefab).";
                return;
            }

            var targets = GetSelectedPrefabPaths();
            if (targets.Count == 0)
            {
                _status = "Selecciona uno o más prefabs en la ventana Project y vuelve a pulsar.";
                return;
            }

            GameObject templateRoot = PrefabUtility.LoadPrefabContents(templatePath);
            if (templateRoot == null)
            {
                _status = "No se pudo cargar la plantilla.";
                return;
            }

            int applied = 0;
            try
            {
                foreach (string targetPath in targets)
                {
                    if (targetPath == templatePath) continue;

                    if (ApplyTemplateToPrefab(templateRoot, targetPath))
                        applied++;
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                _status = $"Listo. Componentes aplicados a {applied} de {targets.Count} prefab(s).";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(templateRoot);
            }
        }

        System.Collections.Generic.List<string> GetSelectedPrefabPaths()
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (Object o in Selection.objects)
            {
                if (o == null) continue;
                string path = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab")) continue;
                list.Add(path);
            }
            return list;
        }

        bool ShouldCopyType(System.Type t)
        {
            if (t == typeof(Animator)) return _copyAnimatorController;
            if (t == typeof(UnityEngine.AI.NavMeshAgent)) return _copyNavMeshAgentSettings;
            if (t == typeof(CapsuleCollider)) return _copyCollider;
            if (t == typeof(UnitMover) || t == typeof(UnitAnimatorDriver)) return _copyMovementAndAnim;
            if (t == typeof(UnitSelectable)) return _copySelectable;
            if (t == typeof(Health) || t == typeof(WorldBarSettings)) return _copyHealthAndBar;
            if (t == typeof(UnitStatsRuntime)) return _copyUnitStats;
            return true;
        }

        bool ApplyTemplateToPrefab(GameObject templateRoot, string targetPrefabPath)
        {
            GameObject targetRoot = PrefabUtility.LoadPrefabContents(targetPrefabPath);
            if (targetRoot == null) return false;

            try
            {
                bool changed = false;
                foreach (System.Type compType in kUnitComponentTypes)
                {
                    if (!ShouldCopyType(compType)) continue;

                    var src = templateRoot.GetComponent(compType);
                    if (src == null) continue;

                    var dst = targetRoot.GetComponent(compType);
                    if (dst == null)
                    {
                        dst = targetRoot.AddComponent(compType);
                        if (dst == null) continue;
                        changed = true;
                    }

                    CopyComponentValues(src, dst);
                    changed = true;
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(targetRoot, targetPrefabPath);
                return changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(targetRoot);
            }
        }

        static void CopyComponentValues(Component source, Component destination)
        {
            if (source == null || destination == null || source.GetType() != destination.GetType()) return;
            EditorUtility.CopySerialized(source, destination);
        }
    }
}
