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

        Sta.Run(() =>
        {
            var panel = fixture.Plugin.CreateSettingPanel();
            Assert.IsType<SettingsPanel>(panel);
            panel.Measure(new Size(900, 2000));
            panel.Arrange(new Rect(0, 0, 900, 2000));
        });
    }

    [Fact]
    public void Profiles_can_be_added_renamed_and_deleted()
    {
        using var fixture = new PluginFixture();
        fixture.Api.ShowMsgBox(default!, default!, default, default, default).ReturnsForAnyArgs(MessageBoxResult.Yes);

        Sta.Run(() =>
        {
            var model = new SettingsViewModel(fixture.Services);

            model.NewProfile();
            model.ProfileName = "web";
            model.ProfileCommand = "admin@web";
            Assert.True(model.SaveProfile());
            Assert.Equal(new SshProfile("web", "ssh admin@web"), Assert.Single(fixture.Data.Profiles));

            model.SelectedProfile = model.Profiles.Single();
            model.EditProfile();
            model.ProfileName = "web-01";
            Assert.True(model.SaveProfile());
            Assert.Equal(["web-01"], fixture.Data.Profiles.Select(profile => profile.Name));

            model.SelectedProfile = model.Profiles.Single();
            model.DeleteProfile();
            Assert.Empty(fixture.Data.Profiles);
        });
    }

    [Fact]
    public void Invalid_profiles_are_explained()
    {
        using var fixture = new PluginFixture();
        fixture.Services.Store.Update(data => data.UpsertProfile(new SshProfile("prod", "ssh prod")));

        Sta.Run(() =>
        {
            var model = new SettingsViewModel(fixture.Services);

            model.NewProfile();
            model.ProfileName = "PROD";
            model.ProfileCommand = "ssh other";
            Assert.False(model.SaveProfile());
            Assert.Equal("⚠ \"PROD\" already exists", model.Status);

            model.ProfileName = "";
            Assert.False(model.SaveProfile());
            Assert.Equal("⚠ The name is required", model.Status);
            Assert.Single(fixture.Data.Profiles);
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
            model.ShowSshConfigHosts = false;
            Assert.False(fixture.Data.KeepWindowOpenOnError);
            Assert.False(fixture.Data.ShowSshConfigHosts);
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

            Assert.Equal("prod", Assert.Single(model.Profiles).Name);
            model.Detach();
        });
    }

    [Fact]
    public void Old_data_can_be_imported_by_hand()
    {
        using var fixture = new PluginFixture();
        fixture.Temp.Write(Path.Combine("ssh", "profiles.json"), """{ "EntriesLists": { "legacy": "root@legacy" } }""");

        Sta.Run(() =>
        {
            var model = new SettingsViewModel(fixture.Services);

            model.ImportLegacy();

            Assert.StartsWith("Imported 1 profile(s) and 0 terminal(s)", model.Status);
            Assert.Equal("ssh root@legacy", fixture.Data.FindProfile("legacy")!.Command);
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
