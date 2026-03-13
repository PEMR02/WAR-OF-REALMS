using UnityEngine;
using UnityEditor;

namespace ProjectEditor.Environment
{
    /// <summary>
    /// Crea los assets de entorno para el estilo visual tipo Anno/RTS:
    /// - Material de skybox procedural (Skybox/Procedural) con valores recomendados.
    /// Menú: Tools → Project → Crear assets de entorno RTS (Skybox + instrucciones)
    /// </summary>
    public static class CreateRTSEnvironmentAssets
    {
        const string kEnvironmentPath = "Assets/_Project/06_Visual/Environment";
        const string kSkyboxMaterialName = "MAT_Skybox_Procedural_RTS";

        [MenuItem("Tools/Project/Crear assets de entorno RTS (Skybox procedural)")]
        public static void CreateRTSEnvironment()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project"))
            {
                Debug.LogError("[RTS Environment] No existe Assets/_Project.");
                return;
            }

            EnsureFolder("Assets/_Project", "06_Visual");
            EnsureFolder("Assets/_Project/06_Visual", "Environment");

            Shader procedural = Shader.Find("Skybox/Procedural");
            if (procedural == null)
            {
                Debug.LogWarning("[RTS Environment] No se encontró el shader Skybox/Procedural. Crea el material a mano: Assets → Create → Material, shader Skybox/Procedural. Ver VISUAL_ANNO_RTS_SETUP.md.");
                return;
            }

            string matPath = $"{kEnvironmentPath}/{kSkyboxMaterialName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(procedural);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat.shader = procedural;
            }

            if (procedural.name.Contains("Procedural"))
            {
                mat.SetFloat("_SunSize", 0.04f);
                mat.SetFloat("_AtmosphereThickness", 1.1f);
                mat.SetColor("_SkyTint", new Color(0.5f, 0.6f, 0.8f, 1f));
                mat.SetColor("_GroundColor", new Color(0.4f, 0.45f, 0.5f, 1f));
                mat.SetFloat("_Exposure", 1.2f);
                EditorUtility.SetDirty(mat);
            }

            AssetDatabase.SaveAssets();
            Selection.activeObject = mat;
            Debug.Log($"[RTS Environment] Material creado/actualizado: {matPath}. Asigna este material en Window → Rendering → Lighting → Environment → Skybox Material, o al componente RTSLightingBootstrap.");
        }

        static void EnsureFolder(string parent, string name)
        {
            if (AssetDatabase.IsValidFolder($"{parent}/{name}")) return;
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
