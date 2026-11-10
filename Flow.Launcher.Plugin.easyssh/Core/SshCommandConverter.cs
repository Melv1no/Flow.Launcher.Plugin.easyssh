using System.Text;

namespace Flow.Launcher.Plugin.EasySsh.Core;

/// <summary>Result of <see cref="SshCommandConverter.ToHost"/>.</summary>
/// <param name="Host">The host, when the command can be written as an ssh_config block.</param>
/// <param name="Destination">The host name given to ssh (without user nor port), when one was found.</param>
/// <param name="Unsupported">What prevents the conversion: an option such as <c>-N</c>, or the program name.</param>
public sealed record HostConversion(SshHostEntry? Host, string? Destination, string? Unsupported)
{
    /// <summary>Whether the command can be saved as a host.</summary>
    public bool Succeeded => Host is not null;
}

/// <summary>
/// Converts between ssh command lines (<c>ssh -p 2222 -i key admin@10.0.0.5</c>) and ssh_config host blocks,
/// so that <c>ssh alias</c> does exactly what the command did.
/// </summary>
/// <remarks>
/// Only keywords understood by every OpenSSH version shipped with Windows 10 and 11 are written: an unknown
/// keyword is a fatal error for ssh, for every host. Options without such a keyword (<c>-N</c>, <c>-f</c>,
/// <c>-W</c>…) make the conversion fail: the command is then kept as a command.
/// </remarks>
public static class SshCommandConverter
{
    /// <summary>Keywords that can appear several times (every occurrence is used).</summary>
    private static readonly HashSet<string> MultipleKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "IdentityFile", "CertificateFile", "LocalForward", "RemoteForward", "DynamicForward", "SendEnv", "SetEnv",
    };

    /// <summary>Keywords whose value is the rest of the line.</summary>
    private static readonly HashSet<string> RestOfLineKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProxyCommand", "RemoteCommand", "LocalCommand", "KnownHostsCommand",
    };

    /// <summary>Keywords taking several words.</summary>
    private static readonly HashSet<string> MultiWordKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "LocalForward", "RemoteForward", "SendEnv", "SetEnv", "UserKnownHostsFile", "GlobalKnownHostsFile",
        "CanonicalDomains", "PermitRemoteOpen", "ChannelTimeout",
    };

    /// <summary>Keywords holding a file path.</summary>
    private static readonly HashSet<string> PathKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "IdentityFile", "CertificateFile", "PKCS11Provider",
    };

    /// <summary>ssh flags without value and the options they stand for.</summary>
    private static readonly Dictionary<char, (string Keyword, string Value)[]> Flags = new()
    {
        ['4'] = [("AddressFamily", "inet")],
        ['6'] = [("AddressFamily", "inet6")],
        ['A'] = [("ForwardAgent", "yes")],
        ['a'] = [("ForwardAgent", "no")],
        ['C'] = [("Compression", "yes")],
        ['g'] = [("GatewayPorts", "yes")],
        ['K'] = [("GSSAPIAuthentication", "yes"), ("GSSAPIDelegateCredentials", "yes")],
        ['k'] = [("GSSAPIDelegateCredentials", "no")],
        ['q'] = [("LogLevel", "QUIET")],
        ['T'] = [("RequestTTY", "no")],
        ['X'] = [("ForwardX11", "yes")],
        ['x'] = [("ForwardX11", "no")],
        ['Y'] = [("ForwardX11", "yes"), ("ForwardX11Trusted", "yes")],
    };

    /// <summary>ssh options taking a value and their keyword (null for the ones converted specially).</summary>
    private static readonly Dictionary<char, string?> ValueOptions = new()
    {
        ['b'] = "BindAddress",
        ['c'] = "Ciphers",
        ['D'] = "DynamicForward",
        ['e'] = "EscapeChar",
        ['I'] = "PKCS11Provider",
        ['i'] = "IdentityFile",
        ['J'] = "ProxyJump",
        ['L'] = null,
        ['l'] = "User",
        ['m'] = "MACs",
        ['o'] = null,
        ['p'] = "Port",
        ['R'] = null,
    };

    /// <summary>
    /// Converts <paramref name="command"/> into the host <paramref name="alias"/>. The command can be a full
    /// ssh command or what follows <c>ssh</c> (<c>admin@10.0.0.5 -p 2222</c>).
    /// </summary>
    public static HostConversion ToHost(string alias, string command)
    {
        var tokens = CommandLine.Tokenize(SshCommand.Normalize(command));
        if (tokens.Count == 0)
        {
            return new HostConversion(null, null, string.Empty);
        }

        if (!IsPlainSsh(tokens[0].Value))
        {
            return new HostConversion(null, null, tokens[0].Value);
        }

        var state = new ParseState();
        string? host = null;
        string? remoteCommand = null;
        var optionsEnded = false;
        var index = 1;
        while (index < tokens.Count)
        {
            var token = tokens[index].Value;
            if (!optionsEnded && token == "--")
            {
                optionsEnded = true;
                index++;
                continue;
            }

            if (!optionsEnded && token.Length > 1 && token[0] == '-')
            {
                var error = ParseOptionGroup(tokens, ref index, state);
                if (error is not null)
                {
                    return new HostConversion(null, host, error);
                }

                continue;
            }

            if (host is null)
            {
                if (!TryParseDestination(token, out var parsedHost, out var user, out var port))
                {
                    return new HostConversion(null, null, token);
                }

                // As on the command line, the first value wins: "-l" before "user@" keeps priority, not after.
                host = parsedHost;
                state.User ??= user;
                state.Port ??= port;
                index++;
                continue;
            }

            // ssh reads options after the destination too, up to the first word of the remote command.
            remoteCommand = string.Join(" ", tokens.Skip(index).Select(remaining => remaining.Value));
            break;
        }

        if (host is null)
        {
            return new HostConversion(null, null, string.Empty);
        }

        if (!SshConfigText.IsValidAlias(alias))
        {
            return new HostConversion(null, host, alias);
        }

        // On the command line '%' is a plain character; in ssh_config, HostName expands %-tokens (an IPv6 zone such
        // as fe80::1%12 must be written %%), and recent versions expand User too, older ones don't: no safe way.
        if (state.User is { } userWithPercent && userWithPercent.Contains('%'))
        {
            return new HostConversion(null, host, "-l " + userWithPercent);
        }

        var options = new List<SshOption>();
        if (!host.Equals(alias, StringComparison.OrdinalIgnoreCase))
        {
            options.Add(SshOption.Single("HostName", host.Replace("%", "%%", StringComparison.Ordinal)));
        }

        if (state.User is { } effectiveUser)
        {
            options.Add(SshOption.Single("User", effectiveUser));
        }

        if (state.Port is { } effectivePort)
        {
            options.Add(SshOption.Single("Port", effectivePort));
        }

        options.AddRange(state.Options);
        if (state.Tty > 0)
        {
            AddOption(options, "RequestTTY", state.Tty > 1 ? "force" : "yes");
        }

        if (state.Verbose > 0)
        {
            AddOption(options, "LogLevel", "DEBUG" + Math.Min(state.Verbose, 3));
        }

        // An empty remote command ("ssh host \"\"") opens a shell, as without command.
        if (!string.IsNullOrWhiteSpace(remoteCommand))
        {
            // '%' is a token in RemoteCommand; since OpenSSH 8.7, quotes must be balanced on every line and '#'
            // starts a comment: commands using them stay commands.
            if (remoteCommand.IndexOfAny(['"', '\'', '#', '\r', '\n']) >= 0)
            {
                return new HostConversion(null, host, remoteCommand);
            }

            AddOption(options, "RemoteCommand", remoteCommand.Replace("%", "%%", StringComparison.Ordinal));
        }

        return new HostConversion(new SshHostEntry(alias, options), host, null);
    }

    /// <summary>
    /// Writes a host back as an ssh command (<c>ssh admin@10.0.0.5 -p 2222 -i ~/.ssh/key</c>): what <c>ssh add</c>
    /// needs to recreate it. It starts with <c>ssh</c>, so a host such as <c>example.com</c> is never taken for a
    /// program. Options without a short form are written with <c>-o</c>.
    /// </summary>
    public static string ToArguments(SshHostEntry host)
    {
        var arguments = new List<string> { "ssh" };
        var destination = (host.HostName ?? host.Alias).Replace("%%", "%", StringComparison.Ordinal);
        arguments.Add(CommandLine.Quote(host.User is { } user ? user + "@" + destination : destination));
        string? remoteCommand = null;
        foreach (var option in host.Options)
        {
            var keyword = option.Keyword;
            if (keyword.Equals("HostName", StringComparison.OrdinalIgnoreCase) || keyword.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            switch (keyword.ToLowerInvariant())
            {
                case "port":
                    arguments.Add("-p " + CommandLine.Quote(option.FirstValue));
                    break;
                case "identityfile":
                    arguments.Add("-i " + CommandLine.Quote(option.FirstValue));
                    break;
                case "proxyjump":
                    arguments.Add("-J " + CommandLine.Quote(option.FirstValue));
                    break;
                case "localforward" or "remoteforward" when SshConfigText.Split(option.Value) is [var listen, var target]:
                    arguments.Add((keyword.Equals("LocalForward", StringComparison.OrdinalIgnoreCase) ? "-L " : "-R ") + CommandLine.Quote(listen + ":" + target));
                    break;
                case "dynamicforward":
                    arguments.Add("-D " + CommandLine.Quote(option.FirstValue));
                    break;
                case "remotecommand":
                    remoteCommand = option.Value.Replace("%%", "%", StringComparison.Ordinal);
                    break;
                default:
                    arguments.Add("-o " + CommandLine.Quote(keyword + "=" + option.Value));
                    break;
            }
        }

        if (remoteCommand is not null)
        {
            arguments.Add(remoteCommand);
        }

        return string.Join(" ", arguments);
    }

    /// <summary>Whether a port number is valid.</summary>
    public static bool IsPort(string? text) =>
        int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port)
        && port is > 0 and <= 65535;

    /// <summary>
    /// Parses an ssh destination: <c>[user@]host</c> or <c>ssh://[user@]host[:port]</c>.
    /// </summary>
    public static bool TryParseDestination(string text, out string host, out string? user, out string? port)
    {
        host = string.Empty;
        user = null;
        port = null;
        var rest = text;
        var isUri = rest.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase);
        if (isUri)
        {
            rest = rest["ssh://".Length..].TrimEnd('/');
        }

        var at = rest.LastIndexOf('@');
        if (at >= 0)
        {
            user = rest[..at];
            rest = rest[(at + 1)..];
            if (user.Length == 0)
            {
                return false;
            }
        }

        if (isUri)
        {
            if (rest.StartsWith('['))
            {
                var close = rest.IndexOf(']');
                if (close < 0)
                {
                    return false;
                }

                var after = rest[(close + 1)..];
                if (after.StartsWith(':'))
                {
                    port = after[1..];
                }
                else if (after.Length > 0)
                {
                    return false;
                }

                rest = rest[1..close];
            }
            else if (rest.LastIndexOf(':') is var colon and >= 0)
            {
                port = rest[(colon + 1)..];
                rest = rest[..colon];
            }

            if (port is not null && !IsPort(port))
            {
                return false;
            }
        }

        host = rest;
        return host.Length > 0 && !host.Any(char.IsWhiteSpace) && host[0] != '-';
    }

    private static bool IsPlainSsh(string program) =>
        program.Equals("ssh", StringComparison.OrdinalIgnoreCase) || program.Equals("ssh.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses one argument starting with '-', such as <c>-p</c>, <c>-p2222</c>, <c>-AX</c> or <c>-vvv</c>.</summary>
    /// <returns>Null, or the option that can't be converted.</returns>
    private static string? ParseOptionGroup(IReadOnlyList<CommandToken> tokens, ref int index, ParseState state)
    {
        var token = tokens[index].Value;
        index++;
        for (var position = 1; position < token.Length; position++)
        {
            var flag = token[position];
            if (Flags.TryGetValue(flag, out var flagOptions))
            {
                foreach (var (flagKeyword, flagValue) in flagOptions)
                {
                    AddOption(state.Options, flagKeyword, flagValue);
                }

                continue;
            }

            if (flag == 't')
            {
                state.Tty++;
                continue;
            }

            if (flag == 'v')
            {
                state.Verbose++;
                continue;
            }

            var takesValue = ValueOptions.TryGetValue(flag, out var keywordForValue);
            if (!takesValue)
            {
                return "-" + flag;
            }

            string value;
            if (position + 1 < token.Length)
            {
                value = token[(position + 1)..];
            }
            else if (index < tokens.Count)
            {
                value = tokens[index].Value;
                index++;
            }
            else
            {
                return "-" + flag;
            }

            return ApplyValueOption(flag, keywordForValue, value, state) ? null : $"-{flag} {value}";
        }

        return null;
    }

    private static bool ApplyValueOption(char flag, string? keyword, string value, ParseState state)
    {
        value = value.Trim();
        if (value.Length == 0 || value.Contains('"'))
        {
            return false;
        }

        switch (flag)
        {
            case 'l':
                state.User ??= value;
                return true;
            case 'p':
                if (!IsPort(value))
                {
                    return false;
                }

                state.Port ??= value;
                return true;
            case 'L' or 'R':
                if (ConvertForward(value, remote: flag == 'R') is not { } forward)
                {
                    return false;
                }

                AddOption(state.Options, flag == 'L' ? "LocalForward" : "RemoteForward", forward);
                return true;
            case 'o':
                return ApplyGenericOption(value, state);
        }

        if (keyword is null)
        {
            return false;
        }

        AddOption(state.Options, keyword, PathKeywords.Contains(keyword) ? SshConfigText.NormalizePath(value) : value);
        return true;
    }

    /// <summary><c>-o Keyword=value</c> or <c>-o "Keyword value"</c>.</summary>
    private static bool ApplyGenericOption(string text, ParseState state)
    {
        var (keyword, value) = SshConfigText.SplitDirective(text);
        if (keyword.Length == 0 || value.Length == 0 || !keyword.All(char.IsAsciiLetterOrDigit) || SshConfigText.SectionKeywords.Contains(keyword))
        {
            return false;
        }

        switch (keyword.ToLowerInvariant())
        {
            case "user":
                state.User ??= value;
                return true;
            case "port":
                if (!IsPort(value))
                {
                    return false;
                }

                state.Port ??= value;
                return true;
            case "hostname":
                // The host name comes from the destination; a different one here is too surprising to convert.
                return false;
        }

        AddOption(state.Options, keyword, PathKeywords.Contains(keyword) ? SshConfigText.NormalizePath(value) : value);
        return true;
    }

    /// <summary>
    /// <c>-L [bind:]port:host:hostport</c> becomes <c>[bind:]port host:hostport</c> (ssh_config separates the
    /// listening side from the target with a space). Unix sockets and IPv6 addresses in brackets are kept.
    /// </summary>
    private static string? ConvertForward(string spec, bool remote)
    {
        var parts = SplitForward(spec);
        if (parts is null)
        {
            return null;
        }

        return parts.Count switch
        {
            4 => $"{parts[0]}:{parts[1]} {parts[2]}:{parts[3]}",
            3 => $"{parts[0]} {parts[1]}:{parts[2]}",
            // "-R [bind:]port" alone is a dynamic forward on the remote side: written as is.
            2 when remote && IsPort(parts[1]) => spec,
            2 => $"{parts[0]} {parts[1]}",
            1 when remote && IsPort(parts[0]) => spec,
            _ => null,
        };
    }

    private static List<string>? SplitForward(string spec)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inBrackets = false;
        foreach (var c in spec)
        {
            switch (c)
            {
                case '[':
                    inBrackets = true;
                    break;
                case ']':
                    inBrackets = false;
                    break;
                case ':' when !inBrackets:
                    parts.Add(current.ToString());
                    current.Clear();
                    continue;
            }

            current.Append(c);
        }

        parts.Add(current.ToString());
        return inBrackets || parts.Any(part => part.Length == 0 || part.Any(char.IsWhiteSpace)) ? null : parts;
    }

    /// <summary>Adds an option; for keywords allowed once, only the first value is kept (as ssh does).</summary>
    private static void AddOption(List<SshOption> options, string keyword, string value)
    {
        if (!MultipleKeywords.Contains(keyword)
            && options.Any(option => option.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var written = RestOfLineKeywords.Contains(keyword) || MultiWordKeywords.Contains(keyword)
            ? value
            : SshConfigText.QuoteValue(value);
        options.Add(new SshOption(keyword, written));
    }

    private sealed class ParseState
    {
        public List<SshOption> Options { get; } = [];

        public string? User { get; set; }

        public string? Port { get; set; }

        public int Tty { get; set; }

        public int Verbose { get; set; }
    }
}
