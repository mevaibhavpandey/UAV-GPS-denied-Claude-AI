using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Config;
using Astra.Core.Logging;

namespace Astra.Flight
{
    /// <summary>
    /// The flight controller: a cascaded attitude and position controller plus the flight state
    /// machine. This is ASTRA's stand-in for a Pixhawk running ArduPilot.
    ///
    /// CONTROL ARCHITECTURE
    /// --------------------
    /// Four nested loops, each running at the physics rate but each with a bandwidth an order of
    /// magnitude below the one inside it. Outermost to innermost:
    ///
    ///   POSITION  (metres)      -> target velocity
    ///   VELOCITY  (m/s)         -> target acceleration -> target tilt angle
    ///   ATTITUDE  (radians)     -> target body rate
    ///   RATE      (rad/s)       -> mixer demand -> motor commands
    ///
    /// and in parallel on the vertical axis:
    ///
    ///   ALTITUDE   (metres)     -> target climb rate
    ///   CLIMB RATE (m/s)        -> collective throttle offset from hover
    ///
    /// This is the same cascade ArduPilot and PX4 use, and the reason is not tradition. Each loop
    /// solves a problem at one timescale. A single loop from position error straight to motor command
    /// would have to be tuned for the slowest dynamics in the chain, which would leave it far too
    /// sluggish to reject a gust; tuned for the fastest, it would be violently unstable. Separating
    /// them lets each be tuned for what it actually controls.
    ///
    /// The separation of bandwidth is what makes it work. If the velocity loop were as fast as the
    /// rate loop they would fight each other, and the aircraft would oscillate. Keeping roughly a
    /// factor of ten between adjacent loops is the standard rule and the gains in UavConfiguration
    /// respect it.
    ///
    /// UNITS: all internal computation is in SI - radians, rad/s, metres, m/s - so the gains are
    /// directly comparable to ArduPilot parameters. Conversion to degrees happens only where a value
    /// is handed to the UI.
    ///
    /// WHAT THIS IS AND IS NOT
    /// ----------------------
    /// This is a SIMULATED flight controller. It reproduces the architecture, the state machine, the
    /// arming logic and the failsafe behaviour of a real autopilot closely enough that the concepts
    /// transfer and the tuning is a sensible starting point. It is not flight-certified software, it
    /// has not been through any airworthiness process, and it lacks a great deal that real autopilot
    /// firmware carries: sensor redundancy and voting, EKF-based state estimation with innovation
    /// gating, watchdogs, hardware fault handling, and years of accumulated field experience.
    /// </summary>
    [DisallowMultipleComponent]
    public class FlightControlSystem : MonoBehaviour, IFlightController
    {
        /// <summary>
        /// What the controller is currently trying to do. Distinct from FlightState: FlightState is
        /// the mission-level narrative shown to the operator, this is the control law in use. Several
        /// flight states share one control mode - Navigating, Avoiding and RejoiningRoute are all
        /// VelocityHold - and conflating the two would mean the state machine could not be extended
        /// without touching the control law.
        /// </summary>
        private enum ControlMode
        {
            Idle,
            Manual,
            VelocityHold,
            PositionHold,
            Takeoff,
            Landing,
            Brake
        }

        // ====================================================================================
        // CONFIGURATION
        // ====================================================================================

        [Header("References")]
        [SerializeField] private UavConfiguration config;
        [SerializeField] private QuadcopterPhysics physics;

        [Header("Arming")]
        [Tooltip("Refuse to arm below this battery fraction. Real autopilots have exactly this " +
                 "check, and it exists because taking off on a nearly flat pack is how aircraft are " +
                 "destroyed.")]
        [Range(0f, 1f)]
        [SerializeField] private float minimumArmingBattery = 0.25f;

        [Tooltip("Refuse to arm if the airframe is tilted more than this many degrees. A quadcopter " +
                 "that arms and spools up on a slope will tip over onto its propellers.")]
        [SerializeField] private float maximumArmingTiltDeg = 12f;

        [Tooltip("Require a GPS fix before arming. Left off by default so manual flight can be " +
                 "demonstrated without waiting for a fix, and so the GPS-denied scenario can start " +
                 "from a cold state. A real autonomous mission would require it.")]
        [SerializeField] private bool requireGpsToArm = false;

        [Header("Takeoff and landing")]
        [Tooltip("Climb rate used during an automatic takeoff, m/s. Deliberately gentler than the " +
                 "envelope maximum: a takeoff that leaps off the pad looks like a game.")]
        [SerializeField] private float takeoffClimbRateMps = 2.0f;

        [Tooltip("Descent rate during the main part of a landing, m/s.")]
        [SerializeField] private float landingDescentRateMps = 1.2f;

        [Tooltip("Height above ground at which the landing slows to its final rate, metres.")]
        [SerializeField] private float landingFlareHeightM = 4f;

        [Tooltip("Final descent rate below the flare height, m/s. Slow enough to touch down rather " +
                 "than arrive.")]
        [SerializeField] private float landingTouchdownRateMps = 0.35f;

        [Header("Failsafe")]
        [Tooltip("Disarm automatically after landing. Standard autopilot behaviour, and a genuine " +
                 "safety feature: a quadcopter with spinning propellers sitting on the ground with " +
                 "nobody expecting it to move is a hazard.")]
        [SerializeField] private bool autoDisarmAfterLanding = true;

        [Tooltip("Seconds on the ground with the motors idle before auto-disarm fires.")]
        [SerializeField] private float autoDisarmDelayS = 2.5f;

        // ====================================================================================
        // CONTROL LOOPS
        // ====================================================================================

        private readonly PidController _rateRoll = new PidController();
        private readonly PidController _ratePitch = new PidController();
        private readonly PidController _rateYaw = new PidController();
        private readonly PidController _velocityEast = new PidController();
        private readonly PidController _velocityNorth = new PidController();
        private readonly PidController _climbRate = new PidController();

        // ====================================================================================
        // STATE
        // ====================================================================================

        private FlightState _state = FlightState.Disarmed;
        private ControlSource _controlSource = ControlSource.None;
        private ControlMode _mode = ControlMode.Idle;
        private SubsystemStatus _status = SubsystemStatus.Offline;

        private bool _isArmed;
        private float _timeInState;
        private float _groundIdleTimer;

        // ---- Setpoints ----
        private Vector3 _targetVelocity;          // world m/s
        private Vector3 _targetPosition;          // world
        private float _targetAltitude;            // world Y
        private float _targetHeadingDeg;          // 0 = north, clockwise
        private float _commandedYawRateDegPerSec;
        private float _cruiseSpeedMps;
        private bool _headingHoldActive = true;

        // ---- Manual input ----
        private float _manualRoll;
        private float _manualPitch;
        private float _manualYaw;
        private float _manualThrottle = 0.5f;

        // ---- Launch reference ----
        private Vector3 _launchPosition;
        private float _launchAltitude;
        private bool _launchRecorded;

        // ---- Instrumentation ----
        private float _lastThrottleCommand;
        private float _targetRollDeg;
        private float _targetPitchDeg;
        private float _targetClimbRateMps;

        // ====================================================================================
        // IFlightController
        // ====================================================================================

        public string Name
        {
            get { return "ASTRA Simulated Flight Controller"; }
        }

