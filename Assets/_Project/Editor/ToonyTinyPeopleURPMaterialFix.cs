using UnityEngine;
using UnityEditor;

namespace ProjectEditor
{
    /// <summary>
    /// Convierte los materiales del paquete ToonyTinyPeople de Built-in (Standard) a URP,
    /// para que los prefabs dejen de verse en magenta cuando el proyecto usa Universal Render Pipeline.
    /// Menú: Tools → Project → Convertir materiales ToonyTinyPeople a URP
    /// </summary>
    public static class ToonyTinyPeopleURPMaterialFix
    {
        const string ToonyTinyRoot = "Assets/ToonyTinyPeople";
        const string URP_Lit = "Universal Render Pipeline/Lit";
        const string URP_ParticlesUnlit = "Universal Render Pipeline/Particles/Unlit";
        const string URP_ParticlesLit = "Universal Render Pipeline/Particles/Lit";
        const string URP_SimpleLit = "Universal Render Pipeline/Simple Lit";

        [MenuItem("Tools/Project/Convertir materiales ToonyTinyPeople a URP")]
        public static void ConvertAllToonyTinyMaterialsToURP()
        {
            var shaderLit = Shader.Find(URP_Lit);
            var shaderParticlesUnlit = Shader.Find(URP_ParticlesUnlit);
            var shaderParticlesLit = Shader.Find(URP_ParticlesLit);
            var shaderSimpleLit = Shader.Find(URP_SimpleLit);

            if (shaderLit == null)
            {
                EditorUtility.DisplayDialog("ToonyTinyPeople → URP",
                    "No se encontró el shader URP/Lit. ¿Tienes instalado el paquete Universal RP?\n\n" +
                    "En Window → Package Manager verifica que 'Universal RP' esté instalado.", "OK");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { ToonyTinyRoot });
            int converted = 0;
            int skipped = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                bool needsConversion = IsBuiltInShader(mat);
                if (!needsConversion)
                {
                    skipped++;
                    continue;
                }

                // Partículas / efectos: nombre típico o cola de render
                bool isParticle = mat.renderQueue >= 3000 || mat.name.Contains("Explo") ||
                    mat.name.Contains("Flames") || mat.name.Contains("Smoke") || mat.name.Contains("Sparks") ||
                    mat.name.Contains("Blood") || mat.name.Contains("Wave") || mat.name.Contains("Glow");

                Shader targetShader = shaderLit;
                if (isParticle)
                    targetShader = shaderParticlesUnlit != null ? shaderParticlesUnlit : shaderParticlesLit;

                if (targetShader == null)
                    targetShader = shaderLit;

                ConvertMaterialToURP(mat, targetShader);
                EditorUtility.SetDirty(mat);
                converted++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("ToonyTinyPeople → URP",
                $"Listo.\nConvertidos: {converted}\nOmitidos (ya URP): {skipped}", "OK");
        }

        static bool IsBuiltInShader(Material mat)
        {
            if (mat.shader == null) return true;
            string name = mat.shader.name;
            // Magenta / error
            if (string.IsNullOrEmpty(name) || (name.Contains("Hidden/") && name.Contains("Error")))
                return true;
            // Built-in: "Standard", "Legacy Shaders/...", "Particles/...", "Mobile/...", etc.
            return name == "Standard" || name.StartsWith("Legacy Shaders/") ||
                name.StartsWith("Particles/") || name.StartsWith("Mobile/");
        }

        static void ConvertMaterialToURP(Material mat, Shader urpShader)
        {
            // Guardar propiedades que usan ambos pipelines
            Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            Color tintColor = mat.HasProperty("_TintColor") ? mat.GetColor("_TintColor") : new Color(0.5f, 0.5f, 0.5f, 0.5f);

            mat.shader = urpShader;

            if (mainTex != null && mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", mainTex);
            else if (mainTex != null && mat.HasProperty("_MainTex"))
                mat.SetTexture("_BaseMap", mainTex);

            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            if (mat.HasProperty("_TintColor"))
                mat.SetColor("_TintColor", tintColor);
        }
    }
}
