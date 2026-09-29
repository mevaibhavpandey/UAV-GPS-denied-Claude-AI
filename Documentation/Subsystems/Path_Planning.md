# Path Planning (ASTRA spec Sec 26, 60–61)

> SIMULATED research platform. Everything below runs inside the Unity simulation. No figure here
> is a hardware measurement; benchmark numbers come from the real planner code executed over a
> fixed synthetic grid fixture.

## Overview

ASTRA plans over a 3D `OccupancyGrid` (`Perception/OccupancyGrid.cs`, implements
`IPlanningGrid`). Cells carry a traversal cost derived from clearance to inflated obstacles, so the
planners naturally prefer routes with margin. Four planners implement the common `IPathPlanner`
contract and are interchangeable:

| Planner | File | AlgorithmId | Incremental replan |
|---|---|---|---|
| Margasoochi D* Lite (default) | `Navigation/MargasoochiDStarLite.cs` | `DSTARLITE` | yes |
| A* baseline | `Navigation/AStarPlanner.cs` | `ASTAR` | no |
| Dijkstra baseline | `Navigation/DijkstraPlanner.cs` | `DIJKSTRA` | no |
| Theta* any-angle | `Navigation/ThetaStarPlanner.cs` | `THETASTAR` | no |

## Theta* any-angle planner (D4)

`ThetaStarPlanner` is modelled on the A* baseline (same `Node`, linear-scan priority queue and
grid usage) with one addition: when relaxing a successor, if that successor has line-of-sight to
the current node's **parent**, its parent is set to that grandparent with straight-line cost
(Path 2); otherwise the standard A* relaxation through the current node applies (Path 1).

Line-of-sight is a half-cell-resolution supercover sample along the segment that rejects the moment
any sampled cell is out of bounds or `!IsTraversable`. Because the grid is already margin-inflated,
"traversable" already means "safe with clearance". The result is a shorter, less grid-locked route
with fewer needless heading changes than 26-connected A*.

`SupportsIncrementalReplan` is `false` and `Replan` re-searches via `Plan`, so it reports
`WasIncremental = false` in benchmarks — this keeps comparisons against the incremental D* Lite
honest. The `MinimumClearanceM` / altitude fields mirror A*'s placeholders and are not per-run
measurements.

## External cost layer (D5)

`OccupancyGrid` exposes `public System.Func<Vector3Int,float> ExternalCostMultiplier` (null by
default, so behaviour is unchanged unless a layer opts in). `TraversalCost` computes the clearance
base cost and then multiplies by `ExternalCostMultiplier(cell)` when it returns > 1. The
threat/no-go zone layer (`Navigation/ZoneManager`) uses this to bias planning away from soft-avoid
and advisory zones and stamps no-go zones solid. See `Threat_Zone_Simulation.md`.

## Benchmarking

`Diagnostics/BenchmarkRunner.RunBenchmark()` runs all four planners over a fixed
`OccupancyGrid(Vector3.zero, new Vector3Int(80,20,80), 2.0f)` fixture and returns, per planner,
initial plan time / node expansions, replan time / expansions, path length, and whether the replan
was incremental. It is driven interactively from the `ExperimentPanel` (F11); the global scenario
seed makes each run reproducible and comparable.

**Verify in the Unity Editor:** run the benchmark and fly each planner. Structural validation does
not execute the search.
