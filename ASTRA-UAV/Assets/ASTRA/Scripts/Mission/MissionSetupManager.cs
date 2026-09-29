using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Geo;
using Astra.Core.Logging;
using Astra.Flight;
using Astra.UI;

namespace Astra.Mission
{
    /// <summary>
    /// Pre-flight Mission Setup screen (ASTRA spec Sec 3-8, 73).
    ///
    /// The simulator previously booted straight into the live Ground Control Station with a single
    /// hardcoded demo mission. The specification calls for an operator-facing configuration step
    /// BEFORE flight: pick the mission profile, the map backend, the launch (home) and objective
    /// (target) coordinates, the navigation mode, and the key flight parameters, then commit with
    /// "Start Simulation". This component provides that step.
    ///
    /// Design constraints honoured here:
    ///  - It does NOT modify or duplicate the working GCS/flight/mission systems (Sec 53). It gates
    ///    the GCS via the static <see cref="IsSetupActive"/> flag and, on commit, hands a normal
    ///    <see cref="MissionDefinition"/> to the existing <see cref="MissionManager"/> and drives the
    ///    existing sensor/flight providers - the same calls the GCS buttons already make.
    ///  - It self-installs via a RuntimeInitializeOnLoadMethod bootstrap, so no scene/prefab editing
    ///    is required (the sandbox has no Unity Editor). If the scene already contains one, the
    ///    bootstrap does nothing.
    ///  - Everything shown is SIMULATED. Coordinates default to the BMSIT&M origin, which is
    ///    labelled UNVERIFIED/APPROXIMATE consistent with GeoReference.
    ///
    /// This delivers backlog item D1. The lightweight per-type waypoint presets below are a stepping
    /// stone to the full mission-type behaviour system (D2); they only shape the route, they do not
    /// yet implement distinct per-type autonomy behaviours.
    /// </summary>
    [DisallowMultipleComponent]
    public class MissionSetupManager : MonoBehaviour
    {
        /// <summary>
        /// True while the pre-flight setup screen is up. The GCS checks this and suppresses its own
        /// panels so the operator configures the run before the operational UI appears.
        /// </summary>
        public static bool IsSetupActive { get; private set; }

        public enum SetupMissionType
        {
            PointToPoint = 0,
            Reconnaissance = 1,
            Surveillance = 2,
            AreaSurvey = 3,
            Search = 4
        }

        public enum SetupNavMode
        {
            GpsAssisted = 0,
            GpsDenied = 1,
            Manual = 2
        }

        public enum SetupMapBackend
        {
            Offline = 0,
            Cesium = 1
        }

        // -------- operator-editable configuration (defaults are sensible + honest) --------
        private SetupMissionType _missionType = SetupMissionType.Reconnaissance;
        private SetupNavMode _navMode = SetupNavMode.GpsAssisted;
        private SetupMapBackend _mapBackend = SetupMapBackend.Offline;

        // Home defaults to the BMSIT&M origin (UNVERIFIED). Target offset ~180 m NE of home.
        private string _homeLat = "13.13200";
        private string _homeLon = "77.56700";
        private string _homeAlt = "890.0";
        private string _tgtLat = "13.13340";
        private string _tgtLon = "77.56830";
        private string _tgtAlt = "35.0";

        private string _cruiseAlt = "40";
        private string _speed = "8";
        private string _safetyMargin = "8";

        private string _validationMessage = string.Empty;
        private bool _validationIsError;

        private Vector2 _scroll;

        // -------- self-install bootstrap (no scene editing needed) --------
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // Gate the GCS immediately, before its first OnGUI frame, so the operational panels never
            // flash up behind the setup screen.
            IsSetupActive = true;

            if (FindAnyOrLegacy() != null) return;

            GameObject go = new GameObject("ASTRA_Mission_Setup");
            go.AddComponent<MissionSetupManager>();
        }

