# ASTRA — Real 3D Map Setup (Cesium + Google Photorealistic 3D Tiles)

This guide turns the map from the stylized offline environment into **real, measured
photogrammetry of Bangalore**, centered on the BMS Institute of Technology & Management
(BMSIT&M) area. The offline environment is only the fallback ("parachute"); the real map is
streamed live by Cesium.

## Why this is a manual step

The photoreal tiles only exist at runtime when three things are true, and none of them can be
done from a script or in a sandbox — they require the Unity Editor and your own account:

1. The **Cesium for Unity** package is installed in the project.
2. A valid **Cesium ion access token** is configured.
3. The machine has **network access** while running.

The C# is already wired: `CesiumMapProvider` detects the package by reflection and reports
honestly whether real tiles are live. Until you complete the steps below it will report
"NOT READY" and the simulator stays on the offline environment (this is by design, not a bug).

## Steps (in the Unity Editor)

1. **Install Cesium for Unity**
   - `Window ▸ Package Manager ▸ + ▸ Add package from git URL`
   - Enter: `https://github.com/CesiumGS/cesium-unity.git`
   - (Or install "Cesium for Unity" from the Unity Asset Store / OpenUPM.)

2. **Sign in to Cesium ion and get a token**
   - Create a free account at https://ion.cesium.com
   - `Access Tokens ▸ Create token` (default scopes are fine), copy it.
   - In Unity: `Cesium ▸ Cesium` panel ▸ paste the token, or set it on the
     `CesiumGeoreference`/`CesiumIonServer` as prompted.

3. **Add the world and the Google tiles**
   - In the panel, choose **"Google Photorealistic 3D Tiles"** and click to add it to the scene.
     This creates a `CesiumGeoreference` with a child `Cesium3DTileset`.
   - `CesiumMapProvider.FindTilesetComponent()` will pick this up automatically.

4. **Anchor the georeference to BMSIT&M**
   - On the `CesiumGeoreference`, set the origin to the values ASTRA already uses in
     `GeoReference.cs`:
     - Latitude: **13.1320°N**
     - Longitude: **77.5670°E**
     - Height: **~890 m** (Bangalore plateau elevation)
   - ⚠️ **These coordinates are APPROXIMATE / UNVERIFIED.** They place the origin in the
     Avalahalli / Yelahanka area of north Bangalore near BMSIT&M, but they have not been
     survey-confirmed. Verify against an authoritative source before citing them as exact.

5. **Enable Cesium as the default (optional)**
   - On the `MapManager` component, tick **Use Cesium By Default**.
   - At runtime, press **F9** to toggle between the Cesium map and the offline environment at
     any time (including mid-flight) — the parachute for when the campus network drops.

## What you get

- Real building shells, roads and terrain of the BMSIT&M surroundings, streamed on demand.
- The autonomy stack samples heights by raycasting the rendered tile mesh (photogrammetry has no
  per-building semantics — see the note in `IMapDataProvider.cs`), so occupancy-grid accuracy is
  bounded by the sample spacing, which the planner reports.

## If tiles do not appear

- Check the token is valid and has not hit its free-tier quota.
- Confirm network access (corporate/campus firewalls often block tile CDNs).
- `CesiumMapProvider` will log the exact reason and keep the offline environment running so a
  demo never goes to a black screen.
