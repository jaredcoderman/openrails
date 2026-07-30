using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrackBuilderGui;

public sealed class ClipEntry : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isChecked;

    public ClipEntry(string id, string displayName, string folderPath, int featureCount, DateTime createdUtc)
    {
        Id = id;
        DisplayName = displayName;
        FolderPath = folderPath;
        FeatureCount = featureCount;
        CreatedUtc = createdUtc;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string FolderPath { get; }
    public int FeatureCount { get; }
    public DateTime CreatedUtc { get; }

    public string LocalNetworkPath => Path.Combine(FolderPath, "bbox_network_local.json");

    public string ListLabel => DisplayName;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
                return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ClipMeta
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("created_utc")]
    public DateTime CreatedUtc { get; set; }

    [JsonPropertyName("feature_count")]
    public int FeatureCount { get; set; }

    [JsonPropertyName("objectid_count")]
    public int ObjectIdCount { get; set; }

    [JsonPropertyName("corner_a")]
    public string? CornerA { get; set; }

    [JsonPropertyName("corner_b")]
    public string? CornerB { get; set; }

    [JsonPropertyName("source_geojson")]
    public string? SourceGeoJson { get; set; }
}

/// <summary>
/// Persists fitted map extracts ("clips") under Tools/curve-fitter/clips/.
/// </summary>
public static class ClipStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public static string? GetClipsRoot(string? curveFitterDir)
    {
        if (string.IsNullOrWhiteSpace(curveFitterDir))
            return null;
        return Path.Combine(curveFitterDir, "clips");
    }

    public static IReadOnlyList<ClipEntry> List(string clipsRoot)
    {
        var list = new List<ClipEntry>();
        if (!Directory.Exists(clipsRoot))
            return list;

        foreach (string dir in Directory.GetDirectories(clipsRoot))
        {
            string local = Path.Combine(dir, "bbox_network_local.json");
            if (!File.Exists(local))
                continue;

            string id = Path.GetFileName(dir);
            string metaPath = Path.Combine(dir, "clip.json");
            string name = id;
            int features = 0;
            DateTime created = Directory.GetCreationTimeUtc(dir);

            if (File.Exists(metaPath))
            {
                try
                {
                    var meta = JsonSerializer.Deserialize<ClipMeta>(File.ReadAllText(metaPath));
                    if (meta != null)
                    {
                        if (!string.IsNullOrWhiteSpace(meta.Name))
                            name = meta.Name;
                        if (!string.IsNullOrWhiteSpace(meta.Id))
                            id = meta.Id;
                        features = meta.FeatureCount;
                        if (meta.CreatedUtc != default)
                            created = meta.CreatedUtc.ToUniversalTime();
                    }
                }
                catch
                {
                    // Fall through to file heuristics.
                }
            }

            if (features <= 0)
            {
                try
                {
                    var network = NetworkLoader.Load(local);
                    features = network.Features.Count;
                }
                catch
                {
                    features = 0;
                }
            }

            string display =
                $"{name} · {features} feat · {created.ToLocalTime():MMM d h:mm tt}";
            list.Add(new ClipEntry(id, display, dir, features, created));
        }

        return list
            .OrderByDescending(c => c.CreatedUtc)
            .ToList();
    }

    /// <summary>
    /// Copy a finished extract into a new clip folder and write clip.json.
    /// </summary>
    public static ClipEntry SaveNew(
        string clipsRoot,
        string localNetworkPath,
        string? geoJsonPath,
        string? objectIdsPath,
        int objectIdCount,
        string? cornerA,
        string? cornerB,
        string? sourceGeoJson)
    {
        Directory.CreateDirectory(clipsRoot);
        string id = "clip_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string folder = Path.Combine(clipsRoot, id);
        Directory.CreateDirectory(folder);

        string destLocal = Path.Combine(folder, "bbox_network_local.json");
        File.Copy(localNetworkPath, destLocal, overwrite: true);

        if (!string.IsNullOrWhiteSpace(geoJsonPath) && File.Exists(geoJsonPath))
            File.Copy(geoJsonPath, Path.Combine(folder, "bbox_network.geojson"), overwrite: true);
        if (!string.IsNullOrWhiteSpace(objectIdsPath) && File.Exists(objectIdsPath))
            File.Copy(objectIdsPath, Path.Combine(folder, "bbox_objectids.txt"), overwrite: true);

        int features = 0;
        try
        {
            features = NetworkLoader.Load(destLocal).Features.Count;
        }
        catch
        {
            // ignore
        }

        string name = features > 0
            ? $"Clip · {features} features"
            : $"Clip · {objectIdCount} ids";

        var meta = new ClipMeta
        {
            Id = id,
            Name = name,
            CreatedUtc = DateTime.UtcNow,
            FeatureCount = features,
            ObjectIdCount = objectIdCount,
            CornerA = cornerA,
            CornerB = cornerB,
            SourceGeoJson = sourceGeoJson,
        };
        File.WriteAllText(
            Path.Combine(folder, "clip.json"),
            JsonSerializer.Serialize(meta, JsonOptions));

        return new ClipEntry(
            id,
            $"{name} · {meta.CreatedUtc.ToLocalTime():MMM d h:mm tt}",
            folder,
            features,
            meta.CreatedUtc);
    }

    public static void Delete(string clipsRoot, string clipId)
    {
        if (string.IsNullOrWhiteSpace(clipId))
            return;
        // Disallow path traversal.
        if (clipId.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
            return;

        string folder = Path.Combine(clipsRoot, clipId);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    /// <summary>
    /// One-time import of the legacy gui_selection working folder as a clip,
    /// if clips/ is empty and gui_selection has a fitted network.
    /// </summary>
    public static ClipEntry? ImportLegacyGuiSelectionIfNeeded(string curveFitterDir)
    {
        string clipsRoot = Path.Combine(curveFitterDir, "clips");
        if (Directory.Exists(clipsRoot)
            && Directory.GetDirectories(clipsRoot).Any(d =>
                File.Exists(Path.Combine(d, "bbox_network_local.json"))))
            return null;

        string legacyLocal = Path.Combine(curveFitterDir, "gui_selection", "bbox_network_local.json");
        if (!File.Exists(legacyLocal))
            return null;

        string? legacyGeo = Path.Combine(curveFitterDir, "gui_selection", "bbox_network.geojson");
        if (!File.Exists(legacyGeo))
            legacyGeo = null;
        string? legacyIds = Path.Combine(curveFitterDir, "gui_selection", "bbox_objectids.txt");
        int idCount = 0;
        if (File.Exists(legacyIds))
            idCount = File.ReadAllLines(legacyIds).Count(l => !string.IsNullOrWhiteSpace(l));

        var clip = SaveNew(
            clipsRoot,
            legacyLocal,
            legacyGeo,
            File.Exists(legacyIds!) ? legacyIds : null,
            idCount,
            cornerA: null,
            cornerB: null,
            sourceGeoJson: null);

        // Rename display to make legacy obvious.
        try
        {
            string metaPath = Path.Combine(clip.FolderPath, "clip.json");
            var meta = JsonSerializer.Deserialize<ClipMeta>(File.ReadAllText(metaPath));
            if (meta != null)
            {
                meta.Name = $"Last extract (recovered) · {clip.FeatureCount} features";
                File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, JsonOptions));
            }
        }
        catch
        {
            // keep default name
        }

        return clip;
    }
}
