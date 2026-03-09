# Diagnóstico: encaje de edificios en la grilla (WAR OF REALMS)

**Objetivo:** Revisar y corregir la relación entre `BuildingSO.size`, `cellSize`/grid, tamaño real del prefab, pivot, collider y visual para que los edificios encajen bien en las celdas.

**Convención:** `cellSize` = 2.5 m (GridConfig / MapGrid). Tamaño en metros = `size.x * cellSize` (ancho) y `size.y * cellSize` (fondo).

---

## 1. BuildingSO como fuente de verdad del footprint

**Confirmado.** El footprint en celdas viene únicamente de `BuildingSO.size` (Vector2: x = ancho, y = fondo en celdas).

- **BuildingPlacer:** usa `selectedBuilding.size` para snap (`GridSnapUtil.SnapToBuildingGrid(p, origin, gridSize, bw, bh)`), muestreo de terreno (`FootprintTerrainSampler.Sample(..., new Vector2(bw, bh), ...)`), validación (`PlacementValidator.IsValidPlacement(p, selectedBuilding.size, ...)`) y ocupación de celdas (`OccupyCells(position, selectedBuilding.size, true)`).
- **PlacementValidator:** convierte `size` a metros con `MapGrid.Instance.cellSize` para OverlapBox y comprueba `IsWorldAreaFree(pos, size, ...)`.
- **MapGrid:** `WorldToCell` / `CellToWorld` y `SetOccupiedRect` usan celdas; el “centro” del edificio en mundo es el punto que se pasa (posición del pivot del prefab).
- **BuildingController (runtime):** cuando el grid está listo, aplica al BoxCollider y NavMeshObstacle el tamaño `BuildingSO.size.x * cellSize` y `BuildingSO.size.y * cellSize` en metros, convertido a espacio local del transform (dividiendo por `lossyScale`). Opcionalmente limita X/Z al tamaño visual para no superar el mesh.

Conclusión: **BuildingSO.size es la única fuente de verdad para el número de celdas y para el tamaño lógico en metros.** No reescribir el sistema; cualquier incoherencia se resuelve alineando prefabs y, si hace falta, el valor de `BuildingSO.size`.

---

## 2. Uso correcto del footprint en placement X/Z

**Revisado y coherente.**

- **GridSnapUtil.SnapToBuildingGrid:** Dado un punto mundo, calcula la celda “min” del footprint con `minCellX = Round(u - halfW)`, `minCellZ = Round(v - halfH)`, luego el centro del footprint en unidades de celda y devuelve `origin + (centerU * cellSize, y, centerV * cellSize)`. El punto devuelto es el **centro** del rectángulo del edificio en XZ.
- **BuildingPlacer:** asigna esa posición al ghost y al BuildSite; el prefab se instancia con ese punto como posición del **root**. Por tanto el pivot del root debe ser el centro del footprint en XZ.
- **OccupyCells:** `center = WorldToCell(worldPos)`, `min = center - size/2`, `SetOccupiedRect(min, size)` — coherente con “worldPos = centro del edificio”.
- **PlacementValidator:** `halfExtents = (size.x * cellSize * 0.5, yOffset, size.y * cellSize * 0.5)` — asume que `pos` es el centro. Correcto.

Si los edificios “no encajan” en la grilla, la causa no es la fórmula de snap ni el uso de `BuildingSO.size` en X/Z, sino **pivot del prefab, escala/centrado del visual o collider inicial** (hasta que BuildingController lo sobrescribe en runtime).

---

## 3. Auditoría: prefabs y alineación con el footprint lógico

Se asume **cellSize = 2.5 m**.

### 3.1 PF_TownCenter

| Concepto | Valor |
|---------|--------|
| **BuildingSO** | TownCenter_BuildingSO, `size (3, 3)` |
| **Footprint teórico** | 3×3 celdas = **7.5 m × 7.5 m** |
| **Root (prefab)** | Transform: position (0, **2**, 0), scale (7.7, 7.5, 7.5) |
| **Visual** | Mesh en root + hijo “TownCenter2” (nested prefab) scale 150,150,150. Escala final dominada por root (7.7, 7.5, 7.5). |
| **BoxCollider (prefab)** | size (2, 2, 1), center (0, 0.5, 0) — en local; con scale root → ~15.4×15×7.5 m. No coincide con 7.5×7.5. |
| **NavMeshObstacle (prefab)** | Extents (0.5, 0.5, 0.5), Center (0,0,0). |

**Problemas detectados:**

