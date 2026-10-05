using System;
using System.Collections.Generic;

namespace MTGOOverlay.Data;

public class FuzzyMatcher
{
    private readonly List<string> _cardNames;

    public FuzzyMatcher(IEnumerable<string> cardNames)
    {
        _cardNames = new List<string>(cardNames);
    }

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var chars = new char[input.Length];
        int idx = 0;
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                chars[idx++] = char.ToLowerInvariant(c);
            }
        }
        return new string(chars, 0, idx);
    }

    /// <summary>
    /// Finds the best matching card name for the given OCR string.
    /// </summary>
    public (string? BestMatch, int Distance) FindBestMatch(string ocrText, int maxDistance = 3)
    {
        string norm = Normalize(ocrText);
        if (norm.Length < 3) return (null, int.MaxValue);

        string? bestMatch = null;
        int minDistance = maxDistance + 1;

        for (int i = 0; i < _cardNames.Count; i++)
        {
            string candidate = _cardNames[i];
            int lenDiff = Math.Abs(candidate.Length - norm.Length);
            if (lenDiff >= minDistance) continue;

            int dist = LevenshteinDistanceBounded(norm, candidate, minDistance);
            if (dist < minDistance)
            {
                minDistance = dist;
                bestMatch = candidate;
                if (dist == 0) break; // Exact match found
            }
        }

        return minDistance <= maxDistance ? (bestMatch, minDistance) : (null, minDistance);
    }

    /// <summary>
    /// Bounded Levenshtein calculation. Returns maxLimit if distance exceeds maxLimit.
    /// </summary>
    private static int LevenshteinDistanceBounded(string s, string t, int maxLimit)
    {
        int n = s.Length;
        int m = t.Length;

        if (n == 0) return m <= maxLimit ? m : maxLimit;
        if (m == 0) return n <= maxLimit ? n : maxLimit;

        // Use two rows instead of full matrix to minimize allocations
        int[] v0 = new int[m + 1];
        int[] v1 = new int[m + 1];

        for (int j = 0; j <= m; j++) v0[j] = j;

        for (int i = 0; i < n; i++)
        {
            v1[0] = i + 1;
            int minInRow = v1[0];

            for (int j = 0; j < m; j++)
            {
                int cost = (s[i] == t[j]) ? 0 : 1;
                v1[j + 1] = Math.Min(Math.Min(v1[j] + 1, v0[j + 1] + 1), v0[j] + cost);
                if (v1[j + 1] < minInRow) minInRow = v1[j + 1];
            }

            if (minInRow >= maxLimit) return maxLimit;

            // Swap rows
            var temp = v0;
            v0 = v1;
            v1 = temp;
        }

        return v0[m];
    }
}
