namespace TopsonGames
{
    using UnityEngine;
    using UnityEngine.InputSystem;

    [RequireComponent(typeof(Camera))]
    public class CameraController : MonoBehaviour
    {

        [Header("Input Actions via Reference")]
        [SerializeField] InputActionReference moveActionReference;
        [SerializeField] InputActionReference zoomActionReference;
        [SerializeField] InputActionReference sprintActionReference;
        [SerializeField] InputActionReference rotateActionReference;

        [Header("Movement Settings")]
        [SerializeField] float panSpeed = 10f;
        [SerializeField] float sprintMultiplier = 2f;
        [SerializeField] float zoomSpeed = 25f;
        [SerializeField] float rotationSpeed = 100f;
        [SerializeField] float pitchRotationSpeed = 20f;
        [SerializeField] float minZoomHeight = 5f;
        [SerializeField] float maxZoomHeight = 100f;

        [Header("Pitch Rotation Settings")]
        public float minPitch = 10f;
        public float maxPitch = 80f;
        private float currentPitchVelocity;

        [Header("Smoothing Settings")]
        [Range(0.01f, 1f)] public float panSmoothTime = 0.1f;
        [Range(0.01f, 1f)] public float zoomSmoothTime = 0.1f;
        [Range(0.01f, 1f)] public float rotationSmoothTime = 0.1f;

        [Header("Screen Edge Controls")]

        [SerializeField] float screenEdgeBorderThickness = 25f;
        [SerializeField] ScreenEdgePanningMode panningMode = ScreenEdgePanningMode.AllEdges;
        [SerializeField] bool enableScreenEdgeRotation = false;

        public enum ScreenEdgePanningMode
        {
            Disabled,
            TopAndBottom,
            AllEdges
        }

        [Header("Ground Clipping Prevention")]
        [SerializeField] LayerMask groundLayer;
        [SerializeField] float minHeightAboveGround = 1f;

        [Header("Cursor")]
        public bool lockCursor = false;

        [Header("World")]
        [SerializeField] bool lockCamera;
        [SerializeField] Transform worldCorner1;
        [SerializeField] Transform worldCorner2;

        private Camera cam;
        private float targetZoomHeight;
        private Vector3 currentPanVelocity;
        private float currentZoomVelocity;
        private float currentYawVelocity;
        private float minX, maxX, minZ, maxZ;
        private float targetYaw;
        private float targetPitch;

        private void Start()
        {
            if (lockCamera && worldCorner1 != null && worldCorner2 != null)
            {
                minX = Mathf.Min(worldCorner1.position.x, worldCorner2.position.x);
                maxX = Mathf.Max(worldCorner1.position.x, worldCorner2.position.x);
                minZ = Mathf.Min(worldCorner1.position.z, worldCorner2.position.z);
                maxZ = Mathf.Max(worldCorner1.position.z, worldCorner2.position.z);
            }
            targetYaw = transform.eulerAngles.y;
            targetPitch = transform.eulerAngles.x;
        }
        private void OnEnable()
        {
            moveActionReference?.action.Enable();
            zoomActionReference?.action.Enable();
            sprintActionReference?.action.Enable();
            rotateActionReference?.action.Enable();

            cam = GetComponent<Camera>();
            targetZoomHeight = transform.position.y;
            if (lockCursor)
                Cursor.lockState = CursorLockMode.Confined;
        }

        private void OnDisable()
        {
            moveActionReference?.action.Disable();
            zoomActionReference?.action.Disable();
            sprintActionReference?.action.Disable();
            rotateActionReference?.action.Disable();
        }

        private void Update()
        {
            HandlePan();
            HandleZoom();
            HandleRotationInput();
            HandleScreenEdgeRotation();
            AdjustTargetZoomHeightForClipping();
        }
        private void LateUpdate()
        {
            ApplyRotation();
            if (lockCamera)
            {
                Vector3 pos = transform.position;
                pos.x = Mathf.Clamp(pos.x, minX, maxX);
                pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
                transform.position = pos;
            }
        }

