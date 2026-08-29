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

```
[ Real World Environment ] <---> [ Unified UAV Simulation State ] <---> [ Autonomy Perception View ]
 (Photorealistic 3D Tiles)            (Physics, Sensors, EKF)              (Monochrome Point Cloud)
```

---

### 2. 🧠 Margasoochi (D\* Lite) Dynamic Path Planning
ASTRA implements the **Margasoochi incremental D\* Lite algorithm** alongside standard **A\*** and **Dijkstra** baselines.
- **Initial Planning**: Formulates a collision-free 3D trajectory over a voxel occupancy grid with an $8\,\text{m}$ obstacle inflation safety corridor.
- **Dynamic Replanning**: When an obstacle emerges mid-flight, Margasoochi updates only the affected local search tree vertices, achieving replan times orders of magnitude faster than full re-search algorithms.
- **Trajectory Smoothing**: Catmull-Rom cubic spline interpolation with continuous spherecast line-of-sight shortcutting.

---

### 3. 🔁 7-Stage Autonomous Decision Loop
The autonomy executive executes a continuous 10 Hz decision cycle:
$$\boxed{\text{SENSE}} \longrightarrow \boxed{\text{PERCEIVE}} \longrightarrow \boxed{\text{LOCALIZE}} \longrightarrow \boxed{\text{PLAN}} \longrightarrow \boxed{\text{DECIDE}} \longrightarrow \boxed{\text{ACT}} \longrightarrow \boxed{\text{REASSESS}}$$

Every decision generates a verified **`DecisionRecord`** displayed in the GCS **AI Decision Panel**, presenting live numeric evidence (Time to Collision, Closest Point of Approach, obstacle range, confidence index, and rejected alternatives).

---

### 4. 🛰️ GPS-Denied Visual-Inertial Navigation
Simulates realistic operations when GNSS satellite fixes are jammed or unavailable:
- Switches state estimation from GNSS+Baro EKF to **Visual-Inertial Odometry (VIO)**.
- Models distance-proportional integration random walk, feature tracking confidence decay, and covariance expansion.
- Discloses honest demonstration provenance (`DataProvenance.Demonstration`) in accordance with academic integrity standards.

---

### 5. 🔬 Digital Twin Engineering & Exploded View (`F6`)
- Full component hierarchy: 4x 380KV brushless motors, 40A Opto ESCs, 15" carbon props, Pixhawk 2.4.8 autopilot, Raspberry Pi 5 companion computer, 6S 22.2V LiPo battery, Mauch power module, and telemetry transceiver.
- Interactive **Exploded View** animating component separation along radial vectors.
- **Transparent X-Ray** and **Wireframe** modes for internal avionics and wiring inspection.

---

### 6. 🎬 18-Step Automated Academic Presentation (`F8`)
One-key automated presentation sequence orchestrating an end-to-end demonstration from splash screen, airframe inspection, autonomous takeoff, dynamic obstacle avoidance, perception view toggling, GPS-denied navigation, to precision landing.

---

## 🏗️ 9-Layer System Architecture

```
                                  +---------------------------------------+
                                  |     Ground Control Station (GCS)      |
                                  |   (Tactical HUD, AI Decision Panel)   |
                                  +-------------------+-------------------+
                                                      |
+-----------------------------------------------------v-----------------------------------------------------+
|                                          AstraEvents Bus                                                  |
|                                & Service Registry (AstraServices)                                         |
+-----------+-------------------------+-------------------------+-------------------------+-----------------+
            |                         |                         |                         |
+-----------v-----------+ +-----------v-----------+ +-----------v-----------+ +-----------v-----------+
|  Layer 3: Sensors &   | |   Layer 4: Perception | |  Layer 5: Navigation  | | Layer 6: Autonomy &     |
|     Localization      | |  (LiDAR / CPA / TTC)  | | (Margasoochi D* Lite) | |    Mission Executive    |
| • IMU / Baro / GNSS   | | • Multi-beam Scanner  | | • Incremental Replan  | | • 7-Stage Decision Loop |
| • Visual Odometry VIO | | • 3D Occupancy Grid   | | • Catmull-Rom Spline  | | • Waypoint Sequencer    |
| • Telemetry Provider  | | • Threat Analyzer     | | • Avoidance Evaluator | | • Battery/Tilt Failsafe |
+-----------+-----------+ +-----------+-----------+ +-----------+-----------+ +-----------+-----------+
            |                         |                         |                         |
+-----------v-------------------------v-------------------------v-------------------------v-----------------+
|                                 Layer 2: Flight Control System (Cascaded PID)                             |
|                           (Position Loop -> Velocity Loop -> Attitude Loop -> Rate Loop)                  |
+-----------------------------------------------------+-----------------------------------------------------+
                                                      |
                                       +--------------v--------------+
                                       | Layer 2: Quadcopter Physics |
                                       |     & Motor Mixing Matrix   |
                                       +-----------------------------+
```

