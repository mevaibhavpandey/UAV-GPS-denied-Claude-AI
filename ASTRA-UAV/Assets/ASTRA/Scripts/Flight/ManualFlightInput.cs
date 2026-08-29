using UnityEngine;
using UnityEngine.InputSystem;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Logging;

namespace Astra.Flight
{
    /// <summary>
    /// Translates operator input into flight controller commands.
    ///
    /// KEY MAP (as specified)
    /// ----------------------
    ///   W / S            pitch forward / backward
    ///   A / D            roll left / right
    ///   Q / E            yaw left / right
    ///   Space / L-Ctrl   climb / descend
    ///   R                arm
    ///   F                disarm
    ///   L                land
    ///   H                hover, i.e. hold position
    ///   T                automatic takeoff        [addition, not in the original key map]
    ///   M                assume manual control    [addition, not in the original key map]
    ///
    /// WHY THE INPUT IS SMOOTHED
    /// -------------------------
    /// A key is binary: pressed or not. A real control stick is analogue and takes a moment to move.
    /// Feeding a raw 0-to-1 step into the attitude controller commands full tilt instantly, and the
    /// aircraft snaps to 35 degrees of bank in one frame. That looks like a video game, and it is also
    /// nothing like what a real airframe experiences, because no pilot can move a stick that fast.
    ///
    /// So each axis ramps toward its commanded value at a configurable rate and self-centres faster
    /// than it deflects - which is what a spring-centred gimbal does. The result is that keyboard
    /// flight looks like stick flight, and the physics is being asked for something physically
    /// reasonable.
    ///
    /// This is emphatically NOT input filtering used to hide a control problem. The smoothing happens
    /// entirely upstream of the flight controller, on the operator's demand. The controller and the
    /// airframe below it see a plausible stick input and respond to it honestly.
    ///
    /// WHY DIRECT DEVICE READS RATHER THAN AN .inputactions ASSET
    /// ---------------------------------------------------------
    /// This uses the New Input System's device API - Keyboard.current, Gamepad.current - with the key
    /// for each function exposed as a serialised field, so bindings are editable in the inspector.
    /// An .inputactions asset would be more idiomatic, but it is a generated file full of GUIDs; a
    /// hand-authored one is difficult to verify and a corrupt one fails at runtime with an unhelpful
    /// message. Inspector-editable Key fields give the same rebinding capability with no fragile
    /// generated asset, and Keyboard.current is the New Input System, not the legacy Input class.
    /// </summary>
    [DisallowMultipleComponent]
    public class ManualFlightInput : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private FlightControlSystem flightController;

        [Header("Attitude keys")]
        [SerializeField] private Key pitchForwardKey = Key.W;
        [SerializeField] private Key pitchBackwardKey = Key.S;
        [SerializeField] private Key rollLeftKey = Key.A;
        [SerializeField] private Key rollRightKey = Key.D;
        [SerializeField] private Key yawLeftKey = Key.Q;
        [SerializeField] private Key yawRightKey = Key.E;

        [Header("Throttle keys")]
        [SerializeField] private Key climbKey = Key.Space;
        [SerializeField] private Key descendKey = Key.LeftCtrl;

        [Header("Command keys")]
        [SerializeField] private Key armKey = Key.R;
        [SerializeField] private Key disarmKey = Key.F;
        [SerializeField] private Key landKey = Key.L;
        [SerializeField] private Key hoverKey = Key.H;
        [SerializeField] private Key takeoffKey = Key.T;

        [Tooltip("Takes manual control back from the autonomy stack or from a failsafe. Needed " +
                 "because stick input alone is deliberately ignored while another source holds " +
                 "authority, so without an explicit key the operator could not recover an aircraft " +
                 "that had entered an emergency descent.")]
        [SerializeField] private Key assumeControlKey = Key.M;

        [Header("Stick feel")]
        [Tooltip("How fast an axis deflects toward full travel, in units per second. 3 means about " +
                 "a third of a second from centre to full - roughly a brisk but realistic stick " +
                 "movement.")]
        [SerializeField] private float deflectionRate = 3f;

        [Tooltip("How fast an axis returns to centre when released, units per second. Faster than " +
                 "deflection, as a spring-centred gimbal is.")]
        [SerializeField] private float centringRate = 5f;

        [Tooltip("Throttle axis return rate. Slower than the attitude axes: releasing the climb key " +
                 "should settle into a hold rather than snap the aircraft to level flight.")]
        [SerializeField] private float throttleCentringRate = 3f;

