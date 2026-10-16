using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MTGOOverlay.Data;

/// <summary>
/// Specialized text normalizer tailored for Magic: The Gathering typography across all eras:
/// - Retro / Old-school Frames (Alpha-Scourge, 1993-2003): Goudy Medieval font (medial 's'/'f', 'w'/'vv', minims 'm'/'rn', 'd'/'cl')
/// - Modern Frames (8ED-M14, 2003-2014): Matrix Bold font
/// - Current Frames (M15-present): Beleren font
/// - Special character sets: ligatures (Æther), accents (Lim-Dûl, Jötun, Dandân), split cards, and DFCs.
/// </summary>
public static class MtgFontNormalizer
{
    /// <summary>
    /// Removes diacritics and ligatures (e.g. Æther -> Aether, Lim-Dûl -> Lim-Dul, Dandân -> Dandan).
    /// </summary>
    public static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // Replace common MTG ligatures first
        string s = text.Replace("Æ", "Ae", StringComparison.OrdinalIgnoreCase)
                       .Replace("æ", "ae", StringComparison.OrdinalIgnoreCase)
                       .Replace("Œ", "Oe", StringComparison.OrdinalIgnoreCase)
                       .Replace("œ", "oe", StringComparison.OrdinalIgnoreCase);

        var normalizedString = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalizedString.Length);

        foreach (char c in normalizedString)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Converts input to pure alphanumeric lowercase with diacritics stripped.
    /// </summary>
    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        string clean = RemoveDiacritics(input);
        var chars = new char[clean.Length];
        int idx = 0;

        foreach (char c in clean)
        {
            if (char.IsLetterOrDigit(c))
            {
                chars[idx++] = char.ToLowerInvariant(c);
            }
        }

        return new string(chars, 0, idx);
    }

    /// <summary>
    /// Corrects systematic OCR misrecognitions caused by MTG font features:
    /// - Goudy Medieval 'w' (rendered as overlapping 'v's): "vv" -> "w"
    /// - Goudy Medieval 'm' minims: "rn" -> "m", leading "iv"/"in" -> "m"
    /// - Goudy Medieval 'd': "cl" -> "d"
    /// - OCR number-as-letter substitutions: '0'->'o', '1'->'i', '5'->'s', '8'->'b', '$'->'s'
    /// </summary>
    public static string CleanFontNoise(string normalizedOcr)
    {
        if (string.IsNullOrEmpty(normalizedOcr)) return string.Empty;

        string s = normalizedOcr;

        // 1. Replace Goudy Medieval 'vv' -> 'w' (e.g. "svvords" -> "swords", "vvrath" -> "wrath")
        if (s.Contains("vv"))
        {
            s = s.Replace("vv", "w");
        }

        // 2. Replace Goudy Medieval leading "iv" or "in" -> "m" (e.g. "ivox" -> "mox", "ivana" -> "mana")
        if (s.StartsWith("iv", StringComparison.Ordinal) || s.StartsWith("in", StringComparison.Ordinal))
        {
            s = "m" + s[2..];
        }

        // 3. Replace 'cl' -> 'd' if length > 3 (e.g. "clark" -> "dark")
        if (s.Contains("cl"))
        {
            s = s.Replace("cl", "d");
        }

        // 4. Replace OCR character noise: 0->o, 1->i, 5->s, 8->b, $->s
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '0': sb.Append('o'); break;
                case '1': sb.Append('i'); break;
                case '5': sb.Append('s'); break;
                case '8': sb.Append('b'); break;
                case '$': sb.Append('s'); break;
                default: sb.Append(c); break;
            }
        }
        s = sb.ToString();

        // 5. Goudy Medieval 'rn' -> 'm' (e.g. "tirne" -> "time", "derionic" -> "demonic", "arnageddon" -> "armageddon")
        if (s.Length >= 4 && s.Contains("rn"))
        {
            s = s.Replace("rn", "m");
        }

        return s;
    }

    /// <summary>
    /// Generates Goudy Medieval font OCR variants for database pre-indexing.
    /// In Goudy Medieval (used on Alpha through Scourge retro cards):
    /// - 's' frequently misrecognized as 'f' (medial 's' shape)
    /// - 'w' frequently misrecognized as 'vv'
    /// - 'm' frequently misrecognized as 'rn'
    /// </summary>
    public static IEnumerable<string> GenerateFontAliases(string normalizedKey)
    {
        if (string.IsNullOrEmpty(normalizedKey) || normalizedKey.Length < 3) yield break;

        // 1. 'w' -> 'vv' variant (e.g. "swords" -> "svvords", "wrath" -> "vvrath")
        if (normalizedKey.Contains('w'))
        {
            string vv = normalizedKey.Replace("w", "vv");
            yield return vv;

            // Also combined with 's' -> 'f' if applicable
            if (vv.Contains('s'))
            {
                yield return vv.Replace('s', 'f');
            }
        }

        // 2. 's' -> 'f' variant (Goudy Medieval medial-s)
        // e.g. "counterspell" -> "counterfpell", "stasis" -> "ftafif", "fastbond" -> "faftbond"
        if (normalizedKey.Contains('s'))
        {
            yield return normalizedKey.Replace('s', 'f');
        }

        // 3. 'm' -> 'rn' variant
        // e.g. "timewalk" -> "tirnewalk", "demonic" -> "derionic"
        if (normalizedKey.Contains('m'))
        {
            yield return normalizedKey.Replace("m", "rn");
        }

        // 4. 'd' -> 'cl' variant
        // e.g. "darkritual" -> "clarkritual"
        if (normalizedKey.Contains('d'))
        {
            yield return normalizedKey.Replace("d", "cl");
        }
    }
}
