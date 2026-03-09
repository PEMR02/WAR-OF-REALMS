using System.Collections.Generic;
using UnityEngine;

namespace Project.Gameplay.Units
{
    public static class FormationHelper
    {
        /// <summary>Formación en cuadrícula: buena para grupos grandes, todos mirando hacia forward.</summary>
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

        /// <summary>Formación en arco/círculo: unidades repartidas en semicírculo hacia el destino. Reduce amontonamiento.</summary>
        public static List<Vector3> GenerateCircle(
            Vector3 center,
            int unitCount,
            float spacing,
            Vector3 forward)
        {
            var positions = new List<Vector3>(unitCount);
            if (unitCount <= 0) return positions;
            if (unitCount == 1)
            {
                positions.Add(center);
                return positions;
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            // Radio para que el arco tenga separación ~spacing entre unidades
            float arcRadians = Mathf.PI * 0.85f;
            float radius = Mathf.Max(spacing * 0.5f, (spacing * (unitCount - 1)) / arcRadians);

            for (int i = 0; i < unitCount; i++)
            {
                float t = (float)i / (unitCount - 1);
                float angle = Mathf.Lerp(-arcRadians * 0.5f, arcRadians * 0.5f, t);
                Vector3 offset = (-forward * Mathf.Cos(angle) + right * Mathf.Sin(angle)) * radius;
                positions.Add(center + offset);
            }

            return positions;
        }

        /// <summary>Pequeña variación aleatoria por posición para evitar solapamiento exacto (mismo NavMesh).</summary>
        public static void ApplyRandomOffset(List<Vector3> positions, float maxOffset)
        {
            if (positions == null || maxOffset <= 0f) return;
            for (int i = 0; i < positions.Count; i++)
            {
                float rx = (Random.value - 0.5f) * 2f * maxOffset;
                float rz = (Random.value - 0.5f) * 2f * maxOffset;
                positions[i] += new Vector3(rx, 0f, rz);
            }
        }
    }
}
