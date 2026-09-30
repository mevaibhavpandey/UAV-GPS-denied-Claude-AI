# Georeferencing & Floating Origin (ASTRA spec: coordinate correctness, large-scale map)

> SIMULATED research platform. All coordinates here anchor the Unity simulation to the real world
> through a local tangent plane. The BMSIT&M site origin (≈13.1320°N, 77.5670°E, ≈890 m) is
> APPROXIMATE and UNVERIFIED — it is labelled as such in `GeoReference` and the GCS header, and no
> figure here is a surveyed or hardware measurement.

## Why an origin exists

Unity world space is single-precision. Placing the aircraft at raw ECEF coordinates (~6.4 million m
from Earth's centre) would leave a float resolving only ~0.5 m, so the aircraft would visibly jitter
and the physics integrator would degrade. ASTRA therefore anchors a **local East-North-Up tangent
plane** near the operating site so every Unity coordinate stays small and precise. This is the same
technique Cesium's georeference uses.

## The single source of truth

`Core/Geo/GeoReference.cs` is a `DisallowMultipleComponent` singleton holding the one geodetic
origin that corresponds to Unity `(0,0,0)`. Every conversion between map coordinates and Unity
coordinates goes through it, so the 3D world, the 2D map panel, the perception overlay and the
Cesium tileset can never silently disagree about where a point is. A second `GeoReference` disables
itself with a warning, because two origins would put the visualisations into different frames.

Conversions live in `Core/Geo/GeoMath.cs` (double precision throughout):

- `GeodeticToUnity` / `UnityToGeodetic` — geodetic ⇄ Unity, applying the ASTRA axis convention
  `x=East, y=Up, z=North`, matching Cesium for Unity so no correction step is needed.
- Exact `GeodeticToEcef` / `EcefToGeodetic` (Ferrari–Zhu closed form) plus the tangent-plane
  `GeodeticToEnu` used for local work. The exact ECEF path exists so the tangent-plane
  approximation can be **validated against ground truth in tests rather than merely asserted**.
- Great-circle distance/bearing, heading wrap (`HeadingError`), and projection helpers.

### Tangent-plane accuracy (honest limit)

`GeodeticToEnu` is a flat-earth approximation whose error grows roughly with the square of range:
below ~0.1 m at 5 km, ~0.5 m at 10 km, a few metres at 30 km. Since ASTRA missions are local
(a few km) this is well under the simulated GPS noise, so it is not the limiting error source. Above
~50 km the exact ECEF path (`GeodeticToEnuExact`) should be used instead.

## Floating origin / world rebasing (large-map precision)

Anchoring the origin fixes the *starting* precision, but an aircraft that then flies 20 km from the
origin has the same float-precision problem again — squarely in the 1–25 km range the spec asks
about. `Core/Geo/FloatingOriginManager.cs` closes this gap **additively**.

**Mechanism.** The manager is self-installing (`ASTRA_FloatingOrigin`, via
`RuntimeInitializeOnLoadMethod`), tracks the aircraft (resolved through the `IFlightController`
service, falling back to a scene search), and in `LateUpdate` checks the focus's horizontal
distance from the Unity origin. When it exceeds `rebaseThresholdM` it:

1. moves the geodetic origin by the equal-and-opposite amount first, via
   `GeoReference.RebaseOrigin(GeoMath.UnityToGeodetic(-shift, oldOrigin))`, so latitude/longitude
   are perfectly continuous across the translation;
2. translates every root transform in every loaded scene by `shift` (children move with parents);
3. raises `AstraEvents.WorldRebased(shift)` so subsystems that cache Unity-space positions add the
   same delta.

**Why it cannot perturb the flight.** Because the world and every cached setpoint shift by the
*same* vector, all relative geometry is preserved and every control error the flight controller
computes is unchanged. `FlightControlSystem` shifts its launch/target positions and altitudes on
`WorldRebased`; `AutonomyController` shifts its planned-path nodes and tracked waypoint. The rebase
is invisible to the control law, the camera framing, and the operator.

**Zero regression risk to the working demo.** The default threshold is deliberately large (2 km).
The current campus demo operates within a few hundred metres, so it **never rebases** — the tuned,
working flight behaviour is completely unaffected. The manager holds no control authority and never
sets the aircraft's position relative to the world; it only translates the shared frame. Horizontal
plane only: altitude is already origin-relative and small.

Diagnostics: `RebaseCount` and `TotalShiftMagnitude` are exposed for inspection, and each rebase
logs one line to the event log.

## Verify in the Unity Editor

Structural validation does not execute conversions or move the world. In the Editor: confirm the GCS
header shows the unverified-site label; fly a long synthetic mission past 2 km and confirm the
geographic readout stays continuous while the aircraft returns toward the origin (a rebase fired);
optionally run the exact-vs-tangent comparison in `GeoMath` over a range of distances.
