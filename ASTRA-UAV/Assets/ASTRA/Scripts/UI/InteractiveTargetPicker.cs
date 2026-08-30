using System;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Geo;
using Astra.Core.Logging;
using Astra.Flight;
using Astra.Localization;
using Astra.Mission;

namespace Astra.UI
{
    /// <summary>
    /// Interactive 3D Target & Landing Spot Picker.
    /// Allows the operator to click anywhere on the 3D map/ground to designate target coordinates,
    /// spawns a visual holographic landing beacon, and coordinates autonomous navigation and precision landing.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractiveTargetPicker : MonoBehaviour
    {
        [Header("Beacon Appearance")]
        [SerializeField] private Color beaconColor = new Color(0.1f, 0.95f, 0.7f, 0.9f);
        [SerializeField] private float beaconRingRadius = 4f;

        private GameObject _beaconObject;
        private Vector3 _selectedTargetPosition = new Vector3(80f, 0f, 120f);
        private bool _hasTarget = false;
        private string _targetLabel = "Target Alpha";

        private FlightControlSystem _fc;
        private MissionManager _mission;
        private Camera _mainCam;

        public Vector3 SelectedTargetPosition => _selectedTargetPosition;
        public bool HasTarget => _hasTarget;
        public string TargetLabel => _targetLabel;

        private void Awake()
        {
            _fc = FindFirstObjectByType<FlightControlSystem>();
            _mission = FindFirstObjectByType<MissionManager>();
            _mainCam = Camera.main;
        }

        private void Start()
        {
            CreateBeaconVisual();
            SetTargetPosition(new Vector3(60f, 0f, 90f), "Urban Dropzone Alpha");
        }

        private void Update()
        {
            if (_mainCam == null) _mainCam = Camera.main;

            // Right-Click (or Left-Click with LeftAlt) anywhere on the 3D ground to place target
            if (Input.GetMouseButtonDown(1) || (Input.GetMouseButtonDown(0) && Input.GetKey(KeyCode.LeftAlt)))
            {
                Ray ray = _mainCam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 2000f))
                {
                    SetTargetPosition(hit.point, $"Custom Target [{hit.point.x:F0}, {hit.point.z:F0}]");
                }
            }

