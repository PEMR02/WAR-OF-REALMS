# Generador Definitivo de Mapas RTS — Diseño

## 1. Esquema de clases (UML textual)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ MapGenConfig (ScriptableObject)                                              │
├─────────────────────────────────────────────────────────────────────────────┤
│ + gridW, gridH : int                                                        │
│ + cellSizeWorld : float                                                     │
│ + seed : int                                                                 │
│ + maxRetries : int                                                          │
│ + regionCount, regionNoiseScale : int, float                                │
│ + waterHeight01, riverCount, lakeCount, maxLakeCells : float, int           │
│ + cityCount, minCityDistanceCells, cityRadiusCells, maxCitySlopeDeg : ...   │
│ + roadWidthCells, roadFlattenStrength : int, float                          │
│ + resourceRanges, rings, fairness : (struct/Serializable)                   │
│ + terrainTextureThresholds, blendWidth : float[]                            │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│ CellType (enum)     │ Land, Water, River, Mountain                          │
│ ResourceType (enum) │ None, Wood, Stone, Gold, Food                         │
├─────────────────────────────────────────────────────────────────────────────┤
│ CellData (struct)                                                            │
│ + height01 : float   + type : CellType     + regionId, biomeId, cityId : int│
│ + slopeDeg : float   + walkable, buildable, occupied : bool                  │
│ + resourceType : ResourceType   + roadLevel : byte (0=none, 1=trail, 2=main) │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│ IRng (interface)                                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│ + NextInt(min, max) : int   + NextFloat() : float   + State/Seed para debug │
└─────────────────────────────────────────────────────────────────────────────┘
        ▲
        │ implements
┌───────┴─────────────────────────────────────────────────────────────────────┐
│ XorShiftRng                                                                  │
│ + Constructor(seed)   + NextInt, NextFloat                                   │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│ GridSystem                                                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│ + Width, Height : int   + CellSizeWorld : float   + Origin : Vector3         │
│ + Cells : CellData[,]   (acceso por [cx, cy])                                │
│ + WorldToCell(world) : Vector2Int   + CellToWorldCenter(cx, cz) : Vector3    │
│ + WorldToNode(world) : Vector2Int   + NodeToWorld(nx, nz) : Vector3         │
│ + InBoundsCell(cx, cz) : bool   + GetCell(cx, cz) : ref CellData             │
│ + Neighbors4(cell), Neighbors8(cell) : IEnumerable<Vector2Int>                 │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│ MapGenerator (orquestador)                                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│ + Generate(config, terrain) : bool                                           │
│ - RunPhase0() .. RunPhase10()                                                │
│ - CreateRng(), CreateGrid(), ValidateAndRetry()                              │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│ RegionGenerator    │ GenerateRegions(grid, config, rng)                     │
│ WaterGenerator     │ GenerateWater(grid, config, rng)                       │
│ HeightGenerator    │ GenerateHeights(grid, config, rng)                      │
│ CityGenerator      │ GenerateCities(grid, config, rng) : List<CityNode>      │
│ RoadNetworkGenerator │ BuildRoads(grid, cities, config) : List<Road>         │
│ TerrainCarver      │ ApplyCityFlatten(grid, cities, config)                   │
│                    │ ApplyRoadFlatten(grid, roads, config)                   │
│ ResourceGenerator  │ PlaceResources(grid, cities, config, rng)               │
│ TerrainExporter    │ ApplyToTerrain(terrain, grid, config)                   │
│ WaterMeshBuilder   │ BuildWaterMeshes(grid, config, material) : GameObject  │
│ MapValidator       │ Validate(grid, cities, config, out reason) : bool       │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│ CityNode (struct/class)   │ Id, Center (Vector2Int), RadiusCells            │
│ Road (struct/class)       │ FromCityId, ToCityId, List<Vector2Int> pathCells  │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Pseudocódigo del pipeline (orden exacto)

```
GENERATE(config, terrain):
  for retry = 0 .. config.maxRetries:
    rng = new XorShiftRng(config.seed + retry)
    grid = new GridSystem(config.gridW, config.gridH, config.cellSizeWorld)

    Fase0_Init(rng, grid, config)
    Fase1_GridBase(grid, config)           // inicializar Cells con valores por defecto
    Fase2_Regions(grid, config, rng)      // regionId, biomeId
    Fase3_Water(grid, config, rng)        // type Water/River, ríos + lagos
    Fase4_Heights(grid, config, rng)     // height01 coherente, agua plana, slopeDeg
    Fase5_Cities(grid, config, rng)       // CityNodes, cityId, buildable
    Fase6_Roads(grid, cities, config)      // MST + A*/BFS por grid, roadLevel
    Fase7_Carve(grid, cities, roads, config)  // aplanar ciudades y caminos en height01
    Fase8_Resources(grid, cities, config, rng)
    Fase9_TerrainExport(terrain, grid, config)
    Fase9_WaterMesh(terrain/grid, config)  // mesh agua por chunks
    Fase10_GameplayExport(config)         // hook NavMesh, datos para BuildSystem

    if MapValidator.Validate(grid, cities, config, out reason):
      return true
    else:
      Log("Validación fallida: " + reason)
  return false
```

