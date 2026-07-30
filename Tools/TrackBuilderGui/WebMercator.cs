using System;

namespace TrackBuilderGui;

/// <summary>
/// EPSG:3857 Web Mercator — same projection QGIS uses with OSM/XYZ basemaps.
/// Input is WGS84 lon/lat (EPSG:4326), output meters easting/northing.
/// </summary>
public static class WebMercator
{
    public const double EarthRadius = 6378137.0;
    public const double MaxLat = 85.05112878;
    private const double DegToRad = Math.PI / 180.0;
    private const double RadToDeg = 180.0 / Math.PI;

    public static void LonLatToMeters(double lon, double lat, out double x, out double y)
    {
        lat = Math.Clamp(lat, -MaxLat, MaxLat);
        x = lon * DegToRad * EarthRadius;
        double sin = Math.Sin(lat * DegToRad);
        y = 0.5 * EarthRadius * Math.Log((1.0 + sin) / (1.0 - sin));
    }

    public static void MetersToLonLat(double x, double y, out double lon, out double lat)
    {
        lon = (x / EarthRadius) * RadToDeg;
        lat = (2.0 * Math.Atan(Math.Exp(y / EarthRadius)) - Math.PI * 0.5) * RadToDeg;
    }
}
