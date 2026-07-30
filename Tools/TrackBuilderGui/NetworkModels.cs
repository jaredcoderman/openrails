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
