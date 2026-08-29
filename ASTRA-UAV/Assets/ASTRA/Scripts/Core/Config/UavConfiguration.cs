using UnityEngine;

namespace Astra.Core.Config
{
    /// <summary>
    /// Physical and control parameters for the ASTRA airframe.
    ///
    /// EVIDENCE STATUS OF THE DEFAULT VALUES
    /// -------------------------------------
    /// Every default below is tagged in its tooltip with one of:
    ///   [DERIVED]     - arithmetic from a stated input; sound if the input is
    ///   [ESTIMATE]    - engineering estimate with reasoning; NOT measured
    ///   [UNVERIFIED]  - recalled figure with no authoritative source consulted
    ///
    /// None of them are [VERIFIED], because verification against manufacturer datasheets was not
    /// possible when this file was written. See Docs/10-UAV-Hardware-Layout.md for the full
    /// verification worklist and the critical concerns list.
    ///
    /// The single most important item to replace with real data is MaxThrustPerMotorN. It is an
    /// estimate for a motor that very likely has no published thrust curve, and the entire
    /// thrust-to-weight margin of this aircraft rests on it. Measure it on a load cell.
    ///
    /// PRACTICAL NOTE: this is a ScriptableObject so the airframe can be reconfigured without
    /// touching code, and so several configurations (as-designed, as-measured, best-case,
    /// worst-case) can sit side by side in the project and be swapped for comparison. That is
    /// useful when an evaluator asks "what if the motors underperform?" - you can show them.
    /// </summary>
    [CreateAssetMenu(fileName = "UavConfiguration",
                     menuName = "ASTRA/UAV Configuration",
                     order = 1)]
    public class UavConfiguration : ScriptableObject
    {
        // ====================================================================================
        // MASS AND GEOMETRY
        // ====================================================================================

        [Header("Mass and geometry")]

        [Tooltip("All-up weight in kilograms. [ESTIMATE] Component budget: frame ~500 g, " +
                 "4 motors ~740 g, 4 ESCs ~140 g, 4 props ~100 g, FC+GPS ~150 g, " +
                 "6S 6200 mAh pack ~900 g, Pi 5 + cooler + camera ~120 g, power wiring ~120 g, " +
                 "landing gear and fasteners ~150 g. Reweigh on a scale once parts arrive.")]
        [SerializeField] private float massKg = 2.92f;

        [Tooltip("Diagonal motor-to-motor distance in metres. [UNVERIFIED, high confidence] " +
                 "The '650' in 'Tarot 650 Sport' denotes the wheelbase.")]
        [SerializeField] private float wheelbaseM = 0.650f;

        [Tooltip("Propeller diameter in metres. [DERIVED] 15 inches = 0.381 m exactly.")]
        [SerializeField] private float propellerDiameterM = 0.381f;

        [Tooltip("Propeller pitch in metres. [DERIVED] 5.5 inches = 0.1397 m exactly. " +
                 "Pitch/diameter = 0.367, a low-pitch lifter profile suited to endurance rather " +
                 "than speed - correct for this application.")]
        [SerializeField] private float propellerPitchM = 0.1397f;

        [Tooltip("Radius of the sphere bounding the airframe, metres. Used by collision " +
                 "prediction as the vehicle's own extent. [DERIVED] Half the diagonal plus half " +
                 "a propeller: 0.325 + 0.19 = 0.515 m, rounded up for margin.")]
        [SerializeField] private float boundingRadiusM = 0.55f;

        [Header("Inertia")]

        [Tooltip("Moment of inertia about the roll and pitch axes, kg m^2. [ESTIMATE] " +
                 "Four 185 g motors at 0.23 m from the axis (325 mm arm in X configuration " +
                 "gives 325/sqrt(2) = 230 mm per axis) contribute 4 x 0.185 x 0.23^2 = 0.039. " +
                 "Frame, battery and electronics add roughly 0.016. Set explicitly rather than " +
                 "letting Unity infer it from colliders, because inferred inertia depends on " +
                 "collider shape and would change silently if the model changed.")]
        [SerializeField] private float inertiaRollPitch = 0.055f;

