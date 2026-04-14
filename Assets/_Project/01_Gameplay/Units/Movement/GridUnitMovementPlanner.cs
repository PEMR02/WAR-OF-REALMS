using System.Collections.Generic;
using Project.Gameplay.Buildings;
using Project.Gameplay.Map;
using Project.Gameplay.Pathfinding;
using UnityEngine;

namespace Project.Gameplay.Units.Movement
{
    /// <summary>
    /// Planificación táctica: pathfinding grid, suavizado de ruta, validación de agua
    /// y subflujo de puertas. No mueve físicamente al agente.
    /// </summary>
    public sealed class GridUnitMovementPlanner : IUnitMovementPlanner
    {
        readonly Pathfinder _pathfinder = new();
        readonly bool _useGridPathfinding;
        readonly bool _canSwim;
        readonly float _pathSmoothEpsilon;

        public GridUnitMovementPlanner(bool useGridPathfinding, bool canSwim, float pathSmoothEpsilon)
        {
            _useGridPathfinding = useGridPathfinding;
            _canSwim = canSwim;
            _pathSmoothEpsilon = pathSmoothEpsilon;
        }

        public bool TryCreateGateTraversal(Vector3 from, Vector3 destination, GateController ignoredGate, out GateTraversalPlan gatePlan)
        {
            gatePlan = default;
            var gate = GateController.FindGateOnSegment(from, destination, 6f);
            if (gate == null || gate == ignoredGate || gate.entryPoint == null || gate.exitPoint == null || gate.gateCenter == null)
                return false;

            Vector3 center = gate.gateCenter.position;
            Vector3 forward = gate.gateCenter.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f)
                return false;

            forward.Normalize();
            Vector3 toUnit = from - center;
            toUnit.y = 0f;
            Vector3 toDest = destination - center;
            toDest.y = 0f;
            float sideUnit = toUnit.sqrMagnitude > 0.0001f ? Vector3.Dot(toUnit.normalized, forward) : 0f;
            float sideDest = toDest.sqrMagnitude > 0.0001f ? Vector3.Dot(toDest.normalized, forward) : 0f;
            bool oppositeSides = (sideUnit > 0.1f && sideDest < -0.1f) || (sideUnit < -0.1f && sideDest > 0.1f);
            if (!oppositeSides)
                return false;

            float entrySide = Vector3.Dot(gate.entryPoint.position - center, forward);
            Transform sameSidePoint = (entrySide * sideUnit > 0f) ? gate.entryPoint : gate.exitPoint;
            Transform oppositeSidePoint = (entrySide * sideUnit > 0f) ? gate.exitPoint : gate.entryPoint;
            if ((sameSidePoint.position - from).sqrMagnitude <= 0.9f)
                return false;

            gatePlan = new GateTraversalPlan
            {
                isValid = true,
                sameSidePoint = sameSidePoint.position,
                oppositeSidePoint = oppositeSidePoint.position,
                finalDestination = destination,
                gate = gate
            };
            return true;
        }

        public bool TryPlanPath(Vector3 from, Vector3 destination, out UnitMovementPlan plan, out string failureReason)
        {
            plan = new UnitMovementPlan
            {
                requestedDestination = destination,
                finalDestination = destination,
                isDirectFallback = true,
                waypoints = null
            };
            failureReason = null;

            if (!_useGridPathfinding || MapGrid.Instance == null || !MapGrid.Instance.IsReady)
                return true;

            PathResult result = _pathfinder.FindPath(from, destination, _canSwim);
            if (!result.success || result.cells == null)
            {
                failureReason = result.error;
                return false;
            }

            if (result.cells.Count == 0)
                return true;

            var waypoints = CellsToWorldPoints(result.cells);
            if (waypoints.Count == 0)
                return true;

            plan.isDirectFallback = false;
            plan.waypoints = waypoints;
            return true;
        }

        List<Vector3> CellsToWorldPoints(List<Vector2Int> cells)
        {
            if (cells == null || cells.Count == 0)
                return new List<Vector3>();
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady)
                return new List<Vector3>();

