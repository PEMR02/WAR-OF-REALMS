# Tutorial: ajustar tributarios (Lake First) — join + carve

**Estado:** actualizado 2026-07-14 (main base ancha + perfil V en Headwater + cupo headwater)  
**Pipeline:** `riverWaterPlayPipeline = LakeFirstHydrology` (`UwpLakeFirstPlayPipeline`)  
**No aplica a:** WaterAuthoring / HydroGraphV2 / Spline Play

**Guía operativa completa (síntomas → knobs):**  
`LAKE_FIRST_WATER_SYSTEM_TUTORIAL.md` ← empezar ahí ante bugs de foam / T / inland.

Complementa: `LAKE_FIRST_CHANNEL_CONTRACT_PLAN.md`, `Docs/AUDIT_MOTOR_HIDROLOGIA_RTS.md`.

---

## 1. Mapa mental (qué pieza hace qué)

```
UwpLakeFirstHydrologyBuilder
  ├─ Path A* lago → confluencia
  └─ PinLakeFirstTributaryEndpointConfluence  ← extremo a menudo DENTRO del main

RiverSurfaceMeshBuilder.TryResolveLakeFirstFinalCenterlineCells
  ├─ [spill] Trim hook + first-entry + SnapBank   ← ANTES del meandro
  ├─ Meandro (protectJoinTail)
  ├─ Append (spill: solo ancla a orilla, sin ingress profundo)
  └─ [spill] Trim first-entry + SnapBank          ← DESPUÉS del meandro

RiverSurfaceMeshBuilder (anchos)
  └─ meshHalf → maskHalf = mesh × (1/1.3)         ← foam ratio

TerrainExporter
  └─ stamps carve ≤ ~0.98× mesh (+ cuña spill acotada)
```

**Overlay verde / naranja** = `FinalCenterline` / `DebugCarvePathCells` (path visual final, no el A* crudo).

---

## 2. Tipos (nombres producto ↔ código)

| Producto | `UwpTributaryOriginKind` | Perfil carve stamp |
|----------|--------------------------|--------------------|
| mainriver | índice 0 | **Bandeja plana** ancha (base visible bajo el mesh) |
| tributary-lake | `LakeSpill` | Bandeja plana (foam en uniones) |
| tributary-inland | `InlandFeeder` | Bandeja plana |
| headwaterfeeder | `HeadwaterFeeder` | **Bandeja plana continua** bajo el mesh + lecho más hondo. **Prohibido** V/U bajo el ribbon (deja terreno alto → charcos / foam / “carve se lo come”) |

---

## 3. Bug “pasa de largo y se devuelve” (gancho U)

### Causa raíz
1. Pin de confluencia **dentro** del canal del main.
2. Meandro con `protectJoinTail` redibuja entre lago ↔ ese extremo → gancho U.
3. Trim *solo post-meandro* a menudo no cortaba (`beyond ≤ 1`, gates dendrítico / ratio emissary).

### Qué tocar (orden correcto)

| Paso | Dónde | Qué hacer |
|------|--------|-----------|
| A | `TryResolveLakeFirstFinalCenterlineCells` | Spill: **trim + snap orilla ANTES del meandro** |
| B | Mismo método, post-meandro | Spill: `TrimRiverAtFirstMainCorridorContact(..., forceSpillJoin: true)` + `SnapLakeSpillMouthToMainBank` |
| C | `AppendLakeFirstTributaryCenterlineTowardMainRiver` | Spill: **no** ingress hacia el eje; anclar a orilla |
| D | Tip post-trim | **No** reactivar `ApplyLakeSpillMainBankTip…` en spill (recrea el V) |

### Logs de validación (Play)
```
[LakeSpillJoinTrim] phase=preMeander  riverIndex=N before=X after=Y
[RiverEmissaryMainTrim] … mode=first_entry_spill removed=…
[LakeSpillJoinTrim] phase=postMeander …
```
Esperado: `after < before` en preMeander cuando había overshoot.

### Anti-patrones (no volver a)
- Re-pin a `MainRiverConfluenceCell` tras meandro en spill.
- Tip post-trim hacia el eje del main.
- Encoger carve bajo el mesh “para que se vea más fino” (rompe foam / crea islas).
- Confiar en `IsLakeEmissaryRiverIndex` (ratio 0.24–0.32): no identifica spill de forma fiable.
- Aplicar perfil V/U al **main** pensando que “se ve más RTS”: deja el mesh azul muy delgado en una trinchera de arena.

---

## 4. Bug “sin borde blanco” (mesh no intersecta carve)

### Regla
El material genera foam por **profundidad** (mesh ∩ terreno).  
Sin intersección → sin borde blanco.

Contrato canal:
```
maskHalf ≈ meshHalf / 1.3     (MeshOverCarveMul)
stamp maxDist ≤ meshHalf * 0.98
```

### Qué tocar

| Síntoma | Archivo | Knobs / cuidado |
|---------|---------|-----------------|
| Orilla trib sin foam | `TerrainExporter.StampUwp…` | No `Max(mask, mesh)` en stamps Lake First |
| Bandeja blanca en junta spill | `StampLakeSpillMainJoinWedgeCarve` + cap join | Cuña compacta; join ≤ foamCeil |
| Main “azul fino en valle ancho” | Stamp `flatFloor` / flatRatio | Main debe ser **bandeja plana**; V solo Headwater |
| Mask vs riverCells muy distintos | Continuity prune / placer | Árboles: `IsWater` lógico ≠ máscara visual |

