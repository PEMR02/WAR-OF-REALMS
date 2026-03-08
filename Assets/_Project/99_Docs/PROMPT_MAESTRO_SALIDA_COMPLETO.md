# Prompt Maestro — Salida completa (Fases 1–8)

## Lista de mejoras realmente integradas

### Fase 1 — Building Placement
- **BuildingAnchorSolver** integrado: un solo `FootprintTerrainSampler.Sample` por frame; TerrainPlacementValidator + BuildingAnchorSolver.Solve para placementY y ghostPivotY. Ghost y BuildSite comparten la misma altura. AlignBuildSiteToTerrain se mantiene como complemento.

### Fase 2 — Órdenes RTS
- **RTSOrderTargetResolver** ya era la única entrada (un Resolve por clic derecho). **Caché de componentes:** `CachedUnitComponents` + lista reutilizable `_cachedUnits`; un GetComponent por tipo por unidad por orden (5×N una vez); los cuatro Dispatch* reciben la lista cacheada y no llaman GetComponent. Sin alloc por orden.

### Fase 3 — Selección RTS
- **UnitSelectableRegistry** ya usado en BoxSelect y DoubleClickSelect; no hay FindObjectsByType para unidades. Click simple y hover siguen con raycast (correcto). UnitSelectable se registra en OnEnable/OnDisable.

### Fase 4 — Prefabs
- **Testing desactivado:** `startWithPercentForTesting: 0` en PF_TownCenter, PF_House, PF_Granary, PF_LumberCamp, PF_MiningCamp, PF_Peasant, PF_Swordman, PF_Archer, PF_Mounted_King. Unidades y edificios arrancan a vida llena.

### Fase 5 — Auditoría assets importados
- Documento **FASE_5_AUDITORIA_ASSETS_IMPORTADOS.md**: posibles problemas (pivot, scale, rotación, materiales, animator, avatar), checklist manual en Unity (FBX, prefabs, animaciones) y prefabs que podrían depender de asset mal importado. Sin cambios en assets; auditoría documental.

### Fase 6 — RTSMapGenerator
- Documento **FASE_6_RTSMAPGENERATOR_SIGUIENTE_EXTRACCION.md**: responsabilidades actuales, **siguiente extracción recomendada = agua (malla visual)** (~350 líneas). Pasos sugeridos para extraer MapWaterMeshGenerator sin reescribir todo. Siguiente candidato: Town center placement.

### Fase 7 — Rendimiento
- **FASE_7_AUDITORIA_RENDIMIENTO.md**: scripts con Update/LateUpdate, raycasts, GetComponent, Find, GC/listas. Sin costes muy altos; mejoras aplicadas: **HealthBarManager** usa lista reutilizable `_toRemove` (zero alloc en LateUpdate/Unregister cuando hay entradas a eliminar). Opcionales: desactivar HealthBarWorld si no se usa, culling de barras.

### Fase 8 — Cámara RTS
- Sin cambios de código; documentado en **FASE_8_CAMARA_RTS.md** (qué está bien, qué afinar, valores de tuning). Opcional: implementar `smoothEdgeMovement` en los bordes.

---

## Lista de cosas pendientes reales

- **Cámara:** Implementar uso real de `smoothEdgeMovement` en los bordes del mapa (opcional).
- **Prefabs:** Revisión manual en Unity de footprint vs collider, BarAnchor, buildingSO y escala (PF_Peasant, edificios).
- **RTSMapGenerator:** Ejecutar la extracción de la malla de agua (MapWaterMeshGenerator) según FASE_6; luego Town center placement.
- **Assets:** Ejecutar checklist de FASE_5 en Unity (pivot, scale, materiales, animator) por cada tipo de prefab.
- **HealthBarWorld (legacy):** Desactivar en prefabs/escena si solo se usa HealthBarManager.

---

## Lista de riesgos futuros

- Cambiar estrategia de altura (avgHeight vs minHeight) en placement sin tocar BuildingAnchorSolver y ghost a la vez.
- Nuevos prefabs con pivot en la base: el solver ya contempla visualOffsetY = 0.
- Refactors grandes en RTSMapGenerator sin extracciones pequeñas: riesgo de regresiones.

---

## Documentos generados (todas las fases)

| Fase | Documento |
|------|-----------|
| 1 | FASE_1_PLACEMENT_CONSOLIDADO.md |
| 2 | FASE_2_ORDENES_RTS_CERRADO.md |
| 3 | FASE_3_SELECCION_RTS_CERRADO.md |
| 4 | FASE_4_PREFABS_LIMPIEZA.md |
| 5 | FASE_5_AUDITORIA_ASSETS_IMPORTADOS.md |
| 6 | FASE_6_RTSMAPGENERATOR_SIGUIENTE_EXTRACCION.md |
| 7 | FASE_7_AUDITORIA_RENDIMIENTO.md |
| 8 | FASE_8_CAMARA_RTS.md |
| — | PROMPT_MAESTRO_SALIDA_FASE_1_4_8.md (salida parcial sesión anterior) |
| — | **PROMPT_MAESTRO_SALIDA_COMPLETO.md** (este archivo) |

---

## Cambios de código realizados en esta sesión (Fases 2–7)

- **RTSOrderController.cs:** Struct `CachedUnitComponents`, lista `_cachedUnits`, `CacheSelectedUnits()`; Dispatch* reciben `List<CachedUnitComponents>` y dejan de hacer GetComponent por unidad.
- **HealthBarManager.cs:** Campo `_toRemove` reutilizable; `Unregister` y `LateUpdate` usan `_toRemove.Clear()` y `_toRemove.Add` en lugar de `toRemove ??= new List<>()`, evitando alloc cuando hay entradas a eliminar.
