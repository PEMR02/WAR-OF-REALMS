using UnityEngine;

namespace Project.Gameplay.Environment
{
    /// <summary>
    /// Preset de estilo visual: luz, niebla, skybox, color grading, rim, terreno y vegetación.
    /// Permite cambiar el look del juego (Stylized Fantasy, Anime Fantasy, Realistic RTS) sin rehacer escena.
    /// </summary>
    [CreateAssetMenu(menuName = "Project/Visual Preset", fileName = "VisualPreset")]
    public class VisualPreset : ScriptableObject
    {
        [Header("Directional Light")]
        public float lightIntensity = 1.35f;
        public Color lightColor = new Color(1f, 0.88f, 0.7f, 1f);
        public Vector3 lightRotation = new Vector3(52f, -35f, 0f);
        [Range(0f, 1f)] public float shadowStrength = 0.95f;

        [Header("Fog")]
        public bool fogEnabled = true;
        [Range(0.001f, 0.02f)] public float fogDensity = 0.0035f;
        public Color fogColor = new Color(0.56f, 0.65f, 0.75f, 1f);

        [Header("Skybox")]
        [Range(0.3f, 2f)] public float skyboxExposure = 1.2f;

        [Header("Color Grading (Post)")]
        public float postExposure = 0.55f;
        [Range(-50f, 50f)] public float contrast = 28f;
        [Range(-50f, 50f)] public float saturation = 12f;

        [Header("Rim Lighting")]
        public Color rimColor = new Color(1f, 0.9f, 0.75f, 1f);
        [Range(0f, 1f)] public float rimIntensity = 0.2f;
        [Range(1f, 8f)] public float rimPower = 4f;

        [Header("Terrain Style (tints)")]
        public Color grassColor = new Color(0.4f, 0.55f, 0.25f, 1f);
        public Color shadowTint = new Color(0.4f, 0.5f, 0.7f, 1f);
        public Color highlightTint = new Color(1f, 0.95f, 0.8f, 1f);

        [Header("Vegetation")]
        [Range(0f, 2f)] public float vegetationSaturation = 1f;
        [Range(0f, 2f)] public float vegetationBrightness = 1f;
    }
}
