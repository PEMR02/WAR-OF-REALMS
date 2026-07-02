using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace PMG.UnifiedWorldPipeline
{
    /// <summary>Defaults runtime cuando no hay asset baseline (modo export mínimo).</summary>
    public static class UwpBootstrapRuntimeDefaults
    {
        public static void ApplyMapGen(MapGenConfig cfg)
        {
            if (cfg == null) return;
            cfg.gridW = 358;
            cfg.gridH = 358;
            cfg.cellSizeWorld = 2.5f;
            cfg.seed = 424242;
            cfg.waterHeight01 = 0.24f;
            cfg.cityCount = 2;
            cfg.riverCount = 4;
            cfg.lakeCount = 2;
            cfg.maxLakeCells = 1400;
            cfg.macroTerrainEnabled = false;
            cfg.macroMountainMassCount = 0;
            cfg.macroBasinCount = 0;
            cfg.terrainHeightWorld = 38f;
            cfg.paintTerrainByHeight = true;
        }
    }
}
