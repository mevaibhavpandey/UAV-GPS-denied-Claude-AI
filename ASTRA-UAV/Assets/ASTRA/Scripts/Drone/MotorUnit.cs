using UnityEngine;
using Astra.Core.Config;

namespace Astra.Drone
{
    /// <summary>
    /// Which way a propeller turns, viewed from above the aircraft.
    ///
    /// On a quadcopter in X configuration the two diagonal pairs must turn opposite ways, otherwise
    /// their reaction torques sum instead of cancelling and the airframe spins continuously. This is
    /// not a detail - getting it wrong is one of the classic first-flight failures, and it is worth
    /// the demonstration showing it correctly.
    /// </summary>
    public enum PropellerRotation
    {
        /// <summary>Clockwise viewed from above.</summary>
        Clockwise = 0,

        /// <summary>Counter-clockwise viewed from above.</summary>
        CounterClockwise = 1
    }

    /// <summary>
    /// One motor, ESC and propeller assembly, simulated as an independent unit.
    ///
    /// WHY EACH MOTOR IS A SEPARATE OBJECT RATHER THAN FOUR NUMBERS IN AN ARRAY
    /// ------------------------------------------------------------------------
    /// Because the specification asks for a digital twin, and because it makes the demonstration
    /// answer questions rather than assert conclusions. Each unit here carries its own rotor speed,
    /// thrust, reaction torque, current, temperature and health, and applies its thrust at its own
    /// physical position on the airframe. That means:
    ///
    ///   - The aircraft rolls because the motors on one side genuinely produce more thrust than the
    ///     other side, not because a torque was applied to the body to make it look like it rolled.
    ///   - Yaw comes from the reaction torque difference between the clockwise and counter-clockwise
    ///     pairs, which is how a real quadcopter yaws.
    ///   - A single degraded motor produces an asymmetry the controller has to fight, which is
    ///     visible in the per-motor panel and is a genuinely interesting thing to demonstrate.
    ///
    /// The alternative - computing a net force and torque and applying them at the centre of mass -
    /// would look identical in the common case and would be wrong in every interesting one.
    ///
    /// THRUST MODEL
    /// ------------
    /// Rotor thrust scales with the square of rotor speed, which is standard blade-element/momentum
    /// theory for a fixed-pitch propeller in hover. Rotor speed follows the commanded value through a
    /// first-order lag, because a 15-inch propeller has real rotational inertia and cannot change
    /// speed instantly.
    ///
    /// WHAT THIS MODEL DOES NOT INCLUDE, stated plainly:
    ///   - Blade flapping and the resulting off-axis moments
    ///   - Translational lift (thrust rising with forward airspeed at constant rotor speed)
    ///   - Ground effect (extra lift within roughly one rotor diameter of the ground)
    ///   - Propeller-wash interaction between adjacent rotors
    ///   - Vortex ring state proper; only a crude drag increase during descent stands in for it
    ///
    /// Each of these is a real aerodynamic effect and each is absent. The model is a reasonable
    /// engineering approximation for trajectory-level behaviour, and it is not a substitute for CFD
    /// or for flight test.
    /// </summary>
    [DisallowMultipleComponent]
    public class MotorUnit : MonoBehaviour
    {
        // ====================================================================================
        // CONFIGURATION
        // ====================================================================================

        [Header("Identity")]
        [Tooltip("Motor index 0-3. ASTRA follows ArduPilot's QuadX ordering: 0 = front-right, " +
                 "1 = rear-left, 2 = front-left, 3 = rear-right. Using the same ordering as the " +
                 "autopilot the team intends to fly means the per-motor panel here can be compared " +
                 "directly against Mission Planner's motor test output.")]
        [SerializeField] private int motorIndex;

        [Tooltip("Rotation direction viewed from above. Diagonal pairs must match: motors 0 and 1 " +
                 "counter-clockwise, motors 2 and 3 clockwise.")]
        [SerializeField] private PropellerRotation rotation = PropellerRotation.CounterClockwise;

        [Header("Scene references")]
        [Tooltip("The propeller transform, spun about its local Y axis. Leave empty and the unit " +
                 "will look for a child named 'Propeller'.")]
        [SerializeField] private Transform propeller;

        [Tooltip("Optional translucent disc shown at high rotor speed in place of the blades, " +
                 "standing in for motion blur. See the note on visual aliasing below.")]
        [SerializeField] private GameObject blurDisc;