1. **Pivot Y:** Root en (0, 2, 0) indica que el pivot está 2 m por encima de la base; el placement ya compensa con `_ghostPivotToBottom` y BuildingAnchorSolver, pero el prefab no sigue la convención “pivot en la base”.
2. **Collider en prefab:** Tamaño y centro no reflejan el footprint; en runtime BuildingController los sobrescribe con el footprint del BuildingSO, así que en juego puede ser correcto. En editor, el collider del prefab sigue siendo engañoso.
3. **Escala del root (7.7, 7.5, 7.5):** Casi 7.5 en Y/Z; X=7.7 genera ligera asimetría. Si el mesh de origen es 1×1×1, el edificio sería ~7.7×7.5×7.5 m, cercano a 3×3 celdas pero no exactamente centrado en X si el mesh tiene pivot distinto.
4. **Residuos de testing:**  
   - `Health.startWithPercentForTesting`, `startPercent: 50`  
   - `HealthBarWorld.debugLogs: 1`  
   - `BuildingController.debugDrawObstacleBounds: 1`  
   - `DropAnchor` con scale (0,0,0) (posible ocultación intencional).

**Recomendación:**  
- Dejar que el collider/obstacle sigan siendo aplicados en runtime por BuildingController.  
- Opcional: unificar escala del root a (7.5, 7.5, 7.5) y asegurar que el mesh tenga pivot en la base y centrado en XZ; mover root a (0,0,0) y subir el visual como hijo para cumplir “pivot en la base”.  
- Apagar `debugLogs` y `debugDrawObstacleBounds` en el prefab; revisar `startWithPercentForTesting`/`startPercent`.

---

### 3.2 PF_House

| Concepto | Valor |
|---------|--------|
| **BuildingSO** | House_SO, `size (3, 3)` |
| **Footprint teórico** | 3×3 celdas = **7.5 m × 7.5 m** |
| **Root** | Prefab anidado (guid f16c…). Transform del root: position (0,0,0), scale (1,1,1) en el override. |
| **Visual** | Hijo “Casa_01” (nested prefab) con scale (2, 2, 2), rotación Y = -90°. Tamaño visual real depende del mesh; con scale 2 es pequeño respecto a 7.5 m. |
| **BoxCollider (prefab)** | size (1, 1, 1), center (0, 0, 0) — 1 m³ en mundo. Muy por debajo de 7.5×7.5. |

**Problemas detectados:**

1. **Collider en prefab:** 1×1×1 no tiene relación con 3×3 celdas. En runtime BuildingController lo reemplaza por el footprint (7.5×7.5 en metros, en local), así que en juego el bloqueo y la selección pueden ser correctos.
2. **Visual muy pequeño:** Scale (2,2,2) en “Casa_01” hace que el modelo sea mucho menor que 7.5 m; el edificio se verá pequeño dentro de su celda o desalineado con la grilla si se espera que llene el footprint.
3. **Posible desalineación:** Si el mesh de la casa tiene pivot en una esquina, con root en centro de celdas la base no coincidirá con el cuadrado de la grilla.

**Recomendación:**  
- Ajustar **escala del modelo** (hijo visual) para que el bounds en XZ sea ~7.5×7.5 m (o al menos coherente con 3×3 celdas).  
- Mantener root en (0,0,0) y centrado; que el visual sea hijo con pivot en la base.  
- No es necesario tocar BuildingSO.size si se confirma que 3×3 es el diseño deseado; el problema es escala/centrado del asset visual.

---

### 3.3 PF_Barracks

| Concepto | Valor |
|---------|--------|
| **BuildingSO** | Barracks_SO, `size (2, 2)` |
| **Footprint teórico** | 2×2 celdas = **5 m × 5 m** |
| **Root** | Transform: position (0, 0, 0), scale (**5**, **2.5**, **4.5**) |
| **Visual** | Mesh en root + hijo “Barraca_02” (nested) scale (250, 250, 500). Tamaño mundial dominado por root: 5×2.5×4.5 m. |
| **BoxCollider (prefab)** | size (1, 1, 1), center (0, 0, 0) — en local; con scale root → 5×2.5×4.5 m. X=5 correcto, Z=4.5 (falta 0.5 m para 5). |
| **NavMeshObstacle** | Extents (0.5, 0.5, 0.5). |

**Problemas detectados:**

1. **Footprint Z:** 2×2 = 5×5 m; el root tiene scale.z = 4.5, por lo que el visual y el collider (antes de runtime) son 4.5 m de fondo. BuildingController en runtime fuerza el collider a 5×5 según BuildingSO, pero el **modelo** sigue siendo 4.5 m en Z → desalineación visual con la grilla.
2. **Residuos:**  
   - `Health._currentHP: 0`  
   - `HealthBarWorld.debugLogs: 1`  
3. **SpawnPoint** en (0, 0, 5): correcto para salida de unidades.

