# Análisis sistemático: grilla no a nivel del terreno en toda la superficie

**Problema:** La grilla se ve bien en algunas zonas y en otras aparece tenue, incompleta, cortada, a rayas o no sigue la superficie.

---

## 1. Terrain incorrecto resuelto por GetTerrain()

| Estado | **Probable** (si hay varios Terrain) |
|--------|--------------------------------------|

**Descartado si:** Solo hay un `Terrain` en la escena y `mapGridSource` apunta al GameObject que tiene `RTSMapGenerator` (y ese tiene `terrain` asignado).

**Código actual:** GetTerrain() usa: (1) campo `terrain`, (2) caché, (3) RTSMapGenerator del propio objeto, (4) RTSMapGenerator de `mapGridSource`, (5) RTSMapGenerator del MapGrid, (6) `FindFirstObjectByType<Terrain>()`. Si hay varios Terrain, (6) puede devolver cualquiera.

**Cómo comprobar:** En Play, en consola o con un botón de debug: `var t = GetComponent<GridGizmoRenderer>()?.; Debug.Log("Terrain usado: " + (t.GetTerrain()?.name ?? "null") + " | RTSMapGenerator.terrain: " + (mapGridSource?.GetComponent<RTSMapGenerator>()?.terrain?.name ?? "null"));` Comparar que sea el mismo objeto.

**Corrección:** Hacer obligatoria la referencia en Inspector: si `mapGridSource` está asignado, usar **solo** `mapGridSource.GetComponent<RTSMapGenerator>()?.terrain` (y no caer en FindObject). Opcional: campo `Terrain terrain` obligatorio cuando hay más de un Terrain.

---

## 2. SampleHeight en zonas fuera del Terrain real

| Estado | **Probable** en bordes |
|--------|-------------------------|

**Descartado en centro:** Si el grid está dentro de los bounds del Terrain, SampleHeight es válido.

**Código actual:** No se comprueba si (worldX, worldZ) está dentro de `terrain.terrainData.bounds` (en mundo: `terrain.transform.position` + `terrainData.size` en XZ). Unity documenta que SampleHeight **clampa** el punto a los límites del terreno; en el borde la altura puede ser la del borde, no la real visual.

**Cómo comprobar:** Calcular bounds mundo del Terrain: `var b = new Bounds(terrain.transform.position + terrain.terrainData.size * 0.5f, terrain.terrainData.size);` (ajustar según pivot). Comparar con `origin` y `origin + new Vector3(w*cellSize, 0, h*cellSize)`. Comprobar si el rectángulo del grid se sale por algún lado.

**Corrección:** Antes de dibujar, clampear los vértices del grid al rectángulo XZ del Terrain (o no dibujar segmentos fuera de bounds). Opcional: debug visual de los bounds del Terrain y del Grid (Gizmos o líneas).

---

## 3. MapGrid más pequeño o distinto al Terrain visual

| Estado | **Descartado** si generador y grid comparten origen |
|--------|-----------------------------------------------------|

**Código:** MapGrid se inicializa en MapGeneratorBridge con `grid.Origin` y `grid.CellSizeWorld`. TerrainExporter hace `terrain.transform.position = config.origin` y `data.size = (grid.Width * grid.CellSizeWorld, H, grid.Height * grid.CellSizeWorld)`. Mismo config → mismo origen y tamaño lógico.

**Cómo comprobar:** En Play: `Debug.Log($"Grid: origin={grid.origin}, w={grid.width}, h={grid.height}, cellSize={grid.cellSize} => XZ=[{grid.origin.x},{grid.origin.x+grid.width*grid.cellSize}] x [{grid.origin.z},{grid.origin.z+grid.height*grid.cellSize}]");` y para el Terrain: `Debug.Log($"Terrain: pos={terrain.transform.position}, size={terrain.terrainData.size}");` Deben coincidir en XZ.

**Corrección:** Si no coinciden, revisar que MapGenConfig.origin y el origin con el que se llama a MapGrid.Initialize sean el mismo (MapGeneratorBridge).

---

## 4. Desfase entre macrogrid y gameplay grid

| Estado | **Descartado** para la grilla visual |
|--------|--------------------------------------|

