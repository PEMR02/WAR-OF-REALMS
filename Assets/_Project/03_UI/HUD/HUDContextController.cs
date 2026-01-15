using UnityEngine;
using Project.Gameplay.Units;

public class HUDContextController : MonoBehaviour
{
    public RTSSelectionController selection;

    [Header("Roots")]
    public GameObject hudGeneralRoot;
    public GameObject hudVillagerRoot;

    void Awake()
    {
        if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();
    }

    void Update()
    {
        if (selection == null) return;

        bool vill = selection.HasSelectedVillagers();

        if (hudGeneralRoot != null && hudGeneralRoot.activeSelf != !vill)
            hudGeneralRoot.SetActive(!vill);

        if (hudVillagerRoot != null && hudVillagerRoot.activeSelf != vill)
            hudVillagerRoot.SetActive(vill);
    }
}
