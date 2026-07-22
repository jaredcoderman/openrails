using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace TrackBuilderGui;

/// <summary>
/// 2D map of network polylines in local (x, z) meters. Screen Y grows down,
/// so world Z is flipped for display.
/// </summary>
public sealed class TrackMapControl : Control
{
    private const double ClickSlopPixels = 5;
    private const double HitPixels = 14;

    private readonly List<IList<Point>> _polylines = new();
    private readonly List<NetworkEndpoint> _freeEnds = new();
    private readonly List<JunctionInfo> _junctions = new();
    private readonly EndpointNamer _namer = new();
    private double _minX, _maxX, _minZ, _maxZ;
    private bool _hasBounds;

    private double _scale = 1;
    private double _offsetX;
    private double _offsetY;
    private bool _viewInitialized;

    private bool _panning;
    private bool _didPan;
    private Point _panStart;
    private double _panOriginOffsetX;
    private double _panOriginOffsetY;

    public NetworkEndpoint? StartEndpoint { get; private set; }
    public NetworkEndpoint? GoalEndpoint { get; private set; }

    public string? StartName => StartEndpoint == null ? null : _namer.GetName(StartEndpoint);
    public string? GoalName => GoalEndpoint == null ? null : _namer.GetName(GoalEndpoint);

    public int JunctionCount => _junctions.Count;

    public event EventHandler? SelectionChanged;

    public void SetNetwork(NetworkLocalFile? network)
    {
        _polylines.Clear();
        _freeEnds.Clear();
        _junctions.Clear();
        _namer.Reset();
        StartEndpoint = null;
        GoalEndpoint = null;
        _hasBounds = false;
        _viewInitialized = false;

        if (network?.Features == null)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }

        foreach (var feature in network.Features)
        {
            if (feature.PointsLocal == null || feature.PointsLocal.Count < 2)
                continue;

            var line = new List<Point>(feature.PointsLocal.Count);
            foreach (var pt in feature.PointsLocal)
            {
                if (pt == null || pt.Count < 2)
                    continue;
                double x = pt[0];
                double z = pt[1];
                line.Add(new Point(x, z));
                ExpandBounds(x, z);
            }

            if (line.Count >= 2)
                _polylines.Add(line);
        }

        _freeEnds.AddRange(FreeEndpointFinder.Find(network));
        _junctions.AddRange(JunctionRoleFinder.Find(network));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void ClearSelection()
    {
        if (StartEndpoint == null && GoalEndpoint == null)
            return;
        StartEndpoint = null;
        GoalEndpoint = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void ExpandBounds(double x, double z)
    {
        if (!_hasBounds)
        {
            _minX = _maxX = x;
            _minZ = _maxZ = z;
            _hasBounds = true;
            return;
        }

        if (x < _minX) _minX = x;
        if (x > _maxX) _maxX = x;
        if (z < _minZ) _minZ = z;
        if (z > _maxZ) _maxZ = z;
    }

    public void FitToView()
    {
        _viewInitialized = false;
        InvalidateVisual();
    }

    public int FreeEndCount => _freeEnds.Count;

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!_hasBounds)
            return;

        Point mouse = e.GetPosition(this);
        double worldX = (mouse.X - _offsetX) / _scale;
        double worldZ = -((mouse.Y - _offsetY) / _scale);

        double zoom = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        _scale = Math.Clamp(_scale * zoom, 0.0005, 50);

