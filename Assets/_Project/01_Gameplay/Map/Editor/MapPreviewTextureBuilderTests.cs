using NUnit.Framework;
using UnityEngine;
using Project.Gameplay.Map.Generator;

namespace Project.Gameplay.Map.Editor.Tests
{
    public class MapPreviewTextureBuilderTests
    {
        [Test]
        public void Build_WithWaterCells_ProducesNonEmptyTexture()
        {
            var grid = new GridSystem(8, 4, 1f, Vector3.zero);
            for (int x = 0; x < grid.Width; x++)
            {
                for (int z = 0; z < grid.Height; z++)
                {
                    ref var c = ref grid.GetCell(x, z);
                    c = CellData.Default();
                    c.type = (x + z) % 3 == 0 ? CellType.Water : CellType.Land;
                    c.height01 = (x + z) / (float)(grid.Width + grid.Height);
                }
            }

            var tex = MapPreviewTextureBuilder.Build(grid, null, maxDimension: 64);
            Assert.NotNull(tex);
            Assert.GreaterOrEqual(tex.width, 32);
            Assert.GreaterOrEqual(tex.height, 8);
            Object.DestroyImmediate(tex);
        }

        [Test]
        public void Build_WithCities_DrawsRedPixels()
        {
            var grid = new GridSystem(32, 32, 1f, Vector3.zero);
            for (int x = 0; x < grid.Width; x++)
                for (int z = 0; z < grid.Height; z++)
                {
                    ref var c = ref grid.GetCell(x, z);
                    c = CellData.Default();
                }

            var cities = new System.Collections.Generic.List<CityNode>
            {
                new CityNode { Center = new Vector2Int(16, 16), RadiusCells = 2 }
            };

            var tex = MapPreviewTextureBuilder.Build(grid, cities, maxDimension: 64);
            Assert.NotNull(tex);
            // Centro del mapa en ~1:1 → ciudad en celda (16,16) proyecta cerca de (33,33) en 64×64.
            var p = tex.GetPixel(33, 33);
            Assert.Greater(p.r, 0.7f);
            Object.DestroyImmediate(tex);
        }
    }
}
