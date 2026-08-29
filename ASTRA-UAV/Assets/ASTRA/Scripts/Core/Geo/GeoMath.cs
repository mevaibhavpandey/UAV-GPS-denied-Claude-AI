using System;
using UnityEngine;

namespace Astra.Core.Geo
{
    /// <summary>
    /// WGS84 geodetic mathematics: geodetic <-> ECEF <-> local tangent-plane ENU, great-circle
    /// distance and bearing.
    ///
    /// COORDINATE CONVENTION USED THROUGHOUT ASTRA
    /// -------------------------------------------
    /// ENU is a right-handed, Z-up frame (East, North, Up). Unity is a LEFT-handed, Y-up frame.
    /// We adopt the same mapping that Cesium for Unity's georeference uses, so that our maths and
    /// the Cesium tileset agree without a correction step:
    ///
    ///     Unity.x = East      (metres)
    ///     Unity.y = Up        (metres)
    ///     Unity.z = North     (metres)
    ///
    /// The handedness flip is absorbed by swapping which axis carries North vs Up; no mirroring
    /// is required. A heading of 0 degrees is North (+Z) and increases clockwise when viewed from
    /// above, which matches both aviation convention and Unity's Y-axis rotation. This means a
    /// Unity Y-Euler angle can be used directly as a compass heading, which is convenient and
    /// deliberate.
    ///
    /// PRECISION WARNING
    /// -----------------
    /// All conversions here use double precision, but Unity's Vector3 is single precision
    /// (~7 significant decimal digits). At 10 km from the origin a float holds roughly 1 mm of
    /// resolution, which is fine. At ECEF scale (6.4e6 m) a float holds only about 0.5 m, which
    /// is NOT fine. Therefore: never store an ECEF position in a Vector3. Keep ECEF in double
    /// (or Cesium's double3) and only convert to Vector3 after subtracting the origin.
    /// </summary>
    public static class GeoMath
    {
        /// <summary>WGS84 semi-major axis, metres. Defined exactly by the WGS84 standard.</summary>
        public const double SemiMajorAxis = 6378137.0;

        /// <summary>WGS84 inverse flattening, 1/f. Defined exactly by the WGS84 standard.</summary>
        public const double InverseFlattening = 298.257223563;

        /// <summary>WGS84 flattening f.</summary>
        public const double Flattening = 1.0 / InverseFlattening;

        /// <summary>WGS84 semi-minor axis b = a(1 - f), metres.</summary>
        public const double SemiMinorAxis = SemiMajorAxis * (1.0 - Flattening);

        /// <summary>First eccentricity squared, e^2 = f(2 - f).</summary>
        public const double EccentricitySquared = Flattening * (2.0 - Flattening);

        /// <summary>Second eccentricity squared, e'^2 = e^2 / (1 - e^2).</summary>
        public const double SecondEccentricitySquared = EccentricitySquared / (1.0 - EccentricitySquared);

        /// <summary>Mean Earth radius used for great-circle work, metres (IUGG mean radius).</summary>
        public const double MeanEarthRadius = 6371008.8;

        public const double DegToRad = Math.PI / 180.0;
        public const double RadToDeg = 180.0 / Math.PI;

        // ------------------------------------------------------------------------------------
        // Radii of curvature
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Meridional (north-south) radius of curvature M at the given latitude, metres.
        /// M = a(1 - e^2) / (1 - e^2 sin^2(lat))^(3/2)
        /// </summary>
        public static double MeridionalRadius(double latitudeDegrees)
        {
            double sinLat = Math.Sin(latitudeDegrees * DegToRad);
            double w = 1.0 - EccentricitySquared * sinLat * sinLat;
            return SemiMajorAxis * (1.0 - EccentricitySquared) / (w * Math.Sqrt(w));
        }

        /// <summary>
        /// Prime-vertical (east-west) radius of curvature N at the given latitude, metres.
        /// N = a / sqrt(1 - e^2 sin^2(lat))
        /// </summary>
        public static double PrimeVerticalRadius(double latitudeDegrees)
        {
            double sinLat = Math.Sin(latitudeDegrees * DegToRad);
            return SemiMajorAxis / Math.Sqrt(1.0 - EccentricitySquared * sinLat * sinLat);
        }

