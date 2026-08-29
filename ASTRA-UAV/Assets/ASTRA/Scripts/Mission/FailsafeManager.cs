using System;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Logging;
using Astra.Drone;
using Astra.Flight;

namespace Astra.Mission
{
    /// <summary>
    /// Monitors battery state, attitude limits, geofence, and sensor health to trigger automated failsafe procedures.
    /// </summary>
    [DisallowMultipleComponent]
    public class FailsafeManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private FlightControlSystem flightController;
        [SerializeField] private BatterySystem batterySystem;
        [SerializeField] private MissionManager missionManager;

        [Header("Failsafe Thresholds")]
        [SerializeField] private float returnHomeBatteryThreshold = 0.30f;
        [SerializeField] private float emergencyLandBatteryThreshold = 0.15f;
        [SerializeField] private float maxSafeTiltDeg = 55.0f;
        [SerializeField] private float geofenceRadiusM = 1500.0f;

        private bool _rthTriggered;
        private bool _emergencyLandTriggered;

        private void Awake()
        {
            if (flightController == null) flightController = GetComponent<FlightControlSystem>();
            if (batterySystem == null) batterySystem = GetComponent<BatterySystem>();
            if (missionManager == null) missionManager = GetComponent<MissionManager>();
        }

        private void Update()
        {
            if (flightController == null || !flightController.IsArmed) return;

            // 1. Battery Failsafe
            if (batterySystem != null)
            {
                float frac = batterySystem.RemainingFraction;
                if (!_emergencyLandTriggered && frac <= emergencyLandBatteryThreshold)
                {
                    _emergencyLandTriggered = true;
                    string msg = $"CRITICAL BATTERY ({frac * 100f:F0}%). Executing emergency auto-land.";
                    AstraEvents.RaiseFailsafeTriggered(msg);
                    EventLog.Error(LogSource.Failsafe, msg);
                    flightController.CommandLand();
                }
                else if (!_rthTriggered && frac <= returnHomeBatteryThreshold)
                {
                    _rthTriggered = true;
                    string msg = $"LOW BATTERY ({frac * 100f:F0}%). Triggering Return-to-Launch failsafe.";
                    AstraEvents.RaiseFailsafeTriggered(msg);
                    EventLog.Warn(LogSource.Failsafe, msg);
                    missionManager?.CommandReturnHome("Low Battery Reserve Failsafe");
                }
            }

            // 2. Geofence & Tilt Limit Check
            float currentTilt = Vector3.Angle(transform.up, Vector3.up);
            if (currentTilt > maxSafeTiltDeg)
            {
                string msg = $"ATTITUDE EXCURSION: Tilt {currentTilt:F1}° exceeded limit ({maxSafeTiltDeg}°).";
                AstraEvents.RaiseFailsafeTriggered(msg);
                EventLog.Error(LogSource.Failsafe, msg);
            }

            if (transform.position.magnitude > geofenceRadiusM)
            {
                string msg = $"GEOFENCE BREACH: Vehicle exceeded safety radius {geofenceRadiusM}m.";
                AstraEvents.RaiseFailsafeTriggered(msg);
                EventLog.Warn(LogSource.Failsafe, msg);
                missionManager?.CommandReturnHome("Geofence Containment Failsafe");
            }
        }
    }
}
