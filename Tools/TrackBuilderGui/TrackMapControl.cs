using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace TrackBuilderGui;

/// <summary>
/// 2D map: fitted local-meter network, or full GeoJSON preview in Web Mercator
/// (EPSG:3857 — same as QGIS with OSM/XYZ basemaps).
/// </summary>
public sealed class TrackMapControl : Control
{
    private const double ClickSlopPixels = 5;
    private const double HitPixels = 14;
    private const double GeoDrawPixelThreshold = 1.25;

    private enum MapContent
    {
        Empty,
        Fitted,
        Geo,
    }

    private MapContent _content = MapContent.Empty;
    private readonly List<(int ObjectId, IList<Point> Line)> _features = new();
    private readonly List<NetworkEndpoint> _freeEnds = new();
    private readonly HashSet<int> _pathFeatureIds = new();
    private bool _pathInvalid;
    private readonly EndpointNamer _namer = new();
    private NetworkLocalFile? _fittedNetwork;
    private GeoPreviewNetwork? _geo;
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

    private Point? _cursorScreen;
    private string _cursorLabel = "";

    private bool _boxing;
    private Point _boxStartScreen;
    private Point _boxEndScreen;
    private Rect? _selectionScreen;
    private List<int> _selectedObjectIds = new();
    private (double Lat, double Lon)? _selCornerA;
    private (double Lat, double Lon)? _selCornerB;

    public NetworkEndpoint? StartEndpoint { get; private set; }
    public NetworkEndpoint? GoalEndpoint { get; private set; }

    public string? StartName => StartEndpoint == null ? null : _namer.GetName(StartEndpoint);
    public string? GoalName => GoalEndpoint == null ? null : _namer.GetName(GoalEndpoint);

    public bool HasValidPathHighlight => _pathFeatureIds.Count > 0;
    public bool PathSelectionInvalid => _pathInvalid;
    public bool IsGeoPreview => _content == MapContent.Geo;
    public GeoPreviewNetwork? GeoPreview => _geo;
    public IReadOnlyList<int> SelectedObjectIds => _selectedObjectIds;
    public bool HasBboxSelection => _selectedObjectIds.Count > 0;
    public (double Lat, double Lon)? SelectionCornerA => _selCornerA;
    public (double Lat, double Lon)? SelectionCornerB => _selCornerB;
    public int FreeEndCount => _freeEnds.Count;

    public event EventHandler? SelectionChanged;
    public event EventHandler? BboxSelectionChanged;

    public void SetNetwork(NetworkLocalFile? network)
    {
        ClearContent();
        _content = MapContent.Fitted;
        _fittedNetwork = network;

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
                _features.Add((feature.ObjectId, line));
        }

        _freeEnds.AddRange(FreeEndpointFinder.Find(network));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void SetGeoPreview(GeoPreviewNetwork? network)
    {
        ClearContent();
        _content = MapContent.Geo;
        _geo = network;

        if (network == null || network.Features.Count == 0)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }

        _minX = network.MinX;
        _maxX = network.MaxX;
        _minZ = network.MinY;
        _maxZ = network.MaxY;
        _hasBounds = true;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void ClearContent()
    {
        _features.Clear();
        _freeEnds.Clear();
        _pathFeatureIds.Clear();
        _pathInvalid = false;
        _namer.Reset();
        StartEndpoint = null;
        GoalEndpoint = null;
        _fittedNetwork = null;
        _geo = null;
        _hasBounds = false;
        _viewInitialized = false;
        _cursorScreen = null;
        _cursorLabel = "";
        ClearBboxSelection();
        _content = MapContent.Empty;
    }

