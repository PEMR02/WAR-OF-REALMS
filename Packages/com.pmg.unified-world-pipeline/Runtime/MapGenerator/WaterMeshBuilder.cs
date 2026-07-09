using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 9: construye mesh de agua por chunks; quads por celda Water/River. Un GameObject raíz "Water".</summary>
    public static class WaterMeshBuilder
    {
        private static Transform _waterRoot;
        static int s_waterVisualLakeCellsSuppressed;
        static int s_waterVisualLakeFordComponentsPreserved;
        static int s_riverVisualStrayPoolCellsRemoved;
        static int s_waterVisualPreservedRealLakeComponents;
        static int s_waterVisualStrayNearRiverComponentsRemoved;
        static int s_waterVisualTinyLakeCellsRemoved;
        static int s_waterVisualFinalMaskCleanupCells;
        static int s_waterVisualFinalMaskCleanupComponents;
        static int s_waterVisualFinalMaskPreservedFord;
        static int s_waterVisualFinalMaskPreservedRealLakes;
        static int s_waterVisualFinalMaskPreservedRiverVisual;
        static int s_waterVisualFinalCleanupNearRiverStrays;
        static int s_waterVisualFinalCleanupComponentsScanned;
        static int s_waterVisualRootCleanupDestroyedChildren;
        static int s_waterVisualRootCleanupDestroyedOrphans;
        static int s_lakeMSFinalCells;
        static int s_waterMarchingSquaresCreated;
        static int s_waterChunksCreated;
        static int s_waterChunkFallbackAllowed;
        static string s_waterChunkFallbackDisabledReason = "";
        static int s_waterChunkCleanupDestroyedChunks;
        static int s_waterChunkCleanupDestroyedMarchingSquares;
        static int s_waterChunkCleanupDestroyedRiverSurface;
        static int s_waterChunkCleanupDestroyedWaterPlane;
        // Solo para la fase actual de BuildWaterMeshes: centros de spawn (en celdas) para validación de conectividad.
        static List<Vector2Int> s_spawnCellsForConnectivity;
        public static List<Vector3> DebugRibbonPathPointsWorld { get; } = new List<Vector3>();
        public static List<Vector3> DebugRibbonAcceptedSegmentsAWorld { get; } = new List<Vector3>();
        public static List<Vector3> DebugRibbonAcceptedSegmentsBWorld { get; } = new List<Vector3>();
        public static List<Vector3> DebugRibbonDiscardedSegmentsAWorld { get; } = new List<Vector3>();
        public static List<Vector3> DebugRibbonDiscardedSegmentsBWorld { get; } = new List<Vector3>();
        public static List<Vector3> DebugWaterCrossingPositionsWorld { get; } = new List<Vector3>();
        /// <summary>Centros mundo de huellas de debug de vado (solo OnDrawGizmos; no crear meshes).</summary>
        public static readonly List<Vector3> DebugRiverFordFootprintCentersWorld = new List<Vector3>();
        /// <summary>Tamaño completo del wire cube de huella (ancho, alto fino, fondo).</summary>
        public static readonly List<Vector3> DebugRiverFordFootprintSizesWorld = new List<Vector3>();
        /// <summary>Centros de candidatos a vado rechazados (gizmos).</summary>
        public static readonly List<Vector3> DebugRiverFordFailedCenterPositionsWorld = new List<Vector3>();
        public static int[,] DebugLastWaterInteriorDistanceGrid;
        public static int DebugLastWaterInteriorDistanceMax;

        static int _riverVisualHalfSamples;
        static double _riverVisualHalfSum;
        static float _riverVisualHalfMin;
        static float _riverVisualHalfMax;

        /// <summary>Valores seguros si el clone runtime no trae campos nuevos (0 / sin serializar).</summary>
        static void ApplyWaterMeshBuilderRuntimeDefaults(MapGenConfig config)
        {
            if (config == null)
                return;
            if (config.uwpOwnedVisualPolicy)
            {
                config.riverVisualMinSurfacePieceLengthCells = Mathf.Min(config.riverVisualMinSurfacePieceLengthCells, 4);
                config.riverVisualMinSurfacePieceAreaCells = Mathf.Min(config.riverVisualMinSurfacePieceAreaCells, 3);
                config.lakeMSRemoveNearRiverDistanceCells = 0;
            }
            if (config.lakeShoreVisualWidth <= 0.01f)
                config.lakeShoreVisualWidth = 7f;
            if (config.riverShoreVisualWidth <= 0.01f)
                config.riverShoreVisualWidth = 1.55f;
            if (config.riverVisualRibbonFullWidthCellsMain <= 0.01f)
                config.riverVisualRibbonFullWidthCellsMain = 2.75f;
            if (config.riverVisualRibbonFullWidthCellsTributary <= 0.01f)
                config.riverVisualRibbonFullWidthCellsTributary = 1.55f;
            if (config.riverSurfaceMeshUvScale <= 1e-5f)
                config.riverSurfaceMeshUvScale = 0.042f;
            if (config.riverVisualMinSurfacePieceLengthCells <= 0)
                config.riverVisualMinSurfacePieceLengthCells = 18;
            if (config.riverVisualMinSurfacePieceAreaCells <= 0)
                config.riverVisualMinSurfacePieceAreaCells = 12;
            if (config.riverVisualMainCorridorKeepDistanceCells <= 0)
                config.riverVisualMainCorridorKeepDistanceCells = 3;
            if (config.riverVisualFordKeepDistanceCells <= 0)
                config.riverVisualFordKeepDistanceCells = 5;
            if (config.riverVisualMaskKeepFordDistanceCells <= 0)
                config.riverVisualMaskKeepFordDistanceCells = config.riverVisualFordKeepDistanceCells;
            if (config.riverVisualMaskRemoveDetachedPatchMaxCells <= 0)
                config.riverVisualMaskRemoveDetachedPatchMaxCells = 60;
            if (config.lakeVisualRealLakeMinCells <= 0)
                config.lakeVisualRealLakeMinCells = 60;
            if (config.lakeMSMinComponentCells <= 0)
                config.lakeMSMinComponentCells = config.lakeVisualRealLakeMinCells;
            if (config.lakeMSRemoveNearRiverDistanceCells <= 0)
                config.lakeMSRemoveNearRiverDistanceCells = 5;
            if (!config.riverVisualFinalCleanupEnabled && config.riverVisualMaskCleanupEnabled)
                config.riverVisualFinalCleanupEnabled = true;
            if (config.riverVisualFinalCleanupMaxPatchCells <= 0)
                config.riverVisualFinalCleanupMaxPatchCells = 80;
            if (config.riverVisualFinalCleanupNearRiverCells <= 0)
                config.riverVisualFinalCleanupNearRiverCells = 5;
            if (config.riverVisualFinalCleanupKeepFordDistanceCells <= 0)
                config.riverVisualFinalCleanupKeepFordDistanceCells = config.riverVisualMaskKeepFordDistanceCells;
        }

        /// <summary>Diagnóstico compacto por malla/objeto de agua (solo debug).</summary>
        public static void LogWaterVisualObject(
            MapGenConfig config,
            string name,
            string kind,
            int riverIndex,
            int verts,
            int tris,
            Bounds bounds,
            int intersectsRiverVisualMask,
            int nearRiverVisualMaskCells,
            int nearFord,
            int isMainRiver,
            int isTributary,
            int culled,
            string note = "")
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            string noteSuffix = string.IsNullOrEmpty(note) ? "" : $" note={note}";
            Debug.Log(
                $"[WaterVisualObject] name={name} kind={kind} riverIndex={riverIndex} verts={verts} tris={tris} " +
                $"boundsMin=({bounds.min.x:F1},{bounds.min.y:F1},{bounds.min.z:F1}) boundsMax=({bounds.max.x:F1},{bounds.max.y:F1},{bounds.max.z:F1}) " +
                $"intersectsRiverVisualMask={intersectsRiverVisualMask} nearRiverVisualMaskCells={nearRiverVisualMaskCells} " +
                $"nearFord={nearFord} isMainRiver={isMainRiver} isTributary={isTributary} culled={culled}{noteSuffix}");
        }

        /// <summary>Celdas de máscara visual del río que intersectan o rodean el AABB mundo del objeto.</summary>
        public static void ComputeWaterVisualBoundsMaskStats(
            GridSystem grid,
            Bounds worldBounds,
            int nearCellRadius,
            out int intersectsRiverVisualMask,
            out int nearRiverVisualMaskCells)
        {
            intersectsRiverVisualMask = 0;
            nearRiverVisualMaskCells = 0;
            bool[,] rivMask = grid?.RiverVisualSurfaceMask;
            if (rivMask == null || grid.Width <= 0 || grid.Height <= 0)
                return;

            float cs = Mathf.Max(1e-4f, grid.CellSizeWorld);
            Vector3 origin = grid.Origin;
            int gw = grid.Width;
            int gh = grid.Height;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((worldBounds.min.x - origin.x) / cs), 0, gw - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt((worldBounds.max.x - origin.x) / cs), 0, gw - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((worldBounds.min.z - origin.z) / cs), 0, gh - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt((worldBounds.max.z - origin.z) / cs), 0, gh - 1);
            if (x1 < x0)
            {
                int t = x0;
                x0 = x1;
                x1 = t;
            }

            if (z1 < z0)
            {
                int t = z0;
                z0 = z1;
                z1 = t;
            }

            int expand = Mathf.Max(0, nearCellRadius);
            int ex0 = Mathf.Max(0, x0 - expand);
            int ex1 = Mathf.Min(gw - 1, x1 + expand);
            int ez0 = Mathf.Max(0, z0 - expand);
            int ez1 = Mathf.Min(gh - 1, z1 + expand);
            int insideMask = 0;
            int ringMask = 0;
            for (int z = ez0; z <= ez1; z++)
            {
                for (int x = ex0; x <= ex1; x++)
                {
                    if (!rivMask[x, z])
                        continue;
                    if (x >= x0 && x <= x1 && z >= z0 && z <= z1)
                        insideMask++;
                    else
                        ringMask++;
                }
            }

            intersectsRiverVisualMask = insideMask > 0 ? 1 : 0;
            nearRiverVisualMaskCells = insideMask + ringMask;
        }

        public static int ComputeNearFordFromWorldBounds(GridSystem grid, Bounds worldBounds, int fordDistCells)
        {
            if (grid == null || fordDistCells <= 0)
                return 0;
            float cs = Mathf.Max(1e-4f, grid.CellSizeWorld);
            Vector3 origin = grid.Origin;
            int gw = grid.Width;
            int gh = grid.Height;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((worldBounds.min.x - origin.x) / cs), 0, gw - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt((worldBounds.max.x - origin.x) / cs), 0, gw - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((worldBounds.min.z - origin.z) / cs), 0, gh - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt((worldBounds.max.z - origin.z) / cs), 0, gh - 1);
            int stepX = Mathf.Max(1, (x1 - x0) / 6);
            int stepZ = Mathf.Max(1, (z1 - z0) / 6);
            for (int z = z0; z <= z1; z += stepZ)
            {
                for (int x = x0; x <= x1; x += stepX)
                {
                    if (GridCellNearFordRiverChebyshev(grid, x, z, fordDistCells))
                        return 1;
                }
            }

            return 0;
        }

        static bool IsWaterVisualOrphanName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
                return false;
            if (objectName == "Water")
                return true;
            if (objectName == "WaterPlane" || objectName == "Water_MarchingSquares")
                return true;
            return objectName.StartsWith("Water_") ||
                   objectName.StartsWith("WaterChunk_") ||
                   objectName.StartsWith("RiverSurface_") ||
                   objectName.StartsWith("Water_RiverSurface");
        }

        static void TallyWaterVisualOrphanDestroy(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
                return;
            if (objectName.StartsWith("WaterChunk_"))
                s_waterChunkCleanupDestroyedChunks++;
            else if (objectName == "Water_MarchingSquares")
                s_waterChunkCleanupDestroyedMarchingSquares++;
            else if (objectName.StartsWith("Water_RiverSurface") || objectName.StartsWith("RiverSurface_"))
                s_waterChunkCleanupDestroyedRiverSurface++;
            else if (objectName == "WaterPlane")
                s_waterChunkCleanupDestroyedWaterPlane++;
        }

        static bool RiversRenderedBySurfaceMesh(MapGenConfig config)
        {
            return config != null &&
                config.riverVisualUseContinuousMesh &&
                config.riverVisualUseRiverSurfaceMeshStrip &&
                !config.riverVisualRenderRiverAsMarchingSquaresCells;
        }

        static void LogWaterRenderMode(
            bool renderRibbonMesh,
            bool riversRenderedBySurface,
            bool riverRibbonsOk,
            bool marchingSquaresOk,
            bool msIncludesRiverCells)
        {
            Debug.Log(
                $"[WaterRenderMode] riversBySurfaceMesh={(renderRibbonMesh && riversRenderedBySurface && riverRibbonsOk ? 1 : 0)} " +
                $"lakesByMarchingSquares={(marchingSquaresOk ? 1 : 0)} msIncludesRiver={(msIncludesRiverCells ? 1 : 0)} " +
                $"waterMarchingSquaresCreated={s_waterMarchingSquaresCreated} lakeMSFinalCells={s_lakeMSFinalCells} " +
                $"waterChunksCreated={s_waterChunksCreated} waterChunkFallbackAllowed={s_waterChunkFallbackAllowed} " +
                $"waterChunkFallbackDisabledReason={s_waterChunkFallbackDisabledReason} " +
                $"riverMeshCount={RiverSurfaceMeshBuilder.LastMeshCount} riverMeshVerts={RiverSurfaceMeshBuilder.LastVertexSum} " +
                $"riverMeshTris={RiverSurfaceMeshBuilder.LastTriSum}");
        }

        static void LogWaterChunkFallbackAudit(
            MapGenConfig config,
            GridSystem grid,
            bool riversRenderedBySurface,
            bool marchingSquaresOk,
            bool riverSurfaceOk,
            bool willEnterChunkFallback,
            int waterCellCount)
        {
            if (config == null)
                return;
            int lakeBody = grid?.LakeBodyCellsPacked != null ? grid.LakeBodyCellsPacked.Count : 0;
            string surfaceMode = riversRenderedBySurface ? "RiverSurfaceMesh" : "legacy_or_ms";
            Debug.Log(
                $"[WaterChunkFallbackAudit] riverSurfaceMode={surfaceMode} lakeCount={config.lakeCount} " +
                $"lakeBodyPackedCount={lakeBody} marchingSquaresOk={(marchingSquaresOk ? 1 : 0)} " +
                $"riverSurfaceOk={(riverSurfaceOk ? 1 : 0)} willEnterChunkFallback={(willEnterChunkFallback ? 1 : 0)} " +
                $"waterCellCount={waterCellCount}");
        }

        static void CollectWaterVisualOrphans(Transform node, List<GameObject> acc, Transform excludeRoot)
        {
            if (node == null)
                return;
            if (IsWaterVisualOrphanName(node.name))
            {
                if (excludeRoot != null && node == excludeRoot)
                    return;
                if (excludeRoot == null || !node.IsChildOf(excludeRoot))
                {
                    acc.Add(node.gameObject);
                    return;
                }
            }

            for (int i = 0; i < node.childCount; i++)
                CollectWaterVisualOrphans(node.GetChild(i), acc, excludeRoot);
        }

        static void DestroyWaterVisualGameObject(GameObject go)
        {
            if (go == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(go);
            else
                Object.DestroyImmediate(go);
        }

        /// <summary>Destruye raíz Water previa y meshes huérfanos Water_* / RiverSurface_* en escena.</summary>
        static void CleanupWaterVisualRootsAndOrphans(MapGenConfig config)
        {
            s_waterVisualRootCleanupDestroyedChildren = 0;
            s_waterVisualRootCleanupDestroyedOrphans = 0;
            s_waterChunkCleanupDestroyedChunks = 0;
            s_waterChunkCleanupDestroyedMarchingSquares = 0;
            s_waterChunkCleanupDestroyedRiverSurface = 0;
            s_waterChunkCleanupDestroyedWaterPlane = 0;
            string rootName = "none";
            Transform oldRoot = _waterRoot;
            if (oldRoot != null)
            {
                rootName = oldRoot.name;
                s_waterVisualRootCleanupDestroyedChildren = oldRoot.childCount;
                for (int i = 0; i < oldRoot.childCount; i++)
                    TallyWaterVisualOrphanDestroy(oldRoot.GetChild(i).name);
            }

            var orphans = new List<GameObject>(16);
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid())
            {
                var roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                    CollectWaterVisualOrphans(roots[i].transform, orphans, oldRoot);
            }

            if (oldRoot != null)
            {
                DestroyWaterVisualGameObject(oldRoot.gameObject);
                _waterRoot = null;
            }

            for (int i = 0; i < orphans.Count; i++)
            {
                GameObject go = orphans[i];
                if (go == null)
                    continue;
                TallyWaterVisualOrphanDestroy(go.name);
                DestroyWaterVisualGameObject(go);
                s_waterVisualRootCleanupDestroyedOrphans++;
            }

            bool shouldLog = config != null &&
                (config.debugLogs || config.debugHydrologyNetwork ||
                 s_waterVisualRootCleanupDestroyedChildren > 0 ||
                 s_waterVisualRootCleanupDestroyedOrphans > 0 ||
                 s_waterChunkCleanupDestroyedChunks > 0 ||
                 s_waterChunkCleanupDestroyedMarchingSquares > 0 ||
                 s_waterChunkCleanupDestroyedRiverSurface > 0 ||
                 s_waterChunkCleanupDestroyedWaterPlane > 0);
            if (shouldLog)
            {
                Debug.Log(
                    $"[WaterVisualRootCleanup] rootName={rootName} destroyedChildren={s_waterVisualRootCleanupDestroyedChildren} " +
                    $"destroyedOrphans={s_waterVisualRootCleanupDestroyedOrphans}");
                Debug.Log(
                    $"[WaterChunkCleanup] destroyedChunks={s_waterChunkCleanupDestroyedChunks} " +
                    $"destroyedMarchingSquares={s_waterChunkCleanupDestroyedMarchingSquares} " +
                    $"destroyedRiverSurface={s_waterChunkCleanupDestroyedRiverSurface} " +
                    $"destroyedWaterPlane={s_waterChunkCleanupDestroyedWaterPlane}");
            }
        }

        /// <summary>Parámetros: config.waterChunkSize, config.waterHeight01, config.waterSurfaceOffset. material puede ser null (fallback).</summary>
        public static GameObject BuildWaterMeshes(
            GridSystem grid,
            MapGenConfig config,
            Material waterMaterial,
            List<Vector2Int> spawnCells = null,
            List<CityNode> strategicCities = null,
            List<Road> strategicRoads = null)
        {
            if (grid == null || config == null) return null;
            WaterVisualPipelinePolicy.ApplyToRuntimeConfig(config);
            ApplyWaterMeshBuilderRuntimeDefaults(config);
            RiverSurfaceMeshBuilder.ResetStats();
            s_waterVisualLakeCellsSuppressed = 0;
            s_waterVisualLakeFordComponentsPreserved = 0;
            s_riverVisualStrayPoolCellsRemoved = 0;
            s_waterVisualPreservedRealLakeComponents = 0;
            s_waterVisualStrayNearRiverComponentsRemoved = 0;
            s_waterVisualTinyLakeCellsRemoved = 0;
            s_waterVisualFinalMaskCleanupCells = 0;
            s_waterVisualFinalMaskCleanupComponents = 0;
            s_waterVisualFinalMaskPreservedFord = 0;
            s_waterVisualFinalMaskPreservedRealLakes = 0;
            s_waterVisualFinalMaskPreservedRiverVisual = 0;
            s_waterVisualFinalCleanupNearRiverStrays = 0;
            s_waterVisualFinalCleanupComponentsScanned = 0;
            s_spawnCellsForConnectivity = spawnCells;
            DebugWaterCrossingPositionsWorld.Clear();
            DebugRiverFordFootprintCentersWorld.Clear();
            DebugRiverFordFootprintSizesWorld.Clear();
            DebugRiverFordFailedCenterPositionsWorld.Clear();
            DebugLastWaterInteriorDistanceGrid = null;

            CleanupWaterVisualRootsAndOrphans(config);

            int chunkSize = Mathf.Max(1, config.waterChunkSize);
            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            // Offset Y pequeño para evitar z-fighting sin dejar el río visiblemente "suspendido".
            float y = grid.Origin.y + config.waterHeight01 * terrainY + Mathf.Max(config.waterSurfaceOffset, 0.02f);
            int w = grid.Width;
            int h = grid.Height;
            float cellSize = grid.CellSizeWorld;
            if (config.riverVisualUseRiverSurfaceMeshStrip)
            {
                if (!grid.RiverVisualSurfaceCacheFrozen)
                    RiverSurfaceMeshBuilder.EnsureRiverVisualSurfaceCache(grid, config);
            }
            if (grid.WaterShoreDistanceCells == null || grid.WaterDepth01 == null || grid.WaterFlowXZ == null)
                WaterSurfaceFieldBuilder.Build(grid, config);
            int waterCellCount = 0;
            int lakeGridCells = 0;
            int riverGridCells = 0;
            for (int gx = 0; gx < w; gx++)
                for (int gz = 0; gz < h; gz++)
                {
                    var t = grid.GetCell(gx, gz).type;
                    if (t == CellType.Water || t == CellType.River) waterCellCount++;
                    if (t == CellType.Water) lakeGridCells++;
                    else if (t == CellType.River) riverGridCells++;
                }

            // Siempre capa 0 (Default) para que la cámara muestre el agua sin tocar Culling Mask.
            const int waterLayer = 0;
            bool unifiedSingleSurface = WaterVisualPipelinePolicy.IsUnifiedSingleSurface(config);
            Material sharedWaterMat = GetOrCreateWaterMaterial(waterMaterial, config);
            Material lakeMat = GetOrCreateLakeWaterMaterial(
                waterMaterial,
                config,
                sharedWaterMat,
                out bool lakeFallbackUsed,
                out string lakeFallbackReason);
            bool unifiedWaterMaterial = MaterialsUseSameVisualSource(sharedWaterMat, lakeMat);

            _waterRoot = new GameObject("Water").transform;
            _waterRoot.SetParent(null);
            _waterRoot.position = Vector3.zero;
            _waterRoot.rotation = Quaternion.identity;
            _waterRoot.localScale = Vector3.one;
            _waterRoot.gameObject.layer = waterLayer;
            _waterRoot.gameObject.SetActive(true);
            bool renderRibbonMesh =
                (config.riverVisualUseContinuousMesh && !config.riverVisualRenderRiverAsMarchingSquaresCells) ||
                (config.debugRenderRiverRibbonMesh && config.riverVisualUseContinuousMesh);
            bool riversRenderedBySurface = RiversRenderedBySurfaceMesh(config);
            bool waterChunkFallbackAllowed = !riversRenderedBySurface;
            s_waterChunksCreated = 0;
            s_waterChunkFallbackAllowed = waterChunkFallbackAllowed ? 1 : 0;
            s_waterChunkFallbackDisabledReason = riversRenderedBySurface ? "river_surface_mode" : "";
            bool msIncludesRiverCells = !renderRibbonMesh;
            int riverCenterlinesCount = grid.RiverCenterlinesCellSpace != null ? grid.RiverCenterlinesCellSpace.Count : 0;
            Debug.Log(
                $"[RiverRibbonRuntime] renderRiverAsMS={(config.riverVisualRenderRiverAsMarchingSquaresCells ? 1 : 0)} " +
                $"useContinuousMesh={(config.riverVisualUseContinuousMesh ? 1 : 0)} debugRibbon={(config.debugRenderRiverRibbonMesh ? 1 : 0)} " +
                $"waterVisualPipeline={WaterVisualPipelinePolicy.RuntimeName(config)} " +
                $"mainFullWidth={config.riverVisualRibbonFullWidthCellsMain:F2} tributaryFullWidth={config.riverVisualRibbonFullWidthCellsTributary:F2} " +
                $"riverShoreVisualWidth={config.riverShoreVisualWidth:F2} lakeShoreVisualWidth={config.lakeShoreVisualWidth:F2} " +
                $"riverCenterlinesCount={riverCenterlinesCount}");
            Debug.Log(
                $"[WaterMeshMask] riverRibbonActive={(renderRibbonMesh ? 1 : 0)} msIncludesRiver={(msIncludesRiverCells ? 1 : 0)} " +
                $"waterCells={lakeGridCells + riverGridCells} riverCells={riverGridCells} lakeCells={lakeGridCells}");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (config.debugLogs)
            {
                string mode = unifiedSingleSurface
                    ? "UnifiedSingleSurfaceMS"
                    : (!renderRibbonMesh
                        ? "MarchingSquaresAllWater"
                        : (config.riverVisualUseRiverSurfaceMeshStrip ? "LakesMS_RiverSurfaceStrip" : "LakesMS_RiverRibbonLegacy"));
                Debug.Log($"[WaterMesh] mode={mode}");
            }
#endif
            s_lakeMSFinalCells = 0;
            s_waterMarchingSquaresCreated = 0;
            // Marching squares: solo lagos reales cuando el río va por RiverSurfaceMesh.
            bool marchingSquaresOk = false;
            float unifiedDepthDrivenLiftWorld = unifiedSingleSurface
                ? ComputeUnifiedWaterDepthDrivenLiftWorld(config, terrainY)
                : 0f;
            bool useSurfaceStrip = config.riverVisualUseRiverSurfaceMeshStrip;
            float riverY;
            float lakeY;
            if (WaterVisualPipelinePolicy.UsesUwpUnifiedWaterSurfaceLevel(config))
            {
                riverY = WaterVisualPipelinePolicy.ResolveUwpUnifiedChannelSurfaceWorldY(config, y);
                lakeY = riverY;
                if (config.debugLogs || config.debugHydrologyNetwork)
                {
                    Debug.Log(
                        $"[UwpUnifiedWaterHeight] baseY={y:F3} unifiedY={riverY:F3} " +
                        $"antiZ={Mathf.Max(0.02f, config.riverRibbonAntiZFightYOffsetWorld):F3} " +
                        $"extra={Mathf.Max(0f, config.riverSurfaceMeshExtraYOffsetWorld):F3}");
                }
            }
            else
            {
                riverY = y + Mathf.Max(0f, config.riverRibbonVerticalLiftWorld) +
                    Mathf.Max(0f, config.riverRibbonAntiZFightYOffsetWorld);
                if (useSurfaceStrip)
                    riverY += Mathf.Max(0f, config.riverSurfaceMeshExtraYOffsetWorld);

                lakeY = unifiedSingleSurface
                    ? y + config.unifiedWaterSurfaceExtraYOffsetWorld + unifiedDepthDrivenLiftWorld
                    : y + Mathf.Max(0f, config.lakeWaterSurfaceExtraOffsetWorld);
                if (!unifiedSingleSurface && useSurfaceStrip)
                    lakeY = ResolveLakeMarchingSquaresDisplayY(grid, config, y, riverY);
            }

            if (unifiedSingleSurface)
                riverY = lakeY;
            if (unifiedWaterMaterial)
                riverY = lakeY + 0.015f;
            if (unifiedSingleSurface && config.debugLogs)
            {
                Debug.Log(
                    $"[UnifiedWaterHeight] baseY={y:F3} extraY={config.unifiedWaterSurfaceExtraYOffsetWorld:F3} " +
                    $"depthLift={unifiedDepthDrivenLiftWorld:F3} lakeY={lakeY:F3} terrainY={terrainY:F2}");
            }

            WaterSubsurfaceBedBuilder.Build(_waterRoot, grid, config, lakeY, riverY, cellSize, waterLayer);
            if (config.waterRoundedEdges)
                marchingSquaresOk = BuildRoundedWaterMarchingSquares(
                    _waterRoot, grid, config, lakeMat, lakeY, cellSize, waterLayer, renderRibbonMesh,
                    strategicCities, strategicRoads);
            s_waterMarchingSquaresCreated = marchingSquaresOk ? 1 : 0;
            if (unifiedSingleSurface && marchingSquaresOk)
                BuildUnifiedRiverCurrentOverlays(_waterRoot, grid, config, lakeY, cellSize, waterLayer);

            bool riverRibbonsOk = false;
            if (renderRibbonMesh)
            {
                if (useSurfaceStrip)
                {
                    riverRibbonsOk = RiverSurfaceMeshBuilder.BuildRiverSurfaces(
                        _waterRoot,
                        grid,
                        config,
                        sharedWaterMat,
                        riverY,
                        cellSize,
                        waterLayer);
                }
                else
                {
                    riverRibbonsOk = BuildRiverRibbonMeshes(_waterRoot, grid, config, sharedWaterMat, riverY, cellSize, waterLayer);
                }

            }

            bool willEnterChunkFallback = waterChunkFallbackAllowed && !marchingSquaresOk;
            LogWaterChunkFallbackAudit(
                config,
                grid,
                riversRenderedBySurface,
                marchingSquaresOk,
                riverRibbonsOk,
                willEnterChunkFallback,
                waterCellCount);

            if (riversRenderedBySurface)
            {
                if (config.debugLogs || config.debugHydrologyNetwork)
                {
                    Debug.Log(
                        "[WaterLegacyCleanup] removedMethods=0 deprecatedFields=waterChunk_fallback " +
                        "silencedLogs=0 keptForCompatibility=WaterChunk_MS_legacy_path risk=low");
                    Debug.Log("[WaterChunkFallbackDisabled] reason=river_surface_mode chunksCreated=0");
                    if (!marchingSquaresOk && config.lakeCount > 0)
                    {
                        int lakeBody = grid.LakeBodyCellsPacked != null ? grid.LakeBodyCellsPacked.Count : 0;
                        if (lakeBody > 0)
                        {
                            Debug.LogWarning(
                                "[WaterMesh] Lake MS no se generó en modo RiverSurfaceMesh; fallback WaterChunk desactivado.");
                        }
                    }
                }

                LogLakeMaterial(config, lakeMat, lakeFallbackUsed, lakeFallbackReason, marchingSquaresOk);
                LogWaterPipelineGuard(riversRenderedBySurface, msIncludesRiverCells, marchingSquaresOk, config);
                DestroyStrayWaterChunksUnderRoot(_waterRoot, config);
                LogWaterRenderMode(renderRibbonMesh, riversRenderedBySurface, riverRibbonsOk, marchingSquaresOk, msIncludesRiverCells);
                LogMcpWaterSystemPostPatchAudit(
                    grid,
                    config,
                    lakeMat,
                    lakeFallbackUsed,
                    msIncludesRiverCells,
                    marchingSquaresOk);
                return _waterRoot != null ? _waterRoot.gameObject : null;
            }

            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                var reason = new StringBuilder(48);
                if (s_riverVisualStrayPoolCellsRemoved > 0)
                    reason.Append("near_river_stray_pool;");
                if (s_waterVisualTinyLakeCellsRemoved > 0)
                    reason.Append("tiny_lake_ms;");
                if (reason.Length == 0)
                    reason.Append("none");
                int removedRiverPieces =
                    RiverSurfaceMeshBuilder.DetachedRiverSurfaceSkips +
                    RiverSurfaceMeshBuilder.ShortRiverSurfaceSkips +
                    RiverSurfaceMeshBuilder.RiverSurfaceFragmentCullCount;
                Debug.Log(
                    $"[WaterVisualPatchCleanup] removedRiverSurfacePieces={removedRiverPieces} " +
                    $"riverSurfaceFragmentCull={RiverSurfaceMeshBuilder.RiverSurfaceFragmentCullCount} " +
                    $"rootCleanupOrphans={s_waterVisualRootCleanupDestroyedOrphans} " +
                    $"removedLakeMSPatches={s_waterVisualLakeCellsSuppressed} " +
                    $"strayNearRiverComponents={s_waterVisualStrayNearRiverComponentsRemoved} " +
                    $"preservedFordPatches={s_waterVisualLakeFordComponentsPreserved} " +
                    $"preservedRealLakes={s_waterVisualPreservedRealLakeComponents} " +
                    $"visualMaskCleanupCells={s_waterVisualFinalMaskCleanupCells} visualMaskCleanupComponents={s_waterVisualFinalMaskCleanupComponents} " +
                    $"nearRiverStrays={s_waterVisualFinalCleanupNearRiverStrays} componentsScanned={s_waterVisualFinalCleanupComponentsScanned} " +
                    $"reason={reason}");
            }

            if (marchingSquaresOk)
            {
                LogWaterRenderMode(renderRibbonMesh, riversRenderedBySurface, riverRibbonsOk, marchingSquaresOk, msIncludesRiverCells);
                return _waterRoot.gameObject;
            }

            // Post-proceso de máscara (rápido): suaviza Water/River -> bool mask para reducir dientes sin MS.
            bool[,] smoothMask = null;
            if (config.waterMaskPostProcess && config.waterMaskSmoothIterations > 0)
                smoothMask = BuildSmoothedWaterMask(grid, config, !renderRibbonMesh);

            int chunkCount = 0;
            int totalVerts = 0;
            int totalTris = 0;

            for (int cz = 0; cz < h; cz += chunkSize)
            {
                for (int cx = 0; cx < w; cx += chunkSize)
                {
                    int cxe = Mathf.Min(cx + chunkSize, w);
                    int cze = Mathf.Min(cz + chunkSize, h);
                    int chunkW = cxe - cx;
                    int chunkH = cze - cz;

                    // Vertex lattice determinista: (chunkW+1) x (chunkH+1).
                    // Importante: NO hay subdivisiones ni "merge por floats".
                    // Los chunks adyacentes comparten posiciones en borde, pero NO comparten caras => sin overlap.
                    int vertsW = chunkW + 1;
                    int vertsH = chunkH + 1;
                    int vertCount = vertsW * vertsH;
                    var verts = new List<Vector3>(vertCount);
                    var uvs = new List<Vector2>(vertCount);
                    var tris = new List<int>();

                    for (int lz = 0; lz < vertsH; lz++)
                    {
                        for (int lx = 0; lx < vertsW; lx++)
                        {
                            float wx = grid.Origin.x + (cx + lx) * cellSize;
                            float wz = grid.Origin.z + (cz + lz) * cellSize;
                            verts.Add(new Vector3(wx, y, wz));

                            // UVs simples y deterministas (0..1 global en el mapa).
                            float u = w > 1 ? (float)(cx + lx) / (w - 1) : 0f;
                            float v = h > 1 ? (float)(cz + lz) / (h - 1) : 0f;
                            uvs.Add(new Vector2(u, v));
                        }
                    }

                    int Index(int lx, int lz) => lz * vertsW + lx;

                    for (int gz = cz; gz < cze; gz++)
                    {
                        for (int gx = cx; gx < cxe; gx++)
                        {
                            ref var cell = ref grid.GetCell(gx, gz);
                            bool countRiver = !renderRibbonMesh;
                            bool isWater = smoothMask != null
                                ? smoothMask[gx, gz]
                                : (cell.type == CellType.Water || (countRiver && cell.type == CellType.River));
                            if (!isWater) continue;

                            int lx = gx - cx;
                            int lz = gz - cz;

                            int i0 = Index(lx, lz);
                            int i1 = Index(lx + 1, lz);
                            int i2 = Index(lx + 1, lz + 1);
                            int i3 = Index(lx, lz + 1);

                            // Winding para que la normal apunte hacia +Y (cara superior).
                            tris.Add(i0); tris.Add(i2); tris.Add(i1);
                            tris.Add(i0); tris.Add(i3); tris.Add(i2);
                        }
                    }

                    if (tris.Count == 0) continue;

                    var mesh = new Mesh();
                    mesh.name = $"WaterChunk_{cx}_{cz}";
                    // Vertices ya están en world-space y el root está en identidad => local == world.
                    mesh.SetVertices(verts);
                    mesh.SetTriangles(tris, 0);
                    mesh.SetUVs(0, uvs);
                    // Vertex colors uniformes (blanco) para que Unlit no multiplique por valores basura.
                    var colors = new List<Color>(verts.Count);
                    for (int i = 0; i < verts.Count; i++) colors.Add(Color.white);
                    mesh.SetColors(colors);
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    WaterStylizedIntegration.PrepareMesh(mesh, sharedWaterMat);

                    var go = new GameObject($"WaterChunk_{cx}_{cz}");
                    go.transform.SetParent(_waterRoot, false);
                    go.layer = waterLayer;
                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    if (sharedWaterMat != null)
                    {
                        mr.sharedMaterial = sharedWaterMat;
                        mr.enabled = true;
                    }
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.renderingLayerMask = 1u;
                    WaterStylizedIntegration.AttachWaterObject(go, mf, mr, sharedWaterMat);
                    chunkCount++;
                    totalVerts += verts.Count;
                    totalTris += tris.Count / 3;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (config.debugLogs)
                        Debug.Log($"Fase9 WaterChunk [{cx},{cz}] size={chunkW}x{chunkH}: verts={verts.Count}, tris={(tris.Count / 3)} (1 quad/celda agua, sin subdiv ni merge).");
#endif
                }
            }

            if (chunkCount == 0)
            {
                if (riverRibbonsOk)
                    return _waterRoot.gameObject;
                Debug.LogWarning($"Fase9 WaterMesh: 0 chunks (había {waterCellCount} celdas agua). Revisa Fase3 o sube riverCount/lakeCount.");
            }
            else
            {
                string matInfo = sharedWaterMat != null ? sharedWaterMat.name : "null";
                int expectedTris = waterCellCount * 2;
                if (config.debugLogs)
                    Debug.Log($"Fase9 WaterMesh: {waterCellCount} celdas agua, {chunkCount} chunks, Y={y:F2}, material={matInfo}, totalVerts={totalVerts}, totalTris={totalTris}. Esperado ~{expectedTris} tris (2 por celda agua).");
            }

            s_waterChunksCreated = chunkCount;
            if (chunkCount > 0 && !marchingSquaresOk)
            {
                Debug.LogWarning(
                    "[WaterMesh] Agua en modo CUADRÍCULA (fallback): marching squares no generó la malla principal. " +
                    "Revisa en consola (más arriba) avisos que empiecen por «Fase9 WaterMesh (MS):». " +
                    "Causas típicas: bbox de agua enorme (supera waterMsMaxCornerSamples), iso sin cruces (0 tris), o waterRoundedEdges desactivado en plantilla.");
            }

            LogWaterRenderMode(renderRibbonMesh, riversRenderedBySurface, riverRibbonsOk, marchingSquaresOk, msIncludesRiverCells);
            return _waterRoot != null ? _waterRoot.gameObject : null;
        }

        private static bool[,] BuildSmoothedWaterMask(GridSystem grid, MapGenConfig config, bool includeRiverCells)
        {
            int w = grid.Width;
            int h = grid.Height;
            var a = new bool[w, h];
            var b = new bool[w, h];
            bool countRiver = includeRiverCells;

            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    var t = grid.GetCell(x, z).type;
                    a[x, z] = countRiver ? (t == CellType.Water || t == CellType.River) : (t == CellType.Water);
                }

            int iters = Mathf.Clamp(config.waterMaskSmoothIterations, 0, 8);
            int thr = Mathf.Clamp(config.waterMaskSmoothThreshold, 0, 9);
            if (iters <= 0) return a;

            for (int it = 0; it < iters; it++)
            {
                for (int z = 0; z < h; z++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int count = a[x, z] ? 1 : 0;
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int zz = z + dz;
                            if ((uint)zz >= (uint)h) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx;
                                if ((uint)xx >= (uint)w) continue;
                                if (dx == 0 && dz == 0) continue;
                                if (a[xx, zz]) count++;
                            }
                        }
                        b[x, z] = count >= thr;
                    }
                }
                // swap
                var tmp = a; a = b; b = tmp;
            }

            // El filtro de mayoría borra trazos de 1 celda; reforzar río solo si el MS/chunk lo usa como agua.
            if (countRiver)
            {
                for (int z = 0; z < h; z++)
                    for (int x = 0; x < w; x++)
                        if (grid.GetCell(x, z).type == CellType.River)
                            a[x, z] = true;
            }

            return a;
        }

        /// <summary>Río visual: strip desde centerline en espacio celda convertida a mundo en este paso (mismo Origin/cellSize que el terreno).</summary>
        private static bool BuildRiverRibbonMeshes(Transform parent, GridSystem grid, MapGenConfig config, Material mat, float waterY, float cellSize, int waterLayer)
        {
            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return false;
            if (config == null)
                return false;

            DebugRibbonPathPointsWorld.Clear();
            DebugRibbonAcceptedSegmentsAWorld.Clear();
            DebugRibbonAcceptedSegmentsBWorld.Clear();
            DebugRibbonDiscardedSegmentsAWorld.Clear();
            DebugRibbonDiscardedSegmentsBWorld.Clear();

            LogRiverRibbonGeometryPassBanner(config, grid);

            if (config.debugRiverVisualStats)
            {
                _riverVisualHalfSamples = 0;
                _riverVisualHalfSum = 0.0;
                _riverVisualHalfMin = float.MaxValue;
                _riverVisualHalfMax = float.MinValue;
            }

            float inset = Mathf.Max(0f, config.riverVisualBankInset);

            float sampleW = Mathf.Max(0.06f, config.riverVisualSampleSpacing);
            float csSafe = Mathf.Max(0.0001f, cellSize);
            float minSegCells = Mathf.Clamp(sampleW / csSafe, 0.04f, 2f);
            float maxJumpCells = Mathf.Clamp(Mathf.Max(minSegCells * 2.15f, 1.45f), 1.35f, 2.95f);
            float dedupeEpsCells = 0.055f;
            float dedupeEpsWorld = Mathf.Max(0.035f, cellSize * 0.065f);
            // Un solo remuestreo fino bastaba con ~9k vértices/río; aflojamos paso en celda para menos puntos previos a Catmull.
            float stepCells = Mathf.Clamp(sampleW / csSafe * 0.62f, 0.10f, 0.52f);
            float maxSegmentCells = Mathf.Clamp(minSegCells * 0.82f, 0.16f, 0.92f);

            bool any = false;
            for (int riverIndex = 0; riverIndex < grid.RiverCenterlinesCellSpace.Count; riverIndex++)
            {
                var cellPath = grid.RiverCenterlinesCellSpace[riverIndex];
                if (cellPath == null || cellPath.Count < 2)
                    continue;

                float fullCellsW = riverIndex == 0
                    ? config.riverVisualRibbonFullWidthCellsMain
                    : (config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                        ? config.riverVisualRibbonFullWidthCellsTributary
                        : config.riverVisualRibbonFullWidthCellsMain);
                float halfW = fullCellsW > 0.01f
                    ? Mathf.Max(0.08f, fullCellsW * 0.5f * cellSize - inset)
                    : Mathf.Max(0.08f, config.riverVisualMeshHalfWidth - inset);

                int sourcePoints = cellPath.Count;
                var ribbonCell = new List<Vector2>(cellPath);
                int lapPasses = config != null ? Mathf.Clamp(config.riverRibbonCellSpaceLaplacianPasses, 0, 10) : 2;
                float lapAlpha = config != null ? Mathf.Clamp01(config.riverRibbonCellSpaceLaplacianAlpha) : 0.22f;
                SmoothRibbonPolylineCellSpace(ribbonCell, grid.Width, grid.Height, lapPasses, lapAlpha);
                var cellLakePieces = SplitCellPolylineExcludingLakeCells(grid, ribbonCell);
                if (cellLakePieces.Count == 0)
                    continue;

                float maxJumpWorldStrict = csSafe * maxJumpCells * 1.02f;
                float resampleStepWorld = Mathf.Clamp(sampleW * 0.52f, csSafe * 0.14f, csSafe * 0.62f);
                float catmullStepWorld = Mathf.Clamp(sampleW * 0.48f, csSafe * 0.12f, csSafe * 0.52f);

                int segmentCount = 0;
                int totalSegPoints = 0;
                int sub = 0;

                foreach (var ribbonSegment in cellLakePieces)
                {
                    if (ribbonSegment == null || ribbonSegment.Count < 2)
                        continue;

                    var cellDeduped = DedupeNearlyDuplicateConsecutivePointsCell(ribbonSegment, dedupeEpsCells, riverIndex, config);
                    var cellSubdivided = SubdivideLongSegmentsCell2D(cellDeduped, maxSegmentCells);
                    var cellGapRuns = SplitPolylineAtExcessiveGapsCell(cellSubdivided, maxJumpCells, riverIndex, config);

                    foreach (var cellRun in cellGapRuns)
                    {
                        if (cellRun == null || cellRun.Count < 2)
                            continue;
                        if (cellRun.Count == 2 && Vector2.Distance(cellRun[0], cellRun[1]) > maxJumpCells * 1.01f)
                        {
                            LogRiverRibbonDegenerateSegment(config, riverIndex, sub, cellRun.Count, "cell_two_points_excessive_span");
                            sub++;
                            continue;
                        }

                        var cellResampled = ResamplePolylineUniformCell(cellRun, stepCells);
                        if (cellResampled.Count < 2)
                        {
                            LogRiverRibbonDegenerateSegment(config, riverIndex, sub, cellResampled.Count, "cell_after_resample_lt2");
                            sub++;
                            continue;
                        }

                        var worldPath = BuildWorldPathFromCellCenterline(grid, waterY, cellResampled);
                        var deduped = DedupeNearlyDuplicateConsecutivePoints(worldPath, dedupeEpsWorld, riverIndex, config);
                        var gapRuns = SplitPolylineAtExcessiveGaps(deduped, maxJumpWorldStrict, riverIndex, config);
                        foreach (var run in gapRuns)
                        {
                            if (run == null || run.Count < 2)
                                continue;
                            if (run.Count == 2 && HorizontalDistanceXZ(run[0], run[1]) > maxJumpWorldStrict * 1.01f)
                            {
                                LogRiverRibbonDegenerateSegment(config, riverIndex, sub, run.Count, "world_two_points_excessive_span");
                                sub++;
                                continue;
                            }

                            var preCtrl = ResamplePolylineUniformXZ(run, resampleStepWorld * 1.12f, waterY);
                            if (preCtrl.Count < 2)
                            {
                                LogRiverRibbonDegenerateSegment(config, riverIndex, sub, preCtrl.Count, "after_world_presample_lt2");
                                sub++;
                                continue;
                            }

                            var resampled = ResamplePolylineCatmullRomUniformXZ(preCtrl, catmullStepWorld, waterY);
                            if (resampled.Count < 2)
                            {
                                LogRiverRibbonDegenerateSegment(config, riverIndex, sub, resampled.Count, "after_catmull_lt2");
                                sub++;
                                continue;
                            }

                            var ribbonAfterLake = SplitRiverPolylineExcludingLakeCells(grid, cellSize, resampled, waterY);
                            foreach (var ribbonPath in ribbonAfterLake)
                            {
                                if (ribbonPath == null || ribbonPath.Count < 2)
                                    continue;

                                float len = PolylineLengthXZ(ribbonPath);
                                // Evita "charcos-cinta" y micro-segmentos en confluencias/lago.
                                if (len < Mathf.Max(cellSize * 6f, 6f))
                                {
                                    sub++;
                                    continue;
                                }

                                // Decimación ligera para bajar triángulos sin deformar la forma general del cauce.
                                float ribbonStep = Mathf.Max(catmullStepWorld * 2f, cellSize * 0.85f);
                                var ribbonForMesh = ResamplePolylineUniformXZ(ribbonPath, ribbonStep, waterY);
                                if (ribbonForMesh == null || ribbonForMesh.Count < 2)
                                {
                                    sub++;
                                    continue;
                                }

                                float lenForMesh = PolylineLengthXZ(ribbonForMesh);
                                if (lenForMesh < Mathf.Max(cellSize * 6f, 6f))
                                {
                                    sub++;
                                    continue;
                                }

                                float maxD = MaxConsecutiveHorizontalSegment(ribbonPath);
                                ComputeBoundsXZ(ribbonPath, out Vector3 bMin, out Vector3 bMax);
                                LogRiverRibbonSegmentDetail(config, riverIndex, sub, ribbonPath.Count, len, maxD, bMin, bMax);

                                ApplyRibbonCenterlineLateralJitter(ribbonForMesh, waterY, config, riverIndex, sub);

                                string ribbonGoName = riverIndex == 0
                                    ? $"Water_RiverRibbon_Main_{sub}"
                                    : $"Water_RiverRibbon_Tributary_{riverIndex}_{sub}";
                                if (TryBuildRiverRibbonStripMesh(
                                        parent,
                                        ribbonForMesh,
                                        halfW,
                                        waterY,
                                        mat,
                                        waterLayer,
                                        cellSize,
                                        riverIndex,
                                        sub,
                                        config,
                                        catmullStepWorld,
                                        ribbonGoName,
                                        fullCellsW))
                                {
                                    any = true;
                                    segmentCount++;
                                    totalSegPoints += ribbonForMesh.Count;
                                }

                                sub++;
                            }
                        }
                    }
                }

                LogRiverRibbonRiverAggregate(config, riverIndex, segmentCount, totalSegPoints, sourcePoints);
            }

            if (config.debugRiverVisualStats && _riverVisualHalfSamples > 0)
            {
                float avg = (float)(_riverVisualHalfSum / _riverVisualHalfSamples);
                float span = avg > 1e-5f ? (_riverVisualHalfMax - _riverVisualHalfMin) / avg : 0f;
                Debug.Log($"[RiverVisual] Ribbon semiancho (m): medio={avg:F3} min={_riverVisualHalfMin:F3} max={_riverVisualHalfMax:F3} muestras={_riverVisualHalfSamples} variacionRel={span:F3}");
            }

            return any;
        }

        /// <summary>Jitter lateral suave en la centerline antes del strip (bordes menos “vectoriales”).</summary>
        static void ApplyRibbonCenterlineLateralJitter(List<Vector3> path, float waterY, MapGenConfig config, int riverIndex, int segmentHint)
        {
            if (path == null || path.Count < 2 || config == null) return;
            float jit = config.riverRibbonLateralJitterWorld;
            if (jit < 1e-5f) return;
            float nsc = Mathf.Max(0.06f, config.riverRibbonJitterNoiseScale);
            float seed = config.seed * 0.00017f + riverIndex * 1.713f + segmentHint * 0.331f;
            float acc = 0f;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 tangent;
                if (i == 0) tangent = path[1] - path[0];
                else if (i == path.Count - 1) tangent = path[i] - path[i - 1];
                else tangent = path[i + 1] - path[i - 1];
                tangent.y = 0f;
                if (tangent.sqrMagnitude < 1e-10f) continue;
                tangent.Normalize();
                Vector3 r = Vector3.Cross(Vector3.up, tangent);
                r.y = 0f;
                if (r.sqrMagnitude < 1e-10f) continue;
                r.Normalize();
                if (i > 0) acc += HorizontalDistanceXZ(path[i - 1], path[i]);
                float pn = Mathf.PerlinNoise(seed + acc * nsc * 0.29f, seed * 0.51f + path[i].x * nsc * 0.17f + path[i].z * nsc * 0.13f);
                float off = (pn - 0.5f) * 2f * jit;
                path[i] = new Vector3(path[i].x + r.x * off, waterY, path[i].z + r.z * off);
            }
        }

        private static void LogRiverRibbonGeometryPassBanner(MapGenConfig config, GridSystem grid)
        {
            if (config == null || !config.debugRiverRibbonGeometry)
                return;
            int rivers = grid != null && grid.RiverCenterlinesCellSpace != null ? grid.RiverCenterlinesCellSpace.Count : 0;
            int seed = config.seed;
            string scene = SceneManager.GetActiveScene().name;
            Debug.Log($"[RiverRibbonDebug] seed={seed} scene={scene} rivers={rivers}");
        }

        /// <summary>Solo visual del ribbon: suaviza esquinas (Laplaciano, extremos fijos) sin tocar las celdas River del grid.</summary>
        private static void SmoothRibbonPolylineCellSpace(List<Vector2> poly, int gridW, int gridH, int iterations, float alpha)
        {
            if (poly == null || poly.Count < 3 || iterations <= 0)
                return;
            float minX = 0.5f;
            float maxX = Mathf.Max(minX, gridW - 0.5f);
            float minY = 0.5f;
            float maxY = Mathf.Max(minY, gridH - 0.5f);
            alpha = Mathf.Clamp01(alpha);
            for (int it = 0; it < iterations; it++)
            {
                var copy = new List<Vector2>(poly);
                for (int i = 1; i < poly.Count - 1; i++)
                {
                    Vector2 neighborAvg = (copy[i - 1] + copy[i + 1]) * 0.5f;
                    Vector2 p = Vector2.Lerp(copy[i], neighborAvg, alpha);
                    p.x = Mathf.Clamp(p.x, minX, maxX);
                    p.y = Mathf.Clamp(p.y, minY, maxY);
                    poly[i] = p;
                }
            }
        }

        private static void LogRiverRibbonRiverAggregate(MapGenConfig config, int riverIndex, int segments, int totalPointsInSegments, int sourcePoints)
        {
            if (config == null || !config.debugRiverRibbonGeometry)
                return;
            Debug.Log($"[RiverRibbonDebug] river={riverIndex} sourcePoints={sourcePoints} segments={segments} totalPoints={totalPointsInSegments}");
        }

        private static void LogRiverRibbonSegmentDetail(MapGenConfig config, int riverIndex, int segmentIndex, int pointCount, float length, float maxSegment, Vector3 boundsMin, Vector3 boundsMax)
        {
            if (config == null || !config.debugRiverRibbonGeometry)
                return;
            Debug.Log($"[RiverRibbonDebug] segment={riverIndex}_{segmentIndex} points={pointCount} length={length:F3} maxSegment={maxSegment:F3} boundsMin=({boundsMin.x:F2},{boundsMin.z:F2}) boundsMax=({boundsMax.x:F2},{boundsMax.z:F2})");
        }

        private static void LogRiverRibbonDegenerateSegment(MapGenConfig config, int riverIndex, int segmentIndex, int pointCount, string reason)
        {
            if (config == null || !config.debugRiverRibbonGeometry)
                return;
            Debug.LogWarning($"[RiverRibbonDebug] WARNING degenerate segment | river={riverIndex} segment={segmentIndex} points={pointCount} reason={reason}");
        }

        private static void ComputeBoundsXZ(List<Vector3> path, out Vector3 min, out Vector3 max)
        {
            min = path[0];
            max = path[0];
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 p = path[i];
                min = new Vector3(Mathf.Min(min.x, p.x), p.y, Mathf.Min(min.z, p.z));
                max = new Vector3(Mathf.Max(max.x, p.x), p.y, Mathf.Max(max.z, p.z));
            }
        }

        private static float PolylineLengthXZ(List<Vector3> path)
        {
            if (path == null || path.Count < 2)
                return 0f;
            float s = 0f;
            for (int i = 1; i < path.Count; i++)
                s += HorizontalDistanceXZ(path[i - 1], path[i]);
            return s;
        }

        private static List<Vector3> DedupeNearlyDuplicateConsecutivePoints(List<Vector3> path, float eps, int riverIndex, MapGenConfig config)
        {
            var o = new List<Vector3>(path.Count);
            for (int i = 0; i < path.Count; i++)
            {
                if (o.Count == 0)
                {
                    o.Add(path[i]);
                    continue;
                }

                if (HorizontalDistanceXZ(o[o.Count - 1], path[i]) < eps)
                {
                    if (config != null && config.debugRiverRibbonGeometry)
                        Debug.Log($"[RiverRibbonDebug] dedupe skip near-duplicate | river={riverIndex} idx={i}");
                    continue;
                }

                o.Add(path[i]);
            }

            return o;
        }

        /// <summary>Muestreo uniforme por longitud de arco en XZ (antes del ribbon).</summary>
        private static List<Vector3> ResamplePolylineUniformXZ(List<Vector3> path, float stepWorld, float waterY)
        {
            if (path == null || path.Count == 0)
                return new List<Vector3>();
            if (path.Count == 1)
                return new List<Vector3> { new Vector3(path[0].x, waterY, path[0].z) };

            stepWorld = Mathf.Max(0.05f, stepWorld);
            float totalLen = PolylineLengthXZ(path);
            if (totalLen < 1e-5f)
                return new List<Vector3> { new Vector3(path[0].x, waterY, path[0].z) };

            var o = new List<Vector3>(Mathf.Max(8, Mathf.CeilToInt(totalLen / stepWorld) + 2));
            Vector3 first = new Vector3(path[0].x, waterY, path[0].z);
            o.Add(first);

            float targetDist = stepWorld;
            float acc = 0f;

            for (int seg = 0; seg < path.Count - 1; seg++)
            {
                Vector3 a = new Vector3(path[seg].x, waterY, path[seg].z);
                Vector3 b = new Vector3(path[seg + 1].x, waterY, path[seg + 1].z);
                float sl = HorizontalDistanceXZ(a, b);
                if (sl < 1e-7f)
                    continue;

                while (targetDist <= acc + sl + 1e-5f)
                {
                    float u = (targetDist - acc) / sl;
                    u = Mathf.Clamp01(u);
                    Vector3 p = Vector3.Lerp(a, b, u);
                    p.y = waterY;
                    o.Add(p);
                    targetDist += stepWorld;
                }

                acc += sl;
            }

            Vector3 last = new Vector3(path[path.Count - 1].x, waterY, path[path.Count - 1].z);
            if (HorizontalDistanceXZ(o[o.Count - 1], last) > 1e-4f)
                o.Add(last);
            else
                o[o.Count - 1] = last;

            return o;
        }

        private static Vector2 CatmullRom2D(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        /// <summary>Curva Catmull–Rom en XZ pasando por los puntos de control; remuestreo fino para cauce orgánico.</summary>
        private static List<Vector3> ResamplePolylineCatmullRomUniformXZ(List<Vector3> ctrl, float stepWorld, float waterY)
        {
            if (ctrl == null || ctrl.Count == 0)
                return new List<Vector3>();
            if (ctrl.Count == 1)
                return new List<Vector3> { new Vector3(ctrl[0].x, waterY, ctrl[0].z) };
            if (ctrl.Count == 2)
                return ResamplePolylineUniformXZ(ctrl, stepWorld, waterY);

            stepWorld = Mathf.Max(0.028f, stepWorld);
            var ext = new List<Vector2>(ctrl.Count + 2);
            Vector2 To2(Vector3 v) => new Vector2(v.x, v.z);
            ext.Add(To2(ctrl[0]) * 2f - To2(ctrl[1]));
            for (int i = 0; i < ctrl.Count; i++)
                ext.Add(To2(ctrl[i]));
            ext.Add(To2(ctrl[ctrl.Count - 1]) * 2f - To2(ctrl[ctrl.Count - 2]));

            var o = new List<Vector3>(Mathf.Max(16, Mathf.CeilToInt(PolylineLengthXZ(ctrl) / stepWorld) + 4));
            int nSeg = ctrl.Count - 1;
            for (int seg = 0; seg < nSeg; seg++)
            {
                Vector2 p0 = ext[seg];
                Vector2 p1 = ext[seg + 1];
                Vector2 p2 = ext[seg + 2];
                Vector2 p3 = ext[seg + 3];
                float chord = Vector2.Distance(p1, p2);
                int steps = Mathf.Max(2, Mathf.CeilToInt(chord / stepWorld));
                for (int s = 0; s < steps; s++)
                {
                    if (seg > 0 && s == 0)
                        continue;
                    float t = s / (float)steps;
                    Vector2 q = CatmullRom2D(p0, p1, p2, p3, t);
                    o.Add(new Vector3(q.x, waterY, q.y));
                }
            }

            Vector3 last = new Vector3(ctrl[ctrl.Count - 1].x, waterY, ctrl[ctrl.Count - 1].z);
            if (o.Count == 0 || HorizontalDistanceXZ(o[o.Count - 1], last) > 1e-4f)
                o.Add(last);
            else
                o[o.Count - 1] = last;

            return o;
        }

        private static List<Vector3> BuildWorldPathFromCellCenterline(GridSystem grid, float waterY, List<Vector2> cellPath)
        {
            float cs = Mathf.Max(0.0001f, grid.CellSizeWorld);
            var w = new List<Vector3>(cellPath.Count);
            foreach (var c in cellPath)
                w.Add(new Vector3(grid.Origin.x + c.x * cs, waterY, grid.Origin.z + c.y * cs));
            return w;
        }

        private static float PolylineLength2D(List<Vector2> path)
        {
            if (path == null || path.Count < 2)
                return 0f;
            float s = 0f;
            for (int i = 1; i < path.Count; i++)
                s += Vector2.Distance(path[i - 1], path[i]);
            return s;
        }

        private static List<Vector2> DedupeNearlyDuplicateConsecutivePointsCell(List<Vector2> path, float epsCells, int riverIndex, MapGenConfig config)
        {
            var o = new List<Vector2>(path != null ? path.Count : 0);
            if (path == null)
                return o;
            for (int i = 0; i < path.Count; i++)
            {
                if (o.Count == 0)
                {
                    o.Add(path[i]);
                    continue;
                }

                if (Vector2.Distance(o[o.Count - 1], path[i]) < epsCells)
                {
                    if (config != null && config.debugRiverRibbonGeometry)
                        Debug.Log($"[RiverRibbonDebug] dedupe skip near-duplicate (cell) | river={riverIndex} idx={i}");
                    continue;
                }

                o.Add(path[i]);
            }

            return o;
        }

        private static List<List<Vector2>> SplitPolylineAtExcessiveGapsCell(List<Vector2> path, float maxJumpCells, int riverIndex, MapGenConfig config)
        {
            var outRuns = new List<List<Vector2>>();
            if (path == null || path.Count < 2)
                return outRuns;

            var cur = new List<Vector2>();
            for (int i = 0; i < path.Count; i++)
            {
                if (cur.Count == 0)
                {
                    cur.Add(path[i]);
                    continue;
                }

                float d = Vector2.Distance(cur[cur.Count - 1], path[i]);
                if (d > maxJumpCells)
                {
                    int segLabel = outRuns.Count;
                    if (config != null && config.debugRiverRibbonGeometry)
                        Debug.LogWarning($"[RiverRibbonDebug] WARNING abnormal jump | river={riverIndex} segment={segLabel} idx={i} dist={d:F3} maxAllowed={maxJumpCells:F3} (cell-space)");

                    if (cur.Count >= 2)
                        outRuns.Add(new List<Vector2>(cur));
                    cur.Clear();
                    cur.Add(path[i]);
                    continue;
                }

                cur.Add(path[i]);
            }

            if (cur.Count >= 2)
                outRuns.Add(cur);

            if (outRuns.Count == 0 && path.Count >= 2)
                outRuns.Add(new List<Vector2>(path));

            return outRuns;
        }

        private static List<Vector2> SubdivideLongSegmentsCell2D(List<Vector2> path, float maxSegCells)
        {
            if (path == null || path.Count < 2)
                return path != null ? new List<Vector2>(path) : new List<Vector2>();
            maxSegCells = Mathf.Max(0.12f, maxSegCells);
            var o = new List<Vector2>(path.Count * 2);
            o.Add(path[0]);
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 a = o[o.Count - 1];
                Vector2 b = path[i];
                float d = Vector2.Distance(a, b);
                if (d <= maxSegCells + 1e-5f)
                {
                    o.Add(b);
                    continue;
                }

                int steps = Mathf.CeilToInt(d / maxSegCells);
                for (int s = 1; s < steps; s++)
                {
                    float t = s / (float)steps;
                    o.Add(Vector2.Lerp(a, b, t));
                }

                o.Add(b);
            }

            return o;
        }

        private static List<Vector2> ResamplePolylineUniformCell(List<Vector2> path, float stepCells)
        {
            if (path == null || path.Count == 0)
                return new List<Vector2>();
            if (path.Count == 1)
                return new List<Vector2> { path[0] };

            stepCells = Mathf.Max(0.04f, stepCells);
            float totalLen = PolylineLength2D(path);
            if (totalLen < 1e-5f)
                return new List<Vector2> { path[0] };

            var o = new List<Vector2>(Mathf.Max(8, Mathf.CeilToInt(totalLen / stepCells) + 2));
            o.Add(path[0]);

            float targetDist = stepCells;
            float acc = 0f;

            for (int seg = 0; seg < path.Count - 1; seg++)
            {
                Vector2 a = path[seg];
                Vector2 b = path[seg + 1];
                float sl = Vector2.Distance(a, b);
                if (sl < 1e-7f)
                    continue;

                while (targetDist <= acc + sl + 1e-5f)
                {
                    float u = (targetDist - acc) / sl;
                    u = Mathf.Clamp01(u);
                    o.Add(Vector2.Lerp(a, b, u));
                    targetDist += stepCells;
                }

                acc += sl;
            }

            Vector2 last = path[path.Count - 1];
            if (Vector2.Distance(o[o.Count - 1], last) > 1e-4f)
                o.Add(last);
            else
                o[o.Count - 1] = last;

            return o;
        }

        private static float HorizontalDistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float MaxConsecutiveHorizontalSegment(List<Vector3> path)
        {
            if (path == null || path.Count < 2)
                return 0f;
            float m = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                float d = HorizontalDistanceXZ(path[i - 1], path[i]);
                if (d > m)
                    m = d;
            }

            return m;
        }

        /// <summary>Parte la polilínea donde el salto consecutivo supera el umbral (antes de resample).</summary>
        private static List<List<Vector3>> SplitPolylineAtExcessiveGaps(List<Vector3> path, float maxSegWorld, int riverIndex, MapGenConfig config)
        {
            var outRuns = new List<List<Vector3>>();
            if (path == null || path.Count < 2)
                return outRuns;

            var cur = new List<Vector3>();
            for (int i = 0; i < path.Count; i++)
            {
                if (cur.Count == 0)
                {
                    cur.Add(path[i]);
                    continue;
                }

                float d = HorizontalDistanceXZ(cur[cur.Count - 1], path[i]);
                if (d > maxSegWorld)
                {
                    int segLabel = outRuns.Count;
                    if (config != null && config.debugRiverRibbonGeometry)
                        Debug.LogWarning($"[RiverRibbonDebug] WARNING abnormal jump | river={riverIndex} segment={segLabel} idx={i} dist={d:F3} maxAllowed={maxSegWorld:F3}");

                    if (cur.Count >= 2)
                        outRuns.Add(new List<Vector3>(cur));
                    cur.Clear();
                    cur.Add(path[i]);
                    continue;
                }

                cur.Add(path[i]);
            }

            if (cur.Count >= 2)
                outRuns.Add(cur);

            if (outRuns.Count == 0 && path.Count >= 2)
                outRuns.Add(new List<Vector3>(path));

            return outRuns;
        }

        /// <summary>Parte la centerline en espacio celda: tramos fuera de celdas Water (tras suavizado Laplaciano).</summary>
        private static List<List<Vector2>> SplitCellPolylineExcludingLakeCells(GridSystem grid, List<Vector2> path)
        {
            var runs = new List<List<Vector2>>();
            if (path == null || path.Count == 0)
                return runs;
            var cur = new List<Vector2>();
            for (int i = 0; i < path.Count; i++)
            {
                Vector2 pi = path[i];
                if (cur.Count == 0)
                {
                    if (!CellSampleIsLakeGrid(grid, pi))
                        cur.Add(pi);
                    continue;
                }

                Vector2 prev = cur[cur.Count - 1];
                Vector2 mid = (prev + pi) * 0.5f;
                if (CellSampleIsLakeGrid(grid, mid) || CellSampleIsLakeGrid(grid, pi))
                {
                    if (cur.Count >= 2)
                        runs.Add(new List<Vector2>(cur));
                    cur.Clear();
                    if (!CellSampleIsLakeGrid(grid, pi))
                        cur.Add(pi);
                    continue;
                }

                cur.Add(pi);
            }

            if (cur.Count >= 2)
                runs.Add(cur);
            return runs;
        }

        private static bool CellSampleIsLakeGrid(GridSystem grid, Vector2 cellSample)
        {
            int gx = Mathf.FloorToInt(cellSample.x);
            int gz = Mathf.FloorToInt(cellSample.y);
            if (!grid.InBoundsCell(gx, gz))
                return false;
            return grid.GetCell(gx, gz).type == CellType.Water;
        }

        /// <summary>Parte la polilínea en tramos cuyo punto/medio no cae en celda Water.</summary>
        private static List<List<Vector3>> SplitRiverPolylineExcludingLakeCells(GridSystem grid, float cellSize, List<Vector3> path, float waterY)
        {
            var runs = new List<List<Vector3>>();
            var cur = new List<Vector3>();

            for (int i = 0; i < path.Count; i++)
            {
                var pi = new Vector3(path[i].x, waterY, path[i].z);

                if (cur.Count == 0)
                {
                    if (!WorldXZIsLakeCell(grid, cellSize, pi))
                        cur.Add(pi);
                    continue;
                }

                Vector3 prev = cur[cur.Count - 1];
                Vector3 mid = (prev + pi) * 0.5f;
                if (WorldXZIsLakeCell(grid, cellSize, mid) || WorldXZIsLakeCell(grid, cellSize, pi))
                {
                    if (cur.Count >= 2)
                        runs.Add(new List<Vector3>(cur));
                    cur.Clear();
                    if (!WorldXZIsLakeCell(grid, cellSize, pi))
                        cur.Add(pi);
                    continue;
                }

                cur.Add(pi);
            }

            if (cur.Count >= 2)
                runs.Add(cur);
            return runs;
        }

        private static bool WorldXZIsLakeCell(GridSystem grid, float cellSize, Vector3 w)
        {
            int gx = Mathf.FloorToInt((w.x - grid.Origin.x) / cellSize);
            int gz = Mathf.FloorToInt((w.z - grid.Origin.z) / cellSize);
            if (!grid.InBoundsCell(gx, gz))
                return false;
            return grid.GetCell(gx, gz).type == CellType.Water;
        }

        /// <summary>Strip por segmento: tangente por arista, lateral estable (evita flip 180°) y semiancho opcionalmente modulado con ruido suave.</summary>
        private static bool TryBuildRiverRibbonStripMesh(
            Transform parent,
            List<Vector3> pts,
            float halfWidthWorld,
            float waterY,
            Material mat,
            int waterLayer,
            float cellSize,
            int riverIndex,
            int segmentIndex,
            MapGenConfig config,
            float ribbonResampleStepWorld,
            string objectName,
            float ribbonFullWidthCellsForLog)
        {
            if (pts == null || pts.Count < 2)
                return false;

            float sampleW = config != null ? Mathf.Max(0.06f, config.riverVisualSampleSpacing) : 0.4f;
            float stepRef = ribbonResampleStepWorld > 1e-5f
                ? ribbonResampleStepWorld
                : Mathf.Clamp(sampleW * 0.38f, cellSize * 0.055f, cellSize * 0.34f);
            float maxEdge = Mathf.Clamp(stepRef * 1.38f, cellSize * 0.68f, cellSize * 2.25f);
            if (config != null && config.riverMaxSegmentLengthWorld > 1e-4f)
                maxEdge = Mathf.Min(maxEdge, config.riverMaxSegmentLengthWorld);

            var work = new List<Vector3>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
                work.Add(new Vector3(pts[i].x, waterY, pts[i].z));
            if (config != null && config.debugDrawRiverRibbonGizmos)
                DebugRibbonPathPointsWorld.AddRange(work);

            // Umbral robusto: adapta el máximo permitido al paso real del path para no marcar como "abnormal"
            // un ribbon ya decimado (caso seed 33069), pero mantiene corte para saltos verdaderamente largos.
            float avgStep = (work.Count > 1) ? (PolylineLengthXZ(work) / (work.Count - 1)) : 0f;
            if (avgStep > 1e-4f)
                maxEdge = Mathf.Max(maxEdge, avgStep * 1.8f);

            float varAmt = config != null ? Mathf.Clamp01(config.riverRibbonWidthVariation) : 0f;
            float freq = config != null ? Mathf.Max(0.001f, config.riverRibbonWidthNoiseFreq) : 0.1f;
            float phase = riverIndex * 19.17f + segmentIndex * 7.41f;
            float perlinBlend = config != null ? Mathf.Clamp01(config.riverRibbonPerlinWidthBlend) : 0f;
            float perlinFreq = config != null ? Mathf.Max(0.001f, config.riverRibbonPerlinWidthFreq) : 0.09f;
            float perlinOff = (config != null ? config.seed * 0.00011f : 0f) + riverIndex * 3.907f + segmentIndex * 2.173f;
            var halfPerVertex = new float[work.Count];
            float accLen = 0f;
            for (int i = 0; i < work.Count; i++)
            {
                if (i > 0)
                    accLen += HorizontalDistanceXZ(work[i - 1], work[i]);
                float n =
                    Mathf.Sin(accLen * freq * (Mathf.PI * 2f) + phase) * 0.40f +
                    Mathf.Sin(accLen * freq * 0.47f * (Mathf.PI * 2f) + phase * 1.3f) * 0.34f +
                    Mathf.Sin(accLen * freq * 0.13f * (Mathf.PI * 2f) + phase * 0.71f) * 0.28f;
                float halfBase = halfWidthWorld * (1f + varAmt * n * 1.22f);
                float perlin01 = Mathf.PerlinNoise(perlinOff + accLen * perlinFreq, perlinOff * 0.37f + segmentIndex * 0.19f);
                float widthMul = Mathf.Lerp(1f, 0.8f + perlin01 * 0.4f, perlinBlend);
                halfPerVertex[i] = halfBase * widthMul;
            }

            if (varAmt > 1e-4f && work.Count > 2)
            {
                for (int pass = 0; pass < 3; pass++)
                {
                    var copy = (float[])halfPerVertex.Clone();
                    for (int i = 1; i < work.Count - 1; i++)
                        halfPerVertex[i] = (copy[i - 1] + 2f * copy[i] + copy[i + 1]) * 0.25f;
                }
            }

            float minMul = config != null ? Mathf.Clamp(config.riverRibbonHalfWidthMinMul, 0.45f, 1f) : 0.66f;
            float maxMul = config != null ? Mathf.Clamp(config.riverRibbonHalfWidthMaxMul, minMul + 0.02f, 1.75f) : 1.42f;
            for (int i = 0; i < work.Count; i++)
                halfPerVertex[i] = Mathf.Clamp(halfPerVertex[i], halfWidthWorld * minMul, halfWidthWorld * maxMul);

            if (config != null && config.debugRiverVisualStats)
            {
                for (int i = 0; i < work.Count; i++)
                {
                    float h = halfPerVertex[i];
                    _riverVisualHalfSamples++;
                    _riverVisualHalfSum += h;
                    if (h < _riverVisualHalfMin) _riverVisualHalfMin = h;
                    if (h > _riverVisualHalfMax) _riverVisualHalfMax = h;
                }
            }

            Vector3 TangentAt(int idx)
            {
                int n = work.Count;
                if (n < 2)
                    return Vector3.forward;
                if (idx <= 0)
                {
                    Vector3 d = work[1] - work[0];
                    d.y = 0f;
                    return d.sqrMagnitude > 1e-10f ? d.normalized : Vector3.forward;
                }
                if (idx >= n - 1)
                {
                    Vector3 d = work[n - 1] - work[n - 2];
                    d.y = 0f;
                    return d.sqrMagnitude > 1e-10f ? d.normalized : Vector3.forward;
                }
                Vector3 dm = work[idx + 1] - work[idx - 1];
                dm.y = 0f;
                return dm.sqrMagnitude > 1e-10f ? dm.normalized : Vector3.forward;
            }

            var verts = new List<Vector3>((work.Count - 1) * 4);
            var uvs = new List<Vector2>((work.Count - 1) * 4);
            var tris = new List<int>((work.Count - 1) * 6);

            float uScale = Mathf.Max(halfWidthWorld * 2f, Mathf.Max(0.01f, cellSize) * 0.5f);
            float uAcc = 0f;
            Vector3 rPrev = Vector3.zero;
            bool haveR = false;

            for (int i = 0; i < work.Count - 1; i++)
            {
                Vector3 a = work[i];
                Vector3 b = work[i + 1];
                Vector3 d = b - a;
                d.y = 0f;
                float sl = d.magnitude;
                if (sl < 1e-5f)
                    continue;
                if (sl > maxEdge)
                {
                    if (config != null && config.debugRiverRibbonGeometry)
                        Debug.LogWarning($"[RiverRibbonDebug] WARNING abnormal jump | seed={config.seed} river={riverIndex} segment={segmentIndex} idx={i + 1} dist={sl:F3} maxAllowed={maxEdge:F3} (pre-triangulation)");
                    if (config != null && config.debugDrawRiverRibbonGizmos)
                    {
                        DebugRibbonDiscardedSegmentsAWorld.Add(a);
                        DebugRibbonDiscardedSegmentsBWorld.Add(b);
                    }
                    // Corte quirúrgico: no triangulamos este salto; el strip continúa como sub-tramo.
                    continue;
                }
                if (config != null && config.debugDrawRiverRibbonGizmos)
                {
                    DebugRibbonAcceptedSegmentsAWorld.Add(a);
                    DebugRibbonAcceptedSegmentsBWorld.Add(b);
                }

                Vector3 ta = TangentAt(i);
                Vector3 tb = TangentAt(i + 1);
                Vector3 tMid = ta + tb;
                tMid.y = 0f;
                if (tMid.sqrMagnitude < 1e-12f)
                    tMid = d.sqrMagnitude > 1e-12f ? d.normalized : ta;
                else
                    tMid.Normalize();

                Vector3 r = Vector3.Cross(Vector3.up, tMid);
                r.y = 0f;
                if (r.sqrMagnitude < 1e-12f)
                {
                    if (config != null && config.debugRiverRibbonGeometry)
                        Debug.LogWarning($"[RiverRibbonDebug] WARNING invalid tangent | river={riverIndex} segment={segmentIndex} idx={i} (cross)");
                    continue;
                }

                r.Normalize();
                if (haveR && Vector3.Dot(rPrev, r) < 0f)
                    r = -r;
                rPrev = r;
                haveR = true;

                float hwA = halfPerVertex[i];
                float hwB = halfPerVertex[i + 1];

                int b0 = verts.Count;
                verts.Add(a - r * hwA);
                uvs.Add(new Vector2(uAcc / uScale, 0f));
                verts.Add(a + r * hwA);
                uvs.Add(new Vector2(uAcc / uScale, 1f));
                verts.Add(b + r * hwB);
                uvs.Add(new Vector2((uAcc + sl) / uScale, 1f));
                verts.Add(b - r * hwB);
                uvs.Add(new Vector2((uAcc + sl) / uScale, 0f));

                tris.Add(b0);
                tris.Add(b0 + 3);
                tris.Add(b0 + 1);
                tris.Add(b0 + 1);
                tris.Add(b0 + 3);
                tris.Add(b0 + 2);

                uAcc += sl;
            }

            if (verts.Count == 0 || tris.Count == 0)
                return false;

            var mesh = new Mesh();
            mesh.name = objectName;
            if (verts.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            var colors = new List<Color>(verts.Count);
            for (int i = 0; i < verts.Count; i++)
                colors.Add(Color.white);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            WaterStylizedIntegration.PrepareMesh(mesh, mat);

            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.layer = waterLayer;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (mat != null)
            {
                mr.sharedMaterial = mat;
                mr.enabled = true;
            }

            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.renderingLayerMask = 1u;
            WaterStylizedIntegration.AttachWaterObject(go, mf, mr, mat);
            Debug.Log(
                $"[RiverRibbonMesh] riverId={riverIndex} class=MeshRenderer points={pts.Count} verts={verts.Count} tris={tris.Count / 3} " +
                $"fullWidthCells={ribbonFullWidthCellsForLog:F2} yOffset={waterY:F3} material={(mat != null ? mat.name : "null")} enabled={mr.enabled}");
            return true;
        }

        private static int[,] BuildWaterInteriorDistanceToShoreGrid(GridSystem grid, out int maxDist)
        {
            maxDist = 1;
            int w = grid.Width;
            int h = grid.Height;
            var d = new int[w, h];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    d[x, z] = -1;

            var q = new Queue<Vector2Int>();
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    var t = grid.GetCell(x, z).type;
                    if (t != CellType.Water && t != CellType.River)
                        continue;
                    bool boundary = false;
                    foreach (var n in grid.Neighbors4(x, z))
                    {
                        var nt = grid.GetCell(n.x, n.y).type;
                        if (nt != CellType.Water && nt != CellType.River)
                        {
                            boundary = true;
                            break;
                        }
                    }
                    if (boundary)
                    {
                        d[x, z] = 0;
                        q.Enqueue(new Vector2Int(x, z));
                    }
                }
            }

            if (q.Count == 0)
            {
                for (int z = 0; z < h; z++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        var t = grid.GetCell(x, z).type;
                        if (t == CellType.Water || t == CellType.River)
                            d[x, z] = 0;
                    }
                }
                return d;
            }

            while (q.Count > 0)
            {
                var p = q.Dequeue();
                int baseD = d[p.x, p.y];
                foreach (var n in grid.Neighbors4(p.x, p.y))
                {
                    var nt = grid.GetCell(n.x, n.y).type;
                    if (nt != CellType.Water && nt != CellType.River)
                        continue;
                    int nd = baseD + 1;
                    if (d[n.x, n.y] >= 0)
                        continue;
                    d[n.x, n.y] = nd;
                    if (nd > maxDist)
                        maxDist = nd;
                    q.Enqueue(n);
                }
            }

            return d;
        }

        /// <summary>Distancia Chebyshev al borde de la máscara MS del lago (solo celdas dentro del rect).</summary>
        static int MaxInteriorDistance(int[,] d)
        {
            if (d == null)
                return 1;
            int max = 1;
            int w = d.GetLength(0);
            int h = d.GetLength(1);
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    if (d[x, z] > max && d[x, z] < 1_000_000)
                        max = d[x, z];
            return max;
        }

        static int[,] BuildLakeRectInteriorDistanceGrid(bool[,] mask, int rectW, int rectH, out int maxDist)
        {
            maxDist = 1;
            var d = new int[rectW, rectH];
            for (int z = 0; z < rectH; z++)
                for (int x = 0; x < rectW; x++)
                    d[x, z] = -1;

            var q = new Queue<Vector2Int>();
            for (int z = 0; z < rectH; z++)
            {
                for (int x = 0; x < rectW; x++)
                {
                    if (!mask[x, z])
                        continue;
                    bool boundary = x == 0 || z == 0 || x == rectW - 1 || z == rectH - 1;
                    if (!boundary)
                    {
                        if (!mask[x - 1, z] || !mask[x + 1, z] || !mask[x, z - 1] || !mask[x, z + 1])
                            boundary = true;
                    }

                    if (boundary)
                    {
                        d[x, z] = 0;
                        q.Enqueue(new Vector2Int(x, z));
                    }
                }
            }

            if (q.Count == 0)
            {
                for (int z = 0; z < rectH; z++)
                    for (int x = 0; x < rectW; x++)
                        if (mask[x, z])
                            d[x, z] = 0;
                return d;
            }

            while (q.Count > 0)
            {
                var p = q.Dequeue();
                int baseD = d[p.x, p.y];
                for (int ni = 0; ni < 4; ni++)
                {
                    int nx = p.x + (ni == 0 ? 1 : ni == 1 ? -1 : 0);
                    int nz = p.y + (ni == 2 ? 1 : ni == 3 ? -1 : 0);
                    if ((uint)nx >= (uint)rectW || (uint)nz >= (uint)rectH)
                        continue;
                    if (!mask[nx, nz] || d[nx, nz] >= 0)
                        continue;
                    int nd = baseD + 1;
                    d[nx, nz] = nd;
                    if (nd > maxDist)
                        maxDist = nd;
                    q.Enqueue(new Vector2Int(nx, nz));
                }
            }

            return d;
        }

        /// <summary>Distancia interior en celdas; -1 si fuera de la matriz o celda no acuática en el rect MS.</summary>
        static int SampleInteriorDistanceCells(
            int gx,
            int gz,
            int[,] dist,
            int rectMinX,
            int rectMinZ,
            int rectW,
            int rectH)
        {
            if (dist == null)
                return -1;

            int dw = dist.GetLength(0);
            int dh = dist.GetLength(1);
            bool useRect = rectW > 0 && rectH > 0 && rectMinX >= 0 && rectMinZ >= 0;
            if (useRect)
            {
                if (dw != rectW || dh != rectH)
                    return -1;
                int lx = gx - rectMinX;
                int lz = gz - rectMinZ;
                if ((uint)lx >= (uint)rectW || (uint)lz >= (uint)rectH)
                    return -1;
                return dist[lx, lz];
            }

            if ((uint)gx >= (uint)dw || (uint)gz >= (uint)dh)
                return -1;
            return dist[gx, gz];
        }

        private static float SampleInteriorDistance01(
            Vector3 world,
            int[,] distGrid,
            GridSystem grid,
            float cellSize,
            float normCells,
            int rectMinX = -1,
            int rectMinZ = -1,
            int rectW = -1,
            int rectH = -1)
        {
            if (distGrid == null) return 0.5f;
            float fx = (world.x - grid.Origin.x) / cellSize;
            float fz = (world.z - grid.Origin.z) / cellSize;
            int x0 = Mathf.FloorToInt(fx);
            int z0 = Mathf.FloorToInt(fz);
            float tx = fx - x0;
            float tz = fz - z0;
            bool useRect = rectMinX >= 0 && rectMinZ >= 0 && rectW > 0 && rectH > 0;

            float Sample(int cx, int cz)
            {
                if (useRect)
                {
                    if (cx < rectMinX || cz < rectMinZ || cx >= rectMinX + rectW || cz >= rectMinZ + rectH)
                        return 0f;
                }
                else
                {
                    int dw = distGrid.GetLength(0);
                    int dh = distGrid.GetLength(1);
                    if ((uint)cx >= (uint)dw || (uint)cz >= (uint)dh)
                        return 0f;
                }

                int v = SampleInteriorDistanceCells(cx, cz, distGrid, rectMinX, rectMinZ, rectW, rectH);
                if (v < 0)
                    return 0f;
                return Mathf.Clamp01(v / Mathf.Max(1f, normCells));
            }

            float c00 = Sample(x0, z0);
            float c10 = Sample(x0 + 1, z0);
            float c01 = Sample(x0, z0 + 1);
            float c11 = Sample(x0 + 1, z0 + 1);
            float a = Mathf.Lerp(c00, c10, tx);
            float b = Mathf.Lerp(c01, c11, tx);
            return Mathf.Lerp(a, b, tz);
        }

        internal static float SampleLakeMouthProximity01(Vector3 world, GridSystem grid, MapGenConfig config)
        {
            if (grid?.LakeMouthCellsPacked == null || grid.LakeMouthCellsPacked.Count == 0 || config == null)
                return 0f;
            float cs = Mathf.Max(1e-4f, grid.CellSizeWorld);
            int gx = Mathf.FloorToInt((world.x - grid.Origin.x) / cs);
            int gz = Mathf.FloorToInt((world.z - grid.Origin.z) / cs);
            float radius = Mathf.Clamp(config.lakeRiverMouthBlendCells + 2.5f, 3f, 10f);
            float best = radius + 1f;
            foreach (long pk in grid.LakeMouthCellsPacked)
            {
                int mx = (int)(pk >> 32);
                int mz = (int)(pk & 0xffffffffL);
                float d = Mathf.Max(Mathf.Abs(gx - mx), Mathf.Abs(gz - mz));
                if (d < best)
                    best = d;
            }

            return Mathf.Clamp01(1f - best / radius);
        }

        static bool CellTouchesLakeBodyOrMouth(GridSystem grid, int gx, int gz, HashSet<long> lakeBody)
        {
            if (grid == null)
                return false;
            if (lakeBody != null && lakeBody.Contains(PackCellLongMask(gx, gz)))
                return true;
            if (grid.LakeMouthCellsPacked != null && grid.LakeMouthCellsPacked.Contains(PackCellLongMask(gx, gz)))
                return true;
            foreach (var n in grid.Neighbors4(gx, gz))
            {
                if (lakeBody != null && lakeBody.Contains(PackCellLongMask(n.x, n.y)))
                    return true;
                if (grid.LakeMouthCellsPacked != null && grid.LakeMouthCellsPacked.Contains(PackCellLongMask(n.x, n.y)))
                    return true;
                if (grid.GetCell(n.x, n.y).type == CellType.Water)
                    return true;
            }

            return false;
        }

        private static float ComputeRiverVisualWidthMultiplierRaw(GridSystem grid, int gx, int gz, float cellX, float cellZ, MapGenConfig config)
        {
            int w = grid.Width;
            int h = grid.Height;
            float downT = (gx + gz) / Mathf.Max(1f, (float)(w + h - 2));
            float mul = Mathf.Lerp(1f, Mathf.Max(0.5f, config.riverWidthDownstreamFactor), downT);
            int rc = 0;
            for (int dz = -2; dz <= 2; dz++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int xx = gx + dx;
                    int zz = gz + dz;
                    if (!grid.InBoundsCell(xx, zz)) continue;
                    if (grid.GetCell(xx, zz).type == CellType.River)
                        rc++;
                }
            }
            float conf = Mathf.Clamp01((rc - 4) / 18f);
            mul *= 1f + config.riverWidthConfluenceBoost * conf;
            float amp = Mathf.Clamp(config.riverWidthNoiseScale, 0f, 0.45f);
            if (amp > 1e-5f)
            {
                float ns = Mathf.Max(0.06f, config.waterEdgeNoiseScale * 0.55f + 0.06f);
                float n = Mathf.PerlinNoise(config.seed * 0.00117f + cellX * ns * 4.1f, config.seed * 0.00191f + cellZ * ns * 4.1f);
                mul *= 1f + (n - 0.5f) * 2f * amp;
            }
            return Mathf.Clamp(mul, 0.45f, 1.85f);
        }

        private static float ComputeRiverVisualWidthMultiplier(GridSystem grid, int gx, int gz, float cellX, float cellZ, MapGenConfig config)
        {
            // Suavizado espacial para evitar cambios serruchados de celda a celda.
            float center = ComputeRiverVisualWidthMultiplierRaw(grid, gx, gz, cellX, cellZ, config);
            float acc = center * 0.5f;
            float wAcc = 0.5f;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = gx + dx;
                    int nz = gz + dz;
                    if (!grid.InBoundsCell(nx, nz)) continue;
                    if (grid.GetCell(nx, nz).type != CellType.River) continue;
                    float nMul = ComputeRiverVisualWidthMultiplierRaw(grid, nx, nz, nx, nz, config);
                    float ww = (Mathf.Abs(dx) + Mathf.Abs(dz) == 1) ? 0.125f : 0.0625f;
                    acc += nMul * ww;
                    wAcc += ww;
                }
            }
            return Mathf.Clamp(acc / Mathf.Max(1e-4f, wAcc), 0.45f, 1.85f);
        }

        private static void ApplyRiverLakeVisualBlendField(
            float[,] field,
            int sw,
            int sh,
            int sampleX0,
            int sampleZ0,
            int effectiveSubdiv,
            GridSystem grid,
            MapGenConfig config,
            bool marchingSquaresLakesOnlyRibbon,
            bool[,] coarseMask = null,
            int rectMinX = 0,
            int rectMinZ = 0,
            int rectW = 0,
            int rectH = 0)
        {
            if (config == null || config.riverLakeVisualBlend <= 1e-5f || grid == null)
                return;

            float blend = Mathf.Clamp01(config.riverLakeVisualBlend) * 0.5f;
            if (marchingSquaresLakesOnlyRibbon)
                blend = Mathf.Clamp01(config.riverLakeVisualBlend) * 0.62f;

            var lakeBody = grid.LakeBodyCellsPacked;
            for (int z = 0; z < sh; z++)
            {
                int iz = sampleZ0 + z;
                int gz = Mathf.Clamp(iz / effectiveSubdiv, 0, grid.Height - 1);
                for (int x = 0; x < sw; x++)
                {
                    int ix = sampleX0 + x;
                    int gx = Mathf.Clamp(ix / effectiveSubdiv, 0, grid.Width - 1);
                    bool touchLake;
                    bool nearLake;
                    if (marchingSquaresLakesOnlyRibbon && coarseMask != null && rectW > 0 && rectH > 0)
                    {
                        int mx = gx - rectMinX;
                        int mz = gz - rectMinZ;
                        if ((uint)mx >= (uint)rectW || (uint)mz >= (uint)rectH)
                            continue;
                        if (!coarseMask[mx, mz] && grid.GetCell(gx, gz).type != CellType.River)
                            continue;
                        touchLake = CellTouchesLakeBodyOrMouth(grid, gx, gz, lakeBody);
                        nearLake = !touchLake && CellNearLakeMouthOrBody(grid, gx, gz, lakeBody, config);
                    }
                    else
                    {
                        if (grid.GetCell(gx, gz).type != CellType.River)
                            continue;
                        touchLake = false;
                        foreach (var n in grid.Neighbors4(gx, gz))
                        {
                            if (grid.GetCell(n.x, n.y).type == CellType.Water)
                            {
                                touchLake = true;
                                break;
                            }
                        }

                        nearLake = false;
                        if (!touchLake)
                        {
                            for (int dz = -1; dz <= 1 && !nearLake; dz++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    if (dx == 0 && dz == 0) continue;
                                    int nx = gx + dx;
                                    int nz = gz + dz;
                                    if (!grid.InBoundsCell(nx, nz)) continue;
                                    if (grid.GetCell(nx, nz).type == CellType.Water)
                                    {
                                        nearLake = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (touchLake)
                    {
                        Vector3 mouthW = grid.CellToWorldCenter(gx, gz);
                        float mouthBoost = SampleLakeMouthProximity01(mouthW, grid, config);
                        float tBlend = Mathf.Lerp(blend, Mathf.Min(1f, blend * 1.35f), mouthBoost * 0.65f);
                        field[x, z] = Mathf.Lerp(field[x, z], 1f, tBlend);
                    }
                    else if (nearLake)
                    {
                        Vector3 mouthW = grid.CellToWorldCenter(gx, gz);
                        float mouthBoost = SampleLakeMouthProximity01(mouthW, grid, config);
                        field[x, z] = Mathf.Lerp(field[x, z], 1f, blend * (0.48f + mouthBoost * 0.42f));
                    }
                }
            }
        }

        static bool CellNearLakeMouthOrBody(GridSystem grid, int gx, int gz, HashSet<long> lakeBody, MapGenConfig config)
        {
            int r = Mathf.Clamp(config != null ? config.lakeRiverMouthBlendCells + 2 : 4, 2, 10);
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    int nx = gx + dx;
                    int nz = gz + dz;
                    if (!grid.InBoundsCell(nx, nz))
                        continue;
                    if (CellTouchesLakeBodyOrMouth(grid, nx, nz, lakeBody))
                        return true;
                }
            }

            return false;
        }

        private static void ApplyMarchingSquaresDepthUv(
            List<Vector2> uvs,
            List<Vector3> verts,
            GridSystem grid,
            MapGenConfig config,
            int[,] distGrid,
            float yWater,
            int rectMinX = -1,
            int rectMinZ = -1,
            int rectW = -1,
            int rectH = -1)
        {
            if (config == null || distGrid == null || Mathf.Abs(config.waterDepthColorStrength) < 1e-5f)
                return;
            float normLake = Mathf.Max(1f, config.lakeShoreVisualWidth > 0.01f ? config.lakeShoreVisualWidth : config.shoreVisualWidth);
            float normRiver = Mathf.Max(1f, config.riverShoreVisualWidth > 0.01f ? config.riverShoreVisualWidth : config.shoreVisualWidth);
            float powK = Mathf.Max(0.35f, config.shoreVisualBlend);
            float str = Mathf.Clamp01(config.waterDepthColorStrength);
            float cs = Mathf.Max(1e-5f, grid.CellSizeWorld);
            int gw = grid.Width;
            int gh = grid.Height;
            for (int i = 0; i < uvs.Count && i < verts.Count; i++)
            {
                int gx = Mathf.Clamp(Mathf.FloorToInt((verts[i].x - grid.Origin.x) / cs), 0, gw - 1);
                int gz = Mathf.Clamp(Mathf.FloorToInt((verts[i].z - grid.Origin.z) / cs), 0, gh - 1);
                var ct = grid.GetCell(gx, gz).type;
                float norm = ct == CellType.River ? normRiver : normLake;
                float depth01 = SampleInteriorDistance01(
                    verts[i],
                    distGrid,
                    grid,
                    grid.CellSizeWorld,
                    norm,
                    rectMinX,
                    rectMinZ,
                    rectW,
                    rectH);
                if (grid.WaterDepth01 != null && rectMinX < 0)
                    depth01 = Mathf.Max(depth01, WaterSurfaceFieldBuilder.SampleDepth01(grid, verts[i]));
                depth01 = Mathf.Pow(Mathf.Clamp01(depth01), 1f / powK);
                depth01 = depth01 * depth01 * (3f - 2f * depth01); // smoothstep
                float targetV = Mathf.Lerp(0.06f, 0.5f, depth01);
                var uv = uvs[i];
                uv.y = Mathf.Lerp(uv.y, targetV, str);
                uvs[i] = uv;
            }
        }

        private static void ApplyPlaneLikeLakeUvs(List<Vector2> uvs, List<Vector3> verts)
        {
            if (uvs == null || verts == null || uvs.Count == 0 || verts.Count == 0)
                return;

            float minX = float.PositiveInfinity;
            float minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxZ = float.NegativeInfinity;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i];
                if (v.x < minX) minX = v.x;
                if (v.z < minZ) minZ = v.z;
                if (v.x > maxX) maxX = v.x;
                if (v.z > maxZ) maxZ = v.z;
            }

            float invX = maxX > minX ? 1f / (maxX - minX) : 1f;
            float invZ = maxZ > minZ ? 1f / (maxZ - minZ) : 1f;
            for (int i = 0; i < uvs.Count && i < verts.Count; i++)
            {
                Vector3 v = verts[i];
                uvs[i] = new Vector2((v.x - minX) * invX, (v.z - minZ) * invZ);
            }
        }

        private static void ExpandLakeMsPerimeterVertices(
            List<Vector3> verts,
            List<bool> perimeterVerts,
            float[,] field,
            int sw,
            int sh,
            int sampleX0,
            int sampleZ0,
            float step,
            Vector3 origin,
            float expandWorld)
        {
            if (verts == null || perimeterVerts == null || field == null || expandWorld <= 1e-4f || step <= 1e-5f || sw < 3 || sh < 3)
                return;

            Vector3 center = Vector3.zero;
            int count = 0;
            for (int i = 0; i < verts.Count; i++)
            {
                if (i >= perimeterVerts.Count || !perimeterVerts[i])
                    continue;
                center += verts[i];
                count++;
            }
            if (count > 0)
                center /= count;

            float amount = Mathf.Max(0f, expandWorld);
            for (int i = 0; i < verts.Count && i < perimeterVerts.Count; i++)
            {
                if (!perimeterVerts[i])
                    continue;

                Vector3 v = verts[i];
                int sx = Mathf.Clamp(Mathf.RoundToInt((v.x - origin.x) / step) - sampleX0, 1, sw - 2);
                int sz = Mathf.Clamp(Mathf.RoundToInt((v.z - origin.z) / step) - sampleZ0, 1, sh - 2);
                float dfx = field[sx + 1, sz] - field[sx - 1, sz];
                float dfz = field[sx, sz + 1] - field[sx, sz - 1];
                Vector3 outward = new Vector3(-dfx, 0f, -dfz);
                if (outward.sqrMagnitude < 1e-6f)
                {
                    outward = new Vector3(v.x - center.x, 0f, v.z - center.z);
                    if (outward.sqrMagnitude < 1e-6f)
                        continue;
                }

                verts[i] = v + outward.normalized * amount;
            }
        }

        private static float HashNoise01(float x, float z, int seed)
        {
            unchecked
            {
                int xi = Mathf.FloorToInt(x * 11.37f);
                int zi = Mathf.FloorToInt(z * 13.91f);
                uint h = (uint)(seed * 374761393);
                h ^= (uint)xi * 668265263u;
                h ^= (uint)zi * 2246822519u;
                h ^= h >> 13;
                h *= 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFF) / 16777215f;
            }
        }

        private static void NudgeEdgeVertexAtIso(ref Vector3 p, float v0, float v1, float iso, float amplitudeWorld, float noiseScale, int seed, Vector3 p0, Vector3 p1)
        {
            if (amplitudeWorld <= 1e-6f) return;
            float mid = (v0 + v1) * 0.5f;
            if (Mathf.Abs(mid - iso) > 0.14f) return;
            Vector3 e = p1 - p0;
            e.y = 0f;
            float el = e.magnitude;
            if (el < 1e-5f) return;
            Vector3 perp = new Vector3(-e.z, 0f, e.x) / el;
            float n = HashNoise01(p.x * noiseScale, p.z * noiseScale, seed) - 0.5f;
            p += perp * (n * 2f) * amplitudeWorld;
        }

        private static int EstimateRiverThicknessCells(GridSystem grid, int gx, int gz)
        {
            if (!grid.InBoundsCell(gx, gz) || grid.GetCell(gx, gz).type != CellType.River)
                return 999;
            int l = 0;
            for (int x = gx - 1; x >= 0 && grid.GetCell(x, gz).type == CellType.River; x--) l++;
            int r = 0;
            for (int x = gx + 1; x < grid.Width && grid.GetCell(x, gz).type == CellType.River; x++) r++;
            int u = 0;
            for (int z = gz - 1; z >= 0 && grid.GetCell(gx, z).type == CellType.River; z--) u++;
            int d = 0;
            for (int z = gz + 1; z < grid.Height && grid.GetCell(gx, z).type == CellType.River; z++) d++;
            return Mathf.Min(l + r + 1, u + d + 1);
        }

        /// <summary>Ejes unitarios para buscar orillas opuestas (cada uno incluye también -eje).</summary>
        static readonly Vector2Int[] CrossingBankSearchAxes =
        {
            new Vector2Int(1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(1, 1),
            new Vector2Int(1, -1),
        };

        struct RiverSegment
        {
            public int segmentId;
            public List<Vector2Int> cells;
            public bool touchesLake;
            public bool touchesEdge;
            public bool hasJunction;
        }

        static long PackEdge(Vector2Int a, Vector2Int b)
        {
            long pa = ((long)(uint)a.x << 32) | (uint)a.y;
            long pb = ((long)(uint)b.x << 32) | (uint)b.y;
            if (pa > pb) { long t = pa; pa = pb; pb = t; }
            return (pa * 73856093L) ^ (pb * 19349663L);
        }

        static bool IsRiverAt(GridSystem grid, int x, int z)
        {
            return grid != null && grid.InBoundsCell(x, z) && grid.GetCell(x, z).type == CellType.River;
        }

        static bool TouchesWaterNeighbor(GridSystem grid, Vector2Int c)
        {
            foreach (var n in grid.Neighbors4(c))
                if (grid.GetCell(n.x, n.y).type == CellType.Water)
                    return true;
            return false;
        }

        static bool IsEdgeCell(GridSystem grid, Vector2Int c)
        {
            return c.x <= 0 || c.y <= 0 || c.x >= grid.Width - 1 || c.y >= grid.Height - 1;
        }

        static int CountRiverConnectedComponents(GridSystem grid)
        {
            if (grid == null) return 0;
            int w = grid.Width;
            int h = grid.Height;
            var seen = new bool[w, h];
            var q = new Queue<Vector2Int>();
            int comps = 0;

            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (seen[x, z] || !IsRiverAt(grid, x, z))
                        continue;
                    comps++;
                    seen[x, z] = true;
                    q.Clear();
                    q.Enqueue(new Vector2Int(x, z));
                    while (q.Count > 0)
                    {
                        var p = q.Dequeue();
                        foreach (var n in grid.Neighbors4(p))
                        {
                            if (seen[n.x, n.y] || !IsRiverAt(grid, n.x, n.y))
                                continue;
                            seen[n.x, n.y] = true;
                            q.Enqueue(n);
                        }
                    }
                }
            }

            return comps;
        }

        static List<RiverSegment> BuildRiverSegments(
            GridSystem grid,
            MapGenConfig config,
            out HashSet<Vector2Int> junctionNodes,
            out HashSet<Vector2Int> endpointNodes)
        {
            junctionNodes = new HashSet<Vector2Int>();
            endpointNodes = new HashSet<Vector2Int>();
            var segments = new List<RiverSegment>(32);
            if (grid == null)
                return segments;

            var lines = grid.RiverCenterlinesCellSpace;
            if (lines == null || lines.Count == 0)
            {
                // Fallback: sin centerlines, degradar a un segmento por componente conectado.
                int w = grid.Width;
                int h = grid.Height;
                var seen = new bool[w, h];
                var q = new Queue<Vector2Int>();
                for (int z = 0; z < h; z++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (seen[x, z] || !IsRiverAt(grid, x, z))
                            continue;
                        var path = new List<Vector2Int>();
                        bool touchesLake = false;
                        bool touchesEdge = false;
                        q.Clear();
                        q.Enqueue(new Vector2Int(x, z));
                        seen[x, z] = true;
                        while (q.Count > 0)
                        {
                            var c = q.Dequeue();
                            path.Add(c);
                            touchesLake |= TouchesWaterNeighbor(grid, c);
                            touchesEdge |= IsEdgeCell(grid, c);
                            foreach (var n in grid.Neighbors4(c))
                            {
                                if (seen[n.x, n.y] || !IsRiverAt(grid, n.x, n.y))
                                    continue;
                                seen[n.x, n.y] = true;
                                q.Enqueue(n);
                            }
                        }
                        if (path.Count >= 2)
                        {
                            segments.Add(new RiverSegment
                            {
                                segmentId = segments.Count,
                                cells = path,
                                touchesLake = touchesLake,
                                touchesEdge = touchesEdge,
                                hasJunction = false
                            });
                        }
                    }
                }
                return segments;
            }

            var cellOwners = new Dictionary<Vector2Int, List<int>>(256);
            for (int li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                if (line == null || line.Count == 0)
                    continue;
                for (int i = 0; i < line.Count; i++)
                {
                    Vector2 p = line[i];
                    var c = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
                    if (!grid.InBoundsCell(c))
                        continue;
                    if (!cellOwners.TryGetValue(c, out var list))
                    {
                        list = new List<int>(4);
                        cellOwners[c] = list;
                    }
                    if (!list.Contains(li))
                        list.Add(li);
                }
            }

            foreach (var kv in cellOwners)
            {
                if (kv.Value != null && kv.Value.Count >= 2)
                    junctionNodes.Add(kv.Key);
            }

            int maxSegLen = config != null ? Mathf.Clamp(config.riverFordMaxSegmentLengthCells, 8, 512) : 80;
            int minSubLen = config != null ? Mathf.Clamp(config.riverFordMinSubSegmentLengthCells, 2, 256) : 20;
            // Corte por lago solo si el contacto es persistente en la centerline (evita 1 celda aislada).
            const int LakeCutMinRun = 3;

            for (int li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                if (line == null || line.Count < 2)
                    continue;

                var cells = new List<Vector2Int>(line.Count);
                Vector2Int last = new Vector2Int(int.MinValue, int.MinValue);
                for (int i = 0; i < line.Count; i++)
                {
                    Vector2 p = line[i];
                    var c = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
                    if (!grid.InBoundsCell(c))
                        continue;
                    if (c == last)
                        continue;
                    cells.Add(c);
                    last = c;
                }

                if (cells.Count < 2)
                    continue;

                // Subsegmentación: cortes por junction/lago/borde, cambio de dirección fuerte, y longitud máxima.
                // IMPORTANTE: NO cortamos por ángulo local (zigzag de grilla crea falsos cortes).

                var cut = new bool[cells.Count];
                // Cortes en extremos siempre.
                cut[0] = true;
                cut[cells.Count - 1] = true;

                for (int i = 0; i < cells.Count; i++)
                {
                    if (junctionNodes.Contains(cells[i]))
                        cut[i] = true;
                }

                // Corte por borde de mapa (además del edgePad de candidatos).
                for (int i = 0; i < cells.Count; i++)
                {
                    if (IsEdgeCell(grid, cells[i]))
                        cut[i] = true;
                }

                // Corte por lago persistente: si hay un run de >=LakeCutMinRun celdas tocando Water,
                // cortamos al inicio del run y al final (para aislar la zona cercana al lago).
                int runStart = -1;
                int runLen = 0;
                for (int i = 0; i < cells.Count; i++)
                {
                    bool t = TouchesWaterNeighbor(grid, cells[i]);
                    if (t)
                    {
                        if (runLen == 0) runStart = i;
                        runLen++;
                    }
                    else
                    {
                        if (runLen >= LakeCutMinRun && runStart >= 0)
                        {
                            cut[runStart] = true;
                            cut[i - 1] = true;
                        }
                        runStart = -1;
                        runLen = 0;
                    }
                }
                if (runLen >= LakeCutMinRun && runStart >= 0)
                {
                    cut[runStart] = true;
                    cut[cells.Count - 1] = true;
                }

                int currentLen = 1;
                for (int i = 1; i < cells.Count; i++)
                {
                    currentLen++;
                    if (currentLen >= maxSegLen)
                    {
                        cut[i] = true;
                        currentLen = 1;
                    }
                }

                int start = 0;
                for (int i = 1; i < cells.Count; i++)
                {
                    if (!cut[i])
                        continue;
                    int end = i;
                    int len = end - start + 1;
                    if (len >= minSubLen)
                    {
                        var sub = new List<Vector2Int>(len);
                        bool touchesLake = false;
                        bool touchesEdge = false;
                        bool hasJunction = false;
                        for (int k = start; k <= end; k++)
                        {
                            var c = cells[k];
                            sub.Add(c);
                            touchesLake |= TouchesWaterNeighbor(grid, c);
                            touchesEdge |= IsEdgeCell(grid, c);
                            if (junctionNodes.Contains(c))
                                hasJunction = true;
                        }

                        endpointNodes.Add(sub[0]);
                        endpointNodes.Add(sub[sub.Count - 1]);
                        segments.Add(new RiverSegment
                        {
                            segmentId = segments.Count,
                            cells = sub,
                            touchesLake = touchesLake,
                            touchesEdge = touchesEdge,
                            hasJunction = hasJunction
                        });
                    }
                    start = i;
                }
            }

            return segments;
        }

        /// <summary>
        /// Intenta marcar un corredor <c>riverFord</c> continuo entre dos orillas Land walkables.
        /// Prueba varios ejes (cardinales y diagonales); restringe a la componente 4-vecinos que contiene el centro
        /// y exige contacto con tierra a ambos lados del eje transversal.
        /// </summary>
        public static bool TryApplyBankToBankFordCorridor(
            GridSystem grid,
            MapGenConfig config,
            Vector2Int center,
            HashSet<Vector2Int> globalMarked,
            Vector2Int? preferredTransverseDirPos,
            out string rejectReason)
        {
            return TryApplyBankToBankFordCorridor(
                grid,
                config,
                center,
                globalMarked,
                preferredTransverseDirPos,
                out rejectReason,
                out _,
                out _,
                true);
        }

        public static bool TryApplyBankToBankFordCorridor(
            GridSystem grid,
            MapGenConfig config,
            Vector2Int center,
            HashSet<Vector2Int> globalMarked,
            Vector2Int? preferredTransverseDirPos,
            out string rejectReason,
            out Vector2Int rejectAxis,
            bool applyChanges)
        {
            return TryApplyBankToBankFordCorridor(
                grid,
                config,
                center,
                globalMarked,
                preferredTransverseDirPos,
                out rejectReason,
                out rejectAxis,
                out _,
                applyChanges);
        }

        public static bool TryApplyBankToBankFordCorridor(
            GridSystem grid,
            MapGenConfig config,
            Vector2Int center,
            HashSet<Vector2Int> globalMarked,
            Vector2Int? preferredTransverseDirPos,
            out string rejectReason,
            out Vector2Int rejectAxis,
            out int corridorLength,
            bool applyChanges)
        {
            rejectReason = null;
            rejectAxis = Vector2Int.zero;
            corridorLength = 0;
            if (grid == null || config == null)
            {
                rejectReason = "null_grid_config";
                return false;
            }

            if (!grid.InBoundsCell(center.x, center.y))
            {
                rejectReason = "oob_center";
                return false;
            }

            if (grid.GetCell(center.x, center.y).type != CellType.River)
            {
                rejectReason = "not_river";
                return false;
            }

            int bankSearch = Mathf.Clamp(config.riverCrossingBankSearchCells, 4, 20);
            int halfWidth = Mathf.Max(1, config.riverCrossingFunctionalHalfWidthCells);
            bool dbg = config.riverCrossingCorridorDebugLogs;

            bool IsRiverCell(int x, int z) => grid.InBoundsCell(x, z) && grid.GetCell(x, z).type == CellType.River;
            bool IsValidBank(int x, int z)
            {
                if (!grid.InBoundsCell(x, z))
                    return false;
                var c = grid.GetCell(x, z);
                if (c.type == CellType.Water || c.type == CellType.River || c.riverFord)
                    return false;
                return c.walkable;
            }

            var axisTryOrder = new List<Vector2Int>(8);
            void AddAxisUnique(Vector2Int ax)
            {
                if (ax.x == 0 && ax.y == 0)
                    return;
                for (int i = 0; i < axisTryOrder.Count; i++)
                {
                    if (axisTryOrder[i].x == ax.x && axisTryOrder[i].y == ax.y)
                        return;
                }

                axisTryOrder.Add(ax);
            }

            if (preferredTransverseDirPos.HasValue)
                AddAxisUnique(preferredTransverseDirPos.Value);
            for (int i = 0; i < CrossingBankSearchAxes.Length; i++)
                AddAxisUnique(CrossingBankSearchAxes[i]);

            bool TryPaintForAxis(Vector2Int dirPos, out string axisRej, out int paintedCount, out bool hasLandA, out bool hasLandB)
            {
                axisRej = null;
                paintedCount = 0;
                hasLandA = false;
                hasLandB = false;
                var dirNeg = new Vector2Int(-dirPos.x, -dirPos.y);

                var lineCells = new List<Vector2Int> { center };
                bool posBankFound = false;
                bool negBankFound = false;

                for (int s = 1; s <= bankSearch; s++)
                {
                    int x = center.x + dirPos.x * s;
                    int z = center.y + dirPos.y * s;
                    if (!grid.InBoundsCell(x, z))
                        break;
                    if (IsRiverCell(x, z))
                    {
                        lineCells.Add(new Vector2Int(x, z));
                        continue;
                    }

                    posBankFound = IsValidBank(x, z);
                    break;
                }

                for (int s = 1; s <= bankSearch; s++)
                {
                    int x = center.x + dirNeg.x * s;
                    int z = center.y + dirNeg.y * s;
                    if (!grid.InBoundsCell(x, z))
                        break;
                    if (IsRiverCell(x, z))
                    {
                        lineCells.Add(new Vector2Int(x, z));
                        continue;
                    }

                    negBankFound = IsValidBank(x, z);
                    break;
                }

                if (!posBankFound || !negBankFound)
                {
                    axisRej = "no_opposite_banks";
                    return false;
                }

                var dirTan = new Vector2Int(-dirPos.y, dirPos.x);
                var toPaint = new HashSet<Vector2Int>();
                var spineUnique = new HashSet<Vector2Int>();
                for (int i = 0; i < lineCells.Count; i++)
                {
                    spineUnique.Add(lineCells[i]);
                }

                foreach (var lc in spineUnique)
                {
                    for (int o = -halfWidth; o <= halfWidth; o++)
                    {
                        int x = lc.x + dirTan.x * o;
                        int z = lc.y + dirTan.y * o;
                        if (!IsRiverCell(x, z))
                            continue;
                        toPaint.Add(new Vector2Int(x, z));
                    }
                }

                if (toPaint.Count == 0)
                {
                    axisRej = "empty_corridor";
                    return false;
                }

                var reached = new HashSet<Vector2Int>();
                var qq = new Queue<Vector2Int>();
                if (!toPaint.Contains(center))
                {
                    axisRej = "center_not_in_slab";
                    return false;
                }

                qq.Enqueue(center);
                reached.Add(center);
                while (qq.Count > 0)
                {
                    var p = qq.Dequeue();
                    foreach (var n in grid.Neighbors4(p.x, p.y))
                    {
                        if (!toPaint.Contains(n) || reached.Contains(n))
                            continue;
                        reached.Add(n);
                        qq.Enqueue(n);
                    }
                }

                if (reached.Count < toPaint.Count)
                    toPaint = reached;

                int minFordCells = Mathf.Clamp(config.riverFordMinWalkableBlobCells, 2, 48);
                if (toPaint.Count < minFordCells)
                {
                    axisRej = "corridor_too_few_cells";
                    return false;
                }

                foreach (var fc in toPaint)
                {
                    foreach (var n in grid.Neighbors4(fc.x, fc.y))
                    {
                        if (toPaint.Contains(n))
                            continue;
                        if (!IsValidBank(n.x, n.y))
                            continue;
                        int vx = n.x - center.x;
                        int vy = n.y - center.y;
                        int dot = vx * dirPos.x + vy * dirPos.y;
                        if (dot > 0)
                            hasLandA = true;
                        else if (dot < 0)
                            hasLandB = true;
                        else
                        {
                            int dotT = vx * dirTan.x + vy * dirTan.y;
                            if (dotT >= 0)
                                hasLandA = true;
                            else
                                hasLandB = true;
                        }
                    }
                }

                if (!hasLandA || !hasLandB)
                {
                    axisRej = "land_not_both_sides";
                    return false;
                }

                if (applyChanges)
                {
                    foreach (var cc in toPaint)
                    {
                        ref var cell = ref grid.GetCell(cc.x, cc.y);
                        cell.riverFord = true;
                        cell.walkable = true;
                        cell.buildable = false;
                        cell.waterTraverse = WaterTraverseMode.FordShallow;
                        globalMarked?.Add(cc);
                    }
                }

                paintedCount = toPaint.Count;
                return true;
            }

            for (int ai = 0; ai < axisTryOrder.Count; ai++)
            {
                Vector2Int dirPos = axisTryOrder[ai];
                rejectAxis = dirPos;
                if (TryPaintForAxis(dirPos, out string axisRej, out int len, out bool ha, out bool hb))
                {
                    corridorLength = len;
                    if (dbg)
                        Debug.Log(
                            $"[RiverFordCorridor] center={center} axis={dirPos} len={len} hasLandA={ha} hasLandB={hb}");
                    return true;
                }

                rejectReason = axisRej ?? "axis_failed";
            }

            if (string.IsNullOrEmpty(rejectReason))
                rejectReason = "all_axes_failed";
            if (dbg)
                Debug.Log($"[RiverFordCorridor] REJECT center={center} reason={rejectReason}");
            return false;
        }

        private static void ApplyFunctionalFordsFromCrossingCells(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2Int> pickedCells,
            Transform parent,
            float waterY,
            float cellSize)
        {
            if (grid == null || config == null || pickedCells == null || pickedCells.Count == 0)
                return;
            if (!config.enableFunctionalRiverFords || !config.useCrossingAssetFords)
                return;

            int halfWidth = Mathf.Max(1, config.riverCrossingFunctionalHalfWidthCells);
            int bankSearch = Mathf.Clamp(config.riverCrossingBankSearchCells, 4, 20);
            var marked = new HashSet<Vector2Int>();
            var failedCenters = new List<Vector2Int>(pickedCells.Count);
            int createdCorridors = 0;

            bool IsRiverCell(int x, int z) => grid.InBoundsCell(x, z) && grid.GetCell(x, z).type == CellType.River;

            for (int i = 0; i < pickedCells.Count; i++)
            {
                var p = pickedCells[i];
                int thX = 1;
                for (int x = p.x - 1; x >= 0 && IsRiverCell(x, p.y); x--) thX++;
                for (int x = p.x + 1; x < grid.Width && IsRiverCell(x, p.y); x++) thX++;
                int thZ = 1;
                for (int z = p.y - 1; z >= 0 && IsRiverCell(p.x, z); z--) thZ++;
                for (int z = p.y + 1; z < grid.Height && IsRiverCell(p.x, z); z++) thZ++;

                Vector2Int preferred = thX <= thZ ? Vector2Int.right : Vector2Int.up;
                if (TryApplyBankToBankFordCorridor(grid, config, p, marked, preferred, out _))
                    createdCorridors++;
                else
                    failedCenters.Add(p);
            }

            if (config.riverCrossingDebugVisuals)
                BuildCrossingDebugCubes(parent, marked, failedCenters, grid, waterY, cellSize);

            if (config != null && config.riverCrossingCorridorDebugLogs)
            {
                if (marked.Count > 0)
                    Debug.Log($"[RiverFord] source=crossing-assets created={createdCorridors} cells={marked.Count} radius={halfWidth}");
                Debug.Log($"[RiverFordCorridor] created={createdCorridors} cells={marked.Count} failed={failedCenters.Count} width={halfWidth} bankSearch={bankSearch}");
            }
        }

        private static void BuildCrossingDebugCubes(
            Transform parent,
            HashSet<Vector2Int> markedFordCells,
            List<Vector2Int> failedCenters,
            GridSystem grid,
            float waterY,
            float cellSize)
        {
            DebugRiverFordFootprintCentersWorld.Clear();
            DebugRiverFordFootprintSizesWorld.Clear();
            DebugRiverFordFailedCenterPositionsWorld.Clear();

            if (grid == null)
                return;

            // Limpia meshes legacy de builds anteriores (sin crear geometría nueva).
            if (parent != null)
            {
                for (int i = parent.childCount - 1; i >= 0; i--)
                {
                    var ch = parent.GetChild(i);
                    if (ch != null && ch.name.StartsWith("DEBUG_RiverFord"))
                    {
                        if (Application.isPlaying) Object.Destroy(ch.gameObject);
                        else Object.DestroyImmediate(ch.gameObject);
                    }
                }
            }

            if (markedFordCells == null || markedFordCells.Count == 0)
            {
                if (failedCenters != null)
                {
                    for (int fi = 0; fi < failedCenters.Count; fi++)
                    {
                        var fc = failedCenters[fi];
                        Vector3 w = grid.CellToWorldCenter(fc);
                        DebugRiverFordFailedCenterPositionsWorld.Add(new Vector3(w.x, waterY, w.z));
                    }
                }

                return;
            }

            float thinY = Mathf.Max(0.08f, cellSize * 0.14f);
            var remaining = new HashSet<Vector2Int>(markedFordCells);
            var q = new Queue<Vector2Int>();

            while (remaining.Count > 0)
            {
                Vector2Int start = default;
                foreach (var p in remaining)
                {
                    start = p;
                    break;
                }

                var blob = new HashSet<Vector2Int>();
                q.Clear();
                q.Enqueue(start);
                while (q.Count > 0)
                {
                    var c = q.Dequeue();
                    if (!remaining.Contains(c))
                        continue;
                    remaining.Remove(c);
                    blob.Add(c);
                    foreach (var n in grid.Neighbors4(c))
                    {
                        if (remaining.Contains(n))
                            q.Enqueue(n);
                    }
                }

                int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
                foreach (var c in blob)
                {
                    minX = Mathf.Min(minX, c.x);
                    maxX = Mathf.Max(maxX, c.x);
                    minZ = Mathf.Min(minZ, c.y);
                    maxZ = Mathf.Max(maxZ, c.y);
                }

                float width = (maxX - minX + 1) * cellSize * 0.96f;
                float depth = (maxZ - minZ + 1) * cellSize * 0.96f;
                float cx = grid.Origin.x + (minX + maxX + 1) * 0.5f * cellSize;
                float cz = grid.Origin.z + (minZ + maxZ + 1) * 0.5f * cellSize;

                DebugRiverFordFootprintCentersWorld.Add(new Vector3(cx, waterY + 0.55f, cz));
                DebugRiverFordFootprintSizesWorld.Add(new Vector3(width, thinY, depth));
            }

            if (failedCenters != null)
            {
                for (int fi = 0; fi < failedCenters.Count; fi++)
                {
                    var fc = failedCenters[fi];
                    Vector3 w = grid.CellToWorldCenter(fc);
                    DebugRiverFordFailedCenterPositionsWorld.Add(new Vector3(w.x, waterY, w.z));
                }
            }
        }

        private static List<GameObject> CollectCrossingDecorationPrefabs(MapGenConfig config)
        {
            var list = new List<GameObject>(8);
            if (config == null)
                return list;

            if (config.waterCrossingDecorationPrefabs != null)
            {
                for (int i = 0; i < config.waterCrossingDecorationPrefabs.Length; i++)
                {
                    var p = config.waterCrossingDecorationPrefabs[i];
                    if (p != null) list.Add(p);
                }
            }

            if (list.Count == 0 && config.waterCrossingDecorationPrefab != null)
                list.Add(config.waterCrossingDecorationPrefab);

            if (list.Count == 0)
            {
                var stone = Resources.Load<GameObject>("Stone/PF_Stone");
                if (stone != null)
                    list.Add(stone);
            }

            return list;
        }

        /// <summary>
        /// Revoca riverFord en componentes conexos demasiado pequeños (fantasmas de 1–2 celdas que permitían cruzar).
        /// </summary>
        private static void StripRiverFordBlobsSmallerThan(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null)
                return;
            int minCells = Mathf.Clamp(config.riverFordMinWalkableBlobCells, 2, 48);

            var unassigned = new HashSet<Vector2Int>();
            for (int z = 0; z < grid.Height; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    ref var cd = ref grid.GetCell(x, z);
                    if (cd.type == CellType.River && cd.riverFord)
                        unassigned.Add(new Vector2Int(x, z));
                }
            }

            if (unassigned.Count == 0)
                return;

            var q = new Queue<Vector2Int>();
            var blob = new List<Vector2Int>(32);

            while (unassigned.Count > 0)
            {
                Vector2Int seedCell = default;
                foreach (var p in unassigned)
                {
                    seedCell = p;
                    break;
                }

                blob.Clear();
                q.Clear();
                q.Enqueue(seedCell);
                unassigned.Remove(seedCell);

                while (q.Count > 0)
                {
                    var c = q.Dequeue();
                    blob.Add(c);
                    foreach (var n in grid.Neighbors4(c))
                    {
                        if (!unassigned.Contains(n))
                            continue;
                        ref var cd = ref grid.GetCell(n.x, n.y);
                        if (cd.type != CellType.River || !cd.riverFord)
                            continue;
                        unassigned.Remove(n);
                        q.Enqueue(n);
                    }
                }

                if (blob.Count >= minCells)
                    continue;

                for (int i = 0; i < blob.Count; i++)
                {
                    var c = blob[i];
                    ref var cell = ref grid.GetCell(c.x, c.y);
                    cell.riverFord = false;
                    cell.walkable = false;
                    cell.buildable = false;
                    cell.waterTraverse = WaterTraverseMode.SwimNavigable;
                }
            }
        }

        /// <summary>Hash determinista para dispersión de props de vado (sin System.Random).</summary>
        private static uint FordDecorHash(int seed, int blobKey, int index)
        {
            unchecked
            {
                uint x = (uint)seed;
                x ^= (uint)blobKey * 2246822519u;
                x ^= (uint)index * 3266489917u;
                x ^= x >> 16;
                x *= 2654435761u;
                x ^= x >> 13;
                return x;
            }
        }

        /// <summary>
        /// Rellena matrices de decoración por cada mancha conexa de celdas River+riverFord en el grid.
        /// Varias piedras por vado, aleatorias pero deterministas por seed, dentro del rectángulo que envuelve la mancha.
        /// </summary>
        private static void ScatterRiverFordDecorationFromGrid(
            GridSystem grid,
            MapGenConfig config,
            float waterY,
            float cellSize,
            List<GameObject> crossingPrefabs,
            Dictionary<GameObject, List<Matrix4x4>> crossingMatsByPrefab)
        {
            if (grid == null || config == null)
                return;

            bool hasPrefabs = crossingPrefabs != null && crossingPrefabs.Count > 0;

            bool skipDecorOutsideVisualMask = config.uwpOwnedVisualPolicy &&
                                              grid.RiverVisualSurfaceCacheFrozen &&
                                              grid.RiverVisualSurfaceMask != null &&
                                              grid.RiverVisualSurfaceMask.GetLength(0) == grid.Width &&
                                              grid.RiverVisualSurfaceMask.GetLength(1) == grid.Height;
            bool[,] rivMask = skipDecorOutsideVisualMask ? grid.RiverVisualSurfaceMask : null;
            int skippedDecorOutsideMask = 0;

            var unassigned = new HashSet<Vector2Int>();
            for (int z = 0; z < grid.Height; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    ref var cd = ref grid.GetCell(x, z);
                    if (cd.type != CellType.River || !cd.riverFord)
                        continue;
                    if (rivMask != null && !rivMask[x, z])
                    {
                        skippedDecorOutsideMask++;
                        continue;
                    }
                    unassigned.Add(new Vector2Int(x, z));
                }
            }

            if (skippedDecorOutsideMask > 0)
            {
                Debug.Log(
                    $"[UWP_SKIP_FORD_DECOR_OUTSIDE_VISUAL_MASK] cells={skippedDecorOutsideMask}");
            }

            if (unassigned.Count == 0)
                return;

            float yOff = Mathf.Max(0f, config.riverCrossingDecorYOffset);
            var q = new Queue<Vector2Int>();
            var blob = new List<Vector2Int>(64);

            while (unassigned.Count > 0)
            {
                Vector2Int seedCell = default;
                foreach (var p in unassigned)
                {
                    seedCell = p;
                    break;
                }

                blob.Clear();
                q.Clear();
                q.Enqueue(seedCell);
                unassigned.Remove(seedCell);

                while (q.Count > 0)
                {
                    var c = q.Dequeue();
                    blob.Add(c);
                    foreach (var n in grid.Neighbors4(c))
                    {
                        if (!unassigned.Contains(n))
                            continue;
                        ref var cd = ref grid.GetCell(n.x, n.y);
                        if (cd.type != CellType.River || !cd.riverFord)
                            continue;
                        unassigned.Remove(n);
                        q.Enqueue(n);
                    }
                }

                int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
                float cxSum = 0f, czSum = 0f;
                foreach (var c in blob)
                {
                    minX = Mathf.Min(minX, c.x);
                    maxX = Mathf.Max(maxX, c.x);
                    minZ = Mathf.Min(minZ, c.y);
                    maxZ = Mathf.Max(maxZ, c.y);
                    cxSum += c.x + 0.5f;
                    czSum += c.y + 0.5f;
                }

                int gw = maxX - minX + 1;
                int gh = maxZ - minZ + 1;
                int bboxAreaCells = gw * gh;

                int stoneCount;
                if (blob.Count <= 1)
                    stoneCount = 1;
                else if (blob.Count <= 3)
                    stoneCount = 2;
                else
                {
                    // Mezcla blob + caja envolvente: reparte mejor en vados anchos/largos (un solo tipo de prefab también se ve lleno).
                    float est = blob.Count * 0.38f + bboxAreaCells * 0.17f;
                    stoneCount = Mathf.RoundToInt(est);
                    stoneCount = Mathf.Clamp(stoneCount, 4, 56);
                    stoneCount = Mathf.Max(stoneCount, Mathf.Min(blob.Count, 8));
                }

                Vector3 centroid = new Vector3(
                    grid.Origin.x + (cxSum / blob.Count) * cellSize,
                    waterY + yOff,
                    grid.Origin.z + (czSum / blob.Count) * cellSize);
                DebugWaterCrossingPositionsWorld.Add(centroid);

                if (!hasPrefabs)
                    continue;

                float margin = cellSize * 0.06f;
                float wx0 = grid.Origin.x + minX * cellSize + margin;
                float wx1 = grid.Origin.x + (maxX + 1) * cellSize - margin;
                float wz0 = grid.Origin.z + minZ * cellSize + margin;
                float wz1 = grid.Origin.z + (maxZ + 1) * cellSize - margin;
                if (wx1 <= wx0)
                {
                    float mid = (wx0 + wx1) * 0.5f;
                    wx0 = mid - cellSize * 0.25f;
                    wx1 = mid + cellSize * 0.25f;
                }

                if (wz1 <= wz0)
                {
                    float mid = (wz0 + wz1) * 0.5f;
                    wz0 = mid - cellSize * 0.25f;
                    wz1 = mid + cellSize * 0.25f;
                }

                int blobKey = blob[0].x ^ (blob[0].y << 16) ^ (config.seed * 31);

                // Rejilla estratificada + jitter: cubre todo el rect sin amontonar en el centro.
                int slots = stoneCount;
                int cols = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(slots * (float)gw / Mathf.Max(1, gh))));
                int rows = Mathf.Max(1, Mathf.CeilToInt(slots / (float)cols));
                while (cols * rows < slots)
                    cols++;

                int prefabCount = Mathf.Max(1, crossingPrefabs.Count);
                for (int i = 0; i < slots; i++)
                {
                    int ix = i % cols;
                    int iz = i / cols;
                    float u0 = ix / (float)cols;
                    float u1 = (ix + 1f) / cols;
                    float v0 = iz / (float)rows;
                    float v1 = (iz + 1f) / rows;

                    uint h = FordDecorHash(config.seed, blobKey, i ^ (ix * 31337) ^ (iz * 7919));
                    float ju = ((h & 1023) / 1024f) * 0.88f + 0.06f;
                    float jv = (((h >> 10) & 1023) / 1024f) * 0.88f + 0.06f;
                    float u = Mathf.Lerp(u0, u1, ju);
                    float v = Mathf.Lerp(v0, v1, jv);
                    float px = Mathf.Lerp(wx0, wx1, u);
                    float pz = Mathf.Lerp(wz0, wz1, v);
                    float yaw = ((h >> 8) & 255) / 255f * 360f;
                    float sc = 0.82f + ((h >> 24) & 63) / 63f * 0.35f;
                    int prefabIndex = (int)(h % (uint)prefabCount);
                    var prefab = crossingPrefabs[prefabIndex];
                    if (prefab == null)
                        continue;
                    Vector3 pos = new Vector3(px, waterY + yOff, pz);
                    if (!crossingMatsByPrefab.TryGetValue(prefab, out var mats))
                    {
                        mats = new List<Matrix4x4>();
                        crossingMatsByPrefab[prefab] = mats;
                    }

                    mats.Add(Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), Vector3.one * sc));
                }
            }
        }

        private static bool IsValidCrossingBankCell(GridSystem grid, int x, int z)
        {
            if (!grid.InBoundsCell(x, z))
                return false;
            var c = grid.GetCell(x, z);
            if (c.type == CellType.Water || c.type == CellType.River || c.riverFord)
                return false;
            return c.walkable;
        }

        private static bool HasOppositeBanksAlongAxis(GridSystem grid, Vector2Int center, Vector2Int axis, int bankSearch)
        {
            bool posBank = false;
            bool negBank = false;
            int posRiverLen = 0;
            int negRiverLen = 0;

            for (int s = 1; s <= bankSearch; s++)
            {
                int x = center.x + axis.x * s;
                int z = center.y + axis.y * s;
                if (!grid.InBoundsCell(x, z))
                    break;
                if (grid.GetCell(x, z).type == CellType.River)
                {
                    posRiverLen++;
                    continue;
                }
                posBank = IsValidCrossingBankCell(grid, x, z);
                break;
            }

            for (int s = 1; s <= bankSearch; s++)
            {
                int x = center.x - axis.x * s;
                int z = center.y - axis.y * s;
                if (!grid.InBoundsCell(x, z))
                    break;
                if (grid.GetCell(x, z).type == CellType.River)
                {
                    negRiverLen++;
                    continue;
                }
                negBank = IsValidCrossingBankCell(grid, x, z);
                break;
            }

            // Exigir que exista cauce a ambos lados y banco válido en ambos extremos.
            return posBank && negBank && posRiverLen > 0 && negRiverLen > 0;
        }

        /// <summary>True si la celda River puede alojar un cruce (orillas opuestas en algún eje cardinal o diagonal).</summary>
        public static bool IsCrossingCandidatePlacementValid(GridSystem grid, Vector2Int c, int bankSearch)
        {
            if (!grid.InBoundsCell(c.x, c.y) || grid.GetCell(c.x, c.y).type != CellType.River)
                return false;

            // Evita instalar cruces pegados al borde del mapa.
            int edgePad = Mathf.Max(2, bankSearch / 2);
            if (c.x < edgePad || c.y < edgePad || c.x >= grid.Width - edgePad || c.y >= grid.Height - edgePad)
                return false;

            for (int i = 0; i < CrossingBankSearchAxes.Length; i++)
            {
                if (HasOppositeBanksAlongAxis(grid, c, CrossingBankSearchAxes[i], bankSearch))
                    return true;
            }

            return false;
        }

        /// <summary>Vados donde rutas lógicas atraviesan <see cref="CellType.River"/>. La verdad final es <see cref="TryApplyBankToBankFordCorridor"/>.</summary>
        private static void BuildStrategicFordCandidatesFromPaths(
            GridSystem grid,
            MapGenConfig config,
            IReadOnlyList<List<Vector2Int>> strategicPaths,
            HashSet<Vector2Int> marked,
            List<Vector2Int> createdCenters,
            List<Vector2Int> failedCenters,
            int spacing,
            int bankSearchForSelection,
            int maxThicknessCells,
            int minDistConfluence,
            ICollection<Vector2Int> junctionNodes,
            int maxStrategic,
            bool dbgFord,
            ref int strategicCreated,
            ref int roadFordRejected,
            ref int riverCrossingRunsAccumulated,
            ref int retrySaved)
        {
            if (strategicPaths == null || strategicPaths.Count == 0 || maxStrategic <= 0)
                return;

            bool FarEnough(Vector2Int c)
            {
                foreach (var p in createdCenters)
                {
                    if (Mathf.Max(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) < spacing)
                        return false;
                }

                return true;
            }

            int DistanceToNearestJunction(Vector2Int c)
            {
                if (junctionNodes == null || junctionNodes.Count == 0)
                    return int.MaxValue;
                int best = int.MaxValue;
                foreach (var j in junctionNodes)
                {
                    int d = Mathf.Max(Mathf.Abs(c.x - j.x), Mathf.Abs(c.y - j.y));
                    if (d < best) best = d;
                }
                return best;
            }

            bool TouchesLake8(Vector2Int c)
            {
                foreach (var n in grid.Neighbors8(c))
                {
                    if (grid.GetCell(n).type == CellType.Water)
                        return true;
                }
                return false;
            }

            int pathId = 0;
            foreach (var path in strategicPaths)
            {
                if (strategicCreated >= maxStrategic)
                    break;
                if (path == null || path.Count == 0)
                {
                    pathId++;
                    continue;
                }

                for (int pi = 0; pi < path.Count; pi++)
                {
                    if (strategicCreated >= maxStrategic)
                        break;
                    var pc = path[pi];
                    if (!grid.InBoundsCell(pc) || grid.GetCell(pc).type != CellType.River)
                        continue;

                    int pj = pi;
                    while (pj < path.Count)
                    {
                        var pc2 = path[pj];
                        if (!grid.InBoundsCell(pc2) || grid.GetCell(pc2).type != CellType.River)
                            break;
                        pj++;
                    }

                    riverCrossingRunsAccumulated++;
                    int runLen = pj - pi;
                    int mid = pi + runLen / 2;
                    int crossingId = riverCrossingRunsAccumulated;
                    pi = pj - 1;

                    // En vez de probar SOLO el centro, evaluar una ventana de candidatos del mismo tramo River.
                    // Esto mejora casos donde el centro cae en confluencia/curva/ancho y hay un punto cercano viable.
                    var candidates = new List<Vector2Int>(10);

                    // indices absolutos en path dentro del run [runStart, runEnd)
                    int runStart = pj - runLen;
                    int runEnd = pj;
                    void AddIdx(int idx)
                    {
                        if (idx < runStart || idx >= runEnd) return;
                        var c = path[idx];
                        for (int k = 0; k < candidates.Count; k++)
                            if (candidates[k] == c) return;
                        candidates.Add(c);
                    }

                    AddIdx(mid);
                    AddIdx(mid - 2);
                    AddIdx(mid + 2);
                    AddIdx(mid - 4);
                    AddIdx(mid + 4);
                    if (runLen >= 12)
                    {
                        AddIdx(runStart + runLen / 4);
                        AddIdx(runStart + (3 * runLen) / 4);
                    }

                    // Scoring (mejor primero):
                    // - no pegado a lago
                    // - más lejos de confluencia
                    // - río más delgado (menos grosor)
                    // - spacing OK
                    candidates.Sort((a, b) =>
                    {
                        bool al = TouchesLake8(a);
                        bool bl = TouchesLake8(b);
                        int cmp = al.CompareTo(bl); // false(0) antes que true(1)
                        if (cmp != 0) return cmp;
                        int ad = DistanceToNearestJunction(a);
                        int bd = DistanceToNearestJunction(b);
                        cmp = bd.CompareTo(ad); // más lejos primero
                        if (cmp != 0) return cmp;
                        int ath = EstimateRiverThicknessCells(grid, a.x, a.y);
                        int bth = EstimateRiverThicknessCells(grid, b.x, b.y);
                        cmp = ath.CompareTo(bth); // más delgado primero
                        if (cmp != 0) return cmp;
                        bool af = FarEnough(a);
                        bool bf = FarEnough(b);
                        cmp = bf.CompareTo(af); // true primero
                        if (cmp != 0) return cmp;
                        // desempate determinista
                        cmp = a.x.CompareTo(b.x);
                        if (cmp != 0) return cmp;
                        return a.y.CompareTo(b.y);
                    });

                    int attempts = 0;
                    bool success = false;
                    string bestReason = null;
                    Vector2Int bestCenter = default;
                    Vector2Int bestAxis = Vector2Int.zero;
                    int bestLen = 0;

                    for (int ci = 0; ci < candidates.Count && strategicCreated < maxStrategic; ci++)
                    {
                        var center = candidates[ci];
                        attempts++;

                        if (!grid.InBoundsCell(center))
                        {
                            bestReason ??= "oob_center";
                            continue;
                        }

                        ref var cd = ref grid.GetCell(center);
                        if (cd.type == CellType.Water)
                        {
                            bestReason ??= "water_cell";
                            continue;
                        }
                        if (cd.type != CellType.River)
                        {
                            bestReason ??= "not_river";
                            continue;
                        }

                        // filtros rápidos (evita spam de TryApply)
                        if (DistanceToNearestJunction(center) < minDistConfluence)
                        {
                            bestReason ??= "near_confluence";
                            if (dbgFord)
                                Debug.Log($"[RoadFordReject] pathId={pathId} crossingId={crossingId} attempt={attempts} center=({center.x},{center.y}) reason=near_confluence");
                            continue;
                        }
                        int th = EstimateRiverThicknessCells(grid, center.x, center.y);
                        if (th > maxThicknessCells)
                        {
                            bestReason ??= "too_thick";
                            if (dbgFord)
                                Debug.Log($"[RoadFordReject] pathId={pathId} crossingId={crossingId} attempt={attempts} center=({center.x},{center.y}) reason=too_thick thickness={th}");
                            continue;
                        }
                        if (!IsCrossingCandidatePlacementValid(grid, center, bankSearchForSelection))
                        {
                            bestReason ??= "no_opposite_banks";
                            if (dbgFord)
                                Debug.Log($"[RoadFordReject] pathId={pathId} crossingId={crossingId} attempt={attempts} center=({center.x},{center.y}) reason=no_opposite_banks");
                            continue;
                        }

                        if (!FarEnough(center))
                        {
                            bestReason ??= "too_close_spacing";
                            if (dbgFord)
                                Debug.Log($"[RoadFordReject] pathId={pathId} crossingId={crossingId} attempt={attempts} center=({center.x},{center.y}) reason=too_close_spacing");
                            continue;
                        }

                        if (!TryApplyBankToBankFordCorridor(grid, config, center, null, null, out string preReason, out Vector2Int preAxis, out int preLen, false))
                        {
                            bestReason ??= preReason;
                            failedCenters.Add(center);
                            if (dbgFord)
                                Debug.Log($"[RoadFordReject] pathId={pathId} crossingId={crossingId} attempt={attempts} center=({center.x},{center.y}) reason={preReason}");
                            continue;
                        }

                        if (preLen > 0 && preLen < 6)
                        {
                            bestReason ??= "corridor_too_short";
                            failedCenters.Add(center);
                            if (dbgFord)
                                Debug.Log($"[RoadFordReject] pathId={pathId} crossingId={crossingId} attempt={attempts} center=({center.x},{center.y}) reason=corridor_too_short");
                            continue;
                        }

                        if (!TryApplyBankToBankFordCorridor(grid, config, center, marked, null, out string applyReason, out Vector2Int applyAxis, out int applyLen, true))
                        {
                            bestReason ??= applyReason;
                            failedCenters.Add(center);
                            if (dbgFord)
                                Debug.Log($"[RoadFordReject] pathId={pathId} crossingId={crossingId} attempt={attempts} center=({center.x},{center.y}) reason={applyReason}");
                            continue;
                        }

                        // éxito
                        strategicCreated++;
                        createdCenters.Add(center);
                        success = true;
                        bestCenter = center;
                        bestAxis = applyAxis;
                        bestLen = applyLen;
                        if (attempts > 1) retrySaved++;
                        if (dbgFord)
                            Debug.Log($"[RoadFordCreated] pathId={pathId} crossingId={crossingId} center=({center.x},{center.y}) len={applyLen} axis=({applyAxis.x},{applyAxis.y})");
                        break;
                    }

                    if (!success)
                    {
                        roadFordRejected++;
                        if (dbgFord)
                            Debug.Log($"[RoadFordRetry] pathId={pathId} crossingId={crossingId} attempts={attempts} success=False bestReason={bestReason}");
                    }
                    else
                    {
                        if (dbgFord)
                            Debug.Log($"[RoadFordRetry] pathId={pathId} crossingId={crossingId} attempts={attempts} success=True bestReason=ok center=({bestCenter.x},{bestCenter.y}) axis=({bestAxis.x},{bestAxis.y}) len={bestLen}");
                    }
                }

                pathId++;
            }
        }

        /// <summary>
        /// MandatoryRiverFordsByCenterline: regla principal.
        /// Cada centerline apta debe intentar crear ≥1 vado funcional (bank-to-bank vía <see cref="TryApplyBankToBankFordCorridor"/>).
        /// </summary>
        private static void MandatoryRiverFordsByCenterline(
            GridSystem grid,
            MapGenConfig config,
            HashSet<Vector2Int> marked,
            List<Vector2Int> createdCenters,
            int bankSearchForSelection,
            bool dbgFord,
            ref int mandatoryCreated,
            out int aptMandatoryRiverCount)
        {
            aptMandatoryRiverCount = 0;
            if (grid == null || config == null)
                return;
            var lines = grid.RiverCenterlinesCellSpace;
            if (lines == null || lines.Count == 0)
                return;

            int maxPerRiver = Mathf.Clamp(config.riverCrossingMaxMandatoryPerRiver, 0, 2);
            if (maxPerRiver <= 0)
                return;

            int centerlines = lines.Count;
            int mandatorySpacing = Mathf.Clamp(config.mandatoryRiverFordMinSpacing, 0, 24);

            if (dbgFord)
                Debug.Log($"[RiverCoverageStart] centerlines={centerlines} maxPerRiver={maxPerRiver} mandatorySpacing={mandatorySpacing}");

            bool TrySnapRiverCell(Vector2Int approx, out Vector2Int snapped)
            {
                // Buscar River cercano en radio 0..3 (anillos), orden determinista.
                if (grid.InBoundsCell(approx) && grid.GetCell(approx).type == CellType.River)
                {
                    snapped = approx;
                    return true;
                }
                for (int r = 1; r <= 3; r++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r)
                            continue;
                        var c = new Vector2Int(approx.x + dx, approx.y + dz);
                        if (!grid.InBoundsCell(c))
                            continue;
                        if (grid.GetCell(c).type == CellType.River)
                        {
                            snapped = c;
                            return true;
                        }
                    }
                }
                snapped = default;
                return false;
            }

            // Crea un orden pseudoaleatorio determinista para índices (barrido) sin RNG global.
            int HashIndex(int seed, int riverId, int idx)
            {
                unchecked
                {
                    int h = seed;
                    h = h * 486187739 + riverId * 16777619;
                    h = h * 486187739 + idx * 73856093;
                    return h;
                }
            }

            for (int riverId = 0; riverId < centerlines; riverId++)
            {
                if (config.uwpOwnedVisualPolicy &&
                    grid.RiverVisualSurfaceCacheFrozen &&
                    grid.RiverVisualSurfaces != null &&
                    riverId < grid.RiverVisualSurfaces.Count &&
                    grid.RiverVisualSurfaces[riverId] != null &&
                    grid.RiverVisualSurfaces[riverId].Skipped)
                {
                    string skipReason = grid.RiverVisualSurfaces[riverId].SkipReason;
                    if (string.IsNullOrEmpty(skipReason))
                        skipReason = "skipped";
                    Debug.Log(
                        $"[UWP_SKIP_DEGENERATE_TRIBUTARY_FORD] riverIndex={riverId} reason={skipReason}");
                    continue;
                }

                var line = lines[riverId];
                int rawPoints = line != null ? line.Count : 0;
                if (line == null || line.Count < 2)
                {
                    if (dbgFord)
                        Debug.Log($"[RiverCoverageMissing] riverId={riverId} reason=centerline_empty attempts=0 bestReason=centerline_empty");
                    continue;
                }

                // Snap Vector2 → Vector2Int aproximado + buscar River cercano (0..3).
                var snappedRiverCells = new List<Vector2Int>(line.Count);
                var seen = new HashSet<Vector2Int>();
                for (int i = 0; i < line.Count; i++)
                {
                    int ax = Mathf.Clamp(Mathf.RoundToInt(line[i].x), 0, grid.Width - 1);
                    int az = Mathf.Clamp(Mathf.RoundToInt(line[i].y), 0, grid.Height - 1);
                    var approx = new Vector2Int(ax, az);
                    if (!TrySnapRiverCell(approx, out var snap))
                        continue;
                    if (seen.Contains(snap))
                        continue;
                    seen.Add(snap);
                    snappedRiverCells.Add(snap);
                }

                bool apt = snappedRiverCells.Count >= 6;
                if (apt)
                    aptMandatoryRiverCount++;
                bool coveredBefore = false;
                if (apt)
                {
                    // Cobertura: si cualquier createdCenter está a <=3 de cualquier celda snap.
                    for (int i = 0; i < createdCenters.Count && !coveredBefore; i++)
                    {
                        var cc = createdCenters[i];
                        for (int j = 0; j < snappedRiverCells.Count; j++)
                        {
                            var rc = snappedRiverCells[j];
                            if (Mathf.Max(Mathf.Abs(cc.x - rc.x), Mathf.Abs(cc.y - rc.y)) <= 3)
                            {
                                coveredBefore = true;
                                break;
                            }
                        }
                    }
                }

                if (dbgFord)
                    Debug.Log($"[RiverCoverageLine] riverId={riverId} rawPoints={rawPoints} snappedRiverCells={snappedRiverCells.Count} coveredBefore={coveredBefore}");

                if (!apt)
                {
                    if (dbgFord)
                        Debug.Log($"[RiverCoverageMissing] riverId={riverId} reason=no_enough_river_cells attempts=0 bestReason=no_enough_river_cells");
                    continue;
                }
                if (coveredBefore)
                    continue;

                int createdForThisRiver = 0;
                // Candidatos: 50/33/66/25/75 + barrido cada 5 con orden pseudoaleatorio determinista.
                var idxs = new List<int>(64);
                void AddIdx(int idx)
                {
                    idx = Mathf.Clamp(idx, 0, snappedRiverCells.Count - 1);
                    for (int k = 0; k < idxs.Count; k++) if (idxs[k] == idx) return;
                    idxs.Add(idx);
                }
                AddIdx(snappedRiverCells.Count / 2);
                AddIdx((snappedRiverCells.Count * 1) / 3);
                AddIdx((snappedRiverCells.Count * 2) / 3);
                AddIdx(snappedRiverCells.Count / 4);
                AddIdx((snappedRiverCells.Count * 3) / 4);

                int edgeSkip = 3;
                for (int i = edgeSkip; i < snappedRiverCells.Count - edgeSkip; i += 5)
                    AddIdx(i);

                // Dejar los “fijos” primero; pseudoaleatorio solo para el barrido (los que no son top 5).
                if (idxs.Count > 5)
                {
                    var sweep = new List<int>(idxs.Count - 5);
                    for (int i = 5; i < idxs.Count; i++) sweep.Add(idxs[i]);
                    sweep.Sort((a, b) => HashIndex(config.seed, riverId, a).CompareTo(HashIndex(config.seed, riverId, b)));
                    for (int i = 0; i < sweep.Count; i++) idxs[5 + i] = sweep[i];
                }

                int attempts = 0;
                string bestReason = null;
                bool createdThisRiver = false;

                bool SpacingOk(Vector2Int c, int spacing)
                {
                    if (spacing <= 0) return true;
                    for (int i = 0; i < createdCenters.Count; i++)
                    {
                        var p = createdCenters[i];
                        if (Mathf.Max(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) < spacing)
                            return false;
                    }
                    return true;
                }

                // Si todo falla por spacing, último intento ignora spacing.
                bool spacingWasTheOnlyBlock = false;

                for (int ii = 0; ii < idxs.Count; ii++)
                {
                    if (createdForThisRiver >= maxPerRiver)
                        break;
                    var center = snappedRiverCells[idxs[ii]];
                    attempts++;

                    if (!grid.InBoundsCell(center))
                    {
                        bestReason ??= "oob_center";
                        continue;
                    }
                    var t = grid.GetCell(center).type;
                    if (t == CellType.Water)
                    {
                        bestReason ??= "water_cell";
                        continue;
                    }
                    if (t != CellType.River)
                    {
                        bestReason ??= "not_river";
                        continue;
                    }

                    if (!SpacingOk(center, mandatorySpacing))
                    {
                        bestReason ??= "too_close_spacing";
                        spacingWasTheOnlyBlock = true;
                        continue;
                    }

                    if (!TryApplyBankToBankFordCorridor(grid, config, center, marked, null, out string applyReason, out Vector2Int applyAxis, out int applyLen, true))
                    {
                        bestReason ??= applyReason;
                        continue;
                    }

                    mandatoryCreated++;
                    createdCenters.Add(center);
                    createdThisRiver = true;
                    createdForThisRiver++;
                    if (dbgFord)
                        Debug.Log($"[RiverCoverageFordCreated] riverId={riverId} center=({center.x},{center.y}) len={applyLen} axis=({applyAxis.x},{applyAxis.y})");
                    break;
                }

                if (!createdThisRiver && spacingWasTheOnlyBlock && idxs.Count > 0)
                {
                    var center = snappedRiverCells[idxs[0]];
                    attempts++;
                    if (grid.InBoundsCell(center) && grid.GetCell(center).type == CellType.River)
                    {
                        if (TryApplyBankToBankFordCorridor(grid, config, center, marked, null, out string applyReason, out Vector2Int applyAxis, out int applyLen, true))
                        {
                            mandatoryCreated++;
                            createdCenters.Add(center);
                            createdThisRiver = true;
                            createdForThisRiver++;
                            if (dbgFord)
                                Debug.Log($"[RiverCoverageFordCreated] riverId={riverId} center=({center.x},{center.y}) len={applyLen} axis=({applyAxis.x},{applyAxis.y})");
                        }
                        else
                        {
                            bestReason ??= applyReason;
                        }
                    }
                }

                if (!createdThisRiver)
                {
                    if (dbgFord)
                        Debug.Log($"[RiverCoverageMissing] riverId={riverId} reason=unable_to_create_ford attempts={attempts} bestReason={bestReason}");
                }
            }
        }

        /// <summary>Invariantes sobre <see cref="CellType.River"/> tras cruces funcionales (solo log).</summary>
        static void ValidateRiverFordConsistency(GridSystem grid, bool log)
        {
            if (!log || grid == null) return;
            int invalid = 0;
            for (int z = 0; z < grid.Height; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    ref var cell = ref grid.GetCell(x, z);
                    if (cell.type != CellType.River)
                        continue;
                    bool bad =
                        (cell.walkable && !cell.riverFord)
                        || (cell.riverFord && !cell.walkable)
                        || (cell.riverFord && cell.waterTraverse != WaterTraverseMode.FordShallow);
                    if (!bad)
                        continue;
                    invalid++;
                    Debug.Log(
                        $"[RiverCellInvalid] x={x} y={z} walkable={cell.walkable} riverFord={cell.riverFord} waterTraverse={cell.waterTraverse} source=CellData");
                }
            }

            if (invalid > 0)
                Debug.LogWarning($"[RiverCellInvalid] total={invalid}");
        }

        private static void TryBuildCrossingAndShoreDecorations(
            Transform parent,
            GridSystem grid,
            MapGenConfig config,
            float waterY,
            float cellSize,
            int waterLayer,
            int[,] interiorDistGrid,
            List<CityNode> strategicCities = null,
            List<Road> strategicRoads = null,
            int distRectMinX = -1,
            int distRectMinZ = -1,
            int distRectW = -1,
            int distRectH = -1)
        {
            DebugWaterCrossingPositionsWorld.Clear();
            DebugRiverFordFootprintCentersWorld.Clear();
            DebugRiverFordFootprintSizesWorld.Clear();
            DebugRiverFordFailedCenterPositionsWorld.Clear();
            if (grid == null || config == null) return;

            var crossingMatsByPrefab = new Dictionary<GameObject, List<Matrix4x4>>();
            if (config.enableRiverCrossings)
            {
                // Umbral coherente con riverWidthRadiusCells: el ancho en grid suele ser ~2*radio+1 en el eje del cauce.
                int maxThicknessCells = Mathf.Clamp(
                    Mathf.Max(
                        Mathf.Max(1, config.riverCrossingMaxThicknessCells),
                        2 * Mathf.Clamp(config.riverWidthRadiusCells, 0, 6) + 1),
                    1, 12);
                int bankSearchForSelection = Mathf.Clamp(config.riverCrossingBankSearchCells, 4, 20);
                int spacing = Mathf.Max(2, config.riverCrossingMinSpacing);
                int maxPick = Mathf.Clamp(Mathf.Max(0, config.riverCrossingMaxPerMap), 0, 32);
                int minSegmentLen = Mathf.Max(2, config.riverFordMinSegmentLengthCells);
                int minDistConfluence = Mathf.Max(0, config.riverFordMinDistanceFromConfluenceCells);
                int maxPerSegment = Mathf.Clamp(config.riverFordMaxPerSegment, 1, 3);
                bool dbgFord = config.riverCrossingCorridorDebugLogs;

                int connectedComponents = CountRiverConnectedComponents(grid);
                var segments = BuildRiverSegments(grid, config, out var junctionNodes, out var endpointNodes);
                int segmentCount = segments.Count;

                if (dbgFord)
                    Debug.Log($"[RiverSegmentSummary] segments={segmentCount} junctions={junctionNodes.Count} endpoints={endpointNodes.Count}");

                int DistanceToNearestJunction(Vector2Int c)
                {
                    if (junctionNodes == null || junctionNodes.Count == 0)
                        return int.MaxValue;
                    int best = int.MaxValue;
                    foreach (var j in junctionNodes)
                    {
                        int d = Mathf.Max(Mathf.Abs(c.x - j.x), Mathf.Abs(c.y - j.y));
                        if (d < best) best = d;
                    }
                    return best;
                }

                var candidateEntries = new List<(Vector2Int c, int th)>(256);
                var segmentCandidates = new Dictionary<int, List<(Vector2Int c, int th, int distJ, int centerBias)>>(segmentCount);
                for (int si = 0; si < segments.Count; si++)
                {
                    var seg = segments[si];
                    var list = new List<(Vector2Int c, int th, int distJ, int centerBias)>();
                    if (seg.cells != null && seg.cells.Count >= minSegmentLen)
                    {
                        int mid = seg.cells.Count / 2;
                        for (int i = 0; i < seg.cells.Count; i++)
                        {
                            var c = seg.cells[i];
                            int distJ = DistanceToNearestJunction(c);
                            if (distJ < minDistConfluence)
                                continue;
                            int th = EstimateRiverThicknessCells(grid, c.x, c.y);
                            if (th > maxThicknessCells)
                                continue;
                            if (!IsCrossingCandidatePlacementValid(grid, c, bankSearchForSelection))
                                continue;
                            int centerBias = Mathf.Abs(i - mid);
                            list.Add((c, th, distJ, centerBias));
                            candidateEntries.Add((c, th));
                        }
                    }

                    list.Sort((a, b) =>
                    {
                        int cmp = b.th.CompareTo(a.th); // mayor grosor primero
                        if (cmp != 0) return cmp;
                        cmp = a.centerBias.CompareTo(b.centerBias);
                        if (cmp != 0) return cmp;
                        return a.distJ.CompareTo(b.distJ);
                    });
                    segmentCandidates[seg.segmentId] = list;
                }

                var orderedSegments = new List<RiverSegment>(segments);
                orderedSegments.Sort((a, b) =>
                {
                    int ac = segmentCandidates.TryGetValue(a.segmentId, out var al) ? al.Count : 0;
                    int bc = segmentCandidates.TryGetValue(b.segmentId, out var bl) ? bl.Count : 0;
                    // Preferir segmentos lejos de lago si hay opciones.
                    int cmp = a.touchesLake.CompareTo(b.touchesLake); // false(0) antes que true(1)
                    if (cmp != 0) return cmp;
                    cmp = bc.CompareTo(ac); // más candidatos primero
                    if (cmp != 0) return cmp;
                    cmp = b.cells.Count.CompareTo(a.cells.Count); // luego longitud
                    if (cmp != 0) return cmp;
                    return a.segmentId.CompareTo(b.segmentId);
                });

                int maxStrategic = (config.enableFunctionalRiverFords && config.riverCrossingEnableStrategicRoadFords)
                    ? Mathf.Clamp(config.riverCrossingMaxStrategicRoadFords, 0, 32)
                    : 0;
                var createdCenters = new List<Vector2Int>(maxPick + maxStrategic + 8);
                var marked = new HashSet<Vector2Int>();
                var failedCenters = new List<Vector2Int>(64);
                int createdCorridors = 0;
                int strategicRoadFords = 0;
                int connectivityCorridors = 0;
                int mandatoryCorridors = 0;
                int rejectedCount = 0;
                int roadFordRejected = 0;

                bool IsFarEnoughFromPicked(Vector2Int c)
                {
                    foreach (var p in createdCenters)
                    {
                        int ch = Mathf.Max(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y));
                        if (ch < spacing)
                            return false;
                    }
                    return true;
                }

                var crossingPrefabs = CollectCrossingDecorationPrefabs(config);
                if (crossingPrefabs.Count == 0 && (maxPick > 0 || maxStrategic > 0))
                {
                    Debug.LogWarning(
                        "[WaterCrossing] No hay prefabs en waterCrossingDecorationPrefabs ni waterCrossingDecorationPrefab: " +
                        "no se generará malla decorativa de vado (los vados lógicos pueden existir igual). Asigna al menos un prefab en MapGenConfig.");
                }

                // 1) Mandatory coverage por centerline (regla principal).
                MandatoryRiverFordsByCenterline(
                    grid,
                    config,
                    marked,
                    createdCenters,
                    bankSearchForSelection,
                    dbgFord,
                    ref mandatoryCorridors,
                    out int aptMandatoryRiverCount);

                bool mandatoryMetAptQuota =
                    aptMandatoryRiverCount > 0 && mandatoryCorridors >= aptMandatoryRiverCount;

                // Recorte de RoadFords si mandatory ya cubrió todos los ríos aptos (0 = no recortar).
                int afterM = Mathf.Clamp(config.riverCrossingMaxStrategicRoadFordsAfterMandatory, 0, 16);
                int effectiveMaxStrategic = maxStrategic;
                if (mandatoryMetAptQuota && afterM > 0)
                    effectiveMaxStrategic = Mathf.Min(maxStrategic, afterM);

                // ─────────────────────────────────────────────────────────────
                // Seguridad gameplay: conectividad de spawns por tierra + vados
                // ─────────────────────────────────────────────────────────────
                if (config.riverCrossingExtraForSpawnConnectivity
                    && s_spawnCellsForConnectivity != null
                    && s_spawnCellsForConnectivity.Count > 0
                    && config.riverCrossingMaxExtraConnectivityFords > 0)
                {
                    bool IsWalkableCell(Vector2Int c)
                    {
                        if (!grid.InBoundsCell(c)) return false;
                        ref var cd = ref grid.GetCell(c);
                        if (cd.type == CellType.Water) return false;
                        if (cd.type == CellType.River) return cd.riverFord && cd.walkable;
                        return cd.walkable;
                    }

                    Vector2Int FindNearestWalkable(Vector2Int start, int maxR)
                    {
                        if (IsWalkableCell(start)) return start;
                        for (int r = 1; r <= maxR; r++)
                        {
                            for (int dx = -r; dx <= r; dx++)
                            for (int dz = -r; dz <= r; dz++)
                            {
                                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                                var c = new Vector2Int(start.x + dx, start.y + dz);
                                if (!grid.InBoundsCell(c)) continue;
                                if (IsWalkableCell(c)) return c;
                            }
                        }
                        return start;
                    }

                    int[,] BuildWalkableComponents(out int compCount)
                    {
                        int ww = grid.Width;
                        int hh = grid.Height;
                        var comp = new int[ww, hh];
                        for (int z = 0; z < hh; z++)
                            for (int x = 0; x < ww; x++)
                                comp[x, z] = -1;
                        compCount = 0;
                        var q2 = new Queue<Vector2Int>();
                        for (int z = 0; z < hh; z++)
                        {
                            for (int x = 0; x < ww; x++)
                            {
                                var c = new Vector2Int(x, z);
                                if (comp[x, z] >= 0 || !IsWalkableCell(c))
                                    continue;
                                int id = compCount++;
                                comp[x, z] = id;
                                q2.Clear();
                                q2.Enqueue(c);
                                while (q2.Count > 0)
                                {
                                    var p = q2.Dequeue();
                                    foreach (var n in grid.Neighbors4(p))
                                    {
                                        if (comp[n.x, n.y] >= 0 || !IsWalkableCell(n))
                                            continue;
                                        comp[n.x, n.y] = id;
                                        q2.Enqueue(n);
                                    }
                                }
                            }
                        }
                        return comp;
                    }

                    // Normalizar spawns a celdas caminables cercanas
                    var spawns = new List<Vector2Int>(s_spawnCellsForConnectivity.Count);
                    for (int i = 0; i < s_spawnCellsForConnectivity.Count; i++)
                        spawns.Add(FindNearestWalkable(s_spawnCellsForConnectivity[i], 14));

                    int walkCompCount;
                    var compGrid = BuildWalkableComponents(out walkCompCount);
                    // Contar tamaños de componentes caminables y detectar el componente principal (más grande).
                    var compSizes = new int[Mathf.Max(0, walkCompCount)];
                    for (int z = 0; z < grid.Height; z++)
                        for (int x = 0; x < grid.Width; x++)
                        {
                            int id = compGrid[x, z];
                            if (id >= 0 && id < compSizes.Length)
                                compSizes[id]++;
                        }

                    int mainWalkComp = -1;
                    int mainWalkCells = 0;
                    for (int i = 0; i < compSizes.Length; i++)
                    {
                        if (compSizes[i] > mainWalkCells)
                        {
                            mainWalkCells = compSizes[i];
                            mainWalkComp = i;
                        }
                    }

                    int minCells = Mathf.Max(0, config.minSpawnWalkableComponentCells);
                    float minRatio = Mathf.Clamp01(config.minSpawnWalkableComponentRatio);

                    bool SpawnNeedsFix(int spawnComp, int spawnCells)
                    {
                        if (mainWalkComp < 0) return false;
                        if (spawnComp != mainWalkComp) return true;
                        if (minCells > 0 && spawnCells < minCells) return true;
                        if (minRatio > 0f && mainWalkCells > 0 && (spawnCells / (float)mainWalkCells) < minRatio) return true;
                        return false;
                    }

                    bool allConnectedToMain = true;
                    if (dbgFord)
                        Debug.Log($"[SpawnConnectivity] spawns={spawns.Count} components={walkCompCount} mainComponentCells={mainWalkCells} allConnectedToMain={(mainWalkComp >= 0)}");

                    Vector2Int FindBankLand(Vector2Int center, Vector2Int axis)
                    {
                        // Busca el primer no-River en esa dirección (igual que el corredor).
                        for (int s = 1; s <= bankSearchForSelection; s++)
                        {
                            int x = center.x + axis.x * s;
                            int z = center.y + axis.y * s;
                            var c = new Vector2Int(x, z);
                            if (!grid.InBoundsCell(c))
                                break;
                            if (grid.GetCell(x, z).type == CellType.River)
                                continue;
                            if (IsWalkableCell(c))
                                return c;
                            break;
                        }
                        return new Vector2Int(int.MinValue, int.MinValue);
                    }

                    int extraBudget = Mathf.Clamp(config.riverCrossingMaxExtraConnectivityFords, 0, 8);
                    for (int spawnIndex = 0; spawnIndex < spawns.Count && extraBudget > 0; spawnIndex++)
                    {
                        if (mainWalkComp < 0)
                            break;
                        var spawnCell = spawns[spawnIndex];
                        int spawnComp = grid.InBoundsCell(spawnCell) ? compGrid[spawnCell.x, spawnCell.y] : -1;
                        int spawnCells = (spawnComp >= 0 && spawnComp < compSizes.Length) ? compSizes[spawnComp] : 0;
                        bool needs = SpawnNeedsFix(spawnComp, spawnCells);
                        if (dbgFord)
                            Debug.Log($"[SpawnConnectivity] spawnIndex={spawnIndex} component={spawnComp} componentCells={spawnCells} main={(spawnComp == mainWalkComp)} isolated={needs}");
                        if (!needs)
                            continue;

                        Vector2Int bestCenter = default;
                        Vector2Int bestAxis = Vector2Int.zero;
                        int bestScore = int.MaxValue;
                        int bestLen = 0;

                        foreach (var seg in orderedSegments)
                        {
                            if (!segmentCandidates.TryGetValue(seg.segmentId, out var perSeg) || perSeg == null) continue;
                            for (int i = 0; i < perSeg.Count; i++)
                            {
                                var e = perSeg[i];
                                var c = e.c;
                                if (!IsFarEnoughFromPicked(c)) continue;
                                if (!TryApplyBankToBankFordCorridor(grid, config, c, null, null, out _, out Vector2Int ax, out int len, false))
                                    continue;
                                if (len > 0 && len < 6) continue;

                                var a = FindBankLand(c, ax);
                                var b = FindBankLand(c, new Vector2Int(-ax.x, -ax.y));
                                if (a.x == int.MinValue || b.x == int.MinValue)
                                    continue;

                                int ca = compGrid[a.x, a.y];
                                int cb = compGrid[b.x, b.y];
                                bool connects = (ca == mainWalkComp && cb == spawnComp) || (ca == spawnComp && cb == mainWalkComp);
                                if (!connects)
                                    continue;

                                int score = Mathf.Max(Mathf.Abs(c.x - spawnCell.x), Mathf.Abs(c.y - spawnCell.y));
                                if (seg.touchesLake) score += 20;
                                if (score < bestScore)
                                {
                                    bestScore = score;
                                    bestCenter = c;
                                    bestAxis = ax;
                                    bestLen = len;
                                }
                            }
                        }

                        if (bestScore == int.MaxValue)
                        {
                            if (dbgFord)
                                Debug.Log($"[SpawnConnectivityFix] spawnIndex={spawnIndex} fromComponent={spawnComp} toComponent={mainWalkComp} fordCenter=(none) success=False reason=no_bridge_candidate");
                            allConnectedToMain = false;
                            continue;
                        }

                        bool ok = TryApplyBankToBankFordCorridor(grid, config, bestCenter, marked, null, out string applyR, out Vector2Int applyAx, out int applyLen, true);
                        if (!ok)
                        {
                            if (dbgFord)
                                Debug.Log($"[SpawnConnectivityFix] spawnIndex={spawnIndex} fromComponent={spawnComp} toComponent={mainWalkComp} fordCenter=({bestCenter.x},{bestCenter.y}) success=False reason={applyR}");
                            allConnectedToMain = false;
                            continue;
                        }

                        connectivityCorridors++;
                        createdCenters.Add(bestCenter);
                        extraBudget--;

                        compGrid = BuildWalkableComponents(out walkCompCount);
                        if (dbgFord)
                            Debug.Log($"[SpawnConnectivityFix] spawnIndex={spawnIndex} fromComponent={spawnComp} toComponent={mainWalkComp} fordCenter=({bestCenter.x},{bestCenter.y}) success=True reason=ok axis=({applyAx.x},{applyAx.y}) len={applyLen}");
                    }

                    if (dbgFord)
                        Debug.Log($"[SpawnConnectivity] spawns={spawns.Count} components={walkCompCount} mainComponentCells={mainWalkCells} allConnectedToMain={allConnectedToMain}");
                }

                // 3) Strategic road fords (mejora, no condición principal). Evita duplicar cerca de vados existentes.
                if (effectiveMaxStrategic > 0 && config.enableFunctionalRiverFords)
                {
                    // Evitar que RoadFords dupliquen vados ya creados por mandatory/conectividad.
                    int roadSpacing = Mathf.Max(spacing, Mathf.Clamp(config.mandatoryRiverFordMinSpacing, 0, 24));

                    // Nota: si quieres aislar mandatory, desactiva riverCrossingEnableStrategicRoadFords en MapGenConfig.
                    int riverCrossingRuns = 0;
                    int retrySavedRoad = 0;
                    double planningMs = 0d;
                    StrategicPathBuildStats st2 = default;

                    var pathsPrimary = RoadNetworkGenerator.BuildStrategicFordPlanningPaths(
                        grid,
                        strategicCities,
                        strategicRoads,
                        config,
                        StrategicFordPathBuildParts.MstAndRoadsOnly,
                        out var st1);
                    planningMs += st1.ElapsedMs;
                    BuildStrategicFordCandidatesFromPaths(
                        grid,
                        config,
                        pathsPrimary,
                        marked,
                        createdCenters,
                        failedCenters,
                        roadSpacing,
                        bankSearchForSelection,
                        maxThicknessCells,
                        minDistConfluence,
                        junctionNodes,
                        effectiveMaxStrategic,
                        dbgFord,
                        ref strategicRoadFords,
                        ref roadFordRejected,
                        ref riverCrossingRuns,
                        ref retrySavedRoad);

                    if (strategicRoadFords < effectiveMaxStrategic)
                    {
                        var synthParts = StrategicFordPathBuildParts.SyntheticToMainLand;
                        if (Mathf.Clamp(config.riverCrossingStrategicAnchorCount, 0, 4) > 0)
                            synthParts |= StrategicFordPathBuildParts.SyntheticAnchors;
                        var pathsSynth = RoadNetworkGenerator.BuildStrategicFordPlanningPaths(
                            grid,
                            strategicCities,
                            strategicRoads,
                            config,
                            synthParts,
                            out st2);
                        planningMs += st2.ElapsedMs;
                        BuildStrategicFordCandidatesFromPaths(
                            grid,
                            config,
                            pathsSynth,
                            marked,
                            createdCenters,
                            failedCenters,
                            roadSpacing,
                            bankSearchForSelection,
                            maxThicknessCells,
                            minDistConfluence,
                            junctionNodes,
                            effectiveMaxStrategic,
                            dbgFord,
                            ref strategicRoadFords,
                            ref roadFordRejected,
                            ref riverCrossingRuns,
                            ref retrySavedRoad);
                    }

                    if (dbgFord)
                    {
                        Debug.Log(
                            $"[RoadFord] planningMs={planningMs:F2} " +
                            $"pathsTotal={st1.TotalPaths + st2.TotalPaths} " +
                            $"mstLax={st1.PathsMstLax + st2.PathsMstLax} phase6Roads={st1.PathsPhase6Roads + st2.PathsPhase6Roads} " +
                            $"synthMain={st1.PathsSyntheticMainLand + st2.PathsSyntheticMainLand} synthAnchors={st1.PathsSyntheticAnchors + st2.PathsSyntheticAnchors} " +
                            $"riverCrossings={riverCrossingRuns} created={strategicRoadFords} rejected={roadFordRejected} retrySaved={retrySavedRoad} " +
                            $"maxStrategicConfig={maxStrategic} effectiveMaxStrategic={effectiveMaxStrategic} mandatoryMetQuota={mandatoryMetAptQuota} aptRivers={aptMandatoryRiverCount} " +
                            $"anchorBudget={Mathf.Clamp(config.riverCrossingStrategicAnchorCount, 0, 4)}");
                    }
                }

                // 4) Optional geometric fords (solo si falta variedad).
                // Regla simple: si aún no llenamos maxPick, intentamos segmentos.
                if (!mandatoryMetAptQuota && createdCenters.Count < maxPick)
                foreach (var seg in orderedSegments)
                {
                    if (createdCorridors >= maxPick)
                        break;

                    if (!segmentCandidates.TryGetValue(seg.segmentId, out var perSeg))
                        continue;

                    int segCandidates = perSeg != null ? perSeg.Count : 0;
                    int segRejected = 0;
                    int segCreated = 0;

                    if (seg.cells == null || seg.cells.Count < minSegmentLen || segCandidates == 0)
                    {
                        if (dbgFord)
                            Debug.Log($"[RiverSegment] id={seg.segmentId} cells={(seg.cells != null ? seg.cells.Count : 0)} candidates={segCandidates} created=0 rejected=0 touchesLake={seg.touchesLake} touchesEdge={seg.touchesEdge}");
                        continue;
                    }

                    for (int i = 0; i < perSeg.Count; i++)
                    {
                        if (createdCorridors >= maxPick || segCreated >= maxPerSegment)
                            break;

                        var entry = perSeg[i];
                        var c = entry.c;
                        if (!IsFarEnoughFromPicked(c))
                            continue;

                        if (!TryApplyBankToBankFordCorridor(grid, config, c, null, null, out string preReason, out Vector2Int preAxis, out int preLen, false))
                        {
                            segRejected++;
                            rejectedCount++;
                            failedCenters.Add(c);
                            if (dbgFord)
                                Debug.Log($"[RiverFordReject] segmentId={seg.segmentId} reason={preReason} center=({c.x},{c.y}) axis=({preAxis.x},{preAxis.y}) thickness={entry.th} distanceToConfluence={entry.distJ} bankSearch={bankSearchForSelection}");
                            continue;
                        }

                        if (preLen > 0 && preLen < 6 && i + 1 < perSeg.Count)
                        {
                            segRejected++;
                            rejectedCount++;
                            failedCenters.Add(c);
                            if (dbgFord)
                                Debug.Log($"[RiverFordReject] segmentId={seg.segmentId} reason=corridor_too_short center=({c.x},{c.y}) axis=({preAxis.x},{preAxis.y}) thickness={entry.th} distanceToConfluence={entry.distJ} bankSearch={bankSearchForSelection}");
                            continue;
                        }

                        if (!TryApplyBankToBankFordCorridor(grid, config, c, marked, null, out string applyReason, out Vector2Int applyAxis, out int len, true))
                        {
                            segRejected++;
                            rejectedCount++;
                            failedCenters.Add(c);
                            if (dbgFord)
                                Debug.Log($"[RiverFordReject] segmentId={seg.segmentId} reason={applyReason} center=({c.x},{c.y}) axis=({applyAxis.x},{applyAxis.y}) thickness={entry.th} distanceToConfluence={entry.distJ} bankSearch={bankSearchForSelection}");
                            continue;
                        }

                        segCreated++;
                        createdCorridors++;
                        createdCenters.Add(c);
                        if (dbgFord)
                            Debug.Log($"[RiverFordCreated] segmentId={seg.segmentId} center=({c.x},{c.y}) len={len} axis=({applyAxis.x},{applyAxis.y})");
                    }

                    if (dbgFord)
                        Debug.Log($"[RiverSegment] id={seg.segmentId} cells={seg.cells.Count} candidates={segCandidates} created={segCreated} rejected={segRejected} touchesLake={seg.touchesLake} touchesEdge={seg.touchesEdge}");
                }

                // (Mandatory ahora corre primero; aquí no se repite.)

                StripRiverFordBlobsSmallerThan(grid, config);
                if (marked != null)
                {
                    marked.Clear();
                    for (int z = 0; z < grid.Height; z++)
                    {
                        for (int x = 0; x < grid.Width; x++)
                        {
                            ref var cd = ref grid.GetCell(x, z);
                            if (cd.type == CellType.River && cd.riverFord)
                                marked.Add(new Vector2Int(x, z));
                        }
                    }
                }

                // Visual: varias instancias por mancha conexa riverFord (dispersas en el rectángulo del corredor).
                ScatterRiverFordDecorationFromGrid(
                    grid,
                    config,
                    waterY,
                    cellSize,
                    crossingPrefabs,
                    crossingMatsByPrefab);

                if (config.debugLogs)
                    Debug.Log($"[RiverCrossingSelect] segments={segmentCount} maxPick={maxPick} picked={createdCenters.Count}");

                if (dbgFord)
                {
                    int totalFordsTyped = createdCorridors + strategicRoadFords + connectivityCorridors + mandatoryCorridors;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (createdCenters.Count != totalFordsTyped)
                        Debug.LogWarning(
                            $"[RiverFordSummary] Inconsistencia: createdCenters={createdCenters.Count} vs normal+road+conn+mandatory={totalFordsTyped} " +
                            "(cada vado debería añadir un centro).");
#endif
                    Debug.Log(
                        $"[RiverFordSummary] rivers={config.riverCount} components={connectedComponents} segments={segmentCount} candidates={candidateEntries.Count} " +
                        $"normalCreated={createdCorridors} roadCreated={strategicRoadFords} connectivityCreated={connectivityCorridors} " +
                        $"mandatoryCreated={mandatoryCorridors} totalCreated={totalFordsTyped} centers={createdCenters.Count} rejected={rejectedCount} roadRejected={roadFordRejected} " +
                        $"maxPerMap={maxPick} maxStrategic={maxStrategic} effectiveStrategicCap={effectiveMaxStrategic} aptRivers={aptMandatoryRiverCount} mandatoryMetQuota={mandatoryMetAptQuota} " +
                        $"skipOptionalGeometric={mandatoryMetAptQuota} minSpacing={spacing} bankSearch={bankSearchForSelection}");
                }

                ValidateRiverFordConsistency(grid, dbgFord);

                // Sin fallback ThinZones (Chebyshev). La verdad final son corredores bank-to-bank.

                if (config.riverCrossingDebugVisuals)
                    BuildCrossingDebugCubes(parent, marked, failedCenters, grid, waterY, cellSize);
            }
            else
            {
                StripRiverFordBlobsSmallerThan(grid, config);
            }

            var shoreMats = new List<Matrix4x4>();
            if (config.waterEnableShoreProps && config.waterShoreRockPrefab != null && config.waterShorePropDensity > 1e-4f)
            {
                if (interiorDistGrid != null)
                {
                    TryBuildLightweightShorelineDressing(
                        shoreMats,
                        grid,
                        config,
                        waterY,
                        cellSize,
                        interiorDistGrid,
                        distRectMinX,
                        distRectMinZ,
                        distRectW,
                        distRectH);
                }
            }

            foreach (var kv in crossingMatsByPrefab)
            {
                if (kv.Key == null || kv.Value == null || kv.Value.Count == 0) continue;
                CombineDecorationMatrices(parent, $"Water_CrossingDecor_{kv.Key.name}", kv.Value, kv.Key, waterLayer);
            }
            if (crossingMatsByPrefab.Count > 0)
            {
                int created = 0;
                foreach (var kv in crossingMatsByPrefab)
                    created += kv.Value != null ? kv.Value.Count : 0;
                Debug.Log($"[RiverCrossingDecor] created={created} prefabs={crossingMatsByPrefab.Count} yOffset={Mathf.Max(0f, config.riverCrossingDecorYOffset):F2}");
            }
            CombineDecorationMatrices(parent, "Water_ShoreDecor", shoreMats, config.waterShoreRockPrefab, waterLayer);
        }

        private static void CombineDecorationMatrices(Transform parent, string objectName, List<Matrix4x4> matrices, GameObject prefab, int waterLayer)
        {
            if (matrices == null || matrices.Count == 0 || prefab == null) return;
            var srcMf = prefab.GetComponentInChildren<MeshFilter>();
            if (srcMf == null || srcMf.sharedMesh == null) return;
            Mesh srcMesh = srcMf.sharedMesh;
            var combine = new CombineInstance[matrices.Count];
            for (int i = 0; i < matrices.Count; i++)
            {
                combine[i].mesh = srcMesh;
                combine[i].transform = matrices[i];
            }
            var outMesh = new Mesh();
            outMesh.name = objectName;
            int estVerts = srcMesh.vertexCount * matrices.Count;
            outMesh.indexFormat = estVerts > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            outMesh.CombineMeshes(combine, true, true);
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.layer = waterLayer;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = outMesh;
            var mr = go.AddComponent<MeshRenderer>();
            var srcMr = prefab.GetComponentInChildren<MeshRenderer>();
            if (srcMr != null)
                mr.sharedMaterial = srcMr.sharedMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                cols[i].isTrigger = true;
        }

        /// <summary>
        /// Solo visual MS lagos: elimina islas Water pequeñas y charcos cerca del río (no toca CellData).
        /// </summary>
        static bool ComponentWithinChebyshevOfAnyRiverCell(
            List<Vector2Int> comp,
            int rectMinX,
            int rectMinZ,
            GridSystem grid,
            int maxDist)
        {
            int w = grid.Width;
            int h = grid.Height;
            int d0 = Mathf.Max(1, maxDist);
            for (int ci = 0; ci < comp.Count; ci++)
            {
                int gx0 = rectMinX + comp[ci].x;
                int gz0 = rectMinZ + comp[ci].y;
                for (int dz = -d0; dz <= d0; dz++)
                {
                    for (int dx = -d0; dx <= d0; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > d0)
                            continue;
                        int gx = gx0 + dx;
                        int gy = gz0 + dz;
                        if ((uint)gx < (uint)w && (uint)gy < (uint)h && grid.GetCell(gx, gy).type == CellType.River)
                            return true;
                    }
                }
            }

            return false;
        }

        public static bool GridCellNearFordRiverChebyshev(GridSystem grid, int gx0, int gz0, int fordDistCells)
        {
            int d0 = Mathf.Max(1, fordDistCells);
            int w = grid.Width;
            int h = grid.Height;
            for (int dz = -d0; dz <= d0; dz++)
            {
                for (int dx = -d0; dx <= d0; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > d0)
                        continue;
                    int gx = gx0 + dx;
                    int gz = gz0 + dz;
                    if ((uint)gx >= (uint)w || (uint)gz >= (uint)h)
                        continue;
                    ref var c = ref grid.GetCell(gx, gz);
                    if (c.type == CellType.River && c.riverFord)
                        return true;
                }
            }

            return false;
        }

        static void ComputeLakeMsCoarseMaskDiagnostics(
            bool[,] coarseMask,
            int rectW,
            int rectH,
            int rectMinX,
            int rectMinZ,
            GridSystem grid,
            MapGenConfig cfg,
            out int connectedToMainRiver,
            out int nearFord,
            out int nearLake)
        {
            connectedToMainRiver = 0;
            nearFord = 0;
            nearLake = 0;
            if (coarseMask == null || grid == null)
                return;
            int riverCheb = cfg != null ? Mathf.Clamp(cfg.riverVisualStrayPoolRiverChebyshevCells, 1, 12) : 4;
            int fordD = cfg != null ? Mathf.Max(1, cfg.riverVisualFordKeepDistanceCells) : 5;
            for (int lz = 0; lz < rectH; lz++)
            {
                for (int lx = 0; lx < rectW; lx++)
                {
                    if (!coarseMask[lx, lz])
                        continue;
                    int gx = rectMinX + lx;
                    int gz = rectMinZ + lz;
                    if (!grid.InBoundsCell(gx, gz) || grid.GetCell(gx, gz).type != CellType.Water)
                        continue;
                    nearLake = 1;
                    if (GridCellNearFordRiverChebyshev(grid, gx, gz, fordD))
                        nearFord = 1;
                    for (int dz = -riverCheb; dz <= riverCheb; dz++)
                    {
                        for (int dx = -riverCheb; dx <= riverCheb; dx++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > riverCheb)
                                continue;
                            int nx = gx + dx;
                            int ny = gz + dz;
                            if ((uint)nx < (uint)grid.Width && (uint)ny < (uint)grid.Height &&
                                grid.GetCell(nx, ny).type == CellType.River)
                            {
                                connectedToMainRiver = 1;
                                break;
                            }
                        }

                        if (connectedToMainRiver != 0)
                            break;
                    }

                    if (nearLake != 0 && nearFord != 0 && connectedToMainRiver != 0)
                        return;
                }
            }
        }

        static bool FloodCoarseWaterOutsideComponentAtLeast(
            Vector2Int seedRect,
            HashSet<Vector2Int> compSet,
            bool[,] coarseMask,
            int rectW,
            int rectH,
            int rectMinX,
            int rectMinZ,
            GridSystem grid,
            int minCount)
        {
            if ((uint)seedRect.x >= (uint)rectW || (uint)seedRect.y >= (uint)rectH)
                return false;
            if (compSet.Contains(seedRect) || !coarseMask[seedRect.x, seedRect.y])
                return false;
            int gxs = rectMinX + seedRect.x;
            int gzs = rectMinZ + seedRect.y;
            if (!grid.InBoundsCell(gxs, gzs) || grid.GetCell(gxs, gzs).type != CellType.Water)
                return false;
            var q = new Queue<Vector2Int>();
            var vis = new HashSet<Vector2Int>();
            q.Enqueue(seedRect);
            vis.Add(seedRect);
            int cnt = 0;
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                cnt++;
                if (cnt >= minCount)
                    return true;
                int gx = rectMinX + p.x;
                int gz = rectMinZ + p.y;
                foreach (var nb in grid.Neighbors4(gx, gz))
                {
                    int lx = nb.x - rectMinX;
                    int lz = nb.y - rectMinZ;
                    if ((uint)lx >= (uint)rectW || (uint)lz >= (uint)rectH)
                        continue;
                    if (!coarseMask[lx, lz] || grid.GetCell(nb.x, nb.y).type != CellType.Water)
                        continue;
                    var key = new Vector2Int(lx, lz);
                    if (compSet.Contains(key) || vis.Contains(key))
                        continue;
                    vis.Add(key);
                    q.Enqueue(key);
                }
            }

            return cnt >= minCount;
        }

        static bool ComponentTouchesLargeWaterOutsideCoarse(
            List<Vector2Int> comp,
            HashSet<Vector2Int> compSet,
            bool[,] coarseMask,
            int rectW,
            int rectH,
            int rectMinX,
            int rectMinZ,
            GridSystem grid,
            int preserveMinCells)
        {
            for (int i = 0; i < comp.Count; i++)
            {
                int gx = rectMinX + comp[i].x;
                int gz = rectMinZ + comp[i].y;
                foreach (var nb in grid.Neighbors4(gx, gz))
                {
                    int lx = nb.x - rectMinX;
                    int lz = nb.y - rectMinZ;
                    if ((uint)lx >= (uint)rectW || (uint)lz >= (uint)rectH)
                        continue;
                    var key = new Vector2Int(lx, lz);
                    if (compSet.Contains(key))
                        continue;
                    if (!coarseMask[lx, lz] || grid.GetCell(nb.x, nb.y).type != CellType.Water)
                        continue;
                    if (FloodCoarseWaterOutsideComponentAtLeast(
                            key,
                            compSet,
                            coarseMask,
                            rectW,
                            rectH,
                            rectMinX,
                            rectMinZ,
                            grid,
                            preserveMinCells))
                        return true;
                }
            }

            return false;
        }

        static int SuppressSmallDetachedLakeCoarseMask(
            bool[,] coarseMask,
            int rectW,
            int rectH,
            int rectMinX,
            int rectMinZ,
            GridSystem grid,
            int minCells,
            MapGenConfig cfg,
            out int fordSmallPatchesPreserved,
            out int strayPoolCellsRemoved)
        {
            fordSmallPatchesPreserved = 0;
            strayPoolCellsRemoved = 0;
            if (coarseMask == null || grid == null || minCells <= 1)
                return 0;

            int preserveMin = cfg != null ? Mathf.Max(8, cfg.lakeVisualPreserveMinCells) : 40;
            int strayMax = cfg != null ? Mathf.Clamp(cfg.riverVisualStrayPoolMaxCells, 4, 64) : 18;
            int riverCheb = cfg != null ? Mathf.Clamp(cfg.riverVisualStrayPoolRiverChebyshevCells, 1, 12) : 4;
            int fordKeep = cfg != null ? Mathf.Max(1, cfg.riverVisualFordKeepDistanceCells) : 5;

            int removedCells = 0;
            var visited = new bool[rectW, rectH];
            var q = new Queue<Vector2Int>();

            for (int lz = 0; lz < rectH; lz++)
            {
                for (int lx = 0; lx < rectW; lx++)
                {
                    if (!coarseMask[lx, lz] || visited[lx, lz])
                        continue;
                    int gx0 = rectMinX + lx;
                    int gz0 = rectMinZ + lz;
                    if (!grid.InBoundsCell(gx0, gz0) || grid.GetCell(gx0, gz0).type != CellType.Water)
                        continue;

                    q.Clear();
                    var comp = new List<Vector2Int>(32);
                    q.Enqueue(new Vector2Int(lx, lz));
                    visited[lx, lz] = true;
                    while (q.Count > 0)
                    {
                        var p = q.Dequeue();
                        comp.Add(p);
                        int ggx = rectMinX + p.x;
                        int ggz = rectMinZ + p.y;
                        foreach (var nb in grid.Neighbors4(ggx, ggz))
                        {
                            int nlx = nb.x - rectMinX;
                            int nlz = nb.y - rectMinZ;
                            if ((uint)nlx >= (uint)rectW || (uint)nlz >= (uint)rectH)
                                continue;
                            if (!coarseMask[nlx, nlz] || visited[nlx, nlz])
                                continue;
                            if (grid.GetCell(nb.x, nb.y).type != CellType.Water)
                                continue;
                            visited[nlx, nlz] = true;
                            q.Enqueue(new Vector2Int(nlx, nlz));
                        }
                    }

                    bool touchesFord = false;
                    for (int ci = 0; ci < comp.Count; ci++)
                    {
                        int ggx = rectMinX + comp[ci].x;
                        int ggz = rectMinZ + comp[ci].y;
                        if (GridCellNearFordRiverChebyshev(grid, ggx, ggz, fordKeep))
                        {
                            touchesFord = true;
                            break;
                        }
                    }

                    if (touchesFord && comp.Count < minCells)
                        fordSmallPatchesPreserved++;

                    if (touchesFord)
                        continue;

                    if (comp.Count >= preserveMin)
                    {
                        s_waterVisualPreservedRealLakeComponents++;
                        continue;
                    }

                    var compSet = new HashSet<Vector2Int>(comp);
                    bool touchesBigLake = ComponentTouchesLargeWaterOutsideCoarse(
                        comp,
                        compSet,
                        coarseMask,
                        rectW,
                        rectH,
                        rectMinX,
                        rectMinZ,
                        grid,
                        preserveMin);
                    if (touchesBigLake)
                    {
                        s_waterVisualPreservedRealLakeComponents++;
                        continue;
                    }

                    bool nearRiver = ComponentWithinChebyshevOfAnyRiverCell(comp, rectMinX, rectMinZ, grid, riverCheb);
                    bool removeTiny = comp.Count < minCells;
                    bool removeStray = nearRiver && comp.Count <= strayMax;
                    if (!removeTiny && !removeStray)
                        continue;

                    if (removeStray)
                    {
                        strayPoolCellsRemoved += comp.Count;
                        s_waterVisualStrayNearRiverComponentsRemoved++;
                    }
                    else if (removeTiny)
                        s_waterVisualTinyLakeCellsRemoved += comp.Count;

                    for (int i = 0; i < comp.Count; i++)
                    {
                        coarseMask[comp[i].x, comp[i].y] = false;
                        removedCells++;
                    }
                }
            }

            return removedCells;
        }

        static long PackCellLongMask(int x, int z) => ((long)x << 32) | (uint)z;

        static bool WaterComponentIntersectsRiverVisualMask(
            List<Vector2Int> comp,
            int rectMinX,
            int rectMinZ,
            bool[,] rivMask,
            GridSystem grid)
        {
            for (int ci = 0; ci < comp.Count; ci++)
            {
                int ggx = rectMinX + comp[ci].x;
                int ggz = rectMinZ + comp[ci].y;
                if ((uint)ggx < (uint)grid.Width && (uint)ggz < (uint)grid.Height && rivMask[ggx, ggz])
                    return true;
            }

            return false;
        }

        static bool WaterComponentNearRiverVisualMask(
            List<Vector2Int> comp,
            int rectMinX,
            int rectMinZ,
            bool[,] rivMask,
            GridSystem grid,
            int nearCells)
        {
            if (nearCells <= 0)
                return false;
            int gw = grid.Width;
            int gh = grid.Height;
            for (int ci = 0; ci < comp.Count; ci++)
            {
                int ggx = rectMinX + comp[ci].x;
                int ggz = rectMinZ + comp[ci].y;
                for (int dz = -nearCells; dz <= nearCells; dz++)
                {
                    for (int dx = -nearCells; dx <= nearCells; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > nearCells)
                            continue;
                        int nx = ggx + dx;
                        int nz = ggz + dz;
                        if ((uint)nx < (uint)gw && (uint)nz < (uint)gh && rivMask[nx, nz])
                            return true;
                    }
                }
            }

            return false;
        }

        static bool WaterComponentTouchesLakeBodyPacked(
            List<Vector2Int> comp,
            int rectMinX,
            int rectMinZ,
            HashSet<long> lakeBody)
        {
            if (lakeBody == null || lakeBody.Count == 0)
                return false;
            for (int ci = 0; ci < comp.Count; ci++)
            {
                int ggx = rectMinX + comp[ci].x;
                int ggz = rectMinZ + comp[ci].y;
                if (lakeBody.Contains(PackCellLongMask(ggx, ggz)))
                    return true;
            }

            return false;
        }

        static int CountRiverVisualMaskCells(bool[,] rivMask, int gw, int gh)
        {
            if (rivMask == null)
                return 0;
            int c = 0;
            for (int z = 0; z < gh; z++)
                for (int x = 0; x < gw; x++)
                    if (rivMask[x, z])
                        c++;
            return c;
        }

        /// <summary>
        /// Limpieza MS lagos: filtra charcos/tiras Water usando RiverVisualSurfaceMask como verdad visual del río.
        /// </summary>
        static int ApplyRiverVisualFinalLakeMaskCleanup(
            bool[,] coarseMask,
            int rectW,
            int rectH,
            int rectMinX,
            int rectMinZ,
            GridSystem grid,
            MapGenConfig cfg)
        {
            bool enabled = cfg != null && (cfg.riverVisualFinalCleanupEnabled || cfg.riverVisualMaskCleanupEnabled);
            if (!enabled || coarseMask == null || grid?.RiverVisualSurfaceMask == null)
                return 0;

            bool[,] rivMask = grid.RiverVisualSurfaceMask;
            int fordDist = Mathf.Max(1, cfg.riverVisualFinalCleanupKeepFordDistanceCells);
            if (fordDist <= 0)
                fordDist = Mathf.Max(1, cfg.riverVisualMaskKeepFordDistanceCells);
            int nearRiver = Mathf.Clamp(cfg.riverVisualFinalCleanupNearRiverCells, 1, 16);
            int maxPatch = Mathf.Max(4, cfg.riverVisualFinalCleanupMaxPatchCells);
            int minRealLake = Mathf.Max(8, cfg.lakeVisualRealLakeMinCells);
            int preserveMin = Mathf.Max(8, cfg.lakeVisualPreserveMinCells);
            var lakeBody = grid.LakeBodyCellsPacked;
            int visualMaskCells = CountRiverVisualMaskCells(rivMask, grid.Width, grid.Height);

            int removedCells = 0;
            int removedComponents = 0;
            int removedNearStrays = 0;
            int presFord = 0;
            int presReal = 0;
            int componentsScanned = 0;
            var visited = new bool[rectW, rectH];
            var q = new Queue<Vector2Int>();
            var reasons = new List<string>(4);

            for (int lz = 0; lz < rectH; lz++)
            {
                for (int lx = 0; lx < rectW; lx++)
                {
                    if (!coarseMask[lx, lz] || visited[lx, lz])
                        continue;
                    int gx0 = rectMinX + lx;
                    int gz0 = rectMinZ + lz;
                    if (!grid.InBoundsCell(gx0, gz0) || grid.GetCell(gx0, gz0).type != CellType.Water)
                        continue;

                    q.Clear();
                    var comp = new List<Vector2Int>(32);
                    q.Enqueue(new Vector2Int(lx, lz));
                    visited[lx, lz] = true;
                    while (q.Count > 0)
                    {
                        var p = q.Dequeue();
                        comp.Add(p);
                        int ggx = rectMinX + p.x;
                        int ggz = rectMinZ + p.y;
                        foreach (var nb in grid.Neighbors4(ggx, ggz))
                        {
                            int nlx = nb.x - rectMinX;
                            int nlz = nb.y - rectMinZ;
                            if ((uint)nlx >= (uint)rectW || (uint)nlz >= (uint)rectH)
                                continue;
                            if (!coarseMask[nlx, nlz] || visited[nlx, nlz])
                                continue;
                            if (grid.GetCell(nb.x, nb.y).type != CellType.Water)
                                continue;
                            visited[nlx, nlz] = true;
                            q.Enqueue(new Vector2Int(nlx, nlz));
                        }
                    }

                    componentsScanned++;

                    bool touchesFord = false;
                    for (int ci = 0; ci < comp.Count; ci++)
                    {
                        int ggx = rectMinX + comp[ci].x;
                        int ggz = rectMinZ + comp[ci].y;
                        if (GridCellNearFordRiverChebyshev(grid, ggx, ggz, fordDist))
                        {
                            touchesFord = true;
                            break;
                        }
                    }

                    bool intersectsVisual = WaterComponentIntersectsRiverVisualMask(comp, rectMinX, rectMinZ, rivMask, grid);
                    bool nearVisualNotOn = !intersectsVisual &&
                        WaterComponentNearRiverVisualMask(comp, rectMinX, rectMinZ, rivMask, grid, nearRiver);
                    bool touchesLakeBody = WaterComponentTouchesLakeBodyPacked(comp, rectMinX, rectMinZ, lakeBody);
                    bool isLargeReal = comp.Count >= minRealLake;
                    var compSet = new HashSet<Vector2Int>(comp);
                    bool touchesBigLake = ComponentTouchesLargeWaterOutsideCoarse(
                        comp,
                        compSet,
                        coarseMask,
                        rectW,
                        rectH,
                        rectMinX,
                        rectMinZ,
                        grid,
                        preserveMin);

                    if (touchesFord)
                    {
                        presFord++;
                        continue;
                    }

                    if (touchesLakeBody || isLargeReal || touchesBigLake)
                    {
                        presReal++;
                        continue;
                    }

                    if (intersectsVisual)
                        continue;

                    bool removeSmallNoBody = comp.Count < maxPatch && !touchesLakeBody;
                    bool removeNearStray = nearVisualNotOn;
                    if (!removeSmallNoBody && !removeNearStray)
                        continue;

                    if (comp.Count > maxPatch * 80)
                        continue;

                    removedComponents++;
                    if (removeNearStray)
                        removedNearStrays++;
                    for (int i = 0; i < comp.Count; i++)
                    {
                        coarseMask[comp[i].x, comp[i].y] = false;
                        removedCells++;
                    }
                }
            }

            s_waterVisualFinalMaskCleanupCells += removedCells;
            s_waterVisualFinalMaskCleanupComponents += removedComponents;
            s_waterVisualFinalMaskPreservedFord += presFord;
            s_waterVisualFinalMaskPreservedRealLakes += presReal;
            s_waterVisualFinalCleanupNearRiverStrays += removedNearStrays;
            s_waterVisualFinalCleanupComponentsScanned += componentsScanned;

            if (cfg.debugLogs || cfg.debugHydrologyNetwork)
            {
                if (removedNearStrays > 0)
                    reasons.Add("near_river_stray");
                if (removedComponents - removedNearStrays > 0)
                    reasons.Add("small_detached");
                if (reasons.Count == 0)
                    reasons.Add("none");
                string reason = string.Join(";", reasons);
                Debug.Log(
                    $"[WaterVisualFinalCleanup] componentsScanned={componentsScanned} removedDetachedComponents={removedComponents} " +
                    $"removedDetachedCells={removedCells} removedNearRiverStrays={removedNearStrays} " +
                    $"preservedFordComponents={presFord} preservedRealLakes={presReal} visualMaskCells={visualMaskCells} reason={reason}");
            }

            return removedCells;
        }

        static int CountRawWaterAndRiverCells(GridSystem grid, out int riverCells)
        {
            riverCells = 0;
            if (grid == null)
                return 0;
            int water = 0;
            for (int gz = 0; gz < grid.Height; gz++)
            {
                for (int gx = 0; gx < grid.Width; gx++)
                {
                    var t = grid.GetCell(gx, gz).type;
                    if (t == CellType.Water)
                        water++;
                    else if (t == CellType.River)
                        riverCells++;
                }
            }

            return water;
        }

        static int CountRiverVisualMaskCells(GridSystem grid)
        {
            bool[,] m = grid?.RiverVisualSurfaceMask;
            if (m == null)
                return 0;
            int c = 0;
            int gw = grid.Width;
            int gh = grid.Height;
            for (int z = 0; z < gh; z++)
                for (int x = 0; x < gw; x++)
                    if (m[x, z])
                        c++;
            return c;
        }

        static int CountTrueInCoarseMask(bool[,] mask, int rectW, int rectH)
        {
            if (mask == null)
                return 0;
            int c = 0;
            for (int z = 0; z < rectH; z++)
                for (int x = 0; x < rectW; x++)
                    if (mask[x, z])
                        c++;
            return c;
        }

        static void LogLakeMSInputAudit(GridSystem grid, MapGenConfig config, bool marchingSquaresLakesOnly)
        {
            if (config == null)
                return;
            int rawWater = CountRawWaterAndRiverCells(grid, out int riverCells);
            int lakeBodyCount = grid?.LakeBodyCellsPacked != null ? grid.LakeBodyCellsPacked.Count : 0;
            int rivMaskCells = CountRiverVisualMaskCells(grid);
            int candidateLake = lakeBodyCount;
            bool willBuild = marchingSquaresLakesOnly &&
                config.lakeCount > 0 &&
                lakeBodyCount > 0 &&
                config.waterRoundedEdges;
            Debug.Log(
                $"[LakeMSInputAudit] lakeCountConfig={config.lakeCount} lakeBodyPackedCount={lakeBodyCount} " +
                $"rawWaterCells={rawWater} riverCells={riverCells} riverVisualMaskCells={rivMaskCells} " +
                $"candidateLakeCells={candidateLake} willBuildMarchingSquares={(willBuild ? 1 : 0)}");
        }

        static void LogLakeMSDisabled(MapGenConfig config, string reason, int lakeBodyCount, int destroyedExisting)
        {
            if (config == null)
                return;
            Debug.Log(
                $"[LakeMSDisabled] reason={reason} lakeCount={config.lakeCount} lakeBodyPackedCount={lakeBodyCount} " +
                $"destroyedExisting={destroyedExisting}");
        }

        static int DestroyExistingWaterMarchingSquares(Transform parent)
        {
            int destroyed = 0;
            if (parent == null)
                return destroyed;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform ch = parent.GetChild(i);
                if (ch == null || ch.name != "Water_MarchingSquares")
                    continue;
                if (Application.isPlaying)
                    Object.Destroy(ch.gameObject);
                else
                    Object.DestroyImmediate(ch.gameObject);
                destroyed++;
            }

            return destroyed;
        }

        static bool CellNearRiverExclusion(GridSystem grid, int gx, int gz, int nearCells)
        {
            if (grid == null)
                return false;
            int gw = grid.Width;
            int gh = grid.Height;
            bool[,] rivMask = grid.RiverVisualSurfaceMask;
            for (int dz = -nearCells; dz <= nearCells; dz++)
            {
                for (int dx = -nearCells; dx <= nearCells; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > nearCells)
                        continue;
                    int nx = gx + dx;
                    int nz = gz + dz;
                    if ((uint)nx >= (uint)gw || (uint)nz >= (uint)gh)
                        continue;
                    if (grid.GetCell(nx, nz).type == CellType.River)
                        return true;
                    if (rivMask != null && rivMask[nx, nz])
                        return true;
                }
            }

            return grid.GetCell(gx, gz).type == CellType.River;
        }

        static int ExpandRealLakeMaskShore(
            bool[,] mask,
            int rectW,
            int rectH,
            int rectMinX,
            int rectMinZ,
            GridSystem grid,
            MapGenConfig config)
        {
            if (mask == null || grid == null || config == null)
                return 0;
            int expandLayers = Mathf.Clamp(config.lakeMSShoreExpandCells, 0, 2);
            if (expandLayers <= 0)
                return 0;
            int nearRiver = Mathf.Max(1, config.lakeMSRemoveNearRiverDistanceCells);
            int added = 0;
            for (int layer = 0; layer < expandLayers; layer++)
            {
                var toAdd = new List<Vector2Int>(rectW * rectH / 8);
                for (int z = 0; z < rectH; z++)
                {
                    for (int x = 0; x < rectW; x++)
                    {
                        if (!mask[x, z])
                            continue;
                        int gx = rectMinX + x;
                        int gz = rectMinZ + z;
                        foreach (var nb in grid.Neighbors4(gx, gz))
                        {
                            int lx = nb.x - rectMinX;
                            int lz = nb.y - rectMinZ;
                            if ((uint)lx >= (uint)rectW || (uint)lz >= (uint)rectH)
                                continue;
                            if (mask[lx, lz])
                                continue;
                            bool nbIsMouth = grid.LakeMouthCellsPacked != null &&
                                grid.LakeMouthCellsPacked.Contains(PackCellLongMask(nb.x, nb.y));
                            if (!nbIsMouth && CellNearRiverExclusion(grid, nb.x, nb.y, nearRiver))
                                continue;
                            toAdd.Add(new Vector2Int(lx, lz));
                        }
                    }
                }

                for (int i = 0; i < toAdd.Count; i++)
                {
                    if (!mask[toAdd[i].x, toAdd[i].y])
                    {
                        mask[toAdd[i].x, toAdd[i].y] = true;
                        added++;
                    }
                }
            }

            return added;
        }

        /// <summary>Incluye boca del lago y tramo corto de río/máscara visual para evitar corte rectangular en MS.</summary>
        static int ExpandLakeMouthIntoCoarseMask(
            bool[,] mask,
            int rectW,
            int rectH,
            int rectMinX,
            int rectMinZ,
            GridSystem grid,
            MapGenConfig config)
        {
            if (mask == null || grid == null || config == null)
                return 0;
            var mouths = grid.LakeMouthCellsPacked;
            var lakeBody = grid.LakeBodyCellsPacked;
            if (mouths == null || mouths.Count == 0)
                return 0;

            int layers = Mathf.Clamp(config.lakeRiverMouthBlendCells + 2, 2, 8);
            if (config.uwpLakeFirstHydrologyPipeline)
                layers = Mathf.Clamp(layers + 4, layers, 12);
            bool[,] rivMask = grid.RiverVisualSurfaceMask;
            var q = new Queue<Vector2Int>();
            var seen = new bool[rectW, rectH];

            void TrySeed(int gx, int gz)
            {
                int lx = gx - rectMinX;
                int lz = gz - rectMinZ;
                if ((uint)lx >= (uint)rectW || (uint)lz >= (uint)rectH || seen[lx, lz])
                    return;
                seen[lx, lz] = true;
                q.Enqueue(new Vector2Int(lx, lz));
            }

            foreach (long pk in mouths)
            {
                int gx = (int)(pk >> 32);
                int gz = (int)(pk & 0xffffffffL);
                if (!grid.InBoundsCell(gx, gz))
                    continue;
                TrySeed(gx, gz);
            }

            int added = 0;
            for (int layer = 0; layer < layers && q.Count > 0; layer++)
            {
                int n = q.Count;
                for (int i = 0; i < n; i++)
                {
                    var p = q.Dequeue();
                    int gx = rectMinX + p.x;
                    int gz = rectMinZ + p.y;
                    var ct = grid.GetCell(gx, gz).type;
                    bool ok = ct == CellType.River
                        || ct == CellType.Water
                        || (rivMask != null && rivMask[gx, gz])
                        || (lakeBody != null && lakeBody.Contains(PackCellLongMask(gx, gz)))
                        || mouths.Contains(PackCellLongMask(gx, gz));
                    if (!ok)
                        continue;

                    if (!mask[p.x, p.y])
                    {
                        mask[p.x, p.y] = true;
                        added++;
                    }

                    foreach (var nb in grid.Neighbors4(gx, gz))
                        TrySeed(nb.x, nb.y);
                }
            }

            return added;
        }

        /// <summary>Lake-first: ensancha máscara MS del lago hacia el centro desde la boca del tributario.</summary>
        static int ExpandLakeFirstTributaryIngressIntoCoarseMask(
            bool[,] mask,
            int rectW,
            int rectH,
            int rectMinX,
            int rectMinZ,
            GridSystem grid,
            MapGenConfig config)
        {
            if (mask == null || grid == null || config == null || !config.uwpLakeFirstHydrologyPipeline)
                return 0;
            var graph = grid.LakeFirstWaterGraph;
            if (graph?.Tributaries == null || grid.LakeBodyComponents == null)
                return 0;

            int added = 0;
            int ingressRadius = Mathf.Clamp(config.lakeRiverMouthBlendCells + 3, 4, 8);
            for (int ti = 0; ti < graph.Tributaries.Count; ti++)
            {
                var trib = graph.Tributaries[ti];
                if (!trib.Accepted || trib.LakeComponentIndex < 0 ||
                    trib.LakeComponentIndex >= grid.LakeBodyComponents.Count)
                    continue;

                var comp = grid.LakeBodyComponents[trib.LakeComponentIndex];
                if (comp == null || comp.Count == 0)
                    continue;

                Vector2 centroid = ComputeLakeFirstComponentCentroid(comp);
                Vector2 mouth = new Vector2(trib.LakeOutletCell.x + 0.5f, trib.LakeOutletCell.y + 0.5f);
                for (int k = 0; k <= 12; k++)
                {
                    float t = k / 12f;
                    Vector2 p = Vector2.Lerp(mouth, centroid, 0.06f + t * 0.52f);
                    int cx = Mathf.FloorToInt(p.x);
                    int cz = Mathf.FloorToInt(p.y);
                    for (int dz = -ingressRadius; dz <= ingressRadius; dz++)
                    {
                        for (int dx = -ingressRadius; dx <= ingressRadius; dx++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > ingressRadius)
                                continue;
                            int gx = cx + dx;
                            int gz = cz + dz;
                            int lx = gx - rectMinX;
                            int lz = gz - rectMinZ;
                            if ((uint)lx >= (uint)rectW || (uint)lz >= (uint)rectH)
                                continue;
                            if (mask[lx, lz])
                                continue;
                            ref var cell = ref grid.GetCell(gx, gz);
                            if (cell.type != CellType.Water && cell.type != CellType.River)
                                continue;
                            mask[lx, lz] = true;
                            added++;
                        }
                    }
                }
            }

            return added;
        }

        static Vector2 ComputeLakeFirstComponentCentroid(HashSet<long> comp)
        {
            if (comp == null || comp.Count == 0)
                return Vector2.zero;
            Vector2 sum = Vector2.zero;
            int n = 0;
            foreach (long pk in comp)
            {
                int x = (int)(pk >> 32);
                int z = (int)(pk & 0xffffffffL);
                sum += new Vector2(x + 0.5f, z + 0.5f);
                n++;
            }

            return n > 0 ? sum / n : Vector2.zero;
        }

        static int CleanupLakeMSComponents(
            bool[,] mask,
            int rectW,
            int rectH,
            int rectMinX,
            int rectMinZ,
            GridSystem grid,
            HashSet<long> lakeBody,
            MapGenConfig config,
            out int componentsScanned,
            out int keptRealLakes,
            out int removedSmall,
            out int removedNearRiver,
            out int removedNoLakeBody)
        {
            componentsScanned = keptRealLakes = removedSmall = removedNearRiver = removedNoLakeBody = 0;
            if (mask == null || grid == null || config == null)
                return 0;
            int minCells = Mathf.Max(
                Mathf.Max(8, config.lakeVisualRealLakeMinCells),
                Mathf.Max(8, config.lakeMSMinComponentCells));
            int nearRiver = Mathf.Max(1, config.lakeMSRemoveNearRiverDistanceCells);
            var visited = new bool[rectW, rectH];
            var q = new Queue<Vector2Int>();

            for (int lz = 0; lz < rectH; lz++)
            {
                for (int lx = 0; lx < rectW; lx++)
                {
                    if (!mask[lx, lz] || visited[lx, lz])
                        continue;
                    q.Clear();
                    var comp = new List<Vector2Int>(64);
                    q.Enqueue(new Vector2Int(lx, lz));
                    visited[lx, lz] = true;
                    while (q.Count > 0)
                    {
                        var p = q.Dequeue();
                        comp.Add(p);
                        int ggx = rectMinX + p.x;
                        int ggz = rectMinZ + p.y;
                        foreach (var nb in grid.Neighbors4(ggx, ggz))
                        {
                            int nlx = nb.x - rectMinX;
                            int nlz = nb.y - rectMinZ;
                            if ((uint)nlx >= (uint)rectW || (uint)nlz >= (uint)rectH)
                                continue;
                            if (!mask[nlx, nlz] || visited[nlx, nlz])
                                continue;
                            visited[nlx, nlz] = true;
                            q.Enqueue(new Vector2Int(nlx, nlz));
                        }
                    }

                    componentsScanned++;
                    bool touchesBody = false;
                    bool nearRiverComp = false;
                    for (int ci = 0; ci < comp.Count; ci++)
                    {
                        int ggx = rectMinX + comp[ci].x;
                        int ggz = rectMinZ + comp[ci].y;
                        if (lakeBody != null && lakeBody.Contains(PackCellLongMask(ggx, ggz)))
                            touchesBody = true;
                        if (CellNearRiverExclusion(grid, ggx, ggz, nearRiver))
                            nearRiverComp = true;
                    }

                    bool remove = false;
                    if (!touchesBody)
                    {
                        remove = true;
                        removedNoLakeBody++;
                    }
                    else if (comp.Count < minCells)
                    {
                        remove = true;
                        removedSmall++;
                    }
                    else if (nearRiverComp && comp.Count < minCells * 2)
                    {
                        remove = true;
                        removedNearRiver++;
                    }

                    if (remove)
                    {
                        for (int ci = 0; ci < comp.Count; ci++)
                            mask[comp[ci].x, comp[ci].y] = false;
                    }
                    else
                    {
                        keptRealLakes++;
                    }
                }
            }

            return CountTrueInCoarseMask(mask, rectW, rectH);
        }

        internal static bool IsWorWaterMaterial(Material mat)
        {
            return mat != null && mat.shader != null && mat.shader.name.StartsWith("Project/WOR");
        }

        /// <summary>Solo mesh MS de lagos: alinear Y con franja de río/tributario sin tocar carve de cuenca.</summary>
        static float ResolveLakeMarchingSquaresDisplayY(
            GridSystem grid,
            MapGenConfig config,
            float baseWaterY,
            float riverStripY)
        {
            if (config == null)
                return riverStripY;

            if (!config.riverVisualUseRiverSurfaceMeshStrip)
                return Mathf.Max(riverStripY, baseWaterY + Mathf.Max(0f, config.lakeWaterSurfaceExtraOffsetWorld));

            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            float yOffset = Mathf.Max(config.waterSurfaceOffset, 0.02f);
            float antiZ = Mathf.Max(0.02f, config.riverRibbonAntiZFightYOffsetWorld);
            float extra = Mathf.Max(0f, config.riverSurfaceMeshExtraYOffsetWorld);
            float ribbon = Mathf.Max(0f, config.riverRibbonVerticalLiftWorld);
            float originY = grid != null ? grid.Origin.y : 0f;
            float channelY = originY + config.waterHeight01 * terrainY + yOffset + antiZ + extra + ribbon;
            float displayY = Mathf.Max(riverStripY, channelY);
            if (config.uwpOwnedVisualPolicy && WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
            {
                // Misma franja que tributarios WebFusion (sin ribbon vertical del main).
                float tribChannelY = baseWaterY + antiZ + extra;
                displayY = Mathf.Max(displayY, tribChannelY);
            }

            return displayY;
        }

        internal static float ComputeUnifiedWaterDepthDrivenLiftWorld(MapGenConfig config, float terrainY)
        {
            if (config == null)
                return 0f;

            float maxDepth01 = Mathf.Max(
                Mathf.Max(config.lakeBedDepthBelowWater01, config.riverBedDepthBelowWater01),
                config.tributaryBedDepthBelowWater01);
            float lift = maxDepth01 * Mathf.Max(1e-4f, terrainY) * Mathf.Clamp(config.unifiedWaterSurfaceLiftFromDepthFactor, 0f, 0.15f);
            return Mathf.Clamp(lift, 0f, 0.45f);
        }

        static bool CellTouchesLandCardinal(GridSystem grid, int gx, int gz)
        {
            foreach (var n in grid.Neighbors4(gx, gz))
            {
                var nt = grid.GetCell(n.x, n.y).type;
                if (nt == CellType.Land || nt == CellType.Mountain)
                    return true;
            }
            return false;
        }

        static void TryBuildLightweightShorelineDressing(
            List<Matrix4x4> shoreMats,
            GridSystem grid,
            MapGenConfig config,
            float waterY,
            float cellSize,
            int[,] dist,
            int distRectMinX,
            int distRectMinZ,
            int distRectW,
            int distRectH)
        {
            if (shoreMats == null || grid == null || config == null)
                return;
            int target = Mathf.RoundToInt(config.waterShorePropDensity * Mathf.Max(16, grid.Width * grid.Height / 2000f));
            target = Mathf.Clamp(target, 0, 36);
            if (target <= 0)
                return;

            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            uint rnd = (uint)(config.seed ^ 0xA531C2E1);
            int placed = 0;
            int scanned = 0;

            for (int gz = 0; gz < grid.Height && placed < target; gz++)
            {
                for (int gx = 0; gx < grid.Width && placed < target; gx++)
                {
                    ref var cell = ref grid.GetCell(gx, gz);
                    if (cell.type != CellType.Water && cell.type != CellType.River)
                        continue;
                    if (cell.riverFord || GridCellNearFordRiverChebyshev(grid, gx, gz, fordD))
                        continue;
                    if (!CellTouchesLandCardinal(grid, gx, gz))
                        continue;

                    int ds = SampleInteriorDistanceCells(gx, gz, dist, distRectMinX, distRectMinZ, distRectW, distRectH);
                    if (ds < 1 || ds > 3)
                        continue;

                    scanned++;
                    float weight = cell.type == CellType.River ? 1.35f : 1f;
                    Vector3 world = grid.CellToWorldCenter(gx, gz);
                    weight += SampleLakeMouthProximity01(world, grid, config) * 2.5f;
                    if (cell.type == CellType.River && ds <= 2)
                        weight += 0.45f;

                    rnd = rnd * 1664525u + 1013904223u;
                    uint threshold = (uint)Mathf.Clamp(220f / weight, 24f, 220f);
                    if ((rnd & 255u) > threshold)
                        continue;

                    float yaw = ((rnd >> 8) & 255u) / 255f * 360f;
                    float scale = 0.72f + ((rnd >> 16) & 63u) / 63f * 0.38f;
                    shoreMats.Add(Matrix4x4.TRS(
                        new Vector3(world.x, waterY, world.z),
                        Quaternion.Euler(0f, yaw, 0f),
                        Vector3.one * scale));
                    placed++;
                }
            }

            if (config.debugLogs && placed > 0)
                Debug.Log($"[ShorelineDressing] placed={placed} scanned={scanned} target={target} seed={config.seed}");
        }

        /// <summary>Suaviza silueta del lago en grid (reduce esquinas tipo Minecraft).</summary>
        static void SmoothLakeCoarseMaskMajority(bool[,] mask, int rectW, int rectH, int iterations, int threshold)
        {
            if (mask == null || rectW < 3 || rectH < 3 || iterations <= 0)
                return;

            var tmp = new bool[rectW, rectH];
            for (int it = 0; it < iterations; it++)
            {
                for (int z = 0; z < rectH; z++)
                {
                    for (int x = 0; x < rectW; x++)
                    {
                        int count = mask[x, z] ? 1 : 0;
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int zz = z + dz;
                            if ((uint)zz >= (uint)rectH)
                                continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx;
                                if ((uint)xx >= (uint)rectW)
                                    continue;
                                if (dx == 0 && dz == 0)
                                    continue;
                                if (mask[xx, zz])
                                    count++;
                            }
                        }

                        tmp[x, z] = count >= threshold;
                    }
                }

                for (int z = 0; z < rectH; z++)
                    for (int x = 0; x < rectW; x++)
                        mask[x, z] = tmp[x, z];
            }
        }

        /// <summary>
        /// Rellena celdas de tierra encerradas dentro del contorno del lago (islas falsas en la malla MS).
        /// </summary>
        static void FillInteriorHolesInLakeMask(bool[,] mask, int rectW, int rectH)
        {
            if (mask == null || rectW < 3 || rectH < 3)
                return;

            var outside = new bool[rectW, rectH];
            var q = new Queue<Vector2Int>(rectW * rectH / 4);

            void TryEnqueueOutside(int x, int z)
            {
                if ((uint)x >= (uint)rectW || (uint)z >= (uint)rectH)
                    return;
                if (mask[x, z] || outside[x, z])
                    return;
                outside[x, z] = true;
                q.Enqueue(new Vector2Int(x, z));
            }

            for (int x = 0; x < rectW; x++)
            {
                TryEnqueueOutside(x, 0);
                TryEnqueueOutside(x, rectH - 1);
            }

            for (int z = 0; z < rectH; z++)
            {
                TryEnqueueOutside(0, z);
                TryEnqueueOutside(rectW - 1, z);
            }

            while (q.Count > 0)
            {
                var p = q.Dequeue();
                TryEnqueueOutside(p.x + 1, p.y);
                TryEnqueueOutside(p.x - 1, p.y);
                TryEnqueueOutside(p.x, p.y + 1);
                TryEnqueueOutside(p.x, p.y - 1);
            }

            for (int z = 1; z < rectH - 1; z++)
            {
                for (int x = 1; x < rectW - 1; x++)
                {
                    if (mask[x, z] || outside[x, z])
                        continue;
                    mask[x, z] = true;
                }
            }
        }

        static bool TryPrepareRealLakeMarchingSquaresMask(
            GridSystem grid,
            MapGenConfig config,
            int blurPadCells,
            out bool[,] coarseMask,
            out int rectMinX,
            out int rectMinZ,
            out int rectW,
            out int rectH,
            out int candidateLakeCells,
            out int expandedCells,
            out int riverCellsExcluded,
            out int riverMaskExcluded)
        {
            coarseMask = null;
            rectMinX = rectMinZ = rectW = rectH = 0;
            candidateLakeCells = expandedCells = riverCellsExcluded = riverMaskExcluded = 0;
            if (grid == null || config == null)
                return false;
            var lakeBody = grid.LakeBodyCellsPacked;
            if (config.lakeCount <= 0 || lakeBody == null || lakeBody.Count == 0)
                return false;

            int w = grid.Width;
            int h = grid.Height;
            int minX = w;
            int minZ = h;
            int maxX = -1;
            int maxZ = -1;
            foreach (long pk in lakeBody)
            {
                int gx = (int)(pk >> 32);
                int gz = (int)(pk & 0xffffffffL);
                if ((uint)gx >= (uint)w || (uint)gz >= (uint)h)
                    continue;
                candidateLakeCells++;
                if (gx < minX)
                    minX = gx;
                if (gz < minZ)
                    minZ = gz;
                if (gx > maxX)
                    maxX = gx;
                if (gz > maxZ)
                    maxZ = gz;
            }

            if (maxX < 0 || candidateLakeCells <= 0)
                return false;

            int pad = Mathf.Max(2, blurPadCells);
            rectMinX = Mathf.Clamp(minX - pad, 0, w - 1);
            rectMinZ = Mathf.Clamp(minZ - pad, 0, h - 1);
            int rectMaxX = Mathf.Clamp(maxX + pad, 0, w - 1);
            int rectMaxZ = Mathf.Clamp(maxZ + pad, 0, h - 1);
            rectW = rectMaxX - rectMinX + 1;
            rectH = rectMaxZ - rectMinZ + 1;
            coarseMask = new bool[rectW, rectH];
            foreach (long pk in lakeBody)
            {
                int gx = (int)(pk >> 32);
                int gz = (int)(pk & 0xffffffffL);
                if ((uint)gx >= (uint)w || (uint)gz >= (uint)h)
                    continue;
                int lx = gx - rectMinX;
                int lz = gz - rectMinZ;
                bool inLakeBody = lakeBody.Contains(PackCellLongMask(gx, gz));
                if (!inLakeBody && grid.GetCell(gx, gz).type == CellType.River)
                {
                    riverCellsExcluded++;
                    continue;
                }

                bool[,] rivMask = grid.RiverVisualSurfaceMask;
                if (!inLakeBody && rivMask != null && rivMask[gx, gz])
                {
                    riverMaskExcluded++;
                    continue;
                }

                coarseMask[lx, lz] = true;
            }

            expandedCells = ExpandRealLakeMaskShore(coarseMask, rectW, rectH, rectMinX, rectMinZ, grid, config);
            int finalCells = CleanupLakeMSComponents(
                coarseMask,
                rectW,
                rectH,
                rectMinX,
                rectMinZ,
                grid,
                lakeBody,
                config,
                out int scanned,
                out int kept,
                out int remSmall,
                out int remNearRiver,
                out int remNoBody);
            FillInteriorHolesInLakeMask(coarseMask, rectW, rectH);
            ExpandLakeMouthIntoCoarseMask(coarseMask, rectW, rectH, rectMinX, rectMinZ, grid, config);
            if (config.uwpLakeFirstHydrologyPipeline)
            {
                int ingressAdded = ExpandLakeFirstTributaryIngressIntoCoarseMask(
                    coarseMask, rectW, rectH, rectMinX, rectMinZ, grid, config);
                expandedCells += ingressAdded;
            }
            SmoothLakeCoarseMaskMajority(coarseMask, rectW, rectH, 3, 5);
            finalCells = CountTrueInCoarseMask(coarseMask, rectW, rectH);
            s_lakeMSFinalCells = finalCells;

            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                int lakeMouthPacked = grid.LakeMouthCellsPacked != null ? grid.LakeMouthCellsPacked.Count : 0;
                Debug.Log(
                    $"[LakeMSMask] source=LakeBodyCellsPacked lakeBodyPackedCount={lakeBody.Count} lakeMouthPackedCount={lakeMouthPacked} " +
                    $"candidateLakeCells={candidateLakeCells} expandedCells={expandedCells} riverCellsExcluded={riverCellsExcluded} " +
                    $"riverMaskExcluded={riverMaskExcluded} finalCells={finalCells}");
                Debug.Log(
                    $"[LakeMSComponentCleanup] componentsScanned={scanned} keptRealLakes={kept} removedSmallComponents={remSmall} " +
                    $"removedNearRiver={remNearRiver} removedNoLakeBodyIntersection={remNoBody} finalLakeCells={finalCells}");
            }

            return finalCells > 0;
        }

        private static bool BuildRoundedWaterMarchingSquares(
            Transform parent,
            GridSystem grid,
            MapGenConfig config,
            Material mat,
            float y,
            float cellSize,
            int waterLayer,
            bool marchingSquaresLakesOnly,
            List<CityNode> strategicCities = null,
            List<Road> strategicRoads = null)
        {
            DebugLastWaterInteriorDistanceGrid = null;
            DebugWaterCrossingPositionsWorld.Clear();
            int w = grid.Width;
            int h = grid.Height;
            int subdiv = Mathf.Clamp(config.waterEdgeSubdiv, 1, 8);
            int blurIters = Mathf.Max(0, config.waterEdgeBlurIterations);
            int blurRadius = Mathf.Clamp(config.waterEdgeBlurRadius, 1, 4);
            float iso = Mathf.Clamp(config.waterIsoLevel, 0.05f, 0.95f);

            int effectiveSubdiv = subdiv;
            int padCells = Mathf.Max(2, blurRadius * Mathf.Max(1, blurIters) + 2);
            bool[,] coarseMask = null;
            int rectMinX;
            int rectMinZ;
            int rectW;
            int rectH;

            LogLakeMSInputAudit(grid, config, marchingSquaresLakesOnly);

            if (marchingSquaresLakesOnly)
            {
                int destroyedExisting = DestroyExistingWaterMarchingSquares(parent);
                int lakeBodyCount = grid.LakeBodyCellsPacked != null ? grid.LakeBodyCellsPacked.Count : 0;
                if (config.lakeCount <= 0 || lakeBodyCount <= 0)
                {
                    LogLakeMSDisabled(
                        config,
                        config.lakeCount <= 0 ? "no_real_lakes" : "empty_lake_body_packed",
                        lakeBodyCount,
                        destroyedExisting);
                    s_lakeMSFinalCells = 0;
                    return false;
                }

                if (!TryPrepareRealLakeMarchingSquaresMask(
                        grid,
                        config,
                        padCells,
                        out coarseMask,
                        out rectMinX,
                        out rectMinZ,
                        out rectW,
                        out rectH,
                        out _,
                        out _,
                        out _,
                        out _))
                {
                    LogLakeMSDisabled(config, "no_valid_lake_components", lakeBodyCount, destroyedExisting);
                    s_lakeMSFinalCells = 0;
                    return false;
                }

                goto LakeMsMaskReady;
            }

            int minX = w, minZ = h, maxX = -1, maxZ = -1;
            for (int gz = 0; gz < h; gz++)
            {
                for (int gx = 0; gx < w; gx++)
                {
                    var t = grid.GetCell(gx, gz).type;
                    if (t != CellType.Water && t != CellType.River)
                        continue;
                    if (gx < minX)
                        minX = gx;
                    if (gz < minZ)
                        minZ = gz;
                    if (gx > maxX)
                        maxX = gx;
                    if (gz > maxZ)
                        maxZ = gz;
                }
            }

            if (maxX < 0 || maxZ < 0)
            {
                if (config.debugLogs)
                    Debug.LogWarning("Fase9 WaterMesh (MS): no hay celdas de agua.");
                return false;
            }

            rectMinX = Mathf.Clamp(minX - padCells, 0, w - 1);
            rectMinZ = Mathf.Clamp(minZ - padCells, 0, h - 1);
            int rectMaxX = Mathf.Clamp(maxX + padCells, 0, w - 1);
            int rectMaxZ = Mathf.Clamp(maxZ + padCells, 0, h - 1);
            rectW = rectMaxX - rectMinX + 1;
            rectH = rectMaxZ - rectMinZ + 1;

            if (!marchingSquaresLakesOnly && config.waterMaskPostProcess && config.waterMaskSmoothIterations > 0)
            {
                coarseMask = new bool[rectW, rectH];
                for (int z = 0; z < rectH; z++)
                    for (int x = 0; x < rectW; x++)
                    {
                        int gx = rectMinX + x;
                        int gz = rectMinZ + z;
                        var t = grid.GetCell(gx, gz).type;
                        coarseMask[x, z] = t == CellType.Water || t == CellType.River;
                    }

                int iters = Mathf.Clamp(config.waterMaskSmoothIterations, 0, 8);
                int thr = Mathf.Clamp(config.waterMaskSmoothThreshold, 0, 9);
                if (iters > 0)
                {
                    var tmp = new bool[rectW, rectH];
                    for (int it = 0; it < iters; it++)
                    {
                        for (int z = 0; z < rectH; z++)
                        {
                            for (int x = 0; x < rectW; x++)
                            {
                                int count = coarseMask[x, z] ? 1 : 0;
                                for (int dz = -1; dz <= 1; dz++)
                                {
                                    int zz = z + dz;
                                    if ((uint)zz >= (uint)rectH) continue;
                                    for (int dx = -1; dx <= 1; dx++)
                                    {
                                        int xx = x + dx;
                                        if ((uint)xx >= (uint)rectW) continue;
                                        if (dx == 0 && dz == 0) continue;
                                        if (coarseMask[xx, zz]) count++;
                                    }
                                }
                                tmp[x, z] = count >= thr;
                            }
                        }
                        var swap = coarseMask; coarseMask = tmp; tmp = swap;
                    }
                }

                if (config != null && config.lakeVisualMinPatchCells > 1)
                {
                    int fordPres;
                    int stray;
                    int rem = SuppressSmallDetachedLakeCoarseMask(
                        coarseMask,
                        rectW,
                        rectH,
                        rectMinX,
                        rectMinZ,
                        grid,
                        config.lakeVisualMinPatchCells,
                        config,
                        out fordPres,
                        out stray);
                    s_waterVisualLakeCellsSuppressed += rem;
                    s_waterVisualLakeFordComponentsPreserved += fordPres;
                    s_riverVisualStrayPoolCellsRemoved += stray;
                }

                if (!marchingSquaresLakesOnly)
                {
                    for (int z = 0; z < rectH; z++)
                        for (int x = 0; x < rectW; x++)
                        {
                            int gx = rectMinX + x;
                            int gz = rectMinZ + z;
                            if (grid.GetCell(gx, gz).type == CellType.River)
                                coarseMask[x, z] = true;
                        }
                }
            }

            LakeMsMaskReady:
            // Límite de seguridad (evita mallas gigantes / GC).
            // En vez de caer directamente a chunks, intentamos degradar calidad bajando subdiv (3→2→1).
            int maxSamples = config.waterMsMaxCornerSamples;
            int sw, sh, sampleX0, sampleZ0;
            while (true)
            {
                sampleX0 = rectMinX * effectiveSubdiv;
                sampleZ0 = rectMinZ * effectiveSubdiv;
                sw = rectW * effectiveSubdiv + 1;
                sh = rectH * effectiveSubdiv + 1;
                int samples = sw * sh;

                if (maxSamples <= 0 || samples <= maxSamples)
                    break;

                if (effectiveSubdiv > 1)
                {
                    effectiveSubdiv--;
                    continue;
                }

                Debug.LogWarning($"Fase9 WaterMesh (MS): demasiado grande para MS (samples={samples} > max={maxSamples}). Fallback a agua por chunks. Sugerencia: sube waterMsMaxCornerSamples o baja waterEdgeSubdiv.");
                return false;
            }

            float step = cellSize / effectiveSubdiv;
            int _interiorMd = 1;
            bool useLakeRectDepth = marchingSquaresLakesOnly && coarseMask != null;
            int[,] interiorDistGrid = useLakeRectDepth
                ? BuildLakeRectInteriorDistanceGrid(coarseMask, rectW, rectH, out _interiorMd)
                : (grid.WaterShoreDistanceCells ?? BuildWaterInteriorDistanceToShoreGrid(grid, out _interiorMd));
            if (!useLakeRectDepth && grid.WaterShoreDistanceCells != null)
                _interiorMd = MaxInteriorDistance(grid.WaterShoreDistanceCells);

            // Campo escalar: lagos ~planos; ríos = perfil por submuestra (euclídeo) + refuerzo post-blur para no caer bajo iso.
            float riverSoftStart = Mathf.Clamp(config.riverMsCellSoftStart01, 0f, 0.82f);
            var field = new float[sw, sh];
            for (int z = 0; z < sh; z++)
            {
                int iz = sampleZ0 + z;
                for (int x = 0; x < sw; x++)
                {
                    int ix = sampleX0 + x;
                    int gx = Mathf.Clamp(ix / effectiveSubdiv, 0, w - 1);
                    int gz = Mathf.Clamp(iz / effectiveSubdiv, 0, h - 1);
                    var t = grid.GetCell(gx, gz).type;

                    if (t == CellType.River)
                    {
                        if (marchingSquaresLakesOnly)
                        {
                            bool inMouthMask = false;
                            if (coarseMask != null)
                            {
                                int mx = Mathf.Clamp(gx - rectMinX, 0, rectW - 1);
                                int mz = Mathf.Clamp(gz - rectMinZ, 0, rectH - 1);
                                inMouthMask = coarseMask[mx, mz];
                            }

                            field[x, z] = inMouthMask ? 1f : 0f;
                        }
                        else
                            field[x, z] = RiverLatticeSoftValue(ix, iz, gx, gz, effectiveSubdiv, riverSoftStart);
                        continue;
                    }

                    bool inCoarse = true;
                    if (coarseMask != null)
                    {
                        int mx = Mathf.Clamp(gx - rectMinX, 0, rectW - 1);
                        int mz = Mathf.Clamp(gz - rectMinZ, 0, rectH - 1);
                        inCoarse = coarseMask[mx, mz];
                    }

                    if (marchingSquaresLakesOnly)
                        field[x, z] = inCoarse ? 1f : 0f;
                    else if (t == CellType.Water)
                        field[x, z] = inCoarse ? 1f : 0f;
                    else
                        field[x, z] = 0f;
                }
            }

            // Ruido suave en lagos (solo MS lagos): desplaza el iso en la orilla tras el blur.
            if (marchingSquaresLakesOnly && config != null && config.lakeShoreMsNoiseAmplitude > 1e-5f)
            {
                float amp = Mathf.Clamp(config.lakeShoreMsNoiseAmplitude, 0f, 0.28f);
                float sc = Mathf.Max(0.015f, config.lakeShoreMsNoiseScale);
                float ox = (config.seed % 997) * 0.0371f;
                float oz = (config.seed / 997) * 0.0413f;
                for (int zi = 0; zi < sh; zi++)
                {
                    float worldZf = grid.Origin.z + (sampleZ0 + zi) * step;
                    for (int xi = 0; xi < sw; xi++)
                    {
                        int ix = sampleX0 + xi;
                        int iz = sampleZ0 + zi;
                        int gx = Mathf.Clamp(ix / effectiveSubdiv, 0, w - 1);
                        int gz = Mathf.Clamp(iz / effectiveSubdiv, 0, h - 1);
                        bool inLakeMask = true;
                        if (coarseMask != null)
                        {
                            int mx = Mathf.Clamp(gx - rectMinX, 0, rectW - 1);
                            int mz = Mathf.Clamp(gz - rectMinZ, 0, rectH - 1);
                            inLakeMask = coarseMask[mx, mz];
                        }
                        else if (grid.GetCell(gx, gz).type != CellType.Water)
                        {
                            continue;
                        }

                        if (!inLakeMask)
                            continue;
                        float worldXf = grid.Origin.x + (sampleX0 + xi) * step;
                        float n = (Mathf.PerlinNoise(ox + worldXf * sc, oz + worldZf * sc) - 0.5f) * 2f * amp;
                        field[xi, zi] = Mathf.Clamp01(field[xi, zi] + n);
                    }
                }
            }

            // Campo continuo MS en celdas (solo si el río no va por ribbon aparte).
            bool useUnifiedWaterField = !marchingSquaresLakesOnly && UnifiedWaterField.IsEnabled(config);
            if (useUnifiedWaterField)
            {
                UnifiedWaterField.FillField(
                    field,
                    sw,
                    sh,
                    sampleX0,
                    sampleZ0,
                    effectiveSubdiv,
                    grid,
                    config);
            }

            if (!useUnifiedWaterField
                && !marchingSquaresLakesOnly
                && config.riverVisualUseContinuousField
                && grid.RiverCenterlinesCellSpace != null
                && grid.RiverCenterlinesCellSpace.Count > 0)
            {
                float halfWBase = Mathf.Max(0.08f, config.riverVisualHalfWidthCells);
                float soft = Mathf.Max(0.02f, config.riverVisualSoftnessCells);
                float strength = Mathf.Clamp01(config.riverVisualFieldStrength);
                float cullMargin = halfWBase * 1.85f + soft + 0.35f;
                for (int z = 0; z < sh; z++)
                {
                    int iz = sampleZ0 + z;
                    float cellZ = iz / (float)effectiveSubdiv;
                    for (int x = 0; x < sw; x++)
                    {
                        int ix = sampleX0 + x;
                        float cellX = ix / (float)effectiveSubdiv;
                        int gx = Mathf.Clamp(ix / effectiveSubdiv, 0, w - 1);
                        int gz = Mathf.Clamp(iz / effectiveSubdiv, 0, h - 1);
                        if (grid.GetCell(gx, gz).type == CellType.Water)
                            continue;

                        float wMul = ComputeRiverVisualWidthMultiplier(grid, gx, gz, cellX, cellZ, config);
                        float halfW = halfWBase * wMul;
                        float d2 = MinDistSqPointToPolylinesCellSpace(cellX, cellZ, grid.RiverCenterlinesCellSpace, cullMargin);
                        if (d2 >= 1e20f)
                            continue;
                        float d = Mathf.Sqrt(d2);
                        float cont = RiverContinuousFieldFromDistance(d, halfW, soft) * strength;
                        if (cont > 1e-5f)
                            field[x, z] = Mathf.Max(field[x, z], cont);
                    }
                }
            }

            ApplyRiverLakeVisualBlendField(
                field,
                sw,
                sh,
                sampleX0,
                sampleZ0,
                effectiveSubdiv,
                grid,
                config,
                marchingSquaresLakesOnly,
                coarseMask,
                rectMinX,
                rectMinZ,
                rectW,
                rectH);

            // Blur para redondear la máscara (suaviza esquinas).
            int msBlurIters = blurIters;
            if (marchingSquaresLakesOnly)
                msBlurIters = Mathf.Min(blurIters + 3, 10);
            if (msBlurIters > 0)
                BoxBlur(field, sw, sh, blurRadius, msBlurIters);
            if (config.waterEdgeSmoothness > 1e-4f)
            {
                float requestedSmooth = config.waterEdgeSmoothness;
                float effectiveSmooth = Mathf.Clamp(requestedSmooth, 0f, 3f);
                if (requestedSmooth > 3f && config.debugLogs)
                    Debug.LogWarning("[WaterMesh] High blur cost avoided");
                int extraBlur = Mathf.RoundToInt(effectiveSmooth);
                extraBlur = Mathf.Clamp(extraBlur, 0, 3);
                for (int k = 0; k < extraBlur; k++)
                    BoxBlur(field, sw, sh, 1, 1);
            }

            float landRiverClamp = Mathf.Clamp(iso - 0.028f, 0f, 0.92f);
            for (int z = 0; z < sh; z++)
            {
                int gz = Mathf.Clamp((sampleZ0 + z) / effectiveSubdiv, 0, h - 1);
                for (int x = 0; x < sw; x++)
                {
                    int gx = Mathf.Clamp((sampleX0 + x) / effectiveSubdiv, 0, w - 1);
                    var t = grid.GetCell(gx, gz).type;
                    // No forzar suelo en río: el perfil por submuestra + blur ya redondea; el mínimo plano devolvía bordes cuadrados.
                    if (!WaterVisualPipelinePolicy.IsUnifiedSingleSurface(config) && t == CellType.Land && CellTouchesRiverCardinal(grid, gx, gz))
                        field[x, z] = Mathf.Min(field[x, z], landRiverClamp);
                }
            }

            if (marchingSquaresLakesOnly && coarseMask != null)
            {
                float mouthFloor = Mathf.Clamp(iso + 0.04f, iso, 0.98f);
                for (int z = 0; z < sh; z++)
                {
                    int iz = sampleZ0 + z;
                    int gz = Mathf.Clamp(iz / effectiveSubdiv, 0, h - 1);
                    for (int x = 0; x < sw; x++)
                    {
                        int ix = sampleX0 + x;
                        int gx = Mathf.Clamp(ix / effectiveSubdiv, 0, w - 1);
                        if (grid.GetCell(gx, gz).type != CellType.River)
                            continue;
                        int mx = gx - rectMinX;
                        int mz = gz - rectMinZ;
                        if ((uint)mx >= (uint)rectW || (uint)mz >= (uint)rectH)
                        {
                            field[x, z] = 0f;
                            continue;
                        }

                        field[x, z] = coarseMask[mx, mz] ? Mathf.Max(field[x, z], mouthFloor) : 0f;
                    }
                }
            }

            float minAboveIso = Mathf.Max(0f, config.riverMsMinAboveIsoAfterBlur);
            if (!marchingSquaresLakesOnly && minAboveIso > 0.0005f)
            {
                float riverFloor = Mathf.Clamp(iso + minAboveIso, iso + 0.02f, 0.995f);
                for (int z = 0; z < sh; z++)
                {
                    int gz = Mathf.Clamp((sampleZ0 + z) / effectiveSubdiv, 0, h - 1);
                    for (int x = 0; x < sw; x++)
                    {
                        int gx = Mathf.Clamp((sampleX0 + x) / effectiveSubdiv, 0, w - 1);
                        if (grid.GetCell(gx, gz).type != CellType.River) continue;
                        field[x, z] = Mathf.Max(field[x, z], riverFloor);
                    }
                }
            }

            // Precrear vértices de esquina (samples) y caches de edges para evitar explosión de vértices.
            var verts = new List<Vector3>(sw * sh);
            var uvs = new List<Vector2>(sw * sh);
            var colors = new List<Color>(sw * sh);
            var tris = new List<int>(Mathf.Max(1024, sw * sh / 2));
            var perimeterVerts = new List<bool>(sw * sh);

            int[,] cornerIndex = new int[sw, sh];
            float uvScale = Mathf.Max(0.001f, config.waterUVScale);
            bool IsPerimeterSample(int x, int z)
            {
                if (field[x, z] < iso)
                    return false;
                for (int dz = -1; dz <= 1; dz++)
                {
                    int zz = z + dz;
                    if ((uint)zz >= (uint)sh)
                        continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                            continue;
                        int xx = x + dx;
                        if ((uint)xx >= (uint)sw)
                            continue;
                        if (field[xx, zz] < iso)
                            return true;
                    }
                }

                return false;
            }

            for (int z = 0; z < sh; z++)
            {
                float wz = grid.Origin.z + (sampleZ0 + z) * step;
                for (int x = 0; x < sw; x++)
                {
                    float wx = grid.Origin.x + (sampleX0 + x) * step;
                    cornerIndex[x, z] = verts.Count;
                    verts.Add(new Vector3(wx, y, wz));
                    uvs.Add(new Vector2(wx * uvScale, wz * uvScale));
                    colors.Add(Color.white);
                    perimeterVerts.Add(IsPerimeterSample(x, z));
                }
            }

            int[,] hEdge = new int[sw - 1, sh];   // entre (x,z) y (x+1,z)
            int[,] vEdge = new int[sw, sh - 1];   // entre (x,z) y (x,z+1)
            for (int z = 0; z < sh; z++)
                for (int x = 0; x < sw - 1; x++)
                    hEdge[x, z] = -1;
            for (int z = 0; z < sh - 1; z++)
                for (int x = 0; x < sw; x++)
                    vEdge[x, z] = -1;

            int GetHEdge(int x, int z, float v0, float v1)
            {
                int idx = hEdge[x, z];
                if (idx != -1) return idx;
                float t = Mathf.Abs(v1 - v0) < 1e-6f ? 0.5f : (iso - v0) / (v1 - v0);
                t = Mathf.Clamp01(t);
                Vector3 p0 = verts[cornerIndex[x, z]];
                Vector3 p1 = verts[cornerIndex[x + 1, z]];
                Vector3 p = Vector3.Lerp(p0, p1, t);
                float wMul = ComputeRiverVisualWidthMultiplier(grid, Mathf.Clamp((sampleX0 + x) / effectiveSubdiv, 0, w - 1), Mathf.Clamp((sampleZ0 + z) / effectiveSubdiv, 0, h - 1), (sampleX0 + x) / (float)effectiveSubdiv, (sampleZ0 + z) / (float)effectiveSubdiv, config);
                float ampWorld = Mathf.Min(config.waterEdgeNoiseAmplitude * cellSize, cellSize * 0.4f) * Mathf.Lerp(0.45f, 1f, Mathf.Clamp01((wMul - 0.45f) / 1.4f));
                NudgeEdgeVertexAtIso(ref p, v0, v1, iso, ampWorld, config.waterEdgeNoiseScale, config.seed, p0, p1);
                idx = verts.Count;
                verts.Add(p);
                uvs.Add(new Vector2(p.x * uvScale, p.z * uvScale));
                colors.Add(Color.white);
                perimeterVerts.Add(true);
                hEdge[x, z] = idx;
                return idx;
            }

            int GetVEdge(int x, int z, float v0, float v1)
            {
                int idx = vEdge[x, z];
                if (idx != -1) return idx;
                float t = Mathf.Abs(v1 - v0) < 1e-6f ? 0.5f : (iso - v0) / (v1 - v0);
                t = Mathf.Clamp01(t);
                Vector3 p0 = verts[cornerIndex[x, z]];
                Vector3 p1 = verts[cornerIndex[x, z + 1]];
                Vector3 p = Vector3.Lerp(p0, p1, t);
                float wMul = ComputeRiverVisualWidthMultiplier(grid, Mathf.Clamp((sampleX0 + x) / effectiveSubdiv, 0, w - 1), Mathf.Clamp((sampleZ0 + z) / effectiveSubdiv, 0, h - 1), (sampleX0 + x) / (float)effectiveSubdiv, (sampleZ0 + z) / (float)effectiveSubdiv, config);
                float ampWorld = Mathf.Min(config.waterEdgeNoiseAmplitude * cellSize, cellSize * 0.4f) * Mathf.Lerp(0.45f, 1f, Mathf.Clamp01((wMul - 0.45f) / 1.4f));
                NudgeEdgeVertexAtIso(ref p, v0, v1, iso, ampWorld, config.waterEdgeNoiseScale, config.seed, p0, p1);
                idx = verts.Count;
                verts.Add(p);
                uvs.Add(new Vector2(p.x * uvScale, p.z * uvScale));
                colors.Add(Color.white);
                perimeterVerts.Add(true);
                vEdge[x, z] = idx;
                return idx;
            }

            void AddTri(int a, int b, int c)
            {
                // Invertimos winding para normal +Y (consistente con el agua anterior).
                tris.Add(a); tris.Add(c); tris.Add(b);
            }

            // Marching Squares (relleno) por celda de sample.
            var poly = new List<int>(8);
            for (int z = 0; z < sh - 1; z++)
            {
                for (int x = 0; x < sw - 1; x++)
                {
                    float a = field[x, z];
                    float b = field[x + 1, z];
                    float c = field[x + 1, z + 1];
                    float d = field[x, z + 1];
                    bool i0 = a >= iso;
                    bool i1 = b >= iso;
                    bool i2 = c >= iso;
                    bool i3 = d >= iso;

                    int mask = (i0 ? 1 : 0) | (i1 ? 2 : 0) | (i2 ? 4 : 0) | (i3 ? 8 : 0);
                    if (mask == 0) continue;

                    int p0 = cornerIndex[x, z];
                    int p1 = cornerIndex[x + 1, z];
                    int p2 = cornerIndex[x + 1, z + 1];
                    int p3 = cornerIndex[x, z + 1];

                    bool e0 = i0 != i1; // p0-p1
                    bool e1 = i1 != i2; // p1-p2
                    bool e2 = i2 != i3; // p2-p3
                    bool e3 = i3 != i0; // p3-p0

                    int vE0 = e0 ? GetHEdge(x, z, a, b) : -1;
                    int vE1 = e1 ? GetVEdge(x + 1, z, b, c) : -1;
                    int vE2 = e2 ? GetHEdge(x, z + 1, d, c) : -1; // p3-p2 (top)
                    int vE3 = e3 ? GetVEdge(x, z, a, d) : -1;

                    // Casos ambiguos (diagonales): 5 y 10.
                    if (mask == 5 || mask == 10)
                    {
                        bool centerInside;
                        if (marchingSquaresLakesOnly)
                        {
                            // Decisor asintótico: evita agujeros en sillas tras blur (islas en el lago).
                            centerInside = mask == 5 ? (a + c >= b + d) : (b + d >= a + c);
                        }
                        else
                        {
                            float center = (a + b + c + d) * 0.25f;
                            centerInside = center >= iso;
                        }

                        if (!centerInside)
                        {
                            if (mask == 5)
                            {
                                // Triángulos separados: (p0-e0-e3) y (p2-e2-e1)
                                AddTri(p0, vE0, vE3);
                                AddTri(p2, vE2, vE1);
                            }
                            else
                            {
                                // mask==10: (p1-e1-e0) y (p3-e3-e2)
                                AddTri(p1, vE1, vE0);
                                AddTri(p3, vE3, vE2);
                            }
                            continue;
                        }
                        // Si el centro está dentro, usamos polígono conectado (sin self-intersection).
                    }

                    poly.Clear();
                    if (i0) poly.Add(p0);
                    if (e0) poly.Add(vE0);
                    if (i1) poly.Add(p1);
                    if (e1) poly.Add(vE1);
                    if (i2) poly.Add(p2);
                    if (e2) poly.Add(vE2);
                    if (i3) poly.Add(p3);
                    if (e3) poly.Add(vE3);

                    if (poly.Count < 3) continue;
                    int a0 = poly[0];
                    for (int i = 1; i < poly.Count - 1; i++)
                        AddTri(a0, poly[i], poly[i + 1]);
                }
            }

            if (tris.Count == 0)
            {
                Debug.LogWarning("Fase9 WaterMesh (MS): 0 tris (sin agua o iso demasiado alto).");
                return false;
            }

            float perimeterExpandWorld = 0f;
            if (marchingSquaresLakesOnly)
                perimeterExpandWorld = config.lakeMSPerimeterExpandWorld;
            else if (WaterVisualPipelinePolicy.IsUnifiedSingleSurface(config))
                perimeterExpandWorld = config.unifiedWaterPerimeterExpandWorld;
            if (perimeterExpandWorld > 1e-4f)
            {
                ExpandLakeMsPerimeterVertices(
                    verts,
                    perimeterVerts,
                    field,
                    sw,
                    sh,
                    sampleX0,
                    sampleZ0,
                    step,
                    grid.Origin,
                    perimeterExpandWorld);
            }

            bool directAssetLakeMaterial =
                marchingSquaresLakesOnly &&
                config != null &&
                config.lakeWaterMaterialMode == WaterMaterialRuntimeMode.DirectAsset;

            if (directAssetLakeMaterial)
            {
                ApplyPlaneLikeLakeUvs(uvs, verts);
                for (int i = 0; i < colors.Count; i++)
                    colors[i] = Color.white;
            }
            else if (!IsWorWaterMaterial(mat))
                ApplyMarchingSquaresDepthUv(
                    uvs,
                    verts,
                    grid,
                    config,
                    interiorDistGrid,
                    y,
                    useLakeRectDepth ? rectMinX : -1,
                    useLakeRectDepth ? rectMinZ : -1,
                    useLakeRectDepth ? rectW : -1,
                    useLakeRectDepth ? rectH : -1);
            if (!directAssetLakeMaterial)
            {
                ApplyStylizedLakeVertexColors(
                    colors,
                    uvs,
                    verts,
                    grid,
                    config,
                    interiorDistGrid,
                    mat,
                    useLakeRectDepth,
                    rectMinX,
                    rectMinZ,
                    rectW,
                    rectH);
            }
            if (WaterVisualPipelinePolicy.IsUnifiedSingleSurface(config))
                ApplyUnifiedWaterVertexData(colors, uvs, verts, grid, config, interiorDistGrid);

            var mesh = new Mesh();
            string meshObjectName = WaterVisualPipelinePolicy.IsUnifiedSingleSurface(config)
                ? "Water_UnifiedSurface"
                : "Water_MarchingSquares";
            mesh.name = meshObjectName;
            if (verts.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            WaterStylizedIntegration.PrepareMesh(mesh, mat);

            var go = new GameObject(meshObjectName);
            go.transform.SetParent(parent, false);
            go.layer = waterLayer;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.renderingLayerMask = 1u;
            if (!directAssetLakeMaterial)
                WaterStylizedIntegration.AttachWaterObject(go, mf, mr, mat);

            if (marchingSquaresLakesOnly && coarseMask != null)
            {
                int nearRiverCells = Mathf.Max(1, config.riverVisualFinalCleanupNearRiverCells);
                ComputeWaterVisualBoundsMaskStats(
                    grid,
                    mesh.bounds,
                    nearRiverCells,
                    out int msIntersectsMask,
                    out int msNearMaskCells);
                int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
                int msNearFord = ComputeNearFordFromWorldBounds(grid, mesh.bounds, fordD);
                int triCount = tris.Count / 3;
                LogWaterVisualObject(
                    config,
                    go.name,
                    "LakeMS",
                    -1,
                    verts.Count,
                    triCount,
                    mesh.bounds,
                    msIntersectsMask,
                    msNearMaskCells,
                    msNearFord,
                    0,
                    0,
                    mr.enabled ? 1 : 0);
            }

            TryBuildCrossingAndShoreDecorations(
                parent,
                grid,
                config,
                y,
                cellSize,
                waterLayer,
                interiorDistGrid,
                strategicCities,
                strategicRoads,
                useLakeRectDepth ? rectMinX : -1,
                useLakeRectDepth ? rectMinZ : -1,
                useLakeRectDepth ? rectW : -1,
                useLakeRectDepth ? rectH : -1);

            if (config.debugDrawWaterShoreDepthGizmos)
            {
                DebugLastWaterInteriorDistanceGrid = interiorDistGrid;
                DebugLastWaterInteriorDistanceMax = _interiorMd;
            }
            else
            {
                DebugLastWaterInteriorDistanceGrid = null;
            }

            if (config.debugLogs)
                Debug.Log($"Fase9 WaterMesh (MS): rect={rectW}x{rectH} celdas (pad={padCells}), subdiv={effectiveSubdiv} (cfg={subdiv}), blurIters={blurIters}, iso={iso:F2}, verts={verts.Count}, tris={tris.Count / 3}.");
            return true;
        }

        private static void BoxBlur(float[,] field, int w, int h, int radius, int iterations)
        {
            var tmp = new float[w, h];
            int r = Mathf.Max(1, radius);
            for (int it = 0; it < iterations; it++)
            {
                for (int z = 0; z < h; z++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float sum = 0f;
                        int count = 0;
                        for (int dz = -r; dz <= r; dz++)
                        {
                            int zz = z + dz;
                            if ((uint)zz >= (uint)h) continue;
                            for (int dx = -r; dx <= r; dx++)
                            {
                                int xx = x + dx;
                                if ((uint)xx >= (uint)w) continue;
                                sum += field[xx, zz];
                                count++;
                            }
                        }
                        tmp[x, z] = count > 0 ? sum / count : field[x, z];
                    }
                }
                // swap
                for (int z = 0; z < h; z++)
                    for (int x = 0; x < w; x++)
                        field[x, z] = tmp[x, z];
            }
        }

        /// <summary>
        /// Valor 0–1 en la malla MS para celdas de río: alto al centro del tile, suave hacia los bordes (chamfer anti-escalera).
        /// </summary>
        private static float RiverLatticeSoftValue(int ix, int iz, int gx, int gz, int subdiv, float softStart01)
        {
            if (subdiv <= 1)
                return 1f;
            if (softStart01 <= 0.001f)
                return 1f;
            int rx = ix - gx * subdiv;
            int rz = iz - gz * subdiv;
            float fx = rx / (float)subdiv;
            float fz = rz / (float)subdiv;
            float ndx = (fx - 0.5f) * 2f;
            float ndz = (fz - 0.5f) * 2f;
            float r = Mathf.Clamp01(Mathf.Sqrt(ndx * ndx + ndz * ndz) / 1.41421356f);
            float t0 = Mathf.Clamp(softStart01, 0.22f, 0.82f);
            const float t1 = 0.992f;
            return 1f - Mathf.SmoothStep(t0, t1, r);
        }

        /// <summary>Campo 0–1 desde distancia euclídea al eje del río (en celdas).</summary>
        private static float RiverContinuousFieldFromDistance(float distCells, float halfWidthCells, float softnessCells)
        {
            float t = distCells - halfWidthCells;
            if (t <= 0f)
                return 1f;
            if (t >= softnessCells)
                return 0f;
            return 1f - Mathf.SmoothStep(0f, softnessCells, t);
        }

        private static float MinDistSqPointToPolylinesCellSpace(float px, float pz, List<List<Vector2>> polylines, float segmentCullMarginCells)
        {
            float best = float.MaxValue;
            foreach (var poly in polylines)
            {
                if (poly == null || poly.Count < 2)
                    continue;
                for (int i = 0; i < poly.Count - 1; i++)
                {
                    Vector2 a = poly[i];
                    Vector2 b = poly[i + 1];
                    float m = segmentCullMarginCells;
                    float minX = Mathf.Min(a.x, b.x) - m;
                    float maxX = Mathf.Max(a.x, b.x) + m;
                    float minY = Mathf.Min(a.y, b.y) - m;
                    float maxY = Mathf.Max(a.y, b.y) + m;
                    if (px < minX || px > maxX || pz < minY || pz > maxY)
                        continue;
                    float d2 = DistSqPointSegmentXY(px, pz, a.x, a.y, b.x, b.y);
                    if (d2 < best)
                        best = d2;
                }
            }

            return best;
        }

        private static float DistSqPointSegmentXY(float px, float pz, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax;
            float dy = by - ay;
            float len2 = dx * dx + dy * dy;
            if (len2 < 1e-12f)
            {
                float qx = px - ax;
                float qz = pz - ay;
                return qx * qx + qz * qz;
            }

            float t = Mathf.Clamp01(((px - ax) * dx + (pz - ay) * dy) / len2);
            float qx2 = px - (ax + t * dx);
            float qz2 = pz - (ay + t * dy);
            return qx2 * qx2 + qz2 * qz2;
        }

        private static bool CellTouchesRiverCardinal(GridSystem grid, int gx, int gz)
        {
            foreach (var n in grid.Neighbors4(gx, gz))
            {
                if (grid.GetCell(n.x, n.y).type == CellType.River)
                    return true;
            }
            return false;
        }

        /// <summary>LEGACY_COMPATIBILIDAD: fallback WaterChunk; desactivado cuando <see cref="RiversRenderedBySurfaceMesh"/>.</summary>
        static void DestroyStrayWaterChunksUnderRoot(Transform waterRoot, MapGenConfig config)
        {
            if (waterRoot == null)
                return;
            int destroyed = 0;
            for (int i = waterRoot.childCount - 1; i >= 0; i--)
            {
                Transform ch = waterRoot.GetChild(i);
                if (ch == null || !ch.name.StartsWith("WaterChunk_"))
                    continue;
                DestroyWaterVisualGameObject(ch.gameObject);
                destroyed++;
            }

            if (destroyed > 0 && config != null &&
                (config.debugLogs || config.debugHydrologyNetwork))
            {
                Debug.LogWarning(
                    $"[WaterPipelineGuard] destroyedStrayWaterChunks={destroyed} reason=river_surface_mode");
            }
        }

        static void LogWaterPipelineGuard(
            bool riversRenderedBySurface,
            bool msIncludesRiverCells,
            bool marchingSquaresOk,
            MapGenConfig config)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;

            bool ok = riversRenderedBySurface && s_waterChunksCreated == 0 && !msIncludesRiverCells;
            if (config.lakeCount <= 0 && s_waterMarchingSquaresCreated != 0)
                ok = false;
            if (msIncludesRiverCells && riversRenderedBySurface)
            {
                Debug.LogWarning(
                    "[WaterPipelineGuard] msIncludesRiver=1 con RiverSurfaceMesh activo (ríos no deben usar MS).");
                ok = false;
            }

            Debug.Log(
                $"[WaterPipelineGuard] riversBySurfaceMesh={(riversRenderedBySurface ? 1 : 0)} " +
                $"waterChunksCreated={s_waterChunksCreated} msIncludesRiver={(msIncludesRiverCells ? 1 : 0)} " +
                $"lakeMSCreated={s_waterMarchingSquaresCreated} ok={(ok ? 1 : 0)}");
        }

        static void LogMcpWaterSystemPostPatchAudit(
            GridSystem grid,
            MapGenConfig config,
            Material lakeMat,
            bool lakeFallbackUsed,
            bool msIncludesRiverCells,
            bool marchingSquaresOk)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;

            int waterChildren = _waterRoot != null ? _waterRoot.childCount : 0;
            int chunks = 0;
            int ms = 0;
            int main = 0;
            int trib = 0;
            if (_waterRoot != null)
            {
                for (int i = 0; i < _waterRoot.childCount; i++)
                {
                    string n = _waterRoot.GetChild(i).name;
                    if (n.StartsWith("WaterChunk_"))
                        chunks++;
                    if (n == "Water_MarchingSquares")
                        ms = 1;
                    if (n == "Water_RiverSurface_Main")
                        main = 1;
                    if (n.StartsWith("Water_RiverSurface_Tributary"))
                        trib++;
                }
            }

            int confCount = grid?.RiverConfluences != null ? grid.RiverConfluences.Count : 0;
            bool pipelineOk = !msIncludesRiverCells && s_waterChunksCreated == 0;
            if (config.lakeCount <= 0 && ms != 0)
                pipelineOk = false;
            Debug.Log(
                $"[MCPConfluenceAudit] isPlaying={(Application.isPlaying ? 1 : 0)} waterChildren={waterChildren} " +
                $"hasMain={main} tributaryObjects={trib} riverCenterlineCount=" +
                $"{(grid.RiverCenterlinesCellSpace != null ? grid.RiverCenterlinesCellSpace.Count : 0)} " +
                $"riverConfluenceCount={confCount} hasMS={ms} hasChunks={(chunks > 0 ? 1 : 0)} " +
                $"issueLikely={(config.riverCount > 1 && trib == 0 ? "no_tributary_mesh" : (chunks > 0 ? "water_chunk" : "none"))}");

            string lakeShaderName = lakeMat != null && lakeMat.shader != null ? lakeMat.shader.name : "null";
            string visualIssue = chunks > 0 || msIncludesRiverCells
                ? "pipeline"
                : (main == 0 && config.riverCount > 0 ? "no_river_mesh"
                    : (config.riverCount > 1 && trib == 0 ? "no_tributary_mesh"
                        : (lakeFallbackUsed && config.lakeCount > 0 ? "lake_material_fallback" : "none")));
            int centerlineCount = grid?.RiverCenterlinesCellSpace != null ? grid.RiverCenterlinesCellSpace.Count : 0;
            LogMcpTributaryWidthAudit(config, main, trib, chunks, ms, confCount);
            RiverDendriticUtility.LogRiverNetworkTopologyAudit(
                grid,
                config,
                config.riverCount,
                s_waterChunksCreated,
                msIncludesRiverCells ? 1 : 0,
                pipelineOk ? 1 : 0);
            RiverDendriticUtility.LogRiverConfluenceGeometryAudits(grid, config);
            RiverDendriticUtility.LogRiverOrderWidthAudits(grid, config, grid.CellSizeWorld);
            RiverDendriticUtility.LogMcpTributaryTopologyAudit(
                grid,
                config,
                trib,
                chunks > 0 ? 1 : 0,
                ms,
                pipelineOk ? 1 : 0);
            LogMcpWaterConfluencePreFixAudit(
                config,
                waterChildren,
                main,
                trib,
                ms,
                chunks,
                lakeMat,
                lakeShaderName,
                lakeFallbackUsed,
                centerlineCount,
                confCount,
                pipelineOk,
                visualIssue);

            Debug.Log(
                $"[MCPWaterSystemPostPatchAudit] case=rc{config.riverCount}_lc{config.lakeCount} " +
                $"waterChildren={waterChildren} hasWaterChunks={(chunks > 0 ? 1 : 0)} hasMarchingSquares={ms} " +
                $"hasRiverSurfaceMain={main} hasRiverSurfaceTributaries={trib} " +
                $"riverMeshVerts={RiverSurfaceMeshBuilder.LastVertexSum} riverMeshTris={RiverSurfaceMeshBuilder.LastTriSum} " +
                $"lakeMSCreated={s_waterMarchingSquaresCreated} lakeMaterial={(lakeMat != null ? lakeMat.name : "null")} " +
                $"lakeShader={lakeShaderName} lakeFallbackUsed={(lakeFallbackUsed ? 1 : 0)} " +
                $"waterChunksCreated={s_waterChunksCreated} msIncludesRiver={(msIncludesRiverCells ? 1 : 0)} " +
                $"confluenceCount={confCount} confluenceTerrainOk=-1 pipelineGuardOk={(pipelineOk ? 1 : 0)} " +
                $"visualIssueLikely={visualIssue}");
        }

        static void LogMcpTributaryWidthAudit(
            MapGenConfig config,
            int hasMain,
            int tributaryObjects,
            int chunkCount,
            int hasMs,
            int confluenceCount)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;

            float mainAvg = RiverSurfaceMeshBuilder.LastMainRiverAvgHalfWidthWorld;
            float tribAvg = RiverSurfaceMeshBuilder.GetTributaryAvgHalfWidthWorldMean();
            float ratio = mainAvg > 0.01f ? tribAvg / mainAvg : 0f;
            string issue = tributaryObjects > 0 && ratio > 0f && ratio < 0.35f
                ? "tributary_visual_width_scale"
                : "none";
            Debug.Log(
                $"[MCPTributaryWidthAudit] mainWidthAvg={mainAvg:F3} tributaryWidthAvg={tribAvg:F3} " +
                $"tributaryToMainRatio={ratio:F3} hasChunks={(chunkCount > 0 ? 1 : 0)} hasMS={hasMs} " +
                $"confluenceCount={confluenceCount} issueLikely={issue}");
        }

        static void LogMcpWaterConfluencePreFixAudit(
            MapGenConfig config,
            int waterChildren,
            int hasMain,
            int tributaryObjects,
            int hasMs,
            int chunkCount,
            Material lakeMat,
            string lakeShaderName,
            bool lakeFallbackUsed,
            int riverCenterlineCount,
            int confluenceCount,
            bool pipelineGuardOk,
            string issueLikely)
        {
            if (config == null)
                return;
            Debug.Log(
                $"[MCPWaterConfluencePreFixAudit] case=rc{config.riverCount}_lc{config.lakeCount} " +
                $"waterChildren={waterChildren} hasMain={hasMain} tributaryObjects={tributaryObjects} hasMS={hasMs} " +
                $"hasChunks={(chunkCount > 0 ? 1 : 0)} lakeMaterialName={(lakeMat != null ? lakeMat.name : "null")} " +
                $"lakeShaderName={lakeShaderName} lakeFallbackUsed={(lakeFallbackUsed ? 1 : 0)} " +
                $"riverCenterlineCount={riverCenterlineCount} riverConfluenceCount={confluenceCount} " +
                $"issueLikely={issueLikely} pipelineGuardOk={(pipelineGuardOk ? 1 : 0)}");
        }

        static void LogLakeMaterial(
            MapGenConfig config,
            Material lakeMat,
            bool fallbackUsed,
            string fallbackReason,
            bool lakeMsCreated)
        {
            if (config == null || config.lakeCount <= 0)
                return;
            if (!config.debugLogs && !config.debugHydrologyNetwork)
                return;

            Debug.Log(
                $"[LakeMaterial] lakeMaterial={(lakeMat != null ? lakeMat.name : "null")} " +
                $"shader={(lakeMat != null && lakeMat.shader != null ? lakeMat.shader.name : "null")} " +
                $"fallbackUsed={(fallbackUsed ? 1 : 0)} reason={(string.IsNullOrEmpty(fallbackReason) ? "none" : fallbackReason)} " +
                $"lakeMSCreated={(lakeMsCreated ? 1 : 0)} lakeMSFinalCells={s_lakeMSFinalCells}");
        }

        const string DefaultLakeMaterialAssetPath = "Packages/com.pmg.unified-world-pipeline/Content/Materials/MAT_WOR_Lake.mat";
        const string ResourcesLakeMaterialPath = "Water/MAT_LakeWaterSimple";
        const string LakeShaderName = "Project/Lake Water Simple";

        static Material TryLoadDefaultLakeMaterialAsset()
        {
            Material res = UnityEngine.Resources.Load<Material>(ResourcesLakeMaterialPath);
            if (res != null)
                return res;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(DefaultLakeMaterialAssetPath);
#else
            return null;
#endif
        }

        /// <summary>Material URP simple para lagos (Marching Squares).</summary>
        static Material GetOrCreateLakeWaterMaterial(
            Material assignedWater,
            MapGenConfig config,
            Material fallback,
            out bool fallbackUsed,
            out string fallbackReason)
        {
            fallbackUsed = false;
            fallbackReason = null;

            if (config != null && config.lakeWaterMaterial != null)
            {
                var inst = new Material(config.lakeWaterMaterial);
                inst.name = config.lakeWaterMaterial.name;
                ApplyLakeWaterShaderProperties(inst, config);
                StabilizeStylizedWaterLakeMaterial(inst, config);
                return inst;
            }

            Material assetLake = TryLoadDefaultLakeMaterialAsset();
            if (assetLake != null)
            {
                var inst = new Material(assetLake);
                inst.name = assetLake.name;
                ApplyLakeWaterShaderProperties(inst, config);
                StabilizeStylizedWaterLakeMaterial(inst, config);
                return inst;
            }

            Shader lakeShader = Shader.Find(LakeShaderName);
            if (lakeShader != null)
            {
                var mat = new Material(lakeShader) { name = "MAT_LakeWaterSimple" };
                ApplyLakeWaterShaderProperties(mat, config);

                mat.renderQueue = 3000;
                return mat;
            }

            fallbackUsed = true;
            fallbackReason = "shader_not_found";
            if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
            {
                Debug.LogError(
                    $"[LakeMaterialError] reason=shader_not_found shaderName={LakeShaderName}");
            }

            if (fallback != null)
                return new Material(fallback);

            var emergency = GetOrCreateWaterMaterial(assignedWater, config);
            if (emergency != null)
                fallbackReason = "emergency_water_material";
            return emergency;
        }

        static void ApplyLakeWaterShaderProperties(Material mat, MapGenConfig config)
        {
            if (mat == null || config == null)
                return;
            if (IsStylizedWaterMaterial(mat))
                return;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", config.lakeWaterBaseColor);
            if (mat.HasProperty("_Alpha"))
                mat.SetFloat("_Alpha", Mathf.Clamp01(config.lakeWaterAlpha));
            if (mat.HasProperty("_RippleSpeed"))
                mat.SetFloat("_RippleSpeed", Mathf.Clamp(config.waterUvFlowSpeedScale * 0.16f, 0.04f, 0.4f));
        }

        static void ApplyUnifiedWaterVertexData(
            List<Color> colors,
            List<Vector2> uvs,
            List<Vector3> verts,
            GridSystem grid,
            MapGenConfig config,
            int[,] distGrid)
        {
            if (colors == null || uvs == null || verts == null || grid == null || config == null)
                return;

            float cs = Mathf.Max(1e-5f, grid.CellSizeWorld);
            float normLake = Mathf.Max(1f, config.lakeShoreVisualWidth > 0.01f ? config.lakeShoreVisualWidth : config.shoreVisualWidth);
            float normRiver = Mathf.Max(1f, config.riverShoreVisualWidth > 0.01f ? config.riverShoreVisualWidth : config.shoreVisualWidth);
            int gw = grid.Width;
            int gh = grid.Height;

            for (int i = 0; i < colors.Count && i < verts.Count; i++)
            {
                Vector3 v = verts[i];
                int gx = Mathf.Clamp(Mathf.FloorToInt((v.x - grid.Origin.x) / cs), 0, gw - 1);
                int gz = Mathf.Clamp(Mathf.FloorToInt((v.z - grid.Origin.z) / cs), 0, gh - 1);
                CellType type = grid.GetCell(gx, gz).type;
                bool isRiver = type == CellType.River;
                float norm = isRiver ? normRiver : normLake;
                float depth01 = SampleInteriorDistance01(v, distGrid, grid, cs, norm);
                if (grid.WaterDepth01 != null)
                    depth01 = Mathf.Max(depth01, WaterSurfaceFieldBuilder.SampleDepth01(grid, v));
                depth01 = Mathf.Clamp01(depth01);

                Vector2 flow = grid.WaterFlowXZ != null
                    ? WaterSurfaceFieldBuilder.SampleFlowXZ(grid, v)
                    : Vector2.zero;
                float flowSpeed01 = Mathf.Clamp01(flow.magnitude);
                float mouth01 = Mathf.Max(
                    SampleLakeMouthProximity01(v, grid, config),
                    SampleRiverConfluenceProximity01(v, grid, config));

                colors[i] = new Color(depth01, mouth01, flowSpeed01, isRiver ? 1f : 0f);

                if (i < uvs.Count)
                {
                    Vector2 uv = uvs[i];
                    uv.y = Mathf.Lerp(uv.y, Mathf.Lerp(0.08f, 0.55f, depth01), 0.85f);
                    uvs[i] = uv;
                }
            }
        }

        static float SampleRiverConfluenceProximity01(Vector3 world, GridSystem grid, MapGenConfig config)
        {
            if (grid?.RiverConfluences == null || grid.RiverConfluences.Count == 0 || config == null)
                return 0f;

            float cs = Mathf.Max(1e-5f, grid.CellSizeWorld);
            int gx = Mathf.FloorToInt((world.x - grid.Origin.x) / cs);
            int gz = Mathf.FloorToInt((world.z - grid.Origin.z) / cs);
            float radius = Mathf.Clamp(config.riverConfluenceMergeRadiusCells + config.riverConfluenceVisualBlendLengthCells * 0.5f, 3f, 14f);
            float best = radius + 1f;

            for (int i = 0; i < grid.RiverConfluences.Count; i++)
            {
                Vector2Int p = grid.RiverConfluences[i].Cell;
                float d = Mathf.Max(Mathf.Abs(gx - p.x), Mathf.Abs(gz - p.y));
                if (d < best)
                    best = d;
            }

            return Mathf.Clamp01(1f - best / radius);
        }

        static void BuildUnifiedRiverCurrentOverlays(
            Transform parent,
            GridSystem grid,
            MapGenConfig config,
            float waterY,
            float cellSize,
            int waterLayer)
        {
            if (parent == null || grid == null || config == null || !config.unifiedRiverCurrentsEnabled)
                return;
            var lines = grid.RiverCenterlinesCellSpace;
            if (lines == null || lines.Count == 0)
                return;

            float width = Mathf.Max(0.02f, config.unifiedRiverCurrentWidthWorld);
            float length = Mathf.Max(0.05f, config.unifiedRiverCurrentLengthWorld);
            float spacing = Mathf.Max(0.25f, config.unifiedRiverCurrentSpacingCells) * Mathf.Max(0.01f, cellSize);
            float y = waterY + Mathf.Max(0.002f, config.unifiedRiverCurrentYOffsetWorld);
            float alpha = Mathf.Clamp01(config.unifiedRiverCurrentAlpha);

            var verts = new List<Vector3>(512);
            var uvs = new List<Vector2>(512);
            var colors = new List<Color>(512);
            var tris = new List<int>(768);

            for (int li = 0; li < lines.Count; li++)
            {
                List<Vector2> line = lines[li];
                if (line == null || line.Count < 2)
                    continue;

                float carry = 0f;
                for (int i = 0; i < line.Count - 1; i++)
                {
                    Vector2 a = line[i];
                    Vector2 b = line[i + 1];
                    Vector2 d = b - a;
                    float segCells = d.magnitude;
                    if (segCells < 1e-4f)
                        continue;

                    Vector2 dir2 = d / segCells;
                    float segWorld = segCells * cellSize;
                    carry += segWorld;
                    if (carry < spacing)
                        continue;

                    carry = 0f;
                    Vector2 mid = Vector2.Lerp(a, b, 0.5f);
                    int gx = Mathf.Clamp(Mathf.RoundToInt(mid.x), 0, grid.Width - 1);
                    int gz = Mathf.Clamp(Mathf.RoundToInt(mid.y), 0, grid.Height - 1);
                    if (grid.GetCell(gx, gz).type != CellType.River)
                        continue;

                    Vector3 center = new Vector3(
                        grid.Origin.x + (mid.x + 0.5f) * cellSize,
                        y,
                        grid.Origin.z + (mid.y + 0.5f) * cellSize);
                    float phase = Mathf.Repeat((li * 0.37f) + (i * 0.113f), 1f);
                    AddUnifiedCurrentQuad(verts, uvs, colors, tris, center, dir2, width, length, alpha, phase);
                }
            }

            if (tris.Count == 0)
                return;

            var mesh = new Mesh { name = "Water_UnifiedRiverCurrents" };
            if (verts.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Water_UnifiedRiverCurrents");
            go.transform.SetParent(parent, false);
            go.layer = waterLayer;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GetOrCreateUnifiedCurrentMaterial(config);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.renderingLayerMask = 1u;
        }

        static void AddUnifiedCurrentQuad(
            List<Vector3> verts,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> tris,
            Vector3 center,
            Vector2 dir2,
            float width,
            float length,
            float alpha,
            float phase)
        {
            Vector3 dir = new Vector3(dir2.x, 0f, dir2.y).normalized;
            if (dir.sqrMagnitude < 1e-5f)
                return;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            Vector3 a = center - dir * (length * 0.5f);
            Vector3 b = center + dir * (length * 0.5f);
            Vector3 s = side * (width * 0.5f);
            int idx = verts.Count;
            verts.Add(a - s);
            verts.Add(a + s);
            verts.Add(b + s);
            verts.Add(b - s);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(1f, 0f));
            Color c = new Color(phase, 1f, 1f, alpha);
            colors.Add(c);
            colors.Add(c);
            colors.Add(c);
            colors.Add(c);
            tris.Add(idx);
            tris.Add(idx + 2);
            tris.Add(idx + 1);
            tris.Add(idx);
            tris.Add(idx + 3);
            tris.Add(idx + 2);
        }

        static Material s_unifiedCurrentMaterial;

        static Material GetOrCreateUnifiedCurrentMaterial(MapGenConfig config)
        {
            if (s_unifiedCurrentMaterial == null)
            {
                Shader shader = Shader.Find("Project/WOR Unified River Current")
                    ?? Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Color");
                s_unifiedCurrentMaterial = new Material(shader) { name = "MAT_WOR_UnifiedRiverCurrent_Runtime" };
                s_unifiedCurrentMaterial.renderQueue = 3100;
            }

            Color c = new Color(0.78f, 0.94f, 1f, Mathf.Clamp01(config != null ? config.unifiedRiverCurrentAlpha : 0.32f));
            if (s_unifiedCurrentMaterial.HasProperty("_BaseColor"))
                s_unifiedCurrentMaterial.SetColor("_BaseColor", c);
            if (s_unifiedCurrentMaterial.HasProperty("_Color"))
                s_unifiedCurrentMaterial.SetColor("_Color", c);
            if (s_unifiedCurrentMaterial.HasProperty("_Speed"))
                s_unifiedCurrentMaterial.SetFloat("_Speed", Mathf.Max(0f, config != null ? config.unifiedRiverCurrentSpeed : 1.35f));
            return s_unifiedCurrentMaterial;
        }

        static void ApplyStylizedLakeVertexColors(
            List<Color> colors,
            List<Vector2> uvs,
            List<Vector3> verts,
            GridSystem grid,
            MapGenConfig config,
            int[,] distGrid,
            Material mat,
            bool useRectDist = false,
            int rectMinX = -1,
            int rectMinZ = -1,
            int rectW = -1,
            int rectH = -1)
        {
            if (colors == null || verts == null || grid == null || config == null ||
                (!WaterStylizedIntegration.IsStylizedWaterMaterial(mat) &&
                 (mat == null || !mat.shader.name.StartsWith("Project/WOR"))))
                return;

            bool isWor = IsWorWaterMaterial(mat);
            float shoreCells = config.lakeShoreVisualWidth > 0.01f ? config.lakeShoreVisualWidth : config.shoreVisualWidth;
            float normLake = isWor
                ? Mathf.Max(4f, shoreCells * 1.2f)
                : Mathf.Max(1f, shoreCells) * 0.28f;
            int rmx = useRectDist ? rectMinX : -1;
            int rmz = useRectDist ? rectMinZ : -1;
            int rw = useRectDist ? rectW : -1;
            int rh = useRectDist ? rectH : -1;

            for (int i = 0; i < colors.Count && i < verts.Count; i++)
            {
                float depth01 = SampleInteriorDistance01(
                    verts[i],
                    distGrid,
                    grid,
                    grid.CellSizeWorld,
                    normLake,
                    rmx,
                    rmz,
                    rw,
                    rh);
                if (grid.WaterDepth01 != null && !useRectDist)
                    depth01 = Mathf.Max(depth01, WaterSurfaceFieldBuilder.SampleDepth01(grid, verts[i]));
                if (!isWor)
                    depth01 = depth01 * depth01 * (3f - 2f * depth01);

                if (isWor)
                {
                    float mouthProx = SampleLakeMouthProximity01(verts[i], grid, config);
                    colors[i] = new Color(depth01, mouthProx, 0f, 0f);
                    if (uvs != null && i < uvs.Count)
                    {
                        var uv = uvs[i];
                        uv.y = depth01;
                        uvs[i] = uv;
                    }
                }
                else
                {
                    colors[i] = WaterStylizedIntegration.GetLakeVertexColor(depth01);
                }
            }
        }

        static void StabilizeStylizedWaterLakeMaterial(Material mat, MapGenConfig config)
        {
            if (!IsStylizedWaterMaterial(mat))
                return;

            // Solo intersección por textura (CrossPan): en MS plano cubre toda la malla.
            // Mantener intersección depth-based de orilla como en RiverDemo/Clear.
            WaterMaterialRuntimeMode mode = config != null
                ? config.lakeWaterMaterialMode
                : WaterMaterialRuntimeMode.SW2ProceduralTranslator;
            if (mode != WaterMaterialRuntimeMode.SW2ProceduralTranslator)
                return;

            if (mat.HasProperty("_Texture_IntersectionOn")) mat.SetFloat("_Texture_IntersectionOn", 0f);
            if (mat.HasProperty("_CrossPan_IntersectionOn")) mat.SetFloat("_CrossPan_IntersectionOn", 0f);

            WaterStylizedIntegration.ApplyStylizedLakeMaterialRuntime(mat, config, mode);
        }

        static bool IsStylizedWaterMaterial(Material mat)
        {
            return WaterStylizedIntegration.IsStylizedWaterMaterial(mat);
        }

        /// <summary>Material para el ribbon de río. Aplica transparencia si config.riverWaterAlpha &lt; 1.</summary>
        private static Material GetOrCreateWaterMaterial(Material assigned, MapGenConfig config)
        {
            Material mat;
            if (assigned != null)
                mat = new Material(assigned);
            else
            {
                Shader river = Shader.Find("Project/RTS River Water");
                if (river != null)
                    mat = new Material(river);
                else
                {
                    Material fb = GetFallbackMaterial();
                    mat = fb != null ? new Material(fb) : null;
                }
            }

            if (mat == null)
            {
                Debug.LogError("WaterMeshBuilder: No se pudo crear material de agua.");
                return null;
            }

            if (mat.renderQueue < 0) mat.renderQueue = 2001;

            bool isRtsRiver = mat.shader != null && mat.shader.name.Contains("RTS River Water");
            if (assigned == null && !isRtsRiver)
            {
                Color azulAgua = new Color(0.25f, 0.48f, 0.75f, 1f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", azulAgua);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", azulAgua);
            }

            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_ZWriteControl")) mat.SetFloat("_ZWriteControl", 0f);
            if (mat.HasProperty("_ZWriteMode")) mat.SetFloat("_ZWriteMode", 0f);

            float alpha = (config != null && config.riverWaterAlpha > 0f) ? Mathf.Clamp01(config.riverWaterAlpha) : 1f;
            bool isStylizedWater = IsStylizedWaterMaterial(mat);
            if (isRtsRiver)
                ApplyRiverWaterShaderProperties(mat, config);
            else if (!isStylizedWater && alpha < 0.99f)
            {
                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = alpha;
                    mat.SetColor("_BaseColor", c);
                }
                else if (mat.HasProperty("_Color"))
                {
                    Color c = mat.GetColor("_Color");
                    c.a = alpha;
                    mat.SetColor("_Color", c);
                }
                mat.renderQueue = 3000;
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
                if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);
            return mat;
        }

        static bool MaterialsUseSameVisualSource(Material a, Material b)
        {
            if (a == null || b == null)
                return false;
            if (a.shader != b.shader)
                return false;
            return NormalizeRuntimeMaterialName(a.name) == NormalizeRuntimeMaterialName(b.name);
        }

        static string NormalizeRuntimeMaterialName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "";
            return name
                .Replace(" (Instance)", "")
                .Replace("(Instance)", "")
                .Trim();
        }

        static void ApplyRiverWaterShaderProperties(Material mat, MapGenConfig config)
        {
            if (mat == null || config == null) return;
            if (!mat.HasProperty("_ShallowColor")) return;
            mat.SetColor("_ShallowColor", config.riverWaterShallowColor);
            mat.SetColor("_DeepColor", config.riverWaterDeepColor);
            float f = Mathf.Clamp(config.waterUvFlowSpeedScale, 0f, 2.5f);
            mat.SetVector("_FlowSpeed", new Vector4(config.riverUVFlowSpeed.x * f, config.riverUVFlowSpeed.y * f, 0f, 0f));
            mat.SetFloat("_BankSoft", Mathf.Clamp(config.riverBankBlendStrength, 0.05f, 0.55f));
            mat.SetFloat("_Alpha", Mathf.Clamp01(config.riverWaterAlpha));
        }

        private static Material _fallback;
        private static Material GetFallbackMaterial()
        {
            if (_fallback != null) return _fallback;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Legacy Shaders/Vertex Lit");
            if (shader != null)
            {
                _fallback = new Material(shader);
                if (_fallback.HasProperty("_BaseColor")) _fallback.SetColor("_BaseColor", new Color(0.25f, 0.48f, 0.75f, 1f));
                else if (_fallback.HasProperty("_Color")) _fallback.SetColor("_Color", new Color(0.25f, 0.48f, 0.75f, 1f));
                _fallback.renderQueue = 2001;
                if (_fallback.HasProperty("_Cull")) _fallback.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                if (_fallback.HasProperty("_Surface")) _fallback.SetFloat("_Surface", 0f);
                if (_fallback.HasProperty("_ZWrite")) _fallback.SetFloat("_ZWrite", 0f);
            }
            return _fallback;
        }
    }
}
