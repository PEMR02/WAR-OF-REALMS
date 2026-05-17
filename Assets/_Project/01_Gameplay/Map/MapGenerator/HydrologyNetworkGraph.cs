using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    public enum RiverClass
    {
        MainRiver = 0,
        Tributary = 1,
        Creek = 2,
        LakeOutlet = 3,
    }

    public sealed class HydrologyRiverRecord
    {
        public int RiverId;
        public RiverClass RiverClass = RiverClass.MainRiver;
        public int? ParentRiverId;
        public int BasinId;
        public float EstimatedFlow01 = 1f;
        public byte WidthClass;
        public int JoinVertexIndex = -1;
        public Vector2Int StartCell;
        public Vector2Int EndCell;
        public int AcceptedLengthCells;
        public bool HierarchyFromConfluenceTrim;
        public string HierarchyReason = "";

        public bool IsMainStyle => RiverClass == RiverClass.MainRiver;
    }

    public sealed class HydrologyNetworkGraph
    {
        public readonly List<HydrologyRiverRecord> Rivers = new List<HydrologyRiverRecord>(8);

        public void Clear()
        {
            Rivers.Clear();
        }

        public void AddRiver(HydrologyRiverRecord record)
        {
            if (record == null)
                return;
            Rivers.Add(record);
        }

        public List<int> GetMainRiverIds()
        {
            var list = new List<int>(4);
            for (int i = 0; i < Rivers.Count; i++)
            {
                if (Rivers[i] != null && Rivers[i].RiverClass == RiverClass.MainRiver)
                    list.Add(Rivers[i].RiverId);
            }

            return list;
        }

        public void FinalizeLengthClassification()
        {
            if (Rivers.Count == 0)
                return;

            int maxLenTrimExcl = 0;
            for (int i = 0; i < Rivers.Count; i++)
            {
                var r = Rivers[i];
                if (r == null || r.HierarchyFromConfluenceTrim)
                    continue;
                if (r.AcceptedLengthCells > maxLenTrimExcl)
                    maxLenTrimExcl = r.AcceptedLengthCells;
            }

            int bestId = -1;
            for (int i = 0; i < Rivers.Count; i++)
            {
                var r = Rivers[i];
                if (r == null || r.HierarchyFromConfluenceTrim)
                    continue;
                if (r.AcceptedLengthCells == maxLenTrimExcl && (bestId < 0 || r.RiverId < bestId))
                    bestId = r.RiverId;
            }

            for (int i = 0; i < Rivers.Count; i++)
            {
                var r = Rivers[i];
                if (r == null)
                    continue;
                if (r.HierarchyFromConfluenceTrim)
                {
                    r.RiverClass = RiverClass.Tributary;
                    continue;
                }

                if (maxLenTrimExcl <= 0)
                {
                    r.RiverClass = RiverClass.MainRiver;
                    continue;
                }

                if (r.RiverId == bestId)
                    r.RiverClass = RiverClass.MainRiver;
                else if (r.AcceptedLengthCells >= Mathf.RoundToInt(0.82f * maxLenTrimExcl))
                    r.RiverClass = RiverClass.MainRiver;
                else
                    r.RiverClass = RiverClass.Tributary;
            }
        }

        public void LogHydrologyGraphSummary(MapGenConfig config)
        {
            if (config == null || !(config.debugHydrologyNetwork || config.debugLogs))
                return;
            int mains = 0;
            int trib = 0;
            int creeks = 0;
            int outlets = 0;
            for (int i = 0; i < Rivers.Count; i++)
            {
                var r = Rivers[i];
                if (r == null)
                    continue;
                switch (r.RiverClass)
                {
                    case RiverClass.MainRiver:
                        mains++;
                        break;
                    case RiverClass.Tributary:
                        trib++;
                        break;
                    case RiverClass.Creek:
                        creeks++;
                        break;
                    case RiverClass.LakeOutlet:
                        outlets++;
                        break;
                }
            }

            Debug.Log(
                "[HydrologyGraph] rivers=" + Rivers.Count +
                " main=" + mains +
                " tributaries=" + trib +
                " creeks=" + creeks +
                " lakeOutlets=" + outlets);
        }
    }
}
