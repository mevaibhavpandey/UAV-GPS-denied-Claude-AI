using System;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Geo;
using Astra.Core.Logging;

namespace Astra.Map
{
    /// <summary>
    /// Coordinates map providers and allows seamless live switching between Cesium and Offline procedural city.
    /// Handles map raycasting for Home and Target waypoint placement.
    /// </summary>
    [DisallowMultipleComponent]
    public class MapManager : MonoBehaviour
    {
        [Header("Providers")]
        [SerializeField] private OfflineMapProvider offlineProvider;
        [SerializeField] private CesiumMapProvider cesiumProvider;

        [Header("Active State")]
        [SerializeField] private bool useCesiumByDefault = false;

        private IMapDataProvider _activeProvider;

        public IMapDataProvider ActiveProvider => _activeProvider;

        private void Awake()
        {
            if (offlineProvider == null) offlineProvider = GetComponentInChildren<OfflineMapProvider>();
            if (cesiumProvider == null) cesiumProvider = GetComponentInChildren<CesiumMapProvider>();
        }

        private void Start()
        {
            if (useCesiumByDefault && cesiumProvider != null)
            {
                SwitchToProvider(cesiumProvider);
            }
            else if (offlineProvider != null)
            {
                SwitchToProvider(offlineProvider);
            }
        }

        private void Update()
        {
            // F9 shortcut toggles Map Backend between Cesium and Offline
            if (Input.GetKeyDown(KeyCode.F9))
            {
                ToggleMapProvider();
            }
        }

        public void ToggleMapProvider()
        {
            if (_activeProvider == offlineProvider && cesiumProvider != null)
            {
                SwitchToProvider(cesiumProvider);
            }
            else if (offlineProvider != null)
            {
                SwitchToProvider(offlineProvider);
            }
        }

        public void SwitchToProvider(IMapDataProvider newProvider)
        {
            if (newProvider == null || _activeProvider == newProvider) return;

            _activeProvider?.Shutdown();
            _activeProvider = newProvider;
            _activeProvider.Initialise(GeoReference.Instance);

            AstraServices.Register<IMapDataProvider>(_activeProvider);
            AstraEvents.RaiseMapProviderChanged(_activeProvider);

            EventLog.Info(LogSource.System, $"Map provider switched to: {_activeProvider.Name}");
        }

        public bool RaycastMapPosition(Ray ray, out Vector3 worldHitPos)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, 2000f))
            {
                worldHitPos = hit.point;
                return true;
            }
            // Fallback plane intersection at Y=0
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float enter))
            {
                worldHitPos = ray.GetPoint(enter);
                return true;
            }
            worldHitPos = Vector3.zero;
            return false;
        }
    }
}
