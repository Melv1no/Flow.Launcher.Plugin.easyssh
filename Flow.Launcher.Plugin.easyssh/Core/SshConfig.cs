using System.Text.RegularExpressions;

namespace Flow.Launcher.Plugin.EasySsh.Core;

/// <summary>A concrete host alias declared by a <c>Host</c> line of an OpenSSH client configuration.</summary>
/// <param name="Alias">The name given to ssh.</param>
/// <param name="HostName">Real host name, from the host's own block.</param>
/// <param name="User">User name, from the host's own block.</param>
/// <param name="Port">Port, from the host's own block.</param>
/// <param name="ProxyJump">Jump host(s), from the host's own block.</param>
/// <param name="IdentityFile">First identity file, from the host's own block.</param>
/// <param name="SourceFile">Full path of the file declaring the host.</param>
public sealed record SshConfigHost(
    string Alias,
    string? HostName,
    string? User,
    string? Port,
    string? ProxyJump,
    string? IdentityFile,
    string SourceFile)
{
    /// <summary>The command that connects to this host.</summary>
    public string Command => "ssh " + CommandLine.Quote(Alias);

    /// <summary>A short description such as <c>admin@10.0.0.5:2222</c>, or null when the block only has the alias.</summary>
    public string? Target
    {
        get
        {
            if (HostName is null && User is null && Port is null)
            {
                return null;
            }

            var target = HostName ?? Alias;
            if (User is not null)
            {
                target = User + "@" + target;
            }

            return Port is null ? target : target + ":" + Port;
        }
    }
}

/// <summary>Hosts read from the configuration, plus what is needed to know when they are out of date.</summary>
public sealed class SshConfigSnapshot
{
    private readonly IReadOnlyList<(string Path, DateTime? Stamp)> _sources;

    private readonly IReadOnlyList<IReadOnlyList<string>> _patternBlocks;
    private readonly bool _matchesOriginalHost;

    internal SshConfigSnapshot(
        IReadOnlyList<SshConfigHost> hosts,
        IReadOnlyDictionary<string, IReadOnlyList<string>> duplicates,
        IReadOnlyList<string> files,
        IReadOnlyList<(string Path, DateTime? Stamp)> sources,
        IReadOnlyList<IReadOnlyList<string>> patternBlocks,
        bool matchesOriginalHost)
    {
        Hosts = hosts;
        Duplicates = duplicates;
        Files = files;
        _sources = sources;
        _patternBlocks = patternBlocks;
        _matchesOriginalHost = matchesOriginalHost;
    }

    /// <summary>An empty snapshot.</summary>
    public static SshConfigSnapshot Empty { get; } = new([], new Dictionary<string, IReadOnlyList<string>>(), [], [], [], false);

