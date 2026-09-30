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

## 5. Runtime Behaviour Not Verified In This Environment
- **Status**: `SIMULATED` — structural validation only.
- **Limitation**: The development sandbox has no Unity Editor, no C#/.NET compiler and no package network. Code here passes a structural delimiter/lexer check (`Tools/validate_cs.py`), which is NOT a compile or type check. Flight stability, autonomous navigation success, planner timing and frame rate must be confirmed by running the project in the Unity Editor on target hardware. No frame-rate, timing or flight-success figure in this repository is a runtime measurement.

## 6. Geodetic Origin Is Approximate
- **Status**: `SIMULATED` / UNVERIFIED.
- **Limitation**: The BMSIT&M site origin (≈13.1320°N, 77.5670°E, ≈890 m) is approximate and unverified; it is labelled as such in `GeoReference` and the GCS header. The local ENU conversion is a tangent-plane approximation (error grows with range: ~0.5 m at 10 km); above ~50 km the exact ECEF path should be used.

## 7. Floating-Origin Rebasing Is Range-Gated
- **Status**: `REAL IMPLEMENTATION` (additive), untriggered in the campus demo.
- **Limitation**: `FloatingOriginManager` preserves float precision on large (1–25 km) missions by translating the world and origin together. Its default 2 km threshold means it never fires in the current few-hundred-metre demo, so its large-map behaviour must be verified in the Editor with a long synthetic mission.
