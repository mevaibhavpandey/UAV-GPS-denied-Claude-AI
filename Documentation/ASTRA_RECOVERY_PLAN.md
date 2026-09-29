# ASTRA UAV — Living Recovery Plan & Change Log

> **Purpose of this file.** This is a self-contained, resumable work log. If the current AI
> session runs out of tokens, another AI (or a human) can read this file top-to-bottom and
> continue with no other context. It is updated **after every change**. Newest change-log
> entries go at the top of Section E.
>
> **Last updated:** 2026-09-30 · **Maintained by:** Claude (Opus 4.8) working in the Claude desktop app.

---

## A. Ground truth & hard constraints (read first)

- **Engine/stack:** Unity 6 (6000.0.0f1), URP 17.0.3, C#, Windows Desktop. Input System 1.7.0.
- **Repo root:** `Claude- UAV Project Simulation/` · **Unity project:** `ASTRA-UAV/` · **Scripts:**
  `ASTRA-UAV/Assets/ASTRA/Scripts/` (56 `.cs`, ~14.4k lines) · **Scene(s):** only
  `ASTRA-UAV/Assets/ASTRA/Scenes/ASTRA_GCS_Demo.unity`.
- **GitHub:** `https://github.com/mevaibhavpandey/UAV-GPS-denied-Claude-AI` (branch `main`).
- **Sandbox limits (cannot be worked around here):** no Unity Editor, no C#/.NET compiler, no
  package-manager network, no `git push` (proxy 403). All C# is validated **structurally only**
  via `Tools/validate_cs.py`; it does NOT type-check or compile. Unity Editor is the authoritative
  build. `git push` must be run by the user on their machine.
- **Honesty rules (non-negotiable):** never present simulated/unverified things as proven. Label
  everything SIMULATED / DEMONSTRATION / FUTURE HARDWARE / REAL IMPLEMENTATION. Don't claim
  production SLAM/VIO or flight-certified estimators. BMSIT coords (13.1320°N, 77.5670°E, ~890 m)
  are APPROXIMATE/UNVERIFIED. **Section 53 of the spec: do not duplicate, destroy, or regenerate
  working systems.** Reuse what is sound.
- **Verified-working, DO NOT casually touch:** rate-loop body-rate sign conventions in
  `FlightControlSystem` (`-bodyRatesDeg.z/.x/.y`) and the removal of the passive pendulum
  self-leveling torque in `QuadcopterPhysics` — this is what made flight stable. Extreme-attitude
  hardening was layered on top (see change log).

## B. How to use this doc when resuming

1. Read Section A, then Section E (top entry) to see the last thing done.
2. `git log --oneline -5` and `python3 Tools/validate_cs.py ASTRA-UAV/Assets/ASTRA` to confirm state.
3. Pick the next unstarted item from Section D (respect priorities). Do NOT rebuild DONE items.
4. After each change: run the validator, add a Section E entry, commit locally.

<!-- SECTIONS_C_D_E_F -->

## C. Gap analysis — 77-section master spec vs code on disk

Legend: ✅ DONE · 🟡 PARTIAL · ❌ MISSING. Evidence is the file that satisfies the item.

### ✅ DONE (present and sound — do not regenerate)
- **Single sim state / service locator / event bus** — `Core/AstraServices`, `AstraEvents`, `SimClock`.
- **Rigidbody quadcopter physics** (mass, thrust, drag, torque, motor response, mixer with saturation
  priority) — `Flight/QuadcopterPhysics`, `MotorMixer`, `FlightControlSystem`, `PidController`.
- **Autopilot: trajectory→control mapping** — `FlightControlSystem` (CommandVelocity/Takeoff/Land/Hover).
- **Autonomy 7-stage loop** SENSE→…→REASSESS at 10 Hz + live AI decision panel —
  `Mission/AutonomyController`, `UI/GcsManager`, `Contracts/DecisionTypes`.
- **Global planners** A*, Dijkstra, D* Lite over a **3D** occupancy grid (FREE/OCCUPIED/UNKNOWN) —
  `Navigation/AStarPlanner`, `DijkstraPlanner`, `MargasoochiDStarLite`, `Perception/OccupancyGrid`.
