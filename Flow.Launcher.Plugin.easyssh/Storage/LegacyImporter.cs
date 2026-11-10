using System.Text.Json;
using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Storage;

/// <summary>Format of a <c>profiles.json</c> written by an older version.</summary>
public enum LegacyFormat
{
    /// <summary>Unrecognized content.</summary>
    Unknown,

    /// <summary>EasySSH 1.1: <c>{"1": {"Id": 1, "Name": "…", "Command": "user@host"}}</c>; commands are ssh arguments.</summary>
    Version1,

    /// <summary>EasySSH 1.2: <c>{"EntriesLists": {"name": "ssh user@host"}, "CustomShellLists": {…}}</c>.</summary>
    Version2,
}

/// <summary>What was found in a legacy file.</summary>
public sealed record LegacyData(
    LegacyFormat Format,
    IReadOnlyList<SshProfile> Profiles,
    IReadOnlyList<TerminalDefinition> Terminals,
    string? SelectedTerminal);

/// <summary>Result of merging legacy data into the current data.</summary>
public sealed record LegacyMergeResult(PluginData Data, int AddedProfiles, int AddedTerminals);

/// <summary>
/// Reads the data of EasySSH 1.x (<c>%USERPROFILE%\.ssh\profiles.json</c>) so it can be merged into the
/// current data. The legacy file is only read, never modified.
/// </summary>
public static class LegacyImporter
{
    /// <summary>Placeholder of the command in the shells of the 1.2 development builds.</summary>
    private const string LegacyCommandPlaceholder = "{ssh}";

    /// <summary>
    /// Shells that the 1.2 development builds added by themselves. The user didn't choose them and a preset
    /// does the same (with a window title and the window kept open on errors): the preset is used instead of a copy.
    /// </summary>
    private static readonly (string Key, string Value, TerminalDefinition Preset)[] AddedByDevelopmentBuilds =
    [
        ("cmd.exe", "/k \"{ssh}\"", BuiltInTerminals.CommandPrompt),
        ("powershell.exe", "-NoExit -Command \"{ssh}\"", BuiltInTerminals.WindowsPowerShell),
    ];

    /// <summary>Where EasySSH 1.x stored its data.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "profiles.json");

    /// <summary>Reads a legacy file. Returns null when it doesn't exist.</summary>
    /// <exception cref="IOException">The file can't be read.</exception>
    /// <exception cref="JsonException">The file is not valid JSON.</exception>
    public static LegacyData? Read(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : null;

    /// <summary>Parses the content of a legacy file.</summary>
    /// <exception cref="JsonException">The content is not valid JSON.</exception>
    public static LegacyData Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new LegacyData(LegacyFormat.Unknown, [], [], null);
        }

        if (TryGetProperty(root, "EntriesLists", out var entries) || TryGetProperty(root, "CustomShellLists", out _))
        {
            return ParseVersion2(root, entries);
        }

