using System;

namespace Astra.Core.Geo
{
    /// <summary>
    /// A WGS84 geodetic coordinate: latitude/longitude in degrees, altitude in metres above
    /// the WGS84 ellipsoid.
    ///
    /// NOTE ON ALTITUDE DATUM: altitude here is ellipsoidal height (HAE), not orthometric
    /// height above mean sea level (MSL). Cesium works in ellipsoidal height, GPS receivers
    /// typically report MSL after applying a geoid model, and barometric altimeters report
    /// pressure altitude. These three are NOT interchangeable and differ by tens of metres in
    /// many parts of the world. For Bengaluru the geoid undulation is roughly -85 m, meaning
    /// ellipsoidal height is about 85 m LOWER than MSL height for the same physical point.
    ///
    /// Within this simulator we work almost entirely in relative altitude above the local
    /// terrain, which sidesteps the datum problem. The distinction is recorded here because it
    /// becomes a real source of error the moment real GPS hardware is connected.
    /// See Docs/09-Sensor-Fusion.md.
    /// </summary>
    [Serializable]
    public struct GeoCoordinate : IEquatable<GeoCoordinate>
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;

        public GeoCoordinate(double latitude, double longitude, double altitude = 0.0)
        {
            Latitude = latitude;
            Longitude = longitude;
            Altitude = altitude;
        }

        public static GeoCoordinate Zero
        {
            get { return new GeoCoordinate(0.0, 0.0, 0.0); }
        }

        /// <summary>True if latitude and longitude are inside their valid ranges.</summary>
        public bool IsValid
        {
            get
            {
                return Latitude >= -90.0 && Latitude <= 90.0 &&
                       Longitude >= -180.0 && Longitude <= 180.0 &&
                       !double.IsNaN(Latitude) && !double.IsNaN(Longitude) &&
                       !double.IsNaN(Altitude);
            }
        }

        public GeoCoordinate WithAltitude(double altitude)
        {
            return new GeoCoordinate(Latitude, Longitude, altitude);
        }

        /// <summary>
        /// Formats as a degrees-minutes-seconds string, the convention most ground control
        /// stations and survey documents use.
        /// </summary>
        public string ToDmsString()
        {
            return FormatDms(Latitude, "N", "S") + "  " + FormatDms(Longitude, "E", "W");
        }

        private static string FormatDms(double value, string positive, string negative)
        {
            string hemisphere = value >= 0.0 ? positive : negative;
            double abs = Math.Abs(value);
            int degrees = (int)Math.Floor(abs);
            double minutesFull = (abs - degrees) * 60.0;
            int minutes = (int)Math.Floor(minutesFull);
            double seconds = (minutesFull - minutes) * 60.0;
            return string.Format("{0}°{1:00}'{2:00.00}\"{3}", degrees, minutes, seconds, hemisphere);
        }

        /// <summary>Decimal degrees to 7 places, which is roughly 11 mm of ground resolution.</summary>
        public string ToDecimalString()
        {
            return string.Format("{0:F7}, {1:F7}", Latitude, Longitude);
        }

        public override string ToString()
        {
            return string.Format("({0:F7}, {1:F7}, {2:F1}m)", Latitude, Longitude, Altitude);
        }

        public bool Equals(GeoCoordinate other)
        {
            return Latitude.Equals(other.Latitude)
                   && Longitude.Equals(other.Longitude)
                   && Altitude.Equals(other.Altitude);
        }

        public override bool Equals(object obj)
        {
            return obj is GeoCoordinate && Equals((GeoCoordinate)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Latitude.GetHashCode();
                hash = (hash * 397) ^ Longitude.GetHashCode();
                hash = (hash * 397) ^ Altitude.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(GeoCoordinate a, GeoCoordinate b) { return a.Equals(b); }
        public static bool operator !=(GeoCoordinate a, GeoCoordinate b) { return !a.Equals(b); }
    }
}
