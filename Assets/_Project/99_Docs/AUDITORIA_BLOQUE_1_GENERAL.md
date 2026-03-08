# Bloque 1 — Auditoría general del proyecto (Assets/_Project)

**Objetivo:** Detectar sin modificar código: responsabilidades excesivas, duplicidad, dependencias frágiles, sistemas legacy y causas de problemas de placement, cámara y rendimiento.

---

## 1. Inventario de scripts por carpeta

### 00_Core
| Script | Responsabilidad |
|--------|-----------------|
| `CommandBus.cs` | Cola de comandos (MoveCommand, etc.) |
| `ICommand.cs`, `MoveCommand.cs` | Contrato y comando de movimiento |
| `PriorityQueue.cs` | Cola prioritaria (pathfinding) |
| `GameBootstrapper.cs` | Inicialización global |

### 01_Gameplay — Building
| Script | Responsabilidad |
|--------|-----------------|
| `BuildingController.cs` | Lógica de edificio en runtime |
| `BuildingSelectable.cs` | Seleccionable + outline |
| `PlacementValidator.cs` | **Solo** OverlapBox + MapGrid.IsWorldAreaFree (sin terreno/altura) |
| `Placement/BuildingPlacer.cs` | **Núcleo placement:** input, snap, raycast, terreno (1 punto), ghost, BuildSite, OccupyCells |
| `Placement/BuildModeController.cs` | Modo construcción + UI |
| `Placement/GhostPreview.cs` | Visual verde/rojo del ghost |
| `Construction/BuildSite.cs` | Fundación, progreso, spawn edificio final |
| `Construction/Builder.cs` | Aldeano construye en BuildSite |
| `Construction/BuilTask.cs`, `BuildableBuildng.cs` | DTOs |
| `Construction/TownCenter.cs` | Lógica TC |
| `DropOff/DropOffFinder.cs`, `DropOffPoint.cs` | Puntos de entrega |

### 01_Gameplay — Buildings (runtime)
| Script | Responsabilidad |
|--------|-----------------|
| `BuildingInstance.cs` | Instancia de edificio colocado |
| `Production/ProductionBuilding.cs` | Cola de producción, rally, spawn unidades |

### 01_Gameplay — Combat
| Script | Responsabilidad |
|--------|-----------------|
| `Health.cs` | HP, daño, IHealth, IWorldBarSource, BarAnchor |
| `HealthBarManager.cs` | Singleton, barras Screen Space, Register/Unregister |
| `HealthBarUI.cs` | Una barra por entidad, Bind(Health), Refresh |
| `HealthBarWorld.cs` | **Legacy** (obsoleto), world-space bar |
| `IHealth.cs`, `IWorldBarSource.cs`, `WorldBarSettings.cs` | Contratos y config |
| `FloatingDamageText.cs` | Texto flotante de daño |

### 01_Gameplay — Map
| Script | Responsabilidad |
|--------|-----------------|
| `MapGrid.cs` | Grid 2D: blocked, occupied, water, terrainCosts. **Sin altura por celda.** |
| `GridConfig.cs` | ScriptableObject cellSize |
| `GridSnapUtil.cs` | Snap a grid (intersección / building) |
| `GridGizmoRenderer.cs` | Gizmos de celdas |
| `RTSCameraController.cs` | Movimiento, zoom, rotación, límites |
| `RTSMapGenerator.cs` | **Monolito:** preset, grid, seed, terrain, texturas, agua, NavMesh, spawns, TC, recursos, variantes, debug… |
| `MapResourcePlacer.cs` | Colocación de recursos en mapa (estático, usado por generador) |
| `CameraFocusOnSelection.cs`, `CameraOcclusionFade.cs`, `ShadowDistanceFromCamera.cs` | Cámara auxiliar |
| `NavMeshDebugHelper.cs` | Debug NavMesh |

### 01_Gameplay — MapGenerator
| Script | Responsabilidad |
|--------|-----------------|
| `MapGenerator.cs`, `MapGeneratorBridge.cs` | Puente generación |
| `GridSystem.cs`, `CellData.cs`, `HeightGenerator.cs`, `WaterGenerator.cs`, `WaterMeshBuilder.cs` | Datos y geometría |
| `TerrainCarver.cs`, `TerrainSkirtBuilder.cs`, `TerrainExporter.cs` | Terreno |
| `CityGenerator.cs`, `RegionGenerator.cs`, `RoadNetworkGenerator.cs`, `ResourceGenerator.cs` | Contenido |
| `MapValidator.cs`, `MapPreset.cs`, `MapGenConfig.cs`, `XorShiftRng.cs`, `IRng.cs` | Validación, config, RNG |

### 01_Gameplay — Pathfinding
| Script | Responsabilidad |
|--------|-----------------|
| `Pathfinder.cs` | A* sobre MapGrid |
| `PathSmoother.cs` | Suavizado de camino |

