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
    Profiles,
    Direct,
    Terminal,
    Config,
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

/// <summary>Context data of a saved profile result.</summary>
internal sealed record ProfileItem(SshProfile Profile, string Prefix);

/// <summary>Context data of a <c>~/.ssh/config</c> host result.</summary>
internal sealed record HostItem(SshConfigHost Host, string Prefix);

/// <summary>Context data of a one-time connection result.</summary>
internal sealed record DirectItem(string Command, string Prefix);

/// <summary>Turns queries into results.</summary>
internal sealed class QueryHandler(PluginServices services)
{
    // Higher scores come first. Flow Launcher adds the number of times a result was chosen, so the gaps
    // are wide: usage reorders results of the same tier but never moves a result to another tier.
    private const int TierSpacing = 1_000_000;
    private const int PrefixBonus = 100_000;
    private const int ProfileBonus = 10_000;
    private const int TopScore = 50_000_000;
    private const int WarningScore = 60_000_000;
    private const int ShortcutScore = 100;
    private const int FallbackScore = 0;

    private static readonly Dictionary<string, Verb> Verbs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["add"] = Verb.Add,
        ["edit"] = Verb.Edit,
        ["remove"] = Verb.Remove,
        ["rm"] = Verb.Remove,
        ["profiles"] = Verb.Profiles,
        ["p"] = Verb.Profiles,
        ["d"] = Verb.Direct,
        ["direct"] = Verb.Direct,
        ["terminal"] = Verb.Terminal,
        ["shell"] = Verb.Terminal,
        ["config"] = Verb.Config,
        ["help"] = Verb.Help,
        ["docs"] = Verb.Help,
    };

    /// <summary>Commands in help order, with their main keyword and their glyph.</summary>
    private static readonly (Verb Verb, string Keyword, GlyphInfo Glyph)[] Commands =
    [
        (Verb.Add, "add", Glyphs.Add),
        (Verb.Edit, "edit", Glyphs.Edit),
        (Verb.Remove, "remove", Glyphs.Remove),
        (Verb.Profiles, "profiles", Glyphs.Search),
        (Verb.Direct, "d", Glyphs.Host),
        (Verb.Terminal, "terminal", Glyphs.Terminal),
        (Verb.Config, "config", Glyphs.Document),
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
            Verb.Edit => PickProfile(rest, context, PickMode.Edit),
            Verb.Remove => PickProfile(rest, context, PickMode.Remove),
            Verb.Profiles => Search(rest, context, withShortcuts: false, withFallback: false),
            Verb.Direct => Direct(rest, word, context),
            Verb.Terminal => Terminal(rest, context),
            Verb.Config => Config(rest, context),
            _ => Help(context),
        };
    }

    /// <summary>Builds the context menu of a result.</summary>
    public List<Result> ContextMenu(Result selected) => selected.ContextData switch
    {
        ProfileItem item => ProfileMenu(item),
        HostItem item => HostMenu(item),
        DirectItem item => DirectMenu(item),
        _ => [],
    };

    // ------------------------------------------------------------------ search

    private List<Result> Search(string text, QueryContext context, bool withShortcuts, bool withFallback)
    {
        var data = Data;
        var query = SearchQuery.Parse(text.Replace('"', ' '));
        var results = new List<Result>();

        foreach (var profile in data.Profiles.OrderBy(profile => TextFolding.Fold(profile.Name), StringComparer.Ordinal))
        {
            if (ProfileMatcher.Match(query, profile.Name, profile.Command) is { } match)
            {
                results.Add(ProfileResult(profile, match, context, PickMode.Connect));
            }
        }

        foreach (var host in VisibleHosts(data))
        {
            if (ProfileMatcher.Match(query, host.Alias, host.Command + " " + host.Target) is { } match)
            {
                results.Add(HostResult(host, match, context));
            }
        }

        var found = results.Count;
        if (query.IsEmpty && withShortcuts)
        {
            if (!services.OpenSshInstalled)
            {
                results.Add(OpenSshMissingResult());
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
                results.Add(InfoResult(services.T("plugin_easyssh_no_match_title", query.Text), services.T("plugin_easyssh_no_match_subtitle")));
            }
        }
        else if (found == 0)
        {
            results.Add(EmptyStateResult(context));
        }

        return results;
    }

    /// <summary>Hosts of <c>~/.ssh/config</c> that are not already saved as a profile, sorted by alias.</summary>
    private IEnumerable<SshConfigHost> VisibleHosts(PluginData data)
    {
        if (!data.ShowSshConfigHosts)
        {
            return [];
        }

        var savedCommands = data.Profiles.Select(profile => profile.Command).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return services.SshConfig.GetHosts()
            .Where(host => !savedCommands.Contains(host.Command))
            .OrderBy(host => TextFolding.Fold(host.Alias), StringComparer.Ordinal);
    }

    private static int Score(MatchResult match, bool isProfile) =>
        (ProfileMatcher.TierCount - match.Tier) * TierSpacing
        + (match.NameStartsWithQuery ? PrefixBonus : 0)
        + (isProfile ? ProfileBonus : 0);

    private Result ProfileResult(SshProfile profile, MatchResult match, QueryContext context, PickMode mode)
    {
        var (icon, glyph, action) = mode switch
        {
            PickMode.Edit => (Icons.App, Glyphs.Edit, new Func<ActionContext, bool>(_ => EditProfile(profile, context.Prefix))),
            PickMode.Remove => (Icons.Negative, Glyphs.Remove, _ => RemoveProfile(profile, fromContextMenu: false)),
            _ => (Icons.Positive, Glyphs.Host, _ => services.Connect(profile.Command, profile.Name)),
        };

        return new Result
        {
            Title = profile.Name,
            SubTitle = profile.Command,
            TitleToolTip = profile.Name,
            SubTitleToolTip = profile.Command,
            TitleHighlightData = match.NameHighlights.ToList(),
            Score = Score(match, isProfile: true),
            IcoPath = icon,
            Glyph = glyph,
            CopyText = profile.Command,
            AutoCompleteText = context.Prefix + profile.Name,
            QuerySuggestionText = mode == PickMode.Connect ? profile.Name : null,
            // Only connections count as usage: editing or removing must not change the ranking.
            AddSelectedCount = mode == PickMode.Connect,
            RecordKey = "profile:" + profile.Name.ToLowerInvariant(),
            ContextData = new ProfileItem(profile, context.Prefix),
            Action = action,
        };
    }

    private Result HostResult(SshConfigHost host, MatchResult match, QueryContext context) => new()
    {
        Title = host.Alias,
        SubTitle = services.T("plugin_easyssh_config_host_subtitle", host.Target ?? host.Command),
        TitleToolTip = host.Alias,
        SubTitleToolTip = host.SourceFile,
        TitleHighlightData = match.NameHighlights.ToList(),
        Score = Score(match, isProfile: false),
        IcoPath = Icons.App,
        Glyph = Glyphs.Host,
        CopyText = host.Command,
        AutoCompleteText = context.Prefix + host.Alias,
        QuerySuggestionText = host.Alias,
        RecordKey = "config:" + host.Alias.ToLowerInvariant(),
        ContextData = new HostItem(host, context.Prefix),
        Action = _ => services.Connect(host.Command, host.Alias),
    };

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
            context.Prefix + "add ", Glyphs.Add, ShortcutScore + 3);
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
                yield return CommandResult(verb, keyword, glyph, context, ShortcutScore);
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
                context.Prefix + "add ", Glyphs.Add, TopScore);
            return [usage, .. Search(typedWord, context, withShortcuts: false, withFallback: false).Where(IsMatchResult)];
        }

        var (name, commandText) = CommandLine.SplitFirst(text);
        if (commandText.Length == 0)
        {
            return
            [
                new Result
                {
                    Title = services.T("plugin_easyssh_add_title", name),
                    SubTitle = services.T("plugin_easyssh_add_missing_command"),
                    IcoPath = Icons.App,
                    Glyph = Glyphs.Add,
                    Score = TopScore,
                    AutoCompleteText = context.Prefix + "add " + CommandLine.Quote(name) + " ",
                },
            ];
        }

        var command = SshCommand.Normalize(commandText);
        var existing = Data.FindProfile(name);
        var results = new List<Result>
        {
            new()
            {
                Title = services.T(existing is null ? "plugin_easyssh_add_title" : "plugin_easyssh_update_title", name),
                SubTitle = command,
                SubTitleToolTip = existing is null ? command : services.T("plugin_easyssh_update_previous", existing.Command),
                IcoPath = Icons.Positive,
                Glyph = existing is null ? Glyphs.Add : Glyphs.Save,
                Score = TopScore,
                AddSelectedCount = false,
                Action = _ =>
                {
                    if (services.TryUpdate(data => data.UpsertProfile(new SshProfile(name, command), existing?.Name)))
                    {
                        services.Api.ChangeQuery(context.Prefix + name, requery: true);
                    }

                    return false;
                },
            },
        };

        if (!services.OpenSshInstalled && SshCommand.UsesOpenSsh(command))
        {
            results.Add(OpenSshMissingResult());
        }

        return results;
    }

    // ------------------------------------------------------------------ edit / remove

    private List<Result> PickProfile(string text, QueryContext context, PickMode mode)
    {
        var data = Data;
        if (data.Profiles.Count == 0)
        {
            return [EmptyStateResult(context)];
        }

        var query = SearchQuery.Parse(text.Replace('"', ' '));
        var results = data.Profiles
            .OrderBy(profile => TextFolding.Fold(profile.Name), StringComparer.Ordinal)
            .Select(profile => (Profile: profile, Match: ProfileMatcher.Match(query, profile.Name, profile.Command)))
            .Where(candidate => candidate.Match is not null)
            .Select(candidate => ProfileResult(candidate.Profile, candidate.Match!, context, mode))
            .ToList();

        if (results.Count == 0)
        {
            results.Add(InfoResult(services.T("plugin_easyssh_no_match_title", query.Text), services.T("plugin_easyssh_no_match_subtitle")));
        }

        return results;
    }

    private bool EditProfile(SshProfile profile, string prefix)
    {
        services.Api.ChangeQuery($"{prefix}add {CommandLine.Quote(profile.Name)} {profile.Command}");
        return false;
    }

    private bool RemoveProfile(SshProfile profile, bool fromContextMenu)
    {
        var answer = services.Api.ShowMsgBox(
            services.T("plugin_easyssh_remove_confirm", profile.Name, profile.Command),
            services.T("plugin_easyssh_plugin_name"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes || !services.TryUpdate(data => data.RemoveProfile(profile.Name)))
        {
            return false;
        }

        if (fromContextMenu)
        {
            services.Api.BackToQueryResults();
        }

        services.Api.ReQuery();
        return false;
    }

    // ------------------------------------------------------------------ direct

    private List<Result> Direct(string text, string typedWord, QueryContext context)
    {
        if (text.Length == 0)
        {
            var usage = InfoResult(
                services.T("plugin_easyssh_cmd_direct_title"), services.T("plugin_easyssh_cmd_direct_subtitle"),
                Glyphs.Host, context.Prefix + "d ");
            return [usage, .. Search(typedWord, context, withShortcuts: false, withFallback: false).Where(IsMatchResult)];
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

            var isSelected = ReferenceEquals(terminal, selected) || terminal.Name == selected.Name;
            var available = PluginServices.IsAvailable(terminal);
            var description = available ? Describe(terminal) : services.T("plugin_easyssh_terminal_not_found");
            results.Add(new Result
            {
                Title = displayName,
                SubTitle = isSelected ? services.T("plugin_easyssh_terminal_selected", description) : description,
                IcoPath = isSelected ? Icons.Positive : available ? Icons.App : Icons.Negative,
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

                    if (services.TryUpdate(data => data.SelectTerminal(terminal.Name)))
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
                context.Prefix + "terminal add ", Glyphs.Add, ShortcutScore + 2));
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
                IcoPath = available ? Icons.Positive : Icons.Negative,
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
                IcoPath = Icons.Negative,
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

    // ------------------------------------------------------------------ ~/.ssh/config

    private List<Result> Config(string text, QueryContext context)
    {
        var data = Data;
        var openConfig = new Result
        {
            Title = services.T("plugin_easyssh_config_open_title"),
            SubTitle = services.SshConfig.ConfigPath,
            IcoPath = Icons.App,
            Glyph = Glyphs.Document,
            Score = ShortcutScore,
            AddSelectedCount = false,
            Action = _ =>
            {
                services.OpenFile(services.SshConfig.ConfigPath);
                return true;
            },
        };

        if (!data.ShowSshConfigHosts)
        {
            return [InfoResult(services.T("plugin_easyssh_config_disabled_title"), services.T("plugin_easyssh_config_disabled_subtitle")), SettingsResult(TopScore - 1), openConfig];
        }

        var hosts = services.SshConfig.GetHosts();
        if (hosts.Count == 0)
        {
            return [InfoResult(services.T("plugin_easyssh_config_empty_title"), services.SshConfig.ConfigPath), openConfig];
        }

        var query = SearchQuery.Parse(text.Replace('"', ' '));
        var results = hosts
            .OrderBy(host => TextFolding.Fold(host.Alias), StringComparer.Ordinal)
            .Select(host => (Host: host, Match: ProfileMatcher.Match(query, host.Alias, host.Command + " " + host.Target)))
            .Where(candidate => candidate.Match is not null)
            .Select(candidate => HostResult(candidate.Host, candidate.Match!, context))
            .ToList();

        if (results.Count == 0)
        {
            results.Add(InfoResult(services.T("plugin_easyssh_no_match_title", query.Text), services.T("plugin_easyssh_no_match_subtitle")));
        }

        results.Add(openConfig);
        return results;
    }

    // ------------------------------------------------------------------ help

    private List<Result> Help(QueryContext context)
    {
        var results = new List<Result>();
        var score = TopScore;
        foreach (var (verb, keyword, glyph) in Commands)
        {
            results.Add(CommandResult(verb, keyword, glyph, context, score--));
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

    private Result CommandResult(Verb verb, string keyword, GlyphInfo glyph, QueryContext context, int score)
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

    private List<Result> ProfileMenu(ProfileItem item)
    {
        var profile = item.Profile;
        var results = new List<Result>
        {
            CopyResult(profile.Command),
            new()
            {
                Title = services.T("plugin_easyssh_ctx_edit"),
                SubTitle = services.T("plugin_easyssh_ctx_edit_subtitle"),
                IcoPath = Icons.App,
                Glyph = Glyphs.Edit,
                Action = _ =>
                {
                    services.Api.BackToQueryResults();
                    return EditProfile(profile, item.Prefix);
                },
            },
            new()
            {
                Title = services.T("plugin_easyssh_ctx_remove"),
                SubTitle = profile.Command,
                IcoPath = Icons.Negative,
                Glyph = Glyphs.Remove,
                Action = _ => RemoveProfile(profile, fromContextMenu: true),
            },
        };
        results.AddRange(OpenInOtherTerminals(profile.Command, profile.Name));
        return results;
    }

    private List<Result> HostMenu(HostItem item)
    {
        var host = item.Host;
        var results = new List<Result>
        {
            CopyResult(host.Command),
            new()
            {
                Title = services.T("plugin_easyssh_ctx_save_profile"),
                SubTitle = host.Command,
                IcoPath = Icons.Positive,
                Glyph = Glyphs.Save,
                Action = _ =>
                {
                    services.Api.BackToQueryResults();
                    services.Api.ChangeQuery($"{item.Prefix}add {CommandLine.Quote(host.Alias)} {host.Command}");
                    return false;
                },
            },
            new()
            {
                Title = services.T("plugin_easyssh_config_open_title"),
                SubTitle = host.SourceFile,
                IcoPath = Icons.App,
                Glyph = Glyphs.Document,
                Action = _ =>
                {
                    services.OpenFile(host.SourceFile);
                    return true;
                },
            },
        };
        results.AddRange(OpenInOtherTerminals(host.Command, host.Alias));
        return results;
    }

    private List<Result> DirectMenu(DirectItem item)
    {
        var name = SshCommand.GuessDestination(item.Command) ?? "name";
        var results = new List<Result>
        {
            CopyResult(item.Command),
            new()
            {
                Title = services.T("plugin_easyssh_ctx_save_profile"),
                SubTitle = item.Command,
                IcoPath = Icons.Positive,
                Glyph = Glyphs.Save,
                Action = _ =>
                {
                    services.Api.BackToQueryResults();
                    services.Api.ChangeQuery($"{item.Prefix}add {CommandLine.Quote(name)} {item.Command}");
                    return false;
                },
            },
        };
        results.AddRange(OpenInOtherTerminals(item.Command, name));
        return results;
    }

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
        context.Prefix + "add ", Glyphs.Add, TopScore);

    private Result OpenSshMissingResult() => new()
    {
        Title = services.T("plugin_easyssh_ssh_missing_title"),
        SubTitle = services.T("plugin_easyssh_ssh_missing_subtitle"),
        IcoPath = Icons.Negative,
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

    private Result ChangeQueryResult(string title, string subTitle, string query, GlyphInfo glyph, int score) => new()
    {
        Title = title,
        SubTitle = subTitle,
        IcoPath = Icons.App,
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

    /// <summary>Keeps only connections (profiles and hosts), not hints.</summary>
    private static bool IsMatchResult(Result result) => result.ContextData is ProfileItem or HostItem;

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
