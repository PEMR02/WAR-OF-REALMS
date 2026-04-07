using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generation.Alpha
{
    /// <summary>Clasificación semántica por celda (alpha). No reemplaza regionId del grid legacy.</summary>
    public enum TerrainRegionType : byte
    {
        Unknown = 0,
        Plain = 1,
        Hill = 2,
        Mountain = 3,
        RiverBank = 4,
        LakeShore = 5,
        Basin = 6,
        RockyZone = 7,
        WetZone = 8,
        ForestCandidate = 9,
        SpawnFriendly = 10
    }

    [Serializable]
    public struct MountainFeature
    {
        public Vector2Int peakCell;
        public float peakHeight01;
        public int radiusCells;
    }

    [Serializable]
    public struct RiverFeatureSummary
    {
        public int axisIndex;
        public int sampleCount;
        public Vector2Int startCell;
        public Vector2Int endCell;
    }

    [Serializable]
    public struct LakeFeatureSummary
    {
        public int approxCellCount;
        public Vector2Int seedCell;
    }

    /// <summary>Snapshot de features tras generar (para logs y sistemas posteriores).</summary>
    [Serializable]
    public sealed class TerrainFeatureRuntime
    {
        public List<MountainFeature> mountains = new();
        public List<RiverFeatureSummary> rivers = new();
        public List<LakeFeatureSummary> lakes = new();
        public int macroMountainMassRequested;
        public int macroBasinRequested;
    }

    /// <summary>Máscara de clasificación; mismo tamaño que el grid lógico.</summary>
    public sealed class SemanticRegionMap
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        TerrainRegionType[] _cells;

        public SemanticRegionMap(int w, int h)
        {
            Width = w;
            Height = h;
            _cells = new TerrainRegionType[Mathf.Max(1, w) * Mathf.Max(1, h)];
        }

        public TerrainRegionType Get(int x, int z)
        {
            if ((uint)x >= (uint)Width || (uint)z >= (uint)Height) return TerrainRegionType.Unknown;
            return _cells[x + z * Width];
        }

        public void Set(int x, int z, TerrainRegionType t)
        {
            if ((uint)x >= (uint)Width || (uint)z >= (uint)Height) return;
            _cells[x + z * Width] = t;
        }

        public int CountType(TerrainRegionType t)
        {
            int n = 0;
            for (int i = 0; i < _cells.Length; i++)
                if (_cells[i] == t) n++;
            return n;
        }
    }
}
