# Tutorial operativo — sistema de aguas Lake First (UWP)

**Estado:** estable con seed de referencia `424242` (2026-07-14)  
**Pipeline Play:** `riverWaterPlayPipeline = LakeFirstHydrology` → `UwpLakeFirstPlayPipeline`  
**No aplica a:** WaterAuthoring, HydroGraphV2, CleanRiverSplinePlay, Crest / StylizedWater sueltos

Documentos hermanos:

| Doc | Para qué |
|-----|----------|
| Este archivo | Diagnóstico + knobs + contratos por tipo de río |
| `TRIBUTARY_JOIN_TUNING_TUTORIAL.md` | Detalle histórico de joins (spill gancho U, cupos) |
| `LAKE_FIRST_CHANNEL_CONTRACT_PLAN.md` | Plan de producto / M1–M6 |
| `headwater-feeder-carve-form-notes.txt` | Bitácora corta Headwater/Inland |
| `Docs/AUDIT_MOTOR_HIDROLOGIA_RTS.md` | Auditoría motor amplio |

---

## 0b. Cupos tipados por tamaño de mapa (lobby)

| Grid | Lagos | LakeSpill (máx) | Inland | Headwater | `riverCount` |
|------|------:|----------------:|-------:|----------:|-------------:|
| 192 | 2 | 1 | 1 | 1 | **4** |
| 256 | 3 | 2 | 2 | 1 | **6** |
| 320 | 3 | 2 | 2 | 2 | **7** |
| 384 | 3 | 2 | 2 | 2 | **7** |

- MainRiver siempre = 1 (no configurable).
- **LakeSpill ≤ 2** en todos los tamaños (calidad de juntas / evitar 2 spills a la misma boca).
- Targets **blandos**: solo se aceptan si pasan validación.
- Spill no come slots de inland/headwater (`lakeSpillTargetCount` + reserva).
- Implementación: `UwpLakeFirstPlayPipeline.ResolveTypedHydrologyQuotas` / lobby steppers.

---

1. El **borde blanco** del material de río = intersección mesh de agua ∩ terreno tallado (carve).  
   Si falta blanco → mesh no toca el banco (mesh estrecho, carve ancho `Ceil`, o Y distinto).  
2. **Canal** = mesh + carve juntos. Lo orgánico es el **ancho a lo largo**, no meter V/U bajo el ribbon.  
3. Tipos distintos = roles distintos. **No** copiar knobs de spill al inland ni V de headwater al main.

```
MainRiver ──borde a borde──► (+ vados)
     ▲
LakeSpill (lago → main, vados si hace falta)
     ▲
InlandFeeder (origen inland → main)  ← NO vados
     ▲
HeadwaterFeeder (arroyo → Inland mid-body T)  ← NO vados
```

---

## 1. Roles de producto (contrato)

| Tipo | Código | Rol | Vados | Join tipico |
|------|--------|-----|-------|-------------|
| MainRiver | índice `0` | Troncal borde↔borde | Sí | — |
| LakeSpill | `LakeSpill` | Lago → main | Sí si hace falta | Orilla del main (sin pin profundo) |
| InlandFeeder | `InlandFeeder` | Arroyo inland → main | **No** | First-entry orilla main; sin pin confluencia |
| HeadwaterFeeder | `HeadwaterFeeder` | Nacimiento → **Inland** | **No** | **T mid-body** (~32–68 % a lo largo, lejos de ambos tips) |

**GameObjects** (bajo root Water):

| Tipo | Nombre |
|------|--------|
| Main | `Water_RiverSurface_MainRiver` |
| Spill | `Water_RiverSurface_LakeSpill_{i}` |
| Inland | `Water_RiverSurface_InlandFeeder_{i}` |
| Headwater | `Water_RiverSurface_HeadwaterFeeder_{i}` |

Implementación: `ResolveRiverSurfaceGameObjectName` en `RiverSurfaceMeshBuilder`.

---

## 2. Pipeline (orden real)

