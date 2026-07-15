# Contrato de arquetipos de mapa — Lake First como base

**Estado:** borrador acordado (READ-ONLY / planificación).  
**Fecha:** 2026-07-10  
**Complementa:** `MAP_GENERATION_CONFIG_AUTHORITY.md`

---

## 1. Preset base obligatorio

| Concepto | Valor canónico |
|----------|----------------|
| **Pipeline de agua** | `RuntimeRiverWaterPipelineMode.LakeFirstHydrology` |
| **Flags UWP** | `uwpLakeFirstHydrologyPipeline`, `uwpOwnedVisualPolicy`, `uwpLakeFirstSupplementalEnabled` (vía `UwpLakeFirstPlayPipeline`) |
| **Topología** | Main → lagos (flood) → lake-spill → inland feeder → headwater feeder |
| **Perfil técnico agua** | `RtsHydrologyProfile` + tuning `CleanWaterHydrologyTuning` / `UwpLakeFirstPlayPipeline` |

**Regla:** todo arquetipo de mapa del lobby (Desert, Highlands, Wetlands, etc.) es una variante de **hidrología/recursos/terreno/clima** sobre este pipeline. No se diseñan arquetipos que requieran Legacy, Clean Spline ni HydroGraph V2 como generador principal.

**Excepción:** HydroGraph V2 u otros modos siguen disponibles como **override de depuración** en escena; no forman parte del catálogo de tipos de mapa para jugador.

---

## 2. Arquetipo vs pipeline

```
MapArchetype (QUÉ mapa)     →  MatchConfig alpha + overrides lobby
        ↓
Lake First (CÓMO traza agua) →  WaterGenerator Fase4 + supplemental + Fase9 UWP
        ↓
MapGenConfig compilado       →  Generate()
```

- **Lake First** no se sustituye por preset AoE2 ni por `MapPresets.GetPreset()` legacy.
- Los valores de `MapPresetType` / `MapPresets` solo se reutilizan como **inspiración numérica** tras adaptación (ver §6).

---

## 3. Autoridad por capa (orden de aplicación)

Orden **obligatorio** al pulsar «Empezar partida» o preview definitivo:

| Paso | Fuente | Qué define |
|------|--------|------------|
| 1 | **Arquetipo activo** | Snapshot completo: `hydrology`, `terrainShape`, `resourceDistribution`, `regionClassification`, `climate` (cuando exista mapeo) |
| 2 | **Lobby — layout** | `width`, `height`, `seed`, jugadores, patrón spawn, `mapCellSizeWorld` (3 m fijo) |
| 3 | **Lobby — overrides** | Steppers ríos/lagos/montañas/agua %, tiers oro/piedra/bosque (solo si el jugador movió algo tras elegir arquetipo) |
| 4 | **Escala por tamaño mapa** | Árboles/recursos (ya: `BuildGlobalTreeRangeByMapSize`); hidrología supplemental (ya: `minDim` en builders) — **una sola función de escala**, no fórmulas duplicadas |
| 5 | **`PushSceneToMatchForGeneration`** | Copia 1→4 al `MatchConfig` runtime |
| 6 | **`MatchConfigCompiler.Build`** | `SynthesizeIntoLegacySlots` (si alpha ON) → `ApplyMatchToMapGen` → `ApplyHighLevelToMapGen` |
| 7 | **`ApplyRiverWaterPlayPipelineToConfig`** | Fuerza Lake First (preset base §1) |
| 8 | **`RtsHydrologyProfile.Apply`** | Anchos/carve/play tuning UWP |

### 3.1 Conflicto actual a resolver (antes de implementar arquetipos)

Hoy **`preferSceneHydrologyOverrides=true`** hace que el paso 6 (`MapGenerationRuntimeContext.ApplyToMatch`) **pise** `water.riverCount/lakeCount` con los campos del `RTSMapGenerator`, ignorando el arquetipo si no se sincronizaron los RTS.

**Contrato futuro (elegir una):**

- **A (recomendada):** al aplicar arquetipo, escribir **RTS + MatchConfig.hydrology**; `preferSceneHydrologyOverrides` solo aplica si el jugador tocó steppers después del arquetipo (`lobbyHydrologyDirty`).
- **B:** desactivar `preferSceneHydrologyOverrides` cuando el origen es lobby con arquetipo.

Hasta implementar A o B, **no activar solo `useHighLevelAlphaConfig`** — rompe temas de terreno (Push alpha no copia `terrainFlatness` del RTS).

---

## 4. Arquetipo base: «Lake First Continental» (neutral)

Perfil por defecto al abrir lobby (reemplaza «Alpha Neutral» sin efecto):

| Bloque | Valores orientativos (calibrar en implementación) |
|--------|---------------------------------------------------|
| `hydrology.riversEnabled` | true |
| `hydrology.riverCount` | 4 |
| `hydrology.lakeCount` | 3 |
| `hydrology.lakesEnabled` | true |
| `hydrology.waterBaseHeightNormalized` | ~0.24 |
| `terrainShape.mountainMassCount` | 2 |
| `terrainShape.terrainRoughness` | ~0.5 |
| `resourceDistribution.forestDensity` | Medium |
| Pipeline | Lake First (§1) |

