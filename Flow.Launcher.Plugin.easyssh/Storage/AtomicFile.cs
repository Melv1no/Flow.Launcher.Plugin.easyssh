using System.Text;

namespace Flow.Launcher.Plugin.EasySsh.Storage;

/// <summary>Writes files so that a crash never leaves them half written.</summary>
internal static class AtomicFile
{
    /// <summary>UTF-8 without byte order mark: OpenSSH reads a BOM as part of the first keyword.</summary>
    public static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes <paramref name="content"/> to a temporary file of the same folder, then puts it in place with
    /// <see cref="File.Replace(string, string, string?, bool)"/>, which keeps the permissions of the existing file.
    /// </summary>
    /// <param name="path">The file to write. Its folder is created if needed.</param>
    /// <param name="content">The new content.</param>
    /// <param name="backupPath">Where to keep the previous content, or null.</param>
    public static void Write(string path, byte[] content, string? backupPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporaryPath = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(content);
            }

            if (!File.Exists(path))
            {
                File.Move(temporaryPath, path);
                return;
            }

            try
            {
                File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
            }
            catch (Exception e) when (e is IOException or PlatformNotSupportedException)
            {
                if (!File.Exists(path))
                {
                    // ReplaceFile can fail after renaming the original to the backup name: put the new file in place.
                    File.Move(temporaryPath, path);
                    return;
                }

                // File.Replace is not supported everywhere (some network shares): fall back to copy + move.
                if (backupPath is not null)
                {
                    File.Copy(path, backupPath, overwrite: true);
                }

                File.Move(temporaryPath, path, overwrite: true);
            }
        }
        finally
        {
            // Never delete the only copy of the new content.
            if (File.Exists(path))
            {
                TryDelete(temporaryPath);
            }
        }
    }

    /// <summary>Writes text as UTF-8 without BOM.</summary>
    public static void WriteText(string path, string text, string? backupPath) => Write(path, Utf8.GetBytes(text), backupPath);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Only a leftover temporary file.
        }
    }
}
