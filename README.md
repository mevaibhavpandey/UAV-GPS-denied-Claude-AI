# ASTRA UAV — Autonomous Quadcopter Digital Twin & Navigation Simulator

![ASTRA UAV](https://img.shields.io/badge/ASTRA-UAV%20Simulator-blue?style=for-the-badge)
![Unity 6](https://img.shields.io/badge/Unity-6000.0.0f1-black?style=for-the-badge&logo=unity)
![C#](https://img.shields.io/badge/C%23-9.0%20%2F%20.NET%20Standard%202.1-purple?style=for-the-badge&logo=csharp)
![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)

**ASTRA (Armed Squad for Tactical Readiness and Awareness)**  
*BMS Institute of Technology & Management*

---

## 🎯 Executive Overview

**ASTRA UAV** is a high-fidelity, physics-based Digital Twin, Ground Control Station (GCS), Mission Planning, Real-Time Obstacle Avoidance, and GPS-Denied Navigation simulation platform developed in **Unity 6 (Universal Render Pipeline)**.

The project is an engineering and research demonstration system showcasing future tactical autonomous UAV capabilities for academic, institutional, and technical review.

```
+-----------------------------------------------------------------------------------------------+
|                                      ASTRA UAV SIMULATOR                                      |
+-------------------------------+-------------------------------+-------------------------------+
|       DIGITAL TWIN & GCS      |      AUTONOMOUS NAVIGATION    |       GPS-DENIED SENSING      |
|  - Tarot 650 Carbon Airframe  |  - Margasoochi (D* Lite)      |  - Visual-Inertial Odometry   |
|  - Cascaded PID Flight Loops  |  - Real-Time LiDAR Avoidance  |  - IMU + Baro + Mag Fusion    |
|  - Exploded / X-Ray Views     |  - 7-Stage Autonomy Engine    |  - Position Drift Modeling    |
|  - Full Tactical GCS HUD      |  - Dynamic 3D Voxel Grid      |  - GPS Jamming / Dropout Sim  |
+-------------------------------+-------------------------------+-------------------------------+
```

---

## 🚀 Key Features

1. **Dual Synchronized Visualization Modes**:
   - **Mode A (Real-World View)**: 3D photorealistic / procedural urban landscape with buildings, terrain, roads, and flight corridors. Supports live map backend toggling (`F9`) between Cesium ion 3D Tiles and offline procedural environments.
   - **Mode B (Perception View)**: Monochrome minimal machine-vision style rendering what the autonomy stack perceives—obstacle bounding volumes, LiDAR swept beams, point clouds, and position uncertainty ellipsoids (`F5`).

2. **Margasoochi (D* Lite) Incremental Path Planning**:
   - Supports initial 3D corridor planning and lightning-fast local search tree repairs when obstacles appear mid-flight, outperforming conventional A* and Dijkstra planners.

3. **Autonomous 7-Stage Decision Loop**:
   - Live continuous loop: $\text{SENSE} \rightarrow \text{PERCEIVE} \rightarrow \text{LOCALIZE} \rightarrow \text{PLAN} \rightarrow \text{DECIDE} \rightarrow \text{ACT} \rightarrow \text{REASSESS}$.
   - Live **AI Decision Panel** displaying verified numeric justifications, Time to Collision (TTC), Closest Point of Approach (CPA), and stage timings.

4. **GPS-Denied Navigation Demonstration**:
   - Simulates visual odometry, optical flow, and inertial dead reckoning during GNSS outage with realistic integration drift and honest academic disclosures (`F3`).

5. **Digital Twin Engineering Inspection**:
   - Inspect internal components: 6S LiPo battery, Pixhawk flight controller, Raspberry Pi 5, ESCs, brushless motors, wiring, and center of gravity in Normal, Exploded, and X-Ray views (`F6`).

6. **Automated One-Touch Demonstration Mode**:
   - Press **`F8`** to execute an automated 18-step end-to-end mission demonstration from splash screen to precision landing and mission report.

---

## 🎮 Flight Controls & Hotkeys

| Hotkey | Function |
|---|---|
| `R` / `F` | **Arm** / **Disarm** Propulsion Motors |
| `W` / `S` | Pitch Forward / Pitch Backward |
| `A` / `D` | Roll Left / Roll Right |
| `Q` / `E` | Yaw Left / Yaw Right |
| `Space` / `Ctrl` | Altitude Climb / Altitude Descent |
| `H` / `L` | Hover Hold / Automated Controlled Landing |
| `F1` | **Manual Flight Mode** |
| `F2` | **Autonomous GPS Mode** |
| `F3` | **Autonomous GPS-Denied Mode** (Visual-Inertial Odometry) |
| `F5` | **Toggle View Mode** (Real-World $\leftrightarrow$ Perception Mode B) |
| `F6` | **Toggle Engineering View** (Digital Twin Exploded / X-Ray) |
| `F8` | **Automated 18-Step Academic Presentation Sequence** |
| `F9` | **Switch Map Backend** (Cesium 3D Tiles $\leftrightarrow$ Offline Procedural City) |
| `Tab` | **Cycle Camera Rig** (Chase $\rightarrow$ FPV $\rightarrow$ Orbit $\rightarrow$ TopDown $\rightarrow$ Engineering $\rightarrow$ Cinematic) |
| `Esc` | **Abort Active Mission** |

---

## 🛠️ Installation & Setup

1. Clone repository:
   ```bash
   git clone https://github.com/mevaibhavpandey/UAV-GPS-denied-Claude-AI.git
   ```
2. Open the project in **Unity 6 (6000.0.0f1)** with Universal Render Pipeline (URP).
3. In the Unity Editor menu bar, execute:
   - `ASTRA > Setup > Configure Project Settings`
   - `ASTRA > Build > Generate Demo Scene`
4. Open `Assets/ASTRA/Scenes/ASTRA_GCS_Demo.unity` and click **Play**.

---

## 📂 Project Structure

```
ASTRA-UAV/
├── Assets/ASTRA/
│   ├── Editor/           # Procedural Airframe & Scene Generators
│   ├── Scripts/
│   │   ├── Cameras/      # Multi-rig Camera Controller (6 Rigs)
│   │   ├── Contracts/    # Subsystem Interfaces & Types
│   │   ├── Core/         # Event Bus, Service Registry, Geodesy, Logging
│   │   ├── Diagnostics/  # Margasoochi vs A* Benchmark Runner
│   │   ├── Drone/        # Motors, Battery & Digital Twin Components
│   │   ├── Engineering/  # Exploded View & X-Ray Inspector
│   │   ├── Flight/       # 4-Loop Cascaded PID & Physics Model
│   │   ├── Localization/ # GNSS/Baro & Visual-Inertial (GPS-Denied) Estimators
│   │   ├── Map/          # Offline Procedural City & Cesium Providers
│   │   ├── Mission/      # Mission Manager, 7-Stage Autonomy Engine & Failsafes
│   │   ├── Navigation/   # Margasoochi D* Lite, A*, Dijkstra, Avoidance
│   │   ├── Perception/   # LiDAR Obstacle Tracker, CPA/TTC Predictor, Occupancy Grid
│   │   ├── Presentation/ # 18-Step Automated Presentation Controller
│   │   └── UI/           # Ground Control Station HUD & Tactical Panels
├── Documentation/        # Comprehensive Technical & Academic Documentation
├── Packages/             # Unity 6 Package Manifest
└── README.md
```

---

## 📄 Documentation Index
- [Architecture Deep-Dive](Documentation/PROJECT_ARCHITECTURE.md)
- [Layer Recovery Status](Documentation/PROJECT_RECOVERY_STATUS.md)
- [Operator & User Guide](Documentation/USER_GUIDE.md)
- [Demonstration Guide](Documentation/DEMO_GUIDE.md)
- [Known Limitations & Disclosures](Documentation/KNOWN_LIMITATIONS.md)
- [Future Hardware Integration](Documentation/FUTURE_HARDWARE_INTEGRATION.md)
- [Complete 9-Layer Status](Documentation/LAYER_STATUS.md)

---

## 👥 Organization & Credits
- **Project Lead**: Vaibhav Pandey
- **Institution**: BMS Institute of Technology & Management
- **Organization**: ASTRA (Armed Squad for Tactical Readiness and Awareness)
