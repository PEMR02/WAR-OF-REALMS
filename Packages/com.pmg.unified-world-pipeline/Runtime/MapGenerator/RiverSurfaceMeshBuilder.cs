using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Marcador de cuello visual en centerline (para cascadas/decor). Generado en build del ribbon.</summary>
    public readonly struct RiverSurfaceNarrowSite
    {
        public readonly int RiverIndex;
        public readonly int CenterlineIndex;
        public readonly Vector2 Cell;
        public readonly float HalfWidthWorld;
        public readonly float LocalAvgHalfWidthWorld;
        public readonly float NarrowRatio;
        public readonly float TurnAngleDeg;
        public readonly bool NearFord;
        /// <summary>0=caída de ancho orgánico, 1=riesgo de pliegue en curva cerrada, 2=ambos.</summary>
        public readonly int KindFlags;

        public RiverSurfaceNarrowSite(
            int riverIndex,
            int centerlineIndex,
            Vector2 cell,
            float halfWidthWorld,
            float localAvgHalfWidthWorld,
            float narrowRatio,
            float turnAngleDeg,
            bool nearFord,
            int kindFlags)
        {
            RiverIndex = riverIndex;
            CenterlineIndex = centerlineIndex;
            Cell = cell;
            HalfWidthWorld = halfWidthWorld;
            LocalAvgHalfWidthWorld = localAvgHalfWidthWorld;
            NarrowRatio = narrowRatio;
            TurnAngleDeg = turnAngleDeg;
            NearFord = nearFord;
            KindFlags = kindFlags;
        }
    }

    /// <summary>
    /// Malla de superficie de río tipo cinta (quad strip) desde grid.RiverCenterlinesCellSpace.
    /// Prep de centerline + ribbon con joins miter limitados; sin geometría desde máscara visual.
    /// </summary>
    public static class RiverSurfaceMeshBuilder
    {
        public static int LastMeshCount { get; private set; }
        public static int LastVertexSum { get; private set; }
        public static int LastTriSum { get; private set; }
        public static float LastMainRiverAvgHalfWidthWorld { get; private set; }
        static readonly List<float> s_tributaryAvgHalfWidthWorld = new List<float>();
        public static int DetachedRiverSurfaceSkips { get; private set; }
        public static int ShortRiverSurfaceSkips { get; private set; }
        public static int RiverSurfaceFragmentCullCount { get; private set; }

        const float DedupeCellEps = 1e-4f;
        const float MinCenterlineSpacingCells = 0.48f;
        const float MinSegmentCellEps = 1e-5f;
        const float CollinearDotThreshold = 0.998f;
        const float JoinAngleSmoothDeg = 22f;
        const float JoinAngleHardDeg = 52f;
        const float MiterLimitMul = 1.25f;
        const float AlignmentMaxDistCells = 2.25f;
        const float ChaikinMaxRiverDistCells = 1.35f;
        const float VisualCenterlineMaxInputMul = 2.25f;
        const float MaxSmoothDeviationCellsDefault = 0.5f;
        const int CrossSectionVertexCount = 5;
        /// <summary>Debe coincidir con lakePointCount en RebuildWebFusionTributaryLakeMouthNearShore.</summary>
        const int WebFusionLakeMouthSinkVertexCount = 5;
        /// <summary>Vértices de aproximación tierra→orilla antes del tramo hundido en el lago.</summary>
        const int WebFusionLakeApproachVertexCount = 4;
        const float MaxWidthStepFracOfBase = 0.2f;

        struct MouthFusionMeshBuildHints
        {
            public bool Active;
            public bool TaperAtStart;
            public bool TaperAtEnd;
        }

        static MouthFusionMeshBuildHints s_mouthFusionMeshHints;

        struct RiverCenterlinePrepStats
        {
            public int RawPts;
            public int DedupPts;
            public int SimplifiedPts;
            public int ResampledPts;
            public int SmoothedPts;
            public int CornerDensePts;
            public float MaxDeviationCells;
        }

        struct RiverJoinStats
        {
            public int Total;
            public int Smooth;
            public int Medium;
            public int Hard;
            public int MiterRejected;
            public float MaxMiterRatio;
        }

        struct RiverSplineBuildStats
        {
            public int RawPts;
            public int SimplifiedPts;
            public int SplinePts;
            public int AnchorsHard;
            public int FordAnchors;
            public int HardBendAnchorCount;
            public int Attempts;
            public float MaxDeviationCells;
            public float MaxActualDeviationCells;
            public float AvgDeviationCells;
            public float MaxAngleStepDeg;
            public bool EndpointStartAtBorder;
            public bool EndpointEndAtBorder;
            public bool BorderExtensionApplied;
            public bool SelfIntersectionDetected;
            public bool Accepted;
            public bool FallbackUsed;
            public string FallbackReason;
        }

        struct MainRiverCorridorSampler
        {
            public List<Vector2> Line;
            public float RadiusCells;
            public float RadiusSq;
            public float CoreRadiusCells;
            public float CoreRadiusSq;
        }

        static Material s_cachedRiverSurfaceMaterial;
        static Shader s_cachedRiverSurfaceShader;
        static readonly List<RiverSurfaceNarrowSite> s_riverNarrowSites = new List<RiverSurfaceNarrowSite>();

        /// <summary>Cuellos detectados en el último <see cref="BuildRiverSurfaces"/> (troncal y afluentes).</summary>
        public static IReadOnlyList<RiverSurfaceNarrowSite> LastRiverNarrowSites => s_riverNarrowSites;

        /// <summary>Nodos de centerline (mundo) del último build; para gizmos amarillos estilo Pruebas.</summary>
        public static IReadOnlyList<Vector3> DebugRiverSurfaceCenterlineNodesWorld => s_debugCenterlineNodesWorld;

        static readonly List<Vector3> s_debugCenterlineNodesWorld = new List<Vector3>();
        static readonly List<Vector3> s_webFusionMainWorldCenters = new List<Vector3>();

        const int NarrowDetectWindow = 3;
        const float NarrowLocalRatioThreshold = 0.88f;
        const float NarrowStepRatioThreshold = 0.78f;
        const float NarrowSharpTurnDeg = 38f;
        const float NarrowFoldHalfWidthCellsMul = 1.05f;
        const float UwpMainShoreHalfWidthFloorMul = 0.97f;
        const float UwpTributaryConfluenceMeshBoostMul = 1.22f;
        /// <summary>Fracción máxima del path tributario usada como zona de ensanche (solo boca).</summary>
        const float UwpTributaryConfluenceMinPathFraction = 0.08f;

        static float ResolveUwpConfluenceApproachDistCells(List<Vector2> cellPath, float approachCellsConfig)
        {
            float pathLen = PolylineLengthCellSpace(cellPath);
            if (pathLen < 1e-4f)
                return Mathf.Clamp(approachCellsConfig, 4f, 8f);
            float dist = Mathf.Max(4f, approachCellsConfig);
            return Mathf.Clamp(dist, 4f, Mathf.Max(6f, pathLen * UwpTributaryConfluenceMinPathFraction));
        }

        static void ResolveUwpConfluenceBlendRange(
            List<Vector2> cellPath,
            bool fromStart,
            float approachDistCells,
            out int blendStart,
            out int blendEnd,
            out int bodyRefIdx)
        {
            int n = cellPath != null ? cellPath.Count : 0;
            blendStart = 0;
            blendEnd = 0;
            bodyRefIdx = 0;
            if (n < 2)
                return;

            if (fromStart)
            {
                blendStart = 0;
                blendEnd = 0;
                float dist = 0f;
                for (int i = 1; i < n && dist < approachDistCells; i++)
                {
                    dist += Vector2.Distance(cellPath[i - 1], cellPath[i]);
                    blendEnd = i;
                }
                bodyRefIdx = Mathf.Min(n - 1, blendEnd + 2);
            }
            else
            {
                blendEnd = n - 1;
                blendStart = n - 1;
                float dist = 0f;
                for (int i = n - 2; i >= 0 && dist < approachDistCells; i--)
                {
                    dist += Vector2.Distance(cellPath[i], cellPath[i + 1]);
                    blendStart = i;
                }
                bodyRefIdx = Mathf.Max(0, blendStart - 1);
            }
        }

        static float ConfluenceTaper01AlongPath(
            List<Vector2> cellPath,
            int index,
            int blendStart,
            int blendEnd,
            bool fromStart)
        {
            if (blendEnd <= blendStart)
                return 1f;

            float zoneLen = 0f;
            for (int j = blendStart; j < blendEnd; j++)
                zoneLen += Vector2.Distance(cellPath[j], cellPath[j + 1]);
            if (zoneLen < 1e-5f)
            {
                return fromStart
                    ? (blendEnd - index) / (float)(blendEnd - blendStart)
                    : (index - blendStart) / (float)(blendEnd - blendStart);
            }

            float along = 0f;
            if (fromStart)
            {
                for (int j = 0; j < index && j < blendEnd; j++)
                    along += Vector2.Distance(cellPath[j], cellPath[j + 1]);
                return 1f - Mathf.Clamp01(along / zoneLen);
            }

            for (int j = blendStart; j < index; j++)
                along += Vector2.Distance(cellPath[j], cellPath[j + 1]);
            return Mathf.Clamp01(along / zoneLen);
        }

        public static void ResetStats()
        {
            LastMeshCount = 0;
            LastVertexSum = 0;
            LastTriSum = 0;
            LastMainRiverAvgHalfWidthWorld = 0f;
            s_tributaryAvgHalfWidthWorld.Clear();
            s_riverNarrowSites.Clear();
            s_debugCenterlineNodesWorld.Clear();
            s_webFusionMainWorldCenters.Clear();
            WaterMeshBuilder.DebugRibbonPathPointsWorld.Clear();
            DetachedRiverSurfaceSkips = 0;
            ShortRiverSurfaceSkips = 0;
            RiverSurfaceFragmentCullCount = 0;
        }

        static void ApplyWebFusionWorMaterialOverrides(Material mat, MapGenConfig config)
        {
            if (mat == null || config == null)
                return;

            if (mat.HasProperty("_MouthFadeStrength"))
            {
                float mouthFade = WaterVisualPipelinePolicy.IsSplitLakeMsRiverMouthFusion(config) ? 0.14f : 0f;
                mat.SetFloat("_MouthFadeStrength", mouthFade);
            }
            if (mat.HasProperty("_CapFoamFadeDistance"))
                mat.SetFloat("_CapFoamFadeDistance", 0.1f);
            if (mat.HasProperty("_Alpha"))
                mat.SetFloat("_Alpha", 1f);
            if (mat.HasProperty("_Water_Depth"))
                mat.SetFloat("_Water_Depth", 0.48f);
            if (mat.HasProperty("_CenterDarkStrength"))
                mat.SetFloat("_CenterDarkStrength", 0.12f);
            if (mat.HasProperty("_ShoreBand"))
                mat.SetFloat("_ShoreBand", 0.18f);
            if (mat.HasProperty("_FlowStreakStrength"))
                mat.SetFloat("_FlowStreakStrength", 0.08f);

            mat.renderQueue = 3001;

            Color deep = config.riverWaterDeepColor;
            deep.r = Mathf.Min(deep.r, 0.06f);
            deep.g = Mathf.Max(deep.g, 0.28f);
            deep.b = Mathf.Max(deep.b, 0.52f);
            deep.a = 0.98f;
            Color shallow = config.riverWaterShallowColor;
            shallow.g = Mathf.Max(shallow.g, 0.58f);
            shallow.b = Mathf.Max(shallow.b, 0.78f);
            shallow.a = 1f;
            if (mat.HasProperty("_DeepColor"))
                mat.SetColor("_DeepColor", deep);
            if (mat.HasProperty("_ShoreColor"))
                mat.SetColor("_ShoreColor", shallow);
            if (mat.HasProperty("_Color_Deep"))
                mat.SetColor("_Color_Deep", deep);
            if (mat.HasProperty("_Color_Shallow"))
                mat.SetColor("_Color_Shallow", shallow);
        }

        static float ComputeWebFusionEndpointAlpha(
            int i,
            int n,
            bool lakeFadeAtEnd,
            bool lakeFadeAtStart,
            bool mainAtEnd,
            bool mainAtStart,
            float minAlpha,
            int mouthBlend,
            MapGenConfig config = null)
        {
            mouthBlend = Mathf.Clamp(mouthBlend, 3, Mathf.Max(3, n - 1));
            bool mouthFusion = WaterVisualPipelinePolicy.IsSplitLakeMsRiverMouthFusion(config);
            float minA = mouthFusion
                ? Mathf.Clamp01(minAlpha)
                : Mathf.Clamp01(Mathf.Max(minAlpha, 0.32f));
            if (lakeFadeAtEnd && i >= n - mouthBlend)
            {
                float t = (i - (n - mouthBlend)) / (float)Mathf.Max(1, mouthBlend - 1);
                float alpha = Mathf.Lerp(minA, 1f, Mathf.SmoothStep(0f, 1f, t));
                if (mouthFusion && i == n - 1)
                    alpha = minA;
                return alpha;
            }

            if (lakeFadeAtStart && i < mouthBlend)
            {
                float t = (mouthBlend - 1 - i) / (float)Mathf.Max(1, mouthBlend - 1);
                float alpha = Mathf.Lerp(minA, 1f, Mathf.SmoothStep(0f, 1f, t));
                if (mouthFusion && i == 0)
                    alpha = minA;
                return alpha;
            }

            if (mainAtEnd && i >= n - mouthBlend)
            {
                if (mouthFusion)
                    return 1f;
                float confMin = Mathf.Max(minAlpha, 0.16f);
                float t = (i - (n - mouthBlend)) / (float)Mathf.Max(1, mouthBlend - 1);
                return Mathf.Lerp(confMin, 1f, Mathf.SmoothStep(0f, 1f, t));
            }

            if (mainAtStart && i < mouthBlend)
            {
                if (mouthFusion)
                    return 1f;
                float confMin = Mathf.Max(minAlpha, 0.16f);
                float t = (mouthBlend - 1 - i) / (float)Mathf.Max(1, mouthBlend - 1);
                return Mathf.Lerp(confMin, 1f, Mathf.SmoothStep(0f, 1f, t));
            }

            return 1f;
        }

        static void ResolveWebFusionTributaryEndpointFadeFlags(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> cellSpaceLine,
            int riverIndex,
            float cellSizeWorld,
            out bool lakeFadeAtEnd,
            out bool lakeFadeAtStart,
            out bool mainAtEnd,
            out bool mainAtStart)
        {
            lakeFadeAtEnd = false;
            lakeFadeAtStart = false;
            mainAtEnd = false;
            mainAtStart = false;
            if (grid == null || cellSpaceLine == null || cellSpaceLine.Count < 2 || riverIndex <= 0)
                return;

            int last = cellSpaceLine.Count - 1;
            bool startNearMain = IsTributaryEndpointNearMain(grid, config, cellSizeWorld, cellSpaceLine, 0);
            bool endNearMain = IsTributaryEndpointNearMain(grid, config, cellSizeWorld, cellSpaceLine, last);
            bool startNearLake = IsTributaryEndpointNearLake(grid, cellSpaceLine, 0);
            bool endNearLake = IsTributaryEndpointNearLake(grid, cellSpaceLine, last);

            if (IsLakeEmissaryRiverIndex(grid, riverIndex) ||
                IsLakeEmissaryCenterline(grid, cellSpaceLine, riverIndex))
            {
                if (startNearLake && !startNearMain)
                    lakeFadeAtStart = true;
                if (endNearLake && !endNearMain)
                    lakeFadeAtEnd = true;
            }
            else
            {
                if (endNearLake && !endNearMain)
                    lakeFadeAtEnd = true;
                if (startNearLake && !startNearMain)
                    lakeFadeAtStart = true;
            }

            if (endNearMain && !endNearLake)
                mainAtEnd = true;
            if (startNearMain && !startNearLake)
                mainAtStart = true;

            if (mainAtStart && lakeFadeAtStart)
                lakeFadeAtStart = false;
            if (mainAtEnd && lakeFadeAtEnd)
                lakeFadeAtEnd = false;

            // MouthFusion: fusión por recorte geométrico + perfil Y; sin fade alpha en boca lago.
            if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverMouthFusion(config))
            {
                lakeFadeAtStart = false;
                lakeFadeAtEnd = false;
            }
        }

        static int ResolveWebFusionTributaryMouthBlendVerts(int n, MapGenConfig config)
        {
            if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverMouthFusion(config))
                return Mathf.Clamp(WebFusionLakeMouthSinkVertexCount, 3, Mathf.Max(3, n - 1));
            return Mathf.Clamp(4, 3, Mathf.Max(3, n - 1));
        }

        static Material TryCreateWebFusionWorRiverMaterial(MapGenConfig config)
        {
            if (config == null || !WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                return null;

            Shader worShader = Shader.Find("Project/WOR River Water");
            if (worShader == null)
                return null;

            var worInst = new Material(worShader);
            WaterStylizedIntegration.ApplyStylizedRiverMaterialRuntime(
                worInst, config, WaterMaterialRuntimeMode.WORCustomShader);
            ApplyWebFusionWorMaterialOverrides(worInst, config);
            return worInst;
        }

        public static Material GetRiverSurfaceMaterial(MapGenConfig config, Material waterFallback, int riverIndex = 0)
        {
            bool forceFlat = config != null &&
                (config.riverSurfaceDebugForceUnlitFlat || config.riverSurfaceDebugFlatMaterial || config.riverSurfaceDebugShowWire);
            if (config != null && forceFlat)
            {
                Shader sd = Shader.Find("Sprites/Default");
                if (sd == null)
                    sd = Shader.Find("UI/Default");
                if (sd == null)
                    sd = Shader.Find("Universal Render Pipeline/Unlit");
                var m = new Material(sd);
                if (m.HasProperty("_Color"))
                    m.SetColor("_Color", new Color(0.2f, 0.45f, 1f, 0.65f));
                else if (m.HasProperty("_BaseColor"))
                    m.SetColor("_BaseColor", new Color(0.2f, 0.45f, 1f, 0.65f));
                m.renderQueue = 3000;
                if (config.debugLogs || config.debugHydrologyNetwork)
                {
                    Debug.Log(
                        $"[RiverSurfaceMaterialDebug] flat=1 material={(m.shader != null ? m.shader.name : "null")} " +
                        $"forceUnlitFlat={(config.riverSurfaceDebugForceUnlitFlat ? 1 : 0)}");
                }

                return m;
            }

            Material sourceMaterial = waterFallback;
            WaterMaterialRuntimeMode materialMode = WaterMaterialRuntimeMode.SW2ProceduralTranslator;
            if (config != null)
            {
                bool useTributary = riverIndex > 0 && config.tributaryWaterMaterial != null;
                sourceMaterial = useTributary ? config.tributaryWaterMaterial : (config.riverWaterMaterial != null ? config.riverWaterMaterial : waterFallback);
                materialMode = useTributary ? config.tributaryWaterMaterialMode : config.riverWaterMaterialMode;
            }

            if (config != null &&
                WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
            {
                bool directAsset = materialMode == WaterMaterialRuntimeMode.DirectAsset && sourceMaterial != null;
                if (!directAsset)
                {
                    Material worMat = TryCreateWebFusionWorRiverMaterial(config);
                    if (worMat != null)
                        return worMat;
                }
            }

            if (sourceMaterial != null)
            {
                var inst = new Material(sourceMaterial);
                if (config != null)
                    WaterStylizedIntegration.ApplyStylizedRiverMaterialRuntime(inst, config, materialMode);
                if (config != null &&
                    WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config) &&
                    materialMode != WaterMaterialRuntimeMode.DirectAsset)
                    ApplyWebFusionWorMaterialOverrides(inst, config);
                if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
                {
                    float flowLog = inst.HasProperty("_FlowSpeed") ? inst.GetFloat("_FlowSpeed") : -1f;
                    Debug.Log(
                        $"[RiverSurfaceMaterial] riverId={riverIndex} material={inst.name} " +
                        $"shader={(inst.shader != null ? inst.shader.name : "null")} source={(riverIndex > 0 ? "tributaryWaterMaterial" : "riverWaterMaterial")} " +
                        $"mode={materialMode} createdNewMaterial=1 flowSpeed={flowLog:F3}");
                }

                return inst;
            }

            Shader sh = Shader.Find("Project/River Water Simple");
            if (sh != null)
            {
                int createdNew = 0;
                if (s_cachedRiverSurfaceMaterial == null || s_cachedRiverSurfaceShader != sh)
                {
                    s_cachedRiverSurfaceMaterial = new Material(sh);
                    s_cachedRiverSurfaceShader = sh;
                    createdNew = 1;
                }

                if (s_cachedRiverSurfaceMaterial.HasProperty("_BaseColor"))
                    s_cachedRiverSurfaceMaterial.SetColor("_BaseColor", new Color(0.25f, 0.55f, 0.88f, 0.82f));
                if (config != null)
                {
                    if (s_cachedRiverSurfaceMaterial.HasProperty("_Alpha"))
                        s_cachedRiverSurfaceMaterial.SetFloat("_Alpha", Mathf.Clamp01(config.riverWaterAlpha));
                    if (s_cachedRiverSurfaceMaterial.HasProperty("_ScrollV"))
                        s_cachedRiverSurfaceMaterial.SetFloat("_ScrollV", Mathf.Clamp(config.waterUvFlowSpeedScale * 0.32f, 0.04f, 0.85f));
                    if (s_cachedRiverSurfaceMaterial.HasProperty("_RippleStrength"))
                        s_cachedRiverSurfaceMaterial.SetFloat("_RippleStrength", Mathf.Clamp(config.waterDepthColorStrength * 0.22f, 0.06f, 0.22f));
                    if (s_cachedRiverSurfaceMaterial.HasProperty("_FoamStrength"))
                        s_cachedRiverSurfaceMaterial.SetFloat("_FoamStrength", Mathf.Clamp(config.riverBankBlendStrength * 0.35f, 0f, 0.12f));
                }
                if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
                {
                    Debug.Log(
                        $"[RiverSurfaceMaterial] riverId=-1 material={s_cachedRiverSurfaceMaterial.name} " +
                        $"shader={sh.name} createdNewMaterial={createdNew}");
                }

                return s_cachedRiverSurfaceMaterial;
            }

            if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
            {
                Debug.Log(
                    $"[RiverSurfaceMaterial] riverId={riverIndex} material={(sourceMaterial != null ? sourceMaterial.name : "null")} " +
                    $"shader={(waterFallback != null && waterFallback.shader != null ? waterFallback.shader.name : "null")} createdNewMaterial=0");
            }

            return waterFallback;
        }

        static void LogRiverGeometryHistoryAudit(MapGenConfig config)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                "[RiverGeometryHistoryAudit] oldCommit=99277d4/56ade5 oldMethod=TryBuildRiverRibbonStripMesh+TangentAt+halfPerVertex " +
                "currentMethod=BuildOrganicVisualRiverCenterline+CrossSectionMesh " +
                "takeFromOld=tangent_avg_prev_next,ribbon_spacing,resample,1-edge-smooth,width_sine_perlin_clamped " +
                "discardFromOld=visual_mask_geometry,2vert_strip_bevel_caps,miter_fans,meander,border_extension");
        }

        static bool IsPointNearFord(GridSystem grid, Vector2 cellPt, int fordDistCells)
        {
            if (grid == null)
                return false;
            int cx = Mathf.Clamp(Mathf.FloorToInt(cellPt.x), 0, grid.Width - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt(cellPt.y), 0, grid.Height - 1);
            return WaterMeshBuilder.GridCellNearFordRiverChebyshev(grid, cx, cy, fordDistCells);
        }

        static List<Vector2> RemoveCollinearPointsCellFordAware(List<Vector2> pts, GridSystem grid, MapGenConfig config)
        {
            if (pts == null || pts.Count < 3)
                return pts;
            int fordD = Mathf.Max(1, config != null ? config.riverVisualFordKeepDistanceCells : 5);
            var r = new List<Vector2>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                if (IsPointNearFord(grid, pts[i], fordD) || i <= 1 || i >= pts.Count - 2)
                {
                    r.Add(pts[i]);
                    continue;
                }

                Vector2 a = r[r.Count - 1];
                Vector2 b = pts[i];
                Vector2 c = pts[i + 1];
                Vector2 d0 = b - a;
                Vector2 d1 = c - b;
                if (d0.sqrMagnitude < 1e-12f || d1.sqrMagnitude < 1e-12f)
                {
                    r.Add(b);
                    continue;
                }

                float dot = Mathf.Clamp(Vector2.Dot(d0.normalized, d1.normalized), -1f, 1f);
                if (dot > CollinearDotThreshold)
                    continue;
                r.Add(b);
            }

            r.Add(pts[pts.Count - 1]);
            return r;
        }

        static List<Vector2> ChaikinOpenCellPreserveEnds(List<Vector2> pts, int passes)
        {
            var cur = new List<Vector2>(pts);
            for (int p = 0; p < passes && cur.Count >= 3; p++)
            {
                Vector2 first = cur[0];
                Vector2 last = cur[cur.Count - 1];
                var next = new List<Vector2>(cur.Count * 2) { first };
                for (int i = 0; i < cur.Count - 1; i++)
                {
                    Vector2 a = cur[i];
                    Vector2 b = cur[i + 1];
                    next.Add(0.75f * a + 0.25f * b);
                    next.Add(0.25f * a + 0.75f * b);
                }

                next.Add(last);
                cur = next;
            }

            return cur;
        }

        static void ApplyMinimalBorderExtensionCell(List<Vector2> pts, int w, int h, float maxCells)
        {
            if (pts == null || pts.Count < 2 || maxCells < 1e-4f)
                return;
            void ExtendEnd(int idx, int innerIdx)
            {
                if (!IsTrueMapEdgeCellSpace(pts[idx], w, h))
                    return;
                Vector2 dir = pts[idx] - pts[innerIdx];
                if (dir.sqrMagnitude < 1e-10f)
                    return;
                dir.Normalize();
                pts[idx] = pts[idx] + dir * maxCells;
            }

            ExtendEnd(0, 1);
            ExtendEnd(pts.Count - 1, pts.Count - 2);
        }

        static float InteriorTurnAngleDeg(List<Vector2> pts, int i)
        {
            if (pts == null || pts.Count < 3 || i <= 0 || i >= pts.Count - 1)
                return 0f;
            Vector2 a = pts[i] - pts[i - 1];
            Vector2 b = pts[i + 1] - pts[i];
            if (a.sqrMagnitude < 1e-12f || b.sqrMagnitude < 1e-12f)
                return 0f;
            return Vector2.Angle(a, b);
        }

        static void CollectRiverNarrowSites(
            int riverIndex,
            List<Vector2> cellPath,
            List<float> halfWidths,
            GridSystem grid,
            MapGenConfig config,
            float cellSizeWorld)
        {
            if (cellPath == null || halfWidths == null || cellPath.Count != halfWidths.Count)
                return;

            int n = cellPath.Count;
            if (n < NarrowDetectWindow * 2 + 3)
                return;

            int fordD = config != null ? Mathf.Max(1, config.riverVisualFordKeepDistanceCells) : 5;
            float foldHalfMin = Mathf.Max(0.5f, cellSizeWorld * NarrowFoldHalfWidthCellsMul);
            bool log = config != null && (config.debugLogs || config.debugHydrologyNetwork);

            for (int i = NarrowDetectWindow; i < n - NarrowDetectWindow; i++)
            {
                float hw = halfWidths[i];
                float sum = 0f;
                int count = 0;
                bool isLocalMin = true;
                for (int j = i - NarrowDetectWindow; j <= i + NarrowDetectWindow; j++)
                {
                    float wj = halfWidths[j];
                    sum += wj;
                    count++;
                    if (j != i && wj < hw - 1e-4f)
                        isLocalMin = false;
                }

                float localAvg = count > 0 ? sum / count : hw;
                float ratio = localAvg > 1e-5f ? hw / localAvg : 1f;
                float turn = InteriorTurnAngleDeg(cellPath, i);
                bool nearFord = grid != null && IsPointNearFord(grid, cellPath[i], fordD);

                int kind = 0;
                if (isLocalMin && ratio <= NarrowLocalRatioThreshold)
                    kind |= 1;
                if (turn >= NarrowSharpTurnDeg && hw >= foldHalfMin)
                    kind |= 2;

                bool stepDip = i > 0 && i < n - 1 &&
                    hw < halfWidths[i - 1] * NarrowStepRatioThreshold &&
                    hw < halfWidths[i + 1] * NarrowStepRatioThreshold;
                if (stepDip)
                    kind |= 1;

                if (kind == 0)
                    continue;

                var site = new RiverSurfaceNarrowSite(
                    riverIndex,
                    i,
                    cellPath[i],
                    hw,
                    localAvg,
                    ratio,
                    turn,
                    nearFord,
                    kind);

                if (ShouldSkipNearDuplicateNarrow(site))
                    continue;

                s_riverNarrowSites.Add(site);
                if (log)
                {
                    Debug.Log(
                        $"[RiverSurfaceNarrowSite] riverId={riverIndex} idx={i} cell=({cellPath[i].x:F1},{cellPath[i].y:F1}) " +
                        $"halfW={hw:F2} localAvg={localAvg:F2} ratio={ratio:F2} turnDeg={turn:F1} nearFord={(nearFord ? 1 : 0)} " +
                        $"kindFlags={kind} widthDip={(kind & 1) != 0} bendFold={(kind & 2) != 0}");
                }
            }
        }

        static bool ShouldSkipNearDuplicateNarrow(RiverSurfaceNarrowSite site)
        {
            const float mergeCells = 2.5f;
            for (int k = s_riverNarrowSites.Count - 1; k >= 0; k--)
            {
                RiverSurfaceNarrowSite prev = s_riverNarrowSites[k];
                if (prev.RiverIndex != site.RiverIndex)
                    continue;
                if (Vector2.Distance(prev.Cell, site.Cell) < mergeCells)
                    return true;
            }

            return false;
        }

        /// <summary>Cuellos del troncal (riverId=0) para spawn de cascada/decor.</summary>
        public static bool TryGetMainRiverNarrowSites(List<RiverSurfaceNarrowSite> buffer)
        {
            if (buffer == null)
                return false;
            buffer.Clear();
            for (int i = 0; i < s_riverNarrowSites.Count; i++)
            {
                if (s_riverNarrowSites[i].RiverIndex == 0)
                    buffer.Add(s_riverNarrowSites[i]);
            }

            return buffer.Count > 0;
        }

        /// <summary>UWP: sube cuellos locales del troncal para que la malla intersecte el carve (foam blanca).</summary>
        static void ApplyUwpMainRiverShoreIntersectionRepair(
            List<float> halfWidths,
            List<Vector2> cellPath,
            float baseHalfW,
            MapGenConfig config)
        {
            if (config == null || !config.uwpOwnedVisualPolicy || halfWidths == null || cellPath == null)
                return;
            int n = Mathf.Min(halfWidths.Count, cellPath.Count);
            if (n < 8 || baseHalfW < 1e-5f)
                return;

            int win = NarrowDetectWindow;
            float absFloor = baseHalfW * UwpMainShoreHalfWidthFloorMul;
            for (int i = win; i < n - win; i++)
            {
                float hw = halfWidths[i];
                float sum = 0f;
                for (int j = i - win; j <= i + win; j++)
                    sum += halfWidths[j];
                float localAvg = sum / (win * 2 + 1);
                if (hw >= absFloor && hw >= localAvg * 0.90f)
                    continue;
                halfWidths[i] = Mathf.Max(hw, absFloor, localAvg * 0.94f);
            }
        }

        static bool TryFindTributaryMainJoinIndex(
            GridSystem grid,
            List<Vector2> cellPath,
            bool fromEnd,
            MapGenConfig config,
            int riverIndex,
            out int joinIdx)
        {
            joinIdx = fromEnd ? cellPath.Count - 1 : 0;
            if (grid == null || cellPath == null || cellPath.Count < 2)
                return false;

            if (config != null && config.uwpOwnedVisualPolicy && riverIndex > 0)
            {
                if (!TryResolveTributaryMainJoinEndpointIndex(grid, config, cellPath, riverIndex, out joinIdx))
                    return false;
                return fromEnd ? joinIdx == cellPath.Count - 1 : joinIdx == 0;
            }

            if (fromEnd)
            {
                for (int i = cellPath.Count - 1; i >= 0; i--)
                {
                    int cx = Mathf.RoundToInt(cellPath[i].x);
                    int cz = Mathf.RoundToInt(cellPath[i].y);
                    if (grid.InBoundsCell(cx, cz) && grid.GetCell(cx, cz).type == CellType.River)
                    {
                        joinIdx = i;
                        return true;
                    }
                }
            }
            else
            {
                for (int i = 0; i < cellPath.Count; i++)
                {
                    int cx = Mathf.RoundToInt(cellPath[i].x);
                    int cz = Mathf.RoundToInt(cellPath[i].y);
                    if (grid.InBoundsCell(cx, cz) && grid.GetCell(cx, cz).type == CellType.River)
                    {
                        joinIdx = i;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>UWP frozen: joinIdx desde FinalCenterlineCells (no CellType.River ni centerline funcional).</summary>
        static bool TryResolveTributaryJoinIndexFromFinalPath(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> finalPath,
            int riverIndex,
            out int joinIdx)
        {
            joinIdx = finalPath != null && finalPath.Count > 0 ? finalPath.Count - 1 : 0;
            if (grid == null || config == null || finalPath == null || finalPath.Count < 2 || riverIndex <= 0)
                return false;
            if (TryResolveTributaryMainJoinEndpointIndex(grid, config, finalPath, riverIndex, out joinIdx))
                return true;
            joinIdx = finalPath.Count - 1;
            return false;
        }

        static bool TryGetTributaryMainConfluenceApproachFromFinalPath(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> finalPath,
            int riverIndex,
            out Vector2 approach,
            out int joinIdx)
        {
            approach = Vector2.right;
            joinIdx = -1;
            if (grid == null || config == null || finalPath == null || finalPath.Count < 2 || riverIndex <= 0)
                return false;

            if (!TryResolveTributaryJoinIndexFromFinalPath(grid, config, finalPath, riverIndex, out joinIdx))
                joinIdx = finalPath.Count - 1;

            int tcl = joinIdx <= 0
                ? 1
                : Mathf.Clamp(joinIdx, 1, finalPath.Count - 1);
            approach = RiverDendriticUtility.TributaryIncomingAt(finalPath, tcl);
            if (joinIdx <= 0 && approach.sqrMagnitude > 1e-6f)
                approach = -approach;
            if (approach.sqrMagnitude < 1e-6f)
            {
                int prev = Mathf.Max(0, joinIdx - 1);
                approach = finalPath[joinIdx] - finalPath[prev];
                if (joinIdx <= 0 && approach.sqrMagnitude > 1e-6f)
                    approach = -approach;
            }

            if (approach.sqrMagnitude < 1e-6f)
                return false;
            approach.Normalize();
            return true;
        }

        /// <summary>Vector de aproximación al troncal usando centerline funcional (tributario skipped, sin FinalCenterlineCells).</summary>
        static bool TryGetSkippedTributaryConfluenceApproachFromFunctionalCenterline(
            GridSystem grid,
            int tribIdx,
            Vector2Int confluenceCell,
            out Vector2 approach)
        {
            approach = Vector2.right;
            if (grid?.RiverCenterlinesCellSpace == null ||
                tribIdx <= 0 ||
                tribIdx >= grid.RiverCenterlinesCellSpace.Count)
                return false;

            var line = grid.RiverCenterlinesCellSpace[tribIdx];
            if (line == null || line.Count < 2)
                return false;

            Vector2 conf = new Vector2(confluenceCell.x + 0.5f, confluenceCell.y + 0.5f);
            int joinIdx = 0;
            float bestSq = float.MaxValue;
            for (int i = 0; i < line.Count; i++)
            {
                float d = (line[i] - conf).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    joinIdx = i;
                }
            }

            int tcl = joinIdx <= 0
                ? 1
                : Mathf.Clamp(joinIdx, 1, line.Count - 1);
            approach = RiverDendriticUtility.TributaryIncomingAt(line, tcl);
            if (joinIdx <= 0 && approach.sqrMagnitude > 1e-6f)
                approach = -approach;
            if (approach.sqrMagnitude < 1e-6f)
            {
                int prev = Mathf.Max(0, joinIdx - 1);
                approach = line[joinIdx] - line[prev];
                if (joinIdx <= 0 && approach.sqrMagnitude > 1e-6f)
                    approach = -approach;
            }

            if (approach.sqrMagnitude < 1e-6f)
                return false;
            approach.Normalize();
            return true;
        }

        static void ApplyTributaryConfluenceEndHalfWidths(
            GridSystem grid,
            List<Vector2> cellPath,
            List<float> halfWidths,
            MapGenConfig config,
            int endpointIndex,
            bool fromStart)
        {
            int n = Mathf.Min(cellPath.Count, halfWidths.Count);
            if (n < 3 || endpointIndex < 0 || endpointIndex >= n)
                return;

            int endpoint = fromStart ? 0 : n - 1;
            if (endpointIndex != endpoint)
                return;

            float approachCfg = config.riverSurfaceTributaryWidthFixEnabled
                ? Mathf.Clamp(config.riverSurfaceTributaryConfluenceApproachCells, 10, 28)
                : Mathf.Clamp(config.riverConfluenceVisualBlendLengthCells, 6, 18);
            float approachDist = ResolveUwpConfluenceApproachDistCells(cellPath, approachCfg);
            ResolveUwpConfluenceBlendRange(cellPath, fromStart, approachDist, out int blendStart, out int blendEnd, out int bodyRefIdx);
            float bodyHalf = halfWidths[Mathf.Clamp(bodyRefIdx, 0, n - 1)];

            float cs = Mathf.Max(0.01f, grid.CellSizeWorld);
            float mainHalf = config.riverVisualRibbonFullWidthCellsMain * 0.5f * cs;
            float endMul = Mathf.Clamp(config.riverConfluenceTributaryEndWidthMul, 0.42f, 1.12f);
            float joinHalf = mainHalf * endMul;

            for (int i = blendStart; i <= blendEnd; i++)
            {
                float t = ConfluenceTaper01AlongPath(cellPath, i, blendStart, blendEnd, fromStart);
                float target = Mathf.Lerp(bodyHalf, joinHalf, Mathf.SmoothStep(0f, 1f, t));
                halfWidths[i] = Mathf.Max(halfWidths[i], target);
            }
        }

        /// <summary>Tras meshMul: taper gradual hacia boca ≥ ancho mesh troncal (sin salto plano).</summary>
        static void ApplyTributaryConfluenceExtraMeshWidth(
            GridSystem grid,
            List<Vector2> cellPath,
            List<float> halfWidths,
            MapGenConfig config,
            int riverIndex,
            float cellSize,
            bool forCarveMask = false)
        {
            if (config == null || !config.uwpOwnedVisualPolicy || riverIndex <= 0 ||
                cellPath == null || halfWidths == null || grid == null)
                return;
            int n = Mathf.Min(cellPath.Count, halfWidths.Count);
            if (n < 4)
                return;

            float mainHalf = config.riverVisualRibbonFullWidthCellsMain * 0.5f * cellSize;
            float mainMeshHalf = mainHalf * Mathf.Clamp(config.riverSurfaceMainMeshOnlyWidthMul, 1f, 2.5f);
            float endMul = Mathf.Clamp(config.riverConfluenceTributaryEndWidthMul, 0.42f, 1.22f);
            float lakeFirstMul = config.uwpLakeFirstHydrologyPipeline ? 1.2f : 1f;
            if (config.uwpLakeFirstHydrologyPipeline &&
                UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex) &&
                !forCarveMask)
                lakeFirstMul = 1.06f;
            float targetJoin = mainHalf * endMul * UwpTributaryConfluenceMeshBoostMul * lakeFirstMul;
            targetJoin = Mathf.Min(targetJoin, mainMeshHalf * endMul * (config.uwpLakeFirstHydrologyPipeline ? 1.16f : 1.08f));

            void TaperAtEndpoint(int endpoint, bool forceApproach = false)
            {
                if (endpoint < 0 || endpoint >= n)
                    return;
                if (!forceApproach && !IsTributaryEndpointNearMain(grid, config, cellSize, cellPath, endpoint))
                    return;

                bool atStart = endpoint == 0;
                float approachCfg = Mathf.Clamp(config.riverSurfaceTributaryConfluenceApproachCells, 10, 28);
                float approachDist = ResolveUwpConfluenceApproachDistCells(cellPath, approachCfg);
                ResolveUwpConfluenceBlendRange(cellPath, atStart, approachDist, out int blendStart, out int blendEnd, out int bodyRef);
                float bodyW = halfWidths[Mathf.Clamp(bodyRef, 0, n - 1)];

                for (int i = blendStart; i <= blendEnd; i++)
                {
                    float t = ConfluenceTaper01AlongPath(cellPath, i, blendStart, blendEnd, atStart);
                    float desired = Mathf.Lerp(bodyW, targetJoin, Mathf.SmoothStep(0f, 1f, t));
                    halfWidths[i] = Mathf.Max(halfWidths[i], desired);
                }
            }

            bool forceInland = UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex);
            bool headwater = UwpTributaryOriginUtility.GetOrigin(grid, riverIndex) == UwpTributaryOriginKind.HeadwaterFeeder;
            // Mesh y carve headwater→receptor: ensanchar en unión (antes carveMask early-out → isla).
            if (headwater)
            {
                ApplyLakeFirstHeadwaterReceiverJoinMeshWiden(
                    grid, halfWidths, cellPath, config, riverIndex, cellSize);
                return;
            }

            if (TryResolveTributaryMainJoinEndpointIndex(grid, config, cellPath, riverIndex, out int joinEp))
                TaperAtEndpoint(joinEp, forceInland);
            else
            {
                TaperAtEndpoint(n - 1, forceInland);
                TaperAtEndpoint(0, forceInland);
            }
        }

        static void FillRiverSurfaceVertexColors(
            List<Color> colors,
            Material mat,
            List<Vector2> cellSpaceLine,
            int crossSectionCount,
            int vertsPerCrossSection,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex = 0)
        {
            if (colors == null || mat == null || crossSectionCount < 1 || vertsPerCrossSection < 1)
                return;

            bool isWor = WaterMeshBuilder.IsWorWaterMaterial(mat);
            bool webFusion = config != null && WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config);
            int webFusionMouthBlend = webFusion
                ? Mathf.Clamp(config.lakeRiverMouthBlendCells + 3, 4, 14)
                : 0;
            bool useCurveFoam = cellSpaceLine != null && cellSpaceLine.Count == crossSectionCount;
            int confBlend = config != null
                ? Mathf.Clamp(
                    config.riverSurfaceTributaryWidthFixEnabled
                        ? config.riverSurfaceTributaryConfluenceApproachCells
                        : config.riverConfluenceVisualBlendLengthCells,
                    3,
                    14)
                : 6;

            for (int i = 0; i < crossSectionCount; i++)
            {
                float curveFoam = useCurveFoam
                    ? WaterStylizedIntegration.ComputeRiverCurveShoreFoam01(InteriorTurnAngleDeg(cellSpaceLine, i))
                    : 0f;
                float mouthFoam = 0f;
                float confluenceFoam = 0f;
                if (isWor && useCurveFoam && grid != null && config != null)
                {
                    int gx = Mathf.FloorToInt(cellSpaceLine[i].x);
                    int gz = Mathf.FloorToInt(cellSpaceLine[i].y);
                    if (grid.InBoundsCell(gx, gz))
                    {
                        Vector3 world = grid.CellToWorldCenter(gx, gz);
                        mouthFoam = WaterMeshBuilder.SampleLakeMouthProximity01(world, grid, config);
                        if (webFusion)
                        {
                            bool inMouth = i < webFusionMouthBlend || i >= crossSectionCount - webFusionMouthBlend;
                            if (!inMouth)
                                mouthFoam = 0f;
                        }
                    }

                    if (riverIndex > 0)
                    {
                        int distJoin = crossSectionCount - 1 - i;
                        if (distJoin >= 0 && distJoin < confBlend)
                            confluenceFoam = Mathf.SmoothStep(0f, 1f, 1f - distJoin / Mathf.Max(1f, confBlend - 1f));
                    }
                }

                for (int v = 0; v < vertsPerCrossSection; v++)
                {
                    float shoreW = WaterStylizedIntegration.GetRiverShoreAcrossWidth01(v, vertsPerCrossSection);
                    if (isWor)
                    {
                        float mouthG = mouthFoam * Mathf.Lerp(0.35f, 1f, 1f - shoreW);
                        colors.Add(new Color(
                            1f - shoreW,
                            mouthG,
                            curveFoam,
                            confluenceFoam * 0.28f));
                    }
                    else
                        colors.Add(WaterStylizedIntegration.GetRiverVertexColor(mat, curveFoam, shoreW));
                }
            }
        }

        static List<Vector2> ResampleAdaptiveCell(List<Vector2> pts, float baseSpacing, float tightSpacing)
        {
            if (pts == null || pts.Count < 2)
                return pts;
            baseSpacing = Mathf.Clamp(baseSpacing, 0.5f, 1.25f);
            tightSpacing = Mathf.Clamp(tightSpacing, 0.3f, 0.65f);
            var result = new List<Vector2>(pts.Count * 3) { pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float ang = i > 0 ? InteriorTurnAngleDeg(pts, i) : InteriorTurnAngleDeg(pts, i + 1);
                float step = ang >= JoinAngleHardDeg ? tightSpacing : (ang >= JoinAngleSmoothDeg ? Mathf.Lerp(baseSpacing, tightSpacing, 0.5f) : baseSpacing);
                float segLen = Vector2.Distance(pts[i], pts[i + 1]);
                int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / Mathf.Max(0.2f, step)));
                for (int s = 1; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    Vector2 p = Vector2.Lerp(pts[i], pts[i + 1], t);
                    if ((p - result[result.Count - 1]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                        result.Add(p);
                }
            }

            return result.Count >= 2 ? result : pts;
        }

        static List<Vector2> DensifyHardCornersCell(List<Vector2> pts, float tightSpacing)
        {
            if (pts == null || pts.Count < 3)
                return pts;
            var result = new List<Vector2>(pts.Count * 2) { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                float ang = InteriorTurnAngleDeg(pts, i);
                if (ang >= JoinAngleHardDeg)
                {
                    float dBack = Vector2.Distance(pts[i - 1], pts[i]);
                    float dFwd = Vector2.Distance(pts[i], pts[i + 1]);
                    int stepsBack = Mathf.Max(0, Mathf.CeilToInt(dBack / tightSpacing) - 1);
                    int stepsFwd = Mathf.Max(0, Mathf.CeilToInt(dFwd / tightSpacing) - 1);
                    for (int s = stepsBack; s >= 1; s--)
                    {
                        Vector2 p = Vector2.Lerp(pts[i], pts[i - 1], s / (float)(stepsBack + 1));
                        if ((p - result[result.Count - 1]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                            result.Add(p);
                    }

                    if ((pts[i] - result[result.Count - 1]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                        result.Add(pts[i]);
                    for (int s = 1; s <= stepsFwd; s++)
                    {
                        Vector2 p = Vector2.Lerp(pts[i], pts[i + 1], s / (float)(stepsFwd + 1));
                        if ((p - result[result.Count - 1]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                            result.Add(p);
                    }
                }
                else
                {
                    if ((pts[i] - result[result.Count - 1]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                        result.Add(pts[i]);
                }
            }

            if ((pts[pts.Count - 1] - result[result.Count - 1]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                result.Add(pts[pts.Count - 1]);
            return result.Count >= 2 ? result : pts;
        }

        static Vector2 ClampPointToPlayableRect(Vector2 p, int width, int height)
        {
            float minX = 0.5f;
            float maxX = (width - 1) + 0.5f;
            float minZ = 0.5f;
            float maxZ = (height - 1) + 0.5f;
            return new Vector2(
                Mathf.Clamp(p.x, minX, maxX),
                Mathf.Clamp(p.y, minZ, maxZ));
        }

        static void ClampPolylinePlayableCellSpace(List<Vector2> pts, int width, int height)
        {
            if (pts == null)
                return;
            for (int i = 0; i < pts.Count; i++)
                pts[i] = ClampPointToPlayableRect(pts[i], width, height);
        }

        static int CountPointsOutsidePlayableRect(List<Vector2> pts, int width, int height)
        {
            if (pts == null)
                return 0;
            float minX = 0.5f;
            float maxX = (width - 1) + 0.5f;
            float minZ = 0.5f;
            float maxZ = (height - 1) + 0.5f;
            int outside = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 p = pts[i];
                if (p.x < minX - 1e-4f || p.x > maxX + 1e-4f || p.y < minZ - 1e-4f || p.y > maxZ + 1e-4f)
                    outside++;
            }

            return outside;
        }

        static bool MeasureVisualCenterlineRiverAlignment(
            GridSystem grid,
            List<Vector2> visualCells,
            out float maxDistToRiverCell,
            out int pointsFarFromRiver)
        {
            maxDistToRiverCell = 0f;
            pointsFarFromRiver = 0;
            if (grid == null || visualCells == null || visualCells.Count == 0)
                return false;
            int w = grid.Width;
            int h = grid.Height;
            for (int i = 0; i < visualCells.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(visualCells[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(visualCells[i].y), 0, h - 1);
                float d = DistanceToNearestRiverCellChebyshev(grid, cx, cy);
                maxDistToRiverCell = Mathf.Max(maxDistToRiverCell, d);
                if (d > AlignmentMaxDistCells)
                    pointsFarFromRiver++;
            }

            return maxDistToRiverCell <= AlignmentMaxDistCells && pointsFarFromRiver == 0;
        }

        static void LogRiverSurfaceAlignmentFix(
            MapGenConfig config,
            int riverId,
            int inputPts,
            int visualPts,
            float maxDist,
            int pointsFar,
            bool fallbackUsed,
            string fallbackReason)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverSurfaceAlignmentFix] riverId={riverId} input={inputPts} visual={visualPts} " +
                $"maxDistToRiverCell={maxDist:F2} pointsFarFromRiver={pointsFar} fallbackUsed={(fallbackUsed ? 1 : 0)} " +
                $"fallbackReason={(string.IsNullOrEmpty(fallbackReason) ? "none" : fallbackReason)}");
        }

        static void LogRiverSurfaceBorderPolicy(
            MapGenConfig config,
            int riverId,
            bool startAtBorder,
            bool endAtBorder,
            int vertsOutsideAfterClamp)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverSurfaceBorderPolicy] riverId={riverId} borderExtensionDisabled=1 " +
                $"startAtBorder={(startAtBorder ? 1 : 0)} endAtBorder={(endAtBorder ? 1 : 0)} " +
                $"vertsOutsideAfterClamp={vertsOutsideAfterClamp}");
        }

        static List<Vector2> TryChaikinNearRiverCells(GridSystem grid, List<Vector2> pts, MapGenConfig config = null)
        {
            if (pts == null || pts.Count < 3 || grid == null)
                return pts;
            bool uwp = config != null && config.uwpOwnedVisualPolicy;
            int passes = pts.Count >= 10 ? (uwp ? 2 : 2) : (uwp ? 2 : 1);
            var smoothed = ChaikinOpenCellPreserveEnds(pts, passes);
            if (PolylineSelfIntersectsXZCell(smoothed))
                return pts;
            int w = grid.Width;
            int h = grid.Height;
            float maxRiverDist = uwp ? 2.15f : ChaikinMaxRiverDistCells;
            for (int i = 0; i < smoothed.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(smoothed[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(smoothed[i].y), 0, h - 1);
                if (DistanceToNearestRiverCellChebyshev(grid, cx, cy) > maxRiverDist)
                    return pts;
            }

            return smoothed;
        }

        static bool ClosestPointOnPolyline2D(Vector2 p, List<Vector2> poly, out Vector2 closest, out float dist)
        {
            closest = p;
            dist = 99f;
            if (poly == null || poly.Count < 2)
                return false;
            for (int i = 0; i < poly.Count - 1; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[i + 1];
                Vector2 ab = b - a;
                float abLen2 = ab.sqrMagnitude;
                Vector2 q = abLen2 < 1e-10f ? a : a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLen2);
                float d = Vector2.Distance(p, q);
                if (d < dist)
                {
                    dist = d;
                    closest = q;
                }
            }

            return dist < 99f;
        }

        static Vector2 SoftProjectTowardPolyline(Vector2 p, List<Vector2> logical, float maxDev)
        {
            if (logical == null || logical.Count < 2 || maxDev <= 1e-5f)
                return p;
            if (!ClosestPointOnPolyline2D(p, logical, out Vector2 closest, out float dist))
                return p;
            if (dist <= maxDev)
                return p;
            float excess = dist - maxDev;
            float pull = 1f - Mathf.Exp(-excess / Mathf.Max(0.2f, maxDev * 0.5f));
            return Vector2.Lerp(p, closest, Mathf.Clamp01(pull));
        }

        static float MaxConsecutiveAngleDeg(List<Vector2> pts)
        {
            if (pts == null || pts.Count < 3)
                return 0f;
            float maxA = 0f;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i] - pts[i - 1];
                Vector2 b = pts[i + 1] - pts[i];
                if (a.sqrMagnitude < 1e-12f || b.sqrMagnitude < 1e-12f)
                    continue;
                maxA = Mathf.Max(maxA, Vector2.Angle(a, b));
            }

            return maxA;
        }

        static List<Vector2> RefineSamplesByMaxAngle(List<Vector2> samples, float maxAngleDeg, int maxIterations = 6)
        {
            if (samples == null || samples.Count < 3 || maxAngleDeg < 1f)
                return samples;
            float minSeg = MinCenterlineSpacingCells;
            float minSeg2 = minSeg * minSeg;
            var pts = new List<Vector2>(samples);
            for (int iter = 0; iter < maxIterations; iter++)
            {
                bool inserted = false;
                for (int i = pts.Count - 2; i >= 1; i--)
                {
                    Vector2 a = pts[i] - pts[i - 1];
                    Vector2 b = pts[i + 1] - pts[i];
                    if (a.sqrMagnitude < minSeg2 * 0.25f || b.sqrMagnitude < minSeg2 * 0.25f)
                        continue;
                    if (Vector2.Angle(a, b) <= maxAngleDeg)
                        continue;
                    Vector2 mid = (pts[i - 1] + pts[i + 1]) * 0.5f;
                    if ((mid - pts[i]).sqrMagnitude < minSeg2 * 0.36f)
                        continue;
                    if ((mid - pts[i - 1]).sqrMagnitude > minSeg2 && (mid - pts[i + 1]).sqrMagnitude > minSeg2)
                    {
                        pts.Insert(i + 1, mid);
                        inserted = true;
                    }
                }

                if (!inserted)
                    break;
            }

            return pts;
        }

        static float MeasurePolylineDeviation(List<Vector2> visual, List<Vector2> logical, out float avgDev, out int pointsOverLimit, float limit)
        {
            avgDev = 0f;
            pointsOverLimit = 0;
            if (visual == null || logical == null || visual.Count == 0)
                return 0f;
            float maxD = 0f;
            for (int i = 0; i < visual.Count; i++)
            {
                float d = DistancePointToPolyline2D(visual[i], logical);
                maxD = Mathf.Max(maxD, d);
                avgDev += d;
                if (d > limit)
                    pointsOverLimit++;
            }

            avgDev /= visual.Count;
            return maxD;
        }

        static float CentripetalKnotInterval(Vector2 a, Vector2 b, float alpha)
        {
            float d = Vector2.Distance(a, b);
            return Mathf.Pow(Mathf.Max(d, 1e-4f), alpha);
        }

        static Vector2 CentripetalCatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t, float alpha)
        {
            float t0 = 0f;
            float t1 = t0 + CentripetalKnotInterval(p0, p1, alpha);
            float t2 = t1 + CentripetalKnotInterval(p1, p2, alpha);
            float t3 = t2 + CentripetalKnotInterval(p2, p3, alpha);
            float u = Mathf.Lerp(t1, t2, Mathf.Clamp01(t));
            Vector2 A1 = Vector2.Lerp(p0, p1, t1 > 1e-6f ? (u - t0) / (t1 - t0) : 0f);
            Vector2 A2 = Vector2.Lerp(p1, p2, t2 - t1 > 1e-6f ? (u - t1) / (t2 - t1) : 0f);
            Vector2 A3 = Vector2.Lerp(p2, p3, t3 - t2 > 1e-6f ? (u - t2) / (t3 - t2) : 0f);
            Vector2 B1 = Vector2.Lerp(A1, A2, t2 - t0 > 1e-6f ? (u - t0) / (t2 - t0) : 0f);
            Vector2 B2 = Vector2.Lerp(A2, A3, t3 - t1 > 1e-6f ? (u - t1) / (t3 - t1) : 0f);
            return Vector2.Lerp(B1, B2, t2 - t1 > 1e-6f ? (u - t1) / (t2 - t1) : 0f);
        }

        static void CollectSplineAnchorIndices(
            List<Vector2> control,
            GridSystem grid,
            MapGenConfig config,
            out HashSet<int> hardAnchors,
            out HashSet<int> softAnchors,
            out int fordAnchorCount)
        {
            hardAnchors = new HashSet<int>();
            softAnchors = new HashSet<int>();
            fordAnchorCount = 0;
            if (control == null || control.Count < 2)
                return;
            hardAnchors.Add(0);
            hardAnchors.Add(control.Count - 1);
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            float sharpDeg = config.riverSurfaceSharpBendAngleDeg > 1f ? config.riverSurfaceSharpBendAngleDeg : 70f;
            for (int i = 0; i < control.Count; i++)
            {
                if (IsPointNearFord(grid, control[i], fordD))
                {
                    hardAnchors.Add(i);
                    fordAnchorCount++;
                }
                else if (i > 0 && i < control.Count - 1)
                {
                    float ang = InteriorTurnAngleDeg(control, i);
                    if (ang >= sharpDeg)
                        softAnchors.Add(i);
                }
            }
        }

        static List<Vector2> SampleCentripetalSpline(
            List<Vector2> control,
            float spacing,
            float alpha,
            int maxSamples)
        {
            var samples = new List<Vector2>();
            if (control == null || control.Count < 2)
                return samples;
            if (control.Count < 4)
                return ResampleUniformSpacingCell(control, spacing, maxSamples);

            spacing = Mathf.Max(0.12f, spacing);
            maxSamples = Mathf.Clamp(maxSamples, 2, 16384);
            samples.Add(control[0]);
            for (int i = 0; i < control.Count - 1; i++)
            {
                Vector2 p0 = control[Mathf.Max(0, i - 1)];
                Vector2 p1 = control[i];
                Vector2 p2 = control[i + 1];
                Vector2 p3 = control[Mathf.Min(control.Count - 1, i + 2)];
                float segLen = Vector2.Distance(p1, p2);
                int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / spacing));
                for (int s = 1; s <= steps; s++)
                {
                    if (samples.Count >= maxSamples)
                        break;
                    float t = s / (float)steps;
                    Vector2 p = CentripetalCatmullRom(p0, p1, p2, p3, t, alpha);
                    float minD2 = Mathf.Max(DedupeCellEps * DedupeCellEps, spacing * spacing * 0.64f);
                    if ((p - samples[samples.Count - 1]).sqrMagnitude > minD2)
                        samples.Add(p);
                }

                if (samples.Count >= maxSamples)
                    break;
            }

            if ((samples[samples.Count - 1] - control[control.Count - 1]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                samples.Add(control[control.Count - 1]);
            return samples.Count >= 2 ? samples : control;
        }

        static void EnforceSplineConstraints(
            List<Vector2> samples,
            List<Vector2> logical,
            GridSystem grid,
            MapGenConfig config,
            HashSet<int> hardAnchors,
            List<Vector2> control)
        {
            if (samples == null || logical == null || samples.Count == 0 || config == null)
                return;
            float maxDev = Mathf.Clamp(config.riverSurfaceSplineMaxDeviationCells, 0.1f, 2f);
            float fordRadius = Mathf.Clamp(config.riverSurfaceSplineFordLockRadiusCells, 0f, 4f);
            float endpointLock = Mathf.Clamp(config.riverSurfaceSplineEndpointLockCells, 0f, 2f);
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);

            for (int i = 0; i < samples.Count; i++)
            {
                Vector2 p = samples[i];
                float localMaxDev = maxDev;
                if (IsPointNearFord(grid, p, fordD))
                    localMaxDev *= 0.35f;
                else if (i == 0 || i == samples.Count - 1)
                    localMaxDev = Mathf.Min(localMaxDev, endpointLock > 1e-4f ? endpointLock : maxDev * 0.25f);

                p = SoftProjectTowardPolyline(p, logical, localMaxDev);

                if (i == 0)
                    p = Vector2.Lerp(p, logical[0], endpointLock > 1e-4f ? 0.85f : 1f);
                else if (i == samples.Count - 1)
                    p = Vector2.Lerp(p, logical[logical.Count - 1], endpointLock > 1e-4f ? 0.85f : 1f);
                else if (fordRadius > 1e-4f && control != null)
                {
                    for (int a = 0; a < control.Count; a++)
                    {
                        if (!hardAnchors.Contains(a) || !IsPointNearFord(grid, control[a], fordD))
                            continue;
                        float d = Vector2.Distance(p, control[a]);
                        if (d < fordRadius)
                        {
                            float w = 1f - d / fordRadius;
                            p = Vector2.Lerp(p, control[a], w * 0.65f);
                        }
                    }
                }

                samples[i] = p;
            }
        }

        static void LogRiverSurfaceSpline(MapGenConfig config, int riverId, RiverSplineBuildStats s)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverSurfaceSpline] riverId={riverId} attempts={s.Attempts} accepted={(s.Accepted ? 1 : 0)} " +
                $"fallbackUsed={(s.FallbackUsed ? 1 : 0)} fallbackReason={(string.IsNullOrEmpty(s.FallbackReason) ? "none" : s.FallbackReason)} " +
                $"maxDeviationCells={s.MaxDeviationCells:F3} maxActualDeviationCells={s.MaxActualDeviationCells:F3} " +
                $"selfIntersectionDetected={(s.SelfIntersectionDetected ? 1 : 0)} anchorCount={s.SimplifiedPts} " +
                $"fordAnchorCount={s.FordAnchors} hardBendAnchorCount={s.HardBendAnchorCount} sampleCount={s.SplinePts}");
        }

        static List<Vector2> BuildOrganicVisualRiverCenterline(
            GridSystem grid,
            List<Vector2> rawPath,
            MapGenConfig config,
            int riverIndex,
            out int fordCellsNear,
            out RiverCenterlinePrepStats prepStats,
            out RiverSplineBuildStats splineStats)
        {
            return BuildSplineVisualCenterlineFromLogical(
                grid,
                rawPath,
                config,
                riverIndex,
                out fordCellsNear,
                out prepStats,
                out splineStats);
        }

        static List<Vector2> BuildSplineVisualCenterlineFromLogical(
            GridSystem grid,
            List<Vector2> rawPath,
            MapGenConfig config,
            int riverIndex,
            out int fordCellsNear,
            out RiverCenterlinePrepStats prepStats,
            out RiverSplineBuildStats splineStats)
        {
            fordCellsNear = 0;
            prepStats = default;
            splineStats = default;
            if (rawPath == null || rawPath.Count < 2)
                return null;

            prepStats.RawPts = rawPath.Count;
            splineStats.RawPts = rawPath.Count;
            var logical = new List<Vector2>(rawPath);
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            int maxPts = Mathf.Max(2, Mathf.CeilToInt(rawPath.Count * VisualCenterlineMaxInputMul));

            var control = new List<Vector2>(rawPath);
            control = DedupeConsecutiveCell(control, DedupeCellEps);
            control = RemoveNearNullSegmentsCell(control, MinSegmentCellEps);
            control = RemoveCollinearPointsCellFordAware(control, grid, config);
            if (control == null || control.Count < 2)
                return null;

            CollectSplineAnchorIndices(control, grid, config, out HashSet<int> hardAnchors, out HashSet<int> softAnchors, out int fordAnchors);
            splineStats.AnchorsHard = hardAnchors.Count;
            splineStats.FordAnchors = fordAnchors;
            splineStats.SimplifiedPts = control.Count;
            int hardBends = 0;
            for (int hi = 1; hi + 1 < control.Count; hi++)
            {
                if (InteriorTurnAngleDeg(control, hi) >= JoinAngleHardDeg)
                    hardBends++;
            }

            splineStats.HardBendAnchorCount = hardBends;

            float baseSpacing = config.riverSurfaceSplineSampleSpacingCells > 0.01f
                ? config.riverSurfaceSplineSampleSpacingCells
                : 0.4f;
            baseSpacing = Mathf.Clamp(baseSpacing, 0.35f, 0.45f);
            float alpha = 0.5f;
            float tension = Mathf.Clamp01(config.riverSurfaceSplineTension);
            float maxAngle = Mathf.Clamp(config.riverSurfaceSplineMaxAngleStepDeg, 12f, 45f);
            float baseMaxDev = Mathf.Clamp(config.riverSurfaceSplineMaxDeviationCells, 0.1f, 1.35f);
            float[] devTries = { baseMaxDev, 0.9f, 0.55f };
            float[] spacingMul = { 1f, 1.12f, 1.28f };

            List<Vector2> samples = null;
            float maxDevLimitUsed = baseMaxDev;
            for (int att = 0; att < devTries.Length; att++)
            {
                splineStats.Attempts = att + 1;
                maxDevLimitUsed = devTries[att];
                float spacing = baseSpacing * spacingMul[att] * Mathf.Lerp(1.1f, 0.9f, tension);
                var attemptSamples = SampleCentripetalSpline(control, spacing, alpha, maxPts);
                attemptSamples = RefineSamplesByMaxAngle(attemptSamples, maxAngle);
                EnforceSplineConstraints(attemptSamples, logical, grid, config, hardAnchors, control);
                splineStats.MaxAngleStepDeg = MaxConsecutiveAngleDeg(attemptSamples);

                bool invalid = false;
                for (int i = 0; i < attemptSamples.Count; i++)
                {
                    if (float.IsNaN(attemptSamples[i].x) || float.IsNaN(attemptSamples[i].y))
                        invalid = true;
                }

                if (invalid)
                {
                    splineStats.SelfIntersectionDetected = false;
                    splineStats.FallbackReason = "nan";
                    continue;
                }

                if (PolylineSelfIntersectsXZCell(attemptSamples))
                {
                    splineStats.SelfIntersectionDetected = true;
                    continue;
                }

                float maxDev = MeasurePolylineDeviation(attemptSamples, logical, out float avgDev, out int overLimit, maxDevLimitUsed);
                if (maxDev > maxDevLimitUsed + 0.01f || splineStats.MaxAngleStepDeg > maxAngle + 2f)
                    continue;

                samples = attemptSamples;
                splineStats.SplinePts = samples.Count;
                splineStats.MaxDeviationCells = maxDevLimitUsed;
                splineStats.MaxActualDeviationCells = maxDev;
                splineStats.AvgDeviationCells = avgDev;
                splineStats.Accepted = true;
                splineStats.FallbackUsed = false;
                splineStats.SelfIntersectionDetected = false;
                splineStats.FallbackReason = null;
                break;
            }

            if (samples == null)
            {
                splineStats.Accepted = false;
                splineStats.FallbackUsed = true;
                if (string.IsNullOrEmpty(splineStats.FallbackReason))
                    splineStats.FallbackReason = splineStats.SelfIntersectionDetected ? "self_intersection" : "deviation_or_angle";
                LogRiverSurfaceSpline(config, riverIndex, splineStats);
                return null;
            }

            prepStats.DedupPts = control.Count;
            prepStats.SimplifiedPts = control.Count;
            prepStats.ResampledPts = samples.Count;
            prepStats.SmoothedPts = prepStats.CornerDensePts = samples.Count;
            prepStats.MaxDeviationCells = splineStats.MaxActualDeviationCells;

            int outsideBefore = CountPointsOutsidePlayableRect(samples, grid.Width, grid.Height);
            ClampPolylinePlayableCellSpace(samples, grid.Width, grid.Height);
            int outsideAfter = CountPointsOutsidePlayableRect(samples, grid.Width, grid.Height);

            splineStats.EndpointStartAtBorder = IsTrueMapEdgeCellSpace(samples[0], grid.Width, grid.Height);
            splineStats.EndpointEndAtBorder = IsTrueMapEdgeCellSpace(samples[samples.Count - 1], grid.Width, grid.Height);
            splineStats.BorderExtensionApplied = !config.riverSurfaceDisableBorderExtension &&
                config.riverSurfaceBorderExtendMaxCells > 1e-4f;

            LogRiverSurfaceSpline(config, riverIndex, splineStats);
            LogRiverSurfaceBorderPolicy(config, riverIndex, splineStats.EndpointStartAtBorder, splineStats.EndpointEndAtBorder, outsideAfter);

            for (int i = 0; i < samples.Count; i++)
            {
                if (IsPointNearFord(grid, samples[i], fordD))
                    fordCellsNear++;
            }

            if (outsideBefore > 0 && (config.debugLogs || config.debugHydrologyNetwork))
            {
                Debug.LogWarning(
                    $"[RiverSurfaceAlignment] riverId={riverIndex} pointsOutsideMap={outsideBefore} clampedTo={outsideAfter}");
            }

            samples = PolishOrganicCenterlinePolyline(samples, config);
            if (samples == null || samples.Count < 2)
                return null;

            return samples;
        }

        /// <summary>Quita micro-zig-zags, picos terminales y aplica Chaikin suave si es seguro.</summary>
        static List<Vector2> PolishOrganicCenterlinePolyline(List<Vector2> pts, MapGenConfig config)
        {
            if (pts == null || pts.Count < 3)
                return pts;

            pts = RemoveCenterlineMicroBacktracks(pts, 1.75f);
            CollapseStartCenterlineSpike(pts);
            CollapseTerminalCenterlineSpike(pts);

            if (PolylineRevisitsCell(pts))
                pts = RemoveCenterlineMicroBacktracks(pts, 1.15f);

            if (config != null && config.riverSurfaceChaikinPasses > 0)
            {
                var chaikin = ChaikinOpenCell(pts, 1);
                if (!PolylineSelfIntersectsXZCell(chaikin) && !PolylineRevisitsCell(chaikin))
                    pts = chaikin;
            }

            return pts != null && pts.Count >= 2 ? pts : null;
        }

        static List<Vector2> BuildFaithfulFunctionalCenterline(List<Vector2> rawPath, MapGenConfig config)
        {
            if (rawPath == null || rawPath.Count < 2 || config == null)
                return null;
            var pts = DedupeConsecutiveCell(new List<Vector2>(rawPath), DedupeCellEps);
            pts = RemoveNearNullSegmentsCell(pts, MinSegmentCellEps);
            if (pts == null || pts.Count < 2)
                return null;
            float spacing = config.riverSurfaceVisualSpacingCells > 0.01f
                ? config.riverSurfaceVisualSpacingCells
                : 0.75f;
            spacing = Mathf.Clamp(spacing, 0.55f, 1f);
            int maxPts = Mathf.Max(2, Mathf.CeilToInt(rawPath.Count * VisualCenterlineMaxInputMul));
            pts = ResampleUniformSpacingCell(pts, spacing, maxPts);
            return pts != null && pts.Count >= 2 ? pts : null;
        }

        public static List<Vector2> BuildSnappedCellCenterPolyline(List<Vector2> rawPath)
        {
            if (rawPath == null || rawPath.Count < 2)
                return null;
            var pts = new List<Vector2>(rawPath.Count);
            int lastX = int.MinValue;
            int lastY = int.MinValue;
            for (int i = 0; i < rawPath.Count; i++)
            {
                int cx = Mathf.RoundToInt(rawPath[i].x);
                int cy = Mathf.RoundToInt(rawPath[i].y);
                if (pts.Count > 0 && cx == lastX && cy == lastY)
                    continue;
                pts.Add(new Vector2(cx + 0.5f, cy + 0.5f));
                lastX = cx;
                lastY = cy;
            }

            return pts.Count >= 2 ? pts : null;
        }

        static int CountInteriorTurnsAboveDeg(List<Vector2> poly, float minDeg)
        {
            if (poly == null || poly.Count < 3)
                return 0;
            int count = 0;
            for (int i = 1; i < poly.Count - 1; i++)
            {
                if (InteriorTurnAngleDeg(poly, i) >= minDeg)
                    count++;
            }

            return count;
        }

        static List<Vector2> ChamferGridCornerCenterline(List<Vector2> corners, float frac = 0.42f)
        {
            if (corners == null || corners.Count < 3)
                return corners;

            var outPts = new List<Vector2>(corners.Count + 8) { corners[0] };
            for (int i = 1; i < corners.Count - 1; i++)
            {
                float ang = InteriorTurnAngleDeg(corners, i);
                if (ang < 55f)
                {
                    outPts.Add(corners[i]);
                    continue;
                }

                Vector2 prev = corners[i - 1];
                Vector2 cur = corners[i];
                Vector2 next = corners[i + 1];
                Vector2 inLeg = cur - prev;
                Vector2 outLeg = next - cur;
                if (inLeg.sqrMagnitude < 1e-6f || outLeg.sqrMagnitude < 1e-6f)
                {
                    outPts.Add(cur);
                    continue;
                }

                float cut = frac * Mathf.Min(inLeg.magnitude, outLeg.magnitude, 1.15f);
                if (cut < 0.12f)
                {
                    outPts.Add(cur);
                    continue;
                }

                outPts.Add(cur - inLeg.normalized * cut);
                outPts.Add(cur + outLeg.normalized * cut);
            }

            outPts.Add(corners[corners.Count - 1]);
            return DedupeConsecutiveCell(outPts, DedupeCellEps);
        }

        static float PerpendicularDistanceCell2D(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 ab = lineEnd - lineStart;
            if (ab.sqrMagnitude < 1e-8f)
                return Vector2.Distance(point, lineStart);
            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, ab) / ab.sqrMagnitude);
            return Vector2.Distance(point, lineStart + ab * t);
        }

        static List<Vector2> DouglasPeuckerCell2D(List<Vector2> points, float epsilon)
        {
            if (points == null || points.Count < 3)
                return points != null ? new List<Vector2>(points) : null;

            int last = points.Count - 1;
            float maxDist = 0f;
            int maxIndex = 0;
            for (int i = 1; i < last; i++)
            {
                float dist = PerpendicularDistanceCell2D(points[i], points[0], points[last]);
                if (dist > maxDist)
                {
                    maxDist = dist;
                    maxIndex = i;
                }
            }

            if (maxDist <= epsilon)
                return new List<Vector2> { points[0], points[last] };

            var left = DouglasPeuckerCell2D(points.GetRange(0, maxIndex + 1), epsilon);
            var right = DouglasPeuckerCell2D(points.GetRange(maxIndex, points.Count - maxIndex), epsilon);
            var result = new List<Vector2>(left);
            for (int i = 1; i < right.Count; i++)
                result.Add(right[i]);
            return result;
        }

        static float MaxLogicalDeviationFromSimplifiedCell(List<Vector2> simplified, List<Vector2> logical)
        {
            if (simplified == null || logical == null || simplified.Count < 2 || logical.Count < 2)
                return float.MaxValue;

            float max = 0f;
            for (int i = 0; i < logical.Count; i++)
            {
                if (!ClosestPointOnPolyline2D(logical[i], simplified, out Vector2 closest, out _))
                    return float.MaxValue;
                max = Mathf.Max(max, Vector2.Distance(logical[i], closest));
            }

            return max;
        }

        static List<Vector2> MergeCenterlineSegments(List<Vector2> a, List<Vector2> b, List<Vector2> c)
        {
            var merged = new List<Vector2>();
            void AppendSeg(List<Vector2> seg, bool skipFirst)
            {
                if (seg == null || seg.Count == 0)
                    return;
                for (int i = skipFirst ? 1 : 0; i < seg.Count; i++)
                {
                    if (merged.Count == 0)
                    {
                        merged.Add(seg[i]);
                        continue;
                    }

                    if ((merged[merged.Count - 1] - seg[i]).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                        merged.Add(seg[i]);
                }
            }

            AppendSeg(a, false);
            AppendSeg(b, true);
            AppendSeg(c, true);
            return merged.Count >= 2 ? merged : null;
        }

        static float ScoreOrganicRdpCandidate(List<Vector2> rdp)
        {
            if (rdp == null || rdp.Count < 3)
                return -1f;
            float maxTurn = PolylineMaxInteriorTurnDeg(rdp);
            if (maxTurn < 30f || maxTurn >= 92f)
                return -1f;

            float turnScore = 1f - Mathf.Min(Mathf.Abs(maxTurn - 58f) / 34f, 1f);
            float countScore = 1f - Mathf.Min(Mathf.Abs(rdp.Count - 11f) / 7f, 1f);
            return turnScore * 0.58f + countScore * 0.42f;
        }

        static List<Vector2> TryConservativeRdpMiddle(
            List<Vector2> middleLogical,
            float maxDeviationCells,
            float maxTurnDeg,
            int minPts,
            int maxPts)
        {
            if (middleLogical == null || middleLogical.Count < 3)
                return middleLogical != null ? new List<Vector2>(middleLogical) : null;

            List<Vector2> best = null;
            float bestScore = -1f;
            float[] epsTry = { 1.15f, 1.28f, 1.4f, 1.52f, 1.62f };
            foreach (float eps in epsTry)
            {
                var rdp = DouglasPeuckerCell2D(middleLogical, eps);
                if (rdp == null || rdp.Count < 2 || rdp.Count > maxPts)
                    continue;
                if (MaxLogicalDeviationFromSimplifiedCell(rdp, middleLogical) > maxDeviationCells)
                    continue;
                if (PolylineSelfIntersectsXZCell(rdp) || PolylineRevisitsCell(rdp))
                    continue;
                if (PolylineMaxInteriorTurnDeg(rdp) >= maxTurnDeg)
                    continue;

                float score = ScoreOrganicRdpCandidate(rdp);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = rdp;
                }
            }

            if (best != null && best.Count >= minPts)
                return best;

            var chamfer = ChamferGridCornerCenterline(SimplifyGridPathToCornerControls(middleLogical));
            if (chamfer != null && chamfer.Count >= 2 &&
                MaxLogicalDeviationFromSimplifiedCell(chamfer, middleLogical) <= maxDeviationCells + 0.35f &&
                PolylineMaxInteriorTurnDeg(chamfer) < maxTurnDeg + 8f)
                return chamfer;

            return new List<Vector2>(middleLogical);
        }

        static List<Vector2> CapTributaryVisualPointCount(List<Vector2> pts, int minPts, int maxPts)
        {
            if (pts == null || pts.Count <= maxPts)
                return pts;
            float spacing = Mathf.Max(0.65f, PolylineLengthCellSpace(pts) / Mathf.Max(minPts, maxPts - 1));
            var resampled = ResampleUniformSpacingCell(pts, spacing, maxPts);
            return resampled != null && resampled.Count >= 2 ? resampled : pts;
        }

        /// <summary>Centerline tributaria: RDP conservador en tramo central + anclas grid en bocas.</summary>
        static List<Vector2> BuildTributaryGridVisualCenterline(List<Vector2> rawPath, GridSystem grid, MapGenConfig config = null)
        {
            var snapped = BuildSnappedCellCenterPolyline(rawPath);
            if (snapped == null || snapped.Count < 2)
                return null;

            var logical = new List<Vector2>(snapped);
            int n = logical.Count;
            bool uwp = config != null && config.uwpOwnedVisualPolicy;
            int minVisualPts = uwp ? 16 : 10;
            int maxVisualPts = uwp
                ? Mathf.Clamp(Mathf.CeilToInt(n * 0.55f), 28, 96)
                : 22;
            const float maxDeviationCells = 1.4f;
            const float maxTurnDeg = 78f;

            List<Vector2> pts;
            int anchorCount = Mathf.Clamp(Mathf.CeilToInt(n * 0.15f), 5, 8);
            if (n < anchorCount * 2 + 6 || CountInteriorTurnsAboveDeg(logical, 75f) < 6)
            {
                pts = SimplifyGridPathToCornerControls(logical);
                if (pts == null || pts.Count < 2)
                    pts = logical;
            }
            else
            {
                var startAnchor = logical.GetRange(0, anchorCount);
                var endAnchor = logical.GetRange(n - anchorCount, anchorCount);
                int midStart = anchorCount - 1;
                int midCount = n - anchorCount - midStart;
                var middleLogical = logical.GetRange(midStart, midCount);
                int midMin = Mathf.Max(3, minVisualPts - anchorCount);
                var middlePts = TryConservativeRdpMiddle(
                    middleLogical,
                    maxDeviationCells,
                    maxTurnDeg,
                    midMin,
                    maxVisualPts - anchorCount + 2);
                pts = MergeCenterlineSegments(startAnchor, middlePts, endAnchor);
                if (pts == null || pts.Count < 2)
                    pts = logical;
            }

            pts = RemoveNearNullSegmentsCell(pts, MinSegmentCellEps);
            pts = CapTributaryVisualPointCount(pts, minVisualPts, maxVisualPts);
            if (pts != null && PolylineMaxInteriorTurnDeg(pts) < 42f &&
                CountInteriorTurnsAboveDeg(logical, 60f) >= 4)
            {
                var organic = ChamferGridCornerCenterline(SimplifyGridPathToCornerControls(logical));
                if (organic != null && organic.Count >= minVisualPts &&
                    MaxLogicalDeviationFromSimplifiedCell(organic, logical) <= maxDeviationCells + 0.45f &&
                    PolylineMaxInteriorTurnDeg(organic) >= 42f &&
                    PolylineMaxInteriorTurnDeg(organic) < maxTurnDeg &&
                    !PolylineSelfIntersectsXZCell(organic))
                    pts = CapTributaryVisualPointCount(organic, minVisualPts, maxVisualPts);
            }
            else if (pts != null && PolylineMaxInteriorTurnDeg(pts) >= maxTurnDeg && pts.Count >= 5)
            {
                var chamfer = ChamferGridCornerCenterline(SimplifyGridPathToCornerControls(pts));
                if (chamfer != null && chamfer.Count >= 2 &&
                    MaxLogicalDeviationFromSimplifiedCell(chamfer, logical) <= maxDeviationCells + 0.5f &&
                    PolylineMaxInteriorTurnDeg(chamfer) < maxTurnDeg + 5f)
                    pts = CapTributaryVisualPointCount(chamfer, minVisualPts, maxVisualPts);
            }

            return pts != null && pts.Count >= 2 ? pts : snapped;
        }

        /// <summary>Path visual = path funcional en grid (sin spline ni recorte de densidad).</summary>
        public static List<Vector2> BuildTributaryVisualSmoothCenterline(
            GridSystem grid,
            List<Vector2> logicalSnappedPath,
            MapGenConfig config)
        {
            if (logicalSnappedPath == null || logicalSnappedPath.Count < 2)
                return null;
            var pts = BuildSnappedCellCenterPolyline(logicalSnappedPath);
            if (pts == null || pts.Count < 2)
                pts = new List<Vector2>(logicalSnappedPath);
            pts = DedupeConsecutiveCell(pts, DedupeCellEps);
            pts = RemoveNearNullSegmentsCell(pts, MinSegmentCellEps);
            return pts != null && pts.Count >= 2 ? pts : null;
        }

        static List<Vector2> SimplifyGridPathToCornerControls(List<Vector2> logical)
        {
            if (logical == null || logical.Count < 2)
                return logical;
            if (logical.Count < 3)
                return new List<Vector2>(logical);

            const float minCornerDeg = 16f;
            var r = new List<Vector2>(logical.Count) { logical[0] };
            for (int i = 1; i < logical.Count - 1; i++)
            {
                if (InteriorTurnAngleDeg(logical, i) >= minCornerDeg)
                    r.Add(logical[i]);
            }

            r.Add(logical[logical.Count - 1]);
            return DedupeConsecutiveCell(r, DedupeCellEps);
        }

        static void ConstrainSmoothedPointsToRiverRibbon(
            GridSystem grid,
            List<Vector2> samples,
            List<Vector2> logical,
            float maxRiverChebyshev)
        {
            if (grid == null || samples == null || logical == null || samples.Count < 2)
                return;

            int w = grid.Width;
            int h = grid.Height;
            for (int i = 1; i < samples.Count - 1; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(samples[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(samples[i].y), 0, h - 1);
                if (DistanceToNearestRiverCellChebyshev(grid, cx, cy) <= maxRiverChebyshev)
                    continue;

                samples[i] = SoftProjectTowardPolyline(samples[i], logical, 0.42f);
                cx = Mathf.Clamp(Mathf.FloorToInt(samples[i].x), 0, w - 1);
                cy = Mathf.Clamp(Mathf.FloorToInt(samples[i].y), 0, h - 1);
                if (DistanceToNearestRiverCellChebyshev(grid, cx, cy) <= maxRiverChebyshev)
                    continue;

                if (ClosestPointOnPolyline2D(samples[i], logical, out Vector2 closest, out _))
                    samples[i] = closest;
            }
        }

        static List<Vector2> RemoveCenterlineMicroBacktracks(List<Vector2> pts, float minSpikeLenCells)
        {
            if (pts == null || pts.Count < 4)
                return pts;

            var result = new List<Vector2>(pts);
            bool changed = true;
            minSpikeLenCells = Mathf.Max(0.8f, minSpikeLenCells);
            while (changed && result.Count >= 4)
            {
                changed = false;
                for (int i = 1; i < result.Count - 2; i++)
                {
                    Vector2 a = result[i] - result[i - 1];
                    Vector2 b = result[i + 1] - result[i];
                    Vector2 c = result[i + 2] - result[i + 1];
                    if (a.sqrMagnitude < 0.02f || b.sqrMagnitude < 0.02f || c.sqrMagnitude < 0.02f)
                        continue;

                    float ang1 = Vector2.Angle(a, b);
                    float ang2 = Vector2.Angle(b, c);
                    if (ang1 < 105f || ang2 < 105f)
                        continue;

                    float spikeLen = b.magnitude;
                    if (spikeLen > minSpikeLenCells)
                        continue;

                    float backtrack = Vector2.Dot(b.normalized, a.normalized);
                    if (backtrack > -0.15f)
                        continue;

                    result.RemoveAt(i + 1);
                    changed = true;
                    break;
                }
            }

            return result.Count >= 2 ? result : pts;
        }

        /// <summary>Limita densidad de vértices del tributario: evita strip con docenas de quads superpuestos.</summary>
        static List<Vector2> CapTributaryCenterlineForStripMesh(List<Vector2> pts, int logicalPathCells)
        {
            if (pts == null || pts.Count < 2)
                return pts;

            float pathLen = PolylineLengthCellSpace(pts);
            float spacing = Mathf.Max(MinCenterlineSpacingCells, 0.56f);
            int budgetFromLen = Mathf.CeilToInt(pathLen / spacing) + 3;
            int budgetFromLogical = logicalPathCells > 0 ? logicalPathCells + 3 : budgetFromLen;
            int maxPts = Mathf.Clamp(Mathf.Max(budgetFromLen, budgetFromLogical), 6, 44);

            pts = CollapseCenterlineNearDuplicatesCell(pts, spacing * 0.82f);
            if (pts == null || pts.Count < 2)
                return pts;

            if (pts.Count <= maxPts && MaxSegmentLengthCell(pts) <= spacing * 1.4f)
                return pts;

            var resampled = ResampleUniformSpacingCell(pts, spacing, maxPts);
            return resampled != null && resampled.Count >= 2 ? resampled : pts;
        }

        static void FinalizeTributaryCenterlineForMesh(
            MapGenConfig config,
            ref List<Vector2> cellProcessed,
            int logicalPathPointCount)
        {
            if (cellProcessed == null || cellProcessed.Count < 2)
                return;

            cellProcessed = RemoveCenterlineMicroBacktracks(cellProcessed, 2.5f);
            cellProcessed = CapTributaryCenterlineForStripMesh(cellProcessed, logicalPathPointCount);
        }

        static void CollapseTerminalCenterlineSpike(List<Vector2> cellProcessed)
        {
            if (cellProcessed == null || cellProcessed.Count < 3)
                return;

            int n = cellProcessed.Count;
            Vector2 ab = cellProcessed[n - 2] - cellProcessed[n - 3];
            Vector2 bc = cellProcessed[n - 1] - cellProcessed[n - 2];
            if (ab.sqrMagnitude < 1e-6f || bc.sqrMagnitude < 1e-6f)
                return;
            if (Vector2.Dot(ab.normalized, bc.normalized) >= -0.15f)
                return;
            if (bc.magnitude <= Mathf.Max(1.2f, ab.magnitude * 0.75f))
                cellProcessed.RemoveAt(n - 1);
        }

        static void CollapseStartCenterlineSpike(List<Vector2> cellProcessed)
        {
            if (cellProcessed == null || cellProcessed.Count < 3)
                return;

            Vector2 ab = cellProcessed[1] - cellProcessed[0];
            Vector2 bc = cellProcessed[2] - cellProcessed[1];
            if (ab.sqrMagnitude < 1e-6f || bc.sqrMagnitude < 1e-6f)
                return;
            if (Vector2.Dot(ab.normalized, bc.normalized) >= -0.15f)
                return;
            if (ab.magnitude <= Mathf.Max(1.2f, bc.magnitude * 0.75f))
                cellProcessed.RemoveAt(0);
        }

        static List<Vector2> NormalizeTributarySpacingForMesh(List<Vector2> pts, MapGenConfig config)
        {
            if (pts == null || pts.Count < 3 || config == null)
                return pts;

            Vector2 start = pts[0];
            Vector2 end = pts[pts.Count - 1];
            float spacing = Mathf.Clamp(config.riverSurfaceVisualSpacingCells, 0.78f, 0.95f);
            int maxPts = Mathf.Clamp(Mathf.CeilToInt(PolylineLengthCellSpace(pts) / spacing) + 2, 10, 22);
            var resampled = ResampleUniformSpacingCell(pts, spacing, maxPts);
            if (resampled == null || resampled.Count < 2)
                return pts;

            resampled[0] = start;
            resampled[resampled.Count - 1] = end;
            return resampled;
        }

        static List<Vector2> ChamferSharpCornersInRanges(
            List<Vector2> pts,
            int startRange,
            int endRange,
            float minCornerDeg,
            float frac = 0.38f)
        {
            if (pts == null || pts.Count < 4)
                return pts;

            var result = new List<Vector2>(pts);
            int n = result.Count;
            var indices = new List<int>();
            int startMax = Mathf.Clamp(startRange, 2, n - 2);
            int endMin = Mathf.Max(n - Mathf.Clamp(endRange, 2, n - 2), 1);
            for (int i = 1; i < startMax && i < n - 1; i++)
            {
                if (InteriorTurnAngleDeg(result, i) >= minCornerDeg)
                    indices.Add(i);
            }

            for (int i = endMin; i < n - 1; i++)
            {
                if (InteriorTurnAngleDeg(result, i) >= minCornerDeg && !indices.Contains(i))
                    indices.Add(i);
            }

            indices.Sort((a, b) => b.CompareTo(a));
            foreach (int i in indices)
            {
                if (i <= 0 || i >= result.Count - 1)
                    continue;
                Vector2 prev = result[i - 1];
                Vector2 cur = result[i];
                Vector2 next = result[i + 1];
                Vector2 inLeg = cur - prev;
                Vector2 outLeg = next - cur;
                if (inLeg.sqrMagnitude < 1e-6f || outLeg.sqrMagnitude < 1e-6f)
                    continue;
                float cut = frac * Mathf.Min(inLeg.magnitude, outLeg.magnitude, 1.1f);
                if (cut < 0.1f)
                    continue;
                result[i] = cur - inLeg.normalized * cut;
                result.Insert(i + 1, cur + outLeg.normalized * cut);
            }

            return result;
        }

        static List<Vector2> FinalizeTributaryEndpointCenterline(List<Vector2> cellProcessed, MapGenConfig config)
        {
            if (cellProcessed == null || cellProcessed.Count < 2)
                return cellProcessed;
            cellProcessed = RemoveCenterlineMicroBacktracks(cellProcessed, 2.5f);
            CollapseStartCenterlineSpike(cellProcessed);
            CollapseTerminalCenterlineSpike(cellProcessed);
            cellProcessed = ChamferSharpCornersInRanges(cellProcessed, 6, 6, 48f);
            if (config != null)
            {
                float turnBefore = PolylineMaxInteriorTurnDeg(cellProcessed);
                var normalized = NormalizeTributarySpacingForMesh(cellProcessed, config);
                if (normalized != null && normalized.Count >= 2 &&
                    PolylineMaxInteriorTurnDeg(normalized) >= Mathf.Min(turnBefore - 10f, 40f))
                    cellProcessed = normalized;
            }
            return cellProcessed != null && cellProcessed.Count >= 2 ? cellProcessed : null;
        }

        static List<Vector2> FallbackMainRiverCenterlineIfInvalid(
            GridSystem grid,
            List<Vector2> rawPath,
            MapGenConfig config,
            List<Vector2> cellProcessed)
        {
            if (cellProcessed == null || cellProcessed.Count < 2 || rawPath == null)
                return cellProcessed;
            if (!PolylineSelfIntersectsXZCell(cellProcessed) && MaxSegmentLengthCell(cellProcessed) < 2.5f)
                return cellProcessed;

            int maxPts = Mathf.Max(2, Mathf.CeilToInt(rawPath.Count * VisualCenterlineMaxInputMul));
            var fallback = BuildVisualCenterlineSimple(grid, rawPath, config, maxPts, allowChaikin: true);
            if (fallback != null && fallback.Count >= 2 && !PolylineSelfIntersectsXZCell(fallback))
                return PolishOrganicCenterlinePolyline(fallback, config);

            var snapped = BuildSnappedCellCenterPolyline(rawPath);
            return snapped != null && snapped.Count >= 2
                ? PolishOrganicCenterlinePolyline(snapped, config)
                : PolishOrganicCenterlinePolyline(cellProcessed, config);
        }

        static bool IsTributaryEndpointNearLake(GridSystem grid, List<Vector2> cellPath, int endpointIndex)
        {
            if (grid == null || cellPath == null || endpointIndex < 0 || endpointIndex >= cellPath.Count)
                return false;
            return IsCellSpacePointInOrNearLake(grid, cellPath[endpointIndex], 8);
        }

        static bool IsTributaryEndpointNearMain(
            GridSystem grid,
            MapGenConfig config,
            float cellSize,
            List<Vector2> cellPath,
            int endpointIndex)
        {
            if (grid == null || cellPath == null || endpointIndex < 0 || endpointIndex >= cellPath.Count)
                return false;
            return IsCellSpacePointNearMainRiverCorridor(grid, config, cellSize, cellPath[endpointIndex], 1.35f);
        }

        /// <summary>Índice del extremo que debe unirse al troncal (0 o último), nunca la boca al lago.</summary>
        static bool TryResolveTributaryMainJoinEndpointIndex(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> cellProcessed,
            int riverIndex,
            out int endpointIndex)
        {
            endpointIndex = -1;
            if (grid == null || config == null || cellProcessed == null || cellProcessed.Count < 2 || riverIndex <= 0)
                return false;

            float cellSize = grid.CellSizeWorld;
            int last = cellProcessed.Count - 1;
            bool startLake = IsTributaryEndpointNearLake(grid, cellProcessed, 0);
            bool endLake = IsTributaryEndpointNearLake(grid, cellProcessed, last);
            bool startMain = IsTributaryEndpointNearMain(grid, config, cellSize, cellProcessed, 0);
            bool endMain = IsTributaryEndpointNearMain(grid, config, cellSize, cellProcessed, last);

            if (endLake && !startLake && startMain)
            {
                endpointIndex = 0;
                return true;
            }

            if (startLake && !endLake && endMain)
            {
                endpointIndex = last;
                return true;
            }

            if (startMain && endLake && !endMain)
            {
                endpointIndex = 0;
                return true;
            }

            if (endMain && startLake && !startMain)
            {
                endpointIndex = last;
                return true;
            }

            if (!TryResolveTributaryJoinOnMainRiver(grid, riverIndex, cellProcessed, config, out Vector2 join))
                return false;

            float distStart = Vector2.Distance(cellProcessed[0], join);
            float distEnd = Vector2.Distance(cellProcessed[last], join);
            endpointIndex = distStart <= distEnd ? 0 : last;

            if (endpointIndex == 0 && startLake && !startMain)
                return false;
            if (endpointIndex == last && endLake && !endMain)
                return false;

            return true;
        }

        static void ApplyTributaryEndpointCenterlineJoins(
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            ref List<Vector2> cellProcessed,
            bool lakeStartExpected)
        {
            if (grid == null || config == null || cellProcessed == null || cellProcessed.Count < 2 || riverIndex <= 0)
                return;

            int last = cellProcessed.Count - 1;
            if (TryResolveTributaryMainJoinEndpointIndex(grid, config, cellProcessed, riverIndex, out int mainIdx))
            {
                int lakeIdx = mainIdx == 0 ? last : 0;
                bool extendStart = lakeIdx == 0;
                if (lakeStartExpected ||
                    IsCellSpacePointInOrNearLake(grid, cellProcessed[lakeIdx], 8) ||
                    IsTributaryAuthorizedForLakeEndpoint(grid, riverIndex, cellProcessed[lakeIdx], config))
                {
                    ApplyLakeEndpointVisualTuck(grid, cellProcessed, riverIndex, config, extendStart);
                }

                SnapTributaryCenterlineToMainRiver(grid, cellProcessed, riverIndex, config, mainIdx);
                TuckTributaryMouthIntoMainRiver(grid, cellProcessed, riverIndex, config, mainIdx);
                return;
            }

            if (lakeStartExpected || IsCellSpacePointInOrNearLake(grid, cellProcessed[0], 8))
                ApplyLakeEndpointVisualTuck(grid, cellProcessed, riverIndex, config, extendStart: true);

            if (IsCellSpacePointInOrNearLake(grid, cellProcessed[last], 8))
                ApplyLakeEndpointVisualTuck(grid, cellProcessed, riverIndex, config, extendStart: false);
        }

        static void ApplyTributaryEndpointSubmergeDepth(
            List<Vector3> center,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSize,
            float surfaceWaterY)
        {
            if (center == null || cellPath == null || config == null ||
                center.Count != cellPath.Count || center.Count < 2)
                return;

            bool webFusion = WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config);
            if (webFusion)
            {
                ApplyWebFusionTributaryEndpointSubmergeY(
                    center, cellPath, grid, config, riverIndex, cellSize, surfaceWaterY);
                return;
            }

            float ribbonLift = Mathf.Max(0f, config.riverSurfaceMeshExtraYOffsetWorld) +
                Mathf.Max(0f, config.riverRibbonAntiZFightYOffsetWorld);
            float belowSurface = Mathf.Max(cellSize * 0.1f, ribbonLift * 2.5f + config.waterSurfaceOffset * 1.5f);
            int blend = Mathf.Clamp(Mathf.CeilToInt(center.Count * 0.16f), 2, 7);

            void SubmergeRange(int count, bool fromStart)
            {
                for (int k = 0; k < count; k++)
                {
                    int idx = fromStart ? k : (center.Count - 1 - k);
                    float t = 1f - k / (float)Mathf.Max(1, count - 1);
                    Vector3 p = center[idx];
                    float targetY = surfaceWaterY - belowSurface * t * t;
                    if (p.y > targetY - 1e-5f)
                        p.y = targetY;
                    center[idx] = p;
                }
            }

            if (riverIndex == 0)
            {
                int last = center.Count - 1;
                if (IsCellSpacePointInOrNearLake(grid, cellPath[last], 8))
                    SubmergeRange(blend, fromStart: false);
                return;
            }

            if (IsCellSpacePointInOrNearLake(grid, cellPath[0], 7))
                SubmergeRange(blend, fromStart: true);

            int lastIdx = center.Count - 1;
            bool endMain = IsCellSpacePointNearMainRiverCorridor(grid, config, cellSize, cellPath[lastIdx], 1.35f);
            bool endLake = IsCellSpacePointInOrNearLake(grid, cellPath[lastIdx], 7);
            if (endMain || endLake)
                SubmergeRange(blend, fromStart: false);
        }

        /// <summary>WebFusion: re-aplica boca→lago/confluencia tras trims que eliminan puntos dentro del agua.</summary>
        static void ApplyWebFusionLakeMouthAfterTrim(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config)
        {
            if (!WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                return;
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null)
                return;

            if (riverIndex == 0)
            {
                ApplyWebFusionLakeMouthEnd(grid, cellProcessed, 0, config, extendStart: false);
                return;
            }

            if (TributaryTargetsMainConfluence(grid, riverIndex))
                ApplyWebFusionTributaryConfluenceEnd(grid, cellProcessed, riverIndex, config);
            else if (TryResolveTributaryMainJoinEndpointIndex(grid, config, cellProcessed, riverIndex, out _))
                ApplyWebFusionTributaryConfluenceEnd(grid, cellProcessed, riverIndex, config);
        }

        static bool IsLakeMouthLandCell(GridSystem grid, Vector2 p)
        {
            if (grid == null)
                return true;
            if (IsCellSpacePointInLakeBody(grid, p))
                return false;
            return !IsCellSpacePointWater(grid, p);
        }

        static bool IsLakeMouthShoreOrInteriorCell(GridSystem grid, Vector2 p)
        {
            return IsApproachLakeShoreBorderCell(grid, p);
        }

        static bool IsLakeMouthAnchorNearEndpoint(List<Vector2> cellPath, int landIdx, bool mouthAtEnd, int maxNodesFromMouth = 14)
        {
            if (cellPath == null || landIdx < 0 || landIdx >= cellPath.Count)
                return false;
            maxNodesFromMouth = Mathf.Max(4, maxNodesFromMouth);
            if (mouthAtEnd)
                return landIdx >= cellPath.Count - maxNodesFromMouth;
            return landIdx <= maxNodesFromMouth - 1;
        }

        static bool TryGetLakeMouthInteriorDirection(
            List<Vector2> cellPath,
            int landIdx,
            bool mouthAtEnd,
            out Vector2 intoLake)
        {
            intoLake = Vector2.zero;
            if (cellPath == null || cellPath.Count < 2 || landIdx < 0 || landIdx >= cellPath.Count)
                return false;

            if (mouthAtEnd)
            {
                if (landIdx + 1 < cellPath.Count)
                    intoLake = cellPath[landIdx + 1] - cellPath[landIdx];
                else if (landIdx > 0)
                    intoLake = cellPath[landIdx] - cellPath[landIdx - 1];
            }
            else if (landIdx > 0)
            {
                intoLake = cellPath[landIdx - 1] - cellPath[landIdx];
            }
            else if (cellPath.Count >= 2)
            {
                intoLake = cellPath[0] - cellPath[1];
            }

            return intoLake.sqrMagnitude > 1e-5f;
        }

        static bool IsWebFusionLakeInteriorChannelCell(GridSystem grid, Vector2 p)
        {
            return IsVisualLakeMouthBorderCell(grid, p) || IsCellSpacePointInLakeBody(grid, p);
        }

        /// <summary>Primer nodo fuera del interior del lago (boca/cuerpo) desde el inicio del path.</summary>
        static bool TryFindLakeInteriorExitFromStart(
            GridSystem grid,
            List<Vector2> cellPath,
            out int exitIdx,
            out int lastInsideIdx)
        {
            exitIdx = -1;
            lastInsideIdx = -1;
            if (grid == null || cellPath == null || cellPath.Count < 2)
                return false;

            for (int i = 0; i < cellPath.Count; i++)
            {
                if (!IsWebFusionLakeInteriorChannelCell(grid, cellPath[i]))
                {
                    if (lastInsideIdx >= 0)
                    {
                        exitIdx = i;
                        return true;
                    }

                    continue;
                }

                lastInsideIdx = i;
            }

            return false;
        }

        static bool TryResolveLakeShoreCrossingOrLandAnchor(
            GridSystem grid,
            List<Vector2> cellPath,
            bool mouthAtEnd,
            out int landIdx)
        {
            landIdx = -1;
            if (cellPath == null || cellPath.Count < 2)
                return false;

            if (TryFindLakeShoreCrossingIndex(grid, cellPath, mouthAtEnd, out _, out landIdx) &&
                IsLakeMouthAnchorNearEndpoint(cellPath, landIdx, mouthAtEnd))
                return true;

            landIdx = -1;
            if (!mouthAtEnd &&
                IsWebFusionLakeInteriorChannelCell(grid, cellPath[0]) &&
                TryFindLakeInteriorExitFromStart(grid, cellPath, out int exitIdx, out _) &&
                exitIdx > 0)
            {
                landIdx = exitIdx;
                return IsLakeMouthAnchorNearEndpoint(cellPath, landIdx, mouthAtEnd: false);
            }

            landIdx = -1;
            if (mouthAtEnd)
            {
                for (int i = cellPath.Count - 1; i >= 0; i--)
                {
                    if (IsLakeMouthLandCell(grid, cellPath[i]))
                    {
                        landIdx = i;
                        return true;
                    }
                }
            }
            else
            {
                for (int i = 0; i < cellPath.Count; i++)
                {
                    if (IsLakeMouthLandCell(grid, cellPath[i]))
                    {
                        landIdx = i;
                        return true;
                    }
                }
            }

            return false;
        }

        static bool IsVisualLakeMouthBorderCell(GridSystem grid, Vector2 p)
        {
            if (grid?.LakeMouthCellsPacked == null || grid.LakeMouthCellsPacked.Count == 0)
                return false;
            int x = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, grid.Width - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, grid.Height - 1);
            return grid.LakeMouthCellsPacked.Contains(PackLakeCellLong(x, z));
        }

        static bool IsApproachLakeShoreBorderCell(GridSystem grid, Vector2 p)
        {
            if (IsVisualLakeMouthBorderCell(grid, p))
                return true;
            if (grid?.LakeMouthCellsPacked == null || grid.LakeMouthCellsPacked.Count == 0)
                return IsCellSpacePointInLakeBody(grid, p);
            return false;
        }

        static bool TryRayAabbEntryT(
            Vector2 origin,
            Vector2 dir,
            float minX,
            float minY,
            float maxX,
            float maxY,
            out float entryT)
        {
            entryT = 0f;
            const float eps = 1e-6f;
            float tMin = 0f;
            float tMax = float.MaxValue;

            if (Mathf.Abs(dir.x) > eps)
            {
                float tx1 = (minX - origin.x) / dir.x;
                float tx2 = (maxX - origin.x) / dir.x;
                tMin = Mathf.Max(tMin, Mathf.Min(tx1, tx2));
                tMax = Mathf.Min(tMax, Mathf.Max(tx1, tx2));
            }
            else if (origin.x < minX || origin.x > maxX)
                return false;

            if (Mathf.Abs(dir.y) > eps)
            {
                float ty1 = (minY - origin.y) / dir.y;
                float ty2 = (maxY - origin.y) / dir.y;
                tMin = Mathf.Max(tMin, Mathf.Min(ty1, ty2));
                tMax = Mathf.Min(tMax, Mathf.Max(ty1, ty2));
            }
            else if (origin.y < minY || origin.y > maxY)
                return false;

            if (tMax < Mathf.Max(0f, tMin))
                return false;

            entryT = Mathf.Max(0f, tMin);
            return entryT <= tMax;
        }

        /// <summary>Primer cruce del rayo con celdas LakeMouth (borde visual del lago).</summary>
        static bool TryRaycastVisualLakeMouthBorder(
            GridSystem grid,
            Vector2 origin,
            Vector2 dir,
            float maxDistCells,
            out Vector2 shorePoint,
            out Vector2 landTerminus)
        {
            shorePoint = default;
            landTerminus = origin;
            if (grid?.LakeMouthCellsPacked == null || grid.LakeMouthCellsPacked.Count == 0 || dir.sqrMagnitude < 1e-6f)
                return false;

            dir.Normalize();
            float bestT = float.MaxValue;
            int ox = Mathf.RoundToInt(origin.x);
            int oz = Mathf.RoundToInt(origin.y);
            float mouthFilterDist = maxDistCells + 2f;
            foreach (long pk in grid.LakeMouthCellsPacked)
            {
                int mx = (int)(pk >> 32);
                int mz = (int)(uint)pk;
                float cheb = Mathf.Max(Mathf.Abs(mx - ox), Mathf.Abs(mz - oz));
                if (cheb > mouthFilterDist)
                    continue;
                if (!TryRayAabbEntryT(origin, dir, mx, mz, mx + 1f, mz + 1f, out float tEntry))
                    continue;
                if (tEntry <= 1e-4f || tEntry > maxDistCells || tEntry >= bestT)
                    continue;
                bestT = tEntry;
            }

            if (bestT >= float.MaxValue)
                return false;

            shorePoint = origin + dir * bestT;
            float landBack = Mathf.Max(0.04f, 0.08f);
            landTerminus = origin + dir * Mathf.Max(0f, bestT - landBack);
            if (IsVisualLakeMouthBorderCell(grid, landTerminus))
                landTerminus = origin + dir * Mathf.Max(0f, bestT - 0.14f);

            if (!IsVisualLakeMouthBorderCell(grid, shorePoint))
            {
                for (float eps = 0.02f; eps <= 0.24f; eps += 0.02f)
                {
                    Vector2 probe = origin + dir * Mathf.Max(0f, bestT - eps);
                    if (IsVisualLakeMouthBorderCell(grid, probe))
                    {
                        shorePoint = probe;
                        break;
                    }
                }
            }

            return true;
        }

        static bool TrySnapToNearestLakeMouthCell(GridSystem grid, Vector2 near, float maxDistCells, out Vector2 snapped)
        {
            snapped = default;
            if (grid?.LakeMouthCellsPacked == null || grid.LakeMouthCellsPacked.Count == 0)
                return false;

            float maxSq = maxDistCells * maxDistCells;
            float bestSq = float.MaxValue;
            foreach (long pk in grid.LakeMouthCellsPacked)
            {
                int x = (int)(pk >> 32);
                int z = (int)(uint)pk;
                var center = new Vector2(x + 0.5f, z + 0.5f);
                float sq = (center - near).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    snapped = center;
                }
            }

            return bestSq <= maxSq;
        }

        /// <summary>Cruce tierra→orilla de lago a lo largo de la dirección de llegada (sin teletransporte lateral).</summary>
        static bool TryFindApproachLakeShoreCrossPoint(
            GridSystem grid,
            Vector2 fromLand,
            Vector2 intoLake,
            float maxDistCells,
            out Vector2 shorePoint,
            out Vector2 landTerminus)
        {
            shorePoint = default;
            landTerminus = fromLand;
            if (grid == null || intoLake.sqrMagnitude < 1e-6f)
                return false;

            intoLake.Normalize();
            if (TryRaycastVisualLakeMouthBorder(grid, fromLand, intoLake, maxDistCells, out shorePoint, out landTerminus))
                return true;

            if (IsApproachLakeShoreBorderCell(grid, fromLand))
            {
                shorePoint = fromLand;
                landTerminus = fromLand - intoLake * 0.08f;
                return true;
            }

            const float step = 0.1f;
            float prevDist = 0f;
            for (float d = step; d <= maxDistCells; d += step)
            {
                Vector2 p = fromLand + intoLake * d;
                if (IsApproachLakeShoreBorderCell(grid, p))
                {
                    float lo = prevDist;
                    float hi = d;
                    for (int b = 0; b < 10; b++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        Vector2 m = fromLand + intoLake * mid;
                        if (IsApproachLakeShoreBorderCell(grid, m))
                            hi = mid;
                        else
                            lo = mid;
                    }

                    shorePoint = fromLand + intoLake * hi;
                    landTerminus = fromLand + intoLake * lo;
                    return true;
                }

                prevDist = d;
            }

            if (TryGetNearestLakeShorePoint(grid, fromLand, maxDistCells, out Vector2 nearestShore))
            {
                Vector2 toShore = nearestShore - fromLand;
                float proj = Vector2.Dot(toShore, intoLake);
                if (proj > 0.08f && proj <= maxDistCells)
                {
                    shorePoint = fromLand + intoLake * proj;
                    landTerminus = fromLand + intoLake * Mathf.Max(0f, proj - 0.12f);
                    return true;
                }
            }

            return false;
        }

        static void SnapWebFusionLakeMouthShoreToVisualBorder(
            GridSystem grid,
            ref Vector2 landTerminus,
            ref Vector2 shorePoint,
            float maxDistCells)
        {
            if (grid == null || (shorePoint - landTerminus).sqrMagnitude < 1e-8f)
                return;

            Vector2 intoLake = shorePoint - landTerminus;
            intoLake.Normalize();
            if (TryFindApproachLakeShoreCrossPoint(grid, landTerminus, intoLake, maxDistCells, out Vector2 snappedShore, out Vector2 snappedLand))
            {
                shorePoint = snappedShore;
                if ((snappedLand - landTerminus).sqrMagnitude > 1e-6f)
                    landTerminus = snappedLand;
                return;
            }

            if (IsVisualLakeMouthBorderCell(grid, shorePoint))
                return;

            Vector2 towardLand = (landTerminus - shorePoint).normalized;
            for (float eps = 0.02f; eps <= 0.4f; eps += 0.02f)
            {
                Vector2 probe = shorePoint + towardLand * eps;
                if (IsVisualLakeMouthBorderCell(grid, probe))
                {
                    shorePoint = probe;
                    return;
                }

                if (!IsCellSpacePointInLakeBody(grid, probe))
                {
                    shorePoint = probe;
                    return;
                }
            }
        }

        static List<Vector2> BuildWebFusionLakeInteriorFromShore(
            Vector2 shorePoint,
            Vector2 intoLake,
            int pointCount,
            float stepCells,
            Vector2 approachFrom)
        {
            var pts = new List<Vector2>(pointCount) { shorePoint };
            if (pointCount <= 1 || intoLake.sqrMagnitude < 1e-5f)
                return pts;

            intoLake.Normalize();
            Vector2 prev = approachFrom.sqrMagnitude > 1e-5f ? approachFrom : shorePoint - intoLake * stepCells;
            Vector2 inDir = shorePoint - prev;
            if (inDir.sqrMagnitude < 1e-5f)
                inDir = intoLake;
            inDir.Normalize();
            float turnRad = Mathf.Atan2(intoLake.y, intoLake.x) - Mathf.Atan2(inDir.y, inDir.x);
            turnRad = Mathf.Clamp(turnRad, -0.55f, 0.55f);
            for (int s = 1; s < pointCount; s++)
            {
                float fan = turnRad * (s / (float)Mathf.Max(1, pointCount - 1)) * 0.35f;
                float baseAng = Mathf.Atan2(intoLake.y, intoLake.x);
                Vector2 dir = new Vector2(Mathf.Cos(baseAng + fan), Mathf.Sin(baseAng + fan));
                Vector2 p = shorePoint + dir * (stepCells * s);
                if (WouldFoldBackBridge(prev, p, p + dir * 0.01f))
                    break;
                pts.Add(p);
                prev = p;
            }

            return pts;
        }

        static List<Vector2> BuildWebFusionLakeApproachExtension(
            Vector2 landTerminus,
            Vector2 shorePoint,
            Vector2 approachFrom,
            List<Vector2> lakeInteriorPts,
            int approachCount)
        {
            var extension = new List<Vector2>(approachCount + (lakeInteriorPts?.Count ?? 0));
            int steps = Mathf.Clamp(approachCount, 2, 6);

            Vector2 inDir = landTerminus - approachFrom;
            if (inDir.sqrMagnitude < 1e-5f)
                inDir = shorePoint - landTerminus;
            if (inDir.sqrMagnitude < 1e-5f)
                inDir = Vector2.right;
            inDir.Normalize();

            Vector2 outDir = shorePoint - landTerminus;
            if (outDir.sqrMagnitude < 1e-5f && lakeInteriorPts != null && lakeInteriorPts.Count >= 2)
                outDir = lakeInteriorPts[1] - lakeInteriorPts[0];
            if (outDir.sqrMagnitude < 1e-5f)
                outDir = inDir;
            outDir.Normalize();

            float dist = Vector2.Distance(landTerminus, shorePoint);
            Vector2 mid = (landTerminus + shorePoint) * 0.5f;
            Vector2 bisector = inDir + outDir;
            if (bisector.sqrMagnitude < 1e-5f)
                bisector = new Vector2(-inDir.y, inDir.x);
            bisector.Normalize();

            float turnDeg = Vector2.Angle(inDir, outDir);
            float bulge = Mathf.Clamp(
                dist * Mathf.Sin(turnDeg * Mathf.Deg2Rad * 0.5f) * 0.62f,
                cellSpaceBulgeMin(dist),
                dist * 0.48f);
            Vector2 control = mid + bisector * bulge;

            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)(steps + 1);
                float u = 1f - t;
                extension.Add(u * u * landTerminus + 2f * u * t * control + t * t * shorePoint);
            }

            if (lakeInteriorPts != null && lakeInteriorPts.Count > 0)
                extension.AddRange(lakeInteriorPts);
            return extension;
        }

        static float cellSpaceBulgeMin(float distCells) => Mathf.Clamp(distCells * 0.12f, 0.22f, 1.6f);

        /// <summary>Cruce tierra→lago: landIdx en tierra, borderIdx primer nodo dentro del lago.</summary>
        static bool TryFindLakeShoreCrossingIndex(
            GridSystem grid,
            List<Vector2> cellPath,
            bool mouthAtEnd,
            out int borderIdx,
            out int landIdx)
        {
            borderIdx = -1;
            landIdx = -1;
            if (grid == null || cellPath == null || cellPath.Count < 2)
                return false;

            if (mouthAtEnd)
            {
                for (int i = cellPath.Count - 2; i >= 0; i--)
                {
                    if (IsLakeMouthLandCell(grid, cellPath[i]) &&
                        IsApproachLakeShoreBorderCell(grid, cellPath[i + 1]))
                    {
                        landIdx = i;
                        borderIdx = i + 1;
                        return true;
                    }
                }

                return false;
            }

            for (int i = 1; i < cellPath.Count; i++)
            {
                if (IsLakeMouthLandCell(grid, cellPath[i]) &&
                    IsApproachLakeShoreBorderCell(grid, cellPath[i - 1]))
                {
                    landIdx = i;
                    borderIdx = i - 1;
                    return true;
                }
            }

            return false;
        }

        static bool TryFindFirstLakeMouthRunFromStart(
            GridSystem grid,
            List<Vector2> cellPath,
            out int firstMouthIdx,
            out int lastMouthIdx)
        {
            firstMouthIdx = -1;
            lastMouthIdx = -1;
            if (grid == null || cellPath == null || cellPath.Count < 2)
                return false;

            for (int i = 0; i < cellPath.Count; i++)
            {
                if (!IsWebFusionLakeInteriorChannelCell(grid, cellPath[i]))
                {
                    if (firstMouthIdx >= 0)
                        break;
                    continue;
                }

                if (firstMouthIdx < 0)
                    firstMouthIdx = i;
                lastMouthIdx = i;
            }

            return firstMouthIdx >= 0 && lastMouthIdx >= firstMouthIdx;
        }

        /// <summary>Cruce tierra↔lago interpolado en XZ sobre un segmento de la polyline (independiente de Y).</summary>
        static bool TryInterpolateLakeShoreOnSegment(
            GridSystem grid,
            Vector2 landSide,
            Vector2 lakeSide,
            out Vector2 shorePoint,
            out Vector2 landTerminus)
        {
            shorePoint = default;
            landTerminus = landSide;
            if (grid == null || (landSide - lakeSide).sqrMagnitude < 1e-8f)
                return false;
            if (!IsWebFusionLakeInteriorChannelCell(grid, lakeSide) ||
                IsWebFusionLakeInteriorChannelCell(grid, landSide))
                return false;

            float lo = 0f;
            float hi = 1f;
            for (int b = 0; b < 14; b++)
            {
                float mid = (lo + hi) * 0.5f;
                Vector2 p = Vector2.Lerp(landSide, lakeSide, mid);
                if (IsWebFusionLakeInteriorChannelCell(grid, p))
                    hi = mid;
                else
                    lo = mid;
            }

            shorePoint = Vector2.Lerp(landSide, lakeSide, hi);
            landTerminus = Vector2.Lerp(landSide, lakeSide, lo);
            if ((shorePoint - landTerminus).sqrMagnitude < 1e-6f)
            {
                landTerminus = landSide;
                shorePoint = Vector2.Lerp(landSide, lakeSide, Mathf.Max(hi, 0.02f));
            }

            Vector2 intoLake = shorePoint - landTerminus;
            if (intoLake.sqrMagnitude > 1e-6f && !IsVisualLakeMouthBorderCell(grid, shorePoint))
            {
                Vector2 dirN = intoLake.normalized;
                for (float eps = 0.01f; eps <= 0.22f; eps += 0.01f)
                {
                    Vector2 probe = shorePoint + dirN * eps;
                    if (IsVisualLakeMouthBorderCell(grid, probe))
                    {
                        shorePoint = probe;
                        break;
                    }

                    probe = shorePoint - dirN * eps;
                    if (IsVisualLakeMouthBorderCell(grid, probe))
                    {
                        shorePoint = probe;
                        break;
                    }
                }
            }

            return (shorePoint - landTerminus).sqrMagnitude > 1e-6f;
        }

        /// <summary>Último segmento tierra→lago hacia la boca (tributario desembocando).</summary>
        static bool TryFindPolylineLakeEntryCrossingFromEnd(
            GridSystem grid,
            List<Vector2> path,
            out int landSegIdx,
            out Vector2 shorePoint,
            out Vector2 landTerminus)
        {
            landSegIdx = -1;
            shorePoint = default;
            landTerminus = default;
            if (grid == null || path == null || path.Count < 2)
                return false;

            for (int i = path.Count - 2; i >= 0; i--)
            {
                if (!TryInterpolateLakeShoreOnSegment(grid, path[i], path[i + 1], out shorePoint, out landTerminus))
                    continue;
                landSegIdx = i;
                return true;
            }

            return false;
        }

        /// <summary>Primer segmento lago→tierra desde el inicio (emisario saliendo del lago).</summary>
        static bool TryFindPolylineLakeExitCrossingFromStart(
            GridSystem grid,
            List<Vector2> path,
            out int lakeSegIdx,
            out Vector2 shorePoint,
            out Vector2 landTerminus)
        {
            lakeSegIdx = -1;
            shorePoint = default;
            landTerminus = default;
            if (grid == null || path == null || path.Count < 2)
                return false;

            for (int i = 0; i < path.Count - 1; i++)
            {
                if (!TryInterpolateLakeShoreOnSegment(grid, path[i + 1], path[i], out shorePoint, out landTerminus))
                    continue;
                lakeSegIdx = i;
                return true;
            }

            return false;
        }

        static void RebuildWebFusionTributaryLakeMouthNearShore(
            GridSystem grid,
            List<Vector2> cellProcessed,
            MapGenConfig config,
            bool mouthAtEnd)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null)
                return;

            const int lakePointCount = WebFusionLakeMouthSinkVertexCount;
            float stepCells = 0.55f;
            float maxDist = ResolveLakeMouthApproachMaxDistCells(config);
            Vector2 shorePoint;
            Vector2 landTerminus;

            if (mouthAtEnd)
            {
                if (!TryFindPolylineLakeEntryCrossingFromEnd(grid, cellProcessed, out int landSegIdx, out shorePoint, out landTerminus))
                {
                    if (!TryResolveLakeShoreCrossingOrLandAnchor(grid, cellProcessed, mouthAtEnd: true, out int landIdx) ||
                        !IsLakeMouthAnchorNearEndpoint(cellProcessed, landIdx, mouthAtEnd: true))
                        return;
                    Vector2 landPoint = cellProcessed[landIdx];
                    Vector2 rayDir = landIdx > 0
                        ? landPoint - cellProcessed[landIdx - 1]
                        : (landIdx + 1 < cellProcessed.Count
                            ? cellProcessed[landIdx + 1] - landPoint
                            : Vector2.zero);
                    if (rayDir.sqrMagnitude < 1e-5f &&
                        !TryGetLakeMouthInteriorDirection(cellProcessed, landIdx, mouthAtEnd: true, out rayDir))
                        return;
                    if (!TryFindApproachLakeShoreCrossPoint(grid, landPoint, rayDir, maxDist, out shorePoint, out landTerminus))
                        return;
                    landSegIdx = landIdx;
                }

                Vector2 intoLake = shorePoint - landTerminus;
                if (intoLake.sqrMagnitude < 1e-5f)
                    return;

                SnapWebFusionLakeMouthShoreToVisualBorder(grid, ref landTerminus, ref shorePoint, maxDist);
                intoLake = shorePoint - landTerminus;
                if (intoLake.sqrMagnitude < 1e-5f)
                    return;

                Vector2 approachFrom = landSegIdx > 0 ? cellProcessed[landSegIdx - 1] : landTerminus;
                List<Vector2> lakePts = BuildWebFusionLakeInteriorFromShore(
                    shorePoint,
                    intoLake.normalized,
                    lakePointCount,
                    stepCells,
                    approachFrom);
                if (lakePts.Count < 2)
                    return;

                var extension = BuildWebFusionLakeApproachExtension(
                    landTerminus, shorePoint, approachFrom, lakePts, WebFusionLakeApproachVertexCount);

                if (landSegIdx + 1 < cellProcessed.Count)
                    cellProcessed.RemoveRange(landSegIdx + 1, cellProcessed.Count - landSegIdx - 1);
                cellProcessed[landSegIdx] = landTerminus;
                cellProcessed.AddRange(extension);
            }
            else
            {
                if (!TryFindPolylineLakeExitCrossingFromStart(grid, cellProcessed, out int lakeSegIdx, out shorePoint, out landTerminus))
                {
                    if (!TryResolveLakeShoreCrossingOrLandAnchor(grid, cellProcessed, mouthAtEnd: false, out int landIdx))
                        return;
                    Vector2 landPoint = cellProcessed[landIdx];
                    Vector2 rayDir = landIdx > 0
                        ? cellProcessed[landIdx - 1] - landPoint
                        : (landIdx + 1 < cellProcessed.Count
                            ? cellProcessed[landIdx + 1] - landPoint
                            : Vector2.zero);
                    if (rayDir.sqrMagnitude < 1e-5f &&
                        !TryGetLakeMouthInteriorDirection(cellProcessed, landIdx, mouthAtEnd: false, out rayDir))
                        return;
                    if (!TryFindApproachLakeShoreCrossPoint(grid, landPoint, rayDir, maxDist, out shorePoint, out landTerminus))
                        return;
                    lakeSegIdx = -1;
                }

                Vector2 intoLake = shorePoint - landTerminus;
                if (intoLake.sqrMagnitude < 1e-5f)
                    return;

                SnapWebFusionLakeMouthShoreToVisualBorder(grid, ref landTerminus, ref shorePoint, maxDist);
                intoLake = shorePoint - landTerminus;
                if (intoLake.sqrMagnitude < 1e-5f)
                    return;

                Vector2 approachFrom = lakeSegIdx >= 0 ? cellProcessed[lakeSegIdx] : landTerminus + intoLake;
                List<Vector2> lakePts = BuildWebFusionLakeInteriorFromShore(
                    shorePoint,
                    intoLake.normalized,
                    lakePointCount,
                    stepCells,
                    approachFrom);
                if (lakePts.Count < 2)
                    return;

                var extension = BuildWebFusionLakeApproachExtension(
                    landTerminus, shorePoint, approachFrom, lakePts, WebFusionLakeApproachVertexCount);

                if (lakeSegIdx >= 0)
                    cellProcessed.RemoveRange(0, Mathf.Min(lakeSegIdx + 1, cellProcessed.Count));

                var head = new List<Vector2>(extension.Count + 1);
                head.AddRange(extension);
                if ((landTerminus - shorePoint).sqrMagnitude > 1e-5f &&
                    (head.Count == 0 || (landTerminus - head[head.Count - 1]).sqrMagnitude > 1e-5f))
                    head.Add(landTerminus);
                cellProcessed.InsertRange(0, head);
                TrimDuplicateLakeInteriorAfterMouthRebuild(grid, cellProcessed, head.Count);
            }

            ClampWebFusionLakeMouthInteriorVertices(grid, cellProcessed, extendStart: !mouthAtEnd, maxInside: WebFusionLakeMouthSinkVertexCount);
        }

        static void TrimDuplicateLakeInteriorAfterMouthRebuild(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int afterIdx)
        {
            if (grid == null || cellProcessed == null || afterIdx < 0 || afterIdx >= cellProcessed.Count)
                return;

            int tail = afterIdx;
            while (tail < cellProcessed.Count &&
                   IsWebFusionLakeInteriorChannelCell(grid, cellProcessed[tail]))
            {
                tail++;
            }

            if (tail > afterIdx)
                cellProcessed.RemoveRange(afterIdx, tail - afterIdx);
        }

        static void ApplyWebFusionTributaryLakeMouthFinalize(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 ||
                config == null || riverIndex <= 0)
                return;
            if (!WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                return;

            float maxDist = ResolveLakeMouthApproachMaxDistCells(config);
            float cellSize = grid.CellSizeWorld;
            int last = cellProcessed.Count - 1;

            if (IsLakeEmissaryRiverIndex(grid, riverIndex))
            {
                AppendCenterlineTowardLakeShore(grid, cellProcessed, riverIndex, config, extendStart: true);
                RebuildWebFusionTributaryLakeMouthNearShore(grid, cellProcessed, config, mouthAtEnd: false);
                return;
            }

            if (TryResolveTributaryMainJoinEndpointIndex(
                    grid, config, cellProcessed, riverIndex, out int mainJoinIdx))
            {
                int lakeIdx = mainJoinIdx == 0 ? last : 0;
                bool lakeAtEnd = lakeIdx == last;
                if (IsTributaryAuthorizedForLakeEndpoint(
                        grid, riverIndex, cellProcessed[lakeIdx], config) ||
                    IsCellSpacePointInOrNearLake(grid, cellProcessed[lakeIdx], 8))
                {
                    AppendCenterlineTowardLakeShore(grid, cellProcessed, riverIndex, config, extendStart: !lakeAtEnd);
                    RebuildWebFusionTributaryLakeMouthNearShore(grid, cellProcessed, config, mouthAtEnd: lakeAtEnd);
                }

                return;
            }

            bool startNearMain = IsTributaryEndpointNearMain(grid, config, cellSize, cellProcessed, 0);
            bool endNearMain = IsTributaryEndpointNearMain(grid, config, cellSize, cellProcessed, last);
            bool startLake = !startNearMain && (
                TryFindPolylineLakeExitCrossingFromStart(grid, cellProcessed, out _, out _, out _) ||
                IsLakeEmissaryCenterline(grid, cellProcessed, riverIndex) ||
                IsCellSpacePointInOrNearLake(grid, cellProcessed[0], 8) ||
                TryFindLakeShoreCrossingIndex(grid, cellProcessed, mouthAtEnd: false, out _, out _));
            bool endLake = !endNearMain && (
                TryFindPolylineLakeEntryCrossingFromEnd(grid, cellProcessed, out _, out _, out _) ||
                IsCellSpacePointInOrNearLake(grid, cellProcessed[last], 8) ||
                (grid.LakeMouthCellsPacked != null && grid.LakeMouthCellsPacked.Count > 0 &&
                 MinChebyshevDistToLakeMouth(cellProcessed[last], grid) <= maxDist) ||
                RiverFunctionalEndNearLake(grid, riverIndex, config) ||
                TryFindLakeShoreCrossingIndex(grid, cellProcessed, mouthAtEnd: true, out _, out _));

            if (endLake)
            {
                AppendCenterlineTowardLakeShore(grid, cellProcessed, riverIndex, config, extendStart: false);
                RebuildWebFusionTributaryLakeMouthNearShore(grid, cellProcessed, config, mouthAtEnd: true);
            }

            if (startLake)
            {
                AppendCenterlineTowardLakeShore(grid, cellProcessed, riverIndex, config, extendStart: true);
                RebuildWebFusionTributaryLakeMouthNearShore(grid, cellProcessed, config, mouthAtEnd: false);
            }
        }

        static void ApplyWebFusionLakeMouthEnd(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config,
            bool extendStart)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null)
                return;

            int endpoint = extendStart ? 0 : cellProcessed.Count - 1;
            bool nearLake = extendStart
                ? (IsLakeEmissaryCenterline(grid, cellProcessed, riverIndex) ||
                   IsCellSpacePointInOrNearLake(grid, cellProcessed[endpoint], 10))
                : (IsCellSpacePointInOrNearLake(grid, cellProcessed[endpoint], 10) ||
                   (riverIndex == 0 && RiverFunctionalEndNearLake(grid, 0, config)));
            if (!nearLake)
                return;

            ApplyWebFusionPruebasLakeMouthCenterline(grid, cellProcessed, riverIndex, config, extendStart);
        }

        static bool IsCellSpacePointInsideLakeInterior(GridSystem grid, Vector2 p)
        {
            return IsCellSpacePointInLakeBody(grid, p) || IsCellSpacePointWater(grid, p);
        }

        static bool TryGetStrictLakeInteriorSpanIndices(
            GridSystem grid,
            List<Vector2> cellPath,
            out int firstInLakeIdx,
            out int lastInLakeIdx)
        {
            firstInLakeIdx = -1;
            lastInLakeIdx = -1;
            if (grid == null || cellPath == null || cellPath.Count < 2)
                return false;

            for (int i = 0; i < cellPath.Count; i++)
            {
                if (!IsCellSpacePointInsideLakeInterior(grid, cellPath[i]))
                    continue;
                if (firstInLakeIdx < 0)
                    firstInLakeIdx = i;
                lastInLakeIdx = i;
            }

            return firstInLakeIdx >= 0 && lastInLakeIdx >= firstInLakeIdx;
        }

        static void ClampWebFusionLakeMouthInteriorVertices(
            GridSystem grid,
            List<Vector2> cellProcessed,
            bool extendStart,
            int maxInside = 5)
        {
            maxInside = Mathf.Clamp(maxInside, 3, 5);
            if (grid == null || cellProcessed == null || cellProcessed.Count < 3)
                return;

            if (extendStart)
            {
                int i = 0;
                while (i < cellProcessed.Count && IsWebFusionLakeInteriorChannelCell(grid, cellProcessed[i]))
                    i++;
                if (i > maxInside)
                    cellProcessed.RemoveRange(maxInside, i - maxInside);
                return;
            }

            int j = cellProcessed.Count - 1;
            while (j >= 0 && IsWebFusionLakeInteriorChannelCell(grid, cellProcessed[j]))
                j--;
            int firstLake = j + 1;
            int lakeCount = cellProcessed.Count - firstLake;
            if (lakeCount > maxInside)
                cellProcessed.RemoveRange(firstLake, lakeCount - maxInside);
        }

        static int CountWebFusionLakeMouthInteriorVertices(GridSystem grid, List<Vector2> cellProcessed)
        {
            if (!TryGetStrictLakeInteriorSpanIndices(grid, cellProcessed, out int firstInLake, out int lastInLake))
                return 0;
            return lastInLake - firstInLake + 1;
        }

        /// <summary>WebFusion: boca→lago al estilo Pruebas (puente recto + un solo punto interior).</summary>
        static void ApplyWebFusionPruebasLakeMouthCenterline(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config,
            bool extendStart)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null)
                return;

            if (riverIndex > 0)
                ClampWebFusionLakeMouthInteriorVertices(grid, cellProcessed, extendStart, maxInside: 5);

            int endpoint = extendStart ? 0 : cellProcessed.Count - 1;
            Vector2 end = cellProcessed[endpoint];
            bool inLake = IsCellSpacePointInLakeBody(grid, end) || IsCellSpacePointWater(grid, end);
            if (!inLake)
            {
                float maxDist = ResolveLakeMouthApproachMaxDistCells(config);
                if (!TryGetNearestLakeShorePoint(grid, end, maxDist, out Vector2 shore))
                    return;

                float gap = Vector2.Distance(end, shore);
                if (gap <= 0.35f)
                {
                    cellProcessed[endpoint] = shore;
                }
                else if (gap <= maxDist)
                {
                    int steps = Mathf.Clamp(Mathf.CeilToInt(gap / 2f), 2, 8);
                    var bridge = new List<Vector2>(steps);
                    for (int s = 1; s <= steps; s++)
                        bridge.Add(Vector2.Lerp(end, shore, s / (float)steps));
                    if (extendStart)
                        cellProcessed.InsertRange(0, bridge);
                    else
                        cellProcessed.AddRange(bridge);
                }
            }

            endpoint = extendStart ? 0 : cellProcessed.Count - 1;
            Vector2 shorePoint = cellProcessed[endpoint];
            Vector2 approach = Vector2.zero;
            if (cellProcessed.Count >= 2)
            {
                int prevIdx = extendStart ? Mathf.Min(1, cellProcessed.Count - 1) : cellProcessed.Count - 2;
                approach = extendStart
                    ? (cellProcessed[0] - cellProcessed[prevIdx]).normalized
                    : (cellProcessed[cellProcessed.Count - 1] - cellProcessed[prevIdx]).normalized;
            }

            float halfCells = riverIndex == 0
                ? config.riverVisualRibbonFullWidthCellsMain * 0.5f
                : (config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                    ? config.riverVisualRibbonFullWidthCellsTributary * 0.5f
                    : config.riverVisualRibbonFullWidthCellsMain * 0.5f);
            float lakeRadius = TryEstimateLakeRadiusCells(grid, shorePoint, out float estRadius)
                ? estRadius
                : 6f;
            float fullWidth = halfCells * 2f;
            const int maxLakeInteriorVerts = 5;
            const int minLakeInteriorVerts = 3;
            float interiorCellStep = 1.05f;

            if (riverIndex > 0 && approach.sqrMagnitude > 1e-6f)
            {
                bool atLakeMouth = IsCellSpacePointInLakeBody(grid, shorePoint) ||
                    IsCellSpacePointWater(grid, shorePoint) ||
                    IsCellSpacePointInOrNearLake(grid, shorePoint, 4);
                if (!extendStart && atLakeMouth)
                {
                    ClampWebFusionLakeMouthInteriorVertices(grid, cellProcessed, extendStart, maxLakeInteriorVerts);
                    int inside = CountWebFusionLakeMouthInteriorVertices(grid, cellProcessed);
                    if (inside >= minLakeInteriorVerts)
                        return;

                    int steps = Mathf.Clamp(maxLakeInteriorVerts - inside, minLakeInteriorVerts, maxLakeInteriorVerts);
                    float overlap = steps * interiorCellStep;
                    endpoint = cellProcessed.Count - 1;
                    shorePoint = cellProcessed[endpoint];
                    int prevIdxEnd = cellProcessed.Count - 2;
                    approach = (cellProcessed[endpoint] - cellProcessed[prevIdxEnd]).normalized;
                    Vector2 tip = shorePoint;
                    for (int s = 1; s <= steps; s++)
                        cellProcessed.Add(tip + approach * (overlap * s / steps));

                    return;
                }

                if (extendStart && atLakeMouth)
                {
                    ClampWebFusionLakeMouthInteriorVertices(grid, cellProcessed, extendStart, maxLakeInteriorVerts);
                    int inside = CountWebFusionLakeMouthInteriorVertices(grid, cellProcessed);
                    if (inside >= minLakeInteriorVerts)
                        return;

                    int steps = Mathf.Clamp(maxLakeInteriorVerts - inside, minLakeInteriorVerts, maxLakeInteriorVerts);
                    float overlap = steps * interiorCellStep;
                    shorePoint = cellProcessed[0];
                    int prevIdxStart = Mathf.Min(1, cellProcessed.Count - 1);
                    approach = (cellProcessed[0] - cellProcessed[prevIdxStart]).normalized;
                    Vector2 tip = shorePoint;
                    var extension = new List<Vector2>(steps);
                    for (int s = 1; s <= steps; s++)
                        extension.Add(tip + approach * (overlap * s / steps));
                    extension.Reverse();
                    cellProcessed.InsertRange(0, extension);
                    return;
                }

                float overlapShort = Mathf.Clamp(fullWidth * 1.4f, 3f, maxLakeInteriorVerts * interiorCellStep);
                Vector2 mouth = shorePoint + approach * overlapShort;
                if (cellProcessed.Count >= 2)
                {
                    int prevIdx = extendStart ? 1 : cellProcessed.Count - 2;
                    if (WouldFoldBackBridge(cellProcessed[prevIdx], shorePoint, mouth))
                        return;
                }

                if (extendStart)
                    cellProcessed.Insert(0, mouth);
                else
                    cellProcessed.Add(mouth);
                return;
            }

            if (!TryGetLakeCentroidNearShore(grid, shorePoint, out Vector2 centroid))
                return;

            Vector2 toCenter = centroid - shorePoint;
            if (toCenter.sqrMagnitude < 0.04f)
                return;

            float overlapMain = Mathf.Clamp(fullWidth * 1.8f, 6f, lakeRadius * 0.45f);
            overlapMain = Mathf.Min(overlapMain, toCenter.magnitude - 0.35f);
            if (overlapMain < 0.25f)
                return;

            Vector2 mouthMain = shorePoint + toCenter.normalized * overlapMain;
            if (cellProcessed.Count >= 2)
            {
                int prevIdx = extendStart ? 1 : cellProcessed.Count - 2;
                if (WouldFoldBackBridge(cellProcessed[prevIdx], shorePoint, mouthMain))
                    return;
            }

            if (extendStart)
                cellProcessed.Insert(0, mouthMain);
            else
                cellProcessed.Add(mouthMain);
        }

        static void ApplyWebFusionTributaryConfluenceEnd(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null)
                return;
            if (!TryResolveTributaryMainJoinEndpointIndex(grid, config, cellProcessed, riverIndex, out int mainIdx))
            {
                SnapTributaryCenterlineToMainRiver(grid, cellProcessed, riverIndex, config);
                TuckTributaryMouthIntoMainRiver(grid, cellProcessed, riverIndex, config);
                return;
            }

            if (!TryResolveTributaryJoinOnMainRiver(grid, riverIndex, cellProcessed, config, out Vector2 join))
            {
                SnapTributaryCenterlineToMainRiver(grid, cellProcessed, riverIndex, config, mainIdx);
                TuckTributaryMouthIntoMainRiver(grid, cellProcessed, riverIndex, config, mainIdx);
                return;
            }

            bool atStart = mainIdx == 0;
            Vector2 end = cellProcessed[mainIdx];
            float gap = Vector2.Distance(end, join);
            if (gap > 0.35f)
            {
                float maxGap = ResolveConfluenceReachMaxGapCells(config);
                if (gap <= maxGap)
                {
                    int steps = Mathf.Clamp(Mathf.CeilToInt(gap / 1.0f), 2, 6);
                    if (atStart)
                    {
                        cellProcessed.RemoveAt(0);
                        for (int s = steps; s >= 1; s--)
                            cellProcessed.Insert(0, Vector2.Lerp(end, join, s / (float)steps));
                    }
                    else
                    {
                        cellProcessed.RemoveAt(mainIdx);
                        for (int s = 1; s <= steps; s++)
                            cellProcessed.Add(Vector2.Lerp(end, join, s / (float)steps));
                    }
                }
            }

            SnapTributaryCenterlineToMainRiver(grid, cellProcessed, riverIndex, config, mainIdx);
            TuckTributaryMouthIntoMainRiver(grid, cellProcessed, riverIndex, config, mainIdx);
        }

        static void ApplyWebFusionTributaryEndpointConstantWidth(
            List<float> halfWidths,
            int riverIndex,
            MapGenConfig config = null)
        {
            if (riverIndex <= 0 || halfWidths == null || halfWidths.Count < 3)
                return;
            if (config != null && config.uwpOwnedVisualPolicy)
                return;

            int n = halfWidths.Count;
            halfWidths[0] = halfWidths[1];
            halfWidths[n - 1] = halfWidths[n - 2];
        }

        static void ApplyWebFusionConfluenceConstantWidth(
            List<Vector2> cellPath,
            List<float> halfWidths,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSizeWorld)
        {
            if (config == null || riverIndex <= 0 || cellPath == null || halfWidths == null)
                return;
            int n = Mathf.Min(cellPath.Count, halfWidths.Count);
            if (n < 4)
                return;

            int pin = Mathf.Clamp(10, 4, n / 2);
            int last = n - 1;
            if (!IsCellSpacePointNearMainRiverCorridor(grid, config, cellSizeWorld, cellPath[last], 1.35f))
                return;

            int refIdx = Mathf.Clamp(n - pin - 1, 0, n - 1);
            float refHalf = halfWidths[refIdx];
            for (int k = 0; k < pin; k++)
                halfWidths[n - 1 - k] = refHalf;
        }

        static void ApplyWebFusionLakeMouthWidthTaper(
            GridSystem grid,
            List<Vector2> cellPath,
            List<float> halfWidths,
            MapGenConfig config,
            int riverIndex,
            float cellSizeWorld,
            bool atStart)
        {
            if (riverIndex > 0)
                return;
            if (grid == null || cellPath == null || halfWidths == null || config == null)
                return;
            int n = Mathf.Min(cellPath.Count, halfWidths.Count);
            if (n < 2)
                return;

            int endpoint = atStart ? 0 : n - 1;
            if (!atStart && riverIndex > 0 &&
                IsCellSpacePointNearMainRiverCorridor(grid, config, cellSizeWorld, cellPath[endpoint], 1.35f))
                return;

            bool taper = IsCellSpacePointInOrNearLake(grid, cellPath[endpoint], 6);
            if (!taper)
                return;

            int blend = Mathf.Clamp(config.lakeRiverMouthBlendCells + 5, 5, 16);
            for (int k = 0; k < blend && k < n; k++)
            {
                int i = atStart ? k : (n - 1 - k);
                float t = k / (float)Mathf.Max(1, blend - 1);
                halfWidths[i] *= Mathf.Lerp(0.12f, 1f, t * t);
            }
        }

        static bool PolylineRevisitsCell(List<Vector2> poly)
        {
            if (poly == null || poly.Count < 4)
                return false;
            var seen = new HashSet<long>();
            long prevKey = long.MinValue;
            for (int i = 0; i < poly.Count; i++)
            {
                int cx = Mathf.RoundToInt(poly[i].x);
                int cy = Mathf.RoundToInt(poly[i].y);
                long k = PackCellKey(cx, cy);
                if (k == prevKey)
                    continue;
                prevKey = k;
                if (!seen.Add(k))
                    return true;
            }

            return false;
        }

        static bool ShouldRejectLakeEmissaryVisualCenterline(List<Vector2> cellProcessed)
        {
            if (cellProcessed == null || cellProcessed.Count < 2)
                return true;
            if (PolylineSelfIntersectsXZCell(cellProcessed))
                return true;
            if (PolylineRevisitsCell(cellProcessed))
                return true;
            return PolylineWindinessTooHigh(cellProcessed, 3.75f);
        }

        static float PolylineMaxInteriorTurnDeg(List<Vector2> poly)
        {
            if (poly == null || poly.Count < 3)
                return 0f;
            float max = 0f;
            for (int i = 1; i < poly.Count - 1; i++)
                max = Mathf.Max(max, InteriorTurnAngleDeg(poly, i));
            return max;
        }

        static bool PolylineWindinessTooHigh(List<Vector2> poly, float maxRatio = 2.35f)
        {
            if (poly == null || poly.Count < 4)
                return false;
            float direct = Vector2.Distance(poly[0], poly[poly.Count - 1]);
            if (direct < 4f)
                return false;
            return PolylineLengthCellSpace(poly) / direct > maxRatio;
        }

        /// <summary>Validación compartida Fase4/Fase9: lazos, revisitas, sinuosidad extrema o demasiado recto.</summary>
        public static bool UwpTributaryPathRejected(List<Vector2> poly, MapGenConfig config, out string rejectReason)
        {
            rejectReason = null;
            if (poly == null || poly.Count < 2)
            {
                rejectReason = "short";
                return true;
            }

            if (PolylineSelfIntersectsXZCell(poly))
            {
                rejectReason = "self_intersect";
                return true;
            }

            if (PolylineRevisitsCell(poly))
            {
                rejectReason = "revisit";
                return true;
            }

            float windMax = config != null && config.uwpOwnedVisualPolicy ? 2.45f : 2.35f;
            if (PolylineWindinessTooHigh(poly, windMax))
            {
                rejectReason = "too_windy";
                return true;
            }

            if (config != null && config.uwpOwnedVisualPolicy && poly.Count >= 4)
            {
                float direct = Vector2.Distance(poly[0], poly[poly.Count - 1]);
                if (direct > 4f)
                {
                    float ratio = PolylineLengthCellSpace(poly) / direct;
                    bool allowStraight = config.riverSurfaceAllowStraightTrustedTributaries;
                    if (ratio < 1.15f && !allowStraight)
                    {
                        rejectReason = "too_straight";
                        return true;
                    }
                }
            }

            return false;
        }

        const float LakeFirstInlandApproachTailFreezeT01 = 0.70f;
        const float LakeFirstInlandMinDownstreamAlignDot = 0.35f;
        const float LakeFirstInlandSourceEmergenceSpanWorldCells = 5.75f;
        const int LakeFirstInlandSourceEmergenceMinCells = 6;
        const int LakeFirstInlandSourceEmergenceMaxCells = 16;
        const float LakeFirstInlandSourceWidthMinMul = 0.28f;
        /// <summary>
        /// Headwater = arroyo continuo: cuerpo ~0.85–1.1 celdas (0.8× vs antes; sin fragmentar).
        /// Mesh ligeramente por encima del carve para orilla blanca.
        /// </summary>
        const float LakeFirstHeadwaterSourceWidthMinMulMask = 0.72f;
        const float LakeFirstHeadwaterCarveMinHalfCells = 0.98f;
        const float LakeFirstHeadwaterSourceMinHalfCells = 0.90f;
        /// <summary>Body más ancho: stamp debe cubrir el ribbon (evitar charcos / foam-only).</summary>
        const float LakeFirstHeadwaterCarveBodyMaxCells = 1.55f;
        const float LakeFirstHeadwaterCarveJoinMaxCells = 1.95f;
        const float LakeFirstHeadwaterCarveBodyMul = 0.90f;
        /// <summary>Contrato canal Lake First (Headwater/main/inland/spill): mesh / carve ≈ 1.3.</summary>
        const float LakeFirstHeadwaterMeshOverCarveMul = 1.3f;
        const float LakeFirstChannelMeshOverCarveMul = LakeFirstHeadwaterMeshOverCarveMul;
        const float LakeFirstHeadwaterMeshMinHalfMul = 0.84f;
        const float LakeFirstHeadwaterSourceTaperAlongEnd = 0.55f;
        const float LakeFirstHeadwaterSourceTaperSpanWorldCells = 20f;
        const int LakeFirstHeadwaterSourceTaperMinCells = 14;
        const int LakeFirstHeadwaterSourceTaperMaxCells = 40;

        static bool TryResolveHeadwaterReceiverRiverIndex(GridSystem grid, int riverIndex, out int receiverRiverIndex)
        {
            receiverRiverIndex = -1;
            if (grid == null || riverIndex <= 0 ||
                UwpTributaryOriginUtility.GetOrigin(grid, riverIndex) != UwpTributaryOriginKind.HeadwaterFeeder)
                return false;
            if (grid.RiverReceiverIds == null || riverIndex >= grid.RiverReceiverIds.Count)
                return false;
            receiverRiverIndex = grid.RiverReceiverIds[riverIndex];
            return receiverRiverIndex > 0 &&
                grid.RiverCenterlinesCellSpace != null &&
                receiverRiverIndex < grid.RiverCenterlinesCellSpace.Count;
        }

        /// <summary>Nombre jerárquico por tipo: MainRiver / LakeSpill / InlandFeeder / HeadwaterFeeder.</summary>
        static string ResolveRiverSurfaceGameObjectName(GridSystem grid, int riverIndex)
        {
            if (riverIndex <= 0)
                return "Water_RiverSurface_MainRiver";

            UwpTributaryOriginKind kind = UwpTributaryOriginUtility.GetOrigin(grid, riverIndex);
            string typeName;
            switch (kind)
            {
                case UwpTributaryOriginKind.LakeSpill:
                    typeName = "LakeSpill";
                    break;
                case UwpTributaryOriginKind.InlandFeeder:
                    typeName = "InlandFeeder";
                    break;
                case UwpTributaryOriginKind.HeadwaterFeeder:
                    typeName = "HeadwaterFeeder";
                    break;
                default:
                    typeName = UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex)
                        ? "LakeSpill"
                        : "Tributary";
                    break;
            }

            return $"Water_RiverSurface_{typeName}_{riverIndex}";
        }

        static bool RiverSurfaceGameObjectExists(Transform waterRoot, GridSystem grid, int riverIndex)
        {
            if (waterRoot == null)
                return false;
            string primary = ResolveRiverSurfaceGameObjectName(grid, riverIndex);
            if (waterRoot.Find(primary) != null)
                return true;
            // Legacy names (lecturas previas / bake).
            string legacy = riverIndex == 0
                ? "Water_RiverSurface_Main"
                : $"Water_RiverSurface_Tributary_{riverIndex}";
            return waterRoot.Find(legacy) != null;
        }

        static float ResolveRiverRibbonHalfWidthCells(MapGenConfig config, int riverIndex)
        {
            if (config == null)
                return 1.2f;
            if (riverIndex == 0)
                return Mathf.Max(0.5f, config.riverVisualRibbonFullWidthCellsMain * 0.5f);
            return Mathf.Max(
                0.35f,
                (config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                    ? config.riverVisualRibbonFullWidthCellsTributary
                    : config.riverVisualRibbonFullWidthCellsMain) * 0.5f);
        }

        /// <summary>InlandFeeder/HeadwaterFeeder: rampa Y + fade alpha en origen interior.</summary>
        static bool UsesSupplementalFeederSourceEmergence(GridSystem grid, int riverIndex) =>
            grid != null && riverIndex > 0 &&
            (UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex) ||
             UwpTributaryOriginUtility.GetOrigin(grid, riverIndex) == UwpTributaryOriginKind.HeadwaterFeeder);

        static bool IsLakeFirstHeadwaterFeeder(GridSystem grid, int riverIndex) =>
            grid != null && riverIndex > 0 &&
            UwpTributaryOriginUtility.GetOrigin(grid, riverIndex) == UwpTributaryOriginKind.HeadwaterFeeder;

        static bool UsesLakeFirstSupplementalNarrowCarve(GridSystem grid, int riverIndex) =>
            UsesSupplementalFeederSourceEmergence(grid, riverIndex) ||
            IsLakeFirstHeadwaterFeeder(grid, riverIndex);

        static void LogLakeFirstSupplementalMeshHook(
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            string stage)
        {
            if (grid == null || config == null || riverIndex <= 0 ||
                !config.uwpLakeFirstHydrologyPipeline ||
                !UsesLakeFirstSupplementalNarrowCarve(grid, riverIndex))
                return;
            var kind = UwpTributaryOriginUtility.GetOrigin(grid, riverIndex);
            Debug.LogWarning(
                $"[LakeFirstSupplementalVisual] stage={stage} riverIndex={riverIndex} kind={kind} " +
                $"seed={config.seed} emergenceY={(UsesSupplementalFeederSourceEmergence(grid, riverIndex) ? 1 : 0)} " +
                $"narrowCarve=1");
        }

        static void LogLakeFirstSupplementalVisualAudit(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null || !config.uwpLakeFirstHydrologyPipeline)
                return;
            int inland = 0;
            int headwater = 0;
            if (grid.RiverOriginKinds != null)
            {
                for (int i = 0; i < grid.RiverOriginKinds.Count; i++)
                {
                    if (grid.RiverOriginKinds[i] == UwpTributaryOriginKind.InlandFeeder)
                        inland++;
                    else if (grid.RiverOriginKinds[i] == UwpTributaryOriginKind.HeadwaterFeeder)
                        headwater++;
                }
            }

            Debug.LogWarning(
                $"[LakeFirstSupplementalVisual] audit seed={config.seed} inland={inland} headwater={headwater} " +
                $"surfaces={grid.RiverVisualSurfaces?.Count ?? 0} pipeline=UwpFrozenSurface");
        }

        static void ApplyLakeFirstHeadwaterMeshContinuityFloor(
            List<float> halfWidths,
            float cellSize,
            GridSystem grid,
            int riverIndex)
        {
            if (!IsLakeFirstHeadwaterFeeder(grid, riverIndex) ||
                halfWidths == null || halfWidths.Count == 0)
                return;

            float minHalf = Mathf.Max(0.08f, cellSize * LakeFirstHeadwaterMeshMinHalfMul);
            for (int i = 0; i < halfWidths.Count; i++)
                halfWidths[i] = Mathf.Max(halfWidths[i], minHalf);
        }

        static int ResolveLakeFirstInlandFeederSourcePathIndex(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> cellPath,
            int riverIndex)
        {
            if (cellPath == null || cellPath.Count < 2)
                return 0;
            if (grid != null && riverIndex > 0 &&
                UwpTributaryOriginUtility.GetOrigin(grid, riverIndex) == UwpTributaryOriginKind.HeadwaterFeeder &&
                TryResolveHeadwaterReceiverRiverIndex(grid, riverIndex, out _))
                return 0;
            if (TryResolveTributaryMainJoinEndpointIndex(grid, config, cellPath, riverIndex, out int joinIdx) && joinIdx == 0)
                return cellPath.Count - 1;
            return 0;
        }

        static int ResolveLakeFirstInlandFeederSourceBlendCount(List<Vector2> cellPath, int sourceIdx, float cellSize)
        {
            if (cellPath == null || cellPath.Count < 2)
                return LakeFirstInlandSourceEmergenceMinCells;

            float targetSpanWorld = Mathf.Max(cellSize * 0.5f, cellSize * LakeFirstInlandSourceEmergenceSpanWorldCells);
            float acc = 0f;
            int blend = 2;
            if (sourceIdx == 0)
            {
                for (int i = 1; i < cellPath.Count; i++)
                {
                    acc += Vector2.Distance(cellPath[i], cellPath[i - 1]) * cellSize;
                    blend = i + 1;
                    if (acc >= targetSpanWorld)
                        break;
                }
            }
            else
            {
                for (int i = cellPath.Count - 2; i >= 0; i--)
                {
                    acc += Vector2.Distance(cellPath[i + 1], cellPath[i]) * cellSize;
                    blend = cellPath.Count - i;
                    if (acc >= targetSpanWorld)
                        break;
                }
            }

            return Mathf.Clamp(
                blend,
                LakeFirstInlandSourceEmergenceMinCells,
                Mathf.Min(LakeFirstInlandSourceEmergenceMaxCells, cellPath.Count - 1));
        }

        static void ApplyLakeFirstInlandFeederSourceEmergenceY(
            List<Vector3> centers,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSize,
            float surfaceWaterY)
        {
            if (grid == null || config == null || riverIndex <= 0 ||
                !config.uwpLakeFirstHydrologyPipeline ||
                !UsesSupplementalFeederSourceEmergence(grid, riverIndex) ||
                centers == null || cellPath == null ||
                centers.Count != cellPath.Count || centers.Count < 3)
                return;
            // Headwater: no hundir el ribbon bajo el terreno (provoca charcos / tramos enterrados).
            if (IsLakeFirstHeadwaterFeeder(grid, riverIndex))
                return;

            int sourceIdx = ResolveLakeFirstInlandFeederSourcePathIndex(grid, config, cellPath, riverIndex);
            int blend = Mathf.Min(
                ResolveLakeFirstInlandFeederSourceBlendCount(cellPath, sourceIdx, cellSize),
                centers.Count - 1);
            if (blend < 2)
                return;

            float channelY = WaterVisualPipelinePolicy.UsesUwpUnifiedWaterSurfaceLevel(config)
                ? ResolveUwpLakeMouthDisplayLevelY(grid, config)
                : surfaceWaterY;
            float ribbonLift = Mathf.Max(0f, config.riverSurfaceMeshExtraYOffsetWorld) +
                Mathf.Max(0f, config.riverRibbonAntiZFightYOffsetWorld);
            float carveWorld = Mathf.Max(0.01f, config.riverTerrainCarveDepthWorld);
            float belowSurface = Mathf.Max(cellSize * 0.28f, carveWorld * 0.95f + ribbonLift * 2.8f);

            int bodyIdx = sourceIdx == 0
                ? Mathf.Min(centers.Count - 1, blend)
                : Mathf.Max(0, centers.Count - 1 - blend);
            float bodyY = centers[bodyIdx].y;
            float sinkY = Mathf.Min(bodyY, channelY) - belowSurface;

            if (sourceIdx == 0)
            {
                for (int k = 0; k < blend; k++)
                {
                    float t = blend <= 1 ? 1f : k / (float)(blend - 1);
                    t = Mathf.SmoothStep(0f, 1f, t);
                    Vector3 p = centers[k];
                    p.y = Mathf.Lerp(sinkY, channelY, t);
                    centers[k] = p;
                }

                Vector3 p0 = centers[0];
                p0.y = Mathf.Min(p0.y, sinkY);
                centers[0] = p0;
                return;
            }

            for (int k = 0; k < blend; k++)
            {
                int idx = centers.Count - 1 - k;
                float t = blend <= 1 ? 1f : k / (float)(blend - 1);
                t = Mathf.SmoothStep(0f, 1f, t);
                Vector3 p = centers[idx];
                p.y = Mathf.Lerp(sinkY, channelY, t);
                centers[idx] = p;
            }

            int last = centers.Count - 1;
            Vector3 pl = centers[last];
            pl.y = Mathf.Min(pl.y, sinkY);
            centers[last] = pl;
        }

        static void ApplyLakeFirstInlandFeederSourceWidthTaper(
            List<float> halfWidths,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSize)
        {
            if (grid == null || config == null || riverIndex <= 0 ||
                !config.uwpLakeFirstHydrologyPipeline ||
                !UsesSupplementalFeederSourceEmergence(grid, riverIndex) ||
                halfWidths == null || cellPath == null ||
                halfWidths.Count != cellPath.Count || halfWidths.Count < 3)
                return;

            int n = halfWidths.Count;
            int sourceIdx = ResolveLakeFirstInlandFeederSourcePathIndex(grid, config, cellPath, riverIndex);
            int blend = Mathf.Min(
                ResolveLakeFirstInlandFeederSourceBlendCount(cellPath, sourceIdx, cellSize),
                n - 1);
            if (blend < 2)
                return;

            int bodyIdx = sourceIdx == 0
                ? Mathf.Min(n - 1, blend)
                : Mathf.Max(0, n - 1 - blend);
            float bodyW = halfWidths[bodyIdx];
            float minW = Mathf.Max(0.02f, bodyW * LakeFirstInlandSourceWidthMinMul);

            if (sourceIdx == 0)
            {
                for (int i = 0; i < blend; i++)
                {
                    float t = blend <= 1 ? 1f : i / (float)(blend - 1);
                    t = Mathf.SmoothStep(0f, 1f, t);
                    halfWidths[i] = Mathf.Lerp(minW, bodyW, t);
                }

                return;
            }

            for (int k = 0; k < blend; k++)
            {
                int i = n - 1 - k;
                float t = blend <= 1 ? 1f : k / (float)(blend - 1);
                t = Mathf.SmoothStep(0f, 1f, t);
                halfWidths[i] = Mathf.Lerp(minW, bodyW, t);
            }
        }

        static bool ShouldApplyLakeFirstInlandSourceFade(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> cellPath,
            int riverIndex,
            float cellSize,
            out bool fadeFromStart)
        {
            fadeFromStart = true;
            if (config == null || grid == null || riverIndex <= 0 || cellPath == null || cellPath.Count < 3 ||
                !config.uwpLakeFirstHydrologyPipeline || !UsesSupplementalFeederSourceEmergence(grid, riverIndex))
                return false;

            int sourceIdx = ResolveLakeFirstInlandFeederSourcePathIndex(grid, config, cellPath, riverIndex);
            fadeFromStart = sourceIdx == 0;
            if (IsTributaryEndpointNearMain(grid, config, cellSize, cellPath, sourceIdx))
                return false;
            if (IsTributaryEndpointNearLake(grid, cellPath, sourceIdx))
                return false;
            return true;
        }

        static float ComputeLakeFirstInlandSourceEndpointAlpha(int i, int n, int mouthBlend, float minAlpha)
        {
            mouthBlend = Mathf.Clamp(mouthBlend, 3, Mathf.Max(3, n - 1));
            if (i >= mouthBlend)
                return 1f;
            float t = (mouthBlend - 1 - i) / (float)Mathf.Max(1, mouthBlend - 1);
            float minA = Mathf.Clamp01(Mathf.Max(minAlpha, 0.04f));
            return Mathf.Lerp(minA, 1f, Mathf.SmoothStep(0f, 1f, t));
        }

        /// <summary>
        /// Lake-first inland: aplica meandro con cola protegida (sin ingress) y valida unión con el main.
        /// Debe coincidir con TryResolveLakeFirstFinalCenterlineCells para InlandFeeder.
        /// </summary>
        public static bool TryPrepareLakeFirstInlandFeederVisualCenterline(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> centerline,
            Vector2Int joinCell,
            int provisionalRiverIndex,
            out string rejectReason)
        {
            rejectReason = null;
            if (grid == null || config == null || centerline == null || centerline.Count < 4)
            {
                rejectReason = "short_centerline";
                return false;
            }

            int w = grid.Width;
            int h = grid.Height;
            if (w < 2 || h < 2)
            {
                rejectReason = "grid_too_small";
                return false;
            }

            UwpTributaryOriginUtility.PinEndpointConfluence(centerline, joinCell);

            // InlandFeeder = arroyo inland→main con recorrido del routing.
            // El meandro organic Lake-First creaba “colita de chancho” (S apretada + blob).
            // Misma política que tributarios inland clásicos: path lógico sin remandar.
            var normalized = NormalizeCenterlineSpacingForMesh(centerline, config);
            if (normalized == null || normalized.Count < 4)
            {
                rejectReason = "normalize_empty";
                return false;
            }

            centerline.Clear();
            centerline.AddRange(normalized);
            UwpTributaryOriginUtility.PinEndpointConfluence(centerline, joinCell);

            // Rechazar scribble residual del route soft-score.
            float lenCells = ComputePolylineLengthCells(centerline);
            float direct = Vector2.Distance(centerline[0], centerline[centerline.Count - 1]);
            if (direct > 4f && lenCells / direct > 1.42f)
            {
                rejectReason = "inland_too_windy";
                return false;
            }

            if (direct < 22f)
            {
                rejectReason = "inland_span_short";
                return false;
            }

            if (UwpTributaryPathRejected(centerline, config, out rejectReason))
                return false;

            if (!ValidateLakeFirstInlandMainJoinApproach(grid, config, centerline, 0, out rejectReason))
                return false;

            return true;
        }

        public static bool ValidateLakeFirstInlandMainJoinApproach(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> centerline,
            int receiverRiverIndex,
            out string rejectReason)
        {
            rejectReason = null;
            if (grid == null || config == null || centerline == null || centerline.Count < 2)
            {
                rejectReason = "short_centerline";
                return false;
            }

            if (grid.RiverCenterlinesCellSpace == null ||
                receiverRiverIndex < 0 ||
                receiverRiverIndex >= grid.RiverCenterlinesCellSpace.Count)
            {
                rejectReason = "invalid_receiver";
                return false;
            }

            var recvLine = grid.RiverCenterlinesCellSpace[receiverRiverIndex];
            if (recvLine == null || recvLine.Count < 2)
            {
                rejectReason = "invalid_receiver_line";
                return false;
            }

            int joinIdx = centerline.Count - 1;
            Vector2 join = centerline[joinIdx];
            int mainClIdx = ClosestCenterlineIndexForJoin(recvLine, join);
            Vector2 recvDown = RiverConfluenceUtility.ReceiverDownstreamAt(recvLine, mainClIdx);
            if (recvDown.sqrMagnitude < 1e-6f)
            {
                rejectReason = "receiver_dir_missing";
                return false;
            }

            recvDown.Normalize();
            Vector2 tribIn = RiverDendriticUtility.TributaryIncomingAt(centerline, joinIdx);
            if (tribIn.sqrMagnitude < 1e-6f)
            {
                rejectReason = "tributary_dir_missing";
                return false;
            }

            tribIn.Normalize();
            if (Vector2.Dot(tribIn, recvDown) < LakeFirstInlandMinDownstreamAlignDot)
            {
                rejectReason = "join_against_main_flow";
                return false;
            }

            float joinAngleDeg = RiverDendriticUtility.ComputeDirectedJoinAngleDeg(recvDown, tribIn);
            if (!RiverDendriticUtility.IsJoinAngleAcceptable(config, joinAngleDeg, out _, out _))
            {
                rejectReason = "join_angle_strict";
                return false;
            }

            return true;
        }

        static int ClosestCenterlineIndexForJoin(IReadOnlyList<Vector2> line, Vector2 join)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < line.Count; i++)
            {
                float dx = line[i].x - join.x;
                float dz = line[i].y - join.y;
                float d = dx * dx + dz * dz;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            return best;
        }

        static bool ShouldRejectTributaryVisualCenterline(List<Vector2> cellProcessed, MapGenConfig config = null)
        {
            if (cellProcessed == null || cellProcessed.Count < 2)
                return true;
            if (UwpTributaryPathRejected(cellProcessed, config, out _))
                return true;
            if (PolylineMaxInteriorTurnDeg(cellProcessed) >= (config != null && config.uwpOwnedVisualPolicy ? 98f : 98f))
                return true;
            return false;
        }

        static bool UwpTrustHydrologyPlacement(GridSystem grid, MapGenConfig config, int riverIndex, List<Vector2> rawPath)
        {
            if (config == null || !config.uwpOwnedVisualPolicy || grid == null || rawPath == null || rawPath.Count < 10)
                return false;
            if (!TryGetHydrologyRiverRecord(grid, riverIndex, out HydrologyRiverRecord record))
                return false;
            if (record.RiverClass != RiverClass.Tributary)
                return false;
            return record.ParentRiverId.HasValue ||
                   record.HierarchyFromConfluenceTrim ||
                   record.JoinVertexIndex >= 0 ||
                   record.AcceptedLengthCells >= 12;
        }

        static bool TryPrepareStandardTributaryCenterline(
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            List<Vector2> rawPath,
            bool logRm,
            out List<Vector2> cellProcessed,
            out RiverCenterlinePrepStats prepStats,
            out string rejectSubReason)
        {
            cellProcessed = null;
            prepStats = default;
            rejectSubReason = null;
            if (grid == null || config == null || rawPath == null || rawPath.Count < 2 || riverIndex <= 0)
                return false;

            bool trustHydrology = UwpTrustHydrologyPlacement(grid, config, riverIndex, rawPath);

            // UWP: curvas orgánicas ancladas al cauce; tributarios cortos usan path fiel (evita lazos Chaikin).
            if (config.uwpOwnedVisualPolicy)
            {
                if (rawPath.Count < 80)
                {
                    cellProcessed = BuildFaithfulFunctionalCenterline(rawPath, config);
                }
                else
                {
                    int maxPts = Mathf.Max(32, Mathf.CeilToInt(rawPath.Count * VisualCenterlineMaxInputMul));
                    cellProcessed = BuildVisualCenterlineSimple(grid, rawPath, config, maxPts, allowChaikin: true);
                    if (cellProcessed != null && cellProcessed.Count >= 6 &&
                        PolylineMaxInteriorTurnDeg(cellProcessed) < 38f &&
                        !UwpTributaryPathRejected(cellProcessed, config, out _))
                    {
                        var extra = TryChaikinNearRiverCells(grid, cellProcessed, config);
                        if (extra != null && extra.Count >= 2 &&
                            !UwpTributaryPathRejected(extra, config, out _))
                            cellProcessed = extra;
                    }

                    if (cellProcessed == null || cellProcessed.Count < 2 ||
                        UwpTributaryPathRejected(cellProcessed, config, out _))
                        cellProcessed = BuildFaithfulFunctionalCenterline(rawPath, config);
                }
            }
            else if (config.riverSurfaceUseSplineVisualCenterline)
            {
                cellProcessed = BuildSplineVisualCenterlineFromLogical(
                    grid,
                    rawPath,
                    config,
                    riverIndex,
                    out _,
                    out prepStats,
                    out _);
                if (cellProcessed != null && cellProcessed.Count >= 2 &&
                    ShouldRejectTributaryVisualCenterline(cellProcessed, config))
                    cellProcessed = null;
            }

            if (cellProcessed == null || cellProcessed.Count < 2)
                cellProcessed = BuildTributaryGridVisualCenterline(rawPath, grid, config);
            if (cellProcessed == null || cellProcessed.Count < 2)
                cellProcessed = BuildSnappedCellCenterPolyline(rawPath);
            if (cellProcessed == null || cellProcessed.Count < 2)
                cellProcessed = BuildFaithfulFunctionalCenterline(rawPath, config);
            if (cellProcessed == null || cellProcessed.Count < 2)
                return false;

            if (ApplyLakeAwareTributaryCenterlineTrim(grid, config, cellProcessed, riverIndex, logRm) &&
                (cellProcessed == null || cellProcessed.Count < 2))
            {
                rejectSubReason = "lake_trim_empty";
                cellProcessed = null;
                return false;
            }

            if (ShouldRejectTributaryVisualCenterline(cellProcessed, config))
            {
                if (config.uwpOwnedVisualPolicy)
                {
                    var faithful = BuildFaithfulFunctionalCenterline(rawPath, config);
                    if (faithful != null && faithful.Count >= 2 &&
                        (trustHydrology || !ShouldRejectTributaryVisualCenterline(faithful, config)))
                        cellProcessed = faithful;
                }

                if (ShouldRejectTributaryVisualCenterline(cellProcessed, config))
                {
                    if (trustHydrology)
                    {
                        var fallback = BuildFaithfulFunctionalCenterline(rawPath, config);
                        if (fallback == null || fallback.Count < 2)
                            fallback = BuildSnappedCellCenterPolyline(rawPath);
                        if (fallback != null && fallback.Count >= 2 &&
                            !UwpTributaryPathRejected(fallback, config, out _))
                            cellProcessed = fallback;
                    }

                    string hardReason = null;
                    bool hardReject = cellProcessed == null || cellProcessed.Count < 2 ||
                                      UwpTributaryPathRejected(cellProcessed, config, out hardReason) ||
                                      (!trustHydrology && ShouldRejectTributaryVisualCenterline(cellProcessed, config));
                    if (hardReject)
                    {
                        rejectSubReason = hardReason ?? "bad_geometry";
                        if (config.uwpOwnedVisualPolicy || logRm)
                        {
                            Debug.LogWarning(
                                $"[RiverTributaryVisualSkip] riverIndex={riverIndex} reason={rejectSubReason} " +
                                $"trust={(trustHydrology ? 1 : 0)} points={cellProcessed?.Count ?? 0} " +
                                $"maxTurn={(cellProcessed != null ? PolylineMaxInteriorTurnDeg(cellProcessed) : 0f):F1}");
                        }

                        cellProcessed = null;
                        return false;
                    }
                }
            }

            if (UwpTributaryPathRejected(cellProcessed, config, out string finalHardReason))
            {
                rejectSubReason = finalHardReason ?? "loop_geometry";
                if (config.uwpOwnedVisualPolicy)
                {
                    Debug.LogWarning(
                        $"[RiverTributaryVisualSkip] riverIndex={riverIndex} reason={rejectSubReason} stage=final");
                }

                cellProcessed = null;
                return false;
            }

            ApplyTributaryEndpointCenterlineJoins(grid, config, riverIndex, ref cellProcessed, lakeStartExpected: false);
            cellProcessed = FinalizeTributaryEndpointCenterline(cellProcessed, config);
            if (cellProcessed == null || cellProcessed.Count < 2)
                return false;

            float mainJoinRadiusCells = config.uwpOwnedVisualPolicy ? 40f : 10f;
            bool registeredConfluence = TryGetTributaryConfluenceCell(grid, riverIndex, out _, out _);
            if (!registeredConfluence &&
                !TributaryEndNearMainRiverCells(grid, riverIndex, cellProcessed, mainJoinRadiusCells) &&
                !TributaryPolylineTouchesMainCorridor(grid, cellProcessed, config))
            {
                if (trustHydrology)
                {
                    cellProcessed = BuildFaithfulFunctionalCenterline(rawPath, config);
                }
                else if (config.uwpOwnedVisualPolicy)
                {
                    var faithful = BuildFaithfulFunctionalCenterline(rawPath, config);
                    if (faithful != null && faithful.Count >= 2 &&
                        (TributaryEndNearMainRiverCells(grid, riverIndex, faithful, mainJoinRadiusCells + 8f) ||
                         TributaryPolylineTouchesMainCorridor(grid, faithful, config)))
                        cellProcessed = faithful;
                }

                if (cellProcessed == null || cellProcessed.Count < 2 ||
                    (!trustHydrology &&
                     !registeredConfluence &&
                     !TributaryEndNearMainRiverCells(grid, riverIndex, cellProcessed, mainJoinRadiusCells) &&
                     !TributaryPolylineTouchesMainCorridor(grid, cellProcessed, config)))
                {
                    rejectSubReason = "detached_from_main";
                    if (logRm || config.uwpOwnedVisualPolicy)
                    {
                        Debug.LogWarning(
                            $"[RiverTributaryVisualSkip] riverIndex={riverIndex} reason=detached_from_main " +
                            $"trust={(trustHydrology ? 1 : 0)} points={cellProcessed?.Count ?? 0} uwpOwned={config.uwpOwnedVisualPolicy}");
                    }

                    cellProcessed = null;
                    return false;
                }
            }

            if (cellProcessed == null || cellProcessed.Count < 2)
                return false;

            if (logRm && config.uwpOwnedVisualPolicy)
            {
                Debug.Log(
                    $"[RiverTributaryCenterlinePrep] riverIndex={riverIndex} rawPts={rawPath.Count} " +
                    $"meshPts={cellProcessed.Count} mode={(config.uwpOwnedVisualPolicy ? "organicSimple" : "default")}");
            }

            prepStats.RawPts = rawPath.Count;
            prepStats.ResampledPts = cellProcessed.Count;
            prepStats.SmoothedPts = cellProcessed.Count;
            return true;
        }

        /// <summary>Fase9 UWP: extiende el extremo lago del tributario dendrítico (mesh + máscara carve).</summary>
        static bool ApplyStandardTributaryLakeMouthFinalJoin(
            GridSystem grid,
            ref List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config,
            float cellSize)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null || riverIndex <= 0)
                return cellProcessed != null && cellProcessed.Count >= 2;

            if (!ShouldApplyTributaryLakeMouthFinalJoin(grid, cellProcessed, riverIndex, config))
                return cellProcessed.Count >= 2;

            int beforePts = cellProcessed.Count;
            Vector2 endBefore = cellProcessed[cellProcessed.Count - 1];
            float shoreDistBefore = float.MaxValue;
            TryGetNearestLakeShorePoint(
                grid, endBefore, ResolveLakeMouthApproachMaxDistCells(config), out Vector2 shoreBefore);
            if (shoreBefore != default)
                shoreDistBefore = Vector2.Distance(endBefore, shoreBefore);

            ApplyLakeRiverMouthVisualBridging(grid, cellProcessed, riverIndex, config);
            if (cellProcessed == null || cellProcessed.Count < 2)
                return false;

            ApplySplitModeConfluenceAndLakeEndpoints(grid, cellProcessed, riverIndex, config, cellSize);
            if (cellProcessed == null || cellProcessed.Count < 2)
                return false;

            TrimRiverSurfaceEndAtLakeMouth(grid, cellProcessed, config, riverIndex);
            TrimRiverSurfaceStaticWaterFromEnds(grid, cellProcessed, riverIndex, config);

            if (config.uwpOwnedVisualPolicy)
            {
                Vector2 endAfter = cellProcessed[cellProcessed.Count - 1];
                float shoreDistAfter = float.MaxValue;
                if (IsTributaryLakeOwner(grid, riverIndex))
                {
                    int lakeEp = TributaryTargetsMainConfluence(grid, riverIndex) ? cellProcessed.Count - 1 : 0;
                    if (TryResolveTributaryMainJoinEndpointIndex(grid, config, cellProcessed, riverIndex, out int mainEp))
                        lakeEp = mainEp == 0 ? cellProcessed.Count - 1 : 0;
                    if (TryGetTributaryOwnedLakeShorePoint(
                            grid, riverIndex, cellProcessed[Mathf.Clamp(lakeEp, 0, cellProcessed.Count - 1)],
                            ResolveLakeMouthApproachMaxDistCells(config), out Vector2 shoreAfter))
                        shoreDistAfter = Vector2.Distance(cellProcessed[lakeEp], shoreAfter);
                }
                else if (TryGetNearestLakeShorePoint(
                        grid, endAfter, ResolveLakeMouthApproachMaxDistCells(config), out Vector2 shoreAfter))
                {
                    shoreDistAfter = Vector2.Distance(endAfter, shoreAfter);
                }

                Debug.Log(
                    $"[TributaryLakeMouthJoin] riverIndex={riverIndex} owner={(IsTributaryLakeOwner(grid, riverIndex) ? 1 : 0)} " +
                    $"beforePts={beforePts} afterPts={cellProcessed.Count} shoreDistBefore={shoreDistBefore:F2} " +
                    $"shoreDistAfter={shoreDistAfter:F2} tribToMain={(TributaryTargetsMainConfluence(grid, riverIndex) ? 1 : 0)}");
            }

            return cellProcessed != null && cellProcessed.Count >= 2;
        }

        static bool TryPrepareLakeEmissaryCenterline(
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            List<Vector2> rawPath,
            bool logRm,
            out List<Vector2> cellProcessed,
            out RiverCenterlinePrepStats prepStats)
        {
            cellProcessed = null;
            prepStats = default;
            if (grid == null || config == null || rawPath == null || rawPath.Count < 2 || riverIndex <= 0)
                return false;
            if (!IsLakeEmissaryRiverIndex(grid, riverIndex))
                return false;

            cellProcessed = BuildTributaryGridVisualCenterline(rawPath, grid, config);
            if (cellProcessed == null || cellProcessed.Count < 2)
                cellProcessed = BuildSnappedCellCenterPolyline(rawPath);
            if (cellProcessed == null || cellProcessed.Count < 2)
                cellProcessed = BuildFaithfulFunctionalCenterline(rawPath, config);
            if (cellProcessed == null || cellProcessed.Count < 2)
                return false;

            TrimEmissaryHookTailIfRegressesFromMain(grid, config, cellProcessed, riverIndex);
            TrimRiverAtFirstMainCorridorContact(grid, config, cellProcessed, riverIndex);

            if (cellProcessed == null || cellProcessed.Count < 2)
                return false;

            if (ShouldRejectLakeEmissaryVisualCenterline(cellProcessed))
            {
                if (logRm)
                {
                    Debug.Log(
                        $"[RiverEmissaryVisualSkip] riverIndex={riverIndex} reason=bad_geometry " +
                        $"points={cellProcessed.Count} maxTurn={PolylineMaxInteriorTurnDeg(cellProcessed):F1} " +
                        $"revisit={(PolylineRevisitsCell(cellProcessed) ? 1 : 0)} self={(PolylineSelfIntersectsXZCell(cellProcessed) ? 1 : 0)}");
                }

                cellProcessed = null;
                return false;
            }

            ApplyTributaryEndpointCenterlineJoins(grid, config, riverIndex, ref cellProcessed, lakeStartExpected: true);
            cellProcessed = FinalizeTributaryEndpointCenterline(cellProcessed, config);

            if (cellProcessed == null || cellProcessed.Count < 2)
                return false;

            if (logRm)
            {
                Debug.Log(
                    $"[RiverEmissaryCenterlinePrep] riverIndex={riverIndex} rawPts={rawPath.Count} " +
                    $"usedPts={cellProcessed.Count} start=({cellProcessed[0].x:F1},{cellProcessed[0].y:F1}) " +
                    $"end=({cellProcessed[cellProcessed.Count - 1].x:F1},{cellProcessed[cellProcessed.Count - 1].y:F1})");
            }

            prepStats.RawPts = rawPath.Count;
            prepStats.ResampledPts = cellProcessed.Count;
            prepStats.SmoothedPts = cellProcessed.Count;
            return true;
        }

        /// <summary>Solo quita cola si tras la aproximación al troncal el path se aleja monótonamente (gancho U).</summary>
        static void TrimEmissaryHookTailIfRegressesFromMain(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> cellProcessed,
            int riverIndex)
        {
            if (grid == null || config == null || cellProcessed == null || cellProcessed.Count < 6 || riverIndex <= 0)
                return;
            if (!TryBuildMainRiverCorridorSampler(grid, config, grid.CellSizeWorld, out MainRiverCorridorSampler sampler))
                return;

            int n = cellProcessed.Count;
            int minIdx = 0;
            float minDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float d = Mathf.Sqrt(DistanceSqToPolylineCellSpace(cellProcessed[i], sampler.Line));
                if (d < minDist)
                {
                    minDist = d;
                    minIdx = i;
                }
            }

            if (minIdx >= n - 3)
                return;

            float prev = minDist;
            bool regressiveTail = true;
            for (int i = minIdx + 1; i < n; i++)
            {
                float d = Mathf.Sqrt(DistanceSqToPolylineCellSpace(cellProcessed[i], sampler.Line));
                if (d <= prev + 0.2f)
                {
                    regressiveTail = false;
                    break;
                }

                prev = d;
            }

            if (!regressiveTail || minIdx < 1)
                return;

            int removed = n - (minIdx + 1);
            cellProcessed.RemoveRange(minIdx + 1, removed);
            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[RiverEmissaryHookTrim] riverIndex={riverIndex} cutAt={minIdx} removedTail={removed} " +
                    $"remaining={cellProcessed.Count} minMainDist={minDist:F2}");
            }
        }

        /// <summary>
        /// Lago/trib→main: corta en la PRIMERA entrada al corredor del troncal (lado lago).
        /// No usar min-dist: en un V el mínimo está en la punta → deja el “pasa de largo”.
        /// </summary>
        static void TrimRiverAtFirstMainCorridorContact(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> cellProcessed,
            int riverIndex,
            bool forceSpillJoin = false)
        {
            if (grid == null || config == null || cellProcessed == null || cellProcessed.Count < 4 || riverIndex <= 0)
                return;
            if (!TryBuildMainRiverCorridorSampler(grid, config, grid.CellSizeWorld, out MainRiverCorridorSampler sampler))
                return;

            if (!TryResolveTributaryMainJoinEndpointIndex(grid, config, cellProcessed, riverIndex, out int mainEp))
                mainEp = ResolveVisualTributaryMainEndpointIndex(grid, cellProcessed);
            if (mainEp != 0 && mainEp != cellProcessed.Count - 1)
                return;

            // Orilla del canal (no el eje): primera vez que el path “toca” el main.
            // NO usar half visual aquí: radio gordo corta contacto LATERAL (path // main) → quiebre 90°.
            // El snap post-trim empuja la boca a la orilla visual.
            float entryRadius = Mathf.Max(sampler.CoreRadiusCells * 1.05f, sampler.RadiusCells * 0.72f);
            float entryRadiusSq = entryRadius * entryRadius;

            int enter = -1;
            if (mainEp == cellProcessed.Count - 1)
            {
                if (forceSpillJoin)
                {
                    // Spill: boca = mejor acercamiento en el último ~30% (no first-hit mid-path).
                    int tipStart = Mathf.Clamp(
                        Mathf.RoundToInt((cellProcessed.Count - 1) * 0.70f),
                        0,
                        cellProcessed.Count - 2);
                    float bestD = float.MaxValue;
                    int bestI = -1;
                    for (int i = tipStart; i < cellProcessed.Count; i++)
                    {
                        float d = DistanceSqToPolylineCellSpace(cellProcessed[i], sampler.Line);
                        if (d < bestD)
                        {
                            bestD = d;
                            bestI = i;
                        }
                    }

                    if (bestI >= 0 && bestD <= entryRadiusSq)
                        enter = bestI;
                }
                else
                {
                    for (int i = 0; i < cellProcessed.Count; i++)
                    {
                        if (DistanceSqToPolylineCellSpace(cellProcessed[i], sampler.Line) <= entryRadiusSq)
                        {
                            enter = i;
                            break;
                        }
                    }
                }
            }
            else
            {
                if (forceSpillJoin)
                {
                    int tipEnd = Mathf.Clamp(
                        Mathf.RoundToInt((cellProcessed.Count - 1) * 0.30f),
                        1,
                        cellProcessed.Count - 1);
                    float bestD = float.MaxValue;
                    int bestI = -1;
                    for (int i = tipEnd; i >= 0; i--)
                    {
                        float d = DistanceSqToPolylineCellSpace(cellProcessed[i], sampler.Line);
                        if (d < bestD)
                        {
                            bestD = d;
                            bestI = i;
                        }
                    }

                    if (bestI >= 0 && bestD <= entryRadiusSq)
                        enter = bestI;
                }
                else
                {
                    for (int i = cellProcessed.Count - 1; i >= 0; i--)
                    {
                        if (DistanceSqToPolylineCellSpace(cellProcessed[i], sampler.Line) <= entryRadiusSq)
                        {
                            enter = i;
                            break;
                        }
                    }
                }
            }

            if (enter < 0)
                return;

            // Evitar cortar casi al inicio si el lago está muy cerca del main *y* no hay cola.
            // Si hay overshoot (cola detrás de enter), sí cortar aunque enter sea temprano.
            int beyond = mainEp == cellProcessed.Count - 1
                ? cellProcessed.Count - 1 - enter
                : enter;
            // Spill forzado: cortar aunque beyond==1 (suele ser el pin de confluencia dentro del canal).
            if (beyond <= 0)
                return;
            if (beyond <= 1 && !forceSpillJoin)
                return;

            int minKeep = 2;
            if (!forceSpillJoin && beyond <= 3 && enter < minKeep && mainEp == cellProcessed.Count - 1)
            {
                // Path muy corto lago↔main: no recortar.
                if (cellProcessed.Count <= minKeep + 2)
                    return;
            }

            int before = cellProcessed.Count;
            if (mainEp == cellProcessed.Count - 1)
            {
                if (enter >= cellProcessed.Count - 1)
                    return;
                cellProcessed.RemoveRange(enter + 1, cellProcessed.Count - (enter + 1));
            }
            else
            {
                if (enter <= 0)
                    return;
                cellProcessed.RemoveRange(0, enter);
            }

            if (config.debugLogs || config.debugHydrologyNetwork || forceSpillJoin)
            {
                Debug.Log(
                    $"[RiverEmissaryMainTrim] riverIndex={riverIndex} enter={enter} " +
                    $"removed={(before - cellProcessed.Count)} remaining={cellProcessed.Count} " +
                    $"mode={(forceSpillJoin ? "first_entry_spill" : "first_entry")}");
            }
        }

        /// <summary>
        /// Headwater→receptor: corta en el primer contacto con el corredor del parent
        /// (evita overshoot + slide a lo largo del inland/main = gancho U).
        /// </summary>
        static void TrimTributaryAtFirstParentCorridorContact(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> cellProcessed,
            int riverIndex,
            int parentRiverIndex,
            bool forceEntry = false)
        {
            if (grid == null || config == null || cellProcessed == null || cellProcessed.Count < 4 ||
                riverIndex <= 0 || parentRiverIndex <= 0)
                return;
            if (!TryBuildRiverCorridorSampler(
                    grid, config, grid.CellSizeWorld, parentRiverIndex, out MainRiverCorridorSampler sampler))
                return;

            int mainEp = cellProcessed.Count - 1;
            float entryRadius = Mathf.Max(sampler.CoreRadiusCells * 0.95f, sampler.RadiusCells * 0.62f);
            float entryRadiusSq = entryRadius * entryRadius;

            int enter = -1;
            for (int i = 0; i < cellProcessed.Count; i++)
            {
                if (DistanceSqToPolylineCellSpace(cellProcessed[i], sampler.Line) <= entryRadiusSq)
                {
                    enter = i;
                    break;
                }
            }

            if (enter < 0)
                return;

            int beyond = mainEp - enter;
            if (beyond <= 0)
                return;
            if (beyond <= 1 && !forceEntry)
                return;
            if (enter >= cellProcessed.Count - 1)
                return;

            int before = cellProcessed.Count;
            cellProcessed.RemoveRange(enter + 1, cellProcessed.Count - (enter + 1));

            if (config.debugLogs || config.debugHydrologyNetwork || forceEntry)
            {
                Debug.Log(
                    $"[HeadwaterReceiverTrim] riverIndex={riverIndex} parent={parentRiverIndex} enter={enter} " +
                    $"removed={(before - cellProcessed.Count)} remaining={cellProcessed.Count}");
            }
        }

        /// <summary>
        /// Endereza los últimos puntos hacia la boca (T limpia headwater→inland, sin colita).
        /// </summary>
        static void StraightenTributaryMouthApproach(List<Vector2> line, int approachPts = 7)
        {
            if (line == null || line.Count < approachPts + 2)
                return;
            approachPts = Mathf.Clamp(approachPts, 3, Mathf.Min(12, line.Count - 2));
            int anchorIdx = line.Count - 1 - approachPts;
            Vector2 anchor = line[anchorIdx];
            Vector2 mouth = line[line.Count - 1];
            if ((mouth - anchor).sqrMagnitude < 1e-6f)
                return;
            for (int k = 1; k < approachPts; k++)
            {
                float t = k / (float)approachPts;
                line[anchorIdx + k] = Vector2.Lerp(anchor, mouth, t);
            }
        }

        /// <summary>
        /// Suaviza los últimos puntos hacia la boca sin forzar línea recta (evita cuña blanca 90°).
        /// </summary>
        static void SoftenLakeSpillMouthApproach(List<Vector2> line, int approachPts = 5)
        {
            if (line == null || line.Count < approachPts + 3)
                return;
            approachPts = Mathf.Clamp(approachPts, 3, Mathf.Min(8, line.Count - 3));
            int tip = line.Count - 1;
            int anchorIdx = tip - approachPts;
            Vector2 anchor = line[anchorIdx];
            Vector2 tipPt = line[tip];
            if ((tipPt - anchor).sqrMagnitude < 1e-6f)
                return;
            for (int k = 1; k < approachPts; k++)
            {
                float t = k / (float)approachPts;
                // Blend suave: conserva algo de la polilínea original (no escalera recta).
                Vector2 lerped = Vector2.Lerp(anchor, tipPt, t);
                line[anchorIdx + k] = Vector2.Lerp(line[anchorIdx + k], lerped, 0.55f);
            }
        }

        /// <summary>Spill/Inland→main: boca exactamente en orilla del corredor (no eje / no pin de confluencia).</summary>
        static void SnapLakeSpillMouthToMainBank(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> line,
            int riverIndex)
        {
            if (grid == null || config == null || line == null || line.Count < 2 || riverIndex <= 0)
                return;
            bool spillOrInland =
                UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex) ||
                UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex);
            if (!spillOrInland)
                return;
            if (!TryBuildMainRiverCorridorSampler(grid, config, grid.CellSizeWorld, out MainRiverCorridorSampler sampler))
                return;
            if (!TryResolveTributaryMainJoinEndpointIndex(grid, config, line, riverIndex, out int mainIdx))
                mainIdx = line.Count - 1;
            if (mainIdx != 0 && mainIdx != line.Count - 1)
                return;

            Vector2 mouth = line[mainIdx];
            float bestDist = float.MaxValue;
            Vector2 bestJoin = mouth;
            for (int i = 0; i < sampler.Line.Count - 1; i++)
            {
                Vector2 q = ClosestPointOnOpenSegment2D(mouth, sampler.Line[i], sampler.Line[i + 1]);
                float d = (mouth - q).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestJoin = q;
                }
            }

            Vector2 toAxis = bestJoin - mouth;
            float dist = toAxis.magnitude;
            float bankR = Mathf.Max(sampler.CoreRadiusCells * 1.05f, sampler.RadiusCells * 0.72f);
            // LakeSpill/Inland: alinear con half visual del Main (ApplyMainMeshOnlyWidthScale ensancha después).
            if (UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex) ||
                UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex))
                bankR = Mathf.Max(bankR, ResolveMainVisualBankRadiusCells(config, sampler));
            if (dist > 1e-4f && dist > bankR)
                line[mainIdx] = bestJoin - (toAxis / dist) * bankR;
            else if (dist <= bankR && dist > 1e-4f)
            {
                // Ya dentro del corredor: empujar hacia afuera a la orilla (desde el eje).
                Vector2 fromAxis = mouth - bestJoin;
                if (fromAxis.sqrMagnitude > 1e-8f)
                    line[mainIdx] = bestJoin + fromAxis.normalized * bankR;
                else
                {
                    Vector2 neighbor = mainIdx == 0
                        ? line[Mathf.Min(1, line.Count - 1)]
                        : line[Mathf.Max(0, line.Count - 2)];
                    Vector2 outward = neighbor - bestJoin;
                    if (outward.sqrMagnitude > 1e-8f)
                        line[mainIdx] = bestJoin + outward.normalized * bankR;
                }
            }
        }

        /// <summary>
        /// Orilla efectiva del Main en mundo de celdas tras meshOnlyMul (sampler de carve queda más estrecho).
        /// </summary>
        static float ResolveMainVisualBankRadiusCells(MapGenConfig config, MainRiverCorridorSampler sampler)
        {
            float samplerBank = sampler.RadiusCells > 0f
                ? Mathf.Max(sampler.CoreRadiusCells * 1.05f, sampler.RadiusCells * 0.72f)
                : 1.2f;
            if (config == null)
                return samplerBank;
            float half = Mathf.Max(0.75f, ResolveRiverRibbonHalfWidthCells(config, 0));
            float meshMul = Mathf.Clamp(config.riverSurfaceMainMeshOnlyWidthMul, 1f, 2.5f);
            float visualBank = half * meshMul * 0.92f;
            return Mathf.Max(samplerBank, visualBank);
        }

        /// <summary>Tras first-entry: un solo tip corto hacia el eje del main (sin teleport a confluencia).</summary>
        static void ApplyLakeSpillMainBankTipAfterFirstEntryTrim(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> line,
            int riverIndex)
        {
            if (grid == null || config == null || line == null || line.Count < 2 || riverIndex <= 0)
                return;
            if (!TryBuildMainRiverCorridorSampler(grid, config, grid.CellSizeWorld, out MainRiverCorridorSampler sampler))
                return;
            if (!TryResolveTributaryMainJoinEndpointIndex(grid, config, line, riverIndex, out int mainIdx))
                mainIdx = line.Count - 1;
            if (mainIdx != 0 && mainIdx != line.Count - 1)
                return;

            Vector2 mouth = line[mainIdx];
            Vector2 neighbor = mainIdx == 0
                ? line[Mathf.Min(1, line.Count - 1)]
                : line[Mathf.Max(0, line.Count - 2)];

            float bestDist = float.MaxValue;
            Vector2 bestJoin = mouth;
            for (int i = 0; i < sampler.Line.Count - 1; i++)
            {
                Vector2 q = ClosestPointOnOpenSegment2D(mouth, sampler.Line[i], sampler.Line[i + 1]);
                float d = (mouth - q).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestJoin = q;
                }
            }

            Vector2 approach = bestJoin - neighbor;
            if (approach.sqrMagnitude < 1e-8f)
                approach = bestJoin - mouth;
            if (approach.sqrMagnitude < 1e-8f)
                return;
            approach.Normalize();

            float tuck = Mathf.Clamp(sampler.CoreRadiusCells * 0.22f, 0.30f, 0.70f);
            Vector2 tip = bestJoin + approach * tuck;
            if (WouldFoldBackBridge(neighbor, mouth, tip))
                tip = bestJoin;

            line[mainIdx] = bestJoin;
            if (mainIdx == line.Count - 1)
                line.Add(tip);
            else
                line.Insert(0, tip);
        }

        static List<Vector2> BuildVisualCenterlineSimple(
            GridSystem grid,
            List<Vector2> rawPath,
            MapGenConfig config,
            int maxPts,
            bool allowChaikin)
        {
            if (rawPath == null || rawPath.Count < 2)
                return null;
            var pts = new List<Vector2>(rawPath);
            pts = DedupeConsecutiveCell(pts, DedupeCellEps);
            pts = RemoveNearNullSegmentsCell(pts, MinSegmentCellEps);
            pts = RemoveCollinearPointsCellFordAware(pts, grid, config);
            if (pts == null || pts.Count < 2)
                return null;

            float spacing = config.riverSurfaceVisualSpacingCells > 0.01f
                ? config.riverSurfaceVisualSpacingCells
                : 1f;
            spacing = Mathf.Clamp(spacing, 0.5f, 0.85f);
            maxPts = Mathf.Clamp(maxPts, 2, 8192);
            pts = ResampleUniformSpacingCell(pts, spacing, maxPts);
            if (allowChaikin)
                pts = TryChaikinNearRiverCells(grid, pts, config);
            if (pts.Count > maxPts)
                pts = ResampleUniformSpacingCell(pts, spacing, maxPts);
            return pts != null && pts.Count >= 2 ? pts : null;
        }

        static void LogRiverSurfaceCenterlinePrep(MapGenConfig config, int riverId, RiverCenterlinePrepStats s)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverSurfaceCenterlinePrep] riverId={riverId} rawPts={s.RawPts} dedupPts={s.DedupPts} simplifiedPts={s.SimplifiedPts} " +
                $"resampledPts={s.ResampledPts} smoothedPts={s.SmoothedPts} cornerDensePts={s.CornerDensePts} " +
                $"maxDeviationCells={s.MaxDeviationCells:F3}");
        }

        static List<Vector2> BuildVisualCenterlineFromLogical(
            GridSystem grid,
            List<Vector2> rawPath,
            MapGenConfig config,
            int riverIndex,
            out int fordCellsNear,
            out RiverCenterlinePrepStats prepStats)
        {
            fordCellsNear = 0;
            prepStats = default;
            if (rawPath == null || rawPath.Count < 2)
                return null;

            List<Vector2> pts;
            bool fallbackUsed = false;
            string fallbackReason = null;
            var logicalRef = new List<Vector2>(rawPath);
            float devLimit = config.riverSurfaceUseSplineVisualCenterline
                ? Mathf.Clamp(config.riverSurfaceSplineMaxDeviationCells, 0.1f, 2f)
                : AlignmentMaxDistCells;

            bool useSplineVisual = config.riverSurfaceUseSplineVisualCenterline &&
                (riverIndex == 0 || IsLakeEmissaryRiverIndex(grid, riverIndex));

            if (useSplineVisual)
            {
                pts = BuildOrganicVisualRiverCenterline(
                    grid,
                    rawPath,
                    config,
                    riverIndex,
                    out fordCellsNear,
                    out prepStats,
                    out RiverSplineBuildStats splineStats);
                if (pts == null || pts.Count < 2)
                {
                    fallbackUsed = true;
                    splineStats.FallbackUsed = true;
                    fallbackReason = string.IsNullOrEmpty(splineStats.FallbackReason)
                        ? "spline_rejected"
                        : splineStats.FallbackReason;
                    int maxPts = Mathf.Max(2, Mathf.CeilToInt(rawPath.Count * VisualCenterlineMaxInputMul));
                    pts = BuildVisualCenterlineSimple(grid, rawPath, config, maxPts, allowChaikin: true);
                    fordCellsNear = 0;
                    prepStats = default;
                    prepStats.RawPts = rawPath.Count;
                }
            }
            else
            {
                int maxPts = Mathf.Max(2, Mathf.CeilToInt(rawPath.Count * VisualCenterlineMaxInputMul));
                bool allowChaikin = riverIndex == 0;
                pts = BuildVisualCenterlineSimple(grid, rawPath, config, maxPts, allowChaikin: allowChaikin);
                prepStats.RawPts = rawPath.Count;
                prepStats.DedupPts = prepStats.SimplifiedPts = prepStats.ResampledPts = pts != null ? pts.Count : 0;
                if (pts != null)
                {
                    float maxDev = MeasurePolylineDeviation(pts, logicalRef, out _, out int over, devLimit);
                    if (over > 0 || maxDev > devLimit)
                    {
                        fallbackUsed = true;
                        fallbackReason = "alignment_failed";
                        pts = BuildVisualCenterlineSimple(grid, rawPath, config, maxPts, allowChaikin: true);
                    }
                }
            }

            if (pts == null || pts.Count < 2)
                return null;

            float maxDist = MeasurePolylineDeviation(pts, logicalRef, out float avgDist, out int pointsOver, devLimit);
            int pointsOutside = CountPointsOutsidePlayableRect(pts, grid.Width, grid.Height);
            int fordSamples = 0;
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            for (int i = 0; i < pts.Count; i++)
            {
                if (IsPointNearFord(grid, pts[i], fordD))
                    fordSamples++;
            }

            ClampPolylinePlayableCellSpace(pts, grid.Width, grid.Height);
            pointsOutside = CountPointsOutsidePlayableRect(pts, grid.Width, grid.Height);

            bool accepted = pointsOver == 0 && pointsOutside == 0 && maxDist <= devLimit;
            LogRiverSurfaceAlignmentFix(
                config,
                riverIndex,
                rawPath.Count,
                pts.Count,
                maxDist,
                pointsOver,
                fallbackUsed,
                fallbackReason);
            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[RiverSurfaceAlignment] riverId={riverIndex} samples={pts.Count} maxDeviationCells={maxDist:F3} " +
                    $"avgDeviationCells={avgDist:F3} pointsOverLimit={pointsOver} pointsOutsideMap={pointsOutside} " +
                    $"fordSamples={fordSamples} accepted={(accepted ? 1 : 0)}");
            }

            prepStats.SmoothedPts = prepStats.CornerDensePts = pts.Count;
            prepStats.MaxDeviationCells = maxDist;
            LogRiverSurfaceCenterlinePrep(config, riverIndex, prepStats);

            if (fordCellsNear == 0)
            {
                for (int i = 0; i < pts.Count; i++)
                {
                    if (IsPointNearFord(grid, pts[i], fordD))
                        fordCellsNear++;
                }
            }

            if (config.riverSurfaceUseSplineVisualCenterline && !fallbackUsed && (pointsOver > 0 || maxDist > devLimit + 0.01f))
            {
                fallbackUsed = true;
                fallbackReason = "spline_alignment";
                LogRiverSurfaceSpline(config, riverIndex, new RiverSplineBuildStats
                {
                    RawPts = rawPath.Count,
                    FallbackUsed = true,
                    FallbackReason = fallbackReason,
                    Accepted = false
                });
                int maxPtsFb = Mathf.Max(2, Mathf.CeilToInt(rawPath.Count * VisualCenterlineMaxInputMul));
                pts = BuildVisualCenterlineSimple(grid, rawPath, config, maxPtsFb, allowChaikin: true);
                if (pts != null)
                {
                    ClampPolylinePlayableCellSpace(pts, grid.Width, grid.Height);
                    maxDist = MeasurePolylineDeviation(pts, logicalRef, out avgDist, out pointsOver, devLimit);
                    pointsOutside = CountPointsOutsidePlayableRect(pts, grid.Width, grid.Height);
                }
            }

            pts = PolishOrganicCenterlinePolyline(pts, config);
            return pts;
        }

        static void LogRiverSurfaceSource(GridSystem grid, MapGenConfig config, int riverId, List<Vector2> raw)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            int unique = 0;
            var hs = new HashSet<long>();
            int fordNear = 0;
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            if (raw != null)
            {
                for (int i = 0; i < raw.Count; i++)
                {
                    int cx = Mathf.FloorToInt(raw[i].x);
                    int cy = Mathf.FloorToInt(raw[i].y);
                    long k = PackCellKey(cx, cy);
                    if (hs.Add(k))
                        unique++;
                    if (IsPointNearFord(grid, raw[i], fordD))
                        fordNear++;
                }
            }

            Debug.Log(
                $"[RiverSurfaceSource] riverId={riverId} source=RiverCenterlinesCellSpace inputPoints={(raw != null ? raw.Count : 0)} " +
                $"uniqueCells={unique} fordCellsNear={fordNear} usesVisualMaskAsGeometry=0");
        }

        static void LogRiverSurfaceAlignment(GridSystem grid, MapGenConfig config, int riverId, List<Vector2> visualCells)
        {
            if (config == null || grid == null || visualCells == null ||
                (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            int w = grid.Width;
            int h = grid.Height;
            float maxD = 0f;
            float sumD = 0f;
            int far = 0;
            int fordZones = 0;
            int fordAligned = 0;
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            for (int i = 0; i < visualCells.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(visualCells[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(visualCells[i].y), 0, h - 1);
                float d = DistanceToNearestRiverCellChebyshev(grid, cx, cy);
                maxD = Mathf.Max(maxD, d);
                sumD += d;
                if (d > AlignmentMaxDistCells)
                    far++;
                if (IsPointNearFord(grid, visualCells[i], fordD + 1))
                {
                    fordZones++;
                    if (d <= 1.05f)
                        fordAligned++;
                }
            }

            float avg = visualCells.Count > 0 ? sumD / visualCells.Count : 0f;
            Debug.Log(
                $"[RiverSurfaceAlignment] riverId={riverId} visualPoints={visualCells.Count} maxDistToRiverCell={maxD:F2} " +
                $"avgDistToRiverCell={avg:F2} pointsFarFromRiver={far} fordZonesChecked={fordZones} fordZonesAligned={fordAligned}");
        }

        static float DistanceToNearestRiverCellChebyshev(GridSystem grid, int cx, int cy)
        {
            if (grid == null)
                return 99f;
            if (grid.GetCell(cx, cy).type == CellType.River)
                return 0f;
            int w = grid.Width;
            int h = grid.Height;
            float best = 99f;
            for (int r = 1; r <= 4; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = cx + dx;
                        int nz = cy + dz;
                        if ((uint)nx >= (uint)w || (uint)nz >= (uint)h)
                            continue;
                        if (grid.GetCell(nx, nz).type != CellType.River)
                            continue;
                        float d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
                        if (d < best)
                            best = d;
                    }
                }

                if (best < 99f)
                    break;
            }

            return best;
        }

        static void ResolveRiverSurfaceWidthBands(
            MapGenConfig config,
            float baseHalfW,
            int riverIndex,
            GridSystem grid,
            out float minHalfW,
            out float normalHalfW,
            out float maxHalfW)
        {
            minHalfW = Mathf.Max(0.02f, baseHalfW);
            if (riverIndex > 0 && config != null && config.uwpOwnedVisualPolicy)
            {
                bool lakeFirstTrib = UsesLakeFirstMainJoinMeshTreatment(grid, config, riverIndex);
                if (lakeFirstTrib)
                {
                    float lakeFirstNormalMul = Mathf.Clamp(config.riverSurfaceVisualNormalWidthMul, 1.25f, 2.05f);
                    // Inland debe leerse más fino que spill; el mismo mul → blobs ~spill.
                    if (UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex))
                        lakeFirstNormalMul = Mathf.Clamp(lakeFirstNormalMul * 0.48f, 1.02f, 1.22f);
                    normalHalfW = minHalfW * lakeFirstNormalMul;
                    maxHalfW = normalHalfW * 1.1f;
                    return;
                }

                float visCap = 1.2f;
                float visMul = Mathf.Clamp(config.riverSurfaceTributaryVisualWidthMul, 1f, visCap);
                normalHalfW = minHalfW * visMul;
                maxHalfW = normalHalfW * 1.12f;
                return;
            }

            if (riverIndex > 0 && config != null && config.riverDendriticNetworkEnabled && grid != null)
            {
                float ratio = grid.RiverWidthRatioToMain != null && riverIndex < grid.RiverWidthRatioToMain.Count
                    ? grid.RiverWidthRatioToMain[riverIndex]
                    : RiverDendriticUtility.VariableWidthRatioToMain(
                        config,
                        RiverDendriticUtility.RoleForPlacement(riverIndex, 0, 48, 0),
                        riverIndex,
                        48,
                        grid.RiverCenterlinesCellSpace != null && grid.RiverCenterlinesCellSpace.Count > 0 && grid.RiverCenterlinesCellSpace[0] != null
                            ? grid.RiverCenterlinesCellSpace[0].Count
                            : 0);
                float mainHalfRef = RiverDendriticUtility.MainReferenceHalfWidthWorld(config, grid.CellSizeWorld);
                normalHalfW = mainHalfRef * ratio;
                minHalfW = mainHalfRef * ratio * 0.9f;
                maxHalfW = mainHalfRef * ratio * 1.15f;
                return;
            }

            if (riverIndex > 0 && config != null)
            {
                if (config.riverSurfaceTributaryWidthFixEnabled)
                {
                    float visMul = Mathf.Clamp(config.riverSurfaceTributaryVisualWidthMul, 1f, 3f);
                    float tribBandMinMul = Mathf.Clamp(config.riverSurfaceTributaryMinWidthMul, 1f, visMul);
                    float tribBandMaxMul = Mathf.Clamp(config.riverSurfaceTributaryMaxWidthMul, visMul, 3.5f);
                    normalHalfW = baseHalfW * visMul;
                    minHalfW = baseHalfW * tribBandMinMul;
                    maxHalfW = baseHalfW * tribBandMaxMul;
                    return;
                }

                float tribMul = Mathf.Clamp(config.riverSurfaceTributaryWidthMul, 0.35f, 1f);
                float tribMaxMul = Mathf.Clamp(config.riverSurfaceTributaryMaxWidthMul, tribMul, 1.2f);
                float tribMinMul = Mathf.Clamp(config.riverSurfaceTributaryMinWidthMul, 0.25f, tribMul);
                minHalfW *= tribMinMul;
                normalHalfW = minHalfW * tribMul;
                maxHalfW = minHalfW * tribMaxMul;
                return;
            }

            float normalMul = config != null
                ? Mathf.Clamp(config.riverSurfaceVisualNormalWidthMul, 1.25f, 3f)
                : 2f;
            float maxMul = config != null
                ? Mathf.Clamp(config.riverSurfaceVisualMaxWidthMul, normalMul, 4f)
                : 3f;
            normalHalfW = minHalfW * normalMul;
            maxHalfW = minHalfW * maxMul;
        }

        static List<float> BuildOrganicHalfWidths(
            List<Vector2> cellPath,
            float baseHalfW,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            out float hwMin,
            out float hwMax,
            out float maxDeltaStep,
            out int fordDampApplied)
        {
            fordDampApplied = 0;
            maxDeltaStep = 0f;
            ResolveRiverSurfaceWidthBands(config, baseHalfW, riverIndex, grid, out float minHalfW, out float normalHalfW, out float maxHalfW);
            hwMin = minHalfW;
            hwMax = maxHalfW;
            int n = cellPath != null ? cellPath.Count : 0;
            var hw = new List<float>(n);
            if (n < 1)
                return hw;

            float totalLen = PolylineLengthCellSpace(cellPath);
            float acc = 0f;
            float phase = riverIndex * 17.31f + config.seed * 0.013f;
            float organicFrac = Mathf.Clamp(config.riverSurfaceWidthOrganicVarFrac, 0f, 0.2f);
            if (riverIndex == 0)
                organicFrac *= 0.35f;
            float sineAmp = organicFrac;
            float noiseAmp = organicFrac * 0.5f;
            float noiseScale = Mathf.Max(0.002f, config.riverSurfaceWidthNoiseScale);
            if (riverIndex == 0)
                noiseScale *= 0.45f;
            float fordMinW = minHalfW * Mathf.Clamp(config.riverSurfaceFordMinWidthMul, 1f, 1.25f);
            float fordMaxW = minHalfW * Mathf.Clamp(config.riverSurfaceFordMaxWidthMul, config.riverSurfaceFordMinWidthMul, 1.35f);
            float fade = 0.1f;
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            float cellSizeWorld = config != null && config.cellSizeWorld > 0.01f ? config.cellSizeWorld : 3f;
            bool mainOrganicBand = riverIndex == 0;
            bool lakeFirstTrib = UsesLakeFirstMainJoinMeshTreatment(grid, config, riverIndex);
            if (mainOrganicBand)
            {
                normalHalfW = Mathf.Max(baseHalfW, minHalfW);
                minHalfW = normalHalfW * 0.9f;
                maxHalfW = normalHalfW * 1.1f;
            }

            float swing = Mathf.Max(0.01f, maxHalfW - minHalfW);
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                    acc += Vector2.Distance(cellPath[i], cellPath[i - 1]);
                float t01 = totalLen > 1e-4f ? acc / totalLen : 0f;
                float endFade = MeanderEdgeFade(t01, fade);
                float ang = InteriorTurnAngleDeg(cellPath, i);
                float bendFade = ang >= JoinAngleHardDeg ? 0.55f : (ang >= JoinAngleSmoothDeg ? 0.8f : 1f);
                bool inlandFeederWidth = lakeFirstTrib &&
                    UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex);
                if (lakeFirstTrib && !inlandFeederWidth)
                {
                    bendFade = ang >= JoinAngleHardDeg ? 1.32f : (ang >= JoinAngleSmoothDeg ? 1.18f : 1.1f);
                }
                float sine = Mathf.Sin(acc * 0.11f + phase) * sineAmp;
                float n01 = Mathf.PerlinNoise(acc * noiseScale + phase, riverIndex * 0.17f);
                float noise = (n01 * 2f - 1f) * noiseAmp;
                float wv;
                if (mainOrganicBand)
                {
                    float varMul = Mathf.Clamp(organicFrac * 1.15f, 0f, 0.11f);
                    wv = normalHalfW * (1f + (sine + noise) * varMul * endFade * bendFade);
                    wv = Mathf.Clamp(wv, minHalfW, maxHalfW);
                    float turnCap = MaxHalfWidthTurnCapWorld(cellPath, i, cellSizeWorld);
                    wv = Mathf.Min(wv, turnCap);
                }
                else
                {
                    wv = normalHalfW + (sine + noise) * swing * 0.5f * endFade * bendFade;
                    wv = Mathf.Clamp(wv, minHalfW, maxHalfW);
                    if (riverIndex > 0)
                    {
                        float turnCap = MaxHalfWidthTurnCapWorld(cellPath, i, cellSizeWorld);
                        if (lakeFirstTrib && !inlandFeederWidth)
                        {
                            float curveFloor = normalHalfW * (ang >= JoinAngleHardDeg ? 1.22f : (ang >= JoinAngleSmoothDeg ? 1.14f : 1.08f));
                            wv = Mathf.Max(wv, curveFloor);
                        }
                        else
                        {
                            wv = Mathf.Min(wv, turnCap);
                        }
                    }
                }

                if (IsPointNearFord(grid, cellPath[i], fordD))
                {
                    wv = Mathf.Clamp(wv, fordMinW, fordMaxW);
                    fordDampApplied++;
                }

                hw.Add(Mathf.Max(0.02f, wv));
            }

            if (n >= 5)
            {
                var smoothed = new List<float>(hw);
                for (int i = 2; i < n - 2; i++)
                {
                    smoothed[i] = (hw[i - 2] + hw[i - 1] + hw[i] * 2f + hw[i + 1] + hw[i + 2]) * 0.16666667f;
                }

                for (int i = 1; i < n - 1; i++)
                    smoothed[i] = (smoothed[i - 1] + smoothed[i] * 2f + smoothed[i + 1]) * 0.25f;
                hw = smoothed;
            }
            else if (n >= 3)
            {
                var smoothed = new List<float>(hw);
                for (int i = 1; i < n - 1; i++)
                    smoothed[i] = (hw[i - 1] + hw[i] * 2f + hw[i + 1]) * 0.25f;
                hw = smoothed;
            }

            float maxStepAllowed = swing * (riverIndex == 0 ? 0.12f : (lakeFirstTrib ? 0.22f : 0.14f));
            for (int i = 1; i < n; i++)
            {
                float step = hw[i] - hw[i - 1];
                if (Mathf.Abs(step) > maxStepAllowed)
                    hw[i] = hw[i - 1] + Mathf.Sign(step) * maxStepAllowed;
            }

            if (riverIndex == 0)
                StabilizeMainRiverHalfWidths(hw, normalHalfW, maxStepAllowed);

            float avg = 0f;
            for (int i = 0; i < n; i++)
            {
                hwMin = Mathf.Min(hwMin, hw[i]);
                hwMax = Mathf.Max(hwMax, hw[i]);
                avg += hw[i];
                if (i > 0)
                    maxDeltaStep = Mathf.Max(maxDeltaStep, Mathf.Abs(hw[i] - hw[i - 1]));
            }

            if (n > 0)
                avg /= n;
            if (riverIndex == 0)
                LastMainRiverAvgHalfWidthWorld = avg;
            else
            {
                int tribSlot = riverIndex - 1;
                while (s_tributaryAvgHalfWidthWorld.Count <= tribSlot)
                    s_tributaryAvgHalfWidthWorld.Add(0f);
                s_tributaryAvgHalfWidthWorld[tribSlot] = avg;
            }

            LogRiverSurfaceWidthScale(
                config,
                riverIndex,
                baseHalfW,
                minHalfW,
                normalHalfW,
                maxHalfW,
                hwMin,
                hwMax,
                avg,
                maxDeltaStep,
                fordDampApplied);

            return hw;
        }

        /// <summary>Tope de medio-ancho en curvas cerradas (evita pliegue del ribbon).</summary>
        static float MaxHalfWidthTurnCapWorld(List<Vector2> path, int i, float cellSizeWorld)
        {
            if (path == null || path.Count < 3 || i <= 0 || i >= path.Count - 1)
                return float.MaxValue;
            float angDeg = InteriorTurnAngleDeg(path, i);
            if (angDeg < 20f)
                return float.MaxValue;
            float lenIn = Vector2.Distance(path[i], path[i - 1]) * cellSizeWorld;
            float lenOut = Vector2.Distance(path[i + 1], path[i]) * cellSizeWorld;
            float segWorld = Mathf.Min(lenIn, lenOut);
            float tanHalf = Mathf.Tan(angDeg * 0.5f * Mathf.Deg2Rad);
            tanHalf = Mathf.Max(0.14f, tanHalf);
            float cap = segWorld / (2.05f * tanHalf);
            return Mathf.Max(cellSizeWorld * 0.58f, cap);
        }

        /// <summary>Suaviza ancho del troncal: sin mínimos locales tipo embudo.</summary>
        static void StabilizeMainRiverHalfWidths(List<float> hw, float normalHalfW, float maxStepAllowed)
        {
            int n = hw != null ? hw.Count : 0;
            if (n < 5 || normalHalfW < 1e-4f)
                return;

            const int win = 3;
            float floorW = normalHalfW * 0.9f;
            float ceilW = normalHalfW * 1.1f;
            var tmp = new List<float>(hw);
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                int count = 0;
                for (int j = Mathf.Max(0, i - win); j <= Mathf.Min(n - 1, i + win); j++)
                {
                    sum += hw[j];
                    count++;
                }

                tmp[i] = count > 0 ? sum / count : hw[i];
            }

            for (int i = 0; i < n; i++)
                hw[i] = Mathf.Clamp(tmp[i], floorW, ceilW);

            if (maxStepAllowed > 1e-6f)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 1; i < n; i++)
                    {
                        float step = hw[i] - hw[i - 1];
                        if (Mathf.Abs(step) > maxStepAllowed)
                            hw[i] = hw[i - 1] + Mathf.Sign(step) * maxStepAllowed;
                    }

                    for (int i = n - 2; i >= 0; i--)
                    {
                        float step = hw[i] - hw[i + 1];
                        if (Mathf.Abs(step) > maxStepAllowed)
                            hw[i] = hw[i + 1] + Mathf.Sign(step) * maxStepAllowed;
                    }
                }
            }
        }

        public static float GetTributaryAvgHalfWidthWorld(int tributaryRiverIndex)
        {
            int slot = tributaryRiverIndex - 1;
            if (slot < 0 || slot >= s_tributaryAvgHalfWidthWorld.Count)
                return 0f;
            return s_tributaryAvgHalfWidthWorld[slot];
        }

        public static float GetTributaryAvgHalfWidthWorldMean()
        {
            if (s_tributaryAvgHalfWidthWorld.Count == 0)
                return 0f;
            float sum = 0f;
            for (int i = 0; i < s_tributaryAvgHalfWidthWorld.Count; i++)
                sum += s_tributaryAvgHalfWidthWorld[i];
            return sum / s_tributaryAvgHalfWidthWorld.Count;
        }

        static void LogRiverSurfaceWidthScale(
            MapGenConfig config,
            int riverId,
            float oldBaseHalfWidth,
            float minHalfWidth,
            float normalHalfWidth,
            float maxHalfWidth,
            float finalMinHalfWidth,
            float finalMaxHalfWidth,
            float avgHalfWidth,
            float maxWidthStep,
            int fordDampApplied)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            bool widthOk = riverId <= 0
                ? finalMinHalfWidth >= oldBaseHalfWidth - 0.001f &&
                  finalMaxHalfWidth <= maxHalfWidth + 0.001f &&
                  normalHalfWidth >= oldBaseHalfWidth * 1.99f
                : finalMinHalfWidth >= minHalfWidth - 0.001f &&
                  finalMaxHalfWidth <= maxHalfWidth + 0.001f &&
                  avgHalfWidth >= minHalfWidth * 0.9f;
            string riverRole = riverId <= 0 ? "main" : "tributary";
            Debug.Log(
                $"[RiverSurfaceWidth] riverId={riverId} role={riverRole} oldBaseHalfWidth={oldBaseHalfWidth:F3} " +
                $"avgHalfWidth={avgHalfWidth:F3} minHalf={finalMinHalfWidth:F3} maxHalf={finalMaxHalfWidth:F3}");
            Debug.Log(
                $"[RiverSurfaceWidthScale] riverId={riverId} role={riverRole} oldBaseHalfWidth={oldBaseHalfWidth:F3} finalMinHalfWidth={finalMinHalfWidth:F3} " +
                $"normalHalfWidth={normalHalfWidth:F3} finalMaxHalfWidth={finalMaxHalfWidth:F3} avgHalfWidth={avgHalfWidth:F3} " +
                $"minObservedHalfWidth={finalMinHalfWidth:F3} maxObservedHalfWidth={finalMaxHalfWidth:F3} maxWidthStep={maxWidthStep:F4} " +
                $"fordDampApplied={fordDampApplied} widthPolicy={(riverId > 0 ? "tributary_visual_fix" : "main_2x_3x")} accepted={(widthOk ? 1 : 0)}");
            if (riverId > 0 && config != null &&
                (config.riverSurfaceTributaryWidthDebugLogs || config.debugLogs || config.debugHydrologyNetwork))
            {
                float mainRef = LastMainRiverAvgHalfWidthWorld > 0.01f
                    ? LastMainRiverAvgHalfWidthWorld
                    : oldBaseHalfWidth * Mathf.Clamp(config.riverSurfaceVisualNormalWidthMul, 1.25f, 3f);
                Debug.Log(
                    $"[RiverTributaryWidthAudit] riverId={riverId} parentId=0 baseHalfWidth={oldBaseHalfWidth:F3} " +
                    $"finalAvgHalfWidth={avgHalfWidth:F3} mainWidthReference={mainRef:F3} " +
                    $"tributaryVisualWidthMul={config.riverSurfaceTributaryVisualWidthMul:F2}");
            }
        }

        static void ApplyTributaryConfluenceVisualHalfWidths(
            GridSystem grid,
            List<Vector2> cellPath,
            List<float> halfWidths,
            MapGenConfig config,
            int riverIndex)
        {
            if (config == null || riverIndex <= 0 || cellPath == null || halfWidths == null || grid == null)
                return;
            int n = Mathf.Min(cellPath.Count, halfWidths.Count);
            if (n < 3)
                return;

            int joinIdx;
            if (config.uwpOwnedVisualPolicy)
            {
                TryResolveTributaryJoinIndexFromFinalPath(grid, config, cellPath, riverIndex, out joinIdx);
            }
            else
            {
                joinIdx = n - 1;
                while (joinIdx > 0)
                {
                    int cx = Mathf.RoundToInt(cellPath[joinIdx].x);
                    int cz = Mathf.RoundToInt(cellPath[joinIdx].y);
                    if (grid.InBoundsCell(cx, cz) && grid.GetCell(cx, cz).type == CellType.River)
                        break;
                    joinIdx--;
                }
            }

            if (config.riverSurfaceTributaryWidthFixEnabled && config.uwpOwnedVisualPolicy)
            {
                if (TryResolveTributaryMainJoinEndpointIndex(grid, config, cellPath, riverIndex, out int joinEp))
                {
                    ApplyTributaryConfluenceEndHalfWidths(
                        grid, cellPath, halfWidths, config, joinEp, fromStart: joinEp == 0);
                }
                else
                {
                    int last = n - 1;
                    if (IsTributaryEndpointNearMain(grid, config, grid.CellSizeWorld, cellPath, last))
                        ApplyTributaryConfluenceEndHalfWidths(grid, cellPath, halfWidths, config, last, fromStart: false);
                    if (IsTributaryEndpointNearMain(grid, config, grid.CellSizeWorld, cellPath, 0))
                        ApplyTributaryConfluenceEndHalfWidths(grid, cellPath, halfWidths, config, 0, fromStart: true);
                }
            }
            else if (config.riverSurfaceTributaryWidthFixEnabled)
            {
                int blend = Mathf.Clamp(config.riverConfluenceVisualBlendLengthCells, 4, 16);
                int blendStart = Mathf.Max(0, joinIdx - blend + 1);
                float endMul = Mathf.Clamp(config.riverConfluenceTributaryEndWidthMul, 0.45f, 0.88f);
                for (int i = blendStart; i <= joinIdx; i++)
                {
                    float t = joinIdx <= blendStart ? 1f : (i - blendStart) / (float)(joinIdx - blendStart);
                    float taper = Mathf.Lerp(1f, endMul, Mathf.SmoothStep(0f, 1f, t));
                    halfWidths[i] = Mathf.Max(0.02f, halfWidths[i] * taper);
                }

                if (!IsLakeEmissaryRiverIndex(grid, riverIndex))
                {
                    int pinStart = Mathf.Max(blendStart, joinIdx - 3);
                    float pinEnd = 0.42f;
                    float pinStartMul = 0.72f;
                    for (int i = pinStart; i <= joinIdx; i++)
                    {
                        float t = joinIdx <= pinStart ? 1f : (i - pinStart) / (float)(joinIdx - pinStart);
                        halfWidths[i] *= Mathf.Lerp(pinStartMul, pinEnd, Mathf.SmoothStep(0f, 1f, t));
                        halfWidths[i] = Mathf.Max(0.02f, halfWidths[i]);
                    }
                }
            }
            else
            {
                int blend = Mathf.Clamp(config.riverConfluenceVisualBlendLengthCells, 4, 16);
                int blendStart = Mathf.Max(0, joinIdx - blend + 1);
                float endMul = Mathf.Clamp(config.riverConfluenceTributaryEndWidthMul, 0.2f, 1f);
                for (int i = blendStart; i <= joinIdx; i++)
                {
                    float t = joinIdx <= blendStart ? 1f : (i - blendStart) / (float)(joinIdx - blendStart);
                    float taper = Mathf.Lerp(endMul, 1f, t);
                    halfWidths[i] = Mathf.Max(0.02f, halfWidths[i] * taper);
                }
            }

            float len = PolylineLengthCellSpace(cellPath);
            float maxHw = 0f;
            for (int i = 0; i < n; i++)
                maxHw = Mathf.Max(maxHw, halfWidths[i]);
            if (len > 1f && maxHw * 2f > len * 0.55f &&
                !config.uwpOwnedVisualPolicy &&
                (config.debugLogs || config.debugHydrologyNetwork))
            {
                Debug.Log(
                    $"[RiverTributaryVisualWarning] riverId={riverIndex} lenCells={len:F1} maxHalfWidth={maxHw:F3} action=shrink15pct");
                for (int i = 0; i < n; i++)
                    halfWidths[i] *= 0.85f;
            }

            if (config.debugLogs || config.debugHydrologyNetwork)
                LogRiverConfluenceVisualAudit(config, grid, riverIndex, 0, cellPath, joinIdx, false, false);
        }

        /// <summary>UWP: ancho constante en boca lago (igual que confluencia con troncal).</summary>
        static void ApplyTributaryLakeMouthVisualHalfWidths(
            GridSystem grid,
            List<Vector2> cellPath,
            List<float> halfWidths,
            MapGenConfig config,
            int riverIndex)
        {
            if (config == null || !config.uwpOwnedVisualPolicy || riverIndex <= 0 ||
                cellPath == null || halfWidths == null || grid == null)
                return;
            int n = Mathf.Min(cellPath.Count, halfWidths.Count);
            if (n < 4)
                return;

            float cellSize = grid.CellSizeWorld;

            void PinLakeEnd(bool atEnd)
            {
                int endIdx = atEnd ? n - 1 : 0;
                if (IsTributaryEndpointNearMain(grid, config, cellSize, cellPath, endIdx))
                    return;

                bool lakeEnd = IsCellSpacePointInOrNearLake(grid, cellPath[endIdx], 8);
                if (!lakeEnd)
                {
                    int probe = atEnd ? n - 2 : 1;
                    if (probe < 0 || probe >= n)
                        return;
                    lakeEnd = IsCellSpacePointInOrNearLake(grid, cellPath[probe], 6);
                    if (!lakeEnd)
                        return;
                }

                int blend = Mathf.Clamp(config.riverSurfaceTributaryConfluenceApproachCells, 6, 18);
                int blendStart = atEnd ? Mathf.Max(0, n - blend) : 0;
                int blendEnd = atEnd ? n - 1 : Mathf.Min(n - 1, blend - 1);
                int bodyRefIdx = atEnd ? Mathf.Max(0, blendStart - 1) : Mathf.Min(n - 1, blendEnd + 1);
                float bodyHalf = halfWidths[Mathf.Clamp(bodyRefIdx, 0, n - 1)];
                float tribHalf = config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                    ? config.riverVisualRibbonFullWidthCellsTributary * 0.5f * cellSize
                    : bodyHalf;
                float meshMul = Mathf.Clamp(config.riverSurfaceTributaryMeshOnlyWidthMul, 1f, 2f);
                float lakeFirstMul = config.uwpLakeFirstHydrologyPipeline ? 1.24f : 1.05f;
                float lakeHalf = Mathf.Max(bodyHalf, tribHalf * lakeFirstMul);
                lakeHalf = Mathf.Min(lakeHalf, bodyHalf * meshMul * (config.uwpLakeFirstHydrologyPipeline ? 1.38f : 1.16f));

                for (int i = blendStart; i <= blendEnd; i++)
                {
                    float t = blendEnd <= blendStart
                        ? 1f
                        : (atEnd
                            ? (i - blendStart) / (float)(blendEnd - blendStart)
                            : (blendEnd - i) / (float)(blendEnd - blendStart));
                    float target = Mathf.Lerp(bodyHalf, lakeHalf, Mathf.SmoothStep(0f, 1f, t));
                    halfWidths[i] = Mathf.Max(halfWidths[i], target);
                }

                int pin = Mathf.Clamp(WebFusionLakeMouthSinkVertexCount + 4, 6, n / 2);
                for (int k = 0; k < pin; k++)
                {
                    int i = atEnd ? n - 1 - k : k;
                    halfWidths[i] = Mathf.Max(halfWidths[i], lakeHalf);
                }
            }

            PinLakeEnd(true);
            PinLakeEnd(false);
        }

        /// <summary>Solo mesh: ensancha boca lago sin inflar máscara/carve.</summary>
        static void ApplyTributaryLakeMouthExtraMeshWidth(
            GridSystem grid,
            List<Vector2> cellPath,
            List<float> halfWidths,
            MapGenConfig config,
            int riverIndex)
        {
            if (config == null || !config.uwpOwnedVisualPolicy || riverIndex <= 0 ||
                cellPath == null || halfWidths == null || grid == null)
                return;
            int n = Mathf.Min(cellPath.Count, halfWidths.Count);
            if (n < 4)
                return;

            float cellSize = grid.CellSizeWorld;
            float meshMul = Mathf.Clamp(config.riverSurfaceTributaryMeshOnlyWidthMul, 1f, 2f);

            void BoostLakeEnd(bool atEnd)
            {
                int endIdx = atEnd ? n - 1 : 0;
                if (IsTributaryEndpointNearMain(grid, config, cellSize, cellPath, endIdx))
                    return;
                if (!IsCellSpacePointInOrNearLake(grid, cellPath[endIdx], 8))
                {
                    int probe = atEnd ? n - 2 : 1;
                    if (probe < 0 || probe >= n || !IsCellSpacePointInOrNearLake(grid, cellPath[probe], 6))
                        return;
                }

                int blend = Mathf.Clamp(config.lakeRiverMouthBlendCells + 4, 6, 14);
                int blendStart = atEnd ? Mathf.Max(0, n - blend) : 0;
                int blendEnd = atEnd ? n - 1 : Mathf.Min(n - 1, blend - 1);
                int bodyRefIdx = atEnd ? Mathf.Max(0, blendStart - 1) : Mathf.Min(n - 1, blendEnd + 1);
                float bodyHalf = halfWidths[Mathf.Clamp(bodyRefIdx, 0, n - 1)];
                float lakeMeshHalf = bodyHalf * Mathf.Max(1.08f, meshMul * 0.92f);

                for (int i = blendStart; i <= blendEnd; i++)
                {
                    float t = blendEnd <= blendStart
                        ? 1f
                        : (atEnd
                            ? (i - blendStart) / (float)(blendEnd - blendStart)
                            : (blendEnd - i) / (float)(blendEnd - blendStart));
                    float target = Mathf.Lerp(bodyHalf, lakeMeshHalf, Mathf.SmoothStep(0f, 1f, t));
                    halfWidths[i] = Mathf.Max(halfWidths[i], target);
                }
            }

            BoostLakeEnd(true);
            BoostLakeEnd(false);
        }

        /// <summary>UWP Play: reutiliza centerline/ancho ya resueltos (confluencia + boca lago) para la malla MS.</summary>
        static bool TryUseUwpCachedTributaryVisualForMesh(
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            Vector3 origin,
            float cellSize,
            float riverSurfaceWorldY,
            out List<Vector2> cellProcessed,
            out List<float> halfWidths,
            out List<Vector3> worldCenters)
        {
            cellProcessed = null;
            halfWidths = null;
            worldCenters = null;
            if (config == null || grid == null ||
                !config.uwpOwnedVisualPolicy || !config.riverVisualSurfaceCacheEnabled)
                return false;
            if (!grid.RiverVisualSurfaceCacheFrozen ||
                !grid.RiverVisualSurfacesBuilt || grid.RiverVisualSurfaces == null ||
                riverIndex < 0 || riverIndex >= grid.RiverVisualSurfaces.Count)
                return false;

            var surface = grid.RiverVisualSurfaces[riverIndex];
            if (surface.Skipped ||
                surface.FinalCenterlineCells == null || surface.FinalCenterlineCells.Count < 2 ||
                surface.HalfWidthsWorld == null ||
                surface.HalfWidthsWorld.Count != surface.FinalCenterlineCells.Count)
                return false;
            if (riverIndex > 0 && IsUwpDegenerateTributary(surface.FinalCenterlineCells, cellSize, grid, riverIndex))
            {
                if (IsLakeFirstHeadwaterFeeder(grid, riverIndex))
                {
                    float hwLen = ComputePolylineLengthCells(surface.FinalCenterlineCells) * Mathf.Max(0.01f, cellSize);
                    Debug.LogWarning(
                        $"[LakeFirstHeadwaterMeshSkip] riverIndex={riverIndex} finalPts={surface.FinalCenterlineCells?.Count ?? 0} " +
                        $"lengthM={hwLen:F2} reason=degenerate_headwater seed={config?.seed}");
                }
                return false;
            }

            cellProcessed = new List<Vector2>(surface.FinalCenterlineCells);
            halfWidths = new List<float>(surface.HalfWidthsWorld);
            worldCenters = WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config)
                ? CellPolylineToWorldRiverSurface(grid, config, cellProcessed, origin, cellSize, riverSurfaceWorldY, riverIndex)
                : CellPolylineToWorldXZ(cellProcessed, origin, cellSize, riverSurfaceWorldY);
            if (worldCenters.Count < 2 || worldCenters.Count != cellProcessed.Count)
                return false;

            if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
            {
                if (riverIndex > 0 && config.uwpOwnedVisualPolicy)
                    ApplyUwpTributaryTerrainCarveSnapY(
                        worldCenters, cellProcessed, grid, config, riverIndex, cellSize);
                ApplyWebFusionMonotonicSurfaceY(worldCenters, cellSize);
                if (riverIndex > 0 && config.uwpOwnedVisualPolicy)
                    ApplyUwpTributaryChannelUniformSurfaceY(
                        worldCenters, cellProcessed, grid, config, riverIndex, cellSize);
                if (riverIndex > 0)
                {
                    ApplyTributaryEndpointSubmergeDepth(
                        worldCenters, cellProcessed, grid, config, riverIndex, cellSize, riverSurfaceWorldY);
                    ApplyWebFusionTributaryLakeMouthYFadeAfterMonotonic(
                        worldCenters, cellProcessed, grid, config, riverIndex, cellSize);
                    if (config.uwpLakeFirstHydrologyPipeline &&
                        UsesSupplementalFeederSourceEmergence(grid, riverIndex))
                    {
                        ApplyLakeFirstInlandFeederSourceEmergenceY(
                            worldCenters, cellProcessed, grid, config, riverIndex, cellSize, riverSurfaceWorldY);
                        LogLakeFirstSupplementalMeshHook(grid, config, riverIndex, "TryUseUwpCached");
                    }
                }
                else
                {
                    s_webFusionMainWorldCenters.Clear();
                    s_webFusionMainWorldCenters.AddRange(worldCenters);
                }
            }

            if (riverIndex > 0 && config.uwpLakeFirstHydrologyPipeline &&
                UsesSupplementalFeederSourceEmergence(grid, riverIndex) &&
                !WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
            {
                ApplyLakeFirstInlandFeederSourceEmergenceY(
                    worldCenters, cellProcessed, grid, config, riverIndex, cellSize, riverSurfaceWorldY);
            }

            LogRiverVisualCacheUse("BuildRiverSurfaces", grid, riverIndex);
            return true;
        }

        static void LogRiverConfluenceVisualAudit(
            MapGenConfig config,
            GridSystem grid,
            int riverId,
            int receiverId,
            List<Vector2> cellPath,
            int joinIdx,
            bool hasFlatCap,
            bool hasRectPatch)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            bool skipCap = riverId > 0 && config.riverSurfaceSkipTributaryConfluenceCap;
            bool usesBlend = skipCap && !hasFlatCap;
            int jx = cellPath != null && joinIdx >= 0 && joinIdx < cellPath.Count
                ? Mathf.RoundToInt(cellPath[joinIdx].x)
                : -1;
            int jz = cellPath != null && joinIdx >= 0 && joinIdx < cellPath.Count
                ? Mathf.RoundToInt(cellPath[joinIdx].y)
                : -1;
            bool endsAtConf = grid != null && cellPath != null && joinIdx == cellPath.Count - 1;
            Debug.Log(
                $"[RiverConfluenceVisualAudit] riverId={riverId} receiverId={receiverId} meshEndsAtConfluence={(endsAtConf ? 1 : 0)} " +
                $"usesConfluenceBlend={(usesBlend ? 1 : 0)} hasFlatCapAtConfluence={(hasFlatCap ? 1 : 0)} " +
                $"hasRectangularPatch={(hasRectPatch ? 1 : 0)} terrainCarveOk=-1 joinCell=({jx},{jz}) ok={(usesBlend && !hasRectPatch ? 1 : 0)}");
        }

        static void LogRiverSurfaceWidth(
            MapGenConfig config,
            int riverId,
            float baseHalfW,
            float hwMin,
            float hwMax,
            float maxDeltaStep,
            int fordDampApplied)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            ResolveRiverSurfaceWidthBands(config, baseHalfW, riverId, null, out float minH, out float normalH, out float maxH);
            float avg = (hwMin + hwMax) * 0.5f;
            LogRiverSurfaceWidthScale(config, riverId, baseHalfW, minH, normalH, maxH, hwMin, hwMax, avg, maxDeltaStep, fordDampApplied);
        }

        static void ApplyRiverBankNoise(
            List<Vector3> center,
            List<Vector3> left,
            List<Vector3> right,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSizeWorld,
            out float maxEdgeDelta)
        {
            maxEdgeDelta = 0f;
            if (config == null || center == null || left == null || right == null || cellPath == null)
                return;
            if (center.Count != left.Count || center.Count != right.Count || center.Count != cellPath.Count)
                return;

            float ampCells = Mathf.Clamp(config.riverSurfaceBankNoiseAmpCells, 0f, 0.35f);
            if (ampCells < 1e-5f)
                return;

            float ampWorld = ampCells * Mathf.Max(0.01f, cellSizeWorld);
            float lenCells = Mathf.Max(4f, config.riverSurfaceBankNoiseLengthCells);
            float phase = riverIndex * 9.17f + config.seed * 0.021f;
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            int n = center.Count;
            float acc = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                    acc += Vector2.Distance(cellPath[i], cellPath[i - 1]);
                float t01 = n > 1 ? i / (float)(n - 1) : 0f;
                float edgeFade = MeanderEdgeFade(t01, 0.12f);
                if (i == 0 || i == n - 1 || IsPointNearFord(grid, cellPath[i], fordD))
                    edgeFade *= 0.15f;

                Vector3 tan = TangentNormalize(center, i);
                Vector3 nrm = PerpendicularXZ(tan);
                float nL = (Mathf.PerlinNoise(acc / lenCells + phase, 0.11f) * 2f - 1f) * ampWorld * edgeFade;
                float nR = (Mathf.PerlinNoise(acc / lenCells + phase + 17.3f, 0.23f) * 2f - 1f) * ampWorld * edgeFade;
                left[i] += nrm * nL;
                right[i] -= nrm * nR;
                maxEdgeDelta = Mathf.Max(maxEdgeDelta, Mathf.Abs(nL));
                maxEdgeDelta = Mathf.Max(maxEdgeDelta, Mathf.Abs(nR));
            }

            if (n >= 3)
            {
                for (int pass = 0; pass < 1; pass++)
                {
                    var lTmp = new Vector3[n];
                    var rTmp = new Vector3[n];
                    lTmp[0] = left[0];
                    rTmp[0] = right[0];
                    lTmp[n - 1] = left[n - 1];
                    rTmp[n - 1] = right[n - 1];
                    for (int i = 1; i < n - 1; i++)
                    {
                        lTmp[i] = (left[i - 1] + left[i] * 2f + left[i + 1]) * 0.25f;
                        rTmp[i] = (right[i - 1] + right[i] * 2f + right[i + 1]) * 0.25f;
                    }

                    left.Clear();
                    right.Clear();
                    for (int i = 0; i < n; i++)
                    {
                        left.Add(lTmp[i]);
                        right.Add(rTmp[i]);
                    }
                }
            }
        }

        static void LogRiverSurfaceBanks(
            MapGenConfig config,
            int riverId,
            float leftNoiseAmp,
            float rightNoiseAmp,
            float maxBankStep)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverSurfaceBanks] riverId={riverId} leftNoiseAmp={leftNoiseAmp:F3} rightNoiseAmp={rightNoiseAmp:F3} " +
                $"bankSmoothPasses=1 maxBankStep={maxBankStep:F4}");
        }

        static void LogRiverSurfaceMeshBuild(
            MapGenConfig config,
            int riverId,
            int sections,
            int verts,
            int tris,
            bool visibleDebugWire)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverSurfaceMeshBuild] riverId={riverId} sections={sections} verts={verts} tris={tris} " +
                $"crossSectionVerts={CrossSectionVertexCount} usesFans=0 usesBevelCaps=0 usesWaterChunks=0 " +
                $"visibleDebugWire={(visibleDebugWire ? 1 : 0)}");
        }

        static float ComputeEndpointTaperMul(
            int index,
            int count,
            MapGenConfig config,
            bool startAtBorder,
            bool endAtBorder,
            bool skipEndBlend,
            bool skipAllEndpointTaper = false)
        {
            if (skipAllEndpointTaper || config == null || count < 2)
                return 1f;
            int taperCells = Mathf.Clamp(config.riverSurfaceInteriorEndpointTaperCells, 3, 12);
            float minMul = Mathf.Clamp(config.riverSurfaceInteriorEndpointMinWidthMul, 1f, 1.25f);
            float mul = 1f;
            if (!startAtBorder && !skipEndBlend && index < taperCells)
            {
                float t = index / (float)taperCells;
                mul = Mathf.Min(mul, Mathf.SmoothStep(minMul, 1f, t));
            }

            if (!endAtBorder && !skipEndBlend && index >= count - taperCells)
            {
                float t = (count - 1 - index) / (float)taperCells;
                mul = Mathf.Min(mul, Mathf.SmoothStep(minMul, 1f, t));
            }

            return mul;
        }

        static void AddCrossSectionQuad(List<int> tris, int a0, int a1, int b0, int b1)
        {
            AddTriStripWinding(tris, a0, b0, a1);
            AddTriStripWinding(tris, a1, b0, b1);
        }

        static void BuildCrossSectionRiverMesh(
            List<Vector3> center,
            List<float> halfWidthWorld,
            List<Vector2> cellSpaceLine,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSizeWorld,
            float uvScale,
            float baseHalfWidthWorld,
            bool startAtBorder,
            bool endAtBorder,
            bool skipEndBlend,
            bool skipAllEndpointTaper,
            out List<Vector3> verts,
            out List<Vector2> uvs,
            out List<Vector2> uvs2,
            out List<Vector3> normals,
            out List<Vector4> tangents,
            out List<int> tris,
            out float maxSegBuilt,
            out float maxBankStep)
        {
            verts = new List<Vector3>();
            uvs = new List<Vector2>();
            uvs2 = new List<Vector2>();
            normals = new List<Vector3>();
            tangents = new List<Vector4>();
            tris = new List<int>();
            maxSegBuilt = 0f;
            maxBankStep = 0f;
            int n = center.Count;
            if (n < 2 || halfWidthWorld.Count != n)
                return;

            float innerFrac = config != null
                ? Mathf.Clamp(config.riverSurfaceInnerBankWidthFrac, 0.4f, 0.75f)
                : 0.58f;
            float bankAmpWorld = config != null
                ? Mathf.Clamp(config.riverSurfaceBankNoiseAmpCells, 0f, 0.35f) * Mathf.Max(0.01f, cellSizeWorld)
                : 0f;
            if (riverIndex == 0)
                bankAmpWorld *= 0.22f;
            float bankLen = config != null ? Mathf.Max(4f, config.riverSurfaceBankNoiseLengthCells) : 22f;
            if (riverIndex == 0)
                bankLen *= 2.4f;
            float phase = riverIndex * 9.17f + (config != null ? config.seed * 0.021f : 0f);
            int fordD = config != null ? Mathf.Max(1, config.riverVisualFordKeepDistanceCells) : 5;
            float[] accV = BuildAccumulatedStableFlowV(center);
            bool lakeEmissary = IsLakeEmissaryRiverIndex(grid, riverIndex);
            bool lakeAtStart = riverIndex > 0 &&
                cellSpaceLine != null &&
                cellSpaceLine.Count > 1 &&
                IsCellSpacePointInOrNearLake(grid, cellSpaceLine[0], 8);
            bool lakeAtEnd = riverIndex > 0 &&
                cellSpaceLine != null &&
                cellSpaceLine.Count > 1 &&
                IsCellSpacePointInOrNearLake(grid, cellSpaceLine[cellSpaceLine.Count - 1], 8);
            bool lakeConnector = riverIndex > 0 && (lakeEmissary || lakeAtStart || lakeAtEnd);
            if (lakeConnector && !lakeAtStart && !lakeAtEnd)
                lakeAtStart = true;
            bool sourceAtStart = IsRiverFlowSourceAtPolylineStart(
                grid, riverIndex, cellSpaceLine, lakeEmissary, lakeAtStart, lakeAtEnd);
            bool invertFlowAlongForShader = sourceAtStart && UsesRiverRibbonInvertedFlowUv(config, riverIndex);
            bool lakeFadeAtEnd = cellSpaceLine != null && cellSpaceLine.Count > 0 &&
                (IsCellSpacePointInOrNearLake(grid, cellSpaceLine[cellSpaceLine.Count - 1], 5) ||
                 IsCellSpacePointWater(grid, cellSpaceLine[cellSpaceLine.Count - 1]) ||
                 IsCellSpacePointInLakeBody(grid, cellSpaceLine[cellSpaceLine.Count - 1]));
            bool lakeFadeAtStart = cellSpaceLine != null && cellSpaceLine.Count > 0 &&
                (IsCellSpacePointInOrNearLake(grid, cellSpaceLine[0], 5) ||
                 IsCellSpacePointWater(grid, cellSpaceLine[0]) ||
                 IsCellSpacePointInLakeBody(grid, cellSpaceLine[0]));
            bool mainAtEnd = riverIndex > 0 &&
                cellSpaceLine != null &&
                cellSpaceLine.Count > 0 &&
                IsCellSpacePointNearMainRiverCorridor(grid, config, cellSizeWorld, cellSpaceLine[cellSpaceLine.Count - 1], 1.35f);
            bool mainAtStart = false;
            if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config) && riverIndex > 0)
            {
                ResolveWebFusionTributaryEndpointFadeFlags(
                    grid, config, cellSpaceLine, riverIndex, cellSizeWorld,
                    out lakeFadeAtEnd, out lakeFadeAtStart, out mainAtEnd, out mainAtStart);
            }

            bool inlandSourceFade = ShouldApplyLakeFirstInlandSourceFade(
                grid, config, cellSpaceLine, riverIndex, cellSizeWorld, out bool inlandFadeFromStart);

            bool endpointFade = (lakeConnector &&
                config != null &&
                config.riverLakeEmissaryEndpointFadeEnabled) ||
                lakeFadeAtEnd ||
                (riverIndex > 0 && lakeFadeAtStart) ||
                mainAtEnd ||
                mainAtStart ||
                inlandSourceFade;
            if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverMouthFusion(config) && riverIndex > 0)
                endpointFade = false;
            float totalFlowLen = accV != null && accV.Length > 0 ? Mathf.Max(accV[accV.Length - 1], 0.0001f) : 0.0001f;
            float lakeFadeWorld = Mathf.Max(0f, config != null ? config.riverLakeEmissaryLakeFadeCells * cellSizeWorld : 0f);
            float riverFadeWorld = Mathf.Max(0f, config != null ? config.riverLakeEmissaryRiverFadeCells * cellSizeWorld : 0f);
            float lakeEndpointMinAlpha = Mathf.Clamp01(config != null ? config.riverLakeEmissaryLakeEndpointMinAlpha : 0.08f);
            float riverEndpointMinAlpha = Mathf.Clamp01(config != null ? config.riverLakeEmissaryRiverEndpointMinAlpha : 0.42f);
            if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config) &&
                (lakeFadeAtEnd || lakeFadeAtStart || mainAtEnd || mainAtStart))
            {
                lakeFadeWorld = Mathf.Max(lakeFadeWorld, cellSizeWorld * 3.5f);
                riverFadeWorld = Mathf.Max(riverFadeWorld, cellSizeWorld * 3.5f);
                lakeEndpointMinAlpha = Mathf.Max(lakeEndpointMinAlpha, 0.06f);
                riverEndpointMinAlpha = Mathf.Max(riverEndpointMinAlpha, 0.18f);
            }
            float maxLeftNoise = 0f;
            float maxRightNoise = 0f;

            for (int i = 0; i < n; i++)
            {
                Vector3 c = center[i];
                Vector3 tan = TangentNormalize(center, i);
                Vector3 nrm = PerpendicularXZ(tan);
                float hw = Mathf.Max(0.02f, halfWidthWorld[i]) * ComputeEndpointTaperMul(
                    i, n, config, startAtBorder, endAtBorder, skipEndBlend, skipAllEndpointTaper);

                float leftMul = 1f;
                float rightMul = 1f;
                if (i > 0 && i < n - 1)
                {
                    Vector3 tin = center[i] - center[i - 1];
                    Vector3 tout = center[i + 1] - center[i];
                    tin.y = tout.y = 0f;
                    float cross = tin.x * tout.z - tin.z * tout.x;
                    if (Mathf.Abs(cross) > 1e-6f)
                    {
                        float bendMul = riverIndex == 0 ? 0.018f : 0.06f;
                        if (cross > 0f)
                        {
                            leftMul = 1f + bendMul;
                            rightMul = 1f - bendMul;
                        }
                        else
                        {
                            leftMul = 1f - bendMul;
                            rightMul = 1f + bendMul;
                        }
                    }
                }

                float edgeFade = MeanderEdgeFade(n > 1 ? i / (float)(n - 1) : 0f, 0.12f);
                if (i == 0 || i == n - 1 || (cellSpaceLine != null && IsPointNearFord(grid, cellSpaceLine[i], fordD)))
                    edgeFade *= 0.12f;

                float acc = i > 0 ? accV[i] : 0f;
                float nL = (Mathf.PerlinNoise(acc / bankLen + phase, 0.11f) * 2f - 1f) * bankAmpWorld * edgeFade;
                float nR = (Mathf.PerlinNoise(acc / bankLen + phase + 17.3f, 0.23f) * 2f - 1f) * bankAmpWorld * edgeFade;
                maxLeftNoise = Mathf.Max(maxLeftNoise, Mathf.Abs(nL));
                maxRightNoise = Mathf.Max(maxRightNoise, Mathf.Abs(nR));
                maxBankStep = Mathf.Max(maxBankStep, Mathf.Max(Mathf.Abs(nL), Mathf.Abs(nR)));

                float hwL = hw * leftMul;
                float hwR = hw * rightMul;
                Vector3 lb = c - nrm * hwL + nrm * nL;
                Vector3 li = c - nrm * (hwL * innerFrac) + nrm * (nL * 0.55f);
                Vector3 ri = c + nrm * (hwR * innerFrac) - nrm * (nR * 0.55f);
                Vector3 rb = c + nrm * hwR - nrm * nR;

                verts.Add(lb);
                verts.Add(li);
                verts.Add(c);
                verts.Add(ri);
                verts.Add(rb);
                float v = invertFlowAlongForShader
                    ? (totalFlowLen - acc) * uvScale
                    : acc * uvScale;
                float along01 = n > 1 ? i / (float)(n - 1) : 0f;
                float endpointAlpha = 1f;
                if (endpointFade)
                {
                    if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                    {
                        int mouthBlend = riverIndex > 0
                            ? ResolveWebFusionTributaryMouthBlendVerts(n, config)
                            : Mathf.Clamp(
                                Mathf.RoundToInt(config != null ? config.lakeRiverMouthBlendCells + 3 : 6),
                                4,
                                Mathf.Max(4, n / 4));
                        endpointAlpha = ComputeWebFusionEndpointAlpha(
                            i,
                            n,
                            lakeFadeAtEnd,
                            lakeFadeAtStart,
                            mainAtEnd,
                            mainAtStart,
                            lakeEndpointMinAlpha,
                            mouthBlend,
                            config);
                        // MouthFusion: taper de ancho en orilla; alpha siempre opaco en canal visible.
                    }
                    else
                    {
                        bool fadeFromStart = lakeFadeAtStart && (!lakeFadeAtEnd || i < n * 0.5f);
                        bool fadeFromEnd = lakeFadeAtEnd && (!lakeFadeAtStart || i >= n * 0.5f);
                        if (lakeConnector && lakeAtEnd && !lakeAtStart)
                            fadeFromEnd = true;
                        if (lakeConnector && lakeAtStart && !lakeAtEnd)
                            fadeFromStart = true;

                        float distFromLake = fadeFromEnd ? totalFlowLen - acc : acc;
                        float fromLake = lakeFadeWorld > 0.0001f ? Mathf.Clamp01(distFromLake / lakeFadeWorld) : 1f;
                        float distFromRiver = fadeFromEnd ? acc : totalFlowLen - acc;
                        float fromRiver = riverFadeWorld > 0.0001f ? Mathf.Clamp01(distFromRiver / riverFadeWorld) : 1f;
                        float lakeMinA = lakeFadeAtEnd || lakeFadeAtStart
                            ? Mathf.Min(lakeEndpointMinAlpha, 0.05f)
                            : lakeEndpointMinAlpha;
                        float riverMinA = lakeFadeAtEnd || lakeFadeAtStart
                            ? Mathf.Min(riverEndpointMinAlpha, 0.06f)
                            : riverEndpointMinAlpha;
                        float lakeAlpha = Mathf.Lerp(lakeMinA, 1f, Mathf.SmoothStep(0f, 1f, fromLake));
                        float riverAlpha = Mathf.Lerp(riverMinA, 1f, Mathf.SmoothStep(0f, 1f, fromRiver));
                        endpointAlpha = Mathf.Min(lakeAlpha, riverAlpha);
                    }
                }

                if (inlandSourceFade)
                {
                    int sourceIdxFade = ResolveLakeFirstInlandFeederSourcePathIndex(
                        grid, config, cellSpaceLine, riverIndex);
                    int mouthBlend = Mathf.Clamp(
                        ResolveLakeFirstInlandFeederSourceBlendCount(cellSpaceLine, sourceIdxFade, cellSizeWorld),
                        3,
                        Mathf.Max(3, n - 1));
                    if (inlandFadeFromStart && i < mouthBlend)
                    {
                        endpointAlpha = Mathf.Min(
                            endpointAlpha,
                            ComputeLakeFirstInlandSourceEndpointAlpha(i, n, mouthBlend, 0.02f));
                    }
                    else if (!inlandFadeFromStart && i >= n - mouthBlend)
                    {
                        int k = n - 1 - i;
                        endpointAlpha = Mathf.Min(
                            endpointAlpha,
                            ComputeLakeFirstInlandSourceEndpointAlpha(k, n, mouthBlend, 0.04f));
                    }
                }

                if (!WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config) &&
                    mainAtEnd && i >= n - Mathf.Clamp(Mathf.CeilToInt(n * 0.3f), 3, 10))
                {
                    int tail = Mathf.Clamp(Mathf.CeilToInt(n * 0.3f), 3, 10);
                    float t = (n - 1 - i) / (float)Mathf.Max(1, tail - 1);
                    float mainA = Mathf.Lerp(Mathf.Max(riverEndpointMinAlpha, 0.12f), 1f, Mathf.SmoothStep(0f, 1f, t));
                    endpointAlpha = Mathf.Min(endpointAlpha, mainA);
                }

                if (cellSpaceLine != null &&
                    i < cellSpaceLine.Count &&
                    !WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                    endpointAlpha *= SampleSplitLakeWaterSurfaceAlphaMul(grid, cellSpaceLine[i], config);
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(0.25f, v));
                uvs.Add(new Vector2(0.5f, v));
                uvs.Add(new Vector2(0.75f, v));
                uvs.Add(new Vector2(1f, v));
                uvs2.Add(new Vector2(endpointAlpha, along01));
                uvs2.Add(new Vector2(endpointAlpha, along01));
                uvs2.Add(new Vector2(endpointAlpha, along01));
                uvs2.Add(new Vector2(endpointAlpha, along01));
                uvs2.Add(new Vector2(endpointAlpha, along01));
                for (int k = 0; k < CrossSectionVertexCount; k++)
                {
                    normals.Add(Vector3.up);
                    tangents.Add(new Vector4(nrm.x, 0f, nrm.z, -1f));
                }

                if (i < n - 1)
                {
                    Vector3 d = center[i + 1] - center[i];
                    d.y = 0f;
                    maxSegBuilt = Mathf.Max(maxSegBuilt, d.magnitude);
                }
            }

            for (int i = 0; i < n - 1; i++)
            {
                int rowA = i * CrossSectionVertexCount;
                int rowB = (i + 1) * CrossSectionVertexCount;
                for (int q = 0; q < CrossSectionVertexCount - 1; q++)
                {
                    AddCrossSectionQuad(tris, rowA + q, rowA + q + 1, rowB + q, rowB + q + 1);
                }
            }

            if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
            {
                LogRiverSurfaceBanks(
                    config,
                    riverIndex,
                    maxLeftNoise,
                    maxRightNoise,
                    maxBankStep);
            }
        }

        /// <summary>Ensanche muy leve en boca (solo ancho, sin geometría extra).</summary>
        static void ApplyLakeMouthVisualHalfWidthFlare(
            GridSystem grid,
            List<Vector2> cellPath,
            List<float> halfWidths,
            MapGenConfig config)
        {
            if (grid == null || cellPath == null || halfWidths == null || config == null)
                return;
            int n = Mathf.Min(cellPath.Count, halfWidths.Count);
            if (n < 2)
                return;

            int blend = Mathf.Clamp(config.lakeRiverMouthBlendCells + 1, 2, 6);
            const float maxMul = 1.10f;

            void FlareEnd(bool atStart)
            {
                int endIdx = atStart ? 0 : n - 1;
                if (!IsCellSpacePointInOrNearLake(grid, cellPath[endIdx], 5))
                    return;
                for (int k = 0; k < blend && k < n; k++)
                {
                    int i = atStart ? k : (n - 1 - k);
                    float t = 1f - k / Mathf.Max(1f, blend - 1f);
                    float mul = Mathf.Lerp(maxMul, 1f, Mathf.SmoothStep(0f, 1f, t));
                    halfWidths[i] *= mul;
                }
            }

            FlareEnd(true);
            FlareEnd(false);
        }

        static void TrimRiverSurfaceEndAtLakeMouth(
            GridSystem grid,
            List<Vector2> cellProcessed,
            MapGenConfig config,
            int riverIndex = -1)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 4)
                return;
            int last = cellProcessed.Count - 1;
            if (!IsCellSpacePointInLakeBody(grid, cellProcessed[last]) &&
                !IsCellSpacePointWater(grid, cellProcessed[last]))
                return;

            int maxTrim = Mathf.Min(cellProcessed.Count - 2, Mathf.Max(5, config != null ? config.lakeRiverMouthBlendCells + 4 : 7));
            int trim = 0;
            for (int i = 0; i < maxTrim; i++)
            {
                int idx = last - i;
                if (IsCellSpacePointInLakeBody(grid, cellProcessed[idx]) ||
                    IsCellSpacePointWater(grid, cellProcessed[idx]))
                {
                    trim = i + 1;
                    continue;
                }

                if (trim > 0)
                    break;
            }

            if (trim <= 0 || trim >= cellProcessed.Count - 1)
                return;

            bool preserveOwnedLakeOverlap =
                config != null &&
                config.uwpOwnedVisualPolicy &&
                riverIndex > 0 &&
                IsTributaryLakeOwner(grid, riverIndex);
            int remove = preserveOwnedLakeOverlap ? Mathf.Max(0, trim - 1) : trim;
            if (remove <= 0 || remove >= cellProcessed.Count - 1)
                return;

            cellProcessed.RemoveRange(cellProcessed.Count - remove, remove);
            if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
                Debug.Log(
                    $"[LakeMouthEndTrim] trimmedEndPoints={trim} removed={remove} " +
                    $"preserveLakeOverlap={(preserveOwnedLakeOverlap ? 1 : 0)} remaining={cellProcessed.Count}");
        }

        static void TrimRiverSurfaceStartAtLakeMouth(GridSystem grid, List<Vector2> cellProcessed, MapGenConfig config)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 4)
                return;
            if (!IsCellSpacePointInOrNearLake(grid, cellProcessed[0], 3))
                return;

            int maxTrim = Mathf.Min(cellProcessed.Count - 2, Mathf.Max(4, config != null ? config.lakeRiverMouthBlendCells + 4 : 8));
            int trim = 0;
            for (int i = 0; i < maxTrim; i++)
            {
                if (IsCellSpacePointInOrNearLake(grid, cellProcessed[i], 2) ||
                    IsCellSpacePointWater(grid, cellProcessed[i]))
                {
                    trim = i + 1;
                    continue;
                }

                if (trim > 0)
                {
                    trim = Mathf.Min(maxTrim, i + 1);
                    break;
                }
            }

            if (trim <= 0 || trim >= cellProcessed.Count - 1)
                return;

            cellProcessed.RemoveRange(0, trim);
            if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
                Debug.Log($"[LakeTributaryMouthTrim] trimmedStartPoints={trim} remaining={cellProcessed.Count}");
        }

        /// <summary>
        /// Tributarios planificados antes del flood del lago pueden atravesar el cuerpo MS:
        /// conserva el tramo exterior que desemboca en el río principal (no el que cruza el lago).
        /// </summary>
        static void TrimRiverSurfaceExcludingLakeInterior(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config)
        {
            if (grid?.LakeBodyCellsPacked == null || grid.LakeBodyCellsPacked.Count == 0 ||
                cellProcessed == null || cellProcessed.Count < 3)
                return;

            int n = cellProcessed.Count;
            var outside = new bool[n];
            int insideCount = 0;
            for (int i = 0; i < n; i++)
            {
                int x = Mathf.FloorToInt(cellProcessed[i].x);
                int z = Mathf.FloorToInt(cellProcessed[i].y);
                outside[i] = !grid.LakeBodyCellsPacked.Contains(PackLakeCellLong(x, z));
                if (!outside[i])
                    insideCount++;
            }

            if (insideCount == 0)
                return;

            bool lakeEmissary = IsLakeEmissaryRiverIndex(grid, riverIndex);
            bool startNearLake = IsCellSpacePointInOrNearLake(grid, cellProcessed[0], 8);
            bool endNearLake = IsCellSpacePointInOrNearLake(grid, cellProcessed[cellProcessed.Count - 1], 8);
            bool preferLast = riverIndex > 0 && !lakeEmissary && !startNearLake;
            if (lakeEmissary || startNearLake)
                preferLast = false;
            else if (endNearLake)
                preferLast = true;

            int bestStart = -1;
            int bestLen = 0;
            int bestScore = int.MinValue;
            for (int i = 0; i < n;)
            {
                if (!outside[i])
                {
                    i++;
                    continue;
                }

                int j = i;
                while (j < n && outside[j])
                    j++;
                int len = j - i;
                if (len >= 2)
                {
                    int score = len;
                    if (preferLast && j == n)
                        score += 10000;
                    if (!preferLast && i == 0)
                        score += 10000;

                    if (riverIndex > 0 && TributaryTargetsMainConfluence(grid, riverIndex) &&
                        TryBuildMainRiverCorridorSampler(grid, config, grid.CellSizeWorld, out MainRiverCorridorSampler mainSampler))
                    {
                        float minMainDist = float.MaxValue;
                        for (int k = i; k < j; k++)
                        {
                            float d = Mathf.Sqrt(DistanceSqToPolylineCellSpace(cellProcessed[k], mainSampler.Line));
                            if (d < minMainDist)
                                minMainDist = d;
                        }

                        score += Mathf.RoundToInt(Mathf.Max(0f, 14f - minMainDist) * 800f);
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestStart = i;
                        bestLen = len;
                    }
                }

                i = j;
            }

            if (bestStart < 0 || bestLen < 2 || (bestStart == 0 && bestLen == n))
                return;

            var trimmed = cellProcessed.GetRange(bestStart, bestLen);
            cellProcessed.Clear();
            cellProcessed.AddRange(trimmed);

            if (riverIndex > 0 && (TributaryTargetsMainConfluence(grid, riverIndex) ||
                                   IsLakeEmissaryRiverIndex(grid, riverIndex)))
                TrimTributaryAtClosestMainApproach(grid, config, cellProcessed, riverIndex);

            if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
            {
                Debug.Log(
                    $"[RiverLakeInteriorTrim] riverIndex={riverIndex} insidePoints={insideCount} " +
                    $"keptSpan={bestStart}..{bestStart + bestLen - 1} remaining={cellProcessed.Count}");
            }
        }

        static bool HasInteriorLakeBodyPoints(GridSystem grid, List<Vector2> cellProcessed)
        {
            if (grid?.LakeBodyCellsPacked == null || cellProcessed == null || cellProcessed.Count < 4)
                return false;

            for (int i = 1; i < cellProcessed.Count - 1; i++)
            {
                int x = Mathf.FloorToInt(cellProcessed[i].x);
                int z = Mathf.FloorToInt(cellProcessed[i].y);
                if (grid.LakeBodyCellsPacked.Contains(PackLakeCellLong(x, z)))
                    return true;
            }

            return false;
        }

        static bool TributaryCenterlineTouchesLake(GridSystem grid, List<Vector2> cellProcessed)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2)
                return false;
            if (IsCellSpacePointInOrNearLake(grid, cellProcessed[0], 8) ||
                IsCellSpacePointInOrNearLake(grid, cellProcessed[cellProcessed.Count - 1], 8))
                return true;
            return HasInteriorLakeBodyPoints(grid, cellProcessed);
        }

        /// <summary>
        /// Corta el tributario en la primera entrada al corredor del main (lado lago).
        /// Evita el V “pasa de largo y se devuelve” que deja el min-dist en la punta.
        /// </summary>
        static void TrimTributaryAtClosestMainApproach(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> cellProcessed,
            int riverIndex)
        {
            if (grid == null || config == null || cellProcessed == null || cellProcessed.Count < 3 || riverIndex <= 0)
                return;
            if (!TributaryTargetsMainConfluence(grid, riverIndex) &&
                !IsLakeEmissaryRiverIndex(grid, riverIndex) &&
                !UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex))
                return;

            if (!TryResolveTributaryMainJoinEndpointIndex(grid, config, cellProcessed, riverIndex, out int mainEp))
                mainEp = ResolveVisualTributaryMainEndpointIndex(grid, cellProcessed);
            int lakeEp = mainEp == 0 ? cellProcessed.Count - 1 : 0;
            // Lago en el extremo lago es normal (lake-spill). Solo abortar si AMBOS extremos
            // están en lago (gancho lago→main→lago) o hay tramo interior de lago largo.
            bool lakeSpill = UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex);
            bool lakeAtLakeEp = IsTributaryEndpointNearLake(grid, cellProcessed, lakeEp);
            bool lakeAtMainEp = IsTributaryEndpointNearLake(grid, cellProcessed, mainEp);
            if (!lakeSpill && lakeAtLakeEp && lakeAtMainEp)
                return;
            if (!lakeSpill && CountWebFusionLakeMouthInteriorVertices(grid, cellProcessed) > 6)
                return;

            // Lake-first / spill→main: first-entry (evita V). Resto: same helper (más sano que min-dist).
            TrimRiverAtFirstMainCorridorContact(grid, config, cellProcessed, riverIndex, forceSpillJoin: lakeSpill);
        }

        /// <summary>Recorte lago + cola hacia troncal para tributarios dendríticos con agua en el path.</summary>
        static bool ApplyLakeAwareTributaryCenterlineTrim(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> cellProcessed,
            int riverIndex,
            bool logRm)
        {
            if (grid == null || config == null || cellProcessed == null || cellProcessed.Count < 2)
                return false;
            if (!TributaryCenterlineTouchesLake(grid, cellProcessed))
                return false;

            int before = cellProcessed.Count;
            TrimRiverSurfaceStartAtLakeMouth(grid, cellProcessed, config);
            TrimRiverSurfaceExcludingLakeInterior(grid, cellProcessed, riverIndex, config);
            TrimTributaryAtClosestMainApproach(grid, config, cellProcessed, riverIndex);

            if (cellProcessed.Count < 2)
            {
                if (logRm)
                {
                    Debug.Log(
                        $"[RiverTributaryVisualSkip] riverIndex={riverIndex} reason=lake_trim_empty before={before}");
                }

                return true;
            }

            if (logRm && cellProcessed.Count != before)
            {
                Debug.Log(
                    $"[RiverTributaryLakeTrim] riverIndex={riverIndex} before={before} after={cellProcessed.Count}");
            }

            return false;
        }

        static bool TryGetLakeCentroidNearShore(GridSystem grid, Vector2 shore, out Vector2 centroid)
        {
            centroid = default;
            if (grid?.LakeBodyCellsPacked == null || grid.LakeBodyCellsPacked.Count == 0)
                return false;

            int sx = Mathf.RoundToInt(shore.x);
            int sz = Mathf.RoundToInt(shore.y);
            int radius = 64;
            double sumX = 0d;
            double sumY = 0d;
            int count = 0;
            foreach (long pk in grid.LakeBodyCellsPacked)
            {
                int x = (int)(pk >> 32);
                int z = (int)(uint)pk;
                if (Mathf.Max(Mathf.Abs(x - sx), Mathf.Abs(z - sz)) > radius)
                    continue;
                sumX += x + 0.5d;
                sumY += z + 0.5d;
                count++;
            }

            if (count <= 0)
                return false;

            centroid = new Vector2((float)(sumX / count), (float)(sumY / count));
            return true;
        }

        static bool TryEstimateLakeRadiusCells(GridSystem grid, Vector2 shore, out float radiusCells)
        {
            radiusCells = 0f;
            if (!TryGetLakeCentroidNearShore(grid, shore, out Vector2 centroid))
                return false;

            int sx = Mathf.RoundToInt(shore.x);
            int sz = Mathf.RoundToInt(shore.y);
            foreach (long pk in grid.LakeBodyCellsPacked)
            {
                int x = (int)(pk >> 32);
                int z = (int)(uint)pk;
                if (Mathf.Max(Mathf.Abs(x - sx), Mathf.Abs(z - sz)) > 96)
                    continue;

                float d = Vector2.Distance(centroid, new Vector2(x + 0.5f, z + 0.5f));
                if (d > radiusCells)
                    radiusCells = d;
            }

            return radiusCells > 0.5f;
        }

        /// <summary>Estilo Pruebas AppendLakeMouth: mete el extremo bajo el agua del lago (overlap hacia el centro).</summary>
        static void AppendLakeInteriorOverlapPoints(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config,
            bool extendStart)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null)
                return;

            int endpoint = extendStart ? 0 : cellProcessed.Count - 1;
            Vector2 shore = cellProcessed[endpoint];
            if (!TryGetLakeCentroidNearShore(grid, shore, out Vector2 centroid))
                return;

            Vector2 toCenter = centroid - shore;
            if (toCenter.sqrMagnitude < 0.04f)
                return;

            float halfCells = riverIndex == 0
                ? config.riverVisualRibbonFullWidthCellsMain * 0.5f
                : (config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                    ? config.riverVisualRibbonFullWidthCellsTributary * 0.5f
                    : config.riverVisualRibbonFullWidthCellsMain * 0.5f);
            float overlap;
            if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
            {
                float fullWidth = halfCells * 2f;
                float lakeRadius = TryEstimateLakeRadiusCells(grid, shore, out float estRadius)
                    ? estRadius
                    : toCenter.magnitude;
                overlap = Mathf.Clamp(fullWidth * 1.8f, 6f, lakeRadius * 0.45f);
            }
            else
            {
                overlap = Mathf.Clamp(Mathf.Max(halfCells * (riverIndex > 0 ? 1.55f : 2.1f), 1.4f), 1.2f, riverIndex > 0 ? 5.5f : 9f);
            }
            overlap = Mathf.Min(overlap, toCenter.magnitude - 0.35f);
            if (overlap < 0.25f)
                return;

            Vector2 dir = toCenter.normalized;
            if (extendStart && cellProcessed.Count >= 2)
            {
                Vector2 approach = cellProcessed[1] - cellProcessed[0];
                if (approach.sqrMagnitude > 0.04f)
                {
                    approach.Normalize();
                    if (Vector2.Dot(approach, dir) < 0.35f)
                        dir = approach;
                }
            }
            else if (!extendStart && cellProcessed.Count >= 2)
            {
                int prevIdx = cellProcessed.Count - 2;
                Vector2 approach = cellProcessed[cellProcessed.Count - 1] - cellProcessed[prevIdx];
                if (approach.sqrMagnitude > 0.04f)
                {
                    approach.Normalize();
                    float dot = Vector2.Dot(approach, dir);
                    if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                        dir = (approach * 0.72f + dir * 0.28f).normalized;
                    else if (dot < 0.35f)
                        dir = approach;
                }
            }

            Vector2 mouth = shore + dir * overlap;
            if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
            {
                if (extendStart)
                    cellProcessed.Insert(0, mouth);
                else
                    cellProcessed.Add(mouth);
            }
            else
                cellProcessed[endpoint] = mouth;

            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[RiverLakeInteriorOverlap] riverIndex={riverIndex} extendStart={(extendStart ? 1 : 0)} " +
                    $"overlapCells={overlap:F2} mouth=({mouth.x:F1},{mouth.y:F1}) remaining={cellProcessed.Count}");
            }
        }

        static void ApplyLakeEndpointVisualTuck(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config,
            bool extendStart)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null)
                return;

            int endpoint = extendStart ? 0 : cellProcessed.Count - 1;
            if (riverIndex > 0 &&
                !IsTributaryAuthorizedForLakeEndpoint(grid, riverIndex, cellProcessed[endpoint], config))
                return;

            Vector2 p = cellProcessed[endpoint];
            bool inLake = IsCellSpacePointInLakeBody(grid, p) || IsCellSpacePointWater(grid, p);
            if (!inLake)
            {
                int before = cellProcessed.Count;
                AppendCenterlineTowardLakeShore(grid, cellProcessed, riverIndex, config, extendStart);
                if (riverIndex > 0 && cellProcessed.Count == before &&
                    !WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config) &&
                    TryAppendOwnedTributaryClosestLakeBridge(grid, cellProcessed, riverIndex, config))
                {
                    extendStart = false;
                }
            }

            AppendLakeInteriorOverlapPoints(grid, cellProcessed, riverIndex, config, extendStart);
        }

        static int FindLakeComponentOwnedByRiver(GridSystem grid, int riverIndex)
        {
            if (grid?.LakeComponentTributaryOwnerRiverIndex == null || riverIndex <= 0)
                return -1;
            for (int i = 0; i < grid.LakeComponentTributaryOwnerRiverIndex.Count; i++)
            {
                if (grid.LakeComponentTributaryOwnerRiverIndex[i] == riverIndex)
                    return i;
            }

            return -1;
        }

        static bool TryAppendOwnedTributaryClosestLakeBridge(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null)
                return false;
            int comp = FindLakeComponentOwnedByRiver(grid, riverIndex);
            if (comp < 0 || grid.LakeBodyComponents == null || comp >= grid.LakeBodyComponents.Count)
                return false;

            if (!TryResolveTributaryMainJoinEndpointIndex(grid, config, cellProcessed, riverIndex, out int mainIdx))
                mainIdx = ResolveVisualTributaryMainEndpointIndex(grid, cellProcessed);

            float maxDist = ResolveLakeMouthApproachMaxDistCells(config);
            int bestIdx = -1;
            float bestDist = maxDist + 1f;
            Vector2 bestShore = default;
            int last = cellProcessed.Count - 1;
            for (int ti = 0; ti < 2; ti++)
            {
                int i = ti == 0 ? 0 : last;
                if (i == mainIdx)
                    continue;
                if (!TryGetTributaryOwnedLakeShorePoint(grid, riverIndex, cellProcessed[i], maxDist, out Vector2 shore))
                    continue;
                float d = Vector2.Distance(cellProcessed[i], shore);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = i;
                    bestShore = shore;
                }
            }

            if (bestIdx < 0 || bestIdx == mainIdx)
                return false;

            var sub = ExtractVisualSubpathFromMainToIndex(cellProcessed, mainIdx, bestIdx);
            if (sub == null || sub.Count < 2)
                return false;

            Vector2 approach = sub[sub.Count - 1];
            float bridgeDist = Vector2.Distance(approach, bestShore);
            if (bridgeDist > maxDist)
                return false;

            if (bridgeDist > 0.2f)
            {
                int steps = Mathf.Clamp(Mathf.CeilToInt(bridgeDist / 0.75f), 2, 14);
                for (int s = 1; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    sub.Add(Vector2.Lerp(approach, bestShore, t));
                }
            }
            else
            {
                sub[sub.Count - 1] = bestShore;
            }

            cellProcessed.Clear();
            cellProcessed.AddRange(sub);
            return true;
        }

        static void AppendCenterlineBridgeTowardShore(List<Vector2> cellProcessed, int endpointIndex, Vector2 shore)
        {
            if (cellProcessed == null || cellProcessed.Count < 2 || endpointIndex < 0 || endpointIndex >= cellProcessed.Count)
                return;

            Vector2 approach = cellProcessed[endpointIndex];
            float bridgeDist = Vector2.Distance(approach, shore);
            if (bridgeDist < 0.2f)
            {
                cellProcessed[endpointIndex] = shore;
                return;
            }

            int steps = Mathf.Clamp(Mathf.CeilToInt(bridgeDist / 0.75f), 2, 14);
            if (endpointIndex == cellProcessed.Count - 1)
            {
                for (int s = 1; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    cellProcessed.Add(Vector2.Lerp(approach, shore, t));
                }
            }
            else
            {
                var bridge = new List<Vector2>(steps);
                for (int s = steps; s >= 1; s--)
                {
                    float t = s / (float)steps;
                    bridge.Add(Vector2.Lerp(approach, shore, t));
                }

                cellProcessed.InsertRange(0, bridge);
            }
        }

        /// <summary>Pass final UWP: un tributario solo conecta a su lago owned; WebFusion + puente si falta carve.</summary>
        static void EnsureTributaryOwnedLakeMouthPipeline(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null || riverIndex <= 0)
                return;
            if (!config.uwpOwnedVisualPolicy || !IsTributaryLakeOwner(grid, riverIndex))
                return;

            if (!TryResolveTributaryMainJoinEndpointIndex(grid, config, cellProcessed, riverIndex, out int mainEp))
                mainEp = ResolveVisualTributaryMainEndpointIndex(grid, cellProcessed);
            int lakeEp = mainEp == 0 ? cellProcessed.Count - 1 : 0;
            float maxDist = ResolveLakeMouthApproachMaxDistCells(config);

            bool lakeReady = IsCellSpacePointInOrNearOwnedLake(grid, riverIndex, cellProcessed[lakeEp], 4) ||
                             CountWebFusionLakeMouthInteriorVertices(grid, cellProcessed) > 0;
            if (!lakeReady)
            {
                if (TryAppendOwnedTributaryClosestLakeBridge(grid, cellProcessed, riverIndex, config))
                    lakeReady = true;
                else if (TryGetTributaryOwnedLakeShorePoint(
                             grid, riverIndex, cellProcessed[lakeEp], maxDist, out Vector2 ownedShore))
                {
                    AppendCenterlineBridgeTowardShore(cellProcessed, lakeEp, ownedShore);
                    lakeReady = true;
                }
            }

            if (!lakeReady)
                return;

            if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                ApplyWebFusionTributaryLakeMouthFinalize(grid, cellProcessed, riverIndex, config);

            if (config.debugLogs || config.debugHydrologyNetwork || config.uwpOwnedVisualPolicy)
            {
                float shoreDist = float.MaxValue;
                if (TryGetTributaryOwnedLakeShorePoint(grid, riverIndex, cellProcessed[lakeEp], maxDist, out Vector2 shore))
                    shoreDist = Vector2.Distance(cellProcessed[lakeEp], shore);
                Debug.Log(
                    $"[TributaryOwnedLakeMouthFinal] riverIndex={riverIndex} lakeEp={lakeEp} " +
                    $"points={cellProcessed.Count} shoreDist={shoreDist:F2} lakeReady=1");
            }
        }

        /// <summary>Post-WebFusion: segmento main↔lago sin re-entradas al troncal ni ganchos.</summary>
        static void FinalizeTributaryMainLakePath(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 4 || riverIndex <= 0 || config == null)
                return;
            if (!config.uwpOwnedVisualPolicy)
                return;

            int before = cellProcessed.Count;
            PruneTributaryCenterlineInteriorMainCrossings(grid, cellProcessed, riverIndex, config);
            if (TributaryTargetsMainConfluence(grid, riverIndex) || IsTributaryLakeOwner(grid, riverIndex))
                TrimTributaryAtClosestMainApproach(grid, config, cellProcessed, riverIndex);

            if (cellProcessed.Count != before && (config.debugLogs || config.debugHydrologyNetwork || config.uwpOwnedVisualPolicy))
            {
                Debug.Log(
                    $"[TributaryMainLakeFinalize] riverIndex={riverIndex} beforePts={before} " +
                    $"afterPts={cellProcessed.Count} tribToMain={(TributaryTargetsMainConfluence(grid, riverIndex) ? 1 : 0)}");
            }
        }

        /// <summary>Elimina tramos que re-entran al troncal entre boca lago y confluencia (evita cruce doble).</summary>
        static void PruneTributaryCenterlineInteriorMainCrossings(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 4 || riverIndex <= 0 || config == null)
                return;
            if (!config.uwpOwnedVisualPolicy)
                return;
            if (!TryBuildMainRiverCorridorSampler(grid, config, grid.CellSizeWorld, out MainRiverCorridorSampler sampler))
                return;
            if (!TryResolveTributaryMainJoinEndpointIndex(grid, config, cellProcessed, riverIndex, out int mainEp))
                mainEp = ResolveVisualTributaryMainEndpointIndex(grid, cellProcessed);

            int lakeEp = mainEp == 0 ? cellProcessed.Count - 1 : 0;
            var segment = ExtractVisualSubpathFromMainToIndex(cellProcessed, mainEp, lakeEp);
            if (segment == null || segment.Count < 3)
                return;

            float corridorSq = sampler.RadiusSq * 1.12f;
            int guardMain = Mathf.Clamp(Mathf.CeilToInt(sampler.RadiusCells * 0.55f), 3, 8);
            int guardLake = guardMain;
            var cleaned = new List<Vector2>(segment.Count) { segment[0] };
            for (int i = 1; i < segment.Count - 1; i++)
            {
                float distSq = DistanceSqToPolylineCellSpace(segment[i], sampler.Line);
                bool inside = distSq <= corridorSq;
                bool nearMainEnd = i <= guardMain;
                bool nearLakeEnd = i >= segment.Count - 1 - guardLake;
                if (inside && !nearMainEnd && !nearLakeEnd)
                    continue;

                cleaned.Add(segment[i]);
            }

            cleaned.Add(segment[segment.Count - 1]);
            if (cleaned.Count < 2)
                return;

            cellProcessed.Clear();
            cellProcessed.AddRange(cleaned);
        }

        static void SyncMainRiverMaskWidthsToMesh(
            List<float> maskHalfWidths,
            List<float> meshHalfWidths)
        {
            if (maskHalfWidths == null || meshHalfWidths == null)
                return;
            int n = Mathf.Min(maskHalfWidths.Count, meshHalfWidths.Count);
            for (int i = 0; i < n; i++)
                maskHalfWidths[i] = meshHalfWidths[i];
        }

        static void SyncTributaryMeshWidthsToMask(
            List<float> meshHalfWidths,
            List<float> maskHalfWidths,
            int riverIndex)
        {
            if (riverIndex <= 0 || meshHalfWidths == null || maskHalfWidths == null)
                return;
            int n = Mathf.Min(meshHalfWidths.Count, maskHalfWidths.Count);
            for (int i = 0; i < n; i++)
            {
                float maskW = maskHalfWidths[i];
                if (maskW > 1e-4f)
                    meshHalfWidths[i] = Mathf.Max(meshHalfWidths[i], maskW);
            }
        }

        static int ResolveVisualTributaryMainEndpointIndex(GridSystem grid, List<Vector2> cellProcessed)
        {
            if (cellProcessed == null || cellProcessed.Count < 2)
                return 0;
            var mainLine = grid?.RiverCenterlinesCellSpace != null && grid.RiverCenterlinesCellSpace.Count > 0
                ? grid.RiverCenterlinesCellSpace[0]
                : null;
            if (mainLine == null || mainLine.Count < 2)
                return cellProcessed.Count - 1;

            float dStart = Mathf.Sqrt(DistanceSqToPolylineCellSpace(cellProcessed[0], mainLine));
            float dEnd = Mathf.Sqrt(DistanceSqToPolylineCellSpace(cellProcessed[cellProcessed.Count - 1], mainLine));
            return dStart + 0.01f <= dEnd ? 0 : cellProcessed.Count - 1;
        }

        static List<Vector2> ExtractVisualSubpathFromMainToIndex(List<Vector2> line, int mainIdx, int targetIdx)
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

        /// <summary>Mete la boca del tributario ligeramente bajo el eje del troncal (reduce franja superpuesta).</summary>
        static void TuckTributaryMouthIntoMainRiver(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config,
            int endpointIndex = -1)
        {
            if (grid == null || config == null || cellProcessed == null || cellProcessed.Count < 2 || riverIndex <= 0)
                return;
            if (endpointIndex < 0 &&
                !TryResolveTributaryMainJoinEndpointIndex(grid, config, cellProcessed, riverIndex, out endpointIndex))
            {
                endpointIndex = cellProcessed.Count - 1;
            }

            if (endpointIndex < 0 || endpointIndex >= cellProcessed.Count)
                return;
            if (IsTributaryEndpointNearLake(grid, cellProcessed, endpointIndex))
                return;

            if (!TryBuildMainRiverCorridorSampler(grid, config, grid.CellSizeWorld, out MainRiverCorridorSampler sampler))
                return;

            Vector2 end = cellProcessed[endpointIndex];
            float bestDist = float.MaxValue;
            int bestSeg = 0;
            Vector2 bestJoin = end;
            for (int i = 0; i < sampler.Line.Count - 1; i++)
            {
                Vector2 q = ClosestPointOnOpenSegment2D(end, sampler.Line[i], sampler.Line[i + 1]);
                float d = (end - q).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestJoin = q;
                    bestSeg = i;
                }
            }

            float maxJoin = ResolveConfluenceReachMaxGapCells(config);
            if (bestDist > maxJoin * maxJoin)
                return;

            if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
            {
                cellProcessed[endpointIndex] = bestJoin;
                if (config.debugLogs || config.debugHydrologyNetwork)
                {
                    Debug.Log(
                        $"[RiverConfluenceTuck] riverIndex={riverIndex} endpoint={endpointIndex} mode=webFusion_snap " +
                        $"join=({bestJoin.x:F1},{bestJoin.y:F1}) remaining={cellProcessed.Count}");
                }

                return;
            }

            int neighbor = endpointIndex == 0 ? 1 : endpointIndex - 1;
            Vector2 approach = Vector2.zero;
            if (cellProcessed.Count >= 2 && neighbor >= 0 && neighbor < cellProcessed.Count)
                approach = (end - cellProcessed[neighbor]);

            // Lake-spill→main: entrar por approach (no tangente del main → evita gancho en V).
            bool lakeSpillApproach =
                config.uwpLakeFirstHydrologyPipeline &&
                UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex);

            if (lakeSpillApproach)
            {
                if (approach.sqrMagnitude < 1e-8f)
                    approach = bestJoin - end;
                if (approach.sqrMagnitude < 1e-8f)
                    return;
                approach.Normalize();

                float tuckLf = Mathf.Clamp(sampler.CoreRadiusCells * 0.38f, 0.45f, 1.35f);
                Vector2 tuckedLf = bestJoin + approach * tuckLf;
                if (cellProcessed.Count >= 2 && neighbor >= 0 && neighbor < cellProcessed.Count &&
                    WouldFoldBackBridge(cellProcessed[neighbor], end, tuckedLf))
                    tuckedLf = bestJoin;

                cellProcessed[endpointIndex] = tuckedLf;
                if (config.debugLogs || config.debugHydrologyNetwork)
                {
                    Debug.Log(
                        $"[RiverConfluenceTuck] riverIndex={riverIndex} endpoint={endpointIndex} tuckCells={tuckLf:F2} " +
                        $"join=({bestJoin.x:F1},{bestJoin.y:F1}) remaining={cellProcessed.Count} mode=lakeSpill_approach");
                }

                return;
            }

            Vector2 tangent = sampler.Line[bestSeg + 1] - sampler.Line[bestSeg];
            if (tangent.sqrMagnitude < 1e-8f)
                return;
            tangent.Normalize();

            if (cellProcessed.Count >= 2 && neighbor >= 0 && neighbor < cellProcessed.Count)
            {
                Vector2 a = approach.sqrMagnitude > 1e-8f ? approach.normalized : Vector2.zero;
                if (a.sqrMagnitude > 1e-8f && Vector2.Dot(a, tangent) < 0f)
                    tangent = -tangent;
            }

            float tuck = Mathf.Clamp(sampler.CoreRadiusCells * 0.42f, 0.65f, 2.2f);
            Vector2 tucked = bestJoin + tangent * tuck;
            if (cellProcessed.Count >= 2 && neighbor >= 0 && neighbor < cellProcessed.Count &&
                WouldFoldBackBridge(cellProcessed[neighbor], end, tucked))
                return;

            cellProcessed[endpointIndex] = tucked;

            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[RiverConfluenceTuck] riverIndex={riverIndex} endpoint={endpointIndex} tuckCells={tuck:F2} " +
                    $"join=({bestJoin.x:F1},{bestJoin.y:F1}) remaining={cellProcessed.Count}");
            }
        }

        static float ResolveConfluenceReachMaxGapCells(MapGenConfig config)
        {
            if (config == null)
                return 28f;
            float minGap = config.uwpOwnedVisualPolicy ? 22f : 16f;
            float maxGap = config.uwpOwnedVisualPolicy ? 48f : 40f;
            return Mathf.Clamp(
                Mathf.Max(config.lakeRiverConnectorMaxDistanceCells * 0.55f, config.riverConfluenceMergeRadiusCells + 14f),
                minGap,
                maxGap);
        }

        static bool IsCellSpacePointNearMainRiverCorridor(
            GridSystem grid,
            MapGenConfig config,
            float cellSize,
            Vector2 p,
            float radiusMul = 1.35f)
        {
            if (!TryBuildMainRiverCorridorSampler(grid, config, cellSize, out MainRiverCorridorSampler sampler))
                return false;
            float r = sampler.RadiusCells * radiusMul;
            return DistanceSqToPolylineCellSpace(p, sampler.Line) <= r * r;
        }

        static float TributaryEndGapToMainRiverCells(
            GridSystem grid,
            List<Vector2> cellProcessed,
            MapGenConfig config,
            float cellSize)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2)
                return float.MaxValue;
            if (!TryBuildMainRiverCorridorSampler(grid, config, cellSize, out MainRiverCorridorSampler sampler))
                return float.MaxValue;
            Vector2 end = cellProcessed[cellProcessed.Count - 1];
            return Mathf.Sqrt(DistanceSqToPolylineCellSpace(end, sampler.Line));
        }

        static bool IsStandardDendriticTributary(GridSystem grid, int riverIndex)
        {
            return grid != null && riverIndex > 0 && !IsLakeEmissaryRiverIndex(grid, riverIndex);
        }

        static bool TributaryTargetsMainConfluence(GridSystem grid, int riverIndex)
        {
            if (!IsStandardDendriticTributary(grid, riverIndex))
                return false;
            if (TryGetTributaryConfluenceCell(grid, riverIndex, out _, out _))
                return true;
            if (grid.RiverReceiverIds != null && riverIndex < grid.RiverReceiverIds.Count &&
                grid.RiverReceiverIds[riverIndex] == 0)
                return true;
            return TributaryEndNearMainRiverCells(grid, riverIndex, null, 12f);
        }

        static bool TributaryEndNearMainRiverCells(
            GridSystem grid,
            int riverIndex,
            List<Vector2> cellProcessed,
            float maxDistCells)
        {
            if (grid?.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return false;
            var mainLine = grid.RiverCenterlinesCellSpace[0];
            if (mainLine == null || mainLine.Count < 2)
                return false;

            Vector2 end;
            if (cellProcessed != null && cellProcessed.Count >= 2)
                end = cellProcessed[cellProcessed.Count - 1];
            else if (riverIndex >= 0 && riverIndex < grid.RiverCenterlinesCellSpace.Count)
            {
                var raw = grid.RiverCenterlinesCellSpace[riverIndex];
                if (raw == null || raw.Count < 2)
                    return false;
                end = raw[raw.Count - 1];
            }
            else
                return false;

            float maxSq = maxDistCells * maxDistCells;
            return DistanceSqToPolylineCellSpace(end, mainLine) <= maxSq;
        }

        static bool TryResolveTributaryJoinOnMainRiver(
            GridSystem grid,
            int riverIndex,
            List<Vector2> cellProcessed,
            MapGenConfig config,
            out Vector2 join)
        {
            join = default;
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null)
                return false;

            if (TryGetTributaryConfluenceCell(grid, riverIndex, out join, out _))
                return true;

            var mainLine = grid.RiverCenterlinesCellSpace != null && grid.RiverCenterlinesCellSpace.Count > 0
                ? grid.RiverCenterlinesCellSpace[0]
                : null;
            if (mainLine == null || mainLine.Count < 2)
                return false;

            Vector2 refPt = cellProcessed[cellProcessed.Count - 1];
            if (grid.RiverCenterlinesCellSpace != null &&
                riverIndex >= 0 && riverIndex < grid.RiverCenterlinesCellSpace.Count)
            {
                var raw = grid.RiverCenterlinesCellSpace[riverIndex];
                if (raw != null && raw.Count >= 2)
                    refPt = raw[raw.Count - 1];
            }

            int bestSeg = 0;
            float bestSegD = float.MaxValue;
            for (int i = 0; i < mainLine.Count - 1; i++)
            {
                float d = DistancePointToOpenSegment2D(refPt, mainLine[i], mainLine[i + 1]);
                if (d < bestSegD)
                {
                    bestSegD = d;
                    bestSeg = i;
                }
            }

            float maxGap = ResolveConfluenceReachMaxGapCells(config);
            if (bestSegD > maxGap)
                return false;

            int win = 5;
            int seg0 = Mathf.Max(0, bestSeg - win);
            int seg1 = Mathf.Min(mainLine.Count - 2, bestSeg + win);
            float bestSq = float.MaxValue;
            join = mainLine[bestSeg];
            for (int i = seg0; i <= seg1; i++)
            {
                Vector2 q = ClosestPointOnOpenSegment2D(refPt, mainLine[i], mainLine[i + 1]);
                float sq = (refPt - q).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    join = q;
                }
            }

            return true;
        }

        static bool WouldFoldBackBridge(Vector2 prev, Vector2 end, Vector2 target)
        {
            Vector2 approach = end - prev;
            Vector2 toTarget = target - end;
            if (approach.sqrMagnitude < 1e-6f || toTarget.sqrMagnitude < 1e-6f)
                return false;
            return Vector2.Dot(approach.normalized, toTarget.normalized) < -0.15f;
        }

        /// <summary>
        /// Split mode: acerca el extremo del tributario al eje del troncal antes del recorte de solape.
        /// </summary>
        static void ExtendTributaryCenterlineToMainRiverConfluence(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config,
            float cellSize)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || riverIndex <= 0 || config == null)
                return;
            if (IsLakeEmissaryRiverIndex(grid, riverIndex))
                return;
            if (!config.riverConfluenceEnabled)
                return;
            if (!TryGetTributaryConfluenceCell(grid, riverIndex, out Vector2 target, out _))
                return;

            Vector2 end = cellProcessed[cellProcessed.Count - 1];
            float gap = Vector2.Distance(end, target);
            if (gap < 0.12f)
            {
                cellProcessed[cellProcessed.Count - 1] = target;
                return;
            }

            float maxGap = ResolveConfluenceReachMaxGapCells(config);
            if (gap > maxGap)
                return;

            if (cellProcessed.Count >= 2 &&
                WouldFoldBackBridge(cellProcessed[cellProcessed.Count - 2], end, target))
                return;

            float spacing = config.riverSurfaceVisualSpacingCells > 0.01f
                ? Mathf.Clamp(config.riverSurfaceVisualSpacingCells, 0.55f, 1f)
                : 0.75f;
            int steps = Mathf.Clamp(Mathf.CeilToInt(gap / spacing), 1, 12);
            int inserted = 0;
            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                Vector2 p = Vector2.Lerp(end, target, t);
                Vector2 last = cellProcessed[cellProcessed.Count - 1];
                if ((p - last).sqrMagnitude >= spacing * spacing * 0.16f)
                {
                    cellProcessed.Add(p);
                    inserted++;
                }
            }

            cellProcessed[cellProcessed.Count - 1] = target;

            if (inserted > 0 && (config.debugLogs || config.debugHydrologyNetwork))
            {
                Debug.Log(
                    $"[RiverConfluenceExtend] riverIndex={riverIndex} gapCells={gap:F2} inserted={inserted} " +
                    $"remaining={cellProcessed.Count} target=({target.x:F1},{target.y:F1})");
            }
        }

        static bool IsCellSpacePointInLakeBody(GridSystem grid, Vector2 p)
        {
            if (grid?.LakeBodyCellsPacked == null || grid.LakeBodyCellsPacked.Count == 0)
                return false;
            int x = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, grid.Width - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, grid.Height - 1);
            return grid.LakeBodyCellsPacked.Contains(PackLakeCellLong(x, z));
        }

        /// <summary>
        /// Split/WebFusion: recorta solo el interior profundo del lago en emisarios; deja 3 nodos de boca.
        /// </summary>
        static void ApplySplitEmissaryLakeOriginTrim(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 3 ||
                config == null || !IsLakeEmissaryRiverIndex(grid, riverIndex))
                return;

            const int maxKeepAtLakeOrigin = 3;
            bool ShouldTrim(Vector2 p) =>
                IsCellSpacePointWater(grid, p) || IsCellSpacePointInLakeBody(grid, p);

            int inside = 0;
            while (inside < cellProcessed.Count && ShouldTrim(cellProcessed[inside]))
                inside++;

            if (inside <= maxKeepAtLakeOrigin)
                return;

            int remove = inside - maxKeepAtLakeOrigin;
            if (remove <= 0 || remove >= cellProcessed.Count - 1)
                return;

            cellProcessed.RemoveRange(0, remove);

            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[EmissaryLakeOriginTrim] riverIndex={riverIndex} insideWas={inside} " +
                    $"removed={remove} keptAtOrigin={maxKeepAtLakeOrigin} remaining={cellProcessed.Count}");
            }
        }

        /// <summary>
        /// Dendrítico → lago: retira 1 nodo si la boca invade ligeramente el MS del lago.
        /// </summary>
        static void ApplySplitDendriticLakeMouthNudgeBack(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 4 ||
                config == null || riverIndex <= 0 ||
                !IsStandardDendriticTributary(grid, riverIndex))
                return;
            if (config.uwpOwnedVisualPolicy)
                return;

            int last = cellProcessed.Count - 1;
            if (!IsCellSpacePointInOrNearLake(grid, cellProcessed[last], 3))
                return;

            cellProcessed.RemoveAt(last);
        }

        static bool IsSplitLakeMsRiverWebFusionStabilizeMode(MapGenConfig config)
        {
            return config != null &&
                config.waterVisualPipeline == WaterVisualPipelineMode.SplitLakeMsRiverWebFusion;
        }

        static void ApplySplitLakeMouthStabilizationTrims(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config,
            bool standardTrib,
            bool lakeEmissary)
        {
            if (lakeEmissary)
                ApplySplitEmissaryLakeOriginTrim(grid, cellProcessed, riverIndex, config);
            if (standardTrib)
                ApplySplitDendriticLakeMouthNudgeBack(grid, cellProcessed, riverIndex, config);
        }

        /// <summary>
        /// Split mode: no dibujar centerline dentro de lago MS / celdas Water estáticas (deja solo la boca).
        /// </summary>
        static void TrimRiverSurfaceStaticWaterFromEnds(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 3)
                return;

            bool ShouldTrim(Vector2 p) =>
                IsCellSpacePointWater(grid, p) || IsCellSpacePointInLakeBody(grid, p);
            bool preserveOwnedLakeOverlap =
                config != null &&
                config.uwpOwnedVisualPolicy &&
                riverIndex > 0 &&
                IsTributaryLakeOwner(grid, riverIndex);

            int endTrim = 0;
            for (int i = cellProcessed.Count - 1; i >= 1; i--)
            {
                if (ShouldTrim(cellProcessed[i]))
                    endTrim++;
                else
                    break;
            }

            int endRemove = preserveOwnedLakeOverlap ? Mathf.Max(0, endTrim - 1) : endTrim;
            if (endRemove > 0 && endRemove < cellProcessed.Count - 1)
                cellProcessed.RemoveRange(cellProcessed.Count - endRemove, endRemove);

            int startTrim = 0;
            if (riverIndex > 0)
            {
                for (int i = 0; i < cellProcessed.Count - 1; i++)
                {
                    if (ShouldTrim(cellProcessed[i]))
                        startTrim++;
                    else
                        break;
                }

                int startRemove = preserveOwnedLakeOverlap ? Mathf.Max(0, startTrim - 1) : startTrim;
                if (startRemove > 0 && startRemove < cellProcessed.Count - 1)
                    cellProcessed.RemoveRange(0, startRemove);
            }

            if ((endTrim > 0 || startTrim > 0) && config != null &&
                (config.debugLogs || config.debugHydrologyNetwork))
            {
                Debug.Log(
                    $"[RiverStaticWaterTrim] riverIndex={riverIndex} endTrim={endTrim} startTrim={startTrim} " +
                    $"preserveLakeOverlap={(preserveOwnedLakeOverlap ? 1 : 0)} " +
                    $"remaining={cellProcessed.Count}");
            }
        }

        static float SampleSplitLakeWaterSurfaceAlphaMul(
            GridSystem grid,
            Vector2 p,
            MapGenConfig config)
        {
            if (grid == null)
                return 1f;
            bool webFusion = WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config);
            float lakeBodyMul = webFusion ? 0.38f : 0.03f;
            float nearLakeMul = webFusion ? 0.28f : 0.05f;
            if (IsCellSpacePointInLakeBody(grid, p) || IsCellSpacePointWater(grid, p))
                return lakeBodyMul;

            if (!IsCellSpacePointInOrNearLake(grid, p, 3))
                return 1f;

            int x = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, grid.Width - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, grid.Height - 1);
            float blend = Mathf.Clamp(config != null ? config.lakeRiverMouthBlendCells + 2 : 5, 3, 9);
            float near01 = 0f;
            int r = Mathf.CeilToInt(blend);
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int nx = x + dx;
                    int nz = z + dz;
                    if (!grid.InBoundsCell(nx, nz))
                        continue;
                    if (!IsCellSpacePointInLakeBody(grid, new Vector2(nx + 0.5f, nz + 0.5f)))
                        continue;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    near01 = Mathf.Max(near01, 1f - d / blend);
                }
            }

            return Mathf.Lerp(1f, nearLakeMul, Mathf.Clamp01(near01));
        }

        static void ApplyTributaryMainConfluenceCenterlineTrim(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config,
            float cellSize)
        {
            if (grid == null || cellProcessed == null || riverIndex <= 0 || config == null)
                return;
            if (!IsStandardDendriticTributary(grid, riverIndex))
                return;
            if (!config.riverConfluenceHideLastSegmentUnderMain)
                return;
            if (!TributaryTargetsMainConfluence(grid, riverIndex) &&
                !TryResolveTributaryMainJoinEndpointIndex(grid, config, cellProcessed, riverIndex, out _))
                return;

            TrimDendriticTributaryBeforeMainConfluence(grid, cellProcessed, riverIndex, config, cellSize);
        }

        static int PruneConfluenceOppositeBankMaskInWindow(
            bool[,] mask,
            int w,
            int h,
            RiverConfluenceNode node,
            Vector2 approach,
            MainRiverCorridorSampler mainSampler,
            float keepRadiusSq,
            bool pruneSkippedTributaryMouth)
        {
            int r = Mathf.Max(2, node.MergeRadiusCells + 4);
            int cx0 = node.Cell.x;
            int cz0 = node.Cell.y;
            int x0 = Mathf.Clamp(cx0 - r, 0, w - 1);
            int x1 = Mathf.Clamp(cx0 + r, 0, w - 1);
            int z0 = Mathf.Clamp(cz0 - r, 0, h - 1);
            int z1 = Mathf.Clamp(cz0 + r, 0, h - 1);
            int pruned = 0;

            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (!mask[x, z])
                        continue;

                    Vector2 p = new Vector2(x + 0.5f, z + 0.5f);
                    float distSqMain = DistanceSqToPolylineCellSpace(p, mainSampler.Line);
                    if (!ClosestPointOnPolyline2D(p, mainSampler.Line, out Vector2 closest, out _))
                        continue;

                    Vector2 off = p - closest;
                    if (off.sqrMagnitude < 1e-6f)
                        continue;
                    off.Normalize();
                    float sideDot = Vector2.Dot(off, approach);

                    if (pruneSkippedTributaryMouth)
                    {
                        // Tributario skipped: quitar boca ensanchada del troncal en el lado de aproximación.
                        if (sideDot >= -0.18f)
                            continue;
                        if (distSqMain <= keepRadiusSq)
                            continue;
                    }
                    else
                    {
                        if (distSqMain <= keepRadiusSq)
                            continue;
                        if (sideDot <= 0.18f)
                            continue;
                    }

                    mask[x, z] = false;
                    pruned++;
                }
            }

            return pruned;
        }

        /// <summary>
        /// Tras ProtectMainRiverMaskCore: recorta la boca del troncal donde un tributario skipped tenía confluencia.
        /// Usa centerline funcional solo para el vector approach; no rasteriza ni revive el afluente.
        /// </summary>
        static int PruneSkippedTributaryConfluenceMouthMask(
            bool[,] mask,
            int w,
            int h,
            GridSystem grid,
            MapGenConfig config,
            IReadOnlyList<RiverVisualSurfaceData> surfaces)
        {
            if (mask == null || grid?.RiverConfluences == null || config == null || !config.uwpOwnedVisualPolicy)
                return 0;
            if (grid.RiverConfluences.Count == 0 || surfaces == null || surfaces.Count == 0)
                return 0;
            if (!TryBuildMainRiverCorridorSampler(grid, config, grid.CellSizeWorld, out MainRiverCorridorSampler mainSampler))
                return 0;

            float mainHalf = config.riverVisualRibbonFullWidthCellsMain * 0.5f;
            float mouthTrimKeepSq = mainHalf * mainHalf * 0.82f * 0.82f;
            int pruned = 0;

            for (int ci = 0; ci < grid.RiverConfluences.Count; ci++)
            {
                RiverConfluenceNode node = grid.RiverConfluences[ci];
                if (!node.Valid)
                    continue;

                int tribIdx = node.TributaryRiverIndex;
                if (tribIdx < 0 || tribIdx >= surfaces.Count)
                    continue;
                RiverVisualSurfaceData tribSurface = surfaces[tribIdx];
                if (tribSurface == null || !tribSurface.Skipped)
                    continue;

                if (!TryGetSkippedTributaryConfluenceApproachFromFunctionalCenterline(
                        grid, tribIdx, node.Cell, out Vector2 approach))
                    continue;

                int mouthPruned = PruneConfluenceOppositeBankMaskInWindow(
                    mask, w, h, node, approach, mainSampler, mouthTrimKeepSq, pruneSkippedTributaryMouth: true);
                if (mouthPruned > 0)
                {
                    Debug.Log(
                        $"[UWP_PRUNE_SKIPPED_TRIBUTARY_CONFLUENCE_MASK] mainIndex={node.MainRiverIndex} " +
                        $"tributaryIndex={tribIdx} cell=({node.Cell.x},{node.Cell.y}) " +
                        $"prunedCells={mouthPruned} reason=skipped_tributary_confluence");
                }

                pruned += mouthPruned;
            }

            return pruned;
        }

        static int PruneConfluenceOppositeBankMask(
            bool[,] mask,
            int w,
            int h,
            GridSystem grid,
            MapGenConfig config,
            IReadOnlyList<RiverVisualSurfaceData> surfaces)
        {
            if (mask == null || grid?.RiverConfluences == null || config == null || !config.uwpOwnedVisualPolicy)
                return 0;
            if (grid.RiverConfluences.Count == 0 || surfaces == null || surfaces.Count == 0)
                return 0;

            int pruned = 0;
            float cellSize = grid.CellSizeWorld;

            for (int ci = 0; ci < grid.RiverConfluences.Count; ci++)
            {
                RiverConfluenceNode node = grid.RiverConfluences[ci];
                if (!node.Valid)
                    continue;

                int tribIdx = node.TributaryRiverIndex;
                if (tribIdx < 0 || tribIdx >= surfaces.Count)
                    continue;
                RiverVisualSurfaceData tribSurface = surfaces[tribIdx];
                if (tribSurface == null)
                    continue;

                if (tribSurface.Skipped)
                    continue;

                var tribLine = tribSurface.FinalCenterlineCells;
                if (tribLine == null || tribLine.Count < 2)
                    continue;

                int recvIdx = Mathf.Clamp(node.MainRiverIndex, 0, grid.RiverCenterlinesCellSpace != null
                    ? grid.RiverCenterlinesCellSpace.Count - 1
                    : 0);
                if (!TryBuildRiverCorridorSampler(grid, config, cellSize, recvIdx, out MainRiverCorridorSampler recvSampler))
                    continue;

                if (!TryGetTributaryMainConfluenceApproachFromFinalPath(
                        grid, config, tribLine, tribIdx, out Vector2 approach, out _))
                    continue;

                float recvHalf = ResolveRiverRibbonHalfWidthCells(config, recvIdx);
                float keepRadiusSq = recvHalf * recvHalf * 0.90f * 0.90f;
                pruned += PruneConfluenceOppositeBankMaskInWindow(
                    mask, w, h, node, approach, recvSampler, keepRadiusSq, pruneSkippedTributaryMouth: false);
            }

            return pruned;
        }

        static bool[,] CloneBoolMask(bool[,] src, int w, int h)
        {
            var dst = new bool[w, h];
            if (src == null)
                return dst;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    dst[x, z] = src[x, z];
            return dst;
        }

        /// <summary>El troncal no debe ensanchar su máscara por unión con tributarios; solo el afluente se acopla.</summary>
        static int ProtectMainRiverMaskCore(
            bool[,] combined,
            bool[,] mainOnly,
            GridSystem grid,
            MapGenConfig config)
        {
            if (combined == null || mainOnly == null || grid == null || config == null || !config.uwpOwnedVisualPolicy)
                return 0;
            if (!TryBuildMainRiverCorridorSampler(grid, config, grid.CellSizeWorld, out MainRiverCorridorSampler sampler))
                return 0;

            float coreHalf = config.riverVisualRibbonFullWidthCellsMain * 0.5f * 0.90f;
            float coreSq = coreHalf * coreHalf;
            int w = grid.Width;
            int h = grid.Height;
            int restored = 0;
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!combined[x, z] && !mainOnly[x, z])
                        continue;
                    Vector2 p = new Vector2(x + 0.5f, z + 0.5f);
                    if (DistanceSqToPolylineCellSpace(p, sampler.Line) > coreSq)
                        continue;
                    if (combined[x, z] == mainOnly[x, z])
                        continue;
                    combined[x, z] = mainOnly[x, z];
                    restored++;
                }
            }

            return restored;
        }

        static void TrimDendriticTributaryBeforeMainConfluence(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config,
            float cellSize)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 4 || riverIndex <= 0)
                return;
            if (IsLakeEmissaryRiverIndex(grid, riverIndex))
                return;
            if (config != null && !config.riverConfluenceHideLastSegmentUnderMain)
                return;
            if (!TryBuildMainRiverCorridorSampler(grid, config, cellSize, out MainRiverCorridorSampler sampler))
                return;

            int trim = 0;
            float trimRadiusSq = sampler.CoreRadiusSq * 0.50f;
            int maxTrim = Mathf.Clamp(config != null ? config.riverConfluenceMergeRadiusCells + 1 : 4, 2, 5);
            for (int i = cellProcessed.Count - 1; i >= 1 && trim < maxTrim; i--)
            {
                float distSq = DistanceSqToPolylineCellSpace(cellProcessed[i], sampler.Line);
                if (distSq <= trimRadiusSq)
                    trim++;
                else
                    break;
            }

            if (trim > 0 && cellProcessed.Count - trim >= 2)
            {
                int lastKept = cellProcessed.Count - trim - 1;
                ClosestPointOnPolyline2D(cellProcessed[lastKept], sampler.Line, out _, out float dLastKept);
                if (dLastKept > sampler.RadiusCells * 0.92f && trim > 1)
                    trim--;
            }

            if (trim <= 0 || cellProcessed.Count - trim < 2)
                return;

            cellProcessed.RemoveRange(cellProcessed.Count - trim, trim);
            if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
            {
                Debug.Log(
                    $"[RiverTributaryConfluenceTrim] riverIndex={riverIndex} trimmedEndPoints={trim} remaining={cellProcessed.Count}");
            }
        }

        static bool IsLakeEmissaryRiverIndex(GridSystem grid, int riverIndex)
        {
            if (grid == null || riverIndex <= 0)
                return false;
            if (grid.RiverWidthRatioToMain == null || riverIndex >= grid.RiverWidthRatioToMain.Count)
                return false;
            float ratio = grid.RiverWidthRatioToMain[riverIndex];
            return ratio > 0.24f && ratio < 0.32f;
        }

        /// <summary>
        /// WOR River Water desplaza espuma con uv.y - time (hacia UV menor). Si la polyline va fuente→desembocadura
        /// hay que invertir la acumulación UV para que la animación siga la hidrología sin flip de material.
        /// </summary>
        static bool UsesRiverRibbonInvertedFlowUv(MapGenConfig config, int riverIndex)
        {
            if (config == null)
                return true;
            Material m = riverIndex > 0 && config.tributaryWaterMaterial != null
                ? config.tributaryWaterMaterial
                : config.riverWaterMaterial;
            if (m == null || m.shader == null)
                return true;
            string shaderName = m.shader.name ?? string.Empty;
            if (shaderName.Contains("River Water Simple"))
                return false;
            if (shaderName.Contains("RTS River Water"))
                return false;
            return true;
        }

        /// <summary>True si el índice 0 de la centerline es aguas arriba (fuente/lago) y el último es boca/confluencia.</summary>
        static bool IsRiverFlowSourceAtPolylineStart(
            GridSystem grid,
            int riverIndex,
            List<Vector2> cellSpaceLine,
            bool lakeEmissary,
            bool lakeAtStart,
            bool lakeAtEnd)
        {
            if (cellSpaceLine == null || cellSpaceLine.Count < 2)
                return true;

            if (lakeEmissary || lakeAtStart)
                return !lakeAtEnd || lakeAtStart;
            if (lakeAtEnd && !lakeAtStart)
                return false;

            if (riverIndex > 0 && grid?.RiverConfluences != null)
            {
                for (int i = 0; i < grid.RiverConfluences.Count; i++)
                {
                    RiverConfluenceNode node = grid.RiverConfluences[i];
                    if (!node.Valid || node.TributaryRiverIndex != riverIndex)
                        continue;

                    int join = Mathf.Clamp(node.TributaryCenterlineIndex, 0, cellSpaceLine.Count - 1);
                    int last = cellSpaceLine.Count - 1;
                    if (join >= Mathf.RoundToInt(last * 0.65f))
                        return true;
                    if (join <= Mathf.Max(2, Mathf.RoundToInt(last * 0.35f)))
                        return false;
                    break;
                }
            }

            if (riverIndex == 0 && grid?.HydrologyMainRiverTerminusCell.HasValue == true)
            {
                Vector2 term = new Vector2(
                    grid.HydrologyMainRiverTerminusCell.Value.x + 0.5f,
                    grid.HydrologyMainRiverTerminusCell.Value.y + 0.5f);
                float dStart = (cellSpaceLine[0] - term).sqrMagnitude;
                float dEnd = (cellSpaceLine[cellSpaceLine.Count - 1] - term).sqrMagnitude;
                return dEnd + 0.001f <= dStart;
            }

            if (TryGetHydrologyRiverRecord(grid, riverIndex, out HydrologyRiverRecord record))
            {
                Vector2 first = cellSpaceLine[0];
                Vector2 last = cellSpaceLine[cellSpaceLine.Count - 1];
                Vector2 start = new Vector2(record.StartCell.x + 0.5f, record.StartCell.y + 0.5f);
                Vector2 end = new Vector2(record.EndCell.x + 0.5f, record.EndCell.y + 0.5f);
                float normalScore = (first - start).sqrMagnitude + (last - end).sqrMagnitude;
                float reversedScore = (first - end).sqrMagnitude + (last - start).sqrMagnitude;
                return reversedScore + 0.001f >= normalScore;
            }

            return true;
        }

        static bool TryGetHydrologyRiverRecord(GridSystem grid, int riverIndex, out HydrologyRiverRecord record)
        {
            record = null;
            var rivers = grid?.HydrologyNetwork?.Rivers;
            if (rivers == null)
                return false;
            for (int i = 0; i < rivers.Count; i++)
            {
                HydrologyRiverRecord candidate = rivers[i];
                if (candidate == null || candidate.RiverId != riverIndex)
                    continue;
                record = candidate;
                return true;
            }

            return false;
        }

        static bool IsLakeEmissaryCenterline(GridSystem grid, List<Vector2> cellProcessed, int riverIndex)
        {
            if (IsLakeEmissaryRiverIndex(grid, riverIndex))
                return true;
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2)
                return false;
            bool startNear = IsCellSpacePointInOrNearLake(grid, cellProcessed[0], 8);
            bool endNear = IsCellSpacePointInOrNearLake(grid, cellProcessed[cellProcessed.Count - 1], 8);
            return (startNear || endNear) && !HasInteriorLakeBodyPoints(grid, cellProcessed);
        }

        static bool TryGetNearestLakeShorePoint(
            GridSystem grid,
            Vector2 from,
            float maxDistCells,
            out Vector2 shore)
        {
            shore = default;
            if (grid?.LakeBodyCellsPacked == null || grid.LakeBodyCellsPacked.Count == 0)
                return false;

            float maxSq = maxDistCells * maxDistCells;
            float bestSq = float.MaxValue;
            if (grid.LakeMouthCellsPacked != null && grid.LakeMouthCellsPacked.Count > 0)
            {
                foreach (long pk in grid.LakeMouthCellsPacked)
                {
                    int x = (int)(pk >> 32);
                    int z = (int)(uint)pk;
                    var center = new Vector2(x + 0.5f, z + 0.5f);
                    float sq = (center - from).sqrMagnitude;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        shore = center;
                    }
                }
            }

            foreach (long pk in grid.LakeBodyCellsPacked)
            {
                int x = (int)(pk >> 32);
                int z = (int)(uint)pk;
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
                        var center = new Vector2(nx + 0.5f, nz + 0.5f);
                        float sq = (center - from).sqrMagnitude;
                        if (sq < bestSq)
                        {
                            bestSq = sq;
                            shore = center;
                        }
                    }
                }
            }

            return bestSq <= maxSq;
        }

        static bool IsLakeCellInComponent(GridSystem grid, int compIdx, int x, int z)
        {
            if (grid?.LakeBodyComponents == null || compIdx < 0 || compIdx >= grid.LakeBodyComponents.Count)
                return false;
            return grid.LakeBodyComponents[compIdx].Contains(PackLakeCellLong(x, z));
        }

        static bool IsCellSpacePointInOrNearOwnedLake(GridSystem grid, int riverIndex, Vector2 p, int radius)
        {
            int comp = FindLakeComponentOwnedByRiver(grid, riverIndex);
            if (comp < 0)
                return false;

            int cx = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, grid.Width - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, grid.Height - 1);
            int r = Mathf.Clamp(radius, 0, 8);
            for (int z = cz - r; z <= cz + r; z++)
            {
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if ((uint)x >= (uint)grid.Width || (uint)z >= (uint)grid.Height)
                        continue;
                    if (IsLakeCellInComponent(grid, comp, x, z))
                        return true;
                }
            }

            return false;
        }

        /// <summary>Orilla del lago asignado al tributario (un afluente → un lago).</summary>
        static bool TryGetTributaryOwnedLakeShorePoint(
            GridSystem grid,
            int riverIndex,
            Vector2 from,
            float maxDistCells,
            out Vector2 shore)
        {
            shore = default;
            int comp = FindLakeComponentOwnedByRiver(grid, riverIndex);
            if (comp < 0 || grid?.LakeBodyComponents == null || comp >= grid.LakeBodyComponents.Count)
                return false;

            float maxSq = maxDistCells * maxDistCells;
            float bestSq = float.MaxValue;
            if (grid.LakeMouthCellsPacked != null && grid.LakeMouthCellsPacked.Count > 0)
            {
                foreach (long pk in grid.LakeMouthCellsPacked)
                {
                    int x = (int)(pk >> 32);
                    int z = (int)(uint)pk;
                    if (!IsLakeCellInComponent(grid, comp, x, z))
                    {
                        bool touches = false;
                        for (int dz = -1; dz <= 1 && !touches; dz++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (IsLakeCellInComponent(grid, comp, x + dx, z + dz))
                                {
                                    touches = true;
                                    break;
                                }
                            }
                        }

                        if (!touches)
                            continue;
                    }

                    var center = new Vector2(x + 0.5f, z + 0.5f);
                    float sq = (center - from).sqrMagnitude;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        shore = center;
                    }
                }
            }

            foreach (long pk in grid.LakeBodyComponents[comp])
            {
                int x = (int)(pk >> 32);
                int z = (int)(uint)pk;
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
                        if (IsLakeCellInComponent(grid, comp, nx, nz))
                            continue;
                        var center = new Vector2(nx + 0.5f, nz + 0.5f);
                        float sq = (center - from).sqrMagnitude;
                        if (sq < bestSq)
                        {
                            bestSq = sq;
                            shore = center;
                        }
                    }
                }
            }

            return bestSq <= maxSq;
        }

        static bool TryGetLakeShorePointForTributary(
            GridSystem grid,
            int riverIndex,
            Vector2 from,
            float maxDistCells,
            out Vector2 shore)
        {
            if (riverIndex > 0 && IsTributaryLakeOwner(grid, riverIndex) &&
                TryGetTributaryOwnedLakeShorePoint(grid, riverIndex, from, maxDistCells, out shore))
                return true;

            return TryGetNearestLakeShorePoint(grid, from, maxDistCells, out shore);
        }

        static bool TryGetNearestLakeShorePoint(GridSystem grid, Vector2 from, out Vector2 shore)
        {
            return TryGetNearestLakeShorePoint(grid, from, 12f, out shore);
        }

        static float ResolveLakeMouthApproachMaxDistCells(MapGenConfig config)
        {
            if (config == null)
                return 48f;
            return Mathf.Clamp(
                Mathf.Max(config.lakeRiverConnectorMaxDistanceCells, config.lakeRiverMouthBlendCells + 12),
                18f,
                96f);
        }

        static float ResolveLakeTributaryMaxConnectDistCellsVisual(MapGenConfig config)
        {
            if (config == null)
                return 48f;
            float cfgDist = Mathf.Max(8f, config.lakeRiverConnectorMaxDistanceCells);
            return config.uwpOwnedVisualPolicy ? Mathf.Min(cfgDist, 48f) : cfgDist;
        }

        static bool IsTributaryLakeOwner(GridSystem grid, int riverIndex)
        {
            if (grid?.LakeComponentTributaryOwnerRiverIndex == null || riverIndex <= 0)
                return false;
            for (int i = 0; i < grid.LakeComponentTributaryOwnerRiverIndex.Count; i++)
            {
                if (grid.LakeComponentTributaryOwnerRiverIndex[i] == riverIndex)
                    return true;
            }

            return false;
        }

        static bool IsLakeSpillTributaryVisual(GridSystem grid, MapGenConfig config, int riverIndex) =>
            riverIndex > 0 && config != null && config.uwpLakeFirstHydrologyPipeline &&
            UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex);

        /// <summary>Lake-spill e InlandFeeder: mismos anchos/mesh en confluencia con el main.</summary>
        static bool UsesLakeFirstMainJoinMeshTreatment(GridSystem grid, MapGenConfig config, int riverIndex) =>
            UwpTributaryOriginUtility.UsesLakeFirstMainJoinMeshTreatment(grid, config, riverIndex);

        static int FindNearestLakeComponentIndexVisual(Vector2 p, GridSystem grid, float maxDistCells)
        {
            if (grid?.LakeBodyComponents == null || grid.LakeBodyComponents.Count == 0)
                return -1;
            int best = -1;
            float bestDist = maxDistCells + 1f;
            for (int ci = 0; ci < grid.LakeBodyComponents.Count; ci++)
            {
                foreach (long pk in grid.LakeBodyComponents[ci])
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

        static bool IsTributaryAuthorizedForLakeEndpoint(
            GridSystem grid,
            int riverIndex,
            Vector2 endpoint,
            MapGenConfig config)
        {
            if (grid == null || riverIndex <= 0 || config == null)
                return false;
            if (IsTributaryLakeOwner(grid, riverIndex))
                return true;
            return TryGetLakeShorePointForTributary(
                grid,
                riverIndex,
                endpoint,
                ResolveLakeMouthApproachMaxDistCells(config),
                out _);
        }

        static bool ShouldApplyTributaryLakeMouthFinalJoin(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null || riverIndex <= 0)
                return false;
            if (IsTributaryLakeOwner(grid, riverIndex))
                return true;
            if (RiverFunctionalEndNearLake(grid, riverIndex, config))
                return true;

            float maxDist = ResolveLakeMouthApproachMaxDistCells(config);
            if (TryGetLakeShorePointForTributary(grid, riverIndex, cellProcessed[cellProcessed.Count - 1], maxDist, out _))
                return true;
            if (TryGetLakeShorePointForTributary(grid, riverIndex, cellProcessed[0], maxDist, out _))
                return true;

            if (grid.RiverCenterlinesCellSpace != null &&
                riverIndex < grid.RiverCenterlinesCellSpace.Count)
            {
                var raw = grid.RiverCenterlinesCellSpace[riverIndex];
                if (raw != null && raw.Count >= 2)
                {
                    if (TryGetLakeShorePointForTributary(grid, riverIndex, raw[0], maxDist, out _) ||
                        TryGetLakeShorePointForTributary(grid, riverIndex, raw[raw.Count - 1], maxDist, out _))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Split mode (estilo Pruebas): extiende el extremo hacia orilla/boca de lago y añade punto final en la orilla.
        /// </summary>
        static void AppendCenterlineTowardLakeShore(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config,
            bool extendStart)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null)
                return;

            int endpoint = extendStart ? 0 : cellProcessed.Count - 1;
            Vector2 end = cellProcessed[endpoint];
            if (riverIndex > 0 &&
                !IsTributaryAuthorizedForLakeEndpoint(grid, riverIndex, end, config))
                return;

            if (IsCellSpacePointInLakeBody(grid, end) || IsCellSpacePointWater(grid, end))
                return;

            float maxDist = ResolveLakeMouthApproachMaxDistCells(config);
            if (!TryGetLakeShorePointForTributary(grid, riverIndex, end, maxDist, out Vector2 shore))
                return;

            float dist = Vector2.Distance(end, shore);
            if (dist < 0.2f)
            {
                cellProcessed[endpoint] = shore;
                return;
            }

            if (dist > maxDist)
                return;

            if (cellProcessed.Count >= 2)
            {
                int neighbor = extendStart ? 1 : cellProcessed.Count - 2;
                Vector2 approach = end - cellProcessed[neighbor];
                Vector2 toLake = shore - end;
                float dotMin = config.uwpOwnedVisualPolicy &&
                               (IsTributaryLakeOwner(grid, riverIndex) || RiverFunctionalEndNearLake(grid, riverIndex, config))
                    ? -0.35f
                    : 0.25f;
                if (approach.sqrMagnitude > 1e-6f && toLake.sqrMagnitude > 1e-6f &&
                    Vector2.Dot(approach.normalized, toLake.normalized) < dotMin)
                    return;
            }

            float spacing = config.riverSurfaceVisualSpacingCells > 0.01f
                ? Mathf.Clamp(config.riverSurfaceVisualSpacingCells, 0.55f, 1f)
                : 0.75f;
            int steps = Mathf.Clamp(Mathf.CeilToInt(dist / spacing), 2, Mathf.CeilToInt(maxDist / spacing) + 2);
            var bridge = new List<Vector2>(steps);
            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                bridge.Add(Vector2.Lerp(end, shore, t));
            }

            if (extendStart)
            {
                bridge.Reverse();
                cellProcessed.InsertRange(0, bridge);
            }
            else
            {
                cellProcessed.AddRange(bridge);
            }

            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[RiverLakeMouthAppend] riverIndex={riverIndex} extendStart={(extendStart ? 1 : 0)} " +
                    $"distCells={dist:F2} inserted={bridge.Count} shore=({shore.x:F1},{shore.y:F1}) remaining={cellProcessed.Count}");
            }
        }

        static bool RiverFunctionalEndNearLake(GridSystem grid, int riverIndex, MapGenConfig config)
        {
            if (grid?.RiverCenterlinesCellSpace == null ||
                riverIndex < 0 || riverIndex >= grid.RiverCenterlinesCellSpace.Count ||
                config == null)
                return false;
            var raw = grid.RiverCenterlinesCellSpace[riverIndex];
            if (raw == null || raw.Count < 2)
                return false;
            Vector2 funcEnd = raw[raw.Count - 1];
            return TryGetNearestLakeShorePoint(
                grid,
                funcEnd,
                ResolveLakeMouthApproachMaxDistCells(config),
                out _);
        }

        public static bool RiverFunctionalEndNearLakeForAudit(GridSystem grid, int riverIndex, MapGenConfig config) =>
            RiverFunctionalEndNearLake(grid, riverIndex, config);

        static void ApplySplitModeLakeMouthApproaches(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null)
                return;

            bool endNearLake = IsCellSpacePointInOrNearLake(grid, cellProcessed[cellProcessed.Count - 1], 8) ||
                (grid.LakeMouthCellsPacked != null && grid.LakeMouthCellsPacked.Count > 0 &&
                 MinChebyshevDistToLakeMouth(cellProcessed[cellProcessed.Count - 1], grid) <= ResolveLakeMouthApproachMaxDistCells(config));
            bool tribToMain = TributaryTargetsMainConfluence(grid, riverIndex);
            bool mainToLake = riverIndex == 0 &&
                grid.HydrologyMainRiverTerminusCell.HasValue &&
                (grid.HydrologyMainRiverPattern == RiverMainPattern.HighlandToLake ||
                 grid.HydrologyMainRiverPattern == RiverMainPattern.BorderToLake);

            if (!tribToMain && (mainToLake || endNearLake || RiverFunctionalEndNearLake(grid, riverIndex, config)))
                ApplyLakeEndpointVisualTuck(grid, cellProcessed, riverIndex, config, extendStart: false);

            if (riverIndex > 0)
            {
                bool startNearLake = IsCellSpacePointInOrNearLake(grid, cellProcessed[0], 8) ||
                    IsLakeEmissaryCenterline(grid, cellProcessed, riverIndex);
                if (startNearLake)
                    ApplyLakeEndpointVisualTuck(grid, cellProcessed, riverIndex, config, extendStart: true);
            }
        }

        static float MinChebyshevDistToLakeMouth(Vector2 p, GridSystem grid)
        {
            if (grid?.LakeMouthCellsPacked == null || grid.LakeMouthCellsPacked.Count == 0)
                return float.MaxValue;
            int px = Mathf.RoundToInt(p.x);
            int pz = Mathf.RoundToInt(p.y);
            float best = float.MaxValue;
            foreach (long pk in grid.LakeMouthCellsPacked)
            {
                int x = (int)(pk >> 32);
                int z = (int)(uint)pk;
                float d = Mathf.Max(Mathf.Abs(px - x), Mathf.Abs(pz - z));
                if (d < best)
                    best = d;
            }

            return best;
        }

        /// <summary>Estilo Pruebas SnapTributaryMouthToParent: mueve solo el nodo de confluencia con el troncal.</summary>
        static void SnapTributaryCenterlineToMainRiver(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config,
            int endpointIndex = -1)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || riverIndex <= 0 || config == null)
                return;
            bool lakeEmissary = IsLakeEmissaryRiverIndex(grid, riverIndex);
            if (!lakeEmissary && !config.riverConfluenceEnabled)
                return;

            if (endpointIndex < 0 &&
                !TryResolveTributaryMainJoinEndpointIndex(grid, config, cellProcessed, riverIndex, out endpointIndex))
            {
                endpointIndex = cellProcessed.Count - 1;
            }

            if (endpointIndex < 0 || endpointIndex >= cellProcessed.Count)
                return;
            if (IsTributaryEndpointNearLake(grid, cellProcessed, endpointIndex))
                return;

            if (!TryResolveTributaryJoinOnMainRiver(grid, riverIndex, cellProcessed, config, out Vector2 join))
                return;

            Vector2 end = cellProcessed[endpointIndex];
            float gap = Vector2.Distance(end, join);
            if (gap < 0.08f)
            {
                cellProcessed[endpointIndex] = join;
                return;
            }

            float maxGap = ResolveConfluenceReachMaxGapCells(config);
            if (gap > maxGap)
                return;

            int neighbor = endpointIndex == 0 ? 1 : endpointIndex - 1;
            if (cellProcessed.Count >= 2 && neighbor >= 0 && neighbor < cellProcessed.Count &&
                WouldFoldBackBridge(cellProcessed[neighbor], end, join))
                return;

            cellProcessed[endpointIndex] = join;

            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[RiverConfluenceSnap] riverIndex={riverIndex} endpoint={endpointIndex} gapCells={gap:F2} " +
                    $"join=({join.x:F1},{join.y:F1}) remaining={cellProcessed.Count} mode=mouth_only");
            }
        }

        /// <summary>Solo extremos: boca de lago + snap de confluencia. No modifica el recorrido intermedio.</summary>
        static void ApplySplitModeConfluenceAndLakeEndpoints(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config,
            float cellSize)
        {
            if (riverIndex > 0)
            {
                int mainIdx = cellProcessed.Count - 1;
                bool joinMain = TributaryTargetsMainConfluence(grid, riverIndex) ||
                    TryResolveTributaryMainJoinEndpointIndex(grid, config, cellProcessed, riverIndex, out mainIdx);
                if (joinMain)
                {
                    bool lakeSpillMain =
                        config.uwpLakeFirstHydrologyPipeline &&
                        UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex);

                    // Spill→main: no Snap al eje + tangente (recrea el V). Solo tuck corto por approach.
                    if (!lakeSpillMain)
                        SnapTributaryCenterlineToMainRiver(grid, cellProcessed, riverIndex, config, mainIdx);
                    TuckTributaryMouthIntoMainRiver(grid, cellProcessed, riverIndex, config, mainIdx);

                    int lakeIdx = mainIdx == 0 ? cellProcessed.Count - 1 : 0;
                    bool extendStart = lakeIdx == 0;
                    if (IsTributaryAuthorizedForLakeEndpoint(
                            grid, riverIndex, cellProcessed[lakeIdx], config))
                    {
                        ApplyLakeEndpointVisualTuck(grid, cellProcessed, riverIndex, config, extendStart);
                    }
                }
                else
                {
                    ApplySplitModeLakeMouthApproaches(grid, cellProcessed, riverIndex, config);
                }
            }
            else
                ApplySplitModeLakeMouthApproaches(grid, cellProcessed, riverIndex, config);
        }

        static float SamplePolylineWorldYAtClosestXZ(Vector3 p, List<Vector3> polyline)
        {
            if (polyline == null || polyline.Count == 0)
                return p.y;
            if (polyline.Count == 1)
                return polyline[0].y;

            float bestDist = float.MaxValue;
            float bestY = polyline[0].y;
            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Vector3 a = polyline[i];
                Vector3 b = polyline[i + 1];
                Vector2 ab = new Vector2(b.x - a.x, b.z - a.z);
                float lenSq = ab.sqrMagnitude;
                float t = lenSq < 1e-8f
                    ? 0f
                    : Mathf.Clamp01(Vector2.Dot(new Vector2(p.x - a.x, p.z - a.z), ab) / lenSq);
                Vector3 q = Vector3.Lerp(a, b, t);
                float d2 = (p.x - q.x) * (p.x - q.x) + (p.z - q.z) * (p.z - q.z);
                if (d2 < bestDist)
                {
                    bestDist = d2;
                    bestY = q.y;
                }
            }

            return bestY;
        }

        static float SampleWebFusionRiverNodeWorldY(
            GridSystem grid,
            MapGenConfig config,
            Vector2 c,
            ref CellData cell,
            Vector3 origin,
            float terrainY,
            float waterH01,
            float yOffset,
            float antiZ,
            float carveWorld,
            float waterSurfaceY,
            float cellSize,
            int riverIndex = 0)
        {
            if (config != null && WaterVisualPipelinePolicy.UsesUwpUnifiedWaterSurfaceLevel(config))
                return waterSurfaceY;

            if (IsCellSpacePointInLakeBody(grid, c) || cell.type == CellType.Water || cell.type == CellType.River)
                return waterSurfaceY;

            int cx = Mathf.FloorToInt(c.x);
            int cz = Mathf.FloorToInt(c.y);
            if (grid != null && grid.InBoundsCell(cx, cz))
            {
                if (grid.RiverVisualSurfaceMask != null &&
                    cx >= 0 && cz >= 0 &&
                    cx < grid.Width && cz < grid.Height &&
                    grid.RiverVisualSurfaceMask[cx, cz])
                    return waterSurfaceY;

                if (DistanceToNearestRiverCellChebyshev(grid, cx, cz) <= 3f)
                    return waterSurfaceY;

                if (config != null &&
                    IsCellSpacePointNearMainRiverCorridor(grid, config, cellSize, c, 1.15f))
                    return waterSurfaceY;
            }

            float bedY = origin.y + cell.height01 * terrainY;
            return Mathf.Min(bedY + carveWorld + antiZ, waterSurfaceY + cellSize * 0.015f);
        }

        static void ApplyWebFusionMonotonicSurfaceY(List<Vector3> path, float cellSize)
        {
            if (path == null || path.Count < 3)
                return;

            float maxRise = cellSize * 0.035f;
            for (int i = 1; i < path.Count - 1; i++)
            {
                float y = (path[i - 1].y + path[i].y + path[i + 1].y) / 3f;
                path[i] = new Vector3(path[i].x, y, path[i].z);
            }

            for (int i = path.Count - 2; i >= 0; i--)
            {
                float y = Mathf.Min(path[i].y, path[i + 1].y + maxRise);
                path[i] = new Vector3(path[i].x, y, path[i].z);
            }
        }

        static void ApplyWebFusionConfluenceSurfaceY(
            List<Vector3> centers,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config,
            float cellSize,
            int riverIndex)
        {
            if (riverIndex <= 0 || centers == null || cellPath == null ||
                centers.Count != cellPath.Count || centers.Count < 2 || config == null)
                return;
            if (!WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                return;

            int last = cellPath.Count - 1;
            if (!IsCellSpacePointNearMainRiverCorridor(grid, config, cellSize, cellPath[last], 1.35f))
                return;
            if (s_webFusionMainWorldCenters == null || s_webFusionMainWorldCenters.Count < 2)
                return;

            int blend = Mathf.Clamp(10, 8, Mathf.Max(8, centers.Count / 2));
            float tuckWorld = Mathf.Max(cellSize * 0.13f, 0.11f);
            float maxDrop = cellSize * 0.14f;
            for (int i = 0; i < centers.Count; i++)
            {
                int distFromEnd = centers.Count - 1 - i;
                if (distFromEnd >= blend)
                    continue;

                float t = 1f - distFromEnd / (float)Mathf.Max(1, blend - 1);
                t = Mathf.SmoothStep(0f, 1f, t);
                float mainY = SamplePolylineWorldYAtClosestXZ(centers[i], s_webFusionMainWorldCenters);
                float targetY = mainY - tuckWorld * t;
                if (i > 0)
                    targetY = Mathf.Max(targetY, centers[i - 1].y - maxDrop);
                centers[i] = new Vector3(centers[i].x, Mathf.Lerp(centers[i].y, targetY, t), centers[i].z);
            }
        }

        static List<Vector3> CellPolylineToWorldRiverSurface(
            GridSystem grid,
            MapGenConfig config,
            List<Vector2> cellPath,
            Vector3 origin,
            float cellSize,
            float baseWaterWorldY,
            int riverIndex = 0)
        {
            if (cellPath == null || cellPath.Count == 0)
                return new List<Vector3>();

            if (grid == null || config == null)
                return CellPolylineToWorldXZ(cellPath, origin, cellSize, baseWaterWorldY);

            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            float waterH01 = config.waterHeight01;
            float yOffset = Mathf.Max(config.waterSurfaceOffset, 0.02f);
            bool webFusion = WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config);
            float antiZ = Mathf.Max(0.02f, config.riverRibbonAntiZFightYOffsetWorld);
            float extraYOffset = Mathf.Max(0f, config.riverSurfaceMeshExtraYOffsetWorld);
            float ribbonLift = extraYOffset + antiZ;
            if (!webFusion)
                ribbonLift += Mathf.Max(0f, config.riverRibbonVerticalLiftWorld);
            float webFusionSurfaceLift = antiZ + extraYOffset;
            float waterSurfaceY = origin.y + waterH01 * terrainY + yOffset + (webFusion ? webFusionSurfaceLift : ribbonLift);
            float lakeLevelY = origin.y + waterH01 * terrainY + yOffset + antiZ;
            float maxRise = cellSize * 0.035f;
            float maxDrop = cellSize * (webFusion ? 0.12f : 0.035f);
            float carveFill = webFusion ? 0.38f : 0.35f;
            float carveWorld = Mathf.Max(0.01f, config.riverTerrainCarveDepthWorld) * carveFill;

            var r = new List<Vector3>(cellPath.Count);
            float prevY = webFusion ? waterSurfaceY : baseWaterWorldY;
            for (int i = 0; i < cellPath.Count; i++)
            {
                Vector2 c = cellPath[i];
                int cx = Mathf.Clamp(Mathf.FloorToInt(c.x), 0, grid.Width - 1);
                int cz = Mathf.Clamp(Mathf.FloorToInt(c.y), 0, grid.Height - 1);
                ref var cell = ref grid.GetCell(cx, cz);
                float wx = origin.x + c.x * cellSize;
                float wz = origin.z + c.y * cellSize;
                float wy;
                if (webFusion)
                {
                    wy = SampleWebFusionRiverNodeWorldY(
                        grid, config, c, ref cell, origin, terrainY, waterH01, yOffset, antiZ, carveWorld, waterSurfaceY, cellSize, riverIndex);
                }
                else
                {
                    bool inLakeWater = IsCellSpacePointInLakeBody(grid, c) ||
                        cell.type == CellType.Water ||
                        cell.type == CellType.River;
                    wy = inLakeWater
                        ? lakeLevelY
                        : origin.y + cell.height01 * terrainY + yOffset + ribbonLift;
                }

                if (i > 0)
                {
                    wy = Mathf.Min(wy, prevY + maxRise);
                    if (webFusion)
                        wy = Mathf.Max(wy, prevY - maxDrop);
                }

                if (!webFusion && i == cellPath.Count - 1 && IsCellSpacePointInOrNearLake(grid, c, 4))
                    wy = lakeLevelY;
                r.Add(new Vector3(wx, wy, wz));
                prevY = wy;
            }

            if (webFusion &&
                cellPath.Count >= 2 &&
                !(riverIndex > 0 && config != null && config.uwpOwnedVisualPolicy) &&
                IsCellSpacePointInOrNearLake(grid, cellPath[cellPath.Count - 1], 8))
            {
                int last = r.Count - 1;
                float submerge = cellSize * 0.14f;
                Vector3 p = r[last];
                p.y = Mathf.Min(p.y, lakeLevelY - submerge);
                r[last] = p;
            }

            return r;
        }

        static bool IsCellSpacePointOnLakeMouthSpan(GridSystem grid, Vector2 p)
        {
            return IsCellSpacePointInLakeBody(grid, p) ||
                   IsCellSpacePointWater(grid, p) ||
                   IsCellSpacePointInOrNearLake(grid, p, 1);
        }

        static bool TryGetLakeMouthSpanIndices(
            GridSystem grid,
            List<Vector2> cellPath,
            out int firstInLakeIdx,
            out int lastInLakeIdx)
        {
            firstInLakeIdx = -1;
            lastInLakeIdx = -1;
            if (grid == null || cellPath == null || cellPath.Count < 2)
                return false;

            for (int i = 0; i < cellPath.Count; i++)
            {
                if (!IsCellSpacePointOnLakeMouthSpan(grid, cellPath[i]))
                    continue;
                if (firstInLakeIdx < 0)
                    firstInLakeIdx = i;
                lastInLakeIdx = i;
            }

            return firstInLakeIdx >= 0 && lastInLakeIdx >= firstInLakeIdx;
        }

        static bool TryGetLakeMouthBorderSpanIndices(
            GridSystem grid,
            List<Vector2> cellPath,
            out int firstBorderIdx,
            out int lastBorderIdx)
        {
            firstBorderIdx = -1;
            lastBorderIdx = -1;
            if (grid?.LakeMouthCellsPacked == null || grid.LakeMouthCellsPacked.Count == 0 ||
                cellPath == null || cellPath.Count < 2)
                return false;

            for (int i = 0; i < cellPath.Count; i++)
            {
                int x = Mathf.Clamp(Mathf.FloorToInt(cellPath[i].x), 0, grid.Width - 1);
                int z = Mathf.Clamp(Mathf.FloorToInt(cellPath[i].y), 0, grid.Height - 1);
                if (!grid.LakeMouthCellsPacked.Contains(PackLakeCellLong(x, z)))
                    continue;
                if (firstBorderIdx < 0)
                    firstBorderIdx = i;
                lastBorderIdx = i;
            }

            return firstBorderIdx >= 0 && lastBorderIdx >= firstBorderIdx;
        }

        static bool TryResolveWebFusionLakeMouthBorderIndex(
            GridSystem grid,
            List<Vector2> cellPath,
            bool mouthAtEnd,
            out int borderIndex)
        {
            borderIndex = -1;
            if (grid == null || cellPath == null || cellPath.Count < 2)
                return false;

            if (TryGetLakeMouthBorderSpanIndices(grid, cellPath, out int firstBorder, out _))
            {
                borderIndex = firstBorder;
                return true;
            }

            if (TryFindLakeShoreCrossingIndex(grid, cellPath, mouthAtEnd, out int borderIdx, out _))
            {
                borderIndex = borderIdx;
                return true;
            }

            if (TryGetStrictLakeInteriorSpanIndices(grid, cellPath, out int firstInLake, out _))
            {
                borderIndex = firstInLake;
                return true;
            }

            return false;
        }

        /// <summary>Orilla a nivel del lago; perfil Y calibrado manualmente (T2/T3 WebFusion).</summary>
        static float ResolveWebFusionWaterSurfaceLevelY(GridSystem grid, MapGenConfig config)
        {
            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            float yOffset = Mathf.Max(config.waterSurfaceOffset, 0.02f);
            float antiZ = Mathf.Max(0.02f, config.riverRibbonAntiZFightYOffsetWorld);
            return grid.Origin.y + config.waterHeight01 * terrainY + yOffset + antiZ;
        }

        /// <summary>Nivel MS/boca lago UWP alineado con franja tributario (incluye extra mesh).</summary>
        static float ResolveUwpLakeMouthDisplayLevelY(GridSystem grid, MapGenConfig config)
        {
            if (config == null || grid == null)
                return ResolveWebFusionWaterSurfaceLevelY(grid, config);

            if (WaterVisualPipelinePolicy.UsesUwpUnifiedWaterSurfaceLevel(config))
            {
                float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
                float baseWaterY = grid.Origin.y + config.waterHeight01 * terrainY +
                    Mathf.Max(config.waterSurfaceOffset, 0.02f);
                return WaterVisualPipelinePolicy.ResolveUwpUnifiedChannelSurfaceWorldY(config, baseWaterY);
            }

            float lakeLevelY = ResolveWebFusionWaterSurfaceLevelY(grid, config);
            if (config.uwpOwnedVisualPolicy)
                lakeLevelY += Mathf.Max(0f, config.riverSurfaceMeshExtraYOffsetWorld);
            return lakeLevelY;
        }

        /// <summary>Hundimiento total calibrado: L=2.875 → sink=1.0 (Δ≈1.875).</summary>
        static float ResolveWebFusionWaterUnionFullSinkDrop(float lakeLevelY, float cellSize)
        {
            float sinkY = Mathf.Min(lakeLevelY - 1.875f, lakeLevelY * 0.348f);
            sinkY = Mathf.Max(sinkY, 1.0f);
            return Mathf.Max(lakeLevelY - sinkY, Mathf.Max(cellSize * 0.55f, 1.35f));
        }

        static float ResolveWebFusionWaterUnionSinkY(float lakeLevelY, float cellSize)
        {
            float drop = ResolveWebFusionWaterUnionFullSinkDrop(lakeLevelY, cellSize);
            return Mathf.Max(lakeLevelY - drop, lakeLevelY * 0.25f);
        }

        static int ResolveWebFusionWaterUnionChannelIndex(
            bool mouthAtEnd,
            int borderIdx,
            int mouthLastIdx,
            int pointCount)
        {
            if (mouthAtEnd)
                return Mathf.Max(0, borderIdx - 1);
            return Mathf.Min(pointCount - 1, mouthLastIdx + 1);
        }

        /// <summary>
        /// Perfil Y unión agua: desde unionIdx (canal) baja suavemente a sinkY bajo el lago.
        /// </summary>
        static void ApplyWebFusionTributaryWaterUnionYProfile(
            List<Vector3> centers,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config,
            float cellSize,
            bool mouthAtEnd)
        {
            if (centers == null || cellPath == null || grid == null || config == null ||
                centers.Count != cellPath.Count || centers.Count < 2)
                return;

            if (!TryGetWebFusionLakeConvergenceSpan(
                    grid, cellPath, mouthAtEnd, out int unionIdx, out int borderIdx, out int mouthLastIdx))
                return;

            float lakeLevelY = ResolveUwpLakeMouthDisplayLevelY(grid, config);
            float sinkY = ResolveWebFusionWaterUnionSinkY(lakeLevelY, cellSize);
            unionIdx = Mathf.Clamp(unionIdx, 0, centers.Count - 1);
            borderIdx = Mathf.Clamp(borderIdx, unionIdx + 1, centers.Count - 1);
            mouthLastIdx = Mathf.Clamp(mouthLastIdx, borderIdx, centers.Count - 1);

            float approachY = centers[unionIdx].y;
            approachY = Mathf.Clamp(approachY, sinkY, lakeLevelY);

            int fadeStart = unionIdx;
            int fadeEnd = mouthLastIdx;
            int fadeSpan = fadeEnd - fadeStart;
            for (int i = fadeStart + 1; i <= fadeEnd; i++)
            {
                float t = fadeSpan <= 0 ? 1f : (i - fadeStart) / (float)fadeSpan;
                t = Mathf.SmoothStep(0f, 1f, t);
                float y;
                if (t <= 0.38f)
                    y = Mathf.Lerp(approachY, lakeLevelY, t / 0.38f);
                else if (t <= 0.52f)
                    y = lakeLevelY;
                else if (WaterVisualPipelinePolicy.UsesUwpUnifiedWaterSurfaceLevel(config))
                    y = lakeLevelY;
                else
                    y = Mathf.Lerp(lakeLevelY, sinkY, (t - 0.52f) / 0.48f);

                Vector3 p = centers[i];
                p.y = y;
                centers[i] = p;
            }

            EnforceWebFusionLakeMouthConvergenceYMonotonic(
                centers, unionIdx, borderIdx, mouthLastIdx, cellSize);
        }

        /// <summary>UWP: ancla Y del tributario al carve del terreno (evita ribbon flotando).</summary>
        static void ApplyUwpTributaryTerrainCarveSnapY(
            List<Vector3> centers,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSize)
        {
            if (riverIndex <= 0 || centers == null || cellPath == null || grid == null || config == null ||
                !config.uwpOwnedVisualPolicy || centers.Count != cellPath.Count || centers.Count < 2)
                return;

            // Y unificado main/lago/tributario: no bajar el mesh al lecho carveado del terreno.
            if (WaterVisualPipelinePolicy.UsesUwpUnifiedWaterSurfaceLevel(config))
                return;

            int lakeUnionIdx = -1;
            int mainJoinIdx = -1;
            if (TryResolveTributaryMainJoinEndpointIndex(grid, config, cellPath, riverIndex, out mainJoinIdx))
            {
                bool mouthAtEnd = mainJoinIdx == 0;
                if (TryGetWebFusionLakeConvergenceSpan(
                        grid, cellPath, mouthAtEnd, out int unionIdx, out _, out _))
                    lakeUnionIdx = unionIdx;
            }

            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            Vector3 origin = grid.Origin;
            float yOffset = Mathf.Max(config.waterSurfaceOffset, 0.02f);
            float antiZ = Mathf.Max(0.02f, config.riverRibbonAntiZFightYOffsetWorld);
            float extra = Mathf.Max(0f, config.riverSurfaceMeshExtraYOffsetWorld);
            float carveWorld = Mathf.Max(0.01f, config.riverTerrainCarveDepthWorld) * 0.38f;
            float lakeLevelY = ResolveUwpLakeMouthDisplayLevelY(grid, config);
            int mainBlend = Mathf.Clamp(
                Mathf.CeilToInt(config.riverSurfaceTributaryConfluenceApproachCells * 0.45f), 4, 14);

            for (int i = 0; i < centers.Count; i++)
            {
                if (lakeUnionIdx >= 0 && i >= lakeUnionIdx)
                    continue;

                if (mainJoinIdx >= 0 && Mathf.Abs(i - mainJoinIdx) <= mainBlend)
                    continue;

                Vector2 c = cellPath[i];
                if (IsCellSpacePointInLakeBody(grid, c))
                    continue;

                int cx = Mathf.Clamp(Mathf.FloorToInt(c.x), 0, grid.Width - 1);
                int cz = Mathf.Clamp(Mathf.FloorToInt(c.y), 0, grid.Height - 1);
                ref var cell = ref grid.GetCell(cx, cz);
                float bedY = origin.y + cell.height01 * terrainY;
                float carvedRibbonY = bedY + carveWorld + yOffset + antiZ + extra;
                float targetY = Mathf.Min(carvedRibbonY + cellSize * 0.025f, lakeLevelY + cellSize * 0.015f);

                float y = centers[i].y;
                if (y > targetY + cellSize * 0.03f)
                    centers[i] = new Vector3(centers[i].x, targetY, centers[i].z);
            }
        }

        /// <summary>Perfil Y uniforme en el tramo cuerpo tributario (main→lago), evita joroba por relieve.</summary>
        static void ApplyUwpTributaryChannelUniformSurfaceY(
            List<Vector3> centers,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSize)
        {
            if (riverIndex <= 0 || centers == null || cellPath == null || grid == null || config == null ||
                !config.uwpOwnedVisualPolicy || centers.Count != cellPath.Count || centers.Count < 4)
                return;

            if (!TryResolveTributaryMainJoinEndpointIndex(grid, config, cellPath, riverIndex, out int mainJoinIdx))
                return;

            bool lakeAtEnd = mainJoinIdx == 0;
            if (!TryGetWebFusionLakeConvergenceSpan(
                    grid, cellPath, lakeAtEnd, out int unionIdx, out _, out _))
                return;

            int mainBlend = Mathf.Clamp(
                Mathf.CeilToInt(config.riverSurfaceTributaryConfluenceApproachCells * 0.45f), 4, 14);
            int start;
            int end;
            if (mainJoinIdx < unionIdx)
            {
                start = mainJoinIdx + mainBlend;
                end = unionIdx - 2;
            }
            else
            {
                start = unionIdx + 2;
                end = mainJoinIdx - mainBlend;
            }

            if (end <= start)
                return;

            float lakeLevelY = ResolveUwpLakeMouthDisplayLevelY(grid, config);
            float yCap = lakeLevelY + cellSize * 0.02f;
            float yMain = centers[mainJoinIdx].y;
            float yUnion = centers[unionIdx].y;
            float snapTol = cellSize * 0.035f;

            for (int i = start; i <= end; i++)
            {
                float t = (i - start) / (float)(end - start);
                t = Mathf.SmoothStep(0f, 1f, t);
                float yTarget = Mathf.Min(Mathf.Lerp(yMain, yUnion, t), yCap);
                float y = centers[i].y;
                if (Mathf.Abs(y - yTarget) > snapTol)
                    centers[i] = new Vector3(centers[i].x, yTarget, centers[i].z);
            }
        }

        static void EnforceWebFusionLakeMouthConvergenceYMonotonic(
            List<Vector3> centers,
            int unionIdx,
            int borderIdx,
            int mouthLastIdx,
            float cellSize)
        {
            if (centers == null || unionIdx < 0 || borderIdx <= unionIdx || mouthLastIdx < borderIdx)
                return;

            float maxRise = cellSize * 0.025f;
            float maxDrop = cellSize * 0.16f;
            for (int i = unionIdx + 1; i <= mouthLastIdx; i++)
            {
                float prevY = centers[i - 1].y;
                float y = centers[i].y;
                y = Mathf.Min(y, prevY + maxRise);
                y = Mathf.Max(y, prevY - maxDrop);
                Vector3 p = centers[i];
                p.y = y;
                centers[i] = p;
            }
        }

        /// <summary>
        /// Hacia el interior del lago Y no sube; ningún índice anterior a la unión canal supera Y de la unión.
        /// </summary>
        static void EnforceWebFusionWaterUnionYMonotonic(
            List<Vector3> centers,
            List<Vector2> cellPath,
            GridSystem grid,
            int borderIdx,
            int mouthLastIdx,
            bool mouthAtEnd)
        {
            if (centers == null || borderIdx < 0 || mouthLastIdx < borderIdx || mouthLastIdx >= centers.Count)
                return;

            float sinkY = centers[borderIdx].y;
            for (int i = borderIdx + 1; i <= mouthLastIdx; i++)
            {
                if (centers[i].y > sinkY)
                {
                    Vector3 p = centers[i];
                    p.y = sinkY;
                    centers[i] = p;
                }
            }

            int unionIdx = ResolveWebFusionWaterUnionChannelIndex(
                mouthAtEnd, borderIdx, mouthLastIdx, centers.Count);
            if (unionIdx < 0 || unionIdx >= centers.Count)
                return;

            float unionY = centers[unionIdx].y;
            int capEnd = mouthAtEnd ? unionIdx : unionIdx;
            for (int i = 0; i < capEnd; i++)
            {
                if (centers[i].y > unionY)
                {
                    Vector3 p = centers[i];
                    p.y = unionY;
                    centers[i] = p;
                }
            }
        }

        /// <summary>Aplica restricciones Y de unión agua (p. ej. tras editar spline manualmente).</summary>
        public static void ApplyWebFusionWaterUnionYMonotonicConstraints(
            List<Vector3> centers,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config)
        {
            if (centers == null || cellPath == null || grid == null || config == null ||
                centers.Count != cellPath.Count || centers.Count < 2)
                return;
            if (!WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                return;

            if (TryGetWebFusionLakeMouthRunSpan(grid, cellPath, mouthAtEnd: false, out int borderStart, out int mouthEndStart))
            {
                EnforceWebFusionWaterUnionYMonotonic(
                    centers, cellPath, grid, borderStart, mouthEndStart, mouthAtEnd: false);
            }

            if (TryGetWebFusionLakeMouthRunSpan(grid, cellPath, mouthAtEnd: true, out int borderEnd, out int mouthEndEnd))
            {
                EnforceWebFusionWaterUnionYMonotonic(
                    centers, cellPath, grid, borderEnd, mouthEndEnd, mouthAtEnd: true);
            }
        }

        /// <summary>
        /// Span Y boca lago: cruce tierra→lago + vértice de unión (último canal antes del hundimiento).
        /// </summary>
        static bool TryGetWebFusionLakeConvergenceSpan(
            GridSystem grid,
            List<Vector2> cellPath,
            bool mouthAtEnd,
            out int unionIdx,
            out int borderIdx,
            out int mouthLastIdx)
        {
            unionIdx = -1;
            borderIdx = -1;
            mouthLastIdx = -1;
            if (grid == null || cellPath == null || cellPath.Count < 2)
                return false;

            if (mouthAtEnd)
            {
                if (TryFindPolylineLakeEntryCrossingFromEnd(grid, cellPath, out int landSegIdx, out _, out _))
                {
                    unionIdx = Mathf.Clamp(landSegIdx, 0, cellPath.Count - 2);
                    borderIdx = -1;
                    for (int i = unionIdx + 1; i < cellPath.Count; i++)
                    {
                        if (IsStrictMouthFusionLakeInteriorSample(grid, cellPath[i]) ||
                            IsCellSpacePointInOrNearLake(grid, cellPath[i], 1))
                        {
                            borderIdx = i;
                            break;
                        }
                    }

                    if (borderIdx < 0)
                        borderIdx = unionIdx + 1;
                    mouthLastIdx = Mathf.Min(
                        borderIdx + WebFusionLakeMouthSinkVertexCount - 1,
                        cellPath.Count - 1);
                    return mouthLastIdx >= borderIdx;
                }

                for (int i = cellPath.Count - 1; i >= 0; i--)
                {
                    if (IsStrictMouthFusionLakeInteriorSample(grid, cellPath[i]) ||
                        IsCellSpacePointInOrNearLake(grid, cellPath[i], 1))
                        continue;
                    if (i >= cellPath.Count - 1)
                        return false;
                    unionIdx = i;
                    borderIdx = i + 1;
                    mouthLastIdx = Mathf.Min(
                        borderIdx + WebFusionLakeMouthSinkVertexCount - 1,
                        cellPath.Count - 1);
                    return mouthLastIdx >= borderIdx;
                }

                return false;
            }

            if (TryFindPolylineLakeExitCrossingFromStart(grid, cellPath, out int lakeSegIdx, out _, out _))
            {
                borderIdx = 0;
                mouthLastIdx = Mathf.Min(
                    WebFusionLakeMouthSinkVertexCount - 1,
                    lakeSegIdx,
                    cellPath.Count - 1);
                unionIdx = Mathf.Min(mouthLastIdx + 1, cellPath.Count - 1);
                return mouthLastIdx >= borderIdx;
            }

            for (int i = 0; i < cellPath.Count; i++)
            {
                if (IsStrictMouthFusionLakeInteriorSample(grid, cellPath[i]) ||
                    IsCellSpacePointInOrNearLake(grid, cellPath[i], 1))
                    continue;
                if (i <= 0)
                    return false;
                borderIdx = 0;
                mouthLastIdx = Mathf.Min(i - 1, WebFusionLakeMouthSinkVertexCount - 1);
                unionIdx = i;
                return mouthLastIdx >= borderIdx;
            }

            return false;
        }

        static bool TryGetWebFusionLakeMouthRunSpan(
            GridSystem grid,
            List<Vector2> cellPath,
            bool mouthAtEnd,
            out int borderIdx,
            out int mouthLastIdx)
        {
            borderIdx = -1;
            mouthLastIdx = -1;
            if (TryGetWebFusionLakeConvergenceSpan(
                    grid, cellPath, mouthAtEnd, out _, out borderIdx, out mouthLastIdx))
                return true;

            if (grid == null || cellPath == null || cellPath.Count < 2)
                return false;

            if (!TryResolveWebFusionLakeMouthBorderIndex(grid, cellPath, mouthAtEnd, out int legacyBorder))
                return false;

            if (mouthAtEnd)
            {
                borderIdx = legacyBorder;
                int i = legacyBorder;
                int count = 0;
                while (i < cellPath.Count && count < WebFusionLakeMouthSinkVertexCount &&
                       (IsWebFusionLakeInteriorChannelCell(grid, cellPath[i]) ||
                        IsCellSpacePointInsideLakeInterior(grid, cellPath[i])))
                {
                    mouthLastIdx = i;
                    count++;
                    i++;
                }

                if (count == 0)
                {
                    borderIdx = Mathf.Max(0, cellPath.Count - WebFusionLakeMouthSinkVertexCount);
                    mouthLastIdx = cellPath.Count - 1;
                }
            }
            else
            {
                if (!IsWebFusionLakeInteriorChannelCell(grid, cellPath[0]) &&
                    !IsCellSpacePointInsideLakeInterior(grid, cellPath[0]) &&
                    !IsCellSpacePointInOrNearLake(grid, cellPath[0], 4))
                    return false;

                borderIdx = 0;
                mouthLastIdx = Mathf.Min(WebFusionLakeMouthSinkVertexCount - 1, cellPath.Count - 1);
                if (legacyBorder > 0 && legacyBorder < cellPath.Count)
                {
                    borderIdx = legacyBorder;
                    mouthLastIdx = Mathf.Min(
                        legacyBorder + WebFusionLakeMouthSinkVertexCount - 1,
                        cellPath.Count - 1);
                }
            }

            return mouthLastIdx >= borderIdx;
        }

        static bool IsStrictMouthFusionLakeInteriorSample(GridSystem grid, Vector2 p)
        {
            return IsWebFusionLakeInteriorChannelCell(grid, p) ||
                IsCellSpacePointInLakeBody(grid, p);
        }

        static int CountStrictLakeInteriorRunFromEnd(GridSystem grid, List<Vector2> path, int maxCount)
        {
            if (grid == null || path == null || path.Count == 0 || maxCount <= 0)
                return 0;
            int count = 0;
            for (int i = path.Count - 1; i >= 0 && count < maxCount; i--)
            {
                if (!IsStrictMouthFusionLakeInteriorSample(grid, path[i]))
                    break;
                count++;
            }

            return count;
        }

        static int CountStrictLakeInteriorRunFromStart(GridSystem grid, List<Vector2> path, int maxCount)
        {
            if (grid == null || path == null || path.Count == 0 || maxCount <= 0)
                return 0;
            int count = 0;
            for (int i = 0; i < path.Count && count < maxCount; i++)
            {
                if (!IsStrictMouthFusionLakeInteriorSample(grid, path[i]))
                    break;
                count++;
            }

            return count;
        }

        static int CountMouthFusionLakeZoneRunFromEnd(GridSystem grid, List<Vector2> path, int maxCount)
        {
            if (grid == null || path == null || path.Count == 0 || maxCount <= 0)
                return 0;
            int count = 0;
            for (int i = path.Count - 1; i >= 0 && count < maxCount; i--)
            {
                if (!IsMouthFusionLakeZoneSample(grid, path[i]))
                    break;
                count++;
            }

            return count;
        }

        static int CountMouthFusionLakeZoneRunFromStart(GridSystem grid, List<Vector2> path, int maxCount)
        {
            if (grid == null || path == null || path.Count == 0 || maxCount <= 0)
                return 0;
            int count = 0;
            for (int i = 0; i < path.Count && count < maxCount; i++)
            {
                if (!IsMouthFusionLakeZoneSample(grid, path[i]))
                    break;
                count++;
            }

            return count;
        }

        static bool IsMouthFusionLakeZoneSample(GridSystem grid, Vector2 p)
        {
            return IsStrictMouthFusionLakeInteriorSample(grid, p) ||
                IsWebFusionLakeInteriorChannelCell(grid, p) ||
                IsCellSpacePointInOrNearLake(grid, p, 3) ||
                IsCellSpacePointInLakeBody(grid, p);
        }

        static bool TryResolveMouthFusionEndpointNearLake(
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            List<Vector2> cellPath,
            float cellSize,
            out bool trimStart,
            out bool trimEnd)
        {
            trimStart = false;
            trimEnd = false;
            if (grid == null || config == null || cellPath == null || cellPath.Count < 3 || riverIndex <= 0)
                return false;

            int last = cellPath.Count - 1;
            float maxDist = ResolveLakeMouthApproachMaxDistCells(config);

            if (IsLakeEmissaryRiverIndex(grid, riverIndex))
            {
                trimStart = true;
                return true;
            }

            if (TryResolveTributaryMainJoinEndpointIndex(
                    grid, config, cellPath, riverIndex, out int mainJoinIdx))
            {
                int lakeIdx = mainJoinIdx == 0 ? last : 0;
                trimStart = lakeIdx == 0;
                trimEnd = lakeIdx == last;
                return trimStart || trimEnd;
            }

            bool endNearLake = TryFindPolylineLakeEntryCrossingFromEnd(grid, cellPath, out _, out _, out _) ||
                IsTributaryEndpointNearLake(grid, cellPath, last) ||
                (grid.LakeMouthCellsPacked != null && grid.LakeMouthCellsPacked.Count > 0 &&
                 MinChebyshevDistToLakeMouth(cellPath[last], grid) <= maxDist) ||
                RiverFunctionalEndNearLake(grid, riverIndex, config) ||
                TryGetNearestLakeShorePoint(grid, cellPath[last], maxDist, out _);
            bool startNearLake = TryFindPolylineLakeExitCrossingFromStart(grid, cellPath, out _, out _, out _) ||
                IsLakeEmissaryCenterline(grid, cellPath, riverIndex) ||
                IsTributaryEndpointNearLake(grid, cellPath, 0) ||
                TryGetNearestLakeShorePoint(grid, cellPath[0], maxDist, out _);

            trimEnd = endNearLake;
            trimStart = startNearLake;
            return trimStart || trimEnd;
        }

        static void SnapMouthFusionShoreCutVertexY(
            List<Vector3> center,
            GridSystem grid,
            MapGenConfig config,
            bool snapStart,
            bool snapEnd)
        {
            if (grid == null || config == null || center == null || center.Count == 0)
                return;

            float channelY = ResolveUwpLakeMouthDisplayLevelY(grid, config);
            if (snapStart)
            {
                Vector3 p = center[0];
                p.y = channelY;
                center[0] = p;
            }

            if (snapEnd)
            {
                int i = center.Count - 1;
                Vector3 p = center[i];
                p.y = channelY;
                center[i] = p;
            }
        }

        /// <summary>Tras recorte mesh, ancla el primer/último nodo XZ en landTerminus de la orilla lago.</summary>
        static void SnapMouthFusionShoreCutVertexCellSpace(
            GridSystem grid,
            List<Vector3> center,
            List<Vector2> cellSpaceLine,
            bool snapStart,
            bool snapEnd)
        {
            if (grid == null || center == null || cellSpaceLine == null ||
                center.Count != cellSpaceLine.Count || center.Count == 0)
                return;

            if (snapStart)
            {
                Vector2 landTerminus = default;
                bool ok = TryFindPolylineLakeExitCrossingFromStart(
                    grid, cellSpaceLine, out _, out _, out landTerminus);
                if (!ok &&
                    TryResolveLakeShoreCrossingOrLandAnchor(grid, cellSpaceLine, mouthAtEnd: false, out int landIdx))
                {
                    landTerminus = cellSpaceLine[landIdx];
                    ok = true;
                }

                if (ok)
                {
                    cellSpaceLine[0] = landTerminus;
                    int gx = Mathf.FloorToInt(landTerminus.x);
                    int gz = Mathf.FloorToInt(landTerminus.y);
                    if (grid.InBoundsCell(gx, gz))
                    {
                        Vector3 w = grid.CellToWorldCenter(gx, gz);
                        Vector3 p = center[0];
                        center[0] = new Vector3(w.x, p.y, w.z);
                    }
                }
            }

            if (snapEnd)
            {
                Vector2 landTerminus = default;
                bool ok = TryFindPolylineLakeEntryCrossingFromEnd(
                    grid, cellSpaceLine, out _, out _, out landTerminus);
                if (!ok &&
                    TryResolveLakeShoreCrossingOrLandAnchor(grid, cellSpaceLine, mouthAtEnd: true, out int landIdx))
                {
                    landTerminus = cellSpaceLine[landIdx];
                    ok = true;
                }

                if (ok)
                {
                    int i = cellSpaceLine.Count - 1;
                    cellSpaceLine[i] = landTerminus;
                    int gx = Mathf.FloorToInt(landTerminus.x);
                    int gz = Mathf.FloorToInt(landTerminus.y);
                    if (grid.InBoundsCell(gx, gz))
                    {
                        Vector3 w = grid.CellToWorldCenter(gx, gz);
                        Vector3 p = center[i];
                        center[i] = new Vector3(w.x, p.y, w.z);
                    }
                }
            }
        }

        /// <summary>MouthFusion: recorta solo nodos interiores de lago (max 5) del mesh; spline intacto.</summary>
        static bool TryApplyMouthFusionLakeMouthMeshTrim(
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            List<Vector3> center,
            List<float> halfWidthWorld,
            List<Vector2> cellSpaceLine,
            out bool taperAtStart,
            out bool taperAtEnd)
        {
            taperAtStart = false;
            taperAtEnd = false;
            if (!WaterVisualPipelinePolicy.IsSplitLakeMsRiverMouthFusion(config) ||
                grid == null || config == null || riverIndex <= 0 ||
                center == null || halfWidthWorld == null || cellSpaceLine == null ||
                center.Count != cellSpaceLine.Count || center.Count != halfWidthWorld.Count ||
                center.Count < 2)
                return false;

            // Lake-first: el tributario debe penetrar hacia el centro del lago para fusionarse con el MS.
            if (config.uwpLakeFirstHydrologyPipeline && IsTributaryLakeOwner(grid, riverIndex))
                return false;

            int n = cellSpaceLine.Count;
            int meshStart = 0;
            int meshEnd = n - 1;
            float cellSize = grid.CellSizeWorld;
            int maxSink = WebFusionLakeMouthSinkVertexCount;

            TryResolveMouthFusionEndpointNearLake(
                grid, config, riverIndex, cellSpaceLine, cellSize, out bool wantTrimStart, out bool wantTrimEnd);

            if (wantTrimEnd)
            {
                if (TryFindPolylineLakeEntryCrossingFromEnd(grid, cellSpaceLine, out int landSegIdx, out _, out _) &&
                    landSegIdx >= 0 && landSegIdx < meshEnd)
                {
                    meshEnd = landSegIdx;
                    taperAtEnd = true;
                }
                else
                {
                    int tailRun = CountMouthFusionLakeZoneRunFromEnd(grid, cellSpaceLine, maxSink);
                    if (tailRun <= 0)
                        tailRun = CountStrictLakeInteriorRunFromEnd(grid, cellSpaceLine, maxSink);
                    if (tailRun > 0)
                    {
                        meshEnd = n - 1 - tailRun;
                        taperAtEnd = true;
                    }
                }
            }

            if (wantTrimStart)
            {
                if (TryFindPolylineLakeExitCrossingFromStart(
                        grid, cellSpaceLine, out int lakeSegIdx, out _, out _) &&
                    lakeSegIdx >= 0 && lakeSegIdx < maxSink)
                {
                    int unionIdx = lakeSegIdx + 1;
                    if (unionIdx > meshStart && unionIdx <= meshEnd)
                    {
                        meshStart = unionIdx;
                        taperAtStart = true;
                    }
                }

                if (!taperAtStart)
                {
                    int headRun = CountMouthFusionLakeZoneRunFromStart(grid, cellSpaceLine, maxSink);
                    if (headRun <= 0)
                        headRun = CountStrictLakeInteriorRunFromStart(grid, cellSpaceLine, maxSink);
                    if (headRun > 0)
                    {
                        meshStart = headRun;
                        taperAtStart = true;
                    }
                }
            }

            if (!taperAtStart && !taperAtEnd)
                return false;
            if (meshEnd - meshStart + 1 < 2)
                return false;

            int count = meshEnd - meshStart + 1;
            var trimmedCenter = center.GetRange(meshStart, count);
            var trimmedHalf = halfWidthWorld.GetRange(meshStart, count);
            var trimmedCell = cellSpaceLine.GetRange(meshStart, count);
            center.Clear();
            center.AddRange(trimmedCenter);
            halfWidthWorld.Clear();
            halfWidthWorld.AddRange(trimmedHalf);
            cellSpaceLine.Clear();
            cellSpaceLine.AddRange(trimmedCell);

            SnapMouthFusionShoreCutVertexY(center, grid, config, taperAtStart, taperAtEnd);
            SnapMouthFusionShoreCutVertexCellSpace(grid, center, cellSpaceLine, taperAtStart, taperAtEnd);

            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[MouthFusionTrim] riverIndex={riverIndex} pts={n}->{count} " +
                    $"taperStart={(taperAtStart ? 1 : 0)} taperEnd={(taperAtEnd ? 1 : 0)} " +
                    $"shoreY={ResolveWebFusionWaterSurfaceLevelY(grid, config):F3}");
            }

            return true;
        }

        static void ApplyMouthFusionShoreWidthTaper(List<float> halfWidths, bool atStart, int taperVerts = 5, float minMul = 0.42f)
        {
            if (halfWidths == null || halfWidths.Count < 2)
                return;

            int n = halfWidths.Count;
            int taper = Mathf.Clamp(taperVerts, 2, Mathf.Min(5, n - 1));
            for (int k = 0; k < taper; k++)
            {
                int idx = atStart ? k : (n - 1 - k);
                float t = (k + 1) / (float)taper;
                halfWidths[idx] *= Mathf.Lerp(minMul, 1f, t);
            }
        }

        const float LakeFirstTributaryCarveWidthMul = 0.9f;

        static void ScaleLakeFirstTributaryCarveMaskHalfWidths(List<float> maskHalfWidths)
        {
            if (maskHalfWidths == null || maskHalfWidths.Count == 0)
                return;
            for (int i = 0; i < maskHalfWidths.Count; i++)
                maskHalfWidths[i] = Mathf.Max(0.02f, maskHalfWidths[i] * LakeFirstTributaryCarveWidthMul);
        }

        /// <summary>
        /// Headwater arroyo: estrecha máscara/carve en todo el tramo; mesh cubre el surco (orilla blanca).
        /// </summary>
        static void ApplyLakeFirstHeadwaterCarveLikeInland(
            List<float> meshHalfWidths,
            List<float> maskHalfWidths,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSize)
        {
            if (meshHalfWidths == null || maskHalfWidths == null || cellPath == null)
                return;

            ScaleLakeFirstTributaryCarveMaskHalfWidths(maskHalfWidths);

            // Factor arroyo: baja el ancho superior del carve (no solo tip).
            for (int i = 0; i < maskHalfWidths.Count; i++)
                maskHalfWidths[i] = Mathf.Max(0.02f, maskHalfWidths[i] * LakeFirstHeadwaterCarveBodyMul);

            float minBody = Mathf.Max(0.08f, cellSize * LakeFirstHeadwaterCarveMinHalfCells);
            for (int i = 0; i < maskHalfWidths.Count; i++)
                maskHalfWidths[i] = Mathf.Max(maskHalfWidths[i], minBody);

            ApplyLakeFirstHeadwaterSourceWidthTaper(
                maskHalfWidths, cellPath, grid, config, riverIndex, cellSize,
                LakeFirstHeadwaterSourceWidthMinMulMask, LakeFirstHeadwaterSourceMinHalfCells);
            ApplyLakeFirstHeadwaterCarveAlongProfile(maskHalfWidths, cellPath, cellSize);
            ClampLakeFirstHeadwaterCarveHalfWidths(maskHalfWidths, cellPath, cellSize);

            SyncLakeFirstHeadwaterMeshToCarveMask(meshHalfWidths, maskHalfWidths, cellSize);
        }

        /// <summary>Tope duro de half-width: cuerpo arroyo; join ensancha máscara/carve para la esquina Y.</summary>
        static void ClampLakeFirstHeadwaterCarveHalfWidths(
            List<float> maskHalfWidths,
            List<Vector2> cellPath,
            float cellSize)
        {
            if (maskHalfWidths == null || cellPath == null || maskHalfWidths.Count < 2 ||
                maskHalfWidths.Count != cellPath.Count)
                return;

            float tipMin = cellSize * LakeFirstHeadwaterSourceMinHalfCells;
            float bodyMax = cellSize * LakeFirstHeadwaterCarveBodyMaxCells;
            float joinMax = cellSize * LakeFirstHeadwaterCarveJoinMaxCells;
            float total = PolylineLengthCellSpace(cellPath);
            if (total < 1e-4f)
                return;

            float acc = 0f;
            for (int i = 0; i < maskHalfWidths.Count; i++)
            {
                if (i > 0)
                    acc += Vector2.Distance(cellPath[i], cellPath[i - 1]);
                float along = acc / total;
                if (along >= 0.82f)
                {
                    // Subir máscara en la boca (Clamp solo no ensancha). Cubre la cuña Y sin flare a ancho del receptor.
                    float t = Mathf.SmoothStep(0f, 1f, (along - 0.82f) / 0.18f);
                    float joinTarget = Mathf.Lerp(bodyMax, joinMax, t);
                    maskHalfWidths[i] = Mathf.Clamp(
                        Mathf.Max(maskHalfWidths[i], joinTarget), tipMin, joinMax);
                }
                else
                {
                    maskHalfWidths[i] = Mathf.Clamp(maskHalfWidths[i], tipMin, bodyMax);
                }
            }
        }

        /// <summary>Refuerza estrecho en el primer tramo (nacimiento→cuerpo).</summary>
        static void ApplyLakeFirstHeadwaterCarveAlongProfile(
            List<float> maskHalfWidths,
            List<Vector2> cellPath,
            float cellSize)
        {
            if (maskHalfWidths == null || cellPath == null || maskHalfWidths.Count < 3 ||
                maskHalfWidths.Count != cellPath.Count)
                return;

            int n = maskHalfWidths.Count;
            float total = PolylineLengthCellSpace(cellPath);
            if (total < 1e-4f)
                return;

            int bodyIdx = Mathf.Clamp(Mathf.RoundToInt((n - 1) * 0.55f), 0, n - 1);
            float bodyCap = cellSize * LakeFirstHeadwaterCarveBodyMaxCells;
            float bodyW = Mathf.Min(
                Mathf.Max(maskHalfWidths[bodyIdx], cellSize * LakeFirstHeadwaterCarveMinHalfCells),
                bodyCap);
            float tipW = Mathf.Max(cellSize * LakeFirstHeadwaterSourceMinHalfCells, bodyW * LakeFirstHeadwaterSourceWidthMinMulMask);

            float acc = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                    acc += Vector2.Distance(cellPath[i], cellPath[i - 1]);
                float along = acc / total;
                if (along >= LakeFirstHeadwaterSourceTaperAlongEnd)
                {
                    maskHalfWidths[i] = Mathf.Min(maskHalfWidths[i], bodyW);
                    continue;
                }

                float t = Mathf.SmoothStep(0f, 1f, along / LakeFirstHeadwaterSourceTaperAlongEnd);
                maskHalfWidths[i] = Mathf.Lerp(tipW, bodyW, t);
            }
        }

        /// <summary>Mesh cubre el carve arroyo (ligeramente más ancho → orilla blanca continua).</summary>
        static void SyncLakeFirstHeadwaterMeshToCarveMask(
            List<float> meshHalfWidths,
            List<float> maskHalfWidths,
            float cellSize)
        {
            if (meshHalfWidths == null || maskHalfWidths == null)
                return;

            float cs = Mathf.Max(0.01f, cellSize);
            float meshFloor = Mathf.Max(0.08f, cs * LakeFirstHeadwaterMeshMinHalfMul);
            int n = Mathf.Min(meshHalfWidths.Count, maskHalfWidths.Count);
            for (int i = 0; i < n; i++)
            {
                float carveW = Mathf.Max(cs * LakeFirstHeadwaterSourceMinHalfCells, maskHalfWidths[i]);
                maskHalfWidths[i] = carveW;
                // No inflar mesh al radio entero Ceil*cs (sería ~2 celdas / foam gruesa).
                // Stamp usa Ceil+requireMask; el ribbon solo cubre la máscara + margen fino.
                float target = carveW * LakeFirstHeadwaterMeshOverCarveMul;
                meshHalfWidths[i] = Mathf.Max(meshFloor, target);
            }
        }

        /// <summary>
        /// Contrato canal Lake First: carve sigue al mesh con margen de orilla fino y constante
        /// (≈ 1/MeshOverCarveMul). Compartido por Headwater, main, inland y lake-spill.
        /// </summary>
        static void ApplyLakeFirstChannelCarveToMeshGrowthProfile(
            List<float> meshHalfWidths,
            List<float> maskHalfWidths,
            List<Vector2> cellPath,
            float cellSize,
            bool joinAtStart = false) =>
            ApplyLakeFirstHeadwaterCarveToMeshGrowthProfile(
                meshHalfWidths, maskHalfWidths, cellPath, cellSize, joinAtStart);

        /// <summary>
        /// El carve sigue al mesh con un margen de orilla fino y CONSTANTE (≈ 1/MeshOverCarveMul).
        /// El "crecimiento" organico del arroyo viene del ancho del propio canal (mesh+carve juntos,
        /// angosto en nacimiento y ancho en cuerpo/boca), no de encoger el carve respecto al mesh:
        /// reducir el carve por debajo de este margen deja terreno sin tallar dentro del mesh
        /// (las "islas" que asoman). Los vados conservan carve completo bajo el mesh en TerrainExporter.
        /// </summary>
        static void ApplyLakeFirstHeadwaterCarveToMeshGrowthProfile(
            List<float> meshHalfWidths,
            List<float> maskHalfWidths,
            List<Vector2> cellPath,
            float cellSize,
            bool joinAtStart = false)
        {
            if (meshHalfWidths == null || maskHalfWidths == null || cellPath == null ||
                meshHalfWidths.Count != maskHalfWidths.Count ||
                maskHalfWidths.Count != cellPath.Count ||
                cellPath.Count < 2)
                return;

            float total = PolylineLengthCellSpace(cellPath);
            if (total < 1e-4f)
                return;

            // Margen fino y constante: carve justo por debajo del mesh. Deja orilla blanca
            // uniforme y garantiza que el carve cubra el interior del mesh (sin islas).
            // alongToJoin: 0 en origen / 1 en unión (soporta boca al inicio o al final).
            float foamRatio = 1f / Mathf.Max(1.01f, LakeFirstChannelMeshOverCarveMul);
            float minCarveHalf = Mathf.Max(0.08f, cellSize * 0.58f);
            float acc = 0f;
            for (int i = 0; i < cellPath.Count; i++)
            {
                if (i > 0)
                    acc += Vector2.Distance(cellPath[i], cellPath[i - 1]);
                float along = Mathf.Clamp01(acc / total);
                float alongToJoin = joinAtStart ? (1f - along) : along;
                // Boca (union Y): carve casi al mesh para cubrir la cuña del tributario/receptor.
                float ratio = Mathf.Lerp(
                    foamRatio,
                    0.98f,
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 1f, alongToJoin)));
                maskHalfWidths[i] = Mathf.Max(minCarveHalf, meshHalfWidths[i] * ratio);
            }
        }

        /// <summary>Reduce half-width hacia el inicio del headwater; no modifica floor/profundidad del carve.</summary>
        static void ApplyLakeFirstHeadwaterSourceWidthTaper(
            List<float> halfWidths,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSize,
            float minMul,
            float minHalfCells)
        {
            if (halfWidths == null || cellPath == null || grid == null || config == null ||
                riverIndex <= 0 || halfWidths.Count != cellPath.Count || halfWidths.Count < 3 ||
                !IsLakeFirstHeadwaterFeeder(grid, riverIndex))
                return;

            int n = halfWidths.Count;
            int sourceIdx = ResolveLakeFirstInlandFeederSourcePathIndex(grid, config, cellPath, riverIndex);
            int blend = Mathf.Min(
                ResolveLakeFirstHeadwaterSourceBlendCount(cellPath, sourceIdx, cellSize),
                n - 1);
            if (blend < 2)
                return;

            int bodyIdx = sourceIdx == 0
                ? Mathf.Min(n - 1, blend)
                : Mathf.Max(0, n - 1 - blend);
            float bodyW = halfWidths[bodyIdx];
            float floorW = Mathf.Max(0.06f, cellSize * Mathf.Max(0.18f, minHalfCells));
            float minW = Mathf.Max(floorW, bodyW * Mathf.Clamp(minMul, 0.12f, 0.85f));

            if (sourceIdx == 0)
            {
                for (int i = 0; i < blend; i++)
                {
                    float t = blend <= 1 ? 1f : i / (float)(blend - 1);
                    t = Mathf.SmoothStep(0f, 1f, t);
                    halfWidths[i] = Mathf.Lerp(minW, bodyW, t);
                }

                return;
            }

            for (int k = 0; k < blend; k++)
            {
                int i = n - 1 - k;
                float t = blend <= 1 ? 1f : k / (float)(blend - 1);
                t = Mathf.SmoothStep(0f, 1f, t);
                halfWidths[i] = Mathf.Lerp(minW, bodyW, t);
            }
        }

        static int ResolveLakeFirstHeadwaterSourceBlendCount(List<Vector2> cellPath, int sourceIdx, float cellSize)
        {
            if (cellPath == null || cellPath.Count < 2)
                return LakeFirstHeadwaterSourceTaperMinCells;

            float targetSpanWorld = Mathf.Max(cellSize * 0.5f, cellSize * LakeFirstHeadwaterSourceTaperSpanWorldCells);
            float acc = 0f;
            int blend = 2;
            if (sourceIdx == 0)
            {
                for (int i = 1; i < cellPath.Count; i++)
                {
                    acc += Vector2.Distance(cellPath[i], cellPath[i - 1]) * cellSize;
                    blend = i + 1;
                    if (acc >= targetSpanWorld)
                        break;
                }
            }
            else
            {
                for (int i = cellPath.Count - 2; i >= 0; i--)
                {
                    acc += Vector2.Distance(cellPath[i + 1], cellPath[i]) * cellSize;
                    blend = cellPath.Count - i;
                    if (acc >= targetSpanWorld)
                        break;
                }
            }

            return Mathf.Clamp(
                blend,
                LakeFirstHeadwaterSourceTaperMinCells,
                Mathf.Min(LakeFirstHeadwaterSourceTaperMaxCells, cellPath.Count - 1));
        }

        /// <summary>
        /// Tras el pipeline lake-spill/inland (shore + scale 0.9): perfil V angosto sin tocar profundidad carve.
        /// Desactivado para carve continuo: usar ApplyLakeFirstHeadwaterCarveLikeInland.
        /// </summary>
        static void ApplyLakeFirstHeadwaterCarveVNarrowScale(
            List<float> maskHalfWidths,
            List<Vector2> cellPath,
            float cellSize)
        {
            // Conservado por compatibilidad; el pipeline activo ya no lo usa (fragmentaba máscara/carve).
            if (maskHalfWidths == null || maskHalfWidths.Count < 2)
                return;
            float minHalf = Mathf.Max(0.01f, cellSize) * LakeFirstHeadwaterCarveMinHalfCells;
            for (int i = 0; i < maskHalfWidths.Count; i++)
                maskHalfWidths[i] = Mathf.Max(maskHalfWidths[i], minHalf);
        }

        /// <summary>Lake-first: mesh un poco más ancho que el carve para que el shader blanco de orilla intersecte el terreno.</summary>
        static void ApplyLakeFirstTributaryShoreIntersectionWidthBoost(
            GridSystem grid,
            List<float> halfWidths,
            List<Vector2> cellPath,
            MapGenConfig config,
            int riverIndex,
            float baseHalfW,
            float cellSize,
            bool visualMeshOnly = false)
        {
            if (riverIndex <= 0 || halfWidths == null || cellPath == null || config == null ||
                !UwpTributaryOriginUtility.UsesLakeFirstTributaryCarvePipeline(grid, config, riverIndex) || halfWidths.Count < 2)
                return;

            float ribbonHalf = config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                ? config.riverVisualRibbonFullWidthCellsTributary * 0.5f * cellSize
                : baseHalfW;
            bool inlandVisual = visualMeshOnly && UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex);
            float shoreFloor = visualMeshOnly
                ? (inlandVisual
                    ? Mathf.Max(baseHalfW * 1.12f, ribbonHalf * 1.05f)
                    : Mathf.Max(baseHalfW * 1.48f, ribbonHalf * 1.44f))
                : Mathf.Max(baseHalfW * 1.22f, ribbonHalf * 1.14f);
            float globalMul = visualMeshOnly ? (inlandVisual ? 1.04f : 1.14f) : 1.04f;
            int n = Mathf.Min(halfWidths.Count, cellPath.Count);
            float totalLen = PolylineLengthCellSpace(cellPath);
            float acc = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                    acc += Vector2.Distance(cellPath[i], cellPath[i - 1]);
                float along = totalLen > 1e-4f ? acc / totalLen : 0f;
                float ang = InteriorTurnAngleDeg(cellPath, i);
                float bendBoost = visualMeshOnly
                    ? (ang >= JoinAngleHardDeg ? 1.38f : (ang >= JoinAngleSmoothDeg ? 1.24f : 1.1f))
                    : (ang >= JoinAngleHardDeg ? 1.2f : (ang >= JoinAngleSmoothDeg ? 1.1f : 1.04f));
                float mouthBoost = along <= 0.38f ? (visualMeshOnly ? 1.14f : 1.08f) : 1f;
                // Headwater: along≈0 es el nacimiento, no boca de lago — no ensanchar el origen.
                bool headwater = IsLakeFirstHeadwaterFeeder(grid, riverIndex);
                if (headwater && along < 0.45f)
                    continue;
                if (headwater)
                    mouthBoost = 1f;
                bool inlandFeeder = inlandVisual;
                float confAlong = 0.62f;
                float confBoost = along >= confAlong
                    ? (visualMeshOnly
                        ? (inlandFeeder ? 1.08f : 1.14f)
                        : 1.08f)
                    : 1f;
                if (inlandFeeder)
                    bendBoost = ang >= JoinAngleHardDeg ? 1.12f : (ang >= JoinAngleSmoothDeg ? 1.06f : 1.02f);
                halfWidths[i] = Mathf.Max(
                    halfWidths[i],
                    shoreFloor * globalMul * bendBoost * mouthBoost * confBoost);
            }
        }

        /// <summary>
        /// Headwater→receptor: copia el criterio inland/lake-spill→main
        /// (<see cref="ApplyLakeFirstMainJoinApproachMeshWiden"/> + target hacia half del parent).
        /// Cubre la cuña Y del mesh sin hinchar al 100% del receptor.
        /// </summary>
        static void ApplyLakeFirstHeadwaterReceiverJoinMeshWiden(
            GridSystem grid,
            List<float> meshHalfWidths,
            List<Vector2> cellPath,
            MapGenConfig config,
            int riverIndex,
            float cellSize)
        {
            if (config == null || meshHalfWidths == null || cellPath == null || riverIndex <= 0 ||
                !IsLakeFirstHeadwaterFeeder(grid, riverIndex) ||
                !TryResolveHeadwaterReceiverRiverIndex(grid, riverIndex, out int recvRi))
                return;

            int n = Mathf.Min(meshHalfWidths.Count, cellPath.Count);
            if (n < 4)
                return;

            float recvHalf = ResolveRiverRibbonHalfWidthCells(config, recvRi) * cellSize;
            float endMul = Mathf.Clamp(config.riverConfluenceTributaryEndWidthMul, 0.42f, 1.22f);
            // Inland MainJoinApproach: bodyMul 1.10 / endpointMul 1.22.
            const float bodyMul = 1.10f;
            float approachCfg = Mathf.Clamp(config.riverSurfaceTributaryConfluenceApproachCells, 10, 28);
            float approachDist = ResolveUwpConfluenceApproachDistCells(cellPath, approachCfg);
            ResolveUwpConfluenceBlendRange(
                cellPath, fromStart: false, approachDist, out int blendStart, out int blendEnd, out int bodyRef);
            bodyRef = Mathf.Clamp(bodyRef, 0, n - 1);
            float bodyW = meshHalfWidths[bodyRef];

            // Target: hacia half del receptor (Inland). En T inland↔headwater hace falta
            // solape fuerte en la cuña (foam); clamp 0.82×recv dejaba esquina sin orilla blanca.
            bool recvIsInland = UwpTributaryOriginUtility.IsInlandFeeder(grid, recvRi);
            float recvMeshHalf = recvHalf;
            if (grid.RiverVisualSurfaces != null &&
                recvRi >= 0 &&
                recvRi < grid.RiverVisualSurfaces.Count &&
                grid.RiverVisualSurfaces[recvRi] != null &&
                grid.RiverVisualSurfaces[recvRi].HalfWidthsWorld != null &&
                grid.RiverVisualSurfaces[recvRi].HalfWidthsWorld.Count > 0)
            {
                var rw = grid.RiverVisualSurfaces[recvRi].HalfWidthsWorld;
                int mid = rw.Count / 2;
                recvMeshHalf = Mathf.Max(recvHalf, rw[Mathf.Clamp(mid, 0, rw.Count - 1)]);
            }

            float targetJoin = recvIsInland
                ? Mathf.Max(bodyW * 1.38f, recvMeshHalf * 0.92f, cellSize * LakeFirstHeadwaterCarveJoinMaxCells * LakeFirstHeadwaterMeshOverCarveMul)
                : Mathf.Max(bodyW * 1.22f, recvHalf * endMul * 0.68f);
            if (recvIsInland)
                targetJoin = Mathf.Clamp(targetJoin, bodyW * 1.22f, Mathf.Max(recvMeshHalf * 1.08f, bodyW * 1.55f));
            else
                targetJoin = Mathf.Clamp(targetJoin, bodyW * 1.14f, recvHalf * 0.82f);

            for (int i = blendStart; i <= blendEnd; i++)
            {
                if (i < 0 || i >= n)
                    continue;
                float t = ConfluenceTaper01AlongPath(cellPath, i, blendStart, blendEnd, fromStart: false);
                float desired = Mathf.Lerp(bodyW * bodyMul, targetJoin, Mathf.SmoothStep(0f, 1f, t));
                meshHalfWidths[i] = Mathf.Max(meshHalfWidths[i], desired);
            }

            for (int i = Mathf.Max(0, blendEnd + 1); i < n; i++)
                meshHalfWidths[i] = Mathf.Max(meshHalfWidths[i], targetJoin);

            int cornerPts = Mathf.Clamp(Mathf.CeilToInt((blendEnd - blendStart + 1) * 0.28f), 3, 6);
            for (int k = 0; k < cornerPts; k++)
            {
                int i = n - 1 - k;
                if (i < 0)
                    break;
                meshHalfWidths[i] = Mathf.Max(meshHalfWidths[i], targetJoin);
            }
        }

        /// <summary>Lake-first: ensancha el mesh en aproximación al troncal (inland ≥10%).</summary>
        static void ApplyLakeFirstMainJoinApproachMeshWiden(
            GridSystem grid,
            List<float> meshHalfWidths,
            List<Vector2> cellPath,
            MapGenConfig config,
            int riverIndex)
        {
            if (config == null || meshHalfWidths == null || cellPath == null || riverIndex <= 0 ||
                !UsesLakeFirstMainJoinMeshTreatment(grid, config, riverIndex))
                return;

            bool inland = UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex);
            bool lakeSpill = UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex);
            // Spill→main: boca ancha. Inland→main: acercar a spill (evita punta + puente seco);
            // el Cap global ya estrecha el cuerpo; este target se reafirma post-Cap.
            float bodyMul = inland ? 1.10f : (lakeSpill ? 1.14f : 1.04f);
            float endpointMul = inland ? 1.24f : (lakeSpill ? 1.32f : 1.08f);

            int n = Mathf.Min(meshHalfWidths.Count, cellPath.Count);
            if (n < 4)
                return;

            int joinEp = n - 1;
            if (!TryResolveTributaryMainJoinEndpointIndex(grid, config, cellPath, riverIndex, out joinEp))
                joinEp = n - 1;

            bool joinAtStart = joinEp == 0;
            float approachCfg = Mathf.Clamp(config.riverSurfaceTributaryConfluenceApproachCells, 10, 28);
            float approachDist = ResolveUwpConfluenceApproachDistCells(cellPath, approachCfg);
            ResolveUwpConfluenceBlendRange(cellPath, joinAtStart, approachDist, out int blendStart, out int blendEnd, out int bodyRef);

            bodyRef = Mathf.Clamp(bodyRef, 0, n - 1);
            float bodyW = meshHalfWidths[bodyRef];
            float cellSize = Mathf.Max(0.01f, grid != null ? grid.CellSizeWorld : 1f);
            float mainHalf = config.riverVisualRibbonFullWidthCellsMain * 0.5f * cellSize;
            float endMul = Mathf.Clamp(config.riverConfluenceTributaryEndWidthMul, 0.42f, 1.22f);
            float mainJoinFactor = lakeSpill ? 0.72f : (inland ? 0.68f : 0.55f);
            float targetJoin = Mathf.Max(bodyW * endpointMul, mainHalf * endMul * mainJoinFactor);
            targetJoin = Mathf.Min(targetJoin, mainHalf * 0.95f);

            for (int i = blendStart; i <= blendEnd; i++)
            {
                if (i < 0 || i >= n)
                    continue;
                float t = ConfluenceTaper01AlongPath(cellPath, i, blendStart, blendEnd, joinAtStart);
                float desired = Mathf.Lerp(bodyW * bodyMul, targetJoin, Mathf.SmoothStep(0f, 1f, t));
                meshHalfWidths[i] = Mathf.Max(meshHalfWidths[i], desired);
            }

            for (int i = Mathf.Max(0, blendEnd + 1); i < n; i++)
                meshHalfWidths[i] = Mathf.Max(meshHalfWidths[i], targetJoin);

            // Últimos puntos de la cuña Y: garantizar mesh hasta el troncal.
            int cornerPts = Mathf.Clamp(Mathf.CeilToInt((blendEnd - blendStart + 1) * 0.30f), 3, 7);
            for (int k = 0; k < cornerPts; k++)
            {
                int i = joinAtStart ? k : n - 1 - k;
                if (i < 0 || i >= n)
                    break;
                meshHalfWidths[i] = Mathf.Max(meshHalfWidths[i], targetJoin);
            }
        }

        /// <summary>Lake-spill→main: en el tramo final, carve ≈ mesh (cubre cuña Y; evita foam blanca ancha).</summary>
        static void BoostLakeSpillMainJoinCarveMaskToMesh(
            List<float> meshHalfWidths,
            List<float> maskHalfWidths,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex)
        {
            if (meshHalfWidths == null || maskHalfWidths == null || cellPath == null ||
                meshHalfWidths.Count != maskHalfWidths.Count || cellPath.Count < 4)
                return;

            int n = meshHalfWidths.Count;
            int joinEp = n - 1;
            if (grid != null && config != null && riverIndex > 0 &&
                TryResolveTributaryMainJoinEndpointIndex(grid, config, cellPath, riverIndex, out int resolved))
                joinEp = resolved;
            else if (grid != null && config != null)
            {
                float cs = grid.CellSizeWorld;
                bool startMain = IsTributaryEndpointNearMain(grid, config, cs, cellPath, 0);
                bool endMain = IsTributaryEndpointNearMain(grid, config, cs, cellPath, n - 1);
                if (startMain && !endMain)
                    joinEp = 0;
                else if (endMain)
                    joinEp = n - 1;
            }

            bool joinAtStart = joinEp == 0;
            float total = PolylineLengthCellSpace(cellPath);
            if (total < 1e-4f)
                return;

            float acc = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                    acc += Vector2.Distance(cellPath[i], cellPath[i - 1]);
                float along = Mathf.Clamp01(acc / total);
                float fromJoin = joinAtStart ? along : (1f - along);
                if (fromJoin > 0.22f)
                    continue;
                float t = 1f - Mathf.Clamp01(fromJoin / 0.22f);
                float ratio = Mathf.Lerp(0.94f, 0.985f, Mathf.SmoothStep(0f, 1f, t));
                // Contrato: mask ≤ mesh×0.98 (sin sobre-carve → bandeja blanca).
                float capped = meshHalfWidths[i] * Mathf.Min(ratio, 0.98f);
                maskHalfWidths[i] = Mathf.Max(maskHalfWidths[i], capped);
                maskHalfWidths[i] = Mathf.Min(maskHalfWidths[i], meshHalfWidths[i] * 0.98f);
            }
        }

        /// <summary>InlandFeeder: carve/máscara con el mismo perfil estrecho que lake-spill (mesh visual más ancho).</summary>
        const float LakeSpillCarveFromMeshMul = 0.80f;
        /// <summary>Tope world half inland: arroyo, no spill. ~1.85 celdas @ cell=3 → ~5.5m half.</summary>
        const float LakeFirstInlandMeshMaxHalfCells = 1.85f;
        /// <summary>Tras Cap: mesh ≥ Ceil(mask)·cs · OverCarve (evita foam perdido / “desfase Y” en orilla).</summary>
        const float LakeFirstInlandHeadwaterJoinMeshMul = 1.42f;
        /// <summary>Extra half-cells sobre el body inland en la T (cubre stamp combinado + cuña).</summary>
        const float LakeFirstInlandHeadwaterJoinExtraHalfCells = 0.65f;

        static void CapLakeFirstInlandFeederHalfWidths(
            List<float> meshHalfWidths,
            List<float> maskHalfWidths,
            float cellSize)
        {
            if (meshHalfWidths == null || meshHalfWidths.Count == 0)
                return;
            float cs = Mathf.Max(0.01f, cellSize);
            float maxMesh = cs * LakeFirstInlandMeshMaxHalfCells;
            for (int i = 0; i < meshHalfWidths.Count; i++)
                meshHalfWidths[i] = Mathf.Min(meshHalfWidths[i], maxMesh);
            if (maskHalfWidths == null)
                return;
            int n = Mathf.Min(meshHalfWidths.Count, maskHalfWidths.Count);
            for (int i = 0; i < n; i++)
                maskHalfWidths[i] = Mathf.Min(maskHalfWidths[i], meshHalfWidths[i] * LakeSpillCarveFromMeshMul);
        }

        /// <summary>
        /// Cap deja mesh half &lt; radio entero del stamp (Ceil) → orilla sin foam / fake Y-offset.
        /// Reafirma mesh ≥ alcance de máscara × MeshOverCarve (contrato canal Lake First).
        /// </summary>
        static void SyncLakeFirstInlandMeshOverCarveAfterCap(
            List<float> meshHalfWidths,
            List<float> maskHalfWidths,
            float cellSize)
        {
            if (meshHalfWidths == null || maskHalfWidths == null)
                return;
            float cs = Mathf.Max(0.01f, cellSize);
            float over = Mathf.Max(1.05f, LakeFirstChannelMeshOverCarveMul);
            int n = Mathf.Min(meshHalfWidths.Count, maskHalfWidths.Count);
            for (int i = 0; i < n; i++)
            {
                float maskW = Mathf.Max(0.08f, maskHalfWidths[i]);
                int radiusCells = Mathf.Max(1, Mathf.CeilToInt(maskW / cs));
                float stampReach = radiusCells * cs;
                float target = Mathf.Max(stampReach * over, maskW * over);
                meshHalfWidths[i] = Mathf.Max(meshHalfWidths[i], target);
            }
        }

        /// <summary>
        /// Inland receptor: ensancha mesh en la T mid-body donde desemboca un HeadwaterFeeder
        /// (cubre cuña / orilla blanca; el Cap solo no alcanza ahí).
        /// </summary>
        static void ApplyLakeFirstInlandHeadwaterJoinMeshWiden(
            GridSystem grid,
            List<float> meshHalfWidths,
            List<Vector2> cellPath,
            MapGenConfig config,
            int riverIndex,
            float cellSize)
        {
            if (grid == null || config == null || meshHalfWidths == null || cellPath == null ||
                riverIndex <= 0 || !UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex) ||
                grid.RiverOriginKinds == null)
                return;

            int n = Mathf.Min(meshHalfWidths.Count, cellPath.Count);
            if (n < 4)
                return;

            float cs = Mathf.Max(0.01f, cellSize);
            float approachCells = Mathf.Clamp(config.riverSurfaceTributaryConfluenceApproachCells, 8, 22);
            float approachWorld = approachCells * cs;

            for (int hi = 1; hi < grid.RiverOriginKinds.Count; hi++)
            {
                if (UwpTributaryOriginUtility.GetOrigin(grid, hi) != UwpTributaryOriginKind.HeadwaterFeeder)
                    continue;
                if (!TryResolveHeadwaterReceiverRiverIndex(grid, hi, out int recvRi) || recvRi != riverIndex)
                    continue;

                List<Vector2> hwLine = null;
                if (grid.LakeFirstWaterGraph != null &&
                    grid.LakeFirstWaterGraph.FinalCenterlineByRiverIndex != null &&
                    grid.LakeFirstWaterGraph.FinalCenterlineByRiverIndex.TryGetValue(hi, out var finalHw) &&
                    finalHw != null && finalHw.Count >= 2)
                    hwLine = finalHw;
                else if (grid.RiverCenterlinesCellSpace != null &&
                         hi < grid.RiverCenterlinesCellSpace.Count)
                    hwLine = grid.RiverCenterlinesCellSpace[hi];
                if (hwLine == null || hwLine.Count < 2)
                    continue;

                Vector2 mouth = hwLine[hwLine.Count - 1];
                int bestIdx = 0;
                float bestDist = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    float d = (cellPath[i] - mouth).sqrMagnitude;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestIdx = i;
                    }
                }

                float bodyW = meshHalfWidths[Mathf.Clamp(bestIdx, 0, n - 1)];
                // Estimación half-mesh headwater en boca: el stamp lateral de la T
                // ensancha el surco conjunto; el inland debe cubrir esa cuña.
                float hwMouthHalfEst = cs * LakeFirstHeadwaterCarveJoinMaxCells * LakeFirstHeadwaterMeshOverCarveMul;
                if (grid.RiverVisualSurfaces != null &&
                    hi < grid.RiverVisualSurfaces.Count &&
                    grid.RiverVisualSurfaces[hi] != null &&
                    grid.RiverVisualSurfaces[hi].HalfWidthsWorld != null &&
                    grid.RiverVisualSurfaces[hi].HalfWidthsWorld.Count > 0)
                {
                    var hwW = grid.RiverVisualSurfaces[hi].HalfWidthsWorld;
                    hwMouthHalfEst = Mathf.Max(hwMouthHalfEst, hwW[hwW.Count - 1]);
                }

                float target = Mathf.Max(
                    bodyW * LakeFirstInlandHeadwaterJoinMeshMul,
                    bodyW + cs * LakeFirstInlandHeadwaterJoinExtraHalfCells,
                    bodyW * 0.55f + hwMouthHalfEst * 0.95f,
                    cs * (LakeFirstInlandMeshMaxHalfCells * LakeFirstInlandHeadwaterJoinMeshMul));

                // Approach más corto y pico más ancho → cuña T con foam continuo.
                float localApproach = Mathf.Min(approachWorld, cs * 14f);

                float accL = 0f;
                for (int i = bestIdx; i >= 0; i--)
                {
                    if (i < bestIdx)
                        accL += Vector2.Distance(cellPath[i], cellPath[i + 1]);
                    if (accL > localApproach)
                        break;
                    float t = 1f - Mathf.Clamp01(accL / Mathf.Max(1e-4f, localApproach));
                    float desired = Mathf.Lerp(bodyW, target, Mathf.SmoothStep(0f, 1f, t));
                    meshHalfWidths[i] = Mathf.Max(meshHalfWidths[i], desired);
                }

                float accR = 0f;
                for (int i = bestIdx; i < n; i++)
                {
                    if (i > bestIdx)
                        accR += Vector2.Distance(cellPath[i], cellPath[i - 1]);
                    if (accR > localApproach)
                        break;
                    float t = 1f - Mathf.Clamp01(accR / Mathf.Max(1e-4f, localApproach));
                    float desired = Mathf.Lerp(bodyW, target, Mathf.SmoothStep(0f, 1f, t));
                    meshHalfWidths[i] = Mathf.Max(meshHalfWidths[i], desired);
                }

                // Refuerzo pico local (5 pts): cuña interior asimétrica de la T.
                const int peak = 5;
                for (int k = -peak; k <= peak; k++)
                {
                    int i = bestIdx + k;
                    if (i < 0 || i >= n)
                        continue;
                    float fall = 1f - Mathf.Abs(k) / (float)peak;
                    meshHalfWidths[i] = Mathf.Max(
                        meshHalfWidths[i],
                        Mathf.Lerp(bodyW, target, Mathf.SmoothStep(0f, 1f, fall)));
                }
            }
        }

        static void SyncLakeFirstInlandMaskToLakeSpillCarve(
            List<float> meshHalfWidths,
            List<float> maskHalfWidths)
        {
            if (meshHalfWidths == null || maskHalfWidths == null)
                return;

            int n = Mathf.Min(meshHalfWidths.Count, maskHalfWidths.Count);
            for (int i = 0; i < n; i++)
                maskHalfWidths[i] = Mathf.Min(maskHalfWidths[i], meshHalfWidths[i] * LakeSpillCarveFromMeshMul);
        }

        /// <summary>Lake-first: el carve UWP usa Ceil(halfW/cellSize) celdas; el mesh debe cubrir ese alcance.</summary>
        static void AlignLakeFirstTributaryMeshToCarveReach(
            GridSystem grid,
            List<float> meshHalfWidths,
            List<Vector2> cellPath,
            MapGenConfig config,
            int riverIndex,
            float cellSize)
        {
            if (config == null || !UwpTributaryOriginUtility.UsesLakeFirstTributaryCarvePipeline(grid, config, riverIndex) ||
                riverIndex <= 0 || meshHalfWidths == null || meshHalfWidths.Count < 2)
                return;

            float cs = Mathf.Max(0.01f, cellSize);
            float shorePad = Mathf.Max(0.08f, config.riverVisualRasterMaskExtraCellMargin * 0.35f) * cs;
            int n = Mathf.Min(meshHalfWidths.Count, cellPath != null ? cellPath.Count : meshHalfWidths.Count);
            for (int i = 0; i < n; i++)
            {
                float hw = meshHalfWidths[i];
                int radiusCells = Mathf.Max(1, Mathf.CeilToInt(hw / cs));
                float carveReach = radiusCells * cs;
                float ang = cellPath != null ? InteriorTurnAngleDeg(cellPath, i) : 0f;
                float curveMul = ang >= JoinAngleHardDeg ? 1.12f : (ang >= JoinAngleSmoothDeg ? 1.06f : 1f);
                meshHalfWidths[i] = Mathf.Max(hw, carveReach * curveMul + shorePad);
            }
        }

        static void AppendLakeFirstTributaryCenterlineTowardCentroid(
            GridSystem grid,
            int riverIndex,
            List<Vector2> line,
            MapGenConfig config)
        {
            if (grid == null || line == null || line.Count < 2 || config == null ||
                !config.uwpLakeFirstHydrologyPipeline || riverIndex <= 0 ||
                !IsTributaryLakeOwner(grid, riverIndex) || grid.LakeFirstWaterGraph == null)
                return;

            int compIdx = -1;
            for (int ti = 0; ti < grid.LakeFirstWaterGraph.Tributaries.Count; ti++)
            {
                var trib = grid.LakeFirstWaterGraph.Tributaries[ti];
                if (trib.Accepted && trib.RiverIndex == riverIndex)
                {
                    compIdx = trib.LakeComponentIndex;
                    break;
                }
            }

            if (compIdx < 0 || grid.LakeBodyComponents == null || compIdx >= grid.LakeBodyComponents.Count)
                return;

            var comp = grid.LakeBodyComponents[compIdx];
            if (comp == null || comp.Count == 0)
                return;

            Vector2 centroid = ComputeLakeComponentCentroid(comp);
            Vector2 mouth = line[0];
            Vector2 toCentroid = centroid - mouth;
            if (toCentroid.sqrMagnitude < 0.25f)
                return;

            var ingress = new List<Vector2>(6);
            for (int k = 5; k >= 1; k--)
            {
                float t = k / 5f;
                Vector2 p = Vector2.Lerp(mouth, centroid, 0.1f + t * 0.38f);
                int cx = Mathf.FloorToInt(p.x);
                int cz = Mathf.FloorToInt(p.y);
                if (comp.Contains(PackLakeCellLong(cx, cz)) || k <= 2)
                    ingress.Add(p);
            }

            if (ingress.Count == 0)
                return;

            ingress.Reverse();
            line.InsertRange(0, ingress);
        }

        /// <summary>Lake-first: une el extremo al troncal sin recorrer el main (evita “pasa de largo y vuelve”).</summary>
        static void AppendLakeFirstTributaryCenterlineTowardMainRiver(
            GridSystem grid,
            int riverIndex,
            List<Vector2> line,
            MapGenConfig config)
        {
            if (grid == null || line == null || line.Count < 2 || config == null ||
                !UwpTributaryOriginUtility.ShouldApplyMainRiverConfluenceIngress(grid, riverIndex, config))
                return;

            if (!TryResolveTributaryMainJoinEndpointIndex(grid, config, line, riverIndex, out int mainIdx))
                mainIdx = line.Count - 1;

            bool joinAtEnd = mainIdx == line.Count - 1;
            bool joinAtStart = mainIdx == 0;
            if (!joinAtEnd && !joinAtStart)
                return;

            if (!TryBuildMainRiverCorridorSampler(grid, config, grid.CellSizeWorld, out MainRiverCorridorSampler sampler))
                return;

            Vector2 mouth = joinAtEnd ? line[line.Count - 1] : line[0];
            bool lakeSpill = UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex);

            // Spill→main: NUNCA teleportar la boca a la confluencia registrada (suele estar
            // dentro/pasado del canal y recrea el V). Solo acercar al punto más cercano del main.
            if (!lakeSpill &&
                TryResolveTributaryJoinOnMainRiver(grid, riverIndex, line, config, out Vector2 join))
            {
                line[mainIdx] = join;
                mouth = join;
            }

            float bestDist = float.MaxValue;
            Vector2 bestJoin = mouth;
            for (int i = 0; i < sampler.Line.Count - 1; i++)
            {
                Vector2 q = ClosestPointOnOpenSegment2D(mouth, sampler.Line[i], sampler.Line[i + 1]);
                float d = (mouth - q).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestJoin = q;
                }
            }

            // Spill→main: anclar a ORILLA del corredor (no al eje). Ingress/tip al eje recreaban el U.
            if (lakeSpill)
            {
                Vector2 toAxis = bestJoin - mouth;
                float dist = toAxis.magnitude;
                float bankR = Mathf.Max(
                    Mathf.Max(sampler.CoreRadiusCells * 1.05f, sampler.RadiusCells * 0.72f),
                    ResolveMainVisualBankRadiusCells(config, sampler));
                if (dist > 1e-4f && dist > bankR)
                    line[mainIdx] = bestJoin - (toAxis / dist) * bankR;
                else if (dist > 1e-4f)
                {
                    // Ya dentro del half visual: empujar afuera (no dejar mouth dentro de la cinta).
                    Vector2 fromAxis = mouth - bestJoin;
                    if (fromAxis.sqrMagnitude > 1e-8f)
                        line[mainIdx] = bestJoin + fromAxis.normalized * bankR;
                    else
                        line[mainIdx] = bestJoin;
                }
                else
                    line[mainIdx] = bestJoin;
                return;
            }

            // Entrar al canal en la dirección de aproximación del trib (no tangent del main).
            Vector2 neighbor = joinAtEnd ? line[line.Count - 2] : line[1];
            Vector2 approach = bestJoin - neighbor;
            if (approach.sqrMagnitude < 1e-8f)
                approach = bestJoin - mouth;
            if (approach.sqrMagnitude < 1e-8f)
                return;
            approach.Normalize();

            // Tuck corto al interior del troncal (inland / lake-owner no-spill).
            float tuckMul = 0.38f;
            float tuckMax = 1.35f;
            float tuck = Mathf.Clamp(sampler.CoreRadiusCells * tuckMul, 0.35f, tuckMax);
            Vector2 deep = bestJoin + approach * tuck;
            if (WouldFoldBackBridge(neighbor, mouth, deep))
                deep = bestJoin;

            mouth = line[mainIdx];

            var ingress = new List<Vector2>(2);
            for (int k = 1; k <= 2; k++)
            {
                float t = k / 2f;
                ingress.Add(Vector2.Lerp(mouth, deep, 0.40f + t * 0.60f));
            }

            if (ingress.Count == 0)
                return;

            if (joinAtEnd)
                line.AddRange(ingress);
            else
            {
                for (int k = ingress.Count - 1; k >= 0; k--)
                    line.Insert(0, ingress[k]);
            }
        }

        static Vector2 ComputeLakeComponentCentroid(HashSet<long> comp)
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

        static void ApplyWebFusionLakeBorderToAnchorHeightBlend(
            List<Vector3> centers,
            List<Vector2> cellPath,
            GridSystem grid,
            float lakeLevelY,
            float anchorDrop,
            bool anchorAtStart)
        {
            if (centers == null || cellPath == null || grid == null ||
                centers.Count != cellPath.Count || centers.Count < 2)
                return;
            if (!TryGetStrictLakeInteriorSpanIndices(grid, cellPath, out int firstInLake, out int lastInLake))
            {
                if (!TryGetLakeMouthSpanIndices(grid, cellPath, out firstInLake, out lastInLake))
                    return;
            }

            int borderIndex = anchorAtStart ? lastInLake : firstInLake;
            int anchorIndex = anchorAtStart ? firstInLake : lastInLake;
            if (TryGetLakeMouthBorderSpanIndices(grid, cellPath, out int firstBorder, out int lastBorder))
            {
                borderIndex = anchorAtStart ? lastBorder : firstBorder;
                if (anchorAtStart && firstInLake >= 0)
                    anchorIndex = firstInLake;
                else if (!anchorAtStart && lastInLake >= 0)
                    anchorIndex = lastInLake;
            }
            if (anchorIndex == borderIndex)
            {
                Vector3 p = centers[anchorIndex];
                p.y = Mathf.Min(p.y, lakeLevelY - anchorDrop);
                centers[anchorIndex] = p;
                return;
            }

            float anchorY = lakeLevelY - anchorDrop;
            float borderY = lakeLevelY;
            int lo = Mathf.Min(borderIndex, anchorIndex);
            int hi = Mathf.Max(borderIndex, anchorIndex);
            int span = hi - lo;
            for (int i = lo; i <= hi; i++)
            {
                float t = span > 0 ? (i - lo) / (float)span : 1f;
                if (anchorAtStart)
                    t = 1f - t;
                float targetY = Mathf.Lerp(borderY, anchorY, t);
                Vector3 p = centers[i];
                p.y = Mathf.Min(p.y, targetY);
                centers[i] = p;
            }
        }

        /// <summary>Confluencia tributario↔troncal: T2/T3/T4 → 2.875, 2.605, 2.335.</summary>
        static void ApplyWebFusionTributaryMainConfluenceYProfile(
            List<Vector3> centers,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config,
            float cellSize,
            int riverIndex,
            bool fromStart)
        {
            if (riverIndex <= 0 || centers == null || cellPath == null || config == null ||
                centers.Count < 2)
                return;

            float channelY = ResolveWebFusionWaterSurfaceLevelY(grid, config);
            float dropMid = config.uwpOwnedVisualPolicy ? 0.05f : 0.27f;
            float dropEnd = config.uwpOwnedVisualPolicy ? 0.02f : 0.54f;

            if (fromStart)
            {
                if (centers.Count >= 3)
                {
                    centers[0] = new Vector3(centers[0].x, channelY - dropEnd, centers[0].z);
                    centers[1] = new Vector3(centers[1].x, channelY - dropMid, centers[1].z);
                    centers[2] = new Vector3(centers[2].x, channelY, centers[2].z);
                }
                else if (centers.Count >= 1)
                {
                    int i = 0;
                    Vector3 p = centers[i];
                    p.y = channelY - dropEnd;
                    centers[i] = p;
                }

                return;
            }

            int n = centers.Count;
            if (n >= 3)
            {
                centers[n - 3] = new Vector3(centers[n - 3].x, channelY, centers[n - 3].z);
                centers[n - 2] = new Vector3(centers[n - 2].x, channelY - dropMid, centers[n - 2].z);
                centers[n - 1] = new Vector3(centers[n - 1].x, channelY - dropEnd, centers[n - 1].z);
            }
            else if (n >= 1)
            {
                Vector3 p = centers[n - 1];
                p.y = channelY - dropEnd;
                centers[n - 1] = p;
            }
        }

        static void ApplyWebFusionTributaryEndpointSubmergeY(
            List<Vector3> centers,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSize,
            float surfaceWaterY)
        {
            if (riverIndex <= 0 || centers == null || cellPath == null || config == null ||
                centers.Count != cellPath.Count || centers.Count < 2)
                return;
            if (!WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                return;

            if (IsLakeEmissaryRiverIndex(grid, riverIndex))
            {
                ApplyWebFusionTributaryWaterUnionYProfile(
                    centers, cellPath, grid, config, cellSize, mouthAtEnd: false);
                if (TryResolveTributaryMainJoinEndpointIndex(
                        grid, config, cellPath, riverIndex, out int emMainIdx) &&
                    s_webFusionMainWorldCenters != null && s_webFusionMainWorldCenters.Count >= 2)
                {
                    ApplyWebFusionTributaryMainConfluenceYProfile(
                        centers, cellPath, grid, config, cellSize, riverIndex,
                        fromStart: emMainIdx == 0);
                }

                return;
            }

            if (TryResolveTributaryMainJoinEndpointIndex(
                    grid, config, cellPath, riverIndex, out int mainJoinIdx))
            {
                bool lakeAtEnd = mainJoinIdx == 0;
                ApplyWebFusionTributaryWaterUnionYProfile(
                    centers, cellPath, grid, config, cellSize, mouthAtEnd: lakeAtEnd);
                if (s_webFusionMainWorldCenters != null && s_webFusionMainWorldCenters.Count >= 2)
                {
                    ApplyWebFusionTributaryMainConfluenceYProfile(
                        centers, cellPath, grid, config, cellSize, riverIndex,
                        fromStart: mainJoinIdx == 0);
                }

                return;
            }

            float maxDist = ResolveLakeMouthApproachMaxDistCells(config);

            int first = 0;
            int last = centers.Count - 1;
            bool startNearMain = IsTributaryEndpointNearMain(grid, config, cellSize, cellPath, first);
            bool endNearMain = IsTributaryEndpointNearMain(grid, config, cellSize, cellPath, last);
            bool startLake = !startNearMain && (
                IsLakeEmissaryCenterline(grid, cellPath, riverIndex) ||
                IsCellSpacePointInOrNearLake(grid, cellPath[first], 8) ||
                TryResolveWebFusionLakeMouthBorderIndex(grid, cellPath, mouthAtEnd: false, out _));
            if (startLake)
            {
                ApplyWebFusionTributaryWaterUnionYProfile(
                    centers, cellPath, grid, config, cellSize, mouthAtEnd: false);
            }

            bool endLake = !endNearMain && (
                TryFindPolylineLakeEntryCrossingFromEnd(grid, cellPath, out _, out _, out _) ||
                IsCellSpacePointInOrNearLake(grid, cellPath[last], 8) ||
                (grid.LakeMouthCellsPacked != null && grid.LakeMouthCellsPacked.Count > 0 &&
                 MinChebyshevDistToLakeMouth(cellPath[last], grid) <= maxDist) ||
                RiverFunctionalEndNearLake(grid, riverIndex, config) ||
                TryResolveWebFusionLakeMouthBorderIndex(grid, cellPath, mouthAtEnd: true, out _));
            if (endNearMain && s_webFusionMainWorldCenters != null && s_webFusionMainWorldCenters.Count >= 2)
            {
                ApplyWebFusionTributaryMainConfluenceYProfile(
                    centers, cellPath, grid, config, cellSize, riverIndex, fromStart: false);
            }
            else if (endLake)
            {
                ApplyWebFusionTributaryWaterUnionYProfile(
                    centers, cellPath, grid, config, cellSize, mouthAtEnd: true);
            }
            else if (startNearMain && s_webFusionMainWorldCenters != null && s_webFusionMainWorldCenters.Count >= 2)
            {
                ApplyWebFusionTributaryMainConfluenceYProfile(
                    centers, cellPath, grid, config, cellSize, riverIndex, fromStart: true);
            }
        }

        /// <summary>Re-aplica fade Y de boca tras suavizado monótono (monotonic puede elevar nodos hundidos).</summary>
        public static void ApplyWebFusionTributaryLakeMouthYFadeAfterMonotonic(
            List<Vector3> centers,
            List<Vector2> cellPath,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSize)
        {
            if (riverIndex <= 0 || centers == null || cellPath == null || config == null ||
                centers.Count != cellPath.Count || centers.Count < 2)
                return;

            if (IsLakeEmissaryRiverIndex(grid, riverIndex))
            {
                ApplyWebFusionTributaryWaterUnionYProfile(
                    centers, cellPath, grid, config, cellSize, mouthAtEnd: false);
                if (TryResolveTributaryMainJoinEndpointIndex(
                        grid, config, cellPath, riverIndex, out int emMainIdx) &&
                    s_webFusionMainWorldCenters != null && s_webFusionMainWorldCenters.Count >= 2)
                {
                    ApplyWebFusionTributaryMainConfluenceYProfile(
                        centers, cellPath, grid, config, cellSize, riverIndex,
                        fromStart: emMainIdx == 0);
                }

                return;
            }

            if (TryResolveTributaryMainJoinEndpointIndex(
                    grid, config, cellPath, riverIndex, out int mainJoinIdx))
            {
                bool lakeAtEnd = mainJoinIdx == 0;
                ApplyWebFusionTributaryWaterUnionYProfile(
                    centers, cellPath, grid, config, cellSize, mouthAtEnd: lakeAtEnd);
                if (s_webFusionMainWorldCenters != null && s_webFusionMainWorldCenters.Count >= 2)
                {
                    ApplyWebFusionTributaryMainConfluenceYProfile(
                        centers, cellPath, grid, config, cellSize, riverIndex,
                        fromStart: mainJoinIdx == 0);
                }

                return;
            }

            float maxDist = ResolveLakeMouthApproachMaxDistCells(config);

            int first = 0;
            int last = centers.Count - 1;
            bool startNearMain = IsTributaryEndpointNearMain(grid, config, cellSize, cellPath, first);
            bool endNearMain = IsTributaryEndpointNearMain(grid, config, cellSize, cellPath, last);
            bool startLake = !startNearMain && (
                IsLakeEmissaryCenterline(grid, cellPath, riverIndex) ||
                IsCellSpacePointInOrNearLake(grid, cellPath[first], 8) ||
                TryResolveWebFusionLakeMouthBorderIndex(grid, cellPath, mouthAtEnd: false, out _));
            if (startLake)
            {
                ApplyWebFusionTributaryWaterUnionYProfile(
                    centers, cellPath, grid, config, cellSize, mouthAtEnd: false);
            }

            bool endLake = !endNearMain && (
                TryFindPolylineLakeEntryCrossingFromEnd(grid, cellPath, out _, out _, out _) ||
                IsCellSpacePointInOrNearLake(grid, cellPath[last], 8) ||
                (grid.LakeMouthCellsPacked != null && grid.LakeMouthCellsPacked.Count > 0 &&
                 MinChebyshevDistToLakeMouth(cellPath[last], grid) <= maxDist) ||
                RiverFunctionalEndNearLake(grid, riverIndex, config) ||
                TryResolveWebFusionLakeMouthBorderIndex(grid, cellPath, mouthAtEnd: true, out _));
            if (endNearMain && s_webFusionMainWorldCenters != null && s_webFusionMainWorldCenters.Count >= 2)
            {
                ApplyWebFusionTributaryMainConfluenceYProfile(
                    centers, cellPath, grid, config, cellSize, riverIndex, fromStart: false);
            }
            else if (endLake)
            {
                ApplyWebFusionTributaryWaterUnionYProfile(
                    centers, cellPath, grid, config, cellSize, mouthAtEnd: true);
            }
            else if (startNearMain && s_webFusionMainWorldCenters != null && s_webFusionMainWorldCenters.Count >= 2)
            {
                ApplyWebFusionTributaryMainConfluenceYProfile(
                    centers, cellPath, grid, config, cellSize, riverIndex, fromStart: true);
            }
        }

        static void ExtendRiverSurfaceEndpointTowardLake(
            GridSystem grid,
            List<Vector2> cellProcessed,
            bool extendStart,
            MapGenConfig config)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2)
                return;

            int endpoint = extendStart ? 0 : cellProcessed.Count - 1;
            Vector2 end = cellProcessed[endpoint];
            if (IsCellSpacePointInOrNearLake(grid, end, 4))
                return;

            float maxDist = config != null ? ResolveLakeMouthApproachMaxDistCells(config) : 48f;
            if (!TryGetNearestLakeShorePoint(grid, end, maxDist, out Vector2 shore))
                return;

            float dist = Vector2.Distance(end, shore);
            if (dist < 0.25f || dist > maxDist)
                return;

            int steps = Mathf.Clamp(Mathf.CeilToInt(dist * 1.5f), 2, 10);
            var bridge = new List<Vector2>(steps);
            for (int s = 1; s <= steps; s++)
                bridge.Add(Vector2.Lerp(end, shore, s / (float)steps));

            if (extendStart)
                cellProcessed.InsertRange(0, bridge);
            else
                cellProcessed.AddRange(bridge);

            if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
            {
                Debug.Log(
                    $"[RiverLakeMouthBridge] extendStart={(extendStart ? 1 : 0)} distCells={dist:F2} " +
                    $"inserted={bridge.Count} remaining={cellProcessed.Count}");
            }
        }

        static void ApplyOwnedTributaryLakeEndpointVisualTuck(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null)
                return;
            if (!config.uwpOwnedVisualPolicy || riverIndex <= 0 || !IsTributaryLakeOwner(grid, riverIndex))
                return;

            float maxDist = ResolveLakeMouthApproachMaxDistCells(config);
            Vector2 startShore = default;
            Vector2 endShore = default;
            bool startHasLake = IsCellSpacePointInOrNearOwnedLake(grid, riverIndex, cellProcessed[0], 8);
            bool endHasLake = IsCellSpacePointInOrNearOwnedLake(grid, riverIndex, cellProcessed[cellProcessed.Count - 1], 8);
            if (!startHasLake && !endHasLake)
            {
                startHasLake = TryGetTributaryOwnedLakeShorePoint(grid, riverIndex, cellProcessed[0], maxDist, out startShore);
                endHasLake = TryGetTributaryOwnedLakeShorePoint(
                    grid, riverIndex, cellProcessed[cellProcessed.Count - 1], maxDist, out endShore);
            }

            if (!startHasLake && !endHasLake)
                return;

            bool tuckStart;
            if (startHasLake && endHasLake)
            {
                if (startShore == default)
                    TryGetTributaryOwnedLakeShorePoint(grid, riverIndex, cellProcessed[0], maxDist, out startShore);
                if (endShore == default)
                    TryGetTributaryOwnedLakeShorePoint(
                        grid, riverIndex, cellProcessed[cellProcessed.Count - 1], maxDist, out endShore);
                float startDist = Vector2.Distance(cellProcessed[0], startShore);
                float endDist = Vector2.Distance(cellProcessed[cellProcessed.Count - 1], endShore);
                tuckStart = startDist <= endDist;
            }
            else
            {
                tuckStart = startHasLake;
            }

            int before = cellProcessed.Count;
            ApplyLakeEndpointVisualTuck(grid, cellProcessed, riverIndex, config, tuckStart);

            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[OwnedTributaryLakeTuck] riverIndex={riverIndex} endpoint={(tuckStart ? "start" : "end")} " +
                    $"beforePts={before} afterPts={cellProcessed.Count}");
            }
        }

        static void ApplyLakeRiverMouthVisualBridging(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2)
                return;

            bool emissary = riverIndex > 0 && IsLakeEmissaryCenterline(grid, cellProcessed, riverIndex);
            if (emissary)
                ApplyLakeEndpointVisualTuck(grid, cellProcessed, riverIndex, config, extendStart: true);
            ApplyOwnedTributaryLakeEndpointVisualTuck(grid, cellProcessed, riverIndex, config);

            bool tribToMain = TributaryTargetsMainConfluence(grid, riverIndex);
            bool mainToLake = riverIndex == 0 &&
                grid.HydrologyMainRiverTerminusCell.HasValue &&
                (grid.HydrologyMainRiverPattern == RiverMainPattern.HighlandToLake ||
                 grid.HydrologyMainRiverPattern == RiverMainPattern.BorderToLake);
            if (!tribToMain && (mainToLake || IsCellSpacePointInOrNearLake(grid, cellProcessed[cellProcessed.Count - 1], 6)))
                ApplyLakeEndpointVisualTuck(grid, cellProcessed, riverIndex, config, extendStart: false);
        }

        static bool IsCellSpacePointWater(GridSystem grid, Vector2 p)
        {
            if (grid == null)
                return false;
            int x = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, grid.Width - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, grid.Height - 1);
            return grid.GetCell(x, z).type == CellType.Water;
        }

        static bool IsCellSpacePointInOrNearLake(GridSystem grid, Vector2 p, int radius)
        {
            if (grid?.LakeBodyCellsPacked == null || grid.LakeBodyCellsPacked.Count == 0)
                return false;

            int cx = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, grid.Width - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, grid.Height - 1);
            int r = Mathf.Clamp(radius, 0, 4);
            for (int z = cz - r; z <= cz + r; z++)
            {
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if ((uint)x >= (uint)grid.Width || (uint)z >= (uint)grid.Height)
                        continue;
                    if (grid.LakeBodyCellsPacked.Contains(PackLakeCellLong(x, z)))
                        return true;
                }
            }

            return false;
        }

        static long PackLakeCellLong(int x, int z) => ((long)x << 32) | (uint)z;

        static void SmoothCrossSectionRows(
            List<Vector3> verts,
            int crossSectionCount,
            int passes,
            float strength,
            List<Vector2> cellSpaceLine = null,
            float skipTurnDeg = 0f)
        {
            if (verts == null || crossSectionCount < 3 || verts.Count != crossSectionCount * CrossSectionVertexCount)
                return;

            passes = Mathf.Clamp(passes, 0, 4);
            strength = Mathf.Clamp01(strength);
            if (passes <= 0 || strength <= 1e-4f)
                return;

            var smoothed = new List<Vector3>(verts);
            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 1; i < crossSectionCount - 1; i++)
                {
                    if (cellSpaceLine != null && cellSpaceLine.Count == crossSectionCount &&
                        skipTurnDeg > 1f &&
                        InteriorTurnAngleDeg(cellSpaceLine, i) >= skipTurnDeg)
                    {
                        continue;
                    }

                    for (int q = 0; q < CrossSectionVertexCount; q++)
                    {
                        int idx = i * CrossSectionVertexCount + q;
                        Vector3 avg =
                            (verts[idx - CrossSectionVertexCount] +
                             verts[idx] +
                             verts[idx + CrossSectionVertexCount]) / 3f;
                        Vector3 v = Vector3.Lerp(verts[idx], avg, strength);
                        v.y = verts[idx].y;
                        smoothed[idx] = v;
                    }
                }

                for (int i = 1; i < crossSectionCount - 1; i++)
                {
                    for (int q = 0; q < CrossSectionVertexCount; q++)
                    {
                        int idx = i * CrossSectionVertexCount + q;
                        verts[idx] = smoothed[idx];
                    }
                }
            }
        }

        static Vector2 DirXZTo2(Vector3 d)
        {
            d.y = 0f;
            if (d.sqrMagnitude < 1e-12f)
                return Vector2.right;
            d.Normalize();
            return new Vector2(d.x, d.z);
        }

        static Vector2 NormalLeft2(Vector2 dirNorm)
        {
            return new Vector2(-dirNorm.y, dirNorm.x);
        }

        static void BuildRibbonSidesMiterLimited(
            List<Vector3> center,
            List<float> halfWidth,
            out List<Vector3> left,
            out List<Vector3> right,
            out RiverJoinStats joinStats)
        {
            joinStats = default;
            int n = center != null ? center.Count : 0;
            left = new List<Vector3>(n);
            right = new List<Vector3>(n);
            if (n < 2 || halfWidth == null || halfWidth.Count != n)
                return;

            joinStats.Total = n;
            for (int i = 0; i < n; i++)
            {
                float hw = Mathf.Max(0.02f, halfWidth[i]);
                float y = center[i].y;
                Vector2 c = new Vector2(center[i].x, center[i].z);
                Vector2 miter;
                float scale = hw;

                if (i == 0)
                {
                    Vector2 dir = DirXZTo2(center[1] - center[0]);
                    miter = NormalLeft2(dir);
                    joinStats.Smooth++;
                }
                else if (i == n - 1)
                {
                    Vector2 dir = DirXZTo2(center[n - 1] - center[n - 2]);
                    miter = NormalLeft2(dir);
                    joinStats.Smooth++;
                }
                else
                {
                    Vector2 dirIn = DirXZTo2(center[i] - center[i - 1]);
                    Vector2 dirOut = DirXZTo2(center[i + 1] - center[i]);
                    float ang = Vector2.Angle(dirIn, dirOut);
                    Vector2 nIn = NormalLeft2(dirIn);
                    Vector2 nOut = NormalLeft2(dirOut);
                    miter = nIn + nOut;
                    float miterLen = miter.magnitude;
                    if (miterLen < 1e-6f)
                    {
                        miter = nIn;
                        scale = hw;
                        joinStats.Smooth++;
                    }
                    else
                    {
                        miter /= miterLen;
                        float dot = Vector2.Dot(miter, nIn);
                        scale = hw / Mathf.Max(0.15f, Mathf.Abs(dot));
                        float ratio = scale / Mathf.Max(1e-4f, hw);
                        joinStats.MaxMiterRatio = Mathf.Max(joinStats.MaxMiterRatio, ratio);
                        if (ang < JoinAngleSmoothDeg)
                            joinStats.Smooth++;
                        else if (ang < JoinAngleHardDeg)
                            joinStats.Medium++;
                        else
                            joinStats.Hard++;

                        if (scale > hw * MiterLimitMul)
                        {
                            joinStats.MiterRejected++;
                            scale = hw;
                            miter = nIn + nOut;
                            if (miter.sqrMagnitude < 1e-8f)
                                miter = nIn;
                            else
                                miter.Normalize();
                        }
                    }
                }

                left.Add(new Vector3(c.x - miter.x * scale, y, c.y - miter.y * scale));
                right.Add(new Vector3(c.x + miter.x * scale, y, c.y + miter.y * scale));
            }
        }

        static void LogRiverSurfaceJoinStats(MapGenConfig config, int riverId, RiverJoinStats s)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverSurfaceJoinStats] riverId={riverId} total={s.Total} smooth={s.Smooth} medium={s.Medium} hard={s.Hard} " +
                $"miterRejected={s.MiterRejected} maxMiterRatio={s.MaxMiterRatio:F3}");
        }

        static void DrawRiverSurfaceDebugLines(
            MapGenConfig config,
            List<Vector3> center,
            List<Vector3> left,
            List<Vector3> right)
        {
            if (config == null || center == null || left == null || right == null)
                return;
            bool drawCl = config.riverSurfaceDebugDrawCenterline;
            bool drawEd = config.riverSurfaceDebugDrawEdges;
            bool drawJn = config.riverSurfaceDebugDrawJoinNormals;
            if (!drawCl && !drawEd && !drawJn)
                return;

            const float dur = 45f;
            int n = Mathf.Min(center.Count, Mathf.Min(left.Count, right.Count));
            if (drawCl)
            {
                for (int i = 0; i < n - 1; i++)
                    Debug.DrawLine(center[i] + Vector3.up * 0.05f, center[i + 1] + Vector3.up * 0.05f, new Color(1f, 0.95f, 0.05f, 1f), dur);
                for (int i = 0; i < n; i++)
                    Debug.DrawLine(center[i], center[i] + Vector3.up * 0.2f, new Color(1f, 0.95f, 0.05f, 1f), dur);
            }

            if (drawEd)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    Debug.DrawLine(left[i] + Vector3.up * 0.06f, left[i + 1] + Vector3.up * 0.06f, Color.green, dur);
                    Debug.DrawLine(right[i] + Vector3.up * 0.06f, right[i + 1] + Vector3.up * 0.06f, Color.yellow, dur);
                }
            }

            if (drawJn)
            {
                for (int i = 1; i < n - 1; i++)
                {
                    Vector3 mid = (left[i] + right[i]) * 0.5f;
                    Vector3 nrm = mid - center[i];
                    nrm.y = 0f;
                    if (nrm.sqrMagnitude > 1e-8f)
                        Debug.DrawLine(center[i] + Vector3.up * 0.08f, center[i] + nrm.normalized * 0.6f + Vector3.up * 0.08f, Color.magenta, dur);
                }
            }
        }

        static void BuildWebFusionSplineArrays(
            List<Vector3> worldCenters,
            List<float> halfWidths,
            List<Vector2> cellSpaceLine,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSizeWorld,
            out float[] widths,
            out float[] foam,
            out float[] transparency,
            out float[] distances)
        {
            int n = worldCenters != null ? worldCenters.Count : 0;
            widths = new float[n];
            foam = new float[n];
            transparency = new float[n];
            distances = new float[n];
            if (n < 1)
                return;

            for (int i = 1; i < n; i++)
                distances[i] = distances[i - 1] + Vector3.Distance(worldCenters[i - 1], worldCenters[i]);

            float baseHalf = halfWidths != null && halfWidths.Count > 0 ? halfWidths[0] : 0.5f;
            for (int i = 0; i < n; i++)
            {
                float half = halfWidths != null && i < halfWidths.Count ? halfWidths[i] : baseHalf;
                widths[i] = half * 2f;
            }

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Mathf.Max(1, n - 1);
                foam[i] = Mathf.Lerp(0.35f, 0.62f, Mathf.Sin(t * Mathf.PI));
            }

            bool lakeFadeAtEnd = false;
            bool lakeFadeAtStart = false;
            bool mainAtEnd = false;
            bool mainAtStart = false;
            if (riverIndex > 0)
            {
                ResolveWebFusionTributaryEndpointFadeFlags(
                    grid, config, cellSpaceLine, riverIndex, cellSizeWorld,
                    out lakeFadeAtEnd, out lakeFadeAtStart, out mainAtEnd, out mainAtStart);
            }
            else if (cellSpaceLine != null && cellSpaceLine.Count > 0)
            {
                lakeFadeAtEnd = IsCellSpacePointInOrNearLake(grid, cellSpaceLine[cellSpaceLine.Count - 1], 5) ||
                    IsCellSpacePointWater(grid, cellSpaceLine[cellSpaceLine.Count - 1]) ||
                    IsCellSpacePointInLakeBody(grid, cellSpaceLine[cellSpaceLine.Count - 1]);
                lakeFadeAtStart = IsCellSpacePointInOrNearLake(grid, cellSpaceLine[0], 5) ||
                    IsCellSpacePointWater(grid, cellSpaceLine[0]) ||
                    IsCellSpacePointInLakeBody(grid, cellSpaceLine[0]);
            }

            int mouthBlend = riverIndex > 0
                ? ResolveWebFusionTributaryMouthBlendVerts(n, config)
                : Mathf.Clamp(
                    Mathf.RoundToInt(config != null ? config.lakeRiverMouthBlendCells + 3 : 6),
                    4,
                    Mathf.Max(4, n / 4));
            float minAlpha = Mathf.Clamp01(config != null ? config.riverLakeEmissaryLakeEndpointMinAlpha : 0.08f);
            minAlpha = Mathf.Max(minAlpha, 0.06f);

            for (int i = 0; i < n; i++)
            {
                transparency[i] = ComputeWebFusionEndpointAlpha(
                    i,
                    n,
                    lakeFadeAtEnd,
                    lakeFadeAtStart,
                    mainAtEnd,
                    mainAtStart,
                    minAlpha,
                    mouthBlend,
                    config);
            }

            if (grid != null && config != null && cellSpaceLine != null && cellSpaceLine.Count == n)
            {
                int mouthBlendCells = riverIndex > 0 ? mouthBlend : Mathf.Clamp(config.lakeRiverMouthBlendCells + 3, 4, 14);
                for (int i = 0; i < n; i++)
                {
                    bool inMouth = (lakeFadeAtStart && i < mouthBlendCells) ||
                        (lakeFadeAtEnd && i >= n - mouthBlendCells);
                    if (!inMouth)
                        continue;

                    int gx = Mathf.FloorToInt(cellSpaceLine[i].x);
                    int gz = Mathf.FloorToInt(cellSpaceLine[i].y);
                    if (!grid.InBoundsCell(gx, gz))
                        continue;

                    Vector3 world = grid.CellToWorldCenter(gx, gz);
                    float mouth = WaterMeshBuilder.SampleLakeMouthProximity01(world, grid, config);
                    foam[i] = Mathf.Lerp(foam[i], 0.85f, mouth);
                }
            }

            if (riverIndex > 0 && cellSpaceLine != null &&
                TryResolveTributaryMainJoinEndpointIndex(grid, config, cellSpaceLine, riverIndex, out int mainIdx))
            {
                int confBlend = Mathf.Clamp(
                    config != null && config.riverSurfaceTributaryWidthFixEnabled
                        ? config.riverSurfaceTributaryConfluenceApproachCells
                        : (config != null ? config.riverConfluenceVisualBlendLengthCells : 6),
                    3,
                    14);
                for (int i = 0; i < n; i++)
                {
                    int distJoin = mainIdx == 0 ? i : (n - 1 - i);
                    if (distJoin < 0 || distJoin >= confBlend)
                        continue;
                    float confFoam = Mathf.SmoothStep(0f, 1f, 1f - distJoin / Mathf.Max(1f, confBlend - 1f));
                    foam[i] = Mathf.Lerp(foam[i], 0.72f, confFoam * 0.65f);
                }
            }
        }

        static void AttachWebFusionCenterSpline(
            Transform meshRoot,
            List<Vector3> worldCenters,
            List<float> halfWidths,
            List<Vector2> cellSpaceLine,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSizeWorld)
        {
            if (meshRoot == null || worldCenters == null || worldCenters.Count < 2 || config == null)
                return;
            if (!WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                return;

            BuildWebFusionSplineArrays(
                worldCenters,
                halfWidths,
                cellSpaceLine,
                grid,
                config,
                riverIndex,
                cellSizeWorld,
                out float[] widths,
                out float[] foam,
                out float[] transparency,
                out float[] distances);

            var go = new GameObject("Center Spline");
            go.transform.SetParent(meshRoot, false);

            int n = worldCenters.Count;
            var localPoints = new Vector3[n];
            for (int i = 0; i < n; i++)
                localPoints[i] = meshRoot.InverseTransformPoint(worldCenters[i]);

            var spline = go.AddComponent<WebMapRiverSpline>();
            spline.showCenterLine = config.riverSurfaceDebugDrawCenterline;
            var meshFilter = meshRoot.GetComponent<MeshFilter>();
            var meshRenderer = meshRoot.GetComponent<MeshRenderer>();
            spline.BindSurfaceMesh(
                meshFilter,
                meshRenderer,
                config,
                riverIndex,
                config.riverSurfaceMeshUvScale,
                cellSizeWorld,
                grid != null ? grid.Width : 0,
                grid != null ? grid.Height : 0,
                grid != null ? grid.Origin : Vector3.zero,
                cellSpaceLine);
            spline.SetData(localPoints, widths, foam, transparency, distances, rebuildSurfaceMesh: false);

            s_debugCenterlineNodesWorld.AddRange(worldCenters);
            WaterMeshBuilder.DebugRibbonPathPointsWorld.AddRange(worldCenters);
        }

        /// <summary>Reconstruye el mesh del río desde el centerline del spline (vértices Y/ancho autoritativos).</summary>
        public static bool TryRebuildRiverSurfaceMeshFromCenterline(
            MeshFilter meshFilter,
            MeshRenderer meshRenderer,
            List<Vector3> center,
            List<float> halfWidthWorld,
            List<Vector2> cellSpaceLine,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            float cellSizeWorld,
            float uvScale,
            int gridWCells,
            int gridHCells,
            Vector3 mapOrigin,
            bool splineControlsCenterY = false)
        {
            if (meshFilter == null || config == null || center == null || center.Count < 2 ||
                halfWidthWorld == null || halfWidthWorld.Count != center.Count)
                return false;

            var meshCenter = new List<Vector3>(center);
            var meshHalf = new List<float>(halfWidthWorld);
            List<Vector2> meshCell = cellSpaceLine != null ? new List<Vector2>(cellSpaceLine) : null;

            bool mouthTaperStart = false;
            bool mouthTaperEnd = false;
            if (meshCell != null &&
                TryApplyMouthFusionLakeMouthMeshTrim(
                    grid, config, riverIndex, meshCenter, meshHalf, meshCell,
                    out mouthTaperStart, out mouthTaperEnd))
            {
                bool skipLakeFirstTribTaper = config != null && UsesLakeFirstMainJoinMeshTreatment(grid, config, riverIndex);
                if (mouthTaperStart && !skipLakeFirstTribTaper)
                    ApplyMouthFusionShoreWidthTaper(meshHalf, atStart: true);
                if (mouthTaperEnd && !skipLakeFirstTribTaper)
                    ApplyMouthFusionShoreWidthTaper(meshHalf, atStart: false);
            }

            int n = meshCenter.Count;
            Material mat = meshRenderer != null ? meshRenderer.sharedMaterial : null;
            float borderMul = Mathf.Clamp(config.riverSurfaceBorderEndpointWidthMul, 1.5f, 3f);
            float baseHalfForBorder = config.riverVisualRibbonFullWidthCellsMain > 0.01f
                ? config.riverVisualRibbonFullWidthCellsMain * 0.5f * cellSizeWorld
                : (meshHalf.Count > 0 ? meshHalf[0] / borderMul : 0.5f);

            bool cellHintOk = meshCell != null && meshCell.Count == n;
            bool startAtBorder = cellHintOk && IsTrueMapEdgeCellSpace(meshCell[0], gridWCells, gridHCells);
            bool endAtBorder = cellHintOk && IsTrueMapEdgeCellSpace(meshCell[n - 1], gridWCells, gridHCells);
            bool skipEndBlend = riverIndex > 0;
            bool skipAllEndpointTaper = riverIndex > 0 &&
                WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config);

            bool mouthFusionTrimmed = mouthTaperStart || mouthTaperEnd;
            if (!splineControlsCenterY &&
                skipAllEndpointTaper && meshCell != null && meshCell.Count == n && riverIndex > 0)
            {
                ApplyWebFusionTributaryEndpointSubmergeY(
                    meshCenter, meshCell, grid, config, riverIndex, cellSizeWorld, 0f);
            }

            s_mouthFusionMeshHints = new MouthFusionMeshBuildHints
            {
                Active = mouthFusionTrimmed,
                TaperAtStart = mouthTaperStart,
                TaperAtEnd = mouthTaperEnd
            };

            BuildCrossSectionRiverMesh(
                meshCenter,
                meshHalf,
                meshCell,
                grid,
                config,
                riverIndex,
                cellSizeWorld,
                uvScale,
                baseHalfForBorder,
                startAtBorder,
                endAtBorder,
                skipEndBlend,
                skipAllEndpointTaper,
                out List<Vector3> verts,
                out List<Vector2> uvs,
                out List<Vector2> uvs2,
                out List<Vector3> normals,
                out List<Vector4> tangents,
                out List<int> tris,
                out _,
                out _);

            s_mouthFusionMeshHints = default;

            int smoothPasses = config.riverSurfaceEdgeSmoothPasses;
            float edgeSmoothStr = config.riverSurfaceEdgeSmoothStrength;
            if (riverIndex == 0)
            {
                edgeSmoothStr *= 0.32f;
                smoothPasses = Mathf.Min(smoothPasses, 1);
            }

            SmoothCrossSectionRows(
                verts,
                n,
                smoothPasses,
                edgeSmoothStr,
                meshCell,
                JoinAngleHardDeg - 6f);

            if (verts.Count < CrossSectionVertexCount * 2 || tris.Count < 6)
                return false;

            FinalClampVertexListToPlayableBounds(
                verts,
                mapOrigin,
                gridWCells,
                gridHCells,
                cellSizeWorld,
                out _,
                out _,
                out _,
                out _,
                out _);

            Mesh mesh = meshFilter.sharedMesh;
            if (mesh == null)
            {
                mesh = new Mesh { name = meshFilter.gameObject.name + "_Surface" };
                meshFilter.sharedMesh = mesh;
            }

            if (verts.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, uvs2);
            var colors = new List<Color>(verts.Count);
            FillRiverSurfaceVertexColors(colors, mat, cellSpaceLine, n, CrossSectionVertexCount, grid, config, riverIndex);
            mesh.SetColors(colors);
            mesh.RecalculateBounds();
            if (mat != null)
                WaterStylizedIntegration.PrepareMesh(mesh, mat);
            return true;
        }

        static void AttachRiverCenterlineDebugOverlay(
            Transform parent,
            int riverIndex,
            List<Vector3> worldCenters,
            MapGenConfig config)
        {
            if (config == null || !config.riverSurfaceDebugDrawCenterline ||
                parent == null || worldCenters == null || worldCenters.Count < 2)
                return;

            s_debugCenterlineNodesWorld.AddRange(worldCenters);
            WaterMeshBuilder.DebugRibbonPathPointsWorld.AddRange(worldCenters);

            var go = new GameObject(riverIndex == 0
                ? "RiverDebugCenterline_Main"
                : $"RiverDebugCenterline_Trib_{riverIndex}");
            go.transform.SetParent(parent, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = worldCenters.Count;
            lr.SetPositions(worldCenters.ToArray());
            lr.widthMultiplier = 0.14f;
            lr.numCornerVertices = 4;
            lr.numCapVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            Shader lineShader = Shader.Find("Sprites/Default") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color");
            if (lineShader != null)
            {
                var lineMat = new Material(lineShader) { name = "RiverDebugCenterlineMat" };
                var yellow = new Color(1f, 0.95f, 0.05f, 1f);
                if (lineMat.HasProperty("_Color"))
                    lineMat.SetColor("_Color", yellow);
                if (lineMat.HasProperty("_BaseColor"))
                    lineMat.SetColor("_BaseColor", yellow);
                lr.sharedMaterial = lineMat;
                lr.startColor = yellow;
                lr.endColor = yellow;
            }
        }

        static int MinChebyshevDistToMapEdge(Vector2 p, int w, int h)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, w - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, h - 1);
            return Mathf.Min(Mathf.Min(cx, w - 1 - cx), Mathf.Min(cy, h - 1 - cy));
        }

        static void LogRiverSurfaceEndpoint(
            MapGenConfig config,
            int riverId,
            List<Vector2> cellLine,
            int w,
            int h,
            bool startAtBorder,
            bool endAtBorder,
            bool startCap,
            bool endCap,
            float baseHalfW,
            List<float> halfWidths)
        {
            if (config == null || cellLine == null || cellLine.Count < 1 ||
                (!config.debugLogs && !config.debugHydrologyNetwork))
                return;

            int startDist = MinChebyshevDistToMapEdge(cellLine[0], w, h);
            int endDist = MinChebyshevDistToMapEdge(cellLine[cellLine.Count - 1], w, h);
            float startWMul = 1f;
            float endWMul = 1f;
            if (halfWidths != null && halfWidths.Count > 0 && baseHalfW > 1e-5f)
            {
                startWMul = halfWidths[0] / baseHalfW;
                endWMul = halfWidths[halfWidths.Count - 1] / baseHalfW;
            }

            string startMode = startAtBorder ? "BorderFlatCut" : (startCap ? "InteriorTaper" : "ConfluenceBlend");
            string endMode = endAtBorder ? "BorderFlatCut" : (endCap ? "InteriorTaper" : "ConfluenceBlend");
            int warnInterior = config.lakeCount <= 0 && (!startAtBorder || !endAtBorder) ? 1 : 0;
            float minMul = Mathf.Clamp(config.riverSurfaceInteriorEndpointMinWidthMul, 1f, 1.25f);
            Debug.Log(
                $"[RiverSurfaceEndpoint] riverId={riverId} startMode={startMode} endMode={endMode} " +
                $"startAtBorder={(startAtBorder ? 1 : 0)} endAtBorder={(endAtBorder ? 1 : 0)} startDistBorder={startDist} endDistBorder={endDist} " +
                $"startWidthMul={startWMul:F3} endWidthMul={endWMul:F3} flatCut={(startAtBorder || endAtBorder ? 1 : 0)} " +
                $"taperApplied={((!startAtBorder && startCap) || (!endAtBorder && endCap) ? 1 : 0)} " +
                $"warningInteriorEndpointNoLake={warnInterior} endpointMinWidthMul={minMul:F2} " +
                "createdRoundCap=0 createdLargeBevel=0");
        }

        public static bool BuildRiverSurfaces(
            Transform parent,
            GridSystem grid,
            MapGenConfig config,
            Material waterMaterial,
            float riverSurfaceWorldY,
            float cellSize,
            int waterLayer)
        {
            ResetStats();
            if (parent == null || grid == null || config == null)
                return false;
            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return false;

            LogRiverGeometryHistoryAudit(config);

            Material mainMat = GetRiverSurfaceMaterial(config, waterMaterial, 0);
            if (mainMat == null)
                return false;

            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[RiverSurfaceDebug] flatMaterial={(config.riverSurfaceDebugFlatMaterial ? 1 : 0)} wire={(config.riverSurfaceDebugShowWire ? 1 : 0)} " +
                    $"shaderName={(mainMat.shader != null ? mainMat.shader.name : "null")} materialFallback={(mainMat == waterMaterial ? 1 : 0)} yOffset={riverSurfaceWorldY:F3}");
            }

            Vector3 origin = grid.Origin;
            float inset = Mathf.Max(0f, config.riverVisualBankInset);
            int w = grid.Width;
            int h = grid.Height;
            bool any = false;
            bool logRm = config.debugLogs || config.debugHydrologyNetwork;

            for (int riverIndex = 0; riverIndex < grid.RiverCenterlinesCellSpace.Count; riverIndex++)
            {
                Material mat = mainMat;
                if (riverIndex > 0)
                {
                    Material riverMat = GetRiverSurfaceMaterial(config, waterMaterial, riverIndex);
                    mat = riverMat != null ? riverMat : mainMat;
                }

                var rawPath = grid.RiverCenterlinesCellSpace[riverIndex];
                if (rawPath == null || rawPath.Count < 2)
                    continue;

                LogRiverSurfaceSource(grid, config, riverIndex, rawPath);

                bool standardTrib = IsStandardDendriticTributary(grid, riverIndex);
                bool lakeEmissary = IsLakeEmissaryRiverIndex(grid, riverIndex);
                List<Vector2> cellProcessed = null;
                List<float> halfWidths = null;
                List<Vector3> worldCenters = null;
                int fordNearBuild = 0;
                RiverCenterlinePrepStats prepStats = default;
                float hwMin = 0f;
                float hwMax = 0f;
                float baseHalfW = 0f;
                bool usedVisualCache = false;
                if (config.uwpOwnedVisualPolicy && grid.RiverVisualSurfaceCacheFrozen)
                {
                    if (grid.RiverVisualSurfaces != null &&
                        riverIndex < grid.RiverVisualSurfaces.Count &&
                        grid.RiverVisualSurfaces[riverIndex].Skipped)
                        continue;

                    usedVisualCache = TryUseUwpCachedTributaryVisualForMesh(
                        grid, config, riverIndex, origin, cellSize, riverSurfaceWorldY,
                        out cellProcessed, out halfWidths, out worldCenters);
                    if (!usedVisualCache)
                    {
                        Debug.LogError(
                            $"[UWP] Cache congelada pero mesh prep falló riverIndex={riverIndex} seed={config.seed}");
                        continue;
                    }
                }

                if (!usedVisualCache)
                {
                if (standardTrib)
                {
                    if (!TryPrepareStandardTributaryCenterline(
                            grid, config, riverIndex, rawPath, logRm, out cellProcessed, out prepStats, out _))
                        continue;

                    if (!ApplyStandardTributaryLakeMouthFinalJoin(
                            grid, ref cellProcessed, riverIndex, config, cellSize))
                        continue;
                }
                else if (lakeEmissary)
                {
                    if (!TryPrepareLakeEmissaryCenterline(
                            grid, config, riverIndex, rawPath, logRm, out cellProcessed, out prepStats))
                        continue;
                }
                else
                {
                    cellProcessed = BuildVisualCenterlineFromLogical(
                        grid,
                        rawPath,
                        config,
                        riverIndex,
                        out fordNearBuild,
                        out prepStats);
                    if (cellProcessed == null || cellProcessed.Count < 2)
                        continue;

                    if (riverIndex > 0 && !IsLakeEmissaryRiverIndex(grid, riverIndex))
                        TrimRiverSurfaceStartAtLakeMouth(grid, cellProcessed, config);

                    if (cellProcessed == null || cellProcessed.Count < 2)
                        continue;

                    if (riverIndex > 0 && !IsLakeEmissaryCenterline(grid, cellProcessed, riverIndex) &&
                        !TributaryTargetsMainConfluence(grid, riverIndex))
                        TrimRiverSurfaceExcludingLakeInterior(grid, cellProcessed, riverIndex, config);

                    if (cellProcessed == null || cellProcessed.Count < 2)
                        continue;

                    ApplyLakeRiverMouthVisualBridging(grid, cellProcessed, riverIndex, config);

                    if (cellProcessed == null || cellProcessed.Count < 2)
                        continue;

                    ApplySplitModeConfluenceAndLakeEndpoints(grid, cellProcessed, riverIndex, config, cellSize);

                    if (cellProcessed == null || cellProcessed.Count < 2)
                        continue;

                    TrimRiverSurfaceEndAtLakeMouth(grid, cellProcessed, config);

                    if (cellProcessed == null || cellProcessed.Count < 2)
                        continue;

                    TrimRiverSurfaceStaticWaterFromEnds(grid, cellProcessed, riverIndex, config);

                    if (cellProcessed == null || cellProcessed.Count < 2)
                        continue;

                    ApplyWebFusionLakeMouthAfterTrim(grid, cellProcessed, riverIndex, config);

                    if (cellProcessed == null || cellProcessed.Count < 2)
                        continue;

                    if (riverIndex == 0)
                        cellProcessed = FallbackMainRiverCenterlineIfInvalid(grid, rawPath, config, cellProcessed);

                    if (cellProcessed == null || cellProcessed.Count < 2)
                        continue;
                }

                if ((standardTrib || lakeEmissary) &&
                    WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                {
                    ApplyWebFusionLakeMouthAfterTrim(grid, cellProcessed, riverIndex, config);
                    if (cellProcessed == null || cellProcessed.Count < 2)
                        continue;

                    if (standardTrib)
                        ApplyTributaryMainConfluenceCenterlineTrim(grid, cellProcessed, riverIndex, config, cellSize);
                    if (cellProcessed == null || cellProcessed.Count < 2)
                        continue;
                }

                bool webFusionStabilize = IsSplitLakeMsRiverWebFusionStabilizeMode(config);
                if (!webFusionStabilize)
                {
                    ApplySplitLakeMouthStabilizationTrims(
                        grid, cellProcessed, riverIndex, config, standardTrib, lakeEmissary);
                    if (cellProcessed == null || cellProcessed.Count < 2)
                        continue;
                }

                if (cellProcessed == null || cellProcessed.Count < 2)
                    continue;

                if (riverIndex > 0 && TryCullTributarySurfacePiece(grid, cellProcessed, riverIndex, config, logRm))
                    continue;

                if ((!standardTrib && !lakeEmissary) ||
                    WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                    cellProcessed = NormalizeCenterlineSpacingForMesh(cellProcessed, config);

                if (cellProcessed == null || cellProcessed.Count < 2)
                    continue;

                if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config) && riverIndex > 0)
                    ApplyWebFusionTributaryLakeMouthFinalize(grid, cellProcessed, riverIndex, config);

                if (cellProcessed == null || cellProcessed.Count < 2)
                    continue;

                if (webFusionStabilize)
                {
                    ApplySplitLakeMouthStabilizationTrims(
                        grid, cellProcessed, riverIndex, config, standardTrib, lakeEmissary);
                    if (cellProcessed == null || cellProcessed.Count < 2)
                        continue;
                }

                LogRiverSurfaceAlignment(grid, config, riverIndex, cellProcessed);

                float fullCellsW = riverIndex == 0
                    ? config.riverVisualRibbonFullWidthCellsMain
                    : (config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                        ? config.riverVisualRibbonFullWidthCellsTributary
                        : config.riverVisualRibbonFullWidthCellsMain);
                baseHalfW = fullCellsW > 0.01f
                    ? Mathf.Max(0.08f, fullCellsW * 0.5f * cellSize - inset)
                    : Mathf.Max(0.08f, config.riverVisualMeshHalfWidth - inset);

                worldCenters = WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config)
                    ? CellPolylineToWorldRiverSurface(grid, config, cellProcessed, origin, cellSize, riverSurfaceWorldY, riverIndex)
                    : CellPolylineToWorldXZ(cellProcessed, origin, cellSize, riverSurfaceWorldY);
                if (worldCenters.Count < 2)
                    continue;

                if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                {
                    if (riverIndex > 0 && config.uwpOwnedVisualPolicy)
                        ApplyUwpTributaryTerrainCarveSnapY(
                            worldCenters, cellProcessed, grid, config, riverIndex, cellSize);
                    ApplyWebFusionMonotonicSurfaceY(worldCenters, cellSize);
                    if (riverIndex > 0 && config.uwpOwnedVisualPolicy)
                        ApplyUwpTributaryChannelUniformSurfaceY(
                            worldCenters, cellProcessed, grid, config, riverIndex, cellSize);
                    if (riverIndex == 0)
                    {
                        s_webFusionMainWorldCenters.Clear();
                        s_webFusionMainWorldCenters.AddRange(worldCenters);
                    }
                }

                if (riverIndex > 0 || WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                    ApplyTributaryEndpointSubmergeDepth(worldCenters, cellProcessed, grid, config, riverIndex, cellSize, riverSurfaceWorldY);

                if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config) && riverIndex > 0)
                    ApplyWebFusionTributaryLakeMouthYFadeAfterMonotonic(
                        worldCenters, cellProcessed, grid, config, riverIndex, cellSize);

                if (riverIndex > 0 && config.uwpLakeFirstHydrologyPipeline &&
                    UsesSupplementalFeederSourceEmergence(grid, riverIndex))
                {
                    ApplyLakeFirstInlandFeederSourceEmergenceY(
                        worldCenters, cellProcessed, grid, config, riverIndex, cellSize, riverSurfaceWorldY);
                }

                if (riverIndex == 0)
                {
                    var joinCells = BuildJoinProximityCellKeys(grid.RiverCenterlinesCellSpace, 0, w, h);
                    float amp = Mathf.Max(0f, config.riverSurfaceWidthNoiseAmpMain);
                    float noiseScale = Mathf.Max(0.0001f, config.riverSurfaceWidthNoiseScale);
                    halfWidths = BuildMainRiverHalfWidthsWithArcVariation(
                        grid,
                        worldCenters,
                        cellProcessed,
                        baseHalfW,
                        amp,
                        noiseScale,
                        joinCells,
                        config,
                        out hwMin,
                        out hwMax);
                }
                else
                {
                    halfWidths = BuildOrganicHalfWidths(
                        cellProcessed,
                        baseHalfW,
                        grid,
                        config,
                        riverIndex,
                        out hwMin,
                        out hwMax,
                        out _,
                        out _);
                }
                if (halfWidths.Count != worldCenters.Count)
                    continue;

                ApplyFordWidthDampening(grid, cellProcessed, halfWidths, config);

                if (riverIndex > 0 &&
                    (config.riverSurfaceTributaryWidthFixEnabled || config.uwpOwnedVisualPolicy))
                {
                    ApplyTributaryConfluenceVisualHalfWidths(
                        grid, cellProcessed, halfWidths, config, riverIndex);
                    if (config.uwpOwnedVisualPolicy)
                        ApplyTributaryLakeMouthVisualHalfWidths(
                            grid, cellProcessed, halfWidths, config, riverIndex);
                }

                float avgBeforeConfluence = 0f;
                for (int wi = 0; wi < halfWidths.Count; wi++)
                    avgBeforeConfluence += halfWidths[wi];
                if (halfWidths.Count > 0)
                    avgBeforeConfluence /= halfWidths.Count;

                if (riverIndex == 0 || config.uwpOwnedVisualPolicy)
                    ApplyLakeMouthVisualHalfWidthFlare(grid, cellProcessed, halfWidths, config);

                if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                {
                    if (riverIndex > 0)
                    {
                        if (!WaterVisualPipelinePolicy.IsSplitLakeMsRiverMouthFusion(config))
                            ApplyWebFusionTributaryEndpointConstantWidth(halfWidths, riverIndex);
                    }
                    else
                    {
                        ApplyWebFusionLakeMouthWidthTaper(grid, cellProcessed, halfWidths, config, riverIndex, cellSize, atStart: true);
                        ApplyWebFusionLakeMouthWidthTaper(grid, cellProcessed, halfWidths, config, riverIndex, cellSize, atStart: false);
                    }
                }

                if (riverIndex == 0 || config.debugRiverVisualStats)
                    CollectRiverNarrowSites(riverIndex, cellProcessed, halfWidths, grid, config, cellSize);

                if (riverIndex == 0 && config.uwpOwnedVisualPolicy)
                    ApplyUwpMainRiverShoreIntersectionRepair(halfWidths, cellProcessed, baseHalfW, config);

                ApplyMainMeshOnlyWidthScale(halfWidths, config, riverIndex, grid);

                if (riverIndex > 0 && config.uwpOwnedVisualPolicy)
                    ApplyTributaryConfluenceExtraMeshWidth(
                        grid, cellProcessed, halfWidths, config, riverIndex, cellSize);

                if (config.uwpLakeFirstHydrologyPipeline &&
                    UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex))
                {
                    ApplyLakeFirstInlandFeederSourceWidthTaper(
                        halfWidths, cellProcessed, grid, config, riverIndex, cellSize);
                }

                if (riverIndex > 0 && config.riverSurfaceTributaryWidthFixEnabled)
                {
                    float avgAfter = 0f;
                    for (int wi = 0; wi < halfWidths.Count; wi++)
                        avgAfter += halfWidths[wi];
                    if (halfWidths.Count > 0)
                        avgAfter /= halfWidths.Count;
                    if (config.riverSurfaceTributaryWidthDebugLogs || config.debugLogs || config.debugHydrologyNetwork)
                    {
                        Debug.Log(
                            $"[RiverTributaryWidthScale] riverId={riverIndex} parent=0 oldAvgHalfWidth={avgBeforeConfluence:F3} " +
                            $"newAvgHalfWidth={avgAfter:F3} mul={config.riverSurfaceTributaryVisualWidthMul:F2} " +
                            $"minHalf={hwMin:F3} maxHalf={hwMax:F3} points={halfWidths.Count} nearConfluenceBlend=1 ok=1");
                    }
                }
                }

                if (cellProcessed == null || cellProcessed.Count < 2)
                    continue;

                if (halfWidths == null || worldCenters == null || halfWidths.Count != cellProcessed.Count)
                    continue;

                // UWP frozen cache: Inland taper+Y; Headwater Y + piso mesh (carve ya continuo en cache).
                if (usedVisualCache && config.uwpLakeFirstHydrologyPipeline)
                {
                    if (UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex))
                    {
                        ApplyLakeFirstInlandFeederSourceWidthTaper(
                            halfWidths, cellProcessed, grid, config, riverIndex, cellSize);
                        ApplyLakeFirstInlandFeederSourceEmergenceY(
                            worldCenters, cellProcessed, grid, config, riverIndex, cellSize, riverSurfaceWorldY);
                        LogLakeFirstSupplementalMeshHook(grid, config, riverIndex, "BuildRiverSurfaces.CachePath.Inland");
                    }
                    else if (IsLakeFirstHeadwaterFeeder(grid, riverIndex))
                    {
                        ApplyLakeFirstHeadwaterMeshContinuityFloor(
                            halfWidths, cellSize, grid, riverIndex);
                        ApplyLakeFirstInlandFeederSourceEmergenceY(
                            worldCenters, cellProcessed, grid, config, riverIndex, cellSize, riverSurfaceWorldY);
                        LogLakeFirstSupplementalMeshHook(grid, config, riverIndex, "BuildRiverSurfaces.CachePath.Headwater");
                    }
                }

                if (baseHalfW < 1e-5f)
                {
                    float fullCellsWLog = riverIndex == 0
                        ? config.riverVisualRibbonFullWidthCellsMain
                        : (config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                            ? config.riverVisualRibbonFullWidthCellsTributary
                            : config.riverVisualRibbonFullWidthCellsMain);
                    baseHalfW = fullCellsWLog > 0.01f
                        ? Mathf.Max(0.08f, fullCellsWLog * 0.5f * cellSize - inset)
                        : Mathf.Max(0.08f, config.riverVisualMeshHalfWidth - inset);
                }

                bool startAtBorder = IsTrueMapEdgeCellSpace(cellProcessed[0], w, h);
                bool endAtBorder = IsTrueMapEdgeCellSpace(cellProcessed[cellProcessed.Count - 1], w, h);
                if (logRm)
                {
                    var src = new Vector2Int(Mathf.RoundToInt(cellProcessed[0].x), Mathf.RoundToInt(cellProcessed[0].y));
                    var dst = new Vector2Int(
                        Mathf.RoundToInt(cellProcessed[cellProcessed.Count - 1].x),
                        Mathf.RoundToInt(cellProcessed[cellProcessed.Count - 1].y));
                    RiverRouteGenerator.LogRiverBorderPolicy(
                        config,
                        startAtBorder ? RiverAnchorKind.BorderExit : RiverAnchorKind.HighlandSpring,
                        endAtBorder ? RiverAnchorKind.BorderExit : RiverAnchorKind.LakeSink,
                        src,
                        dst,
                        w,
                        h,
                        Mathf.Clamp(config.riverMainBorderExitInsetCells, 0, 48),
                        Mathf.Max(0, config.riverMainMaxBorderPathExtensionCells),
                        config.riverMainBorderExitInsetCells == 0 && config.riverMainMaxBorderPathExtensionCells == 0 ? 1 : 0,
                        meshReachesBorder: (startAtBorder || endAtBorder) ? 1 : 0,
                        terrainCarveReachesBorder: -1);
                }
                bool endCap = !endAtBorder && !(riverIndex > 0 && config.riverSurfaceSkipTributaryConfluenceCap);
                LogRiverSurfaceEndpoint(
                    config,
                    riverIndex,
                    cellProcessed,
                    w,
                    h,
                    startAtBorder,
                    endAtBorder,
                    startCap: !startAtBorder,
                    endCap: endCap,
                    baseHalfW,
                    halfWidths);

                if (logRm)
                {
                    bool debugWire = config.riverSurfaceDebugShowWire ||
                        config.riverSurfaceDebugDrawCenterline ||
                        config.riverSurfaceDebugDrawEdges;
                    Debug.Log(
                        $"[RiverSurfaceMaterial] materialName={(mat != null ? mat.name : "null")} " +
                        $"shaderName={(mat != null && mat.shader != null ? mat.shader.name : "null")} " +
                        $"isInstance={(mat != null && mat.name.Contains("Instance") ? 1 : 0)} debugWire={(debugWire ? 1 : 0)}");
                }

                ApplyRiverSurfaceFlowMaterial(mat, cellProcessed, grid, config, riverIndex);

                if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverMouthFusion(config) &&
                    riverIndex > 0 &&
                    worldCenters.Count == cellProcessed.Count &&
                    halfWidths.Count == cellProcessed.Count &&
                    TryApplyMouthFusionLakeMouthMeshTrim(
                        grid, config, riverIndex, worldCenters, halfWidths, cellProcessed,
                        out bool preTaperStart, out bool preTaperEnd) &&
                    (config.debugLogs || config.debugHydrologyNetwork))
                {
                    Debug.Log(
                        $"[MouthFusionPreTrim] riverIndex={riverIndex} pts={cellProcessed.Count} " +
                        $"taperStart={(preTaperStart ? 1 : 0)} taperEnd={(preTaperEnd ? 1 : 0)}");
                }

                string goName = ResolveRiverSurfaceGameObjectName(grid, riverIndex);
                if (TryBuildStripMeshWithCaps(
                        parent,
                        worldCenters,
                        halfWidths,
                        mat,
                        waterLayer,
                        goName,
                        config.riverSurfaceMeshUvScale,
                        cellSize,
                        config,
                        riverIndex,
                        w,
                        h,
                        cellProcessed,
                        grid,
                        origin,
                        centerlinePreClipped: true,
                        out int verts,
                        out int tris,
                        out float maxSegBuilt))
                {
                    any = true;
                    LastMeshCount++;
                    MarkUwpRiverMeshBuilt(grid, riverIndex, cellProcessed);
                    if (!WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                        AttachRiverCenterlineDebugOverlay(parent, riverIndex, worldCenters, config);
                    if (logRm)
                    {
                        Debug.Log(
                            $"[RiverSurfaceMesh] riverIndex={riverIndex} centerlineInput={rawPath.Count} centerlineUsed={cellProcessed.Count} " +
                            $"verts={verts} tris={tris} maxSegmentLength={maxSegBuilt:F3} fordNear={fordNearBuild} source=RiverCenterlinesCellSpace");
                    }
                }
            }

            return any;
        }

        static void ApplyRiverSurfaceFlowMaterial(
            Material mat,
            List<Vector2> cellProcessed,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex)
        {
            if (mat == null || cellProcessed == null || cellProcessed.Count < 2 || grid == null)
                return;

            Vector2 dir = Vector2.zero;
            if (grid.WaterFlowXZ != null)
            {
                int samples = 0;
                for (int i = 0; i < cellProcessed.Count; i++)
                {
                    int gx = Mathf.Clamp(Mathf.RoundToInt(cellProcessed[i].x), 0, grid.Width - 1);
                    int gz = Mathf.Clamp(Mathf.RoundToInt(cellProcessed[i].y), 0, grid.Height - 1);
                    Vector2 f = grid.WaterFlowXZ[gx, gz];
                    if (f.sqrMagnitude < 1e-5f)
                        continue;
                    dir += f.normalized;
                    samples++;
                }

                if (samples > 0)
                    dir /= samples;
            }

            if (dir.sqrMagnitude < 1e-4f)
                dir = cellProcessed[cellProcessed.Count - 1] - cellProcessed[0];

            float widthRatio = 1f;
            if (grid.RiverWidthRatioToMain != null && riverIndex >= 0 && riverIndex < grid.RiverWidthRatioToMain.Count)
                widthRatio = Mathf.Clamp(grid.RiverWidthRatioToMain[riverIndex], 0.2f, 1.25f);

            float baseSpeed = config != null ? Mathf.Clamp(config.waterUvFlowSpeedScale, 0.25f, 3f) : 1f;
            float speed = baseSpeed * Mathf.Lerp(0.58f, 1.12f, Mathf.Clamp01(widthRatio));
            WaterStylizedIntegration.ApplyRiverFlowDirection(mat, dir, speed);

            if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
            {
                Vector2 nd = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.zero;
                Debug.Log(
                    $"[RiverFlowMaterial] riverId={riverIndex} dir=({nd.x:F2},{nd.y:F2}) speed={speed:F2} " +
                    $"material={(mat != null ? mat.name : "null")}");
            }
        }

        /// <summary>Centerline en espacio celda: dedupe, segmentos nulos, colineales, quiebres, Chaikin opcional, remuestreo.</summary>
        static List<Vector2> ProcessCenterlineCellSpace(
            List<Vector2> cellPath,
            MapGenConfig config,
            out int afterColinear,
            out int afterSmooth,
            out int afterResample)
        {
            afterColinear = afterSmooth = afterResample = 0;
            var pts = new List<Vector2>(cellPath);
            pts = DedupeConsecutiveCell(pts, DedupeCellEps);
            pts = RemoveNearNullSegmentsCell(pts, MinSegmentCellEps);
            pts = RemoveCollinearPointsCell(pts, CollinearDotThreshold);
            afterColinear = pts != null ? pts.Count : 0;
            if (pts == null || pts.Count < 2)
                return null;

            pts = InsertSharpBendMidpointsCell(pts, config.riverSurfaceSharpBendAngleDeg);

            int chaikinPasses = Mathf.Clamp(config.riverSurfaceChaikinPasses, 0, 2);
            if (chaikinPasses > 0)
            {
                var chaikin = ChaikinOpenCell(pts, chaikinPasses);
                if (!PolylineSelfIntersectsXZCell(chaikin))
                    pts = chaikin;
            }

            afterSmooth = pts.Count;

            int pathCells = cellPath.Count;
            int maxPts = Mathf.Max(8, Mathf.CeilToInt(pathCells * Mathf.Max(1.02f, config.riverSurfaceMaxVisualPointRatio)));
            float spacing = Mathf.Max(0.08f, config.riverSurfaceSampleSpacingCells);
            pts = ResampleUniformSpacingCell(pts, spacing, maxPts);
            afterResample = pts != null ? pts.Count : 0;

            return pts;
        }

        static List<float> BuildHalfWidthsDeterministic(
            List<Vector3> worldCenters,
            float baseHalfW,
            float amp,
            float noiseScale,
            int riverIndex)
        {
            int n = worldCenters.Count;
            var hw = new List<float>(n);
            float acc = 0f;
            float yHash = riverIndex * 12.9898f;
            for (int i = 0; i < n; i++)
            {
                float n01 = Mathf.PerlinNoise(acc * noiseScale + yHash, yHash * 0.071f);
                float mul = 1f;
                if (amp > 1e-6f)
                    mul = Mathf.Clamp(1f + amp * (n01 * 2f - 1f), 1f - amp, 1f + amp);
                hw.Add(Mathf.Max(0.02f, baseHalfW * mul));
                if (i < n - 1)
                {
                    Vector3 d = worldCenters[i + 1] - worldCenters[i];
                    d.y = 0f;
                    acc += d.magnitude;
                }
            }

            return hw;
        }

        static long PackCellKey(int x, int y) => ((long)x << 20) ^ (y & 0xfffff);

        static HashSet<long> BuildJoinProximityCellKeys(
            List<List<Vector2>> lines,
            int skipIndex,
            int w,
            int h,
            HashSet<int> excludeRiverIndices = null)
        {
            var hs = new HashSet<long>();
            if (lines == null)
                return hs;
            for (int li = 0; li < lines.Count; li++)
            {
                if (li == skipIndex)
                    continue;
                if (excludeRiverIndices != null && excludeRiverIndices.Contains(li))
                    continue;
                var ln = lines[li];
                if (ln == null)
                    continue;
                for (int k = 0; k < ln.Count; k++)
                {
                    int cx = Mathf.Clamp(Mathf.FloorToInt(ln[k].x), 0, w - 1);
                    int cy = Mathf.Clamp(Mathf.FloorToInt(ln[k].y), 0, h - 1);
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx;
                            int ny = cy + dz;
                            if ((uint)nx < (uint)w && (uint)ny < (uint)h)
                                hs.Add(PackCellKey(nx, ny));
                        }
                    }
                }
            }

            return hs;
        }

        static float MapBorderWidthFade01(float cellX, float cellY, int w, int h, float bandCells)
        {
            float west = 0.5f;
            float east = (w - 1) + 0.5f;
            float south = 0.5f;
            float north = (h - 1) + 0.5f;
            float mx = Mathf.Min(cellX - west, east - cellX);
            float my = Mathf.Min(cellY - south, north - cellY);
            float d = Mathf.Max(0f, Mathf.Min(mx, my));
            return Mathf.Clamp01(d / Mathf.Max(0.25f, bandCells));
        }

        static float BendWidthDampeningAtIndex(List<Vector3> pts, int i, float thrDeg)
        {
            if (pts == null || i < 1 || i >= pts.Count - 1)
                return 1f;
            Vector3 a = pts[i] - pts[i - 1];
            Vector3 b = pts[i + 1] - pts[i];
            a.y = 0f;
            b.y = 0f;
            if (a.sqrMagnitude < 1e-14f || b.sqrMagnitude < 1e-14f)
                return 1f;
            float ang = Vector3.Angle(a, b);
            if (ang >= thrDeg + 14f)
                return 0.26f;
            if (ang >= thrDeg)
                return 0.48f;
            if (ang >= thrDeg - 14f)
                return 0.68f;
            return 1f;
        }

        static List<float> BuildMainRiverHalfWidthsWithArcVariation(
            GridSystem grid,
            List<Vector3> worldCenters,
            List<Vector2> cellSpace,
            float baseHalfW,
            float perlinAmp,
            float noiseScale,
            HashSet<long> joinCells,
            MapGenConfig config,
            out float minW,
            out float maxW)
        {
            minW = float.MaxValue;
            maxW = 0f;
            int n = worldCenters != null ? worldCenters.Count : 0;
            var hw = new List<float>(n);
            if (n < 1 || grid == null || config == null || cellSpace == null || cellSpace.Count != n)
            {
                var fb = BuildHalfWidthsDeterministic(worldCenters, baseHalfW, perlinAmp, noiseScale, 0);
                minW = maxW = baseHalfW;
                if (fb != null && fb.Count > 0)
                {
                    minW = maxW = fb[0];
                    for (int z = 1; z < fb.Count; z++)
                    {
                        minW = Mathf.Min(minW, fb[z]);
                        maxW = Mathf.Max(maxW, fb[z]);
                    }
                }

                return fb;
            }

            int w = grid.Width;
            int h = grid.Height;
            float acc = 0f;
            float yHash = 12.9898f;
            float maxFrac = Mathf.Clamp(config.riverSurfaceMainArcWidthVarMaxFrac, 0f, 0.12f);
            float invLen = Mathf.Max(0.002f, config.riverSurfaceMainArcWidthVarInvLengthWorld);
            bool arcOn = config.riverSurfaceMainArcWidthVarEnabled && maxFrac > 1e-6f;
            float bendThr = Mathf.Clamp(config.riverSurfaceSharpBendAngleDeg - 6f, 35f, 95f);
            const float mapBorderBandCells = 4f;
            for (int i = 0; i < n; i++)
            {
                float n01 = Mathf.PerlinNoise(acc * noiseScale + yHash, yHash * 0.071f);
                float mulP = 1f;
                float perlinUse = arcOn ? perlinAmp * 0.55f : perlinAmp;
                if (perlinUse > 1e-6f)
                    mulP = Mathf.Clamp(1f + perlinUse * (n01 * 2f - 1f), 1f - perlinUse, 1f + perlinUse);

                float arcMul = 1f;
                if (arcOn)
                {
                    float phase = acc * invLen * Mathf.PI * 2f * 0.88f;
                    arcMul = 1f + maxFrac * Mathf.Sin(phase);
                    float t01 = n > 1 ? i / (float)(n - 1) : 0f;
                    float endFade = MeanderEdgeFade(t01, 0.12f);
                    arcMul = 1f + (arcMul - 1f) * endFade;
                    float bendD = BendWidthDampeningAtIndex(worldCenters, i, bendThr);
                    arcMul = Mathf.Lerp(1f, arcMul, bendD);

                    int cx = Mathf.Clamp(Mathf.FloorToInt(cellSpace[i].x), 0, w - 1);
                    int cy = Mathf.Clamp(Mathf.FloorToInt(cellSpace[i].y), 0, h - 1);
                    float fordBlend = 1f;
                    ref var c0 = ref grid.GetCell(cx, cy);
                    if (c0.riverFord)
                        fordBlend = 0.22f;
                    else
                    {
                        foreach (var nb in grid.Neighbors4(cx, cy))
                        {
                            if (grid.GetCell(nb.x, nb.y).riverFord)
                            {
                                fordBlend = 0.32f;
                                break;
                            }
                        }
                    }

                    arcMul = Mathf.Lerp(1f, arcMul, fordBlend);
                    if (joinCells != null && joinCells.Contains(PackCellKey(cx, cy)))
                        arcMul = Mathf.Lerp(1f, arcMul, 0.28f);
                    arcMul = Mathf.Clamp(arcMul, 1f - maxFrac, 1f + maxFrac);
                }

                int cxw = Mathf.Clamp(Mathf.FloorToInt(cellSpace[i].x), 0, w - 1);
                int cyw = Mathf.Clamp(Mathf.FloorToInt(cellSpace[i].y), 0, h - 1);
                int fordDistW = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
                if (WaterMeshBuilder.GridCellNearFordRiverChebyshev(grid, cxw, cyw, fordDistW))
                {
                    mulP = Mathf.Lerp(1f, mulP, 0.4f);
                    arcMul = Mathf.Lerp(1f, arcMul, 0.4f);
                }

                float borderFade = MapBorderWidthFade01(cellSpace[i].x, cellSpace[i].y, w, h, mapBorderBandCells);
                float mulPVis = Mathf.Lerp(1f, mulP, borderFade);
                float arcMulVis = Mathf.Lerp(1f, arcMul, borderFade);
                ResolveRiverSurfaceWidthBands(config, baseHalfW, 0, grid, out float minHwL, out float normalHwL, out float maxHwL);
                float wv = Mathf.Max(0.02f, normalHwL * mulPVis * arcMulVis);
                wv = Mathf.Clamp(wv, minHwL, maxHwL);
                hw.Add(wv);
                minW = Mathf.Min(minW, wv);
                maxW = Mathf.Max(maxW, wv);
                if (i < n - 1)
                {
                    Vector3 d = worldCenters[i + 1] - worldCenters[i];
                    d.y = 0f;
                    acc += d.magnitude;
                }
            }

            return hw;
        }

        static void MeasureSharpBends(List<Vector3> pts, float thresholdDeg, out int sharpCount, out float maxAngleDeg)
        {
            sharpCount = 0;
            maxAngleDeg = 0f;
            if (pts == null || pts.Count < 3)
                return;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i] - pts[i - 1];
                Vector3 b = pts[i + 1] - pts[i];
                a.y = 0f;
                b.y = 0f;
                if (a.sqrMagnitude < 1e-12f || b.sqrMagnitude < 1e-12f)
                    continue;
                float ang = Vector3.Angle(a, b);
                maxAngleDeg = Mathf.Max(maxAngleDeg, ang);
                if (ang > thresholdDeg + 1e-3f)
                    sharpCount++;
            }
        }

        static List<Vector2> InsertSharpBendMidpointsCell(List<Vector2> pts, float thresholdDeg)
        {
            if (pts == null || pts.Count < 3)
                return pts;
            var dst = new List<Vector2>(pts.Count + 8) { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i] - pts[i - 1];
                Vector2 b = pts[i + 1] - pts[i];
                if (a.sqrMagnitude < 1e-14f || b.sqrMagnitude < 1e-14f)
                {
                    dst.Add(pts[i]);
                    continue;
                }

                float ang = Vector2.Angle(a, b);
                dst.Add(pts[i]);
                if (ang > thresholdDeg + 0.01f)
                {
                    Vector2 cut = Vector2.Lerp(pts[i], (pts[i - 1] + pts[i + 1]) * 0.5f, 0.42f);
                    dst.Add(cut);
                }
            }

            dst.Add(pts[pts.Count - 1]);
            return dst;
        }

        static List<Vector2> ChaikinOpenCell(List<Vector2> p, int passes)
        {
            var cur = new List<Vector2>(p);
            for (int pass = 0; pass < passes; pass++)
            {
                if (cur.Count < 2)
                    break;
                var nxt = new List<Vector2>(cur.Count * 2 + 2);
                nxt.Add(cur[0]);
                for (int i = 0; i < cur.Count - 1; i++)
                {
                    nxt.Add(Vector2.Lerp(cur[i], cur[i + 1], 0.25f));
                    nxt.Add(Vector2.Lerp(cur[i], cur[i + 1], 0.75f));
                }

                nxt.Add(cur[cur.Count - 1]);
                cur = nxt;
            }

            return cur;
        }

        static bool PolylineSelfIntersectsXZCell(List<Vector2> poly, int minIndexGap = 2)
        {
            if (poly == null || poly.Count < 4)
                return false;
            int n = poly.Count;
            int gap = Mathf.Max(2, minIndexGap);
            for (int i = 0; i < n - 1; i++)
            {
                for (int j = i + gap; j < n - 1; j++)
                {
                    if (SegmentsIntersect2D(poly[i], poly[i + 1], poly[j], poly[j + 1]))
                        return true;
                }
            }

            return false;
        }

        static bool SegmentsIntersect2D(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            float d = (p2.x - p1.x) * (p4.y - p3.y) - (p2.y - p1.y) * (p4.x - p3.x);
            if (Mathf.Abs(d) < 1e-10f)
                return false;
            float t = ((p3.x - p1.x) * (p4.y - p3.y) - (p3.y - p1.y) * (p4.x - p3.x)) / d;
            float u = ((p3.x - p1.x) * (p2.y - p1.y) - (p3.y - p1.y) * (p2.x - p1.x)) / d;
            return t > 1e-5f && t < 1f - 1e-5f && u > 1e-5f && u < 1f - 1e-5f;
        }

        static List<Vector2> ResampleUniformSpacingCell(List<Vector2> src, float spacingCells, int maxPts)
        {
            if (src == null || src.Count < 2)
                return src;
            maxPts = Mathf.Clamp(maxPts, 2, 8192);
            spacingCells = Mathf.Max(0.05f, spacingCells);

            var cum = new float[src.Count];
            cum[0] = 0f;
            for (int i = 1; i < src.Count; i++)
            {
                float d = Vector2.Distance(src[i], src[i - 1]);
                cum[i] = cum[i - 1] + d;
            }

            float L = cum[src.Count - 1];
            if (L < 1e-6f)
                return src;

            int target = Mathf.Max(2, Mathf.CeilToInt(L / spacingCells) + 1);
            if (target > maxPts)
            {
                spacingCells = L / (maxPts - 1);
                target = maxPts;
            }

            target = Mathf.Min(target, maxPts);
            var dst = new List<Vector2>(target);
            for (int i = 0; i < target; i++)
            {
                float t = (i / (float)Mathf.Max(1, target - 1)) * L;
                int j = 0;
                while (j < cum.Length - 1 && cum[j + 1] < t)
                    j++;
                float seg = Mathf.Max(1e-8f, cum[j + 1] - cum[j]);
                float u = (t - cum[j]) / seg;
                dst.Add(Vector2.Lerp(src[j], src[j + 1], Mathf.Clamp01(u)));
            }

            return dst;
        }

        static float MaxSegmentLengthCell(List<Vector2> pts)
        {
            if (pts == null || pts.Count < 2)
                return 0f;
            float m = 0f;
            for (int i = 1; i < pts.Count; i++)
                m = Mathf.Max(m, Vector2.Distance(pts[i], pts[i - 1]));
            return m;
        }

        static List<Vector2> DedupeConsecutiveCell(List<Vector2> pts, float eps)
        {
            if (pts == null || pts.Count < 2)
                return pts;
            float e2 = eps * eps;
            var r = new List<Vector2>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                if ((pts[i] - r[r.Count - 1]).sqrMagnitude > e2)
                    r.Add(pts[i]);
            }

            return r;
        }

        /// <summary>Elimina vértices de centerline amontonados (spline/proyección en curvas).</summary>
        static List<Vector2> CollapseCenterlineNearDuplicatesCell(List<Vector2> pts, float minSpacingCells)
        {
            if (pts == null || pts.Count < 2)
                return pts;
            minSpacingCells = Mathf.Max(MinCenterlineSpacingCells * 0.85f, minSpacingCells);
            float min2 = minSpacingCells * minSpacingCells;
            var r = new List<Vector2>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                if ((pts[i] - r[r.Count - 1]).sqrMagnitude >= min2)
                    r.Add(pts[i]);
            }

            Vector2 end = pts[pts.Count - 1];
            if ((end - r[r.Count - 1]).sqrMagnitude >= min2 * 0.2f)
                r.Add(end);
            else
                r[r.Count - 1] = end;

            return r.Count >= 2 ? r : pts;
        }

        static List<Vector2> NormalizeCenterlineSpacingForMesh(List<Vector2> pts, MapGenConfig config)
        {
            if (pts == null || pts.Count < 2 || config == null)
                return pts;
            float spacing = config.riverSurfaceUseSplineVisualCenterline
                ? Mathf.Max(MinCenterlineSpacingCells, config.riverSurfaceSplineSampleSpacingCells * 1.1f)
                : Mathf.Max(MinCenterlineSpacingCells, config.riverSurfaceVisualSpacingCells);
            spacing = Mathf.Clamp(spacing, MinCenterlineSpacingCells, 1.1f);
            pts = CollapseCenterlineNearDuplicatesCell(pts, spacing);
            if (pts.Count < 2)
                return pts;
            int maxPts = Mathf.Max(8, Mathf.CeilToInt(pts.Count * Mathf.Max(1.02f, config.riverSurfaceMaxVisualPointRatio)));
            return ResampleUniformSpacingCell(pts, spacing, maxPts);
        }

        static List<Vector2> RemoveNearNullSegmentsCell(List<Vector2> pts, float eps)
        {
            if (pts == null || pts.Count < 2)
                return pts;
            float e2 = eps * eps;
            var r = new List<Vector2>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                if ((pts[i] - r[r.Count - 1]).sqrMagnitude >= e2)
                    r.Add(pts[i]);
            }

            return r;
        }

        static List<Vector2> RemoveCollinearPointsCell(List<Vector2> pts, float dotThresh)
        {
            if (pts == null || pts.Count < 3)
                return pts;
            var r = new List<Vector2>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector2 a = r[r.Count - 1];
                Vector2 b = pts[i];
                Vector2 c = pts[i + 1];
                Vector2 d0 = b - a;
                Vector2 d1 = c - b;
                float m0 = d0.sqrMagnitude;
                float m1 = d1.sqrMagnitude;
                if (m0 < 1e-12f || m1 < 1e-12f)
                {
                    r.Add(b);
                    continue;
                }

                float dot = Mathf.Clamp(Vector2.Dot(d0.normalized, d1.normalized), -1f, 1f);
                if (dot > dotThresh)
                    continue;
                r.Add(b);
            }

            r.Add(pts[pts.Count - 1]);
            return r;
        }

        static List<Vector3> CellPolylineToWorldXZ(List<Vector2> cellPath, Vector3 origin, float cellSize, float yWorld)
        {
            var r = new List<Vector3>(cellPath.Count);
            for (int i = 0; i < cellPath.Count; i++)
            {
                var c = cellPath[i];
                float wx = origin.x + c.x * cellSize;
                float wz = origin.z + c.y * cellSize;
                r.Add(new Vector3(wx, yWorld, wz));
            }

            return r;
        }

        static int CountProximityWarnings(List<Vector3> c, float threshold)
        {
            if (c == null || c.Count < 4)
                return 0;
            float th2 = threshold * threshold;
            int w = 0;
            for (int i = 0; i < c.Count; i++)
            {
                int jMax = Mathf.Min(c.Count - 2, i + 48);
                for (int j = i + 2; j <= jMax; j++)
                {
                    float d2 = PointSegmentDistSqXZ(c[i], c[j], c[j + 1]);
                    if (d2 < th2)
                        w++;
                }
            }

            return w;
        }

        static float PointSegmentDistSqXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 ab = new Vector2(b.x - a.x, b.z - a.z);
            float den = Vector2.Dot(ab, ab);
            if (den < 1e-8f)
            {
                float dx = p.x - a.x;
                float dz = p.z - a.z;
                return dx * dx + dz * dz;
            }

            Vector2 ap = new Vector2(p.x - a.x, p.z - a.z);
            float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / den);
            float qx = a.x + ab.x * t;
            float qz = a.z + ab.y * t;
            float dx2 = p.x - qx;
            float dz2 = p.z - qz;
            return dx2 * dx2 + dz2 * dz2;
        }

        static float MaxSegmentLengthXZ(List<Vector3> pts)
        {
            if (pts == null || pts.Count < 2)
                return 0f;
            float m = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector3 d = pts[i] - pts[i - 1];
                d.y = 0f;
                m = Mathf.Max(m, d.magnitude);
            }

            return m;
        }

        static bool IsTrueMapEdgeCellSpace(Vector2 p, int w, int h, float eps = 0.055f)
        {
            float west = 0.5f;
            float east = (w - 1) + 0.5f;
            float south = 0.5f;
            float north = (h - 1) + 0.5f;
            return p.x <= west + eps || p.x >= east - eps || p.y <= south + eps || p.y >= north - eps;
        }

        static void GetPlayableWorldBoundsXZ(Vector3 origin, int w, int h, float cellSize, float edgeInsetWorld, out float xMin, out float xMax, out float zMin, out float zMax)
        {
            float inset = Mathf.Max(0f, edgeInsetWorld);
            xMin = origin.x + inset;
            xMax = origin.x + w * cellSize - inset;
            zMin = origin.z + inset;
            zMax = origin.z + h * cellSize - inset;
            if (xMax < xMin)
                xMax = xMin = origin.x + w * cellSize * 0.5f;
            if (zMax < zMin)
                zMax = zMin = origin.z + h * cellSize * 0.5f;
        }

        static bool IsInsidePlayableBoundsXZ(Vector3 p, float xMin, float xMax, float zMin, float zMax)
        {
            return p.x >= xMin - 1e-4f && p.x <= xMax + 1e-4f && p.z >= zMin - 1e-4f && p.z <= zMax + 1e-4f;
        }

        static Vector3 ClampXZToPlayableBounds(Vector3 p, float xMin, float xMax, float zMin, float zMax)
        {
            p.x = Mathf.Clamp(p.x, xMin, xMax);
            p.z = Mathf.Clamp(p.z, zMin, zMax);
            return p;
        }

        /// <summary>Proyecta un extremo fuera del mapa sobre el borde jugable a lo largo del segmento interior→extremo.</summary>
        static void ProjectStripEndpointToPlayableEdge(List<Vector3> center, int endIdx, int innerIdx, float xMin, float xMax, float zMin, float zMax)
        {
            if (center == null || endIdx < 0 || endIdx >= center.Count || innerIdx < 0 || innerIdx >= center.Count)
                return;
            Vector3 a = center[innerIdx];
            Vector3 b = center[endIdx];
            a.y = b.y = 0f;
            if (IsInsidePlayableBoundsXZ(b, xMin, xMax, zMin, zMax))
                return;
            for (int step = 0; step < 24; step++)
            {
                float t = 1f - Mathf.Pow(0.5f, step + 1);
                Vector3 p = Vector3.Lerp(a, b, t);
                if (IsInsidePlayableBoundsXZ(p, xMin, xMax, zMin, zMax))
                {
                    center[endIdx] = p;
                    return;
                }
            }

            center[endIdx] = ClampXZToPlayableBounds(b, xMin, xMax, zMin, zMax);
        }

        static void RebuildStripCrossSectionAt(List<Vector3> center, List<float> halfWidths, List<Vector3> left, List<Vector3> right, int i)
        {
            if (center == null || halfWidths == null || left == null || right == null)
                return;
            if (i < 0 || i >= center.Count || halfWidths.Count != center.Count)
                return;
            if (i >= left.Count || i >= right.Count)
                return;
            Vector3 tan = TangentNormalize(center, i);
            Vector3 nrm = PerpendicularXZ(tan);
            float hw = Mathf.Max(0.02f, halfWidths[i]);
            left[i] = center[i] - nrm * hw;
            right[i] = center[i] + nrm * hw;
        }

        static int ClipRiverSurfaceStripToPlayableBounds(
            Vector3 origin,
            int gridW,
            int gridH,
            float cellSize,
            List<Vector3> center,
            List<float> halfWidths,
            List<Vector3> left,
            List<Vector3> right)
        {
            if (center == null || left == null || right == null || center.Count < 2)
                return 0;
            float inset = 0f;
            GetPlayableWorldBoundsXZ(origin, gridW, gridH, cellSize, inset, out float xMin, out float xMax, out float zMin, out float zMax);

            int n = center.Count;
            if (n >= 2)
            {
                ProjectStripEndpointToPlayableEdge(center, 0, 1, xMin, xMax, zMin, zMax);
                ProjectStripEndpointToPlayableEdge(center, n - 1, n - 2, xMin, xMax, zMin, zMax);
            }

            int clipped = 0;
            void ClipList(List<Vector3> pts)
            {
                if (pts == null)
                    return;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (!IsInsidePlayableBoundsXZ(pts[i], xMin, xMax, zMin, zMax))
                        clipped++;
                    pts[i] = ClampXZToPlayableBounds(pts[i], xMin, xMax, zMin, zMax);
                }
            }

            ClipList(center);
            ClipList(left);
            ClipList(right);
            if (halfWidths != null && halfWidths.Count == center.Count)
            {
                RebuildStripCrossSectionAt(center, halfWidths, left, right, 0);
                RebuildStripCrossSectionAt(center, halfWidths, left, right, n - 1);
            }

            return clipped;
        }

        static int FinalClampVertexListToPlayableBounds(
            List<Vector3> verts,
            Vector3 origin,
            int gridW,
            int gridH,
            float cellSize,
            out int visibleOutside,
            out float minX,
            out float maxX,
            out float minZ,
            out float maxZ)
        {
            visibleOutside = 0;
            minX = maxX = minZ = maxZ = 0f;
            if (verts == null || verts.Count == 0)
                return 0;

            float inset = 0f;
            GetPlayableWorldBoundsXZ(origin, gridW, gridH, cellSize, inset, out float xMin, out float xMax, out float zMin, out float zMax);
            minX = maxX = verts[0].x;
            minZ = maxZ = verts[0].z;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 p = verts[i];
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z);
                maxZ = Mathf.Max(maxZ, p.z);
                if (!IsInsidePlayableBoundsXZ(p, xMin, xMax, zMin, zMax))
                    visibleOutside++;
                verts[i] = ClampXZToPlayableBounds(p, xMin, xMax, zMin, zMax);
            }

            return visibleOutside;
        }

        static bool TryCullRiverSurfaceFragmentAfterBuild(
            GameObject go,
            Mesh mesh,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            string objectName,
            List<Vector2> cellSpaceLine,
            float cellSizeWorld,
            bool logDiag,
            out bool culled)
        {
            culled = false;
            if (go == null || mesh == null || grid == null || config == null)
                return false;

            if (config.uwpOwnedVisualPolicy && grid.RiverVisualSurfaceCacheFrozen && riverIndex > 0 &&
                cellSpaceLine != null && cellSpaceLine.Count >= 2)
            {
                float frozenLen = PolylineLengthCellSpace(cellSpaceLine);
                if (frozenLen >= Mathf.Max(2f, config.riverVisualMinSurfacePieceLengthCells))
                    return false;
            }

            Bounds bounds = mesh.bounds;
            float cs = Mathf.Max(0.01f, cellSizeWorld);
            int nearCells = Mathf.Max(1, config.riverVisualFinalCleanupNearRiverCells);
            WaterMeshBuilder.ComputeWaterVisualBoundsMaskStats(grid, bounds, nearCells, out int intersectsMask, out int nearMaskCells);
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            int nearFord = WaterMeshBuilder.ComputeNearFordFromWorldBounds(grid, bounds, fordD);
            float maxDim = Mathf.Max(bounds.size.x, bounds.size.z);
            int areaApprox = Mathf.Max(1, Mathf.RoundToInt(bounds.size.x * bounds.size.z / (cs * cs)));
            float lineLenCells = PolylineLengthCellSpace(cellSpaceLine);

            bool shouldCull = false;
            string reason = "";
            if (riverIndex > 0)
            {
                if (intersectsMask == 0 && maxDim < cs * 2.75f && areaApprox < Mathf.Max(4, config.riverVisualMinSurfacePieceAreaCells))
                {
                    shouldCull = true;
                    reason = "tributary_off_mask_small";
                }
                else if (intersectsMask == 0 && nearMaskCells == 0 && maxDim < cs * 4f && lineLenCells < config.riverVisualMinSurfacePieceLengthCells * 0.5f)
                {
                    shouldCull = true;
                    reason = "tributary_detached_fragment";
                }
            }
            else if (intersectsMask == 0 && nearMaskCells == 0 && nearFord == 0)
            {
                if (maxDim < cs * 1.85f && areaApprox <= 6)
                {
                    shouldCull = true;
                    reason = "tiny_main_off_mask";
                }
            }

            if (!shouldCull)
                return false;

            if (logDiag)
            {
                WaterMeshBuilder.LogWaterVisualObject(
                    config,
                    objectName,
                    "RiverSurface",
                    riverIndex,
                    mesh.vertexCount,
                    mesh.triangles.Length / 3,
                    bounds,
                    intersectsMask,
                    nearMaskCells,
                    nearFord,
                    riverIndex == 0 ? 1 : 0,
                    riverIndex > 0 ? 1 : 0,
                    1,
                    reason);
                Debug.Log(
                    $"[RiverSurfaceFragmentCull] riverIndex={riverIndex} name={objectName} maxDim={maxDim:F3} areaCells={areaApprox} " +
                    $"lineLenCells={lineLenCells:F1} intersectsMask={intersectsMask} nearMaskCells={nearMaskCells} " +
                    $"nearFord={nearFord} culled=1 reason={reason}");
            }

            if (Application.isPlaying)
            {
                Object.Destroy(go);
                Object.Destroy(mesh);
            }
            else
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(mesh);
            }

            RiverSurfaceFragmentCullCount++;
            culled = true;
            return true;
        }

        static void CopyVector3List(IReadOnlyList<Vector3> src, List<Vector3> dst)
        {
            if (src == null || dst == null)
                return;
            if (dst.Count != src.Count)
            {
                dst.Clear();
                for (int i = 0; i < src.Count; i++)
                    dst.Add(src[i]);
            }
            else
            {
                for (int i = 0; i < src.Count; i++)
                    dst[i] = src[i];
            }
        }

        static bool ShouldSkipDetachedTributaryRiverSurface(
            GridSystem grid,
            List<Vector2> tributaryCellSpace,
            int tributaryIndex,
            MapGenConfig config)
        {
            if (grid == null || tributaryCellSpace == null || tributaryIndex <= 0 || config == null)
                return false;
            if (config.uwpOwnedVisualPolicy)
                return false;
            int minPatch = Mathf.Max(2, config.riverVisualMinDetachedPatchCells);
            int corridor = Mathf.Clamp(config.riverVisualMainRiverCorridorCells, 1, 8);
            var mainLine = grid.RiverCenterlinesCellSpace[0];
            if (mainLine == null || mainLine.Count < 2)
                return false;

            var corridorCells = new HashSet<Vector2Int>();
            int w = grid.Width;
            int h = grid.Height;
            for (int i = 0; i < mainLine.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.RoundToInt(mainLine[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.RoundToInt(mainLine[i].y), 0, h - 1);
                for (int dy = -corridor; dy <= corridor; dy++)
                {
                    for (int dx = -corridor; dx <= corridor; dx++)
                    {
                        int nx = cx + dx;
                        int ny = cy + dy;
                        if ((uint)nx < (uint)w && (uint)ny < (uint)h)
                            corridorCells.Add(new Vector2Int(nx, ny));
                    }
                }
            }

            var trib = new HashSet<Vector2Int>();
            bool hasFord = false;
            for (int i = 0; i < tributaryCellSpace.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(tributaryCellSpace[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(tributaryCellSpace[i].y), 0, h - 1);
                trib.Add(new Vector2Int(cx, cy));
                if (grid.GetCell(cx, cy).riverFord)
                    hasFord = true;
            }

            if (hasFord)
                return false;
            foreach (Vector2Int c in trib)
            {
                if (corridorCells.Contains(c))
                    return false;
            }

            return trib.Count < minPatch;
        }

        static float PolylineLengthCellSpace(List<Vector2> pts)
        {
            if (pts == null || pts.Count < 2)
                return 0f;
            float L = 0f;
            for (int i = 1; i < pts.Count; i++)
                L += Vector2.Distance(pts[i], pts[i - 1]);
            return L;
        }

        static bool TributaryPolylineTouchesMainCorridor(
            GridSystem grid,
            List<Vector2> tributaryCellSpace,
            MapGenConfig config)
        {
            if (grid == null || tributaryCellSpace == null || config == null ||
                grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return false;

            int corridor = Mathf.Clamp(config.riverVisualMainRiverCorridorCells, 1, 8);
            int w = grid.Width;
            int h = grid.Height;
            var mainLine = grid.RiverCenterlinesCellSpace[0];
            if (mainLine == null || mainLine.Count < 2)
                return false;

            var corridorCells = new HashSet<Vector2Int>();
            for (int i = 0; i < mainLine.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.RoundToInt(mainLine[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.RoundToInt(mainLine[i].y), 0, h - 1);
                for (int dy = -corridor; dy <= corridor; dy++)
                {
                    for (int dx = -corridor; dx <= corridor; dx++)
                    {
                        int nx = cx + dx;
                        int ny = cy + dy;
                        if ((uint)nx < (uint)w && (uint)ny < (uint)h)
                            corridorCells.Add(new Vector2Int(nx, ny));
                    }
                }
            }

            for (int i = 0; i < tributaryCellSpace.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(tributaryCellSpace[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(tributaryCellSpace[i].y), 0, h - 1);
                if (corridorCells.Contains(new Vector2Int(cx, cy)))
                    return true;
            }

            return false;
        }

        static bool TryCullTributarySurfacePiece(
            GridSystem grid,
            List<Vector2> tributaryCellSpace,
            int tributaryIndex,
            MapGenConfig config,
            bool logRm)
        {
            if (grid == null || tributaryCellSpace == null || tributaryIndex <= 0 || config == null)
                return false;

            if (config.uwpOwnedVisualPolicy &&
                (TryGetTributaryConfluenceCell(grid, tributaryIndex, out _, out _) ||
                 TributaryCenterlineTouchesLake(grid, tributaryCellSpace)))
                return false;

            if (TributaryPolylineTouchesMainCorridor(grid, tributaryCellSpace, config))
                return false;

            bool detachedSkip = ShouldSkipDetachedTributaryRiverSurface(grid, tributaryCellSpace, tributaryIndex, config);
            bool shortSkip = !detachedSkip &&
                ShouldSkipShortTributaryRiverSurfaceVisual(grid, tributaryCellSpace, tributaryIndex, config);
            if (!detachedSkip && !shortSkip)
                return false;

            if (detachedSkip)
                DetachedRiverSurfaceSkips++;
            else
                ShortRiverSurfaceSkips++;

            if (logRm)
            {
                int w = grid.Width;
                int h = grid.Height;
                var trib = new HashSet<Vector2Int>();
                for (int i = 0; i < tributaryCellSpace.Count; i++)
                {
                    int cx = Mathf.Clamp(Mathf.FloorToInt(tributaryCellSpace[i].x), 0, w - 1);
                    int cy = Mathf.Clamp(Mathf.FloorToInt(tributaryCellSpace[i].y), 0, h - 1);
                    trib.Add(new Vector2Int(cx, cy));
                }

                int fordKeep = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
                bool nearFord = false;
                foreach (Vector2Int c in trib)
                {
                    if (WaterMeshBuilder.GridCellNearFordRiverChebyshev(grid, c.x, c.y, fordKeep))
                    {
                        nearFord = true;
                        break;
                    }
                }

                var joinKeys = BuildJoinProximityCellKeys(grid.RiverCenterlinesCellSpace, tributaryIndex, w, h);
                bool nearConfluence = false;
                foreach (Vector2Int c in trib)
                {
                    if (joinKeys.Contains(PackCellKey(c.x, c.y)))
                    {
                        nearConfluence = true;
                        break;
                    }
                }

                float lenCells = PolylineLengthCellSpace(tributaryCellSpace);
                string reason = detachedSkip ? "detached_patch" : "short_piece";
                Debug.Log(
                    $"[RiverSurfacePieceCull] riverIndex={tributaryIndex} lengthCells={lenCells:F1} areaCells={trib.Count} " +
                    $"nearFord={(nearFord ? 1 : 0)} nearConfluence={(nearConfluence ? 1 : 0)} culled=1 reason={reason}");
            }

            return true;
        }

        static bool ShouldSkipShortTributaryRiverSurfaceVisual(
            GridSystem grid,
            List<Vector2> tributaryCellSpace,
            int tributaryIndex,
            MapGenConfig config)
        {
            if (grid == null || tributaryCellSpace == null || tributaryIndex <= 0 || config == null)
                return false;
            if (config.uwpOwnedVisualPolicy)
                return false;
            int minLen = Mathf.Max(2, config.riverVisualMinSurfacePieceLengthCells);
            int minArea = Mathf.Max(2, config.riverVisualMinSurfacePieceAreaCells);
            int corridor = Mathf.Max(
                Mathf.Clamp(config.riverVisualMainRiverCorridorCells, 1, 8),
                Mathf.Clamp(config.riverVisualMainCorridorKeepDistanceCells, 1, 16));
            int fordKeep = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            int w = grid.Width;
            int h = grid.Height;
            var mainLine = grid.RiverCenterlinesCellSpace[0];
            if (mainLine == null || mainLine.Count < 2)
                return false;

            var corridorCells = new HashSet<Vector2Int>();
            for (int i = 0; i < mainLine.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.RoundToInt(mainLine[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.RoundToInt(mainLine[i].y), 0, h - 1);
                for (int dy = -corridor; dy <= corridor; dy++)
                {
                    for (int dx = -corridor; dx <= corridor; dx++)
                    {
                        int nx = cx + dx;
                        int ny = cy + dy;
                        if ((uint)nx < (uint)w && (uint)ny < (uint)h)
                            corridorCells.Add(new Vector2Int(nx, ny));
                    }
                }
            }

            var trib = new HashSet<Vector2Int>();
            for (int i = 0; i < tributaryCellSpace.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(tributaryCellSpace[i].x), 0, w - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(tributaryCellSpace[i].y), 0, h - 1);
                trib.Add(new Vector2Int(cx, cy));
            }

            foreach (Vector2Int c in trib)
            {
                if (WaterMeshBuilder.GridCellNearFordRiverChebyshev(grid, c.x, c.y, fordKeep))
                    return false;
            }

            var joinKeys = BuildJoinProximityCellKeys(grid.RiverCenterlinesCellSpace, tributaryIndex, w, h);
            foreach (Vector2Int c in trib)
            {
                if (joinKeys.Contains(PackCellKey(c.x, c.y)))
                    return false;
            }

            foreach (Vector2Int c in trib)
            {
                if (corridorCells.Contains(c))
                    return false;
            }

            float polyLen = PolylineLengthCellSpace(tributaryCellSpace);
            return polyLen < minLen || trib.Count < minArea;
        }

        static void ResolveVisualMeanderAndOverlapWarnings(
            List<Vector3> worldCenters,
            List<Vector2> cellProcessed,
            List<Vector3> backupAfterBorder,
            List<Vector3> backupCellsOnly,
            Vector3 origin,
            float cellSize,
            int w,
            int h,
            MapGenConfig config,
            int riverIndex,
            float baseHalfW,
            bool logDetail,
            out int overlapWarningsAfter,
            out int overlapWarningsBefore)
        {
            float th = Mathf.Max(0.02f, baseHalfW * 0.9f);
            overlapWarningsBefore = backupAfterBorder != null ? CountProximityWarnings(backupAfterBorder, th) : 0;
            int retryReduced = 0;
            int disabledDueToOverlap = 0;
            int revertedNoBorder = 0;
            bool meanderAccepted = false;
            float maxOffsetCells = 0f;
            string reject = "na";

            if (config == null ||
                !config.riverSurfaceVisualMeanderEnabled ||
                worldCenters == null ||
                backupAfterBorder == null ||
                backupCellsOnly == null ||
                worldCenters.Count < 4)
            {
                if (worldCenters != null && backupAfterBorder != null)
                    CopyVector3List(backupAfterBorder, worldCenters);
                meanderAccepted = false;
                reject = "meander_disabled_or_short";
                overlapWarningsAfter = worldCenters != null ? CountProximityWarnings(worldCenters, th) : 0;
                if (logDetail)
                {
                    Debug.Log(
                        $"[RiverVisualMeander] riverId={riverIndex} enabled={(config != null && config.riverSurfaceVisualMeanderEnabled ? 1 : 0)} " +
                        $"amplitudeCells={(config != null ? config.riverSurfaceVisualMeanderAmplitudeCells : 0f):F3} frequencyCells={(config != null ? config.riverSurfaceVisualMeanderFrequencyCells : 0f):F2} " +
                        $"points={(worldCenters != null ? worldCenters.Count : 0)} maxOffsetCells={maxOffsetCells:F3} accepted={(meanderAccepted ? 1 : 0)} " +
                        $"retryReducedAmplitude=0 disabledDueToOverlap=0 overlapWarningsBefore={overlapWarningsBefore} overlapWarningsAfter={overlapWarningsAfter} rejectReason={reject}");
                }

                return;
            }

            void ApplyMeander(float ampOverride, bool silent, out bool acc, out float maxOff, out string rej)
            {
                CopyVector3List(backupAfterBorder, worldCenters);
                ApplyVisualMeanderToCenters(
                    worldCenters,
                    cellProcessed,
                    origin,
                    cellSize,
                    w,
                    h,
                    config,
                    riverIndex,
                    logDetail: false,
                    ampOverride,
                    silent,
                    out maxOff,
                    out acc,
                    out rej);
            }

            ApplyMeander(-1f, true, out meanderAccepted, out maxOffsetCells, out reject);
            overlapWarningsAfter = CountProximityWarnings(worldCenters, th);
            if (overlapWarningsAfter > 0)
            {
                retryReduced = 1;
                float halfAmp = Mathf.Clamp(config.riverSurfaceVisualMeanderAmplitudeCells * 0.5f, 0f, 0.6f);
                ApplyMeander(halfAmp, true, out meanderAccepted, out maxOffsetCells, out reject);
                overlapWarningsAfter = CountProximityWarnings(worldCenters, th);
            }

            if (overlapWarningsAfter > 0)
            {
                disabledDueToOverlap = 1;
                meanderAccepted = false;
                CopyVector3List(backupAfterBorder, worldCenters);
                overlapWarningsAfter = CountProximityWarnings(worldCenters, th);
            }

            if (overlapWarningsAfter > 0)
            {
                revertedNoBorder = 1;
                meanderAccepted = false;
                CopyVector3List(backupCellsOnly, worldCenters);
                overlapWarningsAfter = CountProximityWarnings(worldCenters, th);
            }

            if (logDetail)
            {
                Debug.Log(
                    $"[RiverVisualMeander] riverId={riverIndex} enabled=1 amplitudeCells={config.riverSurfaceVisualMeanderAmplitudeCells:F3} " +
                    $"frequencyCells={config.riverSurfaceVisualMeanderFrequencyCells:F2} points={worldCenters.Count} maxOffsetCells={maxOffsetCells:F3} " +
                    $"accepted={(meanderAccepted ? 1 : 0)} retryReducedAmplitude={retryReduced} disabledDueToOverlap={disabledDueToOverlap} " +
                    $"revertedNoBorderExtension={revertedNoBorder} overlapWarningsBefore={overlapWarningsBefore} overlapWarningsAfter={overlapWarningsAfter} " +
                    $"rejectReason={reject}");
            }
        }

        static void ApplyBorderVisualExtensionToCenters(
            List<Vector3> centersWorld,
            IReadOnlyList<Vector2> cellSpace,
            Vector3 origin,
            float cellSize,
            int w,
            int h,
            MapGenConfig config,
            float fullRiverWidthWorld)
        {
            if (centersWorld == null || cellSpace == null || centersWorld.Count != cellSpace.Count || config == null)
                return;
            float extLegacy = Mathf.Clamp(config.riverSurfaceExtendBorderExitVisualCells, 0f, 1.5f) * cellSize;
            float mul = config.riverSurfaceExtendBeyondMapWidthMul;
            float fullW = Mathf.Max(cellSize * 0.25f, fullRiverWidthWorld);
            float extByWidth = mul > 1e-5f ? Mathf.Clamp(mul * fullW, 1.5f * fullW, 3f * fullW) : 0f;
            float extUse = Mathf.Max(extLegacy, extByWidth);
            if (extUse < 1e-5f)
                return;
            int n = centersWorld.Count;
            if (n < 2)
                return;
            if (IsTrueMapEdgeCellSpace(cellSpace[n - 1], w, h))
            {
                Vector3 t = TangentNormalize(centersWorld, n - 1);
                if (t.sqrMagnitude > 1e-12f)
                    centersWorld[n - 1] += t * extUse;
            }

            if (IsTrueMapEdgeCellSpace(cellSpace[0], w, h))
            {
                Vector3 t0 = TangentNormalize(centersWorld, 0);
                if (t0.sqrMagnitude > 1e-12f)
                    centersWorld[0] -= t0 * extUse;
            }
        }

        static List<Vector2> WorldCentersToCellSpacePolyline(List<Vector3> centersWorld, Vector3 origin, float cellSize, int w, int h, bool clampToMapInterior)
        {
            float inv = 1f / Mathf.Max(1e-5f, cellSize);
            var r = new List<Vector2>(centersWorld.Count);
            for (int i = 0; i < centersWorld.Count; i++)
            {
                float x = (centersWorld[i].x - origin.x) * inv;
                float y = (centersWorld[i].z - origin.z) * inv;
                if (clampToMapInterior)
                {
                    x = Mathf.Clamp(x, 0.2f, w - 0.2f);
                    y = Mathf.Clamp(y, 0.2f, h - 0.2f);
                }

                r.Add(new Vector2(x, y));
            }

            return r;
        }

        static float MeanderEdgeFade(float t01, float fade)
        {
            fade = Mathf.Clamp01(fade);
            if (fade < 1e-5f)
                return 1f;
            if (t01 <= fade)
                return Mathf.SmoothStep(0f, 1f, t01 / Mathf.Max(1e-5f, fade));
            if (t01 >= 1f - fade)
                return Mathf.SmoothStep(1f, 0f, (t01 - (1f - fade)) / Mathf.Max(1e-5f, fade));
            return 1f;
        }

        static Vector2 CellPolylineTangentNormalize(List<Vector2> pts, int i)
        {
            if (pts == null || pts.Count < 2)
                return Vector2.right;
            Vector2 a = i > 0 ? pts[i] - pts[i - 1] : pts[Mathf.Min(1, pts.Count - 1)] - pts[0];
            Vector2 b = i < pts.Count - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1];
            Vector2 t = a + b;
            if (t.sqrMagnitude < 1e-10f)
                t = b.sqrMagnitude > 1e-10f ? b : a;
            return t.sqrMagnitude < 1e-10f ? Vector2.right : t.normalized;
        }

        /// <summary>
        /// Amplitud relativa a min(W,H). Main usa fórmula S/contra-S (2 senos + ruido suave) + Catmull-Rom denso.
        /// </summary>
        static void ResolveGridScaledMeanderParams(
            int w,
            int h,
            MapGenConfig config,
            bool isMain,
            out float ampC,
            out float freqC,
            out float fade01,
            out int phaseSeed)
        {
            float minDim = Mathf.Max(64f, Mathf.Min(w, h));
            // ChatGPT ref: main ~0.055 * mapSize (ampC = A1).
            float ampRatio = isMain ? 0.055f : 0.0055f;
            float ampFromGrid = minDim * ampRatio;
            float ampFromCfg = config != null
                ? config.riverSurfaceVisualMeanderAmplitudeCells * (isMain ? 6.0f : 1.55f)
                : 0f;
            ampC = Mathf.Clamp(
                Mathf.Max(ampFromGrid, ampFromCfg),
                isMain ? 4.0f : 0.45f,
                isMain ? 18.0f : 2.2f);

            float freqFromGrid = isMain
                ? Mathf.Clamp(minDim * 0.40f, 48f, 140f)
                : Mathf.Clamp(minDim * 0.028f, 4.5f, 12f);
            float freqCfg = config != null
                ? Mathf.Max(2f, config.riverSurfaceVisualMeanderFrequencyCells)
                : freqFromGrid;
            freqC = isMain ? freqFromGrid : Mathf.Lerp(freqFromGrid, freqCfg, 0.20f);
            fade01 = config != null
                ? Mathf.Clamp(config.riverSurfaceVisualMeanderEndFade01, 0.02f, isMain ? 0.12f : 0.35f)
                : (isMain ? 0.10f : 0.12f);
            phaseSeed = (config != null ? config.seed : 0) * 31 + (isMain ? 0 : 17);
        }

        static float EstimateAvgSegmentLengthCell(List<Vector2> pts)
        {
            if (pts == null || pts.Count < 2)
                return 0.5f;
            float sum = 0f;
            for (int i = 1; i < pts.Count; i++)
                sum += Vector2.Distance(pts[i], pts[i - 1]);
            return Mathf.Max(0.2f, sum / (pts.Count - 1));
        }

        static float ComputePolylineLengthCell(List<Vector2> pts)
        {
            if (pts == null || pts.Count < 2)
                return 0f;
            float sum = 0f;
            for (int i = 1; i < pts.Count; i++)
                sum += Vector2.Distance(pts[i], pts[i - 1]);
            return sum;
        }

        /// <summary>Limita giro entre nodos (~25°) para evitar quiebres caritos.</summary>
        static void ClampControlTurnAngles(List<Vector2> controls, float maxTurnDeg)
        {
            if (controls == null || controls.Count < 3)
                return;
            float maxRad = maxTurnDeg * Mathf.Deg2Rad;
            for (int i = 2; i < controls.Count; i++)
            {
                Vector2 prev = controls[i - 1] - controls[i - 2];
                Vector2 cand = controls[i] - controls[i - 1];
                if (prev.sqrMagnitude < 1e-8f || cand.sqrMagnitude < 1e-8f)
                    continue;
                prev.Normalize();
                float candLen = cand.magnitude;
                cand /= candLen;
                float dot = Mathf.Clamp(Vector2.Dot(prev, cand), -1f, 1f);
                float ang = Mathf.Acos(dot);
                if (ang <= maxRad)
                    continue;
                // Mezcla suave hacia la dirección previa (evita codo poligonal).
                Vector2 blended = Vector2.Lerp(prev, cand, 0.30f).normalized;
                controls[i] = controls[i - 1] + blended * candLen;
            }
        }

        /// <summary>
        /// Main: fórmula tipo ChatGPT — A1·sin(2.1) + A2·sin(4.3) + ruido suave, 10–14 nodos,
        /// límite de giro, Catmull-Rom denso. No fallback lineal (eso dejaba tramos rectos).
        /// </summary>
        static bool TryApplyMainMeanderOnCoarseCenterline(
            List<Vector2> cellPath,
            int w,
            int h,
            int riverIndex,
            float ampC,
            float freqC,
            float fade01,
            int phaseSeed,
            out float maxOffsetCells)
        {
            maxOffsetCells = 0f;
            if (cellPath == null || cellPath.Count < 6)
                return false;

            float pathLen = ComputePolylineLengthCell(cellPath);
            if (pathLen < 8f)
                return false;

            float minDim = Mathf.Max(64f, Mathf.Min(w, h));
            // Nodos cada 0.08–0.14 L → ~8–14 controles.
            float controlSpacing = Mathf.Clamp(pathLen * 0.10f, pathLen * 0.08f, pathLen * 0.14f);
            controlSpacing = Mathf.Clamp(controlSpacing, 10f, 28f);
            var baseControls = ResampleUniformSpacingCell(cellPath, controlSpacing, 16);
            if (baseControls == null || baseControls.Count < 5)
                return false;

            // Amplitudes relativas al mapa (ref. ChatGPT).
            float a1 = Mathf.Clamp(ampC, minDim * 0.040f, minDim * 0.070f);
            float a2 = minDim * 0.015f;
            float an = minDim * 0.012f;
            float phase1 = phaseSeed * 0.0173f + 0.4f;
            float phase2 = phaseSeed * 0.0311f + 1.7f;
            float seedNoise = (phaseSeed % 997) * 0.013f;

            float[] ampScales = { 1f, 0.82f, 0.65f };
            List<Vector2> bestControls = null;
            float bestMax = 0f;

            for (int si = 0; si < ampScales.Length; si++)
            {
                float s = ampScales[si];
                var trial = new List<Vector2>(baseControls.Count);
                float acc = 0f;
                Vector2 prevNrm = Vector2.zero;
                float peak = 0f;

                for (int i = 0; i < baseControls.Count; i++)
                {
                    if (i > 0)
                        acc += Vector2.Distance(baseControls[i], baseControls[i - 1]);

                    Vector2 p = baseControls[i];
                    if (i == 0 || i == baseControls.Count - 1 || IsTrueMapEdgeCellSpace(p, w, h))
                    {
                        trial.Add(p);
                        continue;
                    }

                    float t01 = Mathf.Clamp01(acc / pathLen);
                    float fade = MeanderEdgeFade(t01, fade01);
                    if (fade < 1e-4f)
                    {
                        trial.Add(p);
                        continue;
                    }

                    Vector2 tan = CellPolylineTangentNormalize(baseControls, i);
                    Vector2 nrm = new Vector2(-tan.y, tan.x);
                    if (prevNrm.sqrMagnitude > 1e-8f && Vector2.Dot(nrm, prevNrm) < 0f)
                        nrm = -nrm;
                    if (nrm.sqrMagnitude > 1e-8f)
                        prevNrm = nrm;

                    float mainWave = Mathf.Sin(t01 * Mathf.PI * 2f * 2.1f + phase1) * (a1 * s);
                    float secondaryWave = Mathf.Sin(t01 * Mathf.PI * 2f * 4.3f + phase2) * (a2 * s);
                    float noise = (Mathf.PerlinNoise(t01 * 2.5f, seedNoise) - 0.5f) * 2f * (an * s);
                    float off = (mainWave + secondaryWave + noise) * fade;
                    peak = Mathf.Max(peak, Mathf.Abs(off));
                    trial.Add(p + nrm * off);
                }

                if (trial.Count != baseControls.Count)
                    continue;

                ClampControlTurnAngles(trial, 25f);
                if (PolylineSelfIntersectsXZCell(trial, 3))
                    continue;

                bestControls = trial;
                bestMax = peak;
                break;
            }

            if (bestControls == null)
                return false;

            // Catmull-Rom denso: la curva visible no debe ser polilínea de nodos.
            float sampleSpacing = 0.28f;
            int maxPts = Mathf.Clamp(Mathf.CeilToInt(pathLen / sampleSpacing) + 8, 128, 2048);
            var smooth = SampleCentripetalSpline(bestControls, sampleSpacing, 0.5f, maxPts);
            if (smooth == null || smooth.Count < 8)
                return false;

            // Si el spline se auto-cruza: bajar amp ya falló arriba; suavizar Chaikin 1 pass si cabe.
            if (PolylineSelfIntersectsXZCell(smooth, 5))
            {
                var chaikin = ChaikinOpenCellPreserveEnds(smooth, 2);
                if (chaikin != null && chaikin.Count >= 8 && !PolylineSelfIntersectsXZCell(chaikin, 5))
                    smooth = chaikin;
                else
                    return false; // mejor rechazar que caer a tramos rectos entre nodos
            }

            maxOffsetCells = bestMax;
            cellPath.Clear();
            cellPath.AddRange(smooth);
            return true;
        }

        static bool ApplyVisualMeanderToCellSpace(
            List<Vector2> cellPath,
            int w,
            int h,
            MapGenConfig config,
            int riverIndex,
            out float maxOffsetCells)
        {
            maxOffsetCells = 0f;
            if (config == null || !config.riverSurfaceVisualMeanderEnabled || cellPath == null || cellPath.Count < 4)
                return false;

            bool isMain = riverIndex == 0;
            ResolveGridScaledMeanderParams(w, h, config, isMain, out float ampC, out float freqC, out float fade01, out int phaseSeed);

            if (isMain)
                return TryApplyMainMeanderOnCoarseCenterline(
                    cellPath, w, h, riverIndex, ampC, freqC, fade01, phaseSeed, out maxOffsetCells);

            if (TryApplyMeanderOffsets(
                    cellPath, w, h, riverIndex, ampC, freqC, fade01, out maxOffsetCells,
                    phaseSeed: phaseSeed))
                return true;

            float ampSoft = Mathf.Max(0.4f, ampC * 0.7f);
            return TryApplyMeanderOffsets(
                cellPath, w, h, riverIndex, ampSoft, freqC * 1.3f, fade01, out maxOffsetCells,
                phaseSeed: phaseSeed + 17);
        }

        static bool TryApplyMeanderOffsets(
            List<Vector2> cellPath,
            int w,
            int h,
            int riverIndex,
            float ampC,
            float freqC,
            float fade01,
            out float maxOffsetCells,
            bool protectMainJoinApproachTail = false,
            int phaseSeed = -1,
            int selfIntersectIndexGap = 2)
        {
            maxOffsetCells = 0f;
            int seed = phaseSeed >= 0 ? phaseSeed : riverIndex;
            bool isMain = riverIndex == 0;
            int indexGap = Mathf.Max(2, selfIntersectIndexGap);
            var trial = new List<Vector2>(cellPath);
            float acc = 0f;
            Vector2 prevNrm = Vector2.zero;
            for (int i = 0; i < trial.Count; i++)
            {
                if (i > 0)
                    acc += Vector2.Distance(trial[i], trial[i - 1]);
                if (i == 0 || i == trial.Count - 1)
                    continue;
                if (IsTrueMapEdgeCellSpace(trial[i], w, h))
                    continue;

                float t01 = trial.Count > 1 ? i / (float)(trial.Count - 1) : 0f;
                if (protectMainJoinApproachTail && !isMain && t01 > LakeFirstInlandApproachTailFreezeT01)
                    continue;
                float fade = MeanderEdgeFade(t01, fade01);
                if (!isMain && t01 < 0.22f)
                    fade = Mathf.Max(fade, 0.72f);
                if (fade < 1e-4f)
                    continue;

                Vector2 tan = CellPolylineTangentNormalize(trial, i);
                Vector2 nrm = new Vector2(-tan.y, tan.x);
                if (prevNrm.sqrMagnitude > 1e-8f && Vector2.Dot(nrm, prevNrm) < 0f)
                    nrm = -nrm;
                if (nrm.sqrMagnitude > 1e-8f)
                    prevNrm = nrm;

                float phase = acc / freqC * (Mathf.PI * 2f) + seed * 1.713f;
                float organicWave = Mathf.Sin(phase);
                float offCells = organicWave * ampC * fade;
                trial[i] += nrm * offCells;
                maxOffsetCells = Mathf.Max(maxOffsetCells, Mathf.Abs(offCells));
            }

            if (PolylineSelfIntersectsXZCell(trial, indexGap))
                return false;

            for (int i = 0; i < cellPath.Count; i++)
                cellPath[i] = trial[i];
            return true;
        }

        /// <summary>Lake-first: meandro orgánico. Main incluido (rompe tramos rectos/cuadriculados). Headwater excluido.</summary>
        static bool ApplyLakeFirstOrganicMeanderToCellSpace(
            GridSystem grid,
            List<Vector2> cellPath,
            int w,
            int h,
            MapGenConfig config,
            int riverIndex,
            out float maxOffsetCells,
            bool protectMainJoinApproachTail = false)
        {
            maxOffsetCells = 0f;
            if (config == null || cellPath == null || cellPath.Count < 4)
                return false;

            bool isMain = riverIndex == 0;
            if (!isMain && IsLakeFirstHeadwaterFeeder(grid, riverIndex))
                return false;
            if (!isMain &&
                !protectMainJoinApproachTail &&
                !UwpTributaryOriginUtility.UsesLakeFirstTributaryMeanderTreatment(grid, config, riverIndex))
                return false;

            ResolveGridScaledMeanderParams(w, h, config, isMain, out float ampC, out float freqC, out float fade01, out int phaseSeed);

            if (isMain)
                return TryApplyMainMeanderOnCoarseCenterline(
                    cellPath, w, h, riverIndex, ampC, freqC, fade01, phaseSeed, out maxOffsetCells);

            if (TryApplyMeanderOffsets(
                    cellPath, w, h, riverIndex, ampC, freqC, fade01, out maxOffsetCells,
                    protectMainJoinApproachTail, phaseSeed))
                return true;
            float ampSoft = Mathf.Max(0.45f, ampC * 0.7f);
            return TryApplyMeanderOffsets(
                cellPath, w, h, riverIndex, ampSoft, freqC * 1.3f, fade01, out maxOffsetCells,
                protectMainJoinApproachTail, phaseSeed: phaseSeed + 17);
        }

        static void SnapMainRiverMapBorderEndpoints(List<Vector2> cellPath, int w, int h)
        {
            if (cellPath == null || cellPath.Count < 2)
                return;

            SnapMainRiverEndpointFlushToMapBorder(cellPath, w, h, isStart: true);
            SnapMainRiverEndpointFlushToMapBorder(cellPath, w, h, isStart: false);
        }

        static void SnapMainRiverEndpointFlushToMapBorder(List<Vector2> cellPath, int w, int h, bool isStart)
        {
            int ep = isStart ? 0 : cellPath.Count - 1;
            int inner = isStart ? 1 : cellPath.Count - 2;
            if (inner < 0 || inner >= cellPath.Count)
                return;
            if (!IsTrueMapEdgeCellSpace(cellPath[ep], w, h))
                return;

            Vector2 outward = ResolveMapBorderOutwardDir(cellPath[ep], w, h);
            if (outward.sqrMagnitude < 1e-8f)
                return;

            float west = 0.5f;
            float east = (w - 1) + 0.5f;
            float south = 0.5f;
            float north = (h - 1) + 0.5f;
            Vector2 snapped = cellPath[ep];
            if (outward.x < -0.5f)
                snapped.x = west;
            else if (outward.x > 0.5f)
                snapped.x = east;
            if (outward.y < -0.5f)
                snapped.y = south;
            else if (outward.y > 0.5f)
                snapped.y = north;

            Vector2 approach = snapped - cellPath[inner];
            float approachLen = approach.sqrMagnitude > 1e-6f ? approach.magnitude : 0.85f;
            cellPath[inner] = snapped - outward * Mathf.Max(approachLen, 0.85f);
            cellPath[ep] = snapped;
        }

        static void ApplyVisualMeanderToCenters(
            List<Vector3> centersWorld,
            IReadOnlyList<Vector2> cellSpace,
            Vector3 origin,
            float cellSize,
            int w,
            int h,
            MapGenConfig config,
            int riverIndex,
            bool logDetail,
            float amplitudeCellsOverride,
            bool silent,
            out float maxOffsetCells,
            out bool accepted,
            out string rejectReason)
        {
            maxOffsetCells = 0f;
            accepted = false;
            rejectReason = "disabled";
            if (config == null || !config.riverSurfaceVisualMeanderEnabled || centersWorld == null || centersWorld.Count < 4)
            {
                rejectReason = config == null || !config.riverSurfaceVisualMeanderEnabled ? "disabled" : "too_few_points";
                return;
            }

            float ampC = amplitudeCellsOverride >= 0f
                ? Mathf.Clamp(amplitudeCellsOverride, 0f, 0.6f)
                : Mathf.Clamp(
                    Mathf.Max(config.riverSurfaceVisualMeanderAmplitudeCells, riverIndex == 0 ? 0.28f : 0.20f),
                    0f,
                    0.6f);
            float freqC = Mathf.Max(2f, config.riverSurfaceVisualMeanderFrequencyCells);
            float fade01 = Mathf.Clamp(config.riverSurfaceVisualMeanderEndFade01, 0.02f, 0.35f);

            var trial = new List<Vector3>(centersWorld);
            float acc = 0f;
            maxOffsetCells = 0f;
            for (int i = 0; i < trial.Count; i++)
            {
                if (i > 0)
                {
                    Vector3 d = trial[i] - trial[i - 1];
                    d.y = 0f;
                    acc += d.magnitude;
                }

                if (i == 0 || i == trial.Count - 1)
                    continue;
                if (cellSpace != null && i < cellSpace.Count && IsTrueMapEdgeCellSpace(cellSpace[i], w, h))
                    continue;

                float t01 = trial.Count > 1 ? i / (float)(trial.Count - 1) : 0f;
                float fade = MeanderEdgeFade(t01, fade01);
                if (fade < 1e-4f)
                    continue;

                Vector3 tan = TangentNormalize(trial, i);
                Vector3 nrm = PerpendicularXZ(tan);
                float phase = acc / Mathf.Max(0.01f, cellSize) / freqC * (Mathf.PI * 2f) + riverIndex * 1.713f;
                float accCells = acc / Mathf.Max(0.01f, cellSize);
                float noise01 = Mathf.PerlinNoise(accCells / Mathf.Max(2f, freqC * 0.73f) + riverIndex * 13.17f, riverIndex * 0.271f + 4.13f);
                float organicWave = Mathf.Clamp(Mathf.Sin(phase) * 0.72f + (noise01 * 2f - 1f) * 0.38f, -1f, 1f);
                float offWorld = organicWave * ampC * cellSize * fade;
                trial[i] += nrm * offWorld;
                maxOffsetCells = Mathf.Max(maxOffsetCells, Mathf.Abs(offWorld / Mathf.Max(1e-5f, cellSize)));
            }

            var cellPoly = WorldCentersToCellSpacePolyline(trial, origin, cellSize, w, h, true);
            if (PolylineSelfIntersectsXZCell(cellPoly))
            {
                rejectReason = "self_intersect";
                if (logDetail && !silent)
                {
                    UnityEngine.Debug.Log(
                        $"[RiverVisualMeander] riverId={riverIndex} enabled=1 amplitudeCells={ampC:F3} frequencyCells={freqC:F2} " +
                        $"points={trial.Count} maxOffsetCells={maxOffsetCells:F3} accepted=0 rejectReason={rejectReason}");
                }

                return;
            }

            for (int i = 0; i < centersWorld.Count; i++)
                centersWorld[i] = trial[i];
            accepted = true;
            rejectReason = "ok";
            if (logDetail && !silent)
            {
                UnityEngine.Debug.Log(
                    $"[RiverVisualMeander] riverId={riverIndex} enabled=1 amplitudeCells={ampC:F3} frequencyCells={freqC:F2} " +
                    $"points={trial.Count} maxOffsetCells={maxOffsetCells:F3} accepted=1 rejectReason=none");
            }
        }

        static float ComputeMaxInteriorBendAngleDeg(List<Vector3> c)
        {
            int n = c != null ? c.Count : 0;
            if (n < 3)
                return 0f;
            float m = 0f;
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 a = c[i] - c[i - 1];
                Vector3 b = c[i + 1] - c[i];
                a.y = 0f;
                b.y = 0f;
                if (a.sqrMagnitude < 1e-14f || b.sqrMagnitude < 1e-14f)
                    continue;
                m = Mathf.Max(m, Vector3.Angle(a, b));
            }

            return m;
        }

        static float BendCapRelaxMulFromEnd(List<Vector3> c, bool atStart, float relaxAngleDeg, int scan)
        {
            int n = c != null ? c.Count : 0;
            if (n < 3)
                return 1f;
            float minMul = 1f;
            int used = Mathf.Min(Mathf.Max(1, scan), n - 2);
            for (int k = 0; k < used; k++)
            {
                int i = atStart ? 1 + k : n - 2 - k;
                if (i < 1 || i >= n - 1)
                    break;
                Vector3 a = c[i] - c[i - 1];
                Vector3 b = c[i + 1] - c[i];
                a.y = 0f;
                b.y = 0f;
                if (a.sqrMagnitude < 1e-14f || b.sqrMagnitude < 1e-14f)
                    continue;
                float ang = Vector3.Angle(a, b);
                if (ang >= relaxAngleDeg + 16f)
                    minMul = Mathf.Min(minMul, 0.34f);
                else if (ang >= relaxAngleDeg)
                    minMul = Mathf.Min(minMul, 0.54f);
                else if (ang >= relaxAngleDeg - 14f)
                    minMul = Mathf.Min(minMul, 0.76f);
            }

            return minMul;
        }

        struct RiverBorderEndpointSymmetry
        {
            public bool AtBorder;
            public bool GhostInserted;
            public Vector2 OutwardDir;
            public float VisualHalfWidth;
            public bool UsesTaper;
            public bool UsesCap;
        }

        static void ResolveRiverBorderEndpointDirs(
            List<Vector2> cellSpace,
            int endpointIndex,
            int interiorIndex,
            int gridW,
            int gridH,
            out Vector2 outwardDir,
            out Vector2 inwardDir)
        {
            Vector2 endpoint = cellSpace[endpointIndex];
            Vector2 interior = cellSpace[interiorIndex];
            outwardDir = ResolveMapBorderOutwardDir(endpoint, gridW, gridH);
            if (outwardDir.sqrMagnitude < 1e-10f)
                outwardDir = endpoint - interior;
            if (outwardDir.sqrMagnitude < 1e-10f)
                outwardDir = endpointIndex == 0 ? Vector2.left : Vector2.right;
            else
                outwardDir.Normalize();
            inwardDir = -outwardDir;
        }

        static Vector2 ResolveMapBorderOutwardDir(Vector2 p, int gridW, int gridH)
        {
            float west = Mathf.Abs(p.x - 0.5f);
            float east = Mathf.Abs(p.x - ((gridW - 1) + 0.5f));
            float south = Mathf.Abs(p.y - 0.5f);
            float north = Mathf.Abs(p.y - ((gridH - 1) + 0.5f));
            float best = Mathf.Min(Mathf.Min(west, east), Mathf.Min(south, north));
            if (best > 0.12f)
                return Vector2.zero;
            if (Mathf.Approximately(best, west)) return Vector2.left;
            if (Mathf.Approximately(best, east)) return Vector2.right;
            if (Mathf.Approximately(best, south)) return Vector2.down;
            return Vector2.up;
        }

        static void ApplySymmetricBorderEndpointMeshTreatment(
            List<Vector3> center,
            List<float> halfWidths,
            List<Vector2> cellSpace,
            Vector3 mapOrigin,
            int gridW,
            int gridH,
            float cellSizeWorld,
            MapGenConfig config,
            float baseHalfWidthWorld,
            out RiverBorderEndpointSymmetry startSym,
            out RiverBorderEndpointSymmetry endSym)
        {
            startSym = default;
            endSym = default;
            if (center == null || halfWidths == null || cellSpace == null || config == null)
                return;
            if (center.Count != halfWidths.Count || center.Count != cellSpace.Count || center.Count < 2)
                return;

            float ghostCells = config.riverSurfaceFlatMapBorderCut
                ? Mathf.Min(1.25f, Mathf.Clamp(config.riverSurfaceBorderGhostCells, 0f, 6f))
                : Mathf.Clamp(config.riverSurfaceBorderGhostCells, 0f, 6f);
            bool uniformBorderWidth = config.riverSurfaceSkipCapAtMapBorder &&
                config.riverSurfaceBorderEndpointWidthMul <= 1.01f;
            float borderWidthMul = uniformBorderWidth
                ? 1f
                : Mathf.Clamp(config.riverSurfaceBorderEndpointWidthMul, 1.5f, 3f);
            float minBorderHalf = Mathf.Max(0.02f, baseHalfWidthWorld * borderWidthMul);

            void TreatEndpoint(bool isStart, ref RiverBorderEndpointSymmetry sym)
            {
                int n = center.Count;
                if (n < 2)
                    return;
                int ep = isStart ? 0 : n - 1;
                int inner = isStart ? 1 : n - 2;
                sym.AtBorder = IsTrueMapEdgeCellSpace(cellSpace[ep], gridW, gridH);
                sym.UsesTaper = false;
                sym.UsesCap = false;
                if (!sym.AtBorder)
                    return;

                ResolveRiverBorderEndpointDirs(cellSpace, ep, inner, gridW, gridH, out Vector2 outward, out _);
                sym.OutwardDir = outward;
                if (uniformBorderWidth)
                    sym.VisualHalfWidth = halfWidths[ep];
                else
                    sym.VisualHalfWidth = Mathf.Max(halfWidths[ep], minBorderHalf);
                halfWidths[ep] = sym.VisualHalfWidth;

                bool flatBorderCut = config.riverSurfaceFlatMapBorderCut;
                float ghostUse = uniformBorderWidth && flatBorderCut
                    ? Mathf.Min(ghostCells, 0.85f)
                    : ghostCells;
                if (ghostUse > 1e-4f && (!uniformBorderWidth || flatBorderCut))
                {
                    Vector2 ghostCell = cellSpace[ep] + outward * ghostUse;
                    Vector3 ghostWorld = new Vector3(
                        mapOrigin.x + ghostCell.x * cellSizeWorld,
                        center[ep].y,
                        mapOrigin.z + ghostCell.y * cellSizeWorld);
                    if (isStart)
                    {
                        center.Insert(0, ghostWorld);
                        halfWidths.Insert(0, sym.VisualHalfWidth);
                        cellSpace.Insert(0, ghostCell);
                    }
                    else
                    {
                        center.Add(ghostWorld);
                        halfWidths.Add(sym.VisualHalfWidth);
                        cellSpace.Add(ghostCell);
                    }

                    sym.GhostInserted = true;
                }
            }

            TreatEndpoint(true, ref startSym);
            TreatEndpoint(false, ref endSym);
        }

        static void LogRiverEndpointSymmetry(
            MapGenConfig config,
            int riverId,
            RiverBorderEndpointSymmetry startSym,
            RiverBorderEndpointSymmetry endSym)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            Debug.Log(
                $"[RiverEndpointSymmetry] riverId={riverId} startAtBorder={(startSym.AtBorder ? 1 : 0)} endAtBorder={(endSym.AtBorder ? 1 : 0)} " +
                $"startOutwardDir=({startSym.OutwardDir.x:F2},{startSym.OutwardDir.y:F2}) endOutwardDir=({endSym.OutwardDir.x:F2},{endSym.OutwardDir.y:F2}) " +
                $"startGhostPoints={(startSym.GhostInserted ? 1 : 0)} endGhostPoints={(endSym.GhostInserted ? 1 : 0)} " +
                $"startUsesTaper={(startSym.UsesTaper ? 1 : 0)} endUsesTaper={(endSym.UsesTaper ? 1 : 0)} " +
                $"startUsesCap={(startSym.UsesCap ? 1 : 0)} endUsesCap={(endSym.UsesCap ? 1 : 0)} " +
                $"startVisualHalfWidth={startSym.VisualHalfWidth:F3} endVisualHalfWidth={endSym.VisualHalfWidth:F3} symmetricPolicy=1");
        }

        static void LogRiverSurfaceEndFix(
            MapGenConfig config,
            int riverId,
            RiverBorderEndpointSymmetry endSym,
            float baseHalfWidthWorld)
        {
            if (config == null || (!config.debugLogs && !config.debugHydrologyNetwork))
                return;
            bool stable = endSym.GhostInserted && endSym.AtBorder;
            float widthMul = baseHalfWidthWorld > 1e-5f ? endSym.VisualHalfWidth / baseHalfWidthWorld : 0f;
            Debug.Log(
                $"[RiverSurfaceEndFix] riverId={riverId} endAtBorder={(endSym.AtBorder ? 1 : 0)} endGhostPointUsed={(endSym.GhostInserted ? 1 : 0)} " +
                $"endTangentStable={(stable ? 1 : 0)} endWidthMul={widthMul:F3} " +
                $"endCapDisabled=1 endTaperDisabled=1 endFlatCut=1 endContinuesBeyondMapVisually={(endSym.GhostInserted ? 1 : 0)}");
        }

        static bool TryBuildStripMeshWithCaps(
            Transform parent,
            List<Vector3> center,
            List<float> halfWidthWorld,
            Material mat,
            int waterLayer,
            string objectName,
            float uvScale,
            float cellSizeWorld,
            MapGenConfig config,
            int riverIndex,
            int gridWCells,
            int gridHCells,
            List<Vector2> cellSpaceLine,
            GridSystem grid,
            Vector3 mapOrigin,
            bool centerlinePreClipped,
            out int outVerts,
            out int outTris,
            out float maxSegBuilt)
        {
            outVerts = 0;
            outTris = 0;
            maxSegBuilt = 0f;
            int n = center.Count;
            if (n < 2 || halfWidthWorld == null || halfWidthWorld.Count != n)
                return false;

            var meshCenter = new List<Vector3>(center);
            var meshHalfW = new List<float>(halfWidthWorld);
            var meshCell = cellSpaceLine != null ? new List<Vector2>(cellSpaceLine) : null;
            float borderMul = Mathf.Clamp(config != null ? config.riverSurfaceBorderEndpointWidthMul : 2f, 1.5f, 3f);
            float baseHalfForBorder = config != null && config.riverVisualRibbonFullWidthCellsMain > 0.01f
                ? config.riverVisualRibbonFullWidthCellsMain * 0.5f * cellSizeWorld
                : (halfWidthWorld.Count > 0 ? halfWidthWorld[0] / borderMul : 0.5f);
            ApplySymmetricBorderEndpointMeshTreatment(
                meshCenter,
                meshHalfW,
                meshCell,
                mapOrigin,
                gridWCells,
                gridHCells,
                cellSizeWorld,
                config,
                baseHalfForBorder,
                out RiverBorderEndpointSymmetry startSym,
                out RiverBorderEndpointSymmetry endSym);
            if (config != null && (config.debugLogs || config.debugHydrologyNetwork))
            {
                LogRiverEndpointSymmetry(config, riverIndex, startSym, endSym);
                if (endSym.AtBorder)
                    LogRiverSurfaceEndFix(config, riverIndex, endSym, baseHalfForBorder);
            }

            bool mouthFusionTrimmed = false;
            bool mouthTaperStart = false;
            bool mouthTaperEnd = false;
            if (meshCell != null &&
                TryApplyMouthFusionLakeMouthMeshTrim(
                    grid, config, riverIndex, meshCenter, meshHalfW, meshCell,
                    out mouthTaperStart, out mouthTaperEnd))
            {
                mouthFusionTrimmed = true;
                bool skipLakeFirstTribTaper = config != null && UsesLakeFirstMainJoinMeshTreatment(grid, config, riverIndex);
                if (mouthTaperStart && !skipLakeFirstTribTaper)
                    ApplyMouthFusionShoreWidthTaper(meshHalfW, atStart: true);
                if (mouthTaperEnd && !skipLakeFirstTribTaper)
                    ApplyMouthFusionShoreWidthTaper(meshHalfW, atStart: false);
            }

            n = meshCenter.Count;
            center = meshCenter;
            halfWidthWorld = meshHalfW;
            cellSpaceLine = meshCell;

            bool cellHintOkPre = cellSpaceLine != null && cellSpaceLine.Count == n;
            bool startAtBorderPre = cellHintOkPre && IsTrueMapEdgeCellSpace(cellSpaceLine[0], gridWCells, gridHCells);
            bool endAtBorderPre = cellHintOkPre && IsTrueMapEdgeCellSpace(cellSpaceLine[n - 1], gridWCells, gridHCells);
            float extensionWorldPre = 0f;
            if (startAtBorderPre || endAtBorderPre)
            {
                GetPlayableWorldBoundsXZ(mapOrigin, gridWCells, gridHCells, cellSizeWorld, 0f,
                    out float xMin, out float xMax, out float zMin, out float zMax);
                if (startAtBorderPre && !IsInsidePlayableBoundsXZ(center[0], xMin, xMax, zMin, zMax))
                    extensionWorldPre = Mathf.Max(extensionWorldPre, Vector3.Distance(center[0], ClampXZToPlayableBounds(center[0], xMin, xMax, zMin, zMax)));
                if (endAtBorderPre && !IsInsidePlayableBoundsXZ(center[n - 1], xMin, xMax, zMin, zMax))
                    extensionWorldPre = Mathf.Max(extensionWorldPre, Vector3.Distance(center[n - 1], ClampXZToPlayableBounds(center[n - 1], xMin, xMax, zMin, zMax)));
            }

            bool logCap = config != null && (config.debugLogs || config.debugHydrologyNetwork);
            bool cellHintOk = cellSpaceLine != null && cellSpaceLine.Count == n;
            bool startAtBorder = cellHintOk && IsTrueMapEdgeCellSpace(cellSpaceLine[0], gridWCells, gridHCells);
            bool endAtBorder = cellHintOk && IsTrueMapEdgeCellSpace(cellSpaceLine[n - 1], gridWCells, gridHCells);
            bool skipEndBlend = riverIndex > 0;
            bool skipAllEndpointTaper = riverIndex > 0 &&
                config != null &&
                WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config);

            if (skipAllEndpointTaper && cellSpaceLine != null && cellSpaceLine.Count == n)
            {
                ApplyWebFusionTributaryEndpointSubmergeY(
                    center, cellSpaceLine, grid, config, riverIndex, cellSizeWorld, 0f);
            }

            s_mouthFusionMeshHints = new MouthFusionMeshBuildHints
            {
                Active = mouthFusionTrimmed,
                TaperAtStart = mouthTaperStart,
                TaperAtEnd = mouthTaperEnd
            };

            BuildCrossSectionRiverMesh(
                center,
                halfWidthWorld,
                cellSpaceLine,
                grid,
                config,
                riverIndex,
                cellSizeWorld,
                uvScale,
                baseHalfForBorder,
                startAtBorder,
                endAtBorder,
                skipEndBlend,
                skipAllEndpointTaper,
                out List<Vector3> verts,
                out List<Vector2> uvs,
                out List<Vector2> uvs2,
                out List<Vector3> normals,
                out List<Vector4> tangents,
                out List<int> tris,
                out maxSegBuilt,
                out _);

            s_mouthFusionMeshHints = default;

            int smoothPasses = config != null ? config.riverSurfaceEdgeSmoothPasses : 0;
            float edgeSmoothStr = config != null ? config.riverSurfaceEdgeSmoothStrength : 0f;
            if (riverIndex == 0)
            {
                edgeSmoothStr *= 0.32f;
                smoothPasses = Mathf.Min(smoothPasses, 1);
            }

            SmoothCrossSectionRows(
                verts,
                n,
                smoothPasses,
                edgeSmoothStr,
                cellSpaceLine,
                JoinAngleHardDeg - 6f);

            bool visibleDebugWire = config != null &&
                (config.riverSurfaceDebugShowWire ||
                 config.riverSurfaceDebugDrawCenterline ||
                 config.riverSurfaceDebugDrawEdges ||
                 config.riverSurfaceDebugDrawJoinNormals);

            if (logCap)
            {
                LogRiverSurfaceMeshBuild(config, riverIndex, n, verts.Count, tris.Count / 3, visibleDebugWire);
            }

            if (verts.Count < CrossSectionVertexCount * 2 || tris.Count < 6)
                return false;

            if (riverIndex > 0 && grid != null && config != null &&
                WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config) &&
                !IsLakeEmissaryRiverIndex(grid, riverIndex) &&
                !(config.uwpOwnedVisualPolicy && grid.RiverVisualSurfaceCacheFrozen))
            {
                int trisBeforeCull = tris.Count / 3;
                int overlapCulled = CullTributaryTrianglesInsideMainRiverSurface(
                    verts, tris, grid, config, riverIndex, mapOrigin, cellSizeWorld);
                if (logCap)
                {
                    Debug.Log(
                        $"[RiverSurfaceTriangleCull] riverIndex={riverIndex} trisBefore={trisBeforeCull} trisAfter={tris.Count / 3} " +
                        $"culledTris=0 overlapCulledTris={overlapCulled} reason=webfusion_main_core_overlap");
                }
            }
            else if (riverIndex > 0 && grid != null && config != null &&
                     config.uwpOwnedVisualPolicy &&
                     UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex))
            {
                // Cull solo núcleo duro del Main (no orilla visual): cull amplio comía
                // media cinta del spill → carve blanco sin ribbon (todas las seeds).
                int trisBeforeCull = tris.Count / 3;
                int overlapCulled = CullTributaryTrianglesInsideMainRiverSurface(
                    verts, tris, grid, config, riverIndex, mapOrigin, cellSizeWorld);
                if (logCap)
                {
                    Debug.Log(
                        $"[RiverSurfaceTriangleCull] riverIndex={riverIndex} trisBefore={trisBeforeCull} trisAfter={tris.Count / 3} " +
                        $"culledTris=0 overlapCulledTris={overlapCulled} reason=lakefirst_spill_main_core_only");
                }
            }
            else if (riverIndex > 0 && IsLakeEmissaryRiverIndex(grid, riverIndex))
            {
                int trisBeforeCull = tris.Count / 3;
                int culledTris = CullTrianglesOutsideVisualMask(verts, tris, grid, config, riverIndex, mapOrigin, cellSizeWorld);
                if (logCap && culledTris > 0)
                {
                    Debug.Log(
                        $"[RiverSurfaceTriangleCull] riverIndex={riverIndex} trisBefore={trisBeforeCull} trisAfter={tris.Count / 3} " +
                        $"culledTris={culledTris} overlapCulledTris=0 reason=outside_visual_mask");
                }
            }

            if (tris.Count < 6)
                return false;

            int visibleOutside = 0;
            float bMinX = 0f, bMaxX = 0f, bMinZ = 0f, bMaxZ = 0f;
            int capVertsOutsidePlayable = FinalClampVertexListToPlayableBounds(
                verts,
                mapOrigin,
                gridWCells,
                gridHCells,
                cellSizeWorld,
                out visibleOutside,
                out bMinX,
                out bMaxX,
                out bMinZ,
                out bMaxZ);

            if (logCap && (startAtBorderPre || endAtBorderPre || visibleOutside > 0))
            {
                Debug.Log(
                    $"[RiverSurfaceBorderClip] riverIndex={riverIndex} clippedVerts=0 " +
                    $"startAtBorder={(startAtBorderPre ? 1 : 0)} endAtBorder={(endAtBorderPre ? 1 : 0)} " +
                    $"extensionWorld={extensionWorldPre:F3} visibleOutsideBounds={visibleOutside} " +
                    $"minX={bMinX:F2} maxX={bMaxX:F2} minZ={bMinZ:F2} maxZ={bMaxZ:F2} note=legacy_post_clamp");
            }

            var mesh = new Mesh { name = objectName };
            if (verts.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, uvs2);
            var colors = new List<Color>(verts.Count);
            FillRiverSurfaceVertexColors(colors, mat, cellSpaceLine, n, CrossSectionVertexCount, grid, config, riverIndex);
            mesh.SetColors(colors);
            mesh.RecalculateBounds();
            WaterStylizedIntegration.PrepareMesh(mesh, mat);

            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            if (config != null && Mathf.Abs(config.riverSurfaceMainRootYOffsetWorld) > 1e-5f &&
                !WaterVisualPipelinePolicy.UsesUwpUnifiedWaterSurfaceLevel(config) &&
                (config.uwpOwnedVisualPolicy || riverIndex == 0))
            {
                Vector3 lp = go.transform.localPosition;
                lp.y = config.riverSurfaceMainRootYOffsetWorld;
                go.transform.localPosition = lp;
            }
            go.layer = waterLayer;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.enabled = true;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.renderingLayerMask = 1u;
            WaterStylizedIntegration.AttachWaterObject(go, mf, mr, mat);

            bool logDiag = config != null && (config.debugLogs || config.debugHydrologyNetwork);
            if (riverIndex > 0 && config != null && config.riverDendriticNetworkEnabled && logDiag && cellSpaceLine != null)
            {
                LogRiverConfluenceVisualAudit(
                    config,
                    grid,
                    riverIndex,
                    0,
                    cellSpaceLine,
                    cellSpaceLine.Count - 1,
                    hasFlatCap: !config.riverSurfaceSkipTributaryConfluenceCap,
                    hasRectPatch: false);
            }

            if (riverIndex > 0 &&
                !(grid != null && config != null && config.uwpOwnedVisualPolicy && grid.RiverVisualSurfaceCacheFrozen) &&
                TryCullRiverSurfaceFragmentAfterBuild(
                    go,
                    mesh,
                    grid,
                    config,
                    riverIndex,
                    objectName,
                    cellHintOk ? cellSpaceLine : null,
                    cellSizeWorld,
                    logDiag,
                    out bool fragmentCulled))
            {
                outVerts = 0;
                outTris = 0;
                return false;
            }

            if (config != null && WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
            {
                AttachWebFusionCenterSpline(
                    go.transform,
                    center,
                    halfWidthWorld,
                    cellSpaceLine,
                    grid,
                    config,
                    riverIndex,
                    cellSizeWorld);
            }

            if (logDiag)
            {
                var bounds = mesh.bounds;
                int nearCells = Mathf.Max(1, config.riverVisualFinalCleanupNearRiverCells);
                WaterMeshBuilder.ComputeWaterVisualBoundsMaskStats(
                    grid,
                    bounds,
                    nearCells,
                    out int intersectsMask,
                    out int nearMaskCells);
                int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
                int nf = WaterMeshBuilder.ComputeNearFordFromWorldBounds(grid, bounds, fordD);
                WaterMeshBuilder.LogWaterVisualObject(
                    config,
                    objectName,
                    "RiverSurface",
                    riverIndex,
                    verts.Count,
                    tris.Count / 3,
                    bounds,
                    intersectsMask,
                    nearMaskCells,
                    nf,
                    riverIndex == 0 ? 1 : 0,
                    riverIndex > 0 ? 1 : 0,
                    0,
                    centerlinePreClipped ? "cache_preclip" : "");
            }

            LastVertexSum += verts.Count;
            LastTriSum += tris.Count / 3;
            outVerts = verts.Count;
            outTris = tris.Count / 3;
            return true;
        }

        /// <summary>Mismo orden que el strip principal: (a,c,b) y (b,c,d) para normal hacia arriba.</summary>
        static void AddTriStripWinding(List<int> tris, int a, int b, int c)
        {
            tris.Add(a);
            tris.Add(b);
            tris.Add(c);
        }

        static float[] BuildAccumulatedV(List<Vector3> center)
        {
            int n = center.Count;
            var acc = new float[n];
            acc[0] = 0f;
            for (int i = 1; i < n; i++)
            {
                Vector3 d = center[i] - center[i - 1];
                d.y = 0f;
                acc[i] = acc[i - 1] + d.magnitude;
            }

            return acc;
        }

        /// <summary>UV longitudinal estable: avanza por distancia real, sin acelerar/frenar por ancho local.</summary>
        static float[] BuildAccumulatedStableFlowV(List<Vector3> center)
        {
            return BuildAccumulatedV(center);
        }

        /// <summary>UV-V acumulado: en tramos estrechos avanza menos (evita agua “rápida”/estirada).</summary>
        static float[] BuildAccumulatedFlowV(List<Vector3> center, List<float> halfWidths, float refHalfWidthWorld)
        {
            int n = center != null ? center.Count : 0;
            var acc = new float[n];
            if (n < 1)
                return acc;

            refHalfWidthWorld = Mathf.Max(0.25f, refHalfWidthWorld);
            bool useWidth = halfWidths != null && halfWidths.Count == n;
            acc[0] = 0f;
            for (int i = 1; i < n; i++)
            {
                Vector3 d = center[i] - center[i - 1];
                d.y = 0f;
                float seg = d.magnitude;
                float widthMul = 1f;
                if (useWidth)
                {
                    float hw = Mathf.Max(0.02f, (halfWidths[i] + halfWidths[i - 1]) * 0.5f);
                    widthMul = Mathf.Clamp(hw / refHalfWidthWorld, 0.72f, 1.22f);
                }

                acc[i] = acc[i - 1] + seg * widthMul;
            }

            return acc;
        }

        static Vector3 QuadraticBezierXZ(Vector3 a, Vector3 m, Vector3 b, float t)
        {
            float o = 1f - t;
            Vector3 p = o * o * a + 2f * o * t * m + t * t * b;
            p.y = a.y;
            return p;
        }

        static void SmoothLRPass(List<Vector3> left, List<Vector3> right, float strength)
        {
            int n = left.Count;
            if (n < 3)
                return;
            var nl = new List<Vector3>(left);
            var nr = new List<Vector3>(right);
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 al = (left[i - 1] + left[i] + left[i + 1]) / 3f;
                Vector3 ar = (right[i - 1] + right[i] + right[i + 1]) / 3f;
                Vector3 ll = Vector3.Lerp(left[i], al, strength);
                ll.y = left[i].y;
                nl[i] = ll;
                Vector3 rr = Vector3.Lerp(right[i], ar, strength);
                rr.y = right[i].y;
                nr[i] = rr;
            }

            for (int i = 1; i < n - 1; i++)
            {
                left[i] = nl[i];
                right[i] = nr[i];
            }
        }

        static Vector3 TangentNormalize(List<Vector3> path, int i)
        {
            int n = path.Count;
            if (n < 2)
                return Vector3.forward;
            if (i <= 0)
            {
                Vector3 d = path[1] - path[0];
                d.y = 0f;
                return d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.forward;
            }

            if (i >= n - 1)
            {
                Vector3 d = path[n - 1] - path[n - 2];
                d.y = 0f;
                return d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.forward;
            }

            Vector3 t = path[i + 1] - path[i - 1];
            t.y = 0f;
            return t.sqrMagnitude > 1e-12f ? t.normalized : Vector3.forward;
        }

        /// <summary>
        /// Asegura máscara + centerlines cacheadas (misma preparación que el mesh).
        /// </summary>
        public static void BuildRiverVisualSurfaceMask(GridSystem grid, MapGenConfig config, float cellSize)
        {
            if (grid == null || config == null)
                return;
            EnsureRiverVisualSurfaceCache(grid, config);
            if (config.debugLogs || config.debugHydrologyNetwork)
                LogRiverVisualCacheUse("BuildRiverVisualSurfaceMask", grid, -1);
        }

        static int AnchorCellKey(Vector2 p) =>
            (Mathf.Clamp(Mathf.RoundToInt(p.x), -32768, 32767) << 16) ^
            (Mathf.Clamp(Mathf.RoundToInt(p.y), -32768, 32767) & 0xffff);

        static void AddAnchorKey(HashSet<int> keys, Vector2 p, List<Vector2Int> list)
        {
            int k = AnchorCellKey(p);
            if (keys.Add(k))
                list.Add(new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y)));
        }

        static HashSet<int> BuildLockedAnchorKeys(
            GridSystem grid,
            List<Vector2> rawFunctional,
            int riverIndex,
            MapGenConfig config,
            out List<Vector2Int> lockedList,
            out int fordAnchorCount,
            out float fordMaxDistance)
        {
            lockedList = new List<Vector2Int>();
            fordAnchorCount = 0;
            fordMaxDistance = 0f;
            var keys = new HashSet<int>();
            if (rawFunctional == null || rawFunctional.Count < 2)
                return keys;

            AddAnchorKey(keys, rawFunctional[0], lockedList);
            AddAnchorKey(keys, rawFunctional[rawFunctional.Count - 1], lockedList);
            for (int i = 0; i < rawFunctional.Count; i++)
                AddAnchorKey(keys, rawFunctional[i], lockedList);

            for (int i = 1; i < rawFunctional.Count - 1; i++)
            {
                Vector2 a = rawFunctional[i - 1];
                Vector2 b = rawFunctional[i];
                Vector2 c = rawFunctional[i + 1];
                Vector2 d0 = b - a;
                Vector2 d1 = c - b;
                if (d0.sqrMagnitude < 1e-10f || d1.sqrMagnitude < 1e-10f)
                    continue;
                float dot = Mathf.Clamp(Vector2.Dot(d0.normalized, d1.normalized), -1f, 1f);
                if (Vector2.Angle(d0, d1) >= Mathf.Max(35f, config.riverSurfaceSharpBendAngleDeg - 8f))
                    AddAnchorKey(keys, b, lockedList);
            }

            if (riverIndex > 0 && grid.RiverCenterlinesCellSpace != null)
            {
                var joinKeys = BuildJoinProximityCellKeys(grid.RiverCenterlinesCellSpace, riverIndex, grid.Width, grid.Height);
                for (int i = 0; i < rawFunctional.Count; i++)
                {
                    int cx = Mathf.Clamp(Mathf.FloorToInt(rawFunctional[i].x), 0, grid.Width - 1);
                    int cy = Mathf.Clamp(Mathf.FloorToInt(rawFunctional[i].y), 0, grid.Height - 1);
                    if (joinKeys.Contains(PackCellKey(cx, cy)))
                        AddAnchorKey(keys, rawFunctional[i], lockedList);
                }
            }

            int fordSearch = Mathf.Max(2, config.riverVisualFordKeepDistanceCells + 2);
            for (int i = 0; i < rawFunctional.Count - 1; i++)
            {
                Vector2 a = rawFunctional[i];
                Vector2 b = rawFunctional[i + 1];
                int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x)) - fordSearch, 0, grid.Width - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x)) + fordSearch, 0, grid.Width - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y)) - fordSearch, 0, grid.Height - 1);
                int z1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y)) + fordSearch, 0, grid.Height - 1);
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        if (!grid.GetCell(x, z).riverFord)
                            continue;
                        Vector2 cellCenter = new Vector2(x + 0.5f, z + 0.5f);
                        float dSeg = DistancePointToOpenSegment2D(cellCenter, a, b);
                        fordMaxDistance = Mathf.Max(fordMaxDistance, dSeg);
                        if (dSeg <= fordSearch + 0.5f)
                        {
                            Vector2 onPath = ClosestPointOnOpenSegment2D(cellCenter, a, b);
                            AddAnchorKey(keys, onPath, lockedList);
                            fordAnchorCount++;
                        }
                    }
                }
            }

            return keys;
        }

        static Vector2 ClosestPointOnOpenSegment2D(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float den = Vector2.Dot(ab, ab);
            if (den < 1e-10f)
                return a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / den);
            return a + ab * t;
        }

        static float DistancePointToPolyline2D(Vector2 p, List<Vector2> poly)
        {
            if (poly == null || poly.Count < 2)
                return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < poly.Count - 1; i++)
                best = Mathf.Min(best, DistancePointToOpenSegment2D(p, poly[i], poly[i + 1]));
            return best;
        }

        static List<Vector2> ProcessCenterlineCellSpaceAnchored(
            List<Vector2> cellPath,
            HashSet<int> lockedKeys,
            MapGenConfig config,
            out int afterColinear,
            out int afterSmooth,
            out int afterResample)
        {
            afterColinear = afterSmooth = afterResample = 0;
            var pts = new List<Vector2>(cellPath);
            pts = DedupeConsecutiveCell(pts, DedupeCellEps);
            pts = RemoveNearNullSegmentsCell(pts, MinSegmentCellEps);
            pts = RemoveCollinearPointsCellAnchored(pts, CollinearDotThreshold, lockedKeys);
            afterColinear = pts != null ? pts.Count : 0;
            if (pts == null || pts.Count < 2)
                return null;

            pts = InsertSharpBendMidpointsCell(pts, config.riverSurfaceSharpBendAngleDeg);

            int chaikinPasses = Mathf.Clamp(config.riverSurfaceChaikinPasses, 0, 2);
            if (chaikinPasses > 0)
            {
                var chaikin = ChaikinOpenCellAnchored(pts, chaikinPasses, lockedKeys);
                if (!PolylineSelfIntersectsXZCell(chaikin))
                    pts = chaikin;
            }

            afterSmooth = pts.Count;
            int pathCells = cellPath.Count;
            int maxPts = Mathf.Max(8, Mathf.CeilToInt(pathCells * Mathf.Max(1.02f, config.riverSurfaceMaxVisualPointRatio)));
            float spacing = Mathf.Max(0.08f, config.riverSurfaceSampleSpacingCells);
            pts = ResampleUniformSpacingCellAnchored(pts, spacing, maxPts, lockedKeys);
            afterResample = pts != null ? pts.Count : 0;
            return pts;
        }

        static List<Vector2> RemoveCollinearPointsCellAnchored(List<Vector2> pts, float dotThresh, HashSet<int> lockedKeys)
        {
            if (pts == null || pts.Count < 3)
                return pts;
            var r = new List<Vector2>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                if (lockedKeys.Contains(AnchorCellKey(pts[i])))
                {
                    r.Add(pts[i]);
                    continue;
                }

                Vector2 a = r[r.Count - 1];
                Vector2 b = pts[i];
                Vector2 c = pts[i + 1];
                Vector2 d0 = b - a;
                Vector2 d1 = c - b;
                if (d0.sqrMagnitude < 1e-12f || d1.sqrMagnitude < 1e-12f)
                {
                    r.Add(b);
                    continue;
                }

                float dot = Mathf.Clamp(Vector2.Dot(d0.normalized, d1.normalized), -1f, 1f);
                if (dot > dotThresh)
                    continue;
                r.Add(b);
            }

            r.Add(pts[pts.Count - 1]);
            return r;
        }

        static List<Vector2> ChaikinOpenCellAnchored(List<Vector2> pts, int passes, HashSet<int> lockedKeys)
        {
            var cur = new List<Vector2>(pts);
            for (int p = 0; p < passes && cur.Count >= 3; p++)
            {
                var next = new List<Vector2> { cur[0] };
                for (int i = 0; i < cur.Count - 1; i++)
                {
                    Vector2 a = cur[i];
                    Vector2 b = cur[i + 1];
                    if (lockedKeys.Contains(AnchorCellKey(a)) || lockedKeys.Contains(AnchorCellKey(b)))
                    {
                        if (i > 0 || next[next.Count - 1] != a)
                            next.Add(a);
                        next.Add(b);
                        continue;
                    }

                    Vector2 q = 0.75f * a + 0.25f * b;
                    Vector2 r = 0.25f * a + 0.75f * b;
                    next.Add(q);
                    next.Add(r);
                }

                if (next[next.Count - 1] != cur[cur.Count - 1])
                    next.Add(cur[cur.Count - 1]);
                cur = next;
            }

            return cur;
        }

        static List<Vector2> ResampleUniformSpacingCellAnchored(
            List<Vector2> pts,
            float spacing,
            int maxPts,
            HashSet<int> lockedKeys)
        {
            if (pts == null || pts.Count < 2)
                return pts;
            float total = PolylineLengthCellSpace(pts);
            if (total < spacing * 0.5f)
                return new List<Vector2>(pts);
            int target = Mathf.Clamp(Mathf.FloorToInt(total / spacing) + 1, 2, maxPts);
            var result = new List<Vector2>(target + lockedKeys.Count);
            var forced = new List<Vector2>();
            for (int i = 0; i < pts.Count; i++)
            {
                if (lockedKeys.Contains(AnchorCellKey(pts[i])))
                    forced.Add(pts[i]);
            }

            float step = total / (target - 1);
            float acc = 0f;
            int seg = 0;
            result.Add(pts[0]);
            for (int k = 1; k < target - 1; k++)
            {
                float want = k * step;
                while (seg < pts.Count - 1)
                {
                    float segLen = Vector2.Distance(pts[seg], pts[seg + 1]);
                    if (acc + segLen >= want)
                    {
                        float t = segLen > 1e-8f ? (want - acc) / segLen : 0f;
                        result.Add(Vector2.Lerp(pts[seg], pts[seg + 1], t));
                        break;
                    }

                    acc += segLen;
                    seg++;
                }
            }

            result.Add(pts[pts.Count - 1]);
            for (int i = 0; i < forced.Count; i++)
            {
                Vector2 f = forced[i];
                bool exists = false;
                for (int j = 0; j < result.Count; j++)
                {
                    if ((result[j] - f).sqrMagnitude < DedupeCellEps * DedupeCellEps)
                    {
                        exists = true;
                        result[j] = f;
                        break;
                    }
                }

                if (!exists)
                    InsertPointSortedAlongPolyline(result, f);
            }

            return result;
        }

        static void InsertPointSortedAlongPolyline(List<Vector2> poly, Vector2 p)
        {
            if (poly == null || poly.Count < 2)
            {
                poly?.Add(p);
                return;
            }

            float bestT = 0f;
            int bestSeg = 0;
            float bestD = float.MaxValue;
            float acc = 0f;
            for (int i = 0; i < poly.Count - 1; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[i + 1];
                Vector2 ab = b - a;
                float len = ab.magnitude;
                float t = len > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / (len * len)) : 0f;
                Vector2 q = a + ab * t;
                float d = (p - q).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    bestSeg = i;
                    bestT = acc + t * len;
                }

                acc += len;
            }

            for (int i = bestSeg + 1; i < poly.Count; i++)
            {
                float segStart = 0f;
                for (int s = 0; s < i; s++)
                    segStart += Vector2.Distance(poly[s], poly[s + 1]);
                if (bestT <= segStart + 1e-5f)
                {
                    poly.Insert(i, p);
                    return;
                }
            }

            poly.Insert(bestSeg + 1, p);
        }

        static void EnforcePathFitToFunctional(
            List<Vector2> processed,
            List<Vector2> functional,
            HashSet<int> lockedKeys,
            float maxDevCells,
            out float maxDev,
            out float avgDev,
            out int reverted)
        {
            maxDev = 0f;
            avgDev = 0f;
            reverted = 0;
            if (processed == null || functional == null || processed.Count == 0)
                return;
            float sum = 0f;
            for (int i = 0; i < processed.Count; i++)
            {
                if (lockedKeys.Contains(AnchorCellKey(processed[i])))
                {
                    float dLocked = DistancePointToPolyline2D(processed[i], functional);
                    maxDev = Mathf.Max(maxDev, dLocked);
                    sum += dLocked;
                    continue;
                }

                float d = DistancePointToPolyline2D(processed[i], functional);
                maxDev = Mathf.Max(maxDev, d);
                sum += d;
                if (d > maxDevCells)
                {
                    processed[i] = ClosestPointOnPolyline2D(processed[i], functional);
                    reverted++;
                    d = DistancePointToPolyline2D(processed[i], functional);
                    maxDev = Mathf.Max(maxDev, d);
                }
            }

            avgDev = sum / Mathf.Max(1, processed.Count);
        }

        static Vector2 ClosestPointOnPolyline2D(Vector2 p, List<Vector2> poly)
        {
            if (poly == null || poly.Count == 0)
                return p;
            if (poly.Count == 1)
                return poly[0];
            float bestD = float.MaxValue;
            Vector2 best = poly[0];
            for (int i = 0; i < poly.Count - 1; i++)
            {
                Vector2 q = ClosestPointOnOpenSegment2D(p, poly[i], poly[i + 1]);
                float d = (p - q).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = q;
                }
            }

            return best;
        }

        static bool ClipSegmentToPlayableRect(
            Vector2 a,
            Vector2 b,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            out Vector2 ca,
            out Vector2 cb)
        {
            ca = a;
            cb = b;
            float t0 = 0f;
            float t1 = 1f;
            Vector2 d = b - a;
            bool Clip(float p, float q)
            {
                if (Mathf.Abs(p) < 1e-8f)
                    return q >= 0f;
                float r = q / p;
                if (p < 0f)
                {
                    if (r > t1)
                        return false;
                    if (r > t0)
                        t0 = r;
                }
                else
                {
                    if (r < t0)
                        return false;
                    if (r < t1)
                        t1 = r;
                }

                return true;
            }

            if (!Clip(-d.x, a.x - minX))
                return false;
            if (!Clip(d.x, maxX - a.x))
                return false;
            if (!Clip(-d.y, a.y - minZ))
                return false;
            if (!Clip(d.y, maxZ - a.y))
                return false;

            ca = a + d * t0;
            cb = a + d * t1;
            return t1 > t0 + 1e-6f;
        }

        static List<Vector2> PreClipCenterlineCellSpace(List<Vector2> cellPath, int w, int h, out bool startClipped, out bool endClipped)
        {
            startClipped = endClipped = false;
            if (cellPath == null || cellPath.Count < 2)
                return cellPath;
            float minX = 0.5f;
            float maxX = (w - 1) + 0.5f;
            float minZ = 0.5f;
            float maxZ = (h - 1) + 0.5f;

            bool Inside(Vector2 p) => p.x >= minX - 1e-4f && p.x <= maxX + 1e-4f && p.y >= minZ - 1e-4f && p.y <= maxZ + 1e-4f;
            startClipped = !Inside(cellPath[0]);
            endClipped = !Inside(cellPath[cellPath.Count - 1]);

            var clipped = new List<Vector2>(cellPath.Count + 4);
            for (int i = 0; i < cellPath.Count - 1; i++)
            {
                Vector2 a = cellPath[i];
                Vector2 b = cellPath[i + 1];
                if (!ClipSegmentToPlayableRect(a, b, minX, maxX, minZ, maxZ, out Vector2 ca, out Vector2 cb))
                    continue;
                if (clipped.Count == 0 || (clipped[clipped.Count - 1] - ca).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                    clipped.Add(ca);
                if ((ca - cb).sqrMagnitude > DedupeCellEps * DedupeCellEps)
                    clipped.Add(cb);
            }

            if (clipped.Count < 2)
            {
                clipped.Clear();
                for (int i = 0; i < cellPath.Count; i++)
                {
                    Vector2 c = cellPath[i];
                    c.x = Mathf.Clamp(c.x, minX, maxX);
                    c.y = Mathf.Clamp(c.y, minZ, maxZ);
                    clipped.Add(c);
                }
            }

            return clipped.Count >= 2 ? clipped : cellPath;
        }

        static void ApplyFordWidthDampening(GridSystem grid, List<Vector2> cellPath, List<float> halfWidths, MapGenConfig config)
        {
            if (grid == null || cellPath == null || halfWidths == null || cellPath.Count != halfWidths.Count)
                return;
            int fordD = Mathf.Max(1, config.riverVisualFordKeepDistanceCells);
            for (int i = 0; i < cellPath.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(cellPath[i].x), 0, grid.Width - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(cellPath[i].y), 0, grid.Height - 1);
                if (WaterMeshBuilder.GridCellNearFordRiverChebyshev(grid, cx, cy, fordD))
                    halfWidths[i] = Mathf.Max(0.02f, halfWidths[i] * 0.94f);
            }
        }

        static int CullTrianglesOutsideVisualMask(
            List<Vector3> verts,
            List<int> tris,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            Vector3 origin,
            float cellSize)
        {
            if (verts == null || tris == null || tris.Count < 6 || grid?.RiverVisualSurfaceMask == null)
                return 0;
            bool[,] mask = grid.RiverVisualSurfaceMask;
            int gw = grid.Width;
            int gh = grid.Height;
            int margin = config != null ? Mathf.Clamp(config.riverVisualTriangleCullMaskMarginCells, 0, 3) : 1;
            int fordD = Mathf.Max(1, config != null ? config.riverVisualFordKeepDistanceCells : 5);
            float invCs = 1f / Mathf.Max(1e-5f, cellSize);
            int before = tris.Count / 3;
            var kept = new List<int>(tris.Count);

            bool NearMask(int cx, int cz)
            {
                for (int dz = -margin; dz <= margin; dz++)
                {
                    for (int dx = -margin; dx <= margin; dx++)
                    {
                        int nx = cx + dx;
                        int nz = cz + dz;
                        if ((uint)nx < (uint)gw && (uint)nz < (uint)gh && mask[nx, nz])
                            return true;
                    }
                }

                return false;
            }

            for (int t = 0; t < tris.Count; t += 3)
            {
                int i0 = tris[t];
                int i1 = tris[t + 1];
                int i2 = tris[t + 2];
                if ((uint)i0 >= (uint)verts.Count || (uint)i1 >= (uint)verts.Count || (uint)i2 >= (uint)verts.Count)
                    continue;
                Vector3 c = (verts[i0] + verts[i1] + verts[i2]) / 3f;
                int cx = Mathf.Clamp(Mathf.FloorToInt((c.x - origin.x) * invCs), 0, gw - 1);
                int cz = Mathf.Clamp(Mathf.FloorToInt((c.z - origin.z) * invCs), 0, gh - 1);
                if (mask[cx, cz] || NearMask(cx, cz))
                {
                    kept.Add(i0);
                    kept.Add(i1);
                    kept.Add(i2);
                    continue;
                }

                if (WaterMeshBuilder.GridCellNearFordRiverChebyshev(grid, cx, cz, fordD))
                {
                    kept.Add(i0);
                    kept.Add(i1);
                    kept.Add(i2);
                }
            }

            int culled = before - kept.Count / 3;
            tris.Clear();
            tris.AddRange(kept);
            return culled;
        }

        static bool TryBuildMainRiverCorridorSampler(
            GridSystem grid,
            MapGenConfig config,
            float cellSize,
            out MainRiverCorridorSampler sampler)
        {
            return TryBuildRiverCorridorSampler(grid, config, cellSize, 0, out sampler);
        }

        static bool TryBuildRiverCorridorSampler(
            GridSystem grid,
            MapGenConfig config,
            float cellSize,
            int riverIndex,
            out MainRiverCorridorSampler sampler)
        {
            sampler = default;
            if (grid == null || riverIndex < 0)
                return false;

            // Preferir Final visual (meander) para que Snap/Tuck/Append headwater alineen con máscara/carve del receptor.
            List<Vector2> line = null;
            if (grid.LakeFirstWaterGraph != null &&
                grid.LakeFirstWaterGraph.TryGetFinalCenterline(riverIndex, out List<Vector2> finalCl) &&
                finalCl != null && finalCl.Count >= 2)
            {
                line = finalCl;
            }
            else if (grid.RiverVisualSurfaces != null &&
                     riverIndex < grid.RiverVisualSurfaces.Count &&
                     grid.RiverVisualSurfaces[riverIndex].FinalCenterlineCells != null &&
                     grid.RiverVisualSurfaces[riverIndex].FinalCenterlineCells.Count >= 2)
            {
                line = grid.RiverVisualSurfaces[riverIndex].FinalCenterlineCells;
            }
            else if (grid.RiverCenterlinesCellSpace != null &&
                     riverIndex < grid.RiverCenterlinesCellSpace.Count)
            {
                line = grid.RiverCenterlinesCellSpace[riverIndex];
            }

            if (line == null || line.Count < 2)
                return false;

            float halfCells = ResolveRiverRibbonHalfWidthCells(config, riverIndex);
            float normalMul = config != null
                ? (config.uwpOwnedVisualPolicy
                    ? Mathf.Clamp(config.riverSurfaceVisualNormalWidthMul, 0.85f, 3f)
                    : Mathf.Clamp(config.riverSurfaceVisualNormalWidthMul, 1.25f, 3f))
                : 2f;
            float coreScale = config != null && config.uwpOwnedVisualPolicy ? 0.42f : 0.68f;
            float radius = Mathf.Max(0.9f, halfCells * normalMul * 0.82f);
            float coreRadius = Mathf.Max(0.65f, halfCells * normalMul * coreScale);
            sampler = new MainRiverCorridorSampler
            {
                Line = line,
                RadiusCells = radius,
                RadiusSq = radius * radius,
                CoreRadiusCells = coreRadius,
                CoreRadiusSq = coreRadius * coreRadius
            };
            return true;
        }

        static bool TryResolveTributaryJoinOnParentRiver(
            GridSystem grid,
            int riverIndex,
            List<Vector2> cellProcessed,
            MapGenConfig config,
            int parentRiverIndex,
            out Vector2 join)
        {
            join = default;
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || config == null ||
                parentRiverIndex < 0 || grid.RiverCenterlinesCellSpace == null ||
                parentRiverIndex >= grid.RiverCenterlinesCellSpace.Count)
                return false;

            if (TryGetTributaryConfluenceCell(grid, riverIndex, out join, out _))
                return true;

            List<Vector2> parentLine = null;
            if (grid.LakeFirstWaterGraph != null &&
                grid.LakeFirstWaterGraph.TryGetFinalCenterline(parentRiverIndex, out List<Vector2> parentFinal) &&
                parentFinal != null && parentFinal.Count >= 2)
                parentLine = parentFinal;
            else if (grid.RiverVisualSurfaces != null &&
                     parentRiverIndex < grid.RiverVisualSurfaces.Count &&
                     grid.RiverVisualSurfaces[parentRiverIndex].FinalCenterlineCells != null &&
                     grid.RiverVisualSurfaces[parentRiverIndex].FinalCenterlineCells.Count >= 2)
                parentLine = grid.RiverVisualSurfaces[parentRiverIndex].FinalCenterlineCells;
            else
                parentLine = grid.RiverCenterlinesCellSpace[parentRiverIndex];
            if (parentLine == null || parentLine.Count < 2)
                return false;

            Vector2 refPt = cellProcessed[cellProcessed.Count - 1];
            int bestSeg = 0;
            float bestSegD = float.MaxValue;
            for (int i = 0; i < parentLine.Count - 1; i++)
            {
                float d = DistancePointToOpenSegment2D(refPt, parentLine[i], parentLine[i + 1]);
                if (d < bestSegD)
                {
                    bestSegD = d;
                    bestSeg = i;
                }
            }

            float maxGap = ResolveConfluenceReachMaxGapCells(config);
            if (bestSegD > maxGap)
                return false;

            int win = 5;
            int seg0 = Mathf.Max(0, bestSeg - win);
            int seg1 = Mathf.Min(parentLine.Count - 2, bestSeg + win);
            float bestSq = float.MaxValue;
            join = parentLine[bestSeg];
            for (int i = seg0; i <= seg1; i++)
            {
                Vector2 q = ClosestPointOnOpenSegment2D(refPt, parentLine[i], parentLine[i + 1]);
                float sq = (refPt - q).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    join = q;
                }
            }

            return true;
        }

        static void SnapTributaryCenterlineToParentRiver(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config,
            int parentRiverIndex,
            int endpointIndex = -1)
        {
            if (grid == null || cellProcessed == null || cellProcessed.Count < 2 || riverIndex <= 0 || config == null)
                return;
            if (!config.riverConfluenceEnabled)
                return;

            if (endpointIndex < 0)
                endpointIndex = cellProcessed.Count - 1;
            if (endpointIndex < 0 || endpointIndex >= cellProcessed.Count)
                return;

            if (!TryResolveTributaryJoinOnParentRiver(
                    grid, riverIndex, cellProcessed, config, parentRiverIndex, out Vector2 join))
                return;

            Vector2 end = cellProcessed[endpointIndex];
            float gap = Vector2.Distance(end, join);
            if (gap < 0.08f)
            {
                cellProcessed[endpointIndex] = join;
                return;
            }

            float maxGap = ResolveConfluenceReachMaxGapCells(config);
            if (gap > maxGap)
                return;

            int neighbor = endpointIndex == 0 ? 1 : endpointIndex - 1;
            if (cellProcessed.Count >= 2 && neighbor >= 0 && neighbor < cellProcessed.Count &&
                WouldFoldBackBridge(cellProcessed[neighbor], end, join))
                return;

            cellProcessed[endpointIndex] = join;
        }

        static void TuckTributaryMouthIntoParentRiver(
            GridSystem grid,
            List<Vector2> cellProcessed,
            int riverIndex,
            MapGenConfig config,
            int parentRiverIndex,
            int endpointIndex = -1)
        {
            if (grid == null || config == null || cellProcessed == null || cellProcessed.Count < 2 || riverIndex <= 0)
                return;
            if (endpointIndex < 0)
                endpointIndex = cellProcessed.Count - 1;
            if (endpointIndex < 0 || endpointIndex >= cellProcessed.Count)
                return;

            if (!TryBuildRiverCorridorSampler(
                    grid, config, grid.CellSizeWorld, parentRiverIndex, out MainRiverCorridorSampler sampler))
                return;

            Vector2 end = cellProcessed[endpointIndex];
            float bestDist = float.MaxValue;
            int bestSeg = 0;
            Vector2 bestJoin = end;
            for (int i = 0; i < sampler.Line.Count - 1; i++)
            {
                Vector2 q = ClosestPointOnOpenSegment2D(end, sampler.Line[i], sampler.Line[i + 1]);
                float d = (end - q).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestJoin = q;
                    bestSeg = i;
                }
            }

            float maxJoin = ResolveConfluenceReachMaxGapCells(config);
            if (bestDist > maxJoin * maxJoin)
                return;

            if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
            {
                cellProcessed[endpointIndex] = bestJoin;
                return;
            }

            Vector2 tangent = sampler.Line[bestSeg + 1] - sampler.Line[bestSeg];
            if (tangent.sqrMagnitude < 1e-8f)
                return;
            tangent.Normalize();

            int neighbor = endpointIndex == 0 ? 1 : endpointIndex - 1;
            if (cellProcessed.Count >= 2 && neighbor >= 0 && neighbor < cellProcessed.Count)
            {
                Vector2 approach = (end - cellProcessed[neighbor]).normalized;
                if (Vector2.Dot(approach, tangent) < 0f)
                    tangent = -tangent;
            }

            float tuck = Mathf.Clamp(sampler.CoreRadiusCells * 0.38f, 0.45f, 1.6f);
            Vector2 tucked = bestJoin + tangent * tuck;
            if (cellProcessed.Count >= 2 && neighbor >= 0 && neighbor < cellProcessed.Count &&
                WouldFoldBackBridge(cellProcessed[neighbor], end, tucked))
                return;

            cellProcessed[endpointIndex] = tucked;
        }

        static void AppendLakeFirstHeadwaterCenterlineTowardReceiverRiver(
            GridSystem grid,
            int riverIndex,
            List<Vector2> line,
            MapGenConfig config,
            int receiverRiverIndex)
        {
            if (grid == null || line == null || line.Count < 2 || config == null || receiverRiverIndex <= 0 ||
                UwpTributaryOriginUtility.GetOrigin(grid, riverIndex) != UwpTributaryOriginKind.HeadwaterFeeder)
                return;
            if (!TryBuildRiverCorridorSampler(
                    grid, config, grid.CellSizeWorld, receiverRiverIndex, out MainRiverCorridorSampler sampler))
                return;

            int mainIdx = line.Count - 1;
            Vector2 mouth = line[mainIdx];
            if (TryResolveTributaryJoinOnParentRiver(grid, riverIndex, line, config, receiverRiverIndex, out Vector2 join))
            {
                line[mainIdx] = join;
                mouth = join;
            }

            float bestDist = float.MaxValue;
            int bestSeg = 0;
            Vector2 bestOn = mouth;
            for (int i = 0; i < sampler.Line.Count - 1; i++)
            {
                Vector2 q = ClosestPointOnOpenSegment2D(mouth, sampler.Line[i], sampler.Line[i + 1]);
                float d = (mouth - q).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestOn = q;
                    bestSeg = i;
                }
            }

            float maxGap = ResolveConfluenceReachMaxGapCells(config);
            if (bestDist > maxGap * maxGap)
                return;

            // Puente densificado mouth→receptor (evita saltos de carve/máscara).
            float gapCells = Mathf.Sqrt(bestDist);
            if (gapCells > 0.35f)
            {
                int steps = Mathf.Clamp(Mathf.CeilToInt(gapCells / 0.45f), 2, 12);
                for (int s = 1; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    line.Add(Vector2.Lerp(mouth, bestOn, t));
                }

                mainIdx = line.Count - 1;
                mouth = bestOn;
            }
            else if ((mouth - bestOn).sqrMagnitude > 1e-6f)
            {
                line[mainIdx] = bestOn;
                mouth = bestOn;
            }

            float ingress = Mathf.Clamp(sampler.CoreRadiusCells * 0.75f, 1.2f, 3.2f);
            var ingressPts = new List<Vector2>(5);
            int ingressSeg = bestSeg;
            Vector2 ingressCursor = bestOn;
            float ingressStep = ingress / 4f;
            for (int k = 1; k <= 4; k++)
            {
                float remaining = ingressStep;
                while (remaining > 1e-4f && ingressSeg < sampler.Line.Count - 1)
                {
                    Vector2 segEnd = sampler.Line[ingressSeg + 1];
                    float segRemaining = Vector2.Distance(ingressCursor, segEnd);
                    if (segRemaining <= 1e-4f)
                    {
                        ingressCursor = segEnd;
                        ingressSeg++;
                        continue;
                    }

                    if (remaining <= segRemaining)
                    {
                        ingressCursor = Vector2.Lerp(
                            ingressCursor, segEnd, remaining / segRemaining);
                        remaining = 0f;
                    }
                    else
                    {
                        remaining -= segRemaining;
                        ingressCursor = segEnd;
                        ingressSeg++;
                    }
                }

                if (ingressPts.Count == 0 ||
                    (ingressCursor - ingressPts[ingressPts.Count - 1]).sqrMagnitude > 1e-6f)
                {
                    ingressPts.Add(ingressCursor);
                }

                if (ingressSeg >= sampler.Line.Count - 1 && remaining > 1e-4f)
                    break;
            }

            if (ingressPts.Count == 0)
                return;

            line.AddRange(ingressPts);
        }

        static float DistanceSqToPolylineCellSpace(Vector2 p, List<Vector2> line)
        {
            if (line == null || line.Count == 0)
                return float.MaxValue;
            if (line.Count == 1)
                return (p - line[0]).sqrMagnitude;

            float best = float.MaxValue;
            for (int i = 0; i < line.Count - 1; i++)
            {
                Vector2 a = line[i];
                Vector2 b = line[i + 1];
                Vector2 ab = b - a;
                float lenSq = ab.sqrMagnitude;
                Vector2 q = lenSq < 1e-8f ? a : a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
                float d = (p - q).sqrMagnitude;
                if (d < best)
                    best = d;
            }

            return best;
        }

        static int CullTributaryTrianglesInsideMainRiverSurface(
            List<Vector3> verts,
            List<int> tris,
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            Vector3 origin,
            float cellSize)
        {
            if (riverIndex <= 0 || verts == null || tris == null || tris.Count < 6)
                return 0;
            bool lakeSpill = UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex);
            // Emissary legacy: no cull. LakeSpill Lake First: sí (cinta visual Main).
            if (IsLakeEmissaryRiverIndex(grid, riverIndex) && !lakeSpill)
                return 0;
            if (!TryBuildMainRiverCorridorSampler(grid, config, cellSize, out MainRiverCorridorSampler sampler))
                return 0;

            float cullRadius = sampler.CoreRadiusCells;
            if (lakeSpill)
            {
                // Solo núcleo del troncal (evitar comer la aproximación spill→orilla).
                cullRadius = Mathf.Max(0.55f, sampler.CoreRadiusCells * 0.82f);
            }
            float cullRadiusSq = cullRadius * cullRadius;

            float invCs = 1f / Mathf.Max(1e-5f, cellSize);
            int before = tris.Count / 3;
            var kept = new List<int>(tris.Count);
            for (int t = 0; t < tris.Count; t += 3)
            {
                int i0 = tris[t];
                int i1 = tris[t + 1];
                int i2 = tris[t + 2];
                if ((uint)i0 >= (uint)verts.Count || (uint)i1 >= (uint)verts.Count || (uint)i2 >= (uint)verts.Count)
                    continue;

                Vector2 p0 = CellSpaceFromWorld(verts[i0], origin, invCs);
                Vector2 p1 = CellSpaceFromWorld(verts[i1], origin, invCs);
                Vector2 p2 = CellSpaceFromWorld(verts[i2], origin, invCs);
                Vector2 pc = (p0 + p1 + p2) / 3f;
                float distSqCentroid = DistanceSqToPolylineCellSpace(pc, sampler.Line);

                // Solo quitar triángulos cuyo centro cae bajo el núcleo del troncal.
                if (distSqCentroid <= cullRadiusSq)
                    continue;

                kept.Add(i0);
                kept.Add(i1);
                kept.Add(i2);
            }

            int culled = before - kept.Count / 3;
            if (culled > 0)
            {
                tris.Clear();
                tris.AddRange(kept);
            }

            return culled;
        }

        static Vector2 CellSpaceFromWorld(Vector3 world, Vector3 origin, float invCellSize)
        {
            return new Vector2(
                (world.x - origin.x) * invCellSize,
                (world.z - origin.z) * invCellSize);
        }

        static bool TryGetTributaryConfluenceCell(
            GridSystem grid,
            int riverIndex,
            out Vector2 confluenceCell,
            out int mergeRadiusCells)
        {
            confluenceCell = default;
            mergeRadiusCells = 0;
            if (grid?.RiverConfluences == null)
                return false;

            for (int i = 0; i < grid.RiverConfluences.Count; i++)
            {
                RiverConfluenceNode node = grid.RiverConfluences[i];
                if (!node.Valid || node.TributaryRiverIndex != riverIndex)
                    continue;

                confluenceCell = new Vector2(node.Cell.x + 0.5f, node.Cell.y + 0.5f);
                mergeRadiusCells = Mathf.Max(0, node.MergeRadiusCells);
                return true;
            }

            return false;
        }

        public static void LogRiverVisualCacheUse(string consumer, GridSystem grid, int riverIndex)
        {
            if (grid == null || !grid.RiverVisualSurfacesBuilt)
                return;
            if (grid.RiverVisualSurfaces == null || grid.RiverVisualSurfaces.Count == 0)
                return;
            Debug.Log(
                $"[RiverVisualCacheUse] consumer={consumer} riverIndex={riverIndex} usedCachedMask=1 usedCachedCenterline=1 " +
                $"surfaces={grid.RiverVisualSurfaces.Count}");
        }

        static void BuildUwpDegenerateTributaryPrepass(
            GridSystem grid,
            MapGenConfig config,
            int w,
            int h,
            float cellSize,
            bool logDetail,
            out HashSet<int> degenerateIndices,
            out Dictionary<int, List<Vector2>> resolvedFinalByRiverIndex)
        {
            degenerateIndices = new HashSet<int>();
            resolvedFinalByRiverIndex = new Dictionary<int, List<Vector2>>();
            if (grid?.RiverCenterlinesCellSpace == null || config == null || !config.uwpOwnedVisualPolicy)
                return;

            for (int riverIndex = 1; riverIndex < grid.RiverCenterlinesCellSpace.Count; riverIndex++)
            {
                var rawPath = grid.RiverCenterlinesCellSpace[riverIndex];
                if (rawPath == null || rawPath.Count < 2)
                    continue;

                if (!TryResolveRiverVisualFinalCenterlineCells(
                        grid,
                        config,
                        riverIndex,
                        rawPath,
                        w,
                        h,
                        cellSize,
                        logDetail,
                        null,
                        out List<Vector2> finalCenterline,
                        out _))
                    continue;

                resolvedFinalByRiverIndex[riverIndex] = finalCenterline;
                if (!IsUwpDegenerateTributary(finalCenterline, cellSize, grid, riverIndex))
                    continue;

                degenerateIndices.Add(riverIndex);
                float lengthWorld = ComputePolylineLengthCells(finalCenterline) * Mathf.Max(0.01f, cellSize);
                Debug.Log(
                    $"[UWP_DEGENERATE_TRIBUTARY_PREPASS] riverIndex={riverIndex} " +
                    $"predictedFinalPts={finalCenterline.Count} predictedLength={lengthWorld:F2} " +
                    $"reason=degenerate_tributary");
            }
        }

        /// <summary>
        /// Lake-First: mismo contrato degenerado que non-LF, pero resolviendo con
        /// TryResolveLakeFirstFinalCenterlineCells + densify (no el resolve visual legacy).
        /// Primero congela el main (para joins trib), luego predice tributarios skipped
        /// antes del loop de máscara/half-widths.
        /// </summary>
        static void BuildUwpLakeFirstDegenerateTributaryPrepass(
            GridSystem grid,
            MapGenConfig config,
            float cellSize,
            out HashSet<int> degenerateIndices,
            out Dictionary<int, List<Vector2>> resolvedFinalByRiverIndex)
        {
            degenerateIndices = new HashSet<int>();
            resolvedFinalByRiverIndex = new Dictionary<int, List<Vector2>>();
            if (grid?.RiverCenterlinesCellSpace == null || config == null || !config.uwpOwnedVisualPolicy)
                return;
            if (!config.uwpLakeFirstHydrologyPipeline)
                return;

            // Main antes que tributarios: Trim/ingress leen FinalCenterline del troncal.
            if (grid.RiverCenterlinesCellSpace.Count > 0)
            {
                var mainRaw = grid.RiverCenterlinesCellSpace[0];
                if (mainRaw != null && mainRaw.Count >= 2 &&
                    TryResolveLakeFirstFinalCenterlineCells(
                        grid, 0, mainRaw, config, out List<Vector2> mainFinal, out _) &&
                    mainFinal != null && mainFinal.Count >= 2)
                {
                    mainFinal = ResampleUniformSpacingCell(mainFinal, 0.28f, 2048);
                    if (grid.LakeFirstWaterGraph != null &&
                        grid.LakeFirstWaterGraph.FinalCenterlineByRiverIndex.ContainsKey(0))
                    {
                        grid.LakeFirstWaterGraph.FinalCenterlineByRiverIndex[0] =
                            new List<Vector2>(mainFinal);
                    }

                    resolvedFinalByRiverIndex[0] = mainFinal;
                }
            }

            for (int riverIndex = 1; riverIndex < grid.RiverCenterlinesCellSpace.Count; riverIndex++)
            {
                var rawPath = grid.RiverCenterlinesCellSpace[riverIndex];
                if (rawPath == null || rawPath.Count < 2)
                    continue;

                if (!TryResolveLakeFirstFinalCenterlineCells(
                        grid,
                        riverIndex,
                        rawPath,
                        config,
                        out List<Vector2> finalCenterline,
                        out _))
                    continue;

                if (finalCenterline != null && finalCenterline.Count >= 2)
                {
                    float densifyStep = IsLakeFirstHeadwaterFeeder(grid, riverIndex) ? 0.35f : 0.52f;
                    finalCenterline = DensifyLakeFirstCenterlineForMask(
                        finalCenterline,
                        densifyStep,
                        fineStream: IsLakeFirstHeadwaterFeeder(grid, riverIndex));

                    if (grid.LakeFirstWaterGraph != null &&
                        finalCenterline != null &&
                        finalCenterline.Count >= 2)
                    {
                        if (grid.LakeFirstWaterGraph.FinalCenterlineByRiverIndex.ContainsKey(riverIndex))
                            grid.LakeFirstWaterGraph.FinalCenterlineByRiverIndex[riverIndex] =
                                new List<Vector2>(finalCenterline);
                        for (int ti = 0; ti < grid.LakeFirstWaterGraph.Tributaries.Count; ti++)
                        {
                            var trib = grid.LakeFirstWaterGraph.Tributaries[ti];
                            if (trib.RiverIndex != riverIndex || !trib.Accepted)
                                continue;
                            trib.DebugCarvePathCells = new List<Vector2>(finalCenterline);
                            trib.CenterlineCells = new List<Vector2>(finalCenterline);
                            break;
                        }
                    }
                }

                if (finalCenterline == null || finalCenterline.Count < 2)
                    continue;

                resolvedFinalByRiverIndex[riverIndex] = finalCenterline;
                if (!IsUwpDegenerateTributary(finalCenterline, cellSize, grid, riverIndex))
                    continue;

                degenerateIndices.Add(riverIndex);
                float lengthWorld = ComputePolylineLengthCells(finalCenterline) * Mathf.Max(0.01f, cellSize);
                Debug.Log(
                    $"[UWP_DEGENERATE_TRIBUTARY_PREPASS] riverIndex={riverIndex} " +
                    $"predictedFinalPts={finalCenterline.Count} predictedLength={lengthWorld:F2} " +
                    $"reason=degenerate_tributary lakeFirst=1");
            }
        }

        static bool TryResolveLakeFirstFinalCenterlineCells(
            GridSystem grid,
            int riverIndex,
            IReadOnlyList<Vector2> rawPath,
            MapGenConfig config,
            out List<Vector2> finalCenterline,
            out string skipReason)
        {
            finalCenterline = null;
            skipReason = null;
            List<Vector2> source = null;
            if (grid?.LakeFirstWaterGraph != null &&
                grid.LakeFirstWaterGraph.TryGetFinalCenterline(riverIndex, out List<Vector2> fromGraph))
            {
                source = fromGraph;
            }
            else if (rawPath != null && rawPath.Count >= 2)
            {
                source = rawPath as List<Vector2> ?? new List<Vector2>(rawPath);
            }

            if (source == null || source.Count < 2)
            {
                skipReason = "lake_first_no_centerline";
                return false;
            }

            finalCenterline = NormalizeCenterlineSpacingForMesh(new List<Vector2>(source), config);
            if (finalCenterline == null || finalCenterline.Count < 2)
            {
                skipReason = "lake_first_centerline_empty";
                return false;
            }

            // Anclar confluencia: lake-spill e inland NO (el cell suele estar dentro del main
            // y recrea gancho U / blob en la junta). Headwater: no anclar aquí.
            Vector2? tribConfluence = null;
            bool headwaterSkipConfPin = riverIndex > 0 && IsLakeFirstHeadwaterFeeder(grid, riverIndex);
            bool lakeSpillSkipConfPin = riverIndex > 0 &&
                UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex);
            bool inlandSkipConfPin = riverIndex > 0 && UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex);
            if (!headwaterSkipConfPin && !lakeSpillSkipConfPin && !inlandSkipConfPin &&
                riverIndex > 0 && grid?.LakeFirstWaterGraph != null)
            {
                for (int i = 0; i < grid.LakeFirstWaterGraph.Tributaries.Count; i++)
                {
                    var trib = grid.LakeFirstWaterGraph.Tributaries[i];
                    if (trib.RiverIndex != riverIndex || !trib.Accepted)
                        continue;
                    tribConfluence = new Vector2(
                        trib.MainRiverConfluenceCell.x + 0.5f,
                        trib.MainRiverConfluenceCell.y + 0.5f);
                    break;
                }
            }

            if (!headwaterSkipConfPin && !lakeSpillSkipConfPin && !inlandSkipConfPin &&
                !tribConfluence.HasValue && riverIndex > 0 &&
                TryGetTributaryConfluenceCell(grid, riverIndex, out Vector2 registeredConf, out _))
            {
                tribConfluence = registeredConf;
            }

            if (tribConfluence.HasValue && finalCenterline.Count >= 2)
                finalCenterline[finalCenterline.Count - 1] = tribConfluence.Value;

            if (riverIndex > 0 && !IsTributaryLakeOwner(grid, riverIndex) &&
                !UwpTributaryOriginUtility.IsSupplemental(grid, riverIndex))
                AppendCenterlineTowardLakeShore(grid, finalCenterline, riverIndex, config, extendStart: true);

            if (tribConfluence.HasValue && finalCenterline.Count >= 2)
                finalCenterline[finalCenterline.Count - 1] = tribConfluence.Value;

            int gw = grid != null ? grid.Width : 0;
            int gh = grid != null ? grid.Height : 0;
            bool inlandFeeder = riverIndex > 0 && UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex);
            bool headwaterFeeder = riverIndex > 0 && IsLakeFirstHeadwaterFeeder(grid, riverIndex);
            bool lakeSpillFeeder = riverIndex > 0 &&
                UwpTributaryOriginUtility.UsesLakeSpillVisualTreatment(grid, riverIndex);

            // Spill: recortar a orilla ANTES del meandro. Si el extremo ya está clavado dentro
            // del main (PinLakeFirstTributaryEndpointConfluence), el meandro con protectJoinTail
            // reconstruye el gancho U entre lago y ese extremo — el trim post-meandro casi no cambia nada.
            if (lakeSpillFeeder && finalCenterline != null && finalCenterline.Count >= 4)
            {
                int beforePre = finalCenterline.Count;
                TrimEmissaryHookTailIfRegressesFromMain(grid, config, finalCenterline, riverIndex);
                TrimRiverAtFirstMainCorridorContact(grid, config, finalCenterline, riverIndex, forceSpillJoin: true);
                SnapLakeSpillMouthToMainBank(grid, config, finalCenterline, riverIndex);
                SoftenLakeSpillMouthApproach(finalCenterline, approachPts: 5);
                if (config.uwpOwnedVisualPolicy || config.debugHydrologyNetwork)
                {
                    Debug.Log(
                        $"[LakeSpillJoinTrim] phase=preMeander riverIndex={riverIndex} " +
                        $"before={beforePre} after={finalCenterline.Count} seed={config.seed}");
                }
            }

            // Lake-spill también congela cola: el meandro libre recreaba el V en la junta.
            // InlandFeeder: ya meandró en TryPrepareLakeFirstInlandFeederVisualCenterline — no apilar.
            bool protectJoinTail = inlandFeeder || headwaterFeeder || lakeSpillFeeder;
            if (gw >= 2 && gh >= 2)
            {
                float meanderMax = 0f;
                bool meanderOk = true;
                // Inland: ya meandró (o se omitió si corto) en prepare. Headwater: meandro libre
                // recreaba overshoot hacia la boca; first-entry al receptor basta.
                if (!inlandFeeder && !headwaterFeeder)
                {
                    meanderOk = ApplyLakeFirstOrganicMeanderToCellSpace(
                        grid, finalCenterline, gw, gh, config, riverIndex, out meanderMax, protectJoinTail);
                }

                // Main Lake-first: si organic falla (self-intersect), fallback visual (antes quedaba RECTO).
                if (riverIndex == 0 && !meanderOk)
                {
                    meanderOk = ApplyVisualMeanderToCellSpace(
                        finalCenterline, gw, gh, config, 0, out meanderMax);
                }

                if (riverIndex == 0)
                {
                    float minDim = Mathf.Max(1f, Mathf.Min(gw, gh));
                    Debug.LogWarning(
                        $"[RiverVisualMeander] lakeFirst main accepted={(meanderOk ? 1 : 0)} mode=splineChatGpt " +
                        $"maxOffsetCells={meanderMax:F3} ampRatio={(meanderMax / minDim):F4} " +
                        $"pts={finalCenterline.Count} minDim={minDim:F0} " +
                        $"meshMul={config.riverSurfaceMainMeshOnlyWidthMul:F2} " +
                        $"flatRatio={config.uwpCarveTransverseFlatRatio:F2}");
                }

                if (riverIndex == 0 && meanderOk)
                {
                    // Conservar densidad del spline (~0.28). Normalize(≥0.48) re-esparsaba → orilla en escalera.
                    finalCenterline = ResampleUniformSpacingCell(finalCenterline, 0.28f, 2048);
                }
                else
                {
                    finalCenterline = NormalizeCenterlineSpacingForMesh(finalCenterline, config);
                }

                // Lake-spill: NO re-anclar a MainRiverConfluenceCell tras meandro — suele quedar
                // dentro del canal y, con Append, vuelve a dibujar el “pasa de largo”.
                // Inland: tampoco re-anclar (mismo gancho U + blob); el trim a orilla cierra la junta.
                if (!headwaterFeeder && !lakeSpillFeeder && !inlandFeeder && tribConfluence.HasValue &&
                    finalCenterline != null && finalCenterline.Count >= 2)
                    finalCenterline[finalCenterline.Count - 1] = tribConfluence.Value;
            }

            if (riverIndex > 0 && UwpTributaryOriginUtility.ShouldApplyMainRiverConfluenceIngress(grid, riverIndex, config))
            {
                if (IsTributaryLakeOwner(grid, riverIndex))
                    AppendLakeFirstTributaryCenterlineTowardCentroid(grid, riverIndex, finalCenterline, config);
                // Ingress corto; trim FIRST-ENTRY / hook-trim deben ir después (Append no puede reabrir el V).
                if (!inlandFeeder)
                    AppendLakeFirstTributaryCenterlineTowardMainRiver(grid, riverIndex, finalCenterline, config);
                int beforePost = finalCenterline != null ? finalCenterline.Count : 0;
                // Quita cola en gancho U (pase de largo → se aleja del troncal) antes del first-entry.
                if (!inlandFeeder && finalCenterline != null && finalCenterline.Count >= 6)
                    TrimEmissaryHookTailIfRegressesFromMain(grid, config, finalCenterline, riverIndex);
                // Spill: first-entry agresivo + snap orilla. Inland: first-entry suave (sin snap spill).
                if (lakeSpillFeeder && finalCenterline != null && finalCenterline.Count >= 3)
                {
                    TrimRiverAtFirstMainCorridorContact(
                        grid, config, finalCenterline, riverIndex, forceSpillJoin: true);
                    SnapLakeSpillMouthToMainBank(grid, config, finalCenterline, riverIndex);
                    // Sin Straighten: con tip ya en orilla, lerp forzada recreaba cuña blanca / 90°.
                    SoftenLakeSpillMouthApproach(finalCenterline, approachPts: 5);
                }
                else if (inlandFeeder && finalCenterline != null && finalCenterline.Count >= 3)
                {
                    TrimRiverAtFirstMainCorridorContact(
                        grid, config, finalCenterline, riverIndex, forceSpillJoin: false);
                    SnapLakeSpillMouthToMainBank(grid, config, finalCenterline, riverIndex);
                    // Tip radial solo mueve 1 vértice → cinta ~90° / punta. Enderezar enfoque como headwater.
                    StraightenTributaryMouthApproach(finalCenterline, approachPts: 7);
                }
                else if (!inlandFeeder && finalCenterline != null && finalCenterline.Count >= 3)
                    TrimTributaryAtClosestMainApproach(grid, config, finalCenterline, riverIndex);

                if (lakeSpillFeeder && (config.uwpOwnedVisualPolicy || config.debugHydrologyNetwork))
                {
                    Debug.Log(
                        $"[LakeSpillJoinTrim] phase=postMeander riverIndex={riverIndex} " +
                        $"before={beforePost} after={(finalCenterline != null ? finalCenterline.Count : 0)} seed={config.seed}");
                }
                if (inlandFeeder && (config.uwpOwnedVisualPolicy || config.debugHydrologyNetwork))
                {
                    Debug.Log(
                        $"[InlandFeederJoinTrim] riverIndex={riverIndex} " +
                        $"before={beforePost} after={(finalCenterline != null ? finalCenterline.Count : 0)} seed={config.seed}");
                }
                // Spill: no tip post-trim — reintroducía penetración al main y el V “pasa de largo”.
            }

            if (riverIndex > 0 &&
                UwpTributaryOriginUtility.GetOrigin(grid, riverIndex) == UwpTributaryOriginKind.HeadwaterFeeder &&
                TryResolveHeadwaterReceiverRiverIndex(grid, riverIndex, out int recvRi))
            {
                // NO Tuck ni Append-ingress. First-entry + snap + enderezado de boca
                // (evita gancho/colita en la T inland↔headwater).
                TrimTributaryAtFirstParentCorridorContact(
                    grid, config, finalCenterline, riverIndex, recvRi, forceEntry: true);
                SnapTributaryCenterlineToParentRiver(
                    grid, finalCenterline, riverIndex, config, recvRi, finalCenterline.Count - 1);
                StraightenTributaryMouthApproach(finalCenterline, approachPts: 7);
                if (config.uwpOwnedVisualPolicy || config.debugHydrologyNetwork)
                {
                    Debug.Log(
                        $"[HeadwaterFeederJoinTrim] riverIndex={riverIndex} receiver={recvRi} " +
                        $"pts={finalCenterline.Count} seed={config.seed}");
                }
            }

            if (grid?.LakeFirstWaterGraph != null)
            {
                if (grid.LakeFirstWaterGraph.FinalCenterlineByRiverIndex.ContainsKey(riverIndex))
                    grid.LakeFirstWaterGraph.FinalCenterlineByRiverIndex[riverIndex] = new List<Vector2>(finalCenterline);
            }

            if (grid?.LakeFirstWaterGraph != null && riverIndex > 0)
            {
                for (int i = 0; i < grid.LakeFirstWaterGraph.Tributaries.Count; i++)
                {
                    var trib = grid.LakeFirstWaterGraph.Tributaries[i];
                    if (trib.RiverIndex != riverIndex || !trib.Accepted)
                        continue;
                    // Ambos overlays: el naranja (CenterlineCells) era el path A* original y
                    // tapaba la evaluación del fix aunque FinalCenterline ya estuviera bien.
                    trib.DebugCarvePathCells = new List<Vector2>(finalCenterline);
                    trib.CenterlineCells = new List<Vector2>(finalCenterline);
                    break;
                }
            }

            // Spill: tip funcional = orilla (no pin hacia eje). Carve usa FinalCenterline;
            // alinear RiverCenterlinesCellSpace evita desync residual pin→bandeja blanca.
            if (lakeSpillFeeder &&
                grid?.RiverCenterlinesCellSpace != null &&
                riverIndex > 0 &&
                riverIndex < grid.RiverCenterlinesCellSpace.Count &&
                finalCenterline != null &&
                finalCenterline.Count >= 2)
            {
                grid.RiverCenterlinesCellSpace[riverIndex] = new List<Vector2>(finalCenterline);
            }

            return true;
        }

        static bool TryResolveRiverVisualFinalCenterlineCells(
            GridSystem grid,
            MapGenConfig config,
            int riverIndex,
            IReadOnlyList<Vector2> rawPath,
            int w,
            int h,
            float cellSize,
            bool logDetail,
            RiverVisualSurfaceData surfaceForAudit,
            out List<Vector2> finalCenterline,
            out string skipReason)
        {
            finalCenterline = null;
            skipReason = null;
            if (grid == null || config == null || rawPath == null || rawPath.Count < 2)
            {
                skipReason = "raw_too_short";
                return false;
            }

            List<Vector2> rawPathList = rawPath as List<Vector2> ?? new List<Vector2>(rawPath);
            List<Vector2> cellProcessed;
            bool standardTrib = IsStandardDendriticTributary(grid, riverIndex);
            bool lakeEmissary = IsLakeEmissaryRiverIndex(grid, riverIndex);
            if (standardTrib)
            {
                if (!TryPrepareStandardTributaryCenterline(
                        grid, config, riverIndex, rawPathList, logDetail, out cellProcessed, out _, out string rejectSub))
                {
                    skipReason = string.IsNullOrEmpty(rejectSub)
                        ? "tributary_visual_reject"
                        : $"tributary_visual_reject:{rejectSub}";
                    return false;
                }

                if (!ApplyStandardTributaryLakeMouthFinalJoin(
                        grid, ref cellProcessed, riverIndex, config, cellSize))
                {
                    skipReason = "lake_mouth_bridge_empty";
                    return false;
                }
            }
            else if (lakeEmissary)
            {
                if (!TryPrepareLakeEmissaryCenterline(
                        grid, config, riverIndex, rawPathList, logDetail, out cellProcessed, out _))
                {
                    skipReason = "emissary_visual_reject";
                    return false;
                }
            }
            else
            {
                cellProcessed = BuildVisualCenterlineFromLogical(
                    grid,
                    rawPathList,
                    config,
                    riverIndex,
                    out _,
                    out _);
                if (cellProcessed == null || cellProcessed.Count < 2)
                {
                    skipReason = "visual_centerline_failed";
                    return false;
                }

                if (riverIndex == 0)
                    cellProcessed = FallbackMainRiverCenterlineIfInvalid(grid, rawPathList, config, cellProcessed);

                if (cellProcessed == null || cellProcessed.Count < 2)
                {
                    skipReason = "main_centerline_invalid";
                    return false;
                }

                if (riverIndex > 0 && !IsLakeEmissaryRiverIndex(grid, riverIndex))
                    TrimRiverSurfaceStartAtLakeMouth(grid, cellProcessed, config);

                if (cellProcessed == null || cellProcessed.Count < 2)
                {
                    skipReason = "lake_mouth_trim_empty";
                    return false;
                }

                if (riverIndex > 0 && !IsLakeEmissaryCenterline(grid, cellProcessed, riverIndex) &&
                    !TributaryTargetsMainConfluence(grid, riverIndex))
                    TrimRiverSurfaceExcludingLakeInterior(grid, cellProcessed, riverIndex, config);

                if (cellProcessed == null || cellProcessed.Count < 2)
                {
                    skipReason = "lake_interior_trim_empty";
                    return false;
                }

                ApplyLakeRiverMouthVisualBridging(grid, cellProcessed, riverIndex, config);

                if (cellProcessed == null || cellProcessed.Count < 2)
                {
                    skipReason = "lake_mouth_bridge_empty";
                    return false;
                }

                ApplySplitModeConfluenceAndLakeEndpoints(grid, cellProcessed, riverIndex, config, cellSize);

                if (cellProcessed == null || cellProcessed.Count < 2)
                {
                    skipReason = "split_endpoint_fix_empty";
                    return false;
                }

                TrimRiverSurfaceEndAtLakeMouth(grid, cellProcessed, config);

                if (cellProcessed == null || cellProcessed.Count < 2)
                {
                    skipReason = "lake_mouth_end_trim_empty";
                    return false;
                }

                TrimRiverSurfaceStaticWaterFromEnds(grid, cellProcessed, riverIndex, config);

                if (cellProcessed == null || cellProcessed.Count < 2)
                {
                    skipReason = "static_water_trim_empty";
                    return false;
                }

                ApplyWebFusionLakeMouthAfterTrim(grid, cellProcessed, riverIndex, config);

                if (cellProcessed == null || cellProcessed.Count < 2)
                {
                    skipReason = "web_fusion_mouth_empty";
                    return false;
                }
            }

            if ((standardTrib || lakeEmissary) &&
                WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
            {
                ApplyWebFusionLakeMouthAfterTrim(grid, cellProcessed, riverIndex, config);
                if (cellProcessed == null || cellProcessed.Count < 2)
                {
                    skipReason = "web_fusion_trib_mouth_empty";
                    return false;
                }

                if (standardTrib)
                    ApplyTributaryMainConfluenceCenterlineTrim(grid, cellProcessed, riverIndex, config, cellSize);
                if (cellProcessed == null || cellProcessed.Count < 2)
                {
                    skipReason = "tributary_confluence_trim_empty";
                    return false;
                }
            }

            bool webFusionStabilize = IsSplitLakeMsRiverWebFusionStabilizeMode(config);
            if (!webFusionStabilize)
            {
                ApplySplitLakeMouthStabilizationTrims(
                    grid, cellProcessed, riverIndex, config, standardTrib, lakeEmissary);
                if (cellProcessed == null || cellProcessed.Count < 2)
                {
                    skipReason = lakeEmissary
                        ? "emissary_lake_origin_trim_empty"
                        : "dendritic_lake_mouth_nudge_empty";
                    return false;
                }
            }

            if (cellProcessed == null || cellProcessed.Count < 2)
            {
                skipReason = "centerline_empty";
                return false;
            }

            if (riverIndex > 0 && TryCullTributarySurfacePiece(grid, cellProcessed, riverIndex, config, logDetail))
            {
                skipReason = "tributary_cull";
                return false;
            }

            if ((!standardTrib && !lakeEmissary) ||
                WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                cellProcessed = NormalizeCenterlineSpacingForMesh(cellProcessed, config);

            if (cellProcessed == null || cellProcessed.Count < 2)
            {
                skipReason = "centerline_empty";
                return false;
            }

            if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config) && riverIndex > 0 &&
                config.uwpOwnedVisualPolicy && standardTrib)
            {
                PruneTributaryCenterlineInteriorMainCrossings(grid, cellProcessed, riverIndex, config);
            }

            if (config.uwpOwnedVisualPolicy && standardTrib && cellProcessed != null)
                EnsureTributaryOwnedLakeMouthPipeline(grid, cellProcessed, riverIndex, config);
            else if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config) && riverIndex > 0)
                ApplyWebFusionTributaryLakeMouthFinalize(grid, cellProcessed, riverIndex, config);

            if (cellProcessed == null || cellProcessed.Count < 2)
            {
                skipReason = "web_fusion_finalize_empty";
                return false;
            }

            if (webFusionStabilize)
            {
                ApplySplitLakeMouthStabilizationTrims(
                    grid, cellProcessed, riverIndex, config, standardTrib, lakeEmissary);
                if (cellProcessed == null || cellProcessed.Count < 2)
                {
                    skipReason = lakeEmissary
                        ? "emissary_lake_origin_trim_empty"
                        : "dendritic_lake_mouth_nudge_empty";
                    return false;
                }
            }

            if (cellProcessed == null || cellProcessed.Count < 2)
            {
                skipReason = "stabilize_trim_empty";
                return false;
            }

            if (!standardTrib && !lakeEmissary)
            {
                if (surfaceForAudit != null)
                    surfaceForAudit.PreClipInputPoints = cellProcessed.Count;
                cellProcessed = PreClipCenterlineCellSpace(cellProcessed, w, h, out bool clipStart, out bool clipEnd);
                if (surfaceForAudit != null)
                {
                    surfaceForAudit.PreClipOutputPoints = cellProcessed != null ? cellProcessed.Count : 0;
                    surfaceForAudit.PreClipStart = clipStart;
                    surfaceForAudit.PreClipEnd = clipEnd;
                }

                if (logDetail && surfaceForAudit != null)
                {
                    Debug.Log(
                        $"[RiverSurfacePreClip] riverIndex={riverIndex} inputPoints={surfaceForAudit.PreClipInputPoints} " +
                        $"outputPoints={surfaceForAudit.PreClipOutputPoints} startClipped={(clipStart ? 1 : 0)} endClipped={(clipEnd ? 1 : 0)} " +
                        $"visibleOutsideBounds=0");
                }
            }
            else if (surfaceForAudit != null)
            {
                surfaceForAudit.PreClipInputPoints = cellProcessed.Count;
                surfaceForAudit.PreClipOutputPoints = cellProcessed.Count;
                surfaceForAudit.PreClipStart = false;
                surfaceForAudit.PreClipEnd = false;
            }

            if (cellProcessed == null || cellProcessed.Count < 2)
            {
                skipReason = "preclip_empty";
                return false;
            }

            if (riverIndex == 0 && config.uwpOwnedVisualPolicy)
            {
                SnapMainRiverMapBorderEndpoints(cellProcessed, w, h);
                bool meanderOk = ApplyVisualMeanderToCellSpace(
                    cellProcessed, w, h, config, riverIndex, out float meanderMax);
                Debug.Log(
                    $"[RiverVisualMeander] riverId=0 mode=cellSpace maxOffsetCells={meanderMax:F3} " +
                    $"accepted={(meanderOk ? 1 : 0)} ampCfg={config.riverSurfaceVisualMeanderAmplitudeCells:F2}");
            }

            finalCenterline = cellProcessed;
            return true;
        }

        /// <summary>
        /// Fase 1 (auditoría): antes del cache, mesh y máscara repetían ProcessCenterline + meander + clip distinto;
        /// el clamp post-malla (verts_clamped) deformaba triángulos. Este método unifica la verdad visual.
        /// </summary>
        public static bool EnsureRiverVisualSurfaceCache(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null || !config.riverVisualUseRiverSurfaceMeshStrip)
                return false;
            if (grid.RiverCenterlinesCellSpace == null || grid.RiverCenterlinesCellSpace.Count == 0)
                return false;

            if (grid.RiverVisualSurfaceCacheFrozen &&
                grid.RiverVisualSurfacesBuilt &&
                grid.RiverVisualSurfaces != null &&
                grid.RiverVisualSurfaceMask != null &&
                grid.RiverVisualSurfaceMask.GetLength(0) == grid.Width &&
                grid.RiverVisualSurfaceMask.GetLength(1) == grid.Height)
            {
                if (config.uwpOwnedVisualPolicy && UwpCachedSurfacesHaveUnskippedDegenerateTributary(grid, config))
                    grid.ClearRiverVisualSurfaceCache();
                else
                    return true;
            }

            if (config.riverVisualSurfaceCacheEnabled &&
                grid.RiverVisualSurfacesBuilt &&
                grid.RiverVisualSurfaces != null &&
                grid.RiverVisualSurfaceMask != null &&
                grid.RiverVisualSurfaceMask.GetLength(0) == grid.Width &&
                grid.RiverVisualSurfaceMask.GetLength(1) == grid.Height &&
                grid.RiverVisualSurfaces.Count == grid.RiverCenterlinesCellSpace.Count)
            {
                if (config.uwpOwnedVisualPolicy && UwpCachedSurfacesHaveUnskippedDegenerateTributary(grid, config))
                    grid.ClearRiverVisualSurfaceCache();
                else
                    return true;
            }

            grid.ClearRiverVisualSurfaceCache();
            float cellSize = grid.CellSizeWorld;
            int w = grid.Width;
            int h = grid.Height;
            Vector3 origin = grid.Origin;
            float inset = Mathf.Max(0f, config.riverVisualBankInset);
            float marginCells = Mathf.Max(0f, config.riverVisualRasterMaskExtraCellMargin);
            float maxDev = Mathf.Max(0.2f, config.riverVisualMaxPathDeviationCells);
            bool logDetail = config.debugLogs || config.debugHydrologyNetwork;
            bool lakeFirstPipeline = config.uwpLakeFirstHydrologyPipeline;
            var combinedMask = new bool[w, h];
            var surfaces = new List<RiverVisualSurfaceData>(grid.RiverCenterlinesCellSpace.Count);
            bool[,] mainOnlyMask = null;
            HashSet<int> uwpDegenerateTributaryPrepass = null;
            Dictionary<int, List<Vector2>> uwpTributaryVisualPrepassFinal = null;
            if (config.uwpOwnedVisualPolicy && grid.RiverCenterlinesCellSpace.Count > 1)
            {
                if (lakeFirstPipeline)
                {
                    BuildUwpLakeFirstDegenerateTributaryPrepass(
                        grid, config, cellSize,
                        out uwpDegenerateTributaryPrepass,
                        out uwpTributaryVisualPrepassFinal);
                }
                else
                {
                    BuildUwpDegenerateTributaryPrepass(
                        grid, config, w, h, cellSize, logDetail,
                        out uwpDegenerateTributaryPrepass,
                        out uwpTributaryVisualPrepassFinal);
                }
            }

            for (int riverIndex = 0; riverIndex < grid.RiverCenterlinesCellSpace.Count; riverIndex++)
            {
                var rawPath = grid.RiverCenterlinesCellSpace[riverIndex];
                var surface = new RiverVisualSurfaceData { RiverIndex = riverIndex, BuiltFromFunctionalPath = true };
                if (rawPath == null || rawPath.Count < 2)
                {
                    surface.Skipped = true;
                    surface.SkipReason = "raw_too_short";
                    surfaces.Add(surface);
                    continue;
                }

                surface.RawFunctionalCenterlineCells = new List<Vector2>(rawPath);
                var lockedKeys = BuildLockedAnchorKeys(grid, rawPath, riverIndex, config, out var lockedAnchors, out int fordAnchors, out float fordMaxDist);
                surface.LockedAnchorCells = lockedAnchors;
                surface.FordAnchorCount = fordAnchors;
                surface.FordMaxDistanceCells = fordMaxDist;

                if (logDetail)
                {
                    Debug.Log(
                        $"[RiverVisualFordAnchor] riverIndex={riverIndex} fordAnchors={fordAnchors} " +
                        $"maxDistanceToFordCell={fordMaxDist:F3} accepted={(fordMaxDist <= 0.35f ? 1 : 0)}");
                }

                List<Vector2> cellProcessed;
                if (lakeFirstPipeline)
                {
                    // Reutilizar finalize+densify del prepass (main y tribs): evita doble meandro.
                    if (uwpTributaryVisualPrepassFinal != null &&
                        uwpTributaryVisualPrepassFinal.TryGetValue(riverIndex, out List<Vector2> lfPrepassFinal))
                    {
                        cellProcessed = lfPrepassFinal;
                        surface.PreClipInputPoints = cellProcessed.Count;
                        surface.PreClipOutputPoints = cellProcessed.Count;
                        surface.PreClipStart = false;
                        surface.PreClipEnd = false;
                    }
                    else if (!TryResolveLakeFirstFinalCenterlineCells(
                            grid,
                            riverIndex,
                            rawPath,
                            config,
                            out cellProcessed,
                            out string lakeFirstSkip))
                    {
                        surface.Skipped = true;
                        surface.SkipReason = lakeFirstSkip ?? "lake_first_centerline_failed";
                        surfaces.Add(surface);
                        continue;
                    }
                    else
                    {
                        surface.PreClipInputPoints = cellProcessed.Count;
                        surface.PreClipOutputPoints = cellProcessed.Count;
                        surface.PreClipStart = false;
                        surface.PreClipEnd = false;

                        // Main también densifica (como trib): sin esto la máscara/carve del troncal queda cuadriculada.
                        if (cellProcessed != null && cellProcessed.Count >= 2)
                        {
                            if (riverIndex == 0)
                            {
                                // Cap 320 de DensifyLakeFirst… es insuficiente en main largo → usar remuestreo denso.
                                cellProcessed = ResampleUniformSpacingCell(cellProcessed, 0.28f, 2048);
                            }
                            else
                            {
                                float densifyStep = IsLakeFirstHeadwaterFeeder(grid, riverIndex) ? 0.35f : 0.52f;
                                cellProcessed = DensifyLakeFirstCenterlineForMask(
                                    cellProcessed,
                                    densifyStep,
                                    fineStream: IsLakeFirstHeadwaterFeeder(grid, riverIndex));
                            }

                            // Densify no debe dejar el grafo/debug con el path pre-trim o pre-densify.
                            if (riverIndex > 0 &&
                                grid.LakeFirstWaterGraph != null &&
                                cellProcessed != null &&
                                cellProcessed.Count >= 2)
                            {
                                if (grid.LakeFirstWaterGraph.FinalCenterlineByRiverIndex.ContainsKey(riverIndex))
                                    grid.LakeFirstWaterGraph.FinalCenterlineByRiverIndex[riverIndex] =
                                        new List<Vector2>(cellProcessed);
                                for (int ti = 0; ti < grid.LakeFirstWaterGraph.Tributaries.Count; ti++)
                                {
                                    var trib = grid.LakeFirstWaterGraph.Tributaries[ti];
                                    if (trib.RiverIndex != riverIndex || !trib.Accepted)
                                        continue;
                                    trib.DebugCarvePathCells = new List<Vector2>(cellProcessed);
                                    trib.CenterlineCells = new List<Vector2>(cellProcessed);
                                    break;
                                }
                            }
                        }
                    }
                }
                else if (config.uwpOwnedVisualPolicy &&
                    riverIndex > 0 &&
                    uwpTributaryVisualPrepassFinal != null &&
                    uwpTributaryVisualPrepassFinal.TryGetValue(riverIndex, out List<Vector2> prepassFinal))
                {
                    cellProcessed = prepassFinal;
                    surface.PreClipInputPoints = cellProcessed.Count;
                    surface.PreClipOutputPoints = cellProcessed.Count;
                    surface.PreClipStart = false;
                    surface.PreClipEnd = false;
                }
                else if (!TryResolveRiverVisualFinalCenterlineCells(
                             grid,
                             config,
                             riverIndex,
                             rawPath,
                             w,
                             h,
                             cellSize,
                             logDetail,
                             surface,
                             out cellProcessed,
                             out string resolveSkipReason))
                {
                    surface.Skipped = true;
                    surface.SkipReason = resolveSkipReason ?? "visual_centerline_failed";
                    surfaces.Add(surface);
                    continue;
                }

                surface.FinalCenterlineCells = cellProcessed;

                if (config.uwpOwnedVisualPolicy && riverIndex > 0 &&
                    ((uwpDegenerateTributaryPrepass != null && uwpDegenerateTributaryPrepass.Contains(riverIndex)) ||
                     IsUwpDegenerateTributary(cellProcessed, cellSize, grid, riverIndex)))
                {
                    ApplyUwpDegenerateTributarySkip(surface, riverIndex, cellProcessed, cellSize);
                    surfaces.Add(surface);
                    continue;
                }

                float fullCellsW = riverIndex == 0
                    ? config.riverVisualRibbonFullWidthCellsMain
                    : (config.riverVisualRibbonFullWidthCellsTributary > 0.01f
                        ? config.riverVisualRibbonFullWidthCellsTributary
                        : config.riverVisualRibbonFullWidthCellsMain);
                float baseHalfW = fullCellsW > 0.01f
                    ? Mathf.Max(0.08f, fullCellsW * 0.5f * cellSize - inset)
                    : Mathf.Max(0.08f, config.riverVisualMeshHalfWidth - inset);

                var worldForWidth = CellPolylineToWorldXZ(cellProcessed, origin, cellSize, 0f);
                float amp = riverIndex == 0
                    ? Mathf.Max(0f, config.riverSurfaceWidthNoiseAmpMain)
                    : Mathf.Max(0f, config.riverSurfaceWidthNoiseAmpTributary);
                float noiseScale = Mathf.Max(0.0001f, config.riverSurfaceWidthNoiseScale);
                List<float> halfWidths;
                if (riverIndex == 0)
                {
                    var joinCells = BuildJoinProximityCellKeys(
                        grid.RiverCenterlinesCellSpace, 0, w, h, uwpDegenerateTributaryPrepass);
                    if (uwpDegenerateTributaryPrepass != null && uwpDegenerateTributaryPrepass.Count > 0)
                    {
                        foreach (int ignoredTrib in uwpDegenerateTributaryPrepass)
                        {
                            Debug.Log(
                                $"[UWP_MAIN_IGNORE_DEGENERATE_TRIBUTARY] mainIndex=0 ignoredTributary={ignoredTrib} " +
                                $"reason=degenerate_tributary");
                        }
                    }

                    halfWidths = BuildMainRiverHalfWidthsWithArcVariation(
                        grid,
                        worldForWidth,
                        cellProcessed,
                        baseHalfW,
                        amp,
                        noiseScale,
                        joinCells,
                        config,
                        out _,
                        out _);
                }
                else
                {
                    halfWidths = BuildOrganicHalfWidths(
                        cellProcessed,
                        baseHalfW,
                        grid,
                        config,
                        riverIndex,
                        out _,
                        out _,
                        out _,
                        out _);
                }

                if (riverIndex > 0 &&
                    (config.riverSurfaceTributaryWidthFixEnabled || config.uwpOwnedVisualPolicy))
                {
                    ApplyTributaryConfluenceVisualHalfWidths(
                        grid, cellProcessed, halfWidths, config, riverIndex);
                }

                if (!lakeFirstPipeline && (riverIndex == 0 || config.uwpOwnedVisualPolicy))
                    ApplyLakeMouthVisualHalfWidthFlare(grid, cellProcessed, halfWidths, config);
                if (WaterVisualPipelinePolicy.IsSplitLakeMsRiverWebFusion(config))
                {
                    if (riverIndex > 0)
                    {
                        if (!WaterVisualPipelinePolicy.IsSplitLakeMsRiverMouthFusion(config))
                            ApplyWebFusionTributaryEndpointConstantWidth(halfWidths, riverIndex);
                    }
                    else
                    {
                        ApplyWebFusionLakeMouthWidthTaper(grid, cellProcessed, halfWidths, config, riverIndex, cellSize, atStart: true);
                        ApplyWebFusionLakeMouthWidthTaper(grid, cellProcessed, halfWidths, config, riverIndex, cellSize, atStart: false);
                    }
                }
                ApplyFordWidthDampening(grid, cellProcessed, halfWidths, config);
                if (config.uwpOwnedVisualPolicy && halfWidths != null && cellProcessed != null)
                {
                    if (riverIndex == 0 && !lakeFirstPipeline)
                        ApplyUwpMainRiverShoreIntersectionRepair(halfWidths, cellProcessed, baseHalfW, config);
                    else if (lakeFirstPipeline && riverIndex > 0)
                        ApplyUwpMainRiverShoreIntersectionRepair(halfWidths, cellProcessed, baseHalfW, config);
                }
                var maskHalfWidths = CloneHalfWidths(halfWidths);
                var meshHalfWidths = CloneHalfWidths(halfWidths);
                ApplyMainMeshOnlyWidthScale(meshHalfWidths, config, riverIndex, grid);
                // Main Lake First: mismo contrato Headwater (carve = mesh × foamRatio).
                if (lakeFirstPipeline && riverIndex == 0)
                {
                    ApplyLakeFirstChannelCarveToMeshGrowthProfile(
                        meshHalfWidths, maskHalfWidths, cellProcessed, cellSize);
                    if (config.uwpOwnedVisualPolicy || config.debugHydrologyNetwork)
                    {
                        Debug.Log(
                            $"[LakeFirstChannelContract] kind=Main riverIndex=0 " +
                            $"meshOverCarve={LakeFirstChannelMeshOverCarveMul:F2} flatFloor=1 seed={config.seed}");
                    }
                }
                if (!lakeFirstPipeline && riverIndex == 0 && config.uwpOwnedVisualPolicy)
                    SyncMainRiverMaskWidthsToMesh(maskHalfWidths, meshHalfWidths);
                if (riverIndex > 0 && config.uwpOwnedVisualPolicy && !lakeFirstPipeline)
                    ApplyTributaryConfluenceExtraMeshWidth(
                        grid, cellProcessed, meshHalfWidths, config, riverIndex, cellSize);
                if (riverIndex > 0 && config.uwpOwnedVisualPolicy && IsLakeSpillTributaryVisual(grid, config, riverIndex))
                {
                    ApplyTributaryLakeMouthExtraMeshWidth(
                        grid, cellProcessed, meshHalfWidths, config, riverIndex);
                    ApplyTributaryLakeMouthVisualHalfWidths(
                        grid, cellProcessed, meshHalfWidths, config, riverIndex);
                }

                // Lake-first: lake-spill, inland→main y headwater→trib comparten pipeline carve; headwater estrecha V al final.
                if (lakeFirstPipeline && riverIndex > 0 && config.uwpOwnedVisualPolicy &&
                    UwpTributaryOriginUtility.UsesLakeFirstTributaryCarvePipeline(grid, config, riverIndex))
                {
                    bool lakeSpill = IsLakeSpillTributaryVisual(grid, config, riverIndex);
                    bool headwater = IsLakeFirstHeadwaterFeeder(grid, riverIndex);
                    ApplyTributaryConfluenceExtraMeshWidth(
                        grid, cellProcessed, meshHalfWidths, config, riverIndex, cellSize, forCarveMask: false);
                    if (!headwater)
                    {
                        ApplyTributaryConfluenceExtraMeshWidth(
                            grid, cellProcessed, maskHalfWidths, config, riverIndex, cellSize, forCarveMask: true);
                    }
                    if (lakeSpill)
                    {
                        ApplyTributaryLakeMouthVisualHalfWidths(
                            grid, cellProcessed, maskHalfWidths, config, riverIndex);
                        ApplyTributaryLakeMouthExtraMeshWidth(
                            grid, cellProcessed, maskHalfWidths, config, riverIndex);
                    }

                    if (!headwater)
                    {
                        ApplyLakeFirstTributaryShoreIntersectionWidthBoost(
                            grid, maskHalfWidths, cellProcessed, config, riverIndex, baseHalfW, cellSize, visualMeshOnly: false);
                    }
                    ApplyLakeFirstTributaryShoreIntersectionWidthBoost(
                        grid, meshHalfWidths, cellProcessed, config, riverIndex, baseHalfW, cellSize, visualMeshOnly: true);
                    if (!headwater)
                        ApplyLakeFirstMainJoinApproachMeshWiden(
                            grid, meshHalfWidths, cellProcessed, config, riverIndex);

                    // Inland: taper mesh+máscara en origen. Headwater: continuo + taper suave de ancho al inicio.
                    if (UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex))
                    {
                        SyncLakeFirstInlandMaskToLakeSpillCarve(meshHalfWidths, maskHalfWidths);
                        ApplyLakeFirstInlandFeederSourceWidthTaper(
                            meshHalfWidths, cellProcessed, grid, config, riverIndex, cellSize);
                        ApplyLakeFirstInlandFeederSourceWidthTaper(
                            maskHalfWidths, cellProcessed, grid, config, riverIndex, cellSize);
                        LogLakeFirstSupplementalMeshHook(grid, config, riverIndex, "EnsureRiverVisualSurfaceCache.Inland");
                    }
                    else if (headwater)
                    {
                        ApplyLakeFirstHeadwaterCarveLikeInland(
                            meshHalfWidths, maskHalfWidths, cellProcessed, grid, config, riverIndex, cellSize);
                        LogLakeFirstSupplementalMeshHook(grid, config, riverIndex, "EnsureRiverVisualSurfaceCache.Headwater");
                    }

                    AlignLakeFirstTributaryMeshToCarveReach(
                        grid, meshHalfWidths, cellProcessed, config, riverIndex, cellSize);
                    // Tras Align: reafirmar taper de CARVE en nacimiento y realinear mesh al surco.
                    if (headwater)
                    {
                        ApplyLakeFirstHeadwaterSourceWidthTaper(
                            maskHalfWidths, cellProcessed, grid, config, riverIndex, cellSize,
                            LakeFirstHeadwaterSourceWidthMinMulMask, LakeFirstHeadwaterSourceMinHalfCells);
                        SyncLakeFirstHeadwaterMeshToCarveMask(meshHalfWidths, maskHalfWidths, cellSize);
                        // Sync pisa boosts previos: reaplicar join mesh como inland/spill→main (hacia el receptor).
                        ApplyLakeFirstHeadwaterReceiverJoinMeshWiden(
                            grid, meshHalfWidths, cellProcessed, config, riverIndex, cellSize);
                        ApplyLakeFirstHeadwaterCarveToMeshGrowthProfile(
                            meshHalfWidths, maskHalfWidths, cellProcessed, cellSize);
                    }
                    float maskAvgBefore = 0f;
                    for (int wi = 0; wi < maskHalfWidths.Count; wi++)
                        maskAvgBefore += maskHalfWidths[wi];
                    if (maskHalfWidths.Count > 0)
                        maskAvgBefore /= maskHalfWidths.Count;
                    // Inland / lake-spill: contrato canal (reemplaza scale 0.9 suelto).
                    // Headwater ya aplicó growth profile arriba.
                    if (!headwater)
                    {
                        bool joinAtStart = false;
                        if (TryResolveTributaryMainJoinEndpointIndex(
                                grid, config, cellProcessed, riverIndex, out int joinEp))
                            joinAtStart = joinEp == 0;
                        else if (cellProcessed != null && cellProcessed.Count >= 2)
                        {
                            bool startMain = IsTributaryEndpointNearMain(
                                grid, config, cellSize, cellProcessed, 0);
                            bool endMain = IsTributaryEndpointNearMain(
                                grid, config, cellSize, cellProcessed, cellProcessed.Count - 1);
                            joinAtStart = startMain && !endMain;
                        }

                        ApplyLakeFirstChannelCarveToMeshGrowthProfile(
                            meshHalfWidths, maskHalfWidths, cellProcessed, cellSize, joinAtStart);
                        // Spill→main: growth deja margen foam; en la cuña Y el carve debe
                        // acercarse al mesh (orilla blanca fina, sin isla entre trib y troncal).
                        if (lakeSpill)
                            BoostLakeSpillMainJoinCarveMaskToMesh(
                                meshHalfWidths, maskHalfWidths, cellProcessed, grid, config, riverIndex);
                        // Inland: cap FINAL tras Align/Growth (si no, vuelven a ~spill width).
                        // Luego boost carve en junta con main + re-sync mesh>Ceil(mask)
                        // (sin eso Cap deja mesh más estrecho que el stamp → sin foam / fake Y).
                        // Ensanche local en T headwater (mid-body).
                        if (UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex))
                        {
                            CapLakeFirstInlandFeederHalfWidths(meshHalfWidths, maskHalfWidths, cellSize);
                            // Cap aplana la boca; restaurar ensanche inland→main (mismo patrón que T headwater).
                            ApplyLakeFirstMainJoinApproachMeshWiden(
                                grid, meshHalfWidths, cellProcessed, config, riverIndex);
                            BoostLakeSpillMainJoinCarveMaskToMesh(
                                meshHalfWidths, maskHalfWidths, cellProcessed, grid, config, riverIndex);
                            SyncLakeFirstInlandMeshOverCarveAfterCap(
                                meshHalfWidths, maskHalfWidths, cellSize);
                            ApplyLakeFirstInlandHeadwaterJoinMeshWiden(
                                grid, meshHalfWidths, cellProcessed, config, riverIndex, cellSize);
                            SyncLakeFirstInlandMeshOverCarveAfterCap(
                                meshHalfWidths, maskHalfWidths, cellSize);
                        }
                    }
                    if (config.uwpOwnedVisualPolicy || config.debugHydrologyNetwork)
                    {
                        float maskAvgAfter = 0f;
                        for (int wi = 0; wi < maskHalfWidths.Count; wi++)
                            maskAvgAfter += maskHalfWidths[wi];
                        if (maskHalfWidths.Count > 0)
                            maskAvgAfter /= maskHalfWidths.Count;
                        string kind = lakeSpill ? "LakeSpill" : headwater ? "HeadwaterFeeder" : "InlandFeeder";
                        Debug.Log(
                            $"[LakeFirstChannelContract] kind={kind} riverIndex={riverIndex} " +
                            $"meshOverCarve={LakeFirstChannelMeshOverCarveMul:F2} continuousCarve={(headwater ? 1 : 0)} " +
                            $"maskHalfAvgBefore={maskAvgBefore:F3} maskHalfAvgAfter={maskAvgAfter:F3} seed={config.seed}");
                    }
                }
                else if (!lakeFirstPipeline && riverIndex > 0 && config.uwpOwnedVisualPolicy)
                {
                    SyncTributaryMeshWidthsToMask(meshHalfWidths, maskHalfWidths, riverIndex);
                }
                surface.HalfWidthsWorld = meshHalfWidths ?? halfWidths;
                surface.MaskHalfWidthsWorld = maskHalfWidths ?? halfWidths;

                float rasterMargin = marginCells;
                if (lakeFirstPipeline && riverIndex > 0)
                {
                    if (IsLakeFirstHeadwaterFeeder(grid, riverIndex))
                        rasterMargin = Mathf.Max(0f, marginCells * 0.25f);
                    else if (UsesSupplementalFeederSourceEmergence(grid, riverIndex))
                        rasterMargin = marginCells + 0.35f;
                    else
                        rasterMargin = marginCells + 1.4f;
                }

                int maskCells = RasterStripCellSpaceToMask(
                    combinedMask, w, h, cellProcessed, maskHalfWidths ?? halfWidths, cellSize, rasterMargin);
                if (IsLakeFirstHeadwaterFeeder(grid, riverIndex) && maskCells <= 0 &&
                    (config.uwpOwnedVisualPolicy || config.debugHydrologyNetwork))
                {
                    Debug.LogWarning(
                        $"[LakeFirstHeadwaterMaskEmpty] riverIndex={riverIndex} maskCells=0 finalPts={cellProcessed.Count} " +
                        $"rasterMargin={rasterMargin:F2} seed={config.seed}");
                }
                if (riverIndex == 0 && config.uwpOwnedVisualPolicy)
                    mainOnlyMask = CloneBoolMask(combinedMask, w, h);
                if (logDetail)
                {
                    Debug.Log(
                        $"[RiverVisualSurfaceCache] riverIndex={riverIndex} rawPoints={surface.RawFunctionalCenterlineCells.Count} " +
                        $"finalPoints={cellProcessed.Count} maskCells={maskCells} lockedAnchors={lockedAnchors.Count} builtOnce=1 " +
                        $"source=functional_centerline");
                }

                surfaces.Add(surface);
            }

            // Inland se construye ANTES que Headwater: sin un 2º pase la T mid-body
            // no ve half reales del headwater → cuña interior sin foam (seed tip. 177848631).
            if (lakeFirstPipeline && config.uwpOwnedVisualPolicy && surfaces != null)
            {
                grid.RiverVisualSurfaces = surfaces;
                float csPass = Mathf.Max(0.01f, grid.CellSizeWorld);
                for (int ri = 1; ri < surfaces.Count; ri++)
                {
                    if (!UwpTributaryOriginUtility.IsInlandFeeder(grid, ri))
                        continue;
                    var surf = surfaces[ri];
                    if (surf?.FinalCenterlineCells == null ||
                        surf.HalfWidthsWorld == null ||
                        surf.MaskHalfWidthsWorld == null ||
                        surf.FinalCenterlineCells.Count < 4 ||
                        surf.HalfWidthsWorld.Count != surf.FinalCenterlineCells.Count)
                        continue;
                    ApplyLakeFirstInlandHeadwaterJoinMeshWiden(
                        grid, surf.HalfWidthsWorld, surf.FinalCenterlineCells, config, ri, csPass);
                    SyncLakeFirstInlandMeshOverCarveAfterCap(
                        surf.HalfWidthsWorld, surf.MaskHalfWidthsWorld, csPass);
                }
            }

            int maskBefore = CountMaskTrue(combinedMask, w, h);
            int oppositePruned = 0;
            int mainCoreRestored = 0;
            int skippedMouthPruned = 0;
            if (config.uwpOwnedVisualPolicy)
            {
                oppositePruned = PruneConfluenceOppositeBankMask(combinedMask, w, h, grid, config, surfaces);
                if (mainOnlyMask != null)
                    mainCoreRestored = ProtectMainRiverMaskCore(combinedMask, mainOnlyMask, grid, config);
                skippedMouthPruned = PruneSkippedTributaryConfluenceMouthMask(combinedMask, w, h, grid, config, surfaces);
            }
            else if (!lakeFirstPipeline)
            {
                MorphologicalClose1(combinedMask, w, h);
            }

            // Lake-first: sin MorphologicalClose1 (dilataba tip headwater → orilla/foam ancha).

            int maskAfter = CountMaskTrue(combinedMask, w, h);
            if (logDetail || config.uwpOwnedVisualPolicy || lakeFirstPipeline)
            {
                Debug.Log(
                    $"[RiverVisualContinuity] uwp={(config.uwpOwnedVisualPolicy ? 1 : 0)} lakeFirst={(lakeFirstPipeline ? 1 : 0)} " +
                    $"holesFilled={Mathf.Max(0, maskAfter - maskBefore)} " +
                    $"oppositeBankPruned={oppositePruned} skippedMouthPruned={skippedMouthPruned} " +
                    $"mainCoreRestored={mainCoreRestored} strayRemoved=0 " +
                    $"maskCellsBefore={maskBefore} maskCellsAfter={maskAfter}");
            }

            grid.RiverVisualSurfaces = surfaces;
            grid.RiverVisualSurfaceMask = combinedMask;
            grid.RiverVisualSurfacesBuilt = true;
            ResetUwpSurfaceAuditFields(grid);
            return true;
        }

        const float UwpDegenerateTributaryMinLengthWorldM = 3f;
        const int UwpDegenerateTributaryMaxFinalPoints = 3;

        public static bool IsUwpDegenerateTributary(List<Vector2> finalPath, float cellSizeWorld) =>
            IsUwpDegenerateTributary(finalPath, cellSizeWorld, null, -1);

        public static bool IsUwpDegenerateTributary(
            List<Vector2> finalPath,
            float cellSizeWorld,
            GridSystem grid,
            int riverIndex)
        {
            if (finalPath == null || finalPath.Count == 0)
                return true;

            if (grid != null && riverIndex > 0 &&
                UwpTributaryOriginUtility.IsSupplemental(grid, riverIndex))
            {
                if (finalPath.Count < 2)
                    return true;
                float supLen = ComputePolylineLengthCells(finalPath) * Mathf.Max(0.01f, cellSizeWorld);
                return supLen < 0.85f;
            }

            if (finalPath.Count <= UwpDegenerateTributaryMaxFinalPoints)
                return true;
            float lengthWorld = ComputePolylineLengthCells(finalPath) * Mathf.Max(0.01f, cellSizeWorld);
            return lengthWorld < UwpDegenerateTributaryMinLengthWorldM;
        }

        static void ApplyUwpDegenerateTributarySkip(
            RiverVisualSurfaceData surface,
            int riverIndex,
            List<Vector2> finalPath,
            float cellSizeWorld)
        {
            if (surface == null)
                return;
            surface.Skipped = true;
            surface.SkipReason = "degenerate_tributary";
            surface.MeshBuilt = false;
            surface.CarveApplied = false;
            surface.CrossfordApplied = false;
            surface.LengthMesh = 0f;
            surface.LengthCarve = 0f;
            float lengthWorld = ComputePolylineLengthCells(finalPath) * Mathf.Max(0.01f, cellSizeWorld);
            Debug.Log(
                $"[UWP_SKIP_DEGENERATE_TRIBUTARY] riverIndex={riverIndex} finalPts={finalPath?.Count ?? 0} " +
                $"length={lengthWorld:F2} reason=degenerate_tributary");
        }

        static bool UwpCachedSurfacesHaveUnskippedDegenerateTributary(GridSystem grid, MapGenConfig config)
        {
            if (config == null || !config.uwpOwnedVisualPolicy || grid?.RiverVisualSurfaces == null)
                return false;
            float cellSize = grid.CellSizeWorld;
            for (int i = 1; i < grid.RiverVisualSurfaces.Count; i++)
            {
                RiverVisualSurfaceData surface = grid.RiverVisualSurfaces[i];
                if (surface == null || surface.Skipped)
                    continue;
                if (IsUwpDegenerateTributary(surface.FinalCenterlineCells, cellSize, grid, i))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// UWP: resuelve trims/culls/skips finales, congela cache y resetea auditoría mesh/carve.
        /// Debe llamarse una vez antes de mesh y terreno.
        /// </summary>
        public static bool FreezeUwpFinalWaterVisualSurfaceCache(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null)
                return false;
            if (!config.uwpOwnedVisualPolicy)
                return EnsureRiverVisualSurfaceCache(grid, config);

            grid.RiverVisualSurfaceCacheFrozen = false;
            grid.ClearRiverVisualSurfaceCache();
            if (!EnsureRiverVisualSurfaceCache(grid, config))
                return false;

            grid.RiverVisualSurfaceCacheFrozen = true;
            ResetUwpSurfaceAuditFields(grid);
            AuditUwpFrozenTributaryGhostRisk(grid, config);
            LogLakeFirstSupplementalVisualAudit(grid, config);
            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[UWP] WaterVisualSurfaceCache congelada | surfaces={grid.RiverVisualSurfaces?.Count ?? 0} " +
                    $"maskBuilt={(grid.RiverVisualSurfaceMask != null ? 1 : 0)} seed={config.seed}");
            }

            return true;
        }

        const int UwpGhostRiskBboxExcessThresholdCells = 4;

        static void AuditUwpFrozenTributaryGhostRisk(GridSystem grid, MapGenConfig config)
        {
            if (grid?.RiverVisualSurfaces == null || grid.RiverVisualSurfaceMask == null || config == null)
                return;
            if (!grid.RiverVisualSurfaceCacheFrozen)
                return;

            bool[,] mask = grid.RiverVisualSurfaceMask;
            int w = grid.Width;
            int h = grid.Height;
            if (mask.GetLength(0) != w || mask.GetLength(1) != h)
                return;

            float cellSize = Mathf.Max(0.01f, grid.CellSizeWorld);
            bool logDetail = config.debugLogs || config.debugHydrologyNetwork;

            for (int i = 0; i < grid.RiverVisualSurfaces.Count; i++)
            {
                RiverVisualSurfaceData surface = grid.RiverVisualSurfaces[i];
                if (surface == null || surface.Skipped || surface.RiverIndex <= 0)
                    continue;

                var finalLine = surface.FinalCenterlineCells;
                if (finalLine == null || finalLine.Count < 2)
                    continue;

                int rawPts = surface.RawFunctionalCenterlineCells?.Count ?? 0;
                int finalPts = finalLine.Count;
                TryResolveTributaryJoinIndexFromFinalPath(
                    grid, config, finalLine, surface.RiverIndex, out int confluenceIdx);

                float meshHalfSum = 0f;
                int meshHalfCount = 0;
                if (surface.HalfWidthsWorld != null)
                {
                    for (int hi = 0; hi < surface.HalfWidthsWorld.Count; hi++)
                    {
                        meshHalfSum += surface.HalfWidthsWorld[hi];
                        meshHalfCount++;
                    }
                }

                float maskHalfSum = 0f;
                int maskHalfCount = 0;
                if (surface.MaskHalfWidthsWorld != null)
                {
                    for (int hi = 0; hi < surface.MaskHalfWidthsWorld.Count; hi++)
                    {
                        maskHalfSum += surface.MaskHalfWidthsWorld[hi];
                        maskHalfCount++;
                    }
                }

                float meshHalfAvg = meshHalfCount > 0 ? meshHalfSum / meshHalfCount : 0f;
                float maskHalfAvg = maskHalfCount > 0 ? maskHalfSum / maskHalfCount : meshHalfAvg;
                float maskHalfCells = maskHalfAvg / cellSize;

                float fMinX = float.MaxValue, fMaxX = float.MinValue;
                float fMinZ = float.MaxValue, fMaxZ = float.MinValue;
                for (int pi = 0; pi < finalLine.Count; pi++)
                {
                    fMinX = Mathf.Min(fMinX, finalLine[pi].x);
                    fMaxX = Mathf.Max(fMaxX, finalLine[pi].x);
                    fMinZ = Mathf.Min(fMinZ, finalLine[pi].y);
                    fMaxZ = Mathf.Max(fMaxZ, finalLine[pi].y);
                }

                float attribRadius = maskHalfCells + 2f;
                float attribRadiusSq = attribRadius * attribRadius;
                int maskCells = 0;
                float mMinX = float.MaxValue, mMaxX = float.MinValue;
                float mMinZ = float.MaxValue, mMaxZ = float.MinValue;
                bool hasMaskBbox = false;

                for (int z = 0; z < h; z++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (!mask[x, z])
                            continue;
                        Vector2 p = new Vector2(x + 0.5f, z + 0.5f);
                        if (DistanceSqToPolylineCellSpace(p, finalLine) > attribRadiusSq)
                            continue;
                        maskCells++;
                        hasMaskBbox = true;
                        mMinX = Mathf.Min(mMinX, x);
                        mMaxX = Mathf.Max(mMaxX, x);
                        mMinZ = Mathf.Min(mMinZ, z);
                        mMaxZ = Mathf.Max(mMaxZ, z);
                    }
                }

                if (logDetail)
                {
                    Debug.Log(
                        $"[UWP HYDRO TRIB AUDIT] trib={surface.RiverIndex} rawPts={rawPts} finalPts={finalPts} " +
                        $"maskCells={maskCells} meshHalfAvg={meshHalfAvg:F3} maskHalfAvg={maskHalfAvg:F3} " +
                        $"confluenceIdx={confluenceIdx}");
                }

                if (!hasMaskBbox)
                    continue;

                float pad = maskHalfCells + UwpGhostRiskBboxExcessThresholdCells;
                float excessMinX = Mathf.Max(0f, (fMinX - pad) - mMinX);
                float excessMaxX = Mathf.Max(0f, mMaxX - (fMaxX + pad));
                float excessMinZ = Mathf.Max(0f, (fMinZ - pad) - mMinZ);
                float excessMaxZ = Mathf.Max(0f, mMaxZ - (fMaxZ + pad));
                float excess = Mathf.Max(Mathf.Max(excessMinX, excessMaxX), Mathf.Max(excessMinZ, excessMaxZ));
                if (excess <= UwpGhostRiskBboxExcessThresholdCells)
                    continue;

                Debug.LogWarning(
                    $"[UWP HYDRO GHOST RISK] trib={surface.RiverIndex} rawPts={rawPts} finalPts={finalPts} " +
                    $"maskCells={maskCells} meshHalfAvg={meshHalfAvg:F3} maskHalfAvg={maskHalfAvg:F3} " +
                    $"confluenceIdx={confluenceIdx} bboxExcessCells={excess:F1} threshold={UwpGhostRiskBboxExcessThresholdCells} " +
                    $"finalBbox=({fMinX:F1},{fMinZ:F1})-({fMaxX:F1},{fMaxZ:F1}) maskBbox=({mMinX},{mMinZ})-({mMaxX},{mMaxZ})");
            }
        }

        static void ResetUwpSurfaceAuditFields(GridSystem grid)
        {
            if (grid?.RiverVisualSurfaces == null)
                return;
            for (int i = 0; i < grid.RiverVisualSurfaces.Count; i++)
            {
                var surface = grid.RiverVisualSurfaces[i];
                if (surface == null)
                    continue;
                surface.MeshBuilt = false;
                surface.CarveApplied = false;
                surface.LengthMesh = 0f;
                surface.LengthCarve = surface.Skipped ? 0f : ComputePolylineLengthCells(surface.FinalCenterlineCells);
                surface.CrossfordApplied = !surface.Skipped && HasCrossfordAlongCenterline(grid, surface);
            }
        }

        public static float ComputePolylineLengthCells(List<Vector2> line)
        {
            if (line == null || line.Count < 2)
                return 0f;
            float len = 0f;
            for (int i = 1; i < line.Count; i++)
                len += Vector2.Distance(line[i - 1], line[i]);
            return len;
        }

        static bool HasCrossfordAlongCenterline(GridSystem grid, RiverVisualSurfaceData surface)
        {
            if (grid == null || surface == null)
                return false;
            if (surface.FordAnchorCount > 0)
                return true;
            if (surface.FinalCenterlineCells == null || surface.FinalCenterlineCells.Count < 1)
                return false;
            int w = grid.Width;
            int h = grid.Height;
            for (int i = 0; i < surface.FinalCenterlineCells.Count; i++)
            {
                int cx = Mathf.Clamp(Mathf.RoundToInt(surface.FinalCenterlineCells[i].x), 0, w - 1);
                int cz = Mathf.Clamp(Mathf.RoundToInt(surface.FinalCenterlineCells[i].y), 0, h - 1);
                if (grid.GetCell(cx, cz).riverFord)
                    return true;
            }

            return false;
        }

        static void MarkUwpRiverMeshBuilt(GridSystem grid, int riverIndex, List<Vector2> cellProcessed)
        {
            if (grid?.RiverVisualSurfaces == null || riverIndex < 0 || riverIndex >= grid.RiverVisualSurfaces.Count)
                return;
            var surface = grid.RiverVisualSurfaces[riverIndex];
            if (surface == null || surface.Skipped)
                return;
            surface.MeshBuilt = true;
            surface.LengthMesh = ComputePolylineLengthCells(cellProcessed);
        }

        public static void MarkUwpRiverCarveApplied(GridSystem grid, int riverIndex, List<Vector2> carveLine)
        {
            if (grid?.RiverVisualSurfaces == null || riverIndex < 0 || riverIndex >= grid.RiverVisualSurfaces.Count)
                return;
            var surface = grid.RiverVisualSurfaces[riverIndex];
            if (surface == null || surface.Skipped)
                return;
            surface.CarveApplied = true;
            surface.LengthCarve = ComputePolylineLengthCells(carveLine);
        }

        /// <summary>
        /// Tras TerrainExporter carve: si el ribbon flota sobre la ORILLA (no el lecho),
        /// baja esos vértices para recuperar foam blanca. No pega el agua al floor del canal.
        /// </summary>
        public static void SnapBuiltRiverMeshesToTerrainContact(
            GameObject waterRoot,
            Terrain terrain,
            GridSystem grid,
            MapGenConfig config)
        {
            if (waterRoot == null || terrain == null || terrain.terrainData == null || config == null)
                return;

            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            float waterY = grid != null
                ? ResolveUwpLakeMouthDisplayLevelY(grid, config)
                : (terrain.transform.position.y + config.waterHeight01 * terrainY +
                   Mathf.Max(config.waterSurfaceOffset, 0.02f));

            // Solo tocar vértices cuya muestra de terreno está cerca del nivel de agua (banco/orilla).
            float bankBand = 0.22f;
            float maxFloat = 0.08f;
            float contactEps = 0.01f;
            int adjusted = 0;
            var filters = waterRoot.GetComponentsInChildren<MeshFilter>(true);
            for (int fi = 0; fi < filters.Length; fi++)
            {
                var mf = filters[fi];
                if (mf == null || mf.sharedMesh == null)
                    continue;
                string n = mf.gameObject.name;
                if (n == null ||
                    (!n.StartsWith("Water_RiverSurface") && !n.StartsWith("RiverSurface_")))
                    continue;

                Mesh src = mf.sharedMesh;
                Vector3[] verts = src.vertices;
                if (verts == null || verts.Length < 3)
                    continue;

                Transform tf = mf.transform;
                bool changed = false;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 world = tf.TransformPoint(verts[i]);
                    float groundY = terrain.SampleHeight(world) + terrain.transform.position.y;
                    // Lecho profundo: no snap (evitar ribbon en el floor).
                    if (groundY < waterY - bankBand)
                        continue;
                    if (world.y <= groundY + maxFloat)
                        continue;

                    world.y = Mathf.Min(world.y, Mathf.Max(groundY + contactEps, waterY - 0.02f));
                    verts[i] = tf.InverseTransformPoint(world);
                    changed = true;
                }

                if (!changed)
                    continue;

                Mesh copy = Object.Instantiate(src);
                copy.name = src.name + "_ShoreSnap";
                copy.vertices = verts;
                copy.RecalculateBounds();
                mf.sharedMesh = copy;
                var mc = mf.GetComponent<MeshCollider>();
                if (mc != null)
                    mc.sharedMesh = copy;
                adjusted++;
            }

            if (adjusted > 0 && (config.debugLogs || config.debugHydrologyNetwork || config.uwpOwnedVisualPolicy))
            {
                Debug.Log($"[RiverShoreContactSnap] meshesAdjusted={adjusted} seed={config.seed}");
            }
        }

        public static void ValidateAndLogUwpWaterSurfaceFinal(GridSystem grid, MapGenConfig config, GameObject waterRoot)
        {
            if (grid?.RiverVisualSurfaces == null || config == null || !config.uwpOwnedVisualPolicy)
                return;

            int violations = 0;
            const float lengthTol = 0.10f;
            for (int i = 0; i < grid.RiverVisualSurfaces.Count; i++)
            {
                var surface = grid.RiverVisualSurfaces[i];
                if (surface == null)
                    continue;

                string goName = ResolveRiverSurfaceGameObjectName(grid, i);
                bool meshGoExists = RiverSurfaceGameObjectExists(
                    waterRoot != null ? waterRoot.transform : null, grid, i);
                bool meshBuilt = surface.MeshBuilt || meshGoExists;
                string type = i == 0
                    ? "main"
                    : UwpTributaryOriginUtility.GetOrigin(grid, i).ToString();
                float refLen = Mathf.Max(surface.LengthCarve, surface.LengthMesh, 1e-4f);
                float lenDelta = Mathf.Abs(surface.LengthMesh - surface.LengthCarve) / refLen;

                if (surface.Skipped)
                {
                    if (surface.CarveApplied)
                    {
                        violations++;
                        Debug.LogError(
                            $"[UWP_WATER_SURFACE_FINAL] VIOLATION riverIndex={i} skipped surface has carveApplied=1 reason={surface.SkipReason}");
                    }

                    if (surface.CrossfordApplied)
                    {
                        violations++;
                        Debug.LogError(
                            $"[UWP_WATER_SURFACE_FINAL] VIOLATION riverIndex={i} skipped surface has crossford=1 reason={surface.SkipReason}");
                    }
                }
                else
                {
                    if (!meshBuilt)
                    {
                        violations++;
                        Debug.LogError($"[UWP_WATER_SURFACE_FINAL] VIOLATION riverIndex={i} valid surface missing mesh");
                    }

                    if (!surface.CarveApplied)
                    {
                        violations++;
                        Debug.LogError($"[UWP_WATER_SURFACE_FINAL] VIOLATION riverIndex={i} valid surface missing carve");
                    }

                    if (surface.CrossfordApplied && (!meshBuilt || !surface.CarveApplied))
                    {
                        violations++;
                        Debug.LogError($"[UWP_WATER_SURFACE_FINAL] VIOLATION riverIndex={i} crossford without mesh+carve");
                    }

                    if (meshBuilt && surface.CarveApplied && lenDelta > lengthTol)
                    {
                        violations++;
                        Debug.LogError(
                            $"[UWP_WATER_SURFACE_FINAL] VIOLATION riverIndex={i} length mismatch mesh={surface.LengthMesh:F2} carve={surface.LengthCarve:F2} delta={lenDelta:P1}");
                    }
                }

                Debug.Log(
                    $"[UWP_WATER_SURFACE_FINAL] riverIndex={i} type={type} skipped={(surface.Skipped ? 1 : 0)} " +
                    $"finalCenterlineCount={(surface.FinalCenterlineCells?.Count ?? 0)} meshBuilt={(meshBuilt ? 1 : 0)} " +
                    $"carveApplied={(surface.CarveApplied ? 1 : 0)} crossfordApplied={(surface.CrossfordApplied ? 1 : 0)} " +
                    $"lengthMesh={surface.LengthMesh:F2} lengthCarve={surface.LengthCarve:F2} skipReason={surface.SkipReason ?? "none"}");
            }

            if (violations > 0)
            {
                Debug.LogError(
                    $"[UWP_WATER_SURFACE_FINAL] {violations} violación(es) de contrato mesh/carve/crossford. seed={config.seed}");
            }
        }

        static List<float> CloneHalfWidths(List<float> src)
        {
            if (src == null)
                return null;
            return new List<float>(src);
        }

        static void ApplyMainMeshOnlyWidthScale(List<float> halfWidths, MapGenConfig config, int riverIndex, GridSystem grid)
        {
            if (halfWidths == null || config == null || !config.uwpOwnedVisualPolicy)
                return;
            float mul = riverIndex == 0
                ? Mathf.Clamp(config.riverSurfaceMainMeshOnlyWidthMul, 1f, 2.5f)
                : Mathf.Clamp(config.riverSurfaceTributaryMeshOnlyWidthMul, 1f, 2.5f);
            if (IsLakeSpillTributaryVisual(grid, config, riverIndex) ||
                UwpTributaryOriginUtility.IsInlandFeeder(grid, riverIndex))
                mul = Mathf.Max(mul, 1.24f);
            else if (IsLakeFirstHeadwaterFeeder(grid, riverIndex))
                mul = Mathf.Max(mul, 1.08f);
            if (mul <= 1.001f)
                return;
            for (int i = 0; i < halfWidths.Count; i++)
                halfWidths[i] = Mathf.Max(0.02f, halfWidths[i] * mul);
        }

        static List<Vector2> DensifyLakeFirstCenterlineForMask(
            List<Vector2> line,
            float spacingCells,
            bool fineStream = false)
        {
            if (line == null || line.Count < 2)
                return line;

            spacingCells = fineStream
                ? Mathf.Clamp(spacingCells, 0.15f, 0.42f)
                : Mathf.Clamp(spacingCells, 0.35f, 0.9f);
            float total = 0f;
            for (int i = 1; i < line.Count; i++)
                total += Vector2.Distance(line[i], line[i - 1]);

            if (total < spacingCells * 0.75f)
            {
                if (!fineStream)
                    return line;

                int forcedSamples = Mathf.Clamp(Mathf.Max(6, line.Count * 2), 4, 40);
                spacingCells = Mathf.Max(0.15f, total / Mathf.Max(1, forcedSamples - 1));
            }

            int samples = Mathf.Clamp(Mathf.CeilToInt(total / spacingCells) + 1, line.Count, 320);
            var densified = new List<Vector2>(samples);
            float step = total / (samples - 1);
            float acc = 0f;
            int seg = 0;
            for (int s = 0; s < samples; s++)
            {
                float target = s * step;
                while (seg < line.Count - 2)
                {
                    float segLen = Vector2.Distance(line[seg], line[seg + 1]);
                    if (acc + segLen >= target)
                        break;
                    acc += segLen;
                    seg++;
                }

                float localLen = Vector2.Distance(line[seg], line[seg + 1]);
                float t = localLen > 1e-6f ? (target - acc) / localLen : 0f;
                densified.Add(Vector2.Lerp(line[seg], line[seg + 1], Mathf.Clamp01(t)));
            }

            return densified.Count >= 2 ? densified : line;
        }

        static int RasterStripCellSpaceToMask(
            bool[,] mask,
            int w,
            int h,
            List<Vector2> cellPath,
            List<float> halfWidthsWorld,
            float cellSize,
            float marginCells)
        {
            int added = 0;
            int n = cellPath != null ? cellPath.Count : 0;
            if (n < 2 || halfWidthsWorld == null || halfWidthsWorld.Count != n)
                return 0;
            float invCs = 1f / Mathf.Max(1e-5f, cellSize);
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 a = cellPath[i];
                Vector2 b = cellPath[i + 1];
                float hwCells =
                    0.5f * (halfWidthsWorld[i] + halfWidthsWorld[i + 1]) * invCs + marginCells;
                float pad = hwCells + 2f;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x) - pad), 0, w - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x) + pad), 0, w - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y) - pad), 0, h - 1);
                int z1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y) + pad), 0, h - 1);
                for (int cz = z0; cz <= z1; cz++)
                {
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        Vector2 p = new Vector2(cx + 0.5f, cz + 0.5f);
                        float d = DistancePointToOpenSegment2D(p, a, b);
                        if (d <= hwCells)
                        {
                            if (!mask[cx, cz])
                            {
                                mask[cx, cz] = true;
                                added++;
                            }
                        }
                    }
                }
            }

            return added;
        }

        static float DistancePointToOpenSegment2D(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = Vector2.Dot(ab, ab);
            if (denom < 1e-10f)
                return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
            Vector2 proj = a + ab * t;
            return Vector2.Distance(p, proj);
        }

        static int CountMaskTrue(bool[,] mask, int w, int h)
        {
            int c = 0;
            if (mask == null)
                return 0;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    if (mask[x, z])
                        c++;
            return c;
        }

        static void MorphologicalClose1(bool[,] mask, int w, int h)
        {
            if (mask == null)
                return;
            var dil = new bool[w, h];
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool v = mask[x, z];
                    if (!v)
                    {
                        for (int dz = -1; dz <= 1 && !v; dz++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, nz = z + dz;
                                if ((uint)nx < (uint)w && (uint)nz < (uint)h && mask[nx, nz])
                                {
                                    v = true;
                                    break;
                                }
                            }
                        }
                    }

                    dil[x, z] = v;
                }
            }

            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool v = dil[x, z];
                    if (v)
                    {
                        for (int dz = -1; dz <= 1 && v; dz++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, nz = z + dz;
                                if ((uint)nx < (uint)w && (uint)nz < (uint)h && !dil[nx, nz])
                                {
                                    v = false;
                                    break;
                                }
                            }
                        }
                    }

                    mask[x, z] = v;
                }
            }
        }

        static Vector3 PerpendicularXZ(Vector3 tangent)
        {
            Vector3 right = Vector3.Cross(Vector3.up, tangent);
            right.y = 0f;
            if (right.sqrMagnitude < 1e-12f)
                right = Vector3.right;
            else
                right.Normalize();
            return right;
        }
    }
}
