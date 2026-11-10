using System.Text.Json;
using Flow.Launcher.Plugin.EasySsh.Core;
using Flow.Launcher.Plugin.EasySsh.Storage;

namespace Flow.Launcher.Plugin.EasySsh.Tests.Storage;

public class LegacyImporterTests
{
    /// <summary>Format written by EasySSH 1.1.x: commands are ssh arguments.</summary>
    private const string Version1 = """
        {
          "2": { "Id": 2, "Name": "dev", "Command": "ssh -i 'C:\\Users\\me\\.ssh\\key' dev@10.0.0.2 -p22222" },
          "1": { "Id": 1, "Name": "prod", "Command": "root@10.0.0.1" },
          "3": { "Id": 3, "Name": "prod", "Command": "root@10.0.0.3" }
        }
        """;

    /// <summary>Format written by EasySSH 1.2.x.</summary>
    private const string Version2 = """
        {
          "PluginVersion": "1.0",
          "EntriesLists": { "web": "ssh admin@web", "old-style": "root@old" },
          "CustomShellLists": {
            "wt.exe": "",
            "git-bash": "wt.exe -p \"Git Bash\"",
            "C:\\Program Files\\PowerShell\\7\\pwsh.exe": "",
            "cmd": "cmd.exe /k"
          },
          "SelectedCustomShell": "git-bash"
        }
        """;

    /// <summary>
    /// Shells written by the 1.2 development builds: the key is the program and <c>{ssh}</c> the command.
    /// The first two were added by the builds themselves; "cmd" comes from 1.2.0.1.
    /// </summary>
    private const string DevelopmentBuild = """
        {
          "PluginVersion": "1.0",
          "EntriesLists": { "web": "ssh admin@web" },
          "CustomShellLists": {
            "cmd.exe": "/k \"{ssh}\"",
            "powershell.exe": "-NoExit -Command \"{ssh}\"",
            "kitty.exe": "-e {ssh}",
            "C:\\Tools\\alacritty.exe": "-e",
            "cmd": "cmd.exe /k"
          },
          "SelectedCustomShell": "cmd.exe"
        }
        """;

    [Fact]
    public void Reads_version_1_and_turns_arguments_into_commands()
    {
        var legacy = LegacyImporter.Parse(Version1);

        Assert.Equal(LegacyFormat.Version1, legacy.Format);
        Assert.Equal(
            [
                new LegacyProfile("prod", "ssh root@10.0.0.1"),
                new LegacyProfile("dev", "ssh -i \"C:\\Users\\me\\.ssh\\key\" dev@10.0.0.2 -p22222"),
                new LegacyProfile("prod (2)", "ssh root@10.0.0.3"),
            ],
            legacy.Profiles);
    }

    [Fact]
    public void Reads_version_2_profiles_and_shells()
    {
        var legacy = LegacyImporter.Parse(Version2);

        Assert.Equal(LegacyFormat.Version2, legacy.Format);
        Assert.Equal([new LegacyProfile("web", "ssh admin@web"), new LegacyProfile("old-style", "ssh root@old")], legacy.Profiles);
        Assert.Equal(
            [
                new TerminalDefinition("wt.exe", "wt.exe", ""),
                new TerminalDefinition("git-bash", "wt.exe", "-p \"Git Bash\""),
                new TerminalDefinition("pwsh.exe", @"C:\Program Files\PowerShell\7\pwsh.exe", ""),
                new TerminalDefinition("cmd (custom)", "cmd.exe", "/k"),
            ],
            legacy.Terminals);
        Assert.Equal("git-bash", legacy.SelectedTerminal);
    }

    [Fact]
    public void Imported_shells_behave_like_in_version_1_2()
    {
        var legacy = LegacyImporter.Parse(Version2);
        var gitBash = legacy.Terminals.Single(terminal => terminal.Name == "git-bash");

        var plan = LaunchPlanBuilder.Build(gitBash, "ssh admin@web", "web", keepOpenOnError: true, "x");

        Assert.Equal(new LaunchPlan("wt.exe", "-p \"Git Bash\" ssh admin@web"), plan);
    }

    [Fact]
    public void Reads_the_shells_of_the_development_builds()
    {
        var legacy = LegacyImporter.Parse(DevelopmentBuild);

        Assert.Equal(
            [
                new TerminalDefinition("kitty.exe", "kitty.exe", "-e {command}"),
                new TerminalDefinition("alacritty.exe", @"C:\Tools\alacritty.exe", "-e"),
                new TerminalDefinition("cmd (custom)", "cmd.exe", "/k"),
            ],
            legacy.Terminals);
    }