- **Local avoidance + smoothing + real-time replanning** — `Navigation/AvoidanceController`,
  `TrajectorySmoother`, replanning in `AutonomyController`.
- **Obstacle detection + collision prediction (TTC/risk) + threat scoring** —
  `Perception/RaycastObstacleDetector`, `CollisionPredictor`, `ThreatAnalyzer`.
- **GPS vs GPS-denied localization switching** — `Localization/LocalizationManager` (registers the
  provider and swaps GNSS↔VIO on GPS change), `GnssBaroLocalizationProvider`,
  `VisualInertialLocalizationProvider` (labelled DEMONSTRATION).
- **Perception (monochrome) view synced to real view** — `Perception/PerceptionViewController`, `PerceptionManager`.
- **GCS dashboard, telemetry, sensor health matrix, event log** — `UI/GcsManager`, `Localization/TelemetryProvider`.
- **Manual mode** (WASD/Q/E/Space/etc.), R disambiguated (manual arm vs autonomous start) —
  `Flight/ManualFlightInput`, `UI/GcsManager`.
- **Engineering / exploded / X-ray view** — `Engineering/EngineeringViewController`.
- **Presentation/demo mode** — `Presentation/PresentationController` (F8).
- **Map provider abstraction + honest Cesium + stylized offline fallback + F9 switch** —
  `Map/IMapDataProvider`, `CesiumMapProvider`, `OfflineMapProvider`, `MapManager`; see `MAP_SETUP.md`.
- **Failsafe: battery / tilt / radius geofence → RTL/land** — `Mission/FailsafeManager`.
- **Flight state machine (17 states) + mission phases (11)** — `Contracts/IFlightController` (`FlightState`),
  `IMissionProvider` (`MissionPhase`). Covers Section 37 conceptually.
- **Hardware-ready interfaces** — `Contracts/` I* provider set.

### 🟡 PARTIAL (exists but short of the spec)
- **LiDAR (Sec 13–15, 54):** real raycasting exists (`RaycastObstacleDetector`) but there is **no
  dedicated configurable LiDAR sensor** exposing range/FOV/H+V resolution/scan-rate/noise/dropout/
  blind-spots, and no point-cloud sensor visualization distinct from the perception view.
- **Onboard camera (Sec 12):** `Cameras/CameraController` is view/rig control, not a payload sensor
  with FOV/noise/frustum + RGB/stereo/depth/thermal architecture.
- **Sensor fusion (Sec 16):** fusion is implicit across localization/perception; **no named
  `SensorFusionManager`** aggregating LiDAR+camera+IMU+GPS+compass+baro.
- **Geofencing / threat zones (Sec 40–43):** only a **radius** geofence in `FailsafeManager`. No
  polygon/no-go zones, no user-placed zones, no hidden "mission constraint" layer, and the planner
  has no zone cost layer.
- **Mission types (Sec 4, 38–39):** ✅ addressed by D2 — `MissionType` enum + `MissionProfiles`
  builder + per-type Observation-dwell behaviour. (Verify in Unity.)
- **Research/experiment mode (Sec 60–61):** `Diagnostics/BenchmarkRunner` exists but there is no
  interactive experiment panel to vary planner/noise/seed and compare runs live.
- **Reproducible randomness (Sec 21, 61):** offline map uses a fixed seed field; no user-facing
  "new scenario / reuse seed / compare" controls, no global scenario seed.

### ❌ MISSING (no implementation on disk)
- **Startup Mission Setup screen/flow (Sec 3–8, 73):** the app boots straight into the GCS. There is
  no pre-flight setup scene/panel to pick mission type, map, home, target, nav mode before flying.
  **This is the most visible gap vs the spec's intended UX.**
- **Theta* planner (Sec 26):** ✅ addressed by D4 — `Navigation/ThetaStarPlanner` registered with the
  other three planners in `BenchmarkRunner`. (Verify with an Editor benchmark run.)
