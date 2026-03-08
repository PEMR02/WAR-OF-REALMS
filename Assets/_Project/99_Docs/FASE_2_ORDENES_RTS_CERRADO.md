# Fase 2 — Refactor órdenes RTS cerrado

## Qué quedó integrado

- **RTSOrderTargetResolver** ya era la única entrada: un solo `Resolve(ray, ...)` por clic derecho; el resultado se usa en un `switch` y se delega a `DispatchBuildSite`, `DispatchGather`, `DispatchBuilding`, `DispatchMove`. No hay raycasts duplicados en el controller.
- **Raycasts necesarios:** Todos están dentro de `RTSOrderTargetResolver.Resolve`: hasta 4 raycasts en orden (BuildSite → Resource → Building → Ground), uno por tipo de capa; el primero que impacta devuelve. No se puede reducir sin cambiar la semántica (prioridad de objetivos).
- **Caché de componentes:** Se añadió `CachedUnitComponents` (selectable, builder, gatherer, mover, repairer) y una lista reutilizable `_cachedUnits`. En cada clic derecho se llama a `CacheSelectedUnits(selectedUnits)`, que hace **un GetComponent por tipo por unidad** (5 componentes × N unidades) una sola vez; los cuatro `Dispatch*` reciben `List<CachedUnitComponents>` y ya no llaman a `GetComponent`. Se eliminan las llamadas repetidas a GetComponent dentro de cada Dispatch.

## Qué se simplificó

- Lógica de órdenes: todo pasa por el resolver y el switch; no hay ramas hardcodeadas de raycast en el controller.
- Coste por orden: de ~4 GetComponent por unidad (Builder, VillagerGatherer, UnitMover, Repairer en distintos bucles) a 5 GetComponent por unidad una sola vez (en CacheSelectedUnits).
- Sin alloc por orden: la lista `_cachedUnits` se reutiliza (Clear + Add); los structs `CachedUnitComponents` viven en la lista.

## Riesgos

- Ninguno funcional; el comportamiento es el mismo. Si en el futuro se añaden más componentes por unidad (ej. Healer), hay que añadirlos a `CachedUnitComponents` y a `CacheSelectedUnits`.