        [Tooltip("Moment of inertia about the yaw axis, kg m^2. [ESTIMATE] Motors contribute " +
                 "4 x 0.185 x (0.23^2 + 0.23^2) = 0.078; frame adds roughly 0.017. Yaw inertia " +
                 "is always the largest of the three for a flat quadcopter, which is why yaw is " +
                 "the slowest axis and why yaw authority comes from rotor drag torque rather " +
                 "than from thrust differential.")]
        [SerializeField] private float inertiaYaw = 0.095f;

        // ====================================================================================
        // PROPULSION
        // ====================================================================================

        [Header("Propulsion")]

        [Tooltip("Maximum static thrust per motor in newtons. [ESTIMATE - THE WEAKEST NUMBER " +
                 "IN THIS FILE] 1.6 kgf x 9.81 = 15.7 N. Derived from typical 5010-class " +
                 "performance on a 15x5.5 prop at 6S. The generic '5010 360KV' motor most " +
                 "likely has no published thrust curve at all, so vendor figures for it are " +
                 "unreliable. MEASURE THIS ON A LOAD CELL before trusting the aircraft's " +
                 "thrust margin.")]
        [SerializeField] private float maxThrustPerMotorN = 15.7f;

        [Tooltip("Motor velocity constant in RPM per volt. [UNVERIFIED] The '360KV' in the " +
                 "part designation.")]
        [SerializeField] private float motorKv = 360f;

        [Tooltip("Maximum rotor drag torque per motor in newton-metres, at full throttle. " +
                 "[ESTIMATE] Yaw authority comes entirely from the reaction torque difference " +
                 "between the clockwise and counter-clockwise pairs. Taken as roughly 2.5% of " +
                 "thrust times propeller radius, which is a conventional first approximation for " +
                 "a low-pitch propeller.")]
        [SerializeField] private float maxMotorTorqueNm = 0.28f;

        [Tooltip("Motor and propeller spin-up time constant in seconds. [ESTIMATE] " +
                 "A 15-inch propeller on a 5010 motor has substantial rotational inertia and " +
                 "cannot change speed instantly. Modelling this first-order lag matters: without " +
                 "it the attitude controller appears to have infinite bandwidth, tunes to gains " +
                 "that would be unflyable on real hardware, and the simulation stops being " +
                 "predictive. Larger propellers mean a larger constant.")]
        [SerializeField] private float motorTimeConstantS = 0.09f;

        [Tooltip("Idle throttle when armed, as a fraction. Real ESCs keep the motors turning " +
                 "when armed so the operator can see the aircraft is live. [ESTIMATE]")]
        [SerializeField] private float armedIdleThrottle = 0.06f;

        // ====================================================================================
        // AERODYNAMICS
        // ====================================================================================

        [Header("Aerodynamics")]

        [Tooltip("Translational drag coefficient, N per (m/s). [ESTIMATE] A multirotor's drag is " +
                 "dominated by the frame and by rotor-plane drag, and rises roughly with the " +
                 "square of airspeed. A linear term is used here for stability at low speed, with " +
                 "the quadratic term below handling higher speeds. Tuned so terminal velocity in " +
                 "level flight lands in a plausible 18-22 m/s band.")]
        [SerializeField] private float linearDragCoefficient = 0.55f;

        [Tooltip("Quadratic drag coefficient, N per (m/s)^2. [ESTIMATE] See above.")]
        [SerializeField] private float quadraticDragCoefficient = 0.075f;

        [Tooltip("Angular drag coefficient, Nm per (rad/s). [ESTIMATE] Aerodynamic damping about " +
                 "the body axes. Real multirotors have meaningful rotational damping from the " +
                 "rotor discs, which is what makes them controllable at all.")]
        [SerializeField] private float angularDragCoefficient = 0.09f;

        [Tooltip("Extra vertical drag multiplier during descent, modelling the rotors descending " +
                 "into their own wake. [ESTIMATE] This is a crude stand-in for vortex ring " +
                 "state, which is a real and dangerous phenomenon during fast vertical descent. " +
                 "It is NOT a physically faithful VRS model - it only reproduces the qualitative " +
                 "effect that fast vertical descent is less controllable than slow descent.")]
        [SerializeField] private float descentWakeDragMultiplier = 1.8f;

