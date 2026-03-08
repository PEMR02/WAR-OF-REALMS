# Fase 1 — Building Placement consolidado (resumen y checklist)

## Qué se corrigió

1. **BuildingAnchorSolver integrado**
   - Antes: no se usaba en el flujo; la altura se ponía directamente con `p.y = sample.avgHeight` y el ghost con `ghostPos.y += _ghostPivotToBottom`.
   - Ahora: en cada frame de colocación (con terreno) se llama una sola vez a `FootprintTerrainSampler.Sample`, luego `TerrainPlacementValidator.IsValid` y `BuildingAnchorSolver.Solve(sample, _ghostPivotToBottom, out placementY, out visualOffsetY)`. Se usa `placementY` para `p.y` (posición de colocación) y `ghostPivotY = placementY + visualOffsetY` para la posición del ghost. Sin terreno se mantiene el fallback (raycast Y + pivotToBottom para el ghost).

2. **Un solo muestreo por frame**
   - El sample del footprint se hace una vez; con él se valida terreno, se obtiene la Y de colocación y la Y del pivot del ghost. Se evita llamar dos veces a `FootprintTerrainSampler.Sample` en el mismo frame.

3. **Ghost y BuildSite coherentes**
   - Ghost: pivot en `ghostPivotY` (base visual en `placementY`).
   - BuildSite: se instancia en `position` con `position.y = placementY`; `AlignBuildSiteToTerrain(go, position.y)` sigue alineando la base visual del site a esa Y (necesario porque el pivot del prefab del site puede no estar en la base).

4. **Sin redundancia AlignBuildSiteToTerrain vs BuildingAnchorSolver**
   - BuildingAnchorSolver define la **altura de referencia** (placementY y offset para el pivot).
   - AlignBuildSiteToTerrain sigue siendo necesario: **mueve el transform del BuildSite** para que la base visual (bounds de renderers/colliders) quede exactamente en `targetBaseY`. No sustituye al solver; lo complementa.

5. **PlacementValidator y MapGrid**
   - Sin cambios. Siguen validando ocupación, OverlapBox y agua (IsWater por celda). La validación de pendiente/delta de altura sigue en TerrainPlacementValidator.

6. **MapGrid.GetCellHeight / GetAreaAverageHeight / GetAreaMinMaxHeight**
   - No se usan en el placement actual porque el footprint debe respetar la **rotación** del edificio (yaw). Esos métodos trabajan por celdas axis-aligned. FootprintTerrainSampler sigue siendo la fuente correcta para placement; MapGrid queda para otros usos (pathfinding, UI, etc.).

---

## Lógica final dominante

| Paso | Responsable | Qué hace |
|------|-------------|----------|
| Snap XZ | BuildingPlacer + GridSnapUtil | Snap a grilla según tamaño del edificio (1x1 vs NxM). |
| Muestreo altura | FootprintTerrainSampler | Centro + 4 esquinas (+ 4 puntos de borde si ≥3 celdas). |
| Validación terreno | TerrainPlacementValidator | heightDelta ≤ maxHeightDelta, pendiente ≤ maxSlopeDegrees. |
| Altura de colocación | BuildingAnchorSolver | placementY = avgHeight; visualOffsetY = pivotToBottom. |
| Posición ghost | BuildingPlacer | position = (p.x, ghostPivotY, p.z) con ghostPivotY = placementY + visualOffsetY. |
| Posición BuildSite | BuildingPlacer | Instantiate en (p.x, placementY, p.z); luego AlignBuildSiteToTerrain(site, placementY). |
| Ocupación / agua | PlacementValidator + MapGrid | IsWorldAreaFree, IsWater por celda, OverlapBox. |

---

## Checklist manual de pruebas en Unity

- [ ] **Ghost**
  - En terreno plano: ghost no flota ni se entierra; base apoyada en el suelo.
  - En ladera suave: ghost sigue el terreno (avgHeight del footprint).
  - En zona muy inclinada o con mucha diferencia de altura: ghost se marca inválido (rojo) y no se puede colocar.
- [ ] **Colocación**
  - Clic válido crea BuildSite en la misma posición Y que el ghost (base del site en placementY).
  - Edificio 3x3 en ladera: base del site no flota; apoyado en el terreno.
- [ ] **Rotación Q/E**
  - Al rotar, el ghost actualiza posición y altura según el nuevo footprint.
- [ ] **Sin terreno**
  - Con Terrain eliminado o no detectado: aviso en consola al intentar colocar; ghost usa altura del raycast + pivotToBottom.
- [ ] **Snap**
  - 1x1 y NxM siguen alineados a la grilla; sin desplazamientos raros.
- [ ] **Agua**
  - No se puede colocar sobre celdas marcadas como agua (PlacementValidator + MapGrid.IsWater).

---

## Riesgos

- **Prefabs con pivot en la base**: `_ghostPivotToBottom` sería 0; BuildingAnchorSolver sigue siendo correcto (placementY + 0).
- **BuildSite prefab con pivot distinto al del edificio final**: AlignBuildSiteToTerrain corrige la Y del site; el edificio final al completarse debería usar la misma lógica (misma placementY o sample en ese punto).

---

## Pendiente (fuera de Fase 1)

- Estrategia configurable (avgHeight vs minHeight) según tamaño del edificio: no implementado; actualmente siempre avgHeight.
- Uso de MapGrid para altura: solo tiene sentido para lógica axis-aligned (ej. pathfinding); no para placement rotado.
