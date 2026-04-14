using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Validaciones geométricas previas al carve/export. Mantiene una sola centerline autoritativa
    /// y evita aceptar ejes o rasterizados con saltos anómalos.
    /// </summary>
    public static class HydrologyValidation
    {
        public static bool ValidateRiverGeometry(
            List<Vector2> centerline,
            List<Vector2Int> rasterPath,
            MapGenConfig config,
            int gridWidth,
            int gridHeight,
            out string reason)
        {
            if (!ValidateCenterline(centerline, config, gridWidth, gridHeight, out reason))
                return false;

            if (!ValidateRasterPath(rasterPath, gridWidth, gridHeight, out reason))
                return false;

            reason = null;
            return true;
        }

        static bool ValidateCenterline(List<Vector2> centerline, MapGenConfig config, int gridWidth, int gridHeight, out string reason)
        {
            if (centerline == null || centerline.Count < 2)
            {
                reason = "centerline vacía o demasiado corta";
                return false;
            }

            float maxSegment = Mathf.Max(1.65f, Mathf.Clamp(config.riverCenterlineSampleSpacingCells, 0.08f, 0.55f) * 6f);
            float maxTurn = Mathf.Clamp(config.riverMaxTurnAngleDegrees + 18f, 60f, 178f);

            for (int i = 0; i < centerline.Count; i++)
            {
                Vector2 p = centerline[i];
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.x) || float.IsInfinity(p.y))
                {
                    reason = $"centerline contiene punto inválido en {i}";
                    return false;
                }

                if (p.x < -0.5f || p.y < -0.5f || p.x > gridWidth + 0.5f || p.y > gridHeight + 0.5f)
                {
                    reason = $"centerline sale del mapa en {i}";
                    return false;
                }

                if (i == 0)
                    continue;

                float step = Vector2.Distance(centerline[i - 1], p);
                if (step > maxSegment)
                {
                    reason = $"centerline con salto brusco {step:F2} en segmento {i - 1}->{i}";
                    return false;
                }
            }

            for (int i = 1; i < centerline.Count - 1; i++)
            {
                Vector2 a = (centerline[i] - centerline[i - 1]);
                Vector2 b = (centerline[i + 1] - centerline[i]);
                if (a.sqrMagnitude < 1e-5f || b.sqrMagnitude < 1e-5f)
                    continue;

                float angle = Vector2.Angle(a, b);
                if (angle > maxTurn)
                {
                    reason = $"centerline con quiebre excesivo {angle:F1}° en muestra {i}";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        static bool ValidateRasterPath(List<Vector2Int> rasterPath, int gridWidth, int gridHeight, out string reason)
        {
            if (rasterPath == null || rasterPath.Count < 2)
            {
                reason = "raster del río vacío o demasiado corto";
                return false;
            }

            for (int i = 0; i < rasterPath.Count; i++)
            {
                Vector2Int c = rasterPath[i];
                if ((uint)c.x >= (uint)gridWidth || (uint)c.y >= (uint)gridHeight)
                {
                    reason = $"raster fuera de bounds en {i}";
                    return false;
                }

                if (i == 0)
                    continue;

                Vector2Int prev = rasterPath[i - 1];
                int dx = Mathf.Abs(c.x - prev.x);
                int dz = Mathf.Abs(c.y - prev.y);
                if (Mathf.Max(dx, dz) > 1)
                {
                    reason = $"raster con salto no contiguo en {i - 1}->{i}";
                    return false;
                }
            }

            reason = null;
            return true;
        }
    }
}
