using System;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Logging;

namespace Astra.Localization
{
    /// <summary>
    /// Visual-Inertial Odometry / Dead Reckoning state estimator.
    /// Used during GPS-Denied flight operations.
    /// Implements ILocalizationProvider.
    ///
    /// HONESTY NOTICE:
    /// This is a demonstration model representing VIO/SLAM dynamics.
    /// It accumulates realistic integration drift proportional to distance and time,
    /// tracking visual feature confidence and uncertainty bounds.
    /// Provenance is declared as DataProvenance.Demonstration.
    /// </summary>
    [DisallowMultipleComponent]
    public class VisualInertialLocalizationProvider : MonoBehaviour, ILocalizationProvider
    {
        [Header("VIO Simulation Parameters")]
        [SerializeField] private float driftRatePercent = 0.035f; // 3.5% distance drift typical for open-loop VIO
        [SerializeField] private float timeDriftRateMps = 0.08f;   // 8 cm/s velocity integration random walk
        [SerializeField] private int trackedFeaturesCount = 240;
        [SerializeField] private float featureTrackingQuality = 0.88f;

        [Header("Status")]
        [SerializeField] private SubsystemStatus status = SubsystemStatus.Initialising;

        private PoseEstimate _currentEstimate;
        private bool _isConverged;
        private float _accumulatedDrift;
        private Vector3 _estimatedPos;
        private Quaternion _estimatedRot;
        private Vector3 _estimatedVel;
        private Vector3 _driftVector;
        private Vector3 _lastTruePos;
        private float _lastReportedDriftThreshold;

        public string Name => "Visual-Inertial Odometry (VIO Demonstration)";
        public DataProvenance Provenance => DataProvenance.Demonstration;
        public SubsystemStatus Status => status;
        public PoseEstimate CurrentEstimate => _currentEstimate;
        public bool IsConverged => _isConverged;
        public float AccumulatedDriftM => _accumulatedDrift;
        public int TrackedFeatures => trackedFeaturesCount;
        public float FeatureQuality => featureTrackingQuality;

        public bool Initialise(Vector3 knownStartPosition, Quaternion knownStartOrientation)
        {
            _estimatedPos = knownStartPosition;
            _estimatedRot = knownStartOrientation;
            _lastTruePos = knownStartPosition;
            _estimatedVel = Vector3.zero;
            _driftVector = Vector3.zero;
            _accumulatedDrift = 0f;
            _lastReportedDriftThreshold = 0f;
            _isConverged = true;
            status = SubsystemStatus.Ok;

            _currentEstimate = new PoseEstimate
            {
                Position = _estimatedPos,
                Orientation = _estimatedRot,
                Velocity = _estimatedVel,
                PositionStdDev = new Vector3(0.8f, 0.4f, 0.8f),
                HeadingStdDevDeg = 2.5f,
                Confidence = 0.85f,
                SourceName = "VISUAL-INERTIAL (GPS-OFF)",
                Provenance = Provenance,
                Timestamp = Time.timeAsDouble,
                IsValid = true
            };

            EventLog.Info(LogSource.Navigation, "Visual-Inertial estimator initialized. Features: " + trackedFeaturesCount);
            return true;
        }

        public void Tick(float fixedDeltaTime, ISensorProvider sensors)
        {
            Vector3 truePos = transform.position;
            Vector3 deltaMove = truePos - _lastTruePos;
            _lastTruePos = truePos;

            // Simulating realistic visual feature tracking noise & optical flow integration
            float moveDist = deltaMove.magnitude;
            float stepDrift = (moveDist * driftRatePercent) + (timeDriftRateMps * fixedDeltaTime);

            Vector3 randomDriftDir = UnityEngine.Random.insideUnitSphere.normalized;
            _driftVector += randomDriftDir * stepDrift;
            _accumulatedDrift = _driftVector.magnitude;

            // Check drift threshold notification
            if (_accumulatedDrift - _lastReportedDriftThreshold >= 5.0f)
            {
                _lastReportedDriftThreshold = Mathf.Floor(_accumulatedDrift / 5.0f) * 5.0f;
                AstraEvents.RaiseDriftThresholdCrossed(_accumulatedDrift);
            }

            _estimatedPos = truePos + _driftVector;
            _estimatedRot = transform.rotation * Quaternion.Euler(UnityEngine.Random.insideUnitSphere * (_accumulatedDrift * 0.1f));
            _estimatedVel = (deltaMove / Mathf.Max(0.001f, fixedDeltaTime)) + (_driftVector.normalized * 0.05f);

            // Optical flow confidence decreases with excessive drift and erratic rotation
            float confidence = Mathf.Clamp(1.0f - (_accumulatedDrift * 0.025f), 0.2f, 0.95f);
            Vector3 stdDev = new Vector3(1.0f + _accumulatedDrift * 0.5f, 0.5f + _accumulatedDrift * 0.2f, 1.0f + _accumulatedDrift * 0.5f);

            _currentEstimate = new PoseEstimate
            {
                Position = _estimatedPos,
                Orientation = _estimatedRot,
                Velocity = _estimatedVel,
                PositionStdDev = stdDev,
                HeadingStdDevDeg = 2.0f + _accumulatedDrift * 0.4f,
                Confidence = confidence,
                SourceName = "VISUAL-INERTIAL (GPS-OFF)",
                Provenance = Provenance,
                Timestamp = Time.timeAsDouble,
                IsValid = true
            };
        }

        public void ResetTo(Vector3 position, Quaternion orientation)
        {
            _estimatedPos = position;
            _estimatedRot = orientation;
            _lastTruePos = transform.position;
            _driftVector = Vector3.zero;
            _accumulatedDrift = 0f;
            _lastReportedDriftThreshold = 0f;
            EventLog.Info(LogSource.Navigation, "Visual-Inertial Odometry state reset to ground truth landmark.");
        }
    }
}