```
RTSMapGenerator / MapLobby
  → UwpLakeFirstPlayPipeline (+ RtsHydrologyProfile.Apply)
  → MapGenerator (paquete com.pmg.unified-world-pipeline)
       ├─ UwpLakeFirstHydrologyBuilder          … main + LakeSpill (A*)
       ├─ UwpLakeFirstSupplementalHydrologyBuilder … Inland + Headwater
       ├─ WaterGenerator / WaterMeshBuilder     … celdas, fords, lagos MS
       ├─ RiverSurfaceMeshBuilder               … centerline final, meshHalf, maskHalf
       └─ TerrainExporter                       … stamp carve + bedFloor + shore
```

**Autoridad de anchos/máscara:** `RiverSurfaceMeshBuilder` (mascaras por río) + stamps en `TerrainExporter`.  
**Autoridad de colocación:** builders Lake First + Supplemental.  
**Perfil de knobs globales:** `RtsHydrologyProfile` / `MapGenConfig` (no abrir otro pipeline).

---

## 3. Contrato de canal (regla de oro)

```
meshHalf  >  maskHalf
maskHalf ≈ meshHalf / MeshOverCarveMul     (≈ / 1.3 → foamRatio ≈ 0.77)
stamp    ≤ ~0.98 × meshHalf                (cubre interior del ribbon)
floor    = uniformFlatChannelFloor         (bandeja plana bajo el mesh)
```

Constante clave: `LakeFirstChannelMeshOverCarveMul` (= `LakeFirstHeadwaterMeshOverCarveMul` ≈ **1.3**) en `RiverSurfaceMeshBuilder`.

| Quiero… | Efecto | Knob |
|---------|--------|------|
| Foam más fino | mesh mucho > carve | ↑ `MeshOverCarveMul` (p.ej. 1.35–1.4) |
| Foam más grueso / “más orilla” | mesh más cerca del carve | ↓ hacia 1.15–1.2 (**peligro:** islas) |
| Orilla sin blanco | mesh < stamp `Ceil(mask)` | Ver §5 síntomas A/B |

**Prohibido:** `flatFloor=false` (perfil V/U) bajo el ribbon en Play Lake First. Deja terreno alto → charcos / “el carve se come el río”.

---

## 4. Archivos y knobs por zona

### 4.1 Placement / validación

| Qué | Archivo | Símbolos / knobs |
|-----|---------|------------------|
| Cupo rivers / headwater | `UwpLakeFirstPlayPipeline.cs` | `riverCount` (≥5 recomendado: main+spills+inland+headwater) |
| Inland + Headwater path/aceptación | `UwpLakeFirstSupplementalHydrologyBuilder.cs` | validación path, `JoinIsOnReceiverMidBody`, ángulo T, rejects |
| Main + spill A* | `UwpLakeFirstHydrologyBuilder.cs` | pin confluencia spill (cuidado: no reintroducir en inland) |

**Inland — validación típica (producto):**

- Path ≥ ~40 celdas, span Chebyshev ≥ ~32  
- Windiness centerline ≤ ~1.42  
- **Sin** meandro orgánico Lake-First apilado (colita / S)

**Headwater → Inland — join:**

- Along receptor **~32–68 %**, Chebyshev ≥ ~10 a ambos tips  
- Ángulo T ~55–125° (relajado ~48–132°)  
- Reject: `join_not_on_receiver_midbody`, `join_angle_not_t_to_inland`

### 4.2 Mesh / máscara / joins visuales

Archivo: `Packages/.../MapGenerator/RiverSurfaceMeshBuilder.cs`

