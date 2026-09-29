# Digital Twin (ASTRA spec Sec 9–11)

> SIMULATED. The airframe is a Unity physics model, not a hardware-calibrated twin. Mass, thrust
> and drag values are plausible simulation parameters, not measured device specifications.

## Composition

The quadcopter twin is authored in the Unity scene/prefab and driven by:

- `Drone/MotorUnit.cs` — per-motor thrust unit (4×), with prop visual.
- `Drone/UavComponent.cs` — component tagging/metadata for the exploded / X-ray inspection view.
- `Drone/DroneVisualEnhancer.cs` — visual detailing of the airframe.
- `Flight/QuadcopterPhysics.cs` — rigid-body dynamics driven by the four motor thrusts.
- `Flight/FlightControlSystem.cs` — cascaded PID flight controller, arm/takeoff/land, control-source
  switching (Manual / Autonomous).
- `Power/BatterySystem.cs` — simulated battery drain feeding the failsafe layer.

> CORRECTION: earlier status docs referenced an `AirframeBuilder.cs`. **No such file exists.** The
> airframe is scene/prefab-authored plus the `Drone/*` components above. Do not regenerate a builder
> to "match" the old doc.

## Inspection view

`EngineeringViewController` (F6) presents the twin in Normal / Exploded / X-Ray modes using the
`UavComponent` tags, so evaluators can inspect the frame, arms, motors, props, battery, flight
controller, compute, GPS, camera and LiDAR mounting points.

## D11 — hierarchy verification (Editor-only)

Confirming the full physical hierarchy (frame / 4× arm-motor-prop / battery / FC / compute / GPS /
camera / LiDAR / ESC / landing gear) and its transforms **requires the Unity Editor** and cannot be
completed in the headless sandbox. This item is flagged Editor-only in the recovery plan: open the
scene/prefab, verify each node is present and correctly parented, and add any missing mount points.

**Verify in the Unity Editor:** open the drone prefab and walk the hierarchy against the spec list.