    [Theory]
    [InlineData("cmd.exe", "cmd")]
    [InlineData("powershell.exe", "powershell")]
    [InlineData("kitty.exe", "kitty.exe")]
    public void Shells_added_by_the_development_builds_are_replaced_by_presets(string selected, string expected)
    {
        var json = DevelopmentBuild.Replace("\"SelectedCustomShell\": \"cmd.exe\"", $"\"SelectedCustomShell\": \"{selected}\"");

        var result = Import(new PluginData(), LegacyImporter.Parse(json));

        Assert.Equal(expected, result.Data.SelectedTerminal);
        Assert.Equal(3, result.AddedTerminals);
    }

    [Fact]
    public void Shells_of_the_development_builds_behave_as_before()
    {
        var kitty = LegacyImporter.Parse(DevelopmentBuild).Terminals.Single(terminal => terminal.Name == "kitty.exe");

        var plan = LaunchPlanBuilder.Build(kitty, "ssh admin@web", "web", keepOpenOnError: true, "x");

        Assert.Equal(new LaunchPlan("kitty.exe", "-e ssh admin@web"), plan);
    }

    [Fact]
    public void A_changed_default_shell_of_the_development_builds_is_imported()
    {
        var legacy = LegacyImporter.Parse("""{ "CustomShellLists": { "cmd.exe": "/k \"{ssh}\" & pause" } }""");

        Assert.Equal([new TerminalDefinition("cmd.exe", "cmd.exe", "/k \"{command}\" & pause")], legacy.Terminals);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "something": "else" }""")]
    [InlineData("[]")]
    public void Unknown_content_imports_nothing(string json)
    {
        var legacy = LegacyImporter.Parse(json);

        Assert.Equal(LegacyFormat.Unknown, legacy.Format);
        Assert.Empty(legacy.Profiles);
    }

    [Fact]
    public void Invalid_json_is_reported()
    {
        Assert.ThrowsAny<JsonException>(() => LegacyImporter.Parse("{ nope"));
    }

    [Fact]
    public void Missing_file_returns_null()
    {
        Assert.Null(LegacyImporter.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "profiles.json")));
    }

    private static LegacyImportResult Import(PluginData data, LegacyData legacy, ManagedConfig? managed = null, string? existingConfig = null)
    {
        using var temp = new TempDirectory();
        var config = temp.Write("config", existingConfig ?? string.Empty);
        return LegacyImporter.Import(data, managed ?? ManagedConfig.CreateEmpty(), SshConfigParser.Load(config), legacy);
    }

    [Fact]
    public void Profiles_become_hosts_and_terminals_are_added()
    {
        var managed = ManagedConfig.CreateEmpty();

        var result = Import(new PluginData(), LegacyImporter.Parse(Version2), managed);

        Assert.Equal(["old-style"], result.AddedHosts);
        Assert.Equal([new SshOption("HostName", "old"), new SshOption("User", "root")], managed.Find("old-style")!.Options);
        // "Host web" with a user would change every connection to web, git's and scp's included.
        Assert.Equal([new SavedCommand("web", "ssh admin@web")], result.Data.Commands);
        Assert.Equal(4, result.AddedTerminals);
        Assert.Equal("git-bash", result.Data.SelectedTerminal);
    }

    [Fact]
    public void Existing_hosts_are_never_shadowed()
    {
        var managed = ManagedConfig.CreateEmpty();
        var legacy = new LegacyData(LegacyFormat.Version2, [new LegacyProfile("web", "ssh admin@10.0.0.5")], [], null);

        var result = Import(new PluginData(), legacy, managed, "Host web\n  HostName 10.9.9.9\n");

        Assert.Equal(["web-2"], result.AddedHosts);
        Assert.Equal("10.0.0.5", managed.Find("web-2")!.HostName);
    }

    [Fact]
    public void A_profile_that_only_opens_an_existing_host_is_skipped()
    {
        var managed = ManagedConfig.CreateEmpty();
        var legacy = new LegacyData(
            LegacyFormat.Version2,
            [new LegacyProfile("Bastion SI", "ssh bastion"), new LegacyProfile("bastion 2222", "ssh bastion -p 2222")],
            [],
            null);

        var result = Import(new PluginData(), legacy, managed, "Host bastion\n  HostName 10.0.0.1\n");

        Assert.Empty(result.AddedHosts);
        // "HostName bastion" would not use the options of bastion: this one stays a command.
        Assert.Equal([new SavedCommand("bastion 2222", "ssh bastion -p 2222")], result.Data.Commands);
    }

    [Fact]
    public void What_ssh_config_cannot_express_becomes_a_command()
    {
        var legacy = new LegacyData(
            LegacyFormat.Version2,
            [new LegacyProfile("wsl box", "wsl ssh box"), new LegacyProfile("tunnel", "ssh -N -L 8080:localhost:80 box"), new LegacyProfile("Prod Server", "root@prod")],
            [],
            null);

        var result = Import(new PluginData(), legacy);

        Assert.Equal(["wsl box", "tunnel"], result.Data.Commands.Select(command => command.Name));
        Assert.Equal(["Prod-Server"], result.AddedHosts);
    }

    [Fact]
    public void Import_does_not_override_a_terminal_chosen_in_version_3()
    {
        var result = Import(new PluginData().SelectTerminal("pwsh"), LegacyImporter.Parse(Version2));

        Assert.Equal("pwsh", result.Data.SelectedTerminal);
    }

    [Fact]
    public void Importing_twice_adds_nothing_the_second_time()
    {
        var legacy = LegacyImporter.Parse(Version2);
        var managed = ManagedConfig.CreateEmpty();
        var once = Import(new PluginData(), legacy, managed).Data;

        var twice = Import(once, legacy, managed);

        Assert.False(twice.AddedAnything);
        Assert.Single(managed.Hosts);
        Assert.Single(twice.Data.Commands);
    }

    [Fact]
    public void Importing_again_once_the_hosts_are_visible_to_ssh_adds_nothing()
    {
        // The settings button imports again while ~/.ssh/config already includes the hosts imported first.
        var legacy = new LegacyData(LegacyFormat.Version2, [new LegacyProfile("web", "ssh admin@10.0.0.5")], [], null);
        var managed = ManagedConfig.CreateEmpty();
        var once = Import(new PluginData(), legacy, managed).Data;

        var twice = Import(once, legacy, managed, string.Join("\n", ManagedConfig.Render(managed.Find("web")!)));

        Assert.False(twice.AddedAnything);
        Assert.Empty(twice.Data.Commands);
    }

    [Fact]
    public void Host_patterns_of_the_configuration_keep_applying()
    {
        // ssh matches "Host *.corp" against the name typed: "Host web / HostName web.corp" would lose the jump host.
        var legacy = new LegacyData(LegacyFormat.Version2, [new LegacyProfile("web", "ssh web.corp")], [], null);

        var result = Import(new PluginData(), legacy, existingConfig: "Host *.corp\n  ProxyJump bastion\n");

        Assert.Empty(result.AddedHosts);
        Assert.Equal([new SavedCommand("web", "ssh web.corp")], result.Data.Commands);
    }

    [Fact]
    public void A_profile_named_after_its_server_does_not_reconfigure_that_server()
    {
        var legacy = new LegacyData(
            LegacyFormat.Version2,
            [new LegacyProfile("github.com", "ssh -i ~/.ssh/work git@github.com"), new LegacyProfile("box", "ssh box")],
            [],
            null);
        var managed = ManagedConfig.CreateEmpty();

        var result = Import(new PluginData(), legacy, managed);

        Assert.Equal(["github.com"], result.Data.Commands.Select(command => command.Name));
        // Without option, "Host box" changes nothing for the other programs.
        Assert.Equal(["box"], result.AddedHosts);
        Assert.Empty(managed.Find("box")!.Options);
    }

    [Fact]
    public void A_destination_that_is_another_profile_stays_a_command_whatever_the_order()
    {
        var legacy = new LegacyData(
            LegacyFormat.Version2,
            [new LegacyProfile("db2", "ssh -p 2222 db"), new LegacyProfile("db", "ssh root@10.0.0.5"), new LegacyProfile("db3", "ssh -p 2223 db")],
            [],
            null);

        var result = Import(new PluginData(), legacy);

        Assert.Equal(["db"], result.AddedHosts);
        Assert.Equal(["db2", "db3"], result.Data.Commands.Select(command => command.Name));
    }

    [Fact]
    public void A_host_refused_by_ssh_stays_a_command_and_the_others_are_imported()
    {
        var legacy = new LegacyData(
            LegacyFormat.Version2,
            [new LegacyProfile("bad", "ssh -o NoSuchKeyword=x admin@10.0.0.1"), new LegacyProfile("good", "ssh admin@10.0.0.2"), new LegacyProfile("empty", "ssh admin@10.0.0.3 \"\"")],
            [],
            null);
        using var temp = new TempDirectory();
        var managed = ManagedConfig.CreateEmpty();

        var result = LegacyImporter.Import(
            new PluginData(), managed, SshConfigParser.Load(temp.Combine("config")), legacy,
            host => host.Options.All(option => option.Keyword != "NoSuchKeyword"));

        Assert.Equal(["good", "empty"], result.AddedHosts);
        Assert.Equal(["bad"], result.Data.Commands.Select(command => command.Name));
    }

    [Fact]
    public void Version_1_arguments_always_run_with_ssh()
    {
        var legacy = LegacyImporter.Parse("""{ "1": { "Id": 1, "Name": "site", "Command": "example.com -p 2222" } }""");

        Assert.Equal([new LegacyProfile("site", "ssh example.com -p 2222")], legacy.Profiles);
    }
}