        /// <summary>
        /// Declared as Simulated, not Demonstration. The distinction: this is a genuine
        /// implementation of a real control architecture operating on simulated physics, rather than a
        /// visual stand-in for something not implemented. It is still simulated, and the badge says so.
        /// </summary>
        public DataProvenance Provenance
        {
            get { return DataProvenance.Simulated; }
        }

        public SubsystemStatus Status { get { return _status; } }
        public bool IsArmed { get { return _isArmed; } }
        public FlightState State { get { return _state; } }
        public ControlSource CurrentControlSource { get { return _controlSource; } }

        // ---- Instrumentation for the diagnostics panel ----
        public float ThrottleCommand { get { return _lastThrottleCommand; } }
        public float TargetRollDeg { get { return _targetRollDeg; } }
        public float TargetPitchDeg { get { return _targetPitchDeg; } }
        public float TargetHeadingDeg { get { return _targetHeadingDeg; } }
        public float TargetClimbRateMps { get { return _targetClimbRateMps; } }
        public Vector3 TargetVelocity { get { return _targetVelocity; } }
        public float TimeInState { get { return _timeInState; } }
        public Vector3 LaunchPosition { get { return _launchPosition; } }
        public PidController RateRollLoop { get { return _rateRoll; } }
        public PidController RatePitchLoop { get { return _ratePitch; } }
        public PidController RateYawLoop { get { return _rateYaw; } }
        public PidController ClimbRateLoop { get { return _climbRate; } }

        /// <summary>Height above the launch point, metres.</summary>
        public float AltitudeAboveLaunchM
        {
            get
            {
                if (!_launchRecorded || physics == null)
                {
                    return 0f;
                }
                return physics.transform.position.y - _launchAltitude;
            }
        }

        // ====================================================================================
        // LIFECYCLE
        // ====================================================================================

        private void Awake()
        {
            if (physics == null)
            {
                physics = GetComponent<QuadcopterPhysics>();
            }
            if (config == null && physics != null)
            {
                config = physics.Config;
            }
        }

        private void OnEnable()
        {
            AstraServices.Register<IFlightController>(this);
        }

        private void OnDisable()
        {
            AstraServices.UnregisterIfCurrent<IFlightController>(this);
        }

        private void Start()
        {
            if (config == null || physics == null)
            {
                _status = SubsystemStatus.Error;
                EventLog.Error(LogSource.FlightController,
                    "Flight controller cannot initialise: configuration or physics reference missing");
                return;
            }

            ApplyGains();
            RecordLaunchPoint();

            TransitionTo(FlightState.Initialising, "Power on");
            _status = SubsystemStatus.Initialising;
        }

        /// <summary>
        /// Copies gains from the configuration into the loops.
        ///
        /// Output limits are worth explaining. The rate loops are limited to 0.45 rather than 1.0
        /// because the mixer gives every motor a unit coefficient on both roll and pitch: if roll
        /// alone could demand the full range there would be nothing left for pitch, and a diagonal
        /// manoeuvre would saturate immediately. Capping each axis below half leaves both axes room
        /// to act together, and leaves a little for yaw on top.
        /// </summary>
        public void ApplyGains()
        {
            if (config == null)
            {
                return;
            }

            // Vector3 gain ordering is (roll, pitch, yaw) - NOT body axes. Body axes would be
            // (pitch, yaw, roll) for (x, y, z), which is exactly the sort of silent mismatch that
            // produces an aircraft that rolls when told to pitch. Stated here once, applied here once.
            _rateRoll.SetGains(config.RateKp.x, config.RateKi.x, config.RateKd.x);
            _ratePitch.SetGains(config.RateKp.y, config.RateKi.y, config.RateKd.y);
            _rateYaw.SetGains(config.RateKp.z, config.RateKi.z, config.RateKd.z);

            _rateRoll.OutputLimit = 0.45f;
            _ratePitch.OutputLimit = 0.45f;
            _rateYaw.OutputLimit = 0.30f;

            _velocityEast.SetGains(config.VelocityKp, config.VelocityKi, config.VelocityKd);
            _velocityNorth.SetGains(config.VelocityKp, config.VelocityKi, config.VelocityKd);

            // Limited to the lateral acceleration the tilt limit permits: tan(maxTilt) * g. Asking
            // the velocity loop for more would produce a tilt command the attitude limiter clips
            // anyway, and the unusable excess would wind up the integrator.
            float maxLateralAccel = Mathf.Tan(config.MaxTiltAngleDeg * Mathf.Deg2Rad) * 9.80665f;
            _velocityEast.OutputLimit = maxLateralAccel;
            _velocityNorth.OutputLimit = maxLateralAccel;

            _climbRate.SetGains(config.ClimbRateKp, config.ClimbRateKi, config.ClimbRateKd);
            _climbRate.OutputLimit = 0.45f;
        }

        private void RecordLaunchPoint()
        {
            if (physics == null)
            {
                return;
            }
            _launchPosition = physics.transform.position;
            _launchAltitude = _launchPosition.y;
            _launchRecorded = true;
        }

        private void Update()
        {
            _timeInState += Time.deltaTime;

            if (_state == FlightState.Initialising && _timeInState > 1.2f)
            {
                // A brief initialisation period is not padding. Real autopilots need time for their
                // IMU to settle and their estimator to converge, and showing that honestly is better
                // than pretending a system is ready the instant it powers on.
                _status = SubsystemStatus.Ok;
                TransitionTo(FlightState.Disarmed, "Initialisation complete");
                EventLog.Success(LogSource.FlightController,
                    "Flight controller ready - press R to arm");
            }
        }

        // ====================================================================================
        // COMMANDS
        // ====================================================================================

        public bool TryArm(out string reason)
        {
            reason = string.Empty;

            if (_isArmed)
            {
                reason = "Already armed";
                return false;
            }

            if (_state == FlightState.Initialising)
            {
                reason = "Flight controller still initialising";
                AstraEvents.RaiseArmingRefused(reason);
                EventLog.Warning(LogSource.FlightController, "ARMING REFUSED: " + reason);
                return false;
            }

            if (_state != FlightState.Disarmed && _state != FlightState.Preflight &&
                !FlightStateInfo.IsTerminal(_state))
            {
                reason = "Cannot arm from state " + FlightStateInfo.ToDisplayName(_state);
                AstraEvents.RaiseArmingRefused(reason);
                EventLog.Warning(LogSource.FlightController, "ARMING REFUSED: " + reason);
                return false;
            }

            TransitionTo(FlightState.Preflight, "Arming requested");

            if (!RunPreflightChecks(out reason))
            {
                TransitionTo(FlightState.Disarmed, "Preflight failed");
                AstraEvents.RaiseArmingRefused(reason);
                EventLog.Warning(LogSource.FlightController, "ARMING REFUSED: " + reason);
                return false;
            }

            // Reset every integrator before taking authority. Skipping this is a real accident cause:
            // integral accumulated while the aircraft sat on the pad with an unsatisfiable setpoint
            // gets applied the instant the loops engage, and the aircraft lurches off the ground.
            ResetControlLoops();

            RecordLaunchPoint();

            _isArmed = true;
            physics.SetMotorsArmed(true);
            _mode = ControlMode.Idle;
            _targetHeadingDeg = physics.HeadingDeg;
            _headingHoldActive = true;

            TransitionTo(FlightState.Armed, "Preflight passed");
            AstraEvents.RaiseArmedStateChanged(true);
            EventLog.Success(LogSource.FlightController,
                "ARMED - motors at idle, throttle up or press T for auto takeoff");

            return true;
        }

