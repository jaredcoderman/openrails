using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace TrackBuilderGui;

public sealed class GeoPreviewFeature
{
    public int ObjectId { get; init; }
    /// <summary>Interleaved Web Mercator X,Y (meters).</summary>
    public float[] MercatorXy { get; init; } = Array.Empty<float>();
    public float MinX { get; init; }
    public float MinY { get; init; }
    public float MaxX { get; init; }
    public float MaxY { get; init; }
    public int PointCount => MercatorXy.Length / 2;
}

public sealed class GeoPreviewNetwork
{
    public string SourcePath { get; init; } = "";
    public List<GeoPreviewFeature> Features { get; init; } = new();
    public int VertexCount { get; init; }
    public double MinX { get; init; }
    public double MinY { get; init; }
    public double MaxX { get; init; }
    public double MaxY { get; init; }
    public TimeSpan LoadDuration { get; init; }
}

/// <summary>
/// Loads WGS84 GeoJSON FeatureCollection LineStrings into Web Mercator
/// polylines for fast map preview (no curve fitting).
/// </summary>
public static class GeoJsonPreviewLoader
{
    public static GeoPreviewNetwork Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("GeoJSON not found.", path);

        var sw = Stopwatch.StartNew();
        byte[] bytes = File.ReadAllBytes(path);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        var features = new List<GeoPreviewFeature>(4096);
        int vertexCount = 0;
        bool hasBounds = false;
        double minX = 0, minY = 0, maxX = 0, maxY = 0;

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new InvalidDataException("GeoJSON root must be an object.");

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            string prop = reader.GetString() ?? "";
            if (!string.Equals(prop, "features", StringComparison.OrdinalIgnoreCase))
            {
                reader.Skip();
                continue;
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                throw new InvalidDataException("GeoJSON 'features' must be an array.");

            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                    continue;

                foreach (var built in ReadFeature(ref reader))
                {
                    features.Add(built);
                    vertexCount += built.PointCount;
                    if (!hasBounds)
                    {
                        minX = built.MinX;
                        minY = built.MinY;
                        maxX = built.MaxX;
                        maxY = built.MaxY;
                        hasBounds = true;
                    }
                    else
                    {
                        if (built.MinX < minX) minX = built.MinX;
                        if (built.MinY < minY) minY = built.MinY;
                        if (built.MaxX > maxX) maxX = built.MaxX;
                        if (built.MaxY > maxY) maxY = built.MaxY;
                    }
                }
            }
        }

        sw.Stop();
        return new GeoPreviewNetwork
        {
            SourcePath = path,
            Features = features,
            VertexCount = vertexCount,
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY,
            LoadDuration = sw.Elapsed,
        };
    }

    private static List<GeoPreviewFeature> ReadFeature(ref Utf8JsonReader reader)
    {
        int objectId = 0;
        string? geomType = null;
        JsonElement? coordinates = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            string name = reader.GetString() ?? "";
            if (string.Equals(name, "properties", StringComparison.OrdinalIgnoreCase))
            {
                objectId = ReadObjectId(ref reader);
            }
            else if (string.Equals(name, "geometry", StringComparison.OrdinalIgnoreCase))
            {
                ReadGeometryObject(ref reader, out geomType, out coordinates);
            }
            else
            {
                reader.Skip();
            }
        }

        var result = new List<GeoPreviewFeature>();
        if (coordinates == null || geomType == null)
            return result;

        foreach (var lonlats in ExtractLines(geomType, coordinates.Value))
        {
            if (lonlats.Count >= 2)
                result.Add(BuildFeature(objectId, lonlats));
        }

        return result;
    }

    private static int ReadObjectId(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return 0;
        }

        int objectId = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;
            string name = reader.GetString() ?? "";
            if (!string.Equals(name, "OBJECTID", StringComparison.OrdinalIgnoreCase))
            {
                reader.Skip();
                continue;
            }

            if (!reader.Read())
                break;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int id))
                objectId = id;
            else if (reader.TokenType == JsonTokenType.String
                     && int.TryParse(reader.GetString(), out int parsed))
                objectId = parsed;
        }

        return objectId;
    }

    private static void ReadGeometryObject(
        ref Utf8JsonReader reader,
        out string? geomType,
        out JsonElement? coordinates)
    {
        geomType = null;
        coordinates = null;

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            string name = reader.GetString() ?? "";
            if (string.Equals(name, "type", StringComparison.OrdinalIgnoreCase))
            {
                if (reader.Read() && reader.TokenType == JsonTokenType.String)
                    geomType = reader.GetString();
                else
                    reader.Skip();
            }
            else if (string.Equals(name, "coordinates", StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                coordinates = doc.RootElement.Clone();
            }
            else
            {
                reader.Skip();
            }
        }
    }

    private static List<List<(double Lon, double Lat)>> ExtractLines(
        string geomType,
        JsonElement coordinates)
    {
        var lines = new List<List<(double Lon, double Lat)>>();
        if (string.Equals(geomType, "LineString", StringComparison.OrdinalIgnoreCase))
        {
            var line = ReadPositions(coordinates);
            if (line.Count >= 2)
                lines.Add(line);
        }
        else if (string.Equals(geomType, "MultiLineString", StringComparison.OrdinalIgnoreCase))
        {
            if (coordinates.ValueKind != JsonValueKind.Array)
                return lines;
            foreach (var part in coordinates.EnumerateArray())
            {
                var line = ReadPositions(part);
                if (line.Count >= 2)
                    lines.Add(line);
            }
        }

        return lines;
    }

    private static List<(double Lon, double Lat)> ReadPositions(JsonElement line)
    {
        var points = new List<(double Lon, double Lat)>();
        if (line.ValueKind != JsonValueKind.Array)
            return points;

        foreach (var pt in line.EnumerateArray())
        {
            if (pt.ValueKind != JsonValueKind.Array || pt.GetArrayLength() < 2)
                continue;
            points.Add((pt[0].GetDouble(), pt[1].GetDouble()));
        }

        return points;
    }

    private static GeoPreviewFeature BuildFeature(int objectId, List<(double Lon, double Lat)> lonlats)
    {
        var xy = new float[lonlats.Count * 2];
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        for (int i = 0; i < lonlats.Count; i++)
        {
            WebMercator.LonLatToMeters(lonlats[i].Lon, lonlats[i].Lat, out double mx, out double my);
            float fx = (float)mx;
            float fy = (float)my;
            xy[i * 2] = fx;
            xy[i * 2 + 1] = fy;
            if (fx < minX) minX = fx;
            if (fy < minY) minY = fy;
            if (fx > maxX) maxX = fx;
            if (fy > maxY) maxY = fy;
        }

        return new GeoPreviewFeature
        {
            ObjectId = objectId,
            MercatorXy = xy,
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY,
        };
    }
}
