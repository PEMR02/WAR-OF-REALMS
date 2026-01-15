using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Units
{
    public static class FormationHelper
    {
        public static List<Vector3> GenerateGrid(
            Vector3 center,
            int unitCount,
            float spacing,
            Vector3 forward)
        {
            var positions = new List<Vector3>(unitCount);

            int cols = Mathf.CeilToInt(Mathf.Sqrt(unitCount));
            int rows = Mathf.CeilToInt(unitCount / (float)cols);

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            int index = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (index >= unitCount) break;

                    float x = (c - (cols - 1) * 0.5f) * spacing;
                    float z = -(r * spacing);

                    Vector3 offset = right * x + forward * z;
                    positions.Add(center + offset);

                    index++;
                }
            }

            return positions;
        }
    }
}
