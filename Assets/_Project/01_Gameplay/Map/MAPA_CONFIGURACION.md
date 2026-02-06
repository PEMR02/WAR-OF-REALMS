# Configuración del Generador de Mapas RTS

## Problemas Identificados y Soluciones

### Problema 1: Terreno Amarillo y Plano
**Causa**: El heightmap no se está generando correctamente o el Noise Scale es demasiado bajo.

**Solución**:
1. Ajusta el **Noise Scale** a un valor entre `0.03` y `0.08` (actualmente está en 0.01)
2. Aumenta **Noise Octaves** a `4` o `5` para más detalle
3. Ajusta **Height Multiplier** para controlar la altura máxima (prueba con 20-40)

**Valores orientativos** (el rango real del terreno se loguea tras generar; usa eso para Water Height):
- Noise Scale: `0.05`
- Noise Octaves: `4`
- Noise Persistence: `0.5`
- Noise Lacunarity: `2`
- Height Multiplier: `8–30` según escala deseada (no recomendar "a ciegas"; ver consola: min/max world Y).

### Problema 2: Recursos No Aparecen
**Causas Posibles**:
1. Las celdas del grid están todas bloqueadas por pendiente
2. No hay prefabs asignados
3. Los spawns de jugadores no se están generando correctamente

**Solución**:
1. Verifica que **Max Slope** no sea demasiado bajo (prueba con `25-35` grados)
2. Asegúrate de que todos los prefabs estén asignados:
   - Tree Prefab: `PF_Wood`
   - Berry Prefab: `PF_Food`
   - Animal Prefab: `PF_Animal`
   - Gold Prefab: `PF_Gold`
   - Stone Prefab: `PF_Stone`
3. Verifica los logs en la consola para ver cuántas celdas están bloqueadas

### Problema 3: Unidades No Se Mueven
**Causa**: El NavMesh no se está generando correctamente.

**Solución**:
1. Asegúrate de que el **NavMesh Surface** esté asignado
2. Verifica que **Rebuild Nav Mesh On Generate** esté activado
3. El NavMesh necesita geometría válida para construirse - asegúrate de que el terreno tenga colisión

## Configuración Recomendada Paso a Paso

### 1. Configuración del Grid
```
Grid Config: [Asignar tu GridConfig asset]
Width: 256
Height: 256
Center At Origin: ✓
```

### 2. Configuración del Terrain
```
Terrain: [Asignar tu Terrain del scene]
Water Height: -999 (sin agua) o un valor en world units tras ver el log
Water Height Relative: -1 (usar Water Height) o 0.25 para ~25% del rango como agua
Max Slope: 30
Height Multiplier: 8–30 (define el rango Y del terreno)
Noise Scale: 0.05
Noise Octaves: 4
Align Terrain To Grid: ✓
```
**Importante:** Water Height está en **world units** y el rango real depende del heightmap (no solo de Height Multiplier). Tras generar el mapa, en consola se muestra:
- **Rango terreno (world Y): min=X, max=Y** → usa esos valores para elegir Water Height con criterio.
- **Sugerencia ~25% agua: Water Height = Z** → valor sugerido para que ~25% de las celdas sean agua.
- O pon **Water Height Relative = 0.25** (0–1) y el generador calculará la cota automáticamente como 25% del rango terreno.

### 3. Configuración de NavMesh
```
Nav Mesh Surface: [Asignar tu NavMesh Surface]
Rebuild Nav Mesh On Generate: ✓
```

### 4. Configuración de Jugadores
```
Player Count: 2
Spawn Edge Padding: 20
Min Player Distance 2p: 120
Spawn Flat Radius: 8
Max Slope At Spawn: 5
```

### 5. Configuración de Town Center
```
Town Center SO: [Asignar TownCenter_BuildingSO]
Town Center Prefab Override: [O asignar PF_TownCenter aquí]
Tc Clear Radius: 6
```

### 6. Configuración de Prefabs de Recursos
```
Tree Prefab: Assets/_Project/08_Prefabs/Resources/PF_Wood
Berry Prefab: Assets/_Project/08_Prefabs/Resources/PF_Food
Animal Prefab: Assets/_Project/08_Prefabs/Resources/PF_Animal
Gold Prefab: Assets/_Project/08_Prefabs/Resources/PF_Gold
Stone Prefab: Assets/_Project/08_Prefabs/Resources/PF_Stone
```

### 7. Resource Rings (Distancias desde el Town Center)
```
Ring Near: X=6, Y=12
Ring Mid: X=12, Y=20
Ring Far: X=30, Y=50
```

### 8. Resource Counts
```
Near Trees: X=8, Y=12
Mid Trees: X=12, Y=20
Berries: X=6, Y=8
Animals: X=2, Y=4
Gold Safe: X=6, Y=8
Stone Safe: X=4, Y=6
Gold Far: X=8, Y=12
```

