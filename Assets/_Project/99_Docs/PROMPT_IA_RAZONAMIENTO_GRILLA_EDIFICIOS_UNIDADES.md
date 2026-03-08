# Prompt para IA de razonamiento: Grilla, edificios y recorrido de unidades

## Contexto

Juego RTS en Unity. Hay un **MapGrid** (grilla lógica), **edificios** que ocupan celdas, y **unidades** (aldeanos, etc.) que se mueven con **A\*** sobre la grilla y **NavMeshAgent** para el movimiento real. El problema es que **las unidades quedan muy alejadas de los edificios** (tanto al construir como al moverse cerca) y las **rutas a veces son raras** (rodeos innecesarios). Algo ha mejorado con cambios recientes pero no está resuelto del todo. Se pide analizar la **relación entre tamaño de grilla, tamaño de edificios y recorrido de las unidades** para proponer una solución coherente.

---

## Descripción del sistema (resumen técnico)

### 1. Grilla (MapGrid)

- **cellSize**: metros por celda (ej. `2.5f`). Viene de `GridConfig.gridSize` al inicializar el mapa.
- **WorldToCell(world)**: `Floor((world.x - origin.x) / cellSize)` para X; igual para Z usando `origin.z`.
- **CellToWorld(cell)**: devuelve el **centro** de la celda: `origin + (cell.x + 0.5, 0, cell.y + 0.5) * cellSize`.
- **IsCellFree(c)**: no bloqueada y no ocupada.
- **SetOccupiedRect(min, size, true)**: marca un rectángulo de celdas como ocupado (por edificios).

### 2. Edificios

- **BuildingSO.size**: tamaño en **celdas** (ej. `Vector2(5, 3)` = 5×3 celdas). No está en metros.
- **Huella en mundo** = `size.x * cellSize` × `size.y * cellSize` (metros).
- **BuildingInstance**: en `OccupyCellsOnStart()` hace `center = WorldToCell(transform.position)` y `SetOccupiedRect(min, size, true)` con ese `size` en celdas.
- **Colocación**: el edificio se coloca con su **transform.position** en mundo; la ocupación usa ese mismo position para calcular el `center` de celdas. Es decir, el pivote del edificio determina qué celdas se marcan.
- **Collider y NavMeshObstacle**: en runtime, `BuildingController` intenta ajustarlos al **tamaño visual** (bounds del Renderer) o, si no hay, a la huella en mundo (`size * cellSize`). Los prefabs pueden tener escalas distintas (ej. modelo 6×3×6 en escala pero huella 6×6 celdas).

### 3. Pathfinding (A\*)

- **Pathfinder.FindPath(worldStart, worldGoal)**:
  - Convierte inicio y destino a celdas: `start = WorldToCell(worldStart)`, `goal = WorldToCell(worldGoal)`.
  - Si **goal está ocupado** (edificio): busca la **celda transitable más cercana** con `FindNearestWalkableCell(goal, 6, canSwim)` (radio máximo 6 celdas).
  - La ruta es una lista de **celdas**; se convierten a mundo con **CellToWorld(cell)** (centro de cada celda) como waypoints.
- Las unidades siguen esos waypoints con **NavMeshAgent**; al llegar a cada uno (remainingDistance ≤ stoppingDistance + margen) pasan al siguiente.

### 4. Construcción (Builder)

- **buildRangeCells**: rango en celdas para poder construir (ej. 1 = una celda desde el borde de la huella).
- **GetBuildSiteClosestPoint(site, unitPos)**: devuelve el punto **sobre el borde** del rectángulo de la huella (buildingSO.size × cellSize) más cercano a la unidad. Ese punto puede estar **dentro de una celda ocupada** por el edificio.
- **GetBuildSiteApproachPoint(site, unitPos, rangeCells)**: devuelve un punto **fuera** de la huella (borde + offset en dirección a la unidad), para que el destino de movimiento esté en celda libre.
- El Builder envía a la unidad a **approachPoint** (no al punto sobre el borde), y comprueba “¿puedo construir?” con la distancia al **borde** (closest point).

### 5. Movimiento (UnitMover)

- **MoveTo(worldPos)**: si hay grilla, usa A\*; el destino `worldPos` se convierte a celda; si esa celda está ocupada, A\* usa FindNearestWalkableCell.
- **Waypoints** = centros de celdas de la ruta A\*.
- **NavMeshAgent.stoppingDistance**: al construir se baja a 0.2 para acercar; si no, valor por defecto del agente.

---

## Síntomas del problema

1. **Unidades muy alejadas de los edificios**: en juego se ven a 3–4, 4–5 o más celdas de distancia del edificio (tanto en construcción como al pasar cerca).
2. **Rutas raras**: las unidades dan rodeos innecesarios; parece que evitan una zona más grande que el edificio visible.
3. **Inconsistencia visual/lógica**: algunos edificios tienen **escala de modelo** (Transform.scale) distinta de la **huella en celdas** (buildingSO.size). Ej.: Barracks escala 5×2.5×3 pero huella 5×3 celdas; con cellSize 2.5 la huella en mundo es 12.5×7.5 m, mayor que el modelo.

---

## Relaciones críticas que debes analizar

