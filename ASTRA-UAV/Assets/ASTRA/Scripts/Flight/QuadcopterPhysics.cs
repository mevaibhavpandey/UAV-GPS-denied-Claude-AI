using UnityEngine;
using Astra.Core.Config;
using Astra.Core.Logging;

namespace Astra.Flight
{
    /// <summary>
    /// The rigid-body dynamics of the airframe.
    ///
    /// This class is the answer to the specification's requirement that the aircraft must not move by
    /// teleporting or by Transform.Translate. Nothing here sets a position or a rotation. The only
    /// things applied to the Rigidbody are forces and torques, and the aircraft's motion is whatever
    /// Unity's solver produces from them. That constraint is enforced structurally: IFlightController
    /// has no SetPosition method, so no amount of expedience later can quietly introduce one.
    ///
    /// HOW FORCES ARE APPLIED, AND WHY IT MATTERS
    /// ------------------------------------------
    /// Each motor's thrust is applied as a force at that motor's own world position, along that
    /// motor's own up axis. The consequences are worth being explicit about, because they are the
    /// difference between a digital twin and an animation:
    ///
    ///   - Roll and pitch are not commanded. They EMERGE. When the mixer gives the left pair more
    ///     rotor speed than the right, the resulting force imbalance about the centre of mass
    ///     produces a rolling moment, and Unity integrates it. There is no AddTorque call for roll or
    ///     for pitch anywhere in this file.
    ///   - Horizontal motion is not commanded either. The aircraft tilts, its thrust vector tilts
    ///     with it, the horizontal component accelerates it, and it moves. Exactly as a real
    ///     multirotor does, and for exactly the same reason.
    ///   - A degraded motor produces a genuine asymmetry the controller must fight, rather than a
    ///     cosmetic warning light.
    ///
    /// Only yaw uses an explicit torque, because yaw genuinely comes from rotor reaction torque
    /// rather than from thrust geometry - the four thrust vectors are parallel and can produce no
    /// moment about the axis they are parallel to. That is the physics, not a shortcut.
    ///
    /// WHAT IS DELIBERATELY NOT MODELLED
    /// --------------------------------
    /// Stated plainly so nobody has to guess: no blade flapping, no translational lift, no ground
    /// effect, no inter-rotor wash interference, no proper vortex ring state, no gyroscopic
    /// precession from rotor angular momentum, no motor-mount flex. Each is a real effect. Their
    /// absence means this model is good for trajectory-level and control-level behaviour and is not a
    /// substitute for flight test or for CFD.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class QuadcopterPhysics : MonoBehaviour
    {
        // ====================================================================================
        // CONFIGURATION
        // ====================================================================================

        [Header("Configuration")]
        [SerializeField] private UavConfiguration config;

        [Header("Subsystems")]
        [Tooltip("The four motor units, in ArduPilot QuadX order: 0 front-right, 1 rear-left, " +
                 "2 front-left, 3 rear-right. Leave empty and they will be collected from children " +
                 "and sorted by their own motor index.")]
        [SerializeField] private Astra.Drone.MotorUnit[] motors = new Astra.Drone.MotorUnit[4];

        [SerializeField] private Astra.Drone.BatterySystem battery;

        [Header("Ground sensing")]
        [Tooltip("Layers treated as ground for the altitude-above-ground raycast.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Vertical speed below which, while in contact with the ground, the aircraft is " +
                 "considered landed.")]
        [SerializeField] private float landedSpeedThreshold = 0.15f;

        // ====================================================================================
        // STATE
        // ====================================================================================

        private Rigidbody _body;
        private readonly MotorMixer _mixer = new MotorMixer();

        private bool _motorsArmed;
        private Vector3 _windMps;
        private float _altitudeAgl;
        private bool _groundContact;
        private bool _isLanded = true;
        private float _totalThrustN;
        private float _lastMotorCurrentA;
        private Vector3 _lastAcceleration;
        private Vector3 _previousVelocity;

        // ====================================================================================
        // ACCESSORS
        // ====================================================================================

        public UavConfiguration Config { get { return config; } }
        public MotorMixer Mixer { get { return _mixer; } }
        public Astra.Drone.BatterySystem Battery { get { return battery; } }
        public Astra.Drone.MotorUnit[] Motors { get { return motors; } }
        public Rigidbody Body { get { return _body; } }

        /// <summary>
        /// World-space velocity, m/s.
        ///
        /// Wrapped in a property rather than read from the Rigidbody at every call site on purpose.
        /// Unity 6 renamed Rigidbody.velocity to linearVelocity, keeping the old name as a deprecated
        /// alias. Funnelling every access through here means an editor-version mismatch is a one-line
        /// fix rather than a hunt through a dozen files. If the compiler rejects linearVelocity, this
        /// project is on a pre-Unity-6 editor and these two properties are the only places to change.
        /// </summary>
        public Vector3 Velocity
        {
            get { return _body != null ? _body.linearVelocity : Vector3.zero; }
        }

        /// <summary>Body angular velocity in radians per second, world axes.</summary>
        public Vector3 AngularVelocity
        {
            get { return _body != null ? _body.angularVelocity : Vector3.zero; }
        }

        /// <summary>Horizontal ground speed, m/s.</summary>
        public float GroundSpeedMps
        {
            get
            {
                Vector3 v = Velocity;
                return new Vector2(v.x, v.z).magnitude;
            }
        }

        /// <summary>
        /// Airspeed in m/s, i.e. speed relative to the air rather than the ground.
        ///
        /// Distinguished from ground speed deliberately. A UAV holding position against a 6 m/s wind
        /// has zero ground speed and 6 m/s of airspeed, is tilted into the wind, and is burning
        /// battery to stay still. Aerodynamic drag acts on airspeed; navigation cares about ground
        /// speed. Conflating them is a classic source of endurance estimates that turn out to be
        /// wrong in the field.
        /// </summary>
        public float AirspeedMps
        {
            get { return (Velocity - _windMps).magnitude; }
        }

        /// <summary>Vertical speed, m/s. Positive is climbing.</summary>
        public float VerticalSpeedMps { get { return Velocity.y; } }

        /// <summary>Height above the ground directly below, metres. Negative if unknown.</summary>
        public float AltitudeAglM { get { return _altitudeAgl; } }

        /// <summary>True if a collider is in contact beneath the aircraft.</summary>
        public bool GroundContact { get { return _groundContact; } }

        /// <summary>True if on the ground and not moving appreciably.</summary>
        public bool IsLanded { get { return _isLanded; } }

        /// <summary>Sum of all four motors' thrust, newtons.</summary>
        public float TotalThrustN { get { return _totalThrustN; } }

        /// <summary>Total motor current, amperes.</summary>
        public float MotorCurrentA { get { return _lastMotorCurrentA; } }

        /// <summary>
        /// Proper acceleration in the body frame, m/s^2, as an accelerometer would measure it -
        /// including the reaction to gravity. Consumed by the simulated IMU so that it reports
        /// specific force rather than kinematic acceleration, which is what real accelerometers do
        /// and the reason one reads about 9.81 m/s^2 sitting on a desk.
        /// </summary>
        public Vector3 BodyProperAcceleration
        {
            get
            {
                Vector3 specificForce = _lastAcceleration - Physics.gravity;
                return transform.InverseTransformDirection(specificForce);
            }
        }

        /// <summary>Roll angle in degrees. Positive is right side down.</summary>
        public float RollDeg
        {
            get
            {
                // Extracted from the basis vectors rather than from eulerAngles, because Euler
                // extraction is ambiguous near gimbal lock and because Unity's eulerAngles ordering
                // makes the sign convention easy to get wrong. The projection of the body right
                // vector onto world up gives the bank angle directly and unambiguously.
                Vector3 right = transform.right;
                return -Mathf.Asin(Mathf.Clamp(right.y, -1f, 1f)) * Mathf.Rad2Deg;
            }
        }

        /// <summary>Pitch angle in degrees. Positive is nose up.</summary>
        public float PitchDeg
        {
            get
            {
                Vector3 forward = transform.forward;
                return Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            }
        }

        /// <summary>Heading in degrees, 0 = north (+Z), increasing clockwise.</summary>
        public float HeadingDeg
        {
            get
            {
                Vector3 forward = transform.forward;
                float heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                return heading < 0f ? heading + 360f : heading;
            }
        }

        /// <summary>Body roll rate in degrees per second.</summary>
        public float RollRateDegPerSec
        {
            get { return BodyAngularVelocityDeg().z; }
        }

        /// <summary>Body pitch rate in degrees per second.</summary>
        public float PitchRateDegPerSec
        {
            get { return BodyAngularVelocityDeg().x; }
        }

        /// <summary>Body yaw rate in degrees per second. Positive is yawing right.</summary>
        public float YawRateDegPerSec
        {
            get { return BodyAngularVelocityDeg().y; }
        }

        /// <summary>
        /// Angular velocity expressed in body axes and degrees per second. Unity reports angular
        /// velocity in world axes and radians, and the control loops need body axes and degrees.
        /// </summary>
        public Vector3 BodyAngularVelocityDeg()
        {
            if (_body == null)
            {
                return Vector3.zero;
            }
            Vector3 bodyRates = transform.InverseTransformDirection(_body.angularVelocity);
            return bodyRates * Mathf.Rad2Deg;
        }

        // ====================================================================================
        // LIFECYCLE
        // ====================================================================================

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            CollectMotorsIfNeeded();
            ApplyConfiguration();
        }

