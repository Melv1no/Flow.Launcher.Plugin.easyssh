using System.Windows;
using Flow.Launcher.Plugin.EasySsh.Core;
using Flow.Launcher.Plugin.EasySsh.Storage;

namespace Flow.Launcher.Plugin.EasySsh;

/// <summary>Commands understood after the action keyword.</summary>
internal enum Verb
{
    Add,
    Edit,
    Remove,
    Hosts,
    Direct,
    Terminal,
    Config,
    Hidden,
    Help,
}

/// <summary>The query being answered.</summary>
/// <param name="Search">What was typed after the action keyword (trimmed by Flow Launcher).</param>
/// <param name="ActionKeyword">The action keyword used, empty for a global query.</param>
internal readonly record struct QueryContext(string Search, string ActionKeyword)
{
    /// <summary>Text to put before a sub-command when changing the query (action keyword and a space).</summary>
    public string Prefix => ActionKeyword.Length == 0 || ActionKeyword == Query.GlobalPluginWildcardSign ? string.Empty : ActionKeyword + " ";
}

/// <summary>Context data of a host result.</summary>
internal sealed record HostItem(SshConfigHost Host, bool IsManaged, string Prefix);

/// <summary>Context data of a saved command result.</summary>
internal sealed record CommandItem(SavedCommand Command, string Prefix);

/// <summary>Context data of a one-time connection result.</summary>
internal sealed record DirectItem(string Command, string Prefix);

/// <summary>Context data of a <c>known_hosts</c> suggestion.</summary>
internal sealed record KnownHostItem(KnownHost Host, string Prefix);

/// <summary>Turns queries into results.</summary>
internal sealed class QueryHandler(PluginServices services)
{
    // Higher scores come first. Flow Launcher adds the number of times a result was chosen, so the gaps
    // are wide: usage reorders results of the same tier but never moves a result to another tier.
    private const int TierSpacing = 1_000_000;
    private const int PrefixBonus = 100_000;
    private const int TopScore = 50_000_000;
    private const int WarningScore = 60_000_000;
    private const int KnownHostScore = 1_000;
    private const int KnownHostLimit = 5;
    private const int ShortcutScore = 100;
    private const int FallbackScore = 0;