        // ====================================================================================
        // FLIGHT ENVELOPE
        // ====================================================================================

        [Header("Flight envelope limits")]

        [Tooltip("Maximum commanded horizontal speed, m/s. Autonomous cruise uses this.")]
        [SerializeField] private float maxHorizontalSpeedMps = 12f;

        [Tooltip("Maximum commanded climb rate, m/s.")]
        [SerializeField] private float maxClimbRateMps = 4f;

        [Tooltip("Maximum commanded descent rate, m/s. Lower than climb rate on purpose: fast " +
                 "descent risks the rotors entering their own downwash, and every real autopilot " +
                 "limits descent more tightly than climb for this reason.")]
        [SerializeField] private float maxDescentRateMps = 2.5f;

        [Tooltip("Maximum roll and pitch angle the controller will command, degrees. This is what " +
                 "bounds horizontal acceleration, since a multirotor accelerates by tilting. " +
                 "35 degrees gives tan(35) x 9.81 = 6.9 m/s^2 of lateral acceleration.")]
        [SerializeField] private float maxTiltAngleDeg = 35f;

        [Tooltip("Maximum commanded yaw rate, degrees per second.")]
        [SerializeField] private float maxYawRateDegPerSec = 90f;

        [Tooltip("Maximum operating altitude above launch, metres. 120 m is the ceiling for " +
                 "small unmanned aircraft in most jurisdictions including India's DGCA rules for " +
                 "the micro and small categories. [UNVERIFIED - confirm against current DGCA CAR " +
                 "Section 3 Series X before any real flight.]")]
        [SerializeField] private float maxAltitudeAglM = 120f;

        // ====================================================================================
        // BATTERY
        // ====================================================================================

        [Header("Battery")]

        [Tooltip("Cell count in series. 6S.")]
        [SerializeField] private int batteryCells = 6;

        [Tooltip("Capacity in milliamp-hours. [UNVERIFIED] 6200 mAh as specified.")]
        [SerializeField] private float batteryCapacityMah = 6200f;

        [Tooltip("Nominal volts per cell. [DERIVED] LiPo nominal is 3.7 V, so 6S = 22.2 V.")]
        [SerializeField] private float nominalVoltsPerCell = 3.7f;

        [Tooltip("Fully charged volts per cell. [DERIVED] LiPo full charge is 4.2 V, so a 6S " +
                 "pack presents 25.2 V. THIS IS THE NUMBER THAT MATTERS for every downstream " +
                 "voltage limit, and it is the number that makes the classic 4S-rated Pixhawk " +
                 "power module unsuitable for this build. See Docs/10 Critical Concerns.")]
        [SerializeField] private float fullVoltsPerCell = 4.2f;

        [Tooltip("Volts per cell at which the pack is considered empty. 3.5 V under load is a " +
                 "sensible landing threshold; taking LiPo below about 3.3 V damages it " +
                 "permanently. [ESTIMATE - standard practice, not a datasheet figure.]")]
        [SerializeField] private float emptyVoltsPerCell = 3.5f;

        [Tooltip("Internal resistance of the whole pack in ohms. [ESTIMATE] Produces realistic " +
                 "voltage sag under load, which is what actually triggers a low-voltage failsafe " +
                 "in flight - a pack that reads fine at rest can sag below the threshold during a " +
                 "climb. Simulating sag makes the failsafe demonstration honest.")]
        [SerializeField] private float packInternalResistanceOhm = 0.018f;

        [Tooltip("Avionics current draw in amperes, excluding motors: flight controller, GPS, " +
                 "receiver, telemetry, and a Raspberry Pi 5 running vision. [ESTIMATE] " +
                 "A Pi 5 under load draws roughly 7-9 W; at 22.2 V through a BEC at ~88% " +
                 "efficiency that is about 0.45 A, plus roughly 0.2 A for everything else.")]
        [SerializeField] private float avionicsCurrentA = 0.65f;

        [Header("Power model")]

