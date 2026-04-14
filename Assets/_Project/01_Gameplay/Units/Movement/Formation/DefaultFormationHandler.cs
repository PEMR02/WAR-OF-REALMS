using System.Collections.Generic;
using Project.Gameplay.Units;
using UnityEngine;

namespace Project.Gameplay.Units.Movement
{
    /// <summary>Adaptador inicial sobre FormationHelper para desacoplar formaciones del order controller.</summary>
    public sealed class DefaultFormationHandler : IFormationHandler
    {
        public List<Vector3> BuildFormationSlots(
            Vector3 target,
            int unitCount,
            float spacing,
            Vector3 forward,
            FormationStyle formationStyle,
            float randomOffset)
        {
            List<Vector3> positions = formationStyle == FormationStyle.Circle
                ? FormationHelper.GenerateCircle(target, unitCount, spacing, forward)
                : FormationHelper.GenerateGrid(target, unitCount, spacing, forward);

            if (randomOffset > 0f)
                FormationHelper.ApplyRandomOffset(positions, randomOffset);

            return positions;
        }
    }
}
