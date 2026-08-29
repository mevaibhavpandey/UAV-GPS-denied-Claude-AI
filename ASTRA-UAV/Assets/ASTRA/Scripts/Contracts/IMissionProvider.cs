using System;
using System.Collections.Generic;
using UnityEngine;
using Astra.Core.Geo;

namespace Astra.Contracts
{
    /// <summary>
    /// What a waypoint is for. Affects how the vehicle behaves on arrival.
    /// </summary>
    public enum WaypointKind
    {
        /// <summary>Fly through without stopping. Used for route shaping.</summary>
        Transit = 0,

        /// <summary>Stop, hold for the dwell time, then continue.</summary>
        Hold = 1,

        /// <summary>The mission objective. Triggers TARGET_APPROACH then TARGET_REACHED.</summary>
        Target = 2,

        /// <summary>The launch and recovery point.</summary>
        Home = 3,

        /// <summary>Survey point where the camera captures an observation.</summary>
        Observation = 4
    }

    /// <summary>
    /// One waypoint in a mission.
    ///
    /// Stored as a geographic coordinate rather than a Unity position, deliberately. A mission
    /// authored against latitude and longitude survives a change of scene origin, a switch between
    /// the Cesium and offline map providers, and export to a real ground station. A mission stored as
    /// Unity coordinates is meaningless the moment any of those change, and would have to be
    /// re-authored. This is also why the vehicle's target is resolved through GeoReference on demand
    /// rather than cached as a Vector3.
    /// </summary>
    [Serializable]
    public struct Waypoint
    {
        /// <summary>Geographic position. Altitude is metres above the mission's ground datum.</summary>
        public GeoCoordinate Position;

        public WaypointKind Kind;

        /// <summary>
        /// Radius within which the waypoint counts as reached, metres. A transit waypoint with too
        /// small a radius makes the vehicle circle it trying to hit an unreachable precision; too
        /// large and the route is cut short. 3-5 m is sensible for this airframe.
        /// </summary>
        public float AcceptanceRadiusM;

        /// <summary>Seconds to hold on arrival. Ignored unless Kind is Hold or Observation.</summary>
        public float DwellSeconds;

        /// <summary>Cruise speed towards this waypoint, m/s. Zero means use the mission default.</summary>
        public float SpeedMps;

        /// <summary>Operator-facing label, e.g. "WP3 - Block B roof".</summary>
        public string Label;

        public static Waypoint Create(GeoCoordinate position, WaypointKind kind, string label)
        {
            Waypoint w = new Waypoint();
            w.Position = position;
            w.Kind = kind;
            w.AcceptanceRadiusM = kind == WaypointKind.Target ? 2.5f : 4f;
            w.DwellSeconds = kind == WaypointKind.Hold ? 3f : 0f;
            w.SpeedMps = 0f;
            w.Label = label;
            return w;
        }
    }

    /// <summary>
    /// A complete mission: an ordered waypoint list plus the parameters that govern how it is flown.
    /// </summary>
    [Serializable]
    public class MissionDefinition
    {
        public string MissionName = "Untitled Mission";

        /// <summary>Free-text objective, shown on the mission briefing panel.</summary>
        public string Objective = string.Empty;

        /// <summary>Launch point. Also the return-to-launch destination.</summary>
        public GeoCoordinate HomePosition;

        public List<Waypoint> Waypoints = new List<Waypoint>();

        /// <summary>Default cruise speed, m/s, for waypoints that do not specify one.</summary>
        public float DefaultSpeedMps = 8f;

        /// <summary>Cruise altitude above the launch point, metres.</summary>
        public float CruiseAltitudeM = 40f;

        /// <summary>Minimum clearance the planner must keep from obstacles, metres.</summary>
        public float SafetyMarginM = 8f;

        /// <summary>
        /// Return to launch automatically when the battery reaches this fraction. Set from the
        /// remaining energy needed to fly home, not from a fixed number, in a real system. Here it is
        /// a configurable threshold and the mission monitor warns if the reserve looks inadequate for
        /// the distance involved.
        /// </summary>
        [Range(0.1f, 0.6f)]
        public float ReturnHomeBatteryFraction = 0.30f;

        /// <summary>
        /// Land immediately when the battery reaches this fraction, overriding return-to-launch.
        /// Below this there is not enough energy to reach home and a controlled landing where it is
        /// beats an uncontrolled one on the way.
        /// </summary>
        [Range(0.05f, 0.3f)]
        public float EmergencyLandBatteryFraction = 0.15f;

        /// <summary>Whether the mission may simulate a GPS outage. Used by the Phase 3 scenario.</summary>
        public bool SimulateGpsDenial = false;

        /// <summary>Waypoint index at which GPS is lost, if simulating denial.</summary>
        public int GpsDenialAtWaypoint = 2;

        /// <summary>Seconds the outage lasts. Zero means it persists for the rest of the mission.</summary>
        public float GpsDenialDurationS = 0f;

        public int WaypointCount
        {
            get { return Waypoints != null ? Waypoints.Count : 0; }
        }