        _offsetX = mouse.X - worldX * _scale;
        _offsetY = mouse.Y - (-worldZ) * _scale;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed || props.IsMiddleButtonPressed)
        {
            _panning = true;
            _didPan = false;
            _panStart = e.GetPosition(this);
            _panOriginOffsetX = _offsetX;
            _panOriginOffsetY = _offsetY;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_panning)
            return;

        Point p = e.GetPosition(this);
        double dx = p.X - _panStart.X;
        double dy = p.Y - _panStart.Y;
        if (!_didPan && (dx * dx + dy * dy) > ClickSlopPixels * ClickSlopPixels)
            _didPan = true;

        if (_didPan)
        {
            _offsetX = _panOriginOffsetX + dx;
            _offsetY = _panOriginOffsetY + dy;
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_panning)
            return;

        _panning = false;
        e.Pointer.Capture(null);

        if (!_didPan && e.InitialPressMouseButton == MouseButton.Left)
            TrySelectAt(e.GetPosition(this));

        e.Handled = true;
    }

    private void TrySelectAt(Point screen)
    {
        NetworkEndpoint? hit = HitTestFreeEnd(screen);
        if (hit == null)
            return;

        if (StartEndpoint == null)
        {
            StartEndpoint = hit;
            _ = _namer.GetName(hit);
        }
        else if (SameEnd(StartEndpoint, hit))
        {
            StartEndpoint = null;
            GoalEndpoint = null;
        }
        else if (GoalEndpoint != null && SameEnd(GoalEndpoint, hit))
        {
            GoalEndpoint = null;
        }
        else
        {
            GoalEndpoint = hit;
            _ = _namer.GetName(hit);
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private static bool SameEnd(NetworkEndpoint a, NetworkEndpoint b)
        => a.ObjectId == b.ObjectId && a.IsStart == b.IsStart;

    private NetworkEndpoint? HitTestFreeEnd(Point screen)
    {
        NetworkEndpoint? best = null;
        double bestDist2 = HitPixels * HitPixels;
        foreach (var end in _freeEnds)
        {
            Point s = WorldToScreen(new Point(end.X, end.Z));
            double dx = s.X - screen.X;
            double dy = s.Y - screen.Y;
            double d2 = dx * dx + dy * dy;
            if (d2 <= bestDist2)
            {
                bestDist2 = d2;
                best = end;
            }
        }
        return best;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Black, bounds);

        if (!_hasBounds || _polylines.Count == 0 || bounds.Width < 1 || bounds.Height < 1)
        {
            var tip = new FormattedText(
                "Load a network JSON to preview track",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                14,
                Brushes.Gray);
            context.DrawText(tip, new Point(16, 16));
            return;
        }

        if (!_viewInitialized)
        {
            InitializeView(bounds.Width, bounds.Height);
            _viewInitialized = true;
        }

        var basePen = new Pen(new SolidColorBrush(Color.Parse("#5A6578")), 1.2);
        foreach (var line in _polylines)
        {
            for (int i = 1; i < line.Count; i++)
                context.DrawLine(basePen, WorldToScreen(line[i - 1]), WorldToScreen(line[i]));
        }

        foreach (var junction in _junctions)
            DrawJunction(context, junction);

        foreach (var end in _freeEnds)
            DrawEndpoint(context, end);

        DrawMapLegend(context, bounds);
    }

    private void DrawJunction(DrawingContext context, JunctionInfo junction)
    {
        Point center = WorldToScreen(new Point(junction.X, junction.Z));
        context.DrawEllipse(
            new SolidColorBrush(Color.Parse("#212830")),
            new Pen(Brushes.White, 1.5),
            center,
            6,
            6);

        foreach (var leg in junction.Legs)
        {
            if (leg.Preview.Count < 2)
                continue;
            var pen = new Pen(new SolidColorBrush(RoleColor(leg.Role)), 3.2);
            for (int i = 1; i < leg.Preview.Count; i++)
            {
                var a = leg.Preview[i - 1];
                var b = leg.Preview[i];
                context.DrawLine(
                    pen,
                    WorldToScreen(new Point(a.X, a.Z)),
                    WorldToScreen(new Point(b.X, b.Z)));
            }
        }

        // Labels in a second pass so we can space them in screen pixels from the junction.
        foreach (var leg in junction.Legs)
            DrawLegLabel(context, junction, leg, center);
    }

    private void DrawLegLabel(
        DrawingContext context, JunctionInfo junction, JunctionLeg leg, Point junctionScreen)
    {
        if (leg.Preview.Count < 2)
            return;

        // Keep labels a fixed screen distance from the junction so they don't
        // pile up when zoomed out (world-fraction placement collapses together).
        const double minScreenDist = 72;
        const double preferScreenDist = 110;

        Point labelScreen = WorldToScreen(new Point(
            leg.Preview[^1].X,
            leg.Preview[^1].Z));
        for (int i = 1; i < leg.Preview.Count; i++)
        {
            Point s = WorldToScreen(new Point(leg.Preview[i].X, leg.Preview[i].Z));
            double dx = s.X - junctionScreen.X;
            double dy = s.Y - junctionScreen.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist >= preferScreenDist)
            {
                labelScreen = s;
                break;
            }
            if (dist >= minScreenDist)
                labelScreen = s;
        }

        // Nudge further along the junction→label direction so the three labels fan out.
        {
            double dx = labelScreen.X - junctionScreen.X;
            double dy = labelScreen.Y - junctionScreen.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 1)
            {
                // Degenerate: push by role angle around the junction.
                double angle = leg.Role switch
                {
                    JunctionLegRole.Stem => -Math.PI * 0.75,
                    JunctionLegRole.Main => -Math.PI * 0.25,
                    _ => Math.PI * 0.25,
                };
                labelScreen = new Point(
                    junctionScreen.X + Math.Cos(angle) * preferScreenDist,
                    junctionScreen.Y + Math.Sin(angle) * preferScreenDist);
            }
            else if (dist < preferScreenDist)
            {
                double scale = preferScreenDist / dist;
                labelScreen = new Point(
                    junctionScreen.X + dx * scale,
                    junctionScreen.Y + dy * scale);
            }
        }

        string label = RoleLabel(leg.Role);
        var text = new FormattedText(
            label,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI", FontStyle.Normal, FontWeight.Bold),
            13,
            new SolidColorBrush(RoleColor(leg.Role)));
        var shadow = new FormattedText(
            label,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI", FontStyle.Normal, FontWeight.Bold),
            13,
            Brushes.Black);

        double tx = labelScreen.X - text.Width * 0.5;
        double ty = labelScreen.Y - text.Height * 0.5;
        context.DrawText(shadow, new Point(tx + 1, ty + 1));
        context.DrawText(text, new Point(tx, ty));
    }

    private void DrawMapLegend(DrawingContext context, Rect bounds)
    {
        double x = 12;
        double y = 12;
        DrawLegendRow(context, ref x, ref y, "#4FC3F7", "POINTS — facing approach (take spur from here)");
        DrawLegendRow(context, ref x, ref y, "#F0C040", "MAIN — default through route");
        DrawLegendRow(context, ref x, ref y, "#FF7043", "SPUR — diverging branch");
    }

    private static void DrawLegendRow(
        DrawingContext context, ref double x, ref double y, string color, string text)
    {
        var brush = new SolidColorBrush(Color.Parse(color));
        context.DrawLine(new Pen(brush, 3), new Point(x, y + 7), new Point(x + 18, y + 7));
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11,
            Brushes.White);
        var shadow = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11,
            Brushes.Black);
        context.DrawText(shadow, new Point(x + 24 + 1, y + 1));
        context.DrawText(ft, new Point(x + 24, y));
        y += 16;
    }

    private static Color RoleColor(JunctionLegRole role)
        => role switch
        {
            JunctionLegRole.Stem => Color.Parse("#4FC3F7"),
            JunctionLegRole.Main => Color.Parse("#F0C040"),
            JunctionLegRole.Spur => Color.Parse("#FF7043"),
            _ => Colors.White,
        };

    private static string RoleLabel(JunctionLegRole role)
        => role switch
        {
            JunctionLegRole.Stem => "POINTS",
            JunctionLegRole.Main => "MAIN",
            JunctionLegRole.Spur => "SPUR",
            _ => "?",
        };

    private void DrawEndpoint(DrawingContext context, NetworkEndpoint end)
    {
        Point s = WorldToScreen(new Point(end.X, end.Z));
        bool isStart = StartEndpoint != null && SameEnd(StartEndpoint, end);
        bool isGoal = GoalEndpoint != null && SameEnd(GoalEndpoint, end);

        double r = isStart || isGoal ? 7 : 5;
        IBrush fill;
        if (isStart)
            fill = new SolidColorBrush(Color.Parse("#E53935"));
        else if (isGoal)
            fill = new SolidColorBrush(Color.Parse("#43A047"));
        else
            fill = new SolidColorBrush(Color.Parse("#90CAF9"));

        context.DrawEllipse(fill, new Pen(Brushes.White, 1.2), s, r, r);

        if ((isStart || isGoal) && _namer.TryGetName(end, out string? label) && label != null)
        {
            var text = new FormattedText(
                label,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI", FontStyle.Normal, FontWeight.SemiBold),
                13,
                Brushes.White);
            double tx = s.X - text.Width * 0.5;
            double ty = s.Y - r - text.Height - 4;
            var shadow = new FormattedText(
                label,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI", FontStyle.Normal, FontWeight.SemiBold),
                13,
                Brushes.Black);
            context.DrawText(shadow, new Point(tx + 1, ty + 1));
            context.DrawText(text, new Point(tx, ty));
        }
    }

    private void InitializeView(double width, double height)
    {
        double pad = 40;
        double worldW = Math.Max(_maxX - _minX, 1);
        double worldH = Math.Max(_maxZ - _minZ, 1);
        double sx = (width - 2 * pad) / worldW;
        double sy = (height - 2 * pad) / worldH;
        _scale = Math.Min(sx, sy);
        double midX = (_minX + _maxX) * 0.5;
        double midZ = (_minZ + _maxZ) * 0.5;
        _offsetX = width * 0.5 - midX * _scale;
        _offsetY = height * 0.5 - (-midZ) * _scale;
    }

    private Point WorldToScreen(Point world)
    {
        return new Point(
            world.X * _scale + _offsetX,
            (-world.Y) * _scale + _offsetY);
    }
}
