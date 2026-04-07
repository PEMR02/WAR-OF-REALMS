using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Gameplay.Buildings;
using Project.Gameplay.Environment;
using Project.Gameplay.Map;
using Project.Gameplay.Map.Generator;
using Project.Gameplay.Resources;
using Project.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Project.Core.Benchmark
{
    [DefaultExecutionOrder(550)]
    public sealed class ForestStressTestRunner : MonoBehaviour
    {
        [Header("Execution")]
        public bool autoRunOnPlay = true;
        [Tooltip("Pausa tras aplicar cada caso antes del muestreo estable (no cuenta en avgFps).")]
        public float settleTime = 1.0f;
        [Tooltip("Solo fase estable: duración del muestreo FPS por caso (segundos).")]
        public float sampleDuration = 8.0f;
        [Tooltip("Tras el arranque y antes del primer caso: tiempo de calentamiento sin medir FPS.")]
        public float steadyStateWarmupSeconds = 10.0f;
        public bool logEveryCase = true;
        [Tooltip("Si true: solo 8 casos A–H y logs [PostFixPerf]/[PostFixSummary] con baseline_forest_postFix.")]
        public bool postFixBenchmarkSuite = true;

        bool _running;
        RuntimeMinimapBootstrap _minimap;
        GridGizmoRenderer _grid;
        RTSMapGenerator _gen;
        string _outPath;

        struct SampleResult
        {
            public float AvgFps;
            public float MedianFps;
            public float AvgMs;
            public float MinFps;
            public float MaxFps;
            public int Tris;
            public int Verts;
            public int Batches;
            public int ShadowCasters;
        }

        /// <summary>Desde entrada a espera de mapa hasta fin de LogCounts: duración y pico de frame (ms).</summary>
        sealed class StartupPhaseTracker
        {
            public float WallStartRealtime;
            public float PeakDeltaTime;

            public void RecordFrame()
            {
                float dt = Time.unscaledDeltaTime;
                if (dt > PeakDeltaTime)
                    PeakDeltaTime = dt;
            }
        }

        struct ActiveSnapshot
        {
            public GameObject Go;
            public bool WasActive;
        }

        readonly List<ActiveSnapshot> _woodTargets = new(4096);
        readonly List<ActiveSnapshot> _stoneTargets = new(1024);
        readonly List<ActiveSnapshot> _goldTargets = new(1024);
        readonly List<ActiveSnapshot> _animalTargets = new(256);
        readonly List<ActiveSnapshot> _foodOtherTargets = new(256);
        readonly List<ActiveSnapshot> _decorativeTargets = new(4096);
        readonly List<ActiveSnapshot> _wallTargets = new(512);

        sealed class CaseDef
        {
            public string Id;
            public bool MinimapTotalOff;
            public bool GridOff;
            public bool WoodOff;
            public bool StoneOff;
            public bool GoldOff;
            public bool AnimalsOff;
            public bool DecorativeScatterOff;
            public bool AllResourcesOff;
            public bool StoneOffGoldOff;
            public bool WoodStoneGoldOff;
        }

        static readonly CaseDef[] s_Cases =
        {
            new CaseDef { Id = "baseline_forest" },
            new CaseDef { Id = "minimapTotalOff", MinimapTotalOff = true },
            new CaseDef { Id = "gridOff", GridOff = true },
            new CaseDef { Id = "woodOff", WoodOff = true },
            new CaseDef { Id = "stoneOff", StoneOff = true },
            new CaseDef { Id = "goldOff", GoldOff = true },
            new CaseDef { Id = "animalsOff", AnimalsOff = true },
            new CaseDef { Id = "decorativeScatterOff", DecorativeScatterOff = true },
            new CaseDef { Id = "minimapTotalOff_gridOff", MinimapTotalOff = true, GridOff = true },
            new CaseDef { Id = "stoneOff_goldOff", StoneOffGoldOff = true },
            new CaseDef { Id = "woodOff_stoneOff_goldOff", WoodStoneGoldOff = true },
            new CaseDef { Id = "allResourcesOff", AllResourcesOff = true },
        };

        static readonly CaseDef[] s_PostFixCases =
        {
            new CaseDef { Id = "baseline_forest_postFix" },
            new CaseDef { Id = "minimapTotalOff", MinimapTotalOff = true },
            new CaseDef { Id = "gridOff", GridOff = true },
            new CaseDef { Id = "woodOff", WoodOff = true },
            new CaseDef { Id = "stoneOff", StoneOff = true },
            new CaseDef { Id = "goldOff", GoldOff = true },
            new CaseDef { Id = "animalsOff", AnimalsOff = true },
            new CaseDef { Id = "allResourcesOff", AllResourcesOff = true },
        };

        void Awake()
        {
            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            if (!Application.isPlaying || _running) return;
            if (!autoRunOnPlay) return;
            Debug.Log("[ForestStressTestRunner] start");
            StartCoroutine(RunWhenReadyCo());
        }

        static IEnumerator EditorNextFrame()
        {
#if UNITY_EDITOR
            EditorApplication.QueuePlayerLoopUpdate();
#endif
            yield return null;
        }

        IEnumerator RunWhenReadyCo()
        {
            var startup = new StartupPhaseTracker { WallStartRealtime = Time.realtimeSinceStartup };

            // Esperar a que la escena/juego termine su bootstrap (algunos flows recargan escenas muy rápido).
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 120f)
            {
                startup.RecordFrame();
                if (SceneManager.GetActiveScene().name == "SampleScene")
                {
                    // Esperar a que existan recursos en escena (señal de Generate completado).
                    var nodes = FindObjectsByType<ResourceNode>(FindObjectsSortMode.None);
                    if (nodes != null && nodes.Length > 0)
                        break;
                }
                yield return EditorNextFrame();
            }

            yield return RunCo(startup);
        }

        IEnumerator RunCo(StartupPhaseTracker startup)
        {
            _running = true;
            _outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ForestStressTestResults.txt"));
            try { File.WriteAllText(_outPath, ""); } catch { }

            Emit("[ForestStressTestRunner] begin");
            startup.RecordFrame();
            yield return EditorNextFrame();

            var results = new Dictionary<string, SampleResult>(s_Cases.Length);
            try
            {
                ResolveRefs();

                CaseDef[] suite = postFixBenchmarkSuite ? s_PostFixCases : s_Cases;

                Emit("[ForestStressTestRunner] ready");
                LogRuntimeConfig();
                BuildSnapshots();

                startup.RecordFrame();
                yield return EditorNextFrame();

                Emit("[ForestStressTestRunner] snapshotsBuilt");
                LogCounts(postFixBenchmarkSuite ? "[PostFixCounts]" : "[ForestCounts]");
                startup.RecordFrame();

                float startupSeconds = Time.realtimeSinceStartup - startup.WallStartRealtime;
                float peakFrameMs = startup.PeakDeltaTime > 0f ? startup.PeakDeltaTime * 1000f : 0f;
                Emit(FormatStartupLine(startupSeconds, peakFrameMs));

                yield return WaitUnscaled(Mathf.Max(0f, steadyStateWarmupSeconds));

                for (int i = 0; i < suite.Length; i++)
                {
                    RestoreBaseline();
                    ApplyCase(suite[i]);

                    yield return EditorNextFrame();
                    yield return WaitUnscaled(Mathf.Max(0f, settleTime));

                    SampleResult r = default;
                    yield return SampleSteadyStateCaseCo(Mathf.Max(0.1f, sampleDuration), rr => r = rr);
                    results[suite[i].Id] = r;

                    if (logEveryCase)
                        Emit(FormatPerfLine(suite[i].Id, r, postFixBenchmarkSuite));
                }

                string baselineKey = postFixBenchmarkSuite ? "baseline_forest_postFix" : "baseline_forest";
                if (!results.TryGetValue(baselineKey, out SampleResult baseline))
                    baseline = default;

                if (postFixBenchmarkSuite)
                {
                    Emit($"[PostFixSummary] baseline_forest_postFix avgFps={baseline.AvgFps:F2}");
                    for (int i = 1; i < suite.Length; i++)
                    {
                        var c = suite[i];
                        if (!results.TryGetValue(c.Id, out SampleResult r)) continue;
                        Emit($"[PostFixSummary] {c.Id} deltaFps={(r.AvgFps - baseline.AvgFps):F2}");
                    }
                }
                else
                {
                    Emit($"[ForestPerfSummary] baseline_forest avgFps={baseline.AvgFps:F2}");
                    for (int i = 1; i < suite.Length; i++)
                    {
                        var c = suite[i];
                        if (!results.TryGetValue(c.Id, out SampleResult r)) continue;
                        Emit($"[ForestPerfSummary] {c.Id} deltaFps={(r.AvgFps - baseline.AvgFps):F2}");
                    }
                }
            }
            finally
            {
                RestoreBaseline();
                _running = false;
            }
        }

        void Emit(string line)
        {
            Debug.Log(line);
            try { File.AppendAllText(_outPath, line + Environment.NewLine); } catch { }
        }

        static IEnumerator WaitUnscaled(float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds)
                yield return EditorNextFrame();
        }

        void ResolveRefs()
        {
            _minimap = FindFirstObjectByType<RuntimeMinimapBootstrap>();
            _grid = FindFirstObjectByType<GridGizmoRenderer>();
            _gen = FindFirstObjectByType<RTSMapGenerator>();
        }

        void LogRuntimeConfig()
        {
            if (_gen == null)
            {
                Emit("[ForestRuntimeConfig] RTSMapGenerator not found");
                return;
            }

            float resourceMultiplier = -1f;
            try
            {
                // ApplyMapPreset usa MapPresets.GetPreset(mapPreset) con preset.resourceMultiplier
                var preset = MapPresets.GetPreset(_gen.mapPreset);
                resourceMultiplier = preset.resourceMultiplier;
            }
            catch { }

            Emit(
                "[ForestRuntimeConfig] " +
                $"mapPreset={_gen.mapPreset} " +
                $"width={_gen.width} height={_gen.height} centerAtOrigin={_gen.centerAtOrigin} " +
                $"randomSeedOnPlay={_gen.randomSeedOnPlay} seed={_gen.seed} " +
                $"globalTrees={_gen.globalTrees} globalStone={_gen.globalStone} globalGold={_gen.globalGold} globalAnimals={_gen.globalAnimals} " +
                $"forestClustering={_gen.forestClustering} clusterDensity={_gen.clusterDensity:F3} clusterMinSize={_gen.clusterMinSize} clusterMaxSize={_gen.clusterMaxSize} " +
                $"resourceMultiplier={resourceMultiplier:F3}"
            );
        }

        void BuildSnapshots()
        {
            _woodTargets.Clear();
            _stoneTargets.Clear();
            _goldTargets.Clear();
            _animalTargets.Clear();
            _foodOtherTargets.Clear();
            _decorativeTargets.Clear();
            _wallTargets.Clear();

            var seen = new HashSet<int>();
            var nodes = FindObjectsByType<ResourceNode>(FindObjectsSortMode.None);
            for (int i = 0; i < nodes.Length; i++)
            {
                ResourceNode n = nodes[i];
                if (n == null) continue;
                GameObject go = n.gameObject;
                if (go == null) continue;
                int id = go.GetInstanceID();
                if (!seen.Add(id)) continue;

                switch (n.kind)
                {
                    case ResourceKind.Wood: _woodTargets.Add(new ActiveSnapshot { Go = go, WasActive = go.activeSelf }); break;
                    case ResourceKind.Stone: _stoneTargets.Add(new ActiveSnapshot { Go = go, WasActive = go.activeSelf }); break;
                    case ResourceKind.Gold: _goldTargets.Add(new ActiveSnapshot { Go = go, WasActive = go.activeSelf }); break;
                    case ResourceKind.Food:
                        if (IsAnimalLike(go))
                            _animalTargets.Add(new ActiveSnapshot { Go = go, WasActive = go.activeSelf });
                        else
                            _foodOtherTargets.Add(new ActiveSnapshot { Go = go, WasActive = go.activeSelf });
                        break;
                }
            }

            var scatters = FindObjectsByType<VegetationScatter>(FindObjectsSortMode.None);
            for (int i = 0; i < scatters.Length; i++)
            {
                if (scatters[i] == null) continue;
                Transform root = scatters[i].transform.Find("VegetationScatter");
                if (root == null) continue;
                GameObject go = root.gameObject;
                int id = go.GetInstanceID();
                if (!seen.Add(id)) continue;
                _decorativeTargets.Add(new ActiveSnapshot { Go = go, WasActive = go.activeSelf });
            }

            var walls = FindObjectsByType<CompoundWallSegmentMarker>(FindObjectsSortMode.None);
            for (int i = 0; i < walls.Length; i++)
            {
                if (walls[i] == null) continue;
                GameObject go = walls[i].gameObject;
                int id = go.GetInstanceID();
                if (!seen.Add(id)) continue;
                _wallTargets.Add(new ActiveSnapshot { Go = go, WasActive = go.activeSelf });
            }
        }

        static bool IsAnimalLike(GameObject go)
        {
            if (go == null) return false;
            string n = go.name ?? string.Empty;
            if (n.IndexOf("Cow", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Deer", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (go.GetComponentInChildren<Project.Gameplay.Resources.AnimalPastureBehaviour>(true) != null) return true;
            if (go.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true) != null) return true;
            if (go.GetComponentInChildren<ithappy.Animals_FREE.CreatureMover>(true) != null) return true;
            if (go.GetComponentInChildren<ithappy.Animals_FREE.MovePlayerInput>(true) != null) return true;
            return false;
        }

        void RestoreBaseline()
        {
            if (_minimap != null)
            {
                _minimap.SetBenchmarkMinimapRenderSuppressed(false);
                _minimap.SetBenchmarkIconOverlaySuppressed(false);
            }

            if (_grid != null)
                _grid.SetBenchmarkForceHideGameGrid(false);

            RestoreList(_woodTargets);
            RestoreList(_stoneTargets);
            RestoreList(_goldTargets);
            RestoreList(_animalTargets);
            RestoreList(_foodOtherTargets);
            RestoreList(_decorativeTargets);
            RestoreList(_wallTargets);
        }

        static void RestoreList(List<ActiveSnapshot> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.Go != null) s.Go.SetActive(s.WasActive);
            }
        }

        void ApplyCase(CaseDef c)
        {
            if (c.MinimapTotalOff && _minimap != null)
            {
                _minimap.SetBenchmarkMinimapRenderSuppressed(true);
                _minimap.SetBenchmarkIconOverlaySuppressed(true);
            }

            if (c.GridOff && _grid != null)
                _grid.SetBenchmarkForceHideGameGrid(true);

            if (c.WoodOff)
                SetActiveAll(_woodTargets, false);

            if (c.StoneOff)
                SetActiveAll(_stoneTargets, false);

            if (c.GoldOff)
                SetActiveAll(_goldTargets, false);

            if (c.AnimalsOff)
                SetActiveAll(_animalTargets, false);

            if (c.DecorativeScatterOff)
                SetActiveAll(_decorativeTargets, false);

            if (c.StoneOffGoldOff)
            {
                SetActiveAll(_stoneTargets, false);
                SetActiveAll(_goldTargets, false);
            }

            if (c.WoodStoneGoldOff)
            {
                SetActiveAll(_woodTargets, false);
                SetActiveAll(_stoneTargets, false);
                SetActiveAll(_goldTargets, false);
            }

            if (c.AllResourcesOff)
            {
                SetActiveAll(_woodTargets, false);
                SetActiveAll(_stoneTargets, false);
                SetActiveAll(_goldTargets, false);
                SetActiveAll(_animalTargets, false);
                SetActiveAll(_foodOtherTargets, false);
            }
        }

        static void SetActiveAll(List<ActiveSnapshot> list, bool active)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.Go != null) s.Go.SetActive(active);
            }
        }

        void LogCounts(string prefix)
        {
            int wood = CountActive(_woodTargets);
            int stone = CountActive(_stoneTargets);
            int gold = CountActive(_goldTargets);
            int animals = CountActive(_animalTargets);
            int foodOther = CountActive(_foodOtherTargets);
            int decorative = CountActive(_decorativeTargets);
            int walls = CountActive(_wallTargets);

            int minimapIcons = CountMinimapIcons();

            Emit($"{prefix} wood={wood} stone={stone} gold={gold} food={foodOther + animals} animals={animals} decorative={decorative} walls={walls} minimapIcons={minimapIcons}");
        }

        static int CountActive(List<ActiveSnapshot> list)
        {
            int c = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.Go != null && s.Go.activeInHierarchy) c++;
            }
            return c;
        }

        static int CountMinimapIcons()
        {
            var overlay = GameObject.Find("MinimapIconOverlay");
            if (overlay == null) return -1;
            if (!overlay.activeInHierarchy) return 0;
            return overlay.transform.childCount;
        }

        static void TryReadEditorRenderStats(out int tris, out int verts, out int batches)
        {
            tris = -1;
            verts = -1;
            batches = -1;
#if UNITY_EDITOR
            try
            {
                Type unityStats = typeof(Editor).Assembly.GetType("UnityEditor.UnityStats");
                if (unityStats == null) return;
                tris = GetInt(unityStats, "triangles");
                verts = GetInt(unityStats, "vertices");
                batches = GetInt(unityStats, "drawCalls");
            }
            catch { }
#endif
        }

