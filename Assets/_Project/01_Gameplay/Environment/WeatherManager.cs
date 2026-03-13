using UnityEngine;

namespace Project.Gameplay.Environment
{
    /// <summary>
    /// Clima que modifica temporalmente el preset: Sunny, Cloudy, Storm, Foggy.
    /// Transición suave (blend) entre estados. No recrea luces ni materiales.
    /// </summary>
    public class WeatherManager : MonoBehaviour
    {
        [Header("Referencias")]
        public Light sun;
        public Material skyboxMaterial;

        [Header("Presets por clima")]
        public float sunnyIntensity = 1.35f;
        public float cloudyIntensity = 0.7f;
        public float stormIntensity = 0.3f;
        public float foggyIntensity = 0.6f;

        public float sunnyFogDensity = 0.003f;
        public float cloudyFogDensity = 0.005f;
        public float stormFogDensity = 0.009f;
        public float foggyFogDensity = 0.012f;

        public float sunnySkyboxExposure = 1.2f;
        public float cloudySkyboxExposure = 0.9f;
        public float stormSkyboxExposure = 0.6f;
        public float foggySkyboxExposure = 0.75f;

        [Header("Blend")]
        [Tooltip("Tiempo en segundos para transición suave entre climas.")]
        public float blendDuration = 3f;

        public enum WeatherPreset
        {
            Sunny,
            Cloudy,
            Storm,
            Foggy
        }

        WeatherPreset _current = WeatherPreset.Sunny;
        float _blendT;
        float _fromIntensity, _toIntensity;
        float _fromFog, _toFog;
        float _fromExposure, _toExposure;
        bool _blending;

        void Awake()
        {
            if (sun == null)
                sun = FindFirstObjectByType<Light>(FindObjectsInactive.Include);
        }

        void Update()
        {
            if (!_blending || blendDuration <= 0f) return;
            _blendT += Time.deltaTime / blendDuration;
            if (_blendT >= 1f) { _blendT = 1f; _blending = false; }
            float t = Mathf.SmoothStep(0f, 1f, _blendT);
            if (sun != null) sun.intensity = Mathf.Lerp(_fromIntensity, _toIntensity, t);
            RenderSettings.fogDensity = Mathf.Lerp(_fromFog, _toFog, t);
            if (skyboxMaterial != null && skyboxMaterial.HasProperty("_Exposure"))
                skyboxMaterial.SetFloat("_Exposure", Mathf.Lerp(_fromExposure, _toExposure, t));
        }

        public void SetSunny() => Apply(WeatherPreset.Sunny);
        public void SetCloudy() => Apply(WeatherPreset.Cloudy);
        public void SetStorm() => Apply(WeatherPreset.Storm);
        public void SetFoggy() => Apply(WeatherPreset.Foggy);

        public void SetPreset(int index)
        {
            if (index < 0) index = 0;
            if (index > 3) index = 3;
            Apply((WeatherPreset)index);
        }

        public void Apply(WeatherPreset preset)
        {
            float targetIntensity = sunnyIntensity;
            float targetFog = sunnyFogDensity;
            float targetExposure = sunnySkyboxExposure;
            switch (preset)
            {
                case WeatherPreset.Cloudy:
                    targetIntensity = cloudyIntensity;
                    targetFog = cloudyFogDensity;
                    targetExposure = cloudySkyboxExposure;
                    break;
                case WeatherPreset.Storm:
                    targetIntensity = stormIntensity;
                    targetFog = stormFogDensity;
                    targetExposure = stormSkyboxExposure;
                    break;
                case WeatherPreset.Foggy:
                    targetIntensity = foggyIntensity;
                    targetFog = foggyFogDensity;
                    targetExposure = foggySkyboxExposure;
                    break;
            }

            if (blendDuration > 0f && Application.isPlaying)
            {
                _fromIntensity = sun != null ? sun.intensity : targetIntensity;
                _fromFog = RenderSettings.fogDensity;
                _fromExposure = (skyboxMaterial != null && skyboxMaterial.HasProperty("_Exposure")) ? skyboxMaterial.GetFloat("_Exposure") : targetExposure;
                _toIntensity = targetIntensity;
                _toFog = targetFog;
                _toExposure = targetExposure;
                _blendT = 0f;
                _blending = true;
            }
            else
            {
                if (sun != null) sun.intensity = targetIntensity;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogDensity = targetFog;
                if (skyboxMaterial != null && skyboxMaterial.HasProperty("_Exposure"))
                    skyboxMaterial.SetFloat("_Exposure", targetExposure);
            }
            _current = preset;
        }

        public WeatherPreset Current => _current;
    }
}
