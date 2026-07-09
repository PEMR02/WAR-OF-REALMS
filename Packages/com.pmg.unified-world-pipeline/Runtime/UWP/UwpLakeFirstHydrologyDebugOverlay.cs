using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Gizmo overlay para el pipeline lake-first: main, outlet, tributario, confluencia y carve path.
    /// </summary>
    public static class UwpLakeFirstHydrologyDebugOverlay
    {
        public static void DrawGizmos(GridSystem grid, MapGenConfig config, float groundY)
        {
            if (grid == null || config == null || !config.debugDrawLakeFirstHydrologyOverlay)
                return;
            var graph = grid.LakeFirstWaterGraph;
            if (graph == null)
                return;

            float cs = grid.CellSizeWorld;
            Vector3 o = grid.Origin;
            float y = groundY + 0.12f;

            void DrawPoly(IReadOnlyList<Vector2> poly, Color col, float widthScale = 1f)
            {
                if (poly == null || poly.Count < 2)
                    return;
                Gizmos.color = col;
                for (int i = 0; i < poly.Count - 1; i++)
                {
                    Vector3 a = new Vector3(o.x + poly[i].x * cs, y, o.z + poly[i].y * cs);
                    Vector3 b = new Vector3(o.x + poly[i + 1].x * cs, y, o.z + poly[i + 1].y * cs);
                    Gizmos.DrawLine(a, b);
                    if (widthScale > 1f)
                    {
                        Vector3 mid = (a + b) * 0.5f;
                        Gizmos.DrawSphere(mid, cs * 0.06f * widthScale);
                    }
                }
            }

            DrawPoly(graph.MainCenterlineCells, new Color(0.1f, 0.55f, 1f, 0.95f), 1.2f);

            for (int i = 0; i < graph.Lakes.Count; i++)
            {
                var lake = graph.Lakes[i];
                if (!lake.Accepted || !lake.OutletValid)
                    continue;
                Vector3 outlet = new Vector3(
                    o.x + (lake.OutletCell.x + 0.5f) * cs,
                    y + 0.05f,
                    o.z + (lake.OutletCell.y + 0.5f) * cs);
                Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.95f);
                Gizmos.DrawSphere(outlet, cs * 0.14f);
            }

            for (int i = 0; i < graph.Tributaries.Count; i++)
            {
                var trib = graph.Tributaries[i];
                if (!trib.Accepted)
                    continue;

                DrawPoly(trib.CenterlineCells, new Color(1f, 0.72f, 0.08f, 0.92f));
                DrawPoly(trib.DebugCarvePathCells, new Color(1f, 0.25f, 0.55f, 0.85f));

                Vector3 conf = new Vector3(
                    o.x + (trib.MainRiverConfluenceCell.x + 0.5f) * cs,
                    y + 0.08f,
                    o.z + (trib.MainRiverConfluenceCell.y + 0.5f) * cs);
                Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.95f);
                Gizmos.DrawWireSphere(conf, cs * 0.2f);
                Gizmos.DrawSphere(conf, cs * 0.08f);
            }

            if (config.debugDrawTributaryCarveAudit && grid.RiverVisualSurfaces != null)
            {
                for (int ri = 1; ri < grid.RiverVisualSurfaces.Count; ri++)
                {
                    var surface = grid.RiverVisualSurfaces[ri];
                    if (surface?.FinalCenterlineCells == null || surface.FinalCenterlineCells.Count < 2)
                        continue;
                    DrawPoly(surface.FinalCenterlineCells, new Color(0.1f, 0.85f, 1f, 0.75f), 0.8f);
                }
            }
        }
    }
}
