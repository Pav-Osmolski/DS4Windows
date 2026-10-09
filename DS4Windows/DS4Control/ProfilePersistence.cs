using System;
using System.IO;
using System.Text;

namespace DS4Windows
{
    internal static class ProfilePersistence
    {
        internal static void Save(string path, string xml,
            Action<string, string> writeTemporary = null)
        {
            path = Path.GetFullPath(path);
            string temporary = Path.Combine(Path.GetDirectoryName(path),
                $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                (writeTemporary ?? WriteTemporary)(temporary, xml);
                // Keep the last valid profile until the complete replacement
                // is ready. Both files are on the same volume.
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    try { File.Delete(temporary); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        AppLogger.LogToGui("Could not remove an incomplete profile write: " + ex.Message, false);
                    }
                }
            }
        }

        private static void WriteTemporary(string path, string xml)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true))
            {
                writer.Write(xml);
                writer.Flush();
            }
            stream.Flush(flushToDisk: true);
        }
    }
}
