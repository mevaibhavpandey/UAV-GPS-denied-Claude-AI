using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Logging;

namespace Astra.Localization
{
    /// <summary>
    /// Owns the localization stack and is the single <see cref="ILocalizationProvider"/> the rest of
    /// the system talks to. It holds both estimators - the nominal GNSS/baro/IMU fusion and the
    /// GPS-denied visual-inertial demonstrator - and switches between them when GPS availability
    /// changes.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// Before this class the two estimators each implemented ILocalizationProvider but neither ever
    /// registered with <see cref="AstraServices"/>, so every consumer's Get&lt;ILocalizationProvider&gt;()
    /// returned null and the whole LOCALIZE stage was a silent no-op. Worse, "GPS-denied mode" only
    /// toggled a flag on the sensor suite: nothing switched the estimator, and the visual-inertial
    /// provider was dead code that never ran. This manager closes both gaps. It is the one component
    /// that registers, and it is the one subscriber to GpsAvailabilityChanged that actually changes
    /// what the aircraft is navigating on.
    ///
    /// HONESTY NOTE: the GPS-denied path hands over to VisualInertialLocalizationProvider, which is a
    /// labelled DEMONSTRATION model of VIO drift, not a real SLAM/VIO implementation. The provenance
    /// on the pose estimate reflects that and the drift is shown, not hidden.
    /// </summary>
    [DisallowMultipleComponent]
    public class LocalizationManager : MonoBehaviour, ILocalizationProvider
    {
        [SerializeField] private GnssBaroLocalizationProvider gnssProvider;
        [SerializeField] private VisualInertialLocalizationProvider vioProvider;

        [Tooltip("The estimator currently feeding navigation. Shown for diagnostics.")]
        [SerializeField] private bool gpsDenied;

        private ILocalizationProvider _active;
        private bool _initialised;

        public string Name => _active != null ? _active.Name : "Localization Manager (no estimator)";
        public DataProvenance Provenance => _active != null ? _active.Provenance : DataProvenance.Simulated;
        public SubsystemStatus Status => _active != null ? _active.Status : SubsystemStatus.Error;
        public PoseEstimate CurrentEstimate => _active != null ? _active.CurrentEstimate : PoseEstimate.Invalid;
        public bool IsConverged => _active != null && _active.IsConverged;
        public float AccumulatedDriftM => _active != null ? _active.AccumulatedDriftM : 0f;

        /// <summary>True when the manager has handed navigation to the GPS-denied estimator.</summary>
        public bool IsGpsDenied => gpsDenied;

        private void Awake()
        {
            // Both estimators live on this GameObject. Create whichever is missing so a scene that
            // only wired up one still gets a working GPS-denied fallback rather than a null switch.
            if (gnssProvider == null)
            {
                gnssProvider = GetComponent<GnssBaroLocalizationProvider>();
                if (gnssProvider == null)
                {
                    gnssProvider = gameObject.AddComponent<GnssBaroLocalizationProvider>();
                }
            }
            if (vioProvider == null)
            {
                vioProvider = GetComponent<VisualInertialLocalizationProvider>();
                if (vioProvider == null)
                {
                    vioProvider = gameObject.AddComponent<VisualInertialLocalizationProvider>();
                }
            }

            _active = gnssProvider;

            // Register in Awake so the estimator is available before any consumer's Start or the
            // first FixedUpdate cycle asks for it.
            AstraServices.Register<ILocalizationProvider>(this);
        }

        private void OnEnable()
        {
            AstraEvents.GpsAvailabilityChanged += OnGpsAvailabilityChanged;
        }

        private void OnDisable()
        {
            AstraEvents.GpsAvailabilityChanged -= OnGpsAvailabilityChanged;
        }

        private void Start()
        {
            gnssProvider.Initialise(transform.position, transform.rotation);
            vioProvider.Initialise(transform.position, transform.rotation);
            _initialised = true;
        }

        private void OnDestroy()
        {
            AstraServices.UnregisterIfCurrent<ILocalizationProvider>(this);
        }

        /// <summary>
        /// Ticks the active estimator. Called once per cycle by the autonomy controller through the
        /// ILocalizationProvider interface, so only the estimator actually in use spends any budget.
        /// </summary>
        public void Tick(float fixedDeltaTime, ISensorProvider sensors)
        {
            if (!_initialised)
            {
                return;
            }
            _active?.Tick(fixedDeltaTime, sensors);
        }

        public bool Initialise(Vector3 knownStartPosition, Quaternion knownStartOrientation)
        {
            bool a = gnssProvider != null && gnssProvider.Initialise(knownStartPosition, knownStartOrientation);
            bool b = vioProvider != null && vioProvider.Initialise(knownStartPosition, knownStartOrientation);
            _initialised = a || b;
            return _initialised;
        }

        public void ResetTo(Vector3 position, Quaternion orientation)
        {
            _active?.ResetTo(position, orientation);
        }

        private void OnGpsAvailabilityChanged(bool gpsAvailable)
        {
            bool nowDenied = !gpsAvailable;
            if (nowDenied == gpsDenied)
            {
                return;
            }
            gpsDenied = nowDenied;

            if (nowDenied)
            {
                // GPS just dropped. Seed the visual-inertial estimator with the last GNSS pose so it
                // begins dead reckoning from the true position rather than from a stale one, then
                // hand it navigation. From here its reported drift grows and is displayed - the whole
                // point of the GPS-denied demonstration.
                PoseEstimate handoff = gnssProvider != null ? gnssProvider.CurrentEstimate : CurrentEstimate;
                Vector3 pos = handoff.IsValid ? handoff.Position : transform.position;
                Quaternion rot = handoff.IsValid ? handoff.Orientation : transform.rotation;
                vioProvider?.ResetTo(pos, rot);
                _active = vioProvider;
                EventLog.Warning(LogSource.Navigation,
                    "GPS DENIED - navigation handed to Visual-Inertial estimator (DEMONSTRATION). " +
                    "Position drift will now accumulate and is shown in telemetry.");
            }
            else
            {
                // GPS restored. Re-anchor the GNSS estimator to the current truth (a real system gets
                // an absolute fix here) and hand navigation back to it.
                gnssProvider?.ResetTo(transform.position, transform.rotation);
                _active = gnssProvider;
                EventLog.Success(LogSource.Navigation,
                    "GPS REACQUIRED - navigation returned to GNSS/baro fusion; drift reset by absolute fix.");
            }
        }
    }
}