        [Header("Automatic takeoff")]
        [Tooltip("Altitude above ground the T key climbs to, metres.")]
        [SerializeField] private float autoTakeoffAltitudeM = 15f;

        [Header("Gamepad")]
        [Tooltip("Read a connected gamepad in addition to the keyboard. Left stick is throttle and " +
                 "yaw, right stick is pitch and roll - the arrangement a Mode 2 transmitter uses.")]
        [SerializeField] private bool enableGamepad = true;

        [Tooltip("Ignore gamepad deflection below this magnitude. Worn analogue sticks rest slightly " +
                 "off centre, and without a dead zone the aircraft drifts continuously.")]
        [Range(0f, 0.4f)]
        [SerializeField] private float gamepadDeadZone = 0.12f;

        // ---- Smoothed axis state ----
        private float _roll;
        private float _pitch;
        private float _yaw;
        private float _throttleAxis;   // -1 = full descent, 0 = hold, +1 = full climb

        private bool _inputIsActive;

        /// <summary>
        /// Current smoothed stick positions, for the virtual stick display on the ground station.
        /// Exposing these lets the UI show what the operator is commanding next to what the aircraft
        /// is doing, which is how the difference between demand and response becomes visible.
        /// </summary>
        public float RollStick { get { return _roll; } }
        public float PitchStick { get { return _pitch; } }
        public float YawStick { get { return _yaw; } }
        public float ThrottleStick { get { return _throttleAxis; } }

        /// <summary>True if any control axis is deflected. Used by the UI to show manual activity.</summary>
        public bool IsCommanding { get { return _inputIsActive; } }

        private void Awake()
        {
            if (flightController == null)
            {
                flightController = GetComponent<FlightControlSystem>();
            }
            if (flightController == null)
            {
                flightController = FindAnyObjectByType<FlightControlSystem>();
            }
        }

        private void Update()
        {
            if (flightController == null)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;

            // A missing keyboard is normal, not an error: the build may be running with only a
            // gamepad attached, or on a machine where the device has not been enumerated yet.
            if (keyboard != null)
            {
                HandleCommandKeys(keyboard);
            }

            UpdateAxes(keyboard);
            PushToFlightController();
        }

        // ====================================================================================
        // DISCRETE COMMANDS
        // ====================================================================================

        private void HandleCommandKeys(Keyboard keyboard)
        {
            if (keyboard[armKey].wasPressedThisFrame)
            {
                string reason;
                if (flightController.TryArm(out reason))
                {
                    // Taking manual control on a successful arm from the keyboard is the honest
                    // interpretation of the operator's intent: they armed with a key, so they intend
                    // to fly with keys. An autonomous mission sets the control source itself.
                    flightController.SetControlSource(ControlSource.Manual);
                    ResetAxes();
                }
                // The refusal is already logged with its reason by TryArm. Nothing to add here -
                // duplicating it would just put the same line in the log twice.
            }

            if (keyboard[disarmKey].wasPressedThisFrame)
            {
                flightController.Disarm();
                ResetAxes();
            }

            if (keyboard[assumeControlKey].wasPressedThisFrame)
            {
                if (flightController.CurrentControlSource != ControlSource.Manual)
                {
                    ResetAxes();
                    flightController.SetControlSource(ControlSource.Manual);

                    // Command a hover on handover rather than dropping the aircraft into manual with
                    // centred sticks. Taking over a moving aircraft and immediately commanding level
                    // attitude would let it coast; commanding a hold stops it and hands the operator a
                    // stationary aircraft, which is what they want when they grab control in a hurry.
                    flightController.CommandHover();
                    EventLog.Warning(LogSource.Operator,
                        "Operator assumed manual control - holding position");
                }
            }

            if (keyboard[takeoffKey].wasPressedThisFrame)
            {
                flightController.SetControlSource(ControlSource.Manual);
                flightController.CommandTakeoff(autoTakeoffAltitudeM);
                ResetAxes();
            }

            if (keyboard[landKey].wasPressedThisFrame)
            {
                flightController.CommandLand();
                ResetAxes();
            }

            if (keyboard[hoverKey].wasPressedThisFrame)
            {
                flightController.CommandHover();
                ResetAxes();
                EventLog.Info(LogSource.Operator, "Position hold commanded by operator");
            }
        }

        // ====================================================================================
        // CONTINUOUS AXES
        // ====================================================================================

