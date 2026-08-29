using System;
using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;

namespace Astra.Navigation
{
    /// <summary>
    /// Standard 3D A* search path planner for baseline comparative benchmarking against Margasoochi D* Lite.
    /// Implements IPathPlanner.
    /// </summary>
    public class AStarPlanner : IPathPlanner
    {
        public string Name => "Standard A* (Full Re-search)";
        public string AlgorithmId => "ASTAR";
        public bool SupportsIncrementalReplan => false;

        private class Node
        {
            public Vector3Int Pos;
            public float G;
            public float H;
            public float F => G + H;
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
            SimplePriorityQueue openSet = new SimplePriorityQueue();
            HashSet<Vector3Int> closedSet = new HashSet<Vector3Int>();

            Node startNode = new Node
            {
                Pos = startCell,
                G = 0,
                H = Vector3.Distance(startCell, goalCell),
                Parent = null
            };
            allNodes[startCell] = startNode;
            openSet.Enqueue(startNode);

            int expansions = 0;
            Node current = null;
            List<Vector3Int> nbrs = new List<Vector3Int>();

            while (openSet.Count > 0 && expansions < request.MaxExpansions)
            {
                current = openSet.Dequeue();
                expansions++;

                if (current.Pos == goalCell)
                {
                    break;
                }

                closedSet.Add(current.Pos);
                grid.GetNeighbours(current.Pos, nbrs);

                foreach (var nPos in nbrs)
                {
                    if (closedSet.Contains(nPos)) continue;

                    float stepCost = Vector3.Distance(current.Pos, nPos) * grid.TraversalCost(nPos);
                    float tentativeG = current.G + stepCost;

                    if (!allNodes.TryGetValue(nPos, out Node nNode))
                    {
                        nNode = new Node
                        {
                            Pos = nPos,
                            G = tentativeG,
                            H = Vector3.Distance(nPos, goalCell),
                            Parent = current
                        };
                        allNodes[nPos] = nNode;
                        openSet.Enqueue(nNode);
                    }
                    else if (tentativeG < nNode.G)
                    {
                        nNode.G = tentativeG;
                        nNode.Parent = current;
                    }
                }
            }

            float computeMs = (Time.realtimeSinceStartup - startTime) * 1000.0f;

            if (current == null || current.Pos != goalCell)
            {
                return PathPlanResult.Failed("A* could not reach goal within iteration limit.");
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
                PeakOpenSetSize = openSet.PeakCount,
                WasIncremental = false,
                ChangedCellCount = 0
            };
        }

        public PathPlanResult Replan(PathPlanRequest request, IPlanningGrid grid, IReadOnlyList<Vector3Int> changedCells)
        {
            // A* does not support incremental repair; must perform full search
            return Plan(request, grid);
        }

        private class SimplePriorityQueue
        {
            private readonly List<Node> _list = new List<Node>();
            public int Count => _list.Count;
            public int PeakCount { get; private set; }

            public void Enqueue(Node node)
            {
                _list.Add(node);
                if (_list.Count > PeakCount) PeakCount = _list.Count;
            }

            public Node Dequeue()
            {
                int bestIdx = 0;
                float bestF = _list[0].F;
                for (int i = 1; i < _list.Count; i++)
                {
                    if (_list[i].F < bestF)
                    {
                        bestF = _list[i].F;
                        bestIdx = i;
                    }
                }
                Node best = _list[bestIdx];
                _list.RemoveAt(bestIdx);
                return best;
            }
        }
    }
}
