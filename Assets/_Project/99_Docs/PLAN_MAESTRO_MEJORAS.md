# Plan Maestro de Mejoras — WAR OF REALMS

Objetivo: base sólida en cinco frentes:
- placement y anclaje de edificios al terreno
- normalización de prefabs y assets
- refactor del sistema de mapa/terrain
- mejora de cámara y rendimiento percibido
- limpieza estructural de scripts centrales

## Orden de ejecución recomendado

1. **Prioridad 1** — Placement + anclaje + footprint  
2. **Prioridad 2** — Prefabs y assets base  
3. **Prioridad 3** — Grid/escala/footprints  
4. **Prioridad 4** — Cámara  
5. **Prioridad 5** — Auditoría de rendimiento  
6. **Prioridad 6** — Descomposición RTSMapGenerator  
7. **Prioridad 7** — OrderController / SelectionController  
8. **Prioridad 8** — Combat / HealthBars  
9. **Prioridad 9** — Limpieza Building vs Buildings  

## Bloques para Cursor (orden)

- **Bloque 1** — Auditoría general (inventario, nodos críticos, candidatos a refactor, riesgos, dependencias)
- **Bloque 2** — Placement y terreno (FootprintTerrainSampler, TerrainPlacementValidator, BuildingAnchorSolver, refactor BuildingPlacer)
- **Bloque 3** — Prefabs y assets (auditoría PF_TownCenter, Barracks, House, Aldeano; tabla, estructura, checklist)
- **Bloque 4** — Cámara (análisis, smoothing, LateUpdate, SmoothDamp)
- **Bloque 5** — Rendimiento (Update pesado, raycasts, Find*, GetComponent, GC)
- **Bloque 6** — RTSMapGenerator (orquestador, extracción incremental)
- **Bloque 7** — Órdenes y selección (OrderController, SelectionController)
- **Bloque 8** — Combat / HealthBars (sistema principal vs legacy, migración)

## Validación después de cada fase

- ¿Compila sin errores?
- ¿La escena principal entra a Play?
- ¿Se puede seleccionar unidades? ¿Colocar edificios?
- ¿Cámara responde bien? ¿Barra de vida se actualiza?
- ¿NavMesh funciona? ¿Edificios no flotan/entierran?
- ¿Prefabs base intactos?

---

*Detalle completo de fases 0–11 y especificaciones técnicas en conversación origen.*