            // Animate Beacon rings
            if (_beaconObject != null && _beaconObject.activeSelf)
            {
                Transform ring = _beaconObject.transform.Find("BeaconRing");
                if (ring != null)
                {
                    ring.Rotate(Vector3.up, 45f * Time.deltaTime, Space.Self);
                    float pulse = 1f + 0.1f * Mathf.Sin(Time.time * 4f);
                    ring.localScale = new Vector3(beaconRingRadius * pulse, 0.1f, beaconRingRadius * pulse);
                }
            }
        }

        public void SetTargetPosition(Vector3 worldPos, string label)
        {
            _selectedTargetPosition = worldPos;
            _targetLabel = label;
            _hasTarget = true;

            if (_beaconObject != null)
            {
                _beaconObject.transform.position = worldPos;
                _beaconObject.SetActive(true);
            }

            GeoCoordinate geo = GeoReference.Instance != null ? GeoReference.Instance.ToGeo(worldPos) : new GeoCoordinate(13.0850, 77.5900, worldPos.y);

            EventLog.Info(LogSource.Mission, $"📍 New Target Selected: '{label}' at ({worldPos.x:F1}m, {worldPos.z:F1}m, Alt: {worldPos.y:F1}m) | Geo: [{geo.Latitude:F5}, {geo.Longitude:F5}]");

            // Update Mission Definition
            if (_mission != null)
            {
                MissionDefinition mission = new MissionDefinition
                {
                    MissionName = $"Fly to {label}",
                    HomePosition = GeoReference.Instance != null ? GeoReference.Instance.ToGeo(Vector3.zero) : new GeoCoordinate(13.0827, 77.5877, 0.0),
                    CruiseAltitudeM = Mathf.Max(30f, worldPos.y + 25f)
                };

                // Waypoint 1: Cruise corridor
                Vector3 midPoint = Vector3.Lerp(Vector3.zero, worldPos, 0.5f) + Vector3.up * mission.CruiseAltitudeM;
                mission.Waypoints.Add(Waypoint.Create(GeoReference.Instance.ToGeo(midPoint), WaypointKind.Transit, "Transit Corridor Alpha"));

                // Waypoint 2: Destination Approach
                Vector3 approachPoint = worldPos + Vector3.up * mission.CruiseAltitudeM;
                mission.Waypoints.Add(Waypoint.Create(GeoReference.Instance.ToGeo(approachPoint), WaypointKind.Target, $"{label} Overhead"));

                // Waypoint 3: Landing Touchdown
                mission.Waypoints.Add(Waypoint.Create(geo, WaypointKind.Land, $"{label} Helipad"));

                _mission.Load(mission, out _);
            }
        }

        /// <summary>
        /// Command the UAV to take off and fly autonomously to the selected target.
        /// </summary>
        public void ExecuteFlyToTarget()
        {
            if (_fc == null) _fc = FindFirstObjectByType<FlightControlSystem>();
            if (_mission == null) _mission = FindFirstObjectByType<MissionManager>();

            if (_fc != null)
            {
                if (!_fc.IsArmed) _fc.TryArm(out _);
                _fc.SetControlSource(ControlSource.Autonomous);

                if (_mission != null)
                {
                    _mission.Start(out _);
                }

                if (!FlightStateInfo.IsAirborne(_fc.State))
                {
                    _fc.CommandTakeoff(35f);
                }
                else
                {
                    Vector3 targetOverhead = _selectedTargetPosition + Vector3.up * 35f;
                    _fc.CommandGoTo(targetOverhead, 10f);
                }

                EventLog.Success(LogSource.Mission, $"🚀 Autonomous Mission Initiated: Flying to '{_targetLabel}'");
            }
        }

        /// <summary>
        /// Command the UAV to fly to the target and execute a precision landing.
        /// </summary>
        public void ExecuteAutoLandAtTarget()
        {
            if (_fc == null) _fc = FindFirstObjectByType<FlightControlSystem>();

            if (_fc != null)
            {
                if (!_fc.IsArmed) _fc.TryArm(out _);
                _fc.SetControlSource(ControlSource.Autonomous);

                Vector3 targetOverhead = _selectedTargetPosition + Vector3.up * 30f;
                _fc.CommandGoTo(targetOverhead, 8f);

                EventLog.Warning(LogSource.FlightController, $"🛬 Auto-Landing Sequence Initiated for '{_targetLabel}'");
            }
        }

        private void CreateBeaconVisual()
        {
            _beaconObject = new GameObject("Holographic_Landing_Beacon");

            // Vertical Light Pillar / Beam
            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "BeaconBeam";
            beam.transform.SetParent(_beaconObject.transform, false);
            beam.transform.localPosition = new Vector3(0f, 40f, 0f);
            beam.transform.localScale = new Vector3(0.35f, 40f, 0.35f);
            DestroyImmediate(beam.GetComponent<Collider>());

            // Glowing Outer Ring
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "BeaconRing";
            ring.transform.SetParent(_beaconObject.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            ring.transform.localScale = new Vector3(beaconRingRadius, 0.05f, beaconRingRadius);
            DestroyImmediate(ring.GetComponent<Collider>());

            // Landing Pad "H" Center Plate
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "HelipadMarker";
            pad.transform.SetParent(_beaconObject.transform, false);
            pad.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            pad.transform.localScale = new Vector3(3.5f, 0.08f, 3.5f);
            DestroyImmediate(pad.GetComponent<Collider>());

            // Apply Materials with Emissive Glow
            Material beamMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            beamMat.color = beaconColor;
            if (beamMat.HasProperty("_EmissionColor"))
            {
                beamMat.EnableKeyword("_EMISSION");
                beamMat.SetColor("_EmissionColor", beaconColor * 2.5f);
            }

            beam.GetComponent<Renderer>().sharedMaterial = beamMat;
            ring.GetComponent<Renderer>().sharedMaterial = beamMat;
            pad.GetComponent<Renderer>().sharedMaterial = beamMat;

            _beaconObject.SetActive(true);
        }
    }
}
