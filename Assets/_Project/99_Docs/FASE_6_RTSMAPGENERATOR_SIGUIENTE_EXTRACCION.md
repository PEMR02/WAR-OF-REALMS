# Fase 6 — Adelgazar RTSMapGenerator: siguiente extracción

**Estado:** La extracción del módulo de agua se realizó. `MapWaterMeshGenerator.cs` contiene la generación de malla de agua; `RTSMapGenerator.GenerateWaterMesh()` ahora delega en `MapWaterMeshGenerator.Generate(config, this, Log)`.

## Responsabilidades que siguen dentro de RTSMapGenerator

Resumen de lo que aún hace el script (sin reescribir todo):

| Bloque | Responsabilidad | Líneas aprox. |
|--------|-----------------|----------------|
| Grid / config | ApplyMapPreset, RunDefinitiveGenerate (orquestación), MapGenConfigFactory, MapGenerator, MapGeneratorBridge, MapResourcePlacer | ~150 |
| Agua (visual) | GenerateWaterMesh, GenerateWaterMeshFullPlaneIntersect, GenerateWaterMeshChunks, EnsureWaterVisibleNextFrame, helpers de material (GetMaterialForWaterMesh, GetDefaultWaterMaterial, etc.) | ~350 |
| Terrain height/visual | GetTerrainHeightRange, BakePassability, GenerateHeightmap, RefreshTerrainVisualNextFrame, PaintTerrainByHeight, EnsureTerrainMaterialSupportsLayers | ~250 |
| Spawns / TC | GenerateSpawns (usa MapGenerator), PlaceTownCenters, AlignTownCenterToTerrain, EnsureWorldBarAnchor, ReleaseTownCenterReservations, MoveExistingUnitsToTownCenters, FlattenSpawnAreas | ~500 |
| NavMesh | RebuildNavMeshCoroutine, BuildNavMeshWithSlope, FixUnitsAfterNavMesh | ~200 |
| Debug / Gizmos | DebugGeneratorState, OnDrawGizmos, OnDrawGizmosSelected, OnRenderObject, DrawGridGizmosSegmented, DrawGridGLSegmented | ~150 |

(Total aproximado: ~2000 líneas; los números son orientativos.)

---

## Siguiente módulo ideal a extraer: **agua (malla visual)**

### Por qué conviene este primero

1. **Responsabilidad clara:** Solo genera la malla de agua (vértices, triángulos, MeshFilter, MeshRenderer, material, capa). No modifica el grid ni el terreno; solo lee `MapGrid` (IsWater, cellSize, width, height, origin) y parámetros de agua (waterHeight, offset, material, mode, chunkSize).
2. **Entrada/salida bien definidas:** Entrada: MapGrid listo, waterHeight, waterSurfaceOffset, waterMaterial, waterMeshMode, waterChunkSize, waterLayerOverride, showWater. Salida: un Transform raíz con hijos (GameObjects con mesh) o null si no hay agua.
3. **Reutilizable:** Otros sistemas (ej. editor de mapa, DLC) podrían generar agua desde un grid sin depender de todo RTSMapGenerator.
4. **Tamaño manejable:** ~350 líneas (incluyendo los 4 helpers de material). El resto del generador solo llamaría algo del estilo `MapWaterMeshGenerator.Generate(_grid, waterConfig)` y asignaría el resultado a `_waterRoot`.

### Riesgo de tocarlo

- **Bajo si se hace incremental:** Crear una clase estática o un componente `MapWaterMeshGenerator` que reciba un struct de config (waterHeight, offset, material, mode, chunkSize, layerOverride) y el MapGrid. Copiar tal cual los métodos de generación y los helpers de material; en RTSMapGenerator reemplazar la llamada a `GenerateWaterMesh()` por una llamada al nuevo módulo. No cambiar la lógica de “qué es agua” (eso sigue en BakePassability / MapGrid).
- **Dependencia de materiales:** Los helpers `GetDefaultWaterMaterial`, `GetFallbackWaterMaterialFromQuad`, `GetOrCreatePinkFallback` son estáticos y usan `Resources`, búsqueda de quad en escena, etc. Conviene moverlos al nuevo módulo para que la responsabilidad del agua quede encapsulada; si algún día se cambia el pipeline de render (URP/HDRP), solo se toca ese módulo.
- **EnsureWaterVisibleNextFrame:** Es una corrutina que modifica el Culling Mask de las cámaras. Puede quedarse en RTSMapGenerator (que la invoca tras generar el agua) o moverse al módulo de agua como “post‑paso” opcional; moverla al módulo deja RTSMapGenerator más limpio.

### Pasos sugeridos (sin reescribir todo)

1. Crear `MapWaterMeshGenerator.cs` (o en `MapGenerator/MapWaterMeshGenerator.cs`) con un struct `WaterMeshConfig` (waterHeight, waterSurfaceOffset, material, mode, chunkSize, layerOverride, showWater).
2. Mover a la nueva clase: `GenerateWaterMeshFullPlaneIntersect`, `GenerateWaterMeshChunks`, los 4 métodos estáticos de material y la corrutina `EnsureWaterVisibleNextFrame` (o un equivalente que reciba el layer).
3. Exponer un método estático `Generate(MapGrid grid, WaterMeshConfig config, out Transform waterRoot)` que, si `config.showWater` y hay celdas de agua, cree el árbol de GameObjects y devuelva el root; si no, `waterRoot = null`.
4. En RTSMapGenerator: construir `WaterMeshConfig` desde los campos actuales (waterHeight, waterMaterial, etc.), llamar a `MapWaterMeshGenerator.Generate(_grid, config, out _waterRoot)` y eliminar los métodos movidos.
5. Probar en Play: generar mapa con agua, comprobar que lagos/ríos se ven igual y que la capa del agua sigue en el Culling Mask.

---

## Después de agua: siguiente candidato

- **Town center placement:** PlaceTownCenters, AlignTownCenterToTerrain, EnsureWorldBarAnchor, ReleaseTownCenterReservations (y opcionalmente MoveExistingUnitsToTownCenters) forman un bloque coherente. Depende de spawns, BuildingSO, prefabs y Terrain; es más acoplado que el agua, por eso se recomienda después.