        [Tooltip("Electrical power constant in watts per newton^1.5, per motor. [ESTIMATE, but " +
                 "physically grounded rather than guessed.]\n\n" +
                 "Momentum theory gives induced power proportional to thrust^1.5, so electrical " +
                 "power per motor is modelled as k x T^1.5. The constant is fitted to a hover " +
                 "figure of merit of 8 g/W, which is typical for a 15-inch propeller on a " +
                 "5010-class motor at part throttle: hover thrust per motor is 7.16 N (730 g), so " +
                 "hover power is 730/8 = 91 W, giving k = 91 / 7.16^1.5 = 4.75.\n\n" +
                 "This is preferred over hardcoding a full-throttle amp figure because it scales " +
                 "correctly across the whole throttle range and it makes the underlying assumption " +
                 "(the figure of merit) explicit and checkable. Replace k after measuring the real " +
                 "motor on a thrust stand with a wattmeter.")]
        [SerializeField] private float powerConstantWattsPerNewton15 = 4.75f;

        [Tooltip("No-load power draw per motor while spinning at armed idle, watts. [ESTIMATE] " +
                 "Bearing friction, iron loss and ESC switching loss, which do not vanish at low " +
                 "thrust.")]
        [SerializeField] private float motorIdlePowerW = 4f;

        [Tooltip("Motor phase resistance in ohms. [UNVERIFIED] Used for the copper-loss thermal " +
                 "model. A 5010-class motor is typically in the 40-60 milliohm range. Measurable " +
                 "with a milliohm meter across any two phase leads.")]
        [SerializeField] private float motorPhaseResistanceOhm = 0.045f;

        [Tooltip("Motor thermal resistance to ambient, degrees C per watt. [ESTIMATE] " +
                 "Determines steady-state temperature rise from copper loss. Roughly 1.2 C/W for " +
                 "an open-frame outrunner with propwash over it.")]
        [SerializeField] private float motorThermalResistanceCPerW = 1.2f;

        [Tooltip("Motor thermal time constant in seconds. [ESTIMATE] A motor's stator has real " +
                 "thermal mass and takes tens of seconds to heat, which is why a brief full-throttle " +
                 "burst is harmless and a sustained climb is not. Modelling the lag rather than the " +
                 "steady state is what makes the temperature display meaningful.")]
        [SerializeField] private float motorThermalTimeConstantS = 55f;

        [Tooltip("Ambient air temperature, degrees C. Bengaluru averages roughly 24-32 C by season.")]
        [SerializeField] private float ambientTemperatureC = 30f;

        [Tooltip("Motor temperature at which a warning is raised, degrees C. [ESTIMATE] Standard " +
                 "neodymium magnets begin to lose flux irreversibly above roughly 80 C for N52 " +
                 "grade and 120 C for higher-temperature grades; winding insulation is usually " +
                 "rated to 155 C or above. 85 C is a conservative warning point.")]
        [SerializeField] private float motorWarningTemperatureC = 85f;

        // ====================================================================================
        // CONTROL GAINS
        // ====================================================================================
        //
        // UNITS: every gain below operates in SI units - radians, radians per second, metres,
        // metres per second. Not degrees.
        //
        // This is a deliberate choice, not an oversight. ArduPilot and PX4 both express their
        // attitude gains in these units, so the values here are directly comparable to
        // ATC_RAT_RLL_P, ATC_ANG_RLL_P and friends. When the team eventually tunes the real
        // aircraft in Mission Planner, the numbers they read here will be in the same currency as
        // the numbers they type there. Working in degrees internally would break that
        // correspondence and would make the simulation's tuning effort non-transferable, which
        // would waste most of its value.
        //
        // The UI converts to degrees for display, because degrees are what an operator reads.
        // The conversion happens at the display boundary and nowhere else.

        [Header("Attitude rate loop (innermost, ~100 Hz)")]
        [Tooltip("Proportional gain on body angular rate error, in rad/s. Ordered (roll, pitch, yaw). " +
                 "Comparable to ArduPilot's ATC_RAT_RLL_P / ATC_RAT_PIT_P / ATC_RAT_YAW_P, whose " +
                 "defaults for a large multirotor sit around 0.10-0.15. Yaw carries a higher gain " +
                 "because yaw inertia is the largest and yaw authority the weakest.")]
        [SerializeField] private Vector3 rateKp = new Vector3(0.085f, 0.085f, 0.16f);

