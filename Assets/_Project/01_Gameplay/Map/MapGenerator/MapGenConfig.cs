using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
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
        [Tooltip("Tras exportar terreno: plano(s) encima del mapa con la máscara de humedad (escala de grises).")]
        public bool debugTerrainMoisture = false;
        [Tooltip("Tras exportar terreno: plano con el ruido macro usado en el splat (antes del reparto grass/dirt).")]
        public bool debugTerrainMacro = false;
        [Tooltip("Tras exportar terreno: plano con la mezcla grass / grass seco (0=verde, 1=seco).")]
        public bool debugTerrainGrassDry = false;

        [Header("Regiones / biomas")]
        [Tooltip("Cantidad aproximada de macro-regiones para regionId/biomeId.")]
        public int regionCount = 8;
        public float regionNoiseScale = 0.02f;

        [Header("Agua (ríos y lagos en grid)")]
        [Range(0f, 1f)] public float waterHeight01 = 0.25f;
        public int riverCount = 3;
        public int lakeCount = 2;
        [Tooltip("Máximo de celdas por lago (flood fill).")]
        public int maxLakeCells = 200;
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
        [Range(0f, 0.22f)] public float riverMsMinAboveIsoAfterBlur = 0.11f;
        [Tooltip("Profundidad del lecho del río en altura 0–1 respecto al nivel del agua (lagos no se ven afectados). Más valor = cauce más bajo que la superficie del agua, mejor continuidad visual.")]
        [Range(0.004f, 0.14f)] public float riverBedDepthBelowWater01 = 0.022f;
        [Tooltip("Grosor base del río en celdas (radio entero). 0 = solo eje 1 celda. Expansión en disco alrededor del eje si riverExpandEuclidean; si no, cuadrado Chebyshev.")]
        [Range(0, 6)] public int riverWidthRadiusCells = 2;
        [Tooltip("Variación ± del radio a lo largo del eje (determinista por índice). 0 = ancho uniforme.")]
        [Range(0, 3)] public int riverWidthNoiseAmplitudeCells = 1;
        [Tooltip("Celdas de río absorbidas como agua desde el borde del lago (boca ancha y orgánica). 0 = desactivar.")]
        [Range(0, 8)] public int lakeRiverMouthBlendCells = 3;
        [Tooltip("Si true, el ensanche del río usa distancia euclídea en el grid (disco); si false, cuadrado Chebyshev (más ortogonal).")]
        public bool riverExpandEuclidean = true;

        [Header("Agua — lago forma orgánica (grid + marching squares)")]
        [Tooltip("Más alto = bordes del flood fill más irregulares (menos manchas rectangulares).")]
        [Range(0f, 1f)] public float lakeOrganicIrregularity = 0.72f;
        [Tooltip("Semillas iniciales extra en radio Chebyshev alrededor del centro (lagos con muescas y bultos).")]
        [Range(0, 10)] public int lakeExtraSeedSpreadCells = 4;
        [Tooltip("Ruido Perlin en el campo MS antes del blur; orillas menos rectas. 0 = desactivar.")]
        [Range(0f, 0.28f)] public float lakeShoreMsNoiseAmplitude = 0.09f;
        [Tooltip("Escala del ruido en espacio mundo (más bajo = ondulación más amplia en la orilla).")]
        [Range(0.015f, 0.45f)] public float lakeShoreMsNoiseScale = 0.105f;

        [Header("Agua — lago, confluencias y profundidad (gameplay)")]
        [Tooltip("Las celdas River que tocan lago (8 vecinos) pasan a Water: mismo cuerpo que el MS del lago y sin ribbon cruzando el lago.")]
        public bool mergeRiverCellsTouchingLake = true;
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
        [Tooltip("Mitad del ancho del cauce en mundo (ribbon); base RTS. Variación: Perlin + riverRibbonWidthVariation.")]
        [Range(0.12f, 16f)] public float riverVisualMeshHalfWidth = 2.15f;
        [Tooltip("Separación entre muestras del eje del río en unidades mundo (más bajo = curva más suave).")]
        [Range(0.06f, 4f)] public float riverVisualSampleSpacing = 0.4f;
        [Tooltip("Reduce el ancho del ribbon respecto al nominal para evitar solape con orillas o lagos.")]
        [Range(0f, 3f)] public float riverVisualBankInset = 0f;
        [Tooltip("Solo ribbon de río: sube la malla en Y (mundo) para alinearla con la orilla; el lecho del terreno suele quedar más bajo que el nivel de agua global.")]
        [Range(0f, 2.5f)] public float riverRibbonVerticalLiftWorld = 0.34f;
        [Tooltip("Si true: logs [RiverRibbonDebug] (puntos, bounds, maxSegment, saltos anormales). Quitar o desactivar tras depurar.")]
        public bool debugRiverRibbonGeometry = false;
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
        [Range(0.08f, 1f)] public float riverTerrainCarveFordMul = 0.32f;

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
        [Range(0.002f, 0.12f)] public float riverFordDepthBelowWater01 = 0.014f;
        [Tooltip("Capa de terreno bajo el agua en celdas de vado (guijarros/tierra). Si es null, se usa arena como el resto del agua.")]
        public TerrainLayer riverFordBedLayer;

        [Tooltip("Si true, no coloca un río nuevo si su corredor (eje + ancho) solapa celdas ya usadas por otro río.")]
        public bool riverAvoidCrossingOtherRivers = true;
        [Tooltip("Reintentos por río (otro borde inicio/salida + variación RNG) antes de descartar ese río.")]
        [Range(4, 96)] public int riverPlacementMaxAttemptsPerRiver = 40;
        [Tooltip("Tras tantos rechazos seguidos por cruce de corredor (evitar cruces), deja de intentar ese río (evita 40× trabajo inútil).")]
        [Range(6, 40)] public int riverCorridorRejectEarlyAbort = 12;
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
        public int waterChunkSize = 32;
        public float waterSurfaceOffset = 0.05f;
        [Tooltip("Material opcional para la malla de agua. Si no se asigna, se intenta el shader Project/RTS River Water.")]
        public Material waterMaterial;
        [Tooltip("Color agua poco profunda (orillas / centro del cauce en el shader RTS River Water).")]
        public Color riverWaterShallowColor = new Color(0.32f, 0.64f, 0.82f, 1f);
        [Tooltip("Color agua profunda (centro del cauce).")]
        public Color riverWaterDeepColor = new Color(0.08f, 0.22f, 0.42f, 1f);
        [Tooltip("Desplazamiento UV por segundo (flujo falso) en el shader de río.")]
        public Vector2 riverUVFlowSpeed = new Vector2(0.06f, 0.02f);
        [Tooltip("Suavizado visual del borde del ribbon (UV transversal). Mayor = transición más ancha hacia shallow.")]
        [Range(0.05f, 0.55f)] public float riverBankBlendStrength = 0.22f;
        [Tooltip("Tope duro de longitud de segmento del ribbon en mundo (0 = usar solo heurística interna).")]
        [Range(0f, 50f)] public float riverMaxSegmentLengthWorld = 0f;
        [Tooltip("Transparencia del agua (0 = opaco, 0.85 = se ve la arena bajo el agua). Requiere material con soporte alpha.")]
        [Range(0.5f, 1f)] public float waterAlpha = 0.88f;
        [Tooltip("Capa de Unity para el GameObject del agua (0 = Default). -1 = usar 0. Debe estar en la Culling Mask de la cámara.")]
        public int waterLayer = -1;

        [Header("Agua - bordes redondeados (Marching Squares)")]
        [Tooltip("✅ ACTIVAR para bordes orgánicos (elimina esquinas cuadradas). Desactivar solo si quieres agua tipo Minecraft.")]
        public bool waterRoundedEdges = true;
        [Tooltip("Subdivisión por celda (2-4 recomendado). Mayor = bordes más suaves pero más vértices. 3-4 es ideal para la mayoría de casos.")]
        [Range(1, 8)] public int waterEdgeSubdiv = 4;
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