        private void HandlePan()
        {
            Vector2 input = moveActionReference?.action.ReadValue<Vector2>() ?? Vector2.zero;
            Vector2 mousePosition = Mouse.current.position.ReadValue();

            switch (panningMode)
            {
                case ScreenEdgePanningMode.TopAndBottom:
                    if (mousePosition.y >= Screen.height - screenEdgeBorderThickness) input.y += 1;
                    if (mousePosition.y <= screenEdgeBorderThickness) input.y -= 1;
                    break;
                case ScreenEdgePanningMode.AllEdges:
                    if (mousePosition.y >= Screen.height - screenEdgeBorderThickness) input.y += 1;
                    if (mousePosition.y <= screenEdgeBorderThickness) input.y -= 1;
                    if (mousePosition.x >= Screen.width - screenEdgeBorderThickness) input.x += 1;
                    if (mousePosition.x <= screenEdgeBorderThickness) input.x -= 1;
                    break;
                case ScreenEdgePanningMode.Disabled:
                    break;
            }

            input = Vector2.ClampMagnitude(input, 1f);

            if (input != Vector2.zero)
            {
                float currentPanSpeed = panSpeed;
                float sprintValue = sprintActionReference?.action.ReadValue<float>() ?? 0f;
                if (sprintValue > 0f) currentPanSpeed *= sprintMultiplier;

                Vector3 right = transform.right; right.y = 0;
                Vector3 forward = transform.forward; forward.y = 0;

                Vector3 moveDirection = (right.normalized * input.x + forward.normalized * input.y);
                Vector3 targetPanPosition = transform.position + moveDirection * currentPanSpeed * Time.deltaTime;

                if (lockCamera)
                {
                    targetPanPosition.x = Mathf.Clamp(targetPanPosition.x, minX, maxX);
                    targetPanPosition.z = Mathf.Clamp(targetPanPosition.z, minZ, maxZ);
                }

                transform.position = Vector3.SmoothDamp(transform.position, targetPanPosition, ref currentPanVelocity, panSmoothTime);
            }
            else
            {
                transform.position = Vector3.SmoothDamp(transform.position, transform.position, ref currentPanVelocity, panSmoothTime);
            }
        }

        private void HandleScreenEdgeRotation()
        {
            if (!enableScreenEdgeRotation) return;

            Vector2 mousePosition = Mouse.current.position.ReadValue();

            if (mousePosition.x >= Screen.width - screenEdgeBorderThickness)
            {
                targetYaw += rotationSpeed * Time.deltaTime;
            }
            if (mousePosition.x <= screenEdgeBorderThickness)
            {
                targetYaw -= rotationSpeed * Time.deltaTime;
            }
        }

        private void HandleZoom()
        {
            float scroll = zoomActionReference?.action.ReadValue<float>() ?? 0f;
            if (!Mathf.Approximately(scroll, 0f))
            {
                targetZoomHeight -= scroll * zoomSpeed * Time.deltaTime;
                targetZoomHeight = Mathf.Clamp(targetZoomHeight, minZoomHeight, maxZoomHeight);
            }
            Vector3 pos = transform.position;
            pos.y = Mathf.SmoothDamp(pos.y, targetZoomHeight, ref currentZoomVelocity, zoomSmoothTime);
            transform.position = pos;
        }

        private void HandleRotationInput()
        {
            float rotateValue = rotateActionReference?.action.ReadValue<float>() ?? 0f;
            if (rotateValue > 0f)
            {
                Vector2 delta = Mouse.current.delta.ReadValue();
                targetYaw += delta.x * rotationSpeed * Time.deltaTime;
                targetPitch -= delta.y * pitchRotationSpeed * Time.deltaTime;
                targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
            }
        }

        private void AdjustTargetZoomHeightForClipping()
        {
            Vector3 rayOrigin = transform.position;
            rayOrigin.y = maxZoomHeight + 100f;

            Ray ray = new Ray(rayOrigin, Vector3.down);
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundLayer))
            {
                float minimumAllowedY = hit.point.y + minHeightAboveGround;
                targetZoomHeight = Mathf.Max(targetZoomHeight, minimumAllowedY);
            }
        }
        private void ApplyRotation()
        {
            float smoothedYaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetYaw, ref currentYawVelocity, rotationSmoothTime);
            float smoothedPitch = Mathf.SmoothDampAngle(transform.eulerAngles.x, targetPitch, ref currentPitchVelocity, rotationSmoothTime);

            transform.rotation = Quaternion.Euler(smoothedPitch, smoothedYaw, 0f);
        }
        private void OnDrawGizmos()
        {
            if (lockCamera && worldCorner1 != null && worldCorner2 != null)
            {
                Gizmos.color = Color.cyan;
                float width = Mathf.Abs(worldCorner2.position.x - worldCorner1.position.x);
                float depth = Mathf.Abs(worldCorner2.position.z - worldCorner1.position.z);
                float height = maxZoomHeight - worldCorner1.position.y;
                Vector3 boxSize = new Vector3(width, height, depth);
                float centerX = (worldCorner1.position.x + worldCorner2.position.x) / 2f;
                float centerZ = (worldCorner1.position.z + worldCorner2.position.z) / 2f;
                float centerY = worldCorner1.position.y + (height / 2f);
                Vector3 boxCenter = new Vector3(centerX, centerY, centerZ);
                Gizmos.DrawWireCube(boxCenter, boxSize);
            }
        }
    }
}