        [Header("Visual")]
        [Tooltip("Maximum apparent rotation rate in degrees per second.\n\n" +
                 "IMPORTANT: this caps the VISUAL rate only. The simulated RPM, and the RPM shown " +
                 "on the diagnostics panel, are the true modelled values.\n\n" +
                 "The cap exists because a real propeller at 6000 RPM turns 100 times per second, " +
                 "which at 60 frames per second is 600 degrees of rotation per frame. Rendering that " +
                 "literally produces the wagon-wheel effect: the propeller appears to crawl, stop, " +
                 "or turn backwards. It looks broken, and on a projector at a lower effective frame " +
                 "rate it looks worse. Capping the apparent rate and fading in a blur disc is what " +
                 "every flight simulator does, for the same reason.")]
        [SerializeField] private float maxVisualDegPerSec = 1440f;

        [Tooltip("Rotor speed fraction above which the blur disc fades in.")]
        [Range(0f, 1f)]
        [SerializeField] private float blurThreshold = 0.25f;

        [Header("Health")]
        [Tooltip("Thrust efficiency in [0,1]. 1 is a healthy motor. Lower values simulate a damaged " +
                 "propeller, a failing bearing or a partially desynchronised ESC. Exposed so a " +
                 "single-motor degradation can be demonstrated and the controller's response to it " +
                 "observed.")]
        [Range(0f, 1f)]
        [SerializeField] private float efficiency = 1f;

        // ====================================================================================
        // STATE
        // ====================================================================================

        private UavConfiguration _config;

        /// <summary>Commanded rotor speed fraction in [0,1], from the mixer.</summary>
        private float _command;

        /// <summary>Actual rotor speed fraction in [0,1], lagging the command.</summary>
        private float _speedFraction;

        private float _thrustN;
        private float _reactionTorqueNm;
        private float _powerW;
        private float _currentA;
        private float _temperatureC;
        private float _visualAngle;
        private bool _isArmed;
        private bool _wasHealthy = true;

        // ====================================================================================
        // PUBLIC READOUTS
        // ====================================================================================

        public int MotorIndex { get { return motorIndex; } }
        public PropellerRotation Rotation { get { return rotation; } }

        /// <summary>
        /// Sign of the reaction torque this motor applies to the airframe about the body up axis.
        ///
        /// A propeller turning counter-clockwise from above drags the airframe clockwise, and in
        /// Unity's left-handed frame with Y up a positive rotation about Y is clockwise from above.
        /// So a counter-clockwise propeller yields a POSITIVE reaction torque about +Y, i.e. it yaws
        /// the aircraft to the right.
        /// </summary>
        public float ReactionTorqueSign
        {
            get { return rotation == PropellerRotation.CounterClockwise ? 1f : -1f; }
        }

        /// <summary>Commanded rotor speed fraction in [0,1].</summary>
        public float Command { get { return _command; } }

        /// <summary>Actual rotor speed fraction in [0,1], after the spin-up lag.</summary>
        public float SpeedFraction { get { return _speedFraction; } }

        /// <summary>Throttle as a percentage, for display.</summary>
        public float ThrottlePercent { get { return _speedFraction * 100f; } }

        /// <summary>
        /// Rotor speed in revolutions per minute.
        ///
        /// [MODEL] Maximum rotor speed is taken as KV x pack voltage x a loading factor of 0.85,
        /// because a loaded propeller never reaches the motor's no-load speed. At 360 KV on a
        /// nominal 22.2 V pack that is 360 x 22.2 x 0.85 = 6793 RPM at full throttle.
        /// </summary>
        public float Rpm
        {
            get
            {
                if (_config == null)
                {
                    return 0f;
                }
                float maxRpm = _config.MotorKv * _config.NominalVoltage * 0.85f;
                return _speedFraction * maxRpm;
            }
        }

        /// <summary>Thrust produced, newtons.</summary>
        public float ThrustN { get { return _thrustN; } }

        /// <summary>Reaction torque magnitude on the airframe, newton-metres. Signed.</summary>
        public float ReactionTorqueNm { get { return _reactionTorqueNm; } }

        /// <summary>Electrical power draw, watts.</summary>
        public float PowerW { get { return _powerW; } }

        /// <summary>Current draw, amperes.</summary>
        public float CurrentA { get { return _currentA; } }

        /// <summary>Estimated winding temperature, degrees C.</summary>
        public float TemperatureC { get { return _temperatureC; } }

        /// <summary>Thrust efficiency in [0,1]. 1 is healthy.</summary>
        public float Efficiency { get { return efficiency; } }

        /// <summary>
        /// True if the motor is operating normally: efficiency near nominal and temperature below
        /// the warning threshold.
        /// </summary>
        public bool IsHealthy
        {
            get
            {
                if (efficiency < 0.9f)
                {
                    return false;
                }
                if (_config != null && _temperatureC > _config.MotorWarningTemperatureC)
                {
                    return false;
                }
                return true;
            }
        }

