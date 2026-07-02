using UnityEngine;

namespace PMG.UnifiedWorldPipeline
{
    /// <summary>
    /// Bindings visuales opcionales desde escena (sustituto de RTSMapGenerator en proyectos sin el juego completo).
    /// </summary>
    public interface IUwpSceneVisualBindings
    {
        TerrainLayer GrassLayer { get; }
        TerrainLayer DirtLayer { get; }
        TerrainLayer RockLayer { get; }
        TerrainLayer SandLayer { get; }
        int SandShoreCells { get; }
        bool CenterAtOrigin { get; }
        int RiverCount { get; }
        int LakeCount { get; }
        int MaxLakeCells { get; }
    }
}
