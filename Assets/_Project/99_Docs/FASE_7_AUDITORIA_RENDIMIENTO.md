# Fase 7 — Auditoría de rendimiento actual

## Ámbitos revisados

Cámara, selección, órdenes, placement, health bars, mapa y scripts que se ejecutan cada frame (Update/LateUpdate).

---

## Scripts con Update/LateUpdate (resumen)

| Script | Método | Uso |
|--------|--------|-----|
| RTSOrderController | Update | Solo reacciona a rightButton.wasPressedThisFrame; un Resolve (4 raycasts máx.) solo al clic. Coste por frame bajo. |
| BuildingPlacer | Update | Un raycast + FootprintTerrainSampler (varios SampleHeight) mientras se coloca. Solo activo en modo colocación. |
| RTSSelectionController | Update | Input + 1 raycast por frame en UpdateResourceHover cuando hay aldeanos seleccionados. |
| RTSCameraController | Update / LateUpdate | Input en Update; SmoothDamp en LateUpdate. Sin Find cada frame (throttle para cam/selection). |
| HealthBarManager | LateUpdate | Itera _barsByEntity; WorldToScreenPoint + RectTransformUtility por barra. O(N) con N = entidades con barra visible. |
| HealthBarWorld (legacy) | LateUpdate | Similar; considerar desactivar si se usa solo HealthBarManager. |
| BuildSite | LateUpdate | Lógica de progreso/constructores. Revisar si hace GetComponent o búsquedas cada frame. |
| UnitAnimatorDriver, VillagerGatherer, Builder, UnitMover, Repairer, etc. | Update | Lógica por unidad/edificio. GetComponent ya cacheado en órdenes (Fase 2); en estos scripts suele ser una vez en Start/Awake o por evento. |

---

## Hallazgos por categoría

### Raycasts por frame

- **RTSSelectionController.UpdateResourceHover:** 1 `Physics.Raycast` por frame cuando hay aldeanos seleccionados y el cursor no está sobre UI. Necesario para detectar recurso bajo el cursor; no hay alternativa sin raycast. Aceptable si el layer mask está bien acotado.
- **BuildingPlacer.Update:** 1 raycast al suelo + FootprintTerrainSampler (varios Terrain.SampleHeight) solo mientras IsPlacing. Aceptable.
- **RTSOrderController:** Raycasts solo en clic derecho (RTSOrderTargetResolver). No por frame.

### GetComponent repetidos

- **Fase 2 (hecho):** RTSOrderController cachea Builder, VillagerGatherer, UnitMover, Repairer por unidad en cada orden; ya no hay múltiples GetComponent por Dispatch.
- **RTSSelectionController.AddSelection:** 2 GetComponent (VillagerGatherer, Builder) por unidad añadida a la selección para actualizar _selectedVillagerCount. Coste bajo (solo al añadir); opcional cachear en UnitSelectable o en un pequeño cache si se prioriza rendimiento extremo.
- **BuildSite:** Revisar si en LateUpdate o en notificaciones hace GetComponent en builders; si es por evento (builder se registra), no hay problema.

### FindObjectOfType / FindObjectsByType

- **RTSCameraController:** Resuelve `cam` y `selection` con throttle (timer 1 s) cuando son null; no cada frame.
- **RTSMapGenerator.EnsureWaterVisibleNextFrame:** `FindObjectsByType<Camera>` una vez al terminar de generar agua. No en bucle.
- **UnitSelectableRegistry:** Ya usado para box/doble clic; no hay FindObjectsByType en el flujo de selección (Fase 3).

### GC Alloc / listas por frame

- **HealthBarManager.LateUpdate:** `toRemove ??= new List<GameObject>()` solo cuando hay entradas null; alloc ocasional, no cada frame. Si se quiere cero alloc, usar una lista reutilizable en el manager (ej. `_toRemove.Clear()` y reciclar).
- **BuildSite:** `new List<Builder>(_builders)` en eventos (p. ej. para iterar sin modificar la colección). Si se llama a menudo, considerar ReadOnlyList o iterar sobre una copia reutilizable.
- **Pathfinder / PathSmoother:** Retornan `new List<Vector2Int>` o `new List<Vector3>` por cada pathfinding request; normal para el resultado. No en Update.
- **FormationHelper.GenerateGrid:** `new List<Vector3>(unitCount)` por orden de movimiento; una vez por clic. Aceptable.
- **RTSOrderController._cachedUnits:** Lista reutilizable; Clear + Add sin alloc nuevo (Fase 2).

---

## Scripts sospechosos y gravedad

| Script | Gravedad | Notas |
|--------|----------|--------|
| HealthBarManager.LateUpdate | Baja | Itera todas las barras; WorldToScreenPoint + Refresh por barra. Con muchas unidades/edificios (50+) puede notarse. Mejora: frustum cull o LOD de barras (ocultar si están lejos). |
| RTSSelectionController.UpdateResourceHover | Baja | 1 raycast por frame con villagers seleccionados. Aceptable; layer mask acotado. |
| HealthBarWorld (legacy) | Baja | Si hay dos sistemas de barras (HealthBarManager + HealthBarWorld), duplicar trabajo. Ya documentado en COMBAT_HEALTHBARS_LEGACY.md; desactivar HealthBarWorld si no se usa. |
| BuildSite (copias de lista) | Muy baja | Solo si hay muchos build sites activos y eventos frecuentes. |
| FadeableByCamera.LateUpdate | Baja | Un Lerp/MoveTowards por instancia; arrays ya cacheados (_renderers, _instancedMaterials). Aceptable. |

---

## Mejoras concretas aplicables (sin cambiar comportamiento)

1. **HealthBarManager:** Reutilizar lista para toRemove: campo `List<GameObject> _toRemove` en el manager; en LateUpdate usar `_toRemove.Clear()` y añadir a `_toRemove` en lugar de `toRemove ??= new List<>()`. Así no se asigna en LateUpdate cuando hay entradas a eliminar.
2. **HealthBarWorld:** Si el proyecto usa solo HealthBarManager para barras en pantalla, desactivar o eliminar el componente HealthBarWorld de prefabs/escena para evitar doble actualización.
3. **RTSSelectionController.AddSelection:** Opcional: cachear en UnitSelectable si es Villager/Builder (dos bools o refs) para no hacer GetComponent al añadir a la selección; impacto pequeño pero evita 2 GetComponent por unidad seleccionada.
4. **Culling de health bars:** En HealthBarManager, antes de WorldToScreenPoint, comprobar si la entidad está en el frustum de la cámara (GeometryUtility.TestPlanesAABB) para no actualizar barras fuera de vista. Reduce trabajo cuando hay muchas entidades.

---

## Resumen

- No se detectaron Update/LateUpdate con coste muy alto; la mayoría son input, un raycast o iteraciones sobre listas ya acotadas (selección, barras).
- Las mejoras de Fase 2 (cache de componentes en órdenes) y Fase 3 (registro para box/doble clic) ya reducen GetComponent y FindObjectsByType.
- Mejoras de bajo riesgo: reutilizar lista en HealthBarManager, desactivar HealthBarWorld si no se usa, y opcional culling/frustum para barras si hay muchos personajes en pantalla.
