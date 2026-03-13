using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Project.Gameplay.Environment
{
    /// <summary>
    /// Aplica valores de post-processing tipo RTS (Manor Lords / AoE IV) al Global Volume si existe.
    /// Tonemapping ACES, Color Adjustments, Bloom, Ambient Occlusion.
    /// </summary>
    public class RTSPostProcessingSetup : MonoBehaviour
    {
        [Header("Volume")]
        [Tooltip("Si no asignas, se busca el primer Volume en la escena (global o no).")]
        public Volume volume;

        [Header("Color Adjustments")]
        public float postExposure = 0.55f;
        public float contrast = 28f;
        public float saturation = 12f;

        [Header("Bloom")]
        public float bloomThreshold = 0.9f;
        public float bloomIntensity = 0.35f;
        public float bloomScatter = 0.6f;

        [Header("Ambient Occlusion")]
        [Tooltip("En URP el SSAO se configura en el Renderer (SSAO feature). Estos valores son solo referencia: Intensity 0.45, Radius 0.25.")]
        public float aoIntensity = 0.45f;
        public float aoRadius = 0.25f;

        void Start()
        {
            if (volume == null)
                volume = FindFirstObjectByType<Volume>();
            if (volume == null || volume.profile == null) return;

            Apply(volume.profile);
        }

        void Apply(VolumeProfile profile)
        {
            if (profile.TryGet<ColorAdjustments>(out var colorAdj))
            {
                colorAdj.postExposure.Override(postExposure);
                colorAdj.contrast.Override(contrast);
                colorAdj.saturation.Override(saturation);
            }

            if (profile.TryGet<Bloom>(out var bloom))
            {
                bloom.threshold.Override(bloomThreshold);
                bloom.intensity.Override(bloomIntensity);
                bloom.scatter.Override(bloomScatter);
            }

            if (profile.TryGet<Tonemapping>(out var tonemapping))
                tonemapping.mode.Override(TonemappingMode.ACES);

            // Nota: en URP el Ambient Occlusion se configura en el Renderer (SSAO Renderer Feature), no como Volume override.
            // Valores recomendados en el asset del renderer: Intensity 0.45, Radius 0.25.
        }
    }
}
