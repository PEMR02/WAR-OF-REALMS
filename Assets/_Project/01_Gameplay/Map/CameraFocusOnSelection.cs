using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using Project.Gameplay.Units;

public class CameraFocusOnSelection : MonoBehaviour
{
    [Header("Refs")]
    public Transform cameraRig;                 // tu RTSCameraRig (o el transform que mueves con WASD)
    public RTSSelectionController selection;

    [Header("Hotkey")]
    public Key key = Key.Space;

    [Header("Move")]
    public float snapSpeed = 12f;              // velocidad de lerp

    Vector3 _targetPos;
    bool _moving;

    void Awake()
    {
        if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();

        if (cameraRig == null)
            cameraRig = transform; // si pones este script en el rig, listo
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || selection == null || cameraRig == null) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (kb[key].wasPressedThisFrame)
        {
            var sel = selection.GetSelected();
            if (sel == null || sel.Count == 0) return;

            // centra en el primero seleccionado (como AoE)
            var u = sel[0];
            if (u == null) return;

            _targetPos = new Vector3(u.transform.position.x, cameraRig.position.y, u.transform.position.z);
            _moving = true;
        }

        if (_moving)
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
