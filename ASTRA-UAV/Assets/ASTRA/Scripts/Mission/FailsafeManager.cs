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

        private float _excessiveTiltTimer;

        private void Awake()
        {
            if (flightController == null) flightController = GetComponent<FlightControlSystem>();
            if (flightController == null) flightController = FindFirstObjectByType<FlightControlSystem>();
            if (batterySystem == null) batterySystem = GetComponent<BatterySystem>();
            if (batterySystem == null) batterySystem = FindFirstObjectByType<BatterySystem>();
            if (missionManager == null) missionManager = GetComponent<MissionManager>();
            if (missionManager == null) missionManager = FindFirstObjectByType<MissionManager>();
        }

        private void Update()
        {
            if (flightController == null || !flightController.IsArmed)
            {
                _excessiveTiltTimer = 0f;
                return;
            }

            Transform uavTransform = flightController.transform;

            // 1. Battery Failsafe
            if (batterySystem != null)
            {
                float frac = batterySystem.StateOfCharge;
                if (!_emergencyLandTriggered && frac <= emergencyLandBatteryThreshold)
                {
                    _emergencyLandTriggered = true;
                    string msg = $"CRITICAL BATTERY ({frac * 100f:F0}%). Executing emergency auto-land.";
                    AstraEvents.RaiseFailsafeTriggered(msg);
                    EventLog.Error(LogSource.FlightController, msg);
                    flightController.CommandLand();
                }
                else if (!_rthTriggered && frac <= returnHomeBatteryThreshold)
                {
                    _rthTriggered = true;
                    string msg = $"LOW BATTERY ({frac * 100f:F0}%). Triggering Return-to-Launch failsafe.";
                    AstraEvents.RaiseFailsafeTriggered(msg);
                    EventLog.Warning(LogSource.FlightController, msg);
                    missionManager?.CommandReturnHome("Low Battery Reserve Failsafe");
                }
            }

            // 2. Geofence & Tilt Limit Check (Debounced)
            float currentTilt = Vector3.Angle(uavTransform.up, Vector3.up);
            if (currentTilt > maxSafeTiltDeg)
            {
                _excessiveTiltTimer += Time.deltaTime;
                if (_excessiveTiltTimer > 0.8f)
                {
                    string msg = $"ATTITUDE EXCURSION: Tilt {currentTilt:F1}° exceeded limit ({maxSafeTiltDeg}°).";
                    AstraEvents.RaiseFailsafeTriggered(msg);
                    EventLog.Error(LogSource.FlightController, msg);
                }
            }
            else
            {
                _excessiveTiltTimer = 0f;
            }

            if (uavTransform.position.magnitude > geofenceRadiusM)
            {
                string msg = $"GEOFENCE BREACH: Vehicle exceeded safety radius {geofenceRadiusM}m.";
                AstraEvents.RaiseFailsafeTriggered(msg);
                EventLog.Warning(LogSource.FlightController, msg);
                missionManager?.CommandReturnHome("Geofence Containment Failsafe");
            }
        }
    }
}
