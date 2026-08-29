using System;
using UnityEngine;
using Astra.Contracts;

namespace Astra.Core
{
    /// <summary>
    /// The cross-system event bus.
    ///
    /// WHAT BELONGS HERE AND WHAT DOES NOT
    /// -----------------------------------
    /// Only events with several unrelated subscribers belong on the bus. Flight state changing is a
    /// good example: the HUD, the event log, the camera director, the perception overlay and the
    /// mission monitor all care, and none of them should need a reference to the flight controller to
    /// find out.
    ///
    /// Continuous per-frame data does NOT belong here. Telemetry is polled from ITelemetryProvider by
    /// whoever needs it, at whatever rate suits them, because pushing a hundred telemetry events per
    /// second through a delegate chain would allocate, would couple the flight loop's timing to the
    /// UI's cost, and would gain nothing over a read.
    ///
    /// The rule of thumb: events for things that HAPPEN, polling for things that ARE.
    ///
    /// EVERY RAISE IS EXCEPTION-ISOLATED
    /// --------------------------------
    /// A subscriber that throws must not prevent other subscribers from being notified, and must
    /// absolutely not propagate into the flight control loop. A UI bug that stops the aircraft
    /// flying would be a design failure, not a UI failure. Each invocation is therefore wrapped
    /// individually.
    ///
    /// THREADING: main thread only.
    /// </summary>
    public static class AstraEvents
    {
        // ====================================================================================
        // FLIGHT
        // ====================================================================================

        /// <summary>Flight state changed. Arguments: previous, current.</summary>
        public static event Action<FlightState, FlightState> FlightStateChanged;

        /// <summary>Armed or disarmed. Argument: true if now armed.</summary>
        public static event Action<bool> ArmedStateChanged;

        /// <summary>Control authority changed between manual, autonomous and failsafe.</summary>
        public static event Action<ControlSource, ControlSource> ControlSourceChanged;

        /// <summary>
        /// An arming attempt was refused. Argument: the reason. Surfaced prominently because a
        /// silent refusal to arm is one of the most confusing things a flight controller can do, and
        /// one of the most common sources of frustration with real autopilots.
        /// </summary>
        public static event Action<string> ArmingRefused;

        // ====================================================================================
        // PERCEPTION AND NAVIGATION
        // ====================================================================================

        /// <summary>A new obstacle entered tracking.</summary>
        public static event Action<ObstacleReading> ObstacleDetected;

        /// <summary>A tracked obstacle was dropped, by track id.</summary>
        public static event Action<int> ObstacleLost;

        /// <summary>A collision was predicted. Arguments: the obstacle, the prediction.</summary>
        public static event Action<ObstacleReading, CollisionPrediction> CollisionPredicted;

        /// <summary>The threat level of the overall situation changed.</summary>
        public static event Action<ThreatLevel, ThreatLevel> ThreatLevelChanged;

        /// <summary>A route was planned or replanned.</summary>
        public static event Action<PathPlanResult> RoutePlanned;

        /// <summary>Planning failed. Argument: the reason.</summary>
        public static event Action<string> PlanningFailed;

        /// <summary>An autonomous decision was taken.</summary>
        public static event Action<DecisionRecord> DecisionMade;

        /// <summary>The decision pipeline entered a stage. Drives the animated pipeline display.</summary>
        public static event Action<DecisionStage> DecisionStageEntered;

        // ====================================================================================
        // LOCALISATION
        // ====================================================================================

        /// <summary>
        /// GPS availability changed. Argument: true if a usable fix now exists.
        ///
        /// This is the single most important event in the Phase 3 demonstration. Note that it
        /// reports what the sensor layer is publishing, not a mode the operator selected: the
        /// autonomy stack finds out about an outage the same way a real aircraft does, by fixes
        /// stopping.
        /// </summary>
        public static event Action<bool> GpsAvailabilityChanged;

        /// <summary>The active localisation source changed, e.g. GPS-aided to dead reckoning.</summary>
        public static event Action<string, string> LocalizationSourceChanged;

        /// <summary>
        /// Accumulated dead-reckoning drift crossed a reporting threshold. Argument: metres.
        /// Reported because unbounded drift is the defining limitation of odometry without loop
        /// closure, and concealing it would misrepresent what the system does.
        /// </summary>
        public static event Action<float> DriftThresholdCrossed;

        // ====================================================================================
        // MISSION
        // ====================================================================================

        public static event Action<MissionDefinition> MissionLoaded;
        public static event Action<MissionPhase, MissionPhase> MissionPhaseChanged;

        /// <summary>Waypoint reached. Arguments: index, the waypoint.</summary>
        public static event Action<int, Waypoint> WaypointReached;

        /// <summary>Mission ended. Arguments: true if successful, reason text.</summary>
        public static event Action<bool, string> MissionEnded;

