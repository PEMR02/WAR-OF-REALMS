using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Project.Core.Commands;
using Project.Gameplay.Buildings;
using Project.Gameplay.Combat;
using Project.Gameplay.Faction;
using Project.Gameplay.Players;
using Project.Gameplay.Resources;
using Project.Gameplay.Units.Movement;
using Project.UI;

namespace Project.Gameplay.Units
{
    public enum FormationStyle { Grid, Circle }

    /// <summary>Caché por unidad de componentes usados al dar órdenes (un GetComponent por tipo por frame de orden).</summary>
    public struct CachedUnitComponents
    {
        public UnitSelectable selectable;
        public Builder builder;
        public VillagerGatherer gatherer;
        public UnitMover mover;
        public Repairer repairer;
        public UnityEngine.AI.NavMeshAgent agent;
    }

    public class RTSOrderController : MonoBehaviour
    {
        enum InputOrderType { None, AttackTarget, Gather, Move, InvalidBlocked }
        [Header("Refs")]
        public Camera cam;
        public RTSSelectionController selection;

        [Header("Raycast Masks")]
        public LayerMask buildSiteMask;
        public LayerMask resourceMask;
        public LayerMask buildingMask;
        public LayerMask groundMask;
        [Tooltip("Unidades enemigas: clic derecho para atacar. 0 = mismo que RTSSelectionController.unitLayerMask.")]
        public LayerMask unitAttackMask;

        [Header("Formation (movimiento en grupo)")]
        [Tooltip("Separación entre unidades en el destino. Valores mayores reducen amontonamiento.")]
        public float formationSpacing = 2f;
        [Tooltip("Grid = cuadrícula; Circle = arco hacia el destino (menos obstrucción mutua).")]
        public FormationStyle formationStyle = FormationStyle.Grid;
        [Tooltip("Pequeña variación aleatoria en cada posición para evitar que todas apunten al mismo punto del NavMesh.")]
        [Range(0f, 0.5f)]
        public float formationRandomOffset = 0.15f;

        private CommandBus _bus;
        private IMovementCoordinator _movementCoordinator;
        [Header("Debug")]
        public bool debugLogs = false;

        /// <summary>Lista reutilizable para cachear componentes de la selección (evita alloc por orden).</summary>
        private readonly List<CachedUnitComponents> _cachedUnits = new List<CachedUnitComponents>(64);
        private readonly List<IUnitMovementComponent> _movementUnits = new List<IUnitMovementComponent>(64);
        InputOrderType _lastLoggedOrderType = InputOrderType.None;

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();
            RefreshAttackRayMaskFromSelection();
            _bus = new CommandBus();
            _movementCoordinator = new MovementCoordinator(new DefaultFormationHandler());
        }

        void Start()
        {
            // Por si RTSSelectionController asigna resourceLayerMask u otros en Start después de nuestro Awake.
            RefreshAttackRayMaskFromSelection();
        }

        void RefreshAttackRayMaskFromSelection()
        {
            if (selection == null) return;
            if (unitAttackMask.value == 0)
                unitAttackMask = selection.unitLayerMask;
            if (resourceMask.value == 0)
                resourceMask = selection.resourceLayerMask;
            unitAttackMask |= selection.unitLayerMask;
            buildingMask |= selection.buildingLayerMask;
            resourceMask |= selection.resourceLayerMask;
            // Capa Default (0): unitarios que aún no se movieron de capa siguen siendo clickeables/ataqueables.
            unitAttackMask |= 1 << 0;
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || cam == null || selection == null) return;
            if (!mouse.rightButton.wasPressedThisFrame) return;