**Recomendación:**  
- Opción A: Cambiar scale del root a (5, 2.5, **5**) para que el visual y la “sensación” de footprint coincidan con 5×5.  
- Opción B: Si el modelo debe mantener proporciones, ajustar **BuildingSO.size** a algo que en metros dé 5×4.5 (p. ej. 2×1.8 celdas) solo si se acepta que el Barracks ocupe 2×2 celdas pero con profundidad visual 4.5 m; normalmente es mejor escalar el modelo a 5×5.  
- Poner `_currentHP` en maxHP o 1 y `debugLogs` en 0.

---

## 4. Propuesta de estándar único para prefabs de edificios

Objetivo: **un solo criterio** para que placement, grid y visual coincidan sin excepciones.

1. **Root = centro del footprint, base en Y**  
   - Posición local del root: **(0, 0, 0)**.  
   - El punto que coloca BuildingPlacer es el centro XZ y la Y de apoyo (terreno).  
   - La “base” del edificio (donde toca el suelo) debe estar en Y=0 en local del root, o el sistema de placement ya compensa con `_ghostPivotToBottom`; en ese caso, documentar que “pivot = centro XZ, Y = punto de apoyo del BuildingAnchorSolver”.

2. **BuildingSO.size = tamaño en celdas**  
   - Definir en el SO el número de celdas (ej. 2×2, 3×3).  
   - Tamaño en metros = `size.x * cellSize`, `size.y * cellSize` (cellSize = 2.5).

3. **Visual como hijo**  
   - Un hijo (p. ej. “Model” o el nombre del asset) con el mesh; el root solo Transform + lógica (BuildingController, BuildingInstance, etc.).  
   - Ventaja: pivot del root siempre centro; el modelo puede tener su propio pivot (centro o base) y colocarse con localPosition/localScale.

4. **Collider coherente con el footprint**  
   - En **runtime**, BuildingController ya aplica tamaño desde BuildingSO cuando MapGrid está listo.  
   - En **prefab**, opción recomendable: BoxCollider con size en **espacio local** = `(size.x * cellSize / scale.x, altura, size.y * cellSize / scale.z)` y center en la base (p. ej. center.y = halfHeight). Así el prefab en editor ya muestra el cuadro correcto; en Play se mantiene o se refuerza igual.

5. **NavMeshObstacle**  
   - Mismo tamaño y centro que el BoxCollider; BuildingController los sincroniza.  
   - En prefab, valores por defecto (0.5, 0.5, 0.5) están bien; en runtime se sobrescriben.

6. **Sin residuos de testing**  
   - `Health`: no usar `startWithPercentForTesting` en producción; `_currentHP` = maxHP o no exponer.  
   - `HealthBarWorld.debugLogs`: 0.  
   - `BuildingController.debugDrawObstacleBounds`: 0 (activar solo al depurar).

---

## 5. Tabla resumen por edificio

| Edificio | size (celdas) | Tamaño teórico (m) cellSize=2.5 | Problemas prefab | Corrección recomendada |
|----------|----------------|----------------------------------|-------------------|-------------------------|
| **TownCenter** | 3×3 | 7.5×7.5 | Pivot root Y=2; collider prefab no acorde; scale 7.7/7.5/7.5; debug flags | Pivot en base o dejar compensación placement; escala 7.5,7.5,7.5; apagar debug; opcional: collider prefab = footprint |
| **House** | 3×3 | 7.5×7.5 | Visual scale (2,2,2) → modelo muy pequeño; collider 1×1×1 | Subir escala del hijo visual para ~7.5×7.5 en XZ; collider lo aplica runtime |
| **Barracks** | 2×2 | 5×5 | Root scale Z=4.5 (debería 5); Health _currentHP=0; debugLogs | Scale root (5, 2.5, 5); _currentHP=maxHP; debugLogs=0 |

---

## 6. Qué ajustar en cada caso

- **Asset visual:**  
  - House: aumentar escala del modelo para que coincida con 7.5×7.5.  
  - Barracks: aumentar scale.z a 5 (o reescalar modelo para 5×5).

- **Collider:**  
  - En runtime ya se fija con BuildingSO; opcionalmente en prefab dejar un BoxCollider con tamaño/centro coherentes con el estándar para mejor vista en editor.

- **Pivot/root:**  
  - TownCenter: valorar mover pivot a la base (0,0,0) y subir el visual como hijo para claridad; si se mantiene Y=2, dejar documentado y asegurar que BuildingAnchorSolver + _ghostPivotToBottom sigan siendo la referencia.

- **BuildingSO.size:**  
  - No cambiar a menos que se decida que el edificio ocupe otro número de celdas (p. ej. Barracks 2×1.8); lo preferible es mantener 2×2 y ajustar escala del modelo a 5×5.

Prioridad: **coherencia entre footprint lógico (BuildingSO + grid) y tamaño/centrado visual**; luego collider/prefab para editor y por último limpieza de flags de testing.

---

## 7. Orden de corrección recomendado