        /// <summary>
        /// Total route length in metres along the great-circle path between consecutive waypoints,
        /// including the leg out from home. Ignores planner deviations for obstacles, so the flown
        /// distance is always at least this and usually more.
        /// </summary>
        public double StraightLineRouteLengthM()
        {
            if (Waypoints == null || Waypoints.Count == 0)
            {
                return 0.0;
            }

            double total = GeoMath.HaversineDistance(HomePosition, Waypoints[0].Position);
            for (int i = 1; i < Waypoints.Count; i++)
            {
                total += GeoMath.HaversineDistance(Waypoints[i - 1].Position, Waypoints[i].Position);
            }
            return total;
        }

        /// <summary>
        /// Checks the mission for problems that should stop it before takeoff, rather than surfacing
        /// as a failure in the air in front of an audience.
        /// </summary>
        public List<string> Validate(float maxAltitudeAglM, float estimatedRangeM)
        {
            List<string> problems = new List<string>();

            if (Waypoints == null || Waypoints.Count == 0)
            {
                problems.Add("Mission has no waypoints.");
                return problems;
            }

            if (!HomePosition.IsValid)
            {
                problems.Add("Home position is not a valid coordinate.");
            }

            for (int i = 0; i < Waypoints.Count; i++)
            {
                Waypoint w = Waypoints[i];

                if (!w.Position.IsValid)
                {
                    problems.Add("Waypoint " + (i + 1) + " is not a valid coordinate.");
                }

                if (w.AcceptanceRadiusM < 0.5f)
                {
                    problems.Add("Waypoint " + (i + 1) + " has an acceptance radius of " +
                                 w.AcceptanceRadiusM.ToString("F1") + " m. Below about 1 m the " +
                                 "vehicle cannot reliably satisfy it and will orbit the waypoint.");
                }
            }

            if (CruiseAltitudeM > maxAltitudeAglM)
            {
                problems.Add("Cruise altitude " + CruiseAltitudeM.ToString("F0") + " m exceeds the " +
                             "configured ceiling of " + maxAltitudeAglM.ToString("F0") + " m.");
            }

            double routeLength = StraightLineRouteLengthM();
            if (estimatedRangeM > 0f && routeLength > estimatedRangeM * 0.7)
            {
                problems.Add("Route is " + routeLength.ToString("F0") + " m against an estimated " +
                             "range of " + estimatedRangeM.ToString("F0") + " m. This leaves less " +
                             "than a 30% reserve, and the estimate does not account for wind, " +
                             "obstacle deviations or battery ageing.");
            }

            if (EmergencyLandBatteryFraction >= ReturnHomeBatteryFraction)
            {
                problems.Add("The emergency-land threshold is at or above the return-to-launch " +
                             "threshold, so return-to-launch would never trigger.");
            }

            return problems;
        }
    }

    /// <summary>
    /// Where a mission has got to.
    /// </summary>
    public enum MissionPhase
    {
        /// <summary>No mission loaded.</summary>
        None = 0,

        /// <summary>Mission loaded, not yet started.</summary>
        Ready = 1,

        /// <summary>Preflight checks running.</summary>
        Preflight = 2,

        /// <summary>Climbing to cruise altitude.</summary>
        Departing = 3,

        /// <summary>Flying the route.</summary>
        EnRoute = 4,

        /// <summary>Approaching the target waypoint.</summary>
        TargetApproach = 5,

        /// <summary>At the target.</summary>
        OnTarget = 6,

        /// <summary>Returning to launch.</summary>
        Returning = 7,

        /// <summary>Descending to land.</summary>
        Landing = 8,

        /// <summary>Landed successfully with all objectives met.</summary>
        Complete = 9,

        /// <summary>Terminated before completion.</summary>
        Aborted = 10
    }

    /// <summary>
    /// Supplies and tracks the active mission.
    ///
    /// Abstracted behind an interface for the same reason as the sensors: a real deployment would
    /// receive its mission over MAVLink from QGroundControl or Mission Planner, as a sequence of
    /// MISSION_ITEM_INT messages. Keeping the consumer side of that boundary clean now means the
    /// autonomy layer never needs to change when the mission source does.
    /// </summary>
    public interface IMissionProvider
    {
        string Name { get; }
        DataProvenance Provenance { get; }

        /// <summary>The loaded mission, or null.</summary>
        MissionDefinition Current { get; }

        MissionPhase Phase { get; }

        /// <summary>Index of the waypoint being flown to, or -1.</summary>
        int ActiveWaypointIndex { get; }

        /// <summary>The waypoint being flown to. Check ActiveWaypointIndex first.</summary>
        Waypoint ActiveWaypoint { get; }

        /// <summary>Fraction of waypoints reached, in [0,1].</summary>
        float Progress { get; }

        /// <summary>Loads a mission. Fails if a mission is already in progress.</summary>
        bool Load(MissionDefinition mission, out string reason);

        /// <summary>Begins the mission. Fails if preflight checks do not pass.</summary>
        bool Start(out string reason);

        /// <summary>Terminates the mission with a stated reason, which is logged.</summary>
        void Abort(string reason);

        /// <summary>Advances to the next waypoint. Called when the active one is reached.</summary>
        void AdvanceWaypoint();

        /// <summary>Switches the mission to return-to-launch.</summary>
        void CommandReturnHome(string reason);

        /// <summary>Raised on every phase transition.</summary>
        event Action<MissionPhase, MissionPhase> PhaseChanged;

        /// <summary>Raised when a waypoint is reached, carrying its index.</summary>
        event Action<int> WaypointReached;
    }
}
