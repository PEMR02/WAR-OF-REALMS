using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Valida el mapa generado: ciudades conectadas por caminos, no ciudad en agua, % agua.</summary>
    public static class MapValidator
    {
        /// <summary>Retorna true si el mapa pasa todas las comprobaciones. reason indica el primer fallo.</summary>
        public static bool Validate(GridSystem grid, List<CityNode> cities, MapGenConfig config, out string reason)
        {
            reason = null;
            if (grid == null) { reason = "Grid null"; return false; }
            if (config == null) { reason = "Config null"; return false; }

            int w = grid.Width;
            int h = grid.Height;
            int total = w * h;
            int waterCells = 0;
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    if (grid.GetCell(x, z).type == CellType.Water || grid.GetCell(x, z).type == CellType.River)
                        waterCells++;
            float waterPct = total > 0 ? (waterCells * 100f / total) : 0f;
            if (waterPct > 60f) { reason = $"Demasiada agua ({waterPct:F0}%)"; return false; }

            if (cities != null)
            {
                foreach (var city in cities)
                {
                    ref var center = ref grid.GetCell(city.Center.x, city.Center.y);
                    if (center.type == CellType.Water || center.type == CellType.River)
                    {
                        reason = $"Ciudad {city.Id} en agua";
                        return false;
                    }
                }
                if (cities.Count < 1) { reason = "Sin ciudades"; return false; }
            }

            reason = "OK";
            return true;
        }

        /// <summary>Valida incluyendo conectividad por caminos. Usar esta si ya tienes la lista de roads.</summary>
        public static bool Validate(GridSystem grid, List<CityNode> cities, List<Road> roads, MapGenConfig config, out string reason)
        {
            if (!Validate(grid, cities, config, out reason)) return false;
            if (cities == null || cities.Count <= 1 || roads == null) return true;

            int componentCount = CountConnectedComponents(cities, roads);
            if (componentCount > 1)
            {
                reason = $"Ciudades no conectadas (componentes={componentCount})";
                return false;
            }
            reason = "OK";
            return true;
        }

        /// <summary>Cuenta componentes conexas del grafo ciudades-aristas (cada road es una arista).</summary>
        private static int CountConnectedComponents(List<CityNode> cities, List<Road> roads)
        {
            if (cities == null || cities.Count == 0) return 0;
            var adj = new Dictionary<int, List<int>>();
            foreach (var c in cities) adj[c.Id] = new List<int>();
            foreach (var r in roads ?? new List<Road>())
            {
                if (adj.ContainsKey(r.FromCityId) && adj.ContainsKey(r.ToCityId))
                {
                    adj[r.FromCityId].Add(r.ToCityId);
                    adj[r.ToCityId].Add(r.FromCityId);
                }
            }
            var visited = new HashSet<int>();
            int components = 0;
            foreach (var city in cities)
            {
                if (visited.Contains(city.Id)) continue;
                components++;
                var queue = new Queue<int>();
                queue.Enqueue(city.Id);
                visited.Add(city.Id);
                while (queue.Count > 0)
                {
                    int id = queue.Dequeue();
                    foreach (int n in adj[id])
                        if (!visited.Contains(n)) { visited.Add(n); queue.Enqueue(n); }
                }
            }
            return components;
        }
    }
}
