using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core.Geo;

namespace Astra.Mission
{
    /// <summary>
    /// Single source of truth for turning a <see cref="MissionType"/> plus a launch/objective pair
    /// and a few parameters into a concrete <see cref="MissionDefinition"/> (D2).
    ///
    /// Both the pre-flight setup screen (<see cref="MissionSetupManager"/>) and any programmatic
    /// caller build missions through here, so the per-type waypoint patterns are defined once and
    /// stay consistent. The autonomy executive then applies per-type flight behaviour by honouring
    /// the waypoint <see cref="WaypointKind"/>s and dwell times this builder assigns (notably the
    /// Observation holds that make Recon/Surveillance/Survey/Search behave differently in the air
    /// from a straight Point-to-Point run).
    ///
    /// Geometry is generated directly in geographic coordinates by offsetting metres from the
    /// objective, so a mission survives a change of scene origin or map backend (see the note on
    /// Waypoint). All missions produced here are SIMULATED.
    /// </summary>
    public static class MissionProfiles
    {
        /// <summary>
        /// Builds a mission of the given type. <paramref name="cruise"/>, <paramref name="speed"/>
        /// and <paramref name="margin"/> are clamped to sane ranges. Per-type shaping (pass count,
        /// survey rows, search rings, pattern radius, observation dwell) is read from the returned
        /// definition's defaults unless overridden by the caller afterwards; pass an optional
        /// pre-seeded <paramref name="template"/> to override them before waypoint generation.
        /// </summary>
        public static MissionDefinition Build(
            MissionType type,
            GeoCoordinate home,
            GeoCoordinate target,
            float cruise,
            float speed,
            float margin,
            MissionDefinition template = null)
        {
            MissionDefinition m = template ?? new MissionDefinition();
            m.Type = type;
            m.MissionName = "ASTRA " + Pretty(type) + " (SIMULATED)";
            m.Objective = Blurb(type);
            m.HomePosition = home;
            m.DefaultSpeedMps = Mathf.Clamp(speed, 1f, 25f);
            m.CruiseAltitudeM = Mathf.Clamp(cruise, 5f, 120f);
            m.SafetyMarginM = Mathf.Clamp(margin, 1f, 30f);
            m.Waypoints = new List<Waypoint>();

            float ca = m.CruiseAltitudeM;
            double tgtAlt = target.Altitude;
            float dwell = Mathf.Max(0f, m.ObservationDwellSeconds);
            float radius = Mathf.Clamp(m.PatternRadiusM, 10f, 200f);

            double mPerDegLat = 111320.0;
            double mPerDegLon = 111320.0 * System.Math.Cos(target.Latitude * System.Math.PI / 180.0);
            if (System.Math.Abs(mPerDegLon) < 1.0) mPerDegLon = 1.0;

            GeoCoordinate Mid(double frac) => new GeoCoordinate(
                home.Latitude + (target.Latitude - home.Latitude) * frac,
                home.Longitude + (target.Longitude - home.Longitude) * frac, ca);

            GeoCoordinate Off(double north, double east, double alt) => new GeoCoordinate(
                target.Latitude + north / mPerDegLat,
                target.Longitude + east / mPerDegLon, alt);

            Waypoint Obs(GeoCoordinate p, string label)
            {
                Waypoint w = Waypoint.Create(p, WaypointKind.Observation, label);
                w.DwellSeconds = dwell;
                return w;
            }

            GeoCoordinate objective = new GeoCoordinate(target.Latitude, target.Longitude, tgtAlt);

            // Every profile begins with a cruise-corridor transit toward the objective.
            m.Waypoints.Add(Waypoint.Create(Mid(0.5), WaypointKind.Transit, "Cruise Corridor"));

            switch (type)
            {
                case MissionType.PointToPoint:
                    break;

                case MissionType.Reconnaissance:
                    m.Waypoints.Add(Obs(Off(-radius, 0, ca), "Recon Hold South"));
                    m.Waypoints.Add(Obs(Off(0, radius, ca), "Recon Hold East"));
                    m.Waypoints.Add(Obs(Off(radius, 0, ca), "Recon Hold North"));
                    m.Waypoints.Add(Obs(Off(0, -radius, ca), "Recon Hold West"));
                    break;

                case MissionType.Surveillance:
                {
                    int passes = Mathf.Clamp(m.SurveillancePassCount, 1, 8);
                    for (int i = 0; i < passes; i++)
                    {
                        double north = (i - (passes - 1) * 0.5) * (radius * 0.6);
                        double east = (i % 2 == 0) ? -radius : radius;
                        m.Waypoints.Add(Obs(Off(north, east, ca), "Surveillance Pass " + (i + 1)));
                    }
                    break;
                }

                case MissionType.AreaSurvey:
                {
                    int rows = Mathf.Clamp(m.AreaSurveyRows, 1, 8);
                    int leg = 1;
                    for (int r = 0; r < rows; r++)
                    {
                        double north = (r - (rows - 1) * 0.5) * (radius * 0.6);
                        double e0 = (r % 2 == 0) ? -radius : radius;
                        double e1 = -e0;
                        m.Waypoints.Add(Obs(Off(north, e0, ca), "Survey Leg " + leg++));
                        m.Waypoints.Add(Obs(Off(north, e1, ca), "Survey Leg " + leg++));
                    }
                    break;
                }

                case MissionType.Search:
                {
                    int rings = Mathf.Clamp(m.SearchRings, 1, 6);
                    double[][] corners = { new[] { 1.0, 1.0 }, new[] { 1.0, -1.0 }, new[] { -1.0, -1.0 }, new[] { -1.0, 1.0 } };
                    for (int ring = 1; ring <= rings; ring++)
                    {
                        double scale = radius * (0.4 + 0.6 * ring / rings) * 2.0 / rings * ring;
                        double rr = radius * ((double)ring / rings);
                        for (int c = 0; c < corners.Length; c++)
                        {
                            m.Waypoints.Add(Obs(Off(corners[c][0] * rr, corners[c][1] * rr, ca),
                                "Search Ring " + ring + "-" + (c + 1)));
                        }
                    }
                    break;
                }
            }

            // Every profile ends on the objective itself.
            m.Waypoints.Add(Waypoint.Create(objective, WaypointKind.Target, "Objective"));
            return m;
        }

        public static string Pretty(MissionType t)
        {
            switch (t)
            {
                case MissionType.PointToPoint: return "Point To Point";
                case MissionType.Reconnaissance: return "Reconnaissance";
                case MissionType.Surveillance: return "Surveillance";
                case MissionType.AreaSurvey: return "Area Survey";
                case MissionType.Search: return "Search";
                default: return t.ToString();
            }
        }

        public static string Blurb(MissionType t)
        {
            switch (t)
            {
                case MissionType.PointToPoint: return "Direct route: launch to single cruise leg to objective. Simplest profile.";
                case MissionType.Reconnaissance: return "Approach the objective and hold at offset observation points around it before landing on target.";
                case MissionType.Surveillance: return "Multiple observation passes over the objective corridor.";
                case MissionType.AreaSurvey: return "Lawnmower coverage pattern across the area around the objective.";
                case MissionType.Search: return "Expanding-box search pattern centred on the objective.";
                default: return string.Empty;
            }
        }
    }
}
