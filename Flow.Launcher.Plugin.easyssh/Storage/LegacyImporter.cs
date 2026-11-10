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

/// <summary>A profile of EasySSH 1.x: a name and its command (normalized, see <see cref="SshCommand.Normalize"/>).</summary>
public sealed record LegacyProfile(string Name, string Command);

/// <summary>What was found in a legacy file.</summary>
public sealed record LegacyData(
    LegacyFormat Format,
    IReadOnlyList<LegacyProfile> Profiles,
    IReadOnlyList<TerminalDefinition> Terminals,
    string? SelectedTerminal);

/// <summary>What an import added.</summary>
/// <param name="Data">The plugin data with the commands and terminals added.</param>
/// <param name="AddedHosts">Aliases of the hosts added to the managed ssh_config file.</param>
/// <param name="AddedCommands">Number of saved commands added.</param>
/// <param name="AddedTerminals">Number of terminals added.</param>
public sealed record LegacyImportResult(PluginData Data, IReadOnlyList<string> AddedHosts, int AddedCommands, int AddedTerminals)
{
    /// <summary>Whether anything was added.</summary>
    public bool AddedAnything => AddedHosts.Count + AddedCommands + AddedTerminals > 0;
}

/// <summary>
/// Reads the data of EasySSH 1.x (<c>%USERPROFILE%\.ssh\profiles.json</c>) and brings it into this version.
/// The legacy file is only read, never modified.
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
    /// Brings the legacy profiles and terminals into this version. A profile becomes a host of
    /// <paramref name="managed"/> (changed in place) only when <c>ssh alias</c> then does exactly what the profile
    /// did, and nothing changes for the other connections:
    /// <list type="bullet">
    /// <item>ssh_config can express its options, and ssh accepts them (<paramref name="isValid"/>);</item>
    /// <item>its destination is not another host, of the configuration or of another profile
    /// (<c>HostName other</c> would not use the options of <c>other</c>);</item>
    /// <item>the <c>Host</c> patterns of the configuration apply the same way to the alias and to the destination;</item>
    /// <item>the alias is not the destination itself with options: <c>Host github.com</c> with a key would change
    /// every connection to github.com, git's included.</item>
    /// </list>
    /// Its name becomes the alias, with a suffix when another host already uses it. A profile that only runs
    /// <c>ssh alias</c> for an existing host is skipped; any other profile becomes a saved command. Nothing existing
    /// is replaced, so importing twice adds nothing. The legacy selected terminal is kept only when the current
    /// selection is still the default one.
    /// </summary>
    /// <param name="data">The current plugin data.</param>
    /// <param name="managed">The managed ssh_config file, where hosts are added.</param>
    /// <param name="existing">The whole ssh configuration, to avoid shadowing or bypassing it.</param>
    /// <param name="legacy">What was read from the legacy file.</param>
    /// <param name="isValid">Whether ssh accepts a host, or null to accept every valid line.</param>
    public static LegacyImportResult Import(
        PluginData data, ManagedConfig managed, SshConfigSnapshot existing, LegacyData legacy, Func<SshHostEntry, bool>? isValid = null)
    {
        var addedHosts = new List<string>();
        var addedCommands = 0;
        var profileAliases = legacy.Profiles
            .Select(profile => SshConfigText.ToAlias(profile.Name))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in legacy.Profiles)
        {
            if (IsExistingAlias(profile.Command, existing, managed))
            {
                continue;
            }

            if (SshConfigText.ToAlias(profile.Name) is { } alias)
            {
                if (IsAlreadyImported(alias, profile.Command, managed, existing))
                {
                    continue;
                }

                if (FindFaithfulHost(alias, profile.Command, managed, existing, profileAliases, isValid) is { } host)
                {
                    try
                    {
                        managed.Upsert(host);
                        addedHosts.Add(host.Alias);
                        continue;
                    }
                    catch (ArgumentException)
                    {
                        // Can't be written: kept as a command below.
                    }
                }
            }

            if (data.FindCommand(profile.Name) is null)
            {
                data = data.UpsertCommand(new SavedCommand(profile.Name, profile.Command));
                addedCommands++;
            }
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

        var merged = data with { Terminals = terminals };
        var selectionIsDefault = PluginDataOperations.NameComparer.Equals(data.SelectedTerminal, BuiltInTerminals.Default.Name);
        if (selectionIsDefault && legacy.SelectedTerminal is not null && merged.FindTerminal(legacy.SelectedTerminal) is not null)
        {
            merged = merged.SelectTerminal(legacy.SelectedTerminal);
        }

        return new LegacyImportResult(merged, addedHosts, addedCommands, addedTerminals);
    }

    /// <summary>Whether a command is only <c>ssh alias</c> for a host that already exists (or was just imported).</summary>
    private static bool IsExistingAlias(string command, SshConfigSnapshot existing, ManagedConfig managed) =>
        CommandLine.Tokenize(command) is [var program, var destination]
        && program.Value.Equals("ssh", StringComparison.OrdinalIgnoreCase)
        && (existing.Find(destination.Value) is not null || managed.Declares(destination.Value));

    /// <summary>
    /// Whether the managed file already has this profile (imported before), under its alias or a suffixed one:
    /// the aliases are tried in the order <see cref="FindFaithfulHost"/> uses them.
    /// </summary>
    private static bool IsAlreadyImported(string alias, string command, ManagedConfig managed, SshConfigSnapshot existing)
    {
        foreach (var candidate in Candidates(alias))
        {
            if (managed.Find(candidate) is { } current)
            {
                if (SshCommandConverter.ToHost(candidate, command).Host is { } host && current.Options.SequenceEqual(host.Options))
                {
                    return true;
                }

                continue;
            }

            if (!managed.Declares(candidate) && existing.Find(candidate) is null)
            {
                // The first free alias: an earlier import would have used it.
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// The host to add under the first free alias (<c>prod</c>, <c>prod-2</c>…), or null when the profile must stay
    /// a command (see <see cref="Import"/>). The conversion is redone for the alias chosen: the host name is only
    /// written when it differs from the alias.
    /// </summary>
    private static SshHostEntry? FindFaithfulHost(
        string alias, string command, ManagedConfig managed, SshConfigSnapshot existing, IReadOnlySet<string> profileAliases, Func<SshHostEntry, bool>? isValid)
    {
        var candidate = Candidates(alias).FirstOrDefault(name => !managed.Declares(name) && existing.Find(name) is null);
        if (candidate is null)
        {
            return null;
        }

        var conversion = SshCommandConverter.ToHost(candidate, command);
        if (conversion is not { Host: { } host, Destination: { } destination })
        {
            return null;
        }

        var isItself = destination.Equals(candidate, StringComparison.OrdinalIgnoreCase);
        var unfaithful = isItself
            ? host.Options.Count > 0
            : existing.Find(destination) is not null || managed.Declares(destination) || profileAliases.Contains(destination)
              || !existing.HaveSameRules(candidate, destination);
        if (unfaithful || !ManagedConfig.IsValidHost(host) || (isValid is not null && !isValid(host)))
        {
            return null;
        }

        return host;
    }

    private static IEnumerable<string> Candidates(string alias)
    {
        yield return alias;
        for (var suffix = 2; suffix < 1000; suffix++)
        {
            yield return $"{alias}-{suffix}";
        }
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

        // EasySSH 1.1 ran its commands as ssh arguments: ssh always goes in front, even before a word that looks like a program.
        var profiles = UniqueProfiles(SshCommand.NormalizeSshArguments, entries.OrderBy(entry => entry.Order).Select(entry => (entry.Name, entry.Command)));
        return new LegacyData(entries.Count > 0 ? LegacyFormat.Version1 : LegacyFormat.Unknown, profiles, [], null);
    }

    private static LegacyData ParseVersion2(JsonElement root, JsonElement entries)
    {
        var profiles = new List<LegacyProfile>();
        if (entries.ValueKind == JsonValueKind.Object)
        {
            profiles = UniqueProfiles(SshCommand.Normalize, entries.EnumerateObject()
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

    private static List<LegacyProfile> UniqueProfiles(Func<string, string> normalize, IEnumerable<(string Name, string Command)> entries)
    {
        var profiles = new List<LegacyProfile>();
        foreach (var (rawName, rawCommand) in entries)
        {
            var command = normalize(rawCommand);
            if (string.IsNullOrWhiteSpace(rawName) || command.Length == 0)
            {
                continue;
            }

            profiles.Add(new LegacyProfile(UniqueName(rawName.Trim(), profiles.Select(profile => profile.Name)), command));
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
