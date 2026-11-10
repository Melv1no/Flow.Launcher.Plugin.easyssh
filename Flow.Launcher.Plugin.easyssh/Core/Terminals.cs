using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Flow.Launcher.Plugin.EasySsh.Core;

/// <summary>How a terminal receives the command to run.</summary>
public enum TerminalKind
{
    /// <summary>User-defined: <see cref="TerminalDefinition.Arguments"/> is a template.</summary>
    Custom,

    /// <summary><c>cmd.exe</c>, hosted by the Windows default terminal application.</summary>
    CommandPrompt,

    /// <summary>Windows Terminal (<c>wt.exe</c>), new tab in the last used window.</summary>
    WindowsTerminal,

    /// <summary>Windows PowerShell 5.1 (<c>powershell.exe</c>).</summary>
    WindowsPowerShell,

    /// <summary>PowerShell 7+ (<c>pwsh.exe</c>).</summary>
    PowerShell,
}

/// <summary>A terminal able to run an SSH command.</summary>
/// <param name="Name">Unique name. Built-in terminals use a short id (<c>cmd</c>, <c>wt</c>…).</param>
/// <param name="Executable">Program to start: a name found in PATH or a full path.</param>
/// <param name="Arguments">
/// For custom terminals, the argument template: <c>{command}</c> is replaced by the command to run and
/// <c>{name}</c> by the profile name. Without <c>{command}</c>, the command is added at the end.
/// </param>
public sealed record TerminalDefinition(string Name, string Executable, string Arguments)
{
    /// <summary>How the command is passed to the terminal.</summary>
    [JsonIgnore]
    public TerminalKind Kind { get; init; } = TerminalKind.Custom;

    /// <summary>Whether this terminal is one of the presets shipped with the plugin.</summary>
    [JsonIgnore]
    public bool IsBuiltIn => Kind != TerminalKind.Custom;
}

/// <summary>Terminal presets shipped with the plugin.</summary>
public static class BuiltInTerminals
{
    /// <summary>Command Prompt, the default: it uses the terminal application chosen in Windows settings.</summary>
    public static readonly TerminalDefinition CommandPrompt = new("cmd", "cmd.exe", string.Empty) { Kind = TerminalKind.CommandPrompt };

    /// <summary>Windows Terminal.</summary>
    public static readonly TerminalDefinition WindowsTerminal = new("wt", "wt.exe", string.Empty) { Kind = TerminalKind.WindowsTerminal };

    /// <summary>Windows PowerShell 5.1.</summary>
    public static readonly TerminalDefinition WindowsPowerShell = new("powershell", "powershell.exe", string.Empty) { Kind = TerminalKind.WindowsPowerShell };

    /// <summary>PowerShell 7+.</summary>
    public static readonly TerminalDefinition PowerShell = new("pwsh", "pwsh.exe", string.Empty) { Kind = TerminalKind.PowerShell };

    /// <summary>All presets, in display order.</summary>
    public static IReadOnlyList<TerminalDefinition> All { get; } = [CommandPrompt, WindowsTerminal, WindowsPowerShell, PowerShell];

    /// <summary>The terminal used when nothing else is selected.</summary>
    public static TerminalDefinition Default => CommandPrompt;

    /// <summary>Finds a preset by name (case-insensitive).</summary>
    public static TerminalDefinition? Find(string? name) =>
        All.FirstOrDefault(terminal => terminal.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether <paramref name="name"/> is reserved by a preset.</summary>
    public static bool IsReservedName(string? name) => Find(name) is not null;
}

/// <summary>What to start: a program and its raw argument string.</summary>
public sealed record LaunchPlan(string FileName, string Arguments);

/// <summary>Builds the program and arguments that open a command in a terminal.</summary>
public static partial class LaunchPlanBuilder
{
    /// <summary>Placeholder replaced by the command to run.</summary>
    public const string CommandPlaceholder = "{command}";

    /// <summary>Placeholder replaced by the profile name.</summary>
    public const string NamePlaceholder = "{name}";

