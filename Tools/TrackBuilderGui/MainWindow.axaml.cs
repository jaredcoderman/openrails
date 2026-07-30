using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace TrackBuilderGui;

public partial class MainWindow : Window
{
    private static readonly IBrush BadgeIdleBrush = SolidColorBrush.Parse("#2A3342");
    private static readonly IBrush BadgeRunningBrush = SolidColorBrush.Parse("#1B3A5F");
    private static readonly IBrush BadgeOkBrush = SolidColorBrush.Parse("#1B4332");
    private static readonly IBrush BadgeFailBrush = SolidColorBrush.Parse("#4A1C1C");
    private static readonly IBrush BadgeIdleText = SolidColorBrush.Parse("#9AA3B2");
    private static readonly IBrush BadgeRunningText = SolidColorBrush.Parse("#90CAF9");
    private static readonly IBrush BadgeOkText = SolidColorBrush.Parse("#A5D6A7");
    private static readonly IBrush BadgeFailText = SolidColorBrush.Parse("#EF9A9A");

    private const string DefaultRouteDirectory =
        @"C:\Users\jared\ORRoutes\BNSF Starter Route - Copy\ROUTES\BNSF_Scenic";

    private readonly ObservableCollection<ClipEntry> _clipEntries = new();
    private string? _loadedPath;
    private string? _loadedGeoPath;
    private bool _generating;
    /// <summary>True only after the Select button loads a clip or finishes an extract.</summary>
    private bool _readyToBuild;

    public MainWindow()
    {
        InitializeComponent();
        ClipCheckList.ItemsSource = _clipEntries;
        _clipEntries.CollectionChanged += (_, _) => UpdateClipChrome();
        Map.SelectionChanged += (_, _) => UpdateSelectionLabels();
        Map.BboxSelectionChanged += (_, _) => UpdateBboxSelectionUi();
        // Prefer full GeoJSON for clipping. Do not auto-load a fitted network —
        // Build stays off until Select (extract or open clip).
        TryLoadDefaultGeo();
        RefreshClipList(importLegacy: true);
        SetConsoleState("Idle", BadgeIdleBrush, BadgeIdleText);
        UpdateBboxSelectionUi();
        UpdateSelectEnabled();
    }

