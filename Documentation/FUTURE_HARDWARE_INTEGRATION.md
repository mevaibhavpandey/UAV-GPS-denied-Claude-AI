# ASTRA UAV — Future Hardware & Flight Avionics Integration

## 1. Hardware Integration Architecture

ASTRA is architected with clean interfaces to ensure the transition from simulation to physical UAV hardware requires zero restructuring of the ground station, mission executive, or path planning modules.

```
                           +----------------------+
                           |  GCS & Mission Plan  |
                           +----------+-----------+
                                      |
                           +----------v-----------+
                           |  Autonomy Controller |
                           +----------+-----------+
                                      |
                     +----------------v----------------+
                     |        MAVLink Bridge           |
                     | (ROS 2 / MAVROS / micro-XRCE)   |
                     +----------------+----------------+
                                      |
          +---------------------------+---------------------------+
          |                                                       |
+---------v----------+                                  +---------v----------+
|   Raspberry Pi 5   | <== I2C / UART / USB / CSI ==>   |   Pixhawk 2.4.8    |
| - ROS 2 Humble     |                                  | - ArduPilot Copter |
| - Depth Camera     |                                  | - 4x ESCs & Motors |
| - Margasoochi Plan |                                  | - 6S LiPo & Sensors|
+--------------------+                                  +--------------------+
```

---

## 2. Core Interfaces for Hardware Transition

| Interface | Simulation Implementation | Physical Hardware Target |
|---|---|---|
| `IFlightController` | `FlightControlSystem.cs` (Cascaded PID) | `MavlinkFlightController` sending `SET_POSITION_TARGET_LOCAL_NED` |
| `ISensorProvider` | `SimulatedSensorSuite.cs` | Pixhawk `RAW_IMU`, `GPS_RAW_INT`, `SCALED_PRESSURE` over MAVLink |
| `ILocalizationProvider` | `VisualInertialLocalizationProvider.cs` | Real-time ROS 2 VINS-Fusion / RTAB-Map node |
| `ITelemetryProvider` | `TelemetryProvider.cs` | MAVLink v2 telemetry stream to Ground Station |
| `IObstacleDetector` | `RaycastObstacleDetector.cs` | Solid-state LiDAR / Stereo Depth Camera (Intel RealSense D435i) |

---

## 3. Physical Hardware Bill of Materials (Tarot 650 Class)
- **Airframe**: Tarot 650 Sport Quadcopter (650mm carbon fiber wheelbase, folding landing gear).
- **Propulsion**: 4x 380–400KV Brushless Motors, 4x 40A Opto ESCs, 15x5.5 Carbon Fiber Propellers.
- **Flight Controller**: Pixhawk 2.4.8 / Pixhawk 6C running ArduPilot Copter 4.5+.
- **Onboard Companion Computer**: Raspberry Pi 5 (8GB RAM) running Ubuntu Linux & ROS 2 Humble.
- **Primary Power**: 6S 22.2V 10,000–16,000mAh High-Discharge LiPo Battery with Mauch Precision Power Module.
- **Perception Sensors**: Intel RealSense D435i Depth Camera + Lightware SF45/B 2D/3D scanning LiDAR.
- **Communications**: Holybro 500mW 915MHz Telemetry Radio + ELRS 2.4GHz RC Receiver.
