using System;
using System.Collections.Generic;

namespace TrackBuilderGui;

/// <summary>
/// Free ends = tips with no other tip within SnapMeters at a real joint.
/// Nearby tips on parallel tracks or hairpin double-track ends do not count.
/// </summary>
public static class FreeEndpointFinder
{
    public const double SnapMeters = 25.0;

    public static List<NetworkEndpoint> Find(NetworkLocalFile network)
    {
        var all = new List<NetworkEndpoint>();
        var features = new Dictionary<int, NetworkFeature>();
        foreach (var feature in network.Features)
        {
            features[feature.ObjectId] = feature;
            if (TryPoint(feature, isStart: true, out var start))
                all.Add(start);
            if (TryPoint(feature, isStart: false, out var end))
                all.Add(end);
        }

        var free = new List<NetworkEndpoint>();
        for (int i = 0; i < all.Count; i++)
        {
            var a = all[i];
            if (!features.TryGetValue(a.ObjectId, out var fa))
                continue;

            bool linked = false;
            for (int j = 0; j < all.Count; j++)
            {
                if (i == j)
                    continue;
                var b = all[j];
                if (a.ObjectId == b.ObjectId)
                    continue;
                if (!features.TryGetValue(b.ObjectId, out var fb))
                    continue;

                if (ParallelTrackTips.ShouldSnapTips(fa, a.IsStart, fb, b.IsStart, SnapMeters))
                {
                    linked = true;
                    break;
                }
            }

            if (!linked)
                free.Add(a);
        }

        return free;
    }

    private static bool TryPoint(NetworkFeature feature, bool isStart, out NetworkEndpoint endpoint)
    {
        endpoint = null!;
        double x, z;
        if (isStart)
        {
            if (feature.Start != null)
            {
                x = feature.Start.X;
                z = feature.Start.Z;
            }
            else if (feature.PointsLocal is { Count: > 0 } pts && pts[0].Count >= 2)
            {
                x = pts[0][0];
                z = pts[0][1];
            }
            else
                return false;
        }
        else
        {
            if (feature.End != null)
            {
                x = feature.End.X;
                z = feature.End.Z;
            }
            else if (feature.PointsLocal is { Count: > 0 } pts && pts[^1].Count >= 2)
            {
                x = pts[^1][0];
                z = pts[^1][1];
            }
            else
                return false;
        }

        endpoint = new NetworkEndpoint
        {
            ObjectId = feature.ObjectId,
            IsStart = isStart,
            X = x,
            Z = z,
        };
        return true;
    }
}
