# PMG Unified World Pipeline (UPM)

Paquete UPM autocontenido con:

- **Motor:** `MapGenerator` + hidrología (ríos, lagos, terreno, ciudades, caminos, recursos)
- **Generación Alpha:** regiones, macro-relieve, clasificación
- **Herramienta UWP:** ventana editor, runner, evaluación de calidad, compilador de perfil
- **Contenido:** shaders WOR, materiales río/lago, terrain layers, texturas, config por defecto

## Instalación

En `Packages/manifest.json`:

```json
"com.pmg.unified-world-pipeline": "file:com.pmg.unified-world-pipeline"
```

O desde otro proyecto:

```json
"com.pmg.unified-world-pipeline": "file:../WAR_OF_REALMS/Packages/com.pmg.unified-world-pipeline"
```

**Requisitos:** Unity 2022.3+, URP.

## Uso rápido

1. Menú **PMG → Unified World Pipeline → Create Default Assets**
2. Ventana **Tools → PMG → Unified World Pipeline**
3. **Apply Full To Scene**

## Estructura

```
Runtime/
  MapGenerator/     # Motor procedural (GridSystem, WaterGenerator, …)
  Generation/       # Alpha + perfiles runtime
  UWP/              # Config, compiler, evaluación
Editor/
  UWP/              # Runner, ventana, bootstrap
Content/
  Shaders/          # WOR_RiverWater, WOR_LakeWater, RiverWaterSimple, …
  Materials/        # MAT_WOR_River, MAT_WOR_Lake
  TerrainLayers/    # Grass, Dirt, Rock, Sand
  TerrainTextures/
  DefaultConfig/    # PMGUnifiedWorldPipelineConfig.asset
```

## Salida generada

Los TerrainData y reportes se escriben en `Assets/PMGUnifiedWorldPipelineOutput/` (no dentro del paquete).

## Dependencias opcionales del proyecto host

- `MatchConfigCompiler` permanece en el juego (`Assets/_Project/01_Gameplay/Map/Generation/`) para `RTSMapGenerator` y lobby.
- UWP en modo `uwpIndependentMode` no requiere MatchConfig.

## Materiales Stylized Water 2

El config por defecto puede referenciar materiales SW2 del proyecto host. Para exportación limpia, reasigna en el Pipeline Config:

- `Packages/com.pmg.unified-world-pipeline/Content/Materials/MAT_WOR_River.mat`
- `Packages/com.pmg.unified-world-pipeline/Content/Materials/MAT_WOR_Lake.mat`

## Versión compiler UWP

Ver log `[UWP] Profile compiled | v=…` en consola al generar.
