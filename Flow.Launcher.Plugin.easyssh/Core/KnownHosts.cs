namespace Flow.Launcher.Plugin.EasySsh.Core;

/// <summary>A server found in <c>~/.ssh/known_hosts</c>: one ssh already connected to.</summary>
/// <param name="Host">Host name or address.</param>
/// <param name="Port">Port, when it isn't 22.</param>
public sealed record KnownHost(string Host, string? Port)
{
    /// <summary>The command that connects to it.</summary>
    public string Command => Port is null ? "ssh " + CommandLine.Quote(Host) : $"ssh -p {Port} {CommandLine.Quote(Host)}";

    /// <summary><c>host</c> or <c>host:port</c>.</summary>
    public string Display => Port is null ? Host : Host + ":" + Port;
}

/// <summary>Reads the servers listed in a <c>known_hosts</c> file.</summary>
public static class KnownHostsParser
{
    /// <summary>The current user's <c>~/.ssh/known_hosts</c> file.</summary>
    public static string DefaultPath => Path.Combine(SshConfigParser.DefaultSshDirectory, "known_hosts");

    /// <summary>Reads a file; a missing or unreadable file gives an empty list.</summary>
    public static IReadOnlyList<KnownHost> Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadLines(path)) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Parses known_hosts lines. Hashed names (<c>|1|…</c>), markers (<c>@revoked</c>, <c>@cert-authority</c>)
    /// and patterns are skipped. Only the first name of a line is kept: the others are usually its IP address.
    /// </summary>
    public static IReadOnlyList<KnownHost> Parse(IEnumerable<string> lines)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hosts = new List<KnownHost>();
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] is '#' or '@' or '|')
            {
                continue;
            }

            var end = line.IndexOfAny([' ', '\t']);
            if (end <= 0)
            {
                continue;
            }

            var name = line[..end].Split(',')[0];
            if (name.Length == 0 || name[0] == '!' || name.IndexOfAny(['*', '?']) >= 0)
            {
                continue;
            }

            string host;
            string? port = null;
            if (name[0] == '[')
            {
                var close = name.IndexOf(']');
                if (close < 0)
                {
                    continue;
                }

                host = name[1..close];
                var after = name[(close + 1)..];
                if (after.StartsWith(':') && SshCommandConverter.IsPort(after[1..]) && after[1..] != "22")
                {
                    port = after[1..];
                }
            }
            else
            {
                host = name;
            }

            if (host.Length > 0 && seen.Add(host + ":" + port))
            {
                hosts.Add(new KnownHost(host, port));
            }
        }

        return hosts;
    }
}
