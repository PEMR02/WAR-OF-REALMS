using System;
using UnityEngine;
using Project.Gameplay.Units;
using Project.Gameplay.Buildings;

public enum BuildState { Idle, BuildRoot, Category, Placing }

public class BuildModeController : MonoBehaviour
{
    [Header("Refs")]
    public RTSSelectionController selection;
    public BuildingPlacer placer;
    public BuildCatalog catalog;

    [Header("State (read only)")]
    public BuildState state = BuildState.Idle;
    public BuildCategory? currentCategory;
    public BuildingSO currentBuilding;

    public event Action<BuildState> OnStateChanged;
    public event Action<BuildCategory?> OnCategoryChanged;
    public event Action<BuildingSO> OnBuildingChanged;

    void Awake()
    {
        if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();
        if (placer == null) placer = FindFirstObjectByType<BuildingPlacer>();
    }

    public bool CanUseBuild()
    {
        // aldeanos seleccionados (gatherer o builder)
        return selection != null && selection.HasSelectedVillagers();
    }

    public void ToggleBuildRoot()
    {
        if (!CanUseBuild()) return;

        if (state == BuildState.Idle) EnterBuildRoot();
        else Cancel(); // AoE2: si ya estabas en build, B cancela/retrocede
    }

    public void EnterBuildRoot()
    {
        if (!CanUseBuild()) return;
        SetState(BuildState.BuildRoot);
        SetCategory(null);
        SetBuilding(null);
    }

    public void EnterCategory(BuildCategory cat)
    {
        if (!CanUseBuild()) return;

        // Si estabas poniendo un edificio, cancela el placing y vuelve a categoría
        if (state == BuildState.Placing)
            placer.Cancel();

        SetState(BuildState.Category);
        SetCategory(cat);
        SetBuilding(null);
    }

    public void PickSlot(int slot)
	{
		if (state != BuildState.Category || currentCategory == null) return;
		if (catalog == null) return;

		var b = catalog.Get(currentCategory.Value, slot);
		if (b == null) return;

		currentBuilding = b;
		OnBuildingChanged?.Invoke(b);

		placer.selectedBuilding = b;
		placer.Begin();
		SetState(BuildState.Placing);
	}

    public void Cancel()
    {
        if (state == BuildState.Placing)
        {
            placer.Cancel();
            SetState(BuildState.Category);
            SetBuilding(null);
            return;
        }

        if (state == BuildState.Category)
        {
            SetState(BuildState.BuildRoot);
            SetCategory(null);
            return;
        }

        if (state == BuildState.BuildRoot)
        {
            SetState(BuildState.Idle);
            return;
        }
    }

    void SetState(BuildState s)
    {
        state = s;
        OnStateChanged?.Invoke(s);
    }

    void SetCategory(BuildCategory? c)
    {
        currentCategory = c;
        OnCategoryChanged?.Invoke(c);
    }

    void SetBuilding(BuildingSO b)
    {
        currentBuilding = b;
        OnBuildingChanged?.Invoke(b);
    }
}
