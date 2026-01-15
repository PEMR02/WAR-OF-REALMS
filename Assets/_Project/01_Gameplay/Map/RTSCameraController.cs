using UnityEngine;
using UnityEngine.InputSystem;

namespace Project.Gameplay
{
    public class RTSCameraController : MonoBehaviour
    {
        [Header("Refs")]
        public Project.Gameplay.Units.RTSSelectionController selection;

        [Header("Move")]
        public float moveSpeed = 25f;
        public float edgeSpeed = 25f;
        public float edgeSize = 12f; // px
        public bool edgeScroll = true;

        [Header("Edge Scroll Cooldown")]
        public float edgeCooldownAfterSelection = 0.2f;

        [Header("Drag Pan (Middle Mouse)")]
        public bool middleMouseDrag = true;
        public float dragSpeed = 0.08f; // sensibilidad

        [Header("Zoom")]
        public float zoomSpeed = 6f;
        public float minHeight = 10f;
        public float maxHeight = 45f;

        [Header("Map Bounds (XZ)")]
        public Vector2 minBounds = new Vector2(-80, -80);
        public Vector2 maxBounds = new Vector2(80, 80);

        private Vector2 _lastMousePos;
        private bool _dragging;

        // cooldown internals
        private bool _prevSelectionDragging;
        private float _edgeCooldownTimer;

        void Update()
        {
            // Auto-asignación (si no la pusiste en Inspector)
            if (selection == null)
                selection = FindFirstObjectByType<Project.Gameplay.Units.RTSSelectionController>();

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            float dt = Time.unscaledDeltaTime;

            bool selectionDragging = selection != null && selection.IsDraggingSelection;

            // Detectar fin del drag de selección => iniciar cooldown
            if (_prevSelectionDragging && !selectionDragging)
                _edgeCooldownTimer = edgeCooldownAfterSelection;

            _prevSelectionDragging = selectionDragging;

            if (_edgeCooldownTimer > 0f)
                _edgeCooldownTimer -= dt;

            bool edgeBlocked = selectionDragging || _edgeCooldownTimer > 0f;

            // =========================
            // 1) WASD movement
            // =========================
            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) move += Vector3.forward;
            if (kb.sKey.isPressed) move += Vector3.back;
            if (kb.aKey.isPressed) move += Vector3.left;
            if (kb.dKey.isPressed) move += Vector3.right;

            if (move.sqrMagnitude > 0.001f)
            {
                move.Normalize();
                transform.position += move * moveSpeed * dt;
            }

            // =========================
            // 2) Edge scrolling (bloqueado durante selección y cooldown)
            // =========================
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
                    transform.position += edgeMove * edgeSpeed * dt;
                }
            }

            // =========================
            // 3) Middle mouse drag pan (bloqueado durante selección)
            // =========================
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
                    transform.position += pan;
                }

                if (_dragging && mouse.middleButton.wasReleasedThisFrame)
                {
                    _dragging = false;
                }
            }

            // =========================
            // 4) Zoom (wheel)
            // =========================
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                float y = transform.position.y - scroll * zoomSpeed * 0.01f;
                y = Mathf.Clamp(y, minHeight, maxHeight);
                transform.position = new Vector3(transform.position.x, y, transform.position.z);
            }

            // =========================
            // 5) Clamp bounds XZ
            // =========================
            Vector3 p = transform.position;
            p.x = Mathf.Clamp(p.x, minBounds.x, maxBounds.x);
            p.z = Mathf.Clamp(p.z, minBounds.y, maxBounds.y);
            transform.position = p;
        }
    }
}
