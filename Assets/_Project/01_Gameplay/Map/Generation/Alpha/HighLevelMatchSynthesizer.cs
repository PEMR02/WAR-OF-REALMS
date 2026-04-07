using UnityEngine;

namespace Project.Gameplay.Map.Generation.Alpha
{
    /// <summary>
    /// Alpha: copia declarativa de alto nivel sobre los slots legacy de <see cref="MatchConfig"/>
    /// para que el compilador y el MapGen existente sigan funcionando sin duplicar fuentes de verdad en runtime.
    /// </summary>
    public static class HighLevelMatchSynthesizer
    {
        public static void SynthesizeIntoLegacySlots(MatchConfig m)
        {
            if (m == null || !m.useHighLevelAlphaConfig) return;

            var L = m.layout;
            var T = m.terrainShape;
            var H = m.hydrology;
            var R = m.resourceDistribution;
            var P = m.playerSpawn;

            m.map.width = Mathf.Max(16, L.mapWidth);
            m.map.height = Mathf.Max(16, L.mapHeight);
            m.map.cellSize = Mathf.Max(0.5f, L.gridCellSize);
            m.map.centerAtOrigin = L.centerMapAtOrigin;
            m.map.randomSeedOnPlay = L.randomSeedOnPlay;
            m.map.seed = L.seed;
            m.players.playerCount = Mathf.Clamp(L.playerCount, 1, 8);
            m.players.spawnEdgePadding = Mathf.Max(4f, L.playableMarginCells * m.map.cellSize * 0.5f);
            m.players.flattenRadius = Mathf.Max(4f, L.baseFlattenRadiusWorld);
            if (m.players.flattenSpawnAreas != T.flattenSpawnZones)
                m.players.flattenSpawnAreas = T.flattenSpawnZones;

            m.water.baseHeightNormalized = Mathf.Clamp01(H.waterBaseHeightNormalized + H.waterCoverageBias * 0.05f);
            m.water.riverCount = H.riversEnabled ? Mathf.Clamp(H.riverCount, 0, 8) : 0;
            m.water.lakeCount = H.lakesEnabled ? Mathf.Clamp(H.lakeCount, 0, 12) : 0;
            int lakeMid = (H.lakeSizeCellsRange.x + H.lakeSizeCellsRange.y) / 2;
            m.water.maxLakeCells = Mathf.Clamp(lakeMid, 50, 2500);
            m.water.sandShoreCells = Mathf.Clamp(Mathf.RoundToInt(H.shorelineBlend * 2.5f), 1, 8);
            int rwMax = Mathf.Max(H.riverWidthCellsRange.x, H.riverWidthCellsRange.y);
            int rwMin = Mathf.Min(H.riverWidthCellsRange.x, H.riverWidthCellsRange.y);
            m.water.riverWidthRadiusCells = Mathf.Clamp(rwMax, 0, 6);
            m.water.riverWidthNoiseAmplitudeCells = Mathf.Clamp(rwMax - rwMin, 0, 3);

            float rough = T.terrainRoughness;
            m.geography.heightMultiplier = Mathf.Lerp(5f, 18f, rough) * (T.mountainsEnabled ? 1f : 0.75f);
            m.geography.terrainFlatness = Mathf.Lerp(0.75f, 0.35f, T.hillDensity);
            m.geography.noiseScale = Mathf.Lerp(0.028f, 0.014f, 1f - rough);
            m.geography.maxSlope = Mathf.Lerp(12f, 22f, rough);

            ApplyAbundance(R.forestDensity, ref m.resources.globalTrees, 58, 240);
            ApplyAbundance(R.stoneAbundance, ref m.resources.globalStone, 4, 28);
            ApplyAbundance(R.goldAbundance, ref m.resources.globalGold, 6, 32);
            ApplyAbundance(R.berryAbundance, ref m.resources.berries, 5, 18);
            if (!R.animalsEnabled)
                m.resources.globalAnimals = Vector2Int.zero;
            else
                ApplyAbundance(R.animalDensity, ref m.resources.globalAnimals, 4, 36);

            ApplyAbundance(R.forestDensity, ref m.resources.nearTrees, 6, 26);
            ApplyAbundance(R.forestDensity, ref m.resources.midTrees, 8, 34);

            m.resources.forestClustering = R.globalResourceClustersEnabled;
            m.resources.clusterDensity = R.clusterDensity;

            float fair = R.playerResourceFairness;
            m.resources.minWoodTrees = Mathf.RoundToInt(Mathf.Lerp(8, 22, fair));
            m.resources.minStoneNodes = Mathf.RoundToInt(Mathf.Lerp(3, 8, fair));
            m.resources.minGoldNodes = Mathf.RoundToInt(Mathf.Lerp(4, 10, fair));
            m.resources.minFoodValue = Mathf.RoundToInt(Mathf.Lerp(5, 14, fair));

            m.players.minPlayerDistance2p = Mathf.Max(40f, P.minSpawnDistanceWorld * 0.9f);
            m.players.minPlayerDistance4p = Mathf.Max(36f, P.minSpawnDistanceWorld * 0.75f);
            if (P.avoidMountains)
                m.players.maxSlopeAtSpawn = Mathf.Min(m.players.maxSlopeAtSpawn, 35f);
            if (H.avoidSpawnFlooding)
                m.players.waterExclusionRadius = Mathf.Max(m.players.waterExclusionRadius, 14f);

            SyncVisualPrefabsIntoResources(m);
        }