    private static readonly Dictionary<string, Verb> Verbs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["add"] = Verb.Add,
        ["edit"] = Verb.Edit,
        ["remove"] = Verb.Remove,
        ["rm"] = Verb.Remove,
        ["hosts"] = Verb.Hosts,
        ["profiles"] = Verb.Hosts,
        ["p"] = Verb.Hosts,
        ["d"] = Verb.Direct,
        ["direct"] = Verb.Direct,
        ["terminal"] = Verb.Terminal,
        ["shell"] = Verb.Terminal,
        ["config"] = Verb.Config,
        ["hidden"] = Verb.Hidden,
        ["help"] = Verb.Help,
        ["docs"] = Verb.Help,
    };

    /// <summary>Commands in help order, with their main keyword and their glyph.</summary>
    private static readonly (Verb Verb, string Keyword, GlyphInfo Glyph)[] Commands =
    [
        (Verb.Add, "add", Glyphs.Add),
        (Verb.Edit, "edit", Glyphs.Edit),
        (Verb.Remove, "remove", Glyphs.Remove),
        (Verb.Hosts, "hosts", Glyphs.Search),
        (Verb.Direct, "d", Glyphs.Host),
        (Verb.Terminal, "terminal", Glyphs.Terminal),
        (Verb.Config, "config", Glyphs.Document),
        (Verb.Hidden, "hidden", Glyphs.Hide),
    ];

    private enum PickMode
    {
        Connect,
        Edit,
        Remove,
    }

    private PluginData Data => services.Store.Data;

    /// <summary>Answers a query.</summary>
    public List<Result> Handle(string search, string actionKeyword)
    {
        var context = new QueryContext(search.Trim(), actionKeyword);
        var (word, rest) = SplitWord(context.Search);
        if (!Verbs.TryGetValue(word, out var verb))
        {
            return Search(context.Search, context, withShortcuts: true, withFallback: true);
        }

        return verb switch
        {
            Verb.Add => Add(rest, word, context),
            Verb.Edit => Pick(rest, context, PickMode.Edit),
            Verb.Remove => Pick(rest, context, PickMode.Remove),
            Verb.Hosts => Search(rest, context, withShortcuts: false, withFallback: false),
            Verb.Direct => Direct(rest, word, context),
            Verb.Terminal => Terminal(rest, context),
            Verb.Config => Config(rest),
            Verb.Hidden => Hidden(rest, context),
            _ => Help(context),
        };
    }

    /// <summary>Builds the context menu of a result.</summary>
    public List<Result> ContextMenu(Result selected) => selected.ContextData switch
    {
        HostItem item => HostMenu(item),
        CommandItem item => CommandMenu(item),
        DirectItem item => DirectMenu(item),
        KnownHostItem item => KnownHostMenu(item),
        _ => [],
    };

    // ------------------------------------------------------------------ search

    private List<Result> Search(string text, QueryContext context, bool withShortcuts, bool withFallback)
    {
        var data = Data;
        var snapshot = services.SshConfig.GetSnapshot();
        var query = SearchQuery.Parse(text.Replace('"', ' '));
        var results = new List<Result>();

        foreach (var host in snapshot.Hosts.Where(host => !data.IsHidden(host.Alias)).OrderBy(host => TextFolding.Fold(host.Alias), StringComparer.Ordinal))
        {
            if (MatchHost(query, host) is { } match)
            {
                results.Add(HostResult(host, snapshot, match, context, PickMode.Connect));
            }
        }

        foreach (var command in data.Commands.OrderBy(command => TextFolding.Fold(command.Name), StringComparer.Ordinal))
        {
            if (ProfileMatcher.Match(query, command.Name, command.Command) is { } match)
            {
                results.Add(CommandResult(command, match, context, PickMode.Connect));
            }
        }

        var found = results.Count;
        if (!query.IsEmpty && data.SuggestKnownHosts)
        {
            results.AddRange(KnownHostResults(query, snapshot, context));
        }

        if (query.IsEmpty && withShortcuts)
        {
            if (!services.OpenSshInstalled)
            {
                results.Add(OpenSshMissingResult());
            }

            if (IncludeMissingResult(snapshot) is { } includeMissing)
            {
                results.Add(includeMissing);
            }

            if (found == 0)
            {
                results.Add(EmptyStateResult(context));
            }

            results.AddRange(Shortcuts(context));
        }
        else if (!query.IsEmpty)
        {
            if (withShortcuts)
            {
                results.AddRange(CommandSuggestions(query.Text, context));
            }

            if (withFallback)
            {
                results.Add(DirectResult(query.Text, context, FallbackScore));
            }
            else if (found == 0)
            {
                results.Add(NoMatchResult(query.Text));
            }
        }
        else if (found == 0)
        {
            results.Add(EmptyStateResult(context));
        }

        return results;
    }

    private static MatchResult? MatchHost(SearchQuery query, SshConfigHost host) =>
        ProfileMatcher.Match(query, host.Alias, $"{host.Command} {host.Target} {host.ProxyJump}");

    private static int Score(MatchResult match) =>
        (ProfileMatcher.TierCount - match.Tier) * TierSpacing + (match.NameStartsWithQuery ? PrefixBonus : 0);

    private Result HostResult(SshConfigHost host, SshConfigSnapshot snapshot, MatchResult match, QueryContext context, PickMode mode)
    {
        var managed = services.IsManaged(host);
        var subTitle = HostSubtitle(host, snapshot);
        var (icon, glyph, completion, action) = mode switch
        {
            PickMode.Edit => (Icons.App, managed ? Glyphs.Edit : Glyphs.Document, "edit ", new Func<ActionContext, bool>(_ => EditHost(host, context.Prefix))),
            PickMode.Remove when managed => (Icons.Remove, Glyphs.Remove, "remove ", _ => RemoveHost(host, fromContextMenu: false)),
            PickMode.Remove => (Icons.App, Glyphs.Document, "remove ", _ => OpenSource(host)),
            _ => (Icons.App, Glyphs.Host, string.Empty, _ => services.Connect(host.Command, host.Alias)),
        };

        if (!managed && mode != PickMode.Connect)
        {
            subTitle = services.T("plugin_easyssh_not_managed", services.DisplayPath(host.SourceFile));
        }

        return new Result
        {
            Title = host.Alias,
            SubTitle = subTitle,
            TitleToolTip = host.Alias,
            SubTitleToolTip = services.DisplayPath(host.SourceFile),
            TitleHighlightData = match.NameHighlights.ToList(),
            Score = Score(match),
            IcoPath = icon,
            Glyph = glyph,
            CopyText = host.Command,
            AutoCompleteText = context.Prefix + completion + host.Alias,
            QuerySuggestionText = mode == PickMode.Connect ? host.Alias : null,
            // Only connections count as usage: editing or removing must not change the ranking.
            AddSelectedCount = mode == PickMode.Connect,
            RecordKey = "host:" + host.Alias.ToLowerInvariant(),
            ContextData = new HostItem(host, managed, context.Prefix),
            Action = action,
        };
    }

    /// <summary><c>admin@10.0.0.5:2222 · via bastion · ~/.ssh/config</c>, and a warning for duplicated aliases.</summary>
    private string HostSubtitle(SshConfigHost host, SshConfigSnapshot snapshot)
    {
        var parts = new List<string> { host.Target ?? host.Alias };
        if (host.ProxyJump is { } jump)
        {
            parts.Add(services.T("plugin_easyssh_host_via", jump));
        }

        parts.Add(services.DisplayPath(host.SourceFile));
        if (snapshot.Duplicates.TryGetValue(host.Alias, out var others) && others.Count > 0)
        {
            parts.Add(services.T("plugin_easyssh_host_duplicate", services.DisplayPath(others[0])));
        }

        return string.Join(" · ", parts);
    }

    private Result CommandResult(SavedCommand command, MatchResult match, QueryContext context, PickMode mode)
    {
        var (icon, glyph, completion, action) = mode switch
        {
            PickMode.Edit => (Icons.App, Glyphs.Edit, "edit ", new Func<ActionContext, bool>(_ => EditCommand(command, context.Prefix))),
            PickMode.Remove => (Icons.Remove, Glyphs.Remove, "remove ", _ => RemoveCommand(command, fromContextMenu: false)),
            _ => (Icons.App, Glyphs.Command, string.Empty, _ => services.Connect(command.Command, command.Name)),
        };

        return new Result
        {
            Title = command.Name,
            SubTitle = command.Command,
            TitleToolTip = command.Name,
            SubTitleToolTip = command.Command,
            TitleHighlightData = match.NameHighlights.ToList(),
            Score = Score(match),
            IcoPath = icon,
            Glyph = glyph,
            CopyText = command.Command,
            AutoCompleteText = context.Prefix + completion + command.Name,
            QuerySuggestionText = mode == PickMode.Connect ? command.Name : null,
            AddSelectedCount = mode == PickMode.Connect,
            RecordKey = "command:" + command.Name.ToLowerInvariant(),
            ContextData = new CommandItem(command, context.Prefix),
            Action = action,
        };
    }

    /// <summary>Servers of known_hosts that are not hosts of the configuration, as connection suggestions.</summary>
    private IEnumerable<Result> KnownHostResults(SearchQuery query, SshConfigSnapshot snapshot, QueryContext context)
    {
        var configured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in snapshot.Hosts)
        {
            configured.Add(host.Alias);
            if (host.HostName is not null)
            {
                configured.Add(host.HostName);
            }
        }

        var position = 0;
        foreach (var known in services.KnownHosts.Get())
        {
            if (configured.Contains(known.Host) || ProfileMatcher.Match(query, known.Display, known.Command) is not { } match)
            {
                continue;
            }

            yield return new Result
            {
                Title = known.Display,
                SubTitle = services.T("plugin_easyssh_known_host_subtitle", known.Command),
                TitleHighlightData = match.NameHighlights.ToList(),
                Score = KnownHostScore + (ProfileMatcher.TierCount - match.Tier) * 10 - position,
                IcoPath = Icons.App,
                Glyph = Glyphs.History,
                CopyText = known.Command,
                AutoCompleteText = context.Prefix + known.Host,
                AddSelectedCount = false,
                ContextData = new KnownHostItem(known, context.Prefix),
                Action = _ => services.Connect(known.Command, known.Host),
            };

            if (++position >= KnownHostLimit)
            {
                yield break;
            }
        }
    }

    private Result DirectResult(string text, QueryContext context, int score)
    {
        var command = SshCommand.Normalize(text);
        var title = SshCommand.GuessDestination(command) ?? command;
        return new Result
        {
            Title = services.T("plugin_easyssh_direct_title", title),
            SubTitle = services.T("plugin_easyssh_direct_subtitle", command, services.DisplayName(Data.GetSelectedTerminal())),
            SubTitleToolTip = command,
            Score = score,
            IcoPath = Icons.App,
            Glyph = Glyphs.Terminal,
            CopyText = command,
            AddSelectedCount = false,
            ContextData = new DirectItem(command, context.Prefix),
            Action = _ => services.Connect(command, title),
        };
    }

    private IEnumerable<Result> Shortcuts(QueryContext context)
    {
        yield return ChangeQueryResult(
            services.T("plugin_easyssh_cmd_add_title"), services.T("plugin_easyssh_cmd_add_subtitle"),
            context.Prefix + "add ", Glyphs.Add, ShortcutScore + 3, Icons.Add);
        yield return ChangeQueryResult(
            services.T("plugin_easyssh_shortcut_terminal_title", services.DisplayName(Data.GetSelectedTerminal())),
            services.T("plugin_easyssh_shortcut_terminal_subtitle"),
            context.Prefix + "terminal ", Glyphs.Terminal, ShortcutScore + 2);
        yield return ChangeQueryResult(
            services.T("plugin_easyssh_cmd_help_title"), services.T("plugin_easyssh_cmd_help_subtitle"),
            context.Prefix + "help", Glyphs.Help, ShortcutScore + 1);
    }

    /// <summary>Commands whose keyword starts with what was typed (e.g. "ter" suggests "terminal").</summary>
    private IEnumerable<Result> CommandSuggestions(string text, QueryContext context)
    {
        if (text.Contains(' '))
        {
            yield break;
        }

        foreach (var (verb, keyword, glyph) in Commands)
        {
            if (keyword.Length > text.Length && keyword.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            {
                yield return CommandHelpResult(verb, keyword, glyph, context, ShortcutScore);
            }
        }
    }

    // ------------------------------------------------------------------ add

    private List<Result> Add(string text, string typedWord, QueryContext context)
    {
        if (text.Length == 0)
        {
            var usage = ChangeQueryResult(
                services.T("plugin_easyssh_cmd_add_title"), services.T("plugin_easyssh_cmd_add_subtitle"),
                context.Prefix + "add ", Glyphs.Add, TopScore, Icons.Add);
            return [usage, .. Search(typedWord, context, withShortcuts: false, withFallback: false).Where(IsEntryResult)];
        }

        var (name, commandText) = CommandLine.SplitFirst(text);
        if (commandText.Length == 0)
        {
            return
            [
                new Result
                {
                    Title = services.T("plugin_easyssh_add_name_title", name),
                    SubTitle = services.T("plugin_easyssh_add_missing_command"),
                    IcoPath = Icons.Add,
                    Glyph = Glyphs.Add,
                    Score = TopScore,
                    AutoCompleteText = context.Prefix + "add " + CommandLine.Quote(name) + " ",
                },
            ];
        }

        var command = SshCommand.Normalize(commandText);
        var results = new List<Result> { AddResult(name, command, context) };
        if (!services.OpenSshInstalled && SshCommand.UsesOpenSsh(command))
        {
            results.Add(OpenSshMissingResult());
        }

        return results;
    }

    /// <summary>
    /// What <c>add name command</c> does: a host of <c>~/.ssh/easyssh.config</c> when ssh_config can express the
    /// command, otherwise a saved command. One name designates one entry: saving a host replaces the saved command
    /// of the same name. A host saved with EasySSH is never replaced by a command, and a host of the user's own
    /// files is never overridden.
    /// </summary>
    private Result AddResult(string name, string command, QueryContext context)
    {
        var snapshot = services.SshConfig.GetSnapshot();
        var alias = SshConfigText.IsValidAlias(name) ? name : SshConfigText.ToAlias(name);
        var conversion = alias is null ? null : SshCommandConverter.ToHost(alias, command);
        var existingHost = alias is null ? null : snapshot.Find(alias);
        var existingIsManaged = existingHost is not null && services.IsManaged(existingHost);
        var existingCommand = Data.FindCommand(name) ?? (alias is null ? null : Data.FindCommand(alias));
        var whyNotAHost = WhyNotAHost(alias, conversion, snapshot);

        if (whyNotAHost is null && existingHost is not null && !existingIsManaged)
        {
            if (existingCommand is null)
            {
                // Writing it in easyssh.config would silently take precedence over the user's own host.
                return new Result
                {
                    Title = services.T("plugin_easyssh_add_exists_title", existingHost.Alias),
                    SubTitle = services.T("plugin_easyssh_add_exists_subtitle", services.DisplayPath(existingHost.SourceFile)),
                    IcoPath = Icons.Warning,
                    Glyph = Glyphs.Warning,
                    Score = TopScore,
                    AddSelectedCount = false,
                    Action = _ => OpenSource(existingHost),
                };
            }

            // A saved command named like one of the user's hosts stays a command when it is edited.
            whyNotAHost = services.T("plugin_easyssh_why_exists", existingHost.Alias);
        }

        if (whyNotAHost is null && alias is not null && conversion?.Host is { } host)
        {
            var update = existingHost is not null || existingCommand is not null;
            var subTitle = services.T("plugin_easyssh_add_host_subtitle", DescribeHost(host), services.DisplayPath(services.Files.ManagedPath));
            if (host.Options.Count > 0 && alias.Equals(conversion.Destination, StringComparison.OrdinalIgnoreCase))
            {
                subTitle += " · " + services.T("plugin_easyssh_add_host_global", alias);
            }

            return new Result
            {
                Title = services.T(update ? "plugin_easyssh_update_host_title" : "plugin_easyssh_add_host_title", alias),
                SubTitle = subTitle,
                SubTitleToolTip = string.Join("\n", ManagedConfig.Render(host)),
                IcoPath = Icons.Add,
                Glyph = update ? Glyphs.Save : Glyphs.Add,
                Score = TopScore,
                AddSelectedCount = false,
                Action = _ =>
                {
                    if (services.TrySaveHost(host, existingHost?.Alias))
                    {
                        if (existingCommand is not null)
                        {
                            services.TryUpdate(data => data.RemoveCommand(existingCommand.Name));
                        }

                        services.Api.ChangeQuery(context.Prefix + alias, requery: true);
                    }

                    return false;
                },
            };
        }

        if (existingIsManaged)
        {
            // Replacing it by a command would delete a host that other programs may use: explain instead.
            return new Result
            {
                Title = services.T("plugin_easyssh_add_managed_conflict_title", existingHost!.Alias),
                SubTitle = services.T("plugin_easyssh_add_managed_conflict_subtitle", whyNotAHost),
                SubTitleToolTip = command,
                IcoPath = Icons.Warning,
                Glyph = Glyphs.Warning,
                Score = TopScore,
                AddSelectedCount = false,
            };
        }

        return new Result
        {
            Title = services.T(existingCommand is not null ? "plugin_easyssh_update_command_title" : "plugin_easyssh_add_command_title", name),
            SubTitle = services.T("plugin_easyssh_add_command_subtitle", command, whyNotAHost),
            SubTitleToolTip = command,
            IcoPath = Icons.Add,
            Glyph = existingCommand is not null ? Glyphs.Save : Glyphs.Add,
            Score = TopScore,
            AddSelectedCount = false,
            Action = _ =>
            {
                if (services.TryUpdate(data => data.UpsertCommand(new SavedCommand(name, command), existingCommand?.Name)))
                {
                    services.Api.ChangeQuery(context.Prefix + name, requery: true);
                }

                return false;
            },
        };
    }

    /// <summary>
    /// Why the command can't become the host <paramref name="alias"/>, or null when it can: ssh_config must express
    /// it, and <c>ssh alias</c> must then do what the command does.
    /// </summary>
    private string? WhyNotAHost(string? alias, HostConversion? conversion, SshConfigSnapshot snapshot)
    {
        if (alias is null || conversion is null)
        {
            return services.T("plugin_easyssh_why_name");
        }

        if (!conversion.Succeeded || conversion.Destination is not { } destination)
        {
            var unsupported = conversion.Unsupported ?? string.Empty;
            return unsupported.StartsWith('-')
                ? services.T("plugin_easyssh_why_option", unsupported)
                : services.T("plugin_easyssh_why_program", unsupported);
        }

        // "HostName other" would not use the options of the host "other".
        if (snapshot.Find(destination) is { } other && !other.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase))
        {
            return services.T("plugin_easyssh_why_alias", destination);
        }

        // ssh matches the Host patterns against the name typed: "web" doesn't get the options of "Host *.corp".
        return snapshot.HaveSameRules(alias, destination) ? null : services.T("plugin_easyssh_why_rules", destination, alias);
    }

    /// <summary><c>admin@10.0.0.5:2222 via bastion</c>.</summary>
    private string DescribeHost(SshHostEntry host)
    {
        var target = host.HostName ?? host.Alias;
        if (host.User is { } user)
        {
            target = user + "@" + target;
        }

        if (host.Port is { } port)
        {
            target += ":" + port;
        }

        return host.ProxyJump is { } jump ? target + " " + services.T("plugin_easyssh_host_via", jump) : target;
    }

    // ------------------------------------------------------------------ edit / remove

    private List<Result> Pick(string text, QueryContext context, PickMode mode)
    {
        var data = Data;
        var snapshot = services.SshConfig.GetSnapshot();
        if (snapshot.Hosts.Count == 0 && data.Commands.Count == 0)
        {
            return [EmptyStateResult(context)];
        }

        var query = SearchQuery.Parse(text.Replace('"', ' '));
        var results = new List<Result>();
        foreach (var host in snapshot.Hosts.OrderBy(host => TextFolding.Fold(host.Alias), StringComparer.Ordinal))
        {
            if (MatchHost(query, host) is { } match)
            {
                var result = HostResult(host, snapshot, match, context, mode);
                if (!services.IsManaged(host))
                {
                    // Hosts EasySSH can change come first.
                    result.Score -= PrefixBonus / 2;
                }

                results.Add(result);
            }
        }

        foreach (var command in data.Commands.OrderBy(command => TextFolding.Fold(command.Name), StringComparer.Ordinal))
        {
            if (ProfileMatcher.Match(query, command.Name, command.Command) is { } match)
            {
                results.Add(CommandResult(command, match, context, mode));
            }
        }

        if (results.Count == 0)
        {
            results.Add(NoMatchResult(query.Text));
        }

        return results;
    }

    private bool EditHost(SshConfigHost host, string prefix)
    {
        if (!services.IsManaged(host))
        {
            return OpenSource(host);
        }

        SshHostEntry? entry;
        try
        {
            entry = services.Files.ReadManaged().Find(host.Alias);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            services.Log("Could not read the managed SSH configuration", e);
            services.Api.ShowMsgError(services.T("plugin_easyssh_file_missing_title"), e.Message);
            return false;
        }

        if (entry is null)
        {
            // Declared with other aliases on the same Host line: only a text editor can change it.
            return OpenSource(host);
        }

        services.Api.ChangeQuery($"{prefix}add {entry.Alias} {SshCommandConverter.ToArguments(entry)}");
        return false;
    }

    private bool EditCommand(SavedCommand command, string prefix)
    {
        services.Api.ChangeQuery($"{prefix}add {CommandLine.Quote(command.Name)} {command.Command}");
        return false;
    }

    private bool RemoveHost(SshConfigHost host, bool fromContextMenu)
    {
        if (!Confirm(services.T("plugin_easyssh_remove_host_confirm", host.Alias, services.DisplayPath(host.SourceFile)))
            || !services.TryRemoveHost(host.Alias))
        {
            return false;
        }

        return AfterRemoval(fromContextMenu);
    }

    private bool RemoveCommand(SavedCommand command, bool fromContextMenu)
    {
        if (!Confirm(services.T("plugin_easyssh_remove_command_confirm", command.Name, command.Command))
            || !services.TryUpdate(data => data.RemoveCommand(command.Name)))
        {
            return false;
        }

        return AfterRemoval(fromContextMenu);
    }

    private bool AfterRemoval(bool fromContextMenu)
    {
        if (fromContextMenu)
        {
            services.Api.BackToQueryResults();
        }

        services.Api.ReQuery();
        return false;
    }

    private bool Confirm(string message) =>
        services.Api.ShowMsgBox(
            message,
            services.T("plugin_easyssh_plugin_name"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    private bool OpenSource(SshConfigHost host)
    {
        services.OpenFile(host.SourceFile);
        return true;
    }

    // ------------------------------------------------------------------ direct

    private List<Result> Direct(string text, string typedWord, QueryContext context)
    {
        if (text.Length == 0)
        {
            var usage = InfoResult(
                services.T("plugin_easyssh_cmd_direct_title"), services.T("plugin_easyssh_cmd_direct_subtitle"),
                Glyphs.Host, context.Prefix + "d ");
            return [usage, .. Search(typedWord, context, withShortcuts: false, withFallback: false).Where(IsEntryResult)];
        }

        var results = new List<Result> { DirectResult(text, context, TopScore) };
        if (!services.OpenSshInstalled && SshCommand.UsesOpenSsh(SshCommand.Normalize(text)))
        {
            results.Add(OpenSshMissingResult());
        }

        return results;
    }

    // ------------------------------------------------------------------ terminals

    private List<Result> Terminal(string text, QueryContext context)
    {
        var (word, rest) = SplitWord(text);
        if (word.Equals("add", StringComparison.OrdinalIgnoreCase))
        {
            return AddTerminal(rest, context);
        }

        if (word.Equals("remove", StringComparison.OrdinalIgnoreCase) || word.Equals("rm", StringComparison.OrdinalIgnoreCase))
        {
            return RemoveTerminal(rest, context);
        }

        var data = Data;
        var selected = data.GetSelectedTerminal();
        var filter = TextFolding.Fold(text);
        var results = new List<Result>();
        var position = 0;
        foreach (var terminal in data.AllTerminals())
        {
            var displayName = services.DisplayName(terminal);
            if (filter.Length > 0
                && !TextFolding.Fold(displayName).Contains(filter, StringComparison.Ordinal)
                && !TextFolding.Fold(terminal.Name).Contains(filter, StringComparison.Ordinal))
            {
                continue;
            }

            var isSelected = terminal.Name == selected.Name;
            var available = PluginServices.IsAvailable(terminal);
            var description = available ? Describe(terminal) : services.T("plugin_easyssh_terminal_not_found");
            results.Add(new Result
            {
                Title = displayName,
                SubTitle = isSelected ? services.T("plugin_easyssh_terminal_selected", description) : description,
                IcoPath = isSelected ? Icons.Selected : available ? Icons.App : Icons.Warning,
                Glyph = isSelected ? Glyphs.Selected : Glyphs.Terminal,
                // Selected first, then installed terminals, then missing ones; shortcuts come after all of them.
                Score = isSelected ? TopScore : (available ? TierSpacing : ShortcutScore * 10) - position++,
                AddSelectedCount = false,
                Action = _ =>
                {
                    if (!available)
                    {
                        services.Api.ShowMsgError(services.T("plugin_easyssh_launch_error_title"), services.T("plugin_easyssh_terminal_missing", displayName, terminal.Executable));
                        return false;
                    }

                    if (services.TryUpdate(current => current.SelectTerminal(terminal.Name)))
                    {
                        services.Api.ReQuery();
                    }

                    return false;
                },
            });
        }

        if (filter.Length == 0)
        {
            results.Add(ChangeQueryResult(
                services.T("plugin_easyssh_terminal_add_usage_title"), services.T("plugin_easyssh_terminal_add_usage_subtitle"),
                context.Prefix + "terminal add ", Glyphs.Add, ShortcutScore + 2, Icons.Add));
            if (data.Terminals.Count > 0)
            {
                results.Add(ChangeQueryResult(
                    services.T("plugin_easyssh_terminal_remove_usage_title"), services.T("plugin_easyssh_terminal_remove_usage_subtitle"),
                    context.Prefix + "terminal remove ", Glyphs.Remove, ShortcutScore + 1));
            }

            results.Add(SettingsResult(ShortcutScore));
        }
        else if (results.Count == 0)
        {
            results.Add(InfoResult(services.T("plugin_easyssh_terminal_no_match_title", text), services.T("plugin_easyssh_terminal_add_usage_subtitle")));
        }

        return results;
    }

    private List<Result> AddTerminal(string text, QueryContext context)
    {
        if (text.Length == 0)
        {
            return
            [
                InfoResult(
                    services.T("plugin_easyssh_terminal_add_usage_title"), services.T("plugin_easyssh_terminal_add_usage_subtitle"),
                    Glyphs.Add, context.Prefix + "terminal add "),
            ];
        }

        // "terminal add <program>" (EasySSH 1.2 syntax) or "terminal add <name> <program> [arguments]".
        var (first, afterFirst) = CommandLine.SplitFirst(text);
        TerminalDefinition terminal;
        if (afterFirst.Length == 0)
        {
            var name = Path.GetFileName(first);
            terminal = new TerminalDefinition(name.Length > 0 ? name : first, first, string.Empty);
        }
        else
        {
            var (program, arguments) = CommandLine.SplitFirst(afterFirst);
            terminal = new TerminalDefinition(first, program, arguments);
        }

        if (BuiltInTerminals.IsReservedName(terminal.Name))
        {
            return [InfoResult(services.T("plugin_easyssh_terminal_add_title", terminal.Name), services.T("plugin_easyssh_terminal_reserved", terminal.Name))];
        }

        var existing = Data.Terminals.FirstOrDefault(candidate => PluginDataOperations.NameComparer.Equals(candidate.Name, terminal.Name));
        var available = PluginServices.IsAvailable(terminal);
        var description = Describe(terminal);
        return
        [
            new Result
            {
                Title = services.T(existing is null ? "plugin_easyssh_terminal_add_title" : "plugin_easyssh_terminal_update_title", terminal.Name),
                SubTitle = available ? description : services.T("plugin_easyssh_terminal_add_not_found", description),
                IcoPath = available ? Icons.Add : Icons.Warning,
                Glyph = Glyphs.Add,
                Score = TopScore,
                AddSelectedCount = false,
                Action = _ =>
                {
                    if (services.TryUpdate(data => data.UpsertTerminal(terminal, existing?.Name).SelectTerminal(terminal.Name)))
                    {
                        services.Api.ChangeQuery(context.Prefix + "terminal", requery: true);
                    }

                    return false;
                },
            },
        ];
    }

    private List<Result> RemoveTerminal(string text, QueryContext context)
    {
        var data = Data;
        if (data.Terminals.Count == 0)
        {
            return [InfoResult(services.T("plugin_easyssh_terminal_none_title"), services.T("plugin_easyssh_terminal_add_usage_subtitle"))];
        }

        var filter = TextFolding.Fold(text);
        var position = 0;
        var matching = data.Terminals
            .Where(terminal => filter.Length == 0 || TextFolding.Fold(terminal.Name).Contains(filter, StringComparison.Ordinal))
            .ToList();
        if (matching.Count == 0)
        {
            return [InfoResult(services.T("plugin_easyssh_terminal_no_match_title", text), services.T("plugin_easyssh_terminal_remove_usage_subtitle"))];
        }

        return matching
            .Select(terminal => new Result
            {
                Title = terminal.Name,
                SubTitle = Describe(terminal),
                IcoPath = Icons.Remove,
                Glyph = Glyphs.Remove,
                Score = TopScore - position++,
                AddSelectedCount = false,
                Action = _ =>
                {
                    if (services.TryUpdate(current => current.RemoveTerminal(terminal.Name)))
                    {
                        services.Api.ChangeQuery(context.Prefix + "terminal", requery: true);
                    }

                    return false;
                },
            })
            .ToList();
    }

    private static string Describe(TerminalDefinition terminal) =>
        terminal.IsBuiltIn || terminal.Arguments.Length == 0 ? terminal.Executable : terminal.Executable + " " + terminal.Arguments;

    // ------------------------------------------------------------------ ssh configuration files

    private List<Result> Config(string text)
    {
        var snapshot = services.SshConfig.GetSnapshot();
        var files = new List<string> { services.Files.MainConfigPath };
        files.AddRange(snapshot.Files.Where(file => !files.Contains(file, StringComparer.OrdinalIgnoreCase)));
        if (!files.Contains(services.Files.ManagedPath, StringComparer.OrdinalIgnoreCase))
        {
            files.Add(services.Files.ManagedPath);
        }

        var filter = TextFolding.Fold(text);
        var results = new List<Result>();
        var score = TopScore;
        foreach (var file in files)
        {
            var display = services.DisplayPath(file);
            if (filter.Length > 0 && !TextFolding.Fold(display).Contains(filter, StringComparison.Ordinal))
            {
                continue;
            }

            var hostCount = snapshot.Hosts.Count(host => string.Equals(host.SourceFile, file, StringComparison.OrdinalIgnoreCase));
            var isManaged = services.Files.IsManaged(file);
            var exists = File.Exists(file);
            results.Add(new Result
            {
                Title = display,
                SubTitle = !exists
                    ? services.T(isManaged ? "plugin_easyssh_config_managed_missing" : "plugin_easyssh_config_file_missing")
                    : services.T(isManaged ? "plugin_easyssh_config_managed_subtitle" : "plugin_easyssh_config_file_subtitle", hostCount),
                SubTitleToolTip = file,
                IcoPath = Icons.App,
                Glyph = Glyphs.Document,
                Score = score--,
                AddSelectedCount = false,
                Action = exists
                    ? _ =>
                    {
                        services.OpenFile(file);
                        return true;
                    }
                    : null,
            });
        }

        if (IncludeMissingResult(snapshot) is { } includeMissing)
        {
            results.Insert(0, includeMissing);
        }

        if (results.Count == 0)
        {
            results.Add(NoMatchResult(text));
        }

        return results;
    }

    /// <summary>
    /// When easyssh.config exists but <c>~/.ssh/config</c> doesn't include it (the line was removed), its hosts
    /// are invisible to ssh: offer to add the line again.
    /// </summary>
    private Result? IncludeMissingResult(SshConfigSnapshot snapshot)
    {
        var managed = services.Files.ManagedPath;
        if (!File.Exists(managed) || snapshot.Files.Contains(managed, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return new Result
        {
            Title = services.T("plugin_easyssh_include_missing_title"),
            SubTitle = services.T("plugin_easyssh_include_missing_subtitle", services.DisplayPath(services.Files.MainConfigPath), SshConfigFiles.IncludeLine),
            IcoPath = Icons.Warning,
            Glyph = Glyphs.Warning,
            Score = WarningScore - 1,
            AddSelectedCount = false,
            Action = _ =>
            {
                if (services.TryEnsureIncluded())
                {
                    services.Api.ReQuery();
                }

                return false;
            },
        };
    }

    // ------------------------------------------------------------------ hidden hosts

    private List<Result> Hidden(string text, QueryContext context)
    {
        var data = Data;
        if (data.HiddenHosts.Count == 0)
        {
            return [InfoResult(services.T("plugin_easyssh_hidden_none_title"), services.T("plugin_easyssh_hidden_none_subtitle"), Glyphs.Hide)];
        }

        var filter = TextFolding.Fold(text);
        var position = 0;
        var results = data.HiddenHosts
            .Where(alias => filter.Length == 0 || TextFolding.Fold(alias).Contains(filter, StringComparison.Ordinal))
            .OrderBy(alias => TextFolding.Fold(alias), StringComparer.Ordinal)
            .Select(alias => new Result
            {
                Title = alias,
                SubTitle = services.T("plugin_easyssh_hidden_subtitle"),
                IcoPath = Icons.App,
                Glyph = Glyphs.Show,
                Score = TopScore - position++,
                AddSelectedCount = false,
                AutoCompleteText = context.Prefix + "hidden " + alias,
                Action = _ =>
                {
                    if (services.TryUpdate(current => current.SetHidden(alias, hidden: false)))
                    {
                        services.Api.ReQuery();
                    }

                    return false;
                },
            })
            .ToList();
        if (results.Count == 0)
        {
            results.Add(NoMatchResult(text));
        }

        return results;
    }

    // ------------------------------------------------------------------ help

    private List<Result> Help(QueryContext context)
    {
        var results = new List<Result>();
        var score = TopScore;
        foreach (var (verb, keyword, glyph) in Commands)
        {
            results.Add(CommandHelpResult(verb, keyword, glyph, context, score--));
        }

        results.Add(new Result
        {
            Title = services.T("plugin_easyssh_docs_title"),
            SubTitle = PluginServices.DocumentationUrl,
            IcoPath = Icons.App,
            Glyph = Glyphs.Link,
            Score = score--,
            AddSelectedCount = false,
            Action = _ =>
            {
                services.Api.OpenUrl(PluginServices.DocumentationUrl);
                return true;
            },
        });
        results.Add(SettingsResult(score));
        return results;
    }

    private Result CommandHelpResult(Verb verb, string keyword, GlyphInfo glyph, QueryContext context, int score)
    {
        var key = verb.ToString().ToLowerInvariant();
        var result = ChangeQueryResult(
            services.T($"plugin_easyssh_cmd_{key}_title"),
            services.T($"plugin_easyssh_cmd_{key}_subtitle"),
            context.Prefix + keyword + " ",
            glyph,
            score);
        result.QuerySuggestionText = keyword + " ";
        return result;
    }

    // ------------------------------------------------------------------ context menus

    private List<Result> HostMenu(HostItem item)
    {
        var host = item.Host;
        var results = new List<Result>
        {
            CopyResult(host.Command),
            new()
            {
                Title = services.T("plugin_easyssh_ctx_sftp"),
                SubTitle = "sftp " + CommandLine.Quote(host.Alias),
                IcoPath = Icons.App,
                Glyph = Glyphs.Folder,
                Action = _ => services.Connect("sftp " + CommandLine.Quote(host.Alias), host.Alias),
            },
            new()
            {
                Title = services.T("plugin_easyssh_ctx_edit"),
                SubTitle = item.IsManaged ? services.T("plugin_easyssh_ctx_edit_subtitle") : services.T("plugin_easyssh_not_managed", services.DisplayPath(host.SourceFile)),
                IcoPath = Icons.App,
                Glyph = Glyphs.Edit,
                Action = _ =>
                {
                    if (item.IsManaged)
                    {
                        services.Api.BackToQueryResults();
                    }

                    return EditHost(host, item.Prefix);
                },
            },
        };

        if (item.IsManaged)
        {
            results.Add(new Result
            {
                Title = services.T("plugin_easyssh_ctx_remove"),
                SubTitle = services.DisplayPath(host.SourceFile),
                IcoPath = Icons.Remove,
                Glyph = Glyphs.Remove,
                Action = _ => RemoveHost(host, fromContextMenu: true),
            });
        }

        results.Add(new Result
        {
            Title = services.T("plugin_easyssh_ctx_hide"),
            SubTitle = services.T("plugin_easyssh_ctx_hide_subtitle"),
            IcoPath = Icons.App,
            Glyph = Glyphs.Hide,
            Action = _ =>
            {
                if (services.TryUpdate(data => data.SetHidden(host.Alias, hidden: true)))
                {
                    services.Api.BackToQueryResults();
                    services.Api.ReQuery();
                }

                return false;
            },
        });
        results.Add(new Result
        {
            Title = services.T("plugin_easyssh_ctx_open_file"),
            SubTitle = services.DisplayPath(host.SourceFile),
            IcoPath = Icons.App,
            Glyph = Glyphs.Document,
            Action = _ => OpenSource(host),
        });
        results.AddRange(OpenInOtherTerminals(host.Command, host.Alias));
        return results;
    }

    private List<Result> CommandMenu(CommandItem item)
    {
        var command = item.Command;
        var results = new List<Result>
        {
            CopyResult(command.Command),
            new()
            {
                Title = services.T("plugin_easyssh_ctx_edit"),
                SubTitle = services.T("plugin_easyssh_ctx_edit_subtitle"),
                IcoPath = Icons.App,
                Glyph = Glyphs.Edit,
                Action = _ =>
                {
                    services.Api.BackToQueryResults();
                    return EditCommand(command, item.Prefix);
                },
            },
            new()
            {
                Title = services.T("plugin_easyssh_ctx_remove"),
                SubTitle = command.Command,
                IcoPath = Icons.Remove,
                Glyph = Glyphs.Remove,
                Action = _ => RemoveCommand(command, fromContextMenu: true),
            },
        };
        results.AddRange(OpenInOtherTerminals(command.Command, command.Name));
        return results;
    }

    private List<Result> DirectMenu(DirectItem item)
    {
        var destination = SshCommand.GuessDestination(item.Command);
        var name = SshConfigText.ToAlias(destination) ?? "name";
        var results = new List<Result>
        {
            CopyResult(item.Command),
            SaveResult(name, item.Command, item.Prefix),
        };
        results.AddRange(OpenInOtherTerminals(item.Command, destination ?? name));
        return results;
    }

    private List<Result> KnownHostMenu(KnownHostItem item)
    {
        var known = item.Host;
        var results = new List<Result>
        {
            CopyResult(known.Command),
            SaveResult(SshConfigText.ToAlias(known.Host) ?? "name", known.Command, item.Prefix),
        };
        results.AddRange(OpenInOtherTerminals(known.Command, known.Host));
        return results;
    }

    private Result SaveResult(string name, string command, string prefix) => new()
    {
        Title = services.T("plugin_easyssh_ctx_save"),
        SubTitle = services.T("plugin_easyssh_ctx_save_subtitle", name),
        IcoPath = Icons.Add,
        Glyph = Glyphs.Save,
        Action = _ =>
        {
            services.Api.BackToQueryResults();
            services.Api.ChangeQuery($"{prefix}add {name} {command}");
            return false;
        },
    };

    private IEnumerable<Result> OpenInOtherTerminals(string command, string title)
    {
        var data = Data;
        var selected = data.GetSelectedTerminal();
        foreach (var terminal in data.AllTerminals())
        {
            if (terminal.Name == selected.Name || !PluginServices.IsAvailable(terminal))
            {
                continue;
            }

            yield return new Result
            {
                Title = services.T("plugin_easyssh_ctx_open_in", services.DisplayName(terminal)),
                SubTitle = command,
                IcoPath = Icons.App,
                Glyph = Glyphs.Terminal,
                Action = _ => services.Connect(command, title, terminal),
            };
        }
    }

    private Result CopyResult(string command) => new()
    {
        Title = services.T("plugin_easyssh_ctx_copy"),
        SubTitle = command,
        IcoPath = Icons.App,
        Glyph = Glyphs.Copy,
        Action = _ =>
        {
            services.Api.CopyToClipboard(command);
            return true;
        },
    };

    // ------------------------------------------------------------------ shared results

    private Result EmptyStateResult(QueryContext context) => ChangeQueryResult(
        services.T("plugin_easyssh_empty_title"), services.T("plugin_easyssh_empty_subtitle"),
        context.Prefix + "add ", Glyphs.Add, TopScore, Icons.Add);

    private Result NoMatchResult(string text) =>
        InfoResult(services.T("plugin_easyssh_no_match_title", text), services.T("plugin_easyssh_no_match_subtitle"));

    private Result OpenSshMissingResult() => new()
    {
        Title = services.T("plugin_easyssh_ssh_missing_title"),
        SubTitle = services.T("plugin_easyssh_ssh_missing_subtitle"),
        IcoPath = Icons.Warning,
        Glyph = Glyphs.Warning,
        Score = WarningScore,
        AddSelectedCount = false,
        Action = _ =>
        {
            services.Api.OpenAppUri("ms-settings:optionalfeatures");
            return true;
        },
    };

    private Result SettingsResult(int score) => new()
    {
        Title = services.T("plugin_easyssh_settings_title"),
        SubTitle = services.T("plugin_easyssh_settings_subtitle"),
        IcoPath = Icons.App,
        Glyph = Glyphs.Settings,
        Score = score,
        AddSelectedCount = false,
        Action = _ =>
        {
            services.Api.OpenSettingDialog();
            return true;
        },
    };

    private Result ChangeQueryResult(string title, string subTitle, string query, GlyphInfo glyph, int score, string icon = Icons.App) => new()
    {
        Title = title,
        SubTitle = subTitle,
        IcoPath = icon,
        Glyph = glyph,
        Score = score,
        AddSelectedCount = false,
        AutoCompleteText = query,
        Action = _ =>
        {
            services.Api.ChangeQuery(query);
            return false;
        },
    };

    /// <summary>A result without action, giving information or usage.</summary>
    private static Result InfoResult(string title, string subTitle, GlyphInfo? glyph = null, string? autoComplete = null) => new()
    {
        Title = title,
        SubTitle = subTitle,
        IcoPath = Icons.App,
        Glyph = glyph ?? Glyphs.Help,
        Score = TopScore,
        AddSelectedCount = false,
        AutoCompleteText = autoComplete,
    };

    /// <summary>Keeps only hosts and commands, not hints.</summary>
    private static bool IsEntryResult(Result result) => result.ContextData is HostItem or CommandItem;

    private static (string Word, string Remainder) SplitWord(string text)
    {
        var trimmed = text.TrimStart();
        var end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]))
        {
            end++;
        }

        return (trimmed[..end], trimmed[end..].Trim());
    }
}
