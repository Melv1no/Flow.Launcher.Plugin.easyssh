using System.Windows;
using System.Windows.Controls;
using Flow.Launcher.Plugin.EasySsh.Core;
using Microsoft.Win32;

namespace Flow.Launcher.Plugin.EasySsh.Settings;

/// <summary>Settings shown in Flow Launcher → Settings → Plugins → EasySSH.</summary>
internal partial class SettingsPanel : UserControl
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPanel(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => _viewModel.Attach();
        Unloaded += (_, _) => _viewModel.Detach();
    }

    private void AddTerminal_OnClick(object sender, RoutedEventArgs e) => _viewModel.NewTerminal();

    private void EditTerminal_OnClick(object sender, RoutedEventArgs e) => _viewModel.EditTerminal();

    private void DeleteTerminal_OnClick(object sender, RoutedEventArgs e) => _viewModel.DeleteTerminal();

    private void SaveTerminal_OnClick(object sender, RoutedEventArgs e) => _viewModel.SaveTerminal();

    private void CancelTerminal_OnClick(object sender, RoutedEventArgs e) => _viewModel.CancelTerminal();

    private void BrowseExecutable_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Programs (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            _viewModel.TerminalExecutable = dialog.FileName;
        }
    }

    private void AddHost_OnClick(object sender, RoutedEventArgs e) => _viewModel.NewHost();

    private void EditHost_OnClick(object sender, RoutedEventArgs e) => _viewModel.EditHost();

    private void DeleteHost_OnClick(object sender, RoutedEventArgs e) => _viewModel.DeleteHost();

    private void ToggleHidden_OnClick(object sender, RoutedEventArgs e) => _viewModel.ToggleHidden();

    private void OpenHostFile_OnClick(object sender, RoutedEventArgs e) => _viewModel.OpenHostFile();

    private void SaveHost_OnClick(object sender, RoutedEventArgs e) => _viewModel.SaveHost();

    private void CancelHost_OnClick(object sender, RoutedEventArgs e) => _viewModel.CancelHost();

    private void BrowseIdentityFile_OnClick(object sender, RoutedEventArgs e)
    {
        var sshDirectory = SshConfigParser.DefaultSshDirectory;
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(sshDirectory) ? sshDirectory : string.Empty,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            _viewModel.HostIdentityFile = SshConfigText.NormalizePath(dialog.FileName);
        }
    }

    private void AddCommand_OnClick(object sender, RoutedEventArgs e) => _viewModel.NewCommand();

    private void EditCommand_OnClick(object sender, RoutedEventArgs e) => _viewModel.EditCommand();

    private void DeleteCommand_OnClick(object sender, RoutedEventArgs e) => _viewModel.DeleteCommand();

    private void SaveCommand_OnClick(object sender, RoutedEventArgs e) => _viewModel.SaveCommand();

    private void CancelCommand_OnClick(object sender, RoutedEventArgs e) => _viewModel.CancelCommand();

    private void OpenMainConfig_OnClick(object sender, RoutedEventArgs e) => _viewModel.OpenMainConfig();

    private void OpenManagedConfig_OnClick(object sender, RoutedEventArgs e) => _viewModel.OpenManagedConfig();

    private void OpenDataFolder_OnClick(object sender, RoutedEventArgs e) => _viewModel.OpenDataFolder();

    private void ImportLegacy_OnClick(object sender, RoutedEventArgs e) => _viewModel.ImportLegacy();
}
