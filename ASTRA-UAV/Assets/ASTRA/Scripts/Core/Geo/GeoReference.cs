using UnityEngine;

namespace Astra.Core.Geo
{
    /// <summary>
    /// Holds the single geodetic origin that anchors Unity world space to the real world.
    ///
    /// Every part of ASTRA that converts between map coordinates and Unity coordinates goes
    /// through this component, so that the 3D world, the 2D map panel, the perception view and
    /// the Cesium tileset can never silently disagree about where a point is. That single-source
    /// -of-truth property is what makes the two visualisation modes represent the same
    /// simulation state rather than two unrelated animations.
    ///
    /// WHY AN ORIGIN AT ALL: Unity's world space is single-precision. If we placed the aircraft
    /// at raw ECEF coordinates (~6.4 million metres from the origin) a float would resolve only
    /// about half a metre, and the aircraft would visibly jitter and the physics would degrade.
    /// Anchoring a local tangent plane near the operating area keeps every coordinate small and
    /// therefore precise. This is the same technique Cesium's georeference uses.
    /// </summary>
    [DisallowMultipleComponent]
    public class GeoReference : MonoBehaviour
    {
        // ------------------------------------------------------------------------------------
        // Origin
        // ------------------------------------------------------------------------------------

        [Header("World Origin (anchors Unity 0,0,0 to a real location)")]
        [Tooltip("Latitude of the Unity world origin, in degrees.")]
        [SerializeField] private double originLatitude = 13.1320;

        [Tooltip("Longitude of the Unity world origin, in degrees.")]
        [SerializeField] private double originLongitude = 77.5670;

        [Tooltip("Ellipsoidal altitude of the Unity world origin, in metres. " +
                 "This is the terrain height at the origin, not the aircraft altitude.")]
        [SerializeField] private double originAltitude = 890.0;

        [Header("Site")]
        [Tooltip("Human-readable name of the operating site, shown in the GCS header.")]
        [SerializeField] private string siteName = "BMSIT&M Campus (COORDINATES UNVERIFIED)";

        // ------------------------------------------------------------------------------------
        // Singleton access
        // ------------------------------------------------------------------------------------

        private static GeoReference _instance;

        /// <summary>
        /// The active georeference. Returns null rather than creating one implicitly, because a
        /// silently-created origin at (0,0) in the Gulf of Guinea is a confusing failure mode.
        /// Callers should check for null and log clearly.
        /// </summary>
        public static GeoReference Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<GeoReference>();
                }
                return _instance;
            }
        }

        public static bool Exists
        {
            get { return Instance != null; }
        }

        // ------------------------------------------------------------------------------------
        // Properties
        // ------------------------------------------------------------------------------------

        /// <summary>The geodetic coordinate that corresponds to Unity world position (0,0,0).</summary>
        public GeoCoordinate Origin
        {
            get { return new GeoCoordinate(originLatitude, originLongitude, originAltitude); }
        }

        public string SiteName
        {
            get { return siteName; }
        }

        // ------------------------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------------------------

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning(
                    "[GeoReference] A second GeoReference was found on '" + name +
                    "'. Only one may be active, because two origins would put the map and the " +
                    "3D world into different coordinate frames. Disabling this one.", this);
                enabled = false;
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        // ------------------------------------------------------------------------------------
        // Conversions
        // ------------------------------------------------------------------------------------

        /// <summary>Geodetic coordinate to Unity world position.</summary>
        public Vector3 ToUnity(GeoCoordinate geo)
        {
            return GeoMath.GeodeticToUnity(geo, Origin);
        }

        /// <summary>
        /// Geodetic coordinate to Unity world position, overriding the vertical with a height
        /// measured in metres above the world origin's terrain level. This is the form mission
        /// code usually wants, because operators think in "50 m above the ground", not in
        /// ellipsoidal height.
        /// </summary>
        public Vector3 ToUnityAtHeight(GeoCoordinate geo, float heightAboveOrigin)
        {
            Vector3 p = GeoMath.GeodeticToUnity(geo, Origin);
            p.y = heightAboveOrigin;
            return p;
        }

        /// <summary>Unity world position to geodetic coordinate.</summary>
        public GeoCoordinate ToGeo(Vector3 unityPosition)
        {
            return GeoMath.UnityToGeodetic(unityPosition, Origin);
        }

        /// <summary>
        /// Reassigns the origin at runtime. Everything already placed in Unity space will now
        /// refer to different real-world coordinates, so this must only be called during setup,
        /// before any mission state exists.
        /// </summary>
        public void SetOrigin(GeoCoordinate newOrigin, string newSiteName = null)
        {
            originLatitude = newOrigin.Latitude;
            originLongitude = newOrigin.Longitude;
            originAltitude = newOrigin.Altitude;
            if (!string.IsNullOrEmpty(newSiteName))
            {
                siteName = newSiteName;
            }
            Debug.Log("[GeoReference] Origin set to " + newOrigin + " (" + siteName + ")");
        }

        // ------------------------------------------------------------------------------------
        // Editor validation
        // ------------------------------------------------------------------------------------

        private void OnValidate()
        {
            originLatitude = Mathf.Clamp((float)originLatitude, -90f, 90f);
            originLongitude = Mathf.Clamp((float)originLongitude, -180f, 180f);
        }
    }
}
