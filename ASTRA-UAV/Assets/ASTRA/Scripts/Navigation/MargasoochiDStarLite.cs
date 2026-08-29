using System;
using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;

namespace Astra.Navigation
{
    /// <summary>
    /// Margasoochi D* Lite incremental path planning algorithm.
    /// Supports dynamic edge-cost updates and efficient local replanning.
    /// Implements IPathPlanner.
    /// </summary>
    public class MargasoochiDStarLite : IPathPlanner
    {
        public string Name => "Margasoochi (D* Lite Incremental)";
        public string AlgorithmId => "DSTAR_LITE";
        public bool SupportsIncrementalReplan => true;

        private class State
        {
            public Vector3Int Pos;
            public float G = float.PositiveInfinity;
            public float Rhs = float.PositiveInfinity;
            public Key Key;
            public int QueueIndex = -1;
        }

        private struct Key : IComparable<Key>
        {
            public float K1;
            public float K2;

            public Key(float k1, float k2)
            {
                K1 = k1;
                K2 = k2;
            }

            public int CompareTo(Key other)
            {
                if (K1 < other.K1 - 0.0001f) return -1;
                if (K1 > other.K1 + 0.0001f) return 1;
                if (K2 < other.K2 - 0.0001f) return -1;
                if (K2 > other.K2 + 0.0001f) return 1;
                return 0;
            }

            public static bool operator <(Key a, Key b) => a.CompareTo(b) < 0;
            public static bool operator >(Key a, Key b) => a.CompareTo(b) > 0;
            public static bool operator <=(Key a, Key b) => a.CompareTo(b) <= 0;
            public static bool operator >=(Key a, Key b) => a.CompareTo(b) >= 0;
        }

        private readonly Dictionary<Vector3Int, State> _states = new Dictionary<Vector3Int, State>();
        private readonly PriorityQueue _openSet = new PriorityQueue();
        private Vector3Int _sStart;
        private Vector3Int _sGoal;
        private Vector3Int _sLast;
        private float _kM;
        private IPlanningGrid _grid;

        public void Reset()
        {
            _states.Clear();
            _openSet.Clear();
            _kM = 0;
        }

        private State GetState(Vector3Int pos)
        {
            if (!_states.TryGetValue(pos, out State s))
            {
                s = new State { Pos = pos };
                _states[pos] = s;
            }
            return s;
        }

        private Key CalculateKey(State s)
        {
            float minVal = Mathf.Min(s.G, s.Rhs);
            return new Key(
                minVal + Heuristic(_sStart, s.Pos) + _kM,
                minVal
            );
        }

        private float Heuristic(Vector3Int a, Vector3Int b)
        {
            return Vector3.Distance(a, b);
        }

        private float Cost(Vector3Int a, Vector3Int b)
        {
            if (!_grid.IsTraversable(a) || !_grid.IsTraversable(b))
                return float.PositiveInfinity;

            float dist = Vector3.Distance(a, b);
            float traversalWeight = (_grid.TraversalCost(a) + _grid.TraversalCost(b)) * 0.5f;
            return dist * traversalWeight;
        }

        private void UpdateVertex(State u)
        {
            if (u.Pos != _sGoal)
            {
                float minRhs = float.PositiveInfinity;
                List<Vector3Int> nbrs = new List<Vector3Int>();
                _grid.GetNeighbours(u.Pos, nbrs);

                foreach (var nPos in nbrs)
                {
                    State sPrime = GetState(nPos);
                    float cost = Cost(u.Pos, nPos);
                    float val = sPrime.G + cost;
                    if (val < minRhs) minRhs = val;
                }
                u.Rhs = minRhs;
            }

            if (_openSet.Contains(u))
            {
                _openSet.Remove(u);
            }

            if (Mathf.Abs(u.G - u.Rhs) > 0.0001f)
            {
                u.Key = CalculateKey(u);
                _openSet.Enqueue(u);
            }
        }