- **Thin-obstacle / wire representation + honest det-limit demo (Sec 24):** none.
- **`AirframeBuilder.cs` / `SceneGenerator.cs`:** referenced as COMPLETE in the older
  `PROJECT_RECOVERY_STATUS.md` but **do not exist in the tree** — that doc overstates. The drone
  hierarchy is authored in the scene/prefab + `Drone/MotorUnit`, `UavComponent`, `DroneVisualEnhancer`
  (verify in Unity). Treat the twin as PARTIAL pending in-editor confirmation.
- **Documentation set (Sec 67):** the named files (Mission_System, Map_System, LiDAR_System,
  Sensor_Fusion, Path_Planning, Obstacle_Avoidance, GPS_Denied, Threat_Zone_Simulation, Digital_Twin,
  Testing) don't exist under those names; current docs cover overlapping ground differently.

<!-- SECTION_D -->

## D. Prioritized backlog

Priorities reflect **presentation impact vs effort**, and that the sandbox can't compile/run — so
items that are self-contained C# with low integration risk are safer to attempt here than deep
scene/prefab surgery (which needs the Unity Editor).

### P0 — highest demo impact
- **D1. Startup Mission Setup flow (Sec 3–8, 73).** ✅ **DONE (structural; verify in Unity)** —
  implemented as `Mission/MissionSetupManager.cs`, a self-installing pre-flight IMGUI screen that
  gates the GCS and hands a chosen-profile `MissionDefinition` to `MissionManager`. See the top
  change-log entry. Remaining: confirm the setup→GCS transition and bootstrap in the Editor.
- **D2. Mission types (Sec 4, 38–39).** ✅ **DONE (structural; verify in Unity)** — `MissionType`
  enum + per-type params in `Contracts/IMissionProvider.cs`, waypoint patterns in new
  `Mission/MissionProfiles.cs`, and Observation-dwell behaviour in `AutonomyController`. Remaining:
  fly each profile in the Editor to confirm the patterns and dwell.
- **D2. Mission types (Sec 4, 38–39).** Add a `MissionType` enum + per-type parameters and behaviour
  (Recon orbit, Surveillance N passes, Area Survey lawnmower, Point-to-Point, Search) in
  `MissionManager`/`AutonomyController`. Feeds D1.

### P1 — closes clear spec gaps, mostly additive C#
- **D3. Configurable LiDAR sensor (Sec 13–15, 54).** ✅ **DONE (structural; verify in Unity)** — new
  `Perception/LidarSensor` implementing the raycast scan with range/FOV/H+V res/scan-rate/noise/dropout;
  exposes stats to the F10 sensor panel; keeps `RaycastObstacleDetector` as consumer. Additive, low risk.
- **D4. Theta* planner (Sec 26).** ✅ **DONE (structural; verify in Unity)** — new
  `Navigation/ThetaStarPlanner.cs : IPathPlanner` with line-of-sight parent shortcutting over the
  `OccupancyGrid`; registered alongside D* Lite / A* / Dijkstra in `BenchmarkRunner`. See top
  change-log entry. Remaining: run the benchmark in the Editor to compare the four planners.
- **D5. Threat/no-go zones + planner cost layer + user placement (Sec 40–43).** ✅ **DONE (structural;
  verify in Unity)** — `Navigation/MissionZone` + `ZoneManager`, `OccupancyGrid.ExternalCostMultiplier`
  cost layer, `AutonomyController` per-version bake, hidden "SIMULATED MISSION CONSTRAINT" flag.
  Extends the radius geofence already in `FailsafeManager`.
- **D6. SensorFusionManager (Sec 16).** ✅ **DONE (structural; verify in Unity)** — `Sensors/SensorFusionManager`
  status aggregator over LiDAR+camera+IMU+GPS+compass+baro (pass-through pose, NOT a re-fusion), surfaced
  in the F10 sensor panel.

### P2 — completeness & polish
- **D7. Onboard payload camera (Sec 12)** ✅ **DONE (structural; verify in Unity)** — `Perception/PayloadCamera`
  with FOV/noise/frustum + RGB/stereo/depth/thermal architecture; models geometry/coverage only, no image formation.
- **D8. Thin-obstacle / wire model + honest detection-limit demo (Sec 24).** ✅ **DONE (structural; verify in Unity)** —
  `Perception/ThinObstacleDemo` spawns thin wires and reports the honest geometric guaranteed-detection range.