**Fase 0:** Seed fijado; RNG creado; log "Fase0 listo, seed=X".

**Fase 1:** Asignar CellData por defecto (Land, height01=0, walkable=true, etc.); log "Fase1: grid WxH inicializado".

**Fase 2:** Ruido/regiones → regionId, biomeId por celda; log "Fase2: N regiones, M biomas".

**Fase 3:** Generar ríos (paths en grid) y lagos (flood fill con límite); marcar CellType Water/River; log "Fase3: X% agua, Y celdas río, Z lagos".

**Fase 4:** height01 desde regiones + ruido; forzar celdas Water/River a waterHeight01; calcular slopeDeg desde vecinos; log "Fase4: heights listos, agua plana".

**Fase 5:** Elegir centros de ciudad (planos, buildable, lejos de agua); CityNode por ciudad; marcar cityId y buildable; log "Fase5: N ciudades colocadas".

**Fase 6:** Grafo ciudades → MST; entre cada par del MST, ruta en grid (A* o BFS evitando agua/montaña); marcar roadLevel; log "Fase6: K caminos, L celdas camino".

**Fase 7:** Por cada ciudad: aplanar height01 en radio; por cada camino: suavizar height01 en roadWidth; log "Fase7: carve aplicado".

**Fase 8:** Por ciudad/spawn, rings cerca/medio/lejos; colocar recursos en celdas válidas; marcar resourceType; log "Fase8: Wood=X, Stone=Y, Gold=Z, Food=W".

**Fase 9:** TerrainData.SetHeights (grid → heightmapResolution); SetAlphamaps por biome; WaterMeshBuilder por celdas Water/River; log "Fase9: Terrain + agua exportados".

**Fase 10:** Evento/hook para NavMesh bake y entrega de datos; log "Fase10: gameplay export listo".

---

## 3. Parámetros por clase (resumen)

| Clase / SO        | Parámetros principales |
|-------------------|--------------------------|
| **MapGenConfig**  | gridW, gridH, cellSizeWorld, seed, maxRetries; regionCount, regionNoiseScale; waterHeight01, riverCount, lakeCount, maxLakeCells; cityCount, minCityDistanceCells, cityRadiusCells, maxCitySlopeDeg; roadWidthCells, roadFlattenStrength; recursos (rangos, rings, fairness); terrain (thresholds, blend). |
| **GridSystem**    | Width, Height, CellSizeWorld, Origin (Vector3). Cells[,] se crea en constructor. |
| **RegionGenerator** | Usa config.regionCount, regionNoiseScale; escribe regionId, biomeId. |
| **WaterGenerator**  | waterHeight01, riverCount, lakeCount, maxLakeCells; escribe CellType Water/River. |
| **HeightGenerator**| regionId/biome + noise; waterHeight01 para agua; calcula slopeDeg. |
| **CityGenerator**   | cityCount, minCityDistanceCells, cityRadiusCells, maxCitySlopeDeg; criterios buildable, no agua. |
| **RoadNetworkGenerator** | roadWidthCells; MST sobre ciudades; pathfinding en grid (coste por tipo celda). |
| **TerrainCarver**   | cityRadiusCells, roadWidthCells, roadFlattenStrength; modifica height01. |
| **ResourceGenerator** | Rangos por tipo, rings (near/mid/far), exclusión, min por jugador/ciudad. |
| **TerrainExporter** | heightmapResolution, size; grid → heights; biomas → alphamaps. |
| **WaterMeshBuilder**| chunkSize (celdas), waterMaterial; quads por celda Water/River. |
| **MapValidator**   | Comprueba: ciudades conectadas, no ciudad en agua, % agua < máx, recursos mínimos, área plana. |

---

## 4. Checklist de integración con tu proyecto

### Dónde llamar a Generate()
- En un **MonoBehaviour** de bootstrap (ej. `GameBootstrap` o `MapGenRunner`): en `Start()` o desde un **botón/hotkey** de debug.
- Ejemplo: `var gen = GetComponent<MapGenerator>(); gen.Generate(mapGenConfig, terrain);`
- No incluye UI: tú añades un botón en Inspector que llame `MapGenerator.Generate(config, terrain)` o un `[ContextMenu("Generate Map")]` para probar desde el componente.

### Qué datos conectar
- **MapGenConfig:** Crear un ScriptableObject desde Assets → Create → Map Generator → MapGenConfig; asignar en el campo `MapGenConfig` del `MapGenerator`.
- **Terrain:** Asignar la referencia al Terrain de la escena (puede ser null; entonces Fase9 solo prepara datos o no escribe).
- **Prefabs / materiales:** El generador no referencia prefabs de edificios ni de recursos; solo escribe `CellData` (cityId, resourceType, etc.). Quien coloque objetos en el mundo (tu BuildSystem o un ResourcePlacer) debe leer el grid y los nodos. Para **agua visual:** pasar un Material al `WaterMeshBuilder` (o al orquestador que lo llame); el SO puede tener un campo `Material waterMaterial` opcional.

