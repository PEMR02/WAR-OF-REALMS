using UnityEngine;
using UnityEngine.InputSystem;
using Project.Gameplay.Map;

namespace Project.Gameplay
{
    /// <summary>
    /// Controlador RTS estilo Age of Empires II: rig se mueve en XZ, pivot fija el pitch, cámara hace zoom por distancia (eje Z local).
    /// FOV fijo; zoom = acercar/alejar la cámara en su eje local Z.
    /// </summary>
    public class RTSCameraController : MonoBehaviour
    {
        [Header("Refs — ¡asigna Cam! Si está en None no controla ninguna cámara")]
        [Tooltip("Cámara que se usa para renderizar. Debe ser la Main Camera o la que quieras controlar.")]
        public Camera cam;
        [Tooltip("Opcional: pivote bajo el rig que solo rota en X (pitch). Si hay Rig > Pivot > Camera, asígnale el Pivot.")]
        public Transform pivot;
        public Project.Gameplay.Units.RTSSelectionController selection;

        [Header("Bounds — elige una fuente para que calcen con el mapa")]
        [Tooltip("Opcional: si se asigna, los límites se calculan del tamaño del terreno (terrainData.size) + margen.")]
        public Terrain terrainForBounds;
        [Tooltip("Opcional: alternativa a Terrain; usa width/height/origin del grid.")]
        public MapGrid mapGridForBounds;
        [Tooltip("Margen extra para no ver el borde del mapa (10–25 típico).")]
        public float boundsMargin = 15f;
        [Tooltip("Si no usas Terrain ni MapGrid, se usan estos valores (ej. mapa 256 centrado: -128..128).")]
        public Vector2 minBounds = new Vector2(-128f, -128f);
        public Vector2 maxBounds = new Vector2(128f, 128f);

        [Header("Move — el rig (este objeto) se mueve en XZ")]
        public float moveSpeed = 25f;
        public float edgeSpeed = 20f;
        public float edgeSize = 12f;
        public bool edgeScroll = true;
        [Tooltip("Si true, la velocidad de movimiento escala con el zoom: más lejos = más rápido (estilo AoE2).")]
        public bool speedScaleWithZoom = true;
        [Range(0.5f, 1f)]
        [Tooltip("Velocidad mínima al zoom in (1 = sin reducir).")]
        public float minSpeedFactor = 0.5f;

        [Header("Edge Scroll Cooldown")]
        public float edgeCooldownAfterSelection = 0.2f;

        [Header("Drag Pan (Middle Mouse)")]
        public bool middleMouseDrag = true;
        [Tooltip("Sensibilidad del arrastre (0.5–1.5 típico; 0.08 era muy lento).")]
        public float dragSpeed = 0.8f;

        [Header("Zoom — por distancia (cámara local Z), FOV fijo estilo AoE2")]
        [Tooltip("Distancia mínima (zoom in).")]
        public float minDistance = 18f;
        [Tooltip("Distancia máxima (zoom out); mapa grande = subir este valor.")]
        public float maxDistance = 70f;
        [Tooltip("Sensibilidad rueda aplicada a distancia (120 típico).")]
        public float zoomSpeed = 120f;
        [Tooltip("FOV fijo en perspectiva (40–45 típico). No se usa para zoom.")]
        [Range(30f, 60f)]
        public float fixedFov = 45f;

        [Header("Pitch fijo (ángulo de la cámara)")]
        [Tooltip("Inclinación del pivot en grados (50–60° estilo AoE2).")]
        [Range(30f, 75f)]
        public float pitchAngle = 55f;

        private Vector2 _lastMousePos;
        private bool _dragging;
        private bool _prevSelectionDragging;
        private float _edgeCooldownTimer;
        private float _resolveTimer;
        private float _selectionResolveTimer;

        /// <summary>Transform que se mueve en XZ (rig). Siempre este objeto.</summary>
        Transform Rig => transform;

        /// <summary>Transform cuyo eje local Z es la distancia de la cámara (pivot o rig si no hay pivot).</summary>
        Transform ZoomParent => pivot != null ? pivot : Rig;

        float CurrentDistance
        {
            get
            {
                if (cam == null) return maxDistance;
                Vector3 local = ZoomParent.InverseTransformPoint(cam.transform.position);
                return Mathf.Abs(local.z);
            }
            set
            {
                if (cam == null) return;
                float clamped = Mathf.Clamp(value, minDistance, maxDistance);
                if (cam.transform.parent == ZoomParent)
                {
                    Vector3 local = cam.transform.localPosition;
                    local.z = -clamped;
                    cam.transform.localPosition = local;
                }
                else
                {
                    cam.transform.position = ZoomParent.position - ZoomParent.forward * clamped;
                }
            }
        }

        void Awake()
        {
            ResolveCamera();
        }

        void Start()
        {
            ResolveCamera();
            RefreshBoundsFromMapOrTerrain();
            ApplyFixedPitch();
            ApplyFixedFov();
            ClampDistance();
        }

        /// <summary>Asegura que Cam esté asignada: hijos, GetComponent, o Main.</summary>
        void ResolveCamera()
        {
            if (cam != null) return;
            cam = GetComponentInChildren<Camera>(true);
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
#if UNITY_EDITOR
            if (cam == null) Debug.LogWarning("RTSCameraController: Cam sigue en None. Asigna la Main Camera en el Inspector.");
#endif
        }

