using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Project.Gameplay.Environment
{
    /// <summary>
    /// Aplica y cambia presets visuales (Stylized Fantasy, Anime Fantasy, Realistic RTS).
    /// Solo actualiza parámetros; no recrea materiales ni luces (optimizado para RTS).
    /// </summary>
    public class VisualPresetManager : MonoBehaviour
    {
        [Header("Visual Style")]
        [Tooltip("Presets disponibles. Arrastra aquí los ScriptableObjects VisualPreset.")]
        public VisualPreset[] presets = new VisualPreset[0];
        [Tooltip("Índice del preset actual (0 = primer preset).")]
        public int currentPresetIndex;

        [Header("Referencias (opcional; si no se asignan se buscan)")]
        public Light directionalLight;
        public Volume volume;
        public Material skyboxMaterial;
        [Tooltip("Material compartido de rim lighting (Project/RTS Rim Lighting). Si no se asigna, no se aplican rim del preset.")]
        public Material rimLightingMaterial;
        [Tooltip("Material de terreno que acepte _GrassColor / _Color. Opcional.")]
        public Material terrainMaterial;

        Light _sun;
        VisualPreset _lastApplied;

        void Awake()
        {
            ResolveReferences();
        }

        void Start()
        {
            if (presets != null && presets.Length > 0 && currentPresetIndex >= 0 && currentPresetIndex < presets.Length)
                ApplyPreset(presets[currentPresetIndex]);
        }

        void ResolveReferences()
        {
            _sun = directionalLight;
            if (_sun == null)
            {
                var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                foreach (var l in lights)
                {
                    if (l.type == LightType.Directional) { _sun = l; break; }
                }
            }
            if (volume == null)
                volume = FindFirstObjectByType<Volume>();
            if (skyboxMaterial == null && RenderSettings.skybox != null)
                skyboxMaterial = RenderSettings.skybox;
        }

        /// <summary>Aplica un preset al mundo (luz, niebla, skybox, post, rim, terreno).</summary>
        public void ApplyPreset(VisualPreset preset)
        {
            if (preset == null) return;
            _lastApplied = preset;

            if (_sun == null) ResolveReferences();

            if (_sun != null)
            {
                _sun.intensity = preset.lightIntensity;
                _sun.color = preset.lightColor;
                _sun.transform.rotation = Quaternion.Euler(preset.lightRotation);
                _sun.shadowStrength = preset.shadowStrength;
            }

            RenderSettings.fog = preset.fogEnabled;
            if (preset.fogEnabled)
            {
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogDensity = preset.fogDensity;
                RenderSettings.fogColor = preset.fogColor;
            }

            if (skyboxMaterial == null && RenderSettings.skybox != null)
                skyboxMaterial = RenderSettings.skybox;
            if (skyboxMaterial != null && skyboxMaterial.HasProperty("_Exposure"))
                skyboxMaterial.SetFloat("_Exposure", preset.skyboxExposure);

            if (volume != null && volume.profile != null)
                ApplyColorGrading(volume.profile, preset);

            if (rimLightingMaterial != null)
            {
                if (rimLightingMaterial.HasProperty("_RimColor")) rimLightingMaterial.SetColor("_RimColor", preset.rimColor);
                if (rimLightingMaterial.HasProperty("_RimIntensity")) rimLightingMaterial.SetFloat("_RimIntensity", preset.rimIntensity);
                if (rimLightingMaterial.HasProperty("_RimPower")) rimLightingMaterial.SetFloat("_RimPower", preset.rimPower);
            }

            if (terrainMaterial != null)
            {
                if (terrainMaterial.HasProperty("_GrassColor")) terrainMaterial.SetColor("_GrassColor", preset.grassColor);
                if (terrainMaterial.HasProperty("_ShadowTint")) terrainMaterial.SetColor("_ShadowTint", preset.shadowTint);
                if (terrainMaterial.HasProperty("_HighlightTint")) terrainMaterial.SetColor("_HighlightTint", preset.highlightTint);
            }
        }

        void ApplyColorGrading(VolumeProfile profile, VisualPreset preset)
        {
            if (profile.TryGet<ColorAdjustments>(out var colorAdj))
            {
                colorAdj.postExposure.Override(preset.postExposure);
                colorAdj.contrast.Override(preset.contrast);
                colorAdj.saturation.Override(preset.saturation);
            }
        }

        /// <summary>Cambia al preset por índice (para dropdown).</summary>
        public void SetPresetByIndex(int index)
        {
            if (presets == null || index < 0 || index >= presets.Length) return;
            currentPresetIndex = index;
            ApplyPreset(presets[index]);
        }

        /// <summary>Preset actualmente aplicado (solo lectura).</summary>
        public VisualPreset CurrentPreset => _lastApplied;
    }
}
