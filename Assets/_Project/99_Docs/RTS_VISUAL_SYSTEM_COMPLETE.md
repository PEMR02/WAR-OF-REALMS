# Sistema visual RTS completo (Manor Lords / AoE IV / Anno)

Sistema de mejora visual para el RTS en Unity URP: iluminación, clima, rim lighting, terreno, decals, sombra bajo unidades, vegetación, grid y post-processing.

---

## PART 1 — RTS Environment Lighting

**Script:** `RTSEnvironmentSetup.cs`

- Configura al iniciar: Directional Light (intensity 1.35, color cálido, rotación 52,-35,0, sombras Soft, strength 0.95).
- Niebla exponencial (density 0.0035, color azul gris).
- Skybox exposure 1.2.
- Todos los parámetros editables en el Inspector.

---

## PART 2 — Weather System

**Script:** `WeatherManager.cs`

- Estados: **Sunny** (intensity 1.35, fog 0.003), **Cloudy** (0.7, 0.005), **Storm** (0.3, 0.009).
- Métodos: `SetSunny()`, `SetCloudy()`, `SetStorm()`, `SetPreset(int)`.

---

## PART 3 — Rim Lighting Shader

**Shader:** `Project/RTS Rim Lighting`  
**Material:** `MAT_RTS_RimLighting.mat`

- Efecto Fresnel en los bordes (mejor lectura desde cámara RTS).
- Parámetros: `RimColor`, `RimIntensity` (0.2), `RimPower` (4).
- Aplicar a unidades y edificios (material con este shader o variante Lit + rim).

---

## PART 4 — Terrain Shader

**Shader:** `Project/RTS Terrain Blend`  
**Material:** `MAT_RTS_TerrainBlend.mat`

- Mezcla grass (plano), dirt (pendiente media), rock (pendiente fuerte).
- Parámetros: texturas, slope threshold, blend sharpness.

---

## PART 5 — Building Ground Decals

**Script:** `BuildingGroundDecal.cs`

- Quad bajo edificios con textura tierra y alpha en los bordes.
- Ya añadido a prefabs de edificios y BuildSite.

---

## PART 6 — Unit Ground Shadow

**Script:** `UnitGroundShadow.cs`

- Sombra circular bajo la unidad, escala configurable, alineada al terreno.
- **Añadido con MCP** a: PF_Aldeano, PF_Archer, PF_Lancero, PF_Scout, PF_Swordman, PF_Mounted_King.

---

## PART 7 — Procedural Vegetation

**Script:** `VegetationScatter.cs`

- Objetos: grass, rocks, bushes, flowers (opcional).
- Parámetros: density/spacing, random rotation, random scale.
- No genera bajo edificios (MapGrid).

---

## PART 8 — Grid Visibility

**Script:** `GridVisibility.cs`

- Grid visible solo al colocar edificios; oculto en juego normal.
- Configura `GridGizmoRenderer.showOnlyInBuildMode = true` y referencia a `BuildingPlacer`.

---

## PART 9 — Post Processing

**Script:** `RTSPostProcessingSetup.cs`

- Ajusta el Volume activo: Tonemapping ACES, Color Adjustments (post exposure 0.55, contrast 28, saturation 12), Bloom (threshold 0.9, intensity 0.35, scatter 0.6).
- SSAO se configura en el Renderer (SSAO feature): intensity 0.45, radius 0.25.

---

## PART 10 — Scene Setup (MCP)

**GameObject:** `RTS Environment`

- Componentes: `RTSEnvironmentSetup`, `WeatherManager`, `RTSPostProcessingSetup`, `GridVisibility`.
- Creado en SampleScene vía MCP. Opcional: en el mismo objeto o en otro, el GameObject **VegetationScatter** con el componente `VegetationScatter`.

---

## Resumen de archivos

| Módulo | Archivo / Asset |
|--------|------------------|
| Lighting | `RTSEnvironmentSetup.cs` |
| Weather | `WeatherManager.cs` |
| Rim | `RTS_RimLighting.shader`, `MAT_RTS_RimLighting.mat` |
| Terrain | `RTS_TerrainBlend.shader`, `MAT_RTS_TerrainBlend.mat` |
| Decals | `BuildingGroundDecal.cs`, `RTS_GroundDecal.shader` |
| Unit shadow | `UnitGroundShadow.cs` |
| Vegetation | `VegetationScatter.cs` |
| Grid | `GridVisibility.cs`, `GridGizmoRenderer.cs` |
| Post | `RTSPostProcessingSetup.cs` |
| Scene | GameObject **RTS Environment** en SampleScene |

---

## Objetivos visuales

- Luz cálida tipo RTS y sombras más marcadas.
- Profundidad atmosférica (fog).
- Variación de terreno (grass/dirt/rock).
- Siluetas más legibles (rim lighting).
- Suelo más natural alrededor de edificios (decals).
- Unidades ancladas al suelo (sombra bajo los pies).
- Vegetación variada y grid solo en modo construcción.
- Menos aspecto “prototipo”, optimizado para mapas grandes.
