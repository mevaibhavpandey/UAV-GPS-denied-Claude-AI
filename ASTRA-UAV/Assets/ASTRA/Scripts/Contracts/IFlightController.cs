using UnityEngine;

namespace Astra.Contracts
{
    /// <summary>
    /// The commands a flight controller accepts. These are deliberately the same abstractions
    /// ArduPilot and PX4 expose over MAVLink, so that a future MavlinkFlightController can satisfy
    /// this interface by forwarding rather than by translating between mismatched concepts.
    ///
    /// Note what is NOT here: there is no SetPosition. Nothing may teleport the aircraft. The only
    /// way to move is to request a velocity, attitude or waypoint and let the physics respond. That
    /// constraint is enforced by the interface itself rather than by convention.
    /// </summary>
    public interface IFlightController
    {
        string Name { get; }
        DataProvenance Provenance { get; }
        SubsystemStatus Status { get; }

        bool IsArmed { get; }

        /// <summary>Current state in the flight state machine.</summary>
        FlightState State { get; }

        /// <summary>
        /// Attempts to arm. Returns false and populates reason if a preflight condition blocks it.
        /// Refusing to arm with a stated reason is the behaviour real autopilots have, and
        /// reproducing it makes the demonstration credible to anyone who has flown one.
        /// </summary>
        bool TryArm(out string reason);

        void Disarm();

        /// <summary>Commands a climb to the given height above the launch point, in metres.</summary>
        void CommandTakeoff(float targetAltitudeAgl);

        /// <summary>Holds current position and altitude.</summary>
        void CommandHover();

        /// <summary>
        /// Requests a velocity in world space, m/s, plus a yaw rate in deg/s. This is the primary
        /// interface the navigation stack uses: it produces velocity commands, and the flight
        /// controller works out the attitude and thrust needed to achieve them.
        /// </summary>
        void CommandVelocity(Vector3 worldVelocity, float yawRateDegPerSec);

        /// <summary>
        /// Requests the vehicle fly to a world position at a given cruise speed. Implemented on top
        /// of CommandVelocity by the position controller.
        /// </summary>
        void CommandGoTo(Vector3 worldPosition, float cruiseSpeedMps);

        /// <summary>Requests a specific heading, degrees clockwise from north.</summary>
        void CommandHeading(float headingDegrees);

        /// <summary>Begins a controlled descent and landing at the current horizontal position.</summary>
        void CommandLand();

        /// <summary>
        /// Immediate stop: command zero horizontal velocity and hold altitude. Used by the
        /// reflexive collision-avoidance path, which must not wait for the planner.
        /// </summary>
        void CommandEmergencyBrake();

        /// <summary>Manual stick input, all axes in [-1,1] except throttle in [0,1].</summary>
        void SetManualInput(float roll, float pitch, float yaw, float throttle);

        /// <summary>Switches between manual and autonomous command sources.</summary>
        void SetControlSource(ControlSource source);

        ControlSource CurrentControlSource { get; }
    }

    /// <summary>Who is currently commanding the aircraft.</summary>
    public enum ControlSource
    {
        /// <summary>Nothing commanding; motors idle or disarmed.</summary>
        None = 0,

        /// <summary>Operator sticks.</summary>
        Manual = 1,

        /// <summary>Autonomous navigation stack.</summary>
        Autonomous = 2,

        /// <summary>A failsafe behaviour has taken control away from both of the above.</summary>
        Failsafe = 3
    }

    /// <summary>
    /// The flight state machine states required by the ASTRA specification.
    ///
    /// Ordering is meaningful only in so far as it roughly follows a nominal mission; the machine
    /// itself defines legal transitions explicitly rather than relying on enum order.
    /// </summary>
    public enum FlightState
    {
        Disarmed = 0,
        Initialising = 1,
        Preflight = 2,
        Armed = 3,
        Takeoff = 4,
        Hover = 5,
        Navigating = 6,
        ObstacleDetected = 7,
        Avoiding = 8,
        RejoiningRoute = 9,
        TargetApproach = 10,
        TargetReached = 11,
        ReturnHome = 12,
        Landing = 13,
        MissionComplete = 14,
        MissionAborted = 15,
        Emergency = 16
    }

    /// <summary>Helpers for presenting and reasoning about FlightState.</summary>
    public static class FlightStateInfo
    {
        /// <summary>Uppercase display name with underscores, as the GCS shows it.</summary>
        public static string ToDisplayName(FlightState state)
        {
            switch (state)
            {
                case FlightState.Disarmed: return "DISARMED";
                case FlightState.Initialising: return "INITIALIZING";
                case FlightState.Preflight: return "PREFLIGHT";
                case FlightState.Armed: return "ARMED";
                case FlightState.Takeoff: return "TAKEOFF";
                case FlightState.Hover: return "HOVER";
                case FlightState.Navigating: return "NAVIGATING";
                case FlightState.ObstacleDetected: return "OBSTACLE_DETECTED";
                case FlightState.Avoiding: return "AVOIDING";
                case FlightState.RejoiningRoute: return "REJOINING_ROUTE";
                case FlightState.TargetApproach: return "TARGET_APPROACH";
                case FlightState.TargetReached: return "TARGET_REACHED";
                case FlightState.ReturnHome: return "RETURN_HOME";
                case FlightState.Landing: return "LANDING";
                case FlightState.MissionComplete: return "MISSION_COMPLETE";
                case FlightState.MissionAborted: return "MISSION_ABORTED";
                case FlightState.Emergency: return "EMERGENCY";
                default: return state.ToString().ToUpperInvariant();
            }
        }

        /// <summary>True if the motors should be spinning in this state.</summary>
        public static bool IsAirborne(FlightState state)
        {
            switch (state)
            {
                case FlightState.Takeoff:
                case FlightState.Hover:
                case FlightState.Navigating:
                case FlightState.ObstacleDetected:
                case FlightState.Avoiding:
                case FlightState.RejoiningRoute:
                case FlightState.TargetApproach:
                case FlightState.TargetReached:
                case FlightState.ReturnHome:
                case FlightState.Landing:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True if the autonomous navigation stack should be running.</summary>
        public static bool IsAutonomousFlight(FlightState state)
        {
            switch (state)
            {
                case FlightState.Navigating:
                case FlightState.ObstacleDetected:
                case FlightState.Avoiding:
                case FlightState.RejoiningRoute:
                case FlightState.TargetApproach:
                case FlightState.ReturnHome:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True for terminal states that end a mission.</summary>
        public static bool IsTerminal(FlightState state)
        {
            return state == FlightState.MissionComplete
                   || state == FlightState.MissionAborted;
        }

        /// <summary>Colour used for the state chip in the GCS top bar.</summary>
        public static Color ToColour(FlightState state)
        {
            switch (state)
            {
                case FlightState.Disarmed:
                case FlightState.Initialising:
                    return new Color(0.55f, 0.58f, 0.62f);
                case FlightState.Preflight:
                case FlightState.Armed:
                    return new Color(0.35f, 0.70f, 0.95f);
                case FlightState.ObstacleDetected:
                case FlightState.Avoiding:
                case FlightState.RejoiningRoute:
                    return new Color(0.98f, 0.65f, 0.20f);
                case FlightState.MissionAborted:
                case FlightState.Emergency:
                    return new Color(0.92f, 0.30f, 0.28f);
                case FlightState.TargetReached:
                case FlightState.MissionComplete:
                    return new Color(0.30f, 0.85f, 0.45f);
                default:
                    return new Color(0.25f, 0.80f, 0.70f);
            }
        }
    }
}
