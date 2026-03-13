using UnityEngine;
using UnityEngine.Rendering;

namespace Project.Gameplay.Environment
{
    /// <summary>
    /// Configura automáticamente la iluminación de la escena al iniciar el juego (estilo Manor Lords / AoE IV).
    /// Luz cálida, sombras suaves, niebla atmosférica y skybox.
    /// </summary>
    public class RTSEnvironmentSetup : MonoBehaviour
    {
        [Header("Directional Light (Sol)")]
        [Tooltip("Si no asignas, se busca la primera Directional Light en la escena.")]
        public Light directionalLight;
        [Tooltip("Intensidad tipo RTS.")]
        [Range(0.5f, 2f)] public float sunIntensity = 1.35f;
        [Tooltip("Luz cálida (sol RTS).")]
        public Color sunColor = new Color(1f, 0.88f, 0.7f, 1f);
        [Tooltip("Rotación que da sombras largas y lectura clara.")]
        public Vector3 sunRotation = new Vector3(52f, -35f, 0f);
        [Tooltip("Tipo de sombra: Soft recomendado para RTS.")]
        public LightShadows shadowType = LightShadows.Soft;
        [Tooltip("Fuerza de la sombra (0-1).")]
        [Range(0f, 1f)] public float shadowStrength = 0.95f;

        [Header("Fog (atmósfera)")]
        public bool enableFog = true;
        [Tooltip("Modo Exponential para profundidad.")]
        public FogMode fogMode = FogMode.Exponential;
        [Tooltip("Densidad típica RTS.")]
        [Range(0.001f, 0.02f)] public float fogDensity = 0.0035f;
        [Tooltip("Azul grisáceo para atmósfera.")]
        public Color fogColor = new Color(0.56f, 0.65f, 0.75f, 1f);

        [Header("Skybox")]
        [Tooltip("Si no asignas, se usa RenderSettings.skybox actual.")]
        public Material skyboxMaterial;
        [Tooltip("Exposición del skybox (propiedad _Exposure si existe).")]
        [Range(0.5f, 2f)] public float skyboxExposure = 1.2f;

        void Start()
        {
            if (directionalLight == null)
            {
                var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                foreach (var l in lights)
                {
                    if (l.type == LightType.Directional)
                    {
                        directionalLight = l;
                        break;
                    }
                }
            }

            if (directionalLight != null)
            {
                directionalLight.type = LightType.Directional;
                directionalLight.intensity = sunIntensity;
                directionalLight.color = sunColor;
                directionalLight.transform.rotation = Quaternion.Euler(sunRotation);
                directionalLight.shadows = shadowType;
                directionalLight.shadowStrength = shadowStrength;
            }

            RenderSettings.fog = enableFog;
            if (enableFog)
            {
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogColor = fogColor;
            }

            if (skyboxMaterial != null)
                RenderSettings.skybox = skyboxMaterial;
            Material sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty("_Exposure"))
                sky.SetFloat("_Exposure", skyboxExposure);
        }
    }
}
