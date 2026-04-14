using System.Collections.Generic;
using Project.Gameplay.Units;
using UnityEngine;

namespace Project.Gameplay.Units.Movement
{
    /// <summary>
    /// Coordina destinos individuales y grupales. La ejecución física sigue ocurriendo en cada unidad.
    /// </summary>
    public sealed class MovementCoordinator : IMovementCoordinator
    {
        readonly IFormationHandler _formationHandler;

        public MovementCoordinator(IFormationHandler formationHandler)
        {
            _formationHandler = formationHandler;
        }

        public void RequestGroupMove(
            IReadOnlyList<IUnitMovementComponent> units,
            Vector3 target,
            Vector3 forward,
            float spacing,
            FormationStyle formationStyle,
            float randomOffset)
        {
            if (units == null || units.Count == 0 || _formationHandler == null)
                return;

            List<Vector3> slots = _formationHandler.BuildFormationSlots(
                target,
                units.Count,
                spacing,
                forward,
                formationStyle,
                randomOffset);

            for (int i = 0; i < units.Count && i < slots.Count; i++)
            {
                units[i]?.RequestMove(slots[i]);
            }
        }
    }
}
