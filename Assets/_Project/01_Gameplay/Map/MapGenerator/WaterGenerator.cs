using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Fase 3: genera agua como sistema. Ríos = centerline procedural (meandro + Catmull–Rom + remuestreo) + Bresenham + ensanchado; lagos = flood fill.</summary>
    public static class WaterGenerator
    {
        // 🟢 Direcciones con diagonales (8) para lagos orgánicos
        private static readonly Vector2Int[] AllDirections = { 
            new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
        };

        /// <summary>Parámetros: riverCount, lakeCount, maxLakeCells. Marca CellType Water/River. Determinista por rng.</summary>
        public static void GenerateWater(GridSystem grid, MapGenConfig config, IRng rng)
        {
            if (grid == null || config == null || rng == null) return;

            int w = grid.Width;
            int h = grid.Height;
            int waterCells = 0;
            grid.RiverCenterlinesCellSpace = new List<List<Vector2>>();
            grid.RiverCenterlinesWorld = new List<List<Vector3>>();
            if (config.debugDrawRiverPathInScene)
            {
                grid.RiverPathDebugMacro = new List<List<Vector2>>();
                grid.RiverPathDebugSmoothed = new List<List<Vector2>>();
            }
            else
            {
                grid.RiverPathDebugMacro = null;
                grid.RiverPathDebugSmoothed = null;
            }

            // Ríos: meandro procedural; opcional evitar solapes; vados = River transitable + riverFord.
            int riverCount = Mathf.Min(config.riverCount, 8);
            var riverOccupiedCells = new HashSet<long>();
            int baseAttempts = Mathf.Clamp(config.riverPlacementMaxAttemptsPerRiver, 4, 96);
            // Más ríos pedidos ⇒ más intentos por río (el corredor se llena y hay más abortos por cruces).
            int maxAttemptsPerPass = Mathf.Clamp(baseAttempts + Mathf.Max(0, riverCount - 2) * 12, 4, 96);
            int earlyAbortBase = Mathf.Clamp(config.riverCorridorRejectEarlyAbort, 6, 40);
            int earlyAbortThreshold = Mathf.Clamp(earlyAbortBase + Mathf.Max(0, riverCount - 3) * 4, 6, 40);

            for (int i = 0; i < riverCount; i++)
            {
                bool placed = false;
                int attemptsUsed = 0;
                int buildFail = 0, corridorReject = 0;
                long buildTicks = 0, corridorTicks = 0, applyTicks = 0;
                bool usedRelaxedCrossing = false;

                // Pase 1: respetar riverAvoidCrossingOtherRivers. Pase 2 (solo si aplica): permitir cruces para cumplir riverCount del lobby.
                for (int pass = 0; pass < 2 && !placed; pass++)
                {
                    bool avoidCross = pass == 0 ? config.riverAvoidCrossingOtherRivers : false;
                    if (pass == 1 && !config.riverAvoidCrossingOtherRivers)
                        break;

                    int consecutiveCorridorReject = 0;
                    int attemptsThisPass = 0;

                    for (int attempt = 0; attempt < maxAttemptsPerPass && !placed; attempt++)
                    {
                        attemptsUsed++;
                        attemptsThisPass++;
                        Vector2Int start = PickRiverStart(w, h, rng);
                        Vector2Int exit = PickRiverExitOpposite(start, w, h, rng, attempt + pass * 997 + i * 13);

                        var swBuild = System.Diagnostics.Stopwatch.StartNew();
                        bool okBuild = RiverPathBuilder.TryBuildSmoothCenterlineAndRaster(w, h, start, exit, config, rng,
                            out List<Vector2> centerline, out List<Vector2Int> path, out List<Vector2Int> fordCells,
                            out List<Vector2> dbgMacro, out List<Vector2> dbgSmooth);
                        swBuild.Stop();
                        buildTicks += swBuild.ElapsedTicks;
                        if (!okBuild)
                        {
                            buildFail++;
                            consecutiveCorridorReject = 0;
                            continue;
                        }

                        var swCor = System.Diagnostics.Stopwatch.StartNew();
                        HashSet<long> corridor = CollectRiverCorridorPacked(path, config, w, h);
                        bool cross = avoidCross && CorridorIntersectsOccupied(corridor, riverOccupiedCells);
                        swCor.Stop();
                        corridorTicks += swCor.ElapsedTicks;

                        if (cross)
                        {
                            corridorReject++;
                            consecutiveCorridorReject++;
                            if (consecutiveCorridorReject >= earlyAbortThreshold)
                                break;
                            continue;
                        }

                        consecutiveCorridorReject = 0;

                        var swApply = System.Diagnostics.Stopwatch.StartNew();
                        if (centerline != null && centerline.Count >= 2)
                        {
                            grid.RiverCenterlinesCellSpace.Add(centerline);
                            float cs = grid.CellSizeWorld;
                            Vector3 o = grid.Origin;
                            var world = new List<Vector3>(centerline.Count);
                            foreach (var p in centerline)
                                world.Add(new Vector3(o.x + p.x * cs, o.y, o.z + p.y * cs));
                            grid.RiverCenterlinesWorld.Add(world);
                        }

                        if (config.debugDrawRiverPathInScene && dbgMacro != null && dbgSmooth != null)
                        {
                            grid.RiverPathDebugMacro.Add(dbgMacro);
                            grid.RiverPathDebugSmoothed.Add(dbgSmooth);
                        }

                        var fordPacked = new HashSet<long>();
                        if (fordCells != null)
                        {
                            foreach (var fc in fordCells)
                                fordPacked.Add(PackCellLong(fc));
                        }

                        foreach (var c in path)
                        {
                            if (!grid.InBoundsCell(c.x, c.y)) continue;
                            ref var cell = ref grid.GetCell(c);
                            if (cell.type != CellType.Land) continue;
                            bool isFord = fordPacked.Contains(PackCellLong(c));
                            cell.type = CellType.River;
                            cell.riverFord = isFord;
                            cell.walkable = isFord;
                            cell.buildable = false;
                            cell.waterTraverse = isFord ? WaterTraverseMode.FordShallow : WaterTraverseMode.SwimNavigable;
                            waterCells++;
                        }
                        waterCells += ExpandRiverWidthAroundPath(grid, path, config, i);

                        if (fordCells != null && fordCells.Count > 0 && config.riverFordCorridorRadiusCells > 0)
                            ApplyRiverFordCorridor(grid, fordCells, config.riverFordCorridorRadiusCells);

                        foreach (long k in corridor)
                            riverOccupiedCells.Add(k);
                        swApply.Stop();
                        applyTicks += swApply.ElapsedTicks;
                        placed = true;
                        usedRelaxedCrossing = pass == 1;
                    }

                    if (!placed && attemptsThisPass < maxAttemptsPerPass && corridorReject > 0 && avoidCross && config.debugLogs)
                        UnityEngine.Debug.Log($"Fase3 Agua: río {i + 1}/{riverCount} pase estricto: aborto anticipado tras {attemptsThisPass} intentos (rechazos consecutivos ≥{earlyAbortThreshold}).");
                }

                if (placed)
                {
                    if (config.debugLogs && usedRelaxedCrossing)
                        UnityEngine.Debug.Log($"Fase3 Agua: río {i + 1}/{riverCount} colocado permitiendo cruce con ríos ya existentes (fallback lobby).");
                    if (config.debugLogs && config.riverLogSuccessfulPlacementMetrics)
                    {
                        double msBuild = buildTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        double msCor = corridorTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        double msApply = applyTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        UnityEngine.Debug.Log($"Fase3 Agua: río {i + 1}/{riverCount} OK | intentos={attemptsUsed} buildFail={buildFail} corridorReject={corridorReject} | ms: build≈{msBuild:F1} corridor≈{msCor:F1} apply≈{msApply:F1}");
                    }
                }
                else if (config.riverLogPlacementFailureSummary || config.debugLogs)
                {
                    double msBuild = buildTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    double msCor = corridorTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    double msApply = applyTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    double msPerAttempt = attemptsUsed > 0 ? (buildTicks + corridorTicks + applyTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency / attemptsUsed : 0.0;
                    UnityEngine.Debug.LogWarning($"Fase3 Agua: río {i + 1}/{riverCount} no colocado | intentos={attemptsUsed} (hasta {maxAttemptsPerPass} por pase) buildFail={buildFail} corridorReject={corridorReject} evitarCrucesCfg={config.riverAvoidCrossingOtherRivers} | ms tot≈{msBuild + msCor + msApply:F0} (build≈{msBuild:F0} corredor≈{msCor:F0} aplicar≈{msApply:F0}) | ms/intento≈{msPerAttempt:F2}");
                }
            }

            // Lagos: flood fill (BFS) desde semilla en Land, hasta maxLakeCells por lago
            int lakeCount = Mathf.Min(config.lakeCount, 12);
            int maxLake = Mathf.Clamp(config.maxLakeCells, 50, 2500);
            grid.LakeBodyCellsPacked = new HashSet<long>();
            for (int i = 0; i < lakeCount; i++)
            {
                int attempts = 20;
                while (attempts-- > 0)
                {
                    int cx = rng.NextInt(w / 6, (5 * w) / 6);
                    int cz = rng.NextInt(h / 6, (5 * h) / 6);
                    ref var seedCell = ref grid.GetCell(cx, cz);
                    if (seedCell.type != CellType.Land) continue;

                    int added = FloodFillLake(grid, new Vector2Int(cx, cz), maxLake, config);
                    waterCells += added;
                    break;
                }
            }

            waterCells += AbsorbRiverMouthIntoLake(grid, config);

            if (config.mergeRiverCellsTouchingLake)
                MergeRiverCellsTouchingLake(grid);

            int deepRing = Mathf.Clamp(config.lakeDeepImpassableMinDistanceFromShore, 0, 64);
            if (deepRing > 0)
                ApplyLakeDeepImpassableCore(grid, deepRing);

            int total = w * h;
            float pct = total > 0 ? (waterCells * 100f / total) : 0f;
            int placedRiverCount = grid.RiverCenterlinesCellSpace != null ? grid.RiverCenterlinesCellSpace.Count : 0;
            if (config.debugLogs)
            {
                Debug.Log($"Fase3 Agua: {waterCells} celdas ({pct:F1}%), ríos colocados={placedRiverCount}/{riverCount} (centerline procedural→borde opuesto), lagos={lakeCount} (flood fill).");
                if (placedRiverCount < riverCount)
                {
                    Debug.LogWarning(
                        $"Fase3 Agua: faltan {riverCount - placedRiverCount} río(s). Suele deberse a " +
                        $"'{nameof(config.riverAvoidCrossingOtherRivers)}' y poco espacio de corredor. " +
                        $"Aumenta {nameof(config.riverPlacementMaxAttemptsPerRiver)}, " +
                        $"sube {nameof(config.riverCorridorRejectEarlyAbort)} o desactiva evitar cruces en el MapGenConfig (perfil técnico).");
                }
            }
        }

        /// <summary>Una sola pasada: River con vecino Water (8-dir) pasa a Water para confluencias coherentes.</summary>
        static void MergeRiverCellsTouchingLake(GridSystem grid)
        {
            int gw = grid.Width;
            int gh = grid.Height;
            var toConvert = new List<Vector2Int>(64);
            for (int z = 0; z < gh; z++)
            {
                for (int x = 0; x < gw; x++)
                {
                    ref var cell = ref grid.GetCell(x, z);
                    if (cell.type != CellType.River)
                        continue;
                    if (!CellTouchesWater8(grid, x, z))
                        continue;
                    toConvert.Add(new Vector2Int(x, z));
                }
            }

            foreach (var c in toConvert)
            {
                ref var cell = ref grid.GetCell(c.x, c.y);
                cell.type = CellType.Water;
                cell.riverFord = false;
                cell.walkable = false;
                cell.buildable = false;
                if (cell.waterTraverse == WaterTraverseMode.FordShallow)
                    cell.waterTraverse = WaterTraverseMode.SwimNavigable;
                else if (cell.waterTraverse == WaterTraverseMode.NotWater)
                    cell.waterTraverse = WaterTraverseMode.SwimNavigable;
            }
        }

        static bool CellTouchesWater8(GridSystem grid, int x, int z)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    int nx = x + dx;
                    int nz = z + dz;
                    if (!grid.InBoundsCell(nx, nz))
                        continue;
                    if (grid.GetCell(nx, nz).type == CellType.Water)
                        return true;
                }
            }
            return false;
        }

        /// <summary>Marca como <see cref="WaterTraverseMode.Impassable"/> el agua muy interior lejos de la orilla (solo celdas Water).</summary>
        static void ApplyLakeDeepImpassableCore(GridSystem grid, int minDistFromShoreCells)
        {
            if (minDistFromShoreCells <= 0)
                return;
            int gw = grid.Width;
            int gh = grid.Height;
            var dist = new int[gw, gh];
            for (int z = 0; z < gh; z++)
                for (int x = 0; x < gw; x++)
                    dist[x, z] = -1;

            var qx = new Queue<int>();
            var qz = new Queue<int>();
            for (int z = 0; z < gh; z++)
            {
                for (int x = 0; x < gw; x++)
                {
                    if (grid.GetCell(x, z).type != CellType.Water)
                        continue;
                    if (!WaterCellIsShore4(grid, x, z))
                        continue;
                    dist[x, z] = 0;
                    qx.Enqueue(x);
                    qz.Enqueue(z);
                }
            }

            while (qx.Count > 0)
            {
                int x = qx.Dequeue();
                int z = qz.Dequeue();
                int d = dist[x, z];
                void Try(int nx, int nz)
                {
                    if (!grid.InBoundsCell(nx, nz))
                        return;
                    if (grid.GetCell(nx, nz).type != CellType.Water)
                        return;
                    if (dist[nx, nz] != -1)
                        return;
                    dist[nx, nz] = d + 1;
                    qx.Enqueue(nx);
                    qz.Enqueue(nz);
                }
                Try(x - 1, z);
                Try(x + 1, z);
                Try(x, z - 1);
                Try(x, z + 1);
            }

            for (int z = 0; z < gh; z++)
            {
                for (int x = 0; x < gw; x++)
                {
                    if (grid.GetCell(x, z).type != CellType.Water)
                        continue;
                    int d = dist[x, z];
                    if (d < 0)
                        continue;
                    if (d >= minDistFromShoreCells)
                    {
                        ref var cell = ref grid.GetCell(x, z);
                        cell.waterTraverse = WaterTraverseMode.Impassable;
                        cell.walkable = false;
                    }
                }
            }
        }

        static bool WaterCellIsShore4(GridSystem grid, int x, int z)
        {
            if (grid.GetCell(x, z).type != CellType.Water)
                return false;
            foreach (var n in grid.Neighbors4(x, z))
            {
                if (grid.GetCell(n.x, n.y).type == CellType.Land)
                    return true;
            }
            return false;
        }

        /// <summary>Elige una celda en el borde del mapa (arriba/abajo/izq/der).</summary>
        private static Vector2Int PickRiverStart(int w, int h, IRng rng)
        {
            int side = rng.NextInt(0, 4);
            switch (side)
            {
                case 0: return new Vector2Int(rng.NextInt(0, w), 0);           // abajo
                case 1: return new Vector2Int(rng.NextInt(0, w), h - 1);      // arriba
                case 2: return new Vector2Int(0, rng.NextInt(0, h));           // izq
                default: return new Vector2Int(w - 1, rng.NextInt(0, h));      // der
            }
        }

        /// <summary>Destino del río en el borde opuesto al de inicio (cruce del mapa, sin sesgo al centro).</summary>
        private static long PackCellLong(Vector2Int c) => ((long)c.x << 32) | (uint)c.y;

        /// <summary>Radio máximo del corredor del río (base + variación) para evitar cruces inválidos al colocar otro río.</summary>
        private static int RiverCorridorMaxRadiusCells(MapGenConfig config)
        {
            if (config == null) return 1;
            int b = Mathf.Clamp(config.riverWidthRadiusCells, 0, 6);
            int a = Mathf.Clamp(config.riverWidthNoiseAmplitudeCells, 0, 3);
            return Mathf.Clamp(b + a, 0, 6);
        }

        private static HashSet<long> CollectRiverCorridorPacked(List<Vector2Int> axis, MapGenConfig config, int gw, int gh)
        {
            var mask = new HashSet<long>();
            if (axis == null) return mask;
            int radiusCells = RiverCorridorMaxRadiusCells(config);
            var centers = new HashSet<Vector2Int>();
            foreach (var c in axis)
                centers.Add(c);
            int rSq = radiusCells * radiusCells;
            foreach (var c in centers)
            {
                mask.Add(PackCellLong(c));
                if (radiusCells <= 0) continue;
                for (int dz = -radiusCells; dz <= radiusCells; dz++)
                {
                    for (int dx = -radiusCells; dx <= radiusCells; dx++)
                    {
                        if (config.riverExpandEuclidean)
                        {
                            if (dx * dx + dz * dz > rSq) continue;
                        }
                        else if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > radiusCells)
                            continue;
                        int x = c.x + dx;
                        int z = c.y + dz;
                        if ((uint)x >= (uint)gw || (uint)z >= (uint)gh) continue;
                        mask.Add(PackCellLong(new Vector2Int(x, z)));
                    }
                }
            }
            return mask;
        }

        /// <summary>
        /// Ensancha el vado al ancho del cauce: celdas River vecinas (Chebyshev) pasan a transitable y riverFord.
        /// Debe ejecutarse después de <see cref="ExpandRiverWidthAroundPath"/>.
        /// </summary>
        private static void ApplyRiverFordCorridor(GridSystem grid, List<Vector2Int> fordSeeds, int radiusChebyshev)
        {
            int r = Mathf.Clamp(radiusChebyshev, 0, 3);
            if (r <= 0 || fordSeeds == null) return;
            int gw = grid.Width;
            int gh = grid.Height;
            foreach (var seed in fordSeeds)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > r)
                            continue;
                        int x = seed.x + dx;
                        int z = seed.y + dz;
                        if ((uint)x >= (uint)gw || (uint)z >= (uint)gh)
                            continue;
                        ref var cell = ref grid.GetCell(x, z);
                        if (cell.type != CellType.River)
                            continue;
                        cell.riverFord = true;
                        cell.walkable = true;
                        cell.buildable = false;
                        cell.waterTraverse = WaterTraverseMode.FordShallow;
                    }
                }
            }
        }

        private static bool CorridorIntersectsOccupied(HashSet<long> corridor, HashSet<long> occupied)
        {
            if (corridor == null || occupied == null || occupied.Count == 0) return false;
            foreach (long k in corridor)
            {
                if (occupied.Contains(k))
                    return true;
            }
            return false;
        }

        /// <summary>Destino del río en el borde opuesto. <paramref name="attempt"/> desplaza el margen para variar rutas entre reintentos.</summary>
        private static Vector2Int PickRiverExitOpposite(Vector2Int start, int w, int h, IRng rng, int attempt = 0)
        {
            int margin = Mathf.Clamp(Mathf.Min(w, h) / 10 + (attempt % 4), 1, 16);
            if (w <= 2 || h <= 2) margin = 0;
            int xMin = Mathf.Clamp(margin, 0, w - 1);
            int xMax = Mathf.Clamp(w - 1 - margin, 0, w - 1);
            if (xMax < xMin) { xMin = 0; xMax = Mathf.Max(0, w - 1); }
            int yMin = Mathf.Clamp(margin, 0, h - 1);
            int yMax = Mathf.Clamp(h - 1 - margin, 0, h - 1);
            if (yMax < yMin) { yMin = 0; yMax = Mathf.Max(0, h - 1); }

            if (start.y == 0) return new Vector2Int(rng.NextInt(xMin, xMax + 1), h - 1);
            if (start.y == h - 1) return new Vector2Int(rng.NextInt(xMin, xMax + 1), 0);
            if (start.x == 0) return new Vector2Int(w - 1, rng.NextInt(yMin, yMax + 1));
            return new Vector2Int(0, rng.NextInt(yMin, yMax + 1));
        }

        /// <summary>
        /// Ensancha el río en el grid tras el eje: disco euclídeo o cuadrado Chebyshev según config (menos “manhattan” visual con euclídeo).
        /// </summary>
        private static int ExpandRiverWidthAroundPath(GridSystem grid, List<Vector2Int> path, MapGenConfig config, int riverIndex)
        {
            if (path == null || path.Count == 0 || config == null) return 0;
            int baseR = Mathf.Clamp(config.riverWidthRadiusCells, 0, 6);
            int amp = Mathf.Clamp(config.riverWidthNoiseAmplitudeCells, 0, 3);
            if (baseR <= 0 && amp <= 0) return 0;

            int seedMix = config.seed ^ (riverIndex * 739391);
            int added = 0;
            for (int pi = 0; pi < path.Count; pi++)
            {
                Vector2Int c = path[pi];
                int delta = 0;
                if (amp > 0)
                {
                    uint h = (uint)((pi * 374761393) ^ (riverIndex * 668265263) ^ (seedMix * 1442695041));
                    int span = amp * 2 + 1;
                    delta = (int)(h % (uint)span) - amp;
                }
                int r = Mathf.Clamp(baseR + delta, 0, 6);
                if (r <= 0) continue;

                int rSq = r * r;
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (config.riverExpandEuclidean)
                        {
                            if (dx * dx + dz * dz > rSq) continue;
                        }
                        else if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > r)
                            continue;
                        int x = c.x + dx;
                        int z = c.y + dz;
                        if (!grid.InBoundsCell(x, z)) continue;
                        ref var cell = ref grid.GetCell(x, z);
                        if (cell.type != CellType.Land) continue;
                        cell.type = CellType.River;
                        cell.riverFord = false;
                        cell.walkable = false;
                        cell.buildable = false;
                        cell.waterTraverse = WaterTraverseMode.SwimNavigable;
                        added++;
                    }
                }
            }
            return added;
        }

        /// <summary>
        /// BFS desde el borde del lago hacia celdas River (sin vados): ensancha la confluencia de forma orgánica.
        /// </summary>
        private static int AbsorbRiverMouthIntoLake(GridSystem grid, MapGenConfig config)
        {
            int depth = config != null ? Mathf.Clamp(config.lakeRiverMouthBlendCells, 0, 8) : 0;
            if (depth <= 0 || grid.LakeBodyCellsPacked == null || grid.LakeBodyCellsPacked.Count == 0)
                return 0;

            var q = new Queue<(int x, int z, int dist)>();
            var seen = new HashSet<long>();

            foreach (long pk in grid.LakeBodyCellsPacked)
            {
                int lx = (int)(pk >> 32);
                int lz = (int)(uint)pk;
                if (!grid.InBoundsCell(lx, lz)) continue;
                foreach (var n in grid.Neighbors8(lx, lz))
                {
                    ref var nc = ref grid.GetCell(n.x, n.y);
                    if (nc.type != CellType.River || nc.riverFord)
                        continue;
                    long nk = PackCellLong(n);
                    if (seen.Add(nk))
                        q.Enqueue((n.x, n.y, 1));
                }
            }

            int added = 0;
            while (q.Count > 0)
            {
                var (x, z, d) = q.Dequeue();
                if (d > depth)
                    continue;

                ref var cell = ref grid.GetCell(x, z);
                if (cell.type != CellType.River || cell.riverFord)
                    continue;

                cell.type = CellType.Water;
                cell.riverFord = false;
                cell.walkable = false;
                cell.buildable = false;
                if (cell.waterTraverse == WaterTraverseMode.FordShallow || cell.waterTraverse == WaterTraverseMode.NotWater)
                    cell.waterTraverse = WaterTraverseMode.SwimNavigable;
                grid.LakeBodyCellsPacked.Add(PackCellLong(new Vector2Int(x, z)));
                added++;

                if (d >= depth)
                    continue;

                foreach (var n in grid.Neighbors8(x, z))
                {
                    ref var nb = ref grid.GetCell(n.x, n.y);
                    if (nb.type != CellType.River || nb.riverFord)
                        continue;
                    long nk = PackCellLong(n);
                    if (seen.Add(nk))
                        q.Enqueue((n.x, n.y, d + 1));
                }
            }

            return added;
        }

        /// <summary>Hash determinista [0,1) para expansiones de lago (reproducible con seed del mapa).</summary>
        private static float LakeExpandHash01(int x, int z, int salt)
        {
            unchecked
            {
                int h = x * 374761393 + z * 668265263 + salt * 1442695041;
                h ^= h >> 13;
                h *= 1274126177;
                uint u = (uint)h;
                return (u & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>
        /// Flood fill desde seed; solo Land; máximo maxCells. 8 direcciones + irregularidad y semillas extra.
        /// </summary>
        private static int FloodFillLake(GridSystem grid, Vector2Int seed, int maxCells, MapGenConfig config)
        {
            float ir = config != null ? Mathf.Clamp01(config.lakeOrganicIrregularity) : 0f;
            int spread = config != null ? Mathf.Clamp(config.lakeExtraSeedSpreadCells, 0, 10) : 0;
            int salt = config != null ? config.seed : 0;

            var queue = new Queue<Vector2Int>();
            var visited = new HashSet<Vector2Int>();

            void TryEnqueueSeed(Vector2Int p)
            {
                if (!grid.InBoundsCell(p.x, p.y) || visited.Contains(p)) return;
                if (grid.GetCell(p.x, p.y).type != CellType.Land) return;
                visited.Add(p);
                queue.Enqueue(p);
            }

            TryEnqueueSeed(seed);

            if (spread > 0 && config != null)
            {
                for (int dz = -spread; dz <= spread; dz++)
                {
                    for (int dx = -spread; dx <= spread; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        var p = new Vector2Int(seed.x + dx, seed.y + dz);
                        if (!grid.InBoundsCell(p.x, p.y)) continue;
                        if (grid.GetCell(p.x, p.y).type != CellType.Land) continue;
                        float h = LakeExpandHash01(p.x, p.y, salt + 9029);
                        float thresh = Mathf.Lerp(0.9f, 0.26f, ir);
                        if (h < thresh) continue;
                        TryEnqueueSeed(p);
                    }
                }
            }

            int count = 0;
            System.Random localRng = new System.Random(seed.x * 1000 + seed.y + salt * 17);

            while (queue.Count > 0 && count < maxCells)
            {
                var c = queue.Dequeue();
                ref var cell = ref grid.GetCell(c);
                if (cell.type != CellType.Land) continue;

                cell.type = CellType.Water;
                cell.walkable = false;
                cell.buildable = false;
                cell.riverFord = false;
                cell.waterTraverse = WaterTraverseMode.SwimNavigable;
                count++;
                if (grid.LakeBodyCellsPacked != null)
                    grid.LakeBodyCellsPacked.Add(PackCellLong(c));

                var directions = new List<Vector2Int>(AllDirections);
                for (int i = directions.Count - 1; i > 0; i--)
                {
                    int j = localRng.Next(i + 1);
                    (directions[i], directions[j]) = (directions[j], directions[i]);
                }

                foreach (var dir in directions)
                {
                    var n = new Vector2Int(c.x + dir.x, c.y + dir.y);
                    if (!grid.InBoundsCell(n.x, n.y) || visited.Contains(n)) continue;
                    ref var ncell = ref grid.GetCell(n);
                    if (ncell.type != CellType.Land) continue;

                    bool isDiagonal = Mathf.Abs(dir.x) == 1 && Mathf.Abs(dir.y) == 1;
                    float hN = LakeExpandHash01(n.x, n.y, salt + 4049);
                    float hD = LakeExpandHash01(n.x, n.y, salt + 8081);
                    float cardBase = Mathf.Lerp(0.92f, 0.52f + 0.44f * hN, ir);
                    float diagMul = Mathf.Lerp(0.82f, 0.45f + 0.48f * hD, ir);
                    float expandChance = isDiagonal ? cardBase * diagMul : cardBase;

                    float roll = LakeExpandHash01(n.x, n.y, salt + dir.x * 131 + dir.y * 171 + count * 19);
                    if (roll < expandChance)
                    {
                        visited.Add(n);
                        queue.Enqueue(n);
                    }
                }
            }
            return count;
        }
    }
}
