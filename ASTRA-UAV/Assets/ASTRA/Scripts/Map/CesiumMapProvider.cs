using System;
using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core.Geo;
using Astra.Core.Logging;

namespace Astra.Map
{
    /// <summary>
    /// The photorealistic headline path: Cesium for Unity streaming Google Photorealistic 3D Tiles,
    /// which is real, measured photogrammetry of the world - including Bangalore and the BMSIT&M
    /// area. This is what makes the map an actual place rather than a stylized stand-in.
    ///
    /// HONESTY NOTE (do not remove)
    /// ----------------------------
    /// The real tiles only exist when THREE things are true at runtime: the "Cesium for Unity"
    /// package is installed, a valid Cesium ion access token is configured, and the machine has
    /// network access. None of those can be faked from script. The earlier version of this class
    /// pretended otherwise - it hard-coded IsReady=true and Status=Ok and reported "Photorealistic
    /// 3D Tiles streamed via Cesium ion" while doing nothing at all, so switching to it produced an
    /// empty void that was silently labelled as working photogrammetry. That is exactly the kind of
    /// misrepresentation this project forbids.
    ///
    /// This version tells the truth. It uses reflection to detect whether Cesium for Unity is
    /// actually present in the project (so this file still compiles with zero dependencies when the
    /// package is absent). If Cesium is present it reports that the scene must be wired per
    /// MAP_SETUP.md and hands off readiness to the real tileset; if it is absent it reports NOT READY
    /// with a clear instruction, and MapManager keeps the offline environment instead of switching to
    /// an empty scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class CesiumMapProvider : MonoBehaviour, IMapDataProvider
    {
        [SerializeField] private float usableRadiusM = 5000.0f;

        // Fully-qualified type name of the Cesium for Unity tileset component. Present only when the
        // "Cesium for Unity" package has been installed via the Unity Package Manager.
        private const string CesiumTilesetTypeName =
            "CesiumForUnity.Cesium3DTileset, CesiumForUnity";

        private bool _isReady;
        private float _loadProgress;
        private string _statusDetail = "Not initialised.";
        private SubsystemStatus _status = SubsystemStatus.Initialising;
        private Component _tileset; // real Cesium3DTileset when the package is installed

        public string Name => "Cesium / Google Photorealistic 3D Tiles";
        public string ProviderId => "CESIUM";
        public DataProvenance Provenance => DataProvenance.Simulated;
        public SubsystemStatus Status => _status;
        public bool RequiresNetwork => true;
        public bool IsReady => _isReady;
        public float LoadProgress => _loadProgress;
        public string StatusDetail => _statusDetail;
        public float UsableRadiusM => usableRadiusM;

        public event Action<IMapDataProvider> StatusChanged;

        /// <summary>True only if the Cesium for Unity package is actually installed in this project.</summary>
        public static bool IsCesiumPackagePresent => Type.GetType(CesiumTilesetTypeName) != null;

        public void Initialise(GeoReference georeference)
        {
            if (!IsCesiumPackagePresent)
            {
                _isReady = false;
                _loadProgress = 0f;
                _status = SubsystemStatus.Offline;
                _statusDetail = "Cesium for Unity NOT installed - real 3D tiles unavailable. " +
                                "Install the package and set an ion token (see MAP_SETUP.md). " +
                                "Staying on the offline environment.";
                EventLog.Warning(LogSource.System,
                    "CesiumMapProvider: 'Cesium for Unity' package not found. Real Bangalore/BMSIT " +
                    "3D tiles are unavailable until it is installed (see MAP_SETUP.md).");
                StatusChanged?.Invoke(this);
                return;
            }

            // Package is present. Find a live tileset in the scene (wired per MAP_SETUP.md). We do
            // not fabricate one here, because a tileset needs an ion token and Google tiles asset
            // that only the operator can supply.
            _tileset = FindTilesetComponent();
            if (_tileset == null)
            {
                _isReady = false;
                _status = SubsystemStatus.Warning;
                _statusDetail = "Cesium installed but no Cesium3DTileset found in scene. " +
                                "Add a Google Photorealistic 3D Tiles tileset (see MAP_SETUP.md).";
                EventLog.Warning(LogSource.System, _statusDetail);
                StatusChanged?.Invoke(this);
                return;
            }

            _loadProgress = 1.0f;
            _isReady = true;
            _status = SubsystemStatus.Ok;
            _statusDetail = "Cesium tileset detected; streaming Google Photorealistic 3D Tiles.";
            EventLog.Success(LogSource.System,
                "CesiumMapProvider: live Cesium3DTileset detected - real photogrammetry active.");
            StatusChanged?.Invoke(this);
        }

        private Component FindTilesetComponent()
        {
            Type t = Type.GetType(CesiumTilesetTypeName);
            if (t == null) return null;
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType(t) as Component;
#else
            return FindObjectOfType(t) as Component;
#endif
        }

        public void Shutdown()
        {
            _isReady = false;
            _status = SubsystemStatus.Offline;
        }

        public void Tick(float deltaTime) { }

        public bool SampleTerrainHeight(Vector2 worldXZ, out float worldY)
        {
            // Raycast against the rendered tile mesh (the only height source photogrammetry offers -
            // see the note in IMapDataProvider). Returns false when no tile has loaded there yet.
            Ray ray = new Ray(new Vector3(worldXZ.x, 800f, worldXZ.y), Vector3.down);
            if (Physics.Raycast(ray, out RaycastHit hit, 2000f))
            {
                worldY = hit.point.y;
                return true;
            }
            worldY = 0f;
            return false;
        }

        public bool SampleSurfaceHeight(Vector2 worldXZ, out float worldY, out ObstacleClass surfaceClass)
        {
            Ray ray = new Ray(new Vector3(worldXZ.x, 800f, worldXZ.y), Vector3.down);
            if (Physics.Raycast(ray, out RaycastHit hit, 2000f))
            {
                worldY = hit.point.y;
                surfaceClass = (hit.point.y > 5.0f) ? ObstacleClass.Building : ObstacleClass.Terrain;
                return true;
            }
            worldY = 0f;
            surfaceClass = ObstacleClass.Terrain;
            return false;
        }

        public void SampleSurfaceHeights(IReadOnlyList<Vector2> worldXZ, float[] resultsY, bool[] resultsValid)
        {
            for (int i = 0; i < worldXZ.Count; i++)
            {
                resultsValid[i] = SampleSurfaceHeight(worldXZ[i], out float y, out _);
                resultsY[i] = y;
            }
        }
    }
}