            if (UiInputRaycast.IsPointerOverGameObject())
            {
                LogInputOrder(InputOrderType.InvalidBlocked, "UI bloquea click derecho");
                return;
            }

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
                    OrderFeedback.Spawn(hitRally.point, OrderFeedbackType.Move);
                }
                return;
            }

            CacheSelectedUnitsForPlayerOrders(selectedUnits);
            if (_cachedUnits.Count == 0)
                return;

            if (TryDispatchAttackOrder(ray))
                return;

            var result = RTSOrderTargetResolver.Resolve(ray, buildSiteMask, resourceMask, buildingMask, groundMask);

            switch (result.type)
            {
                case RTSOrderTargetResolver.TargetType.BuildSite:
                    DispatchBuildSite(result.buildSite, _cachedUnits);
                    LogInputOrder(InputOrderType.InvalidBlocked, "build site");
                    return;
                case RTSOrderTargetResolver.TargetType.Resource:
                    DispatchGather(result.resourceNode, _cachedUnits);
                    LogInputOrder(InputOrderType.Gather, "recurso");
                    return;
                case RTSOrderTargetResolver.TargetType.Building:
                    DispatchBuilding(result, _cachedUnits);
                    return;
                case RTSOrderTargetResolver.TargetType.Ground:
                    DispatchMove(result.hit.point, _cachedUnits);
                    LogInputOrder(InputOrderType.Move, "suelo");
                    return;
                default:
                    LogInputOrder(InputOrderType.InvalidBlocked, "sin objetivo");
                    if (result.hasGroundHit)
                        OrderFeedback.Spawn(result.groundPosition, OrderFeedbackType.Invalid);
                    return;
            }
        }

        void CacheSelectedUnitsForPlayerOrders(IReadOnlyList<UnitSelectable> selectedUnits)
        {
            _cachedUnits.Clear();
            for (int i = 0; i < selectedUnits.Count; i++)
            {
                var u = selectedUnits[i];
                if (u == null || !FactionMember.IsPlayerCommandable(u.gameObject)) continue;
                _cachedUnits.Add(new CachedUnitComponents
                {
                    selectable = u,
                    builder = u.GetComponent<Builder>(),
                    gatherer = u.GetComponent<VillagerGatherer>(),
                    mover = u.GetComponent<UnitMover>(),
                    repairer = u.GetComponent<Repairer>(),
                    agent = u.GetComponent<UnityEngine.AI.NavMeshAgent>()
                });
            }
        }

        bool TryDispatchAttackOrder(Ray ray)
        {
            RefreshAttackRayMaskFromSelection();
            LayerMask mask = unitAttackMask | buildingMask;
            if (mask.value == 0) return false;

            // Collide: unidades/decorados con Collider en trigger siguen siendo objetivos válidos (alineado con selección de recursos).
            RaycastHit[] hits = Physics.RaycastAll(ray, 5000f, mask, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0) return false;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int h = 0; h < hits.Length; h++)
            {
                if (hits[h].collider == null) continue;

                var victimSel = hits[h].collider.GetComponentInParent<UnitSelectable>();
                if (!TryResolveAttackTargetFromHit(hits[h], out Transform resolvedTarget, out IHealth victimHealth))
                    continue;

                if (victimSel != null)
                {
                    if (!IsHostileAttackTarget(victimSel.gameObject))
                        continue;
                    if (TryAssignAttackers(victimSel.transform, hits[h].point))
                        return true;
                    continue;
                }

                // Objetivo con vida sin UnitSelectable (unidad o edificio).
                var hpGo = (victimHealth as MonoBehaviour)?.gameObject;
                if (hpGo == null || !IsHostileAttackTarget(hpGo))
                    continue;

                if (TryAssignAttackers(resolvedTarget, hits[h].point))
                    return true;
            }

            return false;
        }

        bool TryAssignAttackers(Transform attackTarget, Vector3 feedbackPoint)
        {
            var healthComp = attackTarget != null ? RTSOrderTargetResolver.ResolveHealthInHierarchy(attackTarget) : null;
            var targetHealth = healthComp as IHealth;
            if (targetHealth == null || !targetHealth.IsAlive)
                return false;
            bool any = false;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var atk = FindUnitAttackerForOrders(_cachedUnits[i].selectable);
                if (atk != null && atk.GetAttackDamage() <= 0) atk = null;
                if (atk == null) continue;
                atk.SetTarget(attackTarget);
                atk.EnableChaseForCurrentOrder();
                any = true;
            }
            if (any)
            {
                OrderFeedback.Spawn(feedbackPoint, OrderFeedbackType.Attack);
                LogInputOrder(InputOrderType.AttackTarget, attackTarget != null ? attackTarget.name : "<null>");
                if (debugLogs && attackTarget != null && healthComp != null
                    && attackTarget.GetComponentInParent<UnitSelectable>() == null
                    && attackTarget.GetComponentInParent<BuildingOwnership>() != null)
                    Debug.Log($"[Combat] Attack building target={attackTarget.root.name} hp={healthComp.CurrentHP}/{healthComp.MaxHP}");
            }
            else
            {
                LogInputOrder(InputOrderType.InvalidBlocked, "sin atacantes válidos");
                OrderFeedback.Spawn(feedbackPoint, OrderFeedbackType.Invalid);
            }
            return any;
        }

        static bool TryResolveAttackTargetFromHit(RaycastHit hit, out Transform target, out IHealth health)
        {
            target = null;
            health = null;
            if (hit.collider == null) return false;
            var hp = RTSOrderTargetResolver.ResolveHealthInHierarchy(hit.collider.transform);
            health = hp as IHealth;
            if (health == null || !health.IsAlive) return false;
            var mb = health as MonoBehaviour;
            if (mb != null)
                target = mb.transform;
            if (target == null)
                target = hit.collider.transform.root;
            return target != null;
        }

        static bool IsHostileAttackTarget(GameObject targetGo)
        {
            if (targetGo == null) return false;
            if (FactionMember.IsHostileToPlayer(targetGo))
                return true;

            var playerOwner = PlayerResources.FindPrimaryHumanSkirmish();
            var ownership = targetGo.GetComponentInParent<BuildingOwnership>();
            if (ownership == null)
            {
                var hp = RTSOrderTargetResolver.ResolveHealthInHierarchy(targetGo.transform);
                if (hp != null)
                    ownership = hp.transform.root.GetComponentInChildren<BuildingOwnership>(true);
            }
            if (ownership != null && ownership.owner != null && playerOwner != null && ownership.owner != playerOwner)
                return true;

            return false;
        }

        static string DescribeBuildingHostilityReason(GameObject targetGo, Health buildingHealth)
        {
            if (targetGo == null) return "null_target";
            if (FactionMember.IsHostileToPlayer(targetGo)) return "faction_hostile_on_chain";
            var humanPr = PlayerResources.FindPrimaryHumanSkirmish();
            var ownership = targetGo.GetComponentInParent<BuildingOwnership>();
            if (ownership == null && buildingHealth != null)
                ownership = buildingHealth.transform.root.GetComponentInChildren<BuildingOwnership>(true);
            if (ownership == null)
            {
                var hp = RTSOrderTargetResolver.ResolveHealthInHierarchy(targetGo.transform);
                if (hp != null)
                    ownership = hp.transform.root.GetComponentInChildren<BuildingOwnership>(true);
            }
            if (ownership == null) return "no_building_ownership";
            if (ownership.owner == null) return "ownership_owner_null";
            if (humanPr == null) return "no_human_PlayerResources";
            if (ownership.owner == humanPr) return "owner_is_human";
            return "owner_not_human";
        }

        static UnitAttacker FindUnitAttackerForOrders(UnitSelectable selectable)
        {
            if (selectable == null) return null;
            var atk = selectable.GetComponent<UnitAttacker>();
            if (atk == null) atk = selectable.GetComponentInChildren<UnitAttacker>(true);
            if (atk == null) atk = selectable.GetComponentInParent<UnitAttacker>();
            return atk;
        }

        static void ClearAttackTargets(List<CachedUnitComponents> cached)
        {
            for (int i = 0; i < cached.Count; i++)
            {
                if (cached[i].selectable == null) continue;
                var atk = FindUnitAttackerForOrders(cached[i].selectable);
                if (atk != null) atk.ClearTarget();
            }
        }

        void DispatchBuildSite(BuildSite site, List<CachedUnitComponents> cached)
        {
            if (site == null) return;
            ClearAttackTargets(cached);
            if (debugLogs) Debug.Log("Orden: construir en " + site.name);
            for (int i = 0; i < cached.Count; i++)
            {
                var c = cached[i];
                if (c.gatherer != null) c.gatherer.PauseGatherKeepCarried();
                if (c.builder != null) c.builder.SetBuildTarget(site, "RTSOrder DispatchBuildSite");
            }
        }

        void DispatchGather(ResourceNode node, List<CachedUnitComponents> cached)
        {
            if (node == null) return;
            ClearAttackTargets(cached);
            for (int i = 0; i < cached.Count; i++)
            {
                var c = cached[i];
                if (c.builder != null) c.builder.SetBuildTarget(null, "RTSOrder DispatchGather");
                if (c.gatherer != null) c.gatherer.Gather(node);
            }
            OrderFeedback.Spawn(node.transform.position, OrderFeedbackType.Gather);
        }

        void DispatchBuilding(RTSOrderTargetResolver.ResolveResult result, List<CachedUnitComponents> cached)
        {
            bool hostileBuilding = result.buildingHealth != null && result.buildingHealth.IsAlive
                && IsHostileAttackTarget(result.buildingHealth.gameObject);

            if (debugLogs && result.buildingHealth != null)
            {
                string reason = DescribeBuildingHostilityReason(result.buildingHealth.gameObject, result.buildingHealth);
                Debug.Log($"[InputOrder] Building target hostile={hostileBuilding} reason={reason} name={result.buildingHealth.name}");
            }

            if (hostileBuilding && TryAssignAttackers(result.buildingHealth.transform, result.hit.point))
            {
                for (int i = 0; i < cached.Count; i++)
                {
                    var c = cached[i];
                    if (c.gatherer != null) c.gatherer.PauseGatherKeepCarried();
                    if (c.builder != null) c.builder.SetBuildTarget(null, "RTSOrder DispatchBuilding hostil");
                    if (c.repairer != null) c.repairer.SetRepairTarget(null);
                }
                return;
            }

            ClearAttackTargets(cached);
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

                if (!hostileBuilding
                    && result.buildingHealth != null && result.buildingHealth.IsAlive && result.buildingHealth.CurrentHP < result.buildingHealth.MaxHP)
                {
                    if (c.repairer != null)
                    {
                        if (c.builder != null) c.builder.SetBuildTarget(null, "RTSOrder DispatchBuilding reparar");
                        if (c.gatherer != null) c.gatherer.PauseGatherKeepCarried();
                        c.repairer.SetRepairTarget(result.buildingHealth);
                        anyHandled = true;
                        continue;
                    }
                }

                if (c.builder != null) c.builder.SetBuildTarget(null, "RTSOrder DispatchBuilding mover a edificio");
                if (c.gatherer != null) c.gatherer.PauseGatherKeepCarried();

                if (c.mover != null)
                {
                    _bus.Enqueue(new MoveCommand(c.mover, result.buildingPosition));
                    anyHandled = true;
                }
            }
            if (anyHandled) _bus.Flush();
            if (anyHandled)
                LogInputOrder(InputOrderType.Move, "building fallback");
        }

        void DispatchMove(Vector3 target, List<CachedUnitComponents> cached)
        {
            ClearAttackTargets(cached);
            OrderFeedback.Spawn(target, OrderFeedbackType.Move);

            for (int i = 0; i < cached.Count; i++)
            {
                var c = cached[i];
                if (c.builder != null)
                {
                    // Guarda mínima: si el click cae sobre/cerca del mismo muro activo, no cancelar build target.
                    var site = c.builder.CurrentBuildSite;
                    if (c.builder.ShouldWallBuildRuntimeLog() && site != null && site.IsCompoundPathBuilding && !site.IsCompleted)
                    {
                        bool kept = ShouldKeepActiveWallTarget(c.builder, target);
                        Vector3 closest = site.GetClosestPointOnActiveCompoundSegment(target, c.builder);
                        float dClick = Vector3.Distance(target, closest);
                        Debug.Log($"[WallBuildDbg] DispatchMove builder={c.builder.name} keptWallTarget={kept} click={target} distClickToActiveSeg={dClick:F3} site={site.name}", c.builder);
                    }
                    if (!ShouldKeepActiveWallTarget(c.builder, target))
                        c.builder.SetBuildTarget(null, "RTSOrder DispatchMove");
                }
                if (c.repairer != null) c.repairer.SetRepairTarget(null);
                if (c.gatherer != null) c.gatherer.PauseGatherKeepCarried();
            }

            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            forward.Normalize();

            float dynamicSpacing = formationSpacing;
            float maxRadius = 0.5f;
            for (int i = 0; i < cached.Count; i++)
            {
                if (cached[i].agent != null)
                    maxRadius = Mathf.Max(maxRadius, cached[i].agent.radius);
            }
            dynamicSpacing = Mathf.Max(dynamicSpacing, maxRadius * 2.4f);

            FormationStyle effectiveStyle = formationStyle;
            if (cached.Count >= 5 && effectiveStyle == FormationStyle.Grid)
                effectiveStyle = FormationStyle.Circle;

            _movementUnits.Clear();
            for (int i = 0; i < cached.Count; i++)
            {
                if (cached[i].mover != null)
                    _movementUnits.Add(cached[i].mover);
            }

            _movementCoordinator?.RequestGroupMove(
                _movementUnits,
                target,
                forward,
                dynamicSpacing,
                effectiveStyle,
                formationRandomOffset);
        }

        void LogInputOrder(InputOrderType type, string detail)
        {
            if (!debugLogs) return;
            if (type == _lastLoggedOrderType) return;
            _lastLoggedOrderType = type;
            Debug.Log($"[InputOrder] RightClick -> {type} ({detail})");
        }

        bool ShouldKeepActiveWallTarget(Builder builder, Vector3 moveTarget)
        {
            if (builder == null)
                return false;
            var site = builder.CurrentBuildSite;
            if (site == null || !site.IsCompoundPathBuilding || site.IsCompleted)
                return false;

            Vector3 closest = site.GetClosestPointOnActiveCompoundSegment(moveTarget, builder);
            float dist = Vector3.Distance(moveTarget, closest);
            float cell = 2.5f;
            var grid = Project.Gameplay.Map.MapGrid.Instance;
            if (grid != null && grid.IsReady)
                cell = grid.cellSize;
            float keepThreshold = Mathf.Max(1.25f, cell * 0.9f);
            return dist <= keepThreshold;
        }
    }
}
