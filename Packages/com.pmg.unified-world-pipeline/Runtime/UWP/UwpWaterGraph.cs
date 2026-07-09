using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    /// <summary>
    /// Grafo hidrológico lake-first: main, lagos validados y tributarios con conectividad comprobada
    /// antes de rasterizar / mesh / carve.
    /// </summary>
    public sealed class UwpWaterGraph
    {
        public int Seed;
        public List<Vector2> MainCenterlineCells = new List<Vector2>();
        public readonly List<UwpLakeGraphNode> Lakes = new List<UwpLakeGraphNode>();
        public readonly List<UwpTributaryGraphEdge> Tributaries = new List<UwpTributaryGraphEdge>();
        public readonly Dictionary<int, List<Vector2>> FinalCenterlineByRiverIndex = new Dictionary<int, List<Vector2>>();
        public UwpLakeFirstBuildReport Report = new UwpLakeFirstBuildReport();

        public bool TryGetFinalCenterline(int riverIndex, out List<Vector2> centerline)
        {
            centerline = null;
            if (!FinalCenterlineByRiverIndex.TryGetValue(riverIndex, out List<Vector2> cl) || cl == null || cl.Count < 2)
                return false;
            centerline = cl;
            return true;
        }
    }

    public sealed class UwpLakeGraphNode
    {
        public int ComponentIndex;
        public int SeedCellX;
        public int SeedCellZ;
        public int CellCount;
        public Vector2Int OutletCell;
        public bool OutletValid;
        public bool Accepted;
        public string RejectReason;
        public int DistanceToMainCells = -1;
        public int OwnerTributaryRiverIndex = -1;
        public HashSet<long> BodyCellsPacked;
    }

    public sealed class UwpTributaryGraphEdge
    {
        public int RiverIndex = -1;
        public int LakeComponentIndex = -1;
        public Vector2Int LakeOutletCell;
        public Vector2Int MainRiverConfluenceCell;
        public int MainCenterlineIndex = -1;
        public List<Vector2> CenterlineCells = new List<Vector2>();
        public List<Vector2Int> PathCells = new List<Vector2Int>();
        public List<Vector2> DebugCarvePathCells = new List<Vector2>();
        public bool Accepted;
        public string RejectReason;
        public int DistanceLakeToMainCells = -1;
        public bool ConnectivityValid;
    }

    public sealed class UwpLakeFirstBuildReport
    {
        public int LakeCandidates;
        public int LakesAccepted;
        public int LakesRejected;
        public int TributariesAccepted;
        public int TributariesRejected;
        public int FinalRiverCount;
        public bool FinalConnectivityOk;
        public readonly List<string> LakeRejectLines = new List<string>();
        public readonly List<string> TributaryRejectLines = new List<string>();
    }
}
