using System.Collections.Generic;
using UnityEngine;
using Project.Gameplay.Buildings;
using Project.Gameplay.Combat;
using Project.Gameplay.Faction;
using Project.Gameplay.Players;
using Project.Gameplay.Units;

namespace Project.Gameplay.AI
{
    /// <summary>Cerebro RTS por jugador IA: managers en ticks, una dificultad = un perfil.</summary>
    public sealed class AIController : MonoBehaviour
    {
        [Header("Identidad")]
        public int playerIndexOneBased = 2;
        public FactionId myFaction = FactionId.Enemy;

        public PlayerResources resources;
        public PopulationManager population;
        public ProductionBuilding townCenterProduction;
        public Transform townCenterTransform;

        AIDifficultyProfile _profile;
        readonly AIKnowledge _knowledge = new();
        readonly AIStrategyManager _strategy = new();
        readonly AIEconomyManager _economy = new();
        AIConstructionManager _construction;
        AIProductionManager _production;
        AIMilitaryManager _military;
        readonly AITacticalManager _tactical = new();
        readonly AIScoutManager _scout = new();

        BuildingPlacer _placer;
        Terrain _terrain;
        LayerMask _blockingMask;

        float _stratAcc, _ecoAcc, _consAcc, _milAcc, _tacAcc, _scoutAcc;
        float _gameTime;

        public void Initialize(
            AIDifficulty difficulty,
            PlayerResources res,
            PopulationManager pop,
            ProductionBuilding tcProd,
            Transform tcTr,
            BuildingPlacer placer,
            Terrain terrain,
            LayerMask blockingMask,
            ProductionCatalog catalog)
        {
            resources = res;
            population = pop;
            townCenterProduction = tcProd;
            townCenterTransform = tcTr;
            _placer = placer;
            _terrain = terrain;
            _blockingMask = blockingMask;
            _profile = AIDifficultyProfile.Create(difficulty);
            _construction = placer != null ? new AIConstructionManager(placer, terrain, blockingMask) : null;
            _production = new AIProductionManager(catalog);
            _military = new AIMilitaryManager(_knowledge, _profile, myFaction);
        }

        void Update()
        {
            if (resources == null || townCenterTransform == null || _profile == null || population == null)
                return;

            float dt = Time.deltaTime;
            _gameTime += dt;
            if (Time.timeSinceLevelLoad < _profile.reactionDelay)
                return;

            _knowledge.GameTimeSeconds = _gameTime;
            _knowledge.MyTownCenterPosition = townCenterTransform.position;
            if (_knowledge.KnownEnemyTownCenter == null)
                _knowledge.KnownEnemyTownCenter = FindEnemyTownCenterTransform();
            _knowledge.RefreshHostiles(myFaction, townCenterTransform.position, 42f * Mathf.Max(0.35f, _profile.scoutingFrequency), 14f);

            var villagers = CollectMyVillagers();
            _knowledge.EstimateSelfMilitary(CollectMyArmyRoots());

            _stratAcc += dt;
            if (_stratAcc >= _profile.strategyTick)
            {
                _stratAcc = 0f;
                _strategy.Tick(_knowledge, resources, _profile, _military);
            }

            _ecoAcc += dt;
            if (_ecoAcc >= _profile.economyTick)
            {
                _ecoAcc = 0f;
                _economy.Tick(_knowledge, resources, villagers, _profile, townCenterTransform.position, myFaction);
            }

            _consAcc += dt;
            if (_consAcc >= _profile.constructionTick)
            {
                _consAcc = 0f;
                RunConstructionScoring();
            }

            _milAcc += dt;
            if (_milAcc >= _profile.militaryTick)
            {
                _milAcc = 0f;
                RunMilitaryScoring();
            }

            _tacAcc += dt;
            if (_tacAcc >= _profile.tacticalTick)
            {
                _tacAcc = 0f;
                _tactical.TickDefense(_knowledge, myFaction, _profile);
            }

            _scoutAcc += dt;
            if (_scoutAcc >= _profile.scoutingTick)
            {
                _scoutAcc = 0f;
                _scout.Tick(townCenterTransform.position, resources, myFaction, _profile, _gameTime);
            }

            if (townCenterProduction != null)
                _production.TickTownCenter(townCenterProduction, resources, population, _strategy.State, _profile);
            var rax = FindMyBarracksProduction();
            if (rax != null)
                _production.TickBarracks(rax, resources, population, _profile);
        }

        void RunMilitaryScoring()
        {
            float atk = AIDecisionScoring.AttackScore(_military, _strategy.State, _profile);
            float def = AIDecisionScoring.DefendScore(_knowledge, _strategy.State, _profile);
            if (def >= atk) return;
            var army = CollectMyArmy();
            if (army.Count == 0 || _knowledge.KnownEnemyTownCenter == null) return;
            if (!_military.ShouldAttackNow()) return;
            _tactical.TickAttackMove(army, _knowledge.KnownEnemyTownCenter, _profile);
        }