### 9. Fairness
```
Min Wood Trees: 40
Min Gold Nodes: 6
Min Stone Nodes: 4
Min Food Value: 8
Max Resource Retries: 5
```

### 10. Debug
```
Draw Gizmos: ✓
```

## Cómo Usar

1. **Presiona Play** en Unity
2. El mapa se generará automáticamente al iniciar
3. Revisa la **Consola** para ver los logs de debug:
   - "=== Iniciando generación de mapa ==="
   - Información sobre spawns generados
   - Información sobre recursos colocados
   - Porcentaje de celdas bloqueadas

## Logs de Debug Importantes

Si ves estos mensajes, hay un problema:

- `"BakePassability: Grid no está listo"` → El grid no se inicializó
- `"X% celdas bloqueadas"` → Si es más del 80%, reduce el Max Slope
- `"No se pudo colocar después de 30 intentos"` → Los spawns no encuentran lugares válidos
- `"No se lograron recursos justos"` → No hay suficiente espacio libre para los recursos

## Troubleshooting

### El terreno sigue viéndose plano
1. Verifica que el Terrain tenga un TerrainData asignado
2. Aumenta el Height Multiplier a 40-50
3. Aumenta el Noise Scale a 0.08-0.1

### Los recursos siguen sin aparecer
1. Reduce Max Slope a 45 grados
2. Verifica en la consola cuántas celdas están bloqueadas
3. Verifica que los prefabs estén correctamente asignados

### El NavMesh no se construye
1. Asegúrate de que el Terrain tenga un Terrain Collider
2. Verifica que el NavMesh Surface esté en el mismo GameObject que el Terrain
3. Intenta hacer Bake manualmente después de generar

### Avisos: "Source mesh does not allow read access" / "invalid vertex data"
Si en consola aparecen avisos de **RuntimeNavMeshBuilder** diciendo que un mesh (ej. UCX_SM_HP_Tree, SM_HP_Tree, SM_Rocks_XX) no permite lectura o tiene datos de vértices inválidos, el NavMesh puede construirse en el Editor pero **fallar en builds** o ignorar esos objetos.

**Causa**: Los FBX/meshes usados por árboles, rocas o edificios (y recogidos por NavMeshSurface) tienen **Read/Write Enabled = false** en la importación.

**Solución** (para los assets que aparecen en el aviso):
1. En el **Project**, localiza el FBX o mesh (ej. `Assets/_Project/08_Prefabs/Resources/Wood/SM_HP_Tree.FBX`, `Free Pack - Rocks Stylized.fbx`).
2. Selecciónalo y en el **Inspector** → pestaña **Model** (o **Rig** si aplica).
3. En **Meshes** (o en la sección raíz del Model Importer), activa **Read/Write Enabled**.
4. Aplica (**Apply**) y regenera el mapa (Play) para que el NavMesh vuelva a construirse.

En este proyecto ya se ha activado Read/Write en los FBX de árbol (SM_HP_Tree, Free Pack - Tree), rocas (Free Pack - Rocks Stylized) y casa base. Si añades nuevos prefabs que estén en las capas que recoge el NavMesh Surface, activa Read/Write también en sus modelos.

## Grilla visual (cuadrícula)

La grilla se dibuja desde el **RTS Map Generator** (sección "Grilla visual").

- **Si la grilla se ve cortada o en parches**: Las líneas se ocluyen tras el terreno (ZTest). Opciones:
  1. **Solución rápida**: Sube **Grid Height Offset** a 0,15–0,35 (por defecto 0,2) o asigna un material con **ZTest Always** en **Grid Line Material Override**.
  2. **Material recomendado**: Crea un material con el shader **Unlit/GridAlwaysOnTop** (carpeta `05_Art/Shaders/GridAlwaysOnTop.shader`) y asígnalo en Grid Line Material Override; la grilla se verá siempre encima del terreno.
  3. **Solución geométrica**: Activa **Grid Segment Follow Terrain**; cada línea se segmenta y sigue el relieve (más vértices, grilla pegada al terreno).

- **Grid Segment Follow Terrain**: Si está activado, cada línea de la grilla se dibuja por tramos con altura muestreada en cada vértice, así la grilla no se entierra en lomas.

## Medidas de la grilla y relación con edificios

Una sola medida define el tamaño de cada celda; los edificios se definen en **cantidad de celdas**. Así la grilla y los edificios siempre coinciden.

### Dónde se configura el tamaño de la grilla (metros por celda)

| Dónde | Qué configurar |
|-------|----------------|
| **GridConfig** (asset) | **Grid Size**: tamaño en unidades de mundo (metros) de un lado de cada celda. Ej: `1` → celda 1×1 m; `2` → celda 2×2 m. |
| **RTS Map Generator** | Asigna el mismo **Grid Config** que usas en el mapa. Al generar, el `MapGrid` recibe ese `gridSize` como `cellSize`. |
| **Building Placer** | Asigna el mismo **Grid Config** (o deja que use **MapGrid** en Play). El snap de colocación usa ese tamaño para que los edificios encajen en la grilla. |

