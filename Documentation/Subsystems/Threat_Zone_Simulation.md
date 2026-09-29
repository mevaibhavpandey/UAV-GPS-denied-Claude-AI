# Threat / No-Go Zone Simulation (ASTRA spec Sec 40–43)

> SIMULATED mission constraints. Zones shape the planner's cost field inside the simulation only.

## Model

`Navigation/MissionZone.cs`:

- `enum ZoneShape { Cylinder, PolygonPrism }` — a vertical cylinder (centre + radius) or an
  extruded polygon (`PolygonXz` footprint between `MinY` and `MaxY`).
- `enum ZoneSeverity { Advisory, SoftAvoid, NoGo }`.
- `MissionZone { Name, Shape, Severity, Centre, RadiusM, PolygonXz, MinY, MaxY, IsHiddenConstraint }`
  with `Contains(Vector3)`, `CostMultiplierAt(Vector3)` and a `PointInPolygonXz` test.

Cost multipliers: `SoftAvoid = 8×`, `Advisory = 2×`, `NoGo = 1×` (no-go is enforced by stamping
cells solid, not by cost). Hidden constraints (`IsHiddenConstraint = true`) model a zone the
operator is not shown; they are logged as `[SIMULATED MISSION CONSTRAINT]`.

## Manager and planner integration

`Navigation/ZoneManager.cs` (self-installing) holds the zone list and a `Version` that bumps on any
change. `ApplyToGrid(OccupancyGrid grid)`:

1. Sets `grid.ExternalCostMultiplier = cell => CostMultiplierAt(grid.CellToWorld(cell))` — this is
   the null-default delegate the grid multiplies its clearance cost by (see `Path_Planning.md`), so
   advisory/soft-avoid zones bias routing without hard-blocking.
2. Stamps every cell inside a `NoGo` zone solid via `grid.SetSolid`, so planners route around it.

`Mission/AutonomyController.cs` bakes zones into the planning grid **once per version change** in
its PLAN stage: it keeps `_zoneVersionApplied` and re-applies only when `zones.Version` differs and
the planning grid is an `OccupancyGrid`. This keeps the per-tick planning path cheap and touches no
existing planning logic beyond the additive bake (Sec 53). It also extends — does not replace — the
radius geofence already enforced by `FailsafeManager`.

**Verify in the Unity Editor:** add zones at runtime and confirm the planner routes around no-go
cells and prefers lower-cost corridors.
