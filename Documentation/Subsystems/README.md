# ASTRA Subsystem Documentation (Sec 67)

Named subsystem docs required by the spec. All describe the SIMULATED research platform; each notes
what is verified structurally vs what still needs the Unity Editor.

- [Mission_System.md](Mission_System.md) — pre-flight setup, mission types, 7-stage autonomy loop
- [Map_System.md](Map_System.md) — offline stylized vs Cesium photoreal backends, geo anchoring
- [LiDAR_System.md](LiDAR_System.md) — configurable raycast LiDAR model, noise/dropout, stats
- [Sensor_Fusion.md](Sensor_Fusion.md) — status aggregator over the sensor channels (not a re-fusion)
- [Path_Planning.md](Path_Planning.md) — D* Lite / A* / Dijkstra / Theta*, external cost layer, benchmark
- [Obstacle_Avoidance.md](Obstacle_Avoidance.md) — perception pipeline + honest thin-obstacle limit
- [GPS_Denied.md](GPS_Denied.md) — demonstration visual-inertial estimator and its drift
- [Threat_Zone_Simulation.md](Threat_Zone_Simulation.md) — no-go/soft-avoid/advisory zones + cost layer
- [Digital_Twin.md](Digital_Twin.md) — airframe composition; D11 hierarchy verification is Editor-only
- [Testing.md](Testing.md) — what is structurally verified vs the Editor/runtime checklist
