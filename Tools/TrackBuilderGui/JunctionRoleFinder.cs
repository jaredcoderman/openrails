using System;
using System.Collections.Generic;
using System.Linq;

namespace TrackBuilderGui;

/// <summary>
/// Mirrors TrackBuilder's 3-way cluster + stem/main/spur role assignment so the
/// GUI can preview which leg is the facing approach, main, and spur.
/// </summary>
public static class JunctionRoleFinder
{
    public const double SnapMeters = FreeEndpointFinder.SnapMeters;

    public static List<JunctionInfo> Find(NetworkLocalFile network)
    {
        var tips = new List<Tip>();
        foreach (var feature in network.Features)
        {
            if (TryBuildTip(feature, isStart: true, out Tip start))
                tips.Add(start);
            if (TryBuildTip(feature, isStart: false, out Tip end))
                tips.Add(end);
        }

        var used = new HashSet<int>();
        var junctions = new List<JunctionInfo>();
        double snap2 = SnapMeters * SnapMeters;

        for (int i = 0; i < tips.Count; i++)
        {
            if (used.Contains(i))
                continue;

            var cluster = new List<int> { i };
            for (int j = 0; j < tips.Count; j++)
            {
                if (i == j || used.Contains(j))
                    continue;
                double dx = tips[i].X - tips[j].X;
                double dz = tips[i].Z - tips[j].Z;
                if (dx * dx + dz * dz <= snap2)
                    cluster.Add(j);
            }

            if (cluster.Count != 3)
                continue;

            // Distinct features only (same as TrackBuilder 3-way clusters).
            if (cluster.Select(idx => tips[idx].ObjectId).Distinct().Count() != 3)
                continue;

            foreach (int idx in cluster)
                used.Add(idx);

            var members = cluster.Select(idx => tips[idx]).ToList();
            if (!TryAssignRoles(members, out Tip stem, out Tip main, out Tip spur))
                continue;

            double jx = (stem.X + main.X + spur.X) / 3.0;
            double jz = (stem.Z + main.Z + spur.Z) / 3.0;
            junctions.Add(new JunctionInfo
            {
                X = jx,
                Z = jz,
                Legs =
                {
                    ToLeg(stem, JunctionLegRole.Stem),
                    ToLeg(main, JunctionLegRole.Main),
                    ToLeg(spur, JunctionLegRole.Spur),
                },
            });
        }

        return junctions;
    }

    private static JunctionLeg ToLeg(Tip tip, JunctionLegRole role)
        => new()
        {
            ObjectId = tip.ObjectId,
            IsStart = tip.IsStart,
            Role = role,
            TipX = tip.X,
            TipZ = tip.Z,
            Preview = BuildFullLeg(tip),
        };

    /// <summary>Entire feature polyline, ordered starting at the junction tip.</summary>
    private static List<(double X, double Z)> BuildFullLeg(Tip tip)
    {
        var pts = tip.Feature.PointsLocal;
        var result = new List<(double X, double Z)>();
        if (pts == null || pts.Count < 2)
        {
            result.Add((tip.X, tip.Z));
            return result;
        }

        IEnumerable<List<float>> ordered = tip.IsStart ? pts : pts.AsEnumerable().Reverse();
        bool first = true;
        foreach (var pt in ordered)
        {
            if (pt == null || pt.Count < 2)
                continue;
            double x = pt[0];
            double z = pt[1];
            if (first)
            {
                first = false;
                result.Add((tip.X, tip.Z));
                if (Dist2(x, z, tip.X, tip.Z) < 1)
                    continue;
            }
            result.Add((x, z));
        }

        if (result.Count == 0)
            result.Add((tip.X, tip.Z));
        return result;
    }