        /// <summary>
        /// The preflight checklist.
        ///
        /// Every item here is a check a real autopilot performs, and every one exists because
        /// skipping it has destroyed aircraft. Reproducing them makes the demonstration credible to
        /// anyone in the room who has flown a multirotor, and it means the aircraft refuses to arm
        /// with a STATED REASON rather than silently doing nothing - which is the single most
        /// frustrating behaviour a flight controller can have.
        /// </summary>
        private bool RunPreflightChecks(out string reason)
        {
            reason = string.Empty;

            if (config == null || physics == null)
            {
                reason = "Airframe not configured";
                return false;
            }

            // ---- Configuration sanity ----
            if (config.ThrustToWeightRatio < 1.5f)
            {
                reason = string.Format(
                    "Thrust-to-weight ratio {0:F2} is below 1.5 - the aircraft cannot control " +
                    "attitude while climbing", config.ThrustToWeightRatio);
                return false;
            }

            // ---- Attitude ----
            float tilt = Mathf.Max(Mathf.Abs(physics.RollDeg), Mathf.Abs(physics.PitchDeg));
            if (tilt > maximumArmingTiltDeg)
            {
                reason = string.Format(
                    "Airframe tilted {0:F1} degrees, limit is {1:F0} - arming on a slope risks a " +
                    "propeller strike", tilt, maximumArmingTiltDeg);
                return false;
            }

            // ---- Motion ----
            if (physics.Velocity.magnitude > 0.5f)
            {
                reason = string.Format("Airframe is moving at {0:F1} m/s - it must be stationary",
                                       physics.Velocity.magnitude);
                return false;
            }

            // ---- Battery ----
            Astra.Drone.BatterySystem battery = physics.Battery;
            if (battery != null)
            {
                if (battery.StateOfCharge < minimumArmingBattery)
                {
                    reason = string.Format(
                        "Battery at {0:F0}%, minimum for arming is {1:F0}%",
                        battery.PercentRemaining, minimumArmingBattery * 100f);
                    return false;
                }
            }
            else
            {
                EventLog.Warning(LogSource.FlightController,
                    "Preflight: no battery system present, power checks skipped");
            }

            // ---- Motors ----
            Astra.Drone.MotorUnit[] motors = physics.Motors;
            for (int i = 0; i < motors.Length; i++)
            {
                if (motors[i] == null)
                {
                    reason = "Motor " + (i + 1) + " is missing from the airframe";
                    return false;
                }
                if (!motors[i].IsHealthy)
                {
                    reason = string.Format("Motor {0} reports {1}",
                                           i + 1, motors[i].HealthDescription);
                    return false;
                }
            }

            // ---- Geodetic reference ----
            if (!Astra.Core.Geo.GeoReference.Exists)
            {
                reason = "No geodetic reference in the scene - position cannot be resolved";
                return false;
            }

            // ---- Position estimate ----
            ILocalizationProvider localization = AstraServices.Get<ILocalizationProvider>();
            if (localization != null && !localization.IsConverged)
            {
                reason = "Position estimate has not converged";
                return false;
            }

            // ---- GPS, if required ----
            if (requireGpsToArm)
            {
                ISensorProvider sensors = AstraServices.Get<ISensorProvider>();
                if (sensors == null || !sensors.GpsAvailable)
                {
                    reason = "No GPS fix and GPS is required for arming";
                    return false;
                }
            }

            EventLog.Success(LogSource.FlightController, "Preflight checks passed");
            return true;
        }

        public void Disarm()
        {
            if (!_isArmed)
            {
                return;
            }

            // Warn if disarming in the air but do it anyway. A disarm command must never be refused:
            // it is the operator's last resort, and a flight controller that argues with it is worse
            // than one that obeys a mistake. Real autopilots do gate disarm in flight for exactly the
            // opposite reason, which is a legitimate design disagreement - noted here rather than
            // glossed over.
            if (FlightStateInfo.IsAirborne(_state) && !physics.IsLanded)
            {
                EventLog.Critical(LogSource.FlightController,
                    string.Format("DISARM IN FLIGHT at {0:F1} m AGL - motors stopped",
                                  physics.AltitudeAglM));
            }

            _isArmed = false;
            physics.SetMotorsArmed(false);
            _mode = ControlMode.Idle;
            ResetControlLoops();

            TransitionTo(FlightState.Disarmed, "Disarm commanded");
            AstraEvents.RaiseArmedStateChanged(false);
            SetControlSource(ControlSource.None);
            EventLog.Info(LogSource.FlightController, "DISARMED");
        }

        public void CommandTakeoff(float targetAltitudeAgl)
        {
            if (!_isArmed)
            {
                EventLog.Warning(LogSource.FlightController,
                    "Takeoff refused: aircraft is not armed");
                return;
            }

            if (FlightStateInfo.IsAirborne(_state))
            {
                EventLog.Warning(LogSource.FlightController,
                    "Takeoff refused: already airborne");
                return;
            }

            RecordLaunchPoint();
            _targetAltitude = _launchAltitude + Mathf.Max(1f, targetAltitudeAgl);
            _targetPosition = physics.transform.position;
            _targetHeadingDeg = physics.HeadingDeg;
            _mode = ControlMode.Takeoff;

            // Preload the climb-rate integrator with the throttle needed to hover. Without this the
            // integrator has to discover the hover point from scratch while the aircraft sits on the
            // pad producing not quite enough thrust - which looks hesitant and, more importantly,
            // wastes the first second of authority.
            _climbRate.PresetIntegralForOutput(0f);

            TransitionTo(FlightState.Takeoff, "Takeoff commanded");
            EventLog.Info(LogSource.FlightController, string.Format(
                "TAKEOFF to {0:F1} m AGL at {1:F1} m/s", targetAltitudeAgl, takeoffClimbRateMps));
        }

        public void CommandHover()
        {
            if (!_isArmed)
            {
                return;
            }

            _targetPosition = physics.transform.position;
            _targetAltitude = physics.transform.position.y;
            _targetVelocity = Vector3.zero;
            _mode = ControlMode.PositionHold;
            _headingHoldActive = true;
            _targetHeadingDeg = physics.HeadingDeg;

            if (_state != FlightState.Hover)
            {
                TransitionTo(FlightState.Hover, "Hover commanded");
            }
        }

        public void CommandVelocity(Vector3 worldVelocity, float yawRateDegPerSec)
        {
            if (!_isArmed)
            {
                return;
            }

            // Clamp into the flight envelope here rather than trusting the caller. The navigation
            // stack is a separate subsystem and could ask for anything; the flight controller owns
            // the envelope, exactly as a real autopilot does.
            Vector3 horizontal = new Vector3(worldVelocity.x, 0f, worldVelocity.z);
            if (horizontal.magnitude > config.MaxHorizontalSpeedMps)
            {
                horizontal = horizontal.normalized * config.MaxHorizontalSpeedMps;
            }

            float vertical = Mathf.Clamp(worldVelocity.y,
                                         -config.MaxDescentRateMps,
                                         config.MaxClimbRateMps);

            _targetVelocity = new Vector3(horizontal.x, vertical, horizontal.z);
            _commandedYawRateDegPerSec = Mathf.Clamp(yawRateDegPerSec,
                                                     -config.MaxYawRateDegPerSec,
                                                     config.MaxYawRateDegPerSec);
            _headingHoldActive = Mathf.Abs(_commandedYawRateDegPerSec) < 0.5f;

            // Altitude target tracks the current altitude while a vertical velocity is commanded, so
            // that releasing the command holds the altitude reached rather than snapping back to a
            // stale target.
            if (Mathf.Abs(vertical) > 0.05f)
            {
                _targetAltitude = physics.transform.position.y;
            }

            _mode = ControlMode.VelocityHold;
        }