| Síndrome / ajuste | Método / constante |
|-------------------|--------------------|
| Cap ancho inland | `LakeFirstInlandMeshMaxHalfCells` (≈ **1.85**) + `CapLakeFirstInlandFeederHalfWidths` |
| Tras Cap: mesh < stamp Ceil | **`SyncLakeFirstInlandMeshOverCarveAfterCap`** (obligatorio tras Cap) |
| Foam T Inland↔Headwater | `ApplyLakeFirstInlandHeadwaterJoinMeshWiden` mul≈**1.42** + extraHalf≈0.65 + **2º pase** tras headwater; headwater boca→inland solape fuerte |
| Headwater boca sin gancho | `StraightenTributaryMouthApproach` + first-entry + snap (sin Tuck/Append ingress) |
| Headwater mesh≥carve | `SyncLakeFirstHeadwaterMeshToCarveMask` |
| Spill/Inland → main cuña | `BoostLakeSpillMainJoinCarveMaskToMesh` (también inland tras Cap) |
| Spill sin “pasa de largo” | trim + `SnapLakeSpillMouthToMainBank` pre/post meandro; **no** tip post-trim al eje |
| Foam ratio compartido | `ApplyLakeFirstChannelCarveToMeshGrowthProfile` |
| Origen inland Y | `ApplyLakeFirstInlandFeederSourceEmergenceY` (**solo tip origen**; no cabeza mid-body) |

### 4.3 Carve / lecho (Y mundo)

Archivo: `Packages/.../MapGenerator/TerrainExporter.cs`

| Qué | Dónde |
|-----|-------|
| Floor 01 unificado | `ComputeUwpUnifiedChannelBedFloor01` |
| Lecho inland ≈ main | rama `IsInlandFeeder` → ~`1.05 × riverTerrainCarveDepthWorld`, piso ≥ ~0.42 |
| Lecho headwater | ≥ ~0.40 world / ~`1.15 × carveDepth` |
| Lecho main | ~`1.22 × carveDepth` (más hondo) |
| Flat floor | stamps con `uniformFlatChannelFloor` / política UWP frozen |

Superficie Y unificada: `WaterVisualPipelinePolicy.ResolveUwpUnifiedChannelSurfaceWorldY`  
(`riverRibbonAntiZFightYOffsetWorld`, `riverSurfaceMeshExtraYOffsetWorld` en `MapGenConfig`).

### 4.4 Vados (fords)

| Qué | Archivo |
|-----|---------|
| Supplemental sin fords | `UwpLakeFirstSupplementalHydrologyBuilder` → `fordCells = null` |
| Skip mandatory en supplemental | `WaterMeshBuilder` |
| Limpieza post thin-zones | `WaterGenerator.ClearFordsAlongSupplementalRivers` (no tocar junta main) |

**Regla:** Inland / Headwater **nunca** deben llevar vados fantasma (path gizmos en seco).

### 4.5 Anchos globales (perfil)

`RtsHydrologyProfile` / `MapGenConfig`:

- `riverVisualRibbonFullWidthCellsMain` / `…Tributary`  
- Confluence: `riverConfluenceTributaryEndWidthMul`, `riverSurfaceTributaryConfluenceApproachCells`  
- Carve depth: `riverTerrainCarveDepthWorld`

Preferir **constantes tipadas** en `RiverSurfaceMeshBuilder` para Lake First antes de tocar el perfil global (menos regresiones).

---

## 5. Catálogo de síntomas → causa → ajuste

### A — Falta orilla blanca en un tramo / en una T

| | |
|--|--|
| **Se ve** | Azul cortado a canto vivo; arena/cesped asoma; “desfase en Y” en la orilla |
| **Causa real** | Mesh no intersecta el banco (`meshHalf` &lt; radio entero del stamp, o cap sin re-sync) |
| **Ajuste** | 1) `SyncLakeFirstInlandMeshOverCarveAfterCap` tras Cap<br>2) En T headwater: `ApplyLakeFirstInlandHeadwaterJoinMeshWiden` (mul≈1.42) + **2º pase** cuando ya existe mesh headwater<br>3) Headwater→inland: `ApplyLakeFirstHeadwaterReceiverJoinMeshWiden` sin clamp 0.82 si receptor inland<br>4) No bajar `MeshOverCarveMul` por debajo ~1.2 |
| **Archivo** | `RiverSurfaceMeshBuilder` |

### B — Charcos / tramos solo foam / path visible sin agua

