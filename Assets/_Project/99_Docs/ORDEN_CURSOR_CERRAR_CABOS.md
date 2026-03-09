# Orden completa para Cursor — Cerrar cabos sueltos

Trabajar sobre el estado actual del proyecto Unity WAR OF REALMS.

**Objetivo:** Consolidar las mejoras ya implementadas y cerrar los cabos sueltos detectados en revisión del repo y validación en Unity.

**IMPORTANTE:**
- No reescritura total
- No sobreingeniería
- Mejoras incrementales y seguras
- Cada fase termina con resumen + checklist de pruebas manuales

---

## FASE 1 — Validar e integrar completamente Building Placement

**Revisar:** BuildingPlacer.cs, PlacementValidator.cs, BuildingAnchorSolver.cs, FootprintTerrainSampler.cs, TerrainPlacementValidator.cs, MapGrid.cs.

**Objetivos:**
1. Confirmar si BuildingAnchorSolver está realmente integrado en el flujo principal.
2. Si no lo está completamente, integrarlo correctamente.
3. El placement final debe usar: footprint completo, min/max/avg height, slope validation, anchor correcto según pivot/base real.
4. Revisar ghost placement: evitar ghost flotando o enterrado.
5. Revisar redundancias entre avgHeight directo, AlignBuildSiteToTerrain y BuildingAnchorSolver.
6. Dejar una lógica dominante clara para altura final de colocación.
7. Revisar si conviene estrategia configurable por edificio (avgHeight, minHeight, otra).

**Entregar:** Resumen exacto de lo corregido, lógica dominante, checklist manual de pruebas en Unity.

---

## FASE 2 — Limpieza real de prefabs base

**Revisar:** PF_TownCenter.prefab, PF_House.prefab, PF_Barracks.prefab, Aldeano.prefab (en el proyecto: PF_Peasant como villager).

**Objetivos:**
1. Detectar y eliminar residuos de testing: startWithPercentForTesting, startPercent, _currentHP incoherente.
2. Confirmar buildingSO bien asignado.
3. Revisar: barAnchor, renderers, colliders, NavMeshObstacle, escala, animator en Aldeano/villager.
4. Alinear prefabs con uso jugable real.
5. Marcar qué requiere revisión manual en Unity (no asegurable solo por YAML).

**Entregar:** Tabla por prefab (error detectado, corrección aplicada, revisión manual pendiente), resumen de consistencia final.

---

## FASE 3 — Consolidar órdenes RTS

**Revisar:** RTSOrderController.cs, RTSOrderTargetResolver.cs, clases de caché relacionadas.

**Objetivos:**
1. Confirmar que RTSOrderTargetResolver se usa en el flujo principal.
2. Reducir raycasts redundantes o lógica duplicada.
3. Revisar GetComponent repetidos y consolidar caché de componentes.
4. Mantener comportamiento: move, gather, build site.
5. Mejorar legibilidad sin reescribir todo.

**Entregar:** Qué quedó integrado, qué se simplificó, qué raycasts siguen siendo necesarios.

---

## FASE 4 — Consolidar selección RTS

**Revisar:** RTSSelectionController.cs, UnitSelectableRegistry.cs.

**Objetivos:**
1. Confirmar que box select y doble clic usan UnitSelectableRegistry.
2. Revisar si quedan FindObjectsByType o búsquedas globales innecesarias.
3. Mantener: clic simple, box selection, doble clic, hover.
4. No romper UX actual.

**Entregar:** Qué quedó reemplazado, qué sigue pendiente, mejora estimada de rendimiento o limpieza.

---

## FASE 5 — Validación de cámara RTS

**Revisar:** RTSCameraController.cs.

**Objetivos:**
1. Confirmar si el smoothing actual está correcto.
2. Revisar causas de sensación tosca: clamps, edge movement, aceleración, rotación, zoom.
3. Ajustar solo si mejora sensación sin romper comportamiento.
4. Proponer valores iniciales recomendados de tuning.

**Entregar:** Qué ya está bien, qué conviene afinar, valores sugeridos para move, rotation y zoom.

---

## FASE 6 — Siguiente extracción segura de RTSMapGenerator

**Revisar:** RTSMapGenerator.cs, MapWaterMeshGenerator.cs, MapResourcePlacer.cs, MapGenConfigFactory.cs, clases auxiliares.

**Objetivos:**
1. Confirmar qué ya fue extraído realmente.
2. Identificar la siguiente responsabilidad más clara para sacar de RTSMapGenerator.
3. No reescribir todo.
4. Proponer una extracción incremental segura.

**Entregar:** Siguiente módulo ideal a extraer, por qué ese primero, riesgo técnico, impacto esperado.

---

## FASE 7 — Auditoría de rendimiento actual

**Revisar:** placement, selección, órdenes, cámara, health bars, mapa.

**Buscar:** Update/LateUpdate costosos, raycasts por frame, GetComponent repetidos, FindObjectOfType/FindObjectsByType, GC Alloc innecesario, listas/strings por frame.

**Entregar:** Scripts sospechosos, gravedad, mejora concreta aplicable, qué medir en Profiler manualmente.

---

## Reglas importantes

1. No romper jugabilidad actual.
2. No crear sistemas nuevos innecesarios.
3. No hacer reescrituras completas.
4. Si un cambio es dudoso, explicarlo antes de aplicarlo.
5. Cada fase debe terminar con: resumen de cambios, riesgos, checklist manual para Unity.

---

## Salida final esperada

- Lista de mejoras realmente consolidadas
- Lista de pendientes reales
- Lista de riesgos futuros
- Orden recomendado para la siguiente iteración

---

## Orden práctica de ejecución (sesiones)

| Sesión | Fases |
|--------|--------|
| **Sesión 1** | Fase 1 + Fase 2 |
| **Sesión 2** | Fase 3 + Fase 4 |
| **Sesión 3** | Fase 5 + Fase 7 |
| **Sesión 4** | Fase 6 |

Así se mantiene control y se evita tocar todo a la vez.

---

## Sugerencia directa de prioridad

1. **Primero placement** (edificios bien apoyados).
2. **Después prefabs** (prefabs limpios y coherentes).
3. **Después cámara** (sensación fluida).
4. Luego selector/órdenes y adelgazar RTSMapGenerator.

Eso mejora primero lo que más se ve: edificios mal apoyados, prefabs sucios, cámara tosca.
