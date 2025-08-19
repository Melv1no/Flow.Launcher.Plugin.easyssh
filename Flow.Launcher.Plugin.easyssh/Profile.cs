using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Holds plugin user data persisted on disk.
/// Contains the plugin version and two key/value stores:
/// - EntriesLists: main entries (serialized)
/// - CustomShellLists: custom shell mappings (serialized)
/// Exposes auto-saving wrappers (not serialized) via <see cref="Entries"/> and <see cref="CustomShell"/>.
/// </summary>
public class UserData
{
    /// <summary>
    /// Semantic version of the data format used by the plugin.
    /// </summary>
    public string PluginVersion { get; set; } = "1.0";

    /// <summary>
    /// Backing store for entries (serialized). Kept private to enforce autosave via <see cref="Entries"/>.
    /// </summary>
    [JsonProperty]
    private Dictionary<string, string> EntriesLists { get; set; } = new();

    /// <summary>
    /// Auto-saving facade over <see cref="EntriesLists"/> (not serialized).
    /// Any mutation triggers the provided onChanged callback.
    /// </summary>
    [JsonIgnore]
    public AutoSaveDictionary<string, string> Entries { get; private set; }

    /// <summary>
    /// Backing store for custom shells (serialized). Kept private to enforce autosave via <see cref="CustomShell"/>.
    /// </summary>
    [JsonProperty]
    private Dictionary<string, string> CustomShellLists { get; set; } = new();

    /// <summary>
    /// Auto-saving facade over <see cref="CustomShellLists"/> (not serialized).
    /// Any mutation triggers the provided onChanged callback.
    /// </summary>
    [JsonIgnore]
    public AutoSaveDictionary<string, string> CustomShell { get; private set; }

    /// <summary>
    /// Binds the auto-save callback to both facades.
    /// Call this after construction and after deserialization.
    /// </summary>
    /// <param name="onChanged">Callback invoked on any mutation.</param>
    public void Attach(Action onChanged)
    {
        // ensure lists are not null (safe guard if deserialized from older/minimal files)
        EntriesLists ??= new Dictionary<string, string>();
        CustomShellLists ??= new Dictionary<string, string>();

        Entries = new AutoSaveDictionary<string, string>(EntriesLists, onChanged);
        CustomShell = new AutoSaveDictionary<string, string>(CustomShellLists, onChanged);
    }
}

/// <summary>
/// Minimal auto-saving dictionary wrapper that forwards to an inner dictionary
/// and invokes a change callback on any mutating operation.
/// This type is NOT serialized; only the inner dictionary is.
/// </summary>
public sealed class AutoSaveDictionary<TKey, TValue> : IDictionary<TKey, TValue>
{
    private static readonly Action Noop = () => { };

    private readonly IDictionary<TKey, TValue> _inner;
    private Action _onChanged;

    /// <summary>
    /// Creates a facade over an existing dictionary.
    /// </summary>
    /// <param name="inner">The target dictionary to mutate.</param>
    /// <param name="onChanged">Callback invoked after each mutation.</param>
    public AutoSaveDictionary(IDictionary<TKey, TValue> inner, Action onChanged)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _onChanged = onChanged ?? Noop;
    }

    /// <summary>
    /// Replaces the change callback at runtime (e.g., after reloading configuration).
    /// </summary>
    /// <param name="onChanged">New callback to use (no-op if null).</param>
    public void SetCallback(Action onChanged) => _onChanged = onChanged ?? Noop;

    public TValue this[TKey key]
    {
        get => _inner[key];
        set { _inner[key] = value; _onChanged(); }
    }

    public ICollection<TKey> Keys => _inner.Keys;
    public ICollection<TValue> Values => _inner.Values;
    public int Count => _inner.Count;
    public bool IsReadOnly => _inner.IsReadOnly;

    public void Add(TKey key, TValue value) { _inner.Add(key, value); _onChanged(); }
    public bool ContainsKey(TKey key) => _inner.ContainsKey(key);
    public bool Remove(TKey key) { var ok = _inner.Remove(key); if (ok) _onChanged(); return ok; }
    public bool TryGetValue(TKey key, out TValue value) => _inner.TryGetValue(key, out value);

    public void Add(KeyValuePair<TKey, TValue> item) { _inner.Add(item); _onChanged(); }
    public void Clear() { _inner.Clear(); _onChanged(); }
    public bool Contains(KeyValuePair<TKey, TValue> item) => _inner.Contains(item);
    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);
    public bool Remove(KeyValuePair<TKey, TValue> item) { var ok = _inner.Remove(item); if (ok) _onChanged(); return ok; }
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _inner.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _inner.GetEnumerator();
}

/// <summary>
/// Coordinates reading/writing of <see cref="UserData"/> to a JSON file.
/// Exposes an instance whose dictionaries auto-save on mutation.
/// </summary>
public class ProfileManager
{
    private readonly string _path;

    /// <summary>
    /// Current in-memory user data. Use <see cref="UserData.Entries"/> and <see cref="UserData.CustomShell"/>
    /// to get auto-saving behavior on mutation.
    /// </summary>
    public UserData UserData { get; private set; }

    /// <summary>
    /// Creates a manager bound to a JSON file at <paramref name="path"/>.
    /// File is created if missing; otherwise it is loaded.
    /// </summary>
    public ProfileManager(string path)
    {
        _path = path;

        if (!File.Exists(_path))
        {
            UserData = new UserData();
            UserData.Attach(SaveConfiguration);
            SaveConfiguration();
        }
        else
        {
            LoadConfiguration();
        }
    }

    /// <summary>
    /// Persists <see cref="UserData"/> to disk (atomic write).
    /// </summary>
    public void SaveConfiguration()
    {
        var json = JsonConvert.SerializeObject(UserData, Formatting.Indented);

        // Atomic write to avoid partial writes: write to temp then replace.
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, _path, overwrite: true);
        File.Delete(tmp);
    }

    /// <summary>
    /// Loads <see cref="UserData"/> from disk and rebinds auto-save facades.
    /// </summary>
    public void LoadConfiguration()
    {
        var json = File.ReadAllText(_path);
        UserData = JsonConvert.DeserializeObject<UserData>(json) ?? new UserData();

        // Ensure non-null inner stores (in case of minimal/legacy files)
        // then rebind auto-save wrappers to call SaveConfiguration on change.
        UserData.Attach(SaveConfiguration);
    }
}
