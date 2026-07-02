using UnityEngine;
using UnityEngine.Serialization;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Modo de adaptacion runtime para materiales de agua visual.</summary>
    public enum WaterMaterialRuntimeMode
    {
        DirectAsset = 0,
        SW2MinimalAdapter = 1,
        SW2ProceduralTranslator = 2,
        WORCustomShader = 3
    }

    /// <summary>Pipeline visual que consume la hidrologia ya generada.</summary>
    public enum WaterVisualPipelineMode
    {
        [Tooltip("Sistema actual estable: lagos por Marching Squares y rios/tributarios por RiverSurfaceMesh.")]
        CurrentSplitLakeMsRiverSurface = 0,
        [Tooltip("Reservado para probar una sola superficie/malla/material con flujo por zonas. Hoy cae al sistema actual.")]
        UnifiedSingleSurfaceExperimental = 1,
        [Tooltip("Legacy: rios y lagos dentro de la misma mascara Marching Squares.")]
        LegacyMarchingSquaresAllWater = 2,
        [Tooltip("Legacy/debug: fallback de chunks cuadriculados. No recomendado para el look final.")]
        LegacyChunkGridFallback = 3,
        [Tooltip("Split MS lagos + ribbon rios como el modo actual, con fusion boca-lago estilo Pruebas (overlap interior, perimeter expand, fade emisarios).")]
        SplitLakeMsRiverWebFusion = 4,
        [Tooltip("WebFusion + boca lago mejorada: mesh recortado en la orilla (sin strip dentro del lago), taper de ancho y fade alineado a 5 nodos.")]
        SplitLakeMsRiverMouthFusion = 5
    }

    /// <summary>Extremos de la malla de superficie de río (solo visual).</summary>
    public enum RiverSurfaceCapMode
    {
        Bevel = 0,
        Round = 1
    }

    /// <summary>Configuración del Generador Definitivo de Mapas. Fuente única de parámetros (sin valores mágicos).</summary>
    [CreateAssetMenu(fileName = "MapGenConfig", menuName = "Map Generator/MapGenConfig", order = 0)]
    public class MapGenConfig : ScriptableObject
    {
        [Header("Grid (solo plantilla en disco)")]
        [Tooltip("En Play, RTSMapGenerator pisa estos valores con el MatchConfig activo. Este asset queda como plantilla interna del generador.")]
        public int gridW = 256;
        [Tooltip("En Play, lo define RTSMapGenerator.height. Este campo en el asset es solo referencia / escenas sin RTS.")]
        public int gridH = 256;
        [Tooltip("En Play, el tamaño de celda lo define el MatchConfig activo, no este número.")]
        public float cellSizeWorld = 2.5f;
        [Tooltip("En Play, se recalcula desde centerAtOrigin y el transform del RTS. Origen (0,0,0) en el asset suele quedar desfasado del terreno centrado.")]
        public Vector3 origin = Vector3.zero;

        [Header("Seed y reintentos")]
        public int seed = 12345;
        [Tooltip("Reintentos máximos si la validación falla (fairness, ciudades conectadas, etc.).")]
        public int maxRetries = 5;

        [Header("Debug / Logs")]
        [Tooltip("Si está activo, el Generador Definitivo imprime logs detallados por fase. Recomendado OFF para optimizar y limpiar consola.")]
        public bool debugLogs = false;
        [Tooltip("Logs [RiverPerf], [RiverEarlyReject], [RiverBuildPerf], [WaterPerfSummary] para hidrología (Fase4 ríos).")]
        public bool debugRiverHydrologyPerf = false;
        [Tooltip("Logs [HydrologyGraph], [RiverHierarchy], [RiverPlacementV2] (grafo hidrológico PR0–PR2).")]
        public bool debugHydrologyNetwork = false;
        [Tooltip("Tras exportar terreno: plano(s) encima del mapa con la máscara de humedad (escala de grises).")]
        public bool debugTerrainMoisture = false;
        [Tooltip("Tras exportar terreno: plano con el ruido macro usado en el splat (antes del reparto grass/dirt).")]
        public bool debugTerrainMacro = false;
        [Tooltip("Tras exportar terreno: plano con la mezcla grass / grass seco (0=verde, 1=seco).")]
        public bool debugTerrainGrassDry = false;
        [Tooltip("Permite mostrar los planos _TerrainSplatDebug durante Play Mode. Mantener apagado salvo depuracion puntual.")]
        public bool debugTerrainSplatOverlayInPlay = false;

        [Header("Regiones / biomas")]
        [Tooltip("Cantidad aproximada de macro-regiones para regionId/biomeId.")]
        public int regionCount = 8;
        public float regionNoiseScale = 0.02f;

        [Header("Agua (ríos y lagos en grid)")]
        [Range(0f, 1f)] public float waterHeight01 = 0.24f;
        public int riverCount = 3;
        public int lakeCount = 2;
        [Tooltip("Máximo de celdas por lago (flood fill).")]
        public int maxLakeCells = 360;
        [Tooltip("Si hay ríos colocados, no sembrar lago más cerca que N (Chebyshev) de la centerline del río principal.")]
        public bool lakeValidateSeparationFromMainRiver = true;
        [Range(4, 32)] public int lakeMinChebyshevDistanceFromMainRiverCells = 26;
        [Tooltip("Logs [LakeRiverSeparation] al colocar lagos.")]
        public bool debugLakeRiverSeparationLog = false;

        [Header("UWP — herramienta independiente (runtime)")]
        [Tooltip("Anchos mesh/carve = riverVisualRibbonFullWidthCells*; ignora escala dendrítica visual.")]
        public bool uwpOwnedVisualPolicy = false;
        [Tooltip("lakeCount/maxLakeCells del autor sin caps lobby ni mapa 256.")]
        public bool ignoreLobbyHydrologyCaps = false;

        [Header("Río principal — anclas y patrones (hidrología)")]
        [Tooltip("Si true, el fallback borde↔borde sigue disponible tras intentar anclas interiores.")]
        public bool riverMainAllowBorderToBorder = true;
        [Tooltip("Peso relativo del fallback borde↔borde (bajo = pocos ríos solo-borde).")]
        [Range(0f, 1f)] public float riverMainBorderToBorderWeight = 0.12f;
        [Range(0f, 1f)] public float riverMainInteriorSourceWeight = 0.6f;
        [Range(0f, 1f)] public float riverMainLakeSinkWeight = 0.2f;
        [Tooltip("Si true, se permiten patrones con origen en borde real (BorderToLake, BorderToInteriorBasin, etc.).")]
        public bool riverMainAllowBorderStart = true;
        [Tooltip("Peso relativo de patrones con inicio en borde (no incluye BorderToBorder; ver riverMainBorderToBorderWeight).")]
        [Range(0f, 1f)] public float riverMainBorderStartWeight = 0.2f;
        [Tooltip("Distancia mínima Chebyshev al borde para fuentes interiores (alta/montaña/cuenca).")]
        [Range(4, 160)] public int riverMainMinSourceDistanceFromBorderCells = 24;
        [Tooltip("Distancia máxima al borde para clasificar fuentes interiores (anillo).")]
        [Range(8, 200)] public int riverMainMaxSourceDistanceFromBorderCells = 96;
        [Tooltip("Inset desde el borde físico al elegir BorderExit. 0 = celda de borde real (x==0, y==0, etc.).")]
        [Range(0, 48)] public int riverMainBorderExitInsetCells = 0;
        [Tooltip("Máximo de celdas que TryExtendPathTowardTrueMapEdge puede añadir tras A*. 0 = desactivar extensión BFS al borde.")]
        [Range(0, 32)] public int riverMainMaxBorderPathExtensionCells = 0;
        [Tooltip("0 = automático: redondeo(min(W,H)×0.45).")]
        public int riverMainMinPathCells = 0;
        [Tooltip("0 = automático: redondeo((W+H)×1.25).")]
        public int riverMainMaxPathCells = 0;
        [Tooltip("Si lakeCount==0, priorizar patrón BorderToBorder para evitar fuentes muy interiores.")]
        public bool riverMainPreferBorderToBorderWhenNoLake = true;
        [Tooltip("Ruta principal: pathCells / diag(mapa) mínimo antes de aceptar (solo main).")]
        [Range(0.25f, 0.75f)] public float riverMainMinPathToMapDiagRatio = 0.55f;
        [Tooltip("Si lakeCount==0 y la fuente queda lejos del borde, reintentar otro par/patrón.")]
        public bool riverMainRetryIfSourceTooFarFromBorder = true;
        [Tooltip("Chebyshev máximo del origen al borde cuando lakeCount==0.")]
        [Range(0, 24)] public int riverMainMaxSourceDistanceFromBorderWhenNoLakeCells = 6;
        [Tooltip("Emitir [RiverRouteLengthAudit] y [RiverEndpointPolicy] en ruta principal.")]
        public bool riverMainEndpointAuditEnabled = true;

        [Header("Río principal — forma de ruta (grid, A*)")]
        [Tooltip("Si straightnessRatio supera este valor, la ruta se considera demasiado recta y puede forzarse reshape orgánico.")]
        [Range(0.45f, 0.92f)] public float riverMainMaxAcceptedStraightnessRatio = 0.64f;
        [Tooltip("Máximo de celdas consecutivas en la misma dirección aceptado sin reshape orgánico.")]
        [Range(4, 64)] public int riverMainMaxStraightRunCells = 18;
        [Tooltip("Si true: rutas demasiado rectas intentan reshape por cuencas / waypoints antes de aceptar la ruta directa.")]
        public bool riverMainForceOrganicReshape = true;
        [Tooltip("Tiempo extra máximo (ms) para intentos de reshape orgánico del río principal.")]
        [Range(10f, 200f)] public float riverMainOrganicReshapeBudgetMs = 80f;
        [Tooltip("Si max(W,H) ≥ este valor, en reshape orgánico se intenta primero con 2 waypoints (luego 1).")]
        [Range(96, 512)] public int riverMainOrganicLargeMapMinCells = 192;
        [Tooltip("En A* del río principal: tras esta corrida recta acumulada, sumar coste extra suave por celda adicional.")]
        [Range(3, 24)] public int riverMainStraightRunCostStartCells = 8;
        [Tooltip("Multiplicador del coste extra por celda recta por encima del umbral (río principal).")]
        [Range(0.02f, 0.35f)] public float riverMainStraightRunCostMul = 0.08f;

        [Tooltip("Reservado (compatibilidad con assets); el trazado orgánico usa Bezier. Ignorado en la generación actual.")]
        [Range(0.55f, 0.92f)] public float riverTowardExitStepChance = 0.72f;
        [Tooltip("Reservado (compatibilidad con assets); ignorado en la generación actual.")]
        [Range(1.2f, 3.5f)] public float riverMeanderLengthMultiplier = 2.05f;
        [Tooltip("Legacy / compatibilidad: el trazado procedural ya no usa Bezier; meandro = riverMacroBendStrength y riverMacroBendFrequency.")]
        [Range(0.03f, 0.28f)] public float riverBezierLateral01 = 0.1f;
        [Tooltip("Muestras por celda de distancia en el trazado curvo. Más muestras = menos escalones y giros más suaves.")]
        [Range(1.2f, 6f)] public float riverCurveSamplesPerCellDist = 2.85f;
        [Tooltip("Marching Squares: inicio de caída del campo dentro de cada celda River (más bajo = meseta más ancha, más estable tras blur). 0 = desactiva suavizado interno y usa 1.0 en río (solo compat / depuración).")]
        [Range(0f, 0.78f)] public float riverMsCellSoftStart01 = 0.44f;
        [Tooltip("Tras blur MS: mínimo (iso + este valor) en muestras cuya celda es River. Evita que el río desaparezca por debajo del iso; 0 = desactivar (compat).")]
        [Range(0f, 0.22f)] public float riverMsMinAboveIsoAfterBlur = 0.13f;
        [Tooltip("Profundidad del lecho del río en altura 0–1 respecto al nivel del agua (lagos no se ven afectados). Más valor = cauce más bajo que la superficie del agua, mejor continuidad visual.")]
        [Range(0.004f, 0.14f)] public float riverBedDepthBelowWater01 = 0.04f;
        [Tooltip("Profundidad del lecho de tributarios bajo waterHeight01. Suele necesitar mas profundidad visual que el rio principal por su menor ancho.")]
        [Range(0.004f, 0.14f)] public float tributaryBedDepthBelowWater01 = 0.052f;
        [Tooltip("Profundidad maxima del lecho de lagos bajo waterHeight01. Corrige lagos visualmente blancos por poca profundidad.")]
        [Range(0.004f, 0.16f)] public float lakeBedDepthBelowWater01 = 0.082f;
        [Tooltip("Distancia desde la orilla hasta alcanzar la profundidad maxima del lago, en celdas.")]
        [Range(1f, 24f)] public float lakeBedDepthRampCells = 8f;
        [Tooltip("Profundidad minima cerca de la orilla del lago para evitar lectura totalmente somera.")]
        [Range(0f, 0.05f)] public float lakeBedMinDepthBelowWater01 = 0.016f;
        [Tooltip("Grosor base del río en celdas (radio entero). 0 = solo eje 1 celda. Expansión en disco alrededor del eje si riverExpandEuclidean; si no, cuadrado Chebyshev.")]
        [Range(0, 6)] public int riverWidthRadiusCells = 2;
        [Tooltip("Variación ± del radio a lo largo del eje (determinista por índice). 0 = ancho uniforme.")]
        [Range(0, 3)] public int riverWidthNoiseAmplitudeCells = 1;
        [Tooltip("Celdas de río absorbidas como agua desde el borde del lago (boca ancha y orgánica). 0 = desactivar.")]
        [Range(0, 8)] public int lakeRiverMouthBlendCells = 5;
        [Tooltip("Si true, el ensanche del río usa distancia euclídea en el grid (disco); si false, cuadrado Chebyshev (más ortogonal).")]
        public bool riverExpandEuclidean = true;

        [Header("Agua — lago forma orgánica (grid + marching squares)")]
        [Tooltip("Más alto = bordes del flood fill más irregulares (menos manchas rectangulares).")]
        [Range(0f, 1f)] public float lakeOrganicIrregularity = 0.12f;
        [Tooltip("Escala orgánica del tamaño de lagos. 1 = tamaño base; >1 lagos más presentes.")]
        [Range(0.75f, 1.8f)] public float lakeSizeScale = 1f;
        [Tooltip("Semillas iniciales extra en radio Chebyshev alrededor del centro (lagos con muescas y bultos).")]
        [Range(0, 10)] public int lakeExtraSeedSpreadCells = 0;
        [Tooltip("Ruido Perlin en el campo MS antes del blur; orillas menos rectas. 0 = desactivar.")]
        [Range(0f, 0.28f)] public float lakeShoreMsNoiseAmplitude = 0.008f;
        [Tooltip("Escala del ruido en espacio mundo (más bajo = ondulación más amplia en la orilla).")]
        [Range(0.015f, 0.45f)] public float lakeShoreMsNoiseScale = 0.06f;

        [Header("Agua — lago, confluencias y profundidad (gameplay)")]
        [Tooltip("Las celdas River que tocan lago (8 vecinos) pasan a Water: mismo cuerpo que el MS del lago y sin ribbon cruzando el lago.")]
        public bool mergeRiverCellsTouchingLake = true;
        [Tooltip("Crea pequeños emisarios visuales desde algunos lagos hacia el río más cercano cuando la distancia es razonable.")]
        public bool lakeConnectToNearestRiver = true;
        [Range(0, 8)] public int lakeRiverConnectorMaxPerMap = 4;
        [Range(8, 96)] public int lakeRiverConnectorMaxDistanceCells = 54;
        [Tooltip("Celdas de agua a distancia geodésica ≥ este valor desde la orilla (tierra) se marcan infranqueables. 0 = desactivar.")]
        [Range(0, 24)] public int lakeDeepImpassableMinDistanceFromShore = 0;

        [Header("Río — campo visual continuo (MS, no gameplay)")]
        [Tooltip("Mezclar en Marching Squares un campo por distancia al eje Bezier (menos pegado a la grilla).")]
        public bool riverVisualUseContinuousField = true;
        [Tooltip("Radio interior del cauce en celdas (mitad del ancho aproximado donde el campo ya es alto).")]
        [Range(0.12f, 2f)] public float riverVisualHalfWidthCells = 0.55f;
        [Tooltip("Ancho de transición suave más allá del radio interior (celdas).")]
        [Range(0.05f, 1.5f)] public float riverVisualSoftnessCells = 0.42f;
        [Tooltip("Peso máximo del campo continuo [0–1] mezclado con la máscara por celda.")]
        [Range(0f, 1f)] public float riverVisualFieldStrength = 1f;
        [Tooltip("Separación mínima entre puntos del eje Bezier muestreado (celdas). Más bajo = polilínea más densa.")]
        [Range(0.04f, 0.55f)] public float riverVisualSampleSpacingCells = 0.17f;

        [Header("Río — malla continua (ribbon, solo visual)")]
        [Tooltip("Si true: el río es una malla ribbon sobre el Bezier; el marching squares solo cubre lagos (Water). Si false: el MS incluye celdas River como antes.")]
        public bool riverVisualUseContinuousMesh = true;
        [Tooltip("Si true: Marching Squares incluye celdas River (modo cuadriculado). Si false y riverVisualUseContinuousMesh: el cauce va por ribbon y MS solo lagos. Default false = ribbon activo en configs antiguas sin el campo.")]
        public bool riverVisualRenderRiverAsMarchingSquaresCells = false;
        [Tooltip("Mitad del ancho del cauce en mundo (ribbon); base RTS. Variación: Perlin + riverRibbonWidthVariation. Si riverVisualRibbonFullWidthCellsMain > 0, el ancho principal se toma de celdas.")]
        [Range(0.12f, 16f)] public float riverVisualMeshHalfWidth = 2.15f;
        [Tooltip("Ancho visual completo del ribbon del río principal en celdas (~2.2–3.2). 0 = usar solo riverVisualMeshHalfWidth en mundo.")]
        [Range(0f, 4f)] public float riverVisualRibbonFullWidthCellsMain = 2.75f;
        [Tooltip("Ancho visual completo del tributario en celdas (~1.2–2.0). 0 = igual que main / fallback a riverVisualMeshHalfWidth.")]
        [Range(0f, 3f)] public float riverVisualRibbonFullWidthCellsTributary = 1.55f;
        [Tooltip("Separación entre muestras del eje del río en unidades mundo (más bajo = curva más suave).")]
        [Range(0.06f, 4f)] public float riverVisualSampleSpacing = 0.4f;
        [Tooltip("Reduce el ancho del ribbon respecto al nominal para evitar solape con orillas o lagos.")]
        [Range(0f, 3f)] public float riverVisualBankInset = 0f;
        [Tooltip("Solo malla surface: no generar tributario si tiene menos de N celdas únicas y no toca el corredor del río principal.")]
        [Range(2, 64)] public int riverVisualMinDetachedPatchCells = 8;
        [Tooltip("Radio Chebyshev (celdas) alrededor del eje del río principal para considerar conectado un tributario.")]
        [Range(1, 8)] public int riverVisualMainRiverCorridorCells = 2;
        [Tooltip("Ribbon surface: longitud mínima de la centerline (suma de tramos en celdas) para renderizar tributario; 0 = usar default runtime.")]
        [Range(0, 256)] public int riverVisualMinSurfacePieceLengthCells = 18;
        [Tooltip("Ribbon surface: mínimo de celdas únicas ocupadas por el tributario; 0 = usar default runtime.")]
        [Range(0, 256)] public int riverVisualMinSurfacePieceAreaCells = 12;
        [Tooltip("Ribbon/MS cleanup: radio Chebyshev al eje del río principal para considerar corredor (protege tributarios conectados).")]
        [Range(1, 16)] public int riverVisualMainCorridorKeepDistanceCells = 3;
        [Tooltip("Protección visual alrededor de celdas River con vado (Chebyshev); no suprimir ni recortar ribbon cerca del vado.")]
        [Range(1, 24)] public int riverVisualFordKeepDistanceCells = 5;
        [Tooltip("Solo MS de lagos: suprimir en la máscara coarse componentes Water aisladas con menos de N celdas (no modifica CellData).")]
        [Range(1, 200)] public int lakeVisualMinPatchCells = 20;
        [Tooltip("Componentes Water con al menos estas celdas no se tratan como charco errante (preservación MS visual).")]
        [Range(8, 256)] public int lakeVisualPreserveMinCells = 40;
        [Tooltip("Charcos Water cerca del río (Chebyshev a River) con a lo sumo N celdas: suprimir en máscara MS (solo visual).")]
        [Range(4, 48)] public int riverVisualStrayPoolMaxCells = 18;
        [Tooltip("Distancia Chebyshev a cualquier celda River para considerar 'cerca del corredor del río' en limpieza MS.")]
        [Range(1, 10)] public int riverVisualStrayPoolRiverChebyshevCells = 4;
        [Tooltip("Solo ribbon de río: sube la malla en Y (mundo) para alinearla con la orilla; el lecho del terreno suele quedar más bajo que el nivel de agua global.")]
        [Range(0f, 2.5f)] public float riverRibbonVerticalLiftWorld = 0.34f;
        [Tooltip("Extra Y mundo sobre el agua MS para evitar z-fighting del ribbon (delgado).")]
        [Range(0f, 0.25f)] public float riverRibbonAntiZFightYOffsetWorld = 0.035f;
        [Tooltip("Si true: río visual por cinta simple (RiverSurfaceMeshBuilder). Si false: ribbon legacy (Catmull/Laplacian en WaterMeshBuilder).")]
        public bool riverVisualUseRiverSurfaceMeshStrip = true;

        [Header("Río — máscara visual final (surface mesh, sin gameplay)")]
        [Tooltip("Cache único mesh+máscara+terreno desde RiverCenterlinesCellSpace (no recalcular por consumidor).")]
        public bool riverVisualSurfaceCacheEnabled = true;
        [Tooltip("Máx. desviación (celdas) de la centerline visual respecto al camino funcional.")]
        [Range(0.2f, 2f)] public float riverVisualMaxPathDeviationCells = 0.85f;
        [Tooltip("Margen Chebyshev al cull de triángulos fuera de RiverVisualSurfaceMask.")]
        [Range(0, 3)] public int riverVisualTriangleCullMaskMarginCells = 1;
        [Tooltip("Radio extra en celdas al rasterizar la cinta (centerline + halfWidth) sobre RiverVisualSurfaceMask.")]
        [Range(0f, 1.5f)] public float riverVisualRasterMaskExtraCellMargin = 0.35f;
        [Tooltip("Si true: antes del MS de lagos, elimina charcos que no tocan la máscara visual, vado ni lago real.")]
        public bool riverVisualMaskCleanupEnabled = true;
        [Range(1, 24)] public int riverVisualMaskKeepFordDistanceCells = 5;
        [Tooltip("Techo de celdas por componente para seguir evaluando borrado (seguridad; el borrado real depende de preservación).")]
        [Range(4, 512)] public int riverVisualMaskRemoveDetachedPatchMaxCells = 60;
        [Tooltip("Componentes Water con al menos estas celdas = lago real (no borrar solo por falta de máscara si tocan cuerpo de lago).")]
        [Range(8, 2000)] public int lakeVisualRealLakeMinCells = 45;
        [Tooltip("Mínimo de celdas por componente para crear mesh Lake MS.")]
        [Range(8, 2000)] public int lakeMSMinComponentCells = 45;
        [Tooltip("Elimina componentes MS a esta distancia Chebyshev de River o RiverVisualSurfaceMask.")]
        [Range(1, 16)] public int lakeMSRemoveNearRiverDistanceCells = 2;
        [Tooltip("Expansión visual de orilla del lago (celdas), solo si no toca río/máscara de río.")]
        [Range(0, 2)] public int lakeMSShoreExpandCells = 0;
        [Tooltip("Expande solo el contorno geometrico del Lake MS hacia tierra, en unidades mundo. Valores 3-5 imitan mejor un plane que se mete bajo el terreno.")]
        [Range(0f, 6f)] public float lakeMSPerimeterExpandWorld = 4f;

        [Tooltip("Limpieza MS final por RiverVisualSurfaceMask (charcos laterales / tiras sueltas).")]
        public bool riverVisualFinalCleanupEnabled = true;
        [Range(4, 512)] public int riverVisualFinalCleanupMaxPatchCells = 80;
        [Range(1, 16)] public int riverVisualFinalCleanupNearRiverCells = 5;
        [Range(1, 24)] public int riverVisualFinalCleanupKeepFordDistanceCells = 5;

        [Header("Río — salida visual al borde del mapa (surface mesh)")]
        [Tooltip("Extensión en borde: max(legacy, clamp(mul×anchoTotal, 1.5×ancho, 3×ancho)) en mundo. 0 = solo riverSurfaceExtendBorderExitVisualCells.")]
        [Range(0f, 4f)] public float riverSurfaceExtendBeyondMapWidthMul = 2f;
        [Tooltip("Si true: extremos en borde del mapa sin caps/bevel (corte plano).")]
        public bool riverSurfaceDisableBorderCaps = true;

        [Header("Río — tallado terreno alineado a máscara visual")]
        [Tooltip("Si true y hay máscara visual: tallar cauce usando RiverVisualSurfaceMask (no solo celdas River).")]
        public bool riverVisualTerrainCarveEnabled = true;
        [Range(0, 4)] public int riverVisualTerrainCarveExtraCells = 1;
        [Range(0, 8)] public int riverVisualTerrainBankFalloffCells = 3;
        [Range(1f, 1.5f)] public float riverVisualTerrainCenterDepthMul = 1.15f;
        [Range(0.35f, 1f)] public float riverVisualTerrainBankSoftness = 0.65f;

        [Tooltip("Escala V de UV en la superficie de río (distancia acumulada * escala).")]
        [Range(0.005f, 0.2f)] public float riverSurfaceMeshUvScale = 0.042f;
        [Tooltip("Y extra mundo solo para malla de superficie simple (subir si z-fight con terreno).")]
        [Range(0f, 0.25f)] public float riverSurfaceMeshExtraYOffsetWorld = 0.12f;
        [Tooltip("Multiplicador de ancho solo en malla del troncal UWP (no ensancha máscara/carve).")]
        [Range(1f, 2.5f)] public float riverSurfaceMainMeshOnlyWidthMul = 1f;
        [Tooltip("Multiplicador de ancho solo en malla de tributarios UWP (no ensancha máscara/carve).")]
        [Range(1f, 2.5f)] public float riverSurfaceTributaryMeshOnlyWidthMul = 1f;
        [Tooltip("Offset Y local del GO Water_RiverSurface_Main (metros).")]
        [Range(-1f, 1f)] public float riverSurfaceMainRootYOffsetWorld = 0f;
        [Tooltip("Debug: material plano azul semitransparente (sin foam/normal/scroll) para aislar artefactos del shader.")]
        public bool riverSurfaceDebugFlatMaterial = false;

        [Header("Río — superficie mesh (centerline orgánica, solo visual)")]
        [Tooltip("Pases de Chaikin (0–2) sobre la centerline en espacio celda. Se omite si genera autointersección.")]
        [Range(0, 2)] public int riverSurfaceChaikinPasses = 1;
        [Tooltip("Espaciado objetivo entre puntos al remuestrear por longitud de arco (en celdas).")]
        [Range(0.35f, 2.5f)] public float riverSurfaceSampleSpacingCells = 1f;
        [Tooltip("Techo de puntos visuales = ceil(celdas del path × este ratio).")]
        [Range(1.05f, 2f)] public float riverSurfaceMaxVisualPointRatio = 1.35f;
        [Tooltip("Pases de suavizado solo sobre vértices left/right (no mueve extremos).")]
        [Range(0, 3)] public int riverSurfaceEdgeSmoothPasses = 1;
        [Tooltip("Fuerza del suavizado de bordes (0 = sin efecto). Recomendado 0.25–0.35.")]
        [Range(0f, 1f)] public float riverSurfaceEdgeSmoothStrength = 0.25f;
        [Tooltip("Amplitud relativa del ruido de ancho en río principal (≈1±amp). Recomendado 0.06–0.10 solo visual.")]
        [Range(0f, 0.25f)] public float riverSurfaceWidthNoiseAmpMain = 0.09f;
        [Tooltip("Amplitud relativa del ruido de ancho en tributarios.")]
        [Range(0f, 0.2f)] public float riverSurfaceWidthNoiseAmpTributary = 0.06f;
        [Tooltip("Escala del Perlin a lo largo de la distancia acumulada en mundo (m⁻¹ aprox.).")]
        [Range(0.005f, 0.2f)] public float riverSurfaceWidthNoiseScale = 0.035f;
        [Tooltip("Forma del tapón en inicio/fin del cauce (el builder fuerza Bevel para evitar cuñas redondas grandes).")]
        public RiverSurfaceCapMode riverSurfaceCapMode = RiverSurfaceCapMode.Bevel;
        [Tooltip("Segmentos a lo largo del arco cuadrático del tapón redondo (pocos tris).")]
        [Range(2, 8)] public int riverSurfaceRoundCapSegments = 4;
        [Tooltip("Longitud del bisel en celdas (modo Bevel); en runtime se clampa a 0.25–0.65 y al ancho/celda.")]
        [Range(0.25f, 0.65f)] public float riverSurfaceBevelCapLengthCells = 0.45f;
        [Tooltip("Si true: el tributario no recibe tapón en el extremo de confluencia (solo inicio).")]
        public bool riverSurfaceSkipTributaryConfluenceCap = true;
        [Tooltip("Activa ancho visual dedicado para tributarios (no afecta río principal).")]
        public bool riverSurfaceTributaryWidthFixEnabled = true;
        public bool riverSurfaceTributaryWidthDebugLogs = true;
        [Tooltip("Ancho normal tributario = baseHalfWidth × este factor (~2 = doble del cauce estrecho previo).")]
        [Range(1f, 3f)] public float riverSurfaceTributaryVisualWidthMul = 2f;
        [Range(1f, 3f)] public float riverSurfaceTributaryMinWidthMul = 1.75f;
        [Range(1f, 3.5f)] public float riverSurfaceTributaryMaxWidthMul = 2.35f;
        [Tooltip("Legacy estrecho; ignorado si riverSurfaceTributaryWidthFixEnabled.")]
        [Range(0.35f, 1f)] public float riverSurfaceTributaryWidthMul = 0.55f;
        [Tooltip("Ensanche suave en los últimos tramos antes de la confluencia (1 = sin cambio).")]
        [Range(1f, 1.3f)] public float riverSurfaceTributaryConfluenceApproachWidthMul = 1.08f;
        [Range(4, 12)] public int riverSurfaceTributaryConfluenceApproachCells = 8;
        [Tooltip("Radio de tallado visual bajo tributario = halfWidth visual × este factor.")]
        [Range(1f, 1.25f)] public float riverTributaryTerrainCarveRadiusMul = 1.12f;
        [Tooltip("Celdas de taper visual antes de unir al río principal.")]
        [Range(4, 16)] public int riverConfluenceVisualBlendLengthCells = 8;
        [Range(0.3f, 1f)] public float riverConfluenceTributaryEndWidthMul = 0.65f;
        [Tooltip("Baja levemente el extremo del tributario bajo el mesh del principal.")]
        public bool riverConfluenceHideLastSegmentUnderMain = true;
        [Tooltip("Profundidad del bulbo del tapón redondo en fracción del ancho medio.")]
        [Range(0.35f, 1.4f)] public float riverSurfaceRoundCapBulgeMul = 0.95f;
        [Tooltip("Ángulo entre tramos consecutivos (grados) por encima del cual se inserta un punto de suavizado.")]
        [Range(25f, 120f)] public float riverSurfaceSharpBendAngleDeg = 70f;

        [Header("Río — superficie mesh (borde real / meandro solo visual)")]
        [Tooltip("Extensión extra del centerline en celdas más allá del borde del mapa (solo visual, BorderExit).")]
        [Range(0f, 1.5f)] public float riverSurfaceExtendBorderExitVisualCells = 0.75f;
        [Tooltip("Si true: en el borde real del mapa se acorta u omite el cap biselado para que el cauce parezca continuar fuera.")]
        public bool riverSurfaceSkipCapAtMapBorder = true;
        [Tooltip("Si true: en borde de mapa el corte es plano (sin tapón biselado puntiagudo).")]
        public bool riverSurfaceFlatMapBorderCut = true;
        [Tooltip("Desde este ángulo (°) cerca del inicio/fin se acorta el bisel del cap (menos agresivo en curvas fuertes).")]
        [Range(28f, 95f)] public float riverSurfaceBendCapRelaxAngleDeg = 52f;
        [Tooltip("Variación suave de ancho solo río principal (fracción máxima ±). 0 = desactivar arco.")]
        [Range(0f, 0.12f)] public float riverSurfaceMainArcWidthVarMaxFrac = 0.09f;
        [Tooltip("Frecuencia de la variación de ancho a lo largo del arco (mundo⁻¹). Más bajo = ondulación más lenta.")]
        [Range(0.003f, 0.06f)] public float riverSurfaceMainArcWidthVarInvLengthWorld = 0.012f;
        [Tooltip("Si true: aplica variación de ancho tipo seno suave al río principal (solo visual).")]
        public bool riverSurfaceMainArcWidthVarEnabled = true;
        [Tooltip("Meandro sinusoidal leve sobre la centerline en mundo (no altera pathCells ni grid).")]
        public bool riverSurfaceVisualMeanderEnabled = true;
        [Range(0f, 0.6f)] public float riverSurfaceVisualMeanderAmplitudeCells = 0.22f;
        [Range(2f, 48f)] public float riverSurfaceVisualMeanderFrequencyCells = 14f;
        [Range(0.02f, 0.35f)] public float riverSurfaceVisualMeanderEndFade01 = 0.10f;
        [Tooltip("Separación objetivo entre puntos de la centerline visual (celdas).")]
        [Range(0.75f, 1.25f)] public float riverSurfaceVisualSpacingCells = 1f;
        [Tooltip("Extensión máxima en borde (celdas) antes del clip jugable.")]
        [Range(0f, 0.5f)] public float riverSurfaceBorderExtendMaxCells = 0f;
        [Tooltip("Material plano debug para inspeccionar la cinta (no activar en build final).")]
        public bool riverSurfaceDebugShowWire = false;
        [Tooltip("Unlit plano transparente para diagnosticar artefactos de shader (no cambia geometría).")]
        public bool riverSurfaceDebugForceUnlitFlat = false;
        [Tooltip("Dibuja centerline lógica vs visual (solo editor/debug).")]
        public bool riverSurfaceDebugDrawCenterline = false;
        [Tooltip("Dibuja bordes left/right del ribbon (solo editor/debug).")]
        public bool riverSurfaceDebugDrawEdges = false;
        [Tooltip("Dibuja normales de join en codos (solo editor/debug).")]
        public bool riverSurfaceDebugDrawJoinNormals = false;
        [Tooltip("Desviación máxima (celdas) del suavizado respecto al path lógico.")]
        [Range(0.2f, 1.2f)] public float riverSurfaceMaxSmoothDeviationCells = 0.5f;

        [Header("Río — spline visual sobre path lógico")]
        [Tooltip("Centerline visual Catmull-Rom centrípeta sobre RiverCenterlinesCellSpace (no altera gameplay).")]
        public bool riverSurfaceUseSplineVisualCenterline = true;
        [Range(0.15f, 1.5f)] public float riverSurfaceSplineSampleSpacingCells = 0.4f;
        [Range(0.1f, 2f)] public float riverSurfaceSplineMaxDeviationCells = 1.35f;
        [Tooltip("Ángulo máximo (°) entre muestras consecutivas antes de insertar punto intermedio.")]
        [Range(12f, 45f)] public float riverSurfaceSplineMaxAngleStepDeg = 28f;
        [Range(0f, 2f)] public float riverSurfaceSplineEndpointLockCells = 1f;
        [Range(0f, 4f)] public float riverSurfaceSplineFordLockRadiusCells = 2f;
        [Range(0f, 1f)] public float riverSurfaceSplineTension = 0.5f;
        [Range(0f, 0.35f)] public float riverSurfaceBankNoiseAmpCells = 0.18f;
        [Range(4f, 40f)] public float riverSurfaceBankNoiseLengthCells = 22f;
        [Range(0f, 0.2f)] public float riverSurfaceWidthOrganicVarFrac = 0.14f;
        [Tooltip("Legacy (ignorado): usar riverSurfaceVisualNormalWidthMul / riverSurfaceVisualMaxWidthMul.")]
        [Range(1f, 1f)] public float riverSurfaceMinWidthMul = 1f;
        [Tooltip("Legacy (ignorado): usar riverSurfaceVisualNormalWidthMul / riverSurfaceVisualMaxWidthMul.")]
        [Range(1f, 1f)] public float riverSurfaceMaxWidthMul = 1f;
        [Tooltip("Ancho normal/promedio del ribbon = base × este factor (mínimo local = base).")]
        [Range(1.5f, 3f)] public float riverSurfaceVisualNormalWidthMul = 2f;
        [Tooltip("Ancho máximo del ribbon = base × este factor.")]
        [Range(2f, 4f)] public float riverSurfaceVisualMaxWidthMul = 3f;
        [Tooltip("En vados: no bajar de base × este factor.")]
        [Range(1f, 1.25f)] public float riverSurfaceFordMinWidthMul = 1f;
        [Range(1f, 1.35f)] public float riverSurfaceFordMaxWidthMul = 1.2f;
        [Tooltip("Celdas de taper gradual en extremos interiores (sin cap bevel).")]
        [Range(3, 12)] public int riverSurfaceInteriorEndpointTaperCells = 5;
        [Tooltip("Factor mínimo en taper interior; 1 = no punta (ancho mínimo = base).")]
        [Range(1f, 1.25f)] public float riverSurfaceInteriorEndpointMinWidthMul = 1f;
        [Tooltip("Fracción del ancho hacia el centro (borde interior de orilla).")]
        [Range(0.4f, 0.75f)] public float riverSurfaceInnerBankWidthFrac = 0.58f;
        [Tooltip("Si true: no extender centerline visual hacia fuera del mapa.")]
        public bool riverSurfaceDisableBorderExtension = true;
        [Tooltip("Alias explícito: corte plano en borde del mapa (usa riverSurfaceFlatMapBorderCut si false).")]
        public bool riverSurfaceFlatCutAtMapBorder = true;

        [Tooltip("Si true: logs [RiverRibbonDebug] (puntos, bounds, maxSegment, saltos anormales). Quitar o desactivar tras depurar.")]
        public bool debugRiverRibbonGeometry = false;
        [Tooltip("Debug explícito: permite crear malla visible de River Ribbon. OFF por defecto para usar solo Marching Squares como render principal.")]
        public bool debugRenderRiverRibbonMesh = false;
        [Tooltip("Resumen: ancho ribbon medio/min/max, tallada de terreno, variación (consola).")]
        public bool debugRiverVisualStats = false;

        [Header("Río — relieve visual (terrain export, no gameplay)")]
        [Tooltip("Profundidad extra del cauce en unidades mundo sobre el heightmap. 0 = desactivar tallada visual.")]
        [Range(0f, 2.5f)] public float riverTerrainCarveDepthWorld = 1f;
        [Tooltip("Distancia en celdas desde el borde del río (contacto tierra) hasta máxima tallada.")]
        [Range(1, 28)] public int riverTerrainCarveFalloffCells = 8;
        [Tooltip("Curva del fondo: mayor = más profundidad hacia el centro del cauce.")]
        [Range(0.45f, 3.5f)] public float riverTerrainCarveCenterCurve = 1.35f;
        [Tooltip("En celdas de vado, factor aplicado a la tallada (cauce menos hondo).")]
        [Range(0.08f, 1f)] public float riverTerrainCarveFordMul = 0.18f;

        [Header("Río — perfil transversal carve UWP (canal uniforme)")]
        [Tooltip("Perfil euclídeo tipo campana/U en StampUniformUwpFloorDisk (sustituye disco cuadrado Chebyshev).")]
        public bool uwpCarveEuclideanBellProfileEnabled = true;
        [Tooltip("Fracción del radio con fondo plano del cauce (ancho inferior).")]
        [Range(0.22f, 0.55f)] public float uwpCarveTransverseFlatRatio = 0.38f;
        [Tooltip("Potencia del talud: mayor = orillas más empinadas.")]
        [Range(1.2f, 2.8f)] public float uwpCarveTransverseBankPower = 1.8f;
        [Tooltip("Escala radio carve vs halfWidth mesh — main.")]
        [Range(0.65f, 1.15f)] public float uwpCarveHalfWidthMulMain = 0.92f;
        [Tooltip("Escala radio carve vs halfWidth mesh — tributario.")]
        [Range(0.65f, 1.15f)] public float uwpCarveHalfWidthMulTributary = 0.90f;
        [Tooltip("Variación longitudinal muy sutil del radio carve — main (±).")]
        [Range(0f, 0.08f)] public float uwpCarveLongitudinalRadiusNoiseAmpMain = 0.03f;
        [Tooltip("Variación longitudinal muy sutil del radio carve — tributario (±).")]
        [Range(0f, 0.08f)] public float uwpCarveLongitudinalRadiusNoiseAmpTributary = 0.025f;
        [Tooltip("Escala del ruido longitudinal a lo largo del centerline.")]
        [Range(0.01f, 0.12f)] public float uwpCarveLongitudinalRadiusNoiseScale = 0.04f;

        [Header("Río — desembocadura en borde del mapa (solo terrain export)")]
        [Tooltip("Rebaja terreno bajo la salida/entrada del río en celda de borde real (sin tocar mesh ni ruta).")]
        public bool riverOutletTerrainFixEnabled = true;
        [Range(4, 20)] public int riverOutletTerrainFixLengthCells = 10;
        [Range(0.8f, 2f)] public float riverOutletTerrainFixRadiusMul = 1.15f;
        [Range(1, 8)] public int riverOutletTerrainFixBankFalloffCells = 3;
        [Range(0f, 0.02f)] public float riverOutletTerrainFixMaxHeightAboveWater01 = 0.006f;
        public bool riverOutletTerrainFixOnlyAtMapBorder = true;
        public bool riverOutletTerrainFixDebugLogs = true;

        [Header("Río — último tramo en borde (terrain export, ancho visual)")]
        [Tooltip("Talla el último tramo del río en borde con radio basado en ancho visual del ribbon (no solo celdas lógicas).")]
        public bool riverEndReachTerrainFixEnabled = true;
        [Range(12, 48)] public int riverEndReachTerrainFixLengthCells = 24;
        [Range(0.8f, 2f)] public float riverEndReachTerrainFixRadiusMul = 1.25f;
        public bool riverEndReachTerrainFixUseVisualWidth = true;
        [Range(0f, 0.02f)] public float riverEndReachTerrainFixMaxHeightAboveWater01 = 0.006f;
        public bool riverEndReachTerrainFixDebugLogs = true;

        [Header("Río — confluencias tributario→principal")]
        public bool riverConfluenceEnabled = true;
        [Range(0f, 1f)] public float riverConfluenceChanceForTributary = 0.65f;
        [Range(8, 64)] public int riverConfluenceMinDistanceFromMainEndpointsCells = 24;
        [Range(8, 64)] public int riverConfluenceMinSpacingCells = 32;
        [Range(1, 8)] public int riverConfluenceMergeRadiusCells = 4;
        [Range(10f, 120f)] public float riverConfluenceMaxJoinAngleDeg = 80f;
        [Range(5f, 89f)] public float riverConfluenceMinJoinAngleDeg = 18f;
        [Range(0, 12)] public int riverConfluenceAvoidFordRadiusCells = 6;
        [Range(0f, 0.02f)] public float riverConfluenceTerrainMaxHeightAboveWater01 = 0.006f;
        public bool riverConfluenceDebugLogs = true;
        [Tooltip("Si el ángulo de unión queda fuera de rango, registrar confluencia igual para mesh/terreno.")]
        public bool riverConfluenceAcceptLooseAngle = false;

        [Header("Río — red dendrítica (topología tributarios)")]
        public bool riverDendriticNetworkEnabled = true;
        public bool riverDendriticAuditLogs = true;
        [Range(4, 12)] public int riverTributaryMaxParallelRunCells = 7;
        [Range(0, 8)] public int riverTributaryApproachParallelExtraCells = 4;
        [Range(6, 20)] public int riverTributaryJoinTailCells = 12;
        [Range(8, 48)] public int riverTributaryCandidatesPerSlot = 24;
        [Range(3, 16)] public int riverTributarySourcesPerConfluence = 6;
        [Range(14, 36)] public int riverTributaryShortStreamMinCells = 20;
        [Range(16, 40)] public int riverTributaryShortStreamVisualMinCells = 22;
        [Range(6, 24)] public int riverTributaryProceduralMinCells = 10;
        [Range(24, 96)] public int riverTributaryProceduralMaxSourceDistCells = 72;
        [Range(8, 48)] public int riverTributaryProceduralCandidatesPerSlot = 16;
        [Range(25f, 85f)] public float riverTributaryPreferredJoinAngleMinDeg = 35f;
        [Range(45f, 90f)] public float riverTributaryPreferredJoinAngleMaxDeg = 75f;
        [Range(8, 24)] public int riverTributaryDownstreamBlendCells = 14;
        [Range(0.45f, 0.85f)] public float riverSecondaryWidthRatioToMain = 0.65f;
        [Range(0.25f, 0.60f)] public float riverMediumWidthRatioToMain = 0.45f;
        [Range(0.12f, 0.35f)] public float riverHeadwaterWidthRatioToMain = 0.22f;
        public bool riverOrphanWaterAuditEnabled = true;
        public bool riverOrphanWaterCleanupEnabled = false;

        [Header("Río — tributario recuperación (mapas con lagos)")]
        public bool riverTributaryRecoveryEnabled = true;
        [Tooltip("Solo debug: recovery relaja cruce/paralelismo. Confluence-first debe dejarlo en false.")]
        public bool riverTributaryRecoveryRelaxGeometry = false;
        [Range(16, 64)] public int riverTributaryRecoveryMinLengthCells = 28;
        [Range(4, 24)] public int riverTributaryRecoveryAttempts = 12;
        [Range(80, 320)] public int riverTributaryRecoveryMaxMs = 160;
        [Range(32, 160)] public int riverTributaryAStarMaxGoalCells = 72;
        [Range(120, 400)] public int riverTributaryRouteBudgetMs = 280;
        [Range(4, 12)] public int riverTributaryRouteMaxAttempts = 8;
        [Tooltip("Tras el pase normal, reintenta tributarios no colocados (corredor relajado; prioriza ≥1 bueno).")]
        public bool riverRelaxedMissingTributaryFillPass = false;

        [Header("Río — extremos en borde (mesh, tangente estable)")]
        [Tooltip("Puntos fantasma fuera del mapa (celdas) para calcular tangente en start/end de borde; el mesh se clippea al área jugable.")]
        [Range(0f, 6f)] public float riverSurfaceBorderGhostCells = 2.5f;
        [Tooltip("Ancho mínimo en extremo de borde = baseHalfWidth × este factor (igual start/end).")]
        [Range(1.5f, 3f)] public float riverSurfaceBorderEndpointWidthMul = 2f;

        [Header("Río — borde orgánico (ribbon)")]
        [Tooltip("Jitter lateral en mundo sobre la polilínea antes del strip (evita línea perfecta).")]
        [Range(0f, 1.4f)] public float riverRibbonLateralJitterWorld = 0.38f;
        [Tooltip("Escala del muestreo Perlin para el jitter lateral.")]
        [Range(0.06f, 2f)] public float riverRibbonJitterNoiseScale = 0.62f;

        [Header("Río — ancho Perlin (ribbon)")]
        [Tooltip("Mezcla del patrón ancho = base × (0.8 + Perlin×0.4) a lo largo del curso.")]
        [Range(0f, 1f)] public float riverRibbonPerlinWidthBlend = 1f;
        [Tooltip("Frecuencia del Perlin a lo largo de la longitud acumulada del río (mundo⁻¹).")]
        [Range(0.02f, 0.55f)] public float riverRibbonPerlinWidthFreq = 0.09f;

        [Header("Arena / orilla (splat)")]
        [Tooltip("Transición arena: potencia >1 acerca arena al agua (menos blur visual en la franja).")]
        [Range(1f, 4f)] public float sandShoreFalloffPower = 2.15f;
        [Tooltip("Ruido extra sobre distF de orilla (máscara arena).")]
        [Range(0f, 2.2f)] public float sandShoreExtraDistanceNoise = 0.6f;
        [Tooltip("Contraste hierba/tierra en la franja donde arena mezcla (1 = sin cambio).")]
        [Range(1f, 2.6f)] public float sandSoilContrastNearShore = 1.38f;
        [Tooltip("-1 = usar terrainAlphamapSmoothPasses; ≥0 = máximo de pasadas de suavizado alphamap tras pintar orillas.")]
        [Range(-1, 8)] public int sandShoreAlphamapSmoothCap = 1;

        [Header("Río — centerline procedural (meandro + suavizado)")]
        [Tooltip("Nodos de control macro a lo largo del curso (inicio→fin). Más nodos = más oportunidades de curva.")]
        [Range(3, 18)] public int riverMacroNodeCount = 8;
        [Tooltip("Intensidad del meandro macro (0–1 escalado por min(ancho, alto) del mapa en celdas).")]
        [Range(0f, 1f)] public float riverMacroBendStrength = 0.32f;
        [Tooltip("Frecuencia del meandro principal (ciclos a lo largo del tramo). Valores bajos (~0.5–0.9) = meandros amplios estilo mapa RTS.")]
        [Range(0.25f, 4f)] public float riverMacroBendFrequency = 0.78f;
        [Tooltip("Segunda componente lenta (gran S); ciclos a lo largo del tramo. Complementa al meandro principal.")]
        [Range(0.12f, 1.8f)] public float riverMacroSlowBendFrequency = 0.42f;
        [Tooltip("Peso de la componente lenta respecto a la principal (0 = desactivar).")]
        [Range(0f, 1.2f)] public float riverMacroSlowBendWeight = 0.58f;
        [Tooltip("Cuántas zonas de “más curva / más recto” a lo largo del río (frecuencia en espacio 0–1 del tramo). ~0.7–1.2 = varios tramos alternos.")]
        [Range(0.08f, 3.5f)] public float riverCurvatureSectionFrequency = 0.92f;
        [Tooltip("Contraste entre tramos casi rectos y muy curvos. 0 = curvatura uniforme; ~0.5–0.75 = alternancia clara.")]
        [Range(0f, 1f)] public float riverCurvatureSectionContrast = 0.52f;
        [Tooltip("Escala espacial del ruido lateral de baja frecuencia (suma de senos).")]
        [Range(0.3f, 4f)] public float riverLateralNoiseScale = 1.35f;
        [Tooltip("Fuerza del ruido lateral (fracción de min(ancho, alto) del mapa).")]
        [Range(0f, 0.5f)] public float riverLateralNoiseStrength = 0.1f;
        [Tooltip("Pases Laplacianos sobre la polilínea densa (tras Catmull–Rom).")]
        [Range(0, 14)] public int riverSmoothingPasses = 5;
        [Tooltip("Intensidad de cada pase Laplaciano (0–1).")]
        [Range(0f, 1f)] public float riverSmoothingStrength = 0.36f;
        [Tooltip("Remuestreo uniforme final de la centerline en celdas (riverSampleSpacing). Más bajo = más puntos y curva más suave.")]
        [Range(0.08f, 0.55f)] public float riverCenterlineSampleSpacingCells = 0.2f;
        [Tooltip("Si el ángulo entre segmentos consecutivos es mayor que este valor, se relaja el vértice (riverMaxTurnAnglePerStep equivalente).")]
        [Range(25f, 175f)] public float riverMaxTurnAngleDegrees = 152f;
        [Tooltip("Radio mínimo de curvatura deseado en celdas; 0 = no aplicar.")]
        [Range(0f, 12f)] public float riverMinCurveRadiusCells = 2.4f;
        [Tooltip("En tramos casi rectos, empuja lateralmente los puntos (fracción de min(ancho, alto)). 0 = desactivar.")]
        [Range(0f, 0.35f)] public float riverStraightnessPenalty = 0.09f;
        [Tooltip("Cada N celdas del eje se marca como vado (River transitable, lecho menos profundo). 0 = sin vados. ~22–34 en mapas 200+ para varios cruces.")]
        [Range(0, 80)] public int riverFordEveryCells = 26;
        [Tooltip("Radio Chebyshev alrededor de cada celda de vado: también marca River transitable en el ancho del cauce (útil tras riverWidthRadiusCells). 0 = solo el eje.")]
        [Range(0, 3)] public int riverFordCorridorRadiusCells = 1;
        [Tooltip("Profundidad del lecho en vados (0–1 bajo el nivel del agua). Más bajo = más superficial que el cauce normal.")]
        [Range(0.002f, 0.12f)] public float riverFordDepthBelowWater01 = 0.012f;
        [Tooltip("Capa de terreno bajo el agua en celdas de vado (guijarros/tierra). Si es null, se usa arena como el resto del agua.")]
        public TerrainLayer riverFordBedLayer;

        [Header("Río — cruces entre ríos (Inspector)")]
        [Tooltip("Si true, no coloca un río nuevo si su corredor (eje + ancho) solapa celdas ya usadas por otro río.")]
        public bool riverAvoidCrossingOtherRivers = true;
        [Tooltip("Si true y riverAvoidCrossingOtherRivers, tras agotar el pase estricto se reintenta permitiendo cruces. Si false, no hay segundo pase.")]
        public bool allowFallbackCrossing = true;
        [Tooltip("Reintentos por río (otro borde inicio/salida + variación RNG) antes de descartar ese río.")]
        [Range(4, 96)] public int riverPlacementMaxAttemptsPerRiver = 40;
        [Tooltip("Tras tantos rechazos seguidos por cruce de corredor (evitar cruces), deja de intentar ese río (evita 40× trabajo inútil).")]
        [Range(6, 40)] public int riverCorridorRejectEarlyAbort = 12;
        [Tooltip("Tope global de intentos de colocación de río por GenerateWater. 0 = auto (≈48–96 según tamaño). Superado: relajar pase estricto y log [RiverAttemptBudget].")]
        [Range(0, 500)] public int maxTotalRiverBuildAttempts = 0;
        [Tooltip("Solo diagnóstico: ignora maxTotalRiverBuildAttempts (puede disparar cientos de intentos).")]
        public bool riverDebugUnlimitedBuildAttempts = false;
        [Tooltip("En pase estricto, tras tantos early-reject seguidos, salta al pase con cruces permitidos sin agotar todos los intentos estrictos (si allowFallbackCrossing).")]
        [Range(3, 24)] public int riverEarlyRejectConsecutiveToBreakStrictPass = 7;
        [Tooltip("Si true, al fallar un río se imprime una línea resumida (intentos, rechazos, ms) aunque debugLogs esté en false.")]
        public bool riverLogPlacementFailureSummary = true;
        [Tooltip("Si true y debugLogs, tras cada río colocado con éxito se imprime métricas de intentos/tiempo.")]
        public bool riverLogSuccessfulPlacementMetrics = false;

        [Header("Río — ribbon (ancho en mundo)")]
        [Tooltip("Semiancho mínimo del ribbon como fracción del nominal (tras ruido). Más bajo = más contraste con tramos anchos.")]
        [Range(0.45f, 1f)] public float riverRibbonHalfWidthMinMul = 0.66f;
        [Tooltip("Semiancho máximo del ribbon como fracción del nominal (tras ruido).")]
        [Range(1f, 1.75f)] public float riverRibbonHalfWidthMaxMul = 1.42f;
        [Tooltip("Variación relativa del semiancho del ribbon (0 = ancho fijo). Ruido suave a lo largo del curso.")]
        [Range(0f, 0.55f)] public float riverRibbonWidthVariation = 0.32f;
        [Tooltip("Frecuencia del ruido de ancho (ciclos por unidad de longitud en mundo). Más bajo = tramos anchos/estrechos más largos.")]
        [Range(0.005f, 0.8f)] public float riverRibbonWidthNoiseFreq = 0.075f;

        [Header("Río — ribbon post-proceso (espacio celda)")]
        [Tooltip("Pases Laplacianos sobre la centerline en celdas antes del ribbon. La centerline ya es suave; valores altos la aplastan.")]
        [Range(0, 10)] public int riverRibbonCellSpaceLaplacianPasses = 2;
        [Tooltip("Intensidad Laplaciana en espacio celda.")]
        [Range(0f, 1f)] public float riverRibbonCellSpaceLaplacianAlpha = 0.22f;

        [Header("Río — debug en escena")]
        [Tooltip("Dibuja centerlines en Scene view (requiere MapGenerator en escena tras Generate).")]
        public bool debugDrawRiverPathInScene = false;
        [Tooltip("Color macro (nodos + polilínea de control aproximada).")]
        public bool debugRiverDrawMacro = true;
        [Tooltip("Centerline final suavizada y remuestreada (celdas→mundo).")]
        public bool debugRiverDrawSmoothedCenterline = true;
        [Tooltip("Dibuja gizmos del ribbon visual: puntos de path, segmentos válidos y segmentos descartados por salto anormal.")]
        public bool debugDrawRiverRibbonGizmos = false;
        [Tooltip("Tamaño en mundo de los puntos de path del ribbon en Scene view.")]
        [Range(0.02f, 1.2f)] public float debugRiverRibbonPointSize = 0.12f;

        [Header("Ciudades (CityNodes)")]
        public int cityCount = 4;
        [Tooltip("Distancia mínima entre centros de ciudad en celdas.")]
        public int minCityDistanceCells = 40;
        public int cityRadiusCells = 8;
        [Tooltip("Pendiente máxima en grados para colocar una ciudad.")]
        public float maxCitySlopeDeg = 15f;
        [Tooltip("Celdas de separación mínima entre el borde del área de la ciudad y agua/río (evita ciudades pegadas al agua).")]
        public int cityWaterBufferCells = 2;

        [Header("Caminos")]
        [Tooltip("Ancho del camino en celdas (para carve y roadLevel).")]
        public int roadWidthCells = 2;
        [Range(0f, 1f)] public float roadFlattenStrength = 0.8f;

        [Header("Recursos (rings y fairness)")]
        public Vector2Int ringNear = new Vector2Int(6, 12);
        public Vector2Int ringMid = new Vector2Int(12, 24);
        public Vector2Int ringFar = new Vector2Int(24, 50);
        public int minWoodPerCity = 10;
        public int minStonePerCity = 4;
        public int minGoldPerCity = 4;
        public int minFoodPerCity = 6;
        public int maxResourceRetries = 5;

        [Header("Terrain export (alturas y texturas)")]
        [Tooltip("Altura del terreno en unidades mundo (eje Y). Debe coincidir con Height Multiplier del RTS si se usa desde ahí.")]
        public float terrainHeightWorld = 50f;
        [Tooltip("Resolución del heightmap del Terrain (potencia de 2 + 1).")]
        public int heightmapResolution = 513;

        [Header("Terrain — continuidad visual (solo export Unity Terrain, no grid lógico)")]
        [Tooltip("Pases de suavizado sobre el heightmap tras muestreo bilinear. Equivale a suavizar normales / menos facetas.")]
        [Range(0, 8)] public int terrainNormalSmoothingPasses = 1;
        [Tooltip("Fuerza de cada pase (mezcla hacia el promedio de vecinos 4-conectados).")]
        [Range(0f, 1f)] public float terrainNormalSmoothingStrength = 0.32f;
        [Tooltip("Pases de blur suave sobre alphamaps tras pintar (transiciones grass/dirt/rock menos duras).")]
        [Range(0, 6)] public int terrainAlphamapSmoothPasses = 1;
        [Tooltip("Tinte base multiplicativo sobre pesos de altura antes del splat (1,1,1 = neutro). Ayuda a unificar tono.")]
        public Color terrainBaseColor = Color.white;
        [Tooltip("Empuja zonas altas hacia roca / bajas hacia hierba (0 = desactivar).")]
        [Range(0f, 1f)] public float terrainHeightTintStrength = 0.12f;
        [Tooltip("Empuja pendientes fuertes hacia roca según gradiente del heightmap (0 = desactivar).")]
        [Range(0f, 1f)] public float terrainSlopeTintStrength = 0.22f;
        [Tooltip("Escala del ruido Perlin en espacio alphamap (más bajo = manchas más grandes).")]
        [Range(0.02f, 2f)] public float terrainNoiseScale = 0.35f;
        [Tooltip("Intensidad del ruido sobre la altura normalizada usada para splat (0 = desactivar).")]
        [Range(0f, 0.35f)] public float terrainNoiseStrength = 0.06f;
        [Tooltip("Si no es null, asigna materialTemplate del Terrain tras exportar (URP Terrain/Lit o custom).")]
        public Material terrainMaterialTemplateOverride;

        [Tooltip("Pintar terreno por altura (grass/dirt/rock). Asigna Terrain Layers en el RTS o aquí.")]
        public bool paintTerrainByHeight = true;
        [Tooltip("Capas de terreno para pintar por altura (grass = bajo, dirt = medio, rock = alto).")]
        public TerrainLayer grassLayer;
        public TerrainLayer dirtLayer;
        public TerrainLayer rockLayer;
        [Tooltip("Porcentaje 0–1 del mapa para grass (zonas bajas). Si > 0 se usa con dirt/rock para derivar umbrales.")]
        public float grassPercent01 = 0.6f;
        public float dirtPercent01 = 0.2f;
        public float rockPercent01 = 0.2f;
        [Tooltip("Umbrales derivados o legacy: grass hasta este valor, dirt hasta dirtMaxHeight01.")]
        public float grassMaxHeight01 = 0.6f;
        public float dirtMaxHeight01 = 0.8f;
        [Range(0f, 0.25f)] public float textureBlendWidth = 0.08f;
        [Tooltip("0 = transiciones como hasta ahora; 1 = bordes más duros (menos ancho de mezcla + poco contraste en pesos).")]
        [Range(0f, 1f)] public float terrainBlendSharpness = 0.2f;

        [Header("Terreno — ruido macro (zonas secas / verdes a gran escala)")]
        [Tooltip("Escala del ruido macro en espacio alphamap (más bajo = parches más grandes). 0 = desactivar fuerza.")]
        [Range(0.001f, 0.15f)] public float terrainMacroNoiseScale = 0.012f;
        [Tooltip("Cuánto empuja la altura efectiva usada para grass/dirt/rock (0 = sin efecto).")]
        [Range(0f, 0.45f)] public float terrainMacroNoiseStrength = 0.08f;

        [Header("Terreno — segunda capa de pasto (manchas grandes)")]
        [Tooltip("Capa opcional de hierba más seca; si es null o fuerza 0, no se añade capa.")]
        public TerrainLayer grassDryLayer;
        [Tooltip("Intensidad de la mezcla hacia grassDry (0–1). El patrón es ruido de baja frecuencia, no altura.")]
        [Range(0f, 1f)] public float grassDryBlendStrength = 0.55f;
        [Tooltip("Escala del ruido para manchas de pasto seco (más bajo = manchas más grandes).")]
        [Range(0.002f, 0.08f)] public float grassDryNoiseScale = 0.009f;

        [Header("Terreno — humedad cerca del agua (capa tierra húmeda)")]
        [Tooltip("Tierra vegetal húmeda junto a ríos/lagos; si es null o fuerza 0, se ignora.")]
        public TerrainLayer wetDirtLayer;
        [Tooltip("Distancia máxima en celdas de grid desde agua donde puede aparecer humedad (más amplio que la franja de arena).")]
        [Range(0.5f, 48f)] public float terrainMoistureRadius = 10f;
        [Tooltip("Intensidad máxima con la que la humedad sustituye grass/dirt (0 = desactivar).")]
        [Range(0f, 1f)] public float terrainMoistureStrength = 0.65f;
        [Tooltip("Escala del ruido que rompe el borde de la máscara de humedad.")]
        [Range(0.02f, 1.2f)] public float terrainMoistureNoiseScale = 0.14f;
        [Tooltip("Fuerza del ruido sobre la distancia efectiva a agua (0 = borde suave solo por distancia).")]
        [Range(0f, 1f)] public float terrainMoistureNoiseStrength = 0.35f;

        [Header("Arena en orillas")]
        public TerrainLayer sandLayer;
        [Range(1, 6)] public int sandShoreCells = 3;
        [Tooltip("Escala del ruido que deforma la distancia a orilla (arena invade/pierde terreno de forma irregular).")]
        [Range(0.02f, 0.8f)] public float sandEdgeNoiseScale = 0.22f;
        [Tooltip("Amplitud del ruido en unidades de celda aproximadamente (0 = borde limpio como antes).")]
        [Range(0f, 2.5f)] public float sandEdgeNoiseStrength = 0.85f;

        [Header("Shoreline smoothing (visual)")]
        [Tooltip("Radio en celdas para suavizar el terreno cerca del agua (solo visual al exportar a Terrain).")]
        public int shoreSmoothRadiusCells = 5;
        [Tooltip("Cuánto empuja el terreno hacia la altura del agua en la orilla (0 = nada, 1 = máximo).")]
        [Range(0f, 1f)] public float shoreSmoothStrength = 1f;

        [Header("Agua visual (mesh)")]
        [Tooltip("Selector canonico del sistema visual de agua. Usa Current para produccion; Unified queda reservado para el proximo sistema experimental.")]
        public WaterVisualPipelineMode waterVisualPipeline = WaterVisualPipelineMode.CurrentSplitLakeMsRiverSurface;
        [Tooltip("Solo Unified: expande el borde de la malla en mundo para que el agua se meta bajo el terreno y vuelva a aparecer la linea de orilla.")]
        [Range(0f, 8f)] public float unifiedWaterPerimeterExpandWorld = 1.35f;
        [Tooltip("Solo Unified: ajuste Y propio. Valores bajos/negativos ayudan a intersectar con el terreno y activar la espuma/interseccion del material.")]
        [Range(-0.25f, 0.35f)] public float unifiedWaterSurfaceExtraYOffsetWorld = 0.24f;
        [Tooltip("Solo Unified: levanta la superficie visual en proporcion a la profundidad del lecho. No cambia waterHeight01 logico.")]
        [Range(0f, 0.15f)] public float unifiedWaterSurfaceLiftFromDepthFactor = 0.055f;
        [Tooltip("Solo Unified: ajuste visual del terreno en la orilla respecto a waterHeight01. Negativo mete la orilla bajo el agua; positivo la deja sobre el agua.")]
        [Range(-0.25f, 0.25f)] public float unifiedWaterShoreTerrainOffsetWorld = 0.035f;
        [Tooltip("Solo Unified: ancho en celdas del ajuste heightmap que acompana la misma mascara continua del agua.")]
        [Range(0.25f, 4f)] public float unifiedWaterTerrainBandCells = 1.35f;
        [Tooltip("Solo Unified: cuanto queda el borde interior del lecho bajo la superficie visual del agua.")]
        [Range(0f, 0.45f)] public float unifiedWaterTerrainEdgeSubmergeWorld = 0.09f;
        [Tooltip("Solo Unified: labio minimo de terreno en tierra inmediatamente afuera del agua visual.")]
        [Range(0f, 0.35f)] public float unifiedWaterTerrainBankLipWorld = 0.045f;
        [Tooltip("Solo Unified: suavizado adicional del campo de rio antes de Marching Squares para reducir dientes de sierra.")]
        [Range(0f, 2.5f)] public float unifiedRiverFieldExtraSoftnessCells = 0.95f;
        [Tooltip("Solo Unified: ancho minimo del campo continuo de rio en celdas.")]
        [Range(0.3f, 3f)] public float unifiedRiverFieldMinHalfWidthCells = 1.05f;
        [Tooltip("Solo Unified: dibuja corrientes graficas sobre celdas River/tributarios.")]
        public bool unifiedRiverCurrentsEnabled = true;
        [Range(0.05f, 3f)] public float unifiedRiverCurrentWidthWorld = 0.7f;
        [Range(0.2f, 8f)] public float unifiedRiverCurrentLengthWorld = 2.2f;
        [Range(0.5f, 12f)] public float unifiedRiverCurrentSpacingCells = 3f;
        [Range(0f, 2f)] public float unifiedRiverCurrentAlpha = 0.32f;
        [Range(0f, 4f)] public float unifiedRiverCurrentSpeed = 1.35f;
        [Range(0f, 0.25f)] public float unifiedRiverCurrentYOffsetWorld = 0.035f;
        public int waterChunkSize = 32;
        public float waterSurfaceOffset = 0.05f;
        [Tooltip("Material del ribbon de río (Stylized Water con _RIVER). No usar aquí el material de lago.")]
        [FormerlySerializedAs("waterMaterial")]
        public Material riverWaterMaterial;
        [Tooltip("Como se adapta el material del rio en runtime. SW2ProceduralTranslator conserva el traductor legacy; SW2MinimalAdapter deja el look al material.")]
        public WaterMaterialRuntimeMode riverWaterMaterialMode = WaterMaterialRuntimeMode.DirectAsset;
        [Tooltip("Material opcional para tributarios. Si null, usa riverWaterMaterial.")]
        public Material tributaryWaterMaterial;
        [Tooltip("Como se adapta el material de tributarios en runtime.")]
        public WaterMaterialRuntimeMode tributaryWaterMaterialMode = WaterMaterialRuntimeMode.DirectAsset;
        [Tooltip("Material/shader para lagos (Marching Squares). Si null, se usa Project/Lake Water Simple.")]
        public Material lakeWaterMaterial;
        [Tooltip("Como se adapta el material del lago en runtime.")]
        public WaterMaterialRuntimeMode lakeWaterMaterialMode = WaterMaterialRuntimeMode.DirectAsset;
        [Tooltip("Material reservado para mar/oceano si una escena o pipeline futuro genera ese cuerpo de agua.")]
        public Material seaWaterMaterial;
        [Tooltip("Como se adaptara el material de mar/oceano cuando exista consumidor visual.")]
        public WaterMaterialRuntimeMode seaWaterMaterialMode = WaterMaterialRuntimeMode.SW2ProceduralTranslator;
        [Tooltip("Color base del shader Project/Lake Water Simple cuando no hay material asignado.")]
        public Color lakeWaterBaseColor = new Color(0.16f, 0.48f, 0.74f, 0.88f);
        [Range(0.2f, 1f)] public float lakeWaterAlpha = 0.72f;
        [Tooltip("Offset Y extra solo para malla de lago. Sirve para que el lago no se vea enterrado sin mover los ríos.")]
        [Range(0f, 1.2f)] public float lakeWaterSurfaceExtraOffsetWorld = 0.22f;
        [Tooltip("Color agua poco profunda (orillas / centro del cauce en el shader RTS River Water).")]
        public Color riverWaterShallowColor = new Color(0.32f, 0.64f, 0.82f, 1f);
        [Tooltip("Color agua profunda (centro del cauce).")]
        public Color riverWaterDeepColor = new Color(0.08f, 0.22f, 0.42f, 1f);
        [Tooltip("Desplazamiento UV por segundo (flujo falso) en el shader de río.")]
        public Vector2 riverUVFlowSpeed = new Vector2(0.12f, 0.04f);
        [Tooltip("Suavizado visual del borde del ribbon (UV transversal). Mayor = transición más ancha hacia shallow.")]
        [Range(0.05f, 0.55f)] public float riverBankBlendStrength = 0.22f;
        [Tooltip("Tope duro de longitud de segmento del ribbon en mundo (0 = usar solo heurística interna).")]
        [Range(0f, 50f)] public float riverMaxSegmentLengthWorld = 0f;
        [Tooltip("Transparencia del ribbon de río (0 = opaco). El lago usa lakeWaterAlpha.")]
        [FormerlySerializedAs("waterAlpha")]
        [Range(0.5f, 1f)] public float riverWaterAlpha = 0.88f;
        [Tooltip("Emisarios lago-rio: desvanece el inicio dentro del lago y el final al conectar con el rio principal para ocultar solapes.")]
        public bool riverLakeEmissaryEndpointFadeEnabled = true;
        [Tooltip("Celdas de fade visual al salir del lago en un tributario emisario.")]
        [Range(0f, 24f)] public float riverLakeEmissaryLakeFadeCells = 10f;
        [Tooltip("Celdas de fade visual antes de entrar al rio principal en un tributario emisario.")]
        [Range(0f, 24f)] public float riverLakeEmissaryRiverFadeCells = 7f;
        [Tooltip("Alpha minimo del extremo que nace en el lago. 0 = invisible; valores bajos ocultan mejor el solape bajo el lago.")]
        [FormerlySerializedAs("riverLakeEmissaryEndpointMinAlpha")]
        [Range(0f, 0.6f)] public float riverLakeEmissaryLakeEndpointMinAlpha = 0.02f;
        [Tooltip("Alpha minimo del extremo que conecta con el rio principal. Usar mas alto que el lago para que la union no desaparezca.")]
        [Range(0f, 0.85f)] public float riverLakeEmissaryRiverEndpointMinAlpha = 0.42f;
        [Tooltip("Capa de Unity para el GameObject del agua (0 = Default). -1 = usar 0. Debe estar en la Culling Mask de la cámara.")]
        public int waterLayer = -1;

        [Header("Agua - lecho subacuatico")]
        [Tooltip("Crea una capa visual bajo rios y lagos para que el agua transparente tome color del fondo.")]
        public bool underwaterBedEnabled = true;
        [Tooltip("Material opcional para el lecho. Si queda null se crea uno procedural con shader Project/WOR Submerged Bed URP.")]
        public Material underwaterBedMaterial;
        [Tooltip("Separacion vertical bajo la superficie de agua. Evita z-fighting y mantiene la textura visible a traves del agua.")]
        [Range(0.02f, 0.8f)] public float underwaterBedYOffsetWorld = 0.18f;
        [Tooltip("Escala de UV/world texture del lecho subacuatico.")]
        [Range(0.005f, 0.35f)] public float underwaterBedUvScale = 0.052f;
        [Tooltip("Escala del ruido procedural del lecho.")]
        [Range(0.005f, 0.5f)] public float underwaterBedNoiseScale = 0.055f;
        [Tooltip("Cuanto rompe el color base del lecho.")]
        [Range(0f, 1f)] public float underwaterBedNoiseStrength = 0.58f;
        [Tooltip("Color profundo del lecho de lago.")]
        public Color lakeBedDeepColor = new Color(0.018f, 0.065f, 0.095f, 0.58f);
        [Tooltip("Color somero del lecho de lago, cerca de orillas.")]
        public Color lakeBedShallowColor = new Color(0.24f, 0.205f, 0.145f, 0.10f);
        [Tooltip("Color profundo del lecho de rio.")]
        public Color riverBedDeepColor = new Color(0.035f, 0.095f, 0.12f, 0.42f);
        [Tooltip("Color somero del lecho de rio, cerca de bordes.")]
        public Color riverBedShallowColor = new Color(0.23f, 0.20f, 0.15f, 0.24f);

        [Header("Agua - bordes redondeados (Marching Squares)")]
        [Tooltip("✅ ACTIVAR para bordes orgánicos (elimina esquinas cuadradas). Desactivar solo si quieres agua tipo Minecraft.")]
        public bool waterRoundedEdges = true;
        [Tooltip("Subdivisión por celda (4-5 recomendado). Mayor = bordes más suaves pero más vértices.")]
        [Range(1, 8)] public int waterEdgeSubdiv = 5;
        [Tooltip("Iteraciones de blur (3-4 recomendado para lagos naturales). Más iteraciones = bordes más redondeados.")]
        [Range(0, 8)] public int waterEdgeBlurIterations = 4;
        [Tooltip("Radio del blur. 2 es óptimo para suavizar sin perder definición.")]
        [Range(1, 4)] public int waterEdgeBlurRadius = 2;
        [Tooltip("Nivel de iso. 0.5 es perfecto (no cambiar a menos que quieras lagos más grandes/pequeños).")]
        [Range(0.05f, 0.95f)] public float waterIsoLevel = 0.5f;

        [Header("Agua - post-proceso de máscara (rápido)")]
        [Tooltip("✅ ACTIVAR para eliminar píxeles solitarios y esquinas afiladas ANTES del Marching Squares. Mejora mucho el resultado.")]
        public bool waterMaskPostProcess = true;
        [Tooltip("Iteraciones del suavizado de máscara (2-3 recomendado). Reduce esquinas aisladas.")]
        [Range(0, 8)] public int waterMaskSmoothIterations = 2;
        [Tooltip("Umbral de vecinos. 5 = mayoría (recomendado). Bajar a 4 hace lagos más grandes, subir a 6 los hace más pequeños.")]
        [Range(0, 9)] public int waterMaskSmoothThreshold = 5;
        [Tooltip("Umbral de núcleo de río fusionado (>= bloqueado/no caminable). Mayor = cauce profundo más estrecho.")]
        [Range(0.35f, 0.9f)] public float riverFusionCoreThreshold = 0.55f;
        [Tooltip("Umbral de borde de río fusionado (>= candidato a orilla caminable). Debe ser menor que core.")]
        [Range(0.05f, 0.7f)] public float riverFusionShoreThreshold = 0.25f;
        [Tooltip("Pasadas de blur para fusión de máscara de ríos (2-4 recomendado).")]
        [Range(1, 6)] public int riverFusionBlurPasses = 3;
        [Tooltip("Ancho de franja caminable de orilla (en celdas) medido hacia tierra desde el borde del río.")]
        [Range(0, 2)] public int riverShoreWalkableWidthCells = 1;
        [Tooltip("Debug opcional en SceneView: muestra máscara final de río fusionada, núcleo no caminable y franja de orilla caminable.")]
        public bool debugDrawWaterMaskGizmos = false;
        [Tooltip("Overlay SceneView: campo escalar de la fusión de ríos tras blur (antes de umbrales core/orilla). Independiente de la máscara con core/shore.")]
        public bool debugDrawWaterFusionMask = false;
        [Tooltip("Logs [RiverFusionRemoved] / [RiverFusionPreserved] / [RiverContinuityProtected] al fusionar celdas de río (sin afectar gameplay).")]
        public bool riverFusionContinuityDebug = false;

        [Header("Agua — limpieza topológica (pre-MS, solo grid visual)")]
        [Tooltip("Pasada final tras fusión/orilla, islas de tierra y núcleo profundo de lago: quita micro-islas de agua, puntas y artefactos diagonales antes del Marching Squares.")]
        public bool enableWaterTopologyCleanup = true;
        [Tooltip("Elimina componentes 4-conexos Water/River con menos de N celdas (ford/centerline excluyen todo el componente).")]
        [Range(2, 24)] public int waterTopologyRemoveIslandThresholdCells = 4;
        [Tooltip("Consola: resumen [WaterCleanup] con contadores y ms.")]
        public bool debugWaterTopologyCleanup = false;
        [Tooltip("SceneView: overlay de celdas eliminadas (requiere consumidor que lea WaterGenerator.DebugLastWaterCleanupRemovedPacked).")]
        public bool debugDrawWaterTopologyCleanupGizmo = false;
        [Tooltip("Consola: desglose [WaterGenPerf] de tiempos y contadores BFS/conectividad dentro de GenerateWater (solo diagnóstico; overhead ligero si está OFF).")]
        public bool debugWaterGeneratePerfDiagnostics = false;

        [Header("Agua MS - calidad visual (solo malla, no gameplay)")]
        [Tooltip("Suavizado extra del campo escalar antes del iso (0 = igual que antes). Mayor = orillas más redondeadas (más iteraciones de blur ligero).")]
        [Range(0f, 3f)] public float waterEdgeSmoothness = 1.2f;
        [Tooltip("Desplaza vértices del borde del iso en mundo (perpendicular a la arista). 0 = desactivar.")]
        [Range(0f, 0.45f)] public float waterEdgeNoiseAmplitude = 0.045f;
        [Tooltip("Escala espacial del ruido Perlin en el borde (mundo⁻¹ aprox.).")]
        [Range(0.02f, 0.6f)] public float waterEdgeNoiseScale = 0.18f;
        [Tooltip("Ancho relativo del cauce aguas abajo (según posición en mapa). 1 = uniforme; >1 ensancha hacia una esquina del mapa.")]
        [Range(0.75f, 1.45f)] public float riverWidthDownstreamFactor = 1.12f;
        [Tooltip("Boost de ancho en confluencias (vecinos River en ventana 5×5). 0 = desactivar.")]
        [Range(0f, 0.55f)] public float riverWidthConfluenceBoost = 0.16f;
        [Tooltip("Ruido suave de ancho del campo del río (amplitud efectiva acotada internamente). 0 = desactivar.")]
        [Range(0f, 0.35f)] public float riverWidthNoiseScale = 0.05f;
        [Tooltip("Mezcla extra del campo río↔lago en celdas River tocando Water (evita corte duro). 0 = desactivar.")]
        [Range(0f, 1f)] public float riverLakeVisualBlend = 0.35f;
        [Tooltip("Rango en celdas para mapear profundidad visual (interior → centro oscuro). Mayor = transición más ancha. Legacy: si river/lake shore = 0 se usa este valor.")]
        [Range(1f, 24f)] public float shoreVisualWidth = 7f;
        [Tooltip("Orilla UV en cauce río (más bajo = menos franja tipo lago). ~1.2–1.8.")]
        [Range(0.5f, 8f)] public float riverShoreVisualWidth = 1.55f;
        [Tooltip("Orilla UV en lagos (Marching Squares). ~valor histórico shoreVisualWidth.")]
        [Range(1f, 24f)] public float lakeShoreVisualWidth = 7f;
        [Tooltip("Contraste del gradiente orilla→profundo en UV (1 = lineal).")]
        [Range(0.35f, 3f)] public float shoreVisualBlend = 1.25f;
        [Tooltip("Fuerza del tinte profundo/orilla vía UV.y en Project/RTS River Water (0 = conservar mapeo UV anterior por posición).")]
        [Range(0f, 1f)] public float waterDepthColorStrength = 0.62f;
        [Tooltip("Multiplicador de velocidad del material Stylized Water río (1 = asset, 1.3 = +30%).")]
        [Range(0.25f, 3f)] public float waterUvFlowSpeedScale = 1.5f;
        [Tooltip("Escala planar world-space de UV para la malla de agua (u=x*scale, v=z*scale).")]
        [Range(0.001f, 0.2f)] public float waterUVScale = 0.018f;
        [Tooltip("Genera decoración opcional en pasos de río estrechos (solo visual). Requiere prefab si quieres malla.")]
        public bool enableRiverCrossings = true;
        [Tooltip("Marca vados jugables (walkable) en tramos de río angostos. Independiente de la decoración visual de cruces.")]
        public bool enableFunctionalRiverFords = true;
        [Tooltip("Modo simplificado recomendado: los vados funcionales se derivan SOLO de los cruces visuales elegidos en WaterMeshBuilder (post-río), reduciendo lógica duplicada.")]
        public bool useCrossingAssetFords = true;
        [Tooltip("Máximo de puntos de cruce decorados por mapa.")]
        [Range(0, 32)] public int riverCrossingMaxPerMap = 3;
        [Tooltip("Separación mínima Chebyshev entre cruces (celdas).")]
        [Range(2, 48)] public int riverCrossingMinSpacing = 13;
        [Tooltip("Reservado (no spawnea gameplay): probabilidad 0–1 para futura extensión / mods.")]
        [Range(0f, 1f)] public float riverCrossingResourceChance = 0f;
        [Tooltip("Solo candidatos con ancho estimado ≤ este valor (celdas).")]
        [Range(1, 8)] public int riverCrossingMaxThicknessCells = 2;
        [Tooltip("Radio funcional de vado aplicado alrededor de la celda de cruce (Chebyshev). En modo crossing-assets, 0 se eleva internamente a 2.")]
        [Range(0, 6)] public int riverCrossingFordRadiusCells = 2;
        [Tooltip("Semiancho mínimo funcional del corredor de vado (en celdas), aplicado a lo largo del eje del río.")]
        [Range(1, 6)] public int riverCrossingFunctionalHalfWidthCells = 2;
        [Tooltip("Máximo de celdas para buscar cada orilla desde la celda de cruce (bank-to-bank).")]
        [Range(4, 20)] public int riverCrossingBankSearchCells = 12;
        [Tooltip("Offset vertical visual para decoraciones de cruce (m). Evita que queden hundidas en la malla de agua.")]
        [Range(0f, 1f)] public float riverCrossingDecorYOffset = 0.35f;
        [Tooltip("Debug visual temporal de cruces funcionales (rays/marcadores).")]
        public bool riverCrossingDebugVisuals = false;
        [Tooltip("Logs de diagnóstico para corredores de vado (candidato rechazado, longitud, conectividad, orillas Land).")]
        public bool riverCrossingCorridorDebugLogs = false;
        [Tooltip("Seguridad gameplay: si true, añade vados extra solo si es necesario para conectar spawns por tierra.")]
        public bool riverCrossingExtraForSpawnConnectivity = true;
        [Tooltip("Máximo de vados extra de conectividad (no cuenta contra riverCrossingMaxPerMap).")]
        [Range(0, 8)] public int riverCrossingMaxExtraConnectivityFords = 4;
        [Tooltip("Vados prioritarios donde rutas lógicas (A* con río permitido, caminos Fase6, anclas) cruzan ríos. No cuentan contra riverCrossingMaxPerMap.")]
        public bool riverCrossingEnableStrategicRoadFords = true;
        [Tooltip("Presupuesto máximo de vados estratégicos (independiente de riverCrossingMaxPerMap).")]
        [Range(0, 16)] public int riverCrossingMaxStrategicRoadFords = 6;
        [Tooltip("Cuando mandatoryCreated alcanza todos los ríos aptos, tope de RoadFords (menor coste que riverCrossingMaxStrategicRoadFords). 0 = no recortar.")]
        [Range(0, 8)] public int riverCrossingMaxStrategicRoadFordsAfterMandatory = 2;
        [Tooltip("Anclas del mapa por ciudad para rutas sintéticas (0 = solo spawn→masa principal; 2 = recomendado para rendimiento).")]
        [Range(0, 4)] public int riverCrossingStrategicAnchorCount = 2;
        [Tooltip("Cobertura obligatoria: máximo de vados funcionales forzados por centerline de río (no cuenta contra riverCrossingMaxPerMap).")]
        [Range(0, 2)] public int riverCrossingMaxMandatoryPerRiver = 1;
        [Tooltip("Cobertura obligatoria: spacing mínimo (Chebyshev, celdas) para vados forzados si un río quedaría sin cobertura.")]
        [Range(0, 24)] public int mandatoryRiverFordMinSpacing = 6;
        [Tooltip("Seguridad spawn: mínimo de celdas caminables requeridas en el componente del spawn. Si es menor, se intenta forzar vado extra hacia el componente principal.")]
        [Range(0, 200000)] public int minSpawnWalkableComponentCells = 1500;
        [Tooltip("Seguridad spawn: ratio mínimo (0–1) del componente del spawn respecto al componente caminable más grande. Si es menor, se intenta forzar vado extra.")]
        [Range(0f, 1f)] public float minSpawnWalkableComponentRatio = 0.20f;
        [Tooltip("Longitud mínima de tramo (celdas) para considerar un segmento de río apto para vado.")]
        [Range(4, 256)] public int riverFordMinSegmentLengthCells = 12;
        [Tooltip("Distancia mínima (Chebyshev, celdas) desde una confluencia para permitir un vado.")]
        [Range(0, 64)] public int riverFordMinDistanceFromConfluenceCells = 8;
        [Tooltip("Máximo de vados funcionales por segmento de río.")]
        [Range(1, 3)] public int riverFordMaxPerSegment = 1;
        [Tooltip("Longitud máxima (celdas) de un segmento lógico derivado de centerline. Si se supera, se subdivide para repartir vados en un mismo río.")]
        [Range(8, 512)] public int riverFordMaxSegmentLengthCells = 80;
        [Tooltip("Longitud mínima (celdas) de un subsegmento derivado de centerline. Subsegmentos más cortos se descartan para evitar microcortes.")]
        [Range(4, 256)] public int riverFordMinSubSegmentLengthCells = 20;
        [Tooltip("Mínimo de celdas en un vado conexo (4-vecinos) para que sea transitable. Manzas más pequeñas se revierten a río profundo (evita cruces fantasma de 1–2 celdas).")]
        [Range(2, 24)] public int riverFordMinWalkableBlobCells = 4;
        [Tooltip("Prefab legacy opcional (roca/tronco). Si no hay variantes en waterCrossingDecorationPrefabs, se usa este.")]
        public GameObject waterCrossingDecorationPrefab;
        [Tooltip("Variantes visuales para el nuevo vado (pasto/roca/tronco). Se distribuyen de forma determinista por celda y se combinan por prefab.")]
        public GameObject[] waterCrossingDecorationPrefabs;
        [Tooltip("Detalles opcionales genéricos en orilla (instancias combinadas).")]
        public bool waterEnableShoreProps = true;
        public GameObject waterShoreRockPrefab;
        [Tooltip("Densidad aproximada (props por 1000 celdas de borde de agua). 0 = desactivar.")]
        [Range(0f, 40f)] public float waterShorePropDensity = 5f;
        [Tooltip("Debug SceneView: interior-dist a orilla (muestreo grueso).")]
        public bool debugDrawWaterShoreDepthGizmos = false;
        [Tooltip("Debug SceneView: candidatos a cruce de río.")]
        public bool debugDrawWaterCrossingGizmos = false;

        [Header("Agua MS - límites de seguridad")]
        [Tooltip("Máximo de samples (esquinas) para Marching Squares (sw*sh). Si se supera, se hace fallback a agua por chunks (más barato).")]
        public int waterMsMaxCornerSamples = 250000;

        [Header("Terrain Skirt (volumen visual)")]
        [Tooltip("Activa las paredes laterales y base que dan volumen al mapa (efecto bloque de tierra).")]
        public bool showTerrainSkirt = true;
        [Tooltip("Profundidad en metros de las paredes laterales bajo el terreno.")]
        public float skirtDepth = 30f;
        [Tooltip("Número de muestras de altura por cada borde del terreno. Más muestras = bordes más suaves.")]
        [Range(32, 512)] public int skirtEdgeSamples = 128;
        [Tooltip("Material URP Lit para paredes y base del skirt (p. ej. MAT_TerrainSkirt_SoilLayers con soil_layers). Si es null, se intenta cargar desde Resources; si falla, shader procedural Custom/TerrainSkirt (bandas de color). Las UV del mesh asumen atlas 4 columnas en soil_layers (Sur/Este/Norte/Oeste).")]
        public Material skirtMaterial;

        [Header("Alpha — relieve macro (automático, sin esculpir a mano)")]
        [Tooltip("Relieve procedural: masas montañosas y cuencas. Lo rellena MatchConfigCompiler desde MatchConfig alpha.")]
        public bool macroTerrainEnabled;
        [Range(0, 12)] public int macroMountainMassCount;
        [Range(0.02f, 0.4f)] public float macroMountainHeight01Min = 0.08f;
        [Range(0.03f, 0.5f)] public float macroMountainHeight01Max = 0.18f;
        [Range(3, 80)] public int macroMountainRadiusCellsMin = 10;
        [Range(4, 96)] public int macroMountainRadiusCellsMax = 28;
        [Range(0, 8)] public int macroBasinCount;
        [Range(0.01f, 0.2f)] public float macroBasinDepth01 = 0.05f;
        [Range(0f, 1f)] public float macroRoughnessWeight = 0.5f;
        [Range(0f, 1f)] public float macroHillDensity = 0.45f;
        [Tooltip("Evita picos en el margen interior (spawns suelen ir hacia bordes).")]
        [Range(4, 96)] public int macroMountainSpawnAvoidanceMarginCells = 24;
        public bool macroAvoidCitiesForMountains = true;

        [Header("Alpha — sesgo de recursos por terreno")]
        public bool alphaUseTerrainResourceBias;
        [Range(0f, 3f)] public float alphaWoodNearWaterWeight = 1f;
        [Range(0f, 3f)] public float alphaStoneMountainWeight = 1f;
        [Range(0f, 3f)] public float alphaGoldMountainWeight = 1f;
        [Range(0f, 3f)] public float alphaFoodNearWaterWeight = 1f;

        [Header("Alpha — ciudades en llanura")]
        public bool alphaPreferPlainsForCities;
        [Range(0.35f, 0.92f)] public float alphaCityCenterMaxMeanHeight01 = 0.72f;
        [Tooltip("Distancia mínima Chebyshev desde agua/río para colocar centro de ciudad (0 = desactivar).")]
        [Range(0, 24)] public int alphaMinChebyshevFromWaterForSpawn;

        /// <summary>No serializado: el compilador asigna el mismo objeto que <see cref="RuntimeMapGenerationSettings.TerrainFeatures"/> para registrar picos/cuencas.</summary>
        [System.NonSerialized] public Project.Gameplay.Map.Generation.Alpha.TerrainFeatureRuntime alphaTerrainFeatureRecord;
        [System.NonSerialized] public Project.Gameplay.Map.Generation.Alpha.RegionClassificationConfig alphaRegionRules;
    }
}