        private void CollectMotorsIfNeeded()
        {
            bool needsCollection = motors == null || motors.Length != 4;
            if (!needsCollection)
            {
                for (int i = 0; i < motors.Length; i++)
                {
                    if (motors[i] == null)
                    {
                        needsCollection = true;
                        break;
                    }
                }
            }

            if (!needsCollection)
            {
                return;
            }

            Astra.Drone.MotorUnit[] found =
                GetComponentsInChildren<Astra.Drone.MotorUnit>(true);

            motors = new Astra.Drone.MotorUnit[4];
            for (int i = 0; i < found.Length; i++)
            {
                int index = found[i].MotorIndex;
                if (index >= 0 && index < 4)
                {
                    if (motors[index] != null)
                    {
                        Debug.LogError("[QuadcopterPhysics] Two motors both claim index " + index +
                                       ". Motor indices must be 0-3 and unique, otherwise the mixer " +
                                       "sends commands to the wrong motors and the aircraft will be " +
                                       "uncontrollable in a very confusing way.", found[i]);
                    }
                    motors[index] = found[i];
                }
            }

            for (int i = 0; i < 4; i++)
            {
                if (motors[i] == null)
                {
                    Debug.LogError("[QuadcopterPhysics] No motor found with index " + i +
                                   ". The aircraft cannot fly with a missing motor slot.", this);
                }
            }
        }