        private int ComputeShortestPath(int maxExpansions)
        {
            int expansions = 0;
            List<Vector3Int> nbrs = new List<Vector3Int>();

            while (_openSet.Count > 0 && expansions < maxExpansions)
            {
                State u = _openSet.Peek();
                State sStart = GetState(_sStart);
                Key kOld = u.Key;
                Key kNew = CalculateKey(u);

                if (kOld < kNew)
                {
                    _openSet.UpdateKey(u, kNew);
                }
                else if (u.G > u.Rhs)
                {
                    u.G = u.Rhs;
                    _openSet.Dequeue();
                    expansions++;

                    _grid.GetNeighbours(u.Pos, nbrs);
                    foreach (var sPos in nbrs)
                    {
                        State s = GetState(sPos);
                        UpdateVertex(s);
                    }
                }
                else
                {
                    u.G = float.PositiveInfinity;
                    UpdateVertex(u);
                    expansions++;

                    _grid.GetNeighbours(u.Pos, nbrs);
                    foreach (var sPos in nbrs)
                    {
                        State s = GetState(sPos);
                        UpdateVertex(s);
                    }
                }

                if (_openSet.Count > 0 && _openSet.Peek().Key >= CalculateKey(sStart) && Mathf.Abs(sStart.Rhs - sStart.G) < 0.0001f)
                    break;
            }
            return expansions;
        }

        public PathPlanResult Plan(PathPlanRequest request, IPlanningGrid grid)
        {
            float startTime = Time.realtimeSinceStartup;
            _grid = grid;
            Reset();

            _sStart = grid.WorldToCell(request.Start);
            _sGoal = grid.WorldToCell(request.Goal);
            _sLast = _sStart;
            _kM = 0;

            if (!grid.IsInBounds(_sStart) || !grid.IsInBounds(_sGoal))
            {
                return PathPlanResult.Failed("Start or Goal outside planning grid bounds.");
            }

            State goalState = GetState(_sGoal);
            goalState.Rhs = 0;
            goalState.Key = CalculateKey(goalState);
            _openSet.Enqueue(goalState);

            int expansions = ComputeShortestPath(request.MaxExpansions);
            float computeMs = (Time.realtimeSinceStartup - startTime) * 1000.0f;

            return ExtractPathResult(computeMs, expansions, false, 0);
        }

        public PathPlanResult Replan(PathPlanRequest request, IPlanningGrid grid, IReadOnlyList<Vector3Int> changedCells)
        {
            float startTime = Time.realtimeSinceStartup;
            _grid = grid;

            Vector3Int sCurr = grid.WorldToCell(request.Start);
            _kM += Heuristic(_sLast, sCurr);
            _sStart = sCurr;
            _sLast = sCurr;

            if (changedCells != null)
            {
                foreach (var cell in changedCells)
                {
                    State u = GetState(cell);
                    UpdateVertex(u);
                }
            }

            int expansions = ComputeShortestPath(request.MaxExpansions);
            float computeMs = (Time.realtimeSinceStartup - startTime) * 1000.0f;

            return ExtractPathResult(computeMs, expansions, true, changedCells != null ? changedCells.Count : 0);
        }

        private PathPlanResult ExtractPathResult(float computeMs, int expansions, bool wasIncremental, int changedCells)
        {
            State curr = GetState(_sStart);
            if (float.IsInfinity(curr.G) && float.IsInfinity(curr.Rhs))
            {
                return PathPlanResult.Failed("No traversable route found to goal.");
            }

            List<Vector3> waypoints = new List<Vector3>();
            Vector3Int currPos = _sStart;
            waypoints.Add(_grid.CellToWorld(currPos));

            HashSet<Vector3Int> visited = new HashSet<Vector3Int> { currPos };
            int stepLimit = 2000;
            List<Vector3Int> nbrs = new List<Vector3Int>();

            while (currPos != _sGoal && stepLimit-- > 0)
            {
                float minCost = float.PositiveInfinity;
                Vector3Int nextPos = currPos;

                _grid.GetNeighbours(currPos, nbrs);
                foreach (var nPos in nbrs)
                {
                    State sPrime = GetState(nPos);
                    float cost = Cost(currPos, nPos) + sPrime.G;
                    if (cost < minCost)
                    {
                        minCost = cost;
                        nextPos = nPos;
                    }
                }

                if (nextPos == currPos || visited.Contains(nextPos))
                {
                    break;
                }

                currPos = nextPos;
                visited.Add(currPos);
                waypoints.Add(_grid.CellToWorld(currPos));
            }

            float totalLength = 0f;
            float minClearance = 100f;
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (i > 0) totalLength += Vector3.Distance(waypoints[i - 1], waypoints[i]);
                Vector3Int c = _grid.WorldToCell(waypoints[i]);
                float clr = _grid.ClearanceAt(c);
                if (clr < minClearance) minClearance = clr;
            }