| | |
|--|--|
| **Se ve** | Agua a trozos; blanco zig-zag; gizmos del path sobre tierra |
| **Causa** | Carve no cubre interior del mesh (`flatFloor=false`, body carve estrecho, mask &lt;&lt; mesh) |
| **Ajuste** | `flatFloor=true`; subir body carve headwater (`LakeFirstHeadwaterCarveBodyMaxCells` ~1.45–1.55); `SyncLakeFirstHeadwaterMeshToCarveMask` |
| **Archivo** | `TerrainExporter` + `RiverSurfaceMeshBuilder` |

### C — Headwater “colita” / gancho en la boca (U / V)

| | |
|--|--|
| **Se ve** | Boca en forma de gancho o Y aguda en tip del inland |
| **Causa** | Join en tip; Tuck/Append; meandro en boca; ángulo no-T |
| **Ajuste** | Mid-body only; first-entry+snap; `StraightenTributaryMouthApproach`; **sin** Tuck |
| **Archivo** | Supplemental (validación) + `RiverSurfaceMeshBuilder` (trim) |

### D — Inland blob / demasiado ancho / “loco”

| | |
|--|--|
| **Se ve** | Cinta ancha tipo spill; S apretada; gizmo circular |
| **Causa** | Anchos spill + meandro×N + pin dentro del main |
| **Ajuste** | Cap `LakeFirstInlandMeshMaxHalfCells`; **sin** meandro orgánico Lake-First; **sin** pin confluencia profundo |
| **Archivo** | `RiverSurfaceMeshBuilder` prepare/resolve + Cap |

### E — Escalon / blanco jagged Inland→Main

| | |
|--|--|
| **Se ve** | Junta con main “rota”; lecho inland más alto; orilla blanquecina irregular |
| **Causa** | BedDepth inland (0.58× genérico) ≠ main; mask join floja tras Cap |
| **Ajuste** | `ComputeUwpUnifiedChannelBedFloor01` inland ~1.05×; `BoostLakeSpillMainJoinCarveMaskToMesh` tras Cap |
| **Archivo** | `TerrainExporter` + `RiverSurfaceMeshBuilder` |

### F — Spill “pasa de largo y vuelve” (gancho U)

| | |
|--|--|
| **Ver** | `TRIBUTARY_JOIN_TUNING_TUTORIAL.md` §3 |
| **Resumen** | Trim+snap **antes** del meandro; no tip post-trim al eje; no re-pin a confluencia |

### G — Headwater no spawnea

| | |
|--|--|
| **Ver** | `TRIBUTARY_JOIN_TUNING_TUTORIAL.md` §6 |
| **Resumen** | Subir `riverCount`; preferir receptor Inland; relajar sep lago si fallback spill |

### H — Vados fantasma en inland/headwater

| | |
|--|--|
| **Se ve** | Celdas `riverFord` / gizmos en seco sin ribbon |
| **Ajuste** | `fordCells=null` supplemental; skip mandatory; `ClearFordsAlongSupplementalRivers` |
| **Archivo** | Supplemental + `WaterMeshBuilder` + `WaterGenerator` |

---

## 6. Orden seguro al tocar anchos inland (no romper foam)

Tras cualquier Cap / boost de inland:

```
1. CapLakeFirstInlandFeederHalfWidths          // estrecha producto
2. BoostLakeSpillMainJoinCarveMaskToMesh      // junta con main
3. SyncLakeFirstInlandMeshOverCarveAfterCap   // mesh ≥ Ceil(mask)×OverCarve
4. ApplyLakeFirstInlandHeadwaterJoinMeshWiden // T headwater
5. SyncLakeFirstInlandMeshOverCarveAfterCap   // otra vez tras widen
```

**Nunca** dejar Cap como último paso sin Sync: el stamp usa `Ceil(mask/cellSize)` y el mesh queda corto → síntoma A.

---

## 7. Logs útiles (Play)

Activar con `uwpOwnedVisualPolicy` y/o `debugHydrologyNetwork`.

```
[LakeFirstSupplemental] … inlandAccepted=… headwaterAccepted=… headwaterRejected=…
[LakeFirstChannelContract] kind=InlandFeeder|HeadwaterFeeder|LakeSpill|Main meshOverCarve=1.30 …
[HeadwaterFeederJoinTrim] riverIndex=… receiver=… pts=…
[InlandFeederJoinTrim] …
[LakeSpillJoinTrim] phase=preMeander|postMeander …
[LakeFirstSupplementalVisual] audit … inland=… headwater=…
```