            List<Vector2Int> simplifiedCells = SimplifyCellPath(cells);
            var smoothed = PathSmoother.SmoothPath(simplifiedCells, MapGrid.Instance, _pathSmoothEpsilon);
            if (!_canSwim && smoothed != null && smoothed.Count >= 2 && SmoothedWorldPathCrossesWater(smoothed))
                smoothed = null;
            if (_canSwim && smoothed != null && smoothed.Count >= 2 && SmoothedWorldPathCrossesImpassableWater(smoothed))
                smoothed = null;
            if (smoothed != null && smoothed.Count >= 1)
                return smoothed;

            var list = new List<Vector3>(simplifiedCells.Count);
            foreach (Vector2Int cell in simplifiedCells)
                list.Add(MapGrid.Instance.CellToWorld(cell));
            return list;
        }

        List<Vector2Int> SimplifyCellPath(List<Vector2Int> cells)
        {
            if (cells == null || cells.Count <= 2 || MapGrid.Instance == null || !MapGrid.Instance.IsReady)
                return cells;

            var result = new List<Vector2Int>(cells.Count);
            int anchorIndex = 0;
            result.Add(cells[anchorIndex]);

            while (anchorIndex < cells.Count - 1)
            {
                int furthestReachable = anchorIndex + 1;
                for (int i = anchorIndex + 2; i < cells.Count; i++)
                {
                    if (!HasGridLineOfSight(cells[anchorIndex], cells[i]))
                        break;
                    furthestReachable = i;
                }

                result.Add(cells[furthestReachable]);
                anchorIndex = furthestReachable;
            }

            return result;
        }

        bool HasGridLineOfSight(Vector2Int from, Vector2Int to)
        {
            int x0 = from.x;
            int y0 = from.y;
            int x1 = to.x;
            int y1 = to.y;

            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                Vector2Int c = new Vector2Int(x0, y0);
                if (!MapGrid.Instance.IsInBounds(c))
                    return false;
                if (MapGrid.Instance.IsImpassableWater(c))
                    return false;
                if (!_canSwim && MapGrid.Instance.IsWater(c))
                    return false;
                if (!MapGrid.Instance.IsCellFree(c) && !MapGrid.Instance.IsOpenGatePassableCell(c))
                    return false;

                if (x0 == x1 && y0 == y1)
                    return true;

                int e2 = err * 2;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        bool SmoothedWorldPathCrossesWater(List<Vector3> worldPts)
        {
            if (worldPts == null || MapGrid.Instance == null || !MapGrid.Instance.IsReady)
                return false;
            for (int i = 0; i < worldPts.Count - 1; i++)
            {
                if (WorldXZSegmentCrossesWater(worldPts[i], worldPts[i + 1]))
                    return true;
            }
            return false;
        }

        bool SmoothedWorldPathCrossesImpassableWater(List<Vector3> worldPts)
        {
            if (worldPts == null || MapGrid.Instance == null || !MapGrid.Instance.IsReady)
                return false;
            for (int i = 0; i < worldPts.Count - 1; i++)
            {
                if (WorldXZSegmentCrossesImpassableWater(worldPts[i], worldPts[i + 1]))
                    return true;
            }
            return false;
        }

        static bool WorldXZSegmentCrossesWater(Vector3 a, Vector3 b)
        {
            var g = MapGrid.Instance;
            if (g == null || !g.IsReady)
                return false;
            Vector2Int c0 = g.WorldToCell(a);
            Vector2Int c1 = g.WorldToCell(b);
            foreach (var c in BresenhamLineCells(c0, c1))
            {
                if (!g.IsInBounds(c))
                    continue;
                if (g.IsWater(c))
                    return true;
            }
            return false;
        }

        static bool WorldXZSegmentCrossesImpassableWater(Vector3 a, Vector3 b)
        {
            var g = MapGrid.Instance;
            if (g == null || !g.IsReady)
                return false;
            Vector2Int c0 = g.WorldToCell(a);
            Vector2Int c1 = g.WorldToCell(b);
            foreach (var c in BresenhamLineCells(c0, c1))
            {
                if (!g.IsInBounds(c))
                    continue;
                if (g.IsImpassableWater(c))
                    return true;
            }
            return false;
        }

        static IEnumerable<Vector2Int> BresenhamLineCells(Vector2Int from, Vector2Int to)
        {
            int x0 = from.x, y0 = from.y;
            int x1 = to.x, y1 = to.y;
            int dx = Mathf.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                yield return new Vector2Int(x0, y0);
                if (x0 == x1 && y0 == y1)
                    yield break;
                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }
                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }
    }
}