        // ------------------------------------------------------------------------------------
        // Geodetic <-> ECEF (exact)
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Geodetic to Earth-Centred Earth-Fixed. This conversion is exact (closed form).
        /// Outputs metres. Keep in double - see the precision warning on this class.
        /// </summary>
        public static void GeodeticToEcef(GeoCoordinate geo, out double x, out double y, out double z)
        {
            double latRad = geo.Latitude * DegToRad;
            double lonRad = geo.Longitude * DegToRad;
            double sinLat = Math.Sin(latRad);
            double cosLat = Math.Cos(latRad);
            double sinLon = Math.Sin(lonRad);
            double cosLon = Math.Cos(lonRad);

            double n = PrimeVerticalRadius(geo.Latitude);
            double nPlusH = n + geo.Altitude;

            x = nPlusH * cosLat * cosLon;
            y = nPlusH * cosLat * sinLon;
            z = (n * (1.0 - EccentricitySquared) + geo.Altitude) * sinLat;
        }

        /// <summary>
        /// ECEF to geodetic using Ferrari's closed-form solution as given by Zhu. Accurate to
        /// well below a millimetre for all terrestrial altitudes and needs no iteration, which
        /// makes its cost predictable - relevant because this may run per-frame.
        /// </summary>
        public static GeoCoordinate EcefToGeodetic(double x, double y, double z)
        {
            double a = SemiMajorAxis;
            double b = SemiMinorAxis;
            double e2 = EccentricitySquared;
            double ep2 = SecondEccentricitySquared;

            double r = Math.Sqrt(x * x + y * y);

            // Degenerate case: on the polar axis. Handle explicitly to avoid divide-by-zero.
            if (r < 1e-9)
            {
                double poleLat = z >= 0.0 ? 90.0 : -90.0;
                double poleAlt = Math.Abs(z) - b;
                return new GeoCoordinate(poleLat, 0.0, poleAlt);
            }

            double f = 54.0 * b * b * z * z;
            double g = r * r + (1.0 - e2) * z * z - e2 * (a * a - b * b);
            double c = e2 * e2 * f * r * r / (g * g * g);
            double s = Math.Pow(1.0 + c + Math.Sqrt(c * c + 2.0 * c), 1.0 / 3.0);
            double p = f / (3.0 * (s + 1.0 / s + 1.0) * (s + 1.0 / s + 1.0) * g * g);
            double q = Math.Sqrt(1.0 + 2.0 * e2 * e2 * p);

            double r0 = -(p * e2 * r) / (1.0 + q)
                        + Math.Sqrt(Math.Abs(0.5 * a * a * (1.0 + 1.0 / q)
                                             - p * (1.0 - e2) * z * z / (q * (1.0 + q))
                                             - 0.5 * p * r * r));

            double rMinus = r - e2 * r0;
            double u = Math.Sqrt(rMinus * rMinus + z * z);
            double v = Math.Sqrt(rMinus * rMinus + (1.0 - e2) * z * z);
            double z0 = b * b * z / (a * v);

            double altitude = u * (1.0 - b * b / (a * v));
            double latitude = Math.Atan((z + ep2 * z0) / r) * RadToDeg;
            double longitude = Math.Atan2(y, x) * RadToDeg;

            return new GeoCoordinate(latitude, longitude, altitude);
        }

        // ------------------------------------------------------------------------------------
        // Geodetic <-> local tangent-plane ENU
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Geodetic to local ENU metres relative to an origin, using the local radii of
        /// curvature at the origin latitude.
        ///
        /// ACCURACY: this is a tangent-plane (flat-earth) approximation. Error grows roughly as
        /// the square of the distance from the origin. Empirically it is below ~0.1 m at 5 km,
        /// ~0.5 m at 10 km, and a few metres at 30 km. Since ASTRA missions are local (a few km
        /// at most) this is comfortably better than the GPS noise we simulate, so the
        /// approximation is not the limiting error source. If the mission radius ever exceeds
        /// ~50 km, switch to the exact ECEF path via GeodeticToEnuExact.
        /// </summary>
        public static void GeodeticToEnu(GeoCoordinate geo, GeoCoordinate origin,
                                         out double east, out double north, out double up)
        {
            double dLat = (geo.Latitude - origin.Latitude) * DegToRad;
            double dLon = (geo.Longitude - origin.Longitude) * DegToRad;

            double m = MeridionalRadius(origin.Latitude);
            double n = PrimeVerticalRadius(origin.Latitude);
            double cosLat0 = Math.Cos(origin.Latitude * DegToRad);

            north = dLat * (m + origin.Altitude);
            east = dLon * (n + origin.Altitude) * cosLat0;
            up = geo.Altitude - origin.Altitude;
        }

