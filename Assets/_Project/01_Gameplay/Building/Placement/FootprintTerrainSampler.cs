using UnityEngine;
using Project.Gameplay.Map;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Muestrea alturas del terreno en el footprint completo del edificio (centro + cuatro esquinas).
    /// Usado para placement y validación topográfica.
    /// </summary>
    public static class FootprintTerrainSampler
    {
        public struct SampleResult
        {
            public float minHeight;
            public float maxHeight;
            public float avgHeight;
            public float heightDelta;
            public bool valid;
        }

        /// <summary>
        /// Muestrea terreno en centro, esquinas y (si footprint >= 3) puntos medios de bordes.
        /// Estilo Anno: más puntos en edificios grandes para evitar flotar en laderas.
        /// </summary>
        public static SampleResult Sample(Terrain terrain, Vector3 originWorld, Vector2 sizeInCells, float yawDegrees)
        {
            var result = new SampleResult { valid = false };
            if (terrain == null) return result;

            float cellSize = MapGrid.Instance != null && MapGrid.Instance.IsReady
                ? MapGrid.Instance.cellSize
                : 2.5f;

            float wx = sizeInCells.x * cellSize;
            float wz = sizeInCells.y * cellSize;
            float hx = wx * 0.5f;
            float hz = wz * 0.5f;

            Quaternion rot = Quaternion.Euler(0f, yawDegrees, 0f);

            // Siempre: centro + 4 esquinas
            float yCenter = SampleHeight(terrain, originWorld);
            float yFL = SampleHeight(terrain, originWorld + rot * new Vector3(-hx, 0f, hz));
            float yFR = SampleHeight(terrain, originWorld + rot * new Vector3(hx, 0f, hz));
            float yBL = SampleHeight(terrain, originWorld + rot * new Vector3(-hx, 0f, -hz));
            float yBR = SampleHeight(terrain, originWorld + rot * new Vector3(hx, 0f, -hz));

            float min = Mathf.Min(yCenter, yFL, yFR, yBL, yBR);
            float max = Mathf.Max(yCenter, yFL, yFR, yBL, yBR);
            float sum = yCenter + yFL + yFR + yBL + yBR;
            int count = 5;

            // Edificios 3x3 o mayores: añadir 4 puntos medios de borde (mejor apoyo en laderas, estilo Anno)
            if (sizeInCells.x >= 2.5f || sizeInCells.y >= 2.5f)
            {
                float yF = SampleHeight(terrain, originWorld + rot * new Vector3(0f, 0f, hz));
                float yB = SampleHeight(terrain, originWorld + rot * new Vector3(0f, 0f, -hz));
                float yL = SampleHeight(terrain, originWorld + rot * new Vector3(-hx, 0f, 0f));
                float yR = SampleHeight(terrain, originWorld + rot * new Vector3(hx, 0f, 0f));
                min = Mathf.Min(min, yF, yB, yL, yR);
                max = Mathf.Max(max, yF, yB, yL, yR);
                sum += yF + yB + yL + yR;
                count += 4;
            }

            result.minHeight = min;
            result.maxHeight = max;
            result.avgHeight = sum / count;
            result.heightDelta = max - min;
            result.valid = true;
            return result;
        }

        static float SampleHeight(Terrain terrain, Vector3 worldPos)
        {
            if (terrain == null) return worldPos.y;
            return terrain.SampleHeight(new Vector3(worldPos.x, 0f, worldPos.z)) + terrain.transform.position.y;
        }
    }
}