        // ====================================================================================
        // POWER AND HEALTH
        // ====================================================================================

        /// <summary>Battery crossed a warning threshold. Argument: remaining fraction.</summary>
        public static event Action<float> BatteryWarning;

        /// <summary>A subsystem's health changed. Arguments: subsystem name, previous, current.</summary>
        public static event Action<string, SubsystemStatus, SubsystemStatus> SubsystemStatusChanged;

        /// <summary>A motor's health changed. Arguments: motor index 0-3, healthy.</summary>
        public static event Action<int, bool> MotorHealthChanged;

        /// <summary>A failsafe triggered. Argument: description.</summary>
        public static event Action<string> FailsafeTriggered;

        // ====================================================================================
        // PRESENTATION
        // ====================================================================================

        /// <summary>The map view mode changed between real-world and perception.</summary>
        public static event Action<MapViewMode> MapViewModeChanged;

        /// <summary>The map data provider was switched, e.g. Cesium to offline.</summary>
        public static event Action<IMapDataProvider> MapProviderChanged;

        /// <summary>The operating mode changed between simulation, hybrid and hardware.</summary>
        public static event Action<OperatingMode> OperatingModeChanged;

        /// <summary>Presentation mode toggled. Argument: true if active.</summary>
        public static event Action<bool> PresentationModeChanged;

        // ====================================================================================
        // RAISE METHODS
        // ====================================================================================
        // Public raise methods rather than public events being invoked directly by anyone. C#
        // already prevents external invocation of an event, so this is not about access control -
        // it is so that every raise passes through the same exception isolation, and so the set of
        // things that can be announced is enumerable in one place.

        public static void RaiseFlightStateChanged(FlightState from, FlightState to)
        {
            Raise(FlightStateChanged, from, to, "FlightStateChanged");
        }

        public static void RaiseArmedStateChanged(bool armed)
        {
            Raise(ArmedStateChanged, armed, "ArmedStateChanged");
        }

        public static void RaiseControlSourceChanged(ControlSource from, ControlSource to)
        {
            Raise(ControlSourceChanged, from, to, "ControlSourceChanged");
        }

        public static void RaiseArmingRefused(string reason)
        {
            Raise(ArmingRefused, reason, "ArmingRefused");
        }

        public static void RaiseObstacleDetected(ObstacleReading obstacle)
        {
            Raise(ObstacleDetected, obstacle, "ObstacleDetected");
        }

        public static void RaiseObstacleLost(int trackId)
        {
            Raise(ObstacleLost, trackId, "ObstacleLost");
        }

        public static void RaiseCollisionPredicted(ObstacleReading obstacle, CollisionPrediction p)
        {
            Raise(CollisionPredicted, obstacle, p, "CollisionPredicted");
        }

        public static void RaiseThreatLevelChanged(ThreatLevel from, ThreatLevel to)
        {
            Raise(ThreatLevelChanged, from, to, "ThreatLevelChanged");
        }

        public static void RaiseRoutePlanned(PathPlanResult result)
        {
            Raise(RoutePlanned, result, "RoutePlanned");
        }

        public static void RaisePlanningFailed(string reason)
        {
            Raise(PlanningFailed, reason, "PlanningFailed");
        }

        public static void RaiseDecisionMade(DecisionRecord record)
        {
            Raise(DecisionMade, record, "DecisionMade");
        }

        public static void RaiseDecisionStageEntered(DecisionStage stage)
        {
            Raise(DecisionStageEntered, stage, "DecisionStageEntered");
        }

        public static void RaiseGpsAvailabilityChanged(bool available)
        {
            Raise(GpsAvailabilityChanged, available, "GpsAvailabilityChanged");
        }

        public static void RaiseLocalizationSourceChanged(string from, string to)
        {
            Raise(LocalizationSourceChanged, from, to, "LocalizationSourceChanged");
        }

        public static void RaiseDriftThresholdCrossed(float driftM)
        {
            Raise(DriftThresholdCrossed, driftM, "DriftThresholdCrossed");
        }

        public static void RaiseMissionLoaded(MissionDefinition mission)
        {
            Raise(MissionLoaded, mission, "MissionLoaded");
        }

        public static void RaiseMissionPhaseChanged(MissionPhase from, MissionPhase to)
        {
            Raise(MissionPhaseChanged, from, to, "MissionPhaseChanged");
        }

        public static void RaiseWaypointReached(int index, Waypoint waypoint)
        {
            Raise(WaypointReached, index, waypoint, "WaypointReached");
        }

        public static void RaiseMissionEnded(bool success, string reason)
        {
            Raise(MissionEnded, success, reason, "MissionEnded");
        }

        public static void RaiseBatteryWarning(float remainingFraction)
        {
            Raise(BatteryWarning, remainingFraction, "BatteryWarning");
        }

