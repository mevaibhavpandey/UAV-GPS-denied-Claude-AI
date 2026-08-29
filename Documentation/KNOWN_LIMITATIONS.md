# ASTRA UAV — Known Limitations & Honest Technical Disclosures

In adherence to strict academic and research integrity standards, this simulator does not overstate its maturity or claim flight-certified capability. All simulated systems are honestly labeled with their respective provenance badges (`SIMULATED`, `DEMONSTRATION`, `HARDWARE`, `FUTURE HARDWARE`).

---

## 1. Visual SLAM & VIO Representation
- **Status**: Tagged as `DEMONSTRATION`.
- **Limitation**: The current GPS-Denied estimator simulates the kinematic properties of visual odometry (accumulating integration drift, feature tracking confidence, covariance expansion) rather than running real-time bundle adjustment or non-linear optimization over camera image frames.
- **Future Transition**: When real hardware is connected, an external ROS 2 node running ORB-SLAM3 or VINS-Fusion can satisfy `ILocalizationProvider` without altering downstream code.

## 2. Obstacle Classification
- **Status**: Tagged as `SIMULATED`.
- **Limitation**: Obstacle classification in the simulation relies on Unity collider tags/names rather than deep neural network bounding-box inference (e.g. YOLOv8). The path planner is intentionally engineered to rely on geometry, extents, and velocities rather than classification labels, ensuring algorithm validity.

## 3. Cesium Photorealistic 3D Tiles Network Dependency
- **Status**: Network & Ion Token dependent.
- **Limitation**: High-resolution photogrammetric tiles require an active internet connection and Cesium ion token.
- **Mitigation**: The system includes a built-in zero-dependency procedural 3D city provider (`OfflineMapProvider`), accessible seamlessly via `F9` at any time.

## 4. Aerodynamic & Ground Effect Approximations
- **Status**: Physics Simulation.
- **Limitation**: Rigid-body physics models motor thrust, drag, and gyroscopic moments with high fidelity, but does not perform full computational fluid dynamics (CFD) turbulence or ground-effect downwash recirculation.