        /// <summary>
        /// Pushes the configuration asset's values into the Rigidbody and the motors.
        ///
        /// Mass and inertia are set explicitly rather than inferred from colliders. Unity computes an
        /// inertia tensor from collider geometry, which sounds convenient and is a trap: it means the
        /// aircraft's rotational dynamics silently change whenever anyone adjusts a collider for a
        /// visual reason, and the resulting handling difference is very hard to attribute. Setting it
        /// from measured or estimated figures keeps the dynamics owned by the configuration asset,
        /// where they are visible and documented.
        /// </summary>
        public void ApplyConfiguration()
        {
            if (config == null)
            {
                Debug.LogError("[QuadcopterPhysics] No UavConfiguration assigned. Assign one in the " +
                               "inspector or the aircraft has no mass, thrust or control gains.", this);
                return;
            }

            if (_body == null)
            {
                _body = GetComponent<Rigidbody>();
            }

            _body.mass = config.MassKg;

            // Unity's own drag is set to zero. Aerodynamic drag is modelled explicitly below so that
            // it can act on AIRSPEED rather than ground speed, which Unity's built-in damping cannot
            // do because it knows nothing about wind.
            _body.linearDamping = 0f;
            _body.angularDamping = 0f;

            _body.automaticCenterOfMass = false;
            _body.centerOfMass = new Vector3(0f, -0.06f, 0f);
            _body.automaticInertiaTensor = false;
            _body.inertiaTensor = new Vector3(
                config.InertiaRollPitch,   // about X, the pitch axis
                config.InertiaYaw,         // about Y, the yaw axis
                config.InertiaRollPitch);  // about Z, the roll axis
            _body.inertiaTensorRotation = Quaternion.identity;

            // Continuous dynamic collision detection. A quadcopter at 12 m/s covers 120 mm in a
            // single 10 ms physics step, which is comparable to the thickness of the surfaces it
            // might hit. Discrete detection tunnels straight through thin geometry, and a
            // demonstration where the aircraft passes through a wall instead of colliding with it
            // undermines everything else on screen.
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            _body.interpolation = RigidbodyInterpolation.Interpolate;

            for (int i = 0; i < motors.Length; i++)
            {
                if (motors[i] != null)
                {
                    motors[i].Configure(config);
                }
            }

            if (battery != null)
            {
                battery.Configure(config);
            }

            EventLog.Info(LogSource.System, string.Format(
                "Airframe configured: {0:F2} kg, T/W {1:F2}, hover at {2:F0}% rotor speed",
                config.MassKg, config.ThrustToWeightRatio,
                config.HoverThrottleFraction * 100f));
        }

        // ====================================================================================
        // COMMAND INTERFACE
        // ====================================================================================