        public static void RaiseSubsystemStatusChanged(string name, SubsystemStatus from,
                                                       SubsystemStatus to)
        {
            Raise(SubsystemStatusChanged, name, from, to, "SubsystemStatusChanged");
        }

        public static void RaiseMotorHealthChanged(int motorIndex, bool healthy)
        {
            Raise(MotorHealthChanged, motorIndex, healthy, "MotorHealthChanged");
        }

        public static void RaiseFailsafeTriggered(string description)
        {
            Raise(FailsafeTriggered, description, "FailsafeTriggered");
        }

        public static void RaiseMapViewModeChanged(MapViewMode mode)
        {
            Raise(MapViewModeChanged, mode, "MapViewModeChanged");
        }

        public static void RaiseMapProviderChanged(IMapDataProvider provider)
        {
            Raise(MapProviderChanged, provider, "MapProviderChanged");
        }

        public static void RaiseOperatingModeChanged(OperatingMode mode)
        {
            Raise(OperatingModeChanged, mode, "OperatingModeChanged");
        }

        public static void RaisePresentationModeChanged(bool active)
        {
            Raise(PresentationModeChanged, active, "PresentationModeChanged");
        }

        // ====================================================================================
        // EXCEPTION-ISOLATED DISPATCH
        // ====================================================================================
        // One generic helper per arity. Walking the invocation list manually costs an array
        // allocation per raise, which is why continuous per-frame data is polled rather than
        // pushed - these events fire on discrete occurrences, a handful per second at most, so the
        // allocation is irrelevant and the isolation is worth having.

        private static void Raise<T>(Action<T> handler, T arg, string eventName)
        {
            if (handler == null)
            {
                return;
            }
            Delegate[] targets = handler.GetInvocationList();
            for (int i = 0; i < targets.Length; i++)
            {
                try
                {
                    ((Action<T>)targets[i])(arg);
                }
                catch (Exception e)
                {
                    LogSubscriberFailure(eventName, targets[i], e);
                }
            }
        }

        private static void Raise<T1, T2>(Action<T1, T2> handler, T1 a1, T2 a2, string eventName)
        {
            if (handler == null)
            {
                return;
            }
            Delegate[] targets = handler.GetInvocationList();
            for (int i = 0; i < targets.Length; i++)
            {
                try
                {
                    ((Action<T1, T2>)targets[i])(a1, a2);
                }
                catch (Exception e)
                {
                    LogSubscriberFailure(eventName, targets[i], e);
                }
            }
        }

        private static void Raise<T1, T2, T3>(Action<T1, T2, T3> handler, T1 a1, T2 a2, T3 a3,
                                              string eventName)
        {
            if (handler == null)
            {
                return;
            }
            Delegate[] targets = handler.GetInvocationList();
            for (int i = 0; i < targets.Length; i++)
            {
                try
                {
                    ((Action<T1, T2, T3>)targets[i])(a1, a2, a3);
                }
                catch (Exception e)
                {
                    LogSubscriberFailure(eventName, targets[i], e);
                }
            }
        }

        private static void LogSubscriberFailure(string eventName, Delegate target, Exception e)
        {
            string owner = target.Target != null ? target.Target.GetType().Name : "static";
            Debug.LogError("[AstraEvents] Subscriber " + owner + "." + target.Method.Name +
                           " threw handling " + eventName + ". The event was still delivered to " +
                           "the remaining subscribers.\n" + e);
        }

        /// <summary>
        /// Removes every subscriber. Called on scene teardown, and by the static reset below.
        ///
        /// This matters more than it looks. A static event holds a strong reference to every
        /// subscriber, so a MonoBehaviour that subscribes and is then destroyed without
        /// unsubscribing stays alive in the delegate chain and gets invoked as a destroyed object.
        /// Components should always unsubscribe in OnDisable; this is the backstop for when one
        /// does not.
        /// </summary>
        public static void ClearAllSubscribers()
        {
            FlightStateChanged = null;
            ArmedStateChanged = null;
            ControlSourceChanged = null;
            ArmingRefused = null;

            ObstacleDetected = null;
            ObstacleLost = null;
            CollisionPredicted = null;
            ThreatLevelChanged = null;
            RoutePlanned = null;
            PlanningFailed = null;
            DecisionMade = null;
            DecisionStageEntered = null;

            GpsAvailabilityChanged = null;
            LocalizationSourceChanged = null;
            DriftThresholdCrossed = null;

            MissionLoaded = null;
            MissionPhaseChanged = null;
            WaypointReached = null;
            MissionEnded = null;

            BatteryWarning = null;
            SubsystemStatusChanged = null;
            MotorHealthChanged = null;
            FailsafeTriggered = null;

            MapViewModeChanged = null;
            MapProviderChanged = null;
            OperatingModeChanged = null;
            PresentationModeChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            ClearAllSubscribers();
        }
    }
}
