# ASTRA UAV — Engineering Report: Phase A Audit & Non-Runtime Work

**Date:** 2026-09-30 · **Scope:** Master Repair + Optimization prompt, Phase A (audit) plus the
four non-runtime work items selected by the operator. **Status labelling** follows the project's
honesty rule throughout: nothing here is a hardware measurement, and runtime behaviour is marked
UNVERIFIED where it could not be confirmed without the Unity Editor.

---

## 1. What was asked

Take over ASTRA and fix core systems in priority order — stable flight, correct georeferencing,
reliable autonomous navigation, sensor-driven avoidance, a realistic large-scale map, and
research-quality telemetry — **modifying the existing project**, without adding parallel controllers,
masking instability with drag, or regenerating working systems. Phase A required a full technical
audit before any major change.

## 2. Central audit finding

The codebase **already satisfies most of the prompt's architecture**. There is a single force-based
`FlightControlSystem` (no duplicate controllers), a proper cascaded PID stack driving a derived
hover throttle, a clean geodetic/Unity coordinate separation (`GeoReference` + `GeoMath`), 3D
occupancy-grid planning with four interchangeable planners, and sensor-driven reactive avoidance. The
reported instability symptoms are **runtime behaviours that cannot be reproduced or measured by
reading source** — so the correct engineering response was to make the runtime measurable, not to
guess at edits to tuned, sign-sensitive control code. Full detail in
`Documentation/FLIGHT_AND_NAVIGATION_AUDIT.md`.

## 3. Work delivered this round (all additive, validator-clean)

**Flight & navigation diagnostics** — `Diagnostics/FlightDiagnosticsRecorder.cs`. F12 opens a
read-only overlay of desired-vs-actual for every control loop (climb rate, attitude, rate loops,
thrust-to-weight, PID P/I/D split, saturation) plus navigation (desired vs actual velocity,
cross-track error, decision cycle time). F7 records a timestamped CSV. It holds **no control
authority** — it only reads — so it honours "do not add a parallel controller / do not mask
instability." This is the instrument for the telemetry-first tuning loop.

**Floating-origin / world rebasing** — `Core/Geo/FloatingOriginManager.cs`. Keeps the aircraft near
the Unity origin on large (1–25 km) missions by translating the whole world and moving the geodetic
origin by the equal-and-opposite amount, so geographic coordinates stay continuous and float
precision is preserved. Because every cached setpoint shifts by the same delta
(`AstraEvents.WorldRebased`), the rebase is invisible to the control law. Default threshold is 2 km,
so it **never fires in the current campus demo** — zero regression risk. See
`Documentation/Subsystems/Georeferencing.md`.

**Global static-obstacle avoidance** — `Contracts/IStaticObstacleSource.cs`, implemented by
`OfflineMapProvider` and consumed by `AutonomyController`. The procedural map knows its own building
boxes, so they are now stamped into the planning grid (once per change, inflated by one voxel) and
the planner routes around them globally instead of relying only on close-range reaction. Cesium
deliberately does not implement this (its fused photogrammetry has no per-building semantics), which
keeps the distinction honest. Toggleable via `ingestStaticObstacles`.

**Documentation reconciliation** — added `Flight_Control.md`, `Georeferencing.md`, `Performance.md`
to `Documentation/Subsystems/` (index updated), and expanded `KNOWN_LIMITATIONS.md` with the
runtime-unverified, approximate-origin, and rebase-range-gated disclosures.

## 4. Verification performed

`Tools/validate_cs.py` over `ASTRA-UAV/Assets/ASTRA/Scripts`: **71 files, 0 errors / 0 warnings /
0 advisories**. New `.cs.meta` GUIDs confirmed unique across the tree. Note: this is a structural
delimiter/lexer check, **not** a compile or type check.

## 5. What remains for the operator (Unity Editor required)

The sandbox has no Unity Editor, compiler, or package network, so the following must be confirmed on
target hardware — this is the telemetry-first loop the operator selected:

1. Arm and hover; open the diagnostics recorder (F12). Confirm thrust-to-weight ≈ 1 in hover,
   climb-rate desired ≈ actual, attitude tracking tight, loops not chronically saturated.
2. If any instability appears, record a CSV (F7) and share it — fixes will then be made against
   measured data, targeting the specific loop at fault, without touching sign conventions blindly.
3. Fly a long synthetic mission past 2 km to exercise floating origin; confirm geographic readouts
   stay continuous and nothing jitters.
4. Fly the offline map and confirm nominal routes clear buildings and takeoff is not blocked.

All changes are uncommitted; committing and pushing remain with the operator.