- **D9. Research/experiment panel + global scenario seed + compare-runs (Sec 60–61, 21).** ✅ **DONE (structural;
  verify in Unity)** — `Core/ScenarioSeed` + `Diagnostics/ExperimentPanel` (F11): reproducible seed, LiDAR
  noise/dropout sliders, planner benchmark with last-8-runs comparison.
- **D10. Documentation set (Sec 67)** ✅ **DONE** — added the named subsystem docs under
  `Documentation/Subsystems/` (Mission_System, Map_System, LiDAR_System, Sensor_Fusion, Path_Planning,
  Obstacle_Avoidance, GPS_Denied, Threat_Zone_Simulation, Digital_Twin, Testing) and reconciled the
  overstated `PROJECT_RECOVERY_STATUS.md` (removed the nonexistent `AirframeBuilder`/`SceneGenerator`).
- **D11. Verify digital-twin hierarchy in the Unity scene/prefab** (frame/4×arm-motor-prop/battery/
  FC/compute/GPS/camera/LiDAR/ESC/gear). ⚠️ **EDITOR-ONLY — cannot be done in the headless sandbox.**
  Flagged in `Subsystems/Digital_Twin.md`: open the prefab and walk the hierarchy against the spec.

### Cross-cutting rules for every item
- Reuse existing contracts/services; register new providers via `AstraServices`.
- Validate structurally after each edit; commit locally; append a Section E entry.
- Keep the "not a game" tone and all honesty labels.

<!-- SECTION_E -->

## E. Change log (newest first)

> Append a dated entry after every change: what changed, which files, validator result, commit.

### 2026-09-30 — D3, D5, D6, D7, D8, D9, D10 (batch: "do everything")

**D3 — Configurable LiDAR sensor.** New `Perception/LidarSensor.cs` (+ meta
b7e2f4a1c8d34965a2b1e0f6d7c93a5e): raycast beam grid with configurable range/FOV/H+V resolution/
scan-rate and an imperfection model (Box–Muller Gaussian range noise, per-beam dropout, lower blind
cone). Live stats (`LastPointCount`/`LastBeamsCast`/`LastReturnRatePercent`/`NearestReturnM`/
`LastScanDurationMs`) + `PointCloud`. Self-installs on the `FlightControlSystem` GameObject, registers
`AstraServices.Register<LidarSensor>(this)`, `DataProvenance.Simulated`. `RaycastObstacleDetector`
kept as the consumer (Sec 53).

**D5 — Threat/no-go zones + cost layer.** New `Navigation/MissionZone.cs`
(+ meta f6d3b8a5c4e29071a7b1f2c9e0d84a61) and `Navigation/ZoneManager.cs`
(+ meta a7e4c9b6d5f30182b8c2f3d0e1f95b72). `OccupancyGrid` gained a null-default
`ExternalCostMultiplier` delegate (behaviour unchanged unless opted in); `TraversalCost` multiplies
its clearance base cost by it. `ZoneManager.ApplyToGrid` wires the delegate (SoftAvoid 8×, Advisory
2×) and stamps NoGo cells solid. `AutonomyController` bakes zones once per `Version` change in PLAN
(`_zoneVersionApplied`). Hidden zones logged `[SIMULATED MISSION CONSTRAINT]`. Extends, not replaces,
the `FailsafeManager` geofence.

**D6 — SensorFusionManager.** New `Sensors/SensorFusionManager.cs`
(+ meta c3a9e5f2b1d7468fa4c2e8b0d6f19a73) in namespace `Astra.Sensors`, plus `Sensors.meta` folder
meta (d4b1f6a3c2e84790b5d3f9c1e7a2408b). HONEST **status aggregator**, not an estimator: per-channel
`SensorChannel { Name, Contributing, Status, Provenance, Detail }` for IMU/GNSS/Baro/Mag/LiDAR/Camera;
`FusedPose` is a pass-through of the active `ILocalizationProvider`. New `UI/SensorPanel.cs`
(+ meta e5c2a7f4b3d9481fa6e0c1b8d2f74a90), F10 read-only overlay, gated by `MissionSetupManager.IsSetupActive`.

