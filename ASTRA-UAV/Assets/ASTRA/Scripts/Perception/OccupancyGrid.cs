using System;
using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;

namespace Astra.Perception
{
    /// <summary>
    /// 3D Voxel Occupancy Grid for path planning.
    /// Stores traversability, clearance distance field, and terrain elevations.
    /// Implements IPlanningGrid.
    /// </summary>
    public class OccupancyGrid : IPlanningGrid
    {
        private readonly float _cellSize;
        private readonly Vector3Int _dimensions;
        private readonly Vector3 _originWorld;
        private readonly bool[] _solidCells;
        private readonly float[] _clearanceField;
        private readonly float[] _terrainHeight;
        private int _version;

        private static readonly Vector3Int[] NeighbourOffsets26 = CreateNeighbourOffsets();

        public float CellSizeM => _cellSize;
        public Vector3Int Dimensions => _dimensions;
        public Vector3 OriginWorld => _originWorld;
        public int Version => _version;

        public OccupancyGrid(Vector3 originWorld, Vector3Int dimensions, float cellSizeM)
        {
            _originWorld = originWorld;
            _dimensions = dimensions;
            _cellSize = Mathf.Max(0.5f, cellSizeM);

            int totalCells = dimensions.x * dimensions.y * dimensions.z;
            _solidCells = new bool[totalCells];
            _clearanceField = new float[totalCells];
            _terrainHeight = new float[dimensions.x * dimensions.z];
            _version = 1;

            // Initialize all cells as traversable with default clearance
            for (int i = 0; i < totalCells; i++)
            {
                _clearanceField[i] = 100f;
            }
        }

        private int GetIndex(Vector3Int cell)
        {
            return cell.x + _dimensions.x * (cell.y + _dimensions.y * cell.z);
        }

        private int Get2DIndex(int x, int z)
        {
            return x + _dimensions.x * z;
        }

        public Vector3Int WorldToCell(Vector3 world)
        {
            Vector3 rel = world - _originWorld;
            return new Vector3Int(
                Mathf.FloorToInt(rel.x / _cellSize),
                Mathf.FloorToInt(rel.y / _cellSize),
                Mathf.FloorToInt(rel.z / _cellSize)
            );
        }

        public Vector3 CellToWorld(Vector3Int cell)
        {
            return _originWorld + new Vector3(
                (cell.x + 0.5f) * _cellSize,
                (cell.y + 0.5f) * _cellSize,
                (cell.z + 0.5f) * _cellSize
            );
        }

        public bool IsInBounds(Vector3Int cell)
        {
            return cell.x >= 0 && cell.x < _dimensions.x &&
                   cell.y >= 0 && cell.y < _dimensions.y &&
                   cell.z >= 0 && cell.z < _dimensions.z;
        }

        public bool IsTraversable(Vector3Int cell)
        {
            if (!IsInBounds(cell)) return false;
            return !_solidCells[GetIndex(cell)];
        }

        public float TraversalCost(Vector3Int cell)
        {
            if (!IsInBounds(cell)) return float.PositiveInfinity;
            float clearance = _clearanceField[GetIndex(cell)];
            if (clearance < 2.0f) return 10.0f;
            if (clearance < 5.0f) return 3.0f;
            if (clearance < 8.0f) return 1.5f;
            return 1.0f;
        }

        public float ClearanceAt(Vector3Int cell)
        {
            if (!IsInBounds(cell)) return 0f;
            return _clearanceField[GetIndex(cell)];
        }

        public float TerrainHeightAt(Vector3Int cell)
        {
            if (cell.x < 0 || cell.x >= _dimensions.x || cell.z < 0 || cell.z >= _dimensions.z)
                return 0f;
            return _terrainHeight[Get2DIndex(cell.x, cell.z)];
        }

        public void SetTerrainHeight(int x, int z, float height)
        {
            if (x >= 0 && x < _dimensions.x && z >= 0 && z < _dimensions.z)
            {
                _terrainHeight[Get2DIndex(x, z)] = height;
            }
        }

        public void SetSolid(Vector3Int cell, bool solid)
        {
            if (!IsInBounds(cell)) return;
            int idx = GetIndex(cell);
            if (_solidCells[idx] != solid)
            {
                _solidCells[idx] = solid;
                _version++;
            }
        }

        public void InsertObstacleBox(Vector3 centreWorld, Vector3 halfExtentsWorld, float inflationMarginM = 0f)
        {
            Vector3 totalHalfExtents = halfExtentsWorld + Vector3.one * inflationMarginM;
            Vector3 minWorld = centreWorld - totalHalfExtents;
            Vector3 maxWorld = centreWorld + totalHalfExtents;

            Vector3Int minCell = WorldToCell(minWorld);
            Vector3Int maxCell = WorldToCell(maxWorld);

            minCell = Vector3Int.Max(minCell, Vector3Int.zero);
            maxCell = Vector3Int.Min(maxCell, _dimensions - Vector3Int.one);

            for (int z = minCell.z; z <= maxCell.z; z++)
            {
                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    for (int x = minCell.x; x <= maxCell.x; x++)
                    {
                        Vector3Int c = new Vector3Int(x, y, z);
                        int idx = GetIndex(c);
                        _solidCells[idx] = true;
                        _clearanceField[idx] = 0f;
                    }
                }
            }
            _version++;
        }

        public void UpdateClearanceField()
        {
            // Simple fast clearance distance field approximation
            int total = _solidCells.Length;
            for (int i = 0; i < total; i++)
            {
                if (_solidCells[i])
                {
                    _clearanceField[i] = 0f;
                }
                else
                {
                    _clearanceField[i] = 10f; // nominal open space
                }
            }
        }

        public void GetNeighbours(Vector3Int cell, List<Vector3Int> into)
        {
            into.Clear();
            for (int i = 0; i < NeighbourOffsets26.Length; i++)
            {
                Vector3Int neighbour = cell + NeighbourOffsets26[i];
                if (IsInBounds(neighbour) && !_solidCells[GetIndex(neighbour)])
                {
                    into.Add(neighbour);
                }
            }
        }

        private static Vector3Int[] CreateNeighbourOffsets()
        {
            List<Vector3Int> offsets = new List<Vector3Int>();
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && y == 0 && z == 0) continue;
                        offsets.Add(new Vector3Int(x, y, z));
                    }
                }
            }
            return offsets.ToArray();
        }
    }
}
