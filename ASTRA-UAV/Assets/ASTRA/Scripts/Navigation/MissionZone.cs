using System.Collections.Generic;
using UnityEngine;

namespace Astra.Navigation
{
    /// <summary>
    /// A geofenced zone the planner and failsafe layer must respect (ASTRA spec Sec 40-43).
    ///
    /// ASTRA already had a single circular RADIUS geofence in the FailsafeManager. This model
    /// generalises that to named, shaped zones with a severity, so the mission can express no-fly
    /// volumes, threat/avoid volumes and advisory areas. A zone is either a vertical CYLINDER
    /// (centre + radius, optional altitude band) or a vertical PRISM over an XZ polygon.
    ///
    /// HONESTY: zones are SIMULATED planning/mission constructs. The <see cref="IsHiddenConstraint"/>
    /// flag models a "constraint the operator was not briefed on" for research scenarios; it is
    /// labelled SIMULATED MISSION CONSTRAINT wherever surfaced and confers no real-world meaning.
    /// </summary>
    public enum ZoneShape { Cylinder = 0, PolygonPrism = 1 }

    public enum ZoneSeverity
    {
        /// <summary>Advisory only. Raises a small planning cost; flight still permitted.</summary>
        Advisory = 0,

        /// <summary>Soft avoid. Large planning cost so the planner routes around it if it can.</summary>
        SoftAvoid = 1,

        /// <summary>Hard no-go. Cells inside are made non-traversable; the planner will not enter.</summary>
        NoGo = 2
    }

    public class MissionZone
    {
        public string Name = "Zone";
        public ZoneShape Shape = ZoneShape.Cylinder;
        public ZoneSeverity Severity = ZoneSeverity.SoftAvoid;

        // Cylinder parameters.
        public Vector3 Centre;
        public float RadiusM = 30f;

        // Polygon-prism parameters (XZ polygon; Y band shared with cylinder fields below).
        public readonly List<Vector2> PolygonXz = new List<Vector2>();

        // Altitude band (world Y). Default band is effectively "all altitudes".
        public float MinY = -1000f;
        public float MaxY = 1000f;

        /// <summary>See class docs: a research-scenario "unbriefed" constraint. SIMULATED only.</summary>
        public bool IsHiddenConstraint = false;

        public bool Contains(Vector3 world)
        {
            if (world.y < MinY || world.y > MaxY) return false;

            if (Shape == ZoneShape.Cylinder)
            {
                float dx = world.x - Centre.x;
                float dz = world.z - Centre.z;
                return dx * dx + dz * dz <= RadiusM * RadiusM;
            }
            return PointInPolygonXz(new Vector2(world.x, world.z));
        }

        /// <summary>Planning cost multiplier for a point (1 = no penalty). Ignores NoGo (handled as solid).</summary>
        public float CostMultiplierAt(Vector3 world)
        {
            if (Severity == ZoneSeverity.NoGo) return 1f;
            if (!Contains(world)) return 1f;
            return Severity == ZoneSeverity.SoftAvoid ? 8f : 2f;
        }

        private bool PointInPolygonXz(Vector2 p)
        {
            int n = PolygonXz.Count;
            if (n < 3) return false;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 a = PolygonXz[i];
                Vector2 b = PolygonXz[j];
                if (((a.y > p.y) != (b.y > p.y)) &&
                    (p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y + 1e-6f) + a.x))
                {
                    inside = !inside;
                }
            }
            return inside;
        }
    }
}
