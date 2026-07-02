using System.Collections.Generic;
using Project.Gameplay.Map.Generation.Alpha;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Modo de coloreado de la vista 2D del lobby.</summary>
    public enum MapPreviewOverlayMode
    {
        Terrain = 0,
        Regions = 1,
        Resources = 2
    }

    /// <summary>Vista 2D del <see cref="GridSystem"/> para lobby / herramientas (sin terreno ni mallas).</summary>
    public static class MapPreviewTextureBuilder
    {
        static Color ColorTerrain(in CellData cell)
        {
            switch (cell.type)
            {
                case CellType.Water:
                    return new Color(0.12f, 0.35f, 0.75f);
                case CellType.River:
                    return new Color(0.25f, 0.55f, 0.92f);
                case CellType.Mountain:
                {
                    float v = 0.35f + cell.height01 * 0.45f;
                    return new Color(v * 0.55f, v * 0.5f, v * 0.48f);
                }
                default:
                {
                    float g = 0.22f + cell.height01 * 0.55f;
                    return new Color(g * 0.45f, g * 0.75f, g * 0.38f);
                }
            }
        }

        static Color ColorSemantic(TerrainRegionType t, in CellData cell)
        {
            switch (t)
            {
                case TerrainRegionType.Mountain:
                case TerrainRegionType.RockyZone:
                    return new Color(0.58f, 0.54f, 0.52f);
                case TerrainRegionType.Hill:
                    return new Color(0.85f, 0.62f, 0.18f);
                case TerrainRegionType.RiverBank:
                    return new Color(0.35f, 0.68f, 0.95f);
                case TerrainRegionType.LakeShore:
                case TerrainRegionType.WetZone:
                    return new Color(0.28f, 0.82f, 0.9f);
                case TerrainRegionType.ForestCandidate:
                    return new Color(0.08f, 0.45f, 0.3f);
                case TerrainRegionType.Plain:
                case TerrainRegionType.SpawnFriendly:
                case TerrainRegionType.Basin:
                    return new Color(0.52f, 0.82f, 0.32f);
                default:
                    return ColorTerrain(in cell);
            }
        }

        static Color ColorRegionsHeuristic(GridSystem grid, int gx, int gz, in CellData cell)
        {
            switch (cell.type)
            {
                case CellType.Water:
                    return new Color(0.18f, 0.72f, 0.88f);
                case CellType.River:
                    return new Color(0.28f, 0.58f, 0.94f);
                case CellType.Mountain:
                    return new Color(0.58f, 0.54f, 0.52f);
            }

            if (grid != null && grid.DistanceToWaterCells != null
                && gx >= 0 && gx < grid.Width && gz >= 0 && gz < grid.Height)
            {
                int d = grid.DistanceToWaterCells[gx, gz];
                if (d >= 0 && d <= 2)
                    return Color.Lerp(new Color(0.42f, 0.7f, 0.95f), new Color(0.52f, 0.82f, 0.32f), d / 3f);
            }

            if (cell.slopeDeg > 12f)
                return new Color(0.85f, 0.62f, 0.18f);
            if (cell.resourceType == ResourceType.Wood)
                return new Color(0.06f, 0.42f, 0.28f);
            return new Color(0.52f, 0.82f, 0.32f);
        }

        static Color ColorResources(in CellData cell)
        {
            switch (cell.type)
            {
                case CellType.Water:
                    return new Color(0.12f, 0.35f, 0.75f);
                case CellType.River:
                    return new Color(0.25f, 0.55f, 0.92f);
                case CellType.Mountain:
                    return new Color(0.48f, 0.48f, 0.5f);
            }

            switch (cell.resourceType)
            {
                case ResourceType.Wood:
                    return new Color(0.05f, 0.48f, 0.3f);
                case ResourceType.Stone:
                    return new Color(0.55f, 0.55f, 0.58f);
                case ResourceType.Gold:
                    return new Color(0.92f, 0.78f, 0.18f);
                case ResourceType.Food:
                    return new Color(0.92f, 0.42f, 0.22f);
                default:
                {
                    float g = 0.18f + cell.height01 * 0.4f;
                    return new Color(g * 0.48f, g * 0.55f, g * 0.48f);
                }
            }
        }

        static Color ColorForMode(GridSystem grid, int gx, int gz, in CellData cell, SemanticRegionMap sem, MapPreviewOverlayMode mode)
        {
            switch (mode)
            {
                case MapPreviewOverlayMode.Resources:
                    return ColorResources(in cell);
                case MapPreviewOverlayMode.Regions:
                    return sem != null
                        ? ColorSemantic(sem.Get(gx, gz), in cell)
                        : ColorRegionsHeuristic(grid, gx, gz, in cell);
                default:
                    return ColorTerrain(in cell);
            }
        }

        /// <summary>Crea una textura RGB. El eje Y de imagen coincide con +Z del mapa (norte arriba).</summary>
        public static Texture2D Build(
            GridSystem grid,
            IReadOnlyList<CityNode> cities,
            int maxDimension = 384,
            SemanticRegionMap semanticRegionsOrNull = null,
            MapPreviewOverlayMode mode = MapPreviewOverlayMode.Terrain)
        {
            if (grid == null || grid.Width < 1 || grid.Height < 1)
                return null;

            maxDimension = Mathf.Clamp(maxDimension, 32, 1024);
            float aspect = grid.Width / (float)grid.Height;
            int w, h;
            if (aspect >= 1f)
            {
                w = maxDimension;
                h = Mathf.Max(1, Mathf.RoundToInt(maxDimension / aspect));
            }
            else
            {
                h = maxDimension;
                w = Mathf.Max(1, Mathf.RoundToInt(maxDimension * aspect));
            }

            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;

            for (int py = 0; py < h; py++)
            {
                float fz = (py + 0.5f) / h;
                int gz = Mathf.Clamp(Mathf.FloorToInt(fz * grid.Height), 0, grid.Height - 1);
                for (int px = 0; px < w; px++)
                {
                    float fx = (px + 0.5f) / w;
                    int gx = Mathf.Clamp(Mathf.FloorToInt(fx * grid.Width), 0, grid.Width - 1);
                    ref var cell = ref grid.GetCell(gx, gz);
                    tex.SetPixel(px, py, ColorForMode(grid, gx, gz, in cell, semanticRegionsOrNull, mode));
                }
            }

            if (cities != null)
            {
                foreach (var city in cities)
                {
                    if (city == null) continue;
                    int cx = city.Center.x;
                    int cz = city.Center.y;
                    float fx = (cx + 0.5f) / grid.Width;
                    float fz = (cz + 0.5f) / grid.Height;
                    int ipx = Mathf.Clamp(Mathf.FloorToInt(fx * w), 0, w - 1);
                    int ipy = Mathf.Clamp(Mathf.FloorToInt(fz * h), 0, h - 1);
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = Mathf.Clamp(ipx + dx, 0, w - 1);
                        int y = Mathf.Clamp(ipy + dy, 0, h - 1);
                        tex.SetPixel(x, y, new Color(0.95f, 0.28f, 0.38f));
                    }
                }
            }

            tex.Apply();
            return tex;
        }
    }
}
