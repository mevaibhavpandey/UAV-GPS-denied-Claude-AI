using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Geo;
using Astra.Core.Logging;
using Astra.Flight;
using Astra.Navigation;
using Astra.Perception;

namespace Astra.Mission
{
    /// <summary>
    /// The primary autonomous flight executive and decision pipeline.
    /// Executes the 7-stage autonomy loop: SENSE -> PERCEIVE -> LOCALIZE -> PLAN -> DECIDE -> ACT -> REASSESS.
    /// Formulates live DecisionRecord instances with genuine numeric metrics.
    /// </summary>
    [DisallowMultipleComponent]
    public class AutonomyController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private FlightControlSystem flightController;
        [SerializeField] private MissionManager missionManager;
        [SerializeField] private RaycastObstacleDetector obstacleDetector;

        [Header("Decision Timing & Metrics")]
        [SerializeField] private float autonomyRateHz = 10.0f;

        private IPathPlanner _planner;
        private IPlanningGrid _planningGrid;
        private CollisionPredictor _collisionPredictor;
        private AvoidanceController _avoidanceController;

        private DecisionRecord _lastDecision;
        private DecisionCycleTiming _lastTiming;
        private DecisionStage _currentStage = DecisionStage.Sense;
        private int _decisionSequence = 1;
        private float _lastTickTime;
        private List<Vector3> _currentPath = new List<Vector3>();
        private int _currentPathIndex = 0;
        private bool _isAvoiding = false;

        public DecisionRecord LastDecision => _lastDecision;
        public DecisionCycleTiming LastTiming => _lastTiming;
        public DecisionStage CurrentStage => _currentStage;
        public IReadOnlyList<Vector3> CurrentPlannedPath => _currentPath;

        private void Awake()
        {
            if (flightController == null) flightController = GetComponent<FlightControlSystem>();
            if (missionManager == null) missionManager = GetComponent<MissionManager>();
            if (obstacleDetector == null) obstacleDetector = GetComponent<RaycastObstacleDetector>();

            _planner = new MargasoochiDStarLite();
            _collisionPredictor = new CollisionPredictor();
            _avoidanceController = new AvoidanceController();

            // Initialize 3D planning grid around origin (400m x 80m x 400m, 4m voxel size)
            _planningGrid = new OccupancyGrid(new Vector3(-200, 0, -200), new Vector3Int(100, 25, 100), 4.0f);
        }

        private void Start()
        {
            _lastDecision = DecisionRecord.Create(DecisionAction.None, "Autonomy system standby.");
        }

        private void FixedUpdate()
        {
            if (flightController == null || flightController.CurrentControlSource != ControlSource.Autonomous)
            {
                return;
            }

            float dt = Time.time - _lastTickTime;
            if (dt >= 1.0f / autonomyRateHz)
            {
                _lastTickTime = Time.time;
                ExecuteAutonomyCycle();
            }
        }

