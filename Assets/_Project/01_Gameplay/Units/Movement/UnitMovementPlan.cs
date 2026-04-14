using System.Collections.Generic;
using Project.Gameplay.Buildings;
using UnityEngine;

namespace Project.Gameplay.Units.Movement
{
    public struct GateTraversalPlan
    {
        public bool isValid;
        public Vector3 sameSidePoint;
        public Vector3 oppositeSidePoint;
        public Vector3 finalDestination;
        public GateController gate;
    }

    public struct UnitMovementPlan
    {
        public Vector3 requestedDestination;
        public Vector3 finalDestination;
        public bool isDirectFallback;
        public List<Vector3> waypoints;
    }
}
