using System.Collections.Generic;
using UnityEngine;

namespace Astra.Contracts
{
    /// <summary>
    /// A path planner. ASTRA ships three implementations behind this interface so they can be
    /// compared on identical problems: A*, Dijkstra, and D* Lite (branded "Margasoochi").
    ///
    /// WHY THE INTERFACE SEPARATES Plan FROM Replan
    /// --------------------------------------------
    /// This is the most important design decision in the navigation layer, and it exists to correct
    /// a measurement problem in the team's earlier browser prototype.
    ///
    /// That prototype benchmarked D* Lite against A* on INITIAL planning and found D* Lite roughly
    /// three times slower for a path about one percent shorter. That result is correct and expected:
    /// on a first plan over an unknown grid, D* Lite does strictly more bookkeeping than A* for the
    /// same answer. Presented on its own it makes the headline algorithm look like a regression.
    ///
    /// D* Lite's actual advantage is INCREMENTAL REPLANNING. When edge costs change - which is
    /// exactly what happens when a UAV discovers an obstacle mid-flight - D* Lite repairs its
    /// existing search tree and touches only the affected region, whereas A* must discard
    /// everything and search again from scratch. The performance gap runs the other way, often by
    /// an order of magnitude, and it grows as the map grows.
    ///
    /// So the interface forces both cases to be measured separately, and the benchmark UI reports
    /// FIRST-PLAN cost and REPLAN cost as distinct figures. That is both the honest presentation
    /// and the one that actually supports the project's thesis.
    /// </summary>
    public interface IPathPlanner
    {
        /// <summary>Display name, e.g. "Margasoochi (D* Lite)".</summary>
        string Name { get; }

        /// <summary>Short algorithm identifier for logs and CSV export, e.g. "DSTAR_LITE".</summary>
        string AlgorithmId { get; }

        /// <summary>
        /// True if this planner supports incremental repair via Replan. A* and Dijkstra return
        /// false and fall back to a full re-plan, which is precisely the cost being measured.
        /// </summary>
        bool SupportsIncrementalReplan { get; }

        /// <summary>
        /// Plans from scratch over the supplied grid. Clears any retained search state.
        /// </summary>
        PathPlanResult Plan(PathPlanRequest request, IPlanningGrid grid);

        /// <summary>
        /// Repairs an existing plan after the grid's costs have changed in the supplied cells.
        ///
        /// Implementations that do not support incremental repair should perform a full Plan and
        /// set WasIncremental to false in the result, so the comparison stays honest rather than
        /// silently reporting a cheap no-op.
        /// </summary>
        /// <param name="changedCells">Cells whose traversal cost changed since the last plan.</param>
        PathPlanResult Replan(PathPlanRequest request, IPlanningGrid grid,
                              IReadOnlyList<Vector3Int> changedCells);

        /// <summary>Discards retained search state.</summary>
        void Reset();
    }

    /// <summary>A planning query.</summary>
    public struct PathPlanRequest
    {
        /// <summary>Start position in world space.</summary>
        public Vector3 Start;

        /// <summary>Goal position in world space.</summary>
        public Vector3 Goal;

        /// <summary>
        /// Minimum clearance the path must keep from any obstacle, metres. The planner inflates
        /// obstacles by this amount rather than planning to the obstacle surface, which is what
        /// stops a "valid" path from clipping a building corner.
        /// </summary>
        public float SafetyMarginM;

        /// <summary>Preferred cruise altitude above ground, metres.</summary>
        public float PreferredAltitudeAglM;

        /// <summary>Maximum altitude the planner may use, metres above ground.</summary>
        public float MaxAltitudeAglM;

        /// <summary>
        /// Penalty weight applied to altitude changes. Climbing costs battery, so a planner that
        /// treats vertical movement as free will happily hop over every building and flatten the
        /// endurance budget. Non-zero values bias towards lateral avoidance.
        /// </summary>
        public float AltitudeChangeCostWeight;

        /// <summary>
        /// Penalty weight applied to proximity to obstacles, beyond the hard safety margin. This
        /// is what implements "prioritise safety over shortest distance" from the specification:
        /// a path that skims the margin costs more than one that keeps its distance, so the
        /// planner prefers the roomier corridor even when it is slightly longer.
        /// </summary>
        public float ClearanceCostWeight;

        /// <summary>Maximum nodes the planner may expand before giving up. Bounds worst-case time.</summary>
        public int MaxExpansions;