1. **Barracks** (rápido): scale root Z = 5; quitar `debugLogs` y corregir `_currentHP`. ✅ **Aplicado.**  
2. **House** (visual): escalar hijo “Casa_01” para que bounds XZ ~7.5×7.5; comprobar centrado. ✅ **Aplicado** (scale 2→3.75).  
3. **TownCenter** (opcional): unificar scale a (7.5,7.5,7.5); apagar `debugLogs` y `debugDrawObstacleBounds`; revisar pivot/base según estándar. ✅ **Aplicado** (scale 7.7→7.5; debug off).  
4. Revisar **BuildSite** (prefab de fundación): que su tamaño/centro sigan el mismo criterio (BuildingSO del edificio asociado).  
5. **Validación manual** con la checklist siguiente.

---

## 8. Checklist manual para verificar encaje en escena

Hacer en Unity Editor, con **GridConfig / MapGrid cellSize = 2.5** y escena de juego con terreno y grid visible (p. ej. GridGizmoRenderer o gizmos de celdas).

- [ ] **TownCenter 3×3**  
  - Colocar un TownCenter; comprobar que ocupa exactamente 3×3 celdas (sin solapamiento ni huecos con las líneas de la grilla).  
  - Comprobar que la base del modelo no flota ni se hunde respecto al terreno.  
  - Comprobar que al colocar otro edificio en una celda adyacente no hay solapamiento visual ni de collider.

- [ ] **House 3×3**  
  - Colocar una Casa; comprobar 3×3 celdas y que el modelo no quede muy pequeño ni descentrado dentro del cuadrado.  
  - Comprobar selección por clic en toda el área del edificio (collider en runtime).

- [ ] **Barracks 2×2**  
  - Colocar un Barracks; comprobar 2×2 celdas y que el modelo llene bien el cuadrado (especialmente en Z, 5 m).  
  - Comprobar que el SpawnPoint y el NavMeshObstacle no bloquean erróneamente celdas vecinas.

- [ ] **Rotación 90°**  
  - Colocar cada tipo con Q/E (90°); comprobar que el footprint sigue siendo el mismo rectángulo en celdas y que el visual no se sale del área ocupada.

- [ ] **Ghost vs final**  
  - Durante placement, el ghost debe coincidir con las celdas que quedan ocupadas al confirmar.  
  - Tras colocar, el edificio final (BuildSite y luego edificio completo) debe estar en la misma posición que el ghost.

- [ ] **Residuos**  
  - En Play, comprobar que no aparecen logs de HealthBarWorld ni comportamientos de “testing” (vida al 50 %, etc.).  
  - Opcional: Shift+B para ver bounds de obstáculos y comprobar que coinciden con el footprint.

---

## 9. Notas para validación manual en Unity Editor

- **GridConfig:** `Assets/_Project/01_Gameplay/Map/GridConfig.asset` → `gridSize: 2.5`.  
- **MapGrid en Play:** Se inicializa por el generador de mapa; `cellSize` debe coincidir con GridConfig (revisar RTSMapGenerator / quien inicialice MapGrid).  
- **BuildingSO:**  
  - TownCenter: `TownCenter_BuildingSO.asset` → size (3, 3).  
  - House: `House_SO.asset` → size (3, 3).  
  - Barracks: `Barracks_SO.asset` → size (2, 2).  
- Si el encaje falla solo en una orientación (0° vs 90°), revisar que `FootprintTerrainSampler` y `GridSnapUtil` usen el mismo criterio de ancho/fondo (size.x = ancho, size.y = fondo) y que la rotación en BuildingPlacer sea solo Y.  
- Los colliders del prefab pueden verse “raros” en editor; en Play, BuildingController los actualiza cuando MapGrid está listo. Para ver el resultado final, ejecutar la escena y comprobar con Gizmos (Shift+B) o con un OverlapBox en la posición del edificio.

Documento generado para alinear BuildingSO.size, cellSize/grid, prefabs y placement sin reescribir el sistema completo.

---

## 14. Estándar RTS (footprint mesh separado)

Implementado según estándar AoE/Anno/SC2: **tres capas (footprint lógico, collider, mesh visual)**.

- **Child Footprint:** Objeto auxiliar en el prefab con escala exacta `size × cellSize` para validar encaje sin depender del mesh. Ver **ESTANDAR_FOOTPRINT_RTS_BUILDINGS.md**.
- **BuildingFootprintGizmo:** Componente que dibuja el volumen en Scene View.
- **Menú Unity:** Tools → Project → **Añadir o actualizar Footprint (edificio seleccionado)** y **Añadir Footprint a TownCenter, House, Barracks**.
- PF_TownCenter y PF_Barracks ya tienen el child **Footprint**; PF_House debe usar el menú una vez (prefab anidado).
