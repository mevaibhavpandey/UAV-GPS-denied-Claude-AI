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

        // Observation/Hold dwell state: while loitering at an observation waypoint we hover in place
        // and count down its dwell time before advancing. This is what makes the mission profiles
        // (Recon/Surveillance/Survey/Search) behave differently in the air from a straight
        // Point-to-Point run, which has no observation waypoints to dwell at.
        private bool _dwelling = false;
        private float _dwellElapsed = 0f;

        // Tracks which ZoneManager.Version has been baked into the planning grid, so threat/no-go
        // zones (Sec 40-43) are stamped in once per change rather than every planning cycle.
        private int _zoneVersionApplied = -1;

        // Tracks which IStaticObstacleSource.StaticObstacleVersion has been baked into the planning
        // grid. Static buildings (from the procedural map) are stamped once so nominal routes clear
        // them globally; perception still handles everything dynamic or unmodelled.
        private int _staticObstacleVersionApplied = -1;

        [Header("Planning")]
        [Tooltip("Stamp known static obstacles (procedural-map buildings) into the planning grid so " +
                 "global routes avoid them. Providers without a priori geometry (Cesium) contribute " +
                 "nothing here and buildings remain handled reactively by perception.")]
        [SerializeField] private bool ingestStaticObstacles = true;

        [Tooltip("Extra clearance, metres, added around each stamped static obstacle when inflating " +
                 "the planning grid. One voxel of margin by default.")]
        [SerializeField] private float staticObstacleInflationM = 4.0f;

        public DecisionRecord LastDecision => _lastDecision;
        public DecisionCycleTiming LastTiming => _lastTiming;
        public DecisionStage CurrentStage => _currentStage;
        public IReadOnlyList<Vector3> CurrentPlannedPath => _currentPath;

        // ---- Navigation instrumentation (read-only, for the flight/nav diagnostics recorder) ----
        // These expose the velocity the executive DECIDED on and the point it is steering toward, so
        // an external recorder can compare desired-vs-actual velocity and compute cross-track error
        // without the recorder having to re-derive the decision. They are set once per cycle in ACT
        // and never drive control, so they cannot affect flight behaviour (Sec 53).
        private Vector3 _lastDesiredVelocity;
        private Vector3 _lastTargetWaypoint;
        private Vector3 _lastSegmentStart;
        private bool _hasNavInstrumentation;

        /// <summary>World-frame velocity the autonomy executive last commanded, m/s.</summary>
        public Vector3 LastDesiredVelocity => _lastDesiredVelocity;

        /// <summary>The path node the aircraft is currently steering toward, Unity world space.</summary>
        public Vector3 LastTargetWaypoint => _lastTargetWaypoint;

        /// <summary>The node the current tracked segment starts from, Unity world space.</summary>
        public Vector3 LastSegmentStart => _lastSegmentStart;

        /// <summary>True once at least one autonomy cycle has populated the instrumentation.</summary>
        public bool HasNavInstrumentation => _hasNavInstrumentation;

        /// <summary>
        /// Signed perpendicular distance from the current path segment, metres (0 when no segment is
        /// being tracked). This is the classic guidance cross-track error: how far the aircraft has
        /// strayed from the straight line between the previous node and the one it is flying to.
        /// </summary>
        public float CrossTrackErrorM
        {
            get
            {
                if (!_hasNavInstrumentation) return 0f;
                Vector3 seg = _lastTargetWaypoint - _lastSegmentStart;
                seg.y = 0f;
                float segLen = seg.magnitude;
                if (segLen < 0.01f) return 0f;
                Vector3 rel = transform.position - _lastSegmentStart;
                rel.y = 0f;
                // Perpendicular component magnitude via the 2D cross product on the XZ plane.
                Vector3 dir = seg / segLen;
                return (rel.x * dir.z - rel.z * dir.x);
            }
        }

        private void Awake()
        {
            if (flightController == null) flightController = GetComponent<FlightControlSystem>();
            if (missionManager == null) missionManager = GetComponent<MissionManager>();
            if (obstacleDetector == null) obstacleDetector = GetComponent<RaycastObstacleDetector>();

            _planner = new MargasoochiDStarLite();
            _collisionPredictor = new CollisionPredictor();
            _avoidanceController = new AvoidanceController();

            // 3D planning grid. Spans X/Z in [-300, 300] m and Y in [0, 100] m at a 4 m voxel, i.e.
            // 600 x 100 x 600 m. Sized to enclose the demo mission's furthest waypoint (Unity z=240):
            // the previous 400 m grid stopped at 200 m, so goals beyond it fell outside the grid, the
            // planner returned Failed, and the aircraft silently fell back to flying straight at the
            // target with no planned path at all.
            _planningGrid = new OccupancyGrid(new Vector3(-300, 0, -300), new Vector3Int(150, 25, 150), 4.0f);
        }

        private void Start()
        {
            _lastDecision = DecisionRecord.Create(DecisionAction.None, "Autonomy system standby.");
        }

        private void OnEnable()
        {
            AstraEvents.WorldRebased += OnWorldRebased;
        }

        private void OnDisable()
        {
            AstraEvents.WorldRebased -= OnWorldRebased;
        }

        /// <summary>
        /// Floating-origin support. A rebase translates the whole world (and this aircraft) by delta.
        /// The cached planned path and the navigation instrumentation are stored in Unity space, so
        /// they must be shifted by the same amount to keep pointing at the same physical places. The
        /// mission waypoints themselves are stored geographically and re-projected each cycle, so they
        /// need no adjustment. This only relocates cached geometry; it does not change any decision.
        /// </summary>
        private void OnWorldRebased(Vector3 delta)
        {
            if (_currentPath != null)
            {
                for (int i = 0; i < _currentPath.Count; i++)
                {
                    _currentPath[i] += delta;
                }
            }
            _lastTargetWaypoint += delta;
            _lastSegmentStart += delta;

            // The planning grid is stored in Unity space and is not itself translated on a rebase,
            // so anything baked into it is now stale. Force a re-bake on the next plan. (Rebases only
            // occur far outside the demo's few-hundred-metre extent, so this is a correctness
            // backstop for large missions rather than something the campus demo exercises.)
            _staticObstacleVersionApplied = -1;
            _zoneVersionApplied = -1;
        }

        // ----------------------------------------------------------------------------------------
        // Static-obstacle ingestion (procedural-map buildings -> planning grid)
        // ----------------------------------------------------------------------------------------

        private readonly List<StaticObstacleBox> _staticObstacleScratch = new List<StaticObstacleBox>(64);

        /// <summary>
        /// The active map provider, IF it can enumerate its static geometry a priori. Returns null for
        /// providers that cannot (Cesium's fused photogrammetry has no per-building semantics), in
        /// which case buildings stay handled reactively by perception and this ingestion is skipped.
        /// </summary>
        private IStaticObstacleSource ResolveStaticObstacleSource()
        {
            return AstraServices.Get<IMapDataProvider>() as IStaticObstacleSource;
        }

        private void IngestStaticObstacles(IStaticObstacleSource src, OccupancyGrid grid)
        {
            src.GetStaticObstacles(_staticObstacleScratch);
            for (int i = 0; i < _staticObstacleScratch.Count; i++)
            {
                StaticObstacleBox box = _staticObstacleScratch[i];
                grid.InsertObstacleBox(box.Center, box.HalfExtents, staticObstacleInflationM);
            }
            EventLog.Info(LogSource.Navigation, string.Format(
                "Planning grid seeded with {0} static obstacle(s) from the map provider for global " +
                "avoidance (SIMULATED geometry).", _staticObstacleScratch.Count));
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
                targetDestination = geo != null ? geo.ToUnity(missionManager.ActiveWaypoint.Position) : uavPos;
            }

            if (_currentPath == null || _currentPath.Count == 0 || _currentPathIndex >= _currentPath.Count)
            {
                // Bake any threat/no-go zones into the grid before planning (Sec 40-43). Only when
                // the zone set has changed, and only onto our OccupancyGrid.
                Astra.Navigation.ZoneManager zones = AstraServices.Get<Astra.Navigation.ZoneManager>();
                if (zones != null && zones.Version != _zoneVersionApplied &&
                    _planningGrid is OccupancyGrid og)
                {
                    zones.ApplyToGrid(og);
                    _zoneVersionApplied = zones.Version;
                }

                // Bake known static obstacles (procedural-map buildings) into the grid, once per
                // change, so nominal routes clear them globally rather than relying on reactive
                // perception alone. Cesium contributes nothing (no per-building semantics), so this
                // is a no-op there and the offline map is where global static avoidance applies.
                if (ingestStaticObstacles && _planningGrid is OccupancyGrid sog)
                {
                    IStaticObstacleSource src = ResolveStaticObstacleSource();
                    if (src != null && src.HasStaticObstacles &&
                        src.StaticObstacleVersion != _staticObstacleVersionApplied)
                    {
                        IngestStaticObstacles(src, sog);
                        _staticObstacleVersionApplied = src.StaticObstacleVersion;
                    }
                }

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

            // Evaluate obstacle avoidance (only when safely airborne above launch clearance)
            if (worstPrediction.WillCollide && uavPos.y > 4.0f)
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

            // Publish navigation instrumentation for the diagnostics recorder (read-only; does not
            // drive control). Segment start is the previously reached path node when one exists, else
            // the current position, so cross-track error is measured against the leg being flown.
            _lastDesiredVelocity = desiredVelocity;
            _lastTargetWaypoint = nextWp;
            _lastSegmentStart =
                (_currentPath != null && _currentPathIndex > 0 && _currentPathIndex <= _currentPath.Count)
                    ? _currentPath[_currentPathIndex - 1]
                    : uavPos;
            _hasNavInstrumentation = true;

            // -------------------------------------------------------------
            // 6. ACT
            // -------------------------------------------------------------
            SetStage(DecisionStage.Act);
            float t5 = Time.realtimeSinceStartup;

            // Yaw the nose toward the direction of travel, capped to a gentle turn rate. Computed once
            // so both the transit-advance and the normal cruise branch below use the same command.
            float actTargetYaw = Quaternion.LookRotation(
                desiredVelocity.sqrMagnitude > 0.1f ? desiredVelocity.normalized : transform.forward).eulerAngles.y;
            float actYawDiff = Mathf.DeltaAngle(transform.eulerAngles.y, actTargetYaw);
            float actYawRate = Mathf.Clamp(actYawDiff * 1.5f, -45f, 45f);

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
            else if (targetDist < 5.0f && missionManager != null &&
                     missionManager.ActiveWaypointIndex >= 0 &&
                     (missionManager.ActiveWaypoint.Kind == WaypointKind.Observation ||
                      missionManager.ActiveWaypoint.Kind == WaypointKind.Hold))
            {
                // Observation / Hold waypoint reached. Loiter in place for the waypoint's dwell time
                // (capturing an observation), then advance and re-plan to the next point. This is the
                // per-mission-type behaviour: Recon/Surveillance/Survey/Search pause at their
                // observation points, Point-to-Point has none and flies straight through to target.
                Waypoint activeWp = missionManager.ActiveWaypoint;
                flightController.CommandHover();

                if (!_dwelling)
                {
                    _dwelling = true;
                    _dwellElapsed = 0f;
                }
                _dwellElapsed += 1f / Mathf.Max(1f, autonomyRateHz);

                chosenAction = DecisionAction.HoldPosition;
                decisionReason = $"Observing at '{activeWp.Label}' ({_dwellElapsed:F1}/{activeWp.DwellSeconds:F1}s).";

                if (_dwellElapsed >= activeWp.DwellSeconds)
                {
                    _dwelling = false;
                    _dwellElapsed = 0f;
                    missionManager.AdvanceWaypoint();
                    _currentPath = null;
                    _currentPathIndex = 0;
                    chosenAction = DecisionAction.ContinueRoute;
                    decisionReason = $"Observation '{activeWp.Label}' complete. Proceeding to next waypoint.";
                }
            }
            else if (targetDist < 5.0f && missionManager != null &&
                     missionManager.ActiveWaypointIndex >= 0 &&
                     missionManager.ActiveWaypoint.Kind != WaypointKind.Target)
            {
                // Transit / corridor waypoint reached. This branch is the fix for a mission that used
                // to stall on its very first waypoint: previously ONLY Target waypoints advanced the
                // sequence, so a mission that (like both demo missions) began with a Transit waypoint
                // climbed to it and then hovered there forever. A transit waypoint is a point to pass
                // through, not stop at, so advance and keep flying toward the next one without
                // hovering. A looser 5 m radius is used because transit points do not need the
                // precision a target does. Invalidate the cached path so the planner re-plans to the
                // new waypoint on the next cycle.
                missionManager.AdvanceWaypoint();
                _currentPath = null;
                _currentPathIndex = 0;
                chosenAction = DecisionAction.ContinueRoute;
                decisionReason = "Transit waypoint passed. Proceeding to next waypoint.";
                flightController.CommandVelocity(desiredVelocity, actYawRate);
            }
            else
            {
                flightController.CommandVelocity(desiredVelocity, actYawRate);
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