    private bool TryLoadDefaultGeo()
    {
        string[] candidates =
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Tools", "curve-fitter", "NTAD_North_American_Rail_Network_Lines_BNSF_2685269841624876744.geojson")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Tools", "curve-fitter", "NTAD_North_American_Rail_Network_Lines_BNSF_2685269841624876744.geojson")),
            @"C:\Users\jared\main\openrails\Tools\curve-fitter\NTAD_North_American_Rail_Network_Lines_BNSF_2685269841624876744.geojson",
        };

        foreach (string path in candidates)
        {
            if (!File.Exists(path))
                continue;
            _ = LoadGeoJsonAsync(path);
            return true;
        }

        return false;
    }

    private async void OnLoadClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open bbox_network_local.json",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Fitted network JSON")
                {
                    Patterns = new[] { "*.json" },
                },
            },
        });

        if (files.Count == 0)
            return;

        string? path = files[0].TryGetLocalPath();
        if (path == null)
        {
            StatusText.Text = "Could not resolve selected file path.";
            return;
        }

        LoadNetwork(path);
        _readyToBuild = false;
        UpdateBuildEnabled();
    }

    private async void OnLoadGeoClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open NTAD / full GeoJSON",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("GeoJSON")
                {
                    Patterns = new[] { "*.geojson", "*.json" },
                },
            },
        });

        if (files.Count == 0)
            return;

        string? path = files[0].TryGetLocalPath();
        if (path == null)
        {
            StatusText.Text = "Could not resolve selected file path.";
            return;
        }

        await LoadGeoJsonAsync(path);
    }

    private async void OnShowGeoClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_loadedGeoPath) || !File.Exists(_loadedGeoPath))
        {
            StatusText.Text = "No GeoJSON loaded yet — use Load full GeoJSON…";
            return;
        }

        await LoadGeoJsonAsync(_loadedGeoPath);
    }

    private void OnFitClicked(object? sender, RoutedEventArgs e)
        => Map.FitToView();

    private void OnClearSelectionClicked(object? sender, RoutedEventArgs e)
        => Map.ClearSelection();

    private async void OnBuildRouteClicked(object? sender, RoutedEventArgs e)
    {
        if (_generating)
            return;

        if (!_readyToBuild)
        {
            StatusText.Text = "Press Select first (extract a box or open a checked clip).";
            SetConsoleState("Failed", BadgeFailBrush, BadgeFailText);
            WriteConsole(StatusText.Text);
            return;
        }

        var start = Map.StartEndpoint;
        var goal = Map.GoalEndpoint;
        if (start == null || goal == null)
        {
            StatusText.Text = "Pick start and goal free ends before Build.";
            SetConsoleState("Failed", BadgeFailBrush, BadgeFailText);
            WriteConsole(StatusText.Text);
            return;
        }

        if (Map.PathSelectionInvalid || !Map.HasValidPathHighlight)
        {
            StatusText.Text = "No valid path between those ends (sharp junction turn).";
            SetConsoleState("Failed", BadgeFailBrush, BadgeFailText);
            WriteConsole(StatusText.Text);
            return;
        }

        if (!TryPrepareTdbDump(out string exe, out string networkPath))
            return;

        string startName = Map.StartName ?? "Start";
        string goalName = Map.GoalName ?? "End";
        string pathId = SanitizePathId($"{startName}_{goalName}");
        string pathName = $"{startName} to {goalName}";

        var args = new StringBuilder();
        args.Append("--network ").Append(Quote(networkPath));
        args.Append(" --route ").Append(Quote(DefaultRouteDirectory));
        args.Append(" --start ").Append(FormatEndRef(start));
        args.Append(" --end ").Append(FormatEndRef(goal));
        args.Append(" --path-id ").Append(Quote(pathId));
        args.Append(" --name ").Append(Quote(pathName));
        args.Append(" --start-label ").Append(Quote(startName));
        args.Append(" --end-label ").Append(Quote(goalName));

        await RunTdbDumpAsync(
            exe,
            args.ToString(),
            busyMessage: $"Building route + path {pathId}…",
            successPrefix: $"Route build complete (tsection, .tdb, world, TILES, {pathId})");
    }

    private bool TryPrepareTdbDump(out string exe, out string networkPath)
    {
        exe = "";
        networkPath = "";

        if (string.IsNullOrWhiteSpace(_loadedPath) || !File.Exists(_loadedPath))
        {
            StatusText.Text = "Load a network JSON before building.";
            SetConsoleState("Failed", BadgeFailBrush, BadgeFailText);
            WriteConsole(StatusText.Text);
            return false;
        }

        string? found = FindTdbDumpExe();
        if (found == null)
        {
            StatusText.Text =
                "Could not find TdbDump.exe. Build Source/TdbDump (Debug) first.";
            SetConsoleState("Failed", BadgeFailBrush, BadgeFailText);
            WriteConsole(StatusText.Text);
            return false;
        }

        exe = found;
        networkPath = Path.GetFullPath(_loadedPath);
        return true;
    }

    private async Task RunTdbDumpAsync(
        string exe,
        string args,
        string busyMessage,
        string successPrefix)
    {
        _generating = true;
        UpdateBuildEnabled();
        StatusText.Text = busyMessage;
        SetConsoleState("Running", BadgeRunningBrush, BadgeRunningText);
        WriteConsole(busyMessage + "\n\n$ TdbDump " + args + "\n");

        try
        {
            var result = await Task.Run(() => RunProcess(exe, args));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result.ExitCode == 0)
                {
                    StatusText.Text = successPrefix;
                    SetConsoleState("Success", BadgeOkBrush, BadgeOkText);
                    WriteConsole(
                        successPrefix
                        + "\n\n"
                        + (string.IsNullOrWhiteSpace(result.Output)
                            ? "(no output)"
                            : result.Output));
                }
                else
                {
                    StatusText.Text = $"TdbDump failed (exit {result.ExitCode})";
                    SetConsoleState("Failed", BadgeFailBrush, BadgeFailText);
                    WriteConsole(
                        StatusText.Text
                        + "\n\n"
                        + (string.IsNullOrWhiteSpace(result.Output)
                            ? "(no output)"
                            : result.Output));
                }
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "TdbDump failed: " + ex.Message;
            SetConsoleState("Failed", BadgeFailBrush, BadgeFailText);
            WriteConsole(StatusText.Text);
        }
        finally
        {
            _generating = false;
            UpdateBuildEnabled();
        }
    }

    private void SetConsoleState(string label, IBrush badgeBrush, IBrush textBrush)
    {
        ConsoleBadge.Background = badgeBrush;
        ConsoleBadgeText.Text = label;
        ConsoleBadgeText.Foreground = textBrush;
    }

    private void WriteConsole(string text)
    {
        ConsoleOutput.Text = text;
        Dispatcher.UIThread.Post(() =>
        {
            ConsoleScroll.Offset = new Avalonia.Vector(
                0,
                Math.Max(0, ConsoleScroll.Extent.Height - ConsoleScroll.Viewport.Height));
        }, DispatcherPriority.Background);
    }

    private void LoadNetwork(string path)
    {
        try
        {
            var network = NetworkLoader.Load(path);
            _loadedPath = path;
            // Keep _loadedGeoPath so Back to GeoJSON can restore the full preview.
            Map.SetNetwork(network);
            StatusText.Text = $"Fitted · {network.Features.Count} features · {Map.FreeEndCount} free ends";
            WriteConsole($"Loaded fitted network · {network.Features.Count} features · {Map.FreeEndCount} free ends\n{path}");
            ToolTip.SetTip(StatusText, path);
            SetConsoleState("Idle", BadgeIdleBrush, BadgeIdleText);
            UpdateSelectionLabels();
            UpdateBuildEnabled();
        }
        catch (Exception ex)
        {
            _loadedPath = null;
            _readyToBuild = false;
            StatusText.Text = "Load failed: " + ex.Message;
            SetConsoleState("Failed", BadgeFailBrush, BadgeFailText);
            WriteConsole(StatusText.Text);
            UpdateBuildEnabled();
        }
    }

    private async Task LoadGeoJsonAsync(string path)
    {
        StatusText.Text = "Loading GeoJSON…";
        SetConsoleState("Running", BadgeRunningBrush, BadgeRunningText);
        WriteConsole("Loading " + path);

        try
        {
            var network = await Task.Run(() => GeoJsonPreviewLoader.Load(path));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _loadedGeoPath = path;
                _loadedPath = null;
                _readyToBuild = false;
                Map.SetGeoPreview(network);
                StatusText.Text =
                    $"Geo · {network.Features.Count:N0} parts · {network.VertexCount:N0} verts · {network.LoadDuration.TotalSeconds:F2}s";
                WriteConsole(
                    $"Geo preview · {network.Features.Count:N0} parts · {network.VertexCount:N0} verts · {network.LoadDuration.TotalSeconds:F2}s\n"
                    + "EPSG:3857 (Web Mercator) — move cursor for lon/lat\n"
                    + path);
                ToolTip.SetTip(StatusText, path);
                SetConsoleState("Success", BadgeOkBrush, BadgeOkText);
                UpdateBuildEnabled();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _loadedGeoPath = null;
                StatusText.Text = "GeoJSON load failed: " + ex.Message;
                SetConsoleState("Failed", BadgeFailBrush, BadgeFailText);
                WriteConsole(StatusText.Text);
                UpdateBuildEnabled();
            });
        }
    }

    private void UpdateSelectionLabels()
    {
        if (Map.StartEndpoint == null)
            StartLabel.Text = "Start: (none)";
        else
            StartLabel.Text = $"Start: {Map.StartName} ({Map.StartEndpoint.Label})";

        if (Map.GoalEndpoint == null)
            GoalLabel.Text = "Goal: (none)";
        else
            GoalLabel.Text = $"Goal: {Map.GoalName} ({Map.GoalEndpoint.Label})";

        if (Map.StartEndpoint != null && Map.GoalEndpoint != null && Map.PathSelectionInvalid)
            StatusText.Text =
                "No valid train path (junction turn too sharp — would need a reverse).";
        else if (Map.HasValidPathHighlight)
            StatusText.Text = "Path highlight OK — Build will write this path with the route.";

        UpdateBuildEnabled();
    }

    private void UpdateBuildEnabled()
    {
        bool hasFitted = !string.IsNullOrWhiteSpace(_loadedPath) && !Map.IsGeoPreview;
        bool hasPath =
            Map.StartEndpoint != null
            && Map.GoalEndpoint != null
            && Map.HasValidPathHighlight
            && !Map.PathSelectionInvalid;
        BuildRouteButton.IsEnabled = !_generating && hasFitted && _readyToBuild && hasPath;
        UpdateShowGeoEnabled();
        UpdateBboxSelectionUi();
        UpdateClipChrome();
        UpdateSelectEnabled();
    }

    private void UpdateShowGeoEnabled()
    {
        bool hasGeo = !string.IsNullOrWhiteSpace(_loadedGeoPath) && File.Exists(_loadedGeoPath);
        bool show = !_generating && hasGeo && !Map.IsGeoPreview;
        ShowGeoButton.IsVisible = show;
        ShowGeoButton.IsEnabled = show;
    }

    private void UpdateBboxSelectionUi()
    {
        if (!Map.IsGeoPreview)
        {
            BboxSelectionLabel.Text = "Selection: (load GeoJSON, then Shift+drag)";
        }
        else if (!Map.HasBboxSelection)
        {
            BboxSelectionLabel.Text = "Selection: (none) — Shift+drag a box";
        }
        else
        {
            var a = Map.SelectionCornerA;
            var b = Map.SelectionCornerB;
            BboxSelectionLabel.Text =
                $"Selection: {Map.SelectedObjectIds.Count} OBJECTID(s)"
                + (a == null || b == null
                    ? ""
                    : $"\nA {a.Value.Lat:F4},{a.Value.Lon:F4}  B {b.Value.Lat:F4},{b.Value.Lon:F4}");
        }

        UpdateSelectEnabled();
    }

    private void UpdateSelectEnabled()
    {
        if (_generating)
        {
            ExtractSelectionButton.IsEnabled = false;
            ToolTip.SetTip(ExtractSelectionButton, "Busy…");
            return;
        }

        bool canExtract = Map.IsGeoPreview
            && Map.HasBboxSelection
            && Map.SelectedObjectIds.Count > 0;
        int checkedClips = _clipEntries.Count(c => c.IsChecked);
        bool canOpenClip = checkedClips == 1;

        if (canExtract)
        {
            ExtractSelectionButton.IsEnabled = true;
            ToolTip.SetTip(
                ExtractSelectionButton,
                "Extract & fit the Shift+drag selection into a clip");
        }
        else if (canOpenClip)
        {
            ExtractSelectionButton.IsEnabled = true;
            ToolTip.SetTip(
                ExtractSelectionButton,
                "Open the checked clip (loads fitted network for Build)");
        }
        else
        {
            ExtractSelectionButton.IsEnabled = false;
            ToolTip.SetTip(
                ExtractSelectionButton,
                "Shift+drag a GeoJSON box, or check one clip, then Select");
        }
    }

    private async void OnSelectClicked(object? sender, RoutedEventArgs e)
    {
        if (_generating)
            return;

        if (Map.IsGeoPreview && Map.HasBboxSelection && Map.SelectedObjectIds.Count > 0)
        {
            await ExtractSelectionAsync();
            return;
        }

        var selected = _clipEntries.Where(c => c.IsChecked).ToList();
        if (selected.Count == 1)
        {
            OpenClip(selected[0]);
            return;
        }

        StatusText.Text = "Shift+drag a GeoJSON box, or check one clip, then Select.";
    }

    private async Task ExtractSelectionAsync()
    {
        if (_generating || !Map.HasBboxSelection)
            return;

        string? geoPath = _loadedGeoPath;
        if (string.IsNullOrWhiteSpace(geoPath) || !File.Exists(geoPath))
        {
            StatusText.Text = "Load a full GeoJSON first.";
            return;
        }

        string? fitterDir = FindCurveFitterDir();
        string? python = FindPython(fitterDir);
        string? script = fitterDir == null
            ? null
            : Path.Combine(fitterDir, "extract_bbox_network.py");
        if (fitterDir == null || python == null || script == null || !File.Exists(script))
        {
            StatusText.Text =
                "Could not find curve-fitter Python / extract_bbox_network.py.";
            SetConsoleState("Failed", BadgeFailBrush, BadgeFailText);
            WriteConsole(StatusText.Text);
            return;
        }

        var ids = Map.SelectedObjectIds.ToList();
        var cornerA = Map.SelectionCornerA;
        var cornerB = Map.SelectionCornerB;
        if (cornerA == null || cornerB == null)
            return;

        string workDir = Path.Combine(fitterDir, "gui_selection");
        Directory.CreateDirectory(workDir);
        string objectIdsPath = Path.Combine(workDir, "bbox_objectids.txt");
        string outputLocal = Path.Combine(workDir, "bbox_network_local.json");
        string outputGeo = Path.Combine(workDir, "bbox_network.geojson");
        File.WriteAllLines(objectIdsPath, ids.Select(id => id.ToString()));

        string tdbDumpLocal = Path.GetFullPath(Path.Combine(
            fitterDir, "..", "..", "Source", "TdbDump", "bbox_network_local.json"));

        string args =
            Quote(script)
            + " --geojson " + Quote(Path.GetFullPath(geoPath))
            + " --objectids " + Quote(objectIdsPath)
            + " --output-local " + Quote(outputLocal)
            + " --output-geojson " + Quote(outputGeo)
            + " --corner-a " + Quote($"{cornerA.Value.Lat},{cornerA.Value.Lon}")
            + " --corner-b " + Quote($"{cornerB.Value.Lat},{cornerB.Value.Lon}");

        _generating = true;
        UpdateBuildEnabled();
        StatusText.Text = $"Extracting & fitting {ids.Count} OBJECTID(s)…";
        SetConsoleState("Running", BadgeRunningBrush, BadgeRunningText);
        WriteConsole(StatusText.Text + "\n\n$ " + python + " " + args + "\n");

        try
        {
            var result = await Task.Run(() => RunProcess(python, args, fitterDir));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result.ExitCode != 0 || !File.Exists(outputLocal))
                {
                    StatusText.Text = $"Extract failed (exit {result.ExitCode})";
                    SetConsoleState("Failed", BadgeFailBrush, BadgeFailText);
                    WriteConsole(StatusText.Text + "\n\n" + result.Output);
                    return;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(tdbDumpLocal)!);
                    File.Copy(outputLocal, tdbDumpLocal, overwrite: true);
                }
                catch (Exception copyEx)
                {
                    WriteConsole("Warning: could not copy to TdbDump: " + copyEx.Message);
                }

                try
                {
                    string clipsRoot = ClipStore.GetClipsRoot(fitterDir)!;
                    var clip = ClipStore.SaveNew(
                        clipsRoot,
                        outputLocal,
                        outputGeo,
                        objectIdsPath,
                        ids.Count,
                        cornerA: $"{cornerA.Value.Lat},{cornerA.Value.Lon}",
                        cornerB: $"{cornerB.Value.Lat},{cornerB.Value.Lon}",
                        sourceGeoJson: Path.GetFullPath(geoPath));
                    WriteConsole($"Saved clip: {clip.Id}\n{clip.FolderPath}");
                    RefreshClipList(importLegacy: false);
                }
                catch (Exception clipEx)
                {
                    WriteConsole("Warning: could not save clip: " + clipEx.Message);
                }

                LoadNetwork(outputLocal);
                _readyToBuild = true;
                StatusText.Text = $"Fitted selection · {ids.Count} OBJECTID(s)";
                ToolTip.SetTip(StatusText, outputLocal);
                SetConsoleState("Success", BadgeOkBrush, BadgeOkText);
                WriteConsole(
                    "Extract & fit complete.\n"
                    + result.Output
                    + "\n\nLoaded fitted network. Build route when ready.");
                UpdateBuildEnabled();
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Extract failed: " + ex.Message;
            SetConsoleState("Failed", BadgeFailBrush, BadgeFailText);
            WriteConsole(StatusText.Text);
        }
        finally
        {
            _generating = false;
            UpdateBuildEnabled();
        }
    }

    private void OnClipsFlyoutOpening(object? sender, EventArgs e)
        => RefreshClipList(importLegacy: false);

    private void OnSelectAllClipsClicked(object? sender, RoutedEventArgs e)
    {
        foreach (var entry in _clipEntries)
            entry.IsChecked = true;
        UpdateClipChrome();
    }

    private void OnSelectNoneClipsClicked(object? sender, RoutedEventArgs e)
    {
        foreach (var entry in _clipEntries)
            entry.IsChecked = false;
        UpdateClipChrome();
    }

    private void OnRefreshClipsClicked(object? sender, RoutedEventArgs e)
        => RefreshClipList(importLegacy: false);

    private void OpenClip(ClipEntry clip)
    {
        if (!File.Exists(clip.LocalNetworkPath))
        {
            StatusText.Text = "Clip network file missing: " + clip.LocalNetworkPath;
            return;
        }

        string? fitterDir = FindCurveFitterDir();
        if (fitterDir != null)
        {
            string tdbDumpLocal = Path.GetFullPath(Path.Combine(
                fitterDir, "..", "..", "Source", "TdbDump", "bbox_network_local.json"));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(tdbDumpLocal)!);
                File.Copy(clip.LocalNetworkPath, tdbDumpLocal, overwrite: true);
            }
            catch (Exception ex)
            {
                WriteConsole("Warning: could not copy clip to TdbDump: " + ex.Message);
            }
        }

        LoadNetwork(clip.LocalNetworkPath);
        _readyToBuild = true;
        StatusText.Text = $"Opened clip {clip.Id}";
        ToolTip.SetTip(StatusText, clip.LocalNetworkPath);
        WriteConsole($"Opened clip {clip.DisplayName}\n{clip.FolderPath}");
        UpdateBuildEnabled();
    }

    private void OnDeleteClipsClicked(object? sender, RoutedEventArgs e)
    {
        string? fitterDir = FindCurveFitterDir();
        string? clipsRoot = ClipStore.GetClipsRoot(fitterDir);
        if (clipsRoot == null)
            return;

        var selected = _clipEntries.Where(c => c.IsChecked).ToList();
        if (selected.Count == 0)
            return;

        int deleted = 0;
        foreach (var clip in selected)
        {
            try
            {
                ClipStore.Delete(clipsRoot, clip.Id);
                deleted++;
            }
            catch (Exception ex)
            {
                WriteConsole($"Failed to delete {clip.Id}: {ex.Message}");
            }
        }

        RefreshClipList(importLegacy: false);
        StatusText.Text = $"Deleted {deleted} clip(s).";
    }

    private void RefreshClipList(bool importLegacy)
    {
        string? fitterDir = FindCurveFitterDir();
        string? clipsRoot = ClipStore.GetClipsRoot(fitterDir);
        if (clipsRoot == null)
        {
            _clipEntries.Clear();
            UpdateClipChrome();
            return;
        }

        string? autoCheckId = null;
        if (importLegacy && fitterDir != null)
        {
            try
            {
                var imported = ClipStore.ImportLegacyGuiSelectionIfNeeded(fitterDir);
                if (imported != null)
                {
                    autoCheckId = imported.Id;
                    WriteConsole(
                        $"Recovered last extract as clip:\n{imported.FolderPath}\n"
                        + "Press Select to load it before Build.");
                }
            }
            catch (Exception ex)
            {
                WriteConsole("Could not import legacy gui_selection: " + ex.Message);
            }
        }

        var previouslyChecked = _clipEntries
            .Where(c => c.IsChecked)
            .Select(c => c.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(autoCheckId))
            previouslyChecked.Add(autoCheckId);

        foreach (var entry in _clipEntries)
            entry.PropertyChanged -= OnClipEntryPropertyChanged;
        _clipEntries.Clear();

        foreach (var clip in ClipStore.List(clipsRoot))
        {
            clip.IsChecked = previouslyChecked.Contains(clip.Id);
            clip.PropertyChanged += OnClipEntryPropertyChanged;
            _clipEntries.Add(clip);
        }

        UpdateClipChrome();
    }

    private void OnClipEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ClipEntry.IsChecked))
            return;

        _readyToBuild = false;
        UpdateClipChrome();
        UpdateBuildEnabled();
    }

    private void UpdateClipChrome()
    {
        int total = _clipEntries.Count;
        int checkedCount = _clipEntries.Count(c => c.IsChecked);
        ClipsDropDownLabel.Text = total == 0
            ? "No clips yet"
            : checkedCount == 0
                ? $"{total} clip(s)…"
                : $"{checkedCount} of {total} selected";
        DeleteClipsButton.IsEnabled = checkedCount > 0 && !_generating;
        UpdateSelectEnabled();
    }

    private static string? FindCurveFitterDir()
    {
        string[] candidates =
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Tools", "curve-fitter")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Tools", "curve-fitter")),
            @"C:\Users\jared\main\openrails\Tools\curve-fitter",
        };
        foreach (string path in candidates)
        {
            if (File.Exists(Path.Combine(path, "extract_bbox_network.py")))
                return path;
        }
        return null;
    }

    private static string? FindPython(string? fitterDir)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(fitterDir))
        {
            candidates.Add(Path.Combine(fitterDir, "Scripts", "python.exe"));
            candidates.Add(Path.Combine(fitterDir, "bin", "python"));
            candidates.Add(Path.Combine(fitterDir, "bin", "python3"));
        }
        candidates.Add("python");
        candidates.Add("python3");

        foreach (string path in candidates)
        {
            if (path is "python" or "python3")
                return path;
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    private static string FormatEndRef(NetworkEndpoint end)
        => end.ObjectId + (end.IsStart ? ":S" : ":E");

    private static string SanitizePathId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "GeneratedTrack";
        var sb = new StringBuilder(value.Length);
        foreach (char c in value.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                sb.Append(c);
            else if (char.IsWhiteSpace(c) || c == '/' || c == '\\')
                sb.Append('_');
        }
        string id = sb.ToString();
        return id.Length == 0 ? "GeneratedTrack" : id;
    }

    private static string Quote(string value)
    {
        if (value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            return value;
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string? FindTdbDumpExe()
    {
        string[] candidates =
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Source", "TdbDump", "bin", "Debug", "TdbDump.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Source", "TdbDump", "bin", "Debug", "TdbDump.exe")),
            @"C:\Users\jared\main\openrails\Source\TdbDump\bin\Debug\TdbDump.exe",
            @"C:\Users\jared\main\openrails\Source\TdbDump\bin\Release\TdbDump.exe",
        };

        foreach (string path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static (int ExitCode, string Output) RunProcess(
        string exe, string args, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
                ?? Path.GetDirectoryName(exe)
                ?? Environment.CurrentDirectory,
        };

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                output.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return (process.ExitCode, output.ToString().Trim());
    }
}