Los steppers del lobby parten de este snapshot; no de deltas acumulativos.

---

## 5. Variantes permitidas (catálogo v1)

| Arquetipo | Agua (intención) | Terreno | Recursos | Notas Lake First |
|-----------|------------------|---------|----------|------------------|
| **Lake First Continental** | 3 ríos, 2 lagos | balance | medio | Base §4 (RTS default) |
| **Highlands** | 2–3 ríos, 1–2 lagos | montañas↑, rough↑ | piedra/oro↑, bosque↓ | Igual topología |
| **Wetlands** | 5–6 ríos, 4 lagos | llano, wet corridor↑ | bosque↑ | Más componentes lake-spill |
| **Drylands** | 0–1 río, 0–1 lago | muy plano | bosque Low/None, sand↑ | **Requiere fix Fase4** (§7) |

No incluir en v1: Archipelago, TeamIslands (topología incompatible con main+lago único).

---

## 6. Legacy `MapPresets` / AoE2

| Preset legacy | Uso en catálogo v1 |
|---------------|-------------------|
| Continental, Forest, Rivers | Referencia numérica → adaptar a Lake First |
| Desert, Arabia | → **Drylands** (con §7) |
| Archipelago, TeamIslands, GoldRush, TeamIslands | **Fuera de alcance** hasta otro pipeline |
| `useLegacyMapPresets` | Permanece **false**; no reactivar en producción |

`map.preset` / `mapType` en `MatchConfig` se mantienen para **guardado/replay**, mapeados al id de arquetipo Lake-First.

---

## 7. Requisitos de pipeline (bloqueantes)

Antes de publicar arquetipo **Drylands** (0 ríos):

1. **`WaterGenerator`:** si `hydrology.riversEnabled == false` o `riverCount == 0` → `riversToPlaceLoop = 0`; no invocar `UwpLakeFirstHydrologyBuilder` ni supplemental (lagos solos opcionales).
2. Hoy Lake First fuerza `riversToPlaceLoop = 1` → **bug respecto al contrato**; debe corregirse.
3. Supplemental (`inland`/`headwater`) solo si existe main y `riverCount >= 2` efectivo post-lake-first.

---

## 8. Clima y apariencia (obligatorio para Desert/Highlands, no opcional)

Arquetipo debe poder fijar, como mínimo:

- `climate.grassPercent`, `dirtPercent`, `rockPercent`, `sandLayer` / shore
- Coherente con `resourceDistribution` y `terrainShape`

`ApplyHighLevelToMapGen` hoy **no** traduce arquetipo → clima; ampliar en implementación (1 punto de extensión, no dispersar en lobby).

---

## 9. Lobby — comportamiento acordado

| Hoy (mal) | Contrato |
|-----------|----------|
| `ApplyTheme` delta acumulativo | `ApplyArchetype(id)` snapshot absoluto |
| Tema guardado en PlayerPrefs, no cargado | Cargar arquetipo + layout al abrir |
| 4 botones sin vínculo a MatchConfig alpha | Botón → arquetipo → RTS + match runtime |
| Pipeline toggle libre | Default **Lake First**; otros modos = debug |

Resumen UI debe mostrar: `Arquetipo: Highlands · Pipeline: Lake First · 320×320`.

---

## 10. Criterios de no-regresión (checklist)

Tras cualquier cambio de arquetipos:

- [ ] Pipeline activo = Lake First (`[UwpLakeFirstPlayPipeline] enabled`).
- [ ] Seed 424242 (Continental): red 3–4 capas, sin skips masivos.
- [ ] Highlands: más macro montaña + más roca, menos agua que Continental.
- [ ] Wetlands: más ríos/lagos colocados que Continental.
- [ ] Drylands: **sin main river** en grid si `riverCount=0`; lagos ≤ configurado.
- [ ] Mapa 384: densidad acordada (documentar si cuenta absoluta o por km²).
- [ ] `preferSceneHydrologyOverrides` / orden compile no pisan arquetipo sin override manual.

---

## 11. Implementación por fases (mínimo riesgo)

1. **Contrato + fix §7** (0 ríos) + fix Push terreno en alpha.  
2. **`MapArchetypeCatalog`** estático (4 entradas §5) + `ApplyArchetype` en lobby.  
3. **Autoridad §3.1** (`lobbyHydrologyDirty` o equivalente).  
4. **Clima §8** por arquetipo.  
5. Bridge opcional `MapPresetType` → id arquetipo (save games).

**No hacer en la misma PR:** reactivar `MapPresets` legacy, cambiar anchos Lake First, ni migrar escenas/prefabs.

---

## 12. Resumen en una línea

**Lake First es el único preset de generación de agua; los «tipos de mapa» del lobby son perfiles declarativos que solo cambian cuánto y qué (agua, relieve, recursos, clima) se piden a ese pipeline.**
