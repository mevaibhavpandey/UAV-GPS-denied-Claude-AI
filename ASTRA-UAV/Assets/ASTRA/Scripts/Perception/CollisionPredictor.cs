using System;
using UnityEngine;
using Astra.Contracts;

namespace Astra.Perception
{
    /// <summary>
    /// Evaluates Closest Point of Approach (CPA) and Time to Collision (TTC) for moving and static obstacles.
    /// Implements ICollisionPredictor.
    /// </summary>
    public class CollisionPredictor : ICollisionPredictor
    {
        private const float DefaultSafetyMarginM = 8.0f;

        public CollisionPrediction Predict(ObstacleReading obstacle, Vector3 vehiclePosition, Vector3 vehicleVelocity, float vehicleRadiusM)
        {
            Vector3 relPos = obstacle.ClosestPoint - vehiclePosition;
            Vector3 relVel = obstacle.Velocity - vehicleVelocity;

            float combinedRadius = vehicleRadiusM + DefaultSafetyMarginM;
            float currentDist = relPos.magnitude;

            // Static or stationary relative velocity
            float relSpeedSqr = relVel.sqrMagnitude;
            if (relSpeedSqr < 0.01f)
            {
                // Not closing in
                if (currentDist < combinedRadius)
                {
                    return new CollisionPrediction
                    {
                        WillCollide = true,
                        TimeToClosestApproachS = 0f,
                        ClosestApproachDistanceM = currentDist,
                        PredictedCollisionPoint = obstacle.ClosestPoint,
                        TimeToCollisionS = 0f,
                        Threat = ThreatLevel.High,
                        RiskIndex = 0.9f
                    };
                }
                return CollisionPrediction.NoRisk;
            }

            // Vector CPA solution: t_cpa = - (r . v) / (v . v)
            float dotRV = Vector3.Dot(relPos, relVel);
            float tCpa = -dotRV / relSpeedSqr;

            if (tCpa < 0f)
            {
                // Closest approach is in the past; bodies are separating
                return CollisionPrediction.NoRisk;
            }

            Vector3 posAtCpa = relPos + relVel * tCpa;
            float distAtCpa = posAtCpa.magnitude;

            bool willCollide = distAtCpa < combinedRadius;
            float ttc = float.PositiveInfinity;
            ThreatLevel threat = ThreatLevel.None;
            float riskIndex = 0f;

            if (willCollide)
            {
                // Time when boundary of safety sphere is penetrated
                float relSpeed = Mathf.Sqrt(relSpeedSqr);
                float penetrationDist = Mathf.Sqrt(Mathf.Max(0f, combinedRadius * combinedRadius - distAtCpa * distAtCpa));
                ttc = Mathf.Max(0f, tCpa - (penetrationDist / relSpeed));

                if (ttc < 2.0f)
                    threat = ThreatLevel.Critical;
                else if (ttc < 5.0f)
                    threat = ThreatLevel.High;
                else if (ttc < 10.0f)
                    threat = ThreatLevel.Medium;
                else
                    threat = ThreatLevel.Low;

                riskIndex = Mathf.Clamp01(1.0f - (ttc / 12.0f));
            }
            else if (distAtCpa < combinedRadius * 1.8f && tCpa < 8.0f)
            {
                threat = ThreatLevel.Low;
                riskIndex = 0.3f;
            }

            return new CollisionPrediction
            {
                WillCollide = willCollide,
                TimeToClosestApproachS = tCpa,
                ClosestApproachDistanceM = distAtCpa,
                PredictedCollisionPoint = vehiclePosition + vehicleVelocity * tCpa,
                TimeToCollisionS = ttc,
                Threat = threat,
                RiskIndex = riskIndex
            };
        }
    }
}
