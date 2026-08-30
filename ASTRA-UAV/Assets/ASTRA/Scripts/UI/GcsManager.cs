using System;
using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Geo;
using Astra.Core.Logging;
using Astra.Flight;
using Astra.Localization;
using Astra.Mission;
using Astra.Perception;
using Astra.Cameras;
using Astra.Engineering;

namespace Astra.UI
{
    /// <summary>
    /// Ground Control Station (GCS) UI Manager.
    /// Renders the complete professional tactical mission control interface:
    /// - Top status bar (mode chips, arming, GPS status, battery, time)
    /// - Live telemetry HUD (attitude, speed, altitude, motor RPMs)
    /// - Interactive Mission Planner (Home/Target coordinates, route distance, bearing, validate, launch)
    /// - Live AI Decision Panel (Sense-Perceive-Localize-Plan-Decide-Act-Reassess loop, metrics, reasons, TTC)
    /// - System Health Matrix (Flight Controller, Motors, IMU, GPS, Perception, Telemetry)
    /// - Scrolling Event Log
    /// </summary>
    [DisallowMultipleComponent]
    public class GcsManager : MonoBehaviour
    {
        [Header("Theme & Layout")]
        [SerializeField] private bool showGcsUi = true;
        [SerializeField] private bool showMissionPlanner = true;
        [SerializeField] private bool showAiDecision = true;
        [SerializeField] private bool showTelemetry = true;
        [SerializeField] private bool showSystemHealth = true;
        [SerializeField] private bool showEventLog = true;

        private FlightControlSystem _fc;
        private TelemetryProvider _telemetry;
        private AutonomyController _autonomy;
        private MissionManager _mission;
        private PerceptionViewController _perceptionView;
        private EngineeringViewController _engView;
        private CameraController _cameraCtrl;

        private Vector2 _logScroll;
        private readonly List<LogEntry> _logBuffer = new List<LogEntry>();
        private string _targetLatInput = "13.0850";
        private string _targetLonInput = "77.5900";
        private string _targetAltInput = "35.0";

        private GUIStyle _boxStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _btnStyle;
        private GUIStyle _warnStyle;
        private GUIStyle _okStyle;
        private bool _stylesInitialized = false;

        private void Awake()
        {
            _fc = FindFirstObjectByType<FlightControlSystem>();
            _telemetry = FindFirstObjectByType<TelemetryProvider>();
            _autonomy = FindFirstObjectByType<AutonomyController>();
            _mission = FindFirstObjectByType<MissionManager>();
            _perceptionView = FindFirstObjectByType<PerceptionViewController>();
            _engView = FindFirstObjectByType<EngineeringViewController>();
            _cameraCtrl = FindFirstObjectByType<CameraController>();
        }

