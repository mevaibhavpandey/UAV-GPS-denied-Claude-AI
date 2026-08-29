using System;
using System.Collections.Generic;
using UnityEngine;
using Astra.Core.Geo;

namespace Astra.Contracts
{
    /// <summary>
    /// Supplies the geographic world: terrain height, building geometry, and the visual
    /// representation shown in Real World Map mode.
    ///
    /// WHY THIS IS AN INTERFACE
    /// ------------------------
    /// ASTRA's headline visual is Cesium streaming Google Photorealistic 3D Tiles. That is genuinely
    /// impressive on a projector, and it is what the team's browser prototype already used. It also
    /// has three failure modes that all land on presentation day: it needs live internet, it needs a
    /// valid Cesium ion token, and it needs quota that has not been exhausted.
    ///
    /// So map data is abstracted, and ASTRA ships two providers:
    ///
    ///   CesiumMapProvider   - photorealistic, network-dependent. The headline.
    ///   OfflineMapProvider  - procedurally generated city, zero dependencies. The parachute.
    ///
    /// The operator can switch between them with a single key at any time, including mid-flight,
    /// because the simulation state lives outside the map provider entirely. If the campus network
    /// fails thirty seconds before the demonstration, one keystroke keeps it running.
    ///
    /// A NOTE ON WHAT PHOTOREALISTIC TILES CANNOT GIVE YOU
    /// ---------------------------------------------------
    /// Google's 3D tiles are a single fused photogrammetric mesh. There is no per-building semantic
    /// data in them: no footprints, no heights, no object identities. You cannot query "give me the
    /// building at this point" because the tileset does not know what a building is. This is why the
    /// team's prototype sampled heights on a grid instead, and it is a real limitation rather than an
    /// implementation shortcut. SampleTerrainHeight below therefore raycasts against the rendered
    /// mesh, and the accuracy of the resulting occupancy grid is bounded by the sample spacing, not
    /// by the tile resolution. The planner reports that spacing to the operator for exactly this
    /// reason.
    /// </summary>
    public interface IMapDataProvider
    {
        /// <summary>Display name shown in the GCS, e.g. "Cesium / Google Photorealistic 3D Tiles".</summary>
        string Name { get; }

        /// <summary>Short identifier for logs, e.g. "CESIUM" or "OFFLINE".</summary>
        string ProviderId { get; }

        DataProvenance Provenance { get; }
        SubsystemStatus Status { get; }

        /// <summary>True if this provider needs network access to function.</summary>
        bool RequiresNetwork { get; }

        /// <summary>
        /// True once enough of the world has loaded to plan and fly over it. For a streaming
        /// provider this becomes true only after tiles around the origin have arrived, and the
        /// mission must not start before then.
        /// </summary>
        bool IsReady { get; }

        /// <summary>Progress of initial loading in [0,1], for the loading indicator.</summary>
        float LoadProgress { get; }

        /// <summary>
        /// Human-readable status detail, surfaced to the operator, e.g.
        /// "Streaming tiles (4 requests in flight)" or "Token rejected - falling back to offline".
        /// </summary>
        string StatusDetail { get; }

        /// <summary>Brings the provider up. Asynchronous completion is reported via IsReady.</summary>
        void Initialise(GeoReference georeference);

        /// <summary>Tears the provider down and releases its scene objects.</summary>
        void Shutdown();

        /// <summary>Called once per frame while active.</summary>
        void Tick(float deltaTime);

        /// <summary>
        /// Samples terrain height at a world XZ position, returning world Y in metres.
        ///
        /// Returns false if the height could not be determined - for a streaming provider that
        /// means the relevant tile has not loaded yet. Callers MUST handle false rather than
        /// treating a missing sample as ground level zero, because doing so would place the
        /// occupancy grid's floor below the terrain and let the planner route the aircraft
        /// underground.
        /// </summary>
        bool SampleTerrainHeight(Vector2 worldXZ, out float worldY);

        /// <summary>
        /// Samples the height of the topmost surface at a world XZ position, including buildings.
        /// This is the figure the planning grid needs: the ceiling of the obstruction it must clear.
        /// </summary>
        bool SampleSurfaceHeight(Vector2 worldXZ, out float worldY, out ObstacleClass surfaceClass);

        /// <summary>
        /// Batch height sampling. A batch call exists because sampling a planning grid means tens of
        /// thousands of queries, and per-query overhead dominates if each is issued individually.
        /// Implementations may parallelise or use Unity's batched raycast API.
        /// </summary>
        void SampleSurfaceHeights(IReadOnlyList<Vector2> worldXZ, float[] resultsY, bool[] resultsValid);

        /// <summary>
        /// The horizontal extent of usable map data around the origin, in metres. Beyond this the
        /// provider cannot answer height queries and missions must not be planned.
        /// </summary>
        float UsableRadiusM { get; }

        /// <summary>Raised when the provider's readiness or status changes.</summary>
        event Action<IMapDataProvider> StatusChanged;
    }

    /// <summary>
    /// Which visualisation mode the map is being presented in. Both modes are driven by the same
    /// simulation state; only the rendering differs.
    /// </summary>
    public enum MapViewMode
    {
        /// <summary>Geographic visualisation: photoreal or procedural city, roads, terrain.</summary>
        RealWorld = 0,

        /// <summary>
        /// Monochrome machine-perception visualisation showing what the autonomy stack has actually
        /// observed, as opposed to what is really there. The difference between the two views is
        /// itself the point: it makes the gap between world and perceived-world visible.
        /// </summary>
        Perception = 1
    }
}
