using UnityEngine;

namespace Project.Gameplay.Environment
{
    /// <summary>
    /// Aplica al inicio la configuración visual tipo Anno/RTS: skybox, niebla atmosférica y (opcional) sol.
    /// Colocar en un GameObject que exista en la escena principal (ej. GameplayBootstrap o mismo que WeatherManager).
    /// El skybox y la niebla también se pueden configurar en Window → Rendering → Lighting → Environment.
    /// </summary>
    public class RTSLightingBootstrap : MonoBehaviour
    {
        [Header("Skybox")]
        [Tooltip("Material del skybox (ej. Procedural). Si no asignas, no se cambia RenderSettings.skybox.")]
        public Material skyboxMaterial;

        [Header("Fog (atmósfera)")]
        [Tooltip("Activar niebla exponencial para dar profundidad al mapa.")]
        public bool enableFog = true;
        [Tooltip("Densidad de niebla (Exponential). ~0.004 da look Anno.")]
        [Range(0.001f, 0.02f)]
        public float fogDensity = 0.004f;
        [Tooltip("Color de la niebla (azul grisáceo típico RTS).")]
        public Color fogColor = new Color(0.55f, 0.62f, 0.7f, 1f);

        [Header("Sol (opcional)")]
        [Tooltip("Si asignas una luz direccional aquí, se aplican estos valores. Si no, solo se aplican skybox y fog.")]
        public Light directionalLight;
        [Tooltip("Intensidad recomendada para RTS.")]
        public float sunIntensity = 1.2f;
        [Tooltip("Color cálido tipo Anno.")]
        public Color sunColor = new Color(1f, 0.95f, 0.85f, 1f);
        [Tooltip("Rotación típica RTS: sombras largas (X=50, Y=-30).")]
        public Vector3 sunRotation = new Vector3(50f, -30f, 0f);

        void Start()
        {
            if (skyboxMaterial != null)
                RenderSettings.skybox = skyboxMaterial;

            RenderSettings.fog = enableFog;
            if (enableFog)
            {
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogColor = fogColor;
            }

            if (directionalLight != null)
            {
                directionalLight.transform.rotation = Quaternion.Euler(sunRotation);
                directionalLight.intensity = sunIntensity;
                directionalLight.color = sunColor;
            }
        }
    }
}
