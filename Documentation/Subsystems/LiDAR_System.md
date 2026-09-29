# LiDAR System (ASTRA spec Sec 13–15, 54)

> SIMULATED sensor. `Perception/LidarSensor.cs`, `DataProvenance.Simulated`. The scan is a Unity
> raycast model, not a calibrated hardware LiDAR; noise and dropout are pseudo-random models, not
> measured device characteristics.

## Model

`LidarSensor` casts a beam grid each scan defined by configurable optics:

- `MaxRangeM` / `MinRangeM` — usable range window
- `HorizontalFovDeg` / `VerticalFovDeg` — angular coverage
- `HorizontalResolution` / `VerticalChannels` — beams per scan
- `ScanRateHz` — scans per second

Imperfection model (all pseudo-random, seeded by `Core/ScenarioSeed` for reproducibility):

- `RangeNoiseSigmaM` — Gaussian (Box–Muller) range noise added to each return
- `DropoutProbability` — chance a beam returns nothing
- `LowerBlindConeDeg` — beams inside the downward cone are skipped (models a body-mounted blind spot)

Beams that would hit the vehicle's own airframe are ignored. Each scan updates live stats:
`LastPointCount`, `LastBeamsCast`, `LastScanDurationMs`, `LastReturnRatePercent`, `NearestReturnM`,
and an `IReadOnlyList<Vector3> PointCloud`.

## Integration

- Self-installs onto the `FlightControlSystem` GameObject via `RuntimeInitializeOnLoadMethod`
  (no scene editing) and registers itself with `AstraServices.Register<LidarSensor>(this)`.
- The existing `RaycastObstacleDetector` remains the perception consumer; the LiDAR model is
  additive and does not replace it (Sec 53).
- Config and live stats are surfaced read-only in the `SensorPanel` (F10); noise σ and dropout are
  adjustable live from the `ExperimentPanel` (F11).

## Detection limits (honest)

`Perception/ThinObstacleDemo.cs` demonstrates the geometric detection limit: guaranteed-detection
range for a thin obstacle ≈ `wireDiameterM / beamSpacingRad`, where `beamSpacingRad` derives from
`HorizontalFovDeg / HorizontalResolution`. Beyond that range a thin wire can pass between beams and
is **not** guaranteed to be detected. This is logged as a warning rather than pretending perfect
detection. See `Obstacle_Avoidance.md`.

**Verify in the Unity Editor:** the raycast scan and point counts require a running scene.
