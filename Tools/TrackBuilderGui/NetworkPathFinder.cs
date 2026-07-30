using System;
using System.Collections.Generic;
using System.Linq;

namespace TrackBuilderGui;

public sealed class NetworkPathResult
{
    public static NetworkPathResult None { get; } = new(Array.Empty<int>(), found: false);

    public NetworkPathResult(IReadOnlyList<int> featureIds, bool found)
    {
        FeatureIds = featureIds;
        Found = found;
    }

    public IReadOnlyList<int> FeatureIds { get; }
    public bool Found { get; }
}

/// <summary>
/// Shortest tip-to-tip route on the fitted network (geometric length), with
/// junction hops that would require an unrealistically sharp turn rejected.
/// </summary>
public static class NetworkPathFinder
{
    /// <summary>
    /// Max heading change at a junction hop, degrees. Real turnouts are a few
    /// degrees; ~75° still allows gentle diverges but blocks V / reverse turns.
    /// </summary>
    public const double MaxJunctionTurnDegrees = 75;

    public static List<int> FindFeatureIds(
        NetworkLocalFile network,
        NetworkEndpoint start,
        NetworkEndpoint goal)
        => Find(network, start, goal).FeatureIds.ToList();

    public static NetworkPathResult Find(
        NetworkLocalFile network,
        NetworkEndpoint start,
        NetworkEndpoint goal)
    {
        if (SameTip(start, goal))
            return NetworkPathResult.None;

        var tips = new List<TipRef>();
        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var featureById = network.Features.ToDictionary(f => f.ObjectId);
        var featureLen = new Dictionary<int, double>();

        foreach (var feature in network.Features)
        {
            if (!TryTip(feature, isStart: true, out var s))
                continue;
            if (!TryTip(feature, isStart: false, out var e))
                continue;
            byKey[Key(s.ObjectId, true)] = tips.Count;
            tips.Add(s);
            byKey[Key(e.ObjectId, false)] = tips.Count;
            tips.Add(e);
            featureLen[feature.ObjectId] = PolylineLength(feature);
        }

        string startKey = Key(start.ObjectId, start.IsStart);
        string goalKey = Key(goal.ObjectId, goal.IsStart);
        if (!byKey.TryGetValue(startKey, out int startIdx)
            || !byKey.TryGetValue(goalKey, out int goalIdx))
            return NetworkPathResult.None;

        // Edge: (toTip, featureId or -1 for hop, length meters)
        var adj = new List<(int To, int FeatureId, double Len)>[tips.Count];
        for (int i = 0; i < tips.Count; i++)
            adj[i] = new List<(int, int, double)>();

        foreach (var feature in network.Features)
        {
            if (!byKey.TryGetValue(Key(feature.ObjectId, true), out int a))
                continue;
            if (!byKey.TryGetValue(Key(feature.ObjectId, false), out int b))
                continue;
            double len = featureLen.TryGetValue(feature.ObjectId, out double L) ? Math.Max(L, 0.01) : 1;
            adj[a].Add((b, feature.ObjectId, len));
            adj[b].Add((a, feature.ObjectId, len));
        }

        double snap2 = FreeEndpointFinder.SnapMeters * FreeEndpointFinder.SnapMeters;
        for (int i = 0; i < tips.Count; i++)
        {
            for (int j = i + 1; j < tips.Count; j++)
            {
                if (tips[i].ObjectId == tips[j].ObjectId)
                    continue;
                double dx = tips[i].X - tips[j].X;
                double dz = tips[i].Z - tips[j].Z;
                double d2 = dx * dx + dz * dz;
                if (d2 > snap2)
                    continue;

                if (!featureById.TryGetValue(tips[i].ObjectId, out var fa)
                    || !featureById.TryGetValue(tips[j].ObjectId, out var fb))
                    continue;

                // Skip lateral parallel gaps and hairpin double-track welds.
                if (!ParallelTrackTips.ShouldSnapTips(
                        fa, tips[i].IsStart, fb, tips[j].IsStart,
                        FreeEndpointFinder.SnapMeters))
                    continue;

                double hop = Math.Max(0.01, Math.Sqrt(d2));
                adj[i].Add((j, -1, hop));
                adj[j].Add((i, -1, hop));
            }
        }

        // Dijkstra: cameFrom[tip] = (prevTip, featureId used to reach tip)
        var dist = new double[tips.Count];
        for (int i = 0; i < tips.Count; i++)
            dist[i] = double.PositiveInfinity;
        dist[startIdx] = 0;

        var cameFrom = new Dictionary<int, (int Prev, int FeatureId)>();
        cameFrom[startIdx] = (-1, -1);

        var heap = new SortedSet<(double Dist, int Tip)>(Comparer<(double Dist, int Tip)>.Create(
            (a, b) =>
            {
                int c = a.Dist.CompareTo(b.Dist);
                return c != 0 ? c : a.Tip.CompareTo(b.Tip);
            }));
        heap.Add((0, startIdx));

        while (heap.Count > 0)
        {
            var (dCur, cur) = heap.Min;
            heap.Remove(heap.Min);
            if (dCur > dist[cur] + 1e-9)
                continue;
            if (cur == goalIdx)
                break;

            foreach (var (to, featureId, len) in adj[cur])
            {
                // Junction hop: require train-plausible turn using actual arrival.
                if (featureId < 0)
                {
                    if (!IsTurnAllowed(cur, to, tips, cameFrom, featureById))
                        continue;
                }

                double nd = dist[cur] + len;
                if (nd + 1e-9 >= dist[to])
                    continue;

                if (!double.IsPositiveInfinity(dist[to]))
                    heap.Remove((dist[to], to));
                dist[to] = nd;
                cameFrom[to] = (cur, featureId);
                heap.Add((nd, to));
            }
        }

        if (!cameFrom.ContainsKey(goalIdx) || goalIdx == startIdx)
            return NetworkPathResult.None;

        var featureIds = new List<int>();
        for (int id = goalIdx; id != startIdx;)
        {
            var step = cameFrom[id];
            if (step.FeatureId >= 0)
                featureIds.Add(step.FeatureId);
            id = step.Prev;
            if (id < 0)
                break;
        }

        featureIds.Reverse();
        var deduped = featureIds
            .Where((id, i) => i == 0 || id != featureIds[i - 1])
            .ToList();
        return new NetworkPathResult(deduped, found: true);
    }

