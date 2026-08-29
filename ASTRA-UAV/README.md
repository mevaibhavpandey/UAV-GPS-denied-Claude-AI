<div align="center">

# 🛸 ASTRA UAV — Autonomous Quadcopter Digital Twin & Navigation Simulator

**AI-Powered Autonomous Quadcopter UAV Digital Twin, Mission Planning, Real-Time Obstacle Avoidance & GPS-Denied Navigation Platform**

[![Unity 6](https://img.shields.io/badge/Unity-6000.0.0f1-000000?style=for-the-badge&logo=unity&logoColor=white)](https://unity.com/)
[![Render Pipeline](https://img.shields.io/badge/Render%20Pipeline-Universal%20RP%20(URP)-0080FF?style=for-the-badge)](https://unity.com/srp/universal-render-pipeline)
[![Language](https://img.shields.io/badge/Language-C%23%209.0%20%2F%20.NET%20Standard%202.1-239120?style=for-the-badge&logo=c-sharp&logoColor=white)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![Institution](https://img.shields.io/badge/Institution-BMSIT%20%26%20M-B31B1B?style=for-the-badge)](https://bmsit.ac.in/)
[![Squad](https://img.shields.io/badge/Team-ASTRA%20Tactical%20Readiness-FFD700?style=for-the-badge&labelColor=000000)](https://github.com/mevaibhavpandey)
[![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)](LICENSE)

*An engineering and research demonstration platform developed for tactical autonomous flight, digital-twin fidelity, and academic review.*

---

[Key Capabilities](#-key-capabilities) •
[9-Layer Architecture](#-9-layer-system-architecture) •
[Flight Modes & Controls](#-flight-modes--controls) •
[Ground Control Station](#-ground-control-station-gcs) •
[Installation & Quickstart](#-installation--quickstart) •
[Documentation](#-documentation-index)

---

</div>

## 📌 Executive Summary

**ASTRA (Armed Squad for Tactical Readiness and Awareness)** is an advanced quadcopter digital twin and autonomous mission-control simulation platform built from the ground up for **Unity 6**. 

Engineered to represent the flight dynamics, avionics stack, and sensor payload of a **Tarot 650-class autonomous UAV**, ASTRA bridges high-fidelity rigid-body aerodynamics with state-of-the-art autonomy algorithms—including **Margasoochi (D\* Lite)** incremental path planning, real-time multi-beam LiDAR collision prediction, dual-view perception rendering, and visual-inertial **GPS-Denied navigation**.

```
+----------------------------------------------------------------------------------------------------+
|                                    ASTRA UAV SIMULATION SUITE                                      |
+--------------------------------+---------------------------------+---------------------------------+
|       DIGITAL TWIN & GCS       |      AUTONOMOUS NAVIGATION      |       GPS-DENIED SENSING        |
|  • Tarot 650 Carbon Airframe   |  • Margasoochi (D* Lite) Search |  • Visual-Inertial Odometry    |
|  • 4-Loop Cascaded PID Control |  • Real-Time LiDAR Raycast CPA  |  • IMU + Baro + Mag Sensor EKF  |
|  • Exploded & X-Ray Avionics   |  • 7-Stage Continuous Loop      |  • Distance Integration Drift   |
|  • Tactical Mission HUD & Log  |  • Dynamic 3D Voxel Grid Map    |  • Simulated Jamming & Outages  |
+--------------------------------+---------------------------------+---------------------------------+
```

---

## ⚡ Key Capabilities

### 1. 🔄 Dual Synchronized Visualization Modes
The simulation maintains a single unified ground-truth state across two real-time visualization pipelines:
- **Mode A: Real-World View**: Complete 3D urban geospatial environment with extruded buildings, road networks, terrain heights, dynamic obstacles, and planned flight corridors. Supports live backend switching (`F9`) between Google Photorealistic 3D Tiles via Cesium ion and an offline zero-dependency procedural city generator.
- **Mode B: Perception View (`F5`)**: High-contrast monochrome machine-perception visualizer reflecting the internal model of the autonomy stack—rendering tracked obstacle bounding boxes, LiDAR swept beams, point clouds, trajectory splines, and expanding localization uncertainty ellipsoids.

---

## 🎮 Flight Modes & Controls

| Hotkey | Function | Details |
|---|---|---|
| `R` | **Arm** | Runs preflight checks (battery, tilt, sensor health) and spools motors |
| `F` | **Disarm** | Cuts motor throttle and sets flight state to Disarmed |
| `W` / `S` | **Pitch** | Pitch forward / Pitch backward |
| `A` / `D` | **Roll** | Roll left / Roll right |
| `Q` / `E` | **Yaw** | Yaw counter-clockwise / Yaw clockwise |
| `Space` | **Climb** | Increase vertical collective throttle |
| `Left Ctrl` | **Descent** | Decrease vertical collective throttle |
| `H` | **Hover** | Automated position and altitude hold |
| `L` | **Land** | Automated controlled descent and landing |
| `F1` | **Manual Mode** | Direct operator stick input |
| `F2` | **Autonomous GPS** | Autonomous mission navigation with GNSS fix |
| `F3` | **GPS-Denied Mode** | Simulates GNSS loss, switching to Visual-Inertial Odometry |
| `F5` | **Toggle View Mode** | Switches between Real-World View (Mode A) and Perception View (Mode B) |
| `F6` | **Engineering View** | Digital Twin component inspection: Normal, Exploded, X-Ray, Wireframe |
| `F8` | **Presentation Demo** | Automated 18-step end-to-end academic demonstration |
| `F9` | **Map Backend Toggle** | Switches between Cesium 3D Tiles and zero-dependency Offline City |
| `Tab` | **Cycle Camera Rig** | Chase $\rightarrow$ FPV $\rightarrow$ Orbit $\rightarrow$ Top-Down $\rightarrow$ Engineering $\rightarrow$ Cinematic |
| `Esc` | **Abort Mission** | Immediate mission termination and failsafe hold |

---

## 🛠️ Installation & Quickstart

1. Clone repository:
   ```bash
   git clone https://github.com/mevaibhavpandey/UAV-GPS-denied-Claude-AI.git
   ```
2. Open in **Unity 6 (6000.0.0f1)** with Universal Render Pipeline (URP).
3. In the Unity menu, select:
   - `ASTRA > Setup > Configure Project Settings`
   - `ASTRA > Build > Generate Demo Scene`
4. Open `Assets/ASTRA/Scenes/ASTRA_GCS_Demo.unity` and click **Play** ▶️.
