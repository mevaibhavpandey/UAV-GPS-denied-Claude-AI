using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Navigation;
using Astra.Perception;

namespace Astra.Diagnostics
{
    /// <summary>
    /// Benchmarks path planning algorithms (Margasoochi D* Lite vs Standard A* vs Dijkstra)
    /// across first-plan and incremental dynamic replan conditions.
    /// Outputs comparative metrics (Compute Time, Expansions, Queue Size, Path Length).
    /// </summary>
    public class BenchmarkRunner : MonoBehaviour
    {
        public struct BenchmarkResult
        {
            public string Algorithm;
            public float InitialPlanTimeMs;
            public int InitialExpansions;
            public float ReplanTimeMs;
            public int ReplanExpansions;
            public float PathLengthM;
            public bool IncrementalSupported;
        }

        public List<BenchmarkResult> RunBenchmark()
        {
            List<BenchmarkResult> results = new List<BenchmarkResult>();

            // Setup a 3D planning grid test fixture
            OccupancyGrid grid = new OccupancyGrid(Vector3.zero, new Vector3Int(80, 20, 80), 2.0f);

            // Add building obstacles
            grid.InsertObstacleBox(new Vector3(40, 10, 40), new Vector3(8, 10, 8));
            grid.InsertObstacleBox(new Vector3(80, 10, 80), new Vector3(10, 10, 10));

            PathPlanRequest req = PathPlanRequest.Default(new Vector3(10, 15, 10), new Vector3(140, 15, 140));

            IPathPlanner[] planners = new IPathPlanner[]
            {
                new MargasoochiDStarLite(),
                new AStarPlanner(),
                new DijkstraPlanner()
            };

            foreach (var planner in planners)
            {
                // 1. Initial Plan
                PathPlanResult initialRes = planner.Plan(req, grid);

                // 2. Introduce dynamic obstacle in path
                Vector3Int dynCell = grid.WorldToCell(new Vector3(60, 15, 60));
                grid.SetSolid(dynCell, true);

                // 3. Dynamic Replan
                PathPlanResult replanRes = planner.Replan(req, grid, new Vector3Int[] { dynCell });

                results.Add(new BenchmarkResult
                {
                    Algorithm = planner.Name,
                    InitialPlanTimeMs = initialRes.ComputeTimeMs,
                    InitialExpansions = initialRes.NodesExpanded,
                    ReplanTimeMs = replanRes.ComputeTimeMs,
                    ReplanExpansions = replanRes.NodesExpanded,
                    PathLengthM = replanRes.PathLengthM,
                    IncrementalSupported = planner.SupportsIncrementalReplan
                });
            }

            return results;
        }
    }
}
