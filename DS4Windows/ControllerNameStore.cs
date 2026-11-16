using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;

namespace DS4Windows;

// Friendly labels belong to physical controllers, never to profiles or virtual
// device descriptors. Keep them in the active settings root for backup/migration.
internal sealed class ControllerNameStore
{
    internal const int MaximumLength = 50;
    private static readonly object FileGate = new();
    private readonly string path;
    internal Action BeforePublishForTest;
    internal static ControllerNameStore Current => new(string.IsNullOrWhiteSpace(Global.appdatapath) ? null : Path.Combine(Global.appdatapath, "ControllerNames.json"));

    internal ControllerNameStore(string path) => this.path = path == null ? null : Path.GetFullPath(path);
    internal bool IsAvailable => path != null;

    private sealed class Document
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, string> Names { get; set; } = new(StringComparer.Ordinal);
    }

    internal static bool TryKey(string identity, out string key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(identity) || !PhysicalAddress.TryParse(identity, out var address)) return false;
        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 6 || bytes.All(value => value == 0) || bytes.All(value => value == 255)) return false;
        key = Convert.ToHexString(bytes);
        return true;
    }

    internal static bool TryNormalize(string value, out string normalized)
    {
        normalized = null;
        try { value = (value ?? string.Empty).Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return false; }
        // Only ordinary spaces are accepted: no tabs/newlines or hidden controls.
        if (value.Length > MaximumLength || value.Any(c => !char.IsLetterOrDigit(c) && c != ' ')) return false;
        normalized = value.Trim(' ');
        return true;
    }

    internal string Get(string identity)
    {
        if (!IsAvailable || !TryKey(identity, out string key)) return string.Empty;
        lock (FileGate)
        {
            try { return Read().Names.GetValueOrDefault(key, string.Empty); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                NLog.LogManager.GetLogger(nameof(ControllerNameStore)).Warn(error, "Could not read controller names.");
                return string.Empty;
            }
        }
    }

    internal string Save(string identity, string value)
    {
        if (!IsAvailable) throw new InvalidOperationException("The settings directory has not been selected.");
        if (!TryKey(identity, out string key)) throw new ArgumentException("A stable controller MAC is required.", nameof(identity));
        if (!TryNormalize(value, out string name)) throw new ArgumentException("Invalid controller name.", nameof(value));
        lock (FileGate)
        {
            // Never replace an unreadable/corrupt document with an empty map.
            Document document = Read();
            if (name.Length == 0) document.Names.Remove(key);
            else document.Names[key] = name;
            if (document.Names.Count > 4096) throw new InvalidDataException("Too many controller names.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions { WriteIndented = true });
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(true);
                }
                BeforePublishForTest?.Invoke();
                File.Move(temporary, path, true);
                return name;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private Document Read()
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Controller names file is too large.");
        Document document = JsonSerializer.Deserialize<Document>(File.ReadAllBytes(path));
        if (document?.Version != 1 || document.Names == null || document.Names.Count > 4096)
            throw new InvalidDataException("Unsupported controller names document.");
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in document.Names)
        {
            if (!TryKey(entry.Key, out string key) || !TryNormalize(entry.Value, out string name))
                throw new InvalidDataException("Invalid saved controller name.");
            if (name.Length > 0) normalized[key] = name;
        }
        document.Names = normalized;
        return document;
    }
}