**Código:** GridGizmoRenderer usa solo MapGrid (GetGridData): origin, width, height, cellSize. No usa GridConfig ni MapGenConfig directamente. MapGrid se rellena desde el generador definitivo con el mismo GridSystem.Origin y CellSizeWorld.

**Cómo comprobar:** Ver que MapGrid.cellSize y MapGrid.origin coincidan con lo que usa el generador (log de Initialize en MapGrid o en MapGeneratorBridge).

**Corrección:** No necesaria si la inicialización del MapGrid es única y desde el mismo config que el terreno.

---

## 5. Height offset insuficiente

| Estado | **Probable** (z-fighting / visibilidad) |
|--------|----------------------------------------|

**Código actual:** heightOffset por defecto 0.08; se suma a SampleHeight en SampleY.

**Cómo comprobar:** En Inspector subir a 0.12–0.20. Si la grilla deja de “desaparecer” o verse a rayas en algunas zonas, era z-fighting.

**Corrección:** Aumentar heightOffset (p. ej. 0.10–0.15 por defecto) o exponer “min height offset” y usarlo en SampleY.

---

## 6. Z-fighting por material / depth

| Estado | **Parcialmente cubierto** |
|--------|----------------------------|

**Código actual:** ApplyCullOff pone `_ZWrite = 0` y `renderQueue = 2500`. Eso reduce z-fighting y dibuja después del terreno.

**Cómo comprobar:** Comprobar en Frame Debugger que el material de la grilla tenga ZWrite off y queue 2500. Ver si hay otro pass del terreno o detalle que dibuje después (queue > 2500).

**Corrección:** Si algo dibuja después, subir la queue de la grilla (p. ej. 3000) o asegurar que el terreno no use queue mayor que 2500 en ningún pass.

---

## 7. Material no compatible con pipeline (URP/HDRP)

| Estado | **Probable** si el pipeline es URP/HDRP |
|--------|----------------------------------------|

**Código actual:** CreateLineMaterial usa `Hidden/Internal-Colored`, luego `Sprites/Default`, luego `Unlit/Color`. En URP/HDRP, Internal-Colored puede no existir o comportarse distinto.

**Cómo comprobar:** Ver en Runtime qué shader tiene el material de la grilla (`Debug.Log(_lineMat?.shader?.name)`). Si es “Hidden/Internal-Colored” en URP, probar asignar en Inspector un **Line Material Override** con shader URP Unlit (o equivalente).

**Corrección:** En URP, intentar primero `Shader.Find("Universal Render Pipeline/Unlit")` o equivalente y usarlo en CreateLineMaterial cuando el pipeline sea URP.

---

## 8. GL.LINES con problemas de profundidad en algunas zonas

| Estado | **Probable** según ángulo y depth |
|--------|-----------------------------------|

**Código actual:** DrawGridGL usa el material con SetPass(0); el material tiene ZWrite off y queue 2500. GL no escribe depth; la visibilidad depende del depth test con lo ya dibujado.

**Cómo comprobar:** Cambiar a **Game View Render Mode = Mesh**. Si con Mesh la grilla se ve bien en todas las zonas, el problema es específico de GL (orden/depth). Si también falla con Mesh, no es solo GL.

**Corrección:** Si solo falla en GL: forzar que el material use depth test LEqual y queue alta; o dibujar la grilla en un RenderFeature/cámara posterior. Si falla en ambos, priorizar causas 2, 5, 7.

---

## 9. fallbackY usado en algunos vértices

| Estado | **Probable** si Terrain es null o terrainData null |
|--------|-----------------------------------------------------|

**Código actual:** SampleY devuelve `fallbackY + heightOffset` cuando `t == null || t.terrainData == null`. fallbackY es `origin.y` (del MapGrid). Si el terreno está por debajo de origin.y, esos segmentos quedarían altos.