            return new PathPlanResult
            {
                Success = true,
                Waypoints = waypoints,
                PathLengthM = totalLength,
                MinimumClearanceM = minClearance,
                MinAltitudeAglM = 10f,
                MaxAltitudeAglM = 40f,
                ComputeTimeMs = computeMs,
                NodesExpanded = expansions,
                NodesQueued = _openSet.Count,
                PeakOpenSetSize = _openSet.PeakCount,
                WasIncremental = wasIncremental,
                ChangedCellCount = changedCells
            };
        }

        private class PriorityQueue
        {
            private readonly List<State> _heap = new List<State>();
            public int Count => _heap.Count;
            public int PeakCount { get; private set; }

            public void Clear()
            {
                _heap.Clear();
                PeakCount = 0;
            }

            public bool Contains(State s) => s.QueueIndex >= 0 && s.QueueIndex < _heap.Count && _heap[s.QueueIndex] == s;

            public void Enqueue(State s)
            {
                s.QueueIndex = _heap.Count;
                _heap.Add(s);
                UpHeap(s.QueueIndex);
                if (_heap.Count > PeakCount) PeakCount = _heap.Count;
            }

            public State Peek() => _heap[0];

            public State Dequeue()
            {
                State root = _heap[0];
                root.QueueIndex = -1;
                State last = _heap[_heap.Count - 1];
                _heap.RemoveAt(_heap.Count - 1);

                if (_heap.Count > 0)
                {
                    last.QueueIndex = 0;
                    _heap[0] = last;
                    DownHeap(0);
                }
                return root;
            }

            public void Remove(State s)
            {
                int idx = s.QueueIndex;
                if (idx < 0 || idx >= _heap.Count) return;

                s.QueueIndex = -1;
                if (idx == _heap.Count - 1)
                {
                    _heap.RemoveAt(_heap.Count - 1);
                    return;
                }

                State last = _heap[_heap.Count - 1];
                _heap.RemoveAt(_heap.Count - 1);
                last.QueueIndex = idx;
                _heap[idx] = last;

                UpHeap(idx);
                DownHeap(idx);
            }

            public void UpdateKey(State s, Key newKey)
            {
                s.Key = newKey;
                if (Contains(s))
                {
                    UpHeap(s.QueueIndex);
                    DownHeap(s.QueueIndex);
                }
            }

            private void UpHeap(int idx)
            {
                State item = _heap[idx];
                while (idx > 0)
                {
                    int parentIdx = (idx - 1) / 2;
                    State parent = _heap[parentIdx];
                    if (item.Key >= parent.Key) break;

                    _heap[idx] = parent;
                    parent.QueueIndex = idx;
                    idx = parentIdx;
                }
                _heap[idx] = item;
                item.QueueIndex = idx;
            }

            private void DownHeap(int idx)
            {
                State item = _heap[idx];
                int half = _heap.Count / 2;
                while (idx < half)
                {
                    int childIdx = 2 * idx + 1;
                    int right = childIdx + 1;
                    if (right < _heap.Count && _heap[right].Key < _heap[childIdx].Key)
                        childIdx = right;

                    if (item.Key <= _heap[childIdx].Key) break;

                    _heap[idx] = _heap[childIdx];
                    _heap[idx].QueueIndex = idx;
                    idx = childIdx;
                }
                _heap[idx] = item;
                item.QueueIndex = idx;
            }
        }
    }
}
