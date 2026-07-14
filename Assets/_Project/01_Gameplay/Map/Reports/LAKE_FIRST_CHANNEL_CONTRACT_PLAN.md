# Plan: contrato de canal Lake First (Headwater → todos)

**Estado:** en ejecución  
**Fecha:** 2026-07-13  
**Decisión de producto:** unificar main / inland / lake-spill / lake-tributary al contrato visual que ya funciona en **HeadwaterFeeder**.  
**No es un rewrite del generador.** Es normalizar física de canal y dejar knobs de ancho.

Complementa:
- `Docs/AUDIT_MOTOR_HIDROLOGIA_RTS.md`
- `graphify-out/PIPELINE_OFICIAL_COMPARATIVA.md`
- `Reports/headwater-feeder-carve-fix-notes.txt`
- `MAP_ARCHETYPE_CONTRACT.md`

---

## 1. Meta de producto (definición de “terminado”)

En Play (SampleScene, Lake First, perfil UWP ON):

| # | Criterio | OK cuando… |
|---|----------|------------|
| M1 | Main: centro azul | Sin franja blanca de punta a punta |
| M2 | Main: solo borde blanco fino | Orilla = intersección mesh↔terreno |
| M3 | Main: recorrido | Se conserva el meandro/ruta actual (no rehacer path) |
| M4 | Uniones | Y headwater, inland→main, spill→lago se conservan |
| M5 | Headwater | No regresa (referencia dorada) |
| M6 | Inland / lake-spill | Mismo look de canal; solo cambia ancho |

Si un cambio no mueve M1–M6, no cuenta como progreso.

---

## 2. Contrato único de canal (regla de oro)

Copiado del Headwater estabilizado:

1. **Profundidad constante** → `uniformFlatChannelFloor` (bandeja plana).  
   No campana / no “profundidad orgánica”.
2. **Canal = mesh + carve juntos.**  
   Lo orgánico es el **ancho a lo largo**, no encoger el carve bajo el mesh.
3. **Mesh ligeramente > carve** (`MeshOverCarveMul ≈ 1.3` → carve ≈ mesh×0.77).  
   Eso produce el borde blanco fino.
4. **Carve debe cubrir el interior del mesh** (stamps euclídeos / sin huecos).  
   Si no: islas o zigzag blanco.
5. **Uniones y recorrido** no se reescriben en este plan; solo se alinean anchos al contrato.
6. **Knobs por tipo:** anchos generales (main / trib / inland / spill). Casi nada más.

```
Centerline (ya OK)
    ↓
Half-widths mesh (ancho visual)
    ↓
Mask/carve half = mesh × foamRatio   ← margen blanco constante
    ↓
Raster máscara + stamps floor plano bajo la huella del mesh
    ↓
Agua: azul en centro, blanco solo orilla
```

---

## 3. Pipeline oficial (no cambiar)

```
RTSMapGenerator
  → CleanWaterPipelineOrchestrator / UwpLakeFirstPlayPipeline (modo 4)
  → RtsHydrologyProfile.Apply
  → MapGenerator (paquete UWP)
       ├─ WaterGenerator / LakeFirst     … colocación + centerlines
       ├─ RiverSurfaceMeshBuilder        … mesh + MaskHalfWidthsWorld
       ├─ WaterMeshBuilder               … lagos MS
       └─ TerrainExporter                … carve heightmap + shore/sand
```

Autoridad de anchos/carve: `RtsHydrologyProfile` + este contrato.  
No introducir otro pipeline (Spline / HydroGraphV2) como default.

---

## 4. Fases (inicio → fin)

### Fase 0 — Baseline / quitar violaciones
**Objetivo:** dejar de pelear el contrato.

| Acción | Detalle |
|--------|---------|
| 0.1 | Quitar perfil campana en main (`flatFloor` otra vez en main frozen) |
| 0.2 | No inventar profundidad variable; floor plano |
| 0.3 | Mantener path stamp euclídeo frozen (compatible con Headwater bajo `bankFall>0`) |
| 0.4 | No tocar lógica Headwater ya OK |

**Validación:** Headwater sigue OK; main no peor que “bandeja plana”.

### Fase 1 — Main al contrato Headwater
**Objetivo:** M1 + M2.

| Acción | Archivo |
|--------|---------|
| 1.1 | Tras `ApplyMainMeshOnlyWidthScale`, aplicar `CarveToMeshGrowthProfile` al main Lake First (mismo ratio 1.3) | `RiverSurfaceMeshBuilder` |
| 1.2 | Stamps main: floor plano + `maxDistWorld` cubriendo huella mesh | `TerrainExporter` |
| 1.3 | Bed main: profundidad **constante** suficiente (sin campana) | `TerrainExporter` |
| 1.4 | Log audit: `mode=… flatFloor=1 meshOverCarve=1.3` | ambos |

**Validación Play:** foto main — azul centro, blanco solo orilla.

