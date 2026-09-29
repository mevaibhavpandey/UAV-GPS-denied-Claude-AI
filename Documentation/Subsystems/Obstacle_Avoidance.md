# Obstacle Avoidance (ASTRA spec Sec 22–24)

> SIMULATED. All detection is raycast-based inside the Unity scene.

## Pipeline

The autonomy loop's SENSE→PERCEIVE stages feed avoidance:

- `Perception/LidarSensor.cs` (D3) casts the configurable beam grid and produces a point cloud +
  nearest-return distance (see `LiDAR_System.md`).
- `Perception/RaycastObstacleDetector.cs` (`IObstacleDetector`) classifies hits and remains the
  authoritative perception consumer.
- `Perception/OccupancyGrid.cs` inflates obstacles by a safety margin so "traversable" already
  implies clearance; planners route through it (see `Path_Planning.md`).
- `Navigation/AvoidanceController.cs` selects among candidate manoeuvres; `CollisionPredictor` /
  `ThreatAnalyzer` provide CPA/TTC context.

## Thin-obstacle honesty demo (D8)

`Perception/ThinObstacleDemo.cs` spawns thin wire cylinders (`wireDiameterM = 0.02`, named `Wire_n`
so `ClassifyCollider` tags them `Wire`) across the flight area and reports, per wire, a
`WireInfo { Name, DiameterM, Midpoint, GuaranteedDetectRangeM }`.

The guaranteed-detection range is computed honestly from the LiDAR's angular resolution:

```
beamSpacingRad     = radians(HorizontalFovDeg / HorizontalResolution)
GuaranteedDetectRangeM = wireDiameterM / beamSpacingRad
```

Beyond that range, adjacent beams are spaced wider than the wire is thick, so the wire can pass
**between** beams and is not guaranteed to be detected. `ReportDetectionLimits()` logs this as an
`EventLog.Warning` rather than implying perfect wire detection. This is a deliberate honesty
demonstration of a real sensing limitation, not a defect.

**Verify in the Unity Editor:** spawn the demo, fly toward the wires, and confirm detection behaves
as the reported range predicts.
