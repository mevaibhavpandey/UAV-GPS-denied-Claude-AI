# Map System (ASTRA spec Sec 7, 44–47)

> SIMULATED environment. The offline map is a STYLIZED, NOT survey-accurate representation of the
> BMSIT&M / Bangalore area. Coordinates default to the BMSIT origin (13.1320°N, 77.5670°E, ~890 m)
> and are labelled APPROXIMATE / UNVERIFIED throughout.

## Two backends

`Map/MapManager.cs` switches between two providers at runtime (F9, or from the pre-flight setup):

- **Offline Stylized City** (`OfflineMapProvider`) — a procedurally-styled Bangalore/BMSIT
  environment. Always available. NOT survey-accurate; it is a plausible stand-in, not a geospatial
  dataset.
- **Cesium Photoreal** (`CesiumMapProvider`) — requests real Google Photorealistic 3D Tiles. It
  only actually engages if Cesium for Unity plus an ion token are configured (see `../MAP_SETUP.md`);
  otherwise it logs the reason and stays on the offline environment. This honest fallback means the
  app never pretends to show real tiles it could not load.

## Geo anchoring

`GeoReference` anchors the Unity world to a chosen launch point. On mission commit,
`MissionSetupManager` calls `SetOrigin(home, "Operator-configured launch point (UNVERIFIED)")` so
the label always reflects that the coordinate is configured, not verified. `GeoReference` converts
between geographic coordinates and Unity world positions (`ToUnityAtHeight`, etc.).

## Setup requirement

Real photorealistic tiles require Unity-Editor-side setup (package + ion token). This cannot be done
in the headless sandbox; the offline environment is the default and needs no setup. See
`../MAP_SETUP.md`.

## Static obstacles feed the planner (global avoidance)

The procedural `OfflineMapProvider` knows every building box it generated, so it implements the
optional `IStaticObstacleSource` capability. `AutonomyController` stamps those boxes into its
`OccupancyGrid` once per change (inflated by `staticObstacleInflationM`, default one voxel), so
nominal routes clear known structures **globally** instead of only reacting to them at close range.
This is additive: perception still handles everything dynamic or unmodelled, and it can be disabled
with the `ingestStaticObstacles` toggle.

`CesiumMapProvider` deliberately does NOT implement `IStaticObstacleSource` — Google's fused
photogrammetric mesh has no per-building semantics to enumerate — so under Cesium the planner relies
on height-sampling and reactive perception, and this ingestion is a no-op. Keeping the capability
optional makes that honest distinction explicit rather than pretending Cesium exposes footprints it
does not. Verify building-avoidance behaviour in the Unity Editor; structural validation does not run
the planner.
