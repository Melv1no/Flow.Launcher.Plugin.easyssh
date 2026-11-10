using System.Text;
using System.Text.Json;

namespace Flow.Launcher.Plugin.EasySsh.Storage;

/// <summary>What happened when the data file was opened.</summary>
public enum DataLoadStatus
{
    /// <summary>No file yet: starting with empty data.</summary>
    Created,

    /// <summary>The file was read normally.</summary>
    Loaded,

    /// <summary>The file was missing or damaged and the backup (<c>.bak</c>) was used.</summary>
    RestoredFromBackup,

    /// <summary>The file was damaged and there was no usable backup: a copy was kept and the data reset.</summary>
    ResetAfterCorruption,
}

/// <summary>
/// Persists <see cref="PluginData"/> as JSON. Reads are lock-free (the current instance is immutable);
/// writes are serialized, atomic (temporary file then <see cref="File.Replace(string, string, string?)"/>)
/// and keep the previous version as <c>.bak</c>. A damaged file is never overwritten silently.
/// </summary>
public sealed class DataStore
{
    /// <summary>Name of the data file.</summary>
    public const string FileName = "easyssh.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly object _writeLock = new();
    private PluginData _data = new();

    private DataStore(string directory)
    {
        DirectoryPath = directory;
    }

    /// <summary>Raised after each change, on the thread that made it.</summary>
    public event EventHandler? Changed;

    /// <summary>Folder of the data file.</summary>
    public string DirectoryPath { get; }

    /// <summary>Full path of the data file.</summary>
    public string FilePath => Path.Combine(DirectoryPath, FileName);

    private string BackupPath => FilePath + ".bak";

    /// <summary>How the data was obtained when the store was opened or reloaded.</summary>
    public DataLoadStatus LoadStatus { get; private set; }

    /// <summary>Where the damaged file was moved, when <see cref="LoadStatus"/> reports a damaged file.</summary>
    public string? DamagedFileCopy { get; private set; }

    /// <summary>The current data. Never mutated: changes publish a new instance.</summary>
    public PluginData Data => Volatile.Read(ref _data);

    /// <summary>Opens the store in <paramref name="directory"/>. The folder is only created by the first change.</summary>
    /// <exception cref="IOException">The file exists but can't be read. Nothing is written in that case.</exception>
    /// <exception cref="UnauthorizedAccessException">The file exists but can't be read.</exception>
    public static DataStore Open(string directory)
    {
        var store = new DataStore(Path.GetFullPath(directory));
        store.LoadFromDisk();
        return store;
    }

    /// <summary>Reads the file again, e.g. after it was edited by hand.</summary>
    public void Reload()
    {
        lock (_writeLock)
        {
            LoadFromDisk();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void LoadFromDisk()
    {
        var (data, status, damagedCopy) = Load(FilePath);
        if (status == DataLoadStatus.RestoredFromBackup)
        {
            // Write the restored data back right away, so the next start reads it normally.
            try
            {
                Save(data);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Still usable in memory; the next change will try again.
            }
        }

        LoadStatus = status;
        DamagedFileCopy = damagedCopy;
        Volatile.Write(ref _data, data);
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the current data and saves the result.
    /// Nothing is published when saving fails, so memory and disk stay consistent.
    /// </summary>
    /// <returns>The new data.</returns>
    public PluginData Update(Func<PluginData, PluginData> change)
    {
        PluginData updated;
        lock (_writeLock)
        {
            var current = Data;
            updated = change(current);
            if (ReferenceEquals(updated, current))
            {
                return current;
            }

            Save(updated);
            Volatile.Write(ref _data, updated);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return updated;
    }

    private void Save(PluginData data)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporaryPath = FilePath + ".tmp";
        var json = JsonSerializer.Serialize(data with { Version = PluginData.CurrentVersion }, JsonOptions);
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(json);
        }

        if (!File.Exists(FilePath))
        {
            File.Move(temporaryPath, FilePath);
            return;
        }

        try
        {
            File.Replace(temporaryPath, FilePath, BackupPath, ignoreMetadataErrors: true);
        }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException)
        {
            // File.Replace is not supported everywhere (some network shares): fall back to copy + move.
            File.Copy(FilePath, BackupPath, overwrite: true);
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
    }

    private static (PluginData Data, DataLoadStatus Status, string? DamagedCopy) Load(string path)
    {
        var backupPath = path + ".bak";
        if (!File.Exists(path))
        {
            // A crash between the steps of File.Replace may leave only the backup.
            return TryRead(backupPath, out var fromBackup)
                ? (fromBackup, DataLoadStatus.RestoredFromBackup, null)
                : (new PluginData(), DataLoadStatus.Created, null);
        }

        var text = File.ReadAllText(path);
        if (TryDeserialize(text, out var data))
        {
            return (data, DataLoadStatus.Loaded, null);
        }

        // Move the damaged file aside (never delete it): the user can still recover it by hand.
        var damagedCopy = $"{path}.damaged-{DateTime.Now:yyyyMMdd-HHmmss}";
        File.Move(path, damagedCopy, overwrite: true);
        return TryRead(backupPath, out var restored)
            ? (restored, DataLoadStatus.RestoredFromBackup, damagedCopy)
            : (new PluginData(), DataLoadStatus.ResetAfterCorruption, damagedCopy);
    }

    private static bool TryRead(string path, out PluginData data)
    {
        data = new PluginData();
        try
        {
            return File.Exists(path) && TryDeserialize(File.ReadAllText(path), out data);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryDeserialize(string json, out PluginData data)
    {
        data = new PluginData();
        try
        {
            var parsed = JsonSerializer.Deserialize<PluginData>(json, JsonOptions);
            if (parsed is null)
            {
                return false;
            }

            data = parsed.Sanitize();
            return true;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}