        public void CommandGoTo(Vector3 worldPosition, float cruiseSpeedMps)
        {
            if (!_isArmed)
            {
                return;
            }

            _targetPosition = worldPosition;
            _targetAltitude = worldPosition.y;
            _cruiseSpeedMps = Mathf.Clamp(cruiseSpeedMps, 0.5f, config.MaxHorizontalSpeedMps);
            _mode = ControlMode.PositionHold;
        }

        public void CommandHeading(float headingDegrees)
        {
            _targetHeadingDeg = NormaliseHeading(headingDegrees);
            _headingHoldActive = true;
            _commandedYawRateDegPerSec = 0f;
        }

        public void CommandLand()
        {
            if (!_isArmed)
            {
                return;
            }

            _targetPosition = physics.transform.position;
            _mode = ControlMode.Landing;
            _headingHoldActive = true;

            TransitionTo(FlightState.Landing, "Land commanded");
            EventLog.Info(LogSource.FlightController, string.Format(
                "LANDING from {0:F1} m AGL", physics.AltitudeAglM));
        }

        public void CommandEmergencyBrake()
        {
            if (!_isArmed)
            {
                return;
            }

            _targetVelocity = Vector3.zero;
            _targetPosition = physics.transform.position;
            _targetAltitude = physics.transform.position.y;
            _mode = ControlMode.Brake;

            // Note what this does NOT do: it does not zero the velocity. It commands zero velocity
            // and lets the aircraft decelerate at whatever rate its tilt limit allows. A quadcopter
            // travelling at 12 m/s needs roughly 1.7 seconds and 10 metres to stop at 6.9 m/s^2, and
            // pretending otherwise would make every avoidance margin in the demonstration wrong.
            EventLog.Warning(LogSource.FlightController, string.Format(
                "EMERGENCY BRAKE at {0:F1} m/s - stopping distance approximately {1:F1} m",
                physics.GroundSpeedMps, EstimateStoppingDistance()));
        }

        /// <summary>
        /// Distance needed to stop from the present speed, metres. v^2 / (2a) with a from the tilt
        /// limit. Shown to the operator during a brake so the margin is a number rather than a hope.
        /// </summary>
        public float EstimateStoppingDistance()
        {
            float v = physics.GroundSpeedMps;
            float a = Mathf.Tan(config.MaxTiltAngleDeg * Mathf.Deg2Rad) * 9.80665f;
            if (a < 0.1f)
            {
                return float.PositiveInfinity;
            }
            return v * v / (2f * a);
        }

        /// <summary>
        /// Accepts operator stick positions. Called every frame by the input layer, including when the
        /// sticks are centred.
        ///
        /// The mode arbitration below is not incidental plumbing - it is two genuine safety behaviours:
        ///
        /// 1. ARMING MUST NOT CAUSE A TAKEOFF. If centred sticks put the controller straight into
        ///    altitude hold, the aircraft would hold the altitude it happens to be at, spool up to
        ///    hover thrust and lift off the instant it was armed. On a real aircraft that is how people
        ///    get hurt. So arming leaves the motors at idle and the controller stays in Idle until the
        ///    operator deliberately raises the throttle past centre - exactly what a real multirotor
        ///    does.
        ///
        /// 2. STICK INPUT MUST NOT SILENTLY CANCEL AN AUTOMATIC SEQUENCE. An automatic takeoff, a
        ///    landing or a position hold should not be abandoned because the operator's finger brushed
        ///    a key. Overriding requires a deliberate deflection, and when it happens it is logged, so
        ///    the record shows an operator took control rather than the sequence mysteriously failing.
        /// </summary>
        public void SetManualInput(float roll, float pitch, float yaw, float throttle)
        {
            _manualRoll = Mathf.Clamp(roll, -1f, 1f);
            _manualPitch = Mathf.Clamp(pitch, -1f, 1f);
            _manualYaw = Mathf.Clamp(yaw, -1f, 1f);
            _manualThrottle = Mathf.Clamp01(throttle);

            if (_controlSource != ControlSource.Manual || !_isArmed)
            {
                return;
            }

            if (_mode == ControlMode.Manual)
            {
                return;
            }

            const float deflectionThreshold = 0.06f;

            bool throttleRaised = _manualThrottle > 0.5f + deflectionThreshold;
            bool attitudeDeflected =
                Mathf.Abs(_manualRoll) > deflectionThreshold ||
                Mathf.Abs(_manualPitch) > deflectionThreshold ||
                Mathf.Abs(_manualYaw) > deflectionThreshold;
            bool throttleDeflected =
                Mathf.Abs(_manualThrottle - 0.5f) > deflectionThreshold;

            // ---- Case 1: armed and idle on the ground ----
            if (_mode == ControlMode.Idle)
            {
                // Only a raised throttle leaves idle. A lowered or centred throttle keeps the motors
                // spinning at idle, which is what the operator expects and what is safe.
                if (throttleRaised)
                {
                    EnterManualControl("Throttle raised");
                }
                return;
            }

            // ---- Case 2: an automatic sequence is running ----
            if (_mode == ControlMode.Takeoff || _mode == ControlMode.Landing ||
                _mode == ControlMode.PositionHold || _mode == ControlMode.Brake)
            {
                if (attitudeDeflected || throttleDeflected)
                {
                    string what = _mode == ControlMode.Takeoff ? "automatic takeoff"
                        : _mode == ControlMode.Landing ? "landing"
                        : _mode == ControlMode.Brake ? "emergency brake"
                        : "position hold";

                    EventLog.Warning(LogSource.Operator,
                        "Operator stick input overrides " + what + " - manual control assumed");
                    EnterManualControl("Operator override");
                }
                return;
            }

            // ---- Case 3: anything else under manual authority ----
            EnterManualControl("Manual control");
        }

        /// <summary>
        /// Switches into manual control, capturing the present altitude and heading as the hold
        /// targets so the handover does not command a jump.
        /// </summary>
        private void EnterManualControl(string reason)
        {
            _mode = ControlMode.Manual;
            _targetAltitude = physics.transform.position.y;
            _targetHeadingDeg = physics.HeadingDeg;

            if (physics.IsLanded)
            {
                ResetControlLoops();
            }
            else
            {
                ResetControlLoopsPreservingMeasurement();
            }

            if (_state == FlightState.Armed || _state == FlightState.Takeoff)
            {
                TransitionTo(FlightState.Takeoff, reason);
            }
            else if (_state == FlightState.Landing || _state == FlightState.Emergency)
            {
                // The operator has taken the aircraft back off an automatic sequence. Leaving the
                // state as Landing while it climbs away under manual control would make the state
                // display a lie, and the state display is the thing the audience is reading.
                TransitionTo(FlightState.Hover, reason);
            }
        }