    /// <summary>Builds the launch plan.</summary>
    /// <param name="terminal">The terminal to use.</param>
    /// <param name="command">The full command to run (already normalized).</param>
    /// <param name="title">Window or tab title, usually the profile name.</param>
    /// <param name="keepOpenOnError">Wait for a key press when ssh fails (exit code 255) so the error can be read.</param>
    /// <param name="pausePrompt">Message shown by PowerShell while waiting.</param>
    public static LaunchPlan Build(TerminalDefinition terminal, string command, string title, bool keepOpenOnError, string pausePrompt)
    {
        title = string.IsNullOrWhiteSpace(title) ? "EasySSH" : title.Trim();
        var arguments = terminal.Kind switch
        {
            TerminalKind.CommandPrompt => CommandPromptArguments(command, title, keepOpenOnError),
            TerminalKind.WindowsTerminal => WindowsTerminalArguments(command, title),
            TerminalKind.WindowsPowerShell or TerminalKind.PowerShell => PowerShellArguments(command, title, keepOpenOnError, pausePrompt),
            _ => ExpandTemplate(terminal.Arguments, command, title),
        };
        return new LaunchPlan(terminal.Executable, arguments);
    }

    /// <summary>
    /// <c>cmd.exe /s /c "title X &amp; command"</c>: with <c>/s</c>, cmd removes only the outer quotes,
    /// so quotes inside the command are kept untouched.
    /// </summary>
    internal static string CommandPromptArguments(string command, string title, bool keepOpenOnError)
    {
        var line = $"title {EscapeForCmd(title)} & {command}";
        if (keepOpenOnError)
        {
            line += " & if errorlevel 255 pause";
        }

        return $"/s /c \"{line}\"";
    }

    /// <summary>
    /// Opens a new tab in the last used Windows Terminal window. The command is given as the tab command line
    /// (Windows Terminal keeps the tab open when the command fails). A <c>;</c> ending an argument is escaped
    /// because Windows Terminal uses it to separate its own sub-commands.
    /// </summary>
    internal static string WindowsTerminalArguments(string command, string title) =>
        $"-w 0 new-tab --title {CommandLine.Quote(title)} {WindowsTerminalSeparator().Replace(command, @"\;")}";

    /// <summary>
    /// Runs the command with <c>-EncodedCommand</c>: the script is passed in Base64, so no quoting rule of
    /// the PowerShell command line can alter it.
    /// </summary>
    internal static string PowerShellArguments(string command, string title, bool keepOpenOnError, string pausePrompt)
    {
        var script = new StringBuilder()
            .Append("$Host.UI.RawUI.WindowTitle = ").Append(PowerShellQuote(title)).Append('\n')
            .Append(command).Append('\n');
        if (keepOpenOnError)
        {
            script.Append("if ($LASTEXITCODE -eq 255) { Read-Host ").Append(PowerShellQuote(pausePrompt)).Append(" | Out-Null }\n");
        }

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString()));
        return $"-NoLogo -EncodedCommand {encoded}";
    }

    /// <summary>Expands a custom terminal template.</summary>
    internal static string ExpandTemplate(string? template, string command, string title)
    {
        var arguments = string.IsNullOrWhiteSpace(template) ? CommandPlaceholder : template.Trim();
        if (!arguments.Contains(CommandPlaceholder, StringComparison.OrdinalIgnoreCase))
        {
            arguments += " " + CommandPlaceholder;
        }

        return arguments
            .Replace(NamePlaceholder, title, StringComparison.OrdinalIgnoreCase)
            .Replace(CommandPlaceholder, command, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    /// <summary>Makes a title safe for <c>cmd.exe</c>: special characters are escaped with <c>^</c>.</summary>
    internal static string EscapeForCmd(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '"' or '%' or '\r' or '\n':
                    continue;
                case '&' or '|' or '<' or '>' or '^' or '(' or ')':
                    builder.Append('^').Append(c);
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string PowerShellQuote(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

    [GeneratedRegex(@";(?=\s|$)")]
    private static partial Regex WindowsTerminalSeparator();
}

/// <summary>Starts terminals.</summary>
public static class TerminalLauncher
{
    /// <summary>Starts <paramref name="plan"/> in <paramref name="workingDirectory"/>.</summary>
    /// <exception cref="FileNotFoundException">The terminal program can't be found.</exception>
    public static void Launch(LaunchPlan plan, string workingDirectory)
    {
        var fileName = ExecutableResolver.Resolve(plan.FileName)
                       ?? throw new FileNotFoundException($"\"{plan.FileName}\" was not found.", plan.FileName);
        var startInfo = new ProcessStartInfo(fileName, plan.Arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : string.Empty,
        };
        using var process = Process.Start(startInfo);
    }
}
