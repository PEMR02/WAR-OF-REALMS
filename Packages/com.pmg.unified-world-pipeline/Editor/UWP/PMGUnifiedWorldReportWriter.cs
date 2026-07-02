using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline.Editor
{
    public static class PMGUnifiedWorldReportWriter
    {
        const string ReportDir = UwpAssetPaths.ReportsRoot;

        public static void WriteBatch(PMGUnifiedWorldBatchSummary summary, PMGUnifiedWorldPipelineConfig config, string fileName = "uwp-batch-report.txt")
        {
            if (!Directory.Exists(ReportDir))
                Directory.CreateDirectory(ReportDir);

            string path = Path.Combine(ReportDir, fileName);
            var sb = new StringBuilder();
            sb.AppendLine("PMG Unified World Pipeline — Batch Report");
            sb.AppendLine($"Generated: {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            if (config != null)
                sb.AppendLine($"MatchConfig: {(config.matchConfig != null ? config.matchConfig.name : "null")}");
            sb.AppendLine();

            sb.AppendLine("--- TOP ---");
            if (summary.top != null)
            {
                for (int i = 0; i < summary.top.Length; i++)
                {
                    PMGUnifiedWorldQualityReport r = summary.top[i];
                    sb.AppendLine($"{i + 1,2}. {r}");
                    sb.AppendLine($"    {FormatMetrics(r.metrics)}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("--- ALL ---");
            if (summary.all != null)
            {
                for (int i = 0; i < summary.all.Length; i++)
                    sb.AppendLine($"{i + 1,2}. {summary.all[i]}");
            }

            File.WriteAllText(path, sb.ToString());
            AssetDatabase.Refresh();
            Debug.Log($"[UWP] Reporte: {path}");
        }

        public static void WriteSingle(PMGUnifiedWorldQualityReport report, PMGUnifiedWorldPipelineConfig config)
        {
            if (!Directory.Exists(ReportDir))
                Directory.CreateDirectory(ReportDir);

            string path = Path.Combine(ReportDir, $"uwp-seed-{report.seed}.txt");
            var sb = new StringBuilder();
            sb.AppendLine($"PMG UWP — Seed {report.seed}");
            sb.AppendLine($"Nota global: {report.totalGrade0To10:F1} ({report.totalGradeLetter})");
            sb.AppendLine(FormatMetrics(report.metrics));
            sb.AppendLine();

            if (report.aspects != null)
            {
                for (int i = 0; i < report.aspects.Length; i++)
                {
                    PMGUnifiedWorldAspectScore a = report.aspects[i];
                    sb.AppendLine($"{a.aspect}: {a.score0To10:F1} ({a.gradeLetter}) — {a.details}");
                }
            }

            File.WriteAllText(path, sb.ToString());
            AssetDatabase.Refresh();
        }

        static string FormatMetrics(PMGUnifiedWorldMetrics m)
        {
            return $"grid={m.gridW}x{m.gridH} lakes={m.lakeComponentCount} minLake={m.minLakeCells} " +
                   $"rivers={m.riverCenterlineCount} tribs={m.tributaryCount} main={m.mainRiverSpan01:P0} " +
                   $"water={m.waterCoverage01:P1} walkable≈{m.estimatedWalkable01:P0} cities={m.cityCount}";
        }
    }
}