**Fuente de verdad**: el valor está en el asset **GridConfig** → `gridSize`. Tanto el generador de mapa como el `BuildingPlacer` (y en Play el `MapGrid`) deben usar el **mismo GridConfig** para que la grilla visual, el paso de unidades y la colocación de edificios coincidan.

### Cómo se relaciona con el tamaño de los edificios

- **Edificios (BuildingSO)**  
  En cada ScriptableObject de edificio, **Size** es el **tamaño en celdas** (ej. 3×3 = 3 celdas de ancho × 3 de fondo), no en metros.  
  El tamaño en mundo se obtiene así:  
  `ancho_mundo = size.x * cellSize`, `fondo_mundo = size.y * cellSize`  
  (con `cellSize` del `MapGrid` / GridConfig).

- **Flujo recomendado**  
  1. Elige un **GridConfig** con el `gridSize` que quieras (ej. 1 m por celda).  
  2. Asígnalo en **RTS Map Generator** y en **Building Placer**.  
  3. En cada **BuildingSO**, pon **Size** en celdas (ej. casa 2×2, granero 3×3).  
  4. La grilla visual, el snap al colocar y la validación de espacio usan el mismo `cellSize`, así que todo queda alineado.

- **Cambiar el tamaño de la grilla**  
  Cambia **Grid Size** en el **GridConfig**. Regenera el mapa (Play) para que el `MapGrid` tome el nuevo valor. Los edificios siguen definidos en celdas, así que no hace falta cambiar cada BuildingSO; solo el tamaño físico de cada celda.

- **Cambiar el tamaño de un edificio**  
  En el **BuildingSO** del edificio, cambia **Size** (en celdas). Eso define cuántas celdas ocupa; el tamaño en metros se calcula con el `cellSize` actual.

## Agua (por altura)

El sistema de agua tiene **3 capas**: cota en world units, reglas de juego (no walk / no build) y malla visual.

- **Nivel de agua (dato)**: En **RTS Map Generator** → Terrain, **Water Height** (world units). Si el terreno está en Y=0 y la altura máxima es Height Multiplier (ej. 8–20), pon un valor intermedio (ej. 3,5) para que las zonas bajas sean agua. Si no usas agua, deja **Water Height** negativo (ej. -999).

- **Reglas de juego**: Las celdas donde la altura del terreno ≤ Water Height se marcan como agua: **no walk** y **no build** (ya integrado en BakePassability y en PlacementValidator/MapGrid).

- **Representación visual**: Sección **Agua (visual)** en el generador:
  - **Show Water**: activar/desactivar la malla de agua (se regenera al generar el mapa).
  - **Water Surface Offset**: pequeño offset en Y sobre la cota para evitar z-fight con el terreno (ej. 0,05).
  - **Water Material**: material de la malla (ej. MAT_Water, URP Lit o Unlit). Si no asignas, se usa un material por defecto azulado.
  - **Water Chunk Size**: tamaño de chunk en celdas (ej. 32). La malla se genera por chunks para mejor rendimiento.

La malla de agua solo cubre celdas con `isWater = true` (altura < Water Height). Si quieres después **shoreline** (borde playa), se puede añadir celdas `isShore` (vecinas a agua) y pintarlas con una capa de terreno o decals.

**¿El chunk tiene mesh y medidas?** Sí. Cada `WaterChunk_X_Y` es un GameObject con **MeshFilter** (mesh con vértices y triángulos, un quad por celda de agua) y **MeshRenderer**. No tiene MeshCollider (el agua es no caminable por el grid/NavMesh). Tras generar el mapa, en consola se muestra el primer chunk: vértices, triángulos y bounds, para comprobar que la geometría existe.

### No aparece agua / lagos

1. **Water Height está en -999 (por defecto)**  
   Con -999 el agua está desactivada. Pon **Water Height** en **world units**, por ejemplo:
   - Si **Height Multiplier** = 8 → prueba **3** o **4** (zonas bajas = agua).
   - Si **Height Multiplier** = 20 → prueba **6–10**.
   Regenera el mapa (Play) y revisa la consola: debe salir "X celdas de agua".

2. **Sigue en 0 celdas de agua**  
   Sube un poco **Water Height** o baja **Terrain Flatness** para tener más relieve (más depresiones). El agua rellena solo las celdas cuya altura del terreno es **menor** que Water Height.

3. **Show Water**  
   En **Agua (visual)** asegúrate de que **Show Water** esté activado.

