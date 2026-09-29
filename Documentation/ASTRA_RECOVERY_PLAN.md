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
- **Theta* planner (Sec 26):** spec asks A*/Theta*/D* Lite; Theta* is absent.
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
- **D3. Configurable LiDAR sensor (Sec 13–15, 54).** New `Perception/LidarSensor` implementing the
  raycast scan with range/FOV/H+V res/scan-rate/noise/dropout; expose stats to the GCS LiDAR panel;
  keep `RaycastObstacleDetector` as consumer. Additive, low risk.
- **D4. Theta* planner (Sec 26).** New `Navigation/ThetaStarPlanner : IPathPlanner` with line-of-sight
  parent updates over `OccupancyGrid`. Additive; register alongside existing planners.
- **D5. Threat/no-go zones + planner cost layer + user placement (Sec 40–43).** `Navigation`/`Mission`
  zone model (sphere/polygon, severity), planner cost penalty, optional GCS placement tool, hidden
  "SIMULATED MISSION CONSTRAINT" flag. Extends the radius geofence already in `FailsafeManager`.
- **D6. SensorFusionManager (Sec 16).** Named aggregator over LiDAR+camera+IMU+GPS+compass+baro that
  the autonomy/localization stack reads, making the fusion explicit for the panel/docs.

### P2 — completeness & polish
- **D7. Onboard payload camera (Sec 12)** with FOV/noise/frustum + RGB/stereo/depth/thermal arch.
- **D8. Thin-obstacle / wire model + honest detection-limit demo (Sec 24).**
- **D9. Research/experiment panel + global scenario seed + compare-runs (Sec 60–61, 21).**
- **D10. Documentation set (Sec 67)** — add/rename the missing named docs; reconcile the overstated
  `PROJECT_RECOVERY_STATUS.md` (it lists `AirframeBuilder`/`SceneGenerator` which don't exist).
- **D11. Verify digital-twin hierarchy in the Unity scene/prefab** (frame/4×arm-motor-prop/battery/
  FC/compute/GPS/camera/LiDAR/ESC/gear). Confirm or add a builder. Needs the Editor.

### Cross-cutting rules for every item
- Reuse existing contracts/services; register new providers via `AstraServices`.
- Validate structurally after each edit; commit locally; append a Section E entry.
- Keep the "not a game" tone and all honesty labels.

<!-- SECTION_E -->

## E. Change log (newest first)

> Append a dated entry after every change: what changed, which files, validator result, commit.

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