        void RefreshBoundsFromMapOrTerrain()
        {
            if (terrainForBounds != null && terrainForBounds.terrainData != null)
            {
                Vector3 pos = terrainForBounds.transform.position;
                Vector3 size = terrainForBounds.terrainData.size;
                minBounds = new Vector2(pos.x - boundsMargin, pos.z - boundsMargin);
                maxBounds = new Vector2(pos.x + size.x + boundsMargin, pos.z + size.z + boundsMargin);
                return;
            }
            if (mapGridForBounds == null)
                mapGridForBounds = FindFirstObjectByType<MapGrid>();
            if (mapGridForBounds != null && mapGridForBounds.IsReady)
            {
                float w = mapGridForBounds.width * mapGridForBounds.cellSize;
                float h = mapGridForBounds.height * mapGridForBounds.cellSize;
                Vector3 o = mapGridForBounds.origin;
                minBounds = new Vector2(o.x - boundsMargin, o.z - boundsMargin);
                maxBounds = new Vector2(o.x + w + boundsMargin, o.z + h + boundsMargin);
            }
        }

        void ApplyFixedPitch()
        {
            if (pivot == null) return;
            pivot.localRotation = Quaternion.Euler(pitchAngle, 0f, 0f);
        }

        void ApplyFixedFov()
        {
            if (cam == null || cam.orthographic) return;
            cam.fieldOfView = fixedFov;
        }

        void ClampDistance()
        {
            CurrentDistance = CurrentDistance;
        }

        void Update()
        {
            // Evitar búsquedas por frame; resolver con throttle.
            if (cam == null)
            {
                _resolveTimer -= Time.unscaledDeltaTime;
                if (_resolveTimer <= 0f)
                {
                    _resolveTimer = 1.0f;
                    ResolveCamera();
                }
                if (cam == null) return;
            }

            if (selection == null)
            {
                _selectionResolveTimer -= Time.unscaledDeltaTime;
                if (_selectionResolveTimer <= 0f)
                {
                    _selectionResolveTimer = 1.0f;
                    selection = FindFirstObjectByType<Project.Gameplay.Units.RTSSelectionController>();
                }
            }

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            float dt = Time.unscaledDeltaTime;
            bool selectionDragging = selection != null && selection.IsDraggingSelection;

            if (_prevSelectionDragging && !selectionDragging)
                _edgeCooldownTimer = edgeCooldownAfterSelection;
            _prevSelectionDragging = selectionDragging;
            if (_edgeCooldownTimer > 0f)
                _edgeCooldownTimer -= dt;
            bool edgeBlocked = selectionDragging || _edgeCooldownTimer > 0f;

            float dist = CurrentDistance;
            float speedMul = 1f;
            if (speedScaleWithZoom && maxDistance > minDistance)
                speedMul = Mathf.Lerp(minSpeedFactor, 1f, (dist - minDistance) / (maxDistance - minDistance));

            // 1) WASD — mueve el rig
            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) move += Vector3.forward;
            if (kb.sKey.isPressed) move += Vector3.back;
            if (kb.aKey.isPressed) move += Vector3.left;
            if (kb.dKey.isPressed) move += Vector3.right;
            if (move.sqrMagnitude > 0.001f)
            {
                move.Normalize();
                Rig.position += move * (moveSpeed * speedMul) * dt;
            }

            // 2) Edge scroll
            if (edgeScroll && !_dragging && !edgeBlocked)
            {
                Vector2 mp = mouse.position.ReadValue();
                Vector3 edgeMove = Vector3.zero;
                if (mp.x <= edgeSize) edgeMove += Vector3.left;
                if (mp.x >= Screen.width - edgeSize) edgeMove += Vector3.right;
                if (mp.y <= edgeSize) edgeMove += Vector3.back;
                if (mp.y >= Screen.height - edgeSize) edgeMove += Vector3.forward;
                if (edgeMove.sqrMagnitude > 0.001f)
                {
                    edgeMove.Normalize();
                    Rig.position += edgeMove * (edgeSpeed * speedMul) * dt;
                }
            }

            // 3) Middle mouse drag — mueve el rig
            if (middleMouseDrag && !selectionDragging)
            {
                if (mouse.middleButton.wasPressedThisFrame)
                {
                    _dragging = true;
                    _lastMousePos = mouse.position.ReadValue();
                }
                if (_dragging && mouse.middleButton.isPressed)
                {
                    Vector2 cur = mouse.position.ReadValue();
                    Vector2 delta = cur - _lastMousePos;
                    _lastMousePos = cur;
                    Vector3 pan = new Vector3(-delta.x, 0f, -delta.y) * dragSpeed;
                    Rig.position += pan;
                }
                if (_dragging && mouse.middleButton.wasReleasedThisFrame)
                    _dragging = false;
            }

            // 4) Zoom por distancia (rueda) — mueve la cámara en local Z
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                float delta = -scroll * zoomSpeed * 0.01f;
                CurrentDistance = CurrentDistance + delta;
            }

            // 5) Pitch y FOV fijos (solo si cambiaron o se desconfiguraron)
            if (pivot != null)
            {
                float currentPitch = pivot.localRotation.eulerAngles.x;
                if (Mathf.Abs(Mathf.DeltaAngle(currentPitch, pitchAngle)) > 0.25f)
                    ApplyFixedPitch();
            }
            if (cam != null && !cam.orthographic && Mathf.Abs(cam.fieldOfView - fixedFov) > 0.25f)
                ApplyFixedFov();

            // 6) Bounds — clamp del rig en XZ
            Vector3 p = Rig.position;
            p.x = Mathf.Clamp(p.x, minBounds.x, maxBounds.x);
            p.z = Mathf.Clamp(p.z, minBounds.y, maxBounds.y);
            Rig.position = p;
        }

        /// <summary>Vuelve a calcular bounds desde Terrain o MapGrid (útil si el mapa se genera en runtime).</summary>
        public void RefreshBoundsFromMap()
        {
            RefreshBoundsFromMapOrTerrain();
        }
    }
}
