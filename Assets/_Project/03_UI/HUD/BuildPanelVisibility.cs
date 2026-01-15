using UnityEngine;
using UnityEngine.InputSystem;
using Project.Gameplay.Units;

public class BuildPanelVisibility : MonoBehaviour
{
    [Header("Refs")]
    public RTSSelectionController selection;
    public GameObject buildPanel;

    [Header("Input")]
    public Key toggleKey = Key.B;

    bool forcedByKey = false;

    void Awake()
    {
        if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();
        if (buildPanel == null) Debug.LogError("BuildPanelVisibility: buildPanel no asignado.");
    }

    void Start()
    {
        if (buildPanel != null) buildPanel.SetActive(false);
    }

    void Update()
    {
        if (selection == null || buildPanel == null) return;

        var kb = Keyboard.current;

        // Toggle manual con tecla (Input System)
        if (kb != null)
        {
            var keyCtrl = kb[toggleKey];
            if (keyCtrl != null && keyCtrl.wasPressedThisFrame)
            {
                forcedByKey = !forcedByKey;
                buildPanel.SetActive(forcedByKey);
                return;
            }
        }

        if (forcedByKey) return;

        bool show = selection.HasSelectedVillagers();
        if (buildPanel.activeSelf != show)
            buildPanel.SetActive(show);
    }
}
