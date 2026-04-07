using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Project.Gameplay.Buildings;
using Project.Gameplay.Environment;
using Project.Gameplay.Map;
using Project.Gameplay.Resources;
using Project.UI;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Project.Core.Benchmark
{
    /// <summary>
    /// Benchmark reproducible en Play: escenarios A–J con restauración al terminar.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class RuntimePerformanceBenchmark : MonoBehaviour
    {
        [Header("Ejecución")]
        public bool autoRunOnPlay;
        [Tooltip("Espera antes de cada caso (estabilizar).")]
        public float settleTime = 1.5f;
        [Tooltip("Duración de muestreo por caso.")]
        public float sampleDuration = 5f;
        public bool logEveryCase = true;

        bool _running;
        bool _snapshotsBuilt;
        SampleResult _scratchSample;

        readonly List<ActiveSnapshot> _treeTargets = new List<ActiveSnapshot>(256);
        readonly List<ActiveSnapshot> _wallTargets = new List<ActiveSnapshot>(256);

        RuntimeMinimapBootstrap _minimap;
        GridGizmoRenderer _grid;
        Camera _minimapCam;

        struct ActiveSnapshot
        {
            public GameObject Go;
            public bool WasActive;
        }

        struct CaseDef
        {
            public string Id;
            public string SummaryDeltaKey;
            public bool MinimapBaseOff;
            public bool MinimapIconsOff;
            public bool GridOff;
            public bool TreesOff;
            public bool WallsOff;
        }

        static readonly CaseDef[] s_Cases =
        {
            new CaseDef { Id = "baseline", SummaryDeltaKey = null },
            new CaseDef { Id = "minimapBaseOff", SummaryDeltaKey = "minimapBaseOff", MinimapBaseOff = true },
            new CaseDef { Id = "minimapIconsOff", SummaryDeltaKey = "minimapIconsOff", MinimapIconsOff = true },
            new CaseDef { Id = "minimapTotalOff", SummaryDeltaKey = "minimapTotalOff", MinimapBaseOff = true, MinimapIconsOff = true },
            new CaseDef { Id = "gridOff", SummaryDeltaKey = "gridOff", GridOff = true },
            new CaseDef { Id = "treesOff", SummaryDeltaKey = "treesOff", TreesOff = true },
            new CaseDef { Id = "wallsOff", SummaryDeltaKey = "wallsOff", WallsOff = true },
            new CaseDef { Id = "gridOff_minimapTotalOff", SummaryDeltaKey = "gridOff+minimapTotalOff", GridOff = true, MinimapBaseOff = true, MinimapIconsOff = true },
            new CaseDef { Id = "treesOff_wallsOff", SummaryDeltaKey = "treesOff+wallsOff", TreesOff = true, WallsOff = true },
            new CaseDef { Id = "gridOff_minimapTotalOff_treesOff", SummaryDeltaKey = "gridOff+minimapTotalOff+treesOff", GridOff = true, MinimapBaseOff = true, MinimapIconsOff = true, TreesOff = true },
        };

        struct SampleResult
        {
            public float AvgFps;
            public float AvgMs;
            public float MinFps;
            public float MaxFps;
            public int Tris;
            public int Verts;
            public int Batches;
        }

        void Start()
        {
            if (autoRunOnPlay && Application.isPlaying)
                RunBenchmark();
        }

        [ContextMenu("Run Benchmark")]
        public void RunBenchmark()
        {
            if (!Application.isPlaying || _running) return;
            StartCoroutine(RunBenchmarkCo());
        }

        IEnumerator RunBenchmarkCo()
        {
            _running = true;
            var results = new Dictionary<string, SampleResult>(s_Cases.Length);
            try
            {
                ResolveRefs();
                BuildSnapshotsIfNeeded();
                for (int i = 0; i < s_Cases.Length; i++)
                {
                    CaseDef c = s_Cases[i];
                    RestoreBaseline();
                    ApplyCase(c);
                    yield return null;
                    float wait = Mathf.Max(0f, settleTime);
                    float w0 = Time.unscaledTime;
                    while (Time.unscaledTime - w0 < wait)
                        yield return null;

                    yield return SampleCaseCo();
                    results[c.Id] = _scratchSample;

                    if (logEveryCase)
                        LogPerfTestLine(c.Id, _scratchSample);
                }

                if (!results.TryGetValue("baseline", out SampleResult baseline))
                    baseline = default;

                Debug.Log($"[PerfSummary] baseline avgFps={baseline.AvgFps:F2}");
                for (int i = 1; i < s_Cases.Length; i++)
                {
                    CaseDef c = s_Cases[i];
                    if (string.IsNullOrEmpty(c.SummaryDeltaKey)) continue;
                    if (!results.TryGetValue(c.Id, out SampleResult r)) continue;
                    Debug.Log($"[PerfSummary] {c.SummaryDeltaKey} deltaFps={(r.AvgFps - baseline.AvgFps):F2}");
                }
            }
            finally
            {
                RestoreBaseline();
                _running = false;
            }
        }

        void ResolveRefs()
        {
            _minimap = FindFirstObjectByType<RuntimeMinimapBootstrap>();
            _grid = FindFirstObjectByType<GridGizmoRenderer>();
            var camGo = GameObject.Find("MinimapCamera_RT");
            _minimapCam = camGo != null ? camGo.GetComponent<Camera>() : null;
        }

        void BuildSnapshotsIfNeeded()
        {
            if (_snapshotsBuilt) return;
            _snapshotsBuilt = true;
            _treeTargets.Clear();
            _wallTargets.Clear();

            var seenTree = new HashSet<int>();
            var nodes = FindObjectsByType<ResourceNode>(FindObjectsSortMode.None);
            for (int i = 0; i < nodes.Length; i++)
            {
                ResourceNode n = nodes[i];
                if (n == null || n.kind != ResourceKind.Wood) continue;
                GameObject go = n.gameObject;
                int id = go.GetInstanceID();
                if (!seenTree.Add(id)) continue;
                _treeTargets.Add(new ActiveSnapshot { Go = go, WasActive = go.activeSelf });
            }

            var scatters = FindObjectsByType<VegetationScatter>(FindObjectsSortMode.None);
            for (int i = 0; i < scatters.Length; i++)
            {
                if (scatters[i] == null) continue;
                Transform root = scatters[i].transform.Find("VegetationScatter");
                if (root == null) continue;
                GameObject go = root.gameObject;
                int id = go.GetInstanceID();
                if (!seenTree.Add(id)) continue;
                _treeTargets.Add(new ActiveSnapshot { Go = go, WasActive = go.activeSelf });
            }

            var walls = FindObjectsByType<CompoundWallSegmentMarker>(FindObjectsSortMode.None);
            var seenWall = new HashSet<int>();
            for (int i = 0; i < walls.Length; i++)
            {
                if (walls[i] == null) continue;
                GameObject go = walls[i].gameObject;
                int id = go.GetInstanceID();
                if (!seenWall.Add(id)) continue;
                _wallTargets.Add(new ActiveSnapshot { Go = go, WasActive = go.activeSelf });
            }
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

            for (int i = 0; i < _treeTargets.Count; i++)
            {
                ActiveSnapshot s = _treeTargets[i];
                if (s.Go != null) s.Go.SetActive(s.WasActive);
            }

            for (int i = 0; i < _wallTargets.Count; i++)
            {
                ActiveSnapshot s = _wallTargets[i];
                if (s.Go != null) s.Go.SetActive(s.WasActive);
            }
        }

        void ApplyCase(in CaseDef c)
        {
            if (c.MinimapBaseOff || c.MinimapIconsOff)
            {
                if (_minimap == null)
                    Debug.LogWarning("[PerfTest] Minimap: RuntimeMinimapBootstrap no encontrado; toggles minimapa sin efecto.");
                else
                {
                    _minimap.SetBenchmarkMinimapRenderSuppressed(c.MinimapBaseOff);
                    _minimap.SetBenchmarkIconOverlaySuppressed(c.MinimapIconsOff);
                }
            }

            if (c.GridOff)
            {
                if (_grid == null)
                    Debug.LogWarning("[PerfTest] Grid: GridGizmoRenderer no encontrado; gridOff sin efecto.");
                else
                    _grid.SetBenchmarkForceHideGameGrid(true);
            }

            if (c.TreesOff)
            {
                if (_treeTargets.Count == 0)
                    Debug.LogWarning("[PerfTest] treesOff: sin nodos Wood ni VegetationScatter; caso sin efecto.");
                for (int i = 0; i < _treeTargets.Count; i++)
                    if (_treeTargets[i].Go != null)
                        _treeTargets[i].Go.SetActive(false);
            }

            if (c.WallsOff)
            {
                if (_wallTargets.Count == 0)
                    Debug.LogWarning("[PerfTest] wallsOff: sin CompoundWallSegmentMarker; caso sin efecto.");
                for (int i = 0; i < _wallTargets.Count; i++)
                    if (_wallTargets[i].Go != null)
                        _wallTargets[i].Go.SetActive(false);
            }
        }

        IEnumerator SampleCaseCo()
        {
            float tEnd = Time.unscaledTime + Mathf.Max(0.1f, sampleDuration);
            int frames = 0;
            double sumDt = 0.0;
            float minFps = float.MaxValue;
            float maxFps = 0f;
            float tStart = Time.unscaledTime;

            while (Time.unscaledTime < tEnd)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                if (dt < 1e-8f) continue;
                frames++;
                sumDt += dt;
                float fps = 1f / dt;
                if (fps < minFps) minFps = fps;
                if (fps > maxFps) maxFps = fps;
            }

            float elapsed = Mathf.Max(1e-4f, Time.unscaledTime - tStart);
            float avgFps = frames / elapsed;
            float avgMs = frames > 0 ? (float)(sumDt / frames) * 1000f : 0f;
            if (minFps > 99999f) minFps = 0f;

            TryReadEditorRenderStats(out int tris, out int verts, out int batches);

            _scratchSample = new SampleResult
            {
                AvgFps = avgFps,
                AvgMs = avgMs,
                MinFps = minFps,
                MaxFps = maxFps,
                Tris = tris,
                Verts = verts,
                Batches = batches
            };
        }

        static void TryReadEditorRenderStats(out int tris, out int verts, out int batches)
        {
            tris = verts = batches = -1;
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
            try
            {
                Type t = typeof(Editor).Assembly.GetType("UnityEditor.UnityStats");
                if (t == null) return;
                const BindingFlags bf = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var triProp = t.GetProperty("triangles", bf) ?? t.GetProperty("tris", bf);
                var vertProp = t.GetProperty("vertices", bf) ?? t.GetProperty("verts", bf);
                var batchProp = t.GetProperty("batches", bf) ?? t.GetProperty("drawCalls", bf);
                if (triProp != null) tris = Convert.ToInt32(triProp.GetValue(null));
                if (vertProp != null) verts = Convert.ToInt32(vertProp.GetValue(null));
                if (batchProp != null) batches = Convert.ToInt32(batchProp.GetValue(null));
            }
            catch
            {
                tris = verts = batches = -1;
            }
#endif
        }

        static void LogPerfTestLine(string caseId, in SampleResult s)
        {
            string ts = s.Tris >= 0 ? s.Tris.ToString() : "n/a";
            string vs = s.Verts >= 0 ? s.Verts.ToString() : "n/a";
            string bs = s.Batches >= 0 ? s.Batches.ToString() : "n/a";
            Debug.Log($"[PerfTest] case={caseId} | avgFps={s.AvgFps:F2} | avgMs={s.AvgMs:F3} | minFps={s.MinFps:F2} | maxFps={s.MaxFps:F2} | tris={ts} | verts={vs} | batches={bs}");
        }
    }
}
