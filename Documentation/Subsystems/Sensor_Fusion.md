# Sensor Fusion (ASTRA spec Sec 16)

> HONESTY: `Sensors/SensorFusionManager.cs` is a **status aggregator**, not a new state estimator.
> The fused pose it exposes is a pass-through of whatever `ILocalizationProvider` is active
> (`GnssBaroLocalizationProvider` in GPS-assisted mode, `VisualInertialLocalizationProvider` in
> GPS-denied mode). It makes the contributing sensor set explicit for the panel and docs; it does
> not itself run a Kalman/factor-graph fusion.

## What it does

`SensorFusionManager` (namespace `Astra.Sensors`, self-installing) reads the sensor providers each
`FixedUpdate` and reports, per channel, a `SensorChannel { Name, Contributing, Status, Provenance,
Detail }`:

| Channel | Source | Contributes when |
|---|---|---|
| IMU | `ISensorProvider.ReadImu` | always (dead-reckoning backbone) |
| GNSS | `ISensorProvider.ReadGps` / `GpsAvailable` | GPS enabled and fix valid |
| Barometer | `ISensorProvider.ReadBarometer` | always |
| Magnetometer | `ISensorProvider.ReadMagnetometer` | always |
| LiDAR | `LidarSensor` (registered) | sensor present and returning points |
| Camera | `IObstacleDetector` / `PayloadCamera` | present |

It also exposes `FusedPose` (pass-through), `ContributingChannelCount`, and `ActiveEstimator`
(the name of the live localization provider). The `SensorPanel` (F10) renders these channels with
green / amber / grey status dots.

## Why a pass-through and not a re-fusion

The localization providers already own the estimate. Re-fusing on top would either duplicate their
work or silently disagree with the authoritative pose the flight stack uses — both dishonest. The
aggregator instead answers "which sensors are currently feeding the estimate, and are they
healthy?", which is what the panel and evaluators need.

## GPS-denied behaviour

When GPS is disabled at launch (nav mode GPS-Denied), GNSS drops to non-contributing and
`ActiveEstimator` becomes the visual-inertial provider. See `GPS_Denied.md`. That provider is a
**demonstration** estimator, not production VIO/SLAM.