        return ParseVersion1(root);
    }

    /// <summary>
    /// Adds the legacy profiles and terminals whose names are not used yet. The legacy selected terminal is
    /// kept only when the current selection is still the default one.
    /// </summary>
    public static LegacyMergeResult Merge(PluginData data, LegacyData legacy)
    {
        var profiles = data.Profiles.ToList();
        var addedProfiles = 0;
        foreach (var profile in legacy.Profiles)
        {
            if (profiles.Any(existing => PluginDataOperations.NameComparer.Equals(existing.Name, profile.Name)))
            {
                continue;
            }

            profiles.Add(profile);
            addedProfiles++;
        }

        var terminals = data.Terminals.ToList();
        var addedTerminals = 0;
        foreach (var terminal in legacy.Terminals)
        {
            if (terminals.Any(existing => PluginDataOperations.NameComparer.Equals(existing.Name, terminal.Name)))
            {
                continue;
            }

            terminals.Add(terminal);
            addedTerminals++;
        }

        var merged = data with { Profiles = profiles, Terminals = terminals };
        var selectionIsDefault = PluginDataOperations.NameComparer.Equals(data.SelectedTerminal, BuiltInTerminals.Default.Name);
        if (selectionIsDefault && legacy.SelectedTerminal is not null && merged.FindTerminal(legacy.SelectedTerminal) is not null)
        {
            merged = merged.SelectTerminal(legacy.SelectedTerminal);
        }

        return new LegacyMergeResult(merged, addedProfiles, addedTerminals);
    }

    private static LegacyData ParseVersion1(JsonElement root)
    {
        var entries = new List<(int Order, string Name, string Command)>();
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object
                || !TryGetString(property.Value, "Name", out var name)
                || !TryGetString(property.Value, "Command", out var command))
            {
                continue;
            }

            var order = int.TryParse(property.Name, out var id) ? id : int.MaxValue;
            entries.Add((order, name, command));
        }

        var profiles = UniqueProfiles(entries.OrderBy(entry => entry.Order).Select(entry => (entry.Name, entry.Command)));
        return new LegacyData(entries.Count > 0 ? LegacyFormat.Version1 : LegacyFormat.Unknown, profiles, [], null);
    }

    private static LegacyData ParseVersion2(JsonElement root, JsonElement entries)
    {
        var profiles = new List<SshProfile>();
        if (entries.ValueKind == JsonValueKind.Object)
        {
            profiles = UniqueProfiles(entries.EnumerateObject()
                .Where(property => property.Value.ValueKind == JsonValueKind.String)
                .Select(property => (property.Name, property.Value.GetString() ?? string.Empty)));
        }

        var terminals = new List<TerminalDefinition>();
        var legacyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (TryGetProperty(root, "CustomShellLists", out var shells) && shells.ValueKind == JsonValueKind.Object)
        {
            foreach (var shell in shells.EnumerateObject())
            {
                var value = shell.Value.ValueKind == JsonValueKind.String ? shell.Value.GetString() : null;
                if (FindAddedPreset(shell.Name, value) is { } preset)
                {
                    legacyNames[shell.Name] = preset.Name;
                    continue;
                }

                var terminal = ConvertShell(shell.Name, value);
                if (terminal is null)
                {
                    continue;
                }

                var name = UniqueName(terminal.Name, terminals.Select(existing => existing.Name));
                terminals.Add(terminal with { Name = name });
                legacyNames[shell.Name] = name;
            }
        }

        string? selected = null;
        if (TryGetString(root, "SelectedCustomShell", out var selectedShell) && legacyNames.TryGetValue(selectedShell, out var selectedName))
        {
            selected = selectedName;
        }

        return new LegacyData(LegacyFormat.Version2, profiles, terminals, selected);
    }

    /// <summary>
    /// Converts an EasySSH 1.x custom shell. Three forms were written:
    /// <list type="bullet">
    /// <item>1.2: the key is the program and the value is empty (<c>"wt.exe": ""</c>);</item>
    /// <item>1.2.0.1: the key is a name and the value holds the program and its arguments
    /// (<c>"git-bash": "wt.exe -p \"Git Bash\""</c>);</item>
    /// <item>development builds: the key is the program and the value its arguments, where <c>{ssh}</c> stands for
    /// the command (<c>"kitty.exe": "-e {ssh}"</c>).</item>
    /// </list>
    /// Without <c>{ssh}</c>, the command was added at the end, as without <c>{command}</c> now.
    /// </summary>
    private static TerminalDefinition? ConvertShell(string key, string? value)
    {
        var executable = key.Trim().Trim('"');
        if (executable.Length == 0)
        {
            return null;
        }

        var arguments = value?.Trim() ?? string.Empty;
        var (program, programArguments) = CommandLine.SplitFirst(arguments);
        if (arguments.Length == 0 || arguments.Contains(LegacyCommandPlaceholder, StringComparison.Ordinal) || IsOption(program))
        {
            return new TerminalDefinition(
                ReserveSafeName(Path.GetFileName(executable)),
                executable,
                arguments.Replace(LegacyCommandPlaceholder, LaunchPlanBuilder.CommandPlaceholder, StringComparison.Ordinal));
        }

        return program.Length == 0 ? null : new TerminalDefinition(ReserveSafeName(key.Trim()), program, programArguments);
    }

    /// <summary>Whether a token is an option (<c>-e</c>, <c>/k</c>) rather than a program.</summary>
    private static bool IsOption(string token) => token.StartsWith('-') || token.StartsWith('/');

    private static TerminalDefinition? FindAddedPreset(string key, string? value) =>
        AddedByDevelopmentBuilds.FirstOrDefault(shell =>
            shell.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase)
            && shell.Value.Equals(value?.Trim(), StringComparison.Ordinal)).Preset;

    private static string ReserveSafeName(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "terminal" : name.Trim();
        return BuiltInTerminals.IsReservedName(trimmed) ? trimmed + " (custom)" : trimmed;
    }

    private static List<SshProfile> UniqueProfiles(IEnumerable<(string Name, string Command)> entries)
    {
        var profiles = new List<SshProfile>();
        foreach (var (rawName, rawCommand) in entries)
        {
            var command = SshCommand.Normalize(rawCommand);
            if (string.IsNullOrWhiteSpace(rawName) || command.Length == 0)
            {
                continue;
            }

            profiles.Add(new SshProfile(UniqueName(rawName.Trim(), profiles.Select(profile => profile.Name)), command));
        }

        return profiles;
    }

    private static string UniqueName(string name, IEnumerable<string> existingNames)
    {
        var used = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(name))
        {
            return name;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{name} ({suffix})";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(element, name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }
}
