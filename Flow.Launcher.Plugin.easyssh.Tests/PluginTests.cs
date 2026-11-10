using System.Windows;
using Flow.Launcher.Plugin.EasySsh.Core;
using Flow.Launcher.Plugin.EasySsh.Storage;
using NSubstitute;

namespace Flow.Launcher.Plugin.EasySsh.Tests;

public class PluginTests
{
    private static PluginFixture WithProfiles(params (string Name, string Command)[] profiles)
    {
        var fixture = new PluginFixture();
        foreach (var (name, command) in profiles)
        {
            fixture.Services.Store.Update(data => data.UpsertProfile(new SshProfile(name, command)));
        }

        return fixture;
    }

    // ------------------------------------------------------------------ startup

    [Fact]
    public void First_start_without_old_data_shows_the_empty_state()
    {
        using var fixture = new PluginFixture();

        var results = fixture.Query("");

        Assert.Equal("No saved connection yet", results[0].Title);
        Assert.Contains(results, result => result.Title == "Add a profile");
        Assert.NotNull(fixture.Data.LegacyImport);
        fixture.Api.DidNotReceive().ShowMsg(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void First_start_imports_the_profiles_of_version_1_once()
    {
        using var fixture = new PluginFixture(temp => temp.Write(Path.Combine("ssh", "profiles.json"), """
            { "1": { "Id": 1, "Name": "prod", "Command": "root@10.0.0.1" } }
            """));

        Assert.Equal(new SshProfile("prod", "ssh root@10.0.0.1"), Assert.Single(fixture.Data.Profiles));
        Assert.Equal(1, fixture.Data.LegacyImport!.Profiles);
        fixture.Api.Received(1).ShowMsg("EasySSH", Arg.Is<string>(text => text.StartsWith("Imported 1 profile")), Arg.Any<string>());

        // Next start: nothing imported again, even if the profile was removed meanwhile.
        fixture.Services.Store.Update(data => data.RemoveProfile("prod"));
        var restarted = new Main();
        restarted.Initialize(fixture.Api, fixture.DataDirectory, fixture.LegacyPath, fixture.SshConfigPath);
        Assert.Empty(restarted.Services!.Store.Data.Profiles);
        Assert.True(File.Exists(fixture.LegacyPath), "The old file must never be deleted");
    }

    [Fact]
    public async Task A_locked_data_file_is_reported_in_the_results_then_recovered()
    {
        // Issue #3: a failure at startup must never prevent Flow Launcher from loading the plugin.
        using var temp = new TempDirectory();
        var dataDirectory = temp.Combine("data");
        temp.Write(Path.Combine("data", DataStore.FileName), "{}");
        var api = FakeApi.Create();
        var plugin = new Main();

        using (new FileStream(Path.Combine(dataDirectory, DataStore.FileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            plugin.Initialize(api, dataDirectory, temp.Combine("profiles.json"), temp.Combine("config"));
            var result = Assert.Single(plugin.Answer("anything", "ssh"));
            Assert.Equal("EasySSH could not start", result.Title);
        }

        await plugin.ReloadDataAsync();

        Assert.NotNull(plugin.Services);
        plugin.Services.OpenSshInstalled = true;
        Assert.Equal("No saved connection yet", plugin.Answer("", "ssh").OrderByDescending(result => result.Score).First().Title);
    }

    [Fact]
    public void Missing_ssh_folder_is_not_a_problem()
    {
        // Issue #3 again: EasySSH 1.x crashed when %USERPROFILE%\.ssh did not exist.
        using var fixture = new PluginFixture();

        Assert.False(Directory.Exists(Path.GetDirectoryName(fixture.LegacyPath)));
        Assert.NotNull(fixture.Plugin.Services);
    }

    // ------------------------------------------------------------------ search

    [Fact]
    public void Search_ranks_prefixes_first_then_alphabetically()
    {
        using var fixture = WithProfiles(("preprod-web", "ssh web@preprod"), ("web-02", "ssh web2"), ("web-01", "ssh web1"));

        var titles = fixture.Query("web").Select(result => result.Title).ToList();

        Assert.Equal(["web-01", "web-02", "preprod-web"], titles.Take(3));
        Assert.Equal("Connect to web", titles[^1]);
    }

    [Fact]
    public void Search_tolerates_accents_and_typos()
    {
        using var fixture = WithProfiles(("Préprod", "ssh admin@10.0.0.9"));

        Assert.Equal("Préprod", fixture.Query("preprod")[0].Title);
        Assert.Equal("Préprod", fixture.Query("prepord")[0].Title);
    }

    [Fact]
    public void Profile_results_are_complete()
    {
        using var fixture = WithProfiles(("prod", "ssh root@prod"));

        var result = fixture.Query("pro")[0];

        Assert.Equal("prod", result.Title);
        Assert.Equal("ssh root@prod", result.SubTitle);
        Assert.Equal("ssh root@prod", result.CopyText);
        Assert.Equal("ssh prod", result.AutoCompleteText);
        Assert.Equal("profile:prod", result.RecordKey);
        Assert.Equal([0, 1, 2], result.TitleHighlightData);
        Assert.True(result.AddSelectedCount);
    }

    [Fact]
    public void Unknown_text_offers_a_direct_connection()
    {
        using var fixture = new PluginFixture();

        var result = fixture.Query("admin@10.0.0.7 -p 2222").Last();

        Assert.Equal("Connect to 10.0.0.7", result.Title);
        Assert.Contains("ssh admin@10.0.0.7 -p 2222", result.SubTitle);
    }

    [Fact]
    public void Typing_the_start_of_a_command_suggests_it()
    {
        using var fixture = new PluginFixture();

        var suggestion = Assert.Single(fixture.Query("term"), result => result.AutoCompleteText == "ssh terminal ");

        Assert.Equal("Choose the terminal", suggestion.Title);
        Assert.False(suggestion.Run());
        fixture.Api.Received().ChangeQuery("ssh terminal ", false);
    }

    [Fact]
    public void Global_action_keyword_has_no_prefix()
    {
        using var fixture = WithProfiles(("prod", "ssh root@prod"));

        Assert.Equal("prod", fixture.Query("prod", actionKeyword: "")[0].AutoCompleteText);
    }

    [Fact]
    public void Ssh_config_hosts_are_listed_unless_already_saved()
    {
        using var fixture = new PluginFixture(temp => temp.Write(Path.Combine("ssh", "config"), """
            Host bastion
              HostName 192.0.2.10
              User ops
            Host saved
            """));
        fixture.Services.Store.Update(data => data.UpsertProfile(new SshProfile("my saved", "ssh saved")));

        var results = fixture.Query("");

        var host = Assert.Single(results, result => result.Title == "bastion");
        Assert.Equal("From ~/.ssh/config · ops@192.0.2.10", host.SubTitle);
        Assert.DoesNotContain(results, result => result.Title == "saved");
        Assert.Contains(results, result => result.Title == "my saved");
        Assert.Equal("bastion", fixture.Query("192.0.2")[0].Title);
    }

    [Fact]
    public void Ssh_config_hosts_can_be_hidden()
    {
        using var fixture = new PluginFixture(temp => temp.Write(Path.Combine("ssh", "config"), "Host bastion"));
        fixture.Services.Store.Update(data => data with { ShowSshConfigHosts = false });

        Assert.DoesNotContain(fixture.Query(""), result => result.Title == "bastion");
        Assert.Equal("Hosts from ~/.ssh/config are hidden", fixture.Query("config")[0].Title);
    }

    // ------------------------------------------------------------------ direct connection (issue #9)

    [Fact]
    public void Direct_connection_adds_ssh_when_only_the_destination_is_given()
    {
        using var fixture = new PluginFixture();

        var result = fixture.Query("d root@127.0.0.1")[0];

        Assert.Equal("Connect to 127.0.0.1", result.Title);
        Assert.Equal("One-time connection: ssh root@127.0.0.1 · in Command Prompt", result.SubTitle);
        Assert.False(result.AddSelectedCount);
    }

    [Fact]
    public void Direct_connection_keeps_full_commands()
    {
        using var fixture = new PluginFixture();

        Assert.Contains("ssh -p 22 root@host", fixture.Query("d ssh -p 22 root@host")[0].SubTitle);
    }

    [Fact]
    public void Direct_without_argument_explains_the_syntax_and_still_searches()
    {
        using var fixture = WithProfiles(("dev", "ssh dev"));

        var results = fixture.Query("d");

        Assert.Equal("Connect without saving", results[0].Title);
        Assert.Contains(results, result => result.Title == "dev");
    }

    // ------------------------------------------------------------------ add / edit / remove

    [Fact]
    public void Add_saves_a_normalized_profile_and_shows_it()
    {
        using var fixture = new PluginFixture();

        var result = Assert.Single(fixture.Query("add prod root@10.0.0.1 -p 2222"));
        Assert.Equal("Add profile \"prod\"", result.Title);
        Assert.Equal("ssh root@10.0.0.1 -p 2222", result.SubTitle);

        Assert.False(result.Run());

        Assert.Equal(new SshProfile("prod", "ssh root@10.0.0.1 -p 2222"), Assert.Single(fixture.Data.Profiles));
        fixture.Api.Received().ChangeQuery("ssh prod", true);
    }

    [Fact]
    public void Add_accepts_quoted_names_and_keeps_the_command_quoting()
    {
        using var fixture = new PluginFixture();

        fixture.Query("add \"My Server\" ssh -i \"C:\\my keys\\id\" admin@host")[0].Run();

        Assert.Equal(new SshProfile("My Server", "ssh -i \"C:\\my keys\\id\" admin@host"), Assert.Single(fixture.Data.Profiles));
    }

    [Fact]
    public void Add_updates_an_existing_profile()
    {
        using var fixture = WithProfiles(("prod", "ssh old@prod"));

        var result = fixture.Query("add PROD new@prod")[0];

        Assert.Equal("Update profile \"PROD\"", result.Title);
        Assert.Equal("Current command: ssh old@prod", result.SubTitleToolTip);
        result.Run();
        Assert.Equal(new SshProfile("PROD", "ssh new@prod"), Assert.Single(fixture.Data.Profiles));
    }

    [Fact]
    public void Add_without_command_asks_for_it()
    {
        using var fixture = new PluginFixture();

        var result = Assert.Single(fixture.Query("add prod"));

        Assert.Equal("Now type the host (user@host) or the full ssh command", result.SubTitle);
        Assert.Null(result.Action);
    }

    [Fact]
    public void Add_alone_shows_the_usage_and_matching_profiles()
    {
        using var fixture = WithProfiles(("addons", "ssh addons"));

        var results = fixture.Query("add");

        Assert.Equal("Add a profile", results[0].Title);
        Assert.Contains(results, result => result.Title == "addons");
    }

    [Fact]
    public void Edit_puts_the_profile_back_in_the_search_bar()
    {
        using var fixture = WithProfiles(("My Server", "ssh -p 22 admin@host"));

        Assert.False(fixture.Query("edit my")[0].Run());

        fixture.Api.Received().ChangeQuery("ssh add \"My Server\" ssh -p 22 admin@host", false);
    }

    [Fact]
    public void Remove_asks_for_confirmation()
    {
        using var fixture = WithProfiles(("prod", "ssh prod"), ("dev", "ssh dev"));
        fixture.Api.ShowMsgBox(default!, default!, default, default, default).ReturnsForAnyArgs(MessageBoxResult.No, MessageBoxResult.Yes);

        var result = fixture.Query("remove prod")[0];
        result.Run();
        Assert.Equal(2, fixture.Data.Profiles.Count);

        result.Run();
        Assert.Equal(["dev"], fixture.Data.Profiles.Select(profile => profile.Name));
        fixture.Api.Received().ReQuery();
    }

    // ------------------------------------------------------------------ terminals (issue #2)

    [Fact]
    public void Terminal_lists_presets_with_the_selected_one_first()
    {
        using var fixture = new PluginFixture();

        var results = fixture.Query("terminal");

        Assert.Equal("Command Prompt", results[0].Title);
        Assert.StartsWith("Selected · ", results[0].SubTitle);
        Assert.Contains(results, result => result.Title == "Windows PowerShell");
        Assert.Contains(results, result => result.Title == "Add a custom terminal");
    }

    [Fact]
    public void Selecting_a_terminal_from_the_search_bar()
    {
        using var fixture = new PluginFixture();

        fixture.Query("terminal").Single(result => result.Title == "Windows PowerShell").Run();

        Assert.Equal("powershell", fixture.Data.SelectedTerminal);
        Assert.Equal("Windows PowerShell", fixture.Query("terminal")[0].Title);
    }

    [Fact]
    public void A_missing_terminal_is_not_selected()
    {
        using var fixture = new PluginFixture();
        fixture.Services.Store.Update(data => data.UpsertTerminal(new TerminalDefinition("ghost", @"C:\nowhere\ghost.exe", "")));

        var result = fixture.Query("terminal ghost")[0];
        Assert.Equal("Not found on this computer", result.SubTitle);
        result.Run();

        Assert.Equal("cmd", fixture.Data.SelectedTerminal);
        fixture.Api.Received().ShowMsgError("EasySSH could not open the terminal", Arg.Any<string>());
    }

    [Fact]
    public void Terminal_add_with_name_program_and_arguments_selects_it()
    {
        using var fixture = new PluginFixture();

        var result = fixture.Query("terminal add git-bash cmd.exe /k {command}")[0];
        Assert.Equal("Add terminal \"git-bash\"", result.Title);
        result.Run();

        Assert.Equal(new TerminalDefinition("git-bash", "cmd.exe", "/k {command}"), Assert.Single(fixture.Data.Terminals));
        Assert.Equal("git-bash", fixture.Data.SelectedTerminal);
    }

    [Fact]
    public void Shell_add_with_only_a_program_still_works()
    {
        using var fixture = new PluginFixture();

        var result = fixture.Query(@"shell add C:\Tools\kitty.exe")[0];
        result.Run();

        Assert.Equal(new TerminalDefinition("kitty.exe", @"C:\Tools\kitty.exe", ""), Assert.Single(fixture.Data.Terminals));
        Assert.StartsWith("Not found on this computer", result.SubTitle);
    }

    [Fact]
    public void Terminal_add_refuses_reserved_names()
    {
        using var fixture = new PluginFixture();

        var result = Assert.Single(fixture.Query("terminal add wt cmd.exe"));

        Assert.Null(result.Action);
        Assert.Contains("built-in terminal", result.SubTitle);
    }

    [Fact]
    public void Terminal_remove_falls_back_to_the_default()
    {
        using var fixture = new PluginFixture();
        fixture.Query("terminal add mine cmd.exe")[0].Run();

        fixture.Query("terminal remove")[0].Run();

        Assert.Empty(fixture.Data.Terminals);
        Assert.Equal("cmd", fixture.Data.SelectedTerminal);
    }

    // ------------------------------------------------------------------ help & context menus

    [Fact]
    public void Help_lists_every_command()
    {
        using var fixture = new PluginFixture();

        var completions = fixture.Query("help").Select(result => result.AutoCompleteText).ToList();

        Assert.Equal(
            ["ssh add ", "ssh edit ", "ssh remove ", "ssh profiles ", "ssh d ", "ssh terminal ", "ssh config "],
            completions.Take(7));
    }

    [Fact]
    public void Profile_context_menu_offers_copy_edit_remove_and_other_terminals()
    {
        using var fixture = WithProfiles(("prod", "ssh prod"));
        var profile = fixture.Query("prod")[0];

        var titles = fixture.Plugin.LoadContextMenus(profile).Select(result => result.Title).ToList();

        Assert.Equal(["Copy the command", "Edit", "Remove"], titles.Take(3));
        Assert.Contains("Open in Windows PowerShell", titles);
        Assert.DoesNotContain("Open in Command Prompt", titles);
    }

    [Fact]
    public void Copy_from_the_context_menu()
    {
        using var fixture = WithProfiles(("prod", "ssh root@prod"));

        fixture.Plugin.LoadContextMenus(fixture.Query("prod")[0])[0].Run();

        fixture.Api.Received().CopyToClipboard("ssh root@prod", false, true);
    }

    [Fact]
    public void Direct_connection_can_be_saved_as_a_profile()
    {
        using var fixture = new PluginFixture();
        var direct = fixture.Query("d admin@192.0.2.4")[0];

        var save = fixture.Plugin.LoadContextMenus(direct).Single(result => result.Title == "Save as a profile");
        save.Run();

        fixture.Api.Received().ChangeQuery("ssh add 192.0.2.4 ssh admin@192.0.2.4", false);
    }

    [Fact]
    public void Missing_openssh_is_reported_once_on_the_home_list()
    {
        using var fixture = new PluginFixture();
        fixture.Services.OpenSshInstalled = false;

        Assert.Equal("OpenSSH client not found", fixture.Query("")[0].Title);
        Assert.DoesNotContain(fixture.Query("abc"), result => result.Title == "OpenSSH client not found");
        Assert.Contains(fixture.Query("d host"), result => result.Title == "OpenSSH client not found");
    }
}
