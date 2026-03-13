# Encaje de edificios en grilla – Verificación y correcciones

**Fecha:** 2026-03  
**CellSize de referencia:** GridConfig.gridSize = **3** → tamaño teórico mundo = `size.x × cellSize` × `size.y × cellSize`.

---

## 1. Estructura estándar por prefab (convención)

| Elemento | Descripción |
|----------|-------------|
| **Root** | GameObject principal (PF_XXX). Contiene: BuildingInstance, BuildingSelectable, BuildingController, Health, WorldBarSettings, NavMeshObstacle, BoxCollider (jugable), MeshFilter/MeshRenderer (visual en algunos prefabs). |
| **Visual** | Hijo con mesh (o nested prefab). Puede ser el mismo root o un child. |
| **Footprint** | Hijo con nombre `Footprint`, escala local `(sizeX*cellSize, 0.1, sizeZ*cellSize)`, solo BuildingFootprintGizmo (editor). No afecta lógica. |
| **Collider principal** | BoxCollider en root. Tamaño = footprint en mundo `(sizeX*cellSize, 2, sizeZ*cellSize)`, center `(0, 1, 0)` para raycast de selección sobre toda la base. |
| **BarAnchor** | Hijo vacío, nombre `BarAnchor`, posición local `(0, 3, 0)` para barra de vida encima. Tanto **Health.barAnchor** como **WorldBarSettings.barAnchor** deben apuntar a este Transform. |

**Testing:** `startWithPercentForTesting = false`, `_currentHP` y `maxHP` coherentes con BuildingSO (edificio completo = maxHP).

---

## 2. Tabla por edificio

| Edificio | Size (celdas) | Tamaño teórico (mundo) | Residuos detectados | Corrección aplicada | Revisión manual Unity |
|----------|----------------|------------------------|----------------------|----------------------|------------------------|
| **PF_TownCenter** | 6×6 | 18×18 (cellSize=3) | Health maxHP 200 (SO 300); Health.barAnchor null; BoxCollider (2,2,1); BarAnchor en (0,0,0) | maxHP/_currentHP 300; Health.barAnchor → BarAnchor; BoxCollider (18,2,18) center (0,1,0); BarAnchor (0,3,0); Footprint ya 18×18 ✓ | Verificar encaje en escena con grilla visible |
| **PF_House** | 3×3 | 9×9 | Health maxHP 100 (SO 300); Health.barAnchor null; BoxCollider (1,1,1); WorldBarSettings localOffset (0,-2,0) | maxHP/_currentHP 300; Health.barAnchor → BarAnchor; BoxCollider (9,2,9) center (0,1,0); BarAnchor (0,3,0); WorldBarSettings localOffset (0,0,0); Footprint ya 9×9 ✓ | Verificar encaje en escena |
| **PF_Barracks** | 6×6 | 18×18 | Health maxHP 100 (SO 300); Health.barAnchor null; BoxCollider (1,1,1); BarAnchor en (0, 0.62, 0) | maxHP/_currentHP 300; Health.barAnchor → BarAnchor; BoxCollider (18,2,18) center (0,1,0); BarAnchor (0,3,0); Footprint ya 18×18 ✓ | Verificar encaje en escena |

---

## 3. Resumen de cambios

- **Testing:** Ningún prefab tenía `startWithPercentForTesting = true`; se mantiene en false. `_currentHP` igual a `maxHP` y al valor de BuildingSO.
- **Anclas unificadas:** Health.barAnchor y WorldBarSettings.barAnchor apuntan al mismo hijo `BarAnchor`; BarAnchor localPosition (0, 3, 0).
- **Footprint visual:** Coincide con BuildingSO.size × cellSize (3): TownCenter/Barracks 18×18, House 9×9.
- **Collider jugable:** BoxCollider cubre la base del edificio (mismo XZ que footprint, altura 2, center Y=1) para selección por clic.
- **Jugabilidad:** Sin cambios en lógica de colocación, construcción ni selección.

---

## 4. Revisión manual pendiente en Unity

1. Abrir escena de juego con **MapGrid** y **GridConfig** (cellSize = 3).
2. Colocar **PF_TownCenter**, **PF_House** y **PF_Barracks** con el BuildingPlacer.
3. Comprobar que el **Footprint** (gizmo verde) coincide con las celdas de la grilla.
4. Comprobar que el **centro** del edificio queda alineado al centro del footprint (snap correcto).
5. Comprobar que la **barra de vida** aparece encima de cada edificio (BarAnchor a Y=3).
6. Comprobar que **clic en la base** del edificio selecciona correctamente (BoxCollider cubriendo la base).

Si en tu proyecto el **cellSize** es distinto (p. ej. 2.5), ajustar en GridConfig y, si hace falta, la escala del child Footprint y el tamaño del BoxCollider para que sigan la regla: `size_celdas × cellSize`.
