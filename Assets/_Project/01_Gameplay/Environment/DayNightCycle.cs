using UnityEngine;

namespace Project.Gameplay.Environment
{
    /// <summary>
    /// Ciclo día/noche: rotación del sol, exposición del skybox, color de luz y niebla.
    /// Fases: Morning, Day, Sunset, Night. Ajustable en velocidad.
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        public enum Phase { Morning, Day, Sunset, Night }

        [Header("Referencias")]
        public Light sun;
        public Material skyboxMaterial;

        [Header("Ciclo")]
        [Tooltip("Duración en segundos de un ciclo completo (24h ficticios).")]
        public float cycleDurationSeconds = 300f;
        [Tooltip("Hora inicial 0-1 (0=medianoche, 0.25=amanecer, 0.5=mediodía, 0.75=atardecer).")]
        [Range(0f, 1f)] public float timeOfDay = 0.35f;
        [Tooltip("Si true, el ciclo avanza automáticamente.")]
        public bool autoAdvance = true;

        [Header("Sol")]
        [Tooltip("Rotación Y mínima (grados) = noche, máxima = día.")]
        public float sunRotationMinY = -90f;
        public float sunRotationMaxY = 90f;
        public Color sunColorDay = new Color(1f, 0.92f, 0.8f, 1f);
        public Color sunColorSunset = new Color(1f, 0.6f, 0.35f, 1f);
        public Color sunColorNight = new Color(0.5f, 0.55f, 0.75f, 1f);
        public float sunIntensityDay = 1.35f;
        public float sunIntensityNight = 0.15f;

        [Header("Skybox")]
        public float exposureDay = 1.2f;
        public float exposureNight = 0.4f;

        [Header("Fog")]
        public Color fogColorDay = new Color(0.56f, 0.65f, 0.75f, 1f);
        public Color fogColorSunset = new Color(0.7f, 0.5f, 0.45f, 1f);
        public Color fogColorNight = new Color(0.2f, 0.22f, 0.35f, 1f);

        Phase _phase = Phase.Day;

        void Awake()
        {
            if (sun == null)
                sun = FindFirstObjectByType<Light>(FindObjectsInactive.Include);
        }

        void Update()
        {
            if (autoAdvance && cycleDurationSeconds > 0f)
            {
                timeOfDay += Time.deltaTime / cycleDurationSeconds;
                if (timeOfDay >= 1f) timeOfDay -= 1f;
            }
            ApplyTimeOfDay();
        }

        void ApplyTimeOfDay()
        {
            float t = timeOfDay;
            float sunT = Mathf.Sin(t * Mathf.PI * 2f) * 0.5f + 0.5f;
            float sunY = Mathf.Lerp(sunRotationMinY, sunRotationMaxY, sunT);
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(50f, sunY, 0f);
                float intensity = Mathf.Lerp(sunIntensityNight, sunIntensityDay, sunT);
                Color color;
                if (t < 0.2f || t > 0.8f) color = Color.Lerp(sunColorNight, sunColorDay, t < 0.2f ? t / 0.2f : (t - 0.8f) / 0.2f);
                else if (t >= 0.2f && t < 0.35f) color = Color.Lerp(sunColorDay, sunColorSunset, (t - 0.2f) / 0.15f);
                else if (t >= 0.35f && t <= 0.5f) color = sunColorSunset;
                else if (t > 0.5f && t <= 0.65f) color = Color.Lerp(sunColorSunset, sunColorDay, (t - 0.5f) / 0.15f);
                else if (t > 0.65f && t <= 0.8f) color = sunColorDay;
                else color = Color.Lerp(sunColorDay, sunColorNight, (t - 0.8f) / 0.2f);
                sun.intensity = intensity;
                sun.color = color;
            }

            if (skyboxMaterial != null && skyboxMaterial.HasProperty("_Exposure"))
                skyboxMaterial.SetFloat("_Exposure", Mathf.Lerp(exposureNight, exposureDay, sunT));

            if (RenderSettings.fog)
            {
                if (t < 0.2f || t > 0.8f) RenderSettings.fogColor = Color.Lerp(fogColorNight, fogColorDay, t < 0.2f ? t / 0.2f : (t - 0.8f) / 0.2f);
                else if (t >= 0.35f && t <= 0.5f) RenderSettings.fogColor = fogColorSunset;
                else if (t >= 0.2f && t < 0.35f) RenderSettings.fogColor = Color.Lerp(fogColorDay, fogColorSunset, (t - 0.2f) / 0.15f);
                else if (t > 0.5f && t <= 0.65f) RenderSettings.fogColor = Color.Lerp(fogColorSunset, fogColorDay, (t - 0.5f) / 0.15f);
                else RenderSettings.fogColor = fogColorDay;
            }

            _phase = GetPhase(t);
        }

        Phase GetPhase(float t)
        {
            if (t >= 0.2f && t < 0.35f) return Phase.Morning;
            if (t >= 0.35f && t <= 0.5f) return Phase.Sunset;
            if (t > 0.5f && t < 0.8f) return Phase.Day;
            return Phase.Night;
        }

        public Phase CurrentPhase => _phase;
    }
}