Rejects headwater frecuentes:  
`join_not_on_receiver_midbody`, `join_angle_not_t_to_inland`, `procedural_exhausted`, `join_near_lake_*`.

**Trampa:** logs `flatFloor=1` hardcodeados en algunos sitios del MeshBuilder **no** garantizan el stamp real → mirar `TerrainExporter`.

---

## 8. Checklist de no-regresión (seed 424242 u otra fija)

1. Stop → Play, Lake First, misma seed.  
2. Consola: `inlandAccepted≥1`, `headwaterAccepted≥1` (ideal).  
3. Hierarchy: GOs `InlandFeeder_*` y `HeadwaterFeeder_*` presentes.  
4. Headwater→Inland: **T** mid-body, sin colita, orilla **blanca continua** en la cuña.  
5. Inland→Main: sin escalón de lecho / orilla jagged.  
6. Main: bandeja ancha, foam solo orilla (sin franja blanca axial).  
7. Spill→main: sin gancho U.  
8. Inland/Headwater: **sin** vados en seco.  
9. Headwater: ribbon continuo (sin charcos).

---

## 9. Anti-patrones (lista corta)

1. `flatFloor=false` bajo ribbon Lake First.  
2. Cap inland **sin** `SyncLakeFirstInlandMeshOverCarveAfterCap`.  
3. Encoger carve “para arroyo fino” dejando huecos bajo el mesh.  
4. Meandro orgánico×N en inland.  
5. Tuck / Append ingress profundo headwater→inland.  
6. Join headwater en tip del inland (nariz V).  
7. Fords en Inland/Headwater.  
8. Pin de confluencia deep-main para inland visual.  
9. Cambiar `SampleScene` / materiales / prefabs “para probar foam” — el foam se arregla en mesh/carve, no en el material.  
10. Abrir HydroGraphV2 / Spline como default para “arreglar” Lake First.

---

## 10. Cómo diagnosticar en Editor (paso a paso)

1. Seleccionar `Water_RiverSurface_InlandFeeder_*` / `HeadwaterFeeder_*`.  
2. Comprobar naranja = bounds del mesh; esferas/puntos = centerline.  
3. Si el mesh azul no llega al borde de arena → síntoma A (widen / Sync).  
4. Si mesh llega pero no hay blanco → mirar Y surface vs bed (`antiZ` / `extraY` / bedDepth).  
5. Si path gizmos sin agua → charcos (B) o fords fantasma (H).  
6. Regenerar **misma seed** tras un solo cambio (diffs mínimos).

---

## 11. Mapa de archivos (resumen)

| Archivo (paquete salvo nota) | Responsabilidad |
|------------------------------|-----------------|
| `Runtime/UWP/UwpLakeFirstHydrologyBuilder.cs` | Main + LakeSpill |
| `Runtime/UWP/UwpLakeFirstSupplementalHydrologyBuilder.cs` | Inland + Headwater + fords null |
| `Runtime/MapGenerator/RiverSurfaceMeshBuilder.cs` | Centerline visual, mesh/mask, joins, Cap/Sync |
| `Runtime/MapGenerator/TerrainExporter.cs` | Stamp carve + bed floors |
| `Runtime/MapGenerator/WaterGenerator.cs` | Clear fords supplemental |
| `Runtime/MapGenerator/WaterMeshBuilder.cs` | Skip mandatory fords supplemental |
| `Runtime/Generation/RtsHydrologyProfile.cs` | Perfil anchos/depth |
| `Runtime/MapGenerator/MapGenConfig.cs` | Campos serializados / offsets Y |
| `Assets/.../06_LakeFirst/UwpLakeFirstPlayPipeline.cs` | Modo Play + cupos |

---

*Última estabilización documentada: foam Inland↔Headwater (Sync post-Cap + widen T) + bed/boost Inland→Main. Seed OK: 424242.*