        static void SyncVisualPrefabsIntoResources(MatchConfig m)
        {
            var vb = m.visualBinding;
            var v = m.resources.visuals;
            void Take(ref GameObject target, GameObject from) { if (from != null) target = from; }
            void TakeArr(ref GameObject[] target, GameObject[] from) { if (from != null && from.Length > 0) target = from; }
            Take(ref v.treePrefab, vb.treePrefab);
            TakeArr(ref v.treePrefabVariants, vb.treePrefabVariants);
            Take(ref v.berryPrefab, vb.berryPrefab);
            TakeArr(ref v.berryPrefabVariants, vb.berryPrefabVariants);
            Take(ref v.animalPrefab, vb.animalPrefab);
            TakeArr(ref v.animalPrefabVariants, vb.animalPrefabVariants);
            Take(ref v.goldPrefab, vb.goldPrefab);
            TakeArr(ref v.goldPrefabVariants, vb.goldPrefabVariants);
            Take(ref v.stonePrefab, vb.stonePrefab);
            TakeArr(ref v.stonePrefabVariants, vb.stonePrefabVariants);
            if (vb.stoneMaterialOverride != null) v.stoneMaterialOverride = vb.stoneMaterialOverride;
            if (vb.treeMaterialOverrides != null && vb.treeMaterialOverrides.Length > 0) v.treeMaterialOverrides = vb.treeMaterialOverrides;
        }

        /// <summary>Mapea tuning alpha a campos técnicos del MapGen (post ApplyMatchToMapGen base).</summary>
        public static void ApplyHighLevelToMapGen(MatchConfig m, Project.Gameplay.Map.Generator.MapGenConfig cfg)
        {
            if (m == null || cfg == null || !m.useHighLevelAlphaConfig) return;

            var T = m.terrainShape;
            var H = m.hydrology;
            var R = m.resourceDistribution;
            var P = m.playerSpawn;

            cfg.macroTerrainEnabled = T.mountainsEnabled || T.basinCount > 0;
            cfg.macroMountainMassCount = T.mountainsEnabled ? Mathf.Clamp(T.mountainMassCount, 0, 12) : 0;
            cfg.macroMountainHeight01Min = T.mountainHeight01Range.x;
            cfg.macroMountainHeight01Max = Mathf.Max(T.mountainHeight01Range.x, T.mountainHeight01Range.y);
            cfg.macroMountainRadiusCellsMin = Mathf.Max(3, T.mountainRadiusCellsRange.x);
            cfg.macroMountainRadiusCellsMax = Mathf.Max(cfg.macroMountainRadiusCellsMin, T.mountainRadiusCellsRange.y);
            cfg.macroBasinCount = Mathf.Clamp(T.basinCount, 0, 8);
            cfg.macroBasinDepth01 = T.basinDepth01;
            cfg.macroRoughnessWeight = T.terrainRoughness;
            cfg.macroHillDensity = T.hillDensity;
            cfg.macroAvoidCitiesForMountains = T.mountainExclusionNearSpawn;

            int rwMax = Mathf.Max(H.riverWidthCellsRange.x, H.riverWidthCellsRange.y);
            int rwMin = Mathf.Min(H.riverWidthCellsRange.x, H.riverWidthCellsRange.y);
            cfg.riverWidthRadiusCells = Mathf.Clamp(rwMax, 0, 6);
            cfg.riverWidthNoiseAmplitudeCells = Mathf.Clamp(rwMax - rwMin, 0, 3);
            cfg.riverBedDepthBelowWater01 = Mathf.Clamp(H.riverDepthHint01, 0.004f, 0.14f);
            cfg.mergeRiverCellsTouchingLake = H.connectRiversToLakesIfPossible;
            cfg.macroMountainSpawnAvoidanceMarginCells = Mathf.Clamp(m.layout.playableMarginCells * 3, 8, 64);

            cfg.alphaUseTerrainResourceBias = R.useTerrainDrivenPlacement;
            cfg.alphaWoodNearWaterWeight = R.forestNearWaterBonus;
            cfg.alphaStoneMountainWeight = R.rockNearMountainBonus;
            cfg.alphaGoldMountainWeight = R.goldNearMountainBonus;
            cfg.alphaFoodNearWaterWeight = R.animalNearWaterBonus;
            cfg.alphaPreferPlainsForCities = P.preferPlains;
            cfg.alphaCityCenterMaxMeanHeight01 = P.preferPlains ? 0.68f : 0.82f;
            cfg.alphaMinChebyshevFromWaterForSpawn = P.avoidDeepWater ? 4 : 1;
            cfg.terrainMoistureRadius = Mathf.Max(cfg.terrainMoistureRadius, H.wetCorridorWidthCells);
        }

        static void ApplyAbundance(AbundanceTier tier, ref Vector2Int range, int baseMin, int baseMax)
        {
            float mul = tier switch
            {
                AbundanceTier.None => 0f,
                AbundanceTier.Low => 0.55f,
                AbundanceTier.Medium => 1f,
                AbundanceTier.High => 1.45f,
                AbundanceTier.VeryHigh => 1.9f,
                _ => 1f
            };
            if (tier == AbundanceTier.None)
            {
                range = Vector2Int.zero;
                return;
            }
            int a = Mathf.RoundToInt(baseMin * mul);
            int b = Mathf.RoundToInt(baseMax * mul);
            if (b < a) (a, b) = (b, a);
            range = new Vector2Int(Mathf.Max(0, a), Mathf.Max(a, b));
        }
    }
}
