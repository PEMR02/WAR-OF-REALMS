using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>Grid lógico: celdas (width x height) y nodos (width+1 x height+1). World &lt;-&gt; Cell &lt;-&gt; Node.</summary>
    public class GridSystem
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public float CellSizeWorld { get; private set; }
        public Vector3 Origin { get; private set; }

        /// <summary>
        /// Eje del río en espacio celda continuo (x=columna, y=fila): centerline suavizada y remuestreada,
        /// alineada con el raster de <see cref="CellType.River"/> (Fase3).
        /// </summary>
        public List<List<Vector2>> RiverCenterlinesCellSpace { get; set; }

        /// <summary>Ejes en mundo al momento de Fase3 (opcional). La malla ribbon deriva de <see cref="RiverCenterlinesCellSpace"/> al construir agua.</summary>
        public List<List<Vector3>> RiverCenterlinesWorld { get; set; }

        /// <summary>
        /// Corredor visual final del río (W×H): raster de la cinta surface mesh (centerline + halfWidth).
        /// Solo render MS / tallado de terreno; no modifica <see cref="CellData"/>.
        /// </summary>
        public bool[,] RiverVisualSurfaceMask { get; set; }

        /// <summary>
        /// Cache por río: centerline visual final, halfWidths y anclas (desde camino funcional).
        /// Construido una vez por <see cref="RiverSurfaceMeshBuilder.EnsureRiverVisualSurfaceCache"/>.
        /// </summary>
        public List<RiverVisualSurfaceData> RiverVisualSurfaces { get; set; }

        /// <summary>True si <see cref="RiverVisualSurfaces"/> y <see cref="RiverVisualSurfaceMask"/> corresponden a la generación actual.</summary>
        public bool RiverVisualSurfacesBuilt { get; set; }

        /// <summary>
        /// UWP: cache visual final congelada; mesh, carve y splat deben leer la misma verdad sin rebuild.
        /// </summary>
        public bool RiverVisualSurfaceCacheFrozen { get; set; }

        /// <summary>
        /// UWP post-freeze: ex-corredor funcional de tributarios skipped fuera de <see cref="RiverVisualSurfaceMask"/>.
        /// </summary>
        public bool[,] UwpSkippedTributaryFunctionalMask { get; set; }

        /// <summary>Invalida cache visual (p. ej. antes de regenerar mapa).</summary>
        public void ClearRiverVisualSurfaceCache()
        {
            RiverVisualSurfacesBuilt = false;
            RiverVisualSurfaceCacheFrozen = false;
            RiverVisualSurfaces = null;
            RiverVisualSurfaceMask = null;
            UwpSkippedTributaryFunctionalMask = null;
        }

        /// <summary>Confluencias tributario→principal registradas tras generación (visual/terreno; no altera CellData).</summary>
        public List<RiverConfluenceNode> RiverConfluences { get; set; }

        /// <summary>Rol dendrítico por índice de centerline (paralelo a <see cref="RiverCenterlinesCellSpace"/>).</summary>
        public List<RiverDendriticRole> RiverDendriticRoles { get; set; }

        /// <summary>Río receptor (índice centerline); -1 main sin receptor, 0 = colector principal.</summary>
        public List<int> RiverReceiverIds { get; set; }

        /// <summary>Ancho lógico/visual como fracción del main (1 = colector).</summary>
        public List<float> RiverWidthRatioToMain { get; set; }

        /// <summary>Metadatos hidrológicos (PR0+): paralelos a <see cref="RiverCenterlinesCellSpace"/>; no alteran gameplay por sí solos.</summary>
        public HydrologyNetworkGraph HydrologyNetwork { get; set; }

        /// <summary>Debug: polilínea macro por río (espacio celda). Solo si MapGenConfig.debugDrawRiverPathInScene.</summary>
        public List<List<Vector2>> RiverPathDebugMacro { get; set; }
        /// <summary>Debug: centerline suavizada antes del raster (espacio celda).</summary>
        public List<List<Vector2>> RiverPathDebugSmoothed { get; set; }

        /// <summary>
        /// Celdas del cuerpo de cada lago (flood fill + bocas absorbidas / embudo de conectores).
        /// Sirve para BFS orgánico río→lago sin propagar por todo el cauce.
        /// </summary>
        public HashSet<long> LakeBodyCellsPacked { get; set; }

        /// <summary>Componentes conectados del cuerpo de lago (post-flood). Cache para reglas tributario↔lago.</summary>
        public List<HashSet<long>> LakeBodyComponents { get; set; }

        /// <summary>Por componente de lago: índice del único tributario autorizado (-1 = ninguno).</summary>
        public List<int> LakeComponentTributaryOwnerRiverIndex { get; set; }

        /// <summary>UWP lake-first: grafo hidrológico validado (main, lagos, tributarios).</summary>
        public UwpWaterGraph LakeFirstWaterGraph { get; set; }

        /// <summary>
        /// Subconjunto de boca de lago (embudo conector): el MS de lago las incluye aunque solapen máscara de ribbon.
        /// </summary>
        public HashSet<long> LakeMouthCellsPacked { get; set; }

        public List<Vector2Int> PlannedLakeSinkCandidates { get; set; }

        /// <summary>Patrón de extremos del río principal colocado (Fase4); usado para alinear lago con Highland→Lago.</summary>
        public RiverMainPattern? HydrologyMainRiverPattern { get; set; }

        /// <summary>Celda Land meta del río principal (LakeSink, borde, etc.); prioriza semilla de lago cerca de la boca.</summary>
        public Vector2Int? HydrologyMainRiverTerminusCell { get; set; }

        /// <summary>Distancia Chebyshev a la celda de agua/río más cercana (alpha / recursos). Null hasta que WaterDistanceField la rellene.</summary>
        public int[,] DistanceToWaterCells { get; set; }

        /// <summary>Distancia interior desde agua/rio hacia su orilla mas cercana. Null hasta WaterSurfaceFieldBuilder.</summary>
        public int[,] WaterShoreDistanceCells { get; set; }

        /// <summary>Profundidad visual normalizada por celda (0=orilla/vado, 1=interior profundo). Null hasta WaterSurfaceFieldBuilder.</summary>
        public float[,] WaterDepth01 { get; set; }

        /// <summary>Flujo horizontal XZ por celda, en direccion downstream del rio. Lagos quedan cerca de cero.</summary>
        public Vector2[,] WaterFlowXZ { get; set; }

        /// <summary>Clasificación semántica alpha (post-carve, pre-recursos).</summary>
        public Project.Gameplay.Map.Generation.Alpha.SemanticRegionMap SemanticRegions { get; set; }

        private CellData[,] _cells;

        public GridSystem(int width, int height, float cellSizeWorld, Vector3 origin = default)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            CellSizeWorld = Mathf.Max(0.01f, cellSizeWorld);
            Origin = origin;
            _cells = new CellData[Width, Height];
            for (int x = 0; x < Width; x++)
                for (int z = 0; z < Height; z++)
                    _cells[x, z] = CellData.Default();
        }

        public ref CellData GetCell(int cx, int cz)
        {
            cx = Mathf.Clamp(cx, 0, Width - 1);
            cz = Mathf.Clamp(cz, 0, Height - 1);
            return ref _cells[cx, cz];
        }

        public ref CellData GetCell(Vector2Int c)
        {
            return ref GetCell(c.x, c.y);
        }

        public bool InBoundsCell(int cx, int cz)
        {
            return cx >= 0 && cx < Width && cz >= 0 && cz < Height;
        }

        public bool InBoundsCell(Vector2Int c)
        {
            return InBoundsCell(c.x, c.y);
        }

        /// <summary>Convierte posición mundo a celda (floor).</summary>
        public Vector2Int WorldToCell(Vector3 world)
        {
            int cx = Mathf.FloorToInt((world.x - Origin.x) / CellSizeWorld);
            int cz = Mathf.FloorToInt((world.z - Origin.z) / CellSizeWorld);
            return new Vector2Int(cx, cz);
        }

        /// <summary>Centro en mundo de la celda (cx, cz).</summary>
        public Vector3 CellToWorldCenter(int cx, int cz)
        {
            float x = Origin.x + (cx + 0.5f) * CellSizeWorld;
            float z = Origin.z + (cz + 0.5f) * CellSizeWorld;
            return new Vector3(x, Origin.y, z);
        }

        public Vector3 CellToWorldCenter(Vector2Int c)
        {
            return CellToWorldCenter(c.x, c.y);
        }

        /// <summary>Nodo (esquina) más cercano en grid de nodos (width+1) x (height+1).</summary>
        public Vector2Int WorldToNode(Vector3 world)
        {
            int nx = Mathf.RoundToInt((world.x - Origin.x) / CellSizeWorld);
            int nz = Mathf.RoundToInt((world.z - Origin.z) / CellSizeWorld);
            nx = Mathf.Clamp(nx, 0, Width);
            nz = Mathf.Clamp(nz, 0, Height);
            return new Vector2Int(nx, nz);
        }

        public Vector3 NodeToWorld(int nx, int nz)
        {
            float x = Origin.x + nx * CellSizeWorld;
            float z = Origin.z + nz * CellSizeWorld;
            return new Vector3(x, Origin.y, z);
        }

        /// <summary>Vecinos 4 (N-S-E-O) en celdas. Solo celdas dentro de bounds.</summary>
        public IEnumerable<Vector2Int> Neighbors4(int cx, int cz)
        {
            if (InBoundsCell(cx - 1, cz)) yield return new Vector2Int(cx - 1, cz);
            if (InBoundsCell(cx + 1, cz)) yield return new Vector2Int(cx + 1, cz);
            if (InBoundsCell(cx, cz - 1)) yield return new Vector2Int(cx, cz - 1);
            if (InBoundsCell(cx, cz + 1)) yield return new Vector2Int(cx, cz + 1);
        }

        public IEnumerable<Vector2Int> Neighbors4(Vector2Int c)
        {
            return Neighbors4(c.x, c.y);
        }

        /// <summary>Vecinos 8 (incluye diagonales). Solo celdas dentro de bounds.</summary>
        public IEnumerable<Vector2Int> Neighbors8(int cx, int cz)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if ((dx != 0 || dz != 0) && InBoundsCell(cx + dx, cz + dz))
                        yield return new Vector2Int(cx + dx, cz + dz);
        }

        public IEnumerable<Vector2Int> Neighbors8(Vector2Int c)
        {
            return Neighbors8(c.x, c.y);
        }
    }

    /// <summary>Nodo urbano: centro en celda y radio para área buildable.</summary>
    [Serializable]
    public class CityNode
    {
        public int Id;
        public Vector2Int Center;
        public int RadiusCells;
    }

    /// <summary>Camino entre dos ciudades: path en celdas.</summary>
    [Serializable]
    public class Road
    {
        public int FromCityId;
        public int ToCityId;
        public List<Vector2Int> PathCells = new List<Vector2Int>();
    }
}
