using UnityEngine;
using Astra.Core.Logging;

namespace Astra.Core
{
    /// <summary>
    /// How ASTRA is sourcing its data. Required by the specification and genuinely useful: the same
    /// ground station UI must work unchanged across all three, which is the test of whether the
    /// hardware abstraction is real or decorative.
    /// </summary>
    public enum OperatingMode
    {
        /// <summary>Everything simulated. The only fully implemented mode today.</summary>
        Simulation = 0,

        /// <summary>
        /// Some subsystems live, some simulated. The realistic intermediate state during hardware
        /// bring-up, when for example a real IMU is connected but the camera is not yet.
        /// </summary>
        Hybrid = 1,

        /// <summary>
        /// All data from physical hardware. NOT IMPLEMENTED - this is a placeholder so that the
        /// architecture and UI account for it now rather than being retrofitted later.
        /// </summary>
        Hardware = 2
    }

    /// <summary>
    /// The simulation clock.
    ///
    /// Everything time-dependent in ASTRA reads mission time from here rather than from Time.time.
    /// Three reasons, all of which matter for a live demonstration:
    ///
    /// 1. Pause. An evaluator asks "wait, go back - why did it turn there?" and the operator needs to
    ///    freeze the simulation and inspect state. Time.time cannot be paused without also freezing
    ///    the UI.
    /// 2. Time scale. Presentation mode compresses a slow climb, and a debugging session slows a fast
    ///    avoidance manoeuvre down to inspect it frame by frame.
    /// 3. Reproducibility. Mission time advances in fixed steps tied to the physics tick, so a
    ///    scripted demonstration follows the same trajectory on a fast machine and a slow one.
    ///    A demonstration that behaves differently on the presentation laptop than on the
    ///    development machine is a demonstration that will embarrass you.
    /// </summary>
    [DisallowMultipleComponent]
    public class SimClock : MonoBehaviour
    {
        // ------------------------------------------------------------------------------------
        // Configuration
        // ------------------------------------------------------------------------------------

        [Header("Physics rate")]
        [Tooltip("Physics steps per second. 100 Hz gives a 10 ms step, which is a reasonable " +
                 "compromise for multirotor attitude dynamics. Below about 50 Hz the attitude " +
                 "loop becomes visibly sloppy and can go unstable.")]
        [SerializeField] private int physicsHz = 100;

        [Header("Time scale")]
        [Range(0.05f, 8f)]
        [Tooltip("Simulation rate multiplier. Presentation mode adjusts this.")]
        [SerializeField] private float timeScale = 1f;

        // ------------------------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------------------------

        private static SimClock _instance;
        private double _missionTime;
        private bool _isPaused;
        private long _fixedStepCount;

        public static SimClock Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<SimClock>();
                }
                return _instance;
            }
        }

        /// <summary>
        /// Seconds of simulated time since the clock started, excluding paused time. This is the
        /// authoritative timestamp for telemetry, logs and flight records.
        /// </summary>
        public double MissionTime
        {
            get { return _missionTime; }
        }

        /// <summary>Number of fixed physics steps executed. Useful for deterministic scripting.</summary>
        public long FixedStepCount
        {
            get { return _fixedStepCount; }
        }

        public bool IsPaused
        {
            get { return _isPaused; }
        }

        public float TimeScale
        {
            get { return timeScale; }
        }

        /// <summary>The fixed timestep in seconds, i.e. 1 / physicsHz.</summary>
        public float FixedDeltaTime
        {
            get { return 1f / Mathf.Max(1, physicsHz); }
        }

        public int PhysicsHz
        {
            get { return physicsHz; }
        }

        // ------------------------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------------------------

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[SimClock] A second SimClock exists. Disabling this one.", this);
                enabled = false;
                return;
            }
            _instance = this;

            ApplyPhysicsRate();

            // Hand the log a way to timestamp entries without it needing to know about this type.
            EventLog.MissionTimeProvider = () => _missionTime;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                EventLog.MissionTimeProvider = null;
            }
        }

        private void FixedUpdate()
        {
            if (_isPaused)
            {
                return;
            }
            _missionTime += Time.fixedDeltaTime;
            _fixedStepCount++;
        }

        // ------------------------------------------------------------------------------------
        // Control
        // ------------------------------------------------------------------------------------

        private void ApplyPhysicsRate()
        {
            Time.fixedDeltaTime = FixedDeltaTime;

            // Cap how much catch-up work a single frame may do. Without this, one long frame (a
            // shader compile, a tile load) makes Unity run many physics steps back to back, which
            // eats the next frame too and can spiral into a visible stall. Three steps is enough
            // catch-up to absorb a hiccup without the death spiral.
            Time.maximumDeltaTime = FixedDeltaTime * 3f;

            EventLog.Info(LogSource.System,
                string.Format("Physics rate set to {0} Hz (fixed step {1:F1} ms)",
                    physicsHz, FixedDeltaTime * 1000f));
        }

        /// <summary>Freezes simulation. The UI keeps running so state can be inspected.</summary>
        public void Pause()
        {
            if (_isPaused)
            {
                return;
            }
            _isPaused = true;
            Time.timeScale = 0f;
            EventLog.Info(LogSource.System, "Simulation paused at T+" + _missionTime.ToString("F2") + "s");
        }

        public void Resume()
        {
            if (!_isPaused)
            {
                return;
            }
            _isPaused = false;
            Time.timeScale = timeScale;
            EventLog.Info(LogSource.System, "Simulation resumed");
        }

        public void TogglePause()
        {
            if (_isPaused)
            {
                Resume();
            }
            else
            {
                Pause();
            }
        }

        /// <summary>
        /// Sets the simulation rate multiplier. Note that raising this does NOT increase the physics
        /// step size - Unity runs more steps per frame instead - so flight dynamics stay valid.
        /// Above roughly 4x the machine may not keep up and the simulation will start dropping
        /// behind real time, which is harmless but means the wall clock and mission clock diverge.
        /// </summary>
        public void SetTimeScale(float scale)
        {
            timeScale = Mathf.Clamp(scale, 0.05f, 8f);
            if (!_isPaused)
            {
                Time.timeScale = timeScale;
            }
            EventLog.Info(LogSource.System, "Time scale set to " + timeScale.ToString("F2") + "x");
        }

        /// <summary>Restarts mission time. Called when a new mission begins.</summary>
        public void ResetMissionTime()
        {
            _missionTime = 0.0;
            _fixedStepCount = 0;
        }

        /// <summary>
        /// Formats mission time as T+MM:SS.s, the convention used on the GCS header.
        /// </summary>
        public string FormatMissionTime()
        {
            int totalSeconds = (int)_missionTime;
            int minutes = totalSeconds / 60;
            double seconds = _missionTime - minutes * 60;
            return string.Format("T+{0:00}:{1:00.0}", minutes, seconds);
        }

        private void OnValidate()
        {
            physicsHz = Mathf.Clamp(physicsHz, 50, 500);
        }
    }
}
