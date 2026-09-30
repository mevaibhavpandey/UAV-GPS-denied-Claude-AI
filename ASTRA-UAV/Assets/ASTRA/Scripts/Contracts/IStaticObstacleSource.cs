using System.Collections.Generic;
using UnityEngine;

namespace Astra.Contracts
{
    /// <summary>
    /// One static, axis-aligned obstacle volume a map provider knows about a priori - typically a
    /// building. World-space, metres.
    /// </summary>
    public struct StaticObstacleBox
    {
        public Vector3 Center;
        public Vector3 HalfExtents;
        public ObstacleClass Class;

        public StaticObstacleBox(Vector3 center, Vector3 halfExtents, ObstacleClass cls)
        {
            Center = center;
            HalfExtents = halfExtents;
            Class = cls;
        }
    }

    /// <summary>
    /// Optional capability for a map provider that KNOWS its static obstacle geometry a priori and
    /// can therefore hand it to the planner for GLOBAL avoidance, instead of leaving buildings to be
    /// discovered reactively by perception.
    ///
    /// WHY THIS IS SEPARATE FROM IMapDataProvider (AND HONEST)
    /// ------------------------------------------------------
    /// Only a provider that generated its own geometry can implement this truthfully. The procedural
    /// OfflineMapProvider knows every building box exactly, so it implements it. The Cesium provider
    /// deliberately does NOT: Google's Photorealistic 3D Tiles are a single fused photogrammetric
    /// mesh with no per-building semantics (see IMapDataProvider), so it genuinely cannot enumerate
    /// buildings - it can only be height-sampled or raycast. Making this a separate, optional
    /// interface keeps that distinction explicit rather than pretending Cesium has data it does not.
    ///
    /// A provider that implements this does not REPLACE reactive perception; it seeds the planner so
    /// nominal routes already clear known static structures, and perception still handles everything
    /// dynamic or unmodelled.
    /// </summary>
    public interface IStaticObstacleSource
    {
        /// <summary>True if this provider currently has static obstacle geometry to hand out.</summary>
        bool HasStaticObstacles { get; }

        /// <summary>
        /// A monotonically increasing version that changes whenever the static geometry changes, so
        /// the planner can re-ingest only when needed rather than every cycle.
        /// </summary>
        int StaticObstacleVersion { get; }

        /// <summary>Appends the current static obstacle boxes into the supplied list (cleared first).</summary>
        void GetStaticObstacles(List<StaticObstacleBox> into);
    }
}
