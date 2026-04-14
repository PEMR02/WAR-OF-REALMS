using System.Collections.Generic;
using Project.Gameplay.Units;
using UnityEngine;

namespace Project.Gameplay.Units.Movement
{
    public interface IMovementCoordinator
    {
        void RequestGroupMove(
            IReadOnlyList<IUnitMovementComponent> units,
            Vector3 target,
            Vector3 forward,
            float spacing,
            FormationStyle formationStyle,
            float randomOffset);
    }
}
