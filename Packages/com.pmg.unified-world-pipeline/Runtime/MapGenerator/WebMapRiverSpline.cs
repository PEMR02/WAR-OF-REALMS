using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    [ExecuteAlways]
    public class WebMapRiverSpline : MonoBehaviour
    {
        public Vector3[] points = new Vector3[0];
        public float[] widths = new float[0];
        public float[] foam = new float[0];
        public float[] transparency = new float[0];
        public float[] distances = new float[0];
        public bool showCenterLine = true;
        public Color centerLineColor = new Color(1f, 0.95f, 0.05f, 1f);

        [SerializeField] MeshFilter surfaceMeshFilter;
        [SerializeField] MeshRenderer surfaceMeshRenderer;
        [SerializeField] MapGenConfig surfaceConfig;
        [SerializeField] int surfaceRiverIndex;
        [SerializeField] float surfaceUvScale = 1f;
        [SerializeField] float surfaceCellSizeWorld = 1f;
        [SerializeField] int surfaceGridWCells;
        [SerializeField] int surfaceGridHCells;
        [SerializeField] Vector3 surfaceMapOrigin;
        [SerializeField] Vector2[] surfaceCellSpaceLine = new Vector2[0];

        bool suppressSurfaceRebuild;

        public void BindSurfaceMesh(
            MeshFilter meshFilter,
            MeshRenderer meshRenderer,
            MapGenConfig config,
            int riverIndex,
            float uvScale,
            float cellSizeWorld,
            int gridWCells,
            int gridHCells,
            Vector3 mapOrigin,
            List<Vector2> cellSpaceLine)
        {
            surfaceMeshFilter = meshFilter;
            surfaceMeshRenderer = meshRenderer;
            surfaceConfig = config;
            surfaceRiverIndex = riverIndex;
            surfaceUvScale = uvScale;
            surfaceCellSizeWorld = cellSizeWorld;
            surfaceGridWCells = gridWCells;
            surfaceGridHCells = gridHCells;
            surfaceMapOrigin = mapOrigin;
            surfaceCellSpaceLine = cellSpaceLine != null ? cellSpaceLine.ToArray() : new Vector2[0];
        }

        public void SetData(
            Vector3[] centerPoints,
            float[] riverWidths,
            float[] riverFoam,
            float[] riverTransparency,
            float[] riverDistances,
            bool rebuildSurfaceMesh = true)
        {
            suppressSurfaceRebuild = true;
            points = centerPoints ?? new Vector3[0];
            widths = riverWidths ?? new float[0];
            foam = riverFoam ?? new float[0];
            transparency = riverTransparency ?? new float[0];
            distances = riverDistances ?? new float[0];
            suppressSurfaceRebuild = false;
            RefreshLineRenderer();
            if (rebuildSurfaceMesh)
                RebuildLinkedSurfaceMesh();
        }

        [ContextMenu("Rebuild Surface Mesh From Spline")]
        public void RebuildLinkedSurfaceMesh()
        {
            if (surfaceMeshFilter == null && transform.parent != null)
            {
                surfaceMeshFilter = transform.parent.GetComponent<MeshFilter>();
                surfaceMeshRenderer = transform.parent.GetComponent<MeshRenderer>();
            }

            MapGenConfig config = ResolveSurfaceConfig();
            GridSystem grid = ResolveLogicalGrid(config, out float cellSizeWorld, out Vector3 mapOrigin);

            if (surfaceMeshFilter == null || config == null ||
                points == null || points.Length < 2)
                return;

            Transform meshRoot = surfaceMeshFilter.transform;
            if (!TryBuildAuthoritativeCenterlineFromSpline(
                    meshRoot, cellSizeWorld, mapOrigin, out List<Vector3> center,
                    out List<float> halfWidths, out List<Vector2> cellLine))
                return;

            surfaceCellSpaceLine = cellLine.ToArray();

            RiverSurfaceMeshBuilder.TryRebuildRiverSurfaceMeshFromCenterline(
                surfaceMeshFilter,
                surfaceMeshRenderer,
                center,
                halfWidths,
                cellLine,
                grid,
                config,
                surfaceRiverIndex,
                cellSizeWorld,
                surfaceUvScale,
                surfaceGridWCells,
                surfaceGridHCells,
                mapOrigin,
                splineControlsCenterY: true);
        }

        bool TryBuildAuthoritativeCenterlineFromSpline(
            Transform meshRoot,
            float cellSizeWorld,
            Vector3 mapOrigin,
            out List<Vector3> center,
            out List<float> halfWidths,
            out List<Vector2> cellLine)
        {
            center = new List<Vector3>(points.Length);
            halfWidths = new List<float>(points.Length);
            cellLine = new List<Vector2>(points.Length);
            if (meshRoot == null || points == null || points.Length < 2)
                return false;

            float invCell = 1f / Mathf.Max(0.01f, cellSizeWorld);
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 world = transform.TransformPoint(points[i]);
                center.Add(meshRoot.InverseTransformPoint(world));
                cellLine.Add(new Vector2(
                    (world.x - mapOrigin.x) * invCell,
                    (world.z - mapOrigin.z) * invCell));

                float fullW = 1f;
                if (widths != null && widths.Length > 0)
                {
                    int wi = Mathf.Clamp(i, 0, widths.Length - 1);
                    fullW = widths[wi];
                }

                halfWidths.Add(Mathf.Max(0.02f, fullW * 0.5f));
            }

            return center.Count >= 2;
        }

        MapGenConfig ResolveSurfaceConfig()
        {
            if (surfaceConfig != null)
                return surfaceConfig;

            var generator = Object.FindFirstObjectByType<MapGenerator>();
            return generator != null ? generator.config : null;
        }

        GridSystem ResolveLogicalGrid(MapGenConfig config, out float cellSizeWorld, out Vector3 mapOrigin)
        {
            cellSizeWorld = surfaceCellSizeWorld;
            mapOrigin = surfaceMapOrigin;
            GridSystem grid = null;

            var generator = Object.FindFirstObjectByType<MapGenerator>();
            if (generator != null)
                grid = generator.Grid;

            if (grid != null)
            {
                if (surfaceGridWCells <= 0)
                    surfaceGridWCells = grid.Width;
                if (surfaceGridHCells <= 0)
                    surfaceGridHCells = grid.Height;
                if (surfaceMapOrigin == Vector3.zero)
                    surfaceMapOrigin = grid.Origin;
                if (surfaceCellSizeWorld <= 0.01f)
                    surfaceCellSizeWorld = grid.CellSizeWorld;
            }

            if (cellSizeWorld <= 0.01f)
                cellSizeWorld = config != null && config.cellSizeWorld > 0.01f ? config.cellSizeWorld : 1f;
            if (mapOrigin == Vector3.zero && grid != null)
                mapOrigin = grid.Origin;

            return grid;
        }

        public void RefreshLineRenderer()
        {
            LineRenderer line = GetComponent<LineRenderer>();
            if (!showCenterLine || points == null || points.Length < 2)
            {
                if (line != null)
                    line.enabled = false;
                return;
            }

            if (line == null)
                line = gameObject.AddComponent<LineRenderer>();

            line.enabled = true;
            line.useWorldSpace = false;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.widthMultiplier = 0.18f;
            line.numCornerVertices = 6;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = RuntimeLineMaterial();
            line.startColor = centerLineColor;
            line.endColor = centerLineColor;
        }

        static Material RuntimeLineMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color");
            Material material = new Material(shader) { name = "Runtime River Centerline" };
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", new Color(1f, 0.95f, 0.05f, 1f));
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", new Color(1f, 0.95f, 0.05f, 1f));
            return material;
        }

        void OnValidate()
        {
            RefreshLineRenderer();
            if (suppressSurfaceRebuild)
                return;
            RebuildLinkedSurfaceMesh();
        }

        void OnDrawGizmosSelected()
        {
            if (points == null || points.Length < 2)
                return;

            Gizmos.color = centerLineColor;
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector3 a = transform.TransformPoint(points[i]);
                Vector3 b = transform.TransformPoint(points[i + 1]);
                Gizmos.DrawLine(a, b);
            }

            for (int i = 0; i < points.Length; i++)
            {
                float width = widths != null && i < widths.Length ? widths[i] : 1f;
                Vector3 p = transform.TransformPoint(points[i]);
                Gizmos.DrawSphere(p, Mathf.Max(0.18f, width * 0.08f));
            }
        }
    }
}
