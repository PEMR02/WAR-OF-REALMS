# Prompt Maestro — Salida (Fases 1, 4 y 8)

## Lista de mejoras realmente integradas

- **Fase 1 — Building Placement**
  - **BuildingAnchorSolver** usado en el flujo: una sola llamada a `FootprintTerrainSampler.Sample` por frame; con el resultado se valida terreno (`TerrainPlacementValidator`), se obtiene la Y de colocación y la Y del pivot del ghost vía `BuildingAnchorSolver.Solve`. Ghost y BuildSite comparten la misma lógica de altura (placementY + visualOffsetY para el pivot).
  - Ghost y BuildSite ya no dependen de lógica duplicada; la altura sale del solver.
  - `AlignBuildSiteToTerrain` se mantiene como complemento (alinea la base visual del site al terreno); no sustituye al solver.

- **Fase 4 — Prefabs**
  - **Testing desactivado en prefabs jugables:** en todos los prefabs con `startWithPercentForTesting: 1` se puso `startWithPercentForTesting: 0` (PF_TownCenter, PF_House, PF_Granary, PF_LumberCamp, PF_MiningCamp, PF_Peasant, PF_Swordman, PF_Archer, PF_Mounted_King). PF_Castillo y PF_Barracks ya tenían testing en 0.
  - Unidades y edificios arrancan con vida al 100% (o lo que fije `_currentHP`/maxHP cuando testing está desactivado).

- **Fase 8 — Cámara**
  - Sin cambios de código; la cámara ya tenía input en Update y aplicación suavizada en LateUpdate. Se documentó qué está bien, qué afinar y valores de tuning recomendados en `FASE_8_CAMARA_RTS.md`.

---

## Lista de cosas pendientes reales

- **Fase 2 — Órdenes RTS:** Revisar si `RTSOrderController` sigue haciendo raycasts redundantes; integrar del todo `RTSOrderTargetResolver`; cachear componentes por unidad donde sea razonable.
- **Fase 3 — Selección RTS:** Sustituir del todo `FindObjectsByType` por `UnitSelectableRegistry` en `RTSSelectionController` (box/doble clic); mantener selección simple y hover.
- **Fase 5 — Auditoría assets importados:** Revisar pivot, scale factor, rotación, materiales, animator/avatar en FBX usados por unidades y edificios; checklist manual en Unity.
- **Fase 6 — RTSMapGenerator:** Siguiente extracción incremental (responsabilidades: agua, recursos, spawns, town center, visual, debug); proponer el siguiente módulo más claro y seguro.
- **Fase 7 — Rendimiento:** Auditoría Update/LateUpdate, raycasts por frame, GetComponent/Find, GC Alloc, listas por frame en cámara, selección, órdenes, placement, health bars, mapa.
- **Cámara:** Implementar uso real de `smoothEdgeMovement` en los bordes del mapa (opcional).
- **Prefabs:** Revisión manual en Unity de footprint vs collider, BarAnchor, buildingSO y escala en Aldeano/villager (PF_Peasant).

---

## Lista de riesgos futuros

- Cambiar estrategia de altura (avgHeight vs minHeight) en placement sin tocar BuildingAnchorSolver y ghost a la vez.
- Nuevos prefabs con pivot en la base: `_ghostPivotToBottom = 0`; el solver ya lo contempla (visualOffsetY = 0).
- Sistemas que asumían “vida al X% al inicio” en prefabs: ahora todos arrancan al 100% con testing desactivado.
- Refactors grandes en RTSMapGenerator sin extracciones pequeñas previas: riesgo de regresiones.

---

## Orden recomendado para la siguiente iteración

1. **Sesión 2 (Fase 2 + Fase 3):** Cerrar refactor de órdenes (RTSOrderController + RTSOrderTargetResolver) y de selección (UnitSelectableRegistry en RTSSelectionController). Impacto directo en claridad y rendimiento.
2. **Sesión 3 (Fase 5 + Fase 6):** Auditoría de assets importados (FBX, materiales, animaciones) y siguiente extracción de RTSMapGenerator.
3. **Sesión 4 (Fase 7 + opcional Fase 8):** Auditoría de rendimiento (scripts sospechosos, raycasts, GetComponent, GC) y, si se desea, implementar `smoothEdgeMovement` en la cámara.

---

## Documentos generados en esta sesión

- `FASE_1_PLACEMENT_CONSOLIDADO.md` — Resumen Fase 1, lógica dominante, checklist manual.
- `FASE_4_PREFABS_LIMPIEZA.md` — Tabla por prefab, correcciones aplicadas, revisión manual pendiente.
- `FASE_8_CAMARA_RTS.md` — Qué quedó bien, qué afinar, valores de tuning recomendados.
- `PROMPT_MAESTRO_SALIDA_FASE_1_4_8.md` — Este archivo (salida consolidada).
