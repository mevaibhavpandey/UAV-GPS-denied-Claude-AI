using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Logging;
using Astra.Perception;

namespace Astra.Navigation
{
    /// <summary>
    /// Holds the mission's threat / no-go / advisory zones and applies them to a planning grid
    /// (ASTRA spec Sec 40-43). This generalises the single radius geofence in the FailsafeManager
    /// into a set of shaped, severity-graded zones.
    ///
    /// How it affects planning (additively, without rewriting the grid - Sec 53):
    ///  - NoGo zones: cells whose centre is inside are marked solid via OccupancyGrid.SetSolid, so
    ///    every planner treats them as walls.
    ///  - SoftAvoid / Advisory zones: registered through OccupancyGrid.ExternalCostMultiplier, which
    ///    multiplies traversal cost inside the zone so the planner routes around it when it can but
    ///    is still permitted through if there is no alternative.
    ///
    /// HONESTY: all zones are SIMULATED. Hidden ("unbriefed") zones are a research-scenario device
    /// and are labelled SIMULATED MISSION CONSTRAINT.
    /// </summary>
    [DisallowMultipleComponent]
    public class ZoneManager : MonoBehaviour
    {
        private readonly List<MissionZone> _zones = new List<MissionZone>();
        public IReadOnlyList<MissionZone> Zones => _zones;

        /// <summary>Bumped whenever the zone set changes, so consumers know to re-bake the grid.</summary>
        public int Version { get; private set; } = 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAny() != null) return;
            new GameObject("ASTRA_Zone_Manager").AddComponent<ZoneManager>();
        }

        private static ZoneManager FindAny()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindAnyObjectByType<ZoneManager>();
#else
            return Object.FindObjectOfType<ZoneManager>();
#endif
        }

        private void Start()
        {
            AstraServices.Register<ZoneManager>(this);
        }

        private void OnDestroy()
        {
            AstraServices.UnregisterIfCurrent<ZoneManager>(this);
        }

        public void AddZone(MissionZone zone)
        {
            if (zone == null) return;
            _zones.Add(zone);
            Version++;
            string hidden = zone.IsHiddenConstraint ? " [SIMULATED MISSION CONSTRAINT]" : "";
            EventLog.Info(LogSource.Navigation,
                $"Zone added: '{zone.Name}' ({zone.Severity}, {zone.Shape}){hidden}. SIMULATED.");
        }

        public void ClearZones()
        {
            if (_zones.Count == 0) return;
            _zones.Clear();
            Version++;
        }

        /// <summary>Highest cost multiplier from any soft/advisory zone containing this world point.</summary>
        public float CostMultiplierAt(Vector3 world)
        {
            float worst = 1f;
            for (int i = 0; i < _zones.Count; i++)
            {
                float m = _zones[i].CostMultiplierAt(world);
                if (m > worst) worst = m;
            }
            return worst;
        }

        /// <summary>True if the point is inside any hard NoGo zone.</summary>
        public bool IsBlocked(Vector3 world)
        {
            for (int i = 0; i < _zones.Count; i++)
            {
                if (_zones[i].Severity == ZoneSeverity.NoGo && _zones[i].Contains(world)) return true;
            }
            return false;
        }

        /// <summary>
        /// Bakes the current zone set into an OccupancyGrid: hard zones become solid cells, soft
        /// zones are exposed through the grid's external cost layer. Safe to call repeatedly; it
        /// re-points the cost delegate and re-marks solids for the current zones.
        /// </summary>
        public void ApplyToGrid(OccupancyGrid grid)
        {
            if (grid == null) return;

            // Soft/advisory zones bias cost through the delegate (evaluated lazily per cell).
            grid.ExternalCostMultiplier = cell => CostMultiplierAt(grid.CellToWorld(cell));

            // Hard NoGo zones are stamped solid.
            Vector3Int dims = grid.Dimensions;
            for (int zi = 0; zi < _zones.Count; zi++)
            {
                MissionZone z = _zones[zi];
                if (z.Severity != ZoneSeverity.NoGo) continue;

                for (int x = 0; x < dims.x; x++)
                    for (int y = 0; y < dims.y; y++)
                        for (int cz = 0; cz < dims.z; cz++)
                        {
                            Vector3Int c = new Vector3Int(x, y, cz);
                            if (z.Contains(grid.CellToWorld(c))) grid.SetSolid(c, true);
                        }
            }
        }
    }
}