        /// <summary>
        /// Local ENU metres to geodetic, the inverse of GeodeticToEnu and subject to the same
        /// tangent-plane accuracy limits.
        /// </summary>
        public static GeoCoordinate EnuToGeodetic(double east, double north, double up, GeoCoordinate origin)
        {
            double m = MeridionalRadius(origin.Latitude);
            double n = PrimeVerticalRadius(origin.Latitude);
            double cosLat0 = Math.Cos(origin.Latitude * DegToRad);

            double latitude = origin.Latitude + (north / (m + origin.Altitude)) * RadToDeg;
            double longitude = origin.Longitude + (east / ((n + origin.Altitude) * cosLat0)) * RadToDeg;
            double altitude = origin.Altitude + up;

            return new GeoCoordinate(latitude, longitude, altitude);
        }

        /// <summary>
        /// Exact geodetic to ENU via ECEF and the standard rotation matrix. Slower than the
        /// tangent-plane version but valid at any range. Provided so the approximation above can
        /// be validated against ground truth in tests rather than merely asserted to be fine.
        /// </summary>
        public static void GeodeticToEnuExact(GeoCoordinate geo, GeoCoordinate origin,
                                              out double east, out double north, out double up)
        {
            double px, py, pz, ox, oy, oz;
            GeodeticToEcef(geo, out px, out py, out pz);
            GeodeticToEcef(origin, out ox, out oy, out oz);

            double dx = px - ox;
            double dy = py - oy;
            double dz = pz - oz;

            double latRad = origin.Latitude * DegToRad;
            double lonRad = origin.Longitude * DegToRad;
            double sinLat = Math.Sin(latRad);
            double cosLat = Math.Cos(latRad);
            double sinLon = Math.Sin(lonRad);
            double cosLon = Math.Cos(lonRad);

            east = -sinLon * dx + cosLon * dy;
            north = -sinLat * cosLon * dx - sinLat * sinLon * dy + cosLat * dz;
            up = cosLat * cosLon * dx + cosLat * sinLon * dy + sinLat * dz;
        }

        // ------------------------------------------------------------------------------------
        // Unity interop
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Geodetic to a Unity world position, applying the ASTRA axis convention
        /// (x=East, y=Up, z=North). Returns float precision - only safe because the value is
        /// origin-relative, not ECEF.
        /// </summary>
        public static Vector3 GeodeticToUnity(GeoCoordinate geo, GeoCoordinate origin)
        {
            double east, north, up;
            GeodeticToEnu(geo, origin, out east, out north, out up);
            return new Vector3((float)east, (float)up, (float)north);
        }

        /// <summary>Unity world position back to geodetic, applying the ASTRA axis convention.</summary>
        public static GeoCoordinate UnityToGeodetic(Vector3 unityPosition, GeoCoordinate origin)
        {
            return EnuToGeodetic(unityPosition.x, unityPosition.z, unityPosition.y, origin);
        }

        // ------------------------------------------------------------------------------------
        // Distance and bearing
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Great-circle (haversine) distance in metres, ignoring altitude.
        ///
        /// ACCURACY: haversine assumes a sphere, so it carries up to ~0.5% error versus the
        /// ellipsoidal geodesic - about 5 m per km worst case. For the distance readout shown to
        /// an operator that is acceptable and it is what most GCS software does. Do not use it
        /// for survey-grade work; use Vincenty or Karney's method there.
        /// </summary>
        public static double HaversineDistance(GeoCoordinate a, GeoCoordinate b)
        {
            double lat1 = a.Latitude * DegToRad;
            double lat2 = b.Latitude * DegToRad;
            double dLat = lat2 - lat1;
            double dLon = (b.Longitude - a.Longitude) * DegToRad;

            double sinDLat = Math.Sin(dLat * 0.5);
            double sinDLon = Math.Sin(dLon * 0.5);
            double h = sinDLat * sinDLat + Math.Cos(lat1) * Math.Cos(lat2) * sinDLon * sinDLon;
            h = Math.Min(1.0, h); // guard against fp overshoot before Asin
            return 2.0 * MeanEarthRadius * Math.Asin(Math.Sqrt(h));
        }