### Fase 2 — Inland + lake-spill / lake-tributary
**Objetivo:** M6 sin romper uniones.

| Acción | Detalle |
|--------|---------|
| 2.1 | Tras ensanches/tapers existentes, aplicar el **mismo** growth profile mesh→carve |
| 2.2 | Sustituir o complementar `ScaleLakeFirstTributaryCarveMaskHalfWidths(0.9)` por ratio contrato |
| 2.3 | Conservar: mouth flare lago, join widen inland/spill→main, headwater Y |
| 2.4 | Solo ajustar anchos base en perfil si hace falta |

**Validación:** 2–3 seeds; inland y spill con borde fino; uniones intactas.

### Fase 3 — Anchos de producto
**Objetivo:** look RTS sin tocar física.

| Knob | Dónde | Notas |
|------|-------|-------|
| Ancho main | `riverVisualRibbonFullWidthCellsMain` + `meshMul` | Subir/bajar canal completo |
| Ancho trib | `riverVisualRibbonFullWidthCellsTributary` | |
| `MeshOverCarveMul` | constante compartida | Solo si el borde blanco es grueso/fino |
| Bed depth constante | floor unificado | Solo si el centro sigue blanco |

### Fase 4 — Limpieza (después de verde visual)
- Eliminar ramas muertas “solo main campana / BFS jagged / excepciones contradictorias”.
- Actualizar `headwater-feeder-carve-fix-notes.txt` → “contrato Lake First canal”.
- Un log `[LakeFirstChannelContract]` por río en audit.

---

## 5. Archivos tocados (presupuesto)

| Fase | Archivos | Límite |
|------|----------|--------|
| 0–1 | `TerrainExporter.cs`, `RiverSurfaceMeshBuilder.cs`, este plan | ≤3 código |
| 2 | mismos + opcional `RtsHydrologyProfile.cs` | pedir OK si >3 |
| 4 | docs + limpieza | sin cambiar escenas/prefabs |

**Prohibido en este plan:** escenas, prefabs, SO, renombres públicos, nuevo framework.

---

## 6. Protocolo anti-caos (productor + IA)

1. Una meta por sesión (ej. solo M1).  
2. Play + captura antes del siguiente cambio.  
3. Si no mejora en 1 Play → revert de ese cambio.  
4. Headwater es regresión bloqueante: si se rompe, stop.  
5. No apilar “otro experimento” encima de un rojo.

---

## 7. Checklist de validación Unity

- [ ] SampleScene, `riverWaterPlayPipeline=4`, `applyUwpHydrologyProfile=1`
- [ ] Stop → Play (mapa nuevo)
- [ ] Log: contrato flatFloor + mesh/carve ratio en main
- [ ] Main: centro azul, borde blanco fino
- [ ] Headwater: continuo, Y limpia (como antes)
- [ ] Inland / spill: sin islas; uniones OK
- [ ] Minimapa / NavMesh no peores (smoke)

---

## 8. Riesgos

| Riesgo | Mitigación |
|--------|------------|
| Main muy ancho → stair sand | Contrato + softener; no reabrir campana |
| Stamp-only deja huecos | `maxDistWorld` = huella mesh; densify 0.28 |
| Lip/sand re-blanquean | No re-levantar lecho excavado; softener frozen |
| Scope creep meandro | Fuera de este plan (recorrido ya OK) |

---

## 9. Registro de ejecución

| Fecha | Fase | Resultado |
|-------|------|-----------|
| 2026-07-13 | Plan escrito | `LAKE_FIRST_CHANNEL_CONTRACT_PLAN.md` |
| 2026-07-13 | Fase 0–1 | `TerrainExporter`: flatFloor + stamps Lake First para todos; log `lakeFirstChannelContract`. `RiverSurfaceMeshBuilder`: growth profile main. |
| 2026-07-13 | Fase 2 | Inland/lake-spill usan el mismo growth profile (ya no scale 0.9 suelto). |
| 2026-07-13 | Fix trib→main | Ingress ya no recorre el main (tuck corto). Trim lake-spill habilitado. Mesh join spill ensanchado para orilla blanca. |


| — | Fase 4 | Pendiente: limpieza ramas muertas + actualizar notas headwater |

### Cambios de código (esta entrega)

- `Packages/.../TerrainExporter.cs` — contrato canal stamps/floor
- `Packages/.../RiverSurfaceMeshBuilder.cs` — mesh→carve ratio compartido
- `Assets/_Project/01_Gameplay/Map/Reports/LAKE_FIRST_CHANNEL_CONTRACT_PLAN.md` — este plan

---

## 10. Decisión explícita

**Sí:** mismo contrato Headwater para todos los tipos de río Lake First.  
**Sí:** solo anchos generales como diferencia de producto.  
**No:** profundidad campana, pipelines alternos, ni rehacer uniones/recorrido main.