| Layer | Module | Key Classes | Description |
|---|---|---|---|
| **Layer 1** | **Core Architecture** | `AstraEvents`, `AstraServices`, `GeoMath`, `GeoReference`, `EventLog` | Event bus dispatch with exception isolation, service registry, WGS84 geodesic conversions. |
| **Layer 2** | **Digital Twin Dynamics** | `AirframeBuilder`, `QuadcopterPhysics`, `FlightControlSystem`, `MotorUnit` | Tarot 650 physics model, 4-loop cascaded PID, thrust/drag/moment integration. |
| **Layer 3** | **Sensors & Localization** | `SimulatedSensorSuite`, `GnssBaroLocalizationProvider`, `VisualInertialLocalizationProvider` | Specific force IMU synthesis, Baro hypsometric altitude, GNSS jamming, VIO drift. |
| **Layer 4** | **Perception & Tracking** | `RaycastObstacleDetector`, `CollisionPredictor`, `ThreatAnalyzer`, `OccupancyGrid` | 3D LiDAR swept volume, vector CPA/TTC prediction, 3D voxel grid with $8\,\text{m}$ margin. |
| **Layer 5** | **Margasoochi Planning** | `MargasoochiDStarLite`, `AStarPlanner`, `TrajectorySmoother`, `AvoidanceController` | D* Lite incremental graph repair, Catmull-Rom smoothing, multi-candidate avoidance. |
| **Layer 6** | **Autonomy Executive** | `MissionManager`, `AutonomyController`, `FailsafeManager` | 7-stage Sense-Perceive-Localize-Plan-Decide-Act-Reassess loop, failsafes. |
| **Layer 7** | **Dual Synchronized Views** | `OfflineMapProvider`, `CesiumMapProvider`, `MapManager`, `PerceptionViewController` | Mode A 3D city geospatial map, Mode B monochrome machine-perception overlay. |
| **Layer 8** | **GCS & Engineering View** | `GcsManager`, `CameraController`, `EngineeringViewController` | Tactical HUD, AI reasoning panel, 6 camera rigs, digital twin exploded & X-ray views. |
| **Layer 9** | **Presentation & Setup** | `PresentationController`, `BenchmarkRunner`, `SceneGenerator` | Automated 18-step presentation sequence (`F8`), Margasoochi benchmark runner. |

---

## 🎮 Flight Modes & Controls

### Manual Flight Controls
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

### Operating Modes & Visualization Toggles
| Hotkey | Function | Details |
|---|---|---|
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

## 💻 Ground Control Station (GCS)

```
+---------------------------------------------------------------------------------------------------+
| [ASTRA UAV GCS v2.4]  [AUTONOMOUS] NAVIGATING   GPS: 3D FIX (14 SAT)   BATT: 94% (22.2V)   T+ 42.5s |
+---------------------------------------------------------------------------------------------------+
| PRIMARY TELEMETRY         |                                       | AI DECISION PIPELINE          |
| Pitch: 4.2° Roll: -1.1°   |                                       | SENSE>PERCEIVE>LOCALIZE>PLAN  |
| Alt: 35.0m AGL (955m MSL) |                                       | DECIDE: AVOID (LATERAL)       |
| Speed: 8.2 m/s (29.5 km/h)|                                       | Reason: Obstacle at 24m,      |
| Motor 1-4: 6,420 RPM      |                                       | TTC 2.4s. Clearing Starboard. |
| Loc: GNSS+BARO+IMU (98%)  |                                       | Confidence: 94% | 1.8ms       |
| [ARM] [TAKEOFF] [LAND]    |                                       | ----------------------------- |
+---------------------------+                                       | MISSION PLANNER               |
| SYSTEM HEALTH MATRIX      |                                       | Target Lat: 13.0850           |
| Flight Controller  [OK]   |                                       | Target Lon: 77.5900           |
| Motors & ESCs      [OK]   |                                       | Cruise Alt: 35.0m             |
| 6S LiPo Battery    [OK]   |                                       | [START MISSION] [ABORT] [RTL] |
| 3D LiDAR Sensor    [OK]   |                                       +-------------------------------+
+---------------------------+-----------------------------------------------------------------------+
| MISSION EVENT LOG: [14:32:05] Obstacle detected at 24m. TTC 2.4s. Route replanned starboard.      |
+---------------------------------------------------------------------------------------------------+
```

---

## 🚀 Installation & Quickstart

### Prerequisites
- **Unity 6** (`6000.0.0f1` or later) with Universal 3D (URP) support.
- Windows 10/11 Desktop target.

### Step-by-Step Setup
1. **Clone the Repository**:
   ```bash
   git clone https://github.com/mevaibhavpandey/UAV-GPS-denied-Claude-AI.git
   ```
