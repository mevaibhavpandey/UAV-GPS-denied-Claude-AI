using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Logging;
using Astra.Perception;

namespace Astra.Sensors
{
    /// <summary>
    /// Named multi-sensor fusion status aggregator (ASTRA spec Sec 16).
    ///
    /// The specification calls for an explicit sensor-fusion component that brings together LiDAR,
    /// camera, IMU, GPS, compass and barometer. In ASTRA the actual state estimation already happens
    /// inside the <see cref="ILocalizationProvider"/> (GNSS+baro or the VIO demonstration) and the
    /// perception stack; what was missing was a single named place that AGGREGATES the health,
    /// provenance and freshness of every sensor feed and reports which sources are currently
    /// contributing to the fused estimate. This component is that place.
    ///
    /// HONESTY (important): this is a fusion STATUS aggregator, not a new estimator. It does not run
    /// an EKF/UKF or invent a better pose than the localization provider already produces - claiming
    /// otherwise would misrepresent the system. It reads the existing providers, summarises which
    /// sensors are healthy and feeding the solution, and exposes that for the panel and the docs.
    /// The fused pose it reports is exactly the localization provider's estimate, passed through.
    /// </summary>
    [DisallowMultipleComponent]
    public class SensorFusionManager : MonoBehaviour
    {
        public struct SensorChannel
        {
            public string Name;
            public bool Contributing;      // healthy AND currently feeding the estimate
            public SubsystemStatus Status;
            public DataProvenance Provenance;
            public string Detail;          // short human-readable value/units
        }

        public SensorChannel Imu;
        public SensorChannel Gnss;
        public SensorChannel Barometer;
        public SensorChannel Magnetometer;
        public SensorChannel Lidar;
        public SensorChannel Camera;

        /// <summary>The fused pose. This IS the localization provider's estimate, not a re-fusion.</summary>
        public PoseEstimate FusedPose { get; private set; }

        /// <summary>Count of sensor channels currently contributing to the solution.</summary>
        public int ContributingChannelCount { get; private set; }

        /// <summary>Which estimator the fused pose came from, e.g. "GNSS+BARO" or "VIO".</summary>
        public string ActiveEstimator { get; private set; } = "NONE";

        // -------- self-install bootstrap (no scene editing needed) --------
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAny() != null) return;
            new GameObject("ASTRA_Sensor_Fusion").AddComponent<SensorFusionManager>();
        }

        private static SensorFusionManager FindAny()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindAnyObjectByType<SensorFusionManager>();
#else
            return Object.FindObjectOfType<SensorFusionManager>();
#endif
        }

        private void Start()
        {
            AstraServices.Register<SensorFusionManager>(this);
            EventLog.Info(LogSource.Sensors, "Sensor fusion aggregator online (status only; estimation stays in the localization provider).");
        }

        private void OnDestroy()
        {
            AstraServices.UnregisterIfCurrent<SensorFusionManager>(this);
        }

        private void FixedUpdate()
        {
            ISensorProvider sensors = AstraServices.Get<ISensorProvider>();
            ILocalizationProvider loc = AstraServices.Get<ILocalizationProvider>();
            IObstacleDetector det = AstraServices.Get<IObstacleDetector>();
            LidarSensor lidar = AstraServices.Get<LidarSensor>();

            // ---- Inertial / GNSS / baro / compass, from the raw sensor provider ----
            if (sensors != null)
            {
                ImuSample imu = sensors.ReadImu();
                Imu = new SensorChannel
                {
                    Name = "IMU",
                    Contributing = imu.IsValid,
                    Status = imu.IsValid ? SubsystemStatus.Ok : SubsystemStatus.Error,
                    Provenance = sensors.Provenance,
                    Detail = imu.IsValid ? $"{imu.AngularVelocity.magnitude:F2} rad/s" : "no data"
                };

                GpsFix gps = sensors.ReadGps();
                Gnss = new SensorChannel
                {
                    Name = "GNSS",
                    Contributing = sensors.GpsAvailable && gps.HasFix,
                    Status = !sensors.GpsAvailable ? SubsystemStatus.Offline
                             : gps.HasFix ? SubsystemStatus.Ok : SubsystemStatus.Warning,
                    Provenance = sensors.Provenance,
                    Detail = !sensors.GpsAvailable ? "DENIED"
                             : gps.HasFix ? $"{gps.SatelliteCount} sats, HDOP {gps.Hdop:F1}" : "no fix"
                };

                BarometerSample baro = sensors.ReadBarometer();
                Barometer = new SensorChannel
                {
                    Name = "Barometer",
                    Contributing = baro.IsValid,
                    Status = baro.IsValid ? SubsystemStatus.Ok : SubsystemStatus.Error,
                    Provenance = sensors.Provenance,
                    Detail = baro.IsValid ? $"{baro.AltitudeM:F1} m" : "no data"
                };

                MagnetometerSample mag = sensors.ReadMagnetometer();
                Magnetometer = new SensorChannel
                {
                    Name = "Compass",
                    Contributing = mag.IsValid,
                    Status = mag.IsValid ? SubsystemStatus.Ok : SubsystemStatus.Error,
                    Provenance = sensors.Provenance,
                    Detail = mag.IsValid ? $"{mag.HeadingDegrees:F0}°" : "no data"
                };
            }

            // ---- LiDAR, from the dedicated sensor model (D3) ----
            if (lidar != null)
            {
                Lidar = new SensorChannel
                {
                    Name = "LiDAR",
                    Contributing = lidar.Status == SubsystemStatus.Ok && lidar.LastPointCount > 0,
                    Status = lidar.Status,
                    Provenance = lidar.Provenance,
                    Detail = $"{lidar.LastPointCount} pts, {lidar.LastReturnRatePercent:F0}% rtn"
                };
            }
            else
            {
                Lidar = new SensorChannel { Name = "LiDAR", Status = SubsystemStatus.Offline, Detail = "absent" };
            }

            // ---- Camera / obstacle detector feed ----
            if (det != null)
            {
                Camera = new SensorChannel
                {
                    Name = "Camera/Depth",
                    Contributing = det.Status == SubsystemStatus.Ok,
                    Status = det.Status,
                    Provenance = det.Provenance,
                    Detail = $"{det.Obstacles.Count} tracks"
                };
            }
            else
            {
                Camera = new SensorChannel { Name = "Camera/Depth", Status = SubsystemStatus.Offline, Detail = "absent" };
            }

            // ---- Fused pose is passed through from the localization provider ----
            if (loc != null)
            {
                FusedPose = loc.CurrentEstimate;
                ActiveEstimator = FusedPose.SourceName ?? "NONE";
            }

            int c = 0;
            if (Imu.Contributing) c++;
            if (Gnss.Contributing) c++;
            if (Barometer.Contributing) c++;
            if (Magnetometer.Contributing) c++;
            if (Lidar.Contributing) c++;
            if (Camera.Contributing) c++;
            ContributingChannelCount = c;
        }
    }
}
