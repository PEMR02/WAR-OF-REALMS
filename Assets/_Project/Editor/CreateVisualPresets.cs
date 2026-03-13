using UnityEngine;
using UnityEditor;
using Project.Gameplay.Environment;

namespace ProjectEditor.Environment
{
    public static class CreateVisualPresets
    {
        const string kPresetsPath = "Assets/_Project/04_Data/VisualPresets";

        [MenuItem("Tools/Project/Crear presets visuales (Stylized / Anime / Realistic)")]
        public static void CreateAllPresets()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project")) return;
            EnsureFolder("Assets/_Project", "04_Data");
            EnsureFolder("Assets/_Project/04_Data", "VisualPresets");

            CreateStylizedFantasyPreset();
            CreateAnimeFantasyPreset();
            CreateRealisticRTSPreset();
            AssetDatabase.SaveAssets();
            Debug.Log("[Visual Presets] Creados: StylizedFantasy, AnimeFantasy, RealisticRTS en " + kPresetsPath);
        }

        static void EnsureFolder(string parent, string name)
        {
            if (AssetDatabase.IsValidFolder(parent + "/" + name)) return;
            AssetDatabase.CreateFolder(parent, name);
        }

        static void CreateStylizedFantasyPreset()
        {
            var p = ScriptableObject.CreateInstance<VisualPreset>();
            p.name = "StylizedFantasyPreset";
            p.lightIntensity = 1.35f;
            p.lightColor = new Color(1f, 0.88f, 0.7f, 1f);
            p.lightRotation = new Vector3(52f, -35f, 0f);
            p.shadowStrength = 0.95f;
            p.fogEnabled = true;
            p.fogDensity = 0.0035f;
            p.fogColor = new Color(0.56f, 0.65f, 0.75f, 1f);
            p.skyboxExposure = 1.2f;
            p.postExposure = 0.55f;
            p.contrast = 28f;
            p.saturation = 12f;
            p.rimColor = new Color(1f, 0.9f, 0.75f, 1f);
            p.rimIntensity = 0.2f;
            p.rimPower = 4f;
            p.grassColor = new Color(0.4f, 0.55f, 0.28f, 1f);
            p.shadowTint = new Color(0.45f, 0.52f, 0.72f, 1f);
            p.highlightTint = new Color(1f, 0.95f, 0.82f, 1f);
            p.vegetationSaturation = 1f;
            p.vegetationBrightness = 1f;
            SavePreset(p, "StylizedFantasyPreset");
        }

        static void CreateAnimeFantasyPreset()
        {
            var p = ScriptableObject.CreateInstance<VisualPreset>();
            p.name = "AnimeFantasyPreset";
            p.lightIntensity = 1.45f;
            p.lightColor = new Color(1f, 0.92f, 0.78f, 1f);
            p.lightRotation = new Vector3(50f, -30f, 0f);
            p.shadowStrength = 0.85f;
            p.fogEnabled = true;
            p.fogDensity = 0.0025f;
            p.fogColor = new Color(0.7f, 0.82f, 0.95f, 1f);
            p.skyboxExposure = 1.3f;
            p.postExposure = 0.65f;
            p.contrast = 22f;
            p.saturation = 30f;
            p.rimColor = new Color(1f, 0.95f, 0.9f, 1f);
            p.rimIntensity = 0.3f;
            p.rimPower = 3f;
            p.grassColor = new Color(0.35f, 0.75f, 0.3f, 1f);
            p.shadowTint = new Color(0.5f, 0.65f, 0.9f, 1f);
            p.highlightTint = new Color(1f, 0.98f, 0.75f, 1f);
            p.vegetationSaturation = 1.4f;
            p.vegetationBrightness = 1.2f;
            SavePreset(p, "AnimeFantasyPreset");
        }

        static void CreateRealisticRTSPreset()
        {
            var p = ScriptableObject.CreateInstance<VisualPreset>();
            p.name = "RealisticRTSPreset";
            p.lightIntensity = 1.2f;
            p.lightColor = new Color(0.98f, 0.95f, 0.9f, 1f);
            p.lightRotation = new Vector3(55f, -38f, 0f);
            p.shadowStrength = 0.98f;
            p.fogEnabled = true;
            p.fogDensity = 0.004f;
            p.fogColor = new Color(0.6f, 0.62f, 0.68f, 1f);
            p.skyboxExposure = 1.1f;
            p.postExposure = 0.45f;
            p.contrast = 18f;
            p.saturation = 5f;
            p.rimColor = new Color(0.9f, 0.88f, 0.85f, 1f);
            p.rimIntensity = 0.12f;
            p.rimPower = 5f;
            p.grassColor = new Color(0.38f, 0.48f, 0.28f, 1f);
            p.shadowTint = new Color(0.4f, 0.45f, 0.55f, 1f);
            p.highlightTint = new Color(0.95f, 0.92f, 0.88f, 1f);
            p.vegetationSaturation = 0.9f;
            p.vegetationBrightness = 0.95f;
            SavePreset(p, "RealisticRTSPreset");
        }

        static void SavePreset(VisualPreset p, string fileName)
        {
            string path = $"{kPresetsPath}/{fileName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VisualPreset>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(p, existing);
                EditorUtility.SetDirty(existing);
                return;
            }
            AssetDatabase.CreateAsset(p, path);
        }
    }
}