2. **Open in Unity Hub**:
   - Launch Unity Hub $\rightarrow$ **Add project from disk** $\rightarrow$ select `ASTRA-UAV`.
   - Template: **Universal 3D (URP)**.
3. **Generate Procedural Scene & Settings**:
   - In the top Unity menu, execute:
     1. `ASTRA > Setup > Configure Project Settings`
     2. `ASTRA > Build > Generate Demo Scene`
4. **Run Simulation**:
   - Open `Assets/ASTRA/Scenes/ASTRA_GCS_Demo.unity` in the Project view.
   - Click **Play** ▶️.
   - Press **`F8`** to watch the automated academic presentation or use `F1`–`F3` to fly manually and autonomously.

---

## 📂 Repository Structure

```
.
├── ASTRA-UAV/
│   ├── Assets/ASTRA/
│   │   ├── Editor/               # Procedural Airframe & Demo Scene Generators
│   │   ├── Scripts/
│   │   │   ├── Cameras/          # Multi-Rig Camera Director (6 Rigs)
│   │   │   ├── Contracts/        # Hardware & Subsystem Interface Contracts
│   │   │   ├── Core/             # Event Bus, Service Registry, Geodesy, Logging
│   │   │   ├── Diagnostics/      # Margasoochi vs A* Benchmark Runner
│   │   │   ├── Drone/            # Motor Units, Battery System & Digital Twin Parts
│   │   │   ├── Engineering/      # Exploded View & X-Ray Avionics Inspector
│   │   │   ├── Flight/           # 4-Loop Cascaded PID & Physics Model
│   │   │   ├── Localization/     # GNSS/Baro EKF & Visual-Inertial (GPS-Denied) Estimator
│   │   │   ├── Map/              # Offline Procedural City & Cesium 3D Tiles Providers
│   │   │   ├── Mission/          # Mission Manager, 7-Stage Autonomy Engine & Failsafes
│   │   │   ├── Navigation/       # Margasoochi D* Lite, A*, Dijkstra & Avoidance Controller
│   │   │   ├── Perception/       # 3D LiDAR Scanner, CPA/TTC Predictor, Occupancy Grid
│   │   │   ├── Presentation/     # 18-Step Automated Presentation Controller
│   │   │   └── UI/               # Ground Control Station HUD & Tactical Panels
│   │   └── Settings/             # UavConfiguration ScriptableObject Settings
│   ├── Packages/                 # Package Manifest (Input System, URP, Cinemachine)
│   └── SETUP.md                  # Detailed Setup & Troubleshooting Reference
├── Documentation/
│   ├── PROJECT_ARCHITECTURE.md   # Architectural Deep-Dive & Mathematical Formulations
│   ├── PROJECT_RECOVERY_STATUS.md# Forensic Audit & Layer Recovery Status
│   ├── USER_GUIDE.md             # Detailed Operator & Hotkey Manual
│   ├── DEMO_GUIDE.md             # Academic & Committee Demonstration Script
│   ├── KNOWN_LIMITATIONS.md      # Honest Technical Bounds & Disclosures
│   ├── FUTURE_HARDWARE_INTEGRATION.md # Pixhawk, ROS 2, and Raspberry Pi 5 Bridge Specs
│   └── LAYER_STATUS.md           # 9-Layer Implementation Verification Matrix
├── LICENSE                       # MIT License
└── README.md                     # Master Repository Readme
```

---

## 📖 Documentation Index

- 📘 [**Project Architecture Deep-Dive**](Documentation/PROJECT_ARCHITECTURE.md)
- 📋 [**Layer Recovery & Forensic Status**](Documentation/PROJECT_RECOVERY_STATUS.md)
- 🕹️ [**Operator & User Guide**](Documentation/USER_GUIDE.md)
- 🎓 [**Academic Demonstration Guide**](Documentation/DEMO_GUIDE.md)
- ⚖️ [**Known Limitations & Academic Disclosures**](Documentation/KNOWN_LIMITATIONS.md)
- 🔌 [**Future Hardware Integration (Pixhawk / ROS 2)**](Documentation/FUTURE_HARDWARE_INTEGRATION.md)
- 📊 [**Complete 9-Layer Status Matrix**](Documentation/LAYER_STATUS.md)

---

## 👥 Organization & Credits

- **Project Lead**: Vaibhav Pandey
- **Institution**: BMS Institute of Technology & Management (BMSIT&M)
- **Organization**: ASTRA (Armed Squad for Tactical Readiness and Awareness)
- **Supervision & Review**: Department of Artificial Intelligence & Machine Learning / Robotics

---

<div align="center">
  <sub>Developed for the ASTRA UAV Autonomous Flight & GPS-Denied Research Initiative.</sub>
</div>
