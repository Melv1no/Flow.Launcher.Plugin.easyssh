using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Flow.Launcher.Plugin.EasySsh.Core;
using Flow.Launcher.Plugin.EasySsh.Storage;

namespace Flow.Launcher.Plugin.EasySsh;

/// <summary>
/// Icons shipped with the plugin (paths relative to the plugin folder).
/// They are drawn by <c>scripts/icons/generate-icons.cs</c>.
/// </summary>
internal static class Icons
{
    /// <summary>The plugin icon: connections, commands and information.</summary>
    public const string App = @"Images\app.png";

    /// <summary>Something is about to be created.</summary>
    public const string Add = @"Images\add.png";

    /// <summary>The current choice, or a success.</summary>
    public const string Selected = @"Images\selected.png";

    /// <summary>Something is about to be deleted.</summary>
    public const string Remove = @"Images\remove.png";

    /// <summary>A problem to look at.</summary>
    public const string Warning = @"Images\warning.png";
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
    public static GlyphInfo Hide { get; } = new(Font, "\xED1A");
    public static GlyphInfo Show { get; } = new(Font, "\xE7B3");
    public static GlyphInfo Folder { get; } = new(Font, "\xE8B7");
    public static GlyphInfo History { get; } = new(Font, "\xE81C");
    public static GlyphInfo Command { get; } = new(Font, "\xE7C3");
}

/// <summary>Caches the ssh configuration and reads it again only when one of its files changes.</summary>
internal sealed class SshConfigCache(string configPath, Action<Exception>? onError = null)
{
    private readonly object _lock = new();
    private SshConfigSnapshot? _snapshot;

    /// <summary>Path of the main configuration file.</summary>
    public string ConfigPath { get; } = configPath;

    /// <summary>The current configuration.</summary>
    public SshConfigSnapshot GetSnapshot()
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
                // Don't retry on every key stroke: the next change of the files or "Reload Plugin Data" will.
                _snapshot = SshConfigSnapshot.Empty;
                onError?.Invoke(e);
            }

            return _snapshot;
        }
    }

    /// <summary>Current hosts.</summary>
    public IReadOnlyList<SshConfigHost> GetHosts() => GetSnapshot().Hosts;

    /// <summary>Forces the next call to read the files again.</summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _snapshot = null;
        }
    }
}

/// <summary>Caches the servers of <c>~/.ssh/known_hosts</c>.</summary>
internal sealed class KnownHostsCache(string path)
{
    private readonly object _lock = new();
    private (DateTime? Stamp, IReadOnlyList<KnownHost> Hosts)? _cache;

    /// <summary>Path of the file.</summary>
    public string Path { get; } = path;

    /// <summary>The servers listed in the file.</summary>
    public IReadOnlyList<KnownHost> Get()
    {
        lock (_lock)
        {
            var stamp = SshConfigParser.Stamp(Path);
            if (_cache is not { } cache || cache.Stamp != stamp)
            {
                cache = (stamp, KnownHostsParser.Load(Path));
                _cache = cache;
            }

            return cache.Hosts;
        }
    }
}

/// <summary>What the query handler, the context menus and the settings panel share.</summary>
internal sealed class PluginServices
{
    public const string DocumentationUrl = "https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh#readme";

    private const string LogClass = "EasySSH";

    private readonly object _hostsLock = new();

    public PluginServices(IPublicAPI api, DataStore store, SshConfigFiles files, bool openSshInstalled, string legacyPath, string knownHostsPath)
    {
        Api = api;
        Store = store;
        Files = files;
        SshConfig = new SshConfigCache(files.MainConfigPath, e => api.LogException(LogClass, "Could not read the SSH configuration", e));
        KnownHosts = new KnownHostsCache(knownHostsPath);
        OpenSshInstalled = openSshInstalled;
        LegacyPath = legacyPath;
    }

    /// <summary>Raised after hosts were added, changed or removed through EasySSH.</summary>
    public event EventHandler? HostsChanged;

    public IPublicAPI Api { get; }

    public DataStore Store { get; }

    /// <summary>The ssh_config files: <c>~/.ssh/config</c> and the file written by EasySSH.</summary>
    public SshConfigFiles Files { get; }

    public SshConfigCache SshConfig { get; }

    public KnownHostsCache KnownHosts { get; }

