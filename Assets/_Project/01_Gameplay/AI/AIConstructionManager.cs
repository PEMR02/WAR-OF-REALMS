using UnityEngine;
using Project.Gameplay.Buildings;
using Project.Gameplay.Faction;
using Project.Gameplay.Map;
using Project.Gameplay.Players;

namespace Project.Gameplay.AI
{
    public sealed class AIConstructionManager
    {
        readonly BuildingPlacer _placer;
        readonly Terrain _terrain;
        readonly LayerMask _blockingMask;

        public AIConstructionManager(BuildingPlacer placer, Terrain terrain, LayerMask blockingMask)
        {
            _placer = placer;
            _terrain = terrain;
            _blockingMask = blockingMask;
        }

        public bool TryBuildNearTownCenter(BuildingSO building, Vector3 tcPos, PlayerResources payFrom, FactionId faction, int maxBuilders, int profileSeed, out BuildSite site)
        {
            site = null;
            if (building == null || payFrom == null || _placer == null) return false;
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return false;
            if (!_placer.CanAffordFor(building, payFrom)) return false;

            Vector2Int center = MapGrid.Instance.WorldToCell(tcPos);
            int bw = Mathf.Max(1, Mathf.RoundToInt(building.size.x));
            int bh = Mathf.Max(1, Mathf.RoundToInt(building.size.y));
            float cs = MapGrid.Instance.cellSize;
            Vector3 origin = MapGrid.Instance.origin;
            Random.InitState(profileSeed + center.x * 73856093 ^ center.y);

            for (int ring = 3; ring <= 22; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    for (int dz = -ring; dz <= ring; dz++)
                    {
                        if (Mathf.Abs(dx) != ring && Mathf.Abs(dz) != ring) continue;
                        var cell = new Vector2Int(center.x + dx, center.y + dz);
                        if (!MapGrid.Instance.IsInBounds(cell)) continue;
                        Vector3 world = MapGrid.Instance.CellToWorld(cell);
                        world = GridSnapUtil.SnapToBuildingGrid(world, origin, cs, bw, bh);
                        if (_terrain != null)
                            world.y = _terrain.SampleHeight(world) + _terrain.transform.position.y;
                        if (world.sqrMagnitude < 9f) continue;
                        if (!PlacementValidator.IsValidPlacement(world, building.size, _blockingMask)) continue;
                        if (MapGrid.Instance.IsWorldAreaFree(world, building.size, true))
                        {
                            var rot = Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f);
                            if (_placer.TryPlaceBuildSiteForOwner(building, world, rot, payFrom, out site))
                            {
                                _placer.AssignBuildersToSiteForOwner(site, payFrom, faction, maxBuilders);
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }
    }
}
