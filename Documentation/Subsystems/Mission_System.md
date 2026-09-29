# Mission System (ASTRA spec Sec 3–8, 38–39, 73)

> SIMULATED. Per-type behaviour is the waypoint pattern plus observation dwell — not distinct
> sensor or mission-planning logic beyond that.

## Pre-flight setup

`Mission/MissionSetupManager.cs` is a self-installing IMGUI screen shown BEFORE the Ground Control
Station. It gates the GCS via the static `IsSetupActive` flag so operational panels never flash up
behind it. The operator picks:

- **Mission profile** (Point-to-Point / Reconnaissance / Surveillance / Area Survey / Search)
- **Navigation mode** (GPS-Assisted / GPS-Denied / Manual — see `GPS_Denied.md`)
- **Map backend** (Offline / Cesium — see `Map_System.md`)
- **Launch (home)** and **objective (target)** lat/lon/alt (default BMSIT origin, labelled
  UNVERIFIED / APPROXIMATE)
- **Flight parameters** (cruise alt, speed, safety margin)

`START SIMULATION` validates the fields, applies the scenario seed, anchors `GeoReference` to the
launch point, builds a `MissionDefinition` of the chosen profile via `MissionProfiles.Build`, hands
it to the existing `MissionManager`, sets GPS availability per nav mode, and (for autonomous modes)
arms + engages + takes off along the same path the GCS F2/F3 handlers use. It does not duplicate or
replace the GCS/flight/mission systems (Sec 53).

## Mission types

`Contracts/IMissionProvider.cs` defines `enum MissionType { PointToPoint, Reconnaissance,
Surveillance, AreaSurvey, Search }` and per-type shaping params on `MissionDefinition`
(`ObservationDwellSeconds`, `SurveillancePassCount`, `AreaSurveyRows`, `SearchRings`,
`PatternRadiusM`). Existing missions default to `PointToPoint`.

`Mission/MissionProfiles.cs` is the single source of truth turning a type + home + objective +
params into a concrete `MissionDefinition`. Geometry is generated in geographic coordinates by
offsetting metres from the objective, so it survives origin/map changes:

- **Reconnaissance** — 4 offset observation holds around the objective
- **Surveillance** — N alternating passes
- **Area Survey** — lawnmower legs
- **Search** — expanding-ring boxes
- **Point-to-Point** — straight to target

Every profile ends on a `Target` waypoint.

## Execution

`Mission/AutonomyController.cs` runs the 7-stage loop (SENSE→PERCEIVE→LOCALIZE→PLAN→DECIDE→ACT
→REASSESS at 10 Hz). Observation/Hold dwell handling makes the profiles behave differently in the
air: on reaching an observation waypoint the executive hovers and counts down `DwellSeconds` before
advancing and re-planning. Point-to-Point flies straight through.

**Verify in the Unity Editor:** confirm the setup→GCS transition and fly each profile to check the
patterns and dwell.
