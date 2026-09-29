# GPS-Denied Navigation (ASTRA spec Sec 16–19)

> HONESTY: the GPS-denied estimator is a **DEMONSTRATION** visual-inertial estimator
> (`Localization/VisualInertialLocalizationProvider.cs`), NOT production VIO or SLAM and NOT a
> flight-certified navigation filter. It shows the architecture and the failure/drift behaviour,
> not hardware-grade accuracy.

## How it is engaged

The pre-flight `Mission/MissionSetupManager` offers three navigation modes:

- **GPS-Assisted** — `GnssBaroLocalizationProvider` active; GNSS + barometer localize the vehicle.
- **GPS-Denied (VIO)** — GNSS disabled at launch; localization runs on the demonstration
  visual-inertial estimator.
- **Manual** — operator flies with the sticks; autonomy disengaged.

On commit, `MissionSetupManager` sets `mission.SimulateGpsDenial` and calls
`ISensorProvider.SetGpsEnabled(false)` for GPS-denied runs. `SensorFusionManager` then reports GNSS
as non-contributing and `ActiveEstimator` as the visual-inertial provider (see `Sensor_Fusion.md`).

## Behaviour to expect

Without GNSS corrections the demonstration estimator dead-reckons from IMU + visual cues and will
**drift** over time — this is intentional and honest. The point of the mode is to show the system
degrading gracefully and the operator/telemetry surfacing the switch, not to claim centimetre
accuracy without GPS.

## Reproducibility

`Core/ScenarioSeed.Apply()` is called at run commit so the simulated sensor noise and dropouts
(which drive the drift) are reproducible for a given seed — useful when comparing GPS-assisted vs
GPS-denied runs in the `ExperimentPanel` (F11).

**Verify in the Unity Editor:** fly a GPS-denied mission and observe drift vs the GPS-assisted case.