        [Tooltip("Integral gain on body rate error. Removes steady-state error from a mis-trimmed " +
                 "airframe or an off-centre battery. Ordered (roll, pitch, yaw).")]
        [SerializeField] private Vector3 rateKi = new Vector3(0.045f, 0.045f, 0.05f);

        [Tooltip("Derivative gain on body rate error. Small by necessity: differentiating a rate " +
                 "signal means differentiating gyro noise twice over. Zero on yaw, matching common " +
                 "practice - yaw is slow enough that the phase lead is not needed and the noise " +
                 "penalty is not worth paying. Ordered (roll, pitch, yaw).")]
        [SerializeField] private Vector3 rateKd = new Vector3(0.0022f, 0.0022f, 0f);

        [Header("Attitude angle loop (~100 Hz)")]
        [Tooltip("Converts attitude error in radians into a target body rate in rad/s. Comparable " +
                 "to ArduPilot's ATC_ANG_RLL_P, default 4.5. Cascaded rather than direct because a " +
                 "single loop from angle to torque cannot reject disturbances at both timescales - " +
                 "this is the architecture both ArduPilot and PX4 use and there is no reason to " +
                 "depart from it. Ordered (roll, pitch, yaw).")]
        [SerializeField] private Vector3 attitudeKp = new Vector3(6.5f, 6.5f, 4.0f);

        [Header("Horizontal velocity loop (~50 Hz)")]
        [Tooltip("Converts horizontal velocity error in m/s into a target acceleration in m/s^2, " +
                 "which is then converted to a tilt angle via atan(a/g). At 1.35, a 1 m/s error asks " +
                 "for 1.35 m/s^2, i.e. about 7.8 degrees of tilt.")]
        [SerializeField] private float velocityKp = 1.35f;
        [SerializeField] private float velocityKi = 0.28f;
        [SerializeField] private float velocityKd = 0.10f;

        [Header("Horizontal position loop (outermost, ~20 Hz)")]
        [Tooltip("Converts position error in metres into a target velocity in m/s. Kept deliberately " +
                 "gentle: an aggressive position loop fights the velocity loop beneath it and " +
                 "produces the oscillating overshoot that makes a simulated aircraft look like a toy.")]
        [SerializeField] private float positionKp = 0.95f;

        [Header("Altitude loop")]
        [Tooltip("Converts altitude error in metres into a target climb rate in m/s. At 2.4, a 1 m " +
                 "error asks for 2.4 m/s, clamped to the climb and descent limits above.")]
        [SerializeField] private float altitudeKp = 2.4f;

        [Header("Climb rate loop")]
        [Tooltip("Converts climb rate error in m/s into a rotor speed offset added to the hover " +
                 "throttle. At 0.06, a 1 m/s error adds 6% rotor speed, which for this airframe is " +
                 "roughly 2 m/s^2 of vertical acceleration - a half-second time constant, brisk " +
                 "without being abrupt.")]
        [SerializeField] private float climbRateKp = 0.06f;

        [Tooltip("Integral gain on climb rate error. Carries the real work here: it absorbs the " +
                 "error between the estimated hover throttle and the actual hover throttle, which " +
                 "differs because mass is estimated, thrust is estimated, and the battery sags as " +
                 "the flight proceeds. Without it the aircraft would slowly sink as the pack drains.")]
        [SerializeField] private float climbRateKi = 0.12f;

        [Tooltip("Derivative gain on climb rate error. Small; vertical acceleration is a noisy " +
                 "signal.")]
        [SerializeField] private float climbRateKd = 0.01f;

        // ====================================================================================
        // DERIVED PROPERTIES
        // ====================================================================================

        public float MassKg { get { return massKg; } }
        public float WheelbaseM { get { return wheelbaseM; } }
        public float PropellerDiameterM { get { return propellerDiameterM; } }
        public float PropellerPitchM { get { return propellerPitchM; } }
        public float BoundingRadiusM { get { return boundingRadiusM; } }
        public float InertiaRollPitch { get { return inertiaRollPitch; } }
        public float InertiaYaw { get { return inertiaYaw; } }

        public float MaxThrustPerMotorN { get { return maxThrustPerMotorN; } }
        public float MotorKv { get { return motorKv; } }
        public float MaxMotorTorqueNm { get { return maxMotorTorqueNm; } }
        public float MotorTimeConstantS { get { return motorTimeConstantS; } }
        public float ArmedIdleThrottle { get { return armedIdleThrottle; } }

