# Auditoría de assets del juego – WAR OF REALMS

Documento generado para compartir con Claude u otra auditoría. Incluye solo la carpeta **Assets/_Project** (contenido propio del juego).

---

## Estructura de carpetas

```
Assets/_Project/
├── 00_Core/           # Núcleo: comandos, bootstrapper
├── 01_Gameplay/       # Gameplay: edificios, combate, mapa, unidades, recursos
├── 03_UI/             # HUD, minimapa, selección, construcción
├── 04_Data/           # ScriptableObjects (BuildingSO, UnitSO, catálogos)
├── 05_Animation/      # Animator controllers
├── 05_Art/            # Materials, Shaders
├── 07_Scenes/         # Prefabs de escena (ej. GameBootstrapper)
├── 08_Prefabs/        # Prefabs: edificios, unidades, recursos, UI
├── 99_Docs/           # Documentación
└── Editor/            # Herramientas de editor (menús, fix prefabs)
```

---

## Scripts C# (.cs)

### 00_Core
- `Commands/CommandBus.cs`, `ICommand.cs`, `MoveCommand.cs`
- `DataStructures/PriorityQueue.cs`
- `GameBootstrapper.cs`

### 01_Gameplay – Building
- `BuildingController.cs`, `BuildingSelectable.cs`, `PlacementValidator.cs`
- `Construction/BuildSite.cs`, `Builder.cs`, `BuildableBuildng.cs`, `BuilTask.cs`, `TownCenter.cs`
- `Placement/BuildingPlacer.cs`, `BuildModeController.cs`, `GhostPreview.cs`
- `DropOff/DropOffFinder.cs`, `DropOffPoint.cs`

### 01_Gameplay – Combat
- `Health.cs`, `HealthBarManager.cs`, `HealthBarUI.cs`, `HealthBarWorld.cs` (obsoleto)
- `IHealth.cs`, `IWorldBarSource.cs`, `WorldBarSettings.cs`
- `FloatingDamageText.cs`

### 01_Gameplay – Map
- `MapGrid.cs`, `GridConfig.cs`, `GridSnapUtil.cs`, `GridGizmoRenderer.cs`
- `RTSCameraController.cs`, `RTSMapGenerator.cs`, `MapResourcePlacer.cs`
- `CameraFocusOnSelection.cs`, `CameraOcclusionFade.cs`, `ShadowDistanceFromCamera.cs`
- `NavMeshDebugHelper.cs`
- `MapGenerator/`: `MapGenerator.cs`, `MapGeneratorBridge.cs`, `GridSystem.cs`, `CellData.cs`, `MapGenConfig.cs`, `HeightGenerator.cs`, `WaterGenerator.cs`, `WaterMeshBuilder.cs`, `TerrainExporter.cs`, `TerrainSkirtBuilder.cs`, `ResourceGenerator.cs`, `MapResourcePlacer`, `CityGenerator.cs`, `RegionGenerator.cs`, `RoadNetworkGenerator.cs`, `TerrainCarver.cs`, `MapValidator.cs`, `MapPreset.cs`, `XorShiftRng.cs`, `IRng.cs`

### 01_Gameplay – Pathfinding
- `Pathfinder.cs`, `PathSmoother.cs`

### 01_Gameplay – Players
- `PlayerResources.cs`, `PopulationManager.cs`

### 01_Gameplay – Resources
- `ResourceNode.cs`, `ResourceSelectable.cs`, `ResourceTypeSO.cs`
- `AnimalPastureBehaviour.cs`, `FadeableByCamera.cs`

### 01_Gameplay – Selection
- `SelectableOutline.cs`

### 01_Gameplay – Units
- `UnitSelectable.cs`, `UnitMover.cs`, `UnitAnimatorDriver.cs`
- `RTSSelectionController.cs`, `RTSOrderController.cs`
- `VillagerGatherer.cs`, `Repairer.cs`, `OrderFeedback.cs`, `IdleVillagerIcon.cs`
- `FormationHelper.cs`

### 01_Gameplay – Buildings (lógica)
- `BuildingInstance.cs`, `Production/ProductionBuilding.cs`

