using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 3: genera agua como sistema. Ríos = centerline procedural (meandro + Catmull–Rom + remuestreo) + Bresenham + ensanchado; lagos = flood fill.</summary>
    public static class WaterGenerator
    {
        // Debug opcional (solo lectura): máscara final y clasificación derivada.
        public static float[,] DebugLastRiverFusionMask01 { get; private set; }
        public static bool[,] DebugLastRiverFusionCoreMask { get; private set; }
        public static bool[,] DebugLastRiverFusionShoreMask { get; private set; }
        /// <summary>Campo 0..1 tras blur de fusión (mismo que etapa intermedia de <see cref="DebugLastRiverFusionMask01"/>). Solo para gizmo <c>debugDrawWaterFusionMask</c>.</summary>
        public static float[,] DebugLastRiverFusionBlurField { get; private set; }

        /// <summary>Celdas (packed) convertidas a tierra por <see cref="WaterTopologyCleanup"/>; solo si <c>MapGenConfig.debugDrawWaterTopologyCleanupGizmo</c>.</summary>
        public static HashSet<long> DebugLastWaterCleanupRemovedPacked { get; private set; }

        // 🟢 Direcciones con diagonales (8) para lagos orgánicos
        private static readonly Vector2Int[] AllDirections = { 
            new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
        };

        private static readonly HashSet<long> s_riverCorridorPackedScratch = new HashSet<long>();
        private static readonly HashSet<long> s_fordPackedScratch = new HashSet<long>();
        private static readonly HashSet<Vector2Int> s_axisCentersScratch = new HashSet<Vector2Int>();

        /// <summary>Solo expansión lateral del raster River (grid); ~30% más estrecho que el config nominal. No afecta lagos.</summary>
        const float RiverRasterLateralExpandScale = 0.70f;

        /// <summary>
        /// Radio efectivo RTS para pintar el eje en grid (jugabilidad/minimapa). El ancho visual lo lleva la malla ribbon.
        /// </summary>
        static int VisualRiverRasterRadiusCells(bool isTributary, MapGenConfig config)
        {
            if (config != null && config.uwpOwnedVisualPolicy)
                return isTributary ? 1 : 1;
            return isTributary ? 0 : 1;
        }

        static int ScaledRiverRasterBaseRadius(MapGenConfig config)
        {
            if (config == null) return 0;
            int b = Mathf.Clamp(config.riverWidthRadiusCells, 0, 6);
            return Mathf.Clamp(Mathf.RoundToInt(b * RiverRasterLateralExpandScale), 0, 6);
        }

        static int ScaledRiverRasterWidthAmplitude(MapGenConfig config)
        {
            if (config == null) return 0;
            int a = Mathf.Clamp(config.riverWidthNoiseAmplitudeCells, 0, 3);
            return Mathf.Clamp(Mathf.RoundToInt(a * RiverRasterLateralExpandScale), 0, 3);
        }

        private static int AdaptiveRiverMaxAttemptsForMap(int minDim, MapGenConfig config = null)
        {
            bool highReliability = config != null && (
                config.uwpOwnedVisualPolicy ||
                config.ignoreLobbyHydrologyCaps ||
                config.maxTotalRiverBuildAttempts >= 720);

            if (minDim <= 64) return highReliability ? 32 : 8;
            if (minDim <= 128) return highReliability ? 64 : 11;
            // Mapas RTS típicos 256×256: el tope 12 ignoraba riverPlacementMaxAttemptsPerRiver=80 del perfil UWP.
            if (minDim <= 256) return highReliability ? 96 : 12;
            if (minDim <= 400) return highReliability ? 96 : 64;
            return highReliability ? 48 : 24;
        }

        private static void RiverOccupiedAddPackedCell(
            HashSet<long> set,
            ref bool aabbValid,
            ref int minX,
            ref int maxX,
            ref int minZ,
            ref int maxZ,
            long k)
        {
            if (!set.Add(k))
                return;
            int x = (int)(k >> 32);
            int z = (int)(uint)k;
            if (!aabbValid)
            {
                aabbValid = true;
                minX = maxX = x;
                minZ = maxZ = z;
            }
            else
            {
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minZ = Mathf.Min(minZ, z);
                maxZ = Mathf.Max(maxZ, z);
            }
        }

        private enum WaterPerfCaller
        {
            None,
            FusionRiverRemovalProbe,
            TopologyTryRemove,
        }

        /// <summary>Solo diagnóstico: tiempos y contadores cuando <see cref="MapGenConfig.debugWaterGeneratePerfDiagnostics"/> está activo.</summary>
        private static class WaterGenPerfDiag
        {
            public static bool Active;

            public static double MsHydrologyRivers;
            public static double MsLakesAbsorbMerge;
            public static double MsFusionPrepBlur;
            public static double MsFusionClassifyCoreShore;
            public static double MsFusionConnectivityApply;
            public static double MsThinZoneFords;
            public static double MsFillLandIslands;
            public static double MsLakeDeepCore;
            public static double MsTopologyCleanupTotal;
            public static double MsTopoCenterlinePacked;
            public static double MsTopoIslandPass;
            public static double MsTopoSpikePass;
            public static double MsTopoDiagonalPass;

            public static long RiverBuildAttempts;
            public static double MsRiverPathBuildSum;
            public static double MsRiverRasterApplySum;

            public static long BfsReachabilityCalls;
            public static long BfsReachabilityNodesVisited;

            public static long MaskCriticalCallsFusion;
            public static long MaskCriticalReturnsTrueFusion;
            public static long MaskCriticalCallsTopology;
            public static long MaskCriticalReturnsTrueTopology;

            public static long BridgeDisconnectedChecks;

            public static long AquaIslandComponentsDiscovered;
            public static long AquaIslandComponentCellsSum;
            public static long AquaIslandComponentMaxSize;
            public static long AquaIslandFloodDequeues;

            public static long TopologyTryRemoveCalls;
            public static long RebuildAquaBaseCalls;

            public static double TarjanBuildMs;
            public static long TarjanFusionLookups;

            public static long RiverHydrologyEarlyRejects;
            public static long RiverHydrologyStrictAttempts;
            public static long RiverHydrologyCrossingAttempts;

            public static double MsGenerateWaterTotal;
            public static long HydroRiverAttemptsTotal;
            public static long HydroEarlyRejectsTotal;
            public static long HydroCorridorRejectsTotal;
            public static long HydroLaplacianAllocAvoidedTotal;
            public static int HydroRiversAccepted;
            public static double HydroPathBuildMsSum;

            public static void Begin(MapGenConfig config)
            {
                Active = config != null && config.debugWaterGeneratePerfDiagnostics;
                if (!Active)
                    return;
                MsHydrologyRivers = 0;
                MsLakesAbsorbMerge = 0;
                MsFusionPrepBlur = 0;
                MsFusionClassifyCoreShore = 0;
                MsFusionConnectivityApply = 0;
                MsThinZoneFords = 0;
                MsFillLandIslands = 0;
                MsLakeDeepCore = 0;
                MsTopologyCleanupTotal = 0;
                MsTopoCenterlinePacked = 0;
                MsTopoIslandPass = 0;
                MsTopoSpikePass = 0;
                MsTopoDiagonalPass = 0;
                RiverBuildAttempts = 0;
                MsRiverPathBuildSum = 0;
                MsRiverRasterApplySum = 0;
                BfsReachabilityCalls = 0;
                BfsReachabilityNodesVisited = 0;
                MaskCriticalCallsFusion = 0;
                MaskCriticalReturnsTrueFusion = 0;
                MaskCriticalCallsTopology = 0;
                MaskCriticalReturnsTrueTopology = 0;
                BridgeDisconnectedChecks = 0;
                AquaIslandComponentsDiscovered = 0;
                AquaIslandComponentCellsSum = 0;
                AquaIslandComponentMaxSize = 0;
                AquaIslandFloodDequeues = 0;
                TopologyTryRemoveCalls = 0;
                RebuildAquaBaseCalls = 0;
                TarjanBuildMs = 0;
                TarjanFusionLookups = 0;
                RiverHydrologyEarlyRejects = 0;
                RiverHydrologyStrictAttempts = 0;
                RiverHydrologyCrossingAttempts = 0;
                MsGenerateWaterTotal = 0;
                HydroRiverAttemptsTotal = 0;
                HydroEarlyRejectsTotal = 0;
                HydroCorridorRejectsTotal = 0;
                HydroLaplacianAllocAvoidedTotal = 0;
                HydroRiversAccepted = 0;
                HydroPathBuildMsSum = 0;
            }

            public static void LogSummary(int w, int h, int rngSeed, MapGenConfig cfg)
            {
                if (Active)
                {
                double fusionSum = MsFusionPrepBlur + MsFusionClassifyCoreShore + MsFusionConnectivityApply;
                double topoInner = MsTopoCenterlinePacked + MsTopoIslandPass + MsTopoSpikePass + MsTopoDiagonalPass;
                double avgIsland =
                    AquaIslandComponentsDiscovered > 0
                        ? AquaIslandComponentCellsSum / (double)AquaIslandComponentsDiscovered
                        : 0.0;

                Debug.Log(
                    "[WaterGenPerf] grid=" + w + "x" + h + " rngSeed=" + rngSeed +
                    "\n  ms hydrologyRivers=" + MsHydrologyRivers.ToString("F2") +
                    " lakesAbsorbMerge=" + MsLakesAbsorbMerge.ToString("F2") +
                    "\n  ms fusion prepBlur=" + MsFusionPrepBlur.ToString("F2") +
                    " classifyCoreShore=" + MsFusionClassifyCoreShore.ToString("F2") +
                    " connectivityApply=" + MsFusionConnectivityApply.ToString("F2") +
                    " fusionSum=" + fusionSum.ToString("F2") +
                    "\n  ms thinZoneFords=" + MsThinZoneFords.ToString("F2") +
                    " fillLandIslands=" + MsFillLandIslands.ToString("F2") +
                    " lakeDeepCore=" + MsLakeDeepCore.ToString("F2") +
                    "\n  ms topologyCleanupTotal=" + MsTopologyCleanupTotal.ToString("F2") +
                    " (centerlinePacked=" + MsTopoCenterlinePacked.ToString("F2") +
                    " islands=" + MsTopoIslandPass.ToString("F2") +
                    " spikes=" + MsTopoSpikePass.ToString("F2") +
                    " diagonal=" + MsTopoDiagonalPass.ToString("F2") +
                    " innerSum=" + topoInner.ToString("F2") + ")" +
                    "\n  river pathBuild attempts=" + RiverBuildAttempts +
                    " sumBuildMs=" + MsRiverPathBuildSum.ToString("F2") +
                    " sumApplyMs=" + MsRiverRasterApplySum.ToString("F2") +
                    "\n  BFS reachability calls=" + BfsReachabilityCalls +
                    " nodesVisited=" + BfsReachabilityNodesVisited +
                    "\n  IsMaskConnectivityCritical fusion calls=" + MaskCriticalCallsFusion +
                    " returnsTrue=" + MaskCriticalReturnsTrueFusion +
                    " topology calls=" + MaskCriticalCallsTopology +
                    " returnsTrue=" + MaskCriticalReturnsTrueTopology +
                    "\n  MaskBridgePairDisconnected checks=" + BridgeDisconnectedChecks +
                    "\n  aqua island labeling components=" + AquaIslandComponentsDiscovered +
                    " avgCellsPerComp=" + avgIsland.ToString("F2") +
                    " maxComp=" + AquaIslandComponentMaxSize +
                    " floodDequeues=" + AquaIslandFloodDequeues +
                    "\n  topology TryRemoveCalls=" + TopologyTryRemoveCalls +
                    " RebuildAquaBaseCalls=" + RebuildAquaBaseCalls +
                    "\n  Tarjan fusion buildMs=" + TarjanBuildMs.ToString("F2") +
                    " fusionLookups=" + TarjanFusionLookups);
                }

                if ((cfg != null && cfg.debugRiverHydrologyPerf) || Active)
                {
                    double avgRiverBuild = HydroRiverAttemptsTotal > 0 ? HydroPathBuildMsSum / HydroRiverAttemptsTotal : 0.0;
                    Debug.Log(
                        "[WaterPerfSummary] totalGenerateWaterMs=" + MsGenerateWaterTotal.ToString("F2") +
                        " hydrologyMs=" + MsHydrologyRivers.ToString("F2") +
                        " avgRiverBuildMs=" + avgRiverBuild.ToString("F3") +
                        " attempts=" + HydroRiverAttemptsTotal +
                        " acceptedRivers=" + HydroRiversAccepted +
                        " earlyRejects=" + HydroEarlyRejectsTotal +
                        " corridorRejects=" + HydroCorridorRejectsTotal +
                        " allocAvoidedEstimate=" + HydroLaplacianAllocAvoidedTotal);
                }
            }
        }

        /// <summary>Parámetros: riverCount, lakeCount, maxLakeCells. Marca CellType Water/River. Determinista por rng.</summary>
        public static void GenerateWater(GridSystem grid, MapGenConfig config, IRng rng)
        {
            if (grid == null || config == null || rng == null) return;

            if (config.debugHydrologyNetwork || config.debugRiverHydrologyPerf || config.debugLogs)
            {
                UnityEngine.Debug.Log(
                    "[HydrologyRuntimeConfig] " +
                    $"debugHydrologyNetwork={config.debugHydrologyNetwork} " +
                    $"debugRiverHydrologyPerf={config.debugRiverHydrologyPerf} " +
                    $"debugWaterGeneratePerfDiagnostics={config.debugWaterGeneratePerfDiagnostics} " +
                    $"riverCount={config.riverCount} lakeCount={config.lakeCount} maxLakeCells={config.maxLakeCells} " +
                    $"seed={config.seed} mapGenAsset='{config.name}'");
            }

            if (config.debugHydrologyNetwork || config.debugRiverHydrologyPerf || config.debugLogs)
            {
                UnityEngine.Debug.Log(
                    "[RiverWidthTrace] stage=water_start riverWidthRadiusCells=" + config.riverWidthRadiusCells);
            }

            WaterGenPerfDiag.Begin(config);

            SimpleRiverPathGenerator.ResetHeightSummaryLog();

            bool logPhaseTiming = config.debugWaterTopologyCleanup || config.debugLogs;
            var swPhase = logPhaseTiming ? System.Diagnostics.Stopwatch.StartNew() : null;
            bool trackRiverHydroSummary = config.debugRiverHydrologyPerf || WaterGenPerfDiag.Active;
            var swPerfWhole = trackRiverHydroSummary ? System.Diagnostics.Stopwatch.StartNew() : null;

            int w = grid.Width;
            int h = grid.Height;
            int waterCells = 0;
            grid.RiverCenterlinesCellSpace = new List<List<Vector2>>();
            grid.RiverCenterlinesWorld = new List<List<Vector3>>();
            grid.RiverOriginKinds = new List<UwpTributaryOriginKind>();
            UwpTributaryOriginUtility.Clear(grid);
            grid.HydrologyMainRiverPattern = null;
            grid.HydrologyMainRiverTerminusCell = null;
            grid.HydrologyNetwork = new HydrologyNetworkGraph();
            if (config.debugDrawRiverPathInScene)
            {
                grid.RiverPathDebugMacro = new List<List<Vector2>>();
                grid.RiverPathDebugSmoothed = new List<List<Vector2>>();
            }
            else
            {
                grid.RiverPathDebugMacro = null;
                grid.RiverPathDebugSmoothed = null;
            }

            RiverRouteGenerator.PreparePlannedLakeSinkCandidates(grid, config, rng);

            // Ríos: descenso simple (SimpleRiverPathGenerator); opcional evitar solapes; vados = River transitable + riverFord.
            int riverCount = Mathf.Min(config.riverCount, 8);
            bool lakeFirstPipeline = config.uwpLakeFirstHydrologyPipeline;
            bool placeRivers = riverCount > 0;
            int riversToPlaceLoop = 0;
            if (placeRivers)
                riversToPlaceLoop = lakeFirstPipeline ? 1 : riverCount;
            grid.LakeFirstWaterGraph = null;
            if (config.uwpOwnedVisualPolicy || config.riverLogPlacementFailureSummary)
            {
                UnityEngine.Debug.LogWarning(
                    $"[HydrologyRuntimeAudit] Fase4 inicio: targetRivers={riverCount} avoidCross={config.riverAvoidCrossingOtherRivers} " +
                    $"maxAttempts={config.maxTotalRiverBuildAttempts} fillPass={config.riverRelaxedMissingTributaryFillPass} " +
                    $"candidatesPerSlot={config.riverTributaryProceduralCandidatesPerSlot}");
            }
            var riverOccupiedCells = new HashSet<long>();
            bool riverOccAabbValid = false;
            int riverOccMinX = 0, riverOccMaxX = 0, riverOccMinZ = 0, riverOccMaxZ = 0;
            int minDim = Mathf.Min(w, h);
            int mapAttemptCap = AdaptiveRiverMaxAttemptsForMap(minDim, config);
            int baseAttempts = Mathf.Clamp(config.riverPlacementMaxAttemptsPerRiver, 4, mapAttemptCap);
            int extraPerRiver = Mathf.Min(8, Mathf.Max(2, mapAttemptCap / 4));
            int maxAttemptsPerPass = Mathf.Clamp(baseAttempts + Mathf.Max(0, riverCount - 2) * extraPerRiver, 4, mapAttemptCap);
            int earlyAbortBase = Mathf.Clamp(config.riverCorridorRejectEarlyAbort, 6, 40);
            int earlyAbortThreshold = Mathf.Clamp(earlyAbortBase + Mathf.Max(0, riverCount - 3) * 4, 6, 40);
            int globalRiverBuildBudget = config.maxTotalRiverBuildAttempts > 0
                ? config.maxTotalRiverBuildAttempts
                : (minDim > 256
                    ? Mathf.Clamp(80 + riverCount * 30, 160, 480)
                    : Mathf.Clamp(40 + riverCount * 5, 48, 72));
            if (config.riverDebugUnlimitedBuildAttempts)
                globalRiverBuildBudget = int.MaxValue;
            bool budgetForcedRelax = false;

            int hydrologyPlacedStrict = 0;
            int hydrologyPlacedFallback = 0;
            int hydrologyRiversStartedFallbackPass = 0;

            var swHydrology = WaterGenPerfDiag.Active ? System.Diagnostics.Stopwatch.StartNew() : null;

            SimpleRiverPathGenerator.TryLogHeightmapSummaryOnce(grid, config);

            for (int i = 0; i < riversToPlaceLoop; i++)
            {
                bool placed = false;
                int attemptsUsed = 0;
                int buildFail = 0, corridorReject = 0;
                long buildTicks = 0, corridorTicks = 0, applyTicks = 0;
                bool usedRelaxedCrossing = false;
                double rbAccBuildMs = 0, rbAccCorMs = 0;
                string rbRejectReason = "";
                int consecutiveEarlyRejectsRiver = 0;

                // Pase 1: respetar riverAvoidCrossingOtherRivers. Pase 2 (solo si aplica): permitir cruces para cumplir riverCount del lobby.
                for (int pass = 0; pass < 2 && !placed; pass++)
                {
                    consecutiveEarlyRejectsRiver = 0;
                    if (pass == 1 && (!config.riverAvoidCrossingOtherRivers || !config.allowFallbackCrossing))
                        break;
                    if (pass == 1)
                        hydrologyRiversStartedFallbackPass++;

                    bool avoidCross = pass == 0 ? config.riverAvoidCrossingOtherRivers : false;
                    if (pass == 0 && budgetForcedRelax && config.allowFallbackCrossing)
                    {
                        if (config.debugRiverHydrologyPerf || config.debugLogs)
                            UnityEngine.Debug.Log(
                                $"[RiverAttemptBudget] used={WaterGenPerfDiag.HydroRiverAttemptsTotal} cap={globalRiverBuildBudget} riverId={i + 1} action=relax skip_strict_pass");
                        continue;
                    }

                    int consecutiveCorridorReject = 0;
                    int attemptsThisPass = 0;
                    int attemptLimit = maxAttemptsPerPass;

                    for (int attempt = 0; attempt < maxAttemptsPerPass && !placed; attempt++)
                    {
                        if (attempt >= attemptLimit)
                            break;

                        if (!config.riverDebugUnlimitedBuildAttempts &&
                            WaterGenPerfDiag.HydroRiverAttemptsTotal >= globalRiverBuildBudget &&
                            avoidCross &&
                            config.allowFallbackCrossing)
                        {
                            if (!budgetForcedRelax)
                            {
                                budgetForcedRelax = true;
                                UnityEngine.Debug.LogWarning(
                                    $"[RiverAttemptBudget] used={WaterGenPerfDiag.HydroRiverAttemptsTotal} cap={globalRiverBuildBudget} riverId={i + 1} action=relax global_cap");
                            }
                            break;
                        }

                        attemptsUsed++;
                        attemptsThisPass++;
                        if (WaterGenPerfDiag.Active)
                        {
                            if (avoidCross)
                                WaterGenPerfDiag.RiverHydrologyStrictAttempts++;
                            else
                                WaterGenPerfDiag.RiverHydrologyCrossingAttempts++;
                        }

                        bool logHydrologyNet = config.debugHydrologyNetwork || config.debugRiverHydrologyPerf || config.debugLogs;

                        bool appliedConfluenceTrim = false;
                        int trimJoinIdx = -1;
                        Vector2Int trimJoinCell = default;

                        bool mergeTributary =
                            i > 0 &&
                            grid.RiverCenterlinesCellSpace != null &&
                            grid.RiverCenterlinesCellSpace.Count > 0;

                        int riverWidthCompiledBackup = config.riverWidthRadiusCells;
                        int riverNoiseCompiledBackup = config.riverWidthNoiseAmplitudeCells;
                        int functionalRiverRadiusCells =
                            Mathf.Clamp(riverWidthCompiledBackup, 0, 6);

                        var swBuild = System.Diagnostics.Stopwatch.StartNew();

                        bool okBuild = false;
                        List<Vector2> centerline = null;
                        List<Vector2Int> path = null;
                        List<Vector2Int> fordCells = null;
                        List<Vector2> dbgMacro = null;
                        List<Vector2> dbgSmooth = null;
                        string simpleFail = null;

                        int visualRiverRadiusCells = VisualRiverRasterRadiusCells(mergeTributary, config);

                        try
                        {
                            // Visual/logs + path temporal: mismo radio que usará Expand (principal=1, tributario=0).
                            config.riverWidthRadiusCells = visualRiverRadiusCells;

                            if (logHydrologyNet)
                            {
                                UnityEngine.Debug.Log(
                                    $"[RiverWidthTrace] stage=before_simple_river slot={i + 1} attempt={attempt} " +
                                    $"merge={(mergeTributary ? 1 : 0)} riverWidthRadiusCells={visualRiverRadiusCells} " +
                                    $"resolvedFromCompiled={riverWidthCompiledBackup} " +
                                    $"functionalRiverRadiusCells={functionalRiverRadiusCells}");
                            }

                            // Tributarios deben poder alcanzar el troncal; no evitar su corredor ocupado.
                            bool routeAvoidCorridor = mergeTributary ? false : avoidCross;

                            okBuild = RiverRouteGenerator.TryGenerateRouteRiver(
                                grid,
                                config,
                                rng,
                                i + 1,
                                attempt,
                                mergeTributary,
                                routeAvoidCorridor,
                                routeAvoidCorridor ? riverOccupiedCells : null,
                                out path,
                                out centerline,
                                out fordCells,
                                out dbgMacro,
                                out dbgSmooth,
                                out simpleFail);

                            swBuild.Stop();
                            buildTicks += swBuild.ElapsedTicks;
                            if (trackRiverHydroSummary)
                            {
                                WaterGenPerfDiag.HydroRiverAttemptsTotal++;
                                WaterGenPerfDiag.HydroPathBuildMsSum += swBuild.Elapsed.TotalMilliseconds;
                            }

                            if (WaterGenPerfDiag.Active)
                            {
                                WaterGenPerfDiag.RiverBuildAttempts++;
                                WaterGenPerfDiag.MsRiverPathBuildSum += swBuild.Elapsed.TotalMilliseconds;
                            }

                            if (config.debugRiverHydrologyPerf)
                                rbAccBuildMs += swBuild.Elapsed.TotalMilliseconds;

                            if (!okBuild)
                            {
                                buildFail++;
                                consecutiveCorridorReject = 0;
                                rbRejectReason = string.IsNullOrEmpty(simpleFail) ? "simple_river_fail" : simpleFail;
                                consecutiveEarlyRejectsRiver++;
                                if (avoidCross &&
                                    consecutiveEarlyRejectsRiver >= config.riverEarlyRejectConsecutiveToBreakStrictPass &&
                                    config.allowFallbackCrossing)
                                {
                                    if (config.debugRiverHydrologyPerf || config.debugLogs)
                                        UnityEngine.Debug.Log(
                                            $"[RiverAttemptBudget] used={WaterGenPerfDiag.HydroRiverAttemptsTotal} cap={globalRiverBuildBudget} riverId={i + 1} action=relax consecutive_simple_fail");
                                    break;
                                }

                                continue;
                            }

                            consecutiveEarlyRejectsRiver = 0;

                            config.riverWidthRadiusCells = functionalRiverRadiusCells;

                            var swCor = System.Diagnostics.Stopwatch.StartNew();
                            CollectRiverCorridorPackedInto(path, config, w, h, s_riverCorridorPackedScratch);
                            bool intersectsOccupied =
                                CorridorIntersectsOccupied(s_riverCorridorPackedScratch, riverOccupiedCells);
                            bool cross = avoidCross && intersectsOccupied;
                            swCor.Stop();
                            corridorTicks += swCor.ElapsedTicks;
                            if (config.debugRiverHydrologyPerf)
                                rbAccCorMs += swCor.Elapsed.TotalMilliseconds;

                            if (cross)
                            {
                                corridorReject++;
                                WaterGenPerfDiag.HydroCorridorRejectsTotal++;
                                consecutiveCorridorReject++;
                                rbRejectReason = "corridor_strict";
                                if (consecutiveCorridorReject >= 4)
                                    attemptLimit = Mathf.Min(attemptLimit, attempt + 1 + Mathf.Max(4, maxAttemptsPerPass / 4));
                                if (consecutiveCorridorReject >= 6)
                                    attemptLimit = Mathf.Min(attemptLimit, attempt + 1 + Mathf.Max(2, maxAttemptsPerPass / 6));
                                if (consecutiveCorridorReject >= earlyAbortThreshold)
                                    break;
                                continue;
                            }

                            consecutiveCorridorReject = 0;

                            // Pase fallback (cruces permitidos): en vez de dibujar una "X", el río nuevo
                            // termina en la primera confluencia con uno existente (comportamiento natural).
                            // UWP tributarios: A* ya evita el corredor; si aún intersecta, recortar con mínimo alto.
                            if (!routeAvoidCorridor && intersectsOccupied)
                            {
                                int minCellsAfterTrim = mergeTributary ? 12 : 18;
                                if (!TryTrimRiverPathToFirstConfluence(
                                        grid,
                                        path,
                                        centerline,
                                        riverOccupiedCells,
                                        w,
                                        h,
                                        minCellsAfterTrim,
                                        out trimJoinIdx,
                                        out trimJoinCell))
                                {
                                    corridorReject++;
                                    WaterGenPerfDiag.HydroCorridorRejectsTotal++;
                                    rbRejectReason = "corridor_trim";
                                    continue;
                                }

                                appliedConfluenceTrim = true;
                                if (fordCells != null && fordCells.Count > 0)
                                    TrimFordCellsToPath(fordCells, path);
                                config.riverWidthRadiusCells = functionalRiverRadiusCells;
                                CollectRiverCorridorPackedInto(path, config, w, h, s_riverCorridorPackedScratch);
                            }

                            if (mergeTributary && config.uwpOwnedVisualPolicy &&
                                path != null &&
                                path.Count < Mathf.Max(36, config.riverVisualMinSurfacePieceLengthCells * 6))
                            {
                                buildFail++;
                                rbRejectReason = "uwp_tributary_centerline_too_short";
                                continue;
                            }

                            if (mergeTributary && config.uwpOwnedVisualPolicy &&
                                centerline != null &&
                                RiverSurfaceMeshBuilder.UwpTributaryPathRejected(centerline, config, out string geomReason))
                            {
                                buildFail++;
                                rbRejectReason = string.IsNullOrEmpty(geomReason)
                                    ? "uwp_tributary_bad_geometry"
                                    : $"uwp_tributary_{geomReason}";
                                continue;
                            }

                            int riverIdBeforeAdd = grid.RiverCenterlinesCellSpace != null ? grid.RiverCenterlinesCellSpace.Count : 0;

                            config.riverWidthRadiusCells = visualRiverRadiusCells;
                            var swApply = System.Diagnostics.Stopwatch.StartNew();
                            if (centerline != null && centerline.Count >= 2)
                            {
                                grid.RiverCenterlinesCellSpace.Add(centerline);
                                float cs = grid.CellSizeWorld;
                                Vector3 o = grid.Origin;
                                var world = new List<Vector3>(centerline.Count);
                                for (int pi = 0; pi < centerline.Count; pi++)
                                {
                                    var p = centerline[pi];
                                    world.Add(new Vector3(o.x + p.x * cs, o.y, o.z + p.y * cs));
                                }
                                grid.RiverCenterlinesWorld.Add(world);
                                UwpTributaryOriginUtility.SetOrigin(
                                    grid,
                                    riverIdBeforeAdd,
                                    i == 0 ? UwpTributaryOriginKind.None : UwpTributaryOriginKind.None);
                            }

                            if (config.debugDrawRiverPathInScene && dbgMacro != null && dbgSmooth != null)
                            {
                                grid.RiverPathDebugMacro.Add(dbgMacro);
                                grid.RiverPathDebugSmoothed.Add(dbgSmooth);
                            }

                            s_fordPackedScratch.Clear();
                            if (fordCells != null)
                            {
                                for (int fi = 0; fi < fordCells.Count; fi++)
                                    s_fordPackedScratch.Add(PackCellLong(fordCells[fi]));
                            }

                            for (int pi = 0; pi < path.Count; pi++)
                            {
                                var c = path[pi];
                                if (!grid.InBoundsCell(c.x, c.y)) continue;
                                ref var cell = ref grid.GetCell(c);
                                if (cell.type != CellType.Land) continue;
                                bool isFord = s_fordPackedScratch.Contains(PackCellLong(c));
                                cell.type = CellType.River;
                                cell.riverFord = isFord;
                                cell.walkable = isFord;
                                cell.buildable = false;
                                cell.waterTraverse = isFord ? WaterTraverseMode.FordShallow : WaterTraverseMode.SwimNavigable;
                                waterCells++;
                            }
                            if (visualRiverRadiusCells <= 0)
                                config.riverWidthNoiseAmplitudeCells = 0;
                            waterCells += ExpandRiverWidthAroundPath(grid, path, config, i);
                            config.riverWidthNoiseAmplitudeCells = riverNoiseCompiledBackup;

                            if (fordCells != null && fordCells.Count > 0 && config.riverFordCorridorRadiusCells > 0)
                                ApplyRiverFordCorridor(grid, fordCells, config.riverFordCorridorRadiusCells);

                            foreach (long k in s_riverCorridorPackedScratch)
                                RiverOccupiedAddPackedCell(
                                    riverOccupiedCells,
                                    ref riverOccAabbValid,
                                    ref riverOccMinX,
                                    ref riverOccMaxX,
                                    ref riverOccMinZ,
                                    ref riverOccMaxZ,
                                    k);
                            swApply.Stop();
                            applyTicks += swApply.ElapsedTicks;
                            if (WaterGenPerfDiag.Active)
                                WaterGenPerfDiag.MsRiverRasterApplySum += swApply.Elapsed.TotalMilliseconds;

                            if (grid.HydrologyNetwork != null && path != null && path.Count > 0)
                            {
                                int? parentRiver = null;
                                float joinDistSq = 0f;
                                if (appliedConfluenceTrim)
                                    parentRiver = TryResolveParentRiverAtJoin(grid, trimJoinCell, riverIdBeforeAdd, out joinDistSq);

                                string hReason = appliedConfluenceTrim ? "trimmed_to_confluence" : "full_cross_map";
                                var hydroRec = new HydrologyRiverRecord
                                {
                                    RiverId = riverIdBeforeAdd,
                                    RiverClass = appliedConfluenceTrim ? RiverClass.Tributary : RiverClass.MainRiver,
                                    ParentRiverId = parentRiver,
                                    BasinId = 0,
                                    EstimatedFlow01 = 1f,
                                    WidthClass = 0,
                                    JoinVertexIndex = appliedConfluenceTrim ? trimJoinIdx : -1,
                                    StartCell = path[0],
                                    EndCell = appliedConfluenceTrim ? trimJoinCell : path[path.Count - 1],
                                    AcceptedLengthCells = path.Count,
                                    HierarchyFromConfluenceTrim = appliedConfluenceTrim,
                                    HierarchyReason = hReason,
                                };
                                grid.HydrologyNetwork.AddRiver(hydroRec);

                                if (logHydrologyNet)
                                {
                                    string pst = parentRiver.HasValue ? parentRiver.Value.ToString() : "null";
                                    UnityEngine.Debug.Log(
                                        $"[RiverHierarchy] riverId={hydroRec.RiverId} class={hydroRec.RiverClass} parent={pst} " +
                                        $"joinCell={(appliedConfluenceTrim ? trimJoinCell.ToString() : "_")} joinIdx={hydroRec.JoinVertexIndex} " +
                                        $"reason={hReason} distSq={(appliedConfluenceTrim ? joinDistSq.ToString("F4") : "na")}");
                                }
                            }

                            if (mergeTributary && path != null && path.Count >= 2 && centerline != null)
                            {
                                Vector2Int joinCell = appliedConfluenceTrim
                                    ? trimJoinCell
                                    : (RiverRouteGenerator.LastTributaryConfluencePlanValid
                                        ? RiverRouteGenerator.LastTributaryConfluencePlan.ConfluenceCell
                                        : path[path.Count - 1]);
                                RiverConfluenceUtility.TryRegisterFromPlacement(
                                    grid,
                                    config,
                                    riverIdBeforeAdd,
                                    path,
                                    centerline,
                                    joinCell,
                                    appliedConfluenceTrim ? "phase4_trim" : "phase4_confluence");
                            }

                            placed = true;
                            usedRelaxedCrossing = pass == 1;
                            rbRejectReason = "";
                        }
                        finally
                        {
                            config.riverWidthRadiusCells = riverWidthCompiledBackup;
                            config.riverWidthNoiseAmplitudeCells = riverNoiseCompiledBackup;
                        }
                    }

                    if (!placed && attemptsThisPass < maxAttemptsPerPass && corridorReject > 0 && avoidCross && config.debugLogs)
                        UnityEngine.Debug.Log($"Fase4 Agua: río {i + 1}/{riverCount} pase estricto: aborto anticipado tras {attemptsThisPass} intentos (rechazos consecutivos ≥{earlyAbortThreshold}).");
                }

                if (placed)
                {
                    WaterGenPerfDiag.HydroRiversAccepted++;
                    if (usedRelaxedCrossing) hydrologyPlacedFallback++;
                    else hydrologyPlacedStrict++;

                    if (config.debugLogs && usedRelaxedCrossing)
                        UnityEngine.Debug.Log($"Fase4 Agua: río {i + 1}/{riverCount} colocado permitiendo cruce con ríos ya existentes (fallback lobby).");
                    if (config.debugLogs && config.riverLogSuccessfulPlacementMetrics)
                    {
                        double msBuild = buildTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        double msCor = corridorTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        double msApply = applyTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        UnityEngine.Debug.Log($"Fase4 Agua: río {i + 1}/{riverCount} OK | intentos={attemptsUsed} buildFail={buildFail} corridorReject={corridorReject} | ms: build≈{msBuild:F1} corridor≈{msCor:F1} apply≈{msApply:F1}");
                    }
                }
                else if (config.riverLogPlacementFailureSummary || config.debugLogs)
                {
                    double msBuild = buildTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    double msCor = corridorTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    double msApply = applyTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    double msPerAttempt = attemptsUsed > 0 ? (buildTicks + corridorTicks + applyTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency / attemptsUsed : 0.0;
                    UnityEngine.Debug.LogWarning($"Fase4 Agua: río {i + 1}/{riverCount} no colocado | intentos={attemptsUsed} (hasta {maxAttemptsPerPass} por pase) buildFail={buildFail} corridorReject={corridorReject} evitarCrucesCfg={config.riverAvoidCrossingOtherRivers} | ms tot≈{msBuild + msCor + msApply:F0} (build≈{msBuild:F0} corredor≈{msCor:F0} aplicar≈{msApply:F0}) | ms/intento≈{msPerAttempt:F2}");
                }

                if (config.debugRiverHydrologyPerf)
                {
                    double msApplyDiag = applyTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    UnityEngine.Debug.Log(
                        $"[RiverBuildPerf] riverId={i + 1} attempts={attemptsUsed} accepted={placed} " +
                        $"buildMs={rbAccBuildMs:F2} corridorMs={rbAccCorMs:F2} applyMs={msApplyDiag:F2} " +
                        $"rejectReason={(string.IsNullOrEmpty(rbRejectReason) ? (placed ? "none" : "unknown") : rbRejectReason)}");
                }
            }

            if (swHydrology != null)
                WaterGenPerfDiag.MsHydrologyRivers = swHydrology.Elapsed.TotalMilliseconds;

            int hydrologyNotPlaced = riverCount - hydrologyPlacedStrict - hydrologyPlacedFallback;
            if (config.debugLogs || config.riverLogPlacementFailureSummary)
            {
                UnityEngine.Debug.Log(
                    $"[Hydrology] Ríos: colocados pase estricto={hydrologyPlacedStrict}, entraron pase cruce={hydrologyRiversStartedFallbackPass}, " +
                    $"colocados tras pase cruce={hydrologyPlacedFallback}, no colocados={hydrologyNotPlaced}, allowFallbackCrossing={config.allowFallbackCrossing}");
            }

            if (!lakeFirstPipeline)
            {
                TryFillMissingTributaryRivers(
                    grid,
                    config,
                    rng,
                    riverCount,
                    ref waterCells,
                    riverOccupiedCells,
                    ref riverOccAabbValid,
                    ref riverOccMinX,
                    ref riverOccMaxX,
                    ref riverOccMinZ,
                    ref riverOccMaxZ,
                    ref hydrologyPlacedFallback);
            }

            if (config.uwpOwnedVisualPolicy || config.riverLogPlacementFailureSummary)
            {
                var joinCandidates = new List<int>(32);
                int candJoin = RiverConfluenceUtility.BuildConfluenceCandidates(grid, config, joinCandidates);
                int placedRivers = grid.RiverCenterlinesCellSpace?.Count ?? 0;
                UnityEngine.Debug.LogWarning(
                    $"[HydrologyRuntimeAudit] Fase4 fin: colocados={placedRivers}/{riverCount} " +
                    $"confluencias={(grid.RiverConfluences?.Count ?? 0)} joinCandidates={candJoin} " +
                    $"fillPass={config.riverRelaxedMissingTributaryFillPass}");
            }

            if (grid.HydrologyNetwork != null && grid.HydrologyNetwork.Rivers.Count > 0)
            {
                grid.HydrologyNetwork.FinalizeLengthClassification();
                grid.HydrologyNetwork.LogHydrologyGraphSummary(config);
            }

            AuditRiverTopology(grid, config);

            // Lagos: flood fill (BFS) desde semilla en Land; en mapas ≤256 tope duro lobby/alpha (no muta el asset).
            var swLakePipe = WaterGenPerfDiag.Active ? System.Diagnostics.Stopwatch.StartNew() : null;
            int lakeCountEff = Mathf.Min(config.lakeCount, 12);
            int maxLakeEff = Mathf.Clamp(config.maxLakeCells, 50, 12000);
            if (!config.ignoreLobbyHydrologyCaps && w <= 256 && h <= 256)
            {
                lakeCountEff = Mathf.Min(lakeCountEff, 2);
                maxLakeEff = Mathf.Min(maxLakeEff, 240);
            }

            // Lobby/alpha: lagos pequeños si maxLakeCells bajo. UWP usa ignoreLobbyHydrologyCaps.
            if (!config.ignoreLobbyHydrologyCaps && riverCount > 0 && config.maxLakeCells <= 200)
            {
                lakeCountEff = Mathf.Min(lakeCountEff, 2);
                maxLakeEff = Mathf.Min(maxLakeEff, 150);
            }

            grid.LakeBodyCellsPacked = new HashSet<long>();
            for (int i = 0; i < lakeCountEff; i++)
            {
                int attempts = 20;
                while (attempts-- > 0)
                {
                    int cx;
                    int cz;
                    var planned = grid.PlannedLakeSinkCandidates;
                    var term = grid.HydrologyMainRiverTerminusCell;
                    RiverMainPattern? mpat = grid.HydrologyMainRiverPattern;
                    bool highToLakeMouth =
                        i == 0 &&
                        term.HasValue &&
                        mpat.HasValue &&
                        (mpat.Value == RiverMainPattern.HighlandToLake || mpat.Value == RiverMainPattern.BorderToLake);

                    string lakeFallback = "random";
                    if (highToLakeMouth && term.HasValue)
                    {
                        Vector2Int t = term.Value;
                        lakeFallback = "highland_to_lake_mouth";
                        cx = Mathf.Clamp(t.x, 1, w - 2);
                        cz = Mathf.Clamp(t.y, 1, h - 2);
                        if (planned != null && planned.Count > 0)
                        {
                            Vector2Int pc = planned[0];
                            if (Mathf.Max(Mathf.Abs(pc.x - t.x), Mathf.Abs(pc.y - t.y)) <= 6)
                            {
                                cx = Mathf.Clamp(pc.x + rng.NextInt(-1, 2), 1, w - 2);
                                cz = Mathf.Clamp(pc.y + rng.NextInt(-1, 2), 1, h - 2);
                                lakeFallback = "planned_near_mouth";
                            }
                        }
                    }
                    else if (planned != null && i < planned.Count && rng.NextFloat() < 0.72f)
                    {
                        Vector2Int pc = planned[i];
                        cx = Mathf.Clamp(pc.x + rng.NextInt(-4, 5), w / 6, (5 * w) / 6 - 1);
                        cz = Mathf.Clamp(pc.y + rng.NextInt(-4, 5), h / 6, (5 * h) / 6 - 1);
                        lakeFallback = "planned_jitter";
                    }
                    else
                    {
                        cx = rng.NextInt(w / 6, (5 * w) / 6);
                        cz = rng.NextInt(h / 6, (5 * h) / 6);
                        lakeFallback = "random";
                    }

                    ref var seedCell = ref grid.GetCell(cx, cz);
                    if (seedCell.type != CellType.Land)
                        continue;

                    int sepDist = -1;
                    if (config.riverCount > 0 &&
                        config.lakeValidateSeparationFromMainRiver &&
                        grid.RiverCenterlinesCellSpace != null &&
                        grid.RiverCenterlinesCellSpace.Count > 0)
                    {
                        sepDist = MinChebyshevSeedToMainRiverCenterline(
                            cx,
                            cz,
                            grid.RiverCenterlinesCellSpace[0],
                            w,
                            h);
                        int mouthCheb = term.HasValue
                            ? Mathf.Max(Mathf.Abs(cx - term.Value.x), Mathf.Abs(cz - term.Value.y))
                            : int.MaxValue;
                        bool mouthExempt = highToLakeMouth && term.HasValue && mouthCheb <= 4;
                        if (sepDist < config.lakeMinChebyshevDistanceFromMainRiverCells && !mouthExempt)
                        {
                            if (config.debugLogs || config.debugLakeRiverSeparationLog)
                            {
                                UnityEngine.Debug.Log(
                                    $"[LakeRiverSeparation] lakeId={i + 1} distanceToNearestRiver={sepDist} accepted=0 reason=too_close_to_main_river");
                            }

                            continue;
                        }
                    }

                    int added = FloodFillLake(grid, new Vector2Int(cx, cz), maxLakeEff, config);
                    waterCells += added;
                    Vector2Int? plannedSinkLog =
                        planned != null && i < planned.Count ? (Vector2Int?)planned[i] : (Vector2Int?)null;
                    int dts = term.HasValue ? Mathf.Max(Mathf.Abs(cx - term.Value.x), Mathf.Abs(cz - term.Value.y)) : -1;
                    bool mouthConnected =
                        added > 0 && term.HasValue && dts <= Mathf.Max(2, config.lakeRiverMouthBlendCells + 2);
                    RiverRouteGenerator.LogLakeSinkValidation(
                        config,
                        i + 1,
                        plannedSinkLog,
                        added > 0,
                        new Vector2Int(cx, cz),
                        term,
                        dts,
                        mouthConnected,
                        lakeFallback);

                    if (config.debugLogs || config.debugLakeRiverSeparationLog)
                    {
                        UnityEngine.Debug.Log(
                            $"[LakeRiverSeparation] lakeId={i + 1} distanceToNearestRiver={sepDist} accepted={(added > 0 ? 1 : 0)} reason={(added > 0 ? "ok" : "flood_empty")}");
                    }

                    break;
                }
            }

            waterCells += AbsorbRiverMouthIntoLake(grid, config);

            if (config.mergeRiverCellsTouchingLake)
                MergeRiverCellsTouchingLake(grid);

            if (lakeFirstPipeline && placeRivers)
            {
                UwpLakeFirstHydrologyBuilder.BuildAndApply(
                    grid,
                    config,
                    rng,
                    riverCount,
                    ref waterCells,
                    riverOccupiedCells,
                    ref riverOccAabbValid,
                    ref riverOccMinX,
                    ref riverOccMaxX,
                    ref riverOccMinZ,
                    ref riverOccMaxZ);

                UwpLakeFirstSupplementalHydrologyBuilder.BuildAndApply(
                    grid,
                    config,
                    rng,
                    ref waterCells,
                    riverOccupiedCells,
                    ref riverOccAabbValid,
                    ref riverOccMinX,
                    ref riverOccMaxX,
                    ref riverOccMinZ,
                    ref riverOccMaxZ);
            }
            else if (config.lakeRiverConnectorMaxPerMap > 0)
            {
                EnforceSingleTributaryPerLakeComponent(grid, config);
            }

            if (swLakePipe != null)
                WaterGenPerfDiag.MsLakesAbsorbMerge = swLakePipe.Elapsed.TotalMilliseconds;

            // Fusión lógica de ríos por máscara acumulativa (no solo visual):
            // elimina puntas/abanicos en confluencias y define una franja de orilla transitable.
            ApplyRiverFusionMaskAndShoreWalkability(grid, config);
            // Vados por zonas delgadas (Fase4 hidrología): solo si NO usamos el modo asset en Fase9 (evita duplicar lógica).
            var swThin = WaterGenPerfDiag.Active ? System.Diagnostics.Stopwatch.StartNew() : null;
            if (!config.useCrossingAssetFords)
                ApplyFunctionalRiverFordsFromThinZones(grid, config);
            if (config != null && config.uwpLakeFirstHydrologyPipeline && config.uwpLakeFirstSupplementalEnabled)
                UwpLakeFirstSupplementalHydrologyBuilder.ClearFordsAlongSupplementalRivers(grid);
            if (swThin != null)
                WaterGenPerfDiag.MsThinZoneFords = swThin.Elapsed.TotalMilliseconds;

            // Elimina pequeñas "islas" de tierra totalmente encerradas por agua (artefactos visuales).
            var swFillIslands = WaterGenPerfDiag.Active ? System.Diagnostics.Stopwatch.StartNew() : null;
            waterCells += FillSmallLandIslandsInsideWater(grid, maxIslandCells: 14);
            if (swFillIslands != null)
                WaterGenPerfDiag.MsFillLandIslands = swFillIslands.Elapsed.TotalMilliseconds;

            int deepRing = Mathf.Clamp(config.lakeDeepImpassableMinDistanceFromShore, 0, 64);
            var swDeep = WaterGenPerfDiag.Active ? System.Diagnostics.Stopwatch.StartNew() : null;
            if (deepRing > 0)
                ApplyLakeDeepImpassableCore(grid, deepRing);
            if (swDeep != null)
                WaterGenPerfDiag.MsLakeDeepCore = swDeep.Elapsed.TotalMilliseconds;

            var swTopo = WaterGenPerfDiag.Active ? System.Diagnostics.Stopwatch.StartNew() : null;
            if (config.enableWaterTopologyCleanup)
                WaterTopologyCleanup(grid, config);
            else
                DebugLastWaterCleanupRemovedPacked = null;
            if (swTopo != null)
                WaterGenPerfDiag.MsTopologyCleanupTotal = swTopo.Elapsed.TotalMilliseconds;

            int total = w * h;
            float pct = total > 0 ? (waterCells * 100f / total) : 0f;
            int placedRiverCount = grid.RiverCenterlinesCellSpace != null ? grid.RiverCenterlinesCellSpace.Count : 0;
            if (config.debugLogs)
            {
                Debug.Log($"Fase4 Agua: {waterCells} celdas ({pct:F1}%), ríos colocados={placedRiverCount}/{riverCount} (centerline procedural→borde opuesto), lagos={lakeCountEff} (flood fill).");
                if (placedRiverCount < riverCount)
                {
                    Debug.LogWarning(
                        $"Fase4 Agua: faltan {riverCount - placedRiverCount} río(s). Suele deberse a " +
                        $"'{nameof(config.riverAvoidCrossingOtherRivers)}' y poco espacio de corredor. " +
                        $"Aumenta {nameof(config.riverPlacementMaxAttemptsPerRiver)}, " +
                        $"sube {nameof(config.riverCorridorRejectEarlyAbort)} o desactiva evitar cruces en el MapGenConfig (perfil técnico).");
                }
            }

            if (swPhase != null)
            {
                Debug.Log($"[Fase4 Agua] GenerateWater total ms={swPhase.Elapsed.TotalMilliseconds:F1} rngSeed={rng.Seed}");
            }

            if (swPerfWhole != null)
                WaterGenPerfDiag.MsGenerateWaterTotal = swPerfWhole.Elapsed.TotalMilliseconds;

            WaterGenPerfDiag.LogSummary(w, h, rng.Seed, config);
        }

        /// <summary>
        /// Pase extra: coloca al menos un tributario usable si el pase normal dejó slots vacíos.
        /// </summary>
        static void TryFillMissingTributaryRivers(
            GridSystem grid,
            MapGenConfig config,
            IRng rng,
            int riverCount,
            ref int waterCells,
            HashSet<long> riverOccupiedCells,
            ref bool riverOccAabbValid,
            ref int riverOccMinX,
            ref int riverOccMaxX,
            ref int riverOccMinZ,
            ref int riverOccMaxZ,
            ref int hydrologyPlacedFallback)
        {
            if (config == null || grid == null || rng == null || !config.riverRelaxedMissingTributaryFillPass)
                return;
            if (riverCount <= 1 || grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return;

            int placedBefore = grid.RiverCenterlinesCellSpace.Count;
            if (placedBefore >= riverCount)
                return;

            int w = grid.Width;
            int h = grid.Height;
            int tribTarget = riverCount - 1;
            int fillAdded = 0;
            int outerFillAttempts = 0;
            int maxOuterFillAttempts = config != null && config.uwpOwnedVisualPolicy ? 16 : 120;
            bool log = config.debugLogs || config.debugHydrologyNetwork || config.uwpOwnedVisualPolicy;

            while (grid.RiverCenterlinesCellSpace.Count < riverCount && outerFillAttempts < maxOuterFillAttempts)
            {
                outerFillAttempts++;
                int slotIndex = grid.RiverCenterlinesCellSpace.Count;
                int tribNow = slotIndex;
                int attemptsBudget = config.uwpOwnedVisualPolicy
                    ? (tribNow <= 1 ? 16 : 12)
                    : (tribNow <= 1 ? 96 : 48);
                bool placed = false;

                for (int attempt = 0; attempt < attemptsBudget && !placed; attempt++)
                {
                    bool fillRouteAvoidCorridor = false;
                    bool okBuild;
                    List<Vector2Int> path;
                    List<Vector2> centerline;
                    List<Vector2Int> fordCells;
                    string simpleFail;

                    if (config.uwpOwnedVisualPolicy)
                    {
                        okBuild = RiverRouteGenerator.TryPlaceUwpFillPassTributaryRoute(
                            grid,
                            config,
                            rng,
                            slotIndex + 1,
                            attempt + 9000,
                            fillRouteAvoidCorridor,
                            fillRouteAvoidCorridor ? riverOccupiedCells : null,
                            out path,
                            out centerline,
                            out fordCells,
                            out simpleFail);
                    }
                    else
                    {
                        okBuild = RiverRouteGenerator.TryGenerateRouteRiver(
                            grid,
                            config,
                            rng,
                            slotIndex + 1,
                            attempt + 9000,
                            mergeToExistingRiver: true,
                            avoidCrossingCorridor: fillRouteAvoidCorridor,
                            fillRouteAvoidCorridor ? riverOccupiedCells : null,
                            out path,
                            out centerline,
                            out fordCells,
                            out _,
                            out _,
                            out simpleFail);
                    }

                    if (!okBuild || path == null || path.Count < 2 || centerline == null || centerline.Count < 2)
                        continue;

                    bool appliedConfluenceTrim = false;
                    int trimJoinIdx = -1;
                    Vector2Int trimJoinCell = default;
                    if (RiverRouteGenerator.LastTributaryConfluencePlanValid)
                    {
                        appliedConfluenceTrim = true;
                        trimJoinCell = RiverRouteGenerator.LastTributaryConfluencePlan.ConfluenceCell;
                        for (int pi = 0; pi < path.Count; pi++)
                        {
                            if (path[pi].x == trimJoinCell.x && path[pi].y == trimJoinCell.y)
                            {
                                trimJoinIdx = pi;
                                break;
                            }
                        }
                    }

                    CollectRiverCorridorPackedInto(path, config, w, h, s_riverCorridorPackedScratch);
                    if (CorridorIntersectsOccupied(s_riverCorridorPackedScratch, riverOccupiedCells))
                    {
                        int fillMinCellsAfterTrim = config.uwpOwnedVisualPolicy
                            ? Mathf.Max(18, config.riverVisualMinSurfacePieceLengthCells * 3)
                            : 6;
                        if (!TryTrimRiverPathToFirstConfluence(
                                grid,
                                path,
                                centerline,
                                riverOccupiedCells,
                                w,
                                h,
                                fillMinCellsAfterTrim,
                                out trimJoinIdx,
                                out trimJoinCell))
                        {
                            if (config.uwpOwnedVisualPolicy)
                                continue;

                            var endCell = path[path.Count - 1];
                            if ((uint)endCell.x < (uint)w && (uint)endCell.y < (uint)h &&
                                grid.GetCell(endCell.x, endCell.y).type == CellType.River)
                            {
                                trimJoinIdx = path.Count - 1;
                                trimJoinCell = endCell;
                                appliedConfluenceTrim = true;
                            }
                            else
                            {
                                continue;
                            }
                        }
                        else
                        {
                            appliedConfluenceTrim = true;
                        }

                        if (fordCells != null && fordCells.Count > 0)
                            TrimFordCellsToPath(fordCells, path);
                        CollectRiverCorridorPackedInto(path, config, w, h, s_riverCorridorPackedScratch);
                    }

                    if (config.uwpOwnedVisualPolicy &&
                        (centerline == null ||
                         centerline.Count < Mathf.Max(14, config.riverVisualMinSurfacePieceLengthCells * 2)))
                        continue;

                    int riverIdBeforeAdd = grid.RiverCenterlinesCellSpace.Count;
                    int visualRiverRadiusCells = VisualRiverRasterRadiusCells(true, config);
                    int riverNoiseCompiledBackup = config.riverWidthNoiseAmplitudeCells;
                    config.riverWidthRadiusCells = visualRiverRadiusCells;

                    grid.RiverCenterlinesCellSpace.Add(centerline);
                    float cs = grid.CellSizeWorld;
                    Vector3 o = grid.Origin;
                    var world = new List<Vector3>(centerline.Count);
                    for (int pi = 0; pi < centerline.Count; pi++)
                    {
                        var p = centerline[pi];
                        world.Add(new Vector3(o.x + p.x * cs, o.y, o.z + p.y * cs));
                    }

                    grid.RiverCenterlinesWorld.Add(world);

                    s_fordPackedScratch.Clear();
                    if (fordCells != null)
                    {
                        for (int fi = 0; fi < fordCells.Count; fi++)
                            s_fordPackedScratch.Add(PackCellLong(fordCells[fi]));
                    }

                    for (int pi = 0; pi < path.Count; pi++)
                    {
                        var c = path[pi];
                        if (!grid.InBoundsCell(c.x, c.y)) continue;
                        ref var cell = ref grid.GetCell(c);
                        if (cell.type != CellType.Land) continue;
                        bool isFord = s_fordPackedScratch.Contains(PackCellLong(c));
                        cell.type = CellType.River;
                        cell.riverFord = isFord;
                        cell.walkable = isFord;
                        cell.buildable = false;
                        cell.waterTraverse = isFord ? WaterTraverseMode.FordShallow : WaterTraverseMode.SwimNavigable;
                        waterCells++;
                    }

                    if (visualRiverRadiusCells <= 0)
                        config.riverWidthNoiseAmplitudeCells = 0;
                    waterCells += ExpandRiverWidthAroundPath(grid, path, config, slotIndex);
                    config.riverWidthNoiseAmplitudeCells = riverNoiseCompiledBackup;

                    if (fordCells != null && fordCells.Count > 0 && config.riverFordCorridorRadiusCells > 0)
                        ApplyRiverFordCorridor(grid, fordCells, config.riverFordCorridorRadiusCells);

                    foreach (long k in s_riverCorridorPackedScratch)
                        RiverOccupiedAddPackedCell(
                            riverOccupiedCells,
                            ref riverOccAabbValid,
                            ref riverOccMinX,
                            ref riverOccMaxX,
                            ref riverOccMinZ,
                            ref riverOccMaxZ,
                            k);

                    if (grid.HydrologyNetwork != null)
                    {
                        int? parentRiver = RiverRouteGenerator.LastTributaryConfluencePlanValid ? 0 : (int?)null;
                        float joinDistSq = 0f;
                        if (appliedConfluenceTrim)
                            parentRiver = TryResolveParentRiverAtJoin(grid, trimJoinCell, riverIdBeforeAdd, out joinDistSq);
                        else if (parentRiver == 0)
                            parentRiver = TryResolveParentRiverAtJoin(grid, path[path.Count - 1], riverIdBeforeAdd, out joinDistSq);

                        grid.HydrologyNetwork.AddRiver(new HydrologyRiverRecord
                        {
                            RiverId = riverIdBeforeAdd,
                            RiverClass = RiverClass.Tributary,
                            ParentRiverId = parentRiver,
                            BasinId = 0,
                            EstimatedFlow01 = 0.55f,
                            WidthClass = 0,
                            JoinVertexIndex = appliedConfluenceTrim ? trimJoinIdx : -1,
                            StartCell = path[0],
                            EndCell = appliedConfluenceTrim ? trimJoinCell : path[path.Count - 1],
                            AcceptedLengthCells = path.Count,
                            HierarchyFromConfluenceTrim = appliedConfluenceTrim,
                            HierarchyReason = "relaxed_fill_pass",
                        });
                    }

                    if (appliedConfluenceTrim || RiverRouteGenerator.LastTributaryConfluencePlanValid)
                    {
                        var joinCell = appliedConfluenceTrim
                            ? trimJoinCell
                            : RiverRouteGenerator.LastTributaryConfluencePlan.ConfluenceCell;
                        RiverConfluenceUtility.TryRegisterFromPlacement(
                            grid,
                            config,
                            riverIdBeforeAdd,
                            path,
                            centerline,
                            joinCell,
                            "relaxed_fill_pass");
                    }

                    placed = true;
                    fillAdded++;
                    hydrologyPlacedFallback++;
                }

                if (!placed)
                    continue;
            }

            int placedAfter = grid.RiverCenterlinesCellSpace.Count;
            if (log || fillAdded > 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[TributaryFillPass] added={fillAdded} rivers={placedAfter}/{riverCount} " +
                    $"(tribs={Mathf.Max(0, placedAfter - 1)}/{tribTarget}) before={placedBefore}");
            }
        }

        static long PackLakeCellLong(int x, int z) => ((long)x << 32) | (uint)z;

        static List<HashSet<long>> BuildLakeBodyComponents(GridSystem grid)
        {
            var components = new List<HashSet<long>>();
            if (grid?.LakeBodyCellsPacked == null || grid.LakeBodyCellsPacked.Count == 0)
                return components;

            var visited = new HashSet<long>();
            foreach (long pk in grid.LakeBodyCellsPacked)
            {
                if (!visited.Add(pk))
                    continue;
                var comp = new HashSet<long> { pk };
                var q = new Queue<long>();
                q.Enqueue(pk);
                while (q.Count > 0)
                {
                    long cur = q.Dequeue();
                    int x = (int)(cur >> 32);
                    int z = (int)(uint)cur;
                    void Try(int nx, int nz)
                    {
                        if (!grid.InBoundsCell(nx, nz))
                            return;
                        long nk = PackLakeCellLong(nx, nz);
                        if (!grid.LakeBodyCellsPacked.Contains(nk) || !visited.Add(nk))
                            return;
                        comp.Add(nk);
                        q.Enqueue(nk);
                    }

                    Try(x - 1, z);
                    Try(x + 1, z);
                    Try(x, z - 1);
                    Try(x, z + 1);
                }

                if (comp.Count > 0)
                    components.Add(comp);
            }

            return components;
        }

        static int FindNearestLakeComponentIndex(Vector2 p, List<HashSet<long>> components, float maxDistCells)
        {
            if (components == null || components.Count == 0)
                return -1;
            int best = -1;
            float bestDist = maxDistCells + 1f;
            for (int ci = 0; ci < components.Count; ci++)
            {
                foreach (long pk in components[ci])
                {
                    int x = (int)(pk >> 32);
                    int z = (int)(uint)pk;
                    float d = Mathf.Max(Mathf.Abs(p.x - (x + 0.5f)), Mathf.Abs(p.y - (z + 0.5f)));
                    if (d <= maxDistCells && d < bestDist)
                    {
                        bestDist = d;
                        best = ci;
                    }
                }
            }

            return best;
        }

        static bool CenterlineEndpointTouchesLakeComponent(
            List<Vector2> line,
            HashSet<long> lakeCells,
            bool atStart,
            float maxDistCells)
        {
            if (line == null || line.Count < 1 || lakeCells == null || lakeCells.Count == 0)
                return false;
            Vector2 p = atStart ? line[0] : line[line.Count - 1];
            foreach (long pk in lakeCells)
            {
                int x = (int)(pk >> 32);
                int z = (int)(uint)pk;
                float d = Mathf.Max(Mathf.Abs(p.x - (x + 0.5f)), Mathf.Abs(p.y - (z + 0.5f)));
                if (d <= maxDistCells)
                    return true;
            }

            return false;
        }

        static void TrimTributaryAwayFromLakeComponent(
            GridSystem grid,
            int riverIndex,
            HashSet<long> lakeCells,
            bool trimStart)
        {
            if (grid?.RiverCenterlinesCellSpace == null ||
                riverIndex <= 0 ||
                riverIndex >= grid.RiverCenterlinesCellSpace.Count ||
                lakeCells == null ||
                lakeCells.Count == 0)
                return;

            var line = grid.RiverCenterlinesCellSpace[riverIndex];
            if (line == null || line.Count < 3)
                return;

            const float nearLakeCells = 14f;
            if (trimStart)
            {
                int cut = 0;
                for (int i = 0; i < line.Count; i++)
                {
                    if (!IsPointNearLakeCells(line[i], lakeCells, nearLakeCells))
                    {
                        cut = i;
                        break;
                    }

                    cut = i + 1;
                }

                cut = Mathf.Clamp(cut, 0, line.Count - 2);
                if (cut <= 0)
                    return;
                line.RemoveRange(0, cut);
            }
            else
            {
                int cut = line.Count;
                for (int i = line.Count - 1; i >= 0; i--)
                {
                    if (!IsPointNearLakeCells(line[i], lakeCells, nearLakeCells))
                    {
                        cut = i + 1;
                        break;
                    }

                    cut = i;
                }

                cut = Mathf.Clamp(cut, 2, line.Count);
                if (cut >= line.Count)
                    return;
                line.RemoveRange(cut, line.Count - cut);
            }

            if (grid.RiverCenterlinesWorld != null && riverIndex < grid.RiverCenterlinesWorld.Count)
            {
                float cs = grid.CellSizeWorld;
                Vector3 o = grid.Origin;
                var world = new List<Vector3>(line.Count);
                for (int pi = 0; pi < line.Count; pi++)
                {
                    var p = line[pi];
                    world.Add(new Vector3(o.x + p.x * cs, o.y, o.z + p.y * cs));
                }

                grid.RiverCenterlinesWorld[riverIndex] = world;
            }
        }

        static bool IsPointNearLakeCells(Vector2 p, HashSet<long> lakeCells, float maxDistCells)
        {
            if (lakeCells == null)
                return false;
            foreach (long pk in lakeCells)
            {
                int x = (int)(pk >> 32);
                int z = (int)(uint)pk;
                float d = Mathf.Max(Mathf.Abs(p.x - (x + 0.5f)), Mathf.Abs(p.y - (z + 0.5f)));
                if (d <= maxDistCells)
                    return true;
            }

            return false;
        }

        /// <summary>Un solo tributario por componente de lago; elige el enlace main↔lago más directo.</summary>
        static void EnforceSingleTributaryPerLakeComponent(GridSystem grid, MapGenConfig config)
        {
            if (grid?.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count < 2)
                return;

            var lakeComponents = BuildLakeBodyComponents(grid);
            grid.LakeBodyComponents = lakeComponents;
            grid.LakeComponentTributaryOwnerRiverIndex = new List<int>(lakeComponents.Count);
            for (int i = 0; i < lakeComponents.Count; i++)
                grid.LakeComponentTributaryOwnerRiverIndex.Add(-1);

            if (lakeComponents.Count == 0)
                return;

            if (!ShouldEnforceUniqueLakeTributary(config))
                return;

            float maxConnectDist = ResolveLakeTributaryMaxConnectDistCells(config);
            var candidates = new List<(int riverIndex, int compIdx, bool atStart, float score)>(16);

            for (int ri = 1; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
            {
                var line = grid.RiverCenterlinesCellSpace[ri];
                if (line == null || line.Count < 3)
                    continue;

                TryCollectTributaryLakeCandidates(
                    ri, line, grid, lakeComponents, maxConnectDist, candidates);
            }

            for (int ci = 0; ci < lakeComponents.Count; ci++)
            {
                int bestRiver = -1;
                float bestScore = float.MaxValue;
                for (int k = 0; k < candidates.Count; k++)
                {
                    var c = candidates[k];
                    if (c.compIdx != ci || c.score >= float.MaxValue * 0.5f)
                        continue;
                    if (c.score < bestScore)
                    {
                        bestScore = c.score;
                        bestRiver = c.riverIndex;
                    }
                }

                if (bestRiver >= 0)
                    grid.LakeComponentTributaryOwnerRiverIndex[ci] = bestRiver;
            }

            AssignFallbackLakeTributaryOwners(grid, lakeComponents, maxConnectDist);
            ResolveDuplicateLakeTributaryOwnerConflicts(grid, lakeComponents, maxConnectDist);
            AssignFallbackLakeTributaryOwners(grid, lakeComponents, maxConnectDist);

            for (int ci = 0; ci < lakeComponents.Count; ci++)
            {
                int owner = grid.LakeComponentTributaryOwnerRiverIndex[ci];
                if (owner <= 0)
                    continue;
                ExtendOwnedTributaryCenterlineToLake(
                    grid, owner, lakeComponents[ci], maxConnectDist);
            }

            for (int ci = 0; ci < lakeComponents.Count; ci++)
            {
                int owner = grid.LakeComponentTributaryOwnerRiverIndex[ci];
                if (owner <= 0)
                    continue;
                PruneNonOwnerTributariesNearLakeComponent(
                    grid, lakeComponents[ci], owner, maxConnectDist);
            }

            for (int k = 0; k < candidates.Count; k++)
            {
                var c = candidates[k];
                int owner = c.compIdx >= 0 && c.compIdx < grid.LakeComponentTributaryOwnerRiverIndex.Count
                    ? grid.LakeComponentTributaryOwnerRiverIndex[c.compIdx]
                    : -1;
                if (owner < 0 || owner == c.riverIndex)
                    continue;

                TrimTributaryAwayFromLakeComponent(
                    grid, c.riverIndex, lakeComponents[c.compIdx], c.atStart);
                if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
                {
                    Debug.Log(
                        $"[LakeTributaryUnique] lakeComp={c.compIdx} keptRiver={owner} " +
                        $"trimmedRiver={c.riverIndex} trimStart={(c.atStart ? 1 : 0)}");
                }
            }

            grid.ClearRiverVisualSurfaceCache();
        }

        static bool ShouldEnforceUniqueLakeTributary(MapGenConfig config)
        {
            if (config == null)
                return true;
            if (config.uwpOwnedVisualPolicy)
                return true;
            return Mathf.Clamp(config.lakeRiverConnectorMaxPerMap, 1, 8) == 1;
        }

        static bool PolylinePassesNearLakeCells(List<Vector2> line, HashSet<long> lakeCells, float maxDistCells)
        {
            if (line == null || line.Count == 0 || lakeCells == null || lakeCells.Count == 0)
                return false;
            for (int i = 0; i < line.Count; i++)
            {
                if (IsPointNearLakeCells(line[i], lakeCells, maxDistCells))
                    return true;
            }

            return false;
        }

        static void AssignFallbackLakeTributaryOwners(
            GridSystem grid,
            List<HashSet<long>> lakeComponents,
            float maxConnectDist)
        {
            if (grid?.LakeComponentTributaryOwnerRiverIndex == null ||
                grid.RiverCenterlinesCellSpace == null ||
                lakeComponents == null)
                return;

            var claimedRivers = new HashSet<int>();
            for (int ci = 0; ci < lakeComponents.Count; ci++)
            {
                int owner = grid.LakeComponentTributaryOwnerRiverIndex[ci];
                if (owner > 0)
                    claimedRivers.Add(owner);
            }

            for (int ci = 0; ci < lakeComponents.Count; ci++)
            {
                if (grid.LakeComponentTributaryOwnerRiverIndex[ci] > 0)
                    continue;

                int bestRiver = -1;
                float bestDist = float.MaxValue;
                for (int ri = 1; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
                {
                    if (claimedRivers.Contains(ri))
                        continue;

                    var line = grid.RiverCenterlinesCellSpace[ri];
                    if (line == null || line.Count < 3)
                        continue;
                    if (!TryFindPolylineLakeApproach(
                            line, lakeComponents[ci], maxConnectDist, out _, out _, out _, out float dist))
                        continue;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestRiver = ri;
                    }
                }

                if (bestRiver >= 0)
                {
                    grid.LakeComponentTributaryOwnerRiverIndex[ci] = bestRiver;
                    claimedRivers.Add(bestRiver);
                }
            }
        }

        static void ResolveDuplicateLakeTributaryOwnerConflicts(
            GridSystem grid,
            List<HashSet<long>> lakeComponents,
            float maxConnectDist)
        {
            if (grid?.LakeComponentTributaryOwnerRiverIndex == null || lakeComponents == null)
                return;

            var riverBestLake = new Dictionary<int, (int compIdx, float dist)>();
            for (int ci = 0; ci < lakeComponents.Count; ci++)
            {
                int owner = grid.LakeComponentTributaryOwnerRiverIndex[ci];
                if (owner <= 0)
                    continue;

                float dist = float.MaxValue;
                var line = grid.RiverCenterlinesCellSpace != null && owner < grid.RiverCenterlinesCellSpace.Count
                    ? grid.RiverCenterlinesCellSpace[owner]
                    : null;
                if (line != null &&
                    TryFindPolylineLakeApproach(line, lakeComponents[ci], maxConnectDist, out _, out _, out _, out float d))
                    dist = d;

                if (!riverBestLake.TryGetValue(owner, out var prev) || dist < prev.dist)
                    riverBestLake[owner] = (ci, dist);
            }

            for (int ci = 0; ci < lakeComponents.Count; ci++)
            {
                int owner = grid.LakeComponentTributaryOwnerRiverIndex[ci];
                if (owner <= 0)
                    continue;
                if (riverBestLake.TryGetValue(owner, out var best) && best.compIdx != ci)
                    grid.LakeComponentTributaryOwnerRiverIndex[ci] = -1;
            }
        }

        static void PruneNonOwnerTributariesNearLakeComponent(
            GridSystem grid,
            HashSet<long> lakeCells,
            int ownerRiver,
            float maxConnectDist)
        {
            if (grid?.RiverCenterlinesCellSpace == null || lakeCells == null || lakeCells.Count == 0)
                return;

            float trimRadius = maxConnectDist + 8f;
            for (int ri = 1; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
            {
                if (ownerRiver > 0 && ri == ownerRiver)
                    continue;

                var line = grid.RiverCenterlinesCellSpace[ri];
                if (line == null || line.Count < 3)
                    continue;
                if (!PolylinePassesNearLakeCells(line, lakeCells, trimRadius))
                    continue;

                TrimTributaryAwayFromLakeComponent(grid, ri, lakeCells, trimStart: true);
                TrimTributaryAwayFromLakeComponent(grid, ri, lakeCells, trimStart: false);
            }
        }

        static float ResolveLakeTributaryMaxConnectDistCells(MapGenConfig config)
        {
            if (config == null)
                return 36f;
            float cfgDist = Mathf.Max(8f, config.lakeRiverConnectorMaxDistanceCells);
            return config.uwpOwnedVisualPolicy ? Mathf.Min(cfgDist, 48f) : cfgDist;
        }

        static void TryCollectTributaryLakeCandidates(
            int riverIndex,
            List<Vector2> line,
            GridSystem grid,
            List<HashSet<long>> lakeComponents,
            float maxConnectDist,
            List<(int riverIndex, int compIdx, bool atStart, float score)> candidates)
        {
            int compIdx = FindNearestLakeComponentForPolyline(
                line, lakeComponents, maxConnectDist, out int nearIdx, out _);
            bool lakeAtStart;

            if (compIdx < 0)
            {
                if (!TryResolveTributaryLakeEndpointSide(grid, line, out lakeAtStart, out Vector2 lakeEnd, out Vector2 mainEnd))
                    return;

                compIdx = FindNearestLakeComponentIndex(lakeEnd, lakeComponents, maxConnectDist);
                if (compIdx < 0)
                    compIdx = FindNearestLakeComponentIndex(mainEnd, lakeComponents, maxConnectDist);
                if (compIdx < 0)
                    return;

                nearIdx = lakeAtStart ? 0 : line.Count - 1;
            }
            else
            {
                lakeAtStart = nearIdx <= (line.Count - 1) / 2;
            }

            float score = ScoreTributaryLakeConnection(
                grid, line, lakeComponents[compIdx], maxConnectDist, nearIdx);
            if (score >= float.MaxValue * 0.5f)
                return;

            candidates.Add((riverIndex, compIdx, lakeAtStart, score));
        }

        static int FindNearestLakeComponentForPolyline(
            List<Vector2> line,
            List<HashSet<long>> lakeComponents,
            float maxConnectDist,
            out int nearIdx,
            out float nearDist)
        {
            nearIdx = -1;
            nearDist = float.MaxValue;
            if (line == null || line.Count < 2 || lakeComponents == null || lakeComponents.Count == 0)
                return -1;

            int bestComp = -1;
            for (int ci = 0; ci < lakeComponents.Count; ci++)
            {
                if (!TryFindPolylineLakeApproach(
                        line, lakeComponents[ci], maxConnectDist, out int idx, out _, out _, out float dist))
                    continue;
                if (dist < nearDist)
                {
                    nearDist = dist;
                    nearIdx = idx;
                    bestComp = ci;
                }
            }

            return bestComp;
        }

        static bool TryFindPolylineLakeApproach(
            List<Vector2> line,
            HashSet<long> lakeCells,
            float maxConnectDist,
            out int nearIdx,
            out Vector2 nearPt,
            out Vector2 lakePt,
            out float nearDist)
        {
            nearIdx = -1;
            nearPt = default;
            lakePt = default;
            nearDist = float.MaxValue;
            if (line == null || line.Count < 1 || lakeCells == null || lakeCells.Count == 0)
                return false;

            for (int i = 0; i < line.Count; i++)
            {
                if (!TryGetNearestLakePointInComponent(line[i], lakeCells, out Vector2 lp, out float d))
                    continue;
                if (d < nearDist)
                {
                    nearDist = d;
                    nearIdx = i;
                    nearPt = line[i];
                    lakePt = lp;
                }
            }

            return nearIdx >= 0 && nearDist <= maxConnectDist;
        }

        static int ResolveTributaryMainEndpointIndex(GridSystem grid, List<Vector2> line)
        {
            if (line == null || line.Count < 2)
                return 0;
            var mainLine = grid?.RiverCenterlinesCellSpace != null && grid.RiverCenterlinesCellSpace.Count > 0
                ? grid.RiverCenterlinesCellSpace[0]
                : null;
            if (mainLine == null || mainLine.Count < 2)
                return line.Count - 1;

            float dStart = MinDistPointToPolylineCellSpace(line[0], mainLine);
            float dEnd = MinDistPointToPolylineCellSpace(line[line.Count - 1], mainLine);
            return dStart + 0.01f <= dEnd ? 0 : line.Count - 1;
        }

        static List<Vector2> ExtractOrderedSubpathFromMainToIndex(List<Vector2> line, int mainIdx, int targetIdx)
        {
            if (line == null || line.Count < 2)
                return line;
            if (mainIdx == targetIdx)
                return new List<Vector2> { line[mainIdx] };

            if (mainIdx < targetIdx)
                return new List<Vector2>(line.GetRange(mainIdx, targetIdx - mainIdx + 1));

            var sub = new List<Vector2>(line.GetRange(targetIdx, mainIdx - targetIdx + 1));
            sub.Reverse();
            return sub;
        }

        static void AppendBridgePointsTowardLake(List<Vector2> line, Vector2 from, Vector2 lakePt, float distCells)
        {
            if (line == null || distCells < 0.2f)
                return;
            if (distCells < 0.35f)
            {
                line.Add(lakePt);
                return;
            }

            int steps = Mathf.Clamp(Mathf.CeilToInt(distCells / 0.75f), 2, 14);
            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                line.Add(Vector2.Lerp(from, lakePt, t));
            }
        }

        static void ReplaceRiverCenterlineCellSpace(GridSystem grid, int riverIndex, List<Vector2> line)
        {
            if (grid?.RiverCenterlinesCellSpace == null ||
                riverIndex < 0 || riverIndex >= grid.RiverCenterlinesCellSpace.Count ||
                line == null || line.Count < 2)
                return;

            grid.RiverCenterlinesCellSpace[riverIndex] = line;
            if (grid.RiverCenterlinesWorld != null && riverIndex < grid.RiverCenterlinesWorld.Count)
            {
                float cs = grid.CellSizeWorld;
                Vector3 o = grid.Origin;
                var world = new List<Vector3>(line.Count);
                for (int pi = 0; pi < line.Count; pi++)
                {
                    var p = line[pi];
                    world.Add(new Vector3(o.x + p.x * cs, o.y, o.z + p.y * cs));
                }

                grid.RiverCenterlinesWorld[riverIndex] = world;
            }
        }

        static void ExtendOwnedTributaryCenterlineToLake(
            GridSystem grid,
            int riverIndex,
            HashSet<long> lakeCells,
            float maxConnectDist)
        {
            if (grid?.RiverCenterlinesCellSpace == null ||
                riverIndex <= 0 ||
                riverIndex >= grid.RiverCenterlinesCellSpace.Count ||
                lakeCells == null || lakeCells.Count == 0)
                return;

            var line = grid.RiverCenterlinesCellSpace[riverIndex];
            if (line == null || line.Count < 2)
                return;

            if (!TryFindPolylineLakeApproach(
                    line, lakeCells, maxConnectDist, out int nearIdx, out Vector2 nearPt, out Vector2 lakePt, out float nearDist))
                return;

            int mainIdx = ResolveTributaryMainEndpointIndex(grid, line);
            List<Vector2> sub;
            if (mainIdx == nearIdx)
                sub = new List<Vector2>(line);
            else
                sub = ExtractOrderedSubpathFromMainToIndex(line, mainIdx, nearIdx);

            if (sub == null || sub.Count < 2)
                return;

            Vector2 approach = sub[sub.Count - 1];
            if (!TryGetNearestLakePointInComponent(approach, lakeCells, out lakePt, out float bridgeDist))
                return;
            if (bridgeDist > maxConnectDist)
                return;

            if (bridgeDist > 0.2f)
                AppendBridgePointsTowardLake(sub, approach, lakePt, bridgeDist);
            else
                sub[sub.Count - 1] = lakePt;

            ReplaceRiverCenterlineCellSpace(grid, riverIndex, sub);
        }

        static bool TryResolveTributaryLakeEndpointSide(
            GridSystem grid,
            List<Vector2> line,
            out bool lakeAtStart,
            out Vector2 lakeEnd,
            out Vector2 mainEnd)
        {
            lakeAtStart = false;
            lakeEnd = default;
            mainEnd = default;
            if (line == null || line.Count < 2)
                return false;

            var mainLine = grid?.RiverCenterlinesCellSpace != null && grid.RiverCenterlinesCellSpace.Count > 0
                ? grid.RiverCenterlinesCellSpace[0]
                : null;
            if (mainLine != null && mainLine.Count >= 2)
            {
                float dStart = MinDistPointToPolylineCellSpace(line[0], mainLine);
                float dEnd = MinDistPointToPolylineCellSpace(line[line.Count - 1], mainLine);
                if (dStart + 0.01f <= dEnd)
                {
                    mainEnd = line[0];
                    lakeEnd = line[line.Count - 1];
                    lakeAtStart = false;
                }
                else
                {
                    mainEnd = line[line.Count - 1];
                    lakeEnd = line[0];
                    lakeAtStart = true;
                }

                return true;
            }

            lakeEnd = line[line.Count - 1];
            mainEnd = line[0];
            return true;
        }

        static float MinDistPointToPolylineCellSpace(Vector2 p, List<Vector2> poly)
        {
            if (poly == null || poly.Count == 0)
                return float.MaxValue;
            if (poly.Count == 1)
                return Vector2.Distance(p, poly[0]);

            float best = float.MaxValue;
            for (int i = 0; i < poly.Count - 1; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[i + 1];
                Vector2 ab = b - a;
                float lenSq = ab.sqrMagnitude;
                float t = lenSq < 1e-8f
                    ? 0f
                    : Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
                float d = Vector2.Distance(p, Vector2.Lerp(a, b, t));
                if (d < best)
                    best = d;
            }

            return best;
        }

        static float ScoreTributaryLakeConnection(
            GridSystem grid,
            List<Vector2> line,
            HashSet<long> lakeCells,
            float maxConnectDist,
            int nearIdx)
        {
            if (line == null || line.Count < 3 || lakeCells == null || lakeCells.Count == 0)
                return float.MaxValue;

            if (nearIdx < 0 || nearIdx >= line.Count)
            {
                if (!TryFindPolylineLakeApproach(line, lakeCells, maxConnectDist, out nearIdx, out _, out _, out _))
                    return float.MaxValue;
            }

            Vector2 nearPt = line[nearIdx];
            if (!TryGetNearestLakePointInComponent(nearPt, lakeCells, out Vector2 lakePt, out float nearDist))
                return float.MaxValue;
            if (nearDist > maxConnectDist)
                return float.MaxValue;

            int mainIdx = ResolveTributaryMainEndpointIndex(grid, line);
            Vector2 mainEnd = line[mainIdx];
            TryGetNearestLakePointInComponent(mainEnd, lakeCells, out _, out float mainLakeDist);
            if (nearDist > maxConnectDist && mainLakeDist > maxConnectDist)
                return float.MaxValue;

            float direct = Vector2.Distance(lakePt, mainEnd);
            if (direct < 4f)
                return float.MaxValue;

            float pathLen = mainIdx == nearIdx
                ? 0f
                : PolylineLengthBetweenIndices(
                    line,
                    Mathf.Min(mainIdx, nearIdx),
                    Mathf.Max(mainIdx, nearIdx));
            float ratio = pathLen / Mathf.Max(1f, direct);
            if (ratio > 3.35f)
                return float.MaxValue;

            if (MaxInteriorTurnDegWater(line) > 96f)
                return float.MaxValue;

            if (MaxTurnNearPathIndex(line, nearIdx) > 86f)
                return float.MaxValue;

            return nearDist + direct * 0.10f + (ratio - 1f) * 12f;
        }

        static float MaxTurnNearPathIndex(List<Vector2> line, int centerIdx, int window = 6)
        {
            if (line == null || line.Count < 3)
                return 0f;
            int i0 = Mathf.Max(1, centerIdx - window);
            int i1 = Mathf.Min(line.Count - 2, centerIdx + window);
            float max = 0f;
            for (int i = i0; i <= i1; i++)
            {
                Vector2 a = line[i] - line[i - 1];
                Vector2 b = line[i + 1] - line[i];
                if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f)
                    continue;
                float dot = Mathf.Clamp(Vector2.Dot(a.normalized, b.normalized), -1f, 1f);
                max = Mathf.Max(max, Mathf.Acos(dot) * Mathf.Rad2Deg);
            }

            return max;
        }

        static bool TryGetNearestLakePointInComponent(
            Vector2 p,
            HashSet<long> lakeCells,
            out Vector2 lakePoint,
            out float distCheb)
        {
            lakePoint = p;
            distCheb = float.MaxValue;
            if (lakeCells == null || lakeCells.Count == 0)
                return false;

            foreach (long pk in lakeCells)
            {
                int x = (int)(pk >> 32);
                int z = (int)(uint)pk;
                float d = Mathf.Max(Mathf.Abs(p.x - (x + 0.5f)), Mathf.Abs(p.y - (z + 0.5f)));
                if (d < distCheb)
                {
                    distCheb = d;
                    lakePoint = new Vector2(x + 0.5f, z + 0.5f);
                }
            }

            return distCheb < float.MaxValue;
        }

        static float PolylineLengthBetweenIndices(List<Vector2> line, int i0, int i1)
        {
            if (line == null || line.Count < 2)
                return 0f;
            if (i0 > i1)
            {
                int t = i0;
                i0 = i1;
                i1 = t;
            }

            i0 = Mathf.Clamp(i0, 0, line.Count - 1);
            i1 = Mathf.Clamp(i1, 0, line.Count - 1);
            float sum = 0f;
            for (int i = i0; i < i1; i++)
                sum += Vector2.Distance(line[i], line[i + 1]);
            return sum;
        }

        static float MaxInteriorTurnDegWater(List<Vector2> poly)
        {
            if (poly == null || poly.Count < 3)
                return 0f;
            float max = 0f;
            for (int i = 1; i < poly.Count - 1; i++)
            {
                Vector2 a = poly[i] - poly[i - 1];
                Vector2 b = poly[i + 1] - poly[i];
                if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f)
                    continue;
                float dot = Mathf.Clamp(Vector2.Dot(a.normalized, b.normalized), -1f, 1f);
                max = Mathf.Max(max, Mathf.Acos(dot) * Mathf.Rad2Deg);
            }

            return max;
        }

        static float MaxTurnNearLakeEndpoint(List<Vector2> line, bool atStart, int window = 6)
        {
            if (line == null || line.Count < 3)
                return 0f;
            float max = 0f;
            if (atStart)
            {
                int n = Mathf.Min(window, line.Count - 2);
                for (int i = 1; i <= n; i++)
                {
                    Vector2 a = line[i] - line[i - 1];
                    Vector2 b = line[i + 1] - line[i];
                    if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f)
                        continue;
                    float dot = Mathf.Clamp(Vector2.Dot(a.normalized, b.normalized), -1f, 1f);
                    max = Mathf.Max(max, Mathf.Acos(dot) * Mathf.Rad2Deg);
                }
            }
            else
            {
                int start = Mathf.Max(1, line.Count - window - 1);
                for (int i = start; i < line.Count - 1; i++)
                {
                    Vector2 a = line[i] - line[i - 1];
                    Vector2 b = line[i + 1] - line[i];
                    if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f)
                        continue;
                    float dot = Mathf.Clamp(Vector2.Dot(a.normalized, b.normalized), -1f, 1f);
                    max = Mathf.Max(max, Mathf.Acos(dot) * Mathf.Rad2Deg);
                }
            }

            return max;
        }

        /// <summary>Una sola pasada: River con vecino Water (8-dir) pasa a Water para confluencias coherentes.</summary>
        static void MergeRiverCellsTouchingLake(GridSystem grid)
        {
            int gw = grid.Width;
            int gh = grid.Height;
            var toConvert = new List<Vector2Int>(64);
            for (int z = 0; z < gh; z++)
            {
                for (int x = 0; x < gw; x++)
                {
                    ref var cell = ref grid.GetCell(x, z);
                    if (cell.type != CellType.River)
                        continue;
                    if (!CellTouchesWater8(grid, x, z))
                        continue;
                    toConvert.Add(new Vector2Int(x, z));
                }
            }

            foreach (var c in toConvert)
            {
                ref var cell = ref grid.GetCell(c.x, c.y);
                cell.type = CellType.Water;
                cell.riverFord = false;
                cell.walkable = false;
                cell.buildable = false;
                if (cell.waterTraverse == WaterTraverseMode.FordShallow)
                    cell.waterTraverse = WaterTraverseMode.SwimNavigable;
                else if (cell.waterTraverse == WaterTraverseMode.NotWater)
                    cell.waterTraverse = WaterTraverseMode.SwimNavigable;
            }
        }

        static bool CellTouchesWater8(GridSystem grid, int x, int z)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    int nx = x + dx;
                    int nz = z + dz;
                    if (!grid.InBoundsCell(nx, nz))
                        continue;
                    if (grid.GetCell(nx, nz).type == CellType.Water)
                        return true;
                }
            }
            return false;
        }

        /// <summary>Marca como <see cref="WaterTraverseMode.Impassable"/> el agua muy interior lejos de la orilla (solo celdas Water).</summary>
        static void ApplyLakeDeepImpassableCore(GridSystem grid, int minDistFromShoreCells)
        {
            if (minDistFromShoreCells <= 0)
                return;
            int gw = grid.Width;
            int gh = grid.Height;
            var dist = new int[gw, gh];
            for (int z = 0; z < gh; z++)
                for (int x = 0; x < gw; x++)
                    dist[x, z] = -1;

            var qx = new Queue<int>();
            var qz = new Queue<int>();
            for (int z = 0; z < gh; z++)
            {
                for (int x = 0; x < gw; x++)
                {
                    if (grid.GetCell(x, z).type != CellType.Water)
                        continue;
                    if (!WaterCellIsShore4(grid, x, z))
                        continue;
                    dist[x, z] = 0;
                    qx.Enqueue(x);
                    qz.Enqueue(z);
                }
            }

            while (qx.Count > 0)
            {
                int x = qx.Dequeue();
                int z = qz.Dequeue();
                int d = dist[x, z];
                void Try(int nx, int nz)
                {
                    if (!grid.InBoundsCell(nx, nz))
                        return;
                    if (grid.GetCell(nx, nz).type != CellType.Water)
                        return;
                    if (dist[nx, nz] != -1)
                        return;
                    dist[nx, nz] = d + 1;
                    qx.Enqueue(nx);
                    qz.Enqueue(nz);
                }
                Try(x - 1, z);
                Try(x + 1, z);
                Try(x, z - 1);
                Try(x, z + 1);
            }

            for (int z = 0; z < gh; z++)
            {
                for (int x = 0; x < gw; x++)
                {
                    if (grid.GetCell(x, z).type != CellType.Water)
                        continue;
                    int d = dist[x, z];
                    if (d < 0)
                        continue;
                    if (d >= minDistFromShoreCells)
                    {
                        ref var cell = ref grid.GetCell(x, z);
                        cell.waterTraverse = WaterTraverseMode.Impassable;
                        cell.walkable = false;
                    }
                }
            }
        }

        static bool WaterCellIsShore4(GridSystem grid, int x, int z)
        {
            if (grid.GetCell(x, z).type != CellType.Water)
                return false;
            foreach (var n in grid.Neighbors4(x, z))
            {
                if (grid.GetCell(n.x, n.y).type == CellType.Land)
                    return true;
            }
            return false;
        }

        /// <summary>Destino del río en el borde opuesto al de inicio (cruce del mapa, sin sesgo al centro).</summary>
        private static long PackCellLong(Vector2Int c) => ((long)c.x << 32) | (uint)c.y;

        private static int CountMaskDegree4(bool[,] mask, int w, int h, int x, int z)
        {
            int n = 0;
            for (int i = 0; i < Cardinal4.Length; i++)
            {
                int nx = x + Cardinal4[i].x;
                int nz = z + Cardinal4[i].y;
                if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                    continue;
                if (mask[nx, nz])
                    n++;
            }

            return n;
        }

        /// <summary>Misma decisión que el antiguo par puente+BFS en fusión: centerline o articulación (grado ≥ 2) sobre <paramref name="riverBase"/>.</summary>
        private static bool IsFusionRiverRemovalCriticalTarjan(
            bool[,] riverBase,
            bool[,] fusionArticulation,
            int w,
            int h,
            int x,
            int z,
            HashSet<long> centerlinePacked)
        {
            if (!riverBase[x, z])
                return false;
            if (centerlinePacked != null && centerlinePacked.Contains(PackCellLong(new Vector2Int(x, z))))
                return true;
            if (CountMaskDegree4(riverBase, w, h, x, z) < 2)
                return false;
            return fusionArticulation[x, z];
        }

        /// <summary>Radio máximo del corredor del río (base + variación) para evitar cruces inválidos al colocar otro río.</summary>
        private static int RiverCorridorMaxRadiusCells(MapGenConfig config)
        {
            if (config == null) return 1;
            int b = ScaledRiverRasterBaseRadius(config);
            int a = ScaledRiverRasterWidthAmplitude(config);
            return Mathf.Clamp(b + a, 0, 6);
        }

        private static void CollectRiverCorridorPackedInto(List<Vector2Int> axis, MapGenConfig config, int gw, int gh, HashSet<long> into)
        {
            into.Clear();
            if (axis == null) return;
            int radiusCells = RiverCorridorMaxRadiusCells(config);
            s_axisCentersScratch.Clear();
            for (int i = 0; i < axis.Count; i++)
                s_axisCentersScratch.Add(axis[i]);
            int rSq = radiusCells * radiusCells;
            foreach (var c in s_axisCentersScratch)
            {
                into.Add(PackCellLong(c));
                if (radiusCells <= 0) continue;
                for (int dz = -radiusCells; dz <= radiusCells; dz++)
                {
                    for (int dx = -radiusCells; dx <= radiusCells; dx++)
                    {
                        if (config.riverExpandEuclidean)
                        {
                            if (dx * dx + dz * dz > rSq) continue;
                        }
                        else if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > radiusCells)
                            continue;
                        int x = c.x + dx;
                        int z = c.y + dz;
                        if ((uint)x >= (uint)gw || (uint)z >= (uint)gh) continue;
                        into.Add(PackCellLong(new Vector2Int(x, z)));
                    }
                }
            }
        }

        /// <summary>
        /// Ensancha el vado al ancho del cauce: celdas River vecinas (Chebyshev) pasan a transitable y riverFord.
        /// Debe ejecutarse después de <see cref="ExpandRiverWidthAroundPath"/>.
        /// </summary>
        private static void ApplyRiverFordCorridor(GridSystem grid, List<Vector2Int> fordSeeds, int radiusChebyshev)
        {
            int r = Mathf.Clamp(radiusChebyshev, 0, 3);
            if (r <= 0 || fordSeeds == null) return;
            int gw = grid.Width;
            int gh = grid.Height;
            foreach (var seed in fordSeeds)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > r)
                            continue;
                        int x = seed.x + dx;
                        int z = seed.y + dz;
                        if ((uint)x >= (uint)gw || (uint)z >= (uint)gh)
                            continue;
                        ref var cell = ref grid.GetCell(x, z);
                        if (cell.type != CellType.River)
                            continue;
                        cell.riverFord = true;
                        cell.walkable = true;
                        cell.buildable = false;
                        cell.waterTraverse = WaterTraverseMode.FordShallow;
                    }
                }
            }
        }

        private static bool CorridorIntersectsOccupied(HashSet<long> corridor, HashSet<long> occupied)
        {
            if (corridor == null || occupied == null || occupied.Count == 0) return false;
            foreach (long k in corridor)
            {
                if (occupied.Contains(k))
                    return true;
            }
            return false;
        }

        static float MinDistSqPointToPolylineCellSpace(Vector2 p, List<Vector2> poly)
        {
            if (poly == null || poly.Count < 2)
                return float.MaxValue;
            float best = float.MaxValue;
            for (int i = 0; i < poly.Count - 1; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[i + 1];
                Vector2 ab = b - a;
                float denom = ab.sqrMagnitude;
                float t = denom < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
                Vector2 proj = a + ab * t;
                float d2 = (p - proj).sqrMagnitude;
                if (d2 < best)
                    best = d2;
            }

            return best;
        }

        static int? TryResolveParentRiverAtJoin(GridSystem grid, Vector2Int joinCell, int previousRiverCount, out float bestDistSq)
        {
            bestDistSq = float.MaxValue;
            if (grid?.RiverCenterlinesCellSpace == null || previousRiverCount <= 0)
                return null;

            Vector2 jp = new Vector2(joinCell.x + 0.5f, joinCell.y + 0.5f);
            int bestR = -1;
            float best = float.MaxValue;
            for (int r = 0; r < previousRiverCount; r++)
            {
                var line = grid.RiverCenterlinesCellSpace[r];
                if (line == null || line.Count < 2)
                    continue;
                float d2 = MinDistSqPointToPolylineCellSpace(jp, line);
                if (d2 < best)
                {
                    best = d2;
                    bestR = r;
                }
            }

            bestDistSq = best;
            if (bestR < 0)
                return null;
            return bestR;
        }

        /// <summary>
        /// Recorta el río candidato hasta su primera confluencia con el corredor de ríos existentes.
        /// Evita cruces tipo "X": el candidato pasa a ser afluente y termina en la unión.
        /// </summary>
        private static bool TryTrimRiverPathToFirstConfluence(
            GridSystem grid,
            List<Vector2Int> path,
            List<Vector2> centerline,
            HashSet<long> occupiedRiverCorridor,
            int gw,
            int gh,
            int minCellsAfterTrim,
            out int joinPathIndex,
            out Vector2Int joinCell)
        {
            joinPathIndex = -1;
            joinCell = default;
            if (grid == null || path == null || path.Count < 12 || occupiedRiverCorridor == null || occupiedRiverCorridor.Count == 0)
                return false;

            int minAfter = Mathf.Clamp(minCellsAfterTrim, 6, 64);

            int hitIdx = -1;
            int startScan = Mathf.Clamp(path.Count / 8, 3, path.Count - 2);
            for (int i = startScan; i < path.Count; i++)
            {
                var c = path[i];
                if ((uint)c.x >= (uint)gw || (uint)c.y >= (uint)gh)
                    continue;
                if (occupiedRiverCorridor.Contains(PackCellLong(c)))
                {
                    hitIdx = i;
                    break;
                }
            }

            if (hitIdx < 0 || hitIdx < 7)
                return false;

            // Confluencia real: mover el punto de corte al primer punto que ya sea celda River existente.
            int realJoinIdx = -1;
            int scanFrom = Mathf.Max(0, hitIdx - 3);
            int scanTo = Mathf.Min(path.Count - 1, hitIdx + 5);
            for (int i = scanFrom; i <= scanTo; i++)
            {
                var c = path[i];
                if ((uint)c.x >= (uint)gw || (uint)c.y >= (uint)gh) continue;
                ref var cell = ref grid.GetCell(c.x, c.y);
                if (cell.type == CellType.River)
                {
                    realJoinIdx = i;
                    break;
                }
            }
            // Si no tocamos una celda River real, no es confluencia válida (evita "corte" visual).
            if (realJoinIdx < 0)
                return false;
            hitIdx = realJoinIdx;
            joinPathIndex = hitIdx;
            joinCell = path[hitIdx];

            int removeStart = hitIdx + 1;
            if (removeStart < path.Count)
                path.RemoveRange(removeStart, path.Count - removeStart);

            if (centerline != null && centerline.Count > 4)
            {
                // Forzar que la malla de río termine exactamente en el raster recortado
                // para evitar "segmento cortado" visual en la confluencia.
                centerline.Clear();
                for (int i = 0; i < path.Count; i++)
                {
                    Vector2Int c = path[i];
                    centerline.Add(new Vector2(c.x + 0.5f, c.y + 0.5f));
                }
            }

            // Evita afluentes demasiado cortos que generan parches/segmentos visuales extraños.
            if (path.Count < minAfter)
            {
                joinPathIndex = -1;
                joinCell = default;
                return false;
            }

            return true;
        }

        private static void TrimFordCellsToPath(List<Vector2Int> fordCells, List<Vector2Int> path)
        {
            if (fordCells == null || path == null) return;
            var pathSet = new HashSet<long>();
            for (int i = 0; i < path.Count; i++)
                pathSet.Add(PackCellLong(path[i]));
            for (int i = fordCells.Count - 1; i >= 0; i--)
            {
                if (!pathSet.Contains(PackCellLong(fordCells[i])))
                    fordCells.RemoveAt(i);
            }
        }

        private static int FillSmallLandIslandsInsideWater(GridSystem grid, int maxIslandCells)
        {
            if (grid == null) return 0;
            int w = grid.Width;
            int h = grid.Height;
            var visited = new bool[w, h];
            int converted = 0;
            int cap = Mathf.Max(1, maxIslandCells);
            var q = new Queue<Vector2Int>(cap * 2);
            var region = new List<Vector2Int>(cap * 2);

            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                if (visited[x, z]) continue;
                ref var start = ref grid.GetCell(x, z);
                if (start.type != CellType.Land) { visited[x, z] = true; continue; }

                q.Clear();
                region.Clear();
                bool touchesBorder = false;
                bool enclosedByWater = true;
                q.Enqueue(new Vector2Int(x, z));
                visited[x, z] = true;

                while (q.Count > 0 && region.Count <= cap)
                {
                    var c = q.Dequeue();
                    region.Add(c);
                    if (c.x == 0 || c.y == 0 || c.x == w - 1 || c.y == h - 1)
                        touchesBorder = true;

                    foreach (var n in grid.Neighbors8(c.x, c.y))
                    {
                        if (!grid.InBoundsCell(n.x, n.y)) continue;
                        ref var nc = ref grid.GetCell(n.x, n.y);
                        if (nc.type == CellType.Land)
                        {
                            if (!visited[n.x, n.y])
                            {
                                visited[n.x, n.y] = true;
                                q.Enqueue(n);
                            }
                        }
                        else if (nc.type != CellType.Water && nc.type != CellType.River)
                        {
                            enclosedByWater = false;
                        }
                    }
                }

                if (region.Count == 0) continue;
                if (region.Count > cap || touchesBorder || !enclosedByWater) continue;

                for (int i = 0; i < region.Count; i++)
                {
                    var c = region[i];
                    ref var cell = ref grid.GetCell(c.x, c.y);
                    if (cell.type != CellType.Land) continue;
                    cell.type = CellType.Water;
                    cell.walkable = false;
                    cell.buildable = false;
                    cell.riverFord = false;
                    cell.waterTraverse = WaterTraverseMode.SwimNavigable;
                    converted++;
                }
            }

            return converted;
        }

        /// <summary>
        /// Ensancha el río en el grid tras el eje: disco euclídeo o cuadrado Chebyshev según config (menos “manhattan” visual con euclídeo).
        /// </summary>
        private static int ExpandRiverWidthAroundPath(GridSystem grid, List<Vector2Int> path, MapGenConfig config, int riverIndex)
        {
            if (path == null || path.Count == 0 || config == null) return 0;
            int baseR = ScaledRiverRasterBaseRadius(config);
            int amp = ScaledRiverRasterWidthAmplitude(config);
            if (baseR <= 0 && amp <= 0) return 0;

            int seedMix = config.seed ^ (riverIndex * 739391);
            int added = 0;
            int previousRadius = baseR;
            for (int pi = 0; pi < path.Count; pi++)
            {
                Vector2Int c = path[pi];
                int delta = 0;
                if (amp > 0)
                {
                    float t = path.Count <= 1 ? 0f : pi / (float)(path.Count - 1);
                    float lowFreq = Mathf.PerlinNoise(seedMix * 0.0137f + t * 1.15f, riverIndex * 0.193f + 0.31f);
                    float midFreq = Mathf.PerlinNoise(seedMix * 0.0271f + t * 2.4f, riverIndex * 0.317f + 7.13f);
                    float smooth = Mathf.Lerp(lowFreq, midFreq, 0.35f);
                    delta = Mathf.RoundToInt((smooth - 0.5f) * 2f * amp);
                }
                int targetRadius = Mathf.Clamp(baseR + delta, 0, 6);
                int r = pi == 0
                    ? targetRadius
                    : Mathf.Clamp(targetRadius, previousRadius - 1, previousRadius + 1);
                previousRadius = r;
                if (r <= 0) continue;

                int rSq = r * r;
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (config.riverExpandEuclidean)
                        {
                            if (dx * dx + dz * dz > rSq) continue;
                        }
                        else if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > r)
                            continue;
                        int x = c.x + dx;
                        int z = c.y + dz;
                        if (!grid.InBoundsCell(x, z)) continue;
                        ref var cell = ref grid.GetCell(x, z);
                        if (cell.type != CellType.Land) continue;
                        cell.type = CellType.River;
                        cell.riverFord = false;
                        cell.walkable = false;
                        cell.buildable = false;
                        cell.waterTraverse = WaterTraverseMode.SwimNavigable;
                        added++;
                    }
                }
            }
            return added;
        }

        /// <summary>
        /// BFS desde el borde del lago hacia celdas River (sin vados): ensancha la confluencia de forma orgánica.
        /// </summary>
        private static int AbsorbRiverMouthIntoLake(GridSystem grid, MapGenConfig config)
        {
            int depth = config != null ? Mathf.Clamp(config.lakeRiverMouthBlendCells, 0, 8) : 0;
            if (depth <= 0 || grid.LakeBodyCellsPacked == null || grid.LakeBodyCellsPacked.Count == 0)
                return 0;

            var q = new Queue<(int x, int z, int dist)>();
            var seen = new HashSet<long>();

            foreach (long pk in grid.LakeBodyCellsPacked)
            {
                int lx = (int)(pk >> 32);
                int lz = (int)(uint)pk;
                if (!grid.InBoundsCell(lx, lz)) continue;
                foreach (var n in grid.Neighbors8(lx, lz))
                {
                    ref var nc = ref grid.GetCell(n.x, n.y);
                    if (nc.type != CellType.River || nc.riverFord)
                        continue;
                    long nk = PackCellLong(n);
                    if (seen.Add(nk))
                        q.Enqueue((n.x, n.y, 1));
                }
            }

            int added = 0;
            while (q.Count > 0)
            {
                var (x, z, d) = q.Dequeue();
                if (d > depth)
                    continue;

                ref var cell = ref grid.GetCell(x, z);
                if (cell.type != CellType.River || cell.riverFord)
                    continue;

                cell.type = CellType.Water;
                cell.riverFord = false;
                cell.walkable = false;
                cell.buildable = false;
                if (cell.waterTraverse == WaterTraverseMode.FordShallow || cell.waterTraverse == WaterTraverseMode.NotWater)
                    cell.waterTraverse = WaterTraverseMode.SwimNavigable;
                grid.LakeBodyCellsPacked.Add(PackCellLong(new Vector2Int(x, z)));
                added++;

                if (d >= depth)
                    continue;

                foreach (var n in grid.Neighbors8(x, z))
                {
                    ref var nb = ref grid.GetCell(n.x, n.y);
                    if (nb.type != CellType.River || nb.riverFord)
                        continue;
                    long nk = PackCellLong(n);
                    if (seen.Add(nk))
                        q.Enqueue((n.x, n.y, d + 1));
                }
            }

            return added;
        }

        struct LakeShapeControl
        {
            public float radiusX;
            public float radiusZ;
            public float cos;
            public float sin;
            public float envelopeNoise;
            public float edgeFade;
        }

        static LakeShapeControl BuildLakeShapeControl(Vector2Int seed, int maxCells, MapGenConfig config)
        {
            float irregularity = config != null ? Mathf.Clamp01(config.lakeOrganicIrregularity) : 0f;
            int salt = config != null ? config.seed : 0;
            float baseRadius = Mathf.Sqrt(Mathf.Max(12f, maxCells) / Mathf.PI);
            float anisotropyA = Mathf.Lerp(0.9f, 1.35f, LakeExpandHash01(seed.x, seed.y, salt + 1703));
            float anisotropyB = Mathf.Lerp(0.85f, 1.25f, LakeExpandHash01(seed.x, seed.y, salt + 2903));
            float angle = LakeExpandHash01(seed.x, seed.y, salt + 4703) * Mathf.PI * 2f;

            return new LakeShapeControl
            {
                radiusX = Mathf.Max(2.5f, baseRadius * anisotropyA),
                radiusZ = Mathf.Max(2.5f, baseRadius * anisotropyB),
                cos = Mathf.Cos(angle),
                sin = Mathf.Sin(angle),
                envelopeNoise = Mathf.Lerp(0.08f, 0.34f, irregularity),
                edgeFade = Mathf.Lerp(0.18f, 0.42f, irregularity)
            };
        }

        static bool IsInsideLakeEnvelope(Vector2Int seed, Vector2Int point, LakeShapeControl shape, int salt, float irregularity)
        {
            float dx = point.x - seed.x;
            float dz = point.y - seed.y;
            float lx = dx * shape.cos - dz * shape.sin;
            float lz = dx * shape.sin + dz * shape.cos;
            float nx = lx / Mathf.Max(1f, shape.radiusX);
            float nz = lz / Mathf.Max(1f, shape.radiusZ);
            float dist01 = Mathf.Sqrt(nx * nx + nz * nz);
            float warp = (LakeExpandHash01(point.x, point.y, salt + 6113) - 0.5f) * 2f * shape.envelopeNoise;
            float envelope = 1f + warp;
            if (dist01 <= Mathf.Max(0.18f, envelope - shape.edgeFade))
                return true;

            if (dist01 > envelope)
                return false;

            float rim = Mathf.InverseLerp(envelope, Mathf.Max(0.19f, envelope - shape.edgeFade), dist01);
            float roll = LakeExpandHash01(point.x, point.y, salt + 7121);
            float threshold = Mathf.Lerp(0.12f, 0.7f, rim);
            threshold = Mathf.Lerp(threshold, 0.92f, irregularity * 0.28f);
            return roll <= threshold;
        }

        /// <summary>Hash determinista [0,1) para expansiones de lago (reproducible con seed del mapa).</summary>
        private static float LakeExpandHash01(int x, int z, int salt)
        {
            unchecked
            {
                int h = x * 374761393 + z * 668265263 + salt * 1442695041;
                h ^= h >> 13;
                h *= 1274126177;
                uint u = (uint)h;
                return (u & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>Distancia Chebyshev mínima de una celda semilla a la huella muestreada de la centerline del río principal (celdas).</summary>
        static int MinChebyshevSeedToMainRiverCenterline(int cx, int cz, List<Vector2> mainCenterlineCellSpace, int gw, int gh)
        {
            if (mainCenterlineCellSpace == null || mainCenterlineCellSpace.Count == 0)
                return 99999;
            int best = 99999;
            for (int i = 0; i < mainCenterlineCellSpace.Count; i++)
            {
                int x = Mathf.Clamp(Mathf.FloorToInt(mainCenterlineCellSpace[i].x), 0, gw - 1);
                int z = Mathf.Clamp(Mathf.FloorToInt(mainCenterlineCellSpace[i].y), 0, gh - 1);
                int d = Mathf.Max(Mathf.Abs(cx - x), Mathf.Abs(cz - z));
                if (d < best)
                    best = d;
            }

            for (int i = 0; i < mainCenterlineCellSpace.Count - 1; i++)
            {
                Vector2 a = mainCenterlineCellSpace[i];
                Vector2 b = mainCenterlineCellSpace[i + 1];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) * 2f));
                for (int s = 0; s <= steps; s++)
                {
                    Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                    int x = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, gw - 1);
                    int z = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, gh - 1);
                    int d = Mathf.Max(Mathf.Abs(cx - x), Mathf.Abs(cz - z));
                    if (d < best)
                        best = d;
                }
            }

            return best;
        }

        /// <summary>
        /// Flood fill desde seed; solo Land; máximo maxCells. 8 direcciones + irregularidad y semillas extra.
        /// </summary>
        private static int FloodFillLake(GridSystem grid, Vector2Int seed, int maxCells, MapGenConfig config)
        {
            float ir = config != null ? Mathf.Clamp01(config.lakeOrganicIrregularity) : 0f;
            int spread = config != null ? Mathf.Clamp(config.lakeExtraSeedSpreadCells, 0, 10) : 0;
            int salt = config != null ? config.seed : 0;
            LakeShapeControl shape = BuildLakeShapeControl(seed, maxCells, config);

            var queue = new Queue<Vector2Int>();
            var visited = new HashSet<Vector2Int>();

            void TryEnqueueSeed(Vector2Int p)
            {
                if (!grid.InBoundsCell(p.x, p.y) || visited.Contains(p)) return;
                if (grid.GetCell(p.x, p.y).type != CellType.Land) return;
                visited.Add(p);
                queue.Enqueue(p);
            }

            TryEnqueueSeed(seed);

            if (spread > 0 && config != null)
            {
                for (int dz = -spread; dz <= spread; dz++)
                {
                    for (int dx = -spread; dx <= spread; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        var p = new Vector2Int(seed.x + dx, seed.y + dz);
                        if (!grid.InBoundsCell(p.x, p.y)) continue;
                        if (grid.GetCell(p.x, p.y).type != CellType.Land) continue;
                        if (!IsInsideLakeEnvelope(seed, p, shape, salt + 9029, ir)) continue;
                        float h = LakeExpandHash01(p.x, p.y, salt + 9029);
                        float thresh = Mathf.Lerp(0.9f, 0.26f, ir);
                        if (h < thresh) continue;
                        TryEnqueueSeed(p);
                    }
                }
            }

            int count = 0;
            System.Random localRng = new System.Random(seed.x * 1000 + seed.y + salt * 17);

            while (queue.Count > 0 && count < maxCells)
            {
                var c = queue.Dequeue();
                ref var cell = ref grid.GetCell(c);
                if (cell.type != CellType.Land) continue;

                cell.type = CellType.Water;
                cell.walkable = false;
                cell.buildable = false;
                cell.riverFord = false;
                cell.waterTraverse = WaterTraverseMode.SwimNavigable;
                count++;
                if (grid.LakeBodyCellsPacked != null)
                    grid.LakeBodyCellsPacked.Add(PackCellLong(c));

                var directions = new List<Vector2Int>(AllDirections);
                for (int i = directions.Count - 1; i > 0; i--)
                {
                    int j = localRng.Next(i + 1);
                    (directions[i], directions[j]) = (directions[j], directions[i]);
                }

                foreach (var dir in directions)
                {
                    var n = new Vector2Int(c.x + dir.x, c.y + dir.y);
                    if (!grid.InBoundsCell(n.x, n.y) || visited.Contains(n)) continue;
                    ref var ncell = ref grid.GetCell(n);
                    if (ncell.type != CellType.Land) continue;
                    if (!IsInsideLakeEnvelope(seed, n, shape, salt + 4049, ir)) continue;

                    bool isDiagonal = Mathf.Abs(dir.x) == 1 && Mathf.Abs(dir.y) == 1;
                    float hN = LakeExpandHash01(n.x, n.y, salt + 4049);
                    float hD = LakeExpandHash01(n.x, n.y, salt + 8081);
                    float cardBase = Mathf.Lerp(0.92f, 0.52f + 0.44f * hN, ir);
                    float diagMul = Mathf.Lerp(0.82f, 0.45f + 0.48f * hD, ir);
                    float expandChance = isDiagonal ? cardBase * diagMul : cardBase;

                    float roll = LakeExpandHash01(n.x, n.y, salt + dir.x * 131 + dir.y * 171 + count * 19);
                    if (roll < expandChance)
                    {
                        visited.Add(n);
                        queue.Enqueue(n);
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// Marca vados funcionales en tramos delgados del río usando distancia interior al borde.
        /// No cambia la topología del agua: solo transitabilidad en celdas River.
        /// </summary>
        /// <summary>Llamado desde Fase4 hidrología (si no hay modo asset) o como fallback desde WaterMeshBuilder.</summary>
        public static void ApplyFunctionalRiverFordsFromThinZones(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null || !config.enableFunctionalRiverFords)
                return;

            int w = grid.Width;
            int h = grid.Height;
            int maxPick = Mathf.Max(0, config.riverCrossingMaxPerMap);
            if (maxPick <= 0)
                return;

            var dist = new int[w, h];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    dist[x, z] = -1;

            var q = new Queue<Vector2Int>();
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    ref var c = ref grid.GetCell(x, z);
                    if (c.type != CellType.River)
                        continue;
                    bool boundary = false;
                    foreach (var n in grid.Neighbors4(x, z))
                    {
                        if (grid.GetCell(n.x, n.y).type != CellType.River)
                        {
                            boundary = true;
                            break;
                        }
                    }
                    if (boundary)
                    {
                        dist[x, z] = 0;
                        q.Enqueue(new Vector2Int(x, z));
                    }
                }
            }

            while (q.Count > 0)
            {
                var p = q.Dequeue();
                int d = dist[p.x, p.y];
                foreach (var n in grid.Neighbors4(p.x, p.y))
                {
                    if (grid.GetCell(n.x, n.y).type != CellType.River)
                        continue;
                    if (dist[n.x, n.y] >= 0)
                        continue;
                    dist[n.x, n.y] = d + 1;
                    q.Enqueue(n);
                }
            }

            int bankSearchClamped = Mathf.Clamp(config.riverCrossingBankSearchCells, 4, 20);

            var candidates = new List<Vector2Int>(128);
            int thinThreshold = Mathf.Max(0, config.riverCrossingMaxThicknessCells / 2);
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    ref var c = ref grid.GetCell(x, z);
                    if (c.type != CellType.River || c.riverFord)
                        continue;
                    if (dist[x, z] < 0 || dist[x, z] > thinThreshold)
                        continue;
                    if (!WaterMeshBuilder.IsCrossingCandidatePlacementValid(grid, new Vector2Int(x, z), bankSearchClamped))
                        continue;
                    candidates.Add(new Vector2Int(x, z));
                }
            }

            candidates.Sort((a, b) =>
            {
                uint ha = (uint)(a.x * 73856093 ^ a.y * 19349663 ^ config.seed * 83492791);
                uint hb = (uint)(b.x * 73856093 ^ b.y * 19349663 ^ config.seed * 83492791);
                return ha.CompareTo(hb);
            });

            int spacing = Mathf.Max(2, config.riverCrossingMinSpacing);
            var picked = new List<Vector2Int>(maxPick);
            foreach (var c in candidates)
            {
                if (picked.Count >= maxPick)
                    break;
                bool farEnough = true;
                for (int i = 0; i < picked.Count; i++)
                {
                    int ch = Mathf.Max(Mathf.Abs(picked[i].x - c.x), Mathf.Abs(picked[i].y - c.y));
                    if (ch < spacing) { farEnough = false; break; }
                }
                if (!farEnough)
                    continue;
                picked.Add(c);
            }

            var thinMarked = new HashSet<Vector2Int>();
            int corridorsPlaced = 0;
            foreach (var p in picked)
            {
                if (WaterMeshBuilder.TryApplyBankToBankFordCorridor(grid, config, p, thinMarked, null, out string rej))
                {
                    corridorsPlaced++;
                }
                else if (config.debugLogs || config.riverCrossingCorridorDebugLogs)
                {
                    Debug.Log($"[RiverFordThin] skip center={p} reason={rej}");
                }
            }

            int applied = thinMarked.Count;

            if (applied > 0 || config.debugLogs || config.riverCrossingCorridorDebugLogs)
            {
                Debug.Log(
                    $"[RiverFord] source=thin_zones corridors={corridorsPlaced} cells={applied} seed={config.seed} spacing={spacing} " +
                    $"thinThreshold={thinThreshold} maxPerMap={maxPick}");
            }
        }

        private static readonly Vector2Int[] Cardinal4 =
        {
            new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(-1, 0)
        };

        /// <summary>True si convertir esta celda acuática a Land rompería 4-conectividad entre pares de vecinos cardinales, o es ford/centerline.</summary>
        private static bool IsMaskConnectivityCritical(
            bool[,] mask, int w, int h, int x, int z, HashSet<long> centerlinePacked, bool fordCell,
            Queue<Vector2Int> q, int[,] visitStamp, ref int stamp,
            WaterPerfCaller perfCaller = WaterPerfCaller.None)
        {
            if (WaterGenPerfDiag.Active && perfCaller != WaterPerfCaller.None)
            {
                if (perfCaller == WaterPerfCaller.FusionRiverRemovalProbe)
                    WaterGenPerfDiag.MaskCriticalCallsFusion++;
                else if (perfCaller == WaterPerfCaller.TopologyTryRemove)
                    WaterGenPerfDiag.MaskCriticalCallsTopology++;
            }

            if (!mask[x, z])
                return false;
            if (fordCell)
            {
                if (WaterGenPerfDiag.Active && perfCaller != WaterPerfCaller.None)
                {
                    if (perfCaller == WaterPerfCaller.FusionRiverRemovalProbe)
                        WaterGenPerfDiag.MaskCriticalReturnsTrueFusion++;
                    else if (perfCaller == WaterPerfCaller.TopologyTryRemove)
                        WaterGenPerfDiag.MaskCriticalReturnsTrueTopology++;
                }

                return true;
            }

            if (centerlinePacked != null && centerlinePacked.Contains(PackCellLong(new Vector2Int(x, z))))
            {
                if (WaterGenPerfDiag.Active && perfCaller != WaterPerfCaller.None)
                {
                    if (perfCaller == WaterPerfCaller.FusionRiverRemovalProbe)
                        WaterGenPerfDiag.MaskCriticalReturnsTrueFusion++;
                    else if (perfCaller == WaterPerfCaller.TopologyTryRemove)
                        WaterGenPerfDiag.MaskCriticalReturnsTrueTopology++;
                }

                return true;
            }

            bool bridge = MaskBridgePairDisconnectedWithoutCell(mask, w, h, x, z, q, visitStamp, ref stamp);
            if (bridge)
            {
                if (WaterGenPerfDiag.Active && perfCaller != WaterPerfCaller.None)
                {
                    if (perfCaller == WaterPerfCaller.FusionRiverRemovalProbe)
                        WaterGenPerfDiag.MaskCriticalReturnsTrueFusion++;
                    else if (perfCaller == WaterPerfCaller.TopologyTryRemove)
                        WaterGenPerfDiag.MaskCriticalReturnsTrueTopology++;
                }
            }

            return bridge;
        }

        private static bool MaskBridgePairDisconnectedWithoutCell(
            bool[,] mask, int w, int h, int bx, int bz, Queue<Vector2Int> q, int[,] visitStamp, ref int stamp)
        {
            if (WaterGenPerfDiag.Active)
                WaterGenPerfDiag.BridgeDisconnectedChecks++;

            var neigh = new List<Vector2Int>(4);
            for (int i = 0; i < Cardinal4.Length; i++)
            {
                int nx = bx + Cardinal4[i].x;
                int nz = bz + Cardinal4[i].y;
                if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                    continue;
                if (mask[nx, nz])
                    neigh.Add(new Vector2Int(nx, nz));
            }

            int n = neigh.Count;
            if (n < 2)
                return false;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (!MaskCellsReachableWithoutBlocking(mask, w, h, neigh[i], neigh[j], bx, bz, q, visitStamp, ref stamp))
                        return true;
                }
            }

            return false;
        }

        private static bool MaskCellsReachableWithoutBlocking(
            bool[,] mask, int w, int h, Vector2Int start, Vector2Int goal, int blockX, int blockZ,
            Queue<Vector2Int> q, int[,] visitStamp, ref int stamp)
        {
            if (start.x == goal.x && start.y == goal.y)
                return true;
            if (WaterGenPerfDiag.Active)
                WaterGenPerfDiag.BfsReachabilityCalls++;

            int gen = ++stamp;
            if (gen == int.MaxValue)
            {
                Array.Clear(visitStamp, 0, visitStamp.Length);
                stamp = 0;
                gen = ++stamp;
            }

            q.Clear();
            q.Enqueue(start);
            visitStamp[start.x, start.y] = gen;
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                if (WaterGenPerfDiag.Active)
                    WaterGenPerfDiag.BfsReachabilityNodesVisited++;
                if (c.x == goal.x && c.y == goal.y)
                    return true;
                for (int i = 0; i < Cardinal4.Length; i++)
                {
                    int nx = c.x + Cardinal4[i].x;
                    int nz = c.y + Cardinal4[i].y;
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                        continue;
                    if (nx == blockX && nz == blockZ)
                        continue;
                    if (!mask[nx, nz])
                        continue;
                    if (visitStamp[nx, nz] == gen)
                        continue;
                    visitStamp[nx, nz] = gen;
                    q.Enqueue(new Vector2Int(nx, nz));
                }
            }

            return false;
        }

        private static HashSet<long> BuildCenterlineRiverCellsPacked(GridSystem grid)
        {
            var cells = new HashSet<long>();
            var lines = grid.RiverCenterlinesCellSpace;
            if (lines == null)
                return cells;
            int w = grid.Width;
            int h = grid.Height;
            foreach (var line in lines)
            {
                if (line == null || line.Count == 0)
                    continue;
                for (int i = 0; i < line.Count; i++)
                {
                    var p = line[i];
                    int cx = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, w - 1);
                    int cz = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, h - 1);
                    cells.Add(PackCellLong(new Vector2Int(cx, cz)));
                }

                for (int i = 0; i < line.Count - 1; i++)
                    RasterCenterlineSegmentToPacked(cells, line[i], line[i + 1], w, h);
            }

            return cells;
        }

        private static void RasterCenterlineSegmentToPacked(HashSet<long> cells, Vector2 a, Vector2 b, int w, int h)
        {
            float x0 = Mathf.Clamp(a.x, 0f, w - 1f);
            float z0 = Mathf.Clamp(a.y, 0f, h - 1f);
            float x1 = Mathf.Clamp(b.x, 0f, w - 1f);
            float z1 = Mathf.Clamp(b.y, 0f, h - 1f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(z1 - z0))) * 2);
            for (int s = 0; s <= steps; s++)
            {
                float t = steps == 0 ? 0f : s / (float)steps;
                int xi = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), 0, w - 1);
                int zi = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(z0, z1, t)), 0, h - 1);
                cells.Add(PackCellLong(new Vector2Int(xi, zi)));
            }
        }

        private static void ApplyRiverFusionMaskAndShoreWalkability(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null)
                return;

            int w = grid.Width;
            int h = grid.Height;
            var riverBase = new bool[w, h];
            var preserveFord = new bool[w, h];
            var preserveCoreBlocked = new bool[w, h];

            int riverCount = 0;
            int preservedFordCount = 0;
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    ref var c = ref grid.GetCell(x, z);
                    bool isRiver = c.type == CellType.River;
                    riverBase[x, z] = isRiver;
                    preserveFord[x, z] = isRiver && c.riverFord;
                    preserveCoreBlocked[x, z] = isRiver && !c.riverFord;
                    if (preserveFord[x, z]) preservedFordCount++;
                    if (isRiver) riverCount++;
                }
            }

            if (riverCount <= 0)
            {
                DebugLastRiverFusionMask01 = null;
                DebugLastRiverFusionCoreMask = null;
                DebugLastRiverFusionShoreMask = null;
                DebugLastRiverFusionBlurField = null;
                return;
            }

            System.Diagnostics.Stopwatch swPrepBlur = WaterGenPerfDiag.Active ? System.Diagnostics.Stopwatch.StartNew() : null;

            var centerlinePacked = BuildCenterlineRiverCellsPacked(grid);
            int removedRiverToLand = 0;
            int preservedContinuity = 0;

            var a = new float[w, h];
            var b = new float[w, h];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    a[x, z] = riverBase[x, z] ? 1f : 0f;

            int blurPasses = Mathf.Clamp(config.riverFusionBlurPasses, 1, 6);
            for (int it = 0; it < blurPasses; it++)
            {
                for (int z = 0; z < h; z++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float sum = 0f;
                        int n = 0;
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int zz = z + dz;
                            if ((uint)zz >= (uint)h) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx;
                                if ((uint)xx >= (uint)w) continue;
                                sum += a[xx, zz];
                                n++;
                            }
                        }

                        b[x, z] = n > 0 ? sum / n : a[x, z];
                    }
                }

                var t = a; a = b; b = t;
            }

            if (swPrepBlur != null)
                WaterGenPerfDiag.MsFusionPrepBlur += swPrepBlur.Elapsed.TotalMilliseconds;

            float coreThreshold = Mathf.Clamp(config.riverFusionCoreThreshold, 0.35f, 0.9f);
            float shoreThreshold = Mathf.Clamp(config.riverFusionShoreThreshold, 0.05f, 0.7f);
            if (shoreThreshold >= coreThreshold)
                shoreThreshold = Mathf.Max(0.05f, coreThreshold - 0.05f);
            int shoreWidth = Mathf.Clamp(config.riverShoreWalkableWidthCells, 0, 2);
            var core = new bool[w, h];
            var shore = new bool[w, h];

            bool LandWithinChebyshev(int cx, int cz, int radius)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int nx = cx + dx;
                        int nz = cz + dz;
                        if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                            continue;
                        if (grid.GetCell(nx, nz).type == CellType.Land)
                            return true;
                    }
                }

                return false;
            }

            bool CoreWithinChebyshev(int cx, int cz, int radius)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int nx = cx + dx;
                        int nz = cz + dz;
                        if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                            continue;
                        if (core[nx, nz])
                            return true;
                    }
                }

                return false;
            }

            System.Diagnostics.Stopwatch swClassify = WaterGenPerfDiag.Active ? System.Diagnostics.Stopwatch.StartNew() : null;

            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (grid.GetCell(x, z).type == CellType.Water)
                        continue; // no tocar lagos aquí

                    float v = a[x, z];
                    if (preserveCoreBlocked[x, z] || v >= coreThreshold)
                    {
                        core[x, z] = true;
                        continue;
                    }

                    if (shoreWidth > 0
                        && v >= shoreThreshold
                        && LandWithinChebyshev(x, z, shoreWidth)
                        && CoreWithinChebyshev(x, z, Mathf.Max(1, shoreWidth)))
                        shore[x, z] = true;
                }
            }

            if (swClassify != null)
                WaterGenPerfDiag.MsFusionClassifyCoreShore += swClassify.Elapsed.TotalMilliseconds;

            System.Diagnostics.Stopwatch swTarjan = WaterGenPerfDiag.Active ? System.Diagnostics.Stopwatch.StartNew() : null;
            bool[,] fusionArticulation = GridArticulationPoints4.Compute(
                riverBase, w, h, out int tarjanNodes, out int tarjanEdges, out int tarjanArticCount);
            if (swTarjan != null)
                WaterGenPerfDiag.TarjanBuildMs += swTarjan.Elapsed.TotalMilliseconds;

            if (WaterGenPerfDiag.Active)
            {
                double tarjanMs = swTarjan != null ? swTarjan.Elapsed.TotalMilliseconds : 0.0;
                Debug.Log(
                    $"[WaterTarjan] nodes={tarjanNodes} edges={tarjanEdges} articulationCount={tarjanArticCount} ms={tarjanMs:F3}");
            }

            System.Diagnostics.Stopwatch swConnApply = WaterGenPerfDiag.Active ? System.Diagnostics.Stopwatch.StartNew() : null;

            int coreCount = 0;
            int shoreCount = 0;
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    ref var c = ref grid.GetCell(x, z);
                    if (c.type == CellType.Water)
                        continue;

                    bool isCore = core[x, z];
                    bool isShore = shore[x, z];
                    // En modo crossing-assets, los vados jugables deben venir de cruces oficiales,
                    // no de toda la franja de shore; así evitamos cruces "fantasma".
                    bool mustFord = config.useCrossingAssetFords
                        ? preserveFord[x, z]
                        : (preserveFord[x, z] || (isShore && !isCore));

                    if (!isCore && !isShore)
                    {
                        if (c.type == CellType.River && !preserveFord[x, z])
                        {
                            if (WaterGenPerfDiag.Active)
                                WaterGenPerfDiag.TarjanFusionLookups++;

                            if (IsFusionRiverRemovalCriticalTarjan(
                                    riverBase,
                                    fusionArticulation,
                                    w,
                                    h,
                                    x,
                                    z,
                                    centerlinePacked))
                            {
                                preservedContinuity++;
                                continue;
                            }

                            removedRiverToLand++;

                            c.type = CellType.Land;
                            c.walkable = true;
                            c.buildable = true;
                            c.riverFord = false;
                            c.waterTraverse = WaterTraverseMode.NotWater;
                        }
                        continue;
                    }

                    c.type = CellType.River;
                    c.buildable = false;
                    c.riverFord = mustFord;
                    c.walkable = mustFord;
                    c.waterTraverse = mustFord ? WaterTraverseMode.FordShallow : WaterTraverseMode.SwimNavigable;
                    if (mustFord) shoreCount++;
                    else coreCount++;
                }
            }

            if (swConnApply != null)
                WaterGenPerfDiag.MsFusionConnectivityApply += swConnApply.Elapsed.TotalMilliseconds;

            if (config.debugLogs)
            {
                Debug.Log(
                    $"[RiverFusion] coreNoWalkable={coreCount} shoreWalkable={shoreCount} preservedFords={preservedFordCount} " +
                    $"thr(core={coreThreshold:F2}, shore={shoreThreshold:F2}) blur={blurPasses} shoreWidth={shoreWidth}");
            }

            if (config.riverFusionContinuityDebug)
            {
                Debug.Log($"[RiverFusionRemoved] count={removedRiverToLand}");
                Debug.Log($"[RiverFusionPreserved] connectivityKeeps={preservedContinuity}");
                Debug.Log(
                    $"[RiverContinuityProtected] centerlineCells={centerlinePacked.Count} convertedRiverToLand={removedRiverToLand} keptForTopology={preservedContinuity}");
            }

            if (config.debugDrawWaterMaskGizmos)
            {
                DebugLastRiverFusionMask01 = a;
                DebugLastRiverFusionCoreMask = core;
                DebugLastRiverFusionShoreMask = shore;
            }
            else
            {
                DebugLastRiverFusionMask01 = null;
                DebugLastRiverFusionCoreMask = null;
                DebugLastRiverFusionShoreMask = null;
            }

            if (config.debugDrawWaterFusionMask)
                DebugLastRiverFusionBlurField = a;
            else
                DebugLastRiverFusionBlurField = null;
        }

        /// <summary>
        /// Última pasada de hidrología antes del MS: elimina micro-islas acuáticas, puntas cardinales y pares diagonales tipo tablero sin soporte N/E/S/W.
        /// No modifica centerlines ni spawn connectivity; preserva vados y celdas críticas para la conectividad 4-del máscara acuático actual.
        /// </summary>
        private static void WaterTopologyCleanup(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null)
            {
                DebugLastWaterCleanupRemovedPacked = null;
                return;
            }

            int w = grid.Width;
            int h = grid.Height;
            int threshold = Mathf.Clamp(config.waterTopologyRemoveIslandThresholdCells, 2, 24);
            bool recordRemoved = config.debugDrawWaterTopologyCleanupGizmo;
            var removedPacked = recordRemoved ? new HashSet<long>() : null;
            DebugLastWaterCleanupRemovedPacked = removedPacked;

            var swTotal = System.Diagnostics.Stopwatch.StartNew();

            System.Diagnostics.Stopwatch swClPacked = WaterGenPerfDiag.Active ? System.Diagnostics.Stopwatch.StartNew() : null;
            var centerlinePacked = BuildCenterlineRiverCellsPacked(grid);
            if (swClPacked != null)
                WaterGenPerfDiag.MsTopoCenterlinePacked += swClPacked.Elapsed.TotalMilliseconds;
            var q = new Queue<Vector2Int>(Mathf.Min(w * h, 8192));
            var comp = new List<Vector2Int>(64);
            var visited = new bool[w, h];
            var aquaBase = new bool[w, h];
            var bfsVisit = new int[w, h];
            int bfsStamp = 0;

            int removedIslands = 0;
            int removedSpikes = 0;
            int removedDiag = 0;
            int preservedFord = 0;
            int preservedCenterline = 0;

            static bool IsAquaType(CellType t) => t == CellType.Water || t == CellType.River;

            void RebuildAquaBase()
            {
                if (WaterGenPerfDiag.Active)
                    WaterGenPerfDiag.RebuildAquaBaseCalls++;

                for (int zz = 0; zz < h; zz++)
                {
                    for (int xx = 0; xx < w; xx++)
                    {
                        aquaBase[xx, zz] = IsAquaType(grid.GetCell(xx, zz).type);
                    }
                }
            }

            void RefreshAquaCell(int cx, int cz)
            {
                aquaBase[cx, cz] = IsAquaType(grid.GetCell(cx, cz).type);
            }

            int CardinalAquaCount(int cx, int cz)
            {
                int n = 0;
                for (int i = 0; i < Cardinal4.Length; i++)
                {
                    int nx = cx + Cardinal4[i].x;
                    int nz = cz + Cardinal4[i].y;
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                        continue;
                    if (IsAquaType(grid.GetCell(nx, nz).type))
                        n++;
                }

                return n;
            }

            bool IsAquaAt(int cx, int cz) =>
                (uint)cx < (uint)w && (uint)cz < (uint)h && IsAquaType(grid.GetCell(cx, cz).type);

            void ConvertAquaToLand(int cx, int cz)
            {
                ref var cell = ref grid.GetCell(cx, cz);
                cell.type = CellType.Land;
                cell.walkable = true;
                cell.buildable = true;
                cell.riverFord = false;
                cell.waterTraverse = WaterTraverseMode.NotWater;
                long pk = PackCellLong(new Vector2Int(cx, cz));
                if (grid.LakeBodyCellsPacked != null)
                    grid.LakeBodyCellsPacked.Remove(pk);
                removedPacked?.Add(pk);
            }

            bool TryRemoveAquaCell(int cx, int cz)
            {
                if (WaterGenPerfDiag.Active)
                    WaterGenPerfDiag.TopologyTryRemoveCalls++;

                ref var cd = ref grid.GetCell(cx, cz);
                if (!IsAquaType(cd.type))
                    return false;
                if (cd.riverFord)
                {
                    preservedFord++;
                    return false;
                }

                if (centerlinePacked != null && centerlinePacked.Contains(PackCellLong(new Vector2Int(cx, cz))))
                {
                    preservedCenterline++;
                    return false;
                }

                if (!aquaBase[cx, cz])
                    RebuildAquaBase();
                if (IsMaskConnectivityCritical(
                        aquaBase,
                        w,
                        h,
                        cx,
                        cz,
                        centerlinePacked,
                        fordCell: false,
                        q,
                        bfsVisit,
                        ref bfsStamp,
                        WaterPerfCaller.TopologyTryRemove))
                    return false;

                ConvertAquaToLand(cx, cz);
                RefreshAquaCell(cx, cz);
                return true;
            }

            RebuildAquaBase();

            // A) Componentes 4-conexos menores que el umbral (sin ford ni centerline en el componente).
            System.Diagnostics.Stopwatch swIslandPass = WaterGenPerfDiag.Active ? System.Diagnostics.Stopwatch.StartNew() : null;
            Array.Clear(visited, 0, visited.Length);
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (visited[x, z] || !aquaBase[x, z])
                        continue;

                    comp.Clear();
                    q.Clear();
                    q.Enqueue(new Vector2Int(x, z));
                    visited[x, z] = true;
                    while (q.Count > 0)
                    {
                        var c = q.Dequeue();
                        if (WaterGenPerfDiag.Active)
                            WaterGenPerfDiag.AquaIslandFloodDequeues++;
                        comp.Add(c);
                        for (int i = 0; i < Cardinal4.Length; i++)
                        {
                            int nx = c.x + Cardinal4[i].x;
                            int nz = c.y + Cardinal4[i].y;
                            if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                                continue;
                            if (visited[nx, nz] || !aquaBase[nx, nz])
                                continue;
                            visited[nx, nz] = true;
                            q.Enqueue(new Vector2Int(nx, nz));
                        }
                    }

                    if (WaterGenPerfDiag.Active)
                    {
                        WaterGenPerfDiag.AquaIslandComponentsDiscovered++;
                        WaterGenPerfDiag.AquaIslandComponentCellsSum += comp.Count;
                        if (comp.Count > WaterGenPerfDiag.AquaIslandComponentMaxSize)
                            WaterGenPerfDiag.AquaIslandComponentMaxSize = comp.Count;
                    }

                    if (comp.Count >= threshold)
                        continue;

                    bool fordBlocked = false;
                    bool clBlocked = false;
                    int fordCells = 0;
                    int clCells = 0;
                    for (int i = 0; i < comp.Count; i++)
                    {
                        var p = comp[i];
                        ref var cd = ref grid.GetCell(p.x, p.y);
                        if (cd.riverFord)
                        {
                            fordBlocked = true;
                            fordCells++;
                        }

                        if (centerlinePacked != null && centerlinePacked.Contains(PackCellLong(p)))
                        {
                            clBlocked = true;
                            clCells++;
                        }
                    }

                    if (fordBlocked || clBlocked)
                    {
                        preservedFord += fordCells;
                        preservedCenterline += clCells;
                        continue;
                    }

                    for (int i = 0; i < comp.Count; i++)
                    {
                        ConvertAquaToLand(comp[i].x, comp[i].y);
                        removedIslands++;
                    }

                    RebuildAquaBase();
                }
            }

            if (swIslandPass != null)
                WaterGenPerfDiag.MsTopoIslandPass += swIslandPass.Elapsed.TotalMilliseconds;

            Array.Clear(visited, 0, visited.Length);
            RebuildAquaBase();

            // B) Spikes: a lo sumo 1 vecino cardinal acuático (Water o River), con protección de conectividad.
            System.Diagnostics.Stopwatch swSpikePass = WaterGenPerfDiag.Active ? System.Diagnostics.Stopwatch.StartNew() : null;
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!aquaBase[x, z])
                        continue;
                    ref var cell = ref grid.GetCell(x, z);
                    if (cell.riverFord)
                        continue;
                    if (centerlinePacked != null && centerlinePacked.Contains(PackCellLong(new Vector2Int(x, z))))
                        continue;

                    if (CardinalAquaCount(x, z) > 1)
                        continue;

                    if (TryRemoveAquaCell(x, z))
                        removedSpikes++;
                }
            }

            if (swSpikePass != null)
                WaterGenPerfDiag.MsTopoSpikePass += swSpikePass.Elapsed.TotalMilliseconds;

            RebuildAquaBase();

            // C) Diagonales tipo "W . / . W" sin soporte cardinal entre ellas.
            System.Diagnostics.Stopwatch swDiagPass = WaterGenPerfDiag.Active ? System.Diagnostics.Stopwatch.StartNew() : null;
            for (int z = 0; z < h - 1; z++)
            {
                for (int x = 0; x < w - 1; x++)
                {
                    if (!IsAquaAt(x, z) || !IsAquaAt(x + 1, z + 1))
                        continue;
                    if (IsAquaAt(x + 1, z) || IsAquaAt(x, z + 1))
                        continue;

                    if (TryRemoveAquaCell(x + 1, z + 1))
                    {
                        removedDiag++;
                        continue;
                    }

                    if (TryRemoveAquaCell(x, z))
                        removedDiag++;
                }
            }

            if (swDiagPass != null)
                WaterGenPerfDiag.MsTopoDiagonalPass += swDiagPass.Elapsed.TotalMilliseconds;

            swTotal.Stop();

            if (config.debugWaterTopologyCleanup)
            {
                Debug.Log(
                    $"[WaterCleanup] removedIslands={removedIslands} removedSpikes={removedSpikes} removedDiagonalArtifacts={removedDiag} " +
                    $"preservedFord={preservedFord} preservedCenterline={preservedCenterline} ms={swTotal.Elapsed.TotalMilliseconds:F1}");
            }
        }

        /// <summary>Auditoría no destructiva post-hidrología (solo logs).</summary>
        static void AuditRiverTopology(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null)
                return;
            if (!config.debugLogs && !config.debugHydrologyNetwork)
                return;

            int w = grid.Width;
            int h = grid.Height;
            int riverCount = grid.RiverCenterlinesCellSpace != null ? grid.RiverCenterlinesCellSpace.Count : 0;
            int centerlineCells = 0;
            var centerlinePacked = new HashSet<long>();
            if (grid.RiverCenterlinesCellSpace != null)
            {
                for (int ri = 0; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
                {
                    var line = grid.RiverCenterlinesCellSpace[ri];
                    if (line == null)
                        continue;
                    for (int pi = 0; pi < line.Count; pi++)
                    {
                        int cx = Mathf.FloorToInt(line[pi].x);
                        int cy = Mathf.FloorToInt(line[pi].y);
                        long k = ((long)cx << 32) | (uint)cy;
                        if (centerlinePacked.Add(k))
                            centerlineCells++;
                    }
                }
            }

            int riverCells = 0;
            int waterCells = 0;
            int fordsCount = 0;
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    ref var c = ref grid.GetCell(x, z);
                    if (c.type == CellType.River)
                        riverCells++;
                    else if (c.type == CellType.Water)
                        waterCells++;
                    if (c.riverFord)
                        fordsCount++;
                }
            }

            int lakeBodyPackedCount = grid.LakeBodyCellsPacked != null ? grid.LakeBodyCellsPacked.Count : 0;
            int fordsNearCenterline = 0;
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!grid.GetCell(x, z).riverFord)
                        continue;
                    bool near = false;
                    for (int dz = -2; dz <= 2 && !near; dz++)
                    {
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            long nk = ((long)(x + dx) << 32) | (uint)(z + dz);
                            if (centerlinePacked.Contains(nk))
                            {
                                near = true;
                                break;
                            }
                        }
                    }

                    if (near)
                        fordsNearCenterline++;
                }
            }

            float maxHeightRise = 0f;
            int heightRiseEvents = 0;
            if (grid.RiverCenterlinesCellSpace != null)
            {
                for (int ri = 0; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
                {
                    var line = grid.RiverCenterlinesCellSpace[ri];
                    if (line == null || line.Count < 2)
                        continue;
                    float prevH = grid.GetCell(
                        Mathf.Clamp(Mathf.FloorToInt(line[0].x), 0, w - 1),
                        Mathf.Clamp(Mathf.FloorToInt(line[0].y), 0, h - 1)).height01;
                    for (int pi = 1; pi < line.Count; pi++)
                    {
                        int cx = Mathf.Clamp(Mathf.FloorToInt(line[pi].x), 0, w - 1);
                        int cy = Mathf.Clamp(Mathf.FloorToInt(line[pi].y), 0, h - 1);
                        float h01 = grid.GetCell(cx, cy).height01;
                        float rise = h01 - prevH;
                        if (rise > 0.001f)
                        {
                            heightRiseEvents++;
                            maxHeightRise = Mathf.Max(maxHeightRise, rise);
                        }

                        prevH = h01;
                    }
                }
            }

            var visited = new bool[w, h];
            int disconnectedRiverLike = 0;
            int smallDetached = 0;
            int touchingCenterline = 0;
            int touchingLakeBody = 0;
            int invalidPoolCandidates = 0;
            int validLakeComponents = 0;

            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (visited[x, z])
                        continue;
                    ref var seed = ref grid.GetCell(x, z);
                    if (seed.type != CellType.Water && seed.type != CellType.River)
                        continue;

                    int size = 0;
                    bool touchesCl = false;
                    bool touchesLake = false;
                    bool allRiver = true;
                    var q = new Queue<Vector2Int>();
                    q.Enqueue(new Vector2Int(x, z));
                    visited[x, z] = true;
                    while (q.Count > 0)
                    {
                        var c = q.Dequeue();
                        size++;
                        ref var cell = ref grid.GetCell(c.x, c.y);
                        if (cell.type != CellType.River)
                            allRiver = false;
                        long ck = ((long)c.x << 32) | (uint)c.y;
                        if (centerlinePacked.Contains(ck))
                            touchesCl = true;
                        if (grid.LakeBodyCellsPacked != null && grid.LakeBodyCellsPacked.Contains(ck))
                            touchesLake = true;

                        foreach (var nb in grid.Neighbors4(c))
                        {
                            if (visited[nb.x, nb.y])
                                continue;
                            ref var nc = ref grid.GetCell(nb.x, nb.y);
                            if (nc.type != CellType.Water && nc.type != CellType.River)
                                continue;
                            visited[nb.x, nb.y] = true;
                            q.Enqueue(nb);
                        }
                    }

                    if (touchesCl)
                        touchingCenterline++;
                    if (touchesLake)
                    {
                        touchingLakeBody++;
                        validLakeComponents++;
                    }
                    else if (seed.type == CellType.Water && size < 12)
                    {
                        smallDetached++;
                        invalidPoolCandidates++;
                    }
                    else if (allRiver && !touchesCl && size >= 2)
                        disconnectedRiverLike++;
                }
            }

            int maskCells = 0;
            if (grid.RiverVisualSurfaceMask != null &&
                grid.RiverVisualSurfaceMask.GetLength(0) == w &&
                grid.RiverVisualSurfaceMask.GetLength(1) == h)
            {
                for (int z = 0; z < h; z++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (grid.RiverVisualSurfaceMask[x, z])
                            maskCells++;
                    }
                }
            }

            int suspectedTerrainMismatch = Mathf.Abs(maskCells - riverCells) > Mathf.Max(48, riverCells / 4) ? 1 : 0;
            int valid = disconnectedRiverLike == 0 && invalidPoolCandidates <= smallDetached ? 1 : 0;

            UnityEngine.Debug.Log(
                $"[RiverTopologyAudit] riverCount={riverCount} centerlineCount={riverCount} centerlineCells={centerlineCells} " +
                $"riverCells={riverCells} waterCells={waterCells} lakeCount={config.lakeCount} lakeBodyPackedCount={lakeBodyPackedCount} " +
                $"disconnectedRiverLikeWaterCells={disconnectedRiverLike} smallDetachedWaterComponents={smallDetached} " +
                $"validLakeComponents={validLakeComponents} invalidPoolCandidates={invalidPoolCandidates} " +
                $"componentsTouchingCenterline={touchingCenterline} componentsTouchingLakeBody={touchingLakeBody} " +
                $"fordsCount={fordsCount} fordsAlignedToCenterline={fordsNearCenterline} maxCenterlineHeightRise={maxHeightRise:F4} " +
                $"heightRiseEvents={heightRiseEvents} visualMaskCells={maskCells} suspectedTerrainMismatch={suspectedTerrainMismatch} valid={valid}");
        }

        /// <summary>Aplica tributario validado por <see cref="UwpLakeFirstHydrologyBuilder"/> (Fase4 lake-first).</summary>
        internal static int ApplyLakeFirstValidatedTributary(
            GridSystem grid,
            MapGenConfig config,
            IRng rng,
            int riverSlotIndex,
            List<Vector2Int> path,
            List<Vector2> centerline,
            HashSet<long> riverOccupiedCells,
            ref bool riverOccAabbValid,
            ref int riverOccMinX,
            ref int riverOccMaxX,
            ref int riverOccMinZ,
            ref int riverOccMaxZ)
        {
            if (grid == null || config == null || path == null || path.Count < 2 || centerline == null || centerline.Count < 2)
                return 0;

            if (grid.RiverCenterlinesCellSpace == null)
                grid.RiverCenterlinesCellSpace = new List<List<Vector2>>();

            int w = grid.Width;
            int h = grid.Height;
            int added = 0;
            int riverIdBeforeAdd = grid.RiverCenterlinesCellSpace != null ? grid.RiverCenterlinesCellSpace.Count : 0;
            int visualRiverRadiusCells = VisualRiverRasterRadiusCells(true, config);
            int riverNoiseCompiledBackup = config.riverWidthNoiseAmplitudeCells;
            config.riverWidthRadiusCells = visualRiverRadiusCells;

            grid.RiverCenterlinesCellSpace.Add(new List<Vector2>(centerline));
            float cs = grid.CellSizeWorld;
            Vector3 o = grid.Origin;
            var world = new List<Vector3>(centerline.Count);
            for (int pi = 0; pi < centerline.Count; pi++)
            {
                var p = centerline[pi];
                world.Add(new Vector3(o.x + p.x * cs, o.y, o.z + p.y * cs));
            }

            if (grid.RiverCenterlinesWorld == null)
                grid.RiverCenterlinesWorld = new List<List<Vector3>>();
            grid.RiverCenterlinesWorld.Add(world);

            var fordCells = SimpleRiverPathGenerator.BuildFordAlongPath(path, config, rng, w, h);
            s_fordPackedScratch.Clear();
            if (fordCells != null)
            {
                for (int fi = 0; fi < fordCells.Count; fi++)
                    s_fordPackedScratch.Add(PackCellLong(fordCells[fi]));
            }

            for (int pi = 0; pi < path.Count; pi++)
            {
                var c = path[pi];
                if (!grid.InBoundsCell(c.x, c.y))
                    continue;
                ref var cell = ref grid.GetCell(c);
                if (cell.type != CellType.Land)
                    continue;
                bool isFord = s_fordPackedScratch.Contains(PackCellLong(c));
                cell.type = CellType.River;
                cell.riverFord = isFord;
                cell.walkable = isFord;
                cell.buildable = false;
                cell.waterTraverse = isFord ? WaterTraverseMode.FordShallow : WaterTraverseMode.SwimNavigable;
                added++;
            }

            if (visualRiverRadiusCells <= 0)
                config.riverWidthNoiseAmplitudeCells = 0;
            added += ExpandRiverWidthAroundPath(grid, path, config, riverSlotIndex);
            config.riverWidthNoiseAmplitudeCells = riverNoiseCompiledBackup;

            if (fordCells != null && fordCells.Count > 0 && config.riverFordCorridorRadiusCells > 0)
                ApplyRiverFordCorridor(grid, fordCells, config.riverFordCorridorRadiusCells);

            CollectRiverCorridorPackedInto(path, config, w, h, s_riverCorridorPackedScratch);
            foreach (long k in s_riverCorridorPackedScratch)
            {
                RiverOccupiedAddPackedCell(
                    riverOccupiedCells,
                    ref riverOccAabbValid,
                    ref riverOccMinX,
                    ref riverOccMaxX,
                    ref riverOccMinZ,
                    ref riverOccMaxZ,
                    k);
            }

            if (grid.HydrologyNetwork != null)
            {
                int? parentRiver = 0;
                float joinDistSq = 0f;
                Vector2Int joinCell = path[path.Count - 1];
                parentRiver = TryResolveParentRiverAtJoin(grid, joinCell, riverIdBeforeAdd, out joinDistSq);
                grid.HydrologyNetwork.AddRiver(new HydrologyRiverRecord
                {
                    RiverId = riverIdBeforeAdd,
                    RiverClass = RiverClass.Tributary,
                    ParentRiverId = parentRiver,
                    BasinId = 0,
                    EstimatedFlow01 = 0.55f,
                    WidthClass = 0,
                    JoinVertexIndex = path.Count - 1,
                    StartCell = path[0],
                    EndCell = joinCell,
                    AcceptedLengthCells = path.Count,
                    HierarchyFromConfluenceTrim = true,
                    HierarchyReason = "lake_first_validated",
                });
            }

            UwpTributaryOriginUtility.SetOrigin(grid, riverIdBeforeAdd, UwpTributaryOriginKind.LakeSpill);

            config.riverWidthRadiusCells = visualRiverRadiusCells;
            return added;
        }

        /// <summary>Aplica tributario supplementario (InlandFeeder / HeadwaterFeeder) tras lake-first.</summary>
        internal static int ApplySupplementalValidatedTributary(
            GridSystem grid,
            MapGenConfig config,
            IRng rng,
            int riverSlotIndex,
            UwpTributaryOriginKind originKind,
            List<Vector2Int> path,
            List<Vector2> centerline,
            List<Vector2Int> fordCells,
            HashSet<long> riverOccupiedCells,
            ref bool riverOccAabbValid,
            ref int riverOccMinX,
            ref int riverOccMaxX,
            ref int riverOccMinZ,
            ref int riverOccMaxZ,
            int receiverRiverIndex = 0)
        {
            if (grid == null || config == null || path == null || path.Count < 2 || centerline == null || centerline.Count < 2)
                return 0;

            if (grid.RiverCenterlinesCellSpace == null)
                grid.RiverCenterlinesCellSpace = new List<List<Vector2>>();

            int w = grid.Width;
            int h = grid.Height;
            int added = 0;
            int riverIdBeforeAdd = grid.RiverCenterlinesCellSpace.Count;
            int visualRiverRadiusCells = VisualRiverRasterRadiusCells(true, config);
            int riverNoiseCompiledBackup = config.riverWidthNoiseAmplitudeCells;
            config.riverWidthRadiusCells = originKind == UwpTributaryOriginKind.HeadwaterFeeder
                ? Mathf.Clamp(Mathf.Max(1, visualRiverRadiusCells / 3), 1, 2)
                : visualRiverRadiusCells;

            grid.RiverCenterlinesCellSpace.Add(new List<Vector2>(centerline));
            float cs = grid.CellSizeWorld;
            Vector3 o = grid.Origin;
            var world = new List<Vector3>(centerline.Count);
            for (int pi = 0; pi < centerline.Count; pi++)
            {
                var p = centerline[pi];
                world.Add(new Vector3(o.x + p.x * cs, o.y, o.z + p.y * cs));
            }

            if (grid.RiverCenterlinesWorld == null)
                grid.RiverCenterlinesWorld = new List<List<Vector3>>();
            grid.RiverCenterlinesWorld.Add(world);

            s_fordPackedScratch.Clear();
            if (fordCells != null)
            {
                for (int fi = 0; fi < fordCells.Count; fi++)
                    s_fordPackedScratch.Add(PackCellLong(fordCells[fi]));
            }

            for (int pi = 0; pi < path.Count; pi++)
            {
                var c = path[pi];
                if (!grid.InBoundsCell(c.x, c.y))
                    continue;
                ref var cell = ref grid.GetCell(c);
                if (cell.type != CellType.Land)
                    continue;
                bool isFord = s_fordPackedScratch.Contains(PackCellLong(c));
                cell.type = CellType.River;
                cell.riverFord = isFord;
                cell.walkable = isFord;
                cell.buildable = false;
                cell.waterTraverse = isFord ? WaterTraverseMode.FordShallow : WaterTraverseMode.SwimNavigable;
                added++;
            }

            if (visualRiverRadiusCells <= 0)
                config.riverWidthNoiseAmplitudeCells = 0;
            added += ExpandRiverWidthAroundPath(grid, path, config, riverSlotIndex);
            config.riverWidthNoiseAmplitudeCells = riverNoiseCompiledBackup;

            if (fordCells != null && fordCells.Count > 0 && config.riverFordCorridorRadiusCells > 0)
                ApplyRiverFordCorridor(grid, fordCells, config.riverFordCorridorRadiusCells);

            CollectRiverCorridorPackedInto(path, config, w, h, s_riverCorridorPackedScratch);
            foreach (long k in s_riverCorridorPackedScratch)
            {
                RiverOccupiedAddPackedCell(
                    riverOccupiedCells,
                    ref riverOccAabbValid,
                    ref riverOccMinX,
                    ref riverOccMaxX,
                    ref riverOccMinZ,
                    ref riverOccMaxZ,
                    k);
            }

            UwpTributaryOriginUtility.SetOrigin(grid, riverIdBeforeAdd, originKind);
            RiverDendriticUtility.EnsureRiverMetadata(grid);
            int receiverId = receiverRiverIndex > 0
                ? receiverRiverIndex
                : (originKind == UwpTributaryOriginKind.HeadwaterFeeder ? 1 : 0);
            var role = RiverDendriticUtility.RoleForPlacement(
                riverIdBeforeAdd,
                receiverId,
                path.Count,
                grid.RiverCenterlinesCellSpace[0]?.Count ?? 0);
            while (grid.RiverDendriticRoles.Count <= riverIdBeforeAdd)
                grid.RiverDendriticRoles.Add(RiverDendriticRole.SecondaryTributary);
            while (grid.RiverReceiverIds.Count <= riverIdBeforeAdd)
                grid.RiverReceiverIds.Add(0);
            while (grid.RiverWidthRatioToMain.Count <= riverIdBeforeAdd)
                grid.RiverWidthRatioToMain.Add(1f);
            grid.RiverDendriticRoles[riverIdBeforeAdd] = role;
            grid.RiverReceiverIds[riverIdBeforeAdd] = receiverId;
            grid.RiverWidthRatioToMain[riverIdBeforeAdd] = RiverDendriticUtility.WidthRatioToMain(config, role);

            if (grid.HydrologyNetwork != null)
            {
                Vector2Int joinCell = RiverRouteGenerator.LastTributaryConfluencePlanValid
                    ? RiverRouteGenerator.LastTributaryConfluencePlan.ConfluenceCell
                    : path[path.Count - 1];
                int? parentRiver = TryResolveParentRiverAtJoin(grid, joinCell, riverIdBeforeAdd, out _);
                var riverClass = originKind == UwpTributaryOriginKind.HeadwaterFeeder
                    ? RiverClass.Creek
                    : RiverClass.Tributary;
                string reason = originKind == UwpTributaryOriginKind.HeadwaterFeeder
                    ? "headwater_feeder"
                    : "inland_feeder";
                grid.HydrologyNetwork.AddRiver(new HydrologyRiverRecord
                {
                    RiverId = riverIdBeforeAdd,
                    RiverClass = riverClass,
                    ParentRiverId = parentRiver,
                    BasinId = 0,
                    EstimatedFlow01 = originKind == UwpTributaryOriginKind.HeadwaterFeeder ? 0.28f : 0.42f,
                    WidthClass = 0,
                    JoinVertexIndex = path.Count - 1,
                    StartCell = path[0],
                    EndCell = joinCell,
                    AcceptedLengthCells = path.Count,
                    HierarchyFromConfluenceTrim = true,
                    HierarchyReason = reason,
                });
            }

            config.riverWidthRadiusCells = visualRiverRadiusCells;
            return added;
        }
    }
}
