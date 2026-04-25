using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Project.Gameplay.Faction;
using Project.Gameplay.Players;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Diagnóstico runtime para edificios económicos terminados y su estado de DropOff.
    /// Solo registra información; no modifica estado del juego.
    /// </summary>
    public static class EconomicBuildingDropOffDiagnostics
    {
        static readonly HashSet<int> LoggedBuildings = new HashSet<int>();

        public static void LogOnConstructionCompleted(GameObject buildingRoot, string sourceTag = "construction_complete")
        {
            if (buildingRoot == null) return;
            if (!IsEconomicBuildingName(buildingRoot.name)) return;

            int id = buildingRoot.GetInstanceID();
            if (!LoggedBuildings.Add(id)) return;

            var dropOff = buildingRoot.GetComponentInChildren<DropOffPoint>(true);
            var factionMember = buildingRoot.GetComponentInParent<FactionMember>() ?? buildingRoot.GetComponentInChildren<FactionMember>(true);
            var production = buildingRoot.GetComponentInParent<ProductionBuilding>() ?? buildingRoot.GetComponentInChildren<ProductionBuilding>(true);
            var owner = production != null ? production.owner : null;
            int layer = buildingRoot.layer;
            string layerName = LayerMask.LayerToName(layer);
            bool inGhostLayer = layerName == "Ghost";

            bool hasNonTriggerCollider = false;
            bool hasTriggerCollider = false;
            var colliders = buildingRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null || !colliders[i].enabled) continue;
                if (colliders[i].isTrigger) hasTriggerCollider = true;
                else hasNonTriggerCollider = true;
            }

            string acceptedMask = dropOff != null ? GetAcceptedMaskText(dropOff) : "<none>";
            string faction = factionMember != null ? factionMember.faction.ToString() : "<none>";
            string ownerName = owner != null ? owner.name : "<none>";

            Debug.Log(
                $"[DropOffDiagnostic] source={sourceTag} building={buildingRoot.name} active={buildingRoot.activeInHierarchy} " +
                $"dropOffPresent={(dropOff != null)} dropOffEnabled={(dropOff != null && dropOff.isActiveAndEnabled)} accepted={acceptedMask} " +
                $"layer={layer}:{layerName} ghost={inGhostLayer} faction={faction} owner={ownerName} " +
                $"colliders(nonTrigger={hasNonTriggerCollider}, trigger={hasTriggerCollider})",
                buildingRoot);

            LogRuntimePresenceSummary();
        }

        static void LogRuntimePresenceSummary()
        {
            var allDropOffs = UnityEngine.Object.FindObjectsByType<DropOffPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int dropOffInEconomicBuildings = 0;
            int inactiveDropOffChildren = 0;
            for (int i = 0; i < allDropOffs.Length; i++)
            {
                var d = allDropOffs[i];
                if (d == null) continue;
                if (IsEconomicBuildingName(d.gameObject.name) || IsEconomicBuildingName(d.transform.root.name))
                    dropOffInEconomicBuildings++;
                if (!d.gameObject.activeInHierarchy)
                    inactiveDropOffChildren++;
            }

            int completedSites = 0;
            int pendingSites = 0;
            var buildSites = UnityEngine.Object.FindObjectsByType<BuildSite>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < buildSites.Length; i++)
            {
                var site = buildSites[i];
                if (site == null || site.buildingSO == null) continue;
                if (!IsEconomicBuildingName(site.buildingSO.id) && !IsEconomicBuildingName(site.name)) continue;
                if (site.IsCompleted) completedSites++;
                else pendingSites++;
            }

            Debug.Log(
                $"[DropOffDiagnostic] runtime summary: dropOffsTotal={allDropOffs.Length}, " +
                $"dropOffsInEconomic={dropOffInEconomicBuildings}, inactiveDropOffChildren={inactiveDropOffChildren}, " +
                $"economicBuildSites(pending={pendingSites}, completed={completedSites})");
        }

        static string GetAcceptedMaskText(DropOffPoint dropOff)
        {
            try
            {
                return dropOff.accepts.ToString();
            }
            catch
            {
                var f = dropOff.GetType().GetField("accepts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object v = f != null ? f.GetValue(dropOff) : null;
                return v != null ? v.ToString() : "<unknown>";
            }
        }

        static bool IsEconomicBuildingName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("granary")
                   || n.Contains("lumbercamp")
                   || n.Contains("miningcamp")
                   || n.Contains("pf_granary")
                   || n.Contains("pf_lumbercamp")
                   || n.Contains("pf_miningcamp");
        }
    }
}
