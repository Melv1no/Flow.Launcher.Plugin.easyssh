using System.Text.RegularExpressions;

namespace Flow.Launcher.Plugin.EasySsh.Core;

/// <summary>A concrete host alias declared by a <c>Host</c> line of an OpenSSH client configuration.</summary>
public sealed record SshConfigHost(string Alias, string? HostName, string? User, string? Port, string SourceFile)
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

    internal SshConfigSnapshot(IReadOnlyList<SshConfigHost> hosts, IReadOnlyList<(string Path, DateTime? Stamp)> sources)
    {
        Hosts = hosts;
        _sources = sources;
    }

    /// <summary>An empty snapshot.</summary>
    public static SshConfigSnapshot Empty { get; } = new([], []);

    /// <summary>Concrete hosts, in declaration order, without duplicates.</summary>
    public IReadOnlyList<SshConfigHost> Hosts { get; }

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
        return new SshConfigSnapshot(hosts, context.Sources);
    }

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

        List<HostBuilder>? block = null;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var (keyword, arguments) = SplitDirective(line);
            switch (keyword.ToLowerInvariant())
            {
                case "host":
                    block = [];
                    foreach (var pattern in CommandLine.Tokenize(arguments).Select(token => token.Value))
                    {
                        if (IsConcreteAlias(pattern) && context.Aliases.Add(pattern))
                        {
                            var host = new HostBuilder(pattern, path);
                            context.Hosts.Add(host);
                            block.Add(host);
                        }
                    }

                    break;

                case "match":
                    block = null;
                    break;

                case "include":
                    foreach (var pattern in CommandLine.Tokenize(arguments).Select(token => token.Value))
                    {
                        foreach (var file in ExpandInclude(pattern, context))
                        {
                            ParseFile(file, depth + 1, context);
                        }
                    }

                    break;

                case "hostname":
                    block?.ForEach(host => host.HostName ??= FirstValue(arguments));
                    break;

                case "user":
                    block?.ForEach(host => host.User ??= FirstValue(arguments));
                    break;

                case "port":
                    block?.ForEach(host => host.Port ??= FirstValue(arguments));
                    break;
            }
        }
    }

    private static (string Keyword, string Arguments) SplitDirective(string line)
    {
        var end = 0;
        while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] != '=')
        {
            end++;
        }

        var rest = line[end..].TrimStart();
        if (rest.StartsWith('='))
        {
            rest = rest[1..].TrimStart();
        }

        return (line[..end], rest);
    }

    private static string? FirstValue(string arguments)
    {
        var tokens = CommandLine.Tokenize(arguments);
        return tokens.Count > 0 && tokens[0].Value.Length > 0 ? tokens[0].Value : null;
    }

    private static bool IsConcreteAlias(string pattern) =>
        pattern.Length > 0 && !pattern.StartsWith('!') && pattern.IndexOfAny(['*', '?']) < 0;

    private static IEnumerable<string> ExpandInclude(string pattern, ParseContext context)
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
            fullPath = Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(context.SshDirectory, expanded));
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
        context.Sources.Add((directory, Stamp(directory)));
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

        public HashSet<string> Visited { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Path, DateTime? Stamp)> Sources { get; } = [];
    }

    private sealed class HostBuilder(string alias, string sourceFile)
    {
        public string? HostName { get; set; }

        public string? User { get; set; }

        public string? Port { get; set; }

        public SshConfigHost Build() => new(alias, HostName, User, Port, sourceFile);
    }
}
