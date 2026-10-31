using System;
using System.Collections.Generic;
using System.Linq;

namespace DS4Windows;

internal static class ProfileActionNames
{
    // New profiles use individual XML elements. Recover legacy slash-separated
    // names only when the installed action definitions identify one unique list.
    internal static List<string> Read(string legacy, IEnumerable<string> explicitNames,
        IEnumerable<string> knownNames)
    {
        if (explicitNames != null)
            return explicitNames.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).ToList();
        if (string.IsNullOrEmpty(legacy)) return new();
        var known = knownNames.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).ToArray();
        var memo = new Dictionary<int, List<List<string>>>();
        List<List<string>> Parse(int offset)
        {
            if (offset == legacy.Length) return new() { new() };
            if (memo.TryGetValue(offset, out var found)) return found;
            var results = new List<List<string>>();
            foreach (string name in known)
            {
                int end = offset + name.Length;
                if (end > legacy.Length || !legacy.AsSpan(offset, name.Length).SequenceEqual(name.AsSpan()) ||
                    (end != legacy.Length && legacy[end] != '/')) continue;
                foreach (var tail in Parse(end == legacy.Length ? end : end + 1))
                {
                    results.Add(new[] { name }.Concat(tail).ToList());
                    if (results.Count == 2) return memo[offset] = results;
                }
            }
            return memo[offset] = results;
        }
        // Bound compatibility recovery on externally supplied profiles.
        if (legacy.Length <= 4096 && known.Length <= 512 && legacy.Count(c => c == '/') <= 128)
        {
            var candidates = Parse(0);
            if (candidates.Count == 1) return candidates[0].Distinct(StringComparer.Ordinal).ToList();
        }
        return legacy.Split('/').Where(n => n.Length != 0).Distinct(StringComparer.Ordinal).ToList();
    }
}