**Cómo comprobar:** Contador estático en SampleY: cuando se usa fallback, incrementar y cada N frames `Debug.Log("SampleY fallback count: " + count)". Si en Play el contador > 0, hay vértices sin terreno.

**Corrección:** Asegurar que GetTerrain() no sea null y que terrainData esté asignado. Si el grid puede estar fuera del Terrain, usar Terrain.SampleHeight solo cuando el punto esté dentro de bounds; fuera, interpolar o usar fallback solo ahí y loguear.

---

## 10. Terreno con desniveles muy abruptos

| Estado | **Posible** (visual, no de lógica) |
|--------|-------------------------------------|

**Código actual:** Cada segmento une dos SampleY; si el relieve es muy abrupto, la línea “corta” el aire entre dos alturas.

**Cómo comprobar:** Inspeccionar en escena zonas donde la grilla “se hunde” o “vuela”; ver si coinciden con pendientes muy fuertes.

**Corrección:** Opcional: subdividir segmentos cuando la diferencia de altura entre extremos supere un umbral (más vértices por segmento).

---

## 11. Orden de dibujo

| Estado | **Parcialmente cubierto** (queue 2500) |
|--------|----------------------------------------|

**Código actual:** OnRenderObject se llama según el orden de los scripts; el material tiene renderQueue 2500.

**Cómo comprobar:** Frame Debugger: ver el orden de los passes y si la grilla se dibuja después del terreno y antes de UI.

**Corrección:** Subir renderQueue si algo (terreno detalle, partículas, etc.) dibuja después y tapa la grilla.

---

## 12. Varios render systems interfiriendo

| Estado | **Descartado** para RTSMapGenerator |
|--------|-------------------------------------|

**Código:** RTSMapGenerator ya no contiene OnDrawGizmos/OnRenderObject de grilla (se eliminó). No hay otro GridRenderer en el proyecto que dibuje la misma grilla.

**Cómo comprobar:** Buscar en el proyecto "OnRenderObject" y "DrawGrid" / "grid" en Gizmos; solo debe aparecer GridGizmoRenderer.

**Corrección:** No aplica.

---

## 13. Origen del grid mal posicionado

| Estado | **Descartado** si MapGrid y Terrain comparten config |
|--------|------------------------------------------------------|

**Código:** origin viene de MapGrid; MapGrid se inicializa con grid.Origin (MapGeneratorBridge), y el terreno usa config.origin (TerrainExporter). Mismo config → mismo origen.

**Cómo comprobar:** Log de grid.origin y terrain.transform.position; deben ser iguales en XZ (y típicamente Y=0 para el pivot del terreno).

**Corrección:** Si difieren, unificar el origen en el generador (un solo config.origin para Terrain y MapGrid).

---

## 14. Diferencia entre Terrain position y SampleHeight

| Estado | **Descartado** en GridGizmoRenderer |
|--------|-------------------------------------|

**Código:** SampleY ya no suma `terrain.transform.position.y`; usa solo `t.SampleHeight(worldPos) + heightOffset`. Unity documenta que SampleHeight devuelve altura en mundo.

**Nota:** MapGrid.GetCellHeight todavía hace `+ terrain.transform.position.y`; es otro uso (gameplay), no la grilla visual.

**Corrección:** Ninguna en GridGizmoRenderer.

---

## 15. Densidad de grid excesiva para visibilidad

| Estado | **Posible** (contraste visual) |
|--------|-------------------------------|

Si hay muchas líneas muy finas o con alpha bajo, en algunas texturas se “pierden”.

**Cómo comprobar:** Subir lineAlpha (p. ej. 0.2) y/o minorLineThickness; ver si la grilla se percibe mejor en las zonas problemáticas.

**Corrección:** Ajustar por defecto lineAlpha y grosor, o exponer “contraste mínimo” según tipo de terreno (avanzado).

---

## 16. Color de grilla insuficiente según textura del terreno

| Estado | **Probable** (arena/tierra clara) |
|--------|-----------------------------------|

En terreno claro el blanco con alpha bajo tiene poco contraste.

**Cómo comprobar:** Probar color gris oscuro o lineAlpha más alto en zonas de arena; ver si mejora.

**Corrección:** Opcional: campo “color de grilla” o “alpha en terreno claro” y usar un color/alpha con más contraste cuando se detecte terreno claro (requiere lógica extra).

---

## 17. Cámara / ángulo de proyección

| Estado | **Posible** (menos probable que 5–8) |
|--------|-------------------------------------|

Near/far o precisión de depth pueden afectar en ángulos RTS muy rasantes.

**Cómo comprobar:** Cambiar ángulo de cámara; si el problema solo aparece en cierto ángulo, revisar near/far y si la grilla está dentro del frustum y con depth correcto.

**Corrección:** Ajustar near clip o asegurar que el material no haga depth test incorrecto.

---

## 18. Grid solo se dibuja cuando MapGrid está Ready

| Estado | **Descartado** como causa de “zonas” |
|--------|-------------------------------------|

GetGridData devuelve false si !grid.IsReady; entonces no se dibuja nada. No explica que se vea bien en una zona y mal en otra.

**Corrección:** No aplica para visibilidad por zonas.

---

## 19. Error en obtención de MapGrid source

| Estado | **Probable** si mapGridSource está vacío o equivocado |
|--------|-------------------------------------------------------|

Si mapGridSource es null, GetResolvedMapGrid usa GetComponent en el propio objeto o FindFirstObjectByType; puede devolver otro MapGrid (ej. de otra escena/prefab).

**Cómo comprobar:** En Play, log de GetResolvedMapGrid() y de gameObject.name del que tiene ese MapGrid; verificar que sea el del mapa actual.

**Corrección:** En Inspector asignar siempre **Map Grid Source** al GameObject que tiene RTSMapGenerator/MapGrid del mapa jugable.

---

## 20. Debug visual adicional

| Estado | **Recomendado** para diagnosticar |
|--------|-----------------------------------|

**Añadir (resumen):**
- Log del Terrain usado (nombre, instanceID) una vez al iniciar.
- Log de bounds del grid (origin, origin + size) y bounds del Terrain en XZ.
- Opcional: SampleHeight en 2 puntos (uno “bueno” y uno “malo”) y log de los valores.
- Gizmos o líneas que dibujen las 4 esquinas del grid en colores (ej. rojo) y el rectángulo del Terrain (ej. verde) en OnDrawGizmos cuando hay un modo “debug bounds”.

---

## Objetivo final: resumen ejecutivo

### Las 3 causas más probables hoy

1. **Terrain incorrecto o SampleHeight fuera de bounds (1 + 2)**  
   Varios Terrain o grid que se sale del Terrain en XZ → GetTerrain() equivocado o SampleHeight clampleado en el borde dando alturas raras.

2. **Z-fighting / depth (5 + 6 + 8)**  
   heightOffset bajo o material/depth en GL haciendo que en algunas zonas la grilla pierda el depth test y se vea tenue o a rayas.

3. **Material / pipeline (7) o contraste (16)**  
   Shader no adecuado para URP/HDRP o alpha/color que en arena/tierra clara no se ve.

### Qué revisar primero

1. **En Inspector:** Map Grid Source asignado al GameObject correcto; Terrain asignado (o dejar que lo resuelva desde mapGridSource). Height Offset a 0.12–0.15.
2. **En Play, una vez:** Log de Terrain usado y de bounds del grid vs Terrain (ver sección 20).
3. **Prueba rápida:** Cambiar a **Game View Render Mode = Mesh**; si mejora, el problema es específico de GL (orden/depth).

### Instrumentación a agregar

- En GridGizmoRenderer, un **modo debug** (bool o [ContextMenu]): al activarlo, en Start o primer OnRenderObject: log del Terrain usado, log de grid origin/size y de terrain position/size XZ, y opcionalmente un contador de cuántas veces SampleY usó fallback.
- Opcional: Gizmos que dibujen el rectángulo del grid y el rectángulo XZ del Terrain en colores distintos cuando debug está activo.

### Cambio mínimo a probar antes de tocar más código

1. **Subir Height Offset** en el objeto Grid Gizmo Renderer a **0.12** o **0.15** y probar en las zonas donde la grilla se pierde.
2. Asignar **Line Material Override** con un material **URP Unlit** (o del pipeline actual), color blanco y alpha ~0.15, y probar de nuevo.
3. Si sigue igual, activar la instrumentación anterior y revisar logs y bounds (y, si se implementa, las esquinas en Gizmos).
