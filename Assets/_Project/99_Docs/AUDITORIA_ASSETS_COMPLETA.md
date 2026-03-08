# Auditoría de assets — WAR OF REALMS (estilo Anno)

Resumen de scripts, prefabs y referencias críticas revisadas para mapa y colocación tipo Anno.

---

## 1. Scripts de colocación y terreno

| Script | Función |
|--------|--------|
| **FootprintTerrainSampler** | Muestreo de altura en centro + 4 esquinas (+ 4 puntos de borde si footprint ≥ 3 celdas). Reduce flotar en edificios grandes. |
| **TerrainPlacementValidator** | Valida maxHeightDelta y maxSlopeDegrees. |
| **BuildingAnchorSolver** | Altura final = promedio del footprint. |
| **BuildingPlacer** | Snap XZ, validación ocupación + terreno, ghost, BuildSite. Parámetros por defecto ajustados a estilo Anno (maxHeightDelta 1.8, maxSlopeDegrees 14). |
| **PlacementValidator** | OverlapBox + MapGrid (ocupación y bloqueados). |
| **MapGrid** | GetCellHeight, GetAreaAverageHeight, GetAreaMinMaxHeight (con Terrain). |

---

## 2. Prefabs de edificios — referencias críticas

| Prefab | BuildingSO (BuildingInstance) | BarAnchor | Health | HealthBarWorld (legacy) |
|--------|-------------------------------|-----------|--------|--------------------------|
| PF_TownCenter | TownCenter_BuildingSO ✅ | Sí | Sí | Sí (opcional desactivar) |
| PF_Granary | Granary_BuildingSO ✅ | Sí | Sí | Sí |
| PF_LumberCamp | LumberCamp_BuildingSO ✅ | Sí | Sí | Sí |
| PF_MiningCamp | MiningCamp_BuildingSO ✅ | Sí | Sí | Sí |
| PF_House | House_SO ✅ | Sí | Sí | Sí |
| PF_Barracks | Barracks_SO ✅ | Sí | Sí | Sí |
| PF_Castillo | Casttle_SO ✅ | Sí | Sí | Sí |
| PF_Buildsite | (asignado en runtime) | N/A | N/A | N/A |

**Correcciones aplicadas:** BuildingSO asignado en TownCenter, Granary, LumberCamp, MiningCamp (antes null).

---

## 3. Referencias rotas

- **Scripts:** No se detectaron `m_Script: {fileID: 0}` en prefabs (ningún componente sin script).
- **BuildingSO:** Todos los edificios tienen ya referencia asignada.
- Los `fileID: 0` en prefabs (m_PrefabInstance, m_CorrespondingSourceObject, m_Father en raíz, etc.) son valores normales de serialización de Unity.

---

## 4. Documentos de referencia creados/actualizados

- **ESTILO_ANNO_MAPA_Y_COLOCACION.md** — Cómo se traduce Anno a nuestro sistema (grid, footprint, ghost, prefabs).
- **AUDITORIA_PREFABS_ESTILO_ANNO.md** — Estado de prefabs, BuildingSO, BarAnchor, checklist en Unity.
- **AUDITORIA_ASSETS_COMPLETA.md** — Este documento.

---

## 5. Revisión manual recomendada en Unity

- **Pivots:** Abrir cada prefab de edificio y comprobar que el pivot del GameObject raíz está en la base del modelo (donde toca el suelo). Ajustar con "Pivot" en el editor de prefab si hace falta.
- **Colliders:** Que el BoxCollider (o el que use) coincida con el footprint en celdas (BuildingSO.size × cellSize).
- **BuildSite:** Comprobar que el visual de la fundación no queda por debajo del suelo en terrenos con pendiente; si ocurre, revisar pivot del PF_Buildsite.

---

*Auditoría de assets completada. Colocación anti-flotar y BuildingSO corregidos; el resto son ajustes visuales en el Editor.*
