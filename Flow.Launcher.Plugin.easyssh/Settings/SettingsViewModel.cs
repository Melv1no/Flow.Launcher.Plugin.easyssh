using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Flow.Launcher.Plugin.EasySsh.Core;
using Flow.Launcher.Plugin.EasySsh.Storage;

namespace Flow.Launcher.Plugin.EasySsh.Settings;

/// <summary>A terminal as listed in the settings.</summary>
/// <param name="Terminal">The terminal.</param>
/// <param name="Label">Display name, with a note when the program can't be found.</param>
internal sealed record TerminalOption(TerminalDefinition Terminal, string Label);

/// <summary>A host as listed in the settings.</summary>
/// <param name="Host">The host.</param>
/// <param name="Target">Where it connects (<c>admin@10.0.0.5:2222</c>).</param>
/// <param name="File">The file declaring it, for display.</param>
/// <param name="State">"Hidden", or empty.</param>
/// <param name="IsManaged">Whether EasySSH can change it (it is declared in <c>easyssh.config</c>).</param>
/// <param name="IsHidden">Whether it is hidden from the results.</param>
internal sealed record HostRow(SshConfigHost Host, string Target, string File, string State, bool IsManaged, bool IsHidden)
{
    public string Alias => Host.Alias;
}

/// <summary>State and actions of the settings panel.</summary>
internal sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly PluginServices _services;
    private readonly Dispatcher _dispatcher;
    private bool _attached;
    private bool _refreshing;
    private TerminalOption? _selectedTerminal;
    private TerminalDefinition? _selectedCustomTerminal;
    private bool _isEditingTerminal;
    private string? _terminalBeingEdited;
    private string _terminalName = string.Empty;
    private string _terminalExecutable = string.Empty;
    private string _terminalArguments = string.Empty;
    private HostRow? _selectedHost;
    private bool _isEditingHost;
    private string? _hostBeingEdited;
    private string _hostAlias = string.Empty;
    private string _hostName = string.Empty;
    private string _hostUser = string.Empty;
    private string _hostPort = string.Empty;
    private string _hostIdentityFile = string.Empty;
    private string _hostProxyJump = string.Empty;
    private string _hostOtherOptions = string.Empty;
    private SavedCommand? _selectedCommand;
    private bool _isEditingCommand;
    private string? _commandBeingEdited;
    private string _commandName = string.Empty;
    private string _commandText = string.Empty;
    private string _status = string.Empty;

    public SettingsViewModel(PluginServices services)
    {
        _services = services;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ------------------------------------------------------------------ terminal & options

    public ObservableCollection<TerminalOption> TerminalOptions { get; } = [];

    public TerminalOption? SelectedTerminal
    {
        get => _selectedTerminal;
        set
        {
            if (Equals(_selectedTerminal, value))
            {
                return;
            }

            _selectedTerminal = value;
            OnPropertyChanged();
            if (!_refreshing && value is not null)
            {
                Apply(data => data.SelectTerminal(value.Terminal.Name));
            }
        }
    }

    public bool KeepWindowOpenOnError
    {
        get => _services.Store.Data.KeepWindowOpenOnError;
        set
        {
            if (!_refreshing && value != KeepWindowOpenOnError)
            {
                Apply(data => data with { KeepWindowOpenOnError = value });
            }

            OnPropertyChanged();
        }
    }

    public bool SuggestKnownHosts
    {
        get => _services.Store.Data.SuggestKnownHosts;
        set
        {
            if (!_refreshing && value != SuggestKnownHosts)
            {
                Apply(data => data with { SuggestKnownHosts = value });
            }

            OnPropertyChanged();
        }
    }

    // ------------------------------------------------------------------ custom terminals

    public ObservableCollection<TerminalDefinition> CustomTerminals { get; } = [];

    public TerminalDefinition? SelectedCustomTerminal
    {
        get => _selectedCustomTerminal;
        set
        {
            if (SetField(ref _selectedCustomTerminal, value))
            {
                OnPropertyChanged(nameof(HasSelectedCustomTerminal));
            }
        }
    }

    public bool HasSelectedCustomTerminal => _selectedCustomTerminal is not null;

    public bool IsEditingTerminal
    {
        get => _isEditingTerminal;
        private set => SetField(ref _isEditingTerminal, value);
    }

    public string TerminalName
    {
        get => _terminalName;
        set => SetField(ref _terminalName, value);
    }

    public string TerminalExecutable
    {
        get => _terminalExecutable;
        set => SetField(ref _terminalExecutable, value);
    }

    public string TerminalArguments
    {
        get => _terminalArguments;
        set => SetField(ref _terminalArguments, value);
    }

    public void NewTerminal()
    {
        _terminalBeingEdited = null;
        TerminalName = string.Empty;
        TerminalExecutable = string.Empty;
        TerminalArguments = LaunchPlanBuilder.CommandPlaceholder;
        IsEditingTerminal = true;
        Status = string.Empty;
    }

    public void EditTerminal()
    {
        if (SelectedCustomTerminal is not { } terminal)
        {
            return;
        }

        _terminalBeingEdited = terminal.Name;
        TerminalName = terminal.Name;
        TerminalExecutable = terminal.Executable;
        TerminalArguments = terminal.Arguments;
        IsEditingTerminal = true;
        Status = string.Empty;
    }

    public bool SaveTerminal()
    {
        var name = TerminalName.Trim();
        if (name.Length == 0)
        {
            return Fail(T("plugin_easyssh_settings_error_name"));
        }

        if (TerminalExecutable.Trim().Length == 0)
        {
            return Fail(T("plugin_easyssh_settings_error_program"));
        }

        if (BuiltInTerminals.IsReservedName(name))
        {
            return Fail(T("plugin_easyssh_terminal_reserved", name));
        }

        var renamedOnto = !PluginDataOperations.NameComparer.Equals(name, _terminalBeingEdited ?? name);
        var exists = _services.Store.Data.Terminals.Any(terminal => PluginDataOperations.NameComparer.Equals(terminal.Name, name));
        if ((_terminalBeingEdited is null || renamedOnto) && exists)
        {
            return Fail(T("plugin_easyssh_settings_error_duplicate", name));
        }

        var terminal = new TerminalDefinition(name, TerminalExecutable.Trim(), TerminalArguments.Trim());
        if (!Apply(data => data.UpsertTerminal(terminal, _terminalBeingEdited)))
        {
            return false;
        }

        IsEditingTerminal = false;
        Status = PluginServices.IsAvailable(terminal)
            ? T("plugin_easyssh_settings_saved")
            : T("plugin_easyssh_terminal_add_not_found", terminal.Executable);
        return true;
    }

    public void CancelTerminal() => IsEditingTerminal = false;

    public void DeleteTerminal()
    {
        if (SelectedCustomTerminal is not { } terminal || !Confirm(T("plugin_easyssh_settings_delete_confirm", terminal.Name)))
        {
            return;
        }

        if (Apply(data => data.RemoveTerminal(terminal.Name)))
        {
            IsEditingTerminal = false;
            Status = T("plugin_easyssh_settings_deleted", terminal.Name);
        }
    }

    // ------------------------------------------------------------------ hosts

    public ObservableCollection<HostRow> Hosts { get; } = [];

    public HostRow? SelectedHost
    {
        get => _selectedHost;
        set
        {
            if (SetField(ref _selectedHost, value))
            {
                OnPropertyChanged(nameof(HasSelectedHost));
                OnPropertyChanged(nameof(CanDeleteHost));
                OnPropertyChanged(nameof(HideButtonLabel));
            }
        }
    }

    public bool HasSelectedHost => _selectedHost is not null;

    /// <summary>Only hosts of easyssh.config can be deleted from here; the others are in the user's own files.</summary>
    public bool CanDeleteHost => _selectedHost is { IsManaged: true };

    public string HideButtonLabel => T(_selectedHost is { IsHidden: true } ? "plugin_easyssh_settings_show" : "plugin_easyssh_settings_hide");

    public bool IsEditingHost
    {
        get => _isEditingHost;
        private set => SetField(ref _isEditingHost, value);
    }

    public string HostAlias
    {
        get => _hostAlias;
        set => SetField(ref _hostAlias, value);
    }

    public string HostName
    {
        get => _hostName;
        set => SetField(ref _hostName, value);
    }

    public string HostUser
    {
        get => _hostUser;
        set => SetField(ref _hostUser, value);
    }

    public string HostPort
    {
        get => _hostPort;
        set => SetField(ref _hostPort, value);
    }

    public string HostIdentityFile
    {
        get => _hostIdentityFile;
        set => SetField(ref _hostIdentityFile, value);
    }

    public string HostProxyJump
    {
        get => _hostProxyJump;
        set => SetField(ref _hostProxyJump, value);
    }

    /// <summary>Other options, one <c>Keyword value</c> per line.</summary>
    public string HostOtherOptions
    {
        get => _hostOtherOptions;
        set => SetField(ref _hostOtherOptions, value);
    }

    public void NewHost()
    {
        _hostBeingEdited = null;
        HostAlias = string.Empty;
        HostName = string.Empty;
        HostUser = string.Empty;
        HostPort = string.Empty;
        HostIdentityFile = string.Empty;
        HostProxyJump = string.Empty;
        HostOtherOptions = string.Empty;
        IsEditingHost = true;
        Status = string.Empty;
    }

    /// <summary>Edits a host of easyssh.config; a host of another file is opened in the text editor instead.</summary>
    public void EditHost()
    {
        if (SelectedHost is not { } row)
        {
            return;
        }

        SshHostEntry? entry = null;
        if (row.IsManaged)
        {
            try
            {
                entry = _services.Files.ReadManaged().Find(row.Alias);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Fail(e.Message);
                return;
            }
        }

        if (entry is null)
        {
            _services.OpenFile(row.Host.SourceFile);
            Status = T("plugin_easyssh_settings_opened_file", row.Alias, row.File);
            return;
        }

        _hostBeingEdited = entry.Alias;
        HostAlias = entry.Alias;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var others = new List<string>();
        foreach (var option in entry.Options)
        {
            if (EditorKeywords.Contains(option.Keyword) && !fields.ContainsKey(option.Keyword))
            {
                fields[option.Keyword] = option.FirstValue;
            }
            else
            {
                others.Add(option.ToString());
            }
        }

        HostName = fields.GetValueOrDefault("HostName", string.Empty);
        HostUser = fields.GetValueOrDefault("User", string.Empty);
        HostPort = fields.GetValueOrDefault("Port", string.Empty);
        HostIdentityFile = fields.GetValueOrDefault("IdentityFile", string.Empty);
        HostProxyJump = fields.GetValueOrDefault("ProxyJump", string.Empty);
        HostOtherOptions = string.Join(Environment.NewLine, others);
        IsEditingHost = true;
        Status = string.Empty;
    }

    public bool SaveHost()
    {
        var alias = HostAlias.Trim();
        if (!SshConfigText.IsValidAlias(alias))
        {
            return Fail(T("plugin_easyssh_settings_error_alias"));
        }

        var port = HostPort.Trim();
        if (port.Length > 0 && !SshCommandConverter.IsPort(port))
        {
            return Fail(T("plugin_easyssh_settings_error_port"));
        }

        foreach (var (label, value) in new[] { ("HostName", HostName), ("ProxyJump", HostProxyJump), ("User", HostUser), ("IdentityFile", HostIdentityFile) })
        {
            var spacesAllowed = label is "User" or "IdentityFile";
            if (value.Contains('"') || (!spacesAllowed && value.Trim().Any(char.IsWhiteSpace)))
            {
                return Fail(T("plugin_easyssh_settings_error_value", label));
            }
        }

        var existing = _services.SshConfig.GetSnapshot().Find(alias);
        var isNewName = _hostBeingEdited is null || !alias.Equals(_hostBeingEdited, StringComparison.OrdinalIgnoreCase);
        if (isNewName && existing is not null)
        {
            return Fail(T("plugin_easyssh_settings_error_host_exists", alias, _services.DisplayPath(existing.SourceFile)));
        }

        var options = new List<SshOption>();
        AddField(options, "HostName", HostName);
        AddField(options, "User", HostUser);
        AddField(options, "Port", port);
        AddField(options, "IdentityFile", HostIdentityFile.Trim().Length > 0 ? SshConfigText.NormalizePath(HostIdentityFile) : string.Empty);
        AddField(options, "ProxyJump", HostProxyJump);
        foreach (var line in HostOtherOptions.Split('\n'))
        {
            if (SshConfigText.IsTrivia(line))
            {
                continue;
            }

            var (keyword, value) = SshConfigText.SplitDirective(line);
            var option = new SshOption(keyword, value);
            if (!ManagedConfig.IsValidOption(option))
            {
                return Fail(T("plugin_easyssh_settings_error_option", line.Trim()));
            }

            options.Add(option);
        }

        var host = new SshHostEntry(alias, options);
        var error = _services.ChangeHosts(config => config.Upsert(host, _hostBeingEdited));
        if (error is not null)
        {
            return Fail(error);
        }

        IsEditingHost = false;
        RefreshIfDetached();
        Status = T("plugin_easyssh_settings_host_saved", alias, _services.DisplayPath(_services.Files.ManagedPath));
        return true;
    }

    public void CancelHost() => IsEditingHost = false;

    public void DeleteHost()
    {
        if (SelectedHost is not { IsManaged: true } row
            || !Confirm(T("plugin_easyssh_remove_host_confirm", row.Alias, row.File)))
        {
            return;
        }

        var error = _services.ChangeHosts(config =>
        {
            if (!config.Remove(row.Alias))
            {
                throw new ArgumentException(T("plugin_easyssh_host_not_managed", row.Alias, ManagedConfigPath));
            }
        });
        if (error is not null)
        {
            Fail(error);
            return;
        }

        IsEditingHost = false;
        RefreshIfDetached();
        Status = T("plugin_easyssh_settings_deleted", row.Alias);
    }

    public void ToggleHidden()
    {
        if (SelectedHost is { } row && Apply(data => data.SetHidden(row.Alias, !row.IsHidden)))
        {
            Status = T(row.IsHidden ? "plugin_easyssh_settings_shown" : "plugin_easyssh_settings_hidden", row.Alias);
        }
    }

    public void OpenHostFile()
    {
        if (SelectedHost is { } row)
        {
            _services.OpenFile(row.Host.SourceFile);
        }
    }

    // ------------------------------------------------------------------ saved commands

    public ObservableCollection<SavedCommand> Commands { get; } = [];

    public SavedCommand? SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            if (SetField(ref _selectedCommand, value))
            {
                OnPropertyChanged(nameof(HasSelectedCommand));
            }
        }
    }

    public bool HasSelectedCommand => _selectedCommand is not null;

    public bool IsEditingCommand
    {
        get => _isEditingCommand;
        private set => SetField(ref _isEditingCommand, value);
    }

    public string CommandName
    {
        get => _commandName;
        set => SetField(ref _commandName, value);
    }

    public string CommandText
    {
        get => _commandText;
        set => SetField(ref _commandText, value);
    }

    public void NewCommand()
    {
        _commandBeingEdited = null;
        CommandName = string.Empty;
        CommandText = string.Empty;
        IsEditingCommand = true;
        Status = string.Empty;
    }

    public void EditCommand()
    {
        if (SelectedCommand is not { } command)
        {
            return;
        }

        _commandBeingEdited = command.Name;
        CommandName = command.Name;
        CommandText = command.Command;
        IsEditingCommand = true;
        Status = string.Empty;
    }

    public bool SaveCommand()
    {
        var name = CommandName.Trim();
        var command = SshCommand.Normalize(CommandText);
        if (name.Length == 0)
        {
            return Fail(T("plugin_easyssh_settings_error_name"));
        }

        if (command.Length == 0)
        {
            return Fail(T("plugin_easyssh_settings_error_command"));
        }

        var renamedOnto = !PluginDataOperations.NameComparer.Equals(name, _commandBeingEdited ?? name);
        if ((_commandBeingEdited is null || renamedOnto) && _services.Store.Data.FindCommand(name) is not null)
        {
            return Fail(T("plugin_easyssh_settings_error_duplicate", name));
        }

        if (!Apply(data => data.UpsertCommand(new SavedCommand(name, command), _commandBeingEdited)))
        {
            return false;
        }

        IsEditingCommand = false;
        Status = T("plugin_easyssh_settings_saved");
        return true;
    }

    public void CancelCommand() => IsEditingCommand = false;

    public void DeleteCommand()
    {
        if (SelectedCommand is not { } command || !Confirm(T("plugin_easyssh_settings_delete_confirm", command.Name)))
        {
            return;
        }

        if (Apply(data => data.RemoveCommand(command.Name)))
        {
            IsEditingCommand = false;
            Status = T("plugin_easyssh_settings_deleted", command.Name);
        }
    }

    // ------------------------------------------------------------------ files & import

    public string MainConfigPath => _services.DisplayPath(_services.Files.MainConfigPath);

    public string ManagedConfigPath => _services.DisplayPath(_services.Files.ManagedPath);

    public string DataFilePath => _services.Store.FilePath;

    public string Status
    {
        get => _status;
        private set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => _status.Length > 0;

    public void OpenMainConfig() => _services.OpenFile(_services.Files.MainConfigPath);

    public void OpenManagedConfig() => _services.OpenFile(_services.Files.ManagedPath);

    public void ImportLegacy()
    {
        var path = _services.LegacyPath;
        LegacyData? legacy;
        try
        {
            legacy = LegacyImporter.Read(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _services.Log($"Could not import {path}", e);
            Fail(e.Message);
            return;
        }

        if (legacy is null)
        {
            Status = T("plugin_easyssh_settings_nothing_to_import", path);
            return;
        }

        try
        {
            var (result, includeAdded) = _services.ImportLegacy(legacy, path);
            RefreshIfDetached();
            Status = Main.DescribeImport(_services, result, includeAdded, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _services.Log($"Could not import {path}", e);
            Fail(e.Message);
        }
    }

    public void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(_services.Store.DirectoryPath);
            _services.Api.OpenDirectory(_services.Store.DirectoryPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Fail(e.Message);
        }
    }

    // ------------------------------------------------------------------ synchronization

    /// <summary>Starts following changes made elsewhere (e.g. from the search bar).</summary>
    public void Attach()
    {
        _services.Store.Changed -= OnChanged;
        _services.Store.Changed += OnChanged;
        _services.HostsChanged -= OnChanged;
        _services.HostsChanged += OnChanged;
        _attached = true;
        Refresh();
    }

    /// <summary>Stops following changes (the panel is closed).</summary>
    public void Detach()
    {
        _services.Store.Changed -= OnChanged;
        _services.HostsChanged -= OnChanged;
        _attached = false;
    }

    /// <summary>Reloads the lists, keeping the selections.</summary>
    public void Refresh()
    {
        _refreshing = true;
        try
        {
            var data = _services.Store.Data;

            TerminalOptions.Clear();
            foreach (var terminal in data.AllTerminals())
            {
                var label = _services.DisplayName(terminal);
                if (!PluginServices.IsAvailable(terminal))
                {
                    label += " — " + T("plugin_easyssh_terminal_not_found");
                }

                TerminalOptions.Add(new TerminalOption(terminal, label));
            }

            var selectedName = data.GetSelectedTerminal().Name;
            _selectedTerminal = TerminalOptions.FirstOrDefault(option => option.Terminal.Name == selectedName);
            OnPropertyChanged(nameof(SelectedTerminal));

            var customName = SelectedCustomTerminal?.Name;
            CustomTerminals.Clear();
            foreach (var terminal in data.Terminals)
            {
                CustomTerminals.Add(terminal);
            }

            SelectedCustomTerminal = CustomTerminals.FirstOrDefault(terminal => terminal.Name == customName);

            var hostAlias = SelectedHost?.Alias;
            Hosts.Clear();
            var hidden = T("plugin_easyssh_settings_hidden_state");
            foreach (var host in _services.SshConfig.GetHosts().OrderBy(host => TextFolding.Fold(host.Alias), StringComparer.Ordinal))
            {
                var isHidden = data.IsHidden(host.Alias);
                Hosts.Add(new HostRow(
                    host,
                    host.Target ?? string.Empty,
                    _services.DisplayPath(host.SourceFile),
                    isHidden ? hidden : string.Empty,
                    _services.IsManaged(host),
                    isHidden));
            }

            SelectedHost = Hosts.FirstOrDefault(row => row.Alias == hostAlias);

            var commandName = SelectedCommand?.Name;
            Commands.Clear();
            foreach (var command in data.Commands.OrderBy(command => TextFolding.Fold(command.Name), StringComparer.Ordinal))
            {
                Commands.Add(command);
            }

            SelectedCommand = Commands.FirstOrDefault(command => command.Name == commandName);

            OnPropertyChanged(nameof(KeepWindowOpenOnError));
            OnPropertyChanged(nameof(SuggestKnownHosts));
            OnPropertyChanged(nameof(HideButtonLabel));
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static readonly HashSet<string> EditorKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "HostName", "User", "Port", "IdentityFile", "ProxyJump",
    };

    private static void AddField(List<SshOption> options, string keyword, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > 0)
        {
            options.Add(SshOption.Single(keyword, trimmed));
        }
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (_dispatcher.CheckAccess())
        {
            Refresh();
        }
        else
        {
            _dispatcher.BeginInvoke(new Action(Refresh));
        }
    }

    // ------------------------------------------------------------------ helpers

    private void RefreshIfDetached()
    {
        if (!_attached)
        {
            // When attached, the change events already refresh the lists.
            Refresh();
        }
    }

    private bool Apply(Func<PluginData, PluginData> change)
    {
        try
        {
            _services.Store.Update(change);
            RefreshIfDetached();
            return true;
        }
        catch (ArgumentException e)
        {
            return Fail(e.Message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _services.Log("Could not save the settings", e);
            return Fail(e.Message);
        }
    }

    private bool Confirm(string message) =>
        _services.Api.ShowMsgBox(
            message,
            T("plugin_easyssh_plugin_name"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    private bool Fail(string message)
    {
        Status = "⚠ " + message;
        return false;
    }

    private string T(string key) => _services.T(key);

    private string T(string key, params object?[] arguments) => _services.T(key, arguments);

    private bool SetField<TValue>(ref TValue field, TValue value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<TValue>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
