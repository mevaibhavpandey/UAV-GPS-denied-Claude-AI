using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;

namespace Astra.Perception
{
    /// <summary>
    /// Analyzes the overall threat level across all tracked obstacles and predicts most critical collision risks.
    /// </summary>
    public class ThreatAnalyzer
    {
        private readonly ICollisionPredictor _predictor;

        public ThreatAnalyzer(ICollisionPredictor predictor)
        {
            _predictor = predictor ?? new CollisionPredictor();
        }

        public (ThreatLevel overallThreat, ObstacleReading mostThreatening, CollisionPrediction worstPrediction)
            Evaluate(IReadOnlyList<ObstacleReading> obstacles, Vector3 vehiclePosition, Vector3 vehicleVelocity, float vehicleRadiusM)
        {
            ThreatLevel highestThreat = ThreatLevel.None;
            ObstacleReading mostThreatening = default;
            CollisionPrediction worstPrediction = CollisionPrediction.NoRisk;
            float lowestTtc = float.PositiveInfinity;

            if (obstacles == null || obstacles.Count == 0)
            {
                return (ThreatLevel.None, mostThreatening, worstPrediction);
            }

            for (int i = 0; i < obstacles.Count; i++)
            {
                ObstacleReading obs = obstacles[i];
                CollisionPrediction pred = _predictor.Predict(obs, vehiclePosition, vehicleVelocity, vehicleRadiusM);

                if (pred.Threat > highestThreat || (pred.Threat == highestThreat && pred.TimeToCollisionS < lowestTtc))
                {
                    highestThreat = pred.Threat;
                    mostThreatening = obs;
                    worstPrediction = pred;
                    lowestTtc = pred.TimeToCollisionS;
                }
            }

            return (highestThreat, mostThreatening, worstPrediction);
        }
    }
}