**D7 — Onboard payload camera.** New `Perception/PayloadCamera.cs`
(+ meta b8f5d0c7e6a41293b9c3f4e1d2a05b73): `CameraMode { Rgb, Stereo, Depth, Thermal }`, HFOV/aspect/
pixels/near/far/noise, computed `VerticalFovDeg`, per-mode `UsableRangeM`, `IsInFrustum(world,out range)`
and `ObstaclesInFrustum`/`NearestInFrustumM`. Self-installs, registers. HONEST: models geometry/coverage
only — no image formation.

**D8 — Thin-obstacle / wire honesty demo.** New `Perception/ThinObstacleDemo.cs`
(+ meta c9a6e1d8f7b52304c0d4a5f2e3b16c84): spawns 0.02 m `Wire_n` cylinders and reports the honest
guaranteed-detection range = wireDiameter / beamSpacing (from the LiDAR angular resolution), warning
that beyond it a wire can slip between beams. A deliberate demonstration of a real sensing limit.

**D9 — Research/experiment panel + scenario seed.** New `Core/ScenarioSeed.cs`
(+ meta d0b7f2e9c8a63415d1e5b6f3a4c07d95): reproducible simulation randomness (default 20260930),
`Apply()`/`SetSeed()`/`NewSeed()`. New `Diagnostics/ExperimentPanel.cs`
(+ meta e1c8a3f0d9b74526e2f6c7a4b5d18e06), F11 overlay: seed Reuse/New, LiDAR noise σ + dropout
sliders bound to the registered `LidarSensor`, planner benchmark button (adds `BenchmarkRunner` on
demand) keeping the last 8 runs for comparison; gated by `MissionSetupManager.IsSetupActive`.
`Mission/MissionSetupManager.cs` now calls `ScenarioSeed.Apply()` in `TryStart` so each run is
reproducible.

**D10 — Documentation set (Sec 67).** Added named subsystem docs under `Documentation/Subsystems/`
(README + Mission_System, Map_System, LiDAR_System, Sensor_Fusion, Path_Planning, Obstacle_Avoidance,
GPS_Denied, Threat_Zone_Simulation, Digital_Twin, Testing). Reconciled `PROJECT_RECOVERY_STATUS.md`:
removed the nonexistent `AirframeBuilder.cs` / `SceneGenerator.cs` references and pointed Layer 2 at
the real `Drone/*` components with a D11 Editor-verification note.

**D11 — Digital-twin hierarchy:** ⚠️ EDITOR-ONLY, not attempted in the sandbox (no Unity Editor).
Documented as the remaining verification step in `Subsystems/Digital_Twin.md` and `Subsystems/Testing.md`.

- **Honesty:** every new sensor/camera/fusion is `DataProvenance.Simulated` with explicit
  "not calibrated / status only / no image formation" notes; the thin-obstacle demo surfaces the real
  detection limit rather than always detecting; the GPS-denied estimator is labelled DEMONSTRATION
  (not production VIO/SLAM); benchmark figures come from the real planners over a fixed fixture.
- Validator: **0 errors / 0 warnings / 0 advisories** across the tree. **Unity Editor compile + the
  runtime checklist in `Subsystems/Testing.md` still required** — structural validation does not
  compile or run anything.


### 2026-09-30 — D4: Theta* any-angle planner
- **New `Navigation/ThetaStarPlanner.cs`** — a fourth `IPathPlanner` (`Name = "Theta* (Any-Angle)"`,
  `AlgorithmId = "THETASTAR"`, `SupportsIncrementalReplan = false`, `Replan` re-searches via `Plan`).
  Modeled on `AStarPlanner` (same `Node`/linear-scan `SimplePriorityQueue`/grid-usage pattern) with
  the one Theta* addition: when relaxing a successor, if the successor has line-of-sight to the
  current node's PARENT, its parent is set to that grandparent with straight-line cost (Path 2);
  otherwise the standard A* relaxation through the current node applies (Path 1). LoS is a
  half-cell-resolution supercover sample along the segment that rejects the moment any cell is
  out-of-bounds or `!IsTraversable` (grid already reports margin-inflated "safe"). Result: shorter,
  less grid-locked routes than A* — fewer needless heading changes for the UAV. Added matching
  `.cs.meta` (guid 4e91d7c2a8b3465fb1f0e6d5c93a72b8).
