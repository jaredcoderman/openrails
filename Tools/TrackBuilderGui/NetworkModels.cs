using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrackBuilderGui;

public sealed class NetworkLocalFile
{
    [JsonPropertyName("features")]
    public List<NetworkFeature> Features { get; set; } = new();
}

public sealed class NetworkFeature
{
    [JsonPropertyName("objectid")]
    public int ObjectId { get; set; }

    [JsonPropertyName("points_local")]
    public List<List<float>> PointsLocal { get; set; } = new();

    [JsonPropertyName("start")]
    public NetworkPoint? Start { get; set; }

    [JsonPropertyName("end")]
    public NetworkPoint? End { get; set; }
}

public sealed class NetworkPoint
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }

    [JsonPropertyName("ay")]
    public float? Ay { get; set; }
}

public enum JunctionLegRole
{
    Stem,
    Main,
    Spur,
}

public sealed class JunctionLeg
{
    public int ObjectId { get; init; }
    public bool IsStart { get; init; }
    public JunctionLegRole Role { get; init; }
    public double TipX { get; init; }
    public double TipZ { get; init; }
    /// <summary>Short polyline from the junction tip along the leg (for map coloring).</summary>
    public List<(double X, double Z)> Preview { get; init; } = new();
}

public sealed class JunctionInfo
{
    public double X { get; init; }
    public double Z { get; init; }
    public List<JunctionLeg> Legs { get; init; } = new();
}
