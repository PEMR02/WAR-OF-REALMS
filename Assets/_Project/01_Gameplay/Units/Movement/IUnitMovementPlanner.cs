using Project.Gameplay.Buildings;
using UnityEngine;

namespace Project.Gameplay.Units.Movement
{
    public interface IUnitMovementPlanner
    {
        bool TryCreateGateTraversal(Vector3 from, Vector3 destination, GateController ignoredGate, out GateTraversalPlan gatePlan);
        bool TryPlanPath(Vector3 from, Vector3 destination, out UnitMovementPlan plan, out string failureReason);
    }
}
