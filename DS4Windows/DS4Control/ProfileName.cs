using System;
using System.IO;

namespace DS4Windows;

internal static class ProfileName
{
    // Stored names are basenames, even if they themselves end in .xml.
    // Prefer the exact existing file before interpreting a supplied suffix.
    internal static string Normalize(string value, bool existingIdentity = false)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Enter a valid profile name.", nameof(value));
        bool exactStoredName = existingIdentity && File.Exists(Path.Combine(Global.appdatapath, "Profiles", value + Global.XML_EXTENSION));
        if (exactStoredName) return value;
        if (value.EndsWith(Global.XML_EXTENSION, StringComparison.OrdinalIgnoreCase))
            value = value[..^Global.XML_EXTENSION.Length];
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.EndsWith(' ') || value.EndsWith('.'))
            throw new ArgumentException("Enter a valid profile name without a trailing space or dot.", nameof(value));
        return value;
    }
}
