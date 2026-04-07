using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Project.Gameplay.Map;
using Project.Gameplay.Buildings;

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
        [Tooltip("Tiempo de suavizado para movimiento (0 = instantáneo). 0.05–0.15 típico.")]
        [Min(0f)] public float moveSmoothTime = 0.08f;
        [Tooltip("Si true, la velocidad de movimiento escala con el zoom: más lejos = más rápido (estilo AoE2).")]
        public bool speedScaleWithZoom = true;
        [Range(0.5f, 1f)]
        [Tooltip("Velocidad mínima al zoom in (1 = sin reducir).")]
        public float minSpeedFactor = 0.5f;

        [Header("Edge Scroll Cooldown")]
        public float edgeCooldownAfterSelection = 0.2f;

        [Header("Suavizado en bordes")]
        [Tooltip("Si true, al llegar al borde del mapa la cámara frena de forma suave en lugar de cortar en seco.")]
        public bool smoothEdgeMovement = true;
        [Tooltip("Velocidad de suavizado hacia el borde (5–15 típico). Mayor = más rápido.")]
        [Min(0.1f)] public float smoothEdgeSpeed = 8f;

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
        [Tooltip("Tiempo de suavizado para zoom (0 = instantáneo). 0.05–0.12 típico.")]
        [Min(0f)] public float zoomSmoothTime = 0.06f;
        [Tooltip("FOV fijo en perspectiva (40–45 típico). No se usa para zoom.")]
        [Range(30f, 60f)]
        public float fixedFov = 45f;

        [Header("Pitch fijo (ángulo de la cámara)")]
        [Tooltip("Inclinación del pivot en grados (50–60° estilo AoE2).")]
        [Range(30f, 75f)]
        public float pitchAngle = 55f;

        [Header("Rotación (Q / E y Ctrl + rueda)")]
        [Tooltip("Grados por segundo al mantener Q (gira a la derecha) o E (gira a la izquierda).")]
        public float rotateSpeedKeys = 60f;
        [Tooltip("Grados por paso de rueda cuando se mantiene Ctrl (0 = desactivado).")]
        public float rotateSpeedScroll = 15f;
        [Tooltip("Tiempo de suavizado para rotación (0 = instantáneo). 0.04–0.1 típico.")]
        [Min(0f)] public float rotationSmoothTime = 0.05f;

        [Header("Inicio — enfocar Town Center (jugador 1)")]
        [Tooltip("Si true, al iniciar la partida la cámara se centra en el Town Center del jugador 1 (el segundo TC será jugador 2/IA en el futuro).")]
        public bool focusOnTownCenterAtStart = true;
        [Tooltip("Segundos de espera antes de buscar el TC (para que el mapa termine de generarse).")]
        public float focusTownCenterDelay = 0.6f;
        [Tooltip("Nombres de objeto del TC para priorizar jugador 1 (ej. TownCenter_Player1).")]
        public string player1TownCenterName = "TownCenter_Player1";

        [Header("Encuadre inicial — zoom al empezar")]
        [Tooltip("Si true: distancia inicial = startZoomDistance (clamp min/max). Salta el auto-fit.")]
        public bool useStartZoomOverride = true;
        [Tooltip("Distancia rig–cámara al iniciar cuando useStartZoomOverride.")]
        public float startZoomDistance = 45f;
        [Tooltip("Solo si useStartZoomOverride es false: ajusta zoom al tamaño del mapa (factor interno 0.18).")]
        public bool autoFitToMapOnStart = false;
        public float fitPaddingMultiplier = 1.15f;

        private Vector2 _lastMousePos;
        private float _yaw;
        private bool _dragging;
        private bool _prevSelectionDragging;
        private float _edgeCooldownTimer;
        private float _resolveTimer;
        private float _selectionResolveTimer;

        // Target values (input en Update); aplicación suavizada en LateUpdate
        private Vector3 _targetRigPosition;
        private float _targetYaw;
        private float _targetDistance;
        private Vector3 _rigVelocity;
        private float _yawVelocity;
        private float _distanceVelocity;

        /// <summary>Transform que se mueve en XZ (rig). Siempre este objeto.</summary>
        Transform Rig => transform;

        /// <summary>Transform cuyo eje local Z es la distancia de la cámara (pivot o rig si no hay pivot).</summary>
        Transform ZoomParent => pivot != null ? pivot : Rig;

        /// <summary>Distancia real en mundo desde el pivot/rig hasta la cámara. El zoom mueve la cámara por esta línea sin desplazar el punto de anclaje.</summary>
        float CurrentDistance
        {
            get
            {
                if (cam == null) return maxDistance;
                float dist = Vector3.Distance(ZoomParent.position, cam.transform.position);
                return dist > 0.001f ? dist : maxDistance;
            }
            set
            {
                if (cam == null) return;
                float clamped = Mathf.Clamp(value, minDistance, maxDistance);
                Vector3 pivotPos = ZoomParent.position;
                Vector3 toCam = cam.transform.position - pivotPos;
                if (toCam.sqrMagnitude < 0.0001f)
                    toCam = -ZoomParent.forward;
                toCam.Normalize();
                // Evitar que la cámara quede bajo el mapa: si la dirección apunta hacia abajo, usar "atrás y arriba"
                if (toCam.y < 0.15f)
                {
                    Vector3 upBack = Vector3.up * 1.5f - ZoomParent.forward;
                    upBack.y = Mathf.Max(0.5f, upBack.y);
                    toCam = upBack.normalized;
                }
                cam.transform.position = pivotPos + toCam * clamped;
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
            // El mapa puede generarse en runtime y tardar algunos frames: reintentar bounds hasta que MapGrid esté listo.
            StartCoroutine(RefreshBoundsUntilReady());
            _yaw = Rig.eulerAngles.y;
            _targetYaw = _yaw;
            _targetRigPosition = Rig.position;
            _targetDistance = CurrentDistance;
            ApplyFixedPitch();
            ApplyFixedFov();
            ClampDistance();
            if (useStartZoomOverride)
            {
                float startD = Mathf.Clamp(startZoomDistance, minDistance, maxDistance);
                _targetDistance = startD;
                CurrentDistance = startD;
            }
            else if (autoFitToMapOnStart)
                StartCoroutine(FitInitialDistanceWhenReady());
            if (focusOnTownCenterAtStart)
                StartCoroutine(FocusOnPlayer1TownCenterDelayed());
        }

        void FitInitialDistanceToMap()
        {
            float worldWidth = 0f;
            float worldHeight = 0f;

            MapGrid grid = FindFirstObjectByType<MapGrid>();
            if (grid != null && grid.IsReady)
            {
                worldWidth = grid.width * grid.cellSize;
                worldHeight = grid.height * grid.cellSize;
            }
            else if (Terrain.activeTerrain != null && Terrain.activeTerrain.terrainData != null)
            {
                Vector3 size = Terrain.activeTerrain.terrainData.size;
                worldWidth = size.x;
                worldHeight = size.z;
            }
            else
            {
                return;
            }

            float mapSize = Mathf.Max(worldWidth, worldHeight);
            float targetDistance = Mathf.Clamp(mapSize * 0.18f * fitPaddingMultiplier, minDistance, maxDistance);

            _targetDistance = targetDistance;
            CurrentDistance = targetDistance;
        }

        IEnumerator FitInitialDistanceWhenReady()
        {
            const float timeout = 4f;
            float t = 0f;
            while (t < timeout)
            {
                MapGrid g = FindFirstObjectByType<MapGrid>();
                if (g != null && g.IsReady)
                {
                    FitInitialDistanceToMap();
                    yield break;
                }
                if (Terrain.activeTerrain != null && Terrain.activeTerrain.terrainData != null)
                {
                    FitInitialDistanceToMap();
                    yield break;
                }
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            FitInitialDistanceToMap();
        }

        IEnumerator RefreshBoundsUntilReady()
        {
            const float timeout = 3.0f;
            float t = 0f;
            while (t < timeout)
            {
                RefreshBoundsFromMapOrTerrain();
                // Si ya salimos de los defaults típicos (ej. -80..80) asumimos que ya calculó con mapa/terrain.
                if (Mathf.Abs(maxBounds.x - minBounds.x) > 200f || Mathf.Abs(maxBounds.y - minBounds.y) > 200f)
                    yield break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>Tras un breve delay, centra la cámara en el Town Center del jugador 1 (para no empezar fuera del mapa o con vista horizontal).</summary>
        IEnumerator FocusOnPlayer1TownCenterDelayed()
        {
            if (focusTownCenterDelay > 0f)
                yield return new WaitForSeconds(focusTownCenterDelay);

            Transform tc = FindPlayer1TownCenter();
            if (tc != null)
            {
                MoveToWorldPosition(tc.position);
                RefreshBoundsFromMapOrTerrain();
                // Reaplicar distancia para que la cámara quede arriba del pivot y no bajo el mapa
                float d = CurrentDistance;
                CurrentDistance = d;
            }
        }

        /// <summary>Busca el Town Center del jugador 1: por nombre "TownCenter_Player1" o el primero con ID de TC.</summary>
        Transform FindPlayer1TownCenter()
        {
            // Prioridad: objeto nombrado explícitamente como jugador 1 (el segundo será Player 2/IA)
            var byName = GameObject.Find(player1TownCenterName);
            if (byName != null) return byName.transform;

            var all = Object.FindObjectsByType<BuildingInstance>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var bi = all[i];
                if (bi == null || bi.buildingSO == null) continue;
                string id = bi.buildingSO.id ?? "";
                if (id.IndexOf("TownCenter", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf("Town Centre", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return bi.transform;
            }
            return null;
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
            if (pivot != null)
                pivot.localRotation = Quaternion.Euler(pitchAngle, 0f, 0f);
            else
                Rig.rotation = Quaternion.Euler(pitchAngle, _yaw, 0f);
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

            // 0) Rotación: Q / E y Ctrl + rueda
            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            float scroll = mouse.scroll.ReadValue().y;
            if (kb.qKey.isPressed) _yaw += rotateSpeedKeys * dt;
            if (kb.eKey.isPressed) _yaw -= rotateSpeedKeys * dt;
            if (ctrl && rotateSpeedScroll > 0f && Mathf.Abs(scroll) > 0.01f)
                _yaw += scroll * rotateSpeedScroll;
            // Con Pivot: el Rig solo gira en Y; el Pivot aplica la inclinación (pitch). Sin Pivot: el Rig inclina también (cámara = Rig).
            if (pivot != null)
                Rig.rotation = Quaternion.Euler(0f, _yaw, 0f);
            else
                Rig.rotation = Quaternion.Euler(pitchAngle, _yaw, 0f);

            Vector3 rigForward = Rig.forward;
            rigForward.y = 0f;
            if (rigForward.sqrMagnitude < 0.01f) rigForward = Vector3.forward;
            rigForward.Normalize();
            Vector3 rigRight = Rig.right;
            rigRight.y = 0f;
            rigRight.Normalize();

            // 1) WASD — mueve el rig en la dirección de la cámara
            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) move += rigForward;
            if (kb.sKey.isPressed) move -= rigForward;
            if (kb.aKey.isPressed) move -= rigRight;
            if (kb.dKey.isPressed) move += rigRight;
            if (move.sqrMagnitude > 0.001f)
            {
                move.Normalize();
                _targetRigPosition += move * (moveSpeed * speedMul) * dt;
            }

            // 2) Edge scroll — en la dirección de la cámara
            if (edgeScroll && !_dragging && !edgeBlocked)
            {
                Vector2 mp = mouse.position.ReadValue();
                Vector3 edgeMove = Vector3.zero;
                if (mp.x <= edgeSize) edgeMove -= rigRight;
                if (mp.x >= Screen.width - edgeSize) edgeMove += rigRight;
                if (mp.y <= edgeSize) edgeMove -= rigForward;
                if (mp.y >= Screen.height - edgeSize) edgeMove += rigForward;
                if (edgeMove.sqrMagnitude > 0.001f)
                {
                    edgeMove.Normalize();
                    _targetRigPosition += edgeMove * (edgeSpeed * speedMul) * dt;
                }
            }

            // 3) Middle mouse drag — mueve el rig en la dirección de la cámara
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
                    Vector3 pan = (-rigRight * delta.x - rigForward * delta.y) * dragSpeed;
                    _targetRigPosition += pan;
                }
                if (_dragging && mouse.middleButton.wasReleasedThisFrame)
                    _dragging = false;
            }

            // 4) Zoom por distancia (rueda; sin Ctrl para no robar rotación)
            if (Mathf.Abs(scroll) > 0.01f && !ctrl)
            {
                float delta = -scroll * zoomSpeed * 0.01f;
                _targetDistance = Mathf.Clamp(CurrentDistance + delta, minDistance, maxDistance);
            }
            else
                _targetDistance = CurrentDistance;

            // 5) Rotación: actualizar target yaw (aplicación en LateUpdate)
            _targetYaw = _yaw;

            // 6) Bounds — clamp del target en XZ (suave cerca del borde si smoothEdgeMovement)
            if (smoothEdgeMovement && smoothEdgeSpeed > 0.001f)
            {
                float clampedX = Mathf.Clamp(_targetRigPosition.x, minBounds.x, maxBounds.x);
                float clampedZ = Mathf.Clamp(_targetRigPosition.z, minBounds.y, maxBounds.y);
                float maxPull = smoothEdgeSpeed * dt;
                _targetRigPosition.x = Mathf.MoveTowards(_targetRigPosition.x, clampedX, maxPull);
                _targetRigPosition.z = Mathf.MoveTowards(_targetRigPosition.z, clampedZ, maxPull);
            }
            else
            {
                _targetRigPosition.x = Mathf.Clamp(_targetRigPosition.x, minBounds.x, maxBounds.x);
                _targetRigPosition.z = Mathf.Clamp(_targetRigPosition.z, minBounds.y, maxBounds.y);
            }
        }

        void LateUpdate()
        {
            if (cam == null) return;

            float dt = Time.unscaledDeltaTime;

            // Aplicar movimiento del rig con suavizado
            if (moveSmoothTime > 0.001f)
                Rig.position = Vector3.SmoothDamp(Rig.position, _targetRigPosition, ref _rigVelocity, moveSmoothTime, Mathf.Infinity, dt);
            else
                Rig.position = _targetRigPosition;

            // Aplicar rotación con suavizado
            if (rotationSmoothTime > 0.001f)
                _yaw = Mathf.SmoothDampAngle(_yaw, _targetYaw, ref _yawVelocity, rotationSmoothTime, Mathf.Infinity, dt);
            else
                _yaw = _targetYaw;

            if (pivot != null)
                Rig.rotation = Quaternion.Euler(0f, _yaw, 0f);
            else
                Rig.rotation = Quaternion.Euler(pitchAngle, _yaw, 0f);

            ApplyFixedPitch();

            // Aplicar zoom (distancia cámara) con suavizado
            if (zoomSmoothTime > 0.001f)
            {
                float d = CurrentDistance;
                float newD = Mathf.SmoothDamp(d, _targetDistance, ref _distanceVelocity, zoomSmoothTime, Mathf.Infinity, dt);
                CurrentDistance = newD;
            }
            else
                CurrentDistance = _targetDistance;

            if (cam != null && !cam.orthographic && Mathf.Abs(cam.fieldOfView - fixedFov) > 0.25f)
                ApplyFixedFov();
        }

        /// <summary>Vuelve a calcular bounds desde Terrain o MapGrid (útil si el mapa se genera en runtime).</summary>
        public void RefreshBoundsFromMap()
        {
            RefreshBoundsFromMapOrTerrain();
        }

        /// <summary>Mueve el rig al punto indicado (XZ dentro de bounds). Sincroniza target para suavizado.</summary>
        public void MoveToWorldPosition(Vector3 worldPos)
        {
            Vector3 p = worldPos;
            p.x = Mathf.Clamp(p.x, minBounds.x, maxBounds.x);
            p.z = Mathf.Clamp(p.z, minBounds.y, maxBounds.y);
            Rig.position = p;
            _targetRigPosition = p;
            _rigVelocity = Vector3.zero;
        }

        /// <summary>Fija solo el target del rig (XZ). La cámara se moverá suavemente hacia ahí en LateUpdate. Evita que otro script (ej. focus en selección) y este controlador se pisen y produzcan tiritones.</summary>
        public void SetRigTargetPosition(Vector3 worldPositionXZ)
        {
            _targetRigPosition.x = Mathf.Clamp(worldPositionXZ.x, minBounds.x, maxBounds.x);
            _targetRigPosition.z = Mathf.Clamp(worldPositionXZ.z, minBounds.y, maxBounds.y);
            _targetRigPosition.y = Rig.position.y;
            _rigVelocity = Vector3.zero;
        }
    }
}