4. **Logs dicen "X chunks generados" pero no se ve agua**  
   La malla existe; suele ser visibilidad o material:
   - En la **Hierarchy** (Play) busca el objeto **Water** en la **raíz** de la escena (no bajo RTSMapGenerator); expande y verifica que los **WaterChunk_X_Y** tengan MeshFilter y MeshRenderer.
   - Si no asignaste **Water Material**, el generador usa un material por defecto (Hidden/Internal-Colored o URP Lit). Para mejor aspecto asigna **MAT_Water** (o un material URP) en **Agua (visual) → Water Material**.
   - El agua usa por defecto la capa **0 (Default)** para que la cámara del Game view la dibuje; el objeto Water y sus hijos deben estar **activos**.

5. **Se ve el agua en Scene View pero no en Game View (Play)**  
   - Por defecto el agua está en la capa **0 (Default)** y en la raíz de la escena, así que la Main Camera (Culling Mask = Everything o con Default) debería dibujarla.
   - Revisa **Main Camera**: **Culling Mask** debe incluir la capa del agua (0 = Default) o "Everything".
   - En URP: **Universal Renderer** (asset) → **Filtering** → **Opaque Layer Mask** debe incluir Default (o la capa que uses en **Water Layer Override** si lo cambiaste).

6. **Texto para pegar en ChatGPT si el agua sigue sin verse en Game view**  
   Copia el bloque siguiente y pégalo en ChatGPT (o otro asistente) para que te guíe con el proyecto Unity:

   ```
   Proyecto Unity con URP. Tengo un sistema de agua por altura: se genera una malla (mesh) de quads en runtime bajo RTSMapGenerator → Water → WaterChunk_X_Y. Cada chunk tiene MeshFilter, MeshRenderer, material (MAT_Water, URP Lit), layer = misma que el Terrain, renderingLayerMask = todo, renderQueue = 2001. El agua SE VE en Scene view en Play, pero NO SE VE en Game view (misma sesión Play). El terreno sí se ve en Game view. ¿Qué puede hacer que la cámara del Game view no dibuje el agua? Revisa: Culling Mask de la cámara, URP Renderer Opaque Layer Mask, si la cámara del Game es otra (Cinemachine, etc.), y si hay algo que excluya objetos creados en runtime.
   ```

## Constitución del mundo (grid como fuente única de verdad)

Para que el RTS se sienta coherente (grilla no “chica”, recursos ordenados, edificios que calzan), todo debe depender de **una sola unidad lógica: la celda**.

### Regla de oro
- **1 celda = 1 unidad de gameplay** (en este proyecto: 1 celda = 1 world unit si `cellSize = 1`).
- **Grid: rey.** El terreno, recursos, edificios y agua se alinean al grid; no al revés.

### Orden de generación recomendado (estilo AoE)
1. **Definir el grid** → Width, Height, CellSize, Origin (p. ej. 256×256, cellSize 1).
2. **Generar heightmap en función del grid** → `terrain.size = (width * cellSize, heightMultiplier, height * cellSize)` (ya se hace en este proyecto).
3. **Clasificar celdas** → height, slope, isWater, walkable, buildable, occupied (BakePassability + MapGrid).
4. **Agua** → isWater por altura; mesh visual por celdas.
5. **Recursos** → solo en celdas válidas (no agua, pendiente OK, no ocupada), medido en celdas/anillos.
6. **Edificios** → snap a nodos; footprint en celdas (BuildingPlacer ya usa el grid).

### Recursos
- Colocar recursos **solo en celdas válidas** (no random en world space).
- Anillos por jugador en **celdas** (safe / mid / far). MapResourcePlacer ya usa el grid y celdas.

### Edificios
- Cursor snapea a **nodos**; el edificio ocupa **celdas** (footprint).
- Validar todas las celdas del footprint antes de colocar; marcar occupied.

### Grilla visual
- Es **debug/build mode**; no define gameplay. Activar/desactivar con hotkey; grosor bajo, alpha baja.

### Prompt para reestructurar (Cursor / asistente)
```
Reestructura mi RTS para que todo el gameplay esté basado en un GridSystem como fuente única de verdad:
- Grid rectangular (ej. 256×256) con cellSize = 1 world unit.
- El Terrain debe alinearse exactamente al grid (terrain.size = gridSize * cellSize).
- Todo (recursos, edificios, pathfinding, agua) debe operar en coordenadas de celda, nunca en world random.
- WorldToCell / CellToWorld; BuildingFootprint por celdas; recursos por celdas válidas.
- La grilla visual es solo debug/build-mode.
- No introducir valores mágicos ni hardcode de world units fuera del grid.
```

## Próximos Pasos

Una vez que el generador funcione:
1. Ajusta los valores de recursos para balance
2. Experimenta con diferentes Noise Scales para terrenos variados
3. Ajusta las distancias de los anillos para cambiar la distribución de recursos
