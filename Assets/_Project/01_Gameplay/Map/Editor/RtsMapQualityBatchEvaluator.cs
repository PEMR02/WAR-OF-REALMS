using System.Collections.Generic;
using System.IO;
using System.Text;
using Project.Gameplay.Map.CleanWaterPipeline;
using Project.Gameplay.Map.Generation;
using Project.Gameplay.Map.Generator;
using UnityEditor;
using UnityEngine;

namespace Project.Gameplay.Map.Editor
{
    public static class RtsMapQualityBatchEvaluator
    {
        const string ReportDir = "Assets/_Project/01_Gameplay/Map/Reports";

        [MenuItem("PMG/Map/RTS/Analyze 20 Map Quality (v1 worldMeters)", false, 20)]
        public static void AnalyzeTwentyMenu() => RunMenuBatch(20, 8);

        [MenuItem("PMG/Map/RTS/Analyze 100 Map Quality (v1 worldMeters)", false, 21)]
        public static void AnalyzeHundredMenu() => RunMenuBatch(100, 12);

        static void RunMenuBatch(int count, int topN)
        {
            var rts = Object.FindFirstObjectByType<RTSMapGenerator>();
            if (rts == null)
            {
                EditorUtility.DisplayDialog(
                    "RTS Map Quality",
                    "No hay RTSMapGenerator en la escena activa.",
                    "OK");
                return;
            }

            var summary = RunBatch(rts, count, topN, showProgress: true);
            if (summary.evaluatedCount == 0)
            {
                EditorUtility.DisplayDialog("RTS Map Quality", "No se evaluó ninguna semilla.", "OK");
                return;
            }

            LogSummary(summary);
            string path = WriteReport(summary, rts);
            EditorUtility.DisplayDialog(
                "Análisis completado",
                $"{RtsMapQualityEvaluator.ContractVersion}\n" +
                $"Evaluados: {summary.evaluatedCount}\n" +
                $"Hard pass: {summary.hardPassCount}\n" +
                $"Mejor: seed={summary.top[0].seed} score={summary.top[0].totalScore:F1}\n\n" +
                path,
                "OK");
        }

        public static RtsMapQualityEvaluator.BatchSummary RunBatch(
            RTSMapGenerator rts,
            int count,
            int topN,
            bool showProgress = false)
        {
            var reports = new List<RtsMapQualityEvaluator.RtsMapQualityReport>(count);
            List<int> seeds = RtsMapQualityEvaluator.BuildVariedSeedList(count);

            for (int i = 0; i < seeds.Count; i++)
            {
                if (showProgress &&
                    EditorUtility.DisplayCancelableProgressBar(
                        "RTS Map Quality",
                        $"Seed {seeds[i]} ({i + 1}/{seeds.Count})",
                        (i + 1) / (float)seeds.Count))
                {
                    break;
                }

                if (!rts.TryGenerateForQualityEvaluation(seeds[i], out GridSystem grid, out MapGenConfig cfg))
                    continue;

                reports.Add(RtsMapQualityEvaluator.Evaluate(grid, cfg, seeds[i]));
                Object.DestroyImmediate(cfg);
            }

            if (showProgress)
                EditorUtility.ClearProgressBar();

            var gen = rts.GetComponent<MapGenerator>();
            if (gen != null)
                gen.config = rts.definitiveMapGenConfig;

            return RtsMapQualityEvaluator.EvaluateBatch(reports, topN);
        }

        public static void LogSummary(RtsMapQualityEvaluator.BatchSummary summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== RTS Map Quality — {RtsMapQualityEvaluator.ContractVersion} ===");
            sb.AppendLine($"Hard pass: {summary.hardPassCount}/{summary.evaluatedCount}");
            sb.AppendLine("--- TOP ---");
            for (int i = 0; i < summary.top.Length; i++)
            {
                var r = summary.top[i];
                sb.AppendLine($"{i + 1,2}. {r} | {r.notes}");
            }

            sb.AppendLine("--- BOTTOM 5 ---");
            int start = Mathf.Max(0, summary.all.Length - 5);
            for (int i = summary.all.Length - 1; i >= start; i--)
                sb.AppendLine($"   {summary.all[i]}");

            Debug.Log(sb.ToString());
        }

