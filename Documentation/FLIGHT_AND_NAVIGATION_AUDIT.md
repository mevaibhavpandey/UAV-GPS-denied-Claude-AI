# ASTRA UAV — Flight & Navigation Technical Audit (Phase A)

**Date:** 2026-09-30
**Scope:** Flight control, physics, coordinate/georeferencing, navigation, perception, map.
**Method:** Static reading of the current C# tree. **NOT** compiled or run — this sandbox has no
Unity Editor, no C#/.NET compiler and no runtime, so every claim below is a *code-reading* finding,
not an observed-behaviour finding. Runtime symptoms must be reproduced in the Unity Editor.

---

## 0. Headline finding (read this first)

The master repair prompt is written against the assumption of a *naive, broken* project — one that
flies by `transform.Translate`, has duplicate controllers, a hardcoded 0.5 hover throttle, degrees
used as navigation coordinates, scripted obstacle events, etc.

**The current ASTRA codebase does not match that assumption.** The large majority of the prompt's
architectural demands are *already implemented*, and implemented carefully. This audit therefore
separates:

- **(A) Already satisfied** — do not rewrite (Section 53).
- **(B) Genuine gaps** — safe to build without a runtime.
- **(C) Runtime-only** — the reported flight symptoms and every acceptance test; **cannot be done
  in this sandbox**, needs the Editor.

Misreading a working system as broken and rewriting it blind would almost certainly regress the
flight tuning that was previously stabilised (commit `5edae01`: rate-loop sign fix + removal of a
pendulum self-levelling torque). That is the single biggest risk here.

---

## A. What currently controls the UAV (answers to the prompt's A–J)

- **A. What controls the UAV:** `Flight/FlightControlSystem.cs` (1616 lines) is the *single*
  authority. It is the only `IFlightController`. No duplicate/parallel flight controller exists.
- **B. What calculates the route:** `Mission/AutonomyController.cs` requests plans from an
  `IPathPlanner` (D* Lite default; A*, Dijkstra, Theta* also registered) over
  `Perception/OccupancyGrid.cs`. Single planning path.
- **C. Lat/lon → Unity conversion:** `Core/Geo/GeoReference.cs` + `Core/Geo/GeoMath.cs`. A local
  ENU tangent-plane conversion around a configurable origin. Geographic and Unity frames are
  already separated — this is not the "degrees used as world coordinates" anti-pattern.
- **D. Altitude control:** cascade in `FlightControlSystem` — altitude error → target climb rate →
  `PidController _climbRate` → throttle offset from a *derived* hover throttle. Not open-loop.
- **E. Horizontal movement:** emergent. `ComputeHorizontalPositionHold` / velocity control produce
  a target tilt; `QuadcopterPhysics` tilts the thrust vector; the horizontal component accelerates
  the body. No direct horizontal position writes.
- **F. Yaw:** explicit reaction torque about the body-up axis in `QuadcopterPhysics.FixedUpdate`
  (correct — parallel thrust vectors produce no yaw moment), commanded via the rate loop.
- **G. Obstacle avoidance:** `Navigation/AvoidanceController.cs` consumes `ObstacleReading` +
  `CollisionPredictor` TTC/CPA and picks lateral/vertical clearance from *sensor readings*, not
  from hardcoded obstacle positions.
- **H. Map control:** `Map/MapManager.cs` switches `OfflineMapProvider` ↔ `CesiumMapProvider`
  (`IMapDataProvider`).
- **I. Authority over UAV position:** the `Rigidbody` on `QuadcopterPhysics`. The only direct
  position write in the whole flight/mission/navigation tree is `ResetToPose` (guarded, mission-
  reset only, not reachable through `IFlightController`).
- **J. Conflicting systems:** none found at the code level. There is exactly one position source,
  one flight controller, one planner path. (Whether they conflict *dynamically* is a runtime
  question — see Section C.)

---

## B. Genuine gaps (safe to implement without a runtime)

1. **Floating origin / world rebasing — NOT implemented.** No rebasing code exists anywhere. For
   large mission extents (the prompt asks for 1–25 km) the UAV would sit at large Unity coordinates
   and lose float precision → jitter. This is the one clearly-missing large-map subsystem. Buildable
   as an additive system (shift world + nearby objects, keep geo/planner/telemetry continuous).