        public float LinearDragCoefficient { get { return linearDragCoefficient; } }
        public float QuadraticDragCoefficient { get { return quadraticDragCoefficient; } }
        public float AngularDragCoefficient { get { return angularDragCoefficient; } }
        public float DescentWakeDragMultiplier { get { return descentWakeDragMultiplier; } }

        public float MaxHorizontalSpeedMps { get { return maxHorizontalSpeedMps; } }
        public float MaxClimbRateMps { get { return maxClimbRateMps; } }
        public float MaxDescentRateMps { get { return maxDescentRateMps; } }
        public float MaxTiltAngleDeg { get { return maxTiltAngleDeg; } }
        public float MaxYawRateDegPerSec { get { return maxYawRateDegPerSec; } }
        public float MaxAltitudeAglM { get { return maxAltitudeAglM; } }

        public int BatteryCells { get { return batteryCells; } }
        public float BatteryCapacityMah { get { return batteryCapacityMah; } }
        public float PackInternalResistanceOhm { get { return packInternalResistanceOhm; } }
        public float AvionicsCurrentA { get { return avionicsCurrentA; } }

        public float PowerConstantWattsPerNewton15 { get { return powerConstantWattsPerNewton15; } }
        public float MotorIdlePowerW { get { return motorIdlePowerW; } }
        public float MotorPhaseResistanceOhm { get { return motorPhaseResistanceOhm; } }
        public float MotorThermalResistanceCPerW { get { return motorThermalResistanceCPerW; } }
        public float MotorThermalTimeConstantS { get { return motorThermalTimeConstantS; } }
        public float AmbientTemperatureC { get { return ambientTemperatureC; } }
        public float MotorWarningTemperatureC { get { return motorWarningTemperatureC; } }

        public Vector3 RateKp { get { return rateKp; } }
        public Vector3 RateKi { get { return rateKi; } }
        public Vector3 RateKd { get { return rateKd; } }
        public Vector3 AttitudeKp { get { return attitudeKp; } }
        public float VelocityKp { get { return velocityKp; } }
        public float VelocityKi { get { return velocityKi; } }
        public float VelocityKd { get { return velocityKd; } }
        public float PositionKp { get { return positionKp; } }
        public float AltitudeKp { get { return altitudeKp; } }
        public float ClimbRateKp { get { return climbRateKp; } }
        public float ClimbRateKi { get { return climbRateKi; } }
        public float ClimbRateKd { get { return climbRateKd; } }

        /// <summary>Nominal pack voltage. [DERIVED] cells x 3.7 V.</summary>
        public float NominalVoltage { get { return batteryCells * nominalVoltsPerCell; } }

        /// <summary>Fully charged pack voltage. [DERIVED] cells x 4.2 V.</summary>
        public float FullVoltage { get { return batteryCells * fullVoltsPerCell; } }

        /// <summary>Pack voltage considered empty. [DERIVED] cells x 3.5 V.</summary>
        public float EmptyVoltage { get { return batteryCells * emptyVoltsPerCell; } }

        /// <summary>Pack energy in watt-hours. [DERIVED] Ah x nominal volts.</summary>
        public float BatteryEnergyWh
        {
            get { return (batteryCapacityMah / 1000f) * NominalVoltage; }
        }

        /// <summary>Weight in newtons. [DERIVED] m x g.</summary>
        public float WeightN { get { return massKg * 9.80665f; } }

        /// <summary>Total available thrust in newtons. [DERIVED] 4 x per-motor thrust.</summary>
        public float TotalThrustN { get { return 4f * maxThrustPerMotorN; } }

        /// <summary>
        /// Thrust-to-weight ratio. [DERIVED from an ESTIMATE, so only as good as
        /// MaxThrustPerMotorN.] The conventional minimum for a controllable multirotor is 2.0;
        /// below that the aircraft cannot arrest a descent or hold attitude in gusts. With the
        /// default values this lands near 2.14, which is acceptable but leaves no margin for
        /// added payload.
        /// </summary>
        public float ThrustToWeightRatio { get { return TotalThrustN / WeightN; } }

