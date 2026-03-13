# RTS Terrain Rendering Improvement System

Sistema de mejora visual del terreno tipo Manor Lords / Age of Empires IV: variación por pendiente, decales bajo edificios, vegetación dispersa y cuadrícula solo en modo construcción.

---

## PART 1 — Terrain Shader

**Shader:** `Project/RTS Terrain Blend`  
**Ruta:** `Assets/_Project/06_Visual/Shaders/RTS_TerrainBlend.shader`

- **Uso:** Material para terreno basado en **malla** (mesh). Mezcla tres texturas según pendiente y altura en mundo.
- **Reglas de mezcla:**
  - **Grass** en superficies planas.
  - **Dirt** en pendientes medias.
  - **Rock** en pendientes fuertes (y opcionalmente a mayor altura).

**Parámetros expuestos:**

| Parámetro | Descripción |
|-----------|-------------|
| Grass / Dirt / Rock | Texturas (2D) |
| Slope threshold | Ángulo (grados) a partir del cual se mezcla más roca. |
| Blend sharpness | Nitidez de la transición (valores altos = corte más duro). |
| Height influence | Influencia de la altura mundial en la mezcla (0 = solo pendiente). |
| Tiling | Escala UV en XZ para las texturas. |

**Nota:** Si usas **Unity Terrain** nativo con TerrainData y alphamaps (p. ej. RTS Map Generator), el material por defecto sigue siendo **Universal Render Pipeline/Terrain/Lit**. Puedes usar este shader en un **plano o malla exportada** para una vista alternativa, o en escenas con terreno mesh.

---

## PART 2 — Building Ground Decal

**Script:** `BuildingGroundDecal.cs`  
**Ruta:** `Assets/_Project/01_Gameplay/Building/BuildingGroundDecal.cs`

- **Comportamiento:** Al colocarse un edificio (BuildSite o BuildingInstance), genera un **quad** bajo la base con textura de tierra y **alpha suave en los bordes** (terreno “gastado” alrededor del edificio).
- **Dónde añadirlo:** En el **prefab del edificio** (o en el prefab del BuildSite si quieres decal también durante la construcción).
- **Inspector:**
  - **Dirt Material:** Material con shader `Project/RTS Ground Decal` (opcional; si no, se usa **Dirt Texture**).
  - **Dirt Texture:** Si no hay material, se crea uno en runtime a partir de esta textura.
  - **Height Offset / Margin:** Ajuste fino de posición y tamaño.

**Shader del decal:** `Project/RTS Ground Decal` — `Assets/_Project/06_Visual/Shaders/RTS_GroundDecal.shader` (borde suave por UV).

---

## PART 3 — Vegetation Scatter

**Script:** `VegetationScatter.cs`  
**Ruta:** `Assets/_Project/01_Gameplay/Environment/VegetationScatter.cs`

- **Comportamiento:** Coloca de forma procedural **hierba, rocas y arbustos** con escala y rotación aleatorias.
- **Evita edificios:** No genera en celdas ocupadas (MapGrid) ni en un radio de celdas configurable (`buildingPadding`).
- **Parámetros:**
  - **Prefabs:** Grass / Rock / Bush (opcional; si falta uno, no se usa ese tipo).
  - **Spacing:** Distancia aproximada entre puntos (menor = más denso).
  - **Weights:** Proporción grass / rock / bush (se normalizan).
  - **Scale Min/Max, Rotation Range:** Variación de escala y rotación Y.
  - **Seed:** Reproducibilidad; 0 = aleatorio.
- **Uso:** Añade el componente a un GameObject de la escena (p. ej. vacío “Environment”). Se ejecuta con un pequeño retraso para que MapGrid y edificios estén listos.

---

## PART 4 — Grid Visibility

La **cuadrícula de construcción** ya está configurada para mostrarse **solo mientras se coloca un edificio**:

- **Componente:** `GridGizmoRenderer`.
- **Parámetro:** `Show Only In Build Mode` = **true** (por defecto).
- Cuando no estás en modo construcción (BuildingPlacer.IsPlacing), la grilla no se dibuja en Scene ni en Game.

Si en alguna escena la grilla se ve siempre, revisa en el objeto con `GridGizmoRenderer` que **Show Only In Build Mode** esté activado.

---

## PART 5 — Objetivos y rendimiento

- **Variación del terreno:** Shader por pendiente/altura + decales bajo edificios.
- **Aspecto más natural:** Menos “prototipo” gracias a mezcla grass/dirt/rock y vegetación dispersa.
- **Rendimiento en mapas grandes:**
  - **VegetationScatter:** Aumentar `spacing` para menos instancias; usar prefabs ligeros (LOD si es posible).
  - **BuildingGroundDecal:** Un quad por edificio; bajo coste.
  - **Shader terreno:** Un draw call por malla; mantener una sola malla de terreno cuando sea posible.
  - **Grid:** Solo visible en build mode, sin coste extra en juego normal.

---

## Resumen de archivos

| Elemento | Ruta |
|----------|------|
| Shader terreno (grass/dirt/rock) | `Assets/_Project/06_Visual/Shaders/RTS_TerrainBlend.shader` |
| Shader decal suelo | `Assets/_Project/06_Visual/Shaders/RTS_GroundDecal.shader` |
| **Material terreno** | `Assets/_Project/06_Visual/Materials/MAT_RTS_TerrainBlend.mat` |
| **Material decal** | `Assets/_Project/06_Visual/Materials/MAT_RTS_GroundDecal.mat` |
| BuildingGroundDecal | `Assets/_Project/01_Gameplay/Building/BuildingGroundDecal.cs` |
| VegetationScatter | `Assets/_Project/01_Gameplay/Environment/VegetationScatter.cs` |
| Grid (solo build mode) | `GridGizmoRenderer.showOnlyInBuildMode = true` |

### Creado con MCP Unity (ai-game-developer)

- Materiales **MAT_RTS_TerrainBlend** y **MAT_RTS_GroundDecal** creados.
- Escena **SampleScene**: GameObject **VegetationScatter** con componente `VegetationScatter` (guardado).
- Prefabs con **BuildingGroundDecal**: PF_House, PF_TownCenter, PF_Barracks, PF_Castillo, PF_Granary, PF_LumberCamp, PF_MiningCamp, PF_Buildsite.
- Opcional: en cada edificio, asignar **Dirt Material** = `MAT_RTS_GroundDecal` en el Inspector para el decal bajo edificios. Si no se asigna, el script puede usar **Dirt Texture** o un material por defecto en runtime.