### Cómo testear en escena
1. Crear un GameObject con `MapGenerator` y asignar `MapGenConfig` y `Terrain`.
2. Añadir un script de prueba con `[ContextMenu("Generate")]` o `void Update() { if (Input.GetKeyDown(KeyCode.F5)) generator.Generate(config, terrain); }`.
3. Play; ejecutar Generate (menú contexto o F5); revisar consola por fases y validación.

### Logs obligatorios para debug
- **Fase 0:** Seed usado (y retry si aplica).
- **Fase 1:** "Grid WxH inicializado".
- **Fase 2:** Número de regiones/biomas o "Fase2 Regiones listo".
- **Fase 3:** % celdas agua, número de ríos/lagos, tiempo aproximado.
- **Fase 4:** "Fase4 Heights listo", agua plana aplicada.
- **Fase 5:** Número de ciudades colocadas, tiempo.
- **Fase 6:** Número de caminos, celdas de camino, tiempo.
- **Fase 7:** "Fase7 Carve aplicado".
- **Fase 8:** Recursos colocados por tipo (Wood, Stone, Gold, Food).
- **Fase 9:** "Terrain exportado", "Water mesh: N chunks".
- **Fase 10:** "Gameplay export listo".
- **Validación:** "Validación OK" o "Validación fallida: &lt;reason&gt;".
- Cada fase puede loguear `Time.realtimeSinceStartup` al inicio/fin para tiempo por fase.

### Hook points (sin asumir tus scripts)
- **OnGenerationComplete:** Al terminar `Generate()` con éxito, se invoca `MapGenerator.OnGenerationComplete(grid, cities, roads, config)`. Suscríbete para:
  - Sincronizar tu `MapGrid` desde `GridSystem` (water, blocked por tipo celda).
  - Colocar Town Centers en `cities[i].Center` (CellToWorldCenter).
  - Colocar prefabs de recursos donde `cell.resourceType != None`.
  - Llamar al bake de NavMesh.
- **MapGeneratorBridge (opcional):** MonoBehaviour que se suscribe a `OnGenerationComplete` y llama a `SyncGridToMapGrid(grid, mapGrid)` para copiar water/blocked al `MapGrid` existente. Asigna `mapGridOverride` o usa `MapGrid.Instance`. La colocación de TCs y recursos la haces tú en otro script que también escuche el evento.
- **Datos tras Generate():** `mapGenerator.Grid`, `mapGenerator.Cities`, `mapGenerator.Roads` (válidos solo si `Generate()` retornó true).
- **BuildSystem / Pathfinding:** Leen `GridSystem` (buildable, walkable, roadLevel); no se referencian desde el generador.

---

## 5. Nota para Cursor / iteración

- **No generar todo de una vez.** Primero: skeleton compilable + orden del pipeline. Luego iterar fase por fase.
- **Cada fase debe poder ejecutarse sola** para debug (por ejemplo, llamando desde MapGenerator a una fase concreta o desde tests).
- **Logs obligatorios por fase:** tiempo, % agua, # ciudades, # caminos, recursos colocados.

---

## 6. Reemplazo / uso junto a RTSMapGenerator

- **Opción A – Todo desde RTSMapGenerator:** En el Inspector del **RTS Map Generator** activa **Use Definitive Generator**. Así, al hacer Generate (Play o botón), se ejecuta el pipeline del Generador Definitivo (terreno, agua, ciudades, caminos, recursos en grid), se sincroniza MapGrid, se colocan Town Centers en las ciudades, se instancian los prefabs de recursos desde `CellData.resourceType` y se hace bake del NavMesh. Puedes dejar **Definitive Map Gen Config** vacío: se crea un config en runtime desde los campos del RTS (width, height, seed, playerCount, rings, etc.).
- **Opción B – MapGenConfig propio:** Asigna un ScriptableObject **MapGenConfig** en **Definitive Map Gen Config** para controlar ciudades, ríos, lagos, rings, etc. sin tocar los campos del RTS.
- **Opción B – Solo Generador Definitivo:** En la escena pon un GameObject con **MapGenerator** (y opcionalmente **MapGeneratorBridge**). Asigna config y terrain y llama `MapGenerator.Generate()`. La colocación de TCs y recursos la haces tú (evento OnGenerationComplete o leyendo Grid/Cities después).

---

## 7. Checklist para que Cursor NO se pierda

Cuando le pases el prompt o pidas cambios, añade esto al final:

1. **"No me generes todo de una. Entrégame primero el skeleton compilable + orden del pipeline. Luego iteramos fase por fase."**
2. **"Cada fase debe poder ejecutarse sola para debug."**
3. **"Logs obligatorios por fase: tiempo, % agua, # ciudades, # caminos, recursos colocados."**
