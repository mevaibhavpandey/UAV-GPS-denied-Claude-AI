using System.Collections.Generic;
using UnityEngine;

namespace Astra.Contracts
{
    /// <summary>
    /// The stages of the autonomy loop, in the order they execute each decision cycle.
    ///
    /// This ordering is not arbitrary presentation dressing - it is the actual execution order in
    /// AutonomyController, and the UI reads the live stage from there. If the code were reordered the
    /// display would change with it, which is the only way a pipeline diagram stays truthful.
    ///
    /// The stages map onto the classic sense-plan-act decomposition, split finely enough that the
    /// operator can see where time is being spent and where a decision came from.
    /// </summary>
    public enum DecisionStage
    {
        /// <summary>Raw sensor acquisition: IMU, barometer, magnetometer, GPS if available, and a
        /// sensor sweep. No interpretation.</summary>
        Sense = 0,

        /// <summary>Turning raw returns into tracked obstacles with position, extent and velocity,
        /// and predicting closest approach.</summary>
        Perceive = 1,

        /// <summary>Estimating where the vehicle is. GPS-aided when a fix exists, dead reckoning when
        /// it does not.</summary>
        Localize = 2,

        /// <summary>Producing or repairing a route to the goal over the occupancy grid.</summary>
        Plan = 3,

        /// <summary>Choosing what to do now: continue, avoid, hold, climb, abort. This is the stage
        /// that produces a DecisionRecord with a stated reason.</summary>
        Decide = 4,

        /// <summary>Issuing commands to the flight controller.</summary>
        Act = 5,

        /// <summary>Checking whether the last action achieved what it was supposed to, and whether
        /// the situation has changed enough to warrant replanning.</summary>
        Reassess = 6
    }

    public static class DecisionStageInfo
    {
        public static string ToDisplayName(DecisionStage stage)
        {
            switch (stage)
            {
                case DecisionStage.Sense:    return "SENSE";
                case DecisionStage.Perceive: return "PERCEIVE";
                case DecisionStage.Localize: return "LOCALIZE";
                case DecisionStage.Plan:     return "PLAN";
                case DecisionStage.Decide:   return "DECIDE";
                case DecisionStage.Act:      return "ACT";
                case DecisionStage.Reassess: return "REASSESS";
                default:                     return "UNKNOWN";
            }
        }