#if UNITY_EDITOR
        static int GetInt(Type t, string name)
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (p == null) return -1;
            object v = p.GetValue(null, null);
            return v is int i ? i : -1;
        }
#endif

        /// <summary>Fase estable: solo cuenta frames dentro de la ventana de muestreo (sin mezclar arranque).</summary>
        static IEnumerator SampleSteadyStateCaseCo(float duration, Action<SampleResult> onDone)
        {
            duration = Mathf.Max(0.1f, duration);
            int frames = 0;
            float sumDt = 0f;
            float minDt = float.MaxValue;
            float maxDt = 0f;
            var frameFps = new List<float>(512);

            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < duration)
            {
                float dt = Time.unscaledDeltaTime;
                if (dt > 0f)
                {
                    frames++;
                    sumDt += dt;
                    minDt = Mathf.Min(minDt, dt);
                    maxDt = Mathf.Max(maxDt, dt);
                    frameFps.Add(1f / dt);
                }

                yield return EditorNextFrame();
            }

            float avgDt = frames > 0 ? (sumDt / frames) : 0f;
            float avgFps = avgDt > 0f ? (1f / avgDt) : 0f;
            float minFps = maxDt > 0f ? (1f / maxDt) : 0f;
            float maxFps = minDt > 0f ? (1f / minDt) : 0f;
            float medianFps = MedianFromList(frameFps);

            TryReadEditorRenderStats(out int tris, out int verts, out int batches);
            int shadowCasters = -1;

            onDone?.Invoke(new SampleResult
            {
                AvgFps = avgFps,
                MedianFps = medianFps,
                AvgMs = avgDt * 1000f,
                MinFps = minFps,
                MaxFps = maxFps,
                Tris = tris,
                Verts = verts,
                Batches = batches,
                ShadowCasters = shadowCasters
            });
        }

        static float MedianFromList(List<float> values)
        {
            int n = values.Count;
            if (n == 0) return 0f;
            values.Sort();
            if ((n & 1) == 1)
                return values[n / 2];
            return (values[n / 2 - 1] + values[n / 2]) * 0.5f;
        }

        static string FormatStartupLine(float startupSeconds, float startupPeakFrameMs)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[PostFixStartup] case=benchmark_startup | startupSeconds={0:F3} | startupPeakFrameMs={1:F2}",
                startupSeconds, startupPeakFrameMs);
        }

        static string FormatPerfLine(string id, SampleResult s, bool postFix)
        {
            if (postFix)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "[PostFixPerf] case={0} | phase=steady | avgFps={1:F2} | medianFps={2:F2} | minFps={3:F2} | maxFps={4:F2} | avgMs={5:F2} | tris={6} | verts={7} | batches={8} | shadowCasters={9}",
                    id, s.AvgFps, s.MedianFps, s.MinFps, s.MaxFps, s.AvgMs, s.Tris, s.Verts, s.Batches, s.ShadowCasters);
            }

            return string.Format(CultureInfo.InvariantCulture,
                "[ForestPerf] case={0} | phase=steady | avgFps={1:F2} | medianFps={2:F2} | minFps={3:F2} | maxFps={4:F2} | avgMs={5:F2} | tris={6} | verts={7} | batches={8} | shadowCasters={9}",
                id, s.AvgFps, s.MedianFps, s.MinFps, s.MaxFps, s.AvgMs, s.Tris, s.Verts, s.Batches, s.ShadowCasters);
        }
    }
}