    /// <summary>
    /// Whether the <c>Host</c> blocks with patterns (<c>Host *.corp</c>, <c>Host * !bastion</c>) apply the same way
    /// to both names. ssh matches them against the name typed, not against HostName: an alias <c>web</c> for
    /// <c>web.corp</c> doesn't get the options of <c>Host *.corp</c>. With a <c>Match originalhost</c> block,
    /// that can't be known, so the answer is no.
    /// </summary>
    public bool HaveSameRules(string name, string other)
    {
        if (name.Equals(other, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !_matchesOriginalHost
               && _patternBlocks.All(patterns => SshConfigParser.MatchesHostLine(patterns, name) == SshConfigParser.MatchesHostLine(patterns, other));
    }

    /// <summary>Concrete hosts, in declaration order, without duplicates (ssh uses the first declaration).</summary>
    public IReadOnlyList<SshConfigHost> Hosts { get; }

    /// <summary>Aliases declared more than once, with the files of the declarations that ssh ignores.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Duplicates { get; }

    /// <summary>Configuration files that exist and were read, in reading order.</summary>
    public IReadOnlyList<string> Files { get; }

    /// <summary>Finds a host by alias (case-insensitive, as ssh does).</summary>
    public SshConfigHost? Find(string? alias) =>
        Hosts.FirstOrDefault(host => host.Alias.Equals(alias?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether one of the files or folders read has changed since the snapshot was taken.</summary>
    public bool IsStale() => _sources.Any(source => SshConfigParser.Stamp(source.Path) != source.Stamp);
}

/// <summary>
/// Reads host aliases from an OpenSSH client configuration (<c>~/.ssh/config</c>).
/// Supports <c>Host</c> blocks, <c>Include</c> (with globs, <c>~</c> and <c>${VAR}</c>), and both
/// <c>Keyword value</c> and <c>Keyword=value</c> syntaxes. Patterns (<c>*</c>, <c>?</c>, <c>!</c>) and
/// <c>Match</c> blocks are ignored because they don't name a single host.
/// </summary>
public static partial class SshConfigParser
{
    private const int MaxIncludeDepth = 16;

    /// <summary>The current user's <c>~/.ssh</c> folder.</summary>
    public static string DefaultSshDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    /// <summary>The current user's <c>~/.ssh/config</c> file.</summary>
    public static string DefaultConfigPath => Path.Combine(DefaultSshDirectory, "config");

    /// <summary>Reads <paramref name="configPath"/> and the files it includes.</summary>
    /// <param name="configPath">The configuration file. It may not exist.</param>
    /// <param name="sshDirectory">Folder used to resolve relative <c>Include</c> paths (defaults to the config folder).</param>
    public static SshConfigSnapshot Load(string configPath, string? sshDirectory = null)
    {
        var context = new ParseContext(sshDirectory ?? Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? string.Empty);
        ParseFile(Path.GetFullPath(configPath), 0, context);
        var hosts = context.Hosts.Select(builder => builder.Build()).ToArray();
        var duplicates = context.Duplicates.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
        return new SshConfigSnapshot(hosts, duplicates, context.Files, context.Sources, context.PatternBlocks, context.MatchesOriginalHost);
    }

    /// <summary>
    /// Whether a <c>Host</c> line applies to <paramref name="host"/>, as ssh decides it: one of its patterns
    /// matches (<c>*</c> and <c>?</c> are wildcards, case is ignored) and none of its negated patterns does.
    /// </summary>
    public static bool MatchesHostLine(IReadOnlyList<string> patterns, string host)
    {
        var matched = false;
        foreach (var pattern in patterns)
        {
            var negated = pattern.StartsWith('!');
            var expression = negated ? pattern[1..] : pattern;
            if (expression.Length == 0 || !System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(expression, host, ignoreCase: true))
            {
                continue;
            }

            if (negated)
            {
                return false;
            }

            matched = true;
        }

        return matched;
    }

    /// <summary>
    /// The files an <c>Include</c> pattern designates: <c>~</c> and <c>${VAR}</c> expanded, relative paths
    /// resolved in <paramref name="sshDirectory"/>, and wildcards in the file name expanded.
    /// </summary>
    public static IReadOnlyList<string> ResolveInclude(string pattern, string sshDirectory) =>
        ExpandInclude(pattern, sshDirectory, watch: null);

    /// <summary>Last write time of a file or folder, or null when it doesn't exist.</summary>
    internal static DateTime? Stamp(string path)
    {
        if (File.Exists(path))
        {
            return File.GetLastWriteTimeUtc(path);
        }

        return Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : null;
    }

    private static void ParseFile(string path, int depth, ParseContext context)
    {
        context.Sources.Add((path, Stamp(path)));
        if (depth > MaxIncludeDepth || !context.Visited.Add(path) || !File.Exists(path))
        {
            return;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        context.Files.Add(path);
        List<HostBuilder>? block = null;
        foreach (var rawLine in lines)
        {
            if (SshConfigText.IsTrivia(rawLine))
            {
                continue;
            }

            var (keyword, arguments) = SshConfigText.SplitDirective(rawLine);
            switch (keyword.ToLowerInvariant())
            {
                case "host":
                    block = [];
                    var patterns = SshConfigText.Split(arguments);
                    if (patterns.Any(pattern => !IsConcreteAlias(pattern)))
                    {
                        context.PatternBlocks.Add(patterns);
                    }

                    foreach (var pattern in patterns)
                    {
                        if (!IsConcreteAlias(pattern))
                        {
                            continue;
                        }

                        if (context.Aliases.Add(pattern))
                        {
                            var host = new HostBuilder(pattern, path);
                            context.Hosts.Add(host);
                            block.Add(host);
                        }
                        else
                        {
                            if (!context.Duplicates.TryGetValue(pattern, out var files))
                            {
                                files = [];
                                context.Duplicates[pattern] = files;
                            }

                            files.Add(path);
                        }
                    }

                    break;

                case "match":
                    block = null;
                    context.MatchesOriginalHost |= arguments.Contains("originalhost", StringComparison.OrdinalIgnoreCase);
                    break;

                case "include":
                    foreach (var pattern in SshConfigText.Split(arguments))
                    {
                        foreach (var file in ExpandInclude(pattern, context.SshDirectory, context.Sources))
                        {
                            ParseFile(file, depth + 1, context);
                        }
                    }

                    break;

                case "hostname":
                    block?.ForEach(host => host.HostName ??= SshConfigText.FirstToken(arguments));
                    break;

                case "user":
                    block?.ForEach(host => host.User ??= SshConfigText.FirstToken(arguments));
                    break;

                case "port":
                    block?.ForEach(host => host.Port ??= SshConfigText.FirstToken(arguments));
                    break;

                case "proxyjump":
                    block?.ForEach(host => host.ProxyJump ??= SshConfigText.FirstToken(arguments));
                    break;

                case "identityfile":
                    block?.ForEach(host => host.IdentityFile ??= SshConfigText.FirstToken(arguments));
                    break;
            }
        }
    }

    private static bool IsConcreteAlias(string pattern) =>
        pattern.Length > 0 && !pattern.StartsWith('!') && pattern.IndexOfAny(['*', '?']) < 0;

    private static IReadOnlyList<string> ExpandInclude(string pattern, string sshDirectory, List<(string Path, DateTime? Stamp)>? watch)
    {
        var expanded = EnvironmentVariable().Replace(pattern, match =>
            Environment.GetEnvironmentVariable(match.Groups[1].Value) ?? string.Empty);
        if (expanded.StartsWith('~'))
        {
            expanded = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + expanded[1..];
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(sshDirectory, expanded));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return [];
        }

        var fileName = Path.GetFileName(fullPath);
        if (fileName.IndexOfAny(['*', '?']) < 0)
        {
            return [fullPath];
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || directory.IndexOfAny(['*', '?']) >= 0)
        {
            return [];
        }

        // Watching the folder lets the cache notice files added to or removed from it.
        watch?.Add((directory, Stamp(directory)));
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory.GetFiles(directory, fileName).Order(StringComparer.Ordinal).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex EnvironmentVariable();

    private sealed class ParseContext(string sshDirectory)
    {
        public string SshDirectory { get; } = sshDirectory;

        public List<HostBuilder> Hosts { get; } = [];

        public HashSet<string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<string>> Duplicates { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Visited { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Files { get; } = [];

        public List<(string Path, DateTime? Stamp)> Sources { get; } = [];

        public List<IReadOnlyList<string>> PatternBlocks { get; } = [];

        public bool MatchesOriginalHost { get; set; }
    }

    private sealed class HostBuilder(string alias, string sourceFile)
    {
        public string? HostName { get; set; }

        public string? User { get; set; }

        public string? Port { get; set; }

        public string? ProxyJump { get; set; }

        public string? IdentityFile { get; set; }

        public SshConfigHost Build() => new(alias, HostName, User, Port, ProxyJump, IdentityFile, sourceFile);
    }
}
