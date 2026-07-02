namespace PMG.UnifiedWorldPipeline
{
    /// <summary>Rutas de assets dentro del paquete UPM (Packages/com.pmg.unified-world-pipeline).</summary>
    public static class UwpAssetPaths
    {
        public const string PackageRoot = "Packages/com.pmg.unified-world-pipeline";
        public const string ContentRoot = PackageRoot + "/Content";
        public const string DefaultConfigRoot = ContentRoot + "/DefaultConfig";
        public const string BaselineRoot = DefaultConfigRoot;
        public const string OutputRoot = "Assets/PMGUnifiedWorldPipelineOutput";
        public const string ReportsRoot = OutputRoot + "/Reports";
        public const string SessionsRoot = OutputRoot + "/Sessions";

        public const string PipelineConfig = DefaultConfigRoot + "/PMGUnifiedWorldPipelineConfig.asset";
        public const string Checklist = DefaultConfigRoot + "/PMGUnifiedWorldChecklist.asset";
        public const string MapGenBaseline = DefaultConfigRoot + "/MapGenConfig.asset";
        public const string MatchBaseline = DefaultConfigRoot + "/UwpMatchBaseline.asset";

        public const string DefaultGrassLayer = ContentRoot + "/TerrainLayers/Texture_Grass.terrainlayer";
        public const string DefaultDirtLayer = ContentRoot + "/TerrainLayers/Texture_Dirt.terrainlayer";
        public const string DefaultRockLayer = ContentRoot + "/TerrainLayers/Texture_Rock.terrainlayer";
        public const string DefaultSandLayer = ContentRoot + "/TerrainLayers/Texture_Sand.terrainlayer";

        public const string DefaultRiverMaterial = ContentRoot + "/Materials/MAT_WOR_River.mat";
        public const string DefaultLakeMaterial = ContentRoot + "/Materials/MAT_WOR_Lake.mat";
        public const string DefaultRiverShader = ContentRoot + "/Shaders/WOR_RiverWater.shader";
        public const string DefaultLakeShader = ContentRoot + "/Shaders/WOR_LakeWater.shader";
    }
}
