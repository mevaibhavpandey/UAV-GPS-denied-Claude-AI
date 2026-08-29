# ASTRA UAV — Operator & User Guide

## 1. Quick Start Guide
1. Open the project in **Unity 6 (6000.0.0f1)**.
2. In the top Unity menu bar, select:
   - `ASTRA > Setup > Configure Project Settings`
   - `ASTRA > Build > Generate Demo Scene`
3. Open `Assets/ASTRA/Scenes/ASTRA_GCS_Demo.unity` and press **Play**.

---

## 2. Flight Controls & Hotkeys

### Manual Flight Controls
| Key | Action |
|---|---|
| `R` | **Arm** Motors (runs preflight checks) |
| `F` | **Disarm** Motors |
| `W` / `S` | Pitch Forward / Pitch Backward |
| `A` / `D` | Roll Left / Roll Right |
| `Q` / `E` | Yaw Left / Yaw Right |
| `Space` | Increase Throttle / Automated Climb |
| `Left Ctrl` | Decrease Throttle / Descent |
| `H` | Altitude / Position Hover Hold |
| `L` | Automated Controlled Land |

### System Modes & Visualization Toggles
| Key | Action |
|---|---|
| `F1` | **Manual Flight Mode** |
| `F2` | **Autonomous GPS Mode** |
| `F3` | **Autonomous GPS-Denied Mode** (Visual-Inertial Odometry) |
| `F5` | **Toggle Real-World vs Perception View** (Mode A $\leftrightarrow$ Mode B) |
| `F6` | **Toggle Digital Twin Engineering View** (Exploded/X-Ray) |
| `F8` | **Start Automated 18-Step Presentation Sequence** |
| `F9` | **Switch Map Backend** (Cesium 3D Tiles $\leftrightarrow$ Offline Procedural City) |
| `Tab` | **Cycle Camera Rig** (Chase $\rightarrow$ FPV $\rightarrow$ Orbit $\rightarrow$ TopDown $\rightarrow$ Engineering $\rightarrow$ Cinematic) |
| `Esc` | **Abort Active Mission** |

---

## 3. Mission Planning Workflow
1. Open the **Mission Planner** panel on the right sidebar.
2. Enter target Latitude/Longitude or click on the 3D map environment.
3. Review computed great-circle distance, bearing, and cruise altitude.
4. Click **Start Mission** to initiate automated arming, climb, corridor navigation, dynamic obstacle avoidance, and target arrival.
