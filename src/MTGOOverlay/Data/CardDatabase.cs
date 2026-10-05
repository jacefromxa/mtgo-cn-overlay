using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    private static readonly HashSet<string> IgnoredSingleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "instant", "sorcery", "creature", "land", "artifact", "enchantment", "planeswalker", "battle",
        "flying", "trample", "haste", "deathtouch", "lifelink", "vigilance", "reach", "ward", "defender",
        "flash", "double strike", "first strike", "hexproof", "indestructible", "menace", "prowess"
    };

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
            var entry = prop.Value.Deserialize<CardEntry>();
            if (entry != null)
            {
                entry.NameEn = ToTitleCase(originalKey);
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

    /// <summary>
    /// Looks up a card given raw OCR output from a full card crop.
    /// Analyzes lines top-to-bottom, strips mana costs from title lines, and filters noise words.
    /// </summary>
    public CardEntry? Lookup(string query)
    {
        if (!_isLoaded || string.IsNullOrWhiteSpace(query)) return null;

        var lines = query.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > 0)
        {
            // 1. Pass 1: Check each line individually (with and without mana suffix cleaning)
            foreach (var line in lines)
            {
                if (line.Length < 3 || IgnoredSingleWords.Contains(line)) continue;

                // Try with mana suffix stripped (e.g. "Lightning Bolt {R}" -> "Lightning Bolt")
                string cleanedLine = CleanManaSuffix(line);
                var match = LookupSingleLine(cleanedLine);
                if (match != null) return match;

                // If cleaned didn't match, try raw line
                if (cleanedLine != line)
                {
                    match = LookupSingleLine(line);
                    if (match != null) return match;
                }
            }

            // 2. Pass 2: Check adjacent line pairs (in case long title wrapped)
            for (int i = 0; i < lines.Length - 1; i++)
            {
                string combined = CleanManaSuffix(lines[i] + " " + lines[i + 1]);
                if (combined.Length < 4) continue;
                var match = LookupSingleLine(combined);
                if (match != null) return match;
            }
        }

        // 3. Fall back to entire query string
        return LookupSingleLine(CleanManaSuffix(query));
    }

    private static string CleanManaSuffix(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return string.Empty;

        // Remove curly braces mana: {1}{U}{U}
        string s = Regex.Replace(line, @"\{[^{}]+\}", "").Trim();

        // Strip trailing typical mana cost pattern at the end of title line (e.g. ' 1G', ' 3WU', ' 5UU', ' R', ' 4BB')
        s = Regex.Replace(s, @"\s+[0-9XYZxyz]*[WUBRGwubrg]{1,4}$", "").Trim();

        return s;
    }

    private CardEntry? LookupSingleLine(string text)
    {
        string norm = FuzzyMatcher.Normalize(text);
        if (string.IsNullOrEmpty(norm) || norm.Length < 2) return null;

        // 1. Direct normalized lookup (O(1))
        if (_normToKey.TryGetValue(norm, out var directKey) && _cards.TryGetValue(directKey, out var directEntry))
        {
            return directEntry;
        }

        // 2. Clean common OCR number-as-letter substitutions (O(1))
        string cleaned = FuzzyMatcher.CleanOcrNoise(norm);
        if (cleaned != norm && _normToKey.TryGetValue(cleaned, out var cleanedKey) && _cards.TryGetValue(cleanedKey, out var cleanedEntry))
        {
            return cleanedEntry;
        }

        // 3. Length-bounded fuzzy matching (Levenshtein)
        if (_fuzzyMatcher != null && norm.Length >= 4)
        {
            var (bestNorm, dist) = _fuzzyMatcher.FindBestMatch(norm, maxDistance: 3);
            if (bestNorm != null && _normToKey.TryGetValue(bestNorm, out var fuzzyKey))
            {
                if (_cards.TryGetValue(fuzzyKey, out var fuzzyEntry))
                {
                    return fuzzyEntry;
                }
            }

            if (cleaned != norm)
            {
                var (bestCleanedNorm, distCleaned) = _fuzzyMatcher.FindBestMatch(cleaned, maxDistance: 2);
                if (bestCleanedNorm != null && _normToKey.TryGetValue(bestCleanedNorm, out var fuzzyCleanedKey))
                {
                    if (_cards.TryGetValue(fuzzyCleanedKey, out var fuzzyCleanedEntry))
                    {
                        return fuzzyCleanedEntry;
                    }
                }
            }
        }

        return null;
    }

    private static readonly HashSet<string> LowercaseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "as", "at", "but", "by", "for", "from", "in", "into", "nor", "of", "on", "onto", "or", "so", "the", "to", "up", "with", "yet"
    };

    public static string ToTitleCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        var words = input.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            if (string.IsNullOrEmpty(w)) continue;

            if (i > 0 && LowercaseWords.Contains(w))
            {
                words[i] = w.ToLowerInvariant();
            }
            else
            {
                words[i] = char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1).ToLowerInvariant() : "");
            }
        }
        return string.Join(" ", words);
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
