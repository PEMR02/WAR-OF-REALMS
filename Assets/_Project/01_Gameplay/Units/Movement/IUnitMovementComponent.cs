using System;
using UnityEngine;

namespace Project.Gameplay.Units.Movement
{
    public interface IUnitMovementComponent
    {
        Transform Transform { get; }
        Vector3 CurrentDestination { get; }
        UnitMovementState MovementState { get; }
        bool IsFollowingPath { get; }
        bool IsGateTransitionActive { get; }

        event Action<IUnitMovementComponent> MovementStarted;
        event Action<IUnitMovementComponent> MovementStopped;
        event Action<IUnitMovementComponent, Vector3> DestinationChanged;
        event Action<IUnitMovementComponent, string> PathFailed;

        bool RequestMove(Vector3 worldPos);
        void Stop();
    }
}
