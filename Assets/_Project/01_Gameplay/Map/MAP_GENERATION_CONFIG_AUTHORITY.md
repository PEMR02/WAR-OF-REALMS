# Autoridad de configuración: generación de mapa (RTS)

## Hidrología (ríos / lagos): dónde manda qué

1. **Fuente final del generador**: `MatchConfig` runtime → `water.riverCount` / `water.lakeCount` / `water.maxLakeCells` → `MatchConfigCompiler.ApplyMatchToMapGen` → `MapGenConfig`.
2. **Modo alpha**: esos valores salen de `hydrology` tras `HighLevelMatchSynthesizer`, **salvo** que en `RTSMapGenerator` tengas **`preferSceneHydrologyOverrides`** activado (por defecto **sí**): entonces los campos **Agua (preset / generador definitivo)** del componente (`riverCount`, `lakeCount`, `maxLakeCells`) **pisan** la copia runtime antes de compilar (útil para iterar en escena). Desactívalo para mandar solo el asset / solo alpha.
3. **Sin `MatchConfig` en escena**: se usa `CreateLegacyMatchConfig()`: mandan los mismos campos del `RTSMapGenerator`.

Los biomas futuros deberían leer **solo** datos ya resueltos (`MatchRuntimeState`, `RuntimeMapGenerationSettings` o `MapGenConfig` compilado), no duplicar ríos/lagos en otro sitio.

## Modo Alpha (`useHighLevelAlphaConfig`)

- **Autoridad única**: `layout`, `terrainShape`, `hydrology`, `regionClassification`, `resourceDistribution`, `playerSpawn`, `visualBinding` (+ `climate` / `startingLoadout` / perfiles en inspector alpha).
- **`HighLevelMatchSynthesizer`** escribe en `map`, `geography`, `water`, `resources`, `players` solo como **espejo** para el pipeline existente; no deben editarse a mano con alpha activo.
- En el inspector, un **CustomEditor** oculta los legacy como campos principales y los muestra **solo lectura** al final.
- `ResolveMatchConfig` y `MatchConfigCompiler.Build` llaman a la síntesis (idempotente) para que no haya desfase Inspector ↔ compilación.

## Flujo actual (objetivo cumplido)

1. Validar / resolver `MatchConfig` (`ResolveMatchConfig`: asset o copia runtime desde campos legacy del `RTSMapGenerator`).
2. **`MatchConfigCompiler.Build`** → **`RuntimeMapGenerationSettings`** (`CompiledMapGen`, `Resources`, seed y agua resueltos).
3. Opcional: **`ApplyLegacyResourceFallbackFromScene`** si faltan prefabs en `MatchConfig.resources.visuals` (warning + flag).
4. **`ApplyAuthoritativeGridLayout`** fija grid en mundo.
5. **`MapGenerator.Generate`** usa solo el `MapGenConfig` compilado.
6. **`MapResourcePlacer`** recibe **`ResourceRuntimeSettings`** inyectado (no lee prefabs “de verdad” desde el generador de escena salvo fallback explícito).

### Pipeline generador (MapGenerator)

- Tras agua: **campo de distancia al agua** (Fase3b) para spawns/recursos.
- Tras carve: **clasificación semántica** (Fase7c) → `grid.SemanticRegions` usada por **ResourceGenerator** (sesgo por región) si `resourceDistribution.useTerrainDrivenPlacement`.
- **CityGenerator** usa distancia al agua + llanura media (`alpha*` en `MapGenConfig`, rellenados por compilador en alpha).
- **MapVisualBinder** solo registra perfiles declarados; mallas siguen en `WaterMeshBuilder` / `TerrainExporter` (desacople visual progresivo).

## Dónde vive qué

| Área | Clase principal | Notas |
|------|-------------------|--------|
| Gameplay / macro (tamaño mapa, seed, jugadores, recuentos, anillos, agua canónica, clima serializable) | **`MatchConfig`** (+ structs anidados: `MapSettings`, `WaterSettings`, `ClimateSettings`, `PlayerSettings`, `ResourceSettings`, `GraphicsProfileSettings`, etc.) | **`water.baseHeightNormalized`** es la única autoridad para `waterHeight01` del generador definitivo. `water.waterHeightRelative` en [0,1] en asset: **ignorado** para ese fin (warning en compilador). |
| Tuning técnico avanzado (ríos MS, post-proceso, etc.) | **`MapGenerationProfile`** → `technicalTemplate` (`MapGenConfig`) | Opcional; si falta, plantilla en escena `definitiveMapGenConfig` (deprecated, warning) o `MapGenConfigFactory.CreateFrom`. |
| Snapshot único pre-generación | **`RuntimeMapGenerationSettings`** | Producido solo por el compilador. |
| Prefabs / clustering visual de recursos en runtime | **`ResourceRuntimeSettings`** | Rellenado desde `MatchConfig.resources` (+ fallback escena). |
| Ejecución en escena | **`RTSMapGenerator`** | Terreno, NavMesh, referencias, debug, **orquestación**. Campos duplicados del Inspector = **legacy** cuando existe `MatchConfig`; se sincronizan con `ApplyMatchConfigToLegacyFields` / `CreateLegacyMatchConfig` para no romper escenas viejas. |

## Legacy / deprecated

- **`definitiveMapGenConfig`** en la escena: plantilla técnica si no hay `MatchConfig.mapGenerationProfile` (warning).
- **Presets de mapa** (`MapPresetType`, `ApplyMapPreset`): aislados; **`useLegacyMapPresets`** por defecto **false**; si `mapPreset != Custom` con presets desactivados → warning.
- **`RTSMapGenerator`** campos de recursos/agua/recuentos: espejo para escenas sin asset; la fuente de verdad deseada es **`MatchConfig`**.
- **`MatchConfig.water.waterHeight` / `waterHeightRelative`**: pasabilidad u otros flujos viejos; no definen `waterHeight01` del definitivo.

## TODO / segunda fase

- Presets temáticos por responsabilidad (Biome / Water / Topology / Tech / Match) sin mezclar con `MapGenConfig` completo.
- Reducir aún más campos serializados en `RTSMapGenerator` cuando todas las escenas migren a `MatchConfig` + perfil técnico.
