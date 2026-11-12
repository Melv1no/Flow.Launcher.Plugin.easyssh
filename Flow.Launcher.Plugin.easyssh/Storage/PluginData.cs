using System.Text.Json;
using System.Text.Json.Serialization;
using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Storage;

/// <summary>
/// A saved command that ssh_config can't express (another program such as <c>wsl</c> or <c>plink</c>, or an ssh
/// option without config keyword such as <c>-N</c>). Everything else is a host of <c>~/.ssh/easyssh.config</c>.
/// </summary>
/// <param name="Name">Unique name (case-insensitive).</param>
/// <param name="Command">Full command line.</param>
public sealed record SavedCommand(string Name, string Command);

/// <summary>Trace of the one-time import of the data of EasySSH 1.x (<c>~/.ssh/profiles.json</c>).</summary>
public sealed record LegacyImportInfo(DateTimeOffset Date, string? Source, int Hosts, int Commands, int Terminals);

/// <summary>Everything the plugin persists besides the ssh_config files. Instances are immutable.</summary>
public sealed record PluginData
{
    /// <summary>Current file format version.</summary>
    public const int CurrentVersion = 4;

    /// <summary>File format version.</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>Saved commands that are not ssh hosts.</summary>
    public IReadOnlyList<SavedCommand> Commands { get; init; } = [];

    /// <summary>User-defined terminals (presets are not stored).</summary>
    public IReadOnlyList<TerminalDefinition> Terminals { get; init; } = [];

    /// <summary>Name of the terminal used to open connections.</summary>
    public string SelectedTerminal { get; init; } = BuiltInTerminals.Default.Name;

    /// <summary>Whether the window waits for a key press when ssh fails, so the error can be read.</summary>
    public bool KeepWindowOpenOnError { get; init; } = true;

    /// <summary>Aliases of hosts not shown in the results (they stay in the ssh configuration).</summary>
    public IReadOnlyList<string> HiddenHosts { get; init; } = [];

    /// <summary>Whether servers of <c>~/.ssh/known_hosts</c> are suggested while typing.</summary>
    public bool SuggestKnownHosts { get; init; } = true;

    /// <summary>Set once the data of the previous versions has been looked for.</summary>
    public LegacyImportInfo? LegacyImport { get; init; }

    /// <summary>Unknown properties, kept so that a file written by a newer version is not truncated.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

/// <summary>Pure operations on <see cref="PluginData"/>.</summary>
public static class PluginDataOperations
{
    /// <summary>Name comparison: case-insensitive.</summary>
    public static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>Finds a saved command by name.</summary>
    public static SavedCommand? FindCommand(this PluginData data, string name) =>
        data.Commands.FirstOrDefault(command => NameComparer.Equals(command.Name, name.Trim()));

    /// <summary>
    /// Adds a command, or replaces the one with the same name. When <paramref name="previousName"/> is given,
    /// the command is renamed: the old entry is removed and its position kept.
    /// </summary>
    /// <exception cref="ArgumentException">The name or the command is empty, or the new name is used by another command.</exception>
    public static PluginData UpsertCommand(this PluginData data, SavedCommand command, string? previousName = null)
    {
        var name = command.Name.Trim();
        var text = command.Command.Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("The name is empty.", nameof(command));
        }

        if (text.Length == 0)
        {
            throw new ArgumentException("The command is empty.", nameof(command));
        }

        var replaced = previousName ?? name;
        if (!NameComparer.Equals(replaced, name) && data.FindCommand(name) is not null)
        {
            throw new ArgumentException($"A command named \"{name}\" already exists.", nameof(command));
        }

        var updated = new SavedCommand(name, text);
        var commands = data.Commands.ToList();
        var index = commands.FindIndex(existing => NameComparer.Equals(existing.Name, replaced));
        if (index >= 0)
        {
            commands[index] = updated;
        }
        else
        {
            commands.Add(updated);
        }

        return data with { Commands = commands };
    }

    /// <summary>Removes a command by name. Returns the same instance when there is no such command.</summary>
    public static PluginData RemoveCommand(this PluginData data, string name)
    {
        var commands = data.Commands.Where(command => !NameComparer.Equals(command.Name, name)).ToList();
        return commands.Count == data.Commands.Count ? data : data with { Commands = commands };
    }

    /// <summary>Whether a host is hidden from the results.</summary>
    public static bool IsHidden(this PluginData data, string alias) => data.HiddenHosts.Contains(alias, NameComparer);

    /// <summary>Hides a host from the results, or shows it again.</summary>
    public static PluginData SetHidden(this PluginData data, string alias, bool hidden)
    {
        if (data.IsHidden(alias) == hidden)
        {
            return data;
        }

        var hiddenHosts = hidden
            ? [.. data.HiddenHosts, alias]
            : data.HiddenHosts.Where(existing => !NameComparer.Equals(existing, alias)).ToList();
        return data with { HiddenHosts = hiddenHosts };
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
        return data.SelectedTerminal == terminal.Name ? data : data with { SelectedTerminal = terminal.Name };
    }

    /// <summary>
    /// Fixes what may be wrong in a file edited by hand: missing lists, blank or duplicated names,
    /// user terminals using a reserved name, unknown selected terminal.
    /// </summary>
    public static PluginData Sanitize(this PluginData data)
    {
        var commandNames = new HashSet<string>(NameComparer);
        var commands = (data.Commands ?? [])
            .Where(command => command is not null && !string.IsNullOrWhiteSpace(command.Name) && !string.IsNullOrWhiteSpace(command.Command))
            .Select(command => new SavedCommand(command.Name.Trim(), command.Command.Trim()))
            .Where(command => commandNames.Add(command.Name))
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

        var hidden = (data.HiddenHosts ?? [])
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(alias => alias.Trim())
            .Distinct(NameComparer)
            .ToList();

        var sanitized = data with { Commands = commands, Terminals = terminals, HiddenHosts = hidden };
        var selected = sanitized.FindTerminal(data.SelectedTerminal)?.Name ?? BuiltInTerminals.Default.Name;
        return sanitized with { SelectedTerminal = selected };
    }
}