1. **cellSize vs buildingSO.size**  
   - La huella en mundo es `size * cellSize`.  
   - Si cellSize cambia (ej. 1 vs 2.5), el mismo edificio “ocupa” más o menos metros y el mismo número de celdas.  
   - ¿El pivote del edificio y el rectángulo de ocupación están alineados con la misma convención (centro, esquina)?

2. **WorldToCell / CellToWorld y borde del edificio**  
   - Un punto **en el borde** del rectángulo de la huella (en metros) puede caer en una celda **ocupada** (porque el rectángulo ocupado es por celdas enteras).  
   - FindNearestWalkableCell(goal, 6) toma el **centro** del edificio como goal cuando el destino mundo está sobre la huella; la “celda libre más cercana” puede estar a varios pasos de ese centro en edificios grandes.

3. **Collider / NavMeshObstacle vs grilla**  
   - El NavMesh (y los obstáculos) usan geometría en mundo (colliders, NavMeshObstacle).  
   - La grilla solo sabe “ocupado/libre” por celdas.  
   - Si el collider/obstacle es más grande (o más pequeño) que la huella `size * cellSize`, las unidades pueden esquivar más (o menos) de lo que la grilla indica.

4. **Destino de movimiento vs “punto útil”**  
   - Para construir, el “punto útil” es el borde del edificio; el “destino de movimiento” debe ser una posición en **celda libre** para que A\* no reemplace el goal por una celda lejana.  
   - Para “ir junto a un edificio” en general, si se pide un destino sobre o dentro de la huella, ocurre lo mismo: goal ocupado → FindNearestWalkableCell → último waypoint lejano.

5. **Waypoints = centros de celdas**  
   - La ruta A\* termina en una celda; el último waypoint es el **centro** de esa celda.  
   - La unidad se detiene cuando está a `stoppingDistance` de ese centro.  
   - Con cellSize 2.5, el centro de la celda “adyacente” al edificio está a ~1.25 m del borde de la celda; según cómo se calcule “adyacente”, la distancia efectiva al edificio puede ser de 1–2 celdas.

---

## Preguntas para la IA de razonamiento

1. **Coherencia grilla–edificios**  
   - ¿Qué convención debería cumplirse entre: (a) posición del edificio (transform), (b) rectángulo ocupado en celdas (min/size), (c) tamaño visual (escala/collider)?  
   - ¿Es correcto que la huella en mundo sea siempre `buildingSO.size.x * cellSize` y `buildingSO.size.y * cellSize` y que el collider/obstacle deban coincidir con eso (o con el modelo, y en ese caso cómo evitar que pathfinding y “distancia al edificio” se desincronicen)?

2. **Pathfinding hacia “cerca del edificio”**  
   - Cuando el destino está en una celda ocupada, FindNearestWalkableCell(goal, 6) puede devolver una celda a varios pasos.  
   - ¿Es mejor (a) que el “cliente” (Builder, órdenes de movimiento) **nunca** pida un destino en celda ocupada y siempre pase un “approach point” en celda libre, o (b) cambiar la lógica del Pathfinder (por ejemplo, buscar la celda libre más cercana al **borde** del edificio en lugar de al centro del goal)?

3. **Tamaño de grilla y sensación de “lejos”**  
   - Con cellSize grande (ej. 2.5), una “celda de distancia” son 2.5 m. ¿Qué valor de buildRangeCells y qué política de “approach point” (offset desde el borde) hacen que las unidades se sientan “pegadas” al edificio sin entrar en la huella?

4. **Recomendación unificada**  
   - Propón una **regla única** (o un conjunto mínimo de reglas) que relacione: cellSize, buildingSO.size, colocación del edificio, ocupación de celdas, tamaño de collider/NavMeshObstacle, y cómo se calcula el “destino de movimiento” cuando la intención es “quedar junto a un edificio” o “construir”. El objetivo es que las unidades no queden sistemáticamente muy lejos y que las rutas no den rodeos innecesarios.

---

## Archivos relevantes (referencia)

- `Assets/_Project/01_Gameplay/Map/MapGrid.cs` – grilla, cellSize, WorldToCell, CellToWorld, ocupación.
- `Assets/_Project/01_Gameplay/Map/GridConfig.cs` – gridSize (origen de cellSize).
- `Assets/_Project/01_Gameplay/Pathfinding/Pathfinder.cs` – A\*, FindNearestWalkableCell cuando goal ocupado.
- `Assets/_Project/01_Gameplay/Units/UnitMover.cs` – MoveTo, waypoints desde A\*, NavMeshAgent.
- `Assets/_Project/01_Gameplay/Building/Construction/Builder.cs` – buildRangeCells, GetBuildSiteClosestPoint, GetBuildSiteApproachPoint.
- `Assets/_Project/01_Gameplay/Buildings/BuildingInstance.cs` – OccupyCellsOnStart (size en celdas).
- `Assets/_Project/01_Gameplay/Building/BuildingController.cs` – ajuste de BoxCollider y NavMeshObstacle (visual o huella).
- `Assets/_Project/04_Data/BuildingSO.cs` – size en celdas.

---

*Documento generado para análisis por IA de razonamiento. Objetivo: identificar causas raíz y proponer diseño coherente entre grilla, edificios y recorrido de unidades.*