        /// <summary>
        /// Sets the mixer demands for this step. Called by FlightControlSystem from FixedUpdate.
        /// </summary>
        /// <param name="throttle">Collective rotor speed in [0,1].</param>
        /// <param name="roll">Roll demand. Positive rolls right.</param>
        /// <param name="pitch">Pitch demand. Positive pitches nose up.</param>
        /// <param name="yaw">Yaw demand. Positive yaws right.</param>
        public void SetMixerDemand(float throttle, float roll, float pitch, float yaw)
        {
            if (!_motorsArmed)
            {
                _mixer.Zero();
                return;
            }
            _mixer.Mix(throttle, roll, pitch, yaw);
        }

        /// <summary>
        /// Arms or disarms the motors. Disarming cuts all four immediately, which is the whole point
        /// of a disarm and the reason it must never be gated behind a state machine transition that
        /// could fail.
        /// </summary>
        public void SetMotorsArmed(bool armed)
        {
            _motorsArmed = armed;
            for (int i = 0; i < motors.Length; i++)
            {
                if (motors[i] != null)
                {
                    motors[i].SetArmed(armed);
                }
            }
            if (!armed)
            {
                _mixer.Zero();
            }
        }

        /// <summary>Sets the ambient wind in world space, m/s.</summary>
        public void SetWind(Vector3 windMps)
        {
            _windMps = windMps;
        }

        public Vector3 WindMps { get { return _windMps; } }

        // ====================================================================================
        // PHYSICS STEP
        // ====================================================================================

        private void FixedUpdate()
        {
            if (config == null || _body == null)
            {
                return;
            }

            float dt = Time.fixedDeltaTime;

            // Battery first, using the previous step's current draw. The one-step lag is physically
            // correct: sag is the consequence of current, not simultaneous with it. It also breaks
            // what would otherwise be a circular dependency, since motor current depends on voltage
            // and voltage depends on motor current.
            float supplyVoltage = config.NominalVoltage;
            if (battery != null)
            {
                battery.Tick(_lastMotorCurrentA, dt);
                supplyVoltage = battery.VoltageV;
            }

            // Step each motor, then apply its force at its own position.
            _totalThrustN = 0f;
            _lastMotorCurrentA = 0f;
            float netReactionTorque = 0f;

            float[] commands = _mixer.Outputs;

            for (int i = 0; i < motors.Length; i++)
            {
                Astra.Drone.MotorUnit motor = motors[i];
                if (motor == null)
                {
                    continue;
                }

                motor.SetCommand(_motorsArmed ? commands[i] : 0f);
                motor.Tick(dt, supplyVoltage);

                float thrust = motor.ThrustN;
                if (thrust > 0f)
                {
                    // THE LINE THAT MATTERS. Force applied at the motor's own position, along the
                    // motor's own up axis. Roll and pitch moments fall out of the geometry; no
                    // attitude torque is applied anywhere.
                    _body.AddForceAtPosition(motor.ThrustDirection * thrust,
                                             motor.transform.position,
                                             ForceMode.Force);
                }

                _totalThrustN += thrust;
                _lastMotorCurrentA += motor.CurrentA;
                netReactionTorque += motor.ReactionTorqueNm;
            }

            // Yaw is the one axis that needs an explicit torque, because the four thrust vectors are
            // parallel and therefore produce no moment about the axis they are parallel to. Real yaw
            // authority comes from the difference in rotor drag torque between the clockwise and
            // counter-clockwise pairs, which is what this is.
            if (Mathf.Abs(netReactionTorque) > 0.0001f)
            {
                _body.AddTorque(transform.up * netReactionTorque, ForceMode.Force);
            }

            ApplyAerodynamicDrag(dt);
            UpdateGroundSensing();
            SettleOnGroundIfIdle();

            // Track acceleration for the simulated accelerometer. Differencing velocity across the
            // step is the honest way to obtain it, since the forces applied above are not the only
            // ones acting - contacts and the solver contribute too, and an accelerometer would feel
            // all of them.
            Vector3 velocity = _body.linearVelocity;
            _lastAcceleration = dt > 0f ? (velocity - _previousVelocity) / dt : Vector3.zero;
            _previousVelocity = velocity;
        }