        public void SetControlSource(ControlSource source)
        {
            if (_controlSource == source)
            {
                return;
            }

            ControlSource previous = _controlSource;
            _controlSource = source;

            // Reset the loops on every handover. The integrator state that suited the outgoing
            // controller is wrong for the incoming one, and carrying it across produces a transient
            // at exactly the moment the operator is watching for a smooth handover.
            //
            // Which reset matters. On the ground a bare Reset is right. In the air the measurement
            // history must be preserved, or the first step differentiates against a stale measurement
            // and the D term produces a spike - a handover that jolts the aircraft, which is the
            // opposite of what a handover is for.
            if (physics != null && !physics.IsLanded)
            {
                ResetControlLoopsPreservingMeasurement();
            }
            else
            {
                ResetControlLoops();
            }

            if (physics != null)
            {
                _targetHeadingDeg = physics.HeadingDeg;
                _targetPosition = physics.transform.position;
                _targetAltitude = physics.transform.position.y;
            }

            AstraEvents.RaiseControlSourceChanged(previous, source);
            EventLog.Info(LogSource.FlightController,
                "Control source: " + previous + " -> " + source);
        }

        /// <summary>
        /// Puts the aircraft into the emergency state. Called by failsafe logic.
        /// </summary>
        public void EnterEmergency(string cause)
        {
            SetControlSource(ControlSource.Failsafe);
            _mode = ControlMode.Landing;
            TransitionTo(FlightState.Emergency, cause);
            EventLog.Critical(LogSource.FlightController, "EMERGENCY: " + cause);
            AstraEvents.RaiseFailsafeTriggered(cause);
        }

        /// <summary>
        /// Sets the flight state from the autonomy layer. The navigation stack owns the narrative
        /// states - Navigating, Avoiding, RejoiningRoute and so on - because it is the thing that
        /// knows what it is doing. The flight controller owns the states tied to control mode, and
        /// refuses changes that would contradict them.
        /// </summary>
        public void ReportNavigationState(FlightState state, string reason)
        {
            if (!_isArmed)
            {
                return;
            }

            // The flight controller's own states outrank the navigation narrative. If a landing or an
            // emergency is in progress, the autonomy layer does not get to say the aircraft is
            // navigating.
            if (_state == FlightState.Emergency ||
                _state == FlightState.Landing ||
                _state == FlightState.Takeoff)
            {
                return;
            }

            if (state != _state)
            {
                TransitionTo(state, reason);
            }
        }

        // ====================================================================================
        // STATE MACHINE
        // ====================================================================================

        private void TransitionTo(FlightState next, string reason)
        {
            if (_state == next)
            {
                return;
            }

            FlightState previous = _state;

            // Validate, but do not block.
            //
            // This is a deliberate choice and worth defending. A hard-blocking transition table sounds
            // more rigorous, and in flight-certified firmware it would be right. Here it would mean
            // that one missing entry in the table silently freezes the aircraft mid-demonstration, with
            // no indication why - the failure mode is invisible and catastrophic to the presentation.
            //
            // Warning instead makes an illegal transition loud in the log and in the Unity console
            // during development, where it gets fixed, without turning a table omission into a
            // grounded aircraft in front of an audience. The table is the specification; the warning
            // is how a violation of it gets found.
            if (!IsTransitionLegal(previous, next))
            {
                Debug.LogWarning(string.Format(
                    "[ASTRA] Unexpected flight state transition {0} -> {1} ({2}). Permitted, but this " +
                    "combination is not in the state machine specification and should be reviewed.",
                    previous, next, reason));
                EventLog.Warning(LogSource.FlightController, string.Format(
                    "Unspecified state transition {0} -> {1}",
                    FlightStateInfo.ToDisplayName(previous),
                    FlightStateInfo.ToDisplayName(next)));
            }

            _state = next;
            _timeInState = 0f;

            AstraEvents.RaiseFlightStateChanged(previous, next);

            LogSeverity severity = LogSeverity.Info;
            if (next == FlightState.Emergency || next == FlightState.MissionAborted)
            {
                severity = LogSeverity.Critical;
            }
            else if (next == FlightState.ObstacleDetected || next == FlightState.Avoiding)
            {
                severity = LogSeverity.Warning;
            }
            else if (next == FlightState.MissionComplete || next == FlightState.TargetReached)
            {
                severity = LogSeverity.Success;
            }

            EventLog.Write(severity, LogSource.FlightController,
                string.Format("{0} -> {1} ({2})",
                    FlightStateInfo.ToDisplayName(previous),
                    FlightStateInfo.ToDisplayName(next),
                    reason));
        }

        /// <summary>
        /// The legal transition table.
        ///
        /// Written as a switch on the source state rather than a two-dimensional array, because the
        /// switch documents itself: reading it tells you what can follow each state and why, whereas a
        /// 17x17 boolean matrix tells you nothing without a legend.
        ///
        /// Two transitions are legal from everywhere and are handled before the switch: Emergency,
        /// because an emergency can arise at any point and a state machine that could not enter it
        /// would be a hazard, and MissionAborted, because the operator may abort at any time.
        /// </summary>
        private static bool IsTransitionLegal(FlightState from, FlightState to)
        {
            // Always permitted, from any state.
            if (to == FlightState.Emergency || to == FlightState.MissionAborted)
            {
                return true;
            }

            // Landing is reachable from any airborne state: every airborne state must have a way down.
            if (to == FlightState.Landing && FlightStateInfo.IsAirborne(from))
            {
                return true;
            }

            switch (from)
            {
                case FlightState.Disarmed:
                    return to == FlightState.Initialising || to == FlightState.Preflight;

                case FlightState.Initialising:
                    return to == FlightState.Disarmed;

                case FlightState.Preflight:
                    // Either the checks pass and it arms, or they fail and it returns to disarmed.
                    return to == FlightState.Armed || to == FlightState.Disarmed;

                case FlightState.Armed:
                    return to == FlightState.Takeoff || to == FlightState.Disarmed;

                case FlightState.Takeoff:
                    return to == FlightState.Hover || to == FlightState.Navigating;

                case FlightState.Hover:
                    return to == FlightState.Navigating || to == FlightState.ReturnHome ||
                           to == FlightState.TargetApproach || to == FlightState.Disarmed ||
                           to == FlightState.ObstacleDetected;

                case FlightState.Navigating:
                    return to == FlightState.ObstacleDetected || to == FlightState.TargetApproach ||
                           to == FlightState.Hover || to == FlightState.ReturnHome ||
                           to == FlightState.RejoiningRoute;

                case FlightState.ObstacleDetected:
                    // Detection either escalates into an avoidance or clears back to the route.
                    return to == FlightState.Avoiding || to == FlightState.Navigating ||
                           to == FlightState.Hover;

                case FlightState.Avoiding:
                    return to == FlightState.RejoiningRoute || to == FlightState.ObstacleDetected ||
                           to == FlightState.Hover || to == FlightState.Navigating;

                case FlightState.RejoiningRoute:
                    return to == FlightState.Navigating || to == FlightState.ObstacleDetected ||
                           to == FlightState.TargetApproach;

                case FlightState.TargetApproach:
                    return to == FlightState.TargetReached || to == FlightState.ObstacleDetected ||
                           to == FlightState.Navigating || to == FlightState.Hover;

                case FlightState.TargetReached:
                    return to == FlightState.ReturnHome || to == FlightState.Navigating ||
                           to == FlightState.Hover;

                case FlightState.ReturnHome:
                    return to == FlightState.ObstacleDetected || to == FlightState.Hover ||
                           to == FlightState.Navigating;

                case FlightState.Landing:
                    // A landing can be abandoned - the operator takes over, or a go-around is needed.
                    return to == FlightState.MissionComplete || to == FlightState.Disarmed ||
                           to == FlightState.Hover;

                case FlightState.MissionComplete:
                case FlightState.MissionAborted:
                    return to == FlightState.Disarmed || to == FlightState.Preflight;

                case FlightState.Emergency:
                    return to == FlightState.Disarmed || to == FlightState.Hover ||
                           to == FlightState.MissionAborted;

                default:
                    return false;
            }
        }