### 01_Gameplay — Players
| Script | Responsabilidad |
|--------|-----------------|
| `PlayerResources.cs` | Oro, madera, etc. |
| `PopulationManager.cs` | Población, casas |

### 01_Gameplay — Resources
| Script | Responsabilidad |
|--------|-----------------|
| `ResourceNode.cs`, `ResourceSelectable.cs`, `ResourceTypeSO.cs` | Nodo, selección, tipo |
| `AnimalPastureBehaviour.cs`, `FadeableByCamera.cs` | Animales, fade |

### 01_Gameplay — Selection
| Script | Responsabilidad |
|--------|-----------------|
| `SelectableOutline.cs` | Outline al seleccionar |

### 01_Gameplay — Units
| Script | Responsabilidad |
|--------|-----------------|
| `RTSSelectionController.cs` | Click, box, doble clic, hover recursos, edificio/recurso, health bars |
| `RTSOrderController.cs` | Clic derecho: BuildSite, recurso, drop-off, reparar, mover (raycasts + dispatch) |
| `UnitSelectable.cs`, `UnitMover.cs`, `UnitAnimatorDriver.cs` | Selección, movimiento, animación |
| `VillagerGatherer.cs`, `Builder.cs`, `Repairer.cs` | Roles aldeano |
| `OrderFeedback.cs`, `IdleVillagerIcon.cs`, `FormationHelper.cs` | Feedback, icono idle, formación |

### 03_UI — HUD
| Script | Responsabilidad |
|--------|-----------------|
| `SelectionBoxUI.cs` | Rectángulo de selección |
| `SelectionHealthBarUI.cs` | Muestra/oculta barras según selección (usa HealthBarManager) |
| `ResourceInfoUI.cs`, `ResourcesHUD.cs`, `PopulationHUD.cs` | Recursos y población |
| `VillagerBuildHUD.cs`, `BuildMenuUI.cs`, `BuildPanelVisibility.cs`, `BuildHotKeyRouter.cs` | Construcción |
| `ProductionHUD.cs` | **Muy grande:** cola de producción, UI, hotkeys |
| `ProductionHotkeyRouter.cs` | Atajos producción |
| `IdleVillagerHUD.cs`, `IdleVillagerHotkey.cs`, `NearestVillagerHotkey.cs`, `TownCenterHotkey.cs` | Aldeanos idle / TC |
| `RuntimeMinimapBootstrap.cs` | **Grande:** minimapa, cámara, clicks |
| `HUDContextController.cs`, `GameplayNotifications.cs` | Contexto y notificaciones |
| `TooltipUI.cs`, `TooltipTrigger.cs` | Tooltips |
| `RaycastDebugger.cs`, `QueueVisibilityDebugger.cs` | Debug |

### 04_Data
| Script | Responsabilidad |
|--------|-----------------|
| `BuildingSO.cs`, `UnitSO.cs` | Datos edificios/unidades |
| `BuildCatalog.cs`, `ProductionCatalog.cs` | Catálogos |

### Editor
| Script | Responsabilidad |
|--------|-----------------|
| `RTSMapGeneratorEditor.cs` | Custom inspector RTSMapGenerator |
| `FixBuildingPrefabEditor.cs`, `FixBuildingPivots.cs` | Herramientas prefabs |
| `WorldBarPrefabTools.cs`, `ToonyTinyPeopleURPMaterialFix.cs`, `CreateTestMPCCube.cs` | Utilidades |

---

## 2. Nodos críticos

| Nodo | Motivo |
|------|--------|
| **BuildingPlacer** | Único punto de colocación. Mezcla input, snap, **un solo sample de altura** (terrain.SampleHeight en pivot), validación, ghost, BuildSite, ocupación grid. Sin footprint ni pendiente. |
| **MapGrid** | Fuente de verdad para celdas ocupadas/blocked/water. **No expone altura por celda** (solo costos). Todo el placement vertical depende de Terrain en BuildingPlacer. |
| **RTSMapGenerator** | Orquestador + configuración + terreno + agua + recursos + spawns + TC + NavMesh + debug. ~2000+ líneas. Cualquier cambio de mapa toca aquí. |
| **RTSSelectionController** | Selección + box + doble clic + hover + health bars + buildingPlacer. Muchas responsabilidades en un solo script. |
| **RTSOrderController** | Resolución de clic derecho (varios raycasts en orden) + dispatch a Builder/Gatherer/Repairer/UnitMover. Orden y ejecución acopladas. |
| **HealthBarManager** | Singleton central para barras. Bien acotado, pero HealthBarWorld sigue en prefabs/escenas (legacy). |
| **RTSCameraController** | Cámara única. Input y aplicación en Update; sin SmoothDamp explícito en todos los ejes (revisar en Bloque 4). |

