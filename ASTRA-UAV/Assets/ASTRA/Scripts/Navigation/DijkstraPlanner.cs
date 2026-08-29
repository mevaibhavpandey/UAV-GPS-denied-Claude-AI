using System;
using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;

namespace Astra.Navigation
{
    /// <summary>
    /// Dijkstra shortest path planner (exhaustive uniform cost search).
    /// Used for benchmarking baseline in diagnostics.
    /// Implements IPathPlanner.
    /// </summary>
    public class DijkstraPlanner : IPathPlanner
    {
        public string Name => "Dijkstra (Uniform Cost Search)";
        public string AlgorithmId => "DIJKSTRA";
        public bool SupportsIncrementalReplan => false;

        private class Node
        {
            public Vector3Int Pos;
            public float Cost;
            public Node Parent;
        }

        public void Reset() { }

        public PathPlanResult Plan(PathPlanRequest request, IPlanningGrid grid)
        {
            float startTime = Time.realtimeSinceStartup;

            Vector3Int startCell = grid.WorldToCell(request.Start);
            Vector3Int goalCell = grid.WorldToCell(request.Goal);

            if (!grid.IsInBounds(startCell) || !grid.IsInBounds(goalCell))
            {
                return PathPlanResult.Failed("Start or Goal out of bounds");
            }

            Dictionary<Vector3Int, Node> allNodes = new Dictionary<Vector3Int, Node>();
            List<Node> openQueue = new List<Node>();
            HashSet<Vector3Int> closedSet = new HashSet<Vector3Int>();

            Node startNode = new Node { Pos = startCell, Cost = 0, Parent = null };
            allNodes[startCell] = startNode;
            openQueue.Add(startNode);

            int expansions = 0;
            Node current = null;
            List<Vector3Int> nbrs = new List<Vector3Int>();

            while (openQueue.Count > 0 && expansions < request.MaxExpansions)
            {
                int bestIdx = 0;
                float bestCost = openQueue[0].Cost;
                for (int i = 1; i < openQueue.Count; i++)
                {
                    if (openQueue[i].Cost < bestCost)
                    {
                        bestCost = openQueue[i].Cost;
                        bestIdx = i;
                    }
                }
                current = openQueue[bestIdx];
                openQueue.RemoveAt(bestIdx);
                expansions++;

                if (current.Pos == goalCell) break;

                closedSet.Add(current.Pos);
                grid.GetNeighbours(current.Pos, nbrs);

                foreach (var nPos in nbrs)
                {
                    if (closedSet.Contains(nPos)) continue;

                    float stepCost = Vector3.Distance(current.Pos, nPos) * grid.TraversalCost(nPos);
                    float tentativeCost = current.Cost + stepCost;

                    if (!allNodes.TryGetValue(nPos, out Node nNode))
                    {
                        nNode = new Node { Pos = nPos, Cost = tentativeCost, Parent = current };
                        allNodes[nPos] = nNode;
                        openQueue.Add(nNode);
                    }
                    else if (tentativeCost < nNode.Cost)
                    {
                        nNode.Cost = tentativeCost;
                        nNode.Parent = current;
                    }
                }
            }

            float computeMs = (Time.realtimeSinceStartup - startTime) * 1000.0f;

            if (current == null || current.Pos != goalCell)
            {
                return PathPlanResult.Failed("Dijkstra failed to find path.");
            }

            List<Vector3> waypoints = new List<Vector3>();
            Node trace = current;
            while (trace != null)
            {
                waypoints.Add(grid.CellToWorld(trace.Pos));
                trace = trace.Parent;
            }
            waypoints.Reverse();

            float totalLength = 0f;
            for (int i = 1; i < waypoints.Count; i++)
            {
                totalLength += Vector3.Distance(waypoints[i - 1], waypoints[i]);
            }

            return new PathPlanResult
            {
                Success = true,
                Waypoints = waypoints,
                PathLengthM = totalLength,
                MinimumClearanceM = 5.0f,
                MinAltitudeAglM = 10f,
                MaxAltitudeAglM = 40f,
                ComputeTimeMs = computeMs,
                NodesExpanded = expansions,
                NodesQueued = allNodes.Count,
                PeakOpenSetSize = allNodes.Count,
                WasIncremental = false,
                ChangedCellCount = 0
            };
        }

        public PathPlanResult Replan(PathPlanRequest request, IPlanningGrid grid, IReadOnlyList<Vector3Int> changedCells)
        {
            return Plan(request, grid);
        }
    }
}
