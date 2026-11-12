using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Flow.Launcher.Plugin.EasySsh.Core;
using Flow.Launcher.Plugin.EasySsh.Settings;
using Flow.Launcher.Plugin.EasySsh.Storage;

namespace Flow.Launcher.Plugin.EasySsh;

/// <summary>EasySSH: search, save and open SSH connections from Flow Launcher, with the hosts of ~/.ssh/config.</summary>
public sealed class Main : IAsyncPlugin, IContextMenu, IPluginI18n, ISettingProvider, IAsyncReloadable
{
    private const string LogClass = "EasySSH";

    private IPublicAPI? _api;
    private string _dataDirectory = string.Empty;
    private string _legacyPath = LegacyImporter.DefaultPath;
    private string _sshConfigPath = SshConfigParser.DefaultConfigPath;
    private PluginServices? _services;
    private QueryHandler? _handler;
    private Exception? _initError;

    /// <summary>Shared services, null when initialization failed.</summary>
    internal PluginServices? Services => _services;

    /// <summary>Why initialization failed, if it did.</summary>
    internal Exception? InitError => _initError;

    /// <inheritdoc />
    public Task InitAsync(PluginInitContext context)
    {
        var settingsDirectory = context.CurrentPluginMetadata?.PluginSettingsDirectoryPath;
        _dataDirectory = string.IsNullOrWhiteSpace(settingsDirectory) ? FallbackDataDirectory() : settingsDirectory;
        Initialize(context.API, _dataDirectory, LegacyImporter.DefaultPath, SshConfigParser.DefaultConfigPath);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<List<Result>> QueryAsync(Query query, CancellationToken token) =>
        Task.FromResult(Answer(query.Search ?? string.Empty, query.ActionKeyword ?? string.Empty));

    /// <inheritdoc />
    public List<Result> LoadContextMenus(Result selectedResult)
    {
        try
        {
            return _handler?.ContextMenu(selectedResult) ?? [];
        }
        catch (Exception e)
        {
            _api?.LogException(LogClass, "Context menu failed", e);
            return [];
        }
    }

    /// <inheritdoc />
    public Control CreateSettingPanel()
    {
        if (_services is not null)
        {
            return new SettingsPanel(new SettingsViewModel(_services));
        }

        return new UserControl
        {
            Content = new TextBlock
            {
                Margin = new Thickness(70, 14, 18, 14),
                TextWrapping = TextWrapping.Wrap,
                Text = $"{Translate("plugin_easyssh_init_error_title")}\n{_initError?.Message}",
            },
        };
    }

    /// <inheritdoc />
    public Task ReloadDataAsync()
    {
        if (_services is null)
        {
            // Initialization failed (e.g. the data file was locked): try again.
            if (_api is not null)
            {
                Initialize(_api, _dataDirectory, _legacyPath, _sshConfigPath);
            }

            return Task.CompletedTask;
        }

        try
        {
            _services.Store.Reload();
            ReportLoadStatus(_services);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _services.Log("Reload failed", e);
            _api?.ShowMsgError(Translate("plugin_easyssh_init_error_title"), e.Message);
        }

        _services.SshConfig.Invalidate();
        _services.OpenSshInstalled = ExecutableResolver.IsOpenSshInstalled();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string GetTranslatedPluginTitle() => Translate("plugin_easyssh_plugin_name", "EasySSH");

    /// <inheritdoc />
    public string GetTranslatedPluginDescription() =>
        Translate("plugin_easyssh_plugin_description", "Search, save and open SSH connections, with the hosts of your ~/.ssh/config.");

    /// <summary>Sets the plugin up. Never throws: a failure is reported by every query instead.</summary>
    /// <param name="api">Flow Launcher's API.</param>
    /// <param name="dataDirectory">Folder of the plugin data (<c>easyssh.json</c>).</param>
    /// <param name="legacyPath">Data file of EasySSH 1.x.</param>
    /// <param name="sshConfigPath">The ssh configuration (<c>~/.ssh/config</c>); its folder holds the other ssh files.</param>
    internal void Initialize(IPublicAPI api, string dataDirectory, string legacyPath, string sshConfigPath)
    {
        _api = api;
        _dataDirectory = dataDirectory;
        _legacyPath = legacyPath;
        _sshConfigPath = sshConfigPath;
        try
        {
            var store = DataStore.Open(dataDirectory);
            var files = new SshConfigFiles(sshConfigPath);
            var services = new PluginServices(
                api, store, files, ExecutableResolver.IsOpenSshInstalled(), legacyPath, Path.Combine(files.SshDirectory, "known_hosts"));
            ReportLoadStatus(services);
            ImportLegacyDataOnce(services, legacyPath);
            _services = services;
            _handler = new QueryHandler(services);
            _initError = null;
        }
        catch (Exception e)
        {
            // Whatever happens, Flow Launcher must keep loading the plugin: show the error in the results.
            _initError = e;
            api.LogException(LogClass, "Initialization failed", e);
        }
    }

    /// <summary>Answers a query (Flow Launcher's <see cref="Query"/> can't be built outside of it).</summary>
    internal List<Result> Answer(string search, string actionKeyword)
    {
        if (_handler is null)
        {
            return [InitErrorResult()];
        }

        try
        {
            return _handler.Handle(search, actionKeyword);
        }
        catch (Exception e)
        {
            _api?.LogException(LogClass, $"Query failed: {search}", e);
            return
            [
                new Result
                {
                    Title = Translate("plugin_easyssh_query_error_title"),
                    SubTitle = e.Message,
                    IcoPath = Icons.Warning,
                    Glyph = Glyphs.Warning,
                },
            ];
        }
    }

    /// <summary>The message telling what an import did.</summary>
    internal static string DescribeImport(PluginServices services, LegacyImportResult result, bool includeAdded, string legacyPath)
    {
        var message = services.T(
            "plugin_easyssh_import_done",
            result.AddedHosts.Count,
            services.DisplayPath(services.Files.ManagedPath),
            result.AddedCommands,
            result.AddedTerminals,
            services.DisplayPath(legacyPath));
        return includeAdded ? message + "\n" + services.T("plugin_easyssh_include_added", services.DisplayPath(services.Files.MainConfigPath)) : message;
    }

    private static void ReportLoadStatus(PluginServices services)
    {
        var store = services.Store;
        switch (store.LoadStatus)
        {
            case DataLoadStatus.RestoredFromBackup:
                services.Notify(services.T("plugin_easyssh_data_restored", store.DamagedFileCopy ?? store.FilePath), Icons.Warning);
                break;
            case DataLoadStatus.ResetAfterCorruption:
                services.Notify(services.T("plugin_easyssh_data_reset", store.DamagedFileCopy ?? store.FilePath), Icons.Warning);
                break;
        }
    }

    /// <summary>
    /// On the first start of this version, brings the profiles and terminals of EasySSH 1.x in: the profiles
    /// become hosts of <c>~/.ssh/easyssh.config</c> when possible. The old file is left untouched.
    /// If something can't be read or written right now, the next start tries again.
    /// </summary>
    private static void ImportLegacyDataOnce(PluginServices services, string legacyPath)
    {
        if (services.Store.Data.LegacyImport is not null)
        {
            return;
        }

        LegacyData? legacy;
        try
        {
            legacy = LegacyImporter.Read(legacyPath);
        }
        catch (JsonException e)
        {
            services.Log($"Ignoring {legacyPath}: not valid JSON", e);
            legacy = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            services.Log($"Could not read {legacyPath}, will try again on next start", e);
            return;
        }

        try
        {
            var (result, includeAdded) = services.ImportLegacy(legacy, legacy is null ? null : legacyPath);
            if (result.AddedAnything)
            {
                services.Notify(DescribeImport(services, result, includeAdded, legacyPath), Icons.Selected);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            services.Log("Could not import the data of EasySSH 1.x, will try again on next start", e);
        }
    }

    private Result InitErrorResult() => new()
    {
        Title = Translate("plugin_easyssh_init_error_title"),
        SubTitle = Translate("plugin_easyssh_init_error_subtitle") + " " + _initError?.Message,
        SubTitleToolTip = _initError?.ToString(),
        IcoPath = Icons.Warning,
        Glyph = Glyphs.Warning,
        Action = _ =>
        {
            if (_api is not null && Directory.Exists(_dataDirectory))
            {
                _api.OpenDirectory(_dataDirectory);
            }

            return true;
        },
    };

    private string Translate(string key, string? fallback = null)
    {
        var text = _api?.GetTranslation(key);
        return string.IsNullOrEmpty(text) || text.Contains(key, StringComparison.Ordinal) ? fallback ?? text ?? key : text;
    }

    /// <summary>Used only if Flow Launcher doesn't give a settings folder.</summary>
    private static string FallbackDataDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FlowLauncher", "Settings", "Plugins", "Flow.Launcher.Plugin.easyssh");
}