        /// <summary>Short health description for the per-motor panel.</summary>
        public string HealthDescription
        {
            get
            {
                if (efficiency < 0.5f)
                {
                    return "FAULT";
                }
                if (efficiency < 0.9f)
                {
                    return "DEGRADED";
                }
                if (_config != null && _temperatureC > _config.MotorWarningTemperatureC)
                {
                    return "OVERTEMP";
                }
                return "OK";
            }
        }

        /// <summary>World-space thrust direction: the airframe's up axis.</summary>
        public Vector3 ThrustDirection
        {
            get { return transform.up; }
        }

        // ====================================================================================
        // LIFECYCLE
        // ====================================================================================

        private void Awake()
        {
            if (propeller == null)
            {
                Transform found = transform.Find("Propeller");
                if (found != null)
                {
                    propeller = found;
                }
            }

            if (blurDisc != null)
            {
                blurDisc.SetActive(false);
            }
        }

        /// <summary>
        /// Supplies the airframe configuration. Called by QuadcopterPhysics during setup rather than
        /// each motor finding it independently, so there is one owner of the configuration and no
        /// possibility of two motors reading different assets.
        /// </summary>
        public void Configure(UavConfiguration config, int index, PropellerRotation spin)
        {
            _config = config;
            motorIndex = index;
            rotation = spin;
            _temperatureC = config != null ? config.AmbientTemperatureC : 30f;
        }

        public void Configure(UavConfiguration config)
        {
            _config = config;
            if (config != null)
            {
                _temperatureC = config.AmbientTemperatureC;
            }
        }

        // ====================================================================================
        // CONTROL
        // ====================================================================================

        /// <summary>
        /// Sets the commanded rotor speed fraction, from the mixer. Clamped to [0,1].
        ///
        /// Note that this is a ROTOR SPEED command, not a thrust command. Thrust follows as the
        /// square. Keeping the command in rotor-speed terms mirrors what an ESC actually receives and
        /// keeps the nonlinearity in one place, where it can be reasoned about.
        /// </summary>
        public void SetCommand(float rotorSpeedFraction)
        {
            _command = Mathf.Clamp01(rotorSpeedFraction);
        }

        public void SetArmed(bool armed)
        {
            _isArmed = armed;
            if (!armed)
            {
                _command = 0f;
            }
        }

        /// <summary>
        /// Degrades this motor to the given thrust efficiency, for failure demonstrations.
        /// Efficiency 0 is a complete motor-out.
        /// </summary>
        public void SetEfficiency(float value)
        {
            efficiency = Mathf.Clamp01(value);
        }

        // ====================================================================================
        // SIMULATION STEP
        // ====================================================================================

