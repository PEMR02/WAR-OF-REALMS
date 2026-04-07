using UnityEngine;

namespace Project.Gameplay.Map.Generation.Alpha
{
    /// <summary>
    /// Alpha: punto único para documentar qué perfiles visuales aplican. La malla real sigue en WaterMeshBuilder/TerrainExporter hasta una fase de desacople total.
    /// </summary>
    public static class MapVisualBinder
    {
        public static void LogBindingPlan(VisualBindingConfig v, string prefix = "[MapGen]")
        {
            if (v == null)
            {
                Debug.Log($"{prefix} VisualBinding: (null)");
                return;
            }
            Debug.Log(
                $"{prefix} VisualBinding (perfiles): river={v.riverConstructionProfile}, lake={v.lakeConstructionProfile}, " +
                $"mountain={v.mountainDecorationProfile}, terrain={v.terrainDecorationProfile}, waterMat={v.waterMaterialProfile}, " +
                $"shore={v.shorelineProfile}, forest={v.forestVisualProfile}, rock={v.rockVisualProfile}, animal={v.animalSpawnProfile}");
        }
    }
}
