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

            switch (result.type)
            {
                case RTSOrderTargetResolver.TargetType.BuildSite:
                    DispatchBuildSite(result.buildSite, selectedUnits);
                    return;
                case RTSOrderTargetResolver.TargetType.Resource:
                    DispatchGather(result.resourceNode, selectedUnits);
                    return;
                case RTSOrderTargetResolver.TargetType.Building:
                    DispatchBuilding(result, selectedUnits);
                    return;
                case RTSOrderTargetResolver.TargetType.Ground:
                    DispatchMove(result.hit.point, selectedUnits);
                    return;
            }
        }

        void DispatchBuildSite(BuildSite site, IReadOnlyList<UnitSelectable> selectedUnits)
        {
            if (site == null) return;
            if (debugLogs) Debug.Log("Orden: construir en " + site.name);
            for (int i = 0; i < selectedUnits.Count; i++)
            {
                var u = selectedUnits[i];
                if (u == null) continue;
                var g = u.GetComponent<VillagerGatherer>();
                if (g != null) g.PauseGatherKeepCarried();
                var builder = u.GetComponent<Builder>();
                if (builder != null) builder.SetBuildTarget(site);
            }
        }

        void DispatchGather(ResourceNode node, IReadOnlyList<UnitSelectable> selectedUnits)
        {
            if (node == null) return;
            for (int i = 0; i < selectedUnits.Count; i++)
            {
                var u = selectedUnits[i];
                if (u == null) continue;
                var builder = u.GetComponent<Builder>();
                if (builder != null) builder.SetBuildTarget(null);
                var g = u.GetComponent<VillagerGatherer>();
                if (g != null) g.Gather(node);
            }
        }

        void DispatchBuilding(RTSOrderTargetResolver.ResolveResult result, IReadOnlyList<UnitSelectable> selectedUnits)
        {
            bool anyHandled = false;
            for (int i = 0; i < selectedUnits.Count; i++)
            {
                var u = selectedUnits[i];
                if (u == null) continue;

                var builder = u.GetComponent<Builder>();
                var gatherer = u.GetComponent<VillagerGatherer>();

                if (result.dropOffPoint != null && gatherer != null && gatherer.IsCarrying && gatherer.GoDepositAt(result.dropOffPoint))
                {
                    if (debugLogs) Debug.Log($"Orden: depositar en {result.dropOffPoint.gameObject.name}");
                    anyHandled = true;
                    continue;
                }

                if (result.buildingHealth != null && result.buildingHealth.IsAlive && result.buildingHealth.CurrentHP < result.buildingHealth.MaxHP)
                {
                    var repairer = u.GetComponent<Repairer>();
                    if (repairer != null)
                    {
                        if (builder != null) builder.SetBuildTarget(null);
                        if (gatherer != null) gatherer.PauseGatherKeepCarried();
                        repairer.SetRepairTarget(result.buildingHealth);
                        anyHandled = true;
                        continue;
                    }
                }

                if (builder != null) builder.SetBuildTarget(null);
                if (gatherer != null) gatherer.PauseGatherKeepCarried();

                var mover = u.GetComponent<UnitMover>();
                if (mover != null)
                {
                    _bus.Enqueue(new MoveCommand(mover, result.buildingPosition));
                    anyHandled = true;
                }
            }
            if (anyHandled) _bus.Flush();
        }

        void DispatchMove(Vector3 target, IReadOnlyList<UnitSelectable> selectedUnits)
        {
            OrderFeedback.Spawn(target);

            for (int i = 0; i < selectedUnits.Count; i++)
            {
                var u = selectedUnits[i];
                if (u == null) continue;
                var builder = u.GetComponent<Builder>();
                if (builder != null) builder.SetBuildTarget(null);
                var repairer = u.GetComponent<Repairer>();
                if (repairer != null) repairer.SetRepairTarget(null);
                var gatherer = u.GetComponent<VillagerGatherer>();
                if (gatherer != null) gatherer.PauseGatherKeepCarried();
            }

            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            forward.Normalize();

            var formationPositions = FormationHelper.GenerateGrid(target, selectedUnits.Count, formationSpacing, forward);

            for (int i = 0; i < selectedUnits.Count; i++)
            {
                var mover = selectedUnits[i].GetComponent<UnitMover>();
                if (mover != null)
                    _bus.Enqueue(new MoveCommand(mover, formationPositions[i]));
            }
            _bus.Flush();
        }
    }
}