        private void UpdateAxes(Keyboard keyboard)
        {
            float dt = Time.deltaTime;

            // ---- Raw demands from the keyboard: each axis is the difference of two keys ----
            float rollDemand = 0f;
            float pitchDemand = 0f;
            float yawDemand = 0f;
            float throttleDemand = 0f;

            if (keyboard != null)
            {
                if (keyboard[rollRightKey].isPressed) rollDemand += 1f;
                if (keyboard[rollLeftKey].isPressed) rollDemand -= 1f;

                if (keyboard[pitchForwardKey].isPressed) pitchDemand += 1f;
                if (keyboard[pitchBackwardKey].isPressed) pitchDemand -= 1f;

                if (keyboard[yawRightKey].isPressed) yawDemand += 1f;
                if (keyboard[yawLeftKey].isPressed) yawDemand -= 1f;

                if (keyboard[climbKey].isPressed) throttleDemand += 1f;
                if (keyboard[descendKey].isPressed) throttleDemand -= 1f;
            }

            // ---- Gamepad, if present, overrides where it is deflected ----
            // A gamepad axis is already analogue, so it bypasses the smoothing entirely: applying a
            // ramp to a real stick would add lag the operator can feel, and would make the aircraft
            // respond worse to better input.
            bool gamepadActive = false;
            if (enableGamepad)
            {
                Gamepad pad = Gamepad.current;
                if (pad != null)
                {
                    Vector2 left = ApplyDeadZone(pad.leftStick.ReadValue());
                    Vector2 right = ApplyDeadZone(pad.rightStick.ReadValue());

                    if (Mathf.Abs(right.x) > 0f || Mathf.Abs(right.y) > 0f ||
                        Mathf.Abs(left.x) > 0f || Mathf.Abs(left.y) > 0f)
                    {
                        gamepadActive = true;
                        _roll = right.x;
                        _pitch = right.y;
                        _yaw = left.x;
                        _throttleAxis = left.y;
                    }
                }
            }

            if (!gamepadActive)
            {
                _roll = MoveAxis(_roll, rollDemand, dt, deflectionRate, centringRate);
                _pitch = MoveAxis(_pitch, pitchDemand, dt, deflectionRate, centringRate);
                _yaw = MoveAxis(_yaw, yawDemand, dt, deflectionRate, centringRate);
                _throttleAxis = MoveAxis(_throttleAxis, throttleDemand, dt,
                                         deflectionRate, throttleCentringRate);
            }

            _inputIsActive = Mathf.Abs(_roll) > 0.001f || Mathf.Abs(_pitch) > 0.001f ||
                             Mathf.Abs(_yaw) > 0.001f || Mathf.Abs(_throttleAxis) > 0.001f;
        }

        /// <summary>
        /// Moves one axis toward its demand, ramping at the deflection rate when driven and at the
        /// centring rate when released.
        ///
        /// The distinction matters more than it looks. Using one rate for both means either the
        /// aircraft responds sluggishly to a new input, or it snaps back to level the instant a key
        /// is released - and the second is worse, because it makes every manoeuvre end in a jolt.
        /// </summary>
        private static float MoveAxis(float current, float demand, float dt,
                                      float driveRate, float releaseRate)
        {
            bool released = Mathf.Abs(demand) < 0.001f;
            float rate = released ? releaseRate : driveRate;
            return Mathf.MoveTowards(current, demand, rate * dt);
        }

        private Vector2 ApplyDeadZone(Vector2 raw)
        {
            float magnitude = raw.magnitude;
            if (magnitude < gamepadDeadZone)
            {
                return Vector2.zero;
            }

            // Rescale from the dead zone edge rather than simply passing the raw value through, so
            // the axis still starts from zero at the edge of the dead zone. Without the rescale the
            // control jumps to the dead zone value the moment the stick leaves centre.
            float rescaled = (magnitude - gamepadDeadZone) / (1f - gamepadDeadZone);
            return raw.normalized * Mathf.Clamp01(rescaled);
        }

        private void ResetAxes()
        {
            _roll = 0f;
            _pitch = 0f;
            _yaw = 0f;
            _throttleAxis = 0f;
            _inputIsActive = false;
        }

        // ====================================================================================
        // OUTPUT
        // ====================================================================================

        private void PushToFlightController()
        {
            // Only push while the operator actually holds authority. Sending stick positions during an
            // autonomous mission would let a stray keypress fight the navigation stack, which is the
            // kind of thing that ruins a demonstration in front of an audience.
            if (flightController.CurrentControlSource != ControlSource.Manual)
            {
                return;
            }

            // Throttle axis in [-1,1] maps to the [0,1] the controller expects, with 0.5 as the
            // altitude-hold neutral point.
            float throttle = 0.5f + _throttleAxis * 0.5f;

            flightController.SetManualInput(_roll, _pitch, _yaw, throttle);
        }
    }
}
