using System;
using System.Collections.Generic;

namespace TrackBuilderGui;

/// <summary>Assigns NATO-style callsigns to selected endpoints for the session.</summary>
public sealed class EndpointNamer
{
    private static readonly string[] Pool =
    {
        "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot",
        "Golf", "Hotel", "India", "Juliet", "Kilo", "Lima",
        "Mike", "November", "Oscar", "Papa", "Quebec", "Romeo",
        "Sierra", "Tango", "Uniform", "Victor", "Whiskey", "Xray",
        "Yankee", "Zulu",
    };

    private readonly Dictionary<string, string> _names = new();
    private readonly List<string> _remaining = new();
    private readonly Random _rng = new();

    public EndpointNamer()
    {
        Reset();
    }

    public void Reset()
    {
        _names.Clear();
        _remaining.Clear();
        _remaining.AddRange(Pool);
        // Fisher–Yates shuffle so first picks feel random.
        for (int i = _remaining.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (_remaining[i], _remaining[j]) = (_remaining[j], _remaining[i]);
        }
    }

    public string GetName(NetworkEndpoint end)
    {
        string key = Key(end);
        if (_names.TryGetValue(key, out string? existing))
            return existing;

        string name;
        if (_remaining.Count > 0)
        {
            name = _remaining[^1];
            _remaining.RemoveAt(_remaining.Count - 1);
        }
        else
        {
            name = Pool[_rng.Next(Pool.Length)] + "-" + (_names.Count + 1);
        }

        _names[key] = name;
        return name;
    }

    public bool TryGetName(NetworkEndpoint end, out string name)
        => _names.TryGetValue(Key(end), out name!);

    private static string Key(NetworkEndpoint end)
        => end.ObjectId + (end.IsStart ? "S" : "E");
}
