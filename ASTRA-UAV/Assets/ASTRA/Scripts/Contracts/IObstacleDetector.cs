using System.Collections.Generic;
using UnityEngine;

namespace Astra.Contracts
{
    /// <summary>
    /// Detects obstacles around the vehicle.
    ///
    /// Implementations in ASTRA simulate sensors with Unity raycasts and spherecasts. The interface
    /// is written so that a real depth camera or LiDAR driver could satisfy it: it exposes a field
    /// of view, a maximum range and a list of readings, and it does NOT expose any method that
    /// would require ground-truth knowledge of the scene.
    ///
    /// That restriction is deliberate. It would be trivial in Unity to ask the engine for every
    /// collider within a radius and call that "perception", but the resulting avoidance logic would
    /// be relying on omniscience and would not transfer to real hardware at all. Everything here is
    /// something a physical sensor could actually measure.
    /// </summary>
    public interface IObstacleDetector
    {
        string Name { get; }
        DataProvenance Provenance { get; }
        SubsystemStatus Status { get; }

        /// <summary>Maximum sensing range in metres.</summary>
        float MaxRangeM { get; }

        /// <summary>Horizontal field of view in degrees.</summary>
        float HorizontalFovDeg { get; }

        /// <summary>Vertical field of view in degrees.</summary>
        float VerticalFovDeg { get; }

        /// <summary>
        /// Obstacles currently tracked. The returned list is owned by the detector and is reused
        /// between frames, so callers must not retain or mutate it - copy if you need to keep it.
        /// This avoids a per-frame allocation on the hot path.
        /// </summary>
        IReadOnlyList<ObstacleReading> Obstacles { get; }

        /// <summary>Number of sensor rays or samples cast on the most recent scan.</summary>
        int LastScanSampleCount { get; }

        /// <summary>Milliseconds the most recent scan took. Surfaced in diagnostics.</summary>
        float LastScanDurationMs { get; }

        void Initialise();

        /// <summary>Performs one scan. Called from FixedUpdate for rate stability.</summary>
        void Scan(Vector3 sensorPosition, Quaternion sensorOrientation, float fixedDeltaTime);

        /// <summary>Clears all tracks. Used when a mission restarts.</summary>
        void Reset();
    }

    /// <summary>
    /// Predicts whether tracked obstacles will collide with the vehicle, and when.
    /// </summary>
    public interface ICollisionPredictor
    {
        /// <summary>
        /// Evaluates one obstacle against the vehicle's current motion.
        /// </summary>
        /// <param name="obstacle">The tracked obstacle.</param>
        /// <param name="vehiclePosition">Current vehicle position, world space.</param>
        /// <param name="vehicleVelocity">Current vehicle velocity, world space m/s.</param>
        /// <param name="vehicleRadiusM">Radius of the sphere bounding the airframe, metres.</param>
        /// <returns>The prediction, including time to closest approach.</returns>
        CollisionPrediction Predict(ObstacleReading obstacle, Vector3 vehiclePosition,
                                    Vector3 vehicleVelocity, float vehicleRadiusM);
    }

    /// <summary>
    /// The result of a collision prediction.
    ///
    /// Built around CLOSEST POINT OF APPROACH rather than a naive "will the ray hit it" test,
    /// because for a moving obstacle the relevant question is whether the two bodies' closest
    /// approach over time falls inside the combined safety radius. Solving for the minimum of the
    /// relative-position magnitude gives that directly and handles the crossing-traffic case that a
    /// forward raycast completely misses.
    /// </summary>
    public struct CollisionPrediction
    {
        /// <summary>True if the predicted closest approach breaches the safety radius.</summary>
        public bool WillCollide;

        /// <summary>
        /// Seconds until closest approach. Negative means the closest approach is in the past,
        /// i.e. the obstacle is already receding, which is important to distinguish from "no risk".
        /// </summary>
        public float TimeToClosestApproachS;

        /// <summary>Predicted minimum separation at closest approach, metres.</summary>
        public float ClosestApproachDistanceM;

        /// <summary>Predicted world position of the vehicle at closest approach.</summary>
        public Vector3 PredictedCollisionPoint;

        /// <summary>
        /// Seconds until the safety radius is actually breached, which is earlier than closest
        /// approach. This is the number the operator cares about and the one shown as
        /// "TIME TO COLLISION" in the AI decision panel.
        /// </summary>
        public float TimeToCollisionS;

        /// <summary>Assessed threat level.</summary>
        public ThreatLevel Threat;

        /// <summary>
        /// Collision probability in [0,1]. HONESTY NOTE: this is a heuristic derived from
        /// separation margin and time-to-collision, not a calibrated probability from a stochastic
        /// motion model. It is labelled as a risk index in the UI for that reason.
        /// </summary>
        public float RiskIndex;

        public static CollisionPrediction NoRisk
        {
            get
            {
                CollisionPrediction p = new CollisionPrediction();
                p.WillCollide = false;
                p.TimeToClosestApproachS = float.PositiveInfinity;
                p.ClosestApproachDistanceM = float.PositiveInfinity;
                p.TimeToCollisionS = float.PositiveInfinity;
                p.Threat = ThreatLevel.None;
                p.RiskIndex = 0f;
                return p;
            }
        }
    }
}