        /// <summary>
        /// Distance from the centre of mass to each motor, metres. [DERIVED] half the wheelbase.
        /// </summary>
        public float ArmLengthM { get { return wheelbaseM * 0.5f; } }

        /// <summary>
        /// Per-axis motor offset for an X configuration, metres. [DERIVED] In an X layout each
        /// motor sits diagonally, so its contribution to roll and to pitch is the arm length
        /// divided by root two.
        /// </summary>
        public float MotorAxisOffsetM
        {
            get { return ArmLengthM * 0.70710678f; }
        }

        /// <summary>
        /// Rotor speed fraction required to hover, in [0,1].
        ///
        /// [DERIVED] Rotor thrust scales with the square of rotor speed, so hovering needs
        /// sqrt(1 / thrust-to-weight) of maximum rotor speed. At a ratio of 2.14 that is
        /// sqrt(0.467) = 0.684, i.e. about 68% of maximum rotor speed.
        ///
        /// Worth understanding if you are comparing against a real aircraft: pilots typically
        /// report hovering near 50% STICK, not 68%. That is not a contradiction. Real autopilots
        /// apply thrust linearisation (ArduPilot's MOT_THST_EXPO) so that stick position maps to
        /// thrust rather than to rotor speed, which moves the hover point on the stick without
        /// changing the physics.
        /// </summary>
        public float HoverThrottleFraction
        {
            get
            {
                float ratio = ThrustToWeightRatio;
                if (ratio <= 0.0001f)
                {
                    return 1f;
                }
                return Mathf.Clamp01(Mathf.Sqrt(1f / ratio));
            }
        }

        /// <summary>
        /// Electrical power for one motor producing the given thrust, watts.
        ///
        /// [MODEL] Momentum theory: induced power scales with thrust^1.5. The constant folds in
        /// propeller figure of merit, motor efficiency and ESC efficiency, all of which vary with
        /// operating point in reality - so this is a single-point fit extrapolated across the range,
        /// accurate near hover and progressively optimistic towards full throttle.
        /// </summary>
        public float MotorPowerForThrust(float thrustN)
        {
            if (thrustN <= 0f)
            {
                return 0f;
            }
            return motorIdlePowerW +
                   powerConstantWattsPerNewton15 * Mathf.Pow(thrustN, 1.5f);
        }

        /// <summary>Total electrical power at hover, watts, including avionics. [DERIVED]</summary>
        public float HoverPowerW
        {
            get
            {
                float perMotorThrust = WeightN * 0.25f;
                return 4f * MotorPowerForThrust(perMotorThrust) +
                       avionicsCurrentA * NominalVoltage;
            }
        }

        /// <summary>
        /// Estimated hover endurance in minutes.
        ///
        /// [ESTIMATE] Uses the power model above and 80% depth of discharge, which is correct
        /// practice for LiPo longevity. Ignores wind, manoeuvring, climb and battery ageing, so
        /// treat it as an upper bound. For mission planning use roughly 70% of this figure.
        /// </summary>
        public float EstimatedHoverEnduranceMin
        {
            get
            {
                float totalWatts = HoverPowerW;
                if (totalWatts <= 0.01f)
                {
                    return 0f;
                }
                float usableWh = BatteryEnergyWh * 0.8f;
                return usableWh / totalWatts * 60f;
            }
        }

        /// <summary>
        /// Full-throttle pack current in amperes. [DERIVED from the power model, which is itself
        /// an estimate.] This is the figure that sets the minimum ESC rating and pack C-rating.
        /// </summary>
        public float FullThrottleCurrentA
        {
            get
            {
                float totalW = 4f * MotorPowerForThrust(maxThrustPerMotorN) +
                               avionicsCurrentA * NominalVoltage;
                return totalW / Mathf.Max(1f, NominalVoltage);
            }
        }

        /// <summary>
        /// Per-motor full-throttle current in amperes. Compare against the ESC's continuous rating.
        /// [DERIVED from an estimate.]
        /// </summary>
        public float FullThrottleCurrentPerMotorA
        {
            get
            {
                return MotorPowerForThrust(maxThrustPerMotorN) / Mathf.Max(1f, NominalVoltage);
            }
        }

