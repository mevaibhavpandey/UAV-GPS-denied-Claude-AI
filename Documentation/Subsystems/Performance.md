# Performance & Telemetry (ASTRA spec Sec 53–54, 68: research-quality measurement)

> SIMULATED research platform. This document describes the performance-relevant design decisions in
> the code and how to MEASURE them in the Unity Editor. It contains no frame-rate or timing numbers,
> because those can only be produced by running the project — none are asserted here.

## Design decisions that keep the loop cheap

**Events for things that happen, polling for things that are.** `Core/AstraEvents.cs` carries only
discrete occurrences (state changes, obstacle detected, waypoint reached) — a handful per second at
most. Continuous per-frame data (telemetry) is *polled* from `ITelemetryProvider` by whoever needs
it, at whatever rate suits them, rather than pushed through a delegate chain. Pushing ~100 telemetry
events/second would allocate every raise and couple the flight loop's timing to the UI's cost for no
benefit. Each event raise is exception-isolated so a subscriber bug cannot stop the flight loop.

**Bounded, allocation-light diagnostics.** `Diagnostics/FlightDiagnosticsRecorder.cs` samples into a
fixed 600-entry ring buffer (~6 s at 100 Hz physics) — no per-frame allocation for history. CSV
capture writes one line per physics step only while recording (F7) and flushes on stop. The overlay
is read-only IMGUI and holds no control authority.

**Cheap floating-origin check.** `Core/Geo/FloatingOriginManager.cs` runs a squared-distance compare
in `LateUpdate`; the expensive work (walking scene roots and translating the world) happens only on
the rare frames a rebase actually fires (default threshold 2 km, so never in the campus demo).

**Bounded planning grid.** The autonomy planner uses a fixed `OccupancyGrid` (the live grid is
600×100×600 m at 4 m resolution; the benchmark fixture is 80×20×80 at 2 m). A fixed grid keeps
planning cost predictable and comparable across runs. `GeoMath`'s ECEF↔geodetic path is closed-form
(Ferrari–Zhu) with no iteration, so its cost is predictable even if called per frame.

## How to measure (Editor only)

- **Frame time / GC:** Unity Profiler (CPU, Rendering, Memory) during a representative mission. Watch
  for per-frame allocations, which show up as GC.Alloc spikes.
- **Control-loop behaviour:** the diagnostics recorder (F12) shows desired-vs-actual for every loop
  live; F7 writes a CSV for offline analysis (columns documented in the recorder header).
- **Planner cost:** `Diagnostics/BenchmarkRunner.RunBenchmark()` (driven from the ExperimentPanel,
  F11) reports plan time, node expansions, path length and incremental-vs-full replan per planner
  over the fixed fixture. The global scenario seed makes runs reproducible and comparable.
- **Decision cadence:** the recorder reports the autonomy decision cycle time (ms) so a planning
  spike is visible against the physics step budget.

## Honest limits

No performance figure in this repository has been produced by running the project in this
environment — the sandbox has no Unity Editor. Any number quoted in a presentation must come from an
actual Editor/Profiler run on target hardware and be labelled with that hardware. See
[KNOWN_LIMITATIONS.md](../KNOWN_LIMITATIONS.md).