### Logs
```
[LakeFirstChannelContract] kind=LakeSpill meshOverCarve=1.30 …
[RiverVisualContinuity] … oppositeBankPruned=…
[RiverTerrainPaintUwpMask] maskCells=… logicalRiverCells=…
```

**Ojo:** el log Main `flatFloor=1` en `RiverSurfaceMeshBuilder` está **hardcodeado** y no refleja el stamp real. Verdad = `TerrainExporter` (`uniformFlatChannelFloor`).

---

## 5b. Jerarquía de GameObjects (lectura)

Nombres bajo water root:

| Tipo | Nombre |
|------|--------|
| Main | `Water_RiverSurface_MainRiver` |
| LakeSpill | `Water_RiverSurface_LakeSpill_{i}` |
| InlandFeeder | `Water_RiverSurface_InlandFeeder_{i}` |
| HeadwaterFeeder | `Water_RiverSurface_HeadwaterFeeder_{i}` |

Implementación: `ResolveRiverSurfaceGameObjectName` en `RiverSurfaceMeshBuilder`.

### InlandFeeder “loco”
Causa: meandro ×2 en prepare + otro en resolve + pin dentro del main.  
Fix: un solo meandro en prepare; resolve **no** remandra inland; trim first-entry + snap orilla.

### Headwater charcos (carve alto / estrecho)
Causa residual tras flatFloor: body carve ~1.08c demasiado estrecho.  
Fix: body max ~1.55c, bodyMul ~0.90, stamp ~0.95–0.98×mesh, bed ≥0.40.

| Tipo | Look deseado | Stamp |
|------|--------------|--------|
| **Main** | Base ancha, mesh azul llena el lecho, foam solo orilla | `flatFloor = true` |
| **Spill / Inland** | Canal estrecho estable + foam | `flatFloor = true` |
| **Headwater** | Continuidad azul sin islas; orilla blanca fina; lecho lo bastante bajo | `flatFloor = true` + `bedDepthWorld` headwater ≥ ~0.28 |

### Regresión “el carve se come el headwater”
**Síntoma:** charcos, tramo solo foam blanco, ribbon interrumpido; markers del path siguen ahí.  
**Causa:** perfil V/U (`flatFloor=false`) o lecho demasiado alto bajo el mesh → terreno dentro del ribbon.  
**Arreglo histórico / vigente:** bandeja plana continua + mesh ≥ carve×1.3 + sync mask↔mesh (ver notas Headwater). **No** reactivar V bajo el ribbon.

Knobs:

| Quiero… | Dónde | Campo |
|---------|--------|--------|
| Main más ancho / estrecho | `RtsHydrologyProfile` | `riverVisualRibbonFullWidthCellsMain` + `meshMul` |
| Main más hondo (sin afilar) | `TerrainExporter` | `bedDepthWorld` main; **mantener flatFloor** |
| Headwater sin que lo coma el terreno | `TerrainExporter` | `flatFloor=true` + subir `bedDepthWorld` headwater |
| Foam más fino / grueso | `RiverSurfaceMeshBuilder` | `LakeFirstChannelMeshOverCarveMul` (≈1.3) |

---

## 6. HeadwaterFeeder no aparece en el mapa

### Causa típica (seed 424242)
```
riverCount=4 → tribBudget=3
LakeSpill aceptados=2 → missingSlots=1
headwaterReserve=1 → inlandTarget=0
Headwater solo puede unirse a LakeSpill → join_near_lake_* / procedural_exhausted (0/48)
```

### Qué tocar

| Paso | Archivo | Qué |
|------|---------|-----|
| Cupo | `UwpLakeFirstPlayPipeline` | `riverCount ≥ 5` (main + spills + **inland** + headwater) |
| Receptor | Supplemental | Preferir InlandFeeder; spill es fallback |
| Sep lago | `PassesHeadwaterFeederValidation` | Si receptor es spill, relajar `minLakeSep` (2/3, piso 10) |

### Logs OK / fail
```
[LakeFirstSupplemental] … inlandAccepted≥1 headwaterAccepted≥1 …
[LakeFirstChannelContract] kind=HeadwaterFeeder …
```
Fail:
```
headwaterAccepted=0 headwaterRejected=N
reason=procedural_exhausted | join_near_lake_mouth | join_near_lake_spill_head
```

---

## 7. Checklist Play (1 seed)

1. SampleScene, pipeline Lake First, Stop→Play.
2. Consola: `rivers≥5`, `headwaterAccepted≥1` (idealmente también `inlandAccepted≥1`).
3. Spill→main: sin gancho U.
4. Main: **base ancha** (azul llena el lecho), foam fino en orilla.
5. Headwater: ribbon **continuo** (sin charcos / tramo solo blanco); lecho bajo el mesh.
6. Junta spill: sin polígono carve sin agua.

---

## 8. Archivos clave

| Archivo | Rol |
|---------|-----|
| `Packages/.../RiverSurfaceMeshBuilder.cs` | Centerline final, trim spill, mesh/mask |
| `Packages/.../TerrainExporter.cs` | Stamps: main flat / headwater V, bed depths |
| `Packages/.../UwpLakeFirstSupplementalHydrologyBuilder.cs` | Inland + Headwater placement/validación |
| `Packages/.../UwpLakeFirstHydrologyBuilder.cs` | Path A* + pin confluencia |
| `Packages/.../RtsHydrologyProfile.cs` | Anchos / flatRatio / bankPower |
| `Assets/.../06_LakeFirst/UwpLakeFirstPlayPipeline.cs` | `riverCount`, headwaterTarget |
