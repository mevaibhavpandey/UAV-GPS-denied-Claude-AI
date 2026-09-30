# Flight Control (ASTRA spec: stable flight, no parallel controllers)

> SIMULATED research platform. Every value below is a reading of the simulated dynamics model in
> the Unity Editor; nothing here is a hardware measurement. Live desired-vs-actual telemetry is
> available from the flight diagnostics recorder (F12; CSV via F7).

## One authoritative controller

`Flight/FlightControlSystem.cs` is the single `IFlightController`, registered in `AstraServices`.
There is deliberately no second controller: manual, autonomous and failsafe inputs all flow through
the same cascade and the same mixer, selected by `ControlSource` (`Manual` / `Autonomous` /
`Failsafe` / `None`). This is a hard architectural constraint — a parallel controller or a
drag-based "stabiliser" would mask instability rather than fix it, and is prohibited by the spec.

## Force-based dynamics (no kinematic teleporting)

`Flight/QuadcopterPhysics.cs` integrates real forces through the Unity `Rigidbody`. Each of the four
motors applies its thrust with `AddForceAtPosition` at the motor's arm location, so roll and pitch
moments **emerge from differential thrust** — there is no direct `AddTorque` for roll or pitch. Yaw
is the summed motor reaction torque. Aerodynamic drag is applied as an opposing force/torque. The
controller never writes the transform to move the aircraft; the only position write is the guarded
`ResetToPose` used for setup/teleport-to-start, never during flight.

## The cascade

Outer loops produce setpoints for inner loops; the innermost loops produce mixer commands:

- **Horizontal:** POSITION → VELOCITY (`_velocityEast`, `_velocityNorth`, limited to
  `maxLateralAccel`) → target roll/pitch ATTITUDE → RATE (`_rateRoll`, `_ratePitch`).
- **Vertical:** ALTITUDE → CLIMB RATE (`_climbRate`) → a throttle **offset from the DERIVED hover
  throttle** (`config.HoverThrottleFraction`, tilt-compensated by `hoverThrottle / cos(tilt)`), so
  the aircraft holds altitude at the physically-correct collective rather than a guessed constant.
- **Yaw:** heading → `_rateYaw`.

All loops are `PidController` instances (see below). Rate-loop output limits are `0.45` (roll/pitch)
and `0.30` (yaw); the climb-rate loop output limit is `0.45`.

## PID design (`Flight/PidController.cs`)

Each loop uses derivative-on-measurement (no derivative kick on setpoint steps), a low-pass filter
on the D term, and **conditional anti-windup**: integration is suppressed when the output is
saturated in the direction that would worsen the wind-up. When the attitude/rate mixer runs out of
authority the controller calls `BleedIntegral` on the affected loops, so integral accumulated during
saturation is shed in a mixer-aware way instead of lingering. The controller exposes `LastP/I/D`,
`LastError`, `LastOutput`, `WasSaturated` and `Integral` for each loop — these feed the diagnostics
recorder so tuning is done against measured behaviour.

## Arming, state machine and failsafe

Flight progresses through an explicit `FlightState` machine (Disarmed → Initialising → Preflight →
Armed → Takeoff → Hover → Navigating → … → Landing → MissionComplete, plus ObstacleDetected /
Avoiding / RejoiningRoute / TargetApproach / ReturnHome / Emergency / MissionAborted). Arming is
gated by pre-flight checks; a refused arm raises `ArmingRefused` with a reason, because a silent
refusal to arm is one of the most confusing failure modes of a real autopilot. A failsafe switches
`ControlSource` to `Failsafe`.

## Sign-convention caution (verified working)

The mixer and physics sign conventions are tuned and known-good (the working flight commit). They
must not be changed without a live Editor run to confirm, because an inverted sign is invisible to
structural validation and would invert a control axis. Flight-tuning code is therefore left
untouched by non-runtime work.

## Verify in the Unity Editor

Structural validation does not integrate the dynamics. In the Editor: arm and take off; open the
diagnostics recorder (F12) and confirm thrust-to-weight ≈ 1 in hover, climb-rate desired ≈ actual,
attitude target ≈ actual, and that loops are not chronically saturated; record a CSV (F7) for any
instability so a fix is made against data rather than a guess.
