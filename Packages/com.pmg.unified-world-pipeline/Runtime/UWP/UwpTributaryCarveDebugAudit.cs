using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Debug de carve tributario en boca lago: compara centerline, máscara, terreno y floorH.
    /// Activar con <see cref="MapGenConfig.debugDrawTributaryCarveAudit"/>.
    /// </summary>
    public static class UwpTributaryCarveDebugAudit
    {
        public struct MouthSample
        {
            public int RiverIndex;
            public int PointIndex;
            public float Along01;
            public Vector2 CellCenter;
            public CellType CellType;
            public bool MaskHit;
            public bool LandAdjacentLake;
            public bool InLakeBody;
            public float Height01Before;
            public float Height01After;
            public float FloorH01;
            public float HalfWidthWorld;
            public bool CarvedOk;
        }

        static readonly List<MouthSample> s_samples = new List<MouthSample>(96);
        static bool s_loggedSummary;

        public static void Clear()
        {
            s_samples.Clear();
            s_loggedSummary = false;
        }

        public static IReadOnlyList<MouthSample> Samples => s_samples;

        public static void AuditAfterCarve(
            float[,] outH,
            GridSystem grid,
            MapGenConfig config,
            bool[,] mask)
        {
            if (config == null || !config.debugDrawTributaryCarveAudit || grid?.RiverVisualSurfaces == null)
                return;

            Clear();
            int w = grid.Width;
            int h = grid.Height;
            float depthW = config.riverTerrainCarveDepthWorld;
            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;
            float depth01 = depthW < 1e-4f ? 0f : Mathf.Clamp(depthW, 0f, 3f) / terrainY;
            const int mouthSpan = 14;

            for (int ri = 1; ri < grid.RiverVisualSurfaces.Count; ri++)
            {
                var surface = grid.RiverVisualSurfaces[ri];
                if (surface == null || surface.Skipped)
                    continue;

                var finalLine = surface.FinalCenterlineCells;
                var rawLine = surface.RawFunctionalCenterlineCells;
                if (finalLine == null || finalLine.Count < 2)
                    continue;

                float floorH = TerrainExporter.ResolveUwpRiverCarveFloor01ForDebug(grid, config, depth01, ri);
                int count = Mathf.Min(mouthSpan, finalLine.Count);
                for (int pi = 0; pi < count; pi++)
                    RecordPoint(outH, grid, mask, w, h, ri, pi, finalLine, floorH, surface, pi / (float)Mathf.Max(1, finalLine.Count - 1));

                // Headwater: el dique está en el extremo join (along alto), no en el origen.
                bool headwater = UwpTributaryOriginUtility.GetOrigin(grid, ri) == UwpTributaryOriginKind.HeadwaterFeeder;
                if (headwater && finalLine.Count > mouthSpan)
                {
                    int joinStart = Mathf.Max(0, finalLine.Count - mouthSpan);
                    for (int pi = joinStart; pi < finalLine.Count; pi++)
                    {
                        float along = pi / (float)Mathf.Max(1, finalLine.Count - 1);
                        RecordPoint(outH, grid, mask, w, h, ri, pi, finalLine, floorH, surface, along);
                    }
                }

                if (rawLine != null && rawLine.Count >= 2)
                {
                    int rawCount = Mathf.Min(6, rawLine.Count);
                    for (int pi = 0; pi < rawCount; pi++)
                    {
                        float along = pi / (float)Mathf.Max(1, rawLine.Count - 1);
                        RecordPoint(outH, grid, mask, w, h, ri, -1 - pi, rawLine, floorH, surface, along);
                    }
                }
            }

            LogSummary(config);
        }

        static void RecordPoint(
            float[,] outH,
            GridSystem grid,
            bool[,] mask,
            int w,
            int h,
            int riverIndex,
            int pointIndex,
            List<Vector2> line,
            float floorH,
            RiverVisualSurfaceData surface,
            float along01)
        {
            int idx = pointIndex < 0 ? (-pointIndex - 1) : pointIndex;
            idx = Mathf.Clamp(idx, 0, line.Count - 1);
            Vector2 p = line[idx];
            int cx = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, w - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, h - 1);
            ref var cell = ref grid.GetCell(cx, cz);

            bool maskHit = mask != null && mask[cx, cz];
            bool landAdj = TerrainExporter.IsLandCellAdjacentToLakeWaterForDebug(grid, cx, cz);
            bool inLake = grid.LakeBodyCellsPacked != null && grid.LakeBodyCellsPacked.Contains(PackCell(cx, cz));
            float after = outH != null ? outH[cx, cz] : cell.height01;
            float halfW = 0f;
            if (surface.HalfWidthsWorld != null && surface.HalfWidthsWorld.Count > 0)
            {
                int hwIdx = Mathf.Clamp(idx, 0, surface.HalfWidthsWorld.Count - 1);
                halfW = surface.HalfWidthsWorld[hwIdx];
            }

            const float carvedTol = 0.004f;
            bool carvedOk = after <= floorH + carvedTol;

            s_samples.Add(new MouthSample
            {
                RiverIndex = riverIndex,
                PointIndex = pointIndex,
                Along01 = along01,
                CellCenter = p,
                CellType = cell.type,
                MaskHit = maskHit,
                LandAdjacentLake = landAdj,
                InLakeBody = inLake,
                Height01Before = cell.height01,
                Height01After = after,
                FloorH01 = floorH,
                HalfWidthWorld = halfW,
                CarvedOk = carvedOk
            });

            if (config_ShouldLogPoint(pointIndex, along01, maskHit, landAdj, carvedOk, inLake))
            {
                Debug.Log(
                    $"[TributaryCarveMouthAudit] riverIndex={riverIndex} pt={pointIndex} along={along01:F2} " +
                    $"cell=({cx},{cz}) type={cell.type} mask={(maskHit ? 1 : 0)} landAdjLake={(landAdj ? 1 : 0)} " +
                    $"height01={cell.height01:F3} after={after:F3} floorH={floorH:F3} carved={(carvedOk ? 1 : 0)}");
            }
        }

        static bool config_ShouldLogPoint(
            int pointIndex,
            float along01,
            bool maskHit,
            bool landAdj,
            bool carvedOk,
            bool inLake)
        {
            if (pointIndex < 0)
                return false;
            if (inLake)
                return false;
            if (!carvedOk && (maskHit || landAdj || along01 <= 0.28f))
                return true;
            return false;
        }

        static void LogSummary(MapGenConfig config)
        {
            if (s_loggedSummary || s_samples.Count == 0)
                return;
            s_loggedSummary = true;

            int fail = 0;
            int maskMiss = 0;
            int landGap = 0;
            for (int i = 0; i < s_samples.Count; i++)
            {
                var s = s_samples[i];
                if (s.PointIndex < 0 || s.InLakeBody)
                    continue;
                if (s.CarvedOk)
                    continue;
                fail++;
                if (!s.MaskHit)
                    maskMiss++;
                if (s.LandAdjacentLake)
                    landGap++;
            }

            if (fail > 0 || config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[TributaryCarveMouthAudit] seed={config.seed} samples={s_samples.Count} " +
                    $"mouthFail={fail} maskMiss={maskMiss} landAdjUncarved={landGap}");
            }
        }

        static long PackCell(int x, int z) => ((long)x << 32) | (uint)z;

        public static void DrawGizmos(GridSystem grid, MapGenConfig config)
        {
            if (grid == null || config == null || !config.debugDrawTributaryCarveAudit || s_samples.Count == 0)
                return;

            float cs = grid.CellSizeWorld;
            Vector3 o = grid.Origin;
            float terrainY = config.terrainHeightWorld > 0f ? config.terrainHeightWorld : 50f;

            for (int i = 0; i < s_samples.Count; i++)
            {
                var s = s_samples[i];
                if (s.InLakeBody)
                    continue;

                float worldTerrainY = o.y + s.Height01After * terrainY;
                float worldFloorY = o.y + s.FloorH01 * terrainY;
                Vector3 p = new Vector3(o.x + s.CellCenter.x * cs, worldTerrainY, o.z + s.CellCenter.y * cs);
                Vector3 floor = new Vector3(p.x, worldFloorY, p.z);

                if (s.PointIndex < 0)
                {
                    Gizmos.color = new Color(1f, 0.92f, 0.1f, 0.85f);
                    Gizmos.DrawWireSphere(p, cs * 0.07f);
                    continue;
                }

                if (s.CarvedOk)
                    Gizmos.color = new Color(0.15f, 0.95f, 0.25f, 0.9f);
                else if (!s.MaskHit && s.LandAdjacentLake)
                    Gizmos.color = new Color(1f, 0.1f, 0.75f, 0.95f);
                else if (!s.MaskHit)
                    Gizmos.color = new Color(1f, 0.25f, 0.1f, 0.9f);
                else
                    Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.95f);

                Gizmos.DrawSphere(p, cs * 0.08f);
                Gizmos.DrawLine(p, floor);

                if (s.HalfWidthWorld > 1e-4f)
                {
                    Gizmos.color = new Color(0.2f, 0.75f, 1f, 0.35f);
                    Gizmos.DrawWireSphere(p, s.HalfWidthWorld);
                }
            }
        }
    }
}
