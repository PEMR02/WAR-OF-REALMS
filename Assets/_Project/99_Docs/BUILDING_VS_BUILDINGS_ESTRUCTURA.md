# Building vs Buildings — Estructura y responsabilidades

## Objetivo

Evitar confusión entre "colocar/construir edificios" y "edificios en runtime", y reducir duplicidad de scripts.

## Carpetas y roles

### 01_Gameplay/Building (singular)

**Colocación, validación y construcción (fundación → edificio terminado).**

| Script / Carpeta | Responsabilidad |
|------------------|-----------------|
| **Placement/** | |
| `BuildingPlacer` | Modo colocación: input, snap XZ, altura por footprint, ghost, BuildSite, ocupación grid. |
| `BuildModeController` | Activa/desactiva modo construcción; integración con UI. |
| `GhostPreview` | Visual verde/rojo del ghost. |
| `FootprintTerrainSampler` | Muestreo de terreno en el footprint. |
| `TerrainPlacementValidator` | Valida pendiente y altura del footprint. |
| `BuildingAnchorSolver` | Calcula altura final de colocación. |
| `PlacementValidator` | OverlapBox + MapGrid (ocupación); sin terreno. |
| **Construction/** | |
| `BuildSite` | Fundación en mundo; progreso de construcción; spawn del prefab final. |
| `Builder` | Aldeano que construye en un BuildSite. |
| `TownCenter` | Lógica específica del Town Center (spawn inicial, etc.). |
| **DropOff/** | Puntos de entrega de recursos. |
| **Raíz** | |
| `BuildingController` | Comportamiento del edificio en runtime (límites, debug). |
| `BuildingSelectable` | Seleccionable + outline para edificios. |

### 01_Gameplay/Buildings (plural)

**Edificio ya colocado en el mundo (runtime).**

| Script | Responsabilidad |
|--------|-----------------|
| `BuildingInstance` | Referencia al BuildingSO; identidad del edificio colocado. |
| `Production/ProductionBuilding` | Cola de producción, rally point, spawn de unidades. |

## Regla práctica

- **Building** (singular): todo lo que ocurre *antes* y *durante* la construcción (placement, BuildSite, Builder).
- **Buildings** (plural): el objeto *edificio* una vez existe en escena (BuildingInstance, ProductionBuilding).

## Dónde poner scripts nuevos

- ¿Colocación, ghost, validación de terreno, BuildSite? → **Building** (o Building/Placement, Building/Construction).
- ¿Comportamiento del edificio ya construido (producción, reparación, drop-off)? → **Buildings** (o Building si es selección/control genérico).

---

*Documento Bloque 9 — orden conceptual Building vs Buildings.*