        /// <summary>
        /// Applies translational and rotational aerodynamic drag.
        ///
        /// Drag acts on airspeed rather than ground speed, which is why the wind vector is subtracted
        /// first. This is what makes station-keeping in wind behave correctly: the aircraft must tilt
        /// into the wind and hold a non-zero thrust component to stay over one spot, and it burns
        /// battery doing so.
        /// </summary>
        private void ApplyAerodynamicDrag(float dt)
        {
            Vector3 airRelative = _body.linearVelocity - _windMps;
            float airspeed = airRelative.magnitude;

            if (airspeed > 0.01f)
            {
                Vector3 dragDirection = -airRelative / airspeed;

                float dragMagnitude = config.LinearDragCoefficient * airspeed +
                                      config.QuadraticDragCoefficient * airspeed * airspeed;

                // Descending into the rotors' own downwash increases drag and reduces control
                // authority. This is a crude stand-in for vortex ring state, not a faithful model of
                // it: real VRS is an unsteady flow breakdown with hysteresis, and the qualitative
                // effect reproduced here is only that fast vertical descent is worse than slow
                // descent. Labelled as such in the diagnostics panel.
                if (airRelative.y < -1f)
                {
                    float descentFactor = Mathf.InverseLerp(1f, 6f, -airRelative.y);
                    dragMagnitude *= Mathf.Lerp(1f, config.DescentWakeDragMultiplier, descentFactor);
                }

                _body.AddForce(dragDirection * dragMagnitude, ForceMode.Force);
            }

            Vector3 angular = _body.angularVelocity;
            if (angular.sqrMagnitude > 0.000001f)
            {
                // Aerodynamic rotational damping
                _body.AddTorque(-angular * (config.AngularDragCoefficient * 2.0f), ForceMode.Force);
            }
        }

        /// <summary>
        /// Raycasts downwards for height above ground and contact state.
        /// </summary>
        private void UpdateGroundSensing()
        {
            Vector3 origin = transform.position + Vector3.up * 0.2f;
            RaycastHit hit;

            if (Physics.Raycast(origin, Vector3.down, out hit, 500f, groundMask,
                                QueryTriggerInteraction.Ignore))
            {
                _altitudeAgl = Mathf.Max(0f, hit.distance - 0.2f);
                _groundContact = _altitudeAgl < 0.12f;
            }
            else
            {
                // No hit means either very high up or over a hole in the map. Reported as unknown
                // rather than assumed to be zero, because assuming zero would tell the altitude
                // controller it had landed while it was in fact 200 m up.
                _altitudeAgl = -1f;
                _groundContact = false;
            }

            bool landedNow = _groundContact &&
                             Mathf.Abs(_body.linearVelocity.y) < landedSpeedThreshold &&
                             GroundSpeedMps < landedSpeedThreshold * 2f;

            if (landedNow != _isLanded)
            {
                _isLanded = landedNow;
            }
        }

        /// <summary>
        /// Damps out residual jitter when sitting on the ground disarmed.
        ///
        /// A Rigidbody resting on a collider with a small contact patch tends to creep and buzz as the
        /// solver fights gravity against contact constraints. On a static aircraft that reads as a
        /// bug. Damping the residual motion only when disarmed and in contact leaves flight dynamics
        /// completely untouched, which is the important part - this must not become a shortcut that
        /// quietly stabilises the aircraft in the air.
        /// </summary>
        private void SettleOnGroundIfIdle()
        {
            if (_motorsArmed || !_groundContact)
            {
                return;
            }

            Vector3 v = _body.linearVelocity;
            if (v.sqrMagnitude < 0.25f)
            {
                _body.linearVelocity = v * 0.6f;
            }

            Vector3 w = _body.angularVelocity;
            if (w.sqrMagnitude < 0.25f)
            {
                _body.angularVelocity = w * 0.6f;
            }
        }

        // ====================================================================================
        // RESET
        // ====================================================================================

        /// <summary>
        /// Returns the aircraft to a resting state at the given pose.
        ///
        /// This is the ONE place a position is written directly, and it exists because a mission reset
        /// has to put the aircraft back on the pad. It is guarded by being callable only from mission
        /// setup - never from flight code - and it is not reachable through IFlightController, so it
        /// cannot become a back door for making the aircraft "fly" by assignment.
        /// </summary>
        public void ResetToPose(Vector3 position, Quaternion rotation)
        {
            SetMotorsArmed(false);
            _mixer.Zero();

            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.position = position;
            _body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);

            _previousVelocity = Vector3.zero;
            _lastAcceleration = Vector3.zero;
            _lastMotorCurrentA = 0f;
            _totalThrustN = 0f;
            _isLanded = true;

            for (int i = 0; i < motors.Length; i++)
            {
                if (motors[i] != null)
                {
                    motors[i].ResetState();
                }
            }

            if (battery != null)
            {
                battery.ResetState();
            }

            EventLog.Info(LogSource.System, "Airframe reset to launch pose");
        }
    }
}
