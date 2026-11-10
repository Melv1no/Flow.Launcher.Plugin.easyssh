using System.Text.Json;
using System.Text.Json.Serialization;
using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Storage;

/// <summary>A saved connection.</summary>
/// <param name="Name">Unique name (case-insensitive).</param>
/// <param name="Command">Full command line, e.g. <c>ssh -p 2222 admin@10.0.0.5</c>.</param>
public sealed record SshProfile(string Name, string Command);

/// <summary>Trace of the one-time import of the data of EasySSH 1.x/2.x (<c>~/.ssh/profiles.json</c>).</summary>
public sealed record LegacyImportInfo(DateTimeOffset Date, string? Source, int Profiles, int Terminals);

/// <summary>Everything the plugin persists. Instances are immutable: changes create a new instance.</summary>
public sealed record PluginData
{
    /// <summary>Current file format version.</summary>
    public const int CurrentVersion = 3;

    /// <summary>File format version.</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>Saved connections.</summary>
    public IReadOnlyList<SshProfile> Profiles { get; init; } = [];

    /// <summary>User-defined terminals (presets are not stored).</summary>
    public IReadOnlyList<TerminalDefinition> Terminals { get; init; } = [];

    /// <summary>Name of the terminal used to open connections.</summary>
    public string SelectedTerminal { get; init; } = BuiltInTerminals.Default.Name;

    /// <summary>Whether hosts of <c>~/.ssh/config</c> are listed with the profiles.</summary>
    public bool ShowSshConfigHosts { get; init; } = true;

    /// <summary>Whether the window waits for a key press when ssh fails, so the error can be read.</summary>
    public bool KeepWindowOpenOnError { get; init; } = true;

    /// <summary>Set once the data of the previous versions has been looked for.</summary>
    public LegacyImportInfo? LegacyImport { get; init; }

    /// <summary>Unknown properties, kept so that a file written by a newer version is not truncated.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

/// <summary>Pure operations on <see cref="PluginData"/>.</summary>
public static class PluginDataOperations
{
    /// <summary>Profile name comparison: case-insensitive.</summary>
    public static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>Finds a profile by name.</summary>
    public static SshProfile? FindProfile(this PluginData data, string name) =>
        data.Profiles.FirstOrDefault(profile => NameComparer.Equals(profile.Name, name.Trim()));

    /// <summary>
    /// Adds a profile, or replaces the one with the same name. When <paramref name="previousName"/> is given,
    /// the profile is renamed: the old entry is removed and its position kept.
    /// </summary>
    /// <exception cref="ArgumentException">The name or the command is empty, or the new name is used by another profile.</exception>
    public static PluginData UpsertProfile(this PluginData data, SshProfile profile, string? previousName = null)
    {
        var name = profile.Name.Trim();
        var command = profile.Command.Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("The profile name is empty.", nameof(profile));
        }

        if (command.Length == 0)
        {
            throw new ArgumentException("The profile command is empty.", nameof(profile));
        }

        var replaced = previousName ?? name;
        if (!NameComparer.Equals(replaced, name) && data.FindProfile(name) is not null)
        {
            throw new ArgumentException($"A profile named \"{name}\" already exists.", nameof(profile));
        }

        var updated = new SshProfile(name, command);
        var profiles = data.Profiles.ToList();
        var index = profiles.FindIndex(existing => NameComparer.Equals(existing.Name, replaced));
        if (index >= 0)
        {
            profiles[index] = updated;
        }
        else
        {
            profiles.Add(updated);
        }

        return data with { Profiles = profiles };
    }

    /// <summary>Removes a profile by name. Returns the same instance when there is no such profile.</summary>
    public static PluginData RemoveProfile(this PluginData data, string name)
    {
        var profiles = data.Profiles.Where(profile => !NameComparer.Equals(profile.Name, name)).ToList();
        return profiles.Count == data.Profiles.Count ? data : data with { Profiles = profiles };
    }

    /// <summary>All terminals: presets first, then user-defined ones.</summary>
    public static IEnumerable<TerminalDefinition> AllTerminals(this PluginData data) =>
        BuiltInTerminals.All.Concat(data.Terminals);

    /// <summary>Finds a terminal (preset or user-defined) by name.</summary>
    public static TerminalDefinition? FindTerminal(this PluginData data, string? name) =>
        data.AllTerminals().FirstOrDefault(terminal => NameComparer.Equals(terminal.Name, name?.Trim()));

