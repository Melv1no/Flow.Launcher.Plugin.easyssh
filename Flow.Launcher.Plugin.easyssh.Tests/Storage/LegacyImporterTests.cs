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
                new SshProfile("prod", "ssh root@10.0.0.1"),
                new SshProfile("dev", "ssh -i \"C:\\Users\\me\\.ssh\\key\" dev@10.0.0.2 -p22222"),
                new SshProfile("prod (2)", "ssh root@10.0.0.3"),
            ],
            legacy.Profiles);
    }

    [Fact]
    public void Reads_version_2_profiles_and_shells()
    {
        var legacy = LegacyImporter.Parse(Version2);

        Assert.Equal(LegacyFormat.Version2, legacy.Format);
        Assert.Equal([new SshProfile("web", "ssh admin@web"), new SshProfile("old-style", "ssh root@old")], legacy.Profiles);
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

        var result = LegacyImporter.Merge(new PluginData(), LegacyImporter.Parse(json));

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

    [Fact]
    public void Merge_keeps_existing_names_and_counts_what_was_added()
    {
        var data = new PluginData().UpsertProfile(new SshProfile("WEB", "ssh mine"));

        var result = LegacyImporter.Merge(data, LegacyImporter.Parse(Version2));

        Assert.Equal(1, result.AddedProfiles);
        Assert.Equal(4, result.AddedTerminals);
        Assert.Equal("ssh mine", result.Data.FindProfile("web")!.Command);
        Assert.Equal("git-bash", result.Data.SelectedTerminal);
    }

    [Fact]
    public void Merge_does_not_override_a_terminal_chosen_in_version_3()
    {
        var data = new PluginData().SelectTerminal("pwsh");

        var result = LegacyImporter.Merge(data, LegacyImporter.Parse(Version2));

        Assert.Equal("pwsh", result.Data.SelectedTerminal);
    }

    [Fact]
    public void Merging_twice_adds_nothing_the_second_time()
    {
        var legacy = LegacyImporter.Parse(Version2);
        var once = LegacyImporter.Merge(new PluginData(), legacy).Data;

        var twice = LegacyImporter.Merge(once, legacy);

        Assert.Equal(0, twice.AddedProfiles);
        Assert.Equal(0, twice.AddedTerminals);
    }
}
