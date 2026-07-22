namespace TrackBuilderGui;

/// <summary>A feature chain tip in local meters (x, z).</summary>
public sealed class NetworkEndpoint
{
    public int ObjectId { get; init; }
    public bool IsStart { get; init; }
    public double X { get; init; }
    public double Z { get; init; }

    public string Label => $"oid {ObjectId} {(IsStart ? "start" : "end")}";
}
