using UnityEngine;

namespace Project.Gameplay
{
    /// <summary>
    /// Ajusta la distancia de sombras según la cámara y opcionalmente la intensidad de sombras
    /// del sol para que se vean más marcadas (sobre todo a máximo zoom).
    /// </summary>
    public class ShadowDistanceFromCamera : MonoBehaviour
    {
        [Tooltip("Controlador de cámara RTS (para leer distancia de zoom). Si no se asigna, se usa la Main Camera para estimar.")]
        public RTSCameraController cameraController;

        [Tooltip("Distancia mínima de sombras (zoom cercano).")]
        public float minShadowDistance = 80f;

        [Tooltip("Distancia máxima de sombras (zoom lejano). Debe cubrir todo lo que abarca la cámara.")]
        public float maxShadowDistance = 280f;

        [Tooltip("Multiplicador: shadowDistance = distanciaCámara * factor, limitado por min/max. 2 = todo el frustum visible suele tener sombras.")]
        [Range(1.2f, 3f)]
        public float distanceFactor = 2f;

        [Tooltip("Actualizar cada N segundos para no tocar QualitySettings cada frame.")]
        public float updateInterval = 0.15f;

        [Header("Intensidad de sombras (opcional)")]
        [Tooltip("Si > 0, busca la luz direccional y aplica esta intensidad (0–1). Hace que las sombras se vean más oscuras.")]
        [Range(0f, 1f)]
        public float shadowStrength = 0.85f;

        private float _timer;
        private Light _dirLight;

        void Start()
        {
            if (cameraController == null)
                cameraController = FindFirstObjectByType<RTSCameraController>();

            if (shadowStrength > 0.01f)
            {
                var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i].type == LightType.Directional)
                    {
                        _dirLight = lights[i];
                        _dirLight.shadowStrength = shadowStrength;
                        break;
                    }
                }
            }
        }

        void LateUpdate()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = updateInterval;

            float camDistance = GetCameraDistance();
            float desired = Mathf.Clamp(camDistance * distanceFactor, minShadowDistance, maxShadowDistance);
            if (QualitySettings.shadowDistance < desired - 5f || QualitySettings.shadowDistance > desired + 5f)
                QualitySettings.shadowDistance = desired;

            if (_dirLight != null && shadowStrength > 0.01f && _dirLight.shadowStrength != shadowStrength)
                _dirLight.shadowStrength = shadowStrength;
        }

        float GetCameraDistance()
        {
            if (cameraController != null && cameraController.cam != null)
            {
                var zoomParent = cameraController.pivot != null ? cameraController.pivot : cameraController.transform;
                Vector3 local = zoomParent.InverseTransformPoint(cameraController.cam.transform.position);
                return Mathf.Abs(local.z);
            }

            if (Camera.main != null)
            {
                float dist = Vector3.Distance(Camera.main.transform.position, Vector3.zero);
                return dist;
            }

            return minShadowDistance;
        }
    }
}