    /// <summary>Whether ssh.exe was found (checked at startup and on "Reload Plugin Data").</summary>
    public bool OpenSshInstalled { get; set; }

    /// <summary>Data file of EasySSH 1.x, offered for import.</summary>
    public string LegacyPath { get; }

    /// <summary>Starts a process. The tests replace it, so that they never start anything.</summary>
    internal Action<ProcessStartInfo> StartProcess { get; set; } = static info =>
    {
        using var process = Process.Start(info);
    };

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

    /// <summary>A short path for display: <c>~/.ssh/config</c>.</summary>
    public string DisplayPath(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var relative = System.IO.Path.GetRelativePath(Files.SshDirectory, fullPath);
        if (!relative.StartsWith("..", StringComparison.Ordinal) && !System.IO.Path.IsPathRooted(relative))
        {
            return "~/.ssh/" + relative.Replace('\\', '/');
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && fullPath.StartsWith(home + "\\", StringComparison.OrdinalIgnoreCase)
            ? "~" + fullPath[home.Length..].Replace('\\', '/')
            : fullPath;
    }

    /// <summary>Whether a host is declared in the file written by EasySSH (so EasySSH can change it).</summary>
    public bool IsManaged(SshConfigHost host) => Files.IsManaged(host.SourceFile);

    /// <summary>Whether the program of <paramref name="terminal"/> can be found.</summary>
    public static bool IsAvailable(TerminalDefinition terminal) => ExecutableResolver.Resolve(terminal.Executable) is not null;

    /// <summary>
    /// Opens <paramref name="command"/> in <paramref name="terminal"/> (the selected one by default).
    /// Graphical clients (PuTTY, KiTTY…) open their own window and are started directly.
    /// </summary>
    /// <returns>Whether the program was started; errors are reported to the user.</returns>
    public bool Connect(string command, string title, TerminalDefinition? terminal = null)
    {
        var data = Store.Data;
        var target = terminal ?? data.GetSelectedTerminal();
        try
        {
            var plan = GraphicalProgramPlan(command)
                       ?? LaunchPlanBuilder.Build(target, command, title, data.KeepWindowOpenOnError, T("plugin_easyssh_pause_prompt"));
            StartProcess(TerminalLauncher.CreateStartInfo(plan, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
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
            Api.LogException(LogClass, $"Could not start {command}", e);
            Api.ShowMsgError(T("plugin_easyssh_launch_error_title"), e.Message);
            return false;
        }
    }

    /// <summary>Applies a change to the plugin data and reports a failure to the user.</summary>
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

    /// <summary>Adds a host to the managed file, or rewrites the one named <paramref name="previousAlias"/>.</summary>
    /// <returns>Whether it was saved; errors are reported to the user.</returns>
    public bool TrySaveHost(SshHostEntry host, string? previousAlias = null) =>
        TryChangeHosts(config => config.Upsert(host, previousAlias));

    /// <summary>Removes a host from the managed file.</summary>
    /// <returns>Whether it was removed; errors are reported to the user.</returns>
    public bool TryRemoveHost(string alias) => TryChangeHosts(config =>
    {
        if (!config.Remove(alias))
        {
            throw new ArgumentException(T("plugin_easyssh_host_not_managed", alias, DisplayPath(Files.ManagedPath)));
        }
    });

    /// <summary>Like <see cref="ChangeHosts"/>, but a failure is shown to the user.</summary>
    public bool TryChangeHosts(Action<ManagedConfig> change)
    {
        var error = ChangeHosts(change);
        if (error is not null)
        {
            Api.ShowMsgError(T("plugin_easyssh_save_error_title"), error);
        }

        return error is null;
    }

    /// <summary>
    /// Changes the managed file: reads it, applies <paramref name="change"/> and saves it (which also makes sure
    /// <c>~/.ssh/config</c> includes it). Throws nothing.
    /// </summary>
    /// <returns>Null when saved, otherwise what went wrong.</returns>
    public string? ChangeHosts(Action<ManagedConfig> change)
    {
        try
        {
            bool includeAdded;
            lock (_hostsLock)
            {
                var config = Files.ReadManaged();
                change(config);
                includeAdded = Files.Save(config);
            }

            if (includeAdded)
            {
                Notify(T("plugin_easyssh_include_added", DisplayPath(Files.MainConfigPath)), Icons.Selected);
            }

            return null;
        }
        catch (SshConfigRejectedException e)
        {
            return T("plugin_easyssh_ssh_refused", e.SshMessage);
        }
        catch (ArgumentException e)
        {
            return e.Message;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Api.LogException(LogClass, "Could not save the SSH configuration", e);
            return e.Message;
        }
        finally
        {
            SshConfig.Invalidate();
            HostsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Imports the data of EasySSH 1.x: hosts into the managed ssh_config file, commands and terminals into the
    /// plugin data, and records the import. When <paramref name="legacy"/> is null, only records that there was
    /// nothing to import.
    /// </summary>
    /// <returns>What was added, and whether the <c>Include</c> line had to be added to <c>~/.ssh/config</c>.</returns>
    /// <exception cref="IOException">A file can't be read or written: nothing is recorded, so it can be tried again.</exception>
    /// <exception cref="UnauthorizedAccessException">A file can't be read or written.</exception>
    public (LegacyImportResult Result, bool IncludeAdded) ImportLegacy(LegacyData? legacy, string? source)
    {
        LegacyImportResult? result = null;
        var includeAdded = false;
        try
        {
            Store.Update(data =>
            {
                LegacyImportResult imported;
                if (legacy is null)
                {
                    imported = new LegacyImportResult(data, [], 0, 0);
                }
                else
                {
                    lock (_hostsLock)
                    {
                        var managed = Files.ReadManaged();
                        // Each host is checked by ssh first: one it would refuse stays a command instead of
                        // making the whole file (and every ssh connection) fail.
                        imported = LegacyImporter.Import(
                            data, managed, SshConfigParser.Load(Files.MainConfigPath), legacy, host => Files.Validate(host) is null);
                        if (imported.AddedHosts.Count > 0)
                        {
                            includeAdded = Files.Save(managed);
                        }
                    }
                }

                result = imported;
                return imported.Data with
                {
                    LegacyImport = new LegacyImportInfo(DateTimeOffset.Now, source, imported.AddedHosts.Count, imported.AddedCommands, imported.AddedTerminals),
                };
            });
        }
        finally
        {
            SshConfig.Invalidate();
            HostsChanged?.Invoke(this, EventArgs.Empty);
        }

        return (result!, includeAdded);
    }

    /// <summary>Adds the <c>Include</c> line to <c>~/.ssh/config</c> when it is missing.</summary>
    public bool TryEnsureIncluded()
    {
        try
        {
            Files.EnsureIncluded();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Api.LogException(LogClass, "Could not change ~/.ssh/config", e);
            Api.ShowMsgError(T("plugin_easyssh_save_error_title"), e.Message);
            return false;
        }
        finally
        {
            SshConfig.Invalidate();
            HostsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Opens a file in its default application (Notepad for files without one, like ssh_config files).</summary>
    public void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            Api.ShowMsgError(T("plugin_easyssh_file_missing_title"), path);
            return;
        }

        try
        {
            StartProcess(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            try
            {
                StartProcess(new ProcessStartInfo("notepad.exe", CommandLine.Quote(path)) { UseShellExecute = true });
            }
            catch (Exception fallbackError) when (fallbackError is Win32Exception or InvalidOperationException)
            {
                Api.LogException(LogClass, $"Could not open {path}", fallbackError);
                Api.ShowMsgError(T("plugin_easyssh_file_missing_title"), fallbackError.Message);
            }
        }
    }

    /// <summary>Shows a notification; a failure to show it must not break the plugin.</summary>
    public void Notify(string message, string icon)
    {
        try
        {
            Api.ShowMsg(T("plugin_easyssh_plugin_name"), message, icon);
        }
        catch (Exception e)
        {
            Log("Could not show a notification", e);
        }
    }

    /// <summary>Writes an exception to Flow Launcher's log.</summary>
    public void Log(string message, Exception e) => Api.LogException(LogClass, message, e);

    /// <summary>A graphical program such as PuTTY is started directly: in a terminal, an empty console would stay open.</summary>
    private static LaunchPlan? GraphicalProgramPlan(string command)
    {
        var (program, arguments) = CommandLine.SplitFirst(command);
        return ExecutableResolver.Resolve(program) is { } path && ExecutableResolver.IsGuiApplication(path)
            ? new LaunchPlan(path, arguments)
            : null;
    }
}
