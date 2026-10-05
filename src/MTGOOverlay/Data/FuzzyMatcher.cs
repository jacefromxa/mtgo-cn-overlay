using System;
using System.Collections.Generic;

namespace MTGOOverlay.Data;

public class FuzzyMatcher
{
    private readonly Dictionary<int, List<string>> _lengthBuckets = new();
    private readonly int _minLen;
    private readonly int _maxLen;

    public FuzzyMatcher(IEnumerable<string> cardNames)
    {
        int min = int.MaxValue;
        int max = int.MinValue;

        foreach (var name in cardNames)
        {
            int len = name.Length;
            if (!_lengthBuckets.TryGetValue(len, out var list))
            {
                list = new List<string>();
                _lengthBuckets[len] = list;
            }
            list.Add(name);

            if (len < min) min = len;
            if (len > max) max = len;
        }

        _minLen = min == int.MaxValue ? 0 : min;
        _maxLen = max == int.MinValue ? 0 : max;
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
    /// Cleans common OCR misrecognitions (numbers mistaken for letters).
    /// </summary>
    public static string CleanOcrNoise(string normalized)
    {
        if (string.IsNullOrEmpty(normalized)) return string.Empty;

        var sb = new System.Text.StringBuilder(normalized.Length);
        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            switch (c)
            {
                case '0':
                    sb.Append('o');
                    break;
                case '1':
                    sb.Append('i');
                    break;
                case '5':
                    sb.Append('s');
                    break;
                case '8':
                    sb.Append('b');
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Finds the best matching card name for the given OCR string using length buckets and bounded Levenshtein.
    /// </summary>
    public (string? BestMatch, int Distance) FindBestMatch(string normalizedOcr, int maxDistance = 3)
    {
        if (normalizedOcr.Length < 3) return (null, int.MaxValue);

        int qLen = normalizedOcr.Length;
        string? bestMatch = null;
        int minDistance = maxDistance + 1;

        int fromLen = Math.Max(_minLen, qLen - maxDistance);
        int toLen = Math.Min(_maxLen, qLen + maxDistance);

        for (int len = fromLen; len <= toLen; len++)
        {
            if (!_lengthBuckets.TryGetValue(len, out var candidates)) continue;

            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                int lenDiff = Math.Abs(candidate.Length - qLen);
                if (lenDiff >= minDistance) continue;

                int dist = LevenshteinDistanceBounded(normalizedOcr, candidate, minDistance);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestMatch = candidate;
                    if (dist == 0) return (bestMatch, 0); // Exact match
                    if (dist == 1)
                    {
                        // Distance 1 is already very strong, continue only if wanting perfect 0
                    }
                }
            }
        }

        return minDistance <= maxDistance ? (bestMatch, minDistance) : (null, minDistance);
    }

    private static int LevenshteinDistanceBounded(string s, string t, int maxLimit)
    {
        int n = s.Length;
        int m = t.Length;

        if (n == 0) return m <= maxLimit ? m : maxLimit;
        if (m == 0) return n <= maxLimit ? n : maxLimit;

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

            var temp = v0;
            v0 = v1;
            v1 = temp;
        }

        return v0[m];
    }
}