    public void ClearBboxSelection()
    {
        _boxing = false;
        _selectionScreen = null;
        _selectedObjectIds = new List<int>();
        _selCornerA = null;
        _selCornerB = null;
        BboxSelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void ClearSelection()
    {
        if (StartEndpoint == null && GoalEndpoint == null)
            return;
        StartEndpoint = null;
        GoalEndpoint = null;
        _pathFeatureIds.Clear();
        _pathInvalid = false;
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

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!_hasBounds)
            return;

        Point mouse = e.GetPosition(this);
        double worldX = (mouse.X - _offsetX) / _scale;
        double worldZ = -((mouse.Y - _offsetY) / _scale);

        double zoom = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        double minScale = _content == MapContent.Geo ? 1e-7 : 0.0005;
        double maxScale = _content == MapContent.Geo ? 5 : 50;
        _scale = Math.Clamp(_scale * zoom, minScale, maxScale);

        _offsetX = mouse.X - worldX * _scale;
        _offsetY = mouse.Y - (-worldZ) * _scale;
        UpdateCursorLabel(mouse);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed && !props.IsMiddleButtonPressed)
            return;

        // Shift+left drag = bbox select in geo preview.
        if (_content == MapContent.Geo
            && props.IsLeftButtonPressed
            && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _boxing = true;
            _panning = false;
            _boxStartScreen = e.GetPosition(this);
            _boxEndScreen = _boxStartScreen;
            _selectionScreen = null;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        _panning = true;
        _didPan = false;
        _panStart = e.GetPosition(this);
        _panOriginOffsetX = _offsetX;
        _panOriginOffsetY = _offsetY;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point p = e.GetPosition(this);
        UpdateCursorLabel(p);

        if (_boxing)
        {
            _boxEndScreen = p;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (!_panning)
        {
            if (_content == MapContent.Geo)
                InvalidateVisual();
            return;
        }

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

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _cursorScreen = null;
        _cursorLabel = "";
        if (_content == MapContent.Geo)
            InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_boxing)
        {
            _boxing = false;
            e.Pointer.Capture(null);
            _boxEndScreen = e.GetPosition(this);
            CommitBboxSelection();
            e.Handled = true;
            return;
        }

        if (!_panning)
            return;

        _panning = false;
        e.Pointer.Capture(null);

        if (!_didPan && e.InitialPressMouseButton == MouseButton.Left && _content == MapContent.Fitted)
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

        RefreshPathHighlight();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void RefreshPathHighlight()
    {
        _pathFeatureIds.Clear();
        _pathInvalid = false;
        if (_fittedNetwork == null || StartEndpoint == null || GoalEndpoint == null)
            return;

        var result = NetworkPathFinder.Find(_fittedNetwork, StartEndpoint, GoalEndpoint);
        if (!result.Found)
        {
            _pathInvalid = true;
            return;
        }

        foreach (int id in result.FeatureIds)
            _pathFeatureIds.Add(id);
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

    private void CommitBboxSelection()
    {
        if (_geo == null)
            return;

        double left = Math.Min(_boxStartScreen.X, _boxEndScreen.X);
        double right = Math.Max(_boxStartScreen.X, _boxEndScreen.X);
        double top = Math.Min(_boxStartScreen.Y, _boxEndScreen.Y);
        double bottom = Math.Max(_boxStartScreen.Y, _boxEndScreen.Y);
        if (right - left < 4 || bottom - top < 4)
        {
            ClearBboxSelection();
            InvalidateVisual();
            return;
        }

        _selectionScreen = new Rect(left, top, right - left, bottom - top);

        ScreenToMercator(left, top, out double mx0, out double my0);
        ScreenToMercator(right, bottom, out double mx1, out double my1);
        double minX = Math.Min(mx0, mx1);
        double maxX = Math.Max(mx0, mx1);
        double minY = Math.Min(my0, my1);
        double maxY = Math.Max(my0, my1);

        WebMercator.MetersToLonLat(mx0, my0, out double lonA, out double latA);
        WebMercator.MetersToLonLat(mx1, my1, out double lonB, out double latB);
        _selCornerA = (latA, lonA);
        _selCornerB = (latB, lonB);

        var ids = new HashSet<int>();
        foreach (var feature in _geo.Features)
        {
            if (feature.MaxX < minX || feature.MinX > maxX
                || feature.MaxY < minY || feature.MinY > maxY)
                continue;

            float[] xy = feature.MercatorXy;
            bool hit = false;
            for (int i = 0; i < feature.PointCount; i++)
            {
                float x = xy[i * 2];
                float y = xy[i * 2 + 1];
                if (x >= minX && x <= maxX && y >= minY && y <= maxY)
                {
                    hit = true;
                    break;
                }
            }

            // Also keep features whose polyline crosses the box even when no
            // vertex sits inside (short connectors / sparse NTAD sampling).
            if (!hit && feature.PointCount >= 2)
            {
                for (int i = 1; i < feature.PointCount; i++)
                {
                    if (SegmentIntersectsAabb(
                            xy[(i - 1) * 2], xy[(i - 1) * 2 + 1],
                            xy[i * 2], xy[i * 2 + 1],
                            minX, minY, maxX, maxY))
                    {
                        hit = true;
                        break;
                    }
                }
            }

            if (hit)
                ids.Add(feature.ObjectId);
        }

        _selectedObjectIds = ids.OrderBy(id => id).ToList();
        BboxSelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void ScreenToMercator(double sx, double sy, out double mx, out double my)
    {
        mx = (sx - _offsetX) / _scale;
        my = -((sy - _offsetY) / _scale);
    }

    /// <summary>
    /// Liang–Barsky style reject: true if segment AB intersects axis-aligned box.
    /// </summary>
    private static bool SegmentIntersectsAabb(
        double ax, double ay, double bx, double by,
        double minX, double minY, double maxX, double maxY)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double t0 = 0;
        double t1 = 1;

        bool Clip(double p, double q)
        {
            if (Math.Abs(p) < 1e-12)
                return q >= 0;
            double r = q / p;
            if (p < 0)
            {
                if (r > t1)
                    return false;
                if (r > t0)
                    t0 = r;
            }
            else
            {
                if (r < t0)
                    return false;
                if (r < t1)
                    t1 = r;
            }
            return true;
        }

        return Clip(-dx, ax - minX)
            && Clip(dx, maxX - ax)
            && Clip(-dy, ay - minY)
            && Clip(dy, maxY - ay);
    }

    private void UpdateCursorLabel(Point screen)
    {
        _cursorScreen = screen;
        if (!_hasBounds || _content != MapContent.Geo)
        {
            _cursorLabel = "";
            return;
        }

        double mx = (screen.X - _offsetX) / _scale;
        double my = -((screen.Y - _offsetY) / _scale);
        WebMercator.MetersToLonLat(mx, my, out double lon, out double lat);
        _cursorLabel = $"lon {lon:F5}   lat {lat:F5}";
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Black, bounds);

        if (!_hasBounds || bounds.Width < 1 || bounds.Height < 1)
        {
            var tip = new FormattedText(
                "Load a fitted network JSON or full GeoJSON to preview",
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

        if (_content == MapContent.Geo)
            RenderGeo(context, bounds);
        else
            RenderFitted(context, bounds);

        if (!string.IsNullOrEmpty(_cursorLabel) && _cursorScreen.HasValue)
            DrawCursorHud(context, bounds);
    }

    private void RenderGeo(DrawingContext context, Rect bounds)
    {
        if (_geo == null)
            return;

        double x0 = (0 - _offsetX) / _scale;
        double x1 = (bounds.Width - _offsetX) / _scale;
        double yTop = -((0 - _offsetY) / _scale);
        double yBot = -((bounds.Height - _offsetY) / _scale);
        double viewMinX = Math.Min(x0, x1);
        double viewMaxX = Math.Max(x0, x1);
        double viewMinY = Math.Min(yBot, yTop);
        double viewMaxY = Math.Max(yBot, yTop);
        double pad = 64 / Math.Max(_scale, 1e-12);
        viewMinX -= pad;
        viewMaxX += pad;
        viewMinY -= pad;
        viewMaxY += pad;

        var pen = new Pen(new SolidColorBrush(Color.Parse("#7EB6FF")), 1.1);
        double thresh2 = GeoDrawPixelThreshold * GeoDrawPixelThreshold;

        foreach (var feature in _geo.Features)
        {
            if (feature.MaxX < viewMinX || feature.MinX > viewMaxX
                || feature.MaxY < viewMinY || feature.MinY > viewMaxY)
                continue;

            float[] xy = feature.MercatorXy;
            if (xy.Length < 4)
                continue;

            Point lastDrawn = WorldToScreen(new Point(xy[0], xy[1]));
            for (int i = 1; i < feature.PointCount; i++)
            {
                Point cur = WorldToScreen(new Point(xy[i * 2], xy[i * 2 + 1]));
                double dx = cur.X - lastDrawn.X;
                double dy = cur.Y - lastDrawn.Y;
                if (dx * dx + dy * dy < thresh2 && i + 1 < feature.PointCount)
                    continue;
                context.DrawLine(pen, lastDrawn, cur);
                lastDrawn = cur;
            }
        }

        var hud = new FormattedText(
            $"EPSG:3857 · {_geo.Features.Count:N0} parts · {_geo.VertexCount:N0} verts"
            + (_selectedObjectIds.Count > 0
                ? $" · selection {_selectedObjectIds.Count} OBJECTID(s)"
                : " · Shift+drag to select area"),
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11,
            new SolidColorBrush(Color.Parse("#9AA3B2")));
        context.DrawText(hud, new Point(12, 12));

        DrawSelectionOverlay(context);
    }

    private void DrawSelectionOverlay(DrawingContext context)
    {
        Rect? rect = null;
        if (_boxing)
        {
            double left = Math.Min(_boxStartScreen.X, _boxEndScreen.X);
            double top = Math.Min(_boxStartScreen.Y, _boxEndScreen.Y);
            double w = Math.Abs(_boxEndScreen.X - _boxStartScreen.X);
            double h = Math.Abs(_boxEndScreen.Y - _boxStartScreen.Y);
            rect = new Rect(left, top, w, h);
        }
        else if (_selectionScreen.HasValue)
        {
            rect = _selectionScreen;
        }

        if (rect == null || rect.Value.Width < 1 || rect.Value.Height < 1)
            return;

        context.FillRectangle(
            new SolidColorBrush(Color.FromArgb(40, 76, 175, 80)),
            rect.Value);
        context.DrawRectangle(
            new Pen(new SolidColorBrush(Color.Parse("#66BB6A")), 1.5),
            rect.Value);
    }

    private void RenderFitted(DrawingContext context, Rect _)
    {
        var basePen = new Pen(new SolidColorBrush(Color.Parse("#5A6578")), 1.2);
        var pathPen = new Pen(new SolidColorBrush(Color.Parse("#CE93D8")), 3.5);

        foreach (var (objectId, line) in _features)
        {
            if (_pathFeatureIds.Contains(objectId))
                continue;
            for (int i = 1; i < line.Count; i++)
                context.DrawLine(basePen, WorldToScreen(line[i - 1]), WorldToScreen(line[i]));
        }

        foreach (var (objectId, line) in _features)
        {
            if (!_pathFeatureIds.Contains(objectId))
                continue;
            for (int i = 1; i < line.Count; i++)
                context.DrawLine(pathPen, WorldToScreen(line[i - 1]), WorldToScreen(line[i]));
        }

        foreach (var end in _freeEnds)
            DrawEndpoint(context, end);

        if (_pathInvalid && StartEndpoint != null && GoalEndpoint != null)
        {
            var msg = new FormattedText(
                "No valid train path — junction turn too sharp (would need a reverse)",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI", FontStyle.Normal, FontWeight.SemiBold),
                13,
                new SolidColorBrush(Color.Parse("#EF9A9A")));
            context.DrawText(msg, new Point(12, 12));
        }
    }

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

    private void DrawCursorHud(DrawingContext context, Rect bounds)
    {
        var text = new FormattedText(
            _cursorLabel,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas, Cascadia Mono, Courier New"),
            12,
            Brushes.White);
        double x = 12;
        double y = bounds.Height - text.Height - 12;
        context.FillRectangle(
            new SolidColorBrush(Color.FromArgb(180, 12, 18, 28)),
            new Rect(x - 6, y - 4, text.Width + 12, text.Height + 8));
        context.DrawText(text, new Point(x, y));
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
