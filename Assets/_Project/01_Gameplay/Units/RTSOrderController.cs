using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Project.Core.Commands;
using Project.Gameplay.Buildings;
using Project.Gameplay.Combat;
using Project.Gameplay.Faction;
using Project.Gameplay.Resources;
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
        [Header("Debug")]
        public bool debugLogs = false;

        /// <summary>Lista reutilizable para cachear componentes de la selección (evita alloc por orden).</summary>
        private readonly List<CachedUnitComponents> _cachedUnits = new List<CachedUnitComponents>(64);

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();
            RefreshAttackRayMaskFromSelection();
            _bus = new CommandBus();
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
            unitAttackMask |= selection.unitLayerMask;
            buildingMask |= selection.buildingLayerMask;
            // Capa Default (0): unitarios que aún no se movieron de capa siguen siendo clickeables/ataqueables.
            unitAttackMask |= 1 << 0;
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || cam == null || selection == null) return;
            if (!mouse.rightButton.wasPressedThisFrame) return;

            if (UiInputRaycast.IsPointerOverGameObject())
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
                var victimHealth = hits[h].collider.GetComponentInParent<IHealth>();

                if (victimSel != null)
                {
                    if (!FactionMember.IsHostileToPlayer(victimSel.gameObject))
                        continue;
                    if (victimHealth == null || !victimHealth.IsAlive)
                        continue;
                    if (TryAssignAttackers(victimSel.transform, hits[h].point))
                        return true;
                    continue;
                }

                // Edificio u objetivo estático con vida (sin UnitSelectable)
                if (victimHealth == null || !victimHealth.IsAlive)
                    continue;
                if (hits[h].collider.GetComponentInParent<UnitMover>() != null)
                    continue;
                var hpGo = (victimHealth as MonoBehaviour)?.gameObject;
                if (hpGo == null || !FactionMember.IsHostileToPlayer(hpGo))
                    continue;

                var targetTf = (victimHealth as MonoBehaviour)?.transform;
                if (targetTf == null) continue;
                if (TryAssignAttackers(targetTf, hits[h].point))
                    return true;
            }

            return false;
        }

        bool TryAssignAttackers(Transform attackTarget, Vector3 feedbackPoint)
        {
            bool any = false;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var atk = FindUnitAttackerForOrders(_cachedUnits[i].selectable);
                if (atk == null) continue;
                atk.SetTarget(attackTarget);
                any = true;
            }
            if (any) OrderFeedback.Spawn(feedbackPoint);
            return any;
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
                if (c.builder != null) c.builder.SetBuildTarget(site);
            }
        }

        void DispatchGather(ResourceNode node, List<CachedUnitComponents> cached)
        {
            if (node == null) return;
            ClearAttackTargets(cached);
            for (int i = 0; i < cached.Count; i++)
            {
                var c = cached[i];
                if (c.builder != null) c.builder.SetBuildTarget(null);
                if (c.gatherer != null) c.gatherer.Gather(node);
            }
        }

        void DispatchBuilding(RTSOrderTargetResolver.ResolveResult result, List<CachedUnitComponents> cached)
        {
            bool hostileBuilding = result.buildingHealth != null && result.buildingHealth.IsAlive
                && FactionMember.IsHostileToPlayer(result.buildingHealth.gameObject);

            if (hostileBuilding && TryAssignAttackers(result.buildingHealth.transform, result.hit.point))
            {
                for (int i = 0; i < cached.Count; i++)
                {
                    var c = cached[i];
                    if (c.gatherer != null) c.gatherer.PauseGatherKeepCarried();
                    if (c.builder != null) c.builder.SetBuildTarget(null);
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
            ClearAttackTargets(cached);
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

            List<Vector3> formationPositions = effectiveStyle == FormationStyle.Circle
                ? FormationHelper.GenerateCircle(target, cached.Count, dynamicSpacing, forward)
                : FormationHelper.GenerateGrid(target, cached.Count, dynamicSpacing, forward);

            if (formationRandomOffset > 0f)
                FormationHelper.ApplyRandomOffset(formationPositions, formationRandomOffset);

            for (int i = 0; i < cached.Count; i++)
            {
                if (cached[i].mover != null)
                    _bus.Enqueue(new MoveCommand(cached[i].mover, formationPositions[i]));
            }
            _bus.Flush();
        }
    }
}
