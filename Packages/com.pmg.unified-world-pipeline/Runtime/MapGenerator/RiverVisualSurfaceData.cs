using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Superficie visual de un rio: centerline y mascara derivadas del camino funcional
    /// (<see cref="GridSystem.RiverCenterlinesCellSpace"/>), compartidas por mesh, terreno y limpieza MS.
    /// </summary>
    public sealed class RiverVisualSurfaceData
    {
        public int RiverIndex;
        public List<Vector2> RawFunctionalCenterlineCells = new List<Vector2>();
        public List<Vector2> FinalCenterlineCells = new List<Vector2>();
        public List<float> HalfWidthsWorld = new List<float>();
        /// <summary>Ancho usado para rasterizar <see cref="GridSystem.RiverVisualSurfaceMask"/>; el carve UWP congelado debe usar este.</summary>
        public List<float> MaskHalfWidthsWorld = new List<float>();
        public List<Vector2Int> LockedAnchorCells = new List<Vector2Int>();
        public bool BuiltFromFunctionalPath = true;
        public bool Skipped;
        public string SkipReason;
        public float PathFitMaxDeviationCells;
        public float PathFitAvgDeviationCells;
        public int PathFitRevertedPoints;
        public int FordAnchorCount;
        public float FordMaxDistanceCells;
        public int PreClipInputPoints;
        public int PreClipOutputPoints;
        public bool PreClipStart;
        public bool PreClipEnd;

        /// <summary>Auditoria UWP: mesh ribbon construido para este rio.</summary>
        public bool MeshBuilt;

        /// <summary>Auditoria UWP: carve de terreno aplicado con <see cref="FinalCenterlineCells"/>.</summary>
        public bool CarveApplied;

        /// <summary>Auditoria UWP: vado/crossford funcional presente en el tramo visual final.</summary>
        public bool CrossfordApplied;

        /// <summary>Longitud acumulada del mesh final (celdas, espacio grid).</summary>
        public float LengthMesh;

        /// <summary>Longitud acumulada del carve (celdas, espacio grid).</summary>
        public float LengthCarve;
    }
}