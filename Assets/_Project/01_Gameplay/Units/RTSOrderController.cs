using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Project.Core.Commands;
using Project.Gameplay.Buildings;
using Project.Gameplay.Combat;
using Project.Gameplay.Resources;
using UnityEngine.EventSystems;

namespace Project.Gameplay.Units
{
    /// <summary>Caché por unidad de componentes usados al dar órdenes (un GetComponent por tipo por frame de orden).</summary>
    public struct CachedUnitComponents
    {
        public UnitSelectable selectable;
        public Builder builder;
        public VillagerGatherer gatherer;
        public UnitMover mover;
        public Repairer repairer;
    }

    public class RTSOrderController : MonoBehaviour
    {
        [Header("Refs")]
        public Camera cam;
        public RTSSelectionController selection;

        [Header("Raycast Masks")]
        public LayerMask buildSiteMask;
        public LayerMask resourceMask;
        public LayerMask buildingMask;
        public LayerMask groundMask;

        [Header("Formation")]
        public float formationSpacing = 1.5f;

        private CommandBus _bus;
        [Header("Debug")]
        public bool debugLogs = false;

        /// <summary>Lista reutilizable para cachear componentes de la selección (evita alloc por orden).</summary>
        private readonly List<CachedUnitComponents> _cachedUnits = new List<CachedUnitComponents>(64);

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();
            _bus = new CommandBus();
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || cam == null || selection == null) return;
            if (!mouse.rightButton.wasPressedThisFrame) return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            var selectedUnits = selection.GetSelected();

            // Solo edificio productor seleccionado: click derecho en suelo = rally
            if (selectedUnits == null || selectedUnits.Count == 0)
            {
                var building = selection.GetSelectedBuilding();
                var prod = building != null ? building.GetComponent<ProductionBuilding>() : null;
                if (prod != null && groundMask != 0 && Physics.Raycast(ray, out RaycastHit hitRally, 5000f, groundMask))
                {
                    prod.useRallyPoint = true;
                    prod.rallyPointWorld = hitRally.point;
                    OrderFeedback.Spawn(hitRally.point);
                }
                return;
            }

            var result = RTSOrderTargetResolver.Resolve(ray, buildSiteMask, resourceMask, buildingMask, groundMask);
            CacheSelectedUnits(selectedUnits);

            switch (result.type)
            {
                case RTSOrderTargetResolver.TargetType.BuildSite:
                    DispatchBuildSite(result.buildSite, _cachedUnits);
                    return;
                case RTSOrderTargetResolver.TargetType.Resource:
                    DispatchGather(result.resourceNode, _cachedUnits);
                    return;
                case RTSOrderTargetResolver.TargetType.Building:
                    DispatchBuilding(result, _cachedUnits);
                    return;
                case RTSOrderTargetResolver.TargetType.Ground:
                    DispatchMove(result.hit.point, _cachedUnits);
                    return;
            }
        }

        void CacheSelectedUnits(IReadOnlyList<UnitSelectable> selectedUnits)
        {
            _cachedUnits.Clear();
            for (int i = 0; i < selectedUnits.Count; i++)
            {
                var u = selectedUnits[i];
                if (u == null) continue;
                _cachedUnits.Add(new CachedUnitComponents
                {
                    selectable = u,
                    builder = u.GetComponent<Builder>(),
                    gatherer = u.GetComponent<VillagerGatherer>(),
                    mover = u.GetComponent<UnitMover>(),
                    repairer = u.GetComponent<Repairer>()
                });
            }
        }

        void DispatchBuildSite(BuildSite site, List<CachedUnitComponents> cached)
        {
            if (site == null) return;
            if (debugLogs) Debug.Log("Orden: construir en " + site.name);
            for (int i = 0; i < cached.Count; i++)
            {
                var c = cached[i];
                if (c.gatherer != null) c.gatherer.PauseGatherKeepCarried();
                if (c.builder != null) c.builder.SetBuildTarget(site);
            }
        }

        void DispatchGather(ResourceNode node, List<CachedUnitComponents> cached)
        {
            if (node == null) return;
            for (int i = 0; i < cached.Count; i++)
            {
                var c = cached[i];
                if (c.builder != null) c.builder.SetBuildTarget(null);
                if (c.gatherer != null) c.gatherer.Gather(node);
            }
        }

        void DispatchBuilding(RTSOrderTargetResolver.ResolveResult result, List<CachedUnitComponents> cached)
        {
            bool anyHandled = false;
            for (int i = 0; i < cached.Count; i++)
            {
                var c = cached[i];

                if (result.dropOffPoint != null && c.gatherer != null && c.gatherer.IsCarrying && c.gatherer.GoDepositAt(result.dropOffPoint))
                {
                    if (debugLogs) Debug.Log($"Orden: depositar en {result.dropOffPoint.gameObject.name}");
                    anyHandled = true;
                    continue;
                }

                if (result.buildingHealth != null && result.buildingHealth.IsAlive && result.buildingHealth.CurrentHP < result.buildingHealth.MaxHP)
                {
                    if (c.repairer != null)
                    {
                        if (c.builder != null) c.builder.SetBuildTarget(null);
                        if (c.gatherer != null) c.gatherer.PauseGatherKeepCarried();
                        c.repairer.SetRepairTarget(result.buildingHealth);
                        anyHandled = true;
                        continue;
                    }
                }

                if (c.builder != null) c.builder.SetBuildTarget(null);
                if (c.gatherer != null) c.gatherer.PauseGatherKeepCarried();

                if (c.mover != null)
                {
                    _bus.Enqueue(new MoveCommand(c.mover, result.buildingPosition));
                    anyHandled = true;
                }
            }
            if (anyHandled) _bus.Flush();
        }

        void DispatchMove(Vector3 target, List<CachedUnitComponents> cached)
        {
            OrderFeedback.Spawn(target);

            for (int i = 0; i < cached.Count; i++)
            {
                var c = cached[i];
                if (c.builder != null) c.builder.SetBuildTarget(null);
                if (c.repairer != null) c.repairer.SetRepairTarget(null);
                if (c.gatherer != null) c.gatherer.PauseGatherKeepCarried();
            }

            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            forward.Normalize();

            var formationPositions = FormationHelper.GenerateGrid(target, cached.Count, formationSpacing, forward);

            for (int i = 0; i < cached.Count; i++)
            {
                if (cached[i].mover != null)
                    _bus.Enqueue(new MoveCommand(cached[i].mover, formationPositions[i]));
            }
            _bus.Flush();
        }
    }
}