### 03_UI – HUD
- `SelectionBoxUI.cs`, `SelectionHealthBarUI.cs`, `ResourceInfoUI.cs`
- `VillagerBuildHUD.cs`, `BuildMenuUI.cs`, `BuildPanelVisibility.cs`, `BuildHotKeyRouter.cs`
- `ProductionHUD.cs`, `ProductionHotkeyRouter.cs`
- `ResourcesHUD.cs`, `PopulationHUD.cs`, `HUDContextController.cs`
- `RuntimeMinimapBootstrap.cs`
- `GameplayNotifications.cs`, `IdleVillagerHotkey.cs`, `IdleVillagerHUD.cs`, `NearestVillagerHotkey.cs`, `TownCenterHotkey.cs`
- `TooltipUI.cs`, `TooltipTrigger.cs`
- `RaycastDebugger.cs`, `QueueVisibilityDebugger.cs`

### 04_Data
- `BuildingSO.cs`, `UnitSO.cs`
- `ScriptableObjecs/Buildings/BuildCatalog.cs`
- `ScriptableObjecs/Units/ProductionCatalog.cs`

### Editor
- `FixBuildingPrefabEditor.cs`, `FixBuildingPivots.cs`, `WorldBarPrefabTools.cs`
- `RTSMapGeneratorEditor.cs`, `ToonyTinyPeopleURPMaterialFix.cs`
- `CreateTestMPCCube.cs`

---

## ScriptableObjects y datos (.asset)

### Buildings
- `Barracks_SO`, `Casttle_SO`, `Granary_BuildingSO`, `House_SO`, `LumberCamp_BuildingSO`, `MiningCamp_BuildingSO`, `TownCenter_BuildingSO`
- `BuildCatalog.asset`

### Units
- `Archer_UnitSO`, `Militia_UnitSO`, `Mounted_UnitSO`, `Villager_UnitSO`
- `ProductionCatalog_Asset.asset`

### Map
- `GridConfig.asset`, `MapGenerator/MapGenConfig.asset`

---

## Prefabs

### Edificios (08_Prefabs/Buildings)
- `PF_Barracks`, `PF_Castillo`, `PF_Granary`, `PF_House`, `PF_LumberCamp`, `PF_MiningCamp`, `PF_TownCenter`
- `Construction/PF_Buildsite`

### Unidades (08_Prefabs/Units)
- `PF_Archer`, `PF_Mounted_King`, `PF_Peasant`, `PF_Swordman`
- `PF_WorldHealthBar` (deprecado), `PF_HealthBarUI` (nuevo sistema)

### Recursos (08_Prefabs/Resources)
- `Animals/PF_Animal`, `PF_Cow`
- `Food/PF_Food`, `Gold/PF_Gold`, `Stone/PF_Stone`, `Wood/PF_Tree PM`, `PF_Wood`

### Otros
- `Obstacles/Obst_01`
- `07_Scenes/GameBootstrapper.prefab`

---

## Animator controllers (05_Animation)
- `PF_Archer_Animator`, `PF_Cow_Animator`, `PF_Mounted_King_Animator`, `PF_Peasant_Animator`, `PF_Swordman_01_Animator`

---

## Materials (05_Art, 08_Prefabs)
- MAT_Animal, MAT_Barracks, MAT_Food, MAT_Gold, MAT_Grid, MAT_House, MAT_Stone, MAT_TownCenter, MAT_Unit, MAT_Villager, MAT_Water
- New Terrain Material, materiales en Buildings (Barraca, Casa, Castillo) y Resources (Stone, Wood)

---

## Shaders (05_Art/Shaders)
- `GridAlwaysOnTop.shader`, `OutlineCullFront.shader`, `TerrainSkirt.shader`

---

## Documentación (99_Docs)
- HEALTH_BAR_MANAGER_SETUP.md, HEALTH_BAR_SISTEMA_VALIDACION.md, PROBLEMA_BARRAS_VIDA_NO_SIGUEN_UNIDAD.md
- BUILD_SITE_Y_HUELLAS.md, PASO_A_PASO_SELECTION_INFO_UI.md, PLAN_VIDA_DAÑO_Y_RECURSOS.md
- FBX_TEXTURAS_UNITY.md, CABALLO_RETARGET_ANIMACIONES_ZEBRA.md, otros .md

---

*Generado para auditoría. Ruta base: `Assets/_Project` (Unity).*
