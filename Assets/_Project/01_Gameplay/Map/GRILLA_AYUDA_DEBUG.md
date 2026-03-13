# Grilla: problema "no queda a nivel del terreno en todas sus partes"

## Descripción del problema

La grilla se ve bien en parte del terreno (p. ej. zona verde/hierba) pero en otras zonas (p. ej. arena/tierra) aparece muy tenue, a rayas o no se ve. Es decir: **no se mantiene a nivel del terreno en todas sus partes** o no es visible en toda la superficie.

## Scripts relacionados

| Script | Rol |
|--------|-----|
| **GridGizmoRenderer.cs** | Dibuja la grilla en vista Scene (Gizmos) y en Game (modo **GLLines** con `GL.LINES` o modo **Mesh** con quads). Obtiene tamaño y origen del MapGrid; obtiene altura del terreno con `Terrain.SampleHeight`. |
| **MapGrid.cs** | Proporciona `origin`, `width`, `height`, `cellSize`, `IsReady`. La grilla se dibuja solo dentro de ese rectángulo. |
| **RTSMapGenerator.cs** | Crea/inicializa el `MapGrid` y asigna el **Terrain** usado para el mapa. No dibuja la grilla (se eliminó); solo genera el terreno y el grid. |
| **GridVisibility.cs** | Opcional: asigna `showOnlyInBuildMode` y `buildingPlacer` al GridGizmoRenderer en Awake. |

## Flujo de la grilla

1. **GridGizmoRenderer** obtiene datos del grid: `GetGridData()` → `GetResolvedMapGrid()` (desde `mapGridSource` o FindObject).
2. Obtiene el terreno: `GetTerrain()` (campo `terrain`, o `mapGridSource.GetComponent<RTSMapGenerator>().terrain`, o FindObject Terrain).
3. Para cada vértice de la grilla: `SampleY(worldX, worldZ, fallbackY)` → `Terrain.SampleHeight(Vector3(worldX, 0, worldZ))` + `heightOffset`. Unity documenta que `SampleHeight` devuelve la altura en **espacio mundo**.
4. Dibujo en Game: si **Game View Render Mode = GLLines**, se usa `DrawGridGL()` (GL.LINES); si no, se usan meshes con quads.

## Posibles causas si sigue fallando

- **Varios Terrenos:** Si hay más de un `Terrain` en la escena, `GetTerrain()` puede devolver solo uno; la grilla muestrea ese y en otras zonas (otro terreno) la altura sería incorrecta o no se vería bien.
- **Terreno fuera del MapGrid:** Si el mapa visual es más grande que el `MapGrid` (p. ej. terreno extra), la grilla solo se dibuja dentro de `[origin, origin+width*cellSize]` x `[origin, origin+height*cellSize]`.
- **Render order / profundidad:** Si la grilla se dibuja antes que algunas capas del terreno (p. ej. otra textura o detalle), puede quedar tapada. El material de la grilla usa `renderQueue = 2500` y `_ZWrite = 0` para dibujar después y no escribir profundidad.
- **Material / pipeline:** En URP o HDRP, el material por defecto (Hidden/Internal-Colored o Unlit/Color) puede comportarse distinto; conviene asignar **Line Material Override** en el Grid Gizmo Renderer con un material del pipeline que uses.

## Qué revisar en Unity

1. **Grid Gizmo Renderer (Inspector):** `Map Grid Source` apuntando al GameObject que tiene **RTS Map Generator** (y por tanto el MapGrid y el Terrain usado).
2. **Un solo Terrain:** Si hay varios Terrain, asegurarse de que el que usa el generador sea el que debe verse bajo la grilla; si hace falta, exponer en el Inspector qué Terrain usa la grilla.
3. **Height Offset:** Subir un poco (p. ej. 0.1–0.15) si la grilla se hunde o hace z-fight en alguna zona.
4. **Game View Render Mode:** Dejar en **GLLines** para el mismo comportamiento que antes (líneas puras, sin culling).

## Cambios recientes en código

- **SampleY:** Se dejó de sumar `terrain.transform.position.y` al resultado de `SampleHeight`, porque la API de Unity devuelve la altura ya en espacio mundo.
- **Render queue** del material de la grilla en **2500** y **ZWrite = 0** para que se vea por encima del terreno en todas las texturas.

Si tras esto la grilla sigue sin verse bien en algunas partes, este documento y la lista de scripts sirven para pedir ayuda (foros, soporte, etc.) describiendo el mismo problema y componentes.
