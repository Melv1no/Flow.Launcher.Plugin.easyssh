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
    private SshProfile? _selectedProfile;
    private bool _isEditingProfile;
    private string? _profileBeingEdited;
    private string _profileName = string.Empty;
    private string _profileCommand = string.Empty;
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

    public bool ShowSshConfigHosts
    {
        get => _services.Store.Data.ShowSshConfigHosts;
        set
        {
            if (!_refreshing && value != ShowSshConfigHosts)
            {
                Apply(data => data with { ShowSshConfigHosts = value });
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
        if (SelectedCustomTerminal is not { } terminal || !Confirm(terminal.Name))
        {
            return;
        }

        if (Apply(data => data.RemoveTerminal(terminal.Name)))
        {
            IsEditingTerminal = false;
            Status = T("plugin_easyssh_settings_deleted", terminal.Name);
        }
    }

    // ------------------------------------------------------------------ profiles

    public ObservableCollection<SshProfile> Profiles { get; } = [];

    public SshProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetField(ref _selectedProfile, value))
            {
                OnPropertyChanged(nameof(HasSelectedProfile));
            }
        }
    }

    public bool HasSelectedProfile => _selectedProfile is not null;

    public bool IsEditingProfile
    {
        get => _isEditingProfile;
        private set => SetField(ref _isEditingProfile, value);
    }

    public string ProfileName
    {
        get => _profileName;
        set => SetField(ref _profileName, value);
    }

    public string ProfileCommand
    {
        get => _profileCommand;
        set => SetField(ref _profileCommand, value);
    }

    public void NewProfile()
    {
        _profileBeingEdited = null;
        ProfileName = string.Empty;
        ProfileCommand = string.Empty;
        IsEditingProfile = true;
        Status = string.Empty;
    }

    public void EditProfile()
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        _profileBeingEdited = profile.Name;
        ProfileName = profile.Name;
        ProfileCommand = profile.Command;
        IsEditingProfile = true;
        Status = string.Empty;
    }

    public bool SaveProfile()
    {
        var name = ProfileName.Trim();
        var command = SshCommand.Normalize(ProfileCommand);
        if (name.Length == 0)
        {
            return Fail(T("plugin_easyssh_settings_error_name"));
        }

        if (command.Length == 0)
        {
            return Fail(T("plugin_easyssh_settings_error_command"));
        }

        var renamedOnto = !PluginDataOperations.NameComparer.Equals(name, _profileBeingEdited ?? name);
        var exists = _services.Store.Data.FindProfile(name) is not null;
        if ((_profileBeingEdited is null || renamedOnto) && exists)
        {
            return Fail(T("plugin_easyssh_settings_error_duplicate", name));
        }

        if (!Apply(data => data.UpsertProfile(new SshProfile(name, command), _profileBeingEdited)))
        {
            return false;
        }

        IsEditingProfile = false;
        Status = T("plugin_easyssh_settings_saved");
        return true;
    }

    public void CancelProfile() => IsEditingProfile = false;

    public void DeleteProfile()
    {
        if (SelectedProfile is not { } profile || !Confirm(profile.Name))
        {
            return;
        }

        if (Apply(data => data.RemoveProfile(profile.Name)))
        {
            IsEditingProfile = false;
            Status = T("plugin_easyssh_settings_deleted", profile.Name);
        }
    }

    // ------------------------------------------------------------------ files & import

    public string DataFilePath => _services.Store.FilePath;

    public string SshConfigPath => _services.SshConfig.ConfigPath;

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

        var added = (Profiles: 0, Terminals: 0);
        if (Apply(data =>
            {
                var merged = LegacyImporter.Merge(data, legacy);
                added = (merged.AddedProfiles, merged.AddedTerminals);
                return merged.Data;
            }))
        {
            Status = T("plugin_easyssh_import_done", added.Profiles, added.Terminals, path);
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

    public void OpenSshConfig() => _services.OpenFile(SshConfigPath);

    // ------------------------------------------------------------------ synchronization

    /// <summary>Starts following changes made elsewhere (e.g. from the search bar).</summary>
    public void Attach()
    {
        _services.Store.Changed -= OnStoreChanged;
        _services.Store.Changed += OnStoreChanged;
        _attached = true;
        Refresh();
    }

    /// <summary>Stops following changes (the panel is closed).</summary>
    public void Detach()
    {
        _services.Store.Changed -= OnStoreChanged;
        _attached = false;
    }

    /// <summary>Reloads the lists from the store, keeping the selections.</summary>
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

            var profileName = SelectedProfile?.Name;
            Profiles.Clear();
            foreach (var profile in data.Profiles.OrderBy(profile => TextFolding.Fold(profile.Name), StringComparer.Ordinal))
            {
                Profiles.Add(profile);
            }

            SelectedProfile = Profiles.FirstOrDefault(profile => profile.Name == profileName);

            OnPropertyChanged(nameof(KeepWindowOpenOnError));
            OnPropertyChanged(nameof(ShowSshConfigHosts));
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void OnStoreChanged(object? sender, EventArgs e)
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

    private bool Apply(Func<PluginData, PluginData> change)
    {
        try
        {
            _services.Store.Update(change);
            if (!_attached)
            {
                // When attached, the Changed event already refreshes the lists.
                Refresh();
            }

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

    private bool Confirm(string name) =>
        _services.Api.ShowMsgBox(
            T("plugin_easyssh_settings_delete_confirm", name),
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
