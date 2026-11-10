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
