using System.ComponentModel;
using System.Globalization;
using Flow.Launcher.Plugin.EasySsh.Core;
using Flow.Launcher.Plugin.EasySsh.Storage;

namespace Flow.Launcher.Plugin.EasySsh;

/// <summary>Icons shipped with the plugin (paths relative to the plugin folder).</summary>
internal static class Icons
{
    public const string App = @"Images\app.png";
    public const string Positive = @"Images\app-green.png";
    public const string Negative = @"Images\app-red.png";
}

/// <summary>Segoe Fluent Icons glyphs, used when "Use Segoe Fluent Icons" is enabled in Flow Launcher.</summary>
internal static class Glyphs
{
    private const string Font = "/Resources/#Segoe Fluent Icons";

    public static GlyphInfo Host { get; } = new(Font, "\xE968");
    public static GlyphInfo Terminal { get; } = new(Font, "\xE756");
    public static GlyphInfo Add { get; } = new(Font, "\xE710");
    public static GlyphInfo Edit { get; } = new(Font, "\xE70F");
    public static GlyphInfo Remove { get; } = new(Font, "\xE74D");
    public static GlyphInfo Selected { get; } = new(Font, "\xE73E");
    public static GlyphInfo Settings { get; } = new(Font, "\xE713");
    public static GlyphInfo Help { get; } = new(Font, "\xE897");
    public static GlyphInfo Warning { get; } = new(Font, "\xE7BA");
    public static GlyphInfo Copy { get; } = new(Font, "\xE8C8");
    public static GlyphInfo Document { get; } = new(Font, "\xE8A5");
    public static GlyphInfo Link { get; } = new(Font, "\xE71B");
    public static GlyphInfo Save { get; } = new(Font, "\xE74E");
    public static GlyphInfo Search { get; } = new(Font, "\xE721");
}

/// <summary>Caches the hosts of <c>~/.ssh/config</c> and reads the file again only when it changes.</summary>
internal sealed class SshConfigCache(string configPath, Action<Exception>? onError = null)
{
    private readonly object _lock = new();
    private SshConfigSnapshot? _snapshot;

    /// <summary>Path of the configuration file.</summary>
    public string ConfigPath { get; } = configPath;

    /// <summary>Current hosts.</summary>
    public IReadOnlyList<SshConfigHost> GetHosts()
    {
        lock (_lock)
        {
            try
            {
                if (_snapshot is null || _snapshot.IsStale())
                {
                    _snapshot = SshConfigParser.Load(ConfigPath);
                }
            }
            catch (Exception e)
            {
                // Don't retry on every key stroke: the next "Reload Plugin Data" will.
                _snapshot = SshConfigSnapshot.Empty;
                onError?.Invoke(e);
            }

            return _snapshot.Hosts;
        }
    }

    /// <summary>Forces the next call to read the file again.</summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _snapshot = null;
        }
    }
}

/// <summary>What the query handler, the context menus and the settings panel share.</summary>
internal sealed class PluginServices(IPublicAPI api, DataStore store, SshConfigCache sshConfig, bool openSshInstalled, string legacyPath)
{
    public const string DocumentationUrl = "https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh#readme";

    private const string LogClass = "EasySSH";

    public IPublicAPI Api { get; } = api;

    public DataStore Store { get; } = store;

    public SshConfigCache SshConfig { get; } = sshConfig;

    /// <summary>Whether ssh.exe was found (checked at startup and on "Reload Plugin Data").</summary>
    public bool OpenSshInstalled { get; set; } = openSshInstalled;

    /// <summary>Data file of EasySSH 1.x, offered for import.</summary>
    public string LegacyPath { get; } = legacyPath;

    /// <summary>Translated text.</summary>
    public string T(string key) => Api.GetTranslation(key);

    /// <summary>Translated text with <see cref="string.Format(IFormatProvider, string, object[])"/> placeholders.</summary>
    public string T(string key, params object?[] arguments)
    {
        var format = Api.GetTranslation(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, arguments);
        }
        catch (FormatException)
        {
            return format + " " + string.Join(" ", arguments);
        }
    }

    /// <summary>Display name of a terminal: translated for presets, as typed for custom ones.</summary>
    public string DisplayName(TerminalDefinition terminal) =>
        terminal.IsBuiltIn ? T("plugin_easyssh_terminal_" + terminal.Name) : terminal.Name;

    /// <summary>Whether the program of <paramref name="terminal"/> can be found.</summary>
    public static bool IsAvailable(TerminalDefinition terminal) => ExecutableResolver.Resolve(terminal.Executable) is not null;

    /// <summary>Opens <paramref name="command"/> in <paramref name="terminal"/> (the selected one by default).</summary>
    /// <returns>Whether the terminal was started; errors are reported to the user.</returns>
    public bool Connect(string command, string title, TerminalDefinition? terminal = null)
    {
        var data = Store.Data;
        var target = terminal ?? data.GetSelectedTerminal();
        try
        {
            var plan = LaunchPlanBuilder.Build(target, command, title, data.KeepWindowOpenOnError, T("plugin_easyssh_pause_prompt"));
            TerminalLauncher.Launch(plan, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            return true;
        }
        catch (FileNotFoundException e)
        {
            Api.LogException(LogClass, $"Terminal not found: {target.Executable}", e);
            Api.ShowMsgError(T("plugin_easyssh_launch_error_title"), T("plugin_easyssh_terminal_missing", DisplayName(target), target.Executable));
            return false;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Api.LogException(LogClass, $"Could not start {target.Executable}", e);
            Api.ShowMsgError(T("plugin_easyssh_launch_error_title"), e.Message);
            return false;
        }
    }

    /// <summary>Applies a change to the data and reports a failure to the user.</summary>
    /// <returns>Whether the change was saved.</returns>
    public bool TryUpdate(Func<PluginData, PluginData> change)
    {
        try
        {
            Store.Update(change);
            return true;
        }
        catch (ArgumentException e)
        {
            Api.ShowMsgError(T("plugin_easyssh_save_error_title"), e.Message);
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Api.LogException(LogClass, "Could not save the data file", e);
            Api.ShowMsgError(T("plugin_easyssh_save_error_title"), e.Message);
            return false;
        }
    }

    /// <summary>Opens a file with its default application, or tells the user it doesn't exist.</summary>
    public void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            Api.ShowMsgError(T("plugin_easyssh_file_missing_title"), path);
            return;
        }

        try
        {
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Api.LogException(LogClass, $"Could not open {path}", e);
            Api.ShowMsgError(T("plugin_easyssh_file_missing_title"), e.Message);
        }
    }

    /// <summary>Writes an exception to Flow Launcher's log.</summary>
    public void Log(string message, Exception e) => Api.LogException(LogClass, message, e);
}