    /// <summary>
    /// Hop from tip <paramref name="fromIdx"/> to <paramref name="toIdx"/>.
    /// Arrival heading uses the inbound feature that actually reached fromIdx.
    /// </summary>
    private static bool IsTurnAllowed(
        int fromIdx,
        int toIdx,
        List<TipRef> tips,
        Dictionary<int, (int Prev, int FeatureId)> cameFrom,
        Dictionary<int, NetworkFeature> featureById)
    {
        var from = tips[fromIdx];
        var to = tips[toIdx];

        if (!featureById.TryGetValue(to.ObjectId, out var toFeat))
            return false;
        if (!TryLeaveHeading(toFeat, to.IsStart, out double departX, out double departZ))
            return false;

        if (!TryArrivalHeading(fromIdx, tips, cameFrom, featureById, out double arriveX, out double arriveZ))
            return false;

        double dot = arriveX * departX + arriveZ * departZ;
        dot = Math.Clamp(dot, -1.0, 1.0);
        double turnDeg = Math.Acos(dot) * (180.0 / Math.PI);
        return turnDeg <= MaxJunctionTurnDegrees;
    }

    private static bool TryArrivalHeading(
        int tipIdx,
        List<TipRef> tips,
        Dictionary<int, (int Prev, int FeatureId)> cameFrom,
        Dictionary<int, NetworkFeature> featureById,
        out double hx,
        out double hz)
    {
        hx = hz = 0;
        var tip = tips[tipIdx];

        if (!cameFrom.TryGetValue(tipIdx, out var step) || step.Prev < 0)
        {
            // Path start: facing into the network from this free tip.
            if (!featureById.TryGetValue(tip.ObjectId, out var feat))
                return false;
            return TryLeaveHeading(feat, tip.IsStart, out hx, out hz);
        }

        if (step.FeatureId >= 0)
        {
            // Arrived by traveling along this feature to tipIdx.
            if (!featureById.TryGetValue(step.FeatureId, out var feat))
                return false;
            // Leave heading from this tip points back into the feature;
            // arrival is the opposite.
            if (!TryLeaveHeading(feat, tip.IsStart, out double leaveX, out double leaveZ))
                return false;
            hx = -leaveX;
            hz = -leaveZ;
            return true;
        }

        // Arrived via a previous hop: use that hop's departure into this tip's feature.
        if (!featureById.TryGetValue(tip.ObjectId, out var hopFeat))
            return false;
        return TryLeaveHeading(hopFeat, tip.IsStart, out hx, out hz);
    }

    private static bool TryLeaveHeading(
        NetworkFeature feature, bool isStart, out double hx, out double hz)
    {
        hx = hz = 0;
        var pts = feature.PointsLocal;
        if (pts == null || pts.Count < 2)
            return false;

        // Prefer geometry near the tip (skip Start.Ay — can disagree with polyline).
        double x0, z0, x1, z1;
        if (isStart)
        {
            if (pts[0].Count < 2 || pts[1].Count < 2)
                return false;
            x0 = pts[0][0];
            z0 = pts[0][1];
            x1 = pts[1][0];
            z1 = pts[1][1];
        }
        else
        {
            if (pts[^1].Count < 2 || pts[^2].Count < 2)
                return false;
            x0 = pts[^1][0];
            z0 = pts[^1][1];
            x1 = pts[^2][0];
            z1 = pts[^2][1];
        }

        double dx = x1 - x0;
        double dz = z1 - z0;
        double len = Math.Sqrt(dx * dx + dz * dz);
        if (len < 1e-6)
            return false;
        hx = dx / len;
        hz = dz / len;
        return true;
    }

    private static double PolylineLength(NetworkFeature feature)
    {
        var pts = feature.PointsLocal;
        if (pts == null || pts.Count < 2)
            return 0;
        double sum = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            if (pts[i - 1].Count < 2 || pts[i].Count < 2)
                continue;
            double dx = pts[i][0] - pts[i - 1][0];
            double dz = pts[i][1] - pts[i - 1][1];
            sum += Math.Sqrt(dx * dx + dz * dz);
        }
        return sum;
    }

    private static bool SameTip(NetworkEndpoint a, NetworkEndpoint b)
        => a.ObjectId == b.ObjectId && a.IsStart == b.IsStart;

    private static string Key(int objectId, bool isStart)
        => objectId + (isStart ? "S" : "E");

    private static bool TryTip(NetworkFeature feature, bool isStart, out TipRef tip)
    {
        tip = default!;
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

        tip = new TipRef
        {
            ObjectId = feature.ObjectId,
            IsStart = isStart,
            X = x,
            Z = z,
        };
        return true;
    }

    private sealed class TipRef
    {
        public int ObjectId;
        public bool IsStart;
        public double X;
        public double Z;
    }
}