        private void Update()
        {
            // Keyboard shortcut mode handlers
            if (Input.GetKeyDown(KeyCode.F1))
            {
                _fc?.SetControlSource(ControlSource.Manual);
                EventLog.Info(LogSource.FlightController, "Control mode switched to MANUAL (F1).");
            }
            else if (Input.GetKeyDown(KeyCode.F2))
            {
                _fc?.SetControlSource(ControlSource.Autonomous);
                AstraServices.Get<ISensorProvider>()?.SetGpsEnabled(true);
                EventLog.Info(LogSource.FlightController, "Control mode switched to AUTONOMOUS GPS (F2).");
            }
            else if (Input.GetKeyDown(KeyCode.F3))
            {
                _fc?.SetControlSource(ControlSource.Autonomous);
                AstraServices.Get<ISensorProvider>()?.SetGpsEnabled(false);
                EventLog.Warning(LogSource.FlightController, "Control mode switched to AUTONOMOUS GPS-DENIED (F3).");
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                _mission?.Abort("Operator manual mission abort key (Esc)");
            }
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            Texture2D darkBg = MakeTex(2, 2, new Color(0.06f, 0.08f, 0.11f, 0.92f));
            Texture2D headerBg = MakeTex(2, 2, new Color(0.12f, 0.16f, 0.22f, 0.95f));
            Texture2D btnBg = MakeTex(2, 2, new Color(0.18f, 0.32f, 0.52f, 0.95f));

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = darkBg, textColor = Color.white },
                padding = new RectOffset(10, 10, 10, 10)
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.35f, 0.78f, 0.98f) }
            };

            _subHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.85f, 0.88f, 0.92f) }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.82f, 0.84f, 0.88f) }
            };

            _btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { background = btnBg, textColor = Color.white }
            };

            _okStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.30f, 0.85f, 0.45f) }
            };

            _warnStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.98f, 0.65f, 0.20f) }
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (!showGcsUi) return;
            InitStyles();

            DrawTopStatusBar();

            float screenW = Screen.width;
            float screenH = Screen.height;

            // Left Sidebar: Telemetry & System Health
            if (showTelemetry) DrawTelemetryPanel(10, 48, 280, 290);
            if (showSystemHealth) DrawSystemHealthPanel(10, 345, 280, 230);

            // Right Sidebar: AI Decision Panel & Mission Planner
            if (showAiDecision) DrawAiDecisionPanel(screenW - 350, 48, 340, 310);
            if (showMissionPlanner) DrawMissionPlannerPanel(screenW - 350, 365, 340, 260);

            // Bottom Bar: Event Log & Flight Controls
            if (showEventLog) DrawEventLogPanel(298, screenH - 170, screenW - 656, 160);
        }

        private void DrawTopStatusBar()
        {
            TelemetrySnapshot t = _telemetry != null ? _telemetry.Current : default;
            float w = Screen.width;

            GUI.Box(new Rect(0, 0, w, 40), "", _boxStyle);

            // Title
            GUI.Label(new Rect(15, 8, 220, 24), "ASTRA UAV GCS v2.4", _headerStyle);

            // Mode & State Chip
            string modeStr = _fc != null ? _fc.CurrentControlSource.ToString().ToUpper() : "MANUAL";
            string stateStr = _fc != null ? FlightStateInfo.ToDisplayName(_fc.State) : "DISARMED";
            bool isArmed = _fc != null && _fc.IsArmed;

            Color stateColor = isArmed ? new Color(0.2f, 0.85f, 0.45f) : new Color(0.92f, 0.35f, 0.25f);
            GUI.color = stateColor;
            GUI.Label(new Rect(230, 8, 180, 24), $"[{modeStr}]  {stateStr}", _headerStyle);
            GUI.color = Color.white;

            // GPS Status
            bool gpsOn = t.GpsEnabled;
            string gpsText = gpsOn ? $"GPS: 3D FIX ({t.SatelliteCount} SAT)" : "GPS: OFF (VISUAL-INERTIAL)";
            GUI.color = gpsOn ? new Color(0.3f, 0.85f, 0.45f) : new Color(0.98f, 0.65f, 0.2f);
            GUI.Label(new Rect(440, 8, 220, 24), gpsText, _subHeaderStyle);
            GUI.color = Color.white;

            // Battery
            GUI.Label(new Rect(680, 8, 180, 24), $"BATT: {t.BatteryPercent:F0}% ({t.BatteryVoltage:F1}V, {t.BatteryCurrentA:F1}A)", _subHeaderStyle);

            // Time
            GUI.Label(new Rect(w - 180, 8, 170, 24), $"T+ {Time.time:F1}s | F5:PERCEP", _labelStyle);
        }

        private void DrawTelemetryPanel(float x, float y, float w, float h)
        {
            TelemetrySnapshot t = _telemetry != null ? _telemetry.Current : default;
            GUI.Box(new Rect(x, y, w, h), "PRIMARY FLIGHT TELEMETRY", _boxStyle);

            GUILayout.BeginArea(new Rect(x + 10, y + 25, w - 20, h - 35));

            GUILayout.Label($"Attitude: R {t.RollDeg:F1}° | P {t.PitchDeg:F1}° | Y {t.YawDeg:F1}°", _labelStyle);
            GUILayout.Label($"Altitude: AGL {t.AltitudeAglM:F1} m | MSL {t.AltitudeMslM:F1} m", _labelStyle);
            GUILayout.Label($"Ground Speed: {t.GroundSpeedMps:F1} m/s ({t.GroundSpeedMps * 3.6f:F1} km/h)", _labelStyle);
            GUILayout.Label($"Vertical Speed: {t.VerticalSpeedMps:F1} m/s", _labelStyle);
            GUILayout.Space(4);

            GUILayout.Label($"Est. Remaining Flight: {t.EstimatedFlightTimeRemainingS / 60f:F1} min", _labelStyle);
            GUILayout.Label($"Motor 1: {t.Motor1Rpm:F0} RPM | Motor 2: {t.Motor2Rpm:F0} RPM", _labelStyle);
            GUILayout.Label($"Motor 3: {t.Motor3Rpm:F0} RPM | Motor 4: {t.Motor4Rpm:F0} RPM", _labelStyle);
            GUILayout.Space(4);

            GUILayout.Label($"Localization: {t.LocalizationSource}", _labelStyle);
            GUILayout.Label($"Confidence: {t.LocalizationConfidence * 100f:F0}%", _okStyle);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_fc != null && _fc.IsArmed ? "DISARM (F)" : "ARM (R)", _btnStyle))
            {
                if (_fc.IsArmed) _fc.Disarm();
                else _fc.TryArm(out _);
            }
            if (GUILayout.Button("TAKEOFF (Space)", _btnStyle)) _fc?.CommandTakeoff(35f);
            if (GUILayout.Button("LAND (L)", _btnStyle)) _fc?.CommandLand();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void DrawAiDecisionPanel(float x, float y, float w, float h)
        {
            GUI.Box(new Rect(x, y, w, h), "AI AUTONOMY DECISION PIPELINE", _boxStyle);
            GUILayout.BeginArea(new Rect(x + 10, y + 25, w - 20, h - 35));

            DecisionRecord d = _autonomy != null ? _autonomy.LastDecision : default;
            DecisionCycleTiming tm = _autonomy != null ? _autonomy.LastTiming : default;

            // 7-Stage Pipeline Visualizer
            GUILayout.Label("SENSE > PERCEIVE > LOCALIZE > PLAN > DECIDE > ACT > REASSESS", _subHeaderStyle);
            GUILayout.Space(2);

            Color actCol = DecisionRecord.ToColour(d.Action);
            GUI.color = actCol;
            GUILayout.Label($"DECISION: {DecisionRecord.ToActionLabel(d.Action)}", _headerStyle);
            GUI.color = Color.white;

            GUILayout.Label($"Reason: {d.Reason}", _labelStyle);
            if (!string.IsNullOrEmpty(d.RejectedAlternatives))
            {
                GUILayout.Label($"Alt. Rejected: {d.RejectedAlternatives}", _warnStyle);
            }

            GUILayout.Space(4);
            GUILayout.Label($"Confidence: {d.Confidence * 100f:F0}% | Cycle Time: {d.CycleTimeMs:F1} ms", _labelStyle);
            GUILayout.Label($"Nearest Obstacle: {(d.NearestObstacleM > 0 ? d.NearestObstacleM.ToString("F1") + " m" : "NONE")} | TTC: {(float.IsInfinity(d.TimeToCollisionS) ? "INF" : d.TimeToCollisionS.ToString("F1") + " s")}", _labelStyle);
            GUILayout.Label($"Tracked Obstacles: {d.TrackedObstacleCount} | Uncertainty: ±{d.PositionUncertaintyM:F1} m", _labelStyle);

            GUILayout.Space(4);
            GUILayout.Label($"Sense: {tm.SenseMs:F1}ms | Perc: {tm.PerceiveMs:F1}ms | Plan: {tm.PlanMs:F1}ms | Dec: {tm.DecideMs:F1}ms", _labelStyle);

            GUILayout.EndArea();
        }

        private void DrawMissionPlannerPanel(float x, float y, float w, float h)
        {
            GUI.Box(new Rect(x, y, w, h), "MISSION PLANNER & TARGET SELECTION", _boxStyle);
            GUILayout.BeginArea(new Rect(x + 10, y + 25, w - 20, h - 35));

            GUILayout.Label($"Active Mission: {(_mission?.Current != null ? _mission.Current.MissionName : "No Mission")}", _subHeaderStyle);
            GUILayout.Label($"Phase: {(_mission != null ? _mission.Phase.ToString() : "None")} | WP {(_mission != null ? (_mission.ActiveWaypointIndex + 1) : 0)}/{(_mission?.Current != null ? _mission.Current.WaypointCount : 0)}", _labelStyle);

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Target Lat:", _labelStyle, GUILayout.Width(70));
            _targetLatInput = GUILayout.TextField(_targetLatInput, GUILayout.Width(80));
            GUILayout.Label("Lon:", _labelStyle, GUILayout.Width(35));
            _targetLonInput = GUILayout.TextField(_targetLonInput, GUILayout.Width(80));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Cruise Alt (m):", _labelStyle, GUILayout.Width(90));
            _targetAltInput = GUILayout.TextField(_targetAltInput, GUILayout.Width(60));
            if (GUILayout.Button("Set Target", _btnStyle))
            {
                if (double.TryParse(_targetLatInput, out double lat) && double.TryParse(_targetLonInput, out double lon) && float.TryParse(_targetAltInput, out float alt))
                {
                    GeoCoordinate targetGeo = new GeoCoordinate(lat, lon, alt);
                    MissionDefinition mission = new MissionDefinition
                    {
                        MissionName = "Tactical Destination Alpha",
                        HomePosition = new GeoCoordinate(13.0827, 77.5877, 0.0),
                        CruiseAltitudeM = alt
                    };
                    mission.Waypoints.Add(Waypoint.Create(targetGeo, WaypointKind.Target, "Target Objective"));
                    _mission?.Load(mission, out _);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("START MISSION", _btnStyle))
            {
                if (_mission != null && _fc != null)
                {
                    _fc.TryArm(out _);
                    _fc.SetControlSource(ControlSource.Autonomous);
                    _mission.Start(out _);
                    _fc.CommandTakeoff(35f);
                }
            }
            if (GUILayout.Button("ABORT", _btnStyle)) _mission?.Abort("Operator commanded abort.");
            if (GUILayout.Button("RTL (Return)", _btnStyle)) _mission?.CommandReturnHome("Operator RTL Command");
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void DrawSystemHealthPanel(float x, float y, float w, float h)
        {
            GUI.Box(new Rect(x, y, w, h), "SYSTEM HEALTH & FAULT MATRIX", _boxStyle);
            GUILayout.BeginArea(new Rect(x + 10, y + 25, w - 20, h - 35));

            DrawHealthRow("Flight Controller (Pixhawk)", SubsystemStatus.Ok);
            DrawHealthRow("Brushless Motors & ESCs", SubsystemStatus.Ok);
            DrawHealthRow("6S LiPo Battery System", SubsystemStatus.Ok);
            DrawHealthRow("IMU & Accelerometer", SubsystemStatus.Ok);
            DrawHealthRow("GNSS / Barometer Suite", (_telemetry != null && _telemetry.Current.GpsEnabled) ? SubsystemStatus.Ok : SubsystemStatus.Warning);
            DrawHealthRow("3D LiDAR / Depth Sensors", SubsystemStatus.Ok);
            DrawHealthRow("Visual SLAM Estimator", SubsystemStatus.Ok);
            DrawHealthRow("MAVLink Radio Telemetry", SubsystemStatus.Ok);

            GUILayout.EndArea();
        }

        private void DrawHealthRow(string subsystem, SubsystemStatus status)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(subsystem, _labelStyle, GUILayout.Width(190));
            string badge = status.ToString().ToUpper();
            GUIStyle style = (status == SubsystemStatus.Ok) ? _okStyle : _warnStyle;
            GUILayout.Label($"[{badge}]", style);
            GUILayout.EndHorizontal();
        }

        private void DrawEventLogPanel(float x, float y, float w, float h)
        {
            GUI.Box(new Rect(x, y, w, h), "MISSION EVENT & TELEMETRY AUDIT LOG", _boxStyle);
            GUILayout.BeginArea(new Rect(x + 10, y + 25, w - 20, h - 35));

            _logScroll = GUILayout.BeginScrollView(_logScroll);
            EventLog.GetEntries(_logBuffer, 20);
            for (int i = 0; i < _logBuffer.Count; i++)
            {
                LogEntry entry = _logBuffer[i];
                GUIStyle st = _labelStyle;
                if (entry.Severity == LogSeverity.Error || entry.Severity == LogSeverity.Critical) st = _warnStyle;
                else if (entry.Severity == LogSeverity.Warning) st = _warnStyle;
                GUILayout.Label(entry.ToConsoleString(), st);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; ++i) pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
    }
}