2. **Map realism** is partial: Cesium photoreal is optional (needs package + ion token) and the
   offline provider is stylised, not satellite/3D-building accurate. Improving offline realism is
   additive.
3. **3D buildings as navigable volume:** buildings feed the occupancy grid via colliders, but there
   is no explicit building-height → voxel ingestion pass documented; worth verifying/strengthening.
4. **Documentation names** the prompt lists (FLIGHT_CONTROL.md, GEOREFERENCING.md, etc.) partly
   overlap the `Documentation/Subsystems/*` set just added; can be reconciled/renamed additively.

---

## C. Runtime-only — CANNOT be done in this sandbox

Every reported *symptom* and every acceptance test in the prompt is a runtime behaviour. Without a
Unity Editor I cannot reproduce, measure, or tune any of these:

- "UAV does not fly stably", "uncontrolled descent", "manual unstable", "vertical not controlled",
  "autonomous doesn't reach target", "drift", "oscillation", "overshoot".
- PID/gain tuning (position/velocity/altitude/attitude/rate), hover-thrust calibration.
- All of Sections 14, 52, 75–81, 90 acceptance tests; planner benchmarking with real timings.

These require running the sim, observing, and iterating. **I can propose specific, code-grounded
fixes and reason about likely root causes, but I cannot verify a stability fix without executing
it — and claiming otherwise would violate this project's engineering-honesty rule.**

### Candidate code-level suspects for "uncontrolled descent" (hypotheses, unverified)

Places worth instrumenting first in the Editor (not yet changed):

- `_climbRate.OutputLimit = 0.45f` (line ~303) caps the climb-rate loop's throttle offset. If the
  derived hover throttle + 0.45 is still below what's needed at the current mass/T-W, the aircraft
  cannot arrest a descent — check `UavConfiguration.HoverThrottleFraction` vs actual.
- `PresetIntegralForOutput` preload on takeoff (line ~562) — if the preloaded integral doesn't match
  true hover thrust, the loop spends time re-discovering it and sags meanwhile.
- Descent-wake drag multiplier (`ApplyAerodynamicDrag`, line ~540) increases drag in fast descent —
  correct physically, but combined with a saturated climb loop could read as "can't stop descending".
- Battery voltage sag (`battery.Tick`) reduces motor thrust as current rises — if hover sits near
  saturation, a loaded climb could sag below hover authority.

None of these is confirmed a bug; they are the first things to log (desired vs actual climb rate,
throttle command, total thrust vs weight) once running.

---

## D. Repair plan (honest, given the constraint)

**Phase A (this document): DONE.**

**What I can do next in-sandbox (no runtime needed):**
- Implement the floating-origin / world-rebasing system (the real large-map gap), additively.
- Add the flight-stability + navigation debug telemetry the prompt asks for (Sections 53–54, 68) so
  that *you* can see desired-vs-actual on every loop in the Editor — this is the highest-leverage
  thing I can build blind, because it turns your Editor runs into a diagnosis I can act on.
- Add a `FlightControllerConfig` consolidation / expose scattered constants (Section 51/84) if any
  are found scattered (most already live in `UavConfiguration`).
- Reconcile/author the named docs (Section 87).

**What needs you at the Editor:**
- Reproduce each symptom, capture the new telemetry, and paste it back. I then make targeted,
  reasoned code/gain changes; you re-run; we iterate. This is the only honest way to "fix stability".

---

## E. Duplicate / hardcoded / anti-pattern scan results

- Duplicate flight controllers: **none**. Duplicate mission/navigation managers: **none**.
- `transform.position =` / `Transform.Translate` in flight path: **none** except guarded
  `ResetToPose`.
- Hardcoded hover throttle (e.g. 0.5): **none** — hover is `[DERIVED]` in `UavConfiguration`.
- Degrees-as-navigation-coordinates: **not found** — planner works in metric grid; geo stays in
  `GeoCoordinate`.
- Coordinate range clamp: `GeoReference.OnValidate` already accepts full ±90 / ±180. The prompt's
  "small coordinate-range bug" is **not reproduced in code**; if it exists it is likely a UI field
  limit or the *world-scale/precision* issue (→ floating origin), not a lat/lon clamp.
- Scripted obstacle events (spawn-at-30s, turn-at-40s): **not found**; obstacles are procedural
  (`Environment/DynamicObstaclePatrol.cs`, seeded).
