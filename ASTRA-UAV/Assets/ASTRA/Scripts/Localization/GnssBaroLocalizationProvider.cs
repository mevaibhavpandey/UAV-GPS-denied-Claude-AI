using System;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Logging;

namespace Astra.Localization
{
    /// <summary>
    /// Nominal GNSS + Barometer + IMU state estimator.
    /// Implements ILocalizationProvider.
    /// Provides low-drift pose estimation when GPS lock is active.
    /// </summary>
    [DisallowMultipleComponent]
    public class GnssBaroLocalizationProvider : MonoBehaviour, ILocalizationProvider
    {
        [SerializeField] private SubsystemStatus status = SubsystemStatus.Initialising;

        private PoseEstimate _currentEstimate;
        private bool _isConverged;
        private float _accumulatedDrift;
        private Vector3 _estimatedPos;
        private Quaternion _estimatedRot;
        private Vector3 _estimatedVel;

        public string Name => "GNSS/Baro Sensor Fusion (EKF2)";
        public DataProvenance Provenance => DataProvenance.Simulated;
        public SubsystemStatus Status => status;
        public PoseEstimate CurrentEstimate => _currentEstimate;
        public bool IsConverged => _isConverged;
        public float AccumulatedDriftM => _accumulatedDrift;

        private void Start()
        {
            Initialise(transform.position, transform.rotation);
        }

        public bool Initialise(Vector3 knownStartPosition, Quaternion knownStartOrientation)
        {
            _estimatedPos = knownStartPosition;
            _estimatedRot = knownStartOrientation;
            _estimatedVel = Vector3.zero;
            _accumulatedDrift = 0f;
            _isConverged = true;
            status = SubsystemStatus.Ok;

            _currentEstimate = new PoseEstimate
            {
                Position = _estimatedPos,
                Orientation = _estimatedRot,
                Velocity = _estimatedVel,
                PositionStdDev = new Vector3(0.5f, 0.3f, 0.5f),
                HeadingStdDevDeg = 1.0f,
                Confidence = 0.98f,
                SourceName = "GNSS+BARO+IMU",
                Provenance = Provenance,
                Timestamp = Time.timeAsDouble,
                IsValid = true
            };

            return true;
        }

        public void Tick(float fixedDeltaTime, ISensorProvider sensors)
        {
            if (sensors == null) return;

            GpsFix gps = sensors.ReadGps();
            BarometerSample baro = sensors.ReadBarometer();
            MagnetometerSample mag = sensors.ReadMagnetometer();
            ImuSample imu = sensors.ReadImu();

            if (gps.HasFix)
            {
                // In nominal mode, position is tightly anchored to GNSS and Baro
                _accumulatedDrift = 0f;
                _estimatedPos = transform.position + UnityEngine.Random.insideUnitSphere * (gps.Hdop * 0.2f);
                _estimatedPos.y = baro.AltitudeM;
                _estimatedRot = transform.rotation;
                _estimatedVel = gps.VelocityEnu;

                _currentEstimate = new PoseEstimate
                {
                    Position = _estimatedPos,
                    Orientation = _estimatedRot,
                    Velocity = _estimatedVel,
                    PositionStdDev = new Vector3(gps.Hdop * 0.4f, gps.Vdop * 0.5f, gps.Hdop * 0.4f),
                    HeadingStdDevDeg = 1.2f,
                    Confidence = Mathf.Clamp01(1.0f - (gps.Hdop * 0.05f)),
                    SourceName = "GNSS+BARO+IMU",
                    Provenance = Provenance,
                    Timestamp = Time.timeAsDouble,
                    IsValid = true
                };
            }
            else
            {
                // Fallback dead reckoning if this provider is forced to run without GPS
                _accumulatedDrift += _estimatedVel.magnitude * fixedDeltaTime * 0.08f;
                _currentEstimate.Confidence = Mathf.Max(0.1f, _currentEstimate.Confidence - fixedDeltaTime * 0.05f);
                _currentEstimate.PositionStdDev += Vector3.one * fixedDeltaTime * 0.2f;
            }
        }

        public void ResetTo(Vector3 position, Quaternion orientation)
        {
            _estimatedPos = position;
            _estimatedRot = orientation;
            _accumulatedDrift = 0f;
        }
    }
}
