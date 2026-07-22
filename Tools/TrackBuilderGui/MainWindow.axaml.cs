using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace TrackBuilderGui;

public partial class MainWindow : Window
{
    private const string DefaultRouteDirectory =
        @"C:\Users\jared\ORRoutes\BNSF Starter Route - Copy\ROUTES\BNSF_Scenic";

    private readonly ObservableCollection<PathEntry> _pathEntries = new();
    private string? _loadedPath;
    private bool _generating;

    public MainWindow()
    {
        InitializeComponent();
        PathCheckList.ItemsSource = _pathEntries;
        _pathEntries.CollectionChanged += OnPathEntriesChanged;
        Map.SelectionChanged += (_, _) => UpdateSelectionLabels();
        TryLoadDefaultNetwork();
        RefreshPathList();
    }

    private void TryLoadDefaultNetwork()
    {
        string[] candidates =
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Source", "TdbDump", "bbox_network_local.json")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Source", "TdbDump", "bbox_network_local.json")),
            @"C:\Users\jared\main\openrails\Source\TdbDump\bbox_network_local.json",
        };

        foreach (string path in candidates)
        {
            if (!File.Exists(path))
                continue;
            LoadNetwork(path);
            return;
        }
    }

    private async void OnLoadClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open bbox_network_local.json",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Network JSON")
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
    }

    private void OnFitClicked(object? sender, RoutedEventArgs e)
    {
        Map.FitToView();
    }

    private void OnClearSelectionClicked(object? sender, RoutedEventArgs e)
    {
        Map.ClearSelection();
    }

    private void OnPathsFlyoutOpening(object? sender, EventArgs e)
    {
        RefreshPathList();
    }

    private void OnSelectAllPathsClicked(object? sender, RoutedEventArgs e)
    {
        foreach (var entry in _pathEntries)
            entry.IsChecked = true;
        UpdatePathChrome();
    }

    private void OnSelectNonePathsClicked(object? sender, RoutedEventArgs e)
    {
        foreach (var entry in _pathEntries)
            entry.IsChecked = false;
        UpdatePathChrome();
    }

    private void OnRefreshPathsClicked(object? sender, RoutedEventArgs e)
    {
        RefreshPathList();
    }

    private void OnDeletePathsClicked(object? sender, RoutedEventArgs e)
    {
        var selected = _pathEntries.Where(p => p.IsChecked).Select(p => p.Id).ToList();
        if (selected.Count == 0)
            return;

        int deletedFiles = 0;
        var missing = new StringBuilder();
        foreach (string id in selected)
        {
            deletedFiles += TryDeleteFile(Path.Combine(DefaultRouteDirectory, "PATHS", id + ".pat"), missing);
            deletedFiles += TryDeleteFile(Path.Combine(DefaultRouteDirectory, "SERVICES", id + ".srv"), missing);
            deletedFiles += TryDeleteFile(Path.Combine(DefaultRouteDirectory, "ACTIVITIES", id + ".act"), missing);
        }

        RefreshPathList();
        StatusText.Text =
            $"Deleted {selected.Count} path id(s), {deletedFiles} file(s)."
            + (missing.Length == 0 ? "" : "\n" + Truncate(missing.ToString().Trim(), 400));
    }

    private static int TryDeleteFile(string path, StringBuilder errors)
    {
        try
        {
            if (!File.Exists(path))
                return 0;
            File.Delete(path);
            return 1;
        }
        catch (Exception ex)
        {
            errors.AppendLine(Path.GetFileName(path) + ": " + ex.Message);
            return 0;
        }
    }

    private async void OnGenerateClicked(object? sender, RoutedEventArgs e)
    {
        if (_generating)
            return;

        var start = Map.StartEndpoint;
        var goal = Map.GoalEndpoint;
        if (start == null || goal == null)
        {
            StatusText.Text = "Select a start and goal free end first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_loadedPath) || !File.Exists(_loadedPath))
        {
            StatusText.Text = "Load a network JSON before generating.";
            return;
        }

        string? exe = FindTdbDumpExe();
        if (exe == null)
        {
            StatusText.Text =
                "Could not find TdbDump.exe. Build Source/TdbDump (Debug) first.";
            return;
        }

        string startName = Map.StartName ?? "Start";
        string goalName = Map.GoalName ?? "End";
        string pathId = ScenarioWriterSanitize($"{startName}_{goalName}");
        string pathName = $"{startName} to {goalName}";
        string startRef = FormatEndRef(start);
        string endRef = FormatEndRef(goal);

        string args =
            "--path-only"
            + " --network " + Quote(Path.GetFullPath(_loadedPath))
            + " --route " + Quote(DefaultRouteDirectory)
            + " --start " + startRef
            + " --end " + endRef
            + " --path-id " + Quote(pathId)
            + " --name " + Quote(pathName)
            + " --start-label " + Quote(startName)
            + " --end-label " + Quote(goalName);

        _generating = true;
        UpdateGenerateEnabled();
        StatusText.Text = $"Generating {pathId}…";

        try
        {
            var result = await Task.Run(() => RunProcess(exe, args));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result.ExitCode == 0)
                {
                    StatusText.Text =
                        $"Wrote path {pathId} (.pat / .srv / .act)\n"
                        + Truncate(result.Output, 600);
                    RefreshPathList();
                }
                else
                {
                    StatusText.Text =
                        $"Generate failed (exit {result.ExitCode})\n"
                        + Truncate(result.Output, 800);
                }
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Generate failed: " + ex.Message;
        }
        finally
        {
            _generating = false;
            UpdateGenerateEnabled();
        }
    }

    private void LoadNetwork(string path)
    {
        try
        {
            var network = NetworkLoader.Load(path);
            _loadedPath = path;
            Map.SetNetwork(network);
            UpdateSelectionLabels();
            StatusText.Text =
                $"Loaded {network.Features.Count} features · {Map.FreeEndCount} free ends · {Map.JunctionCount} switch(es)\n{path}";
        }
        catch (Exception ex)
        {
            _loadedPath = null;
            StatusText.Text = "Load failed: " + ex.Message;
        }
    }

    private void RefreshPathList()
    {
        var previouslyChecked = _pathEntries
            .Where(p => p.IsChecked)
            .Select(p => p.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _pathEntries)
            entry.PropertyChanged -= OnPathEntryPropertyChanged;
        _pathEntries.Clear();

        string pathsDir = Path.Combine(DefaultRouteDirectory, "PATHS");
        if (Directory.Exists(pathsDir))
        {
            foreach (string file in Directory.GetFiles(pathsDir, "*.pat")
                         .OrderBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var entry = new PathEntry(id)
                {
                    IsChecked = previouslyChecked.Contains(id),
                };
                entry.PropertyChanged += OnPathEntryPropertyChanged;
                _pathEntries.Add(entry);
            }
        }

        UpdatePathChrome();
    }

    private void OnPathEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => UpdatePathChrome();

    private void OnPathEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PathEntry.IsChecked))
            UpdatePathChrome();
    }

    private void UpdatePathChrome()
    {
        int total = _pathEntries.Count;
        int checkedCount = _pathEntries.Count(p => p.IsChecked);
        PathsDropDownLabel.Text = total == 0
            ? "No paths found"
            : checkedCount == 0
                ? $"{total} path(s)…"
                : $"{checkedCount} of {total} selected";
        DeletePathsButton.IsEnabled = checkedCount > 0 && !_generating;
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

        UpdateGenerateEnabled();
    }

    private void UpdateGenerateEnabled()
    {
        GenerateButton.IsEnabled =
            !_generating
            && Map.StartEndpoint != null
            && Map.GoalEndpoint != null
            && !string.IsNullOrWhiteSpace(_loadedPath);
        UpdatePathChrome();
    }

    private static string FormatEndRef(NetworkEndpoint end)
        => end.ObjectId + (end.IsStart ? ":S" : ":E");

    private static string Quote(string value)
    {
        if (value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            return value;
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    /// <summary>Mirrors ScenarioWriter.SanitizeId so the GUI shows the real id.</summary>
    private static string ScenarioWriterSanitize(string value)
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

    private static (int ExitCode, string Output) RunProcess(string exe, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
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

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text;
        return text.Substring(0, max).TrimEnd() + "…";
    }
}