        /// <summary>
        /// Minimum battery C-rating required to sustain full throttle. [DERIVED] current / capacity.
        /// Budget C-ratings are routinely overstated, so specify comfortably above this.
        /// </summary>
        public float MinimumBatteryCRating
        {
            get { return FullThrottleCurrentA / (batteryCapacityMah / 1000f); }
        }

        // ====================================================================================
        // VALIDATION
        // ====================================================================================

        /// <summary>
        /// Checks the configuration for physically implausible or unflyable combinations and
        /// returns a human-readable report. Surfaced by the editor validator and by the preflight
        /// check, because a misconfigured airframe should fail preflight rather than fly badly and
        /// leave the operator guessing why.
        /// </summary>
        public string Validate()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            if (ThrustToWeightRatio < 1.5f)
            {
                sb.AppendLine("CRITICAL: thrust-to-weight is " + ThrustToWeightRatio.ToString("F2") +
                              ". Below 1.5 the aircraft cannot control its attitude while " +
                              "climbing. It will not fly.");
            }
            else if (ThrustToWeightRatio < 2.0f)
            {
                sb.AppendLine("WARNING: thrust-to-weight is " + ThrustToWeightRatio.ToString("F2") +
                              ", below the conventional 2.0 minimum. The aircraft will fly but " +
                              "will struggle to arrest a descent or hold attitude in gusts, and " +
                              "has no margin for added payload.");
            }

            if (HoverThrottleFraction > 0.75f)
            {
                sb.AppendLine("WARNING: hover requires " +
                              (HoverThrottleFraction * 100f).ToString("F0") +
                              "% of maximum rotor speed. Above about 75% there is too little " +
                              "control authority left for manoeuvring.");
            }

            if (propellerDiameterM * 2f > wheelbaseM * 0.70710678f * 2f)
            {
                sb.AppendLine("WARNING: propellers may overlap. In an X configuration adjacent " +
                              "motors are wheelbase/sqrt(2) = " +
                              (wheelbaseM * 0.70710678f).ToString("F3") +
                              " m apart, and the propeller diameter is " +
                              propellerDiameterM.ToString("F3") + " m.");
            }

            if (MinimumBatteryCRating > 20f)
            {
                sb.AppendLine("NOTE: full throttle needs at least " +
                              MinimumBatteryCRating.ToString("F1") +
                              "C from the pack (" + FullThrottleCurrentA.ToString("F0") +
                              " A from " + (batteryCapacityMah / 1000f).ToString("F1") +
                              " Ah). Specify comfortably above this; vendor C-ratings are " +
                              "routinely optimistic.");
            }

            if (FullThrottleCurrentPerMotorA > 40f)
            {
                sb.AppendLine("WARNING: the power model puts full-throttle current at " +
                              FullThrottleCurrentPerMotorA.ToString("F1") +
                              " A per motor, which exceeds a 40 A ESC's continuous rating. " +
                              "Note that this is a MODELLED figure, not a measurement - verify on " +
                              "a wattmeter before drawing conclusions either way.");
            }

            if (motorTimeConstantS < 0.02f)
            {
                sb.AppendLine("NOTE: the motor time constant is very short. A 15-inch propeller " +
                              "has real rotational inertia; an unrealistically fast motor lets the " +
                              "attitude controller be tuned to gains that would be unflyable on " +
                              "hardware, which defeats the purpose of simulating first.");
            }

            if (sb.Length == 0)
            {
                sb.AppendLine("No implausible parameter combinations detected.");
                sb.AppendLine("Reminder: plausible is not the same as verified. The thrust figure " +
                              "in particular is an estimate for a motor with no published curve.");
            }

            return sb.ToString();
        }

        private void OnValidate()
        {
            massKg = Mathf.Max(0.1f, massKg);
            wheelbaseM = Mathf.Max(0.05f, wheelbaseM);
            maxThrustPerMotorN = Mathf.Max(0.1f, maxThrustPerMotorN);
            motorTimeConstantS = Mathf.Max(0.001f, motorTimeConstantS);
            batteryCells = Mathf.Clamp(batteryCells, 1, 14);
            batteryCapacityMah = Mathf.Max(100f, batteryCapacityMah);
        }
    }
}
