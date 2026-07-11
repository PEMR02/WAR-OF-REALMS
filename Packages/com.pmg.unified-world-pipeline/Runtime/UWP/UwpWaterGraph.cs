using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    public enum UwpTributaryOriginKind
    {
        None = 0,
        LakeSpill = 1,
        InlandFeeder = 2,
        HeadwaterFeeder = 3,
    }

    /// <summary>Metadata por índice de río (paralelo a <see cref="GridSystem.RiverCenterlinesCellSpace"/>).</summary>
    public static class UwpTributaryOriginUtility
    {
        public static void Clear(GridSystem grid)
        {
            grid?.RiverOriginKinds?.Clear();
        }

        public static void EnsureSlot(GridSystem grid, int riverIndex)
        {
            if (grid == null || riverIndex < 0)
                return;
            if (grid.RiverOriginKinds == null)
                grid.RiverOriginKinds = new List<UwpTributaryOriginKind>(8);
            while (grid.RiverOriginKinds.Count <= riverIndex)
                grid.RiverOriginKinds.Add(UwpTributaryOriginKind.None);
        }

        public static void SetOrigin(GridSystem grid, int riverIndex, UwpTributaryOriginKind kind)
        {
            EnsureSlot(grid, riverIndex);
            grid.RiverOriginKinds[riverIndex] = kind;
        }

        public static UwpTributaryOriginKind GetOrigin(GridSystem grid, int riverIndex)
        {
            if (grid?.RiverOriginKinds == null || riverIndex < 0 || riverIndex >= grid.RiverOriginKinds.Count)
                return UwpTributaryOriginKind.None;
            return grid.RiverOriginKinds[riverIndex];
        }

        public static bool IsSupplemental(GridSystem grid, int riverIndex) =>
            GetOrigin(grid, riverIndex) == UwpTributaryOriginKind.InlandFeeder ||
            GetOrigin(grid, riverIndex) == UwpTributaryOriginKind.HeadwaterFeeder;

        public static bool IsInlandFeeder(GridSystem grid, int riverIndex) =>
            GetOrigin(grid, riverIndex) == UwpTributaryOriginKind.InlandFeeder;

        public static void PinEndpointConfluence(List<Vector2> centerline, Vector2Int confluence)
        {
            if (centerline == null || centerline.Count < 2)
                return;
            centerline[centerline.Count - 1] = new Vector2(confluence.x + 0.5f, confluence.y + 0.5f);
        }

        public static bool ShouldApplyMainRiverConfluenceIngress(GridSystem grid, int riverIndex, MapGenConfig config)
        {
            if (grid == null || config == null || riverIndex <= 0 || !config.uwpLakeFirstHydrologyPipeline)
                return false;
            if (IsTributaryLakeOwnerIndex(grid, riverIndex))
                return true;
            return IsInlandFeeder(grid, riverIndex);
        }

        public static bool UsesLakeSpillVisualTreatment(GridSystem grid, int riverIndex)
        {
            if (riverIndex <= 0)
                return false;
            var kind = GetOrigin(grid, riverIndex);
            if (kind == UwpTributaryOriginKind.InlandFeeder || kind == UwpTributaryOriginKind.HeadwaterFeeder)
                return false;
            if (kind == UwpTributaryOriginKind.LakeSpill)
                return true;
            return IsTributaryLakeOwnerIndex(grid, riverIndex);
        }

        public static bool UsesLakeFirstMainJoinMeshTreatment(GridSystem grid, MapGenConfig config, int riverIndex)
        {
            if (grid == null || config == null || riverIndex <= 0 || !config.uwpLakeFirstHydrologyPipeline)
                return false;
            return UsesLakeSpillVisualTreatment(grid, riverIndex) || IsInlandFeeder(grid, riverIndex);
        }

        public static bool IsHeadwaterFeeder(GridSystem grid, int riverIndex) =>
            GetOrigin(grid, riverIndex) == UwpTributaryOriginKind.HeadwaterFeeder;

        /// <summary>Lake-spill, inland y headwater: mismo meandro/orgánico lake-first.</summary>
        public static bool UsesLakeFirstTributaryMeanderTreatment(GridSystem grid, MapGenConfig config, int riverIndex)
        {
            if (grid == null || config == null || riverIndex <= 0 || !config.uwpLakeFirstHydrologyPipeline)
                return false;
            return UsesLakeFirstMainJoinMeshTreatment(grid, config, riverIndex) ||
                IsHeadwaterFeeder(grid, riverIndex);
        }

        /// <summary>Mismo contrato carve/mesh que lake-spill/inland; headwater aplica estrechamiento V después.</summary>
        public static bool UsesLakeFirstTributaryCarvePipeline(GridSystem grid, MapGenConfig config, int riverIndex) =>
            UsesLakeFirstTributaryMeanderTreatment(grid, config, riverIndex);

        static bool IsTributaryLakeOwnerIndex(GridSystem grid, int riverIndex)
        {
            if (grid?.LakeComponentTributaryOwnerRiverIndex == null || riverIndex <= 0)
                return false;
            for (int i = 0; i < grid.LakeComponentTributaryOwnerRiverIndex.Count; i++)
            {
                if (grid.LakeComponentTributaryOwnerRiverIndex[i] == riverIndex)
                    return true;
            }

            return false;
        }
    }

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
        public UwpSupplementalBuildReport SupplementalReport = new UwpSupplementalBuildReport();

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

    public sealed class UwpSupplementalBuildReport
    {
        public int InlandFeederTarget;
        public int InlandFeedersAccepted;
        public int InlandFeedersRejected;
        public int HeadwaterFeederTarget;
        public int HeadwaterFeedersAccepted;
        public int HeadwaterFeedersRejected;
        public readonly List<string> RejectLines = new List<string>();
    }
}
