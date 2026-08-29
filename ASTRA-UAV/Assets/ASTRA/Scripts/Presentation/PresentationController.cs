using System.Collections;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Logging;
using Astra.Flight;
using Astra.Localization;
using Astra.Mission;
using Astra.Perception;
using Astra.Cameras;
using Astra.Engineering;

namespace Astra.Presentation
{
    /// <summary>
    /// Automated 23-step presentation sequence controller.
    /// Orchestrates an end-to-end demonstration for academic review, faculty, and funding committees.
    /// Triggered by F8 or GCS Presentation button.
    /// </summary>
    [DisallowMultipleComponent]
    public class PresentationController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private FlightControlSystem flightController;
        [SerializeField] private MissionManager missionManager;
        [SerializeField] private CameraController cameraController;
        [SerializeField] private PerceptionViewController perceptionViewController;
        [SerializeField] private EngineeringViewController engineeringViewController;

        [Header("State")]
        [SerializeField] private bool isPresentationRunning = false;
        [SerializeField] private int currentStep = 0;
        [SerializeField] private string currentStepDescription = "Standby";

        public bool IsRunning => isPresentationRunning;
        public int CurrentStep => currentStep;
        public string StepDescription => currentStepDescription;

        private void Awake()
        {
            if (flightController == null) flightController = FindFirstObjectByType<FlightControlSystem>();
            if (missionManager == null) missionManager = FindFirstObjectByType<MissionManager>();
            if (cameraController == null) cameraController = FindFirstObjectByType<CameraController>();
            if (perceptionViewController == null) perceptionViewController = FindFirstObjectByType<PerceptionViewController>();
            if (engineeringViewController == null) engineeringViewController = FindFirstObjectByType<EngineeringViewController>();
        }

