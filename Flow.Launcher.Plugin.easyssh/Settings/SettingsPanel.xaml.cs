using System.Windows;
using System.Windows.Controls;
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

    private void AddProfile_OnClick(object sender, RoutedEventArgs e) => _viewModel.NewProfile();

    private void EditProfile_OnClick(object sender, RoutedEventArgs e) => _viewModel.EditProfile();

    private void DeleteProfile_OnClick(object sender, RoutedEventArgs e) => _viewModel.DeleteProfile();

    private void SaveProfile_OnClick(object sender, RoutedEventArgs e) => _viewModel.SaveProfile();

    private void CancelProfile_OnClick(object sender, RoutedEventArgs e) => _viewModel.CancelProfile();

    private void OpenSshConfig_OnClick(object sender, RoutedEventArgs e) => _viewModel.OpenSshConfig();

    private void OpenDataFolder_OnClick(object sender, RoutedEventArgs e) => _viewModel.OpenDataFolder();

    private void ImportLegacy_OnClick(object sender, RoutedEventArgs e) => _viewModel.ImportLegacy();
}