        // ====================================================================================
        // CONTROL LOOP
        // ====================================================================================

        private void FixedUpdate()
        {
            if (config == null || physics == null)
            {
                return;
            }

            float dt = Time.fixedDeltaTime;

            if (!_isArmed)
            {
                physics.SetMixerDemand(0f, 0f, 0f, 0f);
                _lastThrottleCommand = 0f;
                return;
            }

            UpdateStateProgression(dt);

            // ---- Determine setpoints for this step, per control mode ----
            float throttle;
            float targetRollRad = 0f;
            float targetPitchRad = 0f;
            float targetYawRateRadPerSec = 0f;

            switch (_mode)
            {
                case ControlMode.Idle:
                    // Armed but not commanded: motors at idle, attitude levelled so the aircraft sits
                    // square on the pad rather than drifting into a lean.
                    throttle = config.ArmedIdleThrottle;
                    break;

                case ControlMode.Manual:
                    throttle = ComputeManualControl(dt, out targetRollRad, out targetPitchRad,
                                                    out targetYawRateRadPerSec);
                    break;

                case ControlMode.Takeoff:
                    throttle = ComputeTakeoffControl(dt, out targetRollRad, out targetPitchRad,
                                                     out targetYawRateRadPerSec);
                    break;

                case ControlMode.Landing:
                    throttle = ComputeLandingControl(dt, out targetRollRad, out targetPitchRad,
                                                     out targetYawRateRadPerSec);
                    break;

                case ControlMode.PositionHold:
                    throttle = ComputePositionControl(dt, out targetRollRad, out targetPitchRad,
                                                      out targetYawRateRadPerSec);
                    break;

                case ControlMode.Brake:
                    _targetVelocity = Vector3.zero;
                    throttle = ComputeVelocityControl(dt, out targetRollRad, out targetPitchRad,
                                                      out targetYawRateRadPerSec);
                    break;

                case ControlMode.VelocityHold:
                default:
                    throttle = ComputeVelocityControl(dt, out targetRollRad, out targetPitchRad,
                                                      out targetYawRateRadPerSec);
                    break;
            }

            _targetRollDeg = targetRollRad * Mathf.Rad2Deg;
            _targetPitchDeg = targetPitchRad * Mathf.Rad2Deg;
            _lastThrottleCommand = throttle;

            // ---- Attitude loop: angle error (rad) -> target body rate (rad/s) ----
            float rollRad = physics.RollDeg * Mathf.Deg2Rad;
            float pitchRad = physics.PitchDeg * Mathf.Deg2Rad;

            float targetRollRate = config.AttitudeKp.x * (targetRollRad - rollRad);
            float targetPitchRate = config.AttitudeKp.y * (targetPitchRad - pitchRad);

            // ---- Rate loop: rate error (rad/s) -> mixer demand ----
            // In Unity coordinates:
            // - Rolling right (right wing down) produces negative Z angular velocity.
            // - Pitching up (nose up) produces negative X angular velocity.
            // - Yawing right (clockwise) produces negative Y angular velocity.
            // Inverting them gives positive = roll right, pitch up, yaw right, matching PID & Mixer.
            Vector3 bodyRatesDeg = physics.BodyAngularVelocityDeg();
            float rollRateRad = -bodyRatesDeg.z * Mathf.Deg2Rad;
            float pitchRateRad = -bodyRatesDeg.x * Mathf.Deg2Rad;
            float yawRateRad = -bodyRatesDeg.y * Mathf.Deg2Rad;

            float rollDemand = _rateRoll.Update(targetRollRate, rollRateRad, dt);
            float pitchDemand = _ratePitch.Update(targetPitchRate, pitchRateRad, dt);
            float yawDemand = _rateYaw.Update(targetYawRateRadPerSec, yawRateRad, dt);

            physics.SetMixerDemand(throttle, rollDemand, pitchDemand, yawDemand);
        }

        /// <summary>
        /// Advances the states the flight controller itself owns: takeoff completion, landing
        /// completion, auto-disarm.
        /// </summary>
        private void UpdateStateProgression(float dt)
        {
            switch (_state)
            {
                case FlightState.Takeoff:
                {
                    if (_mode == ControlMode.Takeoff)
                    {
                        // Automatic takeoff: complete when the target altitude is reached and the
                        // climb has settled.
                        float error = _targetAltitude - physics.transform.position.y;
                        if (error < 0.6f && Mathf.Abs(physics.VerticalSpeedMps) < 0.4f)
                        {
                            _targetPosition = physics.transform.position;
                            _mode = ControlMode.PositionHold;
                            TransitionTo(FlightState.Hover, "Target altitude reached");
                            EventLog.Success(LogSource.FlightController, string.Format(
                                "Hovering at {0:F1} m AGL", physics.AltitudeAglM));
                        }
                    }
                    else if (_mode == ControlMode.Manual)
                    {
                        // Manual departure: the aircraft is off the ground and under control once it
                        // is clear of the pad and no longer climbing hard. Note the state used for
                        // airborne manual flight is Hover, because the specified state machine has no
                        // dedicated manual-flight state - the states describe the mission narrative,
                        // and under manual authority the narrative is simply that the aircraft is up.
                        if (!physics.IsLanded && physics.AltitudeAglM > 1.5f)
                        {
                            TransitionTo(FlightState.Hover, "Airborne under manual control");
                        }
                    }
                    break;
                }

                case FlightState.Landing:
                case FlightState.Emergency:
                {
                    if (physics.IsLanded)
                    {
                        _groundIdleTimer += dt;
                        if (autoDisarmAfterLanding && _groundIdleTimer > autoDisarmDelayS)
                        {
                            EventLog.Success(LogSource.FlightController,
                                "Touchdown confirmed - auto-disarming");
                            bool wasEmergency = _state == FlightState.Emergency;
                            Disarm();
                            if (!wasEmergency)
                            {
                                TransitionTo(FlightState.MissionComplete, "Landed and disarmed");
                            }
                            else
                            {
                                TransitionTo(FlightState.MissionAborted,
                                             "Emergency landing complete");
                            }
                        }
                    }
                    else
                    {
                        _groundIdleTimer = 0f;
                    }
                    break;
                }

                default:
                    _groundIdleTimer = 0f;
                    break;
            }
        }

