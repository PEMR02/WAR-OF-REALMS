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

        /// <summary>Movimiento automático (IA, rally, persecución leve): puede encolar destino tras un cruce de puerta.</summary>
        bool RequestMove(Vector3 worldPos);

        /// <summary>Orden directa del jugador: cancela cruce de puerta / destino diferido y replanifica al instante.</summary>
        bool RequestPlayerMove(Vector3 worldPos);

        void Stop();
    }
}
