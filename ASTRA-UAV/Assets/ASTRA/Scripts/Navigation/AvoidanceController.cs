using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Logging;

namespace Astra.Navigation
{
    /// <summary>
    /// Evaluates reactive and dynamic obstacle avoidance maneuvers (lateral vs vertical)
    /// and controls trajectory execution while avoiding obstacles.
    /// </summary>
    public class AvoidanceController
    {
        public enum AvoidanceManeuver
        {
            None,
            AvoidRight,
            AvoidLeft,
            ClimbOver,
            EmergencyBrake
        }

        public struct AvoidanceResult
        {
            public AvoidanceManeuver Maneuver;
            public Vector3 AvoidanceVelocity;
            public string Reason;
            public string RejectedAlternatives;
            public float Confidence;
        }

        public AvoidanceResult EvaluateManeuver(
            ObstacleReading obstacle,
            CollisionPrediction prediction,
            Vector3 vehiclePosition,
            Vector3 desiredVelocity,
            float safetyMarginM)
        {
            if (!prediction.WillCollide)
            {
                return new AvoidanceResult
                {
                    Maneuver = AvoidanceManeuver.None,
                    AvoidanceVelocity = desiredVelocity,
                    Reason = "No collision risk predicted.",
                    RejectedAlternatives = string.Empty,
                    Confidence = 1.0f
                };
            }

            // If collision is imminent (< 1.5s), execute emergency brake immediately
            if (prediction.TimeToCollisionS < 1.5f)
            {
                return new AvoidanceResult
                {
                    Maneuver = AvoidanceManeuver.EmergencyBrake,
                    AvoidanceVelocity = Vector3.zero,
                    Reason = $"Critical TTC {prediction.TimeToCollisionS:F1}s! Emergency braking.",
                    RejectedAlternatives = "Maneuvering rejected: insufficient reaction time.",
                    Confidence = 0.99f
                };
            }

            Vector3 fwd = desiredVelocity.sqrMagnitude > 0.1f ? desiredVelocity.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            float cruiseSpeed = Mathf.Max(5.0f, desiredVelocity.magnitude);

            // Candidate 1: Right lateral deviation
            Vector3 rightDir = (fwd + right * 1.2f).normalized;
            bool rightClear = !Physics.SphereCast(vehiclePosition, safetyMarginM, rightDir, out RaycastHit _, 25.0f);

            // Candidate 2: Left lateral deviation
            Vector3 leftDir = (fwd - right * 1.2f).normalized;
            bool leftClear = !Physics.SphereCast(vehiclePosition, safetyMarginM, leftDir, out RaycastHit _, 25.0f);

            // Candidate 3: Climb over (vertical)
            Vector3 upDir = (fwd * 0.5f + Vector3.up * 1.0f).normalized;
            bool upClear = !Physics.SphereCast(vehiclePosition, safetyMarginM, upDir, out RaycastHit _, 20.0f);

            // Favor lateral side with greater clearance to obstacle center
            Vector3 toObs = obstacle.Centre - vehiclePosition;
            float dotRight = Vector3.Dot(toObs, right);

            if (dotRight <= 0 && rightClear) // Obstacle is to the left or center, prefer right
            {
                return new AvoidanceResult
                {
                    Maneuver = AvoidanceManeuver.AvoidRight,
                    AvoidanceVelocity = rightDir * cruiseSpeed,
                    Reason = $"Obstacle {obstacle.TrackId} at {obstacle.DistanceM:F1}m, TTC {prediction.TimeToCollisionS:F1}s. Clearing Starboard.",
                    RejectedAlternatives = leftClear ? "Port avoidance rejected: lower clearance margin." : "Port avoidance blocked.",
                    Confidence = 0.92f
                };
            }
            else if (leftClear)
            {
                return new AvoidanceResult
                {
                    Maneuver = AvoidanceManeuver.AvoidLeft,
                    AvoidanceVelocity = leftDir * cruiseSpeed,
                    Reason = $"Obstacle {obstacle.TrackId} at {obstacle.DistanceM:F1}m, TTC {prediction.TimeToCollisionS:F1}s. Clearing Port.",
                    RejectedAlternatives = rightClear ? "Starboard avoidance rejected: lower clearance margin." : "Starboard avoidance blocked.",
                    Confidence = 0.90f
                };
            }
            else if (upClear)
            {
                return new AvoidanceResult
                {
                    Maneuver = AvoidanceManeuver.ClimbOver,
                    AvoidanceVelocity = upDir * cruiseSpeed,
                    Reason = $"Lateral paths obstructed. Executing vertical climb avoidance over obstacle.",
                    RejectedAlternatives = "Lateral deviations rejected: physical obstructions.",
                    Confidence = 0.85f
                };
            }

            // Fallback: Brake if all paths obstructed
            return new AvoidanceResult
            {
                Maneuver = AvoidanceManeuver.EmergencyBrake,
                AvoidanceVelocity = Vector3.zero,
                Reason = "All candidate corridors blocked. Holding position to re-evaluate.",
                RejectedAlternatives = "Maneuvers blocked in all directions.",
                Confidence = 0.80f
            };
        }
    }
}