        /// <summary>One-line explanation shown as a tooltip when an evaluator hovers a stage.</summary>
        public static string ToExplanation(DecisionStage stage)
        {
            switch (stage)
            {
                case DecisionStage.Sense:
                    return "Read sensors. IMU at 100 Hz, barometer at 25 Hz, magnetometer at 50 Hz, " +
                           "GPS at 5 Hz when available, obstacle sweep at 10 Hz.";
                case DecisionStage.Perceive:
                    return "Cluster sensor returns into tracked obstacles, estimate their velocity " +
                           "from successive observations, predict closest point of approach.";
                case DecisionStage.Localize:
                    return "Fuse available measurements into a position and heading estimate, with " +
                           "an explicit uncertainty. Falls back to dead reckoning without GPS.";
                case DecisionStage.Plan:
                    return "Search the occupancy grid for a safe route. Repairs the existing plan " +
                           "incrementally when the map changes, rather than searching from scratch.";
                case DecisionStage.Decide:
                    return "Select the current behaviour from the situation. Every selection records " +
                           "the reason that drove it.";
                case DecisionStage.Act:
                    return "Convert the chosen behaviour into velocity, heading and altitude " +
                           "commands for the flight controller.";
                case DecisionStage.Reassess:
                    return "Verify the action had the intended effect and decide whether conditions " +
                           "have changed enough to require replanning.";
                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>
    /// What the autonomy layer decided to do. Kept separate from FlightState: FlightState describes
    /// what the aircraft IS doing, this describes what the autonomy layer CHOSE. They usually agree,
    /// and the cases where they briefly disagree - a decision issued but not yet reflected in the
    /// state machine - are exactly the cases worth being able to see.
    /// </summary>
    public enum DecisionAction
    {
        /// <summary>No autonomous decision active; manual control or disarmed.</summary>
        None = 0,

        /// <summary>Proceed along the current route.</summary>
        ContinueRoute = 1,

        /// <summary>Deviate laterally around a detected obstacle.</summary>
        AvoidLateral = 2,

        /// <summary>Climb over an obstacle. Costlier than lateral avoidance in battery terms, so the
        /// planner prefers lateral where lateral is available.</summary>
        AvoidVertical = 3,

        /// <summary>Stop and hold position while the situation is assessed.</summary>
        HoldPosition = 4,

        /// <summary>Return to the planned route after a deviation.</summary>
        RejoinRoute = 5,

        /// <summary>Slow the approach because the target is close.</summary>
        SlowApproach = 6,

        /// <summary>Return to the launch point.</summary>
        ReturnHome = 7,

        /// <summary>Land at the current position.</summary>
        LandHere = 8,

        /// <summary>Emergency stop: kill horizontal velocity immediately.</summary>
        EmergencyBrake = 9,

        /// <summary>Abandon the mission.</summary>
        AbortMission = 10,

        /// <summary>Replan from the current position because the route is no longer valid.</summary>
        Replan = 11
    }

    /// <summary>
    /// A single autonomous decision with its justification and the evidence behind it.
    ///
    /// The specification asks for a panel showing what the system is thinking. The risk with that
    /// kind of panel is that it becomes a scripted narration - text chosen to sound intelligent,
    /// written by hand, disconnected from the code that actually decides. An evaluator who asks
    /// "where does that sentence come from?" then gets an uncomfortable answer.
    ///
    /// So this record is produced BY the decision logic, at the point of decision, carrying the
    /// actual numbers that drove it. The panel formats the record; it does not invent it. If the
    /// panel says the aircraft is avoiding an obstacle 23.4 m ahead with 1.9 s to collision, those
    /// figures came out of the collision predictor on that tick.
    /// </summary>
    public struct DecisionRecord
    {
        /// <summary>Mission time of the decision, seconds.</summary>
        public double MissionTime;

        /// <summary>Monotonic sequence number.</summary>
        public int Sequence;

        public DecisionAction Action;

        /// <summary>
        /// Short reason, e.g. "Obstacle at 23.4 m, TTC 1.9 s, lateral clearance available to port".
        /// Composed from measured values by the deciding code.
        /// </summary>
        public string Reason;

        /// <summary>
        /// Alternatives considered and why they were not chosen, when the decision involved a
        /// genuine choice. Empty when there was only one option. Populating this honestly means
        /// leaving it empty for trivial decisions rather than manufacturing deliberation.
        /// </summary>
        public string RejectedAlternatives;

        /// <summary>Confidence in [0,1]. A heuristic, not a calibrated posterior - the UI labels it
        /// as a confidence index for that reason.</summary>
        public float Confidence;

        /// <summary>Milliseconds spent on the decision cycle that produced this record.</summary>
        public float CycleTimeMs;

        // ---- Evidence: the measured quantities that drove the decision ----

        /// <summary>Distance to the most threatening obstacle, metres. Negative if none tracked.</summary>
        public float NearestObstacleM;

        /// <summary>Predicted seconds to collision with it. Infinity if no collision predicted.</summary>
        public float TimeToCollisionS;

        /// <summary>Distance remaining to the active waypoint, metres.</summary>
        public float DistanceToWaypointM;

        /// <summary>Number of obstacles tracked at the time of the decision.</summary>
        public int TrackedObstacleCount;

        /// <summary>Position uncertainty from the localisation provider, metres.</summary>
        public float PositionUncertaintyM;

        public static DecisionRecord Create(DecisionAction action, string reason)
        {
            DecisionRecord r = new DecisionRecord();
            r.Action = action;
            r.Reason = reason;
            r.RejectedAlternatives = string.Empty;
            r.Confidence = 1f;
            r.NearestObstacleM = -1f;
            r.TimeToCollisionS = float.PositiveInfinity;
            r.DistanceToWaypointM = -1f;
            return r;
        }

        public string ToDisplayString()
        {
            return ToActionLabel(Action) + ": " + Reason;
        }

        public static string ToActionLabel(DecisionAction action)
        {
            switch (action)
            {
                case DecisionAction.None:           return "IDLE";
                case DecisionAction.ContinueRoute:  return "CONTINUE ROUTE";
                case DecisionAction.AvoidLateral:   return "AVOID (LATERAL)";
                case DecisionAction.AvoidVertical:  return "AVOID (CLIMB)";
                case DecisionAction.HoldPosition:   return "HOLD POSITION";
                case DecisionAction.RejoinRoute:    return "REJOIN ROUTE";
                case DecisionAction.SlowApproach:   return "SLOW APPROACH";
                case DecisionAction.ReturnHome:     return "RETURN TO LAUNCH";
                case DecisionAction.LandHere:       return "LAND";
                case DecisionAction.EmergencyBrake: return "EMERGENCY BRAKE";
                case DecisionAction.AbortMission:   return "ABORT MISSION";
                case DecisionAction.Replan:         return "REPLAN";
                default:                            return "UNKNOWN";
            }
        }

        /// <summary>
        /// Colour for the decision banner. Avoidance and emergency actions read as attention states;
        /// routine progress reads as calm. Deliberately restrained - a panel that flashes red at
        /// every routine event trains the operator to ignore it.
        /// </summary>
        public static Color ToColour(DecisionAction action)
        {
            switch (action)
            {
                case DecisionAction.ContinueRoute:
                case DecisionAction.RejoinRoute:
                    return new Color(0.30f, 0.78f, 0.55f);   // green: nominal progress
                case DecisionAction.SlowApproach:
                case DecisionAction.HoldPosition:
                case DecisionAction.Replan:
                    return new Color(0.35f, 0.68f, 0.92f);   // blue: deliberate, not alarming
                case DecisionAction.AvoidLateral:
                case DecisionAction.AvoidVertical:
                    return new Color(0.98f, 0.72f, 0.20f);   // amber: actively manoeuvring
                case DecisionAction.ReturnHome:
                case DecisionAction.LandHere:
                    return new Color(0.75f, 0.80f, 0.30f);
                case DecisionAction.EmergencyBrake:
                case DecisionAction.AbortMission:
                    return new Color(0.92f, 0.28f, 0.24f);   // red: reserved for genuine emergencies
                default:
                    return new Color(0.62f, 0.65f, 0.70f);
            }
        }
    }

    /// <summary>
    /// Timing for one pass of the decision pipeline, per stage. Displayed as a live bar chart in the
    /// diagnostics panel.
    ///
    /// This exists because "is the autonomy loop keeping up?" is a real engineering question with a
    /// measurable answer, and because it is the honest way to present algorithm performance: if
    /// planning takes 40 ms out of a 100 ms budget, that is visible rather than asserted.
    /// </summary>
    public struct DecisionCycleTiming
    {
        public float SenseMs;
        public float PerceiveMs;
        public float LocalizeMs;
        public float PlanMs;
        public float DecideMs;
        public float ActMs;
        public float ReassessMs;

        public float TotalMs
        {
            get
            {
                return SenseMs + PerceiveMs + LocalizeMs + PlanMs + DecideMs + ActMs + ReassessMs;
            }
        }

        public float Get(DecisionStage stage)
        {
            switch (stage)
            {
                case DecisionStage.Sense:    return SenseMs;
                case DecisionStage.Perceive: return PerceiveMs;
                case DecisionStage.Localize: return LocalizeMs;
                case DecisionStage.Plan:     return PlanMs;
                case DecisionStage.Decide:   return DecideMs;
                case DecisionStage.Act:      return ActMs;
                case DecisionStage.Reassess: return ReassessMs;
                default:                     return 0f;
            }
        }

        public void Set(DecisionStage stage, float ms)
        {
            switch (stage)
            {
                case DecisionStage.Sense:    SenseMs = ms;    break;
                case DecisionStage.Perceive: PerceiveMs = ms; break;
                case DecisionStage.Localize: LocalizeMs = ms; break;
                case DecisionStage.Plan:     PlanMs = ms;     break;
                case DecisionStage.Decide:   DecideMs = ms;   break;
                case DecisionStage.Act:      ActMs = ms;      break;
                case DecisionStage.Reassess: ReassessMs = ms; break;
            }
        }

        /// <summary>
        /// Exponentially smoothed update, so the display shows a stable trend rather than
        /// single-frame noise. Alpha near 0.1 gives a readable but responsive average.
        /// </summary>
        public void Blend(DecisionCycleTiming sample, float alpha)
        {
            SenseMs    = Mathf.Lerp(SenseMs,    sample.SenseMs,    alpha);
            PerceiveMs = Mathf.Lerp(PerceiveMs, sample.PerceiveMs, alpha);
            LocalizeMs = Mathf.Lerp(LocalizeMs, sample.LocalizeMs, alpha);
            PlanMs     = Mathf.Lerp(PlanMs,     sample.PlanMs,     alpha);
            DecideMs   = Mathf.Lerp(DecideMs,   sample.DecideMs,   alpha);
            ActMs      = Mathf.Lerp(ActMs,      sample.ActMs,      alpha);
            ReassessMs = Mathf.Lerp(ReassessMs, sample.ReassessMs, alpha);
        }
    }
}
