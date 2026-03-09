using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using Project.Gameplay.Units;
using Project.Gameplay;

public class CameraFocusOnSelection : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Si se asigna, el foco usa SetRigTargetPosition del controlador (evita tiritar cuando la cámara y la unidad se mueven). Si no, se mueve el rig directamente con Lerp.")]
    public RTSCameraController rtsCamera;
    public Transform cameraRig;
    public RTSSelectionController selection;

    [Header("Hotkey")]
    public Key key = Key.Space;

    [Header("Move (solo si rtsCamera es null)")]
    public float snapSpeed = 12f;

    Vector3 _targetPos;
    bool _moving;

    void Awake()
    {
        if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();
        if (rtsCamera == null) rtsCamera = FindFirstObjectByType<RTSCameraController>();
        if (cameraRig == null) cameraRig = transform;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || selection == null) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (kb[key].wasPressedThisFrame)
        {
            var sel = selection.GetSelected();
            if (sel == null || sel.Count == 0) return;

            var u = sel[0];
            if (u == null) return;

            Vector3 targetXZ = new Vector3(u.transform.position.x, 0f, u.transform.position.z);

            if (rtsCamera != null)
            {
                rtsCamera.SetRigTargetPosition(targetXZ);
                _moving = false;
            }
            else if (cameraRig != null)
            {
                _targetPos = new Vector3(targetXZ.x, cameraRig.position.y, targetXZ.z);
                _moving = true;
            }
        }

        if (_moving && cameraRig != null && rtsCamera == null)
        {
            cameraRig.position = Vector3.Lerp(cameraRig.position, _targetPos, Time.deltaTime * snapSpeed);
            if ((cameraRig.position - _targetPos).sqrMagnitude < 0.01f)
            {
                cameraRig.position = _targetPos;
                _moving = false;
            }
        }
    }
}