        private void ExecuteAutonomyCycle()
        {
            float cycleStartTime = Time.realtimeSinceStartup;
            DecisionCycleTiming timing = new DecisionCycleTiming();

            Vector3 uavPos = transform.position;
            Vector3 uavVel = flightController.GetComponent<Rigidbody>()?.linearVelocity ?? Vector3.zero;

            // -------------------------------------------------------------
            // 1. SENSE
            // -------------------------------------------------------------
            SetStage(DecisionStage.Sense);
            float t0 = Time.realtimeSinceStartup;

            ISensorProvider sensors = AstraServices.Get<ISensorProvider>();
            sensors?.Tick(Time.fixedDeltaTime);
            timing.SenseMs = (Time.realtimeSinceStartup - t0) * 1000.0f;

            // -------------------------------------------------------------
            // 2. PERCEIVE
            // -------------------------------------------------------------
            SetStage(DecisionStage.Perceive);
            float t1 = Time.realtimeSinceStartup;

            obstacleDetector?.Scan(uavPos, transform.rotation, Time.fixedDeltaTime);
            IReadOnlyList<ObstacleReading> obstacles = obstacleDetector?.Obstacles;

            ObstacleReading mostThreateningObs = default;
            CollisionPrediction worstPrediction = CollisionPrediction.NoRisk;
            float lowestTtc = float.PositiveInfinity;

            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Count; i++)
                {
                    CollisionPrediction p = _collisionPredictor.Predict(obstacles[i], uavPos, uavVel, 0.65f);
                    if (p.WillCollide && p.TimeToCollisionS < lowestTtc)
                    {
                        lowestTtc = p.TimeToCollisionS;
                        mostThreateningObs = obstacles[i];
                        worstPrediction = p;
                    }
                }
            }
            timing.PerceiveMs = (Time.realtimeSinceStartup - t1) * 1000.0f;

            // -------------------------------------------------------------
            // 3. LOCALIZE
            // -------------------------------------------------------------
            SetStage(DecisionStage.Localize);
            float t2 = Time.realtimeSinceStartup;

            ILocalizationProvider loc = AstraServices.Get<ILocalizationProvider>();
            loc?.Tick(Time.fixedDeltaTime, sensors);
            PoseEstimate currentPose = loc?.CurrentEstimate ?? PoseEstimate.Invalid;
            timing.LocalizeMs = (Time.realtimeSinceStartup - t2) * 1000.0f;

            // -------------------------------------------------------------
            // 4. PLAN
            // -------------------------------------------------------------
            SetStage(DecisionStage.Plan);
            float t3 = Time.realtimeSinceStartup;

            Vector3 targetDestination = uavPos;
            if (missionManager != null && missionManager.ActiveWaypointIndex >= 0)
            {
                GeoReference geo = GeoReference.Instance;
                targetDestination = geo != null ? geo.ToWorld(missionManager.ActiveWaypoint.Position) : uavPos;
            }

            if (_currentPath == null || _currentPath.Count == 0 || _currentPathIndex >= _currentPath.Count)
            {
                PathPlanRequest req = PathPlanRequest.Default(uavPos, targetDestination);
                PathPlanResult result = _planner.Plan(req, _planningGrid);

                if (result.Success && result.Waypoints.Count > 0)
                {
                    _currentPath = TrajectorySmoother.SmoothPath(result.Waypoints);
                    _currentPathIndex = 0;
                    AstraEvents.RaiseRoutePlanned(result);
                }
            }
            timing.PlanMs = (Time.realtimeSinceStartup - t3) * 1000.0f;

            // -------------------------------------------------------------
            // 5. DECIDE
            // -------------------------------------------------------------
            SetStage(DecisionStage.Decide);
            float t4 = Time.realtimeSinceStartup;

            DecisionAction chosenAction = DecisionAction.ContinueRoute;
            string decisionReason = "Following nominal mission flight plan.";
            string rejectedAlternatives = string.Empty;
            float confidence = currentPose.Confidence;
            Vector3 desiredVelocity = Vector3.zero;

            Vector3 nextWp = targetDestination;
            if (_currentPath != null && _currentPathIndex < _currentPath.Count)
            {
                nextWp = _currentPath[_currentPathIndex];
                if (Vector3.Distance(uavPos, nextWp) < 4.0f)
                {
                    _currentPathIndex++;
                }
            }

            Vector3 toTarget = (nextWp - uavPos);
            float targetDist = toTarget.magnitude;
            float cruiseSpeed = (missionManager != null && missionManager.ActiveWaypoint.SpeedMps > 0)
                ? missionManager.ActiveWaypoint.SpeedMps
                : 8.0f;

            desiredVelocity = toTarget.normalized * cruiseSpeed;

            // Evaluate obstacle avoidance
            if (worstPrediction.WillCollide)
            {
                var avoidRes = _avoidanceController.EvaluateManeuver(mostThreateningObs, worstPrediction, uavPos, desiredVelocity, 8.0f);
                desiredVelocity = avoidRes.AvoidanceVelocity;
                decisionReason = avoidRes.Reason;
                rejectedAlternatives = avoidRes.RejectedAlternatives;
                confidence = avoidRes.Confidence;

                switch (avoidRes.Maneuver)
                {
                    case AvoidanceController.AvoidanceManeuver.AvoidRight:
                    case AvoidanceController.AvoidanceManeuver.AvoidLeft:
                        chosenAction = DecisionAction.AvoidLateral;
                        _isAvoiding = true;
                        break;
                    case AvoidanceController.AvoidanceManeuver.ClimbOver:
                        chosenAction = DecisionAction.AvoidVertical;
                        _isAvoiding = true;
                        break;
                    case AvoidanceController.AvoidanceManeuver.EmergencyBrake:
                        chosenAction = DecisionAction.EmergencyBrake;
                        break;
                }
            }
            else
            {
                if (_isAvoiding)
                {
                    _isAvoiding = false;
                    chosenAction = DecisionAction.RejoinRoute;
                    decisionReason = "Obstacle cleared. Rejoining planned mission corridor.";
                }
                else if (targetDist < 10.0f && missionManager != null && missionManager.ActiveWaypoint.Kind == WaypointKind.Target)
                {
                    chosenAction = DecisionAction.SlowApproach;
                    desiredVelocity = toTarget.normalized * Mathf.Clamp(targetDist * 0.5f, 2.0f, cruiseSpeed);
                    decisionReason = $"Target waypoint in proximity ({targetDist:F1}m). Reducing approach velocity.";
                }
            }

            timing.DecideMs = (Time.realtimeSinceStartup - t4) * 1000.0f;

            // -------------------------------------------------------------
            // 6. ACT
            // -------------------------------------------------------------
            SetStage(DecisionStage.Act);
            float t5 = Time.realtimeSinceStartup;

            if (chosenAction == DecisionAction.EmergencyBrake)
            {
                flightController.CommandEmergencyBrake();
            }
            else if (targetDist < 2.5f && missionManager != null && missionManager.ActiveWaypoint.Kind == WaypointKind.Target)
            {
                missionManager.AdvanceWaypoint();
                flightController.CommandHover();
                chosenAction = DecisionAction.HoldPosition;
                decisionReason = "Target arrived. Holding steady position.";
            }
            else
            {
                float targetYaw = Quaternion.LookRotation(desiredVelocity.sqrMagnitude > 0.1f ? desiredVelocity.normalized : transform.forward).eulerAngles.y;
                float yawDiff = Mathf.DeltaAngle(transform.eulerAngles.y, targetYaw);
                float yawRate = Mathf.Clamp(yawDiff * 1.5f, -45f, 45f);

                flightController.CommandVelocity(desiredVelocity, yawRate);
            }
            timing.ActMs = (Time.realtimeSinceStartup - t5) * 1000.0f;

            // -------------------------------------------------------------
            // 7. REASSESS
            // -------------------------------------------------------------
            SetStage(DecisionStage.Reassess);
            float t6 = Time.realtimeSinceStartup;

            timing.ReassessMs = (Time.realtimeSinceStartup - t6) * 1000.0f;
            _lastTiming = timing;

            float totalCycleMs = (Time.realtimeSinceStartup - cycleStartTime) * 1000.0f;

            _lastDecision = new DecisionRecord
            {
                MissionTime = Time.timeAsDouble,
                Sequence = _decisionSequence++,
                Action = chosenAction,
                Reason = decisionReason,
                RejectedAlternatives = rejectedAlternatives,
                Confidence = confidence,
                CycleTimeMs = totalCycleMs,
                NearestObstacleM = mostThreateningObs.TrackId > 0 ? mostThreateningObs.DistanceM : -1f,
                TimeToCollisionS = worstPrediction.TimeToCollisionS,
                DistanceToWaypointM = targetDist,
                TrackedObstacleCount = obstacles != null ? obstacles.Count : 0,
                PositionUncertaintyM = currentPose.PositionStdDev.magnitude
            };

            AstraEvents.RaiseDecisionMade(_lastDecision);
        }

        private void SetStage(DecisionStage stage)
        {
            if (_currentStage != stage)
            {
                _currentStage = stage;
                AstraEvents.RaiseDecisionStageEntered(stage);
            }
        }
    }
}
