using Project.Gameplay.Map.Generation.Alpha;
using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace Project.Gameplay.Map.Generation
{
    /// <summary>
    /// Compila <see cref="MatchConfig"/> (+ perfil técnico opcional) en un único <see cref="RuntimeMapGenerationSettings"/>.
    /// Toda generación definitiva debe pasar por aquí (vía <see cref="RTSMapGenerator"/> → <c>RunDefinitiveGenerate</c>).
    /// </summary>
    public static class MatchConfigCompiler
    {
        /// <summary>
        /// Prioridad de plantilla técnica: (1) <see cref="MatchConfig.mapGenerationProfile"/>,
        /// (2) <paramref name="sceneLegacyDefinitiveTemplate"/> con warning,
        /// (3) baseline vía <see cref="MapGenConfigFactory.CreateFrom"/>.
        /// </summary>
        public static RuntimeMapGenerationSettings Build(
            MatchConfig match,
            MapGenConfig sceneLegacyDefinitiveTemplate,
            Project.Gameplay.Map.RTSMapGenerator sceneOrNull = null,
            bool logSummary = true)
        {
            var result = new RuntimeMapGenerationSettings();
            if (match == null)
            {
                Debug.LogError("[MapGen] MatchConfigCompiler.Build: match null.");
                return result;
            }

            result.SourceMatch = match;
            result.SourceMatchName = match.name;
            result.TechnicalProfile = match.mapGenerationProfile;
            result.TechnicalProfileName = match.mapGenerationProfile != null ? match.mapGenerationProfile.name : "None";
            result.UsedHighLevelAlphaConfig = match.useHighLevelAlphaConfig;

            if (match.useHighLevelAlphaConfig)
            {
                HighLevelMatchSynthesizer.SynthesizeIntoLegacySlots(match);
                Debug.Log(
                    "[MapGen] Modo ALPHA: slots legacy (map/geography/water/resources/players) sincronizados desde layout/terrain/hydrology/… " +
                    "No edites esos campos en el asset mientras alpha esté activo.");
            }

            sceneOrNull?.ApplySceneHydrologyToMatch(match);
            sceneOrNull?.ApplySceneLobbyMacroToMatch(match);
            if (sceneOrNull != null && sceneOrNull.matchConfig != null && sceneOrNull.preferSceneHydrologyOverrides && logSummary)
                Debug.Log(
                    $"[MapGen] Hidrología aplicada desde RTSMapGenerator (iteración escena): ríos={match.water.riverCount}, lagos={match.water.lakeCount}, maxLakeCells={match.water.maxLakeCells}. " +
                    "Desactiva preferSceneHydrologyOverrides si el asset MatchConfig debe mandar siempre.");

            MapGenConfig cfg;
            MapGenConfig debugSource = null;
            if (match.mapGenerationProfile != null && match.mapGenerationProfile.technicalTemplate != null)
            {
                cfg = Object.Instantiate(match.mapGenerationProfile.technicalTemplate);
                cfg.hideFlags = HideFlags.HideAndDontSave;
                debugSource = match.mapGenerationProfile.technicalTemplate;
            }
            else if (sceneLegacyDefinitiveTemplate != null)
            {
                cfg = Object.Instantiate(sceneLegacyDefinitiveTemplate);
                cfg.hideFlags = HideFlags.HideAndDontSave;
                result.UsedSceneLegacyDefinitiveTemplate = true;
                debugSource = sceneLegacyDefinitiveTemplate;
                Debug.LogWarning(
                    "[MapGen] Se usó 'definitiveMapGenConfig' en escena como plantilla técnica. " +
                    "DEPRECATED: asigna un MapGenerationProfile en MatchConfig.mapGenerationProfile para trazabilidad.");
            }
            else
            {
                cfg = MapGenConfigFactory.CreateFrom(match);
                if (cfg == null)
                {
                    Debug.LogError("[MapGen] MatchConfigCompiler.Build: no se pudo crear MapGenConfig baseline.");
                    return result;
                }
                cfg.hideFlags = HideFlags.HideAndDontSave;
            }

            ApplyMatchToMapGen(match, cfg, result);
            if (match.useHighLevelAlphaConfig)
            {
                HighLevelMatchSynthesizer.ApplyHighLevelToMapGen(match, cfg);
                result.TerrainFeatures = new TerrainFeatureRuntime();
                cfg.alphaTerrainFeatureRecord = result.TerrainFeatures;
                cfg.alphaRegionRules = match.regionClassification.Clone();
            }
            else
            {
                cfg.alphaTerrainFeatureRecord = null;
                cfg.alphaRegionRules = null;
                cfg.alphaUseTerrainResourceBias = false;
                cfg.alphaPreferPlainsForCities = false;
                cfg.alphaMinChebyshevFromWaterForSpawn = 0;
                result.TerrainFeatures = null;
            }

            CopyDebugFlagsFromTemplate(cfg, debugSource);

            result.CompiledMapGen = cfg;
            result.Resources = BuildResourceRuntimeSettings(match);

            if (logSummary)
                LogCompilationSummary(result);

            return result;
        }

        /// <summary>
        /// Si el Match no tiene prefabs en <see cref="MatchConfig.ResourceVisualSettings"/>, rellena desde RTS (solo escena) y marca fallback.
        /// </summary>
        /// <returns>true si se rellenó algo desde la escena (MatchConfig incompleto).</returns>
        public static bool ApplyLegacyResourceFallbackFromScene(ResourceRuntimeSettings res, RTSMapGenerator scene)
        {
            if (res == null || scene == null) return false;
            bool any = false;
            void TakePrefab(ref GameObject main, GameObject fromScene, ref GameObject[] variants, GameObject[] fromVar)
            {
                if (main != null) return;
                if (fromScene == null && (fromVar == null || fromVar.Length == 0)) return;
                main = fromScene;
                if (variants == null || variants.Length == 0) variants = fromVar;
                any = true;
            }
            TakePrefab(ref res.treePrefab, scene.treePrefab, ref res.treePrefabVariants, scene.treePrefabVariants);
            TakePrefab(ref res.berryPrefab, scene.berryPrefab, ref res.berryPrefabVariants, scene.berryPrefabVariants);
            TakePrefab(ref res.animalPrefab, scene.animalPrefab, ref res.animalPrefabVariants, scene.animalPrefabVariants);
            TakePrefab(ref res.goldPrefab, scene.goldPrefab, ref res.goldPrefabVariants, scene.goldPrefabVariants);
            TakePrefab(ref res.stonePrefab, scene.stonePrefab, ref res.stonePrefabVariants, scene.stonePrefabVariants);
            if (res.stoneMaterialOverride == null && scene.stoneMaterialOverride != null) { res.stoneMaterialOverride = scene.stoneMaterialOverride; any = true; }
            if ((res.treeMaterialOverrides == null || res.treeMaterialOverrides.Length == 0) && scene.treeMaterialOverrides != null && scene.treeMaterialOverrides.Length > 0)
            { res.treeMaterialOverrides = scene.treeMaterialOverrides; any = true; }
            if (any)
                Debug.LogWarning(
                    "[MapGen] Recursos: faltaban prefabs en MatchConfig.resources.visuals; se aplicó fallback desde RTSMapGenerator (escena). " +
                    "Migra los prefabs al MatchConfig para eliminar esta fuente duplicada.");
            return any;
        }

        static void CopyDebugFlagsFromTemplate(MapGenConfig target, MapGenConfig template)
        {
            if (target == null || template == null) return;
            target.debugRiverRibbonGeometry = template.debugRiverRibbonGeometry;
            target.debugTerrainMoisture = template.debugTerrainMoisture;
            target.debugTerrainMacro = template.debugTerrainMacro;
            target.debugTerrainGrassDry = template.debugTerrainGrassDry;
            target.debugRiverVisualStats = template.debugRiverVisualStats;
            target.debugLogs = template.debugLogs;
        }

        public static ResourceRuntimeSettings BuildResourceRuntimeSettings(MatchConfig match)
        {
            var r = match.resources;
            var v = r.visuals;
            var p = r.placement;
            return new ResourceRuntimeSettings
            {
                ringNear = r.ringNear,
                ringMid = r.ringMid,
                ringFar = r.ringFar,
                nearTrees = r.nearTrees,
                midTrees = r.midTrees,
                berries = r.berries,
                animals = r.animals,
                goldSafe = r.goldSafe,
                stoneSafe = r.stoneSafe,
                goldFar = r.goldFar,
                globalTrees = r.globalTrees,
                globalStone = r.globalStone,
                globalGold = r.globalGold,
                globalAnimals = r.globalAnimals,
                globalExcludeRadius = r.globalExcludeRadius,
                forestClustering = r.forestClustering,
                clusterDensity = r.clusterDensity,
                clusterMinSize = r.clusterMinSize,
                clusterMaxSize = r.clusterMaxSize,
                minWoodTrees = r.minWoodTrees,
                minGoldNodes = r.minGoldNodes,
                minStoneNodes = r.minStoneNodes,
                minFoodValue = r.minFoodValue,
                maxResourceRetries = r.maxResourceRetries,
                globalTreesClusterFraction = p.globalTreesClusterFraction,
                preferGlobalTreesOnGrassAlphamap = p.preferGlobalTreesOnGrassAlphamap,
                globalStoneGoldClusterFraction = p.globalStoneGoldClusterFraction,
                globalMineralClusterSize = p.globalMineralClusterSize,
                globalMineralClusterRadiusCells = p.globalMineralClusterRadiusCells,
                treePrefab = v.treePrefab,
                treePrefabVariants = v.treePrefabVariants,
                berryPrefab = v.berryPrefab,
                berryPrefabVariants = v.berryPrefabVariants,
                animalPrefab = v.animalPrefab,
                animalPrefabVariants = v.animalPrefabVariants,
                goldPrefab = v.goldPrefab,
                goldPrefabVariants = v.goldPrefabVariants,
                stonePrefab = v.stonePrefab,
                stonePrefabVariants = v.stonePrefabVariants,
                stoneMaterialOverride = v.stoneMaterialOverride,
                treeMaterialOverrides = v.treeMaterialOverrides,
                treePlacementRotation = v.treePlacementRotation,
                resourceLayerName = string.IsNullOrEmpty(v.resourceLayerName) ? "Resource" : v.resourceLayerName,
                randomRotationPerResource = v.randomRotationPerResource,
                cellPlacementRandomOffset = v.cellPlacementRandomOffset,
                forceResourceShadowCasting = v.forceResourceShadowCasting,
            };
        }

        /// <summary>
        /// Autoridad de gameplay sobre <see cref="MapGenConfig"/>: grid, agua, clima, anillos, seed, etc.
        /// </summary>
        public static void ApplyMatchToMapGen(MatchConfig match, MapGenConfig config, RuntimeMapGenerationSettings diagnostics = null)
        {
            if (match == null || config == null) return;

            config.gridW = Mathf.Max(1, match.map.width);
            config.gridH = Mathf.Max(1, match.map.height);
            config.cellSizeWorld = Mathf.Max(0.01f, match.map.cellSize);
            config.seed = match.map.randomSeedOnPlay ? Random.Range(1, int.MaxValue) : match.map.seed;
            config.regionNoiseScale = match.geography.noiseScale;
            config.riverCount = Mathf.Clamp(match.water.riverCount, 0, 8);
            config.lakeCount = Mathf.Clamp(match.water.lakeCount, 0, 12);
            config.maxLakeCells = Mathf.Max(100, match.water.maxLakeCells);
            config.cityCount = Mathf.Max(1, match.players.playerCount);
            config.maxCitySlopeDeg = match.players.maxSlopeAtSpawn;
            config.ringNear = new Vector2Int((int)match.resources.ringNear.x, (int)match.resources.ringNear.y);
            config.ringMid = new Vector2Int((int)match.resources.ringMid.x, (int)match.resources.ringMid.y);
            config.ringFar = new Vector2Int((int)match.resources.ringFar.x, (int)match.resources.ringFar.y);
            config.minWoodPerCity = match.resources.minWoodTrees;
            config.minStonePerCity = match.resources.minStoneNodes;
            config.minGoldPerCity = match.resources.minGoldNodes;
            config.minFoodPerCity = match.resources.minFoodValue;
            config.maxResourceRetries = match.resources.maxResourceRetries;
            config.terrainHeightWorld = match.geography.heightMultiplier;
            config.heightmapResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(match.map.width, match.map.height)) + 1, 33, 4097);
            config.paintTerrainByHeight = match.climate.paintTerrainByHeight;
            config.grassLayer = match.climate.grassLayer;
            config.dirtLayer = match.climate.dirtLayer;
            config.rockLayer = match.climate.rockLayer;
            int totalPct = match.climate.grassPercent + match.climate.dirtPercent + match.climate.rockPercent;
            if (totalPct < 1) totalPct = 100;
            config.grassPercent01 = Mathf.Clamp01((float)match.climate.grassPercent / totalPct);
            config.dirtPercent01 = Mathf.Clamp01((float)match.climate.dirtPercent / totalPct);
            config.rockPercent01 = Mathf.Clamp01((float)match.climate.rockPercent / totalPct);
            config.grassMaxHeight01 = config.grassPercent01;
            config.dirtMaxHeight01 = config.grassPercent01 + config.dirtPercent01;
            config.textureBlendWidth = match.climate.textureBlendWidth;
            config.terrainBlendSharpness = match.climate.terrainBlendSharpness;
            config.terrainMacroNoiseScale = match.climate.terrainMacroNoiseScale;
            config.terrainMacroNoiseStrength = match.climate.terrainMacroNoiseStrength;
            config.grassDryLayer = match.climate.grassDryLayer;
            config.grassDryBlendStrength = match.climate.grassDryBlendStrength;
            config.grassDryNoiseScale = match.climate.grassDryNoiseScale;
            config.wetDirtLayer = match.climate.wetDirtLayer;
            config.terrainMoistureRadius = match.climate.terrainMoistureRadius;
            config.terrainMoistureStrength = match.climate.terrainMoistureStrength;
            config.terrainMoistureNoiseScale = match.climate.terrainMoistureNoiseScale;
            config.terrainMoistureNoiseStrength = match.climate.terrainMoistureNoiseStrength;
            config.sandEdgeNoiseScale = match.climate.sandEdgeNoiseScale;
            config.sandEdgeNoiseStrength = match.climate.sandEdgeNoiseStrength;
            config.debugTerrainMoisture = match.climate.debugTerrainMoisture;
            config.debugTerrainMacro = match.climate.debugTerrainMacro;
            config.debugTerrainGrassDry = match.climate.debugTerrainGrassDry;
            config.sandLayer = match.climate.sandLayer;
            config.sandShoreCells = match.water.sandShoreCells;
            config.waterChunkSize = match.water.chunkSize;
            config.waterSurfaceOffset = match.water.surfaceOffset;
            config.waterMaterial = match.water.material;
            config.waterAlpha = match.water.alpha;
            config.waterLayer = match.water.waterLayer >= 0 ? match.water.waterLayer : -1;
            config.riverWidthRadiusCells = Mathf.Clamp(match.water.riverWidthRadiusCells, 0, 6);
            config.riverWidthNoiseAmplitudeCells = Mathf.Clamp(match.water.riverWidthNoiseAmplitudeCells, 0, 3);
            config.lakeRiverMouthBlendCells = Mathf.Clamp(match.water.lakeRiverMouthBlendCells, 0, 8);

            float wh01 = Mathf.Clamp01(match.water.baseHeightNormalized);
            config.waterHeight01 = wh01;
            if (match.water.waterHeightRelative >= 0f && match.water.waterHeightRelative <= 1f)
            {
                Debug.LogWarning(
                    "[MapGen] MatchConfig.water.waterHeightRelative está en [0,1] pero ya no gobierna el generador definitivo. " +
                    "Usa water.baseHeightNormalized (autoridad canónica). Valor relative ignorado para waterHeight01.");
            }

            if (diagnostics != null)
            {
                diagnostics.ResolvedSeed = config.seed;
                diagnostics.ResolvedWaterHeight01 = config.waterHeight01;
            }
        }

        public static void LogResourcePlacementSummary(RuntimeMapGenerationSettings rt)
        {
            if (rt?.Resources == null) return;
            var r = rt.Resources;
            Debug.Log(
                "[MapGen] Recursos runtime (tras posible fallback escena):\n" +
                $"  Fallback escena: {(rt.UsedLegacyResourceFallbackFromScene ? "Sí (deprecated)" : "No")}\n" +
                $"  globalTrees [{r.globalTrees.x},{r.globalTrees.y}], globalStone [{r.globalStone.x},{r.globalStone.y}], globalGold [{r.globalGold.x},{r.globalGold.y}]\n" +
                $"  treePrefab: {(r.treePrefab != null ? r.treePrefab.name : "null")}");
        }

        static void LogCompilationSummary(RuntimeMapGenerationSettings rt)
        {
            var tree = rt.Resources?.treePrefab != null ? rt.Resources.treePrefab.name : "null";
            string mode = rt.UsedHighLevelAlphaConfig ? "ALPHA" : "Legacy";
            Debug.Log(
                "[MapGen] === Compilación runtime ===\n" +
                $"  Modo: {mode}\n" +
                $"  MatchConfig: {rt.SourceMatchName}\n" +
                $"  Perfil técnico: {rt.TechnicalProfileName}\n" +
                $"  Plantilla escena legacy: {(rt.UsedSceneLegacyDefinitiveTemplate ? "Sí (deprecated)" : "No")}\n" +
                $"  Seed final: {rt.ResolvedSeed}\n" +
                $"  waterHeight01 final: {rt.ResolvedWaterHeight01:F4}\n" +
                $"  Ríos/lagos (MapGen): {rt.CompiledMapGen?.riverCount}/{rt.CompiledMapGen?.lakeCount}\n" +
                $"  Macro montañas (cfg): {rt.CompiledMapGen?.macroMountainMassCount ?? 0}\n" +
                $"  treePrefab: {tree}");
        }
    }
}
