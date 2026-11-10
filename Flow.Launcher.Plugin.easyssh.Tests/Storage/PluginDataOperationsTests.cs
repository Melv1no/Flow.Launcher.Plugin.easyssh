using Flow.Launcher.Plugin.EasySsh.Core;
using Flow.Launcher.Plugin.EasySsh.Storage;

namespace Flow.Launcher.Plugin.EasySsh.Tests.Storage;

public class PluginDataOperationsTests
{
    private static readonly PluginData TwoProfiles = new PluginData()
        .UpsertProfile(new SshProfile("prod", "ssh root@prod"))
        .UpsertProfile(new SshProfile("dev", "ssh root@dev"));

    [Fact]
    public void Upsert_adds_then_replaces_by_name_ignoring_case()
    {
        var data = TwoProfiles.UpsertProfile(new SshProfile("PROD", "ssh admin@prod"));

        Assert.Equal([new SshProfile("PROD", "ssh admin@prod"), new SshProfile("dev", "ssh root@dev")], data.Profiles);
    }

    [Fact]
    public void Upsert_renames_in_place()
    {
        var data = TwoProfiles.UpsertProfile(new SshProfile("production", "ssh root@prod"), previousName: "prod");

        Assert.Equal(["production", "dev"], data.Profiles.Select(profile => profile.Name));
    }

    [Fact]
    public void Upsert_refuses_to_rename_onto_another_profile()
    {
        Assert.Throws<ArgumentException>(() => TwoProfiles.UpsertProfile(new SshProfile("dev", "x"), previousName: "prod"));
    }

    [Theory]
    [InlineData(" ", "ssh x")]
    [InlineData("name", " ")]
    public void Upsert_refuses_blank_values(string name, string command)
    {
        Assert.Throws<ArgumentException>(() => new PluginData().UpsertProfile(new SshProfile(name, command)));
    }

    [Fact]
    public void Upsert_trims_values()
    {
        var data = new PluginData().UpsertProfile(new SshProfile("  web ", " ssh web "));

        Assert.Equal(new SshProfile("web", "ssh web"), Assert.Single(data.Profiles));
        Assert.NotNull(data.FindProfile(" WEB "));
    }

    [Fact]
    public void Remove_returns_the_same_instance_when_nothing_changes()
    {
        Assert.Same(TwoProfiles, TwoProfiles.RemoveProfile("unknown"));
        Assert.Equal(["dev"], TwoProfiles.RemoveProfile("Prod").Profiles.Select(profile => profile.Name));
    }

    [Fact]
    public void Terminals_list_presets_before_custom_ones()
    {
        var data = new PluginData().UpsertTerminal(new TerminalDefinition("kitty", "kitty.exe", "-e {command}"));

        Assert.Equal(["cmd", "wt", "powershell", "pwsh", "kitty"], data.AllTerminals().Select(terminal => terminal.Name));
    }

    [Fact]
    public void Terminal_names_of_presets_are_reserved()
    {
        Assert.Throws<ArgumentException>(() => new PluginData().UpsertTerminal(new TerminalDefinition("WT", "x.exe", "")));
    }

    [Fact]
    public void Renaming_the_selected_terminal_keeps_it_selected()
    {
        var data = new PluginData()
            .UpsertTerminal(new TerminalDefinition("kitty", "kitty.exe", ""))
            .SelectTerminal("kitty")
            .UpsertTerminal(new TerminalDefinition("kitty2", "kitty.exe", ""), previousName: "kitty");

        Assert.Equal("kitty2", data.SelectedTerminal);
    }

    [Fact]
    public void Removing_the_selected_terminal_selects_the_default()
    {
        var data = new PluginData()
            .UpsertTerminal(new TerminalDefinition("kitty", "kitty.exe", ""))
            .SelectTerminal("kitty")
            .RemoveTerminal("KITTY");

        Assert.Equal(BuiltInTerminals.Default.Name, data.SelectedTerminal);
        Assert.Empty(data.Terminals);
    }

    [Fact]
    public void Selecting_uses_the_canonical_name_and_rejects_unknown_terminals()
    {
        Assert.Equal("pwsh", new PluginData().SelectTerminal("PWSH").SelectedTerminal);
        Assert.Throws<ArgumentException>(() => new PluginData().SelectTerminal("nope"));
    }

    [Fact]
    public void Unknown_selection_falls_back_to_the_default_terminal()
    {
        var data = new PluginData { SelectedTerminal = "removed-by-hand" };

        Assert.Same(BuiltInTerminals.Default, data.GetSelectedTerminal());
    }

    [Fact]
    public void Sanitize_repairs_a_file_edited_by_hand()
    {
        var data = new PluginData
        {
            Profiles = [new SshProfile(" a ", " ssh a "), new SshProfile("A", "ssh other"), new SshProfile("", "ssh x"), new SshProfile("b", " ")],
            Terminals = [new TerminalDefinition("cmd", "cmd.exe", ""), new TerminalDefinition("t", "t.exe", null!), new TerminalDefinition("T", "u.exe", "")],
            SelectedTerminal = "missing",
        };

        var sanitized = data.Sanitize();

        Assert.Equal([new SshProfile("a", "ssh a")], sanitized.Profiles);
        Assert.Equal([new TerminalDefinition("t", "t.exe", "")], sanitized.Terminals);
        Assert.Equal(BuiltInTerminals.Default.Name, sanitized.SelectedTerminal);
    }
}
