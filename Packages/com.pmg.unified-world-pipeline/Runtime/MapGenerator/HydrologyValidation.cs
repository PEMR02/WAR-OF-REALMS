using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    public static class HydrologyValidation
    {
        public static bool ValidateRiverCellPath(List<Vector2Int> path, int gridWidth, int gridHeight, out string reason)
        {
            reason = null;
            if (path == null || path.Count < 2)
            {
                reason = "path_corto";
                return false;
            }

            for (int i = 0; i < path.Count; i++)
            {
                Vector2Int c = path[i];
                if ((uint)c.x >= (uint)gridWidth || (uint)c.y >= (uint)gridHeight)
                {
                    reason = "oob";
                    return false;
                }

                if (i == 0)
                    continue;
                Vector2Int p = path[i - 1];
                int dx = Mathf.Abs(c.x - p.x);
                int dz = Mathf.Abs(c.y - p.y);
                if (Mathf.Max(dx, dz) > 1)
                {
                    reason = "salto_chebyshev";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Ruta planificada: continuidad 4-vecinos, tipos, auto-intersección, límites de longitud dinámicos (no downhill).
        /// </summary>
        public static bool ValidatePlannedRiverCellPath(
            GridSystem grid,
            List<Vector2Int> path,
            bool mergeToExistingRiver,
            int gridWidth,
            int gridHeight,
            int mainMinLen,
            int mainMaxLen,
            int tributaryMinLen,
            int tributaryMaxLen,
            out string reason)
        {
            reason = null;
            if (!ValidateRiverCellPath(path, gridWidth, gridHeight, out string baseReason))
            {
                reason = baseReason == "salto_chebyshev" ? "continuity" : baseReason;
                return false;
            }

            int minL = mergeToExistingRiver ? tributaryMinLen : mainMinLen;
            int maxL = mergeToExistingRiver ? tributaryMaxLen : mainMaxLen;
            if (path.Count < minL)
            {
                reason = "too_short";
                return false;
            }

            if (path.Count > maxL)
            {
                reason = "too_long";
                return false;
            }

            var seen = new HashSet<long>();
            for (int i = 0; i < path.Count; i++)
            {
                var c = path[i];
                long pk = PackPlanned(c.x, c.y);
                if (!seen.Add(pk))
                {
                    reason = "self_intersection";
                    return false;
                }

                ref var cd = ref grid.GetCell(c.x, c.y);
                if (cd.type == CellType.Mountain)
                {
                    reason = "mountain";
                    return false;
                }

                if (mergeToExistingRiver)
                {
                    if (i < path.Count - 1)
                    {
                        if (cd.type != CellType.Land)
                        {
                            reason = "invalid_cell_type";
                            return false;
                        }
                    }
                    else
                    {
                        if (cd.type != CellType.River)
                        {
                            reason = "invalid_cell_type";
                            return false;
                        }
                    }
                }
                else
                {
                    if (cd.type == CellType.Water || cd.type == CellType.River)
                    {
                        reason = "water";
                        return false;
                    }

                    if (cd.type != CellType.Land)
                    {
                        reason = "invalid_cell_type";
                        return false;
                    }
                }
            }

            return true;
        }

        static long PackPlanned(int x, int y) => ((long)x << 32) | (uint)y;
    }
}