        // ------------------------------------------------------------------------------------
        // Control modes
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Manual flight.
        ///
        /// Implemented as an ALTITUDE HOLD mode rather than raw throttle passthrough: the throttle
        /// axis commands a climb RATE, with centre stick holding altitude, and the stick axes command
        /// a tilt ANGLE rather than a rotation rate. This corresponds to ArduPilot's AltHold mode.
        ///
        /// Chosen deliberately over raw Stabilize. Raw throttle passthrough means the operator has to
        /// hunt for the hover point continuously, and a demonstration where the presenter is visibly
        /// fighting to hold altitude while explaining the autonomy stack is a bad demonstration. The
        /// aircraft is still fully physics-driven either way - altitude hold is a control law, not a
        /// cheat - and this is what an operator would actually select on a real aircraft for this kind
        /// of flying.
        /// </summary>
        private float ComputeManualControl(float dt, out float targetRollRad,
                                           out float targetPitchRad, out float targetYawRateRad)
        {
            float maxTilt = config.MaxTiltAngleDeg * Mathf.Deg2Rad;

            // A on the keyboard rolls left, D rolls right. Roll positive = right side down, so the
            // input maps straight through.
            targetRollRad = _manualRoll * maxTilt;

            // W moves forward, and a quadcopter moves forward by pitching NOSE DOWN. So a positive
            // forward input becomes a negative pitch angle. This inversion is the single most common
            // place to introduce a sign error in a quadcopter controller.
            targetPitchRad = -_manualPitch * maxTilt;

            targetYawRateRad = _manualYaw * config.MaxYawRateDegPerSec * Mathf.Deg2Rad;
            _headingHoldActive = false;

            // Throttle axis: 0.5 is neutral and holds altitude, 1 is maximum climb, 0 is maximum
            // descent. A dead band around centre stops keyboard or stick noise from producing a slow
            // unintended drift, which on a real aircraft is genuinely disconcerting.
            float axis = (_manualThrottle - 0.5f) * 2f;
            if (Mathf.Abs(axis) < 0.08f)
            {
                axis = 0f;
            }

            // On the ground with the throttle at or below centre, return to idle rather than holding
            // an altitude. This is what a real multirotor does after touchdown, and it re-arms the
            // gate in SetManualInput so the operator has to raise the throttle again to take off.
            // Without it the aircraft would sit on the ground at hover thrust, poised to drift off.
            if (physics.IsLanded && axis <= 0f)
            {
                _mode = ControlMode.Idle;
                _targetAltitude = physics.transform.position.y;
                _climbRate.Reset();
                targetRollRad = 0f;
                targetPitchRad = 0f;
                return config.ArmedIdleThrottle;
            }

            float commandedClimbRate = axis > 0f
                ? axis * config.MaxClimbRateMps
                : axis * config.MaxDescentRateMps;

            if (Mathf.Abs(axis) < 0.001f)
            {
                // Neutral stick: hold the altitude currently held rather than an accumulating target.
                return ComputeAltitudeHoldThrottle(dt, _targetAltitude, targetRollRad, targetPitchRad);
            }

            _targetAltitude = physics.transform.position.y;
            return ComputeClimbRateThrottle(dt, commandedClimbRate, targetRollRad, targetPitchRad);
        }

        private float ComputeTakeoffControl(float dt, out float targetRollRad,
                                            out float targetPitchRad, out float targetYawRateRad)
        {
            // Hold horizontal position over the pad while climbing. A takeoff that drifts sideways is
            // how aircraft find fences.
            ComputeHorizontalPositionHold(dt, _targetPosition, out targetRollRad, out targetPitchRad);
            targetYawRateRad = ComputeHeadingHoldYawRate();

            float remaining = _targetAltitude - physics.transform.position.y;
            float climbRate = Mathf.Min(takeoffClimbRateMps, Mathf.Max(0.3f, remaining * 1.5f));

            _targetClimbRateMps = climbRate;
            return ComputeClimbRateThrottle(dt, climbRate, targetRollRad, targetPitchRad);
        }

        private float ComputeLandingControl(float dt, out float targetRollRad,
                                            out float targetPitchRad, out float targetYawRateRad)
        {
            ComputeHorizontalPositionHold(dt, _targetPosition, out targetRollRad, out targetPitchRad);
            targetYawRateRad = ComputeHeadingHoldYawRate();

            // Two-stage descent with a flare. Descending all the way at cruise descent rate produces
            // a hard arrival; slowing below the flare height gives a touchdown. This is also how a
            // real autopilot lands, and for the same reason.
            float agl = physics.AltitudeAglM;
            float descentRate;

            if (agl < 0f)
            {
                // Ground height unknown. Descend slowly rather than confidently - the correct
                // response to not knowing where the ground is.
                descentRate = -landingTouchdownRateMps;
            }
            else if (agl < landingFlareHeightM)
            {
                float t = Mathf.Clamp01(agl / Mathf.Max(0.1f, landingFlareHeightM));
                descentRate = -Mathf.Lerp(landingTouchdownRateMps, landingDescentRateMps, t);
            }
            else
            {
                descentRate = -landingDescentRateMps;
            }

            if (physics.GroundContact)
            {
                // On the ground: wind the throttle down rather than cutting it, so the aircraft
                // settles onto its gear instead of dropping the last few centimetres.
                _targetClimbRateMps = 0f;
                return Mathf.Max(0f, _lastThrottleCommand - dt * 0.8f);
            }

            _targetClimbRateMps = descentRate;
            return ComputeClimbRateThrottle(dt, descentRate, targetRollRad, targetPitchRad);
        }

        private float ComputePositionControl(float dt, out float targetRollRad,
                                             out float targetPitchRad, out float targetYawRateRad)
        {
            ComputeHorizontalPositionHold(dt, _targetPosition, out targetRollRad, out targetPitchRad);
            targetYawRateRad = ComputeHeadingHoldYawRate();
            return ComputeAltitudeHoldThrottle(dt, _targetAltitude, targetRollRad, targetPitchRad);
        }

        private float ComputeVelocityControl(float dt, out float targetRollRad,
                                             out float targetPitchRad, out float targetYawRateRad)
        {
            ComputeVelocityToTilt(dt, _targetVelocity, out targetRollRad, out targetPitchRad);

            if (_headingHoldActive)
            {
                targetYawRateRad = ComputeHeadingHoldYawRate();
            }
            else
            {
                targetYawRateRad = _commandedYawRateDegPerSec * Mathf.Deg2Rad;
                _targetHeadingDeg = physics.HeadingDeg;
            }

            if (Mathf.Abs(_targetVelocity.y) > 0.05f)
            {
                _targetClimbRateMps = _targetVelocity.y;
                return ComputeClimbRateThrottle(dt, _targetVelocity.y,
                                                targetRollRad, targetPitchRad);
            }

            return ComputeAltitudeHoldThrottle(dt, _targetAltitude, targetRollRad, targetPitchRad);
        }

        // ------------------------------------------------------------------------------------
        // Loop stages
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Position loop: position error -> target velocity, then the velocity loop.
        /// </summary>
        private void ComputeHorizontalPositionHold(float dt, Vector3 target,
                                                   out float targetRollRad, out float targetPitchRad)
        {
            Vector3 position = physics.transform.position;
            Vector3 error = new Vector3(target.x - position.x, 0f, target.z - position.z);

            Vector3 desiredVelocity = error * config.PositionKp;

            float speedLimit = _cruiseSpeedMps > 0.1f
                ? Mathf.Min(_cruiseSpeedMps, config.MaxHorizontalSpeedMps)
                : config.MaxHorizontalSpeedMps;

            if (desiredVelocity.magnitude > speedLimit)
            {
                desiredVelocity = desiredVelocity.normalized * speedLimit;
            }

            ComputeVelocityToTilt(dt, desiredVelocity, out targetRollRad, out targetPitchRad);
        }

