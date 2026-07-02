using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Project.Gameplay.Map.Generator
{
    internal static class WaterSubsurfaceBedBuilder
    {
        const string ShaderName = "Project/WOR Submerged Bed URP";

        public static void Build(
            Transform parent,
            GridSystem grid,
            MapGenConfig config,
            float lakeSurfaceY,
            float riverSurfaceY,
            float cellSize,
            int waterLayer)
        {
            if (parent == null || grid == null || config == null || !config.underwaterBedEnabled)
                return;

            if (grid.WaterDepth01 == null || grid.WaterShoreDistanceCells == null)
                WaterSurfaceFieldBuilder.Build(grid, config);

            Material lakeMat = CreateMaterial(config, true);
            Material riverMat = CreateMaterial(config, false);
            float yOff = Mathf.Clamp(config.underwaterBedYOffsetWorld, 0.02f, 0.8f);
            bool directAssetLake = config.lakeWaterMaterialMode == WaterMaterialRuntimeMode.DirectAsset;
            bool riversBySurfaceStrip = config.riverVisualUseContinuousMesh &&
                config.riverVisualUseRiverSurfaceMeshStrip &&
                !config.riverVisualRenderRiverAsMarchingSquaresCells;
            int lakeCells = directAssetLake
                ? 0
                : BuildCellMesh(parent, grid, config, CellType.Water, lakeMat, lakeSurfaceY - yOff, cellSize, waterLayer, "Water_SubmergedBed_Lakes");
            int riverCells = riversBySurfaceStrip
                ? 0
                : BuildCellMesh(parent, grid, config, CellType.River, riverMat, riverSurfaceY - yOff, cellSize, waterLayer, "Water_SubmergedBed_Rivers");

            if (config.debugLogs || config.debugHydrologyNetwork)
            {
                Debug.Log(
                    $"[WaterSubsurfaceBed] enabled=1 lakeCells={lakeCells} riverCells={riverCells} " +
                    $"riverBedSkippedSurfaceStrip={(riversBySurfaceStrip ? 1 : 0)} " +
                    $"shader={(lakeMat != null && lakeMat.shader != null ? lakeMat.shader.name : "null")} yOffset={yOff:F2} " +
                    $"lakeDirectAsset={(directAssetLake ? 1 : 0)}");
            }
        }

        static Material CreateMaterial(MapGenConfig config, bool lake)
        {
            Material mat = config.underwaterBedMaterial != null
                ? new Material(config.underwaterBedMaterial)
                : CreateDefaultMaterial();

            if (mat == null)
                return null;

            mat.name = lake ? "MAT_Runtime_LakeBed" : "MAT_Runtime_RiverBed";
            if (mat.HasProperty("_DeepColor"))
                mat.SetColor("_DeepColor", lake ? config.lakeBedDeepColor : config.riverBedDeepColor);
            if (mat.HasProperty("_ShallowColor"))
                mat.SetColor("_ShallowColor", lake ? config.lakeBedShallowColor : config.riverBedShallowColor);
            if (mat.HasProperty("_NoiseScale"))
                mat.SetFloat("_NoiseScale", Mathf.Max(0.001f, config.underwaterBedNoiseScale));
            if (mat.HasProperty("_NoiseStrength"))
                mat.SetFloat("_NoiseStrength", Mathf.Clamp01(config.underwaterBedNoiseStrength));
            if (mat.HasProperty("_Alpha"))
                mat.SetFloat("_Alpha", 1f);
            mat.renderQueue = 2990;
            return mat;
        }

        static Material CreateDefaultMaterial()
        {
            Shader sh = Shader.Find(ShaderName);
            if (sh == null)
                sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null)
                sh = Shader.Find("Sprites/Default");
            return sh != null ? new Material(sh) : null;
        }

        static int BuildCellMesh(
            Transform parent,
            GridSystem grid,
            MapGenConfig config,
            CellType cellType,
            Material mat,
            float y,
            float cellSize,
            int waterLayer,
            string objectName)
        {
            if (mat == null)
                return 0;

            int w = grid.Width;
            int h = grid.Height;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var tris = new List<int>();
            float uvScale = Mathf.Max(0.001f, config.underwaterBedUvScale);
            int cells = 0;

            for (int gz = 0; gz < h; gz++)
            {
                for (int gx = 0; gx < w; gx++)
                {
                    if (grid.GetCell(gx, gz).type != cellType)
                        continue;

                    int i = verts.Count;
                    float x0 = grid.Origin.x + gx * cellSize;
                    float z0 = grid.Origin.z + gz * cellSize;
                    float x1 = x0 + cellSize;
                    float z1 = z0 + cellSize;
                    verts.Add(new Vector3(x0, y, z0));
                    verts.Add(new Vector3(x1, y, z0));
                    verts.Add(new Vector3(x1, y, z1));
                    verts.Add(new Vector3(x0, y, z1));
                    uvs.Add(new Vector2(x0 * uvScale, z0 * uvScale));
                    uvs.Add(new Vector2(x1 * uvScale, z0 * uvScale));
                    uvs.Add(new Vector2(x1 * uvScale, z1 * uvScale));
                    uvs.Add(new Vector2(x0 * uvScale, z1 * uvScale));

                    Color c = CellColor(grid, config, gx, gz, cellType);
                    colors.Add(c);
                    colors.Add(c);
                    colors.Add(c);
                    colors.Add(c);

                    tris.Add(i);
                    tris.Add(i + 2);
                    tris.Add(i + 1);
                    tris.Add(i);
                    tris.Add(i + 3);
                    tris.Add(i + 2);
                    cells++;
                }
            }

            if (cells == 0 || tris.Count == 0)
                return 0;

            var mesh = new Mesh { name = objectName };
            if (verts.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.layer = waterLayer;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.renderingLayerMask = 1u;
            return cells;
        }

        static Color CellColor(GridSystem grid, MapGenConfig config, int gx, int gz, CellType cellType)
        {
            float depth = 0.45f;
            if (grid.WaterDepth01 != null)
                depth = Mathf.Clamp01(grid.WaterDepth01[gx, gz]);
            if (cellType == CellType.River)
                depth = Mathf.Clamp01(Mathf.Lerp(0.35f, 0.82f, depth));

            float shore = 0.35f;
            if (grid.WaterShoreDistanceCells != null)
            {
                float visualWidth = cellType == CellType.River
                    ? Mathf.Max(0.5f, config.riverShoreVisualWidth)
                    : Mathf.Max(1f, config.lakeShoreVisualWidth);
                shore = Mathf.Clamp01(1f - grid.WaterShoreDistanceCells[gx, gz] / visualWidth);
            }

            float alpha = cellType == CellType.River
                ? Mathf.Lerp(config.riverBedShallowColor.a, config.riverBedDeepColor.a, depth)
                : Mathf.Lerp(config.lakeBedShallowColor.a, config.lakeBedDeepColor.a, depth);
            float interior = Mathf.Clamp01(1f - shore);
            float edgeFade = cellType == CellType.Water
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.92f, interior))
                : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.78f, interior));
            alpha *= edgeFade;

            return new Color(shore, depth, 0f, alpha);
        }
    }
}