---

## 3. Scripts candidatos a refactor

| Script | Problema | Refactor sugerido (plan maestro) |
|--------|----------|----------------------------------|
| **RTSMapGenerator** | Monolito: terreno, agua, recursos, spawns, TC, NavMesh, texto, config. | Fase 6: orquestador + MapSceneConfig, TerrainVisualSettings, WaterVisualSettings, MapResourceBootstrapper, SpawnAndTownCenterPlacer. |
| **RTSOrderController** | Input + varios raycasts + resolución de tipo de objetivo + dispatch en un solo Update. | Fase 7: RTSOrderResolver, RTSOrderTargetResolver, RTSOrderDispatcher, RTSCommandFactory. |
| **RTSSelectionController** | Click, box, doble clic, hover, health bars, integración buildingPlacer. | Fase 8: núcleo selección + RTSSelectionBoxHandler, RTSSelectionHoverHandler, RTSDoubleClickSelector, RTSSelectionQueryService. |
| **BuildingPlacer** | Snap + validación ocupación + **altura en un punto** + ghost + BuildSite + asignación builders. Sin validación topográfica. | Fase 1: FootprintTerrainSampler, TerrainPlacementValidator, BuildingAnchorSolver; BuildingPlacer solo orquesta y ghost. |
| **PlacementValidator** | Solo OverlapBox + grid. No pendiente ni altura por footprint. | Fase 1: extender o reemplazar por TerrainPlacementValidator que use FootprintTerrainSampler. |
| **MapGrid** | Solo 2D (blocked, occupied, water). Sin altura. | Fase 1/3: agregar GetCellHeight / GetAreaAverageHeight / GetAreaMinMaxHeight (o integrar con datos de terreno existentes). |
| **ProductionHUD** | Script muy largo (cola, UI, eventos). | Extraer QueueItemClickHandler y lógica de cola a servicios o componentes más pequeños. |
| **RuntimeMinimapBootstrap** | Minimapa + cámara + input. | Considerar separar bootstrap, renderizado y manejo de clics. |

---

## 4. Riesgos de rendimiento

### FindFirstObjectByType / Camera.main

- **BuildingPlacer:** Awake + cada 0.75s si `terrain == null`: cam, owner, selection, terrain.
- **RTSSelectionController:** Awake: cam, selectionBoxUI, buildingPlacer.
- **RTSOrderController:** Awake: cam, selection.
- **RTSCameraController:** Awake + throttle: cam, mapGridForBounds, selection.
- **HealthBarManager:** Fallback cada frame si cámara null: `Camera.main`.
- **HealthBarWorld (legacy):** Varias veces `Camera.main` en Update.
- **SelectionHealthBarUI:** Resolución tardía de `selection` con FindFirstObjectByType.
- **VillagerBuildHUD, BuildModeController, IdleVillagerHotkey, ProductionHUD, ResourceInfoUI, HUDContextController, BuildMenuUI, BuildHotKeyRouter, BuildPanelVisibility, NearestVillagerHotkey, TownCenterHotkey, CameraFocusOnSelection:** Resuelven `selection` o otros en Awake/Start; algunos podrían quedar null y reintentar.
- **RTSMapGenerator:** FindFirstObjectByType NavMeshSurface, Terrain, RTSCameraController.
- **BuildSite:** Canvas.worldCamera = Camera.main; FindFirstObjectByType PopulationManager, Terrain.
- **RuntimeMinimapBootstrap:** FindFirstObjectByType RTSCameraController, Camera.
- **GridGizmoRenderer:** FindFirstObjectByType MapGrid, Terrain, BuildingPlacer.

**Recomendación:** Asignar en Inspector todo lo que sea singleton o servicio; usar Find solo como fallback una vez (o con throttle) y cachear. Reducir uso de `Camera.main` en Update.

### GetComponent repetidos

- **RTSOrderController (Update):** Por cada unidad seleccionada: GetComponent Builder, VillagerGatherer, Repairer, UnitMover; además GetComponentInParent BuildSite, ResourceNode, DropOffPoint, Health.
- **RTSSelectionController (AddSelection):** GetComponent VillagerGatherer, Builder por cada AddSelection.
- **BuildingPlacer (AutoAssignBuilders):** GetComponent Builder por cada seleccionado; FindObjectsByType Builder si no hay selección.

**Recomendación:** Cachear componentes en unidades (ej. Builder, VillagerGatherer, UnitMover) o en un pequeño “UnitCommands” que exponga ya resueltos los roles.

### FindObjectsByType en cada box/doble clic

- **RTSSelectionController:** `BoxSelect` y `DoubleClickSelect` llaman a `FindObjectsByType<UnitSelectable>(FindObjectsSortMode.None)` cada vez. Con muchas unidades puede notarse.