    private static bool TryAssignRoles(List<Tip> cluster, out Tip stem, out Tip main, out Tip spur)
    {
        stem = main = spur = default!;
        if (cluster.Count != 3)
            return false;

        double OutX(Tip ep) => Math.Sin(ep.OutAy);
        double OutZ(Tip ep) => Math.Cos(ep.OutAy);

        int bestI = 0, bestJ = 1;
        double bestDot = double.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            for (int j = i + 1; j < 3; j++)
            {
                double dot = OutX(cluster[i]) * OutX(cluster[j]) + OutZ(cluster[i]) * OutZ(cluster[j]);
                if (dot < bestDot)
                {
                    bestDot = dot;
                    bestI = i;
                    bestJ = j;
                }
            }
        }

        Tip a = cluster[bestI];
        Tip b = cluster[bestJ];
        Tip divEp = cluster[0];
        for (int i = 0; i < 3; i++)
        {
            if (i != bestI && i != bestJ)
            {
                divEp = cluster[i];
                break;
            }
        }

        double ScoreStem(Tip candidateStem, Tip candidateMain)
        {
            double inX = -OutX(candidateStem);
            double inZ = -OutZ(candidateStem);
            double mainDot = OutX(candidateMain) * inX + OutZ(candidateMain) * inZ;
            double divDot = OutX(divEp) * inX + OutZ(divEp) * inZ;
            return mainDot + divDot;
        }

        if (ScoreStem(a, b) >= ScoreStem(b, a))
        {
            stem = a;
            main = b;
        }
        else
        {
            stem = b;
            main = a;
        }

        spur = divEp;
        return true;
    }

    private static bool TryBuildTip(NetworkFeature feature, bool isStart, out Tip tip)
    {
        tip = default!;
        if (!TryPoint(feature, isStart, out double x, out double z))
            return false;

        double ay = HeadingAy(feature, isStart);
        // Outward from junction: start tip leaves along ay; end tip outward reverses arrival ay.
        double outAy = isStart ? ay : ay + Math.PI;
        tip = new Tip
        {
            Feature = feature,
            ObjectId = feature.ObjectId,
            IsStart = isStart,
            X = x,
            Z = z,
            OutAy = outAy,
        };
        return true;
    }

    private static bool TryPoint(NetworkFeature feature, bool isStart, out double x, out double z)
    {
        x = z = 0;
        if (isStart)
        {
            if (feature.Start != null)
            {
                x = feature.Start.X;
                z = feature.Start.Z;
                return true;
            }
            if (feature.PointsLocal is { Count: > 0 } pts && pts[0].Count >= 2)
            {
                x = pts[0][0];
                z = pts[0][1];
                return true;
            }
        }
        else
        {
            if (feature.End != null)
            {
                x = feature.End.X;
                z = feature.End.Z;
                return true;
            }
            if (feature.PointsLocal is { Count: > 0 } pts && pts[^1].Count >= 2)
            {
                x = pts[^1][0];
                z = pts[^1][1];
                return true;
            }
        }
        return false;
    }

    private static double HeadingAy(NetworkFeature feature, bool isStart)
    {
        if (isStart && feature.Start?.Ay != null)
            return feature.Start.Ay.Value;
        if (!isStart && feature.End?.Ay != null)
            return feature.End.Ay.Value;

        var pts = feature.PointsLocal;
        if (pts == null || pts.Count < 2)
            return 0;

        if (isStart)
        {
            var a = pts[0];
            var b = pts[Math.Min(1, pts.Count - 1)];
            if (a.Count < 2 || b.Count < 2)
                return 0;
            return Math.Atan2(b[0] - a[0], b[1] - a[1]);
        }
        else
        {
            var a = pts[^2];
            var b = pts[^1];
            if (a.Count < 2 || b.Count < 2)
                return 0;
            // Arrival heading into the end tip.
            return Math.Atan2(b[0] - a[0], b[1] - a[1]);
        }
    }

    private static double Dist2(double x0, double z0, double x1, double z1)
    {
        double dx = x0 - x1;
        double dz = z0 - z1;
        return dx * dx + dz * dz;
    }

    private sealed class Tip
    {
        public NetworkFeature Feature = null!;
        public int ObjectId;
        public bool IsStart;
        public double X;
        public double Z;
        public double OutAy;
    }
}
