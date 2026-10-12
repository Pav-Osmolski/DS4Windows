using System;
using System.IO;
using System.Security.Cryptography;

namespace DS4Windows;

// Own the exact XML and destination from the failed write, not a controller
// slot whose active profile/settings may have changed before the user retries.
internal sealed class PendingProfileWrite
{
    internal string Profile { get; }
    internal string Xml { get; }
    internal int Device { get; }
    private readonly string path;
    private readonly byte[] originalHash;
    private readonly bool originallyExists;
    private readonly bool capturedIdentity;

    internal PendingProfileWrite(int device, string profile, string xml)
    {
        Device = device; Profile = profile; Xml = xml;
        path = Path.Combine(Global.appdatapath, "Profiles", profile + Global.XML_EXTENSION);
        try
        {
            originallyExists = File.Exists(path);
            originalHash = originallyExists ? SHA256.HashData(File.ReadAllBytes(path)) : null;
            capturedIdentity = true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
    }

    internal bool TrySave()
    {
        try
        {
            if (Xml == null)
                throw new IOException("The profile could not be serialized. Reopen the profile to correct its settings.");
            // Never replay stale XML over a file edited/deleted since failure.
            if (!capturedIdentity || File.Exists(path) != originallyExists ||
                originallyExists && !SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(originalHash))
                throw new IOException("The profile changed since the failed save. Reopen it before making further changes.");
            ProfilePersistence.Save(path, Xml, overwrite: originallyExists);
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            AppLogger.LogToGui($"Profile '{Profile}' remains unsaved: {ex.Message}", false);
            return false;
        }
    }
}
