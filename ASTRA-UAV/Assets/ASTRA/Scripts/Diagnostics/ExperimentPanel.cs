using System.Collections.Generic;
using UnityEngine;
using Astra.Core;
using Astra.Perception;

namespace Astra.Diagnostics
{
    /// <summary>
    /// Research / experiment panel (ASTRA spec Sec 60-61, 21). Toggle with F11.
    ///
    /// An interactive panel for the research mode: set or randomise the global scenario seed for
    /// reproducible runs, adjust the LiDAR imperfection parameters, run the planner benchmark, and
    /// keep the last few runs side by side so planners (and seeds) can be compared live. It is a
    /// self-installing overlay and does not touch the operational GCS (Sec 53).
    ///
    /// HONESTY: benchmark figures come from the real planners over a fixed grid fixture; noise and
    /// seed only affect the SIMULATION's pseudo-randomness. Nothing here is a hardware measurement.
    /// </summary>
    [DisallowMultipleComponent]
    public class ExperimentPanel : MonoBehaviour
    {
        private bool _visible = false;
        private string _seedField = ScenarioSeed.Current.ToString();
        private readonly List<string> _runLog = new List<string>();
        private Vector2 _scroll;
        private BenchmarkRunner _runner;

        private GUIStyle _box, _h1, _h2, _row, _btn;
        private bool _styled;
        private Texture2D _bg;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
#if UNITY_2023_1_OR_NEWER
            if (Object.FindAnyObjectByType<ExperimentPanel>() != null) return;
#else
            if (Object.FindObjectOfType<ExperimentPanel>() != null) return;
#endif
            new GameObject("ASTRA_Experiment_Panel").AddComponent<ExperimentPanel>();
        }

        private void Update()
        {
            if (Astra.Mission.MissionSetupManager.IsSetupActive) return;
            if (Input.GetKeyDown(KeyCode.F11)) _visible = !_visible;
        }

        private void InitStyles()
        {
            if (_styled) return;
            _bg = Tex(new Color(0.06f, 0.08f, 0.11f, 0.94f));
            _box = new GUIStyle(GUI.skin.box) { normal = { background = _bg, textColor = Color.white }, padding = new RectOffset(12, 12, 10, 10) };
            _h1 = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.80f, 0.60f) } };
            _h2 = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.86f, 0.90f, 0.95f) } };
            _row = new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = new Color(0.82f, 0.86f, 0.92f) } };
            _btn = new GUIStyle(GUI.skin.button) { fontSize = 11 };
            _styled = true;
        }

        private void OnGUI()
        {
            if (!_visible) return;
            if (Astra.Mission.MissionSetupManager.IsSetupActive) return;
            InitStyles();

            float w = 460f;
            float x = (Screen.width - w) * 0.5f;
            float y = 40f;
            GUILayout.BeginArea(new Rect(x, y, w, 520f), "", _box);

            GUILayout.Label("RESEARCH / EXPERIMENT MODE  ·  F11", _h1);
            GUILayout.Label("SIMULATED. Reproducible via the scenario seed; benchmark uses the real planners.", _row);
            GUILayout.Space(6);

            // ---- Scenario seed ----
            GUILayout.Label("SCENARIO SEED (Sec 21)", _h2);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Current: {ScenarioSeed.Current}", _row, GUILayout.Width(160));
            _seedField = GUILayout.TextField(_seedField, GUILayout.Width(120));
            if (GUILayout.Button("Reuse", _btn, GUILayout.Width(70)))
            {
                if (int.TryParse(_seedField, out int s)) ScenarioSeed.SetSeed(s);
            }
            if (GUILayout.Button("New", _btn, GUILayout.Width(60)))
            {
                _seedField = ScenarioSeed.NewSeed().ToString();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            // ---- LiDAR imperfection controls ----
            LidarSensor lidar = AstraServices.Get<LidarSensor>();
            GUILayout.Label("LiDAR IMPERFECTION MODEL", _h2);
            if (lidar != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Noise σ {lidar.RangeNoiseSigmaM * 100f:F1} cm", _row, GUILayout.Width(120));
                lidar.RangeNoiseSigmaM = GUILayout.HorizontalSlider(lidar.RangeNoiseSigmaM, 0f, 0.25f, GUILayout.Width(160));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Dropout {lidar.DropoutProbability * 100f:F1}%", _row, GUILayout.Width(120));
                lidar.DropoutProbability = GUILayout.HorizontalSlider(lidar.DropoutProbability, 0f, 0.3f, GUILayout.Width(160));
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("No LiDAR sensor registered.", _row);
            }
            GUILayout.Space(6);

            // ---- Planner benchmark ----
            GUILayout.Label("PLANNER BENCHMARK (Sec 60-61)", _h2);
            if (GUILayout.Button("▶ Run benchmark (D* Lite / A* / Dijkstra / Theta*)", _btn, GUILayout.Height(26)))
            {
                RunAndRecord();
            }

            GUILayout.Space(4);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(220f));
            for (int i = _runLog.Count - 1; i >= 0; i--)
            {
                GUILayout.Label(_runLog[i], _row);
            }
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private void RunAndRecord()
        {
            if (_runner == null) _runner = gameObject.AddComponent<BenchmarkRunner>();
            List<BenchmarkRunner.BenchmarkResult> res = _runner.RunBenchmark();

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"--- Run @ seed {ScenarioSeed.Current} ---");
            foreach (var r in res)
            {
                sb.AppendLine($"{r.Algorithm}: plan {r.InitialPlanTimeMs:F2} ms / {r.InitialExpansions} exp, " +
                              $"replan {r.ReplanTimeMs:F2} ms / {r.ReplanExpansions} exp, " +
                              $"len {r.PathLengthM:F1} m, incr {(r.IncrementalSupported ? "yes" : "no")}");
            }
            _runLog.Add(sb.ToString().TrimEnd());
            if (_runLog.Count > 8) _runLog.RemoveAt(0);
        }

        private static Texture2D Tex(Color c)
        {
            Texture2D t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
