using Project.Gameplay.Map.Generation.Alpha;
using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace Project.Gameplay.Map.Generation
{
    /// <summary>Logs estructurados post-generación (alpha / diagnóstico).</summary>
    public static class MapGenerationPipelineLogger
    {
        public static void LogPostGenerate(RuntimeMapGenerationSettings rt, GridSystem grid, MapGenConfig cfg)
        {
            if (rt == null || grid == null) return;
            string mode = rt.UsedHighLevelAlphaConfig ? "ALPHA (alto nivel)" : "Legacy";
            int rivers = cfg?.riverCount ?? 0;
            int lakes = cfg?.lakeCount ?? 0;
            int peaks = rt.TerrainFeatures?.mountains?.Count ?? 0;
            Debug.Log(
                $"[MapGen] === Pipeline completado ({mode}) ===\n" +
                $"  Match: {rt.SourceMatchName} | seed={rt.ResolvedSeed} | water01={rt.ResolvedWaterHeight01:F3}\n" +
                $"  Ríos solicitados={rivers} | Lagos solicitados={lakes} | Picos macro registrados={peaks}\n" +
                $"  Fallback prefabs escena: {(rt.UsedLegacyResourceFallbackFromScene ? "SÍ (deprecated)" : "no")}");

            if (grid.SemanticRegions != null)
            {
                var m = grid.SemanticRegions;
                Debug.Log(
                    "[MapGen] Regiones semánticas: " +
                    $"Plain={m.CountType(TerrainRegionType.Plain)} Hill={m.CountType(TerrainRegionType.Hill)} " +
                    $"Mountain={m.CountType(TerrainRegionType.Mountain)} RiverBank={m.CountType(TerrainRegionType.RiverBank)} " +
                    $"LakeShore={m.CountType(TerrainRegionType.LakeShore)} Wet={m.CountType(TerrainRegionType.WetZone)} " +
                    $"SpawnFriendly={m.CountType(TerrainRegionType.SpawnFriendly)} ForestCand={m.CountType(TerrainRegionType.ForestCandidate)}");
            }

            if (rt.TerrainFeatures != null && rt.TerrainFeatures.rivers != null && rt.TerrainFeatures.rivers.Count > 0)
                Debug.Log($"[MapGen] Ejes de río (centerlines)={rt.TerrainFeatures.rivers.Count}");
        }
    }
}