        /// <summary>
        /// Velocity loop: velocity error -> target acceleration -> tilt angle.
        ///
        /// The conversion from acceleration to tilt is tilt = atan(a / g), which is exact rather than
        /// an approximation: a multirotor accelerates horizontally by pointing part of its thrust
        /// vector sideways, and the ratio of the horizontal to the vertical component IS the tangent
        /// of the tilt angle. Using the small-angle approximation a/g instead would understate the
        /// tilt needed at large angles, and at the 35 degree limit the error is about 15%.
        ///
        /// The tilt is then rotated from world axes into body axes, because the aircraft's roll and
        /// pitch are relative to its own heading. Skipping that rotation is why naive implementations
        /// fly sideways after yawing.
        /// </summary>
        private void ComputeVelocityToTilt(float dt, Vector3 desiredVelocity,
                                           out float targetRollRad, out float targetPitchRad)
        {
            Vector3 velocity = physics.Velocity;

            float accelEast = _velocityEast.Update(desiredVelocity.x, velocity.x, dt);
            float accelNorth = _velocityNorth.Update(desiredVelocity.z, velocity.z, dt);

            // World-frame tilt demands, in radians.
            float tiltEast = Mathf.Atan2(accelEast, 9.80665f);
            float tiltNorth = Mathf.Atan2(accelNorth, 9.80665f);

            // Rotate into the body frame using the current heading.
            float headingRad = physics.HeadingDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(headingRad);
            float sin = Mathf.Sin(headingRad);

            // Body forward tilt: the component of the world tilt along the nose. Negative pitch is
            // nose down, and accelerating forward requires nose down, hence the sign.
            float bodyForward = tiltNorth * cos + tiltEast * sin;
            float bodyRight = tiltEast * cos - tiltNorth * sin;

            targetPitchRad = -bodyForward;
            targetRollRad = bodyRight;

            // Clamp the tilt magnitude while preserving direction, for the same reason the mixer
            // scales roll and pitch together: clamping the axes independently rotates the commanded
            // tilt direction and sends the aircraft somewhere other than where it was told.
            float maxTilt = config.MaxTiltAngleDeg * Mathf.Deg2Rad;
            float magnitude = Mathf.Sqrt(targetRollRad * targetRollRad +
                                         targetPitchRad * targetPitchRad);
            if (magnitude > maxTilt && magnitude > 0.0001f)
            {
                float scale = maxTilt / magnitude;
                targetRollRad *= scale;
                targetPitchRad *= scale;
            }
        }

        /// <summary>
        /// Altitude loop: altitude error -> target climb rate, then the climb rate loop.
        /// </summary>
        private float ComputeAltitudeHoldThrottle(float dt, float targetWorldY,
                                                  float rollRad, float pitchRad)
        {
            float error = targetWorldY - physics.transform.position.y;
            float desiredClimbRate = Mathf.Clamp(error * config.AltitudeKp,
                                                 -config.MaxDescentRateMps,
                                                 config.MaxClimbRateMps);
            _targetClimbRateMps = desiredClimbRate;
            return ComputeClimbRateThrottle(dt, desiredClimbRate, rollRad, pitchRad);
        }

        /// <summary>
        /// Climb rate loop: climb rate error -> throttle offset from the hover point.
        ///
        /// Two pieces of feedforward make this behave properly, and both matter:
        ///
        /// 1. HOVER THROTTLE BASELINE. The loop is an offset from the throttle known to hover, not an
        ///    absolute command. That leaves the integrator with only the modelling error to correct
        ///    rather than the whole hover thrust, so it converges quickly and stays small.
        ///
        /// 2. TILT COMPENSATION. When the aircraft is banked, only the cosine of the tilt angle of
        ///    its thrust acts vertically. Without compensation the aircraft sinks every time it
        ///    banks, and the operator sees altitude sag in every turn - a very recognisable symptom
        ///    of a controller that skipped this term. Dividing by cos(tilt) restores it. At the 35
        ///    degree limit that is a 22% thrust increase, which is not a small effect.
        /// </summary>
        private float ComputeClimbRateThrottle(float dt, float desiredClimbRate,
                                               float rollRad, float pitchRad)
        {
            float hoverThrottle = config.HoverThrottleFraction;

            float tiltCos = Mathf.Cos(rollRad) * Mathf.Cos(pitchRad);
            tiltCos = Mathf.Max(0.4f, tiltCos);   // guard against division by ~zero at extreme tilt
            float tiltCompensated = hoverThrottle / tiltCos;

            float offset = _climbRate.Update(desiredClimbRate, physics.VerticalSpeedMps, dt);

            return Mathf.Clamp01(tiltCompensated + offset);
        }

        /// <summary>
        /// Heading hold: heading error -> target yaw rate, via the yaw attitude gain.
        /// </summary>
        private float ComputeHeadingHoldYawRate()
        {
            float errorDeg = WrapAngle180(_targetHeadingDeg - physics.HeadingDeg);
            float rate = config.AttitudeKp.z * errorDeg * Mathf.Deg2Rad;
            float limit = config.MaxYawRateDegPerSec * Mathf.Deg2Rad;
            return Mathf.Clamp(rate, -limit, limit);
        }

        // ====================================================================================
        // HELPERS
        // ====================================================================================

        private void ResetControlLoops()
        {
            _rateRoll.Reset();
            _ratePitch.Reset();
            _rateYaw.Reset();
            _velocityEast.Reset();
            _velocityNorth.Reset();
            _climbRate.Reset();
        }

        /// <summary>
        /// Clears integrators but keeps the measurement history, so re-engaging a loop on a moving
        /// aircraft does not produce a spurious derivative spike.
        /// </summary>
        private void ResetControlLoopsPreservingMeasurement()
        {
            if (physics == null)
            {
                ResetControlLoops();
                return;
            }

            Vector3 bodyRates = physics.BodyAngularVelocityDeg();
            _rateRoll.ResetPreservingMeasurement(bodyRates.z * Mathf.Deg2Rad);
            _ratePitch.ResetPreservingMeasurement(bodyRates.x * Mathf.Deg2Rad);
            _rateYaw.ResetPreservingMeasurement(bodyRates.y * Mathf.Deg2Rad);

            Vector3 velocity = physics.Velocity;
            _velocityEast.ResetPreservingMeasurement(velocity.x);
            _velocityNorth.ResetPreservingMeasurement(velocity.z);
            _climbRate.ResetPreservingMeasurement(velocity.y);

            // Seed the climb-rate integrator to produce zero offset from the hover throttle, so the
            // aircraft holds altitude through the handover instead of sagging while the integrator
            // rediscovers what it already knew.
            _climbRate.PresetIntegralForOutput(0f);
        }

        /// <summary>
        /// Wraps an angle difference into (-180, 180].
        ///
        /// Not cosmetic. A yaw controller given an unwrapped error of 350 degrees will turn 350
        /// degrees the long way round instead of 10 degrees the short way. Every heading computation
        /// in the project passes through a wrap for this reason.
        /// </summary>
        private static float WrapAngle180(float degrees)
        {
            degrees = degrees % 360f;
            if (degrees > 180f)
            {
                degrees -= 360f;
            }
            else if (degrees <= -180f)
            {
                degrees += 360f;
            }
            return degrees;
        }

        private static float NormaliseHeading(float degrees)
        {
            degrees = degrees % 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }
    }
}