        /// <summary>
        /// Advances the motor one physics step. Called from QuadcopterPhysics.FixedUpdate before the
        /// forces are applied, so thrust reflects this step's rotor speed.
        /// </summary>
        /// <param name="dt">Fixed timestep, seconds.</param>
        /// <param name="supplyVoltage">Pack voltage under load, volts.</param>
        public void Tick(float dt, float supplyVoltage)
        {
            if (_config == null || dt <= 0f)
            {
                return;
            }

            // ---- Rotor speed: first-order lag towards the command ----
            float target = _command;
            if (_isArmed && target < _config.ArmedIdleThrottle)
            {
                // A real ESC keeps the motors turning when armed, so the operator can see the
                // aircraft is live and so the ESC retains rotor position sync. Reproducing it is
                // both realistic and a genuine safety cue in the demonstration.
                target = _config.ArmedIdleThrottle;
            }
            if (!_isArmed)
            {
                target = 0f;
            }

            float tau = Mathf.Max(0.001f, _config.MotorTimeConstantS);
            float alpha = 1f - Mathf.Exp(-dt / tau);
            _speedFraction += (target - _speedFraction) * alpha;
            _speedFraction = Mathf.Clamp01(_speedFraction);

            // ---- Voltage sag reduces achievable rotor speed ----
            // A sagging pack cannot spin the motors as fast, which is why a marginal aircraft loses
            // authority exactly when it needs it most - during a hard climb on a tired battery.
            float voltageFactor = 1f;
            if (_config.NominalVoltage > 0.1f && supplyVoltage > 0.1f)
            {
                voltageFactor = Mathf.Clamp(supplyVoltage / _config.NominalVoltage, 0.6f, 1.15f);
            }
            float effectiveSpeed = _speedFraction * voltageFactor;

            // ---- Thrust: proportional to the square of rotor speed ----
            _thrustN = _config.MaxThrustPerMotorN * effectiveSpeed * effectiveSpeed * efficiency;

            // ---- Reaction torque: also proportional to the square of rotor speed ----
            _reactionTorqueNm = _config.MaxMotorTorqueNm * effectiveSpeed * effectiveSpeed *
                                efficiency * ReactionTorqueSign;

            // ---- Electrical power and current ----
            // Power is computed from the thrust the motor is ACTUALLY producing, so a degraded motor
            // correctly draws power without delivering the corresponding thrust - which is what
            // makes a failing motor show up as an efficiency loss rather than as free thrust loss.
            float aeroThrust = _config.MaxThrustPerMotorN * effectiveSpeed * effectiveSpeed;
            _powerW = _speedFraction > 0.001f ? _config.MotorPowerForThrust(aeroThrust) : 0f;
            _currentA = supplyVoltage > 0.1f ? _powerW / supplyVoltage : 0f;

            // ---- Thermal model ----
            // Copper loss I^2 R drives a steady-state rise of loss x thermal resistance above
            // ambient, approached with a time constant of tens of seconds. Modelling the lag rather
            // than the steady state is what makes the temperature readout meaningful: a brief
            // full-throttle burst barely moves it, a sustained climb does.
            float copperLossW = _currentA * _currentA * _config.MotorPhaseResistanceOhm;
            float steadyStateC = _config.AmbientTemperatureC +
                                 copperLossW * _config.MotorThermalResistanceCPerW;
            float thermalTau = Mathf.Max(1f, _config.MotorThermalTimeConstantS);
            float thermalAlpha = 1f - Mathf.Exp(-dt / thermalTau);
            _temperatureC += (steadyStateC - _temperatureC) * thermalAlpha;

            // ---- Health transition reporting ----
            bool healthy = IsHealthy;
            if (healthy != _wasHealthy)
            {
                _wasHealthy = healthy;
                Astra.Core.AstraEvents.RaiseMotorHealthChanged(motorIndex, healthy);
                if (!healthy)
                {
                    Astra.Core.Logging.EventLog.Warning(
                        Astra.Core.Logging.LogSource.Power,
                        string.Format("Motor {0} {1} - {2:F0} C, efficiency {3:P0}",
                            motorIndex + 1, HealthDescription, _temperatureC, efficiency));
                }
            }
        }

        /// <summary>
        /// Spins the propeller. Called from Update rather than FixedUpdate so the visual rate is tied
        /// to the frame rate and looks smooth, while the physics stays on the fixed step.
        /// </summary>
        private void Update()
        {
            if (propeller == null)
            {
                return;
            }

            float apparentRate = _speedFraction * maxVisualDegPerSec;
            float directionSign = rotation == PropellerRotation.Clockwise ? 1f : -1f;

            _visualAngle += apparentRate * directionSign * Time.deltaTime;
            if (_visualAngle > 360f || _visualAngle < -360f)
            {
                _visualAngle = _visualAngle % 360f;
            }

            propeller.localRotation = Quaternion.Euler(0f, _visualAngle, 0f);

            if (blurDisc != null)
            {
                bool shouldBlur = _speedFraction > blurThreshold;
                if (blurDisc.activeSelf != shouldBlur)
                {
                    blurDisc.SetActive(shouldBlur);
                }
            }
        }

        /// <summary>
        /// Returns the motor to a cold, stopped, healthy state. Called on mission reset.
        /// </summary>
        public void ResetState()
        {
            _command = 0f;
            _speedFraction = 0f;
            _thrustN = 0f;
            _reactionTorqueNm = 0f;
            _powerW = 0f;
            _currentA = 0f;
            _temperatureC = _config != null ? _config.AmbientTemperatureC : 30f;
            _isArmed = false;
            efficiency = 1f;
            _wasHealthy = true;
        }

        private void OnValidate()
        {
            motorIndex = Mathf.Clamp(motorIndex, 0, 3);
            maxVisualDegPerSec = Mathf.Max(0f, maxVisualDegPerSec);
        }

        private void OnDrawGizmosSelected()
        {
            // Thrust vector, so the arm layout and thrust directions can be checked visually in the
            // editor. Getting a motor's orientation wrong is easy and produces confusing flight
            // behaviour that is hard to diagnose from numbers alone.
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f);
            float length = Mathf.Max(0.2f, _thrustN * 0.05f);
            Gizmos.DrawLine(transform.position, transform.position + transform.up * length);
        }
    }
}
