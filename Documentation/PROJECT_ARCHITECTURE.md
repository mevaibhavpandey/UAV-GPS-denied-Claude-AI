# ASTRA UAV — Project Architecture

## 1. Architectural Overview

```
                      +-----------------------------------+
                      |      Ground Control Station       |
                      |   (GcsManager, HUD, AI Panels)    |
                      +-----------------+-----------------+
                                        |
+---------------------------------------v---------------------------------------+
|                               AstraEvents Bus                                 |
|                       & Service Registry (AstraServices)                      |
+-------+--------------------+---------------------+--------------------+-------+
        |                    |                     |                    |
+-------v-------+    +-------v-------+     +-------v-------+    +-------v-------+
|  Sensors &    |    |  Perception   |     | Navigation &  |    | Autonomy &    |
| Localization  |    | (LiDAR / CPA) |     | Margasoochi   |    | Mission Exec  |
+---------------+    +---------------+     +---------------+    +---------------+
        |                    |                     |                    |
+-------v--------------------v---------------------v--------------------v-------+
|                    Flight Control System (Cascaded PID)                       |
+---------------------------------------+---------------------------------------+
                                        |
                         +--------------v--------------+
                         | Quadcopter Physics & Motors |
                         +-----------------------------+
```

## 2. Core Subsystems

### 2.1 Sensor Simulation & State Estimation (Layer 3)
- **`SimulatedSensorSuite`**: Implements `ISensorProvider`. Generates physical accelerometer specific forces, angular velocities with gyro bias, barometer pressure altitudes, magnetometer headings, and GNSS fixes with configurable jamming/dropouts (`SetGpsEnabled`).
- **`GnssBaroLocalizationProvider`**: Fuses GNSS + Barometer + IMU for nominal low-drift estimation.
- **`VisualInertialLocalizationProvider`**: Demonstrates GPS-denied visual odometry with distance-proportional integration drift and honest `DataProvenance.Demonstration` badge.
- **`TelemetryProvider`**: Central aggregator publishing `TelemetrySnapshot` to GCS.

### 2.2 Perception & Threat Assessment (Layer 4)
- **`RaycastObstacleDetector`**: Multi-beam 3D LiDAR scanning up to 60m range, tracking static and dynamic obstacles with finite-difference velocity calculation.
- **`CollisionPredictor`**: Calculates Closest Point of Approach (CPA) and Time to Collision (TTC) using relative trajectory vectors.
- **`OccupancyGrid`**: 3D voxel grid with obstacle inflation by safety margin ($8\,\text{m}$) and 26-connectivity neighbour search.

### 2.3 Navigation & Path Planning (Layer 5)
- **`MargasoochiDStarLite`**: Incremental D* Lite path planning algorithm that repairs existing search trees rapidly upon obstacle appearance.
- **`AStarPlanner` & `DijkstraPlanner`**: Complete re-search baselines for comparative evaluation.
- **`TrajectorySmoother`**: Line-of-sight spherecast shortcutting with Catmull-Rom cubic spline interpolation.
- **`AvoidanceController`**: Multi-candidate lateral (Starboard/Port) and vertical climb avoidance evaluator.

### 2.4 Autonomy Loop & Mission Management (Layer 6)
- **Autonomy Cycle**: Executes continuous 7-stage loop:
  $$\text{SENSE} \longrightarrow \text{PERCEIVE} \longrightarrow \text{LOCALIZE} \longrightarrow \text{PLAN} \longrightarrow \text{DECIDE} \longrightarrow \text{ACT} \longrightarrow \text{REASSESS}$$
- Formulates verified `DecisionRecord` with genuine numeric justifications, confidence metrics, and stage execution timings.

### 2.5 Mapping & Dual Synchronized Views (Layer 7)
- **Mode A (Real World View)**: 3D urban terrain, procedural buildings, roads, flight trails. Supports runtime switching between Cesium ion and Offline procedural city (`F9`).
- **Mode B (Perception View)**: Monochrome minimal machine-perception aesthetic displaying tracked obstacle bounding volumes, LiDAR rays, and uncertainty ellipsoids (`F5`).

### 2.6 Digital Twin & Ground Control Station (Layer 8)
- **Digital Twin**: Accurate 650mm wheelbase Tarot-inspired geometry with brushless motors, ESCs, Pixhawk, Raspberry Pi 5, battery, and avionics.
- **Engineering Inspection**: Exploded view separation along radial vectors, Transparent X-Ray view, and Component Specification inspector (`F6`).
- **Tactical GCS**: Complete HUD, telemetry display, target coordinate selector, AI decision panel, and subsystem health matrix.