- **`Diagnostics/BenchmarkRunner.cs`** — registered `new ThetaStarPlanner()` in the planner array
  alongside D* Lite / A* / Dijkstra so the benchmark compares path length + node expansions across
  all four. No other behaviour touched (Sec 53).
- **Honesty:** Theta* re-searches from scratch and reports `WasIncremental = false`, so benchmarks
  against the incremental D* Lite stay meaningful; the fixed `MinimumClearanceM`/altitude fields
  mirror A*'s placeholders and are not per-run measurements.
- Validator: **0 errors / 61 files**. Commit: pending in this entry. **Editor build + a benchmark
  run comparing the four planners must be verified in Unity** — structural validation does not run
  the search.

### 2026-09-30 — D2: Mission types + per-type behaviours
- **`Contracts/IMissionProvider.cs`** — added a `MissionType` enum (PointToPoint, Reconnaissance,
  Surveillance, AreaSurvey, Search) and gave `MissionDefinition` a `Type` field plus per-type shaping
  params (`ObservationDwellSeconds`, `SurveillancePassCount`, `AreaSurveyRows`, `SearchRings`,
  `PatternRadiusM`). Additive; existing missions default to PointToPoint.
- **New `Mission/MissionProfiles.cs`** — single source of truth that turns a `MissionType` + home +
  objective + params into a concrete `MissionDefinition`. Geometry is generated in geographic
  coords by offsetting metres from the objective (survives origin/map changes). Recon = 4 offset
  observation holds around the objective; Surveillance = N alternating passes; AreaSurvey =
  lawnmower legs; Search = expanding-ring boxes; every profile ends on a Target waypoint. Added
  matching `.cs.meta`.
- **`Mission/MissionSetupManager.cs`** — now uses the shared `MissionType` (dropped its private
  duplicate enum) and delegates waypoint generation to `MissionProfiles.Build`, removing ~80 lines
  of duplicated geometry. UI, gating and commit flow unchanged.
- **`Mission/AutonomyController.cs`** — added Observation/Hold dwell handling to the ACT stage: on
  reaching an observation waypoint the executive hovers and counts down `DwellSeconds` before
  advancing and re-planning. This is what makes the profiles behave differently in the air (the
  patterns pause to observe; Point-to-Point flies straight through). Transit and Target handling
  untouched (Sec 53).
- **Honesty:** all missions labelled SIMULATED; per-type behaviour is the waypoint pattern + dwell,
  not distinct sensor/mission-planning logic beyond that.
- Validator: **0 errors / 60 files**. Commit: pending in this entry. **Editor build + flying each
  profile must be verified in Unity** — structural validation does not exercise the dwell loop or
  the pattern geometry.

### 2026-09-30 — D1: Startup Mission Setup flow (pre-flight configuration screen)
- **New `Mission/MissionSetupManager.cs`** — a pre-flight IMGUI configuration screen shown BEFORE the
  GCS (Sec 3–8, 73). Operator picks: mission profile (Point-to-Point / Recon / Surveillance / Area
  Survey / Search), navigation mode (GPS-Assisted / GPS-Denied / Manual), map backend (Offline /
  Cesium), launch+objective lat/lon/alt (default BMSIT origin, labelled UNVERIFIED), and cruise
  alt / speed / safety-margin. "START SIMULATION" builds a `MissionDefinition` of the chosen profile
  (per-type waypoint presets), anchors `GeoReference` to the home coord, loads it into the existing
  `MissionManager`, sets GPS via `ISensorProvider`, optionally requests Cesium via
  `MapManager.ToggleMapProvider` (honest — stays offline if not live), places the GCS target beacon,
  and for autonomous modes arms+starts+takes off (same path as the F2/F3 handlers). Manual mode
  hands the operator the sticks.
- **Self-install (no scene editing):** `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` bootstrap
  creates the component and sets the static gate `IsSetupActive=true` before the first GUI frame.
  Added matching `.cs.meta` with a fresh GUID.