        private static MissionSetupManager FindAnyOrLegacy()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindAnyObjectByType<MissionSetupManager>();
#else
            return Object.FindObjectOfType<MissionSetupManager>();
#endif
        }

        private void Awake()
        {
            IsSetupActive = true;
        }

        // ------------------------------------------------------------------ UI ------------
        private GUIStyle _panel;
        private GUIStyle _h1;
        private GUIStyle _h2;
        private GUIStyle _label;
        private GUIStyle _note;
        private GUIStyle _field;
        private GUIStyle _btn;
        private GUIStyle _btnActive;
        private GUIStyle _start;
        private GUIStyle _err;
        private GUIStyle _ok;
        private bool _styled;

        private void InitStyles()
        {
            if (_styled) return;
            Texture2D dim = Tex(new Color(0.05f, 0.07f, 0.10f, 0.97f));
            Texture2D card = Tex(new Color(0.10f, 0.13f, 0.18f, 0.98f));
            Texture2D btnBg = Tex(new Color(0.16f, 0.22f, 0.30f, 1f));
            Texture2D btnActiveBg = Tex(new Color(0.20f, 0.42f, 0.62f, 1f));
            Texture2D startBg = Tex(new Color(0.16f, 0.52f, 0.34f, 1f));
            Texture2D fieldBg = Tex(new Color(0.04f, 0.05f, 0.07f, 1f));

            _panel = new GUIStyle(GUI.skin.box) { normal = { background = card, textColor = Color.white }, padding = new RectOffset(18, 18, 14, 14) };
            _h1 = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.40f, 0.80f, 0.98f) } };
            _h2 = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.86f, 0.90f, 0.95f) } };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(0.80f, 0.83f, 0.88f) } };
            _note = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Italic, wordWrap = true, normal = { textColor = new Color(0.62f, 0.66f, 0.72f) } };
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 11, normal = { background = fieldBg, textColor = Color.white }, focused = { background = fieldBg, textColor = Color.white } };
            _btn = new GUIStyle(GUI.skin.button) { fontSize = 11, normal = { background = btnBg, textColor = new Color(0.82f, 0.86f, 0.92f) } };
            _btnActive = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { background = btnActiveBg, textColor = Color.white } };
            _start = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { background = startBg, textColor = Color.white } };
            _err = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, wordWrap = true, normal = { textColor = new Color(0.98f, 0.55f, 0.35f) } };
            _ok = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, wordWrap = true, normal = { textColor = new Color(0.35f, 0.85f, 0.50f) } };
            _bg = dim;
            _styled = true;
        }

        private Texture2D _bg;

        private void OnGUI()
        {
            if (!IsSetupActive) return;
            InitStyles();

            // Full-screen dim so the (gated) 3D view does not distract from configuration.
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _bg, ScaleMode.StretchToFill);

            float w = Mathf.Min(720f, Screen.width - 40f);
            float h = Mathf.Min(640f, Screen.height - 40f);
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            GUI.Box(new Rect(x, y, w, h), "", _panel);
            GUILayout.BeginArea(new Rect(x + 18, y + 14, w - 36, h - 28));
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("ASTRA UAV  ·  PRE-FLIGHT MISSION CONFIGURATION", _h1);
            GUILayout.Label("SIMULATED research platform. Configure the run, then commit with START SIMULATION. " +
                            "Coordinates default to the BMSIT&M origin (APPROXIMATE / UNVERIFIED).", _note);
            GUILayout.Space(10);

            DrawMissionType();
            GUILayout.Space(8);
            DrawNavMode();
            GUILayout.Space(8);
            DrawMapBackend();
            GUILayout.Space(8);
            DrawCoordinates();
            GUILayout.Space(8);
            DrawParameters();
            GUILayout.Space(10);

            if (!string.IsNullOrEmpty(_validationMessage))
            {
                GUILayout.Label(_validationMessage, _validationIsError ? _err : _ok);
                GUILayout.Space(6);
            }

            if (GUILayout.Button("▶  START SIMULATION", _start, GUILayout.Height(40)))
            {
                TryStart();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawMissionType()
        {
            GUILayout.Label("MISSION PROFILE", _h2);
            GUILayout.BeginHorizontal();
            foreach (SetupMissionType t in (SetupMissionType[])System.Enum.GetValues(typeof(SetupMissionType)))
            {
                if (GUILayout.Button(Pretty(t.ToString()), _missionType == t ? _btnActive : _btn)) _missionType = t;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(MissionTypeBlurb(_missionType), _note);
        }

        private void DrawNavMode()
        {
            GUILayout.Label("NAVIGATION MODE", _h2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("GPS-Assisted", _navMode == SetupNavMode.GpsAssisted ? _btnActive : _btn)) _navMode = SetupNavMode.GpsAssisted;
            if (GUILayout.Button("GPS-Denied (VIO)", _navMode == SetupNavMode.GpsDenied ? _btnActive : _btn)) _navMode = SetupNavMode.GpsDenied;
            if (GUILayout.Button("Manual", _navMode == SetupNavMode.Manual ? _btnActive : _btn)) _navMode = SetupNavMode.Manual;
            GUILayout.EndHorizontal();
            GUILayout.Label(_navMode == SetupNavMode.GpsDenied
                    ? "GPS disabled at launch; localization runs on the DEMONSTRATION visual-inertial estimator (not production VIO/SLAM)."
                    : _navMode == SetupNavMode.Manual
                        ? "Operator flies with the sticks (WASD/Q-E/Space). Autonomy stays disengaged."
                        : "GNSS + barometer localization active. Autonomy flies the configured route.", _note);
        }

        private void DrawMapBackend()
        {
            GUILayout.Label("MAP BACKEND", _h2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Offline Stylized City", _mapBackend == SetupMapBackend.Offline ? _btnActive : _btn)) _mapBackend = SetupMapBackend.Offline;
            if (GUILayout.Button("Cesium Photoreal", _mapBackend == SetupMapBackend.Cesium ? _btnActive : _btn)) _mapBackend = SetupMapBackend.Cesium;
            GUILayout.EndHorizontal();
            GUILayout.Label(_mapBackend == SetupMapBackend.Cesium
                    ? "Requests real Google Photorealistic 3D Tiles. Only engages if Cesium for Unity + an ion token are configured (see MAP_SETUP.md); otherwise it stays on the offline environment. Toggle any time with F9."
                    : "Stylized, NOT survey-accurate BMSIT&M / Bangalore environment. Always available.", _note);
        }

        private void DrawCoordinates()
        {
            GUILayout.Label("LAUNCH (HOME) — lat / lon / alt m", _h2);
            GUILayout.BeginHorizontal();
            _homeLat = GUILayout.TextField(_homeLat, _field, GUILayout.Width(150));
            _homeLon = GUILayout.TextField(_homeLon, _field, GUILayout.Width(150));
            _homeAlt = GUILayout.TextField(_homeAlt, _field, GUILayout.Width(90));
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("OBJECTIVE (TARGET) — lat / lon / alt m AGL", _h2);
            GUILayout.BeginHorizontal();
            _tgtLat = GUILayout.TextField(_tgtLat, _field, GUILayout.Width(150));
            _tgtLon = GUILayout.TextField(_tgtLon, _field, GUILayout.Width(150));
            _tgtAlt = GUILayout.TextField(_tgtAlt, _field, GUILayout.Width(90));
            GUILayout.EndHorizontal();
        }

        private void DrawParameters()
        {
            GUILayout.Label("FLIGHT PARAMETERS", _h2);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Cruise alt (m)", _label, GUILayout.Width(90));
            _cruiseAlt = GUILayout.TextField(_cruiseAlt, _field, GUILayout.Width(60));
            GUILayout.Space(10);
            GUILayout.Label("Speed (m/s)", _label, GUILayout.Width(80));
            _speed = GUILayout.TextField(_speed, _field, GUILayout.Width(60));
            GUILayout.Space(10);
            GUILayout.Label("Safety margin (m)", _label, GUILayout.Width(110));
            _safetyMargin = GUILayout.TextField(_safetyMargin, _field, GUILayout.Width(60));
            GUILayout.EndHorizontal();
        }

        private static Texture2D Tex(Color c)
        {
            Texture2D t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private static string Pretty(string enumName)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < enumName.Length; i++)
            {
                if (i > 0 && char.IsUpper(enumName[i])) sb.Append(' ');
                sb.Append(enumName[i]);
            }
            return sb.ToString();
        }

        private static string MissionTypeBlurb(SetupMissionType t)
        {
            switch (t)
            {
                case SetupMissionType.PointToPoint: return "Direct route: launch → single cruise leg → objective. Simplest profile.";
                case SetupMissionType.Reconnaissance: return "Approach the objective and orbit it with observation holds before returning.";
                case SetupMissionType.Surveillance: return "Multiple observation passes over the objective corridor.";
                case SetupMissionType.AreaSurvey: return "Lawnmower coverage pattern across the area around the objective.";
                case SetupMissionType.Search: return "Expanding-box search waypoints centred on the objective.";
                default: return string.Empty;
            }
        }

        // ------------------------------------------------------------- commit --------------
        private void TryStart()
        {
            if (!TryD(_homeLat, out double hLat) || !TryD(_homeLon, out double hLon) || !TryD(_homeAlt, out double hAlt) ||
                !TryD(_tgtLat, out double tLat) || !TryD(_tgtLon, out double tLon) || !TryD(_tgtAlt, out double tAlt))
            {
                Fail("One or more coordinate fields is not a valid number.");
                return;
            }
            if (!TryF(_cruiseAlt, out float cruise) || !TryF(_speed, out float speed) || !TryF(_safetyMargin, out float margin))
            {
                Fail("One or more flight-parameter fields is not a valid number.");
                return;
            }
            if (hLat < -90 || hLat > 90 || tLat < -90 || tLat > 90 || hLon < -180 || hLon > 180 || tLon < -180 || tLon > 180)
            {
                Fail("Latitude must be within ±90° and longitude within ±180°.");
                return;
            }

            GeoCoordinate home = new GeoCoordinate(hLat, hLon, hAlt);
            GeoCoordinate target = new GeoCoordinate(tLat, tLon, tAlt);

            // 1. Anchor the world to the chosen launch point (kept labelled as configured, not verified).
            GeoReference geo = GeoReference.Instance;
            if (geo != null)
            {
                geo.SetOrigin(home, "Operator-configured launch point (UNVERIFIED)");
            }

            // 2. Build a mission of the chosen profile and hand it to the EXISTING mission manager.
            MissionDefinition mission = BuildMission(home, target, cruise, speed, margin);
            MissionManager mm = Find<MissionManager>();
            if (mm == null)
            {
                Fail("No MissionManager present in the scene; cannot start.");
                return;
            }
            if (!mm.Load(mission, out string reason))
            {
                Fail("Mission rejected: " + reason);
                return;
            }

            // 3. Map backend. Cesium only actually engages if the package + token are live (honest;
            //    otherwise it logs and stays offline). Offline is the default and needs no action.
            if (_mapBackend == SetupMapBackend.Cesium)
            {
                Map.MapManager map = Find<Map.MapManager>();
                if (map != null) map.ToggleMapProvider();
            }

            // 4. GPS availability per navigation mode.
            ISensorProvider sensors = AstraServices.Get<ISensorProvider>();
            sensors?.SetGpsEnabled(_navMode != SetupNavMode.GpsDenied);

            // 5. Reflect the objective on the GCS target beacon (visual only; our mission stays
            //    authoritative because we loaded it above).
            InteractiveTargetPicker picker = Find<InteractiveTargetPicker>();
            if (picker != null && geo != null)
            {
                Vector3 tgtWorld = geo.ToUnityAtHeight(target, (float)tAlt);
                picker.SetTargetPosition(tgtWorld, "Configured Objective");
                mm.Load(mission, out _); // re-assert our profile mission over the picker's fly-to stub
            }

            // 6. Engage the run. Autonomous modes arm + start + take off (same path as the GCS
            //    F2/F3 handlers); Manual leaves the operator in command.
            FlightControlSystem fc = Find<FlightControlSystem>();
            if (_navMode == SetupNavMode.Manual)
            {
                fc?.SetControlSource(ControlSource.Manual);
                EventLog.Info(LogSource.System, "Pre-flight setup committed: MANUAL run configured. Operator has the sticks.");
            }
            else if (fc != null)
            {
                if (!fc.IsArmed) fc.TryArm(out _);
                fc.SetControlSource(ControlSource.Autonomous);
                mm.Start(out _);
                if (!FlightStateInfo.IsAirborne(fc.State)) fc.CommandTakeoff(cruise);
                EventLog.Info(LogSource.System,
                    $"Pre-flight setup committed: {Pretty(_missionType.ToString())} / {(_navMode == SetupNavMode.GpsDenied ? "GPS-DENIED" : "GPS-ASSISTED")}. Autonomy engaged.");
            }

            // 7. Hand the operational UI to the GCS.
            IsSetupActive = false;
        }

        private MissionDefinition BuildMission(GeoCoordinate home, GeoCoordinate target, float cruise, float speed, float margin)
        {
            MissionDefinition m = new MissionDefinition
            {
                MissionName = "ASTRA " + Pretty(_missionType.ToString()) + " (SIMULATED)",
                Objective = MissionTypeBlurb(_missionType),
                HomePosition = home,
                DefaultSpeedMps = Mathf.Clamp(speed, 1f, 25f),
                CruiseAltitudeM = Mathf.Clamp(cruise, 5f, 120f),
                SafetyMarginM = Mathf.Clamp(margin, 1f, 30f),
                SimulateGpsDenial = _navMode == SetupNavMode.GpsDenied
            };

            float ca = m.CruiseAltitudeM;
            double tgtAlt = target.Altitude;

            // Metres → degrees near the target latitude.
            double mPerDegLat = 111320.0;
            double mPerDegLon = 111320.0 * System.Math.Cos(target.Latitude * System.Math.PI / 180.0);
            if (System.Math.Abs(mPerDegLon) < 1.0) mPerDegLon = 1.0;

            GeoCoordinate Mid(double frac) => new GeoCoordinate(
                home.Latitude + (target.Latitude - home.Latitude) * frac,
                home.Longitude + (target.Longitude - home.Longitude) * frac, ca);
            GeoCoordinate Off(double north, double east, double alt) => new GeoCoordinate(
                target.Latitude + north / mPerDegLat,
                target.Longitude + east / mPerDegLon, alt);

            switch (_missionType)
            {
                case SetupMissionType.PointToPoint:
                    m.Waypoints.Add(Waypoint.Create(Mid(0.5), WaypointKind.Transit, "Cruise Corridor"));
                    m.Waypoints.Add(Waypoint.Create(new GeoCoordinate(target.Latitude, target.Longitude, tgtAlt), WaypointKind.Target, "Objective"));
                    break;

                case SetupMissionType.Reconnaissance:
                    m.Waypoints.Add(Waypoint.Create(Mid(0.5), WaypointKind.Transit, "Cruise Corridor"));
                    m.Waypoints.Add(Waypoint.Create(Off(-40, 0, ca), WaypointKind.Observation, "Recon Hold South"));
                    m.Waypoints.Add(Waypoint.Create(Off(0, 40, ca), WaypointKind.Observation, "Recon Hold East"));
                    m.Waypoints.Add(Waypoint.Create(new GeoCoordinate(target.Latitude, target.Longitude, tgtAlt), WaypointKind.Target, "Objective"));
                    break;

                case SetupMissionType.Surveillance:
                    m.Waypoints.Add(Waypoint.Create(Mid(0.5), WaypointKind.Transit, "Cruise Corridor"));
                    for (int i = 0; i < 3; i++)
                    {
                        double e = (i % 2 == 0) ? -50 : 50;
                        m.Waypoints.Add(Waypoint.Create(Off((i - 1) * 30, e, ca), WaypointKind.Observation, "Surveillance Pass " + (i + 1)));
                    }
                    m.Waypoints.Add(Waypoint.Create(new GeoCoordinate(target.Latitude, target.Longitude, tgtAlt), WaypointKind.Target, "Objective"));
                    break;

                case SetupMissionType.AreaSurvey:
                    m.Waypoints.Add(Waypoint.Create(Mid(0.5), WaypointKind.Transit, "Cruise Corridor"));
                    int leg = 1;
                    for (int row = -1; row <= 1; row++)
                    {
                        double n = row * 40;
                        double e0 = (row % 2 == 0) ? -50 : 50;
                        double e1 = -e0;
                        m.Waypoints.Add(Waypoint.Create(Off(n, e0, ca), WaypointKind.Observation, "Survey Leg " + leg++));
                        m.Waypoints.Add(Waypoint.Create(Off(n, e1, ca), WaypointKind.Observation, "Survey Leg " + leg++));
                    }
                    m.Waypoints.Add(Waypoint.Create(new GeoCoordinate(target.Latitude, target.Longitude, tgtAlt), WaypointKind.Target, "Objective"));
                    break;

                case SetupMissionType.Search:
                    m.Waypoints.Add(Waypoint.Create(Mid(0.5), WaypointKind.Transit, "Cruise Corridor"));
                    double r = 20;
                    double[][] box = { new[] { r, r }, new[] { r, -r }, new[] { -r, -r }, new[] { -r, r } };
                    for (int i = 0; i < box.Length; i++)
                    {
                        double scale = 1.0 + i * 0.6;
                        m.Waypoints.Add(Waypoint.Create(Off(box[i][0] * scale, box[i][1] * scale, ca), WaypointKind.Observation, "Search Box " + (i + 1)));
                    }
                    m.Waypoints.Add(Waypoint.Create(new GeoCoordinate(target.Latitude, target.Longitude, tgtAlt), WaypointKind.Target, "Objective"));
                    break;
            }

            return m;
        }

        private void Fail(string message)
        {
            _validationMessage = message;
            _validationIsError = true;
            EventLog.Warning(LogSource.System, "Mission setup: " + message);
        }

        private static bool TryF(string s, out float v)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private static bool TryD(string s, out double v)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private static T Find<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }
    }
}