**Recomendación:** Mantener un registro de UnitSelectable activos (registry) y filtrar por pantalla/rect en lugar de FindObjectsByType cada vez.

### Lógica por frame

- **BuildingPlacer Update:** Raycast cada frame cuando está colocando; sincronización gridSize con MapGrid; throttle para Terrain; múltiples comprobaciones. Aceptable pero concentrado.
- **HealthBarManager:** LateUpdate recorre todas las barras registradas y posiciona en pantalla. Proporcional al número de barras visibles.
- **RTSCameraController:** Input y movimiento en Update; revisar si hay allocations (ej. listas nuevas).

---

## 5. Mapa de dependencias (resumido)

```
GameBootstrapper
  └ (escena / flujo de juego)

BuildingPlacer
  ├ GridConfig / MapGrid.Instance
  ├ Camera, Terrain, PlayerResources, RTSSelectionController
  ├ BuildingSO, BuildSite prefab
  └ PlacementValidator → MapGrid

RTSSelectionController
  ├ Camera, SelectionBoxUI, BuildingPlacer
  ├ HealthBarManager (ShowBarForEntity / HideBarForEntity)
  └ UnitSelectable, BuildingSelectable, ResourceSelectable

RTSOrderController
  ├ Camera, RTSSelectionController
  ├ CommandBus → MoveCommand
  └ BuildSite, ResourceNode, DropOffPoint, Health, Builder, VillagerGatherer, Repairer, UnitMover

RTSCameraController
  ├ Camera, MapGrid (bounds), Terrain (bounds), RTSSelectionController
  └ (focus Town Center al inicio)

RTSMapGenerator
  ├ GridConfig, MapGrid, Terrain, NavMeshSurface
  ├ TownCenter SO/prefab, recursos prefabs, MapResourcePlacer
  ├ RTSCameraController (focus), Terrain, NavMesh
  └ MapGenerator, HeightGenerator, WaterGenerator, etc.

HealthBarManager
  ├ Canvas, prefab HealthBarUI, Camera
  └ Health (Register/Unregister por entidad)

MapGrid
  └ (datos de celdas; inicializado por RTSMapGenerator)
```

**Dependencias frágiles:**

- Muchos scripts dependen de `RTSSelectionController` y lo resuelven con FindFirstObjectByType si no está asignado.
- BuildingPlacer depende de Terrain en escena; si no hay Terrain, reintenta cada 0.75s.
- MapGrid es Singleton; RTSMapGenerator lo inicializa. Cualquier flujo que use MapGrid antes de que el mapa esté listo puede fallar.
- HealthBarWorld (legacy) sigue en uso en algunos prefabs; convive con HealthBarManager.

---

## 6. Sistemas legacy detectados

| Sistema | Estado | Acción recomendada |
|---------|--------|---------------------|
| **HealthBarWorld** | Obsoleto, marcado [Obsolete] | Fase 9: identificar prefabs que lo usan, migrar a HealthBarManager + HealthBarUI, eliminar referencias. |
| **PF_WorldHealthBar** | Prefab barra world-space | Sustituir por PF_HealthBarUI donde corresponda; retirar cuando no quede referencia. |
| **PlacementValidator** | Solo overlap + grid | Fase 1: complementar con TerrainPlacementValidator (footprint + pendiente); mantener o reemplazar según diseño. |

---

## 7. Posibles causas de problemas actuales

| Problema | Causa probable (según auditoría) |
|----------|-----------------------------------|
| **Edificios flotando/enterrados** | Un solo sample de altura en BuildingPlacer (pivot); sin FootprintTerrainSampler ni BuildingAnchorSolver. Terrain.SampleHeight(p) no considera extensión del edificio. |
| **Placement en laderas** | No hay validación de pendiente ni de delta de altura en el footprint (TerrainPlacementValidator no existe). |
| **Cámara tosca** | RTSCameraController: revisar si todo el movimiento/zoom usa suavizado (SmoothDamp/SmoothDampAngle) y si input está separado de aplicación (LateUpdate). |
| **Prefabs / referencias raras** | Varios componentes resuelven refs con Find*; BuildingSO nulo en algunos (ej. PF_TownCenter mencionado en plan); BarAnchor, colliders y pivots sin estándar. Fase 2 y 3. |
| **Rendimiento** | FindObjectsByType en box/doble clic; GetComponent repetidos en órdenes; múltiples FindFirstObjectByType/Camera.main sin cachear. |

---

## 8. Siguiente paso recomendado

Seguir el plan maestro en orden: **Bloque 2 (Placement y terreno)** — implementar FootprintTerrainSampler, TerrainPlacementValidator, BuildingAnchorSolver y refactorizar BuildingPlacer sin romper el flujo actual; luego validar en escena (ghost, colocación, grid).

---

*Auditoría Bloque 1 — sin modificación de código. Generado para WAR OF REALMS.*
