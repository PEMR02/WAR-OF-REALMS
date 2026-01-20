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
		// Permitir en Category O Placing (para cambiar edificio en caliente)
		if (currentCategory == null) return;
		if (state != BuildState.Category && state != BuildState.Placing) return;
		if (catalog == null) return;

		var b = catalog.Get(currentCategory.Value, slot);
		if (b == null) 
		{
			Debug.LogWarning($"BuildModeController: No hay edificio en slot {slot} para categoría {currentCategory}");
			return;
		}

		// Si estamos en Placing, cancelar el ghost actual primero
		if (state == BuildState.Placing && placer != null)
		{
			Debug.Log($"BuildModeController: Cancelando placing actual para cambiar a {b.name}");
			placer.Cancel();
		}

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
        Debug.Log($"BuildModeController: SetState({s}) - Estado anterior: {state}");
        state = s;
        OnStateChanged?.Invoke(s);
    }

    void SetCategory(BuildCategory? c)
    {
        Debug.Log($"BuildModeController: SetCategory({c}) - Categoría anterior: {currentCategory}");
        currentCategory = c;
        OnCategoryChanged?.Invoke(c);
    }

    void SetBuilding(BuildingSO b)
    {
        Debug.Log($"BuildModeController: SetBuilding({(b != null ? b.name : "null")})");
        currentBuilding = b;
        OnBuildingChanged?.Invoke(b);
    }
}
