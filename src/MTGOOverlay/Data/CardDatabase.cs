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
        "flash", "double strike", "first strike", "hexproof", "indestructible", "menace", "prowess",
        "damage", "combat", "triggered", "activated", "ability", "target", "spell", "stack", "player",
        "opponent", "step", "phase", "turn", "untap", "upkeep", "draw", "main", "end", "cleanup",
        "graveyard", "battlefield", "exile", "library", "hand", "done", "cancel", "ok", "yes", "no", "pass",
        "cat", "bat", "dog", "bird", "wolf", "elf", "beast", "bear", "frog", "fish", "rat", "ape",
        "boar", "crab", "drake", "fox", "goat", "horse", "insect", "knight", "human", "soldier", "zombie",
        "goblin", "spirit", "vampire", "cleric", "wizard", "rogue", "warrior", "shaman", "druid"
    };

    public static bool IsIgnoredGameText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        string s = text.Trim();
        if (s.Length < 3) return true;
        if (IgnoredSingleWords.Contains(s)) return true;

        // P/T line (e.g. "1/1", "2/3", "*/*")
        if (Regex.IsMatch(s, @"^\*?[0-9]+\s*/\s*\*?[0-9]+$")) return true;

        // Type lines
        if (s.StartsWith("Land —", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("Land -", StringComparison.OrdinalIgnoreCase) ||
            s.IndexOf("Creature —", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("Creature -", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("Instant —", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("Instant -", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("Sorcery —", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("Sorcery -", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("Enchantment —", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("Enchantment -", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("Artifact —", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("Artifact -", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        // Rules text / MTGO game system keywords
        if (s.IndexOf("enters the battlefield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("when this creature", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("when this permanent", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("each opponent", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("sacrifice a", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("gain 1 life", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("gains 1 life", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("loses 1 life", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("lose 1 life", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("until end of turn", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("Triggered Ability", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("Activated Ability", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("Combat Damage", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("End of Combat", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("Declare Attackers", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("Declare Blockers", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

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

                // 1. Primary normalized key (diacritics stripped, e.g. "Æther Vial" -> "aethervial")
                string norm = MtgFontNormalizer.Normalize(originalKey);
                if (!string.IsNullOrEmpty(norm))
                {
                    if (!_normToKey.ContainsKey(norm))
                    {
                        _normToKey[norm] = originalKey;
                        normalizedList.Add(norm);
                    }

                    // 2. Pre-index Goudy Medieval font aliases (vv, f/s, rn, cl)
                    foreach (var alias in MtgFontNormalizer.GenerateFontAliases(norm))
                    {
                        if (!_normToKey.ContainsKey(alias))
                        {
                            _normToKey[alias] = originalKey;
                        }
                    }
                }

                // 3. Split cards (e.g. "Fire // Ice" -> also index "Fire" and "Ice")
                if (originalKey.Contains("//"))
                {
                    var parts = originalKey.Split(new[] { "//", "/" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var part in parts)
                    {
                        string partNorm = MtgFontNormalizer.Normalize(part);
                        if (!string.IsNullOrEmpty(partNorm) && !_normToKey.ContainsKey(partNorm))
                        {
                            _normToKey[partNorm] = originalKey;
                            normalizedList.Add(partNorm);
                        }
                    }
                }

                // 4. Double-faced cards / adventure cards / flip card faces
                if (entry.Faces != null)
                {
                    foreach (var face in entry.Faces)
                    {
                        if (!string.IsNullOrWhiteSpace(face.NameEn))
                        {
                            string faceNorm = MtgFontNormalizer.Normalize(face.NameEn);
                            if (!string.IsNullOrEmpty(faceNorm) && !_normToKey.ContainsKey(faceNorm))
                            {
                                _normToKey[faceNorm] = originalKey;
                                normalizedList.Add(faceNorm);
                            }
                        }
                    }
                }

                // 5. Flavor name (Universes Beyond / Godzilla / Dracula promos)
                if (!string.IsNullOrWhiteSpace(entry.FlavorName))
                {
                    string fnNorm = MtgFontNormalizer.Normalize(entry.FlavorName);
                    if (!string.IsNullOrEmpty(fnNorm) && !_normToKey.ContainsKey(fnNorm))
                    {
                        _normToKey[fnNorm] = originalKey;
                    }
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
                if (line.Length < 3 || IsIgnoredGameText(line)) continue;

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
                if (IsIgnoredGameText(lines[i])) continue;
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
        string norm = MtgFontNormalizer.Normalize(text);
        if (string.IsNullOrEmpty(norm) || norm.Length < 2) return null;

        // 1. Direct normalized lookup (O(1))
        if (_normToKey.TryGetValue(norm, out var directKey) && _cards.TryGetValue(directKey, out var directEntry))
        {
            return directEntry;
        }

        // 2. MTG Font noise cleaning (vv->w, rn->m, cl->d, 0->o, 1->i, 5->s, 8->b) (O(1))
        string cleaned = MtgFontNormalizer.CleanFontNoise(norm);
        if (cleaned != norm && _normToKey.TryGetValue(cleaned, out var cleanedKey) && _cards.TryGetValue(cleanedKey, out var cleanedEntry))
        {
            return cleanedEntry;
        }

        // 3. Medial-s test for Goudy Medieval retro fonts: if norm contains 'f', try substituting with 's' (O(1))
        if (norm.Contains('f'))
        {
            string sSub = norm.Replace('f', 's');
            if (_normToKey.TryGetValue(sSub, out var sKey) && _cards.TryGetValue(sKey, out var sEntry))
            {
                return sEntry;
            }
            string cleanedSSub = MtgFontNormalizer.CleanFontNoise(sSub);
            if (cleanedSSub != sSub && _normToKey.TryGetValue(cleanedSSub, out var csKey) && _cards.TryGetValue(csKey, out var csEntry))
            {
                return csEntry;
            }
        }

        // 4. Length-bounded fuzzy matching (Levenshtein)
        if (_fuzzyMatcher != null && norm.Length >= 5)
        {
            int maxAllowed = norm.Length <= 7 ? 1 : (norm.Length <= 11 ? 2 : 3);
            var (bestNorm, dist) = _fuzzyMatcher.FindBestMatch(norm, maxDistance: maxAllowed);
            if (bestNorm != null && dist <= maxAllowed && dist <= Math.Max(1, (int)(norm.Length * 0.28f)))
            {
                if (_normToKey.TryGetValue(bestNorm, out var fuzzyKey))
                {
                    if (_cards.TryGetValue(fuzzyKey, out var fuzzyEntry))
                    {
                        return fuzzyEntry;
                    }
                }
            }

            if (cleaned != norm)
            {
                int maxCleaned = cleaned.Length <= 7 ? 1 : 2;
                var (bestCleanedNorm, distCleaned) = _fuzzyMatcher.FindBestMatch(cleaned, maxDistance: maxCleaned);
                if (bestCleanedNorm != null && distCleaned <= maxCleaned && distCleaned <= Math.Max(1, (int)(cleaned.Length * 0.28f)))
                {
                    if (_normToKey.TryGetValue(bestCleanedNorm, out var fuzzyCleanedKey))
                    {
                        if (_cards.TryGetValue(fuzzyCleanedKey, out var fuzzyCleanedEntry))
                        {
                            return fuzzyCleanedEntry;
                        }
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
