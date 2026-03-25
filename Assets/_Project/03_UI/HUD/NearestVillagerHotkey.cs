using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using Project.Gameplay.Units;
using Project.UI;

public class NearestVillagerHotkey : MonoBehaviour
{
    [Header("Refs")]
    public Camera cam;
    public RTSSelectionController selection;
    public LayerMask groundMask;

    [Header("Hotkey")]
    public Key key = Key.B;

    [Header("Search")]
    public float maxRadius = 50f;

    [Header("Optional")]
    public BuildModeController buildMode; // arrástralo si quieres; si no, se busca solo

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();
        if (buildMode == null) buildMode = FindFirstObjectByType<BuildModeController>();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || cam == null || selection == null) return;

        // no robar input si estás sobre UI
        if (UiInputRaycast.IsPointerOverGameObject())
            return;

        if (!kb[key].wasPressedThisFrame) return;

        Vector3 refPoint = GetMouseGroundPoint();

        UnitSelectable nearest = FindNearestVillagerSelectable(refPoint);

        if (nearest == null)
        {
            // No hay aldeano: evita dejar build/ghost activado
            if (buildMode != null) buildMode.Cancel();
            return;
        }

        selection.SelectOnly(nearest);
    }

    Vector3 GetMouseGroundPoint()
    {
        var mouse = Mouse.current;
        if (mouse == null) return cam.transform.position;

        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        if (Physics.Raycast(ray, out var hit, 5000f, groundMask))
            return hit.point;

        // fallback
        Vector3 p = cam.transform.position + cam.transform.forward * 10f;
        p.y = 0f;
        return p;
    }

    UnitSelectable FindNearestVillagerSelectable(Vector3 refPoint)
    {
        UnitSelectable best = null;
        float bestSqr = maxRadius * maxRadius;

        // Unity 6 friendly
        var all = Object.FindObjectsByType<UnitSelectable>(FindObjectsSortMode.None);

        for (int i = 0; i < all.Length; i++)
        {
            var u = all[i];
            if (u == null) continue;

            // "aldeano" = tiene Gatherer o Builder en el mismo GO
            if (u.GetComponent<VillagerGatherer>() == null && u.GetComponent<Builder>() == null)
                continue;

            float sqr = (u.transform.position - refPoint).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = u;
            }
        }

        return best;
    }
}
