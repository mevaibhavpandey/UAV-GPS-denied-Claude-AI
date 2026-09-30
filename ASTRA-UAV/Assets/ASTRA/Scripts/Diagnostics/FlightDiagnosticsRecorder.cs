using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Logging;
using Astra.Flight;
using Astra.Mission;

namespace Astra.Diagnostics
{
    /// <summary>
    /// Flight &amp; navigation diagnostic recorder (ASTRA spec Sec 53-54, 68). Toggle with F12.
    ///
    /// PURPOSE. The reported instability symptoms - uncontrolled descent, poor vertical control,
    /// drift, a mission that does not reach its target - are all RUNTIME behaviours. They cannot be
    /// reproduced or measured by reading the source. This recorder turns an Editor run into data: it
    /// samples the desired-vs-actual quantities of every control loop each physics step, shows them
    /// live, and can write them to a CSV that can be inspected or shared so a fix is made against
    /// measured behaviour rather than a guess.
    ///
    /// WHAT IT MEASURES. For the vertical axis: commanded climb rate vs actual, throttle command,
    /// hover throttle, total thrust vs weight (the single most important number for "why is it
    /// descending"), and the climb-rate PID's P/I/D split and saturation flag. For attitude: target
    /// vs actual roll and pitch and the rate loops' saturation. For navigation: the executive's
    /// desired velocity vs the achieved velocity, distance to the tracked waypoint, cross-track error
    /// and the last decision's cycle time.
    ///
    /// WHAT IT IS NOT. It only reads. It never commands the aircraft, never writes a transform, and
    /// holds no authority - so it cannot change flight behaviour and satisfies the "do not add a
    /// parallel controller / do not mask instability" constraints (Sec 53). It is self-installing and
    /// does not touch the operational GCS.
    ///
    /// HONESTY. Every value is a reading of the SIMULATED model's own state, sampled in the Editor.
    /// Nothing here is a hardware measurement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlightDiagnosticsRecorder : MonoBehaviour
    {
        /// <summary>One sampled instant of the flight/navigation state. All SI, all SIMULATED.</summary>
        public struct Sample
        {
            public float Time;            // seconds since level load
            public string State;          // flight state display name
            public string Source;         // control source

            // Vertical axis
            public float DesiredClimbRate; // m/s, commanded
            public float ActualClimbRate;  // m/s, measured
            public float Throttle;         // [0,1] collective command
            public float HoverThrottle;    // [0,1] derived hover point
            public float ThrustN;          // total thrust, newtons
            public float WeightN;          // mass * g, newtons
            public float ThrustToWeight;   // ThrustN / WeightN
            public float ClimbP, ClimbI, ClimbD;
            public bool ClimbSaturated;

            // Altitude
            public float AltitudeAgl;      // m above launch

            // Attitude
            public float TargetRollDeg, ActualRollDeg;
            public float TargetPitchDeg, ActualPitchDeg;
            public bool RollSaturated, PitchSaturated;

            // Navigation
            public bool NavActive;
            public Vector3 DesiredVelocity;
            public Vector3 ActualVelocity;
            public float DistanceToWaypointM;
            public float CrossTrackErrorM;
            public float DecisionCycleMs;
        }

        private const int HistoryLength = 600; // ~6 s at 100 Hz physics
        private readonly Sample[] _history = new Sample[HistoryLength];
        private int _head;
        private int _count;
        private Sample _latest;
        private bool _haveLatest;

        private FlightControlSystem _fcs;
        private QuadcopterPhysics _physics;
        private AutonomyController _autonomy;
        private Rigidbody _body;

        private bool _visible;
        private bool _recording;
        private StreamWriter _writer;
        private string _csvPath;
        private int _rowsWritten;

        // UI
        private GUIStyle _box, _h1, _h2, _row, _good, _warn, _btn;
        private bool _styled;
        private Texture2D _bg;
        private Vector2 _scroll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
#if UNITY_2023_1_OR_NEWER
            if (FindAnyObjectByType<FlightDiagnosticsRecorder>() != null) return;
#else
            if (FindObjectOfType<FlightDiagnosticsRecorder>() != null) return;
#endif
            new GameObject("ASTRA_Flight_Diagnostics").AddComponent<FlightDiagnosticsRecorder>();
        }