        void RunConstructionScoring()
        {
            if (_construction == null || _placer == null) return;

            bool houseSite = HasBuildSiteFor(resources, "House");
            bool raxSite = HasBuildSiteFor(resources, "Barracks");
            bool hasHouse = HasCompletedBuildingId("House");
            bool hasRax = HasCompletedBarracks();
            bool wantsRax = resources.wood >= 42 && (population.MaxPopulation >= 6 || hasHouse);

            float expand = AIDecisionScoring.ExpandScore(_strategy.State, resources.wood, _profile);
            float house = AIDecisionScoring.BuildHouseScore(resources, population, houseSite, hasHouse, _profile);
            float rax = AIDecisionScoring.BuildBarracksScore(resources, hasRax, raxSite, wantsRax, _profile);

            int choice = 0;
            float[] scores = { expand, house, rax };
            for (int i = 1; i < scores.Length; i++)
            {
                if (scores[i] > scores[choice])
                    choice = i;
            }
            float best = scores[choice];
            if (best < 2.5f) return;

            if (choice == 1 && AIControllerRuntimeCatalog.House != null)
            {
                _construction.TryBuildNearTownCenter(
                    AIControllerRuntimeCatalog.House,
                    townCenterTransform.position,
                    resources,
                    myFaction,
                    _profile.maxBuilders,
                    playerIndexOneBased * 1000 + Mathf.FloorToInt(_gameTime),
                    out _);
                return;
            }
            if (choice == 2 && AIControllerRuntimeCatalog.Barracks != null)
            {
                _construction.TryBuildNearTownCenter(
                    AIControllerRuntimeCatalog.Barracks,
                    townCenterTransform.position,
                    resources,
                    myFaction,
                    _profile.maxBuilders,
                    playerIndexOneBased * 2000 + Mathf.FloorToInt(_gameTime),
                    out _);
            }
        }

        Transform FindEnemyTownCenterTransform()
        {
            for (int p = 1; p <= 8; p++)
            {
                if (p == playerIndexOneBased) continue;
                var go = GameObject.Find($"TownCenter_Player{p}");
                if (go == null) continue;
                var fm = go.GetComponent<FactionMember>();
                if (fm != null && FactionMember.IsHostile(myFaction, fm.faction))
                    return go.transform;
            }
            return null;
        }

        List<VillagerGatherer> CollectMyVillagers()
        {
            var list = new List<VillagerGatherer>(8);
            var all = FindObjectsByType<VillagerGatherer>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var g = all[i];
                if (g == null || g.owner != resources) continue;
                var fm = g.GetComponentInParent<FactionMember>();
                if (fm != null && fm.faction == myFaction)
                    list.Add(g);
            }
            return list;
        }

        List<GameObject> CollectMyArmyRoots()
        {
            var list = new List<GameObject>(16);
            var atk = FindObjectsByType<UnitAttacker>(FindObjectsSortMode.None);
            for (int i = 0; i < atk.Length; i++)
            {
                var a = atk[i];
                if (a == null || a.GetComponent<VillagerGatherer>() != null) continue;
                var fm = a.GetComponentInParent<FactionMember>();
                if (fm != null && fm.faction == myFaction)
                    list.Add(a.gameObject);
            }
            return list;
        }

        List<UnitAttacker> CollectMyArmy()
        {
            var list = new List<UnitAttacker>(16);
            var atk = FindObjectsByType<UnitAttacker>(FindObjectsSortMode.None);
            for (int i = 0; i < atk.Length; i++)
            {
                var a = atk[i];
                if (a == null || a.GetComponent<VillagerGatherer>() != null) continue;
                var fm = a.GetComponentInParent<FactionMember>();
                if (fm != null && fm.faction == myFaction)
                    list.Add(a);
            }
            return list;
        }

        ProductionBuilding FindMyBarracksProduction()
        {
            var all = FindObjectsByType<ProductionBuilding>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var p = all[i];
                if (p == null || p == townCenterProduction) continue;
                var bi = p.GetComponent<BuildingInstance>();
                if (bi == null || bi.buildingSO == null || bi.buildingSO.id != "Barracks") continue;
                var fm = p.GetComponentInParent<FactionMember>();
                if (fm != null && fm.faction == myFaction && p.owner == resources)
                    return p;
            }
            return null;
        }

        static bool HasBuildSiteFor(PlayerResources res, string buildingId)
        {
            if (res == null || string.IsNullOrEmpty(buildingId)) return false;
            var sites = FindObjectsByType<BuildSite>(FindObjectsSortMode.None);
            for (int i = 0; i < sites.Length; i++)
            {
                var s = sites[i];
                if (s == null || s.owner != res) continue;
                if (s.buildingSO != null && s.buildingSO.id == buildingId)
                    return true;
            }
            return false;
        }

        bool HasCompletedBuildingId(string id)
        {
            if (townCenterTransform == null) return false;
            float r2 = 95f * 95f;
            var bis = FindObjectsByType<BuildingInstance>(FindObjectsSortMode.None);
            for (int i = 0; i < bis.Length; i++)
            {
                var bi = bis[i];
                if (bi == null || bi.buildingSO == null || bi.buildingSO.id != id) continue;
                var fm = bi.GetComponentInParent<FactionMember>();
                if (fm == null || fm.faction != myFaction) continue;
                if ((bi.transform.position - townCenterTransform.position).sqrMagnitude > r2) continue;
                var prod = bi.GetComponent<ProductionBuilding>();
                if (prod != null && prod.owner != resources) continue;
                return true;
            }
            return false;
        }

        bool HasCompletedBarracks() => HasCompletedBuildingId("Barracks");
    }
}
