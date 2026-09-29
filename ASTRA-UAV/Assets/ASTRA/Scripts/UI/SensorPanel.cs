using System.Text;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Perception;

namespace Astra.UI
{
    /// <summary>
    /// Sensor &amp; Fusion overlay panel (supports ASTRA spec Sec 13-16). Toggle with F10.
    ///
    /// A read-only IMGUI overlay that surfaces the dedicated LiDAR sensor's live statistics (D3) and
    /// the sensor-fusion aggregator's per-channel contribution status (D6). It is a separate,
    /// self-installing overlay rather than an edit to the working GCS, so the operational GCS panels
    /// are left untouched (Sec 53). It reads services via the locator and renders; it never mutates
    /// state. Everything is badged with its provenance so nothing reads as more real than it is.
    /// </summary>
    [DisallowMultipleComponent]
    public class SensorPanel : MonoBehaviour
    {
        private bool _visible = true;
        private GUIStyle _box, _h1, _h2, _row, _badge;
        private bool _styled;
        private Texture2D _bg;
        private readonly StringBuilder _sb = new StringBuilder(256);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
#if UNITY_2023_1_OR_NEWER
            if (Object.FindAnyObjectByType<SensorPanel>() != null) return;
#else
            if (Object.FindObjectOfType<SensorPanel>() != null) return;
#endif
            new GameObject("ASTRA_Sensor_Panel").AddComponent<SensorPanel>();
        }

        private void Update()
        {
            if (Astra.Mission.MissionSetupManager.IsSetupActive) return;
            if (Input.GetKeyDown(KeyCode.F10)) _visible = !_visible;
        }

        private void InitStyles()
        {
            if (_styled) return;
            _bg = Tex(new Color(0.06f, 0.08f, 0.11f, 0.92f));
            _box = new GUIStyle(GUI.skin.box) { normal = { background = _bg, textColor = Color.white }, padding = new RectOffset(12, 12, 10, 10) };
            _h1 = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.40f, 0.80f, 0.98f) } };
            _h2 = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.86f, 0.90f, 0.95f) } };
            _row = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(0.82f, 0.86f, 0.92f) } };
            _badge = new GUIStyle(GUI.skin.label) { fontSize = 9, fontStyle = FontStyle.Bold };
            _styled = true;
        }

        private void OnGUI()
        {
            if (!_visible) return;
            if (Astra.Mission.MissionSetupManager.IsSetupActive) return;
            InitStyles();

            float w = 320f;
            float x = Screen.width - w - 12f;
            float y = 12f;
            GUILayout.BeginArea(new Rect(x, y, w, 420f), "", _box);

            GUILayout.Label("SENSORS & FUSION  ·  F10", _h1);

            LidarSensor lidar = AstraServices.Get<LidarSensor>();
            GUILayout.Space(4);
            GUILayout.Label("LiDAR (SIMULATED)", _h2);
            if (lidar != null)
            {
                GUILayout.Label($"Config: {lidar.VerticalChannels}ch × {lidar.HorizontalResolution} @ {lidar.ScanRateHz:F0} Hz", _row);
                GUILayout.Label($"Range {lidar.MaxRangeM:F0} m · HFOV {lidar.HorizontalFovDeg:F0}° · VFOV {lidar.VerticalFovDeg:F0}°", _row);
                GUILayout.Label($"Points {lidar.LastPointCount} / {lidar.LastBeamsCast} beams ({lidar.LastReturnRatePercent:F0}% return)", _row);
                GUILayout.Label($"Nearest {(lidar.NearestReturnM >= 0 ? lidar.NearestReturnM.ToString("F1") + " m" : "—")} · scan {lidar.LastScanDurationMs:F2} ms", _row);
            }
            else
            {
                GUILayout.Label("LiDAR sensor not present.", _row);
            }

            var fusion = AstraServices.Get<Astra.Sensors.SensorFusionManager>();
            GUILayout.Space(8);
            GUILayout.Label("SENSOR FUSION (status aggregator)", _h2);
            if (fusion != null)
            {
                GUILayout.Label($"Estimator: {fusion.ActiveEstimator} · {fusion.ContributingChannelCount} channels active", _row);
                DrawChannel(fusion.Imu);
                DrawChannel(fusion.Gnss);
                DrawChannel(fusion.Barometer);
                DrawChannel(fusion.Magnetometer);
                DrawChannel(fusion.Lidar);
                DrawChannel(fusion.Camera);
                GUILayout.Space(4);
                GUILayout.Label("Fusion is a status view; estimation stays in the localization provider.",
                    new GUIStyle(GUI.skin.label) { fontSize = 9, fontStyle = FontStyle.Italic, wordWrap = true, normal = { textColor = new Color(0.62f, 0.66f, 0.72f) } });
            }
            else
            {
                GUILayout.Label("Fusion aggregator not present.", _row);
            }

            GUILayout.EndArea();
        }

        private void DrawChannel(Astra.Sensors.SensorFusionManager.SensorChannel ch)
        {
            if (string.IsNullOrEmpty(ch.Name)) return;
            GUILayout.BeginHorizontal();
            Color dot = ch.Contributing ? new Color(0.35f, 0.85f, 0.50f)
                      : ch.Status == SubsystemStatus.Offline ? new Color(0.55f, 0.55f, 0.60f)
                      : new Color(0.98f, 0.71f, 0.20f);
            GUIStyle s = new GUIStyle(_row) { normal = { textColor = dot } };
            GUILayout.Label(ch.Contributing ? "●" : "○", s, GUILayout.Width(14));
            GUILayout.Label($"{ch.Name}: {ch.Detail}", _row);
            GUILayout.EndHorizontal();
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
