using System.Collections.Generic;
using Project.Gameplay.Units;
using UnityEngine;

namespace Project.Gameplay.Units.Movement
{
    public interface IFormationHandler
    {
        List<Vector3> BuildFormationSlots(
            Vector3 target,
            int unitCount,
            float spacing,
            Vector3 forward,
            FormationStyle formationStyle,
            float randomOffset);
    }
}
