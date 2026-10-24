using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DS4WinWPF.Translations;

namespace DS4Windows;

// Source phrases remain readable in code; their stable resource identities are
// shared with XAML extraction. Only UI text goes through this lookup: wire IDs,
// profile values, paths and structured diagnostic records remain invariant.
internal static class UiText
{
    private static readonly ConcurrentDictionary<string, string> Keys = new(StringComparer.Ordinal);
    internal static string Key(string source) => Keys.GetOrAdd(source, value =>
        "UI_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16]);
    internal static string Get(string source) => string.IsNullOrEmpty(source) ? source :
        Strings.ResourceManager.GetString(Key(source), CultureInfo.CurrentUICulture) ?? source;
    internal static string Format(string source, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, Get(source), values);

    // Translate only the descriptions, retaining the shell's filter syntax.
    internal static string FileFilter(string source)
    {
        var parts = source.Split('|');
        for (int index = 0; index < parts.Length; index += 2) parts[index] = Get(parts[index]);
        return string.Join('|', parts);
    }
}
