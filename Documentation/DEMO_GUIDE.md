# ASTRA UAV — Academic & Technical Demonstration Guide

## 1. Overview
This guide outlines the recommended live demonstration procedure for faculty reviews, Head of Department evaluations, Principal presentations, and funding committees.

---

## 2. Automated One-Key Presentation Mode (`F8`)
The simulator features a fully automated presentation sequence triggered by pressing **`F8`** or clicking **Run Demo** on the GCS interface.

### Sequence Breakdown:
1. **Introduction & Splash**: Cinematic camera overview of the ASTRA platform.
2. **Digital Twin Airframe**: Orbit camera inspecting the Tarot 650 quadcopter layout, brushless motors, and battery.
3. **Ground Control Station & Preflight**: Live telemetry initialization, sensor checks, and arming validation.
4. **Margasoochi Path Planning**: Initial 3D corridor route generation from Home to Target.
5. **Takeoff & Climb**: Smooth vertical ascent to 35m AGL cruise altitude.
6. **Autonomous Cruise**: High-speed corridor navigation.
7. **LiDAR Obstacle Detection**: Detection of a building obstruction along the path; CPA and TTC calculations.
8. **Dynamic Avoidance**: Multi-candidate trajectory evaluation and lateral Starboard avoidance maneuver.
9. **Route Rejoin**: Smooth Catmull-Rom spline rejoining the nominal mission corridor.
10. **Synchronized Perception View (`F5`)**: Switch to monochrome Mode B showing point clouds, LiDAR rays, and uncertainty ellipsoids.
11. **GPS-Denied Demonstration (`F3`)**: Simulated GNSS loss with live Visual-Inertial Odometry drift tracking.
12. **Target Arrival**: Precision hover hold over the objective point.
13. **Return-to-Launch (RTL)**: Autonomous return home and descent.
14. **Precision Landing & Disarm**: Safe touchdown and telemetry audit logging.
15. **Engineering Inspection (`F6`)**: Exploded component view and transparent X-Ray avionics inspection.

---

## 3. Manual Step-by-Step Live Demonstration Script
If conducting an interactive demonstration:
1. **Introduce Project**: Show GCS HUD and Digital Twin.
2. **Set Target**: Click on the map or type coordinates in the Mission Planner panel.
3. **Execute Mission**: Click **START MISSION**. Observe autonomous takeoff and navigation.
4. **Show Dynamic Avoidance**: Point to the **AI Autonomy Decision Panel** to explain the live 7-stage loop and numeric reason metrics.
5. **Switch to Perception View**: Press **`F5`** to display how the autonomy stack perceives the world.
6. **Simulate GPS Jamming**: Press **`F3`** to demonstrate GPS-Denied visual-inertial odometry estimation.
7. **Show Engineering View**: Press **`F6`** and press **`3`** for the exploded digital-twin view.