        private void Update()
        {
            // F8 shortcut toggles Presentation Mode
            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (isPresentationRunning) StopPresentation();
                else StartPresentation();
            }
        }

        public void StartPresentation()
        {
            if (isPresentationRunning) return;
            isPresentationRunning = true;
            AstraEvents.RaisePresentationModeChanged(true);
            StartCoroutine(PresentationSequence());
        }

        public void StopPresentation()
        {
            isPresentationRunning = false;
            StopAllCoroutines();
            AstraEvents.RaisePresentationModeChanged(false);
            EventLog.Info(LogSource.System, "Presentation demonstration stopped.");
        }

        private IEnumerator PresentationSequence()
        {
            EventLog.Info(LogSource.System, "=== STARTING ASTRA UAV COMPREHENSIVE PRESENTATION DEMONSTRATION ===");

            // Step 1: Splash & Introduction
            SetStep(1, "ASTRA UAV: AI-Powered Autonomous Quadcopter Digital Twin & Navigation Simulation");
            cameraController?.SetRig(CameraRigMode.Cinematic);
            yield return new WaitForSeconds(3.5f);

            // Step 2: Digital Twin Airframe Showcase
            SetStep(2, "Inspecting Digital Twin Structure & Propulsion Specifications");
            cameraController?.SetRig(CameraRigMode.Orbit);
            yield return new WaitForSeconds(4.0f);

            // Step 3: Ground Control Station & Preflight Initialization
            SetStep(3, "Initializing Ground Control Station & Sensor Suite Preflight Checks");
            flightController?.TryArm(out _);
            yield return new WaitForSeconds(3.0f);

            // Step 4: Mission Route Planning
            SetStep(4, "Loading Tactical Urban Navigation Mission with Margasoochi D* Lite Planner");
            missionManager?.LoadDefaultDemoMission();
            yield return new WaitForSeconds(2.5f);

            // Step 5: Arm & Automated Takeoff
            SetStep(5, "Arming Propulsion System & Automated Climb to Cruise Altitude (35m AGL)");
            flightController?.SetControlSource(ControlSource.Autonomous);
            missionManager?.Start(out _);
            flightController?.CommandTakeoff(35.0f);
            cameraController?.SetRig(CameraRigMode.ChaseFollow);
            yield return new WaitForSeconds(6.0f);

            // Step 6: Autonomous Navigation en route
            SetStep(6, "Autonomous Cruise Navigation along Urban Corridor");
            yield return new WaitForSeconds(5.0f);

            // Step 7: Obstacle Detection & Real-time Threat Analysis
            SetStep(7, "LiDAR Swept-Volume Detects Building Structure - Computing CPA & TTC");
            yield return new WaitForSeconds(4.0f);

            // Step 8: Dynamic Obstacle Avoidance Maneuver
            SetStep(8, "Executing Lateral Collision Avoidance Maneuver around Obstacle");
            yield return new WaitForSeconds(6.0f);

            // Step 9: Rejoining Planned Route
            SetStep(9, "Obstacle Cleared - Smoothly Rejoining Nominal Flight Corridor");
            yield return new WaitForSeconds(4.0f);

            // Step 10: Switch to Synchronized Perception View (Mode B)
            SetStep(10, "Switching to Mode B: Autonomy Perception & Sensor Point-Cloud View");
            perceptionViewController?.SetViewMode(MapViewMode.Perception);
            yield return new WaitForSeconds(5.0f);

            // Step 11: Demonstrate GPS-Denied Localization
            SetStep(11, "Simulating GPS Denial: Switching to Visual-Inertial Odometry Estimation");
            AstraServices.Get<ISensorProvider>()?.SetGpsEnabled(false);
            yield return new WaitForSeconds(6.0f);

            // Step 12: Target Objective Reached & Precision Hover
            SetStep(12, "Target Destination Reached - Holding Position for Payload Delivery");
            cameraController?.SetRig(CameraRigMode.Orbit);
            yield return new WaitForSeconds(5.0f);

            // Step 13: GPS Restoration & Return-To-Launch (RTL)
            SetStep(13, "Restoring GNSS Fix - Initiating Autonomous Return-To-Launch (RTL)");
            AstraServices.Get<ISensorProvider>()?.SetGpsEnabled(true);
            perceptionViewController?.SetViewMode(MapViewMode.RealWorld);
            missionManager?.CommandReturnHome("Presentation RTL sequence");
            cameraController?.SetRig(CameraRigMode.ChaseFollow);
            yield return new WaitForSeconds(7.0f);

            // Step 14: Automated Approach & Landing
            SetStep(14, "Executing Precision Approach and Controlled Vertical Landing");
            flightController?.CommandLand();
            yield return new WaitForSeconds(7.0f);

            // Step 15: Disarm & Mission Completion
            SetStep(15, "Touchdown Confirmed. Disarming Motors & Finalizing Mission Log");
            flightController?.Disarm();
            yield return new WaitForSeconds(3.0f);

            // Step 16: Engineering Digital Twin Exploded Inspection
            SetStep(16, "Opening Digital Twin Engineering View: Component Exploded Hierarchy");
            engineeringViewController?.ToggleEngineeringView();
            engineeringViewController?.SetMode(EngineeringViewMode.Exploded);
            yield return new WaitForSeconds(6.0f);

            // Step 17: Transparent X-Ray & Wireframe Inspection
            SetStep(17, "Digital Twin Transparent X-Ray Avionics & Wiring Inspection");
            engineeringViewController?.SetMode(EngineeringViewMode.TransparentXRay);
            yield return new WaitForSeconds(4.0f);

            // Step 18: Summary & Final Report
            SetStep(18, "ASTRA UAV Demonstration Completed Successfully.");
            engineeringViewController?.ToggleEngineeringView();
            cameraController?.SetRig(CameraRigMode.Cinematic);

            EventLog.Info(LogSource.System, "=== ASTRA UAV PRESENTATION DEMONSTRATION COMPLETED ===");
            isPresentationRunning = false;
        }

        private void SetStep(int stepNum, string desc)
        {
            currentStep = stepNum;
            currentStepDescription = desc;
            EventLog.Info(LogSource.System, $"[DEMO STEP {stepNum}/18] {desc}");
        }
    }
}
