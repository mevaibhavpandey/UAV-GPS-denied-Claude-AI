using System.Collections.Generic;
using UnityEngine;

namespace Astra.Navigation
{
    /// <summary>
    /// Smooths discrete voxel grid waypoints into flyable continuous trajectories using Catmull-Rom splines.
    /// </summary>
    public static class TrajectorySmoother
    {
        public static List<Vector3> SmoothPath(IReadOnlyList<Vector3> rawWaypoints, int subdivisionsPerSegment = 5)
        {
            if (rawWaypoints == null || rawWaypoints.Count < 2)
            {
                return new List<Vector3>(rawWaypoints ?? new Vector3[0]);
            }

            // 1. Line-of-sight shortcutting
            List<Vector3> shortcut = ShortcutWaypoints(rawWaypoints);
            if (shortcut.Count < 3)
            {
                return shortcut;
            }

            // 2. Catmull-Rom Spline Interpolation
            List<Vector3> smoothed = new List<Vector3>();
            int count = shortcut.Count;

            for (int i = 0; i < count - 1; i++)
            {
                Vector3 p0 = i == 0 ? shortcut[0] : shortcut[i - 1];
                Vector3 p1 = shortcut[i];
                Vector3 p2 = shortcut[i + 1];
                Vector3 p3 = (i + 2 < count) ? shortcut[i + 2] : p2;

                for (int s = 0; s < subdivisionsPerSegment; s++)
                {
                    float t = (float)s / subdivisionsPerSegment;
                    smoothed.Add(EvaluateCatmullRom(p0, p1, p2, p3, t));
                }
            }
            smoothed.Add(shortcut[count - 1]);

            return smoothed;
        }

        private static List<Vector3> ShortcutWaypoints(IReadOnlyList<Vector3> waypoints)
        {
            List<Vector3> result = new List<Vector3>();
            result.Add(waypoints[0]);

            int curr = 0;
            while (curr < waypoints.Count - 1)
            {
                int furthest = curr + 1;
                for (int next = curr + 2; next < waypoints.Count; next++)
                {
                    // Check direct ray clearance
                    Vector3 from = waypoints[curr];
                    Vector3 to = waypoints[next];
                    Vector3 dir = to - from;
                    float dist = dir.magnitude;

                    if (!Physics.SphereCast(from, 3.0f, dir.normalized, out RaycastHit hit, dist))
                    {
                        furthest = next;
                    }
                }
                result.Add(waypoints[furthest]);
                curr = furthest;
            }
            return result;
        }

        private static Vector3 EvaluateCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                (2.0f * p1) +
                (-p0 + p2) * t +
                (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2 +
                (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3
            );
        }
    }
}
