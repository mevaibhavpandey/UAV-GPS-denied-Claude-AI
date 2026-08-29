using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Logging;

namespace Astra.Perception
{
    /// <summary>
    /// Coordinates sensor scanning, collision prediction, and threat assessment.
    /// Manages perception event dispatches across the autonomy system.
    /// </summary>
    [DisallowMultipleComponent]
    public class PerceptionManager : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private RaycastObstacleDetector detector;
        [SerializeField] private Rigidbody droneRigidbody;

        private CollisionPredictor _predictor;
        private ThreatAnalyzer _threatAnalyzer;
        private ThreatLevel _currentThreat = ThreatLevel.None;

        public ThreatLevel CurrentThreat => _currentThreat;

        private void Awake()
        {
            if (detector == null) detector = GetComponent<RaycastObstacleDetector>();
            if (droneRigidbody == null) droneRigidbody = GetComponent<Rigidbody>();
            _predictor = new CollisionPredictor();
            _threatAnalyzer = new ThreatAnalyzer(_predictor);
        }

        private void FixedUpdate()
        {
            if (detector == null) return;

            Vector3 pos = droneRigidbody != null ? droneRigidbody.position : transform.position;
            Quaternion rot = droneRigidbody != null ? droneRigidbody.rotation : transform.rotation;
            Vector3 vel = droneRigidbody != null ? droneRigidbody.linearVelocity : Vector3.zero;

            detector.Scan(pos, rot, Time.fixedDeltaTime);

            var eval = _threatAnalyzer.Evaluate(detector.Obstacles, pos, vel, 0.65f);

            if (eval.overallThreat != _currentThreat)
            {
                ThreatLevel prev = _currentThreat;
                _currentThreat = eval.overallThreat;
                AstraEvents.RaiseThreatLevelChanged(prev, _currentThreat);

                if (_currentThreat >= ThreatLevel.Medium)
                {
                    EventLog.Warn(LogSource.Perception, $"Threat Level {_currentThreat}: Obstacle {eval.mostThreatening.TrackId} distance {eval.mostThreatening.DistanceM:F1}m, TTC {eval.worstPrediction.TimeToCollisionS:F1}s");
                }
            }

            if (eval.worstPrediction.WillCollide)
            {
                AstraEvents.RaiseCollisionPredicted(eval.mostThreatening, eval.worstPrediction);
            }
        }
    }
}
