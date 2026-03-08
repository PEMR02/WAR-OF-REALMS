using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Resources;

namespace Project.Gameplay.Map
{
    /// <summary>
    /// Atenúa solo los objetos que ya tienen FadeableByCamera (p. ej. árboles) cuando quedan entre la cámara y el foco.
    /// No se añade FadeableByCamera en runtime: así animales, rocas y oro no se vuelven transparentes al hacer zoom.
    /// </summary>
    public class CameraOcclusionFade : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("Cámara desde la que se detecta la oclusión.")]
        public Camera cam;
        [Tooltip("Solo se comprueban colliders en estos layers. Solo se atenúan objetos que YA tienen FadeableByCamera (p. ej. árboles). Asigna Resource para limitar el OverlapCapsule.")]
        public LayerMask fadeLayerMask = -1;

        [Header("Zona de fade")]
        [Tooltip("Distancia por delante de la cámara donde se considera el 'foco'. Objetos entre cámara y este punto se atenúan.")]
        public float focusDistance = 28f;
        [Tooltip("Radio alrededor del eje cámara→foco dentro del cual se atenuán objetos.")]
        public float fadeRadius = 12f;
        [Tooltip("Solo atenuar cuando el zoom (distancia cámara) está por debajo de este valor (zoom cercano). 0 = siempre.")]
        public float fadeOnlyWhenZoomBelow = 50f;

        private readonly HashSet<FadeableByCamera> _currentFaded = new HashSet<FadeableByCamera>();
        private readonly List<FadeableByCamera> _toUnfade = new List<FadeableByCamera>();
        private int _resourceLayer = -1;

        void Awake()
        {
            if (cam == null) cam = GetComponentInChildren<Camera>(true);
            if (cam == null) cam = Camera.main;
            _resourceLayer = LayerMaskToSingleLayer(fadeLayerMask);
        }

        void LateUpdate()
        {
            if (cam == null) return;

            float camDist = GetCameraDistance();
            if (fadeOnlyWhenZoomBelow > 0.01f && camDist > fadeOnlyWhenZoomBelow)
            {
                UnfadeAll();
                return;
            }

            Vector3 camPos = cam.transform.position;
            Vector3 focus = camPos + cam.transform.forward * focusDistance;
            float radiusSq = fadeRadius * fadeRadius;

            _toUnfade.Clear();
            foreach (var f in _currentFaded)
            {
                if (f == null) continue;
                if (!ShouldFade(camPos, focus, f.transform.position, radiusSq))
                    _toUnfade.Add(f);
            }
            foreach (var f in _toUnfade)
            {
                f.SetFadeTarget(1f);
                _currentFaded.Remove(f);
            }

            int layer = _resourceLayer >= 0 ? _resourceLayer : 0;
            if (_resourceLayer < 0)
            {
                for (int i = 0; i < 32; i++)
                {
                    if ((fadeLayerMask.value & (1 << i)) != 0) { layer = i; break; }
                }
            }

            Collider[] hits = Physics.OverlapCapsule(camPos, focus, fadeRadius, fadeLayerMask);
            for (int i = 0; i < hits.Length; i++)
            {
                var col = hits[i];
                if (col == null) continue;
                if (!ShouldFade(camPos, focus, col.transform.position, radiusSq)) continue;

                var fadeable = col.GetComponentInParent<FadeableByCamera>();
                if (fadeable == null)
                    fadeable = col.GetComponentInChildren<FadeableByCamera>();
                if (fadeable == null)
                    continue;

                fadeable.SetFadeTarget(0f);
                _currentFaded.Add(fadeable);
            }
        }

        bool ShouldFade(Vector3 camPos, Vector3 focus, Vector3 objPos, float radiusSq)
        {
            Vector3 toFocus = focus - camPos;
            float len = toFocus.magnitude;
            if (len < 0.01f) return false;
            Vector3 toObj = objPos - camPos;
            float t = Mathf.Clamp01(Vector3.Dot(toObj, toFocus) / (len * len));
            Vector3 closest = camPos + toFocus * t;
            float distSq = (objPos - closest).sqrMagnitude;
            return distSq <= radiusSq && t > 0.02f && t < 0.98f;
        }

        void UnfadeAll()
        {
            foreach (var f in _currentFaded)
            {
                if (f != null) f.SetFadeTarget(1f);
            }
            _currentFaded.Clear();
        }

        static int LayerMaskToSingleLayer(LayerMask mask)
        {
            int v = mask.value;
            if (v == 0) return -1;
            for (int i = 0; i < 32; i++)
            {
                if ((v & (1 << i)) != 0) return i;
            }
            return -1;
        }

        float GetCameraDistance()
        {
            if (cam == null) return 0f;
            var parent = cam.transform.parent;
            if (parent == null) return (cam.transform.position - transform.position).magnitude;
            Vector3 local = parent.InverseTransformPoint(cam.transform.position);
            return Mathf.Abs(local.z);
        }
    }
}
