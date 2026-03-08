using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Map;

namespace Project.Gameplay.Pathfinding
{
    /// <summary>Suaviza rutas A* eliminando puntos redundantes (Douglas-Peucker).</summary>
    public static class PathSmoother
    {
        /// <summary>Convierte ruta en celdas a posiciones mundo y simplifica la polilínea.</summary>
        public static List<Vector3> SmoothPath(List<Vector2Int> cells, MapGrid grid, float epsilon = 0.5f)
        {
            if (grid == null)
                return new List<Vector3>();

            if (cells == null || cells.Count == 0)
                return new List<Vector3>();

            if (cells.Count < 3)
            {
                var result = new List<Vector3>(cells.Count);
                foreach (Vector2Int cell in cells)
                    result.Add(grid.CellToWorld(cell));
                return result;
            }

            var points = new List<Vector3>(cells.Count);
            foreach (Vector2Int cell in cells)
                points.Add(grid.CellToWorld(cell));

            return DouglasPeucker(points, epsilon);
        }

        private static List<Vector3> DouglasPeucker(List<Vector3> points, float epsilon)
        {
            if (points.Count < 3)
                return new List<Vector3>(points);

            float maxDist = 0f;
            int maxIndex = 0;

            for (int i = 1; i < points.Count - 1; i++)
            {
                float dist = PerpendicularDistance(points[i], points[0], points[points.Count - 1]);
                if (dist > maxDist)
                {
                    maxDist = dist;
                    maxIndex = i;
                }
            }

            if (maxDist > epsilon)
            {
                List<Vector3> left = DouglasPeucker(points.GetRange(0, maxIndex + 1), epsilon);
                List<Vector3> right = DouglasPeucker(points.GetRange(maxIndex, points.Count - maxIndex), epsilon);
                left.RemoveAt(left.Count - 1);
                left.AddRange(right);
                return left;
            }

            return new List<Vector3> { points[0], points[points.Count - 1] };
        }

        private static float PerpendicularDistance(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
        {
            Vector3 line = lineEnd - lineStart;
            float lineLengthSq = line.sqrMagnitude;

            if (lineLengthSq < 0.0001f)
                return Vector3.Distance(point, lineStart);

            float t = Mathf.Clamp01(Vector3.Dot(point - lineStart, line) / lineLengthSq);
            Vector3 projection = lineStart + t * line;
            return Vector3.Distance(point, projection);
        }
    }
}
