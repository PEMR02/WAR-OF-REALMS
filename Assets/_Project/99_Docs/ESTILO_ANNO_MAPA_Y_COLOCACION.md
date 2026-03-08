# Estilo Anno — Mapa y colocación (WAR OF REALMS)

Referencia para que el mapa y la colocación de edificios/unidades se acerquen al estilo **Anno** (grid claro, edificios apoyados, sin flotar).

---

## 1. Cómo funciona Anno (referencia)

- **Grid explícito:** Todas las construcciones se alinean a una cuadrícula. Verde = válido, rojo = inválido (obstrucción, terreno, espacio).
- **Terreno válido:** Solo se puede construir donde el terreno lo permite (pendiente limitada, sin agua). Los edificios **nunca flotan** ni se hunden en laderas.
- **Altura por footprint:** La base del edificio sigue el terreno en toda su huella (centro + esquinas); en Anno a menudo se usa una base “aplanada” o una altura promedio para que el edificio quede estable visualmente.
- **Rotación:** 90° (Q/E en nuestro proyecto). Algunos edificios en Anno no rotan (minas, costeros).
- **Unidades:** Se mueven por el mapa; al parar, quedan apoyadas en el suelo (pivot en los pies).

---

## 2. Requisitos en WAR OF REALMS

| Requisito | Implementación actual |
|-----------|------------------------|
| Grid único (celda = N m) | `GridConfig` + `MapGrid.cellSize`; snap en `BuildingPlacer` y `GridSnapUtil`. |
| Altura por footprint (no solo pivot) | `FootprintTerrainSampler` (centro + 4 esquinas); altura final = promedio. |
| Rechazar pendientes fuertes | `TerrainPlacementValidator`: `maxHeightDelta`, `maxSlopeDegrees`. |
| No construir sobre agua | `PlacementValidator`: si MapGrid tiene celdas de agua en el footprint, rechaza (estilo Anno). |
| Ghost verde/rojo | `GhostPreview` según validez (ocupación + terreno + recursos + aldeanos). |
| Edificio final apoyado | `BuildingPlacer` coloca BuildSite/edificio con `avgHeight`; `AlignBuildSiteToTerrain` con altura objetivo. |
| Pivot en la base del edificio | **Prefabs:** pivot del modelo en la base; si no, el offset `_ghostPivotToBottom` compensa para el ghost; el edificio colocado usa la misma lógica. |
| Unidades con pivot en pies | **Prefabs:** revisar que el pivot del modelo esté en la base del personaje. |

---

## 3. Ajustes recomendados en Inspector (BuildingPlacer)

- **maxHeightDelta:** 1.5–2 m para estilo más “plano” tipo Anno; 2–3 m si quieres más relieve.
- **maxSlopeDegrees:** 12–18° para rechazar laderas pronunciadas (Anno suele ser bastante plano donde se construye).
- **gridSize:** Debe coincidir con `GridConfig` y con el tamaño visual de los edificios (ej. 2.5 m por celda).

---

## 4. Prefabs (evitar flotar)

- **Edificios:** Pivot del GameObject raíz en la **base** del modelo (donde toca el suelo). BarAnchor como hijo, encima del techo. Collider según footprint en celdas (no más grande que el edificio).
- **BuildSite:** Mismo criterio; el prefab de fundación debe tener la base visual alineada con el pivot para que `AlignBuildSiteToTerrain` no desplace de más.
- **Unidades:** Pivot en los pies; NavMeshAgent y collider coherentes con la base.

---

## 5. Mapa (generación tipo Anno)

- Zonas construibles: relieve no demasiado abrupto (el generador ya usa `maxSlope`, `terrainFlatness`).
- Agua: celdas marcadas como agua no construibles (MapGrid + PlacementValidator).
- Town Center y recursos iniciales en zonas válidas (spawns ya consideran pendiente y agua en el generador).

---

*Documento de referencia estilo Anno para mapa y colocación. Revisar prefabs y BuildingPlacer según esta guía.*
