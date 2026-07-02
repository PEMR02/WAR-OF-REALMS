using System.Collections.Generic;
using PMG.UnifiedWorldPipeline;
using Project.Gameplay.Map;
using Project.Gameplay.Map.Generation;
using Project.Gameplay.Map.Generator;
using UnityEditor;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline.Editor
{
    public struct PMGUnifiedWorldGenerationResult
    {
        public bool success;
        public string failureReason;
        public PMGUnifiedWorldQualityReport report;
        public GridSystem grid;
        public List<CityNode> cities;
        public List<Road> roads;
        public MapGenConfig runtimeConfig;
        public MapGenerator generator;
        public GameObject tempHost;
    }

    /// <summary>
    /// Ejecuta el Generador Definitivo (MapGenerator) con MatchConfigCompiler — mismo camino que RTSMapGenerator.
    /// </summary>
    public static class PMGUnifiedWorldPipelineRunner
    {
        public const string UwpRunnerVersion = "2025-06-25-frozen-water-surface-cache-v1";

        static IUwpSceneVisualBindings ResolveSceneBindings(PMGUnifiedWorldPipelineConfig config)
        {
            if (config == null)
                return null;
            var resolved = UwpSceneBindingsUtility.Resolve(config);
            return resolved ?? UwpSceneBindingsUtility.FindInScene();
        }

        /// <summary>Semillas de smoke-test para calidad de tributarios (3/3 + routing).</summary>
        public static readonly int[] TributaryProbeSeeds =
        {
            284719, 12121212, 482910, 90341, 556677, 314159, 771122, 998877, 424242, 650321,
        };

        /// <summary>Seed de preview UWP: relieve suave, ríos legibles, lagos dispersos.</summary>
        public const int RecommendedPreviewSeed = 284719;

        public static PMGUnifiedWorldGenerationResult GenerateLogic(PMGUnifiedWorldPipelineConfig config, int? seedOverride = null)
        {
            var result = new PMGUnifiedWorldGenerationResult();
            if (config == null)
            {
                result.failureReason = "PipelineConfig null.";
                result.report = PMGUnifiedWorldQualityEvaluator.Evaluate(
                    null, null, null, null, config, 0, false, result.failureReason);
                return result;
            }

            config.EnsureDefaultsFromProject();

            int seed = seedOverride ?? config.ResolveSeed();
            MapGenConfig cfg = BuildRuntimeMapGenConfig(config, seed, out string configFail);
            if (cfg == null)
            {
                result.failureReason = configFail ?? "No se pudo compilar MapGenConfig.";
                result.report = PMGUnifiedWorldQualityEvaluator.Evaluate(
                    null, null, null, null, config, seed, false, result.failureReason);
                return result;
            }

            IUwpSceneVisualBindings sceneBindings = ResolveSceneBindings(config);

            GameObject host = new GameObject("PMG_UWP_TempGenerator");
            host.hideFlags = HideFlags.HideAndDontSave;
            var generator = host.AddComponent<MapGenerator>();
            ApplyVisualBindings(config, cfg, generator, sceneBindings);

            bool ok = generator.Generate(
                cfg,
                null,
                skipSurfaceExport: true,
                skipRoadConnectivityValidation: config.skipRoadConnectivityInPreview);

            result.tempHost = host;
            result.generator = generator;
            result.runtimeConfig = cfg;
            result.grid = generator.Grid;
            result.cities = generator.Cities;
            result.roads = generator.Roads;
            result.success = ok;
            result.failureReason = ok ? string.Empty : "Validación MapGenerator fallida o reintentos agotados.";
            result.report = PMGUnifiedWorldQualityEvaluator.Evaluate(
                generator.Grid,
                cfg,
                generator.Cities,
                generator.Roads,
                config,
                seed,
                ok,
                result.failureReason);

            if (config.checklist != null)
            {
                config.checklist.ApplyReportToChecklist(result.report);
                EditorUtility.SetDirty(config.checklist);
            }

            return result;
        }

        public static PMGUnifiedWorldBatchSummary EvaluateBatch(PMGUnifiedWorldPipelineConfig config, IReadOnlyList<int> seeds, int topN)
        {
            var all = new List<PMGUnifiedWorldQualityReport>(seeds.Count);
            for (int i = 0; i < seeds.Count; i++)
            {
                PMGUnifiedWorldGenerationResult gen = GenerateLogic(config, seeds[i]);
                CleanupTemp(ref gen);
                all.Add(gen.report);
            }

            all.Sort((a, b) => b.totalGrade0To10.CompareTo(a.totalGrade0To10));
            int take = Mathf.Clamp(topN, 1, all.Count);
            var top = new PMGUnifiedWorldQualityReport[take];
            for (int i = 0; i < take; i++)
                top[i] = all[i];

            return new PMGUnifiedWorldBatchSummary
            {
                all = all.ToArray(),
                top = top,
                evaluatedCount = seeds.Count
            };
        }

        /// <summary>Smoke-test: tributarios colocados por semilla (solo lógica, sin mesh).</summary>
        public static void RunTributaryProbeBatch(PMGUnifiedWorldPipelineConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[UWP] Probe: PipelineConfig null.");
                return;
            }

            int wanted = Mathf.Clamp(config.uwpTributaryCount, 0, 6);
            int ok = 0;
            var lines = new System.Text.StringBuilder();
            lines.AppendLine(
                $"[UWP] Tributary probe | wanted={wanted} seeds={TributaryProbeSeeds.Length} v={UwpRunnerVersion} " +
                $"independent={config.uwpIndependentMode} compiler={UwpRuntimeProfileCompiler.CompilerVersion}");

            for (int i = 0; i < TributaryProbeSeeds.Length; i++)
            {
                int seed = TributaryProbeSeeds[i];
                PMGUnifiedWorldGenerationResult gen = GenerateLogic(config, seed);
                int centerlines = gen.grid != null && gen.grid.RiverCenterlinesCellSpace != null
                    ? gen.grid.RiverCenterlinesCellSpace.Count
                    : 0;
                int tribs = Mathf.Max(0, centerlines - 1);
                int confluences = gen.grid?.RiverConfluences != null ? gen.grid.RiverConfluences.Count : 0;
                bool uwpOwned = gen.runtimeConfig != null && gen.runtimeConfig.uwpOwnedVisualPolicy;
                bool pass = gen.success && tribs >= wanted;
                if (pass) ok++;

                lines.AppendLine(
                    $"  seed={seed} ok={(pass ? 1 : 0)} tribs={tribs}/{wanted} conf={confluences} " +
                    $"rivers={centerlines} uwpOwned={(uwpOwned ? 1 : 0)} grade={gen.report.totalGrade0To10:F1}");
                CleanupTemp(ref gen);
            }

            lines.AppendLine($"[UWP] Tributary probe summary | pass={ok}/{TributaryProbeSeeds.Length}");
            Debug.LogWarning(lines.ToString());
        }

        public static PMGUnifiedWorldSessionRoot ApplyFullToScene(
            PMGUnifiedWorldPipelineConfig config,
            int? seedOverride = null,
            PMGUnifiedWorldSessionRoot reuseRoot = null)
        {
            if (config == null)
            {
                Debug.LogError("[UWP] PipelineConfig null.");
                return reuseRoot;
            }

            Debug.LogWarning(
                $"[UWP] ApplyFullToScene START | runner={UwpRunnerVersion} " +
                $"mainWidth={config.uwpMainRiverFullWidthCells:F2}c carve={config.uwpMainRiverCarveDepthWorld:F3}m " +
                $"config={UnityEditor.AssetDatabase.GetAssetPath(config)}");

            config.EnsureDefaultsFromProject();

            int seed = seedOverride ?? config.ResolveSeed();
            MapGenConfig cfg = BuildRuntimeMapGenConfig(config, seed, out string configFail);
            if (cfg == null)
            {
                Debug.LogError("[UWP] " + (configFail ?? "No se pudo compilar MapGenConfig."));
                return reuseRoot;
            }

            IUwpSceneVisualBindings sceneBindings = ResolveSceneBindings(config);

            GameObject root = reuseRoot != null ? reuseRoot.gameObject : null;
            bool created = false;
            if (root == null)
            {
                string rootName = $"PMG Unified World - {seed}";
                GameObject existing = GameObject.Find(rootName);
                if (existing != null)
                    Undo.DestroyObjectImmediate(existing);

                root = new GameObject(rootName);
                Undo.RegisterCreatedObjectUndo(root, "Create PMG Unified World");
                created = true;
            }
            else
            {
                Undo.RecordObject(root, "Regenerate PMG Unified World");
                ClearChildren(root);
                root.name = $"PMG Unified World - {seed}";
            }

            float worldSize = cfg.gridW * cfg.cellSizeWorld;
            float terrainY = cfg.terrainHeightWorld > 0f ? cfg.terrainHeightWorld : 48f;
            var terrainData = new TerrainData();
            int hmRes = cfg.heightmapResolution > 33
                ? cfg.heightmapResolution
                : Mathf.Clamp(cfg.gridW + 1, 33, 2049);
            if ((hmRes & 1) == 0) hmRes++;
            terrainData.heightmapResolution = hmRes;
            terrainData.size = new Vector3(worldSize, terrainY, worldSize);
            terrainData.alphamapResolution = Mathf.Min(512, hmRes);

            string folder = UwpAssetPaths.SessionsRoot;
            UwpBootstrapAssets.EnsureFolderTree();

            string terrainPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/TerrainData_{seed}.asset");
            AssetDatabase.CreateAsset(terrainData, terrainPath);

            GameObject terrainGo = Terrain.CreateTerrainGameObject(terrainData);
            terrainGo.name = $"Terrain - {seed}";
            terrainGo.transform.SetParent(root.transform, false);
            if (ResolveCenterAtOrigin(config, sceneBindings))
                terrainGo.transform.position = new Vector3(-worldSize * 0.5f, 0f, -worldSize * 0.5f);
            else
                terrainGo.transform.position = cfg.origin;

            Undo.RegisterCreatedObjectUndo(terrainGo, created ? "Create UWP Terrain" : "Regenerate UWP Terrain");

            ClearSceneWaterRoot();

            GameObject host = new GameObject("PMG_UWP_GeneratorHost");
            host.hideFlags = HideFlags.HideAndDontSave;
            var generator = host.AddComponent<MapGenerator>();
            generator.terrain = terrainGo.GetComponent<Terrain>();
            ApplyVisualBindings(config, cfg, generator, sceneBindings);

            bool surfaceOk = generator.Generate(
                cfg,
                generator.terrain,
                skipSurfaceExport: true,
                skipRoadConnectivityValidation: config.skipRoadConnectivityInPreview);

            Terrain sceneTerrain = terrainGo.GetComponent<Terrain>();
            UwpRuntimeProfileCompiler.ApplyFullProfile(config, cfg);

            if (surfaceOk)
            {
                ApplyUwpSinglePassFrozenSurfacePipeline(
                    sceneTerrain, generator.Grid, cfg, config, generator, sceneBindings,
                    generator.Cities, generator.Roads);
                AttachSceneWaterToWorld(root);
            }

            PMGUnifiedWorldQualityReport report = PMGUnifiedWorldQualityEvaluator.Evaluate(
                generator.Grid,
                cfg,
                generator.Cities,
                generator.Roads,
                config,
                seed,
                surfaceOk,
                surfaceOk ? string.Empty : "Validación o export fallido.");

            Object.DestroyImmediate(host);
            Object.DestroyImmediate(cfg);

            PMGUnifiedWorldSessionRoot session = root.GetComponent<PMGUnifiedWorldSessionRoot>();
            if (session == null)
                session = Undo.AddComponent<PMGUnifiedWorldSessionRoot>(root);

            session.pipelineConfig = config;
            session.StoreReport(report);

            if (config.checklist != null)
            {
                config.checklist.ApplyReportToChecklist(report);
                MarkChecklistDone(config.checklist, "surf-terrain", surfaceOk);
                MarkChecklistDone(config.checklist, "surf-water", surfaceOk);
                bool splatOk = sceneTerrain != null && sceneTerrain.terrainData != null
                    && sceneTerrain.terrainData.terrainLayers != null
                    && sceneTerrain.terrainData.terrainLayers.Length > 0;
                MarkChecklistDone(config.checklist, "setup-materials", splatOk);
                MarkChecklistDone(config.checklist, "hydro-endpoints", !config.useMouthFusionWaterPipeline || cfg.waterVisualPipeline == WaterVisualPipelineMode.SplitLakeMsRiverMouthFusion);
                MarkChecklistInProgress(config.checklist, "polish-freeze");
                EditorUtility.SetDirty(config.checklist);
            }

            Selection.activeObject = root;
            EditorGUIUtility.PingObject(root);
            AssetDatabase.SaveAssets();

            Debug.Log($"[UWP] Mundo aplicado seed={seed} nota={report.totalGrade0To10:F1} ({report.totalGradeLetter}). Preview recomendada: {RecommendedPreviewSeed}");
            return session;
        }

        /// <summary>
        /// Importa heightmap del index.html (JSON) y opcionalmente reemplaza el agua del JSON
        /// por WaterGenerator + WaterMeshBuilder del juego.
        /// </summary>
        public static PMGUnifiedWorldSessionRoot ApplyJsonTerrainWithGameWater(
            PMGUnifiedWorldPipelineConfig config,
            TextAsset jsonOverride = null,
            PMGUnifiedWorldSessionRoot reuseRoot = null)
        {
            if (config == null)
            {
                Debug.LogError("[UWP] PipelineConfig null.");
                return reuseRoot;
            }

            TextAsset jsonAsset = jsonOverride != null ? jsonOverride : config.webTerrainJson;
            if (jsonAsset == null)
            {
                Debug.LogError("[UWP] Asigna webTerrainJson en el config o pásalo como parámetro.");
                return reuseRoot;
            }

            config.EnsureDefaultsFromProject();

            PMGWebTerrainJsonDto data;
            try
            {
                data = JsonUtility.FromJson<PMGWebTerrainJsonDto>(jsonAsset.text);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[UWP] JSON inválido: " + ex.Message);
                return reuseRoot;
            }

            if (data == null || data.sourceSize <= 1)
            {
                Debug.LogError("[UWP] JSON sin sourceSize válido.");
                return reuseRoot;
            }

            bool useVisual = config.jsonUseVisualHeight;
            if (data.recommendedUnity != null && data.recommendedUnity.useVisualHeight)
                useVisual = config.jsonUseVisualHeight;

            float[] source = useVisual && data.height != null && data.height.Length > 0
                ? data.height
                : data.rawHeight;
            if (source == null || source.Length != data.sourceSize * data.sourceSize)
            {
                Debug.LogError($"[UWP] Height array inválido en JSON (esperado {data.sourceSize * data.sourceSize}).");
                return reuseRoot;
            }

            int seed = ResolveJsonSeed(data.seed, config);
            bool flipZ = config.jsonFlipZ;

            float seaLevel01 = data.recommendedUnity != null ? data.recommendedUnity.seaLevel : 0.45f;

            MapGenConfig cfg = BuildRuntimeMapGenConfig(config, seed, out string configFail);
            if (cfg == null)
            {
                Debug.LogError("[UWP] " + (configFail ?? "No se pudo compilar MapGenConfig."));
                return reuseRoot;
            }

            cfg.waterHeight01 = Mathf.Clamp01(seaLevel01);
            UwpTerrainProfileModule.ApplyJsonPresentation(cfg, config, seaLevel01);
            if (config.applyRiverBankTerrainFix)
                UwpRiverProfileModule.ApplyBankTerrainFix(cfg);
            if (config.jsonUseGameWaterSystem && config.applyRiverVisualExportFix)
                UwpRiverProfileModule.ApplyExportFix(cfg, config);

            IUwpSceneVisualBindings sceneBindings = ResolveSceneBindings(config);

            if (!config.uwpIndependentMode && config.matchConfig != null)
                ApplyMatchGridLayout(config.matchConfig, cfg);
            else if (config.uwpIndependentMode)
                UwpGridLayoutUtility.ApplyPipelineLayout(config, cfg);

            GameObject root = reuseRoot != null ? reuseRoot.gameObject : null;
            bool created = false;
            if (root == null)
            {
                string label = string.IsNullOrEmpty(data.seed) ? seed.ToString() : SanitizeAssetLabel(data.seed);
                string rootName = $"PMG Unified World JSON - {label}";
                GameObject existing = GameObject.Find(rootName);
                if (existing != null)
                    Undo.DestroyObjectImmediate(existing);
                root = new GameObject(rootName);
                Undo.RegisterCreatedObjectUndo(root, "Create PMG JSON World");
                created = true;
            }
            else
            {
                Undo.RecordObject(root, "Regenerate PMG JSON World");
                ClearChildren(root);
            }

            float worldSize = cfg.gridW * cfg.cellSizeWorld;
            float terrainY = cfg.terrainHeightWorld > 0f ? cfg.terrainHeightWorld : 50f;
            var terrainData = new TerrainData();
            int hmRes = Mathf.Clamp(cfg.heightmapResolution > 0 ? cfg.heightmapResolution : cfg.gridW + 1, 33, 2049);
            if ((hmRes & 1) == 0) hmRes++;
            terrainData.heightmapResolution = hmRes;
            terrainData.size = new Vector3(worldSize, terrainY, worldSize);
            terrainData.alphamapResolution = Mathf.Min(512, hmRes);

            string folder = UwpAssetPaths.SessionsRoot;
            UwpBootstrapAssets.EnsureFolderTree();

            string terrainPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/TerrainData_JSON_{seed}.asset");
            AssetDatabase.CreateAsset(terrainData, terrainPath);

            GameObject terrainGo = Terrain.CreateTerrainGameObject(terrainData);
            terrainGo.name = $"Terrain JSON - {seed}";
            terrainGo.transform.SetParent(root.transform, false);
            if (ResolveCenterAtOrigin(config, sceneBindings))
                terrainGo.transform.position = new Vector3(-worldSize * 0.5f, 0f, -worldSize * 0.5f);
            else
                terrainGo.transform.position = cfg.origin;

            Undo.RegisterCreatedObjectUndo(terrainGo, created ? "Create UWP JSON Terrain" : "Regenerate UWP JSON Terrain");

            ClearSceneWaterRoot();

            var grid = new GridSystem(cfg.gridW, cfg.gridH, cfg.cellSizeWorld, cfg.origin);
            PopulateGridHeightsFromJson(grid, source, data.sourceSize, seaLevel01, flipZ);
            var rng = new XorShiftRng(seed);

            RegionGenerator.GenerateRegions(grid, cfg, rng);

            if (config.jsonUseGameWaterSystem)
            {
                WaterGenerator.GenerateWater(grid, cfg, rng);
                WaterDistanceField.Build(grid);
                HeightGenerator.ApplyHydrologySurfaceHeights(grid, cfg);
                HeightGenerator.GenerateFinalTerrainPass(grid, cfg);
                WaterSurfaceFieldBuilder.Build(grid, cfg);
            }

            GameObject host = new GameObject("PMG_UWP_JsonHost");
            host.hideFlags = HideFlags.HideAndDontSave;
            var generator = host.AddComponent<MapGenerator>();
            generator.terrain = terrainGo.GetComponent<Terrain>();
            ApplyVisualBindings(config, cfg, generator, sceneBindings);

            Terrain sceneTerrain = terrainGo.GetComponent<Terrain>();

            if (config.jsonUseGameWaterSystem)
            {
                var emptyCities = new List<CityNode>();
                var emptyRoads = new List<Road>();
                ApplyUwpSinglePassFrozenSurfacePipeline(
                    sceneTerrain, grid, cfg, config, generator, sceneBindings,
                    emptyCities, emptyRoads);
                AttachSceneWaterToWorld(root);
            }
            else
            {
                ExportUwpTerrainOnlyOnce(sceneTerrain, grid, cfg, config, generator, sceneBindings, logSplatSummary: true);
            }

            PMGUnifiedWorldQualityReport report = PMGUnifiedWorldQualityEvaluator.Evaluate(
                grid, cfg, null, null, config, seed, true, string.Empty);

            Object.DestroyImmediate(host);
            Object.DestroyImmediate(cfg);

            PMGUnifiedWorldSessionRoot session = root.GetComponent<PMGUnifiedWorldSessionRoot>();
            if (session == null)
                session = Undo.AddComponent<PMGUnifiedWorldSessionRoot>(root);
            session.pipelineConfig = config;
            session.StoreReport(report);

            Selection.activeObject = root;
            EditorGUIUtility.PingObject(root);
            AssetDatabase.SaveAssets();

            Debug.Log($"[UWP] JSON importado seed={seed} gameWater={config.jsonUseGameWaterSystem} nota={report.totalGrade0To10:F1}");
            return session;
        }

        static int ResolveJsonSeed(string jsonSeed, PMGUnifiedWorldPipelineConfig config)
        {
            if (config.useSeedOverride)
                return config.seedOverride;
            if (!string.IsNullOrEmpty(jsonSeed))
            {
                unchecked
                {
                    int h = 17;
                    foreach (char c in jsonSeed)
                        h = h * 31 + c;
                    return Mathf.Abs(h);
                }
            }
            return config.ResolveSeed();
        }

        static void ApplyUwpTerrainJsonPresentationFix(
            PMGUnifiedWorldPipelineConfig pipeline,
            MatchConfig match,
            MapGenConfig cfg,
            float seaLevel01)
        {
            if (cfg == null) return;

            float templateY = pipeline?.definitiveTemplate != null && pipeline.definitiveTemplate.terrainHeightWorld > 1f
                ? pipeline.definitiveTemplate.terrainHeightWorld
                : 50f;
            cfg.terrainHeightWorld = Mathf.Clamp(templateY * 0.76f, 32f, 40f);

            cfg.waterHeight01 = Mathf.Clamp01(seaLevel01);
            cfg.paintTerrainByHeight = true;
            ApplyUwpTerrainSplatLayerCap(cfg);
            EnsureUwpSkirtSettings(pipeline, cfg);
            cfg.macroTerrainEnabled = false;
            cfg.macroMountainMassCount = 0;
            cfg.macroBasinCount = 0;

            pipeline?.PullVisualBindingsFromTemplateIfEmpty();
            if (pipeline != null)
            {
                if (pipeline.grassLayer != null) cfg.grassLayer = pipeline.grassLayer;
                if (pipeline.dirtLayer != null) cfg.dirtLayer = pipeline.dirtLayer;
                if (pipeline.rockLayer != null) cfg.rockLayer = pipeline.rockLayer;
                if (pipeline.sandLayer != null) cfg.sandLayer = pipeline.sandLayer;
            }

            cfg.terrainNormalSmoothingPasses = 3;
            cfg.terrainNormalSmoothingStrength = 0.32f;
            cfg.shoreSmoothRadiusCells = 18;
            cfg.shoreSmoothStrength = 0.48f;
            ApplyUwpShallowerWaterBedCaps(cfg);
            ApplyUwpSoftRiverVisualCarves(cfg);
            ApplyUwpRiverVisualReliabilityFix(cfg);
            ApplyUwpRiverCenterlineQualityFix(cfg);
        }

        static void PopulateGridHeightsFromJson(
            GridSystem grid,
            float[] source,
            int sourceSize,
            float seaLevel01,
            bool flipZ)
        {
            if (grid == null || source == null || sourceSize <= 1) return;

            int w = grid.Width;
            int h = grid.Height;
            int srcMax = sourceSize - 1;
            for (int x = 0; x < w; x++)
            {
                float u = w > 1 ? x / (float)(w - 1) : 0f;
                for (int z = 0; z < h; z++)
                {
                    float v = h > 1 ? z / (float)(h - 1) : 0f;
                    float sx = u * srcMax;
                    float syRaw = v * srcMax;
                    float sy = flipZ ? srcMax - syRaw : syRaw;
                    float height = SampleJsonHeightBilinear(source, sourceSize, sx, sy);

                    ref var cell = ref grid.GetCell(x, z);
                    cell = CellData.Default();
                    cell.height01 = Mathf.Clamp01(height);
                    cell.type = height <= seaLevel01 + 0.002f ? CellType.Water : CellType.Land;
                }
            }

            HeightGenerator.RecalculateLandSlopes(grid, null);
        }

        static float SampleJsonHeightBilinear(float[] source, int sourceSize, float sx, float sy)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(sx), 0, sourceSize - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(sy), 0, sourceSize - 1);
            int x1 = Mathf.Min(sourceSize - 1, x0 + 1);
            int z1 = Mathf.Min(sourceSize - 1, z0 + 1);
            float tx = Mathf.Clamp01(sx - x0);
            float tz = Mathf.Clamp01(sy - z0);

            float a = source[z0 * sourceSize + x0];
            float b = source[z0 * sourceSize + x1];
            float c = source[z1 * sourceSize + x0];
            float d = source[z1 * sourceSize + x1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        static string SanitizeAssetLabel(string name)
        {
            if (string.IsNullOrEmpty(name)) return "json";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }

        [System.Serializable]
        class PMGWebTerrainJsonDto
        {
            public string schema;
            public int schemaVersion;
            public string seed;
            public int sourceSize;
            public int targetHeightmapResolution;
            public PMGWebTerrainJsonRecommended recommendedUnity;
            public float[] height;
            public float[] rawHeight;
        }

        [System.Serializable]
        class PMGWebTerrainJsonRecommended
        {
            public float terrainSizeXZ = 512f;
            public float terrainHeightY = 120f;
            public float seaLevel = 0.45f;
            public bool useVisualHeight = true;
            public bool flipZ = false;
        }

        public static void CleanupTemp(ref PMGUnifiedWorldGenerationResult result)
        {
            if (result.runtimeConfig != null)
            {
                Object.DestroyImmediate(result.runtimeConfig);
                result.runtimeConfig = null;
            }

            CleanupTempHostOnly(result);
        }

        static void CleanupTempHostOnly(PMGUnifiedWorldGenerationResult result)
        {
            if (result.tempHost != null)
            {
                Object.DestroyImmediate(result.tempHost);
                result.tempHost = null;
            }

            result.generator = null;
        }

        static MapGenConfig BuildRuntimeMapGenConfig(PMGUnifiedWorldPipelineConfig config, int seed, out string failReason)
        {
            failReason = null;
            if (config == null)
            {
                failReason = "PipelineConfig null.";
                return null;
            }

            if (config.uwpIndependentMode)
                return UwpRuntimeProfileCompiler.CreateRuntimeConfig(config, seed);

            MatchConfig match = config.ResolveMatchBaseline();
            if (match == null)
            {
                failReason = "Modo legacy: asigna Match baseline UWP o activa uwpIndependentMode.";
                return null;
            }

            var ctx = BuildContext(config);
            RuntimeMapGenerationSettings runtime = MatchConfigCompiler.Build(
                match,
                config.ResolveMapGenBaseline() ?? config.definitiveTemplate,
                ctx,
                logSummary: false);
            MapGenConfig cfg = runtime.CompiledMapGen;
            if (cfg == null)
            {
                failReason = "MatchConfigCompiler no produjo MapGenConfig.";
                return null;
            }

            cfg.seed = seed;
            ApplyMatchGridLayout(match, cfg);

            UwpRuntimeProfileCompiler.ApplyFullProfile(config, cfg);
            return cfg;
        }

        static MapGenerationRuntimeContext BuildContext(PMGUnifiedWorldPipelineConfig config)
        {
            if (config != null && config.uwpIndependentMode)
            {
                return new MapGenerationRuntimeContext
                {
                    applySceneHydrologyOverrides = false,
                    applyLobbyMacroRelief = false,
                    applyLegacyRiverWidthScale = false,
                    legacyRiverWidthScale = 1f,
                };
            }

            IUwpSceneVisualBindings sceneBindings = UwpSceneBindingsUtility.Resolve(config);
            bool useSceneHydrology = config.applySceneHydrologyOverrides
                                     && sceneBindings != null
                                     && config.matchConfig != null;
            return new MapGenerationRuntimeContext
            {
                applySceneHydrologyOverrides = useSceneHydrology,
                sceneRiverCount = useSceneHydrology ? sceneBindings.RiverCount : 0,
                sceneLakeCount = useSceneHydrology ? sceneBindings.LakeCount : 0,
                sceneMaxLakeCells = useSceneHydrology ? sceneBindings.MaxLakeCells : 0,
                applyLobbyMacroRelief = false,
                applyLegacyRiverWidthScale = true,
                legacyRiverWidthScale = 1.5f
            };
        }

        /// <summary>
        /// Mapa grande (~896 m) + hidrología desde PMGUnifiedWorldPipelineConfig (solo runtime UWP).
        /// </summary>
        static void ApplyUwpMapAndHydrologyLayout(
            MapGenConfig cfg,
            MatchConfig match,
            IUwpSceneVisualBindings sceneBindings,
            PMGUnifiedWorldPipelineConfig pipeline)
        {
            if (cfg == null) return;

            const float cellSizeWorld = 2.5f;
            const int gridCells = 358;

            cfg.cellSizeWorld = cellSizeWorld;
            cfg.gridW = gridCells;
            cfg.gridH = gridCells;

            bool centerAtOrigin = sceneBindings != null
                ? sceneBindings.CenterAtOrigin
                : match != null && match.map.centerAtOrigin;
            cfg.origin = centerAtOrigin
                ? new Vector3(-gridCells * cellSizeWorld * 0.5f, 0f, -gridCells * cellSizeWorld * 0.5f)
                : Vector3.zero;

            int hmRes = Mathf.Clamp(Mathf.ClosestPowerOfTwo(gridCells) + 1, 33, 2049);
            if ((hmRes & 1) == 0) hmRes++;
            cfg.heightmapResolution = hmRes;

            int mainRivers = pipeline != null ? Mathf.Clamp(pipeline.uwpMainRiverCount, 1, 1) : 1;
            int tributaries = pipeline != null ? Mathf.Clamp(pipeline.uwpTributaryCount, 0, 6) : 3;
            int lakes = pipeline != null ? Mathf.Clamp(pipeline.uwpLakeCount, 0, 12) : 2;
            int maxLakeCells = pipeline != null ? Mathf.Clamp(pipeline.uwpMaxLakeCells, 50, 12000) : 1400;

            cfg.riverCount = Mathf.Clamp(mainRivers + tributaries, 1, 8);
            cfg.lakeCount = lakes;
            cfg.maxLakeCells = maxLakeCells;
            cfg.allowFallbackCrossing = true;
            cfg.riverAvoidCrossingOtherRivers = false;
            cfg.riverPlacementMaxAttemptsPerRiver = Mathf.Max(cfg.riverPlacementMaxAttemptsPerRiver, 80);
            cfg.riverCorridorRejectEarlyAbort = Mathf.Min(cfg.riverCorridorRejectEarlyAbort, 8);
            cfg.maxTotalRiverBuildAttempts = Mathf.Max(cfg.maxTotalRiverBuildAttempts, 400);
            cfg.riverSurfaceTributaryWidthFixEnabled = true;

            ApplyUwpTributaryPlacementReliability(cfg);

            cfg.regionNoiseScale = Mathf.Max(cfg.regionNoiseScale, 0.022f);
            cfg.terrainMacroNoiseStrength = Mathf.Max(cfg.terrainMacroNoiseStrength, 0.15f);

            float lakeWorldM = Mathf.Sqrt(maxLakeCells) * cellSizeWorld;
            Debug.LogWarning(
                $"[UWP] Hydrology layout | main={mainRivers} tribs={tributaries} riverCount={cfg.riverCount} " +
                $"lakes={lakes} maxLakeCells={maxLakeCells} (~{lakeWorldM:F0}m lado eq.) grid={gridCells} " +
                $"riverAttemptsCap={cfg.maxTotalRiverBuildAttempts} perRiver={cfg.riverPlacementMaxAttemptsPerRiver}");
        }

        /// <summary>
        /// Corrige escala visual del Terrain y perfila relieve orgánico (llanuras + colinas anchas),
        /// evitando mesetas circulares del macro lobby agresivo.
        /// </summary>
        static void ApplyUwpTerrainPresentationFix(
            PMGUnifiedWorldPipelineConfig pipeline,
            MatchConfig match,
            MapGenConfig cfg)
        {
            if (cfg == null) return;

            float templateY = pipeline?.definitiveTemplate != null && pipeline.definitiveTemplate.terrainHeightWorld > 1f
                ? pipeline.definitiveTemplate.terrainHeightWorld
                : 50f;
            // Escala Y más baja: menos “bloque” vertical; el relieve se lee mejor en RTS.
            cfg.terrainHeightWorld = Mathf.Clamp(templateY * 0.76f, 32f, 40f);
            EnsureUwpSkirtSettings(pipeline, cfg);
            cfg.terrainMaterialTemplateOverride = null;

            cfg.paintTerrainByHeight = true;
            ApplyUwpTerrainSplatLayerCap(cfg);
            EnsureUwpSplatPercentDefaults(cfg);
            pipeline?.PullVisualBindingsFromTemplateIfEmpty();
            if (pipeline != null)
            {
                if (pipeline.grassLayer != null) cfg.grassLayer = pipeline.grassLayer;
                if (pipeline.dirtLayer != null) cfg.dirtLayer = pipeline.dirtLayer;
                if (pipeline.rockLayer != null) cfg.rockLayer = pipeline.rockLayer;
                if (pipeline.sandLayer != null) cfg.sandLayer = pipeline.sandLayer;
            }

            float rough = 0.4f;
            if (match != null)
            {
                rough = match.useHighLevelAlphaConfig
                    ? Mathf.Clamp01(match.terrainShape.hillDensity)
                    : 1f - Mathf.Clamp01(match.geography.terrainFlatness);
            }

            MapGenConfig template = pipeline?.definitiveTemplate;

            // Alineado con MapGenConfig principal: sin masas macro (evita muro en el borde).
            cfg.macroTerrainEnabled = false;
            cfg.macroMountainMassCount = 0;
            cfg.macroBasinCount = 0;

            if (template != null)
            {
                cfg.macroHillDensity = template.macroHillDensity;
                cfg.macroRoughnessWeight = template.macroRoughnessWeight;
                cfg.terrainMacroNoiseScale = template.terrainMacroNoiseScale;
                cfg.terrainMacroNoiseStrength = template.terrainMacroNoiseStrength;
            }
            else
            {
                cfg.macroHillDensity = Mathf.Lerp(0.32f, 0.40f, rough);
                cfg.macroRoughnessWeight = Mathf.Lerp(0.38f, 0.48f, rough);
            }

            // index.html: orilla ancha; transición suave sin mesetas planas.
            cfg.shoreSmoothRadiusCells = 18;
            cfg.shoreSmoothStrength = 0.48f;

            cfg.terrainNormalSmoothingPasses = 3;
            cfg.terrainNormalSmoothingStrength = 0.36f;

            ApplyUwpShallowerWaterBedCaps(cfg);
            ApplyUwpSoftRiverVisualCarves(cfg);
            ApplyUwpRiverVisualReliabilityFix(cfg);
            ApplyUwpRiverCenterlineQualityFix(cfg);

            float worldM = cfg.gridW * cfg.cellSizeWorld;
            Debug.Log(
                $"[UWP] Layout: {cfg.gridW}x{cfg.gridH} @ {cfg.cellSizeWorld:F1}m (~{worldM:F0}m), " +
                $"rivers={cfg.riverCount} lakes={cfg.lakeCount} maxLakeCells={cfg.maxLakeCells}, hm={cfg.heightmapResolution}");
        }

        /// <summary>
        /// Borde del mapa (TerrainSkirt) como en MapGenConfig principal: paredes + material soil_layers.
        /// </summary>
        static void EnsureUwpSkirtSettings(PMGUnifiedWorldPipelineConfig pipeline, MapGenConfig cfg)
        {
            if (cfg == null) return;

            MapGenConfig template = pipeline?.definitiveTemplate;
            cfg.showTerrainSkirt = true;
            if (template != null)
            {
                if (template.skirtDepth > 0f) cfg.skirtDepth = template.skirtDepth;
                if (template.skirtEdgeSamples > 0) cfg.skirtEdgeSamples = template.skirtEdgeSamples;
                if (template.skirtMaterial != null) cfg.skirtMaterial = template.skirtMaterial;
            }

            if (cfg.skirtMaterial == null)
            {
                cfg.skirtMaterial = Resources.Load<Material>(TerrainSkirtBuilder.SkirtSoilMaterialResourceName);
            }
        }

        /// <summary>
        /// Lechos menos profundos (solo MapGenConfig runtime UWP; no toca agua/meshes).
        /// </summary>
        static void ApplyUwpShallowerWaterBedCaps(MapGenConfig cfg)
        {
            if (cfg == null) return;
            cfg.lakeBedDepthBelowWater01 = Mathf.Min(cfg.lakeBedDepthBelowWater01, 0.022f);
            cfg.lakeBedMinDepthBelowWater01 = Mathf.Min(cfg.lakeBedMinDepthBelowWater01, 0.008f);
            cfg.lakeBedDepthRampCells = Mathf.Max(cfg.lakeBedDepthRampCells, 10);
            cfg.riverBedDepthBelowWater01 = Mathf.Min(cfg.riverBedDepthBelowWater01, 0.016f);
            cfg.tributaryBedDepthBelowWater01 = Mathf.Min(cfg.tributaryBedDepthBelowWater01, 0.020f);
        }

        /// <summary>
        /// Tallado mínimo de cauce en heightmap (sin end/outlet reach que generaban artefactos circulares).
        /// </summary>
        static void ApplyUwpSoftRiverVisualCarves(MapGenConfig cfg)
        {
            if (cfg == null) return;
            cfg.riverVisualTerrainCarveEnabled = true;
            cfg.riverTerrainCarveDepthWorld = 0.08f;
            cfg.riverTerrainCarveFalloffCells = Mathf.Clamp(Mathf.Max(cfg.riverTerrainCarveFalloffCells, 10), 8, 14);
            cfg.riverVisualTerrainCarveExtraCells = Mathf.Max(cfg.riverVisualTerrainCarveExtraCells, 2);
            cfg.riverVisualTerrainBankFalloffCells = Mathf.Max(cfg.riverVisualTerrainBankFalloffCells, 5);
            cfg.riverVisualTerrainBankSoftness = Mathf.Max(cfg.riverVisualTerrainBankSoftness, 0.72f);
            cfg.riverVisualTerrainCenterDepthMul = Mathf.Max(cfg.riverVisualTerrainCenterDepthMul, 1.18f);
            cfg.riverEndReachTerrainFixEnabled = false;
            cfg.riverOutletTerrainFixEnabled = false;
        }

        /// <summary>Umbrales más permisivos para meshes de tributarios (solo runtime UWP).</summary>
        static void ApplyUwpRiverVisualReliabilityFix(MapGenConfig cfg)
        {
            if (cfg == null) return;
            cfg.riverVisualMinSurfacePieceLengthCells = Mathf.Min(cfg.riverVisualMinSurfacePieceLengthCells, 6);
            cfg.riverVisualMinSurfacePieceAreaCells = Mathf.Min(cfg.riverVisualMinSurfacePieceAreaCells, 4);
            cfg.riverVisualMinDetachedPatchCells = Mathf.Min(cfg.riverVisualMinDetachedPatchCells, 3);
            cfg.riverVisualMainRiverCorridorCells = Mathf.Max(cfg.riverVisualMainRiverCorridorCells, 5);
            cfg.riverVisualMainCorridorKeepDistanceCells = Mathf.Max(cfg.riverVisualMainCorridorKeepDistanceCells, 5);
            cfg.riverConfluenceMergeRadiusCells = Mathf.Max(cfg.riverConfluenceMergeRadiusCells, 6);
            cfg.riverSurfaceMeshExtraYOffsetWorld = Mathf.Max(cfg.riverSurfaceMeshExtraYOffsetWorld, 0.14f);
            cfg.riverRibbonAntiZFightYOffsetWorld = Mathf.Max(cfg.riverRibbonAntiZFightYOffsetWorld, 0.042f);
            cfg.lakeRiverMouthBlendCells = Mathf.Max(cfg.lakeRiverMouthBlendCells, 7);
            cfg.riverConfluenceVisualBlendLengthCells = Mathf.Max(cfg.riverConfluenceVisualBlendLengthCells, 11);
            cfg.riverLakeEmissaryLakeFadeCells = Mathf.Max(cfg.riverLakeEmissaryLakeFadeCells, 16f);
            cfg.riverLakeEmissaryRiverFadeCells = Mathf.Max(cfg.riverLakeEmissaryRiverFadeCells, 10f);
        }

        /// <summary>Spline más estable: menos fallback a grid, curvas más suaves, sin micro-zig-zags.</summary>
        static void ApplyUwpRiverCenterlineQualityFix(MapGenConfig cfg)
        {
            if (cfg == null) return;

            cfg.riverSurfaceUseSplineVisualCenterline = true;
            cfg.riverSurfaceChaikinPasses = 1;
            cfg.riverSurfaceSharpBendAngleDeg = 95f;
            cfg.riverSurfaceSplineMaxAngleStepDeg = 20f;
            cfg.riverSurfaceSplineMaxDeviationCells = 1.55f;
            cfg.riverSurfaceSplineTension = 0.38f;
            cfg.riverSurfaceSplineSampleSpacingCells = 0.48f;
            cfg.riverSurfaceVisualSpacingCells = 0.82f;
            cfg.riverSurfaceSampleSpacingCells = 0.85f;
            cfg.riverSurfaceMaxVisualPointRatio = 1.28f;
        }

        /// <summary>
        /// Prioriza ≥1 tributario usable: pase fill relajado + confluencias más permisivas en mapas UWP.
        /// </summary>
        static void ApplyUwpTributaryPlacementReliability(MapGenConfig cfg)
        {
            if (cfg == null) return;

            cfg.riverRelaxedMissingTributaryFillPass = true;
            cfg.riverConfluenceEnabled = true;
            cfg.riverTributaryRecoveryEnabled = true;
            cfg.riverTributaryRecoveryRelaxGeometry = true;
            cfg.riverConfluenceAcceptLooseAngle = true;
            cfg.riverConfluenceMinDistanceFromMainEndpointsCells =
                Mathf.Min(cfg.riverConfluenceMinDistanceFromMainEndpointsCells, 14);
            cfg.riverConfluenceMinSpacingCells = Mathf.Min(cfg.riverConfluenceMinSpacingCells, 18);
            cfg.riverTributaryRecoveryAttempts = Mathf.Max(cfg.riverTributaryRecoveryAttempts, 24);
            cfg.riverTributaryRouteMaxAttempts = Mathf.Max(cfg.riverTributaryRouteMaxAttempts, 12);
            cfg.riverTributaryRouteBudgetMs = Mathf.Max(cfg.riverTributaryRouteBudgetMs, 320);
            cfg.riverTributaryProceduralCandidatesPerSlot = Mathf.Max(cfg.riverTributaryProceduralCandidatesPerSlot, 32);
            cfg.riverTributaryProceduralMaxSourceDistCells =
                Mathf.Max(cfg.riverTributaryProceduralMaxSourceDistCells, 96);
            cfg.riverTributaryProceduralMinCells = Mathf.Min(cfg.riverTributaryProceduralMinCells, 10);
            cfg.riverTributaryRecoveryMinLengthCells = Mathf.Min(cfg.riverTributaryRecoveryMinLengthCells, 18);
            cfg.maxTotalRiverBuildAttempts = Mathf.Max(cfg.maxTotalRiverBuildAttempts, 600);
            cfg.riverEarlyRejectConsecutiveToBreakStrictPass =
                Mathf.Max(cfg.riverEarlyRejectConsecutiveToBreakStrictPass, 20);
        }

        /// <summary>
        /// Río principal claramente más ancho y con carve más profundo que tributarios (~1.55 celdas).
        /// Debe ejecutarse al final del tuning UWP para no ser pisado por otros pasos.
        /// </summary>
        static void ApplyUwpMainRiverWidthProfile(MapGenConfig cfg, PMGUnifiedWorldPipelineConfig pipeline)
        {
            if (cfg == null) return;

            float tributaryFullWidthCells = pipeline != null && pipeline.uwpTributaryRiverFullWidthCells > 0.01f
                ? pipeline.uwpTributaryRiverFullWidthCells
                : 1.55f;
            float mainFullWidthCells = pipeline != null && pipeline.uwpMainRiverFullWidthCells > 0.01f
                ? pipeline.uwpMainRiverFullWidthCells
                : 3.75f;
            float carveDepthWorld = pipeline != null && pipeline.uwpMainRiverCarveDepthWorld > 0.001f
                ? pipeline.uwpMainRiverCarveDepthWorld
                : 0.11f;

            cfg.riverVisualRibbonFullWidthCellsTributary = tributaryFullWidthCells;
            cfg.riverVisualRibbonFullWidthCellsMain = mainFullWidthCells;

            cfg.riverWidthRadiusCells = 3;
            cfg.riverVisualHalfWidthCells = Mathf.Max(cfg.riverVisualHalfWidthCells, mainFullWidthCells * 0.22f);
            cfg.riverVisualBankInset = 0.06f;

            cfg.riverVisualTerrainCarveEnabled = true;
            cfg.riverTerrainCarveDepthWorld = carveDepthWorld;
            cfg.riverTerrainCarveFalloffCells = 12;
            cfg.riverVisualTerrainCarveExtraCells = 3;
            cfg.riverVisualTerrainBankFalloffCells = 6;
            cfg.riverVisualTerrainBankSoftness = 0.78f;
            cfg.riverVisualTerrainCenterDepthMul = 1.24f;
            cfg.riverTributaryTerrainCarveRadiusMul = 1.05f;

            cfg.riverSurfaceMeshExtraYOffsetWorld = Mathf.Max(cfg.riverSurfaceMeshExtraYOffsetWorld, 0.15f);
            cfg.riverRibbonAntiZFightYOffsetWorld = Mathf.Max(cfg.riverRibbonAntiZFightYOffsetWorld, 0.045f);

            if (pipeline != null && pipeline.uwpMainRiverSurfaceWorldY > 0.01f)
            {
                float terrainY = cfg.terrainHeightWorld > 0f ? cfg.terrainHeightWorld : 38f;
                float baseWaterY = cfg.waterHeight01 * terrainY + Mathf.Max(cfg.waterSurfaceOffset, 0.02f);
                float widthT = Mathf.InverseLerp(2f, 12f, mainFullWidthCells);
                float targetY = Mathf.Lerp(pipeline.uwpMainRiverSurfaceWorldY * 0.12f, pipeline.uwpMainRiverSurfaceWorldY, widthT);
                float requiredLift = Mathf.Max(0f, targetY - baseWaterY + carveDepthWorld * widthT * 0.6f);
                cfg.riverRibbonVerticalLiftWorld = Mathf.Max(cfg.riverRibbonVerticalLiftWorld, requiredLift);
                cfg.riverSurfaceMeshExtraYOffsetWorld = Mathf.Max(cfg.riverSurfaceMeshExtraYOffsetWorld, requiredLift * 0.22f);
            }

            Debug.LogWarning(
                $"[UWP] Main river profile APPLY | main={cfg.riverVisualRibbonFullWidthCellsMain:F2}c " +
                $"(~{cfg.riverVisualRibbonFullWidthCellsMain * cfg.cellSizeWorld:F1}m) trib={cfg.riverVisualRibbonFullWidthCellsTributary:F2}c " +
                $"carve={cfg.riverTerrainCarveDepthWorld:F3}m falloff={cfg.riverTerrainCarveFalloffCells} seed={cfg.seed}");

            ApplyUwpRiverCenterlineQualityFix(cfg);
        }

        /// <summary>Distancia en celdas al cauce (Water/River + centerlines hidrológicas).</summary>
        static int[,] BuildUwpHydrologyCorridorDistance(GridSystem grid, int maxRadius)
        {
            int w = grid.Width;
            int h = grid.Height;
            var dist = new int[w, h];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    dist[x, z] = -1;

            var qx = new Queue<int>();
            var qz = new Queue<int>();

            void Seed(int x, int z)
            {
                if ((uint)x >= (uint)w || (uint)z >= (uint)h) return;
                if (dist[x, z] == 0) return;
                dist[x, z] = 0;
                qx.Enqueue(x);
                qz.Enqueue(z);
            }

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    CellType t = grid.GetCell(x, z).type;
                    if (t == CellType.Water || t == CellType.River)
                        Seed(x, z);
                }
            }

            if (grid.RiverCenterlinesCellSpace != null)
            {
                for (int ri = 0; ri < grid.RiverCenterlinesCellSpace.Count; ri++)
                {
                    var line = grid.RiverCenterlinesCellSpace[ri];
                    if (line == null) continue;
                    for (int i = 0; i < line.Count; i++)
                    {
                        int cx = Mathf.Clamp(Mathf.RoundToInt(line[i].x), 0, w - 1);
                        int cz = Mathf.Clamp(Mathf.RoundToInt(line[i].y), 0, h - 1);
                        Seed(cx, cz);
                    }
                }
            }

            bool[,] visMask = grid.RiverVisualSurfaceMask;
            if (grid.RiverVisualSurfacesBuilt && visMask != null &&
                visMask.GetLength(0) == w && visMask.GetLength(1) == h)
            {
                for (int x = 0; x < w; x++)
                {
                    for (int z = 0; z < h; z++)
                    {
                        if (visMask[x, z])
                            Seed(x, z);
                    }
                }
            }

            while (qx.Count > 0)
            {
                int x = qx.Dequeue();
                int z = qz.Dequeue();
                int d = dist[x, z];
                if (d >= maxRadius) continue;

                void Try(int nx, int nz)
                {
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)h) return;
                    if (dist[nx, nz] != -1) return;
                    dist[nx, nz] = d + 1;
                    qx.Enqueue(nx);
                    qz.Enqueue(nz);
                }

                Try(x - 1, z);
                Try(x + 1, z);
                Try(x, z - 1);
                Try(x, z + 1);
            }

            return dist;
        }

        /// <summary>
        /// Preset visual: Water_RiverSurface_Main.localPosition.y (p. ej. 1.95 con ancho 12c).
        /// </summary>
        static void ApplyUwpMainRiverSurfaceHeightPreset(GameObject waterRoot, PMGUnifiedWorldPipelineConfig pipeline)
        {
            if (waterRoot == null || pipeline == null)
                return;

            Transform main = waterRoot.transform.Find("Water_RiverSurface_Main");
            if (main == null)
                return;

            float presetY = pipeline.uwpMainRiverSurfaceWorldY > 0.01f
                ? Mathf.Lerp(pipeline.uwpMainRiverSurfaceWorldY * 0.12f, pipeline.uwpMainRiverSurfaceWorldY,
                    Mathf.InverseLerp(2f, 12f, pipeline.uwpMainRiverFullWidthCells))
                : 0f;

            Undo.RecordObject(main, "UWP Main River Surface Y");
            Vector3 lp = main.localPosition;
            lp.y = presetY;
            main.localPosition = lp;

            if (presetY > 0.01f)
            {
                Debug.LogWarning(
                    $"[UWP] Main river surface Y preset | localY={presetY:F3} width={pipeline.uwpMainRiverFullWidthCells:F1}c " +
                    $"(target max={pipeline.uwpMainRiverSurfaceWorldY:F3})");
            }
        }

        static void EnsureUwpWaterRenderersVisible(GameObject waterRoot)
        {
            if (waterRoot == null) return;
            MeshRenderer[] renderers = waterRoot.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer mr = renderers[i];
                if (mr == null) continue;
                mr.allowOcclusionWhenDynamic = false;
            }
        }

        /// <summary>
        /// Post-paso sobre grid lógico antes del export (estilo index: edge falloff + terrazas costeras anchas).
        /// Solo height01 de tierra; no toca meshes de agua.
        /// </summary>
        static void ApplyUwpGridHeightPresentationFix(GridSystem grid, MapGenConfig cfg)
        {
            if (grid == null || cfg == null) return;
            ApplyUwpLandHeightCompressAndRelief(grid, cfg);
            ApplyUwpGentleLandSmooth(grid, cfg);
            ApplyUwpMapEdgeHeightFalloff(grid, cfg);
            ApplyUwpGradualWaterShoreBlend(grid, cfg);
            HeightGenerator.RecalculateLandSlopes(grid, cfg);
        }

        /// <summary>
        /// Baja el nivel general y añade ondulación (index: normalize + fractal + ridged suave).
        /// </summary>
        static void ApplyUwpLandHeightCompressAndRelief(GridSystem grid, MapGenConfig cfg)
        {
            int w = grid.Width;
            int h = grid.Height;
            if (w < 3 || h < 3) return;

            float waterH = cfg.waterHeight01;
            const float compress = 0.72f;
            const float maxAboveWater = 0.34f;
            int perlinExcludeRadius = grid.RiverVisualSurfacesBuilt && grid.RiverVisualSurfaceMask != null ? 22 : 14;
            float seed = cfg.seed * 0.017f + 41.3f;
            int[,] nearWaterDist = BuildUwpHydrologyCorridorDistance(grid, perlinExcludeRadius);

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    ref CellData cell = ref grid.GetCell(x, z);
                    if (cell.type == CellType.Water || cell.type == CellType.River)
                        continue;

                    float above = cell.height01 - waterH;
                    if (above > 0f)
                        cell.height01 = waterH + above * compress;

                    int dWater = nearWaterDist[x, z];
                    if (dWater >= 0 && dWater <= perlinExcludeRadius)
                    {
                        cell.height01 = Mathf.Clamp(cell.height01, waterH + 0.01f, waterH + maxAboveWater);
                        continue;
                    }

                    float nx = x / (float)(w - 1);
                    float nz = z / (float)(h - 1);
                    float macro = Mathf.PerlinNoise(nx * 4.2f + seed, nz * 4.2f - seed * 0.7f);
                    float detail = Mathf.PerlinNoise(nx * 9.5f + 19.7f, nz * 9.5f + 13.2f);
                    float ridge = 1f - Mathf.Abs(macro * 2f - 1f);
                    ridge *= ridge;

                    float variation =
                        (macro - 0.5f) * 2f * 0.062f +
                        (detail - 0.5f) * 2f * 0.032f +
                        ridge * 0.048f;
                    cell.height01 += variation;
                    cell.height01 = Mathf.Clamp(cell.height01, waterH + 0.01f, waterH + maxAboveWater);
                }
            }
        }

        static void ApplyUwpGentleLandSmooth(GridSystem grid, MapGenConfig cfg)
        {
            int w = grid.Width;
            int h = grid.Height;
            if (w < 3 || h < 3) return;

            const int smoothExcludeRadius = 8;
            int[,] nearWaterDist = BuildUwpHydrologyCorridorDistance(grid, smoothExcludeRadius);

            var work = new float[w, h];
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                    work[x, z] = grid.GetCell(x, z).height01;
            }

            for (int x = 1; x < w - 1; x++)
            {
                for (int z = 1; z < h - 1; z++)
                {
                    ref CellData cell = ref grid.GetCell(x, z);
                    if (cell.type != CellType.Land) continue;

                    int dWater = nearWaterDist[x, z];
                    if (dWater >= 0 && dWater <= smoothExcludeRadius)
                        continue;

                    float avg = (work[x - 1, z] + work[x + 1, z] + work[x, z - 1] + work[x, z + 1]) * 0.25f;
                    cell.height01 = Mathf.Lerp(work[x, z], avg, 0.20f);
                }
            }
        }

        /// <summary>
        /// Baja suavemente la meseta perimetral (index: edgeFalloff radial desde el centro del mapa).
        /// </summary>
        static void ApplyUwpMapEdgeHeightFalloff(GridSystem grid, MapGenConfig cfg)
        {
            int w = grid.Width;
            int h = grid.Height;
            if (w < 3 || h < 3) return;

            float waterH = cfg.waterHeight01;
            float targetEdgeH = waterH + 0.026f;
            int margin = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(w, h) * 0.08f), 14, 32);
            float maxRadial = Mathf.Sqrt(0.5f * 0.5f + 0.5f * 0.5f);

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    ref CellData cell = ref grid.GetCell(x, z);
                    if (cell.type == CellType.Water || cell.type == CellType.River)
                        continue;

                    float nx = x / (float)(w - 1);
                    float nz = z / (float)(h - 1);
                    float dx = (nx - 0.5f) * 2f;
                    float dz = (nz - 0.5f) * 2f;
                    float radial = Mathf.Sqrt(dx * dx + dz * dz) / maxRadial;
                    float edgeFalloff = Mathf.Clamp01((radial - 0.18f) / 0.92f);
                    float radialPull = edgeFalloff * edgeFalloff * edgeFalloff;

                    int borderDist = Mathf.Min(Mathf.Min(x, z), Mathf.Min(w - 1 - x, h - 1 - z));
                    float borderU = borderDist >= margin ? 0f : 1f - borderDist / (float)margin;
                    borderU = borderU * borderU * (3f - 2f * borderU);

                    float pull = Mathf.Clamp01(Mathf.Max(radialPull * 0.62f, borderU * 0.88f));
                    if (pull < 1e-4f) continue;

                    cell.height01 = Mathf.Lerp(cell.height01, targetEdgeH, pull);
                }
            }
        }

        /// <summary>
        /// Rampas graduales tierra→agua (index: flattenCoastalTerraces ~5.5 celdas; aquí ~14).
        /// </summary>
        static void ApplyUwpGradualWaterShoreBlend(GridSystem grid, MapGenConfig cfg)
        {
            int w = grid.Width;
            int h = grid.Height;
            if (w < 3 || h < 3) return;

            float waterH = cfg.waterHeight01;
            float shoreTarget = waterH + 0.026f;
            int radius = 18;

            var dist = new int[w, h];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    dist[x, z] = -1;

            var qx = new Queue<int>();
            var qz = new Queue<int>();

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    CellType t = grid.GetCell(x, z).type;
                    if (t != CellType.Water && t != CellType.River) continue;
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
                if (d >= radius) continue;

                void Try(int nx, int nz)
                {
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)h) return;
                    if (dist[nx, nz] != -1) return;
                    dist[nx, nz] = d + 1;
                    qx.Enqueue(nx);
                    qz.Enqueue(nz);
                }

                Try(x - 1, z);
                Try(x + 1, z);
                Try(x, z - 1);
                Try(x, z + 1);
            }

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    ref CellData cell = ref grid.GetCell(x, z);
                    if (cell.type == CellType.Water || cell.type == CellType.River) continue;

                    int d = dist[x, z];
                    if (d <= 0 || d > radius) continue;

                    float k = 1f - d / (float)(radius + 1);
                    k = k * k * (3f - 2f * k);
                    k *= 0.52f;

                    if (cell.height01 > shoreTarget + 0.004f)
                        cell.height01 = Mathf.Lerp(cell.height01, shoreTarget, k);
                }
            }
        }

        /// <summary>
        /// Orillas de río en heightmap (TerrainExporter). No modifica WaterMeshBuilder ni hidrología.
        /// </summary>
        static void ApplyUwpRiverBankTerrainFix(PMGUnifiedWorldPipelineConfig pipeline, MapGenConfig cfg)
        {
            if (pipeline == null || cfg == null || !pipeline.applyRiverBankTerrainFix)
                return;

            ApplyUwpSoftRiverVisualCarves(cfg);

            cfg.shoreSmoothRadiusCells = 18;
            cfg.shoreSmoothStrength = 0.48f;
            cfg.sandShoreCells = Mathf.Clamp(cfg.sandShoreCells, 3, 4);

            cfg.unifiedWaterTerrainBankLipWorld = Mathf.Max(cfg.unifiedWaterTerrainBankLipWorld, 0.028f);
            cfg.unifiedWaterTerrainBandCells = Mathf.Max(cfg.unifiedWaterTerrainBandCells, 1.85f);
            cfg.unifiedWaterShoreTerrainOffsetWorld = Mathf.Max(cfg.unifiedWaterShoreTerrainOffsetWorld, 0.022f);
            cfg.unifiedWaterTerrainEdgeSubmergeWorld = Mathf.Max(cfg.unifiedWaterTerrainEdgeSubmergeWorld, 0.052f);

            ApplyUwpShallowerWaterBedCaps(cfg);
        }

        static void ApplyUwpSinglePassFrozenSurfacePipeline(
            Terrain terrain,
            GridSystem grid,
            MapGenConfig cfg,
            PMGUnifiedWorldPipelineConfig pipeline,
            MapGenerator generator,
            IUwpSceneVisualBindings sceneBindings,
            List<CityNode> cities,
            List<Road> roads)
        {
            if (terrain == null || grid == null || cfg == null) return;

            UwpRuntimeProfileCompiler.ApplyFullProfile(pipeline, cfg);
            WaterVisualPipelinePolicy.ApplyToRuntimeConfig(cfg);
            PrepareUwpTerrainExportLayers(terrain, grid, cfg, pipeline, generator, sceneBindings);
            ApplyUwpGridHeightPresentationFix(grid, cfg);
            ClearSceneWaterRoot();

            var spawnCells = new List<Vector2Int>();
            if (cities != null && cities.Count > 0)
            {
                int n = Mathf.Min(cities.Count, Mathf.Max(1, cfg.cityCount));
                for (int i = 0; i < n; i++)
                    spawnCells.Add(cities[i].Center);
            }

            Material waterMat = cfg.riverWaterMaterial;
            if (pipeline != null && pipeline.riverWaterMaterial != null)
                waterMat = pipeline.riverWaterMaterial;

            var layers = BuildUwpTerrainExportLayers(pipeline, generator, sceneBindings, cfg);
            GameObject waterRoot = UwpFrozenSurfacePipeline.Apply(
                grid, cfg, terrain, waterMat, spawnCells, cities, roads, layers);

            LogUwpSceneWaterSummary(grid, cfg, pipeline, waterRoot);
            EnsureUwpWaterRenderersVisible(waterRoot);
            if (!WaterVisualPipelinePolicy.UsesUwpUnifiedWaterSurfaceLevel(cfg))
                ApplyUwpMainRiverSurfaceHeightPreset(waterRoot, pipeline);
        }

        static UwpFrozenSurfacePipeline.TerrainExportLayers BuildUwpTerrainExportLayers(
            PMGUnifiedWorldPipelineConfig pipeline,
            MapGenerator generator,
            IUwpSceneVisualBindings sceneBindings,
            MapGenConfig cfg)
        {
            TerrainLayer grass = generator?.terrainGrassLayerOverride ?? pipeline?.grassLayer ?? cfg.grassLayer;
            TerrainLayer dirt = generator?.terrainDirtLayerOverride ?? pipeline?.dirtLayer ?? cfg.dirtLayer;
            TerrainLayer rock = generator?.terrainRockLayerOverride ?? pipeline?.rockLayer ?? cfg.rockLayer;
            TerrainLayer sand = generator?.terrainSandLayerOverride ?? pipeline?.sandLayer ?? cfg.sandLayer;
            if (grass == null && sceneBindings != null) grass = sceneBindings.GrassLayer;
            if (dirt == null && sceneBindings != null) dirt = sceneBindings.DirtLayer;
            if (rock == null && sceneBindings != null) rock = sceneBindings.RockLayer;
            if (sand == null && sceneBindings != null) sand = sceneBindings.SandLayer;

            return new UwpFrozenSurfacePipeline.TerrainExportLayers
            {
                grass = grass,
                dirt = dirt,
                rock = rock,
                sand = sand,
                grassTile = ResolveTileSize(generator?.terrainGrassTileSize, pipeline?.grassTileSize, null),
                dirtTile = ResolveTileSize(generator?.terrainDirtTileSize, pipeline?.dirtTileSize, null),
                rockTile = ResolveTileSize(generator?.terrainRockTileSize, pipeline?.rockTileSize, null),
                sandTile = ResolveTileSize(generator?.terrainSandTileSize, pipeline?.sandTileSize, null),
                sandShoreCells = generator != null && generator.terrainSandShoreCells > 0
                    ? generator.terrainSandShoreCells
                    : (pipeline != null && pipeline.sandShoreCells > 0
                        ? pipeline.sandShoreCells
                        : (sceneBindings != null ? sceneBindings.SandShoreCells : 3))
            };
        }

        static void LogUwpSceneWaterSummary(
            GridSystem grid,
            MapGenConfig cfg,
            PMGUnifiedWorldPipelineConfig pipeline,
            GameObject waterRoot)
        {
            int centerlines = grid.RiverCenterlinesCellSpace != null ? grid.RiverCenterlinesCellSpace.Count : 0;
            int tribs = Mathf.Max(0, centerlines - 1);
            int tribWanted = pipeline != null ? pipeline.uwpTributaryCount : tribs;
            int meshChildren = waterRoot != null ? waterRoot.transform.childCount : 0;
            Debug.LogWarning(
                $"[UWP] Agua reconstruida | mainWidth={cfg.riverVisualRibbonFullWidthCellsMain:F2}c " +
                $"tribWidth={cfg.riverVisualRibbonFullWidthCellsTributary:F2}c uwpOwned={cfg.uwpOwnedVisualPolicy} " +
                $"centerlines={centerlines} tribs={tribs}/{tribWanted} " +
                $"mainOuter~{RiverSurfaceMeshBuilder.LastMainRiverAvgHalfWidthWorld * 2f:F1}m " +
                $"tribOuter~{RiverSurfaceMeshBuilder.GetTributaryAvgHalfWidthWorldMean() * 2f:F1}m " +
                $"waterChildren={meshChildren} fragmentCull={RiverSurfaceMeshBuilder.RiverSurfaceFragmentCullCount}");
            if (tribs < tribWanted)
            {
                Debug.LogWarning(
                    $"[UWP] Faltan tributarios ({tribs}/{tribWanted}). Revisa consola 'Fase4 Agua' o prueba otra seed. " +
                    $"riverCount cfg={cfg.riverCount} seed={cfg.seed}");
            }
        }

        static void ExportUwpTerrainOnlyOnce(
            Terrain terrain,
            GridSystem grid,
            MapGenConfig cfg,
            PMGUnifiedWorldPipelineConfig pipeline,
            MapGenerator generator,
            IUwpSceneVisualBindings sceneBindings,
            bool logSplatSummary)
        {
            if (terrain == null || grid == null || cfg == null) return;

            UwpRuntimeProfileCompiler.ApplyFullProfile(pipeline, cfg);
            if (!grid.RiverVisualSurfaceCacheFrozen)
                RiverSurfaceMeshBuilder.FreezeUwpFinalWaterVisualSurfaceCache(grid, cfg);
            ApplyUwpGridHeightPresentationFix(grid, cfg);

            TerrainLayer grass = generator?.terrainGrassLayerOverride ?? pipeline?.grassLayer ?? cfg.grassLayer;
            TerrainLayer dirt = generator?.terrainDirtLayerOverride ?? pipeline?.dirtLayer ?? cfg.dirtLayer;
            TerrainLayer rock = generator?.terrainRockLayerOverride ?? pipeline?.rockLayer ?? cfg.rockLayer;
            TerrainLayer sand = generator?.terrainSandLayerOverride ?? pipeline?.sandLayer ?? cfg.sandLayer;
            if (grass == null && sceneBindings != null) grass = sceneBindings.GrassLayer;
            if (dirt == null && sceneBindings != null) dirt = sceneBindings.DirtLayer;
            if (rock == null && sceneBindings != null) rock = sceneBindings.RockLayer;
            if (sand == null && sceneBindings != null) sand = sceneBindings.SandLayer;

            Vector2 grassTile = ResolveTileSize(generator?.terrainGrassTileSize, pipeline?.grassTileSize, null);
            Vector2 dirtTile = ResolveTileSize(generator?.terrainDirtTileSize, pipeline?.dirtTileSize, null);
            Vector2 rockTile = ResolveTileSize(generator?.terrainRockTileSize, pipeline?.rockTileSize, null);
            Vector2 sandTile = ResolveTileSize(generator?.terrainSandTileSize, pipeline?.sandTileSize, null);
            int shoreCells = generator != null && generator.terrainSandShoreCells > 0
                ? generator.terrainSandShoreCells
                : (pipeline != null && pipeline.sandShoreCells > 0 ? pipeline.sandShoreCells : (sceneBindings != null ? sceneBindings.SandShoreCells : 3));

            ExportUwpTerrainHeightAndSplat(
                terrain, grid, cfg, grass, dirt, rock, sand,
                grassTile, dirtTile, rockTile, sandTile, shoreCells,
                logSplatSummary);
        }

        static void PrepareUwpTerrainExportLayers(
            Terrain terrain,
            GridSystem grid,
            MapGenConfig cfg,
            PMGUnifiedWorldPipelineConfig pipeline,
            MapGenerator generator,
            IUwpSceneVisualBindings sceneBindings)
        {
            if (terrain == null || grid == null || cfg == null) return;

            TerrainLayer grass = generator?.terrainGrassLayerOverride ?? pipeline?.grassLayer ?? cfg.grassLayer;
            TerrainLayer dirt = generator?.terrainDirtLayerOverride ?? pipeline?.dirtLayer ?? cfg.dirtLayer;
            TerrainLayer rock = generator?.terrainRockLayerOverride ?? pipeline?.rockLayer ?? cfg.rockLayer;
            TerrainLayer sand = generator?.terrainSandLayerOverride ?? pipeline?.sandLayer ?? cfg.sandLayer;

            if (grass == null && sceneBindings != null) grass = sceneBindings.GrassLayer;
            if (dirt == null && sceneBindings != null) dirt = sceneBindings.DirtLayer;
            if (rock == null && sceneBindings != null) rock = sceneBindings.RockLayer;
            if (sand == null && sceneBindings != null) sand = sceneBindings.SandLayer;

            if (grass == null && dirt == null && rock == null)
                ResolveTerrainLayersFromProject(ref grass, ref dirt, ref rock, ref sand);
            else
            {
                grass = PMGUnifiedWorldPipelineConfig.EnsureLayerHasDiffuse(grass, UwpAssetPaths.DefaultGrassLayer);
                dirt = PMGUnifiedWorldPipelineConfig.EnsureLayerHasDiffuse(dirt, UwpAssetPaths.DefaultDirtLayer);
                rock = PMGUnifiedWorldPipelineConfig.EnsureLayerHasDiffuse(rock, UwpAssetPaths.DefaultRockLayer);
                sand = PMGUnifiedWorldPipelineConfig.EnsureLayerHasDiffuse(sand, UwpAssetPaths.DefaultSandLayer);
            }

            cfg.paintTerrainByHeight = true;
            cfg.terrainMaterialTemplateOverride = null;
            ApplyUwpTerrainSplatLayerCap(cfg);
            EnsureTerrainDataAlphamapReady(terrain.terrainData, cfg);
            PreAssignTerrainLayersForSplat(terrain.terrainData, grass, dirt, rock);
        }

        static void FinalizeTerrainVisuals(
            Terrain terrain,
            GridSystem grid,
            MapGenConfig cfg,
            PMGUnifiedWorldPipelineConfig pipeline,
            MapGenerator generator,
            IUwpSceneVisualBindings sceneBindings)
        {
            if (terrain == null || grid == null || cfg == null) return;

            UwpRuntimeProfileCompiler.ApplyFullProfile(pipeline, cfg);
            PrepareUwpTerrainExportLayers(terrain, grid, cfg, pipeline, generator, sceneBindings);
            RiverSurfaceMeshBuilder.EnsureRiverVisualSurfaceCache(grid, cfg);
            ApplyUwpGridHeightPresentationFix(grid, cfg);

            TerrainLayer grass = generator?.terrainGrassLayerOverride ?? pipeline?.grassLayer ?? cfg.grassLayer;
            TerrainLayer dirt = generator?.terrainDirtLayerOverride ?? pipeline?.dirtLayer ?? cfg.dirtLayer;
            TerrainLayer rock = generator?.terrainRockLayerOverride ?? pipeline?.rockLayer ?? cfg.rockLayer;
            TerrainLayer sand = generator?.terrainSandLayerOverride ?? pipeline?.sandLayer ?? cfg.sandLayer;

            if (grass == null && sceneBindings != null) grass = sceneBindings.GrassLayer;
            if (dirt == null && sceneBindings != null) dirt = sceneBindings.DirtLayer;
            if (rock == null && sceneBindings != null) rock = sceneBindings.RockLayer;
            if (sand == null && sceneBindings != null) sand = sceneBindings.SandLayer;

            Vector2 grassTile = ResolveTileSize(generator?.terrainGrassTileSize, pipeline?.grassTileSize, null);
            Vector2 dirtTile = ResolveTileSize(generator?.terrainDirtTileSize, pipeline?.dirtTileSize, null);
            Vector2 rockTile = ResolveTileSize(generator?.terrainRockTileSize, pipeline?.rockTileSize, null);
            Vector2 sandTile = ResolveTileSize(generator?.terrainSandTileSize, pipeline?.sandTileSize, null);
            int shoreCells = generator != null && generator.terrainSandShoreCells > 0
                ? generator.terrainSandShoreCells
                : (pipeline != null && pipeline.sandShoreCells > 0 ? pipeline.sandShoreCells : (sceneBindings != null ? sceneBindings.SandShoreCells : 3));

            ExportUwpTerrainHeightAndSplat(
                terrain, grid, cfg, grass, dirt, rock, sand,
                grassTile, dirtTile, rockTile, sandTile, shoreCells,
                logSplatSummary: true);
        }

        /// <summary>
        /// Tras construir meshes de agua, re-exporta heightmap usando la máscara visual final del río.
        /// </summary>
        static void ApplyUwpPostWaterTerrainCarveResync(
            Terrain terrain,
            GridSystem grid,
            MapGenConfig cfg,
            PMGUnifiedWorldPipelineConfig pipeline,
            MapGenerator generator,
            IUwpSceneVisualBindings sceneBindings)
        {
            if (terrain == null || grid == null || cfg == null) return;

            UwpRuntimeProfileCompiler.ApplyFullProfile(pipeline, cfg);
            cfg.riverSurfaceTributaryWidthFixEnabled = true;
            if (cfg.uwpOwnedVisualPolicy)
                cfg.riverVisualTerrainCarveExtraCells = Mathf.Clamp(cfg.riverVisualTerrainCarveExtraCells, 1, 2);
            else
                cfg.riverVisualTerrainCarveExtraCells = Mathf.Max(cfg.riverVisualTerrainCarveExtraCells, 4);

            if (!grid.RiverVisualSurfacesBuilt || grid.RiverVisualSurfaceMask == null)
                RiverSurfaceMeshBuilder.EnsureRiverVisualSurfaceCache(grid, cfg);

            int maskCells = 0;
            bool[,] m = grid.RiverVisualSurfaceMask;
            if (m != null)
            {
                for (int x = 0; x < grid.Width; x++)
                    for (int z = 0; z < grid.Height; z++)
                        if (m[x, z]) maskCells++;
            }

            TerrainLayer grass = generator?.terrainGrassLayerOverride ?? pipeline?.grassLayer ?? cfg.grassLayer;
            TerrainLayer dirt = generator?.terrainDirtLayerOverride ?? pipeline?.dirtLayer ?? cfg.dirtLayer;
            TerrainLayer rock = generator?.terrainRockLayerOverride ?? pipeline?.rockLayer ?? cfg.rockLayer;
            TerrainLayer sand = generator?.terrainSandLayerOverride ?? pipeline?.sandLayer ?? cfg.sandLayer;
            if (grass == null && sceneBindings != null) grass = sceneBindings.GrassLayer;
            if (dirt == null && sceneBindings != null) dirt = sceneBindings.DirtLayer;
            if (rock == null && sceneBindings != null) rock = sceneBindings.RockLayer;
            if (sand == null && sceneBindings != null) sand = sceneBindings.SandLayer;

            Vector2 grassTile = ResolveTileSize(generator?.terrainGrassTileSize, pipeline?.grassTileSize, null);
            Vector2 dirtTile = ResolveTileSize(generator?.terrainDirtTileSize, pipeline?.dirtTileSize, null);
            Vector2 rockTile = ResolveTileSize(generator?.terrainRockTileSize, pipeline?.rockTileSize, null);
            Vector2 sandTile = ResolveTileSize(generator?.terrainSandTileSize, pipeline?.sandTileSize, null);
            int shoreCells = generator != null && generator.terrainSandShoreCells > 0
                ? generator.terrainSandShoreCells
                : (pipeline != null && pipeline.sandShoreCells > 0 ? pipeline.sandShoreCells : (sceneBindings != null ? sceneBindings.SandShoreCells : 3));

            ExportUwpTerrainHeightAndSplat(
                terrain, grid, cfg, grass, dirt, rock, sand,
                grassTile, dirtTile, rockTile, sandTile, shoreCells,
                logSplatSummary: false);

#if UNITY_EDITOR
            if (terrain.terrainData != null)
            {
                EditorUtility.SetDirty(terrain.terrainData);
                EditorUtility.SetDirty(terrain);
            }
#endif

            Debug.LogWarning(
                $"[UWP] Terrain resync post-agua | maskCells={maskCells} carve={cfg.riverTerrainCarveDepthWorld:F3}m " +
                $"extra={cfg.riverVisualTerrainCarveExtraCells} falloff={cfg.riverTerrainCarveFalloffCells} seed={cfg.seed}");
        }

        static void ExportUwpTerrainHeightAndSplat(
            Terrain terrain,
            GridSystem grid,
            MapGenConfig cfg,
            TerrainLayer grass,
            TerrainLayer dirt,
            TerrainLayer rock,
            TerrainLayer sand,
            Vector2 grassTile,
            Vector2 dirtTile,
            Vector2 rockTile,
            Vector2 sandTile,
            int shoreCells,
            bool logSplatSummary)
        {
            TerrainExporter.ApplyToTerrain(
                terrain, grid, cfg,
                grass, dirt, rock,
                grassTile, dirtTile, rockTile,
                sand, sandTile, shoreCells);

            PersistUwpTerrainLayersOnTerrainDataAsset(
                terrain.terrainData, grass, dirt, rock, sand,
                grassTile, dirtTile, rockTile, sandTile, shoreCells);

            EnsureUwpTerrainRenderingMaterial(terrain);

            TerrainSplatDebugDisplay.Refresh(terrain, cfg);
            terrain.drawInstanced = true;
            terrain.basemapDistance = 4096f;
            terrain.heightmapPixelError = 2f;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            terrain.Flush();
            terrain.enabled = false;
            terrain.enabled = true;

#if UNITY_EDITOR
            if (logSplatSummary && terrain.terrainData != null)
            {
                int layerCount = terrain.terrainData.terrainLayers != null ? terrain.terrainData.terrainLayers.Length : 0;
                if (layerCount == 0)
                    Debug.LogWarning("[UWP] Terrain sin layers tras export. Asigna Grass/Dirt/Rock en Pipeline Config.");
                else
                {
                    int withDiffuse = 0;
                    if (terrain.terrainData.terrainLayers != null)
                    {
                        foreach (var l in terrain.terrainData.terrainLayers)
                            if (l != null && l.diffuseTexture != null) withDiffuse++;
                    }
                    Debug.Log($"[UWP] Terrain splat OK: {layerCount} layers ({withDiffuse} con diffuse), alphamap {terrain.terrainData.alphamapWidth}x{terrain.terrainData.alphamapHeight}.");
                    if (withDiffuse < layerCount)
                        Debug.LogWarning("[UWP] Algunas TerrainLayers no tienen diffuse. Revisa Texture_Grass/Dirt/Rock/Sand.");
                }
                EditorUtility.SetDirty(terrain.terrainData);
                EditorUtility.SetDirty(terrain);
            }
#endif
        }

        /// <summary>
        /// TerrainExporter clona layers con Instantiate (tile size). Esos clones no se serializan en un
        /// TerrainData guardado como .asset → referencias null y checkerboard. Los embebemos como sub-assets.
        /// </summary>
        static void PersistUwpTerrainLayersOnTerrainDataAsset(
            TerrainData data,
            TerrainLayer grass,
            TerrainLayer dirt,
            TerrainLayer rock,
            TerrainLayer sand,
            Vector2 grassTile,
            Vector2 dirtTile,
            Vector2 rockTile,
            Vector2 sandTile,
            int sandShoreCells)
        {
            if (data == null) return;

            string assetPath = AssetDatabase.GetAssetPath(data);
            bool persistAsSubAssets = !string.IsNullOrEmpty(assetPath);

            if (persistAsSubAssets)
            {
                Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                for (int i = 0; i < subAssets.Length; i++)
                {
                    if (subAssets[i] == data) continue;
                    if (subAssets[i] is TerrainLayer tl && tl.name.EndsWith("_UWP"))
                        AssetDatabase.RemoveObjectFromAsset(tl);
                }
            }

            bool useSand = sand != null && sandShoreCells > 0;
            var layers = new List<TerrainLayer>(useSand ? 4 : 3);

            void AddLayer(TerrainLayer source, Vector2 tileSize)
            {
                if (source == null) return;

                bool customTile = tileSize.x > 0.01f || tileSize.y > 0.01f;
                if (!persistAsSubAssets && !customTile)
                {
                    layers.Add(source);
                    return;
                }

                TerrainLayer layer = customTile ? Object.Instantiate(source) : source;
                if (customTile)
                {
                    layer.name = source.name + "_UWP";
                    layer.tileSize = new Vector2(
                        tileSize.x > 0.01f ? tileSize.x : source.tileSize.x,
                        tileSize.y > 0.01f ? tileSize.y : source.tileSize.y);
                }

                if (persistAsSubAssets && customTile)
                    AssetDatabase.AddObjectToAsset(layer, data);

                layers.Add(layer);
            }

            AddLayer(grass, grassTile);
            AddLayer(dirt, dirtTile);
            AddLayer(rock, rockTile);
            if (useSand) AddLayer(sand, sandTile);

            if (layers.Count == 0) return;

            data.terrainLayers = layers.ToArray();
            EditorUtility.SetDirty(data);
        }

        /// <summary>
        /// URP Terrain/Lit: height-blend solo soporta 4 capas. Desactiva splats extra (humedad, grass dry, ford).
        /// </summary>
        static void ApplyUwpTerrainSplatLayerCap(MapGenConfig cfg)
        {
            if (cfg == null) return;
            cfg.grassDryBlendStrength = 0f;
            cfg.terrainMoistureStrength = 0f;
            cfg.riverFordBedLayer = null;
        }

        static void EnsureUwpSplatPercentDefaults(MapGenConfig cfg)
        {
            if (cfg == null) return;
            float sum = cfg.grassPercent01 + cfg.dirtPercent01 + cfg.rockPercent01;
            if (sum > 0.01f) return;
            cfg.grassPercent01 = 0.6f;
            cfg.dirtPercent01 = 0.2f;
            cfg.rockPercent01 = 0.2f;
        }

        static void EnsureTerrainDataAlphamapReady(TerrainData data, MapGenConfig cfg)
        {
            if (data == null) return;
            if (data.alphamapWidth > 0 && data.alphamapHeight > 0) return;
            int hm = data.heightmapResolution > 0 ? data.heightmapResolution : Mathf.Max(33, cfg != null ? cfg.gridW + 1 : 257);
            int desired = Mathf.Clamp(Mathf.Max(256, (hm - 1) / 2), 16, 1024);
            try { data.alphamapResolution = desired; }
            catch { /* algunas versiones */ }
        }

        /// <summary>
        /// Mismo criterio que RTSMapGenerator / TerrainExporter: URP Terrain/Lit sin asset custom con height-blend.
        /// </summary>
        static void EnsureUwpTerrainRenderingMaterial(Terrain terrain)
        {
            if (terrain == null) return;

            Material mat = terrain.materialTemplate;
            bool needsTerrainShader = mat == null || mat.shader == null;
            if (!needsTerrainShader)
            {
                string shaderName = mat.shader.name;
                bool isUrpTerrainLit = shaderName.Contains("Terrain/Lit");
                bool isTerrainStandard = shaderName.Contains("Terrain/Standard") || shaderName.Contains("Nature/Terrain");
                needsTerrainShader = !isUrpTerrainLit && !isTerrainStandard;
            }

            if (!needsTerrainShader && mat != null && mat.IsKeywordEnabled("_TERRAIN_BLEND_HEIGHT"))
                needsTerrainShader = true;

            if (!needsTerrainShader) return;

            Shader terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit")
                                   ?? Shader.Find("Terrain/Lit")
                                   ?? Shader.Find("Nature/Terrain/Standard")
                                   ?? Shader.Find("Terrain/Standard");
            if (terrainShader == null) return;

            var instance = new Material(terrainShader);
            instance.name = "UWP Terrain Lit";
            instance.hideFlags = HideFlags.HideAndDontSave;
            terrain.materialTemplate = instance;
        }

        static void PreAssignTerrainLayersForSplat(
            TerrainData data,
            TerrainLayer grass,
            TerrainLayer dirt,
            TerrainLayer rock)
        {
            if (data == null) return;

            var layers = new List<TerrainLayer>(3);
            if (grass != null) layers.Add(grass);
            if (dirt != null) layers.Add(dirt);
            if (rock != null) layers.Add(rock);
            if (layers.Count == 0) return;

            data.terrainLayers = layers.ToArray();
        }

        static void ResolveTerrainLayersFromProject(
            ref TerrainLayer grass,
            ref TerrainLayer dirt,
            ref TerrainLayer rock,
            ref TerrainLayer sand)
        {
            if (grass == null)
                grass = AssetDatabase.LoadAssetAtPath<TerrainLayer>(UwpAssetPaths.DefaultGrassLayer);
            if (dirt == null)
                dirt = AssetDatabase.LoadAssetAtPath<TerrainLayer>(UwpAssetPaths.DefaultDirtLayer);
            if (rock == null)
                rock = AssetDatabase.LoadAssetAtPath<TerrainLayer>(UwpAssetPaths.DefaultRockLayer);
            if (sand == null)
                sand = AssetDatabase.LoadAssetAtPath<TerrainLayer>(UwpAssetPaths.DefaultSandLayer);
        }

        static Vector2 ResolveTileSize(Vector2? fromGenerator, Vector2? fromPipeline, Vector2? fromRts)
        {
            if (fromGenerator.HasValue && fromGenerator.Value.sqrMagnitude > 0.01f) return fromGenerator.Value;
            if (fromPipeline.HasValue && fromPipeline.Value.sqrMagnitude > 0.01f) return fromPipeline.Value;
            if (fromRts.HasValue && fromRts.Value.sqrMagnitude > 0.01f) return fromRts.Value;
            return new Vector2(12f, 12f);
        }

        static void ApplyVisualBindings(
            PMGUnifiedWorldPipelineConfig pipeline,
            MapGenConfig cfg,
            MapGenerator generator,
            IUwpSceneVisualBindings sceneBindings)
        {
            if (pipeline == null || generator == null) return;

            UwpVisualBindingsModule.ApplyToGenerator(pipeline, cfg, generator);

            if (!pipeline.uwpIndependentMode)
            {
                if (generator.terrainGrassLayerOverride == null && sceneBindings != null)
                    generator.terrainGrassLayerOverride = sceneBindings.GrassLayer;
                if (generator.terrainDirtLayerOverride == null && sceneBindings != null)
                    generator.terrainDirtLayerOverride = sceneBindings.DirtLayer;
                if (generator.terrainRockLayerOverride == null && sceneBindings != null)
                    generator.terrainRockLayerOverride = sceneBindings.RockLayer;
                if (generator.terrainSandLayerOverride == null && sceneBindings != null)
                    generator.terrainSandLayerOverride = sceneBindings.SandLayer;
            }

            if (cfg != null)
                cfg.terrainMaterialTemplateOverride = null;
        }

        static void ApplyRiverVisualExportFix(PMGUnifiedWorldPipelineConfig pipeline, MapGenConfig cfg)
        {
            if (pipeline == null || cfg == null || !pipeline.applyRiverVisualExportFix)
                return;

            cfg.riverSurfaceBorderEndpointWidthMul = 1f;
            cfg.riverSurfaceBorderGhostCells = 0f;
            cfg.riverSurfaceSkipCapAtMapBorder = true;
            cfg.riverEndReachTerrainFixLengthCells = Mathf.Min(cfg.riverEndReachTerrainFixLengthCells, 12);
            cfg.riverEndReachTerrainFixRadiusMul = Mathf.Min(cfg.riverEndReachTerrainFixRadiusMul, 1.05f);
            cfg.riverOutletTerrainFixLengthCells = Mathf.Min(cfg.riverOutletTerrainFixLengthCells, 8);
            cfg.riverOutletTerrainFixRadiusMul = Mathf.Min(cfg.riverOutletTerrainFixRadiusMul, 1.02f);
            // Evita la franja de "círculos" en diagonal (StampRiverEndReachCorridor).
            cfg.riverEndReachTerrainFixEnabled = false;
            cfg.riverOutletTerrainFixEnabled = false;

            if (pipeline.useMouthFusionWaterPipeline)
            {
                cfg.waterVisualPipeline = WaterVisualPipelineMode.SplitLakeMsRiverMouthFusion;
                cfg.riverConfluenceTributaryEndWidthMul = Mathf.Min(cfg.riverConfluenceTributaryEndWidthMul, 0.42f);
                cfg.riverConfluenceVisualBlendLengthCells = Mathf.Max(cfg.riverConfluenceVisualBlendLengthCells, 12);
                cfg.riverConfluenceHideLastSegmentUnderMain = true;
                cfg.riverSurfaceSkipTributaryConfluenceCap = true;
            }
        }

        static void ApplyMatchGridLayout(MatchConfig match, MapGenConfig cfg)
        {
            if (match == null || cfg == null) return;

            float cellSize = Mathf.Max(0.01f, match.map.cellSize);
            if (cellSize <= 0.0001f) cellSize = 2.5f;
            int gridW = Mathf.Max(1, match.map.width);
            int gridH = Mathf.Max(1, match.map.height);
            cfg.cellSizeWorld = cellSize;
            cfg.gridW = gridW;
            cfg.gridH = gridH;
            cfg.origin = match.map.centerAtOrigin
                ? new Vector3(-gridW * cellSize * 0.5f, 0f, -gridH * cellSize * 0.5f)
                : Vector3.zero;
        }

        static bool ResolveCenterAtOrigin(PMGUnifiedWorldPipelineConfig config, IUwpSceneVisualBindings sceneBindings)
        {
            if (config != null && config.uwpIndependentMode)
                return config.centerMapAtOrigin;
            if (sceneBindings != null) return sceneBindings.CenterAtOrigin;
            return config != null && config.matchConfig != null && config.matchConfig.map.centerAtOrigin;
        }

        static void ClearSceneWaterRoot()
        {
            GameObject water = GameObject.Find("Water");
            if (water != null)
                Undo.DestroyObjectImmediate(water);
        }

        static void AttachSceneWaterToWorld(GameObject worldRoot)
        {
            if (worldRoot == null) return;
            GameObject water = GameObject.Find("Water");
            if (water == null) return;
            Undo.RecordObject(water.transform, "Parent Water To World");
            water.transform.SetParent(worldRoot.transform, true);
        }

        static void ClearChildren(GameObject root)
        {
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(root.transform.GetChild(i).gameObject);
        }

        static void MarkChecklistDone(PMGUnifiedWorldChecklistAsset checklist, string itemId, bool ok)
        {
            if (checklist?.items == null) return;
            for (int i = 0; i < checklist.items.Count; i++)
            {
                if (checklist.items[i].id != itemId) continue;
                checklist.items[i].status = ok
                    ? PMGUnifiedWorldChecklistStatus.Done
                    : PMGUnifiedWorldChecklistStatus.Blocked;
                break;
            }
        }

        static void MarkChecklistInProgress(PMGUnifiedWorldChecklistAsset checklist, string itemId)
        {
            if (checklist?.items == null) return;
            for (int i = 0; i < checklist.items.Count; i++)
            {
                if (checklist.items[i].id != itemId) continue;
                if (checklist.items[i].status == PMGUnifiedWorldChecklistStatus.NotStarted)
                    checklist.items[i].status = PMGUnifiedWorldChecklistStatus.InProgress;
                break;
            }
        }
    }
}
