using System.Windows;
using Flow.Launcher.Plugin.EasySsh.Core;
using Flow.Launcher.Plugin.EasySsh.Settings;
using Flow.Launcher.Plugin.EasySsh.Storage;
using NSubstitute;

namespace Flow.Launcher.Plugin.EasySsh.Tests;

public class SettingsTests
{
    [Fact]
    public void The_panel_loads()
    {
        using var fixture = new PluginFixture();
        fixture.WriteConfig("Host prod\n");

        Sta.Run(() =>
        {
            var panel = fixture.Plugin.CreateSettingPanel();
            Assert.IsType<SettingsPanel>(panel);
            panel.Measure(new Size(900, 3000));
            panel.Arrange(new Rect(0, 0, 900, 3000));
        });
    }

    [Fact]
    public void Hosts_can_be_added_renamed_and_deleted()
    {
        using var fixture = new PluginFixture();
        fixture.Api.ShowMsgBox(default!, default!, default, default, default).ReturnsForAnyArgs(MessageBoxResult.Yes);
        var key = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "key");

        Sta.Run(() =>
        {
            var model = new SettingsViewModel(fixture.Services);

            model.NewHost();
            model.HostAlias = "web";
            model.HostName = "10.0.0.5";
            model.HostUser = "admin";
            model.HostPort = "2222";
            model.HostIdentityFile = key;
            model.HostOtherOptions = "LocalForward 8080 localhost:80\r\n\r\nServerAliveInterval=30";
            Assert.True(model.SaveHost(), model.Status);
            Assert.Equal(
                ["HostName 10.0.0.5", "User admin", "Port 2222", "IdentityFile ~/.ssh/key", "LocalForward 8080 localhost:80", "ServerAliveInterval 30"],
                Assert.Single(fixture.ManagedHosts).Options.Select(option => option.ToString()));
            Assert.Contains("Include easyssh.config", File.ReadAllText(fixture.SshConfigPath));
            Assert.Equal("\"web\" saved in ~/.ssh/easyssh.config", model.Status);

            model.SelectedHost = model.Hosts.Single();
            model.EditHost();
            Assert.Equal("~/.ssh/key", model.HostIdentityFile);
            Assert.Equal($"LocalForward 8080 localhost:80{Environment.NewLine}ServerAliveInterval 30", model.HostOtherOptions);
            model.HostAlias = "web-01";
            Assert.True(model.SaveHost(), model.Status);
            Assert.Equal(["web-01"], fixture.ManagedHosts.Select(host => host.Alias));

            model.SelectedHost = model.Hosts.Single();
            Assert.True(model.CanDeleteHost);
            model.DeleteHost();
            Assert.Empty(fixture.ManagedHosts);
        });
    }

    [Fact]
    public void Invalid_hosts_are_explained()
    {
        using var fixture = new PluginFixture();
        fixture.WriteConfig("Host prod\n");

        Sta.Run(() =>
        {
            var model = new SettingsViewModel(fixture.Services);
            model.NewHost();

            model.HostAlias = "my host";
            Assert.False(model.SaveHost());
            Assert.StartsWith("⚠ The alias must be one word", model.Status);

            model.HostAlias = "PROD";
            Assert.False(model.SaveHost());
            Assert.Equal("⚠ \"PROD\" is already declared in ~/.ssh/config", model.Status);

            model.HostAlias = "ok";
            model.HostPort = "abc";
            Assert.False(model.SaveHost());
            Assert.Equal("⚠ The port must be a number between 1 and 65535", model.Status);

            model.HostPort = "";
            model.HostName = "two words";
            Assert.False(model.SaveHost());
            Assert.Equal("⚠ The HostName field can't contain quotes or spaces", model.Status);

            model.HostName = "";
            model.HostOtherOptions = "Host evil";
            Assert.False(model.SaveHost());
            Assert.Equal("⚠ Not a valid option: Host evil", model.Status);
            Assert.False(File.Exists(fixture.ManagedPath));
        });
    }

    [Fact]
    public void Hosts_of_your_own_files_are_opened_not_edited()
    {
        using var fixture = new PluginFixture();
        fixture.WriteConfig("Host prod\n");

        Sta.Run(() =>
        {
            var model = new SettingsViewModel(fixture.Services);
            model.SelectedHost = model.Hosts.Single();

            Assert.False(model.CanDeleteHost);
            model.EditHost();

            Assert.False(model.IsEditingHost);
            Assert.Equal(fixture.SshConfigPath, Assert.Single(fixture.Started).FileName);
            Assert.Equal("\"prod\" is declared in ~/.ssh/config: the file was opened", model.Status);
        });
    }

    [Fact]
    public void Hosts_can_be_hidden_and_shown_again()
    {
        using var fixture = new PluginFixture();
        fixture.WriteConfig("Host prod\n");

        Sta.Run(() =>
        {
            var model = new SettingsViewModel(fixture.Services);
            model.SelectedHost = model.Hosts.Single();
            Assert.Equal("Hide", model.HideButtonLabel);

            model.ToggleHidden();

            Assert.True(fixture.Data.IsHidden("prod"));
            Assert.Equal("hidden", model.Hosts.Single().State);
            Assert.Equal("Show", model.HideButtonLabel);

            model.ToggleHidden();
            Assert.False(fixture.Data.IsHidden("prod"));
        });
    }

    [Fact]
    public void Commands_can_be_added_renamed_and_deleted()
    {
        using var fixture = new PluginFixture();
        fixture.Api.ShowMsgBox(default!, default!, default, default, default).ReturnsForAnyArgs(MessageBoxResult.Yes);

        Sta.Run(() =>
        {
            var model = new SettingsViewModel(fixture.Services);

            model.NewCommand();
            model.CommandName = "box";
            model.CommandText = "wsl ssh box";
            Assert.True(model.SaveCommand());
            Assert.Equal(new SavedCommand("box", "wsl ssh box"), Assert.Single(fixture.Data.Commands));

            model.SelectedCommand = model.Commands.Single();
            model.EditCommand();
            model.CommandName = "box-01";
            Assert.True(model.SaveCommand());
            Assert.Equal(["box-01"], fixture.Data.Commands.Select(command => command.Name));

            model.SelectedCommand = model.Commands.Single();
            model.DeleteCommand();
            Assert.Empty(fixture.Data.Commands);
        });
    }

    [Fact]
    public void Invalid_commands_are_explained()
    {
        using var fixture = new PluginFixture();
        fixture.Services.Store.Update(data => data.UpsertCommand(new SavedCommand("box", "wsl ssh box")));

        Sta.Run(() =>
        {
            var model = new SettingsViewModel(fixture.Services);

            model.NewCommand();
            model.CommandName = "BOX";
            model.CommandText = "plink other";
            Assert.False(model.SaveCommand());
            Assert.Equal("⚠ \"BOX\" already exists", model.Status);

            model.CommandName = "";
            Assert.False(model.SaveCommand());
            Assert.Equal("⚠ The name is required", model.Status);
            Assert.Single(fixture.Data.Commands);
        });
    }

    [Fact]
    public void Terminals_can_be_created_and_selected()
    {
        using var fixture = new PluginFixture();

        Sta.Run(() =>
        {
            var model = new SettingsViewModel(fixture.Services);

            model.NewTerminal();
            Assert.Equal("{command}", model.TerminalArguments);
            model.TerminalName = "wt";
            model.TerminalExecutable = "wt.exe";
            Assert.False(model.SaveTerminal());
            Assert.Contains("built-in terminal", model.Status);

            model.TerminalName = "mine";
            model.TerminalExecutable = "cmd.exe";
            model.TerminalArguments = "/k {command}";
            Assert.True(model.SaveTerminal());

            model.SelectedTerminal = model.TerminalOptions.Single(option => option.Terminal.Name == "mine");
            Assert.Equal("mine", fixture.Data.SelectedTerminal);

            model.KeepWindowOpenOnError = false;
            model.SuggestKnownHosts = false;
            Assert.False(fixture.Data.KeepWindowOpenOnError);
            Assert.False(fixture.Data.SuggestKnownHosts);
        });
    }

    [Fact]
    public void Changes_made_in_the_search_bar_are_reflected()
    {
        using var fixture = new PluginFixture();

        Sta.Run(() =>
        {
            var model = new SettingsViewModel(fixture.Services);
            model.Attach();

            fixture.Query("add prod root@prod")[0].Run();
            fixture.Query("add box wsl ssh box")[0].Run();

            var row = Assert.Single(model.Hosts);
            Assert.Equal("prod", row.Alias);
            Assert.True(row.IsManaged);
            Assert.Equal("box", Assert.Single(model.Commands).Name);
            model.Detach();
        });
    }

    [Fact]
    public void Old_data_can_be_imported_by_hand()
    {
        using var fixture = new PluginFixture();
        fixture.Temp.Write(Path.Combine("ssh", "profiles.json"), """{ "EntriesLists": { "legacy": "root@10.0.0.8", "other": "wsl ssh x" } }""");

        Sta.Run(() =>
        {
            var model = new SettingsViewModel(fixture.Services);

            model.ImportLegacy();

            Assert.StartsWith("Imported from ~/.ssh/profiles.json: 1 host(s) into ~/.ssh/easyssh.config, 1 command(s), 0 terminal(s).", model.Status);
            Assert.Equal("legacy", Assert.Single(fixture.ManagedHosts).Alias);
            Assert.Equal("legacy", Assert.Single(model.Hosts).Alias);
            Assert.Equal("wsl ssh x", fixture.Data.FindCommand("other")!.Command);
        });
    }

    [Fact]
    public void Missing_old_data_is_explained()
    {
        using var fixture = new PluginFixture();

        Sta.Run(() =>
        {
            var model = new SettingsViewModel(fixture.Services);

            model.ImportLegacy();

            Assert.StartsWith("Nothing to import", model.Status);
        });
    }

    [Fact]
    public void Missing_terminals_are_labelled()
    {
        using var fixture = new PluginFixture();
        fixture.Services.Store.Update(data => data.UpsertTerminal(new TerminalDefinition("ghost", @"C:\nowhere\ghost.exe", "")));

        Sta.Run(() =>
        {
            var model = new SettingsViewModel(fixture.Services);

            Assert.Equal("ghost — Not found on this computer", model.TerminalOptions.Last().Label);
        });
    }
}
