using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TrackBuilderGui;

public static class NetworkLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static NetworkLocalFile Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Network JSON not found.", path);

        string json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<NetworkLocalFile>(json, Options)
            ?? throw new InvalidDataException("Network JSON deserialized to null.");

        data.Features ??= new List<NetworkFeature>();
        return data;
    }
}
