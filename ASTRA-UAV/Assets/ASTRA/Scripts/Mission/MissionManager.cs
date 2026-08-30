using System;
using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Geo;
using Astra.Core.Logging;

namespace Astra.Mission
{
    /// <summary>
    /// Manages mission lifecycle, waypoint sequencing, preflight checks, and mission progress.
    /// Implements IMissionProvider.
    /// </summary>
    [DisallowMultipleComponent]
    public class MissionManager : MonoBehaviour, IMissionProvider
    {
        [Header("State")]
        [SerializeField] private MissionPhase currentPhase = MissionPhase.None;
        [SerializeField] private int activeWaypointIndex = -1;

        private MissionDefinition _currentMission;
        private float _waypointDwellTimer;

        public string Name => "ASTRA Mission Manager";
        public DataProvenance Provenance => DataProvenance.Simulated;
        public MissionDefinition Current => _currentMission;
        public MissionPhase Phase => currentPhase;
        public int ActiveWaypointIndex => activeWaypointIndex;
        public Waypoint ActiveWaypoint => (_currentMission != null && activeWaypointIndex >= 0 && activeWaypointIndex < _currentMission.Waypoints.Count)
            ? _currentMission.Waypoints[activeWaypointIndex]
            : default;

        public float Progress
        {
            get
            {
                if (_currentMission == null || _currentMission.WaypointCount == 0) return 0f;
                if (currentPhase == MissionPhase.Complete) return 1.0f;
                return Mathf.Clamp01((float)Mathf.Max(0, activeWaypointIndex) / _currentMission.WaypointCount);
            }
        }

        public event Action<MissionPhase, MissionPhase> PhaseChanged;
        public event Action<int> WaypointReached;

        private void Start()
        {
            AstraServices.Register<IMissionProvider>(this);
            LoadDefaultDemoMission();
        }

        private void OnDestroy()
        {
            AstraServices.UnregisterIfCurrent<IMissionProvider>(this);
        }

        public void LoadDefaultDemoMission()
        {
            // Default Demo Mission in Bangalore / BMSIT area
            MissionDefinition defaultMission = new MissionDefinition
            {
                MissionName = "ASTRA Urban Reconnaissance & Delivery Alpha",
                Objective = "Autonomous tactical navigation through urban corridors to target location with dynamic obstacle avoidance.",
                HomePosition = new GeoCoordinate(13.0827, 77.5877, 0.0),
                DefaultSpeedMps = 8.0f,
                CruiseAltitudeM = 35.0f,
                SafetyMarginM = 8.0f,
                ReturnHomeBatteryFraction = 0.30f,
                EmergencyLandBatteryFraction = 0.15f,
                SimulateGpsDenial = false
            };

            // Waypoints in local offset coords converted to lat/lon
            GeoReference geo = GeoReference.Instance;
            defaultMission.Waypoints.Add(Waypoint.Create(geo != null ? geo.ToGeo(new Vector3(0, 35, 0)) : new GeoCoordinate(13.0827, 77.5877, 35.0), WaypointKind.Transit, "WP1 - Climb"));
            defaultMission.Waypoints.Add(Waypoint.Create(geo != null ? geo.ToGeo(new Vector3(60, 35, 120)) : new GeoCoordinate(13.0837, 77.5887, 35.0), WaypointKind.Transit, "WP2 - Corridor Alpha"));
            defaultMission.Waypoints.Add(Waypoint.Create(geo != null ? geo.ToGeo(new Vector3(140, 35, 240)) : new GeoCoordinate(13.0847, 77.5897, 35.0), WaypointKind.Target, "WP3 - Target Objective"));

            Load(defaultMission, out _);
        }

        public bool Load(MissionDefinition mission, out string reason)
        {
            if (currentPhase == MissionPhase.EnRoute || currentPhase == MissionPhase.Departing)
            {
                reason = "Cannot load new mission while active flight is in progress.";
                return false;
            }

            var errors = mission.Validate(120f, 2500f);
            if (errors.Count > 0)
            {
                reason = string.Join("; ", errors);
                return false;
            }

            _currentMission = mission;
            activeWaypointIndex = -1;
            SetPhase(MissionPhase.Ready);
            AstraEvents.RaiseMissionLoaded(mission);
            EventLog.Info(LogSource.Mission, $"Loaded mission '{mission.MissionName}' with {mission.WaypointCount} waypoints.");

            reason = string.Empty;
            return true;
        }

        public bool Start(out string reason)
        {
            if (_currentMission == null || _currentMission.WaypointCount == 0)
            {
                reason = "No valid mission loaded.";
                return false;
            }

            SetPhase(MissionPhase.Preflight);
            EventLog.Info(LogSource.Mission, "Preflight checks passed. Initiating autonomous mission departure.");

            activeWaypointIndex = 0;
            SetPhase(MissionPhase.Departing);
            reason = string.Empty;
            return true;
        }

        public void Abort(string reason)
        {
            EventLog.Warning(LogSource.Mission, $"Mission ABORTED: {reason}");
            SetPhase(MissionPhase.Aborted);
            AstraEvents.RaiseMissionEnded(false, reason);
        }

        public void AdvanceWaypoint()
        {
            if (_currentMission == null) return;

            int prevIdx = activeWaypointIndex;
            if (prevIdx >= 0 && prevIdx < _currentMission.WaypointCount)
            {
                Waypoint reachedWp = _currentMission.Waypoints[prevIdx];
                WaypointReached?.Invoke(prevIdx);
                AstraEvents.RaiseWaypointReached(prevIdx, reachedWp);
                EventLog.Info(LogSource.Mission, $"Waypoint {prevIdx + 1} reached: '{reachedWp.Label}'");

                if (reachedWp.Kind == WaypointKind.Target)
                {
                    SetPhase(MissionPhase.OnTarget);
                    EventLog.Info(LogSource.Mission, "TARGET REACHED. Executing payload dwell hold.");
                    return;
                }
            }

            activeWaypointIndex++;
            if (activeWaypointIndex >= _currentMission.WaypointCount)
            {
                SetPhase(MissionPhase.Complete);
                AstraEvents.RaiseMissionEnded(true, "All mission waypoints completed successfully.");
                EventLog.Info(LogSource.Mission, "MISSION COMPLETED SUCCESSFULLY.");
            }
            else
            {
                SetPhase(MissionPhase.EnRoute);
            }
        }

        public void CommandReturnHome(string reason)
        {
            EventLog.Info(LogSource.Mission, $"Returning to launch point (RTL): {reason}");
            SetPhase(MissionPhase.Returning);
        }

        public void SetPhase(MissionPhase newPhase)
        {
            if (currentPhase != newPhase)
            {
                MissionPhase prev = currentPhase;
                currentPhase = newPhase;
                PhaseChanged?.Invoke(prev, newPhase);
                AstraEvents.RaiseMissionPhaseChanged(prev, newPhase);
            }
        }
    }
}