- **`UI/GcsManager.cs`** — gated: `Update()` and `OnGUI()` early-return while
  `MissionSetupManager.IsSetupActive`, so the operational panels and F-key shortcuts stay dormant
  until the run is configured. No other GCS behaviour touched (Sec 53 respected).
- **Scope note (honesty):** the per-type waypoint presets only SHAPE the route; distinct per-type
  autonomy *behaviours* remain D2. Labelled as such in code + this log.
- Validator: **0 errors / 59 files**. Commit: pending in this entry. **Authoritative build & the
  setup→GCS transition must be verified in the Unity Editor** — structural validation cannot confirm
  the GUI gating or runtime bootstrap.

### 2026-09-30 — Inspection, gap analysis, and this living plan
- Inspected the project on disk (56 scripts, 1 scene, manifest has NO Cesium package). Produced the
  Section C gap analysis and Section D backlog. No source behaviour changed in this step.
- Created `Documentation/ASTRA_RECOVERY_PLAN.md` (this file). Validator: not applicable (docs only).
- **Key finding:** app boots straight into the GCS — the spec's startup Mission Setup flow (D1) and
  distinct mission types (D2) are the biggest gaps. `AirframeBuilder.cs`/`SceneGenerator.cs` named in
  the old status doc do not exist in the tree.

### 2026-09-30 — Extreme-attitude, autonomous/GPS-denied, realistic map (commit `caa0cdc`, local)
- **Flight (extreme roll/pitch tumble):** clamp attitude-rate demand to config limit; bleed rate-loop
  PID integrators by delivered mixer authority (anti-windup through saturation); cap rigidbody max
  angular velocity + stiffer solver. Files: `Core/Config/UavConfiguration`, `Flight/FlightControlSystem`,
  `Flight/PidController`, `Flight/QuadcopterPhysics`. Only bites the aggressive regime.
- **Autonomous + GPS-denied:** new `Localization/LocalizationManager` (registers `ILocalizationProvider`
  — was never registered, so LOCALIZE was a silent no-op — and swaps GNSS↔VIO on GPS change);
  `Mission/AutonomyController` enlarged planning grid + advance past Transit waypoints (fixed stall on
  first waypoint); `UI/GcsManager` F2/F3 now arm+start+takeoff via `BeginAutonomousMission`.
- **Map:** rewrote `Map/OfflineMapProvider` into a daytime "Garden City" stylized BMSIT env (roads +
  lane markings, mixed buildings, tree canopy, campus cluster + pad; labelled STYLIZED); made
  `Map/CesiumMapProvider` honest (reflection-detects the real package, reports NOT READY when absent
  instead of faking "streaming"); `Map/MapManager` only switches to Cesium when tiles are live; added
  `Documentation/MAP_SETUP.md`. `GeoReference` origin already at BMSIT (UNVERIFIED).
- Validator: 0 errors / 58 files. Committed locally as `caa0cdc`. **Push still pending** (sandbox 403).

### Earlier (prior sessions, from git history / memory)
- `5edae01` (2026-08-30): fixed flight instability — inverted rate-loop body-rate signs and removed a
  passive pendulum self-leveling torque that fought the controller. This is the load-bearing flight fix.
- Full 9-layer implementation of the simulator (core, twin/flight, sensors/localization, perception,
  navigation, autonomy/mission, dual map+perception, GCS+engineering, presentation+diagnostics).

## F. Verification protocol (every change)
1. `python3 Tools/validate_cs.py ASTRA-UAV/Assets/ASTRA` → expect `ERRORS: 0`.
2. For new `.cs` files, add a matching `.cs.meta` with a fresh GUID (Unity regenerates otherwise).
3. Commit locally with inline identity:
   `git -c user.name="Vaibhav Pandey" -c user.email="vaibhav.pandey1661@gmail.com" commit`.
4. `git push origin main` — **must be run by the user** (sandbox blocks egress with a proxy 403).
5. **Authoritative build & flight test happen only in the Unity Editor** — structural validation is
   necessary but NOT sufficient. State this honestly whenever reporting.