        public static PathPlanRequest Default(Vector3 start, Vector3 goal)
        {
            PathPlanRequest r = new PathPlanRequest();
            r.Start = start;
            r.Goal = goal;
            r.SafetyMarginM = 8f;
            r.PreferredAltitudeAglM = 40f;
            r.MaxAltitudeAglM = 120f;   // 120 m AGL is the common regulatory ceiling for small UAS
            r.AltitudeChangeCostWeight = 1.6f;
            r.ClearanceCostWeight = 2.0f;
            r.MaxExpansions = 250000;
            return r;
        }
    }

    /// <summary>
    /// The outcome of a planning call, including the instrumentation needed for an honest
    /// algorithm comparison.
    /// </summary>
    public struct PathPlanResult
    {
        public bool Success;

        /// <summary>Reason for failure, empty on success.</summary>
        public string FailureReason;

        /// <summary>Waypoints from start to goal in world space, inclusive of both.</summary>
        public List<Vector3> Waypoints;

        /// <summary>Total path length in metres, summed over segments.</summary>
        public float PathLengthM;

        /// <summary>Minimum clearance to any obstacle along the path, metres.</summary>
        public float MinimumClearanceM;

        /// <summary>Lowest and highest AGL altitude used, metres.</summary>
        public float MinAltitudeAglM;
        public float MaxAltitudeAglM;

        // ---- Instrumentation ----

        /// <summary>Wall-clock planning time in milliseconds.</summary>
        public float ComputeTimeMs;

        /// <summary>Nodes expanded (popped from the open set). The algorithm-independent measure of
        /// search effort, and more comparable across machines than wall-clock time.</summary>
        public int NodesExpanded;

        /// <summary>Nodes pushed onto the open set.</summary>
        public int NodesQueued;

        /// <summary>Peak size of the open set. Indicates memory pressure.</summary>
        public int PeakOpenSetSize;

        /// <summary>
        /// False if this was a full plan, true if the planner genuinely repaired existing state.
        /// The benchmark must report this, otherwise a planner that quietly falls back to a full
        /// search would appear to be replanning incrementally.
        /// </summary>
        public bool WasIncremental;

        /// <summary>How many grid cells changed, for a replan. Zero for an initial plan.</summary>
        public int ChangedCellCount;

        public static PathPlanResult Failed(string reason)
        {
            PathPlanResult r = new PathPlanResult();
            r.Success = false;
            r.FailureReason = reason;
            r.Waypoints = new List<Vector3>();
            return r;
        }
    }

    /// <summary>
    /// The occupancy structure planners search over.
    ///
    /// Abstracted so the same planners can run against a coarse grid sampled from Cesium terrain
    /// and building heights, a finer local grid built from live sensor returns in GPS-denied mode,
    /// or a test fixture in a unit test.
    /// </summary>
    public interface IPlanningGrid
    {
        /// <summary>Cell size in metres. Uniform in all three axes.</summary>
        float CellSizeM { get; }

        /// <summary>Grid dimensions in cells.</summary>
        Vector3Int Dimensions { get; }

        /// <summary>World position of the centre of cell (0,0,0).</summary>
        Vector3 OriginWorld { get; }

        /// <summary>Converts a world position to grid coordinates.</summary>
        Vector3Int WorldToCell(Vector3 world);

        /// <summary>Converts grid coordinates to the world position of the cell centre.</summary>
        Vector3 CellToWorld(Vector3Int cell);

        /// <summary>True if the cell index lies inside the grid.</summary>
        bool IsInBounds(Vector3Int cell);

        /// <summary>
        /// True if the cell may be flown through. Accounts for obstacle inflation by the safety
        /// margin, so this already means "safe", not merely "not solid".
        /// </summary>
        bool IsTraversable(Vector3Int cell);

        /// <summary>
        /// Additional traversal cost multiplier for a cell, at or above 1. Used to make cells near
        /// obstacles more expensive without making them illegal, which produces paths that keep
        /// clearance where clearance is available.
        /// </summary>
        float TraversalCost(Vector3Int cell);

        /// <summary>Distance from this cell to the nearest obstacle, metres. Used for clearance
        /// reporting and by the cost function.</summary>
        float ClearanceAt(Vector3Int cell);

        /// <summary>Terrain height in world Y at the given cell column, metres.</summary>
        float TerrainHeightAt(Vector3Int cell);

        /// <summary>
        /// Fills the supplied list with the traversable neighbours of a cell. Takes a list to fill
        /// so the inner loop of the search allocates nothing.
        /// </summary>
        void GetNeighbours(Vector3Int cell, List<Vector3Int> into);

        /// <summary>
        /// Monotonic version counter, incremented whenever cell costs change. Lets a planner detect
        /// staleness cheaply instead of comparing grids.
        /// </summary>
        int Version { get; }
    }
}
