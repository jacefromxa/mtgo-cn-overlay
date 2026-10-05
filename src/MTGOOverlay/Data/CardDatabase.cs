using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using MTGOOverlay.Models;

namespace MTGOOverlay.Data;

public class CardDatabase
{
    private readonly Dictionary<string, CardEntry> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _normToKey = new(StringComparer.OrdinalIgnoreCase);
    private FuzzyMatcher? _fuzzyMatcher;
    private bool _isLoaded;

    public bool IsLoaded => _isLoaded;
    public int Count => _cards.Count;

    public async Task LoadDatabaseAsync(string? explicitPath = null)
    {
        string path = explicitPath ?? FindDatabasePath();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Cannot find card database file at {path}");
        }

        await using var fileStream = File.OpenRead(path);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var jsonDoc = await JsonDocument.ParseAsync(gzipStream);

        var root = jsonDoc.RootElement;
        if (!root.TryGetProperty("cards", out var cardsElement))
        {
            throw new InvalidDataException("Invalid database format: missing 'cards' root property");
        }

        var normalizedList = new List<string>();

        foreach (var prop in cardsElement.EnumerateObject())
        {
            string originalKey = prop.Name;
            var entry = JsonSerializer.Deserialize<CardEntry>(prop.Value.GetRawText());
            if (entry != null)
            {
                entry.NameEn = originalKey;
                _cards[originalKey] = entry;

                string norm = FuzzyMatcher.Normalize(originalKey);
                if (!string.IsNullOrEmpty(norm) && !_normToKey.ContainsKey(norm))
                {
                    _normToKey[norm] = originalKey;
                    normalizedList.Add(norm);
                }
            }
        }

        _fuzzyMatcher = new FuzzyMatcher(normalizedList);
        _isLoaded = true;
    }

    public CardEntry? Lookup(string query)
    {
        if (!_isLoaded || string.IsNullOrWhiteSpace(query)) return null;

        string norm = FuzzyMatcher.Normalize(query);
        if (string.IsNullOrEmpty(norm)) return null;

        // 1. Direct normalized lookup (O(1))
        if (_normToKey.TryGetValue(norm, out var directKey) && _cards.TryGetValue(directKey, out var directEntry))
        {
            return directEntry;
        }

        // 2. Fuzzy match
        if (_fuzzyMatcher != null)
        {
            var (bestNorm, dist) = _fuzzyMatcher.FindBestMatch(norm, maxDistance: 3);
            if (bestNorm != null && _normToKey.TryGetValue(bestNorm, out var fuzzyKey))
            {
                if (_cards.TryGetValue(fuzzyKey, out var fuzzyEntry))
                {
                    return fuzzyEntry;
                }
            }
        }

        return null;
    }

    private static string FindDatabasePath()
    {
        string[] candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "en2zhs.json.gz"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "data", "en2zhs.json.gz"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "en2zhs.json.gz"),
            Path.GetFullPath("data/en2zhs.json.gz")
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }

        return "data/en2zhs.json.gz";
    }
}