    /// <summary>The selected terminal, or the default one when the selection no longer exists.</summary>
    public static TerminalDefinition GetSelectedTerminal(this PluginData data) =>
        data.FindTerminal(data.SelectedTerminal) ?? BuiltInTerminals.Default;

    /// <summary>Adds or replaces a user-defined terminal (renaming it when <paramref name="previousName"/> is given).</summary>
    /// <exception cref="ArgumentException">Invalid or reserved name, empty program, or name already used.</exception>
    public static PluginData UpsertTerminal(this PluginData data, TerminalDefinition terminal, string? previousName = null)
    {
        var name = terminal.Name.Trim();
        var executable = terminal.Executable.Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("The terminal name is empty.", nameof(terminal));
        }

        if (BuiltInTerminals.IsReservedName(name))
        {
            throw new ArgumentException($"\"{name}\" is reserved for a built-in terminal.", nameof(terminal));
        }

        if (executable.Length == 0)
        {
            throw new ArgumentException("The terminal program is empty.", nameof(terminal));
        }

        var replaced = previousName ?? name;
        if (!NameComparer.Equals(replaced, name) && data.Terminals.Any(existing => NameComparer.Equals(existing.Name, name)))
        {
            throw new ArgumentException($"A terminal named \"{name}\" already exists.", nameof(terminal));
        }

        var updated = new TerminalDefinition(name, executable, terminal.Arguments.Trim());
        var terminals = data.Terminals.ToList();
        var index = terminals.FindIndex(existing => NameComparer.Equals(existing.Name, replaced));
        if (index >= 0)
        {
            terminals[index] = updated;
        }
        else
        {
            terminals.Add(updated);
        }

        var selected = NameComparer.Equals(data.SelectedTerminal, replaced) ? name : data.SelectedTerminal;
        return data with { Terminals = terminals, SelectedTerminal = selected };
    }

    /// <summary>Removes a user-defined terminal; the default terminal is selected if it was the selected one.</summary>
    public static PluginData RemoveTerminal(this PluginData data, string name)
    {
        var terminals = data.Terminals.Where(terminal => !NameComparer.Equals(terminal.Name, name)).ToList();
        if (terminals.Count == data.Terminals.Count)
        {
            return data;
        }

        var selected = NameComparer.Equals(data.SelectedTerminal, name) ? BuiltInTerminals.Default.Name : data.SelectedTerminal;
        return data with { Terminals = terminals, SelectedTerminal = selected };
    }

    /// <summary>Selects the terminal used to open connections.</summary>
    /// <exception cref="ArgumentException">There is no terminal with this name.</exception>
    public static PluginData SelectTerminal(this PluginData data, string name)
    {
        var terminal = data.FindTerminal(name) ?? throw new ArgumentException($"Unknown terminal \"{name}\".", nameof(name));
        return NameComparer.Equals(data.SelectedTerminal, terminal.Name) && data.SelectedTerminal == terminal.Name
            ? data
            : data with { SelectedTerminal = terminal.Name };
    }

    /// <summary>
    /// Fixes what may be wrong in a file edited by hand: missing lists, blank or duplicated names,
    /// user terminals using a reserved name, unknown selected terminal.
    /// </summary>
    public static PluginData Sanitize(this PluginData data)
    {
        var profileNames = new HashSet<string>(NameComparer);
        var profiles = (data.Profiles ?? [])
            .Where(profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Name) && !string.IsNullOrWhiteSpace(profile.Command))
            .Select(profile => new SshProfile(profile.Name.Trim(), profile.Command.Trim()))
            .Where(profile => profileNames.Add(profile.Name))
            .ToList();

        var terminalNames = new HashSet<string>(NameComparer);
        var terminals = (data.Terminals ?? [])
            .Where(terminal => terminal is not null
                               && !string.IsNullOrWhiteSpace(terminal.Name)
                               && !string.IsNullOrWhiteSpace(terminal.Executable)
                               && !BuiltInTerminals.IsReservedName(terminal.Name.Trim()))
            .Select(terminal => new TerminalDefinition(terminal.Name.Trim(), terminal.Executable.Trim(), terminal.Arguments?.Trim() ?? string.Empty))
            .Where(terminal => terminalNames.Add(terminal.Name))
            .ToList();

        var sanitized = data with { Profiles = profiles, Terminals = terminals };
        var selected = sanitized.FindTerminal(data.SelectedTerminal)?.Name ?? BuiltInTerminals.Default.Name;
        return sanitized with { SelectedTerminal = selected };
    }
}
