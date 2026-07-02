using System.Collections.Generic;
using PMG.UnifiedWorldPipeline;
using Project.Gameplay.Map;
using Project.Gameplay.Map.Generation;
using Project.Gameplay.Map.Generator;
using UnityEditor;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline.Editor
{
    public class PMGUnifiedWorldPipelineWindow : EditorWindow
    {
        PMGUnifiedWorldPipelineConfig config;
        PMGUnifiedWorldQualityReport lastReport;
        PMGUnifiedWorldBatchSummary lastBatch;
        Vector2 scroll;
        Vector2 checklistScroll;
        bool showChecklist = true;
        bool showScores = true;
        bool showPlan = true;
        int selectedSegment;

        [MenuItem("Tools/PMG/Unified World Pipeline")]
        public static void Open()
        {
            var w = GetWindow<PMGUnifiedWorldPipelineWindow>("Unified World Pipeline");
            w.minSize = new Vector2(420f, 520f);
            w.LoadDefaultConfig();
        }

        void LoadDefaultConfig()
        {
            if (config != null) return;
            config = AssetDatabase.LoadAssetAtPath<PMGUnifiedWorldPipelineConfig>(UwpAssetPaths.PipelineConfig);
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("PMG Unified World Pipeline", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Pipeline único sobre MapGenerator (juego). Evalúa ríos, lagos, terreno, NavMesh y gameplay. " +
                "Checklist persistente para retomar por segmentos.\n\n" +
                "Flujo: 1) Cargar layers  2) Apply Full  3) NO uses otros generadores en la misma escena.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            config = (PMGUnifiedWorldPipelineConfig)EditorGUILayout.ObjectField(
                "Pipeline Config", config, typeof(PMGUnifiedWorldPipelineConfig), false);
            if (EditorGUI.EndChangeCheck() && config != null)
                config.EnsureDefaultsFromProject();

            if (config == null)
            {
                EditorGUILayout.HelpBox("Crea config: PMG → Unified World Pipeline → Create Default Assets", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawPlan();
            DrawConfigQuick();
            DrawVisualBindings();
            DrawJsonImport();
            DrawActions();
            DrawScores();
            DrawChecklist();

            EditorGUILayout.EndScrollView();
        }

        void DrawPlan()
        {
            showPlan = EditorGUILayout.Foldout(showPlan, "Plan segmentado (roadmap)", true);
            if (!showPlan) return;

            EditorGUILayout.LabelField("Segmentos", EditorStyles.miniBoldLabel);
            string[] segments =
            {
                "1 Setup — MatchConfig + materiales RTS",
                "2 Lógica — GridSystem rápido (sin mesh)",
                "3 Hidrología — notas ríos/lagos/costa",
                "4 Superficie — Terrain + WaterMeshBuilder",
                "5 NavMesh — walkable / exclusiones",
                "6 Gameplay — ciudades, recursos",
                "7 Pulido — batch seeds + congelar perfil"
            };

            selectedSegment = GUILayout.SelectionGrid(selectedSegment, segments, 1);
        }

        void DrawConfigQuick()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sesión", EditorStyles.boldLabel);

            config.uwpIndependentMode = EditorGUILayout.Toggle("Modo independiente", config.uwpIndependentMode);
            if (config.uwpIndependentMode)
            {
                EditorGUILayout.HelpBox(
                    "Perfil UWP autosuficiente: mapa, hidrología y anchos desde este config. " +
                    "MatchConfig/RTS son opcionales.",
                    MessageType.Info);
            }

            config.useSeedOverride = EditorGUILayout.Toggle("Usar seed override", config.useSeedOverride);
            if (config.useSeedOverride)
                config.seedOverride = EditorGUILayout.IntField("Seed", config.seedOverride);

            config.batchSeedCount = EditorGUILayout.IntSlider("Batch count", config.batchSeedCount, 5, 100);
            config.batchTopN = EditorGUILayout.IntSlider("Top N", config.batchTopN, 3, 25);

            config.uwpGridCells = EditorGUILayout.IntSlider("Grid (celdas)", config.uwpGridCells, 128, 512);
            config.uwpCellSizeWorld = EditorGUILayout.Slider("Cell size (m)", config.uwpCellSizeWorld, 1.5f, 4f);
            config.centerMapAtOrigin = EditorGUILayout.Toggle("Centrar mapa en origen", config.centerMapAtOrigin);
            config.useDefinitiveTemplateBaseline = EditorGUILayout.Toggle(
                "Baseline MapGen template", config.useDefinitiveTemplateBaseline);

            config.uwpCityCount = EditorGUILayout.IntSlider("Ciudades preview", config.uwpCityCount, 1, 8);

            EditorGUILayout.LabelField("Baseline UWP", EditorStyles.miniBoldLabel);
            config.uwpMapGenBaseline = (MapGenConfig)EditorGUILayout.ObjectField(
                "MapGen baseline", config.uwpMapGenBaseline, typeof(MapGenConfig), false);
            config.uwpMatchBaseline = (MatchConfig)EditorGUILayout.ObjectField(
                "Match baseline", config.uwpMatchBaseline, typeof(MatchConfig), false);
            if (GUILayout.Button("Regenerar baselines UWP"))
            {
                UwpBootstrapAssets.WirePipelineConfig(config);
                EditorUtility.SetDirty(config);
            }

            EditorGUILayout.LabelField("Referencia opcional", EditorStyles.miniBoldLabel);
            EditorGUILayout.ObjectField("MatchConfig", config.matchConfig, typeof(MatchConfig), false);
            EditorGUILayout.ObjectField("MapGen template", config.definitiveTemplate, typeof(MapGenConfig), false);
            config.sceneVisualBindingsHost = (MonoBehaviour)EditorGUILayout.ObjectField(
                "Bindings escena (opcional)", config.sceneVisualBindingsHost, typeof(MonoBehaviour), true);

            if (GUILayout.Button("Auto-vincular bindings en escena"))
            {
                var behaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is IUwpSceneVisualBindings)
                    {
                        config.sceneVisualBindingsHost = behaviours[i];
                        break;
                    }
                }
                EditorUtility.SetDirty(config);
            }
        }

        void DrawVisualBindings()
        {
            float cellM = config.uwpCellSizeWorld > 0.01f ? config.uwpCellSizeWorld : 2.5f;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Materiales / layers", EditorStyles.boldLabel);
            config.applyRiverVisualExportFix = EditorGUILayout.Toggle("Fix ensanche río en borde", config.applyRiverVisualExportFix);
            config.useMouthFusionWaterPipeline = EditorGUILayout.Toggle("MouthFusion (experimental)", config.useMouthFusionWaterPipeline);
            config.applyRiverBankTerrainFix = EditorGUILayout.Toggle("Orillas río (solo terreno)", config.applyRiverBankTerrainFix);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Hidrología (UWP)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.IntSlider("Ríos principales", config.uwpMainRiverCount, 1, 1);
            config.uwpTributaryCount = EditorGUILayout.IntSlider(
                "Tributarios", config.uwpTributaryCount, 0, 6);
            config.uwpLakeCount = EditorGUILayout.IntSlider(
                "Lagos", config.uwpLakeCount, 0, 12);
            config.uwpMaxLakeCells = EditorGUILayout.IntSlider(
                "Tamaño lago (celdas)", config.uwpMaxLakeCells, 50, 12000);
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(config);

            float lakeSideM = Mathf.Sqrt(config.uwpMaxLakeCells) * cellM;
            EditorGUILayout.HelpBox(
                $"Ríos totales: {config.uwpMainRiverCount + config.uwpTributaryCount} " +
                $"(1 troncal + {config.uwpTributaryCount} trib.) | " +
                $"Lago ~{lakeSideM:F0} m lado equiv.",
                MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Ancho río principal (UWP)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            config.uwpMainRiverFullWidthCells = EditorGUILayout.Slider(
                "Ancho principal (celdas)", config.uwpMainRiverFullWidthCells, 2f, 12f);
            config.uwpTributaryRiverFullWidthCells = EditorGUILayout.Slider(
                "Ancho tributario (celdas)", config.uwpTributaryRiverFullWidthCells, 1f, 6f);
            config.uwpMainRiverCarveDepthWorld = EditorGUILayout.Slider(
                "Carve principal (m)", config.uwpMainRiverCarveDepthWorld, 0.04f, 0.2f);
            config.uwpMainRiverSurfaceWorldY = EditorGUILayout.Slider(
                "Y río principal (world)", config.uwpMainRiverSurfaceWorldY, 0f, 4f);
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(config);

            EditorGUILayout.HelpBox(
                $"Principal ~{config.uwpMainRiverFullWidthCells * cellM:F1} m | " +
                $"Tributario ~{config.uwpTributaryRiverFullWidthCells * cellM:F1} m | " +
                $"Y main ~{(config.uwpMainRiverSurfaceWorldY > 0.01f ? config.uwpMainRiverSurfaceWorldY.ToString("F2") : "0 (base)")} | " +
                $"Runner {PMGUnifiedWorldPipelineRunner.UwpRunnerVersion}",
                MessageType.None);

            if (GUILayout.Button("Cargar capas del proyecto"))
            {
                config.PullVisualBindingsFromPipelineOnly();
                EditorUtility.SetDirty(config);
            }

            config.grassLayer = (TerrainLayer)EditorGUILayout.ObjectField("Grass", config.grassLayer, typeof(TerrainLayer), false);
            config.dirtLayer = (TerrainLayer)EditorGUILayout.ObjectField("Dirt", config.dirtLayer, typeof(TerrainLayer), false);
            config.rockLayer = (TerrainLayer)EditorGUILayout.ObjectField("Rock", config.rockLayer, typeof(TerrainLayer), false);
            config.sandLayer = (TerrainLayer)EditorGUILayout.ObjectField("Sand", config.sandLayer, typeof(TerrainLayer), false);
            config.riverWaterMaterial = (Material)EditorGUILayout.ObjectField("Río", config.riverWaterMaterial, typeof(Material), false);
            config.lakeWaterMaterial = (Material)EditorGUILayout.ObjectField("Lago", config.lakeWaterMaterial, typeof(Material), false);
            config.seaWaterMaterial = (Material)EditorGUILayout.ObjectField("Mar", config.seaWaterMaterial, typeof(Material), false);
            config.terrainMaterialTemplate = (Material)EditorGUILayout.ObjectField("Terrain mat", config.terrainMaterialTemplate, typeof(Material), false);

            if (!config.HasAnyTerrainLayerBinding())
            {
                EditorGUILayout.HelpBox(
                    "Sin TerrainLayers: verás checkerboard. Pulsa 'Cargar layers desde MapGen template' o asigna manualmente.",
                    MessageType.Warning);
            }
        }

        void DrawJsonImport()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Import Web Terrain JSON", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Terreno desde index.html (forma de continente). El agua del JSON se descarta; " +
                "se genera con WaterGenerator + WaterMeshBuilder del juego (materiales RTS).",
                MessageType.Info);

            config.webTerrainJson = (TextAsset)EditorGUILayout.ObjectField(
                "Terrain JSON", config.webTerrainJson, typeof(TextAsset), false);
            config.jsonUseVisualHeight = EditorGUILayout.Toggle("Usar height visual", config.jsonUseVisualHeight);
            config.jsonFlipZ = EditorGUILayout.Toggle("Flip Z", config.jsonFlipZ);
            config.jsonUseGameWaterSystem = EditorGUILayout.Toggle("Agua del juego (recomendado)", config.jsonUseGameWaterSystem);

            using (new EditorGUI.DisabledScope(config.webTerrainJson == null))
            {
                if (GUILayout.Button("Import JSON + Agua del juego", GUILayout.Height(28)))
                    ApplyJsonImport();
            }
        }

        void ApplyJsonImport()
        {
            PMGUnifiedWorldSessionRoot selected = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<PMGUnifiedWorldSessionRoot>()
                : null;
            PMGUnifiedWorldSessionRoot root = PMGUnifiedWorldPipelineRunner.ApplyJsonTerrainWithGameWater(
                config, null, selected);
            if (root != null)
                lastReport = root.lastReport;
            Repaint();
        }

        void DrawActions()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Acciones", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Evaluar seed", GUILayout.Height(26)))
                EvaluateCurrentSeed();

            if (GUILayout.Button($"Batch {config.batchSeedCount}", GUILayout.Height(26)))
                RunBatch();
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Apply Full To Scene", GUILayout.Height(30)))
                ApplyFull();

            PMGUnifiedWorldSessionRoot selected = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<PMGUnifiedWorldSessionRoot>()
                : null;

            if (selected != null && GUILayout.Button("Regenerar seleccionado"))
            {
                if (selected.pipelineConfig != null)
                    config = selected.pipelineConfig;
                PMGUnifiedWorldPipelineRunner.ApplyFullToScene(config, null, selected);
            }
        }

        void DrawScores()
        {
            showScores = EditorGUILayout.Foldout(showScores, "Última evaluación", true);
            if (!showScores || lastReport.aspects == null || lastReport.aspects.Length == 0)
                return;

            EditorGUILayout.LabelField(
                $"Global: {lastReport.totalGrade0To10:F1}/10 ({lastReport.totalGradeLetter}) — seed {lastReport.seed}",
                EditorStyles.boldLabel);

            for (int i = 0; i < lastReport.aspects.Length; i++)
            {
                PMGUnifiedWorldAspectScore a = lastReport.aspects[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(a.aspect.ToString(), GUILayout.Width(120f));
                EditorGUILayout.LabelField($"{a.score0To10:F1} ({a.gradeLetter})", GUILayout.Width(64f));
                EditorGUILayout.LabelField(a.details, EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }

            PMGUnifiedWorldMetrics m = lastReport.metrics;
            EditorGUILayout.LabelField(
                $"Métricas: lagos={m.lakeComponentCount} min={m.minLakeCells} ríos={m.riverCenterlineCount} " +
                $"main={m.mainRiverSpan01:P0} agua={m.waterCoverage01:P1}",
                EditorStyles.miniLabel);
        }

        void DrawChecklist()
        {
            if (config.checklist == null) return;

            showChecklist = EditorGUILayout.Foldout(showChecklist, "Checklist (retomable)", true);
            if (!showChecklist) return;

            PMGUnifiedWorldChecklistAsset cl = config.checklist;
            cl.EnsureDefaultStructure();
            float progress = cl.Progress01();
            EditorGUILayout.LabelField($"Progreso: {progress:P0} ({cl.items.Count} ítems)");
            Rect r = GUILayoutUtility.GetRect(1f, 8f);
            EditorGUI.ProgressBar(r, progress, "");

            checklistScroll = EditorGUILayout.BeginScrollView(checklistScroll, GUILayout.MaxHeight(220f));
            for (int i = 0; i < cl.items.Count; i++)
            {
                PMGUnifiedWorldChecklistItem item = cl.items[i];
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                item.status = (PMGUnifiedWorldChecklistStatus)EditorGUILayout.EnumPopup(item.status, GUILayout.Width(90f));
                EditorGUILayout.LabelField(item.title, EditorStyles.boldLabel);
                if (item.lastScore0To10 >= 0f)
                    EditorGUILayout.LabelField($"{item.lastScore0To10:F1}{item.lastGradeLetter}", GUILayout.Width(48f));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(item.description, EditorStyles.wordWrappedMiniLabel);
                item.notes = EditorGUILayout.TextField("Notas", item.notes);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Guardar checklist"))
            {
                EditorUtility.SetDirty(cl);
                AssetDatabase.SaveAssets();
            }
        }

        void EvaluateCurrentSeed()
        {
            int? seed = config.useSeedOverride ? config.seedOverride : (int?)null;
            PMGUnifiedWorldGenerationResult gen = PMGUnifiedWorldPipelineRunner.GenerateLogic(config, seed);
            lastReport = gen.report;
            PMGUnifiedWorldPipelineRunner.CleanupTemp(ref gen);
            PMGUnifiedWorldReportWriter.WriteSingle(lastReport, config);
            EditorUtility.SetDirty(config);
            if (config.checklist != null)
                EditorUtility.SetDirty(config.checklist);
            Repaint();
            Debug.Log($"[UWP] {lastReport}");
        }

        void RunBatch()
        {
            List<int> seeds = PMGUnifiedWorldQualityEvaluator.BuildVariedSeedList(config.batchSeedCount, config.ResolveSeed());
            lastBatch = PMGUnifiedWorldPipelineRunner.EvaluateBatch(config, seeds, config.batchTopN);
            if (lastBatch.top != null && lastBatch.top.Length > 0)
                lastReport = lastBatch.top[0];
            PMGUnifiedWorldReportWriter.WriteBatch(lastBatch, config);
            Repaint();
        }

        void ApplyFull()
        {
            if (config == null)
            {
                Debug.LogError("[UWP] Sin Pipeline Config asignado.");
                return;
            }

            EditorUtility.SetDirty(config);
            int? seed = config.useSeedOverride ? config.seedOverride : (int?)null;
            Debug.LogWarning(
                $"[UWP] Ventana → Apply Full | runner={PMGUnifiedWorldPipelineRunner.UwpRunnerVersion} " +
                $"rivers={config.uwpMainRiverCount + config.uwpTributaryCount} " +
                $"(trib={config.uwpTributaryCount}) lakes={config.uwpLakeCount} " +
                $"lakeCells={config.uwpMaxLakeCells} main={config.uwpMainRiverFullWidthCells:F2}c " +
                $"seed={(seed.HasValue ? seed.Value.ToString() : "auto")}");
            PMGUnifiedWorldSessionRoot root = PMGUnifiedWorldPipelineRunner.ApplyFullToScene(config, seed, null);
            if (root != null)
                lastReport = root.lastReport;
            Repaint();
        }
    }
}
