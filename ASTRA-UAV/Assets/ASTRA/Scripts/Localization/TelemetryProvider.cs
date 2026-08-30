using System;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Geo;
using Astra.Drone;
using Astra.Flight;

namespace Astra.Localization
{
    /// <summary>
    /// Telemetry aggregator that pools vehicle state from physics, battery, propulsion,
    /// sensors, and mission engines into a single TelemetrySnapshot for UI and diagnostics.
    /// Implements ITelemetryProvider.
    /// </summary>
    [DisallowMultipleComponent]
    public class TelemetryProvider : MonoBehaviour, ITelemetryProvider
    {
        [Header("References")]
        [SerializeField] private FlightControlSystem flightController;
        [SerializeField] private BatterySystem batterySystem;
        [SerializeField] private MotorUnit[] motors;
        [SerializeField] private Rigidbody droneRigidbody;

        private TelemetrySnapshot _current;

        public string Name => "ASTRA Telemetry Aggregator (MAVLink v2)";
        public DataProvenance Provenance => DataProvenance.Simulated;
        public TelemetrySnapshot Current => _current;
        public float LinkQuality => 1.0f;
        public float LatencyMs => 0f;

        private void Awake()
        {
            if (flightController == null) flightController = GetComponent<FlightControlSystem>();
            if (batterySystem == null) batterySystem = GetComponent<BatterySystem>();
            if (motors == null || motors.Length == 0) motors = GetComponentsInChildren<MotorUnit>();
            if (droneRigidbody == null) droneRigidbody = GetComponent<Rigidbody>();
        }

        private void Start()
        {
            AstraServices.Register<ITelemetryProvider>(this);
        }

        private void OnDestroy()
        {
            AstraServices.UnregisterIfCurrent<ITelemetryProvider>(this);
        }

        private void Update()
        {
            Vector3 pos = droneRigidbody != null ? droneRigidbody.position : transform.position;
            Vector3 vel = droneRigidbody != null ? droneRigidbody.linearVelocity : Vector3.zero;
            Vector3 euler = droneRigidbody != null ? droneRigidbody.rotation.eulerAngles : transform.rotation.eulerAngles;

            // Attitude normalization
            float roll = euler.z > 180 ? euler.z - 360 : euler.z;
            float pitch = euler.x > 180 ? euler.x - 360 : euler.x;
            float yaw = euler.y;

            // Sensors & Localization
            ISensorProvider sensors = AstraServices.Get<ISensorProvider>();
            ILocalizationProvider loc = AstraServices.Get<ILocalizationProvider>();
            IMissionProvider mission = AstraServices.Get<IMissionProvider>();

            bool gpsActive = sensors != null && sensors.GpsAvailable;
            int satCount = 0;
            float hdop = 1.0f;
            if (sensors != null)
            {
                GpsFix fix = sensors.ReadGps();
                satCount = fix.SatelliteCount;
                hdop = fix.Hdop;
            }

            PoseEstimate pose = loc != null ? loc.CurrentEstimate : PoseEstimate.Invalid;

            // Mission progress
            float progress = mission != null ? mission.Progress * 100f : 0f;
            float distToTarget = 0f;
            float eta = 0f;
            if (mission != null && mission.Current != null && mission.ActiveWaypointIndex >= 0)
            {
                GeoReference geo = GeoReference.Instance;
                Vector3 targetPos = geo != null ? geo.ToWorld(mission.ActiveWaypoint.Position) : pos;
                distToTarget = Vector3.Distance(pos, targetPos);
                float speed = vel.magnitude;
                eta = speed > 0.5f ? distToTarget / speed : distToTarget / 8.0f;
            }

            float battFrac = batterySystem != null ? batterySystem.StateOfCharge : 1.0f;
            float battVolt = batterySystem != null ? batterySystem.VoltageV : 22.2f;
            float battAmp = batterySystem != null ? batterySystem.CurrentA : 0f;
            float battMah = batterySystem != null ? batterySystem.ConsumedMah : 0f;

            _current = new TelemetrySnapshot
            {
                RollDeg = roll,
                PitchDeg = pitch,
                YawDeg = yaw,
                HeadingDeg = yaw,
                AltitudeAglM = pos.y,
                AltitudeMslM = pos.y + 920f,
                GroundSpeedMps = new Vector2(vel.x, vel.z).magnitude,
                VerticalSpeedMps = vel.y,
                VelocityWorld = vel,
                Latitude = 13.0827 + (pos.z / 111319.5),
                Longitude = 77.5877 + (pos.x / 108480.0),
                BatteryPercent = battFrac * 100f,
                BatteryVoltage = battVolt,
                BatteryCurrentA = battAmp,
                BatteryConsumedMah = battMah,
                EstimatedFlightTimeRemainingS = battAmp > 0.5f ? (battFrac * 10000f / battAmp) * 3.6f : 1200f,
                Motor1Rpm = (motors != null && motors.Length > 0 && motors[0] != null) ? motors[0].Rpm : 0f,
                Motor2Rpm = (motors != null && motors.Length > 1 && motors[1] != null) ? motors[1].Rpm : 0f,
                Motor3Rpm = (motors != null && motors.Length > 2 && motors[2] != null) ? motors[2].Rpm : 0f,
                Motor4Rpm = (motors != null && motors.Length > 3 && motors[3] != null) ? motors[3].Rpm : 0f,
                ThrottlePercent = flightController != null ? 50f : 0f,
                GpsEnabled = gpsActive,
                SatelliteCount = satCount,
                Hdop = hdop,
                LocalizationSource = pose.IsValid ? pose.SourceName : "DEAD RECKONING",
                LocalizationConfidence = pose.IsValid ? pose.Confidence : 0f,
                MissionProgressPercent = progress,
                DistanceToTargetM = distToTarget,
                DistanceRemainingM = distToTarget,
                EtaSeconds = eta,
                FlightModeName = flightController != null ? flightController.CurrentControlSource.ToString().ToUpper() : "MANUAL",
                FlightStateName = flightController != null ? FlightStateInfo.ToDisplayName(flightController.State) : "DISARMED",
                IsArmed = flightController != null && flightController.IsArmed,
                Timestamp = Time.timeAsDouble
            };
        }
    }
}
