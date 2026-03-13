# Sistema de grilla (GridGizmoRenderer)

## Estado actual (post-refactor)

- **Solo hay un sistema de grilla:** **GridGizmoRenderer**. La grilla que estaba integrada en **RTSMapGenerator** fue eliminada para evitar duplicación y centralizar visibilidad (tecla Z, Build Mode) en un solo componente.

---

## GridGizmoRenderer (único sistema)

**Dónde:** Componente independiente en un GameObject (puede ser el mismo del generador o otro). Opcionalmente configurado por **GridVisibility** (Build Mode + BuildingPlacer).

**Cómo funciona:**
- **Datos:** Resuelve el `MapGrid` desde `mapGridSource`, el propio GameObject o `FindFirstObjectByType<MapGrid>()`. Puede usar fallback con `gridSize` y `halfSize` si no hay MapGrid (útil en editor).
- **Scene:** `OnDrawGizmos()` dibuja con Gizmos.
- **Game:** Dos modos en **Game View Render Mode**:
  - **GLLines (por defecto):** Dibuja con `GL.LINES` como el antiguo RTSMapGenerator. Las líneas se ven siempre en ambas direcciones, siguen el terreno si está activo "Segment Lines Follow Terrain", y no sufren culling.
  - **Mesh:** Construye meshes con quads (más eficiente en mapas muy grandes).
- **Opciones:** `showInScene`, `showInGameView`, `showOnlyInBuildMode`, tecla **Z** (toggle), `buildingPlacer`, grosor minor/major, alpha, material override, etc.
- **Límite:** 2048 líneas; para la mayoría de mapas RTS es suficiente.

**Ventajas:**
- Separación de responsabilidades: el generador solo genera; la grilla es un componente de visualización reutilizable.
- Game view más eficiente: mesh estático/batchable.
- Lógica de visibilidad centralizada: "solo en Build Mode" + toggle con Z.
- Funciona aunque el MapGrid esté en otro objeto (referencia por `mapGridSource`).
- Puede mostrar grilla en editor con fallback (gridSize/halfSize) sin haber generado el mapa.

---

## Configuración en escena

- Objeto con **GridGizmoRenderer** (p. ej. el mismo del generador o uno hijo):
  - **Map Grid Source** → GameObject que tiene el `RTSMapGenerator` / `MapGrid`.
  - **Show In Scene** / **Show In Game View** según quieras.
  - Tecla **Z** = toggle de visibilidad; **Show Only In Build Mode** = grilla solo al colocar edificios.
- Opcional: **GridVisibility** en otro objeto para asignar `buildingPlacer` y forzar `showOnlyInBuildMode = true` al inicio.