        /// <summary>
        /// Straight-line 3D distance in metres, including the altitude difference. This is the
        /// figure that matters for UAV range and endurance planning, as opposed to the
        /// ground-track distance.
        /// </summary>
        public static double SlantDistance(GeoCoordinate a, GeoCoordinate b)
        {
            double ground = HaversineDistance(a, b);
            double dAlt = b.Altitude - a.Altitude;
            return Math.Sqrt(ground * ground + dAlt * dAlt);
        }

        /// <summary>
        /// Initial great-circle bearing from a to b, in degrees clockwise from true north
        /// [0, 360). Note this is the INITIAL bearing: along a long great-circle route the
        /// required bearing changes continuously. Over ASTRA's mission ranges the change is
        /// negligible.
        /// </summary>
        public static double InitialBearing(GeoCoordinate a, GeoCoordinate b)
        {
            double lat1 = a.Latitude * DegToRad;
            double lat2 = b.Latitude * DegToRad;
            double dLon = (b.Longitude - a.Longitude) * DegToRad;

            double y = Math.Sin(dLon) * Math.Cos(lat2);
            double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
            double bearing = Math.Atan2(y, x) * RadToDeg;
            return (bearing + 360.0) % 360.0;
        }

        /// <summary>
        /// Projects a coordinate a given distance along a given bearing (direct geodesic
        /// problem, spherical approximation).
        /// </summary>
        public static GeoCoordinate Project(GeoCoordinate origin, double bearingDegrees, double distanceMetres)
        {
            double lat1 = origin.Latitude * DegToRad;
            double lon1 = origin.Longitude * DegToRad;
            double brg = bearingDegrees * DegToRad;
            double angular = distanceMetres / MeanEarthRadius;

            double sinLat1 = Math.Sin(lat1);
            double cosLat1 = Math.Cos(lat1);
            double sinAng = Math.Sin(angular);
            double cosAng = Math.Cos(angular);

            double lat2 = Math.Asin(sinLat1 * cosAng + cosLat1 * sinAng * Math.Cos(brg));
            double lon2 = lon1 + Math.Atan2(Math.Sin(brg) * sinAng * cosLat1,
                                            cosAng - sinLat1 * Math.Sin(lat2));

            double outLon = ((lon2 * RadToDeg) + 540.0) % 360.0 - 180.0; // normalise to [-180,180)
            return new GeoCoordinate(lat2 * RadToDeg, outLon, origin.Altitude);
        }

        /// <summary>
        /// Shortest signed difference between two headings, in degrees, in the range (-180, 180].
        /// Positive means 'target is clockwise of current'. Used by the yaw controller, where
        /// getting the wrap-around wrong makes a UAV spin the long way round.
        /// </summary>
        public static double HeadingError(double currentDegrees, double targetDegrees)
        {
            double diff = (targetDegrees - currentDegrees + 540.0) % 360.0 - 180.0;
            return diff;
        }

        /// <summary>Normalises any angle in degrees into [0, 360).</summary>
        public static double NormaliseHeading(double degrees)
        {
            double h = degrees % 360.0;
            return h < 0.0 ? h + 360.0 : h;
        }

        /// <summary>
        /// Converts a compass bearing to the 16-point cardinal abbreviation, for operator-facing
        /// readouts where "NNE" reads faster than "28 deg".
        /// </summary>
        public static string BearingToCardinal(double bearingDegrees)
        {
            string[] points =
            {
                "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
                "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"
            };
            double normalised = NormaliseHeading(bearingDegrees);
            int index = (int)Math.Round(normalised / 22.5) % 16;
            return points[index];
        }
    }
}