        private void ResolveReferences()
        {
            if (_fcs == null)
            {
                _fcs = AstraServices.Get<IFlightController>() as FlightControlSystem;
#if UNITY_2023_1_OR_NEWER
                if (_fcs == null) _fcs = FindAnyObjectByType<FlightControlSystem>();
#else
                if (_fcs == null) _fcs = FindObjectOfType<FlightControlSystem>();
#endif
            }
            if (_fcs != null)
            {
                if (_physics == null) _physics = _fcs.GetComponent<QuadcopterPhysics>();
                if (_body == null) _body = _fcs.GetComponent<Rigidbody>();
                if (_autonomy == null) _autonomy = _fcs.GetComponent<AutonomyController>();
            }
#if UNITY_2023_1_OR_NEWER
            if (_autonomy == null) _autonomy = FindAnyObjectByType<AutonomyController>();
#else
            if (_autonomy == null) _autonomy = FindObjectOfType<AutonomyController>();
#endif
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F12)) _visible = !_visible;
            // F7 toggles CSV capture whether or not the panel is open, so a run can be recorded
            // hands-off while flying.
            if (Input.GetKeyDown(KeyCode.F7)) ToggleRecording();
        }

        private void FixedUpdate()
        {
            ResolveReferences();
            if (_fcs == null || _physics == null) return;

            Sample s = new Sample();
            s.Time = Time.time;
            s.State = FlightStateInfo.ToDisplayName(_fcs.State);
            s.Source = _fcs.CurrentControlSource.ToString();

            // ---- Vertical axis ----
            s.DesiredClimbRate = _fcs.TargetClimbRateMps;
            s.ActualClimbRate = _physics.VerticalSpeedMps;
            s.Throttle = _fcs.ThrottleCommand;

            UavConfiguration cfg = _physics.Config;
            s.HoverThrottle = cfg != null ? cfg.HoverThrottleFraction : 0f;
            s.ThrustN = _physics.TotalThrustN;
            s.WeightN = cfg != null ? cfg.MassKg * 9.80665f : 0f;
            s.ThrustToWeight = s.WeightN > 0.0001f ? s.ThrustN / s.WeightN : 0f;

            PidController climb = _fcs.ClimbRateLoop;
            if (climb != null)
            {
                s.ClimbP = climb.LastP;
                s.ClimbI = climb.LastI;
                s.ClimbD = climb.LastD;
                s.ClimbSaturated = climb.WasSaturated;
            }

            s.AltitudeAgl = _fcs.AltitudeAboveLaunchM;

            // ---- Attitude ----
            s.TargetRollDeg = _fcs.TargetRollDeg;
            s.ActualRollDeg = _physics.RollDeg;
            s.TargetPitchDeg = _fcs.TargetPitchDeg;
            s.ActualPitchDeg = _physics.PitchDeg;
            if (_fcs.RateRollLoop != null) s.RollSaturated = _fcs.RateRollLoop.WasSaturated;
            if (_fcs.RatePitchLoop != null) s.PitchSaturated = _fcs.RatePitchLoop.WasSaturated;

            // ---- Navigation ----
            s.ActualVelocity = _body != null ? _body.linearVelocity : _physics.Velocity;
            if (_autonomy != null && _autonomy.HasNavInstrumentation &&
                _fcs.CurrentControlSource == ControlSource.Autonomous)
            {
                s.NavActive = true;
                s.DesiredVelocity = _autonomy.LastDesiredVelocity;
                s.CrossTrackErrorM = _autonomy.CrossTrackErrorM;
                s.DistanceToWaypointM = _autonomy.LastDecision.DistanceToWaypointM;
                s.DecisionCycleMs = _autonomy.LastDecision.CycleTimeMs;
            }

            _latest = s;
            _haveLatest = true;

            _history[_head] = s;
            _head = (_head + 1) % HistoryLength;
            if (_count < HistoryLength) _count++;

            if (_recording && _writer != null) WriteRow(s);
        }

        // ====================================================================================
        // CSV CAPTURE
        // ====================================================================================

        private void ToggleRecording()
        {
            if (_recording) StopRecording();
            else StartRecording();
        }

        private void StartRecording()
        {
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "AstraDiagnostics");
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                _csvPath = Path.Combine(dir, "flight_" + stamp + ".csv");
                _writer = new StreamWriter(_csvPath, false, Encoding.UTF8);
                _writer.WriteLine(
                    "time_s,state,source,des_climb_mps,act_climb_mps,throttle,hover_throttle," +
                    "thrust_N,weight_N,thrust_to_weight,climb_P,climb_I,climb_D,climb_sat," +
                    "alt_agl_m,tgt_roll_deg,act_roll_deg,tgt_pitch_deg,act_pitch_deg," +
                    "roll_sat,pitch_sat,nav_active,des_vx,des_vy,des_vz,act_vx,act_vy,act_vz," +
                    "dist_to_wp_m,cross_track_m,decision_ms");
                _rowsWritten = 0;
                _recording = true;
                EventLog.Success(LogSource.System, "Flight diagnostics: recording to " + _csvPath);
                Debug.Log("[ASTRA] Flight diagnostics CSV: " + _csvPath);
            }
            catch (Exception e)
            {
                _recording = false;
                _writer = null;
                EventLog.Error(LogSource.System, "Flight diagnostics: could not open CSV - " + e.Message);
            }
        }

        private void StopRecording()
        {
            _recording = false;
            if (_writer != null)
            {
                try { _writer.Flush(); _writer.Dispose(); }
                catch (Exception) { /* nothing useful to do on close failure */ }
                _writer = null;
                EventLog.Info(LogSource.System, string.Format(
                    "Flight diagnostics: recording stopped ({0} rows) - {1}", _rowsWritten, _csvPath));
            }
        }

        private void WriteRow(Sample s)
        {
            try
            {
                var c = CultureInfo.InvariantCulture;
                _writer.WriteLine(string.Join(",", new string[]
                {
                    s.Time.ToString("F3", c), s.State, s.Source,
                    s.DesiredClimbRate.ToString("F3", c), s.ActualClimbRate.ToString("F3", c),
                    s.Throttle.ToString("F3", c), s.HoverThrottle.ToString("F3", c),
                    s.ThrustN.ToString("F2", c), s.WeightN.ToString("F2", c),
                    s.ThrustToWeight.ToString("F3", c),
                    s.ClimbP.ToString("F3", c), s.ClimbI.ToString("F3", c), s.ClimbD.ToString("F3", c),
                    s.ClimbSaturated ? "1" : "0",
                    s.AltitudeAgl.ToString("F2", c),
                    s.TargetRollDeg.ToString("F2", c), s.ActualRollDeg.ToString("F2", c),
                    s.TargetPitchDeg.ToString("F2", c), s.ActualPitchDeg.ToString("F2", c),
                    s.RollSaturated ? "1" : "0", s.PitchSaturated ? "1" : "0",
                    s.NavActive ? "1" : "0",
                    s.DesiredVelocity.x.ToString("F3", c), s.DesiredVelocity.y.ToString("F3", c),
                    s.DesiredVelocity.z.ToString("F3", c),
                    s.ActualVelocity.x.ToString("F3", c), s.ActualVelocity.y.ToString("F3", c),
                    s.ActualVelocity.z.ToString("F3", c),
                    s.DistanceToWaypointM.ToString("F2", c), s.CrossTrackErrorM.ToString("F2", c),
                    s.DecisionCycleMs.ToString("F2", c)
                }));
                _rowsWritten++;
            }
            catch (Exception e)
            {
                EventLog.Error(LogSource.System, "Flight diagnostics: CSV write failed - " + e.Message);
                StopRecording();
            }
        }

        private void OnDisable()
        {
            if (_recording) StopRecording();
        }

        // ====================================================================================
        // OVERLAY
        // ====================================================================================

        private void InitStyles()
        {
            if (_styled) return;
            _bg = Tex(new Color(0.06f, 0.08f, 0.11f, 0.94f));
            _box = new GUIStyle(GUI.skin.box)
            { normal = { background = _bg, textColor = Color.white }, padding = new RectOffset(12, 12, 10, 10) };
            _h1 = new GUIStyle(GUI.skin.label)
            { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.80f, 0.95f) } };
            _h2 = new GUIStyle(GUI.skin.label)
            { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.86f, 0.90f, 0.95f) } };
            _row = new GUIStyle(GUI.skin.label)
            { fontSize = 10, normal = { textColor = new Color(0.82f, 0.86f, 0.92f) } };
            _good = new GUIStyle(_row) { normal = { textColor = new Color(0.45f, 0.85f, 0.55f) } };
            _warn = new GUIStyle(_row) { normal = { textColor = new Color(0.96f, 0.70f, 0.30f) } };
            _btn = new GUIStyle(GUI.skin.button) { fontSize = 11 };
            _styled = true;
        }

        private void OnGUI()
        {
            if (!_visible) return;
            if (Astra.Mission.MissionSetupManager.IsSetupActive) return;
            InitStyles();

            float w = 430f;
            float x = Screen.width - w - 16f;
            float y = 40f;
            GUILayout.BeginArea(new Rect(x, y, w, 560f), "", _box);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("FLIGHT & NAVIGATION DIAGNOSTICS  ·  F12", _h1);
            GUILayout.Label("SIMULATED. Read-only telemetry; holds no control authority.", _row);
            GUILayout.Space(4);

            // ---- Recording controls ----
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_recording ? "STOP RECORDING (F7)" : "RECORD CSV (F7)", _btn))
            {
                ToggleRecording();
            }
            GUILayout.EndHorizontal();
            if (_recording)
            {
                GUILayout.Label(string.Format("● recording · {0} rows", _rowsWritten), _warn);
                GUILayout.Label(_csvPath, _row);
            }
            else if (!string.IsNullOrEmpty(_csvPath))
            {
                GUILayout.Label("last file: " + _csvPath, _row);
            }
            GUILayout.Space(6);

            if (!_haveLatest)
            {
                GUILayout.Label("Waiting for the flight controller...", _row);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            Sample s = _latest;

            GUILayout.Label(string.Format("STATE {0}   ·   SOURCE {1}", s.State, s.Source), _h2);
            GUILayout.Space(4);

            // ---- Vertical axis: the descent-diagnosis block ----
            GUILayout.Label("VERTICAL AXIS", _h2);
            Row("Climb rate desired / actual", string.Format("{0,7:F2} / {1,6:F2} m/s",
                s.DesiredClimbRate, s.ActualClimbRate),
                Mathf.Abs(s.DesiredClimbRate - s.ActualClimbRate) < 0.5f);
            Row("Altitude AGL", string.Format("{0:F2} m", s.AltitudeAgl), true);
            Row("Throttle cmd / hover", string.Format("{0:F3} / {1:F3}", s.Throttle, s.HoverThrottle), true);
            Row("Thrust / weight", string.Format("{0:F1} N / {1:F1} N  (T/W {2:F2})",
                s.ThrustN, s.WeightN, s.ThrustToWeight), s.ThrustToWeight > 0.98f || s.ActualClimbRate >= -0.1f);
            Row("Climb PID P / I / D", string.Format("{0:F3} / {1:F3} / {2:F3}",
                s.ClimbP, s.ClimbI, s.ClimbD), true);
            Row("Climb loop saturated", s.ClimbSaturated ? "YES" : "no", !s.ClimbSaturated);
            GUILayout.Space(6);

            // ---- Attitude ----
            GUILayout.Label("ATTITUDE (deg)", _h2);
            Row("Roll  target / actual", string.Format("{0,6:F1} / {1,6:F1}",
                s.TargetRollDeg, s.ActualRollDeg), Mathf.Abs(s.TargetRollDeg - s.ActualRollDeg) < 5f);
            Row("Pitch target / actual", string.Format("{0,6:F1} / {1,6:F1}",
                s.TargetPitchDeg, s.ActualPitchDeg), Mathf.Abs(s.TargetPitchDeg - s.ActualPitchDeg) < 5f);
            Row("Rate loops saturated", string.Format("roll {0} · pitch {1}",
                s.RollSaturated ? "YES" : "no", s.PitchSaturated ? "YES" : "no"),
                !s.RollSaturated && !s.PitchSaturated);
            GUILayout.Space(6);

            // ---- Navigation ----
            GUILayout.Label("NAVIGATION", _h2);
            if (s.NavActive)
            {
                Row("Velocity desired", string.Format("({0:F1}, {1:F1}, {2:F1}) |{3:F2}| m/s",
                    s.DesiredVelocity.x, s.DesiredVelocity.y, s.DesiredVelocity.z, s.DesiredVelocity.magnitude), true);
                Row("Velocity actual", string.Format("({0:F1}, {1:F1}, {2:F1}) |{3:F2}| m/s",
                    s.ActualVelocity.x, s.ActualVelocity.y, s.ActualVelocity.z, s.ActualVelocity.magnitude), true);
                Row("Speed tracking error", string.Format("{0:F2} m/s",
                    (s.DesiredVelocity - s.ActualVelocity).magnitude),
                    (s.DesiredVelocity - s.ActualVelocity).magnitude < 2f);
                Row("Distance to waypoint", string.Format("{0:F1} m", s.DistanceToWaypointM), true);
                Row("Cross-track error", string.Format("{0:F2} m", s.CrossTrackErrorM),
                    Mathf.Abs(s.CrossTrackErrorM) < 3f);
                Row("Decision cycle", string.Format("{0:F2} ms", s.DecisionCycleMs), s.DecisionCycleMs < 20f);
            }
            else
            {
                GUILayout.Label("autonomy not commanding (manual or idle)", _row);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void Row(string label, string value, bool ok)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _row, GUILayout.Width(200));
            GUILayout.Label(value, ok ? _good : _warn);
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