        public static string WriteReport(RtsMapQualityEvaluator.BatchSummary summary, RTSMapGenerator rts)
        {
            if (!Directory.Exists(ReportDir))
                Directory.CreateDirectory(ReportDir);

            string fileName = $"rts-map-quality-{RtsMapQualityEvaluator.ContractVersion}.txt";
            string path = Path.Combine(ReportDir, fileName);
            var sb = new StringBuilder();
            sb.AppendLine("RTS Map Quality Report");
            sb.AppendLine($"Contract: {RtsMapQualityEvaluator.ContractVersion}");
            sb.AppendLine($"Generated: {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"UWP profile: applyUwpHydrologyProfile={rts.applyUwpHydrologyProfile}");
            sb.AppendLine($"Rivers/Lakes (scene): {rts.riverCount}/{rts.lakeCount}");
            sb.AppendLine($"Hard pass: {summary.hardPassCount}/{summary.evaluatedCount}");
            sb.AppendLine();

            sb.AppendLine("--- TOP ---");
            for (int i = 0; i < summary.top.Length; i++)
            {
                var r = summary.top[i];
                sb.AppendLine($"{i + 1,2}. seed={r.seed} total={r.totalScore:F1} hydro={r.hydrologyScore:F1} carve={r.carveScore:F1} hard={(r.hardPass ? 1 : 0)}");
                sb.AppendLine($"    rivers={r.placedRivers}/{r.targetRivers} tribs={r.tributaryCount} IoU={r.maskLogicalIou:F2}");
                sb.AppendLine($"    lipP50={r.bankLipP50M * 100f:F1}cm lipP90={r.bankLipP90M * 100f:F1}cm stepP95={r.bankStepP95M * 100f:F1}cm crossMed={r.crossDepthMedianM * 100f:F1}cm");
                sb.AppendLine($"    waterVisual={r.waterVisualWorldM:F2}m carveCfg={r.carveDepthCfgM:F3}m mainW={r.mainWidthCfgCells:F2}c");
                sb.AppendLine($"    {r.notes}");
            }

            sb.AppendLine();
            sb.AppendLine("--- ALL (sorted) ---");
            for (int i = 0; i < summary.all.Length; i++)
                sb.AppendLine($"{i + 1,3}. {summary.all[i]}");

            File.WriteAllText(path, sb.ToString());
            AssetDatabase.Refresh();
            return path;
        }

        /// <summary>
        /// Batch Lake First hydro: 20 seeds, sin diálogos. Mide spills, min dist confluencia,
        /// inland/headwater — para no fijarse solo en “peores 5”.
        /// Arranca async (1 seed/frame) para no tumbar MCP / Editor freeze.
        /// </summary>
        [MenuItem("PMG/Map/RTS/Analyze 20 Hydro LakeFirst (silent)", false, 22)]
        public static void AnalyzeTwentyHydroSilentMenu()
        {
            var rts = Object.FindFirstObjectByType<RTSMapGenerator>();
            if (rts == null)
            {
                Debug.LogError("[HydroBatch] No RTSMapGenerator in scene.");
                return;
            }

            if (_hydroBatchRunning)
            {
                Debug.LogWarning("[HydroBatch] already running");
                return;
            }

            StartHydroLakeFirstBatchAsync(rts, 20);
            Debug.Log("[HydroBatch] started async (20 seeds). Watch Console / Reports/lake-first-hydro-batch-20.txt");
        }

        static bool _hydroBatchRunning;
        static RTSMapGenerator _hydroBatchRts;
        static List<int> _hydroBatchSeeds;
        static int _hydroBatchIndex;
        static List<string> _hydroBatchLines;
        static int _hydroBatchOk;
        static int _hydroBatchDualSpill;
        static int _hydroBatchClosePairs;
        static string _hydroBatchPath;

        public static void StartHydroLakeFirstBatchAsync(RTSMapGenerator rts, int count)
        {
            if (!Directory.Exists(ReportDir))
                Directory.CreateDirectory(ReportDir);

            _hydroBatchRts = rts;
            _hydroBatchSeeds = RtsMapQualityEvaluator.BuildVariedSeedList(count);
            _hydroBatchIndex = 0;
            _hydroBatchOk = 0;
            _hydroBatchDualSpill = 0;
            _hydroBatchClosePairs = 0;
            _hydroBatchPath = Path.Combine(ReportDir, "lake-first-hydro-batch-20-post-spill-cap.txt");
            _hydroBatchLines = new List<string>(count + 32)
            {
                "Lake First Hydro Batch",
                $"Generated: {System.DateTime.Now:yyyy-MM-dd HH:mm}",
                $"count={count} grid~={rts.width}x{rts.height} pipeline={rts.riverWaterPlayPipeline}",
                "cols: seed lakes spill inland hw rivers minSpillJoinDist closeSpillPair(<12) notes",
                "---"
            };
            _hydroBatchRunning = true;
            EditorApplication.update -= HydroBatchTick;
            EditorApplication.update += HydroBatchTick;
            File.WriteAllText(_hydroBatchPath, string.Join("\n", _hydroBatchLines) + "\n# running...\n");
        }

        static void HydroBatchTick()
        {
            if (!_hydroBatchRunning || _hydroBatchRts == null)
            {
                EditorApplication.update -= HydroBatchTick;
                return;
            }

            if (_hydroBatchIndex >= _hydroBatchSeeds.Count)
            {
                FinishHydroBatch();
                return;
            }

            int seed = _hydroBatchSeeds[_hydroBatchIndex];
            _hydroBatchIndex++;

            try
            {
                EvaluateOneHydroSeed(seed);
            }
            catch (System.Exception ex)
            {
                _hydroBatchLines.Add($"{seed}\tEX={ex.GetType().Name}");
            }

            // Progress flush
            if (_hydroBatchIndex % 2 == 0 || _hydroBatchIndex >= _hydroBatchSeeds.Count)
            {
                File.WriteAllText(
                    _hydroBatchPath,
                    string.Join("\n", _hydroBatchLines) +
                    $"\n# progress {_hydroBatchIndex}/{_hydroBatchSeeds.Count}\n");
            }

            if (_hydroBatchIndex >= _hydroBatchSeeds.Count)
                FinishHydroBatch();
        }

        static void EvaluateOneHydroSeed(int seed)
        {
            if (!_hydroBatchRts.TryGenerateForQualityEvaluation(seed, out GridSystem grid, out MapGenConfig cfg))
            {
                _hydroBatchLines.Add($"{seed}\tFAIL_GEN");
                return;
            }

            _hydroBatchOk++;
            int lakes = 0, spill = 0, inland = 0, hw = 0;
            var spillEnds = new List<Vector2>();
            if (grid?.RiverOriginKinds != null && grid.RiverCenterlinesCellSpace != null)
            {
                int n = Mathf.Min(grid.RiverOriginKinds.Count, grid.RiverCenterlinesCellSpace.Count);
                for (int ri = 0; ri < n; ri++)
                {
                    var kind = grid.RiverOriginKinds[ri];
                    if (kind == UwpTributaryOriginKind.LakeSpill) spill++;
                    else if (kind == UwpTributaryOriginKind.InlandFeeder) inland++;
                    else if (kind == UwpTributaryOriginKind.HeadwaterFeeder) hw++;

                    if (kind == UwpTributaryOriginKind.LakeSpill)
                    {
                        var line = grid.RiverCenterlinesCellSpace[ri];
                        if (line != null && line.Count >= 2)
                            spillEnds.Add(line[line.Count - 1]);
                    }
                }
            }

            if (grid?.LakeBodyComponents != null)
            {
                for (int li = 0; li < grid.LakeBodyComponents.Count; li++)
                {
                    var c = grid.LakeBodyComponents[li];
                    if (c != null && c.Count > 0)
                        lakes++;
                }
            }

            float minJoin = float.MaxValue;
            bool close = false;
            for (int a = 0; a < spillEnds.Count; a++)
            {
                for (int b = a + 1; b < spillEnds.Count; b++)
                {
                    float d = Mathf.Max(
                        Mathf.Abs(spillEnds[a].x - spillEnds[b].x),
                        Mathf.Abs(spillEnds[a].y - spillEnds[b].y));
                    if (d < minJoin)
                        minJoin = d;
                    if (d < 12f)
                        close = true;
                }
            }

            if (spillEnds.Count < 2)
                minJoin = -1f;
            if (spill >= 2)
                _hydroBatchDualSpill++;
            if (close)
                _hydroBatchClosePairs++;

            string notes = close ? "CLOSE_SPILL_JOIN" : (spill >= 2 ? "dual_spill_ok_sep" : "ok");
            _hydroBatchLines.Add(
                $"{seed}\tlakes={lakes}\tspill={spill}\tinland={inland}\thw={hw}\t" +
                $"rivers={(grid?.RiverCenterlinesCellSpace?.Count ?? 0)}\t" +
                $"minJoin={(minJoin < 0f ? -1f : minJoin):F1}\tclose={(close ? 1 : 0)}\t{notes}");

            Object.DestroyImmediate(cfg);
        }

        static void FinishHydroBatch()
        {
            EditorApplication.update -= HydroBatchTick;
            _hydroBatchRunning = false;

            _hydroBatchLines.Add("---");
            _hydroBatchLines.Add(
                $"evaluated={_hydroBatchOk}/{_hydroBatchSeeds.Count} dualSpillSeeds={_hydroBatchDualSpill} closeSpillPairs={_hydroBatchClosePairs}");
            _hydroBatchLines.Add(
                "Interpret: closeSpillPairs = 2 LakeSpill desembocando a <12 celdas (caso '2 ríos en lago'). " +
                "No corrijáis solo peores 5 sin mirar tasa close/dual.");

            File.WriteAllText(_hydroBatchPath, string.Join("\n", _hydroBatchLines) + "\n");
            AssetDatabase.Refresh();

            var gen = _hydroBatchRts != null ? _hydroBatchRts.GetComponent<MapGenerator>() : null;
            if (gen != null)
                gen.config = _hydroBatchRts.definitiveMapGenConfig;

            Debug.Log(
                $"[HydroBatch] DONE evaluated={_hydroBatchOk}/{_hydroBatchSeeds.Count} " +
                $"dualSpill={_hydroBatchDualSpill} closePairs={_hydroBatchClosePairs} → {_hydroBatchPath}");

            _hydroBatchRts = null;
            _hydroBatchSeeds = null;
            _hydroBatchLines = null;
        }

        /// <summary>CLI / batchmode: genera reporte sync (sin update tick).</summary>
        public static void RunHydroBatchCli20()
        {
            const string sceneRel = "Assets/_Project/07_Scenes/SampleScene.unity";
            if (System.IO.File.Exists(sceneRel))
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(sceneRel);

            var rts = Object.FindFirstObjectByType<RTSMapGenerator>();
            if (rts == null)
            {
                Debug.LogError("[HydroBatch] CLI: no RTSMapGenerator after opening SampleScene.");
                return;
            }

            // Forzar Lake First si el componente está en otro modo.
            rts.riverWaterPlayPipeline = RuntimeRiverWaterPipelineMode.LakeFirstHydrology;

            if (!Directory.Exists(ReportDir))
                Directory.CreateDirectory(ReportDir);

            string path = Path.Combine(ReportDir, "lake-first-hydro-batch-20.txt");
            var lines = new List<string>
            {
                "Lake First Hydro Batch (CLI sync)",
                $"Generated: {System.DateTime.Now:yyyy-MM-dd HH:mm}",
                $"count=20 grid~={rts.width}x{rts.height} pipeline={rts.riverWaterPlayPipeline}",
                "cols: seed lakes spill inland hw rivers minSpillJoinDist closeSpillPair(<12) notes",
                "---"
            };

            var seeds = RtsMapQualityEvaluator.BuildVariedSeedList(20);
            int ok = 0, dual = 0, closeN = 0;
            _hydroBatchLines = lines;
            _hydroBatchOk = 0;
            _hydroBatchDualSpill = 0;
            _hydroBatchClosePairs = 0;
            _hydroBatchRts = rts;

            for (int i = 0; i < seeds.Count; i++)
            {
                EvaluateOneHydroSeed(seeds[i]);
                Debug.Log($"[HydroBatch] CLI {i + 1}/{seeds.Count} seed={seeds[i]}");
            }

            ok = _hydroBatchOk;
            dual = _hydroBatchDualSpill;
            closeN = _hydroBatchClosePairs;
            lines = _hydroBatchLines;
            lines.Add("---");
            lines.Add($"evaluated={ok}/20 dualSpillSeeds={dual} closeSpillPairs={closeN}");
            lines.Add(
                "Interpret: closeSpillPairs = 2 LakeSpill desembocando a <12 celdas (caso '2 ríos en lago').");
            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            Debug.Log($"[HydroBatch] CLI DONE evaluated={ok}/20 dual={dual} close={closeN} → {path}");

            _hydroBatchRts = null;
            _hydroBatchLines = null;
        }
    }
}
