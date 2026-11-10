using System.Text.RegularExpressions;

namespace Flow.Launcher.Plugin.EasySsh.Core;

/// <summary>Turns what the user typed into the command line that will actually run.</summary>
public static partial class SshCommand
{
    /// <summary>Programs that start a "full" command: anything else is treated as arguments for ssh.</summary>
    private static readonly HashSet<string> KnownPrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        "ssh", "scp", "sftp", "mosh", "autossh", "plink", "putty", "kitty", "telnet",
        "wsl", "bash", "docker", "kubectl", "cmd", "powershell", "pwsh",
    };

    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1",
    };

    /// <summary>ssh options that consume the next argument (see <c>man ssh</c>).</summary>
    private const string OptionsWithValue = "BbcDEeFIiJLlmOoPpQRSWw";

    /// <summary>
    /// Normalizes a command:
    /// <list type="bullet">
    /// <item><c>root@host -p 22</c> becomes <c>ssh root@host -p 22</c> (arguments only, as stored by EasySSH 1.x);</item>
    /// <item><c>ssh …</c>, <c>wsl …</c>, <c>C:\tools\plink.exe …</c> are kept as-is;</item>
    /// <item>single-quoted Windows paths (<c>-i 'C:\key'</c>) are converted to double quotes,
    /// because single quotes are not quotes for cmd.exe nor for ssh.exe.</item>
    /// </list>
    /// </summary>
    public static string Normalize(string? input)
    {
        var text = FixSingleQuotedPaths((input ?? string.Empty).Trim());
        if (text.Length == 0)
        {
            return text;
        }

        var (first, _) = CommandLine.SplitFirst(text);
        return StartsWithProgram(first) ? text : "ssh " + text;
    }

    /// <summary>Whether <paramref name="firstToken"/> designates a program rather than an ssh destination or option.</summary>
    public static bool StartsWithProgram(string firstToken)
    {
        if (string.IsNullOrEmpty(firstToken)
            || firstToken.StartsWith('-')
            || firstToken.Contains("://", StringComparison.Ordinal)
            || firstToken.Contains('@'))
        {
            return false;
        }

        if (firstToken.IndexOfAny(['\\', '/']) >= 0)
        {
            return true;
        }

        return ExecutableExtensions.Contains(Path.GetExtension(firstToken)) || KnownPrograms.Contains(firstToken);
    }

    /// <summary>Whether the command runs OpenSSH (so it needs ssh.exe to be installed).</summary>
    public static bool UsesOpenSsh(string command)
    {
        var (first, _) = CommandLine.SplitFirst(command);
        var name = Path.GetFileNameWithoutExtension(first);
        return name.Equals("ssh", StringComparison.OrdinalIgnoreCase)
               || name.Equals("scp", StringComparison.OrdinalIgnoreCase)
               || name.Equals("sftp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Replaces <c>'C:\path'</c>, <c>'~/path'</c> or <c>'%VAR%\path'</c> by the same path in double quotes.</summary>
    public static string FixSingleQuotedPaths(string text) => SingleQuotedPath().Replace(text, "\"$1\"");

    /// <summary>
    /// Best-effort destination host of an ssh command, used to suggest a profile name:
    /// <c>ssh -p 2222 root@10.0.0.5</c> gives <c>10.0.0.5</c>.
    /// </summary>
    public static string? GuessDestination(string command)
    {
        var tokens = CommandLine.Tokenize(command);
        var index = tokens.Count > 0 && StartsWithProgram(tokens[0].Value) ? 1 : 0;
        for (; index < tokens.Count; index++)
        {
            var value = tokens[index].Value;
            if (value.StartsWith('-'))
            {
                if (value.Length == 2 && OptionsWithValue.Contains(value[1]))
                {
                    index++;
                }

                continue;
            }

            if (value.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
            {
                value = value["ssh://".Length..];
                var portSeparator = value.LastIndexOf(':');
                if (portSeparator > value.LastIndexOf(']'))
                {
                    value = value[..portSeparator];
                }
            }

            var at = value.LastIndexOf('@');
            if (at >= 0)
            {
                value = value[(at + 1)..];
            }

            return value.Length > 0 ? value : null;
        }

        return null;
    }

    [GeneratedRegex(@"'((?:[A-Za-z]:|~|%[^%\s']+%)[\\/][^']*)'")]
    private static partial Regex SingleQuotedPath();
}
