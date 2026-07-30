using System;

namespace TrackBuilderGui;

/// <summary>
/// Detects when two nearby tips sit on parallel tracks (double track) rather
/// than at a real joint. NTAD parallel centerlines are often ~12–20 m apart;
/// a blunt 25 m snap would otherwise merge them into one rail. Coincident tips
/// with a ~180° hairpin similarly weld double-track ends into one loop.
/// </summary>
public static class ParallelTrackTips
{
    /// <summary>Tips this close are always a candidate shared vertex / joint.</summary>
    public const double HardJoinMeters = 3.0;

    /// <summary>|dot| of unit track headings above this ⇒ parallel or anti-parallel.</summary>
    public const double MinHeadingAlign = 0.92;

    /// <summary>
    /// Max |along-track| component of the tip-to-tip unit vector. Below this the
    /// offset is mostly lateral (side-by-side rails).
    /// </summary>
    public const double MaxAlongTrack = 0.45;

    public static bool IsLateralParallelGap(
        double ax, double az, double aAy,
        double bx, double bz, double bAy,
        double dist)
    {
        if (dist <= HardJoinMeters || dist < 1e-6)
            return false;

        double axh = Math.Sin(aAy);
        double azh = Math.Cos(aAy);
        double bxh = Math.Sin(bAy);
        double bzh = Math.Cos(bAy);

        double align = Math.Abs(axh * bxh + azh * bzh);
        if (align < MinHeadingAlign)
            return false;

        double sx = (bx - ax) / dist;
        double sz = (bz - az) / dist;
        double along = Math.Abs(sx * axh + sz * azh);
        return along < MaxAlongTrack;
    }

    /// <summary>
    /// True when connecting these tips would reverse ~180° (hairpin). That welds
    /// double-track ends into one loop instead of keeping parallel rails separate.
    /// </summary>
    public static bool IsHairpinConnection(
        double aArriveX, double aArriveZ,
        double bLeaveX, double bLeaveZ)
        => aArriveX * bLeaveX + aArriveZ * bLeaveZ < -0.5;

    /// <summary>
    /// Open Rails-style yaw of the polyline at a tip (travel into the feature
    /// from the tip: first chord at start, last chord reversed at end).
    /// </summary>
    public static bool TryTipHeading(NetworkFeature feature, bool isStart, out double ay)
    {
        ay = 0;
        var pts = feature.PointsLocal;
        if (pts == null || pts.Count < 2)
            return false;

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
            int n = pts.Count;
            if (pts[n - 2].Count < 2 || pts[n - 1].Count < 2)
                return false;
            x0 = pts[n - 1][0];
            z0 = pts[n - 1][1];
            x1 = pts[n - 2][0];
            z1 = pts[n - 2][1];
        }

        ay = Math.Atan2(x1 - x0, z1 - z0);
        return true;
    }

    public static bool TryTipArrivalHeading(
        NetworkFeature feature, bool isStart, out double hx, out double hz)
    {
        hx = hz = 0;
        if (!TryTipHeading(feature, isStart, out double leaveAy))
            return false;
        hx = -Math.Sin(leaveAy);
        hz = -Math.Cos(leaveAy);
        return true;
    }

    public static bool ShouldSnapTips(
        NetworkFeature a, bool aIsStart,
        NetworkFeature b, bool bIsStart,
        double snapMeters)
    {
        if (!TryPoint(a, aIsStart, out double ax, out double az)
            || !TryPoint(b, bIsStart, out double bx, out double bz))
            return false;

        double dist = Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
        if (dist > snapMeters)
            return false;

        if (!TryTipHeading(a, aIsStart, out double aLeaveAy)
            || !TryTipHeading(b, bIsStart, out double bLeaveAy))
            return dist <= HardJoinMeters;

        if (IsLateralParallelGap(ax, az, aLeaveAy, bx, bz, bLeaveAy, dist))
            return false;

        if (TryTipArrivalHeading(a, aIsStart, out double arrX, out double arrZ)
            && IsHairpinConnection(arrX, arrZ, Math.Sin(bLeaveAy), Math.Cos(bLeaveAy)))
            return false;

        if (TryTipArrivalHeading(b, bIsStart, out double bArrX, out double bArrZ)
            && IsHairpinConnection(bArrX, bArrZ, Math.Sin(aLeaveAy), Math.Cos(aLeaveAy)))
            return false;

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
        else if (feature.End != null)
        {
            x = feature.End.X;
            z = feature.End.Z;
            return true;
        }
        else if (feature.PointsLocal is { Count: > 0 } pts && pts[^1].Count >= 2)
        {
            x = pts[^1][0];
            z = pts[^1][1];
            return true;
        }

        return false;
    }
}